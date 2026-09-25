using System.Collections.Generic;
using FirstPersonAction.Spatial;
using Godot;
using Xunit;

namespace FirstPersonAction.Tests;

public class SpatialResolverTests
{
    /// <summary>构造一对相距 dist 的单位（沿 +Z/-Z 方向摆放）。</summary>
    private static (SpatialBody A, SpatialBody B) Pair(
        float dist,
        float massA = 100f,
        float resA = 100f,
        float massB = 100f,
        float resB = 100f,
        float forceA = 100f,
        float forceB = 100f
    )
    {
        var a = new SpatialBody(0.5f, massA, resA, forceA)
        {
            Position = new Vector3(0, 0, -dist / 2f),
        };
        var b = new SpatialBody(0.5f, massB, resB, forceB)
        {
            Position = new Vector3(0, 0, dist / 2f),
        };
        return (a, b);
    }

    [Fact]
    public void MassSplit_MatchesDesignDocExample()
    {
        // 阵型文档第 20 节：Player Mass=100 vs Enemy Mass=300，穿透 0.4 → 玩家修正 75%、敌人 25%
        (SpatialBody a, SpatialBody b) = Pair(0.6f, massA: 100f, massB: 300f);
        SpatialResolver.Resolve(new List<SpatialBody> { a, b });

        Assert.Equal(0.30f, a.Correction.Length(), 2); // 0.4 * 75%
        Assert.Equal(0.10f, b.Correction.Length(), 2); // 0.4 * 25%
        // 方向相反：a 在 -Z 侧沿 -Z 远离 b，b 沿 +Z 远离 a
        Assert.True(a.Correction.Z < 0);
        Assert.True(b.Correction.Z > 0);
    }

    [Fact]
    public void PushForce_GreaterThanResistance_YieldsOtherFully()
    {
        // 冲锋者 force 500 > 对方抗性 250 → 对方全额让位，自己不动
        (SpatialBody a, SpatialBody b) = Pair(0.6f, resB: 250f, forceA: 500f);
        SpatialResolver.Resolve(new List<SpatialBody> { a, b });

        Assert.Equal(0.40f, b.Correction.Length(), 2);
        Assert.Equal(0f, a.Correction.Length(), 2);
    }

    [Fact]
    public void HeavyResistance_BlocksCharger_ChargerYieldsMostly()
    {
        // 冲锋者 force 500 打不穿抗性 800 的重单位 → 逆质量分摊，冲锋者让大头
        (SpatialBody a, SpatialBody b) = Pair(
            0.6f,
            massA: 120f,
            resB: 800f,
            massB: 800f,
            forceA: 500f
        );
        SpatialResolver.Resolve(new List<SpatialBody> { a, b });

        float shareA = 800f / (120f + 800f); // a（冲锋者）承担 87%
        Assert.Equal(0.4f * shareA, a.Correction.Length(), 2);
        Assert.Equal(0.4f * (1f - shareA), b.Correction.Length(), 2);
    }

    [Fact]
    public void Exempt_BodiesSkipPair()
    {
        (SpatialBody a, SpatialBody b) = Pair(0.6f);
        a.SpatialExempt = true;
        SpatialResolver.Resolve(new List<SpatialBody> { a, b });

        Assert.Equal(Vector3.Zero, a.Correction);
        Assert.Equal(Vector3.Zero, b.Correction);
    }

    [Fact]
    public void NoOverlap_NoCorrection()
    {
        (SpatialBody a, SpatialBody b) = Pair(1.2f);
        SpatialResolver.Resolve(new List<SpatialBody> { a, b });

        Assert.Equal(Vector3.Zero, a.Correction);
        Assert.Equal(Vector3.Zero, b.Correction);
    }

    [Fact]
    public void FullOverlap_PicksFallbackDirection()
    {
        var a = new SpatialBody(0.5f, 100f, 100f, 100f) { Position = Vector3.Zero };
        var b = new SpatialBody(0.5f, 100f, 100f, 100f) { Position = Vector3.Zero };
        SpatialResolver.Resolve(new List<SpatialBody> { a, b });

        Assert.Equal(0.5f, a.Correction.Length(), 2);
        Assert.Equal(0.5f, b.Correction.Length(), 2);
    }
}
