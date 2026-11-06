# Nome e foto no primeiro uso

Ao abrir o Glimpse sem um perfil salvo neste usuário do Windows, aparece a tela **Como você quer aparecer?**. Ela solicita um nome de 1 a 24 caracteres e permite escolher um dos quatro robôs padrão ou importar uma foto PNG, JPG ou BMP do computador. Salvar abre a tela principal; sair sem salvar encerra a abertura e mantém a configuração pendente para a próxima vez.

O nome e a foto aparecem nos cartões do próprio usuário. Clique no cartão no canto inferior esquerdo, identificado como **Editar perfil**, para alterá-los. O perfil é mantido nas próximas aberturas e atualizações do aplicativo.

Os dados são locais: `%LOCALAPPDATA%\Glimpse\profile.json`, e, para fotos próprias, um PNG na mesma pasta. O aplicativo não cria uma conta e não envia esse perfil aos demais participantes nesta etapa. A importação lê imagens de até 20 MB, 60 megapixels e 16000 pixels por lado; salva uma cópia quadrada de 256 × 256 pixels, sem alterar o arquivo original. O caminho da foto de origem não é salvo no perfil.

Um perfil ausente, ilegível ou com nome inválido abre novamente a configuração. Uma foto personalizada ausente/ilegível usa o avatar Astro como alternativa. A gravação do perfil usa um arquivo temporário e substituição do arquivo final para não perder um perfil anterior quando a nova configuração não puder ser salva.

As quatro imagens padrão são integradas à aplicação e estão descritas em [arte dos avatares](avatar-artwork.md).
