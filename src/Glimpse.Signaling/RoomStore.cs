using System.Security.Cryptography;
using System.Text;
using Glimpse.RoomProtocol;

namespace Glimpse.Signaling;

public sealed class RoomFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

// One process owns the rooms. All transitions (especially guest reservation) are atomic.
public sealed class RoomStore(TimeProvider clock, IceConfiguration ice)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Room> _rooms = new();

    public RoomTicket Create()
    {
        lock (_gate) {
            Prune();
            if (_rooms.Count >= 1000) throw new RoomFailure(503, "Serviço de salas ocupado. Tente novamente em instantes.");
            string code;
            do { code = new string(Enumerable.Range(0, 12).Select(_ => InternetRoomCode.Alphabet[RandomNumberGenerator.GetInt32(32)]).ToArray()); } while (_rooms.ContainsKey(code));
            var token = NewToken();
            var now = clock.GetUtcNow();
            var room = new Room { HostHash = Hash(token), CreatedAt = now, HostSeen = now, ExpiresAt = now.AddHours(2) };
            _rooms.Add(code, room);
            return new(InternetRoomCode.Display(code), token, ice.Issue(now), room.ExpiresAt);
        }
    }

    public RoomTicket Join(string code)
    {
        lock (_gate) {
            var room = Find(code);
            if (room.GuestHash is not null) throw new RoomFailure(409, "Esta sala já tem um convidado. Peça ao anfitrião um novo código.");
            var token = NewToken();
            room.GuestHash = Hash(token); room.GuestSeen = clock.GetUtcNow();
            return new(InternetRoomCode.Display(InternetRoomCode.Normalize(code)), token, ice.Issue(clock.GetUtcNow()), room.ExpiresAt);
        }
    }

    public RoomSnapshot Read(string code, string token)
    {
        lock (_gate) {
            var room = Find(code); bool host = Authorize(room, token);
            if (host) room.HostSeen = clock.GetUtcNow(); else room.GuestSeen = clock.GetUtcNow();
            return new(room.GuestHash is not null, host ? null : room.Offer, host ? room.Answer : null, room.ExpiresAt);
        }
    }

    public void Publish(string code, string token, string kind, string sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp) || sdp.Length > 100000) throw new RoomFailure(400, "Dados de conexão inválidos.");
        try { if (Convert.FromBase64String(sdp).Length == 0) throw new FormatException(); }
        catch (FormatException) { throw new RoomFailure(400, "Dados de conexão inválidos."); }
        lock (_gate) {
            var room = Find(code); bool host = Authorize(room, token);
            if (kind == "offer" && host && room.GuestHash is not null) {
                if (room.Offer is not null && room.Offer != sdp) throw new RoomFailure(409, "A oferta desta sala já foi enviada.");
                room.Offer = sdp; room.HostSeen = clock.GetUtcNow();
            }
            else if (kind == "answer" && !host && room.Offer is not null) {
                if (room.Answer is not null && room.Answer != sdp) throw new RoomFailure(409, "A resposta desta sala já foi enviada.");
                room.Answer = sdp; room.GuestSeen = clock.GetUtcNow();
            }
            else throw new RoomFailure(409, "Esta etapa de conexão não está disponível.");
        }
    }

    public void Close(string code, string token)
    {
        lock (_gate) { var room = Find(code); Authorize(room, token); _rooms.Remove(InternetRoomCode.Normalize(code)); }
    }

    public void Prune()
    {
        lock (_gate) {
            foreach (var code in _rooms.Where(p => Expired(p.Value)).Select(p => p.Key).ToArray()) _rooms.Remove(code);
        }
    }

    private Room Find(string code)
    {
        try { code = InternetRoomCode.Normalize(code); }
        catch (InvalidDataException ex) { throw new RoomFailure(400, ex.Message); }
        if (!_rooms.TryGetValue(code, out var room)) throw Missing();
        if (Expired(room)) { _rooms.Remove(code); throw Missing(); }
        return room;
    }
    private bool Expired(Room room)
    {
        var now = clock.GetUtcNow();
        return now >= room.ExpiresAt || now - room.HostSeen > TimeSpan.FromSeconds(90)
            || (room.GuestSeen is { } seen && now - seen > TimeSpan.FromSeconds(90))
            || (room.GuestHash is null && now - room.CreatedAt > TimeSpan.FromMinutes(10));
    }
    private static RoomFailure Missing() => new(404, "Sala encerrada ou expirada. Peça um novo código ao anfitrião.");
    private static bool Authorize(Room room, string token)
    {
        if (token.Length is < 32 or > 64) throw new RoomFailure(401, "Acesso à sala inválido.");
        var hash = Hash(token);
        if (CryptographicOperations.FixedTimeEquals(hash, room.HostHash)) return true;
        if (room.GuestHash is not null && CryptographicOperations.FixedTimeEquals(hash, room.GuestHash)) return false;
        throw new RoomFailure(401, "Acesso à sala inválido.");
    }
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private sealed class Room
    {
        public required byte[] HostHash { get; init; }
        public byte[]? GuestHash { get; set; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset HostSeen { get; set; }
        public DateTimeOffset? GuestSeen { get; set; }
        public string? Offer { get; set; }
        public string? Answer { get; set; }
    }
}
