using System.Globalization;
using System.Resources;
namespace Glimpse.Resources;
public static class Strings
{
    private static readonly ResourceManager Manager = new("Glimpse.Resources.Strings", typeof(Strings).Assembly);
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static string Title => Get("Title");
    public static string Tagline => Get("Tagline");
    public static string Local => Get("Local");
    public static string Waiting => Get("Waiting");
    public static string YourText => Get("YourText");
    public static string OtherText => Get("OtherText");
    public static string CreateOffer => Get("CreateOffer");
    public static string CreateAnswer => Get("CreateAnswer");
    public static string ApplyAnswer => Get("ApplyAnswer");
    public static string Copy => Get("Copy");
    public static string Share => Get("Share");
    public static string Stop => Get("Stop");
    public static string Disconnect => Get("Disconnect");
    public static string Session => Get("Session");
    public static string PrivateLan => Get("PrivateLan");
    public static string SessionDescription => Get("SessionDescription");
    public static string Capture => Get("Capture");
    public static string PickerDescription => Get("PickerDescription");
    public static string Ready => Get("Ready");
    public static string PreparingOffer => Get("PreparingOffer");
    public static string SendOffer => Get("SendOffer");
    public static string PreparingAnswer => Get("PreparingAnswer");
    public static string SendAnswer => Get("SendAnswer");
    public static string Connecting => Get("Connecting");
    public static string Stopped => Get("Stopped");
    public static string Disconnected => Get("Disconnected");
    public static string CaptureCancelled => Get("CaptureCancelled");
    public static string Sharing => Get("Sharing");
    public static string HardwareSharing => Get("HardwareSharing");
    public static string SoftwareSharing => Get("SoftwareSharing");
}
