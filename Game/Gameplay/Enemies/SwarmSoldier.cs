using System.Collections.Generic;
using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 低级兵（M4 §4）：追自己的环绕槽位（不是追玩家身体），进入攻击距离后走攻击周期；
/// 槽位由 EnemyGroup 每 0.4s 经 SurroundSlotAssigner 重算。互相挤开由逻辑空间系统自然处理。
/// </summary>
public partial class SwarmSoldier : EnemyAI
{
    private static readonly Color WindupColor = new(1f, 0.55f, 0.2f); // 前摇预告（橙）

    private AttackCycle _attack = null!; // OnEnemyReady 时按数值定义构建

    /// <summary>本兵的环绕槽位角度（EnemyGroup 分配，世界系弧度）。</summary>
    public float SlotAngle { get; set; }

    protected override void OnEnemyReady()
    {
        _attack = new AttackCycle(Def.AttackWindup, Def.AttackCooldown);
    }

    /// <summary>前摇预告色（闪红优先）。</summary>
    protected override Color DisplayColor =>
        _attack.Phase == AttackCyclePhase.Windup && !IsFlashing ? WindupColor : base.DisplayColor;

    protected override void TickActive(float dt)
    {
        Player? player = TargetPlayer;
        if (player is not ICombatTarget { CanBeHit: true })
        {
            _attack.Stop();
            DesiredHorizontal = Vector3.Zero;
            return;
        }

        Vector3 slot =
            player.GlobalPosition
            + new Vector3(Mathf.Sin(SlotAngle), 0f, Mathf.Cos(SlotAngle))
                * SurroundSlotAssigner.Radius;
        Vector3 toSlot = slot - GlobalPosition;
        toSlot.Y = 0f;
        float slotDist = toSlot.Length();

        // 冷却中玩家拉开距离 → 脱离攻击循环重新追槽位
        if (_attack.Phase == AttackCyclePhase.Cooldown && slotDist > Def.AttackRange)
        {
            _attack.Stop();
        }

        if (_attack.Phase == AttackCyclePhase.Idle)
        {
            if (slotDist > Def.AttackRange)
            {
                Vector3 dir = toSlot / Mathf.Max(slotDist, 0.0001f);
                DesiredHorizontal = dir * Def.MoveSpeed;
                FaceTowards(GlobalPosition + dir);
                return;
            }

            _attack.Start(); // 到位，进入前摇
        }

        // 攻击周期（前摇站定、变色预告；前摇内被位移不打断——v1 简化记录在案）
        DesiredHorizontal = Vector3.Zero;
        FaceTowards(player.GlobalPosition);
        _attack.Tick(dt);

        if (_attack.TryConsumeStrike())
        {
            Strike(player);
        }
    }

    /// <summary>打击帧：朝玩家的锥形一次性结算（打空则本周期无伤害）。</summary>
    private void Strike(Player player)
    {
        List<ICombatTarget> hits = MeleeArcQuery.FindHits(
            GlobalPosition,
            ForwardFlat(),
            Def.AttackRange,
            Def.AttackHalfAngleDeg,
            new List<ICombatTarget> { player },
            t => t.Center
        );

        foreach (ICombatTarget target in hits)
        {
            target.ApplyHit(
                new HitData
                {
                    Damage = Def.AttackDamage,
                    PoiseDamage = Def.AttackPoiseDamage,
                    Knockback =
                        (player.GlobalPosition - GlobalPosition).Normalized() * Def.AttackKnockback
                        + Vector3.Up * 0.5f,
                    Source = EntityId.None,
                }
            );
        }
    }
}
