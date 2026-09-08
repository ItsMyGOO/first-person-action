namespace GodotGameTemplate.Combat;

/// <summary>
/// 单段普攻数据。v1 为纯 C# 类（常量表），M3 技能框架落地时迁移为 Resource。
/// </summary>
public sealed class ComboStageData
{
    public float Startup; // 前摇时长（秒）
    public float Active; // 主动（判定）时长
    public float Recovery; // 后摇时长
    public float HitFrom; // 命中窗口起点（Active 阶段内相对时间）
    public float HitTo; // 命中窗口终点
    public float Damage;
    public float PoiseDamage; // 韧性伤害（驱动敌人受击状态机）
    public float ForwardStep; // 前摇期间的前移距离（米，提供突进感）
    public float CancelAfter; // 后摇经过该时间后可被下一段/闪避取消
    public float Knockback = 2f; // 命中击退的水平初速（米/秒）
}
