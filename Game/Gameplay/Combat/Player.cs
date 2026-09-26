using System.Collections.Generic;
using FirstPersonAction.Characters;
using FirstPersonAction.Core;
using FirstPersonAction.Spatial;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 玩家角色控制器（第一人称，规格第 3 节）。
/// 动作层为显式状态机：None / Melee / Dodge / Skill /（Aim 在弓箭手任务加入）。
/// 输入采用物理帧边沿轮询：按住类状态（移动/瞄准）直读，离散动作用
/// 「按下沿压入 InputCommandBuffer、条件满足才消费」的缓冲模式——
/// 对自动化模拟与联机命令化都友好（规格第 6 节）。
/// 技能经 IAbilityContext 驱动（Ability 纯逻辑、可单测）；
/// 技能产生的强制位移统一进入移动管线，位移完成后仍受空间解析约束。
/// </summary>
public partial class Player : CharacterBody3D, IAbilityContext, ICombatTarget
{
    [Export]
    public float MouseSensitivity = CombatTuning.MouseSensitivity;

    /// <summary>出生前由生成方注入（选人流程）；空则回退 GameSession 默认。</summary>
    public CharacterDefinition? Definition { get; set; }

    private Node3D _head = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _viewArm = null!;
    private SpatialAgent _agent = null!;
    private HealthComponent _health = null!;
    private CharacterDefinition _def = null!;
    private float _yaw;
    private float _pitch;

    private readonly InputCommandBuffer _buffer = new();
    private readonly List<Ability> _abilities = new();

    /// <summary>技能槽只读视图（SkillBar 轮询用）：下标 0..N-1 对应 skill_1..N。</summary>
    public IReadOnlyList<Ability> Abilities => _abilities;

    private readonly List<ICombatTarget> _targetBuffer = new(); // 主动帧窗口目标复用（免每帧分配）
    private readonly List<ICombatTarget> _hitBuffer = new();
    private readonly List<EnemyAI> _executionBuffer = new(); // 处决候选复用
    private Ability? _activeAbility;
    private ForcedMovement? _forced;
    private bool _superArmor;
    private MeleeComboTracker _combo = null!;
    private long _nowMs;

    // —— 处决（规格 §5）——
    private EnemyAI? _executionTarget;
    private ExecutionConvergence? _convergence;
    private float _executionElapsed;
    private bool _executionStruck;
    private bool _executionPrompt;

    // —— 动作层状态机 ——
    private enum ActionState
    {
        None,
        Melee,
        Dodge,
        Skill,
        Aim,
        AimCharge,
        Executing,
    }

    private ActionState _action = ActionState.None;

    private float _dodgeElapsed;
    private Vector3 _dodgeDirection = Vector3.Forward;

    // —— 弓箭手 ——
    private ChargeAccumulator _charge = null!;
    private float _quickShotCooldown;
    private float _aimBlend;

    // —— 输入边沿（上一物理帧的按住状态）——
    private bool _prevAttack;
    private bool _prevJump;
    private bool _prevDodge;
    private bool _prevSkill1;
    private bool _prevSkill2;
    private bool _prevSkill3;
    private bool _prevAim;
    private bool _prevExecute;

    public bool IsAttacking => _combo.IsActive;

    public bool IsCastingSkill => _action == ActionState.Skill;

    /// <summary>冲锋冲刺进行中（表现层 CameraFeel/冒烟断言用——M4 §5）。</summary>
    public bool IsChargeDashing => _activeAbility is ChargeAbility { IsCasting: true };

    /// <summary>任意冲刺位移技进行中（冲锋/疾行）：速度线等冲刺表现的统一判据。</summary>
    public bool IsDashing =>
        _activeAbility
            is ChargeAbility { IsCasting: true }
                or DashThroughAbility { IsCasting: true };

    /// <summary>跳劈滞空中（起跳后、落地结算前）。</summary>
    public bool IsLeapAirborne => _activeAbility is LeapSlamAbility { IsCasting: true };

    /// <summary>瞄准混合比例 0~1（CameraFeel 接管 FOV/肩视后仍只读）。</summary>
    public float AimBlend01 => _aimBlend;

    // —— 表现层事件（M4 §5：CameraFeel 等订阅，不回写模拟） ——
    public event System.Action<SkillKind>? SkillStarted;
    public event System.Action<SkillKind>? SkillEnded;
    public event System.Action? LeapLanded;
    public event System.Action? ExecutionImpact; // 处决命中瞬间（震屏/特效订阅）

    /// <summary>处决进行中（规格 §5：锁定→收敛→冲击→结算）。</summary>
    public bool IsExecuting => _action == ActionState.Executing;

    /// <summary>处决按键提示（HUD 轮询）：距离带+锥形内有可处决目标。</summary>
    public bool ExecutionReady => _executionPrompt;

    public bool IsAiming => _action is ActionState.Aim or ActionState.AimCharge;

    public bool IsCharging => _charge.IsCharging;

    public float ChargeProgress01 => _charge.Progress01;

    public bool ShowCrosshair => _def.Style == AttackStyle.Ranged;

    public bool IsInvulnerable =>
        _action == ActionState.Dodge && _dodgeElapsed < CombatTuning.DodgeInvulnerableSeconds;

    /// <summary>霸体中（冲锋/跳劈/旋风斩）：受击只扣血，不打断施法与连段。</summary>
    public bool IsSuperArmor => _superArmor;

    /// <summary>已死亡（死亡状态机冻结动作与移动，延迟重载关卡）。</summary>
    public bool IsDead => _health.IsDead;

    /// <summary>血量比例（0~1），HUD 玩家血条只读。</summary>
    public float Health01 =>
        _health.MaxHealth > 0f ? _health.CurrentHealth / _health.MaxHealth : 0f;

    public override void _Ready()
    {
        _def = Definition ?? GameSession.Instance!.EnsureSelected();
        foreach (int id in _def.SkillIds)
        {
            if (System.Enum.IsDefined(typeof(SkillKind), id))
            {
                _abilities.Add(AbilityFactory.Create((SkillKind)id));
            }
            else
            {
                // .tres 里的 SkillIds 是裸 int，配错值必须在启动时报出来而不是
                // 崩在 AbilityFactory 的 ArgumentOutOfRangeException
                GD.PushError(
                    $"[Player] 角色「{_def.DisplayName}」SkillIds 含非法值 {id}，已跳过该技能槽"
                );
            }
        }

        _charge = new ChargeAccumulator(_def.ChargeFullSeconds);

        _combo = new MeleeComboTracker(CombatTuning.WarriorCombo);
        _head = GetNode<Node3D>("Head");
        _camera = GetNode<Camera3D>("Head/Camera3D");
        _viewArm = GetNode<MeshInstance3D>("Head/Camera3D/ViewModelArm");
        _agent = GetNode<SpatialAgent>("SpatialAgent");
        _health = GetNode<HealthComponent>("HealthComponent");
        _health.Init(_def.MaxHealth);
        _health.Died += OnDied;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        // 本地视角隐藏完整身体（联机时按归属控制，队友视角可见——规格第 3 节）
        GetNode<MeshInstance3D>("BodyVisual").Visible = false;
        AddToGroup(CombatTuning.PlayerGroup);
        AddToGroup(CombatTuning.TargetGroup); // 敌人 AI 与敌方投射物需要找到玩家
    }

    /// <summary>
    /// 死亡状态机：冻结动作与移动 → 相机低垂 → 延迟重载关卡。
    /// 重载前的卸载竞态（冒烟测试提前 QueueFree 关卡、切场景）由有效性守卫兜住。
    /// </summary>
    private async void OnDied()
    {
        BreakAim();
        _combo.Reset();
        if (_action == ActionState.Skill && _activeAbility != null)
        {
            EndAbility();
        }

        Velocity = Vector3.Zero;

        // processAlways=false：暂停时冻结倒计时；ignoreTimeScale=true：不被 hit-stop 时间缩放拖长
        await ToSignal(
            GetTree().CreateTimer(CombatTuning.DeathReloadDelaySeconds, false, false, true),
            SceneTreeTimer.SignalName.Timeout
        );
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return; // 关卡已被外部卸载（冒烟清理/场景切换），重载交还给流程层
        }

        GetTree().ReloadCurrentScene();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (
            @event is InputEventMouseMotion motion
            && Input.MouseMode == Input.MouseModeEnum.Captured
        )
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(
                _pitch - motion.Relative.Y * MouseSensitivity,
                -Mathf.DegToRad(CombatTuning.PitchClampDeg),
                Mathf.DegToRad(CombatTuning.PitchClampDeg)
            );
        }

        // ui_cancel（ESC）由 PauseMenu autoload 接管：暂停菜单 + 鼠标重捕获
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (IsDead)
        {
            _pitch = Mathf.MoveToward(_pitch, -0.45f, 1.2f * dt); // 死亡相机低垂
        }
        else
        {
            TickGamepadLook(dt); // 手柄右摇杆视角（鼠标走 _UnhandledInput 事件路径）
        }

        Rotation = new Vector3(0f, _yaw, 0f);
        _head.Rotation = new Vector3(_pitch, 0f, 0f);

        // 瞄准表现状态仍在此更新；FOV/肩视偏移/震动已移交 CameraFeel（M4 §5）
        float aimTarget = IsAiming ? 1f : 0f;
        _aimBlend = Mathf.MoveToward(_aimBlend, aimTarget, CombatTuning.AimBlendSpeed * dt);
    }

    /// <summary>右摇杆视角：满偏转角速度（弧度/秒）。符号约定与鼠标一致（右=右转，下=低头）。</summary>
    private void TickGamepadLook(float dt)
    {
        Vector2 stick = Input.GetVector("look_left", "look_right", "look_up", "look_down");
        if (stick == Vector2.Zero)
        {
            return;
        }

        float delta = CombatTuning.GamepadLookSpeedRad * dt;
        _yaw -= stick.X * delta;
        _pitch = Mathf.Clamp(
            _pitch - stick.Y * delta,
            -Mathf.DegToRad(CombatTuning.PitchClampDeg),
            Mathf.DegToRad(CombatTuning.PitchClampDeg)
        );
    }

    public override void _ExitTree()
    {
        // 订阅纪律：与 HealthComponent 同树销毁时本可省略，但节点一旦移出
        // 子树（跨场景 HUD 等）就会泄漏——从现在起统一退订
        if (_health != null)
        {
            _health.Died -= OnDied;
        }

        // 处决中途被卸载：解除目标的 Executed 冻结，别把敌人永远留在豁免状态
        _executionTarget?.ExitExecuted();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _nowMs = (long)Time.GetTicksMsec(); // 引擎毫秒钟：长时运行无 float 累计精度衰减

        if (IsDead)
        {
            // 死亡冻结：只剩重力与贴地滑动，动作/移动/视角模型全部停摆
            float vy = IsOnFloor() ? -1f : Velocity.Y - CombatTuning.Gravity * dt;
            Velocity = new Vector3(0f, vy, 0f);
            MoveAndSlide();
            return;
        }

        TickInputEdges();
        TickActions(dt);
        TickMovement(dt);
        TickViewModel(dt);
    }

    /// <summary>把按住状态的变化转换为命令边沿；攻击/瞄准按键按角色风格分派。</summary>
    private void TickInputEdges()
    {
        if (_action == ActionState.Executing)
        {
            return; // 处决期间压制一切输入（规格 §5 锁定）
        }

        TickAttackAndAimInput();
        PushOnPressEdge(ref _prevJump, "jump", InputCommandKind.Jump);
        PushOnPressEdge(ref _prevDodge, "dodge", InputCommandKind.Dodge);
        PushOnPressEdge(ref _prevExecute, "execute", InputCommandKind.Execute);
        PushOnPressEdge(ref _prevSkill1, "skill_1", InputCommandKind.Skill1);
        PushOnPressEdge(ref _prevSkill2, "skill_2", InputCommandKind.Skill2);
        PushOnPressEdge(ref _prevSkill3, "skill_3", InputCommandKind.Skill3);
    }

    /// <summary>攻击/瞄准输入（消逝的光芒式拉弓）：右键按住=瞄准，瞄准中左键按住=蓄力，松开=放箭。</summary>
    private void TickAttackAndAimInput()
    {
        bool attackHeld = Input.IsActionPressed("attack");
        bool attackPressed = attackHeld && !_prevAttack;
        bool attackReleased = !attackHeld && _prevAttack;
        _prevAttack = attackHeld;

        bool aimHeld = Input.IsActionPressed("aim");
        bool aimPressed = aimHeld && !_prevAim;
        bool aimReleased = !aimHeld && _prevAim;
        _prevAim = aimHeld;

        if (_def.Style == AttackStyle.Melee)
        {
            if (attackPressed)
            {
                _buffer.Push(InputCommand.Action(InputCommandKind.Attack), _nowMs);
            }

            return;
        }

        // —— 弓箭手 ——
        if (aimPressed && _action == ActionState.None)
        {
            _action = ActionState.Aim; // 按住右键：进入瞄准（肩视/移速下降）
        }

        if (aimReleased && IsAiming)
        {
            BreakAim(); // 收弓：蓄力中的话打断，不放箭
        }

        if (attackPressed && IsAiming)
        {
            _charge.Begin();
            _action = ActionState.AimCharge; // 瞄准中按住左键：拉弓蓄力
        }

        if (attackReleased && _action == ActionState.AimCharge)
        {
            FireArrow(_charge.ConsumeRelease());
            _action = ActionState.Aim; // 右键仍按住则保持瞄准，可继续蓄力
        }

        if (attackPressed && _action == ActionState.None && _quickShotCooldown <= 0f)
        {
            FireArrow(0, quickShot: true); // 非瞄准快速弱箭
            _quickShotCooldown = _def.QuickShotCooldown;
        }
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

    /// <summary>闪避可接受：空闲、连段取消窗口、瞄准/蓄力中（打断瞄准）。</summary>
    private bool CanAcceptDodge =>
        _action == ActionState.None
        || (_action == ActionState.Melee && _combo.CanStartAction)
        || IsAiming;

    /// <summary>技能可接受：空闲、连段取消窗口、瞄准/蓄力中（技能打断拉弓——规格确认项）。</summary>
    private bool CanAcceptSkill => CanAcceptDodge;

    /// <summary>处决可接受：空闲或连段取消窗口（倒地敌人就在眼前时连段让位）。</summary>
    private bool CanAcceptExecute => CanAcceptMelee;

    private void TickActions(float dt)
    {
        if (_action == ActionState.Executing)
        {
            TickExecution(dt);
            return;
        }

        UpdateExecutionPrompt();

        if (
            CanAcceptExecute
            && _executionPrompt
            && _buffer.TryConsume(InputCommandKind.Execute, _nowMs, out _)
        )
        {
            EnemyAI? target = FindExecutionTarget();
            if (target != null)
            {
                BeginExecution(target);
            }
        }

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
            Sfx.Play("swing");
        }

        // 技能：CD 中的技能不消费输入（缓冲 150ms 内自然过期）
        TryCastSkill(0, InputCommandKind.Skill1);
        TryCastSkill(1, InputCommandKind.Skill2);
        TryCastSkill(2, InputCommandKind.Skill3);

        _combo.Tick(dt);
        _charge.Tick(dt);
        if (_quickShotCooldown > 0f)
        {
            _quickShotCooldown -= dt;
        }

        if (_action == ActionState.Melee && !_combo.IsActive)
        {
            _action = ActionState.None;
        }

        // 施法推进与结束清理
        foreach (Ability ability in _abilities)
        {
            ability.Update(dt, this);
        }

        if (_action == ActionState.Skill && _activeAbility != null && !_activeAbility.IsCasting)
        {
            EndAbility();
        }

        TickHitWindow();
    }

    private void TryCastSkill(int index, InputCommandKind command)
    {
        if (index >= _abilities.Count || !_abilities[index].CanCast)
        {
            return;
        }

        if (!CanAcceptSkill || !_buffer.TryConsume(command, _nowMs, out _))
        {
            return;
        }

        BreakAim(); // 技能打断瞄准/蓄力（规格确认项：打断后不放箭）
        if (_action == ActionState.Melee)
        {
            _combo.Reset(); // 技能打断连段
        }

        // 先 TryCast 成功再提交状态——失败路径不发出没有 SkillStarted 配对的 SkillEnded
        Ability ability = _abilities[index];
        if (ability.TryCast(this))
        {
            _activeAbility = ability;
            _action = ActionState.Skill;
            SkillStarted?.Invoke(ability.Def.Kind);
            if (ability.Def.Kind is SkillKind.Charge or SkillKind.DashThrough)
            {
                Sfx.Play("dash");
            }
        }
    }

    // —— 处决（规格 §5：锁定 → 收敛 → 冲击 → 结算） ——

    /// <summary>HUD 提示刷新：空闲状态下距离带+锥形内存在可处决目标。</summary>
    private void UpdateExecutionPrompt()
    {
        _executionPrompt = _action == ActionState.None && FindExecutionTarget() != null;
    }

    private EnemyAI? FindExecutionTarget()
    {
        _executionBuffer.Clear();
        foreach (Node node in GetTree().GetNodesInGroup(CombatTuning.TargetGroup))
        {
            if (node is EnemyAI { IsExecutionReady: true } enemy)
            {
                _executionBuffer.Add(enemy);
            }
        }

        return ExecutionQuery.FindTarget(
            GlobalPosition,
            ForwardFlat(),
            _executionBuffer,
            t => t.Center,
            _ => true, // 候选已在收集时过滤（IsExecutionReady）
            CombatTuning.ExecutionMinRange,
            CombatTuning.ExecutionMaxRange,
            CombatTuning.ExecutionHalfAngleDeg
        );
    }

    /// <summary>第 1 步·锁定：玩家 → Executing，敌人 → Executed，空间豁免，清空输入缓冲。</summary>
    private void BeginExecution(EnemyAI target)
    {
        _buffer.Clear(); // 规格：锁定瞬间清空输入缓冲
        BreakAim();
        _combo.Reset();
        _action = ActionState.Executing;
        _executionTarget = target;
        _convergence = new ExecutionConvergence(
            GlobalPosition,
            target.GlobalPosition,
            CombatTuning.ExecutionPlayerShare
        );
        _executionElapsed = 0f;
        _executionStruck = false;
        Velocity = Vector3.Zero;
        target.EnterExecuted();
        _agent.SpatialExempt = true; // 空间系统对这一对豁免（规格 §5 第 1 步）
    }

    /// <summary>第 2~4 步：收敛（双向缓动无瞬移）→ 冲击（顿帧+震屏+致命一击）→ 结算（回血回空闲）。</summary>
    private void TickExecution(float dt)
    {
        _executionElapsed += dt;

        // 边界：收敛途中目标被外部击杀/移除 → 干净中止、无奖励（规格 §5）。
        // 冲击后的目标死亡是本流程的结算而非中止——只在冲击前检查。
        EnemyAI? target = _executionTarget;
        if (!_executionStruck && (target == null || !IsInstanceValid(target) || target.IsDead))
        {
            EndExecution(reward: false);
            return;
        }

        if (_executionElapsed < CombatTuning.ExecutionConvergeSeconds)
        {
            // 第 2 步·收敛：双方缓动逼近相遇点，玩家面向敌人
            float t = _executionElapsed / CombatTuning.ExecutionConvergeSeconds;
            _convergence!.Sample(t, out Vector3 playerPos, out Vector3 enemyPos);
            GlobalPosition = new Vector3(playerPos.X, GlobalPosition.Y, playerPos.Z);
            target.GlobalPosition = new Vector3(enemyPos.X, target.GlobalPosition.Y, enemyPos.Z);
            Vector3 to = target.GlobalPosition - GlobalPosition;
            to.Y = 0f;
            if (to.LengthSquared() > 1e-6f)
            {
                _yaw = Mathf.Atan2(-to.X, -to.Z);
            }

            return;
        }

        if (!_executionStruck)
        {
            // 第 3 步·冲击：hit-stop 遮罩 + 震屏 + 致命一击（占位：viewmodel 挥动 + 敌人下沉销毁）
            _executionStruck = true;
            HitstopManager.Request(CombatTuning.ExecutionHitstopMs);
            ExecutionImpact?.Invoke();
            Sfx.Play("execute");
            target.ExecuteKill();
        }

        if (
            _executionElapsed
            >= CombatTuning.ExecutionConvergeSeconds + CombatTuning.ExecutionStrikeSeconds
        )
        {
            // 第 4 步·结算：回血奖励（最大生命百分比），回空闲
            EndExecution(reward: true);
        }
    }

    private void EndExecution(bool reward)
    {
        _executionTarget?.ExitExecuted(); // ExecuteKill 已自行退出；中止路径在此恢复
        _executionTarget = null;
        _convergence = null;
        _agent.SpatialExempt = false;
        _action = ActionState.None;
        if (reward)
        {
            _health.Heal(_health.MaxHealth * CombatTuning.ExecutionHealFraction);
        }
    }

    /// <summary>施法结束的统一收尾：恢复推力、解除霸体、清位移、回空闲。</summary>
    private void EndAbility()
    {
        if (_activeAbility != null)
        {
            SkillEnded?.Invoke(_activeAbility.Def.Kind);
        }

        _activeAbility = null;
        _forced = null;
        _agent.CurrentMovementForce = _agent.MovementForce;
        _superArmor = false;
        _agent.SpatialExempt = false; // 疾行穿人豁免统一在此解除（对照霸体的清理路径）
        _action = ActionState.None;
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
            GlobalPosition,
            forward,
            CombatTuning.AttackRange,
            CombatTuning.AttackHalfAngleDeg,
            targets,
            t => t.Center,
            exclude: this, // 显式排除自己（玩家在 TargetGroup 且可命中），不依赖距离巧合
            results: _hitBuffer
        );

        if (hits.Count == 0)
        {
            return; // 窗口保持开启稍后再试；窗口自然关闭则本段无命中
        }

        foreach (ICombatTarget target in hits)
        {
            target.ApplyHit(
                new HitData
                {
                    Damage = stage.Damage,
                    PoiseDamage = stage.PoiseDamage,
                    Knockback = forward * stage.Knockback + Vector3.Up * 0.5f,
                    Source = EntityId.None, // 联机时填玩家 NetworkId
                }
            );
        }

        if (_combo.ConsumeHit())
        {
            HitstopManager.Request(CombatTuning.HitstopMs);
            Sfx.Play("hit_melee");
        }
    }

    /// <summary>收集可命中目标到复用缓冲（当帧消费，勿持有——每帧 Clear 重填）。</summary>
    private List<ICombatTarget> FindTargets()
    {
        _targetBuffer.Clear();
        foreach (Node node in GetTree().GetNodesInGroup(CombatTuning.TargetGroup))
        {
            if (node is ICombatTarget target && target.CanBeHit)
            {
                _targetBuffer.Add(target);
            }
        }

        return _targetBuffer;
    }

    private void StartDodge()
    {
        BreakAim(); // 闪避打断瞄准/蓄力
        _action = ActionState.Dodge;
        _dodgeElapsed = 0f;
        Vector2 axis = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        _dodgeDirection =
            axis.LengthSquared() > 0.01f
                ? (GlobalTransform.Basis * new Vector3(axis.X, 0f, axis.Y)).Normalized()
                : ForwardFlat();
        _combo.Reset(); // 闪避打断连段（取消规则的第一个成员）
    }

    /// <summary>收弓/被打断：蓄力清零、退出瞄准（不放箭）。</summary>
    private void BreakAim()
    {
        if (!IsAiming)
        {
            return;
        }

        _charge.Cancel();
        _action = ActionState.None;
    }

    /// <summary>放箭：蓄力等级决定伤害/箭速/下坠；快速箭为弱化直射。</summary>
    private void FireArrow(int level, bool quickShot = false)
    {
        Sfx.Play("arrow_release");
        Vector3 direction = -_camera.GlobalTransform.Basis.Z;
        Vector3 origin = _camera.GlobalPosition + direction * CombatTuning.ArrowMuzzleOffset;
        float damage = quickShot ? _def.QuickShotDamage : LevelValue(_def.ArrowDamageLevels, level);
        float speed = quickShot ? _def.QuickShotSpeed : LevelValue(_def.ArrowSpeedLevels, level);

        Projectile.Spawn(
            this,
            origin,
            direction,
            speed,
            new HitData
            {
                Damage = damage,
                PoiseDamage = damage * CombatTuning.ArrowPoiseDamageScale,
                Knockback = direction * CombatTuning.ArrowKnockback,
                Source = EntityId.None, // 联机时填玩家 NetworkId
            },
            gravity: !quickShot && level < 2 ? CombatTuning.ArrowGravity : 0f
        );
    }

    private static float LevelValue(float[] levels, int level) =>
        levels[Mathf.Clamp(level, 0, levels.Length - 1)];

    private void TickMovement(float dt)
    {
        if (_action == ActionState.Executing)
        {
            return; // 收敛由 TickExecution 直写位置，常规移动管线让位（规格 §3 行动层压制移动层）
        }

        Vector2 axis = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 wish = GlobalTransform.Basis * new Vector3(axis.X, 0f, axis.Y);
        if (wish.LengthSquared() > 1f)
        {
            wish = wish.Normalized();
        }

        Vector3 horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
        Vector3 target;

        if (_forced != null)
        {
            // 强制位移（规格第 1 节 Forced 层）：位移增量转速度，仍走 MoveAndSlide 尊重墙体；
            // 垂直方向交给重力/起跳，不做贴地
            Vector3 delta = _forced.Tick(dt);
            horizontal = delta / Mathf.Max(dt, 0.0001f);

            Vector3 v = new Vector3(
                horizontal.X,
                Velocity.Y - CombatTuning.Gravity * dt,
                horizontal.Z
            );
            Velocity = v;
            MoveAndSlide();
            return;
        }

        if (_action == ActionState.Dodge)
        {
            target = _dodgeDirection * CombatTuning.DodgeSpeed;
            horizontal = horizontal.MoveToward(target, CombatTuning.DodgeAccel * dt);
        }
        else if (_action == ActionState.Skill && _activeAbility != null)
        {
            // 施法期间的常规移动（旋风斩等无位移技能）
            target = wish * CombatTuning.WalkSpeed * _activeAbility.Def.ActiveMoveScale;
            horizontal = horizontal.MoveToward(target, CombatTuning.GroundAccel * dt);
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
            float speedScale = IsAiming ? CombatTuning.AimSpeedScale : 1f; // 瞄准时移速下降
            target = wish * CombatTuning.WalkSpeed * speedScale;
            float accel =
                wish.LengthSquared() > 0.01f ? CombatTuning.GroundAccel : CombatTuning.GroundDecel;
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

    /// <summary>viewmodel 占位动画：前摇后拉、主动段前捅、后摇回位；冲锋期间后拉姿态（M4 §5）。动画资产到位后由 AnimationTree 接管。</summary>
    private void TickViewModel(float dt)
    {
        Vector3 rest = CombatTuning.ViewModelRest;
        Vector3 target = rest;
        if (_action == ActionState.Executing)
        {
            // 处决占位挥动：收敛期后拉蓄势，冲击帧前捅重击（动画资产到位后由 AnimationTree 接管）
            target = new Vector3(
                rest.X,
                rest.Y,
                rest.Z
                    + (
                        _executionStruck
                            ? CombatTuning.ViewModelActiveThrustZ
                            : CombatTuning.ViewModelStartupPullbackZ
                    )
            );
        }
        else if (IsChargeDashing)
        {
            target = new Vector3(rest.X, rest.Y, rest.Z + CombatTuning.ViewModelChargePullbackZ);
        }
        else if (_combo.Phase == ComboStagePhase.Startup)
        {
            target = new Vector3(rest.X, rest.Y, rest.Z + CombatTuning.ViewModelStartupPullbackZ);
        }
        else if (_combo.Phase == ComboStagePhase.Active)
        {
            target = new Vector3(rest.X, rest.Y, rest.Z + CombatTuning.ViewModelActiveThrustZ);
        }

        _viewArm.Position = _viewArm.Position.Lerp(target, CombatTuning.ViewModelLerpSpeed * dt);
    }

    private Vector3 ForwardFlat()
    {
        Vector3 forward = -GlobalTransform.Basis.Z;
        forward.Y = 0f;
        return forward.Normalized();
    }

    // —— IAbilityContext（技能只经由该面与控制器交互，规格第 2 节） ——

    Vector3 IAbilityContext.BodyPosition => GlobalPosition;

    Vector3 IAbilityContext.ForwardFlat => ForwardFlat();

    bool IAbilityContext.IsOnFloor => IsOnFloor();

    float IAbilityContext.CurrentMovementForce
    {
        get => _agent.CurrentMovementForce;
        set => _agent.CurrentMovementForce = value;
    }

    void IAbilityContext.RequestForcedMovement(ForcedMovement movement) => _forced = movement;

    void IAbilityContext.CancelForcedMovement() => _forced = null;

    void IAbilityContext.LaunchUp(float velocityY) =>
        Velocity = new Vector3(Velocity.X, velocityY, Velocity.Z);

    void IAbilityContext.SetSuperArmor(bool enabled) => _superArmor = enabled;

    void IAbilityContext.SetSpatialExempt(bool enabled) => _agent.SpatialExempt = enabled;

    List<ICombatTarget> IAbilityContext.QueryTargets() => FindTargets();

    void IAbilityContext.NotifyHitLanded(int hitCount) =>
        HitstopManager.Request(_activeAbility?.Def.HitstopMs ?? CombatTuning.HitstopMs);

    void IAbilityContext.NotifyLeapLanded()
    {
        LeapLanded?.Invoke();
        HitstopManager.Request(_activeAbility?.Def.HitstopMs ?? CombatTuning.HitstopMs);
    }

    // —— ICombatTarget（敌方近战/箭矢的受击面） ——
    // 注：玩家原点在胶囊几何中心（落地后 GlobalPosition.y≈0.9），上偏 0.3m = 世界胸口高度 1.2m

    Vector3 ICombatTarget.Center => GlobalPosition + Vector3.Up * 0.3f;

    /// <summary>无敌帧期间不可命中（M3 验收项接线）：敌方近战锥形与箭矢都走此属性。</summary>
    bool ICombatTarget.CanBeHit => !_health.IsDead && !IsInvulnerable;

    void ICombatTarget.ApplyHit(in HitData hit)
    {
        if (_health.IsDead || IsInvulnerable)
        {
            return; // 闪避无敌帧内完全免伤
        }

        _health.ApplyDamage(hit.Damage);
        Sfx.Play("hurt");

        if (_health.IsDead)
        {
            return; // OnDied 已接管死亡流程
        }

        // 受击打断规则（霸体接线）：霸体中（冲锋/跳劈/旋风斩）照常行动；
        // 否则被打断连段/瞄准/施法。受击硬直与击退表现留到打磨期。
        if (IsSuperArmor)
        {
            return;
        }

        BreakAim();
        _combo.Reset();
        if (_action == ActionState.Skill && _activeAbility != null)
        {
            EndAbility();
        }
    }

    // —— 冒烟测试缝：无头模式无法注入鼠标事件，直接读写视角（Debug 命名空间使用） ——

    internal float DebugYaw
    {
        get => _yaw;
        set => _yaw = value;
    }

    internal float DebugPitch
    {
        get => _pitch;
        set => _pitch = value;
    }
}
