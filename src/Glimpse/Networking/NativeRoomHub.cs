namespace Glimpse.Networking;

// A star topology: only the host needs a reachable port. Guests never connect to each other.
public sealed class NativeRoomHub : IDisposable
{
    public const int MaxParticipants=8;
    private readonly NativeRoomHost _host;
    private readonly object _gate=new(), _hostPresenter=new();
    private readonly List<NativeRoomConnection> _guests=new();
    private object? _presenter;
    private bool _disposed;
    public event Action<byte[]>? VideoFrame;
    public event Action? SharingStopped;
    public event Action? PresentationRevoked;
    public event Action<int>? ParticipantCountChanged;
    public int ParticipantCount { get { lock(_gate) return _guests.Count+1; } }
    public NativeRoomHub(NativeRoomHost host) { _host=host; }
    public async Task RunAsync(CancellationToken token)
    {
        while(true) {
            var guest=await _host.AcceptAsync(token,()=>ParticipantCount<MaxParticipants);
            lock(_gate) {
                if(_disposed || token.IsCancellationRequested) { guest.Dispose(); return; }
                _guests.Add(guest);
                guest.VideoFrame+=frame=>Receive(guest,frame);
                guest.SharingStarted+=()=>Present(guest);
                guest.SharingStopped+=()=>StopPresenting(guest);
                guest.Closed+=message=>_ = Task.Run(()=>Remove(guest));
                PublishCount();
            }
            _=guest.RunAsync();
        }
    }
    public void BeginHostSharing() => Present(_hostPresenter);
    public void StopHostSharing() => StopPresenting(_hostPresenter);
    public bool TrySendVideo(byte[] frame)
    {
        lock(_gate) {
            if(_disposed || !ReferenceEquals(_presenter,_hostPresenter)) return false;
            foreach(var guest in _guests) guest.TrySendVideo(frame);
            return true;
        }
    }
    private void Present(object sender)
    {
        bool revokeHost=false;
        lock(_gate) {
            if(_disposed || (sender is NativeRoomConnection c && !_guests.Contains(c)) || ReferenceEquals(_presenter,sender)) return;
            revokeHost=ReferenceEquals(_presenter,_hostPresenter);
            if(_presenter is NativeRoomConnection previous) previous.RevokePresentation();
            _presenter=sender;
            foreach(var guest in _guests) guest.NotifySharingStopped();
        }
        SharingStopped?.Invoke();
        if(revokeHost) PresentationRevoked?.Invoke();
    }
    private void Receive(NativeRoomConnection sender,byte[] frame)
    {
        lock(_gate) {
            if(_disposed || !ReferenceEquals(_presenter,sender)) return;
            foreach(var guest in _guests) if(!ReferenceEquals(sender,guest)) guest.TrySendVideo(frame);
        }
        VideoFrame?.Invoke(frame);
    }
    private void StopPresenting(object sender)
    {
        lock(_gate) {
            if(_disposed || !ReferenceEquals(_presenter,sender)) return;
            _presenter=null;
            foreach(var guest in _guests) guest.NotifySharingStopped();
        }
        SharingStopped?.Invoke();
    }
    private void Remove(NativeRoomConnection guest)
    {
        bool stopped;
        lock(_gate) {
            if(_disposed || !_guests.Remove(guest)) return;
            stopped=ReferenceEquals(_presenter,guest);
            if(stopped) { _presenter=null; foreach(var remaining in _guests) remaining.NotifySharingStopped(); }
            PublishCount();
        }
        guest.Dispose();
        if(stopped) SharingStopped?.Invoke();
    }
    private void PublishCount()
    {
        int count=_guests.Count+1;
        foreach(var guest in _guests) guest.SendParticipantCount(count);
        ParticipantCountChanged?.Invoke(count);
    }
    public void Dispose()
    {
        lock(_gate) { if(_disposed) return; _disposed=true; foreach(var guest in _guests) guest.Dispose(); _guests.Clear(); _host.Dispose(); }
    }
}
