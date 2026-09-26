using System.Collections.Generic;
using UnityEngine;

/// <summary>培训阶段。</summary>
public enum TrainingPhase
{
    /// <summary>拆卸阶段：按正向步骤顺序拆卸零件。</summary>
    Disassembly,
    /// <summary>组装阶段：按逆向步骤顺序装回零件。</summary>
    Assembly,
    /// <summary>培训已完成。</summary>
    Completed
}

public class TrainingManager : MonoBehaviour
{
    [Header("培训步骤（拆卸顺序）")]
    [SerializeField] private List<TrainingStep> steps = new List<TrainingStep>();

    private int currentStepIndex = 0;

    public int CurrentStepIndex => currentStepIndex;

    /// <summary>总步骤数 = 拆卸步骤数 + 组装步骤数（= steps.Count * 2）。</summary>
    public int TotalSteps => steps.Count * 2;

    /// <summary>纯拆卸步骤数。</summary>
    public int DisassemblyStepCount => steps.Count;

    /// <summary>当前培训阶段。</summary>
    public TrainingPhase CurrentPhase
    {
        get
        {
            if (completed) return TrainingPhase.Completed;
            if (currentStepIndex < steps.Count) return TrainingPhase.Disassembly;
            return TrainingPhase.Assembly;
        }
    }

    /// <summary>获取指定统一步骤索引的名称。拆卸 / 组装步骤自动生成。</summary>
    public string GetStepName(int stepIndex)
    {
        int half = steps.Count;
        if (stepIndex < half)
            return steps[stepIndex].StepName;
        else if (stepIndex < half * 2)
        {
            int partIdx = half - 1 - (stepIndex - half);
            string partName = ExtractPartName(partIdx);
            return "组装 " + partName;
        }
        return $"第 {stepIndex + 1} 步";
    }

    /// <summary>
    /// 判断指定零件（由其 requiredStepIndex 标识）是否为当前步骤的目标。
    /// 拆卸阶段：target == requiredStepIndex
    /// 组装阶段：target == reversed requiredStepIndex
    /// </summary>
    public bool IsPartCurrentStep(int partRequiredStepIndex)
    {
        if (completed || currentStepIndex >= steps.Count * 2)
            return false;

        int half = steps.Count;
        if (currentStepIndex < half)
            return currentStepIndex == partRequiredStepIndex;
        else
            return (half - 1 - (currentStepIndex - half)) == partRequiredStepIndex;
    }

    /// <summary>误操作事件。</summary>
    public event System.Action<int> OnWrongOperation;

    /// <summary>培训重置事件。</summary>
    public event System.Action OnTrainingReset;

    /// <summary>培训完成事件。</summary>
    public event System.Action<TrainingResult> OnTrainingCompleted;

    /// <summary>阶段切换事件。当前阶段发生变化时触发。</summary>
    public event System.Action<TrainingPhase> OnPhaseChanged;

    [Header("统计设置")]
    [Tooltip("两次误操作之间的最小计时间隔（秒）。")]
    [SerializeField] private float wrongOperationCooldown = 0.3f;

    // ===== 统计状态 =====
    private float startTime;
    private int wrongOperationCount;
    private int resetCount;
    private bool completed;
    private float elapsedSeconds;
    private float completionTime;
    private float lastWrongOperationTime = float.MinValue;
    private TrainingPhase lastPhase = TrainingPhase.Disassembly;

    public float ElapsedSeconds => completed ? elapsedSeconds : Time.time - startTime;
    public int WrongOperationCount => wrongOperationCount;
    public int ResetCount => resetCount;
    public bool IsCompleted => completed;
    public float WrongOperationCooldown => wrongOperationCooldown;

    /// <summary>当前步骤。统一索引映射到底层步骤对象（组装阶段返回逆向映射）。</summary>
    public TrainingStep CurrentStep
    {
        get
        {
            int maxIndex = steps.Count * 2;
            if (completed || currentStepIndex < 0 || currentStepIndex >= maxIndex)
                return null;

            int underlyingIndex;
            if (currentStepIndex < steps.Count)
                underlyingIndex = currentStepIndex;
            else
                underlyingIndex = steps.Count - 1 - (currentStepIndex - steps.Count);

            if (underlyingIndex < 0 || underlyingIndex >= steps.Count)
                return null;

            return steps[underlyingIndex];
        }
    }

    /// <summary>供测试 / 工具代码在运行时动态配置拆卸步骤。</summary>
    public void ConfigureSteps(List<TrainingStep> newSteps)
    {
        steps = newSteps;
        currentStepIndex = 0;
        if (steps.Count > 0)
            BeginRound();
    }

    private void Start()
    {
        if (steps.Count == 0)
        {
            Debug.LogWarning("培训系统中还没有配置培训步骤。");
            return;
        }

        BeginRound();

        Debug.Log($"当前培训步骤：{GetStepName(currentStepIndex)}");
    }

    private void BeginRound()
    {
        startTime = Time.time;
        wrongOperationCount = 0;
        completed = false;
        elapsedSeconds = 0f;
        completionTime = 0f;
        lastWrongOperationTime = float.MinValue;
        lastPhase = TrainingPhase.Disassembly;
    }

    /// <summary>生成本轮结果快照。</summary>
    public TrainingResult BuildResult()
    {
        float elapsed = completed ? elapsedSeconds : Time.time - startTime;

        TrainingResult result = new TrainingResult(
            completed,
            elapsed,
            wrongOperationCount,
            resetCount,
            completionTime
        );
        result.totalSteps = steps.Count * 2;
        result.correctSteps = steps.Count * 2;

        if (completed)
        {
            int score = TrainingScoring.CalculateScore(elapsed, wrongOperationCount, resetCount);
            result.SetScore(score, TrainingScoring.GetGrade(score));
        }

        return result;
    }

    /// <summary>读取最近一次保存的培训结果。</summary>
    public TrainingResult LoadLastResult()
    {
        return TrainingResultPersistence.LoadLast();
    }

    /// <summary>
    /// 完成当前步骤（不依赖步骤对象自身的 Complete() 调用，直接推进统一步骤索引）。
    /// 拆卸与组装阶段均通过此方法推进，上级调用者（PartInteractable）负责检测合法的操作。
    /// </summary>
    public void CompleteCurrentStep()
    {
        if (completed)
            return;

        Debug.Log($"【步骤完成】{GetStepName(currentStepIndex)}");
        MoveToNextStep();
    }

    /// <summary>
    /// 记录一次误操作。
    /// </summary>
    public void RecordWrongOperation(int expectedStepIndex)
    {
        string stepLabel = (expectedStepIndex >= 0 && expectedStepIndex < steps.Count)
            ? steps[expectedStepIndex].StepName
            : $"第 {expectedStepIndex + 1} 步";

        Debug.Log($"【误操作】玩家尝试操作了非当前步骤的零件，应先完成：{stepLabel}（当前步骤索引 {currentStepIndex}）");

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
        int maxIndex = steps.Count * 2;

        // 检查阶段切换
        TrainingPhase newPhase = CurrentPhase;
        if (newPhase != lastPhase)
        {
            Debug.Log($"【阶段切换】{lastPhase} → {newPhase}");
            lastPhase = newPhase;
            OnPhaseChanged?.Invoke(newPhase);
        }

        if (currentStepIndex >= maxIndex)
        {
            completed = true;
            elapsedSeconds = Time.time - startTime;
            completionTime = Time.time;

            Debug.Log("【培训完成】所有拆卸与组装步骤已经完成。");
            Debug.Log($"【培训结果】耗时 {elapsedSeconds:0.0} 秒，错误操作 {wrongOperationCount} 次，累计重置 {resetCount} 次");

            TrainingResult result = BuildResult();
            OnTrainingCompleted?.Invoke(result);
            TrainingResultPersistence.Save(result);
            return;
        }

        Debug.Log($"【下一步】{GetStepName(currentStepIndex)}");
    }

    /// <summary>
    /// 重置整个培训流程。
    /// </summary>
    public void ResetTraining()
    {
        resetCount++;

        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] != null)
                steps[i].Reset();
        }

        currentStepIndex = 0;
        completed = false;
        wrongOperationCount = 0;
        elapsedSeconds = 0f;
        startTime = Time.time;
        lastWrongOperationTime = float.MinValue;
        completionTime = 0f;
        lastPhase = TrainingPhase.Disassembly;

        Debug.Log($"【培训重置】已恢复到第一步（累计重置 {resetCount} 次）");

        OnTrainingReset?.Invoke();
    }

    /// <summary>
    /// 从拆卸步骤名称中提取纯零件名。
    /// 例如 "拆卸 Battery" → "Battery"，"拆卸后盖" → "后盖"。
    /// </summary>
    private string ExtractPartName(int partIndex)
    {
        if (partIndex < 0 || partIndex >= steps.Count || steps[partIndex] == null)
            return $"零件{partIndex + 1}";

        string raw = steps[partIndex].StepName;
        // 去掉前2个字符"拆卸"
        string partOnly = raw.Length >= 2 ? raw.Substring(2) : raw;
        return partOnly.Trim();
    }
}