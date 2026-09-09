# M4 修订版实现计划：敌人 AI（远程/护卫/低级兵）+ 战斗手感打磨 + 技能改键底层 + 箭矢方向修复

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复箭矢方向 bug；实现三种敌人（远程/护卫/低级兵）的单体与群体 AI；打磨冲锋速度感、跳劈力量感与落地重量感；技能改键底层预留（默认数字键）。前置文档：`Docs/specs/2026-09-09-player-combat-design.md`、`Docs/plans/2026-09-09-player-combat-m1-m2.md`（M1+M2 已完成）、`Docs/plans/2026-09-09-m3-skills-archer-spatial.md` 同类命名（M3 修订版已完成并合并）。

**Architecture:** 敌人 AI 沿用既有分层——纯逻辑类（攻击周期/环绕槽位/风筝距离带）与 Godot 节点薄胶囊分离，前者全部单元测试；群体 AI 只做槽位分配与激活动，不写行为树。手感打磨全部走表现层（只读玩家状态/订阅事件，规格第 1 节），不改模拟逻辑。

**Tech Stack:** Godot 4.6 + C#、xUnit、CSharpier；输入注入用 `Input.ActionPress/ActionRelease`（匹配物理帧边沿轮询）。

---

## 0. 范围决策（用户已确认，2026-09-09）

| 主题 | 决策 |
|---|---|
| 箭矢方向 bug | 修复（见 §1） |
| 技能改键 | **仅底层预留**：默认 1/2/3 + 持久化加载/保存设施，不做界面 |
| 连段 | 保留现状，**冻结开发**（不再往该方向迭代） |
| 刺客 / 处决 | **暂缓**，本次不做 |
| 开发方向 | 打磨已有两角色与战士三技能手感：冲锋速度感、跳劈力量感、落地重量感 |
| 敌人 | 三种：远程敌人 + 贴身护卫（跟随远程、推不动）+ 会围上来攻击的低级兵；单体 + 群体 AI |

## 1. 箭矢方向 Bug（根因已定位）

**根因**：`Projectile` 生成时挂在 Player（会旋转的节点）下，飞行积分 `Position += _velocity * dt` 是**局部坐标**——玩家 yaw≠0 时速度向量被父级旋转，箭飞向旋转后的方向；持续转视角时箭在空中拐弯。

**修复**：
- `Projectile.Spawn` 改挂 `player.GetTree().CurrentScene`（场景根），飞行积分改用 `GlobalPosition`；
- `Spawn` 增加 `collisionMask` 参数：敌方箭掩码排除敌方层（无友伤），我方箭排除玩家层（默认值保持现状行为）。

**回归冒烟**：玩家 yaw 转 180°（反射设 `_yaw`）射快速箭，必须命中身后木桩。

## 2. 玩家受击路径（敌人 AI 的前提）

- `Player` 实现 `ICombatTarget`：`Center` = GlobalPosition + Up×1.2m；`CanBeHit` = 未死亡；`ApplyHit` → HealthComponent 扣血（v1 玩家无受击状态机/击退，后续再打磨受击反馈）；
- 死亡 → 简单重载当前场景（`ReloadCurrentScene`）；
- HUD 常驻玩家血条（复用 Hud，加 PlayerBar）。

## 3. 技能改键底层

- `project.godot`：skill_1/2/3 默认键从 Q/E/R 改为数字键 **1/2/3**（physical 49/50/51）；
- 新建 `Game/Gameplay/Core/KeybindManager.cs`（autoload）：
  - 启动时加载 `user://keybinds.cfg`（ConfigFile：action → physical_keycode），对每个记录执行 `InputMap.ActionEraseEvents + InputMap.ActionAddEvent(InputEventKey{PhysicalKeycode})`；
  - 提供 `Rebind(action, keycode)` / `Save()` / `ResetToDefaults()` API——本次无 UI，供将来设置界面直接调用；
- 冒烟：运行时 `Rebind("skill_1", ...)` 后新键可施放对应技能。

## 4. 敌人 AI（单体 + 群体）

### 文件结构

```text
Game/Gameplay/Enemies/
├── EnemyAI.cs              抽象基类(CharacterBody3D)：血量/受击状态机(复用HitReactionMachine)/
│                           SpatialAgent/强制位移通道(从DummyEnemy上移)/受击闪红
├── AttackCycle.cs          纯逻辑攻击周期：前摇(预告)→打击帧(锥形查询)→冷却（TDD）
├── SwarmSoldier.cs         低级兵：追"环绕槽位"→近身攻击循环
├── GuardEnemy.cs           护卫：绑定远程单位，左右翼1.2m跟随；玩家近身才攻击；leash 3.5m 回归
├── RangedEnemy.cs          远程：风筝距离带[7,11]m；瞄准0.7s(红线预告)→射 Projectile→冷却2.4s
├── SurroundSlotAssigner.cs 纯逻辑：N 个低级兵按玩家周围角度均匀分配环绕槽位(半径1.8m)（TDD）
└── EnemyGroup.cs           群体AI：聚合激活(玩家<12m，导出 InitiallyActive)；每0.4s重算环绕槽位
```

### 行为要点

- **低级兵**：追自己的环绕槽位（不是追玩家身体，沿用阵型文档第 10 节"回战斗位置"思想），进入攻击距离后走攻击周期；互相挤开由既有逻辑空间系统自然处理；
- **护卫**：跟随目标远程单位的左右翼 1.2m 偏移点；玩家进入 2.6m 才转入攻击；离开 3.5m 回归跟随（leash）；**推不动 = SpatialAgent.PushResistance 9999**（冲锋 500 < 9999，复用已验证的重木桩机制）；
- **远程**：距离 <7m 后退、>11m 接近、区间内驻停留；开火前 0.7s 瞄准线预告（占位：红色细长 Mesh 或 ImmediateMesh 线段）；箭矢复用 Projectile（掩码排除敌方层）；
- **群体激活**：EnemyGroup 每帧检查玩家距离聚合激活；SurroundSlotAssigner 每 0.4s 重算槽位（按当前围攻者集合稳定排序，避免抖动）。

### 纯逻辑 TDD 清单

1. `AttackCycle`：前摇→打击帧一次性结算→冷却→循环；前摇内被位移不打断（v1 简化，记录在案）；
2. `SurroundSlotAssigner`：N 兵均匀分角度、集合变化时槽位稳定（不乱跳）、0 个时返回空；
3. 远程 `KiteBand`（纯函数或小类）：<7 后退 / >11 接近 / 区间驻留。

### 场景接线

- Main.tscn 北侧新放一组默认编组：1 远程 + 2 护卫 + 4 低级兵（`InitiallyActive = false`，不干扰既有冒烟测试的木桩区）；三个木桩保留（技能测试用）；
- EnemyGroup 节点挂编组管理脚本，敌人节点为子节点。

## 5. 战斗手感打磨（纯表现层）

### 玩家状态/事件暴露

- `Player` 增加：`AimBlend01`（现 `_aimBlend` 公开）、`IsChargeDashing`、`IsLeapAirborne`；
- 事件（C# event，表现层订阅）：`SkillStarted(kind) / SkillEnded(kind) / LeapLanded`；
- `IAbilityContext` 增加 `NotifyLeapLanded()`（LeapSlam Impact 时调用）——同步更新测试假件与 Player 实现。

### CameraFeel 组件（新建 `Game/UI/CameraFeel.cs`，挂 Player 相机子节点）

- 接管 FOV 与震动（Player._Process 不再直改相机，只更新 `_aimBlend`）；
- **冲锋速度感**：`SkillStarted(Charge)` → FOV +10（快进慢出曲线）+ 高频小幅震动 + viewmodel 后拉姿态；
- **跳劈力量感/落地重量感**：起跳 FOV -6 + 相机上仰小踢；滞空 FOV 微收；`LeapLanded` → 震动爆发（幅度 0.4、0.35s 衰减）+ 相机下沉回弹 + 跳劈专属 hit-stop 110ms（`SkillDefinition` 增加 `HitstopMs` 字段，默认 70，跳劈 110）+ 生成冲击波环；
- `ShockwaveRing`（代码生成，无美术资产）：TorusMesh 从 0.3m 扩散到 3.2m 并淡出，0.4s 后自毁；挂在落地位置。

### 冒烟断言（轻量）

- 跳劈落地触发 `LeapLanded` 且场景中出现 ShockwaveRing 节点（计数>0），0.6s 后自毁；
- 冲锋期间 `IsChargeDashing` 为真且结束转假。

## 6. 任务序（每任务构建+测试+提交，最后合并 main）

- [x] **T1 箭矢修复**：Spawn 挂场景根 + GlobalPosition 积分 + collisionMask 参数 + 转身 180° 回归冒烟
- [ ] **T2 玩家受击路径**：Player 实现 ICombatTarget + HUD 玩家血条 + 死亡重载场景
- [ ] **T3 改键底层**：默认键改 1/2/3 + KeybindManager(autoload) + 持久化 + 重绑冒烟
- [ ] **T4 敌人基类+低级兵**：EnemyAI 基类（从 DummyEnemy 上移共性）+ AttackCycle（TDD）+ SurroundSlotAssigner（TDD）+ SwarmSoldier + EnemyGroup 激活
- [ ] **T5 远程兵**：KiteBand（TDD）+ RangedEnemy + 瞄准预告线 + 敌方箭（掩码无友伤）+ 玩家掉血冒烟
- [ ] **T6 护卫+编组**：GuardEnemy 跟随/leash/推不动（PushResistance 9999）+ Main.tscn 默认编组接线 + 编组冒烟
- [ ] **T7 手感打磨**：状态/事件暴露 + CameraFeel（FOV/震动/viewmodel 姿态）+ ShockwaveRing + LeapLanded hit-stop 110ms
- [ ] **T8 收尾**：全量单测+冒烟、csharpier、规格里程碑更新（M5 修订记录：敌人AI+手感打磨完成，处决/刺客顺延）、合并 main

## 7. 验收标准

1. 玩家转任意角度射箭，箭沿镜头方向飞行并命中目标（回归冒烟）；
2. 敌方箭与近战能使玩家掉血，玩家死亡后场景重开；HUD 有玩家血条；
3. 技能默认 1/2/3 释放；运行时重绑 skill_1 后新键生效并持久化到 `user://keybinds.cfg`，重启加载；
4. 玩家靠近编组：低级兵围上来且互相保持间距、护卫贴住远程单位左右翼且冲锋推不动（玩家被挡停）、远程兵在 7~11m 带驻停射击、贴近时后撤拉开；
5. 冲锋有明显速度感（FOV 拉伸+震动）、跳劈起跳有力滞空收缩、落地沉重（震动+下沉回弹+110ms 顿帧+冲击波环）——冒烟断事件与节点，手感本身实机确认；
6. 全部单元测试 + 无头冒烟测试通过；csharpier 格式化后构建仍绿。

## 8. 非目标（本次明确不做）

- 处决系统、刺客、连段深化（冻结）、玩家受击状态机/硬直；
- 敌人受击外的玩家受击反馈（受击闪红/方向指示留到打磨期）；
- 改键设置界面（仅底层 API）、敌人动画资产（全部占位表现）；
- 多编组协同/阵型阵形（阵型 MVP 仍在此之后）。
