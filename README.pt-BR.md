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
  <img alt="Versão 1.3.0" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 e 11" src="docs/assets/badges/windows.svg">
  <img alt="Licença Apache-2.0" src="docs/assets/badges/license.svg">
</p>

**Versão 1.3.0** · [Notas da versão](docs/releases/1.3.0-notes.md) · [Validação e estado da publicação](docs/releases/1.3.0.md)

**Preparação da publicação:** esta branch documenta a versão 1.3.0. A publicação oficial está pendente; a [versão publicada mais recente](https://github.com/Keegankei/KeeStashDock/releases/latest) é atualmente a **1.2.3**. O registro de validação acompanha as verificações restantes.

As novas prévias mostram a **interface em inglês da versão 1.3.0** com dados de exemplo isolados. As capturas e os GIFs anteriores continuam identificados como **1.2.3**; temas ilustrados e ações combinadas dos balões são opcionais.

[Primeiros passos](#get-started) · [Recursos](#features) · [Atalhos](#shortcuts) · [Dados e atualizações](#data-and-upgrades) · [Desenvolvimento](#development) · [Licença](#license)

<a id="get-started"></a>
## Primeiros passos

**[Baixar a versão publicada mais recente](https://github.com/Keegankei/KeeStashDock/releases/latest)**

A versão 1.3.0 oferecerá `KeeStashDock-1.3.0-win-x64.zip` e `KeeStashDock-1.3.0-win-x64.zip.sha256` em **Assets**. Esses nomes descrevem os arquivos planejados, não um download já publicado.

1. Baixe o ZIP do aplicativo na seção **Assets**, em vez do arquivo **Source code** do GitHub, destinado ao desenvolvimento.
2. Extraia o pacote completo para uma pasta com permissão de gravação e execute `KeeStashDock.exe`. Mantenha todas as pastas que acompanham o aplicativo juntas.
3. Pressione **Alt+Q** para mostrar o painel rápido. Digite um trecho e pressione **Enter** para salvá-lo; clique no cartão para copiá-lo.
4. Abra **Configurações** pelo painel ou pela bandeja do sistema para selecionar o idioma, o tema e os componentes opcionais.

O pacote para Windows x64 inclui o .NET; não é necessário instalá-lo separadamente. O Windows 11 é a plataforma principal, com compatibilidade com o Windows 10 22H2 na medida do possível. O OCR e a tradução offline podem precisar do runtime do Visual C++ da Microsoft, que pode ser instalado pelo instalador de componentes integrado. Consulte o [registro de aceitação da versão 1.3.0](docs/releases/1.3.0.md) para conhecer a cobertura dos testes.

<details>
<summary>Verificar o ZIP baixado</summary>

Execute o comando abaixo na pasta de download e compare o resultado com o arquivo `.sha256` que acompanha o pacote:

```powershell
Get-FileHash .\KeeStashDock-1.3.0-win-x64.zip -Algorithm SHA256
```

Uma soma de verificação confirma a integridade do arquivo; ela não é uma assinatura do fornecedor. O pacote atual não é assinado.

</details>

**Configuração inicial · 1.3.0:** inglês; **Alt+Q** abre o painel e **Alt+W** inicia uma captura. OCR e tradução local não vêm pré-instalados. Atualizações preservam as preferências salvas.

O balão de seleção fica ativado em **Somente copiar**, com **Google** como tradutor. Estilo padrão, brilho do texto desativado e sombra ativada. Inicialização com o Windows, coleta automática da área de transferência e colagem direta ficam desativadas. Ocultação nas bordas ativada, com **2 s** para recolher e **0,1 s** para expandir. Tema do sistema, modo **Padrão**, **inspeção arrastando com o botão direito**, cores de texto automáticas e opacidade dos controles de **90%**. Sem imagem de fundo; sobreposição de **20%** e desfoque desativado, com raio salvo de **4 px**. Backups automáticos ativados a cada **24 horas**.

O tamanho do texto e a opacidade das superfícies de Configurações começam em **100 %**. O vidro fosco de Configurações e dos balões fica **desativado**. Os temas incluídos são opcionais e não são aplicados automaticamente.

<details>
<summary>Configurações de uma nova instalação · 1.2.3 · interface em inglês</summary>

<p align="center"><img src="docs/images/default-settings.png" width="720" alt="Configurações da versão 1.2.3 com inglês, Alt+Q e Alt+W como padrões"></p>

</details>

<a id="features"></a>
## Recursos

### Novidades da versão 1.3.0

| Recurso | Como usar |
| --- | --- |
| Pesquisa no painel rápido | Com o painel ativo, pressione **Ctrl+F**. Pesquise títulos e texto salvo, incluindo resultados de OCR existentes, e combine filtros de grupo, tag e texto/imagem/modelo. Os resultados são carregados em páginas de 50. **Enter** copia o resultado selecionado e **Esc** volta à coleta, preservando o rascunho. |
| Organização em lote | Selecione itens na área de trabalho para movê-los de grupo, adicionar/remover tags, fixar/desafixar, mover para a lixeira ou restaurar. **Ctrl+A seleciona apenas os resultados carregados.** Cada ação é aplicada por inteiro; em caso de conflito, nenhum item é alterado. É possível desfazer o envio à lixeira enquanto os itens não tiverem sido modificados. |
| Modelos de texto | Marque explicitamente um item de texto como modelo. Use `{{date}}`, `{{time}}` e variáveis como `{{name}}`, preencha os valores e confira a prévia antes de copiar. O original é preservado e nenhum script ou expressão é executado. Pacotes de grupos com modelos usam o formato v3. |
| Temas completos | Visualize e aplique cores e fundos claros/escuros harmonizados entre os painéis. Importe ou exporte as duas variantes e suas imagens em um único ZIP de tema. Os temas são independentes do modo Padrão/Espacial; cancelar a prévia restaura a aparência anterior. |
| Tamanho global do texto | Em **Configurações > Aparência > Tamanho do texto**, escolha entre **90% e 150%** para o painel rápido, a área de trabalho, as configurações e outros textos do aplicativo. **100%** restaura o tamanho original. |
| Área de trabalho renovada | A navegação, a lista de itens e o editor seguem o visual das configurações. No modo escuro, listas e linhas de backup usam fundos harmonizados e texto legível. |
| Balões compactos | Ações de cópia ao lado do original e do resultado, com um layout mais compacto para idioma de destino, grupo e salvamento. O estilo padrão e Wisteria Atelier usam os mesmos controles com paletas coordenadas. |
| Opacidade e vidro fosco | Ajuste separadamente as superfícies de Configurações entre **10 % e 100 %**. O desfoque opcional de Configurações suaviza a imagem de fundo em **1–18 px**; o dos balões usa a composição do Windows, com fundo opaco quando indisponível. O texto continua nítido. |

Novas instalações continuam usando inglês, tema do sistema, modo Padrão e nenhuma imagem de fundo. A atualização não aplica automaticamente um tema ilustrado nem redefine as preferências salvas.

**Configurações:** navegue entre **Geral**, **Captura e tradução**, **Aparência**, **Componentes locais**, **Backup e dados** e **Sobre**. As opções dependentes aparecem quando necessárias; ocultá-las preserva os valores salvos.

Em **Captura e tradução**, mostre ou oculte separadamente o seletor de idioma de destino e os controles para salvar em um grupo. Ambos ficam ativados por padrão e aparecem após o OCR ou a tradução. Ao desativá-los, o resultado e as ações de cópia continuam disponíveis.

Escolha a pasta de backups em **Backup e dados**, e as pastas independentes do OCR e da tradução offline em **Componentes locais**. Mudar a pasta **não move os arquivos existentes**; reinstale o componente se a nova pasta estiver vazia. **Sobre** mostra o local ativo dos dados em modo somente leitura. Use a exportação e importação para mover a biblioteca com segurança.

As prévias **1.3.0** da área de trabalho do aplicativo, dos temas e dos balões são renderizações fora da tela de controles WPF reais com conteúdo fictício. As demais capturas e GIFs da **1.2.3** demonstram os fluxos existentes. O ponteiro do GIF de inspeção e a versão da prévia de atualização são simulados. Caminhos de instalação e de dados estão ocultos; nenhum conteúdo pessoal da área de trabalho ou da área de transferência aparece. [Origem e reprodução das imagens](docs/images/README.md).

<p align="center"><img src="docs/images/standard-panel-workflow.gif" width="400" alt="Fluxo do painel Padrão da versão 1.2.3: digitar, salvar, fixar e desafixar um cartão"></p>

*1.2.3 · Modo Padrão · digitar, salvar, fixar e desafixar · interface em inglês. Interação automatizada com controles reais e dados de exemplo.*

### 1. Capturar, buscar e organizar

<p align="center"><img src="docs/images/1.3.0/workbench-light.png" width="900" alt="Área de trabalho do aplicativo 1.3.0 com busca, seleção múltipla e editor de modelos, usando o tema claro opcional Wisteria Atelier"></p>

*1.3.0 · Área de trabalho do aplicativo e editor de modelos de texto · Tema claro opcional Wisteria Atelier.*

Use o painel rápido para capturas pontuais e a **área de trabalho do aplicativo** para gerenciar sua coleção. Abra essa área pelo botão de expansão do painel ou pela bandeja do sistema.

| Ferramenta | O que você pode fazer |
| --- | --- |
| Entrada rápida e busca | Salve com Enter; insira quebras de linha com Alt+Enter ou Shift+Enter. Ctrl+F abre a busca do painel rápido. O painel e a área de trabalho do aplicativo permitem filtrar por grupo, etiqueta e tipo. |
| Arrastar para o painel | Solte texto para salvá-lo, ou arquivos e pastas para salvar seus caminhos. Clique em um cartão de caminho para abrir o destino; escolha **Copiar** no menu do botão direito para copiar o caminho. O conteúdo dos arquivos não é importado. |
| Grupos, tags e fixação | Reúna entradas relacionadas, edite o título, o texto e as tags e fixe o conteúdo usado com frequência. |
| Expiração e Lixeira | Escolha nunca expirar, um dia, sete dias ou excluir após a leitura; entradas expiradas e excluídas vão para a Lixeira e podem ser recuperadas. Por padrão, as entradas da Lixeira são removidas permanentemente após 30 dias. |
| Notas adesivas na área de trabalho | Arraste um cartão de texto ou de caminho para fora do painel e mantenha-o como uma nota independente na área de trabalho durante a sessão atual. Sair do aplicativo fecha essas notas; as entradas salvas permanecem na sua coleção. Cartões de imagem continuam sendo entradas de imagem. |
| Códigos QR | Clique com o botão direito em um cartão de texto para exibir um código QR com até 1.800 caracteres. |
| Ferramentas de texto | Use o menu Transformar da área de trabalho do aplicativo para converter maiúsculas e minúsculas, limpar espaços em branco, formatar JSON, codificar ou decodificar URLs e usar Base64. |
| Importação e exportação de grupos | Compartilhe pacotes `.ksdgrp` ou carregue comandos predefinidos para Git, Docker, shells e outras ferramentas. Pacotes de modelos usam o formato 3 e exigem a versão 1.3.0 ou posterior; os formatos 1 e 2 continuam sendo importados. |
| Histórico da área de transferência | Colete automaticamente o **texto** copiado, se ativar essa opção; textos vazios ou grandes demais são ignorados e duplicatas não são adicionadas novamente. Desativado por padrão. |
| Colagem direta | Cole uma entrada de texto copiada no aplicativo anterior, se ativar essa opção e a janela de destino puder ser confirmada com segurança. Desativado por padrão. |

### 2. Painéis Padrão e Espacial

*As capturas do painel e o GIF de movimento desta seção mostram a versão 1.2.3.*

Selecione **Configurações > Aparência > Modo do painel**. Os dois modos mantêm os mesmos controles, tipografia, cores e disposição dos cartões.

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
- **Ocultar na borda da área de trabalho:** ativado por padrão; arraste o painel até uma das quatro bordas da tela e afaste o ponteiro. Uma pequena alça permanece visível; passe o ponteiro sobre ela ou clique para restaurar o painel. O bloqueio mantém o painel visível.

Nos dois modos, arraste as bordas ou os cantos do painel expandido para alterar seu tamanho; ele será restaurado ao reiniciar. A barra compacta de seis botões mantém um tamanho fixo.

Há temas claro, escuro e do sistema, com controles de plano de fundo personalizado, opacidade e efeito fosco. As preferências do sistema de alto contraste e redução de animações oferecem alternativas estáticas. [Prévia escura](docs/images/floating-panel-dark.png) · [Prévia da visualização fixa](docs/images/floating-panel-fixed.png).

As configurações também oferecem paletas de texto para o painel, uma cor de texto personalizada e contraste automático, além de controles independentes de estilo, brilho, sombra e opacidade para o balão de seleção.

<details>
<summary>Configurações de aparência iniciais</summary>

<p align="center"><img src="docs/images/default-appearance.png" width="720" alt="Configurações de aparência com modo Padrão e valores de uma nova instalação"></p>

</details>
#### Temas de aparência completos

Abra **Configurações > Aparência > Temas completos**, selecione um tema e escolha **Pré-visualizar e aplicar**. Selecione Claro, Escuro ou Sistema e escolha se os balões de seleção e os resultados acompanham o tema. **Cancelar** restaura a aparência anterior. No modo Sistema, a variante diurna ou noturna acompanha o tema claro ou escuro do Windows.

| Tema | Ambiente |
| --- | --- |
| **Sunlit Gallery** | Galeria tranquila em tons creme com grandes janelas, luz diurna e uma variante noturna iluminada pela lua; sem personagens. |
| **Twilight Study** | Escritório em tons profundos de oliva e nogueira, com variantes diurna e noturna; sem personagens. |
| **Wisteria Atelier** | Pianista em estilo anime em um ateliê luminoso, com variantes clara e escura coordenadas. |
| **Wisteria Reverie** | Pianista em estilo anime em um jardim de inverno iluminado em tons de lavanda, com variantes coordenadas de dia e noite nos formatos retrato e paisagem. |

Todos os temas são opcionais. Novas instalações mantêm o tema do sistema sem imagem de fundo.

[Guia de temas e prévias dos fundos](docs/THEMES.md).

<p align="center"><img src="docs/images/1.3.0/theme-preview-light.png" width="680" alt="Prévia de Wisteria Atelier na versão 1.3.0 com opções de aparência, controles de exemplo e botões fixos Aplicar e Cancelar"></p>

Use **Importar tema** para carregar um ZIP de tema ou **Exportar aparência atual** para compartilhar as variantes clara e escura, as imagens e as configurações de aparência. A inclusão de fundos pessoais precisa ser escolhida explicitamente. Os temas não contêm entradas, chaves de API, atalhos nem instalações de componentes. Os arquivos ficam em `theme/`, ao lado do executável; atualizações preservam os temas importados em `theme/custom/`.

A **opacidade das superfícies de Configurações** ajusta cartões, navegação e campos sem esmaecer o texto. O **fundo fosco de Configurações** desfoca apenas a imagem do tema; o **fundo fosco dos balões** é uma opção separada em **Balões e notificações**. Os dois efeitos começam desativados. Conforme o caso, Configurações sem imagem, alto contraste ou composição de balões indisponível usam superfícies opacas legíveis.

<p align="center"><img src="docs/images/1.3.0/settings-frosted-dark.png" width="760" alt="Configurações de aparência da versão 1.3.0 com tema Wisteria escuro opcional, superfícies a 60 por cento e desfoque do fundo de 8px"></p>

*1.3.0 · Tema Wisteria escuro opcional, superfícies de Configurações a 60 % e desfoque do fundo de 8 px. Não é a aparência inicial nem uma demonstração do desfoque nativo da área de trabalho.*

### 3. Capturas de tela e reconhecimento local de texto

*As imagens do fluxo de captura desta seção mostram a versão 1.2.3.*

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="Entrada de captura de tela salva com a imagem original e o texto reconhecido editável"></p>

Pressione **Alt+W** ou um botão de captura e arraste para selecionar uma região. As capturas salvas se tornam entradas de imagem no grupo selecionado. Abra uma para visualizar, copiar, salvar como PNG ou **Reconhecer texto**. Você pode editar e pesquisar o texto reconhecido mantendo a imagem original.

Após a seleção, uma barra de ferramentas oferece três ações separadas:

| Ação | Resultado |
| --- | --- |
| Salvar captura | Salva uma entrada de imagem. Enter seleciona esta ação. |
| Extrair texto | Executa o OCR local e mostra um texto temporário sem salvar uma entrada nem copiá-lo automaticamente. |
| Extrair e traduzir | Executa o OCR local e envia o texto reconhecido ao provedor de tradução escolhido. A captura de tela em si não é enviada. |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="Resultado temporário de OCR com controles explícitos de cópia de texto na versão 1.2.3"></p>

Arraste novamente para refazer a seleção; **Esc** ou um clique direito cancela a captura. Os resultados temporários permitem copiar explicitamente, preservar o texto original se a tradução falhar e tentar traduzir novamente. O OCR é uma etapa de reconhecimento; ele não traduz automaticamente. [Fluxos de captura de tela](docs/SCREENSHOTS.md) · [Suporte e limites do OCR](docs/LOCAL_OCR.md).

Resultados temporários de OCR e tradução oferecem **Salvar original**, **Salvar tradução** e **Salvar ambos**, com o grupo atual selecionado inicialmente. Salvar ambos cria uma única entrada com seções identificadas. O salvamento é explícito, não altera a área de transferência nem salva também a captura. Se a tradução falhar, o original ainda pode ser salvo; se o salvamento falhar, o resultado permanece para tentar novamente. Texto duplicado reutiliza a entrada existente e a coloca no grupo escolhido.

### 4. Ações de seleção e tradução

<table>
  <tr><th>1.3.0 · Estilo padrão</th><th>1.3.0 · Estilo Wisteria escuro</th></tr>
  <tr>
    <td><img src="docs/images/1.3.0/selection-default.png" width="345" alt="Balão de seleção padrão da versão 1.3.0 com as ações opcionais Copiar e Traduzir"></td>
    <td><img src="docs/images/1.3.0/selection-wisteria-dark.png" width="345" alt="Balão de seleção Wisteria escuro da versão 1.3.0 com as ações opcionais Copiar e Traduzir"></td>
  </tr>
</table>

*As duas prévias da versão 1.3.0 ativam o modo opcional Copiar e traduzir. Novas instalações continuam usando Apenas copiar.*

Ative o balão de seleção em **Configurações > Captura e tradução** e selecione texto em outro aplicativo. **Copiar** salva a seleção no grupo atual do painel; **Traduzir** mostra uma tradução sem adicionar uma entrada.

Escolha **Copiar e traduzir** para manter os dois botões visíveis. Depois de copiar, você ainda pode traduzir a mesma seleção antes que o balão feche. Falhas de tradução preservam o texto original.

Escolha nas configurações um idioma de tradução independente da interface. **Seguir o idioma da interface** continua sendo o padrão em novas instalações; atualizações preservam sua escolha. Traduções de seleção, capturas e testes usam a mesma preferência. A janela de resultado mostra o destino efetivo e permite retraduzir temporariamente o original preservado sem alterar as configurações globais. Os destinos dependem do provedor ou dos modelos instalados. Pares não compatíveis geram um erro sem trocar de provedor nem enviar o texto offline.

<table>
  <tr><th>1.3.0 · Estilo padrão</th><th>1.3.0 · Estilo Wisteria escuro</th></tr>
  <tr>
    <td><img src="docs/images/1.3.0/translation-default.png" width="420" alt="Resultado compacto de tradução padrão da versão 1.3.0 com cópia, idioma de destino e salvamento"></td>
    <td><img src="docs/images/1.3.0/translation-wisteria-dark.png" width="420" alt="Resultado compacto de tradução Wisteria escuro da versão 1.3.0 com as mesmas ações"></td>
  </tr>
</table>

*1.3.0 · Texto de exemplo preenchido em controles reais de resultado; nenhum OCR ou serviço de tradução foi executado. O idioma de destino e as ações de salvamento aparecem nas duas visualizações e podem ser ocultados separadamente em Configurações. O desfoque nativo dos balões não é capturado nestas prévias fora da tela.*

| Provedor | Configuração e disponibilidade |
| --- | --- |
| Bing (China), Google, Lingva | Não exigem chave. A disponibilidade não é garantida. Novas instalações usam Google por padrão; as preferências existentes são mantidas. |
| Baidu, Alibaba Cloud, DeepL | Forneça suas próprias credenciais. As cotas gratuitas e as cobranças dependem do provedor e da sua conta. O Alibaba Cloud exige a ativação do serviço Machine Translation e a permissão `alimt:TranslateGeneral`. |
| Local offline | Instale o mecanismo local opcional; ele usa um serviço de loopback sem conta ou tarifa de API. |

A tradução na nuvem envia apenas o texto solicitado ao provedor selecionado, sem troca automática de provedor. O OCR não envia imagens. As credenciais dos provedores são armazenadas **em texto simples** no arquivo local `settings.json`; campos mascarados não criptografam essas informações.

<details>
<summary>Configurações iniciais de seleção e tradução</summary>

<p align="center"><img src="docs/images/translation-settings.png" width="720" alt="Configurações de captura e tradução da versão 1.2.3 com Apenas copiar, controles de resultados, Google e idioma de destino independente"></p>

</details>

### 5. Instalar componentes opcionais dentro do aplicativo

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="Configurações de componentes opcionais com botões de instalação e reparo de OCR e tradução offline"></p>

*As configurações dos componentes mostram o estado de uma nova instalação da versão 1.2.3: OCR e tradução offline não estão instalados. Use esses botões do aplicativo para instalá-los, sem comandos de terminal.*

<details>
<summary>Pastas de instalação dos componentes</summary>

<p align="center"><img src="docs/images/component-folders.png" width="720" alt="Configurações das pastas de componentes na interface em inglês da versão 1.2.3; os caminhos estão ocultos."></p>

*Configurações das pastas de componentes na interface em inglês da versão 1.2.3; os caminhos estão ocultos.*

</details>

1. Abra **Configurações > Componentes locais**.
2. Antes de usar o OCR pela primeira vez, escolha **Instalar / reparar** em **Reconhecimento de texto (chinês / inglês)**. O pacote padrão não pré-instala mecanismos nem modelos de OCR ou tradução local. A instalação iniciada explicitamente baixa e verifica os arquivos necessários e confere o runtime nativo.
3. Para tradução local, escolha **Instalar / reparar** em **Tradução offline (chinês / inglês)**. Mantenha as configurações abertas durante o download inicial; reserve aproximadamente **1 GB de espaço livre em disco** e **2 GB de memória disponível**.
4. Siga o instalador da Microsoft, se solicitado. Se a instalação informar **Reinicialização necessária**, reinicie o Windows e tente novamente. Cancelar a instalação no aplicativo não desfaz uma instalação do runtime do Windows que já tenha começado.
5. Selecione **Local offline (Argos / LibreTranslate)** como mecanismo de tradução. Mantenha `http://127.0.0.1:5000` e use **Testar tradução**. O serviço inicia quando necessário; **Iniciar serviço** e **Parar serviço** também estão disponíveis.

A instalação inicial precisa de acesso à internet aos servidores de download correspondentes do NuGet, GitHub, Python, pacotes, modelos e Microsoft. Um download que falhou pode ser tentado novamente; arquivos válidos em cache são reutilizados. Depois de instalados, o OCR e a tradução local funcionam offline. O serviço de tradução continua em execução após o aplicativo fechar, até ser interrompido.

**Idiomas compatíveis:** o OCR reconhece texto impresso em inglês e chinês simplificado. Os modelos de tradução de download opcional oferecem suporte a **inglês ↔ chinês simplificado**. Os **20 idiomas da interface** não significam que o OCR ou a tradução offline ofereçam suporte a 20 idiomas. A ausência de modelos nunca provoca uma troca para tradução na nuvem. [Instalação offline e solução de problemas](docs/OFFLINE_TRANSLATION.md).

### 6. Verificar atualizações

<p align="center"><img src="docs/images/update-check.png" width="720" alt="Janela de atualização mostrando uma versão mais recente simulada e o caminho de instalação oculto"></p>

*A publicação mostrada é simulada e o caminho de instalação está oculto. Esta imagem não anuncia uma versão disponível.*

Use **Verificar atualizações** no menu da bandeja ou em **Configurações > Sobre**. A verificação não baixa nem instala nada automaticamente. O download verifica o SHA-256 e o conteúdo do pacote; **Reiniciar e atualizar** exige uma confirmação separada. O atualizador substitui os arquivos gerenciados do aplicativo no caminho existente, preserva os dados do usuário e os componentes instalados e mantém informações de recuperação se a inicialização falhar.

O pacote organizado tem três arquivos na raiz: `KeeStashDock.exe`, `StartHere.md` e `update-manifest.json`. Os guias ficam em `Docs`, os avisos legais em `Licenses`; idiomas e componentes opcionais mantêm suas pastas dedicadas. Mantenha o pacote completo junto. [Atualizações e recuperação](docs/UPDATING.md).

<a id="shortcuts"></a>
## Atalhos

Se um atalho do painel ou de captura não puder ser registrado na inicialização, uma notificação identifica o atalho e oferece acesso às configurações, onde o estado permanece visível. Os botões e a bandeja continuam funcionando. Uma alteração que falha preserva o atalho anterior que funcionava.

| Ação | Atalho ou gesto padrão |
| --- | --- |
| Mostrar/ocultar o painel rápido | **Alt+Q**; configurável |
| Buscar no painel rápido | **Ctrl+F** enquanto o painel está ativo; **Esc** volta à captura |
| Selecionar resultados da área de trabalho do aplicativo | **Ctrl+A** com o foco na lista; seleciona apenas as linhas carregadas |
| Salvar o texto digitado | **Enter** |
| Inserir quebra de linha | **Alt+Enter** ou **Shift+Enter** |
| Captura de tela | **Alt+W**; configurável separadamente |
| Salvar a região selecionada da captura | **Enter** na barra de captura |
| Cancelar captura | **Esc** ou clique direito |
| Inspecionar o painel Espacial | Manter o **botão direito do mouse** pressionado e arrastar; configurável |
| Copiar/traduzir pelo balão de seleção | **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T**, somente enquanto o balão estiver visível e os atalhos estiverem disponíveis |

<a id="data-and-upgrades"></a>
## Dados, backup e atualizações

**Compatibilidade do banco de dados na versão 1.3.0:** o aplicativo atualiza a biblioteca para o **esquema 5**. Antes de alterar um banco antigo, cria e valida um backup consistente, incluindo os dados WAL confirmados. Se o backup falhar, a migração será interrompida. **A versão 1.2.3 e anteriores não abrem o esquema 5.** Restaurar arquivos antigos do aplicativo não reverte o formato do banco. Mantenha a pasta de dados ativa e o backup anterior à atualização. Para voltar à versão anterior, restaure uma cópia separada de um backup compatível, sem sobrescrever dados mais recentes.

Os temas ficam em `theme/`, ao lado do executável: `builtin/` contém recursos gerenciados pelo aplicativo e `custom/` contém temas importados pelo usuário. As atualizações preservam `theme/custom/`. **Backups do banco de dados não incluem temas.** Use a exportação de temas nas configurações para transferir ou guardar separadamente a aparência clara/escura completa e suas imagens.

As configurações mostram o último backup concluído, a última verificação, a próxima verificação, o estado, o local e a quantidade de snapshots. Use **Fazer backup agora**, abra a pasta ou restaure pela lista com o fluxo protegido. Intervalos: **15 / 30 / 60 minutos**, 6 / 12 / 24 / 48 horas ou 7 dias; o padrão permanece **24 horas**. Intervalos personalizados e retenção existentes são preservados. Sete snapshots a cada 15 minutos cobrem apenas **90 minutos** entre o mais antigo e o mais recente; escolha a retenção considerando o espaço estimado. Cada backup agendado cria um snapshot completo mesmo sem alterações no banco; intervalos curtos renovam o histórico mantido mais rapidamente. O backup cobre o banco de entradas e suas imagens, **não todas as configurações, componentes ou fundos**. A limpeza automática não remove exportações manuais nem cópias de recuperação.

<details>
<summary>Configurações de backup · a cada 24 horas por padrão</summary>

<p align="center"><img src="docs/images/backup-settings.png" width="720" alt="Configurações de dados com backups automáticos a cada 24 horas e caminhos ocultos"></p>

<p align="center"><img src="docs/images/backup-status.png" width="720" alt="Status de backup, instantâneos e ações manuais com dados de exemplo isolados. Os caminhos estão ocultos; o intervalo padrão permanece em 24 horas."></p>

*Status de backup, instantâneos e ações manuais com dados de exemplo isolados. Os caminhos estão ocultos; o intervalo padrão permanece em 24 horas.*

</details>

<details>
<summary>Sobre · versão do aplicativo e localização dos dados</summary>

<p align="center"><img src="docs/images/about-settings.png" width="720" alt="Página Sobre da versão 1.2.3 com informações do aplicativo e caminho de dados oculto"></p>

</details>

Entradas, imagens, textos reconhecidos e backups são armazenados localmente. Os dados portáteis normalmente ficam em `Data`, ao lado do executável. Se não for possível gravar nessa pasta, o aplicativo usa `%AppData%\KeeStashDock` e informa o local alternativo nas configurações. Componentes instalados pelo aplicativo normalmente usam `%LOCALAPPDATA%\KeeStashDock`, separados da sua coleção.

Use **Configurações > Backup e dados** para exportar ou importar um backup e configurar backups agendados. A primeira atualização a partir da **1.2.1 é manual**:

1. Exporte um backup e saia do aplicativo pelo menu da bandeja.
2. Extraia o novo ZIP para uma pasta separada. Com o aplicativo fechado, copie **toda a pasta de dados ativa** para a pasta `Data` da nova instalação. Se as configurações indicarem um local alternativo em `%AppData%`, use essa pasta como origem.
3. Mantenha a instalação original e o backup. Inicie a nova versão e confira suas entradas antes de remover as cópias antigas. Guarde um backup anterior à atualização para poder voltar à versão anterior; esquemas antigos de banco de dados não são intercambiáveis.
4. Se **Iniciar com o Windows** estava ativado e você mudou de pasta, desative e ative novamente a opção nas configurações do novo aplicativo. Isso registra o novo caminho; caso contrário, o Windows poderá iniciar a cópia antiga no próximo login.

Na **1.2.2**, use **Verificar atualizações** ou siga as etapas manuais acima. As **versões candidatas 1.3.0 (r1–r5)** precisam de substituição manual porque compartilham o número da versão final. Preserve toda a pasta de dados ativa e os backups; encerre o processo antigo antes de iniciar a nova cópia.

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

O ícone e as ilustrações dos temas incluídos são recursos gerados por IA. Atribuição e licenças: [ícone](docs/assets/README.md), [Sunlit Gallery](theme/builtin/sunlit-gallery/SOURCE.md), [Twilight Study](theme/builtin/twilight-study/SOURCE.md), [Wisteria Atelier](theme/builtin/wisteria-atelier/SOURCE.md), [Wisteria Reverie](theme/builtin/wisteria-reverie/SOURCE.md).
