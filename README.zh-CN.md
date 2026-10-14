<h1 align="center">KeeStashDock（文本坞）</h1>

<p align="center">
  <img src="docs/assets/app-icon.png" width="112" alt="KeeStashDock 应用图标">
</p>

<p align="center">
  <strong>用于收纳文本、截图和日常复用的 Windows 便携工具。</strong><br>
  保存片段、框选截图、识别文字，需要翻译时再调用划词气泡。
</p>

<p align="center">
  <a href="README.md">English</a> ·
  <a href="README.zh-CN.md">简体中文</a> ·
  <a href="README.zh-TW.md">繁體中文</a> ·
  <a href="README.ja-JP.md">日本語</a>
</p>

<p align="center">
  <img alt="版本 1.2.1" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 与 11" src="docs/assets/badges/windows.svg">
  <img alt="许可证：Apache 2.0" src="docs/assets/badges/license.svg">
</p>

当前已发布版本为 **1.2.1**。本分支正在准备 **1.2.2**，新增功能的验收进度见[候选版验收记录](docs/releases/1.2.2.md)。正式发布前，下方下载链接仍指向 1.2.1。

## 1.2.2 候选版新增功能

- **经典 / 悬浮模式：**在设置中切换。两种模式都提供柔和投影；悬浮模式增加鼠标靠近、按压和拖动反馈，并遵循 Windows 减少动画与高对比度设置。
- **截图三入口：**框选后选择保存截图、提取文字或提取并翻译。Enter 保存，Esc 或右键取消。仅提取文字不会生成条目，也不会自动复制。
- **复制与翻译并列：**划词气泡可同时显示两个独立按钮。复制仍会收纳到文本坞，翻译使用同一选区原文且不会新增条目。
- **检查更新：**位于托盘菜单和设置的“关于”。点击后下载并校验 SHA-256，再确认重启升级；只替换受清单管理的应用文件。详情见[升级与恢复说明](docs/UPDATING.zh-CN.md)。

候选版预览：[经典面板](docs/images/classic-panel.png) · [悬浮面板](docs/images/floating-panel.png) · [复制 / 翻译](docs/images/selection-actions.png) · [提取文字](docs/images/screenshot-text.png) · [检查更新](docs/images/update-check.png)。[图片来源说明](docs/images/README.md)。

## 下载与启动

**[最新发布版本](https://github.com/Keegankei/KeeStashDock/releases/latest)** ·
**[下载 1.2.1 Windows x64 版](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip)** ·
[SHA-256 校验文件](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip.sha256)

1. 从发布页面的 **Assets** 下载应用 ZIP。GitHub 自动生成的 **Source code** 压缩包用于源码开发。
2. 将 ZIP 完整解压到可写目录，运行 `KeeStashDock.exe`。请保留所有随附文件和文件夹。
3. 按 **Alt+Q** 打开速览面板，也可通过 Windows 系统托盘访问应用。
4. 打开“设置”，选择语言、外观和所需组件。

发布包面向 **Windows x64**，已包含 .NET 运行时。推荐 Windows 11；Windows 10 22H2 仅提供尽力兼容。OCR 和离线翻译可能需要 Microsoft Visual C++ 运行库，可通过软件内的组件安装功能补齐。测试范围与已知限制见[更新日志](CHANGELOG.md)和[验收记录](docs/releases/1.2.1.md)。

<details>
<summary>校验下载文件（可选）</summary>

在下载目录打开 PowerShell，运行以下命令，将结果与随附 `.sha256` 文件比较：

```powershell
Get-FileHash .\KeeStashDock-1.2.1-win-x64.zip -Algorithm SHA256
```

</details>

## 界面预览

### 主界面

![显示示例片段的 KeeStashDock 主界面](docs/images/main-window.png)

快速查找、钉选、分组和编辑常用内容，也可从回收站恢复条目。

### 截图条目

![截图原图预览与可编辑的识别文字](docs/images/screenshot-entry.png)

原图和识别文字保存在同一条目中。OCR 按需执行，翻译使用已有划词气泡。

### 组件安装

![设置中的 OCR 与离线翻译安装管理界面](docs/images/component-setup.png)

在设置中安装、修复和管理可选组件。以上截图均使用演示内容。

## 日常使用

| 操作 | 使用方法 |
| --- | --- |
| 显示或隐藏速览面板 | **Alt+Q**，可在设置中修改 |
| 保存输入文字 | 在快速输入框中输入，按 **Enter** |
| 输入换行 | **Alt+Enter** 或 **Shift+Enter** |
| 复制文本条目 | 点击卡片；可按需开启直接粘贴 |
| 框选截图 | 截图按钮或 **Ctrl+Alt+S**；拖动选区，按 **Esc** 或右键取消 |
| 打开图片条目 | 点击卡片，预览、复制、另存或识别文字 |
| 管理条目 | 在工作台中搜索、分组、加标签、钉选、编辑和恢复 |
| 翻译选中文字 | 在设置中启用划词气泡，选择“翻译”动作及翻译服务 |

还支持悬浮便签、二维码、文本变换、分组导入导出、保留策略和可选剪贴板历史。剪贴板历史与直接粘贴默认关闭。界面提供 20 个语言包，以及浅色、深色和跟随系统主题。

## 首次安装 OCR 与离线翻译

正常使用只需点击软件内的按钮，无需运行终端命令。

1. 打开 **设置 > 可选组件**。
2. 若 OCR 提示缺少组件，在 **文字识别（中文 / 英文）** 下点击 **安装 / 修复**。标准 ZIP 已包含引擎和英语、简体中文模型；安装功能也会检查原生运行库。
3. 需要离线翻译时，在 **离线翻译（中文 / 英文）** 下点击 **安装 / 修复**。等待下载、校验结束，期间保持设置窗口打开。建议预留约 **1 GB 磁盘空间**和 **2 GB 可用内存**。
4. 若 Windows 要求授权安装微软运行库，请按安装程序提示操作。若软件提示需要重启，请重启 Windows 后重新安装组件。取消软件内的任务不会撤销已开始的 Windows 运行库安装。
5. 将翻译引擎切换为“本地离线”，保留地址 `http://127.0.0.1:5000`，点击“测试翻译”。引擎安装后可在需要时启动；设置中也提供“启动服务”和“停止服务”。

首次下载会按所选组件访问 GitHub、python.org、files.pythonhosted.org、argos-net.com，必要时访问微软下载站点；部分网络可能无法访问这些站点。失败后可重试，有效缓存会复用。安装完成后，文字识别和本地翻译可离线使用。关闭应用后，翻译服务仍会运行，直到手动停止。

OCR 支持印刷体**英语和简体中文**。随附翻译模型支持**英语 ↔ 简体中文**，不涵盖繁体中文、日语或全部界面语言。OCR 不会自动翻译；请选中识别结果后使用划词气泡。缺少模型时不会自动切换至云端。

详见 [OCR 安装与限制](docs/LOCAL_OCR.md)、[离线翻译安装与排障](docs/OFFLINE_TRANSLATION.md)及[截图流程](docs/SCREENSHOTS.md)。详细工程文档使用英文。

## 翻译选择与隐私

- **Bing 中国入口、Google、Lingva：** 无需密钥，但不保证长期可用。新安装默认使用 Bing，已有服务设置保留。
- **百度、阿里云、DeepL：** 使用自己的服务凭据，免费额度及费用取决于账户。阿里云需开通机器翻译，并具备 `alimt:TranslateGeneral` 权限。
- **本地离线：** 通过本机回环地址调用已安装的引擎，无需账户或 API 费用。

文本、图片、识别结果和备份保存在本机，OCR 不上传图片。云端翻译只将所选文本发送给已选服务商，不会自动切换服务。服务凭据以**明文**存储在本机 `settings.json` 中，输入框遮挡不代表加密。向外部服务提交内容前，请确认可以接受相应披露。

离线翻译仅接受本机回环地址，绕过 HTTP 代理并拒绝重定向，随附引擎在翻译时不会下载模型。私密报告漏洞的方法见[安全策略](SECURITY.md)。

## 数据、备份与升级

数据通常保存在程序旁的 `Data` 文件夹中，包括数据库、设置、草稿、日志和备份。程序目录不可写时使用 `%AppData%\KeeStashDock`，设置界面会提示。软件内安装的组件通常保存在 `%LOCALAPPDATA%\KeeStashDock`，与条目数据分开。

升级前：

1. 在 **设置 > 数据** 导出备份，然后从托盘菜单退出 KeeStashDock。
2. 将新版 ZIP 解压到新文件夹。若使用便携数据目录，请在软件关闭时将旧版的**整个 `Data` 文件夹**复制到新版目录，保留旧版作为回退副本。
3. 若设置提示数据位于 `%AppData%`，也要备份该目录。迁移至可写的便携目录时，应在首次启动前将其完整内容复制到新版 `Data` 文件夹，否则可能显示空白资料库。
4. 启动新版并检查条目，确认前保留旧版与备份。需要回退时使用升级前备份，不要让旧版直接打开升级后的数据库。

请勿在 Issue 中上传真实 `Data` 文件夹、凭据、私密备份或日志；[支持指南](SUPPORT.md)说明了如何提交脱敏报告。

## 构建与贡献

使用 Windows 和 `global.json` 指定的 **.NET SDK**（10.0.401 或兼容的 10.0.4xx 补丁版）：

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore --disable-build-servers
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

开发准备、辅助组件测试、代码约定及发布检查见[贡献指南](CONTRIBUTING.md)。源码与通用工程文档使用英文，多语言 `README.<locale>.md` 和界面语言包为例外；译文有差异时以英文文档为准。

[更新日志](CHANGELOG.md) · [架构](docs/ARCHITECTURE.md) · [本地化](docs/LOCALIZATION.md) · [发布流程](docs/RELEASING.md) · [行为准则](CODE_OF_CONDUCT.md) · [第三方声明](THIRD_PARTY_NOTICES.md)

## 许可证

Copyright © 2026 KeeganKei。项目采用 [Apache License 2.0](LICENSE)，署名见 [NOTICE](NOTICE)。依赖、可选运行时和模型遵循各自许可证，详见[第三方声明](THIRD_PARTY_NOTICES.md)。项目许可证不授予商标权。

应用图标由维护者指导 AI 生成，详见[设计与来源记录](docs/assets/README.md)。
