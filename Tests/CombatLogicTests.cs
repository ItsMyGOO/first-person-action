using System.Collections.Generic;
using FirstPersonAction.Combat;
using FirstPersonAction.Core;
using Godot;
using Xunit;

namespace FirstPersonAction.Tests;

public class InputCommandBufferTests
{
    private static InputCommand Cmd(InputCommandKind kind) => new InputCommand { Kind = kind };

    [Fact]
    public void Consume_ReturnsEarliestMatchingCommand()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        buf.Push(Cmd(InputCommandKind.Attack), 20);

        Assert.True(buf.TryConsume(InputCommandKind.Attack, 30, out var first));
        Assert.Equal(InputCommandKind.Attack, first.Kind);
        Assert.True(buf.TryConsume(InputCommandKind.Attack, 30, out _));
        Assert.False(buf.TryConsume(InputCommandKind.Attack, 30, out _));
    }

    [Fact]
    public void Command_ExpiresAfterWindow()
    {
        var buf = new InputCommandBuffer { BufferWindowMs = 150 };
        buf.Push(Cmd(InputCommandKind.Dodge), 0);
        Assert.True(buf.TryConsume(InputCommandKind.Dodge, 149, out _));

        buf.Push(Cmd(InputCommandKind.Dodge), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Dodge, 151, out _));
    }

    [Fact]
    public void Kinds_AreIndependent()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Dodge, 10, out _));
        Assert.True(buf.TryConsume(InputCommandKind.Attack, 10, out _));
    }

    [Fact]
    public void Move_And_None_AreNotBuffered()
    {
        var buf = new InputCommandBuffer();
        buf.Push(new InputCommand { Kind = InputCommandKind.Move, Axis = Vector2.Up }, 0);
        buf.Push(Cmd(InputCommandKind.None), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Move, 10, out _));
        Assert.False(buf.TryConsume(InputCommandKind.None, 10, out _));
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        buf.Clear();
        Assert.False(buf.TryConsume(InputCommandKind.Attack, 10, out _));
    }
}

public class MeleeComboTrackerTests
{
    private static readonly ComboStageData[] Stages =
    {
        new()
        {
            Startup = 0.10f,
            Active = 0.12f,
            Recovery = 0.30f,
            CancelAfter = 0.10f,
        },
        new()
        {
            Startup = 0.10f,
            Active = 0.12f,
            Recovery = 0.32f,
            CancelAfter = 0.10f,
        },
        new()
        {
            Startup = 0.14f,
            Active = 0.16f,
            Recovery = 0.55f,
            CancelAfter = 0.25f,
        },
    };

    private static MeleeComboTracker NewTracker() => new(Stages);

    [Fact]
    public void StartsInactive_AdvanceEntersStage0Startup()
    {
        var t = NewTracker();
        Assert.False(t.IsActive);
        Assert.True(t.TryAdvance());
        Assert.Equal(0, t.StageIndex);
        Assert.Equal(ComboStagePhase.Startup, t.Phase);
    }

    [Fact]
    public void Tick_AdvancesStartupToActiveToRecovery()
    {
        var t = NewTracker();
        t.TryAdvance();
        t.Tick(0.10f);
        Assert.Equal(ComboStagePhase.Active, t.Phase);
        t.Tick(0.12f);
        Assert.Equal(ComboStagePhase.Recovery, t.Phase);
        t.Tick(0.30f);
        Assert.False(t.IsActive);
    }

    [Fact]
    public void HitWindow_CanBeConsumedOnlyOncePerStage()
    {
        var t = NewTracker();
        t.TryAdvance();
        t.Tick(0.05f); // Startup 中段
        Assert.False(t.CanApplyHit);
        t.Tick(0.05f); // 进入 Active
        Assert.True(t.CanApplyHit);
        Assert.True(t.ConsumeHit());
        Assert.False(t.ConsumeHit());
    }

    [Fact]
    public void Chain_BlockedBeforeCancelAfter_AfterItAllowed()
    {
        var t = NewTracker();
        t.TryAdvance();
        t.Tick(0.10f + 0.12f + 0.05f); // Recovery 0.05s < CancelAfter 0.10s
        Assert.False(t.TryAdvance());
        t.Tick(0.06f); // 0.11s ≥ CancelAfter
        Assert.True(t.IsInCancelWindow);
        Assert.True(t.TryAdvance());
        Assert.Equal(1, t.StageIndex);
    }

    [Fact]
    public void Finisher_ChainsBackToStage0()
    {
        var t = NewTracker();
        t.TryAdvance(); // -> 0
        t.Tick(Stages[0].Startup + Stages[0].Active + Stages[0].CancelAfter + 0.01f);
        t.TryAdvance(); // -> 1
        t.Tick(Stages[1].Startup + Stages[1].Active + Stages[1].CancelAfter + 0.01f);
        t.TryAdvance(); // -> 2 (finisher)
        Assert.Equal(2, t.StageIndex);
        t.Tick(Stages[2].Startup + Stages[2].Active + Stages[2].CancelAfter + 0.01f);
        t.TryAdvance(); // 末段之后回到第 1 段
        Assert.Equal(0, t.StageIndex);
    }

    [Fact]
    public void Reset_InterruptsCombo()
    {
        var t = NewTracker();
        t.TryAdvance();
        t.Tick(0.05f);
        t.Reset();
        Assert.False(t.IsActive);
        Assert.True(t.TryAdvance());
        Assert.Equal(0, t.StageIndex);
    }
}

public class MeleeArcQueryTests
{
    private sealed class Target
    {
        public Vector3 Pos;
    }

    [Fact]
    public void FrontTarget_InRange_IsHit()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, -2) } };
        List<Target> hits = MeleeArcQuery.FindHits(
            Vector3.Zero,
            new Vector3(0, 0, -1),
            2.5f,
            55f,
            targets,
            t => t.Pos
        );
        Assert.Single(hits);
    }

    [Fact]
    public void BehindTarget_Missed()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, 2) } };
        List<Target> hits = MeleeArcQuery.FindHits(
            Vector3.Zero,
            new Vector3(0, 0, -1),
            2.5f,
            55f,
            targets,
            t => t.Pos
        );
        Assert.Empty(hits);
    }

    [Fact]
    public void OutOfRangeTarget_Missed()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, -3) } };
        List<Target> hits = MeleeArcQuery.FindHits(
            Vector3.Zero,
            new Vector3(0, 0, -1),
            2.5f,
            55f,
            targets,
            t => t.Pos
        );
        Assert.Empty(hits);
    }

    [Fact]
    public void SideTarget_BeyondHalfAngle_Missed()
    {
        // 距离 2、正侧方 = 90° 夹角 > 55° 半角
        var targets = new List<Target> { new() { Pos = new Vector3(2, 1, 0) } };
        List<Target> hits = MeleeArcQuery.FindHits(
            Vector3.Zero,
            new Vector3(0, 0, -1),
            2.5f,
            55f,
            targets,
            t => t.Pos
        );
        Assert.Empty(hits);
    }

    [Fact]
    public void HeightDifference_BeyondTolerance_Missed()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 5, -2) } };
        List<Target> hits = MeleeArcQuery.FindHits(
            Vector3.Zero,
            new Vector3(0, 0, -1),
            2.5f,
            55f,
            targets,
            t => t.Pos
        );
        Assert.Empty(hits);
    }

    [Fact]
    public void ExcludedSource_NeverHit_EvenAtOrigin()
    {
        // 玩家自伤回归：攻击源与原点重合（distance≈0 的巧合守卫曾挡住它），
        // 显式 exclude 后即使攻击源带水平偏移也不会命中自己
        var source = new Target { Pos = new Vector3(0.4f, 1, -0.2f) };
        var enemy = new Target { Pos = new Vector3(0.4f, 1, -1.8f) };
        var targets = new List<Target> { source, enemy };
        List<Target> hits = MeleeArcQuery.FindHits(
            new Vector3(0.4f, 1, -0.2f),
            new Vector3(0, 0, -1),
            2.5f,
            55f,
            targets,
            t => t.Pos,
            exclude: source
        );
        Assert.Single(hits);
        Assert.Same(enemy, hits[0]);
    }

    [Fact]
    public void ResultsBuffer_IsReusedAndCleared()
    {
        // 复用缓冲（热路径零分配）：上一次的结果不能残留到下一次
        var buffer = new List<Target>();
        var far = new List<Target> { new() { Pos = new Vector3(0, 1, -50) } };
        MeleeArcQuery.FindHits(
            Vector3.Zero,
            new Vector3(0, 0, -1),
            2.5f,
            55f,
            far,
            t => t.Pos,
            results: buffer
        );
        Assert.Empty(buffer);

        var near = new List<Target> { new() { Pos = new Vector3(0, 1, -2) } };
        List<Target> hits = MeleeArcQuery.FindHits(
            Vector3.Zero,
            new Vector3(0, 0, -1),
            2.5f,
            55f,
            near,
            t => t.Pos,
            results: buffer
        );
        Assert.Same(buffer, hits);
        Assert.Single(hits);
    }
}

public class HitReactionMachineTests
{
    private static HitReactionMachine NewMachine() => new(60f, 0.28f, 2.5f);

    [Fact]
    public void PoiseBreak_CausesDowned()
    {
        var m = NewMachine();
        m.ApplyHit(20f);
        Assert.Equal(HitReactionState.Staggered, m.State);
        m.ApplyHit(45f); // 韧性 60 - 20 - 45 < 0
        Assert.Equal(HitReactionState.Downed, m.State);
        Assert.True(m.IsDowned);
    }

    [Fact]
    public void Stagger_RecoversAfterDuration()
    {
        var m = NewMachine();
        m.ApplyHit(10f);
        Assert.Equal(HitReactionState.Staggered, m.State);
        m.Tick(0.10f);
        Assert.Equal(HitReactionState.Staggered, m.State);
        m.ApplyHit(10f); // 刷新硬直
        m.Tick(0.20f);
        Assert.Equal(HitReactionState.Staggered, m.State);
        m.Tick(0.09f);
        Assert.Equal(HitReactionState.Normal, m.State);
    }

    [Fact]
    public void Downed_RecoversWithFullPoise()
    {
        var m = NewMachine();
        m.ApplyHit(60f);
        Assert.True(m.IsDowned);
        Assert.Equal(0f, m.Poise);
        m.Tick(2.4f);
        Assert.True(m.IsDowned);
        m.Tick(0.2f);
        Assert.Equal(HitReactionState.Normal, m.State);
        Assert.Equal(60f, m.Poise);
    }

    [Fact]
    public void Downed_IgnoresFurtherHitReactions()
    {
        var m = NewMachine();
        m.ApplyHit(60f);
        m.Tick(0.5f);
        m.ApplyHit(50f); // 倒地中再被击：韧性/状态不变（可被处决窗口不被打断）
        Assert.Equal(HitReactionState.Downed, m.State);
        Assert.Equal(0f, m.Poise);
    }
}
