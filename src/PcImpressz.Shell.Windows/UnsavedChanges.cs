using System.Windows;
namespace PcImpressz.Shell.Windows;
internal static class UnsavedChanges
{
    public static bool Confirm(string message,string title)
    {
        var owner=Application.Current?.MainWindow;
        return (owner is {IsVisible:true}?MessageBox.Show(owner,message,title,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No):MessageBox.Show(message,title,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No))==MessageBoxResult.Yes;
    }
}