using System.Collections.Generic;
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
    private Quaternion originalRotation;
    private bool detached = false;

    private XRGrabInteractable grabInteractable;
    private int lastStepIndex = int.MinValue;

    // ===== 初始物理状态（M23-2）=====
    // 在 Awake 里抓取，此时还没有发生过任何抓取 / 拆卸，拿到的就是 Inspector 里的设计值。
    private Rigidbody partRigidbody;
    private bool initialIsKinematic;
    private bool initialUseGravity;
    private RigidbodyConstraints initialConstraints;

    // ===== 误操作上报（M23-1）=====
    // 被锁定的零件其 XRGrabInteractable.enabled = false，XRI 不会对它发出任何 hover/select 事件，
    // 所以「玩家尝试抓取被锁定零件」这件事在 XRI 事件层面是不可见的。
    // 这里改用「玩家正在按抓取键 + 射线正命中本零件」来判定一次真实尝试。
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

        // 先取消再订阅，避免重复订阅导致一次重置恢复多次
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
        // 只在 TrainingManager 的步骤发生变化时刷新锁定状态，不做无意义的每帧写入。
        RefreshLockState(false);

        if (detached)
            return;

        if (!IsMyStep())
        {
            // 非本零件的步骤：只做一次性的「尝试操作被锁定零件」上报，不改变原有锁定与流程。
            CheckWrongAttempt();
            return;
        }

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

    /// <summary>
    /// 检测「玩家尝试抓取被锁定的本零件」。
    ///
    /// 触发条件（三条同时满足才算一次真实尝试，避免把射线扫过当成误操作）：
    ///   1. 本零件当前不是当前步骤（已锁定）；
    ///   2. 某个 XRRayInteractor 正处于 select 激活状态（玩家按住了抓取键）；
    ///   3. 该 interactor 的射线正命中本零件。
    ///
    /// 同一次按住只上报一次；松开后标志复位，再次尝试可再次上报。
    /// </summary>
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

        // 玩家松开抓取键后复位，允许下一次尝试再次记录。
        if (!anySelectActive)
            wrongAttemptReported = false;
    }

    /// <summary>
    /// 指定 interactor 的射线是否正指向本零件。
    /// </summary>
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

        // 命中的是本零件自身的碰撞体（或被本零件包含的碰撞体）
        return hit.collider.gameObject == gameObject
               || hit.collider.GetComponentInParent<PartInteractable>() == this;
    }

    /// <summary>
    /// 培训重置：把本零件完全恢复到初始状态。
    /// 严格按四步顺序执行，顺序不能颠倒：
    ///   1. 先强制退出 XR 抓取（拿着东西时不能硬改 Transform，否则会跟 XRI 打架）；
    ///   2. 再恢复 Transform（位置 + 旋转）；
    ///   3. 再恢复 Rigidbody 初始状态（清速度、还原 kinematic/gravity/constraints）；
    ///   4. 最后按重置后的 currentStepIndex 重算抓取锁定。
    /// </summary>
    public void ResetPart()
    {
        ForceReleaseFromXR();

        detached = false;
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

        // 让下一次 Update 与 RefreshLockState 都按「全新的第一步」重新判定。
        lastStepIndex = int.MinValue;
        RefreshLockState(true);

        Debug.Log($"【培训重置】{partName} 已复位");
    }

    /// <summary>
    /// 强制从所有正在抓取本零件的 interactors 上退出。
    /// 直接遍历 interactorsSelecting 并逐个 SelectExit；倒序 + 快照，
    /// 避免 SelectExit 修改原集合导致遍历错乱。
    /// </summary>
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

        // 用快照遍历：SelectExit 会同步修改 interactorsSelecting。
        var snapshot = new List<IXRSelectInteractor>(selecting);

        for (int i = snapshot.Count - 1; i >= 0; i--)
        {
            var interactor = snapshot[i];
            if (interactor == null)
                continue;

            interactionManager.SelectExit(interactor, grabInteractable);
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
