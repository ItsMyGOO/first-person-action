using System.Globalization;
using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 技能栏纯逻辑（M8）：秒数格式化与冷却比例。UI 只做展示，规则在这里可单测。
/// </summary>
public static class SkillBarLogic
{
    /// <summary>冷却剩余秒数文本：≤0 空串（就绪）；≥10s 向上取整为整数；否则一位小数。
    /// InvariantCulture：CI/Linux 上不产生逗号小数点。</summary>
    public static string FormatSeconds(float remaining) =>
        remaining <= 0f
            ? ""
            : remaining < 10f
                ? remaining.ToString("F1", CultureInfo.InvariantCulture)
                : Mathf.Ceil(remaining).ToString(CultureInfo.InvariantCulture);

    /// <summary>冷却进度 0（就绪）~1（刚施放）；total≤0 视为无 CD 恒就绪。</summary>
    public static float Cooldown01(float remaining, float total) =>
        total <= 0f ? 0f : Mathf.Clamp(remaining / total, 0f, 1f);
}
