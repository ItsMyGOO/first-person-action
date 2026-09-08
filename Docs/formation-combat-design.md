# 第一人称战术阵型战斗：敌方前后排与空间突破系统

> 文档定位：玩法概念 + 技术方案 + 第一版实现边界  
> 目标：实现一种“第一人称视角、类似《全面战争》/RTS 编队逻辑”的小规模战斗体验。玩家面对具有前后排层次的敌方编队，需要通过正面突破、侧翼绕行、攻击、冲锋、击杀关键单位等方式破坏阵型，而不是单纯地逐个击杀追击玩家的敌人。

---

## 1. 核心设计目标

### 1.1 核心体验

玩家面对敌方编队时，应产生以下直觉：

- 前排单位是“墙”，可以阻挡玩家直接进入后排。
- 后排单位虽然站得更远，但并非绝对安全。
- 玩家可以通过攻击、走位、冲锋、击杀前排等方式打开缺口。
- 绕过前排并非完全禁止，但会受到边缘单位、攻击范围和威胁区域的制约。
- 玩家是否能突破阵型，不完全由物理碰撞决定，也可以通过敌人的攻击威胁形成“软阻挡”。
- 敌人不需要密集站成一堵真正的物理墙；即使存在一定间隙，也可以通过攻击威胁使“直接穿过敌阵”付出无法接受的代价。

核心目标不是：

> 让 AI 尽可能聪明。

而是：

> 让敌方站位、空间阻挡和攻击威胁共同形成“突破敌阵”的战斗博弈。

---

# 2. 典型战斗结构

最基础的敌方编队：

```text
          后排
     Archer   Mage   Archer

          前排
     Shield  Knight  Shield

                 ↓

               Player
```

前排负责：

- 占据空间
- 阻挡玩家
- 承受伤害
- 给后排创造安全距离
- 对试图突破的玩家造成近战威胁

后排负责：

- 远程攻击
- 范围攻击
- 辅助
- 治疗
- 控制

玩家需要解决的问题：

1. 是否正面突破？
2. 是否从侧面绕行？
3. 是否先击杀前排？
4. 是否使用冲锋/强制位移突破？
5. 是否先处理后排？
6. 是否利用敌人站位制造局部缺口？

---

# 3. 两种“阻挡”机制

敌人阻挡玩家不应该只依赖物理碰撞。

应同时存在两类阻挡：

## 3.1 硬阻挡：空间阻挡

敌人拥有一个逻辑上的空间占用范围：

```text
Enemy
  └── Radius
```

当玩家进入敌人的空间范围时：

```text
Player → Enemy
         X
```

系统阻止玩家直接穿过，或者根据双方属性决定是否发生挤开。

这属于：

> Hard Spatial Constraint

---

## 3.2 软阻挡：攻击威胁

敌人不一定需要实际挡住玩家。

例如：

```text
       Archer
          |
          | Attack Range
          |
          V

Player → → → → → 后排
```

即使敌人之间存在明显空隙，玩家如果直接穿过去：

- 会被多个敌人攻击
- 会受到持续伤害
- 可能受到减速/控制
- 可能触发近战攻击
- 可能受到多个单位的集火

于是：

> “可以走过去” ≠ “值得走过去”。

这就是软阻挡。

---

# 4. 为什么应该同时使用两种阻挡

如果只使用物理阻挡：

```text
E E E E E
```

需要把敌人排得非常密。

容易导致：

- 阵型过于拥挤
- AI 互相挤压
- 移动不自然
- 玩家感觉像撞空气墙
- 地图空间利用率降低

如果只使用攻击威胁：

```text
E     E     E
```

玩家可能完全无视敌人：

```text
Player → → → → 后排
```

因此推荐：

```text
稀疏站位
   +
空间阻挡
   +
攻击威胁
```

这样既能保持阵型的视觉层次，又能控制玩家突破的实际成本。

---

# 5. 空间系统：不强依赖物理碰撞

推荐将玩家与敌人的“战斗空间关系”做成逻辑系统。

每个单位具有：

```text
SpatialState
├── Radius
├── GameplayMass
├── PushResistance
├── MovementForce
├── CollisionPriority
└── ForcedMovementState
```

其中：

### Radius

单位在战斗空间中占据的半径。

### GameplayMass

不是现实世界重量，而是游戏设计参数。

表示：

> 这个单位有多难被挤开。

### PushResistance

抵抗玩家或其他单位推动的能力。

### MovementForce

单位当前移动行为产生的推动能力。

例如：

```text
普通移动
MovementForce = 100

冲锋
MovementForce = 500
```

### CollisionPriority

处理特殊情况下的空间冲突。

### ForcedMovementState

表示：

- Knockback
- Dash
- Charge
- Pull
- Explosion
- Boss 强制位移

等状态。

---

# 6. 挤开规则

玩家接近敌人：

```text
Player → Enemy
```

如果：

```text
Distance < PlayerRadius + EnemyRadius
```

则产生空间重叠。

系统根据双方参数决定：

### 情况 A：无法挤开

```text
Player PushForce < Enemy PushResistance
```

结果：

```text
Player → Enemy
         X
```

玩家被阻挡。

---

### 情况 B：玩家可以挤开

```text
Player PushForce > Enemy PushResistance
```

结果：

```text
Player =====> Enemy
                ↓
              Enemy
              被推开
```

可以用于：

- 冲锋
- 重攻击
- 特殊技能
- 高力量角色

---

# 7. Gameplay Mass 不等于真实质量

不建议把系统做成真实物理模拟。

推荐：

```text
GameplayMass
+
PushForce
+
StateModifier
+
Priority
```

例如：

| 单位 | GameplayMass | 推开难度 |
|---|---:|---|
| 小型敌人 | 50 | 很容易 |
| 普通士兵 | 100 | 中等 |
| 盾兵 | 250 | 困难 |
| 重装骑士 | 500 | 极难 |
| Boss | 1000 | 基本不可 |

冲锋可以通过：

```text
DashForce
```

临时提高玩家的突破能力。

---

# 8. 玩家与敌人的碰撞关系

推荐：

```text
Player ↔ Enemy
```

由逻辑空间系统负责。

不要求一定使用 Godot Physics Collision 来实现 Gameplay 阻挡。

物理系统可以继续用于：

- 世界
- 墙壁
- 地形
- 子弹
- 投射物
- 特殊物理效果

而玩家/敌人的核心战斗空间关系由 Gameplay Spatial System 控制。

这样可以获得更高的确定性。

---

# 9. 敌人之间的碰撞

不推荐默认使用强物理碰撞。

原因：

```text
Enemy ↔ Enemy
```

如果全部使用真实碰撞，很容易产生：

- 互相推挤
- 阵型变形
- 抖动
- 卡死
- 玩家推动整个敌阵
- 狭窄空间无法重新站位

推荐：

> 敌人之间使用逻辑空间分离，而不是强物理碰撞。

敌人拥有：

```text
DesiredFormationPosition
```

然后通过：

```text
Formation
+
Navigation
+
Separation / Avoidance
```

维持合理距离。

---

# 10. 敌人移动模型

敌人不应该简单使用：

```text
MoveTo(Player)
```

否则所有敌人都会：

```text
Player
  ↑
E E E
E E E
```

最终前后排全部挤到玩家身边。

推荐：

```text
Enemy AI
   ↓
Combat Slot
   ↓
Desired Position
   ↓
Movement
```

敌人的主要目标是：

> 回到自己的战斗位置。

而不是：

> 永远追着玩家身体移动。

---

# 11. Formation Slot

例如：

```text
FrontLeft
FrontCenter
FrontRight

BackLeft
BackCenter
BackRight
```

形成：

```text
        A       M       A

      S     K       S

              Player
```

每个单位绑定一个 Slot。

例如：

```text
Shield → FrontLeft
Knight → FrontCenter
Shield → FrontRight

Archer → BackLeft
Mage   → BackCenter
Archer → BackRight
```

---

# 12. 阵型的朝向

阵型应该有：

```text
FormationCenter
FormationFacing
FormationSlots
```

基础版本可以让阵型整体面向玩家。

但不建议每帧无条件旋转。

推荐：

> 当玩家进入一定角度/距离范围时，阵型才重新调整朝向。

例如：

```text
Front Arc = 120°
```

这样可以避免玩家绕着敌阵走一圈时，整个敌阵像一个机械炮台一样持续旋转。

---

# 13. 开阔地形的问题

这是该玩法最重要的设计问题之一。

如果地图完全开放：

```text
             Enemy
          E E E E E
          E E E E E

Player ─────────────→
```

玩家一定会尝试绕后。

因此不要简单依靠：

> AI 永远追着玩家转向。

而应该控制“有效战斗空间”。

---

# 14. 方案一：Combat Arena / Combat Lane

地图视觉上可以开阔，但通过：

- 悬崖
- 建筑
- 墙壁
- 河流
- 障碍物
- 不可通行区域
- 场景边界

形成隐性的战斗走廊。

例如：

```text
██████████████████

      Enemy
    E E E E E

        Player

██████████████████
```

玩家仍然感觉空间比较大，但敌人实际上拥有一个明确的战斗正面。

这是最稳定的设计方案。

---

# 15. 方案二：弧形前排

前排不一定排列成直线。

可以形成：

```text
          Enemy
       E       E
     E    E      E
```

或者：

```text
        E   E
     E   E   E
   E           E
```

这样玩家从侧面接近时，会更容易遇到前排边缘单位。

优点：

- 不需要敌人站得非常密
- 阵型更加自然
- 玩家更难从两个单位之间直接穿过
- 可以强化“防线”视觉

---

# 16. 方案三：Threat Area

前排单位可以拥有威胁区域：

```text
Shield
  \       /
   \     /
    \   /
```

玩家进入区域后：

- 被近战攻击
- 被减速
- 被控制
- 被格挡
- 受到反击

这样玩家可以绕侧面，但：

> 绕过去需要付出代价。

这比直接禁止玩家移动更加自然。

---

# 17. 核心设计原则：允许绕后，但不允许无成本绕后

推荐：

```text
正面突破   ★★★★☆
侧翼突破   ★★★☆☆
完全绕后   ★★☆☆☆
```

不要做：

> 玩家绝对不能绕后。

也不要做：

> 玩家可以轻松绕过所有前排。

应该让绕后成为一种战术选择。

---

# 18. 强制位移与空间重叠

必须考虑：

```text
Player
  ↓
Knockback
  ↓
Enemy
```

强制位移可能直接导致：

```text
Player ●
Enemy  ●
```

两个单位重叠。

因此不要假设所有移动都天然满足空间约束。

推荐移动管线：

```text
Input / AI
    ↓
Movement Intent
    ↓
Normal Movement
    ↓
Forced Movement
    ↓
Spatial Resolution
    ↓
Final Position
```

---

# 19. Spatial Resolution

所有最终位置都经过空间解析。

输入：

```text
Candidate Position
```

检查：

```text
与哪些单位重叠？
```

然后决定：

- 保持重叠
- 推开自己
- 推开对方
- 双方分摊修正
- 忽略空间约束
- 强制脱离

---

# 20. 重叠修正

假设：

```text
Player Radius = 0.5
Enemy Radius = 0.5

Distance = 0.6
```

则：

```text
Required Distance = 1.0
Penetration = 0.4
```

系统需要解决 0.4 的重叠。

如果：

```text
Player Mass = 100
Enemy Mass = 300
```

可以：

```text
Player 修正 75%
Enemy 修正 25%
```

结果：

```text
Player ←──────→ Enemy
       0.3   0.1
```

重单位少移动，轻单位多移动。

---

# 21. 强制位移可以暂时突破空间规则

例如：

```text
Boss Shockwave
```

可以直接将玩家移动。

不要在每一个强制位移阶段都阻止它。

完成强制位移后再进行：

```text
Spatial Resolution
```

这样可以支持：

- 击退
- 冲锋
- 拉拽
- 爆炸
- 推撞
- Boss 技能

而不需要为每种技能单独处理碰撞异常。

---

# 22. “阻挡”最终应该由三套系统共同产生

```text
                    Enemy Formation
                          │
             ┌────────────┼────────────┐
             ↓            ↓            ↓
        Formation      Spatial      Threat
          Slot        Blocking       Area
             │            │            │
             └────────────┼────────────┘
                          ↓
                    Player Decision
```

### Formation

决定：

> 敌人站在哪里。

### Spatial

决定：

> 玩家能不能直接穿过。

### Threat

决定：

> 即使能走过去，值不值得这么做。

这是整个玩法的核心结构。

---

# 23. 第一版 MVP

不要一开始实现完整战术 AI。

第一版只需要：

```text
Player
+
3 个前排敌人
+
2 个后排敌人
```

场景：

```text
       Archer   Mage

     Shield   Shield

          Player
```

实现：

1. Formation Slot
2. 玩家/敌人逻辑空间占用
3. 玩家无法直接穿过重型前排
4. 敌人之间简单 Separation
5. 前排近战攻击
6. 后排远程攻击
7. 玩家可以侧面绕行
8. 前排拥有 Threat Area
9. 前排死亡后产生缺口
10. 冲锋可以突破轻型单位

暂时不要实现：

- 多队伍战术
- 复杂编队重组
- 全局动态寻路
- RTS 级 AI
- 大规模单位模拟
- 完整物理模拟

---

# 24. 推荐的系统架构

```text
CombatGroup
│
├── FormationController
│   ├── FormationSlots
│   ├── FormationFacing
│   └── SlotAssignment
│
├── CombatSpace
│   ├── SpatialResolver
│   ├── Separation
│   └── ThreatArea
│
└── Enemy
    ├── EnemyAI
    ├── FormationMember
    ├── Movement
    ├── Combat
    └── Health
```

玩家：

```text
Player
├── Movement
├── Combat
├── SpatialAgent
└── ForcedMovement
```

---

# 25. 移动系统建议

最终可以抽象成：

```text
Movement Intent
       ↓
Formation Constraint
       ↓
Normal Movement
       ↓
Forced Movement
       ↓
Spatial Resolver
       ↓
Final Position
```

不要让：

```text
AI
Physics
Animation
Skill
Formation
```

全部直接修改 Transform / Position。

统一经过空间解析，可以显著降低后期出现“冲锋、击退、阵型、碰撞互相打架”的概率。

---

# 26. 性能预期

这个玩法本身不属于高性能压力系统。

粗略目标：

### 20～50 个单位

正常 PC 基本不需要担心。

### 50～150 个单位

开始注意：

- AI Tick
- Navigation
- Animation
- Physics
- Projectile
- VFX

### 200～500 个单位

才需要认真考虑：

- 分级 AI Tick
- 简化远距离 AI
- 批量空间查询
- 简化动画
- 简化 Navigation
- 避免每帧完整寻路

真正容易成为瓶颈的通常不是 Formation Slot，而是：

```text
大量 Navigation 查询
+
大量物理检测
+
大量动画
+
大量投射物
+
大量 VFX
```

---

# 27. 第一版验证标准

不要以“AI 看起来聪明”作为成功标准。

应该验证：

### 问题 1

玩家正面冲击时：

> 是否明显感觉前排是一道防线？

### 问题 2

玩家绕侧面时：

> 是否感觉绕行有成本？

### 问题 3

前排死亡时：

> 是否明显感觉敌阵被打开？

### 问题 4

使用冲锋时：

> 是否感觉自己获得了突破能力？

### 问题 5

后排攻击玩家时：

> 玩家是否产生“我要处理后排，但前排挡着我”的决策？

### 问题 6

玩家面对不同敌人组合时：

> 是否会因为敌方阵型不同而改变进攻方式？

如果这六点成立，就说明核心玩法成立。

---

# 28. 最终设计结论

这个系统不应该被定义成：

> “第一人称游戏中的敌人碰撞系统”。

更准确的定义是：

> **第一人称战术阵型战斗系统。**

它由三个核心机制组成：

```text
        战术阵型
           │
           ↓
     ┌───────────┐
     │ Formation │
     └───────────┘
           │
           ↓
   ┌───────────────┐
   │ Spatial Block │
   └───────────────┘
           │
           ↓
    ┌────────────┐
    │ Threat Area│
    └────────────┘
           │
           ↓
      玩家决策
```

其中：

> **物理/逻辑空间阻挡负责“不能轻易过去”。**

> **敌人攻击负责“即使能过去，也不值得过去”。**

> **Formation 负责“敌人为什么站在那里”。**

> **Combat Space 负责“为什么玩家不能随便绕后”。**

最终形成的核心体验是：

**玩家不是在逐个击杀一群会追自己的敌人，而是在寻找、制造和利用敌方阵型中的突破口。**

这也是整个系统最值得优先验证的玩法核心。
