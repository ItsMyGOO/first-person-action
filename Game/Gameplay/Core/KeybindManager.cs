using System.Collections.Generic;
using Godot;

namespace FirstPersonAction.Core;

/// <summary>
/// 技能改键底层（M4 §3，本次无 UI）：启动时加载 user://keybinds.cfg 并应用到 InputMap，
/// 提供 Rebind/Save/ResetToDefaults 供将来设置界面直接调用。
/// 只替换动作的键盘事件（保留鼠标/手柄绑定）；默认键位在启动时捕获。
/// </summary>
public partial class KeybindManager : Node
{
    private const string ConfigPath = "user://keybinds.cfg";
    private const string Section = "keybinds";

    private static readonly string[] ManagedActions =
    {
        "move_forward",
        "move_back",
        "move_left",
        "move_right",
        "jump",
        "attack",
        "dodge",
        "aim",
        "execute",
        "skill_1",
        "skill_2",
        "skill_3",
    };

    public static KeybindManager? Instance { get; private set; }

    private readonly Dictionary<string, Key> _defaults = new();

    public override void _Ready()
    {
        Instance = this;
        foreach (string action in ManagedActions)
        {
            if (!InputMap.HasAction(action))
            {
                continue;
            }

            foreach (InputEvent @event in InputMap.ActionGetEvents(action))
            {
                if (@event is InputEventKey key)
                {
                    _defaults[action] = key.PhysicalKeycode;
                    break;
                }
            }
        }

        Load();
    }

    /// <summary>动作当前绑定的物理键（无键盘绑定时返回 null；HUD 提示等只读场景用）。</summary>
    public Key? CurrentKey(string action)
    {
        if (!InputMap.HasAction(action))
        {
            return null;
        }

        foreach (InputEvent @event in InputMap.ActionGetEvents(action))
        {
            if (@event is InputEventKey key)
            {
                return key.PhysicalKeycode;
            }
        }

        return null;
    }

    /// <summary>把 action 重绑到指定物理键：清除该动作的键盘事件后写入新键（保留鼠标/手柄）。</summary>
    public void Rebind(string action, Key key)
    {
        if (!InputMap.HasAction(action))
        {
            return;
        }

        foreach (InputEvent @event in InputMap.ActionGetEvents(action))
        {
            if (@event is InputEventKey)
            {
                InputMap.ActionEraseEvent(action, @event);
            }
        }

        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    /// <summary>把全部受管动作当前的键盘绑定持久化到 user://keybinds.cfg。</summary>
    public void Save()
    {
        var config = new ConfigFile();
        foreach (string action in ManagedActions)
        {
            foreach (InputEvent @event in InputMap.ActionGetEvents(action))
            {
                if (@event is InputEventKey key)
                {
                    config.SetValue(Section, action, (long)key.PhysicalKeycode);
                    break;
                }
            }
        }

        config.Save(ConfigPath);
    }

    /// <summary>恢复启动时捕获的默认键位，并删除持久化文件。</summary>
    public void ResetToDefaults()
    {
        foreach (KeyValuePair<string, Key> pair in _defaults)
        {
            Rebind(pair.Key, pair.Value);
        }

        if (FileAccess.FileExists(ConfigPath))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ConfigPath));
        }
    }

    private void Load()
    {
        var config = new ConfigFile();
        if (config.Load(ConfigPath) != Error.Ok)
        {
            return;
        }

        foreach (string action in config.GetSectionKeys(Section) ?? [])
        {
            // cfg 可被手改：未知动作/非法键值必须报错跳过，静默写入会产生无效绑定
            if (!InputMap.HasAction(action))
            {
                GD.PushError($"[KeybindManager] keybinds.cfg 中的动作「{action}」不存在，已忽略");
                continue;
            }

            long keycode = config.GetValue(Section, action, 0L).AsInt64();
            if (keycode != 0 && System.Enum.IsDefined(typeof(Key), keycode))
            {
                Rebind(action, (Key)keycode);
            }
            else
            {
                GD.PushError($"[KeybindManager] 动作「{action}」的键值 {keycode} 非法，保持默认");
            }
        }
    }
}
