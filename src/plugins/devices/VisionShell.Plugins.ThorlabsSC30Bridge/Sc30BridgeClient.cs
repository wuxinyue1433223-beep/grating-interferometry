using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VisionShell.SC30Bridge.Protocol;

namespace VisionShell.Plugins.ThorlabsSC30Bridge;

internal sealed class Sc30BridgeClient
{
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<string?> BridgePath = new(ResolveBridgePath);
    private static readonly Lazy<string> BridgePipeName = new(CreateBridgePipeName);

    public async Task<Sc30BridgeResponse> SendAsync(Sc30BridgeRequest request, CancellationToken token = default)
    {
        try
        {
            return await SendOnceAsync(request, token);
        }
        catch (IOException)
        {
            await EnsureBridgeStartedAsync(token);
            return await SendOnceAsync(request, token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            await EnsureBridgeStartedAsync(token);
            return await SendOnceAsync(request, token);
        }
    }

    private static async Task<Sc30BridgeResponse> SendOnceAsync(Sc30BridgeRequest request, CancellationToken token)
    {
        await using var pipe = new NamedPipeClientStream(".", BridgePipeName.Value, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await pipe.ConnectAsync(timeout.Token);

        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
        var line = await reader.ReadLineAsync(timeout.Token);
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new IOException("SC30 bridge closed the pipe without a response.");
        }

        return JsonSerializer.Deserialize<Sc30BridgeResponse>(line, JsonOptions)
            ?? throw new IOException("SC30 bridge returned an invalid response.");
    }

    private static string? ResolveBridgePath()
    {
        var relative = Path.Combine("bridges", "thorlabs-sc30-x86", "win-x86", "VisionShell.SC30Bridge.exe");
        var direct = Path.Combine(AppContext.BaseDirectory, relative);
        if (File.Exists(direct))
        {
            return direct;
        }

        var sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", relative));
        return File.Exists(sibling) ? sibling : null;
    }

    private static async Task EnsureBridgeStartedAsync(CancellationToken token)
    {
        await StartGate.WaitAsync(token);
        try
        {
            var bridgePath = BridgePath.Value;
            if (bridgePath is null)
            {
                throw new FileNotFoundException("SC30 x86 bridge executable was not deployed.");
            }

            var startInfo = new ProcessStartInfo(bridgePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(bridgePath)!
            };
            startInfo.ArgumentList.Add("--pipe");
            startInfo.ArgumentList.Add(BridgePipeName.Value);
            startInfo.ArgumentList.Add("--parent-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Process.Start(startInfo);

            await Task.Delay(300, token);
        }
        finally
        {
            StartGate.Release();
        }
    }

    private static string CreateBridgePipeName()
    {
        var bridgePath = BridgePath.Value
            ?? Path.Combine(AppContext.BaseDirectory, "missing-sc30-bridge");
        var normalizedPath = Path.GetFullPath(bridgePath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)))[..16];
        return $"{Sc30BridgeProtocol.PipeName}.{hash}.{Environment.ProcessId}";
    }
}
