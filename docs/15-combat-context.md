# 15 — Combat Context（無瞄準鍵的戰鬥朝向）

> **狀態**：🟡 **已採納，實作基線（2026-09-06 使用者裁決）** —— 本檔是 **ADR-007（🟡 Trial）的 Living Spec**，
> 承載其 **S3**。⛔ **不另開 ADR-008**：Combat Context 是 ADR-007 **D3 的實例化**（facing authority 的 target source），
> 不是另一套架構哲學；黑板 ownership 寫在 ADR-007 D3 的補充條款，細節全在本檔。
> ⚠️ **Trial 之下 ⇒ 本檔不凍結**：實作若推翻這裡的形狀，**改本檔即可**，只有推翻 ADR-007 D1–D5 才回頭改 ADR。
> **前提裁決（使用者 2026-09-06）**：`<Mouse>/rightButton` **保留給 Block ／ Guard**；
> **不要獨立的 Aim Mode**——玩家只負責移動、攻擊、施法、格擋，**朝向由戰鬥語境自動取得**。
> 本檔是診斷 ＋ 設計提案，**本輪不實作任何 runtime**。
> 對應：`docs/ADR/007-direction-authority.md`（🟡 Trial）§9.3 的 **S3**；路由問題見 §12。

---

## 1. 現況：facing 的唯一來源是一顆**按住**的鍵

S1／S2 落地後，`MotionDriver.RequestFacing` 的方向只可能來自三條路徑：

| # | 路徑 | 觸發條件 | 移除右鍵後 |
|---|---|---|---|
| ① | `AimResolver.Update()` ＝ aim-hold | **按住右鍵** | ❌ **整條消失** |
| ② | `ActionState` 的段落承諾（S2） | 出手期間（0.4–1.6 秒） | ✅ 保留，但**只在出手瞬間** |
| ③ | 無 request ⇒ `MotionDriver` slerp 向 `MoveDirection` | 永遠 | ✅ 保留（＝free locomotion） |

⇒ **拿掉右鍵，自由移動時的 combat facing 直接歸零**，只剩出手那一瞬間會轉。
這也是為什麼 ADR-007 的 Acceptance **A** 目前寫成「按住瞄準 ＋ 按 A」——
**那句話把驗收綁在一個即將退場的輸入模式上**（見 §8）。

📌 **關鍵觀察**：問題不在「右鍵該不該存在」，而在
**「facing 的來源被實作成一個輸入模式，而不是一個語境」**。
只要來源是「按著才有」，任何按鍵配置的改動都會動搖朝向系統——這是耦合，不是鍵位問題。

---

## 2. Facing 來源候選比較

> ⚠️ 先分清兩件常被混為一談的事：
> **(a) authority ＝「這一帧朝哪」的決定者**（有優先序，恰好一個結果）
> **(b) selection input ＝「目標是誰」的判斷依據**（可以有很多個，合成一個目標）
> 使用者列的八個來源裡，**只有四個是 (a)，其餘是 (b)**。把 (b) 誤當 (a) 就會長出優先級表打架。

| 來源 | 是 (a) 還是 (b) | 判斷 |
|---|---|---|
| **Action commitment**（S2 已存在） | **(a) 最高** | 出手期間不得被任何東西拉走——這是 ADR-005／`docs/11` §8.3「揮擊途中甩相機不該一直轉」的既有裁決，**不重新討論** |
| **explicit combat target**（lock-on） | **(a)** | 玩家**明講**的意圖，理當壓過自動推導。⚠️ 目前不存在（`docs/10` 仍未實作）⇒ 本輪只在優先序**留位**，不實作 |
| **combat context target**（自動推導） | **(a)** | 本提案的重點。⚠️ 必須**黏性**（見 §4.3），否則會變成「每帧重選最近敵人」＝朝向抖動 |
| **movement direction** | **(a) 最低（fallback）** | 非戰鬥時的預設，**已經是現行行為**，不需要新機制 |
| **soft target**（相機錐 SphereCast） | **(b)** | ⛔ **不該是 authority**。它是「玩家現在大概想打誰」的**選擇依據**之一，而且是**當帧無記憶**的——直接當朝向來源就會隨相機晃動而抖 |
| **camera forward** | **(b)，且僅限間接** | ⛔ **絕對不該是 facing authority**。理由有二：<br>① ADR-007 §2 已把相機方向排除在任何角色的方向真相之外（相機是 Presentation 私有）；<br>② 讓身體跟著相機轉＝**永久 TPS strafe**，使用者明確表示不要（§3 的「不要無故進入 TPS 式永久 strafe」）。<br>它唯一合法的角色是**餵給目標選擇**（玩家看向誰＝大概想打誰） |
| **最近攻擊／最近被攻擊的敵人** | **(b)** | 不是獨立 authority，而是 combat target 的**持久化與消歧規則**（見 §4.3）。它正是讓「不按鍵也覺得角色懂我」的關鍵——**打過誰就記得誰** |
| **AI target** | **(a)，敵人側** | 敵人的 combat target 就是它的 AI target。⇒ **玩家與 AI 共用同一條 seam、不同 producer**（§6） |

---

## 3. 推薦的 priority（authority 層級，共五級）

```
① Action commitment          出手期間（S2 已落地，不動）
② explicit target            lock-on —— 未來才有，只留位
③ movement direction         預設（含**戰鬥中**）
④ 維持現朝向                 靜止、無承諾、無輸入
```

> 🔄 **2026-09-08 已落地的修正**：原本這裡有第 ③ 級「combat context target（戰鬥中持續面向敵人）」，
> 已依 §19 的操作模型**移除**。`CharacterFacingSource` 自此**完全不讀 `CombatContext`**——
> 朝向只有兩個來源：**出手期間的承諾**，以及**移動方向**。
> ⇒ Movement 與 Facing **只在出手期間分離**，這正是 8-way 該出現的時機（`docs/13` §10）。

**與使用者提案的差異只有一處**：使用者寫的是
`Action commitment > explicit combat target > combat context target > movement-facing fallback`——
**完全同意**，本檔只是把 ⑤（靜止時不要亂轉）補上，並把 camera forward／soft target／threat memory
**移出優先序、改列為 ③ 的輸入**。這個移動是本節的全部價值：

> **優先序只該有「決定者」。把「依據」也塞進優先序，就是 `docs/09` §4-D3 當年禁止的那張 facing 優先級表。**

---

## 4. Combat Context 最小規則

### 4.1 進入（任一成立）

| 條件 | 為什麼夠 minimal | 資料哪裡來 |
|---|---|---|
| **玩家發動攻擊／施法** | 最強、最不可能誤判的訊號 | `data.Intent.RequestedActionSlot != None`（管線順序 2 已寫好，**零新機制**） |
| **受到傷害** | 被打當然算交戰 | 🔄 **2026-09-14 seam migration**：`data.Survivability.JustTookDamage`（`CharacterHealth` 於管線順序 0.5 發布）。<br>原本讀 `ActionRequestTarget.PendingSlot`——因為當時受擊是一個 Action。受擊改為 `StateType.Hurt`（`docs/26` Model B）後那個 mailbox 不再承載受擊，**若不遷移，「被打」會安靜地停止刷新交戰計時 ⇒ 被圍毆時反而提早脫離戰鬥語境，而且沒有任何錯誤訊息**。<br>新來源涵蓋面更廣：**任何來源的傷害**都算互動，不再限於「記得戳 mailbox」的那些。<br>⚠️ 這只是 seam 遷移，**不等於**完成 §16-7 的 target validity 工作（見 §4.2）。 |
| **敵對目標進入威脅半徑** | 讓「走近敵人就進入備戰」成立 | 一次 `OverlapSphereNonAlloc` ＋ 既有的 `ActionRequestTarget` 標記 |
| ~~lock-on~~ | 未來才有 | — |

### 4.2 離開（**全部**成立才離開）

> 🔴 **離開規則比進入規則重要。** 進入錯了只是早一點面向敵人；
> **離開錯了會讓玩家在想跑路／探索時被黏在一個看不見的敵人身上**——那是最惱人的失敗模式。

- **距離**：所有敵對目標都超出 `leaveRadius`（**必須大於** `enterRadius` ＝ 遲滯）
- **時間**：距離上一次敵對互動（出手／被打）超過 `disengageSeconds`
  🔄 **2026-09-06 fold-back**：**純靠走近敵人進場時並不存在「上一次互動」** ⇒ 計時基準為
  **「首次進入半徑的時間」**。不補這條的話，走近但沒交手的角色會**永遠無法滿足時間條件**、卡在戰鬥語境裡。
- **合法性**：沒有任何合法目標
  🔄 **2026-09-06 fold-back**：目前**沒有 Health／Death 契約** ⇒ 「死亡」只能以
  **被摧毀／GameObject 停用／`ActionRequestTarget` 被 disable** 表示。若死亡物件仍 active，本層判斷不出來。
  ⛔ **不為此提前發明 Health 系統**；等真的有死亡流程再回來接（§16-7）。
  🔓 **2026-09-14 狀態更新**：該契約**現在存在了**（ADR-009：`Survivability.IsDead`），
  ⇒ §16-7 的前置條件已解除。但**使用者 2026-09-14 明確裁決本輪不做**——
  受擊 seam 的遷移（§4.1）是 Hurt／Death migration 的必要工作，
  target validity 改讀 `IsDead` **不是**，不得把 targeting scope 拉進那一輪。

  ✅ **2026-09-15 已落地，§16-7 結案。** 新增單一判準
  `PlayerCombatContextSource.IsHostileCandidateLegal(candidate, selfRoot)`，
  由**三處共用**：combat target 掃描、soft target 掃描、保留目標檢查
  （原本三處各自重複 `null / isActiveAndEnabled / root` 三行，正是「只修一半」的溫床）。
  - 查證結果：`DeathState`／`CharacterHealth` **都不會**停用 collider 或 `ActionRequestTarget`
    ⇒ 屍體通過舊版全部檢查、仍是合法目標。錄影可見玩家朝屍體轉、戰鬥語境不脫離。
  - ⚠️ **缺少 `CharacterHealth` 視為合法**——訓練樁、可互動物件「沒有生命值」不等於「死了」。
    **不得把缺席當成死亡**（`TC_D3` 守這一條）。
  - 測試：`CombatContextTests.TC_D1`（不得被選中）／`TC_D2`（已保留的目標必須被放掉）／`TC_D3`（反向守門）。
  **§16-7 維持開啟，等單獨排程。**

⚠️ **遲滯必須是時間感知的，不能只有距離**——這是本輪剛踩過的坑：
`AIMovementSource` 的 `distanceHysteresis 0.15 m` 在 5.66 m/s 下只有 **1.6 帧**（`docs/14` §7-5）。
戰鬥語境的半徑遲滯若照抄距離門檻，會得到一模一樣的高頻震盪。

### 4.3 目標選擇與保留（黏性）

**選擇**（進入語境、或目前目標失效時各算一次）：
1. 候選 ＝ 威脅半徑內、持有 `ActionRequestTarget`、不是自己（**沿用既有標記，⛔ 不新建 `ITargetable`**——`docs/09` §4-D4／`docs/10` §3-D3 的既有禁令仍然有效）
2. ~~排序鍵：**最近一次與我互動的時間** → 其次**相機錐內的角度偏差** → 其次距離~~
   🔄 **2026-09-06 fold-back（實作發現）**：**「最近一次與我互動的時間」目前沒有合法資料來源**——
   `ActionRequestTarget` 只有 slot、**沒有攻擊者身分**，玩家 `Intent` 也不帶 target。
   ⇒ **S3a 實際採用：相機錐角度偏差 → 距離**，選定後靠黏性維持。
   「打過誰就記得誰」與「明確打了別人就換目標」**需要一個攻擊者／命中者身分 seam**，
   ⇒ 登記為 §16-6，**等該 seam 真的出現再做**（⛔ 不為此提前發明 target 身分系統）。

**保留（黏性，本節的核心）**：
- 目標**不因為另一個敵人變得更近就改變**
- 只有在以下情況才重選：目標死亡／失效、超出 `leaveRadius`、或**玩家明確攻擊了別人**
- ⇒ 這正是使用者說的「**不要簡單地永遠 soft-lock 最近敵人**」

📌 **相機在這裡的角色**：只當**消歧的次要鍵**（同時打過兩個人時，看誰在畫面中央），
**不改變**已鎖定的目標。相機因此影響「選誰」，但**永遠不影響「朝哪」**——ADR-007 §2 的界線不破。

### 4.4 Action soft-target candidate（2026-09-11）

同一個 `PlayerCombatContextSource` 額外發布一份**每幀無記憶**的 candidate，專供下一個 Action／連段段落
commitment boundary 使用。它不是黏性 combat target，也不改 `InCombat`：12m 內、camera forward 水平半角
25° 內才合格，先比 alignment、再比距離。這兩個數值是第一版 playtest tuning。

只有 producer 做 query／selection；`ActionState` 不碰 Physics，只讀快照。段落開始後 candidate 是否消失
不再影響該段：facing 與 projectile 共用已承諾的位置／方向，下一段才重取。這不是 hard lock-on，亦不修改
WASD、MoveX／MoveZ、一般 idle facing 或 camera mode。

---

## 5. 最小資料形狀

```csharp
// Core/Blackboard/CombatContextData.cs（新 region，比照 MovementIntentData）
public struct CombatContextData
{
    public bool InCombat;          // mode state：ADR-003 D5「mode/toggle 必須進黑板」
    public bool HasTarget;
    public Vector3 TargetPosition; // 世界座標，每帧由 producer 快照
    public bool HasSoftTarget;
    public Vector3 SoftTargetPosition; // 無記憶 action candidate；下一個 commitment boundary 消費
}
```

**刻意不放 `Transform Target`**：
- 黑板持有 Unity 物件參考會讓狀態**不可 snapshot**（ADR-003 D5 §9-L5 的 netcode 前提）
- facing 只需要**位置**；「目標是誰」是 producer 的私有知識
- ⇒ `Transform` 留在 producer 內，黑板只出現值

**寫入者**：每角色**唯一 active** 的 `ICombatContextSource`（玩家版／AI 版），比照 ADR-003 D2 的 producer 契約。
**管線順序**：**2.6**（順序 2 已寫好 `Intent`，可直接讀「本帧是否出手」；早於順序 3／4，讓 model 與 FSM 都看得到）。
**A5 `WriterRules`**：新增一列（`CombatContext` → producer 檔名白名單）。

---

## 6. Facing authority 的落點（＝ ADR-007 S3 重新定義）

**新增一個元件：每角色唯一的 facing source**，是**全專案唯一** `MotionDriver.RequestFacing` 呼叫者。

| 項目 | 內容 |
|---|---|
| 執行時機 | **順序 4.6**（狀態機之後、LateUpdate 位移之前）——因為 ① Action commitment 是 FSM 在順序 4 才確定的 |
| 讀取 | ① FSM 的當前方向承諾 → ② 黑板 `CombatContext` → ③ 黑板 `MoveDirection` |
| 玩家版／AI 版 | 同一支介面、兩個實作（比照 `IMovementIntentSource`）⇒ **敵人終於也有朝向權威**，順帶解掉 `docs/13` §4.1「敵人繞圈面朝切線」 |

**需要的新讀取 seam**：`FullBodyStateMachine` 暴露唯讀查詢
（例如 `TryGetActiveFacingCommitment(out Vector3 worldDirection)`），把 `ActionState._releaseContext` 的水平投影讀出來。

> **為什麼不讓 `ActionState` 自己送**（＝現行 S2 的 `AimResolver.SubmitFacing` 轉送）：
> 那會有**兩個送出者**（Action 一個、combat facing 一個），優先序又回到「當帧誰最後寫」的執行順序巧合——
> 正是 ADR-007 D3 要消滅的東西。**改成 pull：唯一送出者主動問 FSM。**
> ⇒ S2 的薄轉送 `SubmitFacing` 是**過渡設施**，S3 落地時**一併移除**。

---

## 7. `AimResolver` 的責任拆解

**現況：一個類別混了四件事**（S2 之後仍然如此）：

| # | 責任 | 現況位置 | 應該去哪 |
|---|---|---|---|
| 1 | **輸入 gating**（`aimAction`／`IsAiming`／`OnEnable` 註冊） | `AimResolver` | ❌ **刪除**（右鍵交還給 Guard；不新增替代 Aim 鍵） |
| 2 | **world aim point 解算**（相機射線 → 幾何命中點） | `AimResolver` | ✅ **留下**——這仍是合法的 Presentation 查詢（法術打牆壁、無目標時的落點） |
| 3 | **目標選擇**（`ActionRequestTarget` ＋ 角錐 ＋ 取角度最小） | `AimResolver` | ✅ **移到 combat context producer**。黏性 combat target（§4.3）與無記憶 Action candidate（§4.4）由同一 producer 分別發布；ActionState 不再 query 世界 |
| 4 | **facing 送出**（`Update` 的 aim-hold ＋ S2 的 `SubmitFacing` 轉送） | `AimResolver` | ➡️ **移到 §6 的 facing source**；`AimResolver` 自此**完全不碰朝向** |

⇒ **「輸入觸發 Aim」與「解析 Aim／Target」確實不該綁在同一個類別**（使用者的判斷正確）。
拆完之後 `AimResolver` 只剩責任 2，名字也該跟著收斂（例如 `AimPointResolver`）——**它變成一個沒有狀態、沒有輸入、沒有朝向的純查詢**。

---

## 8. 對 ADR-007 的影響（逐條回答）

| 提問 | 回答 |
|---|---|
| **1. ADR-007 哪些段落隱含「要按 Aim 鍵」** | **只有兩處，而且都不是決策**：<br>① §8 **Acceptance A**：「facing 被鎖住時（**按住瞄準**／後續 lock-on）按左…」<br>② `docs/14` §4 S1「做完的定義」：「Play 可見**按住瞄準** ＋ 按 A…」<br>此外 `docs/14` §3 的候選優先序把「**aim-hold 方向**」列為第 ③ 級，需改成 combat target。<br>✅ **D1–D5 五條決策沒有任何一條提到輸入模式** |
| **2. `AimResolver` 是否混合四件事** | **是**，且已逐條拆解 ⇒ §7 |
| **3. 右鍵交給 Guard 後最小要拆什麼** | 只需拆 §7 的 **1 與 4**（輸入 gating ＋ facing 送出）。責任 2 原地不動、責任 3 搬家並加黏性 |
| **4. Combat-facing 的 authority 該在哪一層** | **Core（gameplay）**。理由：它的輸入是「誰是敵人／打過誰／多久沒交戰」——全是 gameplay 事實；且**敵人也要用**（Presentation 的 `AimResolver` 是玩家專屬的，敵人身上根本沒有）。⚠️ 相機錐當**選擇依據**時仍讀 `data.CameraTransform`（黑板既有欄位，`AIMovementSource` 已有先例），**不引入 Presentation 依賴** |
| **5. 玩家與 AI 是否共用同一條 facing seam** | **是。** 同一條 seam（`MotionDriver.RequestFacing`）、同一支 producer 介面、**兩個實作**——完全比照 ADR-003 對 movement intent 的處理。這正是 ADR-007 **D3** 的原文（「每角色恰好一個 facing authority」），本提案只是把它**實例化**，沒有推翻 |
| **6. S1（movement/facing 分離）是否仍成立** | ✅ **成立，而且變得更必要**。S1 說的是「移動方向是世界方向、不得由身體反推」——與朝向來自哪裡完全正交。**沒有 S1，就不可能有「面向敵人但往左走」** |
| **7. S2（`ActionReleaseContext`）是否仍成立** | ✅ **成立，契約一字不改**。改變的只是**承諾方向的提供者**：目前是 `AimResolver.TryGetAimPoint()`，S3 之後應優先取 combat target、無目標才回落到 aim point。**這是換供應商，不是換契約** |
| **8. 是否需要新增／修改 S3 來表示 Combat Context** | **需要，而且這就是 S3 的新定義**。原 S3 寫的是「facing authority 統一」，觸發條件是「敵人需要面向玩家／lock-on／非按住瞄準的持續朝向」——**第三個條件現在成立了**。差別在：原本假設 facing source 可以自己決定朝哪，現在它需要一個**語境輸入** ⇒ S3 從「一個元件」變成「**一個 region ＋ 兩個 producer**」 |

### 8.1 這些變更**不**影響 ADR-007 Trial 的有效性

- **D1–D5 五條決策全部存活，一字不改。** 需求從「按住瞄準」變成「自動戰鬥語境」，而決策文字**完全不受影響**——這是決策寫在正確抽象層級的證據。
- **需要修訂的是 Acceptance A 的措辭與 S3 的定義**（Trial 明文不凍結，記入 §11 修訂紀錄即可，**不需要開新 ADR 來取代 007**）。
- ⚠️ **一個真實的排程後果**：Acceptance **A**（strafe）原本靠右鍵就能驗，現在
  **必須等 S3 落地才能用最終語意驗收** ⇒ **ADR-007 不可能在 S3 之前翻 `Accepted`**。
  過渡處置見 §10 的「暫留」條款。

---

## 9. 第一個可驗收切片（S3a）

**目標**：不按任何鍵，走近敵人 ⇒ 角色面向它 ⇒ A／D 橫移、S 後退；走遠或久未交戰 ⇒ 回到一般移動。

| 做 | 不做 |
|---|---|
| `CombatContextData` region ＋ 玩家版 producer（進入／離開／黏性目標）＋ 順序 2.6 | ❌ lock-on（`explicit target` 只留優先序的位子） |
| facing source 元件（玩家版）＋ 順序 4.6 ＋ FSM 的承諾唯讀查詢 | ❌ **AI 版 facing source**（敵人朝向留 S3b——先讓玩家這條路走通再複製） |
| `AimResolver` 拆掉責任 1／4；責任 3 搬家並加黏性 | ❌ Block／Guard 實作（右鍵**只是預留**，本輪不接） |
| A5 新增一列；新不變量：**唯一 `RequestFacing` 送出者**（機器守住 D3） | ❌ upper-body layer／sword stance／2D mixer／速度調整 |

**驗收（＝ ADR-007 Acceptance A 的新語意）**
1. 在 Combat Context ＋ 有目標：**Facing = target**、`MovementDirection` = 玩家輸入的世界方向、**A／D 橫移、S 後退，全程不按任何鍵**
2. 不在 Combat Context／無目標：回到 free locomotion，**facing 跟隨移動方向**，⛔ 不得出現永久 TPS strafe
3. 走遠 ＋ 超過 `disengageSeconds` ⇒ **確實離開**戰鬥語境（這條最容易漏，要刻意驗）
4. 出手期間：**S2 的段落承諾仍然壓過 combat target**（甩相機不會讓角色一直轉）

---

## 10. 右鍵 Aim 的退場清單

| 接線 | 處置 | 誰執行 |
|---|---|---|
| `AimResolver.aimAction`（內嵌 `InputAction` → `<Mouse>/rightButton`，序列化在 **X Bot** prefab） | **刪除欄位** ⇒ prefab 的孤兒序列化欄位下次存檔自動消失 | 程式：AI／資產：Unity 自動 |
| `AimResolver.IsAiming`／`OnEnable`／`OnDisable` 的 action 註冊 | 刪除 | AI |
| `AimResolver.Update()` 的 aim-hold facing 送出 | 刪除 | AI |
| `AimResolver.SubmitFacing`（S2 過渡轉送） | S3 落地時刪除 | AI |
| `softTargetMask`／`softTargetRadius`／`softTargetMaxAngle`（X Bot 現值 25°） | **搬到 combat context producer** ⇒ 需要在 Inspector 重填 | **使用者**（AI 不碰 prefab） |
| `docs/09` §4-D3(c) 第 6 點「請求來源＝`AimResolver`：`IsAiming` 為真的每一帧呼叫 `RequestFacing`」 | **標記為被本提案取代** | 文件 |
| `docs/10` §4.3「`RequestFacing` 的唯一送出者仍是 `AimResolver`」 | 改為「唯一送出者是 facing source」——**規則不變，只換執行者** | 文件 |
| ADR-007 §8 Acceptance A ／ `docs/14` §4 的「按住瞄準」措辭 | 改寫為 §9 的新驗收語意 | 文件（本輪已改） |

> **⏳ 暫留條款**：Guard 尚未實作 ⇒ 右鍵目前**沒有別的用途**。
> 建議 **S3a 落地前保留現有綁定當作驗證治具**（零成本），**S3a 一落地立刻拆除**。
> ⛔ 不得因為「留著也沒差」而讓它活過 S3a——那就是本提案要消滅的耦合。

---

## 11. Combat Context ≠ Sword Equipped（與 ADR-006 的界線）

**同意使用者：這是兩件事，且必須保持兩件事。**

| | Combat Context | Sword Equipped ／ Stance |
|---|---|---|
| 是什麼 | **gameplay 狀態**：我現在在打架、目標是誰 | **Presentation ／ 裝備狀態**：手上有沒有劍、播哪個姿勢 |
| 誰擁有 | Core producer（§5） | `WeaponSocket`（既有）＋ ADR-006 的上身層 |
| 誰消費 | facing source、（未來）AI 攻擊性、（未來）上身層權重 | 只有 Presentation |

🔴 **如果把兩者綁成一個布林，會立刻付出的代價**：
徒手戰鬥、法術戰鬥、換武器都會變成「戰鬥狀態」的分支——**每加一種武器就動 gameplay 狀態機**。

**第一版建議（與使用者傾向一致，本輪不實作）**：
- 角色**本來就持劍**，不做 draw／sheathe（那是 polish）
- 施法時若與 spell 動畫衝突 ⇒ **由 Action 身分（`ActionSlot`）決定暫時隱藏**，**不是**由 Combat Context 決定
  （理由同上：武器可見性是「現在在演什麼」，不是「現在在不在打架」）
- 劍看不到的**真因是材質重映指向不存在的 GUID**（`docs/13` §1），與本提案完全無關，隨時可修

---

## 12. 路由：✅ **已裁決（2026-09-06）—— 不開 ADR-008**

使用者裁決：**Combat Context 是 ADR-007 D3「單一 facing authority」的實例化 ＋ 它的 target source，
不是另一個獨立的架構哲學** ⇒ 依 CLAUDE.md 反 ADR 爆炸原則，**不開新 ADR**。

- **判準①（新黑板 region ＋ ownership）的內容寫在 ADR-007 D3 的補充條款**：
  唯一寫入者＝每角色唯一 active 的 combat context producer；⛔ 禁止其他系統自建 `isInCombat`。
- **本檔自此是 ADR-007 的 Living Spec**（與 `docs/14` 並列），不再是提案。
- 未來若 Combat Context 長出與朝向無關的重大職責（AI 攻擊性策略、戰鬥音樂、遭遇系統），**再重新評估是否需要獨立 ADR**——
  那時它才真的離開了 D3 的範圍。

---

## 13. S3a 契約面（最小改動清單）

> 格式比照 `docs/14` §2。**新增 4 檔、修改 8 檔**。⛔ 不做 S3b（敵人 facing）、lock-on、Guard。

### 13.1 新增（Runtime）

| # | 檔案 | 內容 |
|---|---|---|
| N1 | `Core/Blackboard/CombatContextData.cs` | §5 的 struct：黏性 combat context＋無記憶 soft candidate。**純值型別，⛔ 不放 `Transform`** |
| N2 | `Core/Combat/PlayerCombatContextSource.cs` | 進入／離開／黏性目標（§4.3）＋ Action soft candidate（§4.4）。**寫黑板的唯一者**。零 GC：`OverlapSphereNonAlloc` ＋ 預配置緩衝 |
| N3 | `Core/Facing/CharacterFacingSource.cs` | §6 的 pull 式解算：① FSM 承諾 → ② `CombatContext` → ③ `MoveDirection`。**全專案唯一 `MotionDriver.RequestFacing` 呼叫者** |
| N4 | `_Project/Tests/EditMode/CombatContextTests.cs` | §14 的測項 |

> ⚠️ **N2／N3 都不要先抽介面**（`ICombatContextSource`／`IFacingSource`）。
> AI 版是 **S3b** 才出現，CLAUDE.md：**第二個使用者出現前不得建立 production abstraction**。
> Runner 直接 `GetComponent<具體型別>()`；S3b 落地時再抽介面（那時才有兩個資料點）。

### 13.2 修改（Runtime）

| # | 檔案 | 改動 |
|---|---|---|
| M1 | `Core/Blackboard/PlayerRuntimeData.cs` | 加一個 region 欄位 `public CombatContextData CombatContext;`。⚠️ **不參與 `ResetTransientState()`**——它是**連續型 mode state**（同 `MovementIntent`／`WalkModeActive` 的理由，ADR-003 D5） |
| M2 | `Core/Pipeline/CharacterPipelineRunner.cs` | 新增**順序 2.6**（combat context，在 2.5 之後）與**順序 4.6**（facing source，在狀態機 4／仲裁 4.5 之後、LateUpdate 之前）。Runner 只呼方法、不認識戰鬥語意（比照 6.5 的 `PresentationPipeline`） |
| M3 | `Core/StateMachine/FullBodyStateMachine.cs` | 新增唯讀查詢 `bool TryGetActiveFacingCommitment(out Vector3 worldDirection)`，轉呼當前 `ActionState`。**FSM 不新增權威，只是把既有承諾讀出來** |
| M4 | `Core/StateMachine/States/ActionState.cs` | 暴露 commitment 水平投影；`CaptureReleaseContext` 依 authored targeting policy，在段落邊界一次選 dedicated `SoftTargetPosition` 或 camera aim。段內不重選，facing 與 release 共用同一份 `ActionReleaseContext` |
| M5 | `Presentation/Camera/AimResolver.cs` | **刪除**：`aimAction` 欄位、`IsAiming`、`OnEnable`／`OnDisable` 的 action 註冊、`Update()`、`SubmitFacing`、`_motionDriver` 快取、soft target 三個欄位與相關的 SphereCast 選擇邏輯（搬到 N2）。**只留** `TryGetAimPoint` 的相機射線幾何解算 ＋ 其純函數。⚠️ 類別自此不碰輸入、不碰朝向、不選目標 |
| M6 | `Presentation/Camera/ThirdPersonCamera.cs` | `IsAiming` 消失 ⇒ **見 §13.4 的取捨，本切片先把 `targetBlend` 固定為 0**（探索取景），並在該處留 TODO 指向 §13.4 |
| M7 | `Core/Pipeline/CharacterPipelineRunner.cs`／`FullBodyStateMachine.cs`／`ActionState.cs` | `AimResolver` 的注入鏈保留（`ActionState` 仍需要 aim point 當 fallback），但**不再用於 facing** |
| M8 | `_Project/Tests/EditMode/ArchitectureRegressionTests.cs` | 新增 **A31**（§14.2） |

### 13.3 資產／Inspector（✅ 2026-09-06 已接線）

- `SampleScene` 的 X Bot scene instance 已加掛 `PlayerCombatContextSource` ＋ `CharacterFacingSource`；
  以 prefab instance overrides 保存，**未修改 prefab asset**
- 🔄 **2026-09-06 fold-back，只搬兩個**：`softTargetMask → targetMask`、`softTargetMaxAngle → selectionConeAngle`（現值 **25°**）。
  ⛔ **`softTargetRadius = 1.2` 不要搬**——它是 SphereCast 的**射線厚度**，與 `enterRadius`（交戰半徑）是不同語意的東西，填進去會得到一個 1.2 公尺才進入戰鬥的角色
- `enterRadius`／`leaveRadius`／`disengageSeconds` 起手值建議 **6 / 9 / 5 秒**（`leaveRadius > enterRadius` 是硬性要求）
- 實際 `targetMask = Enemy`（128），取自 Y Bot 啟用中的 `CharacterController` layer；
  Y Bot 的 `ActionRequestTarget` 也已核對為啟用中

### 13.4 🟡 已知取捨：相機的「瞄準取景」會暫時消失

`ThirdPersonCamera:130` 目前用 `aimResolver.IsAiming` 混合 `aimOffset`／`aimFieldOfView`
（＝按住右鍵時的過肩近景，`docs/09` §6.2 是使用者實機調定過的參數）。

**問題**：相機是獨立 `MonoBehaviour`，**不是 `IPresentationController`、不持有黑板**（`ThirdPersonCamera:111` 的註解明說），
而 A4 `LayerRules` 禁止 Presentation 依賴 `Project.Core.Combat` ⇒ **它沒有合法路徑讀到 `InCombat`**。

**S3a 的處置**：`targetBlend` 固定為 0（維持探索取景），把「戰鬥取景」登記為獨立議題。
**為什麼不順手接**：`aimResolver.IsAiming` 一直是「戰鬥取景」的**代理訊號**；要正名就得決定
①戰鬥中是否永遠近景（feel 決策，不是技術決策）②相機用什麼合法管道讀黑板。
**兩者都不該夾帶在 S3a 裡偷偷決定。** ⇒ 見 §16-1。

---

## 14. 測試與不變量

### 14.1 EditMode（`CombatContextTests.cs`，純函數／可注入時間）

| 測項 | 內容 |
|---|---|
| **T-C1** | 出手（`Intent.RequestedActionSlot != None`）⇒ 立即 `InCombat` |
| **T-C2** | 離開需**同時**滿足距離與時間：只滿足其一 ⇒ 仍在戰鬥 |
| **T-C3** | `leaveRadius > enterRadius` 的遲滯成立：在兩者之間來回不會反覆進出 |
| **T-C4** | **黏性**：目前目標仍合法時，**更近的新敵人不奪取目標** |
| **T-C5** | 目標失效（死亡／停用／超出 `leaveRadius`）⇒ 重選；無候選 ⇒ `HasTarget = false` |
| **T-C6** | facing 優先序：有 Action 承諾時**壓過** combat target；無戰鬥時輸出 = `MoveDirection`；靜止且無目標 ⇒ **不送出 request** |
| **T-C7** | `ActionState` 的承諾在有 combat target 時**取 target**，無 target 才回落 aim point |

### 14.2 架構不變量

| 編號 | 內容 | 實作 |
|---|---|---|
| **A31** 🆕 | **全專案恰好一個** `MotionDriver.RequestFacing` 呼叫者（＝機器化守住 ADR-007 **D3**） | 掃 `Core/`＋`Presentation/` 的 `RequestFacing(`，排除 `MotionDriver.cs` 自身定義，斷言命中檔案數 **== 1** 且為 `CharacterFacingSource.cs` |
| **A5**（既有） | `WriterRules` 新增一列：`CombatContext` → `PlayerCombatContextSource.cs` | S3b 加 AI 版時才追加第二個檔名 |
| **A9**（既有） | Runner 新增兩個階段後仍不得認識 locomotion 概念 | 重跑即可；**facing／combat 不是 locomotion 詞彙** |

### 14.3 Play 驗收（＝ ADR-007 Acceptance **A** 的新語意）

1. 走近敵人 ⇒ **不按任何鍵**，角色面向它；A／D 橫移、S 後退
2. 走遠 ＋ 超過 `disengageSeconds` ⇒ **確實離開**戰鬥語境，facing 回到跟隨移動方向
3. ⛔ 不得出現永久 TPS strafe
4. 出手期間：段落承諾仍壓過 combat target（甩相機不會讓角色一直轉）
5. 兩個敵人時：**打過的那個不會因為另一個更近就被換掉**（黏性）

---

## 15. Codex 實作邊界

**允許改**：§13.1／§13.2 列出的檔案 ＋ §14 的測試檔。
⛔ **不得**：碰 `.asset`／`.prefab`／`.meta`／`.unity`／任何資產；執行 Git；實作 Block／lock-on／敵人版 facing source／upper-body／2D mixer；抽 `ICombatContextSource`／`IFacingSource` 介面；新增第二個 `RequestFacing` 送出者；改 `ActionReleaseContext` 契約；動 `MotionDriver` 的位移路徑（S1 成果）。

**撞到就停下來回報**：發現 `ThirdPersonCamera` 需要讀黑板才能完成（那是 §13.4／§16-1，不在本切片）；發現需要為 combat context 新增第二個黑板 region。

---

## 16. 開放項（登記，不在 S3a）

1. **戰鬥取景**（§13.4）：戰鬥中要不要自動切近景？相機要用什麼合法管道讀 `InCombat`？——**feel ＋ 架構兩個決策，需獨立處理**
2. **S3b：敵人版 facing source** ⇒ 順帶解掉 `docs/13` §4.1「敵人繞圈面朝切線」
3. **lock-on**（`docs/10`）：落地時成為優先序 ②，**不改動 ①③④⑤**
4. `AIMovementSource` 的交戰模式震盪（`docs/14` §7-5）——與本檔無關，仍待處理
5. `docs/11` §8.3 遺留：**無目標時**的 facing 行為（維持現朝向 vs 朝相機正前方）。S3a 採「維持現朝向」，未再議
6. 🆕 **攻擊者／命中者身分 seam**（§4.3 fold-back）：有了它才能做「打過誰就記得誰」與「打別人就換目標」
7. 🆕 **Health／Death 契約**（§4.2 fold-back）：有了它「目標死亡」才判斷得出來
8. 🆕 **S3b 的 origin 來源**：敵人沒有 `AimResolver`，若要為它建立 `ActionReleaseContext` 需要一個角色 origin。
   S3a 的玩家仍沿用 `AimResolver` 所在 Root 的位置，**刻意不提前抽象**

---

## 17. 實作紀錄（S3a，2026-09-06，Codex 落地／本會話覆核）

**程式面完成**，`dotnet build` Runtime ＋ Tests.EditMode 皆 **0 error**（新檔以暫時注入 Compile item 驗證後已還原 `.csproj`）。
改動範圍：**新增 4 檔、修改 9 檔**，全為 `.cs`；**零資產、零 `.meta`、零 Git**。
`RequestFacing` 全專案呼叫者 **恰好一個**（`CharacterFacingSource.cs`），A31 ＋ A5 新列已加。

**2026-09-06 場景整合**：`SampleScene` 的 X Bot 已完成 §13.3 接線與數值設定；
Y Bot 的 target marker／Collider layer 前置條件已由 Editor 回讀核對，場景已儲存。

### 17.1 規格修正（已 fold back）

| # | 原規格 | 實作發現 |
|---|---|---|
| 1 | §4.3 以「最近互動時間」排序目標 | **無合法資料來源** ⇒ 改為相機錐 → 距離，並登記 §16-6 |
| 2 | §4.2「上一次敵對互動」 | 走近進場時**不存在**該時間 ⇒ 基準改為首次進入半徑的時間 |
| 3 | §4.2「死亡」 | **無 Health 契約** ⇒ 只能判斷摧毀／停用，登記 §16-7 |
| 4 | §13.3 搬移 `softTargetRadius` | **語意不同**（SphereCast 厚度 ≠ 交戰半徑）⇒ 刪除該項 |
| 5 | §13 標題「修改 8 檔」 | M7 重複列了 M2–M4；且漏列必須同步的 `ActionStateTests`／`CameraAimTests` ⇒ 實際 **7 個唯一修改 ＋ 2 個測試** |

### 17.2 🟡 一個 Play 要特別看的副作用（本會話覆核時發現）

facing 優先序 ④ 會在**任何移動時**送出 request ⇒ `MotionDriver.ExecuteBaseMovement` 裡
`if (!hasFacingRequest) { slerp 向 MoveDirection }` 這條**對玩家而言自此是死路**（敵人在 S3b 前仍走它）。

**後果**：自由移動的轉身改吃 `ApplyFacingRequest` 的兩個旋鈕——
**`aimFacingAngleDeadzone`（X Bot 現值 8°）＋ `aimFacingTurnSpeed`（10）**，
而不是原本移動轉向的 **12、且無死區**。

⇒ **可能的觀感**：小幅度轉向時身體**不轉**（8° 死區），或轉得比以前慢一點。
**這兩個旋鈕當初是為 Throw 的半蹲釘地姿勢調的**（`docs/09` §4-D3(c) 第 4 點），
現在它們的語意已經變成「角色的轉身 dynamics」。
**若自由移動的轉身變鈍 ⇒ 把 X Bot 的 `aimFacingAngleDeadzone` 往 0–2 調**（Inspector，使用者側）。
📌 這不是 bug，是「單一 facing authority」把兩套轉向規則收斂成一套的**必然後果**——
但它值得被看見，因為調錯地方會很難歸因。

---

## 18. 戰鬥待機朝向（S3c，2026-09-07）

> 使用者實跑 S3a 後的回饋：**「idle 只有頭面向敵人，除非轉動太大；轉動時腳要跟著動」**。
> 現況是 root 每幀被 slerp 向目標 ⇒ **腳釘在地上、身體整個轉** ＝ 滑步。

### 18.1 三段式，界線寫死

| 角度（身體正面 → 目標，水平） | 表現 | 誰負責 |
|---|---|---|
| **小**（< `idleTurnEnterAngle`，建議 **60°**） | **root 完全不轉**，只有頭轉 | `CharacterFacingSource` 不送 request ＋ `HeadLookController` |
| **大**（≥ `idleTurnEnterAngle`） | root 轉向目標，**且腳要動** | `CharacterFacingSource` 送 request；腳的部分見 §18.4 |
| **移動中**（有移動意圖） | **一律面向目標**（＝strafe 的前提） | 既有優先序 ②，**不套用本節門檻** |

⚠️ **門檻只在「戰鬥中且靜止」時適用**。移動中若也套門檻，8 向 strafe 當場失效。

### 18.2 為什麼需要兩個門檻（遲滯）

只有進入門檻的話，root 會**停在門檻角度上**——轉到 59° 就不轉了，看起來像「轉一半放棄」。
⇒ 需要 `idleTurnExitAngle`（建議 **10°**）：**一旦開始轉就轉到底**，低於 exit 才停。
這是本元件唯一允許的第二個旋鈕，理由與 `docs/09` §4-D3 的「兩個構圖參數」同型：
**遲滯在數學上就需要兩個數**，不是加旋鈕的藉口。⛔ 不得有第三個。

### 18.3 頭部 look-at（新 Presentation controller）

| 項目 | 內容 |
|---|---|
| 落點 | `Presentation/Look/HeadLookController.cs`，實作 **`IPresentationController`** |
| 為什麼合法 | 它讀的是 **`Core.Blackboard` 的 `CombatContext`**（Presentation 允許），由 `PresentationPipeline` 在**順序 6.5** 驅動——**動畫已評估完、位移已結算**，正是改骨頭的時間窗（與 `FootIKController` 同一條路） |
| 骨頭 | `[SerializeField] Transform headBone`（`mixamorig:Head`）。**v1 只轉頭**，不做胸椎分攤 |
| 數學 | ⚠️ **用世界空間的「附加旋轉」，不要 `LookRotation` 絕對姿態**——後者需要知道骨頭的 rest orientation，Mixamo 骨架很容易轉歪。作法：算出「目前頭部朝向 → 目標」的水平／垂直偏差，以 `Quaternion.AngleAxis` 疊加到 `headBone.rotation`。<br>🔄 **2026-09-08 修訂**：yaw **不 clamp**，超出 `maxYaw` 一律**回傳「沒有目標」**（理由見下方修正紀錄）。pitch 維持 clamp。 |
| 旋鈕 | `maxYaw`（建議 70）／`maxPitch`（建議 25）／`blendSpeed`（權重淡入淡出）／🆕 `lookDegreesPerSecond`（**角度本身**的追隨速率，預設 360）。**離開戰鬥語境時權重淡回 0**，不得瞬切 |
| 契約 | 對黑板**只讀不寫**（`IPresentationController` 既有契約，A5 已機器守住） |

**2026-09-07 場景整合**：`SampleScene` 的 X Bot scene instance 已在角色 Root 加掛
`HeadLookController`，`headBone` 指向該角色骨架的 `mixamorig:Head`；起手值為
`maxYaw = 70`／`maxPitch = 25`／`blendSpeed = 8`。未修改角色 prefab asset。
📌 `lookDegreesPerSecond` 是 2026-09-08 **新增**的欄位 ⇒ 場景實例會吃程式預設 `360`，**不需要重新接線**。

#### 🔴 2026-09-08 修正紀錄：頭會一瞬間轉到另一邊（使用者 Play 實證）

| | 內容 |
|---|---|
| **症狀** | 目標繞到角色**正後方**附近時，頭部單幀翻轉到另一側 |
| **根因** | `Vector3.SignedAngle` 的值域是 `(-180, 180]`。目標從 `+175°` 越過正後方變成 `-175°` 時，值本身是**連續的角度、不連續的數字**；原本的 `Mathf.Clamp(±maxYaw)` 把這個 350° 的數字跳變**壓成 `+70° → -70°` 的可見翻轉**——clamp 沒有製造不連續，但它把不連續**留在了可見範圍內** |
| **為什麼不修補 wrap** | 偵測 wrap 再補正只是讓斷點換個位置：真正的問題是「**目標已經不在可視角內，卻仍然被表達成一個可視角內的角度**」。⇒ 改成**超出可視角就回傳 false**（＝視為沒有目標），權重循既有路徑淡回 0、頭自然回正。**不連續從構造上消失。** |
| **第二個缺陷（同批修）** | `Tick` 對 `_yaw`／`_pitch` **直接指派、完全沒有速率上限**——`blendSpeed` 只管權重。任何目標位置變化都是瞬間套用。⇒ 補 `lookDegreesPerSecond`（度／秒）＋ `Mathf.MoveTowards`，與 `_weight` 同一種節流方式 |
| **測試** | `CombatContextTests.TC6C` **已改寫**——舊版斷言「clamp 到 ±maxYaw」＝**把缺陷寫成了正確行為**，是這個 bug 能活到 Play 才被發現的原因。新版掃過整圈 360°，斷言「回傳 true 的相鄰取樣之間不得跳變」＋「必須存在回傳 false 的區段」。另加 `TC6D` 守住 pitch 仍 clamp |
| ⚠️ **只靠人工守的部分** | `lookDegreesPerSecond` 的節流發生在 `Tick`（需要 `Time.deltaTime` ＋ 真實 `Transform`），EditMode 測不到 ⇒ **平滑的手感留 Play 驗收**。可自動測的是 ①的結構性連續性，那也是 BLOCKER 的所在 |

#### 🔴 2026-09-08（第二輪）：回正仍然是瞬間的 ＋ 邊界抖動（使用者再次 Play 實證）

第一輪只修了**追隨**，沒修**回正**，因此症狀只好了一半。兩個殘留缺陷：

| | 內容 |
|---|---|
| **回正沒有速率限制** | 失去目標時 `_yaw`／`_pitch` **凍結**，只有 `_weight` 在淡出。場景值 `blendSpeed = 8` ⇒ 權重 1→0 只要 **0.125 秒**，而 `_yaw` 可能停在 70° ⇒ 有效角度以 **≈560°/秒**歸零。⇒ `lookDegreesPerSecond` 這條速率限制**完全沒有作用在回正路徑上** |
| **邊界沒有遲滯** | 「超出 `maxYaw` 視為沒有目標」是對的，但**取得與放棄用同一個門檻** ⇒ 目標停在 ±70° 附近時 `hasTarget` 逐幀翻動 ⇒ 權重上下抖 ⇒ 頭抽搐。**這是第一輪 ① 引入的新抖動源** |

**修法（觀念修正）**：**「回正」是一個角度變化，不是「停用這套系統」。**
⇒ 權重負責「這套 look-at 有沒有在運作」，角度負責「現在看向哪」。

- 無目標時 `_yaw`／`_pitch` 一樣以 `lookDegreesPerSecond` `MoveTowards` 0（與追隨**共用同一條速率**）
- **權重只在角度歸零後才淡出**（`SettledAngleEpsilon = 0.01°`）⇒ 權重淡出時角度已 ≈0，乘起來看不見
  ⇒ **可見的回正時間自此由 `lookDegreesPerSecond`（新欄位、吃程式預設）決定，不再由場景的 `blendSpeed` 決定**
- 新增 `releaseYawHysteresis = 10f`：取得門檻維持 `maxYaw`，**放棄**門檻放寬為 `maxYaw + releaseYawHysteresis`
  ⇒ 遲滯帶內回傳的 yaw 會略大於 `maxYaw`，這是**可接受的**（仍受速率限制 ⇒ 仍連續）
- ⛔ **沒有**把 clamp 改回來。遲滯只是把**放棄的門檻**往外推

**測試**：新增 `CombatContextTests.TC6E`（同一個純函數、兩種上限 ⇒ 72° 在取得門檻失敗、在放棄門檻成功且**不 clamp**）。`TC6C`／`TC6D` 未改動。

⚠️ **這修的是「頭」的抖動，不保證修掉使用者回報的「站小斜坡上一直抽搐」**——
若在**沒有敵人**（脫離戰鬥、權重為 0）時仍然抽搐，那就與 look-at 無關，
下一個懷疑對象是 **Foot IK 對斜坡 collider 的逐幀重解**與 **`CharacterController` 在斜坡上的貼地滑動**
（`GetGravityThisFrame` 每幀施加 `reboundForce`）。⇒ 見下方交辦的分辨實驗。

### 18.4 🔴 「腳要跟著動」不在 S3c —— 它需要資產

大角度轉身要**不滑步**，唯一正解是播原地轉身 clip **並讓 root 的旋轉速率與 clip 一致**——
也就是走 `MotionBakeData` 的旋轉曲線（與 Roll 同一條路徑，`ExecuteBakedCurveMovement` 已支援）。

**素材在、但沒烘**（`docs/13` §2.2／§2.4）：`TurnRt90_Loop`／`TurnLt90_Loop`／`TurnRt180`／`TurnLt180` 四支 clip 都在，
**只有 `Bake_TurnRt180` 烘好**（`RotationFinishedTime 1.5333`，引用次數 0）。

⇒ **S3c 交付的是「小角度不轉頭轉」**；大角度仍是 slerp（比現況好，因為次數大幅減少，但仍會滑）。
**turn-in-place 是獨立切片（S3d）**，前置是**使用者用既有 Editor 烘焙工具把另外三支烘出來**。
⚠️ 這是 `docs/09` §4-D3(c) 早就預告過的那條路：「若死區＋降速仍不可接受，正解是 turn-in-place clip，
**屆時應另立工作包**」——現在就是那個工作包，且它需要資產先到位。

### 18.5 驗收

1. 戰鬥中站著不動、敵人小幅繞行 ⇒ **身體不動，只有頭跟著轉**
2. 敵人繞到側後方（> 60°）⇒ 身體轉過去，**且轉完是對準的**（不會停在 60° 邊界）
3. 移動中 ⇒ 身體一律面向目標（門檻不生效），A／D 仍可 strafe
4. 離開戰鬥語境 ⇒ 頭部**平滑**回正，不瞬切
5. 出手期間 ⇒ Action 承諾仍壓過一切（優先序 ①）

---

## 19. 🔄 操作模型重新定義（2026-09-08 使用者裁決）

> **起因**：Play 之後使用者釐清真正想要的操作體驗，並提出一個關鍵重構：
> **「8-way 是 MovementDirection 與 FacingDirection 分離時的 locomotion representation，
> 不是『進戰鬥就切』的模式。」**
> ⚠️ 本節**只做相容性分析與降級清單，不實作**。

### 19.1 使用者的操作模型

| 情況 | Movement | Facing | 動畫 |
|---|---|---|---|
| 普通移動 | Camera-relative | **移動方向** | 既有 1D |
| **戰鬥中跑路** | Camera-relative | **移動方向** | **既有 1D** |
| 原地攻擊／施法 | 0 | Action 方向／soft target | Action clip |
| **邊移動邊施法** | Camera-relative | **Action target** | **這時才要 8-way** |
| 邊後退邊維持 Action facing | Camera-relative | Action target | 需要 backward／strafe |

Action 的方向意圖：**以 camera forward 為主**；**某些指定技能**若鏡頭前方有合法敵人才 soft-target。
⛔ 不要 Aim 鍵；右鍵保留給 Guard／Block。

### 19.2 與 ADR-007 的相容性：**五條決策全部成立，不需 Supersede**

| 決策 | 影響 |
|---|---|
| **D1**（三概念不得互相冒充） | ✅ **這個模型是 D1 的純粹實例**——它整套規則就是用「Movement 與 Facing 何時相同／何時分離」定義的 |
| **D2**（移動方向世界化，MotionDriver 只執行） | ✅ 不受影響，且**是前提**：camera-relative 輸入 → 世界方向，正是 S1 交付的東西 |
| **D3**（每角色單一 facing authority） | ✅ 權威結構不變。**改變的只是優先序清單的內容**——而 D3 明文「**不凍結**優先序清單本身」 |
| **D4**（單一 aim authority，方向由 lifecycle 交付） | ✅ 不受影響。「攻擊方向以 camera forward 為主、可 soft-target」正是既有 aim 解算 ＋ 承諾機制 |
| **D5**（承諾有作用域，facing 與 release 讀同一份） | ✅ **反而被強化**：承諾成為 facing 唯一會離開移動方向的理由 |

⇒ **S1／S2 都是承重結構，不但沒白做，而且是這個模型的前提。**

### 19.3 需要降級／改寫的既有假設

| # | 既有假設 | 處置 |
|---|---|---|
| **1** | `CharacterFacingSource` 優先序 ③「combat context target」＝**戰鬥中持續面向敵人** | 🔴 **移除**。新優先序：① Action 承諾 → ② explicit target（lock-on，未來）→ ③ **移動方向** → ④ 維持現朝向 |
| **2** | S3c 的 `idleTurnEnterAngle`／`idleTurnExitAngle`（60°／10° 待機轉身門檻） | 🔴 **失去存在理由**——門檻是為了「持續面向敵人時不要一直磨腳」而生；不再持續面向就不需要。**兩個旋鈕應移除** |
| **3** | `HeadLookController`（頭部 look-at） | ✅ **保留，而且更重要**——它變成待機時**唯一**承認敵人存在的表現，且是純表現層、不動 root |
| **4** | **Combat Context ＝ facing 模式** | 🔄 **職責收窄**：不再驅動持續朝向，改為**「我正在跟哪個敵人交戰」的黏性目標提供者**，供 ① Action 承諾挑對象 ② 頭部 look-at ③ 未來的戰鬥表現（持劍／取景）消費 |
| **5** | `docs/11` §8.3「**所有** Action 一律先朝向目標，且**不得**做成 authored 欄位」 | ✅ **2026-09-08 已改寫**為 **Action Targeting Policy** 三分類（`CameraForward` 預設／`CameraConeSoftTarget`／`SelfCentered` 僅概念）。可預測性改由「**一句話講得完的規則** ＋ **預設值就是最可預測的行為**」保證，而非「禁止任何 per-Action 差異」。ADR-007 D3 的附屬禁令同步收窄（見該 ADR §11 修訂紀錄） |
| **6** | `docs/13` 全篇的「**戰鬥中面向敵人橫移**」目標敘述 | 🔄 **改寫**：目標不是「戰鬥中橫移」，而是「**出手期間移動時**維持 Action 朝向」 |
| **7** | 「Combat Context ⇒ 常駐 8-way／常駐持劍」 | ⛔ **明確否決**（使用者裁決）。三者互相獨立 |

### 19.4 兩個直接消失的問題

**① 鏡頭與身體的參考系錯配（前一輪診斷的那個）**
先前的矛盾是「身體釘在目標、鏡頭卻自由」＝三種標準配置之外的第四種組合。
新模型讓**平時身體跟著移動方向**⇒ 回到標準的「探索配置」，**矛盾自然消失**，
且**不需要**為此導入 lock-on 相機。
（出手期間仍會短暫分離，但那是 0.4–1.6 秒的瞬時狀態，不是持續體驗。）

**② 「戰鬥是否要限速在 run 階」**
8-way 不再常駐 ⇒ **不需要**一個持續的戰鬥速度政策。`docs/13` §10.5 的那個開放問題**直接作廢**。

### 19.5 順帶：兩個原本很棘手的資產問題，變小了

- **側移／後退動畫需要 1.6× 加速**：只在出手期間短暫出現 ⇒ 影響大幅下降
- **沒有側向收步素材**（Play 實測的「停步很奇怪」）：出手期間很少發生「放開輸入收步」
  ⇒ **從阻擋降為瑕疵**

### 19.6 這輪之後才要回答的

1. §8.3 改寫後，「哪些技能 soft-target」是 authored 欄位還是規則？**怎麼維持可預測性？**
2. 8-way 的切換條件怎麼表達——是「facing 是否被承諾覆蓋」這個狀態，還是別的？
3. Combat Context 收窄後，`enterRadius`／`leaveRadius`／`disengageSeconds` 是否還需要那麼講究？
4. 短暫 8-way 的進出要不要淡入淡出，還是直接切？

### 19.7 落地紀錄（2026-09-08）

**已完成**：§19.3 的第 1、2 項。

| 項目 | 結果 |
|---|---|
| **1. 移除常駐 combat target 朝向** | ✅ `CharacterFacingSource.TryResolveFacing` 新簽名只吃 `hasActionCommitment` / `actionCommitment` / `moveDirection`——**`CombatContextData`、`actorPosition`、`actorForward` 三個參數一併移除**，簽名誠實反映依賴 |
| **2. 移除 idle 轉身遲滯** | ✅ `idleTurnEnterAngle`／`idleTurnExitAngle`／`_isIdleTurning` 全部刪除 |
| A31（單一 `RequestFacing` 送出者） | ✅ 維持恰好一個 |
| build | ✅ Runtime ＋ Tests.EditMode 皆 0 error |
| 改動範圍 | 2 個 `.cs`（元件 ＋ 測試），零資產、零 Git |

**測試改寫**（不是刪斷言）：新增「`InCombat` 且有目標但**無承諾** ⇒ facing 跟隨 `MoveDirection`」——
這是本次核心新行為，必須被釘住；idle 遲滯的兩個測項刪除並留註解指向本節。

#### `CombatContext` 剩下的三個讀者（掃描結果，都仍合理）

| 讀者 | 用途 | 判斷 |
|---|---|---|
| `PlayerCombatContextSource` | 讀前幀 `InCombat` 維持進退場與黏性 | ✅ 自己的狀態機 |
| `ActionState.CaptureReleaseContext` | 出手時取目標建立承諾 | ✅ **本輪刻意保留**——改它屬 Action Targeting Policy 切片（`docs/11` §8.3） |
| `HeadLookController` | 頭部 look-at | ✅ 現在是**待機時唯一承認敵人存在**的表現 |

⇒ **`CharacterFacingSource` 自此與 `CombatContext` 零耦合。**

#### 📌 殘留的孤兒序列化欄位（無害，不要動）

`SampleScene.unity` 仍留有 `idleTurnEnterAngle: 60`／`idleTurnExitAngle: 10` 兩個 key。
欄位已不存在 ⇒ Unity 不會反序列化，**下次場景存檔自動丟棄**。⛔ 不要為此手改場景。

#### ~~⚠️ 這次**沒有**改變的事~~ → ✅ **已於 2026-09-08 稍後補上**

~~**出手期間仍然會自動朝向 combat target**（`ActionState` 未動）⇒ 目前所有 Action 都等同
`CameraConeSoftTarget` 的行為。**三分類政策（`CameraForward` 為預設）是下一個切片**，
在那之前「普通攻擊朝鏡頭」還沒生效。~~

**該切片已落地**（同日）：`ActionDefinitionSO.Targeting` ＋ `ActionState.TrySoftTarget` 角錐閘門。
既有三份 Definition 因為吃新欄位的預設值，**已自動從「全部吸敵」變成「全部朝鏡頭」**。
落地細節與那個「相機錐 vs 黏性目標」開放問題的答案：`docs/11` §8.3。
