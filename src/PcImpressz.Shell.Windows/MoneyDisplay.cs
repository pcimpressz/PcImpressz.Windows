using System.Globalization;
namespace PcImpressz.Shell.Windows;
internal static class MoneyDisplay
{
    public static int Decimals{get;set;}
    public static string Format(decimal value)=>value.ToString("N"+Decimals,CultureInfo.GetCultureInfo("hu-HU"));
}