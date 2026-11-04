# Glimpse — milestone 1: LAN preview

A native Windows WPF/.NET 10 prototype for one-way sharing between two Glimpse instances. This is milestone 1, not the finished product. It uses the Windows picker, Windows.Graphics.Capture, H.264 via Media Foundation, and SIPSorcery WebRTC. The two peers exchange SDP text manually; there is no separate signaling server.

## Status and boundaries

The project compiles on .NET 10. A headless test passed with generated video on two WebRTC peers in the same machine: Media Foundation reported an H.264 hardware encoder, synthetic BGRA frames were encoded, transmitted, received, and decoded. No desktop or window was captured in that test. Capture through the picker, transmission between two physical PCs, window resize, and performance targets have **not** been verified. This is a development prototype, not a security-reviewed release.

The Windows picker lists displays and windows; only the selected item is captured. The encoder operates at 720p/30 fps, with a fixed 2.5 Mbps target. Frames are currently copied from GPU textures to CPU memory before FFmpeg; GPU encoding does not imply a zero-copy pipeline or guarantee low CPU usage. A resize requires stopping and selecting the source again. Minimized/protected content can be unavailable. No audio is captured.

## Project-local development setup

On Windows x64, the following installs .NET SDK 10.0.403 and Gyan.dev's FFmpeg 9.0.2 Essentials in `.tools/` inside this checkout. It checks the FFmpeg archive against a pinned SHA-256, and verifies the required encoder and filter. It does not modify the system PATH. Downloading the .NET installer and FFmpeg archive requires internet access.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Setup-Environment.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Dev.ps1 -Action Build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Dev.ps1 -Action Test
```

`Test` uses only generated frames, without launching the interface or capturing a screen. It first runs an in-process media test, then starts **two independent headless processes** on the same computer and exchanges SDP through temporary files to exercise WebRTC and decoding. It also checks whether Windows Graphics Capture is supported without starting a capture. It does not validate a live monitor/window or connectivity between physical computers.

When you are ready to open the app yourself, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Dev.ps1 -Action Run
```

The commands in the remaining sections describe the equivalent manual setup and testing with a separately installed SDK and FFmpeg. Project-local binaries are ignored by Git and must be installed separately on each computer. The bundled FFmpeg build is downloaded locally, is GPL-enabled, and is not committed to the repository.

## Prerequisites

- Windows 10 2004 (19041) or newer, x64, and a working Direct3D 11 device.
- .NET 10 SDK including the Windows Desktop build tools: https://dotnet.microsoft.com/download/dotnet/10.0
- An FFmpeg Windows build that contains `h264_mf`, the H.264 decoder and the `h264_metadata` bitstream filter: https://ffmpeg.org/download.html. Put `ffmpeg.exe` on `PATH` or beside the built Glimpse executable. FFmpeg is not bundled here; check the license and components of your chosen binary before distributing it. A GPL-enabled FFmpeg build was used for the headless test.
- Both computers on a network that permits local UDP traffic between them. VPNs and firewall restrictions can prevent WebRTC/ICE from connecting.

Check the encoder and filter using PowerShell:

```powershell
ffmpeg -hide_banner -h encoder=h264_mf
ffmpeg -hide_banner -h bsf=h264_metadata
```

## Build and run

From this folder in PowerShell:

```powershell
dotnet restore src/Glimpse/Glimpse.csproj
dotnet build src/Glimpse/Glimpse.csproj -c Release
dotnet run --project src/Glimpse/Glimpse.csproj -c Release
```

If Media Foundation reports a hardware H.264 MFT, the app requests FFmpeg's `h264_mf -hw_encoding 1`; if none is registered, it requests software. A registered but malfunctioning or incompatible hardware encoder produces an error rather than silently changing to software. The on-screen status indicates the selected mode. The driver must support the requested format and resolution.

## Manual two-computer test

1. Build and run Glimpse on **both** computers. Install the FFmpeg dependency on each.
2. On the host, click **Create offer**, copy the text, and send it privately to the guest.
3. The guest pastes it in the right-hand field and clicks **Create answer**. They send their generated text back privately.
4. The host pastes the answer and clicks **Apply answer**. Wait for `Connection: connected` on both sides.
5. The host clicks **Share screen** and selects a display or window in the Windows picker. The guest should see it. Only do this when you are ready to share the selected content.
6. **Stop sharing** stops capture while leaving the peer connection open. **Disconnect** closes the connection.

Exchange SDP only over a trusted channel: it includes network addresses and the peer fingerprints used for DTLS authentication. The prototype does not verify your friend's identity independently. Do not expose its SDP exchange to untrusted users. SIPSorcery supplies DTLS-SRTP for the media transport; no security audit has been performed.

## Headless test with generated frames — no screen capture

Set `GLIMPSE_FFMPEG` to the complete path of `ffmpeg.exe`, then run:

```powershell
$env:GLIMPSE_FFMPEG = 'C:\path\to\ffmpeg.exe'
dotnet run --project tests/Glimpse.Headless/Glimpse.Headless.csproj -c Release
```

It generates artificial BGRA frames in memory, encodes them, sends them over two WebRTC peers on the local machine, and decodes the received stream. It does not start the WPF window or Windows capture. Results depend on the installed codec driver and FFmpeg build. This test does not measure CPU use or validate a real screen/window.

## Project map

- `src/Glimpse/Capture/WindowsCaptureSource.cs`: system picker, WGC frame pool and D3D11 texture readback.
- `src/Glimpse/Encoding/HardwareEncoderProbe.cs`: Media Foundation hardware-MFT enumeration.
- `src/Glimpse/Encoding/H264Encoder.cs`, `H264Decoder.cs`, `AnnexBAccessUnits.cs`: FFmpeg process, H.264 access units and decoding.
- `src/Glimpse/Networking/LocalPeer.cs`: direct LAN WebRTC peer and manual offer/answer.
- `src/Glimpse/Views`, `ViewModels`: WPF presentation and session orchestration.
- `src/Glimpse/Resources`: English and pt-BR resource files for primary UI labels. English is the default; for resource testing only, set `GLIMPSE_UI_CULTURE=pt-BR` before starting. Some tooltips, technical errors and status messages remain English.
- `tests/Glimpse.Headless`: artificial-video integration test in one process.
- `tests/Glimpse.TwoProcess`: independent-process WebRTC test with generated frames and no screen capture.
- `scripts/`: project-local dependency setup and build/test/run commands.

Errors handled by the session are appended to `%LOCALAPPDATA%\Glimpse\errors.log`; there is no telemetry. Some exceptions can contain technical details such as file paths. Normal SDP values are not intentionally logged.

## Licensing

SharpDX uses the MIT license. FFmpeg licenses vary by build configuration, including LGPL and GPL variants; consult the chosen binary's license and corresponding source obligations. SIPSorcery 10.0.17's bundled license contains a **geographic use/distribution restriction** in addition to BSD terms; it is therefore not an unrestricted OSI-style open-source license. See the versioned package license at https://www.nuget.org/packages/SIPSorcery/10.0.17 and the project license at https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE.md. Review applicability before distributing. An alternative WebRTC library would be needed for a dependency set with unrestricted open-source licenses.

## Known limitations and next steps

- Actual Windows Graphics Capture end-to-end and two-PC LAN use need hands-on validation; no user screen was used during development tests.
- The one-to-one test uses manual SDP text. Rooms, links, automatic signaling, server expiry, multiple viewers, STUN/TURN settings, reconnect and adaptive bitrate are **not implemented** in this milestone.
- Quality selection, 1080p/60, stats overlay, measurement/optimization of CPU and memory, GPU texture-to-encoder transfer, light theme, comprehensive accessibility/localization and settings are **not implemented**.
- Protocol registration, single-instance link handling, a landing page, installer and portable distributable are **not implemented**.
- No signaling server project/API is supplied yet: the user asked to wait for confirmation before implementing signaling and rooms.
- Prioritize capture correctness, predictable disposal, authenticated signaling, congestion handling, and performance profiling on multiple GPUs before calling this production-ready.
