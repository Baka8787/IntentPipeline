# 專案開發更新日誌 (Changelog & Learning Record)

> **分卷制（2026-07-25 起）**：本檔只保留**最近 4 個版本**；更早的歷史全數在 **[`docs/changelog-archive.md`](changelog-archive.md)**（一字未改，版本／章節編號原樣保留）。
> **為什麼分卷**：changelog 是 append-only 歷史，日常開發不需要（也不該）整檔讀入——單檔膨脹到 800+ 行後，任何一次查閱都是全檔成本。分卷後「查最近進度」與「考古」是兩個不同成本的動作。
> **新增版本一律寫在本檔頂端**；本檔超過 4~5 個版本時，把最舊的搬進歸檔卷並更新卷末索引表。

---

## [v0.24] - 熱路徑每帧 40 B：介面型 foreach 的裝箱（2026-07-26）

> 起因是去補「零 GC」那項一直沒做的 Profiler 驗收。README 稽核時已經把這句話從「已達成」降級為「設計目標」，這次是真的去量——**結果量出一個真的 bug**。

### 1. 從「看錯欄位」到「找到真凶」的三步

第一次量看的是 CPU 圖表的 `GarbageCollector` 毫秒數，每帧 0.00ms，看起來很漂亮。但那一列量的是「**GC 回收花了多少時間**」，不是「配置了多少 bytes」——配置會先累積在 managed heap、等閾值才觸發回收。**零配置 ⇒ GC 時間 0ms，反過來不成立。**

改看 CPU → Hierarchy 的 `GC Alloc` 欄，穩態下 `PlayerLoop` 是 **40 B/frame**。同一幀 `EditorLoop` 佔 89.2%／28.25 ms、`PlayerLoop` 只有 2.61 ms——順帶量化了「Editor 的數字不能直接當結論」。

展開 `UpdateScene → Update.ScriptRunBehaviourUpdate → BehaviourUpdate → CharacterPipelineRunner.Update → GC.Alloc 40 B`：**是我們的程式，不是 Editor 記帳。**

### 2. 真凶：對介面 `foreach`，struct enumerator 被裝箱

```csharp
// FullBodyStateMachine.EvaluateTransitions
var allowedTargets = _config.GetValidTransitions(_currentState.Type);  // 靜態型別＝IReadOnlyList<StateType>
foreach (var targetType in allowedTargets)                             // ← 每帧 40 B
```

`GetValidTransitions` 回傳的是**介面**。foreach 對象的靜態型別是介面時，編譯器不能用 `List<T>` 那個 **struct** enumerator，只能呼叫 `IEnumerable<T>.GetEnumerator()`——**struct enumerator 因此被裝箱到堆上**。裝箱後大小：標頭 16 ＋ List 參照 8 ＋ index 4 ＋ version 4 ＋ current(enum) 4 → 對齊 **40 B**，與實測完全吻合。

**修法是換迭代方式，不是換型別**：改成索引迴圈（`for (int i = 0; i < allowedTargets.Count; i++)`），根本不建立 enumerator。刻意**不**把回傳型別改成具體 `List<StateType>`——那會讓呼叫端拿到可變集合，**為了效能犧牲唯讀封裝並不划算**。

複驗：穩態 `PlayerLoop` = **0 B**。

### 3. 順手掃了全 Runtime 的 foreach，其餘 5 處都安全

`EvaluateInterrupts` 迭代**具體** `Dictionary`（struct enumerator，且是熱路徑）、`StateMachineConfigSO` 三處迭代**具體** `List` 且只在 `Initialize()` 跑一次、`AnimancerFacade` 兩處在 `Awake()`、`PresentationPipeline` 本來就是索引迴圈。**全專案唯一一個「靜態型別為介面」的熱路徑迭代，就是踩到的那個。**

諷刺的是，`EvaluateInterrupts` 的註解明文寫著「Dictionary.Enumerator 結構體迭代，零 GC Alloc」——**同一個檔案、同一個作者，一個做對了、一個沒有**，差別只在回傳型別是具體類別還是介面。

### 4. 順帶量清楚了狀態切換那一幀

切換狀態時 `PlayerLoop` 會跳到約 2.6 KB，拆解後：

```
LogStringToConsole   2.4 KB
  └ StackTraceUtility 2.4 KB   ← Unity 為 Debug.Log 擷取 stack trace
GC.Alloc              180 B    ← 富文本訊息字串本身
```

來源是各 State `OnEnter` 的 `#if UNITY_EDITOR Debug.Log`（ADR-002 §3 既有取捨，Release 整段移除）。**大頭甚至不是我們的字串，是 Unity 的 stack trace 擷取。** 這不是回歸，是已知且刻意的 Editor-only 成本——但現在有數字，下次看到 2.6 KB 不必重新推理。

### 5. ✅ 達標複驗：Development Build

同日以 **Development Build ＋ Autoconnect Profiler** 複驗，`PlayerLoop` 的 `GC Alloc` 在穩態移動下為 **0 B**。Player 與 Editor 的對照本身也很說明問題：

| | Editor（Play Mode） | Development Build |
| --- | --- | --- |
| `PlayerLoop` GC Alloc | 0 B | **0 B** |
| `EditorLoop` | 佔 89.2%／28.25 ms | **不存在** |
| CPU／Total Used Memory | 31.65 ms／3.38 GB | **7.19 ms／499.6 MB** |

零 GC 自此在 README 從「設計目標」升為「**已驗證（Player 實測）**」——**這是整份 README 唯一一條有量測數據撐著的宣稱**。措辭限定在「穩態移動」：狀態切換幀的 Editor-only `Debug.Log` 在 Release build 已被編譯移除，不在量測範圍也不影響結論。

存證截圖進版控（`docs/images/profiler/`），**build 產物本身不進**（`.gitignore` 新增 `/[Bb]uilds/` 排除，該目錄實測 173 MB）——**進版控的是證據，不是產物**。順帶發現原本的 `# Builds` 區段只擋 `*.apk`／`*.unitypackage` 等副檔名，擋不到建置**資料夾**。

### 6. 文件

* **新增 dev-spec §7.4「零 GC 量測 SOP」**：量哪裡（`GC Alloc` 欄，**不是** `GarbageCollector` 毫秒）／排除什麼（Editor 開銷、自訂 Inspector 的字串配置、狀態 Debug.Log、Deep Profile、Profiler 自身的 frame buffer——後者實測緩衝 14,877 幀時 Reserved 2.88 GB 並造成週期性卡頓）／兩級判定標準／當前實測狀態。
* **§7.1-A3 補上能力邊界**：token 掃描抓不到介面型 foreach 裝箱，**熱路徑迭代介面型集合一律用索引迴圈**。

### 7. 反思（Why）

* **靜態測試守不到的東西，要誠實標出它守不到。** A3「禁 LINQ」一直被當成零 GC 的自動防線，但它是 token 掃描——這次的配置點沒有任何可疑 token。與其讓人誤以為有防線，不如把邊界寫進 A3 自己的敘述裡。
* **「宣稱」逼出了「量測」，量測逼出了 bug。** 這個 40 B 從 B9 平滑那輪就存在，一直沒人發現，因為沒有人真的量過。README 稽核把「零 GC」降級為設計目標，才觸發了這次量測——**對外誠實的副作用是對內發現問題。**
* **看錯欄位比看不到更危險。** 第一次量到「GarbageCollector 每帧 0.00ms」時，如果就此收工，我們會帶著一個錯誤的結論繼續往前走，而且會**更有信心**。這也是為什麼 SOP 的第一節寫的是「量哪裡」而不是「怎麼修」。

---

## [v0.23] - 切斷 Runtime → AnimationClip 的最後一條依賴（2026-07-26）

> 起因是一次「README ↔ 實際專案」的一致性稽核：README 要宣稱「Kubold 只是 sample content、不是 framework dependency」，就必須先**沿實際程式追一遍**——移除動畫資產後，哪些功能仍運作、哪些只是 sample data 失效。追完的結論大致成立，**但抓到一條真的耦合**。

### 1. 追蹤結果（實測，非文件推測）

`Assets/Scripts/Core` 全層搜不到 `AnimationClip`／`ClipTransition`／`AnimancerState`——核心層不認識 clip 型別。移除 Kubold 後：移動速度、gait 三檔、Ctrl toggle、B9 平滑、Idle↔Move 轉換、**Jump 完整物理**、**Roll 計時與曲線位移**、Foot IK、落地音效**全部照常**；失效的只有 locomotion 的動畫視覺（`Play()` 印警告後略過，不拋例外）。

原因是烘焙資料自足：`MotionBakeData` 的特徵欄位（速度曲線、腳相曲線、代表速度、起跳前搖、頂點高度、反推重力…）都是**序列化值**，clip 消失後仍在。Jump 與 Roll 的烘焙來源本來就指向版控內的 Mixamo clip，不是 Kubold。

### 2. 但有一個例外——而它正好是最危險的那種

```
MotionBakeData.cs   public float Duration => SourceClip != null ? SourceClip.length : 0f;
RollState.cs        _rollTimer = _rollBakeData != null ? _rollBakeData.Duration : FallbackDuration;
```

**全專案唯一一條「執行期 gameplay 邏輯讀 `AnimationClip`」。** 而且退化條件檢查的是**引用是否為 null**，不是**值是否可用**——clip 一旦缺席，`_rollBakeData` 仍非 null → `Duration` 靜默為 0 → `_rollTimer` 為 0 → **翻滾第一帧就結束**，`FallbackDuration` 永遠用不到。

這是「Roll 秒退」的**同型變體**：上一次根因在 asset 層（`bakeMappings` 未綁，v0.16.x 已修），這一次在 clip 層，**舊的防線守不到**。當時不會觸發（Roll 用版控內的 Mixamo clip、GUID 穩定），所以它是潛伏缺陷——**在做完 Footstep／Stop／IK 之前抓到，比之後便宜得多**。

### 3. 修法：烘焙期快照，而不是執行期回退

新增序列化欄位 `BakedDuration`，烘焙時自 `sourceClip.length` 寫入；`Duration` 改為 `=> BakedDuration`。與 `AutoAverageSpeed` 同一個 pattern。`SourceClip` 降為 **Editor-side provenance**，執行期程式碼讀取數歸零。

**刻意否決的替代做法**：`Duration => BakedDuration > 0 ? BakedDuration : SourceClip.length`。它能讓舊資產無縫過渡，但**保留了 runtime clip 依賴，本輪目標即刻作廢**——而且更糟：遷移缺口會永遠隱形，沒有人會知道該重烘。

代價是舊資產在重烘前 `Duration` 為 0。這個代價**由消費端接住**：`RollState` 的退化條件從「引用是否為 null」改為「**值是否 > 0**」，並補一則 Editor 警告指出該重烘。順帶把原本那個守不到的洞永久補上——**現在無論 clip 缺席、資產未重烘、還是 bakeMappings 沒綁，Roll 都退化成 0.5s 固定計時，而不是秒退。**

### 4. 測試

`[Test]` 73 → **76**：
* `Duration_ReadsBakedDuration_WithoutAnySourceClip` — EditMode 建的 `MotionBakeData` 天然沒有 clip，正好是這條契約的自然測試環境。
* `Duration_StaleAsset_ReturnsZero_SoConsumersCanDetectIt` — 明確斷言「舊資產如實回 0」，把「不准偷偷回退讀 clip」寫成測試。
* `Roll_WhenBakeDataHasNoDuration_FallsBackInsteadOfEndingInstantly` — 行為層的迴歸鎖：注入一顆 `BakedDuration = 0` 的資產，翻滾推進 0.1s 後**必須仍在 Roll**。

前兩條守資料契約，第三條守行為——**缺了第三條，未來有人把退化條件改回 `!= null` 也不會有任何測試變紅**。

### 5. 反思（Why）

* **「fallback 存在」不等於「fallback 會被觸發」。** 這個 bug 的本體不是缺少防線，而是防線**檢查錯了東西**——檢查引用而非值。凡是「資產存在但內容不可用」的半斷鏈，用 null 檢查一律守不到。
* **快照優於即時查詢，正是因為它會壞得很大聲。** 回退讀 clip 看起來更「穩健」，實際上是把遷移債務藏起來。讓舊資產回傳 0、讓消費端警告，是**刻意選擇讓問題可見**。
* **稽核 README 稽核出了一個程式缺陷。** 對外宣稱之所以有價值，是因為它逼你證明——而證明的過程會踩到你平常不會走的路徑（「如果動畫資產不見了會怎樣」）。

---

## [v0.22] - Walk 型態：hold／toggle 成為資產可配置項（2026-07-25）

> 落地第一套完整的玩家控制方案（參考終末地）：**WASD 預設 Run／Ctrl 切換 Walk 型態／Shift 閃避／Space 跳躍**，sprint 規劃為 buff 驅動而非按鍵。表面上是「綁一顆鍵」，實際逼出了一個設計問題：**「按住生效」與「按一下切換」的差別該住在哪一層。**

### 1. 裁決：操作語意也是 per-game 配置，所以住在資產

`walkIsToggle` 做成 `GaitProfileSO` 的欄位，而不是寫死在 `PlayerLocomotionPolicy`。理由：若 policy 內建「終末地是 toggle」，那「換玩法＝換一顆資產」的承諾就只對**數值**成立、對**操作語意**不成立——而 Souls-like 的按住走路與開放世界 ARPG 的切換走路，差的正是後者。

配套：`InputData` 同時提供 `WalkButtonHeld`（持續）與 🆕 `WalkButtonDown`（邊沿），**兩者並存而非取代**。raw input 層不預設控制方案，只如實回答「按住沒有」與「這帧剛按下沒有」，由資產決定採用哪一種。這與 ADR-003 §6.3 否決 `[Flags] MovementModifier` 是同一條紀律：**領域決策不下放到 input 層，也不上鎖進程式碼。**

### 2. toggle 的狀態放黑板，不放 producer

新增 `MovementIntentData.WalkModeActive`，語意刻意定為「**型態開著沒有**」而非「鍵按住沒有」——hold 方案下兩者等價，toggle 方案下它是被閂住的持久值。推進方式是 **讀黑板 → 邊沿翻轉 → 寫回黑板**，`PlayerLocomotionPolicy` 因此**沒有增加任何私有欄位**。

這不是潔癖：ADR-003 D5 早就寫了「mode/toggle state 進黑板」，§9-L5 的理由是 netcode 的 rewind／replay 前提＝所有狀態可 snapshot。藏在元件私有欄位裡的 mode，會讓回捲後的角色型態與紀錄對不上。**這條現在有測試守**：同一顆 producer 換一塊新黑板，型態必須從乾淨狀態開始——若 producer 偷藏了狀態，這條會紅。

順帶釐清了三種生命週期的分界（寫進 §1.5）：trigger 邊沿「當帧生當帧死」由順序 7 清；連續型 intent「每帧整體覆寫」故不需清；**mode state 是持久的，被每帧清零就永遠關不起來**。三者都不參與復位，但理由各不相同。

### 3. 同一輪順帶查清的速度疑問（不是 bug）

使用者反映「套上 gait 後 Run 確實出現了，但速度偏慢，又看不出滑步」。查證結論：**烘焙沒問題，是 Kubold 的 Run clip 本身就是 3.5 m/s 的慢跑**（現實參照：慢跑 3、跑 5、衝刺 8+）。

- **「看不出滑步」正是校準正確的證據**，不是「不確定」：烘焙速度若錯，動畫與位移必然分岔＝滑步。兩件事是同一枚硬幣。
- 量到一個**真實但微小**的方法學偏差：`ComputeAverageSpeed` 把第 0 帧那支人造的 0 值算進算術平均，造成 **1.6%~2.6% 低估**（Run 真值 3.578 記為 3.502）。不足以造成「感覺慢」，但確實是缺陷 → 列入待修（修正需重烘全部資產）。
- **推導出一條原本沒寫下來的性質**：因為 `threshold_i = speed_i / speed_max`，混合區間內任意 intensity `p` 的混合動畫速度恰為 `p × speed_max`，與 `MotionDriver` 的位移速度**恆等**——所以**調 `defaultIntensity` 不會引入滑步**，全域成立而非只在 tier 點上。這讓「想跑更快」有了架構安全的第一階解法（調資料），不必動 clip。

### 4. 測試

`[Test]` 69 → **73**。新增 4 條涵蓋：hold 語意鏡射按鍵、toggle 邊沿翻轉並閂住、toggle 只看邊沿不看 Held（長按不得以 frame rate 抖動）、**toggle 狀態不得殘留在 producer**。既有 4 條 `ResolveIntensity` 測試因參數改名 `walkHeld`→`walkActive` 一併更新——具名引數編不過這件事，剛好證明了改名是有意義的語意變更而非美化。

### 5. 誠實記錄的未來衝突

Sprint 規劃由 **buff** 驅動（非按鍵）。buff 是 gameplay state，producer 直接查詢它會違反 ADR-003 D2 的 context-free，`§7-A4` 的層級掃描會直接擋下。可行方向是「buff 寫進黑板的 status region，producer 讀資料而非查詢系統」，但那條界線（描述性 vs gameplay authority，§13.2）需要真需求才裁決。已寫進 dev-spec §7.3 張力表，**現在不做**。

---

## [v0.21] - ADR-003 Migration Stage 2：locomotion dynamics 歸位（2026-07-25）

> **本輪的完成判準只有一句話：Runner 不再認識任何 locomotion 概念。** Stage 1 把「輸入 → 意圖」搬進了 producer，但「意圖 → 實際怎麼動」（B9 平滑、`MoveSpeed` 導出、動畫參數驅動）仍留在通用管線裡——那正是 ADR-003 §9-L1 自列的殘餘耦合。本輪把它整組遷入 **Movement Model**，並用兩條新的回歸測試把門關上。**零新增 gameplay 功能，行為等價優先。**

### 1. 落地內容

| 項目 | 從 | 到 |
| --- | --- | --- |
| B9 平滑（`LocomotionSpeedSmoother`） | `CharacterPipelineRunner` 持有 | `LocomotionModel` 持有（**計算邏輯一字未改**，只換持有者——Stage 1 把它抽成 struct 就是為了這一刻） |
| Movement Output 導出（`MoveSpeed`／`MoveDirection`／`UpperBodyWeight`） | `Runner.DeriveMovementParameters()`（順序 3） | `LocomotionModel.Tick()`（順序 3，Runner 只呼介面） |
| `SetFloat(MoveSpeed)` 動畫參數 | `Runner.SyncAnimation()`（順序 5） | `LocomotionModel.Tick()` 內自驅（D4：每個 model 驅動自己的參數） |
| Idle／Move 的 `CanEnter` 門檻 | 讀黑板 `MoveSpeed < 0.1` | 問 `MovementModel.IsProducingMotion`（0.1 回歸 model 內部） |
| Idle／Move 的 `OnUpdateMotion` | `BaseState` 預設 `ExecuteBaseMovement` | delegate 給 `MovementModel.UpdateMotion`（D3） |

新增 `Assets/Scripts/Core/Movement/Models/`：`IMovementModel`（通用抽象）＋`LocomotionModel`（MonoBehaviour，掛角色 Root）。

### 2. 三個裁決點（設計討論先於實作）

* **model 的形式與擁有者** → MonoBehaviour ＋ **狀態機單一持有、注入所有 state**。理由是本輪最大陷阱：平滑狀態是值型別，**每個持有者都會有自己一份**；若 Idle／Move 各持一份，切換時平滑值被重置、放開輸入的收步就會斷。選定的注入鏈（Runner 解析 → 注入 FSM → FSM 發給所有 state）讓唯一性是**結構保證**而非紀律，並補 **A10** 測試守住。
* **FSM 門檻信號** → `IsProducingMotion`。不改讀原始 intent，因為那會讓放開輸入的瞬間就切 Idle、但角色仍在滑行（動畫與位移分岔）——這正是 Stage 1 當初記錄該張力時寫下的理由，至今成立。
* **Movement Output 的去留** → **保留黑板欄位，語意重定義**為「當下 active Movement Model 發布的運動輸出」。D4 字面要求「不再是黑板欄位」，但完全內化需連動 `MotionDriver` API 與 `JumpState` 空中控制（intrinsic 狀態也消費這組值），會模糊 ambient／intrinsic 的界線。**刻意留為 migration intermediate state**，待第二個 model 進場時一併處理（dev-spec §7.3 誠實記錄）。

### 3. 實作時撞到的兩個坑（都不在 ADR 的預想裡）

* **坑一：`OnUpdateMotion` 單一進入點會壞掉。** ADR 說「model 走 `OnUpdateMotion` 路徑」，最直觀的讀法是把 dynamics 全放進去。但 `JumpState.OnUpdateMotion` 的空中控制吃的正是這組運動輸出——若 dynamics 只在 ambient 狀態推進，Jump／Roll 期間平滑會**凍結**，落地時拿起跳的殘值續走＝滑步。而且原本 `DeriveMovementParameters` 是**每帧無條件**跑的，不看當前狀態。→ model 改為**兩個進入點**：`Tick`（順序 3，Update，每帧無條件）＋`UpdateMotion`（順序 6，LateUpdate，ambient delegate）。
* **坑二：動畫參數若隨之落到 LateUpdate 會晚一帧。** Animator 的評估卡在 Update 與 LateUpdate 之間，`SetFloat` 移到 LateUpdate 等於參數比位移慢一帧。→ `SetFloat` 留在 `Tick`（Update）內。
* 兩者合起來的結論：**順序 3 沒有消失，只是換人執行**（dev-spec §2.1 原本預測「Stage 2 後本順序消失」，本輪推翻並補上理由）。已寫成 §2.1 脆弱點警告第 6 條，避免未來有人為了「乾淨」再合併一次。

### 4. 順帶修好的規則粒度問題

原 `LayerRules` 禁止 `Core/Movement` 認識 `Project.Presentation`（守 D2 的 producer context-free），但 D4 要求 **model 必須驅動 Facade**——同一條規則套在兩種角色上，會逼出「model 不能自驅動畫參數」的錯誤結論。→ 規則拆成兩條：`Core/Movement` **不遞迴**（producer 紀律不變）＋`Core/Movement/Models` 另立（禁 StateMachine／Pipeline，**允許** Presentation）。**零檔案搬移**（不動既有 `.meta`），規則本身變成更精確的規格。

### 5. 測試

* 新增 **A9**（`CharacterPipelineRunner` 原始碼不得出現 `MoveSpeed`／`SmoothDamp`／`GaitProfile` 等 token＝**完成判準本身變成可執行斷言**）與 **A10**（`LocomotionSpeedSmoother` 的執行期持有者恰好一個）。
* A9 掃描時**額外剝除字串常值**：Tooltip／LogError 合法地指名預設元件叫 `LocomotionModel`，那是設定指引不是型別依賴。只放寬這一項測試。
* `StateMachineTests` 改用 `FakeMovementModel` 驅動門檻（原本直接設 `data.MoveSpeed`）——**所有權轉移讓測試變紅是設計上刻意的摩擦**，紅的位置正好指出「誰現在說了算」。
* `[Test]` 總數 67 → **69**。

### 6. 反思（Why）

* **ADR 固定契約，不固定時序。** ADR-003 D3／D4 說對了「誰該擁有什麼」，但「什麼時候跑」得靠實作期的時序驗證補上。本輪兩個坑都不是契約錯誤，是**契約在具體幀序下的展開方式**——這類知識該回寫進 dev-spec 的脆弱點警告，而不是改 ADR。
* **值型別的跨帧狀態，所有權即正確性。** 平滑器是 struct 這件事讓「誰持有它」從風格問題升級為行為問題。這也是為什麼本輪選擇用注入鏈＋測試來保證唯一性，而不是靠註解提醒。
* **遷移要留下可執行的門，不是註解。** §9-L1 能在 Stage 1 悄悄存在整整一輪，正因為當時只有一句「這是已知殘餘耦合」的註解。這次收尾的同時把 A9 寫下去——**下一個想把速度塞回 Runner 的人會先看到紅燈**。

---

## [v0.20.1] - 文件結構優化：Context 讀取放大率治理（2026-07-25）

> **不是功能版本，是可維護性版本。** 起因：v0.20 完成後盤點發現，一個「單一功能、單一分層」的任務讀掉了全專案 **23%** 的內容。量測後確認問題不是專案大（9.7k 行其實很小），而是**讀取放大率**——沒有索引時，取得一張 9 列的黑板權限表要付 603 行的代價（~40×）。本輪治理的是這個倍率，不動任何程式碼。

### 1. 量測（先量再開藥）

| 需要的東西 | 實際付出 | 放大率 |
| --- | --- | --- |
| dev-spec 的 §1.1／§2.1／§3.1 三節 | grep 標題＋分塊讀 ~360 行（檔案 1,169 行） | ~5× |
| 「誰寫黑板哪個欄位」一張 9 列表 | Runner 281＋MotionDriver 237＋Blackboard 85＝603 行 | ~40× |
| ADR-003 契約本身 | 265 行（**無浪費，這是必要成本**） | 1× |

三個結構性成因：①`docs/02-dev-spec.md` 1,169 行且**結構性地會繼續長**（每個子系統都往 §3 加）；②`changelog.md` 819 行是 append-only 歷史，卻與「當前狀態」混在同一個閱讀入口；③註解密度高——**這是學習導向的刻意選擇，明文不動**。

### 2. 四項措施

* **changelog 分卷**：主檔只留最近 4 版（v0.20／v0.18.7／v0.18.6／v0.18.5），其餘 → `docs/changelog-archive.md`（**一字未改**，版本編號原樣保留），卷末附版本區間索引表，讓人不必開檔就能判斷該不該讀。819 → 169 行。
* **新增 `docs/00-map.md`（45 行）**：模組 → 檔案 → 治理章節的單頁索引，外加「常見問題的最短路徑」表（例：查黑板寫入權限 → 讀測試的 `WriterRules` 15 行，而非三個實作檔）。刻意**只記指標不記細節**——細節會漂移，指標不會。
* **dev-spec 分卷**：§3.5 Foot IK → `docs/05-foot-ik.md`；§3.2 的動畫呈現三小節（`AnimancerFacade`／`Locomotion 1D Mixer`／`動畫數據→配置資料流`）→ `docs/06-animation-presentation.md`。1,169 → 1,018 行。
* **CLAUDE.md 新增 `Context Discipline` 章**：閱讀協定（開場只讀 WORKLOG 交辦段＋對應 ADR；大檔先 grep 標題再 offset/limit；不重讀已摘要過的檔）、**Test-as-Spec 原則**、**Explore subagent 授權**。

### 3. 零斷鏈是怎麼做到的（分卷的關鍵手法）

**逐字搬移＋章節編號原樣保留＋原位留 stub**。`docs/05-foot-ik.md` 內仍是 `3.5.1`~`3.5.4`，因此既有的「dev-spec §3.5.2 L1~L6」在新檔以**同一編號**直接定位，**零引用改寫**。原位置留下的 stub 含一句話現況＋連結＋「為什麼是這節被搬走」。全 docs 相對連結掃描：3 條全部有效。

### 4. 推翻 2026-07-21「不回頭拆文件」規則（本輪唯一的規則變更）

原規則的理由是「交叉引用斷裂風險 > 收益」。**推翻依據不是改變主意，而是新輸入**：①收益側變了（context 耗盡當時不存在）；②風險側被手法消解了（編號保留法證明斷鏈可避免）。修訂後的資格條件寫死三條：**已凍結/穩定**（不拆正在重設計的東西，否則搬兩次）、**非跨領域契約**（§0/§1/§2/§3.1/§3.3/§7 永遠留 dev-spec）、**逐字搬移＋保編號＋留 stub**。並明文：**這不是整理癖的許可證**——拆檔是對已量測成本的回應。

### 5. 反思（Why）

* **v0.20 的意外收穫變成了本輪的支柱**：`ArchitectureRegressionTests` 原本是為了防架構漂移而寫，結果它同時是「最便宜且不可能過期的架構摘要」——因為一旦過期測試就會紅。**把不變量寫成測試，同一份成本買到執行力與可讀性兩件事**，這條已升格為 CLAUDE.md 的 Test-as-Spec 原則。
* **先量測再開藥**：直覺答案是「專案太大了要拆」，但量測顯示 9.7k 行根本不大，真正的病是放大率。若照直覺動手，會去拆程式碼（高風險、零收益），而不是建索引（零風險、高收益）。
* **規則不是不能改，是不能無理由地改**：ADR 是不可變的**決策快照**，CLAUDE.md 的工作準則則應隨新證據修訂——關鍵在於把「當初的理由 vs 現在的新輸入」寫清楚，讓未來的人看得出這是推導不是反覆。

---

## [v0.19] - Locomotion Foundation 收案 ＋ 里程碑檢查點（2026-07-26 補記）

> **關於版本號**：`v0.19` 自 v0.20 起就被保留給 Foundation 收案——當時程式已完成、卡在使用者側的資產作業（4-tier mixer、重烘），因此後續程式輪次先取用了 v0.20。資產於 2026-07-25～26 陸續到位並驗證完畢，本條目為**補記**，故位置依版本號排在 v0.20.1 之後，而非依「新版本寫頂端」的常規。
>
> 這同時是一個**檢查點**：Foundation 收案讓前面幾輪的架構工作第一次全部接上，因此本條目除了記錄 v0.19 本身，也標記「到此為止累積達成了什麼」。

### 1. Foundation 本體（資產側，磁碟驗證）

| 項目 | 收案狀態 |
| --- | --- |
| `Locomotion.asset` 擴為 **4-tier** | ✅ 4 支 clip、thresholds `0 / 0.265 / 0.574 / 1`；Sprint 段取自 `MovementAnimsetPro_SprintFixed.fbx` |
| `MotionDriver.moveSpeedSource` | ✅ → `Bake_SprintFwdLoop`（`AutoAverageSpeed` 6.100843）。**mixer 頂 tier 與位移滿速自此同源**，ADR-003 §9-L4 的校準風險解除 |
| 4 支 loop 重烘補 `FootPhaseCurve` | ✅ Idle 401／Walk 61／Run 47／Sprint 39 個關鍵影格 |

**速度真相（全部從 Bake Data 讀出，非手填）**：Walk 1.617／Run 3.502／Sprint 6.101 m/s。門檻依 `threshold_i = speed_i / speed_max` 換算——這條公式是**不可協商的校準**，它保證動畫速度與位移速度在整條混合曲線上恆等。

### 2. Gait 落地：Run 成為預設型態

`Gait_ActionRPG.asset`（`Assets/ScriptableObjects/Movement/`）建立並綁入 `PlayerLocomotionPolicy`：

* `defaultIntensity` **0.75**（預設 Run）／`sprintIntensity` **1.0**／`walkIntensity` **0.3651**／`walkIsToggle` ✅
* 控制方案（參考終末地）：**WASD 移動（預設 Run）／Ctrl 切換 Walk 型態／Shift 閃避／Space 跳躍**；`SprintAction` 刻意不綁鍵——sprint 規劃由 buff 驅動。

⚠️ 數值**刻意偏離**了公式換算的基準值（Run 0.574、Walk 0.265）。這是合法的：公式綁的是 **threshold**，不是 gait intensity——對任意 intensity `p`，混合後動畫速度恆為 `p × speed_max`，與位移恆等。偏離只代表**刻意選了一個混合姿態**（Run 偏向衝刺、Walk 偏向快走），不是校準錯誤、不會滑步。這條釐清在 v0.22 寫進 dev-spec §3.1／§7-M4，因為原文把 threshold 與 intensity 混在同一句話裡。

### 3. 檢查點：這條線現在是完整的

Foundation 接上後，**「輸入 → 意圖 → 模型 → 狀態 → 位移 → 動畫」第一次全程走通且每一段都有明確擁有者**：

```
InputAction ─► InputData(ref struct) ─► PlayerLocomotionPolicy(+GaitProfileSO)
   ─► MovementIntent{強度[0-1], 方向, WalkModeActive}   ← 模型無關契約
   ─► LocomotionModel(B9 平滑 → Movement Output，並自驅 SetFloat)
   ─► FSM(問 IsProducingMotion) ─► MotionDriver(讀 Movement Output) ─► CharacterController
```

沿途沒有任何一段回頭查詢別的 gameplay 系統，也沒有任何一段被寫死在通用管線裡。

### 4. 累積達成的兩個性質（跨 v0.19～v0.23）

**① Animation-independent gameplay core.** `Assets/Scripts/Core` 全層搜不到 `AnimationClip`／`ClipTransition`／`AnimancerState`。移除 locomotion 動畫來源後，移動速度、gait 三檔、型態切換、狀態轉換、Jump 物理、Roll 計時與曲線位移、Foot IK、落地音效**全部照常**——失效的只有 locomotion 的動畫視覺。**動畫是這個框架的表現層，不是它的地基。**

**② Runtime baked data.** `MotionBakeData` 的特徵欄位全部是烘焙期寫入的序列化值（速度曲線、腳相曲線、代表速度、**動畫長度**、起跳前搖、頂點高度、反推重力）。v0.23 補上最後一塊 `BakedDuration` 後，執行期程式碼對 `SourceClip` 的讀取數為 **0**，`SourceClip` 降為 Editor-side provenance。**gameplay 讀的是數據，不是資產。**

這兩點合起來，才讓「這套東西可以抽成 Unity Plugin」從願景變成一個**可以被檢查的性質**——而不是一句自我宣稱。

### 5. 還沒達成的（誠實記錄，避免檢查點變成勝利宣言）

* **`SourceClip` 欄位仍在** → clip 仍會被打包進 build、仍隨資產載入。現在成立的是「執行期**邏輯**不依賴 clip」，不是「不載入 clip」。
* **執行期 0 GC 未經 Profiler 驗收**（dev-spec §7-M2）。目前有的是設計約束 ＋ A3 靜態切片。
* **`MovementContext` 未實作**：只有一個 movement model，正交軸尚未被第二個 model 壓測（ADR-003 §9-L2）。
* **7 顆 Bake 資產的 `BakedDuration` 為 0**（刻意延後，用到再烘）；`ComputeAverageSpeed` 的 0 值哨兵造成代表速度低估 1.6~2.6%，待下次全面重烘一併修。

### 6. 反思（Why）

* **保留版本號是對的。** v0.19 空著等資產、程式輪次照常往前推進到 v0.23——如果當初硬要「等資產到齊才發版」，四輪架構工作都會被一件 Inspector 作業卡住。**版本號可以亂序，架構進度不該被資產進度綁架。**
* **收案的價值不在資產，在於它逼出了驗證。** 4-tier 到位後才會發現「無 gait profile 時穩態只用得到最高 tier」；gait 填上去才會問「Run 為什麼這麼慢」；追那個問題才發現烘焙其實是對的、並順手推導出「任意 intensity 都不滑步」這條原本沒寫下來的性質。**每一次接上真實資產，都會問出光看程式碼問不出來的問題。**

---

## 更早的版本（v0.1 ～ v0.20）

完整歷史已分卷至 **[`docs/changelog-archive.md`](changelog-archive.md)**（內容一字未改，版本／章節編號原樣保留）。

歸檔卷內的版本索引（便於定位，不必開檔即可判斷該不該讀）：

| 版本區間 | 主題 |
| --- | --- |
| v0.20 | ADR-003 Movement Intent Migration Stage 1（最小 seam ＋ 架構回歸測試 A1~A8 首度落地） |
| v0.18.7 | Foot IK v1 凍結（樓梯歪斜根因＝斜坡 collider、A/B 收束、設計哲學轉向） |
| v0.18.5 ～ v0.18.6 | M3.5 Regression Recovery → 最終形字面回歸 M3.1（實驗代碼連同 flag 全數清除） |
| v0.18.1 ～ v0.18.4 | M3.1～M3.4 Foot IK：反饋迴路修正（雙管道）→ 極端案例收束 → 實測校正 → 方向性修正 |
| v0.18 | M3 Foot IK 首版（Presentation Pipeline 第二個 Controller） |
| v0.17 | M2 Presentation Pipeline ＋ Landing Audio |
| v0.16 ～ v0.16.3 | M1 Locomotion（Transition 資產／1D Mixer）、FBX 子 clip 直引治理、動畫數據→配置資料流、B11 Editor Tool 準則 |
| v0.15 ～ v0.15.1 | Jump 腳滑工具鏈、Capsule 對齊規範、Humanoid 匯入矩陣定調 |
| v0.10 ～ v0.14.2 | StateParamsSO 職責分離、ADR-001／ADR-002 定調、Jump 特徵分析演算法修復、結構與命名定調 |
| v0.1 ～ v0.9 | 地基：黑板／ref struct／資料驅動狀態機／根運動烘焙管線／Code Review 追平 |
