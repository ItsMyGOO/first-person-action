using System.Collections.Generic;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 群体 AI（M4 §4）：聚合激活——玩家进入半径内全体敌人开始行动（不写行为树）；
/// 每 0.4s 经 SurroundSlotAssigner 重算低级兵环绕槽位（按围攻者集合稳定排序防抖）。
/// 敌人节点挂在本节点下作为子节点。
/// </summary>
public partial class EnemyGroup : Node3D
{
    [Export]
    public bool InitiallyActive = false;

    [Export]
    public float ActivateRadius = CombatTuning.EnemyGroupActivateRadius;

    private bool _activated;
    private float _slotTimer;

    public override void _Ready()
    {
        if (InitiallyActive)
        {
            SetActive(true);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (!_activated)
        {
            Player? player = GetTree().GetFirstNodeInGroup(CombatTuning.PlayerGroup) as Player;
            if (
                player is ICombatTarget { CanBeHit: true }
                && GlobalPosition.DistanceTo(player.GlobalPosition) <= ActivateRadius
            )
            {
                SetActive(true);
            }

            return;
        }

        _slotTimer -= dt;
        if (_slotTimer <= 0f)
        {
            _slotTimer = CombatTuning.SlotReassignInterval;
            ReassignSlots();
        }
    }

    private void SetActive(bool active)
    {
        _activated = active;
        foreach (Node child in GetChildren())
        {
            if (child is EnemyAI enemy)
            {
                enemy.Active = active;
            }
        }
    }

    private void ReassignSlots()
    {
        var attackers = new List<long>();
        var soldiers = new List<SwarmSoldier>();
        foreach (Node child in GetChildren())
        {
            if (child is SwarmSoldier soldier && soldier.Active && soldier.CanBeHit)
            {
                attackers.Add((long)soldier.GetInstanceId());
                soldiers.Add(soldier);
            }
        }

        float[] angles = SurroundSlotAssigner.Assign(attackers);
        for (int i = 0; i < soldiers.Count; i++)
        {
            soldiers[i].SlotAngle = angles[i];
        }
    }
}
