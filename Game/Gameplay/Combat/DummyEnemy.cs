using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 木桩敌人：验证打击感与受击状态的最小敌人（规格 M2）。
/// 商业化重构：改继承 EnemyAI，与正式敌人共用受击/强制位移/死亡管线——
/// 此前与基类逐行重复且已漂移（木桩受击不查 CanBeHit）。
/// 差异只剩表现：灰色涂装、受击闪白（正式敌人闪红）、倒地躺平/恢复立起。
/// 永不主动行为：Active 恒为 false，TickActive 为空实现。
/// </summary>
public partial class DummyEnemy : EnemyAI
{
    private static readonly Color DummyFlashColor = new(1f, 0.9f, 0.8f);

    public DummyEnemy()
    {
        Tint = new Color(0.55f, 0.58f, 0.62f); // 灰色涂装默认值（场景未覆盖 Tint 时生效）
    }

    public bool IsDowned => Reaction.IsDowned; // M4 处决条件从此读取

    /// <summary>受击闪白（与正式敌人的闪红区分，其余颜色逻辑同基类）。</summary>
    protected override Color DisplayColor => IsFlashing ? DummyFlashColor : base.DisplayColor;

    protected override void TickActive(float dt)
    {
        DesiredHorizontal = Vector3.Zero; // 木桩无行为
    }

    /// <summary>倒地躺平/恢复立起（占位表现，正式版换 Ragdoll——规格第 5 节）。</summary>
    protected override void TickPresentation(float dt)
    {
        Vector3 meshRot = Mesh.RotationDegrees;
        meshRot.X = Mathf.MoveToward(meshRot.X, Reaction.IsDowned ? -90f : 0f, 360f * dt);
        Mesh.RotationDegrees = meshRot;
    }
}
