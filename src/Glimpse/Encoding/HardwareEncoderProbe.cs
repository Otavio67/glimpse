using System.Runtime.InteropServices;
namespace Glimpse.Encoding;
public static class HardwareEncoderProbe
{
    private const uint Hardware = 0x4;
    private static readonly Guid VideoEncoder = new("f79eac7d-e545-4387-bdee-d647d7bde42a");
    private static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid H264 = new("34363248-0000-0010-8000-00aa00389b71");
    [StructLayout(LayoutKind.Sequential)] private struct TypeInfo { public Guid MajorType, Subtype; }
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFTEnumEx(Guid category, uint flags, IntPtr input, ref TypeInfo output, out IntPtr activates, out int count);
    public static bool HasH264HardwareEncoder()
    {
        Marshal.ThrowExceptionForHR(MFStartup(0x00020070, 0));
        try {
            var type = new TypeInfo { MajorType = Video, Subtype = H264 };
            Marshal.ThrowExceptionForHR(MFTEnumEx(VideoEncoder, Hardware, IntPtr.Zero, ref type, out var array, out var count));
            try { for (int i = 0; i < count; i++) Marshal.Release(Marshal.ReadIntPtr(array, i * IntPtr.Size)); }
            finally { Marshal.FreeCoTaskMem(array); }
            return count > 0;
        } finally { MFShutdown(); }
    }
}
