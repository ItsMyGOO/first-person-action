using System.Collections.Generic;
using Godot;
using GodotGameTemplate.Core;

namespace GodotGameTemplate.Combat;

/// <summary>
/// 远程敌人（M4 §4）：风筝距离带 [7,11]m——<7 后退、>11 接近、区间驻停留；
/// 驻停时走「瞄准 0.7s（红色预告线）→ 射箭 → 冷却 2.4s」循环。
/// 箭矢复用 Projectile，掩码取世界+玩家层（排除敌方层，无友伤）。
/// </summary>
public partial class RangedEnemy : EnemyAI
{
    public static readonly uint ArrowMask = 0b011; // 世界(第1层) + 玩家(第2层)

    private static readonly Color AimLineColor = new(1f, 0.12f, 0.12f);

    private readonly KiteBand _band = new(CombatTuning.RangedKiteNear, CombatTuning.RangedKiteFar);

    private MeshInstance3D _aimLine = null!;
    private float _aimElapsed;
    private float _cooldown;

    protected override void OnEnemyReady()
    {
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

        // 风筝走位（瞄准离开距离带即作废）
        KiteAction action = _band.Decide(distance);
        if (action == KiteAction.Hold)
        {
            DesiredHorizontal = Vector3.Zero;
            TickAim(dt, player);
            return;
        }

        HideAimLine();
        Vector3 dir = toPlayer / Mathf.Max(distance, 0.0001f);
        float speed = CombatTuning.RangedMoveSpeed * (action == KiteAction.Retreat ? -1f : 1f);
        DesiredHorizontal = dir * speed;
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
        if (_aimElapsed < CombatTuning.RangedAimSeconds)
        {
            return;
        }

        Fire(player);
        _aimElapsed = 0f;
        _cooldown = CombatTuning.RangedAttackCooldown;
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
            speed: 14f,
            new HitData
            {
                Damage = 10f,
                PoiseDamage = 12f,
                Knockback = direction * 2f,
                Source = EntityId.None,
            },
            gravity: 0f,
            collisionMask: ArrowMask
        );
    }

    protected override void OnDied()
    {
        _aimLine.QueueFree();
        base.OnDied();
    }
}
