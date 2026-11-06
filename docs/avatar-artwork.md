# Avatares padrão do Glimpse

Quatro imagens originais criadas com a ferramenta integrada de geração de imagens, em 05/11/2026. Os arquivos finais foram copiados para o projeto e são incorporados ao executável; não dependem de download no primeiro uso.

| Avatar | Arquivo | Subject usado no prompt |
| --- | --- | --- |
| Astro | `src/Glimpse/Assets/Avatars/astronaut.png` | Friendly round purple toy robot astronaut with a clear bubble helmet, little mint antenna and a big delighted grin. |
| Óculos | `src/Glimpse/Assets/Avatars/glasses.png` | Funny mint-green round toy robot wearing oversized purple glasses, one eye playfully winking, tiny orange cheeks. |
| Festa | `src/Glimpse/Assets/Avatars/party.png` | Silly coral-orange round toy robot wearing a striped violet party cone hat, squinting happy eyes and a goofy broad grin. |
| Torradinha | `src/Glimpse/Assets/Avatars/toaster.png` | Adorable buttery-yellow toaster-shaped toy robot, a toast slice peeking above its head, tiny purple antenna, surprised delighted face. |

Cada imagem foi gerada individualmente usando o prompt abaixo, substituindo `{subject}` pelo texto correspondente da tabela:

```text
Use case: stylized-concept. Asset type: square default profile avatar for the Glimpse desktop app. Create ONE original character portrait. {subject} Consistent friendly polished clay-like 3D cartoon style, front-facing head and small shoulders centered, simple bold silhouette readable at 40px, soft studio lighting, no text, no logos, no watermark. Square image with a clean solid deep navy background #202638 and plenty of safe margin for a circular crop. Keep all important features within the central 75 percent. High-quality playful app avatar.
```

Os PNGs originais gerados foram preservados; o aplicativo decodifica versões menores para exibição. Não foi usado o modo CLI/API ou chave de API do usuário.
