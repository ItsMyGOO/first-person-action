using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 阵型后排弓手/法师（M5 §1.3）：覆写移动意图为「槽位点站定」，
/// 完整保留基类瞄准红线 → Projectile.Spawn → 冷却链路；
/// 伤害/冷却经导出参数可调（弓默认同 M4 远程；法师场景改 Tint/伤害/冷却）。
/// </summary>
public partial class FormationArcher : RangedEnemy, IFormationMember
{
    [Export]
    public int SlotIndex { get; set; }

    [Export]
    public float ArrowDamage = 10f;

    [Export]
    public float ArrowPoiseDamage = 12f;

    [Export]
    public float ArrowCooldownSeconds = CombatTuning.RangedAttackCooldown;

    public bool Alive => CanBeHit;

    public Vector3 SlotPosition { get; set; }

    public float SlotFacing { get; set; }

    protected override float AttackCooldownSeconds => ArrowCooldownSeconds;

    protected override void OnEnemyReady()
    {
        base.OnEnemyReady();
        // 未被控制器接管前，槽位即出生点
        SlotPosition = GlobalPosition;
        SlotFacing = Rotation.Y;
    }

    /// <summary>槽位驻守：回槽 → 站定（站定后交回基类瞄准/射击链路）。</summary>
    protected override Vector3 MoveIntent(Player player, Vector3 toPlayer, float distance)
    {
        Vector3 toSlot = SlotPosition - GlobalPosition;
        toSlot.Y = 0f;
        float slotDist = toSlot.Length();
        if (slotDist <= CombatTuning.FormationSlotSnapDist)
        {
            return Vector3.Zero;
        }

        return toSlot / Mathf.Max(slotDist, 0.0001f) * CombatTuning.FormationMoveSpeed;
    }

    protected override HitData NewArrowHit(Vector3 direction) =>
        new()
        {
            Damage = ArrowDamage,
            PoiseDamage = ArrowPoiseDamage,
            Knockback = direction * 2f,
            Source = FirstPersonAction.Core.EntityId.None,
        };
}
