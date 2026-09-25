using UnityEditor;
using UnityEngine;

// Scene 在 UnityEngine.SceneManagement 命名空间下，这里用别名避免与 Editor 侧的其它 Scene 概念混淆。
using Scene = UnityEngine.SceneManagement.Scene;

/// <summary>
/// 培训配置校验器（M26.2）。
///
/// 定位：只读诊断工具，与菜单下的「修复」类工具互补，只负责「发现」问题。
/// 与 VRDeviceTraining 菜单下的其它工具不同，本工具：
///   - 只读：不修改任何场景对象、不写磁盘、不自动修复；
///   - 手动：只通过菜单项触发，不会随脚本编译 / 场景加载 / 构建自动运行；
///   - 不改代码：不修改 TrainingManager、PartInteractable 的任何逻辑。
///
/// 校验的目的：
/// 零件与步骤的绑定关系（requiredStepIndex）在运行时是「静默失败」的——
/// 一旦配错，表现为「零件永远拿不起来」或「培训永远无法完成」，
/// 但 Console 不会有任何报错，排查成本极高。本工具把这五类配置错误提前暴露出来。
/// </summary>
public static class TrainingSetupValidator
{
    private const string MenuPath = "VRDeviceTraining/校验培训配置";
    private const string FieldRequiredStepIndex = "requiredStepIndex";
    private const string FieldTrainingManager = "trainingManager";

    [MenuItem(MenuPath, false, 200)]
    public static void Validate()
    {
        Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            Debug.LogError("[配置校验] 当前没有已加载的场景，无法校验。请先打开 TrainingScene。");
            return;
        }

        TrainingManager[] managers = CollectComponents<TrainingManager>(activeScene);
        PartInteractable[] parts = CollectComponents<PartInteractable>(activeScene);

        int errorCount = 0;
        int warningCount = 0;

        Debug.Log($"[配置校验] 场景：{activeScene.name} | TrainingManager x{managers.Length} | PartInteractable x{parts.Length}");

        if (managers.Length == 0)
        {
            Debug.LogError("[配置校验] 场景中没有找到 TrainingManager，培训流程无法运行。");
            Summarize(1, 0);
            return;
        }

        if (managers.Length > 1)
        {
            for (int i = 0; i < managers.Length; i++)
            {
                Debug.LogWarning(
                    $"[配置校验] 场景中存在多个 TrainingManager（共 {managers.Length} 个），请确保它们不是重复布置的。",
                    managers[i].gameObject);
            }
            warningCount += managers.Length;
        }

        for (int i = 0; i < managers.Length; i++)
            CheckEmptySteps(managers[i], ref warningCount);

        PartBinding[] bindings = BuildBindings(parts, ref errorCount);

        for (int i = 0; i < managers.Length; i++)
            CheckMissingParts(managers[i], bindings, ref errorCount);

        CheckDuplicateBindings(bindings, ref warningCount);

        Summarize(errorCount, warningCount);
    }

    /// <summary>收集场景中（含非激活对象）的指定组件。只看当前场景，不扫描 Assets。</summary>
    private static T[] CollectComponents<T>(Scene scene) where T : Component
    {
        var results = new System.Collections.Generic.List<T>();
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null)
                continue;

            results.AddRange(roots[i].GetComponentsInChildren<T>(true));
        }

        return results.ToArray();
    }

    /// <summary>把一个零件的配置读成一条绑定记录。只读，不写回。</summary>
    private static PartBinding ReadBinding(PartInteractable part)
    {
        var binding = new PartBinding
        {
            Part = part,
            Owner = part.gameObject,
            DisplayName = part != null ? part.name : string.Empty
        };

        var so = new SerializedObject(part);

        SerializedProperty managerProp = so.FindProperty(FieldTrainingManager);
        binding.Manager = managerProp != null ? managerProp.objectReferenceValue as TrainingManager : null;

        SerializedProperty indexProp = so.FindProperty(FieldRequiredStepIndex);
        binding.HasLegalIndex = indexProp != null;
        binding.StepIndex = indexProp != null ? indexProp.intValue : 0;

        return binding;
    }

    private static PartBinding[] BuildBindings(PartInteractable[] parts, ref int errorCount)
    {
        var results = new System.Collections.Generic.List<PartBinding>();

        for (int i = 0; i < parts.Length; i++)
        {
            PartInteractable part = parts[i];
            if (part == null)
                continue;

            PartBinding binding = ReadBinding(part);

            // 1. TrainingManager 未绑定：该零件完全不参与培训流程，但看起来仍然可以抓取。
            if (binding.Manager == null)
            {
                Debug.LogError(
                    "[配置校验] 该零件没有绑定 TrainingManager，它不会被培训流程接管（可以自由抓取，但不参与步骤）。",
                    binding.Owner);
                errorCount++;
                results.Add(binding);
                continue;
            }

            if (!binding.HasLegalIndex)
            {
                Debug.LogError(
                    "[配置校验] 读取 requiredStepIndex 失败，可能字段名已变更。",
                    binding.Owner);
                errorCount++;
                results.Add(binding);
                continue;
            }

            // 2. 负索引：永远不会等于 CurrentStepIndex。
            if (binding.StepIndex < 0)
            {
                Debug.LogError(
                    $"[配置校验] requiredStepIndex = {binding.StepIndex} 为负数。培训永远不会轮到该零件，它将一直处于锁定状态。",
                    binding.Owner);
                errorCount++;
                results.Add(binding);
                continue;
            }

            // 3. 越界索引：当前步骤只会推进到 TotalSteps - 1，越界的零件永远轮不到。
            if (binding.StepIndex >= binding.Manager.TotalSteps)
            {
                Debug.LogError(
                    $"[配置校验] requiredStepIndex = {binding.StepIndex} 超出范围（TrainingManager 共 {binding.Manager.TotalSteps} 个步骤，合法范围 0 ~ {binding.Manager.TotalSteps - 1}）。该零件永远无法被拆卸，培训将无法完成。",
                    binding.Owner);
                errorCount++;
                results.Add(binding);
                continue;
            }

            binding.IsUsable = true;
            results.Add(binding);
        }

        return results.ToArray();
    }

    /// <summary>4. 没有任何零件绑定的步骤：流程推进到该步时会卡死。</summary>
    private static void CheckMissingParts(TrainingManager manager, PartBinding[] bindings, ref int errorCount)
    {
        int totalSteps = manager.TotalSteps;

        for (int stepIndex = 0; stepIndex < totalSteps; stepIndex++)
        {
            bool bound = false;

            for (int i = 0; i < bindings.Length; i++)
            {
                PartBinding binding = bindings[i];
                if (!binding.IsUsable || binding.Manager != manager)
                    continue;

                if (binding.StepIndex == stepIndex)
                {
                    bound = true;
                    break;
                }
            }

            if (bound)
                continue;

            string stepName = manager.GetStepName(stepIndex);
            Debug.LogError(
                $"[配置校验] 步骤 {stepIndex}（{stepName}）没有任何零件绑定。培训推进到该步后将无法继续。",
                manager.gameObject);
            errorCount++;
        }
    }

    /// <summary>5. 多个零件绑定到同一个步骤：先被拆下的那个会推进流程，其余零件会被永久锁定。</summary>
    private static void CheckDuplicateBindings(PartBinding[] bindings, ref int warningCount)
    {
        for (int i = 0; i < bindings.Length; i++)
        {
            PartBinding outer = bindings[i];
            if (!outer.IsUsable)
                continue;

            for (int j = i + 1; j < bindings.Length; j++)
            {
                PartBinding inner = bindings[j];
                if (!inner.IsUsable)
                    continue;

                if (inner.Manager != outer.Manager || inner.StepIndex != outer.StepIndex)
                    continue;

                Debug.LogWarning(
                    $"[配置校验] 零件「{outer.DisplayName}」与「{inner.DisplayName}」绑定到同一个步骤 {outer.StepIndex}（{SafeStepName(outer.Manager, outer.StepIndex)}）。其中任意一个被拆卸都会推进流程，另一个将被永久锁定。",
                    inner.Owner);
                warningCount++;
            }
        }
    }

    /// <summary>6. 没有配置任何步骤。</summary>
    private static void CheckEmptySteps(TrainingManager manager, ref int warningCount)
    {
        if (manager.TotalSteps != 0)
            return;

        Debug.LogWarning(
            "[配置校验] TrainingManager 没有配置任何培训步骤，进入 Play 后流程不会启动。",
            manager.gameObject);
        warningCount++;
    }

    private static string SafeStepName(TrainingManager manager, int index)
    {
        return manager != null ? manager.GetStepName(index) : $"第 {index + 1} 步";
    }

    private static void Summarize(int errorCount, int warningCount)
    {
        if (errorCount == 0 && warningCount == 0)
        {
            Debug.Log($"[配置校验] 通过：未发现配置问题。");
            return;
        }

        Debug.Log($"[配置校验] 完成：Error {errorCount} 条，Warning {warningCount} 条。");
    }

    // 用引用类型：绑定记录的字段分阶段填写，值类型会导致加入集合后改动丢失。
    private class PartBinding
    {
        public PartInteractable Part;
        public GameObject Owner;
        public string DisplayName;
        public TrainingManager Manager;
        public int StepIndex;
        public bool HasLegalIndex;
        public bool IsUsable;
    }
}
