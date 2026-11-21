namespace Metaphor.Features.Menus;

/// <summary>The rows of one settings page in cursor order (#560): <paramref name="Page"/> is the page's place on the
/// category list (0 System, 1 Sound, 2 Graphics, 3 Display), <paramref name="Total"/> the row count these rows are for,
/// and <paramref name="NameLabels"/> each row's name as a message label, or null for a row whose name isn't known. A page
/// showing another count isn't in this order: System has 20 rows from the title and 24 in play
/// (docs/findings/settings-screens-inventory-static.md).</summary>
public sealed record SettingsPageRows(int Page, int Total, IReadOnlyList<string?> NameLabels)
{
    /// <summary>The rows known on game build 548ddc95… (Steam build 18330018): none yet. The labels are fetched as a page
    /// is built, not per row, and the order they are fetched in doesn't give the rows' order (System fetches 7 row names
    /// for its 20 rows; docs/raw-sources/settings-pages-resolves-by-ear-2026-11-20.md), so which label each row shows is
    /// <c>[NEEDS VERIFICATION]</c>: #558's check 1, from the drawn row text in a session.</summary>
    public static IReadOnlyList<SettingsPageRows> Steam18330018 { get; } = [];

    /// <summary>The name label of the row at <paramref name="index"/>, or null when it isn't known.</summary>
    public string? NameLabel(int index) => index >= 0 && index < NameLabels.Count ? NameLabels[index] : null;
}
