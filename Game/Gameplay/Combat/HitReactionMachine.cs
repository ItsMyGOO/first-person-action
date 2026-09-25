namespace FirstPersonAction.Combat;

public enum HitReactionState
{
    Normal,
    Staggered,
    Downed,
}

/// <summary>
/// 受击状态机（纯逻辑，可单元测试；规格第 4 节，阵型系统的敌人将来直接复用）。
/// 韧性（Poise）被清零 → 倒地（= 可被处决窗口，M4 消费）；
/// 硬直可被后续命中刷新；倒地期间不再叠加韧性/硬直；倒地结束韧性回满。
/// </summary>
public sealed class HitReactionMachine
{
    private readonly float _staggerSeconds;
    private readonly float _downedSeconds;

    public HitReactionMachine(float maxPoise, float staggerSeconds, float downedSeconds)
    {
        MaxPoise = maxPoise;
        Poise = maxPoise;
        _staggerSeconds = staggerSeconds;
        _downedSeconds = downedSeconds;
    }

    public HitReactionState State { get; private set; } = HitReactionState.Normal;
    public float StateElapsed { get; private set; }
    public float MaxPoise { get; }
    public float Poise { get; private set; }
    public bool IsDowned => State == HitReactionState.Downed;

    public void ApplyHit(float poiseDamage)
    {
        if (State == HitReactionState.Downed)
        {
            return;
        }

        Poise -= poiseDamage;
        if (Poise <= 0f)
        {
            Poise = 0f;
            Enter(HitReactionState.Downed);
        }
        else
        {
            Enter(HitReactionState.Staggered);
        }
    }

    public void Tick(float dt)
    {
        StateElapsed += dt;
        if (State == HitReactionState.Staggered && StateElapsed >= _staggerSeconds)
        {
            Enter(HitReactionState.Normal);
        }
        else if (State == HitReactionState.Downed && StateElapsed >= _downedSeconds)
        {
            Poise = MaxPoise;
            Enter(HitReactionState.Normal);
        }
    }

    private void Enter(HitReactionState state)
    {
        State = state;
        StateElapsed = 0f;
    }
}
