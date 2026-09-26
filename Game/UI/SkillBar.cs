using System.Collections.Generic;
using FirstPersonAction.Combat;
using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.UI;

/// <summary>
/// 技能栏（M8，龙之谷式底部居中）：槽位 = 角色技能槽 skill_1..N（左侧数字键）。
/// 占位图标 = 技能色块 + 显示名首字（真资产到位后换 TextureRect，同音频占位策略）；
/// 冷却 = 径向遮罩 shader + 秒数；键位标签跟随 KeybindManager 实际绑定。
/// 只读轮询（规格第 1 节），不回写模拟。
/// </summary>
public partial class SkillBar : Control
{
    private const float SlotSize = 64f;

    private static readonly Dictionary<SkillKind, Color> KindColors = new()
    {
        [SkillKind.LeapSlam] = new(0.95f, 0.55f, 0.2f), // 跳劈·橙
        [SkillKind.Charge] = new(0.3f, 0.5f, 0.9f), // 冲锋·蓝
        [SkillKind.Whirlwind] = new(0.9f, 0.8f, 0.25f), // 旋风斩·黄
        [SkillKind.Backstep] = new(0.35f, 0.75f, 0.4f), // 后跳·绿
        [SkillKind.DashThrough] = new(0.65f, 0.4f, 0.85f), // 疾行·紫
    };

    private sealed class Slot
    {
        public PanelContainer Panel = null!;
        public Label Glyph = null!; // 占位图标：技能名首字
        public Label Key = null!; // 左上角键位标签
        public Label Seconds = null!; // 居中秒数
        public ColorRect Mask = null!; // 径向冷却遮罩
        public ShaderMaterial MaskMaterial = null!;
        public string ActionName = "";
    }

    private readonly List<Slot> _slots = new();
    private readonly List<float> _cooldown01Cache = new(); // 冒烟断言用：最近一帧各槽冷却比例
    private HBoxContainer _row = null!;
    private Combat.Player? _player;

    public override void _Ready()
    {
        _row = GetNode<HBoxContainer>("Row");
    }

    public override void _Process(double delta)
    {
        if (_player == null || !IsInstanceValid(_player))
        {
            _player = GetTree().GetFirstNodeInGroup(CombatTuning.PlayerGroup) as Combat.Player;
            if (_player == null)
            {
                return;
            }
        }

        if (_slots.Count != _player.Abilities.Count)
        {
            RebuildSlots(); // 换角色（槽数变化）自动重建
        }

        for (int i = 0; i < _slots.Count; i++)
        {
            UpdateSlot(i, _player.Abilities[i]);
        }
    }

    private void RebuildSlots()
    {
        foreach (Slot slot in _slots)
        {
            slot.Panel.QueueFree();
        }

        _slots.Clear();
        _cooldown01Cache.Clear();
        if (_player == null)
        {
            return;
        }

        for (int i = 0; i < _player.Abilities.Count; i++)
        {
            _slots.Add(BuildSlot(_player.Abilities[i].Def, $"skill_{i + 1}"));
            _cooldown01Cache.Add(0f);
        }
    }

    private Slot BuildSlot(SkillDefinition def, string actionName)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(SlotSize, SlotSize) };
        Color color = KindColors.GetValueOrDefault(def.Kind, new Color(0.5f, 0.5f, 0.5f));

        panel.AddChild(new ColorRect { Color = color, MouseFilter = MouseFilterEnum.Ignore });

        var glyph = new Label
        {
            Text = def.DisplayName.Length > 0 ? def.DisplayName[..1] : "?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        glyph.AddThemeFontSizeOverride("font_size", 28);
        glyph.AddThemeColorOverride("font_color", new Color(0.1f, 0.1f, 0.12f));
        panel.AddChild(glyph);

        var maskMaterial =
            new ShaderMaterial
            {
                Shader = ResourceLoader.Load<Shader>(
                    "res://Game/UI/Shaders/cooldown_mask.gdshader"
                )!,
            };
        var mask = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            Material = maskMaterial,
        };
        panel.AddChild(mask);

        var seconds = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        seconds.AddThemeFontSizeOverride("font_size", 20);
        seconds.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        seconds.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        seconds.AddThemeConstantOverride("outline_size", 3);
        panel.AddChild(seconds);

        var key = new Label
        {
            Text = "",
            Position = new Vector2(3, 1),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        key.AddThemeFontSizeOverride("font_size", 12);
        key.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        key.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        key.AddThemeConstantOverride("outline_size", 2);
        panel.AddChild(key);

        _row.AddChild(panel);
        return new Slot
        {
            Panel = panel,
            Glyph = glyph,
            Key = key,
            Seconds = seconds,
            Mask = mask,
            MaskMaterial = maskMaterial,
            ActionName = actionName,
        };
    }

    /// <summary>每帧刷新：键位标签（重绑跟随）+ 冷却遮罩与秒数。</summary>
    private void UpdateSlot(int index, Ability ability)
    {
        Slot slot = _slots[index];
        Key? current = KeybindManager.Instance?.CurrentKey(slot.ActionName);
        slot.Key.Text = current?.ToString() ?? "-";

        float cd01 = SkillBarLogic.Cooldown01(ability.CooldownRemaining, ability.Def.Cooldown);
        _cooldown01Cache[index] = cd01;
        slot.Mask.Visible = cd01 > 0f;
        slot.MaskMaterial.SetShaderParameter("progress", cd01);
        slot.Seconds.Visible = cd01 > 0f;
        slot.Seconds.Text = SkillBarLogic.FormatSeconds(ability.CooldownRemaining);
    }

    // —— 冒烟测试缝（只读） ——

    public int SlotCount => _slots.Count;

    public string GetKeyLabel(int index) => _slots[index].Key.Text;

    public string GetSecondsText(int index) => _slots[index].Seconds.Text;

    public bool IsCooling(int index) => _slots[index].Mask.Visible;

    public float GetCooldown01ForSmoke(int index) => _cooldown01Cache[index];
}
