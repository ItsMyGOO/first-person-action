using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FirstPersonAction.Characters;
using FirstPersonAction.Combat;
using FirstPersonAction.Core;
using FirstPersonAction.UI;
using Godot;

namespace FirstPersonAction.DebugTools;

/// <summary>
/// 引擎内战斗冒烟测试（无头可跑，CI 回归）。覆盖 M3 修订版全部验收项 +
/// 商业化修复项（无敌帧/霸体打断/死亡状态机）：
/// 连段/逻辑阻挡/冲锋推挤/重单位挡停/跳劈/旋风斩/弓手快速箭与三级蓄力/打断规则。
/// 每个阶段用全新的 Main 实例（技能 CD/木桩状态互不干扰），输入注入用
/// Input.ActionPress/ActionRelease（与控制器的物理帧边沿轮询匹配）。
/// 运行：godot --headless --path . res://Game/Scenes/Debug/CombatSmokeTest.tscn
/// 退出码：0=全过，1=有失败/崩溃，2=看门狗超时。
/// </summary>
public partial class CombatSmokeTestRunner : Node
{
    /// <summary>看门狗：整体超时秒数。防阶段卡死后空转到 --quit-after 造成的假绿退出码 0。</summary>
    private const float WatchdogSeconds = 240f;

    /// <summary>
    /// 执行数守卫下限：正常跑完应远超此数。低于它说明 Check 大面积没执行
    /// （如阶段异常逃逸、断言链被跳过），即使全部「通过」也判失败。
    /// </summary>
    private const int MinExecutedChecks = 40;

    private int _failures;
    private int _executedChecks;
    private bool _finished;

    public override async void _Ready()
    {
        GetTree().CreateTimer(WatchdogSeconds, true, false, true).Timeout += () =>
        {
            if (_finished)
            {
                return;
            }

            GD.PrintErr(
                $"[SMOKE] 看门狗超时（{WatchdogSeconds:F0}s）：已执行 {_executedChecks} 项检查"
            );
            GetTree().Quit(2);
        };

        // 冒烟会写/删 user://keybinds.cfg——先备份真实玩家键位，结束后无条件还原
        byte[]? keybindBackup = BackupUserKeybinds();
        try
        {
            List<(string Name, Func<Task> Phase)> phases = BuildPhases();
            foreach ((string name, Func<Task> phase) in phases)
            {
                try
                {
                    await phase();
                }
                catch (Exception e)
                {
                    // 单阶段崩溃只作废该阶段，后续阶段继续——异常逃逸出 async void
                    // 曾会让 Quit(1) 永不执行、进程假绿退出
                    _failures++;
                    GD.PrintErr($"[SMOKE][CRASH] 阶段「{name}」异常中断（后续阶段继续）：{e}");
                }
            }
        }
        finally
        {
            RestoreUserKeybinds(keybindBackup);
        }

        _finished = true;
        if (_executedChecks < MinExecutedChecks)
        {
            _failures++;
            GD.PrintErr(
                $"[SMOKE][FAIL] 执行数守卫：仅执行 {_executedChecks} 项检查（< {MinExecutedChecks}），断言链疑似大面积未运行"
            );
        }

        GD.Print(_failures == 0 ? "[SMOKE] 全部通过" : $"[SMOKE] {_failures} 项失败");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private List<(string Name, Func<Task> Phase)> BuildPhases() =>
        new()
        {
            ("战士基础", WarriorCombatPhase),
            ("战士技能", WarriorSkillPhase),
            ("重单位挡停", HeavyBlockPhase),
            ("弓手连段", ArcherPhase),
            ("箭矢方向回归", ArrowDirectionPhase),
            ("改键", KeybindPhase),
            ("键位设置UI", KeybindUiPhase),
            ("远程敌人", RangedEnemyPhase),
            ("编组激活", EnemyGroupPhase),
            ("手感事件", FeelPhase),
            ("阵型主链路", FormationPhase),
            ("阵型朝向滞回", FormationYawPhase),
            ("阵型冲锋推挡-盾", FormationChargePushPhase),
            ("阵型冲锋推挡-骑", FormationChargeStopPhase),
            ("阵型侧翼", FormationFlankPhase),
            ("速度线", SpeedLinesPhase),
            ("闪避无敌帧", DodgeInvulnPhase),
            ("霸体打断", SuperArmorPhase),
            ("玩家死亡", PlayerDeathPhase),
            ("处决全流程", ExecutionPhase),
        };

    // —— 阶段 ——

    /// <summary>战士基础：三段连击 + 逻辑空间阻挡。</summary>
    private async Task WarriorCombatPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        DummyEnemy dummy = main.GetNode<DummyEnemy>("Dummy1");
        HealthComponent hp = main.GetNode<HealthComponent>("Dummy1/HealthComponent");

        player.GlobalPosition = dummy.GlobalPosition + new Vector3(0, 0.2f, 1.6f);
        player.Rotation = Vector3.Zero;

        float before = hp.CurrentHealth;
        PressRelease("attack");
        await Frames(40);
        Check(hp.CurrentHealth < before, $"近战第1段命中：HP {before} -> {hp.CurrentHealth}");

        PressRelease("attack");
        await Frames(20);
        PressRelease("attack");
        await Frames(50);
        Check(
            before - hp.CurrentHealth >= 30,
            $"连段累计伤害 {before - hp.CurrentHealth}（三段链成立）"
        );

        // 逻辑空间：3m 外走向木桩，停在半径和附近
        player.GlobalPosition = dummy.GlobalPosition + new Vector3(0, 0, 3f);
        player.Rotation = Vector3.Zero;
        await Frames(10);
        Input.ActionPress("move_forward");
        await Frames(90);
        Input.ActionRelease("move_forward");
        await Frames(5);
        float dist = (player.GlobalPosition - dummy.GlobalPosition).Length();
        Check(dist > 0.75f && dist < 1.4f, $"玩家被木桩挡在 {dist:F2}m（~0.95m 半径和附近）");

        // 旋风斩（skill_3）：站到两木桩中间，360° 应同时命中
        HealthComponent hp2 = main.GetNode<HealthComponent>("Dummy2/HealthComponent");
        HealthComponent hp3 = main.GetNode<HealthComponent>("Dummy3/HealthComponent");
        player.GlobalPosition = new Vector3(0, 0.2f, -4f);
        player.Rotation = Vector3.Zero;
        await Frames(10);
        float h2before = hp2.CurrentHealth;
        float h3before = hp3.CurrentHealth;
        PressRelease("skill_3");
        await Frames(60);
        Check(
            hp2.CurrentHealth < h2before && hp3.CurrentHealth < h3before,
            $"旋风斩 360° 同时命中两侧木桩（hp2 {h2before}->{hp2.CurrentHealth}, hp3 {h3before}->{hp3.CurrentHealth}）"
        );

        await EndPhase(main);
    }

    /// <summary>战士技能：冲锋推散轻木桩、跳劈AoE倒地、旋风斩360°。</summary>
    private async Task WarriorSkillPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        DummyEnemy dummy1 = main.GetNode<DummyEnemy>("Dummy1");
        DummyEnemy dummy2 = main.GetNode<DummyEnemy>("Dummy2");
        DummyEnemy dummy3 = main.GetNode<DummyEnemy>("Dummy3");
        HealthComponent hp2 = main.GetNode<HealthComponent>("Dummy2/HealthComponent");
        HealthComponent hp3 = main.GetNode<HealthComponent>("Dummy3/HealthComponent");

        // 冲锋（skill_2）：从木桩南 3m 冲锋，轻木桩应被推走
        player.GlobalPosition = dummy1.GlobalPosition + new Vector3(0, 0.2f, 3f);
        player.Rotation = Vector3.Zero;
        await Frames(10);
        Vector3 dummyBefore = dummy1.GlobalPosition;
        PressRelease("skill_2");
        await Frames(60);
        Check(
            (dummy1.GlobalPosition - dummyBefore).Length() > 0.8f,
            $"冲锋推开轻木桩：位移 {(dummy1.GlobalPosition - dummyBefore).Length():F2}m"
        );
        Check(!player.IsCastingSkill, "冲锋结束后回到空闲");

        // 跳劈（skill_1）：面向 dummy3 跃击，落地 AoE 应击倒
        Vector3 toDummy = (dummy3.GlobalPosition - player.GlobalPosition);
        Vector3 flat = new Vector3(toDummy.X, 0, toDummy.Z).Normalized();
        player.Rotation = new Vector3(0, Mathf.Atan2(-flat.X, -flat.Z), 0);
        await Frames(5);
        PressRelease("skill_1");
        await Frames(80);
        Check(dummy3.IsDowned, $"跳劈落地AoE击倒 dummy3（韧性 {dummy3.MaxPoise} < 跳劈韧性伤 65）");
        Check(hp3.CurrentHealth < 100f, $"跳劈造成伤害：dummy3 HP {hp3.CurrentHealth}");

        await EndPhase(main);
    }

    /// <summary>重单位挡停：冲锋撞上高抗性重木桩应被挡停，重木桩几乎不动。</summary>
    private async Task HeavyBlockPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        DummyEnemy heavy = main.GetNode<DummyEnemy>("HeavyDummy");

        player.GlobalPosition = heavy.GlobalPosition + new Vector3(0, 0.2f, 3f);
        player.Rotation = Vector3.Zero;
        await Frames(10);

        PressRelease("skill_2");
        await Frames(70);
        float dist = (player.GlobalPosition - heavy.GlobalPosition).Length();
        Check(dist > 0.9f, $"冲锋被重木桩挡停在 {dist:F2}m（未穿过，半径和 1.05m）");
        Check(!player.IsCastingSkill, "冲锋已结束");

        await EndPhase(main);
    }

    /// <summary>弓箭手：快速箭、瞄准+满蓄力二箭、闪避打断蓄力不放箭。</summary>
    private async Task ArcherPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Archer.tres");
        DummyEnemy dummy = main.GetNode<DummyEnemy>("Dummy1");
        HealthComponent hp = main.GetNode<HealthComponent>("Dummy1/HealthComponent");

        player.GlobalPosition = dummy.GlobalPosition + new Vector3(0, 0, 2.5f);
        player.Rotation = Vector3.Zero;
        // 低头 ~20°（相机在 2.5m 高，木桩胶囊在 0~1.8m，平射会从头顶飞过）。
        // 无头模式下鼠标事件注入不可靠，测试直接写视角（Player 的 internal 测试缝）
        player.DebugPitch = -0.35f;
        await Frames(10);

        // 快速箭（非瞄准左键）
        float before = hp.CurrentHealth;
        PressRelease("attack");
        await Frames(40);
        Check(hp.CurrentHealth == before - 6f, $"快速箭伤害 6：HP {before} -> {hp.CurrentHealth}");

        // 瞄准 + 满蓄力（≥1.1s）松开 = 2 级箭 30 伤
        Input.ActionPress("aim");
        await Frames(10);
        Check(player.IsAiming, "右键进入瞄准状态");
        Input.ActionPress("attack");
        await Frames(80); // >1.1s 满蓄力
        Check(player.IsCharging, "左键拉弓蓄力中");
        Input.ActionRelease("attack");
        await Frames(40);
        Check(hp.CurrentHealth == before - 6f - 30f, $"满蓄力2级箭伤害 30：HP {hp.CurrentHealth}");
        Input.ActionRelease("aim");
        await Frames(5);

        // 打断：瞄准蓄力中按闪避 → 蓄力清零、不放箭
        Input.ActionPress("aim");
        await Frames(10);
        Input.ActionPress("attack");
        await Frames(20); // 蓄力中（未满）
        Check(player.IsCharging, "再次拉弓蓄力中");
        Input.ActionPress("dodge");
        await Frames(10);
        Input.ActionRelease("attack");
        Input.ActionRelease("dodge");
        Input.ActionRelease("aim");
        await Frames(40);
        Check(hp.CurrentHealth == before - 36f, $"打断后不放箭：HP 仍为 {hp.CurrentHealth}");
        Check(!player.IsCharging, "蓄力已被打断清零");

        await EndPhase(main);
    }

    /// <summary>箭矢方向回归（M4 §1）：yaw 转 180° 后射快速箭，箭必须沿镜头方向命中身后木桩。
    /// 旧实现把箭挂在玩家下做局部坐标积分，转身后箭会飞向旋转后的错误方向。</summary>
    private async Task ArrowDirectionPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Archer.tres");
        DummyEnemy dummy = main.GetNode<DummyEnemy>("Dummy1");
        HealthComponent hp = main.GetNode<HealthComponent>("Dummy1/HealthComponent");

        // 玩家站到木桩北侧（-Z），yaw 转 180° 后镜头朝 +Z，木桩位于镜头正前方
        player.GlobalPosition = dummy.GlobalPosition + new Vector3(0, 0.2f, -2.5f);
        player.DebugYaw = Mathf.Pi;
        player.DebugPitch = -0.35f;
        await Frames(10); // 等 _Process 把测试缝设置的偏航写入变换

        float before = hp.CurrentHealth;
        PressRelease("attack"); // 快速箭
        await Frames(40);
        Check(
            hp.CurrentHealth == before - 6f,
            $"yaw 180° 后快速箭命中身后木桩：HP {before} -> {hp.CurrentHealth}"
        );

        await EndPhase(main);
    }

    /// <summary>改键底层（M4 §3）：运行时 Rebind 后，新物理键沿可施放技能并持久化到
    /// user://keybinds.cfg。必须注入真实 InputEventKey——Input.ActionPress 走动作层，
    /// 绕过 InputMap，验证不了改键。</summary>
    private async Task KeybindPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        var keybinds = Core.KeybindManager.Instance!;
        keybinds.ResetToDefaults(); // 从确定的默认状态开始

        keybinds.Rebind("skill_1", Key.F);
        keybinds.Save();
        Check(
            FileAccess.FileExists("user://keybinds.cfg"),
            "Rebind+Save 后 user://keybinds.cfg 已持久化"
        );

        Input.ActionRelease("skill_1");
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F, Pressed = true });
        await Frames(5);
        Check(player.IsCastingSkill, "重绑后按新键 F 释放 skill_1（跳劈）");
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F, Pressed = false });
        Input.ActionRelease("skill_1");
        await Frames(90); // 等跳劈完整收尾
        keybinds.ResetToDefaults(); // 还原默认键位并清除持久化，不影响后续运行
        Check(
            !FileAccess.FileExists("user://keybinds.cfg"),
            "ResetToDefaults 后 keybinds.cfg 已删除"
        );

        await EndPhase(main);
    }

    /// <summary>键位设置 UI（M6 收尾）：面板打开 → 重绑捕获 → 持久化 → 恢复默认。
    /// 直调 PauseMenu 的公开缝（autoload 常驻），键事件走真实 Input.ParseInputEvent。</summary>
    private async Task KeybindUiPhase()
    {
        var keybinds = Core.KeybindManager.Instance!;
        keybinds.ResetToDefaults(); // 从确定的默认状态开始
        var pauseMenu = GetNode<UI.PauseMenu>("/root/PauseMenu");

        pauseMenu.OpenKeybindSettings();
        await Frames(2);
        Check(pauseMenu.IsKeybindVisible, "键位设置面板打开");
        Check(keybinds.CurrentKey("dodge") == Key.Shift, "默认键位显示（dodge = Shift）");

        pauseMenu.BeginRebind("dodge"); // 等同点击行按钮
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.G, Pressed = true });
        await Frames(2);
        Check(keybinds.CurrentKey("dodge") == Key.G, "UI 重绑捕获生效（dodge → G）");
        Check(FileAccess.FileExists("user://keybinds.cfg"), "重绑经面板 Save 持久化");

        pauseMenu.ResetKeybinds();
        await Frames(2);
        Check(keybinds.CurrentKey("dodge") == Key.Shift, "恢复默认（dodge 回 Shift，持久化清除）");
        Check(!FileAccess.FileExists("user://keybinds.cfg"), "恢复默认后 keybinds.cfg 已删除");

        pauseMenu.CloseKeybindSettings();
        await Frames(1);
        Check(!pauseMenu.IsKeybindVisible, "面板关闭");
    }

    /// <summary>远程敌人（M4）：9m 距离带内驻停瞄准（0.7s 红线预告）→ 射箭，
    /// 掩码为世界+玩家层（无友伤），玩家应掉血 10。</summary>
    private async Task RangedEnemyPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        var ranged = ResourceLoader
            .Load<PackedScene>("res://Game/Scenes/RangedEnemy.tscn")
            .Instantiate<RangedEnemy>();
        ranged.Position = new Vector3(0, 0.2f, 1f); // 与玩家出生点(0,0.2,10)相距 9m → 驻留带内
        ranged.Active = true;
        main.AddChild(ranged);
        await Frames(10);

        HealthComponent php = main.GetNode<HealthComponent>("Player/HealthComponent");
        float before = php.CurrentHealth;

        // 瞄准 0.7s(~42帧) + 箭飞行 9m/14ms⁻¹(~39帧)，留余量；冷却 2.4s 保证只中一箭
        await Frames(140);
        Check(
            php.CurrentHealth == before - 10f,
            $"远程敌箭命中玩家掉血 10：HP {before} -> {php.CurrentHealth}"
        );
        Check(player.Health01 == php.CurrentHealth / php.MaxHealth, "HUD 血条比例与实际血量一致");

        ranged.QueueFree();
        await EndPhase(main);
    }

    /// <summary>编组（M4 §4）：默认未激活不干扰木桩区；玩家进入半径聚合激活；
    /// 低级兵逼近环绕、远程被贴近后撤、护卫被冲锋推不动且挡停玩家。</summary>
    private async Task EnemyGroupPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        EnemyGroup group = main.GetNode<EnemyGroup>("EnemyGroup");
        SwarmSoldier swarm1 = group.GetNode<SwarmSoldier>("Swarm1");
        RangedEnemy ranged = group.GetNode<RangedEnemy>("Ranged1");
        GuardEnemy guard2 = group.GetNode<GuardEnemy>("Guard2");

        Check(!swarm1.Active, "编组初始未激活（不干扰木桩区冒烟）");
        Vector3 swarmStart = swarm1.GlobalPosition;

        // 玩家逼近东侧编组（M5 迁至 (13,0,-2)；距 8.5m < 12m 激活半径）→ 聚合激活
        player.GlobalPosition = new Vector3(5, 0.9f, -5);
        await Frames(10);
        Check(swarm1.Active && guard2.Active, "玩家进入激活半径后编组聚合激活");

        // 贴近远程兵（<7m 带内）→ 后撤拉开（趁人群未散开先测，避免阻挡干扰）
        Vector3 rangedBefore = ranged.GlobalPosition;
        player.GlobalPosition = ranged.GlobalPosition + new Vector3(0, 0.9f, 5f);
        await Frames(60);
        Check(
            (ranged.GlobalPosition - rangedBefore).Length() > 0.8f,
            $"远程兵被贴近后撤 {(ranged.GlobalPosition - rangedBefore).Length():F2}m"
        );

        // 低级兵向环绕槽位移动
        await Frames(60);
        Check(
            (swarm1.GlobalPosition - swarmStart).Length() > 1f,
            $"低级兵激活后向槽位移动 {(swarm1.GlobalPosition - swarmStart).Length():F2}m"
        );

        // 护卫推不动：冲锋命中右翼护卫，冲锋窗口内护卫位移有限、玩家被挡停
        player.GlobalPosition = guard2.GlobalPosition + new Vector3(0, 0.2f, 3f);
        player.Rotation = Vector3.Zero;
        await Frames(10);
        Vector3 guardBefore = guard2.GlobalPosition;
        PressRelease("skill_2");
        await Frames(30); // 冲锋时长 0.42s ≈ 25 帧
        float guardMoved = (guard2.GlobalPosition - guardBefore).Length();
        await Frames(40);
        Check(
            guardMoved < 1.5f,
            $"护卫冲锋窗口内位移 {guardMoved:F2}m < 1.5m（推不动；对照轻木桩被推 ~4m）"
        );
        float stopDist = (player.GlobalPosition - guard2.GlobalPosition).Length();
        Check(stopDist > 0.8f && stopDist < 1.4f, $"玩家冲锋被护卫挡停在 {stopDist:F2}m");
        Check(!player.IsCastingSkill, "冲锋已结束");

        await EndPhase(main);
    }

    /// <summary>手感打磨冒烟（M4 §5，轻量断事件与节点）：冲锋 IsChargeDashing 真→假；
    /// 跳劈落地触发 LeapLanded、ShockwaveRing 出现且 0.6s 内自毁。</summary>
    private async Task FeelPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");

        // 冲锋：状态标志真→假
        PressRelease("skill_2");
        await Frames(5);
        Check(player.IsChargeDashing, "冲锋期间 IsChargeDashing 为真");
        await Frames(40);
        Check(!player.IsChargeDashing, "冲锋结束后 IsChargeDashing 转假");
        await Frames(30); // 等 CD 不影响后续（skill_1 CD 9s 独立）

        // 跳劈：LeapLanded 事件 + ShockwaveRing 生成与自毁
        bool leapLanded = false;
        player.LeapLanded += () => leapLanded = true;
        int ringsBefore = CountShockwaves();
        PressRelease("skill_1");
        // 等落地事件（上限 120 帧）再计数，避免固定等待与环 0.4s 生命重叠的边沿时序
        for (int i = 0; i < 120 && !leapLanded; i++)
        {
            await Frames(1);
        }

        Check(leapLanded, "跳劈落地触发 LeapLanded");
        await Frames(5);
        int ringsAfter = CountShockwaves();
        Check(ringsAfter > ringsBefore, $"落地生成 ShockwaveRing（{ringsAfter - ringsBefore} 个）");
        await Frames(50); // 0.4s 生命 + 余量
        Check(CountShockwaves() == ringsBefore, "ShockwaveRing 0.6s 内自毁");

        await EndPhase(main);
    }

    /// <summary>速度线（M5 T6）：冲锋期间 shader 强度 > 0.5；结束后 0.5s 内回落 < 0.05。</summary>
    private async Task SpeedLinesPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        SpeedLines speedLines = main.GetNode<SpeedLines>("Hud/SpeedLines");

        Check(speedLines.CurrentIntensity < 0.05f, "空闲时速度线强度 ≈ 0");
        PressRelease("skill_2");
        await Frames(8); // 混合度快进 14/s，冲锋中即达峰
        Check(
            speedLines.CurrentIntensity > 0.5f,
            $"冲锋期间速度线强度 {speedLines.CurrentIntensity:F2} > 0.5"
        );
        await Frames(65); // 冲锋 0.42s 结束 + 慢出 3.5/s（0.29s 归零）+ 0.5s 限额余量
        Check(
            speedLines.CurrentIntensity < 0.05f,
            $"结束后 0.5s 强度回落 {speedLines.CurrentIntensity:F2} < 0.05"
        );

        await EndPhase(main);
    }

    /// <summary>无敌帧（商业修复 M3 遗留断线）：闪避前 0.25s 内直接受击免伤；
    /// 闪避结束后恢复可命中。直接调 ApplyHit 避免敌箭时序抖动。</summary>
    private async Task DodgeInvulnPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        HealthComponent php = main.GetNode<HealthComponent>("Player/HealthComponent");
        ICombatTarget target = player;

        float before = php.CurrentHealth;
        PressRelease("dodge");
        await Frames(5); // < 0.25s（15 帧）无敌窗口内
        Check(player.IsInvulnerable, "闪避前段处于无敌窗口");
        target.ApplyHit(new HitData { Damage = 10f });
        Check(php.CurrentHealth == before, $"无敌帧内受击免伤：HP 保持 {php.CurrentHealth}");

        await Frames(30); // 累计 35 帧 > 0.40s 闪避总时长，无敌窗口早已结束
        Check(!player.IsInvulnerable, "闪避后段无敌窗口结束");
        target.ApplyHit(new HitData { Damage = 10f });
        Check(php.CurrentHealth == before - 10f, $"无敌结束后恢复可命中：HP {php.CurrentHealth}");

        await EndPhase(main);
    }

    /// <summary>霸体（商业修复 M3 遗留断线）：冲锋中受击仍扣血但不打断；
    /// 无霸体时受击打断连段。</summary>
    private async Task SuperArmorPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        HealthComponent php = main.GetNode<HealthComponent>("Player/HealthComponent");
        ICombatTarget target = player;

        // 冲锋（skill_2，0.42s=25 帧）霸体：受击不打断
        player.Rotation = Vector3.Zero;
        PressRelease("skill_2");
        await Frames(8);
        Check(player.IsSuperArmor, "冲锋期间霸体生效");
        float before = php.CurrentHealth;
        target.ApplyHit(new HitData { Damage = 10f });
        await Frames(3);
        Check(player.IsChargeDashing, "霸体受击不打断冲锋");
        Check(php.CurrentHealth == before - 10f, $"霸体受击仍扣血：HP {php.CurrentHealth}");
        await Frames(50); // 等冲锋完整收尾

        // 无霸体：连段中被受击打断
        PressRelease("attack");
        await Frames(15); // 进入主动段（前摇 0.10s + 主动 0.12s）
        Check(player.IsAttacking, "连段进行中");
        target.ApplyHit(new HitData { Damage = 10f });
        await Frames(2);
        Check(!player.IsAttacking, "无霸体受击打断连段");

        await EndPhase(main);
    }

    /// <summary>死亡状态机（商业修复）：死亡后不可命中、移动冻结；
    /// 1.5s 重载计时未到即卸载场景——重载守卫应吞掉，不影响后续阶段。</summary>
    private async Task PlayerDeathPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        HealthComponent php = main.GetNode<HealthComponent>("Player/HealthComponent");

        Vector3 before = player.GlobalPosition;
        php.ApplyDamage(999f);
        await Frames(5);
        Check(player.IsDead, "玩家死亡进入死亡状态");
        Check(!((ICombatTarget)player).CanBeHit, "死亡后不可命中");

        Input.ActionPress("move_forward"); // 死亡冻结：按住前进也不该移动
        await Frames(30);
        Input.ActionRelease("move_forward");
        float moved = (player.GlobalPosition - before).Length();
        Check(moved < 0.1f, $"死亡后移动冻结（位移 {moved:F3}m）");

        await EndPhase(main); // 重载计时（90 帧）未到即卸载——守卫应跳过重载
        await Frames(10);
    }

    /// <summary>处决全流程（M4 顺延项补齐，规格 §5）：击倒→HUD提示→背向/超距否定→
    /// 无瞬移收敛（双向缓动）→顿帧击杀→回血奖励→回空闲。</summary>
    private async Task ExecutionPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        DummyEnemy dummy = main.GetNode<DummyEnemy>("Dummy1");
        HealthComponent dhp = main.GetNode<HealthComponent>("Dummy1/HealthComponent");
        HealthComponent php = main.GetNode<HealthComponent>("Player/HealthComponent");
        Label prompt = main.GetNode<Label>("Hud/ExecutionPrompt");

        // 击倒木桩（韧性 60，一击 999 韧性伤 → Downed = 可处决窗口）
        dummy.ApplyHit(new HitData { Damage = 5f, PoiseDamage = 999f });
        await Frames(5);
        Check(dummy.IsDowned, "木桩被击倒（进入可处决状态）");

        // 站到 1.2m 正前方（距离带 0.8~1.8m + 45° 锥形内）
        player.GlobalPosition = dummy.GlobalPosition + new Vector3(0, 0.2f, 1.2f);
        player.Rotation = Vector3.Zero;
        await Frames(5);
        Check(player.ExecutionReady, "距离带+锥形内处决就绪");
        Check(prompt.Visible, "HUD 处决按键提示显示");

        // 背对目标（偏差 180° > 45°）→ 不提示（DebugYaw：直接设 Rotation 会被 _Process 的 yaw 覆盖）
        player.DebugYaw = Mathf.Pi;
        await Frames(2);
        Check(!player.ExecutionReady, "背对目标不提示（45° 锥形约束）");
        player.DebugYaw = 0f;

        // 超距 3m（带外上界 1.8m）→ 不提示
        player.GlobalPosition = dummy.GlobalPosition + new Vector3(0, 0.2f, 3f);
        player.Rotation = Vector3.Zero;
        await Frames(2);
        Check(!player.ExecutionReady, "超距 3m 不提示（0.8~1.8m 距离带约束）");

        // 回到带内执行处决：收敛期逐帧采样，验证无瞬移
        player.GlobalPosition = dummy.GlobalPosition + new Vector3(0, 0.2f, 1.2f);
        await Frames(3);
        php.ApplyDamage(30f); // 120 -> 90，验证结算回血 18（15% 最大生命）
        PressRelease("execute");
        await Frames(2);
        Check(player.IsExecuting, "按键进入处决（锁定）");

        Vector3 prev = player.GlobalPosition;
        float maxStep = 0f;
        for (int i = 0; i < 10; i++) // 收敛 0.20s ≈ 12 帧
        {
            await Frames(1);
            maxStep = Mathf.Max(maxStep, (player.GlobalPosition - prev).Length());
            prev = player.GlobalPosition;
        }

        Check(maxStep < 0.15f, $"收敛无瞬移（最大单帧位移 {maxStep:F3}m，双向缓动）");

        await Frames(30); // 冲击（第 ~14 帧）+ 恢复 0.38s，留余量
        Check(dummy.IsDead || !IsInstanceValid(dummy), "冲击命中：处决目标死亡");
        Check(!player.IsExecuting, "处决结束回空闲");
        Check(
            Mathf.Abs(php.CurrentHealth - 108f) < 0.1f,
            $"处决回血奖励：HP {php.CurrentHealth}（90 + 120×15%）"
        );
        Check(dhp.CurrentHealth == 0f, "目标血量归零");

        await EndPhase(main);
    }

    private int CountShockwaves() =>
        GetTree().CurrentScene!.FindChildren("ShockwaveRing*", "", true, false).Count;

    // —— 阵型冒烟（M5 T5，对应验收六问）——

    /// <summary>阵型①②④：聚合激活+面向玩家；后排箭掉血；正面被前排挡停；
    /// 击杀中排骑士后缺口可穿（盾位悬空不补位）。</summary>
    private async Task FormationPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        FormationController formation = main.GetNode<FormationController>("Formation");
        FormationMelee knight = formation.GetNode<FormationMelee>("KnightC");
        HealthComponent php = main.GetNode<HealthComponent>("Player/HealthComponent");

        Check(!knight.Active, "阵型初始未激活（不干扰木桩区冒烟）");
        player.GlobalPosition = new Vector3(0, 0.9f, -10); // 距阵型中心 6.5m < 12m
        await Frames(10);
        Check(
            knight.Active && formation.GetNode<FormationMelee>("ShieldE").Active,
            "玩家进入半径后阵型聚合激活"
        );
        Check(
            Mathf.Abs(Mathf.Wrap(formation.FacingYawDeg - 180f, -180f, 180f)) < 1f,
            $"激活瞬间阵型面向玩家（yaw={formation.FacingYawDeg:F1}°）"
        );

        // ④ 后排箭矢压制
        float hpBefore = php.CurrentHealth;
        await Frames(110); // 瞄准 0.7s + 箭飞行 ~7.7m，留余量
        Check(
            php.CurrentHealth < hpBefore,
            $"后排箭矢使玩家掉血：{hpBefore} -> {php.CurrentHealth}"
        );

        // ① 正面冲击被前排挡停
        Input.ActionPress("move_forward");
        await Frames(90);
        Input.ActionRelease("move_forward");
        await Frames(5);
        float dist = (player.GlobalPosition - knight.GlobalPosition).Length();
        Check(
            dist > 0.75f && dist < 1.7f,
            $"正面被前排挡停在 {dist:F2}m（半径和 ≈0.9m 附近，前排是一道墙）"
        );

        // ② 击杀中排骑士 → 缺口可穿（其余成员槽位不动）
        float shieldEx = formation.GetNode<FormationMelee>("ShieldE").GlobalPosition.X;
        formation.GetNode<HealthComponent>("KnightC/HealthComponent").ApplyDamage(999f);
        await Frames(10);
        Input.ActionPress("move_forward");
        await Frames(120);
        Input.ActionRelease("move_forward");
        Check(
            player.GlobalPosition.Z < -18.3f,
            $"击杀中排后缺口可穿：Z={player.GlobalPosition.Z:F2}（越过后排线 -17.7）"
        );
        Check(
            Mathf.Abs(formation.GetNode<FormationMelee>("ShieldE").GlobalPosition.X - shieldEx)
                < 0.5f,
            "死亡成员槽位悬空，其余成员未重分配补位"
        );

        await EndPhase(main);
    }

    /// <summary>阵型③：朝向滞回——阈值内绕行不跟转；绕到阵后越阈才匀速转到位。</summary>
    private async Task FormationYawPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        FormationController formation = main.GetNode<FormationController>("Formation");

        player.GlobalPosition = new Vector3(0, 0.9f, -10);
        await Frames(10); // 激活：面向南（180°）

        // 阈值内（≈40°）绕行：不炮塔式跟转
        player.GlobalPosition = new Vector3(5.1f, 0.9f, -10.4f);
        await Frames(60);
        Check(
            Mathf.Abs(Mathf.Wrap(formation.FacingYawDeg - 180f, -180f, 180f)) < 1f,
            $"阈值内绕阵不跟转（yaw={formation.FacingYawDeg:F1}°，应保持 180°）"
        );

        // 绕到阵型背后（偏差 180° > 55°）：90°/s 匀速转到位（需 2s）
        player.GlobalPosition = new Vector3(0, 0.9f, -24f);
        await Frames(200);
        Check(
            Mathf.Abs(Mathf.Wrap(formation.FacingYawDeg, -180f, 180f)) < 3f,
            $"越阈后转向玩家并转到位（yaw={formation.FacingYawDeg:F1}°，应 ≈0°）"
        );

        await EndPhase(main);
    }

    /// <summary>阵型⑤a：冲锋推开 250 盾兵（推力 500 > 抗性 250）。</summary>
    private async Task FormationChargePushPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        FormationController formation = main.GetNode<FormationController>("Formation");
        FormationMelee shieldW = formation.GetNode<FormationMelee>("ShieldW");

        player.GlobalPosition = new Vector3(0, 0.9f, -10);
        await Frames(10);
        player.GlobalPosition = new Vector3(-1.4f, 0.2f, -12f);
        player.Rotation = Vector3.Zero; // 面向 -Z（阵型方向，正对西侧盾兵）
        await Frames(10);
        Vector3 before = shieldW.GlobalPosition;
        float maxPush = 0f;
        PressRelease("skill_2");
        // 盾兵被推的同时会走回槽位，取冲锋窗口内的峰值位移
        for (int i = 0; i < 15; i++)
        {
            await Frames(2);
            maxPush = Mathf.Max(maxPush, (shieldW.GlobalPosition - before).Length());
        }

        Check(maxPush > 0.8f, $"冲锋推开 250 盾兵：峰值位移 {maxPush:F2}m（冲锋=突破能力）");
        Check(!player.IsCastingSkill, "冲锋已结束");

        await EndPhase(main);
    }

    /// <summary>阵型⑤b：冲锋被 500 骑士挡停且骑士推不动（推力 500 ≤ 抗性 500）。</summary>
    private async Task FormationChargeStopPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        FormationController formation = main.GetNode<FormationController>("Formation");
        FormationMelee knight = formation.GetNode<FormationMelee>("KnightC");

        player.GlobalPosition = new Vector3(0, 0.9f, -10);
        await Frames(10);
        player.GlobalPosition = new Vector3(0, 0.2f, -12f);
        player.Rotation = Vector3.Zero;
        await Frames(10);
        Vector3 knightBefore = knight.GlobalPosition;
        float maxPush = 0f;
        PressRelease("skill_2");
        for (int i = 0; i < 18; i++) // 冲锋 0.42s ≈ 25 帧，取窗口峰值
        {
            await Frames(2);
            maxPush = Mathf.Max(maxPush, (knight.GlobalPosition - knightBefore).Length());
        }

        Check(maxPush < 1.5f, $"500 骑士冲锋窗口内峰值位移 {maxPush:F2}m < 1.5m（推不动）");
        float stopDist = (player.GlobalPosition - knight.GlobalPosition).Length();
        Check(
            stopDist > 0.8f && stopDist < 1.7f,
            $"冲锋被骑士挡停在 {stopDist:F2}m（重装是一道墙）"
        );
        Check(!player.IsCastingSkill, "冲锋已结束");

        await EndPhase(main);
    }

    /// <summary>阵型⑥：贴侧绕行被边缘前排蹭血（绕后需付出代价）。</summary>
    private async Task FormationFlankPhase()
    {
        (Node main, Player player) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
        FormationController formation = main.GetNode<FormationController>("Formation");
        HealthComponent php = main.GetNode<HealthComponent>("Player/HealthComponent");

        player.GlobalPosition = new Vector3(8, 0.9f, -16.5f); // 正东激活：阵型面向东
        await Frames(80); // 等成员走位到东侧朝向的新槽位再开始侧绕

        // 沿前排队侧 1.1m 处纵穿（全程偏差 < 55°，阵型不转）
        player.GlobalPosition = new Vector3(2.6f, 0.9f, -18f);
        await Frames(5);
        float hpBefore = php.CurrentHealth;
        Input.ActionPress("move_forward");
        await Frames(60);
        Input.ActionRelease("move_forward");
        Check(
            php.CurrentHealth < hpBefore,
            $"贴侧绕行被边缘前排蹭血：{hpBefore} -> {php.CurrentHealth}（侧面绕行有成本）"
        );

        await EndPhase(main);
    }

    // —— 工具 ——

    private async Task<(Node main, Player player)> StartPhase(string definitionPath)
    {
        GameSession.Instance!.SelectedCharacter = ResourceLoader.Load<CharacterDefinition>(
            definitionPath
        );
        Node main = ResourceLoader.Load<PackedScene>("res://Game/Scenes/Main.tscn").Instantiate();
        AddChild(main);
        await Frames(5);
        return (main, main.GetNode<Player>("Player"));
    }

    private async Task EndPhase(Node main)
    {
        main.QueueFree();
        await Frames(3);
    }

    private void Check(bool ok, string message)
    {
        _executedChecks++;
        if (ok)
        {
            GD.Print($"[SMOKE][PASS] {message}");
        }
        else
        {
            GD.PrintErr($"[SMOKE][FAIL] {message}");
            _failures++;
        }
    }

    // 冒烟与正式游戏共用 user:// 目录：改键阶段会写/删 keybinds.cfg，
    // 备份还原防止测试销毁真实玩家键位
    private static byte[]? BackupUserKeybinds()
    {
        if (!FileAccess.FileExists("user://keybinds.cfg"))
        {
            return null;
        }

        using FileAccess f = FileAccess.Open("user://keybinds.cfg", FileAccess.ModeFlags.Read);
        return f?.GetBuffer((long)f.GetLength());
    }

    private static void RestoreUserKeybinds(byte[]? backup)
    {
        if (backup == null)
        {
            if (FileAccess.FileExists("user://keybinds.cfg"))
            {
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://keybinds.cfg"));
            }

            return;
        }

        using FileAccess f = FileAccess.Open("user://keybinds.cfg", FileAccess.ModeFlags.Write);
        f?.StoreBuffer(backup);
    }

    private void PressRelease(string action)
    {
        Input.ActionPress(action);
        _ = ReleaseLater(action);
    }

    private async Task ReleaseLater(string action)
    {
        await Frames(3);
        Input.ActionRelease(action);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
