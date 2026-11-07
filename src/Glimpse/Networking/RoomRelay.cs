using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using TextEncoding=System.Text.Encoding;

namespace Glimpse.Networking;

internal static class RoomRelay
{
    public static Uri? Endpoint()
    {
        var value=Environment.GetEnvironmentVariable("GLIMPSE_RELAY_URL");
        if(string.IsNullOrWhiteSpace(value)) {
            var file=Path.Combine(AppContext.BaseDirectory,"relay-url.txt");
            if(!File.Exists(file)) return null;
            try { value=File.ReadAllText(file).Trim(); }
            catch(IOException) { return null; }
        }
        return Uri.TryCreate(value,UriKind.Absolute,out var uri) &&
            (uri.Scheme=="wss" || uri.Scheme=="ws" && uri.IsLoopback) &&
            string.IsNullOrEmpty(uri.UserInfo+uri.Query+uri.Fragment) && uri.AbsolutePath=="/" ? uri : null;
    }

    public static ClientWebSocket CreateSocket()
    {
        var socket=new ClientWebSocket();
        socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout=TimeSpan.FromSeconds(10);
        return socket;
    }

    public static async Task<ClientWebSocket> OpenAsync(Uri endpoint,byte[] id,byte[]? hostKey,CancellationToken token,bool ice=false)
    {
        var socket=CreateSocket();
        if(hostKey is not null) socket.Options.SetRequestHeader("Authorization","Bearer "+Convert.ToHexStringLower(hostKey));
        var role=hostKey is null ? "guest" : "host";
        try {
            await socket.ConnectAsync(new Uri(endpoint,$"v1/rooms/{Convert.ToHexStringLower(id)}/{role}"+(ice ? "?ice=1" : "")),token);
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    public static async Task ExpectAsync(ClientWebSocket socket,string expected,CancellationToken token)
    {
        if(await ReceiveTextAsync(socket,16,token)!=expected)
            throw new IOException("O retransmissor não confirmou a conexão.");
    }

    public static async Task<string> ReceiveTextAsync(ClientWebSocket socket,int maxLength,CancellationToken token)
    {
        var buffer=new byte[maxLength];
        int length=0;
        while(length<buffer.Length) {
            var result=await socket.ReceiveAsync(buffer.AsMemory(length),token);
            if(result.MessageType!=WebSocketMessageType.Text) break;
            length+=result.Count;
            if(result.EndOfMessage) return TextEncoding.UTF8.GetString(buffer,0,length);
        }
        throw new IOException("Resposta de sinalização inválida.");
    }

    public static ValueTask SendTextAsync(ClientWebSocket socket,string message,CancellationToken token) =>
        socket.SendAsync(TextEncoding.UTF8.GetBytes(message).AsMemory(),WebSocketMessageType.Text,true,token);
}

internal sealed class RelayHost : IDisposable
{
    private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);
    private readonly CancellationTokenSource _stop=new();
    private int _disposed;
    private RelayHost() { Id=SHA256.HashData(_key); }
    public byte[] Id { get; }

    public static async Task<RelayHost> StartAsync(Uri endpoint,int port,CancellationToken token,byte[]? iceKey=null)
    {
        var host=new RelayHost();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try {
            var first=await host.WaitingSocketAsync(endpoint,deadline.Token);
            _=host.RunAsync(endpoint,port,first,token,iceKey);
            return host;
        }
        catch { host._stop.Dispose(); CryptographicOperations.ZeroMemory(host._key); throw; }
    }

    private async Task<ClientWebSocket> WaitingSocketAsync(Uri endpoint,CancellationToken token)
    {
        var socket=await RoomRelay.OpenAsync(endpoint,Id,_key,token);
        try { await RoomRelay.ExpectAsync(socket,"waiting",token); return socket; }
        catch { socket.Dispose(); throw; }
    }

    private async Task RunAsync(Uri endpoint,int port,ClientWebSocket first,CancellationToken roomToken,byte[]? iceKey)
    {
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(roomToken,_stop.Token);
        var token=lifetime.Token;
        var forwarding=new List<Task>();
        ClientWebSocket? pending=first;
        try {
            while(!token.IsCancellationRequested) {
                try {
                    if(pending is null) {
                        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(5));
                        pending=await WaitingSocketAsync(endpoint,deadline.Token);
                    }
                    string mode=await RoomRelay.ReceiveTextAsync(pending,16,token);
                    if(mode!="ready" && (mode!="ice" || iceKey is null)) throw new IOException("Modo de conexão desconhecido.");
                    forwarding.RemoveAll(task=>task.IsCompletedSuccessfully);
                    forwarding.Add(ForwardAsync(pending,port,token,mode=="ice" ? iceKey : null));
                    pending=null;
                }
                catch(Exception ex) when(ex is IOException or WebSocketException or OperationCanceledException or HttpRequestException) {
                    pending?.Dispose(); pending=null;
                    await Task.Delay(TimeSpan.FromSeconds(2),token);
                }
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        finally {
            pending?.Dispose();
            await lifetime.CancelAsync();
            await Task.WhenAll(forwarding);
            CryptographicOperations.ZeroMemory(_key);
        }
    }

    private static async Task ForwardAsync(ClientWebSocket socket,int port,CancellationToken token,byte[]? iceKey)
    {
        using var signaling=socket;
        using var local=new TcpClient(AddressFamily.InterNetwork) { NoDelay=true };
        using var pair=CancellationTokenSource.CreateLinkedTokenSource(token);
        try {
            using var remote=await OpenTransportAsync(socket,iceKey,pair.Token);
            await local.ConnectAsync(IPAddress.Loopback,port,pair.Token);
            using var stream=local.GetStream();
            var inbound=remote.CopyToAsync(stream,32768,pair.Token);
            var outbound=stream.CopyToAsync(remote,32768,pair.Token);
            await Task.WhenAny(inbound,outbound);
            await pair.CancelAsync();
            await Task.WhenAll(inbound,outbound);
        }
        catch(Exception ex) when(ex is IOException or SocketException or WebSocketException or OperationCanceledException or InvalidOperationException) { }
    }

    private static async Task<Stream> OpenTransportAsync(ClientWebSocket socket,byte[]? iceKey,CancellationToken token)
    {
        if(iceKey is null) return new RelayStream(socket);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        var direct=await IceRoomStream.ConnectAsync(socket,false,iceKey,deadline.Token);
        socket.Dispose();
        return direct;
    }

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)==0) { _stop.Cancel(); _stop.Dispose(); }
    }
}

internal sealed class RelayStream(ClientWebSocket socket) : Stream
{
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get=>throw new NotSupportedException(); set=>throw new NotSupportedException(); }
    public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Flush() { }
    public override int Read(byte[] buffer,int offset,int count) => ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] buffer,int offset,int count) => WriteAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token) => ReadAsync(buffer.AsMemory(offset,count),token).AsTask();
    public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken token) => WriteAsync(buffer.AsMemory(offset,count),token).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
    {
        if(buffer.IsEmpty) return 0;
        while(true) {
            var message=await socket.ReceiveAsync(buffer,cancellationToken);
            if(message.MessageType==WebSocketMessageType.Close) return 0;
            if(message.MessageType!=WebSocketMessageType.Binary) throw new IOException("Resposta inválida do retransmissor.");
            if(message.Count!=0) return message.Count;
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken cancellationToken=default)
    {
        while(!buffer.IsEmpty) {
            int count=Math.Min(buffer.Length,32768);
            await socket.SendAsync(buffer[..count],WebSocketMessageType.Binary,true,cancellationToken);
            buffer=buffer[count..];
        }
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing) socket.Dispose();
        base.Dispose(disposing);
    }
}
