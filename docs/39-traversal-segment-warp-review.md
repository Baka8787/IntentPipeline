# 39 — Climb2m Segment-Warp Prototype 架構審查 ＋ Formalization Plan

| 欄位 | 內容 |
|---|---|
| 日期 | 2026-09-20 |
| 性質 | **一次性 review ＋ 正式化計畫，非規格正本**。依 `docs/16-review-protocol.md` 執行 |
| 審查對象 | 工作樹未 commit 的 Climb2m segment-warp prototype（`MotionDriver` / `TraversalState` / `TraversalHandIKController` / `TraversalWarpPlanTests`） |
| 對照基線 | `docs/30` §C／§F（I1–I10）、`docs/02-dev-spec.md` §3.2 執行 API、ADR-008（🟡 **Trial**） |
| 動作 | ⛔ 本輪只讀不改程式。所有修法都是建議，未實作 |

---

## 0. 結論（先讀這段）

**Execution model 成立，值得升格；但現在這個形狀不能直接升格。**

- ✅ **可以升格的核心**：`committed landmarks 固定 ＋ phase boundary 取 actual root rebase ＋ baked delta ＋ compensation`。
  這是一個「分段收斂到固定錨點」的模型，誤差在每個 boundary 歸零、不跨段累積，而且沒有引入任何新的環境查詢。
  它與 UE Motion Warping 的 per-window warp target 是同一類模型，不是我們發明的東西。
- ⛔ **仍是 prototype hack 的部分**：六個 exception 裡只有 **1 個**可以原樣保留，2 個要改語意，3 個必須刪。
- 🔴 **最重要的一件事**：本輪用來支撐決策的數字裡，**至少一個是被污染的量測**（見 F1），
  另一個（residual guard 0.5 m）**是另一個 exception 的症狀，不是獨立發現**（見 F8）。
  在修好量測之前，「1.6/1.8/2.0 都穩定」這個結論的可信度低於它看起來的樣子。

---

## 1. Findings（依 `docs/16` §1 的順序）

### ① Correctness

#### F1 — `BLOCKER`：「最大 blocked displacement 2.99 cm」不是攀爬的證據，是 post-exit 貼地力被地板吸收

| | |
|---|---|
| **File** | `Assets/Scripts/Presentation/Motion/MotionDriver.cs:151`、`:1326`、`MoveTraversalAndRecord` |
| **Evidence** | `reboundForce = -2f`（`:151`）。prototype 在 exit 之後另外發一次 `MoveTraversalAndRecord(Vector3.up * (reboundForce * frameDeltaTime), …)`（`:1326-1328`），這一次 Move **也被計入** `LastTraversalMaxBlockedDisplacement`。角色此時已坐在落腳面上 ⇒ 這 2 m/s 的向下位移**必然 100% 被擋**。`2 m/s × dt = 0.0299 m ⇒ dt ≈ 0.01495 s`（≈67 fps），與回報值完全吻合。 |
| **決定性證據** | 三個高度回報 **2.99 / 2.99 / 2.99 cm**，逐位元相同。攀爬碰撞不可能對 1.6 m 與 2.0 m 產生相同到小數第二位的結果；一個與高度無關的固定量只能來自與高度無關的來源。 |
| **Why it matters here** | 這個數字現在被當成「碰撞沒有擋住 root」的主要證據，而且它的姊妹指標（root error ≤ 10 cm）是 IK residual guard 放寬的**啟用條件**（`TraversalHandIKController.cs:198-201`）。用被污染的代理量去支撐架構決策，正是記憶中 `measurement-proxy-mismatch` 那一類錯誤。**真正的攀爬段 blocked displacement 目前是未知數，不是 2.99 cm。** |
| **Smallest fix** | `MoveTraversalAndRecord` 拆兩個累加器（segment 位移／post-exit grounding），或 rebound 走一條不計入統計的路徑。**這一步必須先做，否則後面每一步的驗收都不可信。** |

#### F2 — `HIGH`：prototype 讓 Climb2m 的 `RootReachConstraint` 在段內失效

| | |
|---|---|
| **File** | `TraversalWarpPlan.cs:662`（`TryEvaluateOriginal`）vs `:727-734`（`TryEvaluatePiecewise`）；`MotionDriver.cs:1428-1441` |
| **Evidence** | reach constraint **只**在 `TryEvaluatePiecewise` 內被套用（`position += Quaternion.AngleAxis(yaw, Vector3.up) * localReachCorrection`）。prototype 的逐幀位移走 `TryEvaluateOriginal` — 該函式**完全不認識** reach constraint。它只在 `EnsureClimb2mPrototypeSegment` 取 segment **端點** desired 時經過 `plan.TryEvaluate`。⇒ **端點對得上，段內軌跡被換成「baked ＋ 線性 compensation」的直線混合。** |
| **Why it matters here** | Climb2m 是三支 bake 裡**唯一**開著 reach constraint 的（`docs/30` 附錄 B，ratio 0.95）。那條 constraint 是 `docs/24` §17 為了把 reach peak 從 **1.048 壓到 0.957** 而加的——也就是說，沒有它，Climb2m 的手在段內**本來就會超過可及範圍**。prototype 把它的段內形狀拿掉了，而那個時間窗**正好**就是 residual guard 被迫從 0.35 放寬到 0.5 的窗。**F2 與 exception #6 極可能是同一個原因。** |
| **Smallest fix** | compensation **疊在** `plan.TryEvaluate(t)` 的結果上，而不是取代它。也就是 segment 的 baked 基底改用 piecewise 求值（含 reach），compensation 只補「actual start 與 committed end 之間的差」。 |

#### F3 — `HIGH`：compensation 線性分配 ⇒ segment boundary 有速度不連續

| | |
|---|---|
| **File** | `MotionDriver.cs:100-111`（`EvaluateCompensatedDelta`，fraction 線性）vs `TraversalWarpPlan.cs:719`（`float smooth = t * t * (3f - 2f * t)`） |
| **Evidence** | production 的 knot 內插是 smoothstep；prototype 的 compensation 是常速率。段與段的 compensation 向量不同 ⇒ 邊界瞬間速度跳變 = `(comp_next − comp_prev) / span`。 |
| **Why it matters here** | 驗收項寫的是「no large snap at segment boundary」，而量到的是**位置**。位置連續、速度不連續在畫面上就是頓挫，而且它的量級**正比於 compensation** ⇒ 在 compensation 最大的低牆（**未測**的 1.26 m）最嚴重。 |
| **Smallest fix** | fraction 改成 smoothstep 的差分：`S(t₁) − S(t₀)`，其中 `S(x)=x²(3−2x)`。一行。 |

#### F4 — `MEDIUM`：fall-through 會重複套用 yaw 與 `SyncGroundedState`

`MotionDriver.cs:1219-1220` 先 `transform.Rotate(...)`；若 `TryExecuteClimb2mSegmentPrototype` 回 `false`（`:1237` 註解明寫 fall through），會落到 `:1256-1258` **再轉一次**，`SyncGroundedState` 也跑兩次。
目前 Climb2m 的 authored knot times 不會觸發，但 fall-through 正是「輸入不合法」那條路徑——最需要可信的那一條。**最小修法**：prototype 分支移到 rotate 之前，或分支內不動 transform。

#### F5 — `MEDIUM`：post-exit rebound 變成同一幀的第二次 `Move()`

production 把 rebound 折進同一個 `movementDelta`（`:1253`）；prototype 另外發一次 `Move`（`:1326-1328`）。兩次 `Move()` 各做一次 sweep／depenetration，**不等價**於一次合成位移，並直接造成 F1 的雙記。

#### F6 — `MEDIUM`：Transfer→Exit 沒有下游 boundary 可以吸收誤差

模型的誤差歸零機制是「下一個 boundary 重新 rebase」。**最後一段沒有下一個 boundary** ⇒ 該段被擋掉的位移直接變成落點誤差，永久。
目前實測 ≤3.14 cm 看不出來，但這是**模型本身的邊界**，必須寫進 invariant，不能靠運氣。

### ② Contract violations

#### F7 — `HIGH`：I7 已被打破，文件與 dev-spec 仍寫著舊的

- `docs/30` §F **I7**：「plan 是絕對 pose 的函數；被碰撞擋掉的位移不回授進 plan」— 後半句仍成立，**前半句已不成立**。
- `docs/02-dev-spec.md:813`：「`ExecuteTraversalWarpedMovement(...)` **每幀評估 warped absolute pose**」— 對 Climb2m 已是錯的。
- 這是 CLAUDE.md「Code／Documentation Fold-back」的債。⚠️ 但**不需要開新 ADR**，見 §5。

#### F8 — `HIGH`：residual guard 0.5 m 在掩蓋 exception #2，不是在容忍 rig

| | |
|---|---|
| **File** | `TraversalHandIKController.cs:192-201` |
| **Evidence** | guard 比對的是 **pre-IK** 的 `LeftResidualError`（`_poseData.LeftHandPosition` ↔ target），**不是**回報的 solved residual（3.5–3.9 cm 是後者）。而 Entry→Contact 段的 Y **完全不做環境適應**（`MotionDriver.cs:1382`：`desiredEnd.y = actualSegmentStart.y + bakedSegmentDelta.y`）⇒ 在 contact 瞬間，root 高度與牆高無關。 |
| **推導** | Climb2m 的 clip 為 ~2.0 m 牆 author（baked 垂直總位移 1.9781 m）。1.6 m 牆的 committed grip target 比動畫手低約 **0.4 m** ⇒ pre-IK residual ≈ 0.4 m ⇒ 舊門檻 0.35 不夠、0.5 剛好夠。**guard 的經驗區間 `[0.35, 0.5)` 與這個 0.4 m 完全吻合。** |
| **可證偽的預測** | band 下緣 **1.26 m** 的 delta 約 **0.72 m > 0.5** ⇒ **現行 prototype 在 band 下緣應該會直接掉 IK**。這是 harness 可以立刻驗的一條。 |
| **結論** | **0.5 是 Y policy 的函數，不是 rig 的函數。** 順帶：原本的 0.35 在 `docs/` 裡也**查不到任何推導**（grep 過），它本來就是無主的數字。 |

### ③ Dependency direction — ✅ **沒有 finding**

逐條對照過，全部乾淨：
- Probe 仍是唯一 environment physics query owner（prototype 未新增任何 `Physics.` 呼叫）。
- `CharacterController.Move` 仍是唯一 root position writer；新 struct 只算數，不寫 transform。
- HandIK 仍只讀 `MotionDriver.ActiveTraversalPlan`、只寫骨骼。
- boundary rebase 讀的是**角色自己的 `transform.position`**，不是環境 ⇒ **不違反 I1／I5**（見 §2 Q1 的語意論證）。
- `TraversalSegmentWarpPrototypeSegment` 是 `readonly struct`、零配置，符合 Zero GC。

### ④ Decision ownership

#### F9 — `HIGH`：用 `Kind == Climb2m` 做 execution-model 決策

`TraversalState.cs:342`：`bool enableClimb2mPrototype = _committedCandidate.Kind == TraversalKind.Climb2m;`
`Kind` 是 **classifier 的輸出（分類結果）**，不是執行策略的擁有者。ADR-008 **D1** 的原話是「kind 是資料，不新增三個 state subclass」——用 kind 當行為開關，等於把 subclass 的分支搬進 if。
**Smallest fix**：改成 binding／bake 上一個 authored 的 `executionModel`（`Absolute` / `Segmented`）。這同時是 §5 revert 路徑成本低的關鍵理由。

#### F10 — `MEDIUM`：Transfer target 仍是 hand-derived 的間接函數（直接回答 Q4）

`TraversalStateParamsSO.cs:297-302` 的 `heldTransferKnot` **直接沿用** `rightKnot.PositionCorrection`。
prototype segment 1 的 `desiredEnd = plan.TryEvaluate(transferT)` ⇒ **仍然含有那份 contact correction**。
也就是說「Transfer 不再維持固定 correction」只在**執行期**成立；**資料層的 Transfer target 依然是 contact solve 的函數**。

### ⑤ Scope

#### F11 — `LOW`：工作樹混了兩個不相干的主題且都未 commit

本批同時含 2026-09-19 已驗收的 debug panel／hand-target 可視化（`RuntimeDebugPanel`、`TraversalHandIKController` 的 LineRenderer、`GrabSphereRadius` 0.11→0.04）與 Climb2m prototype。
CLAUDE.md 2026-09-16「每完成一個段落就 commit」正是為此（當時的教訓是 244 檔 × 5 主題）。**建議先把已驗收的 debug panel 切出來 commit**，讓 prototype 有一個乾淨的 revert 點。

### ⑥ Tests

#### F12 — `HIGH`：三條新測試守的是 struct 算術，沒有一條守 execution model

`TraversalWarpPlanTests.cs:542-608` 的三條全部只測 `TraversalSegmentWarpPrototypeSegment` 的純算術。
其中 `PrototypeSegment_DeltaDoesNotReadActualPositionPerFrame` **名不副實**——它測的是「同輸入同輸出」，而「不逐幀讀 actual position」這個事實在 `MotionDriver` 裡，不在 struct 裡。名字宣稱的不變量沒有被守到。
**目前沒有任何測試守**：rebase 只發生在 phase boundary、compensation 有上限、segment boundary 的速度連續性、IK 在 contact→transfer 不掉權重。

---

## 2. 逐題回答

### Q1 — 這個 execution model 是否成立？ ✅ **成立**

**actual-root rebase 是否仍符合 committed 語意？—— 是。**

判準應該是：**commit 之後，沒有任何「新的世界觀測」可以改變結果。**
`transform.position` **不是新的世界觀測**——它是我們自己上一幀執行的結果，是**執行誤差**，不是環境資訊。
landmarks、timing、contact targets 全部沒變 ⇒ 在 `docs/18` §3 的分類下**仍然是 committed motion**，不是 continuous。
決定性的差別：continuous motion 會因為環境改變而改變**去哪裡**；segment rebase 只改變**怎麼過去**，去哪裡是 commit 當下就定死的。

**「plan 是常數」應重新定義成**（採用使用者提的版本，但要更精確）：

> **committed landmarks／timing／contact targets 在一次 traversal 內是常數。**
> **execution 的每一段以 actual root 為起點、以 committed landmark 為終點；段內是開迴路。**

而**不是**「traversal 全程 absolute world pose trajectory 必須完全固定」。

但這個改寫必須**同時**補三條護欄，否則它會變成一張空白支票：
1. **rebase 取樣點只能是 phase boundary**，且每次 traversal 的取樣次數是有限且已知的（目前 = 3）。
2. **每段 compensation 必須有上限**，超限要有 rejection reason（接上 I8），不得靜默分配。
   目前唯一的守門員是 `MaxCommittedFrameDisplacement = 10f`，那不是品質閘門。
3. **最後一段的誤差不可回收**，必須明寫（F6）。

**要改的 invariant**：I6 改述、I7 拆成兩條、新增 I11／I12。**需要改 ADR-008**（詳見 §4／§5）。

### Q2 — Entry→Contact 的 Y policy ⇒ 選 **C（support-aware vertical target）**

⛔ **不要照抄 prototype 的 A。** 理由：

| 選項 | 判斷 |
|---|---|
| **A. 保留「Y 只跟 baked delta」** | ❌ **這正是 exception #6 的成因**（F8）。它讓 contact 瞬間的 root 高度**與牆高完全無關** ⇒ 把整個垂直適應推給 IK，而 IK 的容忍度只好被調大。它在 1.6–2.0 成立，是因為誤差剛好還在手臂能搆到的範圍內；band 下緣 1.26 m 預測會直接失效。 |
| **B. authored contact-root landmark** | ❌ 解不了根本問題。同一支 clip 要服務 **(1.25, 2.1] m** 的整個 band，一個 authored 常數對其中任何一個高度都是錯的。 |
| **C. support-aware vertical target** | ✅ **建議採用。** |
| **D. 其他** | — |

**C 的具體做法（重點是：不新增任何 query，全部用已 committed 的資料）**：

保留 committed 的 grip 解算結果，**只在它會把膠囊壓進支撐面時往上夾**：

```
contactRootY = max(committedContactRoot.y, supportPlaneY + capsuleBottomOffset)
```

`supportPlaneY` 來自已經在 committed candidate 裡的 entry 站立面，不需要新的 Physics。
夾持量要記成**可觀測的 reason**（接 I8），而不是靜默調整。

🎯 **這個手法我們已經有先例**：`TraversalStateParamsSO.cs` 的 `ClampOutsideWall` 對**水平**方向做的就是這件事——
「保留 hand solve 的結果，只在它會把膠囊推進牆裡時沿法線夾住」。
**垂直方向照抄同一個手法即可，不需要發明新概念。** 這也讓 §C 的 landmark 表維持同構。

### Q3 — Contact→Transfer 的 held-correction invariant ⇒ **過度嚴格，但新寫法不能只寫 tolerance**

**舊 invariant 確實過度嚴格。** 它的理由（`TraversalStateParamsSO.cs:288-296`）是「沒有 IK 釘住手時，手會被 root 拖著滑」——
括號裡那句「**沒有 IK 釘住手時**」現在已經不成立了。前提消失，結論就不該繼續當鐵律。

但**不能**直接改成使用者提的三行版本，因為第二行「hand residual 必須在 tolerance 內」留了一個沒有單位的洞。建議：

> 1. **Contact world target 在 Contact→Transfer 期間固定。**（不變，這條是硬約束）
> 2. **root correction 可以改變**，但 Contact→Transfer 的**累積相對位移必須在該 clip 的 reach budget 內**
>    （用已存在的 `RootReachConstraint.maximumReachRatio`，**不是**新的公分門檻）。
> 3. residual 超出 budget 時的處置是**可觀測的降級 ＋ reason**，⛔ 不是放寬門檻。

⚠️ **一個 prototype 沒有證明的東西**：它證明了 IK 能把**位置**殘差壓到 4 cm、weight 維持 0.989–0.998。
但**沒有任何人量過 shoulder→wrist 距離 vs 手臂長度**。
兩骨 IK **一定**會給你一個解——問題是那個解可能是手肘反折或手臂拉直。
**「解得到」≠「看起來對」。** 這是 §3 harness 無法覆蓋、必須留給人工 Play 的那一項。

**這是否更符合「視覺穩定優先，不要求數學 rigid pivot」？** — 是，但要誠實：
視覺穩定的**成本**從「root 不准動」轉移到「手臂被拉伸」。這個成本目前**沒有被量測**。

### Q4 — Transfer target 的 ownership ⇒ **要獨立 landmark，但只缺一個**

不需要三個新欄位。盤點現況：

| Landmark | 現在有沒有獨立來源 | 判斷 |
|---|---|---|
| **ContactRootTarget** | ✅ 有 — `primaryGripTarget` 由 `ledge.PointAt(...)` ＋ `gripEdgeInRoot` 反解 | 保留，套 Q2 的垂直夾持 |
| **TransferRootTarget** | ❌ **沒有** — 直接沿用 contact correction（F10） | 🎯 **這才是真正缺的那一個** |
| **ExitRootTarget** | ✅ 有 — `exitTarget`（probe destination 與 bake natural depth 取近） | 保留 |

**建議的 TransferRootTarget**：由 bake 已有的 `transferAnchor.rootLocalPosition` ＋ committed ledge frame 推出的世界點。
這樣它與其他三個 landmark **同構**（時間屬 bake、位置屬世界），`docs/30` §C 的表四列一致，
而且 §C 目前那條「Transfer 為什麼刻意不指向 corridor clearance 點」的警告依然成立——
**新的 target 來自動畫自己的 anchor，不是來自 corridor evidence**。這兩件事不衝突。

### Q5 — Hand IK window ⇒ **混合模式，但 authored 優先；runtime override 必須刪**

prototype 的 `EvaluateClimb2mPrototypeWindow`（`TraversalHandIKController.cs:256+`）是 **runtime 偷改 authored 資料**：
Inspector 上顯示的 window 值與實際行為不符。這是「單一真相來源」的直接違反，也是本 repo 加重項之一。

**正式版**：
1. **時間**來自 bake（`leftHandContactNormalizedTime` / `transferNormalizedTime` **已經存在**）。
2. **形狀**來自 authored `HandIKWindow`（**已經存在**）。
3. 真正缺的只有一條 **authoring 慣例**：「window 的 hold 段預設撐到 transfer」。
4. ⇒ **最小修法 = 重新 author 三支 bake 的 window 值，程式端刪掉 override。零新程式。**

BBB-Nexus 的「fixed world hand target × authored HandIKWeightCurve」我們**已經有這個結構了**——
差的只是 authored 的值不對。**不需要因為參考案例而改架構，只需要改資料。**

### Q6 — Residual guard ⇒ **三個都不選；用已存在的 reach ratio**

**先回答使用者的疑問：是的，0.5 m 目前就是在掩蓋錯誤**（F8 的推導 ＋ F2 的機制）。
而且它掩蓋的是**兩件**事：exception #2 的 Y policy，以及 reach constraint 在段內失效。

比較四個選項：

| 選項 | 判斷 |
|---|---|
| rig-specific reach budget | 🟡 方向對，但我們只有一個 rig ⇒ 現在建 rig-level 抽象是提前抽象 |
| clip-specific residual budget | 🟡 方向對 |
| body-proportion-derived reach | 🟡 方向對 |
| 固定 conservative threshold | ❌ 這就是現況的 0.35。它的問題不是數字大小，是**單位錯了** |

🎯 **建議：直接用 `TraversalRootReachConstraint.maximumReachRatio`（Climb2m 已 author 為 0.95）當貨幣。**

理由——它同時滿足上面三個「方向對」的選項，而且**已經存在**：
- 它是 **rig-derived**：bake 時從 humanoid geometry 量出來的（`MotionBakeData.cs:209-211` 的註解原話）。
- 它是 **per-clip**：存在 bake block 裡。
- 它**已有 authoring 管道與驗證**（`docs/24` §17 用它把 peak 1.048 壓到 0.957）。

guard 應該問的是「**這個 residual 換算成 reach ratio 有沒有超過 budget**」，而不是「有沒有超過 0.5 m」。
公尺不是 reach 的自然單位——同一個 0.35 m 對不同姿勢、不同 rig 意義完全不同。
⛔ **不要引入第四個數字。我們已經有三個無主的公分門檻了。**

### Q7 — Animation coverage ⇒ **不能宣告 1.6–2.0，而且沒測的正好是最該測的**

**現況（磁碟值）**：

| | 值 | 來源 |
|---|---|---|
| Climb2m classifier band | **(1.25, 2.1] m** | `X Bot.prefab:4582-4583`（`climb1mMaxHeight 1.25` / `climb2mMaxHeight 2.1`） |
| `climb1mEnabled` | **0**（關閉） | `docs/30` 附錄 B |
| Climb1m 的 `HasValidBakedData` | ❌ | 同上 |

⇒ 🔴 **Climb2m 目前單獨承擔整個攀爬能力。** 它的 coverage 就是遊戲的 coverage。

**已測 1.6 / 1.8 / 2.0 ＝ band 的中上段。** 而 §Q2 的機制告訴我們：
**compensation 隨牆高下降而單調增大**（1.6 m ≈ 0.4 m；1.26 m ≈ 0.72 m）
⇒ **failure mode 最嚴重的區間正好完全沒測**，而且 F8 已經給出一個可證偽的預測（band 下緣應該直接掉 IK）。

**正式 coverage 還需要**：

| 維度 | 需要的驗證 | 優先級 |
|---|---|---|
| **更低高度** | **1.26 / 1.4**（band 下緣） | 🔴 **最高** — F8 預測這裡會壞 |
| **2.1 m 上限** | band 上緣 | 🟠 高 |
| **entry distance** | 合法帶 `[0.06, 0.81]` 至少三點（近／理想 0.4615／遠） | 🟠 高 |
| **obstacle depth** | 跨過 `exitTarget` 取 natural(0.2129) vs probe 的分支點 | 🟡 中 |
| **facing / lateral offset** | 影響 `ClampOutsideWall` 與 tangent clamp | 🟡 中 |
| **不同 rig** | ⛔ **先不做** — 只有一個 rig，第二個使用者出現前不建抽象 |

### 測試 formalization ⇒ ✅ **值得升格，但 assertion 要守不變量，不是守數字**

⚠️ 先說清楚現況：`Assets/_Project/Tests/PlayMode/TraversalStatePlayModeTests.cs` 大量是 `[Test]` ＋ fake driver，
`TraversalAuthoredGeometryTests.cs` 有真 `CharacterController` 但**沒有 Animator**。
⇒ **「真實 rig harness」在這個 repo 裡是新東西**，不是把既有測試改個參數。這是升格的真實成本，要先認。

建議的 assertion（全部避開「當次量到的值」）：

| 守什麼 | 怎麼寫 | ⛔ 不要寫 |
|---|---|---|
| traversal completes | 離開 `StateType.Traversal` ＋ `normalizedTime` 達到 exit | — |
| max root divergence | ≤ budget，**budget 由 bake／binding 提供** | ⛔ `Assert.Less(err, 0.0314f)` |
| contact-window hand residual | ≤ **reach ratio** budget | ⛔ 公分門檻 |
| segment boundary 連續性 | **位置 ＋ 速度**跳變都 ≤ budget（F3） | ⛔ 只看位置 |
| IK 不早退 | contact→transfer 全程 weight > 0 | ⛔ assert 0.989 |
| blocked displacement | **只算 segment 段**，排除 post-exit rebound（F1） | ⛔ 用現在這個被污染的欄位 |

⛔ **不要 assert「3.68 cm」這類當次量測值**——那會讓回歸測試變成噪音產生器，一調參數就紅。

---

## 3. 正式資料模型

| 欄位 | 去哪 | 說明 |
|---|---|---|
| contact／transfer／exit／recovery times | 🟢 **Bake（已有）** | 不動 |
| `gripEdgeInRoot` L/R | 🟢 **Bake（已有）** | 不動 |
| `transferAnchor.rootLocalPosition` | 🟢 **Bake（已有）** | 🆕 **改為 TransferRootTarget 的來源**（Q4）——欄位已存在，只是沒被用在這裡 |
| `rootReachConstraint`（含 `maximumReachRatio`） | 🟢 **Bake（已有）** | 🆕 **升格為 residual budget 的單位**（Q6） |
| `handIKWindow` L/R | 🟢 **Bake（已有）** | 🆕 **重新 author 到 transfer**；刪 runtime override（Q5） |
| 每段 compensation 上限 | 🟢 **Bake（新增）** | 從該 clip 的 reach headroom 推導，不手 author |
| `executionModel`（Absolute / Segmented） | 🔵 **Binding／config（新增）** | 取代 `Kind == Climb2m` 硬編（F9）。Vault1m／Climb1m 明確留 `Absolute` |
| compensation 上限 override | 🔵 **Binding（可選）** | 只在 bake 推導不合用時 |
| 當前 segmentIndex ／ segment 五元組 | 🟣 **Runtime state** | 只活在一次 traversal 內，`MotionDriver` 私有 |
| `desiredPosition` ／ `rootError` | 🟣 **Runtime state（debug only）** | ⛔ **不得有任何 gameplay 讀者**。目前 `TraversalHandIKController.cs:199` **已經有一個**（root error ≤ 0.1 當 guard 條件）——那是 Q6 一併要刪的東西 |
| 以上任何一項 | ⛔ **不得進黑板** | 它們是 Presentation 的執行細節 |

---

## 4. Invariant 增修（`docs/30` §F）

| # | 動作 | 內容 |
|---|---|---|
| I1–I5 | ✅ **保留不動** | Probe 唯一 query owner／authored capsule／不寫黑板／policy 純函式／commit 後不再 query。**segment rebase 全部不觸碰這五條**（見 §1 ③） |
| **I6** | 🔧 **改述** | ~~plan 在一次 traversal 內是常數~~ → **committed landmarks／timing／contact targets 在一次 traversal 內是常數** |
| **I7** | 🔧 **拆兩條** | **I7a**：執行分段；每段起點取 actual root，終點是 committed landmark。**I7b**（保留原意）：被碰撞擋掉的位移**不回授進 plan**；它只會在**下一個 phase boundary** 被吸收，**最後一段沒有下一個 boundary ⇒ 該段誤差是永久的**（F6） |
| **I11** | 🆕 **新增** | **rebase 取樣點只能是 phase boundary，且只能讀角色自己的 `transform.position`。** ⛔ 不得讀 Physics、不得讀 Probe、不得在段內重取樣。每次 traversal 的取樣次數有限且已知 |
| **I12** | 🆕 **新增** | **每段 compensation 有上限；超限必須留下 rejection reason**（接 I8）。⛔ 不得靜默分配任意大的 compensation |
| I8–I10 | ✅ 保留不動 | |
| §C Transfer 那一列 | 🔧 **改寫** | 從「刻意沿用 Right Contact 的 correction」改為獨立的 TransferRootTarget（Q4）。**保留**下方那段「corridor 的四個 root 點是 query evidence，不是 motion target」的警告——它仍然成立 |
| `docs/02-dev-spec.md:813` | 🔧 **fold back** | 「每幀評估 warped absolute pose」對 Segmented 已不成立（F7） |

---

## 5. ADR ⇒ ⛔ **不要開新 ADR。改 ADR-008。**

**判斷過程**（依 CLAUDE.md 的 ADR 觸發條件）：

| 條件 | 命中？ |
|---|---|
| ①黑板 schema／ownership 變更 | ❌ |
| ②FSM 拓撲或 GameObject hierarchy 變更 | ❌ |
| ③管線順序或核心驅動介面變更 | ❌（`ExecuteTraversalWarpedMovement` 簽名不變） |
| ④**推翻既有架構不變量** | ✅ **I7** |

命中 ④ ⇒ **需要 ADR 層級的處置，不能只寫 Living Docs。**

**但 `ADR-008` 的 Status 是 🟡 `Trial`，不是 `Accepted`。**
CLAUDE.md 的 ADR Lifecycle 表明文寫著：Trial **不凍結**、**可依實作發現修訂（記入該 ADR 的修訂紀錄，不必開新 ADR）**。
⇒ ✅ **正確處置 = 在 ADR-008 內加一條修訂紀錄 ＋ 一個新的 Decision 條目**，而不是開 ADR-010。這同時符合反 ADR-explosion 規則。

**該修訂必須自帶**（Trial ADR 的強制項）：
- **Acceptance Criteria**：§2 Q7 的 coverage 矩陣全綠 ＋ §2 測試表的 assertion 全綠 ＋ 人工 Play（手臂視覺、boundary 頓挫）通過。
- **失敗處置 ／ revert 路徑**：`executionModel` 切回 `Absolute`。
  🎯 **這是 F9 的修法（authored flag 取代硬編 kind 判斷）最重要的價值——它讓 revert 從「改程式」變成「改一個資產欄位」。**
- **不凍結清單**：compensation 的分配曲線形狀、segment 的切分點數量、budget 的具體數值、debug 欄位組成 —— **全部下放 Living Spec**，ADR 只保留「landmarks 固定、execution 可在 boundary rebase」這個決策本身。

---

## 6. 必須先清掉的 prototype exception

| # | Exception | 處置 | 依據 |
|---|---|---|---|
| 1 | `Climb2m` 硬編啟用 segment warp | ⛔ **刪** → authored `executionModel` | F9；也是 revert 路徑的前提 |
| 2 | Entry→Contact 忽略 grip-derived absolute Y | 🔧 **改** → support-aware 垂直夾持（照抄 `ClampOutsideWall`） | Q2；它是 #6 的根因 |
| 3 | Contact→Transfer 不維持固定 correction | ✅ **保留** ＋ 補 reach budget 護欄 | Q3 |
| 4 | Transfer boundary 使用 actual-root rebase | ✅ **保留** — **這是整個模型的核心** ＋ 補 I11 | Q1 |
| 5 | Hand IK window override 至 Transfer | ⛔ **刪 runtime override** → 改 author bake 值 | Q5；單一真相來源 |
| 6 | residual guard 放寬至 0.5 m | ⛔ **刪** → reach-ratio budget | Q6；F8 |

**六條裡只有 #4 可以原樣保留。**

---

## 7. 正式化順序

> 🔑 **排序原則：先讓量測可信，再改行為。** 目前的數字裡有一個被污染、一個是別的 exception 的症狀。

| Step | 做什麼 | 完成後應該看到 |
|---|---|---|
| **0** | 修 F1（blocked 統計拆兩個累加器）＋ 加 segment boundary **速度**跳變的量測 | **攀爬段真實的 blocked displacement**（現在是未知數）。⚠️ 沒有這一步，後面每一步的驗收都不可信 |
| **1** | 修 F2（compensation 疊在 `plan.TryEvaluate` 之上，恢復 reach constraint）＋ F3（smoothstep）＋ F4 ＋ F5 | 🎯 **重跑 1.6/1.8/2.0，看 pre-IK residual 會不會自己掉下來。如果會，exception #6 直接消失，不需要任何新門檻。** |
| **2** | Q2 的 support-aware 垂直夾持 | exception #2 才有資格結案。重跑並**首次**加測 1.26 / 1.4 |
| **3** | Q6 的 reach-ratio budget 取代公分門檻；刪 0.5 與 root-error 耦合 | exception #6 結案 |
| **4** | Author bake 的 IK window 到 transfer；刪 runtime override | exception #5 結案。⚠️ 資產改動，走 Editor API |
| **5** | `executionModel` flag 取代硬編；Vault1m／Climb1m 明確 `Absolute` | exception #1 結案；**revert 路徑成立** |
| **6** | TransferRootTarget（Q4） | F10 結案；§C 表四列同構 |
| **7** | PlayMode 真實 rig harness ＋ Q7 的 coverage 矩陣 | F12 結案 |
| **8** | Fold back：I6/I7/I11/I12、`docs/30` §C／§F、`docs/02-dev-spec.md:813`、ADR-008 修訂紀錄、`docs/00-map.md` | 文件與程式一致 |

### 舊 path 什麼時候可以刪？

⚠️ **問題本身需要修正一個前提**：`ExecuteTraversalWarpedMovement` 的 absolute path **永遠不會被刪**——
**Vault1m 仍然在用它**，而且它是 Segmented 的 revert 目標。

真正會被刪的只有**「prototype 分支與 absolute 分支並存的那段 `if`」**（`MotionDriver.cs:1210-1237`），
它會變成一個由 `executionModel` 決定的乾淨分派。條件是：

> **Step 5 完成**（flag 取代硬編）**且 Step 7 全綠**（含 band 下緣 1.26 m）**且人工 Play 驗收通過**（手臂視覺、boundary 頓挫）。

在那之前，兩條路徑必須並存。

---

## 8. 最終回答

> **目前結果是否足以支持我們把 traversal 的核心理念從「absolute root plan 必須完全固定」改成「committed landmarks 固定，但 execution 可以在 phase boundary 用 actual root rebase」？**

## ✅ **是。但「足以支持」的範圍要講清楚。**

**足以支持的**：
- **理念本身的改寫**。committed 的語意從來就不該是「絕對軌跡不變」，而是「**結果在 commit 當下決定，之後的觀測不能改變結果**」。
  讀自己的 transform 不是新觀測，landmarks 沒變 ⇒ 仍是 committed motion。
  prototype 把 1.8 m 的 **12.7 cm** 與 1.6 m 的 **34.1 cm** 永久 divergence 消掉了——
  **那不是調參調出來的，那是模型層級的改善**，而且沒有付出任何一條 dependency direction 的代價。
- **改 I6／I7、改 ADR-008（Trial，可直接改）、把 Segmented 當作正式的第二種 execution model。**

**尚不足以支持的**：
- ⛔ **不足以宣告 Climb2m 的 coverage 是 1.6–2.0 m**——band 是 (1.25, 2.1]，而**沒測的下緣正好是 failure mode 最嚴重的地方**，
  F8 甚至給出了「那裡應該會壞」的具體預測。
- ⛔ **不足以保留任何一個 exception 的現行形狀**（只有 #4 的**概念**保留，實作仍要補 I11）。
- ⛔ **不足以宣告「視覺穩定」達成**——量到的是 root 與手的**位置**，沒有量過手臂被拉伸多少。那項只有人工 Play 能答。

**一句話**：**理念改對了，實作還是 prototype。** 先做 Step 0–1，很可能一半的 exception 會自己消失。

---

## 三段結尾（`docs/16` §3 強制）

### 🔴 Must fix before merge
| | |
|---|---|
| **F1** | blocked displacement 統計被 post-exit 貼地力污染 ⇒ **決策依據不可信** |
| **F2** | `RootReachConstraint` 在段內失效 ⇒ 很可能是 exception #6 的真正原因 |
| **F8** | residual guard 0.5 m 在掩蓋 exception #2 的症狀，且與 root error 耦合形成自我驗證迴路 |
| **F9** | `Kind == Climb2m` 硬編 ⇒ 違反 ADR-008 D1，且**擋住了低成本的 revert 路徑** |

### 🟡 Worth fixing later
F3（速度不連續）／F4（重複 yaw）／F5（第二次 `Move`）／F6（末段誤差不可回收，需入 invariant）／
F10（Transfer target 仍 hand-derived）／F11（工作樹混主題未 commit）／F12（測試名不副實、沒守 execution model）

### ✅ No issue（已對照架構確認的刻意偏離）
- **Dependency direction 全部乾淨**：Probe 仍是唯一 physics query owner；`CharacterController.Move` 仍是唯一 root writer；
  HandIK 仍只讀 committed plan、只寫骨骼；prototype 未新增任何 `Physics.` 呼叫。
- **boundary rebase 讀自己的 `transform.position` 不違反 I1／I5**——那是執行誤差，不是環境觀測（§2 Q1 的語意論證）。
- **`TraversalSegmentWarpPrototypeSegment` 是 `readonly struct`、零配置**，符合 Zero GC Runtime。
- **compensation 不跨段累積**：每個 boundary 誤差歸零，模型不會發散。

---

## 9. 外部參考實作對照：BBB-Nexus（2026-09-20 追加）

來源：`github.com/bunkerboy258/BBB-Nexus`（public，132 個 `.cs`）。全域搜尋四個符號的 **writer**（非 reference）。

### 9.1 Writer 盤點 —— 每個符號只有一個 writer 類別

| 符號 | 宣告 | **唯一 writer** | reader |
|---|---|---|---|
| `IsWarping` | `PlayerRuntimeData.cs:144` | `PlayerVaultState.cs:80` / `:147` | `IKController.cs:67` |
| `ActiveWarpData` | `:148` | `PlayerVaultState.cs:81` / `:148` | `IKController.cs:69` |
| `NormalizedWarpTime` | `:150` | `PlayerVaultState.cs:82` / `:115` | `IKController.cs:69` |
| `WarpIKTarget_LeftHand` | `:152` | `PlayerVaultState.cs:85` | `IKController.cs:72` |

### 9.2 完整呼叫鏈

```
[只在 JumpPressed 當幀，非每幀]
JumpOrVaultIntentProcessor.HandleJumpIntent
  └ DetectObstacle(): forward Ray(牆) → down Ray(ledge) → down Ray(落點)
      ├ LedgePoint        = (wallHit.xz, ledgeHit.y)
      ├ ExpectedLandPoint = landHit.point
      ├ Left/RightHandPos = ledgeEdge ∓ rightDir*spread/2 + ledgeBasis*authored offset
      └ HandRot           = LookRotation(−wallNormalFlat) * authored euler
  → data.CurrentVaultInfo                                            [commit]

PlayerVaultState.Enter
  ├ warpTargets = { LedgePoint, ExpectedLandPoint }        ← 只有兩個 warp point
  ├ MotionDriver.InitializeWarpData(SO, warpTargets)
  └ WarpIKTarget_L/R = CurrentVaultInfo.Left/RightHandPos  ← ⭐ 不經過 root solve

MotionDriver.UpdateWarpMotion(t)   每幀
  ├ CheckAndAdvanceWarpSegment: t ≥ WarpPoints[i].NormalizedTime ⇒
  │     SegmentStartPosition = _transform.position          ← ⭐ actual root rebase
  ├ CompensationVel = (Targets[i] − SegmentStartPosition − TransformVector(BakedLocalOffset))
  │                   / segmentSeconds
  ├ finalVelocity = TransformDirection(LocalVelocityXYZ.Evaluate(t)) + CompensationVel
  └ _cc.Move(finalVelocity * dt)

IKController（唯讀）
  if (IsWarping): w = ActiveWarpData.HandIKWeightCurve.Evaluate(NormalizedWarpTime)
                  if (w > 0.01f) SetIKTarget(L/R, WarpIKTarget_L/R, WarpIKRotation_Hand, w)
```

### 9.3 對本審查四個結論的意義

| 發現 | 對應 |
|---|---|
| ✅ **`SegmentStartPosition = _transform.position` ＋ `(realDelta − animDelta)/segmentSeconds` 與我們的 prototype 同構**（他們用速度、我們用位移），boundary 才 rebase、段內開迴路 | **獨立證實 §8 的結論**：這是已出貨的模式，不是我們發明的 |
| ⚠️ 他們的 `CompensationVel` 也是**整段常數** ⇒ 同樣有 boundary 速度跳變 | **F3 沒有先例可抄**，要改得自己改 |
| 🎯 **hand target 完全不經過 root solve**：`WarpIKTarget = ledgeEdge ± spread + authored offset`，從偵測幾何直接算 | **Q4／F10 的外部佐證** —— 他們天生沒有我們「Transfer target 間接依賴 hand-derived correction」的問題 |
| 🎯 **第一個 warp target 就是 `LedgePoint`，含 Y** | **Q2 選 C 的最強證據**。他們 contact 時 root 已被 warp 到真實邊緣、手的目標也在真實邊緣 ⇒ 兩者天生一致。我們刻意抽掉 Y（exception #2），才會在 1.6 m 量到 39.47 cm |
| 🎯 **完全沒有 residual guard**：`IKController` 只判 `w > 0.01f`，唯一權威是 authored `HandIKWeightCurve` | **Q5／Q6 的外部佐證** —— 我們的 0.35／0.5 在參考實作裡不存在，因為他們的 target 依構造就搆得到 |

### 9.4 ⛔ 不要抄的部分

- `InitializeWarpData` 與 `PlayerVaultState.Enter` 每次 vault 都 `new Vector3[]` —— 違反本專案 Zero GC Runtime。
- `animDelta = TransformVector(BakedLocalOffset)` 用 recalculate **當下**的 transform 旋轉；角色若在段內轉向就失準。我們逐幀取樣 baked curve，這點我們較好。

---

## 相關文件

- `docs/30-traversal-architecture-guide.md` §C（landmark authority）／§F（I1–I10）—— **本審查要改的正是這兩節**
- `docs/24-traversal-reference-study.md` §17（面約束、reach ratio 1.048→0.957）—— F2 的由來
- `docs/18-benchmark-convergence.md` §3 —— Committed／Continuous 分類，Q1 語意論證的依據
- `docs/ADR/008-traversal-integration.md` —— 🟡 **Trial**，可直接修訂（§5）
- `docs/16-review-protocol.md` —— 本報告遵循的格式
