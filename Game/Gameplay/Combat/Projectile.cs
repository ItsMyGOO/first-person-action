using Godot;
using GodotGameTemplate.Core;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 箭矢投射物（规格第 4 节：投射物用 Area3D，近战才用窗口查询）。
/// 自建碰撞与网格，代码生成即可用。命中可命中目标 → ApplyHit；
/// 撞墙或超时消失；穿过不可命中的目标（尸体）。
/// </summary>
public partial class Projectile : Area3D
{
    private HitData _hit;
    private Vector3 _velocity;
    private float _gravity;
    private float _life = 3f;

    private Projectile() { }

    /// <summary>生成一支箭。host 通常为玩家节点（箭矢作为其子节点，随场景销毁）。</summary>
    public static Projectile Spawn(
        Node host,
        Vector3 origin,
        Vector3 direction,
        float speed,
        HitData hit,
        float gravity = 0f
    )
    {
        var projectile = new Projectile
        {
            _hit = hit,
            _velocity = direction.Normalized() * speed,
            _gravity = gravity,
        };
        host.AddChild(projectile);
        projectile.GlobalPosition = origin; // AddChild 后再设，避免局部/世界坐标混淆
        return projectile;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0b101; // 世界(第1层) + 单位(第3层)
        Monitorable = false;

        var shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.15f } };
        AddChild(shape);
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.6f) },
        };
        mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.8f, 0.65f, 0.4f),
        };
        AddChild(mesh);

        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _velocity.Y -= _gravity * dt;
        Position += _velocity * dt;

        if (_velocity.LengthSquared() > 0.01f)
        {
            Vector3 dir = _velocity.Normalized();
            Vector3 up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
            LookAt(GlobalPosition + dir, up);
        }

        _life -= dt;
        if (_life <= 0f)
        {
            QueueFree();
        }
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is ICombatTarget target)
        {
            if (!target.CanBeHit)
            {
                return; // 穿过尸体
            }

            target.ApplyHit(_hit);
        }

        QueueFree(); // 命中单位或墙体
    }
}
