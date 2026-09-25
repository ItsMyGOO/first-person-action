using System.Collections.Generic;
using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.UI;

/// <summary>
/// 启动选人界面：扫描 Game/Config/Characters/*.tres 动态生成按钮。
/// 数据驱动承诺的最后一环——加角色 = 只加 .tres 文件，不改 UI 代码。
/// </summary>
public partial class CharacterSelect : Control
{
    private const string CharactersDir = "res://Game/Config/Characters";

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;

        var vbox = GetNode<VBoxContainer>("Panel/VBox");
        int buttons = 0;
        foreach (string path in ListDefinitionPaths())
        {
            var definition = ResourceLoader.Load<Characters.CharacterDefinition>(path);
            if (definition == null)
            {
                GD.PushError($"[CharacterSelect] 角色定义加载失败：{path}");
                continue;
            }

            var button = new Button
            {
                Text = definition.DisplayName,
                CustomMinimumSize = new Vector2(220, 48),
            };
            string captured = path; // 闭包捕获循环变量
            button.Pressed += () => Select(captured);
            vbox.AddChild(button);
            buttons++;
        }

        if (buttons == 0)
        {
            GD.PushError($"[CharacterSelect] {CharactersDir} 下没有任何可用的角色定义");
        }
    }

    private static List<string> ListDefinitionPaths()
    {
        var paths = new List<string>();
        using DirAccess? dir = DirAccess.Open(CharactersDir);
        if (dir == null)
        {
            GD.PushError($"[CharacterSelect] 无法打开角色目录：{CharactersDir}");
            return paths;
        }

        dir.ListDirBegin();
        for (string file = dir.GetNext(); file != ""; file = dir.GetNext())
        {
            if (file.EndsWith(".tres"))
            {
                paths.Add($"{CharactersDir}/{file}");
            }
        }

        dir.ListDirEnd();
        paths.Sort(); // 确定顺序：按文件名字典序
        return paths;
    }

    private void Select(string path)
    {
        GameSession.Instance!.SelectedCharacter =
            ResourceLoader.Load<Characters.CharacterDefinition>(path);
        GetTree().ChangeSceneToFile("res://Game/Scenes/Main.tscn");
    }
}
