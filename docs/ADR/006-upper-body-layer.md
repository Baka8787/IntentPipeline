# ADR-006：上半身動畫層（移動中施法）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🟡 **Trial**（2026-09-11 使用者裁決）——combined vertical slice 已實作，待 Play 驗收 |
| 日期 | 2026-09-06；2026-09-11 依現況診斷收斂並翻 Trial |
| 提出者 | 自主批次；2026-09-11 使用者裁決採用 |
| Trial 例外 | 使用者明確允許 ADR-006 與 ADR-007 在本次 **Animation Layering × Direction Authority combined vertical slice** 暫時同為 Trial。這是一次性驗證安排，**不構成**對 ADR-004「通常同時一個 Trial」的通用放寬。 |
| 關聯文件 | `docs/06-animation-presentation.md`（Facade 現況）、`docs/13-combat-locomotion.md`、`docs/14-direction-authority.md`、ADR-007 |

---

## 1. 實際需求與現況缺口（2026-09-11）

施法期間，角色的 gameplay movement 已經存在：`LocomotionModel` 每幀輸出世界空間
`MoveDirection`，並把它投影到 committed facing local space 後發布 `MoveX / MoveZ`。
Direction Authority 也已獨立決定角色面向。因此**方向解算不是本 ADR 的缺口**。

真正缺口只在 animation presentation：

- X Bot 一般 Idle／Move 的 Layer 0 目前仍使用 1D `Locomotion.asset`。
- `AnimancerFacade.Play(key)` 目前一律把 transition 播到 Layer 0；Spell 因而取代整個 locomotion pose。
- 專案已有完整 `Locomotion_2D_Proto_FullRing`，並已接同一組 `MoveX / MoveZ`；中心＋cardinal＋diagonal
  九個 thresholds 亦有自動測保護。素材齊全，**不是 blocker**。
- 所以 spell 若只改播 Layer 1，Layer 0 仍可能停在一般 1D locomotion；要滿足施法 8-way，spell 的 authored
  mapping 還必須明確指定「施法期間 Layer 0 使用哪一份 locomotion transition」。

預期結果是：**既有完整 8-way locomotion 留在 Layer 0，Spell 只在 Layer 1 透過 AvatarMask 覆蓋上半身。**

### 1.1 `UpperBodyWeight` 現況

`PlayerRuntimeData.UpperBodyWeight` 原本由 `LocomotionModel` 依速度寫成 0 或 0.5，但沒有 runtime 消費者。
它也不能直接驅動 spell layer：原地施法時值為 0，反而會把 spell 隱藏；移動時 0.5 也沒有素材或設計依據。
Trial 實作已刪除這個 dead field 與 writer／Editor 顯示／WriterRule，而不是把錯誤語意硬接成 layer weight。

---

## 2. 候選方案

| 方案 | 做法 | 結論 |
|---|---|---|
| A. 維持全身 Spell | Layer 0 繼續被 Spell 取代 | 角色位移但腿不走，問題保留 |
| B. Action 期間鎖移動 | 把 movement intent 或 output 歸零 | 否定「移動中施法」，且會新增不必要 gameplay policy |
| **C. Layer 0 8-way ＋ Layer 1 Spell／AvatarMask** | Facade 依 authored mapping 同時選兩層 presentation | **採用草案**；沿用既有 movement／facing 資料流 |
| D. Additive Spell | 把素材改做 additive | 現有素材不是為 additive authoring；不採用 |

---

## 3. Decision（Trial）

### D1 — 分層只屬於 `AnimancerFacade`；State／ActionDefinition 不認識 Layer

`ActionState` 繼續只呼叫 `Play(AnimationKey)`。它不知道 Layer、AvatarMask 或 locomotion mixer。
同一 Action 在不同 actor 上能否分層，完全由該 actor prefab 的 presentation mapping 決定。

### D2 — Spell mapping 是 authored data，最小欄位為 Layer＋Mask＋Layer 0 companion

擴充 `AnimancerFacade.TransitionMapping` 的 serialized presentation data：

- `LayerIndex`：預設 0，既有映射零回歸。
- `AvatarMask`：Layer 1 spell 顯式指定上半身遮罩。
- `BaseLayerTransition`（名稱可於實作期微調）：只在 layered mapping 使用；spell mapping 指向既有完整
  FullRing，讓 Layer 0 在 spell 期間以同一組 `MoveX / MoveZ` 播 8-way。

不建立 `AnimationLayerProfileSO`、selector 或第二套 mapping framework。四個 spell keys 只是四列 authored data。

### D3 — Layer lifecycle 由 Facade 內部收斂

- 播 layered spell：Layer 0 播 authored `BaseLayerTransition`；Layer 1 套 mask 並播 spell transition。
- 下一個一般 mapping（Idle／Move／Melee／Roll／Jump／Reaction）：照舊播 Layer 0，並淡出／停止 Layer 1。
- `Play` 與 `PlayWithCallback` 共用同一條內部分派，避免兩套 layer 規則。
- 既有 `AnimationFacadeBase.SetLayerWeight` 已存在；本方案不需要讓 FSM 增加 layer API。

### D4 — 方向資料流完全沿用 ADR-007

Layer 0 Walk／Run directional mixer 只讀既有 `MoveX / MoveZ`。不新增方向欄位、不讀 CameraTransform、不修改
Direction Authority，也不在 animation layer 內重新推導 facing 或 movement direction。

### D5 — 刪除 `UpperBodyWeight`，不把它改造成第二個 layer 權威

Layer 是否啟用由「目前播放的 authored mapping」決定，權重過渡由 Facade／Transition presentation data 決定。
`UpperBodyWeight` 現有的速度語意與需求衝突，因此在本 ADR 實作 slice 一併移除。

---

## 4. 與 ADR-007 的關係

ADR-007 決定的是 **MovementDirection／FacingDirection／AimDirection 的 authority 與載體**；本 ADR 只決定
這些既有輸出如何被兩個 animation layers 呈現。兩者沒有 ownership 衝突：

```text
ADR-007 outputs
  MoveX / MoveZ + committed facing
            ↓
ADR-006 presentation
  Layer 0 Walk/Run 8-way + Layer 1 masked Spell
```

本 ADR 不修訂 ADR-007 D1–D5。2026-09-11 使用者批准兩者在本 combined slice 暫時同為 Trial，目的只在
實測 layering 與 direction commitment 的交互作用。8-way 素材完整與否不是 blocker；實作沿用既有
FullRing，沒有重新設計 mixer。

---

## 5. 明確不做

- 不修改 Direction Authority、Combat Context 或 Action lifecycle。
- 不新增 MovementLock／AttackMovementPolicy。
- 不重新製作或重新設計既有 FullRing；只有檢查發現接線錯誤時才修資產接線。
- 不修改玩家一般狀態仍使用 1D locomotion 的 policy。
- 不把 Melee／Roll／Jump／Reaction 改成上半身動作。
- 不修改 Enemy AI、敵人 locomotion 或 Punch Bake。

---

## 6. Acceptance Criteria（`Trial → Accepted` 草案）

> 📌 **2026-09-12 fold-back**：A／B／D 的 Play 證據**早已存在於 `WORKLOG.md` 的 combined vertical slice 條目**，
> 只是當時忘記回填本節的勾選框。以下勾選只是把**既有證據**登記進來，**不改本 ADR 的 Status**——
> 仍有 E／H 未驗，故**維持 🟡 Trial**（依 CLAUDE.md：Accepted 需要全部通過）。

- [x] **A. Layer 0 8-way**：施法期間持續播放 Walk／Run 雙環 directional mixer，Forward／Backward／Left／Right／45／135
  由既有 `MoveX / MoveZ` 選擇，面向由 Direction Authority 維持。
  ✅ **證據**（`WORKLOG.md` 2026-09-11 combined slice）：「Spell 時 Layer 0 保持 8-way；Forward／Backward／Left／Right／
  Forward-right 能依 `MoveX / MoveZ` 選到對應 child」；接線抽查 Layer 0 ＝ 新 mixer（17 children）。
- [x] **B. Layer 1 Spell**：Fireball 三段與 Ice 只覆蓋 AvatarMask 允許的上半身，原地與移動中都可見。
  ✅ **證據**（同上）：「Layer 1 播 Fireball／Ice 且使用 upper-body mask」；「Layer 1 Ice 同時保持播放，Console 無 error」。
- [x] **C. authored mapping**：增加另一個 layered action 只需新增／修改 mapping，不改 State 或 Action runtime。
- [x] **D. layer cleanup**：Spell 結束、被打斷或切到全身 Action 時，Layer 1 可靠淡出，無殘留 pose。
  ✅ **證據**（同上）：「Action 結束 overlay 歸零」。⚠️ **僅涵蓋「正常結束」**；「被打斷」與「切到全身 Action」
  兩條路徑的 Play 證據**尚未記錄**，若要嚴格驗收需補這兩個情境。
- [ ] **E. 既有行為零回歸**：一般 Player locomotion 仍為 1D；Melee／Roll／Jump／Reaction 仍是全身。
  ❌ **未驗**：`WORKLOG.md` 只記了 Spell 路徑的 Play 結果，沒有記 Melee／Roll／Jump／Reaction 的回歸確認。
- [x] **F. 單一方向來源**：沒有新方向欄位、CameraTransform 解算或 runtime 1D／2D angle switch。
- [x] **G. `UpperBodyWeight` 已移除**，黑板 ownership 表與 WriterRule 同步。
- [ ] **H. 零 GC**：mapping 與 layer state 在初始化期預熱，播放熱路徑不建立配置。
  ❌ **未驗**：預熱**已實作**（§7），但零 GC 是**量測結論**，需 `docs/02` §7.4 SOP（Player build ＋ Profiler），
  目前沒有任何量測紀錄。⚠️ 「已預熱」≠「已量到 0 B/frame」。

**⇒ 2026-09-12 狀態結論：5／8 通過（A・B・C・F・G），D 部分，E・H 未驗 ⇒ 維持 🟡 Trial。**
剩餘工作是**兩次 Play ＋ 一次 Profiler**，不是程式。

---

## 7. Trial 實作狀態（2026-09-11）

- `TransitionMapping` 已加入 `LayerIndex`／`AvatarMask`／`BaseLayerTransition`；四個玩家 Spell key author 到
  Layer 1，Layer 0 companion 指向 `Locomotion_2D_Spell_WalkRun`。
- Spell locomotion 由 Walk radius `0.35` 與 Run radius `0.75` 兩個完整八方向環構成；playback 由各 child
  MotionBakeData 校正到所屬 gait anchor。舊 FullRing「全部追到 Sprint 最大速度」不再位於玩家 Spell 路徑。
- Sprint gameplay gait 與 6.2614 m/s 最大速度來源保留；Sprint directional presentation 未提前加入。
- `AnimancerFacade` 初始化時預熱兩層 state；播放 layered mapping 時同時維持 Layer 0 locomotion、淡入
  masked overlay，下一個 base mapping 會淡出 overlay。
- `UpperBodyWeight` dead field 已移除；Direction Authority、MoveX／MoveZ 與 ActionState layer 邊界未改。
- Unity EditMode 全套 330 passed／0 failed／1 skipped；新 Walk／Run 腳步速度仍待 Play 驗收。

---

## 8. 修訂紀錄

| 日期 | 修改 | 原因 |
|---|---|---|
| 2026-09-06 | 建立草案（Proposed） | 首次確認移動中施法需要 upper-body layer |
| 2026-09-11 | 收斂實際需求、最小 authored mapping 與 ADR-007 關係；素材完整不再列 blocker | 唯讀檢查確認方向資料流與 FullRing 已存在，缺口只在 Layer 0 companion＋Layer 1 mask/lifecycle |
| 2026-09-11 | **Proposed → Trial**；實作 authored layering，與 ADR-007 暫時同為 Trial | 使用者明確批准 combined vertical slice 例外；僅用來驗證兩份 ADR 的交互作用，不放寬一般治理規則 |
| 2026-09-11 | Spell Layer 0 companion 由 Sprint-calibrated FullRing 改為 Walk／Run 雙環 | 現行操作最高為 Run；保持 Sprint gameplay 定義，但不再讓 Walk／Run directional clips 全部追到 Sprint 速度 |
