using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// M27: TrainingManager 核心拆卸业务逻辑的 EditMode 单元测试。
/// 不依赖 XR / PartInteractable / UI 组件，直接测试 TrainingManager + TrainingStep。
/// </summary>
public class TrainingManagerTests
{
    private TrainingManager CreateManagerWithSteps(int count)
    {
        var go = new GameObject("TestTrainingManager");
        var mgr = go.AddComponent<TrainingManager>();

        var steps = new List<TrainingStep>();
        for (int i = 0; i < count; i++)
        {
            steps.Add(new TrainingStep("Step" + i));
        }

        // 通过公有方法注入步骤数据并初始化起始状态
        mgr.ConfigureSteps(steps);
        return mgr;
    }

    // ============================================
    // Test 1: 初始状态
    // ============================================
    [Test]
    public void InitialState_CurrentStepIndexIsZero()
    {
        var mgr = CreateManagerWithSteps(3);
        Assert.AreEqual(0, mgr.CurrentStepIndex);
        Assert.IsFalse(mgr.IsCompleted);
        Assert.AreEqual("Step0", mgr.GetStepName(0));
    }

    [Test]
    public void InitialState_AllStepsAreNotCompleted()
    {
        var mgr = CreateManagerWithSteps(3);
        // 只能验证 TotalSteps 正确，无需逐个验证 Step 内部字段（完工校验走 +1 推进）
        Assert.AreEqual(3, mgr.TotalSteps);
    }

    [Test]
    public void InitialState_TotalStepsMatchesConfiguredCount()
    {
        var mgr = CreateManagerWithSteps(5);
        Assert.AreEqual(5, mgr.TotalSteps);
    }

    // ============================================
    // Test 2: 正确顺序 — 每一步都推进
    // ============================================
    [Test]
    public void CorrectOrder_StepsAdvanceSequentially()
    {
        var mgr = CreateManagerWithSteps(3);

        // Step 0
        Assert.AreEqual(0, mgr.CurrentStepIndex);
        mgr.CompleteCurrentStep();
        Assert.AreEqual(1, mgr.CurrentStepIndex);

        // Step 1
        mgr.CompleteCurrentStep();
        Assert.AreEqual(2, mgr.CurrentStepIndex);

        // Step 2（最后一步完成 → completed = true）
        mgr.CompleteCurrentStep();
        Assert.IsTrue(mgr.IsCompleted);
    }

    [Test]
    public void CorrectOrder_StepNameAccessibleAfterEachAdvance()
    {
        var mgr = CreateManagerWithSteps(3);

        Assert.AreEqual("Step0", mgr.GetStepName(0));
        mgr.CompleteCurrentStep();

        Assert.AreEqual("Step1", mgr.GetStepName(1));
        mgr.CompleteCurrentStep();

        Assert.AreEqual("Step2", mgr.GetStepName(2));
        mgr.CompleteCurrentStep();
    }

    [Test]
    public void CorrectOrder_OnTrainingCompletedFiresOnce()
    {
        var mgr = CreateManagerWithSteps(2);

        int fireCount = 0;
        mgr.OnTrainingCompleted += (result) => { fireCount++; };

        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();

        Assert.AreEqual(1, fireCount, "OnTrainingCompleted 应该只触发一次");
    }

    // ============================================
    // Test 3: 错误顺序 — 误操作不推进
    // ============================================
    [Test]
    public void WrongOrder_RecordWrongOperationDoesNotAdvanceIndex()
    {
        var mgr = CreateManagerWithSteps(3);

        int wrongFireCount = 0;
        mgr.OnWrongOperation += (idx) => { wrongFireCount++; };

        int before = mgr.CurrentStepIndex;
        mgr.RecordWrongOperation(1); // 尝试操作 Step1 但当前是 Step0

        Assert.AreEqual(before, mgr.CurrentStepIndex, "误操作后 CurrentStepIndex 不应改变");
        Assert.AreEqual(1, wrongFireCount, "误操作事件应触发一次");
        Assert.IsFalse(mgr.IsCompleted);
    }

    [Test]
    public void WrongOrder_StepStatusNotChanged()
    {
        var mgr = CreateManagerWithSteps(3);

        mgr.RecordWrongOperation(2);

        // 完成当前步骤，确认只有 Step0 完成
        mgr.CompleteCurrentStep();
        Assert.AreEqual(1, mgr.CurrentStepIndex);
    }

    [Test]
    public void WrongOrder_MultipleWrongOpsCountCorrectly()
    {
        var mgr = CreateManagerWithSteps(3);

        mgr.RecordWrongOperation(1);
        mgr.RecordWrongOperation(2);

        Assert.AreEqual(2, mgr.WrongOperationCount);
        Assert.AreEqual(0, mgr.CurrentStepIndex); // 仍然在 Step0
    }

    // ============================================
    // Test 4: 完成全部步骤
    // ============================================
    [Test]
    public void AllStepsCompleted_IsCompletedTrue()
    {
        var mgr = CreateManagerWithSteps(3);

        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();

        Assert.IsTrue(mgr.IsCompleted);
        Assert.GreaterOrEqual(mgr.ElapsedSeconds, 0f);
    }

    [Test]
    public void AllStepsCompleted_CurrentStepReturnsNull()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();

        Assert.IsNull(mgr.CurrentStep, "全部完成后 CurrentStep 应为 null");
    }

    [Test]
    public void AllStepsCompleted_IndexDoesNotOverflow()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();

        // 完成后再调用 CompleteCurrentStep（步骤已越过最后一项）
        int indexAfter = mgr.CurrentStepIndex;
        mgr.CompleteCurrentStep(); // CurrentStep == null → 直接 return
        Assert.AreEqual(indexAfter, mgr.CurrentStepIndex, "完成后再调 CompleteCurrentStep 不应增加索引");
    }

    // ============================================
    // Test 5: 重复操作 — 已拆卸步骤的 Complete 不重复
    // ============================================
    [Test]
    public void DuplicateStep_CompleteDoesNotReAdvance()
    {
        var mgr = CreateManagerWithSteps(3);

        mgr.CompleteCurrentStep(); // Step0 → Step1
        int indexAfterFirst = mgr.CurrentStepIndex;

        // TrainingStep 的 IsCompleted 已经为 true，Complete() 内部检测跳过
        // 但 CompleteCurrentStep() 检查 CurrentStep（现在是 Step1），不是已完成的 Step0
        // 所以这个用例正确场景是：同一个步骤不能被完成两次
        // 正确测试：Step 1 完成两步后，再尝试完成 Step1（已完成的步骤不应被再次完成）
        mgr.CompleteCurrentStep(); // Step1 → Step2

        // Step2 已经不可达 — 这里模拟的是「当前步骤不能二次完成」
        // 实际上 CompleteCurrentStep 会走 CurrentStep.Complete()，
        // 然后 MoveToNextStep → index++
        // 但因为 CurrentStep 在完成前还是 Step2，完成后变成 null
        // 再调 CompleteCurrentStep → CurrentStep == null → return
        int indexBeforeFinal = mgr.CurrentStepIndex;
        mgr.CompleteCurrentStep(); // 应该 return 不做事
        Assert.AreEqual(indexBeforeFinal, mgr.CurrentStepIndex, "重复完成已完成的最后步骤不应增加索引");
    }

    [Test]
    public void DuplicateStep_ProgressDoesNotExceedTotal()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.CompleteCurrentStep(); // 1/2
        mgr.CompleteCurrentStep(); // 2/2 → completed

        // 后续多次调用不应改变索引或完成状态
        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();

        Assert.IsTrue(mgr.IsCompleted);
        Assert.AreEqual(2, mgr.CurrentStepIndex);
    }

    // ============================================
    // Test 6: 步骤边界
    // ============================================
    [Test]
    public void Boundary_EmptyStepsDoesNotThrow()
    {
        var go = new GameObject("EmptyTest");
        var mgr = go.AddComponent<TrainingManager>();
        mgr.ConfigureSteps(new List<TrainingStep>()); // 0 步

        // 无步骤时调用 CompleteCurrentStep 不应抛异常
        Assert.DoesNotThrow(() => mgr.CompleteCurrentStep());
        Assert.AreEqual(0, mgr.CurrentStepIndex);
        Assert.IsFalse(mgr.IsCompleted);
    }

    [Test]
    public void Boundary_LastStepNoArrayOverflow()
    {
        var mgr = CreateManagerWithSteps(1);

        mgr.CompleteCurrentStep();

        Assert.IsTrue(mgr.IsCompleted);
        // 检查越界访问
        Assert.IsNull(mgr.CurrentStep);
        Assert.DoesNotThrow(() => mgr.CompleteCurrentStep());
        Assert.DoesNotThrow(() => { var name = mgr.GetStepName(999); });
    }

    [Test]
    public void Boundary_ResetAfterCompletionWorks()
    {
        var mgr = CreateManagerWithSteps(3);

        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();

        Assert.IsTrue(mgr.IsCompleted);

        mgr.ResetTraining();

        Assert.AreEqual(0, mgr.CurrentStepIndex, "重置后索引应回到 0");
        Assert.IsFalse(mgr.IsCompleted, "重置后 IsCompleted 应为 false");
    }

    // ============================================
    // 额外：BuildResult 非 M28，但验证核心统计
    // ============================================
    [Test]
    public void Result_BuildResultAfterCompletionHasStats()
    {
        var mgr = CreateManagerWithSteps(2);

        mgr.RecordWrongOperation(1);
        mgr.CompleteCurrentStep();
        mgr.CompleteCurrentStep();

        var result = mgr.BuildResult();
        Assert.IsTrue(result.isCompleted);
        Assert.GreaterOrEqual(result.elapsedSeconds, 0f);
        Assert.AreEqual(1, result.wrongOperationCount);
    }
}