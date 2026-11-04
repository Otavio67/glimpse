using System.Text;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
namespace Glimpse.Networking;
public sealed class LocalPeer : IDisposable
{
    private readonly RTCPeerConnection _pc;
    public event Action<byte[]>? VideoFrame;
    public event Action<string>? StateChanged;
    public bool IsConnected => _pc.connectionState == RTCPeerConnectionState.connected;
    public LocalPeer(bool sender)
    {
        _pc = new RTCPeerConnection(new RTCConfiguration());
        var format = new VideoFormat(VideoCodecsEnum.H264, 102, 90000, "packetization-mode=1;profile-level-id=42e01f;level-asymmetry-allowed=1");
        _pc.addTrack(new MediaStreamTrack(new List<VideoFormat> { format }, sender ? MediaStreamStatusEnum.SendOnly : MediaStreamStatusEnum.RecvOnly));
        _pc.OnVideoFrameReceived += (_, _, frame, _) => VideoFrame?.Invoke(frame);
        _pc.onconnectionstatechange += state => StateChanged?.Invoke(state.ToString());
    }
    public async Task<string> CreateOfferAsync() {
        var offer = _pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
        await _pc.setLocalDescription(offer);
        return Pack(_pc.localDescription.sdp.ToString());
    }
    public async Task<string> AcceptOfferAsync(string offer) {
        SetRemote(RTCSdpType.offer, offer);
        var answer = _pc.createAnswer(new RTCAnswerOptions { X_WaitForIceGatheringToComplete = true });
        await _pc.setLocalDescription(answer);
        return Pack(_pc.localDescription.sdp.ToString());
    }
    public void AcceptAnswer(string answer) => SetRemote(RTCSdpType.answer, answer);
    private void SetRemote(RTCSdpType type, string encoded) {
        if (encoded.Length > 100000) throw new InvalidDataException("The connection text is too large.");
        var sdp = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Trim()));
        var result = _pc.setRemoteDescription(new RTCSessionDescriptionInit { type = type, sdp = sdp });
        if (result != SetDescriptionResultEnum.OK) throw new InvalidOperationException("WebRTC negotiation failed: " + result);
    }
    private static string Pack(string sdp) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sdp));
    public void Send(byte[] accessUnit, int fps) { if (IsConnected) _pc.SendVideo((uint)(90000 / fps), accessUnit); }
    public void Dispose() { _pc.close(); _pc.Dispose(); }
}

