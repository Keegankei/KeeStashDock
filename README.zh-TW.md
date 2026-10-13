<h1 align="center">KeeStashDock</h1>

<p align="center">
  <img src="docs/assets/app-icon.png" width="112" alt="KeeStashDock 應用程式圖示">
</p>

<p align="center">
  <strong>用於收納文字、螢幕擷取與日常重複使用的 Windows 可攜式工具。</strong><br>
  儲存片段、擷取畫面、辨識文字，需要翻譯時再使用選字氣泡。
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
  <img alt="Windows 10 與 11" src="docs/assets/badges/windows.svg">
  <img alt="授權條款：Apache 2.0" src="docs/assets/badges/license.svg">
</p>

目前穩定版本為 **1.2.1**。專案進入按需維護階段，後續以錯誤修正和安全性更新為主。

## 下載與啟動

**[最新發行版本](https://github.com/Keegankei/KeeStashDock/releases/latest)** ·
**[下載 1.2.1 Windows x64 版](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip)** ·
[SHA-256 校驗檔案](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip.sha256)

1. 從發行頁面的 **Assets** 下載應用程式 ZIP。GitHub 自動產生的 **Source code** 壓縮檔供原始碼開發使用。
2. 將 ZIP 完整解壓縮至可寫入的資料夾，執行 `KeeStashDock.exe`。請保留所有隨附檔案與資料夾。
3. 按 **Alt+Q** 開啟快速面板，也可從 Windows 系統匣開啟應用程式。
4. 開啟「設定」，選擇語言、外觀與所需元件。

發行套件適用於 **Windows x64**，已包含 .NET 執行階段。建議使用 Windows 11；Windows 10 22H2 僅提供盡力相容。OCR 與離線翻譯可能需要 Microsoft Visual C++ 執行階段，可透過應用程式內的元件安裝功能補齊。測試範圍與限制請見[更新紀錄](CHANGELOG.md)及[驗收紀錄](docs/releases/1.2.1.md)。

<details>
<summary>校驗下載檔案（選用）</summary>

在下載資料夾開啟 PowerShell，執行下列命令，將結果與隨附 `.sha256` 檔案比對：

```powershell
Get-FileHash .\KeeStashDock-1.2.1-win-x64.zip -Algorithm SHA256
```

</details>

## 介面預覽

### 主介面

![顯示範例片段的 KeeStashDock 主介面](docs/images/main-window.png)

快速搜尋、釘選、分組及編輯常用內容，也可從資源回收筒還原條目。

### 螢幕擷取條目

![原圖預覽與可編輯的辨識文字](docs/images/screenshot-entry.png)

原圖與辨識文字儲存在同一條目。OCR 按需執行，翻譯使用既有的選字氣泡。

### 元件安裝

![設定中的 OCR 與離線翻譯安裝管理介面](docs/images/component-setup.png)

在設定中安裝、修復與管理選用元件。以上畫面皆使用示範內容。

## 日常使用

| 操作 | 使用方法 |
| --- | --- |
| 顯示或隱藏快速面板 | **Alt+Q**，可在設定中修改 |
| 儲存輸入文字 | 在快速輸入框輸入後按 **Enter** |
| 輸入換行 | **Alt+Enter** 或 **Shift+Enter** |
| 複製文字條目 | 按一下卡片；可依需要開啟直接貼上 |
| 擷取畫面區域 | 擷取按鈕或 **Ctrl+Alt+S**；拖曳選取，按 **Esc** 或按滑鼠右鍵取消 |
| 開啟圖片條目 | 按一下卡片，預覽、複製、另存或辨識文字 |
| 管理條目 | 在工作台搜尋、分組、加標籤、釘選、編輯與還原 |
| 翻譯選取文字 | 在設定中啟用選字氣泡，選擇「翻譯」動作及翻譯服務 |

另支援浮動便箋、QR Code、文字轉換、分組匯入匯出、保留策略與選用的剪貼簿歷程記錄。剪貼簿歷程記錄及直接貼上預設關閉。介面提供 20 個語言套件，以及淺色、深色和跟隨系統主題。

## 首次安裝 OCR 與離線翻譯

一般使用只需按應用程式內的按鈕，無須執行終端機命令。

1. 開啟 **設定 > 選用元件**。
2. 若 OCR 提示缺少元件，在 **文字辨識（中文 / 英文）** 下按 **安裝 / 修復**。標準 ZIP 已含引擎與英文、簡體中文模型；安裝功能也會檢查原生執行階段。
3. 需要離線翻譯時，在 **離線翻譯（中文 / 英文）** 下按 **安裝 / 修復**。下載與驗證期間保持設定視窗開啟，並預留約 **1 GB 磁碟空間**與 **2 GB 可用記憶體**。
4. 若 Windows 要求授權安裝微軟執行階段，請依安裝程式提示操作。若顯示需要重新啟動，請重新啟動 Windows 後再安裝元件。取消應用程式內的工作不會復原已開始的 Windows 執行階段安裝。
5. 將翻譯引擎切換為「本機離線」，保留位址 `http://127.0.0.1:5000` 並測試翻譯。引擎安裝後可在需要時啟動；設定中也提供「啟動服務」與「停止服務」。

首次下載會依所選元件存取 GitHub、python.org、files.pythonhosted.org、argos-net.com，必要時存取微軟下載站點。部分網路可能無法連線至這些站點。失敗後可重試，已驗證的快取會重複使用。安裝完成後，文字辨識與本機翻譯可離線使用。關閉應用程式後，翻譯服務仍會執行，直到手動停止。

OCR 支援印刷體**英文與簡體中文**。隨附翻譯模型支援**英文 ↔ 簡體中文**，不包含繁體中文、日文或全部介面語言。OCR 不會自動翻譯；請選取辨識結果後使用選字氣泡。缺少模型時不會自動切換至雲端。

詳見 [OCR 安裝與限制](docs/LOCAL_OCR.md)、[離線翻譯安裝與疑難排解](docs/OFFLINE_TRANSLATION.md)及[螢幕擷取流程](docs/SCREENSHOTS.md)。詳細專案文件使用英文。

## 翻譯選擇與隱私

- **Bing 中國入口、Google、Lingva：** 無須金鑰，但不保證長期可用。新安裝預設使用 Bing，既有服務設定保留。
- **百度、阿里雲、DeepL：** 使用自己的服務憑證，免費額度及費用依帳戶規定。阿里雲需開通機器翻譯並具備 `alimt:TranslateGeneral` 權限。
- **本機離線：** 透過本機回送位址呼叫已安裝引擎，無須帳戶或 API 費用。

文字、圖片、辨識結果與備份儲存在本機，OCR 不上傳圖片。雲端翻譯只將選取文字傳送給已選服務商，不會自動切換服務。服務憑證以**明文**儲存在本機 `settings.json`，輸入框遮蔽不代表加密。將內容傳送至外部服務前，請確認可以接受相應揭露。

離線翻譯僅接受本機回送位址，略過 HTTP Proxy 並拒絕重新導向，隨附引擎翻譯時不會下載模型。私下通報漏洞的方法請見[安全性原則](SECURITY.md)。

## 資料、備份與升級

資料通常儲存在程式旁的 `Data` 資料夾，包含資料庫、設定、草稿、記錄與備份。程式資料夾無法寫入時使用 `%AppData%\KeeStashDock`，設定介面會提示。應用程式內安裝的元件通常位於 `%LOCALAPPDATA%\KeeStashDock`，與條目資料分開。

升級前：

1. 在 **設定 > 資料** 匯出備份，然後從系統匣選單結束 KeeStashDock。
2. 將新版 ZIP 解壓縮到新資料夾。若使用可攜式資料目錄，請在程式關閉時將舊版的**整個 `Data` 資料夾**複製到新版目錄，保留舊版作為還原副本。
3. 若設定提示資料位於 `%AppData%`，也要備份該目錄。移至可寫入的可攜式目錄時，請在首次啟動前將其完整內容複製到新版 `Data` 資料夾，否則可能顯示空白資料庫。
4. 啟動新版並檢查條目，確認前保留舊版與備份。回復舊版時使用升級前的備份，不要讓舊版直接開啟升級後的資料庫。

請勿在 Issue 上傳真實 `Data` 資料夾、憑證、私人備份或記錄；[支援指南](SUPPORT.md)說明如何提交去識別化報告。

## 建置與貢獻

使用 Windows 及 `global.json` 指定的 **.NET SDK**（10.0.401 或相容的 10.0.4xx 修補版本）：

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore --disable-build-servers
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

開發準備、輔助元件測試、程式碼慣例及發行檢查請見[貢獻指南](CONTRIBUTING.md)。原始碼與通用文件使用英文，多語言 `README.<locale>.md` 與介面語言套件為例外；譯文有差異時以英文文件為準。

[更新紀錄](CHANGELOG.md) · [架構](docs/ARCHITECTURE.md) · [在地化](docs/LOCALIZATION.md) · [發行流程](docs/RELEASING.md) · [行為準則](CODE_OF_CONDUCT.md) · [第三方聲明](THIRD_PARTY_NOTICES.md)

## 授權條款

Copyright © 2026 KeeganKei。專案採用 [Apache License 2.0](LICENSE)，署名請見 [NOTICE](NOTICE)。相依套件、選用執行階段與模型遵循各自授權條款，詳見[第三方聲明](THIRD_PARTY_NOTICES.md)。專案授權不授予商標權。

應用程式圖示由維護者指導 AI 生成，詳見[設計與來源紀錄](docs/assets/README.md)。
