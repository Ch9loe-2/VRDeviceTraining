using UnityEngine;

[System.Serializable]
public class TrainingStep
{
    [Header("步骤信息")]
    public string stepName;

    [TextArea]
    public string description;

    [Header("完成状态")]
    public bool completed;
}