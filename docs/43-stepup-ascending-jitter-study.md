# 43 — 上樓抖動研究輪（2026-09-23，研究性質，⛔ 純量測、未提出修法、未裁決）

> **這份文件是什麼**：使用者回報「上下樓皆有抖動」。`docs/42` 的 step-down assist 三輪迭代只
> 處理**下樓**（重力積分導致的逐階彈道落下），Gate③（本幀垂直分量已 ≤ 0）刻意排除了上樓，
> 使用者明確要求**另開一輪、只研究、先不修**：量測 root Y／actual Move delta／`stepOffset`
> 觸發／pelvis offset／foot IK weights，先確認上樓抖動到底是「root 一幀位置跳變」、「Foot IK／
> 骨盆補償」、還是「兩者疊加」，再決定修法方向。
>
> 對應程式：`Assets/Scripts/Presentation/Motion/MotionDriver.cs`
> （`ExecuteBaseMovement` 新增的 `LogStepUpDiagnosticsTrace`，純觀測，`#if UNITY_EDITOR ||
> DEVELOPMENT_BUILD` 內，不寫任何欄位、不影響 `finalMovement`）。
> 銜接文件：`docs/42`（下樓 step-down assist，同一輪對照組）、`docs/29`（Foot IK 架構，`PelvisOffsetY`
> 上下樓骨盆抽動已是既有已知限制）。
>
> 🔄 **2026-09-23 第一輪 Play 量測已完成（§2）：H3 成立（root 雜訊＋Foot IK 放大，兩層疊加，
> 有明確上下游關係），且發現上樓跟下樓是不同失效模式——`IsGrounded` 全程不閃**。
> 🔄 **續：已與 Codex 討論修法方向（§3）**——建議優先方向是「分層 ramp collider（移動用 ramp，
> Foot IK 查詢用原本階梯幾何）＋ Pelvis 支撐語意重構」並行，Presentation Reconciliation
> （視覺/物理 root 解耦）降為保險層。
> 🔄 **續：單一樓梯 ramp collider prototype 已完成並驗證（§4）——H1 成立且已解決**：
> `stepOffsetDelta` 從 6.7× 幀間雜訊收斂到 2.4× 平滑曲線，1963 幀 `IsGrounded` 全程未閃一次。
> 使用者事先設的分岔條件（root 跳變消失但 pelvis 仍週期性歸零才動 Pelvis）**未觸發**，
> 本輪不動 Pelvis 邏輯。⛔ **場景未存檔，只影響單一樓梯，未推廣、未轉正式架構。**

---

## 0. 假說（尚未驗證，等量測資料）

`characterController.stepOffset`（X Bot 實測 0.3m）處理上樓——`CharacterController.Move()`
內部碰撞解算在**一幀內**把角色頂上去，過程完全不經過 `finalMovement`（我們餵多少 Y 分量給
`Move()` 不影響這個內部機制），也沒有任何位置平滑。三個可能的抖動來源，本輪要量出來是哪一個：

| 假說 | 預期訊號 |
|---|---|
| **H1：root 位置本身一幀跳變** | `actualDY − requestedDY`（即 `stepOffsetDelta`）在上樓瞬間出現明顯正值（接近樓梯級高 0.195m 量級），且該幀前後 `pos.y` 有階梯狀跳躍 |
| **H2：Foot IK／骨盆補償跟不上** | 即使 `stepOffsetDelta` 不大（root 位移本身平滑），`pelvisY`／`leftFootW`／`rightFootW` 仍有劇烈擺動——代表問題不在 root，在 IK 補償的權重/位置平滑速率 |
| **H3：兩者疊加** | `stepOffsetDelta` 與 `pelvisY` 同時劇烈變動，且時間點相關（root 跳變後幾幀內 pelvis 才追上） |

---

## 1. 量測工具（純觀測，不改變行為）

`MotionDriver` 新增 `enableStepUpDiagnosticsPrototype`（bool，預設 `false`，與 `docs/42` 的
`enableStepDownAssistPrototype` 完全獨立、不共用任何邏輯）。開啟後每幀印一行：

```
[StepUpDiag] frame=<n> pos=(x,y,z) isGrounded=<bool> hIntent=<bool>
             requestedDY=<本幀餵給 Move() 的 Y 分量，grounded 時通常≈reboundForce×dt，量級很小>
             actualDY=<Move() 真正造成的位移>
             stepOffsetDelta=<actualDY − requestedDY，正值代表 Move() 內部把角色往上頂了多少>
             pelvisY=<FootIKTargetData.PelvisOffsetY>
             leftFootW=<LeftFootPositionWeight>  rightFootW=<RightFootPositionWeight>
             postGrounded=<characterController.isGrounded>
```

量測方法沿用 `docs/42`：`unity cmd console --tail 3000` 撈 Console 緩衝、grep `[StepUpDiag]`、
用 `frame`／`pos.y` 找上樓區段（`pos.y` 連續遞增的片段），逐幀比對 `stepOffsetDelta` 與
`pelvisY`／`leftFootW`／`rightFootW` 的時間相關性。

---

## 2. 量測結果（2026-09-23，第一輪 Play 量測，1979 幀）

### 2.1 上樓期間 `IsGrounded` 完全不閃——跟下樓是不同的失效模式

取一段純上樓連續片段（frame 771→796，`pos.y` 2.264→3.029，單調遞增，約 4 階）：
**`postGrounded` 全程 `True`，一次都沒有 False。** 整個 1979 幀的 trace 裡確實有 199 幀
`postGrounded=False`，但 run-length 分布（2–6 幀）跟 `docs/42` 下樓修前的量級一樣——這是同一次
Play session 裡使用者也有走**下樓**的片段混進來的（測試時 `enableStepDownAssistPrototype`
已關閉，所以那些下樓片段是「未裝 assist」的原始行為，不屬於本輪要看的上樓問題）。

⇒ **H1／H2／H3 的假說前提需要修正一項**：上樓的抖動**不是**靠 `IsGrounded` 閃爍驅動的
（跟下樓的根因完全不同機制），Foot IK 的整體閘門（`ikAllowed = data.IsGrounded`）全程開著。

### 2.2 H1 部分成立：root 位置不是單一大跳變，而是持續、量級不均的雜訊

同一段（771→796）的 `stepOffsetDelta`（`actualDY − requestedDY`，即 `characterController.
stepOffset` 內部這一幀實際把角色頂了多少）：

```
0.049, 0.039, 0.039, 0.064, 0.095, 0.071, 0.055, 0.041, 0.029, 0.032,
0.121, 0.075, 0.074, 0.034, 0.027, 0.121, 0.086, 0.051, 0.028, 0.022,
0.018, 0.045, 0.067, 0.056, 0.040, 0.038   （單位：公尺／幀）
```

**不是單一大跳變**（沒有一幀單獨扛下一整階 0.195m），而是**每一幀都在修正、量級忽大忽小**
（0.018～0.121，約 6.7 倍的幀間變化量）——角色是沿著階梯鼻端的膠囊圓弧邊爬邊修，不是乾脆的
一次到位。這種「量級不均勻的連續修正」本身就會讀起來像抖動，即使 `IsGrounded` 從未離開過 true。

### 2.3 H2 同樣成立：Foot IK 權重／骨盆在延伸窗口內釘死在極值

同一份 1979 幀資料裡（含上樓與其他片段）：
- `pelvisY` 精確等於 `0.00000` 出現 **83 次**（≈4.2%）
- `leftFootW` 精確等於 `1.000` 出現 **117 次**；`rightFootW` 精確等於 `0.000` 出現 **99 次**

且這些極值不是孤立的單幀雜訊——771→796 那段裡，`rightFootW=0.000` 連續了 **10 幀以上**
（frame 775–788，對應 `pelvisY` 也整段釘在 `0.00000`）。這符合 `FootIKController.
ComputePelvisOffset` 的既有邏輯：`pelvisTarget` 只有在**兩隻腳都有地面命中**時才計算，
否則直接是 0；快速連續爬階時，擺盪腳（swing foot）在空中的時間內量不到地面命中，
骨盆補償整段被鎖在 0，等擺盪腳重新落地才開始追趕（`MoveTowards` 在 789 幀之後才重新啟動）。

### 2.4 結論：H3（兩者疊加），但因果方向明確——root 雜訊是上游

- **不是**單純的「grounded 閃爍」（跟下樓不同機制，§2.1）。
- **root 位置本身**確實不平滑（§2.2）：`stepOffset` 的內部修正逐幀量級不均，这是最基礎的雜訊來源。
- **Foot IK／骨盆補償**確實有自己獨立可觀察的動態（§2.3）：雙腳同時命中的門檻條件，配合擺盪腳
  在快速爬階時常態性量不到地面，產生「壓平幾幀、再一次追趕」的節奏——而這個門檻計算本身是
  **root-relative**（`ComputeFootWeight` 用「動畫腳部 goal 相對 Root 平面的高度」），root 越不
  平滑，這一層算出來的權重也跟著更不穩定。
- ⇒ **兩層疊加，且有明確的上下游關係：root 的逐幀修正雜訊是原始訊號，Foot IK 的權重／骨盆補償
  是對這個雜訊訊號的下游放大**，不是兩個互相獨立的病灶。

---

## 3. 修法方向討論（與 Codex 對照，2026-09-23，⛔ 仍是提案，未實作未裁決）

使用者要求先跟 Codex 討論修法方向再回報。Codex 讀過 `MotionDriver.ExecuteBaseMovement` 與
`FootIKController.ComputePelvisOffset`／`ResolveFoot` 後給出獨立判斷，與我自己查到的候選方向
交叉比對如下。

### 3.1 四個候選方向

| # | 方向 | Codex 判斷 | 排序 |
|---|---|---|---|
| **B（分層版）** | **CharacterController 只碰撞平滑 ramp；樓梯視覺階梯的碰撞體改到獨立 layer，Player 忽略、但 Foot IK 的 `GroundLayers` 仍查得到** | 從源頭消除 `stepOffset` 雜訊，同時保留逐階 Foot IK 精確度——**最推薦** | 1 |
| **Pelvis 雙腳命中門檻重構** | 真正的斷點不在 `ComputePelvisOffset`（本身連續），在呼叫端的三元運算式 `ikAllowed && left.HasHit && right.HasHit ? ComputePelvisOffset(...) : 0f`——混了「查到候選面」「是不是支撐腳」「套用多少 IK」三件事 | 應與方向一起做，但**單獨做治不了 root 的雜訊**，不能單獨結案 | 與 1 並行 |
| **A（Presentation Reconciliation）** | 物理 root 保持唯一權威；量出 `solverCorrectionY = actualDY − requestedDY`，грounded／有效 locomotion／正向修正時把這筆量存進一個**有界、連續歸零**的 Presentation-only Y offset，而不是直接對輸出做 `SmoothDamp`（那才是真的在掩蓋斷點）——**全部位移最終都會被完整呈現，只是重新安排呈現的時間**，不是丟掉或降權重 | 次選／保險層，**不建議單獨當主修法**：無法要求關卡提供 ramp 時才用 | 2（保險層，非主修） |
| B（同 layer） | CharacterController 與 Foot IK 共用同一個 ramp collider | Foot IK 會把腳對齊到隱形斜坡、跟視覺踏面不符 | ⛔ 不建議 |
| 自製 step-up solver（停用內建 `stepOffset`，仿照下樓的 Query→Apply） | 角色要先跨過垂直立面，膠囊得先升高或沿虛擬坡面上升——本質上等於「runtime 生成 ramp」，且要另外防跟內建 depenetration／grounding 互相干擾 | 通用性最高但複雜度／風險也最高，**除非目標是「任意樓梯零額外 authoring」，否則不列優先** | 4 |
| 調 `skinWidth`／`minMoveDistance`／切多段 Move | 可能改變雜訊量級，但不保證連續性 | 只適合當敏感度實驗，不是正式設計 | ⛔ 不建議 |

### 3.2 為什麼「分層 ramp」不算掩蓋，是分工

Codex 的論點：這不是平滑遮掩，是把「locomotion support surface」（角色碰撞需要的可行走面）與
「presentation contact surface」（腳部呈現需要的視覺接觸面）**明確分工**——兩者本來就不一定該是
同一份幾何。與 `docs/29` §I 那次「不准 fade 掩蓋斷點」的裁決不衝突：那次反對的是「保留一個
錯誤的目標、只降低它的影響」；分層 ramp 是從根本上讓 `stepOffset` 不再需要處理離散台階，
上游訊號本身就不再有雜訊，不是在下游遮住一個仍然存在的斷點。

代價：**關卡 authoring 契約變重**——每道樓梯需要「一個 ramp collider 供移動用」＋「保留原本階梯
幾何供 Foot IK query 用」，且需要新增一個「Player 不碰撞、但 Foot IK 查得到」的專用 layer
（`docs/42` §6.5.2 已查證本專案目前只用了 8 個 layer 中的 7 個，還有空位；但目前 Physics Layer
Collision Matrix 是全開的，需要顯式停用 Player↔此新 layer 的碰撞）。

### 3.3 Pelvis 門檻的正確修法方向（非「任一腳命中就算」）

Codex 特別提醒：不能直接改成 `left.HasHit || right.HasHit`——`HasHit` 不代表「這隻腳是支撐腳」，
擺盪腳前方新踏面的候選命中也會是 `HasHit=true`。理想的資料模型是「有效支撐集合」：
- 兩隻都是有效支撐 ⇒ 取兩者較低的 surface（現行邏輯，不變）；
- 只有一隻是有效支撐 ⇒ 由那隻腳單獨決定 pelvis；
- 沒有支撐腳／進入 airborne ⇒ 回到 neutral（這才是合理的模式切換，不是「掉到 0 忍著」）。

但「有效支撐」需要 foot-contact／foot-phase 語意，本輪未定案怎麼組（pose height／IK eligibility／
命中狀態的組合公式留待下一輪設計）。

### 3.4 若走 Presentation Reconciliation（方向 A）的實作邊界（若未來需要保險層時參考）

Codex 給的具體分工，記錄供未來參考，**本輪不實作**：
- 物理 root：碰撞、gameplay 座標、狀態、移動權威，唯一不變。
- Presentation root：Animator／模型／相機跟隨 anchor／視覺 Y offset，才是被 reconcile 的對象。
- X/Z 與 yaw 精確跟隨物理 root，只有 Y 被時間平滑。
- Foot IK 的 ray 仍查 world-space 真實踏面（不受影響）；但 pose-relative weight 與 pelvis 用的
  `rootY` 必須改用 **presentation root Y**，不能繼續混用 physics root Y。
- 視覺 offset 必須在 `OnAnimatorIK` 解算**之前**生效，不能等到 LateUpdate 才搬模型（否則 IK 解完
  又被整副骨架的位移蓋掉，腳會再次偏離 world-space target）。
- 必須有**最大視覺落後量上限**，以及跳躍／傳送／traversal／失地／移動平台切換時的明確 reset
  policy——否則高速連續爬多階時 offset 會累積，造成模型落後 collider。

### 3.5 目前合適的方向（本輪結論，⛔ 仍待使用者裁決是否採用）

**依 Codex 判斷＋我方複核：優先方向是「分層 ramp（B 的分層版）＋ Pelvis 支撐語意重構」兩者並行**，
理由：
1. 直接消除上游雜訊來源（root 本身），不是在下游加平滑掩蓋一個仍在跳動的訊號。
2. Pelvis 門檻重構本來就該做——現行的「雙腳都命中才算」邏輯本身混淆了三件事，不管有沒有
   ramp 都是一個獨立的資料構造缺陷。
3. 兩者都符合 `docs/29` §I 既有裁決的精神（修斷點本身，不加 fade）。
4. Presentation Reconciliation（方向 A）留作**保險層**——只有在某些樓梯無法／不便追加 ramp
   collider時才補這一層，不當主修法先做（複雜度與風險都更高：有界回收速率、reset policy、
   presentation/physics root 分離會牽動 Foot IK 與相機兩處消費者）。

**下一步待使用者裁決**：
1. 是否核准「分層 ramp」作為樓梯的新 authoring 慣例（需要新增一個專用 layer、調整 Layer
   Collision Matrix、為現有 4 個樓梯物件各加一個 ramp collider——資產改動，走 Unity Editor API，
   屬於 CLAUDE.md「Unity Asset Authoring」§小範圍可逆的範疇，但仍需先問）。
2. Pelvis 支撐語意的具體公式（pose height／IK eligibility／命中狀態怎麼組合）需要一輪設計，
   不是本文件的範圍。
3. 若核准，建議先做一道樓梯當 prototype 驗證，比照 `docs/42` 的量測方法論（Console log
   逐幀比對），再決定要不要推廣到其餘樓梯。

---

## 4. 單一樓梯 Ramp Collider Prototype（2026-09-23，使用者已核准，⛔ 僅此一道樓梯、未升格規範）

### 4.1 使用者裁決的範圍

核准對 `Stairs_650_400_300_Prefab`（場景實例）做 ramp collider prototype，**明確限縮**：
- 第一輪**只驗證幾何分層**：CharacterController 僅碰 ramp、Foot IK 僅查真實階梯、**Pelvis 邏輯
  完全不動**。
- 沿用既有逐幀量測，至少比較 root Y delta／`IsGrounded`／左右腳 `HasHit`／左右腳 IK 權重／
  `PelvisOffset`。
- 若 root Y 逐階跳變明顯消失、但 pelvis 仍週期性歸零，才進下一輪設計「有效支撐集合」，
  **不同一輪一起重構**。
- Ramp layer 與 Foot IK 用的 layer 必須明確互斥，避免 IK 同時命中 ramp 與真實階梯。

### 4.2 實作內容（皆可逆，僅影響這一道樓梯）

| 改動 | 內容 |
|---|---|
| 新增 layer | `RampProto`（index 3）、`TreadIKOnly`（index 8），寫入 `ProjectSettings/TagManager.asset` |
| 移動既有階梯 | `Stairs_650_400_300_Prefab`（**場景實例**，非 prefab 資產）的 layer 從 `Default` 改到 `TreadIKOnly`——只影響這一個實例 |
| 新建 ramp | `Stairs_650_400_300_RampProto`（純 `BoxCollider`，無視覺 mesh），沿實測輪廓（raycast 掃出真實級高 0.195m、寬度 2.8m）算出的斜率 **31.4°**（遠低於 `slopeLimit` 45°），貼齊量到的底部 (11.7, 0.15)／頂部 (5.3, 4.05) 座標，layer=`RampProto` |
| Foot IK 遮罩 | X Bot `FootIKController.settings.GroundLayers` 的 `m_Bits` 從 `4294967295`（Everything）改成 `4294967287`（排除 `RampProto` 那一位元）——IK 永遠查不到 ramp |
| 新增元件 | `RampPrototypeLayerSetup.cs`（掛在 X Bot）：`Awake()` 呼叫 `Physics.IgnoreLayerCollision(Player, TreadIKOnly, true)`，讓角色物理上只碰 ramp。**Layer Collision Matrix 完全不影響 raycast**，只影響 CharacterController 的實際碰撞，所以這是唯一需要的隔離手段 |
| Log 擴充 | `LogStepUpDiagnosticsTrace` 新增 `leftHasGround`／`rightHasGround`（原本只有平滑後的權重，沒有原始命中訊號） |

Pelvis 相關程式碼**逐字未動**。

### 4.3 量測結果（1963 幀，與 §2 的量測方法一致，同一道樓梯、同一使用者手感）

**headline：整段 1963 幀 `postGrounded` 全部是 `True`，一次都沒有 `False`。**

取一段跟 §2.2 直接可比對的真實爬階連續片段（frame 2977→3012，`pos.y` 0.853→1.336，同樣約
4 階量級）：

| 指標 | §2.2（真實階梯，修前） | §4.3（ramp，本輪） |
|---|---|---|
| `stepOffsetDelta` 範圍 | 0.018–0.121m（**6.7×** 幀間變化） | 0.026–0.062m（**2.4×** 幀間變化） |
| `stepOffsetDelta` 波形 | 數值忽大忽小、無規律跳動 | **平滑曲線**，逐幀變化量本身也連續（不會像修前那樣鄰幀差 3–4 倍） |
| `leftHasGround`／`rightHasGround` 掉出時長 | 未逐幀記錄（本輪才加這個欄位） | 最長連續 3 幀（frame 2985–2987 左腳、3009–3010 右腳）——符合正常擺盪相位，不是「等一整個踏面週期」 |
| `pelvisY` 型態 | 連續 10+ 幀釘在 `0.00000`，之後一次追趕（見 §2.3） | 同一段只有**小幅短暫**偏移（-0.006 ~ -0.054），數幀內自行收斂回 0，沒有觀察到 §2.3 那種「壓平一大段再追趕」的節奏 |
| 全程 1963 幀 `postGrounded=False` 次數 | 上一輪整體 trace 199 次（含混入的其他片段） | **0 次** |

### 4.4 結論：幾何分層假說成立

- **H1（root 位置雜訊）在這道樓梯上已經消失**：`stepOffsetDelta` 從「大幅度、無規律的逐階修正」
  變成「小幅度、平滑的連續曲線」——因為 `characterController` 現在只碰一個連續斜坡，
  `stepOffset` 解算不再需要處理任何離散台階。
- **H2（Foot IK／骨盆放大）的最壞情形（延伸鎖零窗口）在這道樓梯上沒有再觀察到**——但這**不是**
  證明 Pelvis 邏輯本身沒問題，是因為**上游的雜訊源頭被移除了，下游放大器沒有東西可以放大**。
  §3.3 那個「雙腳都命中才算」的資料構造缺陷**仍然存在於程式碼裡**，只是這道樓梯的幾何佈局
  暫時沒有把它逼出來。
- 使用者事先設下的分岔條件（「root 跳變消失但 pelvis 仍週期性歸零 ⇒ 才進 Pelvis 重構」）
  **本輪兩者一起改善，沒有觸發那個分岔**——照原定計畫，Pelvis 重構**不必**在本輪跟進，
  留給日後有需要時（例如更複雜的樓梯幾何、更快的爬升速度）再評估。
- **Ramp／TreadIKOnly 互斥驗證**：全程未觀察到 `queryHit`／`HasGround` 同時命中兩個 layer 的
  異常訊號（Foot IK 排除 `RampProto` 位元後，命中來源只可能是 `TreadIKOnly` 或其他既有地面）。

### 4.5 現況與待裁決

- **這是單一樓梯的可逆實驗**，資產改動只影響 `Stairs_650_400_300_Prefab` 這一個場景實例
  ＋ X Bot 的 `FootIKController.GroundLayers`＋新增兩個 layer 定義（layer 定義本身是專案級的，
  但未使用不影響任何既有物件）。
- 場景目前處於**未存檔**狀態（新建的 ramp GameObject、樓梯 layer 變更），使用者可自行決定
  是否存檔／要不要推廣到其餘 3 道樓梯／是否要正式回頭處理 Pelvis 支撐語意（§3.3）。
- 若要推廣到其餘樓梯或轉正式架構，建議依 `docs/42` §8 的規格（R1–R10 機制規則、regression
  test 清單）同一套紀律走一次，本文件只覆蓋這一道樓梯的驗證。
