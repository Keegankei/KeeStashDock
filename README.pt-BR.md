<h1 align="center">KeeStashDock</h1>

<p align="center">
  <img src="docs/assets/app-icon.png" width="112" alt="Ícone do aplicativo KeeStashDock">
</p>

<p align="center">
  <strong>Um painel portátil para Windows para guardar trechos, capturas de tela e reutilizar texto.</strong><br>
  Capture o que precisa, organize localmente e traduza quando quiser.
</p>

<p align="center">
  <a href="README.md" lang="en">English</a> ·
  <a href="README.zh-CN.md" lang="zh-Hans">简体中文</a> ·
  <a href="README.zh-TW.md" lang="zh-Hant">繁體中文</a> ·
  <a href="README.ja-JP.md" lang="ja">日本語</a> ·
  <a href="README.ko-KR.md" lang="ko">한국어</a> ·
  <a href="README.de-DE.md" lang="de">Deutsch</a> ·
  <a href="README.fr-FR.md" lang="fr">Français</a> ·
  <a href="README.es-ES.md" lang="es">Español</a> ·
  <a href="README.pt-BR.md" lang="pt-BR">Português (Brasil)</a>
</p>

<p align="center">
  <img alt="Versão publicada 1.2.1" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 e 11" src="docs/assets/badges/windows.svg">
  <img alt="Licença Apache-2.0" src="docs/assets/badges/license.svg">
</p>

> **Status da publicação:** a versão publicada é a **1.2.1**. Esta branch prepara a **1.2.2**. As seções marcadas como **versão candidata 1.2.2** descrevem recursos que aguardam publicação; os links de download ainda oferecem a versão 1.2.1. Consulte o [registro de aceitação da versão candidata](docs/releases/1.2.2.md).

[Primeiros passos](#get-started) · [Recursos](#features) · [Atalhos](#shortcuts) · [Dados e atualizações](#data-and-upgrades) · [Desenvolvimento](#development) · [Licença](#license)

<a id="get-started"></a>
## Primeiros passos

**[Versão mais recente](https://github.com/Keegankei/KeeStashDock/releases/latest)** · **[ZIP para Windows x64 — 1.2.1](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip)** · [Soma de verificação SHA-256](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip.sha256)

1. Baixe o ZIP do aplicativo na seção **Assets**, em vez do arquivo **Source code** do GitHub, destinado ao desenvolvimento.
2. Extraia o pacote completo para uma pasta com permissão de gravação e execute `KeeStashDock.exe`. Mantenha todas as pastas que acompanham o aplicativo juntas.
3. Pressione **Alt+Q** para mostrar o painel rápido. Digite um trecho e pressione **Enter** para salvá-lo; clique no cartão para copiá-lo.
4. Abra **Configurações** pelo painel ou pela bandeja do sistema para selecionar o idioma, o tema e os componentes opcionais.

O pacote para Windows x64 inclui o .NET; não é necessário instalá-lo separadamente. O Windows 11 é a plataforma principal, com compatibilidade com o Windows 10 22H2 na medida do possível. O OCR e a tradução offline podem precisar do runtime do Visual C++ da Microsoft, que pode ser instalado pelo instalador de componentes integrado. Consulte o [registro de aceitação da versão 1.2.1](docs/releases/1.2.1.md) para conhecer a cobertura dos testes.

<details>
<summary>Verificar o ZIP baixado</summary>

Execute o comando abaixo na pasta de download e compare o resultado com o arquivo `.sha256` que acompanha o pacote:

```powershell
Get-FileHash .\KeeStashDock-1.2.1-win-x64.zip -Algorithm SHA256
```

Uma soma de verificação confirma a integridade do arquivo; ela não é uma assinatura do fornecedor. O pacote atual não é assinado.

</details>

<a id="features"></a>
## Recursos

As prévias abaixo mostram controles reais do aplicativo com conteúdo fictício de exemplo. O GIF de inspeção é gerado a partir desses controles com um ponteiro simulado. A prévia de atualização usa uma publicação simulada; não há conteúdo pessoal da área de trabalho ou da área de transferência. [Origem e reprodução das imagens](docs/images/README.md).

### 1. Capturar, buscar e organizar

<p align="center"><img src="docs/images/main-window.png" width="900" alt="Área de trabalho do aplicativo com busca, grupos, trechos fixados e entradas de imagem de exemplo"></p>

Use o painel rápido para capturas pontuais e a **área de trabalho do aplicativo** para gerenciar sua coleção. Abra essa área pelo botão de expansão do painel ou pela bandeja do sistema.

| Ferramenta | O que você pode fazer |
| --- | --- |
| Entrada rápida e busca | Salve texto com Enter; insira quebras de linha com Alt+Enter ou Shift+Enter; pesquise suas entradas enquanto digita. |
| Arrastar para o painel | Solte texto para salvá-lo, ou arquivos e pastas para salvar seus caminhos. Clique em um cartão de caminho para abrir o destino; escolha **Copiar** no menu do botão direito para copiar o caminho. O conteúdo dos arquivos não é importado. |
| Grupos, tags e fixação | Reúna entradas relacionadas, edite o título, o texto e as tags e fixe o conteúdo usado com frequência. |
| Expiração e Lixeira | Escolha nunca expirar, um dia, sete dias ou excluir após a leitura; entradas expiradas e excluídas vão para a Lixeira e podem ser recuperadas. Por padrão, as entradas da Lixeira são removidas permanentemente após 30 dias. |
| Notas adesivas na área de trabalho | Arraste um cartão de texto ou de caminho para fora do painel e mantenha-o como uma nota independente na área de trabalho durante a sessão atual. Sair do aplicativo fecha essas notas; as entradas salvas permanecem na sua coleção. Cartões de imagem continuam sendo entradas de imagem. |
| Códigos QR | Clique com o botão direito em um cartão de texto para exibir um código QR com até 1.800 caracteres. |
| Ferramentas de texto | Use o menu Transformar da área de trabalho do aplicativo para converter maiúsculas e minúsculas, limpar espaços em branco, formatar JSON, codificar ou decodificar URLs e usar Base64. |
| Importação e exportação de grupos | Compartilhe pacotes `.ksdgrp` ou carregue conjuntos de comandos para Git, Docker, shells e outras ferramentas. Pacotes com imagens exigem uma versão compatível do aplicativo. |
| Histórico da área de transferência | Colete automaticamente o **texto** copiado, se ativar essa opção; textos vazios ou grandes demais são ignorados e duplicatas não são adicionadas novamente. Desativado por padrão. |
| Colagem direta | Cole uma entrada de texto copiada no aplicativo anterior, se ativar essa opção e a janela de destino puder ser confirmada com segurança. Desativado por padrão. |

### 2. Painéis Padrão e Espacial · versão candidata 1.2.2

Selecione **Configurações > Modo do painel**. Os dois modos mantêm os mesmos controles, tipografia, cores e disposição dos cartões.

<table>
  <tr><th>Padrão</th><th>Espacial</th></tr>
  <tr>
    <td><img src="docs/images/classic-panel.png" width="290" alt="Painel Padrão com visualização plana e sombras suaves"></td>
    <td><img src="docs/images/floating-panel.png" width="290" alt="Painel Espacial com perspectiva, cartões em camadas e uma borda lateral fina"></td>
  </tr>
  <tr><td>Uma apresentação plana e estável com sombras suaves.</td><td>Perspectiva, respostas independentes dos cartões e profundidade visível entre as camadas.</td></tr>
</table>

<p align="center"><img src="docs/images/floating-panel-motion.gif" width="400" alt="Modo Espacial: resposta dos cartões à proximidade do ponteiro, seguida de inspeção por arraste com o botão pressionado"></p>

*O ponteiro branco demonstra a resposta dos cartões próximos; o ponteiro laranja indica a inspeção por arraste com o botão pressionado. Esta animação é ilustrativa, não uma medição da taxa de quadros.*

- **Perspectiva fixa:** mantenha uma visualização inclinada estável enquanto os cartões próximos respondem ao ponteiro.
- **Arrastar para inspecionar:** mantenha o **botão direito do mouse** pressionado sobre o corpo de um cartão ou o fundo do painel e arraste. Solte para manter o ângulo. Um clique direito sem arrastar continua abrindo o menu do cartão. As configurações também permitem usar o botão do meio ou uma combinação personalizada de botão e teclas modificadoras.
- **Barra compacta:** recolha o painel para exibir seis ações: minimizar, expandir/recolher, bloquear, captura de tela, área de trabalho do aplicativo e Configurações. Ao expandir, o tamanho anterior do painel é restaurado.
- **Ocultar na borda da área de trabalho:** ative essa opção, arraste o painel até uma das quatro bordas da tela e afaste o ponteiro. Uma pequena alça permanece visível; passe o ponteiro sobre ela ou clique para restaurar o painel. O bloqueio mantém o painel visível.

Há temas claro, escuro e do sistema, com controles de plano de fundo personalizado, opacidade e efeito fosco. As preferências do sistema de alto contraste e redução de animações oferecem alternativas estáticas. [Prévia escura](docs/images/floating-panel-dark.png) · [Prévia da visualização fixa](docs/images/floating-panel-fixed.png) · [Desempenho medido e limites](docs/performance/1.2.2-floating.md).

As configurações também oferecem paletas de texto para o painel, uma cor de texto personalizada e contraste automático, além de controles independentes de estilo, brilho, sombra e opacidade para o balão de seleção.

### 3. Capturas de tela e reconhecimento local de texto

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="Entrada de captura de tela salva com a imagem original e o texto reconhecido editável"></p>

Pressione **Ctrl+Alt+S** ou um botão de captura e arraste para selecionar uma região. As capturas salvas se tornam entradas de imagem no grupo selecionado. Abra uma para visualizar, copiar, salvar como PNG ou **Reconhecer texto**. Você pode editar e pesquisar o texto reconhecido mantendo a imagem original.

**Versão candidata 1.2.2:** após a seleção, uma barra de ferramentas oferece três ações separadas:

| Ação | Resultado |
| --- | --- |
| Salvar captura | Salva uma entrada de imagem. Enter seleciona esta ação. |
| Extrair texto | Executa o OCR local e mostra um texto temporário sem salvar uma entrada nem copiá-lo automaticamente. |
| Extrair e traduzir | Executa o OCR local e envia o texto reconhecido ao provedor de tradução escolhido. A captura de tela em si não é enviada. |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="Resultado temporário de OCR com controles explícitos de cópia de texto na versão candidata 1.2.2"></p>

Arraste novamente para refazer a seleção; **Esc** ou um clique direito cancela a captura. Os resultados temporários permitem copiar explicitamente, preservar o texto original se a tradução falhar e tentar traduzir novamente. O OCR é uma etapa de reconhecimento; ele não traduz automaticamente. [Fluxos de captura de tela](docs/SCREENSHOTS.md) · [Suporte e limites do OCR](docs/LOCAL_OCR.md).

### 4. Ações de seleção e tradução

<p align="center"><img src="docs/images/selection-actions.png" width="480" alt="Balão de seleção da versão 1.2.2 com botões independentes de Copiar e Traduzir"></p>

Ative o balão de seleção em **Configurações > Comportamento** e selecione texto em outro aplicativo. **Copiar** salva a seleção no grupo atual do painel; **Traduzir** mostra uma tradução sem adicionar uma entrada.

**Versão candidata 1.2.2:** escolha **Copiar e traduzir** para manter os dois botões visíveis. Depois de copiar, você ainda pode traduzir a mesma seleção antes que o balão feche. Falhas de tradução preservam o texto original.

O idioma de destino da tradução acompanha o idioma da interface do aplicativo. Os idiomas de destino disponíveis dependem do provedor selecionado ou dos modelos offline instalados.

| Provedor | Configuração e disponibilidade |
| --- | --- |
| Bing (China), Google, Lingva | Não exigem chave. A disponibilidade não é garantida. Novas instalações usam Bing por padrão; as preferências existentes são mantidas. |
| Baidu, Alibaba Cloud, DeepL | Forneça suas próprias credenciais. As cotas gratuitas e as cobranças dependem do provedor e da sua conta. O Alibaba Cloud exige a ativação do serviço Machine Translation e a permissão `alimt:TranslateGeneral`. |
| Local offline | Instale o mecanismo local opcional; ele usa um serviço de loopback sem conta ou tarifa de API. |

A tradução na nuvem envia apenas o texto solicitado ao provedor selecionado, sem troca automática de provedor. O OCR não envia imagens. As credenciais dos provedores são armazenadas **em texto simples** no arquivo local `settings.json`; campos mascarados não criptografam essas informações.

### 5. Instalar componentes opcionais dentro do aplicativo

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="Configurações de componentes opcionais com botões de instalação e reparo de OCR e tradução offline"></p>

*Os controles de componentes são mostrados em um estado de exemplo não instalado da versão 1.2.1. A instalação normal usa esses botões do aplicativo; nenhum comando de terminal é necessário.*

1. Abra **Configurações > Componentes opcionais**.
2. Para OCR, escolha **Instalar / reparar** em **Reconhecimento de texto (chinês / inglês)** se algum componente estiver ausente. O ZIP padrão já inclui o mecanismo e os modelos de inglês e chinês simplificado; a instalação também verifica o runtime nativo.
3. Para tradução local, escolha **Instalar / reparar** em **Tradução offline (chinês / inglês)**. Mantenha as configurações abertas durante o download inicial; reserve aproximadamente **1 GB de espaço livre em disco** e **2 GB de memória disponível**.
4. Siga o instalador da Microsoft, se solicitado. Se a instalação informar **Reinicialização necessária**, reinicie o Windows e tente novamente. Cancelar a instalação no aplicativo não desfaz uma instalação do runtime do Windows que já tenha começado.
5. Selecione **Local offline (Argos / LibreTranslate)** como mecanismo de tradução. Mantenha `http://127.0.0.1:5000` e use **Testar tradução**. O serviço inicia quando necessário; **Iniciar serviço** e **Parar serviço** também estão disponíveis.

A instalação inicial precisa de acesso à internet aos servidores de download correspondentes do GitHub, Python, pacotes, modelos e Microsoft. Um download que falhou pode ser tentado novamente; arquivos válidos em cache são reutilizados. Depois de instalados, o OCR e a tradução local funcionam offline. O serviço de tradução continua em execução após o aplicativo fechar, até ser interrompido.

**Idiomas compatíveis:** o OCR reconhece texto impresso em inglês e chinês simplificado. Os modelos de tradução incluídos oferecem suporte a **inglês ↔ chinês simplificado**. Os **20 idiomas da interface** não significam que o OCR ou a tradução offline ofereçam suporte a 20 idiomas. A ausência de modelos nunca provoca uma troca para tradução na nuvem. [Instalação offline e solução de problemas](docs/OFFLINE_TRANSLATION.md).

### 6. Verificar atualizações · versão candidata 1.2.2

<p align="center"><img src="docs/images/update-check.png" width="720" alt="Janela de atualização mostrando uma versão mais recente simulada e um caminho de instalação de exemplo"></p>

*A versão e o caminho mostrados são ilustrativos; esta imagem não anuncia uma versão disponível.*

Use **Verificar atualizações** no menu da bandeja ou em **Configurações > Sobre**. A verificação não baixa nem instala nada automaticamente. O download verifica o SHA-256 e o conteúdo do pacote; **Reiniciar e atualizar** exige uma confirmação separada. O atualizador substitui os arquivos gerenciados do aplicativo no caminho existente, preserva os dados do usuário e os componentes instalados e mantém informações de recuperação se a inicialização falhar.

O pacote organizado da versão candidata tem três arquivos na raiz: `KeeStashDock.exe`, `StartHere.md` e `update-manifest.json`. Os guias ficam em `Docs`, os avisos legais em `Licenses`; idiomas e componentes opcionais mantêm suas pastas dedicadas. Mantenha o pacote completo junto. [Atualizações e recuperação](docs/UPDATING.md).

<a id="shortcuts"></a>
## Atalhos

| Ação | Atalho ou gesto padrão |
| --- | --- |
| Mostrar/ocultar o painel rápido | **Alt+Q**; configurável |
| Salvar o texto digitado | **Enter** |
| Inserir quebra de linha | **Alt+Enter** ou **Shift+Enter** |
| Captura de tela | **Ctrl+Alt+S**; configurável separadamente |
| Salvar a região selecionada da captura | **Enter** na barra de captura da versão 1.2.2 |
| Cancelar captura | **Esc** ou clique direito |
| Inspecionar o painel Espacial | Manter o **botão direito do mouse** pressionado e arrastar; configurável na versão 1.2.2 |
| Copiar/traduzir pelo balão de seleção | **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T** na versão 1.2.2, somente enquanto o balão estiver visível e os atalhos estiverem disponíveis |

<a id="data-and-upgrades"></a>
## Dados, backup e atualizações

Entradas, imagens, textos reconhecidos e backups são armazenados localmente. Os dados portáteis normalmente ficam em `Data`, ao lado do executável. Se não for possível gravar nessa pasta, o aplicativo usa `%AppData%\KeeStashDock` e informa o local alternativo nas configurações. Componentes instalados pelo aplicativo normalmente usam `%LOCALAPPDATA%\KeeStashDock`, separados da sua coleção.

Use **Configurações > Dados** para exportar ou importar um backup e configurar backups agendados. A primeira atualização a partir da **1.2.1 é manual**:

1. Exporte um backup e saia do aplicativo pelo menu da bandeja.
2. Extraia o novo ZIP para uma pasta separada. Com o aplicativo fechado, copie **toda a pasta de dados ativa** para a pasta `Data` da nova instalação. Se as configurações indicarem um local alternativo em `%AppData%`, use essa pasta como origem.
3. Mantenha a instalação original e o backup. Inicie a nova versão e confira suas entradas antes de remover as cópias antigas. Guarde um backup anterior à atualização para poder voltar à versão anterior; esquemas antigos de banco de dados não são intercambiáveis.

Não anexe sua pasta pessoal `Data`, credenciais, backups privados ou logs a uma issue. Consulte [suporte e relatórios sem dados sensíveis](SUPPORT.md), [recuperação de atualização](docs/UPDATING.md) e a [política de segurança](SECURITY.md).

<a id="development"></a>
## Desenvolvimento e documentação

Compile no Windows com o SDK selecionado por [`global.json`](global.json) (10.0.401 ou uma atualização de correção compatível 10.0.4xx):

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore --disable-build-servers
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

O inglês é a referência para a documentação do repositório. Os READMEs traduzidos seguem a mesma estrutura de recursos; as traduções da interface ficam em `Locales/*.json`. Correções de tradução são bem-vindas conforme o [guia de contribuição](CONTRIBUTING.md).

| Documentação | Conteúdo |
| --- | --- |
| [Histórico de alterações](CHANGELOG.md) | Histórico de versões |
| [Arquitetura](docs/ARCHITECTURE.md) | Projetos, responsabilidades e armazenamento |
| [Localização](docs/LOCALIZATION.md) | Convenções de tradução da interface e dos READMEs |
| [Processo de publicação](docs/RELEASING.md) | Compilação, empacotamento e verificações de aceitação |
| [Suporte](SUPPORT.md) · [Código de conduta](CODE_OF_CONDUCT.md) | Perguntas, relatos de problemas e participação na comunidade |
| [Avisos de terceiros](THIRD_PARTY_NOTICES.md) | Licenças de dependências, runtimes e modelos |

<a id="license"></a>
## Licença

Copyright © 2026 KeeganKei. Licenciado sob a [Apache-2.0](LICENSE); consulte [NOTICE](NOTICE) para atribuições. Dependências, runtimes opcionais e modelos mantêm suas próprias licenças, registradas em [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). A licença do projeto não concede direitos sobre marcas.

O ícone do aplicativo foi gerado por IA sob a direção do mantenedor. O design e a origem estão documentados em [docs/assets/README.md](docs/assets/README.md).
