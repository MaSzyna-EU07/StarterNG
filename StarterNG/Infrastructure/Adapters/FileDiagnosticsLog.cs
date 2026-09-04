using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

/// <summary>
/// <see cref="IDiagnosticsLog"/> that mirrors every line into starter/bledy.txt.
/// Writing is best-effort: a read-only or full installation directory degrades to
/// an in-memory log rather than taking the starter down, and a single retry is
/// made at <see cref="Flush"/> time.
/// </summary>
/// <remarks>
/// Instance state replaces the previous static <c>Diagnostics</c> accumulator, so
/// tests can assert on what a use case logged without touching disk (pair it with
/// an in-memory <see cref="IFileSystem"/>).
/// </remarks>
public sealed class FileDiagnosticsLog : IDiagnosticsLog
{
    private readonly IFileSystem _files;
    private readonly IClock _clock;
    private readonly string _logPath;

    private readonly object _gate = new();
    private readonly List<string> _lines = new();

    private StreamWriter? _writer;
    private bool _writerFailed;

    public FileDiagnosticsLog(IFileSystem files, IClock clock, IGamePaths paths)
    {
        _files = files;
        _clock = clock;
        _logPath = paths.DiagnosticsLog;
    }

    public string LogPath => _logPath;

    public string Text
    {
        get { lock (_gate) return string.Join(Environment.NewLine, _lines); }
    }

    public void Log(string message)
    {
        string line = $"{_clock.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {message}";
        lock (_gate)
        {
            _lines.Add(line);
            Append(line);
        }
    }

    public void Log(string context, Exception exception) =>
        Log($"{context}: {exception.GetType().Name}: {exception.Message}");

    public void Log(IEnumerable<string> messages)
    {
        foreach (string message in messages)
            Log(message);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            CloseWriter();
            try
            {
                if (_files.FileExists(_logPath))
                    _files.DeleteFile(_logPath);
            }
            catch
            {
                // A log we cannot delete is not worth failing a user action over.
            }
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_lines.Count == 0)
                return;

            try
            {
                _writer?.Flush();
            }
            catch (Exception ex)
            {
                _writerFailed = true;
                _lines.Add($"log flush failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (!_writerFailed && _writer is not null)
                return;

            // The streaming writer never came up (or died mid-session): fall back
            // to one bulk write so the session is not lost entirely.
            try
            {
                _files.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                _files.WriteAllLines(_logPath, _lines);
            }
            catch
            {
                // Nothing left to try; the in-memory log still backs the UI.
            }
        }
    }

    private void Append(string line)
    {
        if (_writerFailed)
            return;

        try
        {
            if (_writer is null)
            {
                _files.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                _writer = new StreamWriter(_logPath, append: false) { AutoFlush = true };
            }
            _writer.WriteLine(line);
        }
        catch
        {
            _writerFailed = true;
            CloseWriter();
        }
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Disposing a broken writer is allowed to fail.
        }
        _writer = null;
    }
}
