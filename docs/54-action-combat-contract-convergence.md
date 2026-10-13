# 54 — Action／Combat 契約收斂：設計與影響範圍（2026-10-12，第五版）

| | |
|---|---|
| **狀態** | 🟠 設計與影響分析（第五版）＋ **實作紀錄**：WP-0、WP-1（部分）、🆕 WP-1b＋WP-2、🆕 WP-3a 已實作（各 WP 狀態見 §0.1／§6）。未批准的部分（WP-3b、WP-4）⛔ 仍不得作為實作基線 |
| **輸入** | `docs/51`（審查）＋ 使用者 2026-10-12 暫定裁決（第一輪）＋ 第二輪回覆（§0.1） |
| **路由** | 全部落在既有 Trial ADR（ADR-006／007／009）可修訂範圍或 Living Spec；**不需要新 ADR**。唯一會碰核心驅動介面的是 §3.5 選項 R2（`AnimationFacadeBase` 加一個唯讀查詢），走 ADR-006 Trial 修訂記錄 |
| **閱讀方式** | §0.1 裁決紀錄 → §1 衝突表 → §2 共享 vs 差異 → §3 逐項變更 → §4 F-01／F-07 專審 → §5 文件 vs runtime 分離 → §6 最小方案與 AC → §7 真正阻擋實作的事項 |

> **對 `docs/51` 的一處更正（第一版已記）**：`Bake_Fists_Punch_R` 的 `SpeedCurve` 全為 0（0–0.8 s，總位移 0 m）⇒ 敵人出拳**沒有前衝**、原地不動，只有**轉身追蹤**；後果是 root 子物件 `MeleeHitbox`（盒 0.9×1.2×1.2、中心在身前 0.9 m）在命中窗 0.32→0.8 s 內跟著轉。`docs/51` 已加更正框（`c7749fd`）。

---

## 0.1 裁決紀錄與實作狀態（第五版，2026-10-12）

| # | 狀態 | 內容 |
|---|---|---|
| **WP-0** | **Automated Verification Passed / Human Verification Pending**（`ec4bc09`） | EditMode／PlayMode 中本案測試全部通過；`TD5B_b` mutation 已確認。仍待人工：AC B 的 Play 重驗、受擊語音聆聽。⚠️ `CA11` 只是結構性守門，**不代表已驗證實際聲音**。兩個無關失敗另記於 §8。⛔ 不得標 Closed |
| **WP-1** | **Automated Verification Passed / Human Verification Pending**（`a378271`＋WP-1b） | 已實作部分（存活謂詞、`AIInputSource`、近戰有效命中、AoE Slow、投射物／玩家同一謂詞）的測試全部通過。~~AI Movement 的死亡目標處理尚未完成~~ → 🆕 已由 WP-1b 完成（AC-1.1 ✅）。⚠️ `T24B` 無法觀察命中音，**不代表已驗證實際聲音**。⛔ 不得標 Closed |
| **B1／B2** | ✅ **已實作（WP-1b＋WP-2）——Automated Verification Passed / Human Verification Pending** | 單一 target 權威（T1：沿用 `AIMovementSource.target`）；管線 combat context 步驟移到 2.4 以消除一帧不一致；只改 `Y Bot.prefab`、⛔ 不碰 `SampleScene.unity`。不新增 target 管理元件、不新增介面 |
| **Q1a** | ✅ 已裁決（WP-0） | — |
| **Q2a** | 🔬 待量測（M2a–M2c） | 不固定 Punch 承諾時點 |
| **Q2b／Q2c** | ✅ 已裁決 | 屬 WP-4，未實作 |
| **Q3a／Q3b** | ✅ 已裁決；✅ 已全部實作 | `AIMovementSource` 部分由 WP-1b 完成：目標死亡當帧解除交戰、移動意圖同帧歸零 |
| **Q4** | ✅ 維持既有裁決 | 未受擊保留 8／12 m；有效敵對傷害 ⇒ Provoked；20 m／5 s 為量測候選；目標死亡 ⇒ 立即解除；⛔ 通用仇恨系統。✅ 已實作於 `AICombatContextSource`（AC-B.5–B.8）；L／G 仍是量測候選值 |
| **Q5** | ✅ **WP-3a 已實作——Automated Verification Passed / Human Verification Pending** | 首帧延遲：沿用既有生命週期**無法消除**（三個消除方案皆違反 Accepted 契約），界定為一帧；殘留：撤銷收斂於 `ResetExecutionState`＋每帧續約 ⇒ 缺少宣告自動回 `None` |
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
| **Q4** | ADR-007 D3 補充條款（唯一 producer） | ✅ 唯一 producer 不變；🔄 2026-10-12 起敵人的 producer 是 `AICombatContextSource`（方案③） | — |
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

#### 3.3.2 方案③ 的三項設計確認（第五版，2026-10-12；使用者原則選③，⛔ 尚未實作）

**① Target 的唯一權威**

今天有**兩份**序列化 target：`AIMovementSource.target`、`AIInputSource.target`（W15 只在 prefab 層約束兩者相同；場景各有一筆 override，都指向 `1472398490`）。

| 做法 | 權威數 | 新依賴 | 場景是否要改 | 評估 |
|---|---|---|---|---|
| **T1（推薦）沿用既有參照**：`AIMovementSource.target` 留在原處，成為**唯一**權威；新增唯讀屬性 `Target`。`AICombatContextSource` 與 `AIInputSource` 都讀它；`AIInputSource` 的序列化 `target` 移除 | **1** | `Core/Combat → Core/Movement`（讀一個具體元件的唯讀屬性；A4 沒有 `Core/Combat` 規則，`Core/Pipeline` 白名單已含 `Project.Core.Movement`）；**不新增介面** | ❌ 不用改（`AIMovementSource.target` 的 override 原樣保留） | 唯一不必動場景的做法 |
| T2 把 target 搬到 `AICombatContextSource` | 1 | `AIMovementSource` 只讀 `CombatContext`（無新依賴） | ✅ 要把場景 override 搬到新元件 ⇒ 撞使用者未 commit 的場景 | 語意更乾淨；等使用者的場景 commit 後才適合 |
| T3 兩個元件各自持有 | 2–3 | — | — | ⛔ 違反本項要求 |

⇒ **需不需要新增 Target 管理元件或跨層介面？不需要。** T1 只新增一條 Core 內部的具體依賴（Combat 讀 Movement 的一個唯讀屬性）與一個屬性；`AICombatContextSource` 本身就是③核准的 producer，不是另一個 target 管理者。Runner 以**第二個具體 optional 欄位**排程它（與 `PlayerCombatContextSource` 並列），⛔ 不新增 `ICombatContextSource` 介面。

**② 更新順序與一帧延遲**

`AIMovementSource`（2.5）若繼續在 `AICombatContextSource`（2.6）之前執行，就只能讀**上一帧**的 `CombatContext`：

| 事件（發生在帧 N） | `CombatContext` 何時反映 | 移動意圖何時反映 | 判定 |
|---|---|---|---|
| 進入（≤ 8 m） | N | N+1 | 可接受（只是晚一帧開始走） |
| 受擊 → Provoked | N（0.5 已發布 `JustTookDamage`） | N+1 | 可接受（同帧進 Hurt，硬直 0.7 s 期間本來就不位移） |
| 自己死亡 | — | N（`AIMovementSource` 讀**自己的** `Survivability`，同帧） | 不變 |
| **脫戰（> 12 m 或寬限期滿）** | N（`!InCombat`） | **N+1** | ⛔ **違反** `docs/21` §1.1「`!InCombat` 時整體清空 `MovementIntent`」——帧 N 已發布不在交戰、卻仍有移動意圖 |
| **目標死亡** | N | **N+1** | ⛔ **違反** Q4「目標死亡時立即解除交戰」（移動仍朝屍體多走一帧） |
| `TargetPosition` 給導航 | — | 晚一帧 | 可接受（≤ 0.08 m） |

**消除方法：把 combat context 步驟移到 movement intent 之前**（兩種角色一起）：

```text
現在：2 Intent → 2.5 MovementIntent producer → 2.6 CombatContext producer → 3 Model → 4 FSM → 4.6 Facing
改為：2 Intent → 2.4 CombatContext producer → 2.5 MovementIntent producer → 3 Model → 4 FSM → 4.6 Facing
```
- 玩家**行為不變**：`PlayerCombatContextSource` 的輸入（順序 2 的 Intent、0.5 的 Survivability、相機）都在 2.4 之前就緒；2.4–2.6 之間沒有任何讀者（`PlayerLocomotionPolicy` 不讀 `CombatContext`）；`LocomotionModel`（3）、`ActionState`（4）、facing（4.6）讀到的值與今天相同。`docs/15` O2「Intent 之後、FSM 之前」仍成立。
- AI：所有事件的 `CombatContext` 與移動意圖**同帧一致**，上表兩個違反消失。
- ⚠️ 這是**管線順序**變更（CLAUDE.md 判準③）⇒ 以 ADR-007（Trial）修訂記錄、同步 dev-spec §2 與 Runner 註解；不需要新 ADR。

**③ Y Bot prefab 與 SampleScene 的序列化遷移**

| 資產 | 是否有使用者未 commit 改動 | 要做的事 | 方式 |
|---|---|---|---|
| `Assets/Prefabs/Y Bot.prefab` | 否 | 加 `AICombatContextSource`；把 `aggroEnterRadius 8`／`aggroLeaveRadius 12` 由 `AIMovementSource` 搬過去；設 Provoked 候選 L＝20、G＝5；`AIInputSource` 的 `target` 欄位因類別移除而在存檔時自然消失 | `PrefabUtility.LoadPrefabContents` → 改 → `SaveAsPrefabAsset`；改前印出現值；還原路徑＝git |
| `Assets/Scenes/SampleScene.unity` | **是**（含 X Bot `GroundLayers=503` 等） | **不改、不存** | 新元件經 prefab 繼承自動出現在場景實例上；`AIMovementSource.target` 的 override 原樣有效（T1）；`AIInputSource.target` 的 override 變成指向已不存在欄位的**孤兒 override**——Unity 讀取時忽略，不影響行為，留待使用者日後存場景時處理。⛔ 不呼叫 `SaveScene`、⛔ 不清孤兒 override |

驗證：一條讀出貨場景的 EditMode 測試（比照 `StairAuthoringContractTests` 的場景讀取），斷言場景 Y Bot 實例上 `AICombatContextSource` 經 `AIMovementSource.Target` 拿到的是場景裡的 X Bot。

⚠️ **實作時的協調點**：Runner 排程要改 `CharacterPipelineRunner.cs`，而該檔目前有**使用者未 commit 的改動**——commit 時只能把我的 hunk 寫進 index（不得連帶 commit 使用者的改動），動手前先重讀磁碟現況。

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

#### 3.5.7 Q5 的時序、撤銷與 stale 防護（第五版；三件契約方向與 ADR-006 窄化已原則批准，⛔ 尚未批准實作）

**① 首帧延遲：能不能消除？**

| 候選 | 結果 | 判定 |
|---|---|---|
| Z1：model 在順序 3 讀 `Intent.RequestedActionSlot` 預先套用 | 請求 ≠ 被接受（冷卻／`RequiresGrounded` 會拒絕）；Movement 還得去查 Action Definition | ⛔ 違反 ADR-004 D2（能不能出手只有 `ActionState` 回答） |
| Z2：把 FSM 移到 model 之前 | `IdleState`／`MoveState.CanEnter` 讀 `IsProducingMotion`（順序 3 的結果） | ⛔ 違反 ADR-003 D3 的既有順序 |
| Z3：`ActionState` 在順序 6 自己限制位移 | `MoveSpeed`／`MoveX`／`MoveZ` 的唯一寫入者是 model（A5）；只限位移不限參數 ⇒ 動畫與位移分岔 | ⛔ |
| **Z4：接受一帧** | 進入帧 N：model（3）已跑完 ⇒ N 不受限；N+1 起受限 | ✅ **唯一不破壞既有契約的選項** |

⇒ **沿用既有生命週期，首帧延遲無法消除，只能界定。** 量級：限速只改變 smoother 的目標值，`moveSpeedAccelTime 0.12`／`moveSpeedDecelTime 0.18` 的 SmoothDamp 讓「開始減速」整條曲線晚一帧（60 fps ≈ 16.7 ms）；同帧 Layer 0 companion 正在 cross-fade 進場。對照：今天 X Bot 非首次施法時 cap 因 `InCombat` 早已持續生效，**第一次**施法則在請求帧（2.6 → 3）就生效——Z4 對「第一次施法」晚一帧、對「交戰中連續施法」行為改為逐次啟用。以 AC-3a.7 量測。

**② 撤銷規則（Hurt／Death／Roll／Action 結束／同帧切換）**

全部走既有的單一收斂點：

| 結束路徑 | 既有呼叫鏈 | 撤銷點 |
|---|---|---|
| 自然結束 | `OnTick` → `Complete()` → `ResetExecutionState()` | ✅ `ResetExecutionState` |
| 被 Hurt／Death／Roll／Jump 打斷 | `FullBodyStateMachine.TransitionTo` → `OnExit` → `ResetExecutionState()` | ✅ 同上 |
| Cancel phase | `EnterPhase(Cancel)` | 依 Cancel entry 自己的宣告（預設 `None`） |
| 連段換段 | `TryAdvanceChain` | 依新段 entry 的宣告 |
| Action → Action 重入（不同 slot） | `TransitionTo(self)`：`OnExit`（撤銷）→ `OnEnter`（新宣告），同一個 Tick 內依序執行 | 最終值＝新 Action 的宣告 ✅ |
| 同帧內 `OnTick` 先宣告、隨後被打斷 | `OnTick`（宣告）→ `EvaluateInterrupts` → `OnExit`（撤銷） | 最終值＝`None` ✅（後寫者贏，順序由 FSM 保證） |

Hurt／Death／Roll 本身**不宣告**——它們的位移走 `ExecuteVerticalOnlyMovement`／Bake，不消費 model 輸出。
⇒ 撤銷生效時點：被打斷的帧 N 已執行完的 model 輸出不變；N+1 的順序 3 讀到 `None`。

**③ Stale 防護與「缺少宣告 ⇒ 自動回到 None」**

| | S1 只靠生命週期撤銷 | **S2 每帧續約 ＋ 生命週期撤銷（推薦）** |
|---|---|---|
| 形狀 | `EnterPhase`／`TryAdvanceChain` 設定；`ResetExecutionState` 清除 | 同 S1，另外 `ActionState.OnTick` 每帧以 `Time.frameCount` 重新宣告；model 只承認「上一帧或本帧」的宣告，否則視為 `None` |
| 缺少宣告時 | ❌ 不會自動回 `None`（若某條結束路徑沒走到 `ResetExecutionState`，限制會一直留著） | ✅ 自動回 `None`（最多晚一帧） |
| 既有先例 | Action lifecycle 本身 | `MotionDriver.RequestFacing`：「提交**僅限當帧**的請求」，以 `_facingRequestFrame = Time.frameCount` 判斷有效 |
| 成本 | 每次 phase 變化一次呼叫 | 每帧一次介面呼叫＋一個整數比較；零 GC |
| ⚠️ 細節 | — | `ActionState.OnTick` 在 `deltaTime <= 0`（暫停）時提早 return；續約必須寫在該 return **之前**，否則解除暫停後的第一帧會短暫失去限制 |

**仍然不是框架**：model 端只有一個 `ActionMovementLimit` 值與一個帧號；`ActionState` 端只轉送當前 entry 的宣告。沒有堆疊、沒有優先序、沒有多來源合併。

**結論**
- **首帧延遲**：沿用既有生命週期**無法消除**（三個消除方案都違反 Accepted 契約）；界定為「進入晚一帧」並量測。
- **中斷後殘留**：**可以完全避免**——撤銷收斂在既有的 `ResetExecutionState`（所有結束路徑都經過），再以每帧續約保證「沒有宣告 ⇒ 下一帧自動 `None`」。

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

### WP-0 文件／註解／測試 fold back（Q1、Q6、F-07）——**Automated Verification Passed / Human Verification Pending**（`ec4bc09`）

| AC | 狀態 |
|---|---|
| AC-0.1 §5.1 全部改寫；「讀同一份」只剩標明已被取代的歷史段 | ✅ 文字已改（grep 可驗） |
| AC-0.2 `TD5B_a`／`TD5B_b` 綠；`TD5B_b` 在「effect 改回段落快照」時會紅 | ✅ Unity EditMode 通過；✅ mutation：把 `RefreshEffectContext` 改成不刷新後 `TD5B_b` 轉紅（run `20261012T082546Z`），還原後 `ActionStateTests` 32／32 綠（`20261012T082647Z`） |
| AC-0.3 HD3B 綠；A38 不再斷言 Hurt→Hurt | ✅ Unity EditMode 通過 |
| AC-0.4 CA11 綠 | ✅ Unity EditMode 通過（⚠️ 結構性守門，行為仍待 M5 聆聽，見 §4.3） |
| AC-0.5 runtime 只有註解／log 字串 diff；EditMode／PlayMode 全綠 | ✅ diff 已確認；EditMode 898／896（唯一失敗 `StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable` 來自使用者未 commit 的 `SampleScene` X Bot `GroundLayers=503` override，與本案無關）；PlayMode 107／106（唯一失敗為既有 `C0`） |

### WP-1 Q3 eligibility——**Partial**（`a378271`）：新增測試已通過；AI Movement 的死亡目標處理未完成

| AC | 狀態 |
|---|---|
| AC-1.1 目標 `IsDead` ⇒ `CombatContext = default`（且移動意圖歸零） | ✅ 由方案③完成（AC-B.8；寫入者改為 `AICombatContextSource`） |
| AC-1.2 目標 `IsDead` ⇒ `AIInputSource` 不送 `Slot1ButtonDown` | ✅ Unity EditMode 通過（`DeadTarget_IsNotAttacked_UntilRevived`） |
| AC-1.3 沒有 `CharacterHealth` 的目標與今天相同 | ✅ 同上測試的前半 |
| AC-1.4 `Revive` 後恢復 | ✅ 同上測試的後半 |
| AC-1.5 屍體：近戰不是有效命中（無命中音）、AoE 不 Slow；致死一擊有效；投射物不變 | 🟡 `T24B`、`GroundEffect_DoesNotSlowDeadTarget_ButSlowsUntrackedTarget`；既有投射物測試；✅ Unity EditMode 通過（命中音本身仍待人工） |
| AC-1.6 存活謂詞唯一定義；玩家／投射物改用後行為不變 | ✅ `H14`、既有 `TC_D1`–`TC_D3`／投射物測試，Unity EditMode 通過 |
| AC-1.7 A4／A27 綠 | ✅ Unity EditMode 通過 |
| AC-1.8（Play）玩家死後敵人不揮拳、無命中音 | ⬜ 人工；繞行／面向屍體要等 AC-1.1 |

### WP-1b＋WP-2 方案③：`AICombatContextSource`（含 Q3 的 AI 目標有效性與 Q4 Provoked）——**Automated Verification Passed / Human Verification Pending**（2026-10-12 使用者批准後實作）

> **實作決定：Target 怎麼傳（使用者要求權衡後記錄）**
> | 候選 | 結果 |
> |---|---|
> | Runner 傳遞 | ❌ Runner 只以 `IMovementIntentSource` 持有 movement source；要傳 target 就得在 Runner 轉型成具體 `AIMovementSource`（違背 Runner「只認介面」，ADR-003 D2／A9 精神），或新增 `ITargetProvider` 類介面——為了消除一個唯讀欄位讀取而新增抽象，正是使用者明確排除的方向 |
> | 黑板傳遞 | ❌ 黑板只放值型別（`CombatContextData` 不放 `Transform`，`docs/15` §4.4）；且 producer 本身就是黑板的上游 |
> | **既有資料來源：`AIMovementSource.Target` 唯讀屬性（採用）** | ✅ 場景 override 原本就掛在這個欄位上（不必改場景）；`AICombatContextSource`／`AIInputSource` 讀它是**參照資料依賴**——讀一個序列化引用，不呼叫對方任何決策邏輯。反方向不存在：`AIMovementSource` 只經黑板讀 `CombatContext`，A54 機器化禁止它引用 `AICombatContextSource`／`Project.Core.Combat`／`CharacterHealth`；A4 的 `Core/Movement` 白名單本來就不含 `Project.Core.Combat` |
> 代價（登記）：`AIMovementSource` 保管一個自己不再用於決策的序列化欄位。若日後使用者存場景、想讓語意更乾淨，可改走 §3.3.2-① 的 T2（把 target 搬到 `AICombatContextSource`，需改場景 override）。

**最小方案**（T1 ＋ 管線 2.4）：
1. 新增 `Core/Combat/AICombatContextSource`：讀 `AIMovementSource.Target`（唯一權威）、自己的 `Survivability`（自我死亡 ⇒ 不寫，沿用 `docs/19` §3.1 的既有裁決）、自己的 `JustTookDamage`、目標的 `CharacterHealth.IsAliveOrUntracked`；寫 `CombatContext`。距離規則（8／12 m）與 Provoked（L、G）都在這裡，以純函數表達。
2. `AIMovementSource`：不再寫 `CombatContext`、不再持有 aggro 半徑；改讀**同帧**的 `CombatContext`（`InCombat`／`TargetPosition`）決定移動；保留站位帶、側移、Recover、Slow。新增唯讀 `Target`。
3. `AIInputSource`：移除序列化 `target`，改讀 `AIMovementSource.Target`；保留 WP-1 的存活判斷（同一謂詞、同一目標）。
4. Runner：combat context 步驟由 2.6 移到 2.4（movement intent 之前），以第二個具體 optional 欄位排程 `AICombatContextSource`。
5. A5 `WriterRules`：`CombatContext` 寫入者 `AIMovementSource.cs` → `AICombatContextSource.cs`；A4 白名單**不變**。
6. 資產：只改 `Y Bot.prefab`（§3.3.2-③）；⛔ 不碰 `SampleScene.unity`。

| AC | 驗證 |
|---|---|
| AC-B.1 全專案只有一個序列化的 AI target（`AIMovementSource.target`）；`AIInputSource` 不再有 `target` 欄位 | ✅ `AICombatContextSourceTests.AB1_OnlyAIMovementSourceSerializesATarget`（反射）＋ `PrefabWiringTests.W15`（prefab 上無殘留 `target` 屬性） |
| AC-B.2 出貨場景的 Y Bot 實例：`AICombatContextSource` 經 `AIMovementSource.Target` 得到場景裡的 X Bot（場景以唯讀方式載入） | ✅ EditMode `AICombatContextSceneTests.AB2_*`（唯讀開啟磁碟場景）＋ PlayMode `AICombatContextPlayModeTests.YBot_InShippedScene_*`（執行期解析、`CombatContext.TargetPosition` 落在 X Bot） |
| AC-B.3 `CombatContext` 的唯一 AI 寫入者是 `AICombatContextSource.cs`（A5）；`Core/Movement` 白名單不變（A4） | ✅ A5、A4；🆕 `PrefabWiringTests.W27`（AI 角色恰好一顆 `AICombatContextSource`、無玩家 producer） |
| AC-B.4 管線順序：combat context 步驟在 movement intent 之前；玩家既有 TC 系列逐位元不變 | ✅ 🆕 A54（排程順序＋單向依賴）；`CombatContextTests` 玩家 TC 全綠。🧪 **mutation**：把 AI 的 Tick 排回 `ProduceIntent` 之後 ⇒ PlayMode `YBot_StopsChasingOnTheFrameThePlayerDies` 轉紅（「同一帧移動意圖必須歸零」，run `20261012T100706Z-mutation-order`）；還原後綠（`20261012T100815Z`） |
| AC-B.5 從未受擊：8／12 m 進出逐位元保留（Q4-G＝(i)）；脫戰當帧 `MovementIntent = default`（同帧一致） | ✅ `CombatContextTests.TC8`（改指向新純函數，斷言不變）、`AICombatContextSourceTests.AB5_*`；PlayMode：出生距離 ≈18.6 m 未受擊 30 帧皆不交戰 |
| AC-B.6 任何距離受未致死傷害 ⇒ 當帧 `InCombat`；R＝L；d > L ⇒ 寬限 G；再受擊或回到 L 內 ⇒ 取消；期滿 ⇒ 離開 | ✅ `AB6_ProvokedEngagement_*`、`AB6_ProvokedBeyondProvokedRadius_*`、`AB6_ProvokedRadiusSmallerThanLeave_*`、`AB6_OwnNonLethalDamage_*`（真 `CharacterHealth`）；PlayMode：18.6 m 受擊 ⇒ 下一帧交戰、> 12 m 持續交戰。⚠️ L＝20／G＝5 是**測試值** |
| AC-B.7 一致性：7 m 受擊後退到 13 m、與 15 m 受擊，兩者脫戰距離都是 L | ✅ `AB7_DisengageRadius_DoesNotDependOnWhereTheHitHappened` |
| AC-B.8 目標死亡 ⇒ **當帧** `CombatContext = default` 且 `MovementIntent = default`；無寬限；`Revive` 後依距離恢復 | ✅ `AB8_TargetDeath_*`（Provoked episode 中死亡也不給寬限）、`AB8_UntrackedTarget_*`；PlayMode `YBot_StopsChasingOnTheFrameThePlayerDies`（真 Runner；同帧 `AIInputSource` 也不出手） |
| ↳ 🔄 2026-10-13（`docs/55` P1） | 有 `EnemyTerritory` 的敵人在目標死亡（或任何脫戰）的那一帧起改由**非戰鬥行為**給意圖（回家／遊走，walk 速度），不再是 `MovementIntent = default`。本 AC 真正要守的「**同一帧不再追擊**」不變：`AICombatContextPlayModeTests` 改為斷言「意圖若存在必須來自非戰鬥行為（模式非 None、速度＜0.5）」——順序 mutation 仍會被抓到（追擊意圖的模式是 None、速度 1）。EditMode `AB8` 的 rig 沒有 territory，仍斷言歸零 |
| AC-B.9 自己死亡 ⇒ 不寫 `CombatContext`、不產生意圖（沿用既有裁決） | ✅ `AB9_SelfDead_*` |
| AC-B.10 `SampleScene.unity` 的 git diff 與實作前**位元相同**（使用者 WIP 未被覆寫） | ✅ 工作樹 blob `ab892bb…`、`git diff` sha256 `b8d8fe03…` 實作前後相同；prefab 遷移後 Editor 回報 scene dirty＝False；未呼叫任何存場景 API |
| AC-B.11（Play）玩家死後敵人停止繞行、不面向屍體、血條收起；10–20 m 外施法會激怒敵人；L／G 觀感 | ⬜ 人工（PlayMode 已驗資料層：交戰解除與移動歸零；**面向、血條、觀感未驗**） |
| 驗收④ `AIInputSource`／`AICombatContextSource`／`AIMovementSource` 用同一個 Target | ✅ `SingleTargetAuthority_AllThreeAIComponentsFollowTheSameReference`（改那一份，三者同時跟著變；置空三者同時停） |
| 驗收⑤ Y Bot prefab 序列化引用正確 | ✅ `W15`／`W27`；遷移 log：`movement.Target`＝X Bot（遷移前後相同）、`AICombatContextSource` 8／12／20／5、`AIInputSource.target` 屬性已移除。⚠️ `SaveAsPrefabAsset` 連帶把 prefab 中**缺漏的欄位**以當下記憶體值（＝程式預設）寫出（FootIK settings 約 60 欄、`LocomotionModel.alignRunEntryPhase 0`／`entryPhaseMaxHeadingDegrees 30`），並移除程式已不存在的 `GroundStepTolerance`——行為中性（Unity 本來就以這些預設值反序列化），但只有 Unity 匯入保護、沒有逐欄測試 |

### WP-3a Q5 typed movement limit——**Automated Verification Passed / Human Verification Pending**（2026-10-12 使用者批准後實作；§3.5.6、§3.5.7）

> **落地形狀**：①`ActionPhaseEntry.MovementLimit : ActionMovementLimit { None = 0, DirectionalSpeed = 1 }`（enum 住在 `Project.Core.Movement`，宣告者依賴 Movement 是既有方向）；②`IActionMovementLimitTarget.SetActionMovementLimit`（`IMovementModel.cs` 內，`IFootPhaseSource` 同形），`LocomotionModel` 實作：一個值＋一個帧號，只承認本帧或上一帧；③`LocomotionModel.directionalSpeedLimitActivation : DirectionalSpeedLimitActivation { WhileInCombat = 0, WhileActionDeclares = 1 }`。`ActionState` 在 `EnterPhase`／`TryAdvanceChain` 宣告、`OnTick` 每帧續約（在 `deltaTime <= 0` 的 early return **之前**）、`ResetExecutionState` 撤銷。
> **資產（Editor API）**：`FireballDefinition` Phases[0]＋ChainSegments[0..1]、`IceSpellDefinition` Phases[0] → `DirectionalSpeed`（由 X Bot mapping 的 `BaseLayerTransition` 推導，不是手挑）；所有 Bake phase（MeleeSlash1、EnemyPunch、Throw×4）維持 `None`；`X Bot.prefab` 的 `LocomotionModel` → `WhileActionDeclares`；`Y Bot.prefab` 不動（`WhileInCombat`）。還原＝git。

| AC | 驗證 |
|---|---|
| AC-3a.1 X Bot 的啟用政策＝`WhileActionDeclares`；Y Bot＝`WhileInCombat` 且逐位元不變 | ✅ `SpellLayeringWiringTests.SL8`（讀出貨 prefab）；`Policy_WhileInCombat_IgnoresActionDeclarations_YBotUnchanged`；`RealLocomotionModel_*`（WhileInCombat 照舊受限） |
| AC-3a.2 宣告 `DirectionalSpeed` 的 phase 進入帧 N：model 在 N 不受限、N+1 起受限（明文斷言首帧延遲） | ✅ `ActionMovementLimitTests.AC2_*`（真 `ActionState`＋真 `FullBodyStateMachine`；model 自己數帧但閘門呼叫正式純函數） |
| AC-3a.3 被 Hurt／Death／Roll 打斷、自然結束、Action→Action 重入：撤銷後的下一帧 model 讀到 `None` | ✅ `AC3_NaturalCompletion_*`、`AC3_InterruptedByHurtDeathOrRoll_*`（3 例）、`AC3_ActionToActionSwitch_*`（雙向，後寫者贏） |
| AC-3a.4 stale：停止續約（不呼叫 `OnTick`）兩帧後 model 自動視為 `None`；暫停（`deltaTime = 0`）期間續約不中斷 | ✅ `AC4_MissingRenewal_*`、`AC4_Pause_*`、`Stale_*`（含哨兵帧號不溢位）。🧪 **mutation**：把續約移到 early return 之後 ⇒ `AC4_Pause` 轉紅（run `20261012T101845Z-mutation-pause`），已還原 |
| AC-3a.5 未宣告（`None`）的 Definition：與今天逐位元相同 | ✅ `AC5_UndeclaredAction_NeverLimits`；`RealLocomotionModel_*`：未宣告與沒有 profile 的 `MoveSpeed` 逐位元相同（交戰中亦然） |
| AC-3a.6 ⛔ `IMovementModel` 未修改；⛔ 黑板未新增欄位；`ActionMovementLimit` 封閉清單（`None`＋一個成員）；`ActionState` 不含 Layer／Mask 字樣（A36） | ✅ 🆕 A55（另守：唯一宣告者＝`ActionState`；啟用判斷不讀 Presentation 播放狀態）＋ A36 |
| AC-3a.7 關聯測試：有 Layer 0 companion 的 Spell phase 必須宣告；有 Bake 的 phase 必須為 `None` | ✅ `SpellLayeringWiringTests.SL7`（讀出貨資產；runtime 不讀 mapping，只守兩份 authored 事實不漂移） |
| AC-3a.8（Play）空地施法側移 playback ≤ 1.35×；交戰中未施法轉向不再減速；首帧延遲不可見 | 🟡 **首帧延遲：已量測（下表）**；playback 倍率與「交戰中轉向不再減速」的手感 ⬜ 人工 |
| AC-3a.9 ADR-006 §5 與 `docs/21` §1.3.1 的窄化修訂已寫入（單一型別化成員、無數值、只啟用既有 actor 限速） | ✅ ADR-006 §5＋§8、`docs/21` §1.3.1／§1.5、`docs/11` §13、`docs/01` |

**首帧延遲量測（驗收⑦，`SpellMovementLimitPlayModeTests`；出貨場景、真鍵盤輸入 D 側移→Q 施放 Fireball、固定 60 fps；資料 `docs/artifacts/action-combat/wp3a-first-frame-delay-*.csv`）**

| 情境 | 意圖速度 | 進入帧 N 的本地方向 cap | N 的 smoother 目標差（延遲的全部影響） | cap 開始咬住 | 速度 N-1／N／N+1 → N+30 | 最大逐帧速度變化 | A（正式）vs B（零延遲對照） |
|---|---|---|---|---|---|---|---|
| 第一次施法 | 0.7500 | 0.7715 | **0.0000** | N+6 | 0.7500／0.7500／0.7500 → 0.56 | 0.0125 | cap 咬住前 [N-10, N+6)：速度差 **0.0000**、位置差 **0.000 cm**。之後兩次執行的施法朝向（瞄準）不可重現，整段位置差 0–1.6 cm 隨執行而變——那是朝向雜訊，與宣告無關（宣告只可能影響帧 N） |
| 冷卻一結束就再施法（最早可達） | 0.7500 | 0.7716 | **0.0000** | N+6～N+7 | 0.7488／0.7490／0.7492 → 0.56–0.61（朝向雜訊） | ≤ 0.0123 | 不比較：兩次執行在施法**前**已差約 3 cm（第一次施法後的相機／瞄準時序不可重現），改用解析對照 |

**判讀**：進入帧 N 時角色仍面向移動方向（本地＝前方），前方 cap 0.7715 高於鍵盤 Run 意圖 0.75（Walk 0.3651 更低；Sprint 未綁定）⇒ 宣告在 N 生效與否**不改變任何東西**。cap 要等施法的朝向承諾在 N 的 LateUpdate 開始轉身後（N+6）才低於意圖，那時宣告早已生效。⇒ 在可達的鍵盤情境中**首帧延遲不可見**，沒有突變（逐帧速度變化 ≤ 0.0125），⛔ 無需也未改動管線。⚠️ 理論上的可見情境：進入施法時朝向**已經**偏離移動方向且意圖高於該方向 cap（例如由一個帶朝向承諾的 Action 直接切入施法）——影響上限是 smoother 一帧（≈16.7 ms）的目標差，未實測；`SpellMovementLimitPlayModeTests` 的 `entryTargetGap == 0` 斷言會在調校或朝向行為改變時轉紅，屆時先重量、回報。

**行為差異（相對 WP-3a 之前）**：X Bot **交戰中未施法**不再套用施法限速（`docs/11` §13 已接受的副作用消失；`docs/48` §7 量到的戰鬥轉向最低速度 0.553–0.677 不再出現——⬜ 手感待人工）；X Bot **非交戰施法**仍受限（不再依賴「出手即交戰」）；交戰中**連續施法**改為逐次啟用；Y Bot 逐位元不變。

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

## 7. 真正阻擋實作的事項（第五版）

| 阻擋 | 擋住哪裡 | 需要什麼 |
|---|---|---|
| ~~**方案③ 的實作批准**~~ | WP-1b＋WP-2 | ✅ 2026-10-12 已批准並實作；剩人工 AC-B.11 |
| ~~**Q5 的實作批准**~~ | WP-3a → WP-3b | ✅ 2026-10-12 已批准並實作（WP-3a）；剩人工 AC-3a.8 手感。WP-3b 不在本輪 |
| **M2a–M2c** | WP-4 | Editor；未驗證前不固定承諾時點 |
| **人工驗收** | WP-0、WP-1 關閉 | AC B 的 Play 重驗；受擊語音（M5）與近戰命中音聆聽 |

**明確不在本輪**：攻擊者身分 seam、陣營／友軍傷害、通用仇恨系統、`ActionRequestTarget` 改名、近戰連段 release-once、場景孤兒 override 的清理（留給使用者存場景時）。

## 8. 與本案無關的既有失敗（獨立紀錄）

| 測試 | 模式 | 失敗訊息 | 來源 | 與本案的關係 |
|---|---|---|---|---|
| `StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable` | EditMode | `scene/X Bot FootIKController.settings.GroundLayers=503，應為 -9` | 使用者**未 commit** 的 `SampleScene.unity` 改動新增了 `settings.GroundLayers.m_Bits: 503` override（HEAD 沒有） | 無關。本案不改場景 |
| `MotionDriverCommittedMovementPlayModeTests.C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact` | PlayMode | 陽性對照失敗：非 traversal 的 false→true 邊沿本來就該回報落地 | 既有失敗（`WORKLOG.md` 多輪回歸皆記為「只有既有 C0」） | 無關 |

紀錄：EditMode `Logs/safe-test/20261012T082055Z-wp01-editmode.*`；PlayMode `Logs/safe-test/20261012T082210Z-wp01-playmode.*`。
WP-1b＋WP-2（2026-10-12）：EditMode **912／910**（1 failed＝上表場景 mask、1 skipped）`20261012T100155Z-wp1b-editmode`；PlayMode **109／108**（唯一失敗＝`C0`）`20261012T100251Z-wp1b-playmode`——失敗清單與 WP-0／WP-1 基線逐名相同。
WP-3a（2026-10-12）：EditMode **928／926（1 failed＝場景 mask、1 skipped）** `20261012T101738Z-wp3a-editmode-2`；PlayMode **111／110（唯一失敗＝C0）** `20261012T103416Z-wp3a-playmode-2`——失敗清單同上。