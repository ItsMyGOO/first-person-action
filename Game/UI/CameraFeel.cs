using FirstPersonAction.Combat;
using Godot;

namespace FirstPersonAction.UI;

/// <summary>
/// 相机手感组件（M4 §5，纯表现层）：接管 FOV 与震动——
/// 冲锋速度感（FOV +10 快进慢出 + 高频小幅抖动 + viewmodel 后拉由 Player 表现层处理）、
/// 跳劈力量感（起跳 FOV -6 + 上仰小踢、滞空微收）、
/// 落地重量感（震动爆发 0.4 / 0.35s 衰减 + 相机下沉回弹 + ShockwaveRing 冲击波环）。
/// 只读玩家状态 / 订阅事件，不回写模拟（规格第 1 节）。挂 Player 相机子节点。
/// </summary>
public partial class CameraFeel : Node
{
    private Camera3D _camera = null!;
    private Player? _player;

    private float _chargeBlend; // 冲锋 FOV 拉伸（快进慢出）
    private float _leapBlend; // 跳劈滞空收束
    private float _shake; // 震动幅度（爆发后衰减）
    private float _landSink; // 落地下沉（1→0 回弹）
    private float _kickPitch; // 起跳上仰小踢

    public override void _Ready()
    {
        AddToGroup("camera_feel");
        _camera = GetParent<Camera3D>();
    }

    /// <summary>速度线驱动强度：max(冲锋混合度, 滞空混合度)，复用既有渐入渐出（M5 §2）。</summary>
    public float SpeedIntensity => Mathf.Max(_chargeBlend, _leapBlend);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_player == null)
        {
            _player = GetTree().GetFirstNodeInGroup(CombatTuning.PlayerGroup) as Player;
            if (_player == null)
            {
                return;
            }

            _player.SkillStarted += OnSkillStarted;
            _player.LeapLanded += OnLeapLanded;
        }

        bool dashing = _player.IsChargeDashing;
        bool airborne = _player.IsLeapAirborne;

        _chargeBlend = dashing
            ? Mathf.MoveToward(_chargeBlend, 1f, 14f * dt) // 快进
            : Mathf.MoveToward(_chargeBlend, 0f, 3.5f * dt); // 慢出
        _leapBlend = airborne
            ? Mathf.MoveToward(_leapBlend, 1f, 10f * dt)
            : Mathf.MoveToward(_leapBlend, 0f, 6f * dt);
        _shake = Mathf.MoveToward(
            _shake,
            0f,
            CombatTuning.LeapLandShake / CombatTuning.LeapLandShakeDecaySeconds * dt
        );
        _landSink = Mathf.MoveToward(_landSink, 0f, 6f * dt);
        _kickPitch = Mathf.MoveToward(_kickPitch, 0f, 0.4f * dt);

        float aimBlend = _player.AimBlend01;
        _camera.Fov =
            Mathf.Lerp(CombatTuning.BaseFov, CombatTuning.AimFov, aimBlend)
            + CombatTuning.ChargeFovKick * _chargeBlend
            - CombatTuning.LeapFovDrop * _leapBlend;

        float shake = _shake + (dashing ? CombatTuning.ChargeShakeAmplitude : 0f);
        var jitter = new Vector3(
            (float)GD.RandRange(-1.0, 1.0),
            (float)GD.RandRange(-1.0, 1.0),
            0f
        );
        Vector3 shakeOffset = jitter * shake;
        _camera.Position =
            new Vector3(Mathf.Lerp(0f, CombatTuning.AimShoulderX, aimBlend), 0f, 0f)
            + shakeOffset
            + new Vector3(0f, -_landSink * CombatTuning.LandSinkMeters, 0f);
        _camera.Rotation = new Vector3(_kickPitch, 0f, 0f);
    }

    private void OnSkillStarted(SkillKind kind)
    {
        if (kind == SkillKind.LeapSlam)
        {
            _kickPitch = CombatTuning.LeapKickPitchRad; // 起跳上仰小踢
            _leapBlend = 0f;
        }
    }

    private void OnLeapLanded()
    {
        _shake = CombatTuning.LeapLandShake;
        _landSink = 1f;
        SpawnShockwave();
    }

    /// <summary>ShockwaveRing（代码生成，无美术资产）：TorusMesh 0.3→3.2m 扩散淡出，0.4s 后自毁。</summary>
    private void SpawnShockwave()
    {
        if (_player == null)
        {
            return;
        }

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.9f, 0.6f, 0.85f),
        };
        var ring = new MeshInstance3D
        {
            Name = "ShockwaveRing",
            Mesh = new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1.08f },
            MaterialOverride = material,
        };
        _player.GetTree().CurrentScene.AddChild(ring);
        ring.GlobalPosition = _player.GlobalPosition + Vector3.Down * 0.75f;
        ring.Scale = new Vector3(0.3f, 0.3f, 0.3f);
        Tween tween = ring.CreateTween();
        tween.TweenProperty(ring, "scale", new Vector3(3.2f, 1f, 3.2f), 0.4f);
        tween.Parallel().TweenProperty(material, "albedo_color:a", 0f, 0.4f);
        tween.TweenCallback(Callable.From(ring.QueueFree));
    }
}
