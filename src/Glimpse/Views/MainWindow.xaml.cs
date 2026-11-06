using System.Windows;
using System.Windows.Interop;
using Glimpse.ViewModels;
using Glimpse.Profiles;
using System.Windows.Input;
using Glimpse.Encoding;
namespace Glimpse.Views;
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly UserProfileStore _profileStore;
    private UserProfile _profile;
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
        PreviewKeyDown+=(_,e)=>{ if(e.Key==Key.F11 && !e.IsRepeat && _vm.HasFrame) { e.Handled=true; ShowFullScreen(); } };
        Closed += (_, _) => _vm.Dispose();
    }
    private void OnLayoutSizeChanged(object sender,SizeChangedEventArgs e) {
        bool navigation=e.NewSize.Width>=980,details=e.NewSize.Width>=1240;
        NavigationPanel.Visibility=navigation?Visibility.Visible:Visibility.Collapsed;
        NavigationColumn.Width=new GridLength(navigation?205:0);
        DetailsPanel.Visibility=details?Visibility.Visible:Visibility.Collapsed;
        DetailsColumn.Width=new GridLength(details?220:0);
        CompactProfileButton.Visibility=navigation?Visibility.Collapsed:Visibility.Visible;
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
    private ShareRequest? ChooseShareSource(VideoQuality currentQuality) {
        var dialog=new ShareSourceDialog(currentQuality) { Owner=this };
        return dialog.ShowDialog()==true ? dialog.Request : null;
    }
}
