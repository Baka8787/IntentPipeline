# 17 — 外部 3C Demo Benchmark：Observability 與環境互動問題域

> **性質**：研究／對照筆記。**不是** ADR、**不是** roadmap 修訂、**不是**實作規格。
> 它只做三件事：**盤點影片**、**對照 IntentPipeline 磁碟現況**、**把「攀爬翻越」拆成問題域**。
> 建立日期：2026-09-10。對照的程式基準：`71f5a5c`（同日 commit，見 §7-F1)。
>
> 🔄 **2026-09-10 修訂**：已套用 `docs/18-benchmark-convergence.md` §7 的 **S1–S7** 全部七條更正。
> 其中兩條是**推理錯誤的主動更正**，不是文字潤飾——閱讀時請以修訂後版本為準：
> - **S2**（§4.1-L5）：「traversal 是 jump 的特化」**已撤回**。入口回退鏈成立，執行模型從屬**不成立**。
> - **S4**（§4.3-1）：「環境查詢會是第一份世界資料」**是事實錯誤**，`CombatContext.TargetPosition` 早已是先例。
>
> 逐條對照見 `docs/18` §7。修訂**未動 roadmap**（`docs/03` 不受本文影響）。
>
> 🎯 **2026-09-10 Observability 範圍收斂**：下方 §3 的保留為 benchmark 盤點，不代表實作清單。
> 目前 world-space debug presentation **只保留 traversal 與 Foot IK 的實際世界幾何採樣**；
> Direction Authority 只留 Editor-only snapshot／測試，不再畫四箭頭、history 或 HUD。
> Game View 主通道必須使用 runtime-visible renderer，不依賴 Gizmos；Scene View 才以 Gizmos 詳查。
> 目前 production code **連角色前方 obstacle query 本身都不存在**，不只是缺 candidate owner；
> Foot IK runtime lines 已落地。完整盤點與契約見 `docs/18` §1.1～§1.2。

---

## 0. 證據等級聲明（先讀這段，否則後面的話會被高估）

**對象**：`蓝炉-Unity 3C 控制器 Demo【动画TA】`（B 站，作者頻道名在畫面上是「知北游petrichor」），
本地錄影檔 `2026-09-10 15-23-21.mp4`，1280×720 / 30fps / **415 秒**。

**我實際取用的**：
- 以 **5 秒間隔**抽出的 83 張縮圖（6 張 contact sheet）——用來建立時間軸與章節結構。
- **20 張全解析度取樣影格**（裁掉瀏覽器邊框）——用來讀 debug 幾何與字幕。

**三個必須先講清楚的限制**：

| # | 限制 | 對結論的影響 |
|---|---|---|
| **E-1** | **畫面右側被裁掉。** 錄影視窗比擷取區寬，右上角黃底字幕**每行只看得到前 8–12 個字** | 本文引用的所有字幕都是**殘句**，以「…」標記。**未補完的部分我不當事實使用**，也不猜後半句 |
| **E-2** | **我沒有音訊轉錄能力** | 影片若有旁白，本文完全沒有取用。任何「作者說…」的敘述都不存在於本文 |
| **E-3** | **我只看到畫面，沒有專案** | ⛔ 本文**不敘述**對方的架構、分層、測試品質、GC 特性、實作方式。§8 明列所有「無法判斷」項 |

---

## 1. 影片能力盤點（依時間軸）

時間為 5 秒取樣推得的**近似值**。全片 415 秒（6:55）。

> ⚠️ **本表時間欄已於 2026-09-10 全面更正**（`docs/18` §7-S1）。舊版時間欄整體偏移，
> 把 debug 預覽段寫成 10 秒的小節。更正後的比重才是這支影片真正的訊息：
> **debug 預覽段（2:05–4:00，115s）約佔全片 28%；traversal 全段（1:40–4:15，155s）約 37%。**
> ⇒ 作者把**超過三分之一**的展示時間投在「環境檢測 ＋ 讓它可被看見」上。這不是附帶功能，是主軸。

| 時間（約） | 章節標題 | 展示內容 | 畫面字幕（**殘句**） |
|---|---|---|---|
| 0:00–0:35 | **基础LOCOMOTION** | Idle、360° 行走、奔跑、行走停步、奔跑停步 | 「为ACT/ARPG中角色**非入战状态**设计／轴向移动，快速响应／高角色性能」「IDLE与360°行走、奔跑…」「停步动作：行走停步、奔跑停…」 |
| 0:35–0:50 | 跳躍／落地 | 站立跳、助跑跳；落地分 輕／重／翻滾緩衝 | 「空中姿态由**VERTICALVELOCITY**参数…／通过**2D BLEND TREE**…／通过**FEETTWEEN**参数随机…与调整站立跳/助跑…」 |
| 0:50–1:35 | **脚步IK多地形适配** | 斜坡、樓梯／台階、起伏地形 | 「**脚跟、脚心**分别发出射线…／提前采样脚部落…／根据确定的脚部位置重…」「楼梯部分使用**分LAYER**…／分别解决平滑移动与…」 |
| 1:40–2:00 | **TRAVERSAL 動作**（环境检测与攀爬翻越系统） | 各種高度／厚度障礙的翻越與攀爬**成品演出** | 「在翻越前控制角…／使用 **MATCHTARGET** 訂製…／在 **STATEMACHINEBEHAV**…／使动画精准…」 |
| **2:05–4:00** | ⭐ **障碍物检测系统 · 可开启调试预览模式** | ⭐ **本片 observability 的核心，也是全片最長的一段（115s ≈ 28%）**（見 §2） | 「由下向上的 **WHIL**…／任一条件未满足…／**全部检测失败则返回跳跃**」 |
| ↳ 同段內 | ↳ 失敗原因展示 | 距離、上方空間、底部形狀 | 「**距离过远✕**」「障碍物上方空…超出最大攀…」「障碍底部不规则…**单杠地形**…」 |
| ↳ 同段內 | ↳ 厚度分流與側向避讓 | 厚度足夠→攀爬；薄→翻越；左右各自阻擋 | 「**厚度足够：攀爬 / 翻**…／通过**左右两侧射线检**…／自动避…」「左侧有阻挡」「右侧有阻挡」「两侧均有阻挡」 |
| ↳ 同段內 | ↳ 移速影響 | 不同移速走不同翻越 | 「不同移速影响…」 |
| 4:00–4:15 | **运动碰撞体处理** | 翻越過程的碰撞體調整 | 「**运动碰撞体处理／减小穿模**」 |
| 4:15–4:55 | **角色音效系统** | 腳步音效隨地面材質切換 | 「对不同地面…／不同脚步声、急停摩擦…／通过**动画事**…／并添加随机音…」 |
| 5:00–5:30 | **战斗动画** | 普攻 4 段、戰鬥 IDLE、拔刀 | 「设计目的：在**非入战情况下**，保留攻击输入的动画反馈，同时**不把整个战斗系统开启**」「从简处理拔刀…通过**SHADER溶解**…／移动会打断非入战…／战斗IDLE保持3S…」 |
| 5:30–5:55 | **BUFF响应** | 地面 trigger 區（`dance club`／`fear area`）觸發疊加動畫層 | 「与BUFF或角色设计相配合（环境、性格/能力）」「**TRIGGER**触发…／使用 **ADDITIVE LAYER**…」；畫面可見 Animator Layers：`Base Layer`／`Dance Buff L…`／`Fear Buff Lay…` |
| 5:55–6:35 | **相机控制** | 遮擋拉近、相機切換系統（OTS／窄巷） | 「通过 **CINEMACHINE** 实…／受阻挡时拉…」「相机切换系统」 |
| 6:40–6:55 | 二級動畫 | 布料／頭髮 | 「**物理模拟**实现的二级动画（布料、头发较多角色效果好）」 |

> ⛔ **「↳ 同段內」是誠實標記，不是遺漏**：5 秒取樣的解析度不足以把 2:05–4:00 這段內部再細分，
> 這三個子項確定發生在該段內，但**不推測各自的起訖秒數**（§0 證據等級）。

**場景本身也是說明書**：地面貼著 world-space 文字標籤標示測試區——
`Ramp - foot IK`／`Stairs - foot IK`／`Terrain - foot IK`／`Falling feedback`／`ground SFX switch`／
`Climbing & environment detecting`／`Traversal anim`／`anim buff - layer override`／`fear area`／`dance club`／`Camera switch - OTS`。
📌 **這本身是一個值得學的做法**：測試場景把「這一區在驗什麼」寫在地上，
外行看得懂、自己三個月後回來也看得懂。成本近乎零。

---

## 2. ⭐ 釐清：影片的「debug 顯示」到底是什麼

這是本輪最重要的一次釐清，因為它直接改變 §3 的成本估算。

畫面上有**兩種完全不同的東西**被視覺上混在一起：

| | 內容 | 是不是 runtime 能力 |
|---|---|---|
| **(a) 引擎內 world-space debug 幾何** | 紅色**水平射線陣列**（由下而上堆疊、逐層向前）／綠色線框球（候選點、頭頂淨空探測）／綠色線框膠囊（角色目前 capsule）／**白色線框膠囊**（翻越後的預測落點）／地面綠圈與白圈／每肢彩色小膠囊（運動碰撞體）／向左右延伸的綠色側向探測線 | ✅ **是**。畫在 Game view（畫面右上可見 Play Maximized 旁的 Gizmos 開關），走 Unity 內建 gizmo／`Debug.Draw*` 管道 |
| **(b) 後製字幕** | 右上角黃底黑字說明、以及「**距离过远✕**」「**左侧有阻挡**」「**右侧有阻挡**」「**两侧均有阻挡**」 | ❌ **不是**。這些是**影片剪輯加上去的**，位置固定在畫面座標、字體與章節標題同一套 |

**因此有兩條結論，方向相反，都必須說**：

1. ✅ **「能看見系統內部判斷」在這支影片裡幾乎全部是靠幾何達成的，不是靠文字。**
   這對我們是好消息——**幾何 debug 的架構成本最低**（Editor-only、只讀、不需要 UI 框架、不需要新黑板欄位）。

2. ⚠️ **「為什麼失敗」這件事，在影片裡是「人講的」，不是「系統畫出來的」。**
   我在 20 張取樣影格中**沒有看到任何 runtime 文字 HUD**——沒有狀態名、沒有數值面板、沒有 transition trace。
   所以**不要把「對方有一套 debug HUD 系統」當成已知事實**。已知的只有：對方把**查詢的形狀與結果**畫了出來。

> 🔑 **這條釐清的價值**：如果照著「影片看起來的樣子」去做，會做成一套字幕式 HUD——
> 那既不是影片實際有的東西，也不是最有用的東西。**最有用的是幾何。**

---

## 3. A — Observability 設計

### 3.1 概念：好的 debug draw 在回答什麼

影片的 traversal 可視化之所以有用，**不是因為線多**，而是因為它在**同一張畫面上同時給出四層資訊**：

| 畫的東西 | 回答的問題 |
|---|---|
| 紅色射線陣列的**形狀**（掃了幾層、每層多遠） | **查詢是怎麼問的**——迴圈的取樣密度、範圍、方向 |
| 射線**畫到哪就停**（有些層根本沒畫出來） | **查詢走到哪一步就結束了**——這是控制流的可視化 |
| 命中點的**綠球** | **找到了什麼候選** |
| **白色膠囊**（預測的落點姿勢） | **如果採用這個候選，角色會被放到哪**——即「未來狀態」 |

⇒ **判準：一個好的 debug draw，讓你不用讀程式就能回答「它為什麼選了這個 / 為什麼沒選」。**

📌 **本專案已經寫下過這條原則**，只是它躺在一個 `.cs` 的 XML 註解裡
（`Assets/Scripts/Core/Movement/AIMovementSource.cs`，`OnDrawGizmosSelected` 上方）：

> 「目的不是漂亮的 debug UI，而是回答一個具體問題：敵人行為怪的時候，
> **問題在 decision、movement execution 還是 action pipeline？**
> …⚠️ 全段包在 `#if UNITY_EDITOR` 內（A2 守），且**只讀不寫**——gizmo 不得成為第二個決策來源。」

**這段話就是本節要的概念，本專案自己已經有了。** 差距不在觀念，在**覆蓋率**（見 §3.2）。
（這段原則目前沒有被任何 Living Doc 索引到 ⇒ 已列為 §7-F2。）

### 3.2 IntentPipeline 現況盤點（磁碟核對 2026-09-10）

| 位置 | 通道 | 畫／顯示什麼 | 限制 |
|---|---|---|---|
| `Editor/Pipeline/CharacterPipelineRunnerEditor.cs` | **Inspector**（`Project.Editor` asmdef） | 「黑板數據流即時監視 v0.4」：`CurrentState`／原始輸入快照（Move/Look/各按鍵）／`MovementIntent`（DesiredSpeed、DesiredDirection、WalkModeActive）／Movement Output（MoveDirection／MoveSpeed）／`IsGrounded`／`CurrentWeapon`／`Arbitration` 四個 block 旗標 | 只在**選中角色**時可見；**只有數字沒有幾何**（方向是 `Vector3.ToString()`）；**沒有時間軸** |
| `Core/Movement/AIMovementSource.cs` | **Scene gizmo**（`OnDrawGizmosSelected`，`#if UNITY_EDITOR`） | 接戰內／外圈、到目標的方向線、strafe 方向線、模式文字標籤、`target = <none>` 的紅字警告 | **僅選中時**；**Scene view**（Play 時通常看的是 Game view） |
| `Core/Pipeline/AIInputSource.cs` | 同上 | 攻擊距離圈、是否想出手、`target = <none>` 警告 | 同上 |
| `Presentation/Actions/GroundEffectSink.cs` | 同上 | AoE 效果半徑線框球 | 同上 |
| `Core/Pipeline/CharacterPipelineRunner.cs` | **debug 快照 API** | `public InputDebugSnapshot InputDebug` —— 把當幀原始輸入以**只讀屬性**曝露給 Editor | ⭐ 這是**已經存在的正確 pattern**，見 §3.3 紅線 2 |

**結論**：本專案的 observability **不是「沒有」，而是「只有兩個子系統有，而且都要先選中物件、都在 Scene view」**。
與影片的差距**主要是覆蓋率與可及性，不是能力**。

### 3.3 四個顯示通道與選擇判準

| 通道 | 適合什麼資訊 | 判準（一句話） |
|---|---|---|
| **World-space Gizmo / Debug Draw** | 有**空間語意**的量：方向、位置、半徑、法線、探測形狀、預測落點 | 「畫成箭頭／球，是不是比寫成數字更容易看懂？」 |
| **Runtime Debug HUD**（Game view 文字） | **無空間語意但要與操作同步觀察**的量：狀態名、gait、phase、布林旗標 | 「我需要在**操作角色的同時**盯著它嗎？」 |
| **State / transition trace**（時間軸／捲動列表） | **事件與順序**：誰中斷了誰、第幾幀切的、選了哪支變體、為什麼退化 | 「我要問的是『現在是什麼』還是『剛剛發生了什麼』？」後者才需要 trace |
| **Inspector / Log** | 一次性、低頻、需要精確數值 | 「我會連續盯著它看 10 秒嗎？」若否，Inspector 就夠了 |

#### 三條紅線

1. **debug 讀取不得成為第二個決策來源。**
   已寫在 `AIMovementSource` 註解裡，沿用。gizmo／HUD 一律**只讀**，且不得把值寫回任何 runtime 路徑。

2. ⛔ **不得為了顯示而新增黑板欄位。**
   黑板欄位有 Owner／Writer／Readers 的治理成本（dev-spec §1.1 權限表 ＋ `ArchitectureRegressionTests.WriterRules` 必須同步改）。
   只為 debug 加欄位＝**用永久的架構成本換一個臨時的視窗**。
   ✅ **正解已經存在**：`CharacterPipelineRunner.InputDebugSnapshot`——
   **debug 快照是「元件的公開唯讀屬性」，不是黑板成員**。要擴充就擴充這個 pattern。

   > 🆕 **2026-09-15：這條紅線第一次被真實需求測試，而且守住了。**
   > 玩家 HUD 要畫技能冷卻，而冷卻住在 `ActionState` 內部（ADR-004 D2）、**不在黑板上**。
   > 最順手的做法就是加一個 `PlayerRuntimeData.Cooldowns[]`——正是本條禁止的東西。
   > ✅ 實際採用：`CharacterPipelineRunner.GetActionCooldownNormalized(slot)`
   > ——**本條所說 pattern 的第二個使用者**（Runner → FSM → `ActionState` 的唯讀轉送，不快取、不判斷）。
   > 📌 **換算也留在擁有者那一側**：`CooldownVariance` 讓每次冷卻長度不同，
   > HUD 若拿 authored 的 `Cooldown` 當分母，進度條會在變異非零時失準
   > ⇒ 由 `ActionState` 回答 0–1，HUD 只消費結果（`ActionStateTests.T27` 釘住）。
   > ⚠️ 對照第 1 條：這個查詢**只回答「畫多滿」**，⛔ 不得用來決定要不要出手。

3. **runtime HUD 若要顯示 `StateType`，它不能住在 `Presentation/`。**
   `LayerRules` 明禁 `Presentation` → `Project.Core.StateMachine`／`StateType`。詳見 §3.5。

   > 🆕 **2026-09-15 實證**：`PlayerHud` 需要 `CharacterPipelineRunner` 才問得到冷卻，
   > 而 `LayerRules` 同樣禁 `Presentation` → `Project.Core.Pipeline`
   > ⇒ HUD 結構上就住進了 **`App/`**（與 `GamePauseController`／`CursorModeController`／`RespawnController` 同層）。
   > **這條紅線的預測是對的，只是觸發它的不是 `StateType` 而是 `Pipeline`。**
   > 📌 同一輪順手把 `UnityEngine.UI` 加進 `Core` 與 `Presentation` 的 `Forbidden`：
   > `Project.Runtime.asmdef` 為了 HUD 新增的 UI 參考是**組件層級**的，
   > 不關門的話整個 Runtime 從此都編得過 `Image`／`Canvas`。**加參考的同一刻就要關門。**

### 3.4 逐項資訊清單

欄位說明：
- **debug 什麼問題**＝這個顯示存在的理由（不是「它是什麼」，是「它替你回答什麼」）
- **資料層**＝現在能從哪裡取得
- **邊界影響**＝顯示它會不會撞到 `LayerRules`／`WriterRules`

| # | 資訊 | 它在 debug 什麼問題 | 資料現在在哪一層 | 建議通道 | 邊界影響 |
|---|---|---|---|---|---|
| 1 | **Move Intent**（`DesiredSpeedNormalized` ＋ `DesiredDirection`） | 「角色不動 / 走錯方向」是**意圖錯**還是**執行錯**？這是分岔點 | 黑板 `MovementIntent`（producer 唯一寫入） | **Gizmo 箭頭**（長度＝強度）＋ 現有 Inspector | ✅ 無。任何能讀黑板的層都可畫 |
| 2 | **Velocity / MoveSpeed**（Movement Output） | 平滑器是否還在收步？放開輸入後的滑行是設計還是 bug？ | 黑板 `MoveSpeed`／`MoveDirection`（`LocomotionModel` 唯一寫入） | **Gizmo 箭頭**（與 #1 疊在一起看差值最有用）＋ Inspector | ✅ 無 |
| 3 | **Facing Direction** | 朝向與移動方向是否分家 | `transform.rotation`（`MotionDriver` 唯一寫入者） | **Gizmo 箭頭** | ✅ 無 |
| 4 | ⭐ **Direction Authority 三箭頭**（move／facing／aim 同框） | **ADR-007 的全部問題就是「三個概念共用兩個載體」**。三支箭同框＝該 ADR 的問題陳述與驗收條件的**視覺形式** | move＝黑板；facing＝`transform.forward`；aim＝`IAimSource.TryGetAimPoint` | **Gizmo，三色三箭** | ⚠️ 讀 `IAimSource` 需在能引用 `Core.Actions` 的層 → Editor-only 元件無成本 |
| 5 | **Gait / WalkModeActive** | 速度階層選錯？toggle 卡住？ | 黑板 `MovementIntent.WalkModeActive` ＋ `GaitProfileSO` | HUD 文字 or Inspector（**無空間語意**） | ✅ 無 |
| 6 | **Movement State**（`StateType`） | 現在到底在哪個狀態 | `FullBodyStateMachine`（`CharacterPipelineRunner.CurrentState`） | HUD 文字；**Editor-only 時零成本** | ⚠️ **`Presentation/` 不得讀 `StateType`**（LayerRules）。§3.5 |
| 7 | **Action slot / phase / cooldown** | 連段為什麼沒接上？冷卻是不是連坐了？ | `ActionState`（`Core.StateMachine.States`）＋ `ActionSlot`（`Core.Actions`） | HUD 文字 ＋ **trace** | 同 #6 |
| 8 | **MoveX / MoveZ** | 2D mixer 的取樣點對不對（8 向 strafe 的前提） | `LocomotionModel` 算出後 `SetFloat` 給 Facade，**沒有留在黑板** | **Gizmo 2D 十字／點**＋ HUD 數值 | ⚠️ 目前只在 `LocomotionModel` 內部。⛔ **不要為了顯示把它加進黑板**（紅線 2）——用 debug 快照屬性 |
| 9 | **Ground Probe / IsGrounded** | 落地判定抖動？走出高台的 grace 有沒有誤觸發？ | 黑板 `IsGrounded`（`MotionDriver` 唯一寫入，源頭是 `CharacterController.isGrounded`） | **Gizmo 腳下色塊**＋ trace（狀態變化的**時間點**才是關鍵） | ✅ 無 |
| 10 | 🟢 **Ground Normal** | 斜坡採樣與踝角 clamp 是否一致 | 不存在於契約層；Foot IK 內部 `sample.Normal`／`SoleNormal`，只供 IK 與 debug snapshot | ✅ Game View runtime lines：黃色 raw normal／綠色 clamped normal；Scene View 另有 Gizmo，仍不發布 | 只有 IK 一個 gameplay consumer，維持局部 implementation detail；⛔ 不進黑板 |
| 11 | **Jump / Falling / Landing phase** | 落地分類（normal／hard）為什麼選這個？stage 推進到哪？ | `JumpState` ＋ `JumpStateParams`；`VerticalVelocity` 在黑板 | HUD 文字 ＋ **trace**（`VerticalVelocity` 曲線用 gizmo 沒意義） | 同 #6 |
| 12 | 🟢 **Toe / Heel probe** | 腳為什麼戳穿／浮空？是 ankle ray 還是端點殘差？ | `FootIKController` 內部真實 query 結果 → Editor／Development-only private snapshot | ✅ Game View runtime lines ＋ Scene View Gizmo：rays、hit／miss、residual lift；兩者皆不重發 query（A34） | 零 public API、零黑板欄位 |
| 13 | 🟢 **Foot IK goal** | Rig 實際被要求擺到哪、修正方向與權重是多少 | `FootIKTargetData`＋Controller 內部 snapshot | ✅ pre-IK goal → target、sole pose；透明度編碼 position weight | 零跨層 |
| 14 | **Animation state / transition** | 播了哪支 clip？`TryGetTransition` 查表失敗了嗎？ | `AnimancerFacade`（查表失敗目前只有 `Debug.LogWarning`） | ⭐ **trace 最有價值**——W13 那輪的整個診斷都是在補這件事 | ⚠️ Facade 在 `Presentation.Animation`；Editor-only 讀取無成本 |
| 15 | 🔴 **角色前方 Environment query** | — | **完全不存在**（既有 physics query 只服務相機遮擋、鏡頭瞄準、Foot IK、`GroundEffectSink` 探地／附近碰撞、`PlayerCombatContextSource` 戰鬥目標 overlap） | — | **缺的是前方 obstacle query 本身，不只是 candidate owner**。見 `docs/18` §1.1、本文 §4 |
| 16 | **Arbitration blocks** | 輸入為什麼沒反應？IK 為什麼關了？ | 黑板 `Arbitration`（`ArbiterPipeline` 唯一寫入） | HUD 布林燈號（已有 Inspector） | ✅ 無 |
| 17 | **Combat Context**（`InCombat`／`HasTarget`／`TargetPosition`） | facing 來源切換的前提條件成不成立（ADR-007 S3） | 黑板 `CombatContext`（`PlayerCombatContextSource` 唯一寫入） | **Gizmo：到 target 的線 ＋ 進出戰鬥語境的半徑** | ✅ 無 |
| 18 | **Temporary effects（Slow）** | 速度變慢是 buff 還是 bug | `Core/Effects/TemporaryGameplayEffectState` | HUD 文字 | ⚠️ 目前 `AIMovementSource` 回讀它＝**已登記的架構債**（`LayerRules` 註解＋dev-spec §7.3）。**debug 顯示不得成為留著這條債的理由** |
| 19 | ⭐ **Locomotion Stop 選片** | 收步為什麼選這支變體／為什麼**靜默退化**成不播 | `LocomotionStopSelector.SelectByEntryPhase`（entry phase → 變體索引，回 −1 ＝退化） | **trace ＋ HUD**：entry phase 數值、選到的索引、`_landDuration` | ✅ 無（`Core.Movement.Models`） |
| 20 | **Footstep events** | 腳步聲節奏與實際步伐對不對得上 | 黑板 `PresentationEvents.LeftFootPlanted`／`RightFootPlanted`（`PresentationPipeline` 唯一寫入） | **Gizmo：落地瞬間在地面留一個短命標記**（比聲音更容易判斷時機） | ✅ 無 |

**分流小結**：
- **World-space gizmo**（空間語意）：#1 #2 #3 **#4** #9 #10 #12 #13 #17 #20
- **Runtime HUD**（無空間語意、需同步觀察）：#5 #6 #7 #11 #16 #18
- **Trace**（順序與事件）：**#14** **#19** ＋ #7 #9 #11
- **Inspector 就夠**：#8 的精確數值、#5、以及所有一次性檢查

### 3.5 唯一真正的架構問題：runtime HUD 該住哪

**先講一個容易被忽略的事實：Editor-only 的 observability 幾乎沒有 layer 成本。**
`Assets/Scripts/Editor/` 是 `Project.Editor` asmdef，不進 build、可引用所有層；
runtime 檔案內的 `OnDrawGizmos*` 只要包在 `#if UNITY_EDITOR` 內就由 A2 守住（既有做法）。

**有成本的只有「要在 Player build 裡看得到的 runtime HUD」**：

| 放哪 | 問題 |
|---|---|
| `Presentation/` | ⛔ 不得讀 `Project.Core.StateMachine`／`StateType`／`InputData`（LayerRules）⇒ **顯示不了狀態名與原始輸入** |
| `Core/` | ⛔ 不得碰 `Animancer`／`Animator`（LayerRules）⇒ 顯示不了動畫實況 |
| 新的頂層 `Debug/` 資料夾（自己的 asmdef） | ✅ 概念上正確：**單向依賴所有層，任何層都不得依賴它**，再加一條 `LayerRule` 守住「沒有人 import Debug」 |

⚠️ **但這是一句提醒，不是提案。**
判準是「**你現在需要在 Player build 裡看它嗎？**」——目前所有除錯都發生在 Editor Play，
**Editor-only 通道就夠**。依 CLAUDE.md「第二個使用者出現前不得建立 production abstraction」，
`Debug/` asmdef 現在不該開。等到真的有「build 裡才復現的問題」再說。

### 3.6 當時的最小起手建議（⚠️ 已由 `docs/18` §1 的最新裁決取代）

以下保留當時的研究推論；**不是目前實作清單**。最新範圍只保留 traversal／Foot IK 世界幾何，
Direction Authority presentation 與 Stop trace 均已撤回／暫緩：

1. ⭐ **Direction Authority 三箭頭**（`OnDrawGizmos`，**非** `Selected`）
   **理由最強**：ADR-007 §8 的驗收條件 **A／B／C／D 四條，全部是「兩個方向是否一致」**。
   目前要驗只能靠肉眼比對角色姿勢與火球飛行方向——
   **這正是應該被畫出來、而不是被目測的東西。** 而且 ADR-007 現在是 **Trial（未驗證）**，
   這個 gizmo 直接降低它的驗收成本。

2. **Foot IK 探測可視化**（heel／toe ray、ankle ray、命中點、clamp 前後 normal、`PelvisOffsetY`）
   `docs/05` §3.5.5 的 L1 約束模型與升級階梯全部建立在這些量上，**現在只能靠讀程式想像**。
   影片的腳步 IK 章節之所以說服力強，就是因為它讓人看見探測而不是只看見結果。

3. **Locomotion Stop 選片 trace**（entry phase → 選到的變體 → `_landDuration`）
   W13 那一輪的教訓是「**選片失敗會靜默退化**」（`SelectByEntryPhase` 回 −1 ⇒ `default` ⇒ 立即退場）。
   把它畫出來／印成 trace，靜默就不再靜默。

> **本輪不要求做漂亮 UI。** 線條密度、顏色、畫面配置、可讀性一律**保留給人工 review**——
> ⛔ **不得把「有顯示」當成「視覺設計完成」。**

---

## 4. B — 環境互動 / 攀爬翻越的問題域

### 4.0 先講結論（比功能清單重要）

影片從 2:35 開始展現的，**不是「多了一個 Climb 狀態」，是多了一整個問題域：**

> **角色必須對「還沒發生的移動」做出承諾。**

目前 IntentPipeline 的**所有**移動決策都是**當幀的**：
`intent（當幀）→ model dynamics（當幀推進）→ MotionDriver（當幀 Move）`。
**沒有任何一處需要回答「如果我做這個動作，1.2 秒後我會站在哪裡」。**

唯二的例外是 `RollState`（烘焙曲線位移）與 Jump 的 stages——
它們是**有始有終的 authoritative 位移**，但**目標是自己算出來的，不是從世界查來的**。
Traversal 的差別在於：**目標來自環境**，而且**採不採用是一個決策**。

**這才是真正的差距。** 功能名稱（Climb／Vault）只是這個差距的外觀。

### 4.1 八層拆解

#### L1 — Player Intent（玩家意圖）
| | |
|---|---|
| **解決什麼** | 「玩家現在是不是想跨過去」——把一顆按鍵映射到**可能是多種動作**的請求 |
| **輸入** | 按鍵（通常沿用跳躍鍵）、當前移動意圖方向與強度 |
| **輸出** | 一個**未定型**的請求：「我想往前突破」 |
| **Unity 概念** | Input System action、意圖與動作的解耦 |
| **IntentPipeline 承接位置** | ✅ **已有且形狀正確**：`IntentData.JumpRequested`／`RequestedActionSlot`，管線順序 2 翻譯 |
| **缺什麼** | 觀念上缺一步：目前 `JumpRequested` **直接就是** Jump。traversal 之後它會變成「**候選解析的觸發**」 |
| **要先懂** | 「意圖 ≠ 動作」在**有多個候選**時的差別（本專案在 Action slot 上已經走過一次，可類比） |

#### L2 — Environment Query（環境查詢）
| | |
|---|---|
| **解決什麼** | 「我前面有什麼」——把幾何變成**結構化事實** |
| **輸入** | 角色位置、朝向／移動方向、capsule 尺寸、LayerMask、速度 |
| **輸出** | 命中點、法線、高度、深度／厚度、頂面位置、側向淨空 |
| **Unity 概念** | `Physics.Raycast`／`SphereCast`／`CapsuleCast`／`BoxCast`／`OverlapCapsule`；`QueryTriggerInteraction`；`*NonAlloc`（零 GC 必要）；LayerMask 分層設計 |
| **IntentPipeline 承接位置** | ⚠️ **沒有這一層**。既有 physics query 全部是**點狀、單一用途**：相機遮擋 SphereCast、鏡頭瞄準 Raycast、Foot IK 腳下射線、`GroundEffectSink` 探地／附近碰撞、`PlayerCombatContextSource` 戰鬥目標 overlap。**沒有任何「描述角色前方世界」的元件** |
| **缺什麼** | 一個 query 元件、一份查詢結果的資料形狀、以及**「這份資料屬於誰」的決定** |
| **要先懂** | 各種 cast 的**幾何差異與成本**；為什麼 traversal 通常用**由下而上的階梯掃描**而不是單發射線（影片字幕「由下向上的 WHIL…」正是這個）；`*NonAlloc` 與預配置緩衝 |

#### L3 — Environment / Obstacle Classification（分類）
| | |
|---|---|
| **解決什麼** | 「那是什麼」——把幾何事實變成**類別**（矮牆／可攀高台／薄欄杆／不可通過） |
| **輸入** | L2 的量測值 ＋ 角色能力參數（最大攀爬高度、最小落腳深度、capsule 半徑） |
| **輸出** | 類別 ＋ **失敗原因** |
| **Unity 概念** | 純資料判斷（不需要 Unity API）；能力參數應是**資產**（ScriptableObject）而非常數 |
| **IntentPipeline 承接位置** | ⚠️ 無，但 **pattern 已存在**：`LocomotionStopSelector.SelectByEntryPhase`（把連續量→離散選擇，且**明確定義退化值 −1**）就是同一類函式，且已是**純函式、可 EditMode 測**的形狀 |
| **缺什麼** | 分類維度本身。影片字幕給了五個維度（**這是影片對我們資訊量最高的部分**）：<br>① **距離**（「距离过远✕」）② **上方淨空**（「障碍物上方空…超出最大攀…」）③ **底部形狀**（「障碍底部不规则…单杠地形…」）④ **厚度** → 決定**攀爬 vs 翻越**（「厚度足够：攀爬 / 翻…」）⑤ **左右阻擋**（「通过左右两侧射线检…」「两侧均有阻挡」） |
| **要先懂** | ⚠️ **這裡有一個本專案踩過的陷阱**：`snap-to-default` 缺陷類型——「不連續選擇餵給連續量」。分類是離散的，但輸入（高度、厚度）是連續的；**邊界附近的抖動會讓角色在兩個類別間跳動**。Foot IK 的 heel/toe `argmax` 跳變是同一類問題 |

#### L4 — Affordance / Candidate Action（可供性／候選動作）
| | |
|---|---|
| **解決什麼** | 「這個類別**允許**我做什麼」——類別 → 一組候選動作，每個候選帶著**它自己的參數**（起跳點、對齊目標、時長） |
| **輸入** | L3 的類別 ＋ 角色當前速度／狀態 |
| **輸出** | 候選清單（可能為空）。**空 ＝ 不是錯誤，是有效答案** |
| **Unity 概念** | 資料驅動的動作定義（ScriptableObject）、動畫與參數綁在一起 |
| **IntentPipeline 承接位置** | ✅ **形狀已經有**：`ActionDefinitionSO` ＋ `ActionSlot` ＋ `ActionState`（ADR-004／005，**已 Accepted**）。「一份資產 ＝ 一個動作，加動作不需改 runtime」這條已被 ADR-005 Acceptance **B** 實證 |
| **缺什麼** | 候選的**來源**不同：現有 Action 由**按鍵**選出，traversal 的候選由**環境**選出。這是「誰決定要做什麼」的來源變更 |
| **要先懂** | affordance 的原意（環境對行動者提供的行動可能性）；「候選有參數」與「候選只是一個 enum」的差別 |

#### L5 — Action Selection / Arbitration（選擇與仲裁）
| | |
|---|---|
| **解決什麼** | 「候選有好幾個 / 一個都沒有時怎麼辦」 |
| **輸入** | 候選清單 ＋ 當前狀態 ＋ 是否可中斷 |
| **輸出** | 選中的一個，**或明確的 fallback** |
| **Unity 概念** | 優先序 vs 評分；**fallback 鏈** |
| **IntentPipeline 承接位置** | ✅ **FSM 的 `CanEnter` / `EvaluateInterrupts` 就是這一層**，而且**已經有過完全同構的先例**：<br>`JumpState.CanEnter` 現在有**兩條入口**（主動 Jump／非主動失地），各自帶不同的起始 phase 與 launch 注入規則。<br>traversal 只是**第三條入口**——結構上不是新東西 |
| **缺什麼** | ⭐ **影片給了最關鍵的一句**：「**全部检测失败则返回跳跃**」。<br>⇒ 這是一條**入口回退鏈**：traversal 查詢失敗 → 回退到 jump，**失敗回退到既有動作**，玩家永遠得到反饋。<br>這對 IntentPipeline 是好消息：`JumpRequested` 已經在了，traversal 可以掛在它前面當**優先候選**，而不是新開一條輸入。<br>⚠️ **2026-09-10 更正**（`docs/18` §7-S2）：舊版此欄寫「traversal 是**跳躍的特化**，不是平行系統」——**這是把 UX fallback 當成架構從屬，推理錯誤**。<br>「入口共用回退鏈」**不足以推論執行模型從屬**。理由見 `docs/18` §2.2：①`JumpState` 水平軸是 continuous、traversal 是 committed；②jump 沒有 world target、traversal 有；③traversal 的落點是**准入條件**（查得到才進得去），jump 不是；④`JumpState` 已 603 行，塞進去會撞複雜度上限。<br>⇒ **入口成立、執行模型不成立**，兩者要分開講 |
| **要先懂** | fallback 鏈的設計；「查詢失敗」與「查詢成功但不合格」為什麼要分開（前者可能是效能/時機問題，後者是設計問題） |

#### L6 — Character Alignment（角色對齊）
| | |
|---|---|
| **解決什麼** | 「動畫是為某個相對位置做的，但角色現在不在那個位置」——**把角色搬到動畫假設的起點** |
| **輸入** | 選中候選的 world target（位置＋朝向）、當前 transform、可用的對齊時間窗 |
| **輸出** | 一段**受控的 transform 修正** |
| **Unity 概念** | `Animator.MatchTarget`、`StateMachineBehaviour` 的時間窗、root motion vs procedural、Motion Warping／distance matching |
| **IntentPipeline 承接位置** | ⚠️ **這是衝突最大的一層。** `MotionDriver` 是 `transform` 的**唯一寫入者**，且它的位移來源只有兩種：Movement Output，或烘焙曲線（`ExecuteBakedCurveMovement`／`ApplyBakedCompensation`）。<br>📌 **但 `ApplyBakedCompensation` 已經接受一個 `actualTarget` 參數**——**「朝一個 world target 修正烘焙位移」的 seam 已經存在**，只是目前沒有真實使用者 |
| **缺什麼** | target 的**來源**（L2/L4）與**承諾語意**（進入後多久內不可中斷） |
| **要先懂** | `MatchTarget` 的實際語意與限制；root motion 與本專案「烘焙曲線 ＋ 程序位移」路線的差別（本專案刻意走第三條路，**不要直接套 root motion 教學**） |

#### L7 — Animation / Movement Coordination（動畫與位移協同）
| | |
|---|---|
| **解決什麼** | 動畫播放期間，位移／碰撞／IK／重力各自該做什麼 |
| **輸入** | 動畫進度（normalized time）、烘焙位移資料、碰撞體狀態 |
| **輸出** | 每幀的位移 ＋ 碰撞體設定 ＋ 其他系統的抑制 |
| **Unity 概念** | 動畫事件、`CharacterController` 的 `height`／`center` 動態調整、IK 權重淡出、重力抑制 |
| **IntentPipeline 承接位置** | ✅ **大部分已有**：`RollState` 就是完整範例（烘焙曲線位移 ＋ 動畫時間軸驅動）；`ArbiterData.BlockIK` 已存在；`ExecuteVerticalOnlyMovement` 已示範「只做垂直不做水平」 |
| **缺什麼** | 影片明確展示的「**运动碰撞体处理／减小穿模**」——**翻越期間動態改變碰撞體**。本專案目前沒有任何一處會在執行期改 `CharacterController` 的尺寸 |
| **要先懂** | 改 `CharacterController.height`／`center` 的副作用（`isGrounded` 判定、卡牆、`Move` 的解算）；為什麼 traversal 通常暫時**關掉**碰撞而不是修正它 |

#### L8 — Return to Locomotion（回到移動）
| | |
|---|---|
| **解決什麼** | 動作結束時，把角色**乾淨地交還**給 locomotion，不跳變、不滑步 |
| **輸入** | 結束時的位置／朝向／速度、玩家當下的輸入 |
| **輸出** | locomotion 的初始條件 |
| **Unity 概念** | 出場速度繼承、blend 過渡、foot phase 對齊 |
| **IntentPipeline 承接位置** | ✅ **這一層本專案反而比較成熟**：Jump 落地的 `normalLandToMove`／`walk[]`／`run[]` 變體表、`LocomotionStopSelector` 的 entry-phase 選片、`LocomotionSpeedSmoother` 的連續性保證——**「交還」這件事已經被認真處理過**（那正是「跳躍落地不滑步」這條驗收的內容） |
| **缺什麼** | 目前沒有缺口，但 traversal 的出場條件更多樣（爬上頂面 vs 翻過去落在另一側） |
| **要先懂** | 已經懂了。這一層可以直接沿用既有經驗 |

### 4.2 影片為哪幾層提供了**直接證據**

| 層 | 證據強度 | 依據 |
|---|---|---|
| **L2** | 🟢 **直接看到** | 紅色射線陣列的形狀與範圍、綠球候選、白膠囊預測落點、側向探測線 |
| **L3** | 🟢 **字幕給了維度** | 距離／上方淨空／底部形狀／厚度／左右阻擋（見 §4.1-L3） |
| **L5** | 🟢 **字幕明說** | 「全部检测失败则返回跳跃」 |
| **L6 / L7** | 🟡 **字幕殘句** | 「使用 MATCHTARGET 訂製…在 STATEMACHINEBEHAV…」「运动碰撞体处理／减小穿模」 |
| **L8** | 🟡 **只看到結果** | 看得到角色順暢接回跑動，**看不到規則** |
| **L1 / L4** | 🔴 **沒有直接證據** | ⛔ 不推測 |

### 4.3 對 IntentPipeline 架構的三個真正衝擊

1. **黑板現在沒有「可被多方消費的世界查詢結果」。**
   ⚠️ **2026-09-10 更正**（`docs/18` §7-S4）：舊版此條寫「環境查詢結果會是**第一份**『角色以外的世界』資料」——**這是事實錯誤**。
   `CombatContextData.TargetPosition` 已經是黑板裡的 world-derived 資料，**先例早就存在**。

   正確的說法是：環境查詢結果會是**第一份需要跨幀存活、參與 selection、且被多方消費的**世界資料。
   `PlayerRuntimeData` 目前的每個欄位不是角色自身狀態（intent／output／grounded／arbitration），
   就是**已被 producer 收斂成單點**的世界衍生值（`CombatContext` — 一個 target 位置，`PlayerCombatContextSource` 唯一寫入）。
   traversal 查詢的產物不同：它是**一組候選 ＋ 各自的合格判定**，而且要活過「查詢幀 → selection → alignment → 執行」多幀。

   ⇒ **判準不是「資料是否來自世界」，而是「有沒有第二個消費者、要不要跨幀存活」**（`docs/18` §5.1）。
   ⇒ 因此**「一定要開 ADR」也是過度推論**——真正的 ADR 觸發條件見 `docs/18` §5.2 的 T1–T6；
   在 T1–T6 全不成立的前提下，spike 是做得出翻越的。

2. **`IMovementModel` 的契約是「每幀推進 dynamics」，不是「執行一段有始有終的位移」。**
   ⇒ 這是 `docs/03` §6.4 的 **G1 承載問題**（`LocomotionModel` 內部 phase／獨立 Presentation FSM／Gameplay State）
   換一個**更難**的案例重演。Stop／Pivot 只是「locomotion 內部的過渡」，traversal 是「離開 locomotion 一段時間再回來」。
   **在 G1 有答案之前談 traversal 的承載，一定會重跑一次同樣的討論。**

3. **Character Alignment 是「動畫決定位移」的反轉，而 roadmap 已經預留了它的位置。**
   `docs/03` §6.3 **步驟 6 ＝ Motion Warping**，且原文已寫「**以 Combat 貼靶或 Traversal 對齊為第一個 world-target 案例**」。
   ⇒ **影片印證了既有排序是對的**，不需要改 roadmap。差別只在：影片讓我們**具體看到**那個 world target 是怎麼被找出來的。

### 4.4 需要先建立的知識（三層，由淺到深）

| 層 | 內容 |
|---|---|
| **A. 幾何與查詢** | `Raycast`／`SphereCast`／`CapsuleCast`／`BoxCast`／`OverlapCapsule` 的幾何差異與成本；`*NonAlloc` 與預配置緩衝（零 GC 前提）；`QueryTriggerInteraction`；LayerMask 分層設計；`CharacterController` 的 `stepOffset`／`slopeLimit` **實際上做了什麼**（本專案目前完全倚賴它，但沒有人驗證過它的邊界） |
| **B. 動畫對齊** | `Animator.MatchTarget` 的語意與限制；`StateMachineBehaviour` 的時間窗；root motion vs procedural vs **本專案的烘焙曲線第三條路**；Motion Warping 與 distance matching 的差別 |
| **C. 決策** | affordance 的定義；候選**評分** vs 硬性 **gate**（影片是 gate：任一條件不滿足就失敗）；**fallback 鏈**的設計；**承諾（commitment）與可中斷窗**——這一條與 ADR-007 D5（方向承諾的作用域）是同一個概念家族，可以互相印證 |

---

## 5. 能力矩陣

### ① 已具備，而且概念相近

| 影片能力 | IntentPipeline 對應 | 備註 |
|---|---|---|
| Idle／行走／奔跑、速度階層 | `GaitProfileSO` ＋ 4-tier `Locomotion.asset`（1D LinearMixer）＋ `LocomotionSpeedSmoother` | threshold ＝ `speed_i/speed_max` 的校準紀律已文件化 |
| **行走停步／奔跑停步** | Phase C1 Forward Stop：`LocomotionStopRuntime`／`LocomotionStopSelector`／`WalkStop_*`／`RunStop_*` ＋ foot-phase 選片 | **對方展示的「停步動作」我們已經有，而且做到了 foot phase 級** |
| 跳躍：站立跳／助跑跳 | `JumpStateParams` 的 18 格變體表（`JumpIdleStart`／`JumpWalkStart_LU/RU`／`JumpRunStart_LU/RU`…） | |
| 落地：輕／重 | `JumpIdleLand`／`JumpIdleLandHard`，門檻 `hardLandingSpeed` | 對方多一個「翻滾緩衝」；我們的 `RollState` 存在但**未接為落地緩衝** |
| 空中姿態由 vertical velocity 驅動 | 黑板 `VerticalVelocity`（`MotionDriver` 唯一寫入）＋ `FallingLoop` | 對方用 2D blend tree 混合，我們目前是單一 `FallingLoop`（**presentation 差距，非能力差距**） |
| 走出高台自動進入下墜 | `JumpState.CanEnter` 的第二條入口（連續離地 ≥ `fallEntryGrace`，不注入 launch、拒絕空中跳） | 2026-09-10 落地 |
| **腳步 IK 多地形適配** | `FootIKController` L1 v3：ankle ray 為高度／法線權威 ＋ **heel／toe 雙點**端點殘差 ＋ `ClampGroundNormal` ＋ `PelvisOffsetY` | **雙點採樣對上**（影片字幕「脚跟、脚心分别发出射线」）。<br>⚠️ **2026-09-10 更正**（`docs/18` §7-S3）：舊版寫「概念完全對上」**過度樂觀**——字幕另一句「**提前采样脚部落**…」**沒有對上**：IntentPipeline 採的是**當幀 pre-IK pose**（`FootIKController.SampleGround` 讀 `FootIKPoseData`），**不做預測性採樣**。<br>殘句不足以判斷對方指的是預測落點、預先計算、還是別的東西 ⇒ **列為未定**（§8） |
| 腳步音效事件 | `FootstepDetector`（讀 Foot IK pre-IK pose）→ 黑板 `PresentationEvents` → `AudioController` | 事件層已有 |
| 相機遮擋處理 | `ThirdPersonCamera` 的 `SphereCast` ＋ `AdvanceOccludedDistance` | 對方用 Cinemachine，我們手寫；**能力對等** |
| 多技能／連段／各自冷卻 | ADR-005（**Accepted**）：三份 `ActionDefinitionSO`、共用一顆 `ActionState`、per-slot 冷卻 | **這一項我們比影片展示的更明確**（影片的戰鬥動畫刻意「不把整个战斗系统开启」） |
| 環境觸發的暫時效果 | `TemporaryGameplayEffectState`（Slow）＋ ADR-005 Acceptance G（五個下游檔案零修改自動傳播） | 概念與影片的「BUFF 響應」同源，**但表現形式不同**（我們改速度，對方疊動畫層） |

### ② 已具備，但展示 / observability 明顯不足

| 能力 | 現況 | 差在哪 |
|---|---|---|
| **方向權威**（move／facing／aim） | ADR-007 **Trial**，S1／S2／S3b／F6 程式已落地 | **完全看不見**。七條驗收條件一條都沒打勾，而其中四條本質上是「兩個方向是否一致」⇒ §3.6-1 |
| **Foot IK 探測** | heel／toe／ankle 三條射線 ＋ normal clamp 全部算了 | **一條線都沒畫**。`docs/05` 的約束模型只能靠讀程式理解 |
| **Stop 選片** | entry phase → 變體索引，退化值 −1 | **靜默退化**（W13 那輪的教訓）。沒有 trace |
| **落地分類** | normal／hard 依 `|v_y|` 分流 | 只有 `Debug.Log`；診斷「敵人摔了 3.3 公尺」是靠**手算**反推的（WORKLOG 2026-09-10） |
| **動畫查表失敗** | `AnimancerFacade.TryGetTransition` 失敗 → warn ＋ 保留當前動畫 | 設計好的退化路徑，但**只有 Play 時看 Console 才知道**。W13 把它壓回 EditMode 是正解，**但只涵蓋 authored 的格子** |
| **測試場景本身** | `SampleScene` | 影片的場景把「這一區在驗什麼」用 world-space 文字寫在地上。**這是零成本的 observability**，我們沒有 |

### ③ 正在開發或尚未完整

| 能力 | 狀態 |
|---|---|
| **8 向 strafe / 2D locomotion** | 資產在（8 支 clip）、prototype mixer 在（`Locomotion_2D_Proto_FullRing`／`RunRing`），但 **`Locomotion.asset` 仍是 1D 4-tier**；`docs/13` §9.5 已判定「單環 2D mixer 是**結構性**錯誤」，假說 H1（依戰鬥語境切換兩個 mixer）**待驗** |
| **Combat Context / facing 來源** | `docs/15` 已採納為 ADR-007 的 S3；`CombatContextData` 與 `PlayerCombatContextSource` 已存在 |
| **Lock-on** | `docs/10` 規劃中，`LockOnController` **不存在** |
| **上半身分層（移動中施法）** | ADR-006 **Trial**；X Bot Spell 已接 Layer 1 upper-body mask＋Layer 0 Walk／Run 雙 8-way ring；Sprint directional presentation 延後，待 Play 驗收 |
| **Motion Warping** | roadmap 步驟 6，未開始；但 `MotionDriver.ApplyBakedCompensation(…, actualTarget, …)` 的 seam 已存在 |

### ④ 尚未具備，但與 IntentPipeline 很自然地相容

| 能力 | 為什麼相容 | 代價 |
|---|---|---|
| **地面材質 → 腳步音／急停摩擦音** | 事件層（`PresentationEvents`）與查表層（`AudioLibrarySO`／`AudioEventId`）**都已存在**；缺的只是「腳下是什麼材質」這一個查詢 ＋ enum 加值 | 小。`AudioEventId` 明文「只增不改不重排」，加值安全 |
| **落地翻滾緩衝** | `RollState` 已存在且有烘焙位移；`JumpState` 的落地分類已有 hard／normal 兩檔 | 小。是「第三檔落地」而非新機制 |
| **相機切換（OTS／窄巷）** | `ThirdPersonCamera` 已有 aim／normal 兩組 offset＋FOV 切換的形狀 | 小—中。**但「誰決定切哪個相機」屬於 `docs/09` §2.3 的 scope boundary，要先確認** |
| **世界標籤化的測試場景** | 純資產工作 | 近乎零（但**只有使用者能做**——AI 不碰場景） |
| **Foot IK / 探測可視化** | §3.6-2 | 近乎零 |

### ⑤ 尚未具備，而且會明顯擴張 scope

| 能力 | 擴張在哪 |
|---|---|
| **環境查詢層（L2）** | **黑板第一次要容納「角色以外的世界」** ⇒ ADR 判準 ①，必開 ADR。見 §4.3-1 |
| **攀爬 / 翻越（L2–L8 全鏈）** | 除了 L2，還撞上 **G1 承載問題**（§4.3-2）與 **Motion Warping**（§4.3-3）。**這是三個未決問題的交集，不是一個功能** |
| **執行期改變碰撞體** | 目前 `CharacterController` 尺寸是靜態的；動態改它會牽動 `isGrounded`、卡牆、`Move` 解算——而 `IsGrounded` 是黑板欄位、有單一寫入者契約 |
| **Additive 動畫層（BUFF 疊加）** | ADR-006 Trial 只批准 masked upper-body Spell，不包含 additive BUFF framework；仍屬 scope expansion |
| **物理二級動畫（布料／頭髮）** | 純第三方套件與美術資產問題，與本專案的架構命題**幾乎無關**。⚠️ 作品集角度也不加分（外行看得到，但它不是「架構」） |

### ⑥ 僅憑影片無法判斷

見 §8。

---

## 6. 學習順序建議（⚠️ **這是理解順序，不是實作順序；本文不修改 `docs/03` roadmap**）

| # | 主題 | 為什麼是這個順序 | 與既有排程的關係 |
|---|---|---|---|
| **0** | **Observability 範圍收斂**（`docs/18` §1） | world-space 只保留 traversal／Foot IK 的世界幾何採樣；Direction snapshot／測試保留但不做四箭頭／HUD。Foot IK Game View runtime lines 已落地；前方 obstacle query 本身尚不存在 | 不衝突。屬 owner 內部 Editor／Development 診斷；零新契約 |
| **1** | **把 ADR-007 從 Trial 驗到 Accepted** | **方向權威是後面所有東西的前提**：strafe 要問「移動方向 vs 朝向」、lock-on 要問「facing 來源」、traversal 的 alignment 要問「誰有權改 transform」。**在它未驗證前談 traversal，等於在未定的地基上疊樓** | `docs/03` §6.3 步驟 5「Combat 最小垂直切片」的前置 |
| **2** | **Unity query 工具箱**（§4.4-A） | 純學習，**不寫 production code**。用 spike／probe 探針（CLAUDE.md 已授權用完即丟，不受 Editor Tool 兩道閘門限制）。<br>⭐ **補充**（`docs/18` §7-S7）：`CharacterController.stepOffset`／`slopeLimit` 的**實際行為是一個真缺口**——本專案目前**完全倚賴**這兩個值，卻**沒有人量過它們的邊界**（多高的階梯會被吃掉、多陡會滑下來）。這是 spike 的第一個題目 | 不佔用任何輪次 |
| **3** | **G1 承載問題**（`docs/03` §6.4） | 「一段有始有終的 authoritative 位移該由誰承載」——Stop／Pivot 是它的簡單案例，**traversal 是它的困難案例**。先在簡單案例上得到答案 | `docs/03` §6.3 步驟 3 已排 |
| **4** | **Motion Warping / MatchTarget 語意**（§4.4-B） | L6 對齊的技術基礎 | `docs/03` §6.3 步驟 6 已排，且原文已指名 traversal 對齊 |
| **5** | **才有資格談 traversal 的 ADR** | 到這裡，L2（查詢）／L5（fallback 掛在 `JumpRequested`）／L6（warping）／L8（交還）都有答案或先例，剩下的才是真正要決策的部分 | — |

> **提醒**：這份順序刻意**不**把「做出攀爬」當終點。
> 影片對本專案最大的價值不是「多一個功能」，而是**它讓「當幀決策 vs 對未來的承諾」這個分野第一次具體可見**。

---

## 7. Findings（文件 / 程式 / 資產不一致或風險）

> 依 `docs/16-review-protocol.md` 的精神列出。**只讀不改**，且**不為了批評而發明新原則**。

### F1 — 🟢 **已解決**（2026-09-10 當日）：大量未 commit 工作已入版控
**原始紀錄**：`git status --porcelain` 曾達 **211 筆**（61 個 tracked 已修改，`+6767 / −808`，
外加約 150 筆 untracked，多為 `Assets/ScriptableObjects/Animation/Jump*.asset` 與 `Projectile_Fireball.prefab`），
其中包含 `CLAUDE.md`、`WORKLOG.md`、`docs/00`／`01`／`02`／`05`／`09`／`10`／`11`、`docs/ADR/005`、
`docs/artifacts/architecture-tour.html`、`docs/changelog.md`。
⇒ 風險陳述為：CLAUDE.md 明文「只存在於 session 的東西等於不存在」，那批東西當時只在**工作樹**。

**解決**：使用者已於同日 commit `71f5a5c feat: integrate action, direction authority, jump and presentation progress`，
工作樹清空，分支回到 `main`。⇒ 本條**風險已消除**，保留紀錄僅為說明本文對照的程式基準已入版控。
**Git 全由使用者執行，本文只回報。**

### F2 — 🟡 本專案的 observability 原則只寫在一個 `.cs` 的 XML 註解裡，沒有任何 Living Doc 索引到它
`AIMovementSource.OnDrawGizmosSelected` 的註解寫下了非常好的判準
（「回答一個具體問題」「gizmo 不得成為第二個決策來源」），
但 `docs/00-map.md` 沒有任何一列指向它，dev-spec 也沒有承接。
⇒ 依 CLAUDE.md「新增的文件要在 `docs/00-map.md` 留一行指標，否則等同不存在」的精神，
**這條原則目前處於「在 repo 裡但找不到」的狀態**。本文 §3.1 已把它引出來，
但正式的歸屬（dev-spec 還是子系統分卷）**由使用者裁決，本文不自行決定**。

> ✅ **2026-09-10 落地**：這條原則已有兩個可執行形式——A33 讓 Direction Authority snapshot
> 保持被動且禁止 world-space presentation；A34 禁止 Foot IK runtime renderer／Scene Gizmo 重發
> physics query，並守住 Game View 必須使用 `LineRenderer`、不能依賴 Gizmos 開關的可見性契約。
> 依 CLAUDE.md **Test-as-Spec**「加不變量時優先寫成測試而非散文」，**執行力的部分已到位**。
> 散文規格已收進 Foot IK Living Spec `docs/05` §3.5.5；跨子系統判準留在 `docs/18` §1.0～§1.1。

### F3 — 🟢 Foot IK 已算出 ground normal，但刻意未發布（**不是缺陷**）
`FootIKController` 的 `sample.Normal`（原始命中法線）與 `sample.SoleNormal`（clamp 後）
只用於 IK 目標旋轉，**不進黑板、不對外可見**。
⇒ 目前 `IsGrounded`（單一 bool）是 gameplay 層**唯一**的地面資訊，
**坡度對移動決策完全不可見**。

⚠️ **2026-09-10 語氣校準**（`docs/18` §7-S6）：舊版此條語氣偏向「缺陷／缺口」，**這是錯的**。
**今天只有一個消費者（IK 自己）⇒ 它就該是局部 query 的 implementation detail，未發布是正確的設計。**
判準見 `docs/18` §5.1：**「有沒有第二個消費者」才是發布與否的分界，不是「這個值是否來自世界」。**

**仍然成立的部分**（保留）：它是「環境資訊缺席」的**最小前哨**，也是 §4 那一整層的預告。
⛔ **不得因為這條、或因為「想在 gizmo 裡看見它」，就把 normal 加進黑板**（§3.3 紅線 2）——
要看它，就在 `FootIKController` **內部**畫（§3.4 #10）。

### F4 — 🟡 ADR-007 是 Trial，~~§8 的七條驗收**一條都沒打勾**~~，而多個切片已「程式落地」

> ⚠️ **2026-09-12 更新**：「一條都沒打勾」**已不再屬實**，但成因是**文件落後**而非驗收缺席——
> A1／B 的 Play 證據當時已存在於 `WORKLOG.md`，只是沒回填 ADR 勾選框；C／D 可由程式與 A29／A30 直接證明。
> 已於 2026-09-12 fold back（見 ADR-007 §8）。**現況：4 條通過、F 部分、A2／E／G 未驗 ⇒ 仍是 Trial。**
> 📌 本 finding 的真正教訓因此改寫為：**「Trial 期 code-first 是允許的，但證據散落在 `WORKLOG.md` 而不回填 ADR，
> 會讓下一個會話低估實際進度並做出過度保守的規劃」**——本輪的 traversal 規劃正是這樣被誤導的。

`docs/14` §4 記載 S1／S2／S3b／F6 皆於 2026-09-08 落地，程式面也已核實
（`MotionDriver.ExecuteBaseMovement` 已改用世界方向 `data.MoveDirection`，不再是 `transform.forward`）。
這**符合** CLAUDE.md 允許的 Trial 期 code-first，**不是違規**——
但依 CLAUDE.md「引用 Trial 文件時必須註明其狀態」，
**任何以 ADR-007 為前提的後續規劃都必須標註「使用者已裁決 ≠ 工程上已驗證」**。本文已照做。
⇒ 這也是 §6 把「驗完 ADR-007」排在 traversal 之前的直接理由。

### F5 — 🟢 **不是矛盾**（主動澄清，避免誤判）：8 向 strafe 的三份資料一致
`docs/00-map.md` 標「戰鬥移動 尚未實作」、`docs/13` §9.3 記「資產面 ✅ 已接線」、
磁碟上 `Locomotion.asset` **仍是 1D `LinearMixerTransition`**（4 個 clip / thresholds `0, 0.35, 0.75, 1`），
2D 版本以 `Locomotion_2D_Proto_*` **獨立 prototype 資產**存在。
⇒ 三者**互相一致**：prototype 存在 ≠ 已接進主路徑，正是 `docs/13` §10.3 明訂的「不取代現有 1D」。

### F6 — 🟢 已解決的舊紀錄（更正）
先前紀錄的「`X Bot.prefab` 的 `debugLogGoals` 孤兒序列化值」**已不存在**——
兩個 prefab 與所有 `.cs` 皆搜不到該字串。此項可從遺留清單移除。

---

## 8. 僅憑影片無法判斷（⛔ 不推測）

- 對方的**分層架構**：有沒有黑板、intent／model 是否分離、狀態機拓撲
- 對方的**測試**：有沒有、覆蓋什麼
- 對方的**效能特性**：是否零 GC、每幀 query 成本
- traversal 的**承載**：是 FSM 狀態、`StateMachineBehaviour`、還是獨立元件
- 「由下向上的 while 迴圈」是**每幀跑**還是**只在按鍵時跑**——這兩者的效能與手感特性完全不同
- ⭐ **Foot IK 的「提前采样脚部落…」到底指什麼**（2026-09-10 新增，`docs/18` §7-S3）：
  可能是「預測下一步的落點」、「在 IK 解算前先採樣」、或別的意思。
  對照組是明確的——IntentPipeline 採的是**當幀 pre-IK pose**（`FootIKController.SampleGround` 讀 `FootIKPoseData`），**不做預測性採樣**——
  但**殘句不足以判斷對方是不是在講同一件事** ⇒ **列為未定，不推測**
- 腳步音效是**動畫事件**還是**偵測器**（字幕出現「通过动画事…」但被裁切，不足以斷言）
- 相機用了 Cinemachine 的**哪些**元件（字幕只看得到「通过 CINEMACHINE 实…」）
- 二級動畫是哪一套（Magica Cloth／Dynamic Bone／Unity Cloth／自製）
- **「轴向移动」與 strafe 的關係**：字幕明說「轴向移动」，而我在取樣影格中**沒有看到明確的側向 strafe 展示** ⇒ 不下結論
- 戰鬥動畫的「4 段普攻」是否有輸入緩衝／取消窗——影片只看得到結果

---

## 附錄 — 本次影片取樣方法（可重現）

```bash
# 概覽：每 5 秒一張，4×4 拼成 contact sheet
ffmpeg -i "<video>" -vf "fps=1/5,scale=480:270,tile=4x4" -fps_mode passthrough sheet_%02d.png

# 細讀：指定秒數的全解析度影格，裁掉瀏覽器邊框（本次錄影的內容區為 1120×610 @ 160,110）
ffmpeg -ss <t> -i "<video>" -frames:v 1 -vf "crop=1120:610:160:110" f_<t>.png
```

⚠️ 裁切參數是**這一支錄影**的，換錄影要重新量。
⚠️ 本次錄影的**右側內容超出擷取範圍**（見 §0-E1）——若日後要補讀右上角字幕，需重新錄製。
