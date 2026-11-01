using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 本地规则引擎 AI 提供方（默认模式，无需 API Key）。
///
/// 不依赖任何外部服务，根据培训系统实时状态（阶段/当前步骤/误操作统计/任务名）
/// 生成操作指导。用于无网络环境演示、离线开发与 CI 测试。
///
/// 真实大模型模式（DeepSeekChatProvider）就绪后，只需在
/// AITrainingAssistant 中切换 Provider，上层逻辑完全不变。
/// </summary>
public class LocalAIChatProvider : IAIChatProvider
{
    private readonly TrainingManager manager;

    public string ProviderName => "本地规则引擎";

    public LocalAIChatProvider(TrainingManager manager)
    {
        this.manager = manager;
    }

    public IEnumerator AskAsync(string systemPrompt, string userMessage, System.Action<string> onResult)
    {
        // 本地引擎：同步生成回答，包一层协程以保持接口契约一致
        yield return null;
        onResult?.Invoke(BuildAnswer(systemPrompt, userMessage));
    }

    /// <summary>
    /// 根据培训系统实时状态生成指导文本。
    /// 上下文 = 当前任务名 + 当前阶段 + 当前步骤 + 误操作分类统计。
    /// </summary>
    private string BuildAnswer(string systemPrompt, string userMessage)
    {
        if (manager == null)
            return "（未绑定培训系统，无法获取当前状态）";

        string taskName = string.IsNullOrEmpty(manager.CurrentTaskName) ? "默认任务" : manager.CurrentTaskName;
        string phase = DescribePhase();
        string stepName = manager.IsCompleted ? "（已完成）" : manager.GetStepName(manager.CurrentStepIndex);
        string wrongDesc = DescribeWrongOperations();

        // 用户提问分类：当前该做什么 / 为什么报错 / 通用
        string lower = (userMessage ?? string.Empty).ToLowerInvariant();
        string answer;

        if (lower.Contains("做") || lower.Contains("干什么") || lower.Contains("下一步"))
        {
            answer = BuildWhatToDo(taskName, phase, stepName);
        }
        else if (lower.Contains("错") || lower.Contains("报错") || lower.Contains("不行") || lower.Contains("不能"))
        {
            answer = BuildWrongReason(wrongDesc, stepName);
        }
        else
        {
            answer = $"【{taskName}】当前阶段：{phase}。{stepName}。{wrongDesc}";
        }

        return answer;
    }

    private string DescribePhase()
    {
        if (manager == null)
            return "未知";
        switch (manager.CurrentPhase)
        {
            case TrainingPhase.Disassembly: return "拆卸阶段";
            case TrainingPhase.Assembly:    return "组装阶段";
            case TrainingPhase.Completed:   return "已完成";
            default:                        return "未知";
        }
    }

    private string BuildWhatToDo(string taskName, string phase, string stepName)
    {
        if (manager.IsCompleted)
            return $"培训已完成！本任务（{taskName}）的拆装流程全部通过，可以点击重置开始新一轮训练。";

        return $"在【{phase}】中，你的当前目标是：{stepName}。\n" +
               "操作提示：用手柄射线对准目标零件，按下抓取键（Grip/Select）握住它，然后将其从设备上移开（移出判定距离即可完成拆卸）。\n" +
               "注意：只有当前步骤的零件可以被操作，其他零件会被系统锁定。";
    }

    private string BuildWrongReason(string wrongDesc, string stepName)
    {
        if (string.IsNullOrEmpty(wrongDesc))
            return $"当前没有误操作记录。你的任务是完成：{stepName}。请按步骤顺序操作，不要提前抓取未到步骤的零件。";

        return $"已统计到误操作：{wrongDesc}\n" +
               "原因分析：你尝试操作的零件不在当前步骤。系统按步骤顺序锁定零件，只有\"当前步骤\"的零件可以抓取。\n" +
               $"请先完成当前目标：{stepName}，再进行下一步。";
    }

    private string DescribeWrongOperations()
    {
        if (manager == null)
            return string.Empty;

        IReadOnlyDictionary<string, int> byCategory = manager.WrongOperationByCategory;
        if (byCategory == null || byCategory.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var kv in byCategory)
            parts.Add($"{kv.Key}×{kv.Value}");

        return "误操作分类：" + string.Join("，", parts.ToArray()) + "。";
    }
}
