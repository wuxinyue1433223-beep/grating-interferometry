# x64 Hardware Migration

## Confirmed SDK Facts

- `D:\XA\SDK\.NET Framework (C#)\Libraries\x64\tlmc_xa_dotnet.dll` is x64. The matching Win32 DLL is x86.
- `C:\Program Files\Thorlabs\Kinesis\` contains the x64 Kinesis runtime. The installed x64 version is 1.14.58.26351.
- The local SC30 TL_DeviceSDK guide defines `OpenComPort`, `WriteComPort`, and `CloseComPort` as virtual COM-port APIs. The locally installed `TL_DeviceSDK.dll` is x86 only.
- XA sample code exposes device-list discovery and immediate stop. Kinesis continues to be used for controllers that XA does not enumerate or support.

## Runtime Layout

- The WPF host is `win-x64`.
- XA and Legacy Kinesis stay in the x64 host process.
- SC30 uses the x64 `thorlabs-sc30-shutter` plugin, which starts the self-contained x86 `VisionShell.SC30Bridge.exe` on demand.
- The bridge loads the existing x86 TL_DeviceSDK, SC30 plugin DLL, and XML configuration; host and bridge communicate through the local named pipe `VisionShell.SC30Bridge.v1`.

## Device Binding Policy

- `Actuator1` and `Actuator2` prefer discovered XA descriptors.
- `VerticalStage` and `HorizontalStage` retain the Legacy Kinesis KVS/BDC preference.
- Confirm every role against its physical serial number in the device-mapping UI before enabling motion. Do not rely on first-device fallback for an instrument deployment.

## Safe Rollout

1. Start with only the SC30 connected. Confirm discovery, open, close, and bridge restart.
2. Connect both XA-supported actuators. Confirm serial mapping, direction, range, and immediate stop independently.
3. Connect each Kinesis stage one at a time. Confirm homing and physical soft limits before restoring saved targets.
4. Run all four axes with camera acquisition disabled, then repeat under normal image load.
5. Only after the above passes, retire old x86 host release artifacts.

## Temporary Compatibility Logic

- Legacy actuator negative-range normalization and negative absolute-move fallback remain enabled. They protect previously observed controller configuration inconsistencies.
- Remove each fallback only after recording the controller model, serial number, home reference, physical travel range, and successful cold-start regression result.
- Immediate stop now bypasses the normal per-axis command gate for both XA and Legacy Kinesis. Ordinary moves remain serialized per axis.
