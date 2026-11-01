using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// DeepSeek 大模型提供方（真实 API 模式，可选）。
///
/// 说明：
/// - 需要 API Key（platform.deepseek.com 注册获取，sk- 开头）
/// - Key 通过 AITrainingAssistant 的 Inspector 字段注入，或本地配置加载，
///   绝不硬编码在代码里；仓库内不包含任何真实 Key。
/// - Key 为空 / 请求失败时，自动回退到 LocalAIChatProvider（本地规则引擎），
///   保证演示永不中断。
/// </summary>
public class DeepSeekChatProvider : IAIChatProvider
{
    private const string ApiEndpoint = "https://api.deepseek.com/chat/completions";
    private const string ModelName = "deepseek-chat";

    /// <summary>API Key（由外部注入，不进场景序列化提交）。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>请求失败时的回退提供方（默认本地规则引擎）。</summary>
    public IAIChatProvider FallbackProvider { get; set; }

    public string ProviderName => "DeepSeek 大模型";

    public IEnumerator AskAsync(string systemPrompt, string userMessage, System.Action<string> onResult)
    {
        // Key 缺失 → 回退本地引擎，保证演示可用
        if (string.IsNullOrEmpty(ApiKey))
        {
            Debug.LogWarning("[DeepSeek] 未配置 API Key，回退到本地规则引擎。");
            if (FallbackProvider != null)
            {
                yield return FallbackProvider.AskAsync(systemPrompt, userMessage, onResult);
                yield break;
            }

            onResult?.Invoke("（未配置 DeepSeek API Key，且无回退引擎）");
            yield break;
        }

        // 构造请求体（JsonUtility 要求顶层为可序列化对象）
        ChatRequest request = new ChatRequest
        {
            model = ModelName,
            messages = new ChatMessage[]
            {
                new ChatMessage { role = "system", content = systemPrompt },
                new ChatMessage { role = "user", content = userMessage }
            }
        };

        string jsonBody = JsonUtility.ToJson(request);
        Debug.Log($"[DeepSeek] 请求 {ApiEndpoint}，模型 {ModelName}");

        using (UnityWebRequest web = new UnityWebRequest(ApiEndpoint, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            web.uploadHandler = new UploadHandlerRaw(body);
            web.downloadHandler = new DownloadHandlerBuffer();
            web.SetRequestHeader("Content-Type", "application/json");
            web.SetRequestHeader("Authorization", "Bearer " + ApiKey);
            web.timeout = 30;

            yield return web.SendWebRequest();

            if (web.result == UnityWebRequest.Result.Success)
            {
                string reply = ParseReply(web.downloadHandler.text);
                onResult?.Invoke(reply);
            }
            else
            {
                Debug.LogWarning($"[DeepSeek] 请求失败：{web.error}（回退本地引擎）。");
                if (FallbackProvider != null)
                    yield return FallbackProvider.AskAsync(systemPrompt, userMessage, onResult);
                else
                    onResult?.Invoke($"（DeepSeek 请求失败：{web.error}）");
            }
        }
    }

    private static string ParseReply(string json)
    {
        try
        {
            ChatResponse resp = JsonUtility.FromJson<ChatResponse>(json);
            if (resp != null && resp.choices != null && resp.choices.Length > 0
                && resp.choices[0].message != null)
                return resp.choices[0].message.content;

            return "（DeepSeek 返回格式异常）";
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[DeepSeek] 解析响应失败：{e.Message}");
            return "（DeepSeek 响应解析失败）";
        }
    }

    // ==================== 请求/响应模型 ====================

    [System.Serializable]
    private class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
    }

    [System.Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;
    }

    [System.Serializable]
    private class ChatResponse
    {
        public Choice[] choices;
    }

    [System.Serializable]
    private class Choice
    {
        public ChatMessage message;
    }
}
