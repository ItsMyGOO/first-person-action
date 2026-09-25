# 项目约定（给编码代理）

## 完成即提交

做完一个完整的工作单元（一批修复 / 一个功能 / 一次回归通过）后**自动提交**，
不需要再询问用户。规则：

1. **提交前必须验证**（这套项目的质量门禁）：
   - `dotnet build FirstPersonAction.csproj` —— 0 警告 0 错误
   - `dotnet test Tests/GameplayTests.csproj` —— 全部通过
   - 冒烟（涉及玩法/场景/输入改动时）：
     `"D:\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64_console.exe" --headless --path . res://Game/Scenes/Debug/CombatSmokeTest.tscn`
     退出码必须为 0
   - 改过 C# 代码后先跑 `~/.dotnet/tools/csharpier.exe format .`
2. **一个逻辑批次一个 commit**：多批工作分开提交，每个 commit 独立可编译、
   独立过测试（混合文件用备份+反向编辑构造中间态，提交后恢复）。
3. **提交信息**：Conventional Commits + 中文，主题行带验证口径
   （如"单测76+冒烟65全绿"），正文列明细。参考 `git log` 既有风格。
4. 只在用户明确要求时 push。

## 其他

- 冒烟测试改动了 `Check(` 数量时，确认 `MinExecutedChecks` 下限仍合理。
- 加了新 .cs 脚本后跑一次 `godot --headless --path . --import` 生成 .uid 并入库。
- 数值调参进 `CombatTuning` / `CharacterDefinition`（.tres），不在热代码里写裸数字。
- 物理层引用 `PhysicsLayers` 常量，禁止裸位掩码。
