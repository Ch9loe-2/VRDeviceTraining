#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRDeviceTraining.EditorTools
{
    /// <summary>
    /// 一键把场景中 XR Controller (Action-based) 的输入动作，
    /// 关联到 XRI Default Input Actions 资产中对应的动作引用。
    ///
    /// 背景：手动 Add Component 添加的 XR Controller (Action-based)，
    /// 其 action 默认是「内联空动作」(useReference=false 且 bindings 为空)，
    /// 导致 Grip/Trigger 永远不会触发 Select，表现为「射线命中但抓不起来」。
    ///
    /// 用法：Unity 菜单栏 → VRDeviceTraining → 修复控制器 Action 绑定
    /// </summary>
    public static class XRControllerActionFixer
    {
        const string AssetPath =
            "Assets/Samples/XR Interaction Toolkit/2.6.5/Starter Assets/XRI Default Input Actions.inputactions";

        [MenuItem("VRDeviceTraining/修复控制器 Action 绑定", false, 1)]
        public static void FixControllerActions()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError($"[XRFixer] 找不到 XRI Default Input Actions：{AssetPath}");
                return;
            }

            // 收集资产中所有 InputActionReference 子对象，按 "MapName/ActionName" 建索引
            var index = new Dictionary<string, InputActionReference>();
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(AssetPath))
            {
                if (sub is InputActionReference reference &&
                    reference.action != null &&
                    reference.action.actionMap != null)
                {
                    string key = reference.action.actionMap.name + "/" + reference.action.name;
                    index[key] = reference;
                }
            }

            if (index.Count == 0)
            {
                Debug.LogError("[XRFixer] 资产中没有解析出任何 InputActionReference，请检查 .inputactions 是否导入正常。");
                return;
            }

            var controllers = Object.FindObjectsOfType<ActionBasedController>(true);
            if (controllers.Length == 0)
            {
                Debug.LogWarning("[XRFixer] 场景中没有找到 ActionBasedController。");
                return;
            }

            int fixedCount = 0;
            foreach (var controller in controllers)
            {
                bool isRight = controller.gameObject.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                string hand = isRight ? "RightHand" : "LeftHand";

                int bound = BindAll(controller, hand, index);
                EditorUtility.SetDirty(controller);
                fixedCount++;

                Debug.Log($"[XRFixer] {controller.gameObject.name} → {hand}，成功绑定 {bound} 个动作。");
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[XRFixer] 完成！共修复 {fixedCount} 个控制器，场景已保存。现在进入 Play 模式测试：Y → 瞄准 → 按住 G。");
        }

        static int BindAll(ActionBasedController c, string hand, Dictionary<string, InputActionReference> index)
        {
            int count = 0;

            // —— 追踪类（XRI LeftHand / XRI RightHand）——
            count += Bind(c, hand, index, "Position", v => c.positionAction = v);
            count += Bind(c, hand, index, "Rotation", v => c.rotationAction = v);
            count += Bind(c, hand, index, "Is Tracked", v => c.isTrackedAction = v);
            count += Bind(c, hand, index, "Tracking State", v => c.trackingStateAction = v);

            // —— 交互类（XRI LeftHand Interaction / XRI RightHand Interaction）——
            count += Bind(c, hand, index, "Select", v => c.selectAction = v);
            count += Bind(c, hand, index, "Select Value", v => c.selectActionValue = v);
            count += Bind(c, hand, index, "Activate", v => c.activateAction = v);
            count += Bind(c, hand, index, "Activate Value", v => c.activateActionValue = v);
            count += Bind(c, hand, index, "UI Press", v => c.uiPressAction = v);
            count += Bind(c, hand, index, "UI Press Value", v => c.uiPressActionValue = v);
            count += Bind(c, hand, index, "UI Scroll", v => c.uiScrollAction = v);

            // —— 触觉反馈 ——
            count += Bind(c, hand, index, "Haptic Device", v => c.hapticDeviceAction = v);

            return count;
        }

        static int Bind(ActionBasedController c, string hand, Dictionary<string, InputActionReference> index,
                        string actionName, System.Action<InputActionProperty> setter)
        {
            var reference = Find(index, hand, actionName);
            if (reference == null)
                return 0;

            setter(new InputActionProperty(reference));
            return 1;
        }

        /// <summary>
        /// 在索引里查找属于指定手的动作：
        /// 键形如 "XRI RightHand Interaction/Select"，要求包含 hand 关键字且以 /actionName 结尾。
        /// </summary>
        static InputActionReference Find(Dictionary<string, InputActionReference> index, string hand, string actionName)
        {
            string suffix = "/" + actionName;
            foreach (var kv in index)
            {
                if (kv.Key.Contains(hand) && kv.Key.EndsWith(suffix))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// 诊断用：列出场景中所有 ActionBasedController 的动作绑定状态。
        /// </summary>
        [MenuItem("VRDeviceTraining/诊断控制器 Action 状态", false, 2)]
        public static void DiagnoseControllerActions()
        {
            var controllers = Object.FindObjectsOfType<ActionBasedController>(true);
            if (controllers.Length == 0)
            {
                Debug.LogWarning("[XRFixer] 场景中没有 ActionBasedController。");
                return;
            }

            foreach (var c in controllers)
            {
                bool selectOk = c.selectAction.action != null && c.selectAction.action.bindings.Count > 0;
                Debug.Log($"[XRFixer] {c.gameObject.name}：Select 已绑定 = {selectOk}，" +
                          $"useReference = {(c.selectAction.reference != null)}，" +
                          $"bindings = {c.selectAction.action?.bindings.Count ?? 0}");
            }
        }
    }
}
#endif
