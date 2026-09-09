namespace GodotGameTemplate.Combat;

/// <summary>
/// 技能工厂：SkillKind → Ability 实例 + 代码权威默认数据。
/// 加新技能 = 加 SkillKind 枚举 + 一个 Ability 子类 + 这里一行；
/// 之后在 CharacterDefinition.Skills 里引用即可（加角色不改控制器）。
/// </summary>
public static class AbilityFactory
{
    public static Ability Create(SkillKind kind) => kind switch
    {
        SkillKind.Whirlwind => new WhirlwindAbility(CreateDefinition(kind)),
        SkillKind.Charge => new ChargeAbility(CreateDefinition(kind)),
        SkillKind.LeapSlam => new LeapSlamAbility(CreateDefinition(kind)),
        SkillKind.Backstep => new BackstepAbility(CreateDefinition(kind)),
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static SkillDefinition CreateDefinition(SkillKind kind) => kind switch
    {
        SkillKind.Whirlwind => new SkillDefinition
        {
            Kind = kind,
            DisplayName = "旋风斩",
            Cooldown = 5f,
            Startup = 0.15f,
            Active = 0.20f,
            Recovery = 0.35f,
            Damage = 14f,
            PoiseDamage = 30f,
            Knockback = 3.5f,
            AttackRange = 2.6f,
            HalfAngleDeg = 180f,
        },
        SkillKind.Charge => new SkillDefinition
        {
            Kind = kind,
            DisplayName = "冲锋",
            Cooldown = 7f,
            MoveDistance = 6f,
            MoveDuration = 0.42f,
            PushForce = 500f,
            SuperArmor = true,
            ActiveMoveScale = 0f,
        },
        SkillKind.LeapSlam => new SkillDefinition
        {
            Kind = kind,
            DisplayName = "跳劈",
            Cooldown = 9f,
            MoveDistance = 5.5f,
            MoveDuration = 0.50f,
            LaunchVelocityY = 7.5f,
            Damage = 26f,
            PoiseDamage = 65f,
            Knockback = 6f,
            AttackRange = 3.2f,
            HalfAngleDeg = 180f,
            SuperArmor = true,
            ActiveMoveScale = 0f,
        },
        SkillKind.Backstep => new SkillDefinition
        {
            Kind = kind,
            DisplayName = "后跳",
            Cooldown = 6f,
            MoveDistance = 2.5f,
            MoveDuration = 0.25f,
            ActiveMoveScale = 0f,
        },
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
