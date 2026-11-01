using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CSV 配置加载器：从 CSV 读取多套培训任务、步骤与零件绑定，动态注入 TrainingManager。
///
/// 设计目标：新增培训任务只改 CSV 表格，不改代码、不改场景绑定。
///
/// CSV 格式（列顺序固定，见 Assets/Configs/TrainingConfig.csv）：
///   TaskID,TaskName,StepName,PartObjectName,PartCategory
/// 同一 TaskID 的多行组成一套任务的完整步骤序列（从上到下 = 拆卸顺序）。
///
/// 使用方式（场景接线）：
/// 1. 在 Assets/Configs 下准备 TrainingConfig.csv；
/// 2. 新建空物体挂本脚本（或挂在 TrainingManager 同一物体上）；
/// 3. Inspector 拖入 configCsv（TextAsset）与 trainingManager；
/// 4. 进入 Play，Awake 自动加载全部任务，默认启动第一个任务；
///    运行时调用 SelectTask(taskId) 即可切换任务。
///
/// 向后兼容：
/// - CSV 缺省 TaskID / TaskName 列时（旧格式），整张表视为单个任务；
/// - configCsv 未配置 / 解析失败时，什么都不做，场景 Inspector 配置照常工作。
/// </summary>
public class TrainingConfigLoader : MonoBehaviour
{
    [Header("CSV 配置文件（Inspector 拖入）")]
    [SerializeField] private TextAsset configCsv;

    [Header("目标培训管理器")]
    [SerializeField] private TrainingManager trainingManager;

    [Header("零件自动绑定")]
    [Tooltip("按 CSV 的 PartObjectName 自动查找场景中的零件并绑定步骤索引。关闭时只加载步骤列表，不绑定零件。")]
    [SerializeField] private bool autoBindParts = true;

    /// <summary>已解析的全部任务（保持 CSV 出现顺序）。</summary>
    public IReadOnlyList<TrainingTaskConfig> Tasks => tasks;

    private readonly List<TrainingTaskConfig> tasks = new List<TrainingTaskConfig>();
    private TrainingTaskConfig currentTask;

    private void Awake()
    {
        if (configCsv == null)
        {
            Debug.LogWarning("[TrainingConfigLoader] 未配置 configCsv，跳过 CSV 加载（使用场景原有步骤配置）。", this);
            return;
        }

        if (trainingManager == null)
        {
            Debug.LogWarning("[TrainingConfigLoader] 未绑定 trainingManager，跳过 CSV 加载。", this);
            return;
        }

        List<StepRow> rows = ParseCsv(configCsv.text);
        if (rows == null || rows.Count == 0)
        {
            Debug.LogWarning("[TrainingConfigLoader] CSV 解析结果为空，跳过 CSV 加载。", this);
            return;
        }

        BuildTasks(rows);
        if (tasks.Count == 0)
        {
            Debug.LogWarning("[TrainingConfigLoader] CSV 中没有可用的任务，跳过 CSV 加载。", this);
            return;
        }

        // 默认启动第一个任务
        SelectTask(tasks[0].TaskId);

        Debug.Log($"[TrainingConfigLoader] 已从 CSV 加载 {tasks.Count} 套任务：{string.Join("、", tasks.ConvertAll(t => t.TaskName).ToArray())}", this);
    }

    /// <summary>
    /// 切换到指定任务：注入步骤、绑定零件、锁定其它任务的零件。
    /// taskId 不存在时忽略并返回 false。
    /// </summary>
    public bool SelectTask(string taskId)
    {
        if (trainingManager == null)
            return false;

        TrainingTaskConfig target = tasks.Find(t => t.TaskId == taskId);
        if (target == null)
        {
            Debug.LogWarning($"[TrainingConfigLoader] 找不到任务：{taskId}，切换失败。", this);
            return false;
        }

        currentTask = target;

        // 1. 生成步骤列表（CSV 从上到下 = 拆卸顺序）
        List<TrainingStep> steps = new List<TrainingStep>(target.Rows.Count);
        for (int i = 0; i < target.Rows.Count; i++)
            steps.Add(new TrainingStep(target.Rows[i].StepName));

        // 2. 按零件名自动绑定当前任务零件
        if (autoBindParts)
            BindParts(target);

        // 3. 锁定不属于当前任务的零件（防止跨任务误抓）
        LockPartsOfOtherTasks(taskId);

        // 4. 注入 TrainingManager（内部会 BeginRound 重置统计状态）
        trainingManager.SetCurrentTask(taskId, target.TaskName);
        trainingManager.ConfigureSteps(steps);

        Debug.Log($"[TrainingConfigLoader] 已切换到任务：{target.TaskName}（{target.Rows.Count} 个步骤）", this);
        return true;
    }

    /// <summary>获取当前任务配置。</summary>
    public TrainingTaskConfig GetCurrentTask()
    {
        return currentTask;
    }

    /// <summary>
    /// 遍历场景中所有 PartInteractable，按 GameObject 名匹配当前任务 CSV 中的 PartObjectName，
    /// 命中后调用 ConfigureByCSV 写入步骤索引、零件名、类别与任务ID。
    /// </summary>
    private void BindParts(TrainingTaskConfig task)
    {
        PartInteractable[] allParts = FindObjectsOfType<PartInteractable>(true);
        if (allParts == null || allParts.Length == 0)
        {
            Debug.LogWarning("[TrainingConfigLoader] 场景中没有找到 PartInteractable 零件，跳过零件绑定。", this);
            return;
        }

        int boundCount = 0;
        for (int i = 0; i < task.Rows.Count; i++)
        {
            string partObjectName = task.Rows[i].PartObjectName;
            if (string.IsNullOrEmpty(partObjectName))
                continue;

            for (int j = 0; j < allParts.Length; j++)
            {
                if (allParts[j] == null)
                    continue;

                if (allParts[j].gameObject.name != partObjectName)
                    continue;

                allParts[j].ConfigureByCSV(i, task.Rows[i].StepName, task.Rows[i].PartCategory, task.TaskId);
                boundCount++;
                break;
            }
        }

        if (boundCount < task.Rows.Count)
            Debug.LogWarning($"[TrainingConfigLoader] 零件绑定完成 {boundCount}/{task.Rows.Count} 个（未匹配到的零件名请检查 CSV 与场景 GameObject 名是否一致）。", this);
    }

    /// <summary>把不属于当前任务的所有零件锁定（禁止抓取）。</summary>
    private void LockPartsOfOtherTasks(string currentTaskId)
    {
        PartInteractable[] allParts = FindObjectsOfType<PartInteractable>(true);
        if (allParts == null)
            return;

        for (int i = 0; i < allParts.Length; i++)
        {
            PartInteractable part = allParts[i];
            if (part == null)
                continue;

            if (string.IsNullOrEmpty(part.TaskId) || part.TaskId != currentTaskId)
                part.LockForOtherTask();
        }
    }

    /// <summary>把 CSV 行按 TaskID 分组为多套任务。</summary>
    private void BuildTasks(List<StepRow> rows)
    {
        tasks.Clear();

        // 保持 CSV 中的任务出现顺序
        var order = new List<string>();
        var groups = new Dictionary<string, List<StepRow>>();

        for (int i = 0; i < rows.Count; i++)
        {
            StepRow row = rows[i];
            string taskId = string.IsNullOrEmpty(row.TaskId) ? "default" : row.TaskId;
            string taskName = string.IsNullOrEmpty(row.TaskName) ? taskId : row.TaskName;

            List<StepRow> group;
            if (!groups.TryGetValue(taskId, out group))
            {
                group = new List<StepRow>();
                groups[taskId] = group;
                order.Add(taskId);
            }

            group.Add(row);
        }

        for (int i = 0; i < order.Count; i++)
        {
            string taskId = order[i];
            List<StepRow> group = groups[taskId];
            if (group == null || group.Count == 0)
                continue;

            TrainingTaskConfig task = new TrainingTaskConfig
            {
                TaskId = taskId,
                TaskName = group[0].TaskName,
                Rows = group
            };
            tasks.Add(task);
        }
    }

    // ==================== CSV 数据结构 ====================

    /// <summary>一套培训任务的配置数据。</summary>
    public class TrainingTaskConfig
    {
        public string TaskId;
        public string TaskName;
        public List<StepRow> Rows;
    }

    private class StepRow
    {
        public string TaskId;
        public string TaskName;
        public string StepName;
        public string PartObjectName;
        public string PartCategory;
    }

    // ==================== CSV 解析 ====================

    /// <summary>
    /// 轻量 CSV 解析：
    /// - 第一行为表头（TaskID,TaskName,StepName,PartObjectName,PartCategory），自动跳过；
    /// - 旧格式（3列：StepName,PartObjectName,PartCategory）也兼容，TaskID/TaskName 缺省；
    /// - 支持双引号包裹字段（字段内含逗号/引号时）；
    /// - 跳过空行；
    /// - 兼容 \r\n 与 \n 换行。
    /// </summary>
    private static List<StepRow> ParseCsv(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        List<StepRow> result = new List<StepRow>();

        // 按行拆分（统一去掉 \r）
        string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        bool firstLine = true;

        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // 表头跳过
            if (firstLine)
            {
                firstLine = false;
                continue;
            }

            List<string> fields = SplitCsvLine(line);
            if (fields == null || fields.Count < 1)
                continue;

            string stepName = fields[0].Trim();
            if (string.IsNullOrEmpty(stepName))
                continue;

            StepRow row = new StepRow
            {
                StepName = stepName,
                PartObjectName = fields.Count > 1 ? fields[1].Trim() : string.Empty,
                PartCategory = fields.Count > 2 ? fields[2].Trim() : string.Empty,
                TaskId = fields.Count > 3 ? fields[3].Trim() : string.Empty,
                TaskName = fields.Count > 4 ? fields[4].Trim() : string.Empty
            };
            result.Add(row);
        }

        return result;
    }

    /// <summary>
    /// 单行 CSV 字段拆分：支持双引号转义（"a,b" → a,b；"" → "）。
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        List<string> fields = new List<string>();
        bool inQuotes = false;
        System.Text.StringBuilder current = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // 连续两个引号 = 转义引号
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
