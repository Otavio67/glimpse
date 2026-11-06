using System.IO;
using System.Net;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Glimpse.Encoding;
using Glimpse.Networking;

using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));
foreach(var quality in VideoQuality.Presets) {
    using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); lifetime.CancelAfter(TimeSpan.FromSeconds(45));
    using var host=new NativeRoomHost(0); using var hub=new NativeRoomHub(host);
    var hosting=hub.RunAsync(lifetime.Token);
    using var first=await NativeRoomConnection.JoinAsync(host.Invite(IPAddress.Loopback),lifetime.Token);
    using var second=await NativeRoomConnection.JoinAsync(host.Invite(IPAddress.Loopback),lifetime.Token);
    using var decoder1=new H264Decoder(); using var decoder2=new H264Decoder();
    var result1=new TaskCompletionSource<DecodedVideoFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
    var result2=new TaskCompletionSource<DecodedVideoFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
    var errors=new ConcurrentQueue<string>();
    decoder1.FrameDecoded+=frame=>result1.TrySetResult(frame); decoder2.FrameDecoded+=frame=>result2.TrySetResult(frame);
    decoder1.Error+=message=>{if(!lifetime.IsCancellationRequested) errors.Enqueue(message);}; decoder2.Error+=message=>{if(!lifetime.IsCancellationRequested) errors.Enqueue(message);};
    first.VideoFrame+=frame=>decoder1.TryWrite(frame); second.VideoFrame+=frame=>decoder2.TryWrite(frame);
    var firstRun=first.RunAsync(); var secondRun=second.RunAsync();
    var decode1=Task.Run(()=>decoder1.RunAsync(lifetime.Token)); var decode2=Task.Run(()=>decoder2.RunAsync(lifetime.Token));
    try {
        while(hub.ParticipantCount!=3) await Task.Delay(10,lifetime.Token);
        hub.BeginHostSharing();
        using var encoder=new H264Encoder();
        encoder.Error+=message=>errors.Enqueue(message);
        encoder.AccessUnit+=frame=>{if(!hub.TrySendVideo(frame)) errors.Enqueue("Transport rejected an encoded frame.");};
        var channel=Channel.CreateBounded<byte[]>(2);
        var encode=Task.Run(()=>encoder.RunAsync(channel.Reader,320,180,30,lifetime.Token,quality));
        for(int n=0;n<100;n++) {
            var pixels=new byte[320*180*4];
            for(int y=0;y<180;y++) for(int x=0;x<320;x++) { int p=(y*320+x)*4; pixels[p]=(byte)(n%80); pixels[p+1]=(byte)(y<90?210:35); pixels[p+2]=(byte)(y<90?35:210); pixels[p+3]=255; }
            await channel.Writer.WriteAsync(pixels,lifetime.Token); await Task.Delay(33,lifetime.Token);
        }
        channel.Writer.Complete(); await encode.WaitAsync(lifetime.Token);
        if(!errors.IsEmpty) throw new Exception(string.Join("; ",errors));
        var frames=await Task.WhenAll(result1.Task,result2.Task).WaitAsync(lifetime.Token);
        foreach(var frame in frames) {
            if(frame.Width!=quality.Width || frame.Height!=quality.Height || frame.Pixels.Length!=quality.Width*quality.Height*4) throw new Exception("Receiver resolution does not match selected quality.");
            int top=((frame.Height/4)*frame.Width+frame.Width/2)*4, bottom=((frame.Height*3/4)*frame.Width+frame.Width/2)*4;
            if(frame.Pixels[top+1]<=frame.Pixels[top+2]+50 || frame.Pixels[bottom+2]<=frame.Pixels[bottom+1]+50 || frame.Pixels[top+3]!=255) throw new Exception("Decoded frame color order, orientation or alpha is incorrect.");
        }
        Console.WriteLine($"{quality.Label}: {quality.Width}x{quality.Height}, encoder={(encoder.UsingHardware?"hardware":"software")}, decoded at both receivers with correct dimensions and orientation.");
    }
    finally {
        lifetime.Cancel(); first.Dispose(); second.Dispose(); hub.Dispose(); decoder1.Dispose(); decoder2.Dispose();
        await Task.WhenAll(firstRun,secondRun,decode1,decode2).WaitAsync(TimeSpan.FromSeconds(5));
        try { await hosting; } catch(Exception) when(lifetime.IsCancellationRequested) { }
    }
}
Console.WriteLine("All five qualities passed native TLS broadcast with synthetic frames only. No screen or router was accessed.");
