using System.Collections.Generic;
using UnityEngine;

public class TrainingManager : MonoBehaviour
{
    [Header("培训步骤")]
    [SerializeField] private List<TrainingStep> steps = new List<TrainingStep>();

    private int currentStepIndex = 0;

    public int CurrentStepIndex => currentStepIndex;

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
}