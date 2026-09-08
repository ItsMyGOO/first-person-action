using Godot;
using GodotGameTemplate.Spatial;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 木桩敌人：验证打击感与受击状态的最小敌人（规格 M2）。
/// 受击表现全部占位：闪白 / 强制位移滑行 / 躺平倒地 / 下沉消失。
/// 单位间阻挡与被推挤由逻辑空间系统处理（SpatialAgent 参数经导出代理可按实例覆盖）。
/// </summary>
public partial class DummyEnemy : CharacterBody3D, ICombatTarget
{
    private static readonly Color BaseColor = new(0.55f, 0.58f, 0.62f);
    private static readonly Color FlashColor = new(1f, 0.9f, 0.8f);

    [Export]
    public float MaxPoise = 60f;

    // 空间参数代理：方便在场景实例上按实例覆盖（如重木桩），_Ready 时写入 SpatialAgent
    [Export]
    public float BodyRadius = 0.5f;

    [Export]
    public float BodyMass = 250f;

    [Export]
    public float BodyPushResistance = 250f;

    private HealthComponent _health = null!;
    private HitReactionMachine _reaction = null!;
    private SpatialAgent _agent = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;

    // 强制位移通道：击退、冲锋推挤等都走这里
    private Vector3 _forcedVelocity = Vector3.Zero;
    private float _forcedTimer;
    private float _forcedDuration = 1f;
    private float _flashElapsed;

    public Vector3 Center => GlobalPosition + Vector3.Up * 0.9f;

    public bool CanBeHit => !_health.IsDead;

    public bool IsDowned => _reaction.IsDowned; // M4 处决条件从此读取

    public override void _Ready()
    {
        _health = GetNode<HealthComponent>("HealthComponent");
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _agent = GetNode<SpatialAgent>("SpatialAgent");
        _agent.Radius = BodyRadius;
        _agent.GameplayMass = BodyMass;
        _agent.PushResistance = BodyPushResistance;
        _reaction = new HitReactionMachine(MaxPoise, CombatTuning.StaggerSeconds, CombatTuning.DownedSeconds);
        _material = new StandardMaterial3D { AlbedoColor = BaseColor };
        _mesh.MaterialOverride = _material;
        _health.Died += OnDied;
    }

    public void ApplyHit(in HitData hit)
    {
        _health.ApplyDamage(hit.Damage);
        _reaction.ApplyHit(hit.PoiseDamage);
        Vector3 knock = hit.Knockback;
        ApplyForcedDisplacement(new Vector3(knock.X, 0f, knock.Z), CombatTuning.KnockbackDuration);
        _flashElapsed = 0.08f;
    }

    /// <summary>强制位移通道（规格第 1 节 Forced Movement）：匀速位移 duration 秒，不叠加（后到覆盖）。</summary>
    public void ApplyForcedDisplacement(Vector3 horizontalVelocity, float duration)
    {
        _forcedVelocity = horizontalVelocity;
        _forcedDuration = Mathf.Max(0.01f, duration);
        _forcedTimer = _forcedDuration;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _reaction.Tick(dt);

        // 占位表现：受击闪白 + 倒地躺平/恢复立起
        _material.AlbedoColor = _flashElapsed > 0f ? FlashColor : BaseColor;
        if (_flashElapsed > 0f)
        {
            _flashElapsed -= dt;
        }

        Vector3 meshRot = _mesh.RotationDegrees;
        meshRot.X = Mathf.MoveToward(meshRot.X, _reaction.IsDowned ? -90f : 0f, 360f * dt);
        _mesh.RotationDegrees = meshRot;

        Vector3 horizontal;
        if (_forcedTimer > 0f)
        {
            _forcedTimer -= dt;
            horizontal = _forcedVelocity;
        }
        else
        {
            horizontal = Vector3.Zero;
        }

        Vector3 velocity = new Vector3(horizontal.X, Velocity.Y, horizontal.Z);
        if (IsOnFloor())
        {
            velocity.Y = -1f;
        }
        else
        {
            velocity.Y -= CombatTuning.Gravity * dt;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void OnDied()
    {
        // 占位死亡：关闭碰撞、退出空间系统、下沉后销毁。正式版换 Ragdoll/溶解（规格第 5 节）。
        SetCollisionLayerValue(3, false);
        _agent.RemoveFromGroup(SpatialAgent.GroupName);
        Tween tween = CreateTween();
        tween.TweenProperty(_mesh, "position:y", _mesh.Position.Y - 1.5f, 0.7f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
