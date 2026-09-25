namespace FirstPersonAction.Combat;

public enum SkillKind
{
    Whirlwind = 0, // 旋风斩：360° 填充技
    Charge = 1, // 冲锋：直线位移，按挤开规则推人/被挡停
    LeapSlam = 2, // 跳劈：弧线强制位移 + 落地 AoE 击退
    Backstep = 3, // 后跳：弓手位移技，打断瞄准/蓄力
}

/// <summary>
/// 技能数据（纯类，AbilityFactory 持有代码权威默认值；
/// 迁移为 Resource/.tres 管线时以此为蓝本）。三阶段时长与判定窗口沿用
/// 连段的「前摇/主动/后摇」语义（规格第 2 节）。
/// </summary>
public sealed class SkillDefinition
{
    public SkillKind Kind;
    public string DisplayName = "";

    public float Cooldown = 5f;

    // —— 阶段（旋风斩类按时长推进的技能用）——
    public float Startup = 0.15f;
    public float Active = 0.20f;
    public float Recovery = 0.35f;

    // —— 命中 ——
    public float Damage;
    public float PoiseDamage;
    public float Knockback;
    public float AttackRange = 2.6f;
    public float HalfAngleDeg = 180f;

    // —— 强制位移 ——
    public float MoveDistance;
    public float MoveDuration;
    public float LaunchVelocityY; // 跳劈起跳垂直速度

    // —— 挤开 / 霸体 ——
    public float PushForce = 500f; // 冲刺期间 CurrentMovementForce 的值
    public bool SuperArmor;

    // —— 其它 ——
    public bool InterruptsAim = true;
    public float ActiveMoveScale = 0.3f; // 施法期间移动输入保留比例
    public int HitstopMs = 70; // 命中/落地顿帧（跳劈覆盖为 110，M4 §5）
}
