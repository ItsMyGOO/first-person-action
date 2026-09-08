using System;

namespace GodotGameTemplate.Core;

/// <summary>
/// 稳定的逻辑实体标识。模拟层一切跨实体引用都通过它而非节点引用，
/// 为联机同步（实体按 ID 寻址）预留——规格第 6 节第 1 条。
/// </summary>
public readonly struct EntityId : IEquatable<EntityId>
{
    public readonly ulong Value;

    public EntityId(ulong value) => Value = value;

    public bool IsNone => Value == 0;

    public static EntityId None => default;

    public bool Equals(EntityId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => $"Entity({Value})";

    public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

    public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
}
