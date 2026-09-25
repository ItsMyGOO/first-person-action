using System.Collections.Generic;
using Godot;

namespace FirstPersonAction.Spatial;

/// <summary>
/// 每物理帧收集 spatial_agents 组的快照 → 纯逻辑 SpatialResolver 解析 →
/// 把修正 MoveAndCollide 回场景。挂在关卡场景里（如 Main.tscn）。
/// 节点顺序要求：放在所有单位之后，保证修正发生在各单位本帧移动之后。
/// SpatialBody 实例按池复用（只增不减），热路径零 new。
/// </summary>
public partial class SpatialSystem : Node
{
    private readonly List<SpatialBody> _bodies = new();
    private int _activeCount;

    public override void _PhysicsProcess(double delta)
    {
        _activeCount = 0;
        foreach (Node node in GetTree().GetNodesInGroup(SpatialAgent.GroupName))
        {
            if (node is not SpatialAgent agent || agent.SpatialExempt)
            {
                continue;
            }

            if (_activeCount < _bodies.Count)
            {
                UpdateBody(_bodies[_activeCount], agent);
            }
            else
            {
                var body = new SpatialBody { Agent = agent };
                UpdateBody(body, agent);
                _bodies.Add(body);
            }

            _activeCount++;
        }

        if (_bodies.Count > _activeCount)
        {
            _bodies.RemoveRange(_activeCount, _bodies.Count - _activeCount);
        }

        SpatialResolver.Resolve(_bodies);

        for (int i = 0; i < _bodies.Count; i++)
        {
            SpatialBody body = _bodies[i];
            if (body.Correction.LengthSquared() > 0.000001f)
            {
                body.Agent!.ApplyCorrection(body.Correction);
            }
        }
    }

    private static void UpdateBody(SpatialBody body, SpatialAgent agent)
    {
        body.Agent = agent;
        body.Position = agent.ReadPosition();
        body.Radius = agent.Radius;
        body.GameplayMass = agent.GameplayMass;
        body.PushResistance = agent.PushResistance;
        body.MovementForce = agent.CurrentMovementForce;
        body.SpatialExempt = agent.SpatialExempt;
    }
}
