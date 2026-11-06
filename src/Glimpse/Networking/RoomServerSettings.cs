namespace Glimpse.Networking;

internal static class RoomServerSettings
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glimpse", "room-server.txt");
    public static string Load()
    {
        var configured = Environment.GetEnvironmentVariable("GLIMPSE_SIGNALING_URL");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        try { return File.ReadAllText(FilePath).Trim(); }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }
    public static void Save(Uri server)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, server.AbsoluteUri); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
