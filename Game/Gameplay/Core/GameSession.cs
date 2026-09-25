using FirstPersonAction.Characters;
using FirstPersonAction.Combat;
using Godot;

namespace FirstPersonAction.Core;

/// <summary>
/// 跨场景会话状态（autoload）：当前所选角色定义。
/// 单机版全局可读；联机时由大厅/房间流程按玩家填充并同步。
/// </summary>
public partial class GameSession : Node
{
    private const string DefaultDefinitionPath = "res://Game/Config/Characters/Warrior.tres";

    public static GameSession? Instance { get; private set; }

    public CharacterDefinition? SelectedCharacter { get; set; }

    public override void _EnterTree() => Instance = this;

    /// <summary>
    /// 未选择时回退到战士（直接运行 Main 场景与自动化冒烟测试用）。
    /// .tres 加载失败时报错并回退到代码内默认定义——崩在 _Ready 的 NRE
    /// 指向使用处而非根因，这里是唯一能给出可读错误的位置。
    /// </summary>
    public CharacterDefinition EnsureSelected()
    {
        if (SelectedCharacter != null)
        {
            return SelectedCharacter;
        }

        SelectedCharacter = ResourceLoader.Load<CharacterDefinition>(DefaultDefinitionPath);
        if (SelectedCharacter == null)
        {
            GD.PushError(
                $"[GameSession] 默认角色定义加载失败：{DefaultDefinitionPath}，使用代码内兜底定义"
            );
            SelectedCharacter = CreateFallbackDefinition();
        }

        return SelectedCharacter;
    }

    /// <summary>兜底定义与 Warrior.tres 数值一致（战士：跳劈/冲锋/旋风斩）。</summary>
    private static CharacterDefinition CreateFallbackDefinition() =>
        new()
        {
            DisplayName = "战士（兜底）",
            Style = AttackStyle.Melee,
            MaxHealth = 120f,
            WalkSpeed = 5.0f,
            SkillIds = new[]
            {
                (int)SkillKind.LeapSlam,
                (int)SkillKind.Charge,
                (int)SkillKind.Whirlwind,
            },
        };
}
