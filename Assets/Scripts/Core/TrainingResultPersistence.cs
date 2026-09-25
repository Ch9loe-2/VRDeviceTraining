using System;
using System.IO;
using UnityEngine;

/// <summary>
/// TrainingResult JSON 持久化（纯静态工具，无 MonoBehaviour / 无 DontDestroyOnLoad）。
///
/// 设计要点：
/// - 不修改 TrainingResult.cs。TrainingResult 的 score / grade 为 private 字段，
///   因此用内部私有 DTO 承载全部需持久化字段后再序列化，避免侵入核心数据类。
/// - 写入采用 training_result.json.tmp → 删除旧文件 → Move 的原子替换，
///   避免写到一半被强杀时留下半截 JSON 导致下次读取失败。
/// - Save / LoadLast 任何异常一律吞掉并 Debug.LogWarning，绝不向调用方抛出，
///   保证存档失败不会影响培训流程 / ResultPanel 行为。
/// - 不引入第三方库、不使用 PlayerPrefs。
/// </summary>
public static class TrainingResultPersistence
{
    private const string FileName = "training_result.json";

    /// <summary>只读路径，便于测试与排查。始终基于 Application.persistentDataPath。</summary>
    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    /// <summary>
    /// 序列化 DTO。private 嵌套 [Serializable] 类，承载 TrainingResult 的全部需持久化字段
    /// （含私有的 score / grade）。不直接序列化 TrainingResult 本身。
    /// </summary>
    [Serializable]
    internal class ResultDTO
    {
        public bool isCompleted;
        public float elapsedSeconds;
        public int wrongOperationCount;
        public int resetCount;
        public float completionTime;
        public int score;
        public string grade;
    }

    /// <summary>
    /// 保存一次培训结果。
    /// 成功返回 true；result == null / IO 异常 / 序列化异常均返回 false，且不向调用方抛异常。
    /// </summary>
    public static bool Save(TrainingResult result)
    {
        if (result == null)
            return false;

        try
        {
            ResultDTO dto = ToDTO(result);
            string json = JsonUtility.ToJson(dto, true);

            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(tmp, FilePath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TrainingResultPersistence] 保存失败：{e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 读取最近一次保存的结果。
    /// 文件不存在 / 为空 / JSON 损坏 / DTO 无效 / 读取异常，均返回 null，且不向调用方抛异常。
    /// 第一版对损坏文件保持只读失败（不删除原文件）。
    /// </summary>
    public static TrainingResult LoadLast()
    {
        if (!File.Exists(FilePath))
            return null;

        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TrainingResultPersistence] 读取失败：{e.Message}");
            return null;
        }

        if (string.IsNullOrEmpty(json))
            return null;

        ResultDTO dto;
        try
        {
            dto = JsonUtility.FromJson<ResultDTO>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TrainingResultPersistence] JSON 解析失败：{e.Message}");
            return null;
        }

        if (dto == null)
            return null;

        TrainingResult result = new TrainingResult(
            dto.isCompleted,
            dto.elapsedSeconds,
            dto.wrongOperationCount,
            dto.resetCount,
            dto.completionTime
        );
        result.SetScore(dto.score, dto.grade);
        return result;
    }

    private static ResultDTO ToDTO(TrainingResult result)
    {
        return new ResultDTO
        {
            isCompleted = result.isCompleted,
            elapsedSeconds = result.elapsedSeconds,
            wrongOperationCount = result.wrongOperationCount,
            resetCount = result.resetCount,
            completionTime = result.completionTime,
            score = result.Score,
            grade = result.Grade,
        };
    }
}
