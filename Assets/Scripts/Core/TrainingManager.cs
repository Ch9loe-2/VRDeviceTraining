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

        Debug.Log($"当前培训步骤：{CurrentStep.stepName}");
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

        OnWrongOperation?.Invoke(expectedStepIndex);
    }

    private void MoveToNextStep()
    {
        currentStepIndex++;

        if (currentStepIndex >= steps.Count)
        {
            Debug.Log("【培训完成】所有步骤已经完成。");
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
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] != null)
                steps[i].completed = false;
        }

        currentStepIndex = 0;

        Debug.Log("【培训重置】已恢复到第一步");

        OnTrainingReset?.Invoke();
    }
}