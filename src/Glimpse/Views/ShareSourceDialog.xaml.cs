using System.Windows;
namespace Glimpse.Views;

public partial class ShareSourceDialog : Window
{
    public string? SelectedSource { get; private set; }
    public ShareSourceDialog() { InitializeComponent(); MaxWidth=Math.Max(320,SystemParameters.WorkArea.Width-40); }
    private void ChooseWindow(object sender,RoutedEventArgs e) { SelectedSource="window"; DialogResult=true; }
    private void ChooseDisplay(object sender,RoutedEventArgs e) { SelectedSource="display"; DialogResult=true; }
}
