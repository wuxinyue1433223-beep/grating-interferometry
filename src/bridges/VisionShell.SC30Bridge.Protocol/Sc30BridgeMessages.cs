namespace VisionShell.SC30Bridge.Protocol;

public static class Sc30BridgeProtocol
{
    public const string PipeName = "VisionShell.SC30Bridge.v1";
}

public sealed record Sc30BridgeRequest(string Operation, string? DeviceId = null, bool? IsOpen = null);

public sealed record Sc30BridgeDevice(string DeviceId, string DisplayName);

public sealed record Sc30BridgeResponse(
    bool Success,
    string? Error = null,
    string? DeviceId = null,
    string? Name = null,
    bool? IsOpen = null,
    IReadOnlyList<Sc30BridgeDevice>? Devices = null);
