using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 培训结果面板 UI：培训完成后显示一轮统计结果。
///
/// 只读 TrainingManager 与 TrainingResult，不计算任何统计数据：
/// 耗时、错误次数、重置次数全部由 TrainingManager 提供，这里只做格式化显示。
///
/// 本脚本所在的 GameObject 必须保持激活（挂在 TrainingCanvas 上），
/// 因为 ResultPanel 自身是隐藏的，脚本不能挂在会被 SetActive(false) 的物体上，
/// 否则收不到 OnTrainingCompleted 事件、无法自己显示出来。
///
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

        // 先取消再订阅，避免 Enabling/Disabling 反复触发时重复订阅
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

    /// <summary>
    /// 培训完成：填数据并立即显示面板。结果对象里的数值一律直接采用。
    /// </summary>
    private void HandleTrainingCompleted(TrainingResult result)
    {
        if (result == null)
            return;

        SetText(resultTitle, resultTitleText);
        SetText(statusText, result.isCompleted ? statusCompleted : statusNotCompleted);
        SetText(timeText, timePrefix + result.elapsedSeconds.ToString("0.0") + SuffixSecond);
        SetText(wrongText, wrongPrefix + result.wrongOperationCount + SuffixCount);
        SetText(resetCountText, resetCountPrefix + result.resetCount + SuffixCount);

        ShowResultPanel();

        Debug.Log($"[结果面板] {resultTitleText} | {timePrefix}{result.elapsedSeconds:0.0}{SuffixSecond} | {wrongPrefix}{result.wrongOperationCount}{SuffixCount} | {resetCountPrefix}{result.resetCount}{SuffixCount}");
    }

    /// <summary>
    /// 培训重置：立刻隐藏结果面板（本组件挂在常驻物体上，不受隐藏影响）。
    /// </summary>
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
