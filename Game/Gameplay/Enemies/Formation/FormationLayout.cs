using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 标准阵型本地槽位（M5 §1.1，文档 §23 MVP）：前排 3（盾L/骑C/盾R）+ 后排 2（弓/法）。
/// 本地系 -Z 为阵型正面（面向玩家侧）；SlotWorld 负责本地 → 世界换算。
/// </summary>
public static class FormationLayout
{
    public const int SlotCount = 5;

    public const int FrontStart = 0; // 槽位 0-2：盾L / 骑士C / 盾R
    public const int BackStart = 3; // 槽位 3-4：弓 / 法

    private static readonly Vector3[] Locals =
    {
        new(-1.4f, 0f, -1.5f), // 0 前左
        new(0f, 0f, -1.5f), // 1 前中
        new(1.4f, 0f, -1.5f), // 2 前右
        new(-0.7f, 0f, 1.2f), // 3 后左
        new(0.7f, 0f, 1.2f), // 4 后右
    };

    public static Vector3 SlotLocal(int slotIndex) => Locals[slotIndex];

    /// <summary>本地槽位 → 世界坐标（yaw 为阵型朝向弧度，与 Godot Y 轴旋转约定一致）。</summary>
    public static Vector3 SlotWorld(Vector3 center, float yawRad, int slotIndex)
    {
        Vector3 local = Locals[slotIndex];
        float cos = Mathf.Cos(yawRad);
        float sin = Mathf.Sin(yawRad);
        return center
            + new Vector3(local.X * cos + local.Z * sin, 0f, -local.X * sin + local.Z * cos);
    }
}
