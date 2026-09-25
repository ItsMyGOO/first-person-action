using Godot;

namespace FirstPersonAction.Combat;

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
    public const float GamepadLookSpeedRad = 2.6f; // 右摇杆满偏转角速度（弧度/秒 ≈150°/s）

    // —— viewmodel 占位动画（动画资产到位后由 AnimationTree 接管）——
    public static readonly Vector3 ViewModelRest = new(0.28f, -0.26f, -0.55f);
    public const float ViewModelChargePullbackZ = 0.15f; // 冲锋后拉
    public const float ViewModelStartupPullbackZ = 0.12f; // 前摇后拉
    public const float ViewModelActiveThrustZ = -0.18f; // 主动段前捅
    public const float ViewModelLerpSpeed = 18f;

    // —— 闪避（M2 先落状态与数据，M3 加无敌帧交互）——
    public const float DodgeDuration = 0.40f;
    public const float DodgeSpeed = 12f;
    public const float DodgeAccel = 60f;
    public const float DodgeInvulnerableSeconds = 0.25f;

    // —— 受击 ——
    public const float StaggerSeconds = 0.28f;
    public const float DownedSeconds = 2.5f;
    public const float KnockbackDuration = 0.25f;

    // —— 玩家死亡（冻结→低垂→延迟重载；正式死亡UI/检查点在打磨期） ——
    public const float DeathReloadDelaySeconds = 1.5f;

    // —— 弓箭手瞄准 ——
    public const float BaseFov = 90f;
    public const float AimFov = 65f; // 瞄准时 FOV 收缩
    public const float AimShoulderX = 0.35f; // 肩视镜头横向偏移（米）
    public const float AimBlendSpeed = 10f; // 瞄准表现过渡速度
    public const float AimSpeedScale = 0.5f; // 瞄准时移速比例
    public const float ArrowGravity = 4f; // 未满级箭矢下坠（米/秒²）
    public const float ArrowMuzzleOffset = 0.4f; // 出膛点距镜头前移（防贴脸自撞）
    public const float ArrowPoiseDamageScale = 2f; // 箭矢韧性伤 = 伤害 × 此系数
    public const float ArrowKnockback = 1.5f; // 箭矢击退冲量（米/秒）

    // —— 敌人 AI 系统参数（M4；敌人个体数值已迁移至 Game/Config/Enemies/*.tres）——
    public const float EnemyGroupActivateRadius = 12f; // 编组聚合激活：玩家接近半径
    public const float SlotReassignInterval = 0.4f; // 环绕槽位重算间隔

    // —— 手感打磨（M4 §5，纯表现层）——
    public const float ChargeFovKick = 10f; // 冲锋 FOV 拉伸峰值
    public const float ChargeShakeAmplitude = 0.05f; // 冲锋高频小幅抖动
    public const float LeapFovDrop = 6f; // 起跳/滞空 FOV 收束
    public const float LeapKickPitchRad = 0.06f; // 起跳相机上仰小踢
    public const float LeapLandShake = 0.4f; // 落地震动爆发幅度
    public const float LeapLandShakeDecaySeconds = 0.35f; // 落地震动衰减时长
    public const float LandSinkMeters = 0.15f; // 落地相地下沉

    // —— 冲刺速度线（M5 §2，纯表现层）——
    public const float SpeedLineDensity = 48f; // 径向扇区数
    public const float SpeedLineScrollSpeed = 2.5f; // 线条流动速度
    public const float SpeedVignetteStrength = 0.35f; // 暗角强度
    public const float SpeedLinesMaxIntensity = 1.0f; // 强度上限

    // —— 阵型系统（M5；成员个体数值在各自 .tres，这里只留系统参数）——
    public const float FormationSlotSnapDist = 0.15f; // 到槽判定距离

    // —— 处决（M4 顺延项，规格 §5：Doom 式按键处决，短演出，不允许明显吸附）——
    public const float ExecutionMinRange = 0.8f; // 距离带下界（贴脸不可处决）
    public const float ExecutionMaxRange = 1.8f; // 距离带上界
    public const float ExecutionHalfAngleDeg = 45f; // 前方锥形半角
    public const float ExecutionConvergeSeconds = 0.20f; // 收敛时长（规格：150~250ms）
    public const float ExecutionStrikeSeconds = 0.18f; // 冲击+恢复时长
    public const int ExecutionHitstopMs = 150; // 冲击顿帧（处决修正的遮罩）
    public const float ExecutionHealFraction = 0.15f; // 结算回血 = 最大生命 × 此比例
    public const float ExecutionPlayerShare = 0.35f; // 收敛分摊：玩家上步 35%、敌人靠拢 65%
    public const float ExecutionShakeAmplitude = 0.5f; // 冲击震屏幅度（CameraFeel）
}
