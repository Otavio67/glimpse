namespace Glimpse.Encoding;

public sealed record VideoQuality(string Id,string Label,int Width,int Height,int Bitrate)
{
    public static readonly VideoQuality[] Presets=[
        new("360p","360p",640,360,900_000),
        new("480p","480p",854,480,1_500_000),
        new("720p","720p",1280,720,2_500_000),
        new("1080p","1080p",1920,1080,6_000_000),
        new("1440p","1440p (2K)",2560,1440,12_000_000)
    ];
    public string DisplayText => $"{Label} — {Width} × {Height}";
    public string UploadHint => $"Aproximadamente {Bitrate/1_000_000d:0.#} Mbps por destino, a 30 fps. Resoluções maiores exigem mais do PC e da conexão.";
    public static VideoQuality Default => Presets[2];
    public static VideoQuality FromId(string? id) => Presets.FirstOrDefault(p=>p.Id==id)??Default;
    private static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Glimpse","video-quality.txt");
    public static VideoQuality Load() { try { return FromId(File.ReadAllText(SettingsFile).Trim()); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { return Default; } }
    public void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile,Id); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { } }
}

public sealed record ShareRequest(string Source,VideoQuality Quality);
public sealed record DecodedVideoFrame(int Width,int Height,byte[] Pixels);
