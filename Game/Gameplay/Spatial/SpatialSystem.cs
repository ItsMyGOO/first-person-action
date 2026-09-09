using System.Collections.Generic;
using Godot;

namespace GodotGameTemplate.Spatial;

/// <summary>
/// 每物理帧收集 spatial_agents 组的快照 → 纯逻辑 SpatialResolver 解析 →
/// 把修正 MoveAndCollide 回场景。挂在关卡场景里（如 Main.tscn）。
/// 节点顺序要求：放在所有单位之后，保证修正发生在各单位本帧移动之后。
/// </summary>
public partial class SpatialSystem : Node
{
    private readonly List<SpatialBody> _bodies = new();

    public override void _PhysicsProcess(double delta)
    {
        _bodies.Clear();
        foreach (Node node in GetTree().GetNodesInGroup(SpatialAgent.GroupName))
        {
            if (node is SpatialAgent agent && !agent.SpatialExempt)
            {
                _bodies.Add(
                    new SpatialBody
                    {
                        Agent = agent,
                        Position = agent.ReadPosition(),
                        Radius = agent.Radius,
                        GameplayMass = agent.GameplayMass,
                        PushResistance = agent.PushResistance,
                        MovementForce = agent.CurrentMovementForce,
                    }
                );
            }
        }

        SpatialResolver.Resolve(_bodies);

        foreach (SpatialBody body in _bodies)
        {
            if (body.Correction.LengthSquared() > 0.000001f)
            {
                body.Agent!.ApplyCorrection(body.Correction);
            }
        }
    }
}
