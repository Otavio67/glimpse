# Milestone 1 — in progress

## Plan and scope

Replace the current capture/readback/FFmpeg encoding path in `Capture/WindowsCaptureSource.cs`, `Encoding/H264Encoder.cs`, and the orchestration in `ViewModels/MainViewModel.cs`. Use Windows.Graphics.Capture, D3D11 video processing, `IMFDXGIDeviceManager`, asynchronous Media Foundation hardware MFT events and tracked GPU sample lifetimes. Add synthetic and resize tests and a repeatable 720p30 benchmark. Keep the existing network and playback behavior during this milestone. The FFmpeg decoder is a separate next milestone, so removing FFmpeg from the entire application cannot be claimed until that decoder is replaced too.

Main risks: different encoder drivers support different formats and codec properties; async sample ownership and shutdown must be correct; frame pool resize must avoid use-after-free; video color conversion and aspect ratio must be correct; and an encoder-only benchmark is not a complete streaming benchmark.

## Saved progress

- `benchmarks/Glimpse.Baseline` and `scripts/Benchmark-Baseline.ps1` measure the current 720p30 synthetic BGRA → FFmpeg/Media Foundation encoding path. See `docs/perf/baseline-synthetic-720p30.md` and CSV for the 120-second result and its limitations.
- `tests/Glimpse.GpuProbe` is a **standalone experiment**, not connected to the application. It creates a synthetic BGRA GPU texture, converts it to NV12 using D3D11 Video Processor, submits a GPU surface to an async H.264 MFT using a shared D3D11 device manager, and reads compressed samples. It does not capture the desktop or start FFmpeg in its own code. It passed locally for NVIDIA H.264 and Microsoft AVC DX12 MFTs. The output began with Annex-B start codes and tracked-sample callbacks occurred. It is a small functional probe, not an assertion that no child process can ever be started, a full zero-copy production implementation, a quality test, or a performance benchmark.
- The existing app remains unchanged and still requires FFmpeg for encoding and decoding. The GPU prototype is not a new release and should not be distributed as one.

## APIs and dependencies

- Windows.Graphics.Capture, Direct3D 11 and Media Foundation MFT/IMFDXGIDeviceManager: Windows system APIs.
- `Vortice.Direct3D11` 3.8.3 and `Vortice.MediaFoundation` 3.8.3: MIT; used only by the separate probe, with user approval. Their transitive dependencies include Vortice.DXGI/Vortice.DirectX and SharpGen.Runtime (MIT; the package currently resolves SharpGen.Runtime 2.4.2-beta). See the NuGet package metadata before updating versions.
- The production app still depends on SharpDX, FFmpeg and SIPSorcery, with the restrictions described in the main README. No distribution has been made during this work.

## Remaining work before milestone 1 is complete

- Integrate capture and encoder without CPU texture readback; replace SharpDX in the production path; implement bounded GPU texture pool with reliable tracked-sample release and correct callback/disposal order.
- Check and set the requested `ICodecAPI` properties; provide explicit software-fallback consent and report a selected hardware encoder's failure rather than silently falling back.
- Implement cancellation, drain, source resize/minimize/close, cursor setting, Windows border permissions and production-grade event handling. The current probe uses blocking `GetEvent` and reuses a static texture; it is not safe as a capture pipeline.
- Normalize/validate Annex-B or AVCC against the packetizer, test no FFmpeg child on the encoder path, test dynamic resize and verify no access to user screens in automated tests.
- Run an equivalent two-minute after benchmark on the same machine. No after result or end-to-end streaming CPU/GPU comparison has been measured yet; the saved baseline excludes WGC and networking.
- Manually verify capture and streaming with explicit user involvement; retain the existing functional path until the replacement is tested. Then update the main README and present the complete diff and results before asking to start milestone 2.
