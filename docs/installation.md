# Instalar o Glimpse no Windows

1. Baixe [Glimpse-Windows-x64.zip completo](https://github.com/Otavio67/glimpse/releases/download/v0.2.6-preview/Glimpse-Windows-x64.zip).
2. Extraia o ZIP inteiro para uma pasta. Não execute de dentro do ZIP.
3. Dê dois cliques em **Abrir Glimpse.cmd**. Mantenha os arquivos e pastas juntos.
4. Opcional: execute **Instalar Glimpse.cmd** para copiar o aplicativo e criar atalhos **Glimpse** na Área de Trabalho e no menu Iniciar, sem downloads adicionais.
5. Na primeira abertura, escolha seu nome e uma foto. Você pode usar um dos quatro robôs ou importar uma foto do computador; nas próximas aberturas o perfil já estará salvo.

Se o repositório estiver privado, o GitHub só permite baixar a versão às pessoas com acesso ao repositório.

O pacote pronto inclui o aplicativo, suas bibliotecas, o runtime .NET e o FFmpeg. Não exige instalar Visual Studio, Git ou .NET separadamente. Depois do download do ZIP, abrir e instalar não precisam baixar dependências. O instalador verifica os arquivos com SHA-256 antes de copiar. As licenças e a origem dos componentes acompanham o pacote em licenses. Ao abrir, escolha Hospedar ou Entrar e, dentro da sala, Compartilhar tela. A conexão entre redes diferentes exige internet e acesso ao PC anfitrião.

Também é possível usar **Code → Download ZIP**: depois de extrair o código-fonte, execute o mesmo **Instalar Glimpse.cmd**. Nesse caso o instalador também baixa o SDK .NET 10 e as bibliotecas para compilar a aplicação antes de instalar; esse caminho baixa mais arquivos e demora mais. O ZIP de Releases já contém a aplicação compilada.

Requisitos: Windows 10 2004 (19041) ou mais recente, x64, recursos gráficos e de captura do Windows disponíveis e driver de vídeo compatível. O instalador verifica o sistema e os arquivos; ele não garante funcionamento de drivers, acesso a telas protegidas ou conectividade da operadora.

A instalação é feita para o usuário atual em `%LOCALAPPDATA%\Programs\Glimpse`, sem alterar o PATH global ou exigir instalação do .NET no sistema. Cada instalação usa uma pasta de versão nova, mantendo a anterior. Depois de instalar, é possível remover o ZIP extraído; os arquivos necessários já estarão copiados. As licenças e avisos de terceiros ficam na pasta `licenses` da versão instalada. Para remover, feche o aplicativo e remova a pasta de instalação e os atalhos.

O pacote ainda não é assinado digitalmente. O Windows pode apresentar um aviso de aplicativo desconhecido. Não desative a proteção do sistema; confira a origem e o hash publicado antes de decidir executar.

O instalador não configura firewall ou roteador. Para compartilhar pela internet hospedando no próprio PC, veja a documentação de salas nativas no repositório. CGNAT e bloqueios de entrada podem impedir a conexão mesmo com todas as dependências instaladas.
