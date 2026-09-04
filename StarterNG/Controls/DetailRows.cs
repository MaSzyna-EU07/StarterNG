using Avalonia.Controls;

namespace StarterNG.Controls;

public static class DetailRows
{
    public const string NoValue = "–";

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

    public static SettingRow Reading(string label, string? value)
    {
        bool empty = string.IsNullOrWhiteSpace(value);
        return Row(label, value: empty ? NoValue : value, tip: empty ? null : value);
    }
}
