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
