using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 冲锋：直线快速位移 + 霸体。期间 CurrentMovementForce 拉到 PushForce——
/// 逻辑空间系统据此把路径上的轻单位推开、被重单位挡停（阵型文档第 6 节挤开规则）。
/// 冲刺结束由控制器恢复推力与霸体（EndAbility 统一清理）。
/// </summary>
public sealed class ChargeAbility : Ability
{
    private float _elapsed;

    public ChargeAbility(SkillDefinition def)
        : base(def)
    {
    }

    protected override void OnCastStart(IAbilityContext ctx)
    {
        _elapsed = 0f;
        if (Def.SuperArmor)
        {
            ctx.SetSuperArmor(true);
        }

        ctx.RequestForcedMovement(ForcedMovement.Linear(
            ctx.ForwardFlat, Def.MoveDistance, Def.MoveDuration, ForcedMovementEase.Smooth));
    }

    protected override void TickCast(float dt, IAbilityContext ctx)
    {
        _elapsed += dt;
        ctx.CurrentMovementForce = Def.PushForce; // 持续压住推力，直到控制器收尾恢复

        if (_elapsed >= Def.MoveDuration)
        {
            EndCast();
        }
    }
}
