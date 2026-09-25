using System.Collections.Generic;
using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 护卫（M4 §4）：绑定远程单位，左右翼 1.2m 跟随；玩家进入 2.6m 才转入攻击，
/// 玩家离开 3.5m 回归跟随（leash 滞回）。
/// 推不动 = 场景实例覆盖 BodyPushResistance 9999：冲锋 500 推力 &lt; 9999，
/// 复用已验证的重木桩挡停机制（SpatialResolver 规则 2/3）。
/// </summary>
public partial class GuardEnemy : EnemyAI
{
    private static readonly Color WindupColor = new(1f, 0.55f, 0.2f); // 前摇预告（橙）

    [Export]
    public NodePath? AnchorPath; // 绑定的远程单位（同编组内）

    [Export]
    public float WingSide = 1f; // 1 = 右翼，-1 = 左翼

    private AttackCycle _attack = null!; // OnEnemyReady 时按数值定义构建

    private EnemyAI? _anchor;
    private bool _engaged;

    protected override void OnEnemyReady()
    {
        _anchor = GetNodeOrNull<EnemyAI>(AnchorPath);
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
            _engaged = false;
            DesiredHorizontal = Vector3.Zero;
            return;
        }

        float playerDist = GlobalPosition.DistanceTo(player.GlobalPosition);
        if (!_engaged && playerDist < Def.EngageRange)
        {
            _engaged = true;
        }
        else if (_engaged && playerDist > Def.LeashRange)
        {
            _engaged = false;
            _attack.Stop();
        }

        if (!_engaged)
        {
            FollowAnchor(player);
            return;
        }

        TickEngaged(dt, player);
    }

    /// <summary>跟随锚点（远程单位）的左右翼 1.2m 偏移点；无锚点时退化为直接逼近玩家。</summary>
    private void FollowAnchor(Player player)
    {
        Vector3 target;
        if (_anchor is ICombatTarget { CanBeHit: true } anchor)
        {
            Vector3 side = _anchor.GlobalTransform.Basis.X;
            side.Y = 0f;
            side = side.Normalized();
            target = _anchor.GlobalPosition + side * (Def.WingOffset * WingSide);
        }
        else
        {
            target = player.GlobalPosition;
        }

        Vector3 toTarget = target - GlobalPosition;
        toTarget.Y = 0f;
        float dist = toTarget.Length();
        if (dist > 0.25f)
        {
            Vector3 dir = toTarget / Mathf.Max(dist, 0.0001f);
            DesiredHorizontal = dir * Def.MoveSpeed;
            FaceTowards(GlobalPosition + dir);
        }
        else
        {
            DesiredHorizontal = Vector3.Zero;
            FaceTowards(player.GlobalPosition); // 到位后警戒朝向玩家
        }
    }

    private void TickEngaged(float dt, Player player)
    {
        Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
        toPlayer.Y = 0f;
        float dist = toPlayer.Length();
        FaceTowards(player.GlobalPosition);

        // 攻击中玩家拉开距离 → 重新逼近
        if (_attack.Phase == AttackCyclePhase.Cooldown && dist > Def.AttackRange)
        {
            _attack.Stop();
        }

        if (_attack.Phase == AttackCyclePhase.Idle)
        {
            if (dist > Def.AttackRange)
            {
                Vector3 dir = toPlayer / Mathf.Max(dist, 0.0001f);
                DesiredHorizontal = dir * Def.MoveSpeed;
                return;
            }

            _attack.Start();
        }

        DesiredHorizontal = Vector3.Zero;
        _attack.Tick(dt);
        if (_attack.TryConsumeStrike())
        {
            Strike(player);
        }
    }

    /// <summary>打击帧：朝玩家的锥形一次性结算。</summary>
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
