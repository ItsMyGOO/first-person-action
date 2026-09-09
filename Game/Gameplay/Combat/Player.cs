using System.Collections.Generic;
using Godot;
using GodotGameTemplate.Core;
using GodotGameTemplate.Spatial;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 玩家角色控制器（第一人称，规格第 3 节）。
/// 动作层为显式状态机：None / Melee / Dodge /（Skill、Aim 在后续任务加入）。
/// 输入采用物理帧边沿轮询：按住类状态（移动/瞄准）直读，离散动作用
/// 「按下沿压入 InputCommandBuffer、条件满足才消费」的缓冲模式——
/// 对自动化模拟与联机命令化都友好（规格第 6 节）。
/// </summary>
public partial class Player : CharacterBody3D
{
    [Export]
    public float MouseSensitivity = CombatTuning.MouseSensitivity;

    private Node3D _head = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _viewArm = null!;
    private SpatialAgent _agent = null!;
    private float _yaw;
    private float _pitch;

    private readonly InputCommandBuffer _buffer = new();
    private MeleeComboTracker _combo = null!;
    private float _timeAccum;
    private long _nowMs;

    // —— 动作层状态机 ——
    private enum ActionState
    {
        None,
        Melee,
        Dodge,
    }

    private ActionState _action = ActionState.None;

    private float _dodgeElapsed;
    private Vector3 _dodgeDirection = Vector3.Forward;

    // —— 输入边沿（上一物理帧的按住状态）——
    private bool _prevAttack;
    private bool _prevJump;
    private bool _prevDodge;
    private bool _prevSkill1;
    private bool _prevSkill2;
    private bool _prevSkill3;

    public bool IsAttacking => _combo.IsActive;

    public bool IsInvulnerable =>
        _action == ActionState.Dodge && _dodgeElapsed < CombatTuning.DodgeInvulnerableSeconds;

    public override void _Ready()
    {
        _combo = new MeleeComboTracker(CombatTuning.WarriorCombo);
        _head = GetNode<Node3D>("Head");
        _camera = GetNode<Camera3D>("Head/Camera3D");
        _viewArm = GetNode<MeshInstance3D>("Head/Camera3D/ViewModelArm");
        _agent = GetNode<SpatialAgent>("SpatialAgent");
        Input.MouseMode = Input.MouseModeEnum.Captured;
        // 本地视角隐藏完整身体（联机时按归属控制，队友视角可见——规格第 3 节）
        GetNode<MeshInstance3D>("BodyVisual").Visible = false;
        AddToGroup(CombatTuning.PlayerGroup);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(
                _pitch - motion.Relative.Y * MouseSensitivity,
                -Mathf.DegToRad(CombatTuning.PitchClampDeg),
                Mathf.DegToRad(CombatTuning.PitchClampDeg));
        }
        else if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _Process(double delta)
    {
        Rotation = new Vector3(0f, _yaw, 0f);
        _head.Rotation = new Vector3(_pitch, 0f, 0f);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _timeAccum += dt;
        _nowMs = (long)(_timeAccum * 1000f);

        TickInputEdges();
        TickActions(dt);
        TickMovement(dt);
        TickViewModel(dt);
    }

    /// <summary>把按住状态的变化转换为命令边沿：按下沿压入缓冲（条件满足时被消费）。</summary>
    private void TickInputEdges()
    {
        PushOnPressEdge(ref _prevAttack, "attack", InputCommandKind.Attack);
        PushOnPressEdge(ref _prevJump, "jump", InputCommandKind.Jump);
        PushOnPressEdge(ref _prevDodge, "dodge", InputCommandKind.Dodge);
        PushOnPressEdge(ref _prevSkill1, "skill_1", InputCommandKind.Skill1);
        PushOnPressEdge(ref _prevSkill2, "skill_2", InputCommandKind.Skill2);
        PushOnPressEdge(ref _prevSkill3, "skill_3", InputCommandKind.Skill3);
    }

    private void PushOnPressEdge(ref bool prev, string action, InputCommandKind kind)
    {
        bool held = Input.IsActionPressed(action);
        if (held && !prev)
        {
            _buffer.Push(InputCommand.Action(kind), _nowMs);
        }

        prev = held;
    }

    // —— 动作层状态机 ——

    /// <summary>近战可接受（开始或连段链）：空闲，或处于连段中且可被取消。</summary>
    private bool CanAcceptMelee =>
        _action == ActionState.None || (_action == ActionState.Melee && _combo.CanStartAction);

    /// <summary>闪避可接受：空闲或连段取消窗口（后续任务扩展到瞄准状态）。</summary>
    private bool CanAcceptDodge => CanAcceptMelee;

    private void TickActions(float dt)
    {
        if (CanAcceptDodge && _buffer.TryConsume(InputCommandKind.Dodge, _nowMs, out _))
        {
            StartDodge();
        }

        if (_action == ActionState.Dodge)
        {
            _dodgeElapsed += dt;
            if (_dodgeElapsed >= CombatTuning.DodgeDuration)
            {
                _action = ActionState.None;
            }
        }

        if (CanAcceptMelee && _buffer.TryConsume(InputCommandKind.Attack, _nowMs, out _))
        {
            _combo.TryAdvance();
            _action = ActionState.Melee;
        }

        _combo.Tick(dt);
        if (_action == ActionState.Melee && !_combo.IsActive)
        {
            _action = ActionState.None;
        }

        TickHitWindow();
    }

    /// <summary>主动帧内的锥形判定（规格第 4 节）。命中即结算并触发 hit-stop。</summary>
    private void TickHitWindow()
    {
        if (!_combo.CanApplyHit)
        {
            return;
        }

        ComboStageData stage = CombatTuning.WarriorCombo[_combo.StageIndex];
        Vector3 forward = ForwardFlat();
        List<ICombatTarget> targets = FindTargets();
        List<ICombatTarget> hits = MeleeArcQuery.FindHits(
            GlobalPosition, forward, CombatTuning.AttackRange,
            CombatTuning.AttackHalfAngleDeg, targets, t => t.Center);

        if (hits.Count == 0)
        {
            return; // 窗口保持开启稍后再试；窗口自然关闭则本段无命中
        }

        foreach (ICombatTarget target in hits)
        {
            target.ApplyHit(new HitData
            {
                Damage = stage.Damage,
                PoiseDamage = stage.PoiseDamage,
                Knockback = forward * stage.Knockback + Vector3.Up * 0.5f,
                Source = EntityId.None, // 联机时填玩家 NetworkId
            });
        }

        if (_combo.ConsumeHit())
        {
            HitstopManager.Request(CombatTuning.HitstopMs);
        }
    }

    private List<ICombatTarget> FindTargets()
    {
        var result = new List<ICombatTarget>();
        foreach (Node node in GetTree().GetNodesInGroup(CombatTuning.TargetGroup))
        {
            if (node is ICombatTarget target && target.CanBeHit)
            {
                result.Add(target);
            }
        }

        return result;
    }

    private void StartDodge()
    {
        _action = ActionState.Dodge;
        _dodgeElapsed = 0f;
        Vector2 axis = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        _dodgeDirection = axis.LengthSquared() > 0.01f
            ? (GlobalTransform.Basis * new Vector3(axis.X, 0f, axis.Y)).Normalized()
            : ForwardFlat();
        _combo.Reset(); // 闪避打断连段（取消规则的第一个成员）
    }

    private void TickMovement(float dt)
    {
        Vector2 axis = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 wish = GlobalTransform.Basis * new Vector3(axis.X, 0f, axis.Y);
        if (wish.LengthSquared() > 1f)
        {
            wish = wish.Normalized();
        }

        Vector3 horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
        Vector3 target;

        if (_action == ActionState.Dodge)
        {
            target = _dodgeDirection * CombatTuning.DodgeSpeed;
            horizontal = horizontal.MoveToward(target, CombatTuning.DodgeAccel * dt);
        }
        else if (_combo.IsActive)
        {
            target = wish * CombatTuning.WalkSpeed * CombatTuning.AttackMoveScale;
            if (_combo.Phase == ComboStagePhase.Startup)
            {
                ComboStageData stage = CombatTuning.WarriorCombo[_combo.StageIndex];
                float lungeSpeed = stage.ForwardStep / Mathf.Max(0.01f, stage.Startup);
                target += ForwardFlat() * lungeSpeed * 0.6f;
            }

            horizontal = horizontal.MoveToward(target, CombatTuning.GroundAccel * dt);
        }
        else
        {
            target = wish * CombatTuning.WalkSpeed;
            float accel = wish.LengthSquared() > 0.01f ? CombatTuning.GroundAccel : CombatTuning.GroundDecel;
            horizontal = horizontal.MoveToward(target, accel * dt);
        }

        Vector3 velocity = new Vector3(horizontal.X, Velocity.Y, horizontal.Z);
        if (IsOnFloor())
        {
            velocity.Y = -1f; // 贴地
            if (_buffer.TryConsume(InputCommandKind.Jump, _nowMs, out _))
            {
                velocity.Y = CombatTuning.JumpVelocity;
            }
        }
        else
        {
            velocity.Y -= CombatTuning.Gravity * dt;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    /// <summary>viewmodel 占位动画：前摇后拉、主动段前捅、后摇回位。动画资产到位后由 AnimationTree 接管。</summary>
    private void TickViewModel(float dt)
    {
        Vector3 rest = new Vector3(0.28f, -0.26f, -0.55f);
        Vector3 target = rest;
        if (_combo.Phase == ComboStagePhase.Startup)
        {
            target = new Vector3(rest.X, rest.Y, rest.Z + 0.12f);
        }
        else if (_combo.Phase == ComboStagePhase.Active)
        {
            target = new Vector3(rest.X, rest.Y, rest.Z - 0.18f);
        }

        _viewArm.Position = _viewArm.Position.Lerp(target, 18f * dt);
    }

    private Vector3 ForwardFlat()
    {
        Vector3 forward = -GlobalTransform.Basis.Z;
        forward.Y = 0f;
        return forward.Normalized();
    }
}
