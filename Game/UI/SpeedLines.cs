using FirstPersonAction.Combat;
using Godot;

namespace FirstPersonAction.UI;

/// <summary>
/// 冲刺速度线（M5 §2，纯表现层）：挂 Hud CanvasLayer 顶层的全屏 ColorRect，
/// Material 为程序化径向速度线 shader + 暗角；每帧从 CameraFeel 读
/// max(冲锋混合度, 滞空混合度) 写 uniform，不新增模拟状态。
/// </summary>
public partial class SpeedLines : ColorRect
{
    private ShaderMaterial _material = null!;
    private CameraFeel? _feel;

    /// <summary>本帧写入 uniform 的强度（冒烟断言用）。</summary>
    public float CurrentIntensity { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Game/UI/SpeedLines.gdshader"),
        };
        _material.SetShaderParameter("density", CombatTuning.SpeedLineDensity);
        _material.SetShaderParameter("speed", CombatTuning.SpeedLineScrollSpeed);
        _material.SetShaderParameter("vignette", CombatTuning.SpeedVignetteStrength);
        Material = _material;
    }

    public override void _Process(double delta)
    {
        _feel ??= GetTree().GetFirstNodeInGroup("camera_feel") as CameraFeel;
        float raw = _feel?.SpeedIntensity ?? 0f;
        CurrentIntensity = Mathf.Min(raw, CombatTuning.SpeedLinesMaxIntensity);
        _material.SetShaderParameter("intensity", CurrentIntensity);
    }
}
