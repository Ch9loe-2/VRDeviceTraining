using System.Collections;
using UnityEngine;

/// <summary>
/// AI 对话提供方接口 —— 核心抽象层。
///
/// 设计意图：
/// 通过接口解耦"LLM 提供方"与"培训系统"，上层只依赖本接口，
/// 底层可无缝切换：本地规则引擎 / DeepSeek / 豆包 / 任意大模型。
///
/// 简历亮点：依赖倒置 + 策略模式，新增 AI 提供方只需实现本接口。
/// </summary>
public interface IAIChatProvider
{
    /// <summary>
    /// 发起一次 AI 问答。
    /// </summary>
    /// <param name="systemPrompt">系统提示词（角色设定与约束）</param>
    /// <param name="userMessage">用户提问 / 当前上下文</param>
    /// <param name="onResult">回答完成后回调（在主线程执行）</param>
    IEnumerator AskAsync(string systemPrompt, string userMessage, System.Action<string> onResult);

    /// <summary>提供方名称（用于日志与 UI 展示）。</summary>
    string ProviderName { get; }
}
