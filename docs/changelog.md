# 專案開發更新日誌 (Changelog & Learning Record)

> **分卷制（2026-07-25 起）**：本檔只保留**最近 4 個版本**；更早的歷史全數在 **[`docs/changelog-archive.md`](changelog-archive.md)**（一字未改，版本／章節編號原樣保留）。
> **為什麼分卷**：changelog 是 append-only 歷史，日常開發不需要（也不該）整檔讀入——單檔膨脹到 800+ 行後，任何一次查閱都是全檔成本。分卷後「查最近進度」與「考古」是兩個不同成本的動作。
> **新增版本一律寫在本檔頂端**；本檔超過 4~5 個版本時，把最舊的搬進歸檔卷並更新卷末索引表。

---

## [v0.35] - ADR-005 Trial：ActionSlot 身分讓多 Action 共用一顆 ActionState（2026-09-02，⏳ 待編譯與驗收）

`docs/08` §11.1 登記的 FU-1／FU-2／FU-3 一次解掉。三者共同根因是「系統裡沒有『這是哪一個 Action』的概念」——概念不存在，查表就只能用 `StateType` 當鍵、mailbox 只能是無名旗標、中斷只能比型別。新增 `ActionSlot`（`None`／`Primary`／`Secondary`／`Tertiary`／`Reaction`）作為單一身分，輸入映射、per-slot 冷卻、external request、Action→Action 中斷全部以它為鍵。

`IntentData.FireRequested` 改為 `RequestedActionSlot`，writer 不變。`StateMachineConfigSO` 新增一條**平行的** `actionDefinitions` 索引，刻意不動既有四張以 `StateType` 為鍵的表——改它們會波及 Jump／Roll 等與 Action 無關的狀態；清單為空時退回舊路徑，既有 Throw／Damage 資產不用改任何欄位。`ActionState` 的冷卻由單一 `float` 改為 per-slot 陣列，仍住在 `ActionState` 內（ADR-004 D2 未破）；Definition 改為每次進入時依 request 現查。`BaseState.CanReenter` 預設 `false`，`ActionState` override 以支援 Action→Action 中斷，判準與 `CanEnter` 同源，沒有引入新決策來源。

本輪採 code-first（ADR-005 Trial ＋ Fold-back 規則）。程式推翻兩個原假設：`ActionSlot` 原放 `Core/StateMachine/Actions/`，但 Presentation 的 `ThrownProjectile` 需要它而 `LayerRules` 禁止該 namespace，⇒ 移到 `Core/Actions/`——**身分屬於跨層 seam 層，不屬於 FSM 層**，這個結論是架構測試教的；重入的第一版就地 `TransitionTo`，會讓字典迭代順序決定結果並繞過更高優先的狀態（Roll），⇒ 改為與其他候選走同一套 priority 比較。另接受一個新耦合：`OnEnter` 現在必須重新解析 request。

ADR-005 同輪瘦身，五條決策砍到兩條——既有 authority 的複述、schema routing 事實、實作分析都不該佔用 ADR 的凍結力，那會稀釋「ADR ＝ 改錯會造成架構污染」的訊號。

順帶修掉一個既存缺陷：**A22 自 ADR-004 Trial 期起一直是紅的**。它斷言 `IActionReleaseSink`，但該介面早已改名為 `IActionLifecycleSink` 並從 1 個方法擴為 3 個，斷言與 `docs/08` §2.7 都沒同步。這也意味 ADR-004 §10 的 D 當時是在不成立的基礎上打勾的。教訓：改名要一併 grep 測試與文件。

新增 T18–T21 與 A23（守 ADR-005 D1：身分只准宣告一次、冷卻不得外流）。⏳ **本批在遠端容器完成，容器內無 Unity 與 C# 編譯器，尚未編譯過**；EditMode、資產接線、Play 與 Profiler 零 GC 全部待驗——熱路徑有改動（`ProcessIntents`／`EvaluateInterrupts`），零 GC **必須**複驗。

## [v0.34] - ADR-004 Accepted：Action 進 FSM 拓撲結案（2026-09-02，已驗收）

Throw vertical slice 通過 ADR-004 §10 的 Acceptance Criteria，ADR-004 自 `Trial` 改為 `Accepted`——**本專案第一個走完 `Design → Trial → Implement → Observe → Revise → Accept` 全流程的 ADR**。D1–D7 decision content 自此凍結進入 Immutable Log。

A（Play 全程跑通）與 C（既有 Idle／Move／Jump／Roll 無回歸）由 Play 驗收；D（EditMode 全綠）與 E（穩態 `0 B/frame`）由實跑確認。B 與 F 改以**靜態稽核**完成，明細新增為 ADR-004 §10.1：以符號搜尋列舉三個權威的所有呼叫點，確認 `ActionState` 全檔只有 `IsPlaying`／`GetNormalizedTime` 兩處唯讀查詢而從不呼叫 `Play`、冷卻僅 `OnExit` 寫入與 `CanEnter` 讀取、`Core/` 下 `Instantiate` 零命中、sink 呼叫點全部在 `ActionState` 內。

稽核同時釐清 B 的正確讀法是「**Action 子系統**的動畫權威唯一」而非專案全域只有一個播放點——`LocomotionModel` 的 Stop 選片播放屬 ADR-003 D4 授權，早於 ADR-004 且與之正交。另登記兩處防禦性冗餘（release 雙重去重、`Cleanup()` 多路徑呼叫），現階段判定為冪等 safety net 而非 workaround，但多 Action 落地後需重新評估是否滑向兩個真相。

作品集方向同日重訂：Throw 降級為 Acceptance 證據與架構歷史案例，不再投入手感調整，也不出現在展示影片。主線改為 Quick Spell／Ice Spell／Melee Slash 三技能加 Slow effect，由新增的 **ADR-005（Action Identity，同日翻牌 `Trial`）** 與 `docs/09-multi-action.md` 承載。ADR-005 只凍結五條決策，identity 的表示法、容器形狀與 API 一律不凍結。原 WP1（鏡頭＋Aim＋Throw 依 AimPoint）解散——該包的存在理由是救 Throw 手感，前提已消失。

治理面另新增 `CLAUDE.md` 的 Remote Container Exception：遠端容器 session 中純文件變更可由 Claude commit／push，變更集一旦出現程式或 Unity 資產即整批退回原禁令。

## [v0.33] - Walk Pending Stop 相位等待（2026-08-21，已驗收）

Walk LU／RU Fade `0.15 → 0.25 s` 後全身瞬間變動仍明顯，確認問題是固定起點 pose mismatch，繼續加長淡入只會讓錯誤混合更久。放開 Walk 現在先進入 `LocomotionStopRuntime` 私有 Pending 階段：用 Stop 入場 `FootPhaseCurve` 的連續值比對 Walk loop 烘焙鍵，選下一個最近的 authored 入場時刻，到點才播放。

Pending 期間暫停 B9 smoother、維持 release-entry 速度與方向並走既有 Procedural 位移，避免先減速再被 Stop 曲線重新加速。重新輸入、離地、Jump／Roll 接管仍走既有中斷；child clock 失效或超過 0.5 秒立即播放，避免卡死。此行為只套 Walk，已驗收 Run 零改動。

新增純函式最近相位測試、Pending runtime 階段測試與 model 整合測試。沒有手填 0／0.5 相位、沒有新增黑板、Gameplay State、Facade、MotionDriver、Mixer 或 AnimationClip 修改；ownership／hierarchy 不變，不開 ADR。明確代價是停止反應距離最多增加約四分之一 Walk 週期，換取完整姿勢在 authored 接點銜接。

最終 Unity EditMode＋Play 驗收通過：任意 Walk 腳相放開能自然走到匹配點再 Stop，無全身瞬跳、無先慢後衝；Pending 期間重新輸入、Jump、Roll 均能立即取消且不補播舊 Stop。本節正式收案。

## [v0.32] - Walk Stop 主導子動作時鐘（2026-08-21，待 Play 複驗）

Walk 偶發同腳連踩不是速度接縫：穩態 `0.3651` 下 Stop 曲線沒有前衝峰值，問題集中在腳相交越附近。根因是初版以 Locomotion Mixer root 的加權 `NormalizedTime` 查 Walk Bake Data；Mixer 未同步、各 gait 的相位原點又不同，少量 Run 權重也可能在零交越附近把符號推到另一側。

Facade 新增通用、唯讀的 `TryGetDominantChildNormalizedTime`；Animancer 實作以零配置索引迴圈取得最高權重直接 child，同權重固定取前者。`LocomotionModel` 改用該時鐘查已選 tier 的 loop Bake Data，Stop 播放進度仍走原本主層時間。補 model-level 回歸測試，刻意讓 Mixer root 與主導 child 落在相反腳相，鎖住必須依 child 選片。

沒有開啟 `SynchronizeChildren`，避免改寫子 playable 速度並破壞已驗收的門檻／PlaybackSpeed 校正；也沒有新增黑板、Gameplay State、Clip 引用或 MotionDriver 契約。此為既有 Facade 的通用唯讀查詢擴充，不改 ownership／hierarchy，故不開 ADR。符號選片約 0.24 週期的理論誤差仍如實保留，待腳相交越點 Play 複驗後再決定是否需要相位起始對齊。

Play 複驗隨即證實剩餘的固定起點 pose mismatch 會造成全身瞬間些微跳動。先依 Data／Presentation 優先序把 Walk LU／RU 的 Fade `0.15 → 0.25 s`；其他資產與 Runtime 零改動。若仍明顯，不再加長 Fade，而是另案設計等待最近 authored 入場相位的 pending Stop。

## [v0.31] - Phase C1.1 Run Forward Stop（2026-08-20，待 Play 驗收）

C1 Walk 驗收通過後，依既定觸發條件加入 Run Stop。新增的只有入場強度 tier 選擇：`0.75–0.875` 命中 Run，之後逐字共用 authored FootPhase 選片、單一 `LocomotionStopRuntime`、通用 Facade callback 與 MotionDriver 曲線位移。兩帶重疊時 fail closed 回 B9；Sprint 因 Catalog 無 Stop 資產維持原路徑。

Run LU／RU 已用 X Bot 60 FPS 重烘，來源非 Loop。Play 驗收抓到 RU 在 `t≈0.117 s` 的峰值會造成小暴衝，因此 RU playback 調為 `1.2588`，峰值對齊 Run 錨點 `4.696 m/s`；Walk／Run 下界同步收緊為 `0.35／0.75`，避免加速途中放開時用較快 Stop 曲線反推角色。`LocomotionModel` 增加 Run loop Bake Data 與兩支明確變體配置，但不加黑板、不加 Gameplay State、不改 Facade／MotionDriver 契約。功能測試鎖住 tier、Run 腳相、60 FPS 與非循環資產前提。

下界收緊後的首輪 Play 發現 Run Stop 消失：根因是 tier 原先讀取本幀已被 SmoothDamp 衰減的值，穩定 `0.75` 在 60 FPS 首幀即成為 `0.7388`。改為在 Tick 前快照放開入場速度；B9 輸出仍用 Tick 後值。這是取樣時序修正，不新增狀態或第二真相，並以 model-level 回歸測試鎖住。

第二輪 Play 仍未觸發，進一步確認 SmoothDamp 穩態本身是 `0.74999994` 而非精確 `0.75`；第一版測試手塞精確值，未忠實模擬 dynamics。Band 比較改沿用既有 `Epsilon=0.001`，測試也改為實際跑 120 幀 SmoothDamp。有效下界僅到 `0.749`，速度容差 0.13%，不回到 `0.70` 的暴衝範圍。

## [v0.30] - Phase C1 Walk Forward Stop（2026-08-20，待 Play 驗收）

Walk Stop 以 `LocomotionModel` 私有 phase 落地：release edge 只在 Walk 帶觸發，依 authored FootPhase 選 LU／RU，播放走通用 Facade，位移仍收斂到 MotionDriver。Run／Sprint 未命中時維持 B9。

否決初稿的 `LocomotionModel → FootIKPoseData` 回讀；改以 Facade 主層時間查單一 `Bake_WalkFwdLoop.FootPhaseCurve`，A4 守住 IK 不回流 Core。新增 Stop runtime／selector、播放頭差值曲線多載、功能測試與 A12～A15。世代持續遞增，callback 只設旗標，避免 Jump／Roll 被舊回調蓋回 Locomotion；Roll 舊多載算法不變。

Unity 序列化資產未由 AI 修改：V1 `moveSpeedSource`、兩份 Stop Transition、Facade mapping 與 Model refs 待 Inspector 接線。

## [v0.29] - M3.x-B：Footstep 落地，順帶示範「契約不放寬也能解」（2026-07-28）

> 輪 3 的主體。需求一句話：腳踩地時播腳步聲。卡住的從來不是偵測演算法，是**「誰能寫黑板」**。

### 1. 契約擋在路上，而正解是不去動它

`FootstepDetector` 要發事件、`AudioController` 要收，中間需要一個與 Hierarchy 順序無關的 seam。最直覺的做法是讓 Detector 寫黑板——但 `IPresentationController` 的契約白紙黑字：**對 `PlayerRuntimeData` 只讀不寫**。

放寬它、開 Footstep 特例、或讓 Detector 直接寫，三條路都被否決。最後的形狀是：

```
FootstepDetector ──value struct──▶ PresentationPipeline ──▶ PlayerRuntimeData ──▶ AudioController
   （偵測，不寫）                    （唯一寫入者）           （廣播快照）          （只讀不清）
```

**偵測器回傳值、管線負責寫。**「誰能寫黑板」與「誰負責偵測」由**型別**分開，而不是靠紀律分開——契約一個字都不用改。

這個設計不是新發明：`IArbiterSource` 在輪 4 就是這樣做的（來源回傳 `ArbiterData`、`ArbiterPipeline` 合併後獨佔寫入）。**同一個問題出現第二次時，能直接套上第一次的答案，這件事本身就是架構有在收斂的證據。**

### 2. 發布必須卡在所有 Controller Tick 之後——這是唯一的正確性依據

`PresentationPipeline.Tick` 拆成兩步：①依序驅動 Controller ②收集來源 → 合併 → 整體覆寫。

**順序不可調換。** 若發布混在迭代中途，同窗口的 consumer 讀不讀得到就取決於 `GetComponentsInChildren` 的回傳順序——也就是**階層裡誰在上面**。拖動一個物件會改變腳步聲的正確性，而且完全看不出關聯。

拆成兩步之後：Controller 在①讀到的永遠是**上一帧**發布的快照，發布在②固定發生。代價是固定一帧延遲（約 16ms，聽不出來），換到的是「每個事件恰好被每個 consumer 看到一次」的**結構保證**——不需要 sequence number、frame identity、consumer acknowledgement 這三樣通常讓 event 系統開始長胖的東西。

### 3. 廣播快照，不是可消費佇列

consumer **只讀不清除**。若做成「讀了就消費掉」，第一個 consumer 會吃掉事件，未來的 VFX／鏡頭震動就收不到——而且症狀是「有時候有、有時候沒有」，取決於誰先跑。

整體覆寫本身就是復位機制，所以 **`ResetTransientState()` 完全不用改**。這一點特別值得記：順序 7 是 dev-spec 脆弱點警告的第 1 條，任何要為它加例外的方案都在替下一個人埋雷。這裡不需要例外，因為復位是發布的副作用（與 `Arbitration` 同源）。

### 4. 偵測：速度雙門檻 ＋ 最小垂直行程，不用時間閘

腳底高度 ＝ `FootPosition.y − FootBottomHeight`，取 **pre-IK** pose。不 raycast、不問地面。

* **速度雙門檻（Schmitt trigger）**：下降速度達 `ArmDescentSpeed` 才「上膛」，回升到慢於 `FireDescentSpeed` 才「擊發」。兩個門檻不同值，所以速度在 0 附近抖動時**永遠回不到上膛狀態**——打的是時間軸上的抖動。
* **最小垂直行程**：距上次落腳之後腳底必須先抬高 `MinLiftExcursion`，下一步才算數——打的是空間上的微幅假動作（Idle 呼吸、腳貼地晃動）。

**刻意不用「最小間隔時間」當主要去抖手段**：時間閘的門檻必須依最快步頻反推，估錯就會把 sprint 的真實腳步濾掉。兩道機制各打一種雜訊，比一道時間閘準確得多。

**語意是「動畫落腳事件」，不是物理 ground contact。** 所以讀 IK 套用**前**的 pose——斜坡上 IK 會把腳修到別處，但落腳的**時機**不變，而玩家聽到的聲音要對齊看到的動畫。

### 5. Landing 抑制：抑制的是「報告」，不是「發生」

落地那一帧 `JustLanded == true` 時不報腳步（落地是更高階的語意）。但**抑制發生在 tracker 推進之後**——跨帧狀態照常前進。

若順手把 tracker 一起回滾，落地後的第一步會因為行程基準錯亂而漏報或誤報，而這種 bug 只在「跳完之後走第一步」時出現，極難重現。測試裡專門有一條對照組守這件事。

### 6. 可測性逼出來的一個小拆分

`Time.deltaTime` 在 EditMode 不可控。若把它藏在偵測方法內部，所有演算法不變量都只能靠 Play 模式肉眼驗。

所以偵測拆成兩層：`FootPlantTracker.Advance(height, deltaTime, settings)`（單腳純推進）與 `FootstepDetector.Detect(pose, justLanded, deltaTime)`（雙腳＋抑制），兩者都收顯式時間步長。`Evaluate` 退化成三行轉接。

先例是 `FootIKController.ComputeFootWeight`——把純判定公開成可測單元，是這個專案已經在用的手法。`FootPlantTracker` 做成值型別跨帧狀態，同 `LocomotionSpeedSmoother`。

### 7. 檔案與檢核

* 新增 4 檔：`Core/Blackboard/PresentationEventData.cs`（必須住 Core.Blackboard——`LayerRules` 禁 `Core/Blackboard` 認識 `Project.Presentation`）、`Presentation/IPresentationEventSource.cs`、`Presentation/Footstep/FootPlantTracker.cs`、`Presentation/Footstep/FootstepDetector.cs`
* 修改 6 檔：`PlayerRuntimeData`（加欄位）、`PresentationPipeline`（第二陣列＋發布步驟）、`CharacterPipelineRunner`（建構子多一個陣列，**未動 pipeline phase**）、`AudioController`（消費）、`AudioEventId`（`LeftFootstep = 1`／`RightFootstep = 2`，只增不改）、`ArchitectureRegressionTests`（A5 白名單）
* **測試 99 → 120**（+21）。既有 `PresentationPipelineTests` 3 條因建構子簽名改變一併更新——**沒有為了讓舊測試繼續編譯而加多載**，測試該反映真實 API
* A5 新增的那一列同時是 **`IPresentationController` 契約的機器化守衛**：任何 Controller 想寫這一區都會變紅，契約不再需要靠人記得

### 8. 沒做的事

沒有 EventBus、沒有 event queue、沒有 sequence／frame identity、沒有 consumer acknowledgement、沒有 surface／material variation framework、沒有新增管線階段、沒有動 ADR。`PresentationEventData` 今天只有兩個 bool。

---

## [v0.28] - M3.x-A：擁有權跟著寫入權走（2026-07-27）

> 輪 3 Footstep 的**前置**。調查階段確認 Foot Contact 偵測完全不需要 `AnimationFacadeBase` 暴露 Model／Clip／Mixer／normalized time，**ADR-003 D4 一條契約都沒被觸及**（維持 Accepted、不修改、不新增 ADR）。真正卡住的只有一件事：`FootIKPoseData` 的擁有權。

### 1. 問題不是「怎麼把資料傳給第二個 Reader」，是擁有權一開始就錯了

| 管道 | Writer | 原本的 Owner | 一致？ |
| --- | --- | --- | --- |
| `FootIKTargetData` | `FootIKController` | `FootIKController` | ✅ |
| `FootIKPoseData` | **`FootIKRig`** | **`FootIKController`** | ❌ |

`FootIKController` 擁有一份**自己不寫**的資料。單讀時只是輕微味道；一出現第二個 Reader，新 Reader 就被迫向另一個 Controller 要引用——**直接違反 `IPresentationController` 的「Controller 彼此不得互相引用」**。

所以這一輪不是接線，是把本來就寫錯的所有權修回來：

> **管道的 lifetime owner ＝ 該管道的唯一 Writer。**

* Target：Controller 寫 → Controller 擁有 → 注入給 Rig 讀（維持單寫單讀）
* Pose：Rig 寫 → **Rig 擁有** → Controller 向它取引用（自此**單寫多讀**）

### 2. owner 的語意要限定死，否則會被誤讀成業務職責

文件一律寫成：**`FootIKRig` owns the *lifetime* of the pose snapshot *because it is the sole writer* of that snapshot.**

這是**生命週期／權威的擁有權**，**不是業務擁有權**。Rig 不因此取得任何決策職責——它不解讀資料、不判定 plant/lift、不認識任何消費端，兩個方向依然是純轉接、零決策分支。**解讀屬各 Reader 自己的事。**

這句話特別寫下來，是因為「Rig 現在擁有 Pose」很容易被下一個人讀成「Footstep 邏輯該放 Rig 裡」——那正好會摧毀 M3.1 好不容易換來的 Adapter 純度。

### 3. 一個小選擇消掉整類時序 bug：欄位初始式而非 `Awake`

```csharp
private readonly FootIKPoseData _poseData = new FootIKPoseData();
```

欄位初始式在**元件建構時**執行，早於所有 `Awake`。於是「讀取方拿不拿得到有效實例」不再取決於「誰的 `Awake` 先跑」——而元件間的 `Awake` 順序在 Unity 是未定義的。

**這把一個時序紀律換成了結構保證**，代價是零。對照組：如果沿用「Controller 建立、Bind 進 Rig」的形狀再讓 Detector 向 Rig 拿，Detector 就必須改到 `Start` 解析才安全——那是用紀律換正確性。

📌 刻意寫成 `new FootIKPoseData()` 而非 target-typed `new()`，是為了讓「唯一建構點」可被靜態掃描守住。

### 4. 順帶讓兩條管道真正獨立

`OnAnimatorIK` 原本是 `if (_targetData == null || _poseData == null) return;` ——一個 return 守兩條管道。現在拆開：Pose 段沒有前提（它恆存在），只有 Target 段需要等 Controller 綁進來。

**唯一的行為差異**：場上沒有 `FootIKController` 時，Pose 快照現在仍會被寫入。**目前不可觀察**（Pose 的唯一讀取方正是 Controller 本身），但它讓 M3.x-B 的偵測器不會因為「場上剛好沒有 IK Controller」就靜默收不到資料。

### 5. 測試

A11 的掃描方式有個陷阱值得記：**`FootIKTargetData` 與 `FootIKPoseData` 成員同名**（都有 `LeftFootPosition` 等）。若照抄 A5 那種「以成員名做賦值型 regex」的寫法，會把 Controller 對 Target 的**合法**寫入誤判成違規。改掃**建構點**（`new FootIKPoseData`）——它直接編碼「擁有權跟著寫入權走」這條規則本身，且不受同名干擾。

* **A11**（新）：`FootIKPoseData` 建構點恰好一個，且必須在 `FootIKRig.cs`
* `PoseData_IsAvailableImmediately_WithoutAnyAwakeOrBind`：守「不需要關心 Awake 順序」這個結構保證本身——若有人把它改回在 `Awake` 裡 `new`，這條會紅
* `PoseData_IsTheSameInstance_SharedByOwnerAndReader`：守「單寫多讀共享同一份實例」。若 Reader 自己 new 一份，會**靜默**讀到永不更新的空快照——不報錯、只是沒反應
* `[Test]` 96 → **99**

### 6. 沒做的事

**`FootstepDetector` 完全沒有動工**，這是刻意的 milestone 切分：

```
M3.x-A（本輪）  ownership 修正 → 單寫多讀驗證 → 行為零變化 → 文件同步
M3.x-B（下輪）  Foot Contact Detector → threshold / hysteresis / plant-lift → event channel → Audio
```

也沒有碰：`AnimationFacadeBase`／`AnimancerFacade`／`MotionBakeData`／`FootPhaseCurve`／`AudioEventId`／任何 Movement Model。

---

## [v0.27] - 兩個 bug 互相抵銷：`Move(Vector3.zero)` 與那個「不知道為何存在的保護」（2026-07-27）

> v0.26 的暫停留下一條記錄在案的缺口：「暫停不封鎖角色輸入，理論上按跳躍可能補跳」。人工驗收時它變成兩個**看起來無關**的回報，追下去發現是同一個根因，而且**其中一個 bug 正在掩蓋另一個**。

### 1. 兩個觀察

1. 暫停中站在地上按跳躍，解除後**不會**跳（＝文件預測的缺口沒發生）
2. **只要站在地上，解除暫停都會聽到落地聲**（＝角色根本沒離地）

第 2 條顯然是 bug。第 1 條看起來是好消息——但 §7.3 當時寫了一句話：

> 「若沒轉移，**要查出是什麼擋住的**——依賴一個不知道為何存在的保護，比沒有保護更危險。」

### 2. 根因：零位移的 `Move` 會毀掉 `isGrounded`

位移出口是 `characterController.Move(finalMovement * Time.deltaTime)`。暫停時 `deltaTime = 0` ⇒ 等同 `Move(Vector3.zero)`。而 Unity 的 `CharacterController.isGrounded` 是由**上一次 `Move` 有沒有向下撞到東西**決定的——零位移沒有向下推，於是回報 **false**。

逐帧展開：

| 帧 | `isGrounded` | 後果 |
| --- | --- | --- |
| 暫停第 1 帧 | true（暫停前那次真實 Move 的殘值） | — |
| 暫停第 2 帧 | **false** | `JustLeftGround` 假觸發、`_wasGrounded = false` |
| 暫停期間 | false | `data.IsGrounded` 恆 false |
| 解除後第 2 帧 | true | **`JustLanded` 假觸發 → 落地聲** |

**兩個觀察同時被解釋**：落地聲是假的 `JustLanded`；而「不會跳」是因為 `JumpState.CanEnter = JumpRequested && IsGrounded`，暫停中 `IsGrounded` 是 false 所以直接失敗。

**擋住跳躍的不是任何設計，是另一個 bug 的副作用。**

### 3. 為什麼必須同批修

修掉落地聲 ⇒ `IsGrounded` 在暫停期間正確地維持 true ⇒ `CanEnter` 成立 ⇒ **跳躍缺口當場打開**。而且缺口比原本預測的嚴重：`JumpState` 的落地判定靠 `_airborneTimer += deltaTime`，暫停時恆加 0 ⇒ `IsLanded` 永遠 false ⇒ `CanTransitionAway` 永遠 false ⇒ **進得去、退不出來**。

所以兩件一起做：

* **`MotionDriver.IsTimeFrozen`**：`Time.deltaTime <= 0` 時整段跳過——不 `Move`、不重算觸地與邊沿旗標。⚠️ 守衛刻意表述為「**沒有時間流逝**」而不是「暫停」：`MotionDriver` 不認識暫停，它只知道沒有時間就沒有東西要積分。這讓守衛對任何造成 `deltaTime == 0` 的原因都成立，也不引入對應用層的依賴。
* **`GamePauseController` 實作 `IArbiterSource`**，暫停期間要求 `BlockInput`；由 Runner 新增的 `externalArbiterSources` 以 Inspector 引用注入。此後「暫停中不能跳」是**被設計出來的**，不是副作用。

### 4. Runner 為什麼需要「階層外來源」這個新欄位

`ArbiterPipeline` 的來源本來是 `GetComponentsInChildren<IArbiterSource>()`——只掃角色階層。而 `GamePauseController` 依定義**不掛在角色上**（它是應用層的全域狀態，v0.26 §1）。

解法是 Runner 加一個 `[SerializeField] MonoBehaviour[] externalArbiterSources`，沿用它既有的三個介面注入欄位的同款 pattern。方向是**角色收外部給的 source**，而不是角色去查詢全域——不需要 Singleton，也不需要讓 `Core` 認識 `App`。

這正好對上 v0.26 §5 記下的那條判準：**游標是「高層擁有、低層回報意圖」，封鎖是「低層擁有、高層提供來源」；方向相反，因為那兩個狀態的 scope 不同。**

### 5. 這一輪真正的收穫

不是修好兩個 bug，是**驗證了那句話**：「依賴一個不知道為何存在的保護，比沒有保護更危險」。

如果當初滿足於「實測沒事，收案」，那麼未來任何人修好 `isGrounded`（一個看起來完全無關、而且顯然正確的修復）都會**無預警地讓跳躍缺口重現**，且沒有任何線索指向關聯。這種 bug 會在半年後以「為什麼暫停完角色會跳一下」的形式回來，而且沒人找得到。

**把「為什麼它現在沒壞」查清楚，和把壞掉的東西修好一樣重要。**

### 6. 檔案與檢核

* `MotionDriver`：新增 `IsTimeFrozen` 守衛，三個位移出口各一道早退
* `GamePauseController`：實作 `IArbiterSource`
* `CharacterPipelineRunner`：新增 `externalArbiterSources` 欄位與 `CollectArbiterSources()`（一次性收集、純索引迴圈、無 LINQ）
* **新增測試 1 條**（`[Test]` 95 → **96**）：暫停時要求 `BlockInput`、解除後立即停止、且**不得順手抬別人的旗標**
* ⚠️ `MotionDriver` 的守衛**無法自動測**（需要控制 `Time.deltaTime` 與真實 `CharacterController`），走人工驗收 §7.2-M8 ⑥⑦⑧
* 🔴 **需要接線**：Runner 的 `External Arbiter Sources` 要拖入 `GamePauseController`，否則缺口仍開著

---

## [v0.26] - Hold／Tap 分流、應用層暫停、游標擁有權歸位：一顆鍵、兩個 scope（2026-07-27）

> 輪 4.1。v0.25 的 UI 模式是「按一下切換」，實際用起來想要的是兩件事：**按住** Alt 臨時放開滑鼠去點畫面上既有的 UI（遊戲繼續跑），**短按** Alt 開介面並暫停。同一顆鍵、兩種行為——聽起來像個小需求，實際上逼出了一層新的東西。

### 1. 暫停差點被做成 `ArbiterData` 的第 5 個旗標

第一個直覺是：既然已經有 `BlockInput`／`BlockIK`／`BlockAudio`／`BlockExpression`，那就加個 `BlockTime`。

**這是錯的，而且錯在一個之前沒有意識到的維度上：scope。**

`PlayerRuntimeData` 是**單一角色**的黑板，`ArbiterData` 是那隻角色的仲裁旗標。而 `Time.timeScale` 是**應用全域**的——它凍結的不是「這隻角色」，是整個世界。把全域狀態放進 per-character 結構，現在看不出問題（只有一隻角色），第二隻角色進場立刻露餡：**兩塊黑板都會聲稱自己擁有暫停**。

還有一個更硬的證據：`ArbiterPipeline` 的來源是 `GetComponentsInChildren<IArbiterSource>()`，**只掃角色階層**。一個全域暫停器根本不在那裡。而 CLAUDE.md 明禁 Singleton，所以「角色端 source 去查全域單例」這條路也堵死。

三條路同時撞牆 → 這個狀態需要自己的家。於是有了 `Assets/Scripts/App/`（design-doc §4.9），第一個住戶是 `GamePauseController`。

**這一層與角色層唯一的紀律差異**：它**自帶 `Update`**。`IArbiterSource`／`IPresentationController` 都明文禁止自帶 Update（時序由管線保證），但那條紀律的前提是「你屬於角色管線」——本層明確不屬於，沒有管線可掛，只能自己推進。差異寫進 §4.9，免得下次有人照著抄錯。

### 2. Tap 與 Hold 有個沒有免費午餐的取捨

tap ＝ 按下＋快速放開，而**放開之前無法知道它是不是 tap**。所以只有兩種可能：

* 按下就進 hold 模式 → **每次 tap 都會先閃一下游標**
* 等超過門檻才進 hold 模式 → **游標延遲約 0.25s 才出現**

這是時序的物理限制，不是實作技巧能繞過的。**裁決取後者**：Tap 不先觸發 Hold，接受 hold 的 0.25s 延遲。日後調整的是**門檻數值**，不是新增更複雜的判定機制——這句話是刻意寫下的，用來擋住未來「再加個預測性判定」的衝動。

分流方式走 **Input System 原生的 `Hold`／`Tap` interaction**，不自刻計時器。理由與 `GaitProfileSO.walkIsToggle` 完全同源：**操作語意是 per-game 差異，該住在資產裡而不是程式碼裡**。代價是多綁一條 action，而且 **Tap 門檻必須 ≤ Hold 門檻**——這是正確性條件不是調味，寫進 M3。

（自刻計時器那條路還有個陷阱：暫停時 `Time.time` 不走，得記得用 `Time.unscaledTime`。走原生 interaction 就完全繞開了。）

### 3. 進出邊沿刻意不對稱

```csharp
if (UiModeAction.WasPerformedThisFrame())      SetUiMode(true);   // Hold 撐過門檻
else if (_uiMode && !UiModeAction.IsPressed()) SetUiMode(false);  // 鍵已不在按下狀態
```

離場**不用**放開的邊沿訊號，而用「控制鍵現在是不是還按著」。因為 `IsPressed()` 讀的是控制本身的狀態、與 interaction 無關，所以**會自癒**——視窗失焦、Play 模式切換這類會吃掉放開邊沿的情境，不會讓 UI 模式永久卡住（那個 bug 一旦發生，症狀是「游標放不回去」，而且很難重現）。

### 4. 撞上輪 4 剛立的 Ownership 鐵律，選擇不繞過它

輪 4 才判定「`UiModeArbiterSource` 是**唯一**擁有 Cursor API 的元件」。而「短按開介面」隱含暫停時游標要出現——若 `GamePauseController` 也寫 `Cursor`，立刻有兩個擁有者，而且對撞是具體的：**暫停中按住再放開 Alt，對方的 `ApplyCursor(false)` 會把游標收回去，即使暫停還開著**。

要正確解決得抽出一個 cursor-mode 擁有者，但那正是輪 4 明文排除的「Cursor service 抽象」。**當下的裁決：最小版的暫停不碰 Cursor**——只切 `timeScale`，把「等真實壓力再決定」寫進 §7.3。

**然後壓力在同一個工作階段就到了**（「暫停時游標應常駐」），於是有了 §5。這件事本身值得記：**「等真實壓力」不是拖延的藉口，它真的會很快到來，而且到來時你會知道介面該長什麼樣——因為需求把形狀說清楚了**。若在輪 4.1 就憑空抽象，抽出來的很可能是「暫停自己存還原游標」那種埋著 LIFO 假設的版本。

同理，暫停**也不封鎖角色輸入**（`timeScale = 0` 已讓位移與動畫全停）。已知殘留：trigger 意圖仍會寫入、FSM 仍以 `deltaTime = 0` Tick，所以暫停中按跳躍可能在解除時「補跳」。**兩個缺口都記進 §7.3 並各自寫明未來的正解**——不封鎖輸入的正解是讓暫停器實作 `IArbiterSource` 由角色以 Inspector 引用（DIP），**不是**讓角色去查詢全域。

### 5. 壓力到了：`Cursor` 的擁有權搬到應用層（輪 4.2）

需求是一句話：**暫停時游標應常駐存在**。它逼出 §4 那個被延後的裁決。

考慮過兩條路：

* **存○還原**（`UiModeArbiterSource` 進 UI 模式前記住 `Cursor.lockState`，離開時還原而非寫死 `Locked`）。零新檔零接線，而且在**現行綁定下完全正確**——兩個模式共用 Left Alt，「按住中再短按」物理上不可能，所以不會有交錯。
* **抽出單一擁有者**：`App/CursorModeController` 把所有「想要自由游標」的來源 OR 起來，套用一次。

**選了後者。** 存○還原的正確性建立在一個**隱藏的 LIFO 假設**上：模式必須後進先出地退出。今天成立只是因為兩個模式剛好共用同一顆鍵；哪天暫停改綁 Esc，「按住 Alt 時按 Esc 暫停 → 放開 Alt」就會把游標鎖回去。**這種「今天對、明天無聲地錯」正是這個專案的文件一直在防的東西**，不值得為了省一個檔案買下來。

形狀直接沿用 `ArbiterPipeline`：**來源各報各的，單一擁有者合併後套用一次**。差別只在來源是 Inspector 明確引用的兩顆，沒有做成介面集合——第三個滑鼠模式出現時再一般化（同 `PresentationPipeline` 當年的節奏）。

順帶把 `ThirdPersonCamera.Start` 的初始游標鎖定也**移除**了。留著它就是第二個寫入者，「唯一擁有者」會淪為文件上的說法。代價是這顆元件缺席時開場游標不鎖、連帶相機不轉——**刻意讓它大聲壞掉**。

**一個值得記下的對稱性**：游標的解法是「高層擁有、低層回報意圖」（App 讀角色的 `IsUiModeActive`），而「暫停封鎖角色輸入」的未來正解卻是「低層擁有、高層提供來源」（角色的 `ArbiterPipeline` 收一顆 App 給的 `IArbiterSource`）。方向相反，判準卻同一個：**那個狀態的 scope 屬於誰，就由誰擁有**。游標是全域的，封鎖是每角色的。

#### 5.1 初版立刻踩到的 bug：「唯一擁有者」不等於「唯一寫入者」

第一版 `CursorModeController` 為了避免每帧覆寫，快取了「自己上次寫了什麼」，只在要求改變時才動 `Cursor`。實測症狀：**游標永久可見**。

根因是那個快取隱含一個假設——**我們是唯一會動 `Cursor.lockState` 的人**。但 Unity Editor 不是：Play 模式按 **Esc**、以及視窗失焦，Unity 內建都會強制解鎖游標（那是它讓你逃出鎖定游標的後門）。一旦被外力改掉，快取仍認為「已經套用過了」，於是**永遠不再修正**。

改成比對 `Cursor` 的**現值**而非快取後就自癒了：不管誰把它改掉，下一帧都會被拉回。仍然只在不一致時才寫，所以沒有付出每帧覆寫的成本。

**教訓比 bug 本身有價值**：這一輪花了很多力氣確保「程式碼裡只有一個寫入者」，但 `Cursor` 是**作業系統／引擎共管的全域狀態**，我們永遠不可能是唯一寫入者。對這類狀態，正確的模式是**收斂（converge to desired）而不是事件驅動的 set-once**——「我說了算」要靠每帧確認，不能靠記憶。

（副作用要誠實記：Editor 內「按 Esc 逃出鎖定游標」的後門會被我們立刻收回。現行方案下不成問題，Esc 本來就是暫停鍵。）

#### 5.2 暫停改綁 Esc，讓那個被否決的方案當場失效

驗收途中暫停鍵從「短按 Alt」改成獨立的 **Esc**。這順手證明了 §5 的選擇：兩個模式不再共用一顆鍵，**「按住 Alt → 按 Esc 暫停 → 放開 Alt」從此做得到**——正是當初判斷「存○還原」會踩爆 LIFO 假設的那個情境。**被延後的抽象在一小時內就兌現了；被否決的捷徑在同一小時內就失效了。**

連帶：`Tap` 門檻 ≤ `Hold` 門檻的相依解除，`PauseToggleAction` 也不再需要 `Tap` interaction。

#### 5.3 驗收時撞到的第三件事：`Alt`+`Esc` 是 Windows 系統快捷鍵

M8 ④ 原本寫「按住 Alt → 按 Esc 暫停 → 放開 Alt」。實測結果是**直接被丟出 Unity 視窗**——因為 `Alt`+`Esc` 在 Windows 是切換視窗的系統快捷鍵（等同不帶浮層的 Alt+Tab），OS 層就攔截了，Unity 根本收不到。同族還有 `Alt`+`Tab`／`Alt`+`F4`／`Alt`+`Space`。

**這不是程式問題，任何架構都擋不住**，只能靠選鍵避開。處理方式是**改測相反順序**（先 Esc 暫停、再按住 Alt、再放開）——它測到的不變量完全相同（兩個滑鼠模式交錯時，其一收手不得解除另一個的游標要求），而且不碰 OS 快捷鍵。

**記下來當紀律**：選 modifier 型的「持續按住」鍵位時，要先查作業系統保留了哪些組合。`Alt` 在 Windows 上特別危險——它參與多組系統快捷鍵，而遊戲業界又習慣用它做「放開滑鼠」。兩者相安無事的前提是**不要再跟其他鍵組合**。

### 6. 順手修掉的 Editor 錯誤（與本輪功能無關）

```
NullReferenceException: SerializedObject of SerializedProperty has been Disposed
GUI Error: You are pushing more GUIClips than you are popping
```

兩條是**因果**不是兩件事：`OnInspectorGUI` 進行到一半丟例外 → IMGUI 的 clip stack 沒 pop 完。而我們自己的 Editor 程式**完全沒碰過 `SerializedProperty`**（全專案只有一個 `CustomEditor`），所以丟例外的是別人——唯一有大量 string 序列化欄位＋自訂 drawer 的，是 Input System 的 `InputActionDrawer`。

放大器是 `CharacterPipelineRunnerEditor` 在 `OnInspectorGUI` **內部**每帧呼叫 `Repaint()`：在 GUI 遍歷中排程重繪屬重入寫法，而它猛打的正是同時掛著 InputAction drawer 的那個 Inspector 視窗。改用 Unity 為此提供的 `RequiresConstantRepaint()`，由 InspectorWindow 自行決定節奏。

**教訓**：`Repaint()` 寫在 `OnInspectorGUI` 結尾是網路上很常見的「即時 Inspector」寫法，它在只有自家欄位時沒事，一旦視窗裡混進第三方 property drawer 就會開始出現無法解釋的隨機錯誤。

### 7. 沒做的事（都是刻意的）

Pause Menu／Canvas／EventSystem／UI navigation、暫停時封鎖角色輸入、死亡 ArbiterSource、優先級／強制解封、把 `CursorModeController` 的來源一般化成介面集合。

（原本列在這裡的「Cursor service 抽象」**已於 §5 落地**——壓力在同一個工作階段就到了。）

### 8. 檔案與檢核

* 新增 `Assets/Scripts/App/GamePauseController.cs`（應用層首個住戶）
* 新增 `Assets/Scripts/App/CursorModeController.cs`（`Cursor` API 的唯一擁有者）
* 修改 `UiModeArbiterSource`（toggle → hold；**移除 Cursor 寫入**，改為公開 `IsUiModeActive` 回報意圖）
* 修改 `ThirdPersonCamera`（移除 `Start` 的初始游標鎖定＝移除第二個寫入者）
* 修改 `CharacterPipelineRunnerEditor`（`Repaint()` → `RequiresConstantRepaint()`）
* **新增測試 12 條**（`[Test]` 83 → **95**）
  * `GamePauseControllerTests` 6 條：暫停／還原**暫停前的** `timeScale`（非寫死 1）／重複要求暫停不得污染還原值／還原值為 0 時退回 1／切換交替／**停用時必須把時間還給遊戲**
  * `CursorModeControllerTests` 6 條：OR 合併／來源留空安全／**其中一個來源收手時另一個仍在要求則不得解除**（＝本輪 bug 的回歸測試）／全部收手才回到鎖定
* ⚠️ 兩個測試檔都會動到全域狀態（`Time.timeScale`），**`SetUp` 記錄、`TearDown` 無條件還原**——否則一條測試失敗會讓整個測試回合在 `timeScale = 0` 下跑
* `CursorModeControllerTests` 刻意**只測 `WantsFreeCursor` 不測 `Cursor` 本身**：游標是全域且與編輯器視窗焦點互動的狀態，EditMode 斷言它既不穩定、連還原都不可靠。套用行為屬人工驗收（§7.2-**M9**）
* ✅ **零 GC 複驗通過**（2026-07-27，§7.4.6）：本輪新增三處熱路徑——順序 4.5 的 `ArbiterPipeline.Tick`（索引 for、`Evaluate` 回傳 struct 值複製）、`GamePauseController.Update`、`CursorModeController.Update`——實測維持 **0 B**
* ✅ **一個原本擔心的缺口，有一半是結構保證的**：暫停中「移動不會排隊」不是運氣——連續型意圖每帧整體覆寫，而 B9 平滑吃 `deltaTime = 0` 推不動，`MoveSpeed` 恆為 0。⚠️ 但 **trigger 意圖（Jump／Roll）不同**：`FullBodyStateMachine.Tick` 沒有 deltaTime 閘門，`JumpState.CanEnter` ＝ `JumpRequested && IsGrounded` 亦與時間無關，**程式碼層面沒有任何東西阻止暫停中切進 `JumpState`**。待 M8 ⑤ 以 Inspector 的 `[Current State]` 確認（§7.3 已拆成兩半記錄）
* 📌 最後那條刻意用**反射直接呼叫 `OnDisable`**，而不是靠 `DestroyImmediate` 觸發：EditMode 下 Unity 是否派送生命週期訊息不在測試的掌控範圍內，依賴它會讓成敗取決於引擎行為而非我們的程式。**要驗的是那段防禦碼寫對了沒有，就直接驗它**
* ⚠️ 測試自身的紀律：`Time.timeScale` 是全域狀態，`SetUp` 建立確定性基準、`TearDown` 無條件還原——否則一條測試失敗會讓**整個測試回合**在 `timeScale = 0` 下跑
* 人工驗收見 §7.2-**M8**，其中 ④「暫停中能否再短按解除」是關鍵項：它驗證 Input System 的 Tap 判定用的是不受 `timeScale` 影響的真實時間。**若該條失敗，暫停將無法解除**，必須改用 `Time.unscaledTime` 自行計時

---

## [v0.25] - ArbiterPipeline 落地：Arbitration 第一次有寫入者（2026-07-27）

> 輪 4。`RuntimeData.Arbitration` 的三個 reader 早在 M2／M3 就寫好了（`CharacterPipelineRunner` 讀 `BlockInput`、`AudioController` 讀 `BlockAudio`、`FootIKController` 讀 `BlockIK`），但整整兩輪沒有任何 writer——旗標恆為 false，讀取契約先行、等一個真實需求。這一輪那個需求出現了：控制方案裡唯一還沒做的「**Alt ＝ 顯示滑鼠並停止移動**」。

### 1. 第一個真實需求，推翻了設計文件裡的一個假設

design-doc §2.5 原本畫的資料流是「狀態機 → Arbiter 依狀態轉譯 → 黑板」，因為當初想的封鎖情境全是死亡、被控制這類**確實是角色狀態**的東西。所以最自然的實作是在 `BaseState` 開一個 `BlocksInput` virtual，讓每個 State 自己宣告。

接上第一個需求才發現：**UI 模式根本不是角色狀態。** 玩家按 Alt 去點介面，角色本身什麼事都沒發生。硬要為它開一個 FSM 狀態＝為了實作方便去污染狀態機拓撲。

而且 `BaseState.BlocksInput` 還有兩個更深的問題：**方向錯了**（§2.5 說的是 Arbiter 讀 state，不是 state 宣告 arbiter，反過來就讓 FSM 認識了仲裁概念），以及**把一張表拆散**（「哪些狀態封鎖什麼」本質是一張表，散在 N 個 state 檔比放一個檔案難審）。

結論是把上游一般化成 `IArbiterSource` 集合——**狀態機是眾多可能來源之一**，不是唯一來源。這是 `IMovementIntentSource`（順序 2.5）／`IPresentationController`（順序 6.5）之後**第三次沿用同一個 pattern**：管線只認介面、新增實作零改動核心。

### 2. 一個小決定，省掉一條測試：回傳值 vs `ref`

最自然的介面寫法是 `void Contribute(data, ref ArbiterData flags)`——來源直接往共用的旗標上抬。但那樣每個來源都**看得見、也改得掉**別人已經抬起的旗標，「不得清掉別人的封鎖」就只能靠紀律或再寫一條回歸測試去守。

改成各自回傳自己的請求、由管線 OR 合併：

```csharp
public interface IArbiterSource
{
    ArbiterData Evaluate(PlayerRuntimeData data);   // 只回報「我自己」要什麼
}
```

這件事就變成**結構上不可能**。附帶好處是「多來源如何合併」有了唯一的家——未來真要做優先級／強制解封，改的是管線裡那一個迴圈，所有來源零改動。

**學到的**：能用型別讓錯誤寫不出來時，就不要用測試去守它。測試守的是「有人做錯了會變紅」，型別守的是「根本寫不出來」。

### 3. `BlockInput` 到底該凍結什麼——懸了兩輪的 M5 結案

Stage 1 遷移時，順序 2.5（`MovementIntent`）被刻意放在 `BlockInput` 閘門**之外**以維持 Migration 前行為，並明寫「留待 Arbiter 有 writer 時裁決」。現在有 writer 了。

直覺答案是「把 2.5 移進閘門」。**這是個陷阱**：

> `MovementIntent` 是**連續型**意圖，刻意不參與順序 7 復位（§1.5）。**跳過 producer ≠ 意圖歸零，而是意圖凍結在最後一帧。**

也就是說，如果封鎖那一瞬間你正按著 W 全速跑，黑板上 `DesiredSpeedNormalized` 會永遠停在 1.0，`LocomotionModel` 每帧照吃——**角色以全速無限前進，而且放不下來**。這比原本預想的「封鎖期間仍在滑行」嚴重一級。

第二個候選是「封鎖時把 `MovementIntent` 歸零」。也不行：那需要 `MovementIntent` 的**第二個寫入者**（Runner），直接違反 A5 單一寫入者；要嘛就得讓 producer 自己去讀封鎖旗標，那又破壞 ADR-003 D2 的 context-free。

最後採用的是**在閘門處把 `InputData` 整份歸零**：

```csharp
if (_runtimeData.Arbitration.BlockInput) inputData = default;
ProcessIntents(ref inputData);                                     // 順序 2
_movementIntentSource?.ProduceIntent(ref inputData, _runtimeData); // 順序 2.5
```

三個問題一起解決：單一寫入者不變、producer 完全不需要知道「封鎖」存在、手感自然落在既有的 B9 減速收步上（＝與放開 WASD 完全同款，零新增機制、不動 `IMovementModel` 介面）。

**副產物比修法本身更值錢**：`BlockInput` 從「兩套規則」（順序 2 跳過、順序 2.5 不跳過）收斂成**一個語意**——「本帧管線看不到任何輸入」。dev-spec §2.1 上那條解釋為什麼 2.5 在閘門外的 ⚠️ 註記，因此是整條刪掉而不是改寫。

順帶檢查到一個容易漏的副作用：零輸入時 `WalkButtonDown` 也是 false，所以 Ctrl 的 Walk toggle **不會**被誤翻，封鎖解除後型態原樣保留。這條已寫成測試。

### 4. 「顯示滑鼠」歸誰——以及差點只做一半的相機

游標切換依 ADR-003 §13.3 偏 Input／UI 職責，**Arbiter 不該認識滑鼠**。做法是讓 `UiModeArbiterSource` 獨佔三樣東西：UI 模式開關狀態、Left Alt 的 `InputAction`、`Cursor` API；上游 `ArbiterPipeline` 只收到一顆 bool。

**Alt 刻意不進 `InputData`**，理由比職責論更硬：`InputData` 是**可被 `BlockInput` 封鎖的通道**，而解除封鎖的那顆鍵不能住在可被封鎖的通道裡——放進去就得為它開一條「這顆不受封鎖影響」的例外，例外一開，剛剛才收斂好的「封鎖＝看不到輸入」語意馬上又破了。

另外，開場讀碼時抓到一個會讓需求**只做一半**的細節：`ThirdPersonCamera` 每帧**無條件**讀 `Mouse.current.delta`，而 `Cursor.lockState = Locked` 只在它自己的 `Start` 設定一次。只做「游標＋停止移動」的話，玩家一移動滑鼠去點 UI，鏡頭就跟著轉。

修法沒有引入任何新耦合——`Cursor.lockState` 本身就是「該不該吃滑鼠位移」的天然權威：

```csharp
if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
```

**但這條被明確記進 §7.3 張力表而不是當成通則**：它成立的前提是「全專案目前只有 UI Mode 一個滑鼠模式」。哪天出現 Pause／Inventory／Dialogue／Cutscene 等多個模式（它們對相機的期望未必一致），就要重新裁決是否需要一份更上游的 camera-input contract。**把成立前提寫下來，比把結論寫下來重要**——結論會過期，前提會告訴你什麼時候過期。

### 5. 刻意接受的一帧延遲

順序 4.5 卡在狀態機**之後**（脆弱點警告第 2 條），所以封鎖旗標第 N 帧寫入、第 N+1 帧的閘門才看得到。約 16ms。

**不為此把 4.5 提前**：提前並不能消除延遲，只會把「旗標晚一帧生效」換成「旗標依據**過期的**狀態計算」，後者更難除錯。這條連同「若未來真的出現無法容忍一帧的封鎖情境，正解是讓它走 FSM 狀態而非仲裁旗標」一起寫進 §2.1 脆弱點第 7 條。

實務上使用者也感覺不到：來源自身的即時反應（游標、相機）在**當帧**就完成，只有輸入封鎖晚一帧。

### 6. 沒做的事（都是刻意的）

* **優先級／強制解封**：多來源只做 OR。優先級需要真實競爭情境（死亡 vs 過場誰贏？）才能裁決語意，現在決定＝在沒有壓力測試下把介面定死。擴充成本已預先壓到最低（改管線一個迴圈）。
* **死亡的 ArbiterSource**：等第二個真實來源出現再加。屆時它會是一顆**讀 FSM 狀態**的 source，而不是回頭去給 `BaseState` 開 virtual。
* **抽象 Cursor service**：一個滑鼠模式不需要服務層。
* **重新鎖定游標時的鏡頭跳動抑制器**：Unity 的 `Mouse.delta` 在解鎖期間照常回報，重鎖那一帧理論上可能有小幅跳動。但觸發時手在按鍵、滑鼠通常靜止，預估風險低——列為人工觀察項（§7.2-M7），實測明顯再處理。

### 7. 檔案與檢核

**新增**：`Core/Arbitration/IArbiterSource.cs`、`Core/Arbitration/ArbiterPipeline.cs`、`Core/Arbitration/Sources/UiModeArbiterSource.cs`、`Tests/EditMode/ArbiterPipelineTests.cs`
**修改**：`CharacterPipelineRunner`（Start 收集 ＋ 順序 4.5 ＋ 閘門改零輸入）、`ThirdPersonCamera`（游標閘門）、`ArchitectureRegressionTests`（A4／A5 規則）

檢核表變動：
* **A5**：`Arbitration` 從「不得有任何執行期寫入者」→ `ArbiterPipeline.cs`。⚠️ 白名單只有**管線**一個檔案，多來源進場時**不會跟著變長**——這正是回傳值設計的副產物。
* **A4**：新增 `Core/Arbitration` ✗ `Project.Presentation`（落地 §4.5「只能透過黑板旗標與表現層溝通」）。刻意**不**禁 StateMachine——未來的死亡 source 需要讀它。
* **M5**：結案。
* **M7**：新增（Alt 行為的 Play 模式驗收；邊沿輸入與游標狀態無法在 EditMode 確定性重現）。

---

## 更早的版本（v0.1 ～ v0.24）

完整歷史已分卷至 **[`docs/changelog-archive.md`](changelog-archive.md)**（內容一字未改，版本／章節編號原樣保留）。

歸檔卷內的版本索引（便於定位，不必開檔即可判斷該不該讀）：

| 版本區間 | 主題 |
| --- | --- |
| v0.24 | 熱路徑每帧 40 B：介面型 `foreach` 導致 struct enumerator 裝箱（Profiler 實測抓出，A3 靜態掃描的能力邊界） |
| v0.23 | 切斷 Runtime → AnimationClip 的最後一條依賴（`BakedDuration` 烘焙期快照） |
| v0.22 | Walk 型態 hold／toggle 成為資產可配置項（mode state 進黑板） |
| v0.21 | ADR-003 Migration Stage 2：locomotion dynamics 歸位 `LocomotionModel`（§9-L1 收尾） |
| v0.20.1 | 文件結構優化：Context 讀取放大率治理（changelog 分卷制、`00-map.md` 建立） |
| v0.19 | Locomotion Foundation 收案 ＋ 里程碑檢查點（4-tier Mixer、Foot Phase 烘焙） |
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
