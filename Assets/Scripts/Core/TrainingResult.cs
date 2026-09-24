using System;
using UnityEngine;

/// <summary>
/// 培训结果快照（纯数据）。
///
/// 只负责承载一次完成事件的数据，不做任何业务计算、不引用任何 MonoBehaviour。
/// 所有数值由 TrainingManager.BuildResult() 生成，UI 只读不写。
/// </summary>
[Serializable]
public class TrainingResult
{
    /// <summary>本轮是否已经完成最后一个步骤。</summary>
    public bool isCompleted;

    /// <summary>本轮耗时（秒）。完成时是冻结值，未完成时是实时值。</summary>
    public float elapsedSeconds;

    /// <summary>本轮错误操作次数。</summary>
    public int wrongOperationCount;

    /// <summary>整个 Play 会话的累计重置次数（重置本身不会把它清零）。</summary>
    public int resetCount;

    /// <summary>完成时刻的 Time.time 绝对值，仅用于调试与展示。</summary>
    public float completionTime;

    /// <summary>本轮最终评分（0~100）。只在完成时有意义，未完成时为 0。</summary>
    private int score;

    /// <summary>本轮评价等级。只在完成时有意义，未完成时为 null。</summary>
    private string grade;

    /// <summary>本轮最终评分（0~100）。未完成时为 0。</summary>
    public int Score => score;

    /// <summary>本轮评价等级。未完成时为 null。</summary>
    public string Grade => grade;

    /// <summary>
    /// 写入评分结果。只在培训完成时由 TrainingManager 调用一次。
    /// 生产周期：TrainingManager.BuildResult() → TrainingScoring → SetScore() → 广播给 UI。
    /// </summary>
    public void SetScore(int score, string grade)
    {
        this.score = score;
        this.grade = grade;
    }

    public TrainingResult()
    {
    }

    public TrainingResult(bool isCompleted, float elapsedSeconds, int wrongOperationCount, int resetCount, float completionTime)
    {
        this.isCompleted = isCompleted;
        this.elapsedSeconds = elapsedSeconds;
        this.wrongOperationCount = wrongOperationCount;
        this.resetCount = resetCount;
        this.completionTime = completionTime;
    }
}
