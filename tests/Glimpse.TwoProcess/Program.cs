using System.Diagnostics;
using System.Net;
using System.IO;
using System.Reflection;
using System.Threading.Channels;
using Glimpse.Encoding;
using Glimpse.Networking;

// Real independent processes, same machine, synthetic frames only. Temporary files exchange SDP.
if (args.Length == 2 && args[0] == "host") { await HostAsync(args[1]); return; }
if (args.Length == 2 && args[0] == "guest") { await GuestAsync(args[1]); return; }
if (args.Length != 0) throw new ArgumentException("Expected no arguments.");
// Check window placement without opening or moving any window.
foreach (var area in new[] { new System.Windows.Rect(0,0,1920,1040), new System.Windows.Rect(0,0,1366,728), new System.Windows.Rect(-800,0,800,560) }) {
    var placed=Glimpse.Views.InitialWindowPlacement.FitInside(area);
    if (placed.Left < area.Left || placed.Top < area.Top || placed.Right > area.Right || placed.Bottom > area.Bottom) throw new InvalidOperationException("Window placement exceeds the usable work area.");
}
Console.WriteLine("Window placement fits large, small and offset work areas.");
bool captureSupported = Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported();
Console.WriteLine($"Windows.Graphics.Capture supported: {captureSupported}");
if (!captureSupported) throw new NotSupportedException("Windows Graphics Capture is unavailable.");
var runDir = Path.Combine(Path.GetTempPath(), "glimpse-two-process-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runDir);
try {
    using var host = StartChild("host", runDir);
    using var guest = StartChild("guest", runDir);
    var hostOut = host.StandardOutput.ReadToEndAsync(); var hostErr = host.StandardError.ReadToEndAsync();
    var guestOut = guest.StandardOutput.ReadToEndAsync(); var guestErr = guest.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
    try { await Task.WhenAll(host.WaitForExitAsync(timeout.Token), guest.WaitForExitAsync(timeout.Token)); }
    catch (OperationCanceledException) { try { host.Kill(true); } catch { } try { guest.Kill(true); } catch { } throw new TimeoutException("Two-process test timed out."); }
    Console.Write(await hostOut); Console.Write(await guestOut);
    var errors = (await hostErr) + (await guestErr); if (!string.IsNullOrWhiteSpace(errors)) Console.Error.Write(errors);
    if (host.ExitCode != 0 || guest.ExitCode != 0) throw new InvalidOperationException($"Host exit {host.ExitCode}; guest exit {guest.ExitCode}.");
    Console.WriteLine("Two independent processes exchanged synthetic H.264 video in both directions; no screen was captured.");
}
finally {
    foreach (var name in new[] { "offer", "answer", "done", "result", "guestdone", "hostresult", "guestdone.tmp", "hostresult.tmp", "offer.tmp", "answer.tmp", "done.tmp", "result.tmp" }) {
        var file = Path.Combine(runDir, name); if (File.Exists(file)) File.Delete(file);
    }
    Directory.Delete(runDir);
}

static Process StartChild(string mode, string dir) {
    string root = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? throw new InvalidOperationException("DOTNET_ROOT is required.");
    var info = new ProcessStartInfo(Path.Combine(root, "dotnet.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); info.ArgumentList.Add(mode); info.ArgumentList.Add(dir);
    return Process.Start(info) ?? throw new InvalidOperationException("Could not start the test process.");
}
static async Task PublishAsync(string dir, string name, string value) {
    var temporary=Path.Combine(dir,name+".tmp"); await File.WriteAllTextAsync(temporary,value); File.Move(temporary,Path.Combine(dir,name));
}
static async Task<string> WaitForAsync(string dir,string name) {
    var path=Path.Combine(dir,name); var deadline=DateTime.UtcNow.AddSeconds(30);
    while (!File.Exists(path) && DateTime.UtcNow<deadline) await Task.Delay(100);
    if (!File.Exists(path)) throw new TimeoutException("Timed out waiting for " + name);
    return await File.ReadAllTextAsync(path);
}
static async Task WaitConnectedAsync(LocalPeer peer) {
    var deadline=DateTime.UtcNow.AddSeconds(25);
    while (!peer.IsConnected && DateTime.UtcNow<deadline) await Task.Delay(100);
    if (!peer.IsConnected) throw new TimeoutException("WebRTC connection was not established.");
}
static async Task HostAsync(string dir) {
    using var peer=new LocalPeer(true);
    using var room=new DirectRoomHost(IPAddress.Loopback);
    using var recvCancel=new CancellationTokenSource(TimeSpan.FromSeconds(65)); using var decoder=new H264Decoder();
    int hostReceived=0, hostDecoded=0;
    decoder.Frame+=_=>Interlocked.Increment(ref hostDecoded);
    peer.VideoFrame+=frame=>{Interlocked.Increment(ref hostReceived);decoder.TryWrite(frame);};
    var decodeTask=Task.Run(()=>decoder.RunAsync(recvCancel.Token));
    room.Error += message => Console.Error.WriteLine(message);
    await PublishAsync(dir,"offer",room.Code); await room.WaitForGuestAsync(peer); await WaitConnectedAsync(peer);
    using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(50)); using var encoder=new H264Encoder();
    int sent=0; string? error=null;
    encoder.Error+=message=>error=message; encoder.AccessUnit+=frame=>{ Interlocked.Increment(ref sent); peer.Send(frame,30); };
    var channel=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2){FullMode=BoundedChannelFullMode.Wait});
    var task=Task.Run(()=>encoder.RunAsync(channel.Reader,320,180,30,cancel.Token));
    for(int n=0;n<120;n++) { var frame=new byte[320*180*4]; for(int i=0;i<frame.Length;i+=4){frame[i]=(byte)(n*2);frame[i+1]=(byte)(i/4%256);frame[i+2]=80;frame[i+3]=255;} await channel.Writer.WriteAsync(frame,cancel.Token); await Task.Delay(33,cancel.Token); }
    channel.Writer.Complete(); await task.WaitAsync(TimeSpan.FromSeconds(20));
    if (error is not null || sent==0) throw new InvalidOperationException(error ?? "No H.264 frames were sent.");
    await PublishAsync(dir,"done",sent.ToString());
    string result=await WaitForAsync(dir,"result"); Console.WriteLine($"Host: encoder={(encoder.UsingHardware?"hardware":"software")}, sent={sent}, guest={result}");
    if (!result.StartsWith("ok:",StringComparison.Ordinal)) throw new InvalidOperationException("Guest failed: "+result);
    await WaitForAsync(dir,"guestdone"); await Task.Delay(2000); recvCancel.Cancel(); try { await decodeTask; } catch(OperationCanceledException) { }
    if(hostReceived==0 || hostDecoded==0) { await PublishAsync(dir,"hostresult","erro"); throw new InvalidOperationException($"Host received={hostReceived}, decoded={hostDecoded}."); }
    await PublishAsync(dir,"hostresult","ok"); Console.WriteLine($"Host received={hostReceived}, decoded={hostDecoded}");
}
static async Task GuestAsync(string dir) {
    using var peer=new LocalPeer(false); using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(65)); using var decoder=new H264Decoder();
    int received=0, decoded=0; string? error=null;
    decoder.Frame+=_=>Interlocked.Increment(ref decoded); decoder.Error+=message=>error=message;
    peer.VideoFrame+=frame=>{ Interlocked.Increment(ref received); if (!decoder.TryWrite(frame)) error="Decoder queue full."; };
    var task=Task.Run(()=>decoder.RunAsync(cancel.Token));
    var code=await WaitForAsync(dir,"offer"); await DirectRoomGuest.JoinAsync(code,peer,cancel.Token); await WaitConnectedAsync(peer);
    await WaitForAsync(dir,"done"); await Task.Delay(2000); cancel.Cancel(); try { await task; } catch(OperationCanceledException) { }
    if (error is not null || received==0 || decoded==0) { await PublishAsync(dir,"result","error:"+(error??$"received={received}, decoded={decoded}")); throw new InvalidOperationException(error??"No decoded video."); }
    await PublishAsync(dir,"result",$"ok:received={received},decoded={decoded}");
    Console.WriteLine($"Guest: received={received}, decoded={decoded}");
    using var sendCancel=new CancellationTokenSource(TimeSpan.FromSeconds(40)); using var encoder=new H264Encoder();
    int sent=0; string? sendError=null; encoder.Error+=message=>sendError=message; encoder.AccessUnit+=frame=>{Interlocked.Increment(ref sent);peer.Send(frame,30);};
    var channel=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2){FullMode=BoundedChannelFullMode.Wait});
    var encodeTask=Task.Run(()=>encoder.RunAsync(channel.Reader,320,180,30,sendCancel.Token));
    for(int n=0;n<120;n++){var frame=new byte[320*180*4]; for(int i=0;i<frame.Length;i+=4){frame[i]=(byte)(n*2);frame[i+1]=(byte)(i/4%256);frame[i+2]=80;frame[i+3]=255;} await channel.Writer.WriteAsync(frame,sendCancel.Token); await Task.Delay(33,sendCancel.Token);}
    channel.Writer.Complete(); await encodeTask.WaitAsync(TimeSpan.FromSeconds(20));
    if(sendError is not null || sent==0) throw new InvalidOperationException(sendError??"Guest sent no frames.");
    await PublishAsync(dir,"guestdone",sent.ToString());
    var hostResult=await WaitForAsync(dir,"hostresult"); if(hostResult!="ok")throw new InvalidOperationException("Host failed to receive video.");
    Console.WriteLine($"Guest sent={sent}; host result={hostResult}");
}
