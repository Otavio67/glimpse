using System.Runtime.InteropServices;
using System.Threading.Channels;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace Glimpse.Capture;

// One bounded pending frame: a slow encoder must not accumulate old frames.
public sealed class WindowsCaptureSource : IDisposable
{
    [ComImport, Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithWindow { void Initialize(IntPtr hwnd); }
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess { IntPtr GetInterface(ref Guid iid); }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr result);

    private readonly Device _d3d = new(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
    private IDirect3DDevice? _device;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private Texture2D? _staging;
    private long _lastFrame;
    private int _width, _height, _fps;
    private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    public ChannelReader<byte[]> Frames => _frames.Reader;
    public int Width => _width;
    public int Height => _height;
    public event Action<string>? Error;
    // The same immutable BGRA buffer goes to preview and encoder; the preview never reads the encoder channel.
    public event Action<byte[],int,int>? PreviewFrame;

    public async Task<bool> PickAsync(IntPtr owner, int fps)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("A captura de tela do Windows não está disponível neste computador.");
        var picker = new GraphicsCapturePicker();
        picker.As<IInitializeWithWindow>().Initialize(owner);
        var item = await picker.PickSingleItemAsync();
        if (item is null) return false;
        _fps = fps; _width = item.Size.Width; _height = item.Size.Height;
        if (_width <= 0 || _height <= 0) throw new InvalidOperationException("A fonte selecionada não possui uma área visível.");
        using var dxgi = _d3d.QueryInterface<SharpDX.DXGI.Device>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var ptr));
        try { _device = MarshalInterface<IDirect3DDevice>.FromAbi(ptr); } finally { Marshal.Release(ptr); }
        _staging = new Texture2D(_d3d, new Texture2DDescription { Width = _width, Height = _height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, BindFlags = BindFlags.None, CpuAccessFlags = CpuAccessFlags.Read });
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _session = _pool.CreateCaptureSession(item);
        _pool.FrameArrived += OnFrame;
        item.Closed += (_, _) => { Error?.Invoke("A janela selecionada foi fechada."); _frames.Writer.TryComplete(); };
        return true;
    }
    public void Start() => _session?.StartCapture();
    private void OnFrame(Direct3D11CaptureFramePool pool, object _)
    {
        try
        {
            using var frame = pool.TryGetNextFrame();
            if (frame is null) return;
            if (frame.ContentSize.Width != _width || frame.ContentSize.Height != _height) { Error?.Invoke("O tamanho da fonte mudou. Pare o compartilhamento e selecione novamente."); _frames.Writer.TryComplete(); return; }
            long now = Environment.TickCount64;
            if (now - _lastFrame < 1000 / _fps) return;
            _lastFrame = now;
            var iid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            var raw = frame.Surface.As<IDirect3DDxgiInterfaceAccess>().GetInterface(ref iid);
            using var texture = new Texture2D(raw);
            _d3d.ImmediateContext.CopyResource(texture, _staging);
            var mapped = _d3d.ImmediateContext.MapSubresource(_staging, 0, MapMode.Read, MapFlags.None);
            try { var pixels = new byte[checked(_width * _height * 4)]; for (int y = 0; y < _height; y++) Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * mapped.RowPitch), pixels, y * _width * 4, _width * 4); PreviewFrame?.Invoke(pixels,_width,_height); _frames.Writer.TryWrite(pixels); }
            finally { _d3d.ImmediateContext.UnmapSubresource(_staging, 0); }
        }
        catch (Exception ex) { Error?.Invoke(ex.Message); _frames.Writer.TryComplete(ex); }
    }
    public void Dispose() { _frames.Writer.TryComplete(); if (_pool is not null) _pool.FrameArrived -= OnFrame; _session?.Dispose(); _pool?.Dispose(); _staging?.Dispose(); _device?.Dispose(); _d3d.Dispose(); }
}
