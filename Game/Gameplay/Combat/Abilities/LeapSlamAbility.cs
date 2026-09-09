using System.Collections.Generic;
using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 跳劈：向前弧线强制位移（起跳+霸体），落地瞬间 360° 范围结算 + 击退。
/// 空中不结算；若 3 秒内未落地（跳崖等）安全收尾。
/// </summary>
public sealed class LeapSlamAbility : Ability
{
    private const float MaxAirSeconds = 3f;

    private float _elapsed;
    private bool _impacted;

    public LeapSlamAbility(SkillDefinition def)
        : base(def)
    {
    }

    protected override void OnCastStart(IAbilityContext ctx)
    {
        _elapsed = 0f;
        _impacted = false;
        ctx.SetSuperArmor(true);
        ctx.LaunchUp(Def.LaunchVelocityY);
        ctx.RequestForcedMovement(ForcedMovement.Linear(
            ctx.ForwardFlat, Def.MoveDistance, Def.MoveDuration, ForcedMovementEase.Linear));
    }

    protected override void TickCast(float dt, IAbilityContext ctx)
    {
        _elapsed += dt;

        if (!_impacted && _elapsed >= Def.MoveDuration && ctx.IsOnFloor)
        {
            _impacted = true;
            Impact(ctx);
        }
        else if (_elapsed >= Def.MoveDuration + MaxAirSeconds)
        {
            // 兜底：长时间未落地（跳崖/被拉拽），安全收尾不结算
            ctx.SetSuperArmor(false);
            EndCast();
        }
    }

    private void Impact(IAbilityContext ctx)
    {
        List<ICombatTarget> targets = ctx.QueryTargets();
        List<ICombatTarget> hits = MeleeArcQuery.FindHits(
            ctx.BodyPosition, ctx.ForwardFlat, Def.AttackRange, Def.HalfAngleDeg, targets, t => t.Center);

        foreach (ICombatTarget target in hits)
        {
            target.ApplyHit(new HitData
            {
                Damage = Def.Damage,
                PoiseDamage = Def.PoiseDamage,
                Knockback = (target.Center - ctx.BodyPosition).Normalized() * Def.Knockback,
            });
        }

        if (hits.Count > 0)
        {
            ctx.NotifyHitLanded(hits.Count);
        }

        ctx.SetSuperArmor(false);
        EndCast();
    }
}
