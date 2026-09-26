using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// M27 + M28: TrainingManager 核心拆卸/组装/评分/持久化业务逻辑的 EditMode 单元测试。
/// 不依赖 XR / PartInteractable / UI 组件，直接测试 TrainingManager + TrainingStep + 评分+ 持久化。
/// </summary>
public class TrainingManagerTests
{
    // 场景配置的拆卸步骤名称
    private static readonly string[] DisassemblyNames = { "拆卸 Battery", "拆卸后盖" };

    private TrainingManager CreateManagerWithSteps(int count)
    {
        var go = new GameObject("TestTrainingManager");
        var mgr = go.AddComponent<TrainingManager>();

        var steps = new List<TrainingStep>();
        for (int i = 0; i < count; i++)
        {
            string name = i < DisassemblyNames.Length ? DisassemblyNames[i] : "Step" + i;
            steps.Add(new TrainingStep(name));
        }

        mgr.ConfigureSteps(steps);
        return mgr;
    }

    // ============================================
    // Test 1: 初始拆卸步骤正确
    // ============================================
    [Test]
    public void InitialState_CurrentStepIndexIsZero()
    {
        var mgr = CreateManagerWithSteps(2);
        Assert.AreEqual(0, mgr.CurrentStepIndex);
        Assert.IsFalse(mgr.IsCompleted);
        Assert.AreEqual(TrainingPhase.Disassembly, mgr.CurrentPhase);
    }

    [Test]
    public void InitialState_DisassemblyStepCorrect()
    {
        var mgr = CreateManagerWithSteps(2);
        Assert.AreEqual("拆卸 Battery", mgr.GetStepName(0));
        Assert.AreEqual("拆卸后盖", mgr.GetStepName(1));
    }

    [Test]
    public void InitialState_TotalStepsIsDoubled()
    {
        var mgr = CreateManagerWithSteps(2);
        Assert.AreEqual(4, mgr.TotalSteps, "TotalSteps 应为拆卸步骤数 * 2");
    }

    [Test]
    public void InitialState_IsPartCurrentStepBattery()
    {
        var mgr = CreateManagerWithSteps(2);
        Assert.IsTrue(mgr.IsPartCurrentStep(0), "初始时 requiredStepIndex=0 应列为当前步骤");
        Assert.IsFalse(mgr.IsPartCurrentStep(1), "初始时 requiredStepIndex=1 不应为当前步骤");
    }

    // ============================================
    // Test 2: Battery → 后盖 拆卸顺序正确
    // ============================================
    [Test]
    public void DisassemblyOrder_BatteryThenCover()
    {
        var mgr = CreateManagerWithSteps(2);
        Assert.AreEqual(TrainingPhase.Disassembly, mgr.CurrentPhase);
        Assert.IsTrue(mgr.IsPartCurrentStep(0));

        mgr.CompleteCurrentStep(); // Battery done
        Assert.AreEqual(1, mgr.CurrentStepIndex);

        // 现在应轮到后盖 (requiredStepIndex=1)
        Assert.IsTrue(mgr.IsPartCurrentStep(1));
        Assert.IsFalse(mgr.IsPartCurrentStep(0));

        mgr.CompleteCurrentStep(); // 后盖 done
        Assert.AreEqual(2, mgr.CurrentStepIndex);
        Assert.AreEqual("组装 后盖", mgr.GetStepName(2), "第3步应为组装后盖");
    }

    // ============================================
    // Test 3: 错误拆卸顺序不会推进
    // ============================================
    [Test]
    public void WrongDisassemblyOrder_DoesNotAdvance()
    {
        var mgr = CreateManagerWithSteps(2);
        int before = mgr.CurrentStepIndex;

        mgr.RecordWrongOperation(1); // 尝试操作后盖（requiredStepIndex=1），但当前是 Battery(0)

        Assert.AreEqual(before, mgr.CurrentStepIndex, "错误拆卸操作不应推进索引");
        Assert.IsFalse(mgr.IsCompleted);
    }

    [Test]
    public void WrongDisassemblyOrder_WrongOperationCountIncrements()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.RecordWrongOperation(1);
        Assert.AreEqual(1, mgr.WrongOperationCount);
        Assert.AreEqual(0, mgr.CurrentStepIndex);
    }

    // ============================================
    // Test 4: 拆卸完成后进入组装阶段
    // ============================================
    [Test]
    public void DisassemblyComplete_EntersAssemblyPhase()
    {
        var mgr = CreateManagerWithSteps(2);

        bool phaseChanged = false;
        TrainingPhase newPhase = TrainingPhase.Disassembly;
        mgr.OnPhaseChanged += (p) => { phaseChanged = true; newPhase = p; };

        mgr.CompleteCurrentStep(); // Battery
        mgr.CompleteCurrentStep(); // 后盖

        // 现在应该进入组装阶段
        Assert.AreEqual(2, mgr.CurrentStepIndex);
        Assert.AreEqual(TrainingPhase.Assembly, mgr.CurrentPhase);
        Assert.IsTrue(phaseChanged, "进入组装阶段应触发 OnPhaseChanged");
        Assert.AreEqual(TrainingPhase.Assembly, newPhase);
    }

    [Test]
    public void AssemblyPhase_FirstStepIsCover()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep(); // Battery
        mgr.CompleteCurrentStep(); // 后盖

        // 组装第 1 步 = 后盖（requiredStepIndex=1）
        Assert.IsTrue(mgr.IsPartCurrentStep(1), "组装阶段第一步应轮到后盖(requiredStepIndex=1)");
        Assert.IsFalse(mgr.IsPartCurrentStep(0), "组装阶段第一步不应轮到 Battery(true)");
    }

    // ============================================
    // Test 5: 后盖 → Battery 组装顺序正确
    // ============================================
    [Test]
    public void AssemblyOrder_CoverThenBattery()
    {
        var mgr = CreateManagerWithSteps(2);

        // 拆卸完成
        mgr.CompleteCurrentStep(); // step 0 Battery
        mgr.CompleteCurrentStep(); // step 1 后盖

        // 现在组装阶段
        Assert.AreEqual(TrainingPhase.Assembly, mgr.CurrentPhase);
        Assert.IsTrue(mgr.IsPartCurrentStep(1)); // 先装后盖

        mgr.CompleteCurrentStep(); // 后盖装回
        Assert.AreEqual(3, mgr.CurrentStepIndex);
        Assert.IsTrue(mgr.IsPartCurrentStep(0)); // 再装 Battery

        mgr.CompleteCurrentStep(); // Battery 装回
        Assert.IsTrue(mgr.IsCompleted);
    }

    [Test]
    public void AssemblyPhase_StepNamesCorrect()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep(); // 0
        mgr.CompleteCurrentStep(); // 1

        Assert.AreEqual("组装 后盖", mgr.GetStepName(2));
        Assert.AreEqual("组装 Battery", mgr.GetStepName(3));
    }

    // ============================================
    // Test 6: 错误组装顺序不会推进
    // ============================================
    [Test]
    public void WrongAssemblyOrder_DoesNotAdvance()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep(); // Battery
        mgr.CompleteCurrentStep(); // 后盖 → assembly

        int before = mgr.CurrentStepIndex;
        mgr.RecordWrongOperation(0); // 尝试装 Battery(false) 但当前需装后盖(1)

        Assert.AreEqual(before, mgr.CurrentStepIndex, "错误组装操作不应推进索引");
        Assert.AreEqual(TrainingPhase.Assembly, mgr.CurrentPhase);
    }

    [Test]
    public void WrongAssemblyOrder_WrongCountIncrements()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep(); // Battery
        mgr.CompleteCurrentStep(); // 后盖

        int wrongBefore = mgr.WrongOperationCount;
        mgr.RecordWrongOperation(0); // 装 Battery(false) → 当前需后盖
        Assert.AreEqual(wrongBefore + 1, mgr.WrongOperationCount);
    }

    // ============================================
    // Test 7: 全部步骤完成后 IsCompleted 正确
    // ============================================
    [Test]
    public void AllStepsCompleted_IsCompletedTrue()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep(); // 0 Battery 拆
        mgr.CompleteCurrentStep(); // 1 后盖拆
        mgr.CompleteCurrentStep(); // 2 后盖装
        mgr.CompleteCurrentStep(); // 3 Battery 装

        Assert.IsTrue(mgr.IsCompleted);
        Assert.AreEqual(TrainingPhase.Completed, mgr.CurrentPhase);
        Assert.IsNull(mgr.CurrentStep);
    }

    [Test]
    public void AllStepsCompleted_OnTrainingCompletedFiresOnce()
    {
        var mgr = CreateManagerWithSteps(2);

        int fireCount = 0;
        mgr.OnTrainingCompleted += (r) => { fireCount++; };

        mgr.CompleteCurrentStep(); // 0
        mgr.CompleteCurrentStep(); // 1
        mgr.CompleteCurrentStep(); // 2
        mgr.CompleteCurrentStep(); // 3

        Assert.AreEqual(1, fireCount);
    }

    // ============================================
    // Test 8: 错误操作次数正确
    // ============================================
    [Test]
    public void WrongOperationCount_TracksCorrectly()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.RecordWrongOperation(1);
        Assert.AreEqual(1, mgr.WrongOperationCount);

        mgr.RecordWrongOperation(0);
        Assert.AreEqual(2, mgr.WrongOperationCount);

        mgr.CompleteCurrentStep(); // 正确操作，不改错误计数
        Assert.AreEqual(2, mgr.WrongOperationCount);
    }

    [Test]
    public void WrongOperationCount_AfterComplete()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.RecordWrongOperation(1);
        mgr.RecordWrongOperation(1);
        mgr.CompleteCurrentStep(); // Battery
        mgr.CompleteCurrentStep(); // 后盖

        var result = mgr.BuildResult();
        Assert.AreEqual(2, result.wrongOperationCount);
        Assert.IsFalse(result.isCompleted, "仅完成拆卸阶段不应标记为 completed");
    }

    // ============================================
    // Test 9~11: 评分
    // ============================================
    [Test]
    public void Scoring_BaseScoreIs100WithNoErrors()
    {
        // 0 错误，0 重置，快速完成
        int score = TrainingScoring.CalculateScore(30f, 0, 0);
        Assert.AreEqual(100, score);
    }

    [Test]
    public void Scoring_EachWrongOperationDeducts10()
    {
        int score = TrainingScoring.CalculateScore(30f, 1, 0);
        Assert.AreEqual(98, score, "1次错误应扣约2分（操作分扣2，时间分不变）");
    }

    [Test]
    public void Scoring_ScoreNeverBelowZero()
    {
        int score = TrainingScoring.CalculateScore(10f, 100, 50);
        Assert.GreaterOrEqual(score, 0);
    }

    // ============================================
    // Test 12: 等级计算正确
    // ============================================
    [Test]
    public void Grade_90_100_IsExcellent()
    {
        Assert.AreEqual("优秀", TrainingScoring.GetGrade(90));
        Assert.AreEqual("优秀", TrainingScoring.GetGrade(95));
        Assert.AreEqual("优秀", TrainingScoring.GetGrade(100));
    }

    [Test]
    public void Grade_80_89_IsGood()
    {
        Assert.AreEqual("良好", TrainingScoring.GetGrade(80));
        Assert.AreEqual("良好", TrainingScoring.GetGrade(85));
        Assert.AreEqual("良好", TrainingScoring.GetGrade(89));
    }

    [Test]
    public void Grade_60_79_IsPass()
    {
        Assert.AreEqual("合格", TrainingScoring.GetGrade(60));
        Assert.AreEqual("合格", TrainingScoring.GetGrade(70));
        Assert.AreEqual("合格", TrainingScoring.GetGrade(79));
    }

    [Test]
    public void Grade_Below60_IsPractice()
    {
        Assert.AreEqual("待提升", TrainingScoring.GetGrade(0));
        Assert.AreEqual("待提升", TrainingScoring.GetGrade(30));
        Assert.AreEqual("待提升", TrainingScoring.GetGrade(59));
    }

    // ============================================
    // Test 13: 训练结果数据正确
    // ============================================
    [Test]
    public void TrainingResult_ContainsCorrectData()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.RecordWrongOperation(1);
        mgr.CompleteCurrentStep(); // 0
        mgr.CompleteCurrentStep(); // 1
        mgr.CompleteCurrentStep(); // 2
        mgr.CompleteCurrentStep(); // 3

        var result = mgr.BuildResult();
        Assert.IsTrue(result.isCompleted);
        Assert.AreEqual(1, result.wrongOperationCount);
        Assert.GreaterOrEqual(result.elapsedSeconds, 0f);
        Assert.AreEqual(4, mgr.TotalSteps);
    }

    // ============================================
    // Test 14: JSON 保存数据正确
    // ============================================
    [Test]
    public void Persistence_SaveAndLoad_ReturnsMatchingData()
    {
        // 使用 TestResult 模拟完整结果
        var result = new TrainingResult(true, 45.5f, 2, 1, 100f);
        result.SetScore(80, "良好");

        bool saved = TrainingResultPersistence.Save(result);
        Assert.IsTrue(saved, "保存应返回 true");

        var loaded = TrainingResultPersistence.LoadLast();
        Assert.IsNotNull(loaded, "加载应返回非 null");
        Assert.IsTrue(loaded.isCompleted);
        Assert.AreEqual(45.5f, loaded.elapsedSeconds, 0.01f);
        Assert.AreEqual(2, loaded.wrongOperationCount);
        Assert.AreEqual(1, loaded.resetCount);
        Assert.AreEqual(80, loaded.Score);
        Assert.AreEqual("良好", loaded.Grade);
    }

    // ============================================
    // Test 15: JSON 读取数据正确
    // ============================================
    [Test]
    public void Persistence_LoadSavedJson_DeserializesCorrectly()
    {
        // 直接验证序列化到文件的内容
        var result = new TrainingResult(true, 30f, 0, 0, 60f);
        result.SetScore(100, "优秀");
        TrainingResultPersistence.Save(result);

        string filePath = TrainingResultPersistence.FilePath;
        Assert.IsTrue(File.Exists(filePath));

        string rawJson = File.ReadAllText(filePath);
        Assert.IsTrue(rawJson.Contains("优秀"), "JSON 应包含等级信息");
        Assert.IsTrue(rawJson.Contains("100"), "JSON 应包含得分");
        Assert.IsTrue(rawJson.Contains("true"), "JSON 应包含 isCompleted");
    }

    // ============================================
    // Test 16: 空/损坏 JSON 不导致系统崩溃
    // ============================================
    [Test]
    public void Persistence_CorruptJson_ReturnsNull()
    {
        string filePath = TrainingResultPersistence.FilePath;

        // 确保父目录存在
        string dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // 写入无效 JSON
        File.WriteAllText(filePath, "{invalid json!!!}");

        // 加载应返回 null，不抛异常
        TrainingResult loaded = null;
        Assert.DoesNotThrow(() => { loaded = TrainingResultPersistence.LoadLast(); });
        Assert.IsNull(loaded);
    }

    [Test]
    public void Persistence_EmptyFile_ReturnsNull()
    {
        string filePath = TrainingResultPersistence.FilePath;
        string dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(filePath, "");

        TrainingResult loaded = null;
        Assert.DoesNotThrow(() => { loaded = TrainingResultPersistence.LoadLast(); });
        Assert.IsNull(loaded);
    }

    [Test]
    public void Persistence_NoFile_ReturnsNull()
    {
        string filePath = TrainingResultPersistence.FilePath;
        if (File.Exists(filePath))
            File.Delete(filePath);

        var loaded = TrainingResultPersistence.LoadLast();
        Assert.IsNull(loaded);
    }

    // ============================================
    // M27 兼容性测试（必须通过）
    // ============================================
    [Test]
    public void M27Compatible_EmptyStepsDoesNotThrow()
    {
        var go = new GameObject("EmptyTest");
        var mgr = go.AddComponent<TrainingManager>();
        mgr.ConfigureSteps(new List<TrainingStep>());

        Assert.DoesNotThrow(() => mgr.CompleteCurrentStep());
        Assert.AreEqual(0, mgr.CurrentStepIndex);
        Assert.IsFalse(mgr.IsCompleted);
    }

    [Test]
    public void M27Compatible_ResetAfterCompletionWorks()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();
        Assert.IsTrue(mgr.IsCompleted);

        mgr.ResetTraining();
        Assert.AreEqual(0, mgr.CurrentStepIndex);
        Assert.IsFalse(mgr.IsCompleted);
        Assert.AreEqual(TrainingPhase.Disassembly, mgr.CurrentPhase);
    }

    [Test]
    public void M27Compatible_PhaseChangedOnPhaseSwitch()
    {
        var mgr = CreateManagerWithSteps(2);
        int phaseChangeCount = 0;
        mgr.OnPhaseChanged += (p) => { phaseChangeCount++; };

        mgr.CompleteCurrentStep(); // 0, 拆卸中
        Assert.AreEqual(0, phaseChangeCount, "同阶段内不应触发");

        mgr.CompleteCurrentStep(); // 1 → 进入组装
        Assert.AreEqual(1, phaseChangeCount);

        mgr.CompleteCurrentStep(); // 2, 组装中
        Assert.AreEqual(1, phaseChangeCount, "同阶段内不应触发");

        mgr.CompleteCurrentStep(); // 3 → completed
        Assert.AreEqual(2, phaseChangeCount, "进入 complete 应再触发一次");
    }
}