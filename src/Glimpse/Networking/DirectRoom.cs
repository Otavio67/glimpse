using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Glimpse.Networking;

// Local-network room: the code carries an address, certificate fingerprint and secret.
public sealed class DirectRoomHost : IDisposable
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _certificate;
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);
    private readonly CancellationTokenSource _cancel = new();
    private TcpClient? _client;
    public string Code { get; }
    public event Action<string>? Error;
    public DirectRoomHost(IPAddress? address = null)
    {
        address ??= GetLocalAddress();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Glimpse local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(12));
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pfx = temporary.Export(X509ContentType.Pfx, password);
        try { _certificate = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        _listener = new TcpListener(IPAddress.Any, 0); _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        byte[] payload = new byte[70]; address.GetAddressBytes().CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4,2), (ushort)port);
        SHA256.HashData(_certificate.RawData).CopyTo(payload,6); _secret.CopyTo(payload,38);
        Code = "GLM-" + Convert.ToBase64String(payload).TrimEnd('=').Replace('+','-').Replace('/','_');
    }
    public async Task WaitForGuestAsync(LocalPeer peer)
    {
        try {
            _client = await _listener.AcceptTcpClientAsync(_cancel.Token);
            using var stream = new SslStream(_client.GetStream(), false);
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, _cancel.Token);
            byte[] token = new byte[32]; await stream.ReadExactlyAsync(token, _cancel.Token);
            if (!CryptographicOperations.FixedTimeEquals(token, _secret)) throw new InvalidDataException("Código de sala inválido.");
            string offer = await Task.Run(peer.CreateOfferAsync, _cancel.Token);
            await RoomWire.WriteAsync(stream, offer, _cancel.Token);
            string answer = await RoomWire.ReadAsync(stream, _cancel.Token);
            peer.AcceptAnswer(answer);
        } catch(OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch(Exception) { if (!_cancel.IsCancellationRequested) Error?.Invoke("Não foi possível conectar à sala. Confira o código e a rede."); }
        finally { try { _listener.Stop(); } catch { } _client?.Dispose(); }
    }
    private static IPAddress GetLocalAddress() {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)) {
            var props=adapter.GetIPProperties(); if (!props.GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
            var addr=props.UnicastAddresses.Select(x=>x.Address).FirstOrDefault(x=>x.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(x)); if(addr is not null)return addr;
        }
        throw new InvalidOperationException("Não foi encontrado um endereço IPv4 de rede local.");
    }
    public void Dispose() { _cancel.Cancel(); _listener.Stop(); _client?.Dispose(); _certificate.Dispose(); _cancel.Dispose(); }
}
public static class DirectRoomGuest
{
    public static async Task JoinAsync(string code, LocalPeer peer, CancellationToken token)
    {
        if (code.Length > 256 || !code.StartsWith("GLM-", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Informe um código de sala válido.");
        byte[] data;
        try { string b64=code[4..].Trim().Replace('-','+').Replace('_','/'); b64=b64.PadRight((b64.Length+3)/4*4,'='); data=Convert.FromBase64String(b64); }
        catch(FormatException){throw new InvalidDataException("O código de sala está incompleto ou inválido.");}
        if(data.Length!=70) throw new InvalidDataException("O código de sala está incompleto ou inválido.");
        var ip=new IPAddress(data.AsSpan(0,4)); int port=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4,2)); if(port==0) throw new InvalidDataException("Porta inválida no código de sala.");
        byte[] pin=data.AsSpan(6,32).ToArray(), secret=data.AsSpan(38,32).ToArray();
        using var tcp=new TcpClient(); await tcp.ConnectAsync(ip,port,token);
        using var stream=new SslStream(tcp.GetStream(), false, (_, cert, _, _) => cert is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), pin));
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost="Glimpse local", EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13 },token);
        await stream.WriteAsync(secret,token);
        string offer=await RoomWire.ReadAsync(stream,token);
        string answer=await Task.Run(()=>peer.AcceptOfferAsync(offer),token);
        await RoomWire.WriteAsync(stream,answer,token);
    }
}
internal static class RoomWire {
    public static async Task WriteAsync(Stream stream,string value,CancellationToken token) { var bytes=System.Text.Encoding.UTF8.GetBytes(value); if(bytes.Length>150000)throw new InvalidDataException("Dados de conexão muito grandes."); byte[] header=new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header,bytes.Length); await stream.WriteAsync(header,token); await stream.WriteAsync(bytes,token); await stream.FlushAsync(token); }
    public static async Task<string> ReadAsync(Stream stream,CancellationToken token) { byte[] header=new byte[4]; await stream.ReadExactlyAsync(header,token); int length=BinaryPrimitives.ReadInt32BigEndian(header); if(length<1||length>150000)throw new InvalidDataException("Dados de conexão inválidos."); byte[] bytes=new byte[length]; await stream.ReadExactlyAsync(bytes,token); return System.Text.Encoding.UTF8.GetString(bytes); }
}

