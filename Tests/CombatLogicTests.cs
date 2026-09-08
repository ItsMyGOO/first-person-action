using Godot;
using GodotGameTemplate.Core;
using Xunit;

namespace GodotGameTemplate.Tests;

public class InputCommandBufferTests
{
    private static InputCommand Cmd(InputCommandKind kind) => new InputCommand { Kind = kind };

    [Fact]
    public void Consume_ReturnsEarliestMatchingCommand()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        buf.Push(Cmd(InputCommandKind.Attack), 20);

        Assert.True(buf.TryConsume(InputCommandKind.Attack, 30, out var first));
        Assert.Equal(InputCommandKind.Attack, first.Kind);
        Assert.True(buf.TryConsume(InputCommandKind.Attack, 30, out _));
        Assert.False(buf.TryConsume(InputCommandKind.Attack, 30, out _));
    }

    [Fact]
    public void Command_ExpiresAfterWindow()
    {
        var buf = new InputCommandBuffer { BufferWindowMs = 150 };
        buf.Push(Cmd(InputCommandKind.Dodge), 0);
        Assert.True(buf.TryConsume(InputCommandKind.Dodge, 149, out _));

        buf.Push(Cmd(InputCommandKind.Dodge), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Dodge, 151, out _));
    }

    [Fact]
    public void Kinds_AreIndependent()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Dodge, 10, out _));
        Assert.True(buf.TryConsume(InputCommandKind.Attack, 10, out _));
    }

    [Fact]
    public void Move_And_None_AreNotBuffered()
    {
        var buf = new InputCommandBuffer();
        buf.Push(new InputCommand { Kind = InputCommandKind.Move, Axis = Vector2.Up }, 0);
        buf.Push(Cmd(InputCommandKind.None), 0);
        Assert.False(buf.TryConsume(InputCommandKind.Move, 10, out _));
        Assert.False(buf.TryConsume(InputCommandKind.None, 10, out _));
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buf = new InputCommandBuffer();
        buf.Push(Cmd(InputCommandKind.Attack), 0);
        buf.Clear();
        Assert.False(buf.TryConsume(InputCommandKind.Attack, 10, out _));
    }
}
