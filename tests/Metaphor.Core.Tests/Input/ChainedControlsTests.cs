using Metaphor.Core.Input;

namespace Metaphor.Core.Tests.Input;

/// <summary>Two controls as one (#631): the second gets what the first hands on.</summary>
[Trait("Category", "Unit")]
public sealed class ChainedControlsTests
{
    [Fact]
    public void TheSecondSeesWhatTheFirstHandsOn()
    {
        var second = new Drops(button: 0x2, key: 0x48);
        var chained = new ChainedControls(new Drops(button: 0x1, key: 0x46), second);

        Assert.Equal(0x4, chained.Pad(0, 0x1 | 0x2 | 0x4, made: true));
        Assert.Equal([0x41], chained.Keys([0x46, 0x48, 0x41]));
        Assert.Equal(0x2 | 0x4, second.LastButtons);
    }

    private sealed class Drops(int button, int key) : IInputControls
    {
        public int LastButtons { get; private set; }

        public int Pad(int pad, int buttons, bool made)
        {
            LastButtons = buttons;
            return buttons & ~button;
        }

        public IReadOnlyList<int> Keys(IReadOnlyList<int> keys) => [.. keys.Where(held => held != key)];
    }
}
