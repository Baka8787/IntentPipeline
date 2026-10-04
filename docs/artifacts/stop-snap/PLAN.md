# Stop「吸附感」診斷——量測計畫（2026-10-04；⛔ diagnosis only，runtime 不改）

> 本檔是交給 Codex 的實作規格，也是 `docs/45` §22 的方法紀錄。結果寫進 `docs/45` §22，不寫在這裡。

## 0. 要回答的兩題

1. **只看 raw Stop animation（沒有 T3）**，「腳在 plant 前被快速拉過去」的運動特徵是否存在？是不是 Stop clip 特有（對照 loop）？
2. **T3** 是製造、放大，還是只是忠實呈現這段快速收腳？

## 1. 已確認的現況（讀檔得到，Codex 不需重查）

| | RunStop_LU | RunStop_RU | WalkStop_LU | WalkStop_RU |
|---|---|---|---|---|
| Bake | `Bake_RunFwdStop_LU` | `Bake_RunFwdStop_RU` | `Bake_WalkFwdStop_LU` | `Bake_WalkFwdStop_RU` |
| clip 長度 | 1.267 s | 1.500 s | 1.333 s | 1.533 s |
| playback speed p | **1.3124558** | **1.2588** | **1.3327742** | **1.3327742** |
| fade in | 0.15 s | 0.15 s | 0.25 s | 0.25 s |
| Bake plant 排程（clip 秒） | L 0.30 → R 0.60 → **L 1.083** | L 0.567 → L 0.767 → R 1.017 | (L 0) → R 0.267 → L 0.717 | (L 0) → R 0.267 → L 0.650 → R 1.067 |

- p 與 loop 相同（Walk loop 1.3328、Run loop 1.3125）⇒ 停步並沒有比 loop 播得更快；**runtime 秒 ＝ clip 秒 ／ p**。
- root 由 `MotionDriver` 依 Bake 位移推進（與 clip 同一 playhead）⇒ pose 與 root 同 time-base；§19.2 已驗證 root 與 Bake 一致。
- Bake plant 定義：clip root-motion 世界中腳水平速度 ≤ 0.20 m/s（clip 時間）＋高度容差 2.5 cm。
- **T3 鎖定**：β ＝ stance ×（1 − smoothstep((v − 1)/(4 − 1)))，v ＝ 動畫腳**世界**水平速度（一帧外插的速度）；β ≥ 0.95 ⇒ Locked。
  ⇒ 只要 stance≈1，**動畫腳 ≤ 約 1.4 m/s（≈ 2.3 cm／帧）就會鎖**，而且鎖定點是**一帧外插後**的位置。Bake plant 是 ≤ 0.2 m/s。
  這是 H2 的具體機制候選，必須量：鎖定當帧動畫腳速度與殘餘行程。
- X Bot 現行開關：resolver 開、SwingFootLengthReference 開、`alignRunEntryPhase` 開、`PlantAwareExtrapolation`（A2／P8b）**關**。

## 2. 每帧時序（決定三條軌跡怎麼對齊）

```
Update（Core 順序 1–5）
→ Animator 評估 ＋ OnAnimatorIK：FootIKRig 擷取 pose（FK，GetIKPosition）、套用「上一帧」的 IK 目標與骨盆偏移
→ LateUpdate 順序 6：MotionDriver 移動 root（Δroot）── 骨骼跟著 root 一起平移
→ LateUpdate 順序 6.5：T3 Tick 用本帧 pose 算出目標（下一帧才套用）
→ Recorder LateUpdate（order 32000）
```

因此在第 f 列（全部是**畫面時間基準**，＝玩家在第 f 帧看到的）：

| 軌跡 | 定義 |
|---|---|
| **Root** | `root(f)`（LateUpdate 後） |
| **A — Raw animation** | `A(f) = pose(f) + (root(f) − rootEval(f))`；`rootEval(f)` ＝ 本帧動畫評估時的 root |
| **B — T3 target（實際套用的）** | `B(f) = tgt(f−1) + (root(f) − rootEval(f))` |
| **C — Displayed** | `C(f) = bone(f)`（最終腳骨骼） |

`rootEval(f)` 直接量：在 `Update`（`DefaultExecutionOrder(-32000)`）記 root。⛔ 不要用 `root(f−1)` 推（那是假設，不是量測）。

## 3. 三個 A 來源（互相驗證）

- **A0 — 離線純 clip**（Editor-only，不進 Play）：與 `MotionBakeEditor` 同法 `clip.SampleAnimation(animator.gameObject, t)`、`applyRootMotion = true`、120 Hz；4 支 Stop ＋ `RunFwdLoop`／`WalkFwdLoop`。沒有 crossfade、沒有 IK——**clip 內容本身**。
- **A1 — runtime pre-IK pose**（IK 開的 run 中，§2 的 `A(f)`）。
- **A2 — runtime IK 零輸出反事實**：同一情境，但每帧在 T3 Tick 之後把 `FootIKTargetData` 的腳位置／旋轉權重（＋膝 hint 權重若有）與 `PelvisOffsetY` 歸零 ⇒ 下一帧 OnAnimatorIK 不套任何 IK，**pose 仍照常擷取**。此時 `C(f)` 就是「沒有 T3 時玩家會看到的腳」。

## 4. 驗證（任何結論之前先過）

| # | 檢查 | 通過條件 |
|---|---|---|
| V1 | A2 run：`|C(f) − A(f)|`（水平） | 中位 ≤ 0.3 cm、無時間相依漂移（若有固定偏移，報出並改用差分） |
| V2 | 同一情境 IK 開／零輸出：root 軌跡、停步變體、stopNT | 逐帧相同（Core 不依賴 IK） |
| V3 | IK 開 run：`|C(f) − B(f)|`（水平），posW＝1 的帧 | 中位 ≤ 0.5 cm（確認 B 的對齊） |
| V4 | 最後一次 plant（純 clip 段）：A1 的 root-space 腳踝 vs A0 同一 clip 時間（`stopNT × duration`） | ≤ 1 cm（確認 runtime 沒有扭曲 clip，時間換算正確） |

## 5. 情境（第一輪，少量）

| 組 | 內容 | 次數 |
|---|---|---|
| Flat | Walk／Run × 放開位置 o0–o5（自然出現 LU 與 RU）× {IK 開, 零輸出} | 24 |
| Flat P8b | Run × o0–o5 × `PlantAwareExtrapolation` 開 | 6 |
| Stairs | proto 樓梯、yaw 0、Run × {Up, Down} × stopIndex {2, 6, 10}，IK 開 | 6 |
| Loop control | 每個 flat run 在放開前的穩定段（起步 ≥ 90 帧後）也錄 | 同上 |
| Offline | A0：4 Stop ＋ 2 loop | 6 clip |

## 6. 事件與指標（離線，per foot per plant）

**事件定義（只用 A，不依賴 T3）**
- `vA(f)`：A 的世界水平速度（帧差 ／ dt）。`vA_rel`：root-space 版本（輔助）。
- **anim plant `tP`**：擺盪後第一個 `vA ≤ 0.25 m/s` 且連續 3 帧的帧（敏感度：0.5 m/s 另報）。
- **swing 起點 `tS`**：`tP` 之前、`vA > 0.5 m/s` 的最大連續區間起點；swing 行程 `D` ＝ A 在 [tS, tP] 的路徑長。
- **T3 lock `tL`**：該腳在 tS 之後第一次 `Phase == Locked`。**release**：之後第一次離開 Locked。
- 每個 plant 對到 Bake 排程事件（停步 clip 時間 ±3 帧）並標 `eventIdx`（煞車步／最後一步／LU re-step）。

**指標（A、C 各算一次；B 只用於 lock 時刻與差值）**
1. 剩餘行程：`tP` 前 200／100／50 ms（12／6／3 帧）的路徑長（cm），及佔 `D` 的 %。
2. 集中度：[tS, tP] 最後 20% 時間完成的行程 %。
3. peak 水平速度（m/s）。
4. peak 減速（m/s²，3 帧中心差分）、peak 減速到 `tP` 的時間（ms）。
5. **lock 殘餘**：`tL − tP`（帧，負＝提早鎖）、`vA(tL)`、A 從 tL 到 tP 的路徑長、`|lockXZ − A(tP)|`、鎖定期間 A 的總滑移（tL→release）。
6. **T3 放大**：`R_C(100ms) ／ R_A(100ms)`、`peakDecel_C ／ peakDecel_A`、鎖定前後 C 的速度跳變（`vC(tL−1)` vs `vC(tL+1)`）、鎖定前 6 帧 `max|C − A|`、tL→release 的 `max|C − A|`。
   分類：**early-freeze**（tL < tP 且殘餘 > 3 cm）／**pull**（C 的末段行程或減速 > A 的 1.25 倍）／**faithful**（|C − A| ≤ 1 cm 直到 tP，且 |tL − tP| ≤ 1）／**extra**（其他）。
7. Stairs 另報：C 與 A 的垂直位移（最後 100 ms）、最後 100 ms 內腳下 live 支撐是否換階。

**輸出**：`plants.csv`（一列一個 plant，含 gait／variant／side／eventIdx／terrain／arm 與上列全部欄位）、`summary.md`（Run loop vs Run Stop、Walk loop vs Walk Stop 對照表，四支 Stop 分開、平地與樓梯分開）、`traj_<case>_<side>_<event>.csv`（tP 前 500 ms 到後 300 ms：t_ms、A／B／C／root 的 XZ 與「到 A(tP) 的水平距離」、vA、vC、phase、β）供畫圖。

## 7. Codex 實作範圍

**✅ 只允許改／新增**
- `Assets/_Project/Tests/PlayMode/PlantSettleProbe.cs`
  - `Drive` 加 `Gait gait = Gait.Run` 參數（Walk ＝ 全程按住 `leftCtrl`，同 `LandingSurfaceProbe`）；**Run 的檔名與現有所有方法的行為逐位元不變**（label 的 `_Run_` 改成由 gait 產生，Run 時字串相同）。
  - 新 `[UnityTest, Explicit]`：`Probe_StopSnapFlat(Gait gait, int offset 0–5, string arm ∈ {"on","zero","p8b"})`、`Probe_StopSnapStairs(Dir dir, int stopIndex ∈ {2,6,10})`。輸出前綴 `snap_`。
  - Recorder：**只在列尾追加**欄位 `rootEvalX,rootEvalY,rootEvalZ,ikZeroed`，以及每腳 `toeX,toeY,toeZ`（`HumanBodyBones.LeftToes/RightToes`，取不到就 NaN）、`stopBake`（停步 runtime 目前變體的 BakeData 名稱，反射取不到就空字串）。⛔ 不改既有欄位順序。
  - `RootAtUpdate` 小元件（`[DefaultExecutionOrder(-32000)]` 的 `Update` 記 root）；零輸出介入＝recorder 之後（或同一 LateUpdate 末端）把 `FootIKTargetData` 的腳權重／hint 權重／`PelvisOffsetY` 歸零（先讀 `FootIKRig` 確認它消費哪些欄位，全部歸零）。
  - flat 模式：從起步滿 90 帧起全程錄（loop 對照）。
- `docs/artifacts/stop-snap/`（新目錄）
  - `sample_clips.eval.cs.txt`：A0 離線取樣（只讀資產；`HideAndDontSave` 實例、用完 `DestroyImmediate`；輸出 `Temp/StopSnap/clips.csv` ＋ `schedule.csv`（4 支 Stop 的 duration、p、排程事件））。同時檢查並輸出 locomotion mixer 子狀態的 clip 是否就是 loop Bake 的 `SourceClip`。
  - `analyze.py.txt`：§6 全部（Python 標準函式庫；**沒有 matplotlib**）。先跑 §4 驗證並把結果印在 `summary.md` 最上方；驗證不過就在 summary 標紅、仍輸出數字。
  - `README.md`：怎麼產生資料、怎麼跑分析。

**⛔ 禁止**：`Assets/Scripts/**` 任何改動；任何 `.asset`／`.prefab`／`.unity`／`.meta`（新 `.cs` 檔的 `.meta` 由 Unity 產生——本輪不新增 `.cs` 檔，全部寫在 `PlantSettleProbe.cs` 內）；改 Stop clip、playback speed、fade、T3 參數、LockBeta／UnlockBeta、resolver、Bake plant 定義、MotionDriver；改既有 probe 方法的行為；git 任何操作。

**完成條件**：EditMode／PlayMode 測試組件可編譯（`dotnet build` 或 Unity recompile 無錯）；回報 diff 摘要。**資料由 Claude 執行，Codex 不跑 PlayMode。**
