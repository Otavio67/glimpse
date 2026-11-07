using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

namespace Glimpse.Networking;

public sealed record NativeRoomInvite(IPAddress Address, int Port, byte[] Fingerprint, byte[] Secret, IPAddress? IPv6Address=null)
{
    public string Encode()
    {
        if(Address.AddressFamily!=AddressFamily.InterNetwork || Port is <1 or >65535 || Fingerprint.Length!=32 || Secret.Length!=32) throw new InvalidDataException("Dados de convite inválidos.");
        if(IPv6Address is not null && !IsValidIPv6(IPv6Address)) throw new InvalidDataException("Endereço IPv6 inválido no convite.");
        var data=new byte[IPv6Address is null ? 70 : 86]; Address.GetAddressBytes().CopyTo(data,0);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)Port); Fingerprint.CopyTo(data,6); Secret.CopyTo(data,38);
        IPv6Address?.GetAddressBytes().CopyTo(data,70);
        return "GLN-"+Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');
    }
    public static NativeRoomInvite Parse(string code)
    {
        code=code.Trim();
        if(code.Length>128 || !code.StartsWith("GLN-",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Informe um convite de sala hospedada no Glimpse (GLN-...).");
        byte[] data;
        try { var value=code[4..].Replace('-','+').Replace('_','/'); data=Convert.FromBase64String(value.PadRight((value.Length+3)/4*4,'=')); }
        catch(FormatException) { throw new InvalidDataException("Convite incompleto ou inválido."); }
        if(data.Length is not (70 or 86)) throw new InvalidDataException("Convite incompleto ou inválido.");
        int port=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        var address=new IPAddress(data.AsSpan(0,4));
        if(port==0 || address.Equals(IPAddress.Any) || address.GetAddressBytes()[0]>=224) throw new InvalidDataException("Endereço ou porta inválidos no convite.");
        var ipv6=data.Length==86 ? new IPAddress(data.AsSpan(70,16)) : null;
        if(ipv6 is not null && !IsValidIPv6(ipv6)) throw new InvalidDataException("Endereço IPv6 inválido no convite.");
        return new(address,port,data.AsSpan(6,32).ToArray(),data.AsSpan(38,32).ToArray(),ipv6);
    }
    internal static bool IsGlobalIPv6(IPAddress address) => address.AddressFamily==AddressFamily.InterNetworkV6 &&
        address.ScopeId==0 && (address.GetAddressBytes()[0]&0xe0)==0x20;
    private static bool IsValidIPv6(IPAddress address) => address.Equals(IPAddress.IPv6Loopback) || IsGlobalIPv6(address);
}

public sealed class NativeRoomHost : IDisposable
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _certificate;
    private readonly byte[] _secret=RandomNumberGenerator.GetBytes(32);
    private readonly CancellationTokenSource _stop=new();
    private readonly CancellationToken _token;
    private int _disposed;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public NativeRoomHost(int port=45873, IPAddress? bindAddress=null, bool enableIPv6=false)
    {
        _token=_stop.Token;
        using var rsa=RSA.Create(2048);
        var request=new CertificateRequest("CN=Glimpse native",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var temporary=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(12));
        var pfx=temporary.Export(X509ContentType.Pfx);
        try { _certificate=X509CertificateLoader.LoadPkcs12(pfx,null,X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        bool dualStack=bindAddress is null && enableIPv6 && Socket.OSSupportsIPv6;
        _listener=new TcpListener(bindAddress??(dualStack ? IPAddress.IPv6Any : IPAddress.Any),port);
        try {
            if(dualStack) _listener.Server.DualMode=true;
            _listener.Start();
        } catch { _listener.Stop(); _certificate.Dispose(); _stop.Dispose(); throw; }
    }
    public string Invite(IPAddress address, int? publicPort=null, IPAddress? ipv6=null) => new NativeRoomInvite(address,publicPort??Port,SHA256.HashData(_certificate.RawData),_secret,ipv6).Encode();
    public async Task<NativeRoomConnection> AcceptAsync(CancellationToken token,Func<bool>? canJoin=null)
    {
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token,_token);
        try {
            while(true) {
                var client=await _listener.AcceptTcpClientAsync(lifetime.Token);
                var stream=new SslStream(client.GetStream(),false);
                bool transferred=false;
                try {
                    using var attempt=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); attempt.CancelAfter(TimeSpan.FromSeconds(15));
                    await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate=_certificate, EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13 },attempt.Token);
                    var secret=new byte[32]; await stream.ReadExactlyAsync(secret,attempt.Token);
                    if(!CryptographicOperations.FixedTimeEquals(secret,_secret)) continue;
                    if(canJoin is not null && !canJoin()) { await stream.WriteAsync(new byte[]{2},attempt.Token); continue; }
                    await stream.WriteAsync(new byte[]{1},attempt.Token);
                    transferred=true;
                    return new NativeRoomConnection(client,stream);
                }
                catch(Exception ex) when(!lifetime.IsCancellationRequested && ex is IOException or AuthenticationException or OperationCanceledException or SocketException) { }
                finally { if(!transferred) { stream.Dispose(); client.Dispose(); } }
            }
        }
        finally { if(lifetime.IsCancellationRequested) _listener.Stop(); }
    }
    public static IPAddress LocalAddress()
    {
        // Let Windows choose the IPv4 route instead of relying on adapter enumeration order.
        // Connecting a UDP socket here selects a local endpoint without sending a datagram.
        if(RouteAddress(IPAddress.Parse("1.1.1.1")) is { } routed && !routed.Equals(IPAddress.Any)) return routed;
        foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType!=NetworkInterfaceType.Loopback)) {
            var properties=adapter.GetIPProperties();
            var address=properties.UnicastAddresses.Select(a=>a.Address).FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            if(address is not null) return address;
        }
        throw new IOException("Nenhuma rede IPv4 ativa foi encontrada.");
    }
    public static IPAddress? PublicIPv6Address() => Socket.OSSupportsIPv6 &&
        RouteAddress(IPAddress.Parse("2606:4700:4700::1111")) is { } address && NativeRoomInvite.IsGlobalIPv6(address) ? address : null;

    private static IPAddress? RouteAddress(IPAddress destination)
    {
        try {
            using var route=new Socket(destination.AddressFamily,SocketType.Dgram,ProtocolType.Udp);
            route.Connect(new IPEndPoint(destination,443));
            return (route.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch(SocketException) { return null; }
    }
    public static bool IsAssignedLocalAddress(IPAddress address) =>
        address.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) &&
        NetworkInterface.GetAllNetworkInterfaces().Any(adapter=>adapter.OperationalStatus==OperationalStatus.Up &&
            adapter.GetIPProperties().UnicastAddresses.Any(item=>item.Address.Equals(address)));
    public void Dispose() { if(Interlocked.Exchange(ref _disposed,1)!=0) return; _stop.Cancel(); _listener.Stop(); _certificate.Dispose(); _stop.Dispose(); }
}

public sealed class NativeRoomConnection : IDisposable
{
    private const int MaxFrame=2_000_000;
    private readonly TcpClient _client;
    private readonly SslStream _stream;
    private readonly CancellationTokenSource _stop=new();
    private readonly CancellationToken _token;
    private readonly Channel<(byte Type, byte[] Payload)> _outgoing=Channel.CreateBounded<(byte,byte[])>(new BoundedChannelOptions(8){FullMode=BoundedChannelFullMode.Wait,SingleReader=true});
    private int _disposed, _started;
    public event Action<byte[]>? VideoFrame;
    public event Action? SharingStopped;
    public event Action? SharingStarted;
    public event Action? PresentationRevoked;
    public event Action<int>? ParticipantCountChanged;
    public event Action<string>? Closed;
    public bool IsConnected => Volatile.Read(ref _disposed)==0;
    internal NativeRoomConnection(TcpClient client,SslStream stream) { _client=client; _client.NoDelay=true; _stream=stream; _token=_stop.Token; }
    public static async Task<NativeRoomConnection> JoinAsync(string code,CancellationToken token)
    {
        var invite=NativeRoomInvite.Parse(code);
        if(invite.IPv6Address is not { } ipv6 || !Socket.OSSupportsIPv6)
            return await JoinEndpointAsync(invite,invite.Address,25,token);

        // Prefer a known public IPv4 route. Otherwise try IPv6 before the LAN fallback.
        var preferIPv4=RouterPortMapping.IsPublicIpv4(invite.Address);
        var first=preferIPv4 ? invite.Address : ipv6;
        var second=preferIPv4 ? ipv6 : invite.Address;
        try { return await JoinEndpointAsync(invite,first,8,token); }
        catch(Exception ex) when(!token.IsCancellationRequested && ex is TimeoutException or AuthenticationException or NativeRoomRouteException) {
            return await JoinEndpointAsync(invite,second,10,token);
        }
    }

    private static async Task<NativeRoomConnection> JoinEndpointAsync(NativeRoomInvite invite,IPAddress address,int seconds,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        var client=new TcpClient(address.AddressFamily); SslStream? stream=null;
        bool authenticated=false;
        try {
            await client.ConnectAsync(address,invite.Port,deadline.Token);
            stream=new SslStream(client.GetStream(),false,(_,certificate,_,_)=>certificate is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()),invite.Fingerprint));
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost="Glimpse native", EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13 },deadline.Token);
            authenticated=true;
            await stream.WriteAsync(invite.Secret,deadline.Token);
            var acknowledgement=new byte[1]; await stream.ReadExactlyAsync(acknowledgement,deadline.Token);
            if(acknowledgement[0]==2) throw new IOException("A sala está cheia. O limite é de oito pessoas, incluindo o anfitrião.");
            if(acknowledgement[0]!=1) throw new InvalidDataException("A sala não confirmou a entrada.");
            return new(client,stream);
        }
        catch(Exception ex) {
            stream?.Dispose(); client.Dispose();
            if(token.IsCancellationRequested) throw;
            if(!authenticated && (ex is SocketException || deadline.IsCancellationRequested)) {
                var message=$"O anfitrião não respondeu na porta TCP {invite.Port}. Peça um convite da sala aberta agora. Se usarem rede local ou VPN, conectem-se à mesma rede. Pela internet direta, confira o encaminhamento da porta no roteador e a permissão do Glimpse no firewall.";
                if(deadline.IsCancellationRequested || ex is SocketException { SocketErrorCode:SocketError.TimedOut }) throw new TimeoutException(message,ex);
                throw new NativeRoomRouteException(message,ex);
            }
            if(ex is AuthenticationException) throw new AuthenticationException("Não foi possível confirmar a identidade da sala. Peça ao anfitrião para copiar o convite da sala aberta agora.",ex);
            if(!authenticated && ex is IOException) throw new NativeRoomRouteException("A conexão foi interrompida antes de confirmar a sala. Peça o convite da sala aberta agora.",ex);
            if(ex is EndOfStreamException) throw new IOException("O anfitrião encerrou a tentativa de entrada. Confira se a sala segue aberta e peça um novo convite.",ex);
            if(deadline.IsCancellationRequested) throw new TimeoutException("O anfitrião recebeu a conexão, mas não confirmou a entrada a tempo.",ex);
            throw;
        }
    }
    private sealed class NativeRoomRouteException(string message,Exception inner) : IOException(message,inner);
    public bool TrySendVideo(byte[] frame) => Queue(1,frame);
    public void NotifySharingStopped() => Queue(2,Array.Empty<byte>());
    public void NotifySharingStarted() => Queue(4,Array.Empty<byte>());
    public void RevokePresentation() => Queue(5,Array.Empty<byte>());
    public void SendParticipantCount(int count) { var payload=new byte[4]; BinaryPrimitives.WriteInt32BigEndian(payload,count); Queue(6,payload); }
    private bool Queue(byte type,byte[] payload)
    {
        if(!IsConnected) return false;
        if(payload.Length>MaxFrame || !_outgoing.Writer.TryWrite((type,payload))) { Fail("A conexão não acompanhou o vídeo. A sala foi encerrada para evitar atraso acumulado."); return false; }
        return true;
    }
    public async Task RunAsync()
    {
        if(Interlocked.Exchange(ref _started,1)!=0) throw new InvalidOperationException("A conexão já foi iniciada.");
        var reader=ReadAsync(); var writer=WriteAsync(); var heartbeat=HeartbeatAsync();
        try { await Task.WhenAll(reader,writer,heartbeat); }
        catch(Exception ex) { if(!_token.IsCancellationRequested) Fail(ex is OperationCanceledException ? "O outro participante não respondeu. A sala foi encerrada." : "A conexão com o outro participante foi encerrada."); }
        finally { Dispose(); _stop.Dispose(); }
    }
    private async Task ReadAsync()
    {
        try {
            while(true) {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_token); deadline.CancelAfter(TimeSpan.FromSeconds(35));
                var header=new byte[5]; await _stream.ReadExactlyAsync(header,deadline.Token);
                int size=BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
                if(size<0 || size>MaxFrame || header[0] is <1 or >6 || (header[0] is >=2 and <=5 && size!=0) || (header[0]==6 && size!=4) || (header[0]==1 && size==0)) throw new InvalidDataException("Pacote de sala inválido.");
                var payload=new byte[size]; await _stream.ReadExactlyAsync(payload,deadline.Token);
                if(header[0]==1) VideoFrame?.Invoke(payload);
                else if(header[0]==2) SharingStopped?.Invoke();
                else if(header[0]==4) SharingStarted?.Invoke();
                else if(header[0]==5) PresentationRevoked?.Invoke();
                else if(header[0]==6) { int count=BinaryPrimitives.ReadInt32BigEndian(payload); if(count is <1 or >8) throw new InvalidDataException("Quantidade de participantes inválida."); ParticipantCountChanged?.Invoke(count); }
            }
        } catch { if(!_token.IsCancellationRequested) Fail("A conexão com o outro participante foi interrompida."); throw; }
    }
    private async Task WriteAsync()
    {
        try {
            await foreach(var frame in _outgoing.Reader.ReadAllAsync(_token)) {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
                var header=new byte[5]; header[0]=frame.Type; BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1),frame.Payload.Length);
                await _stream.WriteAsync(header,deadline.Token); await _stream.WriteAsync(frame.Payload,deadline.Token); await _stream.FlushAsync(deadline.Token);
            }
        } catch { if(!_token.IsCancellationRequested) Fail("Não foi possível enviar o vídeo ao outro participante."); throw; }
    }
    private async Task HeartbeatAsync() { try { while(true) { await Task.Delay(TimeSpan.FromSeconds(10),_token); if(!Queue(3,Array.Empty<byte>())) return; } } catch(OperationCanceledException) when(_token.IsCancellationRequested) { } }
    private void Fail(string message) { if(DisposeCore()) Closed?.Invoke(message); }
    private bool DisposeCore() { if(Interlocked.Exchange(ref _disposed,1)!=0) return false; _stop.Cancel(); _outgoing.Writer.TryComplete(); _stream.Dispose(); _client.Dispose(); return true; }
    // Keep CTS alive until its asynchronous users have observed cancellation.
    public void Dispose() => DisposeCore();
}
