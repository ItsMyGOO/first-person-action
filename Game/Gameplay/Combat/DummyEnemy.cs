using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 木桩敌人：验证打击感与受击状态的最小敌人（规格 M2）。
/// 受击表现全部占位：闪白 / 击退滑行 / 躺平倒地 / 下沉消失。
/// </summary>
public partial class DummyEnemy : CharacterBody3D, ICombatTarget
{
    private static readonly Color BaseColor = new(0.55f, 0.58f, 0.62f);
    private static readonly Color FlashColor = new(1f, 0.9f, 0.8f);

    [Export]
    public float MaxPoise = 60f;

    private HealthComponent _health = null!;
    private HitReactionMachine _reaction = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private Vector3 _knockback = Vector3.Zero;
    private float _flashElapsed;

    public Vector3 Center => GlobalPosition + Vector3.Up * 0.9f;

    public bool CanBeHit => !_health.IsDead;

    public bool IsDowned => _reaction.IsDowned; // M4 处决条件从此读取

    public override void _Ready()
    {
        _health = GetNode<HealthComponent>("HealthComponent");
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _reaction = new HitReactionMachine(MaxPoise, CombatTuning.StaggerSeconds, CombatTuning.DownedSeconds);
        _material = new StandardMaterial3D { AlbedoColor = BaseColor };
        _mesh.MaterialOverride = _material;
        _health.Died += OnDied;
    }

    public void ApplyHit(in HitData hit)
    {
        _health.ApplyDamage(hit.Damage);
        _reaction.ApplyHit(hit.PoiseDamage);
        _knockback += hit.Knockback;
        _flashElapsed = 0.08f;
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

        // 击退滑行衰减
        Vector3 horizontal = new Vector3(_knockback.X, 0f, _knockback.Z);
        horizontal = horizontal.MoveToward(Vector3.Zero, 14f * dt);
        _knockback = horizontal;

        Vector3 velocity = new Vector3(_knockback.X, Velocity.Y, _knockback.Z);
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
        // 占位死亡：关闭碰撞、下沉后销毁。正式版换 Ragdoll/溶解（规格第 5 节）。
        SetCollisionLayerValue(3, false);
        Tween tween = CreateTween();
        tween.TweenProperty(_mesh, "position:y", _mesh.Position.Y - 1.5f, 0.7f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
