using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Glimpse.ViewModels;
using Glimpse.Profiles;
using System.Windows.Input;
using System.Windows.Media;
using Glimpse.Encoding;
namespace Glimpse.Views;
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly UserProfileStore _profileStore;
    private UserProfile _profile;
    private bool _showingRecording;
    private bool _finishingBeforeClose, _recordingFinishedForClose;
    private static readonly Brush ActiveNavigation=CreateBrush(0x30,0x34,0x48);
    private static readonly Brush InactiveNavigation=CreateBrush(0x20,0x26,0x38);
    public MainWindow(UserProfile? profile=null,UserProfileStore? profileStore=null)
    {
        InitializeComponent();
        _profileStore=profileStore??new UserProfileStore();
        _profile=profile??_profileStore.Load()??new UserProfile("Você","astronaut");
        // Start on the primary monitor and keep the title bar inside its usable area.
        var bounds = InitialWindowPlacement.FitInside(SystemParameters.WorkArea);
        MinWidth = Math.Min(700, bounds.Width);
        MinHeight = Math.Min(500, bounds.Height);
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.Left;
        Top = bounds.Top;
        _vm = new MainViewModel(() => new WindowInteropHelper(this).Handle,OpenRoomDialog,ChooseShareSource,EditProfile);
        _vm.ApplyProfile(_profile,_profileStore);
        DataContext = _vm;
        PreviewKeyDown+=(_,e)=>{ if(!_showingRecording && e.Key==Key.F11 && !e.IsRepeat && _vm.HasFrame) { e.Handled=true; ShowFullScreen(); } };
        Recorder.RecordingChanged+=recording=>RecordingIndicator.Visibility=recording ? Visibility.Visible : Visibility.Collapsed;
        Closing+=FinishRecordingBeforeClose;
        Closed += (_, _) => _vm.Dispose();
    }
    private void OnLayoutSizeChanged(object sender,SizeChangedEventArgs e) => FitLayout(e.NewSize.Width);
    private void FitLayout(double width) {
        bool navigation=width>=980,details=width>=1240 && !_showingRecording;
        NavigationPanel.Visibility=navigation?Visibility.Visible:Visibility.Collapsed;
        NavigationColumn.Width=new GridLength(navigation?205:0);
        DetailsPanel.Visibility=details?Visibility.Visible:Visibility.Collapsed;
        DetailsColumn.Width=new GridLength(details?220:0);
        CompactProfileButton.Visibility=navigation?Visibility.Collapsed:Visibility.Visible;
    }
    private static Brush CreateBrush(byte r,byte g,byte b) {
        var brush=new SolidColorBrush(Color.FromRgb(r,g,b));
        brush.Freeze();
        return brush;
    }
    private void ShowRoom_Click(object sender,RoutedEventArgs e) => ShowRecording(false);
    private void ShowRecording_Click(object sender,RoutedEventArgs e) => ShowRecording(true);
    private void ShowRecording(bool show) {
        _showingRecording=show;
        Recorder.Visibility=show ? Visibility.Visible : Visibility.Collapsed;
        RoomPage.Visibility=RoomNavigationInfo.Visibility=show ? Visibility.Collapsed : Visibility.Visible;
        RoomRail.Background=RoomNavigation.Background=show ? InactiveNavigation : ActiveNavigation;
        RecordingRail.Background=RecordingNavigation.Background=show ? ActiveNavigation : InactiveNavigation;
        FitLayout(AppLayout.ActualWidth);
    }
    private async void FinishRecordingBeforeClose(object? sender,CancelEventArgs e) {
        if(_recordingFinishedForClose || !Recorder.IsBusy) return;
        e.Cancel=true;
        if(_finishingBeforeClose) return;
        _finishingBeforeClose=true;
        ShowRecording(true);
        if(!await Recorder.FinishAsync()) { _finishingBeforeClose=false; return; }
        _recordingFinishedForClose=true;
        Close();
    }
    private void ShowFullScreen() { if(_vm.HasFrame) new FullScreenViewer(_vm) { Owner=this }.ShowDialog(); }
    private void OpenFullScreen(object sender,RoutedEventArgs e) => ShowFullScreen();
    private void VideoDoubleClick(object sender,MouseButtonEventArgs e) { if(e.ClickCount==2 && _vm.HasFrame) { e.Handled=true; ShowFullScreen(); } }
    private void EditProfile() {
        var dialog=new ProfileSetupDialog(_profileStore,_profile) { Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        if(dialog.ShowDialog()==true && dialog.Profile is { } profile) { _profile=profile; _vm.ApplyProfile(profile,_profileStore); }
    }
    private void OpenRoomDialog(bool host) {
        var dialog=new RoomSetupDialog(host,_vm) { Owner=this };
        dialog.ShowDialog();
    }
    internal void OpenRoomInvite(string code) {
        ShowRecording(false);
        _vm.RemoteText=code;
        var dialog=new RoomSetupDialog(false,_vm,true) { Owner=this };
        dialog.ShowDialog();
    }
    private ShareRequest? ChooseShareSource(VideoQuality currentQuality) {
        var dialog=new ShareSourceDialog(currentQuality) { Owner=this };
        return dialog.ShowDialog()==true ? dialog.Request : null;
    }
}
