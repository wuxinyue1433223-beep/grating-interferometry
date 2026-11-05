// 文件职责：相机能力抽象：定义采集、抓拍、曝光和帧事件契约。

namespace VisionShell.Core.Interfaces;

/// <summary>
/// Interface for a camera device.
/// </summary>
// 类型职责：相机接口，规定采集/抓拍能力和帧事件。
public interface ICamera : IDevice
{
    /// <summary>
    /// Raised when a new image frame is available.
    /// Payload is raw image data or a platform-specific image object.
    /// </summary>
    event EventHandler<ImageFrame> FrameReceived;

    Task StartCaptureAsync();
    Task StopCaptureAsync();
    
    /// <summary>
    /// Trigger a single frame capture.
    /// </summary>
    Task SnapAsync();

    /// <summary>
    /// Gets or sets the exposure time in microseconds.
    /// Changed from int to double to support fractional values.
    /// </summary>
    double ExposureTimeUs { get; set; }
}

// 类型职责：图像帧数据模型，封装像素数据与尺寸信息。
public class ImageFrame : EventArgs
{
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime Timestamp { get; set; }
    // Could add format info (Mono8, RGB24, etc.)
}
