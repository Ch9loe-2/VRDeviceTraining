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

        ShowResultPanel();

        Debug.Log($"[结果面板] {resultTitleText} | {timePrefix}{result.elapsedSeconds:0.0}{SuffixSecond} | {wrongPrefix}{wrongCount}{SuffixCount}");
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