using System.Collections.Generic;
using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 旋风斩：360° 范围填充技。前摇后进入主动段结算一次（复用锥形查询、半角放宽到 180°），后摇结束。
/// </summary>
public sealed class WhirlwindAbility : Ability
{
    private float _elapsed;
    private bool _applied;

    public WhirlwindAbility(SkillDefinition def)
        : base(def) { }

    protected override void OnCastStart(IAbilityContext ctx)
    {
        _elapsed = 0f;
        _applied = false;
    }

    protected override void TickCast(float dt, IAbilityContext ctx)
    {
        _elapsed += dt;

        if (!_applied && _elapsed >= Def.Startup)
        {
            _applied = true;
            ApplyWhirlwind(ctx);
        }

        if (_elapsed >= Def.Startup + Def.Active + Def.Recovery)
        {
            EndCast();
        }
    }

    private void ApplyWhirlwind(IAbilityContext ctx)
    {
        List<ICombatTarget> targets = ctx.QueryTargets();
        List<ICombatTarget> hits = MeleeArcQuery.FindHits(
            ctx.BodyPosition,
            ctx.ForwardFlat,
            Def.AttackRange,
            Def.HalfAngleDeg,
            targets,
            t => t.Center
        );

        foreach (ICombatTarget target in hits)
        {
            target.ApplyHit(
                new HitData
                {
                    Damage = Def.Damage,
                    PoiseDamage = Def.PoiseDamage,
                    Knockback = (target.Center - ctx.BodyPosition).Normalized() * Def.Knockback,
                }
            );
        }

        if (hits.Count > 0)
        {
            ctx.NotifyHitLanded(hits.Count);
        }
    }
}
