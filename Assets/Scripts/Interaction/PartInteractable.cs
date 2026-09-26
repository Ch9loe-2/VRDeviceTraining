using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// 可拆卸零件。
/// 支持多步骤拆装流程：
/// - 拆卸阶段：只有轮到 requiredStepIndex 这一步时，零件才允许被 XR 抓取（严格锁定）。
/// - 拆卸阶段：移动超过拆卸距离才会推进培训流程。
/// - 组装阶段：按拆卸的逆向顺序重新装回；零件回到 originalPosition 附近时推进流程。
/// 已经拆卸/组装的零件不再参与锁定，避免影响已完成状态。
/// </summary>
public enum PartStatus
{
    /// <summary>尚未轮到，被锁定，不可抓取。</summary>
    Locked,
    /// <summary>当前步骤要求本零件，可操作。</summary>
    Available,
    /// <summary>已成功拆卸或已装回。</summary>
    Removed
}

public class PartInteractable : MonoBehaviour
{
    [Header("零件信息")]
    [SerializeField] private string partName = "Part";

    [Header("拆卸判定")]
    [SerializeField] private float detachDistance = 0.5f;

    [Header("组装判定")]
    [Tooltip("零件被装回时的距离阈值。小于此距离视为已装回。")]
    [SerializeField] private float attachDistance = 0.3f;

    [Header("培训系统")]
    [SerializeField] private TrainingManager trainingManager;

    [Header("流程顺序")]
    [Tooltip("该零件对应的训练步骤索引（从 0 开始）。TrainingManager 依据该索引判断当前是否轮到本零件。")]
    [SerializeField] private int requiredStepIndex = 0;

    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private bool disassemblyRemoved = false; // 拆卸阶段已移除
    private bool assembled = false;          // 组装阶段已装回

    /// <summary>
    /// 零件当前拆卸/组装状态。
    /// </summary>
    public PartStatus Status
    {
        get
        {
            if (trainingManager == null)
                return PartStatus.Available;

            TrainingPhase phase = trainingManager.CurrentPhase;

            // 培训已完成 — 全部视为已处理
            if (phase == TrainingPhase.Completed)
                return PartStatus.Removed;

            // 已装回
            if (assembled)
                return PartStatus.Removed;

            // 拆卸阶段已移除
            if (phase == TrainingPhase.Disassembly && disassemblyRemoved)
                return PartStatus.Removed;

            // 当前步骤轮到本零件
            if (trainingManager.IsPartCurrentStep(requiredStepIndex))
                return PartStatus.Available;

            return PartStatus.Locked;
        }
    }

    private XRGrabInteractable grabInteractable;
    private int lastPhaseStep = int.MinValue;

    // ===== 初始物理状态 =====
    private Rigidbody partRigidbody;
    private bool initialIsKinematic;
    private bool initialUseGravity;
    private RigidbodyConstraints initialConstraints;

    // ===== 误操作上报 =====
    private XRRayInteractor[] rayInteractors;
    private bool wrongAttemptReported;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        originalRotation = transform.rotation;

        partRigidbody = GetComponent<Rigidbody>();
        if (partRigidbody != null)
        {
            initialIsKinematic = partRigidbody.isKinematic;
            initialUseGravity = partRigidbody.useGravity;
            initialConstraints = partRigidbody.constraints;
        }
    }

    private void Start()
    {
        originalPosition = transform.position;
        rayInteractors = FindObjectsOfType<XRRayInteractor>();
        RefreshLockState(true);
    }

    private void OnEnable()
    {
        SubscribeTrainingEvents();
        RefreshLockState(true);
    }

    private void OnDisable()
    {
        UnsubscribeTrainingEvents();
    }

    private void SubscribeTrainingEvents()
    {
        if (trainingManager == null)
            return;

        trainingManager.OnTrainingReset -= ResetPart;
        trainingManager.OnTrainingReset += ResetPart;
    }

    private void UnsubscribeTrainingEvents()
    {
        if (trainingManager == null)
            return;

        trainingManager.OnTrainingReset -= ResetPart;
    }

    private void Update()
    {
        RefreshLockState(false);

        if (trainingManager == null)
            return;

        TrainingPhase phase = trainingManager.CurrentPhase;

        // 培训已完成，不再处理任何交互
        if (phase == TrainingPhase.Completed)
            return;

        // 组装阶段：检查是否已装回
        if (phase == TrainingPhase.Assembly)
        {
            if (assembled)
                return;

            if (!IsPartStep())
            {
                CheckWrongAttempt();
                return;
            }

            // 零件已被用户移动到靠近原始位置 → 视为装回
            float distance = Vector3.Distance(transform.position, originalPosition);
            if (distance < attachDistance)
            {
                AssemblySuccess();
            }
            return;
        }

        // 拆卸阶段：原始逻辑
        if (disassemblyRemoved)
            return;

        if (!IsPartStep())
        {
            CheckWrongAttempt();
            return;
        }

        float detachDist = Vector3.Distance(transform.position, originalPosition);
        if (detachDist >= detachDistance)
        {
            DisassemblySuccess();
        }
    }

    /// <summary>当前培训步骤是否轮到本零件。</summary>
    private bool IsPartStep()
    {
        if (trainingManager == null)
            return true;

        return trainingManager.IsPartCurrentStep(requiredStepIndex);
    }

    /// <summary>刷新抓取锁定。</summary>
    private void RefreshLockState(bool force)
    {
        if (grabInteractable == null)
            grabInteractable = GetComponent<XRGrabInteractable>();

        if (grabInteractable == null)
            return;

        // 已经装回 — 锁定（不允许再拿起来）
        if (assembled)
        {
            if (grabInteractable.enabled)
            {
                grabInteractable.enabled = false;
                Debug.Log($"【培训流程】{partName} 已装回，锁定抓取");
            }
            return;
        }

        // 已拆卸：拆卸阶段可抓，组装阶段也可抓（供用户装回）
        bool shouldBeGrabbable = disassemblyRemoved || IsPartStep();

        if (trainingManager == null)
        {
            if (grabInteractable.enabled != shouldBeGrabbable)
                grabInteractable.enabled = shouldBeGrabbable;
            return;
        }

        int currentStep = trainingManager.CurrentStepIndex;

        if (!force && currentStep == lastPhaseStep)
            return;

        if (!shouldBeGrabbable && grabInteractable.isSelected)
            return;

        lastPhaseStep = currentStep;

        if (grabInteractable.enabled != shouldBeGrabbable)
        {
            grabInteractable.enabled = shouldBeGrabbable;
            Debug.Log($"【培训流程】{partName} 抓取状态：{(shouldBeGrabbable ? "可抓取" : "已锁定")}（当前步骤 {currentStep}，本零件需要 {requiredStepIndex}）");
        }
    }

    private void CheckWrongAttempt()
    {
        if (trainingManager == null)
            return;

        if (rayInteractors == null || rayInteractors.Length == 0)
            rayInteractors = FindObjectsOfType<XRRayInteractor>();

        bool anySelectActive = false;

        for (int i = 0; i < rayInteractors.Length; i++)
        {
            var interactor = rayInteractors[i];
            if (interactor == null)
                continue;

            if (!((IXRSelectInteractor)interactor).isSelectActive)
                continue;

            anySelectActive = true;

            if (!IsAimingAtThisPart(interactor))
                continue;

            if (wrongAttemptReported)
                continue;

            wrongAttemptReported = true;
            trainingManager.RecordWrongOperation(requiredStepIndex);
        }

        if (!anySelectActive)
            wrongAttemptReported = false;
    }

    private bool IsAimingAtThisPart(XRRayInteractor interactor)
    {
        Transform origin = interactor.rayOriginTransform != null
            ? interactor.rayOriginTransform
            : interactor.transform;

        RaycastHit hit;
        if (!Physics.Raycast(origin.position, origin.forward, out hit, 20f))
            return false;

        if (hit.collider == null)
            return false;

        return hit.collider.gameObject == gameObject
               || hit.collider.GetComponentInParent<PartInteractable>() == this;
    }

    /// <summary>
    /// 培训重置：把本零件完全恢复到初始状态。
    /// </summary>
    public void ResetPart()
    {
        ForceReleaseFromXR();

        disassemblyRemoved = false;
        assembled = false;
        wrongAttemptReported = false;

        transform.position = originalPosition;
        transform.rotation = originalRotation;

        if (partRigidbody == null)
            partRigidbody = GetComponent<Rigidbody>();

        if (partRigidbody != null)
        {
            partRigidbody.velocity = Vector3.zero;
            partRigidbody.angularVelocity = Vector3.zero;
            partRigidbody.isKinematic = initialIsKinematic;
            partRigidbody.useGravity = initialUseGravity;
            partRigidbody.constraints = initialConstraints;
            partRigidbody.Sleep();
        }

        lastPhaseStep = int.MinValue;
        RefreshLockState(true);

        Debug.Log($"【培训重置】{partName} 已复位");
    }

    private void ForceReleaseFromXR()
    {
        if (grabInteractable == null)
            grabInteractable = GetComponent<XRGrabInteractable>();

        if (grabInteractable == null)
            return;

        if (!grabInteractable.isSelected)
            return;

        var interactionManager = grabInteractable.interactionManager;
        var selecting = grabInteractable.interactorsSelecting;

        if (interactionManager == null || selecting == null || selecting.Count == 0)
            return;

        var snapshot = new List<IXRSelectInteractor>(selecting);

        for (int i = snapshot.Count - 1; i >= 0; i--)
        {
            var interactor = snapshot[i];
            if (interactor == null)
                continue;

            interactionManager.SelectExit(interactor, grabInteractable);
        }
    }

    private void DisassemblySuccess()
    {
        if (!IsPartStep())
        {
            Debug.Log($"【培训流程】请先完成前置步骤：{DescribeRequiredStep()}");
            return;
        }

        disassemblyRemoved = true;

        Debug.Log($"【拆卸成功】{partName}");

        if (grabInteractable != null && !grabInteractable.enabled)
            grabInteractable.enabled = true;

        if (trainingManager != null)
            trainingManager.CompleteCurrentStep();
        else
            Debug.LogWarning($"【培训系统】{partName} 没有绑定 TrainingManager。");
    }

    private void AssemblySuccess()
    {
        if (!IsPartStep())
        {
            Debug.Log($"【组装流程】请先完成前置步骤：{DescribeRequiredStep()}");
            return;
        }

        assembled = true;

        Debug.Log($"【组装成功】{partName} 已装回");

        // 装回后不允许再次抓取
        if (grabInteractable != null)
        {
            // 先强制退出抓取
            ForceReleaseFromXR();
            grabInteractable.enabled = false;
        }

        if (trainingManager != null)
            trainingManager.CompleteCurrentStep();
        else
            Debug.LogWarning($"【培训系统】{partName} 没有绑定 TrainingManager。");
    }

    private string DescribeRequiredStep()
    {
        if (trainingManager != null)
            return trainingManager.GetStepName(requiredStepIndex);

        return $"第 {requiredStepIndex + 1} 步";
    }
}