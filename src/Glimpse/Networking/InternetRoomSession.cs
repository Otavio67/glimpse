using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Glimpse.RoomProtocol;

namespace Glimpse.Networking;

public sealed class InternetRoomSession : IDisposable
{
    // Never forward a participant token to a redirected host.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Uri _server;
    private readonly bool _host;
    private readonly CancellationTokenSource _stop;
    private readonly CancellationToken _token;
    private int _disposed;
    public RoomTicket Ticket { get; }
    public event Action<string>? Closed;
    private string Path => "v1/rooms/" + InternetRoomCode.Normalize(Ticket.Code);

    private InternetRoomSession(Uri server, RoomTicket ticket, bool host, CancellationToken cancellation)
    {
        _server = server; Ticket = ticket; _host = host;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation); _token = _stop.Token;
    }

    public static Uri ServerAddress(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Informe o endereço HTTPS do serviço de salas. HTTP é permitido apenas para testes neste computador.");
        return uri;
    }

    public static async Task<InternetRoomSession> OpenAsync(Uri server, string? code, CancellationToken token)
    {
        var path = code is null ? "v1/rooms" : $"v1/rooms/{InternetRoomCode.Normalize(code)}/join";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server, path));
        // Admission is intentionally not retried: a lost response may already have reserved a seat.
        using var response = await Http.SendAsync(request, token);
        await CheckAsync(response, token);
        var ticket = await response.Content.ReadFromJsonAsync<RoomTicket>(token) ?? throw new IOException("Resposta inválida do serviço de salas.");
        return new(server, ticket, code is null, token);
    }

    public async Task RunAsync(LocalPeer peer)
    {
        try {
            using var negotiating = CancellationTokenSource.CreateLinkedTokenSource(_token);
            negotiating.CancelAfter(TimeSpan.FromMinutes(10));
            if (_host) {
                await WaitAsync(snapshot => snapshot.GuestJoined, negotiating.Token);
                // Keep gathering and signaling bounded after the guest arrives.
                negotiating.CancelAfter(TimeSpan.FromSeconds(75));
                var offer = await Task.Run(peer.CreateOfferAsync, negotiating.Token).WaitAsync(negotiating.Token);
                await PublishAsync("offer", offer, negotiating.Token);
                var snapshot = await WaitAsync(snapshot => snapshot.Answer is not null, negotiating.Token);
                peer.AcceptAnswer(snapshot.Answer!);
            }
            else {
                negotiating.CancelAfter(TimeSpan.FromSeconds(75));
                var snapshot = await WaitAsync(snapshot => snapshot.Offer is not null, negotiating.Token);
                var answer = await Task.Run(() => peer.AcceptOfferAsync(snapshot.Offer!), negotiating.Token).WaitAsync(negotiating.Token);
                await PublishAsync("answer", answer, negotiating.Token);
            }
            while (true) {
                await Task.Delay(TimeSpan.FromSeconds(2), _token);
                await ReadAsync(_token); // Heartbeat and explicit remote departure/expiry detection.
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (OperationCanceledException) { Closed?.Invoke("O tempo para conectar à sala terminou. Crie uma nova sala."); }
        catch (Exception ex) { if (!_token.IsCancellationRequested) Closed?.Invoke(ex is HttpRequestException ? "O serviço de salas está indisponível. Confira sua conexão com a internet." : ex.Message); }
    }

    private async Task<RoomSnapshot> WaitAsync(Func<RoomSnapshot, bool> ready, CancellationToken token)
    {
        while (true) {
            var snapshot = await ReadAsync(token);
            if (ready(snapshot)) return snapshot;
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
    }
    private async Task<RoomSnapshot> ReadAsync(CancellationToken token)
    {
        using var response = await RequestAsync(HttpMethod.Get, Path, null, token);
        return await response.Content.ReadFromJsonAsync<RoomSnapshot>(token) ?? throw new IOException("Resposta inválida do serviço de salas.");
    }
    private async Task PublishAsync(string kind, string sdp, CancellationToken token)
    {
        using var response = await RequestAsync(HttpMethod.Put, Path + "/" + kind, new SignalMessage(sdp), token);
    }
    private async Task<HttpResponseMessage> RequestAsync(HttpMethod method, string path, SignalMessage? body, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++) {
            try {
                using var request = new HttpRequestMessage(method, new Uri(_server, path));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Ticket.Token);
                if (body is not null) request.Content = JsonContent.Create(body);
                var response = await Http.SendAsync(request, token);
                try { await CheckAsync(response, token); return response; }
                catch { response.Dispose(); throw; }
            }
            catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException) {
                await Task.Delay(TimeSpan.FromSeconds(1 + attempt), token);
            }
        }
    }
    private static async Task CheckAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new IOException("Muitas tentativas de conexão. Aguarde um minuto e tente novamente.");
        if ((int)response.StatusCode >= 500) throw new HttpRequestException("Serviço de salas indisponível.");
        RoomError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<RoomError>(token); }
        catch (System.Text.Json.JsonException) { }
        throw new IOException(error?.Message ?? "Não foi possível acessar o serviço de salas. Confira o endereço configurado.");
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); _stop.Dispose();
        _ = CloseAsync();
    }
    private async Task CloseAsync()
    {
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); using var response = await RequestAsync(HttpMethod.Delete, Path, null, timeout.Token); }
        catch { /* A crashed/offline client is removed by the server heartbeat deadline. */ }
    }
}
