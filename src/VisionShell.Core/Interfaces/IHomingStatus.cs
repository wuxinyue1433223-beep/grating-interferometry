namespace VisionShell.Core.Interfaces;

/// <summary>
/// Exposes the controller's homing/reference state as reported by the official device SDK.
/// </summary>
public interface IHomingStatus
{
    bool IsHomed { get; }
}
