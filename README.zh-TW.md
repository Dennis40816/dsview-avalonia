<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# dsview-avalonia

本獨立專案規劃以 Avalonia 開發邏輯分析儀介面，使用 DSView 的擷取與解碼函式庫。

本頁為中文導覽。專案文件以 [英文 README](README.md) 為準。

## 目前狀態

目前是 repository 基礎建置階段。**授權清查尚未完成，目前不提供 binary 發行。**

此版本只有授權、repository 文件與固定版本的上游來源指標。
尚未包含可執行的 Avalonia 程式、native bridge、解析器實作或已驗證的建置流程。

介面規劃以波形為主，包含清楚的字體、多層解碼標註、可展開的封包內容與命名時間量測。
這些是開發目標，尚非本版本已實作的能力。

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
