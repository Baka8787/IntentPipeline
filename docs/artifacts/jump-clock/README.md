# F2-J：Jump clock-family 量測（2026-10-08，measurement-only）

結論與解讀見 `docs/48` §7。本資料夾只放**可重現的原始資料與分析腳本**。

## 重現

```bash
unity cmd run_tests --mode PlayMode --async_tests true --include_explicit true --filter "Project.Tests.PlayMode.JumpClockFamilyProbe.Probe_JumpClockFamily"
```

- Probe：`Assets/_Project/Tests/PlayMode/JumpClockFamilyProbe.cs`（🔬 throwaway、`[Explicit]`、不改 runtime）。10 個情境 × 15 個 offset ＝ 150 次，約 12 分鐘。
- 輸出到 `Temp/JumpClock/`（⚠️ Unity 重啟會清空——跑完立刻複製出來）：每次一個逐帧 CSV（`<Case>_oNN.csv`）＋一個 summary（`summary_<Case>_oNN.csv`）。
- `summary_all.csv`＝本輪 150 個 summary 合併。逐帧 CSV 沒有收進 repo（約 3 MB，且 probe 可決定性重跑）。

## 腳本（PowerShell 5.1；`.txt` 只是為了不被當成可執行檔）

| 檔案 | 輸入 | 輸出 |
|---|---|---|
| `jc_summary.ps1.txt` | `summary_*.csv` 的資料夾 | 各情境的主導 loop／被取樣時鐘／Jump 家族統計、錯配列表 |
| `jc_visibility.ps1.txt` | 逐帧 CSV ＋ summary 的資料夾 | 可見性代理：起跳後 10 帧內「左右腳水平間距」的最大單帧變化，對照起跳前 9 帧的正常步態 |

⚠️ PowerShell 5.1 以 ANSI 讀沒有 BOM 的 UTF-8 腳本——中文註解會吃掉下一行。腳本因此全用 ASCII。

## summary 欄位

| 欄位 | 意義 |
|---|---|
| `dominant` | 上一帧 mixer 權重最大的 child（Idle／Walk／Run／Sprint）——與 `AnimancerFacade.TryGetDominantChildNormalizedTime` 同一規則 |
| `sampledClock`／`sampledChild` | `LocomotionModel` 本帧快照的時鐘值，以及它屬於哪個 child（與上一帧 child NT 比對） |
| `jumpTier` | `JumpState._takeoffTier`——Jump 用來挑 loop Bake 的家族（意圖 0.2／0.75 門檻） |
| `phaseUsed` | `JumpState._takeoffFootPhase`——Jump 讀到的腳相（決定 LU／RU） |
| `truthClockPhase` | 被取樣時鐘以**它自己那支 loop 的 Bake** 解讀的腳相 |
| `truthPosePhase` | 動畫 pose（pre-IK）實際較低的腳——混合區兩個不同步時鐘疊加後的結果 |
| `clockFamilyMismatch` | `sampledChild ≠ jumpTier` |
| `wrongVsClock`／`wrongVsPose` | `phaseUsed` 與兩種真值不同 |
