# EverBound Launcher

Launcher do projeto EverBound para Elden Ring 1.17, com instalação, atualização, reparo e inicialização do perfil.

## Para jogar

1. Baixe **EverBoundLauncher.exe** na [versão mais recente](https://github.com/FlylCaruz/everbound/releases/latest).
2. Abra o launcher e entre em **Instalação**.
3. Clique em **Localizar…** e selecione `eldenring.exe` da sua instalação.
4. Clique em **Instalar / reparar** e, ao terminar, em **Jogar EverBound** na tela inicial.

O pacote inicial já está incluído no executável. A instalação funciona sem internet; o launcher consulta o GitHub para encontrar novas versões. O perfil fica em uma pasta separada e o executável do jogo permanece intacto. Não é necessário instalar PowerShell 7 ou ferramentas de desenvolvimento.

Requisitos: Windows de 64 bits com .NET Framework 4.8 e Elden Ring 1.17, executável 2.7.0.0 compatível com este perfil. Nas instalações Steam, mantenha o Steam aberto antes de jogar. O perfil inicia o Elden Ring offline; o chat usa seu próprio servidor e convite.

## Atualizar e reparar

Em **Atualizações**, clique em **Buscar atualização** e depois em **Atualizar / reparar**. Feche o jogo pelo próprio menu antes de atualizar. **Verificar arquivos**, na tela inicial, verifica os componentes do pacote.

Antes de substituir arquivos, o launcher guarda uma cópia de segurança. Uma falha durante a aplicação restaura os arquivos anteriores. Progresso de EXP, convites do chat e preferências do overlay são preservados. As cópias locais podem ser abertas em **Configurações → Cópias de segurança**.

Para instalar por arquivos locais, coloque `manifest.json`, `manifest.sig` e `EverBound-Client-VERSÃO.zip` na mesma pasta e selecione o catálogo em **Configurações**. O catálogo e o ZIP são verificados antes da instalação.

Para atualizar o próprio launcher, baixe novamente `EverBoundLauncher.exe` da versão mais recente. As configurações ficam separadas do executável.

## Chat

Receba do administrador um convite individual `client.json`. Em **Configurações → Importar convite…**, selecione esse arquivo com o jogo fechado. Ao entrar no personagem, use **Enter** para abrir o chat e `/help` para listar os comandos.

Cada jogador usa seu próprio convite. Credenciais do administrador, saves e dados pessoais não fazem parte dos downloads públicos.

## O que está incluído

- HUD EverBound e inventário com três colunas.
- EXP, níveis automáticos, ouro e distribuição de pontos de atributo.
- Minimap circular com moldura dourada e aviso de LEVEL UP.
- Chat entre jogadores, canais, comandos e permissões.
- Inicialização direta do perfil.

O pacote atual oferece chat entre jogadores. Combate e mundo compartilhados são uma etapa futura do projeto.

## Publicação

Os binários ficam em **Releases**, separados do código do launcher. `manifest.json` e `manifest.sig` formam o catálogo de atualizações. O launcher incorpora a chave pública de publicação e verifica as assinaturas e os hashes dos arquivos.

A chave privada de publicação permanece somente no computador do responsável, protegida pelo Windows. Ela não é enviada ao repositório nem incluída no programa. As próximas versões precisam ser assinadas com essa mesma chave para serem aceitas pelos launchers já distribuídos.

O Mod Engine 2 e o EROverlay são distribuídos com suas licenças nos pacotes.
