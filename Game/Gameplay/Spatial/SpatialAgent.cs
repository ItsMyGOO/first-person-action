using Godot;

namespace GodotGameTemplate.Spatial;

/// <summary>
/// 单位的玩法空间参数（阵型文档第 5 节 SpatialState 的 v1 子集）。
/// v1 中物理胶囊负责简单空间阻挡；本组件的数据供 M3 冲锋的挤开规则、
/// M4 处决距离带与将来阵型系统使用。
/// </summary>
public partial class SpatialAgent : Node
{
    [Export]
    public float Radius = 0.5f;

    [Export]
    public float GameplayMass = 100f;

    [Export]
    public float PushResistance = 100f;

    [Export]
    public float MovementForce = 100f;

    [Export]
    public int CollisionPriority;

    /// <summary>运行时标志：穿人/处决等期间对本单位豁免空间约束。</summary>
    public bool SpatialExempt;
}
