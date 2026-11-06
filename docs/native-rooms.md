# Salas hospedadas no próprio PC

Este é o modo padrão do Glimpse: **Hospedar no próprio PC**. Não usa Tailscale, serviço de salas hospedado, TURN, STUN ou conta em provedor. O próprio aplicativo recebe os convidados e distribui o vídeo. Os projetos de sinalização central anteriores permanecem no repositório como alternativa de desenvolvimento, sem serviço contratado ou publicado.

A tela inicial mostra **Hospedar sala** e **Entrar na sala de um amigo**. Cada opção abre um pop-up com os campos correspondentes. Os controles de compartilhar, parar e sair só aparecem depois de criar a sala ou concluir a entrada; ao sair, a tela volta às duas opções iniciais. O anfitrião pode abrir novamente o convite por **Ver convite da sala**.

Ao clicar em **Compartilhar tela**, um pop-up pergunta entre **Uma janela específica** e **Tela inteira**. Em seguida, confirme a fonte no seletor do Windows. Essas opções não ficam mais na lateral. Enquanto você apresenta, a área central mostra sua própria prévia, identificada como **SUA PRÉVIA**. A prévia usa os quadros BGRA já capturados, sem disputar o canal do codificador ou decodificar o próprio vídeo. Um bitmap é reutilizado e há apenas uma atualização pendente na interface. Ao parar, trocar de fonte ou perder a apresentação, quadros pendentes da captura antiga são descartados. Cancelar a escolha de uma nova fonte preserva um compartilhamento que já estava ativo.

A imagem é ajustada à área disponível no aplicativo, preservando a proporção e sem ampliar o layout para a resolução original da captura. Ao reduzir a janela, os painéis laterais são recolhidos para dar espaço ao vídeo. O botão **Tela cheia**, **F11** ou um duplo clique no vídeo abre a visualização ampliada. Use **Esc**, **F11**, duplo clique ou o botão de saída para voltar. A visualização ampliada usa o mesmo quadro e não altera a resolução enviada, a conexão ou o compartilhamento. A saída da sala fecha também a visualização ampliada.

## Pessoas e apresentação

A sala aceita até **8 pessoas: anfitrião + 7 convidados**. Todos usam o mesmo convite `GLN-...`. A saída de um convidado libera a vaga e mantém a sala disponível para outras entradas. A saída do anfitrião encerra a sala para todos.

**Qualquer participante pode compartilhar uma janela ou tela**, usando o seletor do Windows, sem aprovação do anfitrião. Há uma apresentação por vez: quem começa a compartilhar assume a apresentação, interrompendo a captura do apresentador anterior. Um convidado pode apresentar para o anfitrião e os demais convidados. Parar de compartilhar mantém a pessoa na sala e limpa a apresentação dos outros. Não há compartilhamentos simultâneos nesta versão.

## Hospedar pela internet

1. Mantenha **Hospedar no próprio PC** marcado. A porta TCP padrão é `45873`, editável antes de criar a sala.
2. Com **Tentar abrir a porta no roteador automaticamente** marcado, criar a sala usa a API UPnP nativa do Windows para tentar encaminhar essa porta e obter o IPv4 externo informado pelo roteador.
3. Se a abertura automática não estiver disponível, informe seu IPv4 público no aplicativo e encaminhe manualmente a mesma porta TCP externa e interna para o IPv4 local do seu PC, exibido no diagnóstico da sala.
4. Permita conexões de entrada do Glimpse nessa porta no Firewall do Windows. O aplicativo não altera regras do firewall automaticamente.
5. Crie a sala, envie o convite por um canal confiável e mantenha o PC ligado. Os convidados precisam apenas colar o convite e entrar; não precisam abrir portas no próprio roteador.

Se não houver endereço público configurado/descoberto, o aplicativo cria um convite de **rede local** e informa que a internet ainda não está configurada. Um endereço privado retornado pelo roteador indica possível NAT duplo ou CGNAT; não prova que a operadora seja a responsável. A aplicação não consulta sites de descoberta de IP. O encaminhamento no roteador não supera bloqueios de entrada da operadora. Hospedagem direta pode não funcionar nessas redes sem alterar a conexão com a operadora.

O diagnóstico de abertura da porta **não comprova acesso externo**. A validação final precisa de um convidado em outra rede. Usar o convite público dentro da mesma rede pode falhar se o roteador não suportar NAT loopback.

## Transporte e limites

Uma conexão TCP protegida por TLS 1.2/1.3 por convidado transporta controle e vídeo H.264. O convite contém IPv4, porta, impressão SHA-256 do certificado e chave aleatória de 256 bits. O cliente fixa o certificado pelo convite e o anfitrião confere a chave antes de aceitar a entrada. Não é um convite curto: ele contém os dados que um serviço central normalmente guardaria. Não publique o convite; quem o possui pode ocupar uma vaga e assumir a apresentação.

O anfitrião recebe a apresentação de um convidado e a distribui aos demais. Ele termina as conexões TLS, portanto é parte confiável da sala; não há criptografia adicional que esconda a mídia do anfitrião. Esta implementação usa IPv4 e não usa WebRTC no modo nativo. Os modos antigos de rede local e serviço central continuam usando WebRTC.

Cada conexão tem fila limitada, quadros limitados a 2 MB, verificação de tamanhos/tipos antes de alocar o corpo, tempo de autenticação de 15 segundos, envio de 15 segundos e leitura de 35 segundos, com mensagem de presença a cada 10 segundos. Um destinatário lento é desconectado para limitar atraso e memória, sem interromper os outros convidados. TCP pode aumentar a latência quando há perda de pacotes.

O upload do anfitrião cresce com o número de destinatários. Em 720p, aproximadamente 2,5 Mbps por vídeo, enviar para sete convidados pode exigir cerca de 17,5 Mbps de upload, mais o tráfego do protocolo. As opções vão de 360p a 1440p (2K), a 30 fps; veja [qualidades de vídeo](video-quality.md). O limite de oito é funcional; não é uma garantia de desempenho em qualquer PC/conexão. Áudio e múltiplas apresentações simultâneas continuam fora deste modo.

## Roteador e encerramento

A abertura automática usa `HNetCfg.NATUPnP`/`IStaticPortMappingCollection` do Windows. Não instala biblioteca de roteamento ou aplicativo auxiliar. O Glimpse tenta evitar substituir mapeamentos existentes, marca o mapeamento criado com um identificador próprio e tenta removê-lo ao sair da sala. Uma chamada COM que termine após o cancelamento também aciona limpeza do mapeamento resultante.

Essa API cria mapeamentos estáticos, sem prazo de expiração. Se o aplicativo/PC for encerrado abruptamente ou o roteador não responder à limpeza, o mapeamento pode permanecer e precisar ser removido na configuração do roteador. A descoberta/abertura/remoção UPnP em roteadores reais ainda não foi verificada; não há suporte a NAT-PMP/PCP nesta implementação.

Referência: [API de mapeamento UPnP do Windows](https://learn.microsoft.com/en-us/windows/win32/api/natupnp/nf-natupnp-istaticportmappingcollection-add).

## Testes

```powershell
dotnet run --project tests/Glimpse.NativeRooms/Glimpse.NativeRooms.csproj -c Release
```

Esse teste não usa FFmpeg nem captura a tela. Verifica convites, classificação de endereços privados/CGNAT, certificado/chave incorretos, dois espectadores simultâneos, distribuição de apresentação de convidado, troca de apresentador sem autorização, bloqueio de quadros do apresentador anterior, capacidade de oito pessoas, saída e nova entrada, e encerramento.

Com `GLIMPSE_FFMPEG` apontando para o FFmpeg do projeto, acrescente `-- --video` para testar codificação e reprodução reais de quadros artificiais. Em 05/11/2026, esse teste passou para apresentações do anfitrião e de convidado, com dois receptores decodificando em cada caso. Nenhuma tela foi capturada e nenhum roteador ou firewall foi alterado pelos testes.

Permanecem pendentes: teste de tela real com participantes, reprodução e troca de apresentador na interface, encaminhamento UPnP em roteador real e uso entre PCs em redes diferentes. A qualidade/latência com oito pessoas reais não foi medida.
