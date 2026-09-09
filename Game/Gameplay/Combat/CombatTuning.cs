namespace GodotGameTemplate.Combat;

/// <summary>
/// v1 全部战斗调参常量。集中一处便于手感调校；
/// M3 技能框架落地时逐步迁移为 Resource 数据。
/// </summary>
public static class CombatTuning
{
    // —— 普攻连段（战士 3 段）——
    public static readonly ComboStageData[] WarriorCombo =
    {
        new()
        {
            Startup = 0.10f,
            Active = 0.12f,
            Recovery = 0.30f,
            HitFrom = 0.00f,
            HitTo = 0.12f,
            Damage = 10f,
            PoiseDamage = 20f,
            ForwardStep = 0.6f,
            CancelAfter = 0.10f,
            Knockback = 2.0f,
        },
        new()
        {
            Startup = 0.10f,
            Active = 0.12f,
            Recovery = 0.32f,
            HitFrom = 0.00f,
            HitTo = 0.12f,
            Damage = 12f,
            PoiseDamage = 25f,
            ForwardStep = 0.7f,
            CancelAfter = 0.10f,
            Knockback = 2.5f,
        },
        new()
        {
            Startup = 0.14f,
            Active = 0.16f,
            Recovery = 0.55f,
            HitFrom = 0.00f,
            HitTo = 0.16f,
            Damage = 22f,
            PoiseDamage = 55f,
            ForwardStep = 0.9f,
            CancelAfter = 0.25f,
            Knockback = 5.0f,
        },
    };

    public const string TargetGroup = "combat_targets";
    public const string PlayerGroup = "player";
    public const float AttackRange = 2.2f; // 近战判定距离
    public const float AttackHalfAngleDeg = 55f; // 前方锥形半角
    public const float AttackMoveScale = 0.15f; // 攻击期间移动输入衰减
    public const int HitstopMs = 70; // 命中顿帧（毫秒）

    // —— 移动 ——
    public const float WalkSpeed = 5.0f;
    public const float GroundAccel = 40f;
    public const float GroundDecel = 50f;
    public const float Gravity = 18f;
    public const float JumpVelocity = 7.0f;
    public const float MouseSensitivity = 0.0025f;
    public const float PitchClampDeg = 85f;

    // —— 闪避（M2 先落状态与数据，M3 加无敌帧交互）——
    public const float DodgeDuration = 0.40f;
    public const float DodgeSpeed = 12f;
    public const float DodgeAccel = 60f;
    public const float DodgeInvulnerableSeconds = 0.25f;

    // —— 受击 ——
    public const float StaggerSeconds = 0.28f;
    public const float DownedSeconds = 2.5f;
    public const float KnockbackDuration = 0.25f;

    // —— 弓箭手瞄准 ——
    public const float BaseFov = 90f;
    public const float AimFov = 65f; // 瞄准时 FOV 收缩
    public const float AimShoulderX = 0.35f; // 肩视镜头横向偏移（米）
    public const float AimBlendSpeed = 10f; // 瞄准表现过渡速度
    public const float AimSpeedScale = 0.5f; // 瞄准时移速比例
    public const float ArrowGravity = 4f; // 未满级箭矢下坠（米/秒²）
}
