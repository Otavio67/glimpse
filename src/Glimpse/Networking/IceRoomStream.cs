using System.Net.WebSockets;
using System.Security.Cryptography;
using TextEncoding=System.Text.Encoding;
using System.Threading.Channels;
using SIPSorcery.Net;

namespace Glimpse.Networking;

// A reliable ordered WebRTC channel carries the existing room TLS stream over UDP.
internal sealed class IceRoomStream : Stream
{
    private const int PacketSize=16384;
    private const string Label="glimpse-room-v1";
    private readonly RTCPeerConnection _peer;
    private readonly TaskCompletionSource _opened=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<byte[]> _incoming=Channel.CreateBounded<byte[]>(256);
    private ReadOnlyMemory<byte> _buffer;
    private RTCDataChannel? _channel;
    private int _closed;

    private IceRoomStream()
    {
        _peer=new RTCPeerConnection(new RTCConfiguration {
            iceServers=[new RTCIceServer { urls="stun:stun.cloudflare.com:3478" }],
            X_GatherTimeoutMs=2500
        });
        _peer.onconnectionstatechange+=state=>{
            if(state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
                End(new IOException("A conexão direta por ICE foi encerrada."));
        };
    }

    public static byte[] Key(byte[] secret) => HMACSHA256.HashData(secret,"Glimpse ICE signaling v1"u8);

    public static async Task<IceRoomStream> ConnectAsync(ClientWebSocket signaling,bool guest,byte[] key,CancellationToken token)
    {
        var stream=new IceRoomStream();
        try {
            var peer=stream._peer;
            if(guest) {
                stream.Attach(await peer.createDataChannel(Label,new RTCDataChannelInit { ordered=true }).WaitAsync(token));
                var offer=peer.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete=true });
                await peer.setLocalDescription(offer).WaitAsync(token);
                await RoomRelay.SendTextAsync(signaling,Seal(peer.localDescription.sdp.ToString(),key,true),token);
                stream.SetRemote(RTCSdpType.answer,Unseal(await RoomRelay.ReceiveTextAsync(signaling,16384,token),key,false));
            } else {
                peer.ondatachannel+=stream.Attach;
                stream.SetRemote(RTCSdpType.offer,Unseal(await RoomRelay.ReceiveTextAsync(signaling,16384,token),key,true));
                var answer=peer.createAnswer(new RTCAnswerOptions { X_WaitForIceGatheringToComplete=true });
                await peer.setLocalDescription(answer).WaitAsync(token);
                await RoomRelay.SendTextAsync(signaling,Seal(peer.localDescription.sdp.ToString(),key,false),token);
            }
            await stream._opened.Task.WaitAsync(token);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private static string Seal(string sdp,byte[] key,bool offer)
    {
        var plain=TextEncoding.UTF8.GetBytes(sdp);
        if(plain.Length>12000) throw new IOException("Sinalização ICE muito grande.");
        var packet=new byte[28+plain.Length];
        RandomNumberGenerator.Fill(packet.AsSpan(0,12));
        using var aes=new AesGcm(key,16);
        aes.Encrypt(packet.AsSpan(0,12),plain,packet.AsSpan(28),packet.AsSpan(12,16),offer ? "offer"u8 : "answer"u8);
        return Convert.ToBase64String(packet);
    }

    private static string Unseal(string sdp,byte[] key,bool offer)
    {
        try {
            var packet=Convert.FromBase64String(sdp);
            if(packet.Length<=28) throw new IOException("Sinalização ICE incompleta.");
            var plain=new byte[packet.Length-28];
            using var aes=new AesGcm(key,16);
            aes.Decrypt(packet.AsSpan(0,12),packet.AsSpan(28),packet.AsSpan(12,16),plain,offer ? "offer"u8 : "answer"u8);
            return TextEncoding.UTF8.GetString(plain);
        }
        catch(Exception ex) when(ex is FormatException or CryptographicException) { throw new IOException("Sinalização ICE inválida.",ex); }
    }

    private void SetRemote(RTCSdpType type,string sdp)
    {
        if(_peer.setRemoteDescription(new RTCSessionDescriptionInit { type=type,sdp=sdp })!=SetDescriptionResultEnum.OK)
            throw new IOException("Não foi possível negociar uma rota ICE.");
    }

    private void Attach(RTCDataChannel channel)
    {
        if(channel.label!=Label || Interlocked.CompareExchange(ref _channel,channel,null) is not null) { channel.close(); return; }
        channel.onmessage+=(_,protocol,data)=>{
            if(protocol!=DataChannelPayloadProtocols.WebRTC_Binary || data.Length is <1 or >PacketSize || !_incoming.Writer.TryWrite(data))
                End(new IOException("O canal direto recebeu dados fora dos limites."));
        };
        channel.onclose+=()=>End(new IOException("O canal direto foi fechado."));
        channel.onopen+=()=>_opened.TrySetResult();
        if(channel.readyState==RTCDataChannelState.open) _opened.TrySetResult();
    }

    private void End(IOException error)
    {
        Interlocked.Exchange(ref _closed,1);
        _incoming.Writer.TryComplete(error);
        _opened.TrySetException(error);
    }

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

    public override async ValueTask<int> ReadAsync(Memory<byte> destination,CancellationToken cancellationToken=default)
    {
        if(destination.IsEmpty) return 0;
        if(_buffer.IsEmpty) {
            if(!await _incoming.Reader.WaitToReadAsync(cancellationToken)) return 0;
            _buffer=await _incoming.Reader.ReadAsync(cancellationToken);
        }
        int size=Math.Min(destination.Length,_buffer.Length);
        _buffer[..size].CopyTo(destination);
        _buffer=_buffer[size..];
        return size;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken cancellationToken=default)
    {
        var channel=_channel ?? throw new IOException("O canal direto não abriu.");
        while(!buffer.IsEmpty) {
            cancellationToken.ThrowIfCancellationRequested();
            if(Volatile.Read(ref _closed)!=0 || channel.readyState!=RTCDataChannelState.open)
                throw new IOException("O canal direto foi fechado.");
            if(channel.bufferedAmount>262144) { await Task.Delay(10,cancellationToken); continue; }
            int count=Math.Min(buffer.Length,PacketSize);
            channel.send(buffer[..count].ToArray());
            buffer=buffer[count..];
        }
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing) {
            Interlocked.Exchange(ref _closed,1);
            _incoming.Writer.TryComplete();
            _peer.Dispose();
        }
        base.Dispose(disposing);
    }
}
