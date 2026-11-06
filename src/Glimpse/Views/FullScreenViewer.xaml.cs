using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Glimpse.ViewModels;

namespace Glimpse.Views;

public partial class FullScreenViewer : Window
{
    private readonly MainViewModel _viewModel;
    public FullScreenViewer(MainViewModel viewModel)
    {
        InitializeComponent(); _viewModel=viewModel; DataContext=viewModel;
        viewModel.PropertyChanged+=SessionChanged;
        Closed+=(_,_)=>viewModel.PropertyChanged-=SessionChanged;
    }
    private void SessionChanged(object? sender,PropertyChangedEventArgs e) { if(e.PropertyName==nameof(MainViewModel.IsInRoom) && !_viewModel.IsInRoom) Close(); }
    private void ViewerKeyDown(object sender,KeyEventArgs e) { if(!e.IsRepeat && e.Key is Key.Escape or Key.F11) { e.Handled=true; Close(); } }
    private void ExitFullScreen(object sender,RoutedEventArgs e) => Close();
    private void VideoDoubleClick(object sender,MouseButtonEventArgs e) { if(e.ClickCount==2) { e.Handled=true; Close(); } }
}
