using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 培训结果面板 UI：培训完成后显示完整统计结果。
///
/// 只读 TrainingManager 与 TrainingResult，不计算任何统计数据。
/// 全部使用 UnityEngine.UI.Text，不依赖 TextMeshPro。
/// </summary>
public class TrainingResultPanelUI : MonoBehaviour
{
    [Header("数据来源")]
    [SerializeField] private TrainingManager trainingManager;

    [Header("结果面板")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private Text resultTitle;
    [SerializeField] private Text statusText;
    [SerializeField] private Text timeText;
    [SerializeField] private Text wrongText;
    [SerializeField] private Text resetCountText;
    [SerializeField] private Button resultResetButton;

    [Header("误操作分类统计")]
    [Tooltip("展示各零件类别的误操作次数。留空则不显示该行。")]
    [SerializeField] private Text categoryBreakdownText;

    [Header("显示文案")]
    [SerializeField] private string resultTitleText = "培训结果";
    [SerializeField] private string statusCompleted = "培训状态：已完成";
    [SerializeField] private string statusNotCompleted = "培训状态：未完成";
    [SerializeField] private string timePrefix = "培训耗时：";
    [SerializeField] private string wrongPrefix = "错误操作：";
    [SerializeField] private string resetCountPrefix = "重置次数：";

    private const string SuffixSecond = " 秒";
    private const string SuffixCount = " 次";

    private void Awake()
    {
        BindButton(true);
        HideResultPanel();
    }

    private void OnEnable()
    {
        SubscribeTrainingEvents();
    }

    private void OnDisable()
    {
        UnsubscribeTrainingEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeTrainingEvents();
        BindButton(false);
    }

    private void SubscribeTrainingEvents()
    {
        if (trainingManager == null)
            return;

        trainingManager.OnTrainingCompleted -= HandleTrainingCompleted;
        trainingManager.OnTrainingCompleted += HandleTrainingCompleted;
        trainingManager.OnTrainingReset -= HandleTrainingReset;
        trainingManager.OnTrainingReset += HandleTrainingReset;
    }

    private void UnsubscribeTrainingEvents()
    {
        if (trainingManager == null)
            return;

        trainingManager.OnTrainingCompleted -= HandleTrainingCompleted;
        trainingManager.OnTrainingReset -= HandleTrainingReset;
    }

    private void BindButton(bool add)
    {
        if (resultResetButton == null)
            return;

        resultResetButton.onClick.RemoveListener(OnResultResetButtonClicked);
        if (add)
            resultResetButton.onClick.AddListener(OnResultResetButtonClicked);
    }

    private void OnResultResetButtonClicked()
    {
        if (trainingManager == null)
        {
            Debug.LogWarning("[结果面板] 未绑定 TrainingManager，无法重新开始。", this);
            return;
        }

        trainingManager.ResetTraining();
    }

    private void HandleTrainingCompleted(TrainingResult result)
    {
        if (result == null)
            return;

        SetText(resultTitle, resultTitleText);

        int totalSteps = trainingManager != null ? trainingManager.TotalSteps : 0;
        int correctSteps = totalSteps; // 正确步骤 = 全部步骤（得分已通过错误操作扣分体现）
        int wrongCount = result.wrongOperationCount;

        // 两行：第一行 步骤/错误，第二行 得分/等级
        string line1 = $"总步骤：{correctSteps}/{totalSteps}  错误操作：{wrongCount}{SuffixCount}";
        string line2 = $"得分：{result.Score}/100  等级：{result.Grade ?? "未评定"}";

        SetText(statusText, line1 + "\n" + line2);
        SetText(timeText, timePrefix + result.elapsedSeconds.ToString("0.0") + SuffixSecond);
        SetText(wrongText, wrongPrefix + wrongCount + SuffixCount);
        SetText(resetCountText, resetCountPrefix + result.resetCount + SuffixCount);

        // 误操作分类统计展示（未绑定 categoryBreakdownText 时跳过）
        if (categoryBreakdownText != null)
        {
            string breakdown = BuildCategoryBreakdown(result);
            SetText(categoryBreakdownText, breakdown);
        }

        ShowResultPanel();

        Debug.Log($"[结果面板] {resultTitleText} | {timePrefix}{result.elapsedSeconds:0.0}{SuffixSecond} | {wrongPrefix}{wrongCount}{SuffixCount}");
    }

    /// <summary>
    /// 把结果中的误操作分类统计拼成可读文本。
    /// 例："电源系统 2 次  外壳 1 次"；无分类数据时返回空串。
    /// </summary>
    private static string BuildCategoryBreakdown(TrainingResult result)
    {
        if (result == null || result.wrongByCategory == null || result.wrongByCategory.Count == 0)
            return string.Empty;

        var parts = new System.Collections.Generic.List<string>(result.wrongByCategory.Count);
        for (int i = 0; i < result.wrongByCategory.Count; i++)
        {
            TrainingResult.WrongCategoryCount item = result.wrongByCategory[i];
            if (item == null || string.IsNullOrEmpty(item.category))
                continue;
            parts.Add($"{item.category} {item.count} 次");
        }

        return parts.Count > 0 ? string.Join("  ", parts) : string.Empty;
    }

    private void HandleTrainingReset()
    {
        HideResultPanel();
    }

    private void ShowResultPanel()
    {
        SetPanelActive(true);
    }

    private void HideResultPanel()
    {
        SetPanelActive(false);
    }

    private void SetPanelActive(bool active)
    {
        if (resultPanel == null)
            return;

        if (resultPanel.activeSelf == active)
            return;

        resultPanel.SetActive(active);
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