using System.Collections.Generic;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>阵型成员契约：由 FormationController 每帧写入槽位世界坐标与朝向。</summary>
public interface IFormationMember
{
    int SlotIndex { get; }

    bool Alive { get; }

    /// <summary>本帧槽位世界坐标（控制器写入，成员行为读取）。</summary>
    Vector3 SlotPosition { get; set; }

    /// <summary>本帧阵型朝向（弧度；成员警戒朝向用）。</summary>
    float SlotFacing { get; set; }
}

/// <summary>
/// 槽位驻守表（M5 §1.1）：成员死亡后其槽位悬空——不重分配、不补位
/// （文档「死亡产生缺口」，MVP 不重组）。Assign 校验槽位号（越界/重复拒绝），
/// 非法配置在控制器层报错，绝不带病进入每帧槽位换算。
/// </summary>
public sealed class FormationRoster
{
    private readonly List<IFormationMember> _members = new();
    private readonly HashSet<int> _usedSlots = new();
    private readonly List<IFormationMember> _living = new();

    /// <summary>登记成员；槽位号越界或与已登记成员重复时拒绝并返回 false。</summary>
    public bool Assign(IFormationMember member)
    {
        if (!FormationLayout.IsValidSlot(member.SlotIndex) || !_usedSlots.Add(member.SlotIndex))
        {
            return false;
        }

        _members.Add(member);
        return true;
    }

    public IReadOnlyList<IFormationMember> Members => _members;

    /// <summary>存活成员（死亡成员所在槽位自然开缺口）。返回内部复用列表，勿跨帧持有。</summary>
    public IReadOnlyList<IFormationMember> Living
    {
        get
        {
            _living.Clear();
            foreach (IFormationMember member in _members)
            {
                if (member.Alive)
                {
                    _living.Add(member);
                }
            }

            return _living;
        }
    }
}
