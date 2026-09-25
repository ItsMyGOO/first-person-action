using FirstPersonAction.Combat;
using Godot;

namespace FirstPersonAction.UI;

/// <summary>
/// HUD（占位样式）：弓箭手显示准星，拉弓蓄力时显示蓄力条。
/// 只读玩家状态刷新（规格第 1 节表现层），不回写模拟。
/// </summary>
public partial class Hud : CanvasLayer
{
    private ProgressBar _chargeBar = null!;
    private ProgressBar _playerBar = null!;
    private ColorRect _crosshair = null!;
    private Combat.Player? _player;

    public override void _Ready()
    {
        _chargeBar = GetNode<ProgressBar>("ChargeBar");
        _crosshair = GetNode<ColorRect>("Crosshair");
        _playerBar = GetNode<ProgressBar>("PlayerBar");
    }

    public override void _Process(double delta)
    {
        _player ??= GetTree().GetFirstNodeInGroup(CombatTuning.PlayerGroup) as Combat.Player;
        if (_player == null)
        {
            _crosshair.Visible = false;
            _chargeBar.Visible = false;
            _playerBar.Visible = false;
            return;
        }

        _crosshair.Visible = _player.ShowCrosshair;
        _chargeBar.Visible = _player.IsCharging;
        _chargeBar.Value = _player.ChargeProgress01 * 100.0;
        _playerBar.Visible = true; // 常驻玩家血条（M4 §2）
        _playerBar.Value = _player.Health01 * 100.0;
    }
}
