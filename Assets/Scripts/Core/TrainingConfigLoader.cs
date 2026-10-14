using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CSV 配置加载器：从 CSV 读取培训步骤与零件绑定，动态注入 TrainingManager。
///
/// 设计目标：新增培训任务只改 CSV 表格，不改代码、不改场景绑定。
///
/// 使用方式（场景接线）：
/// 1. 在 Assets/Configs 下准备 TrainingConfig.csv（参考同目录示例文件）；
/// 2. 新建空物体挂本脚本（或挂在 TrainingManager 同一物体上）；
/// 3. Inspector 拖入 configCsv（TextAsset）与 trainingManager；
/// 4. 进入 Play，Awake 阶段自动加载并注入步骤，TrainingManager.Start 直接使用配置的步骤。
///
/// 向后兼容：configCsv 未配置 / 解析失败时，什么都不做，
/// 场景里 TrainingManager 原有的 Inspector 步骤配置照常工作。
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

        // 1. 按 CSV 行顺序生成步骤列表（拆卸顺序 = CSV 从上到下）
        List<TrainingStep> steps = new List<TrainingStep>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
            steps.Add(new TrainingStep(rows[i].StepName));

        // 2. 按零件名自动绑定步骤索引
        if (autoBindParts)
            BindParts(rows);

        // 3. 注入 TrainingManager（内部会 BeginRound 重置统计状态）
        trainingManager.ConfigureSteps(steps);

        Debug.Log($"[TrainingConfigLoader] 已从 CSV 加载 {rows.Count} 个步骤：{string.Join(" → ", rows.ConvertAll(r => r.StepName).ToArray())}", this);
    }

    /// <summary>
    /// 遍历场景中所有 PartInteractable，按 GameObject 名匹配 CSV 中的 PartObjectName，
    /// 命中后调用 ConfigureByCSV 写入步骤索引与零件名。
    /// </summary>
    private void BindParts(List<StepRow> rows)
    {
        PartInteractable[] allParts = FindObjectsOfType<PartInteractable>(true);
        if (allParts == null || allParts.Length == 0)
        {
            Debug.LogWarning("[TrainingConfigLoader] 场景中没有找到 PartInteractable 零件，跳过零件绑定。", this);
            return;
        }

        int boundCount = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            string partObjectName = rows[i].PartObjectName;
            if (string.IsNullOrEmpty(partObjectName))
                continue;

            for (int j = 0; j < allParts.Length; j++)
            {
                if (allParts[j] == null)
                    continue;

                if (allParts[j].gameObject.name != partObjectName)
                    continue;

                allParts[j].ConfigureByCSV(i, rows[i].StepName, rows[i].PartCategory);
                boundCount++;
                break;
            }
        }

        if (boundCount < rows.Count)
            Debug.LogWarning($"[TrainingConfigLoader] 零件绑定完成 {boundCount}/{rows.Count} 个（未匹配到的零件名请检查 CSV 与场景 GameObject 名是否一致）。", this);
    }

    // ==================== CSV 解析 ====================

    private class StepRow
    {
        public string StepName;
        public string PartObjectName;
        public string PartCategory;
    }

    /// <summary>
    /// 轻量 CSV 解析：
    /// - 第一行为表头（StepName,PartObjectName,PartCategory），自动跳过；
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
                // 零件名与类别为可选列；缺省时留空，由调用方兜底
                PartObjectName = fields.Count > 1 ? fields[1].Trim() : string.Empty,
                PartCategory = fields.Count > 2 ? fields[2].Trim() : string.Empty
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
