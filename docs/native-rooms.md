# Salas hospedadas no próprio PC

Este é o modo padrão do Glimpse: **Hospedar no próprio PC**. O anfitrião recebe os convidados e distribui o vídeo. Com o retransmissor configurado no pacote, o mesmo convite conecta PCs em redes diferentes mesmo quando o roteador bloqueia as rotas diretas. A conexão inteira é feita pelo Glimpse.

A tela inicial mostra **Hospedar sala** e **Entrar na sala de um amigo**. Cada opção abre um pop-up com os campos correspondentes. Os controles de compartilhar, parar e sair só aparecem depois de criar a sala ou concluir a entrada; ao sair, a tela volta às duas opções iniciais. O anfitrião pode abrir novamente o convite por **Ver convite da sala**.

Ao clicar em **Compartilhar tela**, um pop-up pergunta entre **Uma janela específica** e **Tela inteira**. Em seguida, confirme a fonte no seletor do Windows. Essas opções não ficam mais na lateral. Enquanto você apresenta, a área central mostra sua própria prévia, identificada como **SUA PRÉVIA**. A prévia usa os quadros BGRA já capturados, sem disputar o canal do codificador ou decodificar o próprio vídeo. Um bitmap é reutilizado e há apenas uma atualização pendente na interface. Ao parar, trocar de fonte ou perder a apresentação, quadros pendentes da captura antiga são descartados. Cancelar a escolha de uma nova fonte preserva um compartilhamento que já estava ativo.

A imagem é ajustada à área disponível no aplicativo, preservando a proporção e sem ampliar o layout para a resolução original da captura. Ao reduzir a janela, os painéis laterais são recolhidos para dar espaço ao vídeo. O botão **Tela cheia**, **F11** ou um duplo clique no vídeo abre a visualização ampliada. Use **Esc**, **F11**, duplo clique ou o botão de saída para voltar. A visualização ampliada usa o mesmo quadro e não altera a resolução enviada, a conexão ou o compartilhamento. A saída da sala fecha também a visualização ampliada.

## Pessoas e apresentação

A sala aceita até **8 pessoas: anfitrião + 7 convidados**. Todos usam o mesmo convite `GLN-...`. A saída de um convidado libera a vaga e mantém a sala disponível para outras entradas. A saída do anfitrião encerra a sala para todos.

Cada sala nova gera um certificado e uma chave novos. Com o serviço disponível, o anfitrião vê um código de 16 caracteres no lugar do convite completo. O código vale enquanto a sala está aberta. Convites completos anteriores continuam aceitos e são usados quando o serviço de convites curtos está indisponível.

O anfitrião usa **Copiar link da sala**. O convidado abre o link no navegador e clica em **Abrir Glimpse**. Para o Windows reconhecer o link, execute **Instalar Glimpse.cmd** uma vez. Na versão portátil, cole o link ou o código em **Entrar na sala de um amigo**. O site abre o aplicativo. O convite fica no fragmento do endereço e o navegador não recebe o vídeo.

**Qualquer participante pode compartilhar uma janela ou tela**, usando o seletor do Windows, sem aprovação do anfitrião. Há uma apresentação por vez: quem começa a compartilhar assume a apresentação, interrompendo a captura do apresentador anterior. Um convidado pode apresentar para o anfitrião e os demais convidados. Parar de compartilhar mantém a pessoa na sala e limpa a apresentação dos outros. Não há compartilhamentos simultâneos nesta versão.

## Hospedar pela internet

1. Mantenha **Hospedar no próprio PC** marcado. A porta TCP padrão é `45873`, editável antes de criar a sala.
2. Com **Tentar abrir a porta no roteador automaticamente** marcado, criar a sala usa a API UPnP nativa do Windows para tentar encaminhar essa porta e obter o IPv4 externo informado pelo roteador.
3. Se a abertura automática não estiver disponível, informe seu IPv4 público no aplicativo e encaminhe manualmente a mesma porta TCP externa e interna para o IPv4 local do seu PC, exibido no diagnóstico da sala.
4. Permita conexões de entrada do Glimpse nessa porta no Firewall do Windows. O aplicativo não altera regras do firewall automaticamente.
5. Crie a sala, envie o convite por um canal confiável e mantenha o PC ligado. Os convidados precisam apenas colar o convite e entrar; não precisam abrir portas no próprio roteador.

Se o Windows tiver uma rota com IPv6 global, o Glimpse inclui esse endereço no convite. Quando as rotas TCP diretas falham, os dois aplicativos também podem negociar uma rota UDP direta via ICE, com descoberta de endereços pelo STUN público da Cloudflare. Use a versão atual em ambos os PCs para o código curto e o ICE.

Quando todas as rotas diretas falham, um convite com rota reserva tenta o retransmissor. Um endereço privado retornado pelo roteador pode indicar NAT duplo ou CGNAT. O retransmissor usa conexões de saída por HTTPS/WebSocket dos dois PCs, portanto o anfitrião pode aceitar convidados mesmo sem encaminhamento de porta.

O diagnóstico de abertura da porta **não comprova acesso externo**. A validação final precisa de um convidado em outra rede. Usar o convite público dentro da mesma rede pode falhar se o roteador não suportar NAT loopback.

## Rota reserva

Ao criar a sala, o Glimpse abre uma conexão de espera com o retransmissor, quando disponível no pacote. Só passam dados da sala por essa conexão quando um convidado esgota as rotas diretas e recorre à reserva. O servidor apenas encaminha os bytes TLS entre o convidado e o anfitrião. O convidado confere a impressão do certificado do anfitrião antes de enviar a chave da sala. A criptografia do vídeo termina nos dois Glimpses.

O serviço de convite curto guarda apenas o convite criptografado enquanto o anfitrião está conectado. O código no fragmento da URL permite abrir e verificar o convite. Na tentativa de conexão ICE, o serviço encaminha apenas a sinalização criptografada. Se a negociação funcionar, o vídeo segue diretamente entre os PCs.

O servidor é um Cloudflare Worker com Durable Objects, em `relay/`. O endereço oficial já vem no Glimpse, inclusive quando compilado a partir do código-fonte. Para usar outro serviço em ambos os PCs, informe `-RelayUrl wss://<servidor>` a `scripts/Build-Package.ps1` ou configure `GLIMPSE_RELAY_URL`. O teste `Glimpse.NativeRooms --ice --video` resolve um convite curto, força uma rota TCP direta indisponível e exercita a entrada e a transmissão por ICE. O [plano gratuito](https://developers.cloudflare.com/durable-objects/platform/pricing/) tem cotas e pode recusar novas conexões quando são atingidas.

## Usar a mesma rede Radmin ou Hamachi

1. Conectem os dois PCs à mesma rede virtual e confiram se aparecem um para o outro nela.
2. No PC anfitrião, abra **Hospedar sala → Configurações da conexão** e preencha **IPv4 de rede compartilhada** com o IPv4 do próprio anfitrião mostrado no Radmin ou Hamachi. Deixe **IPv4 público** em branco.
3. Crie a sala e envie o novo convite. O convidado cola o código no Glimpse. A sala escuta no adaptador escolhido e não tenta abrir portas no roteador. O Firewall do Windows do anfitrião precisa permitir a entrada do Glimpse pela rede usada.

## Transporte e limites

Uma conexão TCP protegida por TLS 1.2/1.3 por convidado transporta controle e vídeo H.264. O convite contém IPv4, porta, impressão SHA-256 do certificado, chave aleatória de 256 bits e, quando disponível, IPv6. O cliente fixa o certificado pelo convite e o anfitrião confere a chave antes de aceitar a entrada. Envie o convite somente a quem deve entrar. Quem o possui pode ocupar uma vaga e assumir a apresentação.

O anfitrião recebe a apresentação de um convidado e a distribui aos demais. Ele termina as conexões TLS, portanto é parte confiável da sala; não há criptografia adicional que esconda a mídia do anfitrião. Os modos antigos de rede local e serviço central continuam usando WebRTC.

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
