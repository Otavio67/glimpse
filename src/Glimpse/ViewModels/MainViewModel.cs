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
using Glimpse.RoomProtocol;
using System.Net;
namespace Glimpse.ViewModels;
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<IntPtr> _owner;
    private readonly Action<bool>? _openRoomDialog;
    private readonly Func<string?>? _chooseShareSource;
    private readonly System.Windows.Threading.Dispatcher _dispatcher = Application.Current.Dispatcher;
    private LocalPeer? _peer;
    private DirectRoomHost? _roomHost;
    private InternetRoomSession? _internetRoom;
    private NativeRoomHost? _nativeHost;
    private NativeRoomHub? _nativeHub;
    private NativeRoomConnection? _nativeConnection;
    private RouterPortMapping? _routerMapping;
    private WindowsCaptureSource? _capture;
    private H264Encoder? _encoder;
    private H264Decoder? _decoder;
    private CancellationTokenSource? _captureCancellation, _sessionCancellation, _decoderCancellation;
    private bool _sender, _uiFramePending;
    private int _sessionVersion;
    private int _connectionVersion;
    private int _decoderVersion, _participantCount;
    private string _status="Pronto para começar", _local="", _remote="";
    private bool _useInternetRooms=false;
    private bool _nativeMode=true, _autoMapPort=true;
    private string _hostAddress="", _hostPort="45873", _networkDetails="Seu PC hospeda a sala. Para receber pela internet, ele precisa estar acessível por uma porta TCP.";
    private string _signalingServer=RoomServerSettings.Load();
    private ImageSource? _frame;
    private bool _isInRoom;
    private bool _isSharingLocal;
    private int _localPreviewPending;
    private WriteableBitmap? _localPreviewBitmap;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Status { get => _status; private set => Set(ref _status,value); }
    public string LocalText { get => _local; private set { Set(ref _local,value); Notify(nameof(InvitationVisibility)); } }
    public string RemoteText { get => _remote; set => Set(ref _remote,value); }
    public bool UseInternetRooms { get=>_useInternetRooms; set { Set(ref _useInternetRooms,value); Notify(nameof(RoomTitle)); Notify(nameof(ConnectionLabel)); Notify(nameof(RoomHint)); } }
    public string SignalingServer { get=>_signalingServer; set=>Set(ref _signalingServer,value); }
    public bool NativeMode { get=>_nativeMode; set { Set(ref _nativeMode,value); Notify(nameof(RoomTitle)); Notify(nameof(ConnectionLabel)); Notify(nameof(RoomHint)); } }
    public bool AutoMapPort { get=>_autoMapPort; set=>Set(ref _autoMapPort,value); }
    public string HostAddress { get=>_hostAddress; set=>Set(ref _hostAddress,value); }
    public string HostPort { get=>_hostPort; set=>Set(ref _hostPort,value); }
    public string NetworkDetails { get=>_networkDetails; private set=>Set(ref _networkDetails,value); }
    public bool CanConfigureRoom => _sessionCancellation is null;
    public bool IsInRoom { get=>_isInRoom; private set { Set(ref _isInRoom,value); Notify(nameof(RoomControlsVisibility)); Notify(nameof(RoomEntryVisibility)); Notify(nameof(ParticipantsText)); Notify(nameof(WaitingMessage)); } }
    public Visibility RoomControlsVisibility => IsInRoom ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RoomEntryVisibility => IsInRoom ? Visibility.Collapsed : Visibility.Visible;
    public Visibility InvitationVisibility => string.IsNullOrEmpty(LocalText) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsSharingLocal { get=>_isSharingLocal; private set { Set(ref _isSharingLocal,value); Notify(nameof(ShareAreaLabel)); } }
    public string ShareAreaLabel => IsSharingLocal ? "◉  SUA PRÉVIA" : "◉  COMPARTILHAMENTO";
    public string ParticipantsText => _participantCount==0 ? (IsInRoom ? "Sala conectada" : "Nenhuma sala conectada") : $"{_participantCount} de 8 pessoas na sala";
    public string WaitingMessage => IsInRoom ? "Compartilhe sua tela ou aguarde uma apresentação." : "Hospede uma sala ou entre na sala de um amigo.";
    public string RoomTitle => NativeMode ? "Sala direta" : UseInternetRooms ? "Sala pela internet" : "Sala local";
    public string ConnectionLabel => NativeMode ? "↗   Conexão direta" : UseInternetRooms ? "↗   Internet" : "↗   Rede local";
    public string RoomHint => NativeMode ? "Até 8 pessoas. Qualquer participante pode compartilhar a tela, sem pedir permissão. Uma nova apresentação substitui a atual. O anfitrião mantém o PC ligado." : "Duas pessoas na mesma rede local.";
    public ImageSource? Frame { get => _frame; private set { Set(ref _frame,value); Notify(nameof(WaitingVisibility)); } }
    public Visibility WaitingVisibility => Frame is null ? Visibility.Visible : Visibility.Collapsed;
    public ICommand CreateRoomCommand { get; }
    public ICommand OpenHostRoomCommand { get; }
    public ICommand OpenJoinRoomCommand { get; }
    public ICommand JoinRoomCommand { get; }
    public ICommand CreateOfferCommand { get; }
    public ICommand CreateAnswerCommand { get; }
    public ICommand ApplyAnswerCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ShareCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand DisconnectCommand { get; }
    public MainViewModel(Func<IntPtr> owner,Action<bool>? openRoomDialog=null,Func<string?>? chooseShareSource=null) {
        _owner=owner;
        _openRoomDialog=openRoomDialog;
        _chooseShareSource=chooseShareSource;
        OpenHostRoomCommand=new AsyncCommand(()=>{ _openRoomDialog?.Invoke(true); return Task.CompletedTask; });
        OpenJoinRoomCommand=new AsyncCommand(()=>{ _openRoomDialog?.Invoke(false); return Task.CompletedTask; });
        CreateRoomCommand=new AsyncCommand(async()=>{
            if(NativeMode) { await CreateNativeRoomAsync(); return; }
            if (UseInternetRooms) { await OpenInternetRoomAsync(null); return; }
            Reset(); _sender=true; CreatePeer(true);
            try {
                _roomHost=new DirectRoomHost(); StartDecoder();
                var version=_sessionVersion;
                _roomHost.Error += message => OnUi(()=>{ if (version==_sessionVersion) { Reset(); Status=message; } });
                LocalText=_roomHost.Code; Status="Sala criada. Aguardando seu amigo na mesma rede.";
                IsInRoom=true;
                _= _roomHost.WaitForGuestAsync(_peer!);
            }
            catch { Reset(); throw; }
        });
        JoinRoomCommand=new AsyncCommand(async()=>{
            var code=RemoteText.Trim(); if(code.Length==0)throw new InvalidOperationException("Digite o código da sala.");
            if(code.StartsWith("GLN-",StringComparison.OrdinalIgnoreCase)) { await JoinNativeRoomAsync(code); return; }
            if (UseInternetRooms) { await OpenInternetRoomAsync(code); return; }
            Reset(); _sender=false; CreatePeer(false); Status="Entrando na sala...";
            var version=_sessionVersion;
            try {
                StartDecoder();
                await DirectRoomGuest.JoinAsync(code,_peer!,_sessionCancellation!.Token);
                if (version!=_sessionVersion) return;
                Status=_peer!.IsConnected ? "Conectado à sala" : "Conectando...";
            }
            catch(Exception) when (version!=_sessionVersion) { }
            catch(Exception ex) { Reset(); Status=ex.Message; throw; }
        });
        CreateOfferCommand=new AsyncCommand(async()=>{ Reset(); _sender=true; CreatePeer(true); Status=Strings.PreparingOffer; LocalText=await _peer!.CreateOfferAsync(); Status=Strings.SendOffer; });
        CreateAnswerCommand=new AsyncCommand(async()=>{ var offer=RemoteText; Reset(); _sender=false; CreatePeer(false); Status=Strings.PreparingAnswer; LocalText=await _peer!.AcceptOfferAsync(offer); StartDecoder(); Status=Strings.SendAnswer; });
        ApplyAnswerCommand=new AsyncCommand(()=>{ if (_peer is null || !_sender) throw new InvalidOperationException("Crie uma sala primeiro."); _peer.AcceptAnswer(RemoteText); Status=Strings.Connecting; return Task.CompletedTask; });
        CopyCommand=new AsyncCommand(()=>{ if (!string.IsNullOrEmpty(LocalText)) Clipboard.SetText(LocalText); return Task.CompletedTask; });
        ShareCommand=new AsyncCommand(StartCaptureAsync);
        StopCommand=new AsyncCommand(()=>{ StopCapture(); _nativeHub?.StopHostSharing(); _nativeConnection?.NotifySharingStopped(); Status=Strings.Stopped; return Task.CompletedTask; });
        DisconnectCommand=new AsyncCommand(()=>{ Reset(); Status=Strings.Disconnected; return Task.CompletedTask; });
    }
    public void CancelRoomSetup() { if(!IsInRoom) { Reset(); Status="Pronto para começar"; } }
    private async Task CreateNativeRoomAsync() {
        if(!int.TryParse(HostPort,out var port) || port is <1024 or >65535) throw new InvalidOperationException("Escolha uma porta entre 1024 e 65535.");
        IPAddress? advertised=null;
        if(!string.IsNullOrWhiteSpace(HostAddress) && (!IPAddress.TryParse(HostAddress.Trim(),out advertised) || !RouterPortMapping.IsPublicIpv4(advertised))) throw new InvalidOperationException("Informe um IPv4 público válido, ou deixe o endereço em branco para tentar a configuração automática.");
        Reset(); _sessionCancellation=new(); Notify(nameof(CanConfigureRoom));
        var version=_sessionVersion; var token=_sessionCancellation.Token;
        try {
            var local=NativeRoomHost.LocalAddress();
            _nativeHost=new NativeRoomHost(port);
            var host=_nativeHost;
            string detail="";
            if(AutoMapPort) {
                Status="Preparando conexão no roteador...";
                try {
                    var mapping=await RouterPortMapping.OpenAsync(local,port,token);
                    if(version!=_sessionVersion) { mapping.Dispose(); return; }
                    _routerMapping=mapping; advertised??=mapping.ExternalAddress;
                    detail="Porta TCP configurada automaticamente no roteador.";
                }
                catch(Exception ex) when(!token.IsCancellationRequested) { detail=ex is IOException ? ex.Message : "O roteador não confirmou a abertura automática. Configure a porta manualmente."; }
            }
            if(version!=_sessionVersion) return;
            bool internet=advertised is not null;
            LocalText=host.Invite(advertised??local);
            NetworkDetails=$"PC: {local} • TCP: {port}. "+detail+(internet ? " A conexão externa ainda precisa ser testada. Permita o Glimpse no firewall." : " Convite disponível apenas na rede local. Para internet, informe seu IPv4 público e encaminhe esta porta no roteador. CGNAT pode impedir a entrada.");
            Status=internet ? "Sala hospedada no seu PC. Envie o convite ao seu amigo." : "Sala local criada. Internet ainda não configurada.";
            _nativeHub=new NativeRoomHub(host);
            _nativeHub.VideoFrame+=data=>{ if(version==_sessionVersion) _decoder?.TryWrite(data); };
            _nativeHub.SharingStopped+=()=>ResetNativePlayback(version);
            _nativeHub.PresentationRevoked+=()=>OnUi(()=>{ if(version==_sessionVersion) { StopCapture(); Status="Outro participante assumiu a apresentação."; } });
            _nativeHub.ParticipantCountChanged+=count=>OnUi(()=>{ if(version==_sessionVersion) { _participantCount=count; Notify(nameof(ParticipantsText)); } });
            _participantCount=1; Notify(nameof(ParticipantsText)); StartDecoder();
            IsInRoom=true;
            _=AcceptNativeAsync(_nativeHub,version,token);
        }
        catch(Exception) when(version!=_sessionVersion) { }
        catch(Exception ex) { Reset(); Status=ex.Message; throw; }
    }
    private async Task AcceptNativeAsync(NativeRoomHub hub,int version,CancellationToken token) {
        try {
            await hub.RunAsync(token);
        }
        catch(Exception) when(token.IsCancellationRequested) { }
        catch(Exception) { OnUi(()=>{ if(version==_sessionVersion) { Reset(); Status="Não foi possível receber o convidado. Crie uma nova sala."; } }); }
    }
    private async Task JoinNativeRoomAsync(string code) {
        NativeRoomInvite.Parse(code);
        Reset(); _sessionCancellation=new(); Notify(nameof(CanConfigureRoom));
        var version=_sessionVersion;
        Status="Conectando diretamente ao PC anfitrião...";
        try {
            var connection=await NativeRoomConnection.JoinAsync(code,_sessionCancellation.Token);
            if(version!=_sessionVersion) { connection.Dispose(); return; }
            BindNativeConnection(connection,version);
        }
        catch(Exception) when(version!=_sessionVersion) { }
        catch(Exception ex) { Reset(); Status="Não foi possível conectar ao PC anfitrião."; throw new InvalidOperationException(ex is TimeoutException || (ex is IOException && ex.Message.Contains("sala está cheia")) ? ex.Message : "Não foi possível entrar. Confira se o anfitrião mantém a sala aberta, o convite está correto e a porta está liberada no roteador e no firewall.",ex); }
    }
    private void BindNativeConnection(NativeRoomConnection connection,int version) {
        _nativeConnection=connection;
        StartDecoder();
        connection.VideoFrame+=data=>{ if(version==_sessionVersion) _decoder?.TryWrite(data); };
        connection.SharingStopped+=()=>ResetNativePlayback(version);
        connection.PresentationRevoked+=()=>OnUi(()=>{ if(version==_sessionVersion) { StopCapture(); Status="Outro participante assumiu a apresentação."; } });
        connection.ParticipantCountChanged+=count=>OnUi(()=>{ if(version==_sessionVersion) { _participantCount=count; Notify(nameof(ParticipantsText)); } });
        connection.Closed+=message=>OnUi(()=>{ if(version==_sessionVersion) { Reset(); Status=message; } });
        Status="Conectado diretamente ao outro PC";
        IsInRoom=true;
        _=connection.RunAsync();
    }
    private void ResetNativePlayback(int version) {
        void ResetPlayback() { if(version!=_sessionVersion) return; StartDecoder(); Frame=null; Status="A apresentação foi encerrada ou alterada."; }
        // Complete the decoder swap before accepting frames from the next presenter.
        if(_dispatcher.CheckAccess()) ResetPlayback(); else _dispatcher.Invoke(ResetPlayback);
    }
    private async Task OpenInternetRoomAsync(string? code) {
        var server=InternetRoomSession.ServerAddress(SignalingServer);
        if (code is not null) InternetRoomCode.Normalize(code);
        Reset(); _sessionCancellation=new();
        var version=_sessionVersion;
        Status=code is null ? "Criando sala pela internet..." : "Entrando na sala pela internet...";
        try {
            var session=await InternetRoomSession.OpenAsync(server,code,_sessionCancellation.Token);
            if (version!=_sessionVersion) { session.Dispose(); return; }
            _internetRoom=session; _sender=code is null;
            RoomServerSettings.Save(server);
            CreatePeer(_sender,session.Ticket.IceServers); StartDecoder();
            session.Closed+=message=>OnUi(()=>{ if(version==_sessionVersion) { Reset(); Status=message; } });
            LocalText=_sender ? session.Ticket.Code : "";
            if(_sender) IsInRoom=true;
            Status=_sender ? "Sala criada. Envie o código ao seu amigo." : "Conectando à sala pela internet...";
            _=session.RunAsync(_peer!);
        }
        catch(Exception) when (version!=_sessionVersion) { }
        catch(Exception ex) { Reset(); Status=ex switch { System.Net.Http.HttpRequestException => "Serviço de salas indisponível. Confira o endereço e sua conexão.", OperationCanceledException => "O serviço de salas demorou para responder. Tente novamente.", _ => ex.Message }; throw new InvalidOperationException(Status,ex); }
    }
    private void CreatePeer(bool sender, IEnumerable<IceServer>? iceServers=null) {
        _sessionCancellation ??=new();
        var relayOnly=_internetRoom is not null && Environment.GetEnvironmentVariable("GLIMPSE_RELAY_ONLY")=="1";
        _peer=new LocalPeer(sender,iceServers,relayOnly);
        var version=_sessionVersion;
        var peer=_peer;
        var token=_sessionCancellation.Token;
        _peer.StateChanged += state => OnUi(()=>{
            if (version!=_sessionVersion) return;
            var connectionVersion=++_connectionVersion;
            if(state=="connected") IsInRoom=true;
            if (state is "failed" or "closed") {
                Reset(); Status="A conexão foi encerrada. Crie uma nova sala para conectar novamente."; return;
            }
            if (state=="disconnected") Frame=null;
            Status=state switch { "connected" => "Conectado à sala", "connecting" => "Conectando...", "disconnected" => "Conexão interrompida. Tentando recuperar...", _ => "Preparando conexão..." };
            if (state is "connecting" or "disconnected") _=WatchConnectionAsync(peer,version,connectionVersion,token);
        });
        _peer.VideoFrame += data => { if (version==_sessionVersion && _decoder is not null && !_decoder.TryWrite(data)) OnUi(()=>{ if(version==_sessionVersion) Status="Recepção sobrecarregada. Saia da sala e tente novamente."; }); };
    }
    private async Task WatchConnectionAsync(LocalPeer peer, int version, int connectionVersion, CancellationToken token) {
        try {
            await Task.Delay(TimeSpan.FromSeconds(_internetRoom is null ? 30 : 60),token);
            OnUi(()=>{
                if (version!=_sessionVersion || connectionVersion!=_connectionVersion || peer.IsConnected) return;
                Reset(); Status="A conexão não foi estabelecida. Confira a internet ou a rede e crie uma nova sala.";
            });
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task StartCaptureAsync() {
        if (_nativeHub is null && _nativeConnection?.IsConnected!=true && _peer?.IsConnected!=true) throw new InvalidOperationException("Entre em uma sala e aguarde a conexão antes de compartilhar.");
        var choice=_chooseShareSource?.Invoke();
        if(_chooseShareSource is not null && choice is null) return;
        var version=_sessionVersion;
        var peer=_peer;
        var native=_nativeConnection;
        var hub=_nativeHub;
        bool wasSharing=_capture is not null;
        var source=new WindowsCaptureSource();
        Status=choice=="window" ? "No seletor do Windows, escolha a janela para compartilhar." : "No seletor do Windows, escolha a tela para compartilhar.";
        try { if (!await source.PickAsync(_owner(), 30)) { source.Dispose(); if(version==_sessionVersion) Status=IsSharingLocal ? Strings.Sharing : Strings.CaptureCancelled; return; } }
        catch { source.Dispose(); throw; }
        if (version!=_sessionVersion || (hub is null && native?.IsConnected!=true && peer?.IsConnected!=true)) { source.Dispose(); return; }
        StopCapture();
        if(wasSharing) { hub?.StopHostSharing(); native?.NotifySharingStopped(); }
        _capture=source; _captureCancellation=new(); _encoder=new H264Encoder();
        var encoder=_encoder; var token=_captureCancellation.Token;
        _capture.Error += message=>ReportError(message,version); _encoder.Error += message=>ReportError(message,version);
        _encoder.EncoderSelected += hardware => OnUi(() => { if (version==_sessionVersion && !token.IsCancellationRequested) Status = hardware ? Strings.HardwareSharing : Strings.SoftwareSharing; });
        hub?.BeginHostSharing(); native?.NotifySharingStarted();
        IsSharingLocal=true; Frame=null;
        source.PreviewFrame+=(pixels,width,height)=>QueueLocalPreview(source,pixels,width,height,version,token);
        _encoder.AccessUnit += bytes => { if(version==_sessionVersion && !token.IsCancellationRequested) { if(hub is not null) hub.TrySendVideo(bytes); else if(native is not null) native.TrySendVideo(bytes); else peer?.Send(bytes,30); } };
        _=Task.Run(async()=>{ try { await encoder.RunAsync(source.Frames,source.Width,source.Height,30,token); } catch (Exception ex) { if(!token.IsCancellationRequested) ReportError(ex.Message,version); } });
        source.Start(); Status=Strings.Sharing;
    }
    private void QueueLocalPreview(WindowsCaptureSource source,byte[] pixels,int width,int height,int version,CancellationToken token) {
        if(token.IsCancellationRequested || version!=_sessionVersion || Interlocked.CompareExchange(ref _localPreviewPending,1,0)!=0) return;
        _dispatcher.BeginInvoke(()=>{
            try {
                if(token.IsCancellationRequested || version!=_sessionVersion || !ReferenceEquals(_capture,source) || !IsSharingLocal) return;
                if(_localPreviewBitmap is null || _localPreviewBitmap.PixelWidth!=width || _localPreviewBitmap.PixelHeight!=height)
                    _localPreviewBitmap=new WriteableBitmap(width,height,96,96,PixelFormats.Bgra32,null);
                _localPreviewBitmap.WritePixels(new Int32Rect(0,0,width,height),pixels,checked(width*4),0);
                if(!ReferenceEquals(Frame,_localPreviewBitmap)) Frame=_localPreviewBitmap;
            }
            finally { Interlocked.Exchange(ref _localPreviewPending,0); }
        });
    }
    private void StartDecoder() {
        StopDecoder();
        var version=_sessionVersion;
        var decoderVersion=_decoderVersion;
        _decoderCancellation=CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation!.Token);
        _decoder=new H264Decoder(); _decoder.Error+=message=>{ if(decoderVersion==_decoderVersion) ReportError(message,version); };
        _decoder.Frame+=pixels=>{ lock(this) { if (_uiFramePending || IsSharingLocal) return; _uiFramePending=true; }
            _dispatcher.BeginInvoke(()=>{ try { if(version!=_sessionVersion || decoderVersion!=_decoderVersion || IsSharingLocal) return; var image=BitmapSource.Create(1280,720,96,96,PixelFormats.Bgra32,null,pixels,1280*4); image.Freeze(); Frame=image; } finally {lock(this) _uiFramePending=false;} }); };
        var decoder=_decoder; var token=_decoderCancellation.Token;
        _=Task.Run(async()=>{try {await decoder.RunAsync(token);} catch(Exception ex){if(!token.IsCancellationRequested) ReportError(ex.Message,version);} });
    }
    private void StopDecoder() { _decoderVersion++; _decoderCancellation?.Cancel(); _decoder?.Dispose(); _decoder=null; _decoderCancellation?.Dispose(); _decoderCancellation=null; }
    private void ReportError(string text, int version) { if(version!=_sessionVersion) return; try { var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Glimpse"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir,"errors.log"),$"{DateTimeOffset.Now:O} {text}{Environment.NewLine}"); } catch { } OnUi(()=>{ if(version==_sessionVersion) Status="Ocorreu um erro. Verifique o registro do aplicativo."; }); }
    private void OnUi(Action action) { if (_dispatcher.CheckAccess()) action(); else _dispatcher.BeginInvoke(action); }
    private void StopCapture() { bool local=IsSharingLocal; IsSharingLocal=false; _localPreviewBitmap=null; if(local) Frame=null; _captureCancellation?.Cancel(); _capture?.Dispose(); _encoder?.Dispose(); _capture=null; _encoder=null; _captureCancellation?.Dispose(); _captureCancellation=null; }
    private void Reset() { _sessionVersion++; IsInRoom=false; _nativeConnection?.Dispose(); _nativeConnection=null; _nativeHub?.Dispose(); _nativeHub=null; _nativeHost?.Dispose(); _nativeHost=null; _routerMapping?.Dispose(); _routerMapping=null; _internetRoom?.Dispose(); _internetRoom=null; _roomHost?.Dispose(); _roomHost=null; StopCapture(); _sessionCancellation?.Cancel(); StopDecoder(); _peer?.Dispose(); _peer=null; _sessionCancellation?.Dispose(); _sessionCancellation=null; Frame=null; LocalText=""; _participantCount=0; Notify(nameof(ParticipantsText)); Notify(nameof(CanConfigureRoom)); }
    public void Dispose()=>Reset();
    private void Set<T>(ref T field,T value,[CallerMemberName]string? name=null) { if (!EqualityComparer<T>.Default.Equals(field,value)) {field=value;Notify(name);} }
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    private sealed class AsyncCommand(Func<Task> run) : ICommand {
        private bool _busy; public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p)=>!_busy;
        public async void Execute(object? p) { if(_busy)return; _busy=true; CanExecuteChanged?.Invoke(this,EventArgs.Empty); try {await run();} catch(Exception ex){MessageBox.Show(ex.Message,"Glimpse",MessageBoxButton.OK,MessageBoxImage.Error);} finally {_busy=false;CanExecuteChanged?.Invoke(this,EventArgs.Empty);} }
    }
}
