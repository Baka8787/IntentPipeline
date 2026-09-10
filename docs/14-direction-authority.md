# 14 — Direction Authority（實作規格）

> **狀態**：🟡 **Trial ／ S1＋S2 程式已落地（2026-09-06，Codex），待 Unity Play 驗證**。
> 兩個 slice 的程式面完成、`dotnet build`（Runtime ＋ Tests.EditMode）**0 error**；
> **EditMode／PlayMode 實跑與 Play 驗收仍在使用者側**（Acceptance A–G 尚未打勾）。
> ⚠️ `Assets/Scripts/Core/Actions/ActionReleaseContext.cs` 是新檔 ⇒ **Unity 首次開啟會 import 並生成 `.meta`**（AI 不建 `.meta`，這是正常流程，不是遺漏）。
> 對應 **`docs/ADR/007-direction-authority.md`（🟡 Trial）**。
> ⚠️ **Trial ＝ 已裁決為實作基線，但尚未由第一個 slice 驗證**——引用本檔時須註明此狀態。
> 本檔是 ADR-007 的 Living Spec：**契約面、測試、切片、開放項全在這裡**；ADR 只保留五條決策。
> **Trial 期允許 code-first**：實作若推翻本檔的形狀（欄位、型別、承諾粒度），**改本檔即可**，
> 只有推翻 ADR-007 的 D1–D5 才需要回頭改 ADR。

---

## 1. 概念與語意

### 1.1 載體：現況 → 目標

| 概念 | 現況載體 | 問題 | 目標載體 |
|---|---|---|---|
| **MovementDirection** | `MovementIntent.DesiredDirection`（`Vector2`，**相機空間**）→ `MoveDirection`（同）→ 但**實際位移用 `transform.forward`** | 三層各說各話，最後由身體朝向決定 | `MovementIntent.DesiredDirection`（**世界**）→ `MoveDirection`（**世界**）→ `MotionDriver` **照著用** |
| **FacingDirection** | `MotionDriver.transform.rotation`，由 ①移動方向反推 或 ②`RequestFacing` 當幀信箱覆寫 | 三個送出者，優先序＝執行順序的巧合 | 每角色**單一 facing authority**；`transform.rotation` 寫入者仍恆為 `MotionDriver` |
| **AimDirection ／ AimPoint** | `AimResolver` 當幀 lazy 解算 ＋ `_latchedFacingDirection` 跨帧私有欄位 | 承諾住在表現層私有欄位；sink 各自重解 | `AimResolver` 只做**解算**（無狀態查詢）；**承諾住在承諾的擁有者**（`ActionState`）；sink **收到**方向 |

### 1.2 continuous vs latch（ADR-007 D5 的具體化）

| 概念 | 更新語意 | 誰決定 | 誰執行 |
|---|---|---|---|
| **MovementDirection** | **恆 continuous，永不 latch**。每幀由 producer 重算、由 model dynamics 平滑 | producer（`IMovementIntentSource`） | `MotionDriver` 積分 |
| **FacingDirection** | **預設 continuous**（跟隨移動方向）；**只在承諾作用域內 latch** | facing authority（承諾期間＝承諾） | `MotionDriver`（唯一 rotation 寫入者） |
| **AimDirection** | **解算 continuous**（per-frame lazy ＋ frameCount 去重，維持現況）；**一旦被承諾即凍結至作用域結束** | `AimResolver` 解算 ／ `ActionState` 承諾 | sink（收到什麼用什麼） |

📌 **「latch 一次」永遠指「鎖定目標方向」，不是「只送出一次」**——
`MotionDriver.ApplyFacingRequest` 每幀只消化一次請求並 slerp 一小步（`docs/11` §8.3 已記錄過這個誤讀）。

### 1.3 座標系規約

- 世界方向一律**水平化**（`y = 0`）後正規化；零向量＝**無方向意圖**（不是「朝 +Z」）。
- **建議型別 `Vector3`**（y 恆 0）而非 `Vector2`(XZ)：`Vector2` 的 `x/y` 讀起來像螢幕座標，是目前混淆的來源之一。
  ⚠️ 例外：**AimPoint／AimDirection 保留高度**（投射物要能上下瞄）；**facing 取其水平投影**。
  ⇒ **一份承諾，兩種投影**（ADR-007 D5 的實作面）。
- ⛔ **相機基底只出現在玩家 producer 內**。除 `PlayerLocomotionPolicy` 外，任何 producer 引用 `data.CameraTransform` 都視為回歸（A30）。

---

## 2. 契約面（最小改動清單）

### 2.1 S1 契約（movement 世界化）

| # | 檔案 | 改動 | 註記 |
|---|---|---|---|
| 1 | `Core/Blackboard/MovementIntentData.cs` | `DesiredDirection`：`Vector2`（相機空間）→ `Vector3`（**世界 XZ，y=0**）。doc comment 必須寫明參考框 | ADR-003 契約**細化**，非推翻 |
| 2 | `Core/Movement/PlayerLocomotionPolicy.cs:57` | `DesiredDirection = input.MoveInput` → **在此做相機投影**（讀 `data.CameraTransform`，壓平 forward/right，投影 `input.MoveInput`）。`CameraTransform` 為 null ⇒ 退化為「無方向意圖」 | 投影從 `MotionDriver` **搬家**到玩家 producer，不是新增機制 |
| 3 | `Core/Movement/AIMovementSource.cs:115-125` | **刪除**相機投影三行 ＋ `cameraForward/cameraRight` 局部變數；`worldDirection` 直接寫入。**並刪除 `ProduceIntent` 開頭 `data.CameraTransform == null` 的早退條件** | ⇒ **FU-6 結案**。⚠️ 早退條件不刪，敵人在無相機的測試場景會整個不動 |
| 4 | `Core/Movement/LocomotionSpeedSmoother.cs:55-70` | `_lastDirection`／`_direction`／`Direction`：`Vector2` → `Vector3` | 純型別置換，dynamics 一字不改 |
| 5 | `Core/Movement/Models/LocomotionModel.cs:120` | ✅ **核對即可，無需修改**——`data.MoveDirection = _smoother.Direction` 的指派具型別透明性，型別由 smoother 自動傳播 | 🔄 **2026-09-06 fold-back**：原文寫成「改動」，實作證明是誤判 |
| 6 | `Core/Blackboard/PlayerRuntimeData.cs:57` | `MoveDirection`：`Vector2` → `Vector3`（世界）。權限表語意同步（**writer 不變**） | A5 `WriterRules` **零改動** |
| 7 | `Presentation/Motion/MotionDriver.cs:148-170` | **刪除**相機基底六行；`horizontalVelocity = data.MoveDirection * currentSpeed`；無 facing request 時 slerp 目標改為 `data.MoveDirection` | ⛔ **procedural 路徑不得再出現 `transform.forward`**（A28） |
| 8 | `Editor/Pipeline/CharacterPipelineRunnerEditor.cs:94` | `Vector2Field` → `Vector3Field` | 編譯期會抓到 |
| 9 | 測試 | `MovementIntentTests`／`LocomotionStopTests`／`ArbiterPipelineTests` 中的 `Vector2` 常數改 `Vector3`；**新增方向投影的純函數測試** | 見 §6 |

**S1 不改**：`MotionDriver.RequestFacing`／`ApplyFacingRequest`／`AimResolver`／任何 sink／黑板 schema 的**欄位組成**。

### 2.2 S2 契約（承諾 ＋ release 交付）

| # | 檔案 | 改動 |
|---|---|---|
| 1 | `Core/Actions/ActionReleaseContext.cs` 🆕 | `readonly struct`：`AimPoint`／`Direction`（含高度）／`HasAim`。⚠️ 純資料，不得含 Unity 查詢 |
| 2 | `Core/Actions/IActionLifecycleSink.cs` | `void Release()` → `void Release(in ActionReleaseContext context)`。`Begin`／`Cleanup` 不變 |
| 3 | `Core/StateMachine/States/ActionState.cs` | 承諾**改由本類別持有**：在 `OnEnter` 與 `TryAdvanceChain`（＝每個帶 release 的段落邊界）各取得一次；`TryEmitRelease` 傳給 sink；`OnUpdateMotion` 送出承諾的**水平投影**作為 facing |
| 4 | `Presentation/Camera/AimResolver.cs` | **刪除** `_latchedFacingDirection`／`_hasLatchedFacing`／`TryLatchAutoTargetFacing`／`SubmitLatchedFacing`／`ClearLatchedFacing`（gizmo 的 latch 段一併移除）。保留 `TryGetAimPoint`（無狀態查詢）＋ aim-hold 的 `Update` 送出 |
| 5 | `Presentation/Actions/ThrowProjectileEmitter.cs` | **刪除** `aimResolver` 欄位與 `ResolveThrowRotation`；改用 `context`。無承諾 ⇒ fallback `transform.rotation`（＝現有 fallback，語意不變） |
| 6 | `Presentation/Actions/GroundEffectSink.cs:83-90` | 🔄 **2026-09-06 fold-back，寫法收斂**：不是「落點＝`AimPoint`」，而是**`context.AimPoint` 決定水平世界方向、`castDistance` 仍決定身前固定距離**——保留既有「從身前長出來」的裁決（`docs/11` §4.4），只是方向不再依賴 `casterRoot.forward` 恰好正確。無承諾 ⇒ 保留 `casterRoot.forward` fallback。**同時改寫該處註解的隱性依賴說明** |
| 7 | `Presentation/Actions/MeleeHitboxSink.cs` | 簽名跟改；**行為可維持不變**（hitbox 掛在身上、身體已朝承諾方向）。⚠️ 不要順手把近戰也改成用 aim point |
| 8 | 測試 | `ActionStateTests` 的 fake sink 簽名跟改；新增「同段內 facing 與 release 同向」測項 |

> **⚠️ facing 送出者維持一個**：S2 **不**讓 `ActionState` 直接呼叫 `MotionDriver.RequestFacing`。
> 建議在 `AimResolver` 留一個薄的 `SubmitFacing(in Vector3 worldDirection)` 轉送，
> **決定權在 `ActionState`（承諾擁有者）、送出權仍在 `AimResolver`**——維持 `docs/10` §4.3 的「唯一送出者」不破。
> 這條屬**不凍結**：若 S3 的 facing authority 提前落地，此轉送直接被取代。

### 2.3 S3 契約（facing authority 統一，形狀見 §3）

~~觸發條件（任一）：**敵人需要面向玩家**、**lock-on 落地**、或 **strafe 需要「非按住瞄準」的持續朝向**。~~

🔄 **2026-09-06：第三個觸發條件已成立 ⇒ S3 從「等未來」變成「Acceptance A 的前置」。**
使用者裁決右鍵保留給 Block／Guard、不要 Aim Mode ⇒ 拿掉 aim-hold 後，自由移動時的 combat facing 直接歸零。
**S3 重新定義為：Combat Context（黑板 region ＋ 黏性目標）＋ 單一 facing source（順序 4.6，唯一 `RequestFacing` 送出者）。**
⇒ 完整設計、候選比較、最小切片 S3a、右鍵退場清單全在 **`docs/15-combat-context.md`**（🔵 提案，未裁決）。
📌 S3 落地時一併移除 S2 的過渡設施 `AimResolver.SubmitFacing`（`docs/15` §6）。

---

## 3. Facing authority 的形狀（S3 提案，**不凍結**）

**優先序（authority 內部解決，不是多個送出者競賽）**：

```
① Action 承諾（最高：出手期間不被相機／輸入拉走）
② explicit target（lock-on）—— 未來才有，先留位
③ combat context target（黏性；🔄 2026-09-06 取代原本的「aim-hold 方向」）
④ 移動方向（預設）
⑤ 無 ⇒ 維持現有朝向
```

> 🔄 **2026-09-06 修訂**：原第 ③ 級是「aim-hold 方向」，隨「右鍵改留給 Guard、不要 Aim Mode」的裁決退場。
> ⚠️ **camera forward 與 soft target 不是優先序的層級**，它們是 ③ 的**目標選擇依據**——
> 把「依據」塞進優先序就會長回 `docs/09` §4-D3 禁止的那張 facing 優先級表。分析見 `docs/15` §2／§3。

| 候選 | 形狀 | 優點 | 代價 |
|---|---|---|---|
| **(a) 黑板 `FacingIntent` ＋ `IFacingIntentSource`** | 比照 ADR-003：每角色一個 active source，寫黑板一個 region，`MotionDriver` 於順序 6 消費 | 對稱、可 snapshot（netcode）、**由 A5 機器守住單一 writer**、敵人天然涵蓋 | 新黑板 region ＋ 新管線步驟 ＋ A5 加一列；且**順序有坑**：Action 承諾在順序 4（FSM）才決定，facing source 若排在 2.6 會看不到當幀承諾 ⇒ 需排 4.6 或讓承諾先落黑板 |
| **(b) ✅ 建議先做：單一 `CharacterFacingSource` 元件 ＋ 既有 `RequestFacing` seam** | 每角色掛一個（玩家版／敵人版），**pull** 上述五個來源後送出；`MotionDriver` 一行不改 | 零黑板變更、零管線變更；`docs/09` D3(c) 的紅線只需收窄不需推翻 | 承諾仍是跨帧非黑板狀態（住在 `ActionState`，比現況的 Presentation 私有欄位好，但仍不可 snapshot）；玩家版需要 Presentation 知識 ⇒ 住 `Presentation/` |

⇒ **建議 (b) 起步、保留 (a) 為升級路徑**，升級觸發條件寫死為：**netcode／replay 需求出現**，或 **facing 來源超過五個**。
⚠️ 升級到 (a) 時才推翻 `docs/09` §4-D3(c) 的「⛔ 不得為此新增黑板欄位」，並同步改寫該節。

---

## 4. 切片

### S1 — 移動方向世界化（**第一切片**）

**做完的定義**：§2.1 九列全部完成、EditMode 全綠、位移與朝向在物理上確實分離。
🔄 **2026-09-06 修訂**：原文寫「Play 可見**按住瞄準** ＋ 按 A ⇒ 朝目標、往世界左方移動」——
該驗收綁在即將退場的 aim-hold 上（右鍵改留給 Guard）。**最終語意改由 S3 驗收**（ADR-007 Acceptance A ／ `docs/15` §9）。
📌 S1 本身**已由使用者實跑觀察成立**（「右鍵時始終面朝前方沒問題」）——**分離的物理面沒有問題，缺的是朝向來源**。

**Play 觀察項（不是通過條件，是要看一眼並回報）**
1. ~~**R1 轉彎弧線**~~ → ✅ **2026-09-06 已觸發並處置**。使用者實跑回報的是**敵人移動抽搐**，不是玩家轉彎——同一個成因的另一面：
   S1 之前 `速度＝transform.forward` ＋ `Slerp(12·dt)`，**body slerp 實質上是 producer 方向的低通濾波器**；S1 拿掉別名，也同時拿掉了濾波器 ⇒ producer 的方向不連續直接變成位移抖動。
   **處置（已落地）**：方向 dynamics 加回 model（`LocomotionSpeedSmoother` 以 XZ 平面 `MoveTowardsAngle` 限速轉向，`LocomotionModel.directionTurnDegreesPerSecond` 預設 720°/s）。
   ⚠️ 刻意**不用** `Vector3.RotateTowards`：恰好 180° 反向時旋轉平面未定義、可能繞出水平面（敵人每 ~2.5 秒的側移翻向正是 180°）。已由測試釘住 `y == 0`。
2. **R2 滑行方向**：減速滑行期轉相機，角色不再跟著彎。**這是預期**，確認可接受。
3. **敵人**：移動相機時敵人的移動方向**不再改變**（＝FU-6 結案的肉眼證據）。
4. **預期的醜**：橫移時仍播前進動畫（滑步）。**這是刻意的中間狀態**，由 `docs/13` §7-2 的 2D mixer 解決。

### S2 — 承諾 ＋ release 交付（解 A6）

⚠️ **S1 與 S2 之間有一個已知退化窗口**：S1 解除別名後，`GroundEffectSink` 的 `casterRoot.forward` 落點會在
「身體朝向與瞄準不一致」時失準（ADR-007 R3）。**S1 與 S2 應連續執行，中間不要插入其他工作包。**

**做完的定義**：連段每一段的身體朝向與火球方向一致；`AimResolver` 不再持有任何跨帧承諾狀態；A29 綠。

### S3 — facing authority 統一

見 §2.3 觸發條件與 §3 形狀。**與 `docs/13` §7-2（2D mixer）互相獨立，可並行也可先後。**

#### S3b — 敵人也走 `CharacterFacingSource`；`MotionDriver` 退回純執行者（2026-09-08，程式落地）

> **狀態**：🟡 程式已落地、**尚未驗證**——Y Bot 的元件接線與 EditMode／Play 都還在使用者側。
> ⛔ 不得寫成已完成。

**問題**：`MotionDriver.ExecuteBaseMovement` 原本有一段
`if (!hasFacingRequest) Slerp(LookRotation(MoveDirection), 12f·dt)`，
與 `CharacterFacingSource` 的優先序 ③ 是**同一個決策的第二套實作**，且調參不同
（`12f` 無死區 vs `aimFacingTurnSpeed` ＋ `aimFacingAngleDeadzone`）。
玩家端該路徑已死，**敵人端仍在跑**（Y Bot 沒掛 facing source）⇒ 玩家與敵人依**不同且未宣告**的規則轉身，違反 D3。

**裁決（使用者，2026-09-08）**：(a)+(b) 都做——敵人也走 `CharacterFacingSource`，
目標是**`MotionDriver` 完全只負責執行 rotation**。

**落地形狀**——決策與執行沿「往哪面向／該不該轉」vs「怎麼轉」切開：

| 決策 | 擁有者 |
|---|---|
| 優先序（① commitment → ②lock-on 預留 → ③ MoveDirection → ④ 不送） | `CharacterFacingSource.TryResolveFacing` |
| **這幀該不該轉**（角度死區） | `CharacterFacingSource.ShouldRequestFacing` |
| **死區只適用哪種來源** | `CharacterFacingSource.Tick` |
| 轉多快（`aimFacingTurnSpeed`） | `MotionDriver`（執行參數） |
| 寫 `transform.rotation` | `MotionDriver`（唯一寫入者，D3 未變） |

**🔴 為什麼死區必須跟著決策一起搬走**（這是本切片最容易做錯的一步）：
死區的存在理由是「出手／Throw loop 半蹲、腳掌釘地，連續微轉看起來像腳被拖著扭」——
那是**站定轉身**的性質。若把它留在執行者身上，刪掉 fallback 之後，敵人的
**移動朝向**會突然套上 Y Bot 序列化的 **15° 死區**，沿 NavMesh 追人時會產生
「轉到誤差 15° 內就停住 → 飄出去 → 猛轉」的**極限環 ⇒ 看起來就是抽搐**。
⇒ **死區只套用在優先序 ①（Action commitment）；優先序 ③（movement）一律逐幀送出請求。**

**不變量**：新增 **A32**（`MotionDriver.ExecuteBaseMovement` 方法體不得出現
`LookRotation`／`Slerp`／`transform.rotation`／`transform.Rotate`）＋
**W11**（每個角色 Root 恰好一顆 `CharacterFacingSource`）。
⚠️ **W11 在 Y Bot 接線前會是紅的——這是刻意的**，它就是給使用者的接線提示。

**已知的行為變更（需 Play 確認）**：玩家的**移動朝向**原本經
`MotionDriver` 的 8° 死區，現在**沒有死區**了 ⇒ 轉向變成連續的。這是刻意的（同上理由），
但轉向手感會不一樣。要調就調 `aimFacingTurnSpeed`（玩家 10／敵人 8）。

**刻意沒做**：`Core` 仍持有具體的 `Presentation.CameraControl.AimResolver`／`MotionDriver`（＝F6）。
使用者裁決 **F6 排成下一個獨立切片**，⛔ 不與本切片混做。→ ✅ **已於同日落地，見下方 F6。**

#### F6 — Core 不再持有具體 `AimResolver`（2026-09-08，程式落地）

> **狀態**：🟡 程式已落地、**尚未由 Unity 實跑測試驗證**。⛔ 不得寫成已完成。

**問題（＝ ADR-007 §7 的 E6）**：`ActionState`／`FullBodyStateMachine`／`CharacterPipelineRunner`
三處持有具體的 `Project.Presentation.CameraControl.AimResolver`。**它之所以能靜默進來，是因為
A4 `Core/StateMachine` 的白名單寫的是 `"Project.Presentation"` 前綴**——註解說的是
「`MotionDriver`／`AnimationFacadeBase` 兩個合法 seam」，但前綴放行了整個 `Project.Presentation.*`。

**落地形狀**：

| 項目 | 內容 |
|---|---|
| 新介面 | `Assets/Scripts/Core/Actions/IAimSource.cs`（`Project.Core.Actions`——與 `IActionLifecycleSink` 同層，那裡本來就是「Presentation 實作、Core 消費」的 seam） |
| 成員 | `Vector3 CommitmentOrigin { get; }` ＋ `bool TryGetAimPoint(out Vector3 worldPoint)` |
| 實作 | `AimResolver`（`CommitmentOrigin => transform.position`） |
| 注入 | `Runner` 改 `GetComponent<IAimSource>()`。✅ **原本就不是序列化欄位** ⇒ 沒有「Unity 無法序列化介面」的問題 |
| **A4 收緊** | `Core/StateMachine` 白名單：`"Project.Presentation"` → `"Project.Presentation.Motion"` ＋ `"Project.Presentation.Animation"` |

⚠️ **`CommitmentOrigin` 是誠實的技術債，不是乾淨的設計**：它由 aim source 提供，純粹because
`AimResolver` 與角色 Root **同物件**。這是**既有耦合的忠實描述**，**不是**「瞄準來源理應知道角色在哪」。
⛔ 不要因為它現在有名字就以為它已經對了——拆分兩項責任屬另一個切片。

**驗證**：`Project.Runtime`／`Tests.EditMode`／`Tests.PlayMode` 三個組件皆 **0 error**。
⚠️ **新增 `.cs` 後 `dotnet build` 會失敗，直到 Unity 重生 `.csproj`**（檔案清單由 Unity 產生）——
本次以 scratchpad 內的 `CustomBeforeMicrosoftCommonTargets` 注入 Compile 項驗證，**未動任何版控檔案**。

**新守衛**：
- **P5**（PlayMode）：載入 `SampleScene`，斷言每隻角色**執行期**恰好一顆 `CharacterFacingSource`。
  ⇒ 補上 W11 的盲區：W11 只看 prefab，看不到「prefab 加了元件 ＋ 場景實例仍保留 added override」
  的合併結果（那會變成兩顆，`GetComponent` 的回傳順序變成權威選擇器）。
- **W12**（EditMode）：玩家 prefab（以 Root 上有 `PlayerLocomotionPolicy` 判別）必須恰好一顆
  `PlayerCombatContextSource`。⚠️ **現在是紅的**——見下方。

---

## 5. 不變量（新增／調整）

| 編號 | 內容 | 落點 | 切片 |
|---|---|---|---|
| **A28** 🆕 | `MotionDriver` 的 **procedural 位移路徑**不得讀 `transform.forward`（僅 `ExecuteBakedCurveMovement`／`ApplyBakedCompensation` 允許，因為烘焙曲線表達 clip 自身座標系） | `ArchitectureRegressionTests` 文字檢查：擷取 `ExecuteBaseMovement` 方法體，斷言不含 `transform.forward` | S1 |
| **A29** 🆕 | `IActionLifecycleSink` 實作不得自行解算方向 | 對三個 sink 檔案斷言不含 `AimResolver`／`TryGetAimPoint`／`transform.root.forward`（可併入既有 **A21** 的 forbidden token 陣列） | S2 |
| **A30** 🆕 | 只有 `PlayerLocomotionPolicy` 可引用 `CameraTransform`；其他 `IMovementIntentSource` 實作不得引用 | 掃 `Core/Movement/` 頂層，`CameraTransform` 命中檔名白名單 | S1（**FU-6 的機器化墓碑**） |
| **A21**（既有） | forbidden token 增補 `AimResolver`／`TryGetAimPoint` | 同 A29，二選一實作 | S2 |
| **A4**（既有，建議收緊） | `Core/StateMachine` 的 `AllowedNamespaces` 目前放行**整個** `Project.Presentation`——這正是 `ActionState` 得以持有具體 `AimResolver` 的原因（ADR-007 E6）。建議改為型別白名單（`MotionDriver`／`AnimationFacadeBase`） | `LayerRules` | S3（**不阻擋 S1／S2**） |
| **A5**（既有） | S1／S2 **零改動**（不新增黑板欄位）；S3 若走候選 (a) 才加一列 | `WriterRules` | S3 |

---

## 6. 測試計畫

### 6.1 EditMode

| 測項 | 內容 | 切片 |
|---|---|---|
| **T-D1** | 相機投影純函數：給定壓平的 cam forward/right ＋ 輸入 `(1,0)` ⇒ 世界方向＝cam right；相機仰角不影響結果（y 恆 0） | S1 |
| **T-D2** | `CameraTransform` 為 null ⇒ `DesiredDirection` 為零向量（**不是**輸入原值） | S1 |
| **T-D3** | `AIMovementSource.ProduceIntent` 在**沒有相機**時仍產生非零世界方向 | S1 |
| **T-D4** | `LocomotionSpeedSmoother` 減速滑行保留世界方向；完全停止歸零（既有測項的型別遷移版） | S1 |
| **T-D5** | 承諾在段落邊界取得一次：連段第 2 段的 `Release` 收到的 context **與該段 facing 送出的方向水平投影相同** | S2 |
| **T-D6** | 無 aim（敵人無 `AimResolver`）⇒ `HasAim == false`，sink 走 fallback，流程不中斷（比照既有 `T31`） | S2 |
| **T-D7** | `Reaction` slot 不取得承諾（受擊不是出手，`docs/11` §8.3 例外延續） | S2 |

### 6.2 PlayMode

| 測項 | 內容 | 切片 |
|---|---|---|
| **P-D1** | 給定 facing request ＋ 側向移動意圖，角色**世界位置**朝側向位移，且與 `transform.forward` 夾角 > 45° | S1 |
| **P-D2** | 連段三發投射物的世界方向與各自段落開始時的身體朝向一致（容許死區 8°） | S2 |

### 6.3 人工 Play（`docs/12` Verification Ladder L5，只保留機器做不到的）

- 轉彎弧線手感（R1）、滑行方向觀感（R2）、滑步醜度可接受度。
- **零 GC 複驗**（Development Build ＋ Profiler，`docs/02` §7.4 SOP）——型別由 `Vector2` 改 `Vector3` 不應產生配置，但**要看過才算數**。

---

## 7. 開放問題（實作期決定，**不得自行擴張成新機制**）

1. **無目標時的 facing 行為**：維持現朝向 vs 朝相機正前方。（`docs/11` §8.3 遺留的第三個開放項，本 ADR 未答）
2. **轉彎弧線的 dynamics 形狀**：方向 slerp 的時間常數是否需要與速度平滑分開的旋鈕。⚠️ 上限**一個** `[SerializeField]`，比照 `docs/09` §4-D3 的「兩個構圖參數，不得有第三個」紀律。
3. **承諾粒度**：目前規劃「每個帶 release 的段落一次」。若 Play 顯示連段中角色轉太多，可改為「整次執行一次」——**這是資料／參數決定，不需改 ADR**。
4. **`ActionReleaseContext` 是否該帶 origin**：投射物目前用 `spawnPoint.position`。若之後需要「從承諾當下的位置算方向」再談，**現在不加欄位**。
5. 🆕 **`AIMovementSource` 的交戰模式震盪（獨立缺陷，已登記未處理）**：
   `distanceHysteresis 0.15 m` 在 `moveSpeed 5.66 m/s` 下**只有 ~0.027 秒＝1.6 帧**就跑完 ⇒ Approach↔Hold 可能每兩帧翻一次，而兩模式的方向**差 90°**。
   S1 的方向 dynamics 只是把它從「瞬間 90° 跳變」壓成「受角速度限制的來回追逐」——**可能仍看得到小幅蛇行**。
   **正解是遲滯改成時間／速度感知**（或以距離誤差混合 approach 與切線分量，讓 producer 根本不產生不連續方向），**不是**繼續調濾波器。
   ⚠️ **刻意不與 R1 的處置同批修**：兩個一起改就無法歸因「抽搐是被哪一個修好的」。**先觀察 R1 處置後還剩多少。**

---

## 8. Codex 實作邊界

**允許改（Runtime）**：§2.1／§2.2 列出的檔案，一列都不要多。
**允許改（測試）**：`ArchitectureRegressionTests`（新增 A28／A29／A30）、`MovementIntentTests`、`LocomotionStopTests`、`ArbiterPipelineTests`、`ActionStateTests`、`CameraAimTests`、PlayMode 測試。

⛔ **不得改**：
- 任何 `.asset`／`.prefab`／`.meta`／`.unity`／`.inputactions`／動畫或美術資產（**使用者專屬**）
- `Presentation/IK/**`、`Presentation/Footstep/**`、`Presentation/Audio/**`（與本議題正交）
- `Core/StateMachine/Actions/ActionDefinitionSO.cs`（**承諾不是 authored 欄位**——ADR-007 D3）
- `AnimationFacadeBase` 契約（那是 ADR-006 的範圍）
- Git（一律由使用者執行）

⚠️ **撞到以下情況請停下來回報，不要自行決定**：
- 發現需要新增黑板欄位（那是 S3，需要先改 ADR-007 §7 的處置欄）
- 發現需要第二個 `RequestFacing` 送出者（違反 D3）
- 發現 `MeleeHitboxSink` 的行為必須改變才能通過測試（近戰命中窗屬 `docs/11` §4.1，不在本包）
