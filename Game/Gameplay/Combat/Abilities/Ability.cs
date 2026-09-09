using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 技能运行时基类（规格第 2 节 Ability）：管理 CD 与施放状态，
/// 子类实现阶段推进与命中。通过 IAbilityContext 与控制器交互，
/// 逻辑可脱离引擎单测。
/// </summary>
public abstract class Ability
{
    public SkillDefinition Def { get; }

    public float CooldownRemaining { get; private set; }

    public bool IsCasting { get; protected set; }

    public bool CanCast => CooldownRemaining <= 0f && !IsCasting;

    protected Ability(SkillDefinition def) => Def = def;

    /// <summary>CD 与施法统一推进；由控制器每帧对全部技能调用。</summary>
    public void Update(float dt, IAbilityContext ctx)
    {
        if (CooldownRemaining > 0f)
        {
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - dt);
        }

        if (IsCasting)
        {
            TickCast(dt, ctx);
        }
    }

    public bool TryCast(IAbilityContext ctx)
    {
        if (!CanCast)
        {
            return false;
        }

        CooldownRemaining = Def.Cooldown;
        IsCasting = true;
        OnCastStart(ctx);
        return true;
    }

    /// <summary>外部打断（受击/处决等将来的取消源）。默认回到空闲并清理位移/霸体。</summary>
    public virtual void Cancel(IAbilityContext ctx)
    {
        if (!IsCasting)
        {
            return;
        }

        IsCasting = false;
        ctx.CancelForcedMovement();
        ctx.SetSuperArmor(false);
    }

    protected abstract void OnCastStart(IAbilityContext ctx);

    protected abstract void TickCast(float dt, IAbilityContext ctx);

    protected void EndCast() => IsCasting = false;
}
