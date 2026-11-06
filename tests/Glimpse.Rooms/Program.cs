using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Buffers.Binary;
using Glimpse.Networking;

// Network-only regression checks: no FFmpeg, capture, or windows.
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(70));
using var hostPeer = new LocalPeer(true);
using var room = new DirectRoomHost(IPAddress.Loopback);
var errors = new List<string>();
room.Error += message => errors.Add(message);
var hostTask = room.WaitForGuestAsync(hostPeer);

foreach (var invalid in new[] { "", "GLM-?", "GLM-AA", new string('x', 300) }) {
    using var peer = new LocalPeer(false);
    try { await DirectRoomGuest.JoinAsync(invalid, peer, deadline.Token); throw new Exception("Invalid code accepted."); }
    catch (InvalidDataException) { }
}
Console.WriteLine("Malformed codes rejected.");

// A certificate mismatch must fail before sending the room secret.
await RejectAsync(ChangeByte(room.Code, 6));
// A wrong secret must not consume the host's peer or close its listener.
await RejectAsync(ChangeByte(room.Code, 38));
if (hostTask.IsCompleted) throw new Exception("An unauthenticated client consumed the room.");
Console.WriteLine("Wrong certificate and secret rejected; room remains open.");

var data = Decode(room.Code);
using (var stalled = new TcpClient()) {
    await stalled.ConnectAsync(IPAddress.Loopback, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2)), deadline.Token);
    using var guestPeer = new LocalPeer(false);
    // The server must evict the stalled TLS client and then serve this valid guest.
    await DirectRoomGuest.JoinAsync("  " + room.Code + "\r\n", guestPeer, deadline.Token);
    await hostTask.WaitAsync(deadline.Token);
    while (!hostPeer.IsConnected || !guestPeer.IsConnected) await Task.Delay(100, deadline.Token);
    if (errors.Count != 0) throw new Exception(string.Join("; ", errors));
    Console.WriteLine("Stalled client timed out; valid guest established WebRTC with the same room code.");
}

using (var peer = new LocalPeer(true)) {
    var cancelledRoom = new DirectRoomHost(IPAddress.Loopback);
    var waiting = cancelledRoom.WaitForGuestAsync(peer);
    cancelledRoom.Dispose(); cancelledRoom.Dispose();
    await waiting.WaitAsync(TimeSpan.FromSeconds(3));
}
using (var peer = new LocalPeer(false)) {
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await DirectRoomGuest.JoinAsync(room.Code, peer, cancelled.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { }
}
Console.WriteLine("Host shutdown and guest cancellation completed cleanly.");

async Task RejectAsync(string code) {
    using var peer = new LocalPeer(false);
    try { await DirectRoomGuest.JoinAsync(code, peer, deadline.Token); }
    catch (IOException) { return; }
    throw new Exception("Invalid credentials accepted.");
}
static byte[] Decode(string code) {
    var value = code[4..].Replace('-', '+').Replace('_', '/');
    return Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
}
static string ChangeByte(string code, int offset) {
    var bytes = Decode(code); bytes[offset] ^= 1;
    return "GLM-" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
