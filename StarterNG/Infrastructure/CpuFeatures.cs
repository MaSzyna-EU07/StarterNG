using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace StarterNG.Infrastructure;

/// <summary>What the processor offers against what the simulator builds assume.</summary>
public static class CpuFeatures
{
    /// <summary>
    /// The simulator's CMakeLists has ENABLE_AVX2 on by default since May 2026, so
    /// the published builds stop at the first AVX2 instruction on an older x86
    /// processor. Other architectures cannot run them at all, which is not this.
    /// </summary>
    public static bool LacksAvx2 =>
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86 && !Avx2.IsSupported;
}
