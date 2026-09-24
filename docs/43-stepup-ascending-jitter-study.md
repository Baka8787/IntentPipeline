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

---

## 5. Walk/Run Down/Up 全向量 Play 驗證 ＋ 腳步聲診斷（2026-09-23，⛔ 只讀不改，未修任何程式）

> 使用者人工體感明確裁決：**`IsGrounded` 穩定 ≠ 上樓呈現問題已解**——Sprint 上樓視覺上仍不自然，
> 樓梯上腳步聲節奏明顯不規律。本節任務改為：root 平滑之後，剩下的不自然到底是 Foot IK、
> Pelvis、動畫腳相位、還是腳步接觸語意？⛔ 本節只診斷，不加 IK smoothing／audio cooldown／
> grace window／ramp 改動。

### 5.1 方法與已知限制

沿用 §4 的 ramp prototype（未變動）＋ `enableStepUpDiagnosticsPrototype` 的 `[StepUpDiag]` 逐帧
trace（`docs/43` §1，欄位：pos.y／`IsGrounded`／`stepOffsetDelta`／`pelvisY`／左右腳 IK 權重／
左右腳 `HasGround`）。用 `unity cmd console --tail 20000` 撈 Console、Python 解析 JSON、依
z∈[9.0,12.5]∧x∈[5.0,12.5]（樓梯／ramp 的世界座標footprint）framing 出「在樓梯上」的連續片段，
再依 Δy 符號切出上／下段，依水平速度（Walk≈2.0–2.2 m/s／Run≈3.6–4.7 m/s，對照 `docs/33` §G 的
bake 常數）分類步態。

⚠️ **Console 緩衝區有明確的保留上限**（實測約 2000 筆 log-level entries）——⏹ **第一次**拉取時
（Walk 測試）因為使用者在測試之間有長時間停頓／重新進出 Play，緩衝區只保留了尾端的兩小段真實
高速片段（6.8s ＋ 18.2s），較早的段落已被排出緩衝區。**第二次**拉取（Run 測試，本節主體）同樣
只保留兩段（3.6s ＋ 23.8s）。兩次都足以覆蓋 Walk／Run、上／下四種組合各數次，但**若要覆蓋整場
測試逐秒不漏，需要更小的 `--tail` 間隔或更頻繁地拉取**，本節分析建立在「倖存片段具代表性」
的前提上，不是逐秒無縫的記錄。

🔴 **腳步聲事件本身完全沒有 Console instrumentation**——`FootstepDetector`／`FootPlantTracker`
的既有 debug 機制是 Editor **面板**環形緩衝（`recordDebugTrace` → Inspector「Footstep Trace」
面板），不是 `Debug.Log`，本輪未開啟也未改動。**本節對腳步聲異常的診斷因此是「用已有的地面／
IK 訊號 ＋ 讀 `FootstepDetector.cs`／`FootPlantTracker.cs` 原始碼，推導訊號在該邏輯下會被怎麼
處理」，不是直接讀到的事件序列**——這個界線在下面每一條結論都會標明。

### 5.2 Run Up：root 乾淨，但 Pelvis／Foot IK 權重重現 §3.3 已預告的缺陷

三段獨立的 Sprint 上樓連續片段（同一道樓梯，見 5.1 的 footprint 切法）：

| Pass | Frames | 時長 | 水平速度 | `postGrounded=False` | pelvisY 範圍 | 單腳權重被釘住的最長連續帧數 |
|---|---|---|---|---|---|---|
| pass1 | 588–665 (n=78) | 0.89s | 4.46 m/s | **0/78** | −0.2408 ~ 0.0000 | 右腳釘 1（18 帧）／左腳釘 0（24 帧） |
| pass9 | 905–964 (n=60) | 0.70s | 3.85 m/s | **0/60** | −0.2414 ~ 0.0000 | 右腳釘 1（14 帧）／左腳釘 1（14 帧） |
| pass15 | 1579–1634 (n=56) | 0.54s | 3.62 m/s | **0/56** | −0.2505 ~ 0.0000 | 右腳釘 1（16 帧）／左腳釘 0（7 帧） |

**root／碰撞層完全乾淨**：三段合計 194 帧，`postGrounded` 一次都沒有變 False——ramp 對「移動
碰撞」這一層在 Run 速度下依然成立，跟 §4.3 的 Walk 結論一致，**這部分的驗證通過，且本輪把它
擴大到 Run 也確認了**。

**但下游訊號明顯劣化**：取 pass1 的逐帧片段（f622–665，右腳權重 pin 1.0 → 左腳權重 pin 0 的轉換
帶）：

```
f=622 pelvisY=-0.13664 Lw=0.000 Rw=1.000 Lg=T Rg=T   ← 雙腳都 HasGround=True，但左腳權重已經 0
f=630 pelvisY=-0.11044 Lw=0.000 Rw=1.000 Lg=T Rg=T   ← 持續 8 帧，pelvisY 累積下潛
f=644 pelvisY=-0.24080 Lw=0.000 Rw=0.330 Lg=T Rg=T   ← 右腳權重開始鬆動，pelvisY 來到本段極值
f=645 pelvisY=-0.20078 Lw=0.000 Rw=0.266 Lg=T Rg=F   ← 右腳 HasGround 才變 False（權重早於 Ground 觀測崩潰 15 帧）
f=648 pelvisY= 0.00000 Lw=0.167 Rw=0.000 Lg=T Rg=F   ← pelvisY 立即歸零（「雙腳都命中才算」規則生效）
f=655 pelvisY= 0.00000 Lw=1.000 Rw=0.000 Lg=T Rg=F   ← 左腳權重頂滿 1.0，鏡像重演
```

三段共同型態：①`pelvisY` 在雙腳都 `HasGround=True` 的正常窗口內就先出現 −0.24~−0.25m 的深下潛
（是 §4.3 量到的 Walk 幅度 −0.054m 的 **4.5 倍**）；②單腳權重會在**權重門檻**先被夾到 0 或 1
並維持 14–24 帧（0.17–0.29s @83fps），比 `HasGround` 真正翻成 False 還早幾帧到十幾帧觸底；
③一旦擺盪腳的 `HasGround` 終於翻成 False，`pelvisY` 立即 snap 回 0.00000（`docs/43` §3.3 描述的
「雙腳都命中才算，否則鎖零」三元運算式），下一步換腳時鏡像重演。

**結論**：這正是 §3.3 當時的預告——「這道樓梯這一輪沒有觸發 Pelvis 分岔，但那不代表 Pelvis
邏輯沒問題，只是上游雜訊被拿掉、下游放大器沒東西可放大」。**Run 速度把擺盪腳留空的時間拉長，
剛好重新餵飽了這個放大器**：`ComputePelvisOffset` 的雙腳-AND 閘門，配合 `WeightSmoothSpeed`
的有限爬升速率，在 Run 步頻下第一次被逼出明顯的視覺症狀（±0.25m 的骨盆下潛／回彈）。
**這就是使用者回報「Sprint 上樓仍不自然」的直接成因，且證據鏈完整落在 Foot IK／Pelvis 這一層，
root 本身沒有問題**——`docs/43` §3.3 已經指出正確的修法方向（把「查到候選面」／「是不是支撐
腳」／「套用多少 IK」三件事從同一個三元運算式拆開成「有效支撐集合」的資料模型），本輪只是
用 Run 速度的真實數據把那個預告變成了已驗證的症狀。

### 5.3 Run Down：root 也不像 Walk 那樣乾淨——ramp 沒有完全解決快速下樓

對照組：Walk 下樓（§2/§4 已驗證的乾淨案例）vs 本輪新測到的 Run 下樓：

| Pass | 水平速度 | `postGrounded=False` | `leftHasGround=False` | `rightHasGround=False` | False run 分布（帧） |
|---|---|---|---|---|---|
| Walk 下樓（對照，§4.3 同款） | 2.0–2.2 m/s | 0/205 | — | — | — |
| **Run 下樓 pass6** | 4.28 m/s | **69/83** | 73/83 | 71/83 | [1, 10, 25, 21, 11, 1] |

`pass6`（frames 744–826，y 從 4.080 平滑降到 1.626，全程 82 帧**沒有任何一帧出現位置級的不連續
跳變**——逐帧 `actualDY` 是連續、單調變化的負值曲線，不是拋物線）：`characterController.isGrounded`
本身卻在同一段裡有 69/83 帧回報 `False`，且不是單一長段落地失敗，是分散成 6 段（1/10/25/21/11/1
帧）反覆閃爍。**這不是 §2/§4 那種樓梯級緣造成的彈道下墜**（沒有位置不連續，ramp 幾何本身沒有
造成真的自由落體）——比較像 Unity `CharacterController.isGrounded` 在快速下坡時本身就是已知不
可靠的旗標（社群已知限制，`docs/42` §2 也引用過同一類討論），與「樓梯離散級高」是兩個不同機制。

**與 FSM／事件層的交叉核對（用既有的 `[State]` log，未新增任何程式）**：整場 Run 測試只出現
兩對 Falling/Landing 進出（一對是進場跑動途中的地形小落差、一對是使用者主動按跳），**pass6 這
一整段快速下坡完全沒有觸發 `JumpState` 的 Falling 動畫**——`fallEntryGrace`（0.35s）比 pass6 最
長的單段掉出（25 帧≈0.30s）還長，所以**事件層（動畫／FSM）沒有被污染，這與 `docs/31` 已修的
落地音效閃爍是同一道防線，這裡確認它在 Run 下樓時仍然有效**。

🔴 但 `FootstepDetector.Detect`（`Assets/Scripts/Presentation/Footstep/FootstepDetector.cs:208`）
讀的是**沒有 grace 過濾的原始 `data.IsGrounded`**（同一個布林值，就是上表的 `postGrounded`）：
`!isGrounded` 時，任一隻腳只要同時 `HasGround=False` 就整段強制 `FootstepKind.None`（理由
`Airborne`）。pass6 這 83 帧裡，`leftHasGround`／`rightHasGround` 同樣高比例 False（73／71 之
83），與 `postGrounded` 幾乎同步——**代表 Run 下樓這一整段有極高比例的帧會被腳步偵測器判定
「本帧沒有意義」而直接吃案**，剩下零星的短暫 grounded 窗口才可能真正擊發。這足以解釋「樓梯
上腳步聲節奏不規律」的**下樓**分量：不是誤觸發多餘事件，是**大量真實步伐被 Airborne 靜音，
只在少數窗口恢復**，聽起來就是「該有聲音的地方沒有、忽然又有」。

⚠️ **這一條與 §5.2（Run 上樓）是兩個不同的根因**，不要混在一起修：上樓是 Pelvis／權重的**視覺
下潛**，下樓是 `IsGrounded` 旗標閃爍造成的**腳步聲大量吃案**，根本原因也不同（前者是既有的
雙腳-AND 資料模型缺陷；後者疑似 Unity `CharacterController.isGrounded` 在快速下坡的已知侷限，
不是 ramp prototype 的幾何缺陷——ramp 沒有讓角色真的彈道式墜落，只是這顆旗標本身不可靠）。

### 5.4 Run 上樓的腳步聲：`groundSourceChanged` 不連續守門會吃掉轉換帧，但頻率不算高

`FootPlantTracker.Advance`（`Assets/Scripts/Presentation/Footstep/FootPlantTracker.cs:236-240`）
有三種 `discontinuity` 觸發：①`hasGround` 本帧跟上帧不同（`groundSourceChanged`）；②地面高度單
帧跳超過 `GroundStepTolerance`(0.06m)；③`fallbackGroundReferenceY`（root 平面 Y）單帧跳超過同一
個 0.06m 門檻（**這條不管該腳有沒有地面觀測都算，因為程式碼無條件計算它**）。不連續的那一帧會
被當成「量尺跳動」整帧捨棄（速度算 0、不計行程、`GroundDiscontinuity` 抑制），這正是 `docs/35`
§9 記載的「上樓每隔 8~9 帧多擊發一次」那個 bug 曾經用來擋的機制。

**實測結果（用既有 `[StepUpDiag]` 資料重建，非新增 log）**：

| Pass | rootPlaneJumped 候選帧（\|actualDY\|>0.06） | leftHasGround 翻轉次數 | rightHasGround 翻轉次數 |
|---|---|---|---|
| Run Up pass1 (n=78) | 1 | 1 | 2 |
| Run Up pass9 (n=60) | 0 | 2 | 0 |
| Run Up pass15 (n=56) | 0 | 4 | 0 |
| Walk Up 對照 (n=188) | 0 | 4 | 4 |

`rootPlaneJumped`（0.06m 門檻）**幾乎沒有被 Run 速度的連續 ramp 爬升觸發**（194 帧裡只有 1 帧、
且不是常態）——推翻了本輪一開始「root 逐帧位移在 Run 下太大，會頻繁誤觸發這個原本只防
`stepOffset` 跳變的門檻」的假說，**用資料驗證後不成立，不要沿用**。`HasGround` 翻轉次數在 Run
與 Walk 兩組（1–4 次／每段）量級相近，**並非 Run 特別嚴重**——每次翻轉會讓 `groundSourceChanged`
在那一帧強制不連續，可能剛好蓋到某一步真正落腳的那一帧、使該步的偵測延後或漏拍，但**發生頻率
本身不是 Run 上樓的主要矛盾**，§5.2 的 Pelvis／權重下潛才是更大、更一致的訊號。

**淨結論**：Run 上樓的腳步聲不規律，主要成因**推定**不是 `FootPlantTracker` 的不連續守門（頻率
不足以解釋「明顯不規律」的體感），而更可能與 §5.2 同源——Foot IK 權重被夾在極值的窗口，與
`FootPlantTracker` 自己的「相對地面抬起足夠行程才重新上膛」（`ReArmLift`／`NormalStepLift`，量
的是 pre-IK 動畫 pose，不讀 IK 權重）理論上互相獨立，但**兩者共用同一組 `FootIKGroundData`
（`ground.Left/Right.HasGround`／`GroundY`）**——pass1 顯示右腳權重早於 `HasGround` 翻假 15 帧
就已經觸底，代表地面觀測本身在權重崩潰前就已經不穩定，這個不穩定同時餵給 Pelvis（視覺）與
FootPlantTracker（聲音）兩個消費者，是兩邊症狀「同時且同源」的最合理解釋——⚠️ **這是根據程式
碼交叉推理的假說，不是直接量到的腳步聲事件，見 §5.5 的下一步**。

### 5.5 左右交替／同腳連續擊發：現有訊號看不出異常

在權重與 `HasGround` 的原始資料裡（pass1/9/15 全部逐帧檢視），左右腳的擺盪／支撐相位**沒有
觀察到同一隻腳連續兩次都是擺盪腳**的情形——右腳權重歸零時左腳權重必然在爬升，反之亦然，交替
本身在 pre-audio 訊號層看起來是正確的。**但這不能代表音效端的左右交替一定正確**：`FootstepKind`
的判定（`FootPlantTracker.Advance`）是獨立於 Foot IK 權重的另一條路徑（見 §5.4），本節看到的
「交替正確」只保證了 IK／Pelvis 這一層，不保證音效觸發那一層——如果 §5.4 的不連續守門剛好在
某一步的擊發帧生效，該步可能被跳過而不是錯配到另一腳，**結果會是「漏拍」而不是「同腳連擊」**，
與使用者回報的「節奏不規律」比「認錯腳」更吻合。

### 5.6 現在能回答與不能回答的部分（誠實列表）

**能回答（本節已用資料＋程式碼確認）**：
- Run 上樓 root／碰撞層乾淨（`postGrounded` 194 帧 0 False），但 Pelvis／Foot IK 權重層出現
  ±0.25m 量級的視覺下潛與 14–24 帧的權重釘死——**確認是 §3.3 預告的既有缺陷被 Run 速度重新
  暴露，不是新缺陷、也不是 ramp 造成的新缺陷**。
- Run 下樓 root 位置連續平滑，但 `CharacterController.isGrounded` 本身在同一段裡高比例
  （69–73/83 帧）閃爍 False——**與 Walk 下樓的乾淨結果形成明確對比，ramp 沒有完全解決 Run
  速度下樓的 grounded 旗標穩定性**，且此段全程未觸發 FSM Falling（`fallEntryGrace` 過濾住了）。
- `FootstepDetector` 對 `IsGrounded` 的讀取**沒有** `groundEdgeEventGrace` 過濾，Run 下樓的旗標
  閃爍會直接造成大量 `Airborne` 抑制——**這是下樓腳步聲不規律最直接、證據最完整的成因**。
- `FootPlantTracker` 的不連續守門（`rootPlaneJumped`／`groundSourceChanged`）經量測**不是** Run
  上樓的主要矛盾（頻率與 Walk 對照組相近），已用數據排除，不要沿用「這是門檻沒調好」的猜測。

**不能回答（現有 instrumentation 缺口，本輪刻意沒有動程式去補）**：
- **沒有任何腳步聲事件本身的紀錄**（帧號／`FootstepKind`／`FootstepSuppressReason`／Surface）
  ——§5.2–§5.4 的因果鏈是「用地面／IK 訊號 + 讀程式碼」推導出來的**假說**，不是直接觀測到的
  事件序列。要把「推定」變成「確認」，最小的下一步是暫時把 `FootstepDetector.Detect` 的擊發與
  抑制原因（已經有 `FootstepSuppressReason` 這個 enum，本來就是為了這個目的存在）接一條
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 的 `Debug.Log`（同 `LogStepUpDiagnosticsTrace` 的
  prototype 模式，純觀測、不寫黑板、不改行為），再補一輪 Run Up／Run Down 的 Play 測試。
- **沒有 locomotion／動畫腳相位的直接量測**（例如 Animator 的 normalized time 或 gait blend 權
  重）——目前的 `[StepUpDiag]` 沒有這個欄位，pass1 的「右腳權重早於 HasGround 翻假 15 帧觸底」
  只能說明 Foot IK 內部的權重平滑節奏，不能直接對應到動畫本身的擺盪/支撐相位。
- **沒有實際播放的音效（`AudioSource`／`AudioController`）事件記錄**——即使有 `FootstepKind` 的
  log，仍不確定 `AudioController` 消費端有沒有自己的節流／延遲邏輯疊加在上面（本輪未讀該檔）。

### 5.7 下一步（單一建議，不展開多選項）

在使用者裁決是否要追這條線之前，**唯一合理的下一步**是：加一條純觀測、`#if UNITY_EDITOR ||
DEVELOPMENT_BUILD` 包住的 `Debug.Log`，把 `FootstepDetector.Detect` 每次回傳的
`(frame, foot, FootstepKind, FootstepSuppressReason, clearance, verticalSpeed, horizontalSpeed)`
連同 `events.Landed`（是否為落地音效）一起吐出來——欄位在程式裡已經全部存在（`TraceEntry` 那
個環形緩衝結構就是現成的欄位清單），只是換一個輸出管道（Console log 而非 Inspector 面板），
**不改變任何判定邏輯**。加上之後重跑一次 Run Up／Run Down，才能把 §5.2–§5.4 的假說換成真正
量到的事件序列。⛔ 本節到此為止不擅自加這條 log——留給使用者裁決是否要開下一輪。

---

## 6. `[FootstepTrace]` 事件級驗證（2026-09-24，⛔ 只讀不改，未修任何判定邏輯）

> 使用者裁決：加上 §5.7 建議的觀測 log 再測一輪。**追加的程式**：`FootstepDetector.cs` 新增
> 獨立、預設關閉的 `enableFootstepConsoleTrace`（與既有 `recordDebugTrace` 面板 trace 完全分離、
> 不共用 last-reason 狀態），在 `Detect` 內既有的 `RecordTrace` 呼叫點旁邊，用同一組已經算好的
> `left`／`right`／`leftReason`／`rightReason`／`_leftTracker`／`_rightTracker` 输出一行 Console
> log，只在「有事件或抑制原因改變」時寫（同款節流，不逐帧輸出）。`ikHasGround`／`ikWeight` 只是
> 額外讀 `FootIKController` 當相關性欄位，**偵測邏輯本身完全不知道這兩個欄位存在**。改動已 `unity
> cmd recompile` 驗證 0 error。用完的 flag 仍保留在程式裡（跟其他 prototype 開關同款慣例），
> 目前 X Bot 上是 **開啟**狀態（供後續複測沿用，需要停用時直接把 Inspector 勾選取消即可，可逆）。

### 6.1 資料涵蓋範圍

本輪 Console 拉取同時抓到 `[StepUpDiag]`（1531 筆）＋ `[FootstepTrace]`（441 筆，兩腳合計）＋
FSM `[State]`（16 筆），**時間窗完全重疊**（同一個 23.6 秒的連續 Play 片段，frame 1→1189／1241），
兩條 log 可以逐帧對齊，不像上一輪要跨兩個不連續片段拼湊。額外意外收穫：這一段一開始有 **~250
帧的平地助跑**（frame 109–347，`rootSp`≈4.70 恆定），提供了一個乾淨的「非樓梯」對照組。

### 6.2 平地對照組：偵測器本身沒有壞——交替完美、間隔規律

平地 Sprint 段（frame 109→347，共 16 次 `Fired`）：

```
f=109 L → f=124 R(+15) → f=136 L(+12) → f=153 R(+17) → f=174 L(+21) → f=187 R(+13) →
f=201 L(+14) → f=222 R(+21) → f=238 L(+16) → f=252 R(+14) → f=268 L(+16) → f=288 R(+20) →
f=298 L(+10) → f=311 R(+13) → f=331 L(+20) → f=347 R(+16)
```

左右**完全交替**（L/R/L/R…零例外），間隔全部落在 10–21 帧（@~83fps ≈0.12–0.25s，符合 Sprint
步頻的合理範圍），沒有任何 `Airborne`／`GroundDiscontinuity` 抑制。**這確立了一個關鍵基準：
`FootstepDetector`／`FootPlantTracker` 的核心判定邏輯在平地上是健康的**，下面兩節看到的異常
是樓梯／ramp 這個地形的特定產物，不是偵測器全域性的缺陷。

### 6.3 Run Down（pass4，frame 499–557，58 帧，4.06 m/s）：**假說確認成立，且比原假說更嚴重**

**逐帧比對 `isGrounded` 閃爍與 `Fired`／`Airborne`**：

```
f=500–507  isG=True   （加速下坡，Descending/GroundDiscontinuity/AboveContactBand 循環，未擊發）
f=508      isG=False  → 雙腳 reason=Airborne（detG=False, ikG=False，地面觀測也一起消失）
f=511      isG=True   （短暫恢復，GroundDiscontinuity——恢復瞬間本身又是不連續）
f=512      isG=False  → 雙腳 Airborne
f=524      isG=True   （GroundDiscontinuity）
f=525      isG=False  → 雙腳 Airborne
f=537      isG=True   （GroundDiscontinuity）
f=538      isG=False  → 雙腳 Airborne
f=553      isG=True   （NeverArmed／GroundDiscontinuity）
f=555      isG=False  → 雙腳 Airborne，rootSp 降到 0.00（片段尾端）
```

**整段 58 帧、5 次 `isGrounded` 閃爍、10 次 `Airborne` 抑制（雙腳×5次）、`Fired` 次數 = 0**。
不是「間隔變長」，是**整段快速下坡完全沒有播放任何一聲正常腳步**——原假說預期的是「間隔拉長、
不規律」，實測比假說更嚴重：**每次 `isGrounded` 短暫恢復的窗口都太短，還沒走完
`Descending→AboveContactBand→PlantHorizontalSpeedMax` 這條完整鏈就被下一次 `Airborne` 打斷，
連一次都湊不齊**。

✅ **假說判定：CONFIRMED，且方向正確——是大量吃案／靜音，不是重複事件**。全程 `ForcePlanted`
次數 = 0（見 §6.5 全域統計），**排除「這一段有多餘 Landing 音效被誤插」的可能性**：問題純粹是
「該有聲音的地方沒有」，不是「不該有聲音的地方多了一聲」。

⚠️ **架構觀察，不是這輪要改的東西**：`FootstepDetector.cs:208` 讀的 `isGrounded` 是**未經
`groundEdgeEventGrace` 過濾的原始值**（跟 `postGrounded`／`characterController.isGrounded` 同一
個訊號），這是刻意的架構選擇不是疏漏（見 `FootstepDetector.cs` 類別註解 §Airborne 一節的說明：
「本元件不該知道有哪些狀態存在」，grace 過濾屬於 `MotionDriver` 的事件層，不歸這裡管）。**這條
原始訊號在 Run 下樓時是否適合直接當音效抑制閘門，是這一整輪要驗證的問題本身，不是本節要下
判斷去改的東西**——本節只負責把「用了原始訊號、原始訊號會閃、閃了就吃案」這條因果鏈坐實。

### 6.4 Run Up（pass1 frame 398–446、pass5 frame 557–597）：**不是同一個病灶，是獨立的低觸發率問題**

**與 §5.2 的區隔**：§5.2 量到的是 Pelvis／Foot IK 權重的**視覺**下潛（±0.25m），本節量的是
**音效**觸發率——兩者共用同一份地面觀測（`FootIKGroundData`），但走的是兩條獨立的判定路徑
（`ComputePelvisOffset` vs `FootPlantTracker.Advance`），不能假設兩者是同一個修法能一起解決。

**pass1（0.89s，49 帧）**：只有 **1 次 `Fired`**（f=440，左腳，`ikWeight=1.000`——與 §5.2 的
「權重釘滿 1.0」同一時刻），對照平地在同樣時長內應該有 4–5 次交替擊發。同一段裡右腳在 f441–443
卡在 `Descending`→`AboveContactBand` 循環，視窗結束前沒有解出來（後續在 pass1／pass5 之間的
frame 447–499，即 WALK 速度的 pass3，才在 f=459 補上一次右腳）。**平地→上樓的過渡本身也貴**：
從平地最後一次擊發（f347）到上樓第一次擊發（f440），間隔 **93 帧**，接近平地正常間隔的 6 倍。

**pass5（41 帧）**：f=559 兩腳同時 `GroundDiscontinuity`，f=560 右腳 `Fired`，**4 帧後（f=564）
雙腳同時 `ForcePlanted`**——這代表這 4 帧內 `justLanded` 又觸發了一次（即使 `postGrounded` 在
§5.2 量到的三段 Run Up 全程沒有 `False`，這裡用的是**同一個 X Bot、同一輪測試但不同 pass**，
落地判定仍然可能在某些過渡幀被觸發，見下方 §6.5 的全域統計：整場測試只有 3 次 `ForcePlanted`，
這是其中一次，發生在 Run Up 而非 Run Down）。`ForcePlanted` 會把雙腳強制收回 `Planted` 並清空
上膛狀態——如果這一下發生在某一腳正在累積上膛行程的中途，那次上膛就直接作廢，**是 Run Up
低觸發率的第二個獨立成因**（第一個是 §6.3 同款但發生頻率低很多的 discontinuity 循環）。

**結論**：Run 上樓的腳步聲問題**確認獨立於** Pelvis/Foot IK 視覺缺陷，成因是**擊發率過低**
（平地約 13–21 帧一次，上樓段延伸到 40–101 帧一次，且偶爾疊加一次 `ForcePlanted` 清空上膛進度）
——不是「同一隻腳連擊」，是「該響的那一步經常沒響」，累積出來的聽感才會是「不規律」。

### 6.5 全域統計（整場 23.6s 測試，含 Walk 段）

**`Fired` 事件間隔全列表**（`frame, foot, 與上一次的間隔`，見 scratchpad 原始輸出）：平地段
（f109–347）間隔穩定在 10–21 帧；一進入樓梯／ramp footprint（f440 起）間隔暴衝到
`93, 19, 101, 50, 8, 12, 40, 46, 37, 22, 23, 26, 40, 22, 23, 15, 82, 60, 37, 5, 11, 18, 65`
帧，**忽長忽短，且反覆出現同一隻腳連續兩次擊發**（f610→f670→f716→f753 連續四次都是右腳、
f798→f864 兩次左腳、f1066→f1103 兩次左腳）——這些同腳連擊全部落在 **WALK 速度**的樓梯段
（`rootSp`≈2.29，對應 §4 已驗證「root／IsGrounded 乾淨」的那些 pass）。

🔴 **這是超出本輪原始提問範圍、但資料直接呈現的新發現**：**§4 的「Walk 上下樓乾淨」結論只涵蓋
root／碰撞層（`postGrounded` 全程無 False），不涵蓋腳步聲**——即使 root 完全平滑、`IsGrounded`
全程 `True`，`FootPlantTracker` 在樓梯地形上仍然會反覆讓某一隻腳的步伐無聲無息地被吃掉
（`NeverArmed`／`GroundDiscontinuity`／`NotReArmed` 其中之一），使音效序列變成同腳連續擊發。
**Walk 速度下樓梯腳步聲同樣不規律，只是原因與 Run 不同**（Run Down 是 `Airborne` 整段吃案；
Walk／Run Up 是零星的 discontinuity／NeverArmed 死路，導致其中一腳的步伐持續缺席）。

**`ForcePlanted` 全域只出現 3 次**（f=28 平地早期一次、f=403 與 f=564 各一次，皆在 Run Up 相關
片段）——**Run Down 全程 0 次**（見 §6.3），代表 Run Down 的問題與「意外插入 Landing 音效」
無關，是純粹的吃案；Run Up 的 2 次 `ForcePlanted` 則是低觸發率的其中一個貢獻因子（見 §6.4）。

**抑制原因分布（441 筆裡的 reason 欄位）**：`Descending`(135) > `GroundDiscontinuity`(104) >
`NeverArmed`(48) > `Fired`(39) ≈ `NotReArmed`(39) > `StillSwinging`(30) > `AboveContactBand`(22)
> `Airborne`(14) > `None`(6) > `BelowNormalStep`(2) ≈ `NoTimeStep`(2)。`GroundDiscontinuity` 是
第二大宗（104／441 ≈ 24%）——這條守門（§5.4 已定位在 `FootPlantTracker.cs:236-240`）在樓梯地形
上被觸發的絕對頻率其實不低，只是上一輪只看 `HasGround` 翻轉次數（每段 1–4 次）低估了它的整體
影響——**本輪用事件級資料重新確認：它是 Run Up 與 Walk 上下樓「某一腳經常缺席」的主要嫌疑，
比例上比 `Airborne`（Run Down 專屬）更常出現，但集中影響的是「單一次上膛鏈路被打斷」而非「整段
靜音」，所以聽感上是「不規律」而非「完全沒聲音」——這和 Run Down 的失效模式性質不同，程度也
不同**。

### 6.6 逐項回答使用者的檢查清單

- **左右交替在偵測器層是否正確？** 平地：完全正確。**樓梯／ramp：不正確**——多次觀察到同一隻
  腳連續擊發 2–4 次（§6.5），但根因是**另一腳的步伐被 discontinuity／NeverArmed 吃掉而沒有
  擊發**，不是偵測器把哪一步錯配到另一隻腳——沒有觀察到「腳的身分算錯」，只有「該擊發的那次
  沒有擊發」。
- **間隔是否週期性太短或太長？** 兩者都有：樓梯段間隔從平地穩定的 10–21 帧散布到 5–101 帧，
  同時包含異常短（5、8 帧，可能是延遲補發）與異常長（93、101 帧）的極端值。
- **是否同一隻腳反覆擊發？** 是，已用具體 frame 列表確認（§6.5），機制是「另一腳缺席」而非
  「同一腳誤觸發兩次」。
- **`ForcePlanted`／shuffle／landing 是否插入非預期事件？** `ForcePlanted` 全域僅 3 次，Run Down
  0 次（排除）；Run Up 2 次，是低觸發率的**次要**貢獻因子。`Shuffle` 這個 `FootstepKind` 全程
  未被觀察到觸發過（程式邏輯上本來就已經退化成一律靜音，見 `FootPlantTracker.cs:346-351` 的
  `BelowNormalStep` 分支說明），沒有意外多播的摩擦音。`Landing`（`events.Landed`）本節未單獨
  對照 `AudioController` 消費端，但 `justLanded`＝`ForcePlanted` 的次數已經排除了「大量落地音」
  的可能性。
- **音效觸發時機是否與腳真正透過 IK 取得踏面支撐錯位？** `ikWeight` 相關性欄位顯示 f=440 的
  `Fired` 恰好發生在 `ikWeight=1.000`（IK 權重頂滿）那一刻，**時機吻合**；但樣本數太少
  （全樓梯段只有個位數 `Fired` 事件），不足以下「永遠吻合」或「永遠不吻合」的通則，只能說
  **這次觀察到的少數成功案例沒有錯位**，錯位與否的問題本身被「大部分該響的步伐根本沒響」
  蓋過去了——這才是聽感不規律的主因，不是「響的時機不對」。

### 6.7 小結：兩個獨立成因，都已用事件級資料確認，尚未提出修法

| | Run 上樓 | Run 下樓 |
|---|---|---|
| root／`IsGrounded` | 乾淨（§5.2） | 不乾淨，旗標閃爍（§5.3） |
| Pelvis／Foot IK 視覺 | 深下潛 ±0.25m（§5.2，已確認） | 未特別測（非本輪重點） |
| 腳步聲觸發率 | 過低（平地 13–21 帧一次 → 樓梯 40–101 帧一次） | 全段 0 次擊發（Airborne 吃案） |
| 腳步聲失效模式 | 零星 discontinuity／NeverArmed 死路 ＋ 偶發 `ForcePlanted` 清空上膛 | 集中、連續的 `Airborne` 整段抑制 |
| 是否已知機制 | 是（`GroundDiscontinuity` 守門＋`justLanded` 誤觸發，§5.4／§6.4） | 是（未過濾的 `IsGrounded` 直接餵給 Airborne 閘門，§6.3） |

⛔ **本節只做到「確認」為止，未提出修法、未動任何判定邏輯／grace／cooldown／IK／Pelvis／ramp**。
`enableFootstepConsoleTrace` 目前仍是可逆的 prototype 開關（關閉即回到零輸出零副作用），下一步
待使用者裁決是否要開始討論修法方向。

---

## 7. Run/Walk 下樓腳步聲吃案——訊號歸屬盤點 ＋ 最小修法提案（2026-09-24，⛔ 提案，未實作）

> 使用者裁決：本節**只處理下樓吃案**，Run 上樓的 Pelvis／`FootPlantTracker` 內部守門
> （`GroundDiscontinuity`／`NeverArmed`，§5.2／§6.4）**不在本節範圍，不動**。問題不是「音效該用
> 多少 grace」，是「現有訊號裡，哪一個本來就承載『短暫 `isGrounded=false` 是不是真的空中』這個
> 語意，音效該重用它而不是自己再發明一顆計時器」。本節只盤點與提案，**不實作**。

### 7.1 訊號歸屬總表（黑板現有欄位 ＋ 未進黑板的 FSM 狀態）

| 訊號 | 唯一 Writer | 更新頻率／語意 | 目前消費者 | 對「短暫 flicker vs 真的空中」的判斷力 |
|---|---|---|---|---|
| `PlayerRuntimeData.IsGrounded` | `MotionDriver.SyncGroundedState`（每幀寫本幀原始 `characterController.isGrounded`） | 逐幀、**無任何去抖動**，`docs/38` §13 明文裁決不得加否決層 | `FootIKController`（刻意）、`FootstepDetector` 的 Airborne 閘門（刻意，見 7.2）、`JumpState.CanEnter`（作為 `TrackUngroundedSince` 的原始輸入） | **無**——這正是問題根源，逐幀真值，不代表「遊戲性意義上的空中」 |
| `PlayerRuntimeData.JustLanded`／`JustLeftGround` | `MotionDriver.SyncGroundedState`（同一函式） | **單幀邊沿事件**，經 `groundEdgeEventGrace`（預設 0.1s）確認後才觸發一次；復位由 `ResetTransientState()` 負責 | `AudioController`（Landing／JumpTakeoff 音效）、`FootstepDetector.Evaluate`（`justLanded` → 觸發 `ForcePlanted`） | **有，但只有邊沿，沒有「目前狀態」**——只能問「這一幀剛發生什麼」，問不了「現在算不算空中」 |
| `MotionDriver._wasGrounded`（private） | `MotionDriver.SyncGroundedState` | 每幀維護，**只在分歧持續達 `groundEdgeEventGrace` 才更新**——這其實就是「已確認的接地狀態」的**連續值**，只是目前只拿來算邊沿，沒有整個發布成 level | 無（private，未發布） | **有，且是本表唯一同時具備「有去抖動」＋「是連續狀態不是邊沿」的候選** |
| `FullBodyStateMachine.CurrentState.Type`（經 `CharacterPipelineRunner.CurrentState`） | `FullBodyStateMachine`（FSM 本體） | 逐幀，`StateType.Jump` 代表「FSM 認定目前在跳躍／非主動 Falling」，經 `fallEntryGrace`（預設 0.35s，`JumpState.TrackUngroundedSince`）判斷是否從 Move/Idle 轉入 | **僅 Editor／Debug 工具**（`CharacterPipelineRunnerEditor`／`RuntimeDebugPanel`）——**從未發布進黑板，任何 gameplay／Presentation 系統目前都讀不到** | 有，理論上最「高階」，但見 7.2 的兩個致命問題 |
| `ArbiterData.BlockAudio` | `ArbiterPipeline`（彙總各 `IArbiterSource`） | 逐幀，全域旗標 | `AudioController.Tick` 開頭 gate（`if (BlockAudio) return`） | **無關**——目前**沒有任何 `IArbiterSource` 要求它**（`AudioController.cs` 類別註解明講「恆 false」），語意是「整個音效系統靜音」，不是「這一步是不是踩在空氣上」，用途錯位 |
| `PlayerRuntimeData.VerticalVelocity` | `MotionDriver.SyncGroundedState` | 逐幀，撞地瞬間的 impact velocity（`reboundForce` 貼地前的值） | `JumpState`（落地分類）、Presentation 唯讀 | 無獨立去抖動，且下樓速度與真的墜落速度量級可能重疊，不足以單獨分辨 |

### 7.2 逐一比對候選訊號

**① 直接用 `StateType.Jump`（是否在 FSM 的 Jump／Falling 狀態）——不建議，兩個理由都是硬性的**

1. **架構層：目前完全不在黑板上**。`FootstepDetector` 若要讀它，唯一路徑是拿一個
   `CharacterPipelineRunner`／`FullBodyStateMachine` 的參考——這違反本專案的依賴方向
   （`Input → Pipeline → RuntimeData → StateMachine → Animation → Motion`，Presentation
   不得回頭認識 StateMachine），也違反 `FootstepDetector.cs` 類別註解裡**已經寫明的設計原則**
   （§Airborne 抑制那段：「⛔ 刻意不認識 traversal／jump／任何狀態——本元件不該知道有哪些
   狀態存在」）。這不是可以隨手放寬的細節，是這個元件當初為了修 Climb2m 假腳步聲那個 bug
   （見 `FootstepDetector.cs:191-201` 的說明）刻意做的設計選擇——State-agnostic 才能同時涵蓋
   Jump／Traversal／未來任何新狀態，不必每加一個新狀態就回來改一次 `FootstepDetector`。
2. **行為層：比 `groundEdgeEventGrace` 更不敏感，且用實測資料驗證過會更差**。`fallEntryGrace`
   （0.35s）＋ `JumpState.TrackUngroundedSince` 的重置規則是「**任何一幀 `isGrounded=true` 就
   整個歸零**」（`JumpState.cs:462-469`），比 `MotionDriver` 的確認邏輯（見③）敏感得多——本輪
   §6.3 的 Run Down 實測資料（frame 499–557）證實：raw `isGrounded` 在這段裡有 f510／f523／f536／
   f552-553 四次 1–2 帧的短暫回真，每次都會把 `_ungroundedSince` 打掉重練，導致**整段近 1 秒的
   下坡，FSM 一次都沒有判定為「非主動 Falling」**（比對 `[State]` log：這次測試唯一一次被動
   Falling 發生在 session 最開頭、與樓梯無關）。**用這顆訊號不會比現在更早正確辨識空中，也不會
   更晚——它幾乎不會觸發，等於變相跟現在的「完全不管」差不多，只是換了個閘門，沒解決問題。**

**② 直接沿用 `JustLanded`／`JustLeftGround`（黑板現有的邊沿事件）——不夠，因為它們是邊沿不是狀態**

`FootstepDetector.Evaluate` 已經在讀 `data.JustLanded`（觸發 `ForcePlanted`），但邊沿事件的語意
是「這一幀剛發生什麼」，撐不住 `!isGrounded` 那個**逐幀**的 Airborne 抑制閘門——抑制閘門需要
問「現在算不算空中」，不是「剛剛有沒有切換」。若試圖用「上次 `JustLeftGround` 之後到下次
`JustLanded` 之前」自己組一個區間狀態，等於在 `FootstepDetector` 裡**重新實作**
`MotionDriver._wasGrounded` 早就算好的東西——這正是使用者要求避免的「引入 Footstep 專屬計時器」
（差別只是用邊沿事件湊，本質仍是重新發明）。

**③ 發布 `MotionDriver._wasGrounded`（已確認的接地狀態）成一個連續欄位——推薦**

`MotionDriver.SyncGroundedState` 每幀都在算「這次 raw／confirmed 分歧是否已經持續滿
`groundEdgeEventGrace`」（`TrackGroundStateDivergence`／`ShouldConfirmGroundStateChange`，
`MotionDriver.cs:2677-2692`），**`_wasGrounded` 本身就是這個判斷的連續版本**——`JustLanded`／
`JustLeftGround` 只是它的邊沿投影。目前這個值是 `private`，只拿來算邊沿，沒有整個發布。

**用本輪實測資料驗證這顆訊號的行為**（frame 499–557，逐幀重建 `_wasGrounded` 的演進，方法見
附註）：

```
f499–506   _wasGrounded=True（尚未發生分歧）
f507–509   raw=False，分歧開始累積（3 帧，< 0.1s 門檻）
f510       raw=True → 與尚未確認的 reportedGrounded(True) 相符 ⇒ 分歧歸零重算
f511–522   raw=False，分歧重新累積（12 帧 ≈0.145s，**超過 0.1s 門檻**）
  ⇒ 約 f519（累積達 0.1s 那一刻）：分歧確認，_wasGrounded 翻為 False
f523       raw=True（1 帧）——此時 reportedGrounded 已是 False，1 帧不足以確認回真，分歧作廢
f524–551   raw 多為 False，與已確認的 False 相符，_wasGrounded 維持 False（不因中間 f536／f552-553
           的 1–2 帧短暫回真而抖動——這些回真沒有連續滿 0.1s，不足以確認）
f558起     raw 連續 True ⇒ 約 f564（與 §6.4 實測到的 ForcePlanted 發生於 f564 吻合）確認回真，
           _wasGrounded 翻回 True
```

**結果**：`_wasGrounded` 在這整段裡只翻轉兩次（f519 離地確認、f564 落地確認），把 5 次原始閃爍
收斂成 1 段乾淨的「真的離地」區間（約 f519–563，0.53s）——這與人類看 `docs/43` §6.3 那段 y 座標
連續下降曲線的直覺判斷一致（「這是一次持續的快速下滑，不是 5 次獨立彈跳」）。**這正是使用者要的
語意分辨力，而且完全不是新邏輯——是 `MotionDriver` 現有、已經被 `JustLanded`／`JustLeftGround`
信任的同一段程式碼，只是多發布一個連續值。**

⚠️ **對照組行為（f499–519 這 20 帧）**：目前 `FootstepDetector` 讀 raw `IsGrounded`，f507–509
那 3 帧的短暫閃現就已經足夠讓 Airborne 閘門介入、打斷任何當下正在進行的上膛／擊發判定；改讀
`_wasGrounded` 後，這 20 帧全程仍是「已確認接地」，`FootstepDetector` 的其餘鏈路（Descending／
ContactBand／…）**可以正常運作，不會被這幾帧雜訊打斷**——這是這個提案唯一實質改善的窗口；
f519–563 那 0.53s 本來就是角色真的在快速下滑，改用哪個訊號都一樣會（也應該）保持安靜。

### 7.3 最小提案（⛔ 未實作，供裁決）

| 項目 | 內容 |
|---|---|
| 新增欄位 | `PlayerRuntimeData.IsGroundedConfirmed`（暫定名，型別 `bool`） |
| 唯一 Writer | `MotionDriver.SyncGroundedState`——**與 `IsGrounded`／`JustLanded`／`JustLeftGround` 同一個函式、同一組既有變數**（`_wasGrounded`／`_groundDivergingSince`），只是在既有的 `if (ShouldConfirmGroundStateChange(...))` 分支裡多寫一行 `data.IsGroundedConfirmed = _wasGrounded;`（**兩個分支都要寫**，因為 `_wasGrounded` 在未確認分支維持不變，該值本身仍然正確，只是沒被寫進黑板） |
| 讀取者變更 | 僅 `FootstepDetector.Detect` 的 Airborne 抑制閘門（`FootstepDetector.cs:208` 的 `if (!isGrounded)`）：`isGrounded` 參數改傳 `data.IsGroundedConfirmed`（而不是目前 `Evaluate` 傳入的 `data.IsGrounded`） |
| **明確不改** | `FootIKController` 的閘門（仍讀 `data.IsGrounded`，`docs/38` §13 裁決不動）；`FootPlantTracker.Advance` 內部所有既有判準（`Descending`／`GroundDiscontinuity`／`NeverArmed`／…，§5.2／§6.4 範圍，另案）；`JumpState`／`fallEntryGrace`；`groundEdgeEventGrace` 數值本身；ramp／Pelvis |
| 為什麼不是「Footstep 專屬計時器」 | 沒有新的計時／grace 常數、沒有新的狀態機——`groundEdgeEventGrace` 早就存在且已被 `JustLanded`／`JustLeftGround` 信任；本提案只是把同一次運算的**連續值**也發布出來，Writer／更新時機完全不變 |
| 已知取捨（沿用既有、非新增） | 與 `JustLeftGround`／`JumpTakeoff` 音效相同的已知近似：主動跳躍離地也會被同一道 grace 延遲最多 `groundEdgeEventGrace` 秒才反映在 `IsGroundedConfirmed`——這個延遲**現在已經存在**（`JustLeftGround` 已經這樣做），本提案不新增任何延遲，只是讓 Footstep 也吃到同一份已經被接受的近似 |

### 7.4 這是否構成需要 ADR 的架構變更

`PlayerRuntimeData` 是黑板——新增欄位命中 `CLAUDE.md` ADR 觸發條件①「黑板 schema／ownership 變更」。
但參考既有慣例（`docs/42` §6.6 對 `maxStepDownDistance` 等新欄位的判斷、`JustLanded`／
`JustLeftGround` 當初新增時也未見獨立 ADR）：**單純新增一個由既有唯一 Writer 產出、不改變任何
既有欄位語意、不牽動 FSM 拓撲／管線順序／核心驅動介面的黑板欄位**，屬於 dev-spec §1 的增補，
不是「推翻既有架構不變量」。**建議走 Living Spec 增補（`docs/02-dev-spec.md` §1），不必開新
ADR**——但這是本節的建議，不是本節的裁決，正式新增前仍需使用者確認。

### 7.5 Regression Cases（⛔ 未實作，供實作前對照）

| # | 情境 | 設置 | 斷言 |
|---|---|---|---|
| RC1 | **平地 Sprint** | 平地連續奔跑，`IsGrounded` 全程無閃爍（§6.2 已驗證的乾淨基準） | `IsGroundedConfirmed` 與 `IsGrounded` 逐幀相等（從未分歧，`ShouldConfirmGroundStateChange` 分支不會被觸發）；`FootstepDetector` 的交替與間隔與 §6.2 基準逐位元一致——**這是最重要的一條，確保本提案對已知健康案例零影響** |
| RC2 | **Ramp Walk Down** | 依 §4／§5 的 Walk 下樓 prototype，`postGrounded` 全程 `True`（現有結論） | 同 RC1：`IsGrounded` 從未偏離，`IsGroundedConfirmed` 恆等於 `IsGrounded`，Airborne 抑制從未介入——**本提案對 Walk 下樓沒有行為改變**（Walk 下樓的腳步聲問題若存在，是 §5.4／§6.5 已定位的 `GroundDiscontinuity`／`NeverArmed`，不在本提案範圍） |
| RC3 | **Ramp Run Down** | 合成一段與本輪實測同構的 `IsGrounded` 序列（長 False 段中插入 1–2 帧 True 雜訊，例如 §7.2 的 f499–563 重建序列），驅動 `MotionDriver.SyncGroundedState` 逐幀跑過 | `IsGroundedConfirmed` 只翻轉 2 次（離地一次、落地一次），不因中途的 1–2 帧雜訊而抖動；`FootstepDetector` 在「確認分歧開始前」（本例 f499–519）的視窗內，Airborne 抑制不介入——這是本提案要解決的正例 |
| RC4 | **真的主動跳躍** | `JumpRequested` 觸發、`JumpLaunchData` 注入正向速度，角色持續上升 | `IsGroundedConfirmed` 最晚在 `groundEdgeEventGrace` 秒後翻為 `False`（與現行 `JustLeftGround`／`JumpTakeoff` 音效的已知延遲相同，見 §7.3 取捨欄）；跳躍全程 `FootstepDetector` 的 Airborne 抑制正確介入，**不得播放任何腳步聲**（防止本提案意外讓跳躍時響起腳步聲） |
| RC5 | **真的走出平台邊緣墜落** | 角色水平走出平台，垂直方向持續、單調地自由落體（無中途觸地雜訊） | `IsGroundedConfirmed` 行為與現行 `JustLeftGround` 完全一致（單調、無雜訊的情境下，確認邏輯本來就等於直接觀察）；下墜期間 Airborne 抑制持續介入 |
| RC6 | **落地** | 接續 RC4 或 RC5，角色觸地 | `IsGroundedConfirmed` 在 `groundEdgeEventGrace` 秒的**連續**真值後翻回 `True`（不得被單幀的「擦地」雜訊——例如 Climb2m 膠囊擦牆那類已知案例——提早翻真，見 `MotionDriver.cs:2616-2633` 對 committed traversal 的既有防護，本提案不改那段邏輯，只是多讓 Footstep 也吃到同一份保護）；落地後續 `Fired` 事件的交替與間隔應恢復接近 RC1／RC2 基準（不強制斷言具體帧數，因為那取決於 §5.2／§6.4 範圍外的 `FootPlantTracker` 內部判準） |

⛔ **本節到此為止**：只完成訊號歸屬盤點、候選比較、最小提案與 regression case 設計，**未新增
任何欄位、未修改 `SyncGroundedState`／`FootstepDetector`／任何判定邏輯**。是否採用 §7.3 的提案、
是否需要調整欄位命名／是否要為 RC3–RC6 先寫 EditMode 測試再動 production code，留待使用者裁決。

---

## 8. §7 提案的 Replay 驗證——**REJECTED**，`IsGroundedConfirmed` 淨效果幾乎是平手（2026-09-24）

> 使用者裁決：先別急著信「原始閃爍收斂成更少次確認轉換」這個說法本身，要求**真的把已錄到的
> Run Down trace，帶著唯一一個假設性代換（`data.IsGrounded → IsGroundedConfirmed`），跑過
> `FootstepDetector` 的真實判定邏輯**，而不是用手推的方式估。本節做的正是這件事——**不是重新
> 手推，是真的呼叫 `MotionDriver` 的正式（`internal static`）函式**跑一遍。

### 8.1 方法：真的呼叫正式程式碼，不是重寫一份等價邏輯

用 `unity cmd eval_file` 在 live Editor 對 `MotionDriver.TrackGroundStateDivergence`／
`ShouldConfirmGroundStateChange`（`internal static`，反射呼叫，**與正式程式碼是同一份**，不是
複刻）逐帧餵入這次 Run Down 測試**每一帧**（不是只有 sparse log 的 interesting 帧）的真實
`postGrounded` 序列（來源：`[StepUpDiag]`，逐帧無漏），並讀取 X Bot 上真實的
`groundEdgeEventGrace`（0.1，與文件記載一致）。輸出：每一帧的 `IsGroundedConfirmed`（即
`_wasGrounded` 的逐帧演進）。範圍：frame 480–580（100 帧，涵蓋下坡前的平穩段、整段下坡、落地
安定段），這次是用真實 timestamp 差值換算的 `Time.time`，不是假設等距。

### 8.2 逐帧比對兩種訊號下的 Airborne 抑制條件

`FootstepDetector.cs:208-212` 的真實條件是**逐腳**的：`!isGrounded && !thatFootHasGround`。
本節同時代入這次測試逐帧記錄到的真實 `leftHasGround`／`rightHasGround`（來源同樣是
`[StepUpDiag]`，逐帧無漏，不是 sparse 的 `[FootstepTrace]`），分別算出 raw 版與 confirmed 版
的抑制條件，逐帧比對。

**確認轉換時刻（真實程式碼算出，不是手估）**：離地確認於 frame 516（t=0.626s），落地確認於
frame 563（t=1.461s）——比 §7.2 手推的 f519/f564 略早／略晚幾帧，**手推的方向沒錯但精確帧數
要以本節為準**。

**總抑制帧數（100 帧視窗內，任一腳觸發 Airborne 即算）**：

| | raw `IsGrounded` | 提案的 `IsGroundedConfirmed` |
|---|---|---|
| 抑制帧數 | **39 / 100** | **40 / 100** |
| 換算時長 | ≈0.47s | ≈0.48s |

🔴 **幾乎沒有差異——提案沒有讓抑制總量變少，只是把它重新分布。**

### 8.3 差異的 11 帧逐一列出——有得有失，且失的地方更關鍵

```
f=509  raw=Airborne(兩腳)   confirmed=不抑制    ← 救回（分歧累積中，尚未確認離地）
f=511  raw=Airborne(兩腳)   confirmed=不抑制    ← 救回
f=513  raw=Airborne(兩腳)   confirmed=不抑制    ← 救回
f=514  raw=Airborne(兩腳)   confirmed=不抑制    ← 救回
f=515  raw=Airborne(兩腳)   confirmed=不抑制    ← 救回
f=523  raw=不抑制            confirmed=Airborne(兩腳)  ← 🔴 新增抑制（raw grounded=True 那一帧）
f=536  raw=不抑制            confirmed=Airborne(兩腳)  ← 🔴 新增抑制
f=552  raw=不抑制            confirmed=Airborne(兩腳)  ← 🔴 新增抑制
f=553  raw=不抑制            confirmed=Airborne(兩腳)  ← 🔴 新增抑制
f=558  raw=不抑制            confirmed=Airborne(兩腳)  ← 🔴 新增抑制（角色已經 raw grounded=True 在落地安定）
f=559  raw=不抑制            confirmed=Airborne(兩腳)  ← 🔴 新增抑制
```

**救回 5 帧、新增抑制 6 帧**——淨值是**負的**。而且新增抑制的 6 帧全部發生在 `raw grounded=True`
的瞬間（角色的物理碰撞本身已經確認觸地），只是「確認」還沒追上——這代表**提案會在角色已經真的
踩到東西的那一刻，因為去抖動還沒反應過來而硬生生把那一步吃掉**，尤其 f558／f559 正好卡在整段
下坡即將結束、動作即將恢復正常的**落地安定尾段**，等於延後了下坡結束後第一步正常腳步聲恢復的
時機。這跟「多救回幾步」的原始期待方向相反。

### 8.4 Fired／存活腳步的誠實界限

`FootPlantTracker.Advance()` 的 `Fired` 判定還需要腳的真實世界位置（`footBottomWorldPosition`）
與地面高度（`groundY`）算出的 `clearance`／`verticalSpeed`／`horizontalSpeed`，這些**逐帧原始值
沒有被記錄**（`[FootstepTrace]` 只在「有事件或抑制原因改變」時才寫一筆，救回的 f509/511/513-515
與新增抑制的 f523/536/552-553/558-559 都不在那些「有寫」的帧上）——**無法從現有 log 精確重建
Advance() 在這些特定帧會不會真的判定為 Fired**，這是本輪誠實的資料缺口，不假裝算得出來。

但 §8.2／§8.3 的**抑制條件本身**是用正式程式碼逐帧算出來的，不需要 `Advance()` 的內部細節就能
下結論：**總抑制時長幾乎不變（39→40 帧），且新增的抑制精準落在「角色已經物理接地」的帧上**——
不管 `Advance()` 在那些帧會不會真的擊發，用一個**會在已確認接地的瞬間仍然抑制**的訊號取代
現在的訊號，**沒有理由預期腳步聲整體表現會變好，反而多了一種新的失真模式（延遲恢復）**。

### 8.5 驗收結論：**REJECTED**，提案不通過

使用者定的驗收標準是「`IsGroundedConfirmed` 是否讓 `FootstepDetector` 觀察得到真實踏階，同時
仍抑制真正的 Jump／墜落」——本節用真實程式碼逐帧驗證後：**否**。「原始閃爍收斂成更少次確認
轉換」這句話本身沒錯（§7.2 的兩次轉換確實發生），但**轉換次數變少不等於抑制總量變少，也不等於
抑制的時機變好**——本節的資料顯示抑制總量幾乎不變，且新增的抑制剛好蓋在最不該蓋的地方（角色
剛物理接地的那幾帧）。§7.3 的提案**不建議採用**，`IsGroundedConfirmed` 不進黑板。

### 8.6 第三個訊號：MotionDriver 現有的 movement/collision 結果裡，有沒有「可走表面支撐」語意？

使用者要求檢查：raw contact／debounced contact 之外，是否已經有「實際可站立表面支撐」這個第三
種語意，且**不透過新增計時器**就能從既有 movement/collision 結果推導。逐一檢查：

- **`characterController.Move()` 的回傳值 `CollisionFlags`**（`Above`／`Below`／`Sides`）
  ——Unity 每次 `Move()` 呼叫都會**免費**算出這個值（不需要額外物理查詢）。但
  `ExecuteBaseMovement`（`MotionDriver.cs:558`，一般 locomotion 用的那條路徑）目前**呼叫
  `characterController.Move(...)` 卻完全沒有接收回傳值**——這個資料存在、每帧都算了，只是被
  直接丟掉。**這是本節找到的唯一一個「已經算好、零額外查詢成本、但目前沒有被讀取」的候選**。
  ⚠️ **本輪未驗證它在這個場景下的實際行為**——這次測試沒有記錄 `CollisionFlags`，不知道它在
  Run Down 這種快速下坡場景裡會不會比 `IsGrounded` 更穩定／更早正確反映「還踩在 ramp 上」，
  這需要**另一輪量測**（加一條新的純觀測 log 欄位捕捉它，重跑一次），不是本節能直接下結論的。
- **`StepDownAssist` prototype 的 `slopeOk` 查詢**（`MotionDriver.cs:648`，比對命中面法線與
  `characterController.slopeLimit`）——這確實是「可走表面支撐」的明確語意，但它**不是免費的**：
  只在 `enableStepDownAssistPrototype`（目前關閉）開啟、且三個 Gate 都通過時才執行一次額外的
  `Physics.CapsuleCastNonAlloc`，不是每帧都跑、也不是通用信號，不符合「不加新查詢／新計時器」
  的要求。
- **`FootIKGroundData`（`ground.Left/Right.HasGround`／`GroundY`）**——這是**逐腳**的地面觀測
  （Raycast 命中），語意上最接近「這隻腳下有沒有東西撐著」，但它是 Foot IK 的既有查詢结果
  （`FootIKController.RaycastGround`／`settings.GroundLayers`），**與角色整體是否有效站立是
  兩件事**（一腳有地面觀測不代表角色整體被判定 Grounded，反之亦然——本輪 §6.3／§8.2 的資料
  多次出現兩者不同步的帧）。它已經被 `FootstepDetector` 拿來當第二道閘門用，不是新資源，但也
  不是「角色整體支撐」的答案，只是「這隻腳下方碰到了什麼」。

**結論：目前 `MotionDriver` 沒有一個現成、每帧都算、且明確代表「角色整體正站在合法可走表面上」
的訊號**。最接近、且真正零額外成本的候選是 `ExecuteBaseMovement` 裡被丟棄的 `CollisionFlags`
回傳值——但它目前**沒有被捕捉，也沒有被驗證過在這個場景下有沒有用**，不能跳過量測直接拿來
提案。⛔ **本節不提案新增 `HasGroundSupport` 欄位**，只是把候選與其成本／未知數列清楚，是否
值得開下一輪量測（加一條讀 `CollisionFlags` 的純觀測 log，重跑 Run Down）留待使用者裁決。

### 8.7 目前狀態小結

- `IsGroundedConfirmed`（§7 提案）：**REJECTED**，已用真實程式碼逐帧驗證，不採用。
- `StateType.Jump`：§7.2 已否決（架構違規＋實測更不敏感），本節結論不變。
- `JustLanded`／`JustLeftGround` 重組：§7.2 已否決（邊沿不是狀態），本節結論不變。
- 「可走表面支撐」第三語意：**尚未有答案**，`CollisionFlags` 是唯一零成本候選但未驗證，需要
  另一輪量測才能判斷是否值得繼續往這個方向走。
- ⛔ 本節全程只讀不改，未新增任何欄位、未修改任何判定邏輯／grace／IK／Pelvis／ramp。下樓腳步聲
  吃案問題**目前沒有已驗證可用的修法**，回到研究階段，不進實作。

---

## 9. `CollisionFlags` 量測——第一輪資料**測錯地點**，已知結論僅供參考，需要補測（2026-09-24）

### 9.1 加的量測（`MotionDriver.cs`，`unity cmd recompile` 驗證 0 error）

`ExecuteBaseMovement` 原本呼叫 `characterController.Move(...)` 完全不接收回傳值；本輪只是接住這個
**既有、零額外查詢成本**的 `CollisionFlags`，寫進既有的 `enableStepUpDiagnosticsPrototype` log
（`[StepUpDiag]` 新增 `collisionFlags`／`collisionBelow` 兩欄）。**不消費它做任何判定，不寫黑板，
不影響行為**——production build 分支仍是原本那行 `characterController.Move(...)`（不接回傳值）。

### 9.2 🔴 資料地點核對：這次測的不是 ramp prototype 那道樓梯

用 `unity cmd eval_file` 查詢場景內所有 `Stairs`／`Ramp` 物件的即時座標，比對這次量到的角色軌跡
（x≈−12~−22, z≈0.75~4.17），對上的是 **`Stairs (1)`**（position (−19.93, 1.87, 3.91)，
**layer=Default，一般未改造的樓梯**），不是 `Stairs_650_400_300_Prefab`／`RampProto`
（x≈5~12, z≈9~12，layer=TreadIKOnly／RampProto）。**這次的 Walk Down／Up 是在另一道完全沒套用
ramp prototype 的樓梯上測的**，不是本輪一直在驗證的那個 ramp。

另外：`[State]` log 這次**只有 Idle↔Move 的 6 次切換，一次 Falling／Jump 都沒有**——代表這次測試
**沒有捕捉到主動跳躍，也沒有捕捉到平台邊緣墜落**（兩者都要求進入 `JumpState`，本節全程沒進去）；
且三段 Down/Up pass 的水平速度全部落在 1.3–2.6 m/s（Walk 檔），**沒有 Run/Sprint 速度的下樓資料**。

### 9.3 已經量到的部分（誠實列出，供參考，不是這輪要回答的問題的答案）

> ⚠️ **已被 §9.6 推翻**：下表的 `G=F,B=T`／`G=T,B=F` 兩欄不是兩個訊號的真實分歧，是**量測
> 時差**——log 裡的 `isGrounded` 欄位是 `data.IsGrounded`（晚一帧發布），`collisionBelow` 是本帧
> `Move()` 的結果。兩欄數量永遠成對相等，就是這個時差的指紋。保留原表只為留下「怎麼看錯的」。

**平地 Sprint 對照組**（frame 191–357，167 帧，4.5–5.7 m/s）：`isGrounded` 與 `collisionBelow`
**逐帧 100% 一致**（167/167 都是 `G=T,B=T`）——平地高速移動時兩個訊號完全不衝突。

**`Stairs (1)`（未改造樓梯）Walk Down**（3 段，208／236／269 帧）：

| Pass | G=T,B=T | G=T,B=F | **G=F,B=T**（使用者要問的關鍵案例） | G=F,B=F |
|---|---|---|---|---|
| pass1 | 133 | 14 | **15** | 46 |
| pass3 | 159 | 15 | **15** | 47 |
| pass5 | 201 | 13 | **13** | 42 |

在 `isGrounded=False` 的帧裡（61／62／55 帧），只有約 **25%**（15/61、15/62、13/55）同時
`collisionBelow=True`；剩下 75% 兩個訊號**一致**回報沒有地面接觸。且同時存在數量相近的
`G=T,B=F`（14／15／13）——代表這不是「`CollisionFlags` 系統性比 `isGrounded` 穩定」的單向關係，
是雙向都有噪音，量級接近。**但這是在未改造樓梯、Walk 速度下量到的，不能直接套用到 ramp／Run
的問題上**——這正是本節存在的意義：先把「測錯地點」這件事講清楚，不要用這批數字回答使用者
問的問題。

### 9.4 缺的部分，需要補測

- **Ramp Walk Down／Run Down**（在 `Stairs_650_400_300_RampProto`，即先前所有 §4–§8 使用的那道
  樓梯，世界座標約 x∈[5,12]、z∈[9,12]）——**本輪完全沒有這個地點的資料**。
- **真的主動跳躍**——本輪 `[State]` log 沒有任何 Jump 進場紀錄，需要確認有沒有按到跳躍鍵、
  跳躍是否成立。
- **真的平台邊緣墜落**——同上，沒有任何 Falling 進場紀錄。
- **Run 速度的下樓**——本輪三段 Down pass 最高只到 2.6 m/s（Walk 檔），沒有 Sprint 資料。

⛔ **本節不下任何關於「CollisionFlags 是否解決 Run Down 吃案」的結論**——現有資料的地點與速度
都對不上使用者要問的場景，勉強套用只會產生誤導性的答案。下一步：在正確地點（ramp prototype
那道樓梯）重測 Walk Down／Run Down／主動跳躍／平台邊緣墜落，`enableStepUpDiagnosticsPrototype`
與新的 `collisionFlags`／`collisionBelow` 欄位已經就緒，不需要再改程式。

### 9.5 第二輪（2026-09-24）：仍未涵蓋 ramp，但補到一個真的平台邊緣墜落

Console 保留區只剩最後 18.4 秒（1394 帧），**ramp footprint 內 0 帧**——若有在 ramp 上測，已被
緩衝區擠掉；留存的仍是 `Stairs (1)`。有用的新資料：

| 片段 | n | G=T,B=T | G=T,B=F | G=F,B=T | G=F,B=F |
|---|---|---|---|---|---|
| 平地 Sprint（f300–380） | 81 | 81 | 0 | 0 | 0 |
| **真的平台邊緣墜落**（f800–880，自 y≈3.46 落到 0.03，`[State]` 有非主動 Falling＋真實落地紀錄） | 77 | 3 | 1 | 1 | **72** |
| `Stairs (1)` 所有 Walk 上下樓 | 769 | 684 | 21 | 20 | 44 |

墜落全程 `collisionBelow=False`（途中擦到樓梯側面時是 `Sides`，不是 `Below`），落地那一帧是
`Below|Sides`——**`CollisionFlags.Below` 在真的空中時正確為 false**，這是它作為候選訊號必須
通過的反例檢查，已通過。主動跳躍仍未捕捉到。

**量測方法修正**：兩輪都被 Console 緩衝區（約 2000 筆）擠掉關鍵片段。改用
`unity cmd console --since <cursor>` 每秒輪詢、全部寫入 scratchpad 的 `follow.jsonl`
（`console_follow.py`），測試期間不再遺失任何一行。
⚠️ 第三輪實測這個輪詢**仍有漏洞**：每次呼叫似乎有回傳筆數上限，且 Claude 會話結束時輪詢程序被
一併終止——合併後仍有 7 段 seq 缺口。下次若要無縫記錄，應改寫檔案 log，而不是依賴 Console。

### 9.6 第三輪（2026-09-24）與結論：**`CollisionFlags` REJECTED——它與 `isGrounded` 是同一個訊號**

第三輪仍然**沒有 ramp 資料**（ramp footprint 0 帧，三輪都在 `Stairs (1)`），但補到了主動跳躍
（`[State]` 有主動 JUMP 進場＋落地）、再一次平台邊緣墜落、以及 `Stairs (1)` 上 Run 速度的下坡。

分段矩陣呈現一個可疑規律：`G=T,B=F` 與 `G=F,B=T` **永遠成對相等**（1/1、3/3、13/13、4/4）。
這是一帧時差的指紋，不是兩個訊號的分歧。直接驗證（第三輪全部 1988 帧 `[StepUpDiag]`）：

| 比對 | 一致帧數 |
|---|---|
| `postGrounded`（`Move()` 之後立刻讀的 `characterController.isGrounded`）vs 同帧 `collisionBelow` | **1988 / 1988** |
| log 的 `isGrounded` 欄位（`data.IsGrounded`）vs **前一帧** `collisionBelow` | **1976 / 1976**（所有相鄰帧對） |

**結論**：`CollisionFlags.Below` 與 `CharacterController.isGrounded` **逐位元相同**——Unity 的
`isGrounded` 就是「上一次 `Move()` 是否碰到下方」，兩者是同一個結果的兩種讀法。先前 §9.3／§9.5
看到的所有「分歧」都是 log 欄位取樣時差造成的假象。

⇒ 使用者問的關鍵母體（ramp Run Down 時 `IsGrounded == false` 但 `CollisionFlags.Below == true`）
**在結構上不可能存在**，不管地點在哪裡、速度多快——**這個結論不需要 ramp 資料就成立**，所以不再
為了它要求第四輪測試。

**判定：`CollisionFlags` REJECTED**，不能當作「可走表面支撐」的第三訊號。

### 9.7 目前狀態

| 候選 | 狀態 | 理由 |
|---|---|---|
| `StateType.Jump` | 否決（§7.2） | 違反依賴方向；`fallEntryGrace` 遇單帧回真即歸零，實測更不敏感 |
| `JustLanded`／`JustLeftGround` 重組 | 否決（§7.2） | 邊沿不是狀態 |
| `IsGroundedConfirmed` | 否決（§8） | 真實程式 replay：抑制總量 39→40 帧，新增抑制落在角色已物理接地的帧 |
| `CollisionFlags.Below` | **否決（本節）** | 與 `isGrounded` 1988/1988 逐位元相同，不是新資訊 |

- `MotionDriver` 現有 movement/collision 結果裡，**沒有**任何一個「零額外查詢、零新計時器」的
  訊號能區分「ramp 上的 isGrounded 短暫掉出」與「真的空中」。
- 若要這個語意，必然要**新增一次查詢**（例如向下的 ground probe／`slopeLimit` 檢查，類似
  `StepDownAssist` 的 Query）——那是新的設計決定，不是重用既有訊號，需另開提案，本節不提。
- 量測程式碼：`MotionDriver.ExecuteBaseMovement` 的 `CollisionFlags` 接收與 log 欄位仍在
  （`#if UNITY_EDITOR || DEVELOPMENT_BUILD`，零行為影響），既然已否決，可以移除。
- 另外記錄：三輪 Play 測試都沒有走到 `Stairs_650_400_300_RampProto`（場景座標約 x 5–12、z 9–12），
  而是 `Stairs (1)`（約 x −23～−17、z 2–5）。**若之後還需要 ramp 上的量測，要先確認實際地點**。
  ⚠️ 使用者 2026-09-24 糾正：**這不是可以忽略的測錯地點**——見 §10。

---

## 10. Generalization：把 `Stairs (1)` 當第二個案例——正式方案需要的 authoring contract（2026-09-24，⛔ 研究＋提案，未實作）

> 使用者裁決：換一道樓梯體感就明顯變差 ⇒ **目前只證明「單一道 ramp prototype」成功，不能說樓梯
> 支援完成**。本節找出 `Stairs (1)` 與成功 prototype 的必要差異，並判斷正式方案需要什麼
> authoring contract／自動化，才能讓其他樓梯得到相同行為。全部資料來自 live Editor 的唯讀查詢
> （`unity cmd eval_file`，`Collider.Raycast` 只打單一 collider）與 §9 三輪 Play log。

### 10.1 必要差異：`Stairs (1)` 沒有套用 prototype 的任何一項改動

| 項目 | `Stairs_650_400_300_Prefab`（成功 prototype） | `Stairs (1)` |
|---|---|---|
| 建構方式 | Starter Assets FBX，`Environment_Prefab` 的 prefab instance（Connected），static | **ProBuilder** 場景物件（`ProBuilderMesh`／`ProBuilderShape`，mesh 不是 asset），非 prefab、非 static、**非等比縮放** (1.526, 1.411, 1.037)。`Stairs`（x≈7.9）是同一個 ProBuilder 形狀 |
| 碰撞體 | 階梯 MeshCollider 移到 `TreadIKOnly` ＋ 另一顆 `RampProto` BoxCollider | **只有一顆階梯 MeshCollider，layer=Default**，CharacterController 與 Foot IK 共用 |
| 級高／踏面深／級數 | 0.195 m／0.343 m／18 | 0.194 m／**0.275 m**／18 |
| 踏面前緣連線（nosing line）坡度 | 29.67° | **35.17°**（仍 < `slopeLimit` 45°） |
| Walk Up：`stepOffsetDelta` 範圍／相鄰帧最大變化 | ramp：0.016–0.067／0.031 | 0.015–0.108／**0.065**（與 §2.2 修前的真實樓梯 0.018–0.121 同級） |
| Walk Down：`isGrounded=false` 帧 | ramp：**0 / 148** | **60/208、62/236、55/269**（26–29%） |
| Walk Down：`|pelvisY|` 最大值 | ramp：0.135 | **0.35**（三段都頂到上限） |

**結論**：`Stairs (1)` 的表現就是修改前的基準線——它的退化**不是新 bug，而是證據**：修好的是
「一個被手動改過的物件」，不是「系統的樓梯行為」。每一道樓梯都要被正確 author 過，否則就回到
修前狀態，而目前沒有任何機制保證或檢查這件事。

### 10.2 prototype 本身也不是可以照抄的規格

- **ramp 是手擺的**：坡度 31.4°，比它自己的 nosing line（29.67°）陡 1.7°；ramp 與踏面的高度差在
  **−0.375 ～ +0.065 m** 之間（大部分在踏面下方，最低處在底端）。這個數字沒有被任何規則約束，
  是「那一次剛好能用」。
- **照抄到 `Stairs (1)` 會錯 0.49 m**（5.21 m 水平長度 × 兩者坡度差）——每道樓梯的幾何不同，ramp
  必須從各自的實際幾何推導。
- **即使在 prototype 上，也只證明了 Walk**：Run Down 仍有 69/83 帧 `isGrounded=false`（§5.3），
  Run Up 骨盆下潛 −0.25 m（§5.2）。分層幾何是**必要條件、不是充分條件**。

### 10.3 🔴 稽核：prototype 把階梯移到新 layer，讓其他系統悄悄看不見這道樓梯

逐一查詢 live 場景中所有有 `LayerMask` 的消費者（Edit mode 讀序列化值，未 Play 驗證）：

| 消費者 | 遮罩 | 看得到 TreadIKOnly | 看得到 RampProto | 後果 |
|---|---|---|---|---|
| X Bot 的 CharacterController（Player） | 碰撞矩陣 | Play 時由 `RampPrototypeLayerSetup.Awake` 關掉 | ✅ | 全域物理設定被**角色元件**在執行期改寫（ownership 錯） |
| **Y Bot 的 CharacterController（Enemy）** | 碰撞矩陣 | ✅（沒人關掉） | ✅ | **敵人同時碰踏面與 ramp** ⇒ 走的是兩者的聯集 = 仍然是階梯；prototype 對敵人無效 |
| Y Bot 的 `FootIKSettings.GroundLayers` | `~0` | ✅ | ✅ | 敵人的腳會踩在**看不見的 ramp** 上 |
| X Bot 的 `FootIKSettings.GroundLayers` | `-9` | ✅ | ❌ | 正確，但只是**場景內未存檔的 Inspector override**，不在 prefab |
| `Main Camera` 的 `ThirdPersonCamera.obstructionMask` | `1`（只有 Default） | ❌ | ❌ | **相機會穿過 prototype 樓梯**（改 layer 之前會擋） |
| X Bot 的 `AimResolver.aimRayMask` | `129` | ❌ | ❌ | **瞄準射線穿過樓梯** |
| X Bot 的 `TraversalProbe` `obstacleMask` | `19` | ❌ | ❌ | traversal 完全看不見這道樓梯（行為改變，未評估） |
| `IceGroundEffect` 的 `GroundEffectSink.groundMask` | `51` | ❌ | ❌ | **地面技能在這道樓梯上找不到地面** |
| `MotionDriver.stepDownQueryLayers`（prototype，關閉中） | `~0` | ✅ | ✅ | 若啟用，會同時打到踏面與 ramp，距離不正確 |
| `ThrownProjectile.sweepMask` | prefab 生成，未稽核 | ? | ? | — |

**教訓**：把一顆既有 collider 換到新 layer，對**每一個明確列出 layer 的遮罩**都是破壞性變更。
prototype 只驗證了主角的移動與 Foot IK，其餘消費者全部沒有被檢查。

### 10.4 提案：authoring contract（全域規則 ＋ 每道樓梯的不變量）

**兩個語意 layer**（名稱待定）：
- **`StairTread`**：原本的階梯碰撞體＝**視覺／接觸真相**。除了角色移動以外，所有原本看得到
  Default 的系統都要看得到它（相機、瞄準、AoE、投射物、traversal、Foot IK、腳步材質）。
- **`CharacterMovementSurface`**：產生出來的 ramp＝**移動真相**。**只有** CharacterController
  碰撞（與移動類查詢，例如 step-down）看得到它；對 IK、相機、瞄準、AoE、traversal、投射物不可見。

**全域規則（放在專案設定與 prefab，不放在執行期元件）**：
- G1 **Layer Collision Matrix**（`ProjectSettings/DynamicsManager.asset`）：**所有角色 layer**
  （Player、Enemy，以及未來的新角色 layer）忽略 `StairTread`、碰撞 `CharacterMovementSurface`。
  刪除 `RampPrototypeLayerSetup`——全域碰撞政策不能由角色元件擁有。
- G2 **遮罩規則**：任何包含 Default 的遮罩也必須包含 `StairTread`；除了移動類查詢以外，任何遮罩
  都不得包含 `CharacterMovementSurface`。
- G3 所有角色 prefab 的 `FootIKSettings.GroundLayers` 在 **prefab 本身**符合 G2，不靠場景 override。

**每道樓梯的不變量（可量測、可自動檢查）**：
- I1 每一段連續的階梯恰好一顆 movement ramp，**作為樓梯根物件的子物件**（移動樓梯時一起移動）。
- I2 ramp 平面由實際幾何推導：**待裁決**——穿過踏面前緣連線（ramp 與踏面高度差 ∈ [0, 一級高]，
  根永遠不低於踏面）或穿過中線（± 半級高）。這個選擇直接決定 Foot IK／骨盆需要補償的範圍，
  且差值必須在 Foot IK 可達範圍內。
- I3 坡度 ≤ `slopeLimit` − 餘裕（`Stairs (1)` 35.2°，距 45° 還有 9.8°）。
- I4 ramp 與上下平台的接縫：垂直落差 ≤ `skinWidth`（0.03 m），不得出現需要 `stepOffset` 才能
  跨過的凸緣。
- I5 ramp 寬度＝踏面寬度。**因為角色忽略踏面，ramp 若比踏面窄，角色從 ramp 側邊走出去會直接
  穿過視覺上的樓梯掉下去**。

### 10.5 自動化：Editor 工具通過兩道閘門

- **Gate A（投資值得）**：成立。手動推導每道樓梯的 ramp 容易出錯（prototype 自己就偏 1.7°、
  最低處在踏面下 0.375 m；照抄到 `Stairs (1)` 會錯 0.49 m），場景目前有 5 個樓梯物件，且關卡
  每次修改都要重做。
- **Gate B（不依賴第三方內部結構）**：成立。只需要公開的 `Collider.Raycast` 在世界座標掃描
  （本節 §10.1 的掃描已驗證可行，FBX 與 ProBuilder 兩種建構方式都能掃），不讀 ProBuilder 或
  FBX 內部資料，也適用非等比縮放。

提議兩件工具，一件產生、一件檢查：
1. **產生器**（Editor 命令）：選取樓梯根物件 → 沿上升軸掃描自己的 collider → 偵測級高、踏面、
   上升方向 → 依 I2 擬合 ramp → 建立／更新子物件 ramp（`CharacterMovementSurface`），階梯
   collider 換到 `StairTread` → 把推導參數（來源 collider、級數、級高、坡度、擬合殘差）寫進一個
   標記元件。
2. **驗證測試**（EditMode，Test-as-Spec）：對場景裡每個標記元件重新掃描，檢查 I1–I5（能抓到
   「樓梯被改了但 ramp 沒重產」）；檢查 G1 碰撞矩陣；檢查 G2／G3 遮罩規則（涵蓋 §10.3 表上所有
   消費者與角色 prefab）。

執行期**不需要新程式**，只需要刪掉 `RampPrototypeLayerSetup`。

### 10.6 參考：不需要逐道 author 的替代方案，以及廠商的既有慣例

- **執行期貼地**（`docs/42` 的 Query→Apply）對任何有階梯碰撞體的樓梯自動生效，不需要 author，
  也不動 layer。但下樓只證明「有意義改善、未完全通過」，上樓的 `stepOffset` 雜訊沒有對應的執行期
  修法。分層幾何能推廣的是**已經被 Play 證明**的 Walk 上下樓；執行期路線能自動推廣，但兩個方向都
  沒有被證明過。不建議當主方案，可以作為補充。
- **Starter Assets 的 `Stairs_200x100x200_Prefab` 已經採用同一個原則**：碰撞 mesh 是獨立的
  `Stairs_200_100_200_COL`，掃描結果是**平滑斜面**（24.5°，無階梯），視覺才是階梯。差別是它只有
  一顆 collider，所以 Foot IK 會踩在斜面上，而不是在踏面上；依本契約，它們也需要拆出踏面。

### 10.7 「樓梯支援完成」實際需要什麼

1. §10.4–§10.5 的契約與自動化，套用到所有樓梯——**未完成**。
2. **至少兩道幾何不同的樓梯**（`Stairs_650_400_300` 與 `Stairs (1)`）通過 Play 驗收——這是推廣性的
   驗收條件，取代「單一 prototype 成功」。
3. Ramp 上 Run Down 的 `isGrounded` 流失（69/83 帧）——**未解**，現有訊號全部否決（§9.7）。
4. Run Up 骨盆／Foot IK 權重缺陷——**未解**（§5.2、§3.3）。
5. Walk 速度的腳步聲同腳連擊（§6.5）——**未解**。

### 10.8 待使用者裁決

- **D1**：ramp 平面規則（I2）——穿過踏面前緣連線，還是穿過中線。
- **D2**：兩個 layer 的名稱，以及是否接受 G1–G3 的專案層改動（`TagManager.asset`、
  `DynamicsManager.asset`、角色 prefab、§10.3 所列遮罩）。
- **D3**：這是跨系統的 physics-layer 契約（相機、瞄準、IK、AoE、traversal、角色移動），但**不命中**
  ADR 四項觸發條件中的任何一項（黑板、FSM 拓撲、管線順序／核心介面、推翻既有不變量）。建議寫成
  獨立的 living spec 分卷，而非 ADR——僅為建議。
- **D4**：順序——先做產生器＋驗證測試，再用它處理 `Stairs (1)` 作為推廣驗證（建議）；還是先手動
  在 `Stairs (1)` 做第二個 prototype。

### 10.9 結果（2026-09-24）

D1–D4 已由使用者裁決（前緣連線＋頂／底平台連續；專案層 layer ＋ 碰撞矩陣；語意逐一決定遮罩；
獨立規格；先做工具再套兩道樓梯），並已實作——正本移到 **`docs/44`**。
兩道樓梯由同一工具產生移動面；Player Walk／Run 上下、Enemy 上下、查詢語意皆由 PlayMode 契約測試驗證
（`docs/44` §9.4）。本節 §10.7 列的 Run Down 失地已由「只看移動面的樓梯貼地」修好；
Run Up 骨盆與 Walk 腳步聲同腳連擊仍未解。
