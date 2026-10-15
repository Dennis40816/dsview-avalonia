<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# dsview-avalonia

本獨立專案規劃以 Avalonia 開發邏輯分析儀介面，使用 DSView 的擷取與解碼函式庫。

本頁為中文導覽。專案文件以 [英文 README](README.md) 為準。

## 目前狀態

目前已有可執行的原生 Avalonia **離線工作區**，預設範例使用合成波形及預先產生的解析資料。
選用的開發用 I²C 來源會呼叫外部 native decoder，解析其生成的原始 samples。
可用自含 `.dsview2` 分析檔保存原始 samples、解析快照與工作狀態。
選用的 Windows 開發用 reader 已能匯入部分 DSView logic-v3 `.dsl`／`.demo` 原始訊號；硬體擷取及通用格式支援仍待完成。
**授權清查尚未完成，目前不提供 binary 發行。**

已提供一次一個 Group、全部通道、自訂波形線色、Hex 封包內容、高／低脈寬、完整局部週期／頻率，以及跨通道 A/B edge／位置量測。
捲動通道時時間尺保持可見。局部頻率不代表整段資料具有穩定的時鐘頻率。

選取某個 Hex byte，可在原有詳情區連結其數值、解析區間與欄位；ACK、START／STOP 各自獨立，以語意色搭配文字辨識。NACK 使用琥珀色，診斷證據使用玫紅色。
Locate selected 定位該來源，Previous 返回前一視野，A/B 不變。分析檔 v5 保存逐項選取、多筆量測、解析實例與通道對應，支援至 128 bit 的完整 Hex、SPI 雙向資料及已有的 SPI／UART 設定。仍可讀取 v1–v4，舊檔未記錄的設定保留為未知。這些保存能力尚不代表 App 已能執行 SPI／UART native 解析。配置選用 native runtime 後，可從左側 Decoders 選擇 SCL／SDA、背景解析目前資料或取消。失敗保留舊分析；原生操作體驗仍待驗證。
開發用 [SPI／UART native adapter](docs/NATIVE-SERIAL.md) 已可由程式呼叫，解析有限長度的原始波形；App 設定操作仍未接入。
詳見 [協議資料選取](docs/PROTOCOL-SELECTION.md)。

Activity 可比較全部已擷取通道在整段資料與目前視野的活動。
Capture issues 依回報的解析層列出異常；Locate issue 定位其證據並保留量測標記。
總覽顯示目前 Group 的原始訊號變化，包含未解析的控制訊號。

Group、活動與量測結果集中在左側；檔案、資料來源與 Settings 留在上方。
Settings 可預覽、套用或取消介面／資料字級、波形粗度與滾輪方向；原生視覺操作仍待驗證。
詳見 [顯示設定](docs/DISPLAY-SETTINGS.md)。

開發範圍涵蓋 DSView 既有能力。第一台實機暫定 DSLogic Plus，目前未接機、未確認型號。
[功能對照表](docs/DSVIEW-PARITY.md) 記錄來源、差距、函式庫重用及驗收順序；
有上游原始碼不代表已接入或已通過實機測試。

實作責任與測試原則見 [開發說明](docs/ENGINEERING.md)。

## 執行離線工作區

使用 `global.json` 指定的 .NET SDK（10.0.303，允許最新 patch）：

```sh
dotnet restore DSView2.slnx --locked-mode
dotnet build DSView2.slnx --no-restore
dotnet run --project src/DSView.Desktop --no-build
```

聚焦的數值及狀態檢查：

```sh
dotnet run --project tests/DSView.Checks --no-build
```

File → Save analysis（Ctrl+S）保存分析，Open analysis（Ctrl+O）重新開啟，
Save as 使用 Ctrl+Shift+S。標題的星號表示目前工作尚未保存；關閉或換來源時可保存、捨棄或取消。
正在選取或輸入的量測需先完成／取消。失敗或取消不會取代舊檔。

分析檔保留 Group、波形色、A/B、封包／異常選取、搜尋、時間範圍與垂直來源位置。
局部量測會依保存的 samples 重算。開檔顯示已保存的解析結果，不重跑 decoder，也不需要外部 native worker。
檔案內的 Group／顏色不會覆蓋個人預設。限制詳見 [分析檔格式](docs/ANALYSIS-FILES.md)。

點上方來源摘要，可選 **Board bring-up**（50 MS/s）、**Timing + gaps**（10 MS/s）
或 **Dense signals**（50 MS/s）。Timing + gaps 有不同資料與時鐘速度、讀取結束、
位址 NACK 及缺資料區間。Dense signals 有 32 路原始訊號，120 ms 內共 1,048,576 個
transition，沒有解析封包。
開啟範例會清除分析狀態，保留 Group 定義與線色。若目前 Group 不包含新來源的任何訊號，會改選可顯示訊號的 Group。
詳見[離線範例與驗證數值](docs/OFFLINE-EXAMPLES.md)。

手動選到沒有目前擷取訊號的 Group 時，波形區會說明原因並提供 **Show all signals**。
沒有可見訊號時，edge 導覽與新選點操作會停用。既有量測保留；**Fit** 放大端點時間範圍，
並在垂直空間足夠時顯示兩個來源通道。通道相距太遠時先顯示 A，在畫面外的端點旁提供 **Show B**。
**Show A/B** 不改變端點數值；**Show all signals** 恢復已擷取通道並維持目前時間範圍。

Group 編輯器提供這次已擷取的訊號與來源相符的 decoder tracks；已保存但這次未擷取的成員會另外列出並保留。
編輯 **All signals** 會用這次的訊號建立另一個 Group。Group 只改變觀察範圍，勾選 decoder track 不會啟動擷取或解析。
重開時會還原所有有效的已存 Group。

有本機 native worker 設定時，來源清單會出現 **I²C from samples**。
它只有 D0/SCL 與 D1/SDA，背景解析與顯示、量測使用同一份原始 samples。
Worker 與 runtime 尚未隨產品提供。設定、程序檢查與限制見[開發用解析契約](docs/NATIVE-I2C.md)。
目前僅支援有限長度的連續 I²C，不代表已支援串流或全部解析器。

**File → Import DSView capture** 使用另行設定的 native reader，背景匯入後先顯示 All signals，沒有預設解析結果。
保留實體通道編號與名稱，不套用檔案裡的裝置／decoder 設定。Esc 可取消；失敗或取消保留原分析。
Save 另存 `.dsview2`，不修改來源擷取檔；重開分析不需要來源檔或 reader。
支援範圍與限制見英文[擷取檔 reader 契約](docs/NATIVE-DSL.md)。

密集波形先顯示活動帶，放大後可看見個別 transition。
選點與量測始終使用原始 sample，不受顯示細節影響。
實作方式與選用的操作次數檢查見[波形繪製](docs/WAVEFORM-RENDERING.md)。

時間尺依目前跨度使用 ns、µs、ms 或 s；離擷取起點很遠的細部視野會標明基準時間，
刻度顯示相對差值。A/B 輸入固定以標示的 µs 為單位，縮放不會改變輸入數字的意思。
Packets 沒有結果時，會區分來源無解析資料、Group 無封包或搜尋無符合項目。
被篩選排除的選取封包仍保留在詳情區，並標示它不在目前範圍內。

先選 Group 或 All signals。指向脈衝可預覽量測，點一下固定結果。
選 Cursor A/B 後，可以從 A 按住拖到 B，再放開完成；也保留依序點選兩點。
兩端可在不同通道。Position 可量任意取樣位置，Snap to edge 則吸附實際 edge。
新區間若放開在訊號區外或沒有有效 edge，就還原原量測；Esc 或失去滑鼠擷取也會取消。
拖曳期間滾輪不改變時間視野。
Esc 取消選點或尚未提交的數值修改。波形／時間尺上直接滾輪，以滑鼠位置為中心縮放，無須 Ctrl。
Shift＋滾輪上下捲動訊號；通道標籤區也保留一般滾輪捲動。拖曳波形空白處平移。
選封包可看內容，Locate source 才會移動時間視野；Fit time 顯示完整時間範圍。

Cursor A/B 會將焦點移到波形：上下鍵換訊號、左右鍵逐個 observed edge 移動
（Position 模式每次一個 sample），Enter 選點。未在選點時，Enter 固定游標處的局部脈衝量測。
A/B 在同一時間仍有分開的時間尺把手。輸入數字會設定 Position；錯誤輸入會保留已提交的量測。
可拖曳游標線或時間尺把手；脈衝預覽與 A/B 區間以箭頭標示可見端點，不把視野邊界當成量測端點。
Overview 用較亮底色與外框標出目前範圍；Hex 標題右側使用複製圖示，保留提示與可存取名稱。
Fit 顯示選取區間及局部頻率所依據的完整週期；目前 Group 已含兩端點時不切換 Group。
Previous view 回到原視野，不清除量測。

Activity 的 Show 顯示指定通道，不改時間範圍。
若自訂 Group 已不包含解析器所需通道，明確定位來源／異常時會建立來源 Group。

實體探針色條固定。點訊號名稱旁獨立的短線，會在通道旁開啟顯示設定。
色票或六位 Hex 先更新局部波形預覽，Apply 才套用；Default 也要 Apply 才生效。
取消、Esc 或點面板外會放棄草稿。此處只改波形線色，不改擷取設定。
Group／線色偏好會保存；Save analysis 保存擷取資料、目前量測與已保留結果。

完整量測旁的 Keep result 可保留一份副本；Results 集中顯示名稱、備註及精確來源，
Show on waveform 叫出副本並定位。Clear 只清除目前 A/B，不刪已保留結果。
刪除結果可 Undo；名稱／備註與多筆結果會一起保存。詳見英文 [timing results](docs/TIMING-RESULTS.md)。

已操作驗證 Windows 原生介面。其他桌面平台、完整 DPI 組合及大資料效能仍待驗證。
合成範例不能證明真實裝置的時間精度或解析器正確性。
已實際操作 ns／µs／ms 視野、短脈衝 Fit、跨通道鍵盤選點、數值取消與空搜尋恢復。
大時間 offset、其他空結果種類及完整鍵盤／DPI 組合仍待操作驗證。這些檢查不等於獨立易用性試用。

Binary 發行前需補齊上游來源與許可證據，確認實際建置及執行相依，並建立軟體物料清單（SBOM）。
詳見 [第三方清查狀態](THIRD-PARTY-NOTICES.md)。

## 取得來源

```sh
git clone --recurse-submodules https://github.com/Dennis40816/dsview-avalonia.git
cd dsview-avalonia
git -C external/DSView rev-parse HEAD
```

預期的上游版本：

```text
2e9e2c8e726df4ef5687d39b83d4f797cc44b574
```

既有 clone 可執行：

```sh
git submodule update --init --recursive
```

版本由 `external/DSView` 的 gitlink 固定。
重現此版本時不要使用 `git submodule update --remote`。
GitHub 自動產生的來源 ZIP 不包含 submodule checkout，請透過 Git 取得完整來源參照。

Submodule 包含完整的上游來源，部分元件不會選入本專案規劃的 runtime。
Submodule 的存在不代表已核准所有元件的打包或授權。
Frontend 自有來源目錄沒有複製上游檔案。

## 授權

本專案原創程式、設定與文件採 **GNU GPL 第 3 版或任何後續版本**，
識別碼為 `GPL-3.0-or-later`。
詳見 [LICENSE](LICENSE)、[NOTICE](NOTICE) 與 [SPDX 檔頭規則](docs/SPDX.md)。

第三方來源保留各自授權。根目錄的 LICENSE 不會取代 DSView submodule 或其相依套件的授權。

本專案與 DreamSourceLab 無隸屬或背書關係，也不是 DSView 的官方續作。
