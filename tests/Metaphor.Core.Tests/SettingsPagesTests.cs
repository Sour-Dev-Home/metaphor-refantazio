using Metaphor.Core.Menus;

namespace Metaphor.Core.Tests;

/// <summary>The settings pages' working copy, logged in Debug builds for #558's checks 2 and 3 (#560): which classes are
/// the four pages, and a page's bytes logged only when they are new or changed. Made-up bytes only.</summary>
[Trait("Category", "Unit")]
public sealed class SettingsPagesTests
{
    [Theory]
    [InlineData("?$ConfigMenuChoicePcT@$0HG@@ope@ui@app", true)]
    [InlineData("?$ConfigMenuChoicePcT@$0HH@@ope@ui@app", true)]
    [InlineData("?$ConfigMenuChoicePcT@$0HI@@ope@ui@app", true)]
    [InlineData("?$ConfigMenuChoicePcT@$0HJ@@ope@ui@app", true)]
    [InlineData("?$ConfigTopChoicePc@VConfigTopChoicePcLayoutInit@ui@app@@@ope@ui@app", false)]
    [InlineData("ConfigMenuChoiceKeyConfig@ope@ui@app", false)]
    [InlineData(null, false)]
    public void TheFourPagesAreTheOnesWhoseCopyIsLogged(string? screenClass, bool page)
    {
        Assert.Equal(page, SettingsPages.IsPage(screenClass));
    }

    [Fact]
    public void APagesBytesAreGivenTheFirstTimeAndWhenTheyChange()
    {
        var copies = new ChangedBytes();

        Assert.Equal("0102ff", copies.Changed(0xa0, [0x01, 0x02, 0xff]));
        Assert.Null(copies.Changed(0xa0, [0x01, 0x02, 0xff]));
        Assert.Equal("0103ff", copies.Changed(0xa0, [0x01, 0x03, 0xff]));
    }

    [Fact]
    public void EachPageKeepsItsOwnBytes()
    {
        var copies = new ChangedBytes();
        copies.Changed(0xa0, [0x01]);

        Assert.Equal("01", copies.Changed(0xb0, [0x01]));
        Assert.Null(copies.Changed(0xa0, [0x01]));
    }
}
