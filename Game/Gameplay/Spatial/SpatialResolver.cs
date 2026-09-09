using System.Collections.Generic;
using Godot;

namespace GodotGameTemplate.Spatial;

/// <summary>
/// 逻辑空间解析（阵型文档第 5/6/19/20 节；规格第 1 节 Movement Pipeline 的空间层）。
/// 纯逻辑：输入快照体列表，输出每体的水平修正向量。规则：
///   1. 水平重叠 → 穿透量按下方规则分摊；
///   2. 一方 MovementForce &gt; 对方 PushResistance → 对方全额让位（挤开，冲锋/技能用）；
///   3. 否则按逆质量比分摊（文档第 20 节例：质量 100 vs 300 → 75% / 25%）；
///   4. SpatialExempt 的单位跳过（处决/穿人预留）。
/// </summary>
public static class SpatialResolver
{
    public static void Resolve(List<SpatialBody> bodies)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            bodies[i].Correction = Vector3.Zero;
        }

        for (int i = 0; i < bodies.Count; i++)
        {
            for (int j = i + 1; j < bodies.Count; j++)
            {
                SpatialBody a = bodies[i];
                SpatialBody b = bodies[j];
                if (a.SpatialExempt || b.SpatialExempt)
                {
                    continue;
                }

                Vector3 flat = new Vector3(
                    a.Position.X - b.Position.X,
                    0f,
                    a.Position.Z - b.Position.Z
                );
                float minDist = a.Radius + b.Radius;
                float dist = flat.Length();
                if (dist >= minDist)
                {
                    continue;
                }

                flat = dist < 0.0001f ? new Vector3(0f, 0f, 1f) : flat / dist; // 完全重合时任取分离方向
                float penetration = minDist - dist;

                if (a.MovementForce > b.PushResistance)
                {
                    b.Correction -= flat * penetration; // a 挤开 b
                }
                else if (b.MovementForce > a.PushResistance)
                {
                    a.Correction += flat * penetration; // b 挤开 a
                }
                else
                {
                    float shareA = b.GameplayMass / (a.GameplayMass + b.GameplayMass);
                    a.Correction += flat * penetration * shareA;
                    b.Correction -= flat * penetration * (1f - shareA);
                }
            }
        }
    }
}
