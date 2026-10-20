using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using PcImpressz.Contracts;

namespace PcImpressz.Shell.Windows;

internal sealed class HostConnection(string directory, string? hostOverride)
{
    private Process? _ownedHost;

    public async Task<HostDiagnostic> ReadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await using var pipe = new NamedPipeClientStream(".", DiagnosticChannel.ForDirectory(directory),
            PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await pipe.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (bytes.Length + count > 262_144) throw new IOException("Túl nagy állapotválasz.");
            bytes.Write(buffer, 0, count);
        }
        return JsonSerializer.Deserialize<HostDiagnostic>(bytes.ToArray())
            ?? throw new IOException("Érvénytelen állapotválasz.");
    }

    public async Task<JsonElement> SendAsync(string operation, object payload, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(100));
        await using var pipe = new NamedPipeClientStream(".", DiagnosticChannel.ForDirectory(directory) + "-commands",
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000, deadline.Token);
        await LocalProtocol.WriteAsync(pipe, new LocalRequest(operation, JsonSerializer.SerializeToElement(payload)), deadline.Token);
        var result = await LocalProtocol.ReadAsync<LocalResponse>(pipe, deadline.Token);
        if (!result.Success) throw new InvalidOperationException(result.Error ?? "A művelet nem sikerült.");
        return result.Data ?? JsonSerializer.SerializeToElement(new { });
    }

    public void StartIfNeeded()
    {
        if (_ownedHost is { HasExited: false }) return;
        _ownedHost?.Dispose();
        var host = hostOverride ?? Path.Combine(AppContext.BaseDirectory, "host", "PcImpressz.ApplicationHost.dll");
        if (!File.Exists(host)) throw new FileNotFoundException("A háttérfolyamat nem található. Indítsd a Start.cmd fájlt.", host);
        var dotnet = Environment.ProcessPath;
        if (dotnet is null || !Path.GetFileNameWithoutExtension(dotnet).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            dotnet = root is not null ? Path.Combine(root, "dotnet.exe") : "dotnet.exe";
        }
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(host)!
        };
        start.ArgumentList.Add(host);
        start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(directory);
        start.ArgumentList.Add("--owner-pid"); start.ArgumentList.Add(Environment.ProcessId.ToString());
        _ownedHost = Process.Start(start) ?? throw new IOException("A háttérfolyamat nem indítható.");
        // Drain both streams; the Host has its own persistent log files.
        _ownedHost.OutputDataReceived += (_, _) => { };
        _ownedHost.ErrorDataReceived += (_, _) => { };
        _ownedHost.BeginOutputReadLine(); _ownedHost.BeginErrorReadLine();
    }

    public void Release() { _ownedHost?.Dispose(); _ownedHost = null; }
}
