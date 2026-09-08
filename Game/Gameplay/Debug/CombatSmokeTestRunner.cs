using Godot;

namespace GodotGameTemplate.DebugTools;

/// <summary>
/// 引擎内战斗冒烟测试（无头可跑，作为 CI 回归）：
/// 实例化竞技场 → 把玩家摆到木桩正前方 → 注入攻击按键事件 →
/// 断言木桩掉血 → 以退出码 0/1 报告结果。
/// 运行：godot --headless --path . res://Game/Scenes/Debug/CombatSmokeTest.tscn --quit-after 600
/// </summary>
public partial class CombatSmokeTestRunner : Node
{
    public override async void _Ready()
    {
        int failures = 0;

        PackedScene mainScene = ResourceLoader.Load<PackedScene>("res://Game/Scenes/Main.tscn");
        Node main = mainScene.Instantiate();
        AddChild(main);

        for (int i = 0; i < 5; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        Combat.Player player = main.GetNode<Combat.Player>("Player");
        Combat.DummyEnemy dummy = main.GetNode<Combat.DummyEnemy>("Dummy1");
        Combat.HealthComponent dummyHealth = main.GetNode<Combat.HealthComponent>("Dummy1/HealthComponent");

        // 摆位：玩家在木桩南面 1.5m，默认朝向 -Z 正对木桩
        player.GlobalPosition = dummy.GlobalPosition + new Vector3(0, 0.2f, 1.5f);
        player.Rotation = Vector3.Zero;

        float hpBefore = dummyHealth.CurrentHealth;

        // 注入攻击按下事件，走完整输入管线（_UnhandledInput → 缓冲 → 连段 → 判定）
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });

        for (int i = 0; i < 90; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        float hpAfter = dummyHealth.CurrentHealth;

        if (hpAfter < hpBefore)
        {
            GD.Print($"[SMOKE][PASS] 攻击链路生效：HP {hpBefore} -> {hpAfter}");
        }
        else
        {
            GD.PrintErr($"[SMOKE][FAIL] 攻击未造成伤害：HP 仍为 {hpAfter}");
            failures++;
        }

        // 连段链：继续注入两次攻击，应推进到第 3 段（快速连点节奏下）
        for (int attack = 0; attack < 2; attack++)
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            for (int i = 0; i < 12; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
        }

        if (player.IsAttacking || hpAfter - dummyHealth.CurrentHealth >= 3)
        {
            GD.Print("[SMOKE][PASS] 连段推进生效");
        }
        else
        {
            GD.PrintErr("[SMOKE][FAIL] 连段未按预期推进");
            failures++;
        }

        GD.Print(failures == 0 ? "[SMOKE] 全部通过" : $"[SMOKE] {failures} 项失败");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
