// 文件职责：MVSDK 相机仿真实现，用于无硬件联调。

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VisionShell.Core.Interfaces;

namespace VisionShell.Plugins.MVSDKCamera
{
    /// <summary>
    /// Simulated MVSDK camera for development without hardware.
    /// Generates synthetic grayscale frames.
    /// </summary>
    // 类型职责：模拟 MVSDK 相机，用于替代真实硬件测试流程。
    public class SimulatedMVSDKCamera : ICamera
    {
        private readonly ILogger _logger;
        private bool _capturing;
        private CancellationTokenSource? _cts;
        private int _frameCounter;

        public SimulatedMVSDKCamera(ILogger logger, string? name = null, string? id = null)
        {
            _logger = logger;
            DeviceId = id ?? $"mvsdk-sim-{Guid.NewGuid():N}";
            Name = name ?? "MVSDK Camera (Simulated)";
            State = DeviceState.Disconnected;
        }

        public string DeviceId { get; }
        public string Name { get; }
        public bool IsCloneable => true;
        public DeviceState State { get; private set; }
        public double ExposureTimeUs { get; set; } = 8000.0;

        public event EventHandler<DeviceState>? StateChanged;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<ImageFrame>? FrameReceived;

        public async Task ConnectAsync(CancellationToken token = default)
        {
            try
            {
                _logger.LogInformation("Connecting simulated MVSDK camera...");
                await Task.Delay(200, token);
                State = DeviceState.Connected;
                StateChanged?.Invoke(this, State);
                _logger.LogInformation("Simulated MVSDK camera connected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Simulated camera connect failed");
                State = DeviceState.Error;
                StateChanged?.Invoke(this, State);
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }

        public async Task DisconnectAsync()
        {
            await StopCaptureAsync();
            State = DeviceState.Disconnected;
            StateChanged?.Invoke(this, State);
            _logger.LogInformation("Simulated MVSDK camera disconnected.");
        }

        public Task StartCaptureAsync()
        {
            if (State != DeviceState.Connected)
            {
                _logger.LogWarning("Cannot start capture: simulated camera not connected.");
                return Task.CompletedTask;
            }

            if (_capturing) return Task.CompletedTask;
            _capturing = true;
            _cts = new CancellationTokenSource();
            _ = Task.Run(async () => await CaptureLoop(_cts.Token));
            _logger.LogInformation("Simulated MVSDK camera capture started.");
            return Task.CompletedTask;
        }

        public Task StopCaptureAsync()
        {
            _capturing = false;
            _cts?.Cancel();
            _cts = null;
            _logger.LogInformation("Simulated MVSDK camera capture stopped.");
            return Task.CompletedTask;
        }

        public Task SnapAsync()
        {
            if (State != DeviceState.Connected)
            {
                _logger.LogWarning("Cannot snap: simulated camera not connected.");
                return Task.CompletedTask;
            }

            EmitFrame();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try { StopCaptureAsync(); } catch { }
            State = DeviceState.Disconnected;
        }

        private async Task CaptureLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _capturing)
            {
                EmitFrame();
                try { await Task.Delay(100, token); } catch { break; }
            }
        }

        private void EmitFrame()
        {
            const int width = 640;
            const int height = 480;
            var data = new byte[width * height];
            var t = (byte)(DateTime.UtcNow.Millisecond / 4);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // simple moving gradient pattern
                    data[y * width + x] = (byte)((x + y + t) & 0xFF);
                }
            }

            _frameCounter++;
            if (_frameCounter % 30 == 0)
            {
                // no special pattern diagnostics
            }

            FrameReceived?.Invoke(this, new ImageFrame
            {
                Width = width,
                Height = height,
                Timestamp = DateTime.UtcNow,
                Data = data
            });
        }
    }
}
