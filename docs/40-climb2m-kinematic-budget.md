# 40 — Climb2m 手臂運動學預算與 coverage 推導

| 欄位 | 內容 |
|---|---|
| 日期 | 2026-09-21，**2026-09-22 大幅修訂（§13）** |
| 🔴 先看這個 | **§7 的結論已被 §13 推翻。** §9／§10 的數字是 §13 之前的，最終數字與 coverage 以 **§13** 為準 |
| 性質 | **量測報告 ＋ 設計依據**。`docs/39` 的 Step 0–2 所需要的那個「可信的量測」 |
| 量測手法 | Editor 端 `AnimationClip.SampleAnimation` 逐格採樣（`applyRootMotion = true`），與 `TraversalContactDerivation.Measure` 同源；108 格 / 3.6 s / 30 fps |
| 落地 | `docs/39` §2 Q2 的選項 C（support-aware 垂直夾持）**被實測確認為足夠**；§7 撤回本文件初稿「垂直適應必須搬進 Contact→Transfer」的主張 |
| 閱讀順序 | §1 結論 → §2／§3（回答 bake 問題）→ §7／§8（實測修正與新發現）→ §9（最終實測） |

---

## 1. 結論（先讀這段）

1. **`anchorReleaseNormalizedTime = 0.2130` 與 `transferNormalizedTime = 0.2685` 不是同一件事，bake 沒有過時。**
   兩者由 `TraversalContactDerivation` 用**不同的物理事件**推導，差值正好是 authored 的 0.1 s fade。
2. **「Contact→Transfer 手臂 reach 超過 1.0」的根因是 prototype exception #5（IK window override），不是 warp compensation。**
   純動畫、零 warp 的情況下，把腕點釘在抓握點本身就會在 `n = 0.2593` 讓 ratio 到 **1.044**、`n = 0.2685` 到 **1.102**。
   執行期量到的 1.044 / 1.055 與這條曲線幾乎逐點吻合 ⇒ **compensation 不是主項**。
3. **Contact 的垂直修正有兩個成分，必須分開處理（§13）**：
   **grip-driven 的凍結到放手**（動了就是滑手）；**support-plane clamp 的必須在 Transfer 前鬆開**
   （那是「腳還踩著地」才成立的謊，撐過 Transfer 就會讓 root 爬過頭再沉下來，低牆上實測衝過 **22.8 cm**）。
4. **最終 coverage ＝ candidate 高度 1.65–2.05 m（§13）。**
   ⚠️ 下緣的 pre-IK 殘差 33.8 cm vs guard 0.35 ⇒ **餘裕只有 1.2 cm**，是已知的薄冰（§13）。
5. **另外找到一個與 traversal 無關、但會污染所有量測的東西**：
   Play Mode 中的 domain reload 會把 `_runtimeData` 這類非序列化欄位清成 null 且**不重跑 `Awake`**
   ⇒ `CharacterPipelineRunner.LateUpdate` 每幀 NRE。已用 `EditorUtility.RequestScriptReload()` **重現**。
   **這不是程式缺陷，是「Play 中改腳本」的 Editor 行為。**

---

## 2. 兩個時間戳的語意（回答「bake 是否過時」）

| 符號 | 值 | 推導來源 | 物理意義 |
|---|---|---|---|
| `leftHandContactNormalizedTime` | 0.0648（7/108） | `DetectPlant` 的 plant 起點 | 手速降到自身最大速度 15% 以下並連續維持 |
| `transferNormalizedTime` | 0.2685（29/108） | `Measure`：`max(plant.End)` | **動畫自己放開的時刻** |
| `leftHandIKWindow.release` | 0.2130（23/108） | `BuildHandIKWindow(KinematicRelease)` = `FixedGoalExtensionStart − fade` | **固定腕點開始比 authored 腕點要求更長肩—腕鏈的時刻，減去淡出時長** |
| `rootReachConstraint.anchorRelease` | 0.2130 | 同上 | 同上 |
| `FixedGoalExtensionStart`（未序列化） | 0.2407（26/108） | `FindFixedGoalExtensionStart` | 三格連續 `|fixedGoal − shoulder| > |authoredHand − shoulder|` |
| fade | 0.0278（3/108） | `HandIKFadeSeconds = 0.1 s` ÷ 3.6 s | |

`TraversalHandIKWindow.Evaluate` 的淡出由 `release` 起算、長度等於淡入 ⇒ 權重歸零於 `0.2130 + 0.0278 = 0.2407`，
**正好是過度伸展開始的那一格**。⇒ **authored window 是被推導出來的，不是猜的；它已經是對的。**

---

## 3. 量測：純動畫下的固定腕點 reach ratio

腕點固定在 f7 抓握點、零 warp、`armLen = 0.5617 m`（＝ `|upper−lower| + |lower−hand|`，與 runtime instrumentation 同式）。

| frame | n | 左手 fixed-goal ratio | 手離抓握點位移 |
|---|---|---|---|
| 7 | 0.0648 | 0.632 | 0 |
| 17 | 0.1574 | 0.867 | 6.8 cm（+z 6.3，指尖滾過邊緣） |
| 23 | **0.2130** | 0.801 | 4.5 cm |
| 26 | 0.2407 | 0.922 | 6.2 cm（開始 +y 上抬） |
| 28 | 0.2593 | **1.044** | 9.6 cm |
| 29 | **0.2685** | **1.102** | 11.5 cm（幾乎全是 +y） |

**0.2407 之後的位移幾乎純粹是 +y** ⇒ 動畫在推離、手在離開邊緣。
把 IK 釘到 Transfer 等於**與動畫的意圖對抗**，代價就是那條 ratio 曲線。

> 對照執行期實測（`WORKLOG` 2026-09-21）：0.267 → 1.044、0.270 → 1.055。
> 與上表逐點吻合 ⇒ **exception #5 單獨解釋整個現象。**

---

## 4. 量測：垂直缺口 D 的可負擔範圍

模型：牆比動畫低 `D` ⇒ 抓握點低 `D`；垂直修正以 smoothstep 分配於 Contact(7)→Transfer(29)；
IK 目標 = `Lerp(authoredHand, fixedGoal, w)`，`w` 取 **authored** window 包絡。

| 牆高 | D | IK 驅動的 ratio 峰值 | 峰值位置 | 判定 |
|---|---|---|---|---|
| 2.00 | 0.00 | 0.872 | f25 | ✅ |
| 1.80 | 0.20 | 0.872 | f25 | ✅ |
| 1.65 | 0.35 | **0.878** | f7（接觸瞬間） | ✅ |
| 1.60 | 0.40 | **0.937** | f7 | 🟡 marginal（＝動畫自身天花板 0.937） |
| 1.55 | 0.45 | 1.002 | f7 | ❌ 手臂打直 |
| 1.50 | 0.50 | 1.073 | f7 | ❌ |

**限制因子是接觸瞬間**（此時垂直修正尚未分配、抓握點已經低了 D），不是爬升途中——
爬升途中肩膀本來就在抓握點**下方**（f8–f23 的 `shoulderY − goalY` 為負），下沉修正反而讓幾何更接近動畫。

⇒ **運動學下限 ≈ 1.58 m。**
⚠️ 但**運動學不是最後的約束**——IK 的 pre-IK residual guard 比它更早撞上。最終 coverage 見 §10。

---

## 5. 對 `docs/39` 六個 exception 的最終裁決（依本文件的量測）

| # | Exception | 裁決 | 依據 |
|---|---|---|---|
| 1 | `Kind == Climb2m` 硬編 | ⛔ 刪 → authored `executionModel` | `docs/39` F9 |
| 2 | Entry→Contact 忽略 grip-derived 絕對 Y | ⛔ 刪 → 在 **landmark 層**把 contact root 夾在 entry 支撐面之上（`ClampOutsideWall` 的垂直同型） | §13 |
| 3 | Contact→Transfer 不維持固定 correction | 🔧 **部分保留（§13 修訂）**：水平凍結、**clamp 那一份在 Transfer 前鬆開** | §13：全部凍結 ⇒ 低牆 root 過衝 22.8 cm；全部提前補完 ⇒ 高牆手臂 ratio 0.976 |
| 4 | boundary actual-root rebase | ✅ 保留 | `docs/39` Q1 |
| 5 | Hand IK window override 至 Transfer | ⛔ **刪**（本文件的主要發現） | §2 ＋ §3 |
| 6 | residual guard 放寬至 0.5 m | ✅ **已不存在**——`X Bot.prefab` 實際值是 0.35 | 磁碟現況 |

---

## 6. 由此得到的執行模型

```
desired(t) = plan.TryEvaluate(t) + carriedError × (1 − smoothstep(u))
carriedError = actualSegmentStart − plan.TryEvaluate(segmentStart)
u = (t − segmentStart) / (segmentEnd − segmentStart)
```

- 段起點 `desired = actual`（無跳變）、段終點 `desired = committed landmark`（誤差歸零）。
- 段內形狀＝**production 的 piecewise 絕對軌跡**，因此 `RootReachConstraint` 與 knot 內插原樣保留
  （`docs/39` F2 的完整修法，不再需要 `TryEvaluateBasis` 重建一份）。
- compensation 的量級 = **執行誤差**（公分級），不再是**牆高適應**（公寸級）
  ⇒ reach budget 由構造保證，不需要第四個公分門檻。
- `smoothstep` 在兩端導數為 0 ⇒ compensation 速度在 boundary 連續（`docs/39` F3）。

---

## 7. ⛔ 本節的結論在 2026-09-22 被推翻，見 §13

> 🔴 **不要照用本節。** 下面這段用「blocked displacement ≈ 0」推論出「垂直適應留在 Transfer→Exit 沒問題」，
> 但那個代理量**看不到失效模式**：root 在半空中衝過落腳面再沉回來，**一路上沒有任何東西擋它**，
> 所以 blocked 當然是 0。使用者在真實 Play 一眼就看到了，而我的量測全綠。
> 保留本節是為了留下「代理量選錯」的紀錄本身。正確的規則在 **§13**。

§1.3 原本主張「Transfer 之後的向下修正會被平台擋住」。~~實測推翻了它。~~
（機制猜錯，但**方向是對的**：垂直確實不能整段凍結到 Transfer 之後。見 §13。）

裝上 contact support-plane clamp 之後，production 的 held-correction 形狀原樣保留
（Transfer knot 沿用 Contact 的 correction，垂直適應落在 Transfer→Exit），實測：

| 牆高 | warp blocked | traversal 期間 root divergence | completed |
|---|---|---|---|
| 2.05 m | 0.11 cm | ≤ 0.07 cm | ✅ |
| 1.80 m | 0.36 cm | ≤ 0.07 cm | ✅ |
| 1.70 m | 0.31 cm | ≤ 0.10 cm | ✅ |
| 1.68 m | 0.25 cm | ≤ 0.01 cm | ✅ |

⇒ **沒有任何一段被擋住，也沒有殘留 divergence。**
回報的 `LastTraversalMaxRootDivergence = 3.00 cm` 在四個高度上**逐位元相同**——
它是 exit 之後角色坐在落腳面上的 `CharacterController.skinWidth`（0.03），不是 traversal 誤差。
（與 `docs/39` F1 判定 2.99 cm 是貼地力的手法相同：**與高度無關的固定量只能來自與高度無關的來源。**）

**因此 Transfer knot 的垂直目標（`docs/39` Q4 的 TransferRootTarget）目前不需要動。**
⛔ 不要為了一個沒有被實測支持的機制增加 landmark。

---

## 8. 新發現：手部 IK 的一幀管線延遲（2026-09-21）

### 現象

Contact window 中點的 **post-IK** 手部殘差：

| timeScale | 殘差 |
|---|---|
| 1.00 | **6.77 cm** |
| 0.25 | **2.53 cm** |

殘差隨**每幀 root 位移**縮放，不隨 IK 解算品質變化；
在 root 幾乎不動的 contact 瞬間殘差是 **0.00 cm** ⇒ **解算器本身是收斂的。**

### 機制

1. Unity 的 Animator 評估（含 `OnAnimatorIK`）在 **`LateUpdate` 之前**。
2. `TraversalHandIKRig.OnAnimatorIK` 套用的是 `TraversalHandIKController` 在**上一幀**
   `LateUpdate`（管線順序 6.5）寫入的 goal。goal 是固定世界點，所以「舊」本身無害。
3. `CharacterPipelineRunner.LateUpdate` 順序 6 接著把 root 位移 Δ 套上去——**在 IK pass 之後**。
   整副骨架（含手）被平移 Δ。

⇒ 每一幀畫面上的手都在 `target + Δ(本幀)`。爬升最快時 root 約 2.9 m/s，
60 fps 下 Δ ≈ **4.8 cm 且幾乎全部朝上** ⇒ **身體往上爬、手浮在邊緣上方。**
這正是「抓空氣／手沒真的扣住」的來源，而且它是**管線順序的後果，不是調參問題**。

### 修法與效果

`TraversalHandIKController` 把交給 rig 的 goal 改為 `target − 上一幀實際 root 位移`
（`MotionDriver.LastTraversalExecutionResult.ActualDisplacement`，順序 6.5 讀得到順序 6 的結果）。
誤差因此從 Δ 降為 Δ 的**逐幀變化量**。

| | 中點 post-IK 殘差（timeScale 1.0） |
|---|---|
| 修法前 | 6.77 cm |
| 修法後 | **0.60 cm** |

⚠️ 用 **actual** 位移而非 requested：被碰撞擋掉的那一幀不得過度補償。
⚠️ **pre-IK 殘差與 `maximumResidualDistance` 閘門維持比對未補償的 committed target**——
那個閘門問的是「這個 committed 抓握點搆不搆得到」，屬於 plan 的問題，與幀延遲無關。
⚠️ Foot IK **沒有**套用同一個補償。它有相同的順序特性，但那是另一個問題，本輪刻意不開。

---

## 9. 實測結果（2026-09-21，真實 Play、真實 rig、runtime 生成的牆）

`executionModel = Segmented`、authored IK window、contact clamp、幀延遲補償全部到位後：

| 牆高 | candidate 高度 | contact ratio | mid ratio | contact post-IK | mid post-IK | contact clamp | pre-IK max |
|---|---|---|---|---|---|---|---|
| 2.05 | 2.020 | 0.669 | 0.866 | 0.69 cm | 0.60 cm | 0.00 cm | 8.24 cm |
| 1.80 | ~1.77 | 0.710 | 0.754 | 0.45 cm | 0.71 cm | 14.75 cm | 23.44 cm |
| 1.75 | ~1.72 | 0.737 | 0.739 | 0.49 cm | 0.19 cm | 19.75 cm | 27.01 cm |
| 1.70 | ~1.67 | 0.774 | 0.725 | 0.32 cm | 0.38 cm | 24.75 cm | 31.82 cm |
| 1.68 | ~1.65 | 0.802 | 0.728 | 0.26 cm | 0.01 cm | 26.75 cm | 33.96 cm |

- **IK 滿權重時的 arm reach ratio 全部 ≤ 0.87**，budget 0.95 未被觸及。
- **IK 滿權重時的手部殘差全部 ≤ 0.71 cm。**
- §4 的模型預測 D=0.20 時 contact ratio ≈ 0.714–0.736，**實測 0.710** ⇒ 模型與現實一致。

### 🔴 腳步聲：我第一次量錯了

初次量測回報「0 fired / 64 suppressed」，**那個判準是錯的**：
`FootstepSuppressReason` 的擊發標記是 **`Fired`**，`None` 是列舉的 0 值、代表別的意思。
用 `Reason == None` 當「擊發」等於什麼都沒數到。

改用正確判準之後（2.05 m 牆，clear trace 後一次完整攀爬）：

```
FIRED  right=False  kind=Normal  phase=Planted
       hasGround=True  clearance=0.021  vSpd=-0.01  hSpd=1.70  lift=0.000
traceCount=64  firedTotal=1
```

**一次擊發，而且它是對的**：`hasGround=True`、`phase=Planted`、水平速度 1.70 m/s
⇒ 角色**已經站上平台並開始走**，那一步真的踩在地上。

⇒ 判準的邊界因此是 **committed Exit**，不是「plan 還活著」：
Exit（0.833）之前角色離地、無地面觀測，任何腳步都是幻覺；
Exit 到 Recovery（0.870）之間 plan 仍活著，但人已經被貼地力壓在落腳面上 ⇒ **那一步該出聲**。
`TraversalClimbPlayModeTests` 守的就是前者。

### ⚠️ `MaxArmReachRatio` 是一個會誤報的代理量

上表 1.80／1.70／1.68 三列的 `armRatioMax` 分別回報 **1.047／1.180／1.233**，看起來像 budget 破表。
**它們全部來自 IK 權重已衰減到 0.00–0.03 的 fade 尾段。**
`ArmReachRatio` 量的是「肩膀到 **committed target** 的距離」，**不管那個 target 有沒有被套用**；
窗口關閉後手已經正確放開了，它卻還在量到那個被放開的點。
⇒ **判準必須看滿權重的取樣點**，不是這個 max。

---

## 10. coverage 推導（⚠️ 下緣數字已由 §13 改回 1.65）

> 🔴 **本節的 `1.72` 已作廢。** 推導過程仍然有效（三個下限的分層），但結論被 §13 改回 **1.65**：
> 使用者回報那個高度本來就在用，而 root 過衝修好之後它跑得乾淨。薄冰的部分照實記在 §13。

### 原推導：最終 coverage：三個不同的下限，取最保守的那一個

Climb2m 的下限有**三個來源**，它們不是同一個數字，混用會得到錯的結論。

| 下限 | 值（candidate 高度） | 由什麼決定 | 量測依據 |
|---|---|---|---|
| **運動學下限** | ≈ **1.58 m** | 接觸瞬間手臂打直（ratio → 1.0） | §4 的採樣模型 |
| **IK guard 下限** | ≈ **1.62 m** | contact pre-IK 殘差撞到 `maximumResidualDistance = 0.35` | §9 表格外插（1.65 → 33.96 cm） |
| **authored coverage** | 🟢 **1.72 m** | 上面那個 guard 要有**可用的餘裕** | 1.75 m 牆實測 **27.01 cm ／ 35 cm ＝ 77%** |

### 為什麼不是把 coverage 壓到 1.62

因為 guard 的失效模式**比它想防的東西更糟**：殘差一旦越線，IK 整個掉到 0，
手會在 contact 當幀直接彈開——那是**斷裂**，不是降級。
把 supported 範圍的邊界壓在一個斷崖前 1 cm 的位置，等於把「偶發錯誤」寫進規格。

⇒ 2026-09-21 把 `coverageMinHeight` 從 **1.65 → 1.72**。
代價是 7 cm 的牆高；換到的是 **23% 的 guard 餘裕**（1 cm → 8 cm）。

### ⛔ 沒有做的事，以及為什麼

- **沒有放寬 `maximumResidualDistance`。** 那正是 `docs/39` F8 指名的「用放寬門檻掩蓋問題」。
- **沒有把 guard 改成 reach-ratio 計價**（`docs/39` Q6 的建議）。方向仍然是對的——
  公尺不是 reach 的自然單位——但在目前的 coverage 內 guard **根本不會觸發**，
  改它換不到任何行為改善，只換到風險。**留給真正需要它的那一天。**
- **沒有為了支援 1.3–1.5 m 增加任何機制。** 那需要的是另一支動畫，不是更多程式。

### 最終 coverage 表

| candidate 高度 | 狀態 | 說明 |
|---|---|---|
| **1.72 – 2.05 m** | ✅ **Validated** | 2.05／1.80／1.75 實測完整通過；PlayMode regression 覆蓋 2.05 與 1.80 |
| 1.62 – 1.72 m | 🟡 **Marginal，已關閉** | 運動學上可行，但 IK guard 餘裕不足 ⇒ 刻意不開放 |
| ≤ 1.62 m ／ > 2.05 m | ⛔ **Unsupported** | `TraversalSelectionPolicy` 以 `OutsideAnimationCoverage` 明確否決，退回一般 Jump |
| 1.20 – 1.72 m | ⚠️ **能力空缺** | Climb1m 的 coverage 是 [1.0, 1.2]；這一段沒有任何 traversal 動畫。**這是資產限制，不是程式缺陷** |

> 實測驗證：1.65 m 牆（candH 1.620）被 `OutsideAnimationCoverage` 擋下、退回 Jump，
> 角色停在牆前，**沒有半套動畫、沒有卡住**。否決是可觀測的（`docs/30` I8）。

---

## 11. 怎麼重跑這份量測（SOP，不是工具）

⚠️ 依 CLAUDE.md「Editor Tool vs Documented Process」：這件事一年跑不了幾次，
而且完全走既有 API ⇒ **不建工具，留流程。**

自動化的部分已經是 `Assets/_Project/Tests/PlayMode/TraversalClimbPlayModeTests.cs`
（`unity cmd run_tests --mode PlayMode --async_tests true`）。
需要**任意牆高**的一次性量測時，在 live Editor 的 Play Mode 裡：

1. `unity cmd editor_play`，等 `Application.isPlaying == true`。
2. `eval_file`：先 `DestroyImmediate` 舊的 `PROBE_WALL`，**再**向下射線取得地面高度
   （⚠️ 順序反了會射到上一面牆，我踩過），生成 Cube 當牆，把角色傳送到牆前 1.6 m。
3. `InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W))`——
   ⛔ **不要接著呼叫 `InputSystem.Update()`**：那會在遊戲幀看到之前就把
   `WasPressedThisFrame` 的邊沿吃掉，跳躍永遠不會觸發（我在這裡卡了三輪）。
4. 輪詢角色與牆面的距離，落在 0.18–0.55 m 時送出 `KeyboardState(Key.W, Key.Space)`。
   ⚠️ 起攀需要 `Intent.JumpRequested`，**光是走進牆裡不會觸發**。
5. 攀爬結束後讀 `MotionDriver` / `TraversalHandIKController` 的 max 累加器與四個取樣點。

⚠️ **每次只跑一個腳本。** 背景殘留的輪詢腳本會和新腳本搶同一個角色，
量到的數字會張冠李戴（我踩過一次，先以為是傳送失效）。

---

## 12. 附帶證實：Play 中的 domain reload ≠ 程式缺陷

`WORKLOG` 2026-09-21 記的「787（後續 1987）個 `CharacterPipelineRunner.LateUpdate` NRE」**已結案**。

**先用排除法定位**：那一行是 `_runtimeData.ResetTransientState()`，
而 `IntentData` 是 **struct**、`Reset()` 只指派值型別、`JustLanded`／`JustLeftGround` 是 bool
⇒ 這個方法**不可能**自己丟 NRE ⇒ **只剩 `_runtimeData == null` 一種可能。**

**再用實驗證實**：Play Mode 中執行 `UnityEditor.EditorUtility.RequestScriptReload()`，
`PROBE2` 立刻逐幀吐出

```
[PROBE2] obj=Y Bot runtimeData=NULL :: System.NullReferenceException
[PROBE2] obj=X Bot runtimeData=NULL :: System.NullReferenceException
```

兩個角色、每幀一次，累計數量與使用者回報的量級一致。

**機制**：domain reload 會把所有**非序列化**欄位清成預設值，而且**不會重跑 `Awake`**。
`_runtimeData` 是 private、非序列化、只在 `Awake` 指派的純 C# 物件 ⇒ 變 null 且永遠補不回來。
Unity 的「Script Changes While Playing」預設是 **Recompile And Continue Playing**，
所以**在 Play 中存檔改腳本就會觸發**。

| | |
|---|---|
| **是不是 gameplay 缺陷** | ❌ 不是。乾淨啟動的 Play **0 次**；與 traversal 完全無關（時間軸上也對不上） |
| **要不要加防禦程式** | ❌ **不要。** reload 之後**整個 session 的非序列化狀態都沒了**，`_runtimeData` 只是第一個被解參考的。把這一行變成 null-safe 只會**隱藏一個已經死掉的 session** |
| **怎麼避免** | Unity Preferences → General → **Script Changes While Playing → Recompile After Finished Playing**（使用者的 Editor 偏好，不是 repo 設定，所以我沒有替你改） |

📌 `PROBE2` 已依 CLAUDE.md 的 Spike／Probe 紀律**刪除**——問題回答完，探針就該消失。

---

## 相關文件

- `docs/39-traversal-segment-warp-review.md` —— 本文件是它 Step 0–2 的量測落地；§2 Q2 的選項 C 已被實測確認
- `docs/30-traversal-architecture-guide.md` §C／§F —— 本輪改了 I6／I7／新增 I11／I12
- `docs/ADR/008-traversal-integration.md` 🟡 Trial —— 本輪加了 Segmented execution 的修訂紀錄
- `Assets/_Project/Tests/PlayMode/TraversalClimbPlayModeTests.cs` —— §9 的自動化版本

---

## 13. 🔴 2026-09-22 回歸：root 在低牆上會「爬過頭再沉下來」

使用者實際 Play 後回報「原本能爬的最低高度爬不了了，且效果更差」，並附上兩張畫面：
角色掛在邊緣上姿勢扭曲、以及整個人對折趴在平台上。**兩件事都是真的，而且都是我造成的。**

### 現象（1.75 m 牆，ledge top = 1.750）

| n | 0.083 | 0.174 | 0.263 | **0.350** | 0.439 | 0.525 | 0.702 |
|---|---|---|---|---|---|---|---|
| rootY | 0.039 | 0.727 | 1.660 | **1.978** | 1.934 | 1.874 | 1.780 |

**root 衝過落腳面 22.8 cm，再花約 1.3 s 沉回去。** 畫面上就是「爬上去之後浮在半空、身體垂在邊緣外側再慢慢落下」。

### 為什麼我的量測全綠

- `warpBlocked` ≤ 0.36 cm —— **往下沉是朝著平台，沒有任何東西擋它**，所以擋住量當然是 0。
- hand residual 乾淨 —— 過衝發生在 n > 0.27，手早就放開了。
- `LastTraversalMaxRootDivergence` —— 它量的是 root 與 **plan** 的距離，而 plan 本身就是要它衝上去的。

⇒ **三個指標全部看不到這個失效模式。** 這與 `docs/39` F1 是同一類錯誤，只是這次犯錯的人是我。
📌 **教訓：「沒有指標變紅」不是「沒有問題」。第三人稱看一眼的成本遠低於這一輪的代價。**

### 根因

`TraversalPlanBuilder` 的 Contact 垂直修正由**兩個語意完全不同的部分**組成：

| 成分 | 來源 | 抓握期間該怎麼辦 |
|---|---|---|
| ① **grip-driven** | 把 root 擺到「動畫的手剛好落在真實邊緣」的高度 | **必須凍結**——手是硬約束，root 一動就是滑手 |
| ② **support-plane clamp** | 接觸瞬間角色站在地上，不能把 root 解到地板下，所以往上夾了一段 | **必須鬆開**——這是對 root 高度說的謊，只在腳還踩著地時成立 |

2026-09-21 我加了 clamp（②，正確且必要），卻讓**整份**修正凍結到 Transfer 之後。
於是 ② 一路撐到越過邊緣才開始還，而**動畫自己的垂直位移在 n≈0.35 就結束了**，
Transfer→Exit（0.269→0.833）這個窗口來不及把它還完 ⇒ 過衝。
牆越矮、clamp 越大、過衝越明顯（2.05 m 牆 clamp = 0，所以我在那裡完全量不到）。

### 修法

**Transfer 的垂直 ＝ Contact 的垂直 − clamp 量。** 水平維持凍結。

```csharp
transferCorrection.y -= contactSupportPlaneClampDistance;
```

剩下的（grip 幾何與落腳面之間的差，實測 6–8 cm）留給 Transfer→Exit——
那段手已經放開，而且 2.05 m 牆本來就是這樣跑、從來沒有過衝。

### ⛔ 先試過但否決的版本

`transferCorrection.y = exitKnot.PositionCorrection.y`（垂直全部在 Transfer 前補完）。
低牆確實不再過衝，但 **2.05 m 牆**抓握期間多沉 6.1 cm，
滿權重手臂 ratio 從 0.866 升到 **0.976** ⇒ **拿高牆的手換低牆的 root**。不可接受。
clamp-unwind 版在 2.05 m 上 Transfer knot 與 Contact knot **逐位元相同**（clamp = 0），是真正的 no-op。

### 修好之後（teleport 進場 0.46 m，三個高度）

| 牆高 | ledge top | root 峰值 | 超出量 | 滿權重 ratio（contact／mid） | 滿權重手部殘差 | completed |
|---|---|---|---|---|---|---|
| 2.05 | 2.050 | 2.093 | **4.3 cm** | 0.720 ／ 0.946 | 0.00 ／ 0.09 cm | ✅ |
| 1.75 | 1.750 | 1.794 | **4.4 cm** | 0.773 ／ 0.822 | 0.00 ／ 0.13 cm | ✅ |
| 1.68 | 1.680 | 1.718 | **3.8 cm** | 0.832 ／ 0.802 | 0.00 ／ 0.05 cm | ✅ |

**超出量不再隨牆高變化**（22.8 cm → 一律 ~4 cm，就是站立時的 skin width ＋ 沉降）。
**異常不再隨參數縮放，才是修對了的特徵。**

### coverage 退回 1.65

2026-09-21 我把 `coverageMinHeight` 從 1.65 提到 1.72，理由是 pre-IK 殘差距 IK guard 只剩 1 cm。
**使用者回報那個高度本來就在用**，而修好過衝之後它跑得乾淨（見上表 1.68 那列）⇒ **退回 1.65。**

⚠️ **保留的已知限制**：在 coverage 下緣（candidate 高度 1.65），contact 的 pre-IK 殘差是 **33.8 cm**，
而 `maximumResidualDistance = 0.35` ⇒ **餘裕只有 1.2 cm**。
guard 一旦觸發，IK 會整個掉到 0（**斷裂，不是降級**）。實測沒有觸發，但這是一條薄冰。
⛔ **不要用「放寬 guard」去換餘裕**——那是 `docs/39` F8 指名的錯誤做法。
要處理的話，正解是 `docs/39` Q6：把 guard 改成 reach-ratio 計價。

### 順帶修正的判準

`TraversalClimbPlayModeTests` 原本斷言「Exit 之前不得有腳步聲」。**那個界線是錯的。**
root 過衝修好之後角色在 **n≈0.51 就真的站上平台**，右腳在 n≈0.68 落地
（`hasGround=True / phase=Planted / 水平速度 1.12 m/s`）——**那是正確的腳步**，
卻會被 Exit(0.833) 判成缺陷。改成**「不得在角色離地的那一幀出聲」**：
缺陷的本體是「腳下沒有真地面卻出聲」，與 normalized time 無關。
📌 綁在時間上的判準會隨著 root 軌跡變好而誤報——那不是判準，是噪音產生器。

### 驗證

EditMode 620（619 passed／0 failed／1 skipped）、PlayMode **67／67**。
