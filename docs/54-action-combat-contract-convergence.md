# 54 — Action／Combat 契約收斂：設計與影響範圍（2026-10-12，第四版）

| | |
|---|---|
| **狀態** | 🟠 **Proposed**——設計與影響分析。⛔ 不得作為實作基線；runtime／prefab／ScriptableObject／測試皆未改 |
| **輸入** | `docs/51`（審查）＋ 使用者 2026-10-12 暫定裁決（第一輪）＋ 第二輪回覆（§0.1） |
| **路由** | 全部落在既有 Trial ADR（ADR-006／007／009）可修訂範圍或 Living Spec；**不需要新 ADR**。唯一會碰核心驅動介面的是 §3.5 選項 R2（`AnimationFacadeBase` 加一個唯讀查詢），走 ADR-006 Trial 修訂記錄 |
| **閱讀方式** | §0.1 裁決紀錄 → §1 衝突表 → §2 共享 vs 差異 → §3 逐項變更 → §4 F-01／F-07 專審 → §5 文件 vs runtime 分離 → §6 最小方案與 AC → §7 真正阻擋實作的事項 |

> **對 `docs/51` 的一處更正（第一版已記）**：`Bake_Fists_Punch_R` 的 `SpeedCurve` 全為 0（0–0.8 s，總位移 0 m）⇒ 敵人出拳**沒有前衝**、原地不動，只有**轉身追蹤**；後果是 root 子物件 `MeleeHitbox`（盒 0.9×1.2×1.2、中心在身前 0.9 m）在命中窗 0.32→0.8 s 內跟著轉。`docs/51` 已加更正框（`c7749fd`）。

---

## 0.1 裁決紀錄與實作狀態（第四版，2026-10-12）

| # | 狀態 | 內容 |
|---|---|---|
| **WP-0** | **Implemented / Automated Verification Passed / Human Verification Pending**（`ec4bc09`） | 本案新增與修改的測試全部通過（Unity EditMode 2026-10-12 `20261012T082055Z`：898／896 passed，唯一失敗為與本案無關的場景 WIP；PlayMode `20261012T082210Z`：107／106，唯一失敗為既有 `C0`）；`TD5B_b` 已做 mutation 確認。仍待人工：AC B 的 Play 重驗、Q6 受擊語音聆聽。⛔ 不得標 Closed |
| **WP-1** | **Partial / Automated Verification Passed / Human Verification Pending**（`a378271`） | 已實作部分的測試全部通過（Unity EditMode 2026-10-12 `20261012T082055Z`：898／896 passed，唯一失敗為與本案無關的場景 WIP；PlayMode `20261012T082210Z`：107／106，唯一失敗為既有 `C0`）。未做：`AIMovementSource`（§3.3.1）。仍待人工：玩家死後敵人不揮拳、無命中音。⛔ 不得標 Closed |
| **B1／B2** | 🟡 **方案階段**（第四版 §3.3.1：四種方案比較） | 使用者：暫不批准擴大 A4 白名單；⛔ 不得以自己的 `Survivability` 代替目標存活 |
| **Q1a** | ✅ 已裁決（WP-0） | — |
| **Q2a** | 🔬 待量測（M2a–M2c） | 不固定 Punch 承諾時點 |
| **Q2b／Q2c** | ✅ 已裁決 | 屬 WP-4，未實作 |
| **Q3a／Q3b** | ✅ 已裁決 | 剩 `AIMovementSource`（依 B 方案） |
| **Q4** | ✅ 規則已裁決（第四版 §3.4） | **Q4-G＝(i)**：未受擊敵人保持 8／12 m；受到有效敵對傷害 ⇒ Provoked 交戰來源；**20 m chase／5 s grace＝第一輪量測候選**，不是平衡定值；目標死亡 ⇒ 立即解除；⛔ 通用仇恨系統。「目標死亡」那一條依賴 B 方案 |
| **Q5** | 🟡 **方向＝Option A；介面未批准**（第四版 §3.5.6：最小 typed contract） | 啟用權屬 Action／Movement；Bake 只給數值；X Bot／Y Bot 啟用條件分開；⛔ 框架；⛔ 預設黑板欄位 |
| **WP-3b** | ✅ 原則同意（§3.6） | 依賴 WP-3a |
| **Q6／F-07** | ✅ 已裁決（WP-0） | 聽感仍待人工 |

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

#### 3.3.1 ⏸ `AIMovementSource` 的目標有效性：方案比較（第四版；只提方案，未改 runtime）

**為什麼 `AIMovementSource` 同時產 `MovementIntent` 與 `CombatContext`**（`docs/19` §3.1，2026-09-10）：
它「**已經擁有** `target` 與接戰帶判定」；另建 combat producer 需要第二份 target 參考 ⇒ 「誰是我的目標」出現第二份真相（`docs/16` 加重項）。
該節同時記下取捨與**重拆條件**：「SRP 上略寬……**若日後敵人的戰鬥語境長出與移動無關的判定，再拆。**」
⇒ 目標有效性（Q3）與激怒交戰／寬限（Q4）**正是**與移動無關的戰鬥語境判定——原裁決自己定義的重拆條件已經成立。

**現行資料流**

```text
順序 0.5  CharacterHealth.PublishTo ─► 自己的 data.Survivability（⚠️ 自己的生存，≠ 目標的生存）
順序 1    AIInputSource.FetchRawInput（target#2、attackRange；WP-1 起讀「目標的」CharacterHealth）─► InputData.Slot1ButtonDown
順序 2    ProcessIntents ─► data.Intent
順序 2.5  AIMovementSource.ProduceIntent（target#1、aggro 8/12、站位帶）─► data.CombatContext ＋ data.MovementIntent
順序 2.6  （玩家）PlayerCombatContextSource.Tick ─► data.CombatContext      ← AI 沒有這一步
順序 4.6  CharacterFacingSource 讀 CombatContext
```
A4：`Core/Movement` 頂層白名單＝ Movement／Blackboard／Effects（ADR-003 D2：intent producer context-free）；`Core/Pipeline`、`Core/Combat` 白名單含 `Project.Core.Survivability`。

**四種方案**

| | ① 沿用現有資料契約 | ② 最小只讀目標有效性資訊 | ③ 拆分 CombatContext producer | ④ 擴大 A4 白名單 |
|---|---|---|---|---|
| **做法** | 不新增資料。`AIInputSource` 已不打屍體（WP-1）；`AIMovementSource` 不判斷 | 由合法上游把「目標是否有效」送到 `AIMovementSource`。可行載體只有：②a `InputData` 新欄位（`AIInputSource` 於順序 1 寫）；②b 在 `Core/Movement` 或 `Core/Blackboard` 宣告一支唯讀介面、`CharacterHealth` 實作（DIP） | 新增 `Core/Combat` 的 AI combat producer（與 `PlayerCombatContextSource` 並列），擁有 target／aggro／激怒／目標有效性並寫 `CombatContext`；`AIMovementSource` 改讀 `CombatContext`，成為純 intent producer | `Core/Movement` 白名單加 `Project.Core.Survivability`，`AIMovementSource` 直接讀目標的 `CharacterHealth` |
| **Ownership** | 不變；但「目標有效」只有 `AIInputSource` 知道 ⇒ 兩顆 AI 元件對同一目標的判斷**不一致**（交戰中、卻永不出手） | ②a：`AIInputSource` 成為「目標有效性」的來源——攻擊決策元件兼任 combat 事實發布者；②b：`CharacterHealth` 仍是唯一真相 | 每個概念一個 owner：combat producer＝目標／交戰；`AIMovementSource`＝移動意圖；`CharacterHealth`＝存活 | 不變（`AIMovementSource` 兩個角色照舊） |
| **呼叫順序** | 不變 | 不變（②a 順序 1 → 2.5 同帧） | **可不改管線順序**：AI combat producer 用既有 2.6 槽（與玩家相同）；`AIMovementSource` 在 2.5 讀**上一帧**的 `CombatContext`——它今天本來就讀上一帧的 `InCombat` 作為 `wasInCombat`。代價是交戰進出與 `TargetPosition` 對移動慢一帧 | 不變 |
| **依賴方向** | 不變 | ②a：Movement→Blackboard（合法），但 `InputData` 是**原始裝置輸入**——ADR-003 §6.3 已否決把 gameplay 修飾塞進 `InputData`；②b：新增一支只為繞過分層而存在的介面（**單一實作者**），且 Survivability→Movement/Blackboard 的實作方向不自然 | Combat→Survivability（已允許）；Movement→Blackboard（已允許）；**不新增任何跨層邊** | **新增 Movement→Survivability 邊**；ADR-003 D2 的例外變成兩條（`Core.Effects` 已是登記中的 debt） |
| **資料同步風險** | 高（兩顆元件意見不同，且是常態可達：玩家死後） | ②a：`AIInputSource` 與 `AIMovementSource` 的 target 必須相同（W15 已守 prefab 層）；②b：低 | target 從 `AIMovementSource` **搬到**新 producer（單一 owner）；`AIInputSource` 的第二份 target 仍由 W15 約束（比照現況，改比對新元件）；**Y Bot prefab 與 `SampleScene` 的 `target` override 要遷移**——⚠️ `SampleScene.unity` 目前有使用者未 commit 的改動，必須先協調 | 低 |
| **治理** | 無 | ②a 違反 ADR-003 §6.3 的否決理由；②b 違反「第二個使用者出現前不得建立 production abstraction」 | WriterRules（A5）：`CombatContext` 寫入者由 `AIMovementSource.cs` 換成新檔；`docs/19` §3.1 依其自訂條件重拆；Runner 要排程 AI 的 combat producer——兩個實作者共用一支 Tick 介面時，第二個使用者已存在 | A4 不變量變更 |
| **測試成本** | 零 | ②a 低；②b 低 | **高**：`CombatContextTests` 中 AI 的 combat 測試（TC9–TC11 等）搬家；W15／W12 類 wiring 測試更新；prefab／scene 遷移（Editor API）；但 Q4 的激怒規則可直接寫在新 producer 的純函數裡 | 低 |
| **能否滿足 Q4「目標死亡 ⇒ 立即解除交戰」** | ❌ | ✅ | ✅ | ✅ |

**需不需要新增抽象或改變分層？**
- ① 不需要，但留下常態可達的不一致，且 Q4 的「目標死亡 ⇒ 解除」無法成立。
- ② 需要：要嘛誤用原始輸入契約（②a），要嘛為了繞過白名單新增單一實作者的介面（②b）——兩者都是用新結構換掉一條白名單，本提案**不推薦**。
- ③ **不改分層、不新增跨層邊**；新增的是一個與玩家並列的 producer（比照既有形狀，不是新概念），Runner 的排程需要能容納兩種 producer。
- ④ 不新增抽象，但**改變分層規則**。

**評估（未裁決）**：若接受成本，③ 是唯一同時「不新增跨層依賴、不新增只為繞路的抽象、且符合原裁決重拆條件」的方案，並讓 Q4 的激怒／寬限有自然的家；⚠️ 前提是 `SampleScene` 的協調。若暫不承擔成本，① 是唯一不動分層的選項，代價是玩家死後敵人仍繞行／面向屍體、Q4 的「目標死亡 ⇒ 解除」延後。

### 3.4 Q4 — 受擊交戰：觸發原因與追擊／脫戰政策分開（第三版；第四版：Q4-G＝(i) 已裁決）

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
  · d > R 且受過傷                 ⇒ 開始寬限 G；寬限內 d ≤ R 或再受擊 ⇒ 取消
  · d > 12 且從未受傷             ⇒ 立即結束（現行規則，Q4-G＝(i)）
  · 寬限期滿                      ⇒ 結束；_damagedThisEpisode 清除
```

**與 8／12 m 的互動**

| 情境 | 今天 | 第三版 |
|---|---|---|
| 從未受擊，走近 ≤ 8 m、之後玩家退到 > 12 m | 立即離開 | **立即離開**（Q4-G＝(i)：未受擊時逐位元保留；寬限 G 只屬 Provoked 來源） |
| 7 m 被打，之後玩家退到 13 m | 立即離開 | **繼續交戰**（受過傷 ⇒ R＝L＝20 m） |
| 15 m 被打 | 無反應 | 進入；R＝20 m ⇒ 有效；Approach |
| 25 m 被打（> L） | 無反應 | 進入；但 d > L ⇒ 立即開始寬限 G；寬限內追進 20 m 就繼續，否則離開 |
| 交戰中玩家跑出 L | — | 寬限 G；G 內回來或再被打 ⇒ 繼續 |
| 玩家死亡 | 繼續交戰 | 立即離開（Q3；**依賴 §3.3.1**） |
| 敵人自己死亡 | 不寫 `CombatContext` | 不變 |

**數值**：L＝**20 m**、G＝**5 s**——**第一輪量測候選，不是平衡定值**；8／12 m 不動。
**實作位置依 B 方案**：§3.3.1 選 ③ 時，Provoked 狀態與寬限住在新的 AI combat producer；選 ① 時住在 `AIMovementSource`，但「目標死亡 ⇒ 立即解除」無法成立。⛔ 不建立通用仇恨系統（只有一個布林與一個時間點，不記錄多目標、不累積數值）。
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

#### 3.5.6 Q5 最小 typed movement-limit contract（第四版；方向＝Option A，介面未批准）

**原則（使用者裁決）**：Gameplay 實際移動速度限制的**啟用權**屬 Action／Movement 的合法契約；Bake 只提供**數值**；不由 Presentation 播放狀態反推；X Bot 施法限速與 Y Bot 戰鬥轉向限速**各自的啟用條件**；⛔ 通用 Action Constraint Framework；⛔ 預設新增黑板欄位。

**契約（三件，皆型別化）**

| # | 契約 | 形狀 | Owner | 說明 |
|---|---|---|---|---|
| ① | **Action 的宣告** | `ActionPhaseEntry` 新增一個 enum 欄位，暫名 `ActionMovementLimit { None = 0, DirectionalSpeed = 1 }`；預設 `None` ＝現行行為 | Action（authored 資料） | 只回答「**這個 phase 期間，基底移動受 actor 的方向限速約束**」。⛔ 不含任何數值、角度、倍率；⛔ 不可組合（只有一個非 None 成員） |
| ② | **Action → Movement 的通道** | `Core/Movement` 新增一支**可選能力介面**（暫名 `IActionMovementLimitTarget`，單一方法 `SetActionMovementLimit(ActionMovementLimit)`），`LocomotionModel` 實作；`ActionState` 以 `MovementModel as IActionMovementLimitTarget` 取得，null ⇒ 安全退化 | Movement model（消費者） | **不修改 `IMovementModel`**。形狀逐字比照既有先例 `IFootPhaseSource`（`IMovementModel.cs` 內的可選能力介面，`JumpState` 以 `MovementModel as IFootPhaseSource` 取得） |
| ③ | **Actor 的啟用政策** | `LocomotionModel` 新增一個序列化 enum，暫名 `DirectionalSpeedLimitActivation { WhileInCombat = 0, WhileActionDeclares = 1 }`；預設 `WhileInCombat` ＝現行行為 | Movement model（actor policy） | Y Bot 保持 `WhileInCombat`（持續面向目標造成的方向分離）；X Bot 設為 `WhileActionDeclares`（施法）。**兩個條件不合併**。數值仍來自 `CombatDirectionalSpeedProfileSO`（Bake）——profile **不**持有啟用政策 |

**每帧時序**

```text
順序 3   LocomotionModel.Tick：啟用 =（activation == WhileInCombat）? data.CombatContext.InCombat
                                      : _actionLimit == DirectionalSpeed
         → ApplyCombatDirectionalSpeedLimit（數值＝profile 的 Bake 推導，與今天相同）
順序 4   FSM：ActionState.OnEnter／EnterPhase／TryAdvanceChain ⇒ SetActionMovementLimit(該 entry 的宣告)
              ActionState.OnExit／Complete ⇒ SetActionMovementLimit(None)
順序 6   ActionState.OnUpdateMotion（無 Bake）⇒ ExecuteBaseMovement 消費順序 3 的輸出
```
⚠️ **一帧延遲**：宣告在順序 4 設定、model 在下一帧順序 3 才讀 ⇒ 進入施法的第一帧不受限、結束後多受限一帧。今天的 `InCombat` 路徑在出手帧（2.6 → 3）就生效，所以這是**行為差異**；速度經 smoother 平滑，預期不可見——列入量測（AC-3a.6）。

**相容性檢查**

| 既有契約 | 判定 | 理由 |
|---|---|---|
| ADR-003 D2（producer context-free） | ✅ | producer 不參與；限速發生在 model |
| ADR-003 D3（Model via State；Action 屬 intrinsic-motion） | ✅ | 施法期間的位移擁有者本來就是 Action（`OnUpdateMotion` 借用 base movement）；由 Action 宣告限制是行使它的擁有權 |
| ADR-003 D4（MoveSpeed dynamics 歸 model） | ✅ | 限制在 model 的 smoother 之前套用，與今天同一位置；`MoveSpeed`／`MoveX`／`MoveZ` 唯一寫入者不變（A5） |
| `IMovementModel`（CLAUDE.md 判準③ 的核心驅動介面） | ✅ **不修改** | 走可選能力介面（`IFootPhaseSource` 先例） |
| ADR-004 D2／D4（Action lifecycle 單一權威；位移只經 `MotionDriver`） | ✅ | 通知點就是既有的 lifecycle 節點 |
| ADR-006 D1（State／Definition 不認識 Layer） | ✅ | 宣告的是 gameplay 移動限制，不是 layer；layer 仍由 mapping 決定 |
| ADR-006 §5「⛔ 不新增 MovementLock／AttackMovementPolicy」、`docs/21` §1.3.1 同語 | ⚠️ **需窄化修訂**（ADR-006 為 Trial，可修訂） | 本契約是「單一型別化成員、無數值、只啟用既有 actor 限速」，不是攻擊移動政策框架；修訂文字要把這個邊界寫死 |
| A4 分層 | ✅ | `Core/StateMachine` → `Project.Core.Movement` 已在白名單（`IMovementModel` 同一條）；`Core/Movement/Models` 不需要任何新命名空間 |
| A36（`ActionState` 不擁有 layering） | ✅ | 不碰 Layer／Mask |
| 黑板 | ✅ | 不新增欄位 |
| Bake 不取得啟用權 | ✅ | profile 只算數值；啟用由 ①（Action）或 ③（actor policy）決定 |

**是否需要新增抽象或改變分層？**
- **分層：不改。** 依賴方向是既有的 `Core/StateMachine → Core/Movement`。
- **抽象：需要一支窄介面（②）。** 理由不是「為了未來」：State 只持有 `IMovementModel`，要在不修改核心驅動介面、也不讓 State 認識具體 `LocomotionModel` 的前提下傳一個宣告，只能走可選能力介面——本 repo 已有同形先例（`IFootPhaseSource`）。如果你寧可修改 `IMovementModel` 本身（判準③，ADR-003 已 Accepted ⇒ 需 Trial ADR 記錄），② 可以省掉，但契約影響更大。
- ①、③ 是資料欄位，不是抽象。

**行為後果（WP-3a 落地時）**：X Bot 交戰中**未施法**的轉向不再被限速（1D 播放；`docs/11` §13 接受的副作用消失）；X Bot **非交戰施法**仍受限（今天依賴「出手即交戰」才成立）；Y Bot 逐位元不變。

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

### 4.3 新增測試的覆蓋審查（第四版：是否真的走 runtime 路徑）

| 測試 | 驅動的對象 | 走 runtime 路徑？ | 沒有覆蓋到的 |
|---|---|---|---|
| `TD5`（改名） | 真的 `ActionState`＋`AimResolver` rig | ✅ | — |
| `TD5B_a` | 真的 `ActionState`（`OnEnter`／`OnTick`／`TryGetFacingCommitment`）；只有 `IAimSource` 是假件 | ✅ | 真的 `AimResolver` 射線（由 `TD5` 涵蓋） |
| `TD5B_b` | 同上，release 在 0.5（非 t=0），前搖中清掉候選 | ✅ 走 `RefreshEffectContext`→`TryEmitRelease`→`NotifyRelease` | ✅ mutation 已確認：不刷新效果落點時本測試轉紅 |
| `HD3B` | 真的 `FullBodyStateMachine`＋`HurtState`，config 由測試建構且**不含** Hurt→Hurt | ✅ 走 `EvaluateInterrupts` 的同型重入分支與 `HurtState` 計時 | 出貨 config（A38 已改為不斷言該項） |
| `CA11` | **讀原始碼文字**（`AudioController.cs`） | ❌ **結構性守門，不是行為測試** | EditMode 沒有 AudioSource 池（`PlayAt` 在 `_sources == null` 時直接返回）⇒ 「翻滾中被打會出聲」的**行為**只能人工聆聽（M5） |
| `H14` | 真的 `CharacterHealth`（`ApplyDamage`／`Revive`）＋謂詞 | ✅ | — |
| `AIInputSourceTests.DeadTarget_IsNotAttacked_UntilRevived` | 真的 `AIInputSource.WantsToAttack` 與 `FetchRawInputAtTimeForTests`（僅注入時間、呼叫同一個私有 `FetchRawInput`） | ✅ | 管線順序 1 的排程（由既有 A27／整合路徑涵蓋） |
| `T24B` | 真的 `MeleeHitboxSink.Release`／`TryRequestHit`（`OnTriggerEnter` 唯一呼叫的方法） | ⚠️ **部分**：有效命中的判定與傷害結算走 runtime；**命中音本身觀察不到**（同 `CA11` 的原因），只由「回傳值與播放在同一分支」間接保證 | 物理 trigger 事件；聽感 |
| `GroundEffect_DoesNotSlowDeadTarget_ButSlowsUntrackedTarget` | 真的 `GroundEffectSink.ApplyToRoot`（`ApplyToTargetsAround` 對每個 overlap 呼叫的方法） | ✅ | Physics overlap 本身（由既有 `IceHitVolume_*` 涵蓋） |
| 既有 `TC_D1`–`TC_D3`、`ProjectileHit_*` | 玩家選目標與投射物改用同一謂詞後的回歸 | ✅ | — |

⇒ 兩條需要人工補位：`CA11`（受擊語音）與 `T24B`（命中音）的**聽得到與否**；其餘測試都驅動真實 runtime 元件。

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

### WP-0 文件／註解／測試 fold back（Q1、Q6、F-07）——**Implemented / Automated Verification Passed / Human Verification Pending**（`ec4bc09`）

| AC | 狀態 |
|---|---|
| AC-0.1 §5.1 全部改寫；「讀同一份」只剩標明已被取代的歷史段 | ✅ 文字已改（grep 可驗） |
| AC-0.2 `TD5B_a`／`TD5B_b` 綠；`TD5B_b` 在「effect 改回段落快照」時會紅 | ✅ Unity EditMode 通過；✅ mutation：把 `RefreshEffectContext` 改成不刷新後 `TD5B_b` 轉紅（run `20261012T082546Z`），還原後 `ActionStateTests` 32／32 綠（`20261012T082647Z`） |
| AC-0.3 HD3B 綠；A38 不再斷言 Hurt→Hurt | ✅ Unity EditMode 通過 |
| AC-0.4 CA11 綠 | ✅ Unity EditMode 通過（⚠️ 結構性守門，行為仍待 M5 聆聽，見 §4.3） |
| AC-0.5 runtime 只有註解／log 字串 diff；EditMode／PlayMode 全綠 | ✅ diff 已確認；EditMode 898／896（唯一失敗 `StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable` 來自使用者未 commit 的 `SampleScene` X Bot `GroundLayers=503` override，與本案無關）；PlayMode 107／106（唯一失敗為既有 `C0`） |

### WP-1 Q3 eligibility——**Partial / Automated Verification Passed / Human Verification Pending**（`a378271`）

| AC | 狀態 |
|---|---|
| AC-1.1 目標 `IsDead` ⇒ `AIMovementSource` 寫 `CombatContext = default` | ⏸ **未做**（§3.3.1，A4） |
| AC-1.2 目標 `IsDead` ⇒ `AIInputSource` 不送 `Slot1ButtonDown` | ✅ Unity EditMode 通過（`DeadTarget_IsNotAttacked_UntilRevived`） |
| AC-1.3 沒有 `CharacterHealth` 的目標與今天相同 | ✅ 同上測試的前半 |
| AC-1.4 `Revive` 後恢復 | ✅ 同上測試的後半 |
| AC-1.5 屍體：近戰不是有效命中（無命中音）、AoE 不 Slow；致死一擊有效；投射物不變 | 🟡 `T24B`、`GroundEffect_DoesNotSlowDeadTarget_ButSlowsUntrackedTarget`；既有投射物測試；✅ Unity EditMode 通過（命中音本身仍待人工） |
| AC-1.6 存活謂詞唯一定義；玩家／投射物改用後行為不變 | ✅ `H14`、既有 `TC_D1`–`TC_D3`／投射物測試，Unity EditMode 通過 |
| AC-1.7 A4／A27 綠 | ✅ Unity EditMode 通過 |
| AC-1.8（Play）玩家死後敵人不揮拳、無命中音 | ⬜ 人工；繞行／面向屍體要等 AC-1.1 |

### WP-1b `AIMovementSource` 的目標合法性——⏸ 等 §3.3.1 方案①–④

| AC | 驗證 |
|---|---|
| AC-1b.1 目標 `IsDead` ⇒ `CombatContext = default`、`MovementIntent = default`、Hold | EditMode |
| AC-1b.2 依所選方案：③ ⇒ `CombatContext` 寫入者（A5）換成新 producer、`AIMovementSource` 不再持有 `target`、A4 白名單**不變**；④ ⇒ 白名單只多一項且附理由 | EditMode |
| AC-1b.3（Play）玩家死後敵人停止繞行、不面向屍體、血條收起 | 人工 |

### WP-2 Q4 受擊交戰——✅ 規則已裁決（Q4-G＝(i)）；⏸ 落點依 B 方案（§3.4）

| AC | 驗證 |
|---|---|
| AC-2.1 從未受擊的交戰：全部既有 8／12 m 距離規則**逐位元不變**（Q4-G＝(i)） | EditMode |
| AC-2.2 **任何距離**受未致死傷害 ⇒ 當帧進入；之後 R＝L | EditMode（注入時間） |
| AC-2.3 一致性：7 m 受擊後退到 13 m 與 15 m 受擊，兩者的脫戰距離都是 L | EditMode |
| AC-2.4 d > R ⇒ 寬限 G；寬限內回來或再受擊 ⇒ 取消；期滿 ⇒ 離開並清除受傷標記 | EditMode |
| AC-2.5 目標死亡 ⇒ 立即離開、無寬限（依賴 WP-1b） | EditMode |
| AC-2.6（Play）10–20 m 外施法：Y Bot 轉身、接近、出拳；L＝20 是否合適 | probe M4＋人工 |

### WP-3a 限速啟用權改由 Action／actor policy 決定——🟡 等 §3.5.6 介面批准

| AC | 驗證 |
|---|---|
| AC-3a.1 限速不再讀 `CombatContext`／`InCombat` 作為 X Bot 的觸發 | EditMode（架構測試） |
| AC-3a.2 Y Bot 的觸發 policy＝交戰中，行為逐位元不變 | EditMode |
| AC-3a.3 X Bot 施法中 ⇒ 限速與今天相同；未施法（1D 在播）⇒ 不限速，不論是否交戰 | EditMode |
| AC-3a.4 宣告與 layered mapping 的關聯測試：有 Layer 0 companion 的 Spell phase 必須宣告 `DirectionalSpeed`；有 Bake 的 phase 必須為 `None`（Bake 路徑不消費 model 輸出） | EditMode（讀出貨資產） |
| AC-3a.5 ⛔ 沒有新增黑板欄位；`IMovementModel` 未修改；宣告 enum 只有 `None` 與一個成員（封閉清單測試） | EditMode |
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

## 7. 真正阻擋實作的事項（第四版）

| 阻擋 | 擋住哪裡 | 需要什麼 |
|---|---|---|
| ~~Unity Test Runner 尚未執行~~ ✅ 2026-10-12 已執行 | — | EditMode 898／896、PlayMode 107／106；兩個失敗皆與本案無關（場景 WIP、既有 `C0`）。WP-0／WP-1 仍待**人工**驗收項，不得標 Closed |
| **B 方案（§3.3.1 ①–④）** | WP-1b；WP-2 的「目標死亡 ⇒ 立即解除」與 Provoked 狀態的落點 | 你選一個方案。若選 ③，另需協調 `SampleScene.unity`（使用者有未 commit 改動，`target` override 要遷移） |
| **Q5 介面批准（§3.5.6）** | WP-3a → WP-3b | 批准三件型別化契約：①`ActionPhaseEntry` 的宣告 enum ②`IMovementModel` 旁的可選能力介面（不改 `IMovementModel`）③`LocomotionModel` 的啟用政策 enum；並同意窄化 ADR-006 §5 |
| **M2a–M2c** | WP-4 的 Punch 承諾時點 | Editor；未驗證前不固定數值 |

**不被阻擋、但依指示本輪未做**：WP-2 中與目標存活無關的部分（Provoked 來源、L／G、Q4-G＝(i)）——它們最終住在哪個元件取決於 B 方案，先做會在選 ③ 時搬家一次。

**明確不在本輪**：攻擊者身分 seam、陣營／友軍傷害、通用仇恨系統、`ActionRequestTarget` 改名、近戰連段 release-once。