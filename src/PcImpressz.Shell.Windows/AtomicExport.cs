using System.IO;
using System.Text;

namespace PcImpressz.Shell.Windows;

internal static class AtomicExport
{
    public static async Task WriteAsync(string destination, string content, CancellationToken cancellationToken)
    {
        destination = Path.GetFullPath(destination);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".pcimpressz-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(true), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
