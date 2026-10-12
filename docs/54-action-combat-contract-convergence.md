# 54 — Action／Combat 契約收斂：設計與影響範圍（2026-10-12）

| | |
|---|---|
| **狀態** | 🟠 **Proposed**——設計與影響分析。⛔ 不得作為實作基線；runtime／prefab／ScriptableObject／測試皆未改 |
| **輸入** | `docs/51`（審查）＋ 使用者 2026-10-12 **暫定裁決** Q1–Q6 |
| **路由** | 全部落在既有 Trial ADR（ADR-007／ADR-009）可修訂範圍或 Living Spec；**不需要新 ADR**——唯一例外是 §5.5 的 Q5-b 選項 C（新增黑板欄位），若選它要先開 ADR-007 修訂討論 |
| **閱讀方式** | §1 衝突表 → §2 共享 vs 差異 → §3 逐項變更 → §4 F-01／F-07 專審 → §5 文件 vs runtime 分離 → §6 最小方案與 AC → §7 待裁決 |

> **對 `docs/51` 的一處更正（本輪調查發現）**：`docs/51` F-02 寫「`EnemyPunch` 已有 Bake ⇒ 前衝位移也跟著轉」。
> 實際讀 `Bake_Fists_Punch_R.asset`：`SpeedCurve` 49 個 key **全部為 0**（0–0.8 s），**總位移 0 m**。
> ⇒ 敵人出拳時**沒有前衝**，只有**轉身追蹤**；而因為有 Bake，`ActionState` 走 `ExecuteBakedCurveMovement`，速度恆 0 ⇒ 出拳期間**原地不動**（`docs/19` §3.2 的「無 Bake（可自由移動）」兩個前提都不成立）。
> 追蹤的實際後果是 **hitbox 跟著轉**：Y Bot 的 `MeleeHitbox` 是 root 的子物件，盒 0.9×1.2×1.2、中心在身前 0.9 m，命中窗 0.32→0.8 s 期間 root 仍以 `aimFacingTurnSpeed 8` 追玩家 ⇒ 盒子跟著掃。已同步在 `docs/51` 加更正框。

---

## 1. 暫定裁決 × 既有契約：衝突檢查

| 裁決 | 碰到的既有契約 | 判定 | 處置 |
|---|---|---|---|
| **Q1** facing 段落開始承諾；effect 前搖可更新、release 鎖定 | ADR-007 **D5**「facing 與 release 讀**同一份**」「⛔ facing 讀承諾、release 讀當下」 | **字面衝突**（runtime 自 2026-09-15 已是 Q1 的形狀） | ADR-007 是 🟡 Trial ⇒ D5 加修訂框＋§11 記錄，**不開新 ADR**。D5 自己寫「不凍結：作用域粒度」——但這次改的是「同一份」本身，屬實質修訂，需使用者確認（§7-Q1a） |
| 〃 | ADR-007 **AC B**「每一段身體朝向與火球方向一致（不允許同段內分家）」✅（2026-09-12 依「共用同一 commitment」打勾） | **證據失效**：Q1 明文允許同段分家（目標移動、相機轉向） | AC B 要改寫並**重新驗收**；舊 ✅ 不能沿用 |
| 〃 | ADR-007 D4（sink 不解方向，方向由 lifecycle 交付） | ✅ 相容 | `_effectContext` 仍由 `ActionState` 解、經 `Release(in)` 交付 |
| 〃 | `AimResolver` 責任（每帧無狀態的相機射線點） | ✅ 相容 | 它不知道承諾；`ActionState` 在邊界與每個 tick 各 pull 一次（帧快取，`docs/41` 已審） |
| 〃 | `docs/15` §4.4「soft target 只在 commitment boundary 消費」 | 字面衝突 | Living Spec 改寫 |
| **Q2** 敵人出拳：承諾點前追蹤、後鎖定 | ADR-007 **AC A2**「敵人持續面向 target（側移／後退／繞行／**出手皆然**）」 | **字面衝突** | AC A2 改為「出手時：承諾點前追蹤、之後鎖定」（Trial，可改） |
| 〃 | ADR-007 D5「承諾在段落邊界取得」＋「不凍結：作用域粒度」 | ✅ 相容（**時點**屬明文不凍結的粒度） | 承諾時點變成 authored 參數（§3.2） |
| 〃 | ADR-007 D3 2026-09-08 附屬禁令「⛔ 預設值不得產生與其他 Action 不一致的朝向」「⛔ 不得擴充成一組可自由組合的朝向旋鈕」 | ⚠️ 邊界：新增一個 policy 成員＋一個時點欄位 | 預設值保留現行行為（時點 0、`CameraForward`）⇒ 第一條不觸犯；第二條以「各一個、各有唯一使用者」為界（§7-Q2b） |
| 〃 | ADR-007 D1「⛔ 不得以 `transform.forward` 表示瞄準方向」 | ⚠️ 只要不選「鎖定當下 forward」就相容 | 承諾方向取**目標方向**而非 forward（§3.2） |
| 〃 | `docs/15` §16-8（敵人沒有 commitment origin，開放項） | 本項即其解 | 結案 |
| **Q3** AI 不持續選擇死亡目標；四種 eligibility 分開 | `docs/19` §3.1「否決『死亡時停止產生 `CombatContext`』」 | ✅ 不衝突——那條管**自己死**的自我抑制；Q3 管**目標死**的 target validity（`artifacts/survivability.html` 已區分） | 兩條並存，§H 不合併 |
| 〃 | A27（`AIInputSource` 不得讀 `PlayerRuntimeData`／FSM） | ✅ 相容 | 讀**目標的** `CharacterHealth` 元件（與玩家選目標同一條通道），不讀黑板 |
| 〃 | ADR-009 D2（後果只由 `CharacterHealth` 決定） | ✅ 相容 | Damage eligibility 不動；只調整 sink 的副作用 |
| 〃 | `docs/11`（投射物「屍體不擋子彈」） | ✅ 保留 | 屬 Hit Detection policy，不動 |
| **Q4** 敵人受有效攻擊即交戰（即使在 aggro 外） | ADR-007 D3 補充條款（唯一 producer、⛔ 第二份 isInCombat） | ✅ 相容 | 寫入者仍是 `AIMovementSource`；A5 白名單不變 |
| 〃 | `docs/21` §1.1「`InCombat` 由黏性半徑決定」 | Living Spec 擴充 | 加一條進入條件與保留時間 |
| 〃 | W15（`attackRange ≤ aggroEnter` 等） | ✅ 不受影響 | 距離分層不變 |
| **Q5** 區分 Combat Action 與 InCombat | `docs/15` §4.1「玩家發動攻擊／施法 ⇒ 進入」（最強訊號，零新機制） | **直接推翻一條 Living Spec 規則** | Living Spec 改寫；`CombatContextTests.TC1` 被取代 |
| 〃 | `docs/11` §13：combat directional speed cap **以 `InCombat` 為閘門**，保護 Spell 8-way 的 playback ≤ 1.35× | 🔴 **隱性相依** | 見 §3.5：Q5 會讓「沒有交戰時施法側移」回到 2.07–2.17× playback。**Q5 不能單獨落地，要先決定 cap 的閘門**（§7-Q5b） |
| **Q6** 受傷聲跟實際傷害事件，與 Hurt 動畫分離 | `AudioController.cs:122`（已讀 `JustTookDamage`）；`SurvivabilityData` 註解「只有**表現**被丟棄」 | ✅ runtime 已是 Q6；註解措辭與 Q6 衝突 | 只改註解／`docs/37`，加一條結構測試 |

**結論**：沒有任何一條需要推翻 Accepted ADR（004／005）。需要動的治理文字全在 Trial ADR-007 的 D5、AC A2、AC B，以及 Living Spec `docs/15`／`19`／`21`／`11`。唯一真正的**設計阻塞**是 Q5 對 speed cap 的隱性相依。

---

## 2. 應共享的跨模組契約 vs 應保留的 Player／AI 差異

### 2.1 共享（同一條規則、同一個 owner，兩條路徑都要遵守）

| 契約 | Owner | 為什麼必須共享 |
|---|---|---|
| **方向承諾的語意**：facing 承諾在「承諾點」取一次；effect 在 release 定案；sink 只消費交付值 | `ActionState` | 這是 ADR-007 D3／D4／D5 的核心；玩家與敵人用同一顆 `ActionState`，語意不得分家。差異只能在**資料**（policy、承諾時點），不得在程式分支（A19 精神） |
| **目標存活判準**：「沒有 `CharacterHealth` ＝ 視為活著；有則看已 commit 的 `IsDead`」 | Survivability（`CharacterHealth`） | 「缺席 ≠ 死亡」是最容易漂移的一句（`docs/15` §4.2）；今天已有 2 份手寫、Q3 會變成 5 份 |
| **後果判定**（Damage eligibility） | `CharacterHealth.ApplyDamage` | ADR-009 D2，不變 |
| **受擊是交戰訊號**（被打的一方進入交戰） | 各自的 combat context producer | Q4 讓它對兩邊都成立；判準同義，**實作各在自己的 producer**（ADR-007 D3 每角色唯一 producer） |
| **死亡自我抑制**（我死了所以收手） | 各 consumer | `docs/19` §3.1 已裁決，不變 |

### 2.2 保留差異（不得為了少一個概念而合併）

| 差異 | 玩家 | 敵人 | 為什麼保留 |
|---|---|---|---|
| 承諾的方向來源 | 相機射線／soft target（`CameraForward`／`CameraConeSoftTarget`） | combat target（Q2 新 policy） | 玩家「瞄哪打哪」，敵人「打它的目標」——不同輸入、同一個承諾機制 |
| 承諾時點 | 段落開始（Q1） | 承諾點（Q2，≈ release） | 抗抽搐 vs 前搖瞄準；屬 authored 粒度 |
| 非出手時的 facing | 跟移動方向（`usePersistentCombatFacing 0`） | 持續面向目標（`1`） | ADR-007 2026-09-10 裁決，W14 守 |
| 目標選擇 | 黏性 marker 掃描＋相機錐＋soft candidate | 序列化 Transform | 敵人只有一個已知目標；感知系統不在範圍 |
| 交戰半徑與離開規則 | 6／9 m ＋ 5 s AND | 8／12 m | tuning 不同；`docs/20` E1 明說是第一版值 |
| Combat Action 是否進入交戰 | Q5 決定 | 不適用（AI 的出手由距離觸發，必然已交戰：W15） | — |

---

## 3. 逐項變更：模組／資料來源／寫入者／讀取者／測試缺口／回歸風險

### 3.1 Q1 — per-segment facing、per-release effect（**runtime 零改動**）

| 項目 | 內容 |
|---|---|
| 負責模組 | `ActionState`（`CaptureReleaseContext`／`RefreshEffectContext`／`TryEmitRelease`） |
| 資料來源 | `IAimSource.TryGetAimPoint`（相機射線，帧快取）＋ `CombatContext.SoftTargetPosition`（每帧無記憶候選） |
| 寫入者 | `ActionState` 私有欄位 `_releaseContext`／`_effectContext`；不上黑板 |
| 讀取者 | facing：`FullBodyStateMachine.TryGetActiveFacingCommitment` → `CharacterFacingSource`（4.6）；effect：所有 `IActionLifecycleSink.Release(in)` |
| 現況 vs Q1 | **已一致**：`OnTick` 先 `RefreshEffectContext` 再 `TryEmitRelease`（`:194-196`），release 交付當帧值；sink 取得的是 `in` 複本，投射物方向在生成時固定、AoE 當帧結算 ⇒ 「release 鎖定」由交付語意保證 |
| 測試缺口 | ① `TD5B` 的 release 在 t=0，**沒有測到前搖中目標變化**（見 §4.1）；② 沒有測試釘住「前搖中相機轉出角錐 ⇒ effect 改用相機點、facing 不動」這個**被 Q1 接受的分家**；③ 沒有測試釘住「release 之後 `_effectContext` 繼續刷新不會造成第二次 release」 |
| 回歸風險 | 無（不改 runtime）。風險在文件：AC B 需重驗 |

### 3.2 Q2 — 敵人出拳：承諾點前追蹤、之後鎖定

**承諾時機的調查**（`EnemyPunchDefinition`：Start 0.8 s〔Bake 時長〕、`ReleaseNormalizedTime 0.4` ⇒ release 於 **0.32 s**；hitbox 窗 0.32→0.8 s）：

| 候選時點 | 前搖可追蹤 | 命中窗內追蹤 | 評估 |
|---|---|---|---|
| 0（＝玩家規則） | 無 | 無 | 前搖完全不瞄準；玩家站開一步即落空——與 Q2「承諾前允許追蹤」相反 |
| **＝ release（0.32 s）** | 0–0.32 s | **無** | **推薦初值**：鎖定點與「命中成為真的」同一刻；不需要第二個時間概念的 tuning 起點 |
| < release（例 0.25 → 0.2 s） | 0–0.2 s | 無 | 給玩家 ~0.12 s 的「看到它鎖定」可讀窗；手感決策，Play 後再調 |
| > release | 0–t | 部分 | 命中窗內仍追蹤 ⇒ hitbox 追人，正是要消除的 |

**Bake root displacement 的方向一致性**：
- `ExecuteBakedCurveMovement` 每帧沿**當下** `transform.forward` 積分無號速度（ADR-007 D2 的明文例外）。⇒ **位移方向一致的條件是：位移發生時 facing 已穩定**。
- `EnemyPunch`：位移恆 0 ⇒ 本招**沒有方向一致性問題**；有 Bake 只帶來「出拳原地不動」。
- 玩家 `MeleeSlash1`（對照）：0.6 s、總位移 0.80 m，0.2 s 前只有 0.13 m、峰值在 0.38 s；facing 於段落開始承諾、X Bot `aimFacingTurnSpeed 10`（τ≈0.1 s）⇒ 主要位移發生時 facing 已收斂。
- **通則（建議寫進 Living Spec）**：有位移的 Bake，承諾時點應早於位移起點至少 ~3τ（τ = 1／`aimFacingTurnSpeed`；Y Bot τ≈0.125 s ⇒ ~0.375 s），否則前衝會沿追蹤弧線彎曲。可做成一條資產關聯測試（§6 AC-Q2-5）。

**機制（最小）**——兩個缺口，各補一個 authored 值：

| 缺口 | 最小補法 | 不選的替代方案 |
|---|---|---|
| **方向來源**：敵人沒有 `IAimSource` ⇒ `ResolveAimContext` 恆回 default | `ActionTargetingPolicy` 新增一個成員（暫名 `CombatTarget`）：承諾指向 `CombatContext.TargetPosition`；無目標 ⇒ `HasAim=false`（照舊安靜退化）。唯一使用者：`EnemyPunchDefinition`（現值 `CameraForward` 對敵人無意義） | ① 給 Y Bot 一顆讀黑板的 `IAimSource` 元件：多一個元件＋接線＋資產改動，且 `IAimSource.TryGetAimPoint()` 無 data 參數；② 鎖定「當下 forward」：違反 D1（以 facing 冒充瞄準） |
| **origin**：方向需要角色位置；目前 origin 來自 `IAimSource.CommitmentOrigin`（介面註解自承這是暫時耦合） | FSM 已持有 `MotionDriver`（＝角色 root）；把 root 當作 `ActionState` 的承諾 origin。玩家 `AimResolver` 在同一個 root ⇒ **數值完全相同** | 保留兩個 origin 來源（玩家用 aim source、敵人用 root）＝為同一個值留兩個載體（§7-Q2c） |
| **時點**：承諾目前固定在段落開始 | `ActionPhaseEntry` 新增承諾時點（normalized，**預設 0 ＝段落開始**）。承諾點前 `TryGetFacingCommitment` 回 false ⇒ facing 落到優先序 2（敵人＝持續追蹤），之後鎖定 | 「`CombatTarget` policy 一律在 release 鎖」＝把時點偷偷綁在 policy 上，兩個概念黏在一起 |

| 項目 | 內容 |
|---|---|
| 負責模組 | `ActionState`、`ActionDefinitionSO`／`ActionPhaseEntry`、`FullBodyStateMachine`（傳 origin）、`EnemyPunchDefinition.asset`（Editor API） |
| 資料來源 | `CombatContext.TargetPosition`（敵人 producer 已寫）；root 位置 |
| 寫入者 | `ActionState._releaseContext`（不變）；黑板不變 |
| 讀取者 | `CharacterFacingSource`（不變）；sink（effect，近戰不使用方向） |
| 測試缺口 | 無任何測試涵蓋「敵人出拳有沒有承諾」；`docs/19` T1 只測 `TryResolveFacing` 真值表，從未測敵人的 `ActionState` 真的產生承諾 |
| 回歸風險 | 玩家：所有 Definition 預設時點 0、policy 不變 ⇒ **逐位元不變**（要以既有 TD5／AC1／AC2／TC6 全綠為證）。敵人：命中窗內不再追蹤 ⇒ 玩家側移閃避變得有效（預期）；鎖定時若追蹤落後 < 8°，承諾死區使角色不再轉（≈「就地鎖住」）；若 > 8° 會在 release 後再補轉一次（殘餘追蹤，需量測 M2） |

### 3.3 Q3 — 四種 eligibility 分開

| 概念 | 回答的問題 | Owner | 現況 | Q3 後 |
|---|---|---|---|---|
| **Target Eligibility** | 「我能不能把它當成要追、要打的對象」 | 各角色的目標 producer | 玩家：marker＋非自己＋存活；**AI：不檢查** | AI 兩顆元件加**存活**判準；玩家不變 |
| **Hit Detection** | 「這次投遞的幾何碰到它了嗎」 | 各 sink（形狀專屬） | 近戰 trigger、投射物 sweep（屍體透明）、AoE overlap | **不動**（含「屍體不擋子彈」） |
| **Damage Eligibility** | 「這一下要不要扣血、會不會致死」 | `CharacterHealth.ApplyDamage` | 死亡／非正數 ⇒ false | **不動** |
| **Effect Eligibility** | 「這次命中要不要產生副作用（命中音、Slow、impact）」 | 各 sink | 投射物：屍體不產生；**近戰命中音照播**；**AoE 照 Slow** | （選配，§7-Q3a）近戰命中音與 AoE Slow 以**命中當下是否存活**為準 |

⛔ **不合併**：四者的 owner、時點、輸入都不同。唯一共享的是一個**謂詞**「存活或無生命值元件」——它是 Target Eligibility 與 Effect Eligibility 的共同輸入，**不是**一個合併後的 eligibility 系統。

| 項目 | 內容 |
|---|---|
| 負責模組 | `AIMovementSource`、`AIInputSource`（必要）；`MeleeHitboxSink`、`GroundEffectSink`（選配）；共享謂詞住 Survivability（`CharacterHealth` 上的靜態判準，或各處內聯——§7-Q3b） |
| 資料來源 | **目標的** `CharacterHealth.IsDead`（元件直讀，與玩家選目標、投射物同一條通道）；AI 端在 target 指派時快取元件，零 GC |
| 寫入者 | `AIMovementSource` 寫 `CombatContext`（目標死 ⇒ 視同無目標：整體 default，Hold）；`AIInputSource` 不寫黑板 |
| 讀取者 | 受影響：Facing、HealthBar（`InCombat` 變 false ⇒ 敵人血條收起）、`LocomotionModel` cap、HeadLook（Y Bot 沒掛） |
| 測試缺口 | 無 AI 目標存活測試；無「近戰打屍體不播命中音」測試；`PlayerCombatContextSource` 的「缺 `CharacterHealth` 視為合法」只在玩家側有測試 |
| 回歸風險 | 玩家重生（`Revive`）後目標重新合法 ⇒ 敵人在 aggro 內會**立刻**再交戰（重生點距離決定）；`AIInputSource` 新增 `using Project.Core.Survivability`——A27 的禁用清單不含它（要以 A27 綠為證） |

### 3.4 Q4 — 受有效攻擊即交戰（AI）

| 項目 | 內容 |
|---|---|
| 負責模組 | `AIMovementSource`（唯一 `CombatContext` 寫入者，A5 不變） |
| 資料來源 | 自己的 `Survivability.JustTookDamage`（順序 0.5 發布 → 2.5 讀得到，零新欄位） |
| 「有效敵對攻擊」的定義 | ＝ `CharacterHealth` 接受了一次**未致死**傷害（`ApplyDamage` 已濾掉死亡與非正數）。⚠️ 只有 Slow、不扣血的攻擊**不算**（目前沒有這種投遞；Ice 傷害 15） |
| ⚠️ 只加進入條件不夠 | 現行規則已交戰時用 leave 12 m ⇒ 在 15 m 被打：本帧進、下帧就離開。需要**保留**機制 |
| 保留形狀（推薦） | 時間型，對齊玩家 producer 的形狀：`InCombat = 距離規則 ∨（距上次受擊 ≤ N 秒）`；N 為新的序列化欄位（§7-Q4）。被激怒的敵人會在 N 秒內 Approach，進入 12 m 後距離規則接手 |
| 已知限制 | 沒有攻擊者身分 seam（`docs/15` §16-6）⇒ 假設「打我的是我的 target」。單一玩家下成立；有多個攻擊來源（或互毆）時不成立——登記，不在本輪解 |
| 讀取者影響 | Facing 轉向玩家、`WorldSpaceHealthBar` 出現、Approach 起動；`AIInputSource` 不受影響（距離觸發） |
| 測試缺口 | `ResolveCombatEngagement` 是純函數但沒有時間／受擊輸入；無「aggro 外受擊」測試 |
| 回歸風險 | 低。距離規則逐位元保留；只有「受擊」這條新分支 |

### 3.5 Q5 — Combat Action ≠ InCombat

**現況**：`interactionThisFrame = RequestedActionSlot ≠ None ∨ JustTookDamage`（`PlayerCombatContextSource.cs:64-65`）——**任何**出手請求（含被冷卻擋下的）都會進入交戰並刷新 5 s 計時。

**兩種收斂形狀**：

| | 規則 | 空地出手 | 對 8–12 m 敵人出手（soft target 有、黏性目標無） |
|---|---|---|---|
| **Q5-A 嚴格** | 出手**永不**進入交戰；已交戰時出手只刷新計時 | 不交戰 | **不交戰**（敵人在 6 m 外） |
| **Q5-B 有對象才算**（推薦） | 出手時**存在合法候選**（黏性目標或 soft target）才算互動 | 不交戰 | 交戰 |

**🔴 隱性相依：combat directional speed cap**。
`LocomotionModel.ApplyCombatDirectionalSpeedLimit` 只看 `InCombat`；`docs/11` §13 用它把施法中側移／後退的 Spell 8-way playback 從 **2.07–2.17× 壓到 1.35×**。X Bot 對 `InCombat` 唯一有實際效果的 consumer 就是這個 cap（facing 不吃 persistent、HeadLook 需要 `HasTarget`）。
⇒ Q5 之後「**沒有交戰時施法側移**」會回到 2.07–2.17× playback：Q5-A 下連「對 10 m 外的敵人施法」都會中；Q5-B 下只剩「空地施法」。

**cap 閘門的三個選項**（§7-Q5b）：

| | 做法 | 代價 |
|---|---|---|
| **C-1** | 接受：空地施法側移不受 cap（Q5-B 時） | 零機制；可見的腳步滑動只在無敵人時出現——**要 Play 判斷可不可以接受** |
| **C-2** | cap 改為無條件（只要 actor 有 profile） | X Bot 平時轉向暫態也會被減速（`docs/48` §7 量到戰鬥轉向最低 0.553–0.677）——改變一般手感，⛔ 不推薦 |
| **C-3** | 新的黑板事實「上半身施法中」供 model 讀 | 黑板 schema 變更（CLAUDE.md 判準①），且 model（順序 3）早於 FSM（順序 4）⇒ 只能讀上一帧——要先開 ADR-007 修訂討論，⛔ 不屬最小方案 |

| 項目 | 內容 |
|---|---|
| 負責模組 | `PlayerCombatContextSource`（唯一寫入者不變） |
| 資料來源 | `Intent.RequestedActionSlot`＋同一 `Tick` 已算出的 `hasTarget`／`hasSoftTarget`（零新欄位） |
| 讀取者 | `LocomotionModel` cap（受影響）、Facing（玩家無效果）、HeadLook（需 `HasTarget`，不受影響） |
| 測試缺口 | `CombatContextTests.TC1_ActionIntent_EntersCombatImmediately` 釘的正是要推翻的規則 ⇒ 同包取代（不是暫停） |
| 回歸風險 | **中**：見 cap 相依。另：`docs/15` §4.2 的離開規則依賴「上次互動」——Q5-B 下對空氣出手不再延長交戰，被圍毆時仍靠受擊刷新（TC1B 保持） |

### 3.6 Q6 — 受傷聲跟傷害事件（**runtime 零改動**）

| 項目 | 內容 |
|---|---|
| 負責模組 | `AudioController`（已讀 `Survivability.JustTookDamage`，`:122`） |
| 現況 vs Q6 | **已一致**：Roll／Traversal 擋下 Hurt 狀態時，事件仍發布、語音仍播 |
| 要改的只有措辭 | `SurvivabilityData.JustTookDamage` 註解「傷害與扣血照常成立……**只有表現**被丟棄」→「只有**硬直反應（Hurt 狀態）**被丟棄；受擊**事件**的表現（語音）照常」；`docs/37` 的 Hurt 列補一句 |
| 測試缺口 | 沒有任何測試釘住「受擊語音不看 FSM 狀態」——未來有人「順手」改成跟 `HurtState` 走，不會有任何紅燈 |
| 驗證 | 保留人工聆聽（翻滾中被打）——語音好不好聽只有人聽得出來 |

---

## 4. 專審：F-01 的兩種作用域、F-07 的無效保證

### 4.1 F-01：per-segment（facing）vs per-release（effect）

| 時間點 | facing 承諾 `_releaseContext` | 效果落點 `_effectContext` |
|---|---|---|
| 段落開始（`OnEnter`／`TryAdvanceChain`） | 取一次 | 以同一值初始化 |
| 前搖每帧（`OnTick` 開頭） | **不動** | 重解：相機點；`CameraConeSoftTarget` 時**重選** soft candidate；解不出（`HasAim=false`）時保留上一個 |
| release 帧 | 不動 | 交付給 sink（`in` 複本）＝**鎖定** |
| release 後、段落結束前 | 不動 | 仍每帧刷新，但同段不會再 release（`_releaseEmittedThisExecution`）⇒ 無讀者 |
| 下一段 | 重取 | 重取 |

**Q1 接受的分家**（要寫進 ADR-007 修訂與 AC B）：
1. 目標在前搖期間移動 ⇒ 身體朝段落開始的位置、火球朝 release 當下的位置（3 m 距離約 13°，`docs/11` §14）。
2. 前搖期間相機轉出 25°／12 m 角錐 ⇒ effect 改用相機射線點。
3. 多個敵人時，effect 可在前搖期間換到另一個敵人（現場景只有 1 隻，不可達）。

**`TD5B` 為什麼是無效保證**：測試輔助 `Entry(...)` 的 `releaseNormalizedTime` 預設 0 ⇒ `OnEnter → EnterPhase → TryEmitRelease` **在進場當帧就 release**。之後測試才把 soft target 清掉、把相機點改走——那時 release 早已完成。斷言「失效後仍須保留段落開始時承諾的 soft-target point」因此**在任何 effect 實作下都會綠**，包括它想防止的那一種。換成出貨值 0.35，同一情境 release 會落在相機點（推導），與斷言相反。
⇒ `TD5B` 要拆成兩條：（a）facing 段落快照不受前搖變化影響（保留它的 facing 那一半）；（b）release 在 0.35、前搖中目標失效 ⇒ effect ＝ release 當下的相機點（新契約的「接受的分家 2」）。`TD5` 斷言已是新契約，只需改名。

### 4.2 F-07：A38 的 Hurt→Hurt

**機制事實**：`FullBodyStateMachine.EvaluateInterrupts` 對自己只問 `CanReenter(data)`，**從不呼叫 `CanBeInterruptedBy`** ⇒ config 裡 Hurt 清單中的 Hurt 不被任何 runtime 讀取（`docs/26` §J.3 自己也寫了「冗餘但無害」）。

**無效保證有三層**：
1. **A38** `Require(Hurt, Hurt, true, "Hurt → Hurt：允許（硬直中再次受擊要再踉蹌一次）")`——斷言一個**不影響行為**的資產值，訊息卻宣稱它控制行為。拿掉資產中的 Hurt ⇒ A38 紅、行為不變；想靠 config 關掉「硬直中再受擊」⇒ 被 A38 擋，就算放行也不會生效。
2. **HD3** 只呼叫 `hurt.CanReenter(data)` 這個方法本身，沒有讓 FSM 在 **Hurt 中** 再收到一次受擊、確認 `OnEnter` 重跑（計時重置）。
3. **HD 治具**（`HurtAndDeathStateTests.cs:391-393`）的 config 也把 Hurt 放進 Hurt 的清單 ⇒ 就算 HD3 走 FSM，也分辨不出「靠 config」還是「靠 `CanReenter`」。

**ADR-004 D6** 的三機制表（閘門／競爭／自我承諾）沒有列「同型重入」；`docs/26` §C.4 有列（第 3 條路）。ADR-004 是 Accepted ⇒ 不改；在 Living Doc 補第四列即可。

**最小修法**：A38 刪掉 Hurt→Hurt 那一行（註明由 HD3′ 守）；新增 HD3′：治具 config **不含** Hurt→Hurt，Hurt 中再受擊 ⇒ 仍重入且計時重置——同時證明行為與「config 不參與」。資產中的冗餘項可留可刪（§7-F07）。

---

## 5. 過期文件／註解 vs 真正的 runtime 變更

### 5.1 只改文字（零行為變更）

| 位置 | 現文字 | 處置 |
|---|---|---|
| ADR-007 D5、§6:175、§11、AC B | 「同一份」「結構上不可能分家」；AC B 舊證據 | D5 修訂框＋§11 列；AC B 改寫並標回 ⬜（等重驗）——**需使用者確認**（§7-Q1a） |
| ADR-007 AC A2 | 「出手皆然」持續面向 | 隨 Q2 改寫 |
| `docs/01-design-doc.md:209` | 「facing 與世界效果讀同一份」 | 改為兩個作用域 |
| `docs/15` §4.1、§4.4（:147）、:581、§16-8 | 出手即交戰；soft target 只在邊界消費；D5 被強化；origin 開放項 | 隨 Q1／Q2／Q5 改寫 |
| `docs/19` §3.2 | 敵人出拳有承諾、`EnemyPunch` 無 Bake | 更正 |
| `docs/21` §1.1、§1.3 | 「唯一 target reference」；「false pulse 幀讓 external Reaction 可被消費」 | 更正（兩份參照由 W15 約束；Reaction 已退役） |
| `docs/32`:367,558；`docs/33`:55,233(I9),460 | 人與火球共用同一份 | 導讀改寫 |
| `docs/37` Hurt 列 | — | 補 Q6 語意 |
| `ActionState.cs:182` | 「Reaction 是受擊而非出手，明確不取得」 | 刪 |
| `ActionState.cs:237-238` | `EnemyPunch` 是 Super Armor | 改（出貨 `Interruptible: 1`） |
| `ActionState.cs:569-573` | `TrySoftTarget`「也不在段落內追蹤」 | 改為「facing 不追蹤；effect 每帧重取候選」 |
| `HurtState.cs:75` | 「且不套用烘焙曲線位移」 | 改為「Hurt 永遠不套用烘焙位移（見類別註解）」 |
| `SurvivabilityData.JustTookDamage` 註解 | 「只有表現被丟棄」 | Q6 措辭 |
| `docs/26` 或 dev-spec §3.3 | 中斷三機制 | 補「同型重入」第四列 |

### 5.2 只改測試（零行為變更）

`TD5` 改名；`TD5B` 拆兩條（§4.1）；A38 刪 Hurt→Hurt 行；新增 HD3′（§4.2）；新增 Q6 結構測試（`AudioController` 的 Hurt 觸發只讀 `Survivability`，不認識 `StateType`／`HurtState`）。

### 5.3 真正的 runtime 行為變更

| 裁決 | 檔案 | 資產 |
|---|---|---|
| Q2 | `ActionState`、`ActionDefinitionSO`（policy 成員、`ActionPhaseEntry` 承諾時點）、`FullBodyStateMachine`（origin） | `EnemyPunchDefinition.asset`（Editor API：policy、承諾時點） |
| Q3 | `AIMovementSource`、`AIInputSource`；選配 `MeleeHitboxSink`、`GroundEffectSink`；選配共享謂詞 | 無 |
| Q4 | `AIMovementSource` | Y Bot 新序列化欄位（預設值即生效，可不動資產） |
| Q5 | `PlayerCombatContextSource` | 無（cap 若選 C-3 另計） |
| Q1／Q6 | — | — |

---

## 6. 最小變更方案與 Acceptance Criteria

**順序原則**：先做零行為變更的，讓後續行為變更的測試有乾淨基準；每個工作包一個 commit；行為變更各自獨立，可單獨 revert。

### WP-0 文件／註解／測試 fold back（Q1、Q6、F-07、過期註解）——**不需等待其餘裁決，只需 Q1a**

| AC | 驗證方式 |
|---|---|
| AC-0.1 §5.1 每一列都已改寫；全 repo 搜「讀同一份」「共用同一份」只剩歷史紀錄段（標明已被取代） | grep |
| AC-0.2 `TD5B-a`（facing 快照）與 `TD5B-b`（release 0.35、前搖中目標失效 ⇒ effect＝相機點）皆綠；`TD5B-b` 在**改回段落快照的 effect 實作**下會紅（以 mutation 手法人工確認一次） | EditMode |
| AC-0.3 HD3′：治具 config 不含 Hurt→Hurt，Hurt 中再受擊 ⇒ 重入且計時重置；A38 不再斷言 Hurt→Hurt | EditMode |
| AC-0.4 Q6 結構測試綠；`AudioController` 不含 `StateType`／`HurtState` | EditMode |
| AC-0.5 runtime 程式只有註解 diff；EditMode／PlayMode 全綠 | `git diff` ＋ `safe_unity_test.sh` |

### WP-1 Q3：AI 的 Target Eligibility（必要部分）

| AC | 驗證方式 |
|---|---|
| AC-1.1 目標 `IsDead` ⇒ `AIMovementSource` 寫 `CombatContext = default`、`MovementIntent = default` | EditMode（`CharacterHealth.InitializeForTest(0)`） |
| AC-1.2 目標 `IsDead` ⇒ `AIInputSource.WantsToAttack == false`，且 `Slot1ButtonDown` 不再送 | EditMode |
| AC-1.3 目標**沒有** `CharacterHealth` ⇒ 行為與今天逐位元相同（「缺席 ≠ 死亡」） | EditMode |
| AC-1.4 目標 `Revive` 後，距離規則照常恢復交戰 | EditMode |
| AC-1.5 A27 綠；`AIInputSource` 未引用 `PlayerRuntimeData`／FSM | EditMode |
| AC-1.6（Play）玩家死亡後 30 s：敵人停止繞行、不再揮拳、不再有命中音；血條收起 | probe M3 ＋ 人工 |
| AC-1.7（選配 Q3a）屍體：近戰不播命中音、AoE 不 Slow；活目標的致死一擊仍播命中音 | EditMode |

### WP-2 Q4：受擊即交戰

| AC | 驗證方式 |
|---|---|
| AC-2.1 距離 15 m（> leave 12）受未致死傷害 ⇒ 當帧 `InCombat = true` | EditMode（注入時間） |
| AC-2.2 之後每帧距離 > 12 m 且距受擊 ≤ N 秒 ⇒ 維持交戰；> N 秒且仍 > 12 m ⇒ 離開 | EditMode |
| AC-2.3 未受擊時，全部既有距離規則測試逐位元不變 | 既有 CombatContextTests 綠 |
| AC-2.4 目標已死時受擊（Q3 之後）不得重新交戰 | EditMode |
| AC-2.5（Play）10 m 外連發火球：Y Bot 轉身、接近、出拳 | probe M4 ＋ 人工 |

### WP-3 Q5：Combat Action ≠ InCombat（**阻塞於 Q5a／Q5b**）

| AC | 驗證方式 |
|---|---|
| AC-3.1 無任何合法候選時出手 ⇒ `InCombat` 不變（取代 TC1） | EditMode |
| AC-3.2（Q5-B）有 soft target 時出手 ⇒ 進入交戰 | EditMode |
| AC-3.3 已交戰時出手刷新離開計時；受擊進入（TC1B）不變 | EditMode |
| AC-3.4 cap 依 Q5b 的選項：C-1 ⇒ 記錄並人工確認空地施法側移的觀感；C-3 ⇒ 另案 | 人工 Play／另案 |

### WP-4 Q2：敵人出拳承諾點（**阻塞於 Q2a–c**）

| AC | 驗證方式 |
|---|---|
| AC-4.1 承諾時點 0 的 Definition：`TryGetFacingCommitment` 行為與今天逐位元相同（既有 TD5／AC1／AC2／TC6 全綠） | EditMode |
| AC-4.2 承諾時點 t>0：t 之前 `TryGetFacingCommitment == false`；t 當帧取得；之後目標移動不改變它 | EditMode |
| AC-4.3 `CombatTarget` policy＋有目標 ⇒ 承諾方向＝ root→`TargetPosition` 的水平方向；無目標 ⇒ `HasAim=false`、lifecycle 照常完成 | EditMode |
| AC-4.4 A29／A31／A33 綠（sink 不解方向、唯一 facing 送出者、debug 不重算） | EditMode |
| AC-4.5 資產關聯測試：有位移 Bake 的 Definition，承諾時點 ≤ 位移起點 − 3τ（或明列豁免理由） | EditMode（讀出貨資產） |
| AC-4.6（Play）玩家在敵人前搖期間側移：承諾點前敵人跟轉、之後不轉；命中窗內側移可閃避 | probe M2 ＋ 人工 |

---

## 7. 剩餘待裁決

| # | 問題 | 推薦 | 為什麼需要你決定 |
|---|---|---|---|
| **Q1a** | ADR-007 D5 改寫為「facing 承諾段落一次；effect release 定案」，AC B 改寫為「同段分家只允許來自 §4.1 的三種來源」並**重新驗收** | 同意 | 改的是 Trial ADR 的一條決策主體（不只粒度）；AC B 的 ✅ 會被撤回 |
| **Q2a** | `EnemyPunch` 承諾時點 | ＝ release（normalized 0.4 ⇒ 0.32 s）起步，Play 後再調 | 手感決策；早於 release 會給玩家可讀窗、但前搖瞄準變短 |
| **Q2b** | 新增 `ActionTargetingPolicy` 成員（`CombatTarget`）＋ `ActionPhaseEntry` 承諾時點欄位 | 同意（各一個、各有唯一使用者、預設值保留現行行為） | 觸及 ADR-007 D3 附屬禁令「不得擴充成一組朝向旋鈕」的邊界 |
| **Q2c** | 承諾 origin 統一為角色 root（`IAimSource.CommitmentOrigin` 留作相容、之後再退） | 同意（今天兩者數值相同） | 動到 `IAimSource` 的責任描述 |
| **Q3a** | Effect Eligibility 是否一併收斂（屍體不播近戰命中音、AoE 不 Slow 屍體） | 是（小改、可測） | 超出「AI 不選死目標」的字面範圍 |
| **Q3b** | 存活謂詞：`CharacterHealth` 上一個靜態判準 vs 各處內聯 | 靜態判準（5 個使用者，「缺席 ≠ 死亡」最易漂移） | CLAUDE.md「無使用者不加抽象」——這裡有使用者，但仍是新增 API |
| **Q4** | 被激怒後的保留：時間型 N 秒（推薦）vs 距離型 leash；N 值 | 時間型、N＝5 s（對齊玩家 `disengageSeconds`） | 數值與形狀屬手感 |
| **Q5a** | 嚴格（出手永不進入交戰）vs 有對象才算 | 有對象才算（Q5-B） | Q5-A 會讓「對 8–12 m 敵人施法」也失去 cap |
| **Q5b** | speed cap 閘門：C-1 接受空地施法不受限／C-2 無條件／C-3 新黑板事實 | C-1，並以人工 Play 判斷；不可接受再開 C-3 的 ADR 討論 | 這是 Q5 唯一會讓已裁決行為（`docs/11` §13）退步的地方 |
| F-07 | 資產中 Hurt→Hurt 冗餘項：保留（文件註明無作用）vs 刪除（Editor API） | 保留——刪除零收益且多一次資產改動 | 資產改動要先問 |

**明確不在本輪**：攻擊者身分 seam（`docs/15` §16-6）、陣營／友軍傷害、`ActionRequestTarget` 改名（`docs/51` F-08）、近戰連段的 release-once（`docs/51` F-09，今天不可達）。

**量測**（沿用 `docs/51` §I.3 編號）：M2（敵人追蹤與承諾後殘餘轉向）、M3（玩家死後敵人行為）、M4（遠距受擊交戰）是 WP-1／2／4 的 Play 驗收輸入；M5（翻滾中受擊語音）是 Q6 的人工聆聽。
