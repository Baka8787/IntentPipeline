# ADR-011：Run 起跑入場相位對齊（`AnimationFacadeBase.TryAlignEntryPhase`）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🟡 **Trial**（2026-10-04 使用者裁決：開窄範圍 Trial ADR 後實作）。AC 見 §5——**使用者已裁決 ≠ 工程上已驗證**。 |
| 日期 | 2026-10-04 |
| 範圍 | **只有**：Run、從靜止（Idle／停步）**同方向**起跑時，locomotion 循環的入場相位；目前測過的 locomotion 資產（`Locomotion.asset` 1D mixer） |
| 不含 | Walk、轉向起跑、8-way／2D mixer、AI、移動中加速、任何通用 gait-phase／gait synchronization 框架 |
| 證據 | `docs/45` §19.3（診斷）、§20.3（測試內 phase 掃描）、§21（本 ADR 的 runtime 驗收） |
| 關聯 | ADR-003（Movement Intent Layering：`LocomotionModel` 是 locomotion 動態與動畫參數的擁有者）、`docs/07`（`TryGetDominantChildNormalizedTime` 先例：通用、以 stateKey 為鍵的動畫圖查詢） |

---

## 1. Problem
起跑時 Idle 與 Move 共用同一個 locomotion mixer；Walk／Run 子狀態 `_SynchronizeChildren` 全關，各自的時鐘一直在跑，
所以「開始被看見」時的相位是任意的（LU 停步後 0.33、RU 停步後 0.02）。相位不對 ⇒ crossfade 讓貼地的腳水平滑動：
平地動畫腳 20–34 cm、畫面腳兩腳合計 25–92 cm（`docs/45` §20.3）。縮短 crossfade 只是被 T3 的鎖暫時按住（動畫腳反而滑更多）。

## 2. Decision

### D1 — 決策擁有者是 `LocomotionModel`
觸發條件（純函數 `LocomotionModel.ShouldAlignRunEntryPhase`）：意圖上升緣 ∧ 起跑前靜止（smoother 速度 < `MoveThreshold`）∧ Gait＝Run ∧ 起跑方向與目前朝向夾角 ≤ `entryPhaseMaxHeadingDegrees`。
時機：停步被意圖打斷（已 Play 回 locomotion）之後、本帧動畫參數寫入之前——此帧 Walk／Run 子狀態權重仍為 0。
開關 `LocomotionModel.alignRunEntryPhase`（程式預設關 ＝ 逐位元現行）。

### D2 — 相位只由 Bake 推得
入場相位 ＝ `runLoopBakeData.FootPhaseCurve` 的**左腳觸地極值**（曲線最小值）的 normalized time
（`MotionBakeData.TryGetFootPhaseExtremeNormalizedTime(FootPhase.LeftFootDown, …)`，第一次需要時算一次並快取）。
⛔ 不得以手填常數表達（量到的甜蜜區 0.88–0.92 只是證據，不是設定值）。換 loop 資產 ⇒ 相位跟著 Bake 變。

### D3 — Core↔Animation contract：`AnimationFacadeBase.TryAlignEntryPhase(string stateKey, float normalizedTime)`
- **意圖**：「這個狀態下一個要被看見的循環從 `normalizedTime` 開始」。呼叫端只給 stateKey 與相位，不認識混合樹、子狀態、權重。
- **後端義務**：⛔ 不得讓目前畫面上看得到的動畫跳動。Animancer 後端只重設**目前權重為 0** 的直接子狀態播放頭（它們此刻不在畫面上）；正在顯示的子狀態（Idle）不動。沒有子狀態概念的後端維持預設 `false`（no-op）。
- **為什麼沒有洩漏 Animancer／mixer 細節**：Core 端的參數是 stateKey（已存在的 facade 慣例）與 Bake 推得的相位；「哪些子狀態可以安全重設」完全在後端判斷。
  名稱刻意不含 Locomotion／Walk／Stop ——**架構不變量 A14（Facade 對 locomotion 無知）維持不變**，不需要取代它；與 `TryGetDominantChildNormalizedTime` 同一層級（以 stateKey 為鍵的通用動畫圖操作），差別是寫入。

## 3. Alternatives（否決）
| 方案 | 否決理由 |
|---|---|
| 縮短 crossfade | 量測：動畫腳滑動不減反增（LU 後 19.6 → 52.7 cm），畫面腳變小只因 T3 鎖住，之後補位 |
| 手填入場相位常數 | 違反「Bake Data 是動畫實際運動值的唯一真相」；換資產就失效 |
| 依支撐腳選相位（左／右著地極值） | 量測：兩種前置狀態都是左腳著地極值最好、右腳極值（φ≈0.375）兩者都差 ⇒ 目前證據不需要 |
| 通用 gait-phase 框架／子狀態同步 | 超出證據範圍；使用者明確排除 |
| Facade 介面名 `TryAlignLocomotionEntryPhase` | 違反不變量 A14（Facade 不得出現 Locomotion）；意圖可用通用名稱表達 |

## 4. Impact
- `AnimationFacadeBase` 新增一個 virtual（預設 no-op）；`AnimancerFacade` 實作；`MotionBakeData` 新增純讀取 helper；`LocomotionModel` 新增開關、觸發判斷、相位快取。
- 黑板、管線順序、FSM、T3 皆不變。

## 5. Acceptance Criteria（Trial → Accepted）
| # | 條件 | 狀態 |
|---|---|---|
| AC1 | 開關關閉 ⇒ 逐位元現行；EditMode：極值 helper、觸發條件、`LocomotionModel` 接線（Run 起跑會請求、Walk／關閉不請求）；A14 仍通過 | ✅ 2026-10-04 EditMode 815／814／0 failed |
| AC2 | 平地（`PlantSettleProbe.Probe_EntryPhaseFlatAB`）：LU 後與 RU 後，起跑離地前**動畫腳**貼地位移都降到 ≲ 8 cm（不是只看畫面腳） | ⚠️ **部分**：20.3／33.6 → 9.8／9.4 cm（大幅下降但未達 8 cm；殘留是極值相位下的左腳支撐腳，root 從 0 加速所致）——`docs/45` §21.3 |
| AC3 | 樓梯（`Probe_EntryPhaseStairsAB`，兩道樓梯 × 上下 × 7 位置）：動畫腳與畫面腳都不比關閉時差；分 LU／RU 前置列出 | ⚠️ **大致**：動畫腳每組都下降；畫面腳只有「樓梯下、RU 後」中位 2.4 → 4.5（max 7.2）略增——§21.3 |
| AC4 | 人工 Play（X Bot）：Run 上下樓與平地停步後同方向起跑，拖步消失或明顯減輕；Idle 沒有起跑瞬間的可見跳動 | ✅（部分）2026-10-04 使用者 Play：「拖步有變好」；Idle 跳動未特別回報 |

## 6. 失敗處置（revert 路徑）
`alignRunEntryPhase` 設 false（逐位元回到現行）；若 Trial 失敗：移除 `TryAlignEntryPhase`（base＋Animancer）、`TryGetFootPhaseExtremeNormalizedTime`、`LocomotionModel` 的開關與觸發程式、`RunEntryPhaseTests`，ADR 標 Rejected。

## 7. 不凍結的內容（Trial 期間可依實作修訂，不必開新 ADR）
觸發門檻（靜止速度、方向夾角 30°）、極值掃描點數、後端判定「可安全重設」的條件（目前：權重 0）、對齊時機在 tick 內的確切位置。

## 8. 判準檢查（為什麼需要 ADR）
CLAUDE.md Trial 判準 ③：核心驅動介面（`AnimationFacadeBase`）新增寫入路徑——Core 主動要求 Animation 改變子狀態播放相位，是新的 Core↔Animation contract。

## 9. 裁決紀錄
- 2026-10-04（使用者）：採 start-phase alignment；不建 gait-phase 框架；不得硬寫 0.88–0.92；只限 Run／靜止→Run／同方向；開窄範圍 Trial ADR；Core-facing 名稱用意圖型（facade 內部處理權重 0 子狀態）。
