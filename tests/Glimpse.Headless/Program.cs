using System.IO;
using System.Threading.Channels;
using Glimpse.Encoding;
using Glimpse.Networking;
var ffmpeg=Environment.GetEnvironmentVariable("GLIMPSE_FFMPEG")!;
Environment.SetEnvironmentVariable("PATH",Path.GetDirectoryName(ffmpeg)+";"+Environment.GetEnvironmentVariable("PATH"));
using var host=new LocalPeer(true); using var guest=new LocalPeer(false);
var offer=await host.CreateOfferAsync(); var answer=await guest.AcceptOfferAsync(offer); host.AcceptAnswer(answer);
var deadline=DateTime.UtcNow.AddSeconds(15); while((!host.IsConnected||!guest.IsConnected)&&DateTime.UtcNow<deadline) await Task.Delay(100);
if(!host.IsConnected||!guest.IsConnected)throw new Exception("Peers did not connect.");
using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(45));
using var decoder=new H264Decoder(); using var encoder=new H264Encoder(); int decoded=0,sent=0,received=0; string? failure=null;
decoder.Frame+=_=>Interlocked.Increment(ref decoded); decoder.Error+=e=>{Console.WriteLine(e); failure=e;};
encoder.Error+=e=>{Console.WriteLine(e); failure=e;}; encoder.AccessUnit+=data=>{Interlocked.Increment(ref sent);host.Send(data,30);};
guest.VideoFrame+=data=>{Interlocked.Increment(ref received);decoder.TryWrite(data);};
var channel=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2){FullMode=BoundedChannelFullMode.Wait});
var dec=Task.Run(()=>decoder.RunAsync(cts.Token)); var enc=Task.Run(()=>encoder.RunAsync(channel.Reader,320,180,30,cts.Token));
for(int n=0;n<150;n++){byte[] frame=new byte[320*180*4]; for(int i=0;i<frame.Length;i+=4){frame[i]=(byte)(n*2);frame[i+1]=(byte)(i/4%256);frame[i+2]=80;frame[i+3]=255;} await channel.Writer.WriteAsync(frame,cts.Token); await Task.Delay(33);}
channel.Writer.Complete(); await enc.WaitAsync(TimeSpan.FromSeconds(20)); await Task.Delay(2000); cts.Cancel(); try{await dec;}catch(OperationCanceledException){}
Console.WriteLine($"Hardware={encoder.UsingHardware}; sent={sent}; received={received}; decoded={decoded}; failure={failure??"none"}");
if(failure!=null||sent==0||received==0||decoded==0)throw new Exception("End-to-end synthetic pipeline failed.");
Console.WriteLine("Full synthetic encoder-to-WebRTC-to-decoder passed; no screen captured.");
