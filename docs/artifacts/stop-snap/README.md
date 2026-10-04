# stop-snap — Stop「吸附感」診斷工具

本目錄實作 `PLAN.md` 的 A0/A1/A2 量測；只產生研究資料，不修改 runtime、動畫或資產。
`.eval.cs.txt`／`.py.txt` 是刻意使用的副檔名，避免 Unity 或 IDE 把它們納入專案編譯。

## 1. A0：離線純 clip 取樣

`unity cmd eval_file` 只接受 `.cs`，先複製到 `Temp/StopSnap/` 再執行：

```powershell
New-Item -ItemType Directory -Force Temp/StopSnap | Out-Null
Copy-Item docs/artifacts/stop-snap/sample_clips.eval.cs.txt Temp/StopSnap/sample_clips.eval.cs
unity cmd eval_file --file Temp/StopSnap/sample_clips.eval.cs
Remove-Item -LiteralPath Temp/StopSnap/sample_clips.eval.cs
```

輸出：

- `Temp/StopSnap/clips.csv`：4 支 Stop ＋ Run/Walk loop，以 120 Hz、`AnimationClip.SampleAnimation` 取樣 root、踝與 toe bone。
- `Temp/StopSnap/schedule.csv`：4 支 Stop 的 duration、playback speed、plant 排程，以及 X Bot `Locomotion.asset` 每個 mixer child 是否等於對應 loop Bake 的 `SourceClip`。

取樣器只讀資產；暫時角色使用 `HideAndDontSave`，每支 clip 完成後立即 `DestroyImmediate`。

## 2. Runtime：PlayMode probe

所有方法都是 `[Explicit]`。平地方法展開 Walk/Run × o0–o5 × on/zero/p8b；依 PLAN，Walk+p8b 會明確 `Ignore`，有效資料為 30 組。`zero` 仍正常跑 T3 與 pose 擷取，只在每列記錄後把下一幀會套用的腳 IK 權重與骨盆偏移清零。

```powershell
unity cmd run_tests --mode PlayMode --async_tests true --include_explicit true --filter "Project.Tests.PlayMode.PlantSettleProbe.Probe_StopSnapFlat"
unity cmd run_tests --mode PlayMode --async_tests true --include_explicit true --filter "Project.Tests.PlayMode.PlantSettleProbe.Probe_StopSnapStairs"
```

輸出位於 `Temp/PlantSettle/snap_*.csv`：

- Flat：從起步第 90 幀起連續記錄，包含放開前的 loop control、Stop 與既有 restart 段。
- Stairs：proto 樓梯、yaw 0、Run、IK 開；Down/Up × stopIndex 2/6/10。
- 每列的既有 schema 完全保留；新增資料只追加在列尾。

⚠️ Unity 重啟會清空 `Temp/`；跑完應立即保存所需 CSV。

## 3. 分析

只使用 Python 標準函式庫：

```powershell
$env:PYTHONIOENCODING='utf-8'
python docs/artifacts/stop-snap/analyze.py.txt Temp/PlantSettle Temp/StopSnap/clips.csv --schedule Temp/StopSnap/schedule.csv --out Temp/StopSnap/analysis
```

輸出：

- `plants.csv`：每腳、每次 plant 一列，含 PLAN §6 的 A/C 指標、lock 殘餘、T3 放大、分類與樓梯垂直/支撐換階欄位。
- `summary.md`：最上方先列 V1–V4；失敗會標成紅色但仍繼續輸出全部數字。其後列 Run/Walk loop vs Stop、四支 Stop、flat vs stairs 與 mixer/Bake clip 一致性。
- `traj_<case>_<side>_<event>.csv`：每個 plant 的 tP 前 500 ms 到後 300 ms，含 A/B/C/root XZ、到 `A(tP)` 的距離、速度、phase 與 beta。

分析器只讀取 runtime 目錄中的 `snap_*.csv`，不會混入舊的 `settle_`、`xab*` 或 `phase_*` 資料。

### 軌跡定義

- `A(f) = pose(f) + (root(f) - rootEval(f))`
- `B(f) = target(f-1) + (root(f) - rootEval(f))`
- `C(f) = displayed foot bone(f)`

V4 的 runtime CSV 依 PLAN 只新增 root position，沒有 root rotation；分析器因此用 plant 前後 7 個 sample 做單一最小平方 yaw 對齊，再比較 3D root-space ankle。它不以單點自行配準。

## 4. 第一輪執行後的修正與注意（2026-10-04，Claude）

- **`pose` 是 humanoid IK goal 空間，不是骨骼空間**：`GetIKPosition` 與最終 `LeftFoot/RightFoot` 骨骼差約 6 cm（±1.5 cm，隨腳的朝向變）。IK 開時骨骼正好落在目標上（V3＝0.000 cm），所以 **A／B／C(on) 一律在 goal 空間比較**；zero arm 的骨骼與 A0（離線骨骼）在骨骼空間比較。V1 的「FAIL」就是這個固定偏移，不是時序錯誤。
- V4 改用 zero arm 的 `bone − root`（骨骼空間）對 A0：中位 0.3–0.6 cm、max 1.15 cm ⇒ runtime 的最後一步就是 clip 內容。
- V2 的通過門檻太嚴：on／zero 的 root 差 ≤ 0.07 cm、stopNT 與變體完全相同 ⇒ 實質通過。
- `analyze.py.txt` 已修：鎖定＝「進入」Locked 的那一帧（原本會拿到上一個支撐期還沒釋放的鎖）；離線時間 ÷ playback speed；loop 接一個循環避免邊界截斷。
- ⚠️ `analyze.py.txt` 的自動 plant 偵測（≤ 0.25 m/s 連 3 帧）會把停步中的慢速爬行與下一步合併，`plants.csv` 的停步事件分類不可直接引用。**結論以下列三支腳本為準**（在含 `snap/` 子目錄的資料夾內執行）：
  - `tl.py.txt <csv> <l|r>`：單腳逐帧（放開後 t、相位、β、A／B／C 到最後位置的距離、C−A、每帧位移）。
  - `lockev.py.txt`：每次「進入 Locked」——動畫腳的急停、畫面過衝與回拉、鎖下動畫位移、釋放時的差距。
  - `relev.py.txt`：每次「釋放」——鎖下動畫走了多少、釋放時 C−A、釋放後 10 帧畫面／動畫路徑比。
  - `lastf.py.txt`：鎖定前後 12 帧的 A／C 每帧位移並排。
- A0 的 `eval_file` 會撞 5 秒主執行緒逾時，但背景仍會跑完並寫檔；保險做法是每支 clip 各跑一次（替換 `foreach` 開頭加 `if (spec.Name != "...") continue;`）。
- `data/plants.csv.txt`：第一輪 `analyze` 輸出備份（36 個 runtime case＋6 支 clip）；原始 probe CSV（5 MB）未保存進 repo。
