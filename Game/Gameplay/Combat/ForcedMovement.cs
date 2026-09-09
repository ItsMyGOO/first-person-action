using System;
using Godot;

namespace GodotGameTemplate.Combat;

public enum ForcedMovementEase
{
    /// <summary>匀速：跳跃弧线等抛物类位移。</summary>
    Linear,

    /// <summary>平滑（smoothstep）：冲刺/后步等地面位移。</summary>
    Smooth,
}

/// <summary>
/// 强制位移（规格第 1 节 Movement Pipeline 的 Forced 层；阵型文档第 21 节：
/// 强制位移可暂时突破常规移动，位移完成后仍经空间解析）。
/// 纯逻辑：Tick 返回本帧水平位移增量，由控制器转成速度走 MoveAndSlide（尊重墙体）。
/// 垂直方向（跳跃起跳）由控制器单独处理。
/// </summary>
public sealed class ForcedMovement
{
    public Vector3 Direction { get; }
    public float Distance { get; }
    public float Duration { get; }
    public ForcedMovementEase Ease { get; }

    public float Elapsed { get; private set; }

    public bool IsFinished => Elapsed >= Duration;

    private ForcedMovement(Vector3 direction, float distance, float duration, ForcedMovementEase ease)
    {
        Direction = direction;
        Distance = distance;
        Duration = MathF.Max(0.01f, duration);
        Ease = ease;
    }

    public static ForcedMovement Linear(
        Vector3 direction, float distance, float duration,
        ForcedMovementEase ease = ForcedMovementEase.Smooth) =>
        new(direction, distance, duration, ease);

    /// <summary>推进并返回本帧水平位移增量；结束后返回零向量。</summary>
    public Vector3 Tick(float dt)
    {
        if (IsFinished)
        {
            return Vector3.Zero;
        }

        float t0 = Elapsed / Duration;
        Elapsed = MathF.Min(Elapsed + dt, Duration);
        float t1 = Elapsed / Duration;
        return Direction * Distance * (Curve(t1) - Curve(t0));
    }

    private float Curve(float t) =>
        Ease == ForcedMovementEase.Smooth ? t * t * (3f - 2f * t) : t;
}
