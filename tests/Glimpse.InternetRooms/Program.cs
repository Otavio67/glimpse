using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Glimpse.Networking;
using Glimpse.RoomProtocol;
using Glimpse.Signaling;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
await using var app = SignalingApplication.Build(["--urls", "http://127.0.0.1:0"], builder => {
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["GLIMPSE_ALLOW_NO_RELAY"] = "true" });
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
});
await app.StartAsync(deadline.Token);
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var server = InternetRoomSession.ServerAddress(address);
using var http = new HttpClient { BaseAddress = server };
foreach (var url in new[] { "", "http://example.com", "https://user:pass@example.com", "https://example.com?x=y" }) {
    try { InternetRoomSession.ServerAddress(url); throw new Exception("Unsafe URL accepted: " + url); }
    catch (InvalidOperationException) { }
}
Console.WriteLine("Client rejects non-HTTPS remote services and credential-bearing URLs.");

using var host = await InternetRoomSession.OpenAsync(server, null, deadline.Token);
var path = "v1/rooms/" + InternetRoomCode.Normalize(host.Ticket.Code);
using (var response = await http.GetAsync(path, deadline.Token)) Require(response.StatusCode == HttpStatusCode.Unauthorized, "Missing participant token was accepted.");
using (var request = new HttpRequestMessage(HttpMethod.Get, path)) {
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new string('F',64));
    using var response = await http.SendAsync(request, deadline.Token);
    Require(response.StatusCode == HttpStatusCode.Unauthorized, "Incorrect token was accepted.");
}
using var hostPeer = new LocalPeer(true, host.Ticket.IceServers);
var hostClosed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
host.Closed += message => hostClosed.TrySetResult(message);
var hostRun = host.RunAsync(hostPeer);
using var guest = await InternetRoomSession.OpenAsync(server, host.Ticket.Code.ToLowerInvariant(), deadline.Token);
using (var duplicate = await http.PostAsync(path + "/join", null, deadline.Token)) Require(duplicate.StatusCode == HttpStatusCode.Conflict, "Third participant accepted.");
using (var request = new HttpRequestMessage(HttpMethod.Put, path + "/answer")) {
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.Ticket.Token);
    request.Content = JsonContent.Create(new SignalMessage("dGVzdA=="));
    using var response = await http.SendAsync(request, deadline.Token);
    Require(response.StatusCode == HttpStatusCode.Conflict, "Host could publish a guest answer.");
}
using var guestPeer = new LocalPeer(false, guest.Ticket.IceServers);
var guestRun = guest.RunAsync(guestPeer);
while (!hostPeer.IsConnected || !guestPeer.IsConnected) {
    if (hostClosed.Task.IsCompleted) throw new Exception(await hostClosed.Task);
    await Task.Delay(100, deadline.Token);
}
Console.WriteLine("HTTP rooms negotiated actual WebRTC peers; authentication, role isolation, and two-person limit passed.");
guest.Dispose();
var closedMessage = await hostClosed.Task.WaitAsync(deadline.Token);
Require(closedMessage.Contains("encerrada"), "Remote departure did not close room.");
await Task.WhenAll(hostRun, guestRun).WaitAsync(deadline.Token);
Console.WriteLine("Leaving a room notifies the other participant and ends signaling.");

var clock = new TestClock();
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["GLIMPSE_TURN_URLS"]="turn:relay.example.com:3478,turns:relay.example.com:5349?transport=tcp",
    ["GLIMPSE_TURN_SECRET"]="test-only-secret"
}).Build();
var ice = new IceConfiguration(configuration);
var store = new RoomStore(clock, ice);
var ticket = store.Create();
Require(ticket.IceServers.Length == 2, "TURN URLs missing.");
var turn = ticket.IceServers[0];
var expected = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes("test-only-secret"), Encoding.UTF8.GetBytes(turn.Username!)));
Require(turn.Credential == expected && turn.Credential != "test-only-secret", "Temporary TURN credential generation failed.");
var joins = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => {
    try { store.Join(ticket.Code); return 1; } catch (RoomFailure failure) when (failure.Status == 409) { return 0; }
})));
Require(joins.Sum() == 1, "Concurrent joins reserved multiple guests.");
clock.Advance(TimeSpan.FromSeconds(91));
try { store.Read(ticket.Code, ticket.Token); throw new Exception("Abandoned room did not expire."); }
catch (RoomFailure failure) when (failure.Status == 404) { }
var waiting = store.Create();
for (int i=0; i<11; i++) {
    store.Read(waiting.Code,waiting.Token); clock.Advance(TimeSpan.FromMinutes(1));
}
try { store.Read(waiting.Code,waiting.Token); throw new Exception("Unused invitation did not expire."); }
catch (RoomFailure failure) when (failure.Status == 404) { }
Console.WriteLine("Atomic guest reservation, abandoned/unused room expiration, and temporary TURN credentials passed.");
await app.StopAsync(deadline.Token);
Console.WriteLine("No screens captured; public internet routing and a real TURN relay were not exercised.");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}
