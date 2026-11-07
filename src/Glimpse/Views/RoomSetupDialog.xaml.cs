using System.ComponentModel;
using System.Windows;
using Glimpse.ViewModels;

namespace Glimpse.Views;

public partial class RoomSetupDialog : Window
{
    private readonly MainViewModel _viewModel;
    private readonly bool _host;
    public RoomSetupDialog(bool host,MainViewModel viewModel,bool joinFromLink=false)
    {
        InitializeComponent();
        _host=host; _viewModel=viewModel; DataContext=viewModel;
        Title=host ? "Hospedar sala" : "Entrar na sala de um amigo";
        Heading.Text=Title;
        Description.Text=host ? "Crie seu espaço e envie o link para seus amigos." : "Cole o link ou o código que seu amigo enviou.";
        HostPanel.Visibility=host ? Visibility.Visible : Visibility.Collapsed;
        JoinPanel.Visibility=host ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Content=viewModel.IsInRoom ? "Concluir" : "Voltar";
        MaxHeight=Math.Max(240,SystemParameters.WorkArea.Height-60);
        MaxWidth=Math.Max(320,SystemParameters.WorkArea.Width-40);
        viewModel.PropertyChanged+=SessionChanged;
        Loaded+=(_,_)=>{ if(!host) InviteInput.Focus(); };
        if(joinFromLink) ContentRendered+=JoinFromLink;
        Closed+=(_,_)=>{ viewModel.PropertyChanged-=SessionChanged; viewModel.CancelRoomSetup(); };
    }
    private void JoinFromLink(object? sender,EventArgs e)
    {
        ContentRendered-=JoinFromLink;
        if(_viewModel.JoinRoomCommand.CanExecute(null)) _viewModel.JoinRoomCommand.Execute(null);
    }
    private void SessionChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName!=nameof(MainViewModel.IsInRoom) || !_viewModel.IsInRoom) return;
        if(_host) CloseButton.Content="Concluir";
        else Close();
    }
    private void CloseDialog(object sender,RoutedEventArgs e) => Close();
}
