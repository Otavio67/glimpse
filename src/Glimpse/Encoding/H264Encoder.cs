using System.Diagnostics;
using System.Threading.Channels;
namespace Glimpse.Encoding;
public sealed class H264Encoder : IDisposable
{
    private Process? _process;
    public event Action<byte[]>? AccessUnit;
    public event Action<string>? Error;
    public event Action<bool>? EncoderSelected;
    public bool UsingHardware { get; private set; }
    public async Task RunAsync(ChannelReader<byte[]> frames, int width, int height, int fps, CancellationToken cancel)
    {
        UsingHardware = HardwareEncoderProbe.HasH264HardwareEncoder(); EncoderSelected?.Invoke(UsingHardware);
        string hardware = UsingHardware ? "1" : "0";
        // A registered hardware encoder is required to succeed; no silent software fallback.
        var args = $"-hide_banner -loglevel error -f rawvideo -pixel_format bgra -video_size {width}x{height} -framerate {fps} -i pipe:0 -an -vf scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,format=nv12 -c:v h264_mf -hw_encoding {hardware} -b:v 2500k -g {fps * 2} -bf 0 -bsf:v h264_metadata=aud=insert -f h264 pipe:1";
        _process = Start(args); var proc = _process;
        var stderr = proc.StandardError.ReadToEndAsync(cancel);
        var read = Task.Run(async () => { var parser = new AnnexBAccessUnits(); var buffer = new byte[32768]; int n; while ((n = await proc.StandardOutput.BaseStream.ReadAsync(buffer, cancel)) > 0) foreach (var unit in parser.Push(buffer.AsSpan(0,n))) AccessUnit?.Invoke(unit); }, cancel);
        try { await foreach (var frame in frames.ReadAllAsync(cancel)) await proc.StandardInput.BaseStream.WriteAsync(frame, cancel); proc.StandardInput.Close(); await read; await proc.WaitForExitAsync(cancel); if (proc.ExitCode != 0) throw new InvalidOperationException(await stderr); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) { Error?.Invoke("Encoder stopped: " + ex.Message); }
        finally { Dispose(); }
    }
    internal static Process Start(string arguments) {
        var file = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"); if (!File.Exists(file)) file = "ffmpeg.exe";
        return Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }) ?? throw new InvalidOperationException("Could not start FFmpeg.");
    }
    public void Dispose() { try { if (_process is { HasExited: false }) _process.Kill(true); } catch { } _process?.Dispose(); _process = null; }
}

