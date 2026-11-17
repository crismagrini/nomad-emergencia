# N.O.M.A.D. de emergência

## Enquanto houver internet

1. Abra **NOMAD-Emergencia.exe** no Windows 11 x64. Escolha a pasta onde vai guardar o kit. Use uma unidade com espaço para o acervo, uma distribuição temporária e sua exportação. O formato FAT32 não comporta os arquivos grandes.
2. Marque os módulos desejados e clique em **Baixar e preparar offline**. O programa mostra o progresso de cada transferência e a etapa corrente da preparação. Para retomar depois de pausar, abra a mesma pasta com a mesma seleção. Etapas internas como importação de cursos e exportação do Ubuntu podem precisar terminar antes da pausa.
3. Aguarde **Kit pronto para guardar**. Guarde esta pasta inteira, incluindo o executável e a subpasta `Dados`. Use **Verificar kit** para checar os arquivos por SHA-256 antes de guardar a cópia e periodicamente.

**Catálogo Kiwix completo em português** seleciona uma edição por título exclusivamente em português, preferindo a de maior cobertura pelo tamanho do arquivo. Inclui a Wikipédia. O total depende do catálogo disponível na primeira execução. Os canais educacionais incluídos são Ciênsação, PhET, Sikana e Khan Academy em português. O modelo local é Qwen3 4B.

O modo de preparo cria uma distribuição WSL temporária com nome `NOMAD-Preparo-…` na raiz da unidade escolhida e a deixa parada ao final. Essa pasta de trabalho fica fora do kit e pode ocupar espaço adicional. O Ubuntu de outras instalações é independente.

## Quando precisar, mesmo sem internet

Abra o executável a partir da pasta guardada e clique em **Instalar / abrir**. Ele verifica o kit, usa o instalador do WSL que já está na pasta se necessário, importa o servidor para a unidade do Windows e abre **http://localhost:8080**. O servidor usa as imagens Docker guardadas. A virtualização precisa estar habilitada no firmware. Se o Windows pedir reinício para ativar o WSL, reinicie e clique em **Instalar / abrir** novamente.

Deixe a pasta do kit conectada enquanto o servidor estiver rodando. A biblioteca ZIM é lida dessa pasta. Para parar o servidor, use `wsl --terminate NOMAD-Emergencia` no PowerShell. Para iniciar novamente, use **Instalar / abrir**.

## Alternativas selecionáveis

- **Kiwix portátil**. Extraia o ZIP de `Dados\Extras`, abra o leitor e escolha um arquivo ZIM de `Dados\Acervo`.
- **Kolibri para Windows**. Instalador independente. Os cursos selecionados ficam no servidor N.O.M.A.D. Para usar o Kolibri independente com conteúdo offline, exporte os canais previamente no formato `KOLIBRI_DATA` para importação local.
- **OsmAnd para Android ARM64**. A pasta contém o APK e os mapas das 27 unidades federativas brasileiras. Transfira para o telefone, extraia os arquivos `.obf` dos ZIPs e importe os mapas no OsmAnd. Use a edição do APK salva com o kit. Teste a importação e uma rota em modo avião antes de precisar.
- **Internet-in-a-Box para Raspberry Pi**. Use o Raspberry Pi Imager para gravar a imagem local salva em `Dados\Extras` em um cartão compatível. Essa imagem é uma alternativa independente e o acervo em português deve ser importado separadamente. Teste o modelo do Raspberry Pi e o cartão com antecedência.

O servidor fica acessível no próprio computador. O kit foi desenvolvido para instalação offline em Windows x64 e a restauração completa precisa ser ensaiada antes de depender dele.

## Catálogos e ferramentas

- **Catálogo Kiwix** consulta os metadados do catálogo público. Pesquise pelo título ou nome do arquivo e filtre por idioma ou edição. Para Wikipédia, `mini` é a edição resumida, `nopic` é a edição sem imagens e `maxi` é a edição completa. O catálogo também inclui outros títulos. **Marcar resultados** seleciona todos os itens do filtro atual e exibe o tamanho somado. **Só selecionados** permite revisar a lista antes do preparo. A seleção fica registrada na pasta do kit assim que o preparo começa.
- **Escolher coleções e apps no painel N.O.M.A.D.** abre o assistente oficial e o Supply Depot durante o preparo. Use também as páginas Information Library e Creator Packs para encontrar as coleções, bases de dados e pacotes adicionais. Conclua as instalações de apps no painel e então clique em **Concluir seleção** no executável. O programa aguarda a fila de downloads antes de exportar. A disponibilidade e as condições de acesso seguem o catálogo oficial. Para preservar o escopo de mapas, escolha apenas o Brasil como mapa detalhado.
- **Abrir ferramentas** dá acesso ao painel, aos aplicativos do Supply Depot e às opções independentes selecionadas. O Kiwix portátil abre no Windows a partir do ZIP verificado. Os instaladores nativos selecionados ficam disponíveis no mesmo menu. Os extras de Android e Raspberry Pi são transferidos para o dispositivo correspondente.

[Catálogo oficial do Kiwix](https://browse.library.kiwix.org/) · [Guias do Project N.O.M.A.D.](https://www.projectnomad.us/guides)


## Espaço em disco

As opções mostram estimativas em GB. O resumo separa o tamanho estimado do kit do espaço livre para prepará-lo, contando a base e a cópia temporária do servidor. Os itens escolhidos no painel oficial são adicionais e variam conforme a seleção. Consulte os tamanhos disponíveis no painel antes de escolher. Baixar e preparar offline guarda todos os itens marcados e encerra o servidor temporário. A instalação que você usará no dia a dia fica para o botão Instalar / abrir.

A opção **Wikipédia completa em inglês (maxi)** inclui a edição ilustrada de agosto de 2026, com cerca de 127,42 GB. Marque-a antes de clicar em **Baixar e preparar offline**.

## Progresso e previsão

A v6 usa D:\NOMAD como pasta padrão neste computador. Downloads diretos mostram percentual, GB recebidos, velocidade recente e horas restantes quando há tamanho e velocidade disponíveis. O painel oficial relata transferências separadas no registro quando fornece contadores. Extração de mapas, importação de cursos, instalação de imagens e exportação podem exibir apenas a etapa ou o progresso informado pela ferramenta de origem. A previsão oscila conforme a conexão.


## Logs

A aba Logs mostra o progresso, as mensagens de erro e os códigos de saída. O botão Abrir arquivo de log abre o histórico persistente NOMAD-v6.log, ao lado do executável. Em pasta sem permissão de escrita, o programa tenta salvar em %TEMP%.
