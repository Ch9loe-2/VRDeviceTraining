#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRDeviceTraining.EditorTools
{
    /// <summary>
    /// 修复 TrainingScene 的两个典型配置问题：
    ///
    /// 1. 场景里有 2 个 Camera（2 个 AudioListener）：
    ///    创建 XR Origin 后，Unity 默认留下的旧 Main Camera 没删，
    ///    导致每帧刷 "There are 2 audio listeners in the scene"。
    ///    本工具删除不在 XR Origin 层级下的相机。
    ///
    /// 2. XRRayInteractor 的 attachTransform 为 null：
    ///    抓取时物体会被瞬移到「控制器自身的原点」，
    ///    那里离相机只有几厘米，会被近裁剪面裁掉 —— 表现为"按 G 后物体直接消失"。
    ///    本工具给每个 XRRayInteractor 建一个前方 0.25m 的 Attach Transform 子对象。
    ///
    /// 用法：Unity 菜单栏 → VRDeviceTraining → 修复场景相机与抓取挂点
    /// </summary>
    public static class XRTrainingSceneFixer
    {
        // 抓取时物体停在控制器前方的距离（米）。
        // 0.25 太近：物体贴脸、糊满屏幕。
        // 1.2 是实测较优值：物体基本停在抓取瞬间的原位，不贴脸也不被推远，跟随明显。
        // 想更贴近手就调小（1.0），想更远就调大（1.5）。
        const float AttachDistance = 1.2f;
        const float RecommendedNearClip = 0.01f; // XR 相机推荐近裁剪面，避免贴脸物体被裁掉

        [MenuItem("VRDeviceTraining/修复场景相机与抓取挂点", false, 10)]
        public static void FixScene()
        {
            var xrOrigin = FindXROriginRoot();
            if (xrOrigin == null)
            {
                Debug.LogError("[场景修复] 没找到名字含 \"XR Origin\" 的对象，无法判断该删哪个相机。已中止。");
                return;
            }

            int removedCameras = RemoveStrayCameras(xrOrigin);
            int fixedAttach = SetupAttachTransforms();
            int fixedClip = FixNearClipPlane(xrOrigin);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Debug.Log($"[场景修复] 完成！删除多余相机 {removedCameras} 个，" +
                      $"配置抓取挂点 {fixedAttach} 个，调整近裁剪面 {fixedClip} 个。场景已保存。\n" +
                      $"请重新进入 Play 模式测试：Y → 瞄准 → 按住 G（物体应停在手前方，不会消失）。");
        }

        /// <summary>找到名字含 "XR Origin" 的根对象（不依赖具体类型，避免 API 差异）。</summary>
        static Transform FindXROriginRoot()
        {
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name.Contains("XR Origin"))
                    return t;
            }
            return null;
        }

        /// <summary>删除不在 XR Origin 层级下的相机（解决 2 个 AudioListener 刷屏）。</summary>
        static int RemoveStrayCameras(Transform xrOrigin)
        {
            int count = 0;
            foreach (var camera in Object.FindObjectsOfType<Camera>(true))
            {
                if (camera.transform.IsChildOf(xrOrigin) || camera.transform == xrOrigin)
                    continue;

                Debug.Log($"[场景修复] 删除多余相机：{camera.gameObject.name}（不属于 XR Origin）");
                Undo.DestroyObjectImmediate(camera.gameObject);
                count++;
            }
            return count;
        }

        /// <summary>给每个 XRRayInteractor 配置 Attach Transform，避免抓取时物体贴脸消失。</summary>
        static int SetupAttachTransforms()
        {
            int count = 0;
            foreach (var interactor in Object.FindObjectsOfType<XRRayInteractor>(true))
            {
                Transform attach = interactor.transform.Find("Attach Transform");
                if (attach == null)
                {
                    var go = new GameObject("Attach Transform");
                    Undo.RegisterCreatedObjectUndo(go, "Create Attach Transform");
                    go.transform.SetParent(interactor.transform, false);
                    go.transform.localPosition = new Vector3(0f, 0f, AttachDistance);
                    go.transform.localRotation = Quaternion.identity;
                    attach = go.transform;
                }
                else
                {
                    // 已存在就只校正位置，避免挂在原点
                    Undo.RecordObject(attach, "Adjust Attach Transform");
                    attach.localPosition = new Vector3(0f, 0f, AttachDistance);
                    attach.localRotation = Quaternion.identity;
                }

                Undo.RecordObject(interactor, "Set Attach Transform");
                interactor.attachTransform = attach;

                // 关键：XRI 在 Awake 会把「射线起点」复制成挂点位置，
                // 所以挂点一远射线就一起被拉走。挂上运行时修正组件，
                // 在 Start（晚于 Awake）把射线起点复位回控制器原点。
                if (interactor.GetComponent<XRTrainingRayOriginFix>() == null)
                {
                    Undo.AddComponent<XRTrainingRayOriginFix>(interactor.gameObject);
                    Debug.Log($"[场景修复] {interactor.gameObject.name} → 已挂载射线起点修正组件");
                }

                count++;
                Debug.Log($"[场景修复] {interactor.gameObject.name} → 抓取挂点前方 {AttachDistance}m");
            }
            return count;
        }

        /// <summary>XR 相机近裁剪面过大时调小，防止抓在手里的物体被裁掉。</summary>
        static int FixNearClipPlane(Transform xrOrigin)
        {
            int count = 0;
            foreach (var camera in xrOrigin.GetComponentsInChildren<Camera>(true))
            {
                if (camera.nearClipPlane > 0.05f)
                {
                    Undo.RecordObject(camera, "Fix Near Clip Plane");
                    Debug.Log($"[场景修复] {camera.gameObject.name} 近裁剪面 {camera.nearClipPlane} → {RecommendedNearClip}");
                    camera.nearClipPlane = RecommendedNearClip;
                    count++;
                }
            }
            return count;
        }
    }
}
#endif
