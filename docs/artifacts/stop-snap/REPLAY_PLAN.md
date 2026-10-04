# T3 release-catch-up — offline replay 計畫（2026-10-04；⛔ runtime 不改）

> 使用者裁決（§22 之後）：開 T3 release-catch-up slice；**先用資料做 offline replay，不改 runtime**。
> 優先：**更早 release／避免 gap 累積**；其次：**lock 加 movement-stability 條件**；speed cap 只作 safety candidate。
> Walk RU 15 cm hidden step 不另開修，作為**同一 policy 的 acceptance case**。P8b／A2 維持獨立，不合併。
> 結果寫進 `docs/45` §23；本檔是方法與 Codex 規格。

## 1. 被重播的 T3 邏輯（`FootContactSolver` ＋ `FootIKController.ResolveContactPosition` ①②）

每帧、每腳（dt ＝ 1/60）：
```
continuous = HasHistory && |pose − prevPose| ≤ MaxPoseStepPerFrame(0.5)      # 3D
if continuous: step = pose − prevPose; predicted = (pose.x+step.x, pose.y, pose.z+step.z); speed = |step.xz|/dt
else:          predicted = pose; speed = 0; Phase = Unlocked; ReleaseBlend = 0; PrevContactValid = false
prevPose = pose; HasHistory = true
beta = clamp01(stance) × (1 − smoothstep(clamp01((speed − 1)/(4 − 1))))
StepLock(beta, predicted, dt, LockBeta 0.95, UnlockBeta 0.7, UnlockRadius 0.2, ReleaseTime 0.12)   # 見 FootContactSolver.StepLock
horizontal = ResolveHorizontal(state, predicted)                                    # Locked→LockPosition；Releasing→pred + smoothstep(blend)×ReleaseOffset
PrevContactValid = liveHit(f)                                                         # 本帧值，下一帧 StepLock 才用
```
- `PlantAwareExtrapolation`（P8b）在 X Bot 預設關 ⇒ 不重播（只用 `on` 臂的資料）。
- 垂直（lift、ResolveHeight、resolver、骨盆）**不重播** ⇒ 本輪只比水平；樓梯垂直留給之後的 runtime A/B。

**畫面腳（重播版）**：`C'(f+1) = horizontal(f) + (root(f+1) − rootEval(f+1))`（與 §22 的 B 同一定義；V3 已證 IK 開時骨骼＝套用的目標）。
**動畫腳**：`A(f) = pose(f) + (root(f) − rootEval(f))`。

## 2. 重播正確性（候選之前必須先過）

- R0（現行參數）重播 vs 錄到的值：**相位逐帧一致率**（`lph`／`rph`）與 **`horizontal` vs `tgtX/tgtZ`** 殘差（中位、p99、max）。
- 目標：相位 ≥ 99.5%、水平殘差 p99 ≤ 0.1 cm；不一致的帧要列出（前 20 帧為狀態暖機，排除）。
- 不過就停下來回報差在哪，**不要調候選**。

## 3. 候選 policy（全部在重播器內參數化；⛔ 不碰 runtime）

**R1 — 更早 release／避免 gap 累積（首選族）**
| id | 規則 |
|---|---|
| R1a-r | `UnlockRadius` ∈ {0.10, 0.06, 0.04, 0.03} m（其餘不變） |
| R1b-trend | Locked 中，若動畫腳**離鎖定點的水平距離連續 N 帧遞增**且已 > d0 ⇒ 開始 release。N ∈ {3, 5}，d0 ∈ {0.02, 0.03} m |
| R1c-follow | Locked 中鎖定點**跟隨**：`gap = predicted.xz − lock.xz`；若 \|gap\| > d0 ⇒ `lock.xz += gap × (1 − d0/|gap|)`（gap 永遠 ≤ d0，超出部分以動畫速度可見）。d0 ∈ {0.02, 0.03, 0.05} m。release 規則不變 |

**R2 — lock 加 movement-stability 條件（次選族）**
| id | 規則 |
|---|---|
| R2a-slow | 進入 Locked 另需最近 K 帧水平速度都 < v_s。K ∈ {2, 3}，v_s ∈ {0.3, 0.5} m/s |
| R2b-settle | 進入 Locked 另需最近 K＝3 帧速度**不遞增**（每帧 ≤ 前一帧 ＋ 0.05 m/s）且 < 1.0 m/s |
| R2+R1 | R2 最佳 ＋ R1 最佳的組合（各族選出後再跑） |

**R3 — speed cap（safety，只作參考）**
| R3-cap | Releasing 中：本帧目標相對上一帧目標的水平位移 ≤ max(k × 動畫腳本帧位移, v_min·dt)，k ∈ {1.0, 1.25}，v_min ＝ 0.5 m/s；blend 依此重算（不足的部分留到下一帧） |

## 4. 指標（每個候選 × 每個事件；分組：loop／Run 煞車步／Run LU re-step／Run RU 最後一步／Walk 煞車步／Walk 最後一步／Walk RU hidden step；平地與樓梯分開）

**要變好的**
- M1 釋放時 \|C'−A\|（gap，cm）。
- M2 釋放後 10 帧畫面／動畫路徑比，與 peak 畫面／動畫每帧位移比。
- M3 **Walk RU hidden step**：停步結束時 \|C'−A\|（cm）與該小步的畫面路徑／動畫路徑。

**不能變壞的（成本）**
- M4 **可見滑步**：R0 判為 Locked 的帧裡，C' 的水平路徑（cm）與最大每帧位移——即「T3 原本藏起來、候選放出來的動畫爬行」。分 loop 支撐期與停步支撐期。
- M5 **落地**：每次進入 Locked 前後 ±4 帧 C' 相對動畫停點的過衝與回拉（`lockev.py.txt` 同定義）；鎖定時刻相對 R0 的帧差。
- M6 **鎖定次數與鎖定總帧數**（R2 可能讓腳完全鎖不住）；每次鎖定的長度分佈。
- M7 畫面腳水平 jerk：每帧二階差分的 p95／max（全段，與 R0 比）。

**驗收門檻（草案，回報時一起檢討）**：停步最後一步 M2 ≤ ×1.15（≈ loop）；loop 的 M4 不比 R0 多 > 1 cm／步；M5 過衝不比 R0 大；M6 loop 每步仍有鎖定；Walk RU hidden step 的處理方式由數據呈現，不預設必須「看得見」。

## 5. 資料（需要重錄）

Recorder 列尾追加（不改既有欄位）：`posePlaneY`，每腳 `stance`（＝ `ComputeFootWeight(poseY − posePlaneY, FootGroundedHeightMin, FootGroundedHeightMax)`）、`prevValid`（tick 後的 `FootContactState.PrevContactValid`）。
`snap_` 樓梯 run 也從起步 ≥ 90 帧全程記錄（重播需要從 loop 開始暖機）；`Probe_StopSnapStairs` 加 `Gait` 參數（Walk、Run）。
重錄：Flat（on 臂即可，zero／p8b 不需要）＋ Stairs（Walk／Run × Up／Down × 2／6／10）。

## 6. Codex 實作範圍

✅ `Assets/_Project/Tests/PlayMode/PlantSettleProbe.cs`（§5；Run 既有輸出檔名不變）；`docs/artifacts/stop-snap/replay.py.txt`（§1–§4，Python 標準函式庫；輸入 `snap_on_*.csv` 目錄；輸出 `replay_summary.md`、`replay_events.csv`；`--policy` 可單跑一個候選、預設跑全部）；README 補一節。
⛔ `Assets/Scripts/**`、任何資產、git、Unity Editor 操作、PlayMode 執行（Claude 跑）。
