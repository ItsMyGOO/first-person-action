using Godot;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 阵型控制器（M5 §1.2，仿 EnemyGroup 激活模式）：玩家进入半径聚合激活全体成员；
/// 每物理帧 FormationBrain 更新朝向（滞回）→ FormationLayout 换算各槽位世界坐标 →
/// 写给存活成员（SlotPosition / SlotFacing）。死亡成员槽位悬空（MVP 不重组）。
/// 阵型成员为直接子节点，各自导出 SlotIndex。
/// </summary>
public partial class FormationController : Node3D
{
    [Export]
    public bool InitiallyActive = false;

    [Export]
    public float ActivateRadius = CombatTuning.EnemyGroupActivateRadius;

    private readonly FormationBrain _brain = new();
    private readonly FormationRoster _roster = new();
    private bool _activated;

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            if (child is IFormationMember member)
            {
                _roster.Assign(member);
            }
        }

        if (InitiallyActive)
        {
            SetActive(true);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Player? player = GetTree().GetFirstNodeInGroup(CombatTuning.PlayerGroup) as Player;

        if (!_activated)
        {
            if (
                player is ICombatTarget { CanBeHit: true }
                && GlobalPosition.DistanceTo(player.GlobalPosition) <= ActivateRadius
            )
            {
                SetActive(true);
            }

            return;
        }

        // 朝向滞回推进 → 槽位世界坐标 → 写给存活成员
        if (player is ICombatTarget { CanBeHit: true })
        {
            _brain.Tick(dt, GlobalPosition, player.GlobalPosition);
        }

        float yawRad = Mathf.DegToRad(_brain.YawDeg);
        foreach (IFormationMember member in _roster.Living)
        {
            member.SlotPosition = FormationLayout.SlotWorld(
                GlobalPosition,
                yawRad,
                member.SlotIndex
            );
            member.SlotFacing = yawRad;
        }
    }

    private void SetActive(bool active)
    {
        _activated = active;
        if (active)
        {
            // 激活瞬间先面向玩家（避免初始朝向背对玩家）
            Player? player = GetTree().GetFirstNodeInGroup(CombatTuning.PlayerGroup) as Player;
            if (player != null)
            {
                Vector3 to = player.GlobalPosition - GlobalPosition;
                to.Y = 0f;
                if (to.LengthSquared() > 1e-6f)
                {
                    _brain.Reset(Mathf.RadToDeg(Mathf.Atan2(-to.X, -to.Z)));
                }
            }
        }

        foreach (Node child in GetChildren())
        {
            if (child is EnemyAI enemy)
            {
                enemy.Active = active;
            }
        }
    }
}
