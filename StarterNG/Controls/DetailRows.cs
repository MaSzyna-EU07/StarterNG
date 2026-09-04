using Avalonia.Controls;

namespace StarterNG.Controls;

/// <summary>
/// The read-only label/value lines the depot's detail tabs are built from.
/// </summary>
/// <remarks>
/// Shared so every tab reads the same way: label in the left column, value in
/// the right, aligned down the panel. Previously each tab built its own rows,
/// and the texture tab glued "label: value" into a single line, which lined up
/// with nothing.
/// </remarks>
public static class DetailRows
{
    /// <summary>Shown in place of a reading a vehicle does not have.</summary>
    public const string NoValue = "–";

    /// <summary>
    /// The one row shape every detail tab uses. The label column negotiates its
    /// width through the shared-size group on the host panel, so all rows in a
    /// panel line up without anyone naming a pixel count - see the Compact styles
    /// in Settings.axaml. Omitting <paramref name="content"/> makes it a
    /// read-only line; omitting <paramref name="label"/> indents a bare widget to
    /// the control column.
    /// </summary>
    public static SettingRow Row(string? label = null, Control? content = null,
                                 string? value = null, string? description = null,
                                 string? tip = null)
    {
        var row = new SettingRow
        {
            Label = label,
            Description = description,
            ValueText = value,
            Content = content
        };
        row.Classes.Add("Compact");

        if (!string.IsNullOrWhiteSpace(tip))
            ToolTip.SetTip(row, tip);

        return row;
    }

    /// <summary>
    /// A read-only label/value line. The dash keeps the row from collapsing when a
    /// vehicle has no reading for the field; the full value is on the tooltip, so
    /// a trimmed one is still legible.
    /// </summary>
    public static SettingRow Reading(string label, string? value)
    {
        bool empty = string.IsNullOrWhiteSpace(value);
        return Row(label, value: empty ? NoValue : value, tip: empty ? null : value);
    }
}
