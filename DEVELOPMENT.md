# Desenvolvimento e publicação

O launcher é um programa Windows Forms para .NET Framework 4.8 / x64, compilado com o compilador C# incluído no Windows. Inicia o Mod Engine 2 diretamente, sem chamar scripts PowerShell no computador do jogador.

`Core.cs`: catálogo assinado RSA/SHA-256, downloads HTTPS, pacote ZIP, validação de caminhos e hashes, instalação com rollback e backups, localização do jogo, configuração do perfil e importação de convites.

`Launcher.cs`: interface em português, quatro páginas, operações assíncronas e cancelamento do download. `--render ARQUIVO PÁGINA` renderiza uma prévia isolada sem abrir o jogo nem alterar configurações pessoais. `--data-dir PASTA` escolhe uma pasta de configuração explícita, útil para uma instalação já existente.

## Compilar

Execute `Build-Launcher.ps1 -Profile PASTA_DO_PERFIL -Game PASTA_GAME -OutputDirectory PASTA_DE_SAIDA`. O perfil de origem deve conter os componentes já compilados e aprovados. A pasta do jogo fornece a distribuição do Mod Engine 2; o executável do jogo não é empacotado.

O script usa uma lista explícita de arquivos. Saves, progressão de personagens, credenciais, servidor configurado, logs, backups e chaves privadas ficam de fora. A chave de assinatura é guardada em `%USERPROFILE%/.codex/EverBound-Publisher/release-signing.dpapi`, criptografada para a conta Windows atual. Faça uma cópia privada dessa chave; não a publique. A chave pública está em `build/release-public.xml` e no executável.

## Verificar

Compile `Core.cs` e `Verify.cs` como console x64, referenciando `System.Web.Extensions`, `System.IO.Compression` e `System.IO.Compression.FileSystem`. Execute o verificador com três argumentos: pasta isolada de teste, pasta da distribuição e caminho da chave pública.

Os testes usam arquivos fictícios e uma origem de save injetada; não modificam o jogo nem os saves reais. Cobrem assinatura e catálogo alterados, caminhos inválidos, exclusão de dados privados, instalação em pasta com espaços e acentos, preferências preservadas, rollback após interrupção, jogo aberto, cancelamento, ZIP alterado e configuração da inicialização. Resultado inicial: 59 verificações aprovadas. As quatro páginas foram renderizadas e revisadas visualmente.

## Próxima versão

1. Compile o perfil do mod atualizado e execute seus testes.
2. Execute o script de build com um novo `-Version`, mantendo a chave existente.
3. Execute os testes do launcher com os novos artefatos.
4. Publique o ZIP, `manifest.json`, `manifest.sig` e o launcher como ativos da nova release no GitHub.
5. Marque essa release como a versão mais recente somente depois que todos os ativos estiverem enviados.
6. Teste a consulta pelo endereço `https://github.com/FlylCaruz/everbound/releases/latest/download/manifest.json`.

`Publish-Release.ps1` faz a publicação em duas etapas: cria um rascunho, envia todos os arquivos e então publica. Requer GitHub CLI autenticado e permissão de escrita no repositório.

## Arte

A arte de fundo está em `assets/everbound-world.png`. Foi criada com o gerador de imagens integrado e copiada para o projeto. O emblema do aplicativo e a interface são desenhados pelo próprio programa.

Prompt utilizado: “Wide 16:9 panoramic painterly dark fantasy environment for an Elden Ring mod launcher named EverBound. A lonely ruined medieval castle and a vast luminous ancient tree with fine golden branches on the right; distant mountains and gold dust in quiet mist. Aged gold, charcoal and olive, solemn twilight, worn stone. Dark negative space on the left and bottom for interface controls. No characters, lettering, logos, watermark, buttons or borders.”

Verificação integrada de abertura: Verify-Launch.cs chamou Game.Launch com o perfil existente e confirmou a criação do processo do Elden Ring. Os módulos nativos foram conferidos em seguida.
