using System.Windows;
using System.Windows.Interop;
using Glimpse.ViewModels;
namespace Glimpse.Views;
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    public MainWindow()
    {
        InitializeComponent();
        // Start on the primary monitor and keep the title bar inside its usable area.
        var bounds = InitialWindowPlacement.FitInside(SystemParameters.WorkArea);
        MinWidth = Math.Min(700, bounds.Width);
        MinHeight = Math.Min(500, bounds.Height);
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.Left;
        Top = bounds.Top;
        _vm = new MainViewModel(() => new WindowInteropHelper(this).Handle,OpenRoomDialog);
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();
    }
    private void OpenRoomDialog(bool host) {
        var dialog=new RoomSetupDialog(host,_vm) { Owner=this };
        dialog.ShowDialog();
    }
}
