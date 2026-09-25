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

    // —— 敌人 AI（M4）——
    public const float EnemyGroupActivateRadius = 12f; // 编组聚合激活：玩家接近半径
    public const float SlotReassignInterval = 0.4f; // 环绕槽位重算间隔
    public const float SwarmMoveSpeed = 3.2f; // 低级兵移速
    public const float SwarmAttackRange = 1.7f; // 低级兵进入攻击周期的高速距离
    public const float SwarmAttackHalfAngleDeg = 60f; // 低级兵攻击锥形半角
    public const float SwarmAttackWindup = 0.5f; // 低级兵前摇（预告）
    public const float SwarmAttackCooldown = 1.2f; // 低级兵攻击冷却
    public const float SwarmAttackDamage = 8f;
    public const float SwarmAttackPoiseDamage = 10f;
    public const float SwarmAttackKnockback = 2.5f;

    // —— 远程敌人（M4，规格距离带）——
    public const float RangedKiteNear = 7f; // < 近于此距离后退
    public const float RangedKiteFar = 11f; // > 远于此距离接近
    public const float RangedMoveSpeed = 2.6f;
    public const float RangedAimSeconds = 0.7f; // 瞄准预告（红线）时长
    public const float RangedAttackCooldown = 2.4f;

    // —— 护卫敌人（M4）——
    public const float GuardWingOffset = 1.2f; // 左右翼跟随偏移
    public const float GuardEngageRange = 2.6f; // 玩家近身才转入攻击
    public const float GuardLeashRange = 3.5f; // 玩家离开即回归跟随（leash 滞回）
    public const float GuardMoveSpeed = 3.6f;
    public const float GuardAttackRange = 2.0f;
    public const float GuardAttackHalfAngleDeg = 60f;
    public const float GuardAttackWindup = 0.45f;
    public const float GuardAttackCooldown = 1.5f;
    public const float GuardAttackDamage = 12f;
    public const float GuardAttackPoiseDamage = 15f;
    public const float GuardAttackKnockback = 3f;

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

    // —— 阵型（M5，文档 §23 MVP）——
    public const float FormationMoveSpeed = 3.0f; // 阵型成员回槽速度
    public const float FormationSlotSnapDist = 0.15f; // 到槽判定距离
    public const float FormationMeleeAttackRange = 2.2f; // 前排近战攻击距离
    public const float FormationMeleeAttackHalfAngleDeg = 60f;
    public const float FormationMeleeAttackWindup = 0.45f;
    public const float FormationMeleeAttackCooldown = 1.6f;
    public const float FormationMeleeAttackDamage = 10f;
    public const float FormationMeleeAttackPoiseDamage = 18f;
    public const float FormationMeleeAttackKnockback = 2.5f;
}
