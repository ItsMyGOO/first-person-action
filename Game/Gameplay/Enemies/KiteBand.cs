namespace GodotGameTemplate.Combat;

public enum KiteAction
{
    Retreat, // 玩家过近：后退拉开
    Hold, // 区间内：驻停留
    Approach, // 玩家过远：接近
}

/// <summary>
/// 风筝距离带（纯逻辑，TDD，M4 §4）：远程敌人按与玩家的距离决定走位——
/// &lt; Near 后退、&gt; Far 接近、区间内驻停留。
/// </summary>
public readonly struct KiteBand
{
    public float Near { get; }

    public float Far { get; }

    public KiteBand(float near, float far)
    {
        Near = near;
        Far = far;
    }

    public KiteAction Decide(float distance) =>
        distance < Near ? KiteAction.Retreat
        : distance > Far ? KiteAction.Approach
        : KiteAction.Hold;
}
