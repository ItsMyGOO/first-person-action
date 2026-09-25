using FirstPersonAction.Combat;
using Godot;

namespace FirstPersonAction.Characters;

public enum AttackStyle
{
    Melee = 0,
    Ranged = 1,
}

/// <summary>
/// 角色定义（数据驱动：加角色 = 加 .tres + 技能，不改控制器——规格第 2 节）。
/// 弓箭手参数直接导出在定义上；技能槽按 SkillKind 经 AbilityFactory 实例化。
/// </summary>
[GlobalClass]
public partial class CharacterDefinition : Resource
{
    [Export]
    public string DisplayName = "角色";

    [Export]
    public AttackStyle Style = AttackStyle.Melee;

    [Export]
    public float MaxHealth = 100f;

    [Export]
    public float WalkSpeed = 5.0f;

    /// <summary>技能槽：skill_1/2/3 对应下标 0/1/2（Godot 不支持导出枚举数组，以 int 存储）。</summary>
    [Export]
    public int[] SkillIds = System.Array.Empty<int>();

    public SkillKind[] Skills => System.Array.ConvertAll(SkillIds, id => (SkillKind)id);

    // —— 弓箭手参数 ——

    [Export]
    public float QuickShotDamage = 6f;

    [Export]
    public float QuickShotCooldown = 0.35f;

    [Export]
    public float ChargeFullSeconds = 1.1f;

    [Export]
    public float[] ArrowDamageLevels = { 10f, 18f, 30f };

    [Export]
    public float[] ArrowSpeedLevels = { 26f, 34f, 44f };
}
