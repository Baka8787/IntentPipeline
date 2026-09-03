# Lock-on（FU-13）— 目標鎖定（規格）

> **狀態**：🔵 **2026-09-02 已裁決——persistent Lock-on 暫不做，本檔整體延後。**
>
> **使用者裁決**：不做持續鎖定狀態。改採 **Action-time soft auto-target**——
> **Action 發動的那一刻**依 camera forward、距離與角度自動選最佳目標，**僅供 facing／targeting 使用**，
> **不改 locomotion、不進入持續鎖定狀態**。⇒ D1 的 (a)/(b) 兩案**都沒有被選**，本檔的 Stage 1 設計（§4）暫不實作。
>
> 📌 **理由**：目前擋住展示的是「三招能不能順暢打起來」，不是鎖定。persistent lock-on 不是展示成立的必要條件。
> 📌 **但自動鎖敵仍在路線上**——若之後 Play 顯示 targeting／facing 明顯難看，先補最小 auto-target；
> 真的需要魂系繞圈走位時才回頭做本檔的 Stage 1／Stage 2。
> 📌 **鎖定鍵已預先裁決**：滑鼠中鍵，Toggle 語意（原建議的 Q 已被 `ActionSlot.Slot2`／Quick Spell 佔用）。
>
> ⚠️ 本檔 §4 之後的內容維持有效，但**在上述裁決被推翻前不得據以派工**。
> **來源**：FU-13。2026-08-31 使用者裁決開包。
> **ADR 路由**：**取決於 D1**——
> **Stage 1（不含 strafe）四條 ADR 判準全部不成立 ⇒ 不開 ADR，走本 Living Doc 分卷**（同 WP1 先例）；
> **Stage 2（含 strafe）判準①③成立 ⇒ 必須開 ADR-005**。
> ⚠️ **Stage 2 不得在 ADR-004 仍是 `Trial` 時進入 Trial**——WP2 紅線⑥「同時出現兩個 Trial ADR」的同一理由：
> 兩個未經驗證的架構基線並存時，任何失敗都無法歸因。

---

## 1. Problem（來自 WP1 的 Play 實證，不是預想）

WP1 交付後實測暴露一個**結構性衝突，且已確認調參無法消除**：

> **前向發動的 Action 動畫，與自由瞄準，互相打架。**

- Throw 動畫沿**身體正前方**丟；投射物卻朝 **AimPoint**（螢幕中心解算）飛。
- WP1 的 D3(c) 讓身體轉向 AimPoint，但為了避免半蹲姿勢的腳被連續微調，必須保留**角度死區**（現值 8°）。
- ⇒ **只要死區 > 0，球就會從手部動作的方向偏出去**（使用者原話：「球的軌跡很歪」）。
- ⇒ 死區設 0 則回到腳被扭。**兩個約束不相容，這是取捨的兩端，不是待調的參數。**

**能真正消除歪度的只有兩條路**：

| 路 | 原理 | 本專案可行性 |
| --- | --- | --- |
| **Lock-on** | 身體恆定面向目標 ⇒ 前向動畫**永遠是對的** | ✅ 本文件 |
| 上身分層／aim IK | 手臂指向瞄準方向，身體不必轉 | ⛔ **在 WORKLOG 明確禁止清單內**（「上身層／aim IK」） |

> 📌 **順帶解掉第二個症狀**：使用者回報「看起來像自動追蹤」。追蹤感的來源同樣是
> 「球離開的方向與身體朝向不一致」。身體面向目標後，soft target 的吸附不再顯得突兀。

### 1.1 為什麼它當初被切出 WP1（該判斷仍然成立）

Lock-on **同時**改動 **Camera ＋ Character Facing ＋ Movement Basis ＋ Action Targeting** 四個系統。
塞進 WP1 會讓該包的架構命題（「相機／瞄準是純 Presentation 關切」）失效。
**現在是把它當成獨立工作包正式開包，不是回頭擴張 WP1。**

---

## 2. 素材盤點（磁碟現況，決定 Stage 2 的可行性）

| 需求 | 磁碟現況 |
| --- | --- |
| Run 側向／後退 strafe | ✅ `MovementAnimsetPro_RunStrafeUpdate.fbx`：`RunLtLoop`／`RunRtLoop`／`RunBwdLoop` |
| Walk 後退 ＋ 起停 | ✅ `MovementAnimsetPro_Additionals.fbx`：`WalkBwdLoop`／`WalkBwdStart`／`WalkBwdStop_LU`／`WalkBwdStop_RU` |
| **Walk 站立左右 strafe** | ❌ **沒有**（只有 Crouch 版的 `Crouch_WalkLt/Rt_new`） |
| 施法動作 | ❌ 全專案零施法素材 |

⇒ **Stage 2 若要完整 2D strafe，Walk 層會缺左右**。可行的降級：strafe 只在 Run 層生效，Walk 保持既有前向 locomotion。
⇒ **不需要為 Stage 1 購買任何資產。**

---

## 3. 🔴 裁決點

### D1 — Stage 1（不含 strafe）還是直接做 Stage 2（含 strafe）

| 選項 | 內容 | 架構重量 |
| --- | --- | --- |
| **(a) ✅ 建議：先做 Stage 1** | 鎖定目標 ＋ 相機取景 ＋ **身體面向目標** ＋ Action 目標改為鎖定目標。**移動維持既有的相機相對 locomotion** | **黑板零改動、Core 零改動、不開 ADR**。四條判準全不成立 |
| (b) 直接做 Stage 2 | 上述 ＋ **strafe 移動**（移動基底改為「玩家→目標」軸、locomotion 換 2D 混合） | **判準①③成立** ⇒ 開 ADR-005 ＋ 逼出 FU-6 ＋ 動 `LocomotionModel`（目前**全工作包禁令**） |

**為什麼建議 (a)**

1. **它就解掉你回報的問題**：「鎖定 → 轉身 → 施放」全程角色是站定的，strafe 不參與。
2. **架構重量差一個數量級**：(a) 全部落在 Presentation，複用 WP1 已有的 `RequestFacing` 與 `AimResolver`，**沒有一行 Core 改動**；(b) 要動黑板語意、`LocomotionModel`（`docs/07` §13.1-R4 已預警它走向 God Class，且列為全工作包禁令）與 FU-6。
3. **ADR-004 還在 `Trial`**：(b) 會製造第二個 Trial 基線。
4. **素材不完整**：Walk 層沒有站立左右 strafe。

> ⚠️ **(a) 的已知代價（必須明說）**：鎖定中**移動**時，身體面向目標但播放的是前向 locomotion ⇒ **會出現橫著走（crab walk）**。
> **緩解**：Stage 1 的 facing request **只在角色未產生移動意圖時送出**（見 §4.3）——移動時退回既有的相機相對轉向，
> 鎖定僅保留「相機取景 ＋ Action 目標」。⇒ **站著打＝正確；邊跑邊鎖＝退化成軟鎖定，不會醜。**
> 這是刻意的降級，不是遺漏。若展示需要繞圈走位，那就是 Stage 2 的觸發條件。

### D2 — 鎖定的輸入語意

| 選項 | 評估 |
| --- | --- |
| **(a) ✅ 建議：Toggle（按一下鎖／再按一下解）** | 魂類慣例；不佔用持續按鍵，可與瞄準鍵並存 |
| (b) Hold | 與既有瞄準鍵（RMB 按住）在手感上重疊，且長時間按住不利展示 |

建議綁**滑鼠中鍵**或 `Q`（不與既有 Alt／Esc／RMB 衝突）。

### D3 — 目標選擇沿用既有機制還是新建

**✅ 建議：完全沿用 `AimResolver` 既有的 soft target 機制。**

`AimResolver` 已經在做「沿相機射線 cast → 過濾 `ActionRequestTarget` → 排除自己 → 取角度偏差最小者」。
**Lock-on ＝ 把那個結果「留住」，不是新的目標系統。**

⛔ **不得**新建 `ITargetable`、目標列表、註冊表或 targeting service（WORKLOG 禁止清單既有條目）。
⛔ **Stage 1 不做 target switching**（那是 FU-13 的 Stage 2 範圍，且需要目標列表 ⇒ 撞既有禁令）。

---

## 4. Stage 1 設計（D1 選 (a) 時的實作定形）

### 4.1 職責切分（每個元件只擁有一件事）

| 元件 | 擁有 | **不**擁有 |
| --- | --- | --- |
| 🆕 `LockOnController` | **鎖定目標的取得與保留**（誰是目標、何時解除） | 相機怎麼擺、身體怎麼轉、投擲怎麼發 |
| `AimResolver`（既有，小改） | AimPoint 解算 ＋ **facing request 的唯一送出者** | 目標保留策略 |
| `ThirdPersonCamera`（既有，小改） | 相機位置與旋轉 | 目標是誰 |
| `MotionDriver`（既有，**零改動**） | 角色 `transform.rotation` 的唯一寫入者 | — |

### 4.2 為什麼**仍然不需要黑板**

Stage 1 的目標消費者是：相機（Presentation）、`AimResolver`（Presentation）、`MotionDriver`（Presentation，且經由既有的 `RequestFacing` 同層呼叫）。
**沒有任何 Core 層消費者** ⇒ 與 WP1 §5.2 完全同一論證 ⇒ **黑板 schema 零改動，A5 `WriterRules` 零改動。**

> 🚩 **Trip-wire（撞到即停，改走 ADR）**：
> ① **Core 層**（`StateMachine`／`Movement`／`Pipeline`）需要讀鎖定目標；
> ② **敵人**也需要鎖定（＝不再是玩家專屬的表現層關切）；
> ③ 需要 **strafe**（移動基底改變）⇒ 這就是 Stage 2。

### 4.3 `RequestFacing` 的**唯一送出者**仍是 `AimResolver`

⚠️ **關鍵設計**：不要讓 `LockOnController` 也去呼叫 `MotionDriver.RequestFacing`——那會變成兩個送出者，
需要優先級表（WP1 §4-D3 已明文禁止「facing 優先級表」）。

**正解**：`AimResolver` 向 `LockOnController` 查詢目標，自己決定要送什麼方向：

```text
AimResolver.Update():
    ① 若 LockOnController 有鎖定目標 → aimDirection = 目標 - 角色位置
       否則若 IsAiming              → aimDirection = AimPoint - 角色位置
       否則                          → 不送 request，直接 return
    ② 🆕 Stage 1 降級（見 D1-(a) 代價）：
       若角色**有移動意圖**（MovementIntent.DesiredSpeedNormalized > 門檻）⇒ **不送 request**
       ⇒ 移動時退回既有的相機相對轉向，避免 crab walk
    ③ aimDirection.y = 0；送 MotionDriver.RequestFacing(aimDirection)
```

⛔ 送出者恆為一個，`MotionDriver` **一行都不用改**（`RequestFacing` 已於 WP1-D3(c) 落地）。

### 4.4 `AimResolver.Resolve()` 的鎖定分支

鎖定時 **AimPoint ＝ 目標 collider 的 `bounds.center`**，**跳過**幾何射線與 soft target cast。
⇒ 投擲必定飛向目標中心；且**每帧的 cast 數由 2 降為 0**（鎖定時反而更省）。

### 4.5 相機：驅動 `_yaw`／`_pitch`，**不新增旋轉權威**

⚠️ **不得**讓相機在鎖定時改用 `LookAt` 或第二套旋轉——那會推翻 WP1-D1 剛建立的單一權威。

**正解**：鎖定時**改由程式驅動既有的 `_yaw`／`_pitch`**，最終仍走同一行 `transform.rotation = Quaternion.Euler(_pitch, _yaw, 0)`：

```text
若鎖定：
    desired = LookRotation(目標關注點 - pivot) 的 euler
    _yaw   = MoveTowardsAngle(_yaw,   desired.y, lockOnCameraTurnSpeed * dt)
    _pitch = Clamp(MoveTowardsAngle(_pitch, desired.x, …), minPitch, maxPitch)
    **忽略滑鼠 delta**（否則玩家輸入與鎖定互相拉扯）
```

- 「目標關注點」建議取 `目標 bounds.center`，**不做**玩家/目標中點取景（那是電影式構圖，在禁止清單）。
- `minPitch`／`maxPitch` **照常夾限**——鎖定不得繞過既有夾限。
- 新增**恰好一個**參數 `lockOnCameraTurnSpeed`。⛔ 不得再加第二個。

### 4.6 目標保留與解除（保持最小）

**解除條件（任一成立）**：
1. 玩家再按一次鎖定鍵（toggle）；
2. 目標的 `ActionRequestTarget` 變為 null／`inactive`／被銷毀；
3. 目標距離 > `lockBreakDistance`。

⛔ **不做**：目標死亡狀態判定（沒有 HP 系統）、自動切換到下一個目標、視線遮蔽判定（那是 obstruction，屬另一量級）。

---

## 5. Non-goals（Stage 1 明確不做）

- ❌ **strafe movement mode**（⇒ Stage 2）
- ❌ **target switching**（需要目標列表 ⇒ 撞既有禁令）
- ❌ shoulder swap、電影式構圖、視線遮蔽解除、自動 reposition
- ❌ 任何黑板欄位增刪改
- ❌ 動 `LocomotionModel`（全工作包禁令）
- ❌ 動 Foot IK（已凍結）
- ❌ 上身層／aim IK（明確禁止清單）
- ❌ 目標血條／UI（除了一個最小的鎖定標記，且**零程式**：一個 world-space UI 物件跟隨目標）

---

## 6. Tests／Play 驗收

### 6.1 EditMode（純函數，不碰 Physics）

| # | 測項 | 斷言 |
| --- | --- | --- |
| L-1 | 目標保留：距離超過 `lockBreakDistance` ⇒ 解除 | 邊界值不震盪 |
| L-2 | 目標保留：目標為 null ⇒ 解除且不丟例外 | — |
| L-3 | facing 來源優先序 | 有鎖定 ⇒ 用目標方向；無鎖定但瞄準 ⇒ 用 AimPoint；兩者皆無 ⇒ **不送 request** |
| L-4 | 移動降級（§4.3-②） | 有移動意圖 ⇒ **不送 request**（這條把 D1-(a) 的刻意降級釘死，防止日後被「順手移除」） |
| L-5 | 相機 yaw 逼近 | `MoveTowardsAngle` 跨越 ±180° 不繞遠路 |

### 6.2 Play 驗收

| # | 項目 | 判準 |
| --- | --- | --- |
| **PL1** 🔴 | **歪度歸零（本包存在的理由）** | 鎖定後投擲，**球從身體正前方離開並命中**。這是 §1 那個結構衝突是否被解掉的唯一判準 |
| PL2 | 鎖定／解除 | 按鍵切換順暢；解除後滑鼠立刻恢復控制，**無殘留拉扯** |
| PL3 | 相機 | 目標保持在畫面內；`minPitch`／`maxPitch` 仍生效；**無抖動、無繞遠路轉向** |
| PL4 | 移動降級 | 鎖定中移動 ⇒ 身體退回相機相對轉向、**不出現橫著走**；停下 ⇒ 重新面向目標 |
| PL5 | 解除條件 | 走遠超過 `lockBreakDistance` ⇒ 自動解除，不卡死 |
| PL6 | 零 GC | Development Build 穩態 `0 B/frame`（鎖定中 cast 數應**低於**未鎖定時） |
| PL7 | 回歸 | WP1 的八步驗收鏈**未鎖定時行為逐字不變** |

---

## 7. 檔案邊界

**新增**：`Assets/Scripts/Presentation/Camera/LockOnController.cs`、`Assets/_Project/Tests/EditMode/LockOnTests.cs`
**修改**：`AimResolver.cs`（鎖定分支 ＋ facing 來源優先序 ＋ 移動降級）、`ThirdPersonCamera.cs`（鎖定時驅動 `_yaw`／`_pitch`）

⛔ **不准動**：`Core/**`（全部）、`MotionDriver.cs`、`Presentation/IK/**`、`Presentation/Animation/**`、`ArchitectureRegressionTests.cs`、任何資產、git。

**使用者側**：`LockOnController` 掛載與綁鍵、`lockBreakDistance`／`lockOnCameraTurnSpeed` 調參、鎖定標記 UI（選配、零程式）。

---

## 8. Stage 2（含 strafe）— **延後，需要 ADR-005**

**觸發條件**：展示需要「鎖定中繞圈走位」而 §4.3 的降級不可接受。

**為什麼它是另一個量級**（開包前必讀）：

1. **移動基底改變**：`MovementIntent.DesiredDirection` 目前是 2D 輸入向量，由 `MotionDriver`／`AIMovementSource` 用**相機** forward／right 轉成世界方向。strafe 要求基底改為「玩家→目標」軸 ⇒ **這正是 FU-6 的正解**（給 `MovementIntent` 座標基底語意，或讓 producer 直接輸出世界方向）。
2. ⇒ **Core 的 movement producer 需要讀鎖定目標** ⇒ 觸發 §4.2 的 trip-wire ① ⇒ **黑板 schema 變更 ⇒ ADR 判準①**。
3. ⇒ 同時觸發**判準③**（核心驅動介面語意變更）。
4. **locomotion 要換 2D 混合** ⇒ 動 `LocomotionModel`，而它是**目前全工作包的禁令**（`docs/07` §13.1-R4）。
5. **素材不完整**：Walk 層沒有站立左右 strafe（§2）⇒ 需降級為「strafe 只在 Run 層」或補素材。

⚠️ **Stage 2 不得在 ADR-004 仍為 `Trial` 時開始**。

---

## 9. Risks

| # | 風險 | 處置 |
| --- | --- | --- |
| R1 | 鎖定中敵人移動 ⇒ 身體連續微調 ⇒ 半蹲姿勢的腳又被扭 | 沿用 WP1 的 `aimFacingAngleDeadzone`（現值 8°）。鎖定時偏差通常小於死區 ⇒ 反而比自由瞄準更穩 |
| R2 | 相機自動轉向與玩家滑鼠互相拉扯 | 鎖定期間**完全忽略**滑鼠 delta（§4.5）。⛔ 不做「輕推可微調」——那是第二個權威 |
| R3 | D1-(a) 的 crab walk | §4.3-② 的移動降級；由 L-4 測項與 PL4 守 |
| R4 | 「鎖定 ＋ 瞄準」同時按下時來源衝突 | §4.3 已定優先序：**鎖定優先**。由 L-3 守 |
| R5 | 鎖定解決了歪度，但**動作仍然慢** | **這是獨立問題**。先試 `Throw_*` TransitionAsset 的 `_Speed`（現值 1，從未調過）＝ CLAUDE.md 動畫升級順序第 2 階。⛔ 不要把它混進本包 |
