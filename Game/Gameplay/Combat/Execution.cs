using System;
using System.Collections.Generic;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>可被处决的目标（规格 §5：倒地/失衡敌人）。由 EnemyAI 实现。</summary>
public interface IExecutionTarget
{
    bool IsExecutionReady { get; }
}

/// <summary>
/// 处决触发谓词（规格 §5，纯逻辑）：玩家前方约 45° 锥形 + 0.8~1.8m 距离带内的
/// 可处决目标；多候选取最近。HUD 按键提示与处决触发共用同一判定，
/// 保证「提示出现时按下去一定能处决」。
/// </summary>
public static class ExecutionQuery
{
    /// <summary>返回距离带与锥形约束内的最近可处决目标；无候选返回 null。</summary>
    public static T? FindTarget<T>(
        Vector3 origin,
        Vector3 flatForward,
        IList<T> targets,
        Func<T, Vector3> centerOf,
        Func<T, bool> isReadyOf,
        float minRange,
        float maxRange,
        float halfAngleDeg
    )
        where T : class
    {
        Vector3 forward = new Vector3(flatForward.X, 0f, flatForward.Z).Normalized();

        T? best = null;
        float bestDistance = float.MaxValue;
        foreach (T target in targets)
        {
            if (!isReadyOf(target))
            {
                continue;
            }

            Vector3 flat = new(centerOf(target).X - origin.X, 0f, centerOf(target).Z - origin.Z);
            float distance = flat.Length();
            if (distance < minRange || distance > maxRange)
            {
                continue; // 距离带外：贴脸（<0.8）与超距（>1.8）都不可处决
            }

            float angleDeg = Mathf.RadToDeg(
                Mathf.Abs(forward.SignedAngleTo(flat.Normalized(), Vector3.Up))
            );
            if (angleDeg > halfAngleDeg)
            {
                continue;
            }

            if (distance < bestDistance)
            {
                best = target;
                bestDistance = distance;
            }
        }

        return best;
    }
}

/// <summary>
/// 处决收敛（规格 §5 第 2 步，纯逻辑）：150~250ms 双向缓动——玩家上步约 35%、
/// 敌人靠拢约 65%，双方缓动逼近相遇点。无瞬移、无可见吸附：
/// 每帧采样连续，起止位置精确闭合。
/// </summary>
public sealed class ExecutionConvergence
{
    private readonly Vector3 _playerStart;
    private readonly Vector3 _enemyStart;
    private readonly Vector3 _meeting;

    /// <summary>playerShare：相遇点在连线上的位置（0.35 = 玩家走 35%，敌人走 65%）。</summary>
    public ExecutionConvergence(Vector3 playerStart, Vector3 enemyStart, float playerShare = 0.35f)
    {
        _playerStart = Flat(playerStart);
        _enemyStart = Flat(enemyStart);
        _meeting = _playerStart + (_enemyStart - _playerStart) * playerShare;
    }

    /// <summary>相遇点（世界坐标，Y 取起点高度中较低者——收敛只做水平面）。</summary>
    public Vector3 MeetingPoint => _meeting;

    /// <summary>按进度采样双方位置。t01 会被钳制到 [0,1]，缓动为平滑（ease-in-out）。</summary>
    public void Sample(float t01, out Vector3 playerPos, out Vector3 enemyPos)
    {
        float e = SmoothStep(Mathf.Clamp(t01, 0f, 1f));
        playerPos = _playerStart + (_meeting - _playerStart) * e;
        enemyPos = _enemyStart + (_meeting - _enemyStart) * e;
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
}
