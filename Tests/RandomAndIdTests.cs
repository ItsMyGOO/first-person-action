using GodotGameTemplate.Core;
using Xunit;

namespace GodotGameTemplate.Tests;

public class SeededRandomTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextInt(0, 1000), b.NextInt(0, 1000));
        }
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentSequence()
    {
        var a = new SeededRandom(1);
        var b = new SeededRandom(2);
        Assert.NotEqual(a.NextInt(0, int.MaxValue), b.NextInt(0, int.MaxValue));
    }

    [Fact]
    public void Reseed_RestartsSequence()
    {
        var a = new SeededRandom(7);
        int first = a.NextInt(0, 1000);
        a.Reseed(7);
        Assert.Equal(first, a.NextInt(0, 1000));
    }
}

public class EntityIdTests
{
    [Fact]
    public void Equality_WorksByValue()
    {
        var a = new EntityId(5);
        var b = new EntityId(5);
        var c = new EntityId(6);
        Assert.True(a == b);
        Assert.True(a != c);
        Assert.False(a.IsNone);
        Assert.True(EntityId.None.IsNone);
    }
}
