using FirstPersonAction.Core;
using FirstPersonAction.Spatial;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 敌人基类（M4 §4，从 DummyEnemy 上移共性）：血量/受击状态机(复用 HitReactionMachine)/
/// 强制位移通道/受击闪红。子类只写行为（低级兵/护卫/远程）。
/// 未激活时不索敌不移动——由 EnemyGroup 聚合激活（ InitiallyActive=true 的编组直接激活）。
/// 单位间阻挡与被推挤由逻辑空间系统处理（SpatialAgent 参数按实例导出覆盖）。
/// </summary>
public abstract partial class EnemyAI : CharacterBody3D, ICombatTarget, IExecutionTarget
{
    protected static readonly Color FlashColor = new(1f, 0.25f, 0.2f); // 受击闪红

    [Export]
    public Color Tint = new(0.55f, 0.42f, 0.4f);

    [Export]
    public float MaxPoise = 60f;

    // 空间参数代理：_Ready 时写入 SpatialAgent，场景实例可按实例覆盖
    [Export]
    public float BodyRadius = 0.45f;

    [Export]
    public float BodyMass = 100f;

    [Export]
    public float BodyPushResistance = 100f;

    protected HealthComponent Health = null!;
    protected HitReactionMachine Reaction = null!;
    protected SpatialAgent Agent = null!;
    protected MeshInstance3D Mesh = null!;
    protected StandardMaterial3D Material = null!;

    private Vector3 _forcedVelocity = Vector3.Zero;
    private float _forcedTimer;
    private float _forcedDuration = 1f;
    private float _flashElapsed;

    /// <summary>激活后才行动（EnemyGroup 聚合激活）。</summary>
    public bool Active { get; set; }

    /// <summary>处决锁定中（规格 §5 第 1 步）：AI/受击/空间全部冻结，位置由处决方驱动。</summary>
    public bool Executed { get; private set; }

    public Vector3 Center => GlobalPosition + Vector3.Up * 0.9f;

    /// <summary>处决锁定期间免疫外部伤害（规格 §5）；致命一击走 ExecuteKill 专线。</summary>
    public bool CanBeHit => !Health.IsDead && !Executed;

    public bool IsDead => Health.IsDead;

    /// <summary>可处决（规格 §5）：倒地/失衡且未被锁定、未死亡。</summary>
    public bool IsExecutionReady => Reaction.IsDowned && !Executed && !Health.IsDead;

    /// <summary>期望的水平移动速度，由子类行为每帧写入；强制位移期间被覆盖。</summary>
    protected Vector3 DesiredHorizontal { get; set; } = Vector3.Zero;

    protected Player? TargetPlayer =>
        IsInsideTree() ? GetTree().GetFirstNodeInGroup(CombatTuning.PlayerGroup) as Player : null;

    public override void _Ready()
    {
        Health = GetNode<HealthComponent>("HealthComponent");
        Mesh = GetNode<MeshInstance3D>("Mesh");
        Agent = GetNode<SpatialAgent>("SpatialAgent");
        Agent.Radius = BodyRadius;
        Agent.GameplayMass = BodyMass;
        Agent.PushResistance = BodyPushResistance;
        Reaction = new HitReactionMachine(
            MaxPoise,
            CombatTuning.StaggerSeconds,
            CombatTuning.DownedSeconds
        );
        Material = new StandardMaterial3D { AlbedoColor = Tint };
        Mesh.MaterialOverride = Material;
        Health.Died += OnDied;
        OnEnemyReady();
    }

    public void ApplyHit(in HitData hit)
    {
        if (!CanBeHit)
        {
            return;
        }

        Health.ApplyDamage(hit.Damage);
        Reaction.ApplyHit(hit.PoiseDamage);
        Vector3 knock = hit.Knockback;
        ApplyForcedDisplacement(new Vector3(knock.X, 0f, knock.Z), CombatTuning.KnockbackDuration);
        _flashElapsed = 0.08f;
    }

    /// <summary>强制位移通道：击退/被推挤匀速滑行 duration 秒，不叠加（后到覆盖）。</summary>
    public void ApplyForcedDisplacement(Vector3 horizontalVelocity, float duration)
    {
        _forcedVelocity = horizontalVelocity;
        _forcedDuration = Mathf.Max(0.01f, duration);
        _forcedTimer = _forcedDuration;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Executed)
        {
            // 处决锁定：行为停摆，水平位置由处决方的收敛采样直写，这里只剩重力贴地
            DesiredHorizontal = Vector3.Zero;
            TickShared(dt);
            return;
        }

        if (Active)
        {
            TickActive(dt);
        }
        else
        {
            DesiredHorizontal = Vector3.Zero;
        }

        TickShared(dt);
    }

    /// <summary>进入处决锁定（规格 §5 第 1 步）：冻结行为、清残留滑行、空间豁免。</summary>
    public void EnterExecuted()
    {
        Executed = true;
        DesiredHorizontal = Vector3.Zero;
        _forcedTimer = 0f; // 清残留击退/推挤滑行，避免与收敛采样打架
        Agent.SpatialExempt = true;
    }

    /// <summary>解除处决锁定（中止路径/玩家卸载时恢复常态）。</summary>
    public void ExitExecuted()
    {
        if (!Executed)
        {
            return;
        }

        Executed = false;
        Agent.SpatialExempt = false;
    }

    /// <summary>处决致命一击（规格 §5 第 3 步）：绕过 CanBeHit 免疫——免疫挡的是外部伤害。</summary>
    public void ExecuteKill()
    {
        ExitExecuted();
        Health.ApplyDamage(float.MaxValue); // → Died → OnDied 下沉销毁
    }

    /// <summary>受击闪红进行中（子类预告表现让位于闪红）。</summary>
    protected bool IsFlashing => _flashElapsed > 0f;

    /// <summary>当前显示色：闪红 > 子类预告色 > 基础色。TickShared 统一写材质。</summary>
    protected virtual Color DisplayColor => IsFlashing ? FlashColor : Tint;

    /// <summary>共用的物理推进：受击状态、闪红、强制位移/行为速度、重力、移动。</summary>
    protected void TickShared(float dt)
    {
        Reaction.Tick(dt);
        Material.AlbedoColor = DisplayColor;
        if (_flashElapsed > 0f)
        {
            _flashElapsed -= dt;
        }

        TickPresentation(dt);

        Vector3 horizontal = _forcedTimer > 0f ? _forcedVelocity : DesiredHorizontal;
        if (_forcedTimer > 0f)
        {
            _forcedTimer -= dt;
        }

        Vector3 velocity = new(horizontal.X, Velocity.Y, horizontal.Z);
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

    /// <summary>子类表现钩子（每物理帧，材质色之后）：木桩倒地躺平等。</summary>
    protected virtual void TickPresentation(float dt) { }

    /// <summary>激活期间的行为，由子类实现（移动/攻击循环；每帧在 TickShared 之前调用）。</summary>
    protected abstract void TickActive(float dt);

    /// <summary>水平面向一点（保持直立）。</summary>
    protected void FaceTowards(Vector3 point)
    {
        Vector3 to = point - GlobalPosition;
        to.Y = 0f;
        if (to.LengthSquared() < 0.0001f)
        {
            return;
        }

        Rotation = new Vector3(0f, Mathf.Atan2(-to.X, -to.Z), 0f);
    }

    protected Vector3 ForwardFlat()
    {
        Vector3 forward = -GlobalTransform.Basis.Z;
        forward.Y = 0f;
        return forward.Normalized();
    }

    /// <summary>子类额外初始化（引用/参数）。</summary>
    protected virtual void OnEnemyReady() { }

    /// <summary>占位死亡：关闭碰撞、退出空间系统、下沉后销毁（与木桩一致）。</summary>
    protected virtual void OnDied()
    {
        SetCollisionLayerValue(PhysicsLayers.Enemy, false);
        Active = false;
        Agent.RemoveFromGroup(SpatialAgent.GroupName);
        Tween tween = CreateTween();
        tween.TweenProperty(Mesh, "position:y", Mesh.Position.Y - 1.5f, 0.7f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
