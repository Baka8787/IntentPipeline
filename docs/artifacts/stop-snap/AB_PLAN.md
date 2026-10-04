# R1c runtime A/B — 實作與量測計畫（2026-10-05）

> 語意與 consumer 審查的正本在 `docs/45` §24.1–§24.2。本檔是 Codex 規格與指標定義。

## 1. Runtime（實驗開關；關閉 ⇒ 逐位元現行）

**`FootIKSettings`**（新 Header「🧪 docs/45 §24 R1c」，放在 T3 原型參數附近）
- `public bool LockedFollowExperiment = false;`
- `[Min(0f)] public float LockedFollowDeadZone = 0.03f;`
- Tooltip 必須寫：Locked 仍是 Locked、只改輸出水平位置；UnlockRadius 仍以原始錨點量；垂直／支撐語意不變；**與 `UseStandingPosturePrototype` 路徑同時開啟不受支援**（該路徑鎖存世界座標）。

**`FootContactSolver`**（純函數；所有權不變）
- 新 `public static Vector2 ClampLockOffset(Vector2 offset, float followRadius)`：`followRadius ≤ 0` ⇒ 原樣回傳；否則長度 > r 時縮到 r。
- `ResolveHorizontal(in state, animationPosition, float followRadius = 0f)`：Locked 分支 ⇒ `anim.xz + ClampLockOffset(LockPosition.xz − anim.xz, r)`（r ≤ 0 時等於現行的 `LockPosition.xz`——用分支保證逐位元，不要靠浮點相等）。
- `StepLock(..., float followRadius = 0f)`：Locked → Releasing 時 `ReleaseOffset = ClampLockOffset((−dx, −dz), r)`（r ≤ 0 ⇒ 現行）。釋放判準（β、UnlockRadius 以 `LockPosition` 量、接觸）**完全不動**；`LockPosition` 不寫。

**`FootIKController.ResolveContactPosition`**：`float follow = settings.LockedFollowExperiment ? Mathf.Max(0f, settings.LockedFollowDeadZone) : 0f;` 傳給 `StepLock` 與 `ResolveHorizontal`。其他地方不改。

**EditMode 測試**（`FootContactSolverTests.cs` 追加）
1. r＝0：Locked 輸出＝錨點（既有測試已涵蓋，加一個明確的 r＝0 對照即可）。
2. r＝0.03：動畫腳離錨點 0.02 ⇒ 輸出＝錨點；0.10 ⇒ 輸出在動畫後方 0.03、方向沿（錨點 − 動畫）。
3. 轉移連續：釋放當帧的目標 ＝ 前一刻 Locked 的 effective（同一個動畫輸入）；`ReleaseOffset` 長度 ≤ r。
4. UnlockRadius 以錨點量：r＝0.03、動畫腳離錨點 0.25 ⇒ 進 Releasing（即使 effective 只差 0.03）；0.15 ⇒ 仍 Locked。
5. 相位序列在 r＝0／0.03／0.05 下對同一輸入序列完全相同（lock timing 不變）。

## 2. Probe（`PlantSettleProbe.cs`）
- `Probe_LockFollowFlat([Values(Gait.Walk, Gait.Run)] gait, [Values(0..5)] offset, [Values("cur","f3","f5")] arm)`
- `Probe_LockFollowStairs([Values(Dir.Down, Dir.Up)] dir, [Values(2,6,10)] stopIndex, [Values(Gait.Walk, Gait.Run)] gait, [Values("cur","f3","f5")] arm)`（proto 樓梯、yaw 0）
- arm ⇒ 場景實例 `settings.LockedFollowExperiment`／`LockedFollowDeadZone`（cur＝false；f3＝true/0.03；f5＝true/0.05），不存檔。輸出前綴 `lf_<arm>_`；snap 系列的全程記錄（frame ≥ 90）同樣套用。
- Recorder 列尾追加：每腳 `effX,effZ`（＝ 本帧 T3 輸出水平位置，即 `tgtX/Z`，可省略若與 tgt 相同——請直接確認 `tgt` 就是 effective 並在 README 註明，不要重複欄位）、`hipsY`（最終 Hips 世界 Y）。不改既有欄位。

## 3. 分析（`ab.py.txt`，Python 標準函式庫）
事件錨點：`cur` 臂每腳每次 lock episode（與 `replay.py.txt` 同一套 metadata：`case, gait, terrain, side, context, ordinal, tRel, isLastRelease`）；其他臂在同一時間窗 [L−6, R＋12] 內找自己的 episode。軌跡：`A = pose + (root − rootEval)`、`C = bone`（goal 空間，V3 已證）。

| 指標 | 定義 |
|---|---|
| gapPreMax | 臂自己的 Locked 帧中 max \|C − A\|（水平，cm） |
| gapAtRelease | 臂的釋放帧 \|C − A\| |
| relPathRatio／relPeakRatio | 釋放後 10 帧 C／A 路徑比與每帧位移 peak 比 |
| peakSpeedC | 窗內 C 每帧水平位移 max（cm/f 與 m/s） |
| lockedAwayFrames | 臂 Locked 帧中 \|A − 錨點(lockX/Z)\| 比前一帧增加 > 0.1 cm 的帧數 |
| stanceSlide | `cur` 判為 Locked 的帧裡，C 的水平路徑（cm）與每帧 max |
| treadChangesLocked | 臂 Locked 帧中該腳 `liveSup` 變化 > 5 cm 的次數 |
| finalSupport | 停步結束（restart 前最後一列）該腳的 `lockSupY`（Locked 時）與 `liveSup` |
| footDYmax | 窗內 boneY 每帧 \|Δ\| max（cm） |
| pelvisTargetDY／pelvisOutDY／hipsDY | 窗內 `pelvisSolver`／`pelvisOut`／`hipsY` 每帧 \|Δ\| max（cm） |
| lockCount／releaseCount | 每 case × 腳：全段與停步段（tRel ≥ 0）分開 |

輸出：
- `ab_events.csv`（arm × 事件）
- `ab_summary.md`：
  1. **Known bad／good 逐案例表**（三臂並排）：Run LU re-step（`RunFwdStop_LU` L、isLastRelease）、Walk LU 最後一步（`WalkFwdStop_LU` R、isLastRelease）、Walk RU hidden step（`WalkFwdStop_RU` R 最後一個 episode：停步結束 \|C − A\|、tRel ≥ 40 的 C／A 路徑比）、Run RU（`RunFwdStop_RU` 全部事件）——各分平地／上樓／下樓。
  2. loop 對照（分 gait × terrain）。
  3. 樓梯垂直：footDYmax、pelvisTargetDY、pelvisOutDY、hipsDY、treadChangesLocked、finalSupport 三臂比較。
  4. lock／release 次數三臂比較。
  5. 每臂與 `cur` 的差值欄（f3 − cur、f5 − cur）。
- 與 §23 offline replay 對照：f3／f5 的 gapAtRelease 與 relPathRatio 應接近 replay 的 R1c d0.03／0.05（差異 > 0.1 倍或 > 1 cm 要列出）。

## 4. 範圍
✅ `FootIKSettings.cs`、`FootContactSolver.cs`、`FootIKController.cs`（只有 §1 的那一行傳參）、`FootContactSolverTests.cs`、`PlantSettleProbe.cs`、`docs/artifacts/stop-snap/ab.py.txt`、README 一節。
⛔ 其他 runtime 檔、任何資產／prefab（X Bot 不開）、R2／speed cap／其他 release policy、P8b、locked 垂直語意、git、Unity Editor 操作／PlayMode（Claude 執行）。
