using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// AI 培训指导助手 —— 主控制器。
///
/// 能力：
/// 1. 运行时自动生成 AI 指导面板（挂在场景 TrainingCanvas 下，零场景改动）；
/// 2. 订阅培训事件（误操作/阶段切换/完成），自动推送实时指导；
/// 3. 快捷提问按钮（VR 中无需键盘输入）；
/// 4. 双模式提供方：本地规则引擎（默认，无需 Key）/ DeepSeek 大模型（可选）。
///
/// 场景接线（2 步）：
/// 1. 新建空物体（或挂到 TrainingManager 同一物体）挂本脚本；
/// 2. Inspector 拖入 TrainingManager（留空则自动查找场景中的）。
///
/// DeepSeek 模式：
/// - 编辑 Assets/Resources/DeepSeekConfig.json 填入 apiKey（该文件已提交但 Key 留空，注意不要提交真实 Key）；
/// - 或运行时在 Inspector 的 DeepSeek ApiKey 字段输入（不进场景序列化提交）。
/// </summary>
public class AITrainingAssistant : MonoBehaviour
{
    [Header("培训系统引用（留空自动查找）")]
    [SerializeField] private TrainingManager trainingManager;

    [Header("AI 提供方")]
    [Tooltip("false=本地规则引擎（默认，无需 Key）；true=DeepSeek 大模型（需 Key，失败自动回退本地）")]
    [SerializeField] private bool useDeepSeek = false;

    [Header("DeepSeek 配置（可选，仅 useDeepSeek=true 时生效）")]
    [Tooltip("运行时注入的 API Key，不进场景序列化；留空则从 Resources/DeepSeekConfig.json 读取")]
    [SerializeField] private string deepSeekApiKey = "";

    [Header("AI 面板 UI 样式")]
    [SerializeField] private Font uiFont;
    [SerializeField] private float panelWidth = 520f;
    [SerializeField] private float panelHeight = 320f;

    private IAIChatProvider provider;
    private readonly Queue<string> pendingQuestions = new Queue<string>();
    private bool busy = false;

    // 运行时生成的 UI 引用
    private Text answerText;
    private Text providerLabel;

    private void Awake()
    {
        if (trainingManager == null)
            trainingManager = FindObjectOfType<TrainingManager>();

        if (trainingManager == null)
        {
            Debug.LogWarning("[AI助手] 未找到 TrainingManager，AI 指导面板将无法获取培训状态。", this);
        }

        SetupProvider();
        BuildPanel();

        // 订阅培训事件，自动推送指导
        if (trainingManager != null)
        {
            trainingManager.OnWrongOperation += OnWrongOperation;
            trainingManager.OnPhaseChanged += OnPhaseChanged;
            trainingManager.OnTrainingCompleted += OnTrainingCompleted;
            trainingManager.OnTrainingReset += OnTrainingReset;
        }

        Debug.Log($"[AI助手] 初始化完成，提供方：{provider.ProviderName}", this);
    }

    private void OnDestroy()
    {
        if (trainingManager != null)
        {
            trainingManager.OnWrongOperation -= OnWrongOperation;
            trainingManager.OnPhaseChanged -= OnPhaseChanged;
            trainingManager.OnTrainingCompleted -= OnTrainingCompleted;
            trainingManager.OnTrainingReset -= OnTrainingReset;
        }
    }

    private void SetupProvider()
    {
        // 本地规则引擎（始终可用的回退）
        LocalAIChatProvider local = new LocalAIChatProvider(trainingManager);

        if (useDeepSeek)
        {
            DeepSeekChatProvider deep = new DeepSeekChatProvider
            {
                ApiKey = ResolveApiKey(),
                FallbackProvider = local
            };
            provider = deep;
        }
        else
        {
            provider = local;
        }
    }

    /// <summary>优先使用 Inspector 注入的 Key；否则读 Resources/DeepSeekConfig.json。</summary>
    private string ResolveApiKey()
    {
        if (!string.IsNullOrEmpty(deepSeekApiKey))
            return deepSeekApiKey;

        TextAsset config = Resources.Load<TextAsset>("DeepSeekConfig");
        if (config == null)
            return "";

        try
        {
            DeepSeekConfig json = JsonUtility.FromJson<DeepSeekConfig>(config.text);
            return json != null ? json.apiKey : "";
        }
        catch (System.Exception)
        {
            return "";
        }
    }

    // ==================== 公开 API ====================

    /// <summary>向 AI 提问（供快捷按钮 / 外部脚本 / 测试调用）。</summary>
    public void Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return;

        pendingQuestions.Enqueue(question);

        if (!busy)
            StartCoroutine(ProcessQueue());
    }

    [ContextMenu("问：我现在该做什么")]
    public void AskWhatToDo()
    {
        Ask("我现在该做什么？");
    }

    [ContextMenu("问：刚才为什么报错")]
    public void AskWhyWrong()
    {
        Ask("我刚才为什么报错？");
    }

    [ContextMenu("问：下一步怎么操作")]
    public void AskNextStep()
    {
        Ask("下一步怎么操作？");
    }

    // ==================== 事件响应 ====================

    private void OnWrongOperation(int expectedStepIndex)
    {
        AppendAnswer("检测到误操作，正在分析原因…");
        Ask("我刚才为什么报错？");
    }

    private void OnPhaseChanged(TrainingPhase newPhase)
    {
        string phaseName = newPhase == TrainingPhase.Assembly ? "组装阶段" : "拆卸阶段";
        AppendAnswer($"已进入【{phaseName}】，为你更新操作指引。");
        Ask("我现在该做什么？");
    }

    private void OnTrainingCompleted(TrainingResult result)
    {
        AppendAnswer("培训完成！正在为你总结…");
        Ask("培训完成了，总结一下我的表现。");
    }

    private void OnTrainingReset()
    {
        AppendAnswer("培训已重置，开始新一轮训练。");
        Ask("我现在该做什么？");
    }

    // ==================== 内部处理 ====================

    private IEnumerator ProcessQueue()
    {
        busy = true;

        while (pendingQuestions.Count > 0)
        {
            string question = pendingQuestions.Dequeue();
            string systemPrompt = BuildSystemPrompt();

            AppendAnswer($"正在思考…（{provider.ProviderName}）");

            string reply = null;
            yield return provider.AskAsync(systemPrompt, question, r => reply = r);

            if (!string.IsNullOrEmpty(reply))
                AppendAnswer(reply);
            else
                AppendAnswer("（AI 未返回有效回答，请重试）");

            yield return new WaitForSeconds(0.2f);
        }

        busy = false;
    }

    /// <summary>系统提示词：注入培训系统实时状态，让 AI 掌握上下文。</summary>
    private string BuildSystemPrompt()
    {
        if (trainingManager == null)
            return "你是 VR 设备拆装培训系统的 AI 指导老师。";

        string taskName = string.IsNullOrEmpty(trainingManager.CurrentTaskName)
            ? "默认任务" : trainingManager.CurrentTaskName;
        string phase = trainingManager.CurrentPhase == TrainingPhase.Assembly
            ? "组装阶段" : trainingManager.CurrentPhase == TrainingPhase.Completed
                ? "已完成" : "拆卸阶段";
        string stepName = trainingManager.IsCompleted
            ? "（无）" : trainingManager.GetStepName(trainingManager.CurrentStepIndex);

        var wrongParts = new List<string>();
        IReadOnlyDictionary<string, int> byCategory = trainingManager.WrongOperationByCategory;
        if (byCategory != null)
        {
            foreach (var kv in byCategory)
                wrongParts.Add($"{kv.Key}×{kv.Value}");
        }

        string wrongDesc = wrongParts.Count > 0 ? string.Join("，", wrongParts.ToArray()) : "无";

        return $"你是 VR 设备拆装培训系统的 AI 指导老师，用简洁中文回答。\n" +
               $"当前任务：{taskName}；阶段：{phase}；当前步骤：{stepName}；" +
               $"误操作分类统计：{wrongDesc}。\n" +
               "回答要结合以上培训状态给出具体操作建议，不要泛泛而谈。";
    }

    // ==================== 面板 UI 生成 ====================

    private void BuildPanel()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[AI助手] 场景中未找到 Canvas，跳过面板创建（AI 仍可通过日志输出回答）。", this);
            return;
        }

        // 根：AIPanel
        GameObject root = new GameObject("AIPanel", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.02f, 0.5f);
        rootRt.anchorMax = new Vector2(0.02f, 0.5f);
        rootRt.pivot = new Vector2(0f, 0.5f);
        rootRt.anchoredPosition = Vector2.zero;
        rootRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        root.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.16f, 0.92f);

        // 标题
        Text title = CreateText(root.transform, "AITitle", new Vector2(0f, -28f),
            new Vector2(panelWidth, 36f), "AI 指导老师", 24, TextAnchor.MiddleLeft);

        // 提供方标签
        providerLabel = CreateText(root.transform, "AIProvider", new Vector2(0f, -56f),
            new Vector2(panelWidth, 24f), "引擎：" + provider.ProviderName, 14, TextAnchor.MiddleLeft);
        providerLabel.color = new Color(0.5f, 0.8f, 1f, 1f);

        // 回答文本（可滚动区域省略，直接多行显示）
        answerText = CreateText(root.transform, "AIAnswer", new Vector2(0f, -84f),
            new Vector2(panelWidth - 24f, panelHeight - 120f), "你好，我是你的 AI 培训指导老师。有问题随时问我！", 16, TextAnchor.UpperLeft);
        answerText.horizontalOverflow = HorizontalWrapMode.Wrap;
        answerText.verticalOverflow = VerticalWrapMode.Overflow;

        // 快捷提问按钮
        CreateButton(root.transform, "AskWhatBtn", new Vector2(-80f, -panelHeight + 30f),
            new Vector2(150f, 40f), "我该做什么？", () => AskWhatToDo());
        CreateButton(root.transform, "AskWrongBtn", new Vector2(80f, -panelHeight + 30f),
            new Vector2(150f, 40f), "为什么报错？", () => AskWhyWrong());
    }

    private Text CreateText(Transform parent, string name, Vector2 pos, Vector2 size,
        string content, int fontSize, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Text text = go.GetComponent<Text>();
        text.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = Color.white;
        text.text = content;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private void CreateButton(Transform parent, string name, Vector2 pos, Vector2 size,
        string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.5f, 0.85f, 1f);

        Button btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        Text text = CreateText(go.transform, "Label", Vector2.zero, size, label, 16, TextAnchor.MiddleCenter);
        text.raycastTarget = false;
    }

    private void AppendAnswer(string text)
    {
        if (answerText == null)
        {
            Debug.Log($"[AI助手] {text}");
            return;
        }

        answerText.text = text;
    }
}

/// <summary>DeepSeek 本地配置结构（Assets/Resources/DeepSeekConfig.json）。</summary>
[System.Serializable]
public class DeepSeekConfig
{
    public string apiKey = "";
}
