using System.Windows;
using System.Windows.Interop;
using Glimpse.ViewModels;
using Glimpse.Profiles;
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
        Closed += (_, _) => _vm.Dispose();
    }
    private void EditProfile() {
        var dialog=new ProfileSetupDialog(_profileStore,_profile) { Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        if(dialog.ShowDialog()==true && dialog.Profile is { } profile) { _profile=profile; _vm.ApplyProfile(profile,_profileStore); }
    }
    private void OpenRoomDialog(bool host) {
        var dialog=new RoomSetupDialog(host,_vm) { Owner=this };
        dialog.ShowDialog();
    }
    private string? ChooseShareSource() {
        var dialog=new ShareSourceDialog { Owner=this };
        return dialog.ShowDialog()==true ? dialog.SelectedSource : null;
    }
}
