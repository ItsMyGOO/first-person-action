using Godot;

namespace GodotGameTemplate.Core;

/// <summary>语义化输入命令种类。联机时命令序列化后发往房主权威端。</summary>
public enum InputCommandKind : byte
{
    None = 0,
    Move,       // 连续移动意图（Axis 为朝向）；不进缓冲，每帧即时生效
    Attack,
    Dodge,
    Jump,
    Execute,    // M4 处决
    Skill1,     // M3 技能框架
    Skill2,
    Skill3,
}

/// <summary>可序列化的输入命令（联机边界，规格第 6 节第 3 条）。</summary>
public struct InputCommand
{
    public InputCommandKind Kind;
    public Vector2 Axis; // 仅 Move 使用

    public static InputCommand Action(InputCommandKind kind) =>
        new InputCommand { Kind = kind, Axis = Vector2.Zero };
}
