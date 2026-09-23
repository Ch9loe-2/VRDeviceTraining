using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// 可拆卸零件。
/// 支持多步骤拆装流程：
/// - 只有轮到 requiredStepIndex 这一步时，零件才允许被 XR 抓取（严格锁定）。
/// - 只有轮到 requiredStepIndex 这一步时，移动超过拆卸距离才会推进培训流程。
/// 已经拆卸成功的零件不再参与锁定，避免影响已完成状态。
/// </summary>
public class PartInteractable : MonoBehaviour
{
    [Header("零件信息")]
    [SerializeField] private string partName = "Battery";

    [Header("拆卸判定")]
    [SerializeField] private float detachDistance = 0.5f;

    [Header("培训系统")]
    [SerializeField] private TrainingManager trainingManager;

    [Header("流程顺序")]
    [Tooltip("该零件属于第几步（从 0 开始）。Battery = 0，BackCover = 1。")]
    [SerializeField] private int requiredStepIndex = 0;

    private Vector3 originalPosition;
    private bool detached = false;

    private XRGrabInteractable grabInteractable;
    private int lastStepIndex = int.MinValue;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
    }

    private void Start()
    {
        originalPosition = transform.position;
        RefreshLockState(true);
    }

    private void OnEnable()
    {
        RefreshLockState(true);
    }

    private void Update()
    {
        // 只在 TrainingManager 的步骤发生变化时刷新锁定状态，不做无意义的每帧写入。
        RefreshLockState(false);

        if (detached)
            return;

        if (!IsMyStep())
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

    /// <summary>
    /// 当前培训步骤是否轮到本零件。
    /// 没有绑定 TrainingManager 时一律视为允许，保持原有行为不变。
    /// </summary>
    private bool IsMyStep()
    {
        if (trainingManager == null)
            return true;

        return trainingManager.CurrentStepIndex == requiredStepIndex;
    }

    /// <summary>
    /// 刷新抓取锁定。只在状态真正变化时才写 enabled。
    /// force = true 时无条件刷新一次（Start / OnEnable）。
    /// </summary>
    private void RefreshLockState(bool force)
    {
        if (grabInteractable == null)
            grabInteractable = GetComponent<XRGrabInteractable>();

        if (grabInteractable == null)
            return;

        // 已经拆下来的零件不再上锁，玩家可以继续自由拿放。
        bool shouldBeGrabbable = detached || IsMyStep();

        if (trainingManager == null)
        {
            // 没有培训系统时不做任何锁定，保持原有行为。
            if (grabInteractable.enabled != shouldBeGrabbable)
            {
                grabInteractable.enabled = shouldBeGrabbable;
                Debug.Log($"【培训流程】{partName} 抓取状态：{(shouldBeGrabbable ? "可抓取" : "已锁定")}（未绑定 TrainingManager）");
            }

            return;
        }

        int currentStep = trainingManager.CurrentStepIndex;

        if (!force && currentStep == lastStepIndex)
            return;

        // 手上正拿着时不要强行关掉抓取，避免物体被硬生生甩掉。
        if (!shouldBeGrabbable && grabInteractable.isSelected)
            return;

        lastStepIndex = currentStep;

        if (grabInteractable.enabled != shouldBeGrabbable)
        {
            grabInteractable.enabled = shouldBeGrabbable;
            Debug.Log($"【培训流程】{partName} 抓取状态：{(shouldBeGrabbable ? "可抓取" : "已锁定")}（当前步骤 {currentStep}，本零件需要 {requiredStepIndex}）");
        }
    }

    private void DetachSuccess()
    {
        // 双保险：万一在锁定状态下仍然被移动了足够距离，也不推进流程。
        if (!IsMyStep())
        {
            Debug.Log(
                $"【培训流程】请先完成前置步骤：{DescribeRequiredStep()}"
            );
            return;
        }

        detached = true;

        Debug.Log($"【拆卸成功】{partName}");

        // 拆下来的零件保持可抓取，玩家可以继续自由拿放。
        if (grabInteractable != null && !grabInteractable.enabled)
        {
            grabInteractable.enabled = true;
        }

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

    private string DescribeRequiredStep()
    {
        if (requiredStepIndex <= 0)
            return "拆卸 Battery";

        if (requiredStepIndex == 1)
            return "拆卸后盖";

        return $"第 {requiredStepIndex + 1} 步";
    }
}
