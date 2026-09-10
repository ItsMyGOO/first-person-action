using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotGameTemplate.Combat;

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
/// （文档「死亡产生缺口」，MVP 不重组）。
/// </summary>
public sealed class FormationRoster
{
    private readonly List<IFormationMember> _members = new();

    public void Assign(IFormationMember member) => _members.Add(member);

    public IReadOnlyList<IFormationMember> Members => _members;

    /// <summary>存活成员（死亡成员所在槽位自然开缺口）。</summary>
    public IEnumerable<IFormationMember> Living => _members.Where(m => m.Alive);
}
