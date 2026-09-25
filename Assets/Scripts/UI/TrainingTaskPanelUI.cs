using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 任务面板 UI：把 TrainingManager 的步骤状态显示到 TaskPanel 的 Legacy Text 上。
///
/// 只读取 TrainingManager，不修改 TrainingManager 的任何数据，也不修改步骤配置。
/// 只在 TrainingManager 的进度发生变化时刷新文本，不做每帧无意义赋值。
/// 全部使用 UnityEngine.UI.Text，不依赖 TextMeshPro。
///
/// M26.1：步骤行改为「容器 + 模板」的运行时动态生成，行数 = TrainingManager.TotalSteps，
/// 不再有固定的 3 行上限。旧版固定字段 step1Text / step2Text / step3Text 已移除，
/// 步骤行的数量不再由任何序列化字段决定。
/// </summary>
public class TrainingTaskPanelUI : MonoBehaviour
{
    [Header("数据来源")]
    [SerializeField] private TrainingManager trainingManager;

    [Header("任务面板文本")]
    [SerializeField] private Text taskTitleText;
    [SerializeField] private Text currentStepText;

    [Header("步骤列表（动态）")]
    [Tooltip("步骤行容器。运行时按 TrainingManager.TotalSteps 生成对应数量的步骤行，行数不再有上限。")]
    [SerializeField] private RectTransform stepListContainer;

    [Tooltip("步骤行模板。只用来复制，不参与显示，Start 之后会被隐藏。")]
    [SerializeField] private Text stepRowTemplate;

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

    // ===== 动态步骤行布局常量（M26.1）=====
    // 与旧版固定 Y（-118 / -156 / -194）完全对齐：
    // 单行高 34，行间距 4，三行总高 = 3*34 + 2*4 = 110。
    // 因此 2 步 / 3 步时面板外观与 M26.1 之前逐像素一致。
    private const float RowHeight = 34f;
    private const float RowSpacing = 4f;
    private const int BaselineRowCount = 3;

    // 运行时生成的步骤行（不含模板自身）。索引与 TrainingManager 的步骤索引一一对应。
    private readonly List<Text> stepRows = new List<Text>();

    // ===== 布局基准值 =====
    // 在 Awake 时从场景实际取值，避免把 TaskPanel / HintText / ResetButton
    // 的当前坐标写死进代码。行数超过基线时，这三者整体下移「超出的高度」。
    private RectTransform panelRect;
    private Vector2 panelBaseSize;
    private RectTransform hintRect;
    private Vector2 hintBasePos;
    private RectTransform resetRect;
    private Vector2 resetBasePos;

    private int lastStepIndex = -1;
    private bool lastAllCompleted;
    private bool warnedMissingManager;
    private int lastRowCount = 0;

    // 提示的到期时间（Time.time 基准）；<= 0 表示当前没有正在显示的提示
    private float hintHideTime;

    private void Awake()
    {
        CaptureLayoutBaseline();

        // 模板只用于复制，不参与显示。
        if (stepRowTemplate != null)
            stepRowTemplate.gameObject.SetActive(false);

        // 清掉上一次运行可能残留下来的步骤行，保证从干净状态开始。
        ClearRuntimeRows();

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

        // 步骤行的唯一数据源：TrainingManager.TotalSteps。
        // 行数 = 步骤数，不再受任何固定字段数量限制。
        int stepCount = trainingManager.TotalSteps;
        LayoutStepList(stepCount);

        for (int i = 0; i < stepRows.Count; i++)
        {
            string state;
            if (i < index)
                state = SuffixCompleted;
            else if (i == index && !allCompleted)
                state = SuffixActive;
            else
                state = SuffixPending;

            SetText(stepRows[i], (i + 1) + ". " + SafeStepName(i) + state);
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

    // ==================== 动态步骤行（M26.1）====================

    /// <summary>
    /// 记录布局基准值。TaskPanel / HintText / ResetButton 的当前坐标直接从场景读取，
    /// 不在代码里写死，这样以后在 Inspector 里调整它们的位置不需要改代码。
    /// </summary>
    private void CaptureLayoutBaseline()
    {
        if (stepListContainer == null)
            return;

        panelRect = stepListContainer.parent as RectTransform;
        if (panelRect != null)
            panelBaseSize = panelRect.sizeDelta;

        if (hintText != null)
        {
            hintRect = hintText.rectTransform;
            hintBasePos = hintRect.anchoredPosition;
        }

        if (resetButton != null)
        {
            resetRect = resetButton.GetComponent<RectTransform>();
            if (resetRect != null)
                resetBasePos = resetRect.anchoredPosition;
        }
    }

    /// <summary>
    /// 销毁容器下所有运行时生成的步骤行，只保留模板自身。
    /// 用于 Awake，保证不会残留上一次运行（或编辑态误存）留下来的行。
    /// </summary>
    private void ClearRuntimeRows()
    {
        if (stepListContainer == null)
            return;

        Transform template = stepRowTemplate != null ? stepRowTemplate.transform : null;

        for (int i = stepListContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = stepListContainer.GetChild(i);
            if (child == null || child == template)
                continue;

            DestroySmart(child.gameObject);
        }

        stepRows.Clear();
        lastRowCount = 0;
    }

    /// <summary>
    /// 按步骤数对齐步骤行数量并刷新列表高度。
    /// 行数变化时才增删对象，普通刷新只改文本，不产生额外的对象创建。
    /// </summary>
    private void LayoutStepList(int count)
    {
        if (stepListContainer == null)
            return;

        if (count < 0)
            count = 0;

        if (count != lastRowCount)
        {
            EnsureRowCount(count);
            lastRowCount = count;
        }

        float listHeight = count > 0
            ? count * RowHeight + (count - 1) * RowSpacing
            : 0f;

        if (!Mathf.Approximately(stepListContainer.sizeDelta.y, listHeight))
            stepListContainer.sizeDelta = new Vector2(stepListContainer.sizeDelta.x, listHeight);

        LayoutRebuilder.ForceRebuildLayoutImmediate(stepListContainer);

        ApplyListOverflow(listHeight);
    }

    /// <summary>把步骤行数量对齐到 count：多余的行销毁，不足的从模板复制。</summary>
    private void EnsureRowCount(int count)
    {
        if (stepRowTemplate == null)
            return;

        Transform container = stepListContainer;

        while (stepRows.Count > count)
        {
            int last = stepRows.Count - 1;
            Text row = stepRows[last];
            stepRows.RemoveAt(last);

            if (row != null)
                DestroySmart(row.gameObject);
        }

        while (stepRows.Count < count)
        {
            GameObject rowGO = Instantiate(stepRowTemplate.gameObject, container, false);
            rowGO.name = "StepRow " + (stepRows.Count + 1);
            rowGO.SetActive(true);

            Text rowText = rowGO.GetComponent<Text>();
            stepRows.Add(rowText);

            SetText(rowText, string.Empty);
        }
    }

    /// <summary>
    /// 步骤行超过基线数量（3 行）时，把 TaskPanel 高度、HintText、ResetButton
    /// 整体下移超出的高度，保证步骤行始终在面板内，且不遮挡提示与按钮。
    /// 2 步 / 3 步时 extra 恒为 0，外观与 M26.1 之前完全一致。
    /// </summary>
    private void ApplyListOverflow(float listHeight)
    {
        float baseline = BaselineRowCount * RowHeight + (BaselineRowCount - 1) * RowSpacing;
        float extra = Mathf.Max(0f, listHeight - baseline);

        if (panelRect != null)
            panelRect.sizeDelta = new Vector2(panelBaseSize.x, panelBaseSize.y + extra);

        if (hintRect != null)
            hintRect.anchoredPosition = new Vector2(hintBasePos.x, hintBasePos.y - extra);

        if (resetRect != null)
            resetRect.anchoredPosition = new Vector2(resetBasePos.x, resetBasePos.y - extra);
    }

    /// <summary>运行中用 Destroy，编辑态（ContextMenu 手动刷新）用 DestroyImmediate。</summary>
    private static void DestroySmart(GameObject go)
    {
        if (go == null)
            return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
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
