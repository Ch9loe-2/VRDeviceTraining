using System.Collections.Generic;
using UnityEngine;

public class TrainingManager : MonoBehaviour
{
    [Header("培训步骤")]
    [SerializeField] private List<TrainingStep> steps = new List<TrainingStep>();

    private int currentStepIndex = 0;

    public int CurrentStepIndex => currentStepIndex;

    public int TotalSteps => steps.Count;

    /// <summary>
    /// 误操作事件。参数为「玩家尝试操作的那个零件所要求的步骤索引」（requiredStepIndex）。
    /// 只在玩家尝试操作非当前步骤的零件时触发，不改变任何步骤状态。
    /// </summary>
    public event System.Action<int> OnWrongOperation;

    /// <summary>
    /// 培训重置事件。TrainingManager 完成数据重置后广播，
    /// 由 PartInteractable（恢复零件）与 TrainingTaskPanelUI（刷新 UI）各自订阅。
    /// </summary>
    public event System.Action OnTrainingReset;

    /// <summary>
    /// 培训完成事件。走完最后一个步骤时广播一次，参数是本轮结果快照。
    /// 数据全部更新完毕后才广播，订阅者可以直接读取结果。
    /// </summary>
    public event System.Action<TrainingResult> OnTrainingCompleted;

    [Header("统计设置")]
    [Tooltip("两次误操作之间的最小计时间隔（秒）。用于防止双手同时抓取同一被锁定零件时被重复计数。")]
    [SerializeField] private float wrongOperationCooldown = 0.3f;

    // ===== 统计状态（M23-3）=====
    private float startTime;              // 本轮开始时刻（Time.time）
    private int wrongOperationCount;      // 本轮错误操作次数
    private int resetCount;               // 整个 Play 会话的累计重置次数
    private bool completed;               // 本轮是否已完成
    private float elapsedSeconds;         // 完成时冻结的耗时
    private float completionTime;         // 完成时刻的 Time.time

    // 上一次有效误操作的时刻；初值取 float 最小值，保证第一次误操作不会被误过滤
    private float lastWrongOperationTime = float.MinValue;

    /// <summary>本轮耗时（秒）。未完成实时计算，完成后返回冻结值。</summary>
    public float ElapsedSeconds => completed ? elapsedSeconds : Time.time - startTime;

    /// <summary>本轮错误操作次数。</summary>
    public int WrongOperationCount => wrongOperationCount;

    /// <summary>整个 Play 会话的累计重置次数。</summary>
    public int ResetCount => resetCount;

    /// <summary>本轮是否已经完成。</summary>
    public bool IsCompleted => completed;

    public float WrongOperationCooldown => wrongOperationCooldown;

    public TrainingStep CurrentStep
    {
        get
        {
            if (currentStepIndex < 0 || currentStepIndex >= steps.Count)
                return null;

            return steps[currentStepIndex];
        }
    }

    private void Start()
    {
        if (steps.Count == 0)
        {
            Debug.LogWarning("培训系统中还没有配置培训步骤。");
            return;
        }

        BeginRound();

        Debug.Log($"当前培训步骤：{CurrentStep.stepName}");
    }

    /// <summary>
    /// 开始新一轮统计。进入 Play 与每次重置都从这里走。
    /// 注意：resetCount 是会话累计量，不在这里清零。
    /// </summary>
    private void BeginRound()
    {
        startTime = Time.time;
        wrongOperationCount = 0;
        completed = false;
        elapsedSeconds = 0f;
        completionTime = 0f;
        lastWrongOperationTime = float.MinValue;
    }

    /// <summary>
    /// 生成本轮结果快照。UI 只读这个返回值，不要自己推算耗时。
    /// </summary>
    public TrainingResult BuildResult()
    {
        return new TrainingResult(
            completed,
            completed ? elapsedSeconds : Time.time - startTime,
            wrongOperationCount,
            resetCount,
            completionTime
        );
    }

    public void CompleteCurrentStep()
    {
        if (CurrentStep == null)
            return;

        if (CurrentStep.completed)
            return;

        CurrentStep.completed = true;

        Debug.Log($"【步骤完成】{CurrentStep.stepName}");

        MoveToNextStep();
    }

    /// <summary>
    /// 记录一次误操作（玩家尝试操作了非当前步骤的零件）。
    /// 只广播事件，不推进 currentStepIndex，不修改任何 TrainingStep.completed。
    /// </summary>
    /// <param name="expectedStepIndex">玩家尝试操作的零件所要求的步骤索引</param>
    public void RecordWrongOperation(int expectedStepIndex)
    {
        string stepLabel = (expectedStepIndex >= 0 && expectedStepIndex < steps.Count)
            ? steps[expectedStepIndex].stepName
            : $"第 {expectedStepIndex + 1} 步";

        Debug.Log($"【误操作】玩家尝试操作了非当前步骤的零件，应先完成：{stepLabel}（当前步骤索引 {currentStepIndex}）");

        // 全局去重：双手同时按住同一个被锁定零件时，两只手各会调用一次这里，
        // 冷却窗口内的后续调用仍会正常广播事件（不改动 UI 提示行为），但不重复计数。
        if (Time.time - lastWrongOperationTime < wrongOperationCooldown)
        {
            Debug.Log($"【误操作去重】距离上次误操作不足 {wrongOperationCooldown} 秒，本次不重复计数。");
            OnWrongOperation?.Invoke(expectedStepIndex);
            return;
        }

        lastWrongOperationTime = Time.time;
        wrongOperationCount++;

        Debug.Log($"【误操作计数】本轮累计 {wrongOperationCount} 次");

        OnWrongOperation?.Invoke(expectedStepIndex);
    }

    private void MoveToNextStep()
    {
        currentStepIndex++;

        if (currentStepIndex >= steps.Count)
        {
            // 先完成本轮数据更新，再广播事件，避免订阅者读到中间状态。
            completed = true;
            elapsedSeconds = Time.time - startTime;
            completionTime = Time.time;

            Debug.Log("【培训完成】所有步骤已经完成。");
            Debug.Log($"【培训结果】耗时 {elapsedSeconds:0.0} 秒，错误操作 {wrongOperationCount} 次，累计重置 {resetCount} 次");

            OnTrainingCompleted?.Invoke(BuildResult());
            return;
        }

        Debug.Log($"【下一步】{CurrentStep.stepName}");
    }

    /// <summary>
    /// 重置整个培训流程。
    /// 统一入口：先把所有步骤数据清空、索引回到第一步，再广播 OnTrainingReset，
    /// 由各个订阅者（零件、UI）负责恢复自己的局部状态。
    /// 顺序必须是「先数据、后广播」，否则零件会按旧的 currentStepIndex 重算锁定状态。
    /// </summary>
    public void ResetTraining()
    {
        resetCount++;

        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] != null)
                steps[i].completed = false;
        }

        currentStepIndex = 0;
        completed = false;
        wrongOperationCount = 0;
        elapsedSeconds = 0f;
        startTime = Time.time;
        lastWrongOperationTime = float.MinValue;
        completionTime = 0f;

        Debug.Log($"【培训重置】已恢复到第一步（累计重置 {resetCount} 次）");

        OnTrainingReset?.Invoke();
    }
}