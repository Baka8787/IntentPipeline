# 56 — Enemy Combat Tactical Positioning（P5）：現況盤點與設計（2026-10-13，Research + Design）

| | |
|---|---|
| **狀態** | 🟢 **已裁決（2026-10-13，§10）——方案 B、P5-0 → P5a → P5b**。D3 改寫、D5／D7 轉為研究提案（§10.3、§10.4，⛔ 未裁決的部分不得實作）。§1–§9 保留為設計原文；與 §10 衝突處以 §10 為準 |
| **輸入** | 使用者 2026-10-13：P0–P3 人工 Play 驗收通過；進入 `docs/55` P5。戰鬥移動不應只是「固定距離左右移」，而要依玩家位置、理想攻擊距離、可達性與障礙物決定站位。本輪只做 Research＋Design |
| **範圍限制** | ⛔ 不改 runtime／prefab／scene；⛔ 不動非戰鬥巡邏（P1–P3）；⛔ 不做 P4 多敵人避讓（只保留接入點） |
| **基線** | `docs/55` §11（方案 B 已裁決；§11.5 是 P5 的**契約草稿**，本檔提出修訂）；`docs/54`（單一 target 權威、`AICombatContextSource` 順序 2.4）；`docs/21` §1.5（交戰方向限速） |
| **閱讀方式** | §1 現況 → §2 缺口 → §3 擴充還是獨立（Q1）→ §4 方案比較 → §5 推薦設計（Q2–Q7）→ §6 驗證（Q8）→ §7 ADR → §8 分階段 → §9 待裁決 |

> 行號以 commit `5080b18` 為準。沒有證據的結論標為 ❓**待確認**。

---

## 1. 現況盤點

### 1.1 交戰移動的決策（`AIMovementSource.ProduceIntent`）

| 步驟 | 行為 | 證據 |
|---|---|---|
| 交戰語境 | 只讀黑板 `CombatContext`（順序 2.4 的同帧結果）；未交戰 ⇒ 交給非戰鬥行為 | `AIMovementSource.cs:215–223` |
| 離網 | `Recover`：凍結遲滯、朝最近取樣點走 | `:244–266` |
| 模式 | `ResolveEngagementMovement`：一維距離帶 [1.25, 2.0]、遲滯 0.15 ⇒ Approach／Strafe／Retreat | `:268–273`、`:360–379`；欄位 `:46–48` |
| 路徑請求 | **所有**交戰模式都對**目標本身**請求路徑（節流 0.3 m／0.2 s） | `:278`；`EnemyNavigationSettings.Default` |
| Strafe | 方向＝`toTarget` 繞 Y 轉 90°（純幾何切線）；換向是**時間驅動**（2.5 s × [0.75, 1.25] 隨機）；只問「下一步 0.6 m 會不會穿出 NavMesh」，兩側都不行就站著 | `:284–315`、`:385–396`、`:470–494` |
| Retreat | 方向＝`-toTarget`，**沒有任何可走性檢查** | `:316–322` |
| Approach | 沿路徑轉角走向目標；無路徑／`PathInvalid`／部分路徑盡頭 ⇒ 直接朝目標壓（推牆是刻意接受的 trade-off） | `:323–339`；`EnemyNavigator.ResolvePathFollow` |
| 速度 | Strafe 0.35、其餘 1.0（normalized），再乘 Slow 倍率 | `:350–353`、`:567–573` |

⇒ 現行系統回答的是「**下一步往哪個方向**」，沒有任何地方回答「**我該站在哪裡**」。

### 1.2 目標權威與攻擊條件

| 概念 | 唯一權威 | 證據 |
|---|---|---|
| target（序列化） | `AIMovementSource.target` → 唯讀 `Target` | `AIMovementSource.cs:42`、`:92`；AB1、W15 |
| 交戰／目標位置 | `AICombatContextSource.Tick`（順序 2.4）→ `CombatContext.{InCombat, HasTarget, TargetPosition}` | `AICombatContextSource.cs`；A5 `WriterRules` |
| 「想不想打」 | `AIInputSource.WantsToAttack`（順序 1）：目標存在、活著、**水平距離 ≤ `attackRange`（2.0）** | `AIInputSource.cs:104–115`、`:141` |
| 「能不能打」 | `ActionState`（冷卻、著地、lifecycle；ADR-004 D2） | `AIInputSource.cs` 類別註解 |
| 攻擊圈＝交戰帶上界 | `attackRange` ≡ `maximumEngagementDistance` | `PrefabWiringTests.W10_AttackRange_MatchesTheHoldBandUpperBound`（`:787`） |
| `CombatContext` schema | `InCombat`、`HasTarget`、`TargetPosition`、`HasSoftTarget`、`SoftTargetPosition`——**沒有目標朝向** | `CombatContextData.cs:11–18` |

### 1.3 戰鬥移動的執行（八向、速度、朝向）

| 項目 | 現況 | 證據 |
|---|---|---|
| 朝向 | Y Bot `usePersistentCombatFacing` ⇒ 交戰中**持續面向目標**（Priority 2），與移動方向分離 | `CharacterFacingSource` 優先序；`docs/55` §1.3 |
| 動畫 | 方向由 local direction 決定（Y Bot：cardinal L1 模型，4 個 walk clip） | `docs/21` §1.5.1 |
| 速度上限 | `LocomotionModel` 在 `WhileInCombat` 依 local direction 限在 bake 速度 ⇒ Y Bot 交戰中**所有方向約 1.64 m/s**（四個 walk clip 1.6443–1.6444） | `LocomotionModel.cs:77`、`:360`；`docs/21` §1.5 |
| 推論 | 敵人往站位點移動時，**任何方向都不用轉身、不會滑步**；但玩家跑步明顯快於敵人 ⇒ 「繞著跑動中的玩家選站位」在物理上做不到（§5.8 的 Chase 區） | — |

### 1.4 中斷，以及 producer 看得到什麼

| 狀態 | 是否消費 `MovementIntent` | 時長（Bake） | 證據 |
|---|---|---|---|
| Action（出拳） | ❌（Punch Bake 水平位移 0） | `Bake_Fists_Punch_R` **0.80 s** | `docs/55` §1.5；`.asset:1366` |
| Hurt | ❌（只有垂直） | `Bake_Fists_Hit_Right` 0.70 s／`_Left` 1.17 s；`Idle_Hit_Strong_*` 3.2–4.1 s（❓**Y Bot 實際用哪顆，待確認**） | `HurtState.cs:16`（時長取自 Bake） |
| Death | ❌ | — | producer 自己收手 `AIMovementSource.cs:202–207` |

- producer 在 Action／Hurt 期間**照常每帧執行**，只是輸出沒人消費（`docs/55` §1.5）。
- producer **看不到** FSM 狀態（A4 禁止 `Core/Movement` 引用 StateMachine）。黑板上能看到的只有：`Survivability.JustTookDamage`（單帧脈衝）、`Intent.RequestedActionSlot`（**請求 ≠ 執行**，ActionState 可能拒絕）。
- ⚠️ **卡住偵測會在出拳／受擊時誤報**：`StuckWindowSeconds = 0.75`，而出拳 0.80 s、受擊 0.70–1.17 s——這段時間意圖有發布、角色沒動 ⇒ `IsStuck = true`。**今天沒有 bug**：交戰分支不讀 `IsStuck`，非戰鬥分支在受擊時已經被激怒進交戰。但 **P5 若直接拿 `IsStuck` 觸發換位，就會在每次出拳結尾換位**。

### 1.5 可重用的導航能力

| 能力 | 位置 | P5 能否直接用 |
|---|---|---|
| 每帧同步、請求節流、`PathPartial`、卡住回報 | `EnemyNavigator`（`EnemyNavigator.cs:64`） | ✅ 目的地從「目標」改成「站位點」即可 |
| 唯讀查詢介面 | `IEnemyNavigationQuery`：`TrySampleWalkablePoint`、`IsPathComplete` | ⚠️ 需加三個查詢：直線可走、離邊緣距離、路徑長度（§5.3） |
| `IsPathComplete` | 同步 `NavMesh.CalculatePath`，`NavMeshPath` 重用 | ✅ 但成本較高 ⇒ 只對前幾名候選呼叫（§5.4） |
| 方向最後一步 | `EnemySteering.TryResolveDirection` | ✅ N6 separation 的接入點不變 |
| 純函數測試形狀 | `EnemyEngagementMovementTests`、`EnemyNavigatorTests`、`EnemyNonCombatBehaviorTests`（假的導航查詢） | ✅ |
| 真場景追擊量測 | `EnemyChasePlayModeTests`（出貨場景、6 m 起追、60 fps） | ✅ 可擴充成 §6 的情境 |

---

## 2. 缺口

| # | 缺口 | 證據 | 影響 |
|---|---|---|---|
| **T1** | 沒有「站位」概念：Strafe 只檢查下一步 0.6 m 是否出網，不評估「這個位置好不好」 | `:293–314`、`:470–477` | 敵人會沿切線側移到牆角、窄縫，然後兩側都不能走而站住 |
| **T2** | **Retreat 不看環境** | `:316–322` | 玩家把敵人逼到牆邊時，後退＝推牆或退出網 ⇒ `Recover` |
| **T3** | 沒有「理想攻擊距離」：帶的外緣（2.0）＝攻擊圈的邊緣（W10）；Strafe 在帶內任何距離都算「到位」 | `:46–48`；W10 | 停在 1.9–2.0 m 時，玩家一點點移動就讓「想不想打」閃爍 |
| **T4** | 換向由計時器與亂數決定，與玩家、環境無關 | `:479–494` | 這正是使用者說的「依固定距離左右移動」 |
| **T5** | Approach 的目的地永遠是目標本身；目標不可達 ⇒ 推牆 | `:278`、`:335` | `docs/55` §11.5 原本的 MVP 就是為它 |
| **T6** | 攻擊判定只看水平距離 | `AIInputSource.cs:141` | 隔著矮牆／欄杆 2 m 內也會揮拳；站位若不排除這種點，會主動站過去 |
| **T7** | 卡住回報在 Action／Hurt 中誤報（§1.4） | `EnemyNavigator.cs` Default | P5 不能直接用 `IsStuck` |
| **T8** | producer 看不到 Action／Hurt | A4 | 承諾規則不能依賴「現在在不在出拳」 |
| **T9** | 敵人交戰速度 ≈1.64 m/s，遠低於玩家跑步 | §1.3 | 玩家在跑時，任何以玩家為中心的站位都會被甩開——站位只在「玩家相對靜止或慢移」時有意義 |

---

## 3. Q1：擴充現有側移，還是獨立成 CombatPositioning 模組？

**結論：獨立模組，但第一步只接管「目標不可達」（§8 P5a）。** 理由逐條：

| 判準 | 擴充 Strafe | 獨立模組 |
|---|---|---|
| 問題本質 | Strafe 輸出的是**方向**；站位需要**一個被承諾的點**＋評分＋記憶。把「點」塞進方向函數，等於在 `AIMovementSource` 內再養一個狀態機 | 「站在哪」與「怎麼走過去」是兩個問題，正好對應既有的 Positioning → Navigator 分工 |
| `docs/55` §11 硬限制 | ⛔ `AIMovementSource` 已 575 行，§11 明令「只整合，不得長成大型單體」 | ✅ 與 `EnemyNonCombatBehavior`／`EnemyNavigator` 同一個切法 |
| 既有契約 | §11.5 已裁決 Owner＝獨立元件、不屬於 Recovery | ✅ 一致（本檔只修訂細節） |
| 測試 | 決策夾在 MonoBehaviour 裡，要靠反射與假 agent | ✅ 純類別＋假的 `IEnemyNavigationQuery`，EditMode 完整可測 |
| 風險 | 小步、可逆 | 要改交戰分支的接線（`docs/55` §11.2 寫明「交戰移動本輪不搬家，搬家需另外提出」）⇒ **需要你裁決（D2）** |

**擴充仍然值得做的部分**：T2（Retreat 不看環境）是既有程式的缺陷，不論選哪個方案都該修；在方案 B 裡它會自然被「退往站位點」取代。

---

## 4. 方案比較

| | **A 擴充 Strafe** | **B 獨立 Positioning（目標相對站位槽＋承諾）** | **C 只做 §11.5 MVP（不可達時換目的地）** |
|---|---|---|---|
| **做法** | 在 `AIMovementSource` 內：Strafe 加「牆距」與換向條件、Retreat 加可走性、Approach 不可達時改朝環上點 | 新元件 `EnemyTacticalPositioning`（`Core/Movement`，普通類別）輸出 **TacticalGoal**（以目標為圓心的方位角＋半徑）；`AIMovementSource` 把它投影成世界點交給 `EnemyNavigator`／`EnemySteering` | 同 B 的元件，但只在「Approach 到目標的路徑不完整」時啟用；帶內行為完全不變 |
| **能否滿足需求** | ⚠️ 部分：仍是「選方向」，不是「選位置」 | ✅ 依玩家位置、理想距離、可達性、障礙物選位 | ❌ 正常打鬥看不出差別 |
| **依賴方向** | ✅ | ✅ 只收 `CombatContext` 的值；不讀 Transform target（A54 擴充守） | ✅ |
| **架構風險** | ⛔ 違反 §11 單體限制 | 低：不新增黑板欄位、FSM 狀態、Runner 步驟、target 權威 | 最低 |
| **行為變動** | 中 | **大**（帶內的 Strafe 被取代）⇒ 需人工 Play | 小 |
| **測試成本** | 高 | 低（純函數＋假查詢） | 低 |
| **效能** | 與現在相同 | 每 0.5 s 一次評估（§5.4），單隻敵人可忽略；P4 時要算預算 | 只在不可達時評估 |
| **可擴充** | 差 | 好：分離（N6）、側翼（需目標朝向，D8）都是加一個評分項 | 好（同 B 的骨架） |

⛔ **不採用**：Utility／Influence Map runtime——`docs/21` §3 的升級觸發條件（5–6 種互相競爭的行為）尚未成立。

**推薦：B 的架構，分兩步交付**——P5a 只接管 C 的範圍（行為對正常打鬥中性，先證明元件與量測可信），P5b 再接管帶內站位（行為變動、需人工 Play）。

---

## 5. 推薦設計（方案 B）

### 5.1 模組與資料流

```text
順序 2.4  AICombatContextSource ─ 寫 CombatContext（不變；唯一目標權威）
                                     │（黑板值）
順序 2.5  AIMovementSource（唯一 MovementIntent writer，不變）
            ├─ ResolveEngagementMovement（既有，改當「區域分類器」：Chase／Position／TooClose）
            ├─ EnemyTacticalPositioning（🆕，只在 Position／TooClose 區被詢問）
            │     輸入：self、TargetPosition、理想半徑、now、受擊脈衝、IEnemyNavigationQuery
            │     輸出：TacticalGoal { HasGoal, BearingYaw, Radius, IsArrived }   ← ⛔ 不輸出方向或速度
            ├─ 投影：goalPoint = TargetPosition + R(BearingYaw)·Radius（每帧，跟著玩家平移）
            ├─ 怎麼走過去（§5.8）：直線可走 ⇒ EnemySteering；否則 ⇒ EnemyNavigator 路徑
            └─ 發布 MovementIntent
```

| 元件 | 形狀 | 唯一職責 | ⛔ 不做 |
|---|---|---|---|
| `EnemyTacticalPositioning` | 普通類別，`AIMovementSource` 持有一份；固定大小的候選陣列（零 GC） | 產生候選、過濾、評分、承諾、決定何時重評 | 不寫黑板、不讀 Transform／`Target`、不碰 `NavMeshAgent`、不決定攻擊 |
| `EnemyTacticalScoring` | static 純函數 | 單一候選的過濾與成本 | 無狀態 |
| `IEnemyNavigationQuery`（擴充） | 介面 | 加 `IsStraightLineClear`、`DistanceToEdge`、`TryGetPathLength` | 不暴露 agent |

**為什麼承諾「方位角」而不是「世界座標點」**：玩家平移時，方位角不變、點跟著走 ⇒ 不需要重評就能維持「我在玩家左後方」；只有環境改變（點掉進牆裡）或玩家移動夠遠才需要重評。承諾世界點則每一步玩家移動都會讓點失效，等於回到每帧重算。

### 5.2 Q2-①：候選位置

- **半徑**：理想半徑 `r*`＝交戰帶中點 `(minimumEngagementDistance + attackRange) / 2`＝(1.25 + 2.0)/2＝**1.625 m**——由既有兩個值**推導**，不新增手填常數（D4）。
- **方位**：以「自己目前相對目標的方位角」為 0，取固定偏移 `{0, ±30°, ±60°, ±90°, ±135°}`（9 個）。
  - 以自己的方位為錨，而不是世界格線：「原地不動」永遠是候選 0，連續性天然成立，不會被格線量化逼著移動。
  - 不含 180°：那需要穿過玩家正面，路徑代價高且不可讀。
- 候選陣列建構時配置一次，每次評估覆寫（零 GC）。

### 5.3 Q2-②：過濾（硬條件，不通過就淘汰）

| # | 條件 | 查詢 | 為什麼 |
|---|---|---|---|
| F1 | 候選點 0.5 m 內可取樣到 NavMesh，且取樣後到目標的水平距離仍在 [min, attackRange] | `TrySampleWalkablePoint` | 取樣會把點「吸」到牆的另一側或帶外 |
| F2 | **觸及線**：候選點到目標的 NavMesh 直線不被邊界打斷 | 🆕 `IsStraightLineClear(candidate, target)`（`NavMesh.Raycast`） | 修 T6：不站到矮牆／欄杆另一側去揮空拳 |
| F3 | 可達：自己到候選點直線可走，**或**完整路徑存在 | 🆕 `IsStraightLineClear`，不行才 `IsPathComplete` | 直線可走時省掉 `CalculatePath` |

### 5.4 Q2-③：評分與選擇

成本越低越好，各項正規化到 [0, 1]，權重為 authored 資料（serialized struct，與 `EnemyNavigationSettings` 同形）：

| 項 | 成本 | 來源 | 預設權重（測試值） |
|---|---|---|---|
| C1 移動代價 | 路徑長度（直線可走時用直線距離）÷ 正規化長度 | 🆕 `TryGetPathLength`（`GetCornersNonAlloc`） | 1.0 |
| C2 連續性 | 與目前方位的角度差 ÷ 135° | 純幾何 | 0.5 |
| C3 空間餘裕 | `max(0, clearanceTarget − DistanceToEdge) ÷ clearanceTarget`，`clearanceTarget` 1.0 m | 🆕 `DistanceToEdge`（`NavMesh.FindClosestEdge`） | 0.8 |
| C4 分離（保留） | 與其他敵人的距離 | P4（N6） | **0**（只留接入點） |
| C5 側翼（保留） | 相對目標朝向的角度 | 需要 `CombatContext` 加目標朝向 ⇒ 黑板 schema（D8） | **0** |

**評估成本上限**：先對 9 個候選做 F1、F2、C2、C3（便宜），按暫定分數排序，**只對前 2 名**做 F3／C1（可能要 `CalculatePath`）。每次評估最多 9 次取樣、18 次 Raycast、9 次 FindClosestEdge、2 次 CalculatePath；頻率見 §5.6。

全部候選被淘汰 ⇒ 不輸出 goal ⇒ `AIMovementSource` 退回現行行為（Strafe／直接朝目標）——**不引入新的靜止終態**（2026-09-15 裁決）。

### 5.5 Q3：Position Commitment 與 Hysteresis

| # | 規則 | 預設（測試值） | 防的是什麼 |
|---|---|---|---|
| H1 | **最短承諾時間**：承諾後 `T_commit` 內不做自願換位（硬失效除外，§5.6 E3） | 1.2 s | 分數小幅波動造成的來回 |
| H2 | **換位門檻**：新候選成本須比**目前槽重新計分後的成本**低 `M` 以上 | 0.15 | 兩個差不多的點互換 |
| H3 | **方位死區**：最佳候選與目前方位差 < 20° 視為同一槽 | 20° | 微調造成的小碎步 |
| H4 | **抵達遲滯**：距槽點 ≤ 0.35 m 進入「已抵達」；> 0.7 m 才離開 | 0.35／0.7 | 抵達判定在邊界閃爍 |
| H5 | **禁止 A→B→A**：剛放棄的方位在 3 s 內加成本懲罰 | 3 s／+0.3 | 兩槽之間的乒乓 |
| H6 | **區域遲滯**：距離 ≤ `attackRange + 1.0` 進入站位區；> `attackRange + 2.0` 才回到 Chase | 3.0／4.0 m | Chase 與站位之間的切換 |

**評估 ≠ 換位**：E2 的定期重評只是「看一看」，H1–H5 全部通過才真的換。

**到位後做什麼（D3）**：建議**有界環繞**——在已承諾方位 ±25° 的弧內沿用既有 Strafe（換向計時與 0.6 m 可走性探針都保留），弧端點換向。弧在評估時已經過 F1–F3，所以不會漂進牆角；外觀上保留現在「活著」的感覺。

### 5.6 Q4：何時重算戰術目標

| # | 觸發 | 動作 | 依據 |
|---|---|---|---|
| E1 | 剛進入站位區（H6）或剛交戰 | 立即完整評估 | 沒有承諾 |
| E2 | 距上次評估 ≥ 0.5 s | 完整評估，但受 H1–H5 約束 | 讓更好的位置有機會被發現；上限 2 次／秒 |
| E3 | **硬失效**（每 0.2 s 檢查目前槽一次：1 次取樣＋1 次 Raycast） | 立即完整評估，**無視 H1** | 目前槽點掉出 NavMesh、觸及線被擋、到槽的路徑變 `PathInvalid` |
| E4 | 目標自上次評估後移動 > 1.5 m | 把 E2 提前到現在 | 環境相對玩家改變了；純平移不需要（方位角已跟著走） |
| E5 | **持續卡住**：`IsStuck` 連續成立 ≥ 2 個窗（1.5 s） | 視同 E3 | 1.5 s > 出拳 0.80 s、受擊 0.70–1.17 s（T7）；測試以 Bake 時長守這個不等式（D7） |
| — | ⛔ 不觸發：每帧、玩家只轉身、離網 `Recover` 期間、已死亡 | — | — |

### 5.7 Q5：中斷對承諾的影響

| 事件 | producer 看到什麼 | 對承諾 |
|---|---|---|
| 自己出拳（0.80 s，不消費意圖） | 看不到（只看得到請求） | **保留承諾**。計時照走（H1 只擋自願換位，過期無害）；E5 的 1.5 s 窗不會被出拳觸發 |
| 受擊（0.70–1.17 s） | `JustTookDamage` 單帧 | **保留承諾**（D6 建議）。受擊不代表站位錯——近戰敵人站在攻擊距離內本來就會被打 |
| 長硬直（`Idle_Hit_Strong_*` 3.2–4.1 s，❓是否會發生在 Y Bot） | 同上 | 會讓 E5 誤觸發一次 ⇒ 最壞是恢復後重評一次，不會振盪；若成立，D7 改用受擊脈衝把卡住窗重設 |
| 自己死亡 | `Survivability.IsDead` | `Reset()`：清空承諾（producer 已在 `:202` 收手） |
| 目標死亡／脫戰（含 leash） | `CombatContext.InCombat == false` | `OnDisengaged()`：清空承諾；再交戰從 E1 開始 |
| 離網 `Recover` | `!IsOnNavMesh` | **凍結**：不評估、不換位；回網第一帧跑 E3 檢查 |
| 環繞弧兩端都不可走 | 可走性探針 | 站在槽點，不是終態：下一次 E2 會重評 |

### 5.8 Q7：何時用簡單 Steering，何時要 NavMesh 路徑

| 區域／情況 | 怎麼走 | 理由 |
|---|---|---|
| **Chase 區**（距離 > H6 外圈） | 現行 Approach：路徑到**目標本身** | 玩家比敵人快（T9），沒有站位可言 |
| 站位區、自己→槽點 NavMesh 直線可走、距離 ≤ 3 m | **直接 steering**（不請求路徑） | 絕大多數情況；零路徑成本 |
| 直線會穿過玩家身體（線段距目標 < 0.8 m） | 沿環繞方向取中繼點，仍是直接 steering | NavMesh Raycast 看不到玩家膠囊 |
| 直線被擋（柱子在中間） | `EnemyNavigator.RequestDestination(goalPoint)`（既有節流） | 需要繞 |
| 目標不可達（P5a 的範圍） | 路徑到「環上可達點」 | 修 T5 |
| 太近（< min） | 退向**槽點**（直線可走才退；不行就沿弧側移） | 修 T2：取代盲目的 `-toTarget` |

### 5.9 Q6：與攻擊決策協作，不產生新的 Target Authority

| 規則 | 落地 | 守門 |
|---|---|---|
| 目標只有一個來源 | 站位只收 `CombatContext.TargetPosition` 這個**值**，由 `AIMovementSource` 傳入 | 🆕 擴充 A54：`EnemyTacticalPositioning.cs` 不得出現 `Transform`／`Target`／`GetComponent`／`Project.Core.Combat` |
| 「想不想打」不搬家 | `AIInputSource.WantsToAttack` 照舊；站位**不**寫任何「可以出手」旗標 | 黑板不加欄位（A5 不變） |
| 兩者靠同一組距離契約對齊 | `r*` + 抵達遲滯外緣（0.7 m）的徑向分量必須 < `attackRange` | 🆕 比照 W10：測試讀 prefab 推導，不手填 |
| 觸及線 | 站位用 F2 排除「隔牆」的點；`AIInputSource` 是否也加觸及線檢查 ⇒ **D5** | — |
| 攻擊節奏 | 站位不知道冷卻（`ActionState` 的事），也不需要知道；冷卻期間的重評自然變成「出拳之間調整位置」 | — |
| 朝向 | 不新增輸入；Y Bot 交戰中本來就持續面向目標（Priority 2） | A31／A32 |

---

## 6. Q8：怎麼證明它真的在改善站位，而不只是多動

⚠️ **不能用站位自己的評分當驗收指標**——那等於用答題的人改自己的考卷（本 repo 的「量測代理答非所問」教訓）。驗收全部用**外部可觀察量**，並在實作**前**先量出現行版本的基線，A/B 比較。

### 6.1 外部指標

| # | 指標 | 為什麼代表「站位變好」 |
|---|---|---|
| M1 | **有效攻擊位置時間比**：距離在 [min, attackRange] **且**觸及線暢通的帧比例 | 站位的目的就是能打 |
| M2 | `AIInputSource` 送出的攻擊請求數／分鐘 | 同上，從攻擊端看 |
| M3 | 失效帧數：兩側不可走、`Recover`、持續卡住 | 修 T1／T2 的直接證據 |
| M4 | **移動效率**：移動距離 ÷ M1 時間 | 防「多動但沒變好」 |
| M5 | 換位次數／分鐘、A→B→A 次數 | 防振盪 |
| M6 | 評估次數、`CalculatePath` 次數、路徑請求數、GC bytes | 成本 |

### 6.2 情境（PlayMode，腳本化目標軌跡，固定種子、60 fps）

| # | 情境 | 通過條件（相對同情境的現行基線） |
|---|---|---|
| S1 | 空地、目標靜止 | M1 不低於基線；**M4 不高於基線 × 1.2**（不得多動） |
| S2 | 目標背靠牆角 | M3 降為 0；M1 高於基線 |
| S3 | 柱子在敵人與目標之間 | 繞過柱子抵達槽點；全程不出現「距離 ≤ attackRange 但觸及線被擋」且站定 > 0.5 s |
| S4 | 目標站在不可達平台上 | 走到環上可達點並停住；不無限推牆（P5a 主要驗收） |
| S5 | 目標以 ≤ 敵人速度繞圈 | M5 ≤ 每分鐘 N 次、A→B→A ＝ 0；M1 不低於基線 |
| S6 | 目標以跑速風箏 | 進入 Chase 區；評估次數 ≈ 0（不在追不上的目標身上浪費評估） |
| S7 | 敵人出拳中、受擊中 | 出拳／受擊期間 0 次由 E5 觸發的換位 |

### 6.3 EditMode（純函數＋假的 `IEnemyNavigationQuery`）

- 過濾 F1–F3、評分 C1–C3、前 2 名才算路徑。
- H1–H6 逐條；以抖動的目標位置序列餵入，斷言換位次數 ≤ 上限（比照現行遲滯測試）。
- E1–E5 逐條（注入時間）；中斷表 §5.7 逐列。
- 全部候選被淘汰 ⇒ 不輸出 goal（退回現行行為），不靜止。
- 架構：A4（白名單）、A5（站位檔不得寫 `MovementIntent`——既有 WriterRules 自動涵蓋）、A16 擴充到新檔、A54 擴充（§5.9）。
- 資料推導：`r*` 在攻擊圈內（§5.9）；E5 的窗 > Y Bot 使用的出拳／受擊 Bake 時長（D7）。

### 6.4 人工 Play（只留給手感）

站位是否看起來「有意圖」而不是亂晃；牆角、柱子周圍是否合理；有界環繞是否自然；玩家能否讀懂敵人要去哪。

---

## 7. ADR 判斷

| 判準（CLAUDE.md） | 推薦設計 | 結論 |
|---|---|---|
| ① 黑板 schema／ownership | 不加欄位；`MovementIntent` 寫入者不變 | 不觸發 |
| ② FSM 拓撲／階層 | 不加狀態 | 不觸發 |
| ③ 管線順序／核心介面 | Runner、`IMovementIntentSource` 不變 | 不觸發 |
| ④ 推翻不變量 | A4／A5／A16／A54 全部維持（並擴充） | 不觸發 |

⇒ **不開 ADR**；P5 契約寫在本檔（`docs/55` §11.5 改為指向這裡）。
⚠️ **例外**：D7 若選「黑板發布意圖是否被消費」、或 D8 選「`CombatContext` 加目標朝向」，即觸發判準 ①，需另行評估（ADR-007 Trial 修訂或新 ADR）。

---

## 8. 分階段

| 階段 | 內容 | 行為變動 | 驗收 |
|---|---|---|---|
| **P5-0 基線量測** | 先寫 §6.2 S1–S7 的量測測試，對**現行**程式跑出數字並記錄 | 無 | 數字寫進本檔 |
| **P5a 元件＋不可達接管** | `EnemyTacticalPositioning`、評分、承諾、查詢介面擴充、EditMode 全套；只在 Approach 到目標的路徑不完整時啟用（＝§11.5 MVP） | 只有 S4 情境改變 | S4 通過；S1–S3、S5–S7 與基線**逐值相同**；0 GC |
| **P5b 帶內站位** | 站位區（H6）由槽點取代自由 Strafe；有界環繞；退向槽點（修 T2） | **大** | §6.2 全部；人工 Play 一次 |

每階段一個 commit。不需要任何 prefab／scene 手動接線（設定值以 serialized 預設值進來），Integration Gate 只有人工 Play。

---

## 9. 需要你裁決的問題

| # | 問題 | 選項 | 建議 |
|---|---|---|---|
| **D1** | 方案與範圍 | ①A 擴充 Strafe ②B 一次到位 ③C 只做不可達 ④**B 架構、分 P5a／P5b 交付** | ④ |
| **D2** | 准不准改交戰分支的接線（`docs/55` §11.2 寫明「需另外提出」） | ①准：P5b 讓 Strafe／Retreat 走站位 ②只准 P5a（Approach 不可達時） | ①，但分兩步，P5a 之後再確認一次 |
| **D3** | 到位後做什麼 | ①站定等冷卻 ②**有界環繞**（±25° 弧內沿用既有 Strafe）③維持現行自由 Strafe | ②：保留現在的生氣，又不會漂進牆角 |
| **D4** | 理想攻擊距離 | ①交戰帶中點（推導，1.625 m）②authored 偏移量 | ①：不新增手填常數 |
| **D5** | 攻擊判定要不要加觸及線 | ①只在站位過濾（F2），攻擊仍只看距離 ②`AIInputSource` 也加 `NavMesh.Raycast` 檢查 | ①先做，S3 若量到隔牆揮拳再做② |
| **D6** | 受擊對承諾的影響 | ①保留承諾 ②受擊後允許提前換位（跳過 H1）③受擊後後退 | ①（MVP）；②③是手感決策，Play 後再定 |
| **D7** | 卡住誤報的處理 | ①**以時長過濾**（E5 窗 1.5 s，測試讀 Bake 時長守不等式）②黑板發布「意圖是否被消費」（觸發 ADR 判準 ①） | ① |
| **D8** | 側翼評分（繞到玩家背後） | ①不做 ②`CombatContext` 加目標朝向（黑板 schema 變更） | ①：單一敵人時繞背沒有意義，與 P4 一起再談 |

⛔ 本輪沒有改任何程式、prefab、場景或測試；巡邏（P1–P3）與 P4 未動。

---

## 10. 裁決與實作基線（2026-10-13，使用者裁決）

### 10.1 八項裁決

| # | 裁決 | 對 §1–§9 的修正 |
|---|---|---|
| **D1** | 採 **B**：獨立 `EnemyTacticalPositioning`，避免 `AIMovementSource` 膨脹 | — |
| **D2** | 允許**局部**修改交戰移動分支的接線；⛔ 不得改 `CombatContext` 目標權威、`MovementIntent`／方向權威（`AIMovementSource` 唯一寫入者、`CharacterFacingSource` 唯一 facing sender）或任何跨層規則（A4／A5／A16／A27／A54） | — |
| **D3** | ⛔ **不採**固定 ±25° 弧內持續移動。到位後依**站位有效性**選擇：待命、短距離調整、或有目的地側移。⛔ 不得為了「看起來在動」而移動 | §5.5「到位後做什麼」改為 §10.2 |
| **D4** | 初始理想距離＝交戰帶中點（1.625 m）；**實際值須以攻擊有效距離與動畫表現驗證**（P5b 驗收項） | §5.2 的數值降為「初始值」 |
| **D5** | P5a **先排除隔牆或不可達的候選站位**；攻擊有效性需要一份**共用幾何契約**，避免 `AIInputSource` 與 Positioning 互相矛盾。⛔ 本輪不改寫攻擊判定，先評估契約歸屬 | §5.9 D5 列改為 §10.3（研究提案） |
| **D6** | 一般攻擊與受擊期間**保留**承諾；目標失效、脫戰、死亡時**清除**；只有站位**真正失效**時才允許重評 | §5.6 E2／E4 降級，見 §10.2 |
| **D7** | ⛔ **不採**「只用 1.5 s 門檻過濾卡住」。要從既有管線取得「有移動請求、實際被允許移動、但沒有有效進展」的可靠資訊；合法的攻擊、受擊、Traversal 或其他移動限制都不得算 Stuck；⛔ 不得新增第二套移動授權權威 | §5.6 E5 撤銷，改為 §10.4（研究提案） |
| **D8** | 暫不做繞背／側翼評分；⛔ 不擴充 `CombatContext` schema | C5 保持權重 0 |

**跨契約守門**：D5 或 D7 若需要新增跨層契約、黑板欄位或 ADR ⇒ **先提出方案與依賴影響，不直接實作**。

### 10.2 D3／D6 改寫後的站位行為

**到位後的三種動作（D3）**——每次都由「目前站位是否仍有效」決定，不由計時器決定：

| 動作 | 條件 | 移動 |
|---|---|---|
| **待命** | 目前位置有效：在 [min, attackRange] 內、觸及線暢通、不貼牆 | 不發布移動意圖（朝向照舊由 Priority 2 面向目標） |
| **短距離調整** | 位置仍在有效槽內，但到目標的距離漂出 `r*` 的容許帶（遲滯：出 ±0.3 m 才調、回到 ±0.1 m 停） | 只沿目標連線做**徑向**修正，不側移 |
| **有目的地側移** | 目前槽**失效**（觸及線被擋、掉出 NavMesh、貼牆不可退），或通過 H1–H5 的換槽 | 走向**被選中的新槽點**；抵達即回到待命 |

⇒ 現行 Strafe 的**計時換向**（`AIMovementSource.cs:479–494`，T4）在 P5b 由「失效才換槽」取代；P5a 不動它。

**重評（D6）**——§5.6 的觸發表修正為：

| # | 觸發 | 修正 |
|---|---|---|
| E1 | 剛進入站位區／剛交戰 | 不變 |
| E2 | 定期 0.5 s | **撤銷**為自願換位的觸發：定期只做**有效性檢查**（便宜的 1 次取樣＋1 次 Raycast），不重新挑槽 |
| E3 | 硬失效 | 不變——唯一允許重評的常態入口 |
| E4 | 目標移動 > 1.5 m | 改為「**重檢目前槽的有效性**」；有效就保留 |
| E5 | 持續卡住 | **撤銷**，待 D7（§10.4）裁決後以可靠訊號取代 |
| — | 一般攻擊、受擊 | **不清除、不重評**（D6） |
| — | 目標死亡、脫戰（含 leash）、自己死亡 | **清除**（D6） |

### 10.3 D5 研究：攻擊有效性的共用幾何契約——歸屬（🟠 提案，未裁決）

**問題**：「想不想打」（`AIInputSource`，順序 1）與「該站在哪」（Positioning，順序 2.5）各自回答「這個位置打得到嗎」。今天前者只看水平距離，後者將看距離＋觸及線——**兩個答案會不一致**：Positioning 判定隔牆、離開那個位置的同時，`AIInputSource` 仍判定「在 2 m 內，出手」。

**契約內容**（不論誰擁有都要回答這四件事）：①距離怎麼量（root 對 root 的水平距離，現況）；②觸及上限 R（今天＝2.0）；③觸及線（中間是否有阻隔，今天不存在）；④高低差容許（今天不存在——平台上的玩家只要水平 ≤ 2 m 就會被「打」）。

| 選項 | 擁有者 | 依賴影響 | 評估 |
|---|---|---|---|
| **O1 單一資料資產** | 新 `MeleeEngagementProfile`（SO）同時被 `AIInputSource`、`AIMovementSource` 引用；幾何函數是它的方法 | 新資產＋兩個 prefab 欄位改接（Editor API）；W10「兩份相等」被「一份引用」取代；`Core/Pipeline` → `Core/Movement` 多一個型別依賴（方向已存在：`AIInputSource` 讀 `AIMovementSource.Target`） | ✅ 根治「兩份資料」；資產改動需 Integration Gate |
| **O2 共用純函數，資料不動** | `Core/Movement` 的 static `MeleeEngagementGeometry`（距離定義、範圍判定、理想半徑、觸及線查詢）；兩個元件各自呼叫；R 仍是兩個欄位＋W10 守相等 | 只有程式依賴；不改資產 | ✅ 最小；資料仍兩份（靠測試守） |
| O3 `AIInputSource` 問 Positioning | Positioning | `AIInputSource` 呼叫移動的**決策邏輯** ⇒ 違反 `docs/54` T1「只能是參照資料依賴」 | ⛔ |
| O4 經黑板發布「打得到」 | `AICombatContextSource`（2.4）計算、`CombatContext` 加欄位 | **黑板 schema（ADR 判準 ①）**；且 `AIInputSource` 在順序 1、早於 2.4 ⇒ 只能讀上一帧；A27 禁止 `AIInputSource` 讀 `PlayerRuntimeData` ⇒ **要推翻 A27** | ⛔ 成本最高、要 ADR |

**觸及線用哪種幾何**：NavMesh（「同一可走區域、直線不跨邊界」）或 Physics（膠囊／射線打不打得到牆）。兩者對矮欄杆會給不同答案。建議 **NavMesh 版**——攻擊者本來就必須站在 NavMesh 上，且 Positioning 已用同一套查詢；Physics 版留給「打到沒打到」的 hitbox 自己。

**建議**：先 **O2**（不需 ADR：不碰黑板、FSM、管線、核心介面；是 `Core/Pipeline` → `Core/Movement` 的**新呼叫契約**，依你的守門規則先提出），之後若 R 需要依動作而變（多招式）再升級 **O1**。④高低差另案。
**P5a 怎麼避免矛盾擴大**：P5a 不碰 `AIInputSource`；Positioning 只在「不可達」與「隔牆」兩種情況介入，並以同一個水平距離定義判斷 R。已知殘留：隔牆時 `AIInputSource` 仍可能出手——P5-0 量出它的發生頻率（`attacksWhileReachBlocked`），O2 裁決後才修。

### 10.4 D7 研究：可靠的「沒有有效進展」訊號（🟠 提案，未裁決）

**現況證據**：

| 事實 | 證據 |
|---|---|
| 唯一知道「本帧被允許移動、要求多少、實際走了多少」的地方是 `MotionDriver.ExecuteBaseMovement`：它由 FSM 狀態呼叫（Idle／Move／無 Bake 的 Action），計算 `horizontalVelocity = MoveDirection × MoveSpeed × moveSpeed`，再 `CharacterController.Move` | `MotionDriver.cs:528–585` |
| 不被允許移動的路徑都走別的方法：`ExecuteVerticalOnlyMovement`（Hurt／Death）、`ExecuteBakedCurveMovement`（有 Bake 的 Action）、Traversal／Committed／Warp、`ExecuteLocomotionStopMovement`；hit-stop 時 `IsTimeFrozen` 直接 return | `:531`、`:804`、`:823`、`:866`、`:1367`、`:1559` |
| 這些事實**沒有任何一項**被發布到黑板；`MotionDriver` 目前只發布 `IsGrounded`／`VerticalVelocity`／`JustLanded`／`JustLeftGround` | `:2757–2772`；A5 `WriterRules` |
| `Core/Movement` 不得引用 `Project.Presentation`（A4）⇒ producer 不能直接問 `MotionDriver` | `ArchitectureRegressionTests.cs:344–346` |
| producer 可見的 `MoveSpeed`（順序 3）在 Hurt／Action 期間照樣非零——它是意圖的動態結果，不是「被允許」 | `docs/55` §1.5 |

⇒ **結論：既有管線裡沒有這份資訊。** 任何「不新增契約」的做法都只能用時間或脈衝去猜（＝被否決的 1.5 s 方案）。

| 選項 | 做法 | 依賴影響 | 評估 |
|---|---|---|---|
| **S1 MotionDriver 發布執行事實（建議）** | 黑板新 struct（暫名 `MotionExecutionData`）：`BaseMovementExecuted`、`RequestedHorizontalDistance`、`AchievedHorizontalDistance`；**唯一寫入者 `MotionDriver`**，在 `ExecuteBaseMovement` 填、其餘路徑與 hit-stop 填「未執行」。`EnemyNavigator` 下一帧讀它判定 Stuck＝「已執行 ∧ 要求 > 門檻 ∧ 達成 < 要求 × k」 | **黑板 schema（判準 ①）⇒ 需要 ADR（Trial）**；A5 加一條 WriterRule；讀者只有 producer（順序 2.5，讀上一帧——與 `IsGrounded` 同一個先例）；玩家側不受影響 | ✅ 只**回報**唯一移動權威做了什麼，不是第二個授權者；判斷仍在消費端 |
| S2 Core 介面旁路 | `Core` 定義 `IMotionExecutionReport`，`MotionDriver` 實作，`AIMovementSource` `GetComponent` | 不改黑板，但繞過黑板讓 gameplay 直接問另一個系統——違反「Gameplay reads data」原則 | ⛔ |
| S3 時間／脈衝推測 | `JustTookDamage`、`RequestedActionSlot`＋門檻 | 無 | ⛔ 被 D7 否決；且請求 ≠ 執行（ActionState 可拒絕） |
| S4 FSM 狀態寫「本帧消費意圖」 | 各 State 寫黑板 | 多寫入者；順序 4 晚於 2.5 | ⛔ |

**S1 的細節（供裁決）**：
- 為什麼不是第二個移動授權權威：它在位移**之後**才寫、內容是已發生的事實；沒有任何人能用它**允許或禁止**移動。
- 為什麼量「距離」不量「速度」：`CharacterController.Move` 的碰撞結果就是位移差；hit-stop、步階輔助都已反映在裡面。
- 推牆對象若是玩家膠囊，也會被判為無進展——近戰站位在 [min, max] 帶內不貼玩家，屬可接受；P5b 驗收要量。
- 若不採 S1：P5b 不使用卡住訊號，只靠幾何失效（E3）觸發換槽；`IsStuck` 維持只回報（P0.4 現狀）。

### 10.5 P5a 範圍（依本次裁決收窄）

**只在兩種情況介入，其餘帧與現行程式逐行相同：**

| 情況 | 判定（不用 Stuck、不用計時推測） | 介入 |
|---|---|---|
| **A1 不可達** | 到目標的路徑不完整，且路徑可達終點離目標的水平距離 > `attackRange` + 0.25（離開：≤ `attackRange`） | 改去**候選站位**中最好的可達點；沒有任何候選通過時，以「最接近目標的可達點」（部分路徑終點）為站位；**抵達後待命**，不推牆 |
| **A2 隔牆** | 水平距離 ≤ `attackRange` + 0.5，且自己到目標（投影到 NavMesh）的直線跨過 NavMesh 邊界，**連續兩次檢查（0.2 s 間隔）**成立；離開同樣要連續兩次暢通 | 改去觸及線暢通、可達的候選站位；抵達且暢通 ⇒ 交還現行行為 |

- 候選過濾依 §5.3 F1–F3（D5：排除隔牆與不可達）；評分依 §5.4 C1–C3；承諾依 §10.2（只因失效而換）。
- 投影目標到 NavMesh 再做直線檢查：玩家貼牆時位置在 NavMesh 侵蝕帶外，直接 Raycast 會誤判「隔牆」——P5-0 的牆角情境就是這個守門。
- 已知殘留：A2 期間 `AIInputSource` 仍以距離決定出手（§10.3）。

### 10.6 P5-0 量測方法

見 §11（量測紀錄）。

## 11. P5-0 基線量測紀錄（2026-10-14，現行行為，⛔ P5a／P5b 未開始）

> **狀態**：量測完成，**兩輪**（不是單次）。數字描述的是 `056edca` 的**現行**交戰移動；它是 P5a／P5b 的 A/B 對照基準，不是驗收結論。

### 11.1 方法與重跑

| 項目 | 內容 |
|---|---|
| 工具 | `Assets/_Project/Tests/PlayMode/EnemyCombatPositioningMetrics.cs`（`[Category("Probe")]`＋`[Explicit]`，不進回歸套件；只量不判，唯一的斷言是前提：交戰帧過半、讀得到 `MotionDriver` max-speed） |
| 情境 | 每個情境在執行期建獨立場景（60×60 m 地面＋方塊障礙＋執行期烘焙 NavMesh），放真 `Assets/Prefabs/Y Bot.prefab`；目標是測試逐帧移動的空 GameObject（無生命值、無碰撞——拳打不到任何東西）；Leash 放大到 30/40/50 m（只量交戰，不量脫戰）；`Random.InitState(1234)`；`Time.captureDeltaTime = 1/60` |
| 指標來源 | 全部是外部可觀察量（§6.1）：位置、距離、NavMesh 觸及線、FSM 狀態；producer 內部欄位只用反射**讀**（模式、側移方向、`_strafeBlocked`、`IsStuck`、`PathRequestCount`），不參與判定站位好壞 |
| 命令（安全入口） | `bash docs/artifacts/editor-memory/safe_unity_test.sh --mode PlayMode --include-explicit --filter EnemyCombatPositioningMetrics --label p5-0-baseline --timeout 1500` |
| 輸出 | 每情境一行 `Logs/p5-metrics/metrics.tsv`（含 commit、變體標籤 `current`）；同一行也印在 `Logs/Editor.log` 的 `[P5 metrics]` |
| 本次執行 | 環境：`unity status` ready（本 repo，6000.5.1f1）、`--check-only` CHECK_OK（`Logs/safe-test/20261014T021355Z-p5-0-check`）、唯讀探針確認 `SampleScene` 未 dirty、非 Play。第 1 輪 `20261014T021422Z-p5-0-baseline`：**PASS** 8/8（166.5 s）；第 2 輪 `20261014T021828Z-p5-0-baseline-r2`：**PASS** 8/8（165.9 s）。HEAD `056edca`；工作樹當時有使用者 WIP（`SampleScene`、`ProjectSettings/EditorSettings.asset`、ProBuilder 設定、音訊／材質資產、`StandingPostureSolverTests.cs`）——探針不載出貨場景，但 `ProjectSettings` WIP 仍是當時環境的一部分 |

### 11.2 指標表（兩輪；不同時記為「第 1 輪 / 第 2 輪」）

帶＝[1.25, 2.0] m（`minimumEngagementDistance`／`attackRange`，Y Bot）；有效＝在帶內**且** NavMesh 觸及線暢通；秒數＝帧 ÷ 60。

| 情境 | attackValid | 帶內 s / 帶內但觸及線被擋 s | 首次有效 s | 平均距離 m | 移動距離 m | m／有效秒 | 攻擊 / 無效攻擊 |
|---|---|---|---|---|---|---|---|
| OpenStatic | 93.2% | 18.65 / 0.00 | 1.35 | 1.590 / 1.537 | 23.49 / 22.61 | 1.260 / 1.212 | 6 / 0 |
| Corner | 85.4% | 17.08 / 0.00 | 2.92 | 1.923 | 24.23 | 1.418 | 5 / 0 |
| Pillar | 77.2% | 16.92 / **1.47** | 4.55 | 1.773 / 1.709 | 22.57 / 22.52 | 1.461 / 1.458 | 5 / **1** |
| ThinWall | 77.1% | 15.83 / **0.42** | 4.58 | 1.804 / 1.767 | 23.12 / 23.36 | 1.500 / 1.515 | 5 / 0 |
| Unreachable | **0.0%** | 0.00 / 0.00 | —（從未有效） | 3.726 | 8.23 | ∞ | 0 / 0 |
| MovingSlow（25 s） | 80.0% | 20.00 / 0.00 | 2.55 | 1.914 / 1.896 | 31.24 / 31.20 | 1.562 / 1.560 | 6 / 0 |
| Kiting | 0.0% | 0.00 / 0.00 | — | 4.698 | 31.36 / 31.34 | ∞ | 0 / 0 |
| AttackAndHurt | 93.2% | 18.65 / 0.00 | 1.35 | 1.506 | 14.89 | 0.799 | 5 / 0 |

| 情境 | noProgress / allowedMove 帧 | recover | strafeBlocked | 模式切換 | strafe 翻面 | 路徑請求 | stuck / 其中出拳或受擊中 | Action 帧（最長段） | Hurt 帧 / 段（最長段） |
|---|---|---|---|---|---|---|---|---|---|
| OpenStatic | 0 / 934 / 898 | 0 | 0 | 9 / 5 | 7 | 1 | 52 / 78 · 34 / 42 | 263 / 294（49） | 0 |
| Corner | 0 / 951 | 0 | 0 | 13 | 14 | 1 | 54 · 28 | 245（49） | 0 |
| Pillar | 0 / 946 | 0 | 0 | 7 | 7 | 1 | 64 / 74 · 35 / 42 | 245（49） | 0 |
| ThinWall | 0 / 957 / 969 | 0 | 0 | 7 | 6 | 1 | 41 / 51 · 27 / 29 | 240 / 228（49） | 0 |
| Unreachable | **804 / 1195** | 0 | 0 | 0 | 0 | 1 | **709** · 0 | 0 | 0 |
| MovingSlow | 0 / 1197 | 0 | 0 | 44 / 40 | 9 / 7 | 79 | 56 / 64 · 27 / 30 | 294（49） | 0 |
| Kiting | 0 / 1181 | 0 | 0 | 0 | 0 | **94** | 0 · 0 | 0 | 0 |
| AttackAndHurt | 0 / 579 | 0 | 0 | 3 | 7 | 1 | 153 · 89 | 197（49） | 417 / 9 段（**43**） |

### 11.3 每個情境的觀察

- **OpenStatic**：1.35 s 進帶，之後 93% 時間站在可打位置；每帧位移都有進展（noProgress 0）。這是 S1「不得多動」的對照：m／有效秒 ≈ 1.2–1.3。
- **Corner**：85%，**沒有出現 T1／T2 失效**（strafeBlocked 0、recover 0、noProgress 0），但 strafe 翻面 14 次、模式切換 13 次是各情境最高。⚠️ ⇒ 這個幾何**沒有重現**「兩側不可走／推牆」——§6.2 S2 的「M3 降為 0」在此基線上已經是 0，**不能拿來證明 P5 修好了 T1／T2**；要當驗收情境，先把牆角收緊（目標更深入、敵人起點在牆內側）再量一次基線。
- **Pillar**：帶內但觸及線被柱子擋 1.47 s，其中**出拳 1 次**（隔柱出拳）；首次有效晚到 4.55 s。這是 A2（隔牆）會介入的典型帧。
- **ThinWall**：起點就在帶內但隔牆（距離 2.0 m）；被擋只持續 0.42 s，**沒有隔牆出拳**（無效攻擊 0）——敵人繞過牆（整段只請求 1 次路徑），4.58 s 抵達有效位置。即現行版本在這個幾何下不會「隔牆打」，代價是首次有效時間長。
- **Unreachable**：**推牆**——被允許移動的帧有 **67%（804／1195）**沒有進展，`IsStuck` 為真 709 帧（11.8 s）；平均距離 3.73 m（> 2.0），20 s 內 0 次有效、0 次攻擊、只請求 1 次路徑（不重新規劃）。這是 P5a A1 的主要驗收對象（§6.2 S4）。
- **MovingSlow**：80%；模式切換 44／40 次（約 1.7 次/s）、路徑請求 79——目標移動時現行版本頻繁在模式間切換。S5 的振盪門檻要以此為基準（A→B→A 次數本工具**未量**，見 §11.5）。
- **Kiting**：追不上（平均 4.70 m、0% 有效）；路徑請求 94（約 4.7 次/s）、無卡住。S6「評估次數 ≈ 0」的成本對照是這個數字。
- **AttackAndHurt**：受擊 10 次（每 2 s 一次，第 1 次在 1.5 s），9 段完整、最後一段被測試結束截斷（30 帧）⇒ 417 = 9 × 43 + 30。**出拳／受擊中的 `IsStuck` 誤報 89 帧**，其他情境也都有 27–42 帧——§1.4 的預測（`StuckWindowSeconds 0.75 s` < 出拳 0.80 s／受擊 0.70 s）在執行期成立。

### 11.4 回答 §1.4 待確認項：Y Bot 實際受擊時長

**Hurt 每段 43 帧 ≈ 0.717 s**（9／9 段相同、兩輪相同）⇒ 對應 `Bake_Fists_Hit_Right`（0.70 s，對齊到 60 fps 帧界多 1 帧），**不是** `Idle_Hit_Strong_*`（3.2–4.1 s）。出拳每段 49 帧 ≈ 0.817 s，對應 `Bake_Fists_Punch_R` 0.80 s。
⚠️ 限制：探針用 `CharacterHealth.ApplyDamage(1f)`（不帶來源方向），只觸發到 Right；`_Left`（1.17 s）是否在帶方向的受擊中出現，本次**未量**。D7／E5 的窗若要覆蓋最長受擊，仍以 1.17 s 為上限保守設計。

### 11.5 量測限制（不隱藏）

- **帧對齊**：協程在 Update 後、LateUpdate（`MotionDriver` 位移）前恢復 ⇒ 第 N 次恢復看到的是第 N−1 帧的位移結果。「有沒有進展」用**上一帧記下的**意圖、速度與 FSM 狀態配對本帧觀察到的位移。
- **noProgress 的判定讀 FSM 狀態**：只有上一帧狀態是 Idle／Move（`ExecuteBaseMovement` 會執行的狀態）、有移動請求、期望步長 > 0.3 × 1/60 m 時才計入 allowedMove；位移 < 期望 × 0.25 ⇒ noProgress。**測試端讀得到 FSM 狀態，runtime 的 producer 讀不到**——這正是 D7 提案（§10.4 S1：`MotionDriver` 發布執行事實）要給 runtime 的資訊。期望步長＝`MoveSpeed × MotionDriver.moveSpeed × Δt`，是近似值。
- **觸及線**是 NavMesh Raycast（目標先投影到 NavMesh，半徑 2.5 m），不是物理視線；障礙在 NavMesh 上是挖空的，所以兩者在本情境一致。
- **帶的邊界是常數**（1.25／2.0，取自 Y Bot prefab 當下的設定）；prefab 改了要同步改探針。
- **目標沒有膠囊、沒有生命值**：敵人的拳不會命中、不會被玩家碰撞擋住；「無效攻擊」只看出拳開始那一帧的距離與觸及線。
- **未量**：A→B→A 換位次數（M5 只量了模式切換與 strafe 翻面）、`CalculatePath` 次數與 GC bytes（M6 只量了路徑請求數）、帶方向的受擊。
- **可重現性**：兩輪的**主要指標逐值相同**（attackValid、帶內／被擋時間、首次有效、攻擊與無效攻擊、noProgress（Unreachable 804／1195）、recover、strafeBlocked、路徑請求、Hurt／Action 最長段）；Corner、Unreachable、AttackAndHurt 三個情境整行相同。**次要指標有浮動**：移動距離 ≤ 4%、平均距離 ≤ 0.06 m、模式切換（OpenStatic 9／5、MovingSlow 44／40）、strafe 翻面（MovingSlow 9／7）、allowedMove 帧（OpenStatic 934／898、ThinWall 957／969）、stuck 帧（差 8–26 帧）。⇒ 固定種子＋固定 Δt 下**不是逐位元可重現**；A/B 比較時主要指標可直接比，次要指標要多輪取範圍，差距小於上表的浮動不得當成改善。

### 11.6 牆角情境補強：`CornerPinned`（2026-10-14）

**為什麼要補**：原 `Corner` 情境（§11.3）在基線上 strafeBlocked、recover、noProgress 全為 0——目標在 7 m 外，敵人會先走出牆角，牆角根本沒有困住它。用它驗收 T1／T2 什麼都證明不了。`Corner` **保留不動**（基線數字可比），另加 `CornerPinned`。

**情境**：同一組兩道牆（內面 x＝8、z＝8，NavMesh 邊界 7.5）。敵人起點 (7.3, 0, 7.3)——牆角內。目標沿對角線從外側壓近：0–3 s 離起點 1.6 m（帶內 ⇒ Strafe，兩個切線方向都撞牆＝**T1**）；3–4 s 壓到 0.5 m（敵人退到背貼兩牆後仍只有約 1.0 m < 1.25 ⇒ 持續 Retreat 朝牆角推＝**T2**）；4–12 s 維持；12–13 s 退回 1.6 m；之後維持。
第一版只壓到 1.0 m：敵人退約 0.35 m 就回到帶內，**T2 沒有重現**（Retreat 72 帧、無進展 0）——依此改為 0.5 m。

新增指標：`strafeFrames`、`retreatFrames`、`retreatNoProgressFrames`（無進展且上一帧模式是 Retreat，用來把無進展歸因到 T2）。`metrics.tsv` 欄位改變時，舊檔改名為 `metrics.tsv.old-N` 保留。

**基線（現行程式，三輪，`Logs/safe-test/20261014T…-p5-cornerpinned2-r1..r3`，全部 PASS）**：

| 指標 | 第 1／2／3 輪 | 判讀 |
|---|---|---|
| retreatNoProgress（帧） | 361 / 372 / 359 | **T2 穩定重現**：約 6 s 朝牆角推、沒有進展 |
| noProgress / allowedMove | 365/728 · 375/592 · 359/547 | 被允許移動的帧有 50–67% 沒有進展 |
| strafeBlocked（帧） | 175 / 419 / 463 | **T1 重現，但浮動大**——要用分布比較 |
| retreat / strafe（帧） | 730/241 · 628/467 · 613/501 | 浮動 |
| attackValid | 49.4% / 51.8% / 50.6% | 被壓在 1.0 m 時不在帶內 |
| stuck／其中出拳或受擊中 | 511/156 · 491/132 · 474/115 | 出拳中的 `IsStuck` 誤報仍在（§11.3） |
| 攻擊／無效攻擊、recover、路徑請求 | 6/0、0、7（三輪相同） | — |

⇒ `CornerPinned` 是 **P5b**（T1／T2：站位與 Retreat）的驗收情境，**不是 P5a 的**——P5a 不改 Retreat。P5a 對它的預期只有「出拳／受擊中的 stuck 誤報歸零，推牆時的 stuck 仍被偵測到」（ADR-012）。
