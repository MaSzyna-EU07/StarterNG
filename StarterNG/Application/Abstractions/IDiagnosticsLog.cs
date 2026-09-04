using System;
using System.Collections.Generic;

namespace StarterNG.Application.Abstractions;

/// <summary>
/// Port over the starter's error log. Domain and application code depends on
/// this interface only; the file-backed implementation and the message box that
/// surfaces faults to the user live in Infrastructure and Presentation.
/// </summary>
public interface IDiagnosticsLog
{
    void Log(string message);

    void Log(string context, Exception exception);

    void Log(IEnumerable<string> messages);

    /// <summary>Everything logged so far, newline separated.</summary>
    string Text { get; }

    void Clear();

    void Flush();
}
