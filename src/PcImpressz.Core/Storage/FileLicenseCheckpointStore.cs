using System.Text.Json;
using PcImpressz.Core.Licensing;

namespace PcImpressz.Core.Storage;

// These are non-secret checkpoints. OS permissions are required in production;
// a local administrator can still restore or edit them. This is not a trusted clock.
public sealed class FileLicenseCheckpointStore(string filePath) : ILicenseCheckpointStore
{
    public LicenseCheckpoint Read()
    {
        if (!File.Exists(filePath)) return new(0, new(StringComparer.Ordinal));
        if (new FileInfo(filePath).Length > 131_072) throw new IOException("Checkpoint is too large.");
        var checkpoint = JsonSerializer.Deserialize<LicenseCheckpoint>(File.ReadAllText(filePath))
            ?? throw new IOException("Invalid checkpoint.");
        if (checkpoint.LastObservedUnixTime < 0 || checkpoint.HighestRevisions is null
            || checkpoint.HighestRevisions.Count > 1024
            || checkpoint.HighestRevisions.Any(p => !Identifiers.IsValid(p.Key) || p.Value < 1))
            throw new IOException("Invalid checkpoint fields.");
        return checkpoint;
    }

    public void Write(LicenseCheckpoint checkpoint)
    {
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, checkpoint);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
