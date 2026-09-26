# M8 实现计划：龙之谷式技能栏（底部居中 + 技能图标 + 冷却显示 + 数字键位）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 屏幕底部中心一个龙之谷风格的技能栏：每槽对应角色技能槽 skill_1..N（键盘左侧数字键 1/2/3），显示技能图标（占位）、冷却遮罩 + 剩余秒数、当前实际绑定键位；槽数随角色数据驱动（战士 3 / 刺客 2 / 弓手 1）。

**Architecture:** 纯表现层，沿用 Hud 既有的"只读轮询、不回写模拟"模式（规格第 1 节）：Player 新增只读 `Abilities` 暴露 → `SkillBar`（Hud 子节点，独立脚本）每帧轮询刷新。冷却显示拆两层：`SkillBarLogic` 纯逻辑（秒数格式化 / 冷却比例钳制，TDD 单测）+ 15 行 canvas_item 径向遮罩 shader（复用 M5 速度线的程序化思路）。槽位节点按角色技能数代码生成（同选人按钮/键位行的既有模式），换角色自动重建。

**Tech Stack:** Godot 4.6 + C#、GDShader(canvas_item)、xUnit、CSharpier；冒烟输入注入沿用 `Input.ActionPress/ActionRelease` + `StartPhase` 阶段隔离。

**前置**：M1~M7 已完成（88 单测 + 95 冒烟全绿，提交 `3d15639`）。键位底层（KeybindManager.CurrentKey / 重绑 UI）、技能框架（Ability.CooldownRemaining / Def）、HUD 骨架（Hud 轮询模式）全部就绪。

---

## 0. 范围决策（2026-09-26，默认按此执行）

| 主题 | 决策 |
|---|---|
| 技能栏位置 | 底部居中（龙之谷式），HBoxContainer，锚点 center-bottom |
| 槽数 | 跟随角色 `SkillIds` 数量（数据驱动），当前 1~3 槽 |
| 键位 | 槽位标签显示 KeybindManager 当前实际键（默认 1/2/3，重绑后跟随） |
| 图标 | **占位**：技能色块 + 显示名首字（无美术资产，同音频的占位策略，真资产同名替换思路） |
| 冷却表现 | 径向暗遮罩（shader）+ 居中秒数（≥10s 整数 / <10s 一位小数） |
| 血条迁移 | **本次不做**（PlayerBar 保持在左上；龙之谷式底部角色条记录为后续可选项） |
| 处决/闪避槽 | 不做（处决提示已有独立 HUD 元素；闪避非数字键） |

## 1. 文件结构

```text
Game/UI/SkillBar.cs                 新建：技能栏控件（槽位生成/轮询刷新/测试缝）
Game/UI/Shaders/cooldown_mask.gdshader  新建：径向冷却遮罩（~15 行）
Game/Gameplay/Combat/SkillBarLogic.cs   新建：纯逻辑（FormatSeconds/Cooldown01，TDD）
Tests/SkillBarTests.cs              新建：纯逻辑单测
Game/Gameplay/Combat/Player.cs      修改：+2 行只读暴露 Abilities
Game/Scenes/UI/Hud.tscn             修改：+SkillBar 节点（含 Row 容器）
Game/Gameplay/Debug/CombatSmokeTestRunner.cs  修改：+SkillBarPhase（~8 项检查）
```

改动面刻意收窄：不动 Player 状态机、不动 KeybindManager、不动输入映射。

## 2. 任务分解

### T1 纯逻辑：SkillBarLogic（TDD）

**Files:** Create `Game/Gameplay/Combat/SkillBarLogic.cs`、`Tests/SkillBarTests.cs`

- [ ] 写失败测试（完整代码）：

```csharp
using FirstPersonAction.Combat;
using Xunit;

namespace FirstPersonAction.Tests;

public class SkillBarLogicTests
{
    [Theory]
    [InlineData(0f, "")]         // 就绪不显示
    [InlineData(-0.5f, "")]      // 负数防御
    [InlineData(9.94f, "9.9")]   // <10s：一位小数
    [InlineData(4.0f, "4.0")]
    [InlineData(10f, "10")]      // ≥10s：整数（向上取整）
    [InlineData(12.3f, "13")]
    public void FormatSeconds_DisplaysCorrectText(float remaining, string expected)
    {
        Assert.Equal(expected, SkillBarLogic.FormatSeconds(remaining));
    }

    [Fact]
    public void Cooldown01_ClampsAndGuardsZeroTotal()
    {
        Assert.Equal(0f, SkillBarLogic.Cooldown01(3f, 0f));   // 除零守卫
        Assert.Equal(0.5f, SkillBarLogic.Cooldown01(3f, 6f));
        Assert.Equal(1f, SkillBarLogic.Cooldown01(9f, 6f));   // 上界钳制
        Assert.Equal(0f, SkillBarLogic.Cooldown01(-1f, 6f));  // 下界钳制
    }
}
```

- [ ] 跑 `dotnet test Tests/GameplayTests.csproj` 确认编译失败（类型不存在）。

- [ ] 最小实现（完整代码）：

```csharp
using System.Globalization;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 技能栏纯逻辑（M8）：秒数格式化与冷却比例。UI 只做展示，规则在这里可单测。
/// </summary>
public static class SkillBarLogic
{
    /// <summary>冷却剩余秒数文本：≤0 空串（就绪）；≥10s 向上取整为整数；否则一位小数。
    /// InvariantCulture：CI/Linux 上不产生逗号小数点。</summary>
    public static string FormatSeconds(float remaining) =>
        remaining <= 0f
            ? ""
            : remaining < 10f
                ? remaining.ToString("F1", CultureInfo.InvariantCulture)
                : Mathf.Ceil(remaining).ToString(CultureInfo.InvariantCulture);

    /// <summary>冷却进度 0（就绪）~1（刚施放）；total≤0 视为无 CD 恒就绪。</summary>
    public static float Cooldown01(float remaining, float total) =>
        total <= 0f ? 0f : Mathf.Clamp(remaining / total, 0f, 1f);
}
```

- [ ] 跑测试确认通过（88→94）。
- [ ] 提交：`feat(ui): T1 技能栏纯逻辑——秒数格式化+冷却比例（TDD 6用例）`

### T2 Player 只读暴露 Abilities

**Files:** Modify `Game/Gameplay/Combat/Player.cs`（`_abilities` 字段声明处，当前 :36）

- [ ] 加只读属性（表现层轮询用，不回写——规格第 1 节）：

```csharp
/// <summary>技能槽只读视图（SkillBar 轮询用）：下标 0..N-1 对应 skill_1..N。</summary>
public IReadOnlyList<Ability> Abilities => _abilities;
```

- [ ] `dotnet build` 0 警告 0 错误。
- [ ] 提交：`feat(ui): T2 Player 暴露技能槽只读视图`

### T3 SkillBar 场景与槽位生成（静态视觉）

**Files:** Create `Game/UI/SkillBar.cs`；Modify `Game/Scenes/UI/Hud.tscn`（ExecutionPrompt 之后追加）

- [ ] Hud.tscn 追加节点（完整块）：

```text
[node name="SkillBar" type="Control" parent="."]
anchors_preset = 7
anchor_left = 0.5
anchor_top = 1.0
anchor_right = 0.5
anchor_bottom = 1.0
offset_left = -141.0
offset_top = -116.0
offset_right = 141.0
offset_bottom = -36.0
mouse_filter = 2
script = ExtResource("3_skillbar")

[node name="Row" type="HBoxContainer" parent="SkillBar"]
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
mouse_filter = 2
alignment = 1
theme_override_constants/separation = 10
```

并在文件头 `load_steps` +1、新增 `[ext_resource type="Script" path="res://Game/UI/SkillBar.cs" id="3_skillbar"]`。锚点 7（center-bottom），槽区 282×80（3 槽 64px + 间距）居中贴底上浮 36px。

- [ ] SkillBar.cs 槽位生成 + 测试缝（完整代码，冷却接线在 T4）：

```csharp
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
        public ColorRect Mask = null!; // 径向冷却遮罩（T4 接 shader）
        public string ActionName = "";
    }

    private readonly List<Slot> _slots = new();
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
        if (_player == null)
        {
            return;
        }

        for (int i = 0; i < _player.Abilities.Count; i++)
        {
            _slots.Add(BuildSlot(_player.Abilities[i].Def, $"skill_{i + 1}"));
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

        var mask = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
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
        seconds.AddThemeColorOverride(
            "font_outline_color",
            new Color(0f, 0f, 0f)
        );
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
        panel.AddChild(key);

        _row.AddChild(panel);
        return new Slot
        {
            Panel = panel,
            Glyph = glyph,
            Key = key,
            Seconds = seconds,
            Mask = mask,
            ActionName = actionName,
        };
    }

    /// <summary>每帧刷新：键位标签（重绑跟随）+ 冷却显示（T4 实现）。</summary>
    private void UpdateSlot(int index, Ability ability)
    {
        Slot slot = _slots[index];
        Key? current = KeybindManager.Instance?.CurrentKey(slot.ActionName);
        slot.Key.Text = current?.ToString() ?? "-";

        float cd01 = SkillBarLogic.Cooldown01(ability.CooldownRemaining, ability.Def.Cooldown);
        UpdateCooldownVisual(slot, cd01, ability.CooldownRemaining);
    }

    private void UpdateCooldownVisual(Slot slot, float cd01, float remaining)
    {
        // T4 接线径向遮罩与秒数；T3 先占位保证编译
        slot.Mask.Visible = cd01 > 0f;
        slot.Seconds.Visible = cd01 > 0f;
        slot.Seconds.Text = SkillBarLogic.FormatSeconds(remaining);
    }

    // —— 冒烟测试缝（只读） ——

    public int SlotCount => _slots.Count;

    public string GetKeyLabel(int index) => _slots[index].Key.Text;

    public string GetSecondsText(int index) => _slots[index].Seconds.Text;

    public bool IsCooling(int index) => _slots[index].Mask.Visible;
}
```

- [ ] `godot --headless --path . --import` 生成 SkillBar.cs 的 .uid 并入库。
- [ ] 无头启动主场景一次，确认无脚本错误。
- [ ] 提交：`feat(ui): T3 技能栏槽位生成——底部居中布局+占位图标+键位标签`

### T4 冷却径向遮罩 shader

**Files:** Create `Game/UI/Shaders/cooldown_mask.gdshader`；Modify `Game/UI/SkillBar.cs`（UpdateCooldownVisual）

- [ ] shader（完整代码；暗罩自顶部顺时针消退，progress=剩余比例 1→0）：

```glsl
shader_type canvas_item;

// 径向冷却遮罩（M8）：progress = 剩余冷却比例（1=刚施放全暗 → 0=就绪全亮）
uniform float progress : hint_range(0.0, 1.0) = 0.0;

void fragment()
{
	vec2 centered = UV - vec2(0.5);
	// UV.y 向下：atan 0=右、+π/2=下（顺时针增）；+π/2 平移使 0=顶部
	float t = fract((atan(centered.y, centered.x) + PI * 0.5) / TAU);
	float masked = step(t, progress); // 顶部扇区先亮起来，暗罩顺时针收缩
	vec4 dim = vec4(0.0, 0.0, 0.0, 0.55);
	COLOR = mix(COLOR, dim, masked);
}
```

- [ ] SkillBar.cs：`Slot` 增加 `public ShaderMaterial MaskMaterial = null!;`；BuildSlot 中 mask 改为挂材质：

```csharp
var maskMaterial = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("res://Game/UI/Shaders/cooldown_mask.gdshader")! };
var mask = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false, Material = maskMaterial };
```

- [ ] UpdateCooldownVisual 写 progress：

```csharp
slot.Mask.Visible = cd01 > 0f;
slot.MaskMaterial.SetShaderParameter("progress", cd01);
slot.Seconds.Visible = cd01 > 0f;
slot.Seconds.Text = SkillBarLogic.FormatSeconds(remaining);
```

- [ ] `--import` 生成 .gdshader 的 .uid 入库。
- [ ] 手动冒烟一次（headless 跑完 95 项不回归）。
- [ ] 提交：`feat(ui): T4 冷却显示——径向遮罩shader+剩余秒数`

### T5 键位标签联动（已并入 T3/T4 的 UpdateSlot，本任务收口验证）

- [ ] 无头启动 → 程序化验证：Rebind skill_1→F 后标签变 F（此项由 T6 冒烟覆盖，T5 只做本地手验 + 无回归）。
- [ ] 提交（如无代码改动则并入 T6 提交）。

### T6 冒烟 SkillBarPhase

**Files:** Modify `Game/Gameplay/Debug/CombatSmokeTestRunner.cs`

- [ ] BuildPhases 追加 `("技能栏", SkillBarPhase)`（放在"音效管线"之后）。阶段完整代码：

```csharp
/// <summary>技能栏（M8，龙之谷式）：槽数随角色数据驱动；施放→冷却比例/秒数/遮罩；
/// 冷却结束回就绪；重绑后键位标签跟随。</summary>
private async Task SkillBarPhase()
{
    // 战士：3 槽 + 施放跳劈（CD 9s）进入冷却
    (Node main, Player warrior) = await StartPhase("res://Game/Config/Characters/Warrior.tres");
    var bar = main.GetNode<UI.SkillBar>("Hud/SkillBar");
    Check(bar.SlotCount == 3, "战士技能栏 3 槽（数据驱动）");
    Check(bar.GetKeyLabel(0) == "1", "槽位键位标签显示默认数字键 1");

    warrior.Rotation = Vector3.Zero;
    PressRelease("skill_1"); // 跳劈 CD 9s
    await Frames(5);
    Check(bar.IsCooling(0), "施放后槽位进入冷却（遮罩显示）");
    Check(bar.GetSecondsText(0) != "", $"冷却秒数显示（{bar.GetSecondsText(0)}s）");
    await Frames(60); // 累计 ~1.1s：9s CD 剩 ~7.9s → 比例 ~0.88
    Check(
        bar.GetCooldown01ForSmoke(0) > 0.5f && bar.GetCooldown01ForSmoke(0) < 1f,
        "冷却比例递减中"
    );

    // 重绑联动：skill_1 → F
    Core.KeybindManager.Instance!.Rebind("skill_1", Key.F);
    await Frames(3);
    Check(bar.GetKeyLabel(0) == "F", "重绑后键位标签跟随为 F");
    Core.KeybindManager.Instance!.ResetToDefaults();
    await EndPhase(main);

    // 刺客：2 槽 + 疾行（CD 4s）全程冷却恢复
    (Node main2, Player assassin) = await StartPhase("res://Game/Config/Characters/Assassin.tres");
    var bar2 = main2.GetNode<UI.SkillBar>("Hud/SkillBar");
    Check(bar2.SlotCount == 2, "刺客技能栏 2 槽");
    assassin.Rotation = Vector3.Zero;
    PressRelease("skill_1"); // 疾行 CD 4s
    await Frames(5);
    Check(bar2.IsCooling(0), "疾行施放进入冷却");
    await Frames(280); // 4.67s > 4s CD
    Check(!bar2.IsCooling(0) && bar2.GetSecondsText(0) == "", "冷却结束回到就绪（遮罩/秒数隐藏）");

    await EndPhase(main2);
}
```

- [ ] SkillBar 补一个冒烟缝（`_cooldown01` 缓存）：

```csharp
private readonly List<float> _cooldown01Cache = new(); // 冒烟断言用：最近一帧各槽冷却比例

public float GetCooldown01ForSmoke(int index) => _cooldown01Cache[index];
// UpdateSlot 中：缓存写 _cooldown01Cache（RebuildSlots 时 Clear 并补 0）
```

- [ ] 跑冒烟：预期 95→104 项全绿（+9）。
- [ ] 确认 `MinExecutedChecks = 40` 下限仍远低于执行数。
- [ ] 提交：`test(ui): T6 技能栏冒烟9项——槽数/冷却/递减/恢复/重绑联动`

### T7 收尾

- [ ] csharpier format + check。
- [ ] 全量回归门禁：`dotnet build`（0 警告 0 错误）、`dotnet test`（94/94）、无头冒烟（104 项，退出码 0）。
- [ ] 规格里程碑表（`Docs/specs/2026-09-09-player-combat-design.md` §7）追加 M8 行。
- [ ] 提交：`docs: M8 收尾——规格里程碑修订 + 计划执行记录`（按实际执行情况）。

## 3. 视觉规格（占位阶段）

- 槽位 64×64，圆角由 PanelContainer 默认主题承担（无主题资产）；间距 10px；整栏 282×80 底部居中、距底 36px。
- 图标底色 = 技能种类色（跳劈橙/冲锋蓝/旋风斩黄/后跳绿/疾行紫，`KindColors`），首字 28px 深色。
- 键位标签 12px 左上角；秒数 20px 白字黑描边居中；遮罩黑 55% 径向收缩。
- 真实图标资产到位后：`BuildSlot` 中 ColorRect+Glyph 换 TextureRect（`res://Game/Art/Skills/{kind}.png`，缺图回退占位）——本次不做。

## 4. 验收标准（对照龙之谷参考）

| 验收项 | 口径 |
|---|---|
| 底部居中技能栏 | 冒烟 SlotCount 断言 + 实机目检 |
| 槽位对应左侧数字键 | 默认标签 1/2/3；重绑后跟随（冒烟断言 F） |
| 技能图标 | 占位色块+首字（真资产替换路径已留） |
| 冷却时间显示 | 遮罩+秒数；施放即显示、递减、结束隐藏（冒烟三段断言） |
| 角色面板一致性 | 换角色槽数自动变化（战士3/刺客2/弓手1，冒烟覆盖 3与2） |

## 5. 回归口径与风险

- 单测 88 → 94（+6）；冒烟 95 → 104（+9）。
- 风险点：
  - `FormatSeconds` 文化差异 → 已用 InvariantCulture（T1 注释）；
  - 换角色重建时机 → 槽数变化触发（同帧 rebuild，冒烟换角色阶段覆盖）；
  - shader progress 参数名拼错 → 冒烟 IsCooling/秒数断言覆盖视觉状态线（shader 内部效果仍属"实机体感确认"项，与 M5 速度线同口径）；
  - Hud.tscn 手改锚点 → 计划给出完整节点块，load_steps 同步 +1。

## 6. 遗留（本次不做）

- 血条迁移至技能栏左侧（龙之谷式底部角色条）——待真图标/面板美术方向确定后一并做。
- 技能长按预览 tooltip、连段计数显示、处决提示并入技能栏——后续 UI 里程碑。
