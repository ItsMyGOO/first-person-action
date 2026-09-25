using System.Collections.Generic;
using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 阵型前排近战（M5 §1.3，盾兵/骑士共用类）：驻守槽位不离槽追击（槽位即 leash）；
/// 玩家进入攻击距离 → AttackCycle 近战（前摇预告色 → 锥形结算，模板同 SwarmSoldier）。
/// 两个场景仅参数不同：EnemyShield（PushResistance 250，冲锋 500 可推）/
/// EnemyKnight（PushResistance 500，冲锋不可推——文档 §7 表值）。
/// </summary>
public partial class FormationMelee : EnemyAI, IFormationMember
{
    private static readonly Color WindupColor = new(1f, 0.55f, 0.2f); // 前摇预告（橙）

    private readonly AttackCycle _attack = new(
        CombatTuning.FormationMeleeAttackWindup,
        CombatTuning.FormationMeleeAttackCooldown
    );

    [Export]
    public int SlotIndex { get; set; }

    public bool Alive => CanBeHit;

    public Vector3 SlotPosition { get; set; }

    public float SlotFacing { get; set; }

    protected override void OnEnemyReady()
    {
        // 未被控制器接管前，槽位即出生点（T2 站桩可验）
        SlotPosition = GlobalPosition;
        SlotFacing = Rotation.Y;
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
            FaceFormationDirection();
            return;
        }

        float playerDist = FlatDistance(GlobalPosition, player.GlobalPosition);

        // 冷却中玩家拉开 → 脱离攻击循环回槽驻守
        if (
            _attack.Phase == AttackCyclePhase.Cooldown
            && playerDist > CombatTuning.FormationMeleeAttackRange
        )
        {
            _attack.Stop();
        }

        if (_attack.Phase == AttackCyclePhase.Idle)
        {
            if (playerDist <= CombatTuning.FormationMeleeAttackRange)
            {
                _attack.Start(); // 原地进入前摇（不离槽追击）
            }
            else
            {
                HoldSlotOrReturn(dt, player);
                return;
            }
        }

        // 攻击周期（前摇站定、变色预告）
        DesiredHorizontal = Vector3.Zero;
        FaceTowards(player.GlobalPosition);
        _attack.Tick(dt);

        if (_attack.TryConsumeStrike())
        {
            Strike(player);
        }
    }

    /// <summary>回槽/站定警戒：不在槽位则回槽，到点站定面向玩家。</summary>
    private void HoldSlotOrReturn(float dt, Player player)
    {
        Vector3 toSlot = SlotPosition - GlobalPosition;
        toSlot.Y = 0f;
        float slotDist = toSlot.Length();

        if (slotDist > CombatTuning.FormationSlotSnapDist)
        {
            Vector3 dir = toSlot / Mathf.Max(slotDist, 0.0001f);
            DesiredHorizontal = dir * CombatTuning.FormationMoveSpeed;
            FaceTowards(SlotPosition);
            return;
        }

        DesiredHorizontal = Vector3.Zero;
        FaceTowards(player.GlobalPosition); // 到点站定，警戒朝向玩家
    }

    private void FaceFormationDirection()
    {
        float yaw = SlotFacing;
        FaceTowards(GlobalPosition + new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw)));
    }

    /// <summary>打击帧：朝玩家的锥形一次性结算（模板同 SwarmSoldier.Strike）。</summary>
    private void Strike(Player player)
    {
        List<ICombatTarget> hits = MeleeArcQuery.FindHits(
            GlobalPosition,
            ForwardFlat(),
            CombatTuning.FormationMeleeAttackRange,
            CombatTuning.FormationMeleeAttackHalfAngleDeg,
            new List<ICombatTarget> { player },
            t => t.Center
        );

        foreach (ICombatTarget target in hits)
        {
            target.ApplyHit(
                new HitData
                {
                    Damage = CombatTuning.FormationMeleeAttackDamage,
                    PoiseDamage = CombatTuning.FormationMeleeAttackPoiseDamage,
                    Knockback =
                        (player.GlobalPosition - GlobalPosition).Normalized()
                            * CombatTuning.FormationMeleeAttackKnockback
                        + Vector3.Up * 0.5f,
                    Source = EntityId.None,
                }
            );
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        d.Y = 0f;
        return d.Length();
    }
}
