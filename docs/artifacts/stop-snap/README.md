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
bash docs/artifacts/editor-memory/safe_unity_test.sh --mode PlayMode --filter "Project.Tests.PlayMode.PlantSettleProbe.Probe_StopSnapFlat" --include-explicit --timeout 1800   # 經安全入口（docs/53 §17）
bash docs/artifacts/editor-memory/safe_unity_test.sh --mode PlayMode --filter "Project.Tests.PlayMode.PlantSettleProbe.Probe_StopSnapStairs" --include-explicit --timeout 1800   # 經安全入口（docs/53 §17）
```

輸出位於 `Temp/PlantSettle/snap_*.csv`：

- Flat：從起步第 90 幀起連續記錄，包含放開前的 loop control、Stop 與既有 restart 段。
- Stairs：proto 樓梯、yaw 0、Walk/Run、IK 開；Down/Up × stopIndex 2/6/10，同樣從起步第 90 幀起連續記錄。
- 每列的既有 schema 完全保留；列尾追加 `posePlaneY,lstance,lprevValid,rstance,rprevValid`。`stance` 與 runtime 同樣由 pose goal 高度減 `posePlaneY` 後呼叫 `FootIKController.ComputeFootWeight`；`prevValid` 是該 tick 結束時的值（供下一 tick `StepLock` 使用）。

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

## 5. T3 offline replay

`replay.py.txt` 只讀新錄的 `snap_on_*.csv`，先以 X Bot prefab 參數重播 R0。每個 case 的前 20 列只用來暖機，不納入 R0 相位／水平殘差檢查；若相位一致率低於 99.5% 或水平殘差 p99 高於 0.1 cm，會停止而不跑候選。舊 CSV 缺少 `stance`／`prevValid` 時會明確報錯，不會推測。

```powershell
# 預設跑 R0 與全部展開候選，輸出寫回輸入目錄
$env:PYTHONIOENCODING='utf-8'
python docs/artifacts/stop-snap/replay.py.txt Temp/PlantSettle

# 查看可單跑的完整 policy id
python docs/artifacts/stop-snap/replay.py.txt --list-policies

# R0 正確性通過後，只跑指定候選（輸出仍含 R0 對照）
python docs/artifacts/stop-snap/replay.py.txt Temp/PlantSettle --policy R1a-r0.06

# 組合只接受「一個完整 R2 id:一個完整 R1 id」，由研究者明確挑選
python docs/artifacts/stop-snap/replay.py.txt Temp/PlantSettle `
  --combo R2a-slow-K3-v0.5:R1a-r0.06 --output-dir Temp/StopSnap/replay
```

輸出：

- `replay_summary.md`：開頭列出實際使用的 X Bot `FootIKSettings` 與 R0 正確性；其後每個 policy 一列彙總 M1–M7。M2 是畫面／動畫路徑比，M4 只量 R0 判為 Locked 的幀，M7 單位是 cm/frame²。
- `replay_events.csv`：一列＝policy × R0 lock episode。識別欄固定含 `case,gait,terrain,side,context,ordinal,tRel,isLastRelease`；`context` 只會是 `loop` 或原始 `stopBake` 名稱，不另造情境分類。`ordinal=0` 表示 lock 發生在 stop 前；stop 內由 1 起算。`tRel` 是 R0 release transition 相對輸入放開（首列 `phase=stop`）的幀數；錄製結束仍未 release 的 episode 留空。`isLastRelease` 只標該腳 stop 內最後一次 R0 release。
- 其餘事件欄：R0／policy lock、release frame；M1 gap；M2 十幀 path/peak ratio；Walk RU 右腳的 M3；R0 Locked window 的 M4；`lockev.py.txt` 定義的 M5；該 R0 episode window 內的 M6；以及 case 全段 M7（同 case 的事件列會重複此兩值，方便分組）。

重播軌跡沿用 §3 的 goal 空間定義：`A(f)=pose(f)+rootDelta(f)`，`C'(f+1)=replayedHorizontal(f)+rootDelta(f+1)`。`continuous` 使用 3D pose 位移；垂直接觸、resolver、lift 與骨盆不在本 replay 範圍。

目前 schema 沒有 `ikAllowed`；probe 情境預期全程 grounded 且未被 arbitration 阻擋。若實際 run 曾進入 runtime 的 `ikAllowed=false` 整體重設，replay 不會猜測，R0 相位／水平正確性 gate 應直接失敗。R3 的「動畫腳本幀位移」採 T3 內部的 `predicted.xz` 位移，因限速發生在加上下一幀 `rootDelta` 之前。

### 5.1 第一輪 replay 的執行紀錄（2026-10-04，Claude）

- 重錄：Flat 30（只用 `on` 臂 12 個）＋ Stairs 12（Walk／Run × Up／Down × 2／6／10）。R0 gate：相位 **99.953%**（3 帧，同一個 Run 下樓 β≈0.95 捨入）、水平殘差 p99 **0.02 cm** ⇒ PASS。
- 修正：`finite()` 對未匹配事件的空字串（`""`）會丟 TypeError ⇒ 改為只接受數值。
- 分組腳本（在 replay 輸出目錄外執行）：
  - `replay_keycols.py.txt <replay_summary.md>`：每 policy 一列的關鍵欄。
  - `replay_groups.py.txt <events.csv[,events2.csv]> <policy,policy,…>`：依 `gait／context／side＋(*＝isLastRelease)＋ordinal／flat|stairs` 分組；欄位＝gap、補回路徑比、peak 比、M4 可見爬行（中位／max）、M4 單帧 max、M5 過衝／回拉。
- `data/replay_summary.md.txt`：19 個候選＋`R2a-slow-K2-v0.3:R1c-follow-d0.03` 組合的彙總。`replay_events.csv`（600 KB）未存。

## 6. R1c runtime A/B

`PlantSettleProbe.Probe_LockFollowFlat`／`Probe_LockFollowStairs` 產生 `lf_cur_*`、`lf_f3_*`、`lf_f5_*`：
`cur` 關閉實驗，`f3`／`f5` 分別開啟並使用 0.03／0.05 m dead zone。介入只改載入場景的
`FootIKSettings` 實例，不存 prefab；三臂都要求 `PlantAwareExtrapolation`（P8b）保持關閉。

Recorder 的 `tgtX/tgtZ` 已經是 `ResolveHorizontal` 之後、本帧 T3 寫入 `FootIKTargetData` 的 effective 水平位置；
後續只會解算同一目標的 Y。因此不另增重複的 `effX/effZ`。尾欄 `hipsY` 是最終 Hips 世界 Y。

```powershell
$env:PYTHONIOENCODING='utf-8'
python docs/artifacts/stop-snap/ab.py.txt Temp/PlantSettle

# 若離線 replay summary 不在預設的 docs/artifacts/stop-snap/data/：
python docs/artifacts/stop-snap/ab.py.txt Temp/PlantSettle `
  --replay-summary Temp/StopSnap/replay/replay_summary.md `
  --output-dir Temp/StopSnap/ab
```

輸出：

- `ab_events.csv`：一列＝arm × `cur` lock episode；metadata 沿用 replay 的
  `case,gait,terrain,side,context,ordinal,tRel,isLastRelease`，其他臂在 stop-relative `[L−6, R＋12]` 內配對。
- `ab_summary.md`：Known bad／good、loop、樓梯垂直、lock／release 次數與 offline replay 對照；
  每表都含 `f3 − cur`、`f5 − cur`。gap 差超過 1 cm 或 path ratio 差超過 0.1× 會標 `CHECK`。
- 軌跡沿用既有 goal-space 定義：`A = pose + (root − rootEval)`、`C = bone`。

### 6.1 第一輪 A/B 紀錄（2026-10-05，Claude）
- 72 次全過（Flat 36、Stairs 36）；`data/ab_summary.md.txt` 為彙總。逐案例（known bad／good × terrain × arm）：`python ab_cases.py.txt <ab_events.csv>`。
- `ab.py.txt` 的「Offline replay comparison」需要 `--replay-summary docs/artifacts/stop-snap/data/replay_summary.md.txt`；本輪人工比對：Run LU re-step、Walk LU／RU 最後一步的補回比與 §23 replay 完全相同。
