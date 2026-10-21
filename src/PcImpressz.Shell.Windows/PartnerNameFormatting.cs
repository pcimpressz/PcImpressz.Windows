using System.Globalization;
using System.Text.RegularExpressions;
namespace PcImpressz.Shell.Windows;
internal static class PartnerNameFormatting
{
    public static bool Enabled{get;set;}=true;
    private static readonly CultureInfo Hungarian=CultureInfo.GetCultureInfo("hu-HU");
    private static readonly Dictionary<string,string> Abbreviations=new(StringComparer.OrdinalIgnoreCase)
    { ["KFT"]="Kft",["BT"]="Bt",["ZRT"]="Zrt",["NYRT"]="Nyrt",["KKT"]="Kkt",["PC"]="PC",["IT"]="IT",["NAV"]="NAV",["IBM"]="IBM",["HP"]="HP",["EU"]="EU",["II"]="II",["III"]="III",["IV"]="IV",["VI"]="VI",["VII"]="VII",["VIII"]="VIII",["IX"]="IX",["XI"]="XI",["XII"]="XII",["XIII"]="XIII",["XIV"]="XIV",["XV"]="XV",["XVI"]="XVI",["XVII"]="XVII",["XVIII"]="XVIII",["XIX"]="XIX",["XX"]="XX",["XXI"]="XXI",["XXII"]="XXII",["XXIII"]="XXIII" };
    public static string Format(string text)
    {
        if(!Enabled)return text;
        // Only normalize all-capital input; preserve mixed-case brands and manually entered casing.
        if(!text.Any(char.IsLetter)||text.Any(char.IsLower))return text;
        var title=Hungarian.TextInfo.ToTitleCase(text.ToLower(Hungarian));
        return Regex.Replace(title,@"\p{L}+",match=>Abbreviations.TryGetValue(match.Value,out var value)?value:match.Value);
    }
}