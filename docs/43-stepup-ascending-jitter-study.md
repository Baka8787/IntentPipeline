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
> ⛔ 尚未提出修法，下一輪再談方向。

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

## 3. 下一步（尚未裁決）

量測完成，**尚未提出修法**（使用者裁決：先確認根因再談怎麼修）。可能的方向留待下一輪討論，
不在本輪展開：
- 是否要在 `stepOffset` 的解算之外，對「本幀究竟要頂多高」做一層自己的平滑（呼應 `docs/42`
  下樓那套 Query→Apply 的精神，但方向相反：上樓已經有 `stepOffset` 保底不掉出去，缺的是平滑）。
- 或者只處理下游（Foot IK／骨盆的權重平滑速率、或雙腳命中門檻的判定方式），不動 root。
- 或者兩者都要——但**這是下一輪的討論**，本輪只交付量測結論。
