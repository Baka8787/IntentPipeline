# ADR-007：Direction Authority（移動方向／朝向／瞄準方向的擁有權與更新時點）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🟡 **Trial**（2026-09-06 使用者裁決）——**已是目前的實作基線**，但**尚未由第一個 vertical slice 驗證**。⚠️ 引用本檔時必須註明此狀態：**使用者已裁決 ≠ 工程上已驗證**。決策內容**不凍結**，可依實作發現修訂（記入 §11，不必開新 ADR） |
| 日期 | 2026-09-06（建立／同日 `Proposed → Trial`） |
| 判準 | CLAUDE.md ADR 判準 **①**（黑板 schema ／ ownership 變更）＋ **③**（核心驅動介面語意變更：`IMovementIntentSource` 的方向語意、`IActionLifecycleSink.Release` 契約）＋ **④**（推翻既有不變量：`docs/09` §4-D3 的「⛔ 不得為此新增黑板欄位」與「⛔ 不得長成 facing system」） |
| Supersede | 無。**延續** ADR-003（D1／D2／D4 的 intent 契約）與 ADR-004 D2（release 時點單一權威）、ADR-005 D1（Action identity）。⚠️ 但**修訂** `docs/09` §4-D3(c) 的兩條紅線與 `docs/10` §4.2 的 trip-wire 處置——見 §7 |
| 關聯文件 | **`docs/14-direction-authority.md`（Living Spec：契約面、切片、測試、開放項全在該檔）**、`docs/13-combat-locomotion.md` §4（診斷）、`docs/09-camera-aim.md` §4-D3／§5.2、`docs/10-lock-on.md` §4.3／§8、`docs/11-multi-action.md` §8.3 |
| 前置事實 | ADR-005 已於 2026-09-06 `Accepted`。2026-09-11 使用者明確批准 ADR-006 與本 ADR 在「施法 8-way＋soft target」combined vertical slice 暫時同為 Trial；這是一次性互動驗證，**不放寬** ADR-004 的一般治理規則。 |

> **本 ADR 刻意寫短，比照 ADR-005 的檢討。** 只凍結五條「改錯會造成架構污染」的決策；
> 候選比較的細節、欄位形狀、切片順序、測試清單一律下放 `docs/14`，且**允許在 Trial 期間被程式推翻**。

---

## 1. 問題：三個方向概念沒有名字，因此互相冒充

系統裡實際存在三個**不同**的方向決策，但只有兩個載體（`transform.rotation` 與 `AimResolver` 的當幀快取）。
概念比載體多，於是概念只能互相冒充——**這不是三個 bug，是同一個根因的三個出口。**

### 1.1 磁碟證據（2026-09-06 核對）

| # | 位置 | 事實 | 這是誰在冒充誰 |
|---|---|---|---|
| **E1** | `MotionDriver.ExecuteBaseMovement`（`Presentation/Motion/MotionDriver.cs`） | `horizontalVelocity = transform.forward * currentSpeed` | **朝向冒充移動方向** ⇒ strafe 結構上不可能（`docs/13` §4.1／§4.2） |
| **E2** | `AIMovementSource.ProduceIntent`（`Core/Movement/AIMovementSource.cs:115-125`） | 敵人算出**世界方向**後，用 `data.CameraTransform` 投影成相機空間 2D，只為了讓 `MotionDriver` 再投影回世界 | **相機基底冒充移動基底**（＝登記已久的 **FU-6**）。敵人的移動因此綁在玩家相機上 |
| **E3** | `AimResolver._latchedFacingDirection`（`Presentation/Camera/AimResolver.cs`） | Action 期間的朝向承諾是一個**跨帧存在的 Presentation 私有欄位** | **表現層元件持有 gameplay 承諾**。與 ADR-003 D5「mode／toggle state 必須進黑板、不得藏在元件私有欄位」是同一類問題 |
| **E4** | `ThrowProjectileEmitter.ResolveThrowRotation`（`Presentation/Actions/ThrowProjectileEmitter.cs`） | 每次 `Release()` 重新呼叫 `aimResolver.TryGetAimPoint()` | **sink 自帶第二個方向權威**。與 E3 兩個擁有者、兩個時點 ⇒ **A6 的「人朝 A、火球飛 B」** |
| **E5** | `GroundEffectSink.ResolveGroundCenter`（`Presentation/Actions/GroundEffectSink.cs`） | 落點取 `casterRoot.forward * castDistance`，**註解明寫**它的正確性建立在「forward 本來就指著敵人」 | **一個功能的正確性依賴 E3 的別名剛好成立**。別名一旦解除（本 ADR 的目的），它會安靜地失準 |
| **E6** | `ActionState`／`FullBodyStateMachine`／`CharacterPipelineRunner` | 三個 **Core** 檔案直接 `using Project.Presentation.CameraControl` 並持有**具體類別** `AimResolver` | `docs/09` §5.2 的 **trip-wire ①「Core 層需要讀 AimPoint」已於 2026-09-05 被跨過**——而且是以最重的形式（依賴具體類別而非抽象），當時沒有停下來開 ADR |

### 1.2 為什麼「每段重新 latch facing」不是解

那只是讓 E3 與 E4 兩個擁有者的**時點碰巧一致**。擁有者仍然是兩個，
下一個跨越多次 `Release` 的機制（持續施法、多段投射、蓄力）出現時，同一條裂縫會原封不動再開一次。
**A6 不是連段的 bug，是「方向沒有擁有者」在連段這個放大鏡下第一次被看見。**

---

## 2. 三個概念（＋一個必須被排除在外的第四個）

| 概念 | 一句話定義 | 誰在乎 |
|---|---|---|
| **MovementDirection** | 身體**位移**往哪裡去（世界方向） | locomotion、AI 走位、物理 |
| **FacingDirection** | 身體**正面**朝哪裡（世界方向，水平） | 動畫可讀性、近戰命中窗、玩家可預測性 |
| **AimDirection ／ AimPoint** | 這一擊的**世界效果**朝哪／落在哪（含高度） | 投射物、地面 AoE、命中判定 |
| ~~CameraDirection~~ | 相機取景方向 | **Presentation 私有**。它是**玩家輸入的投影基底**，⛔ **不是**任何角色的方向真相（E2 正是踩到這條） |

📌 **三者可以相等，但相等必須是結果，不能是實作**。
今天它們相等是因為共用同一個變數；那正是為什麼「想讓它們不等」時整個系統會抵抗。

---

## 3. 候選形狀比較

| 方案 | 形狀 | 解掉 strafe？ | 解掉 A6？ | 代價／為什麼不選 |
|---|---|---|---|---|
| **A. 黑板三欄位**<br>`DesiredMoveDirection`／`DesiredFacingDirection`／`DesiredAimDirection` | 三個概念各給一個黑板欄位，各自一個 writer，由 A5 機器守住 | ✅ | ✅ | **對稱、可 snapshot、可機器守衛**——但**一次付清三筆**。`AimDirection` 今天只有玩家有意義（敵人恆為 default），且它的生產者與消費者**目前仍全在 Presentation** ⇒ 現在就加，正是 `docs/09` §5.2 要用負面證明打掉的「加功能＝加欄位」反射。**方向對，時機過早** |
| **B. movement／facing 分離，aim 仍由 release-time resolver 決定** | 只做 `docs/13` §7-1 | ✅ | ❌ | **只解一半**。E4 原封不動，A6 完全不受影響；更糟的是**別名解除後 E5（AoE 落點）會跟著壞**，因為它靠 forward 指著敵人。**單獨做會製造回歸** |
| **C. `ActionState` latch 一個 action-local directional context** | 承諾住在 Action 內，出手期間 facing 與 release 都讀它 | ❌ | ✅ | 承諾的擁有者選對了（ADR-004 D2：release 時點本來就是 `ActionState` 的），但**完全不碰 locomotion** ⇒ strafe 一動不動。且若只做這一步，承諾仍是私有跨帧欄位（E3 只是換個地方存在） |
| **D. ✅ 推薦：分離載體 ＋ 單一承諾（one commitment, two projections）** | ①移動方向改世界座標、由 movement chain 獨佔；②每角色恰好一個 facing authority；③每角色恰好一個 aim authority，且**世界效果的方向由 lifecycle 交付**；④承諾有明確作用域，**facing 與 release 讀同一份** | ✅ | ✅ | ＝B 的分離 ＋ C 的承諾 ＋ A 的所有權紀律，**但黑板只在第二個 facing 來源真的出現時才長欄位**。代價是 `IActionLifecycleSink.Release` 契約要加參數（加法，見 §7） |
| **E. ⛔ 否決：每段重新 latch facing** | 連段每段呼一次 `TryLatchAutoTargetFacing` | ❌ | 🟡 表面上 | §1.2。**修的是巧合，不是權威。** 使用者明確否決，本 ADR 同意 |

---

## 4. Decision（凍結內容，共五條）

### D1 — 三個方向概念各自獨立，任何一個都不得以另一個的載體表示

`MovementDirection`／`FacingDirection`／`AimDirection` 是三個決策。
⛔ **禁止別名**：不得以 `transform.forward` 表示移動方向或瞄準方向；不得以「移動方向反推朝向」作為**唯一**的朝向規則。

**理由**：別名讓兩個決策共用一個變數 ⇒「改一個」必然「動到另一個」。E1–E5 全部是這條的直接後果。
**不凍結**：三者的載體型別、命名、是否進黑板。

### D2 — 移動方向是**世界方向**，擁有者是 movement chain；`MotionDriver` 是執行者，不是決策者

producer（`IMovementIntentSource`）輸出**世界方向** → model dynamics → 黑板 Movement Output（世界）→ `MotionDriver` 照著積分。

- ⛔ `MotionDriver` 的 **procedural 位移路徑**不得再讀 `transform.forward`，也不得持有相機基底。
- ⛔ 相機基底（`CameraTransform`）只能在**玩家自己的 producer** 內作為輸入投影使用；其他 producer 不得引用它（＝ **FU-6 結案**）。
- ✅ **明文例外**：`ExecuteBakedCurveMovement` 沿 `transform.forward` 位移**不是別名**——烘焙曲線本來就表達 clip **自身座標系**的位移，身體正面就是它的參考框。此路徑保留。

**理由**：`MotionDriver` 住在 `Presentation/`。它「決定」方向就是表現層取得 gameplay 權威（CLAUDE.md 依賴方向 ＋ 不變量 A4）。
它該做的是積分、碰撞、與 `transform.rotation` 的單一寫入——**不含「往哪走」**。
**不凍結**：世界方向用 `Vector3`(y=0) 還是 `Vector2`(XZ)、轉向弧線的 dynamics 放哪、平滑參數。

### D3 — 每個角色**恰好一個** facing authority；`transform.rotation` 的寫入者仍恆為 `MotionDriver`

lock-on、AI、玩家輸入、aim **都是那個 authority 的來源，不是第二個送出者**。優先序在該 authority **內部**解決。

- ⛔ 不得出現第二個 `RequestFacing` 送出者（`docs/10` §4.3 的規則，自此適用於**所有角色**，不只玩家）。
- 🔄 **2026-09-08 修訂（Trial 期，依使用者裁決）**：原文為「⛔ 不得把『這個 Action 要不要轉向』做成 `ActionDefinitionSO` 的 authored 欄位」。
  **該禁令針對的是布林開關**——忘了勾就得到「這招不轉向」這種不可預測且無線索的行為。
  改採**帶預設值的 enum（Action Targeting Policy）之後，該失敗模式不存在**：沒填得到的是預設的
  `CameraForward`（＝與所有普通招式一致的可預測行為），要吸敵**必須顯性標記**。
  ⇒ **禁令收窄為**：⛔ 不得讓 authored 欄位的**預設值**產生「與其他 Action 不一致」的朝向行為；
  ⛔ 不得擴充成一組可自由組合的朝向旋鈕。政策清單與理由見 `docs/11` §8.3。

**理由**：今天已經有三個送出者（`AimResolver.Update` 的 aim、`ActionState` 驅動的 latch 重送、`MotionDriver` 自己的移動反推），
優先序靠「當幀誰最後寫」與一個 `hasFacingRequest` 布林**碰巧成立**。
`docs/09` 禁止的是「長出 facing 優先級表」；但**表已經存在，只是寫在執行順序裡**。
把它收斂成單一 authority 不是新增系統，是為既存的東西指定擁有者。
**不凍結**：該 authority 叫什麼、住在哪一層、承諾走黑板欄位還是同層呼叫、優先序清單本身。

> **📌 補充條款（2026-09-06 修訂，Trial 不凍結）—— facing 來源必須是「語境」，不得是「輸入模式」**
>
> 使用者裁決：`<Mouse>/rightButton` **保留給 Block／Guard**，且**不要任何獨立的 Aim Mode**。
> ⇒ D3 的「單一 facing authority」自此加一條約束：**它的來源不得以「玩家按住某鍵」為前提。**
> ⛔ 也**不得**用另一顆鍵代替右鍵當 combat-facing 的開關——那只是把耦合換一顆按鍵。
>
> **理由**：把 facing 綁在輸入模式上，等於讓鍵位配置變成朝向系統的前提；
> 任何按鍵改動都會動搖朝向。**朝向該由「現在在跟誰打」決定，不是由「現在按著什麼」決定。**
> ⇒ 設計與實作規格見 **`docs/15-combat-context.md`**（🟡 已採納，本 ADR 的 Living Spec）。
> **D1–D5 五條決策本身不受此需求變更影響、一字未改**（分析見 `docs/15` §8.1）。
>
> **Combat Context 的歸屬（2026-09-06 使用者裁決：⛔ 不另開 ADR-008）**
> Combat Context 是本條 D3 的**實例化**——它提供 facing authority 所需的 target source，
> **不是另一套架構哲學** ⇒ 不開新 ADR，判準①的內容由本條承載：
> - `CombatContextData` 黑板 region 的**唯一寫入者**＝每角色唯一 active 的 combat context producer
>   （比照 ADR-003 D2 的 single-writer 契約），由 A5 `WriterRules` 機器守住。
> - ⛔ **禁止任何其他系統自建 `isInCombat` 旗標或第二份 target 清單**——那是 ADR-005 D1
>   「N 把鍵＝N 份必須人工同步的真相」的同型錯誤。
> - **不凍結**：欄位組成、進出規則的門檻與時間常數、producer 的實作與檔名、目標選擇演算法。全部下放 `docs/15`。

### D4 — 每個角色**恰好一個** aim authority；世界效果的方向**由 lifecycle 交付**，consumer 不得自行重解

- ⛔ `IActionLifecycleSink` 的實作（投射物／AoE／hitbox）不得呼叫 aim 解算，也不得從 `transform` 反推方向。
- ✅ 方向隨 `Release` **交付**給 sink。

**理由**：release 的**時點**唯一權威是 `ActionState`（ADR-004 D2）。**方向與時點是同一個決定的兩半**——
分開擁有必然分岔，A6 就是分岔的第一次現形。這也是 A21「external seam 不得持有動畫／轉移權威」的同一條紀律，
只是延伸到**方向**權威（該不變量的 forbidden 清單因此要補 token，見 §7）。
**不凍結**：交付的是方向、瞄準點還是一個小 struct；欄位組成；sink 端如何使用。

### D5 — 方向承諾（commitment）有**明確且單一**的作用域；facing 與 release 讀**同一份**承諾

「這一擊朝哪」在**明確的邊界**取得一次，Action 期間 **facing 與世界效果都只讀它**。
⛔ 禁止「facing 讀承諾、release 讀當下」的混合——那正是 A6。

**保留 ADR-005 的設計意圖**：承諾在**段落邊界**取得、段落內不追蹤 ⇒
「揮擊途中甩相機，角色不會一直轉」的性質**一字不變**；改變的只是**承諾與發射不再各自解讀**。
**不凍結**：作用域的粒度（每次執行一次 vs 每個帶 release 的段落一次）＝可調參數。
**凍結的是「同一份」，不是「多久一份」。**

---

## 5. 明確**不**由本 ADR 管的事（下放 Living Docs，不需要 ADR）

| 項目 | 為什麼不需要 ADR | 去處 |
|---|---|---|
| 「Presentation／Animation 不得成為 gameplay direction 的 authority」 | **CLAUDE.md 依賴方向 ＋ 不變量 A4 已凍結**，複述不增加保護。D2／D4 已是它在方向議題上的具體化 | — |
| 欄位型別、命名、struct 形狀、pipeline 順序編號 | 實作細節 | `docs/14` §2 |
| 承諾粒度、死區、轉向速率、方向平滑的時間常數 | 手感參數（`docs/09` §4-D3 已定調此類為構圖參數） | `docs/14` §3／`MotionDriver` Inspector |
| 2D locomotion mixer、strafe 動畫接線 | 表現層，與方向權威正交 | `docs/13` §7-2 |
| lock-on 的目標保留策略 | 已有規格 | `docs/10` §4 |
| 敵人 facing 來源的具體實作 | 加法，走既有 routing rule | `docs/14` §4-S3 |

---

## 6. 為什麼 D 同時解掉 strafe 與 A6

**因為兩者是同一個別名的兩端，而 D 是唯一同時切斷兩端的形狀。**

```
                        ┌─ 位移執行 ─► E1「速度＝transform.forward」─► strafe 結構上不可能
別名：方向真相 ＝ transform ─┤
                        └─ 世界效果 ─► E5「AoE 用 root.forward」（正確性靠別名成立）

承諾分裂：facing 鎖在 OnEnter ／ aim 每次 Release 重解 ─► E4 ─► A6：人朝 A、火球飛 B
```

- **D2 切斷上半**：位移不再讀身體 ⇒「面向 A、往 B 走」在物理上成立 ⇒ strafe。
- **D4＋D5 切斷下半**：世界效果不再自己解方向，且與 facing 讀同一份承諾 ⇒ 人與火球**在結構上不可能分家**。
- **D1 讓兩端不會再長回來**：概念有了名字，下一個方向消費者（受擊反擊方向、格擋角度、鎖定 strafe）必須說明自己讀的是三者中的哪一個。
- 📌 **順帶**：D4 讓 E5 的隱性依賴（AoE 靠 forward 指著敵人）從「碰巧成立」變成「明確交付」——
  **否則單獨做候選 B 會把它弄壞。**

### 6.1 逐條回答裁決清單

| 提問 | 裁決 | 出處 |
|---|---|---|
| 三者是否為不同概念 | **是**，且禁止別名 | D1 |
| 各自的 authority / owner | 移動＝movement chain（producer→model）／朝向＝每角色單一 facing authority，`transform.rotation` 寫入者恆為 `MotionDriver`／瞄準＝每角色單一 aim authority | D2／D3／D4 |
| 誰 continuous、誰可 latch | 移動方向**恆 continuous，永不 latch**；朝向**預設 continuous，只在承諾作用域內 latch**；瞄準**解算 continuous，但一旦承諾即凍結至作用域結束** | D5 ＋ `docs/14` §1.2 |
| Action 能否提出 facing request | **不能**「提出請求」，而是**取得承諾**——差別在於前者可被逐 Action 資產化（會不一致），後者由單一位置強制 | D3 ＋ D5 |
| lock-on / AI / player 是否共用一條 facing seam | **是**，且是**同一條 seam、每角色一個送出者**；三者都是該送出者**內部的來源** | D3 |
| projectile / spell 是否讀共同 aim authority | **是**，而且更強：sink **不讀** authority，方向由 lifecycle **交付**給它 | D4 |
| movement 是否仍可假定 `transform.forward` | **不可**（procedural 路徑）。烘焙曲線路徑例外且已明文 | D2 |
| Presentation / Animation 能否成為 direction authority | **不能**——但這由 CLAUDE.md 依賴方向 ＋ A4 已凍結，本 ADR 不重複凍結，只給出方向議題上的具體化 | §5 第一列 |
| 如何保留「Action 中途甩相機不應一直轉」 | 承諾在**段落邊界**取得、段落內不追蹤 ⇒ 該性質一字不變 | D5 |

---

## 7. 受影響的既有決策與不變量

| 對象 | 影響 | 處置 |
|---|---|---|
| **ADR-003** D1／D2／D4 | `MovementIntentData.DesiredDirection` 的**座標系語意**由「相機空間 2D 輸入」改為「世界方向」。契約本身（model-agnostic 的方向＋強度）**不變**，改的是它一直沒被寫明的參考框 | **不 Supersede，屬細化**。ADR-003 §9-L2 與 FU-6 早已把它登記為開放點；世界方向比相機空間**更** model-agnostic |
| **ADR-004 D2** | release 時點的單一權威**延伸**到 release 方向 | 延續，不推翻 |
| **ADR-005 D1** | 不受影響。承諾**不是**第二把 identity 鍵 | 無 |
| **ADR-006**（Trial） | 無 ownership 衝突；本 combined slice 驗證 Layer 0 locomotion＋Layer 1 spell 與方向承諾能否共同成立 | 2026-09-11 一次性 Trial 例外，見 §9.4 |
| **`docs/09` §4-D3(c) 紅線「⛔ 不得為此新增黑板欄位」** | 🔴 **本 ADR 保留推翻它的權利**（僅限 facing／aim 承諾，且僅在 S3 第二個 facing 來源落地時）。該紅線成立的前提是「送出者與消費者全在 Presentation」——E6 已證明前提不再成立 | S3 落地時同步改寫 `docs/09` §4-D3 並在此註記；**S1／S2 不動黑板 schema** |
| **`docs/09` §4-D3(c) 紅線「⛔ 不得長成 facing system」** | 🟡 **語意收窄**：仍禁止「散落的多送出者＋優先級表」，但**允許**單一 facing authority 內部有明確優先序（D3） | 於 `docs/14` §3 明文，並更新 `docs/09` 註記 |
| **`docs/09` §5.2 trip-wire ①②** | ✅ **確認已被跨過**（E6）。本 ADR 即是它要求的處置 | `docs/09` §5.2 已加註「已觸發 → ADR-007」 |
| **`docs/10` §8 Stage 2** | 本 ADR 即該節預告的「必開新 ADR」；FU-6 於此結案 | 已加註指向本 ADR |
| **`docs/11` §8.3 開放項**（強制點放哪／過程中是否持續追向／無目標行為） | 前兩項由 D3／D5 回答；第三項仍開放 | 已於 `docs/11` §8.3 加註 |
| **A5 `WriterRules`** | **S1／S2 零改動**（不新增黑板欄位）。S3 才可能新增一列 | — |
| **A16**（`AIMovementSource` 只用 NavMesh 做查詢） | 檔案會改，斷言不變 | 重跑即可 |
| **A21**（external seam 無動畫／轉移權威） | forbidden token 清單**增補**方向權威相關 token（`AimResolver`／`TryGetAimPoint`） | S2 一併更新 |
| **A4 `LayerRules`**（`Core/StateMachine` 允許整個 `Project.Presentation`） | ⚠️ 這條寬鬆正是 E6 得以安靜發生的原因——規則的**意圖**只放行 `MotionDriver`／`AnimationFacadeBase` 兩個 seam | 建議 S3 收緊為型別白名單。**不阻擋 S1／S2** |
| **新增不變量** | A28（procedural 位移路徑不得讀 `transform.forward`）／A29（sink 不得自行解算方向）／A30（只有玩家 producer 可引用 `CameraTransform`） | 詳見 `docs/14` §5 |

---

## 8. Acceptance Criteria（`Trial → Accepted`）

> 📌 **2026-09-12 fold-back**：`docs/17` §7-F4 曾記「§8 的七條驗收**一條都沒打勾**」。
> 本輪核對後，**A1／B 有 Play 證據（在 `WORKLOG.md`，未回填）、C／D 可由程式與測試直接證明**——
> 下方已把這四條登記進來。**Status 不變（🟡 Trial）**：A2／E／G 仍未驗，F 只驗了 EditMode。
> ⛔ 依使用者指示與 CLAUDE.md，**證據不足不得自行升 Accepted**。

- **A. strafe 物理成立**（🔄 **2026-09-10 拆為 A1／A2**，見 §11。原 A 把「Combat Context ⇒ 玩家持續 target-facing」
  寫進了驗收，而**那個前提已於 2026-09-10 被使用者否決**——玩家不以 combat context 切換朝向）：

  - [x] **A1（玩家）**：committed action 取得 facing authority 期間，Facing ＝ action 承諾方向、
    `MovementDirection` ＝ 玩家輸入的世界方向，**兩者分離成立**；
    **action 結束當幀** facing 立即回到跟隨 `MovementDirection`，⛔ 不得殘留 strafe 姿態或延遲回正。
    ⚠️ **玩家正常 locomotion 維持 1D**；⛔ **不得**以 Combat Context 作為玩家切換朝向或 8-way 的條件。
    📌 本條**不依賴** 8-way 動畫，也**不依賴** ADR-006 上下半身分層——驗的是 facing／movement 的
    **分離與復原**，不是表現層。⇒ **在 A2 之前即可獨立驗收。**
  - [ ] **A2（敵人）**：進入 combat 且有 target 後，敵人**持續**面向 target（側移／後退／繞行／出手皆然）；
    **脫離 combat 後**回到 facing 跟隨 `MovementDirection`，⛔ 不得永久 TPS strafe。
    ⚠️ 「脫離 combat」必須**真的可達成**——語境進出是黏性 enter／leave 半徑，
    ⛔ 不得只由「target 欄位非 null」決定（否則本條永遠驗不過）。切片與門檻見 `docs/19` §3.1。

  📌 **過渡觀察（2026-09-06，使用者實跑）**：以舊治具（按住右鍵）驗到「右鍵時始終面朝前方沒問題」，
  **位移／朝向分離的物理面已成立**；缺的只是「朝向來源不靠按鍵」。<br>~~facing 被鎖住時（按住瞄準／後續 lock-on）按左，角色**朝目標、往世界左方位移**，不再朝目標走過去~~
  ✅ **A1 證據**（`WORKLOG.md` 2026-09-11 combined slice，使用者實跑）：「無目標、前方、側方、背後、雙目標與
  段內 target 消失情境皆符合 snapshot 規則」「Action 結束 overlay 歸零」；facing 與 movement 分離在 Layer 0
  持續 8-way 期間成立。⚠️ 手感（瞄準修正量）仍待驗，但**本條驗的是分離與復原，不是手感**。
- [x] **B. A6 消失**：Fireball 連段期間敵人走動，**每一段的身體朝向與火球方向一致**（允許段與段之間變向，不允許同段內分家）
  ✅ **證據**：`PlayerCombatContextSource` 每個 Action／連段段落**只取一次** soft candidate，
  「facing 與 projectile release 共用同一 commitment」（`WORKLOG.md`）⇒ 同段內結構上不可能分家；
  Play 已涵蓋雙目標與段內 target 消失。
- [x] **C. FU-6 結案**：`AIMovementSource` 不再引用 `CameraTransform`；**移動相機不改變敵人的移動方向**（Play 直接可驗）
  ✅ **證據（程式，2026-09-12 核對）**：`Core/Movement/AIMovementSource.cs` 對 `CameraTransform`／`camera` 的
  引用數為 **0**；並由 **A30**（`A30_OnlyPlayerMovementProducer_MayReferenceCameraTransform`）機器守。
- [x] **D. sink 零方向解算**：`ThrowProjectileEmitter` 不再持有 `AimResolver`；`GroundEffectSink` 不再以 `root.forward` 決定落點（由 A29 機器守）
  ✅ **證據（程式，2026-09-12 核對）**：`ThrowProjectileEmitter` 對 `AimResolver` 的引用數為 **0**；
  `GroundEffectSink` 的 `casterRoot.forward` 只剩 `HasAim == false` 的 fallback 分支（註解已說明），
  由 **A29**（`A29_ActionLifecycleSinks_DoNotResolveDirectionAuthority`）機器守。
- [ ] **E. 既有零回歸**：Idle／Move（含收步）／Jump（含空中控制）／Roll（含烘焙位移與旋轉）行為不變；動畫參數不晚一幀
  ❌ **未驗**：無任何 Play 紀錄涵蓋 Roll 烘焙位移與 Jump 空中控制的回歸確認。
- [ ] **F. EditMode ＋ PlayMode 全綠**，含新增的 A28／A29／A30
  🟡 **部分**：EditMode **330 passed／0 failed／1 skipped**（`WORKLOG.md`），A28–A32 均存在於
  `ArchitectureRegressionTests`。**PlayMode 結果未記錄** ⇒ 本條未完成。
- [ ] **G. 零 GC**：方向載體改型別後穩態仍 `0 B/frame`（`docs/02` §7.4 SOP）
  ❌ **未驗**：無量測紀錄。

**⇒ 2026-09-12 狀態結論：A1・B・C・D 通過（4），A2・E・G 未驗、F 部分 ⇒ 維持 🟡 Trial。**
剩餘工作是 **Play（A2 敵人脫離 combat／E 回歸）＋ PlayMode 跑一次 ＋ Profiler 一次**，**不是程式**。
⚠️ 這同時是 `docs/18` §6-C **X-3** 的解除條件——見該節 2026-09-12 的狀態欄。

**未通過** ⇒ 先修本 ADR ／ `docs/14` → 再驗證，**不得補 workaround**。
**Revert 成本：低—中**。S1 是語意置換（投影搬家，非新增機制）；S2 是介面加法。
若 **D2 被證偽**（世界方向讓轉彎手感無法接受且無法由 model dynamics 補回），退回別名並改以 2D mixer ＋ 分層解 strafe；
但 **D4／D5 可獨立成立**——A6 的修法不依賴 D2。

---

## 9. 實作切片

> 完整清單、契約面與測試在 **`docs/14-direction-authority.md` §4**。此處只放邊界。

### 9.1 第一切片（S1）：移動方向改世界座標，解除 movement／facing 別名

producer 輸出世界方向（玩家 producer 內做相機投影、AI 直接輸出）→ model 的方向 dynamics → `MotionDriver` 照著積分。
**一次改完整條鏈**，因為半條鏈沒有可驗收的中間狀態。

### 9.2 明確**不**在第一切片內

| 不做 | 理由 |
|---|---|
| 2D locomotion mixer ／ strafe 動畫 | `docs/13` §7-2。S1 的預期中間狀態就是「橫移但播前進動畫」＝滑步，**這是刻意的** |
| `IActionLifecycleSink.Release` 契約變更（A6 的修法） | 屬 **S2**。與 S1 分開驗收，否則 Play 出問題分不清是位移算錯還是承諾錯 |
| 任何黑板 schema 變更 ／ facing 欄位 | 屬 **S3**，且要等第二個 facing 來源真的出現 |
| lock-on、敵人 facing 來源 | S3 之後 |
| 上半身持劍層（ADR-006）、速度數值重調、原地轉身 clip | `docs/13` §7-3／§8／§7-4 |

### 9.3 後續切片

**S2**＝承諾 ＋ release 交付（解 A6）。~~**S3**＝facing authority 統一（lock-on／敵人朝向落地時）。~~

🔄 **S3 已於 2026-09-06 重新定義**（使用者裁決：右鍵改留給 Guard、不要 Aim Mode）：
S3 ＝ **Combat Context（gameplay 語境 ＋ 黏性目標）＋ 單一 facing source**。
原本假設 facing source 可以自己決定朝哪；現在它需要一個**語境輸入** ⇒ 從「一個元件」變成「**一個黑板 region ＋ 兩個 producer**」。
⚠️ 觸發條件的第三項（「strafe 需要非按住瞄準的持續朝向」）**現已成立** ⇒ S3 不再是「等未來」，而是 **Acceptance A 的前置**。
設計提案與最小切片（S3a）見 **`docs/15-combat-context.md`**（🔵 提案，未裁決）。

### 9.4 Trial 名額

2026-09-06 原裁決由本 ADR 單獨佔用 Trial 名額。2026-09-11 使用者針對
**Animation Layering × Direction Authority combined vertical slice** 明確批准一次性例外：ADR-006 與本 ADR
可暫時同為 Trial，以驗證施法期間 committed facing、MoveX／MoveZ FullRing 與 upper-body overlay 的交互作用。
此例外不適用其他 ADR，也不把「可並行多個 Trial」變成一般規則。

---

## 10. Risks

| # | 風險 | 處置 |
|---|---|---|
| R1 | **轉彎的「體重弧線感」消失**——現行速度跟著身體轉，是 E1 的刻意副作用 | 弧線是 **dynamics**，屬 model 職責（ADR-003 D4）。以 model 內的方向平滑取回，**不是**退回讀 transform。列為 S1 的 Play 觀察項 |
| R2 | 減速滑行期的方向保留語意改變：世界方向在滑行中**不再隨相機轉** | 更正確（滑行不該跟著鏡頭彎），但**看得出來**。列為 S1 Play 觀察項 |
| R3 | E5（AoE 落點）在別名解除後失準 | 正是 S2 要交付的東西。**S1 與 S2 若拆太開，中間會有一段 AoE 落點退化**——`docs/14` §4 已標記為連續執行 |
| R4 | `IActionLifecycleSink` 加參數 ⇒ 三個 sink ＋ 測試 fixture 同時要改 | 加法且編譯期可見，不會靜默失敗。舊行為＝把承諾當成 fallback 即可 |
| R5 | 敵人尚無 facing authority ⇒ S1 後敵人繞圈仍「面朝切線」 | **預期**。敵人朝向屬 S3，不在 S1 的驗收範圍——不得為了視覺在 S1 偷加第二個送出者 |

---

## 11. 修訂紀錄

| 日期 | 修改 | 原因 |
|---|---|---|
| 2026-09-06 | 建立草案（`Proposed`） | ADR-005 結案時登記的 A6（`docs/13` §4.3）與 §4.1／§4.2 的 strafe 阻擋被判定為同一根因；盤點時另發現 `docs/09` §5.2 trip-wire ① 已於 2026-09-05 被安靜跨過（E6）⇒ 觸及 ADR 判準①③④，必須先開 ADR 再實作 |
| 2026-09-06 | **`Proposed → Trial`（使用者裁決）**。同時裁定 **ADR-006 維持 `Proposed`**（Trial 名額由本 ADR 佔用），實作順序為 **S1 → S2 連續執行**，交由 Codex 落地 | ADR-005 已 `Accepted`，Trial 名額釋出；`docs/13` §7 的順序要求 movement/facing 先於上身層，否則兩組症狀混在一起無法歸因 |
| 2026-09-06 | **裁決：採納 `docs/15` 的 no-Aim-button combat-facing 模型與切片 S3a；⛔ 不另開 ADR-008。** Combat Context 視為 **D3 的實例化**（facing authority 的 target source），其黑板 ownership 由 D3 補充條款承載；`docs/15` 自此為本 ADR 的 Living Spec（不再是提案） | 使用者判斷：Combat Context 不是另一個獨立的架構哲學，而是既有 D3 的具體化 ⇒ 依 CLAUDE.md 反 ADR 爆炸原則，不值得為它開新 ADR |
| 2026-09-06 | **需求變更修訂（Trial 不凍結）**：使用者裁決右鍵保留給 Block／Guard、**不要獨立 Aim Mode** ⇒ ①**D3 加補充條款**（facing 來源必須是語境、不得是輸入模式）；②**Acceptance A 改寫**為「不按任何鍵的 combat facing」；③**S3 重新定義**為 Combat Context ＋ 單一 facing source。**D1–D5 五條決策一字未改** | 需求從「按住瞄準」變成「自動戰鬥語境」，而**決策文字完全不受影響**——這是決策寫在正確抽象層級的證據。連帶後果：Acceptance A 必須等 S3 才能以最終語意驗收 ⇒ **本 ADR 不可能在 S3 之前 `Accepted`**。分析見 `docs/15` §8 |
| 2026-09-06 | **R1 處置落地**：方向 dynamics 加回 model（`LocomotionSpeedSmoother` XZ 限速轉向，預設 720°/s）。使用者回報「敵人不抽搐」 | S1 拿掉 `transform.forward` 別名時，也拿掉了 body slerp 這個 producer 方向的低通濾波器。⛔ 未退回讀 `transform.forward`（A28 仍綠） |
| 2026-09-08 | **操作模型重新定義（使用者裁決）**：8-way 定位為「**MovementDirection 與 FacingDirection 分離時**的 locomotion 表示法」，**不是「進戰鬥就切」的模式**；Combat Context **不再驅動持續朝向**。**D1／D2／D4／D5 一字未動**，**D3 的主決策（每角色單一 facing authority）亦未動**，僅**修訂 D3 的一條附屬禁令**（見上）。相容性分析與降級清單：`docs/15` §19 | Trial 的目的就是讓實作與 Play 推翻假設。原本「Combat Context ⇒ 常駐面向敵人 ⇒ 常駐 8-way」是**我方推導出來的假設，不是使用者要的體驗**；Play 之後由使用者更正 |
| 2026-09-08 | **Action Targeting Policy 三分類定案**（`CameraForward` 預設／`CameraConeSoftTarget`／`SelfCentered` 僅保留概念），改寫 `docs/11` §8.3 並據此收窄 D3 附屬禁令 | 原禁令防的是「布林開關忘了勾 ⇒ 不可預測」；改成帶預設值的 enum 後該失敗模式消失，可預測性改由「一句話講得完的規則」保證 |
| 2026-09-08 | **F6 落地：Core 不再持有具體 `AimResolver`。** 新增 `Project.Core.Actions.IAimSource`（`CommitmentOrigin` ＋ `TryGetAimPoint`），`AimResolver` 實作之；`ActionState`／`FullBodyStateMachine`／`CharacterPipelineRunner` 三處改依賴介面。**同時收緊 §7 所列的 A4**：`Core/StateMachine` 白名單由整個 `Project.Presentation` 前綴改為 `Project.Presentation.Motion` ＋ `Project.Presentation.Animation`。<br>📌 **與 §7 的偏差（明示）**：§7 建議「型別白名單」，實作採**命名空間**層級——達成同一目的（把 `CameraControl` 擋在外面）、沿用既有比對機制、零新機器。**Trial 不凍結，故此偏差合法**，記於此處而非另開 ADR | E6 的根因是「規則的**意圖**只放行兩個 seam，但前綴比對放行了整個 `Project.Presentation.*`」⇒ 修的是那個縫隙本身。⚠️ `IAimSource.CommitmentOrigin` **忠實承接**「`AimResolver` 與角色 Root 同物件」的既有耦合，**不宣稱**瞄準來源理應知道角色位置——拆分屬另一個切片 |
| 2026-09-08 | **Action Targeting Policy 程式落地**（`ActionDefinitionSO.Targeting` ＋ `ActionState.TrySoftTarget` 角錐閘門，取代無條件優先取 combat target 的 `TrySelectAimPoint`）。**D1–D5 一字未動**；D5「同一份承諾」的性質不受影響——改的只是**承諾怎麼取得**，不是取得幾次。實作期開放問題（相機錐 vs 黏性目標）已答，記於 `docs/11` §8.3 | 上一列只是裁決，程式一行未動 ⇒ 磁碟上「所有 Action 都等同 soft-target」與裁決相反。⚠️ **`dotnet build` 0 error 不等於測試通過**；EditMode 實跑仍在使用者側 |
| 2026-09-06 | **S1＋S2 程式落地（code-first，Trial 期允許）**。**D1–D5 五條決策一字未動、未被推翻**。實作推翻的是 `docs/14` 的兩處**寫法**（`LocomotionModel` 無需修改；AoE 是「`AimPoint` 定方向、`castDistance` 定距離」而非「落點＝`AimPoint`」），已 fold back 進 `docs/14` §2.1／§2.2——**兩者都屬本 ADR 明文不凍結的範圍** | Trial 的存在目的就是讓實作有機會推翻規格。⚠️ **Acceptance A–G 全部尚未打勾**：EditMode／PlayMode 實跑與 Play 驗收在使用者側，`dotnet build` 0 error **不等於**測試通過 |
| 2026-09-10 | **持續 target-facing 的適用範圍收斂為「敵人」（使用者裁決）**，並據此把 **Acceptance A 拆成 A1（玩家）／A2（敵人）**。<br>⚠️ **與 2026-09-08 那一列的關係（重要，不要誤讀為互相推翻）**：09-08 記的是「**Combat Context 不再驅動持續朝向**」，當時的語境是**玩家體驗**；本列把該裁決**明確限定在玩家**，並確認**敵人相反**——敵人在 combat 且有 target 時**持續** target-facing，因為那是敵人**既有** Hold／strafe 行為本來就需要的 facing policy。<br>**D1–D5 五條決策一字未動**；D3 的補充條款（facing 來源必須是語境、單一 producer、⛔ 禁止自建第二份 isInCombat／target）**全部原樣適用**，敵人 producer 正是它明文允許的「每角色唯一 active 的 combat context producer」。<br>連帶：8-way 的定位仍是 09-08 那句「**MovementDirection 與 FacingDirection 分離時**的 locomotion 表示法」——**未改**；改的只是**誰會長時間處於分離狀態**。1D／2D 由 **locomotion presentation mode ／ actor policy** 決定，⛔ **不得**依 separation angle 做 runtime 切換（不連續選擇餵給連續量）。切片規格：`docs/19-enemy-combat-facing-slice.md` | 玩家 8-way 的真正前置是「施法期間下半身仍播 locomotion」＝ ADR-006 上下半身分層，而 ADR-006 仍是 `Proposed`（Trial 名額被本 ADR 佔用）⇒ 玩家那半邊**現在做不動**；敵人那半邊**不需要分層**（純 Layer 0 locomotion），且今天就已經是可見缺陷（繞圈時側身／背對玩家）。⇒ 先做做得動、且修的是既有缺陷的那半邊 |
| 2026-09-11 | **combined Trial 例外＋soft-target commitment 落地**：使用者批准 ADR-006 與本 ADR 暫時同為 Trial。`PlayerCombatContextSource` 在同一 producer 內額外發布無記憶的 soft-target candidate（12m／水平半角 25°；角度優先、距離次要），`ActionState` 只在每個 Action／連段段落邊界消費一次。段內不重選；facing 與 projectile release 共用同一份 commitment；target 中途失效仍保留該段承諾。**D1–D5 未改** | 這不是 hard lock-on，也不是第二個 target authority；它只是既有 D5 commitment 的選擇輸入。與 ADR-006 的 Layer 0 Walk／Run 8-way＋Layer 1 spell 一起接受 Play 驗證 |
