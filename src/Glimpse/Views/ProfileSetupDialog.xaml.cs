using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Glimpse.Profiles;
using Microsoft.Win32;

namespace Glimpse.Views;

public partial class ProfileSetupDialog : Window
{
    private readonly UserProfileStore _store;
    private string _avatarId="astronaut";
    private string? _existingPhoto;
    private BitmapSource? _newPhoto;
    public UserProfile? Profile { get; private set; }
    public ProfileSetupDialog(UserProfileStore store,UserProfile? current=null)
    {
        InitializeComponent(); _store=store;
        MaxHeight=Math.Max(320,SystemParameters.WorkArea.Height-60);
        MaxWidth=Math.Max(340,SystemParameters.WorkArea.Width-40);
        AstroImage.Source=UserProfileStore.BuiltinImage("astronaut"); GlassesImage.Source=UserProfileStore.BuiltinImage("glasses");
        PartyImage.Source=UserProfileStore.BuiltinImage("party"); ToasterImage.Source=UserProfileStore.BuiltinImage("toaster");
        current??=new UserProfile("","astronaut"); NameInput.Text=current.DisplayName;
        _avatarId=current.AvatarId; _existingPhoto=current.AvatarFile;
        SelectedPhoto.ImageSource=store.AvatarImage(current);
        foreach(var choice in AvatarGrid.Children.OfType<RadioButton>()) choice.IsChecked=(string)choice.Tag==_avatarId;
        if(current.DisplayName.Length>0) { Heading.Text="Seu perfil"; SaveButton.Content="Salvar perfil"; CancelButton.Content="Cancelar"; }
        Loaded+=(_,_)=>{ NameInput.Focus(); NameInput.SelectAll(); };
    }
    private void ChooseAvatar(object sender,RoutedEventArgs e)
    {
        _avatarId=(string)((RadioButton)sender).Tag;
        _newPhoto=null;
        SelectedPhoto.ImageSource=UserProfileStore.BuiltinImage(_avatarId);
        ErrorMessage.Text="";
    }
    private void ImportPhoto(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFileDialog { Title="Escolha sua foto", Filter="Imagens|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists=true, Multiselect=false };
        if(picker.ShowDialog(this)!=true) return;
        try {
            var photo=UserProfileStore.ImportPhoto(picker.FileName);
            foreach(var choice in AvatarGrid.Children.OfType<RadioButton>()) choice.IsChecked=false;
            _newPhoto=photo; _avatarId="custom"; SelectedPhoto.ImageSource=photo; ErrorMessage.Text="";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException) { ErrorMessage.Text="Não foi possível abrir essa imagem. Escolha uma foto PNG, JPG ou BMP de até 20 MB."; }
    }
    private void SaveProfile(object sender,RoutedEventArgs e)
    {
        try { Profile=_store.Save(NameInput.Text,_avatarId,_newPhoto,_existingPhoto); DialogResult=true; }
        catch(ArgumentException ex) { ErrorMessage.Text=ex.Message; NameInput.Focus(); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { ErrorMessage.Text="Não foi possível salvar seu perfil neste PC. Verifique o acesso à pasta do usuário e tente novamente."; }
    }
}
