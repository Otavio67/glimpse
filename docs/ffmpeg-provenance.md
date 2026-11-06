# Componente de vídeo incluído

O pacote usa `ffmpeg.exe` sem modificações do build estático Windows x64 **9.0.2 essentials**, fornecido por Gyan Doshi. O programa é executado como processo separado. Nenhuma DLL FFmpeg é vinculada ao executável Glimpse.

- Distribuição original: [Gyan.dev](https://www.gyan.dev/ffmpeg/builds/).
- Arquivo original: [ffmpeg-9.0.2-essentials_build.zip](https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip).
- SHA-256 do ZIP: `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`.
- SHA-256 do executável: `3256173f3f8bffd7df12227c68adf68025edb1832273a9530688a7bb1ed8edec`.
- Código-fonte FFmpeg indicado pelo fornecedor: [commit 946fcce07b](https://github.com/FFmpeg/FFmpeg/commit/946fcce07b), com [download do fonte](https://github.com/FFmpeg/FFmpeg/archive/946fcce07b.tar.gz).
- O fornecedor identifica esse build como GPLv3. A íntegra da licença está em `LICENSE`; a configuração de compilação, as bibliotecas externas e suas versões estão no `README.txt` original, preservado nesta pasta. As referências acima devem acompanhar redistribuições deste componente.

O Glimpse não modifica esse binário. Os fontes, licenças e condições dos componentes externos permanecem os de seus respectivos autores, identificados no README do fornecedor. As bibliotecas .NET do Glimpse têm seus próprios avisos nas demais pastas de `licenses`.
