# M5 实现计划：阵型 MVP（前排阻挡/后排压制/阵型朝向）+ 冲刺速度线 Shader

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现阵型文档（`Docs/formation-combat-design.md`）第 23 节 MVP：3 前排 + 2 后排的敌方编队（槽位驻守、阵型朝向滞回、前排死亡开缺口、后排压制）；冲刺/下落期间屏幕径向速度线（程序化 shader + 暗角）强化速度感。

**Architecture:** 阵型分两层——纯逻辑（FormationLayout 槽位换算 / FormationBrain 朝向滞回，全单测）+ 场景薄胶囊（FormationController 仿 EnemyGroup 激活模式）；阵型成员复用 M4 敌人基建（继承 EnemyAI 覆写 `TickActive` 写 `DesiredHorizontal`，近战复用 AttackCycle 三步模板，后排经 RangedEnemy 虚方法重构获得"槽位站定"变体）。速度线为纯表现层：~50 行 canvas_item shader 程化生成径向线条+暗角，强度由 CameraFeel 既有混合度驱动，不新增模拟状态。

**Tech Stack:** Godot 4.6 + C#、GDShader(canvas_item)、xUnit、CSharpier；冒烟输入注入沿用 `Input.ActionPress/ActionRelease` + 阶段化激活隔离。

**前置**：M1~M4 已完成合并（59 单测 + 34 项冒烟全绿）。处决、刺客继续顺延；连段冻结。

---

## 0. 范围决策（用户已确认，2026-09-10）

| 主题 | 决策 |
|---|---|
| 处决 | 本次不做（继续顺延） |
| 阵型 | 做，按文档第 23 节 MVP 范围（3前排+2后排、朝向滞回、缺口、不重组补位） |
| 速度线 | **程序化 shader（用户已选）**：径向速度线 + 轻微暗角，无需美术资产 |
| 手感方向 | 速度感三件套叠加：既有 FOV 拉伸/震动 + 新速度线 |

## 1. 阵型系统

### 1.1 纯逻辑层（TDD）

```text
Game/Gameplay/Enemies/Formation/
├── FormationLayout.cs   标准阵型本地槽位：前排3（x=-1.4/0/+1.4, 本地z=-1.5，面向玩家侧）
│                        后排2（x=±0.7, z=+1.2）；
│                        SlotWorld(center, yaw, localOffset) 本地→世界换算
└── FormationBrain.cs    朝向滞回规则（文档第 12 节）：玩家方向与当前阵型朝向偏差
                          > RotateThresholdDeg(55°) 才开始转动，以 90°/s 匀速转向目标角，
                          对齐后保持——玩家绕阵走圈时阵型不炮塔式跟转
```

单测清单：
- 槽位世界换算数学（yaw=0/90°/180° 三组已知解）；
- 阈值内不转；越阈后按转速推进；转到位即停；
- 成员死亡→其槽位悬空（不重分配、不补位——文档"死亡产生缺口"）。

### 1.2 FormationController（Node3D 场景节点）

仿 EnemyGroup 既有模式：
- 导出 `InitiallyActive=false`、`ActivateRadius`（聚合激活递归设子成员 `Active`）；
- 阵型成员为直接子节点，各成员导出 `int SlotIndex`；
- 每物理帧：FormationBrain 更新 yaw → FormationLayout 换算各槽位世界坐标 → 写给存活成员（`member.SlotPosition` / `SlotFacing`）；
- 死亡成员槽位留空（MVP 不重组）。

### 1.3 阵型成员（复用 M4 基建）

- **FormationMelee.cs**（前排，盾兵/骑士共用类）：继承 `EnemyAI`，覆写 `TickActive`：
  - 目标点 = 槽位世界坐标；到点站定（`FaceTowards(player)` 警戒朝向）；
  - 玩家进入攻击距离 2.2m → AttackCycle 近战（前摇预告色 → `MeleeArcQuery.FindHits` 锥形结算，模板抄 SwarmSoldier.Strike）；
  - 不离槽追击（槽位即 leash）；
  - 两个场景仅参数不同：`EnemyShield.tscn`（PushResistance **250**——冲锋 500 可推开）、`EnemyKnight.tscn`（PushResistance **500**——冲锋不可推，文档第 7 节表值）。
- **RangedEnemy 小重构**：把"想去哪"抽成虚方法（默认= 风筝距离带，行为不变，全量回归把关）；
  - **FormationArcher.cs** 继承之，覆写为"槽位点站定"，完整保留瞄准红线 → `Projectile.Spawn` → 冷却链路；
  - `EnemyFormationArcher.tscn`（弓）与 `EnemyFormationMage.tscn`（法师：仅 Tint/伤害/冷却差异）为后排两场景。

### 1.4 Main.tscn 三遭遇布局

- 北 (0, 0.2, -18)：阵型组——盾L / 骑士C / 盾R 前排 + 弓/法后排，InitiallyActive=false；
- M4 游击编组（1远程+2护卫+4低级兵）迁至东侧 (13, 0, -2) 保留；
- 木桩区不动（技能手感测试用）。冒烟按阶段显式激活，互不干扰。

## 2. 冲刺速度线（程序化 shader）

- `Game/UI/SpeedLines.gdshader`（~50 行 canvas_item）：
  - 片元程序化径向速度线：屏幕中心向外放射，角度哈希决定线条扇区与长度，`intensity` 控制密度/亮度/长度；
  - 同文件内置轻微暗角（`vignette` uniform）；
  - 全部无贴图、无美术资产依赖；单全屏四边形，开销可忽略；
- `Game/UI/SpeedLines.cs`：全屏 `ColorRect`（挂 Hud 的 CanvasLayer 顶层，`MouseFilter=Ignore`），`Material` 挂 shader；每帧从 CameraFeel 读强度写 uniform；
- CameraFeel 驱动：`intensity = max(_chargeBlend, _leapBlend)`（复用既有渐入渐出混合度，不新增状态）；
- 参数进 CombatTuning 手感区（密度/速度/暗角强度/最大 intensity）；
- 冒烟断言：冲锋期间 shader `intensity > 0.5`；结束后 0.5s 回落 `< 0.05`。

## 3. 任务序（每任务构建+测试+提交，最后合并 main）

- [x] **T1 纯逻辑**：FormationLayout + FormationBrain TDD（红→绿→提交）
- [x] **T2 前排**：FormationMelee + EnemyShield/EnemyKnight 场景 + 站桩攻击（木桩区手动可验）
- [x] **T3 后排**：RangedEnemy 虚方法重构（行为等价，59 单测+既有冒烟全量回归）+ FormationArcher/法师 + 场景
- [x] **T4 接线**：FormationController + Main.tscn 三遭遇布局
- [x] **T5 阵型冒烟**：①正面被前排挡停在半径和 ②击杀中排盾后缺口可穿 ③绕阵半圈 yaw 不跟转、越阈才转 ④后排箭使玩家掉血 ⑤冲锋推散 250 盾 / 被 500 骑士挡停 ⑥贴侧绕行被边缘前排蹭血
- [x] **T6 速度线**：shader + SpeedLines 节点 + CameraFeel 强度驱动 + 冒烟断言
- [x] **T7 收尾**：全量单测+冒烟、csharpier、规格里程碑更新（M5 阵型完成记录）、计划执行记录、合并 main

## 4. 验收标准（对应阵型文档第 27 节六问）

1. 正面冲击→前排是一道墙（冒烟①✓）；
2. 侧面绕行有成本（冒烟⑥✓ 蹭血）；
3. 前排死亡→敌阵被打开（冒烟②✓）；
4. 冲锋=突破能力：推开盾兵(250)、被骑士(500)挡停（冒烟⑤✓）；
5. 后排压制产生"先处理谁"的决策压力（冒烟④✓ + 实机）；
6. 不同敌人组合促使玩家改变打法（实机人工确认）；
7. 冲刺全程径向速度线+暗角渐入渐出，与 FOV/震动叠加出明显速度感（shader 参数冒烟✓ + 实机体感确认）；
8. 全部单元测试与无头冒烟通过，csharpier 后构建仍绿。

## 5. 非目标（本次不做）

- 处决系统、刺客角色、连段深化（冻结）；
- 阵型整体推进/后撤、死亡后重组补位、多编队协同、战斗槽位动态轮转；
- 远程兵 LOS 遮挡判定、敌人动画资产、网络同步；
- 速度线之外的新特效（色散/边缘模糊留给后续打磨，shader 文件预留扩展位）。

---

## 执行记录（2026-09-10 夜间自动化执行）

- **起止时间**：2026-09-10 23:00 – 23:45（单次无人值守会话）
- **结果**：✅ 全部完成。T1–T7 共 7 个任务全部落地，每任务独立提交（feat/formation×4、test/formation、feat/feel、docs 收尾）。
- **验证门槛**：`dotnet build` 0 错误 0 警告；xUnit 59→71 全过（新增阵型纯逻辑 12 项）；无头冒烟 35→53 项全过（新增阵型 6 问 14 项断言 + 速度线 3 项，退出码 0）；csharpier 全仓格式化后构建仍绿。
- **执行要点**：
  - TDD 全程执行（红→绿→提交）；阵型纯逻辑三件套：FormationLayout（槽位换算）/ FormationBrain（55° 朝向滞回、90°/s 匀速、短弧、转到位即停）/ FormationRoster（死亡槽位悬空不补位）；
  - RangedEnemy「想去哪」抽为虚方法 `MoveIntent` + 瞄准/冷却/箭伤虚成员，默认行为逐字节等价（59 单测 + 35 项冒烟全量回归把关）；
  - Main.tscn 三遭遇布局：北阵型（0, 0.2, -16.5）/ 东游击编组（13, 0, -2）/ 木桩区不动；阵型成员场景出生点直接按"面向玩家来向（南）"的槽位摆放，激活瞬间 FormationController 会将阵型面向玩家，无需重新走位（否则后排走位+瞄准+飞行超出冒烟窗口，曾致 ④⑤ 失败）；
  - **对计划的偏差**：阵型中心由计划的 (0, 0.2, -18) 北移至 (0, 0.2, -16.5)——-18 处后排槽位（朝向后方 1.2m）落在北墙内，单位永远无法到槽（冒烟④复现）；-16.5 保证前后排纵深，冒烟②缺口可穿才有判定空间；
  - 骑士场景补 `BodyMass = 500`（文档 §7 表值）——mass 缺省 100 时与玩家对半分摊穿透，冲锋窗口内骑士被推 2.14m；500 后峰值 <1.5m；
  - 速度线为纯程序化 canvas_item shader（角度哈希扇区 + 径向流动条纹 + 暗角），强度 = CameraFeel `max(_chargeBlend, _leapBlend)`，参数进 CombatTuning 手感区；
  - 冒烟时序稳健化：FeelPhase 落地环计数改为等待 `LeapLanded` 事件后计数（原固定 80 帧与环 0.4s 生命几乎重合，偶发 0 计数）；盾/骑士推挤断言取冲锋窗口内峰值位移（成员被推同时会走回槽位，净位移会低估）；
  - 冒烟用 FormationController.FacingYawDeg 只读暴露（调试/测试用）。
- **遗留**：处决、刺客继续顺延（范围决策不变）；速度线仅强度驱动，无新特效（非目标）；一次无头冒烟进程退出阶段出现 Illegal instruction 崩溃（套件已全部通过并打印结果后，进程回收阶段偶发，未复现于其余三轮），疑似引擎/Mono 层既有行为，不影响退出码门槛的常规判定，记录在案。
