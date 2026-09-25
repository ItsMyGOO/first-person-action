using Godot;

namespace FirstPersonAction.Spatial;

/// <summary>
/// 单位的玩法空间参数（阵型文档第 5 节 SpatialState）。
/// 单位间阻挡的权威是逻辑空间系统（SpatialSystem + SpatialResolver）；
/// 物理胶囊只负责对地形。本组件把场景体接入该系统，并承载挤开参数。
/// </summary>
public partial class SpatialAgent : Node
{
    public const string GroupName = "spatial_agents";

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

    /// <summary>冲锋/技能等强制位移期间临时调高的推力；结束后应恢复为 MovementForce。</summary>
    public float CurrentMovementForce;

    /// <summary>运行时标志：穿人/处决等期间对本单位豁免空间约束。</summary>
    public bool SpatialExempt;

    /// <summary>宿主物理体（角色的父节点）。</summary>
    public PhysicsBody3D? OwnerBody { get; private set; }

    public override void _Ready()
    {
        CurrentMovementForce = MovementForce;
        OwnerBody = GetParent() as PhysicsBody3D;
        AddToGroup(GroupName);
    }

    public Vector3 ReadPosition() =>
        OwnerBody != null ? OwnerBody.GlobalPosition : ((Node3D)GetParent()).GlobalPosition;

    /// <summary>把一帧修正应用到宿主（MoveAndCollide：撞墙则截断，不会把人推进墙）。</summary>
    public void ApplyCorrection(Vector3 correction)
    {
        OwnerBody?.MoveAndCollide(correction);
    }
}
