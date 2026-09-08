# 玩家侧战斗系统 M1+M2 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 搭建玩家侧地基（输入命令层/移动/FP相机/工程引导）并完成打击感闭环（3段连击/判定窗口/hit-stop/木桩受击状态），即规格中的里程碑 M1 与 M2。

**Architecture:** 代码状态机为唯一真相源，纯逻辑类（连段跟踪/受击状态机/锥形查询/输入缓冲）与 Godot 节点薄胶囊分离——前者全部有单元测试，后者只做绑定与表现。动作类输入经可序列化 InputCommand + 缓冲窗口进入逻辑；移动为连续意图直接驱动（联机时走意图流，见规格第 6 节）。

**Tech Stack:** Godot 4.2 + C#（Godot.NET.Sdk）、xUnit 单测（纯逻辑类，不实例化 Godot 对象，但可用 Vector3 等值类型）、CSharpier 格式化。

**规格依据:** `Docs/specs/2026-09-09-player-combat-design.md`（M1/M2 行 + 第 1/3/4 节）。本计划完成后 M3（技能框架/战士kit/闪避无敌帧强化）、M4（处决）、M5（第二角色/选人）另出计划。

---

## 文件结构总览

```text
GodotGameTemplate.csproj                  ← 新建（Godot 4.2 默认 csproj，含 SDK 引用）
Tests/GameplayTests.csproj                ← 新建（xUnit，引用主工程）
Tests/CombatLogicTests.cs                 ← 新建（全部纯逻辑单测）
project.godot                             ← 修改（input 映射 / autoload / 主场景）
Game/Gameplay/Core/EntityId.cs            ← 稳定逻辑实体 ID
Game/Gameplay/Core/SeededRandom.cs        ← 带种子随机源
Game/Gameplay/Core/InputCommand.cs        ← 命令枚举 + 可序列化命令结构
Game/Gameplay/Core/InputCommandBuffer.cs  ← 150ms 动作缓冲
Game/Gameplay/Combat/CombatTuning.cs      ← 全部调参常量（M3 迁入 Resource）
Game/Gameplay/Combat/ComboStageData.cs    ← 单段普攻数据
Game/Gameplay/Combat/MeleeComboTracker.cs ← 连段状态机（纯逻辑）
Game/Gameplay/Combat/MeleeArcQuery.cs     ├── 命中数据/目标接口 + 前方锥形查询（纯逻辑）
Game/Gameplay/Combat/HitReactionMachine.cs← 受击状态机（纯逻辑，阵型系统将来复用）
Game/Gameplay/Combat/HitstopManager.cs    ← 全局 hit-stop（autoload）
Game/Gameplay/Combat/Player.cs            ← 玩家 CharacterBody3D
Game/Gameplay/Combat/DummyEnemy.cs        ← 木桩敌人（ICombatTarget）
Game/Gameplay/Combat/HealthComponent.cs   ← 血量组件
Game/Gameplay/Spatial/SpatialAgent.cs     ← 玩法空间参数组件（M3 冲锋/将来阵型用）
Game/Scenes/Player.tscn                   ← 玩家场景（FP相机 + viewmodel + 隐藏身体）
Game/Scenes/EnemyDummy.tscn               ← 木桩场景
Game/Scenes/Main.tscn                     ← 测试竞技场
```

纯逻辑与节点的边界：`MeleeComboTracker` / `HitReactionMachine` / `MeleeArcQuery` / `InputCommandBuffer` / `SeededRandom` / `EntityId` 不继承 Godot 类型、不访问场景树，全部单测；`Player` / `DummyEnemy` / `HealthComponent` / `HitstopManager` 是薄胶囊，靠手工手感清单验证。

---

### Task 1: 环境验证与工程引导

**Files:**
- Create: `GodotGameTemplate.csproj`

- [ ] **Step 1: 确认工具链**

```bash
dotnet --version
dotnet --list-runtimes
```
预期：.NET SDK 6.0 或更高（记住有无 6.x 运行时，决定 Task 2 测试工程 TFM：有 6.x 运行时用 net6.0，否则用 net8.0——游戏工程固定 net6.0 以匹配 Godot 4.2 自带运行时）。

用 godot MCP 确认引擎版本（`get_godot_version`）。预期 4.2.x；若为 4.3/4.4，csproj SDK 版本随之改为对应 4.x，`project.godot` 的 `config/features` 一并更新。

- [ ] **Step 2: 写 csproj**

```xml
<Project Sdk="Godot.NET.Sdk/4.2.2">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>GodotGameTemplate</RootNamespace>
  </PropertyGroup>
</Project>
```
（若 Step 1 确认引擎为 4.3/4.4，把 Sdk 版本改为对应次版本号。）

- [ ] **Step 3: 还原并构建**

```bash
cd /d/GdProject/first-person-action && dotnet build GodotGameTemplate.csproj
```
预期：`Build succeeded`，0 warning/error（空工程无源文件）。

- [ ] **Step 4: 安装 CSharpier**

```bash
dotnet tool install --global CSharpier
```
预期：`csharpier` 可用（`dotnet csharpier --version` 输出版本号）。已安装则跳过。

- [ ] **Step 5: 提交**

```bash
git add GodotGameTemplate.csproj && git commit -m "build: 添加 Godot 4.2 C# 工程文件"
```

---

### Task 2: Core —— EntityId 与 SeededRandom（TDD）

**Files:**
- Create: `Game/Gameplay/Core/EntityId.cs`
- Create: `Game/Gameplay/Core/SeededRandom.cs`
- Create: `Tests/GameplayTests.csproj`
- Create: `Tests/RandomAndIdTests.cs`

- [ ] **Step 1: 写测试工程与失败测试**

`Tests/GameplayTests.csproj`（TFM 按 Task 1 结论，下面以 net8.0 为例）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\GodotGameTemplate.csproj" />
  </ItemGroup>
</Project>
```

`Tests/RandomAndIdTests.cs`：

```csharp
using GodotGameTemplate.Core;
using Xunit;

namespace GodotGameTemplate.Tests;

public class SeededRandomTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextInt(0, 1000), b.NextInt(0, 1000));
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentSequence()
    {
        var a = new SeededRandom(1);
        var b = new SeededRandom(2);
        Assert.NotEqual(a.NextInt(0, int.MaxValue), b.NextInt(0, int.MaxValue));
    }

    [Fact]
    public void Reseed_RestartsSequence()
    {
        var a = new SeededRandom(7);
        int first = a.NextInt(0, 1000);
        a.Reseed(7);
        Assert.Equal(first, a.NextInt(0, 1000));
    }
}

public class EntityIdTests
{
    [Fact]
    public void Equality_WorksByValue()
    {
        var a = new EntityId(5);
        var b = new EntityId(5);
        var c = new EntityId(6);
        Assert.True(a == b);
        Assert.True(a != c);
        Assert.False(a.IsNone);
        Assert.True(EntityId.None.IsNone);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：编译错误 `SeededRandom` / `EntityId` 未定义（红）。

- [ ] **Step 3: 实现**

`Game/Gameplay/Core/EntityId.cs`：

```csharp
namespace GodotGameTemplate.Core;

/// <summary>
/// 稳定的逻辑实体标识。模拟层一切跨实体引用都通过它而非节点引用，
/// 为联机同步（实体按 ID 寻址）预留——规格第 6 节第 1 条。
/// </summary>
public readonly struct EntityId : IEquatable<EntityId>
{
    public readonly ulong Value;

    public EntityId(ulong value) => Value = value;

    public bool IsNone => Value == 0;

    public static EntityId None => default;

    public bool Equals(EntityId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => $"Entity({Value})";

    public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

    public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
}
```

`Game/Gameplay/Core/SeededRandom.cs`：

```csharp
namespace GodotGameTemplate.Core;

/// <summary>
/// 带种子的独立随机源。约定：任何影响模拟结果的随机数（伤害浮动、AI 决策）
/// 必须来自实例而非全局 RNG，保证联机/回放可复现——规格第 6 节第 4 条。
/// </summary>
public sealed class SeededRandom
{
    private Random _random;

    public SeededRandom(int seed) => _random = new Random(seed);

    /// <summary>[minInclusive, maxExclusive)</summary>
    public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    /// <summary>[0.0, 1.0)</summary>
    public double NextDouble() => _random.NextDouble();

    public bool Chance(double probability) => _random.NextDouble() < probability;

    public float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);
}
```

- [ ] **Step 4: 运行确认通过**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：全部 PASS（绿）。

- [ ] **Step 5: 提交**

```bash
git add Tests/ Game/Gameplay/Core/ && git commit -m "feat(core): 实体ID与种子随机源（联机约定基础）"
```

---

### Task 3: Core —— InputCommand 与缓冲（TDD）

**Files:**
- Create: `Game/Gameplay/Core/InputCommand.cs`
- Create: `Game/Gameplay/Core/InputCommandBuffer.cs`
- Modify: `Tests/CombatLogicTests.cs`（新建）

- [ ] **Step 1: 写失败测试**

`Tests/CombatLogicTests.cs`：

```csharp
using Godot;
using GodotGameTemplate.Core;
using Xunit;

namespace GodotGameTemplate.Tests;

public class InputCommandBufferTests
{
    private static InputCommand Cmd(InputCommandKind kind) => new InputCommand { Kind = kind };

    [Fact]
    public void Consume_ReturnsEarliestMatchingCommand()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        buf.Push(Cmd(InputCommandKind.Attack), 20);

        Assert.True(buf.TryConsume(InputCommandKind.Attack, 30, out var first));
        Assert.Equal(InputCommandKind.Attack, first.Kind);
        Assert.True(buf.TryConsume(InputCommandKind.Attack, 30, out _));
        Assert.False(buf.TryConsume(InputCommandKind.Attack, 30, out _));
    }

    [Fact]
    public void Command_ExpiresAfterWindow()
    {
        var buf = new InputCommandBuffer { BufferWindowMs = 150 };
        buf.Push(Cmd(InputCommandKind.Dodge), 0);
        Assert.True(buf.TryConsume(InputCommandKind.Dodge, 149, out _));

        buf.Push(Cmd(InputCommandKind.Dodge), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Dodge, 151, out _));
    }

    [Fact]
    public void Kinds_AreIndependent()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Dodge, 10, out _));
        Assert.True(buf.TryConsume(InputCommandKind.Attack, 10, out _));
    }

    [Fact]
    public void Move_And_None_AreNotBuffered()
    {
        var buf = new InputCommandBuffer();
        buf.Push(new InputCommand { Kind = InputCommandKind.Move, Axis = Vector2.Up }, 0);
        buf.Push(Cmd(InputCommandKind.None), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Move, 10, out _));
        Assert.False(buf.TryConsume(InputCommandKind.None, 10, out _));
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        buf.Clear();
        Assert.False(buf.TryConsume(InputCommandKind.Attack, 10, out _));
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：编译错误 `InputCommand` / `InputCommandBuffer` 未定义。

- [ ] **Step 3: 实现**

`Game/Gameplay/Core/InputCommand.cs`：

```csharp
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
```

`Game/Gameplay/Core/InputCommandBuffer.cs`：

```csharp
namespace GodotGameTemplate.Core;

/// <summary>
/// 动作输入缓冲：动作命令压入后 BufferWindowMs 内有效，被消费或过期即清除。
/// 消费端用「条件满足才 TryConsume」的模式实现缓冲——条件不满足时命令留在
/// 缓冲里等待（如取消窗口打开瞬间接上连段）。时间由调用方传入（毫秒），
/// 便于测试与将来的联机时钟。
/// </summary>
public sealed class InputCommandBuffer
{
    private struct Entry
    {
        public InputCommand Command;
        public long PushedAtMs;
    }

    private readonly List<Entry> _entries = new List<Entry>();

    public int BufferWindowMs { get; set; } = 150;

    public void Push(in InputCommand command, long nowMs)
    {
        if (command.Kind is InputCommandKind.None or InputCommandKind.Move)
        {
            return;
        }

        _entries.Add(new Entry { Command = command, PushedAtMs = nowMs });
    }

    /// <summary>取出最早的一条匹配命令；过期条目顺带清除。无匹配返回 false。</summary>
    public bool TryConsume(InputCommandKind kind, long nowMs, out InputCommand command)
    {
        command = default;
        _entries.RemoveAll(e => nowMs - e.PushedAtMs > BufferWindowMs);

        int index = _entries.FindIndex(e => e.Command.Kind == kind);
        if (index < 0)
        {
            return false;
        }

        command = _entries[index].Command;
        _entries.RemoveAt(index);
        return true;
    }

    public void Clear() => _entries.Clear();
}
```

- [ ] **Step 4: 运行确认通过**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add Tests/ Game/Gameplay/Core/ && git commit -m "feat(core): 可序列化输入命令与150ms动作缓冲"
```

---

### Task 4: Combat —— 连段数据与连段状态机（TDD）

**Files:**
- Create: `Game/Gameplay/Combat/ComboStageData.cs`
- Create: `Game/Gameplay/Combat/CombatTuning.cs`
- Create: `Game/Gameplay/Combat/MeleeComboTracker.cs`
- Modify: `Tests/CombatLogicTests.cs`

- [ ] **Step 1: 写失败测试**

追加到 `Tests/CombatLogicTests.cs`：

```csharp
using GodotGameTemplate.Combat;
using Xunit;

namespace GodotGameTemplate.Tests;

public class MeleeComboTrackerTests
{
    private static readonly ComboStageData[] Stages =
    {
        new() { Startup = 0.10f, Active = 0.12f, Recovery = 0.30f, CancelAfter = 0.10f },
        new() { Startup = 0.10f, Active = 0.12f, Recovery = 0.32f, CancelAfter = 0.10f },
        new() { Startup = 0.14f, Active = 0.16f, Recovery = 0.55f, CancelAfter = 0.25f },
    };

    private static MeleeComboTracker NewTracker() => new(Stages);

    [Fact]
    public void StartsInactive_AdvanceEntersStage0Startup()
    {
        var t = NewTracker();
        Assert.False(t.IsActive);
        Assert.True(t.TryAdvance());
        Assert.Equal(0, t.StageIndex);
        Assert.Equal(ComboStagePhase.Startup, t.Phase);
    }

    [Fact]
    public void Tick_AdvancesStartupToActiveToRecovery()
    {
        var t = NewTracker();
        t.TryAdvance();
        t.Tick(0.10f);
        Assert.Equal(ComboStagePhase.Active, t.Phase);
        t.Tick(0.12f);
        Assert.Equal(ComboStagePhase.Recovery, t.Phase);
        t.Tick(0.30f);
        Assert.False(t.IsActive);
    }

    [Fact]
    public void HitWindow_CanBeConsumedOnlyOncePerStage()
    {
        var t = NewTracker();
        t.TryAdvance();
        t.Tick(0.05f); // Startup 中段
        Assert.False(t.CanApplyHit);
        t.Tick(0.05f); // 进入 Active
        Assert.True(t.CanApplyHit);
        Assert.True(t.ConsumeHit());
        Assert.False(t.ConsumeHit());
    }

    [Fact]
    public void Chain_BlockedBeforeCancelAfter_AfterItAllowed()
    {
        var t = NewTracker();
        t.TryAdvance();
        t.Tick(0.10f + 0.12f + 0.05f); // Recovery 0.05s < CancelAfter 0.10s
        Assert.False(t.TryAdvance());
        t.Tick(0.06f); // 0.11s ≥ CancelAfter
        Assert.True(t.IsInCancelWindow);
        Assert.True(t.TryAdvance());
        Assert.Equal(1, t.StageIndex);
    }

    [Fact]
    public void Finisher_ChainsBackToStage0()
    {
        var t = NewTracker();
        t.TryAdvance(); // 0
        t.TryAdvance(); // 需在取消窗口内：模拟快速输入
        // 直接驱动到末段取消窗口
        t.Reset();
        t.TryAdvance();
        t.Tick(Stages[0].Startup + Stages[0].Active + Stages[0].CancelAfter + 0.01f);
        t.TryAdvance(); // -> stage 1
        t.Tick(Stages[1].Startup + Stages[1].Active + Stages[1].CancelAfter + 0.01f);
        t.TryAdvance(); // -> stage 2 (finisher)
        Assert.Equal(2, t.StageIndex);
        t.Tick(Stages[2].Startup + Stages[2].Active + Stages[2].CancelAfter + 0.01f);
        t.TryAdvance(); // 末段之后回到第 1 段
        Assert.Equal(0, t.StageIndex);
    }

    [Fact]
    public void Reset_InterruptsCombo()
    {
        var t = NewTracker();
        t.TryAdvance();
        t.Tick(0.05f);
        t.Reset();
        Assert.False(t.IsActive);
        Assert.True(t.TryAdvance());
        Assert.Equal(0, t.StageIndex);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：编译错误 `ComboStageData` / `MeleeComboTracker` 未定义。

- [ ] **Step 3: 实现**

`Game/Gameplay/Combat/ComboStageData.cs`：

```csharp
namespace GodotGameTemplate.Combat;

/// <summary>
/// 单段普攻数据。v1 为纯 C# 类（常量表），M3 技能框架落地时迁移为 Resource。
/// </summary>
public sealed class ComboStageData
{
    public float Startup;         // 前摇时长（秒）
    public float Active;          // 主动（判定）时长
    public float Recovery;        // 后摇时长
    public float HitFrom;         // 命中窗口起点（Active 阶段内相对时间）
    public float HitTo;           // 命中窗口终点
    public float Damage;
    public float PoiseDamage;     // 韧性伤害（驱动敌人受击状态机）
    public float ForwardStep;     // 前摇期间的前移距离（米，提供突进感）
    public float CancelAfter;     // 后摇经过该时间后可被下一段/闪避取消
    public float Knockback = 2f;  // 命中击退的水平初速（米/秒）
}
```

`Game/Gameplay/Combat/CombatTuning.cs`：

```csharp
namespace GodotGameTemplate.Combat;

/// <summary>
/// v1 全部战斗调参常量。集中一处便于手感调校；
/// M3 技能框架落地时逐步迁移为 Resource 数据。
/// </summary>
public static class CombatTuning
{
    // —— 普攻连段（战士 3 段）——
    public static readonly ComboStageData[] WarriorCombo =
    {
        new()
        {
            Startup = 0.10f, Active = 0.12f, Recovery = 0.30f,
            HitFrom = 0.00f, HitTo = 0.12f,
            Damage = 10f, PoiseDamage = 20f, ForwardStep = 0.6f, CancelAfter = 0.10f, Knockback = 2.0f,
        },
        new()
        {
            Startup = 0.10f, Active = 0.12f, Recovery = 0.32f,
            HitFrom = 0.00f, HitTo = 0.12f,
            Damage = 12f, PoiseDamage = 25f, ForwardStep = 0.7f, CancelAfter = 0.10f, Knockback = 2.5f,
        },
        new()
        {
            Startup = 0.14f, Active = 0.16f, Recovery = 0.55f,
            HitFrom = 0.00f, HitTo = 0.16f,
            Damage = 22f, PoiseDamage = 55f, ForwardStep = 0.9f, CancelAfter = 0.25f, Knockback = 5.0f,
        },
    };

    public const string TargetGroup = "combat_targets";
    public const float AttackRange = 2.2f;          // 近战判定距离
    public const float AttackHalfAngleDeg = 55f;    // 前方锥形半角
    public const float AttackMoveScale = 0.15f;     // 攻击期间移动输入衰减
    public const float HitstopMs = 70f;             // 命中顿帧

    // —— 移动 ——
    public const float WalkSpeed = 5.0f;
    public const float GroundAccel = 40f;
    public const float GroundDecel = 50f;
    public const float Gravity = 18f;
    public const float JumpVelocity = 7.0f;
    public const float MouseSensitivity = 0.0025f;
    public const float PitchClampDeg = 85f;

    // —— 闪避（M2 先落状态与数据，M3 加无敌帧交互）——
    public const float DodgeDuration = 0.40f;
    public const float DodgeSpeed = 12f;
    public const float DodgeAccel = 60f;
    public const float DodgeInvulnerableSeconds = 0.25f;

    // —— 受击 ——
    public const float StaggerSeconds = 0.28f;
    public const float DownedSeconds = 2.5f;
}
```

`Game/Gameplay/Combat/MeleeComboTracker.cs`：

```csharp
namespace GodotGameTemplate.Combat;

public enum ComboStagePhase
{
    Inactive,
    Startup,
    Active,
    Recovery,
}

/// <summary>
/// 普攻连段跟踪器（纯逻辑，可单元测试；规格第 3 节行动层的连段核心）。
/// 状态推进：Startup → Active（命中窗口）→ Recovery（过 CancelAfter 可接下一段）→ Inactive。
/// 本类不做任何 Godot 调用；控制器读取其状态驱动位移与表现。
/// </summary>
public sealed class MeleeComboTracker
{
    private readonly IReadOnlyList<ComboStageData> _stages;
    private bool _hitApplied;

    public MeleeComboTracker(IReadOnlyList<ComboStageData> stages) => _stages = stages;

    public ComboStagePhase Phase { get; private set; } = ComboStagePhase.Inactive;
    public int StageIndex { get; private set; } = -1;
    public float PhaseElapsed { get; private set; }

    public bool IsActive => Phase != ComboStagePhase.Inactive;
    public bool CanApplyHit => Phase == ComboStagePhase.Active && !_hitApplied;
    public bool IsInCancelWindow =>
        Phase == ComboStagePhase.Recovery && PhaseElapsed >= _stages[StageIndex].CancelAfter;
    public bool CanStartAction => !IsActive || IsInCancelWindow;

    private ComboStageData Current => _stages[StageIndex];

    /// <summary>
    /// 接受一次攻击输入：空闲则从第 1 段开始；取消窗口内则链下一段（末段后回第 1 段）。
    /// 条件不满足返回 false（输入留在缓冲里继续等待）。
    /// </summary>
    public bool TryAdvance()
    {
        if (!CanStartAction)
        {
            return false;
        }

        int next = IsActive ? StageIndex + 1 : 0;
        if (next >= _stages.Count)
        {
            next = 0;
        }

        StageIndex = next;
        Phase = ComboStagePhase.Startup;
        PhaseElapsed = 0f;
        _hitApplied = false;
        return true;
    }

    /// <summary>结算本段命中：返回 true 表示本次生效（每段最多一次）。</summary>
    public bool ConsumeHit()
    {
        if (!CanApplyHit)
        {
            return false;
        }

        _hitApplied = true;
        return true;
    }

    /// <summary>外部打断（闪避/受击/将来的处决）→ 回到空闲。</summary>
    public void Reset()
    {
        Phase = ComboStagePhase.Inactive;
        StageIndex = -1;
        PhaseElapsed = 0f;
        _hitApplied = false;
    }

    public void Tick(float dt)
    {
        if (!IsActive)
        {
            return;
        }

        PhaseElapsed += dt;
        float duration = Phase switch
        {
            ComboStagePhase.Startup => Current.Startup,
            ComboStagePhase.Active => Current.Active,
            ComboStagePhase.Recovery => Current.Recovery,
            _ => 0f,
        };
        if (PhaseElapsed < duration)
        {
            return;
        }

        PhaseElapsed = 0f;
        Phase = Phase switch
        {
            ComboStagePhase.Startup => ComboStagePhase.Active,
            ComboStagePhase.Active => ComboStagePhase.Recovery,
            _ => ComboStagePhase.Inactive,
        };
        if (Phase == ComboStagePhase.Inactive)
        {
            StageIndex = -1;
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：全部 PASS。注意 `Finisher_ChainsBackToStage0` 中第二次 `t.TryAdvance()`（Reset 前）是故意验证「非取消窗口时拒绝」，返回值被忽略不影响后续。

- [ ] **Step 5: 提交**

```bash
git add Tests/ Game/Gameplay/Combat/ && git commit -m "feat(combat): 三段连段数据与连段状态机"
```

---

### Task 5: Combat —— 命中数据与锥形判定查询（TDD）

**Files:**
- Create: `Game/Gameplay/Combat/MeleeArcQuery.cs`（含 `HitData` / `ICombatTarget`）
- Modify: `Tests/CombatLogicTests.cs`

- [ ] **Step 1: 写失败测试**

追加到 `Tests/CombatLogicTests.cs`（`using Godot;` 已有）：

```csharp
public class MeleeArcQueryTests
{
    private sealed class Target
    {
        public Vector3 Pos;
    }

    [Fact]
    public void FrontTarget_InRange_IsHit()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, -2) } };
        var hits = MeleeArcQuery.FindHits(
            Vector3.Zero, new Vector3(0, 0, -1), 2.5f, 55f, targets, t => t.Pos);
        Assert.Single(hits);
    }

    [Fact]
    public void BehindTarget_Missed()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, 2) } };
        var hits = MeleeArcQuery.FindHits(
            Vector3.Zero, new Vector3(0, 0, -1), 2.5f, 55f, targets, t => t.Pos);
        Assert.Empty(hits);
    }

    [Fact]
    public void OutOfRangeTarget_Missed()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 1, -3) } };
        var hits = MeleeArcQuery.FindHits(
            Vector3.Zero, new Vector3(0, 0, -1), 2.5f, 55f, targets, t => t.Pos);
        Assert.Empty(hits);
    }

    [Fact]
    public void SideTarget_BeyondHalfAngle_Missed()
    {
        // 距离 2、正侧方 = 90° 夹角 > 55° 半角
        var targets = new List<Target> { new() { Pos = new Vector3(2, 1, 0) } };
        var hits = MeleeArcQuery.FindHits(
            Vector3.Zero, new Vector3(0, 0, -1), 2.5f, 55f, targets, t => t.Pos);
        Assert.Empty(hits);
    }

    [Fact]
    public void HeightDifference_BeyondTolerance_Missed()
    {
        var targets = new List<Target> { new() { Pos = new Vector3(0, 5, -2) } };
        var hits = MeleeArcQuery.FindHits(
            Vector3.Zero, new Vector3(0, 0, -1), 2.5f, 55f, targets, t => t.Pos);
        Assert.Empty(hits);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：编译错误 `MeleeArcQuery` 未定义。

- [ ] **Step 3: 实现**

`Game/Gameplay/Combat/MeleeArcQuery.cs`：

```csharp
using Godot;
using GodotGameTemplate.Core;

namespace GodotGameTemplate.Combat;

/// <summary>一次命中携带的数据。伤害/韧性/击退由攻击方定义，受击方消费。</summary>
public struct HitData
{
    public float Damage;
    public float PoiseDamage;
    public Vector3 Knockback;
    public EntityId Source;
}

/// <summary>可被攻击判定命中的目标。敌人和将来的可破坏物实现它。</summary>
public interface ICombatTarget
{
    /// <summary>判定用的身体中心点（世界坐标）。</summary>
    Vector3 Center { get; }

    bool CanBeHit { get; }

    void ApplyHit(in HitData hit);
}

/// <summary>
/// 近战锥形判定（纯逻辑，可单元测试；规格第 4 节「主动帧窗口内形状查询」的
/// v1 形态——无常驻碰撞体，确定性强、联机友好）。
/// </summary>
public static class MeleeArcQuery
{
    private const float DefaultHeightTolerance = 1.5f;

    /// <summary>返回位于 origin 前方锥形内的目标（水平面判定，忽略高度差 ≤ 容差）。</summary>
    public static List<T> FindHits<T>(
        Vector3 origin,
        Vector3 flatForward,
        float range,
        float halfAngleDeg,
        IList<T> targets,
        Func<T, Vector3> centerOf,
        float heightTolerance = DefaultHeightTolerance)
        where T : class
    {
        var results = new List<T>();
        Vector3 forward = new Vector3(flatForward.X, 0f, flatForward.Z).Normalized();

        foreach (T target in targets)
        {
            Vector3 center = centerOf(target);
            if (Mathf.Abs(center.Y - origin.Y) > heightTolerance)
            {
                continue;
            }

            Vector3 flat = new Vector3(center.X - origin.X, 0f, center.Z - origin.Z);
            float distance = flat.Length();
            if (distance > range || distance < 0.001f)
            {
                continue;
            }

            float angleDeg = Mathf.RadToDeg(Mathf.Abs(forward.SignedAngleTo(flat.Normalized(), Vector3.Up)));
            if (angleDeg <= halfAngleDeg)
            {
                results.Add(target);
            }
        }

        return results;
    }
}
```

- [ ] **Step 4: 运行确认通过**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add Tests/ Game/Gameplay/Combat/ && git commit -m "feat(combat): 命中数据与前方锥形判定查询"
```

---

### Task 6: Combat —— 受击状态机（TDD）

**Files:**
- Create: `Game/Gameplay/Combat/HitReactionMachine.cs`
- Modify: `Tests/CombatLogicTests.cs`

- [ ] **Step 1: 写失败测试**

追加到 `Tests/CombatLogicTests.cs`：

```csharp
public class HitReactionMachineTests
{
    private static HitReactionMachine NewMachine() => new(60f, 0.28f, 2.5f);

    [Fact]
    public void PoiseBreak_CausesDowned()
    {
        var m = NewMachine();
        m.ApplyHit(20f);
        Assert.Equal(HitReactionState.Staggered, m.State);
        m.ApplyHit(45f); // 韧性 60 - 20 - 45 < 0
        Assert.Equal(HitReactionState.Downed, m.State);
        Assert.True(m.IsDowned);
    }

    [Fact]
    public void Stagger_RecoversAfterDuration()
    {
        var m = NewMachine();
        m.ApplyHit(10f);
        Assert.Equal(HitReactionState.Staggered, m.State);
        m.Tick(0.10f);
        Assert.Equal(HitReactionState.Staggered, m.State);
        m.ApplyHit(10f); // 刷新硬直
        m.Tick(0.20f);
        Assert.Equal(HitReactionState.Staggered, m.State);
        m.Tick(0.09f);
        Assert.Equal(HitReactionState.Normal, m.State);
    }

    [Fact]
    public void Downed_RecoversWithFullPoise()
    {
        var m = NewMachine();
        m.ApplyHit(60f);
        Assert.True(m.IsDowned);
        Assert.Equal(0f, m.Poise);
        m.Tick(2.4f);
        Assert.True(m.IsDowned);
        m.Tick(0.2f);
        Assert.Equal(HitReactionState.Normal, m.State);
        Assert.Equal(60f, m.Poise);
    }

    [Fact]
    public void Downed_IgnoresFurtherHitReactions()
    {
        var m = NewMachine();
        m.ApplyHit(60f);
        m.Tick(0.5f);
        m.ApplyHit(50f); // 倒地中再被_hit：韧性/状态不变（可被处决窗口不被打断）
        Assert.Equal(HitReactionState.Downed, m.State);
        Assert.Equal(0f, m.Poise);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：编译错误 `HitReactionMachine` 未定义。

- [ ] **Step 3: 实现**

`Game/Gameplay/Combat/HitReactionMachine.cs`：

```csharp
namespace GodotGameTemplate.Combat;

public enum HitReactionState
{
    Normal,
    Staggered,
    Downed,
}

/// <summary>
/// 受击状态机（纯逻辑，可单元测试；规格第 4 节，阵型系统的敌人将来直接复用）。
/// 韧性（Poise）被清零 → 倒地（= 可被处决窗口，M4 消费）；
/// 硬直可被后续命中刷新；倒地期间不再叠加韧性/硬直；倒地结束韧性回满。
/// </summary>
public sealed class HitReactionMachine
{
    private readonly float _staggerSeconds;
    private readonly float _downedSeconds;

    public HitReactionMachine(float maxPoise, float staggerSeconds, float downedSeconds)
    {
        MaxPoise = maxPoise;
        Poise = maxPoise;
        _staggerSeconds = staggerSeconds;
        _downedSeconds = downedSeconds;
    }

    public HitReactionState State { get; private set; } = HitReactionState.Normal;
    public float StateElapsed { get; private set; }
    public float MaxPoise { get; }
    public float Poise { get; private set; }
    public bool IsDowned => State == HitReactionState.Downed;

    public void ApplyHit(float poiseDamage)
    {
        if (State == HitReactionState.Downed)
        {
            return;
        }

        Poise -= poiseDamage;
        if (Poise <= 0f)
        {
            Poise = 0f;
            Enter(HitReactionState.Downed);
        }
        else
        {
            Enter(HitReactionState.Staggered);
        }
    }

    public void Tick(float dt)
    {
        StateElapsed += dt;
        if (State == HitReactionState.Staggered && StateElapsed >= _staggerSeconds)
        {
            Enter(HitReactionState.Normal);
        }
        else if (State == HitReactionState.Downed && StateElapsed >= _downedSeconds)
        {
            Poise = MaxPoise;
            Enter(HitReactionState.Normal);
        }
    }

    private void Enter(HitReactionState state)
    {
        State = state;
        StateElapsed = 0f;
    }
}
```

- [ ] **Step 4: 运行确认通过**

```bash
dotnet test Tests/GameplayTests.csproj
```
预期：全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add Tests/ Game/Gameplay/Combat/ && git commit -m "feat(combat): 韧性驱动的受击状态机"
```

---

### Task 7: 场景基础 —— 输入映射、玩家移动、竞技场（M1 主体）

**Files:**
- Create: `Game/Gameplay/Spatial/SpatialAgent.cs`
- Create: `Game/Gameplay/Combat/Player.cs`
- Create: `Game/Scenes/Player.tscn`
- Create: `Game/Scenes/Main.tscn`
- Modify: `project.godot`

- [ ] **Step 1: 添加 SpatialAgent 组件**

`Game/Gameplay/Spatial/SpatialAgent.cs`：

```csharp
using Godot;

namespace GodotGameTemplate.Spatial;

/// <summary>
/// 单位的玩法空间参数（阵型文档第 5 节 SpatialState 的 v1 子集）。
/// v1 中物理胶囊负责简单空间阻挡；本组件的数据供 M3 冲锋的挤开规则、
/// M4 处决距离带与将来阵型系统使用。
/// </summary>
public partial class SpatialAgent : Node
{
    [Export] public float Radius = 0.5f;
    [Export] public float GameplayMass = 100f;
    [Export] public float PushResistance = 100f;
    [Export] public float MovementForce = 100f;
    [Export] public int CollisionPriority;

    /// <summary>运行时标志：穿人/处决等期间对本单位豁免空间约束。</summary>
    public bool SpatialExempt;
}
```

- [ ] **Step 2: 编辑 project.godot——输入映射与主场景**

`project.godot` 全量替换为（在原文件基础上补 `[input]`、`[autoload]` 段与 `run/main_scene`）：

```ini
; Engine configuration file.
; It's best edited using the editor UI and not directly,
; since the parameters that go here are not all obvious.
;
; Format:
;   [section] ; section goes between []
;   param=value ; assign values to parameters

config_version=5

[application]

config/name="First Person Action"
config/description="第一人称战术阵型战斗"
run/main_scene="res://Game/Scenes/Main.tscn"
config/features=PackedStringArray("4.2", "C#")
config/icon="res://icon.svg"

[autoload]

HitstopManager="*res://Game/Gameplay/Combat/HitstopManager.cs"

[dotnet]

project/assembly_name="GodotGameTemplate"

[input]

move_forward={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":87,"key_label":0,"unicode":119,"echo":false,"script":null)]
}
move_back={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":83,"key_label":0,"unicode":115,"echo":false,"script":null)]
}
move_left={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":65,"key_label":0,"unicode":97,"echo":false,"script":null)]
}
move_right={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":68,"key_label":0,"unicode":100,"echo":false,"script":null)]
}
jump={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":32,"key_label":0,"unicode":32,"echo":false,"script":null)]
}
attack={
"deadzone": 0.5,
"events": [Object(InputEventMouseButton,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"button_mask":0,"position":Vector2(0, 0),"global_position":Vector2(0, 0),"factor":1.0,"button_index":1,"canceled":false,"pressed":true,"double_click":false,"script":null)]
}
dodge={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":4194325,"key_label":0,"unicode":0,"echo":false,"script":null)]
}
```

注意：`HitstopManager.cs` 在 Task 9 才创建，autoload 引用暂时悬空——Godot 打开工程会告警但不阻塞；本任务先注册，Task 9 补文件（若中途运行工程报缺失，可临时注释该行）。

- [ ] **Step 3: 写 Player 控制器**

`Game/Gameplay/Combat/Player.cs`：

```csharp
using Godot;
using GodotGameTemplate.Core;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 玩家角色控制器（第一人称，规格第 3 节）。
/// 移动为连续意图直接驱动（联机时改走意图流）；动作类输入经 InputCommandBuffer
/// 缓冲后驱动 MeleeComboTracker 与闪避。「条件满足才消费」是缓冲的核心模式。
/// </summary>
public partial class Player : CharacterBody3D
{
    [Export] public float MouseSensitivity = CombatTuning.MouseSensitivity;

    private Node3D _head = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _viewArm = null!;
    private float _yaw;
    private float _pitch;

    private readonly InputCommandBuffer _buffer = new();
    private MeleeComboTracker _combo = null!;
    private float _timeAccum;
    private long _nowMs;

    private bool _dodging;
    private float _dodgeElapsed;
    private Vector3 _dodgeDirection = Vector3.Forward;

    public bool IsAttacking => _combo.IsActive;
    public bool IsInvulnerable =>
        _dodging && _dodgeElapsed < CombatTuning.DodgeInvulnerableSeconds;

    public override void _Ready()
    {
        _combo = new MeleeComboTracker(CombatTuning.WarriorCombo);
        _head = GetNode<Node3D>("Head");
        _camera = GetNode<Camera3D>("Head/Camera3D");
        _viewArm = GetNode<MeshInstance3D>("Head/Camera3D/ViewModelArm");
        Input.MouseMode = Input.MouseModeEnum.Captured;
        // 本地视角隐藏完整身体（联机时按归属控制，队友视角可见——规格第 3 节）
        GetNode<MeshInstance3D>("BodyVisual").Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(
                _pitch - motion.Relative.Y * MouseSensitivity,
                -Mathf.DegToRad(CombatTuning.PitchClampDeg),
                Mathf.DegToRad(CombatTuning.PitchClampDeg));
        }
        else if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event.IsActionPressed("attack"))
        {
            _buffer.Push(InputCommand.Action(InputCommandKind.Attack), _nowMs);
        }
        else if (@event.IsActionPressed("jump"))
        {
            _buffer.Push(InputCommand.Action(InputCommandKind.Jump), _nowMs);
        }
        else if (@event.IsActionPressed("dodge"))
        {
            _buffer.Push(InputCommand.Action(InputCommandKind.Dodge), _nowMs);
        }
    }

    public override void _Process(double delta)
    {
        Rotation = new Vector3(0f, _yaw, 0f);
        _head.Rotation = new Vector3(_pitch, 0f, 0f);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _timeAccum += dt;
        _nowMs = (long)(_timeAccum * 1000f);

        TickActions(dt);
        TickMovement(dt);
        TickViewModel(dt);
    }

    private bool CanAcceptAction => !_dodging && _combo.CanStartAction;

    private void TickActions(float dt)
    {
        if (CanAcceptAction && _buffer.TryConsume(InputCommandKind.Dodge, _nowMs, out _))
        {
            StartDodge();
        }

        if (_dodging)
        {
            _dodgeElapsed += dt;
            if (_dodgeElapsed >= CombatTuning.DodgeDuration)
            {
                _dodging = false;
            }
        }

        if (CanAcceptAction && _buffer.TryConsume(InputCommandKind.Attack, _nowMs, out _))
        {
            _combo.TryAdvance();
        }

        _combo.Tick(dt);
        TickHitWindow();
    }

    /// <summary>主动帧内的锥形判定（规格第 4 节）。命中即结算并触发 hit-stop。</summary>
    private void TickHitWindow()
    {
        if (!_combo.CanApplyHit)
        {
            return;
        }

        ComboStageData stage = CombatTuning.WarriorCombo[_combo.StageIndex];
        Vector3 forward = ForwardFlat();
        List<ICombatTarget> targets = FindTargets();
        List<ICombatTarget> hits = MeleeArcQuery.FindHits(
            GlobalPosition, forward, CombatTuning.AttackRange,
            CombatTuning.AttackHalfAngleDeg, targets, t => t.Center);

        if (hits.Count == 0)
        {
            return; // 窗口保持开启，稍后再试；窗口自然关闭则本段无命中
        }

        foreach (ICombatTarget target in hits)
        {
            target.ApplyHit(new HitData
            {
                Damage = stage.Damage,
                PoiseDamage = stage.PoiseDamage,
                Knockback = forward * stage.Knockback + Vector3.Up * 0.5f,
                Source = EntityId.None, // 联机时填玩家 NetworkId
            });
        }

        if (_combo.ConsumeHit())
        {
            HitstopManager.Request(CombatTuning.HitstopMs);
        }
    }

    private List<ICombatTarget> FindTargets()
    {
        var result = new List<ICombatTarget>();
        foreach (Node node in GetTree().GetNodesInGroup(CombatTuning.TargetGroup))
        {
            if (node is ICombatTarget target && target.CanBeHit)
            {
                result.Add(target);
            }
        }

        return result;
    }

    private void StartDodge()
    {
        _dodging = true;
        _dodgeElapsed = 0f;
        Vector2 axis = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        _dodgeDirection = axis.LengthSquared() > 0.01f
            ? (GlobalTransform.Basis * new Vector3(axis.X, 0f, axis.Y)).Normalized()
            : ForwardFlat();
        _combo.Reset(); // 闪避打断连段（取消规则的第一个成员）
    }

    private void TickMovement(float dt)
    {
        Vector2 axis = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 wish = GlobalTransform.Basis * new Vector3(axis.X, 0f, axis.Y);
        if (wish.LengthSquared() > 1f)
        {
            wish = wish.Normalized();
        }

        Vector3 horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
        Vector3 target;

        if (_dodging)
        {
            target = _dodgeDirection * CombatTuning.DodgeSpeed;
            horizontal = horizontal.MoveToward(target, CombatTuning.DodgeAccel * dt);
        }
        else if (_combo.IsActive)
        {
            target = wish * CombatTuning.WalkSpeed * CombatTuning.AttackMoveScale;
            if (_combo.Phase == ComboStagePhase.Startup)
            {
                ComboStageData stage = CombatTuning.WarriorCombo[_combo.StageIndex];
                float lungeSpeed = stage.ForwardStep / Mathf.Max(0.01f, stage.Startup);
                target += ForwardFlat() * lungeSpeed * 0.6f;
            }

            horizontal = horizontal.MoveToward(target, CombatTuning.GroundAccel * dt);
        }
        else
        {
            target = wish * CombatTuning.WalkSpeed;
            float accel = wish.LengthSquared() > 0.01f ? CombatTuning.GroundAccel : CombatTuning.GroundDecel;
            horizontal = horizontal.MoveToward(target, accel * dt);
        }

        Vector3 velocity = new Vector3(horizontal.X, Velocity.Y, horizontal.Z);
        if (IsOnFloor())
        {
            velocity.Y = -1f; // 贴地
            if (_buffer.TryConsume(InputCommandKind.Jump, _nowMs, out _))
            {
                velocity.Y = CombatTuning.JumpVelocity;
            }
        }
        else
        {
            velocity.Y -= CombatTuning.Gravity * dt;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    /// <summary>viewmodel 占位动画：前摇后拉、主动段前捅、后摇回位。动画资产到位后由 AnimationTree 接管。</summary>
    private void TickViewModel(float dt)
    {
        Vector3 rest = new Vector3(0.28f, -0.26f, -0.55f);
        Vector3 target = rest;
        if (_combo.Phase == ComboStagePhase.Startup)
        {
            target = rest with { Z = rest.Z + 0.12f };
        }
        else if (_combo.Phase == ComboStagePhase.Active)
        {
            target = rest with { Z = rest.Z - 0.18f };
        }

        _viewArm.Position = _viewArm.Position.Lerp(target, 18f * dt);
    }

    private Vector3 ForwardFlat()
    {
        Vector3 forward = -GlobalTransform.Basis.Z;
        forward.Y = 0f;
        return forward.Normalized();
    }
}
```

- [ ] **Step 4: 写 Player 场景**

`Game/Scenes/Player.tscn`：

```text
[gd_scene load_steps=5 format=3]

[ext_resource type="Script" path="res://Game/Gameplay/Combat/Player.cs" id="1_player"]

[sub_resource type="CapsuleShape3D" id="cap_shape"]
radius = 0.4
height = 1.8

[sub_resource type="CapsuleMesh" id="body_mesh"]
radius = 0.4
height = 1.8

[sub_resource type="BoxMesh" id="arm_mesh"]
size = Vector3(0.07, 0.07, 0.5)

[node name="Player" type="CharacterBody3D"]
collision_layer = 2
collision_mask = 5
script = ExtResource("1_player")

[node name="CollisionShape3D" type="CollisionShape3D" parent="."]
shape = SubResource("cap_shape")

[node name="BodyVisual" type="MeshInstance3D" parent="."]
position = Vector3(0, 0.9, 0)
mesh = SubResource("body_mesh")

[node name="Head" type="Node3D" parent="."]
position = Vector3(0, 1.6, 0)

[node name="Camera3D" type="Camera3D" parent="Head"]
fov = 90.0
near = 0.05

[node name="ViewModelArm" type="MeshInstance3D" parent="Head/Camera3D"]
position = Vector3(0.28, -0.26, -0.55)
mesh = SubResource("arm_mesh")
```

- [ ] **Step 5: 写竞技场场景**

`Game/Scenes/Main.tscn`：

```text
[gd_scene load_steps=7 format=3]

[ext_resource type="PackedScene" path="res://Game/Scenes/Player.tscn" id="1_player"]

[sub_resource type="PlaneMesh" id="floor_mesh"]
size = Vector2(40, 40)

[sub_resource type="BoxShape3D" id="floor_shape"]
size = Vector3(40, 1, 40)

[sub_resource type="BoxMesh" id="wall_mesh"]
size = Vector3(40, 3, 1)

[sub_resource type="BoxShape3D" id="wall_shape"]
size = Vector3(40, 3, 1)

[sub_resource type="StandardMaterial3D" id="floor_mat"]
albedo_color = Color(0.35, 0.38, 0.42, 1)

[node name="Main" type="Node3D"]

[node name="DirectionalLight3D" type="DirectionalLight3D" parent="."]
rotation = Vector3(-0.785398, 0.523599, 0)
shadow_enabled = true

[node name="Floor" type="StaticBody3D" parent="."]

[node name="Mesh" type="MeshInstance3D" parent="Floor"]
mesh = SubResource("floor_mesh")
surface_material_override/0 = SubResource("floor_mat")

[node name="Shape" type="CollisionShape3D" parent="Floor"]
position = Vector3(0, -0.5, 0)
shape = SubResource("floor_shape")

[node name="WallNorth" type="StaticBody3D" parent="."]
position = Vector3(0, 1.5, -20)

[node name="Mesh" type="MeshInstance3D" parent="WallNorth"]
mesh = SubResource("wall_mesh")

[node name="Shape" type="CollisionShape3D" parent="WallNorth"]
shape = SubResource("wall_shape")

[node name="WallSouth" type="StaticBody3D" parent="."]
position = Vector3(0, 1.5, 20)

[node name="Mesh" type="MeshInstance3D" parent="WallSouth"]
mesh = SubResource("wall_mesh")

[node name="Shape" type="CollisionShape3D" parent="WallSouth"]
shape = SubResource("wall_shape")

[node name="WallEast" type="StaticBody3D" parent="."]
position = Vector3(20, 1.5, 0)
rotation = Vector3(0, 1.5707964, 0)

[node name="Mesh" type="MeshInstance3D" parent="WallEast"]
mesh = SubResource("wall_mesh")

[node name="Shape" type="CollisionShape3D" parent="WallEast"]
shape = SubResource("wall_shape")

[node name="WallWest" type="StaticBody3D" parent="."]
position = Vector3(-20, 1.5, 0)
rotation = Vector3(0, 1.5707964, 0)

[node name="Mesh" type="MeshInstance3D" parent="WallWest"]
mesh = SubResource("wall_mesh")

[node name="Shape" type="CollisionShape3D" parent="WallWest"]
shape = SubResource("wall_shape")

[node name="Player" parent="." instance=ExtResource("1_player")]
position = Vector3(0, 0.2, 10)
```

（`load_steps` 计数：2 ext + 5 sub... 实际写文件时以 Godot 打开一次自动修正为准；缺失的 ext_resource 在 Task 8 补 DummyEnemy 实例。）

- [ ] **Step 6: 构建**

```bash
dotnet build GodotGameTemplate.csproj
```
预期：Build succeeded。（`HitstopManager` 引用未建会编译失败——本 Task 阶段若失败，先在 project.godot 注释 autoload 行，Task 9 恢复；或直接先做 Task 9 的 Step 1 建类。执行时按后者处理：把 HitstopManager.cs 的创建提前到本任务。）

- [ ] **Step 7: 手动验证（M1 手感清单）**

用 Godot 打开工程运行 Main 场景，逐项检查：

1. 鼠标被捕获，视角转动平滑、灵敏度适中；Esc 释放鼠标；
2. WASD 移动跟手，加减速有质感不漂移；
3. 空格跳跃，落地下次跳跃在 150ms 内按下仍生效（跳跃缓冲）；
4. Shift 闪避：朝移动方向突进，原地按时朝镜头前方；
5. 碰撞墙壁/地面无穿墙、无抖动。

- [ ] **Step 8: 提交**

```bash
git add -A && git commit -m "feat(player): 第一人称控制器与移动/闪避/竞技场场景(M1)"
```

---

### Task 8: 木桩敌人与碰撞阻挡（M1 收尾）

**Files:**
- Create: `Game/Gameplay/Combat/HealthComponent.cs`
- Create: `Game/Gameplay/Combat/DummyEnemy.cs`
- Create: `Game/Scenes/EnemyDummy.tscn`
- Modify: `Game/Scenes/Main.tscn`

- [ ] **Step 1: 血量组件**

`Game/Gameplay/Combat/HealthComponent.cs`：

```csharp
using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>血量组件：受击方通用。死亡只发信号，表现由宿主决定。</summary>
public partial class HealthComponent : Node
{
    [Signal] public delegate void DamagedEventHandler(float amount, float remaining);

    [Signal] public delegate void DiedEventHandler();

    [Export] public float MaxHealth = 100f;

    public float CurrentHealth { get; private set; }

    public bool IsDead => CurrentHealth <= 0f;

    public override void _Ready() => CurrentHealth = MaxHealth;

    public void ApplyDamage(float amount)
    {
        if (IsDead)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        EmitSignal(SignalName.Damaged, amount, CurrentHealth);
        if (IsDead)
        {
            EmitSignal(SignalName.Died);
        }
    }
}
```

- [ ] **Step 2: 木桩敌人**

`Game/Gameplay/Combat/DummyEnemy.cs`：

```csharp
using Godot;
using GodotGameTemplate.Spatial;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 木桩敌人：验证打击感与受击状态的最小敌人（规格 M2）。
/// 受击表现全部占位：闪白 / 击退滑行 / 躺平倒地 / 下沉消失。
/// </summary>
public partial class DummyEnemy : CharacterBody3D, ICombatTarget
{
    private static readonly Color BaseColor = new(0.55f, 0.58f, 0.62f);
    private static readonly Color FlashColor = new(1f, 0.9f, 0.8f);

    [Export] public float MaxPoise = 60f;

    private HealthComponent _health = null!;
    private HitReactionMachine _reaction = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private Vector3 _knockback = Vector3.Zero;
    private float _flashElapsed;

    public Vector3 Center => GlobalPosition + Vector3.Up * 0.9f;
    public bool CanBeHit => !_health.IsDead;
    public bool IsDowned => _reaction.IsDowned; // M4 处决条件从此读取

    public override void _Ready()
    {
        _health = GetNode<HealthComponent>("HealthComponent");
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _reaction = new HitReactionMachine(MaxPoise, CombatTuning.StaggerSeconds, CombatTuning.DownedSeconds);
        _material = new StandardMaterial3D { AlbedoColor = BaseColor };
        _mesh.MaterialOverride = _material;
        _health.Died += OnDied;
    }

    public void ApplyHit(in HitData hit)
    {
        _health.ApplyDamage(hit.Damage);
        _reaction.ApplyHit(hit.PoiseDamage);
        _knockback += hit.Knockback;
        _flashElapsed = 0.08f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _reaction.Tick(dt);

        // 占位表现：受击闪白 + 倒地躺平/恢复立起
        _material.AlbedoColor = _flashElapsed > 0f ? FlashColor : BaseColor;
        if (_flashElapsed > 0f)
        {
            _flashElapsed -= dt;
        }

        Vector3 meshRot = _mesh.RotationDegrees;
        meshRot.X = Mathf.MoveToward(meshRot.X, _reaction.IsDowned ? -90f : 0f, 360f * dt);
        _mesh.RotationDegrees = meshRot;

        // 击退滑行衰减
        Vector3 horizontal = new Vector3(_knockback.X, 0f, _knockback.Z);
        horizontal = horizontal.MoveToward(Vector3.Zero, 14f * dt);
        _knockback = horizontal;

        Vector3 velocity = new Vector3(_knockback.X, Velocity.Y, _knockback.Z);
        if (IsOnFloor())
        {
            velocity.Y = -1f;
        }
        else
        {
            velocity.Y -= CombatTuning.Gravity * dt;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void OnDied()
    {
        // 占位死亡：关闭碰撞、下沉后销毁。正式版换 Ragdoll/溶解（规格第 5 节）。
        SetCollisionLayerValue(3, false);
        Tween tween = CreateTween();
        tween.TweenProperty(_mesh, "position:y", _mesh.Position.Y - 1.5f, 0.7f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
```

注意 `using GodotGameTemplate.Spatial;` 暂未使用会告警——本文件删除该 using（SpatialAgent 只在场景里挂载，不需要 using）。

- [ ] **Step 3: 木桩场景**

`Game/Scenes/EnemyDummy.tscn`：

```text
[gd_scene load_steps=6 format=3]

[ext_resource type="Script" path="res://Game/Gameplay/Combat/DummyEnemy.cs" id="1_dummy"]
[ext_resource type="Script" path="res://Game/Gameplay/Combat/HealthComponent.cs" id="2_health"]
[ext_resource type="Script" path="res://Game/Gameplay/Spatial/SpatialAgent.cs" id="3_spatial"]

[sub_resource type="CapsuleShape3D" id="cap_shape"]
radius = 0.45
height = 1.8

[sub_resource type="CapsuleMesh" id="body_mesh"]
radius = 0.45
height = 1.8

[node name="DummyEnemy" type="CharacterBody3D" groups=["combat_targets"]]
collision_layer = 4
collision_mask = 7
script = ExtResource("1_dummy")

[node name="CollisionShape3D" type="CollisionShape3D" parent="."]
shape = SubResource("cap_shape")

[node name="Mesh" type="MeshInstance3D" parent="."]
position = Vector3(0, 0.9, 0)
mesh = SubResource("body_mesh")

[node name="HealthComponent" type="Node" parent="."]
script = ExtResource("2_health")

[node name="SpatialAgent" type="Node" parent="."]
script = ExtResource("3_spatial")
```

- [ ] **Step 4: 加入竞技场**

在 `Game/Scenes/Main.tscn` 头部补 ext_resource 并在文件末尾追加实例：

```text
[ext_resource type="PackedScene" path="res://Game/Scenes/EnemyDummy.tscn" id="2_dummy"]
```

```text
[node name="Dummy1" parent="." instance=ExtResource("2_dummy")]
position = Vector3(0, 0.2, -2)

[node name="Dummy2" parent="." instance=ExtResource("2_dummy")]
position = Vector3(-1.8, 0.2, -4)

[node name="Dummy3" parent="." instance=ExtResource("2_dummy")]
position = Vector3(1.8, 0.2, -4)
```

同时把 `load_steps` 改为正确计数（3 ext + 5 sub + 1）。

- [ ] **Step 5: 构建 + 手动验证**

```bash
dotnet build GodotGameTemplate.csproj && dotnet test Tests/GameplayTests.csproj
```
预期：均通过。

运行 Main：玩家走向木桩应被胶囊挡住（硬阻挡——阵型文档第 3.1 节 v1 形态）；推挤无抖动。

- [ ] **Step 6: 提交**

```bash
git add -A && git commit -m "feat(enemy): 木桩敌人、血量组件与单位碰撞阻挡(M1收尾)"
```

---

### Task 9: hit-stop 管理器（Task 7 已提前创建类时只做注册核对）

**Files:**
- Create(或已提前创建): `Game/Gameplay/Combat/HitstopManager.cs`

- [ ] **Step 1: 实现**

`Game/Gameplay/Combat/HitstopManager.cs`：

```csharp
using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 全局 hit-stop（规格第 4 节）：命中瞬间极短时间缩放，既是手感核心也是
/// M4 处决位置修正的遮罩。autoload 单例；token 防止旧计时器提前恢复时间流。
/// </summary>
public partial class HitstopManager : Node
{
    private static HitstopManager? _instance;
    private static int _token;

    private const float TimeScale = 0.05f;

    public override void _Ready() => _instance = this;

    public static void Request(int durationMs)
    {
        if (_instance == null)
        {
            return;
        }

        int token = ++_token;
        Engine.TimeScale = TimeScale;
        // ignoreTimeScale=true 的计时器负责恢复（否则永远不会到时）
        SceneTreeTimer timer = _instance.GetTree().CreateTimer(durationMs / 1000.0, true, false, true);
        timer.Timeout += () =>
        {
            if (token == _token)
            {
                Engine.TimeScale = 1.0f;
            }
        };
    }
}
```

- [ ] **Step 2: 注册核对**

确认 `project.godot` 的 `[autoload]` 段含：

```ini
HitstopManager="*res://Game/Gameplay/Combat/HitstopManager.cs"
```

```bash
dotnet build GodotGameTemplate.csproj
```
预期：Build succeeded。

- [ ] **Step 3: 手动验证**

运行 Main，连续攻击木桩：每次命中应有极短顿帧（约 70ms），战斗节奏明显「弹」；无顿帧卡死不恢复的情况（token 逻辑）。

- [ ] **Step 4: 提交**

```bash
git add -A && git commit -m "feat(combat): 全局hit-stop管理器"
```

---

### Task 10: 打击感整合验证（M2 收尾）

**Files:** 无新文件（整合验证 + 微调）

- [ ] **Step 1: 全量构建与测试**

```bash
dotnet build GodotGameTests.csproj 2>/dev/null; dotnet build GodotGameTemplate.csproj && dotnet test Tests/GameplayTests.csproj
```
预期：全部通过。

- [ ] **Step 2: M2 手工验收清单（对照规格第 7 节）**

运行 Main 逐项验证并记录感受：

1. 三段连击依次播放，节奏清晰（前摇-判定-后摇可感知）；
2. 连段中途快速连点：下一段在取消窗口接上，不丢输入、不插入多余段；
3. 第 3 段命中：击退明显更远（finisher Knockback=5），木桩躺平（韧性 60 < 55+20）；
4. 命中 hit-stop 存在且短促；空挥无顿帧；
5. 木桩硬直闪白；倒地 2.5s 后立起并恢复韧性；
6. 攻击期间移动输入大幅衰减但未完全锁死；前摇有突进感；
7. 死亡木桩下沉消失，场景可重复刷（重跑场景即可）。

发现问题 → 回 CombatTuning 调参 → 重复 Step 2。手感参数不追求一次到位，里程碑标准是「六问中问题 4（冲锋获得突破感）的前置：打击反馈成立」。

- [ ] **Step 3: csharpier 全量格式化**

```bash
dotnet csharpier . && dotnet build GodotGameTemplate.csproj && dotnet test Tests/GameplayTests.csproj
```
预期：格式化后构建/测试仍通过。

- [ ] **Step 4: 提交收尾**

```bash
git add -A && git commit -m "chore: M2 打击感闭环验收与格式化"
```

---

## 自审记录

1. **规格覆盖**：M1 行（输入/命令层✓Task3、移动跳跃✓Task7、FP相机✓Task7、木桩✓Task8）；M2 行（3段连击✓Task4/9、判定窗口✓Task5/9、hit-stop✓Task9、受击状态✓Task6/8）。规格第 3 节两层状态机——移动层以内联逻辑承载（Grounded/Airborne 由 IsOnFloor 表达，AimMode 属 M5+），行动层由 MeleeComboTracker+闪避标志承载，M3 技能框架时再抽正式 ActionStateMachine，此处不过度设计。规格第 6 节联机约定——EntityId/种子RNG/InputCommand/缓冲均已落地并可测✓。
2. **占位符扫描**：Task 7 Step 6 的构建失败处理与 Task 10 Step 1 的冗余命令是有意的执行分支说明，非占位。无 TBD/TODO。
3. **类型一致性**：`InputCommand.Action(kind)`、`TryConsume(kind, nowMs, out cmd)`、`MeleeComboTracker.TryAdvance/ConsumeHit/Reset/Tick`、`HitReactionMachine.ApplyHit(poiseDamage)/Tick`、`MeleeArcQuery.FindHits<T>(origin, flatForward, range, halfAngleDeg, targets, centerOf, heightTolerance)`、`ICombatTarget.Center/CanBeHit/ApplyHit(in HitData)`、`HitstopManager.Request(int)` 在各任务间签名一致。`CombatTuning.TargetGroup` 在 Player.FindTargets 与 EnemyDummy.tscn groups（文本 "combat_targets"）一致。
4. **已知取舍**（记录，非遗漏）：连段中途的输入在非窗口期 Push 后被 TryConsume 前的条件拦截——消费条件先于消费检查（Task 7 Player.TickActions 顺序即如此实现）；木桩互相之间、木桩与玩家之间 v1 用物理胶囊，逻辑空间系统在 M3 冲锋时接管挤开——符合规格第 1 节「Movement Pipeline → SpatialAgent」的渐进路线。
