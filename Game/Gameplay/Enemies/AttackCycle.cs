namespace GodotGameTemplate.Combat;

public enum AttackCyclePhase
{
    Idle,
    Windup,
    Cooldown,
}

/// <summary>
/// 纯逻辑攻击周期（TDD，M4 §4）：前摇(预告) → 打击帧(一次性结算) → 冷却 → 循环。
/// 调用方在 TryConsumeStrike() 返回 true 的帧做锥形查询结算伤害。
/// v1 简化记录在案：前摇内被强制位移不打断周期——周期只由 Tick/Stop 驱动。
/// </summary>
public sealed class AttackCycle
{
    private const float Epsilon = 0.0005f; // 抵消 dt 累加的浮点误差
    private readonly float _windupSeconds;
    private readonly float _cooldownSeconds;
    private float _phaseElapsed;
    private bool _strikePending;

    public AttackCycle(float windupSeconds, float cooldownSeconds)
    {
        _windupSeconds = windupSeconds;
        _cooldownSeconds = cooldownSeconds;
    }

    public AttackCyclePhase Phase { get; private set; } = AttackCyclePhase.Idle;

    /// <summary>当前阶段已进行秒数（前摇预告表现用它做进度）。</summary>
    public float PhaseElapsed => _phaseElapsed;

    public float WindupSeconds => _windupSeconds;

    /// <summary>从空闲进入前摇；已在运行中则忽略（避免进度被重置抖动）。</summary>
    public void Start()
    {
        if (Phase != AttackCyclePhase.Idle)
        {
            return;
        }

        Enter(AttackCyclePhase.Windup);
    }

    /// <summary>离开目标/死亡等原因：回到空闲，未消费的打击作废。</summary>
    public void Stop()
    {
        Enter(AttackCyclePhase.Idle);
    }

    public void Tick(float dt)
    {
        if (Phase == AttackCyclePhase.Idle)
        {
            return;
        }

        _phaseElapsed += dt;
        if (Phase == AttackCyclePhase.Windup && _phaseElapsed >= _windupSeconds - Epsilon)
        {
            _strikePending = true;
            Enter(AttackCyclePhase.Cooldown);
        }
        else if (Phase == AttackCyclePhase.Cooldown && _phaseElapsed >= _cooldownSeconds - Epsilon)
        {
            Enter(AttackCyclePhase.Windup); // 循环下一周期（未消费的过期打击作废）
        }
    }

    /// <summary>打击帧结算：每周期只有一次返回 true，由调用方结算伤害。</summary>
    public bool TryConsumeStrike()
    {
        if (!_strikePending)
        {
            return false;
        }

        _strikePending = false;
        return true;
    }

    private void Enter(AttackCyclePhase phase)
    {
        Phase = phase;
        _phaseElapsed = 0f;
        if (phase != AttackCyclePhase.Cooldown)
        {
            _strikePending = false;
        }
    }
}
