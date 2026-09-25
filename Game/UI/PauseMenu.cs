using FirstPersonAction.Combat;
using Godot;

namespace FirstPersonAction.UI;

/// <summary>
/// 暂停菜单（autoload）：ESC/ui_cancel 切换暂停并接管鼠标。
/// 修复 M4 遗留的输入死锁——此前 ESC 只释放鼠标且无任何重新捕获路径。
/// ProcessMode=Always：未暂停时接 ESC，暂停时仍可处理菜单输入；
/// 其余节点默认可暂停，全树随暂停冻结（Player/敌人/计时）。
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private Control _panel = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 10; // HUD（layer 1）之上
        _panel = GetNode<Control>("Panel");
        _panel.Visible = false;
        GetNode<Button>("Panel/Center/VBox/ResumeButton").Pressed += Resume;
        GetNode<Button>("Panel/Center/VBox/RestartButton").Pressed += RestartLevel;
        GetNode<Button>("Panel/Center/VBox/QuitButton").Pressed += QuitGame;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (GetTree().Paused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }
    }

    private void Pause()
    {
        GetTree().Paused = true;
        _panel.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void Resume()
    {
        GetTree().Paused = false;
        _panel.Visible = false;
        RestoreMouseMode();
    }

    private void RestartLevel()
    {
        GetTree().Paused = false;
        _panel.Visible = false;
        RestoreMouseMode();
        GetTree().ReloadCurrentScene();
    }

    private void QuitGame() => GetTree().Quit();

    /// <summary>关卡内重捕获鼠标；选人等 UI 场景保持鼠标可见（无玩家在场）。</summary>
    private void RestoreMouseMode()
    {
        bool inLevel = GetTree().GetFirstNodeInGroup(CombatTuning.PlayerGroup) != null;
        Input.MouseMode = inLevel ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }
}
