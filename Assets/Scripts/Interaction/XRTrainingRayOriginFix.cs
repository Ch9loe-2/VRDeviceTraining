using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRDeviceTraining
{
    /// <summary>
    /// 把 Ray Interactor 的射线起点锁回控制器原点。
    ///
    /// 背景（读 XRRayInteractor 源码确认）：
    /// XRI 在 Awake 时会执行
    ///     m_RayOriginTransform.localPosition = attachTransform.localPosition;
    /// 也就是说「射线起点」会自动跟随「抓取挂点」。
    /// 而抓取时的物体定位（XRGeneralGrabTransformer 的 m_InteractorLocalGrabPoint）
    /// 用的同样是 attachTransform。两者共用一个 Transform。
    ///
    /// 于是出现两难：
    ///   attachTransform = 0   → 射线正常，但物体被抓到眼前，糊满屏幕
    ///   attachTransform = 1.2 → 物体停在原位很舒服，但射线起点被一起拉走，射线失效
    ///
    /// 本脚本在 Start（晚于 Awake）把射线起点复位到控制器原点，
    /// 让「射线起点」和「抓取挂点」各司其职：射线从手上发出，物体停在手前方。
    /// </summary>
    [RequireComponent(typeof(XRRayInteractor))]
    public class XRTrainingRayOriginFix : MonoBehaviour
    {
        void Start()
        {
            var rayInteractor = GetComponent<XRRayInteractor>();
            var rayOrigin = rayInteractor != null ? rayInteractor.rayOriginTransform : null;

            if (rayOrigin == null)
            {
                Debug.LogWarning($"[XRTrainingRayOriginFix] {gameObject.name} 上找不到 rayOriginTransform，已跳过。", this);
                return;
            }

            rayOrigin.localPosition = Vector3.zero;
            rayOrigin.localRotation = Quaternion.identity;
        }
    }
}
