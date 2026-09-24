using UnityEngine;

[System.Serializable]
public class TrainingStep
{
    [Header("步骤信息")]
    [SerializeField] private string stepName;

    [TextArea]
    [SerializeField] private string description;

    [Header("完成状态")]
    [SerializeField] private bool completed;

    public string StepName => stepName;
    public string Description => description;
    public bool IsCompleted => completed;

    public void Complete()
    {
        completed = true;
    }

    public void Reset()
    {
        completed = false;
    }
}