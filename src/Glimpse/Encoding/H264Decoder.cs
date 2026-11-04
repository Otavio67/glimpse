using System.Diagnostics;
using System.Threading.Channels;
namespace Glimpse.Encoding;
public sealed class H264Decoder : IDisposable
{
    private Process? _process;
    private readonly Channel<byte[]> _input = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(120) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    public event Action<byte[]>? Frame;
    public event Action<string>? Error;
    public bool TryWrite(byte[] data) => _input.Writer.TryWrite(data);
    public async Task RunAsync(CancellationToken cancel)
    {
        _process = H264Encoder.Start("-hide_banner -loglevel error -f h264 -i pipe:0 -an -vf scale=1280:720 -pix_fmt bgra -f rawvideo pipe:1"); var proc = _process;
        var stderr = proc.StandardError.ReadToEndAsync(cancel);
        var write = Task.Run(async () => { await foreach (var bytes in _input.Reader.ReadAllAsync(cancel)) await proc.StandardInput.BaseStream.WriteAsync(bytes, cancel); }, cancel);
        try {
            while (!cancel.IsCancellationRequested) {
                byte[] pixels = new byte[1280 * 720 * 4]; int used = 0;
                while (used < pixels.Length) { int n = await proc.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(used), cancel); if (n == 0) throw new EndOfStreamException("Decodificador encerrado: " + (proc.HasExited ? await stderr : "nenhuma imagem recebida")); used += n; }
                Frame?.Invoke(pixels);
            }
        } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
          catch (Exception ex) { Error?.Invoke("Decodificador interrompido: " + ex.Message); }
        finally { _input.Writer.TryComplete(); Dispose(); try { await write; } catch { } }
    }
    public void Dispose() { _input.Writer.TryComplete(); try { if (_process is { HasExited: false }) _process.Kill(true); } catch { } _process?.Dispose(); _process = null; }
}
