using System.Linq;
using FirstPersonAction.Combat;
using Godot;
using Xunit;

namespace FirstPersonAction.Tests;

/// <summary>阵型纯逻辑（M5 §1.1）：槽位换算 / 朝向滞回 / 死亡开缺口。</summary>
public class FormationLayoutTests
{
    private static void AssertVec3(Vector3 expected, Vector3 actual, int precision = 3)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SlotWorld_YawZero_MatchesLocalOffset(int slot)
    {
        Vector3 center = new(10f, 0f, 5f);

        AssertVec3(
            center + FormationLayout.SlotLocal(slot),
            FormationLayout.SlotWorld(center, 0f, slot)
        );
    }

    [Fact]
    public void SlotWorld_KnownSolutions()
    {
        Vector3 center = Vector3.Zero;

        // yaw=0：正面朝 -Z，前中在正前方 1.5m，后排右在身后
        AssertVec3(new Vector3(0f, 0f, -1.5f), FormationLayout.SlotWorld(center, 0f, 1));
        AssertVec3(new Vector3(0.7f, 0f, 1.2f), FormationLayout.SlotWorld(center, 0f, 4));

        // yaw=90°：正面转向 -X，前左(-1.4,0,-1.5) 随阵旋转
        AssertVec3(
            new Vector3(-1.5f, 0f, 1.4f),
            FormationLayout.SlotWorld(center, Mathf.Pi / 2f, 0)
        );

        // yaw=180°：正面转向 +Z，前左镜像到 (+1.4, 0, +1.5)
        AssertVec3(new Vector3(1.4f, 0f, 1.5f), FormationLayout.SlotWorld(center, Mathf.Pi, 0));
    }

    [Fact]
    public void Layout_HasThreeFrontAndTwoBackSlots()
    {
        Assert.Equal(5, FormationLayout.SlotCount);
        // 前排 3 个在本地 -Z（面向玩家侧），后排 2 个在本地 +Z
        for (int i = 0; i < 3; i++)
        {
            Assert.True(FormationLayout.SlotLocal(i).Z < 0f);
        }

        for (int i = 3; i < 5; i++)
        {
            Assert.True(FormationLayout.SlotLocal(i).Z > 0f);
        }
    }
}

public class FormationBrainTests
{
    private static FormationBrain NewBrain() => new();

    /// <summary>当前朝向相对 expectedDeg 的短弧偏差（度）——±180 同角不误判。</summary>
    private static double AngleError(float yawDeg, float expectedDeg) =>
        System.Math.Abs(Mathf.Wrap(yawDeg - expectedDeg, -180f, 180f));

    private static void TickFor(FormationBrain brain, float seconds, Vector3 playerPos)
    {
        int steps = 60;
        for (int i = 0; i < steps; i++)
        {
            brain.Tick(seconds / steps, Vector3.Zero, playerPos);
        }
    }

    [Fact]
    public void WithinThreshold_DoesNotRotate()
    {
        FormationBrain brain = NewBrain();
        brain.Reset(0f);
        // 玩家在左前 30°（< 55° 阈值）——绕阵走圈不炮塔跟转
        TickFor(brain, 1f, new Vector3(-1f, 0f, -Mathf.Sqrt(3f)));

        Assert.Equal(0f, brain.YawDeg, 3);
    }

    [Fact]
    public void BeyondThreshold_RotatesTowardPlayerAtConfiguredSpeed()
    {
        FormationBrain brain = NewBrain();
        brain.Reset(0f);
        // 玩家在正后 +Z（偏差 180°）
        brain.Tick(0.1f, Vector3.Zero, new Vector3(0f, 0f, 1f));

        Assert.Equal(9f, brain.YawDeg, 3); // 90°/s × 0.1s
    }

    [Fact]
    public void RotatesUntilAligned_ThenStops()
    {
        FormationBrain brain = NewBrain();
        brain.Reset(0f);
        TickFor(brain, 2.5f, new Vector3(0f, 0f, 1f)); // 需 180/90=2s，2.5s 足够对齐
        Assert.True(AngleError(brain.YawDeg, 180f) < 0.01, $"实际 {brain.YawDeg}°"); // 转到位即停，不越过

        // 对齐后玩家小幅偏移（阈值内）不再转动
        TickFor(brain, 1f, new Vector3(1f, 0f, 1f));
        Assert.True(AngleError(brain.YawDeg, 180f) < 0.01);
    }

    [Fact]
    public void RotationDirection_TakesShortestArc()
    {
        FormationBrain brain = NewBrain();
        brain.Reset(0f);
        // 玩家在 -X 侧（目标朝向 yaw=+90°），短弧为正向推进
        brain.Tick(0.1f, Vector3.Zero, new Vector3(-1f, 0f, 0f));

        Assert.Equal(9f, brain.YawDeg, 3);
    }
}

public class FormationRosterTests
{
    private sealed class FakeMember : IFormationMember
    {
        public FakeMember(int slot) => SlotIndex = slot;

        public int SlotIndex { get; }

        public bool Alive { get; set; } = true;

        public Vector3 SlotPosition { get; set; }

        public float SlotFacing { get; set; }
    }

    [Fact]
    public void DeadMember_SlotLeftDangling_NoReassignNoBackfill()
    {
        FormationRoster roster = new();
        var front = new FakeMember(0);
        var center = new FakeMember(1);
        var right = new FakeMember(2);
        roster.Assign(front);
        roster.Assign(center);
        roster.Assign(right);

        center.Alive = false; // 中排盾死亡 → 缺口

        Assert.Equal(new[] { front, right }, roster.Living.ToArray()); // 存活者槽位不重分配（0 与 2 原样）
        Assert.Contains(center, roster.Members.ToArray()); // 槽位悬空，不补位
        Assert.Null(roster.Living.FirstOrDefault(m => m.SlotIndex == 1));
        Assert.Equal(0, front.SlotIndex);
        Assert.Equal(2, right.SlotIndex);
    }

    [Fact]
    public void Assign_OutOfRangeSlot_IsRejected()
    {
        // 配置回归：策划在编辑器里把 SlotIndex 配到 5/负数，必须拒绝而不是
        // 带病进入 SlotWorld 的数组索引（曾会在物理帧里每帧越界崩溃）
        FormationRoster roster = new();
        Assert.False(roster.Assign(new FakeMember(5)));
        Assert.False(roster.Assign(new FakeMember(-1)));
        Assert.False(roster.Assign(new FakeMember(FormationLayout.SlotCount)));
        Assert.Empty(roster.Members);
    }

    [Fact]
    public void Assign_DuplicateSlot_IsRejected()
    {
        FormationRoster roster = new();
        Assert.True(roster.Assign(new FakeMember(1)));
        Assert.False(roster.Assign(new FakeMember(1))); // 两个成员配同一槽位：行为未定义，拒绝
        Assert.Single(roster.Members);
    }

    [Fact]
    public void Assign_ValidSlots_AreAccepted()
    {
        FormationRoster roster = new();
        for (int slot = 0; slot < FormationLayout.SlotCount; slot++)
        {
            Assert.True(roster.Assign(new FakeMember(slot)));
        }

        Assert.Equal(FormationLayout.SlotCount, roster.Members.Count);
    }
}
