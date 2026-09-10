using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Logging;

namespace StarterNG.Infrastructure;

public sealed class DiagnosticsLogSink : ILogSink
{
    private const int MaxDistinctMessages = 200;

    private readonly LogEventLevel _minimumLevel;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _capped;

    private DiagnosticsLogSink(LogEventLevel minimumLevel) => _minimumLevel = minimumLevel;

    public static void Install(LogEventLevel minimumLevel = LogEventLevel.Warning) =>
        Logger.Sink = new DiagnosticsLogSink(minimumLevel);

    public bool IsEnabled(LogEventLevel level, string area) => level >= _minimumLevel;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Write(level, area, source, messageTemplate, null);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
                    params object?[] propertyValues) =>
        Write(level, area, source, messageTemplate, propertyValues);

    private void Write(LogEventLevel level, string area, object? source, string messageTemplate,
                       object?[]? propertyValues)
    {
        if (!IsEnabled(level, area))
            return;

        string line = $"[Avalonia/{area}/{level}] {Format(messageTemplate, propertyValues)}";
        if (source is not null)
            line += $" (source: {source.GetType().Name})";

        lock (_gate)
        {
            if (_capped || !_seen.Add(line))
                return;
            if (_seen.Count >= MaxDistinctMessages)
            {
                _capped = true;
                Diagnostics.Log(line);
                Diagnostics.Log($"[Avalonia] {MaxDistinctMessages} distinct messages logged, muting the rest");
                return;
            }
        }

        Diagnostics.Log(line);
    }

    private static string Format(string template, object?[]? values)
    {
        if (values is null || values.Length == 0 || template.IndexOf('{') < 0)
            return template;

        var sb = new StringBuilder(template.Length + 32);
        int next = 0;
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                sb.Append(template[i]);
                continue;
            }

            int close = template.IndexOf('}', i);
            if (close < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            sb.Append(next < values.Length ? values[next]?.ToString() ?? "null" : template[i..(close + 1)]);
            next++;
            i = close;
        }
        return sb.ToString();
    }
}
