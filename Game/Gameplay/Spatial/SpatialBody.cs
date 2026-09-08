using Godot;

namespace GodotGameTemplate.Spatial;

/// <summary>
/// 空间解析的快照体。SpatialSystem 每帧从 SpatialAgent 构建（Agent 非空时
/// 修正会 MoveAndCollide 回场景）；纯逻辑测试可直接无 Agent 构造。
/// </summary>
public sealed class SpatialBody
{
    public SpatialAgent? Agent;
    public Vector3 Position;
    public float Radius = 0.5f;
    public float GameplayMass = 100f;
    public float PushResistance = 100f;
    public float MovementForce = 100f;
    public bool SpatialExempt;

    /// <summary>Resolve 输出：本帧应施加的水平修正向量。</summary>
    public Vector3 Correction;

    public SpatialBody()
    {
    }

    public SpatialBody(float radius, float gameplayMass, float pushResistance, float movementForce)
    {
        Radius = radius;
        GameplayMass = gameplayMass;
        PushResistance = pushResistance;
        MovementForce = movementForce;
    }
}
