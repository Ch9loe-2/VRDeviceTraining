using UnityEngine;

[System.Serializable]
public class TrainingStep
{
    [Header("步骤信息")]
    [SerializeField] private string stepName;

    [Header("完成状态")]
    [SerializeField] private bool completed;

    /// <summary>供代码 / 测试按名称配置步骤（序列化仍走默认无参构造 + 字段填充，不受影响）。</summary>
    public TrainingStep(string name)
    {
        stepName = name;
    }

    public string StepName => stepName;
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