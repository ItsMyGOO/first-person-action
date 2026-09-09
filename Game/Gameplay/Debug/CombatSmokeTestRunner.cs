using System.Threading.Tasks;
using Godot;
using GodotGameTemplate.Characters;
using GodotGameTemplate.Combat;
using GodotGameTemplate.Core;

namespace GodotGameTemplate.DebugTools;

/// <summary>
/// 引擎内战斗冒烟测试（无头可跑，CI 回归）。覆盖 M3 修订版全部验收项：
/// 连段/逻辑阻挡/冲锋推挤/重单位挡停/跳劈/旋风斩/弓手快速箭与三级蓄力/打断规则。
/// 每个阶段用全新的 Main 实例（技能 CD/木桩状态互不干扰），输入注入用
/// Input.ActionPress/ActionRelease（与控制器的物理帧边沿轮询匹配）。
/// 运行：godot --headless --path . res://Game/Scenes/Debug/CombatSmokeTest.tscn --quit-after 1800
/// </summary>
public partial class CombatSmokeTestRunner : Node
{
    private int _failures;

    public override async void _Ready()
    {
        await WarriorCombatPhase();
        await WarriorSkillPhase();
        await HeavyBlockPhase();
        await ArcherPhase();
        await ArrowDirectionPhase();
        await KeybindPhase();
        await RangedEnemyPhase();
        await EnemyGroupPhase();
        await FeelPhase();

        GD.Print(_failures == 0 ? "[SMOKE] 全部通过" : $"[SMOKE] {_failures} 项失败");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

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
        // 无头模式下鼠标事件注入不可靠，测试直接设私有俯仰字段。
        typeof(Player)
            .GetField(
                "_pitch",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            )!
            .SetValue(player, -0.35f);
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
        System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(Player).GetField("_yaw", flags)!.SetValue(player, Mathf.Pi);
        typeof(Player).GetField("_pitch", flags)!.SetValue(player, -0.35f);
        await Frames(10); // 等 _Process 把反射设置的偏航写入变换

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

        // 玩家逼近（距编组 10m < 12m 激活半径）→ 聚合激活
        player.GlobalPosition = new Vector3(0, 0.9f, -8);
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
        await Frames(80); // 起跳 0.5s 位移 + 落地
        Check(leapLanded, "跳劈落地触发 LeapLanded");
        int ringsAfter = CountShockwaves();
        Check(ringsAfter > ringsBefore, $"落地生成 ShockwaveRing（{ringsAfter - ringsBefore} 个）");
        await Frames(50); // 0.4s 生命 + 余量
        Check(CountShockwaves() == ringsBefore, "ShockwaveRing 0.6s 内自毁");

        await EndPhase(main);
    }

    private int CountShockwaves() =>
        GetTree().CurrentScene!.FindChildren("ShockwaveRing*", "", true, false).Count;

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
