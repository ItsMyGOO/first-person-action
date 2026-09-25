using System.Collections.Generic;

namespace FirstPersonAction.Core;

/// <summary>
/// 动作输入缓冲：动作命令压入后 BufferWindowMs 内有效，被消费或过期即清除。
/// 消费端用「条件满足才 TryConsume」的模式实现缓冲——条件不满足时命令留在
/// 缓冲里等待（如取消窗口打开瞬间接上连段）。时间由调用方传入（毫秒），
/// 便于测试与将来的联机时钟。
/// </summary>
public sealed class InputCommandBuffer
{
    private struct Entry
    {
        public InputCommand Command;
        public long PushedAtMs;
    }

    private readonly List<Entry> _entries = new List<Entry>();

    public int BufferWindowMs { get; set; } = 150;

    public void Push(in InputCommand command, long nowMs)
    {
        if (command.Kind is InputCommandKind.None or InputCommandKind.Move)
        {
            return;
        }

        _entries.Add(new Entry { Command = command, PushedAtMs = nowMs });
    }

    /// <summary>取出最早的一条匹配命令；过期条目顺带清除。无匹配返回 false。</summary>
    public bool TryConsume(InputCommandKind kind, long nowMs, out InputCommand command)
    {
        command = default;

        // 热路径（每物理帧被调用多次）手写倒序遍历，避免 lambda 闭包的委托分配
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (nowMs - _entries[i].PushedAtMs > BufferWindowMs)
            {
                _entries.RemoveAt(i);
            }
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Command.Kind != kind)
            {
                continue;
            }

            command = _entries[i].Command;
            _entries.RemoveAt(i);
            return true;
        }

        return false;
    }

    public void Clear() => _entries.Clear();
}
