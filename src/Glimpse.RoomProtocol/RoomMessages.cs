namespace Glimpse.RoomProtocol;

public sealed record IceServer(string Url, string? Username = null, string? Credential = null);
public sealed record RoomTicket(string Code, string Token, IceServer[] IceServers, DateTimeOffset ExpiresAt);
public sealed record RoomSnapshot(bool GuestJoined, string? Offer, string? Answer, DateTimeOffset ExpiresAt);
public sealed record SignalMessage(string Sdp);
public sealed record RoomError(string Message);

public static class InternetRoomCode
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public static string Normalize(string value)
    {
        value = value.Trim().ToUpperInvariant();
        if (value.StartsWith("GLI-")) value = value[4..];
        value = value.Replace("-", "");
        if (value.Length != 12 || value.Any(c => !Alphabet.Contains(c)))
            throw new InvalidDataException("Informe um código de sala no formato GLI-XXXX-XXXX-XXXX.");
        return value;
    }
    public static string Display(string value) => $"GLI-{value[..4]}-{value[4..8]}-{value[8..]}";
}
