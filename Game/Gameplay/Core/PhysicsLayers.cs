namespace FirstPersonAction.Core;

/// <summary>
/// 物理层命名约定（与 project.godot 的 layer_names/3d_physics 对齐）。
/// 场景里的 collision_layer 数值 + 代码里的掩码都必须经由这里引用，
/// 不允许再出现 0b101 / SetCollisionLayerValue(3) 这类裸位。
/// </summary>
public static class PhysicsLayers
{
    /// <summary>第 1 层：世界（墙体/地面静态体）。</summary>
    public const int World = 1;

    /// <summary>第 2 层：玩家。</summary>
    public const int Player = 2;

    /// <summary>第 3 层：敌人（死亡时由单位自身下线该层——OnDied 中 SetCollisionLayerValue）。</summary>
    public const int Enemy = 3;

    public const uint WorldBit = 1u << (World - 1);
    public const uint PlayerBit = 1u << (Player - 1);
    public const uint EnemyBit = 1u << (Enemy - 1);

    /// <summary>玩家箭矢可命中层：世界 + 敌人（不对友方生效）。</summary>
    public const uint PlayerProjectileMask = WorldBit | EnemyBit;

    /// <summary>敌方箭矢可命中层：世界 + 玩家（无友伤）。</summary>
    public const uint EnemyProjectileMask = WorldBit | PlayerBit;
}
