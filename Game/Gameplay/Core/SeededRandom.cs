using System;

namespace GodotGameTemplate.Core;

/// <summary>
/// 带种子的独立随机源。约定：任何影响模拟结果的随机数（伤害浮动、AI 决策）
/// 必须来自实例而非全局 RNG，保证联机/回放可复现——规格第 6 节第 4 条。
/// </summary>
public sealed class SeededRandom
{
    private Random _random;

    public SeededRandom(int seed) => _random = new Random(seed);

    public void Reseed(int seed) => _random = new Random(seed);

    /// <summary>[minInclusive, maxExclusive)</summary>
    public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    /// <summary>[0.0, 1.0)</summary>
    public double NextDouble() => _random.NextDouble();

    public bool Chance(double probability) => _random.NextDouble() < probability;

    public float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);
}
