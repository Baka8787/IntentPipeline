# WORKLOG

> 唯一的進度管理文件。每完成一項立即更新。
> 歷史架構決策請看 `docs/changelog.md` 與 `docs/ADR/`；此檔只管「現在手上的工作」。

---

## 🔖 交辦（下一會話 Handoff）

> ⬆️ **最新狀態（2026-07-26）＝里程碑檢查點：v0.19 Foundation 收案補寫完成**，見緊接的專段；再往下依序是 v0.23／README・LICENSE／v0.22／Stage 2／Stage 1 的交辦。
> 📍 **會話開場請先讀 `docs/00-map.md`**（單頁索引：模組 → 檔案 → 治理章節），再讀本段。

---

## 🏁 里程碑檢查點（2026-07-26，changelog v0.19 補記）

**v0.19 Foundation ＋ GaitProfile ＋ Run 預設型態 ＋ Animation-independent gameplay core ＋ Runtime baked data** 五項齊備，這條線第一次全程走通：

```
InputAction → InputData(ref struct) → PlayerLocomotionPolicy(+GaitProfileSO)
  → MovementIntent{強度[0-1], 方向, WalkModeActive}     ← 模型無關契約
  → LocomotionModel(B9 平滑 → Movement Output，自驅 SetFloat)
  → FSM(問 IsProducingMotion) → MotionDriver → CharacterController
```

**磁碟驗證的收案狀態**：`Locomotion.asset` 4-tier（`0/0.265/0.574/1`）／`moveSpeedSource`→`Bake_SprintFwdLoop`（6.100843）／4 支 loop 的 `FootPhaseCurve` 已補（401·61·47·39 keys）／`Gait_ActionRPG`（0.75／1.0／0.3651／toggle）／`Bake_Stand To Roll.BakedDuration` 2.3666668／**EditMode 76 綠**。

**仍未達成（勿當成已完成）**：`SourceClip` 欄位仍讓 clip 被打包載入（只是邏輯不讀）／0 GC 無 Profiler 存證／`MovementContext` 未實作／7 顆 Bake 的 `BakedDuration` 為 0（刻意延後）／`ComputeAverageSpeed` 低估 1.6~2.6%。

---

## Runtime → AnimationClip 依賴切斷（2026-07-26，changelog v0.23）——✅ 完成並驗證（76 綠）

起因：README 要宣稱「Kubold 只是 sample content」，先做了一次沿實際程式的 animation-independence 追蹤，抓到全專案唯一一條執行期 clip 耦合（`MotionBakeData.Duration => SourceClip.length`）。

### 已完成（修改 3 檔＋測試 3 條）
- `MotionBakeData`：新增序列化 `BakedDuration`；`Duration => BakedDuration`；`SourceClip` 註記為 Editor-side provenance。
- `MotionBakeEditor.SaveAsset`：烘焙時 `asset.BakedDuration = sourceClip.length;`
- `RollState`：退化條件改看**值**（`> 0`）而非引用；新增「資產未重烘」的 Editor 警告。
- 測試 73 → **76**（`Duration` 不依賴 clip／舊資產如實回 0／**Roll 無時長時不得秒退**）。

### ✅ 使用者側已完成
- **只重烘 `Bake_Stand To Roll`**（唯一有 `Duration` 消費者的資產）→ 翻滾恢復正常。
- **EditMode 76 條全綠。**

### 📌 刻意延後：其餘 7 顆 Bake 資產（決策，非遺漏）
其餘 `Bake_*.asset` 的 `BakedDuration` 目前為 **0**，**用到時再烘**（例：做狀態銜接而開始用 `Bake_Jump` 時，順手重烘該顆）。

依據：目前 `Duration` 的消費路徑**只有 Roll 一條**（`RollState.OnEnter` ＋ 它唯一呼叫的 `MotionDriver.ExecuteBakedCurveMovement`），其餘資產無人讀 `Duration`——`moveSpeedSource` 讀的是 `AutoAverageSpeed`、Jump 讀的是 `Auto*` 純量，兩者都已存在且正確。

> ⚠️ **這個延後帶著一個已知風險，別忘了**：日後若有**新的**消費者開始讀某顆未重烘資產的 `Duration`，它會拿到 0，而**目前只有 `RollState` 有「值 > 0」的退化閘門與 Editor 警告**，其他消費者沒有。
> 兩個處理選項（都不急，用到再說）：①新消費者上線時順手重烘該顆；②若這類消費者變多，就在 `MotionBakeData` 加一個 `#if UNITY_EDITOR` 的 `OnValidate` 警告，讓「未重烘」在資產層就現形，不必每個消費者各寫一次閘門。

---

## Repo 門面：README ＋ LICENSE（2026-07-25）——檔案已建，待你 commit

起因：repo 為 Public 且定位作品集，但 ①`LICENSE` 缺席＝保留所有權利，與「未來可抽取的開源套件」定位矛盾；②第三方資產已從歷史清除 → **fresh clone 無法編譯**，沒有 README 的訪客只會看到一個編不起來的專案。這是唯一一項「愈晚做代價愈高」的待辦。

### 已完成（AI 只建檔，git 由你執行）
- **`LICENSE`**：MIT，`Copyright (c) 2026 Baka8787`。**未修改 MIT 原文**（改授權條文是壞習慣）；第三方資產的排除說明放在 README 的 License 段。
- **`README.md`**：英文摘要 3 段（作品集門面）→ 專案定位 → 架構主張表 → **Mermaid 資料流圖**（GitHub 原生渲染）→ ADR 索引 → 專案結構 → 文件導覽 → **測試段（A1~A10 逐條說明「架構不變量是可執行的」）** → ⚠️ 第三方資產需求 → License。
- 順帶把 `docs/01`／`docs/02` 的文件標題從 `CharacterController` 改為 **`IntentPipeline`**（與 repo 名一致）。

### ⚠️ 待你確認／執行
1. **審 README 內容**：特別是「第三方資產需求」表（Animancer 走 `Packages/com.kybernetik.animancer/` 本機 UPM、Kubold 走 `Assets/MovementAnimsetPro/`）與英文摘要的措辭。
2. **commit ＋ push**（AI 不碰 git）。
3. 記憶清單剩餘兩項：**GitHub Topics**（目前空）、**個人頁 Pin**。

---

## Walk 型態 hold／toggle（2026-07-25，changelog v0.22）——✅ 測試已通過

落地第一套完整控制方案（參考終末地）：**WASD 預設 Run／Ctrl 切換 Walk 型態／Shift 閃避／Space 跳躍**，sprint 由 buff 驅動（未來）。**無架構變更**——沿用 ADR-003 D5 既有裁決，未開新 ADR。

### 已完成（修改 6 檔＋測試 4 條）
1. **`InputData.WalkButtonDown`**（邊沿）：與既有 `WalkButtonHeld` **並存**，raw input 層不預設控制方案。
2. **`MovementIntentData.WalkModeActive`**（mode state 進黑板，D5／§9-L5）：語意＝「型態開著沒有」，非「鍵按住沒有」。
3. **`GaitProfileSO.walkIsToggle`**：hold／toggle 成為**資產可配置項**——換玩法＝換資產的承諾對「操作語意」也成立。`ResolveIntensity` 第三參數改名 `walkHeld`→`walkActive`。
4. **`PlayerLocomotionPolicy`**：讀黑板 → 邊沿翻轉 → 寫回黑板，**零私有欄位**。
5. **Editor 監視器**：新增 `Walk Down（邊沿）` 與 `Walk Mode Active（型態）` 兩列，toggle 行為肉眼可驗。
6. **測試 69 → 73**（hold 鏡射／toggle 翻轉閂住／toggle 不看 Held／狀態不得殘留在 producer）。

### ✅ 使用者側已完成
- 建立 gait 資產、綁 `WalkAction` → Left Ctrl、勾 `walkIsToggle`；**EditMode 73 條全數通過**。
- **依手感調整數值**：`walkIntensity` 0.2651 → **0.3651**、`defaultIntensity` 0.574 → **0.75 以上**。
  - **這是安全的**：`threshold = speed_i/speed_max` 讓任意 intensity `p` 的混合動畫速度恆為 `p × speed_max`、與位移速度恆等 → **不會滑步**。偏離基準值只代表「刻意選了一個混合姿態」（walk≈走/跑之間、default≈跑/衝之間），不是校準錯誤。
  - 這條釐清已寫進 dev-spec §3.1（GaitProfileSO 紀律列）與 §7-M4——**公式綁的是 threshold，不是 intensity**，先前兩者被混在同一句話裡。

### 📌 若還想更快（第 3 階，需重烘）
把第三 tier 換成 Kubold 的 Fast Run clip（`Bake_Fast Run.asset` 已存在但缺 `AutoAverageSpeed`，須重烘），threshold 依公式重算。**禁止**調 `MotionDriver.moveSpeed` 或勾 `overrideMoveSpeed` → 那才會全域滑步（§9-L4）。

### 🐛 待修（低優先，需重烘全部資產）
1. `MotionBakeData.ComputeAverageSpeed` 把第 0 帧那支人造的 0 值算進算術平均 → 代表速度**低估 1.6%~2.6%**（Run 真值 3.578 記為 3.502）。修正＝跳過該支哨兵值，但會改變所有已烘值，需重烘一輪。
2. ~~🆕 **`MotionBakeData.Duration` 是全專案唯一一條「執行期邏輯讀 `AnimationClip`」的耦合**~~ → ✅ **已於 2026-07-26 以修法 A 解決（changelog v0.23）**：新增序列化 `BakedDuration`（烘焙期自 `clip.length` 快照）、`Duration` 改讀它、`SourceClip` 降為 Editor-side provenance、`RollState` 退化條件由「引用是否為 null」改為「**值是否 > 0**」。測試 73 → **76**。**⚠️ 需重烘 8 顆 Bake 資產，見下方清單。** 原始診斷保留於下：
   ```
   MotionBakeData.cs:88   public float Duration => SourceClip != null ? SourceClip.length : 0f;
   RollState.cs:58        _rollTimer = _rollBakeData != null ? _rollBakeData.Duration : FallbackDuration;
   ```
   fallback 檢查的是 **asset 為不為 null**，不是 **clip 為不為 null** → clip 遺失時 `_rollTimer = 0`、**Roll 第一帧就結束**，而 `FallbackDuration` 永遠用不到。**是「Roll 秒退」的同型變體**（上次根因在 asset 層＝bakeMappings 未綁，已修；這次在 clip 層，守不到）。
   - **目前不會觸發**：Roll 的 clip 是 Mixamo `X Bot@Stand To Roll.fbx`、在版控內、GUID 穩定 → 屬**潛伏缺陷**非現行 bug。
   - **修法 A（推薦）**：烘焙時把 `clip.length` 序列化成 `BakedDuration`，`Duration` 改讀它（與 `AutoAverageSpeed` 同 pattern）→ `MotionBakeData` 自此**完全不需執行期持有 clip 引用**，`SourceClip` 降為 Editor-only 溯源欄位。這也是「可抽成 Unity Plugin」需要的形狀。代價：重烘一輪。
   - **修法 B**：只在 `RollState` 補 `Duration > 0` 判斷。三字元修補，但耦合仍在，下一個消費者會再踩。
   - **待裁決，本輪未動手。**

---

## ADR-003 Migration Stage 2（2026-07-25，changelog v0.21）——✅ 程式完成、Unity 已驗證（EditMode 綠）

**完成判準已達成：Runner 不再認識任何 locomotion 概念**（並由新測試 A9 守住不回流）。ADR-003 §9-L1 結案；本輪**未改 ADR**（零 Blocking Issue）。

### 已完成
1. **新增 `Core/Movement/Models/`**：`IMovementModel`（通用抽象，兩個進入點）＋ `LocomotionModel`（MonoBehaviour，持有 `LocomotionSpeedSmoother`、寫 Movement Output、自驅 `SetFloat`）。
2. **遷移**：B9 平滑＋運動輸出導出＋動畫參數驅動全數離開 Runner（`DeriveMovementParameters` 刪除、`SyncAnimation` 只剩 `Play`、兩個平滑時間欄位移到 model）。
3. **注入鏈**：Runner 解析 `IMovementModel` → `FullBodyStateMachine.Initialize(config, data, model)` → `BaseState.Initialize(config, model)` 發給所有 state。**唯一實例＝結構保證**（本輪最大陷阱的解法）。
4. **FSM 門檻**：Idle／Move 的 `CanEnter` 改問 `IsProducingMotion`；`OnUpdateMotion` delegate 給 model（D3）。
5. **測試**：新增 **A9**（Runner 不得出現 locomotion token）／**A10**（平滑持有者唯一）；`StateMachineTests` 改用 `FakeMovementModel`。67 → **69** 條。
6. **文件**：dev-spec v0.21（§0.2／§1.1／§2.1 含新增脆弱點第 6 條／§3.1 新節／§7.1 A4・A5・A9・A10／§7.2 M3／**§7.3 結案兩列**）、design-doc v0.21（§4.8 改寫＋Trade-off 補列）、changelog v0.21（並依分卷規則把 v0.18.5／v0.18.6 移入歸檔卷）、`docs/00-map.md` 補 Models 列。

### ✅ 使用者側已完成（2026-07-25 當日回報＋磁碟核對）
1. **角色 Root 掛上 `LocomotionModel`**（與 `CharacterPipelineRunner` 同一顆 GameObject → Runner 欄位留空、`GetComponent` 補洞成立）；Accel 0.12／Decel 0.18 ＝原 Runner 值。
2. **EditMode 測試綠**（含新增的 A9／A10）。
3. **v0.19 Foundation 資產收齊**：`Locomotion.asset` 4-tier（`0 / 0.265 / 0.574 / 1`、4 clip）＋ `MotionDriver.moveSpeedSource` → `Bake_SprintFwdLoop`（`AutoAverageSpeed` 6.1008）。
   - 📌 prefab 內序列化的 `moveSpeed: 5.66` 是**舊值不必手改**——`MotionDriver` 啟動時以來源代表速度覆寫（唯一寫入時機在啟動，非熱路徑）。
   - 這也解除了先前預警的校準風險：mixer 頂 tier（Sprint）與位移滿速現已同源。

### ✅ 已回報通過
1. **§7-M1 行為等價**（含 Stage 2 的兩個迴歸點：跳躍落地不滑步、Idle↔Move 無速度跳變）。
2. **§7-M2 Profiler 0 GC** —— ✅ **自檢級達標**（2026-07-26，changelog v0.24）：量測過程中**抓到並修掉一個真的 bug**——`EvaluateTransitions` 對介面型 `IReadOnlyList<T>` 做 `foreach`，`List<T>` 的 struct enumerator 被裝箱，每帧 40 B。改索引迴圈後**穩態 `PlayerLoop` = 0 B**。
   - **量測程序已寫成 SOP → `docs/02-dev-spec.md` §7.4**（量哪裡／排除什麼／兩級判定／實測數據）。
   - 狀態切換幀約 2.6 KB，已拆解定位為 Editor-only 的 `Debug.Log`（其中 2.4 KB 是 Unity 的 `StackTraceUtility`，非我們的字串），Release 編譯移除。**不是回歸。**
   - **仍差一步**：以上皆 Editor 內量測。要讓 README 把零 GC 從「設計目標」升為「已驗證」，仍須 **Development Build ＋ Autoconnect Profiler** 複驗一次（§7.4.3 的「達標」等級）。**在此之前對外維持「設計目標」。**
3. **changelog v0.19（Foundation 收案）** → ✅ **已於 2026-07-26 補寫**，並升格為里程碑檢查點（見下）。

---

## 文件結構優化（2026-07-25，changelog v0.20.1）——已完成，**無待辦**

起因：v0.20 完成後量測發現單一功能任務讀掉全專案 23%，讀取放大率 5×～40×。四項措施全數落地：

1. **changelog 分卷**：主檔只留最近 4 版（819 → 169 行），其餘進 `docs/changelog-archive.md`（一字未改）＋卷末版本索引表。**新增版本一律寫主檔頂端；主檔超過 4~5 版時把最舊的搬進歸檔卷。**
2. **新增 `docs/00-map.md`（45 行）**：模組 → 檔案 → 治理章節單頁索引＋「常見問題最短路徑」表。**維護規則：只記指標、不記細節。**
3. **dev-spec 分卷**（1,169 → 1,018 行）：§3.5 Foot IK → `docs/05-foot-ik.md`；§3.2 動畫呈現三小節 → `docs/06-animation-presentation.md`。**逐字搬移、章節編號原樣保留、原位留 stub → 既有引用零改寫**（全 docs 連結掃描 3/3 有效）。
4. **CLAUDE.md 新增 `Context Discipline` 章**：閱讀協定／Test-as-Spec 原則／Explore subagent 授權；並**明文推翻 2026-07-21「不回頭拆既有文件」規則**（附推翻依據與三條資格條件：已凍結、非跨領域契約、逐字搬移保編號留 stub）。

> ⚠️ **對你的唯一影響**：查 Foot IK 規格改看 `docs/05-foot-ik.md`（章節仍叫 3.5.x）；查 Animancer／Mixer 規格改看 `docs/06-animation-presentation.md`。dev-spec 原位置都有 stub 指路，不會找不到。

---

## 今日進度（2026-07-25）——ADR-003 Migration Stage 1（程式完成，待 Unity 驗證）

詳見 `docs/changelog.md` v0.20。**本輪未改 ADR-003（零 Blocking Issue）**；不新增 gameplay 功能、不提前實現 AI／Network／Vehicle。

### 已完成
1. **Stage 0 對照盤點（唯讀）**：ADR-003 D1~D5 全條款 ↔ 現有程式，三態標註（已存在相符／尚不存在／存在但形態不符）。結論：契約可完整映射，`Runner.ProcessParameters` 與 B9 的錯置屬 **ADR 自列的 §9-L1 Stage 2 遷移項**，非衝突。
2. **Stage 1 落地**（新增 5 檔／修改 5 檔）：`MovementIntentData` 黑板 region ＋ `IMovementIntentSource` ＋ `PlayerLocomotionPolicy` ＋ `GaitProfileSO` ＋ `LocomotionSpeedSmoother`（B9 抽成純運算 struct＝Stage 2 遷移單位）；管線新增**順序 2.5**；`InputData` 加中性 `SprintButtonHeld`／`WalkButtonHeld`。
3. **架構回歸檢核清單** → `docs/02-dev-spec.md` **§7**（A1~A8 自動／M1~M6 人工，各標實施方式），自動項實作為 **`ArchitectureRegressionTests`（A1~A5）** ＋ **`MovementIntentTests`（A6~A8）**，新增 **20 條**（5＋15）。
4. **文件同步**：dev-spec v0.20（§0.2／§1.1／§1.3／新增 §1.5／§2.1／§3.1／新增 §7）、design-doc v0.20（§4.1／§4.2／新增 §4.8／Trade-off 兩列）、changelog v0.20。

### ⚠️ 待使用者（Inspector／Play／Git——AI 不碰）
1. **【必做，否則角色不會動】在角色 Root（`X Bot` Prefab，掛 `CharacterPipelineRunner` 那顆）加上 `PlayerLocomotionPolicy` 元件。** Runner 的 `Movement Intent Source Component` 欄位可留空（Awake 會自動 `GetComponent` 補洞）；未掛則 Play 時 LogError 且 `MovementIntent` 恆 0。
2. **重編＋跑 EditMode 測試**：預期 0 error、**67 條全綠**。⚠️ 順帶更正文件漂移：先前紀錄的「42 條」已過時——磁碟實際 `[Test]` 為 **47** 條（無參數化測試），故本輪後為 47＋20＝**67**。**以 Test Runner 實跑數字為準**，若與 67 不符請回報。
3. **Play 行為等價驗收（§7-M1）**：**先不要建 `GaitProfileSO` 資產** ——留空時強度＝原始推桿量，手感應與本輪之前**完全一致**（加速平順、放開滑行收步、無滑步）。若有差異即為 regression，回報而非調參。
4. **（可選，行為等價驗收通過後再做）啟用 gait 方案「預設 Run／Shift=Sprint／Ctrl=Walk」**：
   - `PlayerInputSource` 新增的 `Sprint Action`／`Walk Action` 綁 Left Shift／Left Ctrl。
   - 建 `GaitProfile.asset`（`Assets/ScriptableObjects/Movement/`，選單 `Project/Core/Movement/GaitProfile`），拖進 `PlayerLocomotionPolicy`。
   - **數值一律用公式 `intensity_i = speed_i / speed_max` 從 Bake Data 換算**（§7-M4；程式預設一律 1＝無 gait 差異，刻意不硬編實測值）。以目前四段速度真相（Walk 1.62／Run 3.50／Sprint 6.10）換算即 default≈0.574、sprint=1.0、walk≈0.265——**填之前請以重烘焙後的 Bake 值複核**。
5. **Profiler 0 GC 複驗（§7-M2）**：熱路徑新增的是值型別運算，預期 0 B，但仍請實測確認。

### 下一步（擇一）
- ~~**A. Stage 2（B9／MoveSpeed 歸位，收 §9-L1）**~~ → ✅ **已完成（2026-07-25，changelog v0.21）**，見本檔最上方專段。實作時發現 ADR 未預想的兩個時序陷阱（Jump 期間 dynamics 不可凍結／`SetFloat` 不可落 LateUpdate），故 model 採兩個進入點、順序 3 保留。
- **B. 先做 Foundation 收案（v0.19）／Phase C**：見下方前一輪交辦（⚠️ 收案狀態與磁碟不符，見最上方「待釐清」）。
- 📌 Stage 3（`MovementContext`、AI／Replay／Network producer、`CombatIntent`）**待真需求**，勿提前。

---

## 🔖 前一輪交辦（Foundation／Foot IK，仍有效）

> 本 session 量大（Foot IK 收案＋Locomotion Foundation＋B9＋Movement Policy ADR-003＋第三方屏蔽）。**先讀這段**；細節見 `docs/04-locomotion-foundation.md`、`docs/ADR/003-*`、下方各進度段。

### 已完成（本 session，程式全綠）
- **Foot IK v1 收案**（輪 1，changelog v0.18.7）
- **輪 2 Foundation 程式**：Foot Phase Curve stage（`MotionBakeData`+`MotionFeatureAnalysis`）／per-clip 版 `MotionClipImportSOP`／**B9 MoveSpeed 平滑**（`CharacterPipelineRunner`）＋5 新測試
- **Kubold 盤點**（docs/04）＋Import/Bake loops（速度真相 Walk 1.62／Run 3.50／Sprint 6.10 m/s）
- **Movement Policy 四輪對抗式評審**（docs/04 §11–14）→ **`docs/ADR/003-movement-intent-layering.md`**（Accepted＝契約定案、程式未實作；含 §13 四點責任邊界）
- **`.gitignore` 加第三方資產排除**（本段最後任務，SOP 見下）

### 待使用者（Inspector／Git／實測——AI 不碰）
1. **第三方資產屏蔽 SOP**（↓ 專段，你執行 git）
2. **Foundation 資產**：docs/04 §10 — `Locomotion.asset` 擴 4-tier（Idle 0／Walk 0.265／Run 0.574／Sprint 1.0；Sync 開 Walk/Run/Sprint）＋`MotionDriver.moveSpeedSource`→`Bake_SprintFwdLoop`；**重烘 4 支 loop** 補 FootPhaseCurve；Play 驗（按 W 平順加速無滑步）
3. **鏡頭**：角色 Root 拖入 Main Camera 的 `Third Person Camera.Target`、`Mouse Sensitivity` 2→0.1

### 待裁決／下一步（擇一起手）
- **A. Movement Intent Migration Stage 1（動程式）**：審 ADR-003 → 核可 → 落地最小 seam（`MovementIntent` region＋`IMovementIntentSource`＋`PlayerLocomotionPolicy`＋`GaitProfileSO`；行為等價＋順帶落地最初想要的「預設 Run／Shift=Sprint／Ctrl=Walk」）。**Stage 1 紀律：`MovementIntent` 唯一真相、`MoveSpeed` 過渡衍生值（ADR §13.4）**
- **B. Phase C**：停步分腿姿勢（stop 動畫＋Foot Phase 選腳別）＋Starts/Stops/Turns 導入（烘焙曲線驅動＝Roll 先例，per-clip 套 preset）＋承載定案
- 停步姿勢＝loop 無收步語意、非 Blocking，**建議歸 Phase C**（待確認）
- **changelog v0.19（Foundation 收案）** 待 Play 綠燈補

### 🚫 第三方資產屏蔽 SOP（你執行；AI 不碰 git）
現況：Animancer Pro／Kubold／StarterAssets **已被 git 追蹤**（~224MB），`.gitignore` 已加排除但**已追蹤檔需手動取消追蹤**才生效。
```bash
# 0) 最關鍵：確認 repo 為 PRIVATE（公開才觸發 EULA 二次散佈問題）
# 1) 取消追蹤（本機檔案保留、Unity 照常運作；只從 git index 移除）
git rm -r --cached "Packages/com.kybernetik.animancer"
git rm -r --cached "Assets/MovementAnimsetPro" "Assets/MovementAnimsetPro.meta"
git rm -r --cached "Assets/StarterAssets" "Assets/StarterAssets.meta"   # StarterAssets：確認專案不依賴再做
# 2) commit
git commit -m "Untrack third-party paid assets (Animancer Pro, Kubold); enforce via .gitignore"
```
- ⚠️ **歷史殘留**：上述只停「未來」追蹤；資產仍在**過去 commit 的歷史**裡。solo private repo → 保持 private 即足夠。**若曾公開／要公開** → 需 `git filter-repo` 重寫歷史清除（destructive，先備份）。
- ⚠️ **fresh clone 不可編譯**：Animancer＝執行期核心依賴、Kubold＝Bake 資產 GUID 引用來源。**建議 repo 加 `README` 註明必要資產與各自重匯入方式**（要我下輪寫可講）。
- X Bot／Mixamo（角色本體，免費但 Adobe 條款）：**不建議排除**（全場景依賴，破壞成本 > 低風險）；如在意另議。

---

## 今日進度（2026-07-21）——Foot IK v1 收案輪（輪 1）✅

roadmap `docs/03-animation-roadmap.md` §1.4 收案清單執行完畢（詳 changelog v0.18.7）：

1. **程式碼**：`FootIKController.ResolveFoot` 旋轉公式還原基線「保留俯仰式」（`FromToRotation(worldUp, n) × poseRot`；A/B 軸對齊式歸檔）；`FootIKRig` 刪 `debugLogGoals` 臨時診斷段。
2. **文件同步**：changelog v0.18.7（樓梯 collider 根因／A/B 結論／設計哲學／v1 凍結宣告）；design-doc §4.6 補 Foot IK 設計哲學；dev-spec §3.5 補 v1 凍結狀態＋已知限制表 L1~L6、§3.5.3 首查項標否證、版本表補 v0.18.7（順修重複／錯置的 v0.18.3 列）。
3. **Foot IK v1 凍結**：架構健康、6 條已知限制（L1~L6）文件化於 dev-spec §3.5.2；品質升級改由 `docs/03` roadmap 承載。主線下一步＝**輪 2 Locomotion 資產升級**（＋Foot Phase 烘焙 stage＋B9）。

---

## 前次進度（2026-07-18，已收案 → changelog v0.17／v0.18）

三輪連發，詳見 changelog v0.17／v0.18：

1. **M2 Presentation Pipeline + Landing Audio ✅ 收案**（changelog v0.17）：修復前 session 幻覺殘局 → `JustLanded` 落地（YAGNI 閘門走完）＋`PresentationPipeline` 骨架（順序 6.5）＋Audio 三層（Event→Definition→Library）；Play 實測落地音正常。附 EditMode Warning 治理（RollState/JumpState 防線 `isPlaying` 語義精確化；測試契約輸出用 LogAssert.Expect＋鬆耦合 Regex）。
2. **M1 Locomotion ✅ 正式收案**（changelog v0.17 §5）：DoD 五項全過（0 error＋測試全綠／Play 實測／Profiler 0B／moveSpeedSource 接 Bake_Fast Run／Roll fade 資產真相驗證）。Locomotion 基線固定。
3. **M3 Foot IK 實作輪 ✅**（changelog v0.18）＋**M3.1 反饋迴路修正 ✅**（changelog v0.18.1）：實測腳踝抽搐 → Review 定位根因（Controller 採樣骨骼＝上一幀 IK 輸出，旋轉追逐＋權重鎖死雙迴路）→ 裁決雙管道修正——`FootIKController`（Root 決策，對 Animator 零依賴）⇄ 兩條單向管道（`FootIKTargetData` Controller 寫／`FootIKPoseData` Rig 寫）⇄ `FootIKRig`（Model，**Presentation Adapter**）。手填 footHeight 改讀 avatar `FeetBottomHeight`。抽搐複測通過、M3.5 基線已 push（2026-07-18）；**v1 已於 2026-07-21 凍結**（見頂部收案輪）。

---

## 待使用者作業

- **重編確認**：Unity 重編 0 error＋EditMode 測試 **42 條**全綠。收案輪程式改動＝旋轉公式一行還原＋刪 Editor-only 診斷段，不涉純函數／測試契約。
- **孤兒序列化值**（無害，Unity 靜默忽略）：`X Bot.prefab` 殘留 `debugLogGoals` 序列化值——同 v0.18.6 移除 `Enable*` flag 的既定情形，可在 Inspector 順手清、不清亦無影響。
- **資產側**（AI 不碰，SOP 由你在 Editor 執行）：牆壁 collider 過胖修正（身體碰不到牆）；CapsuleFitter Apply Prefab 確認；floor Scale Z 翻正（-25.153 → +25.153）若未做。**樓梯 collider 已修 ✅**。

---

## 工作清單

### Done（2026-07-18）
- [x] M2 全流程（黑板單幀事件 → 6.5 → Audio）＋收案；M1 DoD 收案；Warning 治理兩輪
- [x] M3 Foot IK：3 新檔＋Facade IK 通道＋`FootIKTests` 8 條＋Living Docs v0.18

### Doing
- [ ] **🔬 Movement Policy 設計探索（`docs/04` §11 分析 ＋ §12 Architecture Review，純分析未改程式）**：發現目前**無速度模式選擇層**（`InputData` 無 modifier、`ProcessParameters` 寫死 `magnitude→MoveSpeed`）。§11 初提 MovementProfile＋Resolver；**§12 自我挑戰後部分推翻**——原案 overfit（1D-speed、擴不到 strafe/swim/vehicle）、seam 綁 input（netcode/AI 敵對）、DIP 弱。**修訂設計（§12.3）**：seam 上移黑板中性 **`MovementIntent`**＋介面化 **`IMovementIntentSource`**（player/AI/replay 可換）＋**model 走既有 `OnUpdateMotion` seam**＋gait profile 收窄＋mode/toggle state 進黑板。務實 staging：現在只放最小正確 seam，其餘加法。停步分腿姿勢＝loop 無收步 → Phase C（stop 動畫＋Foot Phase）。**§13 Architecture Validation（Runtime Data Flow Diagram）已完成**——畫圖時再修 3 點：R1 MovementIntent＝模型無關 intensity+dir（非 gait）、R2 B9 屬 Locomotion model（現況在 Runner＝待遷移殘餘耦合）、R3 producer context-free（無循環）。6 問驗證全過（ownership 單寫/lifetime snapshot-able/DIP 反轉/唯一無害 1-frame 回饋/seam 模型無關）。**§14 Design Review R2**：使用者再挑戰，抓出 3 條混淆軸線（皆成立）——①Movement Model（context 軸）≠ Gameplay State（action 軸），正交、需獨立 `MovementContext` resolver；②Blackboard 應 domain-partitioned intents（MovementIntent/CombatIntent/InteractionIntent）非單一 god-Intent；③MoveSpeed 屬 Locomotion model 內部、各 model 自驅動畫參數走通用 Facade（Facade 本身即抽象、**不需** IAnimationModel）。**§14.6/14.7 v3 圖（三軸分離）已重畫並複驗——無新裂縫、設計收斂**：Ownership/Lifetime/R-W/DIP/循環/耦合 六項全過；唯一殘餘＝B9 在 Runner（列 ADR known-migration）；補 nuance＝ambient state(Idle/Move) delegate model、intrinsic-motion state(Roll/Jump/Attack) 本就 override OnUpdateMotion（既有機制）。**✅ `docs/ADR/003-movement-intent-layering.md` 已撰寫**（Status/Context/Problem/Decision 5 契約/Diagram/Responsibility Matrix/Alternatives〔完整保留否決 BaseState-Shift／MovementModeResolver／Input-Modifier 三案理由〕/Trade-offs/Consequences/Known Limitations L1-L6/Migration Plan Stage 0-3+/Future Extension）。狀態＝Accepted（契約定案、程式尚未實作，比照 ADR-002）。**§13 補四點責任邊界**：①MovementIntent schema 僅適「方向性移動家族」非萬用（異質 model 開兄弟 schema）②MovementContext 描述性、不否決 State——Gameplay Authority 屬 Capability/Profile（how vs what's-allowed vs doing 三權分立）③Producer 不管 context-sensitive input，Input Routing 在上游(action map/Input Router)④Stage1 MovementIntent 唯一真相、MoveSpeed 僅過渡衍生值(禁繞過 intent 直寫)。**下一步待使用者核可 → Migration Stage 1（最小 seam：MovementIntent region＋IMovementIntentSource＋PlayerLocomotionPolicy＋GaitProfileSO，行為等價重構）**。實作時才更新 design-doc/dev-spec（ADR §10 文件責任）。停步歸 Phase C 待確認。
- [ ] **輪 2 Locomotion Foundation 進行中**（規劃 `docs/04`）。**已裁決**：四段 Idle/Walk/Run/Sprint（速度段數由資產決定，Jog 不硬補）、Humanoid retarget X Bot、承載延到 Foundation 驗證後。**已完成**：Import＋Bake（loop 速度真相有效——Walk 1.62／Run 3.50／Sprint 6.10 m/s；門檻 = speed/6.10）。**程式已落地**：Foot Phase Curve stage（`MotionBakeData`+`FootPhaseCurve`欄位/`GetFootPhaseAt`；`MotionFeatureAnalysis`+`FootPhaseCurveAnalyzer`+註冊）＋per-clip 版 `MotionClipImportSOP`（選子 clip 只套那幾支）。**SOP 誤用診斷**：主 FBX 全 clip 被灌 loopTime:1，但只波及未用到的非 loop clip（4 支 loop 完好）→ 不重下載，per-clip 工具已備供 Phase C。**待使用者**：重編 0 error → 重烘 4 支 loop 補 FootPhaseCurve → 我接 Mixer 擴充/Calibration。
- [ ] **鏡頭修復**：程式加了 Fail-Fast（target null 報錯）；**待使用者在場景**把角色 Root 拖入 Main Camera 的 `Third Person Camera.Target`，並把 `Mouse Sensitivity` 2→0.1。Cinemachine 為未來打磨選項（已裝 2.10.7），非本輪必要。

### Todo（輪 2，依 `docs/04` §7／§9 拆分）
- [x] Import Preset（loops）＋Bake（loops 速度真相）✅
- [x] Foot Phase Curve stage 程式（analyzer＋欄位）✅／per-clip SOP 工具 ✅
- [ ] 使用者重編 + 重烘 4 支 loop（補 FootPhaseCurve）
- [x] **Mixer 擴充 + Calibration SOP 已出（docs/04 §10）**——查證 `MoveSpeed=[0,1] × moveSpeed` 自洽，**零程式改動**（驗證資產決定規格原則）
- [ ] **使用者 Inspector 作業**：`Locomotion.asset` 擴 4 children（Idle 0 / Walk 0.265 / Run 0.574 / Sprint 1.0；Sync 開 Walk/Run/Sprint）＋`MotionDriver.moveSpeedSource` → `Bake_SprintFwdLoop`。Play：按 W 以 Sprint 6.10 前進無滑步（中間 tier 待 B9/analog）
- [x] **Phase D B9 參數平滑 ✅**（`CharacterPipelineRunner`：SmoothDamp 平滑 MoveSpeed＋減速保留方向；Runner-local、零 GC、FSM 零改動；手感 tunable moveSpeedAccel/DecelTime）→ **待 Play 實測調手感**
- [ ] Phase C Starts/Stops/Turns 導入（烘焙曲線驅動＝Roll 先例；per-clip 套 preset）＋**承載方式實測定案**

---

## Backlog / Future Work（超出目前範圍，不動手）

### Foot IK 品質路線圖 → 已凍結並移交 `docs/03-animation-roadmap.md`
- **v1 已凍結（收案輪，2026-07-21）**：架構健康＋6 條已知限制（L1~L6）文件化於 dev-spec §3.5.2；品質升級順序、技術分類、依賴關係全數移交 roadmap `docs/03`（輪 2 Locomotion 資產 → … → 輪 7 Foot IK v2 雙點採樣）。
- **~~首查項 GetIK* 值域~~ 已否證**：樓梯歪斜真凶＝斜坡 collider（環境資料錯誤，collider 修正後消失）；殘餘跨階腳掌穿模＝L1 單點採樣資訊量天花板，升級＝輪 7 Heel/Toe 雙點採樣。A/B 旋轉公式無感差、已回歸保留俯仰式（changelog v0.18.7）。
- ⚠️ 參考碼防搬運註記（仍有效，動 IK 前重讀）：其 raycast 從骨骼現值起打＝反饋污染（我們 M3.1 修掉的抽搐根因，快照 goal 起點勿退）；其 body 直接覆寫不適用（我們疊加式）；其漏設 RotationWeight 屬原 bug。骨盆模型重評（`bodyY − (minFootGoalY + legHeight)` 以腿長可達性直接建模）併輪 7 評估。

### 使用者明定 Future Work（M3 裁決重申：需要時一律 TODO，不得提前實作）
- **Foot Phase Curve**（烘焙腳相曲線；等 Footstep／Audio 輪一併評估 Mixer 混合取值）
- **Footstep Event ＋ Audio Integration**（腳步音；事件源設計與 Foot IK pose 採樣天然銜接）
- **BlockIK／BlockAudio Writer ＋ Mini Arbiter**（F6 ArbiterPipeline 範疇，順序 4.5 已預留）
- **Animation Rigging Package／Two-Bone IK Solver**（現用 Unity Humanoid IK，Q1 裁決）
- **Motion Warping**（`ApplyBakedCompensation` 已有雛形，無呼叫端）
- **F2 Strafe 2D Mixer**（等瞄準/鎖定移動需求）／**F3 Combat**／**F4 Upper Body Layer**

### 工具/演算法 Backlog（沿革見 changelog）
- **B1** `Mathf.DeltaAngle` 疑慮（≥360° 旋轉動畫進場時重評）
- **B2** 多段跳空中段前搖落地邊角（併 ADR-002 §6-4 後續）
- **B3** `PlayWithCallback` lambda 閉包 GC（仍無呼叫端）
- ~~**B4** Config bakeMappings 冗餘條目~~ ✅ 已收掉（使用者清理，現僅 Roll 一條）
- **B5** CapsuleFitter v2（骨骼推估）
- **B6** ValidateHierarchy 增補 Model identity 警告
- **B7** 前搖期間輸入未鎖手感（F6 範疇）
- **B8** Loop Pose 評估（走路循環有接縫時啟動）
- **B9** 動畫參數平滑（Game Feel 輪：SmoothDamp 落點裁決＋加減速曲線）
- **B10** Facade 映射鍵 Editor 驗證工具（低優先）
- **B12** Config 引用驗證（OnValidate 抓「條目存在但引用死」；JumpState/RollState 執行期防線已覆蓋主要風險）

---

## 建議下一步 → 權威輪次順序見 `docs/03-animation-roadmap.md` §3

- **輪 2（＝既定 M4）購入 locomotion 資產**（Movement Animset Pro 級別）→ 左/右腳停步、pivot、方向性起步＋foot-phase 資料設計（烘焙管線加 analyzer 即可）；一併做 Foot Phase 烘焙 stage 與 B9 參數平滑（資產定形後）
- **輪 4 ArbiterPipeline**（順序 4.5 兌現；BlockIK/BlockAudio writer 到位、§7/§8.3 旗標粒度屆時有真實案例可答）→ Combat 前置
- **輪 6（＝既定 M5）Combat 初版（ARPG）**→ 產生 Hit/Death 等真正需要「封鎖」的狀態（前置：輪 4 Arbiter＋輪 5 Upper Body Layer）
- 表情模組：暫緩（X Bot 無臉部 rig）
