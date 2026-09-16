# 21 — Enemy AI v1 Baseline（FSM-lite ＋ IntentPipeline）

> 狀態：2026-09-11 已實作，待 Play 驗證。
> 本檔是 Living Spec，不是 ADR；既有 ownership、pipeline 與 facing authority 都沒有改變。

## 1. 現行方案 A

Enemy AI v1 採分層式 decision／FSM-lite。沒有 `EnemyBrain`、Behavior Tree、Utility runtime 或通用 scheduler。

```text
Awareness / Engagement
        ↓
CombatContext
        ↓
Enemy Decision
  ├─ Tactical Movement
  ├─ Action Decision
  └─ Facing Policy
        ↓
IntentPipeline
        ↓
Movement / Action / Animation
```

### 1.1 Awareness／Engagement

- Owner：`AIMovementSource`。它已持有敵人的唯一 target reference，因此不另建第二份 target 清單。
- `target != null` 只代表知道目標；`CombatContext.InCombat` 由黏性半徑決定。
- first-pass tuning：`aggroEnterRadius = 8m`、`aggroLeaveRadius = 12m`；尚未進場用 enter，已進場用 leave。
- Aggro range 只回答「是否交戰」；`minimumEngagementDistance`／`maximumEngagementDistance` 只回答戰鬥內站位。
- `!InCombat` 時整體清空 `MovementIntent`，敵人保持 Idle。

### 1.2 Tactical Movement

- Owner：`AIMovementSource`；只產生世界空間 `MovementIntent`，不碰 Action lifecycle 或動畫。
- `Approach`：超出近戰帶，沿 NavMesh 查得的路徑方向接近。
- `Strafe`：位於近戰帶內，沿目標切線側移並維持距離。
- `Retreat`：過近時背離目標；這是既有 BackOff 等價行為。
- `Hold`：真正的零移動 fallback，供未交戰或無 target 時使用。
- `Recover`：🆕 **離開 NavMesh 後的自救**（2026-09-15）。
- 距離帶切換保留 hysteresis；steering 可每幀更新，不另建 decision scheduler。

#### 1.2.1 🔴 環境感知必須覆蓋**穩態**模式（2026-09-15，缺陷修正）

**症狀**：2026-09-15 錄影——敵人在兩片牆的縫旁纏鬥後，**連續 11 秒零位移**，
期間玩家跑遠也不追，直到被打死為止。

**根因是兩個形狀相同的洞**：

| # | 洞 | 為什麼會累積成永久卡死 |
|---|---|---|
| 1 | `Strafe` 的方向是 `toTarget` 繞 Y 轉 90° 的**純幾何切線**，完全不諮詢 NavMesh | 而 `Strafe` 是**穩態**（Y Bot 實測側移帶 `[1.25, 1.6]` ＝ 0.35 m 寬，交戰中幾乎全程待在裡面），`Approach` 只是過渡 ⇒ **唯一諮詢環境的是過渡模式，穩態模式不看環境** ⇒ 打得夠久必然把自己側移出網 |
| 2 | 離網後 `_engagementMovement = Hold; return;`（不寫任何 intent） | 全 repo **只有這一處**讀 `isOnNavMesh`，沒有任何 `Warp`／`SamplePosition`／第二個 intent 寫入者 ⇒ **`Hold` 是沒有出口的終態** |

**修正**：

1. **側移前先問環境**：以 `NavMesh.Raycast` 前視 `strafeWalkableProbeDistance`；偏好側不可走 → 換邊
   （並重置 flip 計時，否則剛翻完就被翻回被擋的那側）；**兩側皆不可走 → 停步但不改 `_engagementMovement`**
   ——下一幀仍由距離推導出 `Strafe` 重試，玩家一動就恢復 ⇒ 停步是暫時的，不是終態。
2. **離網進入 `Recover`**：遲滯狀態機**依舊凍結**（維持原設計意圖：離網期間的距離變化不得污染遲滯記憶），
   但輸出「走回最近可導航點」的 intent；取樣失敗時**退化為朝目標**（目標是玩家，依定義站在可走的地方）。
   ⛔ **不用 `NavMeshAgent.Warp()`**——它會直接設 `transform.position`，違反「位移由 `MotionDriver` 獨佔」。
3. **`Approach` 的 `!hasPath`／`PathInvalid` 也退化為朝目標**（`pathPending` 仍等待，那是短暫的）。
   ⚠️ Trade-off：目標真的不可達時敵人會變成「持續推牆」而非「站著不動」——
   推牆是**可見的嘗試**，站著不動看起來就是壞掉。

**測試**：`EnemyEngagementMovementTests`（EM1–EM11）。NavMesh 查詢在 EditMode 不存在，
因此決策邏輯刻意切成純函數（`TryResolveStrafeDirection`／`TryResolveRecoveryDirection`），
由呼叫端餵入查詢結果 ⇒ **決策完全可測，不需要場景**。
`CombatContextTests.TC9` 的舊不變量（離網 ⇒ `Hold`）**已於同一工作包內被新 baseline 取代**。

#### 1.2.2 死亡收手（2026-09-15）

`AIMovementSource` 在 `data.Survivability.IsDead` 時直接零意圖返回。
`DeathState` 是吸收態、本來就不會走路，所以今天看不出差別——但決策層繼續每帧解出
`Approach`／`Strafe`／NavMesh 查詢是**沒有讀者的計算**。
與 `CharacterFacingSource` 的 `DeathSuppressed` 同一個模式：**死亡由各自的消費者明確收手**，
不靠下游剛好擋住。

### 1.3 Action Decision

- Owner：`AIInputSource`。
- `WantsToAttack` 是持續距離條件；`Slot1ButtonDown` 是單幀 request pulse。
- 首次符合條件立即送一次；留在範圍內以 `attackRequestRetryInterval`（預設 0.5 秒）重試；離開後重新武裝。
- retry 只回答「多久再送一次 request」，不是 attack cooldown。
- `ActionState` 仍唯一裁決 cooldown、variance、grounded requirement 與 lifecycle。
- AI 不讀 FSM／`ActionState`。同幀 intent-vs-Reaction 的既有優先序不變；false pulse 幀讓 external Reaction 可被消費。
- Prefab 契約（`PrefabWiringTests.W10` 守）：**`attackRange` 必須等於 `maximumEngagementDistance`**，
  且不得大於 aggro enter radius。

  🔴 **2026-09-14 修正：原本只寫「位於 engagement band 內」，那是不夠的。**
  `maximumEngagementDistance` 是敵人**會停下來不再前進的最遠距離**（`CombatContextTests.TC11`
  以純函數證明它是緊上界）。`attackRange` 若比它小，兩者之間就是一段
  **「站得住但打不到」的死區**——敵人判定距離剛好而停止前進並側移，卻送不出攻擊 request，
  於是在原地繞圈永不出手，而且**沒有任何錯誤訊息**。
  Y Bot 曾是 `attackRange 1.8` vs `maximumEngagementDistance 2.0`，使用者的體感回報是
  「敵人攻擊距離比迂迴距離短，玩家得自己往前靠」。
  反向的 `attackRange > maximum` 則是 2026-09-05 的「還在 Approach 就揮拳」。
  ⇒ **兩個必要條件的交集只有相等**，因此契約是等號，不是區間。

  ⚠️ 相等**不代表**完全沒有重疊：最後 `distanceHysteresis` 那一段（`maximum − deadZone`
  到 `maximum`）仍是 Approach 且已在攻擊圈內，會「邊走邊揮」一小段。
  要消除它就得 `attackRange ≤ maximum − deadZone`，但那又製造死區。
  **兩害相權**：死區是 gameplay 死局，邊走邊揮只是表現瑕疵 ⇒ 容忍後者。

  📌 **數值取自物理觸及範圍，不是手感猜測**：Y Bot 的 `MeleeHitbox` 是 root 空間
  local position `(0, 1, 0.9)`、size `(0.9, 1.2, 1.2)` 的 box ⇒ 前緣在 **z = 1.5 m**；
  玩家 `CharacterController` 半徑 0.12 ⇒ 實際可及約 **1.62 m**。
  故兩個欄位同取 **1.6**（敵人停在 `1.6 − 0.15 = 1.45 m`，仍在觸及範圍內）。

#### 1.3.1 Enemy Punch 的位移執行（2026-09-11）

- AI movement producer 與 `LocomotionModel` 在 Action 期間仍照既有 pipeline 更新 movement output；它們不讀 FSM，
  也不擁有「Punch 要不要移動」的政策。
- `EnemyPunchDefinition.Start.Bake` 指向 `Bake_Fists_Punch_R`。該 Bake 由 `Enemy_Punch_R` transition 所引用的
  同一支 `Fists_Punch_R` FBX sub-clip 以 60 FPS 烘焙；Bake／clip duration 都是 0.8000001 秒，
  水平 speed curve 與 `TargetLocalDirection` 為 0。
- `ActionState.OnUpdateMotion` 因此走既有 `ExecuteBakedCurveMovement` 分支：Punch 期間不消費 blackboard 上的
  AI locomotion 水平輸出；結束後回到 ambient state 才再由 `ExecuteBaseMovement` 消費。
- 這不是 MovementLock 或 AttackMovementPolicy。若未來某個攻擊 clip 明確帶位移，只需讓該 Action phase 引用
  同 clip 的非零 Bake；ownership 與 runtime 分支不變。
- 玩家 `FireballDefinition`／`IceSpellDefinition` 刻意維持 no-Bake，仍走既有 base-movement 行為。

### 1.4 Facing Policy

- Owner：既有 `CharacterFacingSource`，不新增第二個 facing sender。
- Y Bot：`usePersistentCombatFacing = true`；`InCombat && HasTarget` 時走 Priority 2 持續面向 target。
- X Bot：`usePersistentCombatFacing = false`，玩家路徑不變。

### 1.5 Combat Directional Locomotion Presentation

- Y Bot 的 `Move` mapping 使用 actor-specific Cartesian 2D mixer；minimum set 是 Idle＋Forward／Backward／Left／Right。
- 四個移動 child 直接引用 FBX sub-clips：`WalkFwdLoop`、`WalkBwdLoop`、`StrafeLeftLoop`、`StrafeRightLoop`；
  不含 diagonal、Run 或 Crouch。中間方向由相鄰 cardinal samples 自然 blend。
- Mixer child playback speed 全為 1；threshold 直接由各方向 `MotionBakeData.AutoAverageSpeed / maximumSpeedBake`
  推導，不用人工倍率統一方向速度。
- Y-only `CombatDirectionalSpeedProfileSO` 引用同一批 bakes；`LocomotionModel` 只在 `InCombat` 時，依 committed
  actor facing 的 local direction 把實際 `MoveSpeed` 限在對應 bake speed。它不讀 camera，也不做 runtime
  angle-based 1D／2D switch。
- ~~X Bot 不指派 profile~~ → 🔄 **2026-09-15 已推翻，見 §1.5.1**。X Bot 的 `Move` 仍使用原有 1D `Locomotion.asset`，但**已指派自己的 profile**。
- 本輪實測 bake：Forward 1.6443043、Backward 1.6443497、Left 1.6443504、Right 1.6443514 m/s。
  這組 walk clips 的 native speed 幾乎相同；尚未裁決用其他素材製造 strafe／retreat 較慢差異。

#### 1.5.1 🔄 **Invariant change：bake-derived directional speed**（2026-09-15，使用者明確裁決）

> **正式廢除**舊契約「Combat 8-way 所有方向與 gait anchor 等速」。

**舊契約**：2D 環的每個方向都座落在 1D `Locomotion.asset` 的 gait 半徑上（walk 0.35／run 0.75）
⇒ 方向不影響速度，素材速度差由 playback 倍率吸收。
**代價**：X Bot spell mixer 的側移／後退被拉到 **2.07–2.17×**，腳頻明顯過快。

**新契約**（唯一規則，兩個環共用）：

```
clipN_i     = bake_i.speed / maximumSpeed              ← 量測事實
threshold_i = dir_i × min(gaitAnchor, clipN_i × maxPlaybackStretch)
playback_i  = |threshold_i| / clipN_i   ⇒ 恆 ≤ maxPlaybackStretch
```

`maxPlaybackStretch` 是本契約**唯一的設計參數**（X Bot ＝ 1.35，Y Bot ＝ 1 ＝ 不拉伸）；
其餘全部由 bake 推導。⛔ **不得手填方向倍率、不得新增最低方向速度比例或其他手感補償**
——使用者明確裁決先以純推導結果進 Play 驗收，太慢再另開 tuning 決策。

**落地結果**（X Bot spell mixer，max ＝ `Bake_SprintFwdLoop` 6.2614 m/s）：

| 方向 | clip | bake m/s | threshold | ％ of fwd | playback |
|---|---|---:|---:|---:|---:|
| Forward | RunFwdLoop | 3.578 | 0.7500 | 100% | 1.313 |
| Forward Diagonal | RunStrafe*45Loop | 3.622 | 0.7500 | 100% | 1.297 |
| Right | RunRtLoop | 2.265 | 0.4884 | 65.1% | **1.350** |
| Backward / Back Diagonal | RunBwd／RunStrafe*135 | 2.214 | 0.4773 | 63.6% | **1.350** |
| Left | RunLtLoop | 2.165 | 0.4667 | 62.2% | **1.350** |

素材追得上的方向（Forward、Forward Diagonal）**原速度不變**；追不上的自動降速。
Walk 環不受影響（clipN×1.35 ＝ 0.3545 > gait 0.35 ⇒ 預算沒咬到，仍在 0.35／1.33×）。

**連帶的行為改變（已知並接受）**：`ApplyCombatDirectionalSpeedLimit` 掛在 `LocomotionModel` 上、
只看 `InCombat`，**不看現在播的是哪顆 mixer** ⇒ X Bot 在**所有戰鬥中**（不只施法）側移都會變慢。

**程式面**：`CombatDirectionalSpeedProfileSO` 新增 `maxPlaybackStretch` 與四個斜向 bake 欄位。
斜向四格**全填才啟用八向角度插值**；留空沿用原 cardinal L1 模型 ⇒ **Y Bot 逐位元不變**。
⚠️ 有真斜向 clip 時不得用 L1 模型：X Bot 45° clip 實測 3.62 m/s，L1 會推出 0.31 normalized（≈1.96 m/s），
少 45% ⇒ 明顯滑步。

**被取代的舊 baseline 測試**（同一工作包內更新，非暫停）：
`SpellLayeringWiringTests.SL5`（改名為 `…UsesBakeDerivedDirectionalSpeedWithinPlaybackBudget`）、
`LocomotionMixerWiringTests.L6`（改名為 `L6_BothActorsUseBakeDerivedDirectionalSpeedProfiles`）。
新增 `CombatDirectionalSpeedProfileTests` 的八向與 stretch 測項（含 Y Bot 回歸守門）。

## 2. 明確不做

Patrol／search、vision／hearing、threat、Behavior Tree、Utility runtime、GOAP、HTN、squad、attack token、
cover、HP／death 重構、Player 8-way、upper/lower body layering、完整 enemy 8-way／Run ring 與 traversal 都不屬於 v1。

## 3. 未來方案 C：Utility Selector（只留升級路線，不實作）

當候選行為約達 5–6 種且互相競爭、if/else 優先序難維護、不同敵人需要不同偏好，或必須同時權衡
距離／血量／位置／風險時，才提出 Utility 升級。

```text
EnemySensors / CombatContext
        ↓
Utility Selector
        ↓
Candidate Actions
  ├─ Approach / Strafe / BackOff
  ├─ Light / Heavy Attack
  ├─ Spell / Guard / Dodge / Heal
  └─ Wait
        ↓
Selected Intent
        ↓
IntentPipeline
```

Utility 只選擇「現在值得做什麼」。它不得直接改 Transform、播放動畫、執行 NavMesh 位移或擁有
Action lifecycle；執行仍交給既有 pipeline。未來可評估 `InRange × FacingQuality × Opportunity × Readiness`
等 scoring，但 v1 不新增 scorer、curve asset 或 selector abstraction。
