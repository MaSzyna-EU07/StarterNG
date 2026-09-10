using System;
using System.Collections.Generic;

namespace StarterNG.Application.Abstractions;

public interface IDiagnosticsLog
{
    void Log(string message);

    void Log(string context, Exception exception);

    void Log(IEnumerable<string> messages);

    string Text { get; }

    void Clear();

    void Flush();
}
