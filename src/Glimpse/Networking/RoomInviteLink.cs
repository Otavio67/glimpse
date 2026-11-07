namespace Glimpse.Networking;

public static class RoomInviteLink
{
    private const string WebPrefix="https://otavio67.github.io/glimpse/join/#";
    private const string AppPrefix="glimpse://join/";

    public static string ToWebLink(string code) => WebPrefix+code;

    public static string Unwrap(string text)
    {
        if(text.StartsWith(WebPrefix,StringComparison.OrdinalIgnoreCase)) return ReadCode(text[WebPrefix.Length..]);
        if(text.StartsWith(AppPrefix,StringComparison.OrdinalIgnoreCase)) return ReadCode(text[AppPrefix.Length..]);
        return text;
    }

    public static string? FromActivation(string argument)
    {
        if(!argument.StartsWith(AppPrefix,StringComparison.OrdinalIgnoreCase)) return null;
        try { return ReadCode(argument[AppPrefix.Length..]); }
        catch(InvalidDataException) { return null; }
    }

    private static string ReadCode(string code)
    {
        if(code.Length is not (98 or 119) || !code.StartsWith("GLN-",StringComparison.OrdinalIgnoreCase) ||
            !code.All(c=>char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new InvalidDataException("O link da sala está incompleto. Peça ao anfitrião para copiá-lo novamente.");
        NativeRoomInvite.Parse(code);
        return code;
    }
}
