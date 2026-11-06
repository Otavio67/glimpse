using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glimpse.Profiles;

public sealed record UserProfile(string DisplayName,string AvatarId,string? AvatarFile=null);
public sealed record DefaultAvatar(string Id,string Name);

public sealed class UserProfileStore
{
    public static readonly DefaultAvatar[] DefaultAvatars=[new("astronaut","Astro"),new("glasses","Óculos"),new("party","Festa"),new("toaster","Torradinha")];
    private readonly string _directory;
    private string ProfileFile => Path.Combine(_directory,"profile.json");
    public UserProfileStore(string? directory=null) { _directory=Path.GetFullPath(directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Glimpse")); }

    public UserProfile? Load()
    {
        try {
            var profile=JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(ProfileFile));
            if(profile is null) return null;
            string name=ValidateName(profile.DisplayName);
            if(profile.AvatarId=="custom") {
                var file=PhotoPath(profile.AvatarFile);
                if(file is null || !File.Exists(file)) return new(name,"astronaut");
                return profile with { DisplayName=name };
            }
            return new(name,DefaultAvatars.Any(a=>a.Id==profile.AvatarId)?profile.AvatarId:"astronaut");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }

    public static string ValidateName(string? value)
    {
        string name=(value??"").Trim().Normalize();
        int length=new StringInfo(name).LengthInTextElements;
        if(length is <1 or >24 || name.Any(char.IsControl)) throw new ArgumentException("Escolha um nome de 1 a 24 caracteres, sem quebras de linha.");
        return name;
    }

    public UserProfile Save(string name,string avatarId,BitmapSource? newPhoto=null,string? existingPhoto=null)
    {
        name=ValidateName(name);
        if(avatarId!="custom" && !DefaultAvatars.Any(a=>a.Id==avatarId)) throw new ArgumentException("Escolha um dos avatares disponíveis.");
        if(avatarId=="custom" && newPhoto is null && (PhotoPath(existingPhoto) is not { } file || !File.Exists(file))) throw new ArgumentException("Escolha uma foto ou um dos robôs.");
        Directory.CreateDirectory(_directory);
        string? photo=null;
        if(avatarId=="custom") {
            photo=existingPhoto;
            if(newPhoto is not null) {
                photo="photo-"+Guid.NewGuid().ToString("N")+".png";
                var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(newPhoto));
                using var output=File.Create(Path.Combine(_directory,photo)); png.Save(output);
            }
        }
        var profile=new UserProfile(name,avatarId,photo);
        string temporary=Path.Combine(_directory,"profile-"+Guid.NewGuid().ToString("N")+".tmp");
        File.WriteAllText(temporary,JsonSerializer.Serialize(profile));
        File.Move(temporary,ProfileFile,true);
        return profile;
    }

    public ImageSource AvatarImage(UserProfile profile)
    {
        if(profile.AvatarId=="custom" && PhotoPath(profile.AvatarFile) is { } file) {
            try { return ImportPhoto(file); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException) { }
        }
        return BuiltinImage(DefaultAvatars.Any(a=>a.Id==profile.AvatarId)?profile.AvatarId:"astronaut");
    }
    public static BitmapImage BuiltinImage(string id)
    {
        if(!DefaultAvatars.Any(a=>a.Id==id)) throw new ArgumentException("Avatar inválido.");
        var image=new BitmapImage(); image.BeginInit(); image.UriSource=new Uri($"pack://application:,,,/Glimpse;component/Assets/Avatars/{id}.png"); image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=256; image.EndInit(); image.Freeze(); return image;
    }
    public static BitmapSource ImportPhoto(string file)
    {
        using var input=File.OpenRead(file);
        if(input.Length>20*1024*1024) throw new ArgumentException("Escolha uma imagem de até 20 MB.");
        var metadata=BitmapDecoder.Create(input,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames[0];
        int width=metadata.PixelWidth,height=metadata.PixelHeight;
        if(width<1 || height<1 || width>16000 || height>16000 || (long)width*height>60_000_000) throw new ArgumentException("Escolha uma imagem de até 60 megapixels e 16000 pixels por lado.");
        input.Position=0;
        var image=new BitmapImage(); image.BeginInit(); image.StreamSource=input; image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=Math.Min(512,width); image.EndInit(); image.Freeze();
        int side=Math.Min(image.PixelWidth,image.PixelHeight);
        var cropped=new CroppedBitmap(image,new Int32Rect((image.PixelWidth-side)/2,(image.PixelHeight-side)/2,side,side));
        var result=new TransformedBitmap(cropped,new ScaleTransform(256d/side,256d/side)); result.Freeze(); return result;
    }
    private string? PhotoPath(string? name) => name is not null && Regex.IsMatch(name,"^photo-[a-f0-9]{32}\\.png$",RegexOptions.CultureInvariant) ? Path.Combine(_directory,name) : null;
}
