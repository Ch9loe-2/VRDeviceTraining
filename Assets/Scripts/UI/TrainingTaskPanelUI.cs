using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 任务面板 UI：把 TrainingManager 的步骤状态显示到 TaskPanel 的 Legacy Text 上。
///
/// 只读取 TrainingManager，不修改 TrainingManager 的任何数据，也不修改步骤配置。
/// 只在 TrainingManager 的进度发生变化时刷新文本，不做每帧无意义赋值。
/// 全部使用 UnityEngine.UI.Text，不依赖 TextMeshPro。
/// </summary>
public class TrainingTaskPanelUI : MonoBehaviour
{
    [Header("数据来源")]
    [SerializeField] private TrainingManager trainingManager;

    [Header("任务面板文本")]
    [SerializeField] private Text taskTitleText;
    [SerializeField] private Text currentStepText;
    [SerializeField] private Text step1Text;
    [SerializeField] private Text step2Text;
    [SerializeField] private Text step3Text;

    [Header("错误提示")]
    [Tooltip("误操作提示文本。为空时只在 Console 输出，不显示提示。")]
    [SerializeField] private Text hintText;

    [Tooltip("提示显示时长（秒）")]
    [SerializeField] private float hintDuration = 2f;

    [SerializeField] private string hintPrefix = "请先完成：";

    [Header("操作按钮")]
    [Tooltip("重新开始按钮。为空时只能通过代码调用 TrainingManager.ResetTraining()。")]
    [SerializeField] private Button resetButton;

    [Header("显示文案")]
    [Tooltip("任务标题，一般不需要改动")]
    [SerializeField] private string taskTitle = "设备拆装培训";

    [Tooltip("已废弃：步骤名称统一从 TrainingManager.GetStepName() 读取。此字段仅为兼容旧场景序列化数据而保留，运行时不再使用，也不需要维护。")]
    [SerializeField] private string[] stepNames = { };

    private const string PrefixCurrentStep = "当前步骤：";
    private const string TextAllCompleted = "当前步骤：培训已完成";
    private const string SuffixCompleted = " [已完成]";
    private const string SuffixActive = " [进行中]";
    private const string SuffixPending = " [待完成]";

    private Text[] stepTexts;

    private int lastStepIndex = -1;
    private bool lastAllCompleted;
    private bool warnedMissingManager;

    // 提示的到期时间（Time.time 基准）；<= 0 表示当前没有正在显示的提示
    private float hintHideTime;

    private void Awake()
    {
        stepTexts = new[] { step1Text, step2Text, step3Text };

        if (resetButton != null)
        {
            resetButton.onClick.RemoveListener(OnResetButtonClicked);
            resetButton.onClick.AddListener(OnResetButtonClicked);
        }
    }

    private void OnDestroy()
    {
        if (resetButton != null)
            resetButton.onClick.RemoveListener(OnResetButtonClicked);
    }

    private void OnEnable()
    {
        SubscribeTrainingEvents();
        Refresh();
    }

    private void OnDisable()
    {
        UnsubscribeTrainingEvents();
    }

    private void SubscribeTrainingEvents()
    {
        if (trainingManager == null)
            return;

        // 先取消再订阅，避免重复订阅导致一次误操作触发多次
        trainingManager.OnWrongOperation -= HandleWrongOperation;
        trainingManager.OnWrongOperation += HandleWrongOperation;
        trainingManager.OnTrainingReset -= HandleTrainingReset;
        trainingManager.OnTrainingReset += HandleTrainingReset;
    }

    private void UnsubscribeTrainingEvents()
    {
        if (trainingManager == null)
            return;

        trainingManager.OnWrongOperation -= HandleWrongOperation;
        trainingManager.OnTrainingReset -= HandleTrainingReset;
    }

    /// <summary>
    /// 收到培训重置事件：立刻刷新面板，并清掉可能还挂着的误操作提示。
    /// 零件在同一次广播里由 PartInteractable 自行复位，这里不碰任何零件状态。
    /// </summary>
    private void HandleTrainingReset()
    {
        hintHideTime = 0f;
        SetText(hintText, string.Empty);

        Refresh();
    }

    private void OnResetButtonClicked()
    {
        if (trainingManager == null)
        {
            Debug.LogWarning("[任务面板] 未绑定 TrainingManager，无法重置培训。", this);
            return;
        }

        trainingManager.ResetTraining();
    }

    /// <summary>
    /// 收到误操作事件：显示提示并重新计时（新提示会覆盖旧提示的倒计时）。
    /// </summary>
    private void HandleWrongOperation(int expectedStepIndex)
    {
        string stepName = SafeStepName(expectedStepIndex);
        if (string.IsNullOrEmpty(stepName))
            stepName = $"第 {expectedStepIndex + 1} 步";

        ShowHint(hintPrefix + stepName);
    }

    private void ShowHint(string message)
    {
        if (hintText == null)
            return;

        SetText(hintText, message);
        hintHideTime = Time.time + Mathf.Max(0.1f, hintDuration);
    }

    /// <summary>提示到期后清空。只在到点那一帧写一次，不做每帧赋值。</summary>
    private void UpdateHintVisibility()
    {
        if (hintText == null || hintHideTime <= 0f)
            return;

        if (Time.time < hintHideTime)
            return;

        hintHideTime = 0f;
        SetText(hintText, string.Empty);
    }

    private void Update()
    {
        if (trainingManager == null)
            return;

        // 只在进度真正发生变化时才刷新，避免每帧写 Text
        int index = trainingManager.CurrentStepIndex;
        bool allCompleted = IsAllCompleted();

        if (index != lastStepIndex || allCompleted != lastAllCompleted)
            Refresh();

        UpdateHintVisibility();
    }

    /// <summary>立即刷新一次，可在 Inspector 右键菜单里手动触发验证。</summary>
    [ContextMenu("立即刷新任务面板")]
    public void Refresh()
    {
        if (trainingManager == null)
        {
            if (!warnedMissingManager)
            {
                warnedMissingManager = true;
                Debug.LogWarning("[任务面板] TrainingTaskPanelUI 没有绑定 TrainingManager，无法刷新。", this);
            }
            return;
        }

        int index = trainingManager.CurrentStepIndex;
        bool allCompleted = IsAllCompleted();

        lastStepIndex = index;
        lastAllCompleted = allCompleted;

        SetText(taskTitleText, taskTitle);

        TrainingStep current = trainingManager.CurrentStep;
        if (current != null)
            SetText(currentStepText, PrefixCurrentStep + current.StepName);
        else if (allCompleted)
            SetText(currentStepText, TextAllCompleted);
        else
            SetText(currentStepText, PrefixCurrentStep + SafeStepName(index));

        if (stepTexts == null || stepTexts.Length == 0)
            stepTexts = new[] { step1Text, step2Text, step3Text };

        // 以 TrainingManager 的实际步骤数量为准；超出的行清空并隐藏，
        // 避免出现永远停在"待完成"的幽灵行（例如 steps 只有 2 步时的第 3 行）。
        int stepCount = trainingManager.TotalSteps;

        for (int i = 0; i < stepTexts.Length; i++)
        {
            Text row = stepTexts[i];

            if (i >= stepCount)
            {
                SetText(row, string.Empty);
                SetRowVisible(row, false);
                continue;
            }

            string state;
            if (i < index)
                state = SuffixCompleted;
            else if (i == index && !allCompleted)
                state = SuffixActive;
            else
                state = SuffixPending;

            SetText(row, (i + 1) + ". " + SafeStepName(i) + state);
            SetRowVisible(row, true);
        }
    }

    /// <summary>
    /// 全部完成的判定：TrainingManager.CurrentStep 为 null 表示索引已越过最后一步。
    /// 索引为 0 时也可能为 null（步骤列表为空），这种情况不视为"全部完成"。
    /// </summary>
    private bool IsAllCompleted()
    {
        return trainingManager != null
               && trainingManager.CurrentStep == null
               && trainingManager.CurrentStepIndex > 0;
    }

    /// <summary>
    /// 步骤名称的唯一数据源：TrainingManager。
    /// 不再读取本组件自己的 stepNames，避免 UI 与 TrainingManager 各存一份步骤名而产生漂移。
    /// TrainingManager.GetStepName() 对越界索引会返回「第 N 步」兜底，不会返回 null。
    /// </summary>
    private string SafeStepName(int index)
    {
        if (trainingManager == null || index < 0)
            return string.Empty;

        return trainingManager.GetStepName(index);
    }

    /// <summary>
    /// 显示 / 隐藏某一行步骤文本。
    /// 只切换该行 Text 组件自身的启用状态，不移动、不重建、不改动布局与父对象。
    /// </summary>
    private static void SetRowVisible(Text row, bool visible)
    {
        if (row == null)
            return;

        if (row.enabled != visible)
            row.enabled = visible;
    }

    private static void SetText(Text target, string value)
    {
        if (target == null)
            return;
        if (target.text == value)
            return;
        target.text = value;
    }
}
