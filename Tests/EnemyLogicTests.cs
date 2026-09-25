using System;
using System.Collections.Generic;
using FirstPersonAction.Combat;
using Godot;
using Xunit;

namespace FirstPersonAction.Tests;

public class AttackCycleTests
{
    private static AttackCycle NewCycle() => new(windupSeconds: 0.5f, cooldownSeconds: 1.2f);

    private static void TickFor(AttackCycle cycle, float seconds)
    {
        for (int i = 0; i < 60; i++)
        {
            cycle.Tick(seconds / 60f);
        }
    }

    [Fact]
    public void Windup_DoesNotStrikeEarly()
    {
        AttackCycle cycle = NewCycle();
        cycle.Start();
        TickFor(cycle, 0.25f); // 前摇未结束

        Assert.Equal(AttackCyclePhase.Windup, cycle.Phase);
        Assert.False(cycle.TryConsumeStrike());
    }

    [Fact]
    public void StrikeSettlesExactlyOncePerCycle()
    {
        AttackCycle cycle = NewCycle();
        cycle.Start();
        TickFor(cycle, 0.5f); // 前摇结束

        Assert.Equal(AttackCyclePhase.Cooldown, cycle.Phase);
        Assert.True(cycle.TryConsumeStrike()); // 打击帧一次性结算
        Assert.False(cycle.TryConsumeStrike()); // 不可重复结算
    }

    [Fact]
    public void Cycle_LoopsAfterCooldown()
    {
        AttackCycle cycle = NewCycle();
        cycle.Start();
        TickFor(cycle, 0.5f);
        Assert.True(cycle.TryConsumeStrike());
        TickFor(cycle, 1.2f); // 冷却结束

        Assert.Equal(AttackCyclePhase.Windup, cycle.Phase);
        TickFor(cycle, 0.5f);
        Assert.True(cycle.TryConsumeStrike()); // 第二周期可再次结算
    }

    [Fact]
    public void RestartWhileRunning_DoesNotReset()
    {
        AttackCycle cycle = NewCycle();
        cycle.Start();
        TickFor(cycle, 0.4f);
        cycle.Start(); // 运行中重复开始应被忽略，前摇进度保留
        TickFor(cycle, 0.1f);

        Assert.True(cycle.TryConsumeStrike());
    }

    [Fact]
    public void Stop_ReturnsToIdleAndAllowsRestart()
    {
        AttackCycle cycle = NewCycle();
        cycle.Start();
        TickFor(cycle, 0.2f);
        cycle.Stop();

        Assert.Equal(AttackCyclePhase.Idle, cycle.Phase);
        Assert.False(cycle.TryConsumeStrike());

        cycle.Start();
        TickFor(cycle, 0.5f);
        Assert.True(cycle.TryConsumeStrike());
    }

    [Fact]
    public void DisplacementDuringWindup_DoesNotInterrupt()
    {
        // v1 简化记录在案：前摇内被强制位移不打断周期（无取消输入，周期只由 Tick/Stop 驱动）
        AttackCycle cycle = NewCycle();
        cycle.Start();
        TickFor(cycle, 0.3f);
        cycle.Tick(0.1f);
        TickFor(cycle, 0.1f);

        Assert.True(cycle.TryConsumeStrike());
    }
}

public class SurroundSlotAssignerTests
{
    [Fact]
    public void AssignsEvenlyAroundCircle()
    {
        float[] angles = SurroundSlotAssigner.Assign([10, 11, 12, 13]);

        Assert.Equal(4, angles.Length);
        Assert.Equal(0f, angles[0], 5);
        Assert.Equal(Mathf.Pi / 2f, angles[1], 5);
        Assert.Equal(Mathf.Pi, angles[2], 5);
        Assert.Equal(Mathf.Pi * 3f / 2f, angles[3], 5);
    }

    [Fact]
    public void StableRelativeOrderWhenSetShrinks()
    {
        float[] after = SurroundSlotAssigner.Assign([1, 2, 4]); // id=3 离场

        Assert.Equal(3, after.Length);
        // 幸存者相对角度顺序不变（不乱跳），间距仍均匀（2π/3）
        Assert.True(after[0] < after[1] && after[1] < after[2]);
        Assert.Equal(Mathf.Pi * 2f / 3f, after[1] - after[0], 5);
        Assert.Equal(Mathf.Pi * 2f / 3f, after[2] - after[1], 5);
    }

    [Fact]
    public void InputOrderDoesNotMatter()
    {
        float[] sorted = SurroundSlotAssigner.Assign([1, 2, 3]);
        float[] shuffled = SurroundSlotAssigner.Assign([3, 1, 2]);

        // 同一 id 在两种输入顺序下拿到同一槽位角度（映射稳定，返回按输入顺序对应）
        Assert.Equal(sorted[0], shuffled[1], 5); // id=1
        Assert.Equal(sorted[1], shuffled[2], 5); // id=2
        Assert.Equal(sorted[2], shuffled[0], 5); // id=3
    }

    [Fact]
    public void EmptySetReturnsEmpty()
    {
        float[] angles = SurroundSlotAssigner.Assign([]);
        Assert.Empty(angles);
    }
}

public class KiteBandTests
{
    private static readonly KiteBand Band = new(
        CombatTuning.RangedKiteNear,
        CombatTuning.RangedKiteFar
    );

    [Theory]
    [InlineData(5f, KiteAction.Retreat)] // < 7m 后退
    [InlineData(6.9f, KiteAction.Retreat)]
    [InlineData(7f, KiteAction.Hold)] // 区间边界驻留
    [InlineData(9f, KiteAction.Hold)]
    [InlineData(11f, KiteAction.Hold)]
    [InlineData(11.1f, KiteAction.Approach)] // > 11m 接近
    [InlineData(15f, KiteAction.Approach)]
    public void Decide_FollowsDistanceBand(float distance, KiteAction expected)
    {
        Assert.Equal(expected, Band.Decide(distance));
    }
}
