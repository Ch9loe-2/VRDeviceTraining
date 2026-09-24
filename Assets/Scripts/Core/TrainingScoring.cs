using UnityEngine;

/// <summary>
/// 培训评分计算模块（无状态、纯函数）。
///
/// 独立于 UI、Manager、零件等所有运行时组件，
/// 可在 Editor 测试中直接调用，无需进入 Play Mode。
/// </summary>
public static class TrainingScoring
{
    // ===== 评分常量 =====
    private const int BaseScore = 60;                // 基础完成分
    private const float MaxTimeScore = 25f;          // 时间表现最高分
    private const float BaselineTime = 60f;          // 基准时间（秒）
    private const float MaxTimeThreshold = 180f;     // 时间分归零阈值（秒）
    private const int MaxOperationScore = 15;        // 操作规范最高分
    private const int WrongOpPenalty = 2;            // 每次错误操作扣分
    private const int ResetPenalty = 3;              // 每次重置扣分
    private const int MinScore = 0;
    private const int MaxScore = 100;

    /// <summary>
    /// 计算培训总评分（0~100）。
    /// </summary>
    public static int CalculateScore(float elapsedSeconds, int wrongOperationCount, int resetCount)
    {
        // 防御：非法时间按 0 处理
        if (float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds))
            elapsedSeconds = 0f;

        // 防御：负数按 0 处理
        float safeTime = Mathf.Max(0f, elapsedSeconds);
        int safeWrong = Mathf.Max(0, wrongOperationCount);
        int safeReset = Mathf.Max(0, resetCount);

        // 时间表现分（保留浮点精度，最后统一舍入）
        float timeScore;
        if (safeTime <= BaselineTime)
            timeScore = MaxTimeScore;
        else if (safeTime < MaxTimeThreshold)
            timeScore = MaxTimeScore * (1f - (safeTime - BaselineTime) / (MaxTimeThreshold - BaselineTime));
        else
            timeScore = 0f;

        // 操作规范分
        int operationScore = MaxOperationScore
                             - safeWrong * WrongOpPenalty
                             - safeReset * ResetPenalty;
        operationScore = Mathf.Max(0, operationScore);

        // 总分 = 基础分 + 时间分 + 操作分，最后统一舍入
        float rawTotal = BaseScore + timeScore + operationScore;
        int finalScore = Mathf.RoundToInt(rawTotal);

        // 最终限制
        return Mathf.Clamp(finalScore, MinScore, MaxScore);
    }

    /// <summary>
    /// 获取评价等级。
    /// </summary>
    public static string GetGrade(int score)
    {
        if (score >= 90) return "优秀";
        if (score >= 80) return "良好";
        if (score >= 60) return "合格";
        return "待提升";
    }
}