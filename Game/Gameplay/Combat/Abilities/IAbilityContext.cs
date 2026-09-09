using System.Collections.Generic;
using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// Ability 对控制器的依赖面。控制器（Player）实现它；纯逻辑测试用假实现。
/// 刻意不含任何 GodotObject 类型，保证技能逻辑可脱离引擎单测。
/// </summary>
public interface IAbilityContext
{
    /// <summary>身体位置（世界坐标，水平判定用）。</summary>
    Vector3 BodyPosition { get; }

    Vector3 ForwardFlat { get; }

    bool IsOnFloor { get; }

    /// <summary>空间系统的当前推力（冲锋期间临时调高，结束由控制器恢复）。</summary>
    float CurrentMovementForce { get; set; }

    void RequestForcedMovement(ForcedMovement movement);

    void CancelForcedMovement();

    /// <summary>起跳（跳劈）：设置垂直速度。</summary>
    void LaunchUp(float velocityY);

    void SetSuperArmor(bool enabled);

    /// <summary>收集当前可被命中的目标。</summary>
    List<ICombatTarget> QueryTargets();

    /// <summary>本次结算有命中（控制器触发 hit-stop 等反馈）。</summary>
    void NotifyHitLanded(int hitCount);
}
