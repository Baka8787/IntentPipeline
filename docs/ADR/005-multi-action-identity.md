# ADR-005：Action Identity（多 Action 並存的單一身分概念）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | ✅ **Accepted**（2026-09-06 翻牌）——**決策內容自此凍結**。要改決策請開新 ADR 並 Supersede。語意見 ADR-004 §0 |
| 日期 | 2026-09-02（建立）／**2026-09-06（Accepted）** |
| 驗證方式 | **Code-first**（`CLAUDE.md` Fold-back：Trial 期允許短暫 code-first，那正是驗證的一部分）。文件已於結案前 fold back |
| Acceptance Review | ✅ **2026-09-06 完成，A–G 七條全數通過**（使用者實跑）。詳見 **§4** |
| Supersede | 無。與 ADR-001／002／003／004 並列，**延續**且不推翻 ADR-004 的 D1–D7（特別是 §5.2「每個動作一個 `StateType`」的否決繼續有效） |
| 關聯文件 | `docs/ADR/004-action-in-fsm.md`（前置，✅ Accepted）、**`docs/11-multi-action.md`（Living Spec；候選比較、需求分析、資產配置、輸入映射、實作細節全在該檔）** |
| 前置事實 | `docs/08` §11.1 登記的 FU-1／FU-2／FU-3；2026-09-02 作品集方向調整後三項同時成為阻擋項 |

> **本 ADR 刻意寫得很短。** 2026-09-02 檢討：初版凍結五條，其中三條分別是既有 authority 的複述、schema routing 事實、與實作分析——**那些不是決策，不該佔用 ADR 的凍結力**。只有兩條通過「改錯會造成架構污染」的門檻。其餘全數下放 `docs/11`。

---

## 1. 問題

`docs/08` §11.1 登記的三項發現有共同根因：**系統裡沒有「這是哪一個 Action」的概念**。

| 登記項 | 磁碟證據 |
|---|---|
| **FU-2** 一角色一份 Definition | `ActionState.Initialize` → `config.GetStateParams<ActionDefinitionSO>(Type)`；根因是 `StateMachineConfigSO` 四張表**全以 `StateType` 為鍵** |
| **FU-3** mailbox 無身分 | `ActionRequestTarget.RequestAction()` 無參數，內部只有一顆 `bool` |
| **FU-1** Action→Action 中斷不可能 | `FullBodyStateMachine.EvaluateInterrupts` 首行 `if (targetState.Type == _currentState.Type) continue;` |

概念不存在 ⇒ 查表只能用 `StateType` 當鍵、mailbox 只能是無名旗標、中斷只能比型別。

---

## 2. Decision（凍結內容，共兩條）

### D1 — 恰好一個 Action identity，所有消費者共用

系統中**只准存在一個**「這是哪一個 Action」的身分概念。輸入映射、per-action 冷卻、HUD 查詢、`ActionRequestTarget` 的 request 身分、Action→Action 中斷規則，**全部以它為鍵**。

⛔ **禁止**任一消費者自造第二把鍵。
**理由**：N 把鍵就是 N 份必須人工同步的真相。這是「Respect Ownership」在身分層面的推論。

**不凍結**：identity 的表示法、容器形狀、API、slot 數量、命名 —— 全部下放 `docs/11`，且**允許在 Trial 期間被程式推翻**（那正是本次 Trial 要驗證的事）。

### D2 — 目前不建立通用 Ability ／ StatusEffect framework

命中後施加於他人的效果（首個使用者：Ice 的 Slow）**不屬於 Action 系統**。Action 系統的邊界是「我做了什麼、什麼時候生效」；「被打到的人怎麼了」在邊界之外。

⛔ **禁止**建立 `Ability`／`StatusEffect`／`Buff`／`Modifier` 之類的通用機制。
**理由**：`CLAUDE.md`「第二個使用者出現前不得建立 production abstraction」。第二個效果出現時再談抽象，那時才有兩個資料點可以歸納。

---

## 3. 明確**不**由本 ADR 管的事（下放 Living Docs，不需要 ADR）

| 項目 | 為什麼不需要 ADR |
|---|---|
| 「`ActionState` 是唯一 gate authority」 | **ADR-004 D2 已經凍結**，複述不增加保護 |
| 「中斷以 identity 為鍵、`StateType` 不加成員」 | ADR-004 §5.2 的否決已涵蓋，且 A13′／A19 已機器化守住 |
| 「request 帶身分屬 schema 變更」 | 那是 **routing 事實**，不是決策。走既有 routing rule 即可 |
| identity 的候選比較、需求清單 | **實作分析**，屬 Living Spec（`docs/11` §3） |
| 資產配置、鍵位、冷卻儲存形式、HUD API | 實作細節 |

---

## 4. Acceptance Criteria（`Trial → Accepted`）

> ## ✅ **2026-09-06 — 七條全數通過，本 ADR 結案（`Trial → Accepted`）**
>
> 使用者依 `WORKLOG.md` 的驗收清單實跑：**Editor Play 驗 A／B／C**、**Development Build ＋ Profiler 驗 E**。
> 回報：**「A 功能上沒問題／B 沒問題／C 沒問題／E 沒問題」**。
> 加上先前已成立的 D／F／G ⇒ **A–G 全通過**。
>
> ### 📌 結案時記錄的一個表現層問題（**不阻擋本 ADR**）
>
> A6（Fireball 連段）在**功能上通過**，但表現上有一個不一致：
> **連段期間身體維持在起手鎖定的朝向，火球卻每一段重新解算瞄準點** ⇒
> 敵人走動時，第 2、3 段會出現「人朝著 A 方向、火球飛向 B 方向」。
>
> **根因不在本 ADR 的決策範圍**：`ActionState` 鎖定朝向（`OnEnter` 取一次快照）與
> `ThrowProjectileEmitter.ResolveThrowRotation` 重新解算發射方向（每次 `Release`）是
> **兩個擁有者、兩個時點、沒有單一權威**。
> 這與 `docs/13` §4 診斷出的「移動方向與朝向被綁死」是**同一類問題的兩面**——
> 都是「朝向／瞄準方向由誰決定」缺一條明確的 seam。
>
> ⇒ **已登記為 `docs/13` §4.4**，屬結構性決策，依 CLAUDE.md 判準應在後續 ADR 一併處理。
> **刻意不在本次結案時補 workaround**（§4 末尾的紅線）。
>
> ---
>
> **進度（2026-09-02 EditMode 全綠後）**：**D／F 成立**（使用者實跑確認）。**A／B／C／E 仍待驗**——皆需資產接線、Play 或 Profiler，EditMode 撐不到那裡。
> ⚠️ 2026-09-02 前情：D 曾短暫打勾又撤回——那次「EditMode 全綠」跑在**尚未拉取本分支**的專案上，驗的是舊程式。本次不同：使用者是在合併後的 checkout 上跑的，且**先後回報了編譯錯誤與四條紅**（CS0246／T16／T17／T19／ReleaseNormalizedTime），修完才綠——證據鏈與本批程式對得上。

- [x] **A. 同一角色持有並可獨立觸發至少兩份 `ActionDefinitionSO`**，各自獨立輸入與 cooldown，且**共用同一顆 `ActionState`** —— ✅ **2026-09-06 Play 通過**。實際落地的是**三份**（`MeleeSlash1`／`Fireball`／`IceSpell`），三顆按鍵各自觸發、各自播不同動畫、各自產生不同世界效果（hitbox ／ 投射物 ／ 地面 AoE）。**關鍵證據是「按 E 後立刻按 Q，Q 仍能出手」**——per-slot 冷卻不連坐在 Play 成立，而非只在 EditMode 成立。<br>⚠️ 表現層問題見上方結案註記（連段朝向與發射方向不一致），**功能判準不受影響**。<br>以下為 Trial 期紀錄 —— 🟡 **程式面已成立**：T18（`Assert.AreSame` 同一 `ActionState` 實例）／T19（per-slot 冷卻不連坐）／T20（Action→Action 中斷）／T21（舊資產相容）**已實跑全綠**。**仍缺資產接線 ＋ Play**——真正的兩份資產、兩顆按鍵、實際播放都還沒發生，故本條不打勾
- [x] **B. 加下一個 Action ＝ 零 runtime 程式**（一份資產 ＋ 一列動畫映射 ＋ 一列 slot 映射）—— ✅ **2026-09-06 通過**。實證是 `EnemyPunchDefinition`（2026-09-05 加入，本 ADR 之後的第 4 份 Definition）：只動了一份 `.asset`、一列 `transitionMappings`、一列 `actionDefinitions`，`ActionState.cs`／`ActionDefinitionSO.cs`／`StateMachineConfigSO.cs` **當時一行未改**。<br>⚖️ **誠實的但書（已由使用者裁決不影響通過）**：同批新增了 `AIInputSource.cs`，但那是因為**敵人原本連輸入來源都沒有**，與「Action 系統需不需要改」正交——玩家加第 4 個 Action 不需要它。<br>📌 **已知邊界（非 Fail）**：新增一個**新 slot** 仍需 `ActionSlot` enum 加一員 ＝ 改程式。§3.2 已明文接受該成本（「比照 `StateType` 先例，且該成本本來就該被看見」）。**B 驗的是「加 Definition」，不是「加 slot」。**
- [x] **C. 既有 Idle／Move／Jump／Roll～~／Throw~~ 無回歸** —— ✅ **2026-09-06 Play 通過**：Idle／Move（含收步）／Jump（含落地）／Roll（含烘焙位移）各重複驗證無異常。<br>🔴 **Throw 已依計畫退場，刻意不列入回歸範圍**：`actionDefinitions` 非空 ⇒ `paramsMappings` 相容退路整條不走 ⇒ `ThrowDefinition` 不再被解析（`docs/11` §5.1 的既定移交，Slot1 已交給 Melee）。該死接線已於 2026-09-05 從 `PlayerStateMachineConfig` 移除，並由 **W8** 守住不再長回來
- [x] **D. EditMode 全綠**（含 A13′／A19 維持）—— 🔄 **2026-09-04 撤回打勾，需對「當前」測試集重跑**：原勾成立於 2026-09-02，當時 `SlowEffectTests`／`PrefabWiringTests`／PlayMode 測試層**都還不存在**，Codex 批次（近戰 sink／站位／per-slot 路由）也尚未併入。2026-09-04 實跑出現三條紅（成因：EditMode 不呼叫 `Awake` ⇒ sibling 快取為 null，屬 fixture 缺環境而非斷言錯誤；已以 `internal ResolveEffectState()`／`ResolveHitbox()` 修正）。<br>✅ **2026-09-04 對擴張後的測試集重跑，使用者確認全綠**——涵蓋 `ActionStateTests`（含 T22–T25）／`SlowEffectTests`／`ArchitectureRegressionTests`（A1–A26）／🆕 `PrefabWiringTests`（W0–W6 接線稽核）／🆕 PlayMode（P1–P4）。**本次的勾綁定的是這一套測試集。**<br>📌 **教訓**：Acceptance 的勾綁定的是「當時那一套測試」，測試集擴張後該勾**不自動延續**。<br>以下為 2026-09-02 當時的紀錄 —— ✅ **使用者實跑確認（2026-09-02）**。過程暴露三類問題並全數修正：①`StateMachineConfigSO` 缺 `using Project.Core.StateMachine.Actions` ⇒ CS0246（遠端分支從未編譯過）；②**`ActionState` 冷卻回歸**——`Complete()` 先 `ResetExecutionState()` 清掉 `_activeSlot`／`_definition`，`OnExit()` 才寫冷卻 ⇒ **自然播完的 Action 永遠不進冷卻**（T19 抓到，已抽出 `CommitCooldown()` 在兩個結束路徑各呼叫）；③三條直接呼叫 `OnEnter` 的測試未表達 request，已對齊新 baseline（斷言未放寬）。<br>✅ **A22 已轉綠**——本條的主要觀察點成立：該測項自 ADR-004 落地起一直為紅（斷言 `IActionReleaseSink`，介面實名 `IActionLifecycleSink`）
- [x] **E. 零 GC**，穩態 `0 B/frame` —— ✅ **2026-09-06 通過**（使用者依 `docs/02` §7.4 SOP 以 **Development Build ＋ Profiler** 複驗）。涵蓋本輪新增的熱路徑：`ProcessIntents`／`EvaluateInterrupts`／`ActionState.OnTick` 與連段切段／`AIMovementSource` 的 Hold 側移每幀計算／`GroundEffectSink` 的 `OverlapSphereNonAlloc` ＋ 探地 `RaycastNonAlloc`（皆用預配置緩衝）
- [x] **F. 沒有長出第二個 gate／interrupt 權威**（ADR-004 D2 的延續）—— ✅ **靜態稽核**（對定稿後的檔案重跑符號搜尋，不依賴編譯）：`_cooldownEndTime` 僅存在於 `ActionState.cs`；`CanEnter` 與 `CanReenter` **同源於單一 `TryResolveRequest`**，未引入新決策來源；`ActionState` 仍只讀取 facade（`IsPlaying`／`GetNormalizedTime`），從不 `Play`；`Core/` 下 `Instantiate` 零命中。✅ **A24 已實跑全綠（2026-09-02）**——本條自此由機器守衛，不再依賴人工符號搜尋。（該測項合併時由 A23 順延，因本機同輪的 AnimationKey 不變量先佔了 A23。）
- [x] **G. Slow 跨系統自動傳播，且五個下游檔案零修改** —— ✅ **使用者 Play 實測確認（2026-09-02）**：敵人被冰法命中後，①**跑步動畫自己變成走路動畫**（速度階層自己降級）／②**腳步聲自己變疏**（Footstep 節奏跟著實際速度走）／③**停下來時選的是慢速停步動畫**——而 `LocomotionModel`／`LocomotionSpeedSmoother`／`LocomotionStopSelector`／`FootIKController`／`AudioController` **五檔零修改**（由 **A25** 機器守衛）。<br>📌 **本條原本只存在於 `docs/11` §7.6 的引用中、從未寫進本 ADR**（§4 只列到 F）——2026-09-02 補列並同時記錄通過。<br>⚖️ 這是本輪**架構價值最高**的一條：Slow 沒有告訴任何人它存在，下游全部自動正確。與 Throw 的 release timing 不同——後者是時間精度（外行看不出難度），本條是外行**看得到結果**、內行**知道有多難**。

**未通過**：先修本 ADR ／ `docs/11` → 再驗證 → **不得補 workaround**。
若 **D1 被證偽**（單一 identity 撐不住所有消費者），轉 `Rejected`，code／ADR／invariant 一起 revert 回 ADR-004 的單一 Definition 基線。
**Revert 成本低**：本 ADR 的改動全是加法，Throw 已於 ADR-004 獨立結案、不受影響。

---

## 5. 修訂紀錄

| 日期 | 修改 | 原因 |
|---|---|---|
| 2026-09-02 | 建立草案（`Proposed`） | 作品集方向調整，FU-1／2／3 同時成為阻擋項 |
| 2026-09-02 | `Proposed → Trial` | ADR-004 `Accepted`，「同一時間只允許一個 Trial」解除 |
| 2026-09-02 | **code-first 第一輪落地**：`ActionSlot` 身分 ＋ 多 Definition ＋ per-slot 冷卻 ＋ Action→Action 重入。**D1／D2 未被推翻**，兩條決策一字未動。實作推翻的是**位置**與**重入實作**（詳見 `docs/11` §3.4），兩者都屬本 ADR 明文不凍結的範圍 | Fold-back：Trial 期允許 code-first，工作包結束前同步文件 |
| 2026-09-02 | **瘦身：五條決策砍到兩條**（原 D2／D3／D4 下放 §3 表格）；候選比較與需求清單移入 `docs/11` §3；改採 code-first | 檢討發現 ADR 比它要守護的程式還長。既有 authority 的複述、routing 事實、實作分析**都不該佔用 ADR 的凍結力**——那會稀釋「ADR ＝ 改錯會造成架構污染」的訊號 |
| 2026-09-02 | **EditMode 全綠，D／F 成立**。過程抓到一個**真回歸**：冷卻只寫在 `OnExit`，但 `Complete()` 會先清空 `_activeSlot`／`_definition` ⇒ 自然播完的 Action 不進冷卻。已抽出 `CommitCooldown()` 於兩個結束路徑各呼叫（冪等）。**D1／D2 仍未被推翻** | 冷卻細節屬 §9 明列**不凍結**範圍，修在程式即可，不需改本 ADR 的決策內容 |
| 2026-09-02 | **`ActionSlot` 改名**：`Primary`／`Secondary`／`Tertiary` → `Slot1`／`Slot2`／`Slot3`，`Reaction` 移到 100 起的保留段。**D1 未被推翻**——身分仍是單一 enum、仍只有一把鍵 | 原命名自稱有語意、實為拉丁文序號，與語意命名的 `Reaction` 混用 ⇒ 無成長規則，且把身分綁在「按哪顆鍵」（按鍵屬 Presentation）。命名在 §9 明列**不凍結**，且資產尚未接線 ⇒ 此刻成本最低 |
| 2026-09-06 | ✅ **`Trial → Accepted`：A／B／C／E 由使用者實跑通過**（Editor Play ＋ Development Build Profiler），加上先前的 D／F／G ⇒ **A–G 全數成立**。**D1／D2 兩條決策自 Trial 起一字未動、從未被推翻。**<br>結案時記錄一項**表現層**問題（連段朝向與發射方向不一致），根因是「朝向／瞄準方向由誰決定」缺單一權威——**不在本 ADR 決策範圍**，已登記 `docs/13` §4.4 交後續 ADR 處理，**未補 workaround** | Trial 的存在目的就是「由第一個 vertical slice 驗證」。三個技能已在 Play 中各自觸發、per-slot 冷卻不連坐、既有 locomotion 無回歸、零 GC 複驗通過 ⇒ 驗證完成，決策內容凍結 |
| 2026-09-06 | 📌 **本 ADR 的 ⛔ 名單（`docs/11` §10.2）自此解除**：`MotionDriver` 位移路徑、`AnimationFacadeBase` 契約、`LocomotionModel` 三者不再被 Trial boundary 擋住 | Acceptance **G**（五個下游檔案零修改）的觀察期已結束並通過。後續實作順序見 `docs/13` §7 |
