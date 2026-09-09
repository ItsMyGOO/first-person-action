using System.Threading.Tasks;
using Godot;

namespace GodotGameTemplate.DebugTools;

/// <summary>
/// 引擎内战斗冒烟测试（无头可跑，作为 CI 回归）：
/// 实例化竞技场 → 摆位/注入动作级输入 → 断言伤害与空间行为 → 退出码报告。
/// 输入注入用 Input.ActionPress/ActionRelease（与控制器的物理帧边沿轮询匹配）。
/// 运行：godot --headless --path . res://Game/Scenes/Debug/CombatSmokeTest.tscn --quit-after 900
/// </summary>
public partial class CombatSmokeTestRunner : Node
{
    private int _failures;

    private Node? _main;
    private Combat.Player? _player;
    private Combat.DummyEnemy? _dummy;
    private Combat.HealthComponent? _dummyHealth;

    public override async void _Ready()
    {
        PackedScene mainScene = ResourceLoader.Load<PackedScene>("res://Game/Scenes/Main.tscn");
        _main = mainScene.Instantiate();
        AddChild(_main);

        for (int i = 0; i < 5; i++)
        {
            await Frames(1);
        }

        _player = _main.GetNode<Combat.Player>("Player");
        _dummy = _main.GetNode<Combat.DummyEnemy>("Dummy1");
        _dummyHealth = _main.GetNode<Combat.HealthComponent>("Dummy1/HealthComponent");

        await TestMeleeChain();
        await TestSpatialBlocking();

        GD.Print(_failures == 0 ? "[SMOKE] 全部通过" : $"[SMOKE] {_failures} 项失败");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // —— 用例 ——

    private async Task TestMeleeChain()
    {
        // 摆位：玩家在木桩南面 1.6m，默认朝向 -Z 正对木桩（阻挡停驻距离 ≈0.95m，攻击距离 2.2m 覆盖）
        _player!.GlobalPosition = _dummy!.GlobalPosition + new Vector3(0, 0.2f, 1.6f);
        _player.Rotation = Vector3.Zero;

        float hpBefore = _dummyHealth!.CurrentHealth;

        // 第 1 段
        PressRelease("attack");
        await Frames(40);
        float hpAfter1 = _dummyHealth.CurrentHealth;
        Check(hpAfter1 < hpBefore, $"第1段命中：HP {hpBefore} -> {hpAfter1}");

        // 快速接第 2、3 段
        PressRelease("attack");
        await Frames(20);
        PressRelease("attack");
        await Frames(50);
        float totalDamage = hpBefore - _dummyHealth.CurrentHealth;
        Check(totalDamage >= 30f, $"连段累计伤害 {totalDamage}（三段 10+12+22 链成立）");
    }

    private async Task TestSpatialBlocking()
    {
        // 从 3m 外向木桩走：逻辑空间应把玩家停在半径和（≈0.95m）附近，而不是穿过
        _player!.GlobalPosition = _dummy!.GlobalPosition + new Vector3(0, 0, 3f);
        _player.Rotation = Vector3.Zero;
        await Frames(10);

        Input.ActionPress("move_forward");
        await Frames(90);
        Input.ActionRelease("move_forward");
        await Frames(5);

        float dist = (_player.GlobalPosition - _dummy.GlobalPosition).Length();
        Check(dist > 0.75f && dist < 1.4f, $"玩家被木桩挡在 {dist:F2}m（期望 ~0.95m 半径和附近，不穿不弹）");
    }

    // —— 工具 ——

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

    /// <summary>按下并在数帧后释放一个动作（跨帧，边沿轮询可见）。</summary>
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
