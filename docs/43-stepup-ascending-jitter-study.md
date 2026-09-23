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

## 2. 下一步

1. 使用者在 Play Mode 走一輪 Walk 上樓梯（建議關閉 `enableStepDownAssistPrototype`，只開
   `enableStepUpDiagnosticsPrototype`，避免兩組 log 交錯干擾判讀）。
2. 撈 Console log，用 §0 的三個假說對照實測資料，判定 H1／H2／H3。
3. 依判定結果決定修法方向（例如：H1 成立 ⇒ 需要類似 UE `AdjustFloorHeight` 的位置平滑；
   H2 成立 ⇒ 調整 Foot IK 權重平滑速率或骨盆補償邏輯；H3 成立 ⇒ 兩層都要動）——
   **本輪不先猜修法，等資料**。
