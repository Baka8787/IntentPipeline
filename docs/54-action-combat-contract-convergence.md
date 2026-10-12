# 54 — Action／Combat 契約收斂：設計與影響範圍（2026-10-12，第三版）

| | |
|---|---|
| **狀態** | 🟠 **Proposed**——設計與影響分析。⛔ 不得作為實作基線；runtime／prefab／ScriptableObject／測試皆未改 |
| **輸入** | `docs/51`（審查）＋ 使用者 2026-10-12 暫定裁決（第一輪）＋ 第二輪回覆（§0.1） |
| **路由** | 全部落在既有 Trial ADR（ADR-006／007／009）可修訂範圍或 Living Spec；**不需要新 ADR**。唯一會碰核心驅動介面的是 §3.5 選項 R2（`AnimationFacadeBase` 加一個唯讀查詢），走 ADR-006 Trial 修訂記錄 |
| **閱讀方式** | §0.1 裁決紀錄 → §1 衝突表 → §2 共享 vs 差異 → §3 逐項變更 → §4 F-01／F-07 專審 → §5 文件 vs runtime 分離 → §6 最小方案與 AC → §7 真正阻擋實作的事項 |

> **對 `docs/51` 的一處更正（第一版已記）**：`Bake_Fists_Punch_R` 的 `SpeedCurve` 全為 0（0–0.8 s，總位移 0 m）⇒ 敵人出拳**沒有前衝**、原地不動，只有**轉身追蹤**；後果是 root 子物件 `MeleeHitbox`（盒 0.9×1.2×1.2、中心在身前 0.9 m）在命中窗 0.32→0.8 s 內跟著轉。`docs/51` 已加更正框（`c7749fd`）。

---

## 0.1 裁決紀錄與實作狀態（第三版，2026-10-12）

| # | 狀態 | 內容 |
|---|---|---|
| **WP-0** | ✅ **已實作** `ec4bc09` | ADR-007 D5 修訂、AC B 撤回改寫、Living Docs fold back、TD5／TD5B_a／TD5B_b、A38、HD3B、CA11、過期註解。⚠️ 只做了 `dotnet build`（0 error）；**Unity Test Runner 未執行**（本輪 Editor 未連線） |
| **WP-1** | 🟡 **部分實作** `a378271` | 存活謂詞、`AIInputSource`、近戰命中音、AoE Slow、投射物／玩家改用同一謂詞。⏸ **`AIMovementSource` 那一半未做**——A4 白名單擋下（§3.3.1）。驗證同上 |
| **Q1a** | ✅ 已批准、已落地（WP-0） | — |
| **Q2a** | 🔬 **待量測** | M2a–M2c；不固定數值 |
| **Q2b** | ✅ 已批准（附 §3.2.3 約束） | 欄位與 policy 可先以「預設不改行為」實作——⚠️ 但屬 WP-4，本輪未做 |
| **Q2c** | ✅ 已裁決 | 玩家 origin 不動；敵人新 policy 用 root |
| **Q3a／Q3b** | ✅ 已批准、大部分已落地 | 剩 `AIMovementSource`（§3.3.1） |
| **Q4** | ✅ 方向已批准；🟡 **設計第三版**（§3.4） | L＝20 m 為第一個測試候選；近遠受擊一致；觸發原因與追擊／脫戰政策分開 |
| **Q5** | 🟡 **設計第三版**（§3.5） | R2 未批准；先界定責任歸屬，評估「由 Action 層提供 movement constraint」 |
| **WP-3b** | ✅ 原則同意；🟡 規則細節（§3.6） | 有有效目標的主動攻擊可算交戰互動；按鍵本身不算命中、不無條件刷新 |
| **Q6** | ✅ 已批准、已落地（WP-0） | — |
| **F-07** | ✅ 已批准、已落地（WP-0） | — |

---

## 1. 裁決 × 既有契約：衝突檢查

| 裁決 | 碰到的既有契約 | 判定 | 處置 |
|---|---|---|---|
| **Q1** | ADR-007 **D5**「facing 與 release 讀**同一份**」 | 字面衝突（runtime 自 2026-09-15 已是 Q1 的形狀） | ✅ Q1a：D5 修訂框＋§11 記錄（Trial，不開新 ADR） |
| 〃 | ADR-007 **AC B** ✅（2026-09-12 依「共用同一 commitment」打勾） | 證據失效 | ✅ Q1a：撤回 ✅、改寫、重驗 |
| 〃 | ADR-007 D4、`AimResolver` 責任 | ✅ 相容 | — |
| 〃 | `docs/15` §4.4「soft target 只在 commitment boundary 消費」 | 字面衝突 | Living Spec 改寫 |
| **Q2** | ADR-007 **AC A2**「敵人持續面向 target（……**出手皆然**）」 | 字面衝突 | AC A2 改為「出手時承諾點前追蹤、之後鎖定」 |
| 〃 | ADR-007 D5「不凍結：作用域粒度」 | ✅ 相容——時點屬粒度 | §3.2.3 的約束 |
| 〃 | ADR-007 D3 附屬禁令（預設值不得產生不一致朝向；⛔ 自由組合的朝向旋鈕） | ✅ Q2b 已批准，附 §3.2.3 約束 | 以架構測試把 policy 清單封閉 |
| 〃 | ADR-007 D1（⛔ 以 `transform.forward` 表示瞄準方向） | ✅ 只要承諾方向取**目標方向** | §3.2.2 |
| 〃 | `IAimSource.CommitmentOrigin` | ✅ Q2c：玩家不動 | 敵人 policy 另取 root（兩個載體，今天數值相同，文件註明） |
| **Q3** | `docs/19` §3.1（自我死亡抑制） | ✅ 不衝突——Q3 管的是**目標**死亡 | §H 不合併 |
| 〃 | A27（`AIInputSource` 不讀黑板／FSM） | ✅ 讀目標的 `CharacterHealth` 元件 | — |
| 〃 | ADR-009 D2、`docs/11`「屍體不擋子彈」 | ✅ 不動 | — |
| **Q4** | ADR-007 D3 補充條款（唯一 producer） | ✅ 寫入者仍是 `AIMovementSource` | — |
| 〃 | `docs/21` §1.1 黏性 8／12 m | **規則擴充** | §3.4：新增「激怒」來源，與距離來源 OR；距離規則逐位元保留 |
| **Q5** | `docs/15` §4.1「出手即進入交戰」 | 待推翻（WP-3b） | Living Spec 改寫 |
| 〃 | `docs/11` §13：speed cap 以 `InCombat` 為閘門 | 🔴 **cap 的閘門本來就是代理**——§3.5 追蹤結果 | Q5 先拆成 WP-3a（cap 改用正確的資料來源）→ WP-3b（`InCombat` 語意） |
| **Q6** | `SurvivabilityData` 註解「只有表現被丟棄」 | 措辭衝突 | 改文案 |

---

## 2. 應共享的跨模組契約 vs 應保留的 Player／AI 差異

### 2.1 共享

| 契約 | Owner | 為什麼必須共享 |
|---|---|---|
| 方向承諾的語意（承諾點取一次 facing；release 定案 effect；sink 只消費交付值） | `ActionState` | 同一顆 state 承載玩家與敵人；差異只能在**資料**（policy、時點），不得在程式分支（A19 精神） |
| **最小存活判斷**：「沒有 `CharacterHealth` ＝視為活著；有則看已 commit 的 `IsDead`」 | Survivability | Q3b；它是 Target Eligibility 與 Effect Eligibility 的共同**輸入**，不是合併後的規則 |
| 後果判定（Damage Eligibility） | `CharacterHealth.ApplyDamage` | ADR-009 D2 |
| 「受到敵對攻擊 ⇒ 交戰」 | 各自的 combat context producer | 同義規則，各在自己的 producer（ADR-007 D3） |
| 死亡自我抑制 | 各 consumer | `docs/19` §3.1，不變 |

### 2.2 保留差異

| 差異 | 玩家 | 敵人 | 為什麼保留 |
|---|---|---|---|
| 承諾方向來源 | 相機射線／soft target | combat target（新 policy） | 「瞄哪打哪」vs「打它的目標」 |
| 承諾 origin | `IAimSource.CommitmentOrigin`（**不改**，Q2c） | root（新 policy 專用） | Q2c；今天兩者數值相同 |
| 承諾時點 | 段落開始 | 承諾點（待量測） | 抗抽搐 vs 前搖瞄準 |
| 非出手時 facing | 跟移動 | 持續面向目標 | ADR-007 2026-09-10，W14 |
| 目標選擇 | 黏性 marker＋相機錐＋soft candidate | 序列化 Transform | 敵人只有一個已知目標 |
| 交戰規則與數值 | 6／9 m ＋ 5 s AND | 8／12 m（＋Q4 激怒） | tuning 與角色語意不同 |

---

## 3. 逐項變更

### 3.1 Q1 — per-segment facing、per-release effect（✅ 已批准；runtime 零改動）

| 項目 | 內容 |
|---|---|
| 負責模組 | `ActionState` |
| 資料來源 | `IAimSource.TryGetAimPoint`（相機射線，帧快取）＋ `CombatContext.SoftTargetPosition`（每帧無記憶候選） |
| 寫入者／讀取者 | `_releaseContext` → `CharacterFacingSource`（4.6）；`_effectContext` → 所有 sink 的 `Release(in)`。不上黑板 |
| 現況 vs Q1 | 已一致：`OnTick` 先刷新 effect 再 `TryEmitRelease`；sink 收到 `in` 複本 ⇒ release 即鎖定 |
| 測試缺口 | `TD5B` 無效保證（§4.1）；前搖中「相機轉出角錐」與「release 後不再 release」無測試 |
| 回歸風險 | 無（只改文件與測試）；AC B 重驗 |

### 3.2 Q2 — 敵人出拳：承諾點前追蹤、之後鎖定

#### 3.2.1 承諾時點：候選與驗證方法（🔬 待量測，不固定數值）

**時間軸事實**（出貨資產）：`EnemyPunchDefinition` Start 0.8 s（＝Bake 時長＝clip 時長）、`ReleaseNormalizedTime 0.4` ⇒ release **0.32 s**；命中窗 0.32→0.8 s；`Enemy_Punch_R` transition `_FadeDuration 0.15`、`_Speed 1`；Y Bot `aimFacingTurnSpeed 8`（每帧 `Slerp(cur, target, 8·dt)` ⇒ 時間常數 τ ≈ 0.125 s）；承諾死區 8°。
⚠️ **release 0.4 的來源沒有紀錄**（`docs/11`／`docs/21` 都沒寫它對應 clip 的哪一帧）——它是否等於「拳頭打出」本身就未驗證。

**候選**

| 候選 | 承諾時點 | 前搖追蹤 | 命中窗內 | 主要疑問 |
|---|---|---|---|---|
| **C-R** release 鎖定 | 0.32 s | 0–0.32 s | 不追蹤（但見下方「鎖定後補轉」） | 玩家還有沒有閃避的可能 |
| **C-Δ** release 前 Δ 鎖定 | 0.32 − Δ（候選 Δ ＝ 0.10／0.15／0.20 s） | 0–(0.32−Δ) | 不追蹤 | Δ 多大才「看得到、躲得掉」；前搖瞄準還剩多少 |
| **C-A** 動畫定錨 | clip 的「蓄力結束、拳開始前送」那一帧 | 到蓄力結束 | 不追蹤 | 需要先量 clip（M2a）；可能與 C-R／C-Δ 之一重合 |

**靜態推導**（⚠️ 只為決定量測範圍，不作結論）：
- 敵人停在約 1.45 m（`docs/21` §1.3）。玩家側移角速度 ω ≈ v／d：Walk（0.3651 × 6.26 ≈ 2.29 m/s）≈ **90°/s**；Run（0.75 × 6.26 ≈ 4.70 m/s）≈ **186°/s**。玩家戰鬥中 facing 跟移動 ⇒ 在自己的本地座標是「前跑」，不受側移限速。
- 追蹤穩態落後 ≈ ω·τ：Walk ≈ **11°**、Run ≈ **23°**。
- hitbox 能涵蓋的偏角 θ_max ≈ asin((0.45 + r)／1.45)：r ＝ 0.32（X Bot prefab 的 `CharacterController.m_Radius`）⇒ **≈ 32°**；r ＝ 0.12（`docs/21` §1.3 引用的值）⇒ **≈ 23°**。⚠️ **兩份來源不一致**（動態膠囊 `docs/27`／`28` 會在 runtime 改形狀）——量測要讀 runtime 實際半徑。
- **鎖定後補轉**：承諾方向取「鎖定當下的目標方向」（§3.2.2）。若此刻落後 > 8° 死區，facing 會繼續以 τ 轉向**鎖定時的目標點**——命中窗打開時，盒子對準的是「玩家 Δ 秒前的位置」，追蹤落後帶來的餘裕被補轉吃掉。⇒ 閃避條件近似 ω·Δ > θ_max（落後 < 8° 時則近似 ω·Δ ＋ 落後 > θ_max）。
- 推導結果（未驗證）：**C-R（Δ＝0）下，Walk 側移幾乎躲不掉；Run 是否躲得掉取決於 r**。C-Δ 下 Run 約需 Δ ≥ 0.12–0.17 s；Walk 約需 Δ ≥ 0.26–0.36 s——**後者已超過整段 0.32 s 前搖**，代表 Walk 側移可能在任何 Δ 下都躲不掉，除非在前搖開始前就已經在移動。
- **可讀性**：人類反應 ≈ 0.2–0.25 s。鎖定到開窗 < 0.2 s 時，玩家無法「看到鎖定再躲」，只能「看到前搖就躲」——此時可讀的是**前搖動畫本身**，不是鎖定。⇒ 承諾時點與前搖動畫的可讀起點要一起看。

**驗證方法**

| # | 量什麼 | 怎麼量 | 環境 |
|---|---|---|---|
| **M2a** | clip 的蓄力結束帧、拳最遠帧；release 0.32 s 落在哪 | `AnimationClip.SampleAnimation` 對 Y Bot 以 60 Hz 取樣 `Fists_Punch_R`，記錄 RightHand 在 root 空間的位置與前向速度（含 fade 0.15 s 的影響另記） | Editor（不需 Play） |
| **M2b** | 每個候選 × 每種玩家策略的命中率、開窗瞬間偏角、鎖定後補轉量 | PlayMode probe：承諾時點 ∈ {0.12, 0.17, 0.22, 0.27, 0.32} s × 玩家策略 {站立；前搖開始即 Walk 側移；即 Run 側移；鎖定當帧才開始側移；Roll}× 左／右；每格 ≥ 10 次。記錄 hit／miss、開窗帧的 facing 偏角、`OnTriggerEnter` 帧、runtime 膠囊半徑 | Editor＋PlayMode |
| **M2c** | 可讀性 | 人工 Play：在選定的 1–2 個候選下，能不能「看出要躲」 | 人工 |

**選值要先回答的設計目標**（待量測後由你決定，本提案不預設）：站立不動是否必中；Walk 側移是否應該能躲；Roll 是不是唯一可靠的閃避。

#### 3.2.2 機制（最小）

| 缺口 | 最小補法 | 不選的替代 |
|---|---|---|
| 方向來源 | `ActionTargetingPolicy` 新增一個成員（暫名 `CombatTarget`）：承諾指向 `CombatContext.TargetPosition`；無目標 ⇒ `HasAim=false`（照舊安靜退化）。唯一使用者 `EnemyPunchDefinition` | 給 Y Bot 一顆讀黑板的 `IAimSource`（多元件、多接線、資產改動）；鎖定「當下 forward」（違反 D1） |
| origin | **只對 `CombatTarget` policy** 使用角色 root（FSM 已持有 `MotionDriver`＝root）。玩家路徑仍用 `IAimSource.CommitmentOrigin`（Q2c） | 統一兩條路的 origin（Q2c 已否決） |
| 時點 | `ActionPhaseEntry` 新增承諾時點（normalized，**預設 0 ＝段落開始**）。時點前 `TryGetFacingCommitment` 回 false ⇒ facing 落到優先序 2（敵人＝持續追蹤） | 把時點綁死在 policy 上（兩個概念黏在一起） |

**Bake root displacement 的方向一致性**：`ExecuteBakedCurveMovement` 每帧沿**當下** `transform.forward` 積分（ADR-007 D2 明文例外）⇒ 一致的條件是「位移發生時 facing 已穩定」。`EnemyPunch` 位移恆 0 ⇒ 本招無此問題；玩家 `MeleeSlash1`（0.80 m，0.2 s 前僅 0.13 m、峰值 0.38 s；X Bot τ≈0.1 s）在段落開始承諾下已收斂。**通則**寫進 Living Spec：有位移的 Bake，承諾時點應早於位移起點至少 ~3τ，否則前衝沿追蹤弧線彎曲（AC-4.6 把它變成資產關聯測試）。

#### 3.2.3 「不得形成任意 Facing 控制介面」的具體約束（Q2b 的條件）

1. 新增的只有兩樣：**一個** policy 成員、**一個**承諾時點（normalized，per phase entry）。時點只改變「既有那一份承諾**何時**取得」，不改變它是什麼。
2. ⛔ 不得有：每帧方向覆寫、方向權重／混合、第二個 facing 送出者（A31 不變）、在 Definition 上直接寫方向或角度、讓 sink 或 AI 影響 facing。
3. policy 清單封閉：新增一條架構測試（比照 A13′），`ActionTargetingPolicy` 成員必須恰好等於已裁決清單；加成員就必須同步改測試與 `docs/11` §8.3。
4. 預設值保留現行行為：時點 0、`CameraForward` ⇒ 所有既有 Definition 逐位元不變。

| 項目 | 內容 |
|---|---|
| 負責模組 | `ActionState`、`ActionDefinitionSO`／`ActionPhaseEntry`、`FullBodyStateMachine`（傳 root 給新 policy）、`EnemyPunchDefinition.asset`（Editor API） |
| 資料來源 | `CombatContext.TargetPosition`（敵人 producer 已寫）；root 位置 |
| 寫入者／讀取者 | `_releaseContext`（不變）→ `CharacterFacingSource`（不變）；黑板不變 |
| 測試缺口 | 從未測過敵人的 `ActionState` 會不會產生承諾 |
| 回歸風險 | 玩家逐位元不變（以 TD5／AC1／AC2／TC6 全綠為證）；敵人命中窗內不再追蹤 |

### 3.3 Q3 — 四種 eligibility 分開（✅ 已批准）

| 概念 | 回答的問題 | Owner | Q3 後 |
|---|---|---|---|
| **Target Eligibility** | 「我能不能把它當成要追、要打的對象」 | 各角色的目標 producer | AI 兩顆元件（`AIMovementSource`、`AIInputSource`）加存活判斷；玩家不變 |
| **Hit Detection** | 「這次投遞的幾何碰到它了嗎」 | 各 sink | 不動（含「屍體不擋子彈」） |
| **Damage Eligibility** | 「這一下要不要扣血」 | `CharacterHealth.ApplyDamage` | 不動 |
| **Effect Eligibility** | 「這次命中要不要產生副作用」 | 各 sink | 近戰命中音、AoE Slow 以**命中當下是否存活**為準（投射物已是） |

共享的只有**存活謂詞**（Q3b），住在 Survivability。致死一擊：命中前活著 ⇒ 命中音照播（這一下是有效命中）。

| 項目 | 內容 |
|---|---|
| 資料來源 | 目標的 `CharacterHealth.IsDead`（元件直讀）；AI 在 target 指派時快取元件，零 GC |
| 寫入者 | `AIMovementSource`：目標死 ⇒ `CombatContext = default`、`MovementIntent = default` |
| 讀取者影響 | Facing、HealthBar 收起、`LocomotionModel` cap（依 §3.5 的結果，之後不再讀 `InCombat`） |
| 測試缺口 | 無 AI 目標存活測試；無「打屍體不播命中音／不 Slow」測試 |
| 回歸風險 | 玩家重生後目標重新合法 ⇒ 敵人在距離內會立刻再交戰；`AIInputSource` 新增 `using Project.Core.Survivability`（A27 禁用清單不含它） |

#### 3.3.1 ⏸ 實作時發現的阻擋：`AIMovementSource` 不能讀目標的 `CharacterHealth`

`ArchitectureRegressionTests.A4` 對 `Core/Movement`（頂層，`AIMovementSource` 所在）採**白名單**：只允許
`Project.Core.Movement`／`Project.Core.Blackboard`／`Project.Core.Effects`，理由是 **ADR-003 D2：intent producer 必須 context-free、不得回讀 gameplay state**。
`Project.Core.Effects` 本身就被註明為「已知架構張力，不是被批准的模式」的 debt。
⇒ 在 `AIMovementSource` 加目標存活判斷，必須先在這份白名單加入 `Project.Core.Survivability`——那是**架構不變量的變更**，超出 WP-1 的批准範圍。第二版提案漏掉了這一點。

**為什麼它會撞上**：`AIMovementSource` 同時是**敵人的 intent producer**（ADR-003 D2 要它 context-free）與**敵人的 combat context producer**（ADR-007 D3、`docs/21` §1.1 刻意合併，避免第二份 target 真相）。目標合法性是後者的責任（玩家側在 `Core/Combat` 讀它，白名單允許），卻落在前者的資料夾規則裡。

| 選項 | 做法 | 代價 |
|---|---|---|
| **B1**（推薦） | `Core/Movement` 白名單加 `Project.Core.Survivability`，理由寫明「只供 combat context 角色讀**目標的**已 commit 死亡判定（`IsAliveOrUntracked`）；不讀自己的 gameplay state」，比照 `Core.Effects` 那一行的寫法登記 | 白名單多一項；需要你同意這是合法例外 |
| B2 | 把敵人的 combat context 角色拆成 `Core/Combat` 的獨立 producer | `CombatContext` 寫入者改變（A5）、管線順序（AI 移動要在 2.5 前拿到 `InCombat`）、prefab 新元件、`docs/21` 的「共用 target 真相」裁決要重議——屬 ADR 級，⛔ 不屬最小方案 |

在裁決之前：`AIInputSource` 已不再對屍體出手（WP-1）；**敵人仍會繞行並面向屍體**（交戰語境仍由距離決定）。

### 3.4 Q4 — 受擊交戰：觸發原因與追擊／脫戰政策分開（第三版）

**第二版的問題（使用者指出）**：只在「距離外受擊」才啟動激怒，會讓**同一個受擊事件依距離有不同語意**——
7 m 被打後退到 13 m 就脫戰，15 m 被打卻能追到 20 m。第三版把兩件事拆開：

| 層 | 回答的問題 | 輸入（全部是既有資料） |
|---|---|---|
| **觸發原因**（entry） | 「為什麼現在開始交戰」 | ①距離 ≤ 8 m（現行）；②本帧受到有效敵對傷害（`Survivability.JustTookDamage`，任何距離） |
| **追擊／脫戰政策**（retention） | 「交戰中，什麼時候算失去有效交戰條件、失去後多久放手」 | 目標位置、本次交戰是否受過傷害、時間 |

⚠️ **政策不看觸發原因**——只看「這一次交戰中有沒有被打過」。所以 7 m 被打與 15 m 被打得到**同一套**追擊範圍。

**最小規則（只用現有資料；不建立仇恨系統）**

```text
交戰事件（episode）＝ InCombat 由 false 變 true 到再變回 false 的一段。
私有狀態（AIMovementSource）：_damagedThisEpisode（bool）、_graceStartTime（無寬限時為無效值）

進入：!InCombat ∧（d ≤ 8 ∨ JustTookDamage）
受擊：JustTookDamage ⇒ _damagedThisEpisode = true；取消寬限        ← 任何距離、任何時候都一樣
有效交戰條件：目標合法 ∧ d ≤ R，其中 R = _damagedThisEpisode ? L : 12
失去條件：
  · 目標不合法（死亡）           ⇒ 立即結束，不給寬限                 ← 依賴 §3.3.1 的裁決
  · d > R                         ⇒ 開始寬限 G；寬限內 d ≤ R 或再受擊 ⇒ 取消
  · 寬限期滿                      ⇒ 結束；_damagedThisEpisode 清除
```

**與 8／12 m 的互動**

| 情境 | 今天 | 第三版 |
|---|---|---|
| 從未受擊，走近 ≤ 8 m、之後玩家退到 > 12 m | 立即離開 | 依 §7 的 Q4-G 決定：(i) 立即離開（未受擊時逐位元保留）或 (ii) 寬限 G 後離開（單一政策） |
| 7 m 被打，之後玩家退到 13 m | 立即離開 | **繼續交戰**（受過傷 ⇒ R＝L＝20 m） |
| 15 m 被打 | 無反應 | 進入；R＝20 m ⇒ 有效；Approach |
| 25 m 被打（> L） | 無反應 | 進入；但 d > L ⇒ 立即開始寬限 G；寬限內追進 20 m 就繼續，否則離開 |
| 交戰中玩家跑出 L | — | 寬限 G；G 內回來或再被打 ⇒ 繼續 |
| 玩家死亡 | 繼續交戰 | 立即離開（Q3；**依賴 §3.3.1**） |
| 敵人自己死亡 | 不寫 `CombatContext` | 不變 |

**數值**：L＝**20 m（第一個測試候選，非定值）**；G＝5 s（寬限候選）；8／12 m 不動。
**已知限制**：沒有攻擊者身分 seam（`docs/15` §16-6）⇒ 假設「打我的就是我的 target」。單一玩家下成立。

| 項目 | 內容 |
|---|---|
| 負責模組 | `AIMovementSource`（唯一 `CombatContext` 寫入者，A5 不變） |
| 資料來源 | 自己的 `Survivability.JustTookDamage`（黑板，0.5 → 2.5 讀得到；`Project.Core.Blackboard`，A4 白名單內）；目標位置 |
| 新增 | 兩個私有欄位；序列化 L、G |
| 測試缺口 | 交戰純函數沒有時間／受擊輸入 |
| 回歸風險 | 「受過傷的交戰」脫戰距離由 12 變 20——近戰中撤退的玩家會被追得更遠（使用者要求的一致性後果） |

### 3.5 Q5 — 施法期間的移動限制：資料來源追蹤

#### 3.5.1 現有速度限制的完整鏈路

```text
CombatDirectionalSpeedProfileSO（每方向引用 MotionBakeData ＋ maxPlaybackStretch；不存手填速度）
        ↓ LocomotionModel.combatDirectionalSpeedProfile（序列化，actor-scoped）
LocomotionModel.Tick（順序 3）
  ApplyCombatDirectionalSpeedLimit：profile ≠ null ∧ CombatContext.InCombat ∧ 有移動方向
        local = Inverse(transform.rotation) × DesiredDirection    ← 上一帧 MotionDriver 寫的實際 facing
        DesiredSpeedNormalized = min(意圖, profile.ResolveNormalizedSpeed(local))
        ↓ smoother
data.MoveSpeed／MoveDirection（唯一寫入者）＋ 動畫參數 MoveSpeed／MoveX／MoveZ
        ↓
ActionState（無 Bake 的施法）→ MotionDriver.ExecuteBaseMovement(data)（順序 6）
```

**兩個使用者**

| 使用者 | profile | 它校準的動畫表示 | 那個表示何時在 Layer 0 播放 |
|---|---|---|---|
| X Bot | `XBotSpellDirectionalSpeedProfile`（stretch 1.35，八向） | `Locomotion_2D_Spell_WalkRun` | **只在施法時**：`Spell_*` mapping 的 `BaseLayerTransition`（`AnimancerFacade.PlayMapping`） |
| Y Bot | `YBotCombatDirectionalSpeedProfile`（stretch 1） | Y Bot 的 `Move`＝2D cardinal mixer | 每次 `Move` |

**閘門正確性**

| 情境 | Layer 0 播什麼 | 該限速嗎 | 今天（`InCombat` 閘門） |
|---|---|---|---|
| X Bot 交戰中施法 | spell 8-way | 該 | 限 ✓ |
| X Bot 非交戰施法 | spell 8-way | 該 | 限 ✓——**只因為出手請求本身會進入 `InCombat`** |
| X Bot 交戰中移動、未施法 | 1D `Locomotion` | 不該（1D 只有前跑 clip） | 限 ✗（`docs/11` §13「已知並接受」的副作用；`docs/48` §7 量到戰鬥轉向最低 0.553–0.677） |
| Y Bot 交戰中側移 | 2D mixer | 該 | 限 ✓ |
| Y Bot 非交戰 | — | — | `!InCombat` 時 `AIMovementSource` 不產生意圖 ⇒ 不移動，無差異 |

**結論**：限速真正依賴的是「**Layer 0 正在播的 locomotion 表示，是不是這份 profile 校準的那一個**」。`InCombat` 只是代理，而且對 X Bot 而言，它正確只是因為「出手 ⇒ 進入交戰」。⇒ Q5 若只改 `InCombat` 語意，X Bot 非交戰施法側移會回到 2.07–2.17× playback（`docs/11` §13 修掉的那個問題）。

#### 3.5.2 現有合法資料能不能表達「施法期間」？

| 候選來源 | 能表達嗎 | 理由 |
|---|---|---|
| 黑板 `IntentData` | ❌ | 只有 `RequestedActionSlot`，是**請求邊沿**（單帧），不是持續期 |
| 黑板 `MovementIntentData` | ❌ | `DesiredSpeedNormalized`／`DesiredDirection`／`WalkModeActive`——都是移動意圖 |
| 黑板 `Arbitration` | ❌ | 只有 `BlockInput／BlockIK／BlockAudio／BlockExpression` |
| 黑板 `CombatContext` | ❌ | Q5 明文排除 |
| `IMovementModel` | ❌ | 只有 `IsProducingMotion`／`Tick`／`UpdateMotion`；states 持有它但沒有任何推送約束的 API（改它＝ADR-003 Accepted 的核心介面） |
| `ActionDefinitionSO`／`ActionPhaseEntry` | ❌ | 沒有任何移動或表示相關欄位 |
| FSM `TryGetActiveFacingCommitment` | ❌（不合法） | Movement 讀 StateMachine＝反向依賴（StateMachine 已依賴 `IMovementModel`）；且 model（順序 3）早於 FSM（順序 4） |
| **`AnimationFacadeBase`**（model 的 `Tick` 已經收到它） | ⚠️ **部分** | model 已合法讀 facade：`CaptureLocomotionTime` 用 `IsPlaying(locomotionAnimationKey)`＋`TryGetDominantChildNormalizedTime`（上一帧的播放狀態）。但 spell companion 是 `Layers[0].Play(mapping.BaseLayerTransition)` 播的，**沒有登記在 `_stateCache` 的任何 key 下** ⇒ 現有 API **問不到**「companion 是否在播」；只問得到 Layer 1 的 `IsPlaying("Spell_Fireball_1")` 這類 Action key |

⇒ **沒有任何現成的合法資料能直接表達「這份 profile 校準的表示正在播」**。最接近的是 model 已經在用的 facade 查詢路徑（上一帧狀態，一帧延遲）。

#### 3.5.3 責任歸屬：Gameplay 移動限制，還是 Presentation 動畫校正？（第三版）

| 面向 | 證據 | 指向 |
|---|---|---|
| **它改變什麼** | 限制的是 `DesiredSpeedNormalized` → smoother → `MoveSpeed`／`MoveDirection` → `MotionDriver` 的**實際位移**；施法中側移真的變慢、閃避距離真的變短 | **Gameplay** |
| **數值從哪來** | 每個方向引用 `MotionBakeData`，以角色滿速正規化；唯一設計參數 `maxPlaybackStretch` 是「腳頻可以拉多快」的美術預算 | 表現資產——但這是本專案的既定模式：locomotion 速度本來就由 Bake 決定（CLAUDE.md「MotionBakeData 是動作真實數值的單一真相」） |
| **何時生效（觸發）** | 今天：`InCombat`（代理）。真正的條件：「Layer 0 正在播這份 profile 校準的表示」，而那由 ADR-006 D2 的 prefab mapping 決定 | 現行實作把**觸發**綁在表現上 |
| **既有治理** | ADR-003 D3：Action 屬 intrinsic-motion，`OnUpdateMotion` 自帶位移（無 Bake 的施法借用 `ExecuteBaseMovement`）；ADR-006 §5「⛔ 不新增 MovementLock／AttackMovementPolicy」；`docs/21` §1.3.1「這不是 MovementLock 或 AttackMovementPolicy」 | Action 擁有施法期間的位移，但兩份文件都明確排除過「攻擊移動政策」 |

**判定**：它是 **gameplay 移動限制**，數值依專案慣例由 Bake 推導；問題只在**觸發**目前由表現（或其代理 `InCombat`）推測。
⇒ 正確的方向是讓觸發來自 gameplay 擁有者，而 X Bot 施法期間的 gameplay 擁有者是 **Action**。這與使用者的偏好一致。

**同時要承認**：Y Bot 的限速**不是** Action 觸發的——它是「交戰中持續面向目標 ⇒ facing 與移動分離」的結果（`usePersistentCombatFacing`）。所以觸發條件本來就有兩種，應該是 **profile 上的 actor policy**（X Bot＝施法期間；Y Bot＝交戰中），而不是一條全域規則。

#### 3.5.4 選項比較

**A — 由 Action 宣告，Movement 消費（推薦評估方向）**

```text
ActionPhaseEntry：一個型別化欄位，例如 LocomotionConstraint { None, DirectionalSpeedLimit }（預設 None ＝ 現行行為）
ActionState：進入／離開帶限制的 phase 時，經 IMovementModel 告知 model「目前有一個宣告了方向限速的 Action phase」
LocomotionModel：限速觸發 = profile 的 actor policy（Y Bot：InCombat；X Bot：Action 宣告）
                 數值仍是 actor 自己 profile 的 Bake 推導（Definition 不帶任何數值）
```

| | **A** Action 宣告 | **R2** facade 查詢 | R1 key 清單 |
|---|---|---|---|
| **依賴方向** | Core/StateMachine → Core/Movement（既有：`BaseState` 已持有 `IMovementModel`；A4 白名單已允許）；gameplay 宣告、gameplay 消費 | Core/Movement → Presentation.Animation（既有方向，但語意是**由表現狀態反推 gameplay 限制**） | 同 R2 |
| **跨模組契約數** | 3：①`ActionPhaseEntry` 一個型別化欄位 ②`IMovementModel` 一個方法（**核心驅動介面**，CLAUDE.md 判準③）③profile 的觸發 policy 欄位 | 2：①`AnimationFacadeBase` 一個唯讀查詢（**核心驅動介面**）②profile 引用 transition | 1：model／profile 持有 key 清單 |
| **治理** | ADR-003 已 Accepted ⇒ 介面加方法要以 Trial ADR 記錄；ADR-006 §5「不新增 AttackMovementPolicy」要窄化修訂（這是**型別化的單一 locomotion 限制**，不是攻擊移動政策框架） | ADR-006 Trial 修訂 | 無 |
| **同步風險** | Definition 的宣告 vs prefab mapping 的 companion（兩個 authored 事實）——但語意不同：宣告是 gameplay 規則、companion 是表現；不一致時 gameplay 仍正確，只是表現可能踩快 ⇒ 關聯測試（有 companion 的 layered mapping 對應的 phase 必須宣告） | profile 的 transition 引用 vs mapping 的 `BaseLayerTransition`（引用同一資產）⇒ 關聯測試 | 清單 vs mapping ⇒ 關聯測試；清單是第二份手寫事實，最容易漂移 |
| **時序** | `ActionState` 在順序 4 通知、model 在下一帧順序 3 讀 ⇒ **一帧延遲** | facade 狀態來自上一帧順序 5 ⇒ **一帧延遲** | 同 R2 |
| **測試成本** | 純 EditMode：假 model 驗 `ActionState` 的通知；model 純函數驗限制；不需 Animancer | 需要 facade 假件或 Animancer state | 同 R2 |
| **跨 actor 語意** | Definition 共用時，宣告對每個使用它的 actor 都是同一條 gameplay 規則；數值仍各自 | 完全跟著各 actor 的表現 | 同 R2 |
| ⛔ **不會變成** | 通用 Action Constraint Framework：只有一個型別化成員、只影響方向限速、沒有數值、沒有組合 | — | — |

**評估結論（未裁決）**：A 的依賴方向與語意都比 R2 正確，代價是多一個核心介面方法和兩項治理修訂；R2 契約較少但語意是反推。⛔ 兩者都**不新增黑板欄位**、都**不使用 `InCombat` 代替施法狀態**。

#### 3.5.5 Q5 的拆分（不變）

- **WP-3a**：限速觸發改為 profile 的 actor policy＋（A 或 R2）。
- **WP-3b**：`InCombat` 語意（§3.6）。**3a 之後**才不會連帶影響施法動畫。

### 3.6 WP-3b — Combat Action 與 InCombat（第三版）

**區分兩個不同的判定**：

| 判定 | 回答的問題 | 現有資料 | 用途 |
|---|---|---|---|
| **有效目標的主動攻擊**（Target Eligibility） | 「我這次出手是不是衝著一個合法目標」 | 同一 `Tick` 已算出的黏性目標 `hasTarget` 或 soft candidate `hasSoftTarget`（兩者都已通過 `IsHostileCandidateLegal`，含存活） | 算一次**交戰互動**：可進入、可刷新離開計時 |
| **實際傷害事件** | 「真的有傷害成立了嗎」 | 自己受傷：`Survivability.JustTookDamage` ✅；**自己打中別人：沒有**（沒有攻擊者身分 seam，`docs/15` §16-6） | 受傷照舊算互動；「打中別人」目前無法作為訊號——登記，不在本輪 |

**規則**：
```text
interaction = JustTookDamage ∨ (RequestedActionSlot ≠ None ∧ (hasTarget ∨ hasSoftTarget))
```
- 沒有任何合法候選時按攻擊 ⇒ **不是**交戰互動：不進入、不刷新。
- ⚠️ 殘留：順序 2.6 看不到 `ActionState` 是否接受這次請求（冷卻中被拒也會被算）——在**有合法目標**的前提下，這被視為「仍在跟目標交手」。若要只算「真的出手」，需要讓 combat producer 得知 Action 開始（例如經既有 `IActionLifecycleSink.Begin`），那會讓一個 gameplay 狀態 producer 掛在副作用 seam 上——不推薦，列為備案。

| 項目 | 內容 |
|---|---|
| 負責模組 | `PlayerCombatContextSource`（唯一寫入者不變） |
| 讀取者影響 | 只剩 `LocomotionModel` 的限速（玩家）——**因此依賴 WP-3a**；HeadLook 需要 `HasTarget`、facing 對玩家無效果 |
| 測試 | `CombatContextTests.TC1` 被取代：無候選時出手不進入；有候選時進入；`TC1B` 不變 |

---

## 4. 專審：F-01、F-07

### 4.1 F-01：per-segment（facing）vs per-release（effect）

| 時間點 | facing `_releaseContext` | effect `_effectContext` |
|---|---|---|
| 段落開始 | 取一次（Q2 後：承諾時點取一次，預設＝段落開始） | 以同值初始化 |
| 前搖每帧 | 不動 | 重解；`CameraConeSoftTarget` 時重選候選；`HasAim=false` 時保留上一個 |
| release 帧 | 不動 | 交付 sink（`in` 複本）＝鎖定 |
| release 後 | 不動 | 仍刷新，但同段不再 release ⇒ 無讀者 |

**Q1 接受的分家**（寫進 ADR-007 修訂與新 AC B）：①目標在前搖中移動；②前搖中相機轉出 25°／12 m 角錐 ⇒ effect 改用相機點；③多敵人時 effect 可換目標（現場景不可達）。

**`TD5B` 為什麼無效**：測試輔助 `Entry(...)` 的 `releaseNormalizedTime` 預設 0 ⇒ release 在進場當帧完成，之後才清掉目標、改相機點 ⇒ 斷言在任何 effect 實作下都綠。⇒ 拆成 `TD5B-a`（facing 快照）與 `TD5B-b`（release 0.35、前搖中目標失效 ⇒ effect＝相機點）。`TD5` 斷言已是新契約，只改名。

### 4.2 F-07：A38 的 Hurt→Hurt

- `EvaluateInterrupts` 對自己只問 `CanReenter(data)`，**從不呼叫 `CanBeInterruptedBy`** ⇒ config 中 Hurt 清單的 Hurt 不被任何 runtime 讀取。
- 無效保證三層：①A38 斷言該資產值且訊息宣稱它控制行為；②HD3 只呼叫 `CanReenter` 方法本身，沒有讓 FSM 在 Hurt 中再受擊；③HD 治具 config 也含 Hurt→Hurt，無法分辨是誰在起作用。
- **處置（✅ F-07 已批准）**：資產 entry **保留**；A38 移除該斷言，改成註解「本 entry runtime 不讀取，同型重入由 `HurtState.CanReenter` 決定，見 HD3′」；新增 **HD3′**：治具 config **不含** Hurt→Hurt，FSM 在 Hurt 中再收到受擊 ⇒ 重入且計時重置（`OnEnter` 重跑）。`docs/26` §C.4／dev-spec §3.3 補「同型重入不經 config」一列（ADR-004 Accepted，不改）。

---

## 5. 過期文件／註解 vs 真正的 runtime 變更

### 5.1 只改文字（零行為變更）

| 位置 | 處置 |
|---|---|
| ADR-007 D5、§6:175、§11、AC B | D5 修訂框＋§11 列；AC B 撤回 ✅、改寫（Q1a） |
| ADR-007 AC A2 | 改為「出手時承諾點前追蹤、之後鎖定」 |
| `docs/01-design-doc.md:209` | 兩個作用域 |
| `docs/15` §4.1、§4.4（:147）、:581、§16-8 | 隨 Q1／Q2／Q5 改寫；§16-8 結案 |
| `docs/19` §3.2 | 更正（敵人目前無承諾；Bake 位移 0） |
| `docs/21` §1.1、§1.3 | 「唯一 target reference」→ 兩份參照由 W15 約束；刪「false pulse 讓 Reaction 可被消費」；§1.1 補激怒規則（WP-2） |
| `docs/11` §13 | 補註：cap 閘門的正確資料來源見 `docs/54` §3.5（WP-3a 落地後改寫） |
| `docs/32`:367,558；`docs/33`:55,233(I9),460 | 導讀改寫 |
| `docs/37` Hurt 列；`SurvivabilityData.JustTookDamage` 註解 | Q6 措辭：被丟棄的是**硬直反應**，受擊**事件**的語音照常 |
| `ActionState.cs:182`、`:237-238`、`:569-573`；`HurtState.cs:75` | 過期註解 |
| `docs/26` §C.4 或 dev-spec §3.3 | 補「同型重入」 |

### 5.2 只改測試

`TD5` 改名；`TD5B` 拆兩條；A38 移除 Hurt→Hurt 斷言；新增 HD3′；新增 Q6 結構測試（`AudioController` 的 Hurt 觸發只讀 `Survivability`，不認識 `StateType`／`HurtState`）。

### 5.3 真正的 runtime 行為變更

| 裁決 | 檔案 | 資產 |
|---|---|---|
| Q3 | ✅ `AIInputSource`、`MeleeHitboxSink`、`GroundEffectSink`、存活謂詞（`a378271`）；⏸ `AIMovementSource`（§3.3.1） | 無 |
| Q4 | `AIMovementSource` | Y Bot 兩個新序列化欄位（L、G；預設值即可生效） |
| Q5（3a） | `LocomotionModel`、`CombatDirectionalSpeedProfileSO`（觸發 policy）；A 另含 `ActionPhaseEntry`／`ActionState`／`IMovementModel`；R2 另含 `AnimationFacadeBase`／`AnimancerFacade` | 兩份 profile 補觸發 policy（Editor API）；A 另含四份 Spell Definition 的宣告 |
| Q5（3b） | `PlayerCombatContextSource` | 無 |
| Q2 | `ActionState`、`ActionDefinitionSO`／`ActionPhaseEntry`、`FullBodyStateMachine` | `EnemyPunchDefinition.asset`（policy、承諾時點——**數值待 M2**） |
| Q1／Q6／F-07 | — | — |

---

## 6. 最小變更方案與 Acceptance Criteria

**順序**：零行為變更的先做；每個 WP 一個 commit；行為變更可單獨 revert。

### WP-0 文件／註解／測試 fold back（Q1、Q6、F-07）——✅ 已實作 `ec4bc09`

| AC | 狀態 |
|---|---|
| AC-0.1 §5.1 全部改寫；「讀同一份」只剩標明已被取代的歷史段 | ✅（ADR-007 D5 原句保留並標「已被取代」） |
| AC-0.2 `TD5B_a`／`TD5B_b` 綠；`TD5B_b` 在「effect 改回段落快照」時會紅 | 🟡 已寫；**未在 Unity 跑**；mutation 確認未做 |
| AC-0.3 HD3B 綠；A38 不再斷言 Hurt→Hurt | 🟡 已寫；**未在 Unity 跑** |
| AC-0.4 CA11 綠 | 🟡 已寫；**未在 Unity 跑** |
| AC-0.5 runtime 只有註解／log 字串 diff；EditMode／PlayMode 全綠 | 🟡 diff ✅；`dotnet build` 0 error ✅；**Unity 回歸未跑** |

### WP-1 Q3 eligibility——🟡 部分實作 `a378271`

| AC | 狀態 |
|---|---|
| AC-1.1 目標 `IsDead` ⇒ `AIMovementSource` 寫 `CombatContext = default` | ⏸ **未做**（§3.3.1，A4） |
| AC-1.2 目標 `IsDead` ⇒ `AIInputSource` 不送 `Slot1ButtonDown` | 🟡 已寫（`DeadTarget_IsNotAttacked_UntilRevived`）；未在 Unity 跑 |
| AC-1.3 沒有 `CharacterHealth` 的目標與今天相同 | 🟡 同上測試的前半 |
| AC-1.4 `Revive` 後恢復 | 🟡 同上測試的後半 |
| AC-1.5 屍體：近戰不是有效命中（無命中音）、AoE 不 Slow；致死一擊有效；投射物不變 | 🟡 `T24B`、`GroundEffect_DoesNotSlowDeadTarget_ButSlowsUntrackedTarget`；既有投射物測試；未在 Unity 跑 |
| AC-1.6 存活謂詞唯一定義；玩家／投射物改用後行為不變 | 🟡 `H14`；既有 TC／投射物測試；未在 Unity 跑 |
| AC-1.7 A4／A27 綠 | 🟡 人工比對白名單通過；未在 Unity 跑 |
| AC-1.8（Play）玩家死後敵人不揮拳、無命中音 | ⬜ 人工；繞行／面向屍體要等 AC-1.1 |

### WP-1b `AIMovementSource` 的目標合法性——⏸ 等 §3.3.1（B1／B2）

| AC | 驗證 |
|---|---|
| AC-1b.1 目標 `IsDead` ⇒ `CombatContext = default`、`MovementIntent = default`、Hold | EditMode |
| AC-1b.2 A4 的 `Core/Movement` 白名單只多 `Project.Core.Survivability` 一項，且附理由 | EditMode |
| AC-1b.3（Play）玩家死後敵人停止繞行、不面向屍體、血條收起 | 人工 |

### WP-2 Q4 受擊交戰——🟡 設計第三版（§3.4）

| AC | 驗證 |
|---|---|
| AC-2.1 從未受擊的交戰：依 Q4-G 的裁決，(i) 全部既有距離規則逐位元不變，或 (ii) 改為寬限 G 後離開 | EditMode |
| AC-2.2 **任何距離**受未致死傷害 ⇒ 當帧進入；之後 R＝L | EditMode（注入時間） |
| AC-2.3 一致性：7 m 受擊後退到 13 m 與 15 m 受擊，兩者的脫戰距離都是 L | EditMode |
| AC-2.4 d > R ⇒ 寬限 G；寬限內回來或再受擊 ⇒ 取消；期滿 ⇒ 離開並清除受傷標記 | EditMode |
| AC-2.5 目標死亡 ⇒ 立即離開、無寬限（依賴 WP-1b） | EditMode |
| AC-2.6（Play）10–20 m 外施法：Y Bot 轉身、接近、出拳；L＝20 是否合適 | probe M4＋人工 |

### WP-3a 限速觸發改為 actor policy（A 或 R2）——🟡 等 Q5 裁決（§3.5.4）

| AC | 驗證 |
|---|---|
| AC-3a.1 限速不再讀 `CombatContext`／`InCombat` 作為 X Bot 的觸發 | EditMode（架構測試） |
| AC-3a.2 Y Bot 的觸發 policy＝交戰中，行為逐位元不變 | EditMode |
| AC-3a.3 X Bot 施法中 ⇒ 限速與今天相同；未施法（1D 在播）⇒ 不限速，不論是否交戰 | EditMode |
| AC-3a.4（A）宣告與 layered mapping 的關聯測試；（R2）profile 引用的 transition 與 mapping 一致 | EditMode（讀出貨資產） |
| AC-3a.5 ⛔ 沒有新增黑板欄位；（A）`LocomotionConstraint` 只有 `None` 與一個限速成員 | EditMode |
| AC-3a.6（Play）空地施法側移 playback ≤ 1.35×；交戰中未施法的轉向不再被減速 | probe＋人工 |

### WP-3b `InCombat` ≠ Combat Action——依賴 WP-3a（§3.6）

| AC | 驗證 |
|---|---|
| AC-3b.1 無合法候選時出手 ⇒ 不進入、不刷新（取代 TC1） | EditMode |
| AC-3b.2 有黏性目標或 soft candidate 時出手 ⇒ 進入／刷新 | EditMode |
| AC-3b.3 受擊進入（TC1B）不變 | EditMode |

### WP-4 Q2 敵人承諾——🔬 等 M2a–M2c

| AC | 驗證 |
|---|---|
| AC-4.1 承諾時點 0 的 Definition 逐位元不變（TD5／AC1／AC2／TC6） | EditMode |
| AC-4.2 承諾時點 t > 0：t 前無承諾、t 取得、之後不變 | EditMode |
| AC-4.3 `CombatTarget`＋有目標 ⇒ root→目標水平方向；無目標 ⇒ `HasAim=false` | EditMode |
| AC-4.4 玩家仍用 `IAimSource.CommitmentOrigin`（Q2c） | EditMode |
| AC-4.5 policy 清單封閉；A29／A31／A33 綠 | EditMode |
| AC-4.6 有位移 Bake：承諾時點 ≤ 位移起點 − 3τ（或明列豁免） | EditMode（讀出貨資產） |
| AC-4.7 以 M2b 結果達成你選定的設計目標 | PlayMode probe |
| AC-4.8 可讀性 | 人工 |

---

## 7. 真正阻擋實作的事項（第三版）

| 阻擋 | 擋住哪裡 | 需要什麼 |
|---|---|---|
| **Unity Test Runner 尚未執行** | WP-0、WP-1 的 AC 狀態從 🟡 變 ✅ | Editor 連線後跑 `safe_unity_test.sh --mode EditMode`（PlayMode 回歸也跑一次） |
| **§3.3.1：B1 或 B2** | WP-1b；以及 WP-2 的「目標死亡 ⇒ 立即離開」 | 你決定是否把 `Project.Core.Survivability` 加進 `Core/Movement` 的 A4 白名單（B1） |
| **Q4-G：從未受擊的交戰要不要也用寬限 G** | WP-2 | (i) 不用，未受擊時逐位元保留；(ii) 用，整套只有一個脫戰政策 |
| **Q5：A 或 R2** | WP-3a → WP-3b | 選觸發的資料來源。A 需要 `IMovementModel` 加一個方法（以 Trial ADR 記錄）並窄化 ADR-006 §5；R2 需要 `AnimationFacadeBase` 加一個唯讀查詢 |
| **M2a–M2c** | WP-4 的承諾時點數值 | Editor（M2a 不需 Play）；之後依 §3.2.1 的設計目標選值。欄位與 policy 本身可先以預設不改行為實作 |
| WP-3b 備案 | 僅在你要求「只算真的出手」時 | 經 `IActionLifecycleSink.Begin` 通知 combat producer——不推薦 |

**現在不被阻擋、但本輪未做**：WP-4 的「承諾時點欄位＋`CombatTarget` policy」（預設不改行為）——它是 Q2 的一部分，使用者指示 Q2 保持設計／量測階段。

**明確不在本輪**：攻擊者身分 seam、陣營／友軍傷害、`ActionRequestTarget` 改名、近戰連段 release-once。