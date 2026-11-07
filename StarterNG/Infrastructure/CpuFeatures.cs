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
    public static bool LacksAvx2 { get; } =
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86 && !HasAvx2();

    /// <summary>
    /// Asks the processor itself. Avx2.IsSupported only says whether the starter's own
    /// code may use AVX2, and the AOT compiler fixes that at build time to its baseline,
    /// which has no AVX2, so a published starter always read false.
    /// </summary>
    static bool HasAvx2()
    {
        if (!X86Base.IsSupported || X86Base.CpuId(0, 0).Eax < 7)
            return false;
        // Leaf 1 ECX: bit 12 FMA, which /arch:AVX2 lets the compiler use too; bit 27 the
        // system has turned XSAVE on (whether it saves the AVX registers is in XCR0,
        // which .NET cannot read); bit 28 AVX. Leaf 7 EBX: bit 5 AVX2.
        const int leaf1 = (1 << 12) | (1 << 27) | (1 << 28);
        return (X86Base.CpuId(1, 0).Ecx & leaf1) == leaf1 &&
               (X86Base.CpuId(7, 0).Ebx & (1 << 5)) != 0;
    }
}
