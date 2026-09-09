using Godot;
using GodotGameTemplate.Characters;

namespace GodotGameTemplate.Core;

/// <summary>
/// 跨场景会话状态（autoload）：当前所选角色定义。
/// 单机版全局可读；联机时由大厅/房间流程按玩家填充并同步。
/// </summary>
public partial class GameSession : Node
{
    public static GameSession? Instance { get; private set; }

    public CharacterDefinition? SelectedCharacter { get; set; }

    public override void _EnterTree() => Instance = this;

    /// <summary>未选择时回退到战士（直接运行 Main 场景与自动化冒烟测试用）。</summary>
    public CharacterDefinition EnsureSelected()
    {
        if (SelectedCharacter != null)
        {
            return SelectedCharacter;
        }

        SelectedCharacter = ResourceLoader.Load<CharacterDefinition>(
            "res://Game/Config/Characters/Warrior.tres"
        );
        return SelectedCharacter!;
    }
}
