using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using TextEncoding=System.Text.Encoding;

namespace Glimpse.Networking;

internal sealed class ShortRoomInvite : IDisposable
{
    private static readonly HttpClient Http=new(new HttpClientHandler { AllowAutoRedirect=false });
    private readonly CancellationTokenSource _stop=new();
    private int _disposed;
    private ShortRoomInvite(string code) { Code=code; }
    public string Code { get; }

    public static bool IsCode(string code) => code.Length==16 && code.All(c=>char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static string CodeFor(byte[] invite) => Convert.ToBase64String(SHA256.HashData(invite),0,12).Replace('+','-').Replace('/','_');
    private static byte[] Key(string code) => SHA256.HashData(TextEncoding.ASCII.GetBytes("Glimpse invite encryption v2:"+code));
    private static Uri Address(Uri endpoint,string code) => new(endpoint,"v2/invites/"+Convert.ToHexStringLower(SHA256.HashData(TextEncoding.ASCII.GetBytes(code))));

    public static async Task<ShortRoomInvite> PublishAsync(Uri endpoint,string invite,CancellationToken token)
    {
        var plain=TextEncoding.ASCII.GetBytes(invite);
        string code=CodeFor(plain);
        var sealedBytes=new byte[28+plain.Length];
        RandomNumberGenerator.Fill(sealedBytes.AsSpan(0,12));
        using(var aes=new AesGcm(Key(code),16)) aes.Encrypt(sealedBytes.AsSpan(0,12),plain,sealedBytes.AsSpan(28),sealedBytes.AsSpan(12,16));
        var sealedText=Convert.ToBase64String(sealedBytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        var address=Address(endpoint,code);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        var socket=await OpenAsync(address,sealedText,deadline.Token);
        var registration=new ShortRoomInvite(code);
        _=registration.KeepAliveAsync(socket,address,sealedText,token);
        return registration;
    }

    public static async Task<string> ResolveAsync(Uri endpoint,string code,CancellationToken token)
    {
        if(!IsCode(code)) throw new InvalidDataException("O código curto da sala é inválido.");
        var address=new UriBuilder(Address(endpoint,code)) { Scheme=endpoint.Scheme=="wss" ? "https" : "http" }.Uri;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        try {
            using var response=await Http.GetAsync(address,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound)
                throw new InvalidDataException("A sala não está aberta. Peça um link novo ao anfitrião.");
            response.EnsureSuccessStatusCode();
            using var body=await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer=new byte[513];
            int length=await body.ReadAtLeastAsync(buffer,buffer.Length,false,deadline.Token);
            if(length is <80 or >512) throw new InvalidDataException("Resposta inválida para o link da sala.");
            var encoded=TextEncoding.ASCII.GetString(buffer,0,length).Replace('-','+').Replace('_','/');
            var sealedBytes=Convert.FromBase64String(encoded.PadRight((encoded.Length+3)/4*4,'='));
            var plain=new byte[sealedBytes.Length-28];
            using(var aes=new AesGcm(Key(code),16)) aes.Decrypt(sealedBytes.AsSpan(0,12),sealedBytes.AsSpan(28),sealedBytes.AsSpan(12,16),plain);
            if(CodeFor(plain)!=code) throw new InvalidDataException("Não foi possível confirmar o link da sala.");
            var invite=TextEncoding.ASCII.GetString(plain);
            NativeRoomInvite.Parse(invite);
            return invite;
        }
        catch(Exception ex) when(!token.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException) {
            throw new IOException("Não foi possível consultar o link da sala. Confira se a sala continua aberta e tente novamente.",ex);
        }
        catch(Exception ex) when(ex is CryptographicException or FormatException) {
            throw new InvalidDataException("Não foi possível confirmar o link da sala.",ex);
        }
    }

    private static async Task<ClientWebSocket> OpenAsync(Uri address,string sealedText,CancellationToken token)
    {
        var socket=RoomRelay.CreateSocket();
        socket.Options.SetRequestHeader("X-Glimpse-Invite",sealedText);
        try {
            await socket.ConnectAsync(address,token);
            await RoomRelay.ExpectAsync(socket,"waiting",token);
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    private async Task KeepAliveAsync(ClientWebSocket first,Uri address,string sealedText,CancellationToken roomToken)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token,roomToken);
        var token=linked.Token;
        ClientWebSocket? socket=first;
        try {
            while(!token.IsCancellationRequested) {
                try {
                    if(socket is null) {
                        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(5));
                        socket=await OpenAsync(address,sealedText,deadline.Token);
                    }
                    await RoomRelay.ExpectAsync(socket,"",token);
                }
                catch(Exception ex) when(ex is IOException or WebSocketException or HttpRequestException or OperationCanceledException) { }
                finally { socket?.Dispose(); socket=null; }
                await Task.Delay(TimeSpan.FromSeconds(2),token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        finally { socket?.Dispose(); }
    }

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)==0) { _stop.Cancel(); _stop.Dispose(); }
    }
}
