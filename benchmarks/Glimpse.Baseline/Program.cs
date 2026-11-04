using System.Diagnostics;
using System.Threading.Channels;
using Glimpse.Encoding;

int durationSeconds = args.Length > 0 ? int.Parse(args[0]) : 120;
int width = 1280, height = 720, fps = 30;
using var encoder = new H264Encoder();
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds + 30));
var frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
var image = new byte[width * height * 4];
for (int i = 0; i < image.Length; i += 4) { var pixel=i/4; image[i]=(byte)(pixel%251); image[i+1]=(byte)((pixel/width)%256); image[i+2]=(byte)((pixel/17)%256); image[i+3]=255; }
int accepted = 0, dropped = 0, emitted = 0; string? failure = null;
encoder.AccessUnit += _ => Interlocked.Increment(ref emitted);
encoder.Error += message => failure = message;
var task = Task.Run(() => encoder.RunAsync(frames.Reader, width, height, fps, stop.Token));
Console.WriteLine($"benchmark_pid={Environment.ProcessId}; mode=synthetic_bgra; width={width}; height={height}; fps={fps}; duration_seconds={durationSeconds}");
var clock = Stopwatch.StartNew(); int index=0;
while (clock.Elapsed < TimeSpan.FromSeconds(durationSeconds)) {
    if (frames.Writer.TryWrite(image)) accepted++; else dropped++;
    index++; var due = TimeSpan.FromSeconds((double)index / fps); var delay = due - clock.Elapsed;
    if (delay > TimeSpan.Zero) await Task.Delay(delay, stop.Token);
}
frames.Writer.Complete(); await task.WaitAsync(TimeSpan.FromSeconds(25));
Console.WriteLine($"elapsed_seconds={clock.Elapsed.TotalSeconds:F2}; accepted={accepted}; dropped={dropped}; encoded={emitted}; hardware={encoder.UsingHardware}; error={failure ?? "none"}");
if (failure is not null || emitted == 0) throw new InvalidOperationException(failure ?? "No encoded frames.");
