using Godot;

namespace FirstPersonAction.Characters;

/// <summary>
/// 敌人数值定义（数据驱动：调平衡 = 改 .tres，不改代码——与角色侧 CharacterDefinition 对称）。
/// 一个平面资源覆盖全部敌人原型：近战/远程/护卫字段按需取用，未用字段保持默认。
/// 场景引用 Definition 后其数值为权威——MaxPoise/Body* 等导出覆盖不再生效。
/// </summary>
[GlobalClass]
public partial class EnemyDefinition : Resource
{
    [Export]
    public string DisplayName = "敌人";

    // —— 身体/空间（EnemyAI 基类消费）——
    [Export]
    public float MaxPoise = 60f;

    [Export]
    public float BodyRadius = 0.45f;

    [Export]
    public float BodyMass = 100f;

    [Export]
    public float BodyPushResistance = 100f;

    /// <summary>移动速度（追击/回槽/走位共用，原型间差异靠 .tres 区分）。</summary>
    [Export]
    public float MoveSpeed = 3.2f;

    // —— 近战攻击（SwarmSoldier/GuardEnemy/FormationMelee 消费）——
    [Export]
    public float AttackDamage = 8f;

    [Export]
    public float AttackPoiseDamage = 10f;

    [Export]
    public float AttackKnockback = 2.5f;

    [Export]
    public float AttackRange = 1.7f;

    [Export]
    public float AttackHalfAngleDeg = 60f;

    [Export]
    public float AttackWindup = 0.5f;

    [Export]
    public float AttackCooldown = 1.2f;

    // —— 远程（RangedEnemy/FormationArcher 消费）——
    [Export]
    public float KiteNear = 7f; // < 近于此距离后退

    [Export]
    public float KiteFar = 11f; // > 远于此距离接近

    [Export]
    public float AimSeconds = 0.7f; // 瞄准预告（红线）时长

    [Export]
    public float RangedAttackCooldown = 2.4f;

    [Export]
    public float ArrowDamage = 10f;

    [Export]
    public float ArrowPoiseDamage = 12f;

    [Export]
    public float ArrowKnockback = 2f;

    [Export]
    public float ArrowSpeed = 14f;

    // —— 护卫（GuardEnemy 消费）——
    [Export]
    public float WingOffset = 1.2f; // 左右翼跟随偏移

    [Export]
    public float EngageRange = 2.6f; // 玩家近身才转入攻击

    [Export]
    public float LeashRange = 3.5f; // 玩家离开即回归跟随（滞回）
}
