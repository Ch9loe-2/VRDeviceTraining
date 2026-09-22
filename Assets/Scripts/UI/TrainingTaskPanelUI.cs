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

    [Header("显示文案")]
    [Tooltip("任务标题，一般不需要改动")]
    [SerializeField] private string taskTitle = "设备拆装培训";

    [Tooltip("步骤显示名。TrainingManager 里只配置了 2 步时，这里仍可按顺序填 3 个，第 3 行会显示为待完成")]
    [SerializeField] private string[] stepNames = { "拆卸 Battery", "拆卸后盖", "拆卸主板" };

    private const string PrefixCurrentStep = "当前步骤：";
    private const string TextAllCompleted = "当前步骤：培训已完成";
    private const string SuffixCompleted = " [已完成]";
    private const string SuffixActive = " [进行中]";
    private const string SuffixPending = " [待完成]";

    private Text[] stepTexts;

    private int lastStepIndex = -1;
    private bool lastAllCompleted;
    private bool warnedMissingManager;

    private void Awake()
    {
        stepTexts = new[] { step1Text, step2Text, step3Text };
    }

    private void OnEnable()
    {
        Refresh();
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
            SetText(currentStepText, PrefixCurrentStep + current.stepName);
        else if (allCompleted)
            SetText(currentStepText, TextAllCompleted);
        else
            SetText(currentStepText, PrefixCurrentStep + SafeStepName(index));

        if (stepTexts == null || stepTexts.Length == 0)
            stepTexts = new[] { step1Text, step2Text, step3Text };

        for (int i = 0; i < stepTexts.Length; i++)
        {
            string state;
            if (i < index)
                state = SuffixCompleted;
            else if (i == index && !allCompleted)
                state = SuffixActive;
            else
                state = SuffixPending;

            SetText(stepTexts[i], (i + 1) + ". " + SafeStepName(i) + state);
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

    private string SafeStepName(int index)
    {
        if (stepNames == null || index < 0 || index >= stepNames.Length)
            return string.Empty;
        return string.IsNullOrEmpty(stepNames[index]) ? string.Empty : stepNames[index];
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
