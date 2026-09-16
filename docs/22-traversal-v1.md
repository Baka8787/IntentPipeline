# 22 — Traversal V1 → V3（Vault / Climb）：實作與 Integration

| 欄位 | 內容 |
|---|---|
| 狀態 | 🟡 **V3 Correctness／Debug runtime 與自動驗證完成，停在 Editor Integration Gate**。安全貼牆 Entry、contact/vertical 分段 constraint、Exit→Recovery 高度鎖定、最小 Hand IK 與完整可觀測性已落地；三支正式 Bake 仍缺 marker／重烘與 IK timing，因此現有資產仍會明確顯示 endpoint fallback |
| 日期 | 2026-09-13 |
| 前置 | `docs/17` §4（八層問題域）、`docs/18` §2／§5／§6（假說重檢、ADR 觸發條件 T1–T6、scope 清單） |
| 本檔角色 | §0–§8 保留實作前的診斷與推導；§9 記 Environment／Vertical slice；§10 是 V1 Integration；§11 是 V2；§12 是 V3 implementation 與剩餘人工 Gate 的正本 |
| 範圍 | Vault 1m ／ Climb 1m ／ Climb 2m ／ None（fallback 到既有 Jump）。⛔ 不含空中抓邊、Ledge Hang、Shimmy、Corner、連續攀爬鏈、Ladder、Wall Run、Moving Platform、Free Climbing |
| **State reconciliation** | ✅ **2026-09-12 歷史基線**（見 §0.0）；其「程式面為 0」結論已被同日 §9／§10 的實作取代，不得再當成目前狀態 |

---

## 0.0 State Reconciliation（2026-09-12）

> **為什麼有這一節**：使用者指出「traversal 之前已做過實際驗證，只是忘記同步文件」。
> 本節記錄**實際掃描方法與結果**，避免下一個會話重跑一次同樣的搜尋。

### 掃描方法（可重跑）

| # | 檢查 | 指令／方式 | 結果 |
|---|---|---|---|
| 1 | 所有分支的 traversal commit | `git log --all -i --grep="traversal\|vault\|climb\|environment\|probe"` | **0 筆** |
| 2 | 分支與 stash | `git branch -a`／`git stash list` | 5 個分支（`main`／`integrate-adr005`／`wp1-camera-aim`／2 個 `claude/*`），**stash 空**，無 traversal 分支 |
| 3 | 程式符號 | `grep -rn -i "traversal\|vault\|mantle\|climb" Assets/Scripts Assets/_Project --include=*.cs` | **1 筆，且是註解**（`CharacterFacingSource.cs:161` 提到未來的 traversal） |
| 4 | 檔名 | `find Assets -iname "*traversal*" -o -iname "*vault*" -o -iname "*climb*" -o -iname "*environment*"` | 只有 `MovementAnimsetPro_SlideClimb.fbx` 與 StarterAssets 的 `Environment` 場景資產 |
| **4b** | ⭐ **FBX sub-clip 窮舉**（**2026-09-12 補做**） | `for f in $(find Assets -iname "*.fbx.meta"); do grep -H "^      name:" "$f"; done` | **找到 `Climb1m` 在 `RunStrafeUpdate.fbx`** |
| 5 | 歷史中被刪除的檔案（用完即丟的 spike） | `git log --all --diff-filter=ADMR --name-only` 過濾關鍵字 | 只命中 `ReflectionProbe-*.exr`（無關） |
| 6 | 未追蹤／未提交的工作 | `git status --porcelain`（73 筆全覽） | 全部是 Spell／Strafe／Enemy 相關，**無 traversal** |
| 7 | `StateType` enum | `Assets/Scripts/Core/StateMachine/StateType.cs` | `None／Idle／Move／Jump／Roll／Action` ——**6 個，無 Traversal** |
| 8 | 烘焙資產 | `ls Assets/ScriptableObjects/Motion/ \| grep -i "vault\|climb\|slide"` | **0 筆** |
| 9 | 場景物件 | `grep -i` 於 `SampleScene.unity` | 只命中 NavMesh 的 `agentClimb: 0.4`；場景物件為 `Stairs`／`Cube`／`pb_Mesh*`，屬一般關卡幾何 |

### ⚠️ 本輪掃描自身的一個缺陷（已修，記下來避免重犯）

**檢查 4 只比對檔名，因此漏掉了 `Climb1m`。** 由使用者指出後補做檢查 **4b** 才找到。
⇒ **在這個專案裡，「找某支動畫」永遠要用 4b 的窮舉法，⛔ 不得用 4 的檔名法。**
Kubold 的 FBX 檔名不預測內容（`RunStrafeUpdate` 裡裝著 Crouch 全套、互動、Throw、SitChair **和 `Climb1m`**）。
📌 本教訓已同步寫進 `docs/04` §2 的資產盤點表。

### ⚠️ 一個必須知道的證據限制

`.gitignore:100` 忽略了整個 `/[Aa]ssets/MovementAnimsetPro/`。
⇒ **第三方動畫包的工作（匯入設定、子 clip 裁切）在 git history 中完全不可見**，只能直接讀磁碟。
⇒ 但 `Assets/Scripts`（193 檔）與 `Assets/ScriptableObjects`（214 檔）**都在版控內**，
因此「**程式面與烘焙資產面沒有 traversal 工作**」這個結論是可靠的。

### 結論

**在本 repo 內找不到任何 traversal 實作或驗證的產物。**
可能的解釋（本檔不臆測，交由使用者確認）：驗證在另一個 Unity 專案／scratch 專案進行；
或屬於**動畫預覽層級**的確認（開 FBX 看 `Vault1m`／`Climb2m` 動作是否堪用），該層級本來就不會留下 repo 產物。

⇒ 因此**本檔維持「traversal 程式面從零開始」**，⛔ 不把未找到證據的東西標成「已驗證」。
**但 §1.4 的「可重用」欄位是真實存在的既有基礎設施**——那不是要重做的東西，
**「從零」指的是 traversal 本身，不是整個地基。**

📌 **若使用者能指出驗證的所在（專案路徑／截圖／筆記），本節與 §3 的分期應立即重寫。**

---

## 0. 必須先裁決的六件事（其餘章節都建立在這些答案上）

> 這一節不是「風險提醒」，是**擋住實作的實際閘門**。D1／D2／D3 任一沒答案，P1 以後都不能開始。

### D1 — 治理：`docs/18` §6-C 明確把本工作列為「暫時不要做」

| 項目 | `docs/18` 的原文理由 | 現在成立嗎 |
|---|---|---|
| **X-1 完整 traversal / climb 系統** | 「同時撞上 §2.3 的三個『證據不足』＋ G1 ＋ Motion Warping」 | **部分已解**：本檔 §2 給出不需要 G1 答案的承載方式（見 D3）；但「執行中可否被中斷」仍無戰鬥設計可依據 |
| **X-3 新增 `TraversalState`（StateType）** | 「判準 ②（FSM 拓撲），且 **Trial 名額被 ADR-007 佔用**，新 ADR 只能是 `Proposed`＝不得作為實作基線」 | **仍然成立**。ADR-006／007 目前**同為 Trial**（2026-09-11 使用者一次性例外），兩者都尚未 `Accepted` |
| **X-6 執行期改變碰撞體** | 「會動到 `IsGrounded`（黑板欄位、單一寫入者）」 | **仍然成立**，但本檔 §4.5 論證 V1 **可以不需要它** |

**⚠️ 2026-09-12 reconciliation 後的更新**：X-3 的擋點**仍然成立，但距離解除比原本以為的近**。
ADR-006／007 的驗收證據已 fold back（見兩檔 §6／§8）：

| ADR | 通過 | 未通過 | 剩下要做的事 |
|---|---|---|---|
| **ADR-006** | A・B・C・F・G（5/8），D 部分 | **E**（既有行為零回歸）、**H**（零 GC） | **兩次 Play ＋ 一次 Profiler** |
| **ADR-007** | A1・B・C・D（4/7） | **A2**（敵人脫離 combat）、**E**（既有零回歸）、**G**（零 GC），**F** 只驗了 EditMode | **Play ＋ 跑一次 PlayMode ＋ 一次 Profiler** |

⇒ **兩份 ADR 剩下的都不是程式，是驗證動作。** 這正好是只有使用者做得到的部分。

**要裁的是**：

- (a) 先把上表的驗證做完 → ADR-006／007 翻 `Accepted` → X-3 **自動解除**（推薦，成本已經很低）；**或**
- (b) 再批准一次 Trial 例外（第三份 Trial ADR）；**或**
- (c) 只做本檔的 **P0**（§3.1）。P0 不新增 StateType、不碰黑板、不改介面，**ADR 觸發條件 T1–T6 全不成立**，可在 D1 未決的情況下先行。

> 📌 **建議先做 P0**。它的產出正是 `docs/18` §6-B 的 **S-3「Candidate 形狀探針」**要回答的問題，
> 而且會把 `docs/18` §1.1 的 **O-T ticket**（traversal 可視化，因「production 前方 query 不存在」而被擋住）
> 第一次變成合法可實作。

### D2 — 素材：**三支 clip 全部都在**（✅ **已解除，不再是裁決點**）

> ⛔ **2026-09-12 更正——本檔初版寫「磁碟上沒有 Climb1m」，那是錯的，起因是搜尋方法有缺陷。**
> 初版只在「**檔名**符合 vault／climb」的 FBX 裡列 sub-clip，對其餘 148 個 FBX 只比對檔名。
> 使用者指出 `Climb1m` 實際在 `MovementAnimsetPro_RunStrafeUpdate.fbx`。

**磁碟事實（2026-09-12 重新窮舉全部 149 個 `.fbx.meta` 的 sub-clip 名稱）**：

| Clip | 所在 FBX | frames | loop | `keepOriginalPositionY` | animationType |
|---|---|---|---|---|---|
| **`Vault1m`** | `MovementAnimsetPro_SlideClimb.fbx` | 10–97 | 0 | 1 | 3（Humanoid） |
| **`Climb1m`** | **`MovementAnimsetPro_RunStrafeUpdate.fbx`**（line 8989） | 0–65 | 0 | 1 | 3（Humanoid） |
| **`Climb2m`** | `MovementAnimsetPro_SlideClimb.fbx` | 0–108 | 0 | 1 | 3（Humanoid） |
| `Slide` | `MovementAnimsetPro_SlideClimb.fbx` | — | 0 | 1 | 3（Humanoid） |

⇒ **使用者要的四分類（`None`／`Vault1m`／`Climb1m`／`Climb2m`）與磁碟素材完全對應。**
這不是巧合——scope 本來就是照著手上的素材定的。**⛔ 不需要砍任何分類，也不需要採購。**

> 📌 **教訓（已同步寫進 `docs/04` §2 的資產盤點表）**：
> **Kubold 的 FBX 檔名不預測其內容。** `RunStrafeUpdate` 這個名字底下實際裝著 Crouch 全套、Run strafe、
> 互動、Patrol、Throw、SitChair、GetUp **和 `Climb1m`**。
> 找 clip **必須窮舉所有 `.meta` 的 sub-clip 名稱**：
> `for f in $(find Assets -iname "*.fbx.meta"); do grep -H "^      name:" "$f"; done`

**⇒ 連帶後果（重要）**：初版曾把 D2 與「要不要做 obstacle depth」綁成同一個決定，
並以「砍掉 Climb1m ⇒ 不需要 depth」作為推薦。**該推薦隨 D2 一起作廢。**
`Vault1m` 與 `Climb1m` **同為 1m 且同時在場** ⇒ **高度無法區分兩者** ⇒
**obstacle depth／thickness 是 V1 的必要維度**，正是使用者原本舉的「薄牆 vs 厚平台」例子。詳見 §4.4。

### D3 — 執行：**repo 目前沒有任何「權威垂直位移」路徑**（最大的技術缺口）

⚠️ 這一條**修正** `docs/18` §2.3 的「✅ 可以共用 ④軌跡執行：`MotionDriver.ExecuteBakedCurveMovement`」——
該結論**只對水平軸成立**。磁碟事實：

| 路徑 | 水平 | 垂直 |
|---|---|---|
| `ExecuteBaseMovement` | `transform.forward * speed` | `GetGravityThisFrame`（重力積分） |
| `ExecuteVerticalOnlyMovement` | 無 | 同上 |
| `ExecuteBakedCurveMovement` | `transform.forward * worldSpeed`（`SpeedCurve` 是**純量水平速度**） | 同上 |
| `ApplyBakedCompensation` | 朝 `actualTarget` 補償，但 **`toTarget.y = 0f` 與 `distanceToGo.y = 0f` 各出現一次** | 同上 |

⇒ **四條路徑的垂直軸全部由 `GetGravityThisFrame` 獨佔**，而它會無條件積分重力、著地時夾持成 `reboundForce`。
⇒ `MotionBakeData` 的曲線只有 `SpeedCurve`（純量）＋ `RotationCurve`（yaw），**沒有垂直通道**。
⇒ **Vault1m 要上一次、Climb2m 要上兩公尺——現有任何一條路徑都做不到。**

**三個選項**（必須選一個，否則 P2 無法開始）：

| | 做法 | 代價 | 評估 |
|---|---|---|---|
| **(a)** | `MotionBakeData` 加 `VerticalCurve`，`MotionDriver` 加一條「烘焙 3D 位移、重力暫扣」的執行路徑 | 改 SO schema ＋ 重烘 ＋ 新 Editor 欄位 | ✅ **烘焙管線已經在取樣 root world Y**（`MotionFeatureAnalysis.cs` 的 `maxRootY`／`takeoffRootY`），加通道是**既有 sampler 的機械延伸**，不是新能力 |
| **(b)** | 不烘垂直，執行期由 **start pose → candidate target** 插值驅動全軸位移 | 這**就是** motion warping 的最小形式——與「第 8 步才做 warping」的排序衝突 | 🟡 最少的程式，但把 alignment 的決定提前了 |
| **(c)** | (a) ＋ (b)：烘焙曲線給形狀，target 給終點修正 | 最多 | ⛔ V1 不需要 |

> **建議 (a)**：它讓 V1 的位移**仍然是 untargeted committed motion**（＝`RollState` 已驗證過的形狀，`docs/18` §3.2），
> 對齊誤差先靠「只在 candidate 通過嚴格准入時才進入」吸收，把 warping 真正留到第 8 步。
> **代價要誠實講**：角色會嚴格照烘焙軌跡走，障礙物尺寸與 clip 不符時會穿模或踏空——
> 這正是後來需要 warping 的原因，而不是 V1 的失敗。

### D4 — Query 頻率：每幀 vs 按鍵當下

`docs/17` §8 把「影片的 while 掃描是每幀跑還是按鍵時跑」列為**未解**。這是 V1 必須自己決定的：

| | 每幀 | 只在按鍵當下 |
|---|---|---|
| 成本 | ~10–20 raycast/幀/角色（對照：Foot IK 已經 6 條/幀） | ~0 |
| debug 可視化 | ✅ 連續可見，這是 O-T ticket 想要的 | ❌ 只有按下那一幀有資料 |
| snap-to-default 抖動風險 | 🟡 有（邊界附近類別跳動）——可用**遲滯**吸收，`PlayerCombatContextSource` 的 enter/leave 雙半徑黏性是 repo 現成先例 | ✅ 天然沒有 |
| 「記錄，不要重算」 | ✅ 單一 query owner 每幀記錄，`CanEnter` 與 debug 都只讀快照 | 🟡 debug 若想連續顯示就得自己發第二次 query ⇒ **違反 `docs/18` §1.0 第 3 條** |

> **建議每幀**，並加閘門（僅在 `IsGrounded` 且水平速度 > 門檻時掃描），理由是後者會逼 debug 通道違規。
> ⚠️ **這是效能判斷，依 `docs/12` §7 的保留條款，最終結論需要 Player build ＋ Profiler，不由本檔定死。**

### D5 — Probe 是否讀 `CharacterController` 的膠囊幾何

磁碟事實：**runtime 沒有任何程式讀 `radius`／`height`／`center`／`stepOffset`／`slopeLimit`**；
唯一寫入者是 Editor 工具 `CharacterCapsuleFitter`（`skinWidth = radius × 10%`、`center = height/2 + skinWidth`）。
`MotionDriver` 是唯一持有 `CharacterController` 參考的 runtime 元件。

**建議：probe 以唯讀方式讀同一個 `CharacterController`**，理由是 CLAUDE.md 的加重項「單一真相來源」——
在 probe 上另外 author 一份 radius／height 必然會與 Fitter 的輸出漂移。
順帶：`slopeLimit` 應直接當作「頂面是否可站」的判準，**不要再 author 第二個 max slope 數字**。
（唯讀不製造第二個寫入者，不觸發 `WriterRules`。）

### D6 — 新增管線順序算不算 ADR 判準 ③

CLAUDE.md 判準 ③ 是「**管線順序**或核心驅動介面變更」。本檔的 probe 需要一個確定的執行時點
（建議順序 2.7，緊接 2.6 CombatContext 之後、順序 3 Movement Model 之前）。

- **repo 唯一先例**：順序 2.6（CombatContext producer）是隨 **ADR-007** 一起進場的——但那份 ADR 本來就因判準 ①③④ 成立。
- 「**新增**一個不改動既有階段相對次序的 producer」是否等同「**變更**順序」，尚無先例可循。

⇒ 這條請一併裁決。**但注意：P1 之後無論如何都需要 ADR（判準 ②：新增 `StateType`），
所以不值得為了規避 D6 去扭曲設計。**

---

## 1. 現況診斷（磁碟核對 2026-09-12）

### 1.1 Jump Input → Jump Intent → Jump State 的實際流向

```text
PlayerInputSource（Input System）
  └─ InputData.JumpButtonDown（單幀邊沿；PlayerInputSource 用 WasPressedThisFrame）
       └─ 順序 1：Runner 在 stack 上組 InputData
            └─ 順序 2：CharacterPipelineRunner.ProcessIntents(ref input)
                 └─ Intent.JumpRequested = true       ← Intent 的唯一寫入者（WriterRules 已鎖）
                      └─ 順序 4：FullBodyStateMachine.Tick
                           ├─ EvaluateInterrupts：掃「整個」_stateRegistry，
                           │    以 Config.GetPriority 排序 ＋ CanEnter ＋ current.CanBeInterruptedBy
                           └─ EvaluateTransitions：僅在 current.CanTransitionAway 時，
                                依 Config.GetValidTransitions 的順序取第一個 CanEnter
                                └─ JumpState.CanEnter → OnEnter → OnTick
                                     └─ 順序 6（LateUpdate）：CurrentState.OnUpdateMotion
                      └─ 順序 7：Intent.Reset() 統一復位
```

**`JumpState.CanEnter` 現在已經有兩條入口**，且已在用「本幀裁決存成私有欄位、`OnEnter` 只消費不重推」的 pattern：

| | 主動 Jump | 非主動失地 |
|---|---|---|
| 條件 | `Intent.JumpRequested && IsGrounded` | 連續離地 ≥ `FallEntryGrace` |
| 私有裁決 | `_enterAsFall = false` | `_enterAsFall = true` |
| `OnEnter` | `EnterStartPhase` ＋ 等前搖後 `ApplyJumpLaunch` | `EnterFallingPhase`，**不注入** |

`JumpState.cs` 註解明寫：「`OnEnter` 刻意不從 data 重新推導進入原因，避免下游重算已承諾決策的反模式」。
⇒ **Traversal candidate 需要的正是同一個形狀**（`docs/18` §2.1 已論證），
入口層**不需要新機制、不需要改 `CanEnter` 簽名**。

### 1.2 Arbitration 在哪一層（一個必須先澄清的命名陷阱）

專案裡有**兩個不同的東西**都叫「仲裁」：

| | `Core/Arbitration/`（順序 4.5） | FSM 的狀態准入 |
|---|---|---|
| 內容 | `ArbiterData { BlockInput, BlockIK, BlockAudio, BlockExpression }` | `CanEnter` ／ `CanBeInterruptedBy` ／ `CanTransitionAway` ／ `StateRule.Priority` |
| 決定什麼 | **全域封鎖旗標**（暫停、UI 模式） | **哪個狀態贏** |
| Traversal 屬於哪個 | ❌ 不是 | ✅ **是這個** |

⇒ 「Traversal vs Jump 誰贏」的仲裁**已經有家**：`FullBodyStateMachine` ＋ `StateMachineConfigSO` 的 `StateRule` 資產。
⛔ **不要**為 traversal 新增 `ArbiterData` 欄位。

### 1.3 Environment Query：**完全不存在**（確認 `docs/18` §1.1 的結論仍然成立）

全專案的 physics query 只有五處，**沒有任何一處描述角色前方**：

| 位置 | 查詢 | 方向 |
|---|---|---|
| `FootIKController.RaycastGround` | `Physics.Raycast` | 向下（腳） |
| `ThirdPersonCamera` | `Physics.SphereCast` | 相機 → pivot（遮擋） |
| `AimResolver` | `Physics.Raycast` | 相機 → 準心 |
| `GroundEffectSink` | `Physics.RaycastNonAlloc` ＋ `OverlapSphereNonAlloc` | 向下 ＋ 目標附近 |
| `PlayerCombatContextSource` | `Physics.OverlapSphereNonAlloc` ×2 | 以角色為心的球 |

`MotionDriver` 雖呼叫 `CharacterController.Move`，但**忽略回傳的 `CollisionFlags`**，且沒有 `OnControllerColliderHit`——
連「剛剛撞到什麼」都沒有被記錄。

### 1.4 可直接重用的部分

| 需要的能力 | repo 現成的東西 | 重用程度 |
|---|---|---|
| **Core 層的 physics query producer**（MonoBehaviour、預配置緩衝、由 Runner 注入時間以利決定性測試） | `Core/Combat/PlayerCombatContextSource`（固定 32 格 `Collider[]`、`Tick(data, currentTime)`） | ✅ **整個形狀照抄** |
| **連續量 → 離散分類的純函式**（可 EditMode 測、明確定義退化值） | `LocomotionStopSelector.SelectByEntryPhase`（退化回 `-1`） | ✅ 同型 |
| **邊界抖動的遲滯** | `PlayerCombatContextSource` 的 enter/leave 雙半徑黏性 | ✅ 同型 |
| **有始有終的 committed motion 狀態** | `RollState`（115 行：`OnEnter` latch `BakedDuration`、`CanTransitionAway => finished`、**先確認動畫真的在播才信任 `normalizedTime`**、查無 bake 安全退化） | ✅ **V1 的 TraversalState 就是這 115 行 ＋ 一個 world target** |
| **candidate 帶參數的資料驅動動作定義** | `ActionDefinitionSO` ＋ `ActionPhaseEntry`（`AnimationKey`／`Bake`／`FallbackDuration`／`Interruptible`） | 🟡 形狀可參考，但 V1 只有 3 種 kind，⛔ 不要先做成 SO 清單 |
| **把 Core seam 注入 FSM** | `FullBodyStateMachine.Initialize(..., ActionRequestTarget)` | ✅ 先例已在 |
| **出場交還 locomotion** | Jump 落地的 `normalLandToMove`／`walk[]`／`run[]` 變體表 ＋ `LocomotionStopSelector` 腳相選片 | ✅ 這一層本專案最成熟 |
| **debug 可視化通道** | `FootIKController` 的 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` private snapshot ＋ runtime `LineRenderer` ＋ `OnDrawGizmos`，由 A34 守「不得重發 physics query」 | ✅ **整套照抄**，連測試都可比照 A34 |
| **PlayMode 真實物理場景** | `Assets/_Project/Tests/PlayMode/`（已存在 asmdef ＋ 兩支測試；`JumpLandingPlayModeTests` 在測試內 `new GameObject` ＋ `AddComponent<CharacterController>()`） | ✅ **可用 `GameObject.CreatePrimitive` 造障礙物，整條 query→classification 可自動驗證，不需要使用者開 Editor** |

### 1.5 缺少的部分

| # | 缺口 | 嚴重度 |
|---|---|---|
| **G-a** | 前方環境查詢元件、查詢結果的資料形狀、以及「這份資料屬於誰」 | 本檔 §3 解決 |
| **G-b** | **權威垂直位移路徑**（D3） | 🔴 **擋住 Vault／Climb 執行**，無替代方案 |
| ~~**G-c**~~ | ~~`Climb1m` 動畫~~ | ✅ **2026-09-12 撤銷——素材存在**（`RunStrafeUpdate.fbx`），見 D2。三支 clip 齊全，無素材缺口 |
| **G-d** | `Core/StateMachine` 的 `LayerRules` 允許清單沒有 `Project.Core.Environment`（目前只允許 自己／Blackboard／Actions／Movement／Presentation.Motion／Presentation.Animation） | 🟡 P1 時需要與 `Project.Core.Actions` 同樣理由擴一列，並在**同一工作包內**更新測試 |
| **G-e** | 離地期間 `JumpState` 的「非主動失地」入口與 traversal 的關係 | 🟡 **2026-09-12 已對資產核實，風險比初判小**——見下方 |
| **G-f** | 沒有 StateType 之外的「多個候選」機制 | ✅ V1 不需要——分類互斥，一次最多一個 candidate |

#### G-e 的實際驗證（`PlayerStateMachineConfig.asset` ＋ `FullBodyStateMachine` 核對，2026-09-12）

解碼現行 `rules`（`StateType { None=0, Idle=1, Move=2, Jump=3, Roll=4, Action=5 }`）：

| State | Priority | `CanBeInterruptedBy` | `ValidTransitions` |
|---|---|---|---|
| Idle(1) | 0 | Roll, Jump, Action | Move |
| Move(2) | 0 | Jump, Roll, Action | Idle |
| **Jump(3)** | 10 | **（空）** | Move, Idle |
| **Roll(4)** | 20 | **（空）** | Move, Idle |
| Action(5) | 10 | Roll, Jump | Move, Idle |

`CheckCanInterrupt` 的實作是 `_interruptMap.TryGetValue(current) && list.Contains(next)`
⇒ **空清單 ＝ 完全不可被打斷**。這正是 Jump 與 Roll 今天不會被中途搶走的機制。

**⇒ 結論修正**：「Jump 會在 traversal 中途搶狀態」**不是無解的架構問題**，
只要 `TraversalState` 比照 Roll —— ①`CanTransitionAway => false` 直到播完（程式側）、
②資產側 `CanBeInterruptedBy` **留空**（使用者側）—— 中途搶奪在結構上就不可能發生。

**⚠️ 但真正的風險換了位置，而且仍然是 must-fix**：

1. `EvaluateInterrupts` 的條件是 `!targetState.CanEnter(data) || !_currentState.CanBeInterruptedBy(...)`，
   `||` **短路在後**，代表 **`JumpState.CanEnter` 仍會在 traversal 期間每幀被呼叫**，
   而它**有副作用**（寫 `_ungroundedSince`／`_enterAsFall`）。中途無害（打斷被拒），
   但要確認退出當幀的狀態是乾淨的。
2. **真正的風險是「退出幀」**：traversal 結束時若角色**尚未 grounded**，
   `JumpState` 的 fall-entry 會在下一幀成立並接手。
   對「vault 過去之後是一段落差」而言**這其實是正確行為**；
   對「climb 上平台」則必須確保**結束姿勢已站穩在頂面**。
   ⇒ 這條**只能 Play 驗證**，且應列入 P1 的驗收清單。

---

## 2. Traversal V1 建議資料流

```text
順序 1–2   Input → Intent.JumpRequested            〔完全不動〕
順序 2.7   TraversalProbe.Tick(data)               〔新增；唯一 physics query owner〕
             ├─ stepping scan → forward hit
             ├─ top-down probe → topPoint / topNormal
             ├─ destination capsule clearance
             └─ TraversalClassifier.Classify(...)  〔純函式〕
                  └─ TraversalCandidate { Kind, EntryPoint, TargetPoint, TargetForward,
                                          Height, TopNormal, RejectReason }
                       └─ 快取成 probe 的唯讀屬性（⛔ 不進黑板）
順序 4     FullBodyStateMachine.Tick
             ├─ TraversalState.CanEnter(data)
             │     JumpRequested && IsGrounded && probe.Candidate.Kind != None
             │     └─ 命中 ⇒ 把 candidate 複製進私有欄位（_committedCandidate），
             │              比照 JumpState._enterAsFall；OnEnter 只消費不重推
             └─ JumpState.CanEnter(data)      〔原封不動〕
                  Priority 讓 Traversal 先被評估；沒有 candidate 時 Traversal 回 false
                  ⇒ Jump 照常成立  ← 這就是 fallback，不需要任何新機制
順序 6     TraversalState.OnUpdateMotion
             └─ motionDriver.<新的 3D 烘焙位移路徑>(bake, normalizedTime, data)
                  （重力暫扣；比照 ExecuteVerticalOnlyMovement 的「扣掉一個軸」先例）
結束       CanTransitionAway => 播放完成 ⇒ 交還 Idle／Move
```

### 各層責任邊界（與構想一致，此處只是對上 repo 的既有名字）

| 層 | 誰 | **只**負責 | ⛔ 不得 |
|---|---|---|---|
| Intent | `CharacterPipelineRunner.ProcessIntents` | 按鍵 → `JumpRequested` | 認識 traversal |
| **Environment Query** | `TraversalProbe` | 發 physics query、產出**幾何事實** | 決定 clip／切 State／移動角色／決定 gameplay action |
| **Classification** | `TraversalClassifier`（**static 純函式**） | 量測值 ＋ 能力參數 → `Kind` ＋ **拒絕理由** | 碰 Unity API、碰黑板、碰時間 |
| **Selection / Arbitration** | `FullBodyStateMachine` ＋ `StateMachineConfigSO` 資產 | 誰贏、fallback 到誰 | 重新發 query |
| **Execution** | `TraversalState` ＋ `MotionDriver` | 播放期間的位移／重力抑制／完成訊號 | 重新分類、重新查詢 |
| **Presentation** | `AnimancerFacade` | 播已決定的 key | 自己 raycast、自己判斷環境 |
| **Debug** | `TraversalProbe` 自己（`#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`） | **只畫已記錄的快照** | 重發任何 physics query |

---

## 3. 最小實作方案

### 3.1 P0 — Probe ＋ Candidate ＋ Classification（**無 gameplay 行為變化**）

> **T1–T6 全不成立**（candidate 不進黑板、不新增 StateType、不改介面、單一消費者）⇒ **不需要 ADR**（D6 除外）。

| 新增檔案 | 類型 | 職責 | 行數量級 |
|---|---|---|---|
| `Core/Environment/TraversalKind.cs` | `enum` | `None / Vault1m / Climb1m / Climb2m` | ~10 |
| `Core/Environment/TraversalRejectReason.cs` | `enum` | `None / NoForwardHit / TooHigh / NoValidTop / TopTooSteep / DestinationBlocked / TooFast / …` | ~15 |
| `Core/Environment/TraversalCandidate.cs` | `readonly struct` | 幾何事實 ＋ 分類結果 ＋ 拒絕理由 | ~40 |
| `Core/Environment/TraversalClassifier.cs` | `static class` | **純函式**：量測值 ＋ 能力參數 → `TraversalCandidate` | ~80 |
| `Core/Environment/TraversalProbe.cs` | `MonoBehaviour` | 唯一 query owner；stepping scan ＋ top probe ＋ clearance；快取 candidate；debug 快照與繪製 | ~250（含 debug） |

**能力參數先用 `[SerializeField]` 放在 `TraversalProbe` 上**，⛔ 先不做 SO。
理由：CLAUDE.md「第二個使用者出現前不得建立 production abstraction」；目前只有玩家一隻角色需要。

**修改**：

- `CharacterPipelineRunner`：新增順序 2.7 呼叫（見 D6）；
- `ArchitectureRegressionTests`：新增 A-rule（見 §5）。

**P0 的產出是答案，不只是程式**：它直接回答 `docs/18` §6-B 的 **S-3「一個最小 candidate 需要幾個欄位」**，
並讓 `docs/18` §1.1 的 **O-T** 第一次合法。

### 3.2 Gate A — 使用者裁決 D1／D3（~~D2~~ 已於 2026-09-12 解除）（＋必要時開 ADR）

### 3.3 P1 — `TraversalState` ＋ 入口 ＋ **Vault1m only**

| 動作 | 內容 | 誰做 |
|---|---|---|
| `StateType` 加 `Traversal` | 判準 ②（FSM 拓撲）⇒ **需要 ADR** | 程式（AI）＋ ADR |
| `Core/StateMachine/States/TraversalState.cs` | **照 `RollState` 的 115 行**：`OnEnter` latch `BakedDuration`、`CanTransitionAway => finished`、先確認動畫真的在播才信任 `normalizedTime`、查無 bake 安全退化 | 程式 |
| `LayerRules` 加 `Project.Core.Environment` | 與既有 `Project.Core.Actions` 同一理由；**同一工作包內更新測試** | 程式 |
| `FullBodyStateMachine.Initialize` 注入 probe | 比照 `ActionRequestTarget` | 程式 |
| `StateMachineConfigSO` 資產：`StateRule` 加 Traversal、Priority 高於 Jump、**把 Jump 從 Traversal 的 `CanBeInterruptedBy` 拿掉**（G-e） | ⚠️ **`.asset`，使用者執行** | 使用者 |
| `Vault1m` 的 `MotionBakeData` | 用既有 `MotionBakeEditor.BatchBake` | 使用者（Editor 操作） |

### 3.4 P2 — Climb2m（**需要 D3 的垂直方案**）

### 3.5 P3 — Climb1m（素材已齊；**需要 §4.4 的 depth 維度**）

⚠️ **2026-09-12 排序提醒**：P3 不再是「等素材」的末段工作。
由於 `Vault1m` 與 `Climb1m` 同為 1m，**depth 必須在 P1（Vault1m）就設計進分類器**，
否則 P1 會建立一個「1m ⇒ 一律 Vault」的錯誤前提，P3 再回頭改分類器等於重做。
⇒ **depth 屬於 P1 的分類器設計，不屬於 P3。**

### 3.6 P4 — alignment / contact point / target matching / warping / 角色尺寸

⛔ **不要因為 P4 最終會來，就在 P0–P3 先建框架。**

---

## 4. 環境檢測方案

### 4.1 Stepping scan：**適合，但起點不是 0.2m**

「由低到高逐步檢測」在本專案是**對的做法**，但有一個 repo 特有的修正：

> **掃描下限應該是 `CharacterController.stepOffset`，不是一個 author 的 0.2m。**
> 低於 `stepOffset` 的東西 `CharacterController.Move` 本來就會自己走上去——
> 對它回報 candidate 等於讓 traversal 去搶一個物理引擎已經解決的問題。

- **上限** = 支援的最大攀爬高度（Climb2m）＋ 餘量。
- **步距**：`[SerializeField]`，數量級 0.1–0.15m（⛔ 實際值不由本檔決定，屬 tuning）。
- **零 GC**：`Physics.Raycast(origin, dir, out hit, dist, mask, QueryTriggerInteraction.Ignore)` 這個多載**不配置**
  （`FootIKController` 已在用），N 條射線不需要 NonAlloc 緩衝。
- **查詢方向**：建議 `data.MoveDirection`（`LocomotionModel` 已發布的世界移動方向），為零時退回 `transform.forward`。
  ⚠️ 這是**讀取已 commit 的方向**，不是新建第二套方向權威——符合 ADR-007。

### 4.2 Top surface probe：由上往下

找到「開始變空」的高度後，從 `firstClearHeight + 餘量` 往下打一條射線取 `topPoint` / `topNormal`。
**頂面合法性直接用 `CharacterController.slopeLimit` 判**（`Vector3.Angle(topNormal, Vector3.up) <= slopeLimit`），
⛔ 不要 author 第二個 max slope 數字（D5）。

### 4.3 Destination clearance：`Physics.CheckCapsule`

以**角色自己的膠囊尺寸**（D5）在目的地姿勢做一次 `Physics.CheckCapsule`（回傳 `bool`，零配置）：

- Climb ⇒ 目的地 = `topPoint` 上方一個膠囊；
- Vault ⇒ 目的地 = 障礙物另一側的地面。

**這一項不可省**——它是「有洞可鑽 vs 撞進牆裡」的唯一分野，
也是影片「上方淨空」「左右阻擋」兩個維度的最小代表。

### 4.4 Obstacle depth：**V1 必要**（🔄 2026-09-12 結論反轉）

> ⛔ 初版依據錯誤的 D2（以為沒有 `Climb1m`）推薦「V1 不需要 depth」。**該推薦已作廢。**

`Vault1m` 與 `Climb1m` **同為 1m 高且兩支 clip 都在磁碟上** ⇒ **高度維度無法區分兩者**
⇒ **必須有第二個維度**，而那就是厚度：

| 同為 1m | 判準 | 動作 |
|---|---|---|
| 薄（欄杆／矮牆） | `depth <= vaultMaxDepth` | **`Vault1m`**（翻過去，落在另一側） |
| 厚（平台／箱子） | `depth > vaultMaxDepth` | **`Climb1m`**（爬上去，站在頂面） |

**⇒ 這也改變了 V1 分類器的最小輸入集**：`height` ＋ `topNormal` ＋ `destinationClearance` **＋ `depth`**。

**量測方式（一條射線同時解兩件事）**：`topPoint` 找到後，
從 `topPoint + forward * maxDepthProbe + Vector3.up * ε` 往下打一條射線——

- **命中頂面** ⇒ 平台在該距離仍延續 ⇒ **厚** ⇒ Climb；
- **落空／落到低處** ⇒ 頂面已結束 ⇒ **薄** ⇒ Vault，**而該落點正好就是 vault 的目的地**，
  可直接餵給 §4.3 的 destination clearance。

⚠️ `vaultMaxDepth` 的**實際數值屬 tuning**，且應由 `Vault1m` clip 的實際水平位移量決定
（烘焙後的 `AutoAverageSpeed × Duration` 可給出一個有根據的起點），⛔ 不由本檔定死。

⚠️ **`depth` 是連續量餵給離散選擇 ⇒ 必須有遲滯**，否則角色站在閾值附近會在 Vault／Climb 間跳動
（`snap-to-default` 缺陷類型，見 §5 的 L3 測試）。

### 4.5 執行期碰撞體：**V1 先不要動**（`docs/18` X-6 維持）

`CharacterController.height`／`center` 的改動會直接影響 `isGrounded`，而 `IsGrounded` 是黑板單一寫入者欄位。
**先用「攀爬期間沿牆面垂直移動（膠囊不與垂直面衝突）→ 超過頂面高度後才前推」的路徑試**；
真的穿模時再回來開 X-6，而且要與黑板 ownership 一起決定。
⚠️ 這條**只能由 Play 驗證，無法從程式推斷**。

---

## 5. 測試與 Debug 方案

### L0 — EditMode 架構不變量（`ArchitectureRegressionTests`）

| 新 rule | 內容 |
|---|---|
| **A-n1** | `TraversalProbe.cs` 是**唯一**含前方 physics query 的檔案；`Core/Environment` 其餘檔案不得出現 `Physics.` |
| **A-n2** | traversal debug presentation 不得重發 physics query（**直接比照既有 A34 的 Foot IK 版本**） |
| **A-n3** | `TraversalCandidate` 不得被寫入 `PlayerRuntimeData`（在 D1 裁決前，機器化保證 T1/T5 不被偷跨） |
| **A-n4**（P1） | `LayerRules` 的 `Core/StateMachine` 允許清單擴一列 `Project.Core.Environment`，理由與 `Project.Core.Actions` 並列 |

### L3 — EditMode 純邏輯（**價值最高、成本最低**）

`TraversalClassifier` 是 static 純函式 ⇒ 整個分類矩陣可完整覆蓋，不需要物理場景：

- 每一種 `Kind` 的正例；
- **每一個 `RejectReason` 的負例**（太高／無頂面／頂面太陡／目的地被擋／無前方命中）；
- **邊界與遲滯**：1m 與 2m 交界附近來回抖動時不得逐幀翻類別（守 `snap-to-default` 缺陷類型）；
- 退化輸入：零長度方向、NaN、無命中 ⇒ 必須回 `None`，而不是最近的類別。

### L4 — PlayMode（**這一層比想像中能做的多**）

`Assets/_Project/Tests/PlayMode/` 已存在，且既有測試就是在測試內 `new GameObject` ＋ `AddComponent<CharacterController>()`。
⇒ 可以用 `GameObject.CreatePrimitive(PrimitiveType.Cube)` 造出 1m／2m／薄牆／有頂障礙，
**整條 query → classification 用真實物理驗證，完全不需要使用者開 Editor**：

| 測試 | 斷言 |
|---|---|
| 1m **薄牆**（depth ≤ 閾值）＋ 頂面淨空 | `Kind == Vault1m` |
| 1m **厚平台**（depth > 閾值）＋ 頂面淨空 | `Kind == Climb1m` ⇐ **depth 維度的直接驗證** |
| 1m 障礙，depth 在閾值附近來回 | **不得逐幀跳動**（遲滯生效） |
| 2m 立方體 | `Kind == Climb2m` |
| 2.5m 立方體 | `Kind == None`，`RejectReason == TooHigh` |
| 頂面上方 0.5m 有天花板 | `Kind == None`，`RejectReason == DestinationBlocked` |
| 45° 斜面 | 依 `slopeLimit` 判定，不得回 climb |

### 🔒 如何證明「普通 Jump 沒被破壞」（四層，逐層加嚴）

1. **P0 期**（EditMode）：斷言 `JumpState.cs` 原始碼**不含**任何 traversal 型別的引用——
   P0 的 Jump 在**結構上**不可能被影響。
2. **P0 期**（PlayMode）：同一場景跑兩次跳躍，一次有 `TraversalProbe`、一次沒有，
   斷言 apex 高度、滯空時間、`JustLanded` 時點一致。
3. **P1 期**（PlayMode）：`Kind == None` 時按跳躍，斷言 `CurrentState == StateType.Jump`
   （**這就是 fallback 的機器化定義**）。
4. **P1 期**（PlayMode）：有合法 candidate 但角色**在空中**按跳躍 ⇒ 仍不得進 Traversal
   （守 ADR-002 的「空中不得重新起跳」）。

### Debug／可視化（`docs/18` §1.1 的 **O-T ticket**，P0 起第一次合法）

**整套照抄 `FootIKController`**：`#if UNITY_EDITOR || DEVELOPMENT_BUILD` 的 private snapshot
＋ Game View `LineRenderer`（主通道，不依賴 Gizmos、不需選取角色）＋ `OnDrawGizmos`（Scene View 詳查）。

應顯示：每一條 stepping ray 的起點／命中（命中與落空不同色）、`firstClearHeight`、
top-down probe 與 `topPoint`／`topNormal`、destination capsule（通過／被擋不同色）、
最終 `Kind` 與 `RejectReason`。
⛔ **只畫已記錄的快照**（`docs/18` §1.0 第 3 條）。

---

## 6. 風險與未決策項

### 6.1 現在**必須**決定（擋住實作）

| # | 項目 | 擋住什麼 |
|---|---|---|
| **D1** | 治理：Trial 名額／是否先讓 ADR-007 Accepted | P1 以後全部 |
| ~~**D2**~~ | ~~`Climb1m` 素材政策~~ | ✅ **2026-09-12 解除**：三支 clip 齊全。連帶結論反轉——**depth 成為 V1 必要維度**（§4.4） |
| **D3** | 權威垂直位移路線 (a)/(b)/(c) | P2（Climb2m）、也影響 Vault1m 的品質 |
| **D4** | Query 頻率（每幀 vs 按鍵） | P0 的 debug 通道是否合法 |
| **D5** | Probe 是否唯讀 `CharacterController` 幾何 | P0 的參數來源 |
| **D6** | 新增管線順序是否算判準 ③ | P0 是否需要 ADR |

### 6.2 可以等**動畫接線時**再決定

- 對齊容差、起手距離、`MatchTarget` vs 程序修正、contact point；
- 執行期碰撞體處理（X-6）——先試不改，穿模了再談；
- traversal 執行中**可否被中斷**（受擊／取消）——`docs/18` §2.3 已列為「證據不足」，
  取決於還不存在的戰鬥設計；
- 不同角色尺寸適配（目前只有玩家一個使用者）。

### 6.3 ⛔ 本檔**刻意不定**的數值

最大攀爬高度、vault 厚度閾值、clearance 餘量、掃描步距、進入速度門檻、遲滯寬度、播放速率——
**全部是 gameplay／animation tuning，屬於使用者**。本檔只定義**這些數字住在哪個欄位、被誰讀**。

---

## 7. ADR 判準檢查

| 判準 | P0 | P1+ |
|---|---|---|
| ① 黑板 schema／ownership 變更 | ❌ candidate 只是 probe 的私有快取 | ❌（除非未來出現第二個消費者 ⇒ T2） |
| ② FSM 拓撲／hierarchy 變更 | ❌ | ✅ **新增 `StateType.Traversal`** |
| ③ 管線順序／核心驅動介面變更 | 🟡 **見 D6** | 🟡 同左 |
| ④ 推翻既有架構不變量 | ❌ | 🟡 `LayerRules` 擴一列（`docs/18` X-3／X-6 的解除亦屬此類） |

⇒ **P0 在 D6 判為「不算變更」時完全不需要 ADR；P1 無論如何需要。**

---

## 8. 與既有文件的關係（需要 fold back 的項目）

| 文件 | 需要的修改 | 時機 |
|---|---|---|
| `docs/18` §2.3 | 「可以共用 ④軌跡執行」**只對水平軸成立**——`ExecuteBakedCurveMovement` 與 `ApplyBakedCompensation` 都是水平位移 ＋ 重力，`MotionBakeData` 沒有垂直通道（本檔 D3） | **建議立即修正**（事實更正，非決策變更） |
| `docs/18` §6-C | X-1／X-3／X-6 的狀態需依 D1 的裁決更新 | Gate A 之後 |
| `docs/18` §1.1 | O-T ticket 的前置條件（production 前方 query）於 P0 解除 | P0 完成後 |
| `docs/02-dev-spec.md` §2.1 | 新增順序 2.7 | P0 完成後 |
| `docs/00-map.md` | 本檔一行指標 | ✅ 已加 |

---

## 9. Codex 工作包（🟢 **已裁決，可直接實作**，2026-09-12）

> **狀態**：使用者於 2026-09-12 裁示立即定案並交付 Codex。本節內的 **W1–W8 是實作基線**。
> 本節之外的 §0 裁決點 **D1／D3** 只影響 **P1 以後**；**W1–W8 全部不觸發 ADR**，不必等 Gate A。
> 以下所有「預設值」都是**已拍板的預設**，⛔ Codex 不得重新開會討論。

### 9.0 已拍板的預設（取代 §0 的 D4／D5／D6）

| 原裁決點 | **拍板結果** |
|---|---|
| **D4** query 頻率 | **每幀**，但加閘門：`IsGrounded && 水平速度 > probeMinSpeed` 才掃描。分類器內建遲滯 |
| **D5** 膠囊幾何 | **唯讀** `CharacterController`（`radius`／`height`／`center`／`stepOffset`／`slopeLimit`）。⛔ 不得 author 第二份尺寸 |
| **D6** 管線順序 | **順序 2.7**，由 `CharacterPipelineRunner` 呼叫，緊接順序 2.6 之後、順序 3 之前。視為**加法**，同工作包內更新 `docs/02` §2.1 |
| **D2** 素材 | 三支 clip 齊全 ⇒ **depth 從 W3 就進分類器**（見 §4.4） |

### 9.1 ⛔ Codex 的硬邊界（違反即整批退回）

1. ⛔ **不得執行任何 Git 操作**（CLAUDE.md Solo Developer Mode；此為本機工作樹）。
2. ⛔ **不得修改** `.asset`／`.prefab`／`.meta`／`.unity`／`.inputactions`／任何動畫美術資產。
3. ⛔ **不得新增 `StateType`**、不得新增黑板欄位、不得改 `IMovementModel`／`BaseState` 簽名 ⇒ 會觸發 ADR。
4. ⛔ **不得讓 W1–W8 改變任何現有 gameplay 行為**。Jump／Roll／Action／Locomotion 必須逐字不變。
5. ⛔ **debug 繪製不得重新發射 physics query**（`docs/18` §1.0 第 3 條；比照既有 A34）。
6. ⛔ **不得在 runtime 熱路徑配置**（無 `new`／LINQ／字串插值；`Debug.Log` 一律包 `#if UNITY_EDITOR`）。

### 9.2 工作項

#### 🅰 Track A — Environment Probe（無 gameplay 行為變化）

| # | 檔案 | 內容 |
|---|---|---|
| **W1** | `Assets/Scripts/Core/Environment/TraversalKind.cs` | `enum TraversalKind { None = 0, Vault1m, Climb1m, Climb2m }` |
| **W2** | `Assets/Scripts/Core/Environment/TraversalRejectReason.cs` | `enum { None = 0, NotGrounded, TooSlow, NoForwardHit, TooHigh, NoValidTop, TopTooSteep, DestinationBlocked }`。**每一個拒絕理由都必須有對應的分類器分支與測試** |
| **W3** | `Assets/Scripts/Core/Environment/TraversalCandidate.cs` | `readonly struct`。欄位：`Kind`、`RejectReason`、`ObstacleHitPoint`、`ObstacleNormal`、`TopPoint`、`TopNormal`、`Height`、`Depth`、`DestinationPoint`、`Forward`、`IsValid => Kind != None`。⛔ 不得加 `confidence`／清單／時間戳（提前設計） |
| **W4** | `Assets/Scripts/Core/Environment/TraversalProbeSettings.cs` | `[Serializable] struct`，被 W5 以 `[SerializeField]` 持有。欄位：`obstacleMask`、`scanStep`、`scanCeiling`、`forwardScanDistance`、`vault1mMaxDepth`、`climb1mMaxHeight`、`climb2mMaxHeight`、`heightHysteresis`、`depthHysteresis`、`probeMinSpeed`、`destinationClearanceMargin`。**⛔ 不做 ScriptableObject**（第二個使用者出現前不得建立 production abstraction）。數值填 §6.3 說的「合理起點」即可，⚠️ **這些是 tuning，使用者稍後會改** |
| **W5** | `Assets/Scripts/Core/Environment/TraversalClassifier.cs` | `public static class`，**純函式、零配置、不碰 Unity API 以外的東西**（`Vector3`／`Mathf` 可用，⛔ 不得呼叫 `Physics.*`、不得讀 `Time`、不得讀黑板）。<br>簽名：`public static TraversalCandidate Classify(in TraversalProbeMeasurement m, in TraversalProbeSettings s, TraversalKind previousKind)`<br>`previousKind` **只用於遲滯**，⛔ 不得用於其他決策。<br>判定順序（**固定，不得改**）：①無前方命中 → `NoForwardHit` ②無合法頂面 → `NoValidTop` ③頂面角度 > `slopeLimit` → `TopTooSteep` ④高度 > `climb2mMaxHeight` → `TooHigh` ⑤目的地被擋 → `DestinationBlocked` ⑥高度分層（含遲滯）⑦1m 層再以 `Depth` 分 `Vault1m`／`Climb1m`（含遲滯） |
| **W6** | `Assets/Scripts/Core/Environment/TraversalProbe.cs` | `MonoBehaviour`，**唯一** physics query owner。<br>`public void Tick(PlayerRuntimeData data)` 由 Runner 於順序 2.7 呼叫。<br>`public TraversalCandidate Candidate { get; private set; }`（唯讀快取，⛔ 不寫黑板）。<br>**查詢順序**：①閘門（`IsGrounded && speed > probeMinSpeed`，不過就寫 `default` 並 return）②方向＝`data.MoveDirection`，零長度時退回 `transform.forward` ③**stepping scan**：由 `stepOffset` 起、每 `scanStep` 往上到 `scanCeiling`，`Physics.Raycast(origin, dir, out hit, forwardScanDistance, obstacleMask, QueryTriggerInteraction.Ignore)`（**此多載零配置，⛔ 不要改用 NonAlloc 緩衝**）④首個落空高度 → 由上往下取 `TopPoint`／`TopNormal` ⑤自 `TopPoint + forward*vault1mMaxDepth + up*ε` 向下一條射線同時解 `Depth` 與 vault 落點 ⑥`Physics.CheckCapsule` 以**角色自身膠囊**驗 destination clearance ⑦呼叫 W5。<br>**每幀把量測值寫進 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` 的 private snapshot** |
| **W7** | 同檔（W6 內） | **Debug 呈現，整套比照 `FootIKController`**：Game View `LineRenderer` 主通道（不依賴 Gizmos、不需選取角色）＋ `OnDrawGizmos`（非 selected-only）。顯示每條 stepping ray（命中／落空不同色）、`TopPoint`／`TopNormal`、depth 探線、destination capsule（通過／被擋不同色）、`Kind` 與 `RejectReason`。**只讀 snapshot，⛔ 不得重發 query。** 由 `[SerializeField] bool drawTraversalRuntimeLines / drawTraversalSceneGizmos` 各自控制 |
| **W8** | `Assets/Scripts/Core/Pipeline/CharacterPipelineRunner.cs` | 新增順序 2.7：比照既有 2.6 的寫法取得 sibling `TraversalProbe` 並 `Tick(_runtimeData)`。**probe 缺席時整段跳過**（可選元件，敵人沒有它也要能跑） |

> 📌 **W6 需要一個量測值載體**：請在 `TraversalProbe.cs` 內定義 `public readonly struct TraversalProbeMeasurement`
> （`HasForwardHit`／`ForwardHitPoint`／`ForwardHitNormal`／`FirstClearHeight`／`HasTop`／`TopPoint`／`TopNormal`／
> `Height`／`Depth`／`DestinationPoint`／`DestinationBlocked`／`SlopeLimit`／`Forward`），
> 讓 W5 能在**沒有 Unity 物理場景**的 EditMode 下被完整測試。這是本設計可測性的關鍵，⛔ 不得把量測與分類合併成一個方法。

#### 🅱 Track B — 垂直位移通道（**目前無 gameplay 使用者，先把能力補上**）

| # | 檔案 | 內容 |
|---|---|---|
| **W9** | `Assets/Scripts/Presentation/Motion/MotionBakeData.cs` | 新增 `public AnimationCurve VerticalCurve;`（相對起點的垂直位移，公尺）。**加法欄位，舊資產反序列化為 null** ⇒ 所有讀取點必須有 `null`／空曲線的安全退化（比照既有 `Duration <= 0` 的退化判準）。新增 `public float GetVerticalAt(float time)`，無曲線時回 `0f` |
| **W10** | `Assets/Scripts/Editor/Stages/MotionFeatureAnalysis.cs`（或同層新 stage） | 烘焙時填 `VerticalCurve`。**取樣邏輯已存在**——既有 `maxRootY`／`takeoffRootY` 就是在取 root world Y，沿用同一條取樣迴圈，把「相對第一幀的 Y 位移」逐格寫進曲線 |
| **W11** | `Assets/Scripts/Presentation/Motion/MotionDriver.cs` | 新增 `public void ExecuteCommittedCurveMovement(MotionBakeData bake, float normalizedTime, float previousNormalizedTime, PlayerRuntimeData data)`：水平沿用既有 playhead-delta 速度邏輯，**垂直改用 `VerticalCurve` 的前後差值**，**重力暫扣**（⛔ 不呼叫 `GetGravityThisFrame` 取重力，但**仍必須同步 `IsGrounded`／`VerticalVelocity`／`JustLanded`／`JustLeftGround`**——把既有 `GetGravityThisFrame` 的**狀態同步段**抽成 private `SyncGroundedState(data)`，兩條路徑共用，**單一寫入者不變**）。<br>⚠️ **這是本工作包唯一需要動既有熱路徑的地方**，請以「抽出共用私有方法」的最小重構完成，⛔ 不得改變 `ExecuteBaseMovement` 等既有路徑的逐幀行為 |

### 9.3 測試（**與程式同一個工作包交付，不得延後**）

| 層 | 檔案 | 內容 |
|---|---|---|
| **L3** | `Assets/_Project/Tests/EditMode/TraversalClassifierTests.cs` | **主要價值所在**。①`Vault1m`／`Climb1m`／`Climb2m` 各自正例 ②**W2 每一個 `RejectReason` 至少一條負例** ③**遲滯**：`Height` 在 1m／2m 交界、`Depth` 在 `vault1mMaxDepth` 交界來回微抖時**不得逐幀翻類別** ④退化輸入（零長度 `Forward`、`NaN`、無命中）**必須回 `None`**，⛔ 不得回最近的類別（`snap-to-default` 缺陷類型） |
| **L0** | `Assets/_Project/Tests/EditMode/ArchitectureRegressionTests.cs` | **A-n1**：`TraversalProbe.cs` 是 `Core/Environment` 內**唯一**出現 `Physics.` 的檔案 **A-n2**：traversal debug 區段不得出現 `Physics.`／`Raycast`／`CheckCapsule`（**比照既有 A34 的寫法**） **A-n3**：`TraversalCandidate`／`TraversalKind` 不得出現在 `PlayerRuntimeData.cs`（守住 T1／T5 不被偷跨） **A-n4**：`JumpState.cs` 不得引用任何 `Project.Core.Environment` 型別（守住「Jump 結構上不受影響」） |
| **L4** | `Assets/_Project/Tests/PlayMode/TraversalProbePlayModeTests.cs` | 以 `GameObject.CreatePrimitive(PrimitiveType.Cube)` 造真實障礙（既有 `JumpLandingPlayModeTests` 已示範 runtime 組 `CharacterController`）。案例見 §5 的 L4 表（含 1m 薄牆／1m 厚平台／2m／2.5m／有天花板／45° 斜面） |
| **L4** | 同上 | **Jump 零回歸**：同一場景跑兩次跳躍——一次掛 `TraversalProbe`、一次不掛——斷言 apex 高度、滯空時間、`JustLanded` 時點一致 |

### 9.4 完成定義

- [x] `dotnet build` 0 errors（見 memory 的 compile-without-unity 流程）
- [x] 上述 L0／L3／L4 測試全綠，且**既有全套 EditMode 不得有任何新紅燈**
- [x] `docs/02-dev-spec.md` §2.1 補上順序 2.7；`docs/18` §1.1 的 **O-T** 標為已解除前置條件
- [x] `WORKLOG.md` 交辦段更新為實際完成狀態
- [x] ⛔ **停在 Integration Gate**：把「需要使用者在 Unity Editor 做的事」一次列成 checklist
      （把 `TraversalProbe` 掛到角色、設 `obstacleMask`、烘 `Vault1m`／`Climb1m`／`Climb2m` 的 `MotionBakeData`、
      調 W4 的 tuning 值、Play 看 debug 線），**不要自己動資產，也不要碰 Git**

### 9.5 交付後的下一步（✅ 已由 §10 工作包執行）

`TraversalState` ＋入口＋實際執行已於 2026-09-12 依使用者明確授權進入 scoped Trial，治理記錄見
`docs/ADR/008-traversal-integration.md`。ADR-006／007 仍缺各自的既有驗收證據，未被虛構翻成 Accepted；
本次授權不構成一般 Trial 數量規則的永久放寬。

---

## 10. Traversal V1 Integration（2026-09-12）

### 10.1 Runtime / Data

- `StateType` 新增且只新增 `Traversal`；`Vault1m／Climb1m／Climb2m` 共用一個 `TraversalState`。
- `TraversalState.CanEnter` 以 `JumpRequested && IsGrounded && probe.Candidate.IsValid` 准入，並在該方法內
  commit candidate；`OnEnter`／執行期不回讀 Probe、不重新 query／classify。
- `TraversalStateParamsSO` 沿用既有 `StateParamsSO` seam，只含三格 `animationKey + MotionBakeData`，
  不建立 dictionary、library 或每 kind 一個 state。
- `FullBodyStateMachine` 組裝時注入同物件上的可選 `TraversalProbe`。仲裁仍完全使用既有
  `StateRule.Priority／CanBeInterruptedBy／ValidTransitions`；`JumpState.cs` 保持不變。
- 三種 kind 的 `OnUpdateMotion` 只走 `MotionDriver.ExecuteCommittedCurveMovement`。動畫尚未真的播放時
  不讀 stale normalized time；binding 缺失時不退回普通 gravity，仍由同一 MotionDriver 路徑同步接觸狀態。
- completion 前 `CanTransitionAway == false`。完成後由 authored transitions 決定：`Jump` 排第一，
  grounded 時其 gate 拒絕後回 Move／Idle；airborne 時既有 Jump fall-entry 接手。

### 10.2 Automated Verification

- Runtime build：0 errors（既有 framework warning 不計為 compile error）。
- `TraversalStatePlayModeTests`：T1 None→Jump、T2–T4 三 kind→同一 Traversal、T5 commitment 不受 Probe 改寫、
  T6 空中拒絕、T7 VerticalCurve＋grounded sync、T8 grounded exit、T9 airborne→Jump Falling；另守動畫未開始
  不讀 stale normalized time。
- `ArchitectureRegressionTests`：A13′ topology、A39–A42 既有 Probe/Debug/blackboard/Jump 守衛，
  A43 committed motion／零 query／零第二 writer，A44 Environment seam 白名單。
- 2026-09-12 production 專案由 Unity CLI 實跑：Runtime／Editor compile **0 errors**；EditMode **363 total／362 passed／0 failed／1 existing ignored**；PlayMode **26／26 passed**。完整 PlayMode 同時覆蓋既有 Jump／Roll／Action／Locomotion，沒有新增 regression。
- `TraversalIntegrationWiringTests` 3／3：鎖住玩家 Config priority／committed rule／transition 順序、三格 Params＋有效非平線 bake、X Bot 使用正確 Config 並恰好解析三個 Animancer mapping。

### 10.3 Production Asset Wiring（2026-09-12）

- `TraversalStateParams.asset` 已接 `Vault1m／Climb1m／Climb2m` 三格 animation key＋MotionBakeData。
- `PlayerStateMachineConfig.asset` 已新增 Traversal rule：Priority 30（高於 Jump 10）、`CanBeInterruptedBy` 空、`ValidTransitions = Jump → Move → Idle`；Idle／Move 可被 Traversal 打斷。
- `X Bot.prefab` 的 `AnimancerFacade.transitionMappings` 已新增且只新增三個 base-layer key，分別引用直接使用 FBX sub-clip 的 `Vault1m.asset／Climb1m.asset／Climb2m.asset`，沒有複製 AnimationClip。
- 既有 Motion Bake pipeline 以 30 FPS 產出 `Bake_Vault1m／Bake_Climb1m／Bake_Climb2m`：duration 2.90／2.17／3.60 秒；垂直峰值約 1.02／1.18／2.00m，水平與垂直 curve 均有實際位移。
- 專案已安裝官方 Unity CLI 1.0.0-beta.9，並安裝 `com.unity.pipeline` 0.7.0-exp.1 供可重跑的 Editor automation／test command 使用。

### 10.4 Historical V1 Play Validation Gate

程式與接線已完成；以下只剩人類觀感與幾何 alignment 證據：

1. Play 驗：1m 薄牆 Vault、1m 厚平台 Climb、2m Climb、無障礙 Jump、Vault 後落差→Falling。
2. 記錄起點對齊誤差、頂面終點誤差、穿模、踏空、手腳接觸偏差。

上述 V1 Gate 已產生 §11.1 的實測證據；alignment、selection 與 recovery 問題已由 V2 runtime 處理。

---

## 11. Traversal V2（2026-09-12）

### 11.1 V2 理由：三項人類 Play 證據

以下不是理論假說，而是使用者實際跑過 Vault1m／Climb1m／Climb2m 後的觀察：

1. 固定 baked trajectory 對實際障礙高度、厚度與 entry distance 太敏感，root/capsule 的起點、終點與接觸點有明顯 alignment error；因此 Motion Warping 已是必要項。
2. 部分低平台 Normal Jump 已能自然登上，但 V1 只看幾何 classification，會被 Climb1m 搶走，手感不合理。
3. 主要位移與安全落點已完成後，V1 仍鎖到動畫 100%，收手／站直／pose settle 形成可感知的 recovery latency。

### 11.2 Jump-vs-Climb selection

- `TraversalClassifier` 不變，仍只回報 `Vault1m／Climb1m／Climb2m／None` 幾何事實。
- `TraversalSelectionPolicy` 是 static、deterministic、無 Physics／Time／blackboard 的 gameplay policy。
- Normal Jump capability 從既有 ground Jump bake 的 launch velocity、gravity 與 authored multipliers 推導：`apex = v² / (2g)`，再扣 `normalJumpReachSafetyMargin`。
- `Climb1m／Climb2m` 在 safe reach 內時退回既有 Jump arbitration；超出才進 Traversal。`Vault1m` 不受 Climb policy 過濾。
- Jump authority 無效時採保守 Traversal，避免因壞資料錯誤取消已分類的 Climb。

### 11.3 Committed Motion Warp

- `TraversalState.OnEnter` 只用 committed `TraversalCandidate`、entry pose 與 binding bake 建立一次 `TraversalWarpPlan`；執行中不回讀 Probe，Probe 後續變化不會改 plan。
- 水平保留 baked speed curve 的累積進度並映射到 candidate destination；垂直保留 `VerticalCurve` shape，以 scale／correction 對到實際 target Y；yaw 在同一 committed window 內對齊 traversal forward。
- `warpStartNormalizedTime` 前保留起手；`warpStart → warpEnd` 用 smoothstep 吸收 correction；`warpEnd` 後固定在 committed target，避免 visual tail 繼續推 capsule。
- 每幀只算 `warpedCurrentPosition - warpedPreviousPosition`；實際位移仍由 `MotionDriver` 的 `CharacterController.Move` 執行，不直接寫 `transform.position`。
- Null／空 curve、非有限值、零 baked displacement、超出 tolerance、零 direction、非正 duration 都拒絕 plan，回退既有 committed motion；無效 plan 不開 early recovery。

### 11.4 Recovery

- 三格 binding 各自 author `recoverNormalizedTime`；有效值至少不早於 `warpEndNormalizedTime`。
- `TraversalState` 分開 `_canRecover` 與 `_isFinished`。有效 warp 已完成且到 recover 門檻即可 `CanTransitionAway`，不等待動畫 1.0。
- recovery 後由既有 `ValidTransitions = Jump → Move → Idle` 與 animation blend 接手：持續前進可接 Move，無輸入可回 Idle；不硬切動畫、不新增 Recovery state。

### 11.5 Automated Verification

- Unity 6000.5.1f1 batch compile：0 errors。
- focused EditMode：53／53；focused PlayMode：22／22。
- full EditMode：379 total／378 passed／0 failed／1 existing ignored。
- full PlayMode：36／36 passed；同套件涵蓋既有 Jump／Roll／Action／Locomotion regression。
- Architecture A45／A46 守 selection policy 無 query／Time／blackboard、Jump 不依賴 Probe、warp 無 Physics／direct position write、新增零 blackboard traversal 欄位，且 movement execution authority 仍為 MotionDriver。

### 11.6 Integration Gate

Runtime code、tests、docs 已完成；不修改既有 asset。使用者在 Unity Editor 一次完成：

1. 分別調 Vault1m／Climb1m／Climb2m 的 `warpStartNormalizedTime`、`warpEndNormalizedTime`、`recoverNormalizedTime`。
2. 只有 default tolerance 拒絕合理 target 時，才調 `maxHorizontalCorrection`／`maxVerticalCorrection`。
3. Play 測低平台走 Normal Jump、Vault1m、Climb1m、Climb2m。
4. 記錄 target 對齊誤差、腳／手視覺接觸偏差、recovery latency、是否穿模／踏空。

若 warping 後 root／capsule 軌跡正確而只剩手腳接觸點偏差，下一輪才評估 Hand／Foot IK；V2 不預先加入 IK。

---

## 12. Traversal V3（2026-09-13）

### 12.1 已確認的 Playtest 根因

本輪以既有 V1／V2 Play 證據為前提，不再把問題誤寫成「capsule 太小」或「貼牆卡住」。真實問題是：

1. `probeMinSpeed`／`TooSlow` 把速度錯當 sensing／legality，造成 stationary traversal 失效。
2. Candidate 只代表環境幾何成立，缺少 entry legality，因而可能隔空執行。
3. endpoint-only Motion Warp 沒有 Contact／Transfer constraints，root 到終點正確也不代表途中手與身體合理；本次實際盤點更確認三支正式 Bake 尚未序列化 Traversal block，所以 Play 確實走 V2 endpoint fallback，而不是 V3 contact execution。
4. fixed standing `CharacterController` 與 traversal animation body envelope 在某些 phase 可能不一致；這是 collision mapping 問題，不預設是哪個尺寸錯。
5. 缺少 animation contact sampling 與 runtime hand target mapping，無法量測、更無法約束接觸誤差。

### 12.2 Stationary sensing 與 Entry legality

- Grounded 時 `TraversalProbe` 每幀維護 snapshot，不再用速度 gate。有效 `MoveDirection` 優先；零輸入時使用 actor committed facing，並記錄 `Move／Facing` direction source。
- `JumpRequested` 才是 traversal intent；按鍵當下只消費既有 snapshot，不發第二次 query。
- `TraversalEntryPolicy` 是 pure data transform，輸出 desired root pose、wall/capsule clearance、longitudinal band/error、lateral error、facing error、`Executable` 與精確 reject reason。距離採非對稱 `maximumFarError／maximumCloseError`：`AlreadyClose` 且 standing capsule 對 wall plane 仍安全時允許；只有超過 close 安全下限或 capsule penetration 才回 `TooCloseUnsafe`。太遠、側偏、朝向則分別回 `TooFar／LateralOutOfRange／FacingOutOfRange`。
- Candidate 與 `EntryEvaluation` 都不進 `PlayerRuntimeData`；成立時由同一 `TraversalState` commit。

### 12.3 Ledge、Contact 與 Motion Map

- Candidate 新增 `TraversalLedgeFrame { EdgeOrigin, Tangent, WallNormal, TopNormal, TangentMin, TangentMax }`。Probe 只量出合法 ledge interval，不替動畫選手點。
- `MotionBakeData` 新增 optional `TraversalMotionBakeBlock`。Editor 只 author 左／右手 contact、transfer、exit、recovery normalized time 與 hand role／bone；`MotionBakeEditor` 自動採樣 marker 上的 baked root pose／yaw 與 hand-in-root transform。FBX 與 Animation Events 均不修改。
- `TraversalPlanBuilder` 在 commit 時把角色中心投影到 ledge interval，依 baked hand separation 選 target，套 margin clamp；窄 ledge 可縮 separation 且不得交叉，單手 Vault 亦可成立。
- `TraversalRootConstraintSolver` 支援單手 translation solve；雙手 contact 以兩個 marker 各自所需 correction 的 shared weighted correction 聯合求解，保留 baked root travel，避免極短 Left→Right window 來回拉扯。yaw 服從 traversal forward。所有 solve 只做一次，hot path 只讀 committed result。
- `TraversalWarpPlan` 固定六個 knot：Entry、Left Contact、Right Contact、Transfer／Clearance、Exit、Recovery。每段 deterministic smoothstep，輸出仍是 `WarpedPose(current)-WarpedPose(previous)`，唯一移動出口仍為 `MotionDriver`／`CharacterController.Move`。
- Contact target 現在會形成 required root pose 並直接寫進 Contact knot；plan 同時保存 original animated hand、warped animated hand、world target、error vector/distance，不能再用「只有 target 點」冒充 constraint 已執行。
- Contact translation 仍是 soft constraint：PlanBuilder 由 committed `root wall distance - capsule wall clearance` 還原 standing capsule support distance，將 contact root 限制在 wall 外；若安全夾限造成手部殘差，debug／IK 處理殘差，不為了 0 誤差把主體拉穿牆。
- Vertical correction 由 Entry→Contact→Transfer→Exit 分段吸收；Transfer target 先取得 authored clearance 且在 transfer 前維持 root 不穿過 wall plane。Exit 後直接抵消 animation tail root motion並鎖定 committed destination 到 Recovery，避免平台高度在尾段才 snap。

### 12.4 Corridor 與 Collision Mapping

- `TraversalProbe` 以 standing capsule 對 Entry→Clearance→Transfer→Exit 做固定容量的分段 `CheckCapsule` evidence；destination 合法但 corridor blocked 仍拒絕。Candidate 保存 corridor reject reason 與四個 root samples，State／Motion／debug 不重查。
- `TraversalCollisionProfile` 正式成為每格 binding 的 committed data。C0 固定 capsule 是 baseline；C1 `normalizedTime→centerOffset` 已接入並由 `MotionDriver` 唯一寫 `CharacterController.center`。
- 原始 center／height／radius 在 enter 快照；Exit、early recovery、failure／fallback 都走 idempotent restore。MotionDriver 同時記錄 requested/actual/blocked displacement、CollisionFlags 與實際 capsule。
- C2 heightScale 與 C3 radiusScale 是明確 serialized seam，但本輪沒有 Play 證據，因此保持 disabled、runtime 不啟用。若後續證據要求 height，必須 bottom-anchor preserving；若要求 radius，restore 前必須證明 full-size capsule 合法。Root/controller offset、body/limb proxy 同樣只在 C0–C3 仍不足時提出最小方案，不新增第二 movement authority。

### 12.5 Debug 與 Quality Gate

Development／Editor debug 全部只讀 Probe snapshot、EntryEvaluation、committed plan、Hand IK target/pose pipe 與 MotionDriver result。Entry 面板顯示 current/desired root、wall distance、ideal、far/close tolerance、distance band、lateral/facing error、capsule clearance 與精確 reason；Contact 顯示左右手 animated/target/error vector/distance；Vertical 同時顯示 original/warped trajectory、current/platform Y 與 contact/transfer/exit height error；IK 顯示 implemented/bound/configured/enabled/active 狀態、target、animated/solved position、weight 與 residual。Game View 使用 runtime TextMesh 主通道，Scene View 使用 Gizmos；都不重查 Physics。

最小 `TraversalHandIKController → TraversalHandIKTargetData → TraversalHandIKRig(OnAnimatorIK)` 已沿用既有 Presentation Pipeline／Animator IK seam。它只讀 MotionDriver 發布的 committed plan，不讀 Probe、不改 root/capsule；每手以 authored start/full/release 與 Exit 建 0→1→hold→0 window，position 優先，rotation weight 可為 0。V3 plan、timing、pose 或 finite/residual safety 任一不成立時 weight 歸零並顯示原因；IK disabled 時 root trajectory 不變。Foot IK 未改動。

### 12.6 Automated Verification

- Unity 6000.5.1f1 import／compile：0 errors。
- Correctness／Architecture focused EditMode：80／80 passed；Traversal focused PlayMode：31／31 passed。
- full EditMode：407 total／406 passed／0 failed／1 existing skipped。
- full PlayMode：41／41 passed，涵蓋 Jump／Roll／Action／Locomotion regression。
- 新增 safe-close／unsafe-close Entry、HC contact measurement/shared correction、V1–V5 vertical continuous/early/hold 與 IK window cases；Architecture A49 守 Hand IK 不查 Probe/Physics、不改 root、不進 blackboard。

### 12.7 Integration Gate

程式、自動驗證與文件已完成；未修改任何 FBX、`.meta`、`.unity`、`.inputactions` 或既有第三方動畫／序列化資產值。使用者在 Unity Editor 一次完成：

1. Vault1m、Climb1m、Climb2m 各自 author Entry、需要的 hand contact（可單手）、Transfer、Exit、Recovery marker，選正確 hand bone，然後重烘 MotionBakeData；Game debug 必須由 `Endpoint fallback` 轉為 `Piecewise V3`。
2. 在 Player Root 掛 `TraversalHandIKController`，在 Animator 同物件掛 `TraversalHandIKRig`；每支 animation author left/right IK start/full/release，第一輪 rotation weight 保持 0，以 position correctness 為主。
3. 依實際站位調 `desiredWallDistance／maximumFarError／maximumCloseError`、lateral／facing tolerance；貼牆但 capsule clearance 合法應顯示 `AlreadyClose / Accept`，真正侵入才顯示 `TooCloseUnsafe`。
4. Play：靜止面牆 Jump、正常距離、合法貼牆、太遠 Reject、真正 unsafe 太近 Reject、側偏／朝向 Reject、Vault1m、Climb1m、Climb2m。
5. 記錄 Left／Right contact error、IK animated/solved residual、original/warped/platform Y、Exit root error、Recovery latency、requested-vs-actual displacement；確認 Exit→Recovery 不再有大 Y correction。

本 Gate 不擴充 Free Climbing、Ledge Hang、Moving Platform、Air Grab 或其他玩法。

---

## 13. Repair Phase 1 — Runtime decidability ＋ Root/Pose 同步（2026-09-13）

> **依據**：`docs/23-traversal-motion-mapping-review.md` §A／§E-2／§E-3／§I。
> **狀態**：runtime ＋ tests 完成。四個 assembly `dotnet build` 0 errors；
> 測試已在**使用者開啟中的 Editor** 實跑（`unity cmd run_tests`）：
> **EditMode 421 total／420 passed／0 failed／1 existing skipped**、**PlayMode 41／41 passed**。
> ⛔ `unity test` 會另開 batchmode 而撞 project lock；要用 `unity cmd run_tests`，PlayMode 需 `--async_tests true`。

### 13.1 修掉的兩個模型缺陷

**① Root 播放頭時間重映（`docs/23` §A2，本輪最大缺陷）**
`TraversalWarpPlan.TryEvaluate` 的 endpoint 分支原本以
`trajectoryNormalized = Lerp(warpStart, 1, t)` 把整條 baked trajectory 壓進 warp window，
**但沒有同步改動畫播放速度** ⇒ root 比 animation pose 快。
以三支正式 bake 實測（**correction ＝ 0、平台高度與 bake 完全吻合**）垂直去同步峰值：
Climb1m **+0.82 m**、Climb2m **+0.53 m**、Vault1m **−1.02 m**。

改為 **root 時間 ≡ 動畫時間**：baked 曲線一律以 `normalizedTime` 取樣。
warp window 自此**只控制 correction 融入的快慢**，不再控制 baked shape 的播放速度——
要壓縮／拉伸位移屬 playback speed 的職責（Horizon GDC 2017）。
回歸金絲雀：`TraversalWarpPlan.TrajectoryNormalizedAt(n)` 必須恆等於 `n`。

**② 垂直用乘法、水平用加法（`docs/23` §E-2 缺陷②）**
原 `Lerp(V, V×scale, w)` 讓絕對修正量正比於「動畫自己已經爬了多高」，結構性地把修正集中到尾段。
改為與水平同型的**加法** `V(n) + verticalCorrection × w(n)`；兩軸共用同一個 weight 與同一個時間基準。
`_useVerticalScale` 欄位移除；`VerticalScale`／`HorizontalScale` 保留為純資訊性指標，不參與 evaluation。

### 13.2 Correction 窗改由 bake 推導

新增 `MotionBakeData.GetVerticalSettleNormalizedTime()`：回傳 `VerticalCurve` 最後一次實際改變之後的
第一個取樣點——動畫自己「爬完了」的時刻。**這不是 tuning 值**：三支 clip 的尾段是精確的常數平台，
只需要浮點雜訊等級的 epsilon（`1e-4`），不需要百分比門檻。

| Bake | duration | settle（normalized） | 之後的靜止尾段 |
|---|---|---|---|
| `Bake_Vault1m` | 2.900 s | **0.460** | 54.0%（跑出） |
| `Bake_Climb1m` | 2.167 s | **0.569** | 43.1%（站定） |
| `Bake_Climb2m` | 3.600 s | **0.833** | 16.7%（站定） |

`TraversalMotionBinding.HasAuthoredWarpEnd` 為 `false` 時，`TraversalState.ResolveEndpointWarpEnd`
改用上表推導值，取代涵蓋整支 clip 的常數預設（舊值 0.8，比實際動作窗寬 2–5 倍）。
**已 author 的 binding 行為不變。**

### 13.3 決策可觀測性

| 決策層 | 之前 | 現在 |
|---|---|---|
| **Selection** | 回 `bool`；否決時**零輸出**（畫面顯示 `Climb1m` ＋ `Entry: Accept` 卻什麼都不發生） | `TraversalSelectionPolicy.Evaluate` 回 `TraversalSelectionEvaluation`（verdict ＋ `TraversalSelectionReason` ＋ ledge 高度／apex／safeReach）。`TraversalState.CanEnter` 推給 `TraversalProbe.RecordSelectionEvaluation` |
| **Plan 建立** | `TryCreate`／`TryBuild` 靜默回 `false` | 新增帶 `TraversalWarpPlanRejection` 的多載（既有簽章保留為薄包裝）。endpoint 另回報實際需要的水平／垂直 correction 量 |
| **執行中的 plan** | 只有「Piecewise V3／Endpoint fallback」兩態 | `TraversalPlanMode`：`Piecewise`／`EndpointFallback`／**`Failed`**（兩條都建不起來、正在跑純 baked 曲線）。`TraversalWarpPlanDiagnostics` 由 `TraversalState` 填、`MotionDriver` 保存 |

⚠️ **`BeginTraversalPresentation` 現在即使 plan 無效也會呼叫**——「traversal 開始了但完全沒有 warp」
正是最需要被看見的狀態。`ExecuteCommittedCurveMovement` 也會刷新面板。

### 13.4 Debug（`docs/23` §H）

- **面板不再出現任何 `Vector3` 全值**，也不重印容差常數。Probe 面板三行＝分類／entry／**selection**；
  MotionDriver 面板＝plan 模式與理由／`Root/Pose dt`／`dY applied`／contact 純量／IK 狀態。
- 新增世界空間繪製：entry 的 **L 形誤差拆解**（沿 wall normal 的 longitudinal ＋ 沿 tangent 的 lateral）、
  **合法距離帶的近／遠兩條界線**、facing 與 desired facing 箭頭、
  **當前格的 original ● 與 warped ◎ 及其連線**（這條線的長度就是實際套用的 correction）。
- endpoint fallback 下不再什麼都不畫：改畫窗的兩端與 destination；
  `Failed` 時畫紅色 `NO WARP PLAN` 標記並附理由與超限量。

### 13.5 測試

新增 `Assets/_Project/Tests/EditMode/TraversalBakedAssetTests.cs`——**對出貨資產本身**的回歸
（`docs/23` §A9：既有 traversal 測試全部跑手工 `ScriptableObject`）：

| 測試 | 驗什麼 |
|---|---|
| B1 | 三支正式 bake 的 root 時間 ≡ 動畫時間 |
| B2 | correction ＝ 0 時 warped 逐格等於 original（修正前峰值 0.53–1.02 m） |
| B3 | settle 由曲線推導且其後真的是平的 |
| B4 | 動作結束時垂直 correction 已 100% 融入 |
| B5 | 動作結束後 **correction**（非絕對 Y）不再變化 ⇒ 無結尾 snap |
| B6 | Vault1m 的水平超限回報 `HorizontalCorrectionExceeded` ＋ 需求量，不得靜默 |
| B7 | 三支 bake 的 piecewise 不可用理由必須是 `MissingTraversalBlock`（Phase 2 的目標） |

`TraversalSelectionPolicyTests` 增 S1–S5（否決理由與判準數字）。
既有 `PreferTraversal` 的 bool 多載與 J1–J4 原樣保留。

**兩條既有測試的斷言編碼了被移除的舊語義，已隨同一工作包更新**（不是放寬）：
`WARP4`（改驗「warpEnd 時 correction 融入 100%、動畫結束時抵達 destination」，另補 `WARP4B` 時間基準）與
PlayMode `V2_CommittedWarpEndsAtCandidateAndIgnoresLaterProbeTarget`（量測點由 `n=0.8` 改為 `n=1.0`）。

### 13.6 本階段**不**碰的（`docs/23` §J）

`maximumCloseError` 等 entry 距離容差、capsule 尺寸、`TraversalCollisionProfile`、
hand rotation weight、`SolveDual`、Hand IK 元件掛載（Phase 3）、
Vault1m 的 3.26 m 水平總位移本身（Phase 2）。
`RecoverNormalizedTime` 仍沿用 binding 的 warp end，**未**改用推導值——recovery 是 gameplay 交接時機，
與 correction 窗是兩件事，本階段不動。

### 13.7 Phase 1 Playtest 回饋的三項後續（2026-09-13）

**① Debug 顯示重做（使用者：「一堆文字直接顯示在 game 視窗…有夠難看」）**

固定三層分工，Game View **預設什麼都不顯示**：

| 層 | 內容 |
|---|---|
| Scene View gizmo／runtime line | 空間關係：探線、ledge、corridor、entry band、facing 箭頭、original vs warped 軌跡、knot、手部誤差向量 |
| **Window ▸ Project ▸ Traversal Debug**（新增 `Assets/Scripts/Editor/Tools/TraversalDebugWindow.cs`） | 純量與決策鏈：Classify → Entry → Select ／ Plan mode ＋ rejection ＋ correction 預算 ／ 執行期 `root/pose dt`、已套用 dY、blocked 位移 |
| Game View | **無**。真的要看才開新欄位 `drawTraversalPanelText` |

`drawTraversalPanelText`（`MotionDriver`／`TraversalProbe`／`TraversalHandIKController`，預設 `false`）
只閘住 `TextMesh`，不影響線／gizmo。**是新欄位 ⇒ 既有 prefab 未序列化它 ⇒ 一律吃預設值，不需要改 prefab。**

**② 終點固定高 5 cm（使用者：「終點位置仍然有誤」）— 已修**

`DestinationClearanceMargin`（0.05）是 **query 用的抬升量**（避免 `CheckCapsule`／corridor 把落腳面本身
算成阻擋物），但舊版把它直接加進 `destinationPoint`，而那正是 commit 給 warp plan 的終點。
以實際 CharacterController（center y=0.9134／height=1.8266 ⇒ `rootToCapsuleBottom.y=0.0001`）計算，
1.0 m 箱子的 committed 終點是 **1.0499 m** —— 每次 traversal 都停在平台上方 5 cm 再被重力拉下來。

現在分離：`destinationPoint` ＝ 腳踩在表面上；`destinationQueryRoot` ＝ 抬升版，只給 `CheckCapsule`
與 corridor 最後一段使用（**corridor 必須用抬升版**，否則會被落腳面自己擋掉）。

**③ 手的位置（使用者：「手抓的位置稍低」）— 量測結果：問題在水平，不在高度**

以隔離的 preview scene 採樣 `Climb1m` 手骨（⚠️ 必須先 `applyRootMotion = true`，否則 root 完全不動——
這也是 `MotionBakeEditor` line 522 的理由）：

| 量測 | 值 |
|---|---|
| 左手落點時間 | n ≈ 0.20 |
| 落點高度 | 1.016 m（clip 自身 ledge 高度 1.0134 m） |
| **高度誤差** | **+3 mm** |
| 落點相對當下 root | **前方 0.554 m** |

**動畫自身的抓點高度是準的。** 但 entry band 把 root 放在離邊緣 0.45 m，到 n=0.20 時 root 已前進約 0.37 m
⇒ 手實際落在**邊緣內側約 0.45–0.50 m**，是按在平台面上而非抓邊。
⛔ **不得用調整 root 高度去掩蓋**——那會把水平問題偽裝成垂直問題。正解是 Phase 2 的 contact knot。

**④ Climb1m 暫時停用（使用者裁決 2026-09-13）**

`TraversalStateParamsSO` 新增 per-kind 內容開關（`vault1mEnabled`／`climb1mEnabled`／`climb2mEnabled`，
**預設全開**），由 `TraversalState.CanEnter` 傳入 `TraversalSelectionPolicy.Evaluate`，
停用時回 `TraversalSelectionReason.KindDisabled`——**停用是內容決策，但仍然要說得出口**，
不得變成另一個靜默否決。

⚠️ **已知且被接受的後果**：普通 Jump 的 apex 是 **0.9535 m**，因此停用 Climb1m 後
場景中大量的 1 m 厚箱子（`Box_100x100x100`）**上不去**。這是預期結果，不是 bug。
Vault1m 不能頂替：它的 baked 垂直結尾是 0（翻過去落在對面地面），語意上不是「站上平台」。
勾回 `climb1mEnabled` 即恢復，不刪任何程式。

**驗證**：live Editor recompile 0 errors；**EditMode 424 total／423 passed／0 failed／1 existing skipped**、
**PlayMode 41／41 passed**（含全部 `TraversalProbePlayModeTests`，即 destination 改動的實際覆蓋）。

### 13.8 ⚠️ 更正：13.2 的 bake 推導窗原本沒有生效（2026-09-13，live Editor 實測）

Playtest 中以 `unity cmd eval_file` 讀取執行中的 Editor，面板顯示：

```
PLAN mode=EndpointFallback pw=MissingTraversalBlock window=0.000-0.800 derived=False
```

**`derived=False`** —— §13.2 宣稱「未 author 時由 bake 推導」，但實際上**一次都沒執行過**。

**根因**：`TraversalMotionBinding` 的欄位初始式本來就把 `warpEndNormalizedTime` 設成
`DefaultWarpEndNormalizedTime = 0.8`，而 `TraversalStateParams.asset` **沒有序列化這個欄位**
⇒ 反序列化後仍是 0.8 ⇒ 舊的 `HasAuthoredWarpEnd`（「值 > warpStart 就算 author 過」）**恆為 true**。
這是一個「用值反推意圖」的判斷失效案例：**分不出「作者選了 0.8」與「建構子預設就是 0.8」。**

**修法**：改為顯式 opt-in `overrideWarpWindowEnd`（新欄位，預設 `false`；既有 asset 未序列化 ⇒ 吃 false）。
顯式傳入 warp end 的建構子多載（既有 PlayMode rig 使用）設為 `true`，行為不變。
回歸測試 `B8_DefaultBinding_DoesNotClaimAnAuthoredWarpEnd`。

**修正後實測生效值**：

| kind | authored | fallback | derived | **實際窗** |
|---|---|---|---|---|
| Vault1m | false | 0.800 | 0.460 | **0.000–0.460** |
| Climb1m | false | 0.800 | 0.569 | **0.000–0.569** |
| Climb2m | false | 0.800 | 0.833 | **0.000–0.833** |

### 13.9 手部位置的正確歸因（更正 §13.7 ③ 的表述）

§13.7 ③ 量的是 **Climb1m**，但 Playtest 螢幕上實際發生的是 **Climb2m**（live 讀數 `height=1.973`、
`reason=ClimbAboveJumpReach`）。**那個 +3 mm 不適用於使用者看到的畫面。**

更重要的是表述本身會誤導：「高度誤差 +3 mm」量的是 **clip 在自己座標系內是否自洽**，
**不是**手在世界空間有沒有落對位置。後者取決於我們把 root 放在哪裡，而目前：

```
pw = MissingTraversalBlock   ⇒ 完全沒有 contact constraint
```

**現行 build 裡沒有任何機制在把手瞄準 ledge。** 手的世界位置 ＝「動畫相對 root 的姿勢」
＋「一個單一全域 blend 修正過的 root」，兩者都不看 ledge 在哪。

⛔ **這不是等 IK 就會好的問題。** Hand IK 不但沒在跑（元件掛不上，Phase 3 修），
而且 `TraversalHandIKController` 本身就設計成 residual > `maximumResidualDistance`（0.35 m）時
**主動放棄**——IK 是收殘差的，不是拿來搬幾十公分的。用 IK 硬拉只會拉出扭曲的手臂。
正解仍然是 Phase 2 的 contact knot。

---

## 14. Phase 2 —— 接觸標記由動畫自動測出（2026-09-13）

> **前提變更**：使用者 2026-09-13 裁決解除「AI 不碰資產」鐵律，改為
> **可改資產但只能走 Unity Editor API**（正本：`CLAUDE.md`「Unity Asset Authoring」）。
> 本節的 bake 寫入即以 `unity cmd eval_file` ＋ `SerializedObject`／`SaveAssetIfDirty` 完成，未手改 YAML。

### 14.1 為什麼可以自動測

Horizon（GDC 2017）的分工是**動畫決定 WHEN、程式決定 WHERE**。
「WHEN」不該是人憑感覺填的 6 個數字——它是動畫裡客觀存在的事件：
**手先動、然後停住不動的那一刻就是接觸。**

`Assets/Scripts/Editor/Stages/TraversalContactDerivation.cs` 逐格採樣手骨世界座標並偵測：

- 門檻 ＝ 該手在整段動畫中的**最大速度 × 0.15**（相對量，不受動畫快慢／角色尺寸影響）
- 必須**先超過門檻再掉下來**——否則動畫開頭「手還沒抬起來」的靜止會被誤判成接觸
- 低速需連續 ≥3 格
- 只在前 50% 找（後段是站定／跑出，那裡的低速是走路）

⚠️ 採樣前必須 `animator.applyRootMotion = true`，否則 `SampleAnimation` 完全不移動 root（實測）。

### 14.2 三支正式 clip 的測量結果

| clip | 模式 | 接觸窗 | 手相對 root 前方 | transfer | exit |
|---|---|---|---|---|---|
| `Bake_Vault1m` | **LeftHand**（右手全程揮動，從不停住） | 0.207–0.253 | **0.725 m** | 0.253 | 0.460 |
| `Bake_Climb2m` | **BothHands** | 0.065–0.269 | **0.359 m** | 0.269 | 0.833 |
| `Bake_Climb1m` | 測不到（手全程滑動） | — | — | — | — |

**Vault 是單手翻越**——這是資料告訴我們的，不是人猜的。

### 14.3 旋轉空間統一（修 docs/23 §D 的 FIX 項）

舊的 `SampleTraversalAnchor` 用 `Transform.InverseTransformPoint` 存 hand-in-root，
那會帶進 root 的 **pitch／roll 與 lossyScale**；但執行期是以
`Quaternion.Euler(0, yaw, 0) * handInRoot` 還原。新的採樣一律用 **yaw-only 逆旋轉**，兩端對齊。

### 14.4 結果：piecewise 真的走得到了

| 情境 | 結果 |
|---|---|
| Climb2m @ 離牆 0.45 m | ✅ piecewise，手部殘差 **1.7 cm** |
| Climb2m @ 離牆 0.61 m | ✅ piecewise，手部殘差 **1.7 cm** |
| Vault1m @ 離牆 0.45 m | ✅ piecewise，手部殘差 **0.0 cm**（需把 `maxHorizontalCorrection` 1.5 → 2.5） |

殘差已進入 Hand IK 可收的範圍（controller 的放棄門檻是 0.35 m）。

⚠️ **一個測試資料的教訓**：候選的 `entryCapsuleWallClearance` 必須與真實 CharacterController 一致
（`d − radius + skinWidth`）。隨手填一個值會讓 `ClampOutsideWall` 用不成立的下限把 contact solve 夾掉，
測出來的殘差憑空從 1.7 cm 變成 19 cm。差點誤報成程式 bug。

### 14.5 已知未解 —— 每支動畫需要的進場距離不同

由 14.2 反推「這支動畫希望玩家站在離牆多遠」：

| clip | 動畫想要的進場距離 | 目前允許範圍 |
|---|---|---|
| `Bake_Climb2m` | ≈ **0.36 m** | 0.05–0.80 m ✅ |
| `Bake_Vault1m` | ≈ **1.29 m** | 0.05–0.80 m ❌ **完全在範圍外** |

`DesiredWallDistance` 是**單一全域常數 0.45 m**，服務不了兩支需求差 3.5 倍的動畫。
Vault 因此永遠要靠 warp 抵銷約 1.6 m 的助跑＋跑出（`maxHorizontalCorrection` 被迫調到 2.5），
視覺上會有腳步滑動。**正解是把進場距離改成由各自的 bake 推導**，而不是繼續放寬 correction 預算。
這是 Phase 2 的下一步。

### 14.6 驗證

live Editor：EditMode **426 total／425 passed／0 failed／1 existing skipped**、PlayMode **41／41 passed**。
`B7` 由「斷言沒有 Traversal block」反轉為「斷言自動測出的 marker 存在且順序合法」；
新增 `B9`（正式 bake ＋ 真實幾何 ⇒ piecewise ＋ 殘差 < 5 cm）。
⚠️ 資產改動（marker 寫入、`climb1mEnabled`、Vault 的 correction 預算）**只有 Unity 匯入成功，沒有測試保護**。

---

## 15. Phase 2（續）—— 進場距離下放到各支動畫（2026-09-13）

§14.5 記為「已知未解」的項目，本節解決。

### 15.1 推導方式

`MotionBakeData.TryGetDesiredEntryDistance()`：

```
進場距離 = 接觸瞬間「手相對 root 的前伸距離」 + 「root 到該瞬間已走的水平位移」
```

手要落在邊緣上 ⇒ 接觸當下 root 必須退在邊緣後方剛好一個手臂前伸距離；
而 root 在接觸前已自己往前走了一段，所以進場點要再往後推那一段。**兩個量都在 bake 裡，沒有手填常數。**

| clip | 推導值 | 合法範圍（±既有容差） | 原本（全域 0.45） |
|---|---|---|---|
| `Bake_Climb2m` | **0.354 m** | 0.000–0.704 | 0.050–0.800 |
| `Bake_Vault1m` | **1.243 m** | 0.843–1.593 | 0.050–0.800 ❌ 完全不重疊 |

`TraversalEntryPolicy.Evaluate` 新增 `desiredWallDistanceOverride` 參數（`NaN` ⇒ 退回全域設定，
既有呼叫端與合成 bake 的測試行為不變）。距離帶隨推導值整體平移，容差本身不動。

### 15.2 效果：修正量大幅下降，權宜放寬得以撤回

| 情境 | 手部殘差 | 最大水平修正 |
|---|---|---|
| Vault1m @ 1.243（理想） | 0.0 cm | **0.825 m**（原 1.618 m） |
| Vault1m @ 0.90（近端） | 0.0 cm | 1.168 m |
| Vault1m @ 1.55（遠端） | 0.0 cm | 0.518 m |
| Vault1m @ 0.45（太近） | — | **正確拒絕 `TooCloseUnsafe`** |
| Climb2m @ 0.354（理想） | 1.7 cm | 0.280 m |
| Climb2m @ 0.61 | 1.7 cm | 0.536 m |

⇒ `vault1m.maxHorizontalCorrection` 的 1.5→2.5 權宜放寬**已撤回為 1.5**。
合法範圍內的最大需求是 1.168 m，進得了預算。

### 15.3 ⚠️ 這是玩法可見的變更

**貼著薄牆按 Jump 不再翻越**——Vault 現在要求離牆 0.84–1.59 m。
這不是退步，是誠實：`Vault1m` 是**跑動翻越**，接觸前自帶 0.57 m 助跑；
站定貼牆翻需要另一支動畫（standing mount），素材庫裡沒有。
以前它「能觸發」只是因為 warp 硬吃掉 1.6 m 的位移差，代價就是腳步滑動。

被拒絕時會退回普通 Jump（apex 0.9535 m），因此 **1 m 薄牆在貼牆時等於過不去**。
與 Climb1m 停用是同一類的內容缺口，不是 bug。**Play 時請退開約一個身位再按 Jump。**

### 15.4 驗證

live Editor：EditMode **427 total／426 passed／0 failed／1 existing skipped**、PlayMode **41／41 passed**。
新增 `B10`（推導值必須明顯不同、且在各自合法範圍內修正量進得了預算）。
合成 bake 沒有 Traversal block ⇒ 推導回傳 false ⇒ 退回全域設定，既有 PlayMode rig 行為不變。

---

## 16. Playtest 回饋（2026-09-13 影片）：兩個問題，同一個錯誤

使用者錄影回報。**兩件事都是同一類錯誤：我修的是被點名的症狀，不是那個症狀所屬的問題。**

### 16.1 「手抓牢固之後身體還在位移」—— 真正的動作缺陷

Climb2m 的抓握窗是 `n=0.065 → 0.269`（由 `TraversalContactDerivation` 測出）。
但舊的 Transfer knot 指向另一個空間目標（`Corridor.ClearanceRoot`），於是
**整段抓握期間 correction 都在內插**：

```
接觸 n=0.065   correction = (-0.083, 0.172, 0.096)
Transfer       correction 被拉向 clearance root          ← 差距一路施加在 root 上
```

手的世界位置 ＝ **clip 自己的手位置 ＋ root correction**。
correction 一變，手就被拖著在牆面上滑——**沒有 IK 釘住它時，這是必然的**。

**修法**：Transfer 的語意就是「最後一隻手放開的時刻」，因此它的 correction **直接沿用最後一次接觸**：

```
L.corr = T.corr = (-0.083, 0.172, 0.096)      ⇒ 抓握期間 warp 零額外位移
```

抓握期間的位移交還給動畫自己——那支動畫本來就是在手固定的前提下把身體拉上去的。
越過邊緣的 clearance 因此也由動畫提供，不再由一個外加的 transfer 目標硬拉。
回歸測試 `B11_RootCorrection_IsFrozenWhileAHandIsGripping`（knot 層級 ＋ 41 點取樣層級）。

> ⚠️ **量測方法的教訓**：第一次量「手的漂移」時我用固定的 `handInRoot` 去重建手的世界位置，
> 得到 166 cm 的假漂移——但 `handInRoot` 只在接觸那一格成立，之後每幀都在變。
> 正確的指標是**我們唯一控制得了的量：correction 在抓握窗內的變化**。

### 16.2 Debug：把文字從 Game View 搬到 Scene View 不算解決

§13.7 只關掉了 `TextMesh`。但 piecewise 生效後，**6 個 knot 各印 3 行**、加上 hand contact 的
`animated=/target=/error=` 三個 `Vector3` 全值、加上 plan 與 entry 面板——全部疊在角色身上。
使用者的原話是「一堆文字直接顯示在 game 視窗…有夠難看」，**問題是「debug 蓋住要看的東西」，
不是「TextMesh 這個類別」。**

**現在的規則（不再有例外）**：

| 通道 | 內容 |
|---|---|
| Scene View | **只畫幾何**。線、球、箭頭。**零文字**——唯一例外是 `NO WARP PLAN (<reason>)` 一行，因為那個失敗態沒有東西可以畫 |
| Window ▸ Project ▸ Traversal Debug | 全部數值與決策鏈 |
| Game View | **什麼都沒有** |

並且**所有 traversal debug 旗標預設關閉**，prefab 上的序列化值也一併設為 0
（`drawTraversalRuntimeLines`／`drawTraversalSceneGizmos`／`drawTraversalMotionDebug`）。
要看什麼自己開一個通道，不是預設全開。

**順手修掉影片裡那條橫貫畫面的洋紅線**：`DrawHandDebug` 無條件畫 `solved → target`，
而 `solved` 在未綁定／手骨解析失敗時是 `(0,0,0)` ⇒ 畫出一條從世界原點連到手部目標的線。
現在畫之前先確認端點是真值。

### 16.3 驗證

EditMode **428 total／427 passed／0 failed／1 existing skipped**、PlayMode **41／41 passed**。

---

## 17. 對齊的是「動畫自己的邊緣」，不是手腕（2026-09-13）

使用者回報：手的高度不準，而且角色會穿進牆裡；並指出正確的抓握姿勢是
**四指按在頂面、大拇指按在牆面**。

### 17.1 量測：動畫本來就做對了，錯的是對齊點

`Climb2m` 抓握格（n=0.065），以動畫自身地面為基準、動畫自身平台頂面 Y=1.978：

| 骨骼 | Y | Z（前） | 相對頂面 |
|---|---|---|---|
| 手腕 `LeftHand` | 1.8152 | 0.3589 | **−0.163** |
| 拇指 `ThumbDistal` | 1.8410 | 0.4321 | −0.137（貼在牆面） |
| 食指 `IndexDistal` | 1.9270 | 0.4518 | −0.051 |
| 中指 `MiddleDistal` | 1.9371 | 0.4570 | −0.041 |
| 無名指 / 小指 | 1.927 / 1.917 | 0.461 | −0.051 / −0.061 |

四指末端貼著頂面、拇指低一截貼牆面——**與使用者描述完全一致，動畫沒有問題**。

問題是我們把**手腕**釘在真實邊緣上。手腕本來該在頂面下方 16.3 cm、後方 10.3 cm，
硬釘上去等於把整個角色**抬高 16 cm、往前推 10 cm** ⇒ 高度不準 ＋ 身體穿進牆裡。

### 17.2 修法：對齊 grip edge

`MotionBakeData` 的 traversal block 新增 `LeftGripEdgeInRoot` / `RightGripEdgeInRoot`
（接觸瞬間、root 空間、yaw-only），由 `TraversalContactDerivation` 自動測出：

- **頂面高度**：⚠️ 兩種動作要分開，且可由資料分辨——
  **攀爬**（結束時人在上面）取結束高度；**翻越**（翻過去落在對面地面、結束高度回到起點）
  取**垂直曲線峰值**。判準：結束位移 < 峰值一半 ⇒ 視為翻越。
  用錯會讓 Vault 的 grip edge 高度翻負號（實測 −1.020）。
- **牆面位置**：接觸瞬間手部**最前端**的接觸點（四指與拇指 distal 取最大前伸）。手不可能在牆裡面。
- **橫向**：取手腕自身座標，左右手分佈仍由 ledge interval 決定。

`TraversalPlanBuilder` 的 root solve 改用 grip edge；IK／debug 的手部目標則改為
**「grip edge 完美對齊時手腕應該在的位置」**——與實際 warped 手腕的差就是
`ClampOutsideWall` 與雙手平均造成的殘差，**不是恆等式**，可以拿來餵 IK。
舊資產沒有 grip edge 時安全退回手腕，行為與先前一致。

### 17.3 結果

| | 抓握點對邊緣 | 手腕相對頂面 | root 相對牆面 | 殘差 |
|---|---|---|---|---|
| Climb2m @0.354 | dY 0.000／dZ 0.000 | −0.163 m（＝動畫原本的關係） | 後方 0.462 m（不穿模） | 0.0 cm |
| Vault1m @1.243 | dY 0.000／dZ 0.000 | +0.017 m（手掌壓頂面） | 後方 0.883 m | 0.0 cm |

修正前 root 會被推到牆面後方僅 0.359 m（Climb2m）⇒ 身體穿進牆裡。現在往後退了 10.3 cm。

### 17.4 驗證

EditMode **428 total／427 passed／0 failed／1 existing skipped**、PlayMode **41／41 passed**。

### 17.5 未做

使用者另外提到「或需要先讓角色調好距離再上去」（進場前先走到正確位置的 adjust 動作）。
目前是**拒絕**不合格的進場（`TooCloseUnsafe` 等），不是自動走位。
per-clip 進場距離（§15）已大幅縮小需要調整的範圍；是否要做 adjust step 待 Play 後再定。

---

## 18. 基準點不一致：Phase 2 自己引入的三個 bug（2026-09-13）

使用者 Playtest 回報「手抓的位置不對、抓牢後還在位移、浮空」，並在我提出「玩法選項」時
明確要求：**去找源碼修，不要給選項。** 結果找到三個 bug，**全部是 §14–§17 自己引入的**，
而且是同一種錯：**該由動畫決定的量，被寫成全域常數，或兩處用了不同基準。**

### 18.1 先排除的可能（實測，不是推論）

從執行中的 Editor 讀出真實資料後確認：

| 檢查 | 結果 |
|---|---|
| 執行期用的是不是 piecewise | ✅ 是（`mode=Piecewise`, rejection=None） |
| 抓握期間 correction 有沒有凍結 | ✅ `corr T` 與 `corr L` 完全相同 |
| 路徑會不會被碰撞擋住 | ✅ 41 取樣點全通；`maxBlocked` 僅 1.14 cm |
| 站定後是否浮空 | ✅ **沒有**。腳趾 2.0025／2.0042 vs 平台 2.0028（誤差 1.4 mm），Foot IK 補償 −0.0299 正確 |

⇒ 計算、碰撞、執行路徑都沒問題。**問題在「計畫要求的位移，動畫裡沒有」。**

### 18.2 Bug 1 — 進場距離與接觸解算用了不同基準

§17 把 root solve 從「對齊手腕」改成「對齊 grip edge」，但
`TryGetDesiredEntryDistance` 仍用手腕前伸距離。

```
告訴玩家站   0.354 m（手腕 0.359 基準）
實際要求     0.462 m（grip edge 0.461 基準）
⇒ 站得完全正確也被硬拉 10.7 cm；站偏 18.5 cm 就變成 30 cm 猛拉
```

**基準不一致比基準不準更糟**——後者至少一致，前者連「站對」都不存在。

### 18.3 Bug 2 — 左右手目標對稱擺放

舊版取 authored separation 對稱分到 `centerCoordinate` 兩側。
但動畫的抓握中心不在 root 正前方：`Climb2m` 左手 −0.206、右手 +0.373，**中心偏右 8.35 cm**。
對稱擺放 ⇒ 強迫 root 橫向補那 8.35 cm。

改為**每隻手用自己的橫向偏移**：`target = edge + tangent * (centerCoordinate + gripEdge.x)`，
理想站位的橫向修正即為 0。ledge interval 的夾限改成同時約束兩手。

### 18.4 Bug 3 — 終點用了探測常數

`TraversalProbeSettings.Vault1mMaxDepth`（0.6 m）是**用來找落腳面在哪**的探測距離，
卻被 `destinationPoint` 直接當成 committed 終點。

```
Climb2m 動畫自然停在邊緣內側 0.212 m
卻被要求走到               0.600 m
⇒ 放手之後還要被往前推 0.39 m —— 那就是「抓牢後還在位移」
```

新增 `MotionBakeData.TryGetNaturalExitDepth()` ＝ `H(end) − 理想進場距離`。
自然落點比探測點近時用它（仍在已驗證的落腳面上）；更遠時（Vault 的長跑出）維持探測點。

### 18.5 結果

| 情境 | 修正前 | 修正後 |
|---|---|---|
| Climb2m 理想站位・接觸修正 | 0.303 m | **0.025 m** |
| Climb2m 理想站位・離開修正 | 0.388 m | **0.025 m** |
| Climb2m 站偏 ±0.15 m | — | 0.152 m（與偏差等量，合理） |
| Vault1m 理想站位・接觸修正 | — | **0.027 m** |
| Vault1m 理想站位・離開修正 | — | 0.666 m（**未解**，見 18.6） |

理想進場距離同步更新：**Climb2m 0.462 m、Vault1m 1.402 m**。

### 18.6 未解

`Vault1m` 的離開修正仍有 0.666 m：該 clip 總水平位移 3.259 m，自然落點在邊緣內側 1.857 m，
遠超過探測到的落腳面深度 0.6 m。**這是 clip 尾段的跑出（run-out）**，
要靠修剪播放區間或改 playback speed 解，不是靠加大 correction 預算。

### 18.7 驗證狀態（2026-09-14：原 M6 已由 §20 關閉）

當輪只確認編譯乾淨（live Editor `compilationFailed=false`），測試因使用者仍在 Play Mode 而未跑。
**次輪已補跑 EditMode：428 total／426 passed／1 failed／1 existing skipped。**

```
TraversalWarpPlanTests.M6_NarrowLedge_ShrinksAndClampsHandSeparation
  Expected: greater than or equal to -0.0700999945f   But was: -0.200000003f
```

§18.3 把雙手橫向目標從「authored separation 對稱分到中心兩側」改成「每隻手用自己的動畫橫向偏移」，
**窄 ledge 因此不再壓縮雙手間距**，手會落到合法區間外。這是真實的行為變更，不是 flake。
參考實作研究（`docs/24` §2／§8）支持**不壓縮間距**（壓縮＝偽造動畫做不到的姿勢），
因此這條測試編碼的是**已被取代的 baseline**，應與「contact target 降為單主手」的改動放同一個工作包更新。

**2026-09-14 更新**：工作包二已以「單一 primary contact 解 root；secondary 查 committed ledge surface」
取代雙手平均；M6 改守「窄 ledge 不壓縮 authored hand separation，沒有 secondary surface 就停用該手 IK」，
原紅燈已關閉。完整證據見本檔 §20 與 `docs/24` §16。

另見 `docs/24` §9.3：本節的修正都在「traversal 開始之後」，
而實測顯示**玩家在 1 m 級障礙物前多數時候根本觸發不了 traversal**（entry 合法窗與感測範圍矛盾）。
先修那個，再談這裡的殘差。

---

## 19. 工作包一 —— Animation Fitting layer ＋ 感測範圍推導（2026-09-13）

> **正本在 `docs/24` §10–§12。** 本節只記「`docs/22` 的讀者需要知道什麼變了」。

**鏈路改變**（研究結論，`docs/24` §8）：

```
Probe → Selection → 【Animation Fitting】 → TraversalPlan → Root Warp → Contact → IK
```

新增的一層是**純函式** `TraversalAnimationFitter`（`Core/StateMachine/TraversalAnimationFit.cs`），
坐在 Selection 與 Plan 之間，回答「這支動畫要怎麼播才貼近現場尺寸」。
v1 **只做 playback rate**，不做 clip selection、不做 start-time scrub。

**三項變更**：

1. **感測範圍改由動畫需求推導**（取代 authored 常數 1.25 m）。
   `TraversalStateParamsSO.GetRequiredSensingReach()` ＝
   `max(啟用中 kind 的理想進場距離 + MaximumFarError) / cos(MaximumFacingAngle)`；
   由 `TraversalState` 推給 `TraversalProbe.SetDerivedRangeRequirements()`，Probe 取
   `max(authored, derived)`。實測 1.25 → **2.138 m**。
   ⇒ Vault 的可執行站位從約 **20 cm** 變成 **0.70 m**（1.00–1.70 m）。

2. **落地預算與分類證據分離**。`Vault1mMaxDepth`（0.6 m）只保留分類用途；
   新增 `TraversalCandidate.LandingSurfaceDepth`，Probe 沿**實際落腳面**
   （Climb 走平台頂面、Vault 走牆另一側地面）續掃到動畫需要的深度。

3. **`MotionBakeData.TryGetNaturalExitDepth()` 改量在 Exit marker**（原本量在 clip 結尾）。
   Vault1m 舊值 1.857 m ⇒ 實際 1.116 m，差的 0.74 m 原本全變成 exit correction。
   **與 §18.2／§13.8 同型：兩處用了不同基準。**

**效果（離線重建，真實場景與資產）**：理想站位的**水平** correction
contact **0.015 m**、exit **0.765 → 0.001 m**。
⚠️ 垂直 correction 不在此列——矮障礙物上仍有固定 −0.33 m，那是 clip 與障礙物高度不匹配，
屬 clip selection，已明確延後。

**驗證**：live Editor recompile 0 errors；**EditMode 441／439 passed／1 failed（`M6`，屬工作包二）／
1 existing skipped**、**PlayMode 41／41**。新增 `F1`–`F7`／`R1`–`R3`／`B12`／`B13`／`A50`，更新 `A44`。
⚠️ **尚未 Play 驗收。**

---

## 20. 工作包二 —— Contact 語意：Primary Root ＋ Secondary Surface Residual（2026-09-14）

> 研究與完整量測正本在 `docs/24` §16；本節記目前 runtime 契約。

`TraversalPlanBuilder` 不再對左右手各解一個 absolute root 後平均。新順序是：

1. `BothHands` 固定以 Left 為 primary（單手動作就用唯一那手）；root correction 只由 primary grip contact 決定。
2. contact→transfer 仍沿用同一份 correction，凍結契約不變。
3. primary root 決定後，secondary animated grip 投影／查詢 `TraversalProbe` 已提交的
   `TraversalLedgeFrame` 與 safety interval；成功才發布 wrist target 給 Hand IK。
4. secondary 落在 verified interval 外時，該手 contact unavailable：保留 authored pose、IK 權重為 0，
   **不壓縮雙手間距來偽造動畫做不到的姿勢**。

`LeftGripEdgeInRoot／RightGripEdgeInRoot`、wrist↔grip offset 與 `FingerContactClearance` 仍是
**animation/contact fitting**；它們不進 Probe、也不成為環境 geometry。Plan／Hand IK 仍不得呼叫 Physics。

production `Bake_Climb2m` 理想進場量測：舊平均解的左右 residual 都是 **9.64 mm**；
新解 primary 左手 **0 mm**、secondary 右手 **19.29 mm**，明確交給既有 Hand IK；
contact correction 漂移仍是 **0 mm**。窄 ledge 測試中 primary 落在安全區，secondary 因無 surface unavailable。

守衛：`HC4`（primary-only root）、`HC6`（secondary committed surface）、新版 `M6`（窄 ledge）、
`A47`（pure builder 且不得恢復 `SolveDual`）、既有 `A39／A46／A49`（query／movement／IK owner）。

驗證：Unity 6000.5.1f1 recompile 0 errors；focused `TraversalWarpPlanTests` **24／24**；
完整 EditMode **445 total／444 passed／0 failed／1 existing skipped**；PlayMode **41／41**。
