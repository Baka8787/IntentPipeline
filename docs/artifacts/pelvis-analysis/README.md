# pelvis-analysis — 研究工具（docs/45 §10–§18）

離線分析腳本。資料來源一律是 PlayMode 研究探針 `Assets/_Project/Tests/PlayMode/LandingSurfaceProbe.cs`（全部 `[Explicit]`，只量測）。
副檔名 `.py.txt`／`.eval.cs.txt` 是刻意的：避免被 Unity／IDE 當成專案原始碼。

## 產生資料

```bash
bash docs/artifacts/editor-memory/safe_unity_test.sh --mode PlayMode --filter "Project.Tests.PlayMode.LandingSurfaceProbe.<方法>" --include-explicit --timeout 1800   # 經安全入口（docs/53 §17）
```

| 方法 | 輸出（`Temp/LandingSurface/`） | 用於 |
|---|---|---|
| `Probe_DiagonalStopLanding` | `land_*.csv`、`sem_*.csv`（208 次，約 15 分鐘；只跑 Run 把 `Gait` 的 `[Values]` 改成 `Gait.Run`） | §10–§15 |
| `Probe_LandingResolverAB` | `lsr0_*`／`lsr1_*`（416 次，約 60 分鐘） | §17 |
| `Probe_FootLengthReferenceAB` | `flr0_*`／`flr1_*`（416 次，約 60 分鐘） | §18 |

⚠️ **Unity 重啟會清空 `Temp/`**——跑完立刻複製出來。

輔助輸入（Editor 匯出；`unity cmd eval_file` 只接受 `.cs` 副檔名 ⇒ 先複製成 `.cs` 再執行），輸出同樣寫到 `Temp/LandingSurface/`：
- `export_stair_edges.eval.cs.txt` → `edges.csv`（兩道樓梯的真實階緣；`semantics*`、`early` 需要）
- `export_bake_disp.eval.cs.txt` → `disp.csv`（4 支 Stop clip 的烘焙水平位移；`timebase` 需要）

## 腳本（`python <腳本> <csv 目錄> [...]`；Windows 終端機加 `PYTHONIOENCODING=utf-8`）

| 腳本 | 內容 | 章節 |
|---|---|---|
| `landing.py.txt` | 擺盪腳落腳面何時切到最終踏面 | §10 |
| `resolver.py.txt` | destination resolver 策略離線重播（其他腳本 import 它的落地定義） | §11–§12 |
| `bias.py.txt` | Run 煞車步 next-plant 偏差拆解 | §13 |
| `timebase.py.txt` | 姿勢 vs root 的時間基準（需 `disp.csv`） | §13 |
| `early.py.txt` | next plant 提早發布前後比較（兩個 csv 目錄＋ `edges.csv`） | §14 |
| `semantics.py.txt`、`semantics_b.py.txt` | T3 支撐語意重現與落地姿勢（需 `edges.csv`） | §15 |
| `outcome.py.txt` | A/B runtime outcome（`lsr`、`flr`） | §17 |
| `verify_window.py.txt` | 即時驗證窗門檻 | §17 |
| `yank_classes.py.txt` | 瞬拉（腳目標 − 動畫踝的二階差分）成因分類 | §18 |
| `footlength.py.txt` | 擺盪腳長參考 A/B | §18 |
| `r7_metrics.ps1.txt`（PowerShell） | 規則⑦ 前後的下樓停步指標（rise→drop／單帧最大／最終高度）；資料來自 `PlantSettleProbe.Probe_RuleSevenStairs` 的 `Temp/PlantSettle/r7_*.csv`。用法：`r7_metrics.ps1 -Pre <前> -Post <後>` | `docs/46` §19.1（2026-10-08） |

## 不在版控裡的
- `edges.csv.txt`：Editor 匯出的結果（可由 `export_stair_edges` 重產，已驗證逐位元相同）。
- `attrib`、`baseline`、`decomp`、`replay`、`stepflip_ab`、`brake`（docs/46 §2x–§34）：資料來源 `StopLandingProbe` 依賴已從正式程式移除的觀測欄位，只存在於 `.prototype-backup-20261003/` ⇒ 無法在目前版本重現。
