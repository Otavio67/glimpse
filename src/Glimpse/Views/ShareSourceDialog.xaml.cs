using System.Windows;
using System.Windows.Controls;
using Glimpse.Encoding;
namespace Glimpse.Views;

public partial class ShareSourceDialog : Window
{
    public ShareRequest? Request { get; private set; }
    public ShareSourceDialog(VideoQuality? currentQuality=null) {
        InitializeComponent(); MaxWidth=Math.Max(320,SystemParameters.WorkArea.Width-40); MaxHeight=Math.Max(320,SystemParameters.WorkArea.Height-60);
        QualityChoice.ItemsSource=VideoQuality.Presets; QualityChoice.SelectedItem=VideoQuality.FromId(currentQuality?.Id);
    }
    private void QualityChanged(object sender,SelectionChangedEventArgs e) { if(QualityChoice.SelectedItem is VideoQuality quality) QualityHelp.Text=quality.UploadHint; }
    private void ChooseWindow(object sender,RoutedEventArgs e) { Request=new("window",(VideoQuality)QualityChoice.SelectedItem); DialogResult=true; }
    private void ChooseDisplay(object sender,RoutedEventArgs e) { Request=new("display",(VideoQuality)QualityChoice.SelectedItem); DialogResult=true; }
}
