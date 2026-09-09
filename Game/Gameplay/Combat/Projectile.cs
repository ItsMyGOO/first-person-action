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
    private uint _collisionMask;

    private Projectile() { }

    /// <summary>
    /// 生成一支箭。host 提供场景树引用，箭矢挂在场景根——挂在会旋转的生成者
    /// （如玩家）下会让局部坐标积分被父级旋转带偏（M4 箭矢方向 bug）。
    /// collisionMask 决定可命中的物理层：默认世界+单位（保持既有行为）；
    /// 敌方箭传世界+玩家，排除敌方层实现无友伤。
    /// </summary>
    public static Projectile Spawn(
        Node host,
        Vector3 origin,
        Vector3 direction,
        float speed,
        HitData hit,
        float gravity = 0f,
        uint collisionMask = 0b101
    )
    {
        var projectile = new Projectile
        {
            _hit = hit,
            _velocity = direction.Normalized() * speed,
            _gravity = gravity,
            _collisionMask = collisionMask,
        };
        host.GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = origin; // AddChild 后再设，避免局部/世界坐标混淆
        return projectile;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = _collisionMask;
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
        GlobalPosition += _velocity * dt; // 世界坐标积分：不受父级旋转影响

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
