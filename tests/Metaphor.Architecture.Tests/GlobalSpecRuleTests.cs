namespace Metaphor.Architecture.Tests;

/// <summary>#321 (#300's globals): each game global the loader locates points at a RIP-relative load in its signature's
/// pattern, so a typo in its offsets fails here instead of pointing a reader at the wrong address.</summary>
[Trait("Category", "Unit")]
public sealed class GlobalSpecRuleTests
{
    // Stands in for the real record; the rule matches its constructor by full name.
    private const string Record = """
        namespace Metaphor.Core
        {
            public sealed record GlobalSpec(string Name, System.Collections.Generic.IReadOnlyList<string> GameBuilds,
                string SignatureName, int LoadOffset, int DisplacementOffset, int InstructionLength, string TurnsOff);
        }
        """;

    private static readonly Dictionary<string, string> Patterns = new(StringComparer.Ordinal)
    {
        ["WindowBody"] = "48 8B 0D ?? ?? ?? ?? 8B 81 60 05 00 00",
        ["FlagBody"] = "4C 63 01 41 83 F8 FF 75 0C 48 8B 0D ?? ?? ?? ?? E9 ?? ?? ?? ??",
        ["Getter"] = "8B 05 ?? ?? ?? ?? C3 CC",
        ["LeaGetter"] = "8D 05 ?? ?? ?? ?? C3 CC",
    };

    private static Mono.Cecil.AssemblyDefinition HookLayer(string signature, string offsets) =>
        Plant.Compile(Rules.HookLayer,
            $"{Record}\nnamespace Metaphor.Hooks {{ public sealed class Reader {{ private static readonly " +
            $"Metaphor.Core.GlobalSpec Spec = new(\"flag\", new[] {{ \"b\" }}, \"{signature}\", {offsets}, \"flag controls\"); }} }}");

    [Theory]
    [InlineData("WindowBody", "0, 3, 7")]
    [InlineData("FlagBody", "9, 3, 7")]
    [InlineData("Getter", "0, 2, 6")] // a 32-bit mov eax,[rip+disp32] (#538)
    public void ASpecOnALoadPasses(string signature, string offsets) =>
        Assert.Empty(Rules.GlobalSpecsPointAtALoad(HookLayer(signature, offsets), Patterns));

    [Theory]
    [InlineData("FlagBody", "8, 3, 7")] // the load starts a byte early
    [InlineData("WindowBody", "0, 2, 7")] // disp32 a byte early
    [InlineData("WindowBody", "0, 3, 6")] // a length that isn't disp32's end
    [InlineData("WindowBody", "7, 3, 7")] // past the pattern's end
    [InlineData("Getter", "0, 1, 5")] // the 32-bit mov's ModR/M taken for disp32
    [InlineData("LeaGetter", "0, 2, 6")] // a lea with no REX.W gives a 32-bit address, not a global's
    public void ASpecOffTheLoadIsAViolation(string signature, string offsets)
    {
        var violation = Assert.Single(Rules.GlobalSpecsPointAtALoad(HookLayer(signature, offsets), Patterns));

        Assert.Contains("Metaphor.Hooks.Reader", violation, StringComparison.Ordinal);
        Assert.Contains(signature, violation, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpecOnAnUnlistedSignatureIsAViolation() =>
        Assert.Contains("isn't in hook-signatures.json",
            Assert.Single(Rules.GlobalSpecsPointAtALoad(HookLayer("Nowhere", "0, 3, 7"), Patterns)),
            StringComparison.Ordinal);

    [Fact]
    public void ASpecOfAnotherShapeIsAViolationNotAPass()
    {
        // The record gained a parameter: the rule can't tell which argument is which, so it says so.
        var hooks = Plant.Compile(Rules.HookLayer, """
            namespace Metaphor.Core
            {
                public sealed record GlobalSpec(string Name, System.Collections.Generic.IReadOnlyList<string> GameBuilds,
                    string SignatureName, int LoadOffset, int DisplacementOffset, int InstructionLength, string TurnsOff,
                    int Width);
            }
            namespace Metaphor.Hooks
            {
                public sealed class Reader
                {
                    private static readonly Metaphor.Core.GlobalSpec Spec =
                        new("flag", new[] { "b" }, "WindowBody", 0, 3, 7, "flag controls", 8);
                }
            }
            """);

        Assert.Contains("can't read", Assert.Single(Rules.GlobalSpecsPointAtALoad(hooks, Patterns)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheRealGlobalsFollowTheRule()
    {
        var hooks = Scanned.Load(Rules.HookLayer);

        // With no patterns every spec is a violation, so this counts the specs the rule found: the dialogue window and
        // the menus-open flag (#300), the game data and the character and follower tables (#81, #107), the voice
        // language (#538), the field's world, scene, database and person table (#181), and the field scene again for the
        // battle status key (#631).
        Assert.Equal(11, Rules.GlobalSpecsPointAtALoad(hooks, new Dictionary<string, string>()).Count);
        Assert.Empty(Rules.GlobalSpecsPointAtALoad(hooks, Scanned.SignaturePatterns()));
    }
}
