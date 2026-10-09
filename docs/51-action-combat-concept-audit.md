# 51 — Action／Combat Concept Fragmentation Audit（2026-10-10）

> **性質**：研究報告，**只讀不改**。runtime、資產、測試、`docs/00-map.md`、`WORKLOG.md` 皆未動。
> **標準與格式**：完全比照 `docs/47`（§A–§I、四類分類法）＋ `docs/48` §0（**先對照既有裁決再下結論**）。
> **範圍**：`Core/StateMachine/States/{Action,Hurt,Death}State`、`Core/{Combat,Actions,Effects,Survivability,Facing}`、`Core/Movement/AIMovementSource`、`Core/Pipeline/AIInputSource`、`Presentation/{Actions,Equipment}`、`Presentation/Camera/AimResolver`（只審 gameplay 契約）；治理 ADR-004／005（✅ Accepted）、ADR-007／009（🟡 Trial）、`docs/11`／`15`／`19`／`21`／`26`。
> **方法**：每個概念從 producer 追到 ≥ 2 個 consumer 的 call site；數值取自 `X Bot.prefab`／`Y Bot.prefab`／`*Definition.asset`／`*StateMachineConfig.asset`／`SampleScene.unity` 的**序列化值**（唯讀 grep），不取 C# 預設值。
> **沒做的事**：⛔ 未使用 Unity Editor（讓給 Traversal session），沒有 Play／probe——所有「會發生」的敘述都是**靜態推導**，標明為推導；需要實測的項目集中在 §I.3「待量測」。

### 分類（同 `docs/47`）
① **Healthy specialization**（同名或相近，但回答不同問題）／② **Duplicate fact derivation**（同一個事實被兩處各自推導）／③ **Representation fragmentation**（同一概念有多個載體／語意隨路徑改變）／④ **Special-case architecture**（為單一案例留下、已失去獨立語意的結構）

---

## 0. 本審查先對照過的既有裁決（不重議）

`docs/47` 的教訓是「沒先對照裁決，就把刻意設計判成缺陷」（F-02／F-03 後來更正）。下列每一條都已逐條核對；凡 finding 碰到它們，會在該條明講。

| 來源 | 內容 | 對本審查的限制 |
|---|---|---|
| ADR-004 D2／D6（Accepted） | Action 能不能出手、release 時點只由 `ActionState` 回答；interrupt＝`CanBeInterruptedBy`（閘門）＋ Priority（競爭）＋ `CanTransitionAway`（自我承諾） | AI 的 request cadence 不算第二個 gate（A27 守） |
| ADR-005 D1／D2（Accepted） | 單一 `ActionSlot` 身分；⛔ 不建 Ability／StatusEffect framework | Slow 不得被建議為 framework |
| ADR-007 D3 補充條款（Trial） | 每角色**唯一** combat context producer；⛔ 禁止其他系統自建 `isInCombat`／第二份 target 清單 | 玩家與 AI 各一個 producer 是**合法**的（A5 `WriterRules` 白名單兩檔） |
| ADR-007 2026-09-10 修訂 | 持續 target-facing **只給敵人**；玩家 `usePersistentCombatFacing = 0` | 玩家戰鬥中面向跟移動方向不是缺陷（W14 守） |
| `docs/11` §14（2026-09-15，使用者明確裁決） | **承諾拆兩份**：`_releaseContext`（facing，段落邊界）／`_effectContext`（每帧刷新、release 定案） | 拆分本身不是缺陷；本審查只看「文件與測試有沒有跟上」 |
| `docs/11` §13（同日） | combat directional speed cap 只看 `InCombat` ⇒ X Bot 所有戰鬥側移都變慢——**已知並接受** | 不重議 |
| `docs/11` §7（2026-09-02） | Slow＝`AIMovementSource` 意圖係數 0.3；不疊層；A25 擋擴散 | 只確認現況 |
| `docs/19` §3.1（2026-09-14，使用者選擇） | 死亡是 facing 的**否決層**；**否決了**「死亡時停止產生 `CombatContext`」⇒ 死亡由**各 consumer 自己收手** | consumer 端的 `!IsDead` 檢查不得判為 duplicate |
| `docs/26` Model B（2026-09-14） | 受擊＝`StateType.Hurt`（不是 Action）；Roll／Traversal → Hurt **不允許**（config 明示，不靠 priority）；被擋的受擊**丟棄不補播** | 不重議 |
| ADR-009 D2／D4（Trial） | 後果（Hurt／Death）只由 `CharacterHealth` 決定；Death 不吃 `Interruptible` 否決（`ActionState.CanBeInterruptedBy` 的型別例外）**保留** | 該例外不是缺陷（4.2 修訂明列保留理由） |
| `docs/21` §1.3／W10 | `attackRange` **必須等於** `maximumEngagementDistance` | 兩個欄位是兩個問題，等號是契約 |

### 0.1 已機器化的不變量（避免把已守住的東西當新發現）

| 測試 | 守什麼 |
|---|---|
| A5 `WriterRules` | `CombatContext`＝`PlayerCombatContextSource`／`AIMovementSource`；`Survivability`＝`CharacterHealth` |
| A19／A20／A22 | 無 per-action subclass；Action 位移只經 `MotionDriver`；`ActionState` 不生滅物件 |
| A21／A29 | 外部 seam 不得持有動畫／轉移權威；三個 sink 不得解方向（token：`AimResolver`／`TryGetAimPoint`／`transform.root.forward`） |
| A24 | `ActionSlot` 唯一宣告；冷卻執行期狀態只在 `ActionState` |
| A25 | Slow 符號不得出現在 Model／Smoother／StopSelector／FootIK／Audio |
| A26 | 近戰命中窗由 lifecycle 驅動、走 `ApplyDamage`、不得用 particle |
| A27 | 攻擊 trigger 只能由 `IInputSource` 產生；`AIInputSource` 不得讀 FSM |
| A31／A32／A33 | `RequestFacing` 唯一送出者＝`CharacterFacingSource`；`MotionDriver` 不決定 facing；debug 快照不重算 |
| A37／A38 | `ActionSlot.Reaction` 退役；**出貨 config** 的 permission 矩陣（Roll／Traversal 不含 Hurt、Death 吸收態、Hurt > Action priority） |
| W10／W14／W15 | 攻擊圈＝接戰帶上界；persistent facing 玩家 false／敵人 true；AI 兩顆元件的 target 相同（**prefab 層**） |

---

## A. Executive Summary

**整體 fragmentation：Low–Moderate。** 明顯比 Presentation/IK（`docs/47`：Moderate、局部 High）健康。

- **決策擁有權是健康的**，而且大多被測試釘住：「能不能出手」只有 `ActionState`（A24／A27）、「被打之後怎樣」只有 `CharacterHealth`（A5／A26）、「轉向哪」只有 `CharacterFacingSource`（A31）、中斷 permission 讀出貨資產（A38）。`docs/26` Model B 把受擊從 Action 移出，消掉了本系統最大的一個 special case。
- **碎裂集中在一條縫：「玩家路徑 vs AI 路徑」。** 同一個概念在玩家側有完整定義（段落承諾、目標合法性、受擊即交戰），在 AI 側是縮減版或缺席——不是因為裁決說 AI 不需要，而是因為 slice 先做玩家（S3a）、AI（S3b）只補了一半。`docs/15` §16-8 登記過其中一項，但後寫的 `docs/19` 又寫成「已存在」。
- **第二條縫是文件／測試沒有 fold back。** 承諾拆兩份（`docs/11` §14，使用者裁決）之後，ADR-007 D5、design-doc、`docs/15`／`32`／`33` 仍寫「facing 與 release 讀同一份」，`TD5B` 的名稱與斷言訊息仍是舊契約、且只因 release 在 t=0 才綠。

**最嚴重的 5 個概念：**

| # | 概念 | 一句話 |
|---|---|---|
| 1 | **方向承諾（commitment）** | 程式已是「facing 一份、效果一份」，但治理文件六處仍寫「同一份」；ADR-007 D5 明文 ⛔ 的正是現況。效果那一份**每帧重選 soft target**，與 2026-09-11 的「段內不重選、目標失效仍保留」也矛盾（F-01） |
| 2 | **攻擊方向承諾（AI 側）** | 敵人沒有 `IAimSource` ⇒ 出拳**沒有承諾**，整段由 persistent combat facing 連續追蹤玩家；`EnemyPunch` 已有 Bake ⇒ 前衝位移也跟著轉。`docs/19` §3.2 卻寫「敵人出拳時朝向承諾方向」（F-02） |
| 3 | **目標合法性／可命中** | 五條規則：玩家選目標（marker＋非屍體）、近戰（`CharacterHealth`＋碰撞矩陣）、投射物（屍體透明）、地面 AoE（屍體照樣被 Slow）、AI（序列化 Transform，**不檢查死活**）。玩家死後敵人會持續繞屍、面向屍體、每 2–3.2 s 揮拳並播命中音（推導）（F-03） |
| 4 | **進入戰鬥** | 玩家：半徑 6／9 ＋ 出手 ＋ **受擊** ＋ 5 s AND 規則；敵人：半徑 8／12 only。玩家可在 8–12 m（soft target 12 m）外打敵人，敵人播受擊後**繼續站著**（推導）（F-04） |
| 5 | **中斷 permission 的表示** | 同型重入（Hurt→Hurt、Action→Action）完全不經 config；A38 卻斷言 config 的 Hurt→Hurt 為 true——斷言了一個 runtime 不讀的值（F-07） |

**是否已出現「每條 function path 重新定義 gameplay truth」？** 沒有擴散成系統性問題。Core 的每個決策都只有一個回答者；分岔發生在**「哪些情境會去問那個回答者」**——AI 路徑常常沒去問（承諾、目標合法性、受擊即交戰），sink 則各自補了一點「這一下算不算」的判斷（屍體、去重）。

**現在需要處理嗎？** F-01 的**文件 fold back** 建議現在做（它會讓下一個讀者或 agent 把刻意的拆分「修」回去——正是 `docs/47` 犯過的錯的鏡像）。F-02／F-03／F-04 是**行為決定**，需要使用者裁決，不是重構。其餘屬 *worth fixing later*。

---

## B. Concept Inventory

| Concept | Canonical owner | Producers | Consumers | Alternate definitions | Risk |
|---|---|---|---|---|---|
| Action identity | `ActionSlot`（ADR-005 D1） | `ProcessIntents` → `Intent.RequestedActionSlot`；`ActionRequestTarget.PendingSlot`（**零 producer**） | `ActionState`、HUD、sink bindings、cooldown | 無（A24） | Low |
| 能不能出手 | `ActionState.TryResolveRequest`（`:347-370`） | — | FSM（`CanEnter`／`CanReenter`） | AI retry cadence 是 producer 節奏、不是 gate（A27） | Low |
| Release 時點 | `ActionState.TryEmitRelease`（`:589-598`），每段一次 | — | 5 種 sink | `MeleeHitboxSink._releasedThisExecution` 每次執行一次（F-09） | Low |
| 命中窗／命中時刻 | 各 sink（依投遞形狀） | 近戰：Release→Cleanup；投射物：自己的 sweep；AoE：Release 當帧 | `CharacterHealth.ApplyDamage` | 三種形狀（① Healthy） | Low |
| 可命中／目標合法 | **無 owner** | `PlayerCombatContextSource.IsHostileCandidateLegal`（`:254-264`）、三個 sink 各自、AI 序列化 Transform | 選目標、傷害、Slow、命中音 | **5 份**（F-03） | **Medium** |
| 被打的後果 | `CharacterHealth`（ADR-009 D2） | `ApplyDamage` → `PublishTo`（順序 0.5） | Hurt／Death／Audio／CombatContext | 無 | Low |
| 死活（自己） | `CharacterHealth` → `Survivability.IsDead` | 同上 | DeathState、DeathArbiter、Facing、HeadLook、AIMovementSource、HealthBar、MotionDriver | consumer 各自收手（**已裁決**，§H） | Low |
| 死活（目標） | 讀**對方的** `CharacterHealth` 元件 | — | 玩家選目標、投射物 | AI 兩顆元件、近戰命中音、AoE Slow **不讀**（F-03） | Medium |
| 受擊事件 vs 受擊反應 | `JustTookDamage`（事件）／`HurtState`（反應） | `CharacterHealth` | HurtState、CombatContext、Audio | 音效跟事件、不跟反應（F-10） | Low |
| 進入戰鬥（`InCombat`） | 每角色唯一 producer（ADR-007 D3） | 玩家 `:64-107`；AI `:134-144` | Facing、LocomotionModel cap、HeadLook、HealthBar、AIMovementSource | **兩套判準**（F-04） | Medium |
| 戰鬥目標 | 同上 | 玩家：黏性 marker；AI：序列化 Transform **×2**（`AIMovementSource`＋`AIInputSource`） | Facing、HeadLook、AIInputSource | AI 兩份參照（F-05）；`TargetPosition` 參考點兩種（F-06） | Low–Medium |
| Soft target | `PlayerCombatContextSource`（無記憶） | `TrySelectSoftTarget`（12 m／25°） | `ActionState.TrySoftTarget` | 與黏性目標**刻意分開**（§H） | Low |
| Aim point | `AimResolver`（玩家唯一 `IAimSource`） | 相機中心射線 | `ActionState.ResolveAimContext` | 敵人**沒有**（F-02） | Low |
| Facing 承諾 | `ActionState._releaseContext`（段落邊界） | `CaptureReleaseContext`（`:497-501`） | `CharacterFacingSource` 優先序 1 | 敵人恆為 `HasAim=false`（F-02） | Medium |
| 效果落點 | `ActionState._effectContext`（每帧） | `RefreshEffectContext`（`:508-514`） | 所有 sink 的 `Release(in)` | 文件仍寫與 facing 同一份（F-01） | **Medium–High（文件）** |
| Facing authority | `CharacterFacingSource` | `TryResolveFacing` 四級 ＋ 死亡否決 | `MotionDriver.RequestFacing` | 無（A31） | Low |
| 中斷 permission | config ＋ `Interruptible` ＋ `CanReenter` ＋ Death 例外 | 資產＋三處程式 | `FullBodyStateMachine.EvaluateInterrupts` | 同型重入不經 config（F-07） | Low |
| Slow | `TemporaryGameplayEffectState`（只 Y Bot 有） | 投射物、地面 AoE | `AIMovementSource` 唯一 | 無（A25） | Low |
| 敵對身分 | **`ActionRequestTarget`（借用）** | prefab 掛載（Y Bot 有、X Bot 無） | 玩家選目標、AoE 探地排除、gizmo | mailbox 本身零寫入者（F-08） | Low |

### 主要概念的 Producer → Transform → Consumer

```text
方向承諾（❗程式已拆、文件未拆）
AimResolver.TryGetAimPoint（相機中心射線，帧快取）
        ↓ ActionState.ResolveAimContext（policy：CameraForward／CameraConeSoftTarget）
        ├─ 段落邊界 CaptureReleaseContext ── _releaseContext ── TryGetFacingCommitment
        │                                                        ↓ 順序 4.6
        │                                   CharacterFacingSource 優先序 1（8° 死區）→ RequestFacing
        └─ 每個 OnTick RefreshEffectContext ── _effectContext（含**重選** soft target）
                                                 ↓ release 當帧
                                     sink.Release(in _effectContext)
敵人：IAimSource == null ⇒ 兩份都 default（HasAim=false）
     ⇒ facing 落到優先序 2（persistent combat facing，無死區、逐帧）
```

```text
目標合法／可命中（❗五份）
玩家選目標   ActionRequestTarget ∧ 非自己 root ∧（無 CharacterHealth ∨ !IsDead）  :254-264
近戰         碰撞矩陣（Player/Enemy 互撞、Enemy/Enemy 也撞）∧ CharacterHealth ∧ 非自己 root；
             屍體：ApplyDamage 回 false，但命中音照播                              Melee :83-122
投射物       sweepMask ∧ 非 owner 子物件 ∧ 屍體透明（跳過繼續找）                 Projectile :222-226
地面 AoE     targetMask(Enemy) ∧ 非自己 root；屍體：不扣血、**照樣 ApplySlow**     Ground :261-285
AI           序列化 Transform（AIMovementSource、AIInputSource 各一份）；**不檢查死活**
```

```text
進入戰鬥（❗兩套）
玩家  inCombat = wasInCombat ∨ 本帧出手 ∨ 本帧受擊 ∨ 有黏性目標（6 m 進／9 m 留）
      離開 ＝ 無合法目標 ∧ 距上次互動 > 5 s                            :64-107
AI    inCombat = 距離 ≤（已交戰 ? 12 : 8）                              :287-296
      受擊不影響；目標死活不影響；死亡時**不寫**（保留上一帧值，consumer 自己收手）
```

---

## C. Fragmentation Findings

### F-01 方向承諾已拆成兩份，治理文件與測試仍寫「同一份」 — **Medium（文件／契約面 High）**

**Canonical concept**：「這一擊朝哪」。ADR-007 D5 原文：「facing 與 release 讀**同一份**承諾」「⛔ 禁止『facing 讀承諾、release 讀當下』的混合——那正是 A6」。

**目前版本**（`docs/11` §14，2026-09-15 使用者明確裁決）
```text
ActionState.cs:497-501   CaptureReleaseContext：_releaseContext = _effectContext = ResolveAimContext（段落邊界）
ActionState.cs:508-514   RefreshEffectContext：每個 OnTick 重跑 ResolveAimContext，live.HasAim ⇒ 覆寫 _effectContext
ActionState.cs:659       NotifyRelease 交付 _effectContext
```

**仍寫舊契約的地方**
```text
docs/ADR/007 D5（:137-145）、§6（:175「人與火球在結構上不可能分家」）、§11 無 2026-09-15 列
docs/01-design-doc.md:209   「facing 與世界效果讀同一份（ADR-007 D5）」
docs/15:581                 「D5 … ✅ 反而被強化」；docs/15:147「facing 與 projectile 共用已承諾的位置」
docs/32:367,558；docs/33:55,233(I9),460
ActionState.cs:569-573      TrySoftTarget 註解「ActionState 不重算選擇，也不在段落內追蹤」
ADR-007 §11 2026-09-11 列   「段內不重選；facing 與 projectile release 共用同一份；target 中途失效仍保留該段承諾」
```

**測試面**：`ActionStateTests.TD5` 已改寫成新契約（名稱仍是 `…ReleaseAndFacingUseSameCommittedDirection`）。`TD5B_SoftTargetSnapshot_IsStableForTheCommittedSegment` 的斷言訊息仍是「失效後仍須保留段落開始時承諾的 soft-target point」「facing 與 projectile release 必須共享同一次 commitment」；它之所以綠，是因為測試用的 `Entry(...)` 預設 `releaseNormalizedTime = 0`——**release 發生在 `OnEnter` 當帧、目標失效之前**。換成出貨值（Fireball／Ice 的 0.35），同一個情境會釋放到相機點，與斷言相反（推導）。

**為什麼這不只是文件問題**：效果那一份不只「跟上目標」，還**每帧重選 soft target**（`ResolveAimContext` 內的 `TrySoftTarget`）。出貨的 Fireball／Ice 都是 `Targeting: 1`（`CameraConeSoftTarget`），release 在抬手 0.42–0.48 s。這段期間：
- 目標離開 12 m／25° 角錐 ⇒ `TrySoftTarget` 失敗 ⇒ 效果落點**換成相機中心射線點**（`HasAim` 仍為 true，`docs/11` §14 的「保留上一次可用落點」不適用——那只處理 `HasAim=false`）；
- 多個敵人時，效果可以換到**另一個**敵人，身體仍朝第一個（現場景只有 1 隻 Y Bot ⇒ 此分支目前不可達）。

**分類**：③ Representation fragmentation——「承諾」從一個概念變成兩個載體是**已裁決的 ① Healthy specialization**；碎裂的是**契約的表示**（ADR／Living Doc／測試名各說各話）。另有一個未裁決的語意：效果端的 soft target 是「跟上同一個目標」還是「每帧重選」。

**為什麼在本 repo 是問題**：ADR-007 是 🟡 Trial，CLAUDE.md 規定 Trial 期修訂要記進該 ADR §11；design-doc 是 Living Doc，Fold-back 規則要求與程式一致。下一個照 D5 審程式的人會把 `_effectContext` 判成違規——這正是 `docs/47` F-02／F-03 的鏡像錯誤。

**最小修法**（只動文件與測試命名，不動行為）：ADR-007 §11 補 2026-09-15 列並在 D5 加修訂框（「facing 承諾段落一次；效果落點 release 當下解算」）；design-doc:209、`docs/15`、`docs/32`／`33` 同步；`TD5B` 改名並把 release 時點改成出貨值，讓它真的測到中途失效。效果端是否「鎖目標身分、只跟位置」→ 使用者裁決（§I.2-Q1）。

---

### F-02 敵人的攻擊沒有方向承諾；`docs/19` 說有 — **Medium**

**Canonical concept**：同 F-01——「這一擊朝哪」，由段落承諾回答，facing 只在死區外補轉（ADR-007 D5；`docs/15` §3 優先序①「出手期間不得被任何東西拉走」）。

**玩家版本**：`AimResolver`（X Bot 有）→ `_releaseContext` → `CharacterFacingSource` 優先序 1（8° 死區）。

**AI 版本**
```text
Y Bot.prefab：沒有任何 IAimSource（AimResolver guid 出現 0 次）
ActionState.cs:530   if (_aimSource == null) return default;   ⇒ HasAim=false
ActionState.cs:555   TryGetFacingCommitment ⇒ false
CharacterFacingSource.cs:54-58  usePersistentCombatFacing（Y Bot = 1）∧ InCombat ∧ HasTarget ⇒ 優先序 2，無死區、逐帧
EnemyPunchDefinition.asset：Targeting 0（CameraForward——對沒有相機的角色無意義）；Phase Start 有 Bake（guid bb9fe3…）
ActionState.cs:303   有 Bake 且正在播 ⇒ ExecuteBakedCurveMovement 沿「當下」transform.forward 積分
```
⇒ **敵人整段出拳都在追蹤玩家，Bake 的前衝位移方向也跟著轉**（推導）。玩家側移閃避時，拳與前衝會跟過來。

**文件分岔**：`docs/15` §16-8 把它登記為開放項（「敵人沒有 `AimResolver`，若要為它建立 `ActionReleaseContext` 需要一個角色 origin」）；`ActionState.cs:527-529` 的註解也知道。但後寫的 `docs/19` §3.2 寫「Action commitment 仍然壓過 combat facing——敵人出拳時朝向承諾方向是正確的，且 `EnemyPunchDefinition` 無 Bake」——兩個前提今天都不成立。

**分類**：③ Representation fragmentation（同一個問題「此擊朝哪」，玩家用承諾回答、AI 用持續 facing 回答）＋ 文件錯誤陳述。

**風險**：手感（追蹤拳）只能 Play 判斷；但架構上，`docs/19` 的錯誤前提會讓後續 AI 工作（例如加入閃避判定、加入 Bake 前衝較長的招式）建立在「敵人有承諾」的假設上。

**最小修法**：先裁決（§I.2-Q2）——(a) 接受追蹤：改 `docs/19` §3.2、把 `EnemyPunch` 的 `Targeting` 意義寫清楚；(b) 給敵人承諾：需要 `docs/15` §16-8 的 origin seam，屬 ADR-007 範圍（Trial，可修訂，不開新 ADR）；(c) 只在 release 後停止追蹤：facing source 需要知道 phase，會碰 A33 的「不重算」紀律，要另評。

---

### F-03 「誰是合法目標／可被命中」有五條規則 — **Medium**

**Canonical concept**：「這個東西此刻能不能被我選為目標／被我的攻擊結算」。

| 規則 | 敵對判準 | 自己排除 | 屍體 | 位置 |
|---|---|---|---|---|
| 玩家選目標（combat／soft） | 有 `ActionRequestTarget` ∧ `targetMask`(Enemy) | `transform.root == selfRoot` | **不合法**（`docs/15` §16-7 結案） | `PlayerCombatContextSource.cs:254-264` |
| 近戰 | 碰撞矩陣 ∧ 有 `CharacterHealth` | `root == root` | `ApplyDamage` 回 false，但**命中音照播** | `MeleeHitboxSink.cs:83-122` |
| 投射物 | `sweepMask`（除 layer 3 外全部） | `IsChildOf(_ownerRoot)` | **透明**（跳過、繼續找後方） | `ThrownProjectile.cs:222-226,275` |
| 地面 AoE | `targetMask`(Enemy) | `root == root` | 不扣血，但**照樣 `ApplySlow`** | `GroundEffectSink.cs:261-285` |
| AI（移動＋出手） | 序列化 Transform | — | **不檢查** | `AIMovementSource.cs:114-144`、`AIInputSource.cs:93-97` |

**可達的分岔**（推導；出貨事實：重生是**手動按鍵**（`RespawnController.RespawnAction`），玩家可以無限期保持死亡）：
- 玩家死亡後，Y Bot 的 `CombatContext` 仍由距離決定 ⇒ 繼續 Approach／Strafe 繞著屍體、persistent facing 面向屍體（敵人自己沒死，`DeathSuppressed` 不適用）；
- `AIInputSource.WantsToAttack` 只看距離 ⇒ 每 `Cooldown 2 + variance ≤1.2` 秒出一拳；拳打到屍體：`ApplyDamage` 回 false（不扣血），但 `MeleeHitboxSink.cs:122` 在被打者身上播 `hitAudio`；
- 反方向（玩家打敵人屍體）：同一具屍體，投射物穿過、近戰揮中播命中音、AoE 對它 `ApplySlow`。Slow 對屍體沒有可見效果（死亡的 `AIMovementSource` 不產生意圖），敵人也不會重生 ⇒ 無害，但它說明 sink 端對「屍體」沒有共同規則。

**附帶（潛在）**：近戰與 AoE 都沒有陣營概念。碰撞矩陣 Enemy↔Enemy 是開的，Y Bot 的 `MeleeHitbox` 在 Enemy 層 ⇒ 兩隻敵人相鄰時會互毆。`SampleScene` 只有 1 隻 Y Bot ⇒ 目前不可達。

**分類**：② Duplicate fact derivation（「目標死活／可命中」各自判斷）。玩家選目標那一份是 `docs/15` §16-7 明確建立的 **target validity**（與 `docs/19` 的「我死了所以我收手」是不同概念，見 `artifacts/survivability.html`）——AI 路徑從未接上它。

**最小修法**：裁決 §I.2-Q3。若「目標死亡 ⇒ 不再是合法目標」要對 AI 也成立：AI 兩顆元件讀**目標的** `CharacterHealth.IsDead`（比照 `IsHostileCandidateLegal`，不新增黑板欄位）；近戰命中音改為只在 `ApplyDamage` 回 true 時播；AoE 對屍體不 Slow。全是行為變更。

---

### F-04 「進入戰鬥」兩個 producer、兩套判準 — **Medium**

**Canonical concept**：`CombatContext.InCombat`——「我現在在跟某個目標交戰」（ADR-007 D3：每角色唯一 producer）。

| | 玩家（`PlayerCombatContextSource`） | 敵人（`AIMovementSource`） |
|---|---|---|
| 進入 | 本帧出手 ∨ 本帧受擊 ∨ 6 m 內有合法目標 | 距離 ≤ 8 m |
| 離開 | 無合法目標 ∧ 距上次互動 > 5 s | 距離 > 12 m |
| 受擊 | **算互動**（`docs/15` §4.1：「被打當然算交戰」） | **不影響** |
| 目標死亡 | 清目標 → 計時離開 | 不影響（F-03） |
| 距離 | 3D（`:269-270`） | 水平（`:122-124`） |
| 自己死亡 | — | **不寫**，保留上一帧值（`:107-112`；consumer 收手，已裁決） |

**可達的分岔**（推導）：玩家的 soft target 範圍 12 m > 敵人 aggro 8 m，Fireball 15 m/s × 5 s。玩家站在 8–12 m 外連發火球，Y Bot 每發都播 Hurt，但 `InCombat` 仍為 false ⇒ 不接近、不面向、不反擊。

**是不是刻意的？** 兩邊的門檻值分開是 ① Healthy（不同角色、不同 tuning；`docs/20` E1 明說是第一版值）。但「**受擊算不算交戰**」是語意，不是門檻：玩家側明確寫了「被打當然算」，AI 側沒有任何文件討論過。⇒ 不是已裁決的差異，是未覆蓋。

**另一個語意重載**（玩家側）：「本帧出手」即進入 `InCombat`，與有沒有敵人無關 ⇒ 空地按一下攻擊，`InCombat` 維持 5 s ⇒ `LocomotionModel.ApplyCombatDirectionalSpeedLimit`（X Bot 有 `XBotSpellDirectionalSpeedProfile`）在這 5 s 內生效。`docs/11` §13 接受的是「所有**戰鬥**中側移變慢」；「沒有敵人的出手」算不算戰鬥沒有被討論過。X Bot 的 facing 跟移動方向 ⇒ 只在轉向暫態可見（`docs/48` §7 量到戰鬥轉向最低速度 0.553–0.677）。

**分類**：① Healthy（門檻）＋ ③ Representation fragmentation（`InCombat` 在玩家側的意思是「交戰或最近出過手」，在 AI 側是「目標在半徑內」；共同 consumer `LocomotionModel` 與 `CharacterFacingSource` 看到的是同一個欄位）。

**最小修法**：裁決 §I.2-Q4。若受擊要算交戰：`AIMovementSource` 在寫 `CombatContext` 時讀自己的 `Survivability.JustTookDamage`（順序 0.5 已發布，零新欄位），進入後由既有的 leave 半徑決定離開。

---

### F-05 AI 的目標與攻擊距離各有兩份 — **Low**

```text
AIMovementSource.target ＋ distance（水平）＋ maximumEngagementDistance 1.6
AIInputSource.target    ＋ distance（水平）＋ attackRange 1.6
```
`docs/21` §1.1 寫「`AIMovementSource` 已持有敵人的**唯一** target reference」——實際上有兩份。

**已被守住的部分**：W10（`attackRange == maximumEngagementDistance`）、W15（兩個 target 相同、`attackRange ≤ aggroEnter`）——這是 `StairAuthoringContractTests.MaskTable` 那種「用關聯式測試管理刻意的重複」，做法正確。
**沒守到的部分**：W15 只掃 **prefab**；`SampleScene.unity:7840,7884` 對兩個 `target` 各有一筆 scene override（今天都指向同一個物件 `1472398490`）。scene 層的分歧不會被任何測試看到。

**分類**：② Duplicate fact derivation（受測試約束）。**為什麼兩份**：A27 規定攻擊 trigger 必須在順序 1（`IInputSource`），移動在 2.5——兩顆元件是時序事實，不是隨意。

**最小修法**：把 W15 的 target 一致性擴到出貨場景；或 `AIInputSource` 改向同物件的 `AIMovementSource` 取 target（一個參照）。前者零行為風險。

---

### F-06 `CombatContext.TargetPosition` 的參考點隨 producer 改變 — **Low（潛在）**

- 玩家：目標 collider 的 `bounds.center`（胸口高度，`:273-278`）；保留檢查卻用 marker 的 `transform.position`（`:269`）。
- 敵人：`target.position`（玩家 root，腳底，`:121,143`）。

**Consumer**：`CharacterFacingSource`（水平化，兩者等價）、`HeadLookController`（**含 pitch**，`:58`）。今天只有 X Bot 掛 HeadLook ⇒ 只消費玩家那一種 ⇒ **不可達**。日後給敵人加 head look，會看向玩家的腳。

另：玩家選目標的角錐用「水平相機前方 · 3D 目標向量」（`:216-220`，高低差會降低 alignment），soft target 用「水平 · 水平」（`:331-341`）——同一個 25° 在兩處意義不同。

**分類**：③ Representation fragmentation（欄位名相同、語意隨 producer 變）。**最小修法**：在 `CombatContextData` 註解寫明「TargetPosition＝目標的瞄準中心」並讓 AI 對齊；或在加敵人 head look 時處理。不急。

---

### F-07 中斷 permission 的第四條路：同型重入完全不經 config — **Low**

```text
FullBodyStateMachine.EvaluateInterrupts：isSelf ⇒ 只問 _currentState.CanReenter(data)，不呼叫 CanBeInterruptedBy
HurtState.CanReenter  => JustTookDamage
ActionState.CanReenter => phase≠None ∧ Interruptible ∧ 不同 slot
```
`docs/26` §C.4 已列出三條授權路徑、§J.3 寫明「config 裡 Hurt 自己清單中的 Hurt 是冗餘但無害的」。**新發現的是測試面**：A38 `Require(Hurt, Hurt, true, "Hurt → Hurt：允許（硬直中再次受擊要再踉蹌一次）")` 斷言了一個 runtime 從不讀取的資產值。後果是雙向的：
- 有人從 Hurt 的清單拿掉 Hurt ⇒ A38 紅，但行為不變；
- 有人想用 config 關掉「硬直中再受擊」⇒ 改了資產、A38 擋下，就算放行也不會生效。

**分類**：③（同一個 permission 有程式與資產兩個載體，其中一個是影子）。ADR-004 D6 的三機制表沒有列 `CanReenter`。
**最小修法**：A38 把那一條改成斷言 `HurtState.CanReenter` 的行為（或註明「本列為文件性質」）；ADR-004 D6 是 Accepted ⇒ 在 Living Doc（`docs/26` 或 dev-spec §3.3）補第四列即可。

---

### F-08 `ActionRequestTarget`：名為 mailbox，實為敵對身分標記 — **Low**

- mailbox：**零 runtime 寫入者**（`RequestAction(` 全 repo 無呼叫者）；`docs/26` §J.5-4 已登記。
- 仍在讀它的 runtime 路徑：`ActionState.TryResolveRequest`（`:351-354`）、`IsRetriggeredThisFrame`（`:392`）、`FullBodyStateMachine` 每 tick `ClearAfterEvaluation`。
- 真正在用的角色：「這是一個可被當成目標的戰鬥者」——玩家選目標（`:152,208`）、`GroundEffectSink` 探地時排除角色（`:175`）、AoE gizmo（`:386`）。
- 出貨：Y Bot 有一顆、X Bot **沒有** ⇒ 「玩家可被當成目標」這件事在 marker 語意下不成立；AI 改用序列化 Transform 繞過（F-03）。

**分類**：④ Special-case architecture（為受擊 mailbox 建立、遷移後留下的物件，被借去承擔另一個概念）。**風險**：名稱誤導；`ActionState` 帶著一條沒有 producer 的分支。**最小修法**：不急。若要動：先在 Living Doc 宣告它的現役語意，再決定是否改名（腳本改名要保 `.meta` GUID，屬資產改動）。

---

### F-09 近戰的「一次執行只 release 一次」與 `ActionState` 的「每段一次」不一致 — **Low（潛在）**

`MeleeHitboxSink.cs:69-70` 自帶 `_releasedThisExecution`，只在 `Begin()` 重置。`ThrowProjectileEmitter` 在 2026-09-05 **因為一模一樣的原因移除了同一個旗標**（其 `Release` 註解：連段第 2、3 段被靜默吞掉；「sink 自帶第二套判斷正是 ADR-004 D2 要防的第二個權威」）。另外，連段之間沒有 `Cleanup` ⇒ 命中窗不會重開、`_hitTargets` 去重跨段。

出貨的 `MeleeSlash1`／`EnemyPunch` 都沒有 `ChainSegments` ⇒ **今天不可達**。給近戰加連段的那一刻會靜默失效。
**分類**：② Duplicate fact derivation（release 次數由 sink 重新定義）。**最小修法**：移除該旗標（與 Throw 同處置），並決定連段時命中窗／去重是否每段重置。

---

### F-10 受擊「事件」與受擊「反應」共用一個旗標 — **Low**

`JustTookDamage` 是「本帧受到未致死傷害」（事件）；`HurtState` 是「播硬直」（反應）。Roll／Traversal 中受擊：傷害成立、硬直被丟棄（`docs/26`，已裁決）——但 `AudioController.cs:122` 跟的是事件，仍播 Hurt 語音。`SurvivabilityData` 的註解寫「只有**表現**被丟棄」，而語音也是表現。
**分類**：① Healthy（事件與反應本來就該分開），但「音效跟哪一個」從未被討論（`docs/37` 只考慮了「致死時不得同時聽到受擊聲」）。**待 Play**：翻滾中被打，聽起來對不對。

---

### F-11 無瞄準時三個 sink 三種 fallback，且 A29 與 `docs/14` 的說法相反 — **Low（不可達）**

`HasAim=false` 時：Throw 用 emitter 自己的 `transform.rotation`（`:50`）；地面 AoE 用 `casterRoot.forward`（`:133`，`casterRoot = transform.root`——A29 的 token `transform.root.forward` 因此掃不到）；近戰不需要方向。`docs/14` T-D6 明文「sink 走 fallback」，A29 則禁止 sink 從 transform 取方向。X Bot 有 `AimResolver`、只在沒有相機時 `HasAim=false` ⇒ 不可達；敵人沒有投射物／AoE。
**分類**：③。**最小修法**：在 A29 或 `docs/14` 二選一寫清楚「無承諾時 sink 可不可以用 actor forward」。

---

## D. Special-case Object Inventory

| Item | Created for | Consumers | Independent domain meaning? | 建議 |
|---|---|---|---|---|
| `ActionState._effectContext` | 打移動目標（`docs/11` §14） | 所有 sink | **有**（效果落點） | Keep；文件 fold back（F-01） |
| `ActionRequestTarget` | 受擊 mailbox（已退役） | 選目標、AoE 探地 | 只剩「戰鬥者標記」 | Investigate（F-08） |
| `ActionState` 的 external request 分支 | 同上 | 無 producer | 無 | Merge candidate（＝移除候選，待裁決） |
| `CharacterPipelineRunner.actionReleaseSinkComponent`（legacy 單顆） | ADR-004 期 | 有 bindings 時完全忽略 | 無 | X Bot root 上仍掛一顆 `ThrowProjectileEmitter`（speed 5）＋ legacy 欄位指向它 ⇒ 死元件；Investigate |
| `ThrowDefinition.asset` | Throw slice（`docs/08`） | 沒有任何 config 引用 | 是唯一有 `Loop`／`Cancel` 的 Definition | Keep 作為 lifecycle 範例或移除——待裁決 |
| `ActionPhaseEntry.CancelMoveIntentThreshold` | Throw 的 Loop 取消 | 只在 `Loop` phase 讀（`:211`） | 有，但出貨內容沒有任何 Loop ⇒ Fireball／Ice／Melee 的 0.1 不生效 | Keep（policy），註記 |
| `ActionTargetingPolicy.SelfCentered` | 概念保留 | 無資產使用 | 有 | Keep（`docs/11` §8.3） |
| `ActionState.CanBeInterruptedBy` 的 Death 型別例外 | ADR-009 D4 | FSM | 有（lethal 層） | Keep（ADR-009 4.2 明列保留；`docs/26` §E.3 的 int 門檻是未來替代） |
| `CharacterFacingSource.usePersistentCombatFacing` | 敵人持續面向 | Facing | 有（actor policy） | Keep（W14） |
| `ActionDefinitionSO.CooldownVariance` | 敵人節奏 | `ActionState` | 有 | Keep（A27 精神：節奏是資料不是 AI 狀態） |
| `EnemyPunchDefinition.Targeting = CameraForward` | 預設值 | 無 aim source ⇒ 無作用 | 無（對敵人） | 隨 F-02 裁決處理 |
| `ThrownProjectile.SlowMovementSpeedMultiplier`（private 別名） | 2026-09-05 提升常數後留下 | 自己 | 無 | 可直接用 `TemporaryGameplayEffectState.SlowMovementSpeedMultiplier`；Low |
| `AIMovementSource._strafeBlocked` | 診斷 | gizmo | 無（純診斷） | Keep |

**過時註解**（不是 fragmentation，但會誤導下一個讀者；一併列出）：
- `ActionState.cs:182`「Reaction 是受擊而非出手，明確不取得」——Reaction 已退役，下一行 `CaptureReleaseContext` 對所有 slot 取承諾。
- `ActionState.cs:237-238`「現況的 `EnemyPunchDefinition` 就是（Super Armor）」——出貨資產 `Interruptible: 1`。
- `HurtState.cs:75`「且不套用烘焙曲線位移」——HurtState **永遠**不套用（`:139-143` 刻意 `ExecuteVerticalOnlyMovement`），措辭暗示有 bake 時會套用。
- `docs/21` §1.3「false pulse 幀讓 external Reaction 可被消費」——Reaction 已退役。
- `docs/19` §3.2「`EnemyPunchDefinition` 無 Bake」——已有 Bake（F-02）。

---

## E. Repeated Decision Rules

| 規則 | 版本數 | 位置與值 |
|---|---|---|
| 目標／命中對象合法 | **5** | 玩家選目標；近戰；投射物；AoE；AI（F-03） |
| 「屍體」的處置 | **4** | 選目標：不合法；近戰：不扣血但播命中音；投射物：透明；AoE：不扣血但 Slow |
| 自己排除 | 3 種寫法 | `root == root`（近戰、AoE、選目標）；`IsChildOf(_ownerRoot)`（投射物）——現行 hierarchy 下等價 |
| 進入戰鬥 | **2** | 玩家 6／9＋互動＋5 s；AI 8／12（F-04） |
| 目標距離 | 3D vs 水平 | 玩家保留檢查 3D；AI 兩顆元件水平；soft target 範圍 3D、角錐水平 |
| 角錐 alignment | 2 | 選目標：水平·3D；soft target：水平·水平（F-06） |
| 「此擊朝哪」 | 2 | 玩家：段落承諾；AI：持續 combat facing（F-02） |
| 效果落點的目標 | 2 | facing：段落邊界選一次；效果：每帧重選（F-01） |
| Release 一次的範圍 | 2 | `ActionState` 每段一次；`MeleeHitboxSink` 每次執行一次（F-09） |
| 每目標結算一次 | 3 種鍵 | 近戰 `CharacterHealth`；AoE root `Transform`；投射物單發 ⇒ ① 各自正確 |
| 時長 fallback | 2 | `ActionPhaseEntry.FallbackDuration`（authored）；`HurtState.FallbackDuration = 0.4`（const，`RollState` 同形）⇒ ① |
| 無瞄準 fallback | 3 | emitter rotation／root forward／不需要（F-11） |
| 中斷授權 | **4 條路徑** | config；`Interruptible`；`CanReenter`；Death 型別例外（F-07；前兩條與最後一條已裁決） |
| 死亡收手 | 7 個 consumer | Facing、HeadLook、AIMovementSource、HealthBar、DeathArbiter、DeathState、MotionDriver ⇒ **已裁決**（§H） |
| Slow 倍率 | 1（共用常數） | `TemporaryGameplayEffectState.SlowMovementSpeedMultiplier`；時長兩份（投遞端，policy） |

---

## F. Data Plumbing

### F-P1：facing 承諾（pull，3 跳）
```text
AimResolver → ActionState._releaseContext → FullBodyStateMachine.TryGetActiveFacingCommitment → CharacterFacingSource（4.6）
```
**判斷**：合理。FSM 只轉呼、不新增權威（`docs/15` M3）；facing 必須在順序 4 之後才能知道承諾。⇒ **保持**。

### F-P2：效果落點（每帧刷新、release 交付）
```text
AimResolver（帧快取）＋ CombatContext.SoftTarget → ActionState.RefreshEffectContext → Release(in) → sink
```
**判斷**：合理邊界（D4：sink 不解方向，方向由 lifecycle 交付）。`AimResolver` 以 `Time.frameCount` 去重，每帧至多一次射線（`docs/41` 已審）。問題不在跳數，而在**文件沒寫它存在**（F-01）。⇒ **保持 pipeline，fold back 契約**。

### F-P3：別人的死活（直接讀元件）
```text
候選 collider → GetComponentInParent<CharacterHealth>().IsDead（玩家選目標、投射物）
```
**判斷**：合理——黑板只有「我自己」的 `Survivability`，別人的狀態沒有黑板管道；讀已 commit 的 `IsDead`（不重算血量）。缺的是 AI 那兩顆元件**沒有走這條**（F-03）。⇒ **consumer 改走既有規則**，不需要新管道。

### F-P4：AI 目標（兩份序列化參照 → 兩份距離）
```text
SampleScene override ×2 → AIMovementSource.target / AIInputSource.target → 各自水平距離 → W10／W15 用測試綁在一起
```
**判斷**：時序上必須兩顆元件（A27），但參照不必兩份。⇒ **測試擴到場景**（低風險）或 **consumer 改問 owner**（F-05）。

### F-P5：受擊 → 交戰（只有玩家接了）
```text
CharacterHealth.PublishTo（0.5）→ Survivability.JustTookDamage ─┬→ HurtState
                                                                ├→ PlayerCombatContextSource（2.6）
                                                                ├→ AudioController
                                                                └✗ AIMovementSource（2.5）不讀
```
**判斷**：canonical source 存在、時序上 AI 也讀得到（0.5 < 2.5），只是沒接。⇒ 與 `docs/47` F-P2 同型：「不是 plumbing 太多，是沒送到 consumer」。是否要接屬行為裁決（F-04）。

---

## G. Top Refactor Candidates（最多 5 個，皆未實作）

### G-1 方向承諾的契約 fold back（純文件＋測試命名，零行為變更）
```text
Current   程式：facing 段落一次、效果每帧；文件：同一份（ADR-007 D5、design-doc、docs/15/32/33）
→ Problem  Trial ADR 的 ⛔ 條文與現行程式字面衝突；TD5B 名稱與訊息是舊契約、靠 t=0 release 才綠（F-01）
→ Canonical concept  「facing 承諾」與「效果落點」兩個概念，各有作用域
→ Desired ownership  不變：ActionState 擁有兩者
→ What should disappear  「同一份」的敘述；TD5B 的舊斷言訊息
→ What should remain  facing 段落快照（抗抽搐）；效果 release 當下（打得到移動目標）
```
ADR-007 是 Trial ⇒ 記 §11 修訂即可，**不開新 ADR**。效果端「每帧重選 vs 鎖定目標身分」需先裁決（Q1），再決定 TD5B 怎麼寫。

### G-2 敵人攻擊的方向語意
```text
Current   敵人無 IAimSource ⇒ 無承諾 ⇒ 整段 persistent facing 追蹤；Bake 前衝跟著轉
→ Problem  同一個「此擊朝哪」玩家與 AI 兩種答案；docs/19 陳述相反（F-02）
→ Canonical concept  Action commitment（ADR-007 D5）
→ Desired ownership  ActionState（不變）；缺的是 AI 的 commitment 來源（docs/15 §16-8）
→ What should disappear  docs/19 §3.2 的錯誤前提；EnemyPunch 上無作用的 Targeting
→ What should remain  persistent combat facing（非出手期間）
```
⚠️ 若選「給敵人承諾」：會新增一個 `IAimSource`（或等價 origin seam）實作，屬 ADR-007 D4 範圍——Trial 可修訂、需使用者裁決，不得直接實作。

### G-3 單一「目標合法」規則，涵蓋 AI 與 sink
```text
Current   五份規則；AI 不看目標死活；近戰對屍體播命中音；AoE 對屍體 Slow（F-03）
→ Canonical concept  Target validity（docs/15 §16-7 已為玩家建立）
→ Desired ownership  一個純函數（比照 IsHostileCandidateLegal 的「無 CharacterHealth ＝ 合法」契約），AI 兩顆元件與 sink 共用
→ What should disappear  AI 對屍體的 Approach／Strafe／attack request；命中音不看 ApplyDamage 結果
→ What should remain  「我死了所以我收手」的 consumer 端檢查（已裁決，與本項正交）；投射物「屍體透明」
```
行為變更 ⇒ 需裁決 Q3；建議同時決定是否引入陣營（目前只有 1 隻敵人，可延後）。

### G-4 AI 受擊即交戰（若裁決要對稱）
```text
Current   AI 交戰只看距離；玩家受擊即交戰（F-04）
→ Desired ownership  AIMovementSource（唯一 producer，不變）讀自己的 Survivability.JustTookDamage
→ What should disappear  「在 aggro 外被打仍站著」
→ What should remain  aggro 半徑與 leave hysteresis；W15 的距離分層
```
零新欄位（A5 不變），但屬行為變更。

### G-5 中斷 permission 的第四條路寫進契約
```text
Current   同型重入不經 config；A38 斷言一個不被讀的資產值（F-07）
→ What should disappear  A38 的 Hurt→Hurt 資產斷言（改成斷言 CanReenter 行為）
→ What should remain  config 管跨型別 permission；CanReenter 管同型；Death 例外（ADR-009 D4）
```
Living Doc（`docs/26` 或 dev-spec §3.3）補一列即可；ADR-004 為 Accepted，不改。

**刻意不列入**：F-08（`ActionRequestTarget` 改名）——成本是資產面（腳本改名＋`.meta`），收益只是命名；等真的要做陣營（G-3 延伸）時一起決定標記該叫什麼。F-09——近戰加連段時再一起處理即可，今天不可達。

---

## H. Things That Look Fragmented But Should NOT Be Merged

| 看起來重複 | 為什麼保持分離 |
|---|---|
| `_releaseContext`（facing）vs `_effectContext`（效果） | `docs/11` §14 使用者裁決：合併成一份必然二選一——facing 每帧追 ⇒ 抬手抽搐；效果段落快照 ⇒ 側移目標必定打不中（≈1.7 m vs 膠囊 0.64 m）。要修的是**文件**（F-01），不是程式 |
| 黏性 combat target vs 無記憶 soft target | ADR-007 2026-09-11：一個回答「在跟誰打」、一個回答「鏡頭大致瞄誰」；共用 producer／mask／buffer，語意分開 |
| `AimResolver` 的瞄準點 vs soft target | 前者是幾何射線（打牆、打地面也成立），後者是角色候選；`CameraConeSoftTarget` 只在角錐內才覆寫 |
| 玩家與 AI 兩個 `CombatContext` producer | ADR-007 D3：每角色**唯一 active** producer，A5 白名單兩檔；不是第二份 target 清單 |
| 每個 consumer 各自 `!IsDead` | `docs/19` §3.1 使用者選擇：否決「死亡時停止產生 `CombatContext`」（會連帶影響其他 consumer）⇒ 死亡由 consumer 收手。與 F-03 的「目標死活」是**不同概念**（自我抑制 vs target validity） |
| `DeathArbiterSource.BlockInput` vs Facing 死亡否決 | 封鎖輸入 ≠ 封鎖所有行為（`docs/19`）；`CombatContext` 不經輸入 |
| `IsDead`／`JustDied`／`JustTookDamage` | 狀態 vs 兩種單帧事件；由同一寫入者同一發布點產生，讀者不必自己做邊沿偵測 |
| `HurtState` vs `ActionState` | `docs/26` Model B：受擊是被動反應、不是出手（`RollState` 先例） |
| 近戰窗／投射物 sweep／AoE 當帧 overlap | 三種投遞形狀，命中時刻本來就不同；A26 只要求時機來自 lifecycle、不來自 VFX |
| 每個 sink 各自的 `damage`、兩份 `slowDuration` | authored policy 值（`docs/11` §7.4：時長是投遞端調整值） |
| `ActionState` 冷卻 vs `AIInputSource` retry cadence | ADR-004 D2／A27：一個是「能不能」，一個是「多久再問一次」 |
| `attackRange` vs `maximumEngagementDistance` | 兩顆元件、兩個問題（願意出手 vs 停下來）；W10 證明兩者必須相等並釘住 ⇒ 正確的「受測試約束的重複」 |
| `Interruptible`（per phase）vs config `CanBeInterruptedBy` vs Priority | ADR-004 D6 ＋ ADR-009 D4／D5：資格、競爭、自我承諾三件事；Death 例外是 lethal 層 |
| Slow 只住在 `AIMovementSource` | `docs/11` §7／A25：在 intent producer 乘係數，下游全部自動正確——這是本系統對抗 fragmentation 最好的範例 |
| combat speed cap 看 `InCombat` 不看 mixer | `docs/11` §13 已知並接受（F-04 只補問「沒有敵人的出手」算不算） |
| `TD5` 的新斷言 ＋ `AC1`／`AC2` | 已守住新契約的兩半；要改的只有 `TD5B` |

---

## I. Architecture Health Verdict

> **Action／Combat 的「決策」是健康的——每個決定只有一個回答者，而且大多被測試釘住。碎裂不在決策，在「誰會去問」：玩家路徑完整地問，AI 路徑常常沒問；文件則停在拆分之前。**

**支持「健康」的證據**
1. 單一回答者全部機器化：出手（A24／A27）、後果（A5／A26，ADR-009 D2）、轉向（A31／A32／A33）、出貨 permission（A38）。
2. `docs/26` Model B 把最大的 special case（受擊借用 Action）整個移除，連帶刪掉兩處「如果是 Reaction 就反過來」（A37 守住不復活）。
3. Slow 證明了「在 producer 乘係數」可以讓下游零認知自動正確（A25）。
4. 刻意的重複用關聯式測試管理（W10／W15），而不是靠紀律。

**支持「局部碎裂」的證據**
1. 承諾拆兩份是正確的決定，但 Trial ADR、design-doc 與三份導讀仍寫「同一份」，`TD5B` 靠 t=0 release 才綠（F-01）。
2. 「此擊朝哪」「目標還合法嗎」「被打算交戰嗎」三個概念，玩家側有、AI 側沒有或不同（F-02／F-03／F-04）；`docs/19` 甚至把 AI 側描述成已具備。
3. 「這一下算不算」在 sink 端有零星重新判斷：屍體四種處置、近戰自帶 release-once（F-03／F-09）。

**趨勢判斷**：與 `docs/47` 不同。那裡的碎裂來自「A/B 實驗結束沒回收」；這裡來自**「玩家先行的 slice，AI 只補一半；決定寫進子系統分卷，沒回寫到 ADR」**。建議把兩件事列入 slice 收尾條件：①動到 Trial ADR 某一條的語意時，同一工作包補 ADR §11；②玩家側新增一條 gameplay 語意（target validity、受擊即交戰、承諾）時，明寫「AI 側：同樣／刻意不同（理由）／尚未（登記）」。

### I.1 不可達 vs 可達（依出貨事實）

| Finding | 今天玩家碰得到嗎 | 依據 |
|---|---|---|
| F-01 文件 | — | 文件／測試問題 |
| F-01 效果重選 | **可**（單一敵人：抬手 0.42 s 內目標離開 25°／12 m） | Fireball／Ice `Targeting: 1`、release 0.35 |
| F-02 | **可**（每一次敵人出拳） | Y Bot 無 `IAimSource`、`usePersistentCombatFacing: 1`、EnemyPunch 有 Bake |
| F-03 繞屍／揮拳 | **可**（玩家死後，重生是手動鍵） | `RespawnController` |
| F-03 互毆 | 不可（場景只有 1 隻 Y Bot） | `SampleScene` |
| F-04 遠距不交戰 | **可**（8–12 m 外施法） | soft target 12、aggro 8、火球 15 m/s × 5 s |
| F-04 空地出手 | 可，只在轉向暫態可見 | X Bot facing 跟移動 |
| F-06／F-09／F-11 | 不可 | 只有 X Bot 有 HeadLook；無近戰連段；X Bot 有 AimResolver |
| F-10 | **可**（翻滾中被打） | Roll → Hurt 不允許 |

### I.2 需要使用者裁決

| # | 問題 | 選項 |
|---|---|---|
| **Q1** | 效果落點在抬手期間：目標離開角錐時改打相機點、或另一個敵人更對準時換目標——是要的嗎？ | (a) 維持現況（「打我現在瞄的地方」）；(b) 鎖定段落邊界選到的**目標身分**、只跟它的位置；目標失效才退回相機點 |
| **Q2** | 敵人出拳整段追蹤玩家（含 Bake 前衝）——要的嗎？ | (a) 接受追蹤，改 `docs/19`；(b) 給敵人段落承諾（`docs/15` §16-8 的 origin seam）；(c) release 前追蹤、之後鎖定 |
| **Q3** | 玩家死後敵人繼續繞屍、揮拳、播命中音——要讓「目標死亡＝不合法」也適用 AI 嗎？ | (a) 是（G-3）；(b) 保持，等陣營／重生流程一起做 |
| **Q4** | 敵人在 aggro 半徑外被打，要不要進入交戰？ | (a) 是（G-4，零新欄位）；(b) 否，寫進 `docs/21` 作為刻意差異 |
| Q5 | 空地按攻擊（無敵人）算不算 `InCombat`？ | (a) 算（現況，`docs/15` §4.1 字面）；(b) 只有「有目標」或「受擊」才算 |
| Q6 | 翻滾中受擊（硬直被丟棄）要不要播受擊語音？ | (a) 播（現況：跟事件）；(b) 不播（跟反應） |
| — | F-01 的文件 fold back（G-1 前半）| 屬已裁決內容的同步，**不需裁決**即可做；`TD5B` 的新寫法要等 Q1 |

### I.3 待量測（Editor 讓給 Traversal session，未執行）

| # | 要量什麼 | 怎麼量 |
|---|---|---|
| M1 | F-01：Fireball 抬手 0.42 s 內轉相機 > 25°，火球落點 vs 身體朝向 | PlayMode probe：場景放 1 隻靜止 Y Bot，X Bot 發 Slot2，第 N 帧把相機 yaw 轉 40°；記錄 release 帧 `_effectContext.AimPoint`、`TryGetFacingCommitment`、投射物初速方向 |
| M2 | F-02：敵人出拳期間的 yaw 追蹤量與前衝方向 | PlayMode：X Bot 在出拳 0.2 s 後以 Walk 橫移；逐帧記錄 Y Bot `transform.forward` 與 root 位移方向，對照「段落邊界方向」 |
| M3 | F-03：玩家死後 30 s 內敵人的行為 | PlayMode：對 X Bot `ApplyDamage(999)`；記錄 Y Bot `_engagementMovement`、`Slot1ButtonDown` 次數、`MeleeHitboxSink` 命中次數、`AudioController.PlayAt(hitAudio)` 次數 |
| M4 | F-04：10 m 外連發 Fireball，Y Bot 的 `InCombat` 與移動 | PlayMode：X Bot 固定在 10 m，每 0.5 s 發 Slot2；記錄 Y Bot `CombatContext.InCombat`、`MovementIntent`、`StateType` |
| M5 | F-10：翻滾中受擊的聽感 | 人工 Play（只能聽） |
| M6 | F-01 測試面：`TD5B` 改為出貨 release 時點（0.35）後是否轉紅 | EditMode：複製 TD5B、`releaseNormalizedTime = 0.35`、在 release 前失效目標；預期（推導）`AimPoint == 相機點` |

---

## 附錄：本次讀過的關鍵 call site

`ActionState.cs:137-187,189-231,252-283,285-305,347-370,389-415,497-598,650-700` ｜ `HurtState.cs:1-146`（全） ｜ `DeathState.cs`（全） ｜ `FullBodyStateMachine.cs`（`Tick`／`EvaluateInterrupts`／`EvaluateTransitions`） ｜ `BaseState.cs:69-84` ｜ `CharacterFacingSource.cs`（全） ｜ `PlayerCombatContextSource.cs`（全） ｜ `AIMovementSource.cs`（全） ｜ `AIInputSource.cs`（全） ｜ `CharacterHealth.cs`（全） ｜ `SurvivabilityData.cs:13-80` ｜ `CombatContextData.cs` ｜ `ActionRequestTarget.cs`／`ActionReleaseContext.cs`／`IAimSource.cs`／`IActionLifecycleSink.cs`／`ActionSlot.cs`／`ActionDefinitionSO.cs`（全） ｜ `TemporaryGameplayEffectState.cs`（全） ｜ `MeleeHitboxSink.cs`／`ThrowProjectileEmitter.cs`／`ThrownProjectile.cs`／`GroundEffectSink.cs`（全） ｜ `WeaponVisibilitySink.cs`／`ActionAudioSink.cs:77-91` ｜ `AimResolver.cs`（全） ｜ `HeadLookController.cs:38-66` ｜ `WorldSpaceHealthBar.cs:85` ｜ `LocomotionModel.cs:280-293` ｜ `AudioController.cs:105-125` ｜ `CharacterPipelineRunner.cs:31-34,95-108,151-156,254-260,285-373,397-477` ｜ `ArchitectureRegressionTests.cs:500-560,790-1240,1640-1880` ｜ `PrefabWiringTests.cs:248-300,740-910` ｜ `ActionStateTests.cs:780-900,1064-1100` ｜ `X Bot.prefab`／`Y Bot.prefab`／`Projectile_Fireball.prefab`／`*Definition.asset`／`XBotSpellDirectionalSpeedProfile.asset`／`SampleScene.unity:7835-7890`／`TagManager.asset`／`DynamicsManager.asset`（唯讀 grep） ｜ 治理：ADR-004 §3、ADR-005 §2、ADR-007 §4／§11、ADR-009 §2／§4.2、`docs/11` §7／§13／§14／§15、`docs/15` §4／§16、`docs/19` §3、`docs/21` §1、`docs/26` §C.4／§E.3／§J
