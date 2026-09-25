using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 阵型朝向滞回（M5 §1.1，文档 §12）：玩家方向与当前阵型朝向偏差超过
/// RotateThresholdDeg 才开始转动，以固定角速度匀速转向目标角，对齐后保持——
/// 玩家绕阵走圈时阵型不做炮塔式跟转。纯逻辑，可单元测试。
/// </summary>
public sealed class FormationBrain
{
    public const float DefaultRotateThresholdDeg = 55f;
    public const float DefaultRotateSpeedDegPerSec = 90f;

    private const float Epsilon = 0.0005f;

    private readonly float _thresholdDeg;
    private readonly float _speedDegPerSec;
    private bool _rotating;

    public FormationBrain(
        float rotateThresholdDeg = DefaultRotateThresholdDeg,
        float rotateSpeedDegPerSec = DefaultRotateSpeedDegPerSec
    )
    {
        _thresholdDeg = rotateThresholdDeg;
        _speedDegPerSec = rotateSpeedDegPerSec;
    }

    /// <summary>当前阵型朝向（度；0 = 面向 -Z，与 Godot yaw 约定一致）。</summary>
    public float YawDeg { get; private set; }

    public void Reset(float yawDeg)
    {
        YawDeg = yawDeg;
        _rotating = false;
    }

    /// <summary>按滞回规则推进朝向（formationCenter 为阵型中心世界坐标）。</summary>
    public void Tick(float dt, Vector3 formationCenter, Vector3 playerPosition)
    {
        Vector3 to = playerPosition - formationCenter;
        to.Y = 0f;
        if (to.LengthSquared() < 1e-6f)
        {
            return;
        }

        float targetDeg = Mathf.RadToDeg(Mathf.Atan2(-to.X, -to.Z)); // 与 EnemyAI.FaceTowards 同约定
        float diff = Mathf.Wrap(YawDeg - targetDeg, -180f, 180f); // 当前相对目标的短弧偏差

        if (!_rotating)
        {
            if (Mathf.Abs(diff) <= _thresholdDeg)
            {
                return; // 阈值内不转
            }

            _rotating = true; // 越阈：开始转向玩家方向
        }

        float step = _speedDegPerSec * Mathf.Max(0f, dt);
        if (Mathf.Abs(diff) <= step + Epsilon)
        {
            YawDeg -= diff; // 转到位即停（保持角度连续，不做 ±180 绕回）
            _rotating = false;
            return;
        }

        YawDeg += diff > 0f ? -step : step; // 沿短弧匀速推进
    }
}
