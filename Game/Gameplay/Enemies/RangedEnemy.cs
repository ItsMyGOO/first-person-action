using System.Collections.Generic;
using FirstPersonAction.Core;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 远程敌人（M4 §4）：风筝距离带 [7,11]m——<7 后退、>11 接近、区间驻停留；
/// 驻停时走「瞄准 0.7s（红色预告线）→ 射箭 → 冷却 2.4s」循环。
/// 箭矢复用 Projectile，掩码取世界+玩家层（排除敌方层，无友伤）。
/// M5 §1.3：「想去哪」抽为虚方法 MoveIntent（默认=风筝距离带，行为不变），
/// 阵型后排变体覆写为槽位驻守；瞄准/射击链路（虚成员可调参）保持复用。
/// </summary>
public partial class RangedEnemy : EnemyAI
{
    public static readonly uint ArrowMask = PhysicsLayers.EnemyProjectileMask; // 世界+玩家层（无友伤）

    private static readonly Color AimLineColor = new(1f, 0.12f, 0.12f);

    private KiteBand _band; // OnEnemyReady 时按数值定义构建（结构体，使用前必经 _Ready）
    private MeshInstance3D _aimLine = null!;
    private float _aimElapsed;
    private float _cooldown;

    /// <summary>瞄准预告时长（数值定义驱动）。</summary>
    protected virtual float AimSeconds => Def.AimSeconds;

    /// <summary>射击冷却（数值定义驱动）。</summary>
    protected virtual float AttackCooldownSeconds => Def.RangedAttackCooldown;

    protected override void OnEnemyReady()
    {
        _band = new KiteBand(Def.KiteNear, Def.KiteFar);

        // 瞄准预告线（占位：细长红盒，TopLevel 世界系摆放，长度=与目标距离）
        _aimLine = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.03f, 1f) },
            Visible = false,
            TopLevel = true,
        };
        _aimLine.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = AimLineColor,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        AddChild(_aimLine);
    }

    /// <summary>移动意图（虚方法）：默认按风筝距离带给出水平速度，驻留返回零向量。</summary>
    protected virtual Vector3 MoveIntent(Player player, Vector3 toPlayer, float distance)
    {
        KiteAction action = _band.Decide(distance);
        if (action == KiteAction.Hold)
        {
            return Vector3.Zero;
        }

        Vector3 dir = toPlayer / Mathf.Max(distance, 0.0001f);
        float speed = Def.MoveSpeed * (action == KiteAction.Retreat ? -1f : 1f);
        return dir * speed;
    }

    protected override void TickActive(float dt)
    {
        Player? player = TargetPlayer;
        if (player is not ICombatTarget { CanBeHit: true })
        {
            DesiredHorizontal = Vector3.Zero;
            HideAimLine();
            return;
        }

        Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
        toPlayer.Y = 0f;
        float distance = toPlayer.Length();
        FaceTowards(player.GlobalPosition);

        // 风筝走位（瞄准随移动作废——与 M4 行为一致：移动中不瞄准）
        Vector3 intent = MoveIntent(player, toPlayer, distance);
        DesiredHorizontal = intent;
        if (intent != Vector3.Zero)
        {
            HideAimLine();
            return;
        }

        TickAim(dt, player);
    }

    private void TickAim(float dt, Player player)
    {
        if (_cooldown > 0f)
        {
            _cooldown -= dt;
            HideAimLine();
            return;
        }

        _aimElapsed += dt;
        ShowAimLine(player);
        if (_aimElapsed < AimSeconds)
        {
            return;
        }

        Fire(player);
        _aimElapsed = 0f;
        _cooldown = AttackCooldownSeconds;
        HideAimLine();
    }

    private void ShowAimLine(Player player)
    {
        Vector3 origin = Center;
        Vector3 target = ((ICombatTarget)player).Center;
        Vector3 to = target - origin;
        float length = to.Length();
        if (length < 0.01f)
        {
            HideAimLine();
            return;
        }

        Vector3 dir = to / length;
        _aimLine.Visible = true;
        _aimLine.GlobalPosition = origin + dir * (length / 2f);
        _aimLine.LookAt(target, Vector3.Up);
        _aimLine.Scale = new Vector3(1f, 1f, length);
    }

    private void HideAimLine()
    {
        _aimElapsed = 0f;
        _aimLine.Visible = false;
    }

    private void Fire(Player player)
    {
        Vector3 origin = Center;
        Vector3 direction = (((ICombatTarget)player).Center - origin).Normalized();
        Projectile.Spawn(
            this,
            origin + direction * 0.5f,
            direction,
            speed: Def.ArrowSpeed,
            NewArrowHit(direction),
            gravity: 0f,
            collisionMask: ArrowMask
        );
    }

    /// <summary>箭矢伤害数据（数值定义驱动，子类可覆写特殊箭）。</summary>
    protected virtual HitData NewArrowHit(Vector3 direction) =>
        new()
        {
            Damage = Def.ArrowDamage,
            PoiseDamage = Def.ArrowPoiseDamage,
            Knockback = direction * Def.ArrowKnockback,
            Source = EntityId.None,
        };

    protected override void OnDied()
    {
        _aimLine.QueueFree();
        base.OnDied();
    }
}
