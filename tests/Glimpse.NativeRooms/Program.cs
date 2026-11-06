using System.IO;
using System.Net;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Glimpse.Encoding;
using Glimpse.Networking;

using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(args.Contains("--video")?120:50));
using var host=new NativeRoomHost(0);
using var hub=new NativeRoomHub(host);
var hosting=hub.RunAsync(deadline.Token);
_=hosting.ContinueWith(task=>{ if(task.IsFaulted) Console.Error.WriteLine(task.Exception); },TaskScheduler.Default);
var code=host.Invite(IPAddress.Loopback);
var invite=NativeRoomInvite.Parse(code);
Require(invite.Port==host.Port && invite.Address.Equals(IPAddress.Loopback),"Invitation round trip failed.");
foreach(var address in new[]{"10.0.0.1","100.64.1.1","100.127.255.254","172.16.0.1","192.168.1.1","169.254.1.2","127.0.0.1","0.0.0.0","224.0.0.1","255.255.255.255","203.0.113.1"})
    Require(!RouterPortMapping.IsPublicIpv4(IPAddress.Parse(address)),"Non-public address accepted: "+address);
Require(RouterPortMapping.IsPublicIpv4(IPAddress.Parse("8.8.8.8")),"Public IPv4 rejected.");
foreach(var invalid in new[]{"", "GLN-?", "GLN-AA",new string('x',200)}) {
    try { NativeRoomInvite.Parse(invalid); throw new Exception("Malformed invite accepted."); } catch(InvalidDataException) { }
}
var wrongSecret=invite.Secret.ToArray(); wrongSecret[0]^=1;
await RejectAsync((invite with {Secret=wrongSecret}).Encode());
var wrongPin=invite.Fingerprint.ToArray(); wrongPin[0]^=1;
await RejectAsync((invite with {Fingerprint=wrongPin}).Encode());
Console.WriteLine("Invitation validation, public/private IPv4 classification, wrong secret and wrong certificate passed.");

var connections=new List<NativeRoomConnection>(); var runs=new List<Task>();
try {
    var first=await NativeRoomConnection.JoinAsync(code,deadline.Token); connections.Add(first);
    var firstFrames=new ConcurrentQueue<byte[]>(); first.VideoFrame+=frame=>firstFrames.Enqueue(frame);
    var firstRevoked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); first.PresentationRevoked+=()=>firstRevoked.TrySetResult();
    runs.Add(first.RunAsync());
    var second=await NativeRoomConnection.JoinAsync(code,deadline.Token); connections.Add(second);
    var secondFrames=new ConcurrentQueue<byte[]>(); second.VideoFrame+=frame=>secondFrames.Enqueue(frame);
    runs.Add(second.RunAsync());
    await UntilAsync(()=>hub.ParticipantCount==3);
    hub.BeginHostSharing();
    var hostPayload=new byte[]{0,0,0,1,0x65,1,2,3}; hub.TrySendVideo(hostPayload);
    await UntilAsync(()=>firstFrames.Count==1 && secondFrames.Count==1);
    Require(firstFrames.TryDequeue(out var f1) && f1.SequenceEqual(hostPayload),"First viewer received corrupt data.");
    Require(secondFrames.TryDequeue(out var f2) && f2.SequenceEqual(hostPayload),"Second viewer received corrupt data.");
    Console.WriteLine("Two simultaneous guests received the host's framed video payload over pinned TLS.");

    var atHost=new ConcurrentQueue<byte[]>(); hub.VideoFrame+=frame=>atHost.Enqueue(frame);
    first.NotifySharingStarted();
    var guestPayload=new byte[]{0,0,0,1,0x65,4,5,6}; first.TrySendVideo(guestPayload);
    await UntilAsync(()=>atHost.Count==1 && secondFrames.Count==1);
    Require(atHost.TryDequeue(out var h1) && h1.SequenceEqual(guestPayload),"Guest presentation did not reach host.");
    Require(secondFrames.TryDequeue(out var g1) && g1.SequenceEqual(guestPayload),"Guest presentation did not reach other guest.");
    Require(!hub.TrySendVideo(hostPayload),"Old host presenter could still publish.");
    second.NotifySharingStarted(); await firstRevoked.Task.WaitAsync(deadline.Token);
    first.TrySendVideo(hostPayload); second.TrySendVideo(guestPayload);
    await UntilAsync(()=>atHost.Count==1 && firstFrames.Count==1);
    Require(atHost.TryDequeue(out var h2) && h2.SequenceEqual(guestPayload),"Revoked participant could inject video.");
    Console.WriteLine("Guest-to-room broadcasting and presenter handover passed; revoked presenters cannot publish.");

    if(args.Contains("--video")) {
        hub.BeginHostSharing();
        await VerifyVideoAsync(first,second,null,frame=>hub.TrySendVideo(frame));
        first.NotifySharingStarted();
        // Wait for the host to process the presentation request before generating frames.
        await UntilAsync(()=>!hub.TrySendVideo(new byte[]{0,0,0,1,0x65}));
        await VerifyVideoAsync(null,second,hub,frame=>first.TrySendVideo(frame));
        Console.WriteLine("Synthetic H.264 encoded and decoded by two receivers for both host and guest presentations.");
    }

    for(int i=0;i<5;i++) { var guest=await NativeRoomConnection.JoinAsync(code,deadline.Token); connections.Add(guest); runs.Add(guest.RunAsync()); }
    await UntilAsync(()=>hub.ParticipantCount==8);
    try { using var extra=await NativeRoomConnection.JoinAsync(code,deadline.Token); throw new Exception("Ninth participant was accepted."); }
    catch(IOException ex) when(ex.Message.Contains("cheia")) { }
    second.Dispose(); await UntilAsync(()=>hub.ParticipantCount==7);
    var replacement=await NativeRoomConnection.JoinAsync(code,deadline.Token); connections.Add(replacement); runs.Add(replacement.RunAsync());
    await UntilAsync(()=>hub.ParticipantCount==8);
    Console.WriteLine("Eight-person limit, guest departure, presenter departure and replacement guest passed.");
}
finally {
    hub.Dispose(); deadline.Cancel(); foreach(var connection in connections) connection.Dispose();
    try { await hosting; } catch(Exception) when(deadline.IsCancellationRequested) { }
    await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(5));
}
Console.WriteLine("Clean shutdown passed. No router mapping, firewall change or screen capture was performed.");
async Task RejectAsync(string badCode) {
    try { using var bad=await NativeRoomConnection.JoinAsync(badCode,deadline.Token); throw new Exception("Invalid credentials accepted."); }
    catch(System.Security.Authentication.AuthenticationException) { }
    catch(IOException) { }
}
async Task UntilAsync(Func<bool> condition) { while(!condition()) await Task.Delay(20,deadline.Token); }
static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }

async Task VerifyVideoAsync(NativeRoomConnection? first,NativeRoomConnection second,NativeRoomHub? hostReceiver,Func<byte[],bool> send) {
    using var cancel=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
    using var decoder1=new H264Decoder(); using var decoder2=new H264Decoder();
    int decoded1=0,decoded2=0; string? error=null;
    decoder1.Frame+=_=>Interlocked.Increment(ref decoded1); decoder2.Frame+=_=>Interlocked.Increment(ref decoded2);
    decoder1.Error+=message=>{if(!cancel.IsCancellationRequested) error=message;}; decoder2.Error+=message=>{if(!cancel.IsCancellationRequested) error=message;};
    Action<byte[]> receive1=frame=>decoder1.TryWrite(frame); Action<byte[]> receive2=frame=>decoder2.TryWrite(frame);
    if(first is not null) first.VideoFrame+=receive1; else hostReceiver!.VideoFrame+=receive1;
    second.VideoFrame+=receive2;
    var decode1=Task.Run(()=>decoder1.RunAsync(cancel.Token)); var decode2=Task.Run(()=>decoder2.RunAsync(cancel.Token));
    try {
        using var encoder=new H264Encoder(); encoder.Error+=message=>error=message;
        encoder.AccessUnit+=frame=>{if(!send(frame)) error="Native transport rejected encoded video.";};
        var frames=Channel.CreateBounded<byte[]>(2); var encode=Task.Run(()=>encoder.RunAsync(frames.Reader,320,180,30,cancel.Token));
        for(int n=0;n<100;n++) { var pixels=new byte[320*180*4]; for(int i=0;i<pixels.Length;i+=4) { pixels[i]=(byte)(n*2); pixels[i+1]=(byte)(i/4%256); pixels[i+2]=80; pixels[i+3]=255; } await frames.Writer.WriteAsync(pixels,cancel.Token); await Task.Delay(33,cancel.Token); }
        frames.Writer.Complete(); await encode.WaitAsync(TimeSpan.FromSeconds(20),deadline.Token);
        await UntilAsync(()=>error is not null || (Volatile.Read(ref decoded1)>0 && Volatile.Read(ref decoded2)>0));
        if(error is not null) throw new Exception(error);
        Console.WriteLine($"Native video: receiver1 decoded={decoded1}, receiver2 decoded={decoded2}.");
    }
    finally {
        if(first is not null) first.VideoFrame-=receive1; else hostReceiver!.VideoFrame-=receive1;
        second.VideoFrame-=receive2; cancel.Cancel(); decoder1.Dispose(); decoder2.Dispose();
        await Task.WhenAll(decode1,decode2).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
