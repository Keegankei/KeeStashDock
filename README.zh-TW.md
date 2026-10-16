<h1 align="center">KeeStashDock</h1>

<p align="center">
  <img src="docs/assets/app-icon.png" width="112" alt="KeeStashDock 應用程式圖示">
</p>

<p align="center">
  <strong>可攜式 Windows 收納面板，用於儲存文字片段、螢幕擷取與重複利用文字。</strong><br>
  收集所需內容，在本機整理，並在需要時主動翻譯。
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
  <img alt="已發布版本 1.2.1" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 與 11" src="docs/assets/badges/windows.svg">
  <img alt="Apache-2.0 授權條款" src="docs/assets/badges/license.svg">
</p>

> **發布狀態：**目前已發布版本為 **1.2.1**，此分支正在準備 **1.2.2**。標示 **1.2.2 候選版**的章節介紹尚未發布的功能；下載連結仍提供 1.2.1。請參閱[候選版驗收紀錄](docs/releases/1.2.2.md)。

[快速開始](#get-started) · [功能介紹](#features) · [快捷操作](#shortcuts) · [資料與升級](#data-and-upgrades) · [開發](#development) · [授權條款](#license)

<a id="get-started"></a>
## 快速開始

**[最新發布版本](https://github.com/Keegankei/KeeStashDock/releases/latest)** · **[Windows x64 ZIP — 1.2.1](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip)** · [SHA-256 校驗檔](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip.sha256)

1. 在發布頁的 **Assets** 中下載應用程式 ZIP；GitHub 的 **Source code** 壓縮檔供開發者使用。
2. 將完整套件解壓縮至可寫入的資料夾，執行 `KeeStashDock.exe`。請保留所有隨附資料夾。
3. 按 **Alt+Q** 顯示快速面板。輸入文字片段後按 **Enter** 儲存；點選卡片即可複製。
4. 從面板或系統匣開啟**設定**，選擇語言、佈景主題與選用元件。

Windows x64 套件已包含 .NET，無需另外安裝。主要支援 Windows 11，並盡力相容 Windows 10 22H2。OCR 與離線翻譯可能需要 Microsoft Visual C++ 執行階段，可透過應用程式內的元件安裝流程安裝。已驗證的平台範圍見 [1.2.1 驗收紀錄](docs/releases/1.2.1.md)。

<details>
<summary>校驗下載的 ZIP</summary>

在下載資料夾中執行以下命令，將結果與隨附的 `.sha256` 檔案比較：

```powershell
Get-FileHash .\KeeStashDock-1.2.1-win-x64.zip -Algorithm SHA256
```

校驗和用於檢查檔案完整性，並非發行者簽章。目前套件未簽章。

</details>

<a id="features"></a>
## 功能介紹

以下預覽展示實際應用程式控制項，內容為合成範例。檢視動圖使用這些控制項與模擬指標產生；更新預覽使用模擬版本。圖片不包含個人桌面或剪貼簿內容。[圖片來源與產生方式](docs/images/README.md)。

### 1. 收集、搜尋與整理

<p align="center"><img src="docs/images/main-window.png" width="900" alt="工作台展示搜尋、群組、釘選文字片段與範例圖片項目"></p>

使用快速面板隨手收集內容，在**工作台**管理收藏。點選面板的展開按鈕或使用系統匣選單開啟工作台。

| 工具 | 可以完成的操作 |
| --- | --- |
| 快速輸入與搜尋 | 按 Enter 儲存文字；用 Alt+Enter 或 Shift+Enter 換行；輸入搜尋內容時即時篩選項目。 |
| 拖入面板 | 拖入文字即可儲存，拖入檔案或資料夾則儲存其路徑。點選路徑卡片開啟目標；以右鍵選擇**複製**可複製路徑。不會匯入檔案內容。 |
| 群組、標籤與釘選 | 將相關項目放在一起，編輯標題、內文與標籤，並釘選常用內容。 |
| 有效期限與資源回收筒 | 可選永不過期、一天、七天或閱後即焚；過期與刪除的項目進入資源回收筒，可以還原。資源回收筒中的項目預設於 30 天後永久刪除。 |
| 桌面便箋 | 將文字或路徑卡片拖出面板，在本次執行期間作為獨立桌面便箋顯示。結束應用程式會關閉這些便箋，但已儲存的項目仍保留在收藏中。圖片卡片仍作為圖片項目使用。 |
| QR 碼 | 以右鍵點選文字卡片，將最多 1,800 個字元展示為 QR 碼。 |
| 文字工具 | 使用工作台的轉換選單進行大小寫轉換、空白清理、JSON 格式化、URL 編解碼與 Base64 處理。 |
| 群組匯入匯出 | 分享 `.ksdgrp` 套件，或載入 Git、Docker、Shell 等工具的命令預設集。含圖片的套件需要相容的應用程式版本。 |
| 剪貼簿歷程 | 可選擇自動收集複製的**文字**；忽略空白與過大的文字，不重複新增相同內容。預設關閉。 |
| 直接貼上 | 能夠安全確認目標視窗時，可選擇將複製的文字項目貼到先前的應用程式。預設關閉。 |

### 2. 標準與空間面板 · 1.2.2 候選版

開啟**設定 > 面板模式**進行選擇。兩種模式使用相同的控制項、字型、配色與卡片版面配置。

<table>
  <tr><th>標準</th><th>空間</th></tr>
  <tr>
    <td><img src="docs/images/classic-panel.png" width="290" alt="標準面板採用平面視圖與柔和陰影"></td>
    <td><img src="docs/images/floating-panel.png" width="290" alt="空間面板具有透視效果、層疊卡片與纖細側邊"></td>
  </tr>
  <tr><td>穩定的平面顯示，搭配柔和陰影。</td><td>透視效果、獨立卡片回饋，以及清晰的層次深度。</td></tr>
</table>

<p align="center"><img src="docs/images/floating-panel-motion.gif" width="400" alt="空間模式：先展示卡片靠近回饋，再按住拖曳檢視"></p>

*白色指標展示靠近卡片時的回饋；橘色指標表示按住拖曳檢視。這是效果示範動圖，並非影格率測試。*

- **固定透視：**保持穩定的傾斜視角，附近卡片仍回應指標。
- **拖曳檢視：**在卡片內文或面板背景上按住**滑鼠右鍵**拖曳，放開後保留角度。未拖曳的右鍵點選仍開啟卡片選單。設定中也可選擇中鍵或自訂滑鼠按鍵與修飾鍵組合。
- **精簡列：**將面板收合為六個操作：最小化、展開/收合、鎖定、螢幕擷取、工作台與設定。展開時恢復先前的面板大小。
- **桌面邊緣隱藏：**啟用此選項後，將面板拖至螢幕任一側邊緣，再移開滑鼠。邊緣會保留一個小把手，停留其上或點選即可恢復。鎖定面板可使其保持顯示。

支援淺色、深色與跟隨系統佈景主題，可設定自訂背景、透明度與磨砂效果。系統高對比與減少動畫的偏好會啟用靜態替代顯示。[深色預覽](docs/images/floating-panel-dark.png) · [固定視角預覽](docs/images/floating-panel-fixed.png) · [效能測量與限制](docs/performance/1.2.2-floating.md)。

設定也提供面板文字配色、自訂文字顏色與自動對比度，以及獨立的選取文字泡泡樣式、發光、陰影與透明度控制。

### 3. 螢幕擷取與本機文字辨識

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="已儲存的螢幕擷取項目，包含原圖與可編輯的辨識文字"></p>

按 **Ctrl+Alt+S** 或點選螢幕擷取按鈕，拖曳選取區域。儲存的擷取畫面會成為目前所選群組中的圖片項目。開啟項目可預覽、複製、另存為 PNG，或**辨識文字**。保留原圖的同時，可以編輯與搜尋辨識結果。

**1.2.2 候選版：**完成選取範圍後，工具列提供三項獨立操作：

| 操作 | 結果 |
| --- | --- |
| 儲存螢幕擷取畫面 | 儲存為圖片項目。按 Enter 執行此操作。 |
| 擷取文字 | 執行本機 OCR 並顯示暫存文字，不儲存項目，也不自動複製。 |
| 擷取並翻譯 | 先執行本機 OCR，再將辨識文字交給所選翻譯服務。擷取畫面本身不會上傳。 |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="1.2.2 候選版的暫存 OCR 結果，提供明確的文字複製操作"></p>

再次拖曳可以重新選取區域；按 **Esc** 或右鍵取消。暫存結果支援主動複製，翻譯失敗後保留原文，並可重試翻譯。OCR 僅負責辨識，不會自動翻譯。[螢幕擷取使用流程](docs/SCREENSHOTS.md) · [OCR 支援範圍與限制](docs/LOCAL_OCR.md)。

### 4. 選取文字操作與翻譯

<p align="center"><img src="docs/images/selection-actions.png" width="480" alt="1.2.2 選取文字泡泡，同時提供獨立的複製與翻譯按鈕"></p>

在**設定 > 行為**中啟用選取文字泡泡，接著在其他應用程式選取文字。**複製**會將所選文字儲存到面板目前的群組；**翻譯**顯示譯文，不新增項目。

**1.2.2 候選版：**選擇**複製和翻譯**，即可同時顯示兩個按鈕。複製後，在泡泡關閉前仍可翻譯同一段選取文字。翻譯失敗時會保留原文。

翻譯目標依照應用程式的介面語言。可用的目標語言取決於所選服務商或已安裝的離線模型。

| 服務 | 設定與可用性 |
| --- | --- |
| Bing（中國）、Google、Lingva | 無需金鑰，但不保證可用性。新安裝預設使用 Bing，既有偏好會保留。 |
| 百度、阿里雲、DeepL | 使用自己的憑證。免費額度與費用取決於服務商及帳戶。阿里雲需要啟用機器翻譯服務，並授予 `alimt:TranslateGeneral` 權限。 |
| 本機離線 | 安裝選用的本機引擎，透過回送位址執行服務，無需帳戶或 API 費用。 |

雲端翻譯僅將主動要求翻譯的文字傳送給所選服務，不會自動切換服務商。OCR 不上傳圖片。服務憑證以**明文**儲存在本機 `settings.json`；輸入欄位的遮蔽顯示不代表加密。

### 5. 在應用程式內安裝選用元件

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="選用元件設定，提供 OCR 與離線翻譯的安裝及修復按鈕"></p>

*圖中為 1.2.1 元件尚未安裝的範例狀態。一般設定透過應用程式內按鈕完成，無需終端機命令。*

1. 開啟**設定 > 選用元件**。
2. OCR 元件缺少時，在**文字辨識（中文 / 英文）**下選擇**安裝 / 修復**。標準 ZIP 已包含引擎及英語、簡體中文模型，安裝流程也會檢查本機執行階段。
3. 本機翻譯請在**離線翻譯（中文 / 英文）**下選擇**安裝 / 修復**。首次下載期間保持設定視窗開啟，並預留約 **1 GB 磁碟空間**與 **2 GB 可用記憶體**。
4. 如出現 Microsoft 安裝程式，請依提示操作。如提示**需要重新啟動**，重新啟動 Windows 後再試。取消應用程式元件安裝不會復原已開始的 Windows 執行階段安裝。
5. 將翻譯引擎設為**本機離線（Argos / LibreTranslate）**，保留 `http://127.0.0.1:5000`，然後使用**測試翻譯**。服務會在需要時啟動，也可手動選擇**啟動服務**與**停止服務**。

首次設定需要存取相關 GitHub、Python、套件、模型與 Microsoft 下載站點。下載失敗後可重試，已驗證的快取檔案會重複使用。安裝完成後，OCR 與本機翻譯可以離線執行。翻譯服務在應用程式關閉後仍會執行，直到主動停止。

**語言支援：**OCR 支援印刷體英語與簡體中文。隨附翻譯模型支援**英語 ↔ 簡體中文**。應用程式的 **20 種介面語言**不代表 OCR 或離線翻譯支援 20 種語言。缺少模型時不會自動切換至雲端翻譯。[離線安裝與疑難排解](docs/OFFLINE_TRANSLATION.md)。

### 6. 檢查更新 · 1.2.2 候選版

<p align="center"><img src="docs/images/update-check.png" width="720" alt="更新視窗展示模擬的新版本與範例安裝路徑"></p>

*圖中的版本與路徑僅供示範，不表示已有該版本可供下載。*

在系統匣選單或**設定 > 關於**中選擇**檢查更新**。檢查不會自動下載或安裝。下載時會驗證 SHA-256 與套件內容；**重新啟動並升級**需要個別確認。更新程式在原有路徑替換受管理的應用程式檔案，保留使用者資料與已安裝元件，並在啟動失敗時保留復原資訊。

整理後的候選套件根目錄只有三個檔案：`KeeStashDock.exe`、`StartHere.md` 與 `update-manifest.json`。使用說明位於 `Docs`，版權聲明位於 `Licenses`，語言與選用元件各有專用資料夾。請完整保留套件。[更新與復原](docs/UPDATING.md)。

<a id="shortcuts"></a>
## 快捷操作

| 操作 | 預設快速鍵或手勢 |
| --- | --- |
| 顯示/隱藏快速面板 | **Alt+Q**，可自訂 |
| 儲存輸入的文字 | **Enter** |
| 換行 | **Alt+Enter** 或 **Shift+Enter** |
| 螢幕擷取 | **Ctrl+Alt+S**，可獨立設定 |
| 儲存擷取範圍 | 在 1.2.2 擷取工具列中按 **Enter** |
| 取消螢幕擷取 | **Esc** 或右鍵 |
| 檢視空間面板 | 按住**滑鼠右鍵**拖曳，1.2.2 中可自訂 |
| 選取文字泡泡複製/翻譯 | 1.2.2 中使用 **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T**，僅在泡泡可見且快速鍵可用時生效 |

<a id="data-and-upgrades"></a>
## 資料、備份與升級

項目、圖片、辨識文字與備份皆儲存在本機。可攜式資料通常位於執行檔旁的 `Data` 資料夾。如果該位置無法寫入，應用程式會改用 `%AppData%\KeeStashDock`，並在設定中提示。透過應用程式安裝的元件通常位於 `%LOCALAPPDATA%\KeeStashDock`，與收藏內容分開儲存。

在**設定 > 資料**中匯出/匯入備份並設定排程備份。從 **1.2.1 首次升級需要手動進行**：

1. 匯出備份，然後透過系統匣選單結束應用程式。
2. 將新 ZIP 解壓縮至獨立資料夾。在應用程式關閉時，將**目前使用的完整資料夾**複製到新安裝的 `Data` 資料夾。如果設定顯示使用 `%AppData%` 備用位置，請以該資料夾為來源。
3. 保留原安裝與備份，啟動新版本並確認項目完整後再清理舊副本。保留升級前的備份以便還原；不同版本的資料庫結構不能任意互換。

請勿在問題回報中附上個人 `Data`、憑證、私人備份或記錄。參閱[支援與去識別化報告](SUPPORT.md)、[升級復原](docs/UPDATING.md)及[安全性政策](SECURITY.md)。

<a id="development"></a>
## 開發與文件

在 Windows 上使用 [`global.json`](global.json) 指定的 SDK（10.0.401 或相容的 10.0.4xx 修補版本）建置：

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore --disable-build-servers
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

儲存庫文件以英文版為準。各語種 README 使用一致的功能結構；介面翻譯位於 `Locales/*.json`。歡迎依照[貢獻指南](CONTRIBUTING.md)提交翻譯修正。

| 文件 | 內容 |
| --- | --- |
| [更新日誌](CHANGELOG.md) | 版本歷程 |
| [架構](docs/ARCHITECTURE.md) | 專案、職責與儲存 |
| [在地化](docs/LOCALIZATION.md) | 介面與 README 的翻譯慣例 |
| [發布流程](docs/RELEASING.md) | 建置、封裝與驗收檢查 |
| [支援](SUPPORT.md) · [行為準則](CODE_OF_CONDUCT.md) | 提問、回報與社群參與 |
| [第三方聲明](THIRD_PARTY_NOTICES.md) | 相依項目、執行階段與模型的授權條款 |

<a id="license"></a>
## 授權條款

Copyright © 2026 KeeganKei。採用 [Apache-2.0](LICENSE) 授權；署名資訊見 [NOTICE](NOTICE)。相依項目、選用執行階段與模型保留各自授權條款，詳見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。專案授權不授予商標權。

應用程式圖示由維護者指導 AI 產生，其設計與來源紀錄見 [docs/assets/README.md](docs/assets/README.md)。
