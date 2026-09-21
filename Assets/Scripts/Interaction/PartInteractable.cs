using UnityEngine;

public class PartInteractable : MonoBehaviour
{
    [Header("零件信息")]
    [SerializeField] private string partName = "Battery";

    [Header("拆卸判定")]
    [SerializeField] private float detachDistance = 0.5f;

    [Header("培训系统")]
    [SerializeField] private TrainingManager trainingManager;

    private Vector3 originalPosition;
    private bool detached = false;

    private void Start()
    {
        originalPosition = transform.position;
    }

    private void Update()
    {
        if (detached)
            return;

        float distance = Vector3.Distance(
            transform.position,
            originalPosition
        );

        if (distance >= detachDistance)
        {
            DetachSuccess();
        }
    }

    private void DetachSuccess()
    {
        detached = true;

        Debug.Log($"【拆卸成功】{partName}");

        if (trainingManager != null)
        {
            trainingManager.CompleteCurrentStep();
        }
        else
        {
            Debug.LogWarning(
                $"【培训系统】{partName} 没有绑定 TrainingManager。"
            );
        }
    }
}