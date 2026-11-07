using System;
using System.Text.RegularExpressions;

namespace StarterNG.Domain.Settings;

/// <summary>A graphics card as the system names it.</summary>
/// <param name="Name">As the driver or the PCI list calls it.</param>
/// <param name="MemoryBytes">The memory it reports; 0 when the system does not tell.</param>
public sealed record GraphicsCard(string Name, long MemoryBytes)
{
    private const long GiB = 1L << 30;

    // Intel but for the discrete Arc A and B series; AMD's processor graphics, which
    // are named by a number with an M, or not at all.
    private static readonly Regex SharedMemoryName = new(
        @"\bIntel\b(?!.*\bArc\b.*\b[AB]\d{3}\b)" +
        @"|\bRadeon(\(TM\))? (\d{3}M\b|Graphics\b|Vega Series\b|(RX )?Vega (3|6|8|10|11)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A processor's card, sharing the computer's memory. No discrete card reports under 1 GB any more.</summary>
    public bool SharedMemory => SharedMemoryName.IsMatch(Name) || MemoryBytes is > 0 and < GiB;

    /// <summary>The memory as it is told apart on a box: 8546942976 bytes are 8 GB.</summary>
    public int MemoryGb => (int)Math.Round(MemoryBytes / (double)GiB);
}
