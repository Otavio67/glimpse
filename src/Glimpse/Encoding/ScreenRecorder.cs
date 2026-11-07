using System.Diagnostics;
using System.Threading.Channels;

namespace Glimpse.Encoding;

public static class ScreenRecorder
{
    public const int FramesPerSecond=30;

    public static string Folder
    {
        get {
            var videos=Environment.GetFolderPath(Environment.SpecialFolder.MyVideos,Environment.SpecialFolderOption.DoNotVerify);
            if(string.IsNullOrWhiteSpace(videos)) videos=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Videos");
            return Path.Combine(videos,"Glimpse");
        }
    }

    public static async Task<string?> SaveAsync(ChannelReader<byte[]> frames,int width,int height,string folder,CancellationToken token)
    {
        if(!await frames.WaitToReadAsync(token) || !frames.TryRead(out var latest)) return null;
        Directory.CreateDirectory(folder);
        var stamp=DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var id=Guid.NewGuid().ToString("N")[..8];
        var path=Path.Combine(folder,$"Glimpse_{stamp}_{id}.mp4");
        var partial=path+".partial";
        var info=H264Encoder.CreateStartInfo();
        info.RedirectStandardOutput=false;
        int maxWidth=Math.Clamp(width,2,1920),maxHeight=Math.Clamp(height,2,1080);
        var filter=$"scale={maxWidth}:{maxHeight}:force_original_aspect_ratio=decrease:force_divisible_by=2,format=yuv420p";
        foreach(var argument in new[]{"-hide_banner","-loglevel","error","-nostdin","-f","rawvideo","-pixel_format","bgra",
            "-video_size",$"{width}x{height}","-framerate",FramesPerSecond.ToString(),"-i","pipe:0","-an","-vf",filter,
            "-c:v","libx264","-preset","veryfast","-crf","21","-movflags","+faststart","-f","mp4","-n",partial})
            info.ArgumentList.Add(argument);

        using var process=Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar a gravação.");
        var errors=process.StandardError.ReadToEndAsync();
        try {
            var clock=Stopwatch.StartNew();
            long count=0;
            do {
                while(frames.TryRead(out var frame)) latest=frame;
                await process.StandardInput.BaseStream.WriteAsync(latest,token);
                // Windows can stop delivering frames on a still screen. Repeat the latest frame to preserve time.
                var delay=TimeSpan.FromSeconds(++count/(double)FramesPerSecond)-clock.Elapsed;
                if(delay>TimeSpan.Zero && !frames.Completion.IsCompleted) await Task.Delay(delay,token);
            } while(!frames.Completion.IsCompleted);

            process.StandardInput.Close();
            using var flush=CancellationTokenSource.CreateLinkedTokenSource(token);
            flush.CancelAfter(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(flush.Token);
            var message=await errors;
            if(process.ExitCode!=0 || !File.Exists(partial) || new FileInfo(partial).Length==0)
                throw new IOException(string.IsNullOrWhiteSpace(message) ? "Não foi possível salvar o vídeo." : message.Trim());
            File.Move(partial,path);
            return path;
        }
        finally {
            if(!process.HasExited) {
                try { process.Kill(true); }
                catch(InvalidOperationException) { }
                await process.WaitForExitAsync();
            }
            try { File.Delete(partial); }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
    }
}
