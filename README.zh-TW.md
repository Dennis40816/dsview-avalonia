<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# dsview-avalonia

本獨立專案規劃以 Avalonia 開發邏輯分析儀介面，使用 DSView 的擷取與解碼函式庫。

本頁為中文導覽。專案文件以 [英文 README](README.md) 為準。

## 目前狀態

目前已有可執行的原生 Avalonia **離線工作區**，使用合成波形及預先產生的解析資料。
尚未接硬體擷取、raw protocol decoder、擷取檔匯入及分析工作階段保存／還原。
**授權清查尚未完成，目前不提供 binary 發行。**

已提供一次一個 Group、全部通道、自訂波形線色、Hex 封包內容、高／低脈寬、完整局部週期／頻率，以及跨通道 A/B edge／位置量測。
捲動通道時時間尺保持可見。局部頻率不代表整段資料具有穩定的時鐘頻率。

Activity 可比較全部已擷取通道在整段資料與目前視野的活動。
Capture issues 依回報的解析層列出異常；Locate issue 定位其證據並保留量測標記。
總覽顯示目前 Group 的原始訊號變化，包含未解析的控制訊號。

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

點上方來源摘要，可切換 **Board bring-up**（50 MS/s）與 **Timing + gaps**（10 MS/s）。
後者有不同資料與時鐘速度、讀取結束、位址 NACK 及缺資料區間。
開啟範例會清除分析狀態，保留 Group／線色偏好。
詳見[離線範例與驗證數值](docs/OFFLINE-EXAMPLES.md)。

先選 Group 或 All signals。指向脈衝可預覽量測，點一下固定結果。
Measure A/B 依序選兩個參考點；Position 可量指定的取樣位置。
Esc 取消選點或尚未提交的數值修改。Ctrl＋滾輪縮放，拖曳波形空白處平移。
選封包可看內容，Locate source 才會移動時間視野；Fit time 顯示完整時間範圍。

Measure A/B 會將焦點移到波形：上下鍵換訊號、左右鍵逐個 observed edge 移動
（Position 模式每次一個 sample），Enter 選點。未在選點時，Enter 固定游標處的局部脈衝量測。
A/B 在同一時間仍有分開的時間尺把手。輸入數字會設定 Position；錯誤輸入會保留已提交的量測。

Activity 的 Show 顯示指定通道，不改時間範圍。
若自訂 Group 已不包含解析器所需通道，明確定位來源／異常時會建立來源 Group。

實體探針色條固定。點訊號名稱旁獨立的短線可改波形顏色。
Group／線色偏好會保存；量測與擷取工作階段尚未保存。

已操作驗證 Windows 原生介面。其他桌面平台、完整 DPI 組合及大資料效能仍待驗證。
合成範例不能證明真實裝置的時間精度或解析器正確性。

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
