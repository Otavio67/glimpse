using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Glimpse.Capture;
using Glimpse.Encoding;
using Glimpse.Networking;
using Glimpse.Resources;
namespace Glimpse.ViewModels;
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<IntPtr> _owner;
    private readonly System.Windows.Threading.Dispatcher _dispatcher = Application.Current.Dispatcher;
    private LocalPeer? _peer;
    private DirectRoomHost? _roomHost;
    private WindowsCaptureSource? _capture;
    private H264Encoder? _encoder;
    private H264Decoder? _decoder;
    private CancellationTokenSource? _captureCancellation, _sessionCancellation;
    private bool _sender, _uiFramePending;
    private string _status="Pronto para começar", _local="", _remote="";
    private ImageSource? _frame;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Status { get => _status; private set => Set(ref _status,value); }
    public string LocalText { get => _local; private set => Set(ref _local,value); }
    public string RemoteText { get => _remote; set => Set(ref _remote,value); }
    public ImageSource? Frame { get => _frame; private set { Set(ref _frame,value); Notify(nameof(WaitingVisibility)); } }
    public Visibility WaitingVisibility => Frame is null ? Visibility.Visible : Visibility.Collapsed;
    public ICommand CreateRoomCommand { get; }
    public ICommand JoinRoomCommand { get; }
    public ICommand CreateOfferCommand { get; }
    public ICommand CreateAnswerCommand { get; }
    public ICommand ApplyAnswerCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ShareCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand DisconnectCommand { get; }
    public MainViewModel(Func<IntPtr> owner) {
        _owner=owner;
        CreateRoomCommand=new AsyncCommand(()=>{
            Reset(); _sender=true; CreatePeer(true);
            try { _roomHost=new DirectRoomHost(); StartDecoder(); _roomHost.Error += Error; LocalText=_roomHost.Code; Status="Sala criada. Compartilhe o código com seu amigo."; _= _roomHost.WaitForGuestAsync(_peer!); }
            catch { Reset(); throw; }
            return Task.CompletedTask;
        });
        JoinRoomCommand=new AsyncCommand(async()=>{
            var code=RemoteText.Trim(); if(code.Length==0)throw new InvalidOperationException("Digite o código da sala.");
            Reset(); _sender=false; CreatePeer(false); Status="Entrando na sala...";
            try { using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation!.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25)); await DirectRoomGuest.JoinAsync(code,_peer!,timeout.Token); StartDecoder(); Status="Conectando..."; }
            catch(OperationCanceledException) { Reset(); throw new InvalidOperationException("Não foi possível conectar. Confira o código e a rede."); }
            catch(Exception) { Reset(); throw new InvalidOperationException("Não foi possível entrar na sala. Confira o código e a rede."); }
        });
        CreateOfferCommand=new AsyncCommand(async()=>{ Reset(); _sender=true; CreatePeer(true); Status=Strings.PreparingOffer; LocalText=await _peer!.CreateOfferAsync(); Status=Strings.SendOffer; });
        CreateAnswerCommand=new AsyncCommand(async()=>{ var offer=RemoteText; Reset(); _sender=false; CreatePeer(false); Status=Strings.PreparingAnswer; LocalText=await _peer!.AcceptOfferAsync(offer); StartDecoder(); Status=Strings.SendAnswer; });
        ApplyAnswerCommand=new AsyncCommand(()=>{ if (_peer is null || !_sender) throw new InvalidOperationException("Crie uma sala primeiro."); _peer.AcceptAnswer(RemoteText); Status=Strings.Connecting; return Task.CompletedTask; });
        CopyCommand=new AsyncCommand(()=>{ if (!string.IsNullOrEmpty(LocalText)) Clipboard.SetText(LocalText); return Task.CompletedTask; });
        ShareCommand=new AsyncCommand(StartCaptureAsync);
        StopCommand=new AsyncCommand(()=>{ StopCapture(); Status=Strings.Stopped; return Task.CompletedTask; });
        DisconnectCommand=new AsyncCommand(()=>{ Reset(); Status=Strings.Disconnected; return Task.CompletedTask; });
    }
    private void CreatePeer(bool sender) {
        _sessionCancellation=new(); _peer=new LocalPeer(sender);
        _peer.StateChanged += state => OnUi(()=>Status=state switch { "connected" => "Conectado à sala", "connecting" => "Conectando...", "disconnected" => "Conexão interrompida", "failed" => "Não foi possível conectar", "closed" => "Conexão encerrada", _ => "Preparando conexão..." });
        _peer.VideoFrame += data => { if (_decoder is not null && !_decoder.TryWrite(data)) OnUi(()=>Status="Recepção sobrecarregada. Saia da sala e tente novamente."); };
    }
    private async Task StartCaptureAsync() {
        if (_peer is null || !_peer.IsConnected) throw new InvalidOperationException("Entre em uma sala e aguarde a conexão antes de compartilhar.");
        StopCapture(); var source=new WindowsCaptureSource();
        try { if (!await source.PickAsync(_owner(), 30)) { source.Dispose(); Status=Strings.CaptureCancelled; return; } }
        catch { source.Dispose(); throw; }
        _capture=source; _captureCancellation=new(); _encoder=new H264Encoder();
        _capture.Error += Error; _encoder.Error += Error; _encoder.EncoderSelected += hardware => OnUi(() => Status = hardware ? Strings.HardwareSharing : Strings.SoftwareSharing);
        _encoder.AccessUnit += bytes => _peer?.Send(bytes,30);
        var encoder=_encoder; var token=_captureCancellation.Token;
        _=Task.Run(async()=>{ try { await encoder.RunAsync(source.Frames,source.Width,source.Height,30,token); } catch (Exception ex) { Error(ex.Message); } });
        source.Start(); Status=Strings.Sharing;
    }
    private void StartDecoder() {
        _decoder=new H264Decoder(); _decoder.Error+=Error;
        _decoder.Frame+=pixels=>{ lock(this) { if (_uiFramePending) return; _uiFramePending=true; }
            _dispatcher.BeginInvoke(()=>{ try { var image=BitmapSource.Create(1280,720,96,96,PixelFormats.Bgra32,null,pixels,1280*4); image.Freeze(); Frame=image; } finally {lock(this) _uiFramePending=false;} }); };
        var decoder=_decoder; var token=_sessionCancellation!.Token;
        _=Task.Run(async()=>{try {await decoder.RunAsync(token);} catch(Exception ex){Error(ex.Message);} });
    }
    private void Error(string text) { try { var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Glimpse"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir,"errors.log"),$"{DateTimeOffset.Now:O} {text}{Environment.NewLine}"); } catch { } OnUi(()=>Status="Ocorreu um erro. Verifique o registro do aplicativo."); }
    private void OnUi(Action action) { if (_dispatcher.CheckAccess()) action(); else _dispatcher.BeginInvoke(action); }
    private void StopCapture() { _captureCancellation?.Cancel(); _capture?.Dispose(); _encoder?.Dispose(); _capture=null; _encoder=null; _captureCancellation?.Dispose(); _captureCancellation=null; }
    private void Reset() { _roomHost?.Dispose(); _roomHost=null; StopCapture(); _sessionCancellation?.Cancel(); _decoder?.Dispose(); _peer?.Dispose(); _decoder=null; _peer=null; _sessionCancellation?.Dispose(); _sessionCancellation=null; Frame=null; LocalText=""; }
    public void Dispose()=>Reset();
    private void Set<T>(ref T field,T value,[CallerMemberName]string? name=null) { if (!EqualityComparer<T>.Default.Equals(field,value)) {field=value;Notify(name);} }
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    private sealed class AsyncCommand(Func<Task> run) : ICommand {
        private bool _busy; public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p)=>!_busy;
        public async void Execute(object? p) { if(_busy)return; _busy=true; CanExecuteChanged?.Invoke(this,EventArgs.Empty); try {await run();} catch(Exception ex){MessageBox.Show(ex.Message,"Glimpse",MessageBoxButton.OK,MessageBoxImage.Error);} finally {_busy=false;CanExecuteChanged?.Invoke(this,EventArgs.Empty);} }
    }
}
