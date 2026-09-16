using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class PartInteractable : MonoBehaviour
{
    [Header("零件信息")]
    [SerializeField] private string partName = "Battery";

    [Header("拆卸设置")]
    [SerializeField] private float detachDistance = 0.5f;

    private Vector3 originalPosition;
    private Transform deviceTransform;
    private bool detached = false;
    private XRGrabInteractable grabInteractable;

    private void Awake()
    {
        originalPosition = transform.position;

        // 零件的父物体就是设备主体（TrainingDevice）
        deviceTransform = transform.parent;
        grabInteractable = GetComponent<XRGrabInteractable>();
    }

    private void Update()
    {
        if (detached || deviceTransform == null)
            return;

        // 关键：被 XR 抓取时不要再插手层级。
        // XRI 会在 RetainTransformParent=false 时自己把零件从设备主体上脱离，
        // 如果这里再调用 SetParent(null)，会打断 XRI 的抓取跟随，零件就会掉落。
        if (grabInteractable != null && grabInteractable.isSelected)
        {
            detached = true;
            Debug.Log($"【拆卸成功】{partName}（由 XR 抓取拆离，层级交给 XRI 处理）");
            return;
        }

        // 非抓取情况下被移动（例如被推走、脚本位移）时，才手动脱离设备主体
        float distance = Vector3.Distance(transform.position, originalPosition);
        if (distance >= detachDistance)
        {
            DetachPart();
        }
    }

    private void DetachPart()
    {
        detached = true;

        // 从设备主体中脱离（保持世界坐标不变）
        transform.SetParent(null);

        Debug.Log($"【拆卸成功】{partName}");
    }
}
