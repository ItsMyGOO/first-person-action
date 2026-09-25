# 2026-09-25 商业级审查修复（两批）

对全部代码/测试/工程配置做商业级标准审查后，按优先级修复的完整记录。
审查结论原文：架构方向正确、无 God class、纯逻辑单测意识好，但「接口代替实现」
的断线机制多、质量门禁不存在、工程配置是模板壳。

## 第一批：P0/P1/P2 主线

| 项 | 修复 | 验证 |
|---|---|---|
| CI 门禁 | `.github/workflows/ci.yml`：构建+单测 → 无头冒烟（自动下载 Godot 4.6.1 .NET）→ csharpier check | 推送后生效 |
| 冒烟假绿 | 每阶段 try/catch、240s 看门狗（退出码 2）、执行数下限守卫、键位备份/还原、反射→`DebugYaw/DebugPitch` 测试缝 | 冒烟 65 项退出码 0 |
| 闪避无敌帧断线 | `CanBeHit` 接入 `IsInvulnerable`；`ApplyHit` 双保险 | 冒烟 +4 项 |
| 霸体死代码 | 受击打断规则：霸体中不打断；无霸体打断连段/瞄准/施法。旋风斩补 `SuperArmor=true`（人群技设计决定） | 冒烟 +4 项 |
| 玩家自伤巧合守卫 | `MeleeArcQuery.FindHits(exclude:)` 显式排除攻击源 | 单测 +2 |
| SlotIndex 越界崩溃 | `FormationRoster.Assign` 校验越界/重复，控制器 PushError 拒激活 | 单测 +3 |
| 资源加载容错 | GameSession 兜底定义 + SkillIds 枚举校验 | 启动报错可读 |
| ESC 输入死锁 | `PauseMenu` autoload（暂停/继续/重开/退出 + 鼠标重捕获） | 实机 |
| 工程配置模板壳 | export_presets 入库；project.godot 补 display/rendering/audio/layer_names 四段；音频总线 Music/SFX | 导出可用 |
| 物理层裸位 | `PhysicsLayers` 常量 + layer_names 命名 | grep 无裸位 |
| 热路径分配 | 输入缓冲去闭包、SpatialSystem 池化（顺带修复 SpatialExempt 未复制）、FormationRoster 去 LINQ、判定缓冲复用、箭矢静态共享资源 | 代码审阅 |
| 箭矢穿墙 | 每物理帧 CCD 射线扫掠（44m/s 不再隧穿） | 代码审阅 |
| 暴力死亡重载 | 死亡状态机：冻结→低垂→1.5s 重载（守卫卸载竞态） | 冒烟 +3 项 |
| 模板命名残留 | `GodotGameTemplate → FirstPersonAction`（csproj/assembly/命名空间/测试引用） | 构建/测试/冒烟全绿 |

## 第二批：次级项

- **DummyEnemy 去重**：改继承 `EnemyAI`（此前 ~80 行逐行重复且已漂移——木桩受击不查
  CanBeHit）。差异收敛为表现：灰色涂装、受击闪白、倒地躺平（新增基类 `TickPresentation` 钩子）。
- **数值收敛**：`FireArrow` 的箭速 26/出膛 0.4/韧性 ×2/击退 1.5 → `CharacterDefinition.QuickShotSpeed`
  + `CombatTuning.Arrow*`；`TickViewModel` 位姿常量 → `CombatTuning.ViewModel*`。策划调参入口统一。
- **选人数据驱动闭环**：`CharacterSelect` 扫描 `Game/Config/Characters/*.tres` 动态生成按钮，
  加角色不再改 UI 代码。
- **手柄支持**：移动/跳跃/攻击(X/RT)/瞄准(LT)/闪避(B)/技能(Y/LB/RB) + 右摇杆视角
  （look_* 动作 + `GamepadLookSpeedRad` 轮询）。KeybindManager「保留手柄绑定」的注释自此为真。
- **订阅纪律**：CameraFeel 绑定/换绑/退出统一退订；Player `_ExitTree` 退订 `Health.Died`。
- **联机预留标记**：`SeededRandom` 打 `[Obsolete]`（仅单测使用）；`EntityId` 注释标明当前仅 None 占位。

## 设计决定记录

1. **旋风斩霸体**：`AbilityFactory` 数据改动，回退只改一行。
2. **受击打断规则**：霸体=只扣血；无霸体=打断连段/瞄准/施法（无硬直位移，留打磨期）。
3. **PauseMenu 用 autoload**：跨场景常驻，ProcessMode=Always 处理暂停中的输入。

## 验证口径

`dotnet build` 0 警告 0 错误；`dotnet test` 76/76；无头冒烟 65/65（退出码 0）；
`csharpier check` 通过。

## 后续批次（2026-09-25 追加完成）

- **M6 处决**（`3ad1503`）：规格 §5 全流程——45°锥+0.8~1.8m 谓词/HUD提示/
  锁定-收敛-冲击-结算/回血 15%/中止边界。单测 +10、冒烟 +11。
- **键位设置 UI**（`157d878`）：重绑捕获/保存/恢复默认闭环。冒烟 +7。
- **M7 刺客**（`db8ba95`）：DashThroughAbility 疾行穿人（空间豁免）+ Assassin.tres
  （选人零代码出现）+ IsDashing 冲刺表现泛化。单测 +2、冒烟 +7。
- **敌人数值 .tres 化**（本批）：EnemyDefinition 资源 + 九个 Game/Config/Enemies/*.tres，
  五个敌人类全部改读定义；CombatTuning 删 32 个敌人常量（保留系统参数）；
  顺带修正 DummyEnemy 并入基类时丢失的木桩默认体格（0.5/250/250）。

## 未处理（后续里程碑）

- 音频/美术内容生产（管线已就绪：总线、gitattributes、目录）
