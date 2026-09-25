using System.Collections.Generic;
using FirstPersonAction.Combat;
using Godot;
using Xunit;

namespace FirstPersonAction.Tests;

/// <summary>处决触发谓词（规格 §5）：45° 锥形 + 0.8~1.8m 距离带 + 可处决状态过滤。</summary>
public class ExecutionQueryTests
{
    private sealed class Target
    {
        public Vector3 Pos;
        public bool Ready = true;
    }

    private static Target? Find(
        Vector3 origin,
        Vector3 forward,
        List<Target> targets,
        float min = 0.8f,
        float max = 1.8f,
        float halfAngle = 45f
    ) =>
        ExecutionQuery.FindTarget(
            origin,
            forward,
            targets,
            t => t.Pos,
            t => t.Ready,
            min,
            max,
            halfAngle
        );

    [Fact]
    public void InBand_InCone_Ready_IsFound()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, -1.2f) } };
        Target? hit = Find(Vector3.Zero, new Vector3(0, 0, -1), targets);
        Assert.NotNull(hit);
        Assert.Same(targets[0], hit);
    }

    [Fact]
    public void MultipleCandidates_NearestIsPicked()
    {
        var near = new Target { Pos = new Vector3(0, 1, -1.0f) };
        var far = new Target { Pos = new Vector3(0, 1, -1.7f) };
        Target? hit = Find(Vector3.Zero, new Vector3(0, 0, -1), new List<Target> { far, near });
        Assert.Same(near, hit);
    }

    [Theory]
    [InlineData(0.5f)] // 贴脸：带内下界
    [InlineData(2.5f)] // 超距：带外上界
    public void OutOfDistanceBand_IsRejected(float distance)
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, -distance) } };
        Assert.Null(Find(Vector3.Zero, new Vector3(0, 0, -1), targets));
    }

    [Fact]
    public void NotReady_IsRejected()
    {
        // 未倒地（韧性未清零）的敌人不可处决——即使站位完美
        var targets = new List<Target>
        {
            new() { Pos = new Vector3(0, 1, -1.2f), Ready = false },
        };
        Assert.Null(Find(Vector3.Zero, new Vector3(0, 0, -1), targets));
    }

    [Fact]
    public void BeyondCone_IsRejected()
    {
        // 距离 1.2、正侧方 = 90° 夹角 > 45° 半角
        var targets = new List<Target> { new() { Pos = new Vector3(1.2f, 1, 0) } };
        Assert.Null(Find(Vector3.Zero, new Vector3(0, 0, -1), targets));
    }

    [Fact]
    public void Behind_IsRejected()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, 1.2f) } };
        Assert.Null(Find(Vector3.Zero, new Vector3(0, 0, -1), targets));
    }
}

/// <summary>处决收敛（规格 §5 第 2 步）：双向缓动、35/65 分摊、无瞬移。</summary>
public class ExecutionConvergenceTests
{
    [Fact]
    public void SampleAtZero_EqualsStartPositions()
    {
        var conv = new ExecutionConvergence(new Vector3(0, 0.2f, 5f), new Vector3(0, 0.2f, 0f));
        conv.Sample(0f, out Vector3 player, out Vector3 enemy);
        AssertVec3(new Vector3(0, 0, 5f), player);
        AssertVec3(new Vector3(0, 0, 0f), enemy);
    }

    [Fact]
    public void SampleAtOne_BothAtMeetingPoint_WithConfiguredShare()
    {
        var conv = new ExecutionConvergence(
            new Vector3(0, 0.2f, 5f),
            new Vector3(0, 0.2f, 0f),
            playerShare: 0.35f
        );
        conv.Sample(1f, out Vector3 player, out Vector3 enemy);

        // 玩家走 35%：从 5m 到 3.25m；敌人走 65%：从 0m 到 3.25m——相遇点重合
        AssertVec3(new Vector3(0, 0, 3.25f), player);
        AssertVec3(new Vector3(0, 0, 3.25f), enemy);
    }

    [Fact]
    public void SampleIsContinuous_NoTeleport()
    {
        // 相邻采样步的位移必须远小于总程（无瞬移/无可见吸附）——
        // 取 60 步，任意相邻步位移不超过总程的 1/10
        var conv = new ExecutionConvergence(new Vector3(0, 0, 0f), new Vector3(1.0f, 0, 0f));
        conv.Sample(0f, out Vector3 prevPlayer, out _);
        for (int i = 1; i <= 60; i++)
        {
            conv.Sample(i / 60f, out Vector3 player, out _);
            Assert.True(
                (player - prevPlayer).Length() < 0.1f,
                $"第 {i} 步位移 {(player - prevPlayer).Length():F3} 超限（瞬移）"
            );
            prevPlayer = player;
        }
    }

    private static void AssertVec3(Vector3 expected, Vector3 actual, int precision = 4)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }
}
