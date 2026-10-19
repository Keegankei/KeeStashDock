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
  <img alt="版本 1.2.3" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 與 11" src="docs/assets/badges/windows.svg">
  <img alt="Apache-2.0 授權條款" src="docs/assets/badges/license.svg">
</p>

**原始碼版本 1.2.3** · [版本說明](docs/releases/1.2.3-notes.md) · [驗收記錄](docs/releases/1.2.3.md)

目前已發佈的穩定套件為 **1.2.2**。下述 1.2.3 功能適用於更新後的原始碼與候選組建，正式發佈後才會提供對應下載。截圖與 GIF 仍標示為 **1.2.2**，未展示新增控制項。

[快速開始](#get-started) · [功能介紹](#features) · [快捷操作](#shortcuts) · [資料與升級](#data-and-upgrades) · [開發](#development) · [授權條款](#license)

<a id="get-started"></a>
## 快速開始

**[最新發布版本](https://github.com/Keegankei/KeeStashDock/releases/latest)** · **[Windows x64 ZIP — 1.2.2](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.2/KeeStashDock-1.2.2-win-x64.zip)** · [SHA-256 校驗檔](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.2/KeeStashDock-1.2.2-win-x64.zip.sha256)

1. 在發布頁的 **Assets** 中下載應用程式 ZIP；GitHub 的 **Source code** 壓縮檔供開發者使用。
2. 將完整套件解壓縮至可寫入的資料夾，執行 `KeeStashDock.exe`。請保留所有隨附資料夾。
3. 按 **Alt+Q** 顯示快速面板。輸入文字片段後按 **Enter** 儲存；點選卡片即可複製。
4. 從面板或系統匣開啟**設定**，選擇語言、佈景主題與選用元件。

Windows x64 套件已包含 .NET，無需另外安裝。主要支援 Windows 11，並盡力相容 Windows 10 22H2。OCR 與離線翻譯可能需要 Microsoft Visual C++ 執行階段，可透過應用程式內的元件安裝流程安裝。已驗證的平台範圍見 [1.2.2 驗收紀錄](docs/releases/1.2.2.md)。

<details>
<summary>校驗下載的 ZIP</summary>

在下載資料夾中執行以下命令，將結果與隨附的 `.sha256` 檔案比較：

```powershell
Get-FileHash .\KeeStashDock-1.2.2-win-x64.zip -Algorithm SHA256
```

校驗和用於檢查檔案完整性，並非發行者簽章。目前套件未簽章。

</details>

**全新安裝預設值 · 1.2.3：**介面為英文；**Alt+Q** 開啟面板，**Alt+W** 擷取螢幕。不預先安裝 OCR 與本機翻譯。升級會保留既有設定。

選取文字泡泡預設開啟，動作為**僅複製**，翻譯引擎為 **Google**；泡泡採用預設樣式，關閉文字發光、開啟懸浮陰影。開機啟動、自動收集剪貼簿與直接貼上預設關閉。貼邊自動收納預設開啟，收起延遲 **2 秒**、展開延遲 **0.1 秒**。外觀跟隨系統，使用**標準**模式；空間模式的檢視方式預設為**按住右鍵拖曳**。面板文字配色自動，控制項不透明度 **90%**；無背景圖，遮罩不透明度 **20%**，毛玻璃關閉，預設半徑 **4 px**。定時自動備份預設開啟，每 **24 小時**執行。

<details>
<summary>全新安裝的設定 · 英文介面</summary>

<p align="center"><img src="docs/images/default-settings.png" width="720" alt="1.2.2 設定頁面，顯示英文、Alt+Q 與 Alt+W 預設值"></p>

</details>

<a id="features"></a>
## 功能介紹

**1.2.3 設定導覽：**依**一般、擷取與翻譯、外觀、本機元件、備份與資料、關於**分類。相關選項僅在需要時顯示，隱藏不會重設已儲存的選擇。

在**擷取與翻譯**中，可獨立開關結果視窗的目標語言選擇器與儲存至群組面板。兩項預設開啟，在 OCR 或翻譯完成後顯示；關閉後仍可查看及複製結果。

在**備份與資料**中選擇備份資料夾，在**本機元件**中分別選擇 OCR 與離線翻譯資料夾。變更位置**不會移動現有檔案**；新的元件資料夾為空時需重新安裝。**關於**中唯讀顯示目前的應用程式資料位置，移轉資料庫請使用匯出及匯入。

以下預覽展示 **1.2.2 的實際英文介面**，內容為合成範例。標準面板使用全新安裝的外觀預設值；空間模式及同時顯示「複製 / 翻譯」的氣泡示範選用設定。空間檢視動圖使用模擬指標，更新預覽使用模擬版本。安裝路徑和資料路徑已遮蔽，不包含個人桌面或剪貼簿內容。[圖片來源與產生方式](docs/images/README.md)。

<p align="center"><img src="docs/images/standard-panel-workflow.gif" width="400" alt="1.2.2 標準面板操作：輸入並儲存文字片段，再釘選和取消釘選卡片"></p>

*標準模式 · 輸入、儲存、釘選與取消釘選 · 英文介面。使用實際控制項和範例內容示範預設操作。*

### 1. 收集、搜尋與整理

<p align="center"><img src="docs/images/main-window.png" width="900" alt="工作台展示搜尋、群組、釘選文字片段與範例圖片項目"></p>

使用快速面板隨手收集內容，在**工作台**管理收藏。點選面板的展開按鈕或使用系統匣選單開啟工作台。

| 工具 | 可以完成的操作 |
| --- | --- |
| 快速輸入與搜尋 | 按 Enter 儲存文字；用 Alt+Enter 或 Shift+Enter 換行；在工作台搜尋和篩選項目。 |
| 拖入面板 | 拖入文字即可儲存，拖入檔案或資料夾則儲存其路徑。點選路徑卡片開啟目標；以右鍵選擇**複製**可複製路徑。不會匯入檔案內容。 |
| 群組、標籤與釘選 | 將相關項目放在一起，編輯標題、內文與標籤，並釘選常用內容。 |
| 有效期限與資源回收筒 | 可選永不過期、一天、七天或閱後即焚；過期與刪除的項目進入資源回收筒，可以還原。資源回收筒中的項目預設於 30 天後永久刪除。 |
| 桌面便箋 | 將文字或路徑卡片拖出面板，在本次執行期間作為獨立桌面便箋顯示。結束應用程式會關閉這些便箋，但已儲存的項目仍保留在收藏中。圖片卡片仍作為圖片項目使用。 |
| QR 碼 | 以右鍵點選文字卡片，將最多 1,800 個字元展示為 QR 碼。 |
| 文字工具 | 使用工作台的轉換選單進行大小寫轉換、空白清理、JSON 格式化、URL 編解碼與 Base64 處理。 |
| 群組匯入匯出 | 分享 `.ksdgrp` 套件，或載入 Git、Docker、Shell 等工具的命令預設集。含圖片的套件需要相容的應用程式版本。 |
| 剪貼簿歷程 | 可選擇自動收集複製的**文字**；忽略空白與過大的文字，不重複新增相同內容。預設關閉。 |
| 直接貼上 | 能夠安全確認目標視窗時，可選擇將複製的文字項目貼到先前的應用程式。預設關閉。 |

### 2. 標準與空間面板 · 1.2.2

開啟**設定 > 外觀 > 面板模式**進行選擇。兩種模式使用相同的控制項、字型、配色與卡片版面配置。

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
- **桌面邊緣隱藏：**1.2.2 預設開啟；將面板拖至螢幕任一側邊緣，再移開滑鼠。邊緣會保留一個小把手，停留其上或點選即可恢復。鎖定面板可使其保持顯示。

支援淺色、深色與跟隨系統佈景主題，可設定自訂背景、透明度與磨砂效果。系統高對比與減少動畫的偏好會啟用靜態替代顯示。[深色預覽](docs/images/floating-panel-dark.png) · [固定視角預覽](docs/images/floating-panel-fixed.png) · [效能測量與限制](docs/performance/1.2.2-floating.md)。

設定也提供面板文字配色、自訂文字顏色與自動對比度，以及獨立的選取文字泡泡樣式、發光、陰影與透明度控制。

<details>
<summary>預設外觀設定</summary>

<p align="center"><img src="docs/images/default-appearance.png" width="720" alt="外觀設定，顯示標準模式與全新安裝預設值"></p>

</details>

### 3. 螢幕擷取與本機文字辨識

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="已儲存的螢幕擷取項目，包含原圖與可編輯的辨識文字"></p>

按 **Alt+W** 或點選螢幕擷取按鈕，拖曳選取區域。儲存的擷取畫面會成為目前所選群組中的圖片項目。開啟項目可預覽、複製、另存為 PNG，或**辨識文字**。保留原圖的同時，可以編輯與搜尋辨識結果。

**1.2.2：**完成選取範圍後，工具列提供三項獨立操作：

| 操作 | 結果 |
| --- | --- |
| 儲存螢幕擷取畫面 | 儲存為圖片項目。按 Enter 執行此操作。 |
| 擷取文字 | 執行本機 OCR 並顯示暫存文字，不儲存項目，也不自動複製。 |
| 擷取並翻譯 | 先執行本機 OCR，再將辨識文字交給所選翻譯服務。擷取畫面本身不會上傳。 |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="1.2.2的暫存 OCR 結果，提供明確的文字複製操作"></p>

再次拖曳可以重新選取區域；按 **Esc** 或右鍵取消。暫存結果支援主動複製，翻譯失敗後保留原文，並可重試翻譯。OCR 僅負責辨識，不會自動翻譯。[螢幕擷取使用流程](docs/SCREENSHOTS.md) · [OCR 支援範圍與限制](docs/LOCAL_OCR.md)。

**1.2.3：**暫存 OCR 與翻譯結果提供**儲存原文**、**儲存譯文**、**同時儲存**及預設使用目前群組的選擇器。同時儲存會建立一筆明確區分原文與譯文的文字項目。僅在主動點選後儲存，不改變剪貼簿，也不附帶儲存截圖。翻譯失敗仍可儲存原文；儲存失敗會保留結果以便重試。重複文字沿用既有項目並移至所選群組。

### 4. 選取文字操作與翻譯

<p align="center"><img src="docs/images/selection-actions.png" width="480" alt="1.2.2 選取文字泡泡，同時提供獨立的複製與翻譯按鈕"></p>

在**設定 > 擷取與翻譯**中啟用選取文字泡泡，接著在其他應用程式選取文字。**複製**會將所選文字儲存到面板目前的群組；**翻譯**顯示譯文，不新增項目。

**1.2.2：**選擇**複製和翻譯**，即可同時顯示兩個按鈕。複製後，在泡泡關閉前仍可翻譯同一段選取文字。翻譯失敗時會保留原文。

**1.2.3：**可在設定中獨立選擇翻譯目標語言。新安裝及升級預設仍為**跟隨介面語言**；選取文字翻譯、截圖翻譯與翻譯測試共用此設定。結果視窗顯示實際目標語言，並允許使用保留的原文暫時重新翻譯；此選擇僅作用於目前結果，不改變全域設定。可用語言取決於服務商或已安裝的離線模型；不支援的語言方向會明確回報錯誤，不會自動切換服務或上傳離線文字。

| 服務 | 設定與可用性 |
| --- | --- |
| Bing（中國）、Google、Lingva | 無需金鑰，但不保證可用性。新安裝預設使用 Google，既有偏好會保留。 |
| 百度、阿里雲、DeepL | 使用自己的憑證。免費額度與費用取決於服務商及帳戶。阿里雲需要啟用機器翻譯服務，並授予 `alimt:TranslateGeneral` 權限。 |
| 本機離線 | 安裝選用的本機引擎，透過回送位址執行服務，無需帳戶或 API 費用。 |

雲端翻譯僅將主動要求翻譯的文字傳送給所選服務，不會自動切換服務商。OCR 不上傳圖片。服務憑證以**明文**儲存在本機 `settings.json`；輸入欄位的遮蔽顯示不代表加密。

<details>
<summary>預設選取文字與翻譯設定</summary>

<p align="center"><img src="docs/images/behavior-settings.png" width="720" alt="行為設定，預設僅複製、Google、關閉文字發光並開啟懸浮陰影"></p>

</details>

### 5. 在應用程式內安裝選用元件

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="選用元件設定，提供 OCR 與離線翻譯的安裝及修復按鈕"></p>

*圖中為 1.2.2 全新安裝的元件設定：OCR 和離線翻譯尚未安裝。使用這些應用程式內按鈕即可安裝，無需終端機命令。*

1. 開啟**設定 > 本機元件**。
2. 首次使用 OCR 前，在**文字辨識（中文 / 英文）**下選擇**安裝 / 修復**。1.2.2 不預先安裝 OCR 或本機翻譯引擎及模型；主動安裝時才下載並驗證所需檔案，同時檢查本機執行階段。
3. 本機翻譯請在**離線翻譯（中文 / 英文）**下選擇**安裝 / 修復**。首次下載期間保持設定視窗開啟，並預留約 **1 GB 磁碟空間**與 **2 GB 可用記憶體**。
4. 如出現 Microsoft 安裝程式，請依提示操作。如提示**需要重新啟動**，重新啟動 Windows 後再試。取消應用程式元件安裝不會復原已開始的 Windows 執行階段安裝。
5. 將翻譯引擎設為**本機離線（Argos / LibreTranslate）**，保留 `http://127.0.0.1:5000`，然後使用**測試翻譯**。服務會在需要時啟動，也可手動選擇**啟動服務**與**停止服務**。

首次設定需要存取相關 NuGet、GitHub、Python、套件、模型與 Microsoft 下載站點。下載失敗後可重試，已驗證的快取檔案會重複使用。安裝完成後，OCR 與本機翻譯可以離線執行。翻譯服務在應用程式關閉後仍會執行，直到主動停止。

**語言支援：**OCR 支援印刷體英語與簡體中文。選用下載的翻譯模型支援**英語 ↔ 簡體中文**。應用程式的 **20 種介面語言**不代表 OCR 或離線翻譯支援 20 種語言。缺少模型時不會自動切換至雲端翻譯。[離線安裝與疑難排解](docs/OFFLINE_TRANSLATION.md)。

### 6. 檢查更新 · 1.2.2

<p align="center"><img src="docs/images/update-check.png" width="720" alt="更新視窗展示模擬的新版本，安裝路徑已遮蔽"></p>

*圖中的版本為模擬範例，安裝路徑已遮蔽，不表示已有該版本可供下載。*

在系統匣選單或**設定 > 關於**中選擇**檢查更新**。檢查不會自動下載或安裝。下載時會驗證 SHA-256 與套件內容；**重新啟動並升級**需要個別確認。更新程式在原有路徑替換受管理的應用程式檔案，保留使用者資料與已安裝元件，並在啟動失敗時保留復原資訊。

整理後的套件根目錄只有三個檔案：`KeeStashDock.exe`、`StartHere.md` 與 `update-manifest.json`。使用說明位於 `Docs`，版權聲明位於 `Licenses`，語言與選用元件各有專用資料夾。請完整保留套件。[更新與復原](docs/UPDATING.md)。

<a id="shortcuts"></a>
## 快捷操作

**1.2.3：**啟動時若無法註冊面板或截圖快速鍵，會顯示包含具體快速鍵與設定入口的提示，設定內保留狀態。系統匣與按鈕仍可使用；重新綁定失敗時保留原本有效的快速鍵。

| 操作 | 預設快速鍵或手勢 |
| --- | --- |
| 顯示/隱藏快速面板 | **Alt+Q**，可自訂 |
| 儲存輸入的文字 | **Enter** |
| 換行 | **Alt+Enter** 或 **Shift+Enter** |
| 螢幕擷取 | **Alt+W**，可獨立設定 |
| 儲存擷取範圍 | 在 1.2.2 擷取工具列中按 **Enter** |
| 取消螢幕擷取 | **Esc** 或右鍵 |
| 檢視空間面板 | 按住**滑鼠右鍵**拖曳，1.2.2 中可自訂 |
| 選取文字泡泡複製/翻譯 | 1.2.2 中使用 **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T**，僅在泡泡可見且快速鍵可用時生效 |

<a id="data-and-upgrades"></a>
## 資料、備份與升級

**1.2.3：**設定中顯示最近成功備份、最近檢查、下次排程檢查、執行狀態、備份位置與快照數量。可**立即備份**、開啟資料夾，或從清單經安全復原流程還原快照。週期新增 **15／30／60 分鐘**，同時保留 6／12／24／48 小時及 7 天，預設仍為 **24 小時**。保留既有自訂週期與保留數量。每 15 分鐘保留 7 份快照時，最早至最新僅涵蓋 **90 分鐘**，請配合儲存空間估算選擇保留數量。每次排程備份都會建立完整快照，即使資料庫未變更；短週期會更快輪替歷史。備份涵蓋項目資料庫與其中的圖片，**不包括全部設定、元件或背景圖**。自動輪替不會刪除手動匯出或復原副本。

<details>
<summary>備份設定 · 預設每 24 小時自動備份</summary>

<p align="center"><img src="docs/images/backup-settings.png" width="720" alt="資料設定，預設每 24 小時自動備份，路徑已遮蔽"></p>

</details>

項目、圖片、辨識文字與備份皆儲存在本機。可攜式資料通常位於執行檔旁的 `Data` 資料夾。如果該位置無法寫入，應用程式會改用 `%AppData%\KeeStashDock`，並在設定中提示。透過應用程式安裝的元件通常位於 `%LOCALAPPDATA%\KeeStashDock`，與收藏內容分開儲存。

在**設定 > 備份與資料**中匯出/匯入備份並設定排程備份。從 **1.2.1 首次升級需要手動進行**：

1. 匯出備份，然後透過系統匣選單結束應用程式。
2. 將新 ZIP 解壓縮至獨立資料夾。在應用程式關閉時，將**目前使用的完整資料夾**複製到新安裝的 `Data` 資料夾。如果設定顯示使用 `%AppData%` 備用位置，請以該資料夾為來源。
3. 保留原安裝與備份，啟動新版本並確認項目完整後再清理舊副本。保留升級前的備份以便還原；不同版本的資料庫結構不能任意互換。
4. 如果原本開啟了**開機啟動**，且此次移至新目錄，請在新版設定中先關閉再開啟此選項，以登記新的程式路徑；否則下次登入 Windows 時可能啟動舊版。

較早的 1.2.2 候選版與正式版使用相同數字版本號，也需要手動替換。請保留資料與備份，並依相同流程遷移。

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
