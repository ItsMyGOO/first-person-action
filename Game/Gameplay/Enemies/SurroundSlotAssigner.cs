using System.Collections.Generic;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 纯逻辑环绕槽位分配（TDD，M4 §4）：N 个低级兵按玩家周围角度均匀分配环绕槽位。
/// 以围攻者稳定键升序决定槽位次序——集合不变时槽位不变，集合变化时相对顺序稳定（不乱跳）。
/// </summary>
public static class SurroundSlotAssigner
{
    /// <summary>环绕半径（米）：低级兵站位目标 = 玩家位置 + 槽位方向 × 半径。</summary>
    public const float Radius = 1.8f;

    /// <summary>
    /// 为围攻者分配槽位角度（弧度，玩家局部坐标系，0 = 玩家正前方，逆时针递增）。
    /// attackerIds 为围攻者稳定键（如实体 InstanceId），内部升序排序保证确定性；
    /// 返回数组与输入等长、按输入顺序一一对应；空集合返回空数组。
    /// </summary>
    public static float[] Assign(IReadOnlyList<long> attackerIds)
    {
        int count = attackerIds.Count;
        if (count == 0)
        {
            return [];
        }

        var sorted = new long[count];
        for (int i = 0; i < count; i++)
        {
            sorted[i] = attackerIds[i];
        }

        System.Array.Sort(sorted);

        float[] angles = new float[count];
        for (int slot = 0; slot < count; slot++)
        {
            angles[slot] = slot * Godot.Mathf.Tau / count;
        }

        // 输入顺序 → 槽位角度：按稳定键在升序序列中的名次取角度
        var angleByKey = new Dictionary<long, float>(count);
        for (int slot = 0; slot < count; slot++)
        {
            angleByKey[sorted[slot]] = angles[slot];
        }

        for (int i = 0; i < count; i++)
        {
            angles[i] = angleByKey[attackerIds[i]];
        }

        return angles;
    }
}
