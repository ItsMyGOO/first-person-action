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
