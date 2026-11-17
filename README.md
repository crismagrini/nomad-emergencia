# N.O.M.A.D. de emergência para Windows

Empacotador independente do [Project N.O.M.A.D.](https://github.com/Crosstalk-Solutions/project-nomad) para Windows x64. O aplicativo prepara, enquanto há internet, uma pasta que pode ser guardada para restaurar o servidor local em uma emergência.

## Uso

1. Abra o executável v6 e escolha uma pasta com espaço livre.
2. Selecione biblioteca Kiwix, idiomas, edições e, conforme necessário, mapa detalhado do Brasil, cursos Kolibri, IA local, guias ou ferramentas extras.
3. Clique em **Baixar e preparar offline**. Aguarde **Kit pronto para guardar** e preserve o executável junto de toda a pasta `Dados`.
4. Use **Verificar kit** para conferir os hashes. Quando precisar, use **Instalar / abrir** para importar o servidor e abrir http://localhost:8080.

O kit preparado contém o MSI do WSL, o arquivo exportado do Ubuntu com o N.O.M.A.D. e as imagens Docker, o conteúdo selecionado e o manifesto de integridade. A preparação requer internet; o fluxo de restauração usa os arquivos locais. Em Windows limpo pode ser necessário habilitar a virtualização, ter acesso administrativo e reiniciar. O mapa detalhado pré-selecionado é o do Brasil. A aba **Logs** e o arquivo `NOMAD-v6.log` registram erros e progresso. Veja [o guia completo](USO.md).

## Compilar e verificar

No Windows x64, execute `Build-Test.ps1` com PowerShell 7. O script usa o compilador do .NET Framework incluído no Windows e gera `dist/NOMAD-Emergencia-v6.exe`. A checagem do catálogo público requer conexão e consulta somente metadados.

Foram verificados a compilação, a interface, o catálogo, o progresso simulado, a verificação de integridade e a correção do fechamento do menu. **A restauração integral em Windows limpo e desconectado ainda está pendente.** Para validar um kit real, prepare-o, copie-o para uma unidade separada e teste em outra instalação Windows com a rede desligada. Confirme a abertura do painel e de cada módulo selecionado.

A configuração Compose de origem vem da revisão upstream `54ee30a44902e5c252b477f8685c26ec3064ab72`, com licença em [LICENSE-UPSTREAM](LICENSE-UPSTREAM). O servidor empacotado é o N.O.M.A.D. 1.35.1. Os projetos e conteúdos baixados mantêm suas próprias licenças. O pacote de código não inclui acervo, logs pessoais, credenciais nem imagens do servidor.

## Créditos

O servidor original é o **Project N.O.M.A.D.**, mantido pela **Crosstalk Solutions** e colaboradores. Este repositório contém o empacotador independente para Windows. Consulte o [repositório original](https://github.com/Crosstalk-Solutions/project-nomad) e o [site do projeto](https://www.projectnomad.us/).
