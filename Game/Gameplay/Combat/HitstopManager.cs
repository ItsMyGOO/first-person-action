using Godot;

namespace FirstPersonAction.Combat;

/// <summary>
/// 全局 hit-stop（规格第 4 节）：命中瞬间极短时间缩放，既是手感核心也是
/// M4 处决位置修正的遮罩。autoload 单例；token 防止旧计时器提前恢复时间流。
/// </summary>
public partial class HitstopManager : Node
{
    private static HitstopManager? _instance;
    private static int _token;

    private const float SlowTimeScale = 0.05f;

    public override void _Ready() => _instance = this;

    public static void Request(int durationMs)
    {
        if (_instance == null)
        {
            return;
        }

        int token = ++_token;
        Engine.TimeScale = SlowTimeScale;
        // ignoreTimeScale=true 的计时器负责恢复（否则自身也被放慢、永远不会到时）
        SceneTreeTimer timer = _instance
            .GetTree()
            .CreateTimer(durationMs / 1000.0, true, false, true);
        timer.Timeout += () =>
        {
            if (token == _token)
            {
                Engine.TimeScale = 1.0f;
            }
        };
    }
}
