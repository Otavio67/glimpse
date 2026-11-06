# Salas pela internet

Este documento descreve o protótipo alternativo de salas com serviço central, anterior ao modo de hospedagem no próprio PC. Ele não está selecionado na interface atual. **Nenhum serviço público ou retransmissor foi publicado.** O modo padrão atual é documentado em [salas nativas](native-rooms.md).

## Como usar depois da publicação

1. Configure o mesmo endereço HTTPS do serviço nos dois aplicativos, no campo **Serviço de salas**. O endereço é salvo neste computador após uma criação/entrada bem-sucedida. A variável `GLIMPSE_SIGNALING_URL` também define esse endereço ao iniciar.
2. O anfitrião cria a sala e envia o código `GLI-XXXX-XXXX-XXXX` ao convidado por um canal confiável.
3. O convidado entra pelo código. Ambos podem compartilhar a tela depois da conexão.
4. Sair da sala encerra a sessão para os dois. Crie outra sala para conectar novamente.

O código de convite permite ocupar a única vaga de convidado. Não publique esse código. As sessões têm duração máxima de duas horas; convites sem convidado expiram em dez minutos. Um cliente sem contato com o serviço por 90 segundos faz a sala expirar. O cliente mantém contato durante a sessão e tenta novamente em falhas transitórias de transporte ou erros HTTP 5xx. Uma falha na criação/entrada não é repetida automaticamente, pois a vaga pode já ter sido reservada.

## Serviço central

Projeto: `src/Glimpse.Signaling`. ASP.NET Core/.NET 10, sem banco de dados ou dependências externas. O servidor mantém as salas em memória, limita a 1000 salas simultâneas, usa transições atômicas e gera códigos aleatórios com 60 bits de entropia. Host e convidado recebem tokens distintos de 256 bits; apenas o hash desses tokens fica armazenado. Publicar oferta/resposta exige o token e a função correta. Publicações idênticas podem ser repetidas sem iniciar outra negociação.

Use **uma instância**. Reiniciar ou substituir o processo encerra suas salas. Várias réplicas exigem um armazenamento compartilhado e limites de tráfego distribuídos, ainda não implementados.

O serviço só armazena os textos de negociação, tempos e credenciais de acesso à sala; não recebe os quadros de vídeo. A sinalização usa HTTPS e confia no operador do serviço. A mídia usa a criptografia WebRTC, também quando atravessa o TURN. Não há verificação adicional de identidade dos participantes, contas ou aprovação de entrada pelo anfitrião nesta etapa.

Variáveis do servidor:

| Variável | Uso |
| --- | --- |
| `GLIMPSE_STUN_URLS` | URLs STUN opcionais, separadas por vírgula. |
| `GLIMPSE_TURN_URLS` | URLs TURN do retransmissor, separadas por vírgula. Exemplo de formato: `turn:relay.seu-dominio:3478,turns:relay.seu-dominio:5349?transport=tcp`. |
| `GLIMPSE_TURN_SECRET` | Segredo compartilhado com o coturn, fornecido pelo gerenciador de segredos da hospedagem. Nunca colocar no aplicativo ou Git. |
| `GLIMPSE_ALLOW_NO_RELAY` | `true` somente para desenvolvimento/testes locais sem TURN. O servidor normalmente recusa iniciar sem TURN configurado. |
| `ASPNETCORE_HTTP_PORTS` | Porta HTTP interna; o contêiner usa `8080`. HTTPS termina no proxy de hospedagem. |

O serviço gera credenciais TURN temporárias com validade de três horas, usando o formato REST do coturn: nome `timestamp:identificador`, senha `base64(HMAC-SHA1(segredo, nome))`. O segredo permanente não sai do servidor. Configuração presente não prova que o retransmissor está acessível: `/health` informa `relayConfigured`, sem testar a conectividade real.

## Publicação

`Dockerfile.signaling` compila somente o serviço e seu protocolo compartilhado; a imagem final executa como usuário sem privilégios.

```powershell
docker build -f Dockerfile.signaling -t glimpse-signaling .
```

Publique esse contêiner em uma hospedagem HTTP com HTTPS, uma única instância, verificação de saúde em `/health` e as variáveis acima. O TURN precisa de uma hospedagem que suporte as portas de mídia e transportes escolhidos; publicar apenas o serviço HTTP não resolve redes restritas.

Para coturn, habilite `use-auth-secret`, configure `static-auth-secret` com o mesmo segredo do serviço, defina o `realm`, o IP público anunciado quando necessário e as portas de retransmissão. Configure cotas de alocação e banda, bloqueio de destinos privados/locais e os transportes necessários. Consulte a [documentação oficial do coturn](https://github.com/coturn/coturn/blob/master/README.turnserver) para a configuração do ambiente. Não foi instalado ou exposto um relay neste PC.

O serviço HTTP tem limites globais de 120 criações/entradas por minuto e 10000 requisições por minuto, sem fila. Antes de uso público amplo, acrescente proteção de admissão/abuso na borda e autenticação conforme o público do produto. Emitir credenciais TURN para clientes anônimos consome a cota e a banda do operador. Esses limites são locais ao processo. Desative logs de caminhos e corpos também no proxy: o código de convite aparece no caminho HTTP. O aplicativo envia tokens no cabeçalho Authorization e recusa redirecionamentos HTTP.

## Verificação

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Rooms.ps1
```

Os testes não usam FFmpeg nem capturam a tela. Exercitam códigos locais inválidos, TLS inválido, cliente travado, cancelamento, um servidor HTTP real em loopback, negociação WebRTC entre dois participantes, tokens incorretos, isolamento de funções, terceiro participante, saída remota, reserva concorrente de vaga, expiração e cálculo das credenciais temporárias TURN.

Verificação local em 05/11/2026: compilação sem avisos/erros, publicação dos arquivos do serviço e os dois testes de salas passaram. Os testes de vídeo gerado existentes também passaram: um teste no mesmo processo e outro entre dois processos independentes, com transmissão e decodificação nos dois sentidos. Nenhuma tela foi capturada. A imagem Docker não foi construída neste computador, que não tem o comando Docker disponível.

Antes de considerar a conexão pela internet validada, testar entre dois computadores em redes diferentes, incluindo uma conexão com `GLIMPSE_RELAY_ONLY=1` nos aplicativos para forçar a passagem pelo TURN, perda temporária de internet, saída e expiração. STUN, TURN/TLS, NATs reais e transmissão de tela entre redes diferentes ainda não foram verificados. Reconexão ICE com nova negociação e entrada de um novo convidado na mesma sala continuam fora desta etapa.
