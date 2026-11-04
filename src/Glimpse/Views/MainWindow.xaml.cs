using System.Windows;
using System.Windows.Interop;
using Glimpse.ViewModels;
namespace Glimpse.Views;
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    public MainWindow() { InitializeComponent(); _vm = new MainViewModel(() => new WindowInteropHelper(this).Handle); DataContext = _vm; Closed += (_, _) => _vm.Dispose(); }
}
