# Glimpse — development preview

## Instalação no Windows

**[⬇ Baixar Glimpse completo para Windows x64](https://github.com/Otavio67/glimpse/releases/download/v0.2.10-preview/Glimpse-Windows-x64.zip)**

Extraia o ZIP inteiro e dê dois cliques em **Abrir Glimpse.cmd**. O pacote já inclui o aplicativo, runtime .NET, bibliotecas e FFmpeg. Para criar atalhos na Área de Trabalho e no menu Iniciar, execute **Instalar Glimpse.cmd**. Não há downloads adicionais de dependências nesses dois caminhos. Requer Windows 10 2004 ou mais recente, x64. O download é público.

Ao hospedar, escolha **Copiar link da sala** e envie ao seu amigo. Quem já executou **Instalar Glimpse.cmd** pode abrir o Glimpse direto do link. Também é possível colar o código de 16 caracteres na tela **Entrar na sala de um amigo**. O Glimpse tenta IPv4 e IPv6, negocia uma rota UDP direta por ICE e recorre ao retransmissor gratuito se as rotas diretas falharem. Veja [salas nativas](docs/native-rooms.md).

Na aba **Gravação**, escolha **Iniciar gravação** e selecione uma tela ou janela no seletor do Windows. **Parar e salvar** finaliza o MP4 em **Vídeos → Glimpse**. Use **Abrir pasta** ou **Abrir último vídeo** para acessar a gravação. A gravação é local, em até 1080p a 30 fps, sem áudio, e funciona sem entrar numa sala.

Se baixar o código por **Code → Download ZIP**, execute o mesmo **Instalar Glimpse.cmd** depois de extrair. O instalador baixa também as ferramentas para compilar a aplicação automaticamente. Mais detalhes e requisitos em [instalação](docs/installation.md).

No primeiro uso, escolha seu nome e uma foto: há quatro avatares de robôs ou importação de foto do computador. O perfil fica salvo neste usuário do Windows e pode ser alterado pelo cartão **Editar perfil**, no canto inferior esquerdo. Veja [perfil local](docs/profile.md).

A native Windows WPF/.NET 10 prototype for two-way sharing between two Glimpse instances. It uses the Windows picker, Windows.Graphics.Capture, H.264 via Media Foundation, and SIPSorcery WebRTC. This is a development preview, not the finished product. The original direct LAN mode exchanges SDP through an encrypted connection to the host; its `GLM-` code carries the host address, certificate fingerprint and secret.

**The default is now native PC hosting:** the app creates a `GLN-...` invitation, accepts up to eight people, and sends video over pinned TLS/TCP without a hosted signaling service, STUN/TURN or VPN. Any participant may present; a new presentation replaces the current one. The app can attempt Windows UPnP router configuration or use a manually configured public IPv4/port. **Router mapping and connectivity between separate networks are not yet verified; CGNAT can prevent direct hosting.** See [native room setup and validation](docs/native-rooms.md). Uncheck **Hospedar no próprio PC** for the original two-person LAN/WebRTC mode below. The earlier central-service prototype remains documented [here](docs/internet-rooms.md) and is not deployed or selected by default.

## Status and boundaries

The project compiles on .NET 10. A headless test passed with generated video on two WebRTC peers in the same machine: Media Foundation reported an H.264 hardware encoder, synthetic BGRA frames were encoded, transmitted, received, and decoded. No desktop or window was captured in that test. Capture through the picker, transmission between two physical PCs, window resize, and performance targets have **not** been verified. This is a development prototype, not a security-reviewed release.

The Windows picker lists displays and windows; only the selected item is captured. The sharing dialog offers 360p, 480p, 720p, 1080p and 1440p (2K), all at 30 fps; 720p remains the default. Encoder targets range from 0.9 to 12 Mbps per destination, and receivers preserve the selected encoded resolution. See [video quality](docs/video-quality.md). Frames are currently copied from GPU textures to CPU memory before FFmpeg; GPU encoding does not imply a zero-copy pipeline or guarantee low CPU usage. A resize requires stopping and selecting the source again. Minimized/protected content can be unavailable. No audio is captured.

## Project-local development setup

On Windows x64, the following installs .NET SDK 10.0.403 and Gyan.dev's FFmpeg 9.0.2 Essentials in `.tools/` inside this checkout. It checks the FFmpeg archive against a pinned SHA-256, and verifies the required encoder and filter. It does not modify the system PATH. Downloading the .NET installer and FFmpeg archive requires internet access.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Setup-Environment.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Dev.ps1 -Action Build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Dev.ps1 -Action Test
```

`Test` uses only generated frames, without launching the interface or capturing a screen. It first runs an in-process media test, then starts **two independent headless processes** on the same computer and exchanges a direct room code through temporary files to exercise negotiation, WebRTC and decoding in both directions. It also checks whether Windows Graphics Capture is supported without starting a capture. It does not validate a live monitor/window or connectivity between physical computers.

When you are ready to open the app yourself, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Dev.ps1 -Action Run
```

The commands in the remaining sections describe the equivalent manual setup and testing with a separately installed SDK and FFmpeg. Project-local binaries are ignored by Git and must be installed separately on each computer. The bundled FFmpeg build is downloaded locally, is GPL-enabled, and is not committed to the repository.

## Prerequisites

- Windows 10 2004 (19041) or newer, x64, and a working Direct3D 11 device.
- .NET 10 SDK including the Windows Desktop build tools: https://dotnet.microsoft.com/download/dotnet/10.0
- An FFmpeg Windows build that contains `h264_mf`, the H.264 decoder and the `h264_metadata` bitstream filter: https://ffmpeg.org/download.html. Put `ffmpeg.exe` on `PATH` or beside the built Glimpse executable. The ready-to-run release includes FFmpeg; see docs/ffmpeg-provenance.md for its origin and license. A GPL-enabled FFmpeg build was used for the headless test.
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

## Teste de sala entre dois computadores

1. Os dois computadores precisam estar na mesma rede local e permitir conexões TCP e UDP no firewall.
2. No computador anfitrião, clique em **Criar sala** e envie o **código da sala** ao seu amigo por um canal confiável.
3. O amigo cola o código no campo e clica em **Entrar em uma sala existente**. Aguarde a conexão.
4. Qualquer uma das duas pessoas pode escolher **Uma janela específica** ou **Tela inteira** e selecionar a fonte no seletor do Windows. A tela inteira mostra as janelas visíveis naquele monitor.
5. **Parar compartilhamento** mantém a conexão; **Sair da sala** encerra a sessão.

A sala direta aceita um convidado; ambos podem transmitir, funciona em redes que permitem conexão direta entre os computadores e não possui servidor intermediário. O código é longo porque contém o endereço local e credenciais criptográficas. Ele não funciona automaticamente entre redes diferentes, especialmente com NAT ou firewalls. Um servidor público de sinalização e, em redes restritas, um servidor TURN ainda precisam ser configurados. A troca do código deve ser feita em um canal confiável; quem o possui pode tentar entrar na sala. O código atual é de uso único: crie outra sala se precisar conectar novamente.

## Room connection regression checks

The network-only regression runner requires .NET 10 but does not require FFmpeg, open the app, or capture a screen:

```powershell
dotnet run --project tests/Glimpse.Rooms/Glimpse.Rooms.csproj -c Release
```

It checks malformed codes, certificate pin mismatch, incorrect room secrets, a stalled TCP client followed by a valid guest using the same code, WebRTC establishment on loopback, and cancellation. Unauthenticated attempts are limited to 15 seconds and leave the room available. Once an authenticated guest begins negotiation, the room remains single-use; a failed negotiation requires a new room. Joining has a 40-second signaling limit, and the interface gives WebRTC 30 seconds per connection/recovery state before ending the session. Late connection and decoded-frame callbacks from a previous session are ignored.

These checks do not validate connectivity between physical PCs or separate networks. Internet signaling, short codes, TURN relay, and automatic room rejoining remain unimplemented.

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
- `src/Glimpse/Networking/LocalPeer.cs` and `DirectRoom.cs`: WebRTC peer and encrypted direct LAN room negotiation.
- `src/Glimpse/Views`, `ViewModels`: WPF presentation and session orchestration.
- `src/Glimpse/Resources`: English and pt-BR resource files for primary UI labels. English is the default; for resource testing only, set `GLIMPSE_UI_CULTURE=pt-BR` before starting. Some tooltips, technical errors and status messages remain English.
- `tests/Glimpse.Headless`: artificial-video integration test in one process.
- `tests/Glimpse.TwoProcess`: independent-process WebRTC test with generated frames and no screen capture.
- `scripts/`: project-local dependency setup and build/test/run commands.

Errors handled by the session are appended to `%LOCALAPPDATA%\Glimpse\errors.log`; there is no telemetry. Some exceptions can contain technical details such as file paths. Normal SDP values are not intentionally logged.

## Licensing

SharpDX uses the MIT license. FFmpeg licenses vary by build configuration, including LGPL and GPL variants; consult the chosen binary's license and corresponding source obligations. SIPSorcery 10.0.17's bundled license contains a **geographic use/distribution restriction** in addition to BSD terms; it is therefore not an unrestricted OSI-style open-source license. See the versioned package license at https://www.nuget.org/packages/SIPSorcery/10.0.17 and the project license at https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE.md. Review applicability before distributing. An alternative WebRTC library would be needed for a dependency set with unrestricted open-source licenses.

## Known limitations and next steps

- Actual Windows Graphics Capture end-to-end and two-PC LAN use need hands-on validation; no user screen was used during development tests. The last received frame may remain visible after the other participant stops sharing.
- Native PC rooms accept seven guests and allow any participant to present. Real-router and different-network validation remains pending. The legacy LAN/WebRTC and central-service prototypes still accept only one guest. ICE restart/rejoining in those older modes and adaptive bitrate are **not implemented**.
- 60 fps, adaptive bitrate, stats overlay, measurement/optimization of CPU and memory, GPU texture-to-encoder transfer, light theme and comprehensive accessibility/localization are **not implemented**. Quality selection through 1440p, local profiles and per-user installation are available.
- Protocol registration, single-instance link handling and a landing page are **not implemented**. A per-user Windows installer and self-contained release ZIP are now provided; the first install downloads the pinned FFmpeg build.
- No publicly deployed signaling server or relay is included yet; these are needed for convenient use between separate networks.
- Prioritize capture correctness, predictable disposal, authenticated signaling, congestion handling, and performance profiling on multiple GPUs before calling this production-ready.

## GPU encoder work in progress

A two-minute synthetic baseline and an isolated GPU/MFT feasibility probe are saved under `docs/perf/`, `benchmarks/Glimpse.Baseline`, and `tests/Glimpse.GpuProbe`. See `docs/milestone-1-progress.md` for the plan, MIT-licensed test dependencies, results, risks, and unimplemented work. **The production capture, encoder, and decoder are not replaced yet and still use FFmpeg.** No before/after end-to-end performance comparison or new distributable is available.
