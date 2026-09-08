using System;
using System.Collections.Generic;
using Godot;
using GodotGameTemplate.Core;

namespace GodotGameTemplate.Combat;

/// <summary>一次命中携带的数据。伤害/韧性/击退由攻击方定义，受击方消费。</summary>
public struct HitData
{
    public float Damage;
    public float PoiseDamage;
    public Vector3 Knockback;
    public EntityId Source;
}

/// <summary>可被攻击判定命中的目标。敌人和将来的可破坏物实现它。</summary>
public interface ICombatTarget
{
    /// <summary>判定用的身体中心点（世界坐标）。</summary>
    Vector3 Center { get; }

    bool CanBeHit { get; }

    void ApplyHit(in HitData hit);
}

/// <summary>
/// 近战锥形判定（纯逻辑，可单元测试；规格第 4 节「主动帧窗口内形状查询」的
/// v1 形态——无常驻碰撞体，确定性强、联机友好）。
/// </summary>
public static class MeleeArcQuery
{
    private const float DefaultHeightTolerance = 1.5f;

    /// <summary>返回位于 origin 前方锥形内的目标（水平面判定，忽略高度差 ≤ 容差）。</summary>
    public static List<T> FindHits<T>(
        Vector3 origin,
        Vector3 flatForward,
        float range,
        float halfAngleDeg,
        IList<T> targets,
        Func<T, Vector3> centerOf,
        float heightTolerance = DefaultHeightTolerance
    )
        where T : class
    {
        var results = new List<T>();
        Vector3 forward = new Vector3(flatForward.X, 0f, flatForward.Z).Normalized();

        foreach (T target in targets)
        {
            Vector3 center = centerOf(target);
            if (Mathf.Abs(center.Y - origin.Y) > heightTolerance)
            {
                continue;
            }

            Vector3 flat = new Vector3(center.X - origin.X, 0f, center.Z - origin.Z);
            float distance = flat.Length();
            if (distance > range || distance < 0.001f)
            {
                continue;
            }

            float angleDeg = Mathf.RadToDeg(
                Mathf.Abs(forward.SignedAngleTo(flat.Normalized(), Vector3.Up))
            );
            if (angleDeg <= halfAngleDeg)
            {
                results.Add(target);
            }
        }

        return results;
    }
}
