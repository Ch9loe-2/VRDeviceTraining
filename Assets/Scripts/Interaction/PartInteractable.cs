using UnityEngine;

public class PartInteractable : MonoBehaviour
{
    [Header("零件信息")]
    [SerializeField] private string partName = "Battery";

    [Header("拆卸判定")]
    [SerializeField] private float detachDistance = 0.5f;

    private Vector3 originalPosition;
    private bool detached = false;

    private void Start()
    {
        // 记录 Battery 初始世界坐标
        originalPosition = transform.position;
    }

    private void Update()
    {
        // 已经拆卸成功，就不再重复检测
        if (detached)
            return;

        // 计算 Battery 与初始位置的距离
        float distance = Vector3.Distance(
            transform.position,
            originalPosition
        );

        // 超过指定距离
        if (distance >= detachDistance)
        {
            DetachSuccess();
        }
    }

    private void DetachSuccess()
    {
        detached = true;

        Debug.Log($"【拆卸成功】{partName}");
    }
}