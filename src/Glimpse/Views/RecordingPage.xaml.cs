using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Glimpse.Capture;
using Glimpse.Encoding;

namespace Glimpse.Views;

public partial class RecordingPage : UserControl
{
    private readonly Stopwatch _clock=new();
    private readonly DispatcherTimer _timer;
    private Task _operation=Task.CompletedTask;
    private WindowsCaptureSource? _capture;
    private CancellationTokenSource? _abort;
    private bool _finishRequested;
    private bool _recordingFailed;
    private string? _lastVideo;

    public event Action<bool>? RecordingChanged;
    public bool IsBusy => !_operation.IsCompleted;

    public RecordingPage()
    {
        InitializeComponent();
        FolderText.Text=ScreenRecorder.Folder;
        _timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(200) };
        _timer.Tick+=(_,_)=>UpdateTime();
        Loaded+=(_,_)=>FindLastVideo();
    }

    private void Start_Click(object sender,RoutedEventArgs e)
    {
        if(!IsBusy && Window.GetWindow(this) is { } owner) _operation=RecordAsync(owner);
    }

    private async Task RecordAsync(Window owner)
    {
        _finishRequested=false;
        _recordingFailed=false;
        StartButton.IsEnabled=false;
        _clock.Reset();
        UpdateTime();
        StatusText.Text="Escolha a fonte no seletor do Windows.";
        string? sourceNotice=null;
        try {
            using var capture=new WindowsCaptureSource();
            if(!await capture.PickAsync(new WindowInteropHelper(owner).Handle,ScreenRecorder.FramesPerSecond) || _finishRequested) {
                StatusText.Text="Seleção cancelada.";
                return;
            }
            using var abort=new CancellationTokenSource();
            _capture=capture;
            _abort=abort;
            capture.Error+=message=>Interlocked.Exchange(ref sourceNotice,message);
            capture.Start();
            StopButton.IsEnabled=true;
            ModeText.Text="●  GRAVANDO";
            StatusText.Text="Gravando a fonte selecionada. Clique em Parar e salvar quando terminar.";
            _clock.Start();
            _timer.Start();
            RecordingChanged?.Invoke(true);

            var video=await Task.Run(()=>ScreenRecorder.SaveAsync(capture.Frames,capture.Width,capture.Height,ScreenRecorder.Folder,abort.Token));
            var notice=Volatile.Read(ref sourceNotice);
            if(video is not null) {
                ShowLastVideo(video);
                StatusText.Text=notice is null ? "Vídeo salvo no seu PC." : "Vídeo salvo. "+notice;
            } else StatusText.Text=notice ?? "Nenhuma imagem foi recebida. Escolha uma fonte visível e tente novamente.";
        }
        catch(OperationCanceledException) { _recordingFailed=true; StatusText.Text="O vídeo demorou demais para finalizar. Tente gravar novamente."; }
        catch(Exception ex) { _recordingFailed=true; StatusText.Text="Não foi possível salvar a gravação. "+ex.Message; }
        finally {
            _capture=null;
            _abort=null;
            _timer.Stop();
            _clock.Stop();
            UpdateTime();
            ModeText.Text="GRAVAÇÃO LOCAL";
            RecordingChanged?.Invoke(false);
            StopButton.IsEnabled=false;
            StartButton.IsEnabled=true;
        }
    }

    private void Stop_Click(object sender,RoutedEventArgs e) => Stop();

    private void Stop()
    {
        _finishRequested=true;
        if(_capture is null || !StopButton.IsEnabled) return;
        StopButton.IsEnabled=false;
        StatusText.Text="Salvando o vídeo…";
        ModeText.Text="FINALIZANDO";
        _clock.Stop();
        _timer.Stop();
        UpdateTime();
        _abort?.CancelAfter(TimeSpan.FromSeconds(25));
        _capture.Complete();
    }

    internal async Task<bool> FinishAsync()
    {
        Stop();
        await _operation;
        return !_recordingFailed;
    }

    private void UpdateTime()
    {
        var elapsed=_clock.Elapsed;
        ElapsedText.Text=$"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void FindLastVideo()
    {
        try {
            if(Directory.Exists(ScreenRecorder.Folder))
                ShowLastVideo(new DirectoryInfo(ScreenRecorder.Folder).EnumerateFiles("Glimpse_*.mp4")
                    .OrderByDescending(file=>file.LastWriteTimeUtc).FirstOrDefault()?.FullName);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
    }

    private void ShowLastVideo(string? path)
    {
        _lastVideo=path;
        FileText.Text=path is null ? "" : "Último vídeo · "+Path.GetFileName(path);
        OpenVideoButton.IsEnabled=path is not null;
    }

    private void OpenFolder_Click(object sender,RoutedEventArgs e) => OpenInWindows(ScreenRecorder.Folder,true);
    private void OpenVideo_Click(object sender,RoutedEventArgs e)
    {
        if(_lastVideo is { } path) OpenInWindows(path,false);
    }

    private void OpenInWindows(string path,bool folder)
    {
        try {
            if(folder) Directory.CreateDirectory(path);
            else if(!File.Exists(path)) { ShowLastVideo(null); StatusText.Text="O último vídeo foi movido ou removido."; return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute=true });
        }
        catch(Exception ex) when(ex is IOException or Win32Exception or UnauthorizedAccessException) {
            StatusText.Text=folder ? "Não foi possível abrir a pasta." : "Não foi possível abrir o vídeo.";
        }
    }
}
