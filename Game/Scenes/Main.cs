using Godot;
using GodotGameTemplate.Characters;
using GodotGameTemplate.Core;

namespace GodotGameTemplate.Combat;

/// <summary>竞技场：按 GameSession 所选角色生成玩家（未选择时回退默认战士）。</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        CharacterDefinition definition = GameSession.Instance!.EnsureSelected();
        PackedScene playerScene = ResourceLoader.Load<PackedScene>("res://Game/Scenes/Player.tscn");
        Player player = playerScene.Instantiate<Player>();
        player.Definition = definition;
        player.Position = GetNode<Node3D>("PlayerSpawn").Position;
        AddChild(player);
    }
}
