using System.Collections.Generic;
using FirstPersonAction.Combat;
using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.UI;

/// <summary>
/// 暂停菜单（autoload）：ESC/ui_cancel 切换暂停并接管鼠标。
/// 修复 M4 遗留的输入死锁——此前 ESC 只释放鼠标且无任何重新捕获路径。
/// ProcessMode=Always：未暂停时接 ESC，暂停时仍可处理菜单输入；
/// 其余节点默认可暂停，全树随暂停冻结（Player/敌人/计时）。
/// 内含键位设置面板（M6 收尾）：改键底层 KeybindManager 的 UI 消费方——
/// 重绑/保存/恢复默认全闭环，冒烟覆盖。
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["move_forward"] = "前进",
        ["move_back"] = "后退",
        ["move_left"] = "左移",
        ["move_right"] = "右移",
        ["jump"] = "跳跃",
        ["attack"] = "攻击",
        ["dodge"] = "闪避",
        ["aim"] = "瞄准",
        ["execute"] = "处决",
        ["skill_1"] = "技能 1",
        ["skill_2"] = "技能 2",
        ["skill_3"] = "技能 3",
    };

    private Control _panel = null!;
    private Control _keybindPanel = null!;
    private readonly Dictionary<string, Button> _keyButtons = new();

    /// <summary>等待捕获下一个按键的重绑动作（null=未在捕获）。</summary>
    private string? _pendingRebind;

    /// <summary>键位设置面板可见（冒烟断言用）。</summary>
    public bool IsKeybindVisible => _keybindPanel.Visible;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 10; // HUD（layer 1）之上
        _panel = GetNode<Control>("Panel");
        _panel.Visible = false;
        _keybindPanel = GetNode<Control>("KeybindPanel");
        _keybindPanel.Visible = false;
        Click("Panel/Center/VBox/ResumeButton", Resume);
        Click("Panel/Center/VBox/KeybindButton", OpenKeybindSettings);
        Click("Panel/Center/VBox/RestartButton", RestartLevel);
        Click("Panel/Center/VBox/QuitButton", QuitGame);
        Click("KeybindPanel/Center/VBox/ResetButton", ResetKeybinds);
        Click("KeybindPanel/Center/VBox/CloseButton", CloseKeybindSettings);
        BuildKeybindRows();
    }

    /// <summary>按钮接线 + 统一点击音。</summary>
    private void Click(string path, System.Action handler)
    {
        Button button = GetNode<Button>(path);
        button.Pressed += ClickSound;
        button.Pressed += handler;
    }

    private static void ClickSound() => Sfx.Play("ui_click");

    public override void _UnhandledInput(InputEvent @event)
    {
        // 重绑捕获优先于 ESC：捕获中的任何按键（含 Escape=取消）不落到暂停切换
        if (_pendingRebind != null)
        {
            if (@event is InputEventKey { Echo: false, Pressed: true } key)
            {
                string action = _pendingRebind;
                _pendingRebind = null;
                if (key.PhysicalKeycode != Key.Escape)
                {
                    KeybindManager.Instance!.Rebind(action, key.PhysicalKeycode);
                    KeybindManager.Instance!.Save();
                }

                RefreshKeybindButtons();
            }

            return;
        }

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
        _keybindPanel.Visible = false;
        RestoreMouseMode();
    }

    private void RestartLevel()
    {
        GetTree().Paused = false;
        _panel.Visible = false;
        _keybindPanel.Visible = false;
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

    // —— 键位设置面板 ——

    /// <summary>打开键位设置（主菜单「键位设置」入口；冒烟亦直调）。</summary>
    public void OpenKeybindSettings()
    {
        _pendingRebind = null;
        RefreshKeybindButtons();
        _keybindPanel.Visible = true;
    }

    public void CloseKeybindSettings()
    {
        _pendingRebind = null;
        _keybindPanel.Visible = false;
    }

    /// <summary>开始捕获某动作的新键（行按钮点击；冒烟亦直调）。</summary>
    public void BeginRebind(string action)
    {
        _pendingRebind = action;
        RefreshKeybindButtons();
    }

    /// <summary>恢复默认键位并清除持久化。</summary>
    public void ResetKeybinds()
    {
        _pendingRebind = null;
        KeybindManager.Instance!.ResetToDefaults();
        RefreshKeybindButtons();
    }

    private void BuildKeybindRows()
    {
        var rows = GetNode<VBoxContainer>("KeybindPanel/Center/VBox/Scroll/Rows");
        foreach (string action in KeybindManager.Actions)
        {
            var row = new HBoxContainer();
            row.AddChild(
                new Label
                {
                    Text = ActionLabels.GetValueOrDefault(action, action),
                    CustomMinimumSize = new Vector2(120, 0),
                }
            );
            var button = new Button { CustomMinimumSize = new Vector2(160, 0) };
            string captured = action; // 闭包捕获循环变量
            button.Pressed += () => BeginRebind(captured);
            row.AddChild(button);
            rows.AddChild(row);
            _keyButtons[action] = button;
        }
    }

    private void RefreshKeybindButtons()
    {
        foreach (KeyValuePair<string, Button> pair in _keyButtons)
        {
            Key? key = KeybindManager.Instance?.CurrentKey(pair.Key);
            pair.Value.Text = _pendingRebind == pair.Key ? "按新键…" : (key?.ToString() ?? "-");
        }
    }
}
