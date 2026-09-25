using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.UI;

/// <summary>启动选人界面：点选角色 → 写入 GameSession → 进入竞技场。</summary>
public partial class CharacterSelect : Control
{
    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetNode<Button>("Panel/VBox/WarriorButton").Pressed += () =>
            Select("res://Game/Config/Characters/Warrior.tres");
        GetNode<Button>("Panel/VBox/ArcherButton").Pressed += () =>
            Select("res://Game/Config/Characters/Archer.tres");
    }

    private void Select(string path)
    {
        GameSession.Instance!.SelectedCharacter =
            ResourceLoader.Load<Characters.CharacterDefinition>(path);
        GetTree().ChangeSceneToFile("res://Game/Scenes/Main.tscn");
    }
}
