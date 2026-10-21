using System.Security.Principal;
using System.Text.Json;
namespace PcImpressz.ApplicationHost;
internal static class DevelopmentAccess
{
    public static bool Enabled(string directory){if(!OperatingSystem.IsWindows())return false;using var identity=WindowsIdentity.GetCurrent();return Allows(directory,identity.User?.Value??"");}
    public static bool Allows(string directory,string sid)
    {
        var file=Path.Combine(directory,"development-access.json");
        try
        {
            if(string.IsNullOrWhiteSpace(sid)||!File.Exists(file)||new FileInfo(file).Length>4096||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)return false;
            using var document=JsonDocument.Parse(File.ReadAllText(file));var root=document.RootElement;
            return root.TryGetProperty("enabled",out var enabled)&&enabled.ValueKind==JsonValueKind.True&&root.TryGetProperty("windowsSid",out var owner)&&owner.ValueKind==JsonValueKind.String&&owner.GetString()==sid;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return false;}
    }
}