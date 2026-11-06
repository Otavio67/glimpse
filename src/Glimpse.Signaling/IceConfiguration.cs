using System.Security.Cryptography;
using System.Text;
using Glimpse.RoomProtocol;

namespace Glimpse.Signaling;

public sealed class IceConfiguration
{
    private readonly string[] _stun;
    private readonly string[] _turn;
    private readonly string? _secret;
    public bool RelayConfigured => _turn.Length > 0;

    public IceConfiguration(IConfiguration config)
    {
        _stun = Split(config["GLIMPSE_STUN_URLS"]);
        _turn = Split(config["GLIMPSE_TURN_URLS"]);
        _secret = config["GLIMPSE_TURN_SECRET"];
        if (_stun.Any(u => !u.StartsWith("stun:", StringComparison.Ordinal) && !u.StartsWith("stuns:", StringComparison.Ordinal)) ||
            _turn.Any(u => !u.StartsWith("turn:", StringComparison.Ordinal) && !u.StartsWith("turns:", StringComparison.Ordinal)))
            throw new InvalidOperationException("Use STUN URLs in GLIMPSE_STUN_URLS and TURN URLs in GLIMPSE_TURN_URLS.");
        if (_turn.Length > 0 && string.IsNullOrWhiteSpace(_secret)) throw new InvalidOperationException("GLIMPSE_TURN_SECRET is required with TURN URLs.");
        if (!config.GetValue<bool>("GLIMPSE_ALLOW_NO_RELAY") && !RelayConfigured)
            throw new InvalidOperationException("Configure TURN for internet rooms, or explicitly set GLIMPSE_ALLOW_NO_RELAY=true for local development only.");
    }

    public IceServer[] Issue(DateTimeOffset now)
    {
        var servers = _stun.Select(url => new IceServer(url)).ToList();
        if (_turn.Length > 0) {
            // Coturn REST credentials; shared secret stays on the server.
            var username = $"{now.AddHours(3).ToUnixTimeSeconds()}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
            var password = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(_secret!), Encoding.UTF8.GetBytes(username)));
            servers.AddRange(_turn.Select(url => new IceServer(url, username, password)));
        }
        return servers.ToArray();
    }
    private static string[] Split(string? value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
