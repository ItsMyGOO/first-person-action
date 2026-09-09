using System;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 蓄力累计器（弓手拉弓；纯逻辑可单测）。
/// 进度满格前按两档阈值升 0/1/2 级；松开走 ConsumeRelease（按级放箭），
/// 技能/闪避/收弓打断走 Cancel（不放箭、直接复位）。
/// </summary>
public sealed class ChargeAccumulator
{
    private readonly float _fullSeconds;
    private readonly float _level1Threshold;
    private readonly float _level2Threshold;

    public ChargeAccumulator(
        float fullSeconds,
        float level1Threshold = 0.33f,
        float level2Threshold = 0.66f
    )
    {
        _fullSeconds = MathF.Max(0.01f, fullSeconds);
        _level1Threshold = level1Threshold;
        _level2Threshold = level2Threshold;
    }

    public bool IsCharging { get; private set; }

    public float Elapsed { get; private set; }

    public float Progress01 => IsCharging ? MathF.Min(1f, Elapsed / _fullSeconds) : 0f;

    public int Level =>
        !IsCharging ? 0
        : Progress01 >= _level2Threshold ? 2
        : Progress01 >= _level1Threshold ? 1
        : 0;

    public void Begin()
    {
        IsCharging = true;
        Elapsed = 0f;
    }

    public void Tick(float dt)
    {
        if (IsCharging)
        {
            Elapsed += dt;
        }
    }

    /// <summary>松开射出：返回当前蓄力等级并复位。</summary>
    public int ConsumeRelease()
    {
        int level = Level;
        Stop();
        return level;
    }

    /// <summary>打断（技能/闪避/收弓）：复位，不放箭。</summary>
    public void Cancel() => Stop();

    private void Stop()
    {
        IsCharging = false;
        Elapsed = 0f;
    }
}
