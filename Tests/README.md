# GameplayTests

纯逻辑单元测试（xUnit，脱离 Godot 运行时，~15ms 跑完）。

## 运行

```bash
dotnet test Tests/GameplayTests.csproj
```

被测对象是 `Game/Gameplay/` 下的纯逻辑类（连段/受击/锥形判定/输入缓冲/蓄力/
AttackCycle/KiteBand/Formation 三件套/SpatialResolver/ForcedMovement/Ability）。
依赖 Godot Node 的接线（Player 状态机/敌人行为/HUD/shader）由引擎内冒烟测试覆盖：
`Game/Gameplay/Debug/CombatSmokeTestRunner.cs`。

```bash
# 无头冒烟（退出码 0=全过 1=失败 2=看门狗超时）
godot --headless --path . res://Game/Scenes/Debug/CombatSmokeTest.tscn
```

CI 在 `.github/workflows/ci.yml` 中依次执行：构建 → 单测 → 无头冒烟 → 格式检查。
