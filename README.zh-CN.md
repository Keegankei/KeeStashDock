<h1 align="center">KeeStashDock</h1>

<p align="center">
  <img src="docs/assets/app-icon.png" width="112" alt="KeeStashDock 应用图标">
</p>

<p align="center">
  <strong>便携式 Windows 收纳面板，用于保存文本片段、截图和复用文字。</strong><br>
  收集所需内容，在本地整理，并在需要时主动翻译。
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
  <img alt="版本 1.2.2" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 和 11" src="docs/assets/badges/windows.svg">
  <img alt="Apache-2.0 许可证" src="docs/assets/badges/license.svg">
</p>

> **1.2.2 正式发布准备：**本指南介绍 **1.2.2**。GitHub Release 发布后，下方 ZIP 和校验文件链接才会生效。更新内容、已验证范围与剩余限制见[发布说明](docs/releases/1.2.2-notes.md)和[验收记录](docs/releases/1.2.2.md)。

[快速开始](#get-started) · [功能介绍](#features) · [快捷操作](#shortcuts) · [数据与升级](#data-and-upgrades) · [开发](#development) · [许可证](#license)

<a id="get-started"></a>
## 快速开始

**[最新发布版本](https://github.com/Keegankei/KeeStashDock/releases/latest)** · **[Windows x64 ZIP — 1.2.2](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.2/KeeStashDock-1.2.2-win-x64.zip)** · [SHA-256 校验文件](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.2/KeeStashDock-1.2.2-win-x64.zip.sha256)

1. 在发布页的 **Assets** 中下载应用 ZIP；GitHub 的 **Source code** 压缩包面向开发者。
2. 将完整软件包解压到可写文件夹，运行 `KeeStashDock.exe`。请保留所有附带文件夹。
3. 按 **Alt+Q** 显示快速面板。输入文本片段后按 **Enter** 保存；点击卡片即可复制。
4. 从面板或系统托盘打开**设置**，选择语言、主题和可选组件。

Windows x64 软件包已包含 .NET，无需另外安装。主要支持 Windows 11，并尽力兼容 Windows 10 22H2。OCR 和离线翻译可能需要 Microsoft Visual C++ 运行库，可通过应用内组件安装流程安装。已验证的平台范围见 [1.2.2 验收记录](docs/releases/1.2.2.md)。

<details>
<summary>校验下载的 ZIP</summary>

在下载文件夹中运行以下命令，将结果与附带的 `.sha256` 文件比较：

```powershell
Get-FileHash .\KeeStashDock-1.2.2-win-x64.zip -Algorithm SHA256
```

校验和用于检查文件完整性，不是发布者签名。当前软件包未签名。

</details>

**全新安装默认设置 · 1.2.2：**界面为英文；**Alt+Q** 打开面板，**Alt+W** 截图。不预装 OCR 和本地翻译。升级会保留已有设置。

划词气泡默认开启，动作为**仅复制**，翻译引擎为 **Google**；气泡采用默认样式，关闭文字发光、开启悬浮投影。开机启动、自动收集剪贴板和直接粘贴默认关闭。贴边自动收纳默认开启，收起延时 **2 秒**、展开延时 **0.1 秒**。外观跟随系统，使用**标准**模式；空间模式的检视方式预设为**按住右键拖动**。面板文字配色自动，控件不透明度 **90%**；无背景图，蒙层不透明度 **20%**，毛玻璃关闭，预设半径 **4 px**。定时自动备份默认开启，每 **24 小时**运行。

<a id="features"></a>
## 功能介绍

以下预览展示实际应用控件，内容为合成示例。检视动图使用这些控件与模拟指针生成；更新预览使用模拟版本。图片不包含个人桌面或剪贴板内容。[图片来源与生成方式](docs/images/README.md)。 图片可能展示早期候选版设置，与上述全新安装默认值不同。

### 1. 收集、查找与整理

<p align="center"><img src="docs/images/main-window.png" width="900" alt="工作台展示搜索、分组、置顶文本片段和示例图片条目"></p>

使用快速面板随手收集内容，在**工作台**管理收藏。点击面板的展开按钮或使用托盘菜单打开工作台。

| 工具 | 可以完成的操作 |
| --- | --- |
| 快速输入与搜索 | 按 Enter 保存文字；用 Alt+Enter 或 Shift+Enter 换行；输入搜索内容时实时筛选条目。 |
| 拖入面板 | 拖入文字即可保存，拖入文件或文件夹则保存其路径。点击路径卡片打开目标；右键选择**复制**可复制路径。不会导入文件内容。 |
| 分组、标签与置顶 | 将相关条目放在一起，编辑标题、正文和标签，并置顶常用内容。 |
| 有效期与回收站 | 可选永不过期、一天、七天或阅后即焚；过期和删除的条目进入回收站，可以恢复。回收站条目默认在 30 天后永久删除。 |
| 桌面便签 | 将文本或路径卡片拖出面板，在本次运行期间作为独立桌面便签显示。退出应用会关闭这些便签，但已保存的条目仍保留在收藏中。图片卡片仍作为图片条目使用。 |
| 二维码 | 右键点击文本卡片，将最多 1,800 个字符展示为二维码。 |
| 文本工具 | 使用工作台的转换菜单进行大小写转换、空白清理、JSON 格式化、URL 编解码和 Base64 处理。 |
| 分组导入导出 | 分享 `.ksdgrp` 包，或加载 Git、Docker、Shell 等工具的命令预设。含图片的包需要兼容的应用版本。 |
| 剪贴板历史 | 可选自动收集复制的**文字**；忽略空文本和超大文本，不重复添加相同内容。默认关闭。 |
| 直接粘贴 | 在能够安全确认目标窗口时，可选将复制的文本条目粘贴到先前应用。默认关闭。 |

### 2. 标准与空间面板 · 1.2.2

打开**设置 > 面板模式**进行选择。两种模式使用相同的控件、字体、配色和卡片布局。

<table>
  <tr><th>标准</th><th>空间</th></tr>
  <tr>
    <td><img src="docs/images/classic-panel.png" width="290" alt="标准面板采用平面视图和柔和阴影"></td>
    <td><img src="docs/images/floating-panel.png" width="290" alt="空间面板具有透视效果、层叠卡片和纤细侧边"></td>
  </tr>
  <tr><td>稳定的平面显示，配以柔和阴影。</td><td>透视效果、独立卡片反馈，以及清晰的层次深度。</td></tr>
</table>

<p align="center"><img src="docs/images/floating-panel-motion.gif" width="400" alt="空间模式：先展示卡片靠近反馈，再按住拖动检视"></p>

*白色指针展示靠近卡片时的反馈；橙色指针表示按住拖动检视。这是效果演示动图，不是帧率测试。*

- **固定透视：**保持稳定的倾斜视角，附近卡片仍响应指针。
- **拖动检视：**在卡片正文或面板背景上按住**鼠标右键**拖动，松开后保留角度。不拖动的右键单击仍打开卡片菜单。设置中也可选择中键或自定义鼠标键与修饰键组合。
- **紧凑栏：**将面板收起为六个操作：最小化、展开/收起、锁定、截图、工作台和设置。展开时恢复之前的面板大小。
- **桌面边缘隐藏：**1.2.2 默认开启；将面板拖到屏幕任意一侧边缘，再移开鼠标。边缘会保留一个小把手，悬停或点击即可恢复。锁定面板可使其保持显示。

支持浅色、深色和跟随系统主题，可设置自定义背景、透明度和磨砂效果。系统高对比度及减少动画的偏好会启用静态回退显示。[深色预览](docs/images/floating-panel-dark.png) · [固定视角预览](docs/images/floating-panel-fixed.png) · [性能测量与限制](docs/performance/1.2.2-floating.md)。

设置还提供面板文字配色方案、自定义文字颜色和自动对比度，以及独立的划词气泡样式、发光、阴影和透明度控制。

### 3. 截图与本地文字识别

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="已保存的截图条目，包含原图和可编辑的识别文字"></p>

按 **Alt+W** 或点击截图按钮，拖动选择区域。保存的截图会成为当前所选分组中的图片条目。打开条目可预览、复制、另存为 PNG，或**识别文字**。保留原图的同时，可以编辑和搜索识别结果。

**1.2.2：**选区完成后，工具栏提供三项独立操作：

| 操作 | 结果 |
| --- | --- |
| 保存截图 | 保存为图片条目。按 Enter 执行此操作。 |
| 提取文字 | 运行本地 OCR 并显示临时文字，不保存条目，也不自动复制。 |
| 提取并翻译 | 先运行本地 OCR，再将识别文字交给所选翻译服务。截图本身不会上传。 |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="1.2.2的临时 OCR 结果，提供明确的文字复制操作"></p>

再次拖动可以重新选择区域；按 **Esc** 或右键取消。临时结果支持主动复制，翻译失败后保留原文，并可重试翻译。OCR 仅负责识别，不会自动翻译。[截图使用流程](docs/SCREENSHOTS.md) · [OCR 支持范围与限制](docs/LOCAL_OCR.md)。

### 4. 划词操作与翻译

<p align="center"><img src="docs/images/selection-actions.png" width="480" alt="1.2.2 划词气泡，同时提供独立的复制和翻译按钮"></p>

在**设置 > 行为**中启用划词气泡，然后在其他应用中选择文字。**复制**会将所选文字保存到面板当前分组；**翻译**显示译文，不添加条目。

**1.2.2：**选择**复制和翻译**，即可同时显示两个按钮。复制后，在气泡关闭前仍可翻译同一段选中文字。翻译失败时会保留原文。

翻译目标跟随应用的界面语言。可用目标语言取决于所选服务商或已安装的离线模型。

| 服务 | 设置与可用性 |
| --- | --- |
| Bing（中国）、Google、Lingva | 无需密钥，但不保证可用性。新安装默认使用 Google，已有偏好会保留。 |
| 百度、阿里云、DeepL | 使用自己的凭据。免费额度和费用取决于服务商及账户。阿里云需要开通机器翻译服务，并授予 `alimt:TranslateGeneral` 权限。 |
| 本地离线 | 安装可选的本地引擎，通过回环地址运行服务，无需账户或 API 费用。 |

云翻译仅将主动请求翻译的文字发送给所选服务，不会自动切换服务商。OCR 不上传图片。服务凭据以**明文**保存在本地 `settings.json` 中；输入框的遮掩显示不代表加密。

### 5. 在应用内安装可选组件

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="可选组件设置，提供 OCR 和离线翻译的安装及修复按钮"></p>

*图中为 1.2.1 组件尚未安装的示例状态。正常设置通过应用内按钮完成，无需终端命令。*

1. 打开**设置 > 可选组件**。
2. 首次使用 OCR 前，在**文字识别（中文 / 英文）**下选择**安装 / 修复**。1.2.2 不预装 OCR 或本地翻译引擎及模型；主动安装时才下载并验证所需文件，同时检查本机运行库。
3. 本地翻译请在**离线翻译（中文 / 英文）**下选择**安装 / 修复**。首次下载期间保持设置窗口打开，并预留约 **1 GB 磁盘空间**和 **2 GB 可用内存**。
4. 如出现 Microsoft 安装程序，按提示操作。如提示**需要重启**，重启 Windows 后重试。取消应用组件安装不会撤销已开始的 Windows 运行库安装。
5. 将翻译引擎设为**本地离线（Argos / LibreTranslate）**，保留 `http://127.0.0.1:5000`，然后使用**测试翻译**。服务会在需要时启动，也可手动选择**启动服务**和**停止服务**。

首次设置需要访问相关 NuGet、GitHub、Python、软件包、模型及 Microsoft 下载站点。下载失败后可重试，已验证的缓存文件会被复用。安装完成后，OCR 和本地翻译可以离线运行。翻译服务在应用关闭后仍会运行，直到主动停止。

**语言支持：**OCR 支持打印体英语和简体中文。可选下载的翻译模型支持**英语 ↔ 简体中文**。应用的 **20 种界面语言**不代表 OCR 或离线翻译支持 20 种语言。缺少模型时不会自动切换到云翻译。[离线安装与故障排查](docs/OFFLINE_TRANSLATION.md)。

### 6. 检查更新 · 1.2.2

<p align="center"><img src="docs/images/update-check.png" width="720" alt="更新窗口展示模拟的新版本和示例安装路径"></p>

*图中的版本与路径仅供演示，不表示已有该版本可供下载。*

在托盘菜单或**设置 > 关于**中选择**检查更新**。检查不会自动下载或安装。下载时会验证 SHA-256 和软件包内容；**重启并升级**需要单独确认。更新程序在原有路径替换受管理的应用文件，保留用户数据和已安装组件，并在启动失败时保留恢复信息。

整理后的软件包根目录只有三个文件：`KeeStashDock.exe`、`StartHere.md` 和 `update-manifest.json`。使用说明位于 `Docs`，版权声明位于 `Licenses`，语言与可选组件各有专用文件夹。请完整保留软件包。[更新与恢复](docs/UPDATING.md)。

<a id="shortcuts"></a>
## 快捷操作

| 操作 | 默认快捷键或手势 |
| --- | --- |
| 显示/隐藏快速面板 | **Alt+Q**，可自定义 |
| 保存输入的文字 | **Enter** |
| 换行 | **Alt+Enter** 或 **Shift+Enter** |
| 截图 | **Alt+W**，可单独配置 |
| 保存截图选区 | 在 1.2.2 截图工具栏中按 **Enter** |
| 取消截图 | **Esc** 或右键 |
| 检视空间面板 | 按住**鼠标右键**拖动，1.2.2 中可自定义 |
| 划词气泡复制/翻译 | 1.2.2 中使用 **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T**，仅在气泡可见且快捷键可用时生效 |

<a id="data-and-upgrades"></a>
## 数据、备份与升级

条目、图片、识别文字和备份均保存在本地。便携数据通常位于可执行文件旁的 `Data` 文件夹。如该位置不可写，应用会改用 `%AppData%\KeeStashDock`，并在设置中提示。通过应用安装的组件通常位于 `%LOCALAPPDATA%\KeeStashDock`，与内容收藏分开存放。

在**设置 > 数据**中导出/导入备份并配置定时备份。从 **1.2.1 首次升级需要手动进行**：

1. 导出备份，然后通过托盘菜单退出应用。
2. 将新 ZIP 解压到独立文件夹。在应用关闭时，将**当前使用的完整数据文件夹**复制到新安装的 `Data` 文件夹。如果设置显示使用 `%AppData%` 回退位置，请以该文件夹为来源。
3. 保留原安装和备份，启动新版本并确认条目完整后再清理旧副本。保留升级前的备份以便回退；不同版本的数据库结构不能随意互换。
4. 如果原来开启了**开机启动**，且此次迁移到新目录，请在新版设置中先关闭再开启该选项，以登记新的程序路径；否则下次登录 Windows 时可能启动旧版。

较早的 1.2.2 候选版与正式版使用相同数字版本号，也需要手动替换。请保留数据和备份，并按相同流程迁移。

请勿在问题报告中附上个人 `Data`、凭据、私密备份或日志。参阅[支持与脱敏报告](SUPPORT.md)、[升级恢复](docs/UPDATING.md)和[安全政策](SECURITY.md)。

<a id="development"></a>
## 开发与文档

在 Windows 上使用 [`global.json`](global.json) 指定的 SDK（10.0.401 或兼容的 10.0.4xx 补丁版本）构建：

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore --disable-build-servers
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

仓库文档以英文版为准。各语种 README 使用一致的功能结构；界面翻译位于 `Locales/*.json`。欢迎按照[贡献指南](CONTRIBUTING.md)提交翻译修正。

| 文档 | 内容 |
| --- | --- |
| [更新日志](CHANGELOG.md) | 版本历史 |
| [架构](docs/ARCHITECTURE.md) | 项目、职责与存储 |
| [本地化](docs/LOCALIZATION.md) | 界面和 README 的翻译约定 |
| [发布流程](docs/RELEASING.md) | 构建、打包与验收检查 |
| [支持](SUPPORT.md) · [行为准则](CODE_OF_CONDUCT.md) | 提问、报告与社区参与 |
| [第三方声明](THIRD_PARTY_NOTICES.md) | 依赖、运行库和模型的许可证 |

<a id="license"></a>
## 许可证

Copyright © 2026 KeeganKei。采用 [Apache-2.0](LICENSE) 许可证；署名信息见 [NOTICE](NOTICE)。依赖、可选运行库和模型保留各自许可证，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。项目许可证不授予商标权。

应用图标由维护者指导 AI 生成，其设计与来源记录见 [docs/assets/README.md](docs/assets/README.md)。
