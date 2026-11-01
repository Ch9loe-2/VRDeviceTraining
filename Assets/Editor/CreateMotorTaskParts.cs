using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// 一键生成"电机拆装培训"任务所需的 Fan / Motor 零件。
///
/// 用法：菜单栏 Tools → VRDeviceTraining → 一键生成电机任务零件
/// 生成后：
/// - 在 Parts 节点下创建 Fan（圆柱）、Motor（立方体）
/// - 自动挂 PartInteractable + XRGrabInteractable + Rigidbody + Collider
/// - taskId 预设为 task_motor，与 TrainingConfig.csv 中 task_motor 任务匹配
/// - 自动绑定场景中的 TrainingManager
/// </summary>
public static class CreateMotorTaskParts
{
    private const string TaskId = "task_motor";
    private const string PartsRootName = "Parts";

    [MenuItem("Tools/VRDeviceTraining/一键生成电机任务零件")]
    public static void Create()
    {
        // 1. 找到 Parts 父节点（Battery/BackCover 都挂在它下面）
        Transform partsRoot = GameObject.Find(PartsRootName)?.transform;
        if (partsRoot == null)
        {
            Debug.LogError("[多任务工具] 场景中找不到 Parts 节点，请先打开 TrainingScene 场景。");
            return;
        }

        // 2. 找到 TrainingManager
        TrainingManager manager = Object.FindObjectOfType<TrainingManager>();
        if (manager == null)
        {
            Debug.LogError("[多任务工具] 场景中找不到 TrainingManager，请先挂载该组件。");
            return;
        }

        // 3. 创建零件（若已存在则跳过，避免重复创建）
        CreatePart(partsRoot, manager, "Fan", PrimitiveType.Cylinder,
            new Vector3(0f, 1.55f, 2f), new Vector3(0.45f, 0.12f, 0.45f),
            new Vector3(90f, 0f, 0f), 0, "散热系统");

        CreatePart(partsRoot, manager, "Motor", PrimitiveType.Cube,
            new Vector3(0.9f, 1.45f, 2f), new Vector3(0.5f, 0.35f, 0.35f),
            Vector3.zero, 1, "动力系统");

        EditorSceneManager.MarkSceneDirty(partsRoot.gameObject.scene);
        Debug.Log("[多任务工具] 电机任务零件生成完成：Fan / Motor（挂于 Parts 下，taskId=task_motor）。");
    }

    private static void CreatePart(Transform parent, TrainingManager manager, string partName,
        PrimitiveType primitive, Vector3 localPos, Vector3 localScale, Vector3 euler, int stepIndex, string category)
    {
        if (GameObject.Find(partName) != null)
        {
            Debug.Log($"[多任务工具] 场景中已存在 {partName}，跳过创建。");
            return;
        }

        // 创建基础几何体（自带 MeshFilter/MeshRenderer/默认Collider）
        GameObject go = GameObject.CreatePrimitive(primitive);
        go.name = partName;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.transform.localEulerAngles = euler;

        // 挂 Rigidbody（XR 抓取需要）
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null)
            rb = go.AddComponent<Rigidbody>();

        // 挂 XRGrabInteractable（XR 抓取交互）
        XRGrabInteractable grab = go.GetComponent<XRGrabInteractable>();
        if (grab == null)
            grab = go.AddComponent<XRGrabInteractable>();

        // 挂 PartInteractable（培训流程控制）并配置
        PartInteractable part = go.GetComponent<PartInteractable>();
        if (part == null)
            part = go.AddComponent<PartInteractable>();

        var so = new SerializedObject(part);
        so.FindProperty("partName").stringValue = partName;
        so.FindProperty("partCategory").stringValue = category;
        so.FindProperty("taskId").stringValue = TaskId;
        so.FindProperty("trainingManager").objectReferenceValue = manager;
        so.FindProperty("requiredStepIndex").intValue = stepIndex;
        so.ApplyModifiedProperties();

        Debug.Log($"[多任务工具] 已创建 {partName}（步骤 {stepIndex}，类别 {category}）。");
    }
}
