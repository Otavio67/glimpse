# Qualidade do compartilhamento

No pop-up de **Compartilhar tela**, escolha a qualidade antes de selecionar **Uma janela específica** ou **Tela inteira**. A última qualidade efetivamente usada fica salva neste usuário do Windows. Para trocar durante a apresentação, clique em Compartilhar tela novamente, escolha a nova qualidade e confirme a fonte. Cancelar preserva a apresentação anterior.

| Qualidade | Resolução enviada | Taxa alvo por destino |
| --- | --- | --- |
| 360p | 640 × 360 | 0,9 Mbps |
| 480p | 854 × 480 | 1,5 Mbps |
| 720p (padrão) | 1280 × 720 | 2,5 Mbps |
| 1080p | 1920 × 1080 | 6 Mbps |
| 1440p (2K) | 2560 × 1440 | 12 Mbps |

Todas usam 30 fps nesta versão. O enquadramento é preservado com faixas quando a fonte tem outra proporção. Selecionar uma resolução maior do que a fonte não cria detalhes que não existiam. A prévia local continua usando os quadros originais da captura; o seletor controla a qualidade enviada aos convidados.

O recebimento usa as dimensões reais do vídeo H.264 decodificado, sem a antiga redução fixa para 720p. A interface mostra a qualidade de envio durante a própria apresentação e a altura recebida quando está assistindo outra pessoa. O encaixe na janela e a tela cheia são modos de visualização, sem alterar essa qualidade.

As taxas são alvos do codificador, não garantia de uso constante da conexão. Mais resolução exige mais processamento e upload. Como o anfitrião distribui a apresentação aos demais, enviar 2K a sete convidados pode exigir aproximadamente 84 Mbps de upload, além dos cabeçalhos. Não há adaptação automática de bitrate ou 60 fps. Alguns drivers podem recusar uma resolução; o aplicativo informa a falha e sugere escolher uma qualidade menor, sem reduzir silenciosamente a resolução.

## Validação

`tests/Glimpse.Quality` transmite vídeo artificial por uma sala TLS para dois convidados. Verifica, em cada uma das cinco qualidades, que ambos decodificam a resolução correta, a orientação das linhas, as cores e o canal alfa. Em 05/11/2026, as cinco opções passaram neste PC com codificação de hardware, incluindo 2560 × 1440 nos dois receptores. Isso não mede desempenho com oito PCs reais nem valida upload por redes diferentes.

O decodificador usa BMP BGRA com dimensões por quadro no pipe local do FFmpeg e valida os cabeçalhos/tamanhos antes de alocar os pixels. Os quadros enviados pela rede continuam sendo H.264; BMP não é enviado aos participantes. O limite recebido é 2560 × 1440 nesta versão.

```powershell
dotnet run --project tests/Glimpse.Quality/Glimpse.Quality.csproj -c Release
```

Defina `GLIMPSE_FFMPEG` com o caminho do FFmpeg para executar esse teste. O comando `scripts/Dev.ps1 -Action Test` também o inclui.
