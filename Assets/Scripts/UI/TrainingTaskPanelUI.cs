using System.Collections.Generic;
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

    [Header("步骤列表（动态）")]
    [Tooltip("步骤行容器。运行时按 TrainingManager.TotalSteps 生成对应数量的步骤行。")]
    [SerializeField] private RectTransform stepListContainer;

    [Tooltip("步骤行模板。只用来复制，不参与显示，Start 之后会被隐藏。")]
    [SerializeField] private Text stepRowTemplate;

    [Header("错误提示")]
    [Tooltip("误操作提示文本。")]
    [SerializeField] private Text hintText;

    [Tooltip("提示显示时长（秒）")]
    [SerializeField] private float hintDuration = 2f;

    [SerializeField] private string hintPrefix = "当前应操作：";

    [Header("操作按钮")]
    [Tooltip("重新开始按钮。")]
    [SerializeField] private Button resetButton;

    [Header("显示文案")]
    [Tooltip("任务标题")]
    [SerializeField] private string taskTitle = "设备拆装培训";

    private const string PrefixCurrentStep = "当前步骤：";
    private const string TextAllCompleted = "培训已完成";
    private const string PrefixPhase = "阶段：";
    private const string SuffixCompleted = " [已完成]";
    private const string SuffixActive = " [进行中]";
    private const string SuffixPending = " [待完成]";

    // ===== 动态步骤行布局常量 =====
    private const float RowHeight = 34f;
    private const float RowSpacing = 4f;
    private const int BaselineRowCount = 3;

    private readonly List<Text> stepRows = new List<Text>();

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
    private TrainingPhase lastPhase = TrainingPhase.Disassembly;

    private float hintHideTime;

    private void Awake()
    {
        CaptureLayoutBaseline();

        if (stepRowTemplate != null)
            stepRowTemplate.gameObject.SetActive(false);

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

        trainingManager.OnWrongOperation -= HandleWrongOperation;
        trainingManager.OnWrongOperation += HandleWrongOperation;
        trainingManager.OnTrainingReset -= HandleTrainingReset;
        trainingManager.OnTrainingReset += HandleTrainingReset;
        trainingManager.OnPhaseChanged -= HandlePhaseChanged;
        trainingManager.OnPhaseChanged += HandlePhaseChanged;
    }

    private void UnsubscribeTrainingEvents()
    {
        if (trainingManager == null)
            return;

        trainingManager.OnWrongOperation -= HandleWrongOperation;
        trainingManager.OnTrainingReset -= HandleTrainingReset;
        trainingManager.OnPhaseChanged -= HandlePhaseChanged;
    }

    private void HandlePhaseChanged(TrainingPhase phase)
    {
        // 重构步骤行以反映组装阶段的步骤名称变化
        Refresh();
    }

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

        int index = trainingManager.CurrentStepIndex;
        bool allCompleted = IsAllCompleted();

        if (index != lastStepIndex || allCompleted != lastAllCompleted)
            Refresh();

        UpdateHintVisibility();
    }

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

        // 阶段与当前步骤
        int stepCount = trainingManager.TotalSteps;
        int doneCount = allCompleted ? stepCount : index;

        if (allCompleted)
        {
            SetText(currentStepText, TextAllCompleted + FormatProgress(stepCount, stepCount));
        }
        else
        {
            string phaseName = GetPhaseLabel(trainingManager.CurrentPhase);
            string stepName = trainingManager.GetStepName(index);
            SetText(currentStepText, PrefixPhase + phaseName + "  " + PrefixCurrentStep + stepName + FormatProgress(doneCount, stepCount));
        }

        // 步骤行：TotalSteps 包含拆卸 + 组装
        int listStepCount = stepCount;
        LayoutStepList(listStepCount);

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

    private bool IsAllCompleted()
    {
        return trainingManager != null
               && trainingManager.CurrentStep == null
               && trainingManager.CurrentStepIndex >= trainingManager.TotalSteps;
    }

    private string SafeStepName(int index)
    {
        if (trainingManager == null || index < 0)
            return string.Empty;

        return trainingManager.GetStepName(index);
    }

    private string GetPhaseLabel(TrainingPhase phase)
    {
        switch (phase)
        {
            case TrainingPhase.Disassembly: return "拆卸";
            case TrainingPhase.Assembly: return "组装";
            case TrainingPhase.Completed: return "已完成";
            default: return "培训";
        }
    }

    // ==================== 动态步骤行 ====================

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

    private static void DestroySmart(GameObject go)
    {
        if (go == null)
            return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }

    private static string FormatProgress(int done, int total)
    {
        return total > 0 ? $"（进度 {done}/{total}）" : string.Empty;
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