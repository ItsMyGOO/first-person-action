using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 箭矢投射物（规格第 4 节：投射物用 Area3D，近战才用窗口查询）。
/// 命中可命中目标 → ApplyHit；撞墙或超时消失；穿过不可命中的目标（尸体/无敌帧）。
/// 高速防隧穿：每物理帧对本帧位移段做射线扫掠（CCD）——满蓄力 44m/s 在 60Hz
/// 每帧位移 0.73m 远超箭半径 0.15m，Area3D 重叠兜不住薄墙窄柱，射线为准、重叠为辅。
/// </summary>
public partial class Projectile : Area3D
{
    // 箭矢是最高频生成物：形状/网格/材质全部静态共享，生成路径零资源分配
    private static readonly SphereShape3D SharedShape = new() { Radius = 0.15f };
    private static readonly BoxMesh SharedMesh = new() { Size = new Vector3(0.05f, 0.05f, 0.6f) };

    private static readonly StandardMaterial3D SharedMaterial = new()
    {
        AlbedoColor = new Color(0.8f, 0.65f, 0.4f),
    };

    private HitData _hit;
    private Vector3 _velocity;
    private float _gravity;
    private float _life = 3f;
    private uint _collisionMask;
    private bool _consumed;

    // 扫掠查询参数复用（每帧只改 From/To/CollisionMask）
    private readonly PhysicsRayQueryParameters3D _rayQuery = new();

    private Projectile() { }

    /// <summary>
    /// 生成一支箭。host 提供场景树引用，箭矢挂在场景根——挂在会旋转的生成者
    /// （如玩家）下会让局部坐标积分被父级旋转带偏（M4 箭矢方向 bug）。
    /// collisionMask 决定可命中的物理层（PhysicsLayers 常量，勿传裸位掩码）。
    /// </summary>
    public static Projectile Spawn(
        Node host,
        Vector3 origin,
        Vector3 direction,
        float speed,
        HitData hit,
        float gravity = 0f,
        uint collisionMask = PhysicsLayers.PlayerProjectileMask
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
        _rayQuery.CollisionMask = _collisionMask;

        AddChild(new CollisionShape3D { Shape = SharedShape });
        var mesh = new MeshInstance3D { Mesh = SharedMesh };
        mesh.MaterialOverride = SharedMaterial;
        AddChild(mesh);

        BodyEntered += OnBodyEntered; // 低速兜底（CCD 射线是主判定路径）
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _velocity.Y -= _gravity * dt;

        Vector3 from = GlobalPosition;
        Vector3 to = from + _velocity * dt;

        if (!_consumed)
        {
            Sweep(from, to);
        }

        if (!_consumed)
        {
            GlobalPosition = to; // 世界坐标积分：不受父级旋转影响
            OrientAlongVelocity();
            _life -= dt;
            if (_life <= 0f)
            {
                QueueFree();
            }
        }
    }

    /// <summary>本帧位移段的 CCD 扫掠：先命中的先结算，不可命中目标直接穿过。</summary>
    private void Sweep(Vector3 from, Vector3 to)
    {
        _rayQuery.From = from;
        _rayQuery.To = to;
        Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(_rayQuery);
        if (result.Count == 0)
        {
            return;
        }

        if (result["collider"].As<Node>() is ICombatTarget target)
        {
            if (!target.CanBeHit)
            {
                return; // 尸体/无敌帧：穿过
            }

            target.ApplyHit(_hit);
        }

        Consume(); // 命中单位或墙体
    }

    /// <summary>Area3D 重叠兜底（与 CCD 共用一次性守卫，防双重结算）。</summary>
    private void OnBodyEntered(Node3D body)
    {
        if (_consumed)
        {
            return;
        }

        if (body is ICombatTarget { CanBeHit: true } target)
        {
            target.ApplyHit(_hit);
        }

        Consume();
    }

    private void Consume()
    {
        if (_consumed)
        {
            return;
        }

        _consumed = true;
        QueueFree();
    }

    private void OrientAlongVelocity()
    {
        if (_velocity.LengthSquared() > 0.01f)
        {
            Vector3 dir = _velocity.Normalized();
            Vector3 up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
            LookAt(GlobalPosition + dir, up);
        }
    }
}
