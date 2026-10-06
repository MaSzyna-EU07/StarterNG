using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StarterNG.Application;

/// <summary>How a simulator the starter launched has ended.</summary>
public sealed record SimulatorExit(int ExitCode, TimeSpan RunTime, IReadOnlyList<string> Errors)
{
    /// <summary>Gone before anyone could have played - however clean the exit code.</summary>
    public static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(10);

    public bool Quick => RunTime < QuickExit;

    public bool Failed => ExitCode != 0 || Quick;

    /// <summary>The error stream without its colour codes and blank lines.</summary>
    public IReadOnlyList<string> CleanErrors =>
        Errors.Select(line => AnsiCode.Replace(line, "").TrimEnd())
              .Where(line => line.Length > 0)
              .ToList();

    /// <summary>The exit code, with what it means where it is a known way to die.</summary>
    public string DescribeCode()
    {
        // Linux reports a death by signal as 128 plus the signal.
        string? meaning = ExitCode switch
        {
            132 => "SIGILL",
            134 => "SIGABRT",
            135 => "SIGBUS",
            136 => "SIGFPE",
            137 => "SIGKILL",
            139 => "SIGSEGV",
            143 => "SIGTERM",
            _ => null
        };

        // Windows reports an unhandled exception as its NTSTATUS.
        meaning ??= unchecked((uint)ExitCode) switch
        {
            0xC0000005 => "access violation",
            0xC00000FD => "stack overflow",
            0xC0000374 => "heap corruption",
            0xC0000409 => "stack buffer overrun",
            _ => null
        };

        string code = ExitCode < 0
            ? "0x" + unchecked((uint)ExitCode).ToString("X8", CultureInfo.InvariantCulture)
            : ExitCode.ToString(CultureInfo.InvariantCulture);
        return meaning is null ? code : $"{code} ({meaning})";
    }

    private static readonly Regex AnsiCode = new(@"\x1B\[[0-9;]*m");
}
