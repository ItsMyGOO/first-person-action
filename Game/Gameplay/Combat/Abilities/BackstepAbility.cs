using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 后跳：向镜头反方向快速位移。无伤害，主要用于打断弓手的瞄准/蓄力
/// （打断发生在控制器接受技能施放的时刻，不由本类处理）。
/// </summary>
public sealed class BackstepAbility : Ability
{
    private float _elapsed;

    public BackstepAbility(SkillDefinition def)
        : base(def)
    {
    }

    protected override void OnCastStart(IAbilityContext ctx)
    {
        _elapsed = 0f;
        ctx.RequestForcedMovement(ForcedMovement.Linear(
            -ctx.ForwardFlat, Def.MoveDistance, Def.MoveDuration, ForcedMovementEase.Smooth));
    }

    protected override void TickCast(float dt, IAbilityContext ctx)
    {
        _elapsed += dt;
        if (_elapsed >= Def.MoveDuration + 0.05f)
        {
            EndCast();
        }
    }
}
