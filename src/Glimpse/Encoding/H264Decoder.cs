using System.Diagnostics;
using System.Threading.Channels;
using System.Buffers.Binary;
namespace Glimpse.Encoding;
public sealed class H264Decoder : IDisposable
{
    private Process? _process;
    private readonly Channel<byte[]> _input = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(120) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    public event Action<byte[]>? Frame;
    public event Action<DecodedVideoFrame>? FrameDecoded;
    public event Action<string>? Error;
    public bool TryWrite(byte[] data) => _input.Writer.TryWrite(data);
    public async Task RunAsync(CancellationToken cancel)
    {
        // Each BMP carries its own dimensions. Keep the encoded resolution instead of forcing 720p.
        _process = H264Encoder.Start("-hide_banner -loglevel error -f h264 -i pipe:0 -an -pix_fmt bgra -c:v bmp -f image2pipe pipe:1"); var proc = _process;
        var stderr = proc.StandardError.ReadToEndAsync(cancel);
        var write = Task.Run(async () => { await foreach (var bytes in _input.Reader.ReadAllAsync(cancel)) await proc.StandardInput.BaseStream.WriteAsync(bytes, cancel); }, cancel);
        try {
            while (!cancel.IsCancellationRequested) {
                var frame=await ReadBmpFrameAsync(proc.StandardOutput.BaseStream,cancel);
                FrameDecoded?.Invoke(frame);
                Frame?.Invoke(frame.Pixels);
            }
        } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
          catch (Exception ex) { Error?.Invoke("Decodificador interrompido: " + ex.Message); }
        finally { _input.Writer.TryComplete(); Dispose(); try { await write; } catch { } }
    }
    private static async Task<DecodedVideoFrame> ReadBmpFrameAsync(Stream stream,CancellationToken token)
    {
        var header=new byte[54]; await stream.ReadExactlyAsync(header,token);
        int size=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(2));
        int offset=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(10));
        int dib=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(14));
        int width=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(18));
        int signedHeight=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(22));
        int planes=BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        int bits=BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        int compression=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(30));
        if(header[0]!='B' || header[1]!='M' || offset!=54 || dib!=40 || planes!=1 || bits!=32 || compression!=0 || width is <1 or >2560 || signedHeight is 0 or int.MinValue || Math.Abs(signedHeight)>1440)
            throw new InvalidDataException("Formato de quadro recebido inválido ou maior que 2K.");
        int height=Math.Abs(signedHeight),stride=checked(width*4),length=checked(stride*height);
        if(size!=54+length) throw new InvalidDataException("Tamanho de quadro recebido inválido.");
        var pixels=new byte[length]; await stream.ReadExactlyAsync(pixels,token);
        if(signedHeight>0) {
            var row=new byte[stride];
            for(int y=0;y<height/2;y++) { var top=pixels.AsSpan(y*stride,stride); var bottom=pixels.AsSpan((height-1-y)*stride,stride); top.CopyTo(row); bottom.CopyTo(top); row.CopyTo(bottom); }
        }
        return new(width,height,pixels);
    }
    public void Dispose() { _input.Writer.TryComplete(); try { if (_process is { HasExited: false }) _process.Kill(true); } catch { } _process?.Dispose(); _process = null; }
}
