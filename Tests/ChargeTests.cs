using GodotGameTemplate.Combat;
using Xunit;

namespace GodotGameTemplate.Tests;

public class ChargeAccumulatorTests
{
    private static ChargeAccumulator NewAccumulator() => new(1.1f);

    [Fact]
    public void Levels_RiseAtThresholds()
    {
        var c = NewAccumulator();
        c.Begin();
        c.Tick(0.2f); // 0.18 < 0.33
        Assert.Equal(0, c.Level);
        c.Tick(0.2f); // 0.36
        Assert.Equal(1, c.Level);
        c.Tick(0.4f); // 0.72 > 0.66
        Assert.Equal(2, c.Level);
        c.Tick(10f); // 封顶
        Assert.Equal(1.0, c.Progress01, 2);
        Assert.Equal(2, c.Level);
    }

    [Fact]
    public void ConsumeRelease_ReturnsLevel_AndResets()
    {
        var c = NewAccumulator();
        c.Begin();
        c.Tick(0.5f); // 0.45 → 1 级
        Assert.Equal(1, c.ConsumeRelease());
        Assert.False(c.IsCharging);
        Assert.Equal(0f, c.Progress01);
    }

    [Fact]
    public void Cancel_Resets_WithoutLevel()
    {
        var c = NewAccumulator();
        c.Begin();
        c.Tick(1.0f); // 已满级
        c.Cancel();
        Assert.False(c.IsCharging);
        Assert.Equal(0, c.Level);
    }

    [Fact]
    public void Tick_WithoutBegin_DoesNothing()
    {
        var c = NewAccumulator();
        c.Tick(1f);
        Assert.False(c.IsCharging);
        Assert.Equal(0f, c.Progress01);
    }
}
