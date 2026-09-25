using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 疾行（刺客原型，规格 §2 技能原型）：直线快速位移 + 空间豁免——
/// 冲刺期间对逻辑空间系统隐身，从人群中穿过而不推挤（对照冲锋的推开/挡停）。
/// 不改推力：穿人靠豁免而非挤压。豁免由控制器统一恢复（EndAbility）；
/// 若冲刺结束停在敌人半径内，由空间解析器温和推出（规格 §5 边界）。
/// </summary>
public sealed class DashThroughAbility : Ability
{
    private float _elapsed;

    public DashThroughAbility(SkillDefinition def)
        : base(def) { }

    protected override void OnCastStart(IAbilityContext ctx)
    {
        _elapsed = 0f;
        ctx.SetSpatialExempt(true);
        ctx.RequestForcedMovement(
            ForcedMovement.Linear(
                ctx.ForwardFlat,
                Def.MoveDistance,
                Def.MoveDuration,
                ForcedMovementEase.Smooth
            )
        );
    }

    protected override void TickCast(float dt, IAbilityContext ctx)
    {
        _elapsed += dt;
        if (_elapsed >= Def.MoveDuration)
        {
            EndCast();
        }
    }
}
