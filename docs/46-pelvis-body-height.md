# 46 — Pelvis / Body Height 子系統重整（Phase 1：盤點＋離線設計；Phase 2：Moving runtime 原型）

> **狀態**：Phase 1 📝 研究（2026-09-28）；Phase 2 🧪 Moving runtime 原型 v1（§12）→ v2：回升拆成 StepRecovery／TerrainExitRecovery（§13，使用者 Play：下到平地 OK）→ **v3：停步／站立交給同一個求解器（StopHold → Stand，舊停步鏈旁路；§14，⏳ 待人工 Play；⛔ 未 commit，使用者指示先保留為原型）**。
> ⛔ 未改 stop 規則、⛔ 未碰 Predictive Planned Stop（WIP，暫停）、⛔ 未做 heel-lift runtime、⛔ 未碰 Run 上樓 T3 釋放時機。
> **✅ 正式 commit（2026-10-03，依 runtime responsibility 切分）**：`33da8c4` T3 riser-intrusion rule（§22–§23）→ `1fc8017` 統一骨盆求解器（§6／§12–§14、§19、§27、§33）→ `e5ff3b3` 停步預測快照＋FutureLandingProvider（**ADR-010 🟡 Trial**，最小 schema；§16–§18）→ `6a4017a` X Bot 啟用 → 本檔 → §35 Walk Fix C。
> **只存在於工作樹、不進正式 commit 的原型**：Planned Stop 軌跡／控制（§16.1 裁決後只保留 FutureLandingProvider）、§21 sample-frame freeze（已否決）、THROWAWAY 觀測欄位與 probe（`StopLandingProbe`／`StairFeelProbe`）、離線分析腳本 `docs/artifacts/pelvis-analysis/`、停步預測快照中的 hip path／plant events／`Generation`（ADR-010 D2）。本檔 §16–§26 描述的部分實作細節屬於這些原型。
> **範圍**：只處理「樓梯**移動中**的身體高度」。Stop／Standing 在本檔只做盤點，接回方式見 §8.3。
> **ADR**：不需要——黑板 schema、唯一寫入者（`FootIKController` → `FootIKTargetData.PelvisOffsetY`）、管線順序都不變（CLAUDE.md ADR 判準 ①–④ 皆不成立）。
> **圖解版**：`docs/artifacts/pelvis-body-height.html`（本檔為正本）。
> **相關**：`docs/05` §3.5.4.2–§3.5.4.6（現行各機制）、`docs/45` §8（T3 接觸）、`docs/44` §9（樓梯量測）。

使用者要求（2026-09-28）：自然動畫姿勢優先；只在腳部可達性真的要求時才下降；允許合理的逐步 body bob；不允許明顯 foot reach loss；不允許單帧 snap；Moving／Stop／Standing 用同一套 reach constraint 語意。

---

## 1. 盤點：所有會影響 `PelvisOffsetY` 的來源

**寫入者只有一個**：`FootIKController`（`_targetData.PelvisOffsetY`）。套用在 `FootIKRig.OnAnimatorIK`：`bodyPosition += up × PelvisOffsetY`（`FootIKRig.cs:124`），**第 N 帧算、第 N+1 帧套用**（trace 驗證：`pelvisApplied(N+1) == pelvis(N)`，誤差 5e-5）。
讀者：`FootIKRig`、`MotionDriver` 除錯 log。

| # | 來源 | 位置 | 產生的量 | 生效條件 |
|---|---|---|---|---|
| S1 | 逐腳支撐需求（§3.5.4.2） | `ComputeSupportedPelvisOffset` `FootIKController.cs:1227` | `min_i( HasHit ? stance_i·Clamp(GroundY_i−rootY, −0.35, 0) : 0 )` | `ikAllowed`；root **不在**代理面上（平地／斜坡）時才是最終值 |
| S2 | Terrain Envelope `Kref`（§3.5.4.3） | `ComputeReferenceOffset` `:1138`；`CombinePelvisTarget` `:1211` | 9 條射線、8 方向取樣，`min_k Clamp(h_k−rootY, −0.35, 0)` | `ReferenceSampleRadius>0`（X Bot 0.45、Y Bot 0）；root 在代理面上 ⇒ 取代 S1 |
| S3 | 全域夾限 | 同上兩處 | `[−MaxPelvisOffset(0.35), 0]`；`!ikAllowed` ⇒ 目標 0 | 永遠 |
| S4 | Moving 平滑 | `:247`（舊路徑）、`StandingPostureSolver.cs:661`（`Moving`） | `MoveTowards(E, 5 m/s)` | 原型關閉（Y Bot）或 `PosturePhase.Moving` |
| S5 | Standing Posture 原型（§3.5.4.6） | `ResolveStandingPosture` `:593`；`StandingPostureSolver.Step` `:553` | 6 個相位各自一套高度規則（§2） | `UseContactPrototype ∧ UseStandingPosturePrototype`（X Bot 開） |
| S5a | `R`（偏好站姿） | `:634` | `min_i LegBound(hipNext_i, target_i, 0.995·L)`，**兩腳都算、不看狀態** | S5 內 |
| S5b | `C`（硬可達上限） | `:637` | `min_i clamp01(1.25·stance_i) × min(0, LegBound(…, L))` ——**以權重乘上可達上限** | S5 內 |
| S5c | `U`（直立上限） | `UprightBound` `StandingPostureSolver.cs:507` | `min(max(E, min(R,0)), C)` | S5 內 |
| S6 | T3 接觸（`docs/45` §8） | `FootContactSolver` | **不寫骨盆**，但決定腳目標（所有可達上限的輸入）與 Locked／Reliable 邊沿（S5 所有相位轉移的觸發） | `UseContactPrototype` |
| S7 | Predictive Planned Stop（⚠️ 工作樹 WIP，未 commit、未寫進 WORKLOG） | `UpdatePlannedStop` `:694`；`StepPlannedStop` `StandingPostureSolver.cs:939` | 停步 clip 預測軌跡；`≥E`；`SaturateBelow(max(C,E))`；有限退讓 | `LocomotionStopPrediction.Active`（Codex 報告：7/8 正式停步 build 失敗） |
| S8 | 失效重設 | `:605` | `!ikAllowed`／腿長無效／瞬移 ⇒ `ResetPosture` ＋ `MoveTowards(E)` | 事件 |
| S9 | root／膠囊 | `CharacterController` | 樓梯上 root 比移動面高 8–23 cm ⇒ `Kref` 常被 −0.35 夾住（§3.5.4.3） | 樓梯 |

## 2. 各機制何時生效

| 相位（S5） | 觸發 | 高度規則 | 可達保護 |
|---|---|---|---|
| **Moving** | 預設；Return 貼近 E 且近乎靜止 | `MoveTowards(E, 5 m/s)` | ⛔ **無**——移動中沒有任何 reach 檢查 |
| **EarlyRecovery** | 移動意圖 >0→0 邊沿 ∧ root ≥ 0.1 m/s | `EarlyBase`（續追 E）＋ 抬升彈簧 ω6 追 `w·max(0,U−base)` | 抬升 ≤ `C−base`，頂到以 1 m/s 退讓 |
| **StopProgress** | 無意圖 ∧ 新 reliable 邊沿 ∧ root ≤ 1.35 m/s | 支撐腿長 Hermite（承接速度）反解 | primary 全長；尾腳 Locked 時 `SaturateBelow(cap)`；尾腳落定後 `min(,C)` |
| **Upright** | 伸展完成 ∧ 尾腳落定 | U 高於骨盆才以 ω6 上調 | **硬夾** `pelvis = C` |
| **Return** | 意圖恢復／primary 失去可靠／Upright 任一腳不可靠 | 彈簧 ω15 追 E | `≤ max(C,E)`，1 m/s 退讓 |
| **PlannedStop**（WIP） | 預測世代開始 | 預測軌跡 | `max(C,E)` 飽和＋退讓 |

**同一件事有四種定義**：「身體該多高」在 Moving＝E（無可達）、Stop＝支撐腿幾何＋C、Upright＝`min(max(E,R),C)`、Return＝E 但 ≤ `max(C,E)`。這正是使用者要求統一的對象。

## 3. `min`／`max`／`clamp` 與相位交接清單

| 類型 | 位置 | 形式 | 連續性 |
|---|---|---|---|
| clamp | S1、S2 | `Clamp(·, −0.35, 0)` | 值連續；飽和時斜率斷 |
| 硬切 | `pelvisTarget = ikAllowed ? … : 0` | 三元回預設值 | ⚠️ snap-to-default 類（2026-09-08 已知、probe 未觸發，刻意未修） |
| min（argmin 換邊） | S1 `min(left,right)`；S2 `min_k`；S5a／S5b `min_i` | min | 值連續、斜率折點 |
| 權重 × 上限 | S5b `clamp01(1.25·stance) × bound` | 權重乘在可達量上 | stance 連續 ⇒ 值連續；但**語意**＝「半支撐的腳只要一半的可達」 |
| min/max 組合 | `UprightBound = min(max(E, min(R,0)), C)` | 三層 | 連續 |
| 硬夾 | Upright `pelvis = C`（`:705`） | 位置直接覆寫 | ⚠️ C 單帧下降多少就跳多少 |
| 有限退讓夾 | Return `max(limit, min(p, start−1·dt))`（`:732`）；EarlyRecovery（`:794`）；PlannedStop（`:965`） | 速度上限 1 m/s | 無 snap，但允許暫時搆不到 |
| 飽和 | `SaturateBelow`（`:500`） | 單邊 C1 | 連續 |
| 相位交接 | 6 相位 × 8 條轉移（`:586–645`、`:677`、`:742`） | Moving 用 MoveTowards（無速度狀態），其餘用彈簧（有速度） | Moving↔其他：速度狀態被丟棄／重設（`state.Velocity = 0`） |

## 4. 哪些腳狀態真的形成 reach constraint（量測）

資料：`StairFeelProbe` 四組（proto／s1 樓梯 × Walk／Run），姿勢原型**關閉**的 `*_t3.csv`（骨盆＝純包絡）。每隻腳每帧算「全腿長可達上限」`B = LegBound(hipNext, T3 目標, L)`（負值＝骨盆至少要下降這麼多），依 T3 相位分類。「landing」＝未鎖、0.15 s 內會鎖（事後標記，只用於分類）。

| 狀態 | 上樓：上限 p50（cm） | 上樓：成為最緊約束的帧 | 下樓：上限 p50 | 下樓：最緊約束的帧 |
|---|---|---|---|---|
| **Locked** | −16 ～ −22 | 37–47% | −6 ～ +11 | 4–34% |
| **Releasing** | **−30 ～ −72** | 24–44% | +21 ～ +28 | 0% |
| **Unlocked landing** | +2 ～ +16 | ≤1% | **−8 ～ −26** | **22–38%** |
| Unlocked swing | −0 ～ +7 | 12–34% | −7 ～ +43 | 20–46% |

- **上樓**的深度需求來自**後腳**：Locked（仍在動畫支撐期：stance＝1、在髖後 0.2–0.3 m、β≈0.9）與 Releasing（T3 仍把目標留在鎖定點）。
- **下樓**的深度需求來自**前腳落地**（下一階）。
- 只看 Locked 腳：一半的帧**完全不需要下沉**（需求中位數 0），p05 −15 ～ −47 cm ⇒ 需求是**逐步脈衝**，不是持續的。
- 現行包絡（−32 ～ −35 cm）≈「所有腳（含 swing／releasing）」需求的 p05 ⇒ 包絡等於一直維持每步最壞時刻。

**真正應該約束骨盆的狀態**：

| 角色 | 定義（提案） | 約束 |
|---|---|---|
| **Support** | T3 `Locked` 且動畫仍在支撐期 | 硬約束。後腳（在髖後）以**前掌為支點、允許腳跟抬起**的最寬鬆放置計算（§6.2）；前腳以腳踝計算 |
| **Landing** | T3 `Unlocked` 且目標腳底正在下降（ḣ ≤ 0） | 預告式約束：`B + κ·h`，h＝目標腳底離其地面高度 ⇒ 接觸時精確、之前連續放寬 |

## 5. 哪些只是 swing／transient，不應直接拉動骨盆

| 狀態 | 為什麼不算 | 代價（誠實記錄） |
|---|---|---|
| **Releasing** | 腳正在離地；T3 把目標留在鎖定點是為了防滑，不是要求身體等它。上樓時它要求 −30 ～ −72 cm——任何骨盆都不該跟 | 搆不到＝腳尖離地提早，屬離地過程 |
| **Unlocked 上升／頂點** | 在空中；搆不到＝擺盪腳略短，不是接觸瑕疵 | 無接觸後果 |
| **擺盪射線跨階跳動** | 目標高度換階是暫態；只透過 Landing 的 `κ·h` 連續進入 | — |
| **Locked 但需求超出預算**（Run 上樓） | 後腳 Locked 期間上限以 3–5 m/s 下墜到 −41 cm，超過 −35 cm 預算；即使完美預知（oracle）仍有 3.8% 帧搆不到 | ⚠️ **需求端問題**（T3 樓梯上的釋放時機），骨盆解不了；見 §8.2 |

移除上限只會「放鬆」骨盆（不會迫使下跳）；**只有加入上限可能迫使下降**——所以 Support 的加入點（落地）由 Landing 的連續預告負責。這是把連續性交給構造，而不是加 fade／gate。

## 6. 統一模型

```
Pelvis Intent  I(t)   動畫／狀態要的身體高度。Moving：I = 0（動畫名目高度）。
                      Stop／Standing 之後只換 I（§8.3），不另立高度規則。
Reach Constraints     每隻腳依角色提供上限（§4）：
                        Support  U = max_{允許放置} LegBound(hipNext, 放置, L)   硬上限 U_hard 用 1.05·L（Leg Stretch）
                        Landing  U = LegBound(hipNext, 目標, L) + κ·h
                        其他      無
Pelvis Solver         g = clamp( min(I, min_k U_k), −MaxPelvisOffset, 0 )
                      非對稱臨界阻尼：g < p ⇒ ω_down（跟上約束）；g > p ⇒ ω_up（回升慢 ⇒ bob 預算）
                      積分後 p ≤ U_hard（位置夾、向上速度歸零）
Terrain Envelope      不是目標。保留為：①約束事實無效時（IK 不允許、腿長無效）的後備；②診斷參照。
```

### 6.1 為什麼非對稱
量測顯示可達需求在每一步內變化約 20 cm。對稱求解器（B／C）每步都回到名目高度再降下 ⇒ 每步起伏 20–25 cm。**回升速率 `ω_up` 就是「允許多少逐步 bob」的旋鈕**（§7.3 曲線）。

### 6.2 為什麼後腳要用「前掌支點」算可達
上樓時綁住高度的是仍在動畫支撐期的後腳（§4）。以腳踝算，它必須拉直到 Leg Stretch 之外；人上樓時後腳是腳跟先抬、以前掌為支點。模型：前掌在腳踝前 0.12 m，允許抬跟 φ ≤ 35°，允許量隨「腳在髖後多遠」以 smoothstep(0, 0.25 m) 增加。
⚠️ **這是 Contact 層的行為**：骨盆只能用 Contact 層**真的會做到**的放置來算約束。T3 目前不會抬跟 ⇒ 要嘛 T3 一起改，要嘛採用 §7 的 `R-noHL` 數字。

### 6.3 參數（離線值，未調整）
`ω_down 12`、`ω_up 1.5–3`、`κ 1.5`、`φmax 35°`、前掌 0.12 m、`U_hard` 1.05·L、下限 −0.35 m。

## 7. 離線比較

### 7.1 方法
- 模擬器（用完即丟，存查於 `docs/artifacts/pelvis_model_sim.py.txt`）：時序與 runtime 相同——第 i 帧以 `hipNext = 2·hip(i) − hip(i−1)`（pre-IK UpperLeg，`FootIKPoseData` 已有）決策，第 i+1 帧以**真實** hip(i+1) 評分。T3 目標與骨盆無關（`docs/05` §3.5.4.5 已驗證）⇒ 所有模型面對同一組腳目標。
- Unity 腿 IK：|髖−目標| ≤ L 精確到位、Leg Stretch 到 1.05·L（2026-09-25 校準）。L＝0.889 m。
- 資料：4 組（2 樓梯 × Walk／Run）× 3 次 probe（`_t3`、`_t3p_ec27`、`_t3p_ec36`）。`t3p` 的包絡以 log 的 E 重算 `MoveTowards(5 m/s)`，steady 帧與記錄**逐位元相同**。
- 每次 probe **整段連續模擬**（只在 probe 瞬移時重設）；停步段跑同一個求解器、不套任何 stop 規則；**只在 steady 移動帧評分**（意圖 >0、Move、在樓梯上、意圖開始 ≥ 0.5 s、排除轉身）。
- 模型：**A** 現行包絡（記錄值）；**B** nominal-first＋最小必要下降（I=0，約束＝Locked＋Landing，對稱 ω12）；**C** support-driven＋trailing cap（I＝支撐腿保持動畫伸展比（J 式），約束＝髖後的 Locked 腳）；**R** 統一模型（§6，`ω_up 3`，硬夾）；**R-noHL** R 但不抬跟；**O′** oracle（完美預知、|p″| ≤ 4 m/s²、含抬跟）——只作為「平順與高度的上限」參照。

指標：平均骨盆深度；Locked 腳膝彎中位數（前腳／後腳分開）；**逐步 bob**（兩次落腳之間、顯示髖高去線性趨勢後的峰對峰，中位數）與 bob21（對 21 帧置中平均偏差 p95）；支撐腳搆不到 >1 cm 的帧比例與最大值；單帧最大骨盆變化；骨盆與顯示髖高的速度／加速度 p95。
**穿模／浮空**：樓梯 steady 帧兩腳 IK 位置權重**恆為 1** ⇒ 穿模完全由 T3 目標決定、**與骨盆模型無關**；唯一受骨盆影響的是「搆不到＝浮空」（下表 float）。

### 7.2 結果（pooled；Walk 上 19 步、下 12 步；Run 上 25 步、Run 下 **0 個完整步**）

| | 深度 cm | 膝 前／後 | bob 中位 cm | bob21 cm | 搆不到 >1cm | 單帧 cm | 骨盆 a95 | 髖 a95 m/s² |
|---|---|---|---|---|---|---|---|---|
| **Walk 上** A 包絡 | −32.2 | 104°／37° | 6.8 | 5.6 | **3.8%**（5.7） | 4.3 | 7 | 15.5 |
| B | −16.5 | 76°／0° | **23.0** | 12.9 | 3.1% | 4.4 | 49 | 57 |
| C | −16.8 | 77°／0° | **22.3** | 12.8 | 3.1% | 4.4 | 49 | 57 |
| **R** | **−24.7** | 93°／0° | **4.4** | 5.2 | **0%**（抬跟 32%） | 4.2 | 23 | **23.4** |
| R（ω_up 1.5） | −26.9 | 99°／16° | 5.0 | 5.1 | 0% | 3.6 | 19 | 18.7 |
| R-noHL | −26.9 | 103°／0° | 7.7 | 6.8 | 3.1% | 4.2 | 33 | 34.8 |
| O′ oracle | −22.3 | 91°／39° | 8.0 | 4.7 | 0% | 1.8 | 4 | 14.7 |
| **Walk 下** A 包絡 | −30.7 | 69°／104° | 7.5 | 7.0 | 0% | 2.9 | 9 | 26.4 |
| B | −14.7 | 30°／69° | 4.6 | 6.1 | 0% | 5.5 | 24 | 29.2 |
| C | −15.6 | 33°／73° | 3.5 | 6.8 | 0% | 7.5 | 28 | 28.5 |
| **R** | **−24.5** | 47°／88° | **4.1** | **2.7** | 0% | **1.0** | **6** | **11.9** |
| R（ω_up 6） | −19.5 | 38°／79° | 3.8 | 2.8 | 0% | 1.3 | 11 | 13.4 |
| O′ oracle | −24.5 | 62°／98° | 7.1 | 4.6 | 0% | 1.5 | 4 | 14.4 |
| **Run 上** A 包絡 | −31.8 | 103°／53° | 12.4 | 6.7 | **15.0%**（17.3） | 8.3 | 90 | 95 |
| B | −18.7 | 47°／0° | 22.7 | 12.8 | 14.4% | 10.2 | 127 | 121 |
| C | −19.5 | 51°／0° | 20.4 | 12.1 | 14.1% | 10.2 | 122 | 113 |
| **R** | **−24.6** | 85°／0° | 11.5 | 6.0 | **3.8%**（11.3） | 9.0 | 92 | 83 |
| O′ oracle | −31.7 | 106°／58° | 11.7 | 7.7 | 3.8%（11.3） | 1.5 | 4 | 31 |
| **Run 下** A 包絡 | −14.1 | 65°／90° | — | 11.1 | 0% | 5.6 | 52 | 79 |
| B | −3.5 | **0°**／51° | — | 12.5 | 0% | 2.2 | 18 | 39 |
| C | −5.8 | **0°**／62° | — | 12.5 | 0% | 1.6 | 11 | 38 |
| **R** | −16.1 | 15°／77° | — | 11.1 | 0% | **0.7** | **1.5** | **36** |

（「R」＝硬夾版；把硬夾改成 2.5 m/s 有限退讓，Walk 完全相同，Run 上則單帧降到 4.2 cm、但搆不到升到 25.6%——**違反「不允許 reach loss」**，不採用。平地：R 比 A 低 1.5–2 cm、髖 a95 10.5→11.3，需人工確認沒有退化。）

### 7.3 取捨曲線（`ω_up` 掃描 vs oracle 加速度上限掃描）

| Walk 上 | 深度 | bob | 髖 a95 | | Walk 下 | 深度 | bob21 | 髖 a95 |
|---|---|---|---|---|---|---|---|---|
| R ω_up 1.5 | −26.9 | 5.0 | 18.7 | | R ω_up 1.5 | −28.5 | 2.8 | 12.9 |
| R ω_up 3 | −24.7 | 4.4 | 23.4 | | R ω_up 3 | −24.5 | 2.7 | 11.9 |
| R ω_up 6 | −19.9 | 11.3 | 29.5 | | R ω_up 6 | −19.5 | 2.8 | 13.4 |
| R ω_up 12 | −13.0 | 17.4 | 51.9 | | R ω_up 12 | −14.2 | 5.0 | 27.0 |
| O′ a=2／4／8／16 | −25.4／−22.3／−19.4／−16.2 | 5.9／8.0／11.6／16.8 | 12.6／14.7／18.7／26.7 | | O′ a=2／4／8／16 | −27.6／−24.5／−21.9／−19.9 | 3.7／4.6／6.4／8.7 | 13.1／14.4／18.8／25.4 |

## 8. 結論與建議

### 8.1 結論
1. **字面的 B 與 C 都不可行**：上樓每步起伏 20–25 cm、骨盆加速度是包絡的 7 倍；下樓前腳落在接近打直的腿上（Run 0°）。與 2026-09-25 NomCap／J 的結論一致——「只在需要時下降」若回升不受限，就是逐腳骨盆。
2. **包絡的深度大部分是「平順的代價」，不是無謂的蹲**：需求每步振盪約 20 cm；完美預知、加速度 ≤ 4 m/s² 的 oracle 也只比包絡高 6–10 cm。
3. **統一模型 R 在 Walk／Run、上／下樓都不輸包絡，而且高 6–8 cm**（Run 下樓除外：深 2 cm，樣本不足）：Walk 下樓每項都更好（髖加速度 26→12、bob21 7→2.7）；Walk 上樓搆不到 3.8%→0%、bob 6.8→4.4；Run 上樓搆不到 15%→3.8%。
   **唯一代價**：Walk 上樓髖加速度 p95 15.5 → 23.4（`ω_up 3`）或 18.7（`ω_up 1.5`）。oracle 在同深度只要 13–15 ⇒ 還有預測空間（下一步：用後腳上限自身的變化率外插）。
4. **抬跟（前掌支點）是需求端最大的單一槓桿**：沒有它，Walk 上樓仍有 3.1% 搆不到、髖加速度 34.8。它屬於 Contact 層，骨盆只能在 Contact 真的會做到時才用它算約束。
5. **Run 上樓剩下的 3.8% 不是骨盆能解的**：oracle 同值。根因是 T3 在樓梯上把後腳鎖太久（上限以 3–5 m/s 下墜）。
6. **穿模與骨盆模型無關**（樓梯上 IK 權重恆為 1）；骨盆只影響「搆不到」。

### 8.2 建議的分工（不改現有擁有權）
- **Contact 層（T3）**：對每隻腳公佈「角色」（Support／Landing／Free）與「允許放置範圍」（含抬跟）。Run 上樓的釋放時機另開 slice。
- **Pelvis 層**：只做 §6 的 `min(I, U)` ＋ 非對稱求解器。刪除 Moving／Return／Upright 各自的高度規則與 `C`／`max(C,E)`／`SaturateBelow` 等私有夾限。
- **Terrain Envelope**：降級為後備＋診斷；⛔ 不當下限（當下限會把包絡深度原樣帶回）。

### 8.3 Stop／Standing 接回方式（本階段不做）
Stop 相位只提供 **Intent**：EarlyRecovery＝I 隨 root 減速升向直立；StopProgress／Upright＝I＝站姿伸展（0.995·L）；Return＝I＝0。約束與求解器共用 ⇒ 「尾腳前瞻上限」自然變成「Locked 尾腳的 Support 約束」，「Final Settle 的 `min(,C)`」變成「兩隻 Support 腳的約束」。

## 9. 裁決紀錄（2026-09-28，使用者）
1. ✅ 採用 §6 作為 **Moving 的 runtime 原型**，先只替換 Moving → §12。
2. ✅ 第一版 `ω_up = 1.5`；其餘沿用離線值。
3. ✅ 第一版用 **`R-noHL`**（T3 尚未支援抬跟，骨盆不得先假設它存在）。**Moving 穩定後再獨立開 T3 heel-lift slice。**
4. ✅ Run 上樓 T3 釋放時機**另開獨立 slice**，不讓骨盆求解器承擔。
5. ✅ Predictive Planned Stop：**保留 WIP diff、暫停修改**。

## 10. 限制
- 離線；未在 Unity 中驗證。抬跟假設 Contact 會真的做到。
- Run 下樓 steady 窗口裡沒有完整的一步（只能用 bob21）⇒ 低信心。
- 顯示髖加速度含動畫本身的上下運動（平地參照：Walk 10、Run 31–39 m/s²）。
- `κ`、`φmax`、前掌距離未調整；支撐約束用全腿長（1.0·L）⇒ 綁住時該腿打直（後腳膝 0°）。runtime 可能改用 0.98–0.995·L 保留一點膝彎（約多降 1–2 cm）。
- 平地也會變（統一模型沒有平地特例）：需回歸確認。

## 11. 重現
`Temp/StairProbe/*_r45_t3*.csv`（Unity 重啟會清空 `Temp/`，已知）＋ `docs/artifacts/pelvis_model_sim.py.txt`（改名 `.py`；需 numpy：`C:\Users\USER\anaconda3\python`）：
`python pelvis_model_sim.py main`（主表）／`sweep`（§7.3）。
Phase 2 runtime：`StairFeelProbe`（`--filter StairFeelProbe`）在旗標開啟時輸出 `*_um.csv`（多 `umRef`／`umLimit`／`umHard`／`umLRole`／`umRRole`／`umClamp`／`terrainE` 欄位）。

## 12. Phase 2：Moving runtime 原型 v1（2026-09-28；X Bot 開啟；⏳ 待人工 Play）

### 12.1 做了什麼
| 檔案 | 內容 |
|---|---|
| `Presentation/IK/MovingPelvisSolver.cs`（新） | 純函數：`MovingPelvisFoot`／`MovingPelvisTuning`／`MovingPelvisState`／`FootLimit`／`Begin`／`Step`。零配置 |
| `FootIKController.cs` | `ResolveUnifiedMoving`：在 ③′ T3 解完兩腳之後組事實、步進求解器，回傳 **Moving 參考骨盆**。事實無效（IK 不允許、腿長無效、瞬移）⇒ 重設並回傳原本的 `pelvisTarget`（Terrain Envelope／逐腳需求；IK 不允許時 0）＝後備 |
| `FootIKSettings.cs` | `UseUnifiedMovingPelvis`（預設 false ⇒ 逐位元不變）＋ `MovingPelvisDropOmega 12`／`MovingPelvisRiseOmega 1.5`／`MovingLandingAnticipation 1.5`／`MovingHardReachRatio 1.05` |
| `X Bot.prefab`（Editor API） | `UseUnifiedMovingPelvis = true`。⚠️ 同一次 SaveAsPrefabAsset 也把原本只存在 C# 預設值的 Standing／Planned 欄位寫進 prefab（值＝預設值，行為不變），並清掉 5 個舊 quintic 孤兒欄位 |
| 測試 | EditMode `MovingPelvisSolverTests` 15 條；PlayMode `Player_UnifiedMoving_SupportFeetReachable_AtAnyFacing`（新）；`Player_BodyReference_IsIndependentOfFacing` 改為同時關掉統一路徑（它本來就註明只測包絡本身，見 §12.4） |

**接線（Stop／Standing 規則不動）**：`posture` 關閉時骨盆 `MoveTowards(Moving 參考, 5 m/s)`；`posture` 開啟時，Moving 參考**取代原本傳給 `StandingPostureSolver` 的 `Envelope` 輸入**。
⇒ Moving 相位、Return 的回歸目標、EarlyRecovery 的 base、`max(C,E)` 裡的 E 都改讀這條路徑；**它們的規則一行都沒改**。
理由：若 stop 仍讀更深的 Terrain E，一放開按鍵骨盆就會從求解器高度往包絡掉（兩套 Moving 定義並存）。

**與離線 R 的一處差異（實作中由單元測試抓到）**：硬夾觸發時，速度改為跟隨**硬上限本身的變化率**，不是「這一帧被夾掉的距離」。
舊規則（離線 R0 同樣如此）會把夾掉的距離當成下行速度，讓彈簧衝過約束，再用慢的 `ω_up` 花數秒爬回——多降的深度沒有任何可達理由。

### 12.2 實作驗證
- **replica 對照**：以 Python 重寫的同一套語意，對 runtime `umRef` 中位誤差 < 0.01 cm、角色一致 96–98% ⇒ runtime 行為就是模型本身。
- **行為不變的部分**：與 baseline 同一 probe 序列，root 差 ≤ 0.01 cm、T3 目標與鎖腳相位逐帧相同（1 次 probe 的單帧 4.9 cm 差在 `ecwarm` 瞬移帧）。
- EditMode **739／738／0 failed／1 skipped**（既有 NavMesh 環境略過）；stair PlayMode **26／26**；完整 PlayMode **118／109／1 failed／8 skipped**（唯一 failed ＝ 既有 `C0_…IncidentalContact`；8 skipped ＝ probe 設計）。改測試前是 116／105／3 failed——多出的 2 條即 §12.4 ⑥。

### 12.3 驗收（runtime；A＝同序列 baseline trace；UM＝本原型；2 樓梯 × 3 probe）

| | 深度 cm | 膝 前／後 | bob 中位 | bob21 | 搆不到 >1cm（最大） | 單帧 cm 骨盆／髖 | 骨盆 v95／a95 | 髖 v95／a95 |
|---|---|---|---|---|---|---|---|---|
| Walk 上 A | −32.2 | 104°／37° | 6.8 | 5.6 | 3.8%（5.7） | 4.3／3.6 | 1.6／7 | 2.1／15.5 |
| Walk 上 **UM** | **−29.5** | 105°／17° | 6.8 | 6.8 | 3.6%（5.7） | 4.4／3.7 | 1.6／**25** | 2.1／**29.9** |
| Walk 下 A | −30.7 | 69°／104° | 7.5 | 7.0 | 0% | 2.9／4.4 | 1.5／9 | 2.1／26.4 |
| Walk 下 **UM** | **−29.9** | 63°／99° | **5.0** | **2.8** | 0% | 8.3＊／5.0 | 0.3／4 | 2.1／**19.3** |
| Run 上 A | −31.8 | 103°／53° | 12.4 | 6.7 | 15.0%（17.3） | 8.3／8.3 | 3.5／90 | 4.2／95 |
| Run 上 **UM** | **−27.3** | 91°／0° | 11.5 | 6.2 | 13.1%（17.3） | 8.3／7.5 | 4.3／93 | 4.2／**81** |
| Run 下 A | −14.8 | 68°／90° | — | 12.4 | 0% | 8.3／11.2 | 2.7／63 | 5.7／74 |
| Run 下 **UM** | **−11.7** | 38°／90° | — | 15.9 | 0% | 8.3／11.2 | 2.1／55 | 5.6／78 |
| 平地（樓梯前）Walk | −2.4 → **−2.8** | | | | 0% | | | 11 → 11 |
| 平地（樓梯前）Run | −1.6 → **−3.2** | | | | 0% | | | |
| 平地（剛下完樓梯）Walk | −2.5 → **−11.3** | | | | 0% | | | |
| 平地（剛下完樓梯）Run | −1.4 → **−9.0** | | | | 0% | | | |

＊Walk 下的 8.3 cm 單帧不在樓梯上：見 §12.4 ③。

### 12.4 診斷
1. **比離線預估的高度增益小很多**（Walk 只高 0.8–2.7 cm）：離線表的 R 是「抬跟＋`ω_up 3`」；v1 是 `R-noHL`＋`ω_up 1.5`，兩個都往深的方向走。
   離線同條件（R-noHL）早就指出**高度增益主要在抬跟**——沒有抬跟，上樓後腳真的需要那個深度。
2. **平地回歸＝下完樓梯後的恢復暫態，不是平地本身**：樓梯前的平地幾乎不變（−2.4 → −2.8 cm）；走下最後一階後，
   `ω_up 1.5` 需要約 2 s 才從 −33 cm 爬回來（該段平均 −9 ～ −12 cm）。**`ω_up` 同時扮演「每步 bob 預算」與「約束消失後的恢復速度」兩個角色**——這是 v1 參數的結構性問題。
   包絡沒有這個問題，因為它的 8 條地形射線**看得到前方樓梯結束**；反應式求解器看不到。
3. **Walk 下樓（`quickresume`）的 8.3 cm 單帧**：角色走出 `Stairs (1)` 開放頂端時骨盆仍在 −31 cm（同 ②，還沒恢復），
   一離地 `ikAllowed=false` ⇒ 後備目標 0 ⇒ 以 5 m/s 上限拉回（8.3 cm／帧）。包絡此時早已在 0。根因同 ②。
4. **Run 上樓 7–8 cm／帧**：T3 鎖住的後腳上限以 5 m/s 下墜到 −35 cm 下限；baseline 同樣是 8.3 cm／帧（MoveTowards 上限）。
   ⇒ 已裁決的 **T3 釋放時機 slice**，不由骨盆承擔。
5. **Walk 上樓加速度翻倍**（髖 a95 15.5 → 29.9）：骨盆沿著逐漸遠離的後腳上限被硬夾（速度跟隨上限），尾腳一釋放再慢慢回升。
   與「每步 bob」無關（bob 中位相同），是加速度層級的顛簸——需要 Play 判斷是否看得出來。
6. **`Player_BodyReference_IsIndependentOfFacing`**：它釘的是包絡的朝向無關性（測試註解已寫明「只測包絡本身」）。
   統一路徑下 Moving 骨盆由兩腳落點決定，轉身改變落點 ⇒ 高度**刻意**改變。包絡仍在計算（後備／診斷），性質仍要守
   ⇒ 測試比照既有的「關掉站直原型」寫法同時關掉統一路徑；統一路徑的基線改由新測試（兩隻 Support 腳搆得到）守。

### 12.5 已離線量過、⛔ 未實作的修正候選（❓ 待使用者）
以 replica 在同一批 runtime trace 上量（樓梯指標之外，重點看「剛下完樓梯的平地」）：

| 候選 | Walk 上深度 | Walk 下深度 | Walk 下完樓梯平地 | Run 下完樓梯平地 | 代價 |
|---|---|---|---|---|---|
| v1（`ω_up 1.5`） | −29.5 | −29.8 | −11.3 | −9.3 | — |
| `ω_up 3` | −27.2 | −25.3 | −7.2 | −5.5 | Walk 上骨盆 a95 26 → 34、樓梯前平地後腳 17° → 8° |
| 每步 hold 0.4–0.7 s ＋ `ω_up 4–6` | −30.3 ～ −30.8 | −30.8 ～ −31.5 | −10.6 ～ −11.4 | −10.6 ～ −12.1 | Walk 下單帧 12 cm ⇒ **淘汰** |
| **`ω_up 1.5` ＋ Terrain E 當恢復提示（E 高於骨盆 5 cm 以上時以 ω 6 回升）** | −29.4 | −29.5 | **−5.9** | **−7.4** | 樓梯上指標不變（E 在樓梯上很深、提示不會觸發）；單帧 ≤ 1.2 cm |

E 提示讓包絡扮演使用者指定的「reference」角色：**只影響回升速度，不是目標、也不是上限**。但它是新的設計要素，需要裁決。

### 12.6 ⏳ 人工 Play 驗收項
兩道樓梯 Walk／Run 上下：①上樓身體是否比以前高、沒有變得更「顛」（§12.4 ⑤）；②走下最後一階後是否明顯「蹲著走一段」（§12.4 ②）；
③從 `Stairs (1)` 頂端走出去時的骨盆；④停步／起步（stop 規則讀的是新的 Moving 參考）；⑤平地走跑無變化；⑥Enemy（Y Bot）不受影響（旗標關閉）。

**還原**：X Bot 的 `FootIKController › settings › UseUnifiedMovingPelvis` 取消勾選（或以 Editor API 設 false）⇒ 與原型之前逐位元相同。

## 13. v2：回升拆成兩種語意（2026-09-28；⏳ 待人工 Play；⛔ 未 commit）

### 13.1 使用者裁決
v1 runtime 確認三個問題：①`ω_up 1.5` 同時負責「步態內抑制 bob」與「離開樓梯後恢復站高」⇒ 下完樓梯仍蹲約 2 s；②`R-noHL` 下 Walk 上樓只高 2–3 cm、髖加速度幾乎翻倍，收益太小；
③Stop／Standing 的舊約束仍在 ⇒ 停步後瞬間下蹲沒有解決。
⇒ **不再調同一個 `ω_up`**，先把回升拆成兩種語意；Terrain Envelope ⛔ 不得重新成為目標或硬約束；Unified Moving 保留為原型、不 commit；Heel Lift 與 Stop／Standing 統一分開做，不在同一輪。
本輪只驗證：樓梯 → 平地不再殘留深蹲；樓梯內 bob／reach 不變；沒有新的 entry／exit snap。

### 13.2 語意（`MovingPelvisRecovery`）
| 語意 | 何時 | ω |
|---|---|---|
| `None` | 目標不高於骨盆：跟上約束或已貼住 | `MovingPelvisDropOmega` 12 |
| **`Step`**（StepRecovery） | 步態內約束暫時解除（後腳放開、下一步還沒落地） | `MovingPelvisRiseOmega` 1.5 |
| **`TerrainExit`**（TerrainExitRecovery） | 地形提示比骨盆高 > `MovingTerrainExitMargin` 5 cm 時進入，< `MovingTerrainExitReleaseMargin` 2 cm 時離開（滯後） | `MovingTerrainExitOmega` 6 |

- **地形提示＝原本的 Terrain Envelope**（`ResolveUnifiedMoving` 的 `fallback`，同一個值），傳進 `Step(…, terrainHint, …)`。
- 目標仍然只有 `clamp(min(0, min U_k), −0.35, 0)`：提示**不進目標、不進上限**，只決定上升時用哪個 ω。EditMode 以兩條測試釘住（提示 0 但約束 −0.2 ⇒ 停在 −0.2；提示比骨盆深 ⇒ 仍回到 0）。
- 切換只改變 ω（加速度不連續），位置與速度都連續。
- 新設定 3 個（C# 預設值，prefab 未改）；`StairFeelProbe` 新增 `umRecovery` 欄。

### 13.3 驗收（runtime；同一 probe 序列；A＝包絡 baseline、v1＝§12、v2＝本節）

**樓梯內（steady 移動帧）——應該不變：**

| | 深度 cm | bob 中位 | bob21 | 搆不到 >1cm（最大） | 骨盆單帧 | 骨盆 a95 | 髖 a95 |
|---|---|---|---|---|---|---|---|
| Walk 上 v1 → v2 | −29.5 → −29.4 | 6.8 → 6.8 | 6.8 → 6.3 | 3.6% → 3.6%（5.7） | 4.4 → 4.4 | 25 → 25 | 29.9 → 30.3 |
| Walk 下 v1 → v2 | −29.9 → −29.4 | 5.0 → 5.1 | 2.8 → 2.9 | 0% → 0% | 8.3 → 8.3＊ | 4 → 4 | 19.3 → **22.7** |
| Run 上 v1 → v2 | −27.3 → −27.3 | 11.5 → 11.5 | 6.2 → 6.2 | 13.1% → 13.1% | 8.3 → 8.3 | 93 → 93 | 81 → 81 |
| Run 下 v1 → v2 | −11.7 → −11.5 | — | 15.9 → 14.9 | 0% → 0% | 8.3 → 8.3＊ | 55 → 38 | 78 → 68 |
| 平地（樓梯前） | −2.8 → −2.8（A −2.4） | | | 0% | | | 11 → 11 |

＊＝走出 `Stairs (1)` 開放頂端的離地帧（見 13.4）。TerrainExit 在樓梯 steady 帧上啟動 1.8–2.9%（樓梯兩端，包絡已看到前方結束）；搆不到完全相同 ⇒ 硬可達仍在。
唯一變差：Walk 下樓髖 a95 19.3 → 22.7（仍低於 A 26.4）——來自樓梯下端提早開始的較快回升。

**樓梯 → 平地（離開樓梯的事件；視窗在 probe 瞬移處截斷）：**

| 事件 | n | 離開後 0.5 s 骨盆 cm：A／v1／**v2** | 最差 | 回到 −5 cm 內：v1／**v2** 最久 | 1.5 s 內沒恢復：v1／v2 |
|---|---|---|---|---|---|
| Walk 離開後停步 | 6 | −2.4／−24.0／**−5.3** | −27.0 → **−5.7** | 0.87 s → **0.55 s** | 4 → **0** |
| Run 離開後停步 | 6 | −0.5／−3.6／**−1.0** | −13.4 → **−3.3** | 0.85 s → **0.40 s** | 1 → **0** |
| Walk 走出開放頂端（離地） | 3 | 0／0／0 | | 0.35 s → 0.32 s | 1 → 0 |
| Run 走出開放頂端（離地） | 6 | 0／0／0 | | 0.22 s → 0.20 s | 0 → 0 |

（probe 序列裡離開樓梯後「繼續走」的事件都緊接著瞬移，沒有可用視窗；上表「離開後停步」的前 0.5 s 仍在 Moving 意圖下。）

**entry／exit snap（±1 s 視窗內的最大骨盆單帧變化）：**

| | A | v1 | v2 |
|---|---|---|---|
| Walk 進入樓梯（n=20） | 8.2 | 8.3 | 8.3（與 v1 逐帧相同：進入時提示不啟動） |
| Run 進入樓梯（n=24） | 8.3 | 8.3 | 8.3 |
| Walk 離開後停步 | 2.9 | 2.0 | **1.3** |
| Run 離開後停步 | 9.7 | 3.1 | **2.7** |
| Walk 走出開放頂端（離地） | 4.3 | 8.3 | 8.3 |
| Run 走出開放頂端（離地） | 8.3 | 8.3 | 8.3 |

⇒ **沒有新的 entry／exit snap**：v2 在每一類都 ≤ v1。

### 13.4 仍存在、不屬於本輪的
- **走出開放頂端的 8.3 cm／帧**：離地那一帧 `ikAllowed=false` ⇒ 既有後備路徑 `MoveTowards(0, 5 m/s)`（`pelvisTarget … : 0f`，2026-09-08 列為 snap-to-default 候選、刻意未修）。
  v1／v2 在頂端平台上仍比包絡低，所以這段拉回比 A（Walk 4.3 cm）長。v2 縮短了恢復時間，但角色在平台上停留不到 0.3 s 就離地、來不及恢復完。這是空中表現的後備規則，不是 Moving 回升語意。
- 進入樓梯的 8.3 cm：A 同樣 8.2–8.3（Run 上樓 T3 後腳鎖太久 ⇒ 已裁決的獨立 slice）。
- 停步後瞬間下蹲（使用者列的 ③）：Stop／Standing 統一，下一輪。
- Walk 上樓收益小、加速度翻倍（②）：本輪不處理；Heel Lift slice。

### 13.5 測試
EditMode **745／744／0 failed／1 skipped**（`MovingPelvisSolverTests` 21 條，新增 6 條：兩種語意、提示不是目標、提示不是上限、滯後、下降不算回升）；
stair probe 16／16；完整 PlayMode **118／109／1 failed（既有 `C0`）／8 skipped**。

### 13.6 ⏳ 人工 Play（取代 §12.6 ②③）
①走下最後一階後是否還「蹲著走一段」；②樓梯中段每步起伏是否與 v1 相同；③從 `Stairs (1)` 頂端走出去的骨盆（13.4 的既有後備路徑）；④Walk 下樓接近底端時是否感覺提早「站起來」（TerrainExit 在樓梯兩端啟動）。

## 14. v3：停步／站立也交給同一個求解器（2026-09-29；⏳ 待人工 Play；⛔ 未 commit）

### 14.1 停步下蹲的來源（runtime 逐帧診斷，v2）
使用者回報「停步後瞬間下蹲」。v2（統一 Moving ＋ 舊停步鏈）的停步逐帧 trace 顯示是**舊停步鏈的幾條規則互相打架**，外加一個 bug：

| # | 來源 | 證據 |
|---|---|---|
| 1 | **`Upright` 硬夾 bug**：`pelvis = C` 單帧下拉後保留負速度、沒有 −0.35 下限 ⇒ 彈簧繼續往下衝 | proto_Run `turnstand`：StopProgress −7.5 → 進 Upright 單帧 −26.4（19 cm）→ **−82 cm** |
| 2 | **`C` 不是真的支撐約束**：`clamp01(1.25·stance) × 可達上限`，停步 clip 抬腳／落腳時權重變 ⇒ C 在 −45 ↔ −7 間跳；PlannedStop 安全夾與 Upright 都跟著它 | s1_Walk 停步升到 −7 再掉到 −25；proto_Run 在 −4 ↔ −19 間鋸齒 |
| 3 | **`StopProgress` 只看 primary 腳**：升過兩腳都搆得到的高度，Upright 再往下夾 | 同 #1 的前半 |
| 4 | **`Return` 目標是 Moving 參考**：樓梯上它仍很深 ⇒ 站起來又被拉回 | s1_Walk Return 目標 −33.8 |
| 5 | **Predictive Planned Stop（WIP）在 runtime 裡是活的**：幾乎每次停步序列都經過 `PlannedStop` | 相位序列 |

### 14.2 統一模型本身也會下蹲——兩種不同的「蹲」
離線（v2 runtime trace 重播、85 次停步）先確認「只把停步交給統一求解器」夠不夠：

- **先升後降**（rise→fall）：停步最後幾步之間骨盆回升，下一個落腳又壓下去——純粹的回升語意問題。
- **最終站姿強迫的下降**：flat-ground 停步 clip 的最後一步（尤其 Run 的煞車步）落在較低的階，可達需求在約 0.1 s 內從 −6 長到 −36 cm——**任何只看現在的骨盆都得在落地前 0.2–0.3 s 開始下降才不會 snap**，那需要預知落點。

檢查過並淘汰：Landing 改讀動畫 pose 腳高（取代 ray 腳底離地高）——ray 的 h 雖然每帧跳 5–10 cm（跨階時 73／121 cm），但換成平滑的 pose 高度後單帧下拉仍 15 cm ⇒ 不是主因。

### 14.3 語意
停步**沒有自己的高度規則**，只改變「回升」：

| `MovingPelvisRecovery` | 何時 | ω |
|---|---|---|
| `StopHold` | 移動意圖放開後、尚未落定：**不回升**（只允許跟著約束下降） | — |
| `Stand` | 落定＝兩腳 T3 `Locked` 且 root 水平速度 < `StandingUnifiedSettleRootSpeed` 0.05 m/s（鎖存到意圖恢復） | `StandingUnifiedRiseOmega` 6 |

- 目標、約束（Support／Landing）、硬可達與移動中完全相同 ⇒ 站立＝「兩隻 Support 腳都搆得到的最高處」。
- `UseUnifiedStandingPelvis`（預設 false；需要 `UseUnifiedMovingPelvis`）開啟時，`posture` 視為關閉：**EarlyRecovery／StopProgress／Upright／Return／PlannedStop 整條旁路**，WIP 程式碼一行未動；關閉 ⇒ 回到舊鏈（A/B）。
- 離線比較：StopHold → Stand 在 rise→fall 上最好（6.6 cm vs 無 hold 13.7、舊鏈 15.7）；無 hold 的快速站起反而更糟（先升後降）。

### 14.4 驗收（runtime；同一 probe 序列；按「意圖為 0 的持續時間」分類：< 0.1 s＝輸入空檔（不算）、≥ 1 s＝停步、其餘＝取消）

| 真正的停步（放開後 1.5 s） | rise→fall 平均／最大 | > 5 cm 次數 | 強迫下降 平均／最大 | 單帧最大下拉 | 搆不到帧 |
|---|---|---|---|---|---|
| **Walk**（24）A／v2／**v3** | 9.9／9.5／**1.3**；20.7／19.8／**4.4** | 15／18／**0** | 1.2／1.5／1.3 | 4.7／3.0／3.6 | 24／24／**9** |
| **Run**（24）A／v2／**v3** | 12.6／20.2／**8.5**；24.6／**75.3**／**18.6** | 18／18／**12** | 1.7／12.4／**8.5** | 18.6／18.8／**8.3** | 36／48／**9** |
| 取消（36）A／v2／**v3** | 4.0／4.6／**0.5** | 11／11／**1** | | 1.7／1.8／4.8 | 12／12／6 |

- **Walk 停步下蹲基本消除**（沒有任何一次 > 5 cm）；舊鏈的 −75 cm 俯衝、C 鋸齒、站起再壓下都不存在了。搆不到帧 60–72 → 18。
- **Run 停步剩下的全部是「強迫下降」**（rise→fall ＝ forced，沒有先升後降）：煞車步落到較低的階。單帧上限 8.3 cm ＝ `MoveTowards` 5 m/s（硬夾 13 cm 被它限速）。
- 站立高度（結束時）：Walk −15.1（A −14.9），Run −15.0（A −12.2）——舊鏈在 Run 站得高一點，是因為 `C` 的權重讓一隻腳「不算數」（搆不到帧 36 vs 9）。
- 取消（放開後 0.3–0.6 s 重按）：v3 不回升 ⇒ 重按時骨盆仍在移動中的深度（−32.6 vs 舊鏈 −20.8），先升後降 4.6 → 0.5 cm。單帧 4.8 cm 待查。
- 移動中（steady 樓梯帧）v2 → v3：Walk 相同；Run 上 骨盆 a95 93 → 98、Run 下 深度 −11.5 → −13.4（停步／取消後延續到移動中的歷史效應）。

### 14.5 還沒解決的，與它的槓桿
- **Run 停步的強迫下降（平均 8.5、最大 18.6 cm）**：需求本身來自最後落腳，現在的約束只看當下 ⇒ 只有三條路：
  ① **預知最後落腳**（烘焙的停步落點——這正是 Predictive Planned Stop WIP 在做的事；統一模型下它可以只提供「未來的 Landing 約束」，不再是一套高度規則）；
  ② **需求端**：Heel Lift（下一輪）或 T3 讓停步最後一步落在可達處；
  ③ 接受（這是 flat-ground clip 在樓梯上的真實代價）。
- 取消時的單帧 4.8 cm、Run 移動中指標的小變動：待查。

### 14.6 測試
EditMode **751／750／0 failed／1 skipped**（`MovingPelvisSolverTests` 27 條，新增 6 條：StopHold 不回升、StopHold 仍跟約束下降、落定後 Stand 較快、站在兩腳都搆得到的最高處、恢復意圖清除落定、語意關閉時行為同移動）。
PlayMode：新增 `Player_UnifiedStanding_StandsAsHighAsBothFeetReach`（兩道樓梯 × 三朝向：兩腳搆到、≥ 下限、面向上方高於 −0.30）；`Player_StandingPosture_RisesAboveEnvelope_WithFeetReachable` 改為明確關掉統一站立（它釘的是舊鏈）。
完整 PlayMode **120／111／1 failed（既有 `C0`）／8 skipped**；stair probe 16／16（probe 的 quick-stop 觸發在統一站立下改為「放開後第一個新 Support 邊沿」，對應舊鏈的 StopProgress 進入）。
X Bot prefab：`UseUnifiedStandingPelvis = true`（Editor API）。**還原**：取消勾選 ⇒ 回到 v2（舊停步鏈）。

### 14.7 ⏳ 人工 Play
兩道樓梯 Walk／Run 上下各停步：①Walk 停步是否不再下蹲；②Run 停步的下降（強迫下降）看起來是「煞車蹲」還是「掉下去」；③停步後站起來的高度與速度；④放開後 0.3–0.6 s 重按（取消）是否順；⑤平地停步無變化。

## 15. 研究：SupportTransfer——前腳成為 Primary Support 後就開始站直（2026-09-29；⛔ 只離線驗證，runtime 未改）

### 15.1 使用者要求
人工 Play：Walking／Moving 先不再動；剩下的是**停步後的站直時機**。`StopHold` 太保守——前腳已經是可靠支撐，卻因為後腳還沒完成而不回升，蹲著等到雙腳都 Locked 才整段站起來。
要的是 `Moving → SupportTransfer → Stand`：前腳第一次 Reliable Locked 成為 Primary Support 後，骨盆就順著支撐腿連續站直；
Trailing Releasing／Swing 不再把骨盆往下拉；Trailing committed landing（已知最後落點）只當 **Future Reach Cap**（限制最多升多高，不主動拉低）；Final Stand 只做少量 settle。
不碰：Moving 求解器、TerrainExit、Heel Lift、Run 上樓 T3 釋放、Planned Stop 軌跡、新的 bake／prediction。本輪只回答這個 transition 能不能成立。

### 15.2 方法
- 輸入：v2 runtime trace（posture 開，記錄了 T3 Reliable 旗標）；T3 目標與髖不受骨盆影響 ⇒ 兩種語意在同一份輸入上重播。
- StopHold replica 先對 v3 runtime 驗證：48 次真正停步，中位誤差 0.03 cm、最差 1.1 cm。
- ⚠️ 不同 probe run 的 `Time.frameCount` 是絕對值（前面的測試長度不同）⇒ 不能跨 run 用帧號對齊。
- **SupportTransfer**（放開後）：第一個**新的** Reliable 邊沿之前＝hold（與 StopHold 相同）；之後往合法最高處回升（ω6），
  `down = min(0, Support 上限)`（Locked 腳，照常可以往下拉、硬可達照舊）；`cap` 只限制上升、永不往下拉；兩腳 Locked ＋ root 靜止後 Stand（ω6）。
- cap 三種來源比較：runtime 的 Landing 上限（未鎖、下降中）／尾腳目前 T3 目標（swing、swing＋releasing）／**oracle：尾腳真正的最後落點**（只為回答「若已知落點是否成立」）。
- 指標：開始站直時間、尾腳最後落腳時的 deficit（＝最終合法最高高度 − 當下骨盆）、單帧最大變化、**rise→drop（只計開始回升之後的下降）**、reach loss、到 final stand 的時間、回收期間尾腳可達性。

### 15.3 結果（真正的停步，24 Walk ＋ 24 Run）

| | 開始站直 | 尾腳落定時 deficit 平均／最大 | 單帧最大 | rise→drop 平均／最大（> 3 cm 次數） | 到 final stand |
|---|---|---|---|---|---|
| **Walk** StopHold（v3） | 0.82 s | 7.8／18.4 cm | 3.6 | 0／0（0） | 1.00 s |
| Walk SupportTransfer ＋ runtime cap | **0.22 s** | 2.8／10.3 | **12.1** | 4.4／12.1（**9**） | 0.96 s |
| Walk SupportTransfer ＋ 尾腳目前目標 cap | 0.22 s | 2.9／10.3 | 10.3 | 3.9／10.3（9） | 0.96 s |
| Walk SupportTransfer ＋ **已知最後落點（oracle）** | **0.37 s** | **3.9**／13.8 | **4.4** | 1.9／10.0（3＊） | **0.86 s** |
| **Run** StopHold（v3） | 1.01 s | 13.9／27.5 | 13.2＊＊ | 0／0（0） | 1.27 s |
| **Run SupportTransfer ＋ runtime cap** | **0.36 s** | **4.0**／14.3 | 13.2＊＊ | **1.1**／3.9（3＊） | **1.05 s** |
| Run SupportTransfer ＋ oracle | 0.42 s | 5.3／19.4 | 13.2＊＊ | 0.9／3.9（3＊） | 1.05 s |

Primary Support 出現在放開後 Walk 0.17 s、Run 0.34 s。reach loss 所有變體相同（Walk 9、Run 3 帧）。
＊ 剩下的 > 3 cm 全部是 `upstop` 在放開後 1.48 s（落定很久之後）的待機擺動，oracle 同樣有 ⇒ 與 transition 無關。
＊＊ Run 的 13.2 cm 是 Primary 之前的煞車強迫下降（§14.5），所有變體相同。

**按停步類型（rise→drop > 3 cm 的次數，每類 6 次）：**

| | runtime cap | oracle |
|---|---|---|
| Walk 樓梯**下行**中停步 | **6／6**（最大 12.1 cm） | 0／6（最大 0.1） |
| Walk 上行停步、原地轉身、平地 | 0（＊ 除外） | 0（＊ 除外） |
| Run 全部四類 | 0（＊ 除外） | 0（＊ 除外） |

尾腳在回收期間的可達性：Run 0 cm（全程搆得到）；Walk runtime cap 最多差 16 cm（swing 中的目標在身後的上一階），oracle 8.8 cm（StopHold 8.3）。

### 15.4 結論：transition 能不能成立
1. **Run：成立，只用現有 runtime 資料**——站直從 1.01 s 提前到 0.36 s，尾腳落定時離最終高度只差 4 cm（原 13.9），沒有新的下拉。
2. **Walk 上行、原地轉身、平地：成立**（runtime cap 下 0 次 > 3 cm）。
3. **Walk 下行中停步：只有在 Primary 成立時就知道尾腳最後落點才成立**。尾腳的最後一步落在**較低的一階**；runtime 在它落地前約 0.15 s 才知道（Landing 上限此時才出現，而且值還在隨目標滑到低階而改變——同一例中 ray 高度還跳到 121 cm 讓上限消失一帧）。
   此時骨盆已經升過頭，而 cap「只限制上升、不往下拉」⇒ 落腳鎖定時由硬可達夾一次拉回，**單帧最多 12 cm**。尾腳目前的 T3 目標也不能替代（它在身後的上一階）。
   已知落點（oracle）時 6／6 乾淨：站直 0.37 s 開始、單帧 ≤ 4.4 cm、final stand 0.86 s（StopHold 1.00 s）。
4. ⇒ **SupportTransfer 語意本身成立**；Walk 下行需要「committed landing 早於落地」這個資訊。現有程式裡唯一有這個資訊的，是 Predictive Planned Stop WIP 發佈的停步落點預測（`LocomotionStopPrediction` 的 plant events，不是它的軌跡）——本輪依指示未使用。

### 15.5 ❓ 待使用者裁決
- 是否以 SupportTransfer 取代 StopHold 做 runtime 原型？若是，Walk 下行停步要選：
  (a) 接受落腳時的強迫下拉（最多約 12 cm、單帧；由 5 m/s 速率上限限為 8.3 cm／帧）；
  (b) 讓 Walk 下行在 Primary 後仍 hold（需要一個判斷「尾腳會落到較低一階」的 runtime 訊號——目前沒有可靠的）；
  (c) 只拿 Planned Stop 的**落點預測**當 Future Reach Cap（不用它的軌跡）——需要先驗證預測準度（Codex 報告：7／8 正式停步的地面查詢失敗）。

## 16. 研究：停步預測能不能當 FutureLandingProvider（2026-09-29；⛔ 只驗證，runtime 未改）

### 16.1 使用者裁決與問題
採 §15.5 (c)，但縮到最小：Planned Stop **只當 `FutureLandingProvider`**——只回答「尾腳最後會踩在哪個世界位置」；
SupportTransfer 由前腳驅動站直，預測落點只提供 `FutureReachCap`，求解器不得升超過它。⛔ Planned Stop 不得再控制 trajectory／phase／pelvis target。
**本輪只驗證**：Stop clip 一開始，能不能可靠預測 **Walk 下樓**尾腳最後落在哪一階（公分級、不翻階）？做不到才考慮 foot-side（改最後一步的 landing policy）。

### 16.2 方法
- 預測＝既有程式原樣：`LocomotionModel.PublishStopPrediction`（發布當下 root、yaw、烘焙剩餘前進量、clip 結束站姿的 ankle-in-root）→
  `FootIKController.TryPredictGroundedAnkle`（預測 root ＋ yaw×ankle-in-root，腳跟／腳踝／腳尖取最高地面）。probe 以反射呼叫同一個函式，**只記錄、不回饋**。
- 真值＝該腳最後一次鎖定（之後保持 Locked ≥ 0.25 s）時的 T3 目標。
- ⚠️ `StairFeelProbe` 是決定性的：同一樓梯的 8 次停步其實是 2 個獨立樣本。因此另寫 throwaway `StopLandingProbe`：下樓途中在 **13 個不同位置**放開（相對階緣的相位不同）× 兩道樓梯 ＝ **26 次獨立 Walk 下樓停步**。
- 翻階＝垂直誤差 > 8 cm（半個踏階高）。

### 16.3 結果（26 次獨立 Walk 下樓停步，預測取 clip 開始發布的第一帧）

| | 結果 |
|---|---|
| **正確階** | **23／26**；垂直誤差平均 1.0、最大 1.4 cm |
| **翻階** | **3／26**（`s1_Walk_00–02`，相鄰三個放開位置）：預測低了一整階（−19.2 cm）；同一次停步的 primary 腳也同樣翻階 |
| 水平誤差 | 平均 5.7 cm：**前後 +3.9 ± 0.5 cm**（非常穩定）、左右 ±4 cm（正負號依腳別固定） |
| 預測何時可用 | 放開後平均 0.20 s（Stop clip 開始時）；primary 第一次鎖定也在 ~0.20 s——約一半的停步預測比 primary **晚**到（最多晚 0.17 s） |
| 最終站姿 | 26／26 **兩腳落在同一階**（尾腳與 primary 高差 ≤ 1 cm）。§15 說的「尾腳落到較低一階」是相對它原本的位置，不是相對 primary |

**誤差來源拆解**：
- **root 的預測幾乎完美**：預測的最終 root 前後誤差 +0.1 ± 0.1 cm（clip 開始時）、+0.3 cm（0.5 s 時）。
- **前後誤差全部在 ankle-in-root**：+3.8 ± 0.5 cm，而且只有兩個值——依播放的停步 clip 變體分別是 +3.4 與 +4.1～4.3 cm。
  ⇒ **不是預測能力的雜訊，是定義不一致**：烘焙的「clip 結束站姿腳踝」和 T3 實際鎖定腳的位置不是同一個點
  （候選原因：烘焙取 Foot 骨骼而 T3 目標是 IK goal；或 T3 在 β≥LockBeta 時就鎖、動畫腳之後還滑幾公分；或烘焙時的 avatar 與執行期不同——本專案記過同一 clip 在兩個 avatar 上差 3.7 cm）。未確認。
- 3 次翻階正好是這個 +4 cm 往前偏差造成的方向：下樓時「太前面」＝下一階。實際腳跟在上一階距階緣 4 cm 內。

### 16.4 結論
1. **目前的預測還不能直接當成「公分級、不翻階」**：12%（3／26）翻一整階。
2. **但它不是「常差一整階」的那種不可靠**：root 幾乎完美、ankle 誤差是 ±0.5 cm 的**固定偏差**；翻階全部來自這個偏差把貼近階緣的腳推過邊緣。修正定義（讓烘焙點＝T3 鎖定點）後，預期水平誤差會降到 ~1 cm 級——**這是預測資料的校準問題，不是需要改 foot-side landing policy 的證據**。⚠️ 修正後的翻階率沒有實測（本輪依指示不改 bake）。
3. **當 cap 用時方向不對稱**：預測偏低（下樓時這 3 次都是）⇒ cap 太低 ⇒ 骨盆多等一下（保守、之後 Stand 補回）；
   預測偏高 ⇒ cap 太高 ⇒ 升過頭、落腳時被硬可達拉回（危險）。上樓時同一個往前偏差會讓預測**偏高一階**（§16 之前 `proto_Walk upstop` 就是 +19 cm）⇒ 上樓若也要用 cap，必須先修偏差。
4. 預測約一半比 primary 晚到（最多 0.17 s）⇒ SupportTransfer 在拿到預測前要 hold。

### 16.5 ❓ 待使用者裁決
- (i) 先查清 +3.8 cm 的定義差（烘焙點 vs T3 鎖定點），修正後重跑這個 sweep 驗證翻階率——需要動烘焙（本輪禁止）；
- (ii) 不修，直接以現況當 Walk 下樓的 cap（翻階都偏保守）；上樓不用 cap；
- (iii) 轉向 foot-side（改最後一步的 landing policy）。
建議 (i)：誤差結構顯示問題在定義而不在可預測性。

### 16.6 產出
throwaway：`Assets/_Project/Tests/PlayMode/StopLandingProbe.cs`（新，未追蹤）；`StairFeelProbe` 增加 `sp*` 欄位。分析腳本在 session 暫存區（用完即丟）。

## 17. 定位：烘焙落點與 T3 鎖定點為何固定差 ~4 cm（2026-09-29；⛔ 只量測）

**範圍**：只定位 §16 的 +3.8 cm 前向偏差出在哪一層；不碰 pelvis／SupportTransfer／StopHold／T3 policy／Heel Lift，不新增預測。
**方法**：同一支 clip、同一個停步事件、同一座樓梯（`Stairs_650_400_300_Prefab`，Walk 下樓，兩個放開位置＝兩個 clip 變體），
throwaway `StopLandingProbe.Probe_FootPointChain` 逐帧記錄四個點，全部換到 **OnAnimatorIK 當下的 root 空間**（root 位移不進入比較）：
烘焙的 clip 結束腳踝（`EndPose.*AnkleInRoot`）／執行期 FK 腳骨（`GetBoneTransform(Foot)`，OnAnimatorIK 當下＝本帧 FK）／
動畫 IK goal（Rig 快照的 `GetIKPosition(AvatarIKGoal.*Foot)`）／T3 鎖定點（`FootContactState.LockPosition`）。

**程式裡的定義**（先讀碼再量）：
- 烘焙：`MotionBakeEditor` 以 `SampleAnimation` 取 **Foot 骨骼** `GetBoneTransform(leftFootBone).position`，減去 `animator.transform`（X Bot 的 `Model` 子物件，local offset 為 0 ⇒ 不是 root 偏移）。
- 執行期：`FootIKRig` 快照的是 **IK goal** `GetIKPosition(AvatarIKGoal.LeftFoot)`；T3 在 β ≥ LockBeta 時把「IK goal（一帧外插）」凍結成 `LockPosition`。

**結果**（root 空間；x＝左右、z＝前後；尾腳的最後落腳；兩個停步事件分別是左腳、右腳當尾腳）：

| 層 | 前後（cm） | 左右（cm） | 上下（cm） |
|---|---|---|---|
| 烘焙腳踝 − 執行期 FK 腳骨 | **0.00／0.00** | **0.00／0.00** | 0.00／0.00 |
| FK 腳骨 − 動畫 IK goal | **+2.28／+3.29** | **+4.86／−3.85** | +0.19／+0.12 |
| IK goal（clip 結束）− T3 鎖定點 | +1.17／+0.86 | −0.10／+0.26 | （不相關：預測會重新落地） |
| **合計（烘焙 − T3 鎖定）** | **+3.45／+4.15** | +4.76／−3.59 | |

**結論**：
1. **烘焙本身完全正確**：烘焙腳踝與執行期 FK 腳骨逐位元相同（同一支 clip、同一個 avatar）。
2. **偏差來自「腳骨」與「Humanoid IK goal」不是同一個點**：X Bot avatar 的 IK goal 在腳骨**後方 2.3–3.3 cm、外側 3.9–4.9 cm**（左右腳不同）。
   烘焙記的是腳骨，T3 鎖的是 IK goal ⇒ 預測參考點用錯了一層。這也解釋了 §16「只有兩個偏差值」：兩個 clip 變體的尾腳分別是左、右腳，左右腳的骨骼→goal 偏移不同。
3. 剩下 ~1 cm：T3 鎖定之後、clip 結束之前，動畫 IK goal 還移動了約 1 cm（鎖定當帧的一帧外插＋腳在鎖定後的微小滑動）。
4. 骨骼→goal 偏移隨腳的旋轉略有變化（同一隻腳在鎖定帧與 clip 結束時差 0.3–0.8 cm）⇒ 它是「腳局部座標系裡的固定偏移」，不是世界空間常數。

**❓ 修正點二選一（待使用者）**：
- **修 bake**：烘焙時記錄 IK goal 而不是腳骨（編輯器 `SampleAnimation` 拿不到 `GetIKPosition`，需在腳骨上加「腳局部座標的 goal 偏移」，偏移由 avatar 決定）；
- **改 prediction reference**：預測端把烘焙的腳骨位置換算成 IK goal（同一個腳局部偏移）。
兩者都只改「參考點」這一層；剩下的 ~1 cm 是鎖定時機，不在烘焙。

## 18. 參考點轉換：烘焙 Foot 骨骼 → T3 的 Humanoid IK goal（2026-09-29；預測端 adapter；⛔ 無消費者）

### 18.1 使用者裁決
方向 2：**Bake 維持 Foot Bone 語意不變**（已驗證與執行期 FK 骨骼逐位元一致，不破壞這個 contract）；T3 消費的是 IK goal ⇒ 轉換放在 prediction／adapter 層；
偏移以 foot-local 表示、左右腳各自一組。剩下的「clip 結束 IK goal → T3 鎖定點」~1 cm 先不修，**除非轉換後仍造成翻階**。不碰 pelvis／SupportTransfer／StopHold／T3 policy／Heel Lift／Planned Stop trajectory。

### 18.2 實作
- `FootIKSettings.StopLandingLeftGoalOffset`／`StopLandingRightGoalOffset`（落地腳座標系：x＝右、z＝前；預設 0 ＝ 不轉換、逐位元不變）。
  X Bot（Editor API）：左 (−0.0486, 0, −0.0228)、右 (+0.0385, 0, −0.0329)＝§17 在 clip 結束站姿量到的 IK goal − Foot 骨骼。y 不使用（預測會重新落地）。
- `FootIKController.TryPredictStopLandingGoal`（internal，FutureLandingProvider）：烘焙的 `EndPose.*AnkleInRoot` ＋ 該腳偏移 → 既有 `TryPredictGroundedAnkle`。純函數 `StopLandingGoalInRoot`。
  **目前沒有任何消費者**（SupportTransfer 尚未實作）；只有 probe 以反射呼叫。
- ⚠️ **兩個誠實的限制**：
  ① 烘焙沒有腳的旋轉 ⇒ 落地腳座標系以**預測 root 的朝向**近似（與既有腳跟／腳尖取樣同一假設）。嚴格的 foot-local（依腳骨旋轉）需要烘焙腳的旋轉，本輪依指示不動 bake。
  ② bone→IK goal 偏移**不是剛體常數**：在腳骨局部、IK goal 局部、root 空間三種座標系下，整個步態中的標準差都是 0.6–0.8 cm（最大偏離 2–2.5 cm）——
  Humanoid 的 IK goal 來自 clip 自己的 goal 曲線（原始動畫的腳位），不是由重定向後的腳骨推出來的。停步 clip 結束的站姿是固定的，所以取該站姿的值。
- 測試：EditMode `StopLandingReferenceTests` 3 條；EditMode 全套 **754／753／0 failed／1 skipped**。

### 18.3 驗收（同一組 26 次 Walk 下樓停步，同一次執行中同時記錄轉換前後）

| 尾腳 | 轉換前（骨骼點） | **轉換後（IK goal）** |
|---|---|---|
| 前後誤差 | +3.9 ± 0.5 cm | **+1.1 ± 0.1 cm** |
| 左右誤差（絕對值） | 4.1 cm | **0.2 cm** |
| 水平合計 | 5.7 cm | **1.1 cm** |
| 正確階 | 23／26 | **23／26** |
| 非翻階的垂直誤差 | 1.0 cm | 1.0 cm |

**水平誤差達標（~1 cm），翻階沒有消失。** 3 次翻階是同一個最終站姿（`s1_Walk_00–02` 三個放開位置都停在同一點 ⇒ 實際上是 1 個樣本）。以 Editor 射線沿前進方向 1 mm 步距掃描該處：
- 階緣在 x ＝ −19.792（上階 1.967、下階 1.773，高差 19.4 cm）。
- **實際 T3 鎖定**：腳踝已過階緣 2.3 cm（懸在下階上方），靠**腳跟取樣點在階緣前 1.9 mm** 站在上階。
- **轉換後預測**：往下坡多 1.1 cm ⇒ 腳跟取樣點過階緣 9 mm ⇒ 三個取樣點都在下階 ⇒ 低一整階。
⇒ 依使用者條件：**剩下的翻階正是那 ~1 cm（clip 結束 IK goal vs T3 鎖定點）造成的**，而且這是**真值本身只有 2 mm 餘裕的站姿**——任何 1 cm 級的預測都無法可靠判斷。

### 18.4 結論
1. 參考點轉換把水平誤差從 5.7 降到 1.1 cm，符合預期；它**不足以消除翻階**。
2. 剩下的 1 個翻階站姿是「腳跟 2 mm 站在階緣」的邊界案例；修掉 ~1 cm 的鎖定時機殘差也只能把餘裕從負變正幾毫米，無法保證。
3. 方向：這次是預測**偏低**（下樓時太前面＝下一階）⇒ 當 `FutureReachCap` 用時 cap 偏低 ⇒ 骨盆多等一下、落定後 Stand 補回（保守，不會升過頭）。偏高才危險。

### 18.5 ❓ 待使用者
- (a) 接受：以轉換後的預測當 Walk 下樓的 cap，把「腳跟站在階緣」的邊界翻階視為保守誤差；
- (b) 再修 ~1 cm 的鎖定時機殘差（例如預測「鎖定時的 goal」而非「clip 結束的 goal」）——只能縮小邊界帶、不能消除；
- (c) cap 取保守值：誤差帶（±1–2 cm）內若跨階緣，取**較低**那一階（對 cap 而言較低＝安全）。

## 19. SupportTransfer runtime 原型（2026-09-29；X Bot；⏳ 待人工 Play；⛔ 未 commit）

### 19.1 使用者裁決（規則原文摘要）
接受 §18 的邊界翻階（保守方向，不再追 ~1 cm 殘差）。開始 SupportTransfer runtime：
①前腳第一次成為 **Reliable Primary Support** 後立即開始站直；②尾腳 Releasing／Swing 不得主動往下拉（**2026-10-02 修訂，§35：下樓停步中，正在下降落地的尾腳可以往下拉**）；
③**Walk 下樓**停步以 corrected stop landing prediction 當 `FutureReachCap`；④cap 只限制「最高升到哪」，不得主動降低骨盆；
⑤尾腳真正 Locked 後改用 live Support 約束、預測退出；⑥預測不可用時用保守 fallback，但**不重新啟用整段 StopHold**；⑦預測只批准 Walk 下樓。
不改 bake、IK goal offset、Planned Stop trajectory 或其他骨盆規則。

### 19.2 實作（`MovingPelvisSolver`／`FootIKController`）
- `MovingPelvisRecovery.SupportTransfer`（新）：停步中、Primary 已出現、尚未落定。Primary ＝ 放開後第一個**新的** Reliable 邊沿（`PostureFootInputs.Reliable`，同 T3 可靠支撐定義），鎖存到意圖恢復。
- 目標：`down = min(0, Locked 腳的 Support 上限)`（只有 Locked 腳能往下拉；硬可達照舊）；`upTo = min(down, cap)`。
  `down < 骨盆` ⇒ 照常下降；`upTo > 骨盆` ⇒ 以 ω（沿用 `StandingUnifiedRiseOmega` 6）站直；否則停住（速度 ≤ 0）——cap 不會把骨盆往下拉。
- cap 來源（`MovingPelvisCapSource`）：尾腳已 Locked ⇒ 無（它已是 Support，預測退出）；
  **Walk 下樓**（放開當下鎖存：`Gait == Walk` ∧ root 垂直速度 < −0.2 m/s）⇒ `PredictedLanding`＝`LegBound(尾腳髖, TryPredictStopLandingGoal, 腿長)`；預測尚未到 ⇒ `PredictionPending`（暫不回升，只到預測到達或尾腳鎖定）；
  其他停步 ⇒ `LiveLanding`（尾腳下降落地時的即時 Landing 上限，只當上限）。
- StopHold 只剩「Primary 出現之前」；落定後 Stand 不變。`FootIKSettings.StandingUseSupportTransfer`（預設 true；false ＝ 回到 StopHold）。
- 預測只在「統一站立 ∧ 停步中 ∧ Walk ∧ 預測發布中」才查（每帧兩次 `TryPredictStopLandingGoal`）。
- 測試：EditMode `MovingPelvisSolverTests` ＋7（Primary 後立即站直、尾腳 swing 不往下拉、cap 限制最高、cap 低於骨盆不往下拉、預測等待只到預測到達、尾腳 Locked 後預測退出、只批准 Walk 下樓）。
  EditMode 全套 **761／760／0 failed／1 skipped**；完整 PlayMode **174／165／1 failed（既有 `C0`）／8 skipped**（總數含 probe runs）。

### 19.3 驗收：26 次 Walk 下樓停步（`StopLandingProbe`，同一停步事件 StopHold／SupportTransfer 各跑一次）

| | StopHold | **SupportTransfer** |
|---|---|---|
| Primary 後多久開始站 | 0.69 s（最大 0.90） | **0.29 s**（最大 0.50） |
| 尾腳落定時距最終站姿 | 18.8 cm（最大 28.7） | **11.6 cm**（最大 26.4） |
| rise→drop | 0 | **0**（最大 0.06 cm） |
| 最大單帧骨盆變化 | 1.08 cm | **1.05 cm** |
| reach loss | 0 帧 | **0 帧** |
| 到最終站姿 | 1.67 s | **1.33 s** |
| 最終高度 | −12.5 cm | −12.5 cm（相同） |

**knife-edge 站姿**（`s1_Walk_00–02`，§18）：rise→drop 0、單帧 ≤ 1.1 cm ⇒ 只是較晚站直，沒有下蹲。

**為什麼尾腳落定時還差 11.6 cm**（離線 replica 在同一批 trace 上重現 runtime：中位差 0.33 cm）：
- 不是 cap 的定義：cap 用「尾腳**現在**的髖」到未來落點（Primary 時水平相距 64 cm ⇒ cap −56.5 cm，落定時只剩 8 cm ⇒ −12.4 cm）確實過度保守，
  但換成「落定時的髖」只從 11.5 → 11.0 cm。
- 不是 primary 支撐腿：落定時 primary 的 Support 上限已經允許 −12.9 cm（≈ 最終 −12.5），骨盆在 −24.1、仍在 SupportTransfer 上升中。
- **是上升速率**（ω6 在 0.3–0.5 s 的窗口內只走完一半）加上 Primary 後約 0.25 s 的幾何延遲（髖還沒越過支撐腳、等預測）。

| SupportTransfer ω | 尾腳落定時差 | rise→drop 最大 | 單帧最大 | 到最終站姿 |
|---|---|---|---|---|
| **6（現值）** | 11.5 cm | 0.01 cm | 1.03 cm | 1.33 s |
| 8 | 9.3 | 0.05 | 1.34 | 1.16 s |
| 10 | 7.7 | 0.13 | 1.64 | 1.04 s |
| 12 | 6.5 | 0.32 | 1.94 | 0.97 s |
| 16 | 4.8 | 0.62 | 2.51 | 0.90 s |

### 19.4 其他停步類型（`StairFeelProbe`，v3 StopHold → SupportTransfer；各 8 次）

| 停步 | 開始站（放開後） | rise→drop 最大 | 單帧最大 | reach loss | 到最終 |
|---|---|---|---|---|---|
| Walk 下樓 | 0.89 → 0.57 s | 0 → 0.1 | 3.6 → 3.6 | 12 → 9 | 1.31 → 1.11 s |
| **Walk 上樓（upstop）** | 0.93 → 0.42 s | **0 → 10.1 cm** | 1.7 → 1.7 | 0 → 0 | 0.70 → 1.12 s |
| Walk 轉身站立 | 0.59 → 0.24 s | 0 → 2.0 | 0.5 → 0.5 | 0 → 0 | 0.95 → 0.74 s |
| Walk 平地 | 0.97 → 0.12 s | 0 → 1.5 | 0.3 → 0.3 | 0 → 0 | 1.03 → 0.83 s |
| Run 下樓 | 1.09 → 0.44 s | 0 → 0 | 8.3 → 8.3 | 8 → 8 | 1.45 → 1.31 s |
| **Run 上樓（upstop）** | 1.09 → 0.36 s | **0 → 4.3 cm** | 2.2 → 2.2 | 4 → 4 | 1.45 → 1.14 s |
| Run 轉身站立 | 0.65 → 0.42 s | 0 → 0 | 6.8 → 6.9 | 0 → 0 | 1.04 → 0.97 s |
| Run 平地 | 1.25 → 0.41 s | 0 → 2.1 | 0.5 → 0.5 | 0 → 0 | 0.67 → 0.97 s |

**上樓停步的 rise→drop**（`proto_Walk upstop` 逐帧）：Primary 後骨盆從 −23 升到 −14 cm（尾腳 Releasing／Swing，依規則②④不往下拉），
尾腳約 0.9 s 鎖定時兩腳最終站姿需要 −24 cm ⇒ 骨盆平滑地（單帧 ≤ 1.7 cm）下降 10 cm。
這正是 Walk 下樓問題的鏡像：上樓停步沒有未來落點資訊（規則⑦只批准 Walk 下樓），規則⑥的 LiveLanding fallback 來得太晚（§15 已對 Walk 下樓證明過）。

### 19.5 ❓ 待使用者
1. **SupportTransfer 的上升速率**：維持 ω6（最平順）或 ω8–10（尾腳落定時更接近最終、單帧 1.3–1.6 cm）。
2. **非批准停步的 fallback**（上樓 rise→drop 最多 10 cm）：
   (a) 預測批准範圍擴到 Walk 上樓（§16 前的 +19 cm 向上翻階已由 §18 的參考點轉換處理，但**上樓尚未驗證**——向上翻階是危險方向）；
   (b) 非批准停步在尾腳鎖定前不回升（等於那些情況回到 StopHold 的後半段）；
   (c) 接受。

## 20. 定位：停穩過程中腳目標的整階翻轉（2026-09-29；⛔ 只量測，未修）

### 20.1 問題
人工 Play：停穩過程中某隻腳的 contact／landing target 在相鄰兩階之間整階切換（上→下或下→上）。
使用者要求：不修、不加 smoothing／hysteresis／freeze，只回答「第一個整階變化發生在哪一層」。

### 20.2 方法
- throwaway `StopLandingProbe.Probe_StepFlip`：2 樓梯 × Walk／Run × 上／下 × 13 個放開位置 ＝ **104 次停步**（SupportTransfer 開），放開後逐帧 150 帧。
- `FootContactState` 加**只寫不讀**的觀測欄位（`Debug*`：三端點的即時命中高度／法線／碰撞體、選定支撐、接觸量、移動面參考、即時解；`#if UNITY_EDITOR || DEVELOPMENT_BUILD`）。
- 事件判準：同一隻腳相鄰兩帧「選定支撐高度」變化 ≥ 7 cm（級高 19.5 cm）**且**「目標 − (移動面 ＋ 動畫離地高度)」單帧跳 ≥ 4 cm。
- 同一停步事件 StopHold 對照：`Probe_StepFlipStopHold`（3 個代表事件）。

### 20.3 結果：誰先變？

| 層 | 結論 | 證據 |
|---|---|---|
| ① 地面取樣（heel／toe 射線） | **第一個變的就是它** | 277／277 個有效整階事件，同一帧必有一個端點的命中高度跳一整階（toe 192、heel 85；另 8 個是 s1 Run 上樓衝出頂端、IK 關閉，不算） |
| ② T3 即時目標 | 同一帧跟著跳（無延遲） | 最小抬升無記憶：穿透 ⇒ 完整抬起；懸空 ⇒ β 比例下拉 |
| ③ LockPosition／LockedSupportY | **沒有被刷新**（Lock contract 成立） | 104 次停步、所有 Locked→Locked 帧：兩者變化 **0 次** |
| ④ Reliable | 翻轉期間維持 true；**不是來源** | Reliable ＝「Locked ∧ β≥0.95 ∧ 即時命中 ∧ 腳速低」＝「目前有可信接觸」，不檢查目標高度是否穩定 |
| ⑤ SupportTransfer | **無關** | 同一事件 ST vs StopHold：骨盆差最多 14 cm，但兩腳目標**逐位元相同**（最大差 0.0000）、取樣差 0、相位差 0 |

### 20.4 兩類翻轉

**A 類 — Locked 中的整階跳（最符合「停穩過程中」）**：proto Walk 上樓 **7／13**（全是右腳最終站姿）、s1 Run 下樓 1／13（左腳最終站姿）；另 4 次在非最終的 Locked 期間。Walk 下樓 **0／26**。
- 機制：鎖定只固定**腳踝 XZ**；三個端點 ＝ 鎖定腳踝 ＋ 本帧顯示旋轉 ×（腳跟／腳尖偏移）。停步動畫的腳旋轉在變 ⇒ 端點水平漂 1–5 mm（Walk 上樓後段 yaw 讓腳尖側移 6 cm）。
  最終站姿的某個端點正好在**階緣前後幾 mm**（proto Walk 上樓腳尖距下一個 riser 3–4 mm；s1 Run 下樓腳跟 ~1 mm）⇒ 射線在上階／下階間翻。
- 傳遞：選定端點的地面 ＝ `max(即時, LockedSupportY)` ⇒ 往**下**翻被擋住，往**上**翻直接通過（「滑進更高的面仍會抬起」）；端點水平上貼著 riser、垂直上在上階頂面之下 ⇒ 判成 9 cm 穿透 ⇒ 完整抬升。
  結果：目標 **+18～19 cm 維持 5–18 帧再掉回**（proto_Walk_Up_04 右腳 t＝56 上、t＝61 下；s1_Run_Down_03 左腳 t＝29 上、50 下、68 上）。
- 下游：骨盆 Support 上限跟著跳（s1_Run_Down_03 limit −6 → −24 cm、recovery 5↔0），但骨盆本身被平滑吸收（≤ 1 cm）；**看得到的是腳本身跳一整階**。

**B 類 — 落腳中（Unlocked，β 0.3–0.9）的端點越過階緣**：每個情境 6–13／13 次停步都有，多為單向（30 次翻回）。
腳跟／腳尖越過階緣的那一帧，支撐整階改變 ⇒ 目標跳 β×級高（4–13.5 cm；最大 proto_Walk_Down_06 左腳 −13.5 cm @β 0.81）。Walk 下樓停步的翻轉全屬此類。

### 20.5 最小修法候選（⛔ 未實作，待裁決）
- **A 類**：鎖定期間的取樣位置（三端點 XZ）改用**鎖定當帧的腳朝向**計算，而不是每帧的顯示旋轉——腳的顯示旋轉不變，只是「腳底踩在哪一階」隨鎖定一起決定（與既有 `LockedSupportY` 同一個理由：`FootContactSolver.cs` 註解已記錄「鎖定中滾動⇒端點跨階緣⇒跳 18 cm」，當時只擋了往下的方向）。
  ⚠️ 需先驗證：鎖定期間真實的大幅轉向（Walk 上樓後段 yaw 6 cm）是否該改變取樣。
- **B 類**：本質是「落腳相位中，腳底支撐是階梯函數」。候選要等使用者確認他看到的是 A 還是 B 再談。

### 20.6 ❓ 待使用者
1. 人工 Play 看到的是哪一類？判別：**A 類會在 5–18 帧後掉回原階**（腳已站定、突然抬高一階又落下）；B 類是落腳那一下單向往下跳。A 類重現點：proto 樓梯 Walk 上樓停步。
2. 是否以 20.5 的 A 類候選作為下一輪的最小修法（先驗證再改）。

### 20.7 產出（throwaway）
`StopLandingProbe.Probe_StepFlip`／`Probe_StepFlipStopHold`（未追蹤）；`FootContactState.Debug*` 觀測欄位（`FootContactSolver.cs`、`FootIKController.AccumulateContact` 由 `in` 改 `ref` 以寫入）——決策不讀，EditMode 761／760／0 failed／1 skipped 與前一輪相同。
還原：刪除兩個 probe 方法、`Debug*` 欄位與兩段 `#if` 寫入、`AccumulateContact` 改回 `in`。

## 21. A/B：Locked sample-frame freeze（2026-09-29；🔬 原型開關，預設關；⛔ 未改 T3 policy）

### 21.1 使用者裁決
只驗證 A 類（§20.4）：Locked 期間除了鎖腳踝，也把 heel／toe **查詢地面用的水平位置**固定在鎖定當帧的腳朝向；畫面上的腳旋轉不 freeze。
若鎖定中的 ~6 cm 腳尖移動是合理的 pivot ⇒ 不要全 freeze，改提更窄的方案。不處理 B 類、骨盆、SupportTransfer、ω、預測、Heel Lift、Planned Stop、Reliable、smoothing。

### 21.2 原型
- `FootIKSettings.LockedSampleFrameFreeze`（預設 false ＝ 現行）；`FootContactState.LockSampleRotation`（鎖定當帧記下顯示旋轉）。
- 開啟且支撐已選定時：`AccumulateContact` 的射線水平位置 ＝ `SolePoint(腳踝, LockSampleRotation, …)`；端點高度（穿透／懸空量）仍用本帧旋轉。
- 驗收：同一 probe 104 次停步 × 關／開（`Probe_StepFlip` 加 `freeze` 參數）；畫面腳底三點另做「真實穿透」＝ min(垂直深度, 最短水平逃脫距離)——貼著 riser 1 mm 不能算成 18 cm 穿透。

### 21.3 結果（104 次停步，A＝現行、B＝freeze）

| 驗收項 | A | B |
|---|---|---|
| ① A 類 Locked 整階翻轉 | **15 次**／8 隻腳（proto Walk 上樓 5、s1 Run 下樓 2、proto Run 下樓 1） | **0** |
| Locked 中單帧接觸修正跳變 max | 19.1 cm | 2.5 cm |
| ② LockPosition／LockedSupportY 變化（Locked 中） | 0 | 0 |
| ③ 顯示旋轉 | — | 與 A **逐帧相同**（18,787 帧 × 2 腳，差異 0）；相對鎖定朝向仍轉 p50 2.2°、p95 14.6°、max 27.4° ⇒ 沒被 freeze |
| ④ 查詢點 vs 畫面腳底（水平） | 0（定義上相同） | heel p95 0.8／max 3.3 cm；**toe p95 4.2／max 6.8 cm** |
| ⑥a 查詢點相對鎖定當帧的漂移 | heel p95 0.8、toe p95 4.2／max 6.8 cm | 0 |
| ⑥b 鎖定中選定支撐整階改變 | 15 | 0 |
| ⑥c 目標高度 | — | 648 個腳-帧不同（max 19.2 cm＝被消掉的翻轉） |
| ⑥d 畫面腳底真實穿透 > 1 cm | 0 帧 | **18 帧（max 2.1 cm）** |
| ⑥d 懸空 > 1 cm | 443 帧 | 400 帧 |
| ⑥e 鎖定時長 | 113.7 帧／腳、1.34 段 | **相同**（相位逐帧差異 0） |

### 21.4 ⑤ 那 6 cm 是什麼：**合理的 pivot，不是查詢漂移**
proto_Walk_Up_04 右腳，Locked 期間（t＝37 起）：
- t＝37–55：動畫腳整體平移 0–3.7 cm（root 滑行，heel／ankle／toe 等量），顯示旋轉 < 2°。**第一次往上翻就發生在這段**（查詢點漂 0.24–0.45 cm、旋轉 1–3°）。
- t＝58–64：動畫腳**繞靠近腳跟的點外旋 20–24°**（動畫腳尖移 5.5–7.7 cm、腳跟 2.6–2.9 cm），之後停在 ~15° 直到 idle 結束——停步進入 idle 站姿的外八。β 只降到 0.77（> UnlockBeta 0.7），所以整段都在同一次 Locked 裡。
- 全體：鎖定中顯示旋轉 p95 14.6°，每個情境都有 ⇒ 是常態，不是特例。

⇒ **全 freeze 不合格**：它確實消掉 A 類，但腳尖查詢點會在整個 idle 期間與畫面腳尖相距 4–7 cm；B 新增的 18 帧穿透全部發生在這種 pivot 之後（顯示旋轉偏離鎖定 8–22°，畫面腳跟／腳尖轉進另一階的 riser 1–2 cm，但查詢還停在鎖定位置）。

A 類 15 次的觸發分布：8 次向上翻發生在查詢點漂 0.2–2.3 cm；7 次掉回全在 pivot 把腳尖帶離 riser 的時候（漂 5.1 cm、21°）。

### 21.5 更窄的方案（⛔ 未實作，待裁決）：Locked riser-intrusion rule
**查詢照舊跟著畫面腳底走**（不 freeze 任何東西），只改一件事：Locked 且支撐已選定時，若某端點的即時地面比 `LockedSupportY` **高出一整階以上**（門檻例如 8 cm），把它判成「側向碰到 riser」而不是垂直穿透——該點改用 `LockedSupportY`，不把整隻腳抬起一階。
- 與既有 `max(即時, 鎖定支撐)` 同一個家族：現在只擋往下，這條補上「往上整階」；小幅抬升（< 門檻）照舊，不會穿進真的凸起。
- 無狀態、不看時間、不是 hysteresis；Unlocked／Releasing（B 類）完全不受影響。
- 以本輪資料估算：規則只在那 **8 隻 A 類腳、147 帧**啟動（其他 Locked 帧 0 次）；Locked 中「比鎖定支撐高 0.5–8 cm」的正常抬升 0 帧 ⇒ 在這兩道樓梯上不會擋到合理抬升。代價＝腳底側向插進 riser：以 B 同時刻畫面估計 **p50 3 mm、p95 11 mm、max 21 mm**（與 B 同量級，但查詢點與畫面腳底脫離 0）。
- ⚠️ 未驗證項：斜坡／不規則地面上的門檻選擇；一整階以上的真實側向侵入（本輪最深 21 mm）時是否要另外處理。

### 21.6 ❓ 待使用者
1. 接受「全 freeze 不合格（pivot 合理）」的結論？
2. 下一輪是否以 21.5 的 riser-intrusion rule 做同一套 104 × A/B 驗證（同樣先以原型開關實作）？

### 21.7 產出
原型開關 `LockedSampleFrameFreeze`（預設關，X Bot 未改）＋ `LockSampleRotation`；probe 擴充（`freeze` 參數、`Probe_StepFlipSmoke`、畫面腳底／真實穿透／動畫腳底點）。EditMode **761／760／0 failed／1 skipped**（不變）；probe 213／213。
還原：刪除設定欄位、`LockSampleRotation`、`sampleRotation` 與 `AccumulateContact` 的 `sampleRotation` 參數（查詢改回本帧端點）。

## 22. A/B：Locked riser-intrusion rule（2026-09-29；🔬 原型開關，預設關；⛔ 未改 T3 policy）

### 22.1 使用者裁決
接受 §21（full freeze 不採用；20–27° pivot 是合理動畫）。原型：Locked 時周邊 heel／toe 命中顯著高於 `LockedSupportY` 的相鄰表面 ⇒ 先視為 pivot 側向碰到 riser，不是新的垂直支撐；不得把整個 T3 目標抬上一階。查詢仍用實際腳位與旋轉。不處理 B 類。不能只用高度差全域忽略。

### 22.2 原型（`FootIKSettings.LockedRiserIntrusionRule`，預設 false）
條件**全部**成立才套用（該點的地面改用 `LockedSupportY`）：Locked ∧ 支撐已選定 ∧ 點是 heel 或 toe（ankle 不適用）∧ **腳踝底的即時命中仍在原支撐**（±`SupportTolerance` 3 cm）∧ 高出 `LockedSupportY` 落在 [`MinRise` 8 cm, `MaxRise` 30 cm]（相鄰一階）。
規則關閉時，編輯器／開發版仍做影子評估（多一條腳踝底射線、只寫 `Debug*`）⇒ 兩組都有同一批侵入紀錄。

### 22.3 結果（104 次停步 × 規則關／開）

| 驗收 | 關 | 開 |
|---|---|---|
| A 類 Locked 整階翻轉 | 15 | **0** |
| 鎖定相位／時長 | — | 逐帧差異 **0**（18,787 帧 × 2 腳） |
| LockPosition／LockedSupportY 在 Locked 中改變 | 0 | 0 |
| 顯示旋轉 | — | 逐帧差異 0 |
| 垂直穿透 > 1 cm（水平逃脫 ≥ 垂直深度） | 0 | **0**（未新增） |
| 側向 riser 侵入（畫面腳底，147 帧） | — | **median 3.0／p95 11／max 21 mm** |
| Locked 中單帧目標變化 max | 19.2 cm | 5.8 cm（侵入 episode 內 1.15 cm） |
| 懸空 > 1 cm | 443 | 443 |
| **B 類（Unlocked 落腳）整階翻轉** | 192 | **192（不變）** |
| Releasing 中的整階跳變 | 62 | **66（+4，見 22.5）** |

### 22.4 七項紀錄（9 個侵入 episode，147 帧；兩組相同）
- 來源點：toe 5（全是 proto Walk 上樓）、heel 4（Run 下樓）。ankle 全程在原支撐上（147／147）。高度差全是一階（19.4／19.5 cm）。
- 查詢點到 riser 的水平侵入：median 3.0、p95 11、max 21 mm。持續帧數：4、5、5、5、8、8、8、21、83（83＝s1 Run 下樓 #03 最終 idle 全程）。
- 侵入期間顯示旋轉相對鎖定：p50 6.7°、max 20.3°。

### 22.5 人工檢查最大案例：proto Run 下樓 #10 左腳（21 mm）——**不是站姿中的側碰**
- t＝22–29：β 0.93 → 0.74（離地中）、外轉 7 → 20°；取樣腳跟高度 0.38–0.43，上一階頂面 0.39 ⇒ **動畫腳跟本身已抬到上一階高度**（7／8 帧在頂面之上）。
- 關：t＝22 目標 +18 cm（鎖定中 β 下拉被上一階取消）。開：目標隨 β 下降緩升 0.299 → 0.369，畫面腳跟側向進 riser 2 → 21 mm。
- t＝30 釋放：規則只管 Locked ⇒ `ResolveHeight` 的接觸下限把腳推出上一階：**+9.7 cm**，t＝32 腳跟越回下階 **−8.8 cm**。s1 Run 下樓 #07 同型（+8.9／−9.8）。這就是 Releasing +4 的全部來源。
⇒ 規則在這 2 個 episode 把「一次 18 cm」換成「釋放時兩次 ~9 cm」，而且介入的是合理的抬跟＋pivot。

### 22.6 三種 episode（取樣點相對上一階頂面的深度）
| 類型 | episode | 深度 | β | 規則是否該管 |
|---|---|---|---|---|
| 真正側碰 riser | proto Walk 上樓 ×5（toe） | **+7.6～+10 cm**（埋在台階體積內） | 0.92–0.97 | ✅ |
| 腳跟卡在階角（站姿） | s1 Run 下樓 #03 ×2（heel） | +1.6～+2.3 cm（t0＝29 有 2 帧 −0.1） | 0.90–0.99 | ✅（結果留在鎖定階，侵入 ≤13 mm） |
| 抬跟越過上一階（離地） | proto Run 下樓 #10、s1 Run 下樓 #07（heel） | **−3.7～+0.8 cm**（在頂面之上） | 0.73–0.93 ↓ | ❌ 合理動作 |

「取樣點必須低於上一階頂面 > ε」可以排除第三類（ε＝0–1 cm），但 s1 #03 t0＝29 有 2 帧深度 −0.1 cm ⇒ 那一段會在 knife-edge 上再翻一次（垂直方向的 knife-edge）。ε＝2 cm 會連 #03 的 idle 也排除。

### 22.7 ❓ 待使用者
1. 接受現版（A 類 15 → 0；代價：2 個離地 episode 的釋放 pop 各 ~9 cm × 2、側向侵入 ≤ 21 mm）？
2. 或加「埋入深度 > 0」條件再跑一次 A/B（預期：第三類回到現行的一次 18 cm；s1 #03 可能剩 1–2 次 knife-edge 翻轉）？
3. 第三類的 18 cm 本質是「鎖定中懸空 β 下拉在碰到上一階時整段取消」——與 riser 側碰不同機制，是否另案？

### 22.8 產出
`FootIKSettings.LockedRiserIntrusionRule`＋`MinRise`／`MaxRise`／`SupportTolerance`（預設關；X Bot 未改）；`AccumulateContact` 多 `ankleOnSupport` 參數；`FootContactState` 侵入觀測欄位；probe 的 `freeze` 參數改為 `rule`（§21 freeze 固定關）。
EditMode **761／760／0 failed／1 skipped**；probe 213／213。還原：刪除四個設定欄位、腳踝底預查詢與 `intrusion` 判斷（`hitY` 改回即時命中）。

## 23. riser-intrusion 的幾何條件（edge tolerance ε）與 A/B（2026-09-29；🔬 原型開關，預設關）

### 23.1 使用者裁決
不接受 §22（把合理的抬跟／釋放誤判成侵入，解除時新增 ±9 cm）。riser 侵入改定義為**幾何側面侵入**：Locked ∧ heel／toe ∧ 腳踝底仍在 LockedSupport ∧ 高出約一階 ∧ **端點本身仍在 riser 的垂直範圍內，或只高出上一階頂面極小的 ε**。ε 由三群資料決定。第三類抬跟原本的 18 cm 變化本輪不處理。

### 23.2 三群的 `sampleY − 上一階頂面`（§22 規則關閉組的影子紀錄）
`sampleY` ＝ T3 **接觸修正之前**的端點高度（移動面 ＋ 動畫離地高度，本帧旋轉）——代表「動畫要這個端點在哪」，不是最後畫出來的腳底。

| 群 | 來源 | n | min | p50 | p95 | max（cm） |
|---|---|---|---|---|---|---|
| ① 真 riser 側碰 | proto Walk 上樓腳尖 ×5 | 31 | −10.0 | −8.5 | −7.7 | −7.6 |
| ② 腳跟階角站姿 | s1 Run 下樓 #03 腳跟 ×2 | 104 | −2.35 | −1.67 | −0.68 | **+0.14** |
| ③ 抬跟／離地誤判 | proto Run 下樓 #10、s1 Run 下樓 #07 腳跟 | 12 | −0.77 | +1.47 | +2.75 | +3.71 |

③ 只有 #10 的前 2 帧落在 ② 的範圍（−0.77、+0.05）；其餘 ③ 最小 **+0.68**。
⇒ **ε ＝ 4 mm**（介於 ② max 1.4 mm 與 ③ 其餘 min 6.8 mm 之間，兩邊各 ~2.6 mm 餘裕）。
#10 的第一帧在頂面**下方** 0.77 cm、側向進 riser 2 mm——依定義那一帧確實是幾何側面侵入；無法用 ε 排除，也不應排除。

### 23.3 實作
條件新增 `sampleY ≤ higherHitY + LockedRiserIntrusionEdgeTolerance`（`FootIKSettings`，4 mm）。其他條件同 §22.2。

### 23.4 結果（104 次停步 × 規則關／開）

| 驗收 | 關 | 開 | |
|---|---|---|---|
| A 類 Locked 整階翻轉 | 15 | **2** | 剩下的全是 ③ 抬跟（本輪不處理）：#07 與現行相同；#10 晚 2 帧（t＝24，16.6 cm，現行 t＝22、18 cm） |
| 腳跟階角站姿重新翻階 | 3 | **0** | s1 #03 的 104 帧全部維持鎖定階 |
| 抬跟誤判帧數 | — | #07 **0**；#10 **2**（起始 2 帧，見 23.2） | #10 從 t＝24 起與現行逐帧相同，含釋放 |
| Releasing 整階跳變 | 62 | **62**（清單逐項相同） | ±9 cm 釋放 pop 消失 |
| B 類（Unlocked 落腳） | 192 | **192** | |
| 垂直穿透 > 1 cm | 0 | **0** | |
| 側向 riser 侵入（畫面腳底，137 帧） | — | **median 3.0／p95 8／max 13 mm** | §22 為 max 21 mm |
| 鎖定相位／時長／顯示旋轉 | — | 逐帧差異 **0** | 18,787 帧 × 2 腳 |
| LockPosition／LockedSupportY 在 Locked 中改變 | 0 | 0 | |
| 侵入 episode 內單帧目標變化 max | 19.2 cm | **1.0 cm** | |

### 23.5 人工檢查最大側向侵入（s1 Run 下樓 #03 左腳，13 mm）
- t＝38–47（β 0.88–0.94）：畫面腳跟畫在**下一階高度**（0.62–0.65；上一階頂面 0.804），側向伸進上一階 riser 2 → 13 → 2 mm，平滑往返；同時刻動畫要的腳跟高度約在上一階頂面（−0.8～+0.14 cm）。
- 最終 idle（t＝68 起，83 帧）：側向 1–3 mm，腳留在鎖定的下一階（現行此處是 +19 cm 抬上上一階，並在 t＝29／50／68 來回翻）。
- ⇒ 可見代價是腳跟短暫插進 riser ≤ 1.3 cm；換掉的是 3 次 19 cm 整階翻轉。

### 23.6 ❓ 待使用者
1. 人工 Play 驗收（開關 `LockedRiserIntrusionRule` 目前在 X Bot 上**未開**——要驗收需在 Inspector 勾選）：proto 樓梯 Walk 上樓停步（腳尖）、s1 樓梯 Run 下樓停步（腳跟階角；注意 riser 插入是否可見）、兩道樓梯 Run 下樓（抬跟離地不該被影響）。
2. 通過後：是否把開關改成 X Bot 預設開啟（仍是原型，T3 policy 正式化另議）。
3. ③ 抬跟的 18 cm 只在人工 Play 仍可見抽動時另開 release slice（使用者既定）。

### 23.7 產出
`LockedRiserIntrusionEdgeTolerance`（4 mm）＋侵入條件一行。EditMode **761／760／0 failed／1 skipped**；probe 213／213。

## 24. 規格：Locked foot 的 XZ bounded compliance（Y hard ／ XZ soft）A/B（2026-09-29；Claude＝規格／審查，Codex＝實作）

### 24.1 假設（使用者）
人工 Play 仍可見：停穩期間動畫只是細微收步／pivot，但 Locked 腳的 XZ 完全不動（XYZ hard lock），heel／toe 取樣被「推」到階緣另一側 ⇒ 整階 Y flip。
驗證：`Y／support surface ＝ hard`、`XZ ＝ bounded soft compliance`、`Rotation ＝ 保留動畫`。**不是解鎖腳**：Locked 期間 XZ ＝ anchor ＋ 有界的動畫偏移。

### 24.2 落在哪一層（依現有責任）
- **`FootContactSolver.ResolveHorizontal`**（純函數）是唯一決定「Locked 腳水平位置」的地方——compliance 只改它的 `Locked` 分支。
- **`FootContactSolver.StepLock`** 的解鎖判準（β、`UnlockRadius`、接觸）**不改**：距離仍量「動畫 − anchor」⇒ 鎖定相位與時長理論上不受 compliance 影響（接觸失效除外，需量測）。
- **`StepLock` 的 Locked → Releasing 轉移**：`ReleaseOffset` 必須從**上一帧實際解出的位置**開始，而不是 anchor——否則釋放當帧跳 r（最多 4 cm）。
- **支撐選擇**（`LockedSupportY`／`LockedSupportPoint`，鎖定當帧決定）與 `ContactGround` 的鎖定下限**不改**。取樣點會隨解出的腳踝移動——這正是要量的東西，⛔ 不加任何防護（不 hysteresis、不 freeze、不特例）。
- `FootIKController` 只負責把設定值傳進去；骨盆求解器讀到的是 T3 本帧目標（會跟著變）——**不改骨盆程式**，只記錄。

### 24.3 資料契約（明寫）
| 欄位 | 語意 |
|---|---|
| `FootContactState.LockPosition` | **＝ anchor**：鎖定當帧的動畫腳踝 XZ；Locked 期間**不變**（語意與現行相同） |
| `FootContactState.LockResolvedPosition`（新） | Locked 期間本帧解出的腳踝 XZ（＝ anchor ＋ 有界偏移）；釋放從它開始 |
| `FootIKSettings.LockedXZComplianceRadius`（新，預設 **0**） | 有界半徑（m）。**0 ＝ 關閉 ＝ 逐位元等於現行**（實作須在 r ≤ 0 時直接回傳 anchor，不經過浮點運算） |

解出位置：`resolved = anchor + ClampMagnitude(animPredictedXZ − anchor, r)`（`animPredictedXZ` ＝ 傳進 `ResolveHorizontal` 的一帧外插動畫位置，y 照現行取動畫值）。

### 24.4 必須維持
1. `LockedSupportY`／`LockedSupportPoint` 在 Locked 中不改變（量測斷言）。
2. compliance 本身不重新選支撐（選擇只在鎖定當帧，程式不動）；取樣跨階緣另計次數。
3. 顯示旋轉不 freeze（逐帧與 A 相同）。
4. `LockPosition` ＝ anchor，語意不變。
5. 不處理 B 類；不碰骨盆、SupportTransfer、ω、預測、Heel Lift、Planned Stop；§21 freeze 開關維持關。

### 24.5 A/B 矩陣（`StopLandingProbe.Probe_StepFlip`，104 次停步）
compliance r ∈ {**A 0**, **B 2 cm**, **C 4 cm**} × riser-intrusion rule（§23）∈ {關, 開} ＝ 6 組 × 104。

### 24.6 量測（每組都要，與 A 對照）
| # | 量 | 定義 |
|---|---|---|
| M1 | Locked step flip | Locked→Locked，`|ΔsupY| ≥ 7 cm` 且 `|Δ(tgtY − refY − predH)| ≥ 4 cm`；逐事件列出，並對 A 的每個事件標註 B／C 的結果（消失／提早／延後／更深） |
| M2 | compliance 使用量 | `|resolved − anchor|` p50／p95／max；飽和（≥ 0.99 r）帧比例 |
| M3 | 動畫 vs 解出位置誤差 | `|animPredicted − resolved|` p50／p95／max（A 即 `|anim − anchor|`） |
| M4 | foot slide | Locked 中解出腳踝的每帧位移與每段鎖定的累積路徑長 |
| M5 | 取樣跨階緣 | Locked 中 heel／toe 即時命中高度單帧變化 ≥ 7 cm 的次數（依點別） |
| M6 | riser 侵入 | 畫面腳底水平侵入 median／p95／max（沿用 §21 的真實穿透算法） |
| M7 | 垂直穿透／懸空 | 真實穿透 > 1 cm 帧數、垂直-only 穿透 > 1 cm 帧數、懸空 > 1 cm 帧數 |
| M8 | final stance | 每次停步最後一帧：兩腳支撐階（LockedSupportY）、解出 XZ − anchor、目標 Y；與 A 比較哪些改變 |
| M9 | lock duration | 相位逐帧差異數；每腳鎖定帧數與段數 |
| M10 | T3 目標單帧 Y 變化 | Locked 中 max、p99 |
| M11 | 釋放連續性 | Locked→Releasing 當帧目標 XZ 跳變 max（應 ≈ 0） |
| M12 | 動畫偏移的形狀 | Locked 中動畫 heel／ankle／toe 相對 anchor 的位移：平移（三點等量＝root 滑行）或旋轉（某一端不動＝pivot）——用來判斷是否該改成 heel／toe pivot anchor |
| M13 | B 類 | Unlocked 整階翻轉次數（應與 A 相同） |

### 24.7 事前預測（Claude，用 §23 規則關閉組的資料離線估算；審查時對照）
- Locked 中 `|動畫腳踝 − anchor|`：p50 2.5、p90 8.5、p95 9.1、max 14.5 cm；> 2 cm 佔 52%、> 4 cm 佔 34% ⇒ 2／4 cm 大半時間會飽和（腳滑到半徑邊緣）。
- 15 個 A 類事件：把 compliance 位移投影到「取樣點越過階緣的方向」——Walk 上樓腳尖 5 次往上翻 **+1.4～+2.9 cm（往更深處推）**；抬跟 2 次 −1.8～−4.0 cm（推離）；s1 #03 三次 +1.2、+1.4／+2.8、−1.0／−2.1。
- ⇒ 預測：compliance 在主要 A 類情境是**把腳尖推向 riser**，不是拉離；若實測如此，假設不成立。

### 24.8 裁決規則（事先寫定）
- **假設成立** 需要同時：M1 在 B 或 C 明顯下降且不是只剩抬跟、M5 不增加、M6／M7 不變差、M4 的滑動在單次鎖定內 ≤ r 且不在 idle 中持續累積、M9 相位差 0。
- **不成立** 任一：M1 不降或變深、M5 增加、M4 顯示 compliance 主要在吸收 root 滑行（M12 平移為主）⇒ **維持 XYZ hard lock**，停止，不在上面補 hysteresis／smoothing／特例。
- M12 若顯示偏移以「繞 heel 或 toe 旋轉」為主 ⇒ 下一步應是 pivot anchor，不是放大 ankle 半徑。

### 24.9 結果（Codex 實作＋6 組 × 104 次停步；Claude 審查）
Codex 報告：`.codex-temp/xzc/REPORT.md`（逐事件 M1 配對、r0 回退：改動前 211 檔中 209 檔逐 cell 相同，唯二不同為 `s1_Run_Up_11` 的整段 pose／root 分岔＝probe 可重複性問題）。實作審查：`ResolveHorizontal` r ≤ 0 走原運算式；r > 0 ＝ anchor ＋ `ClampMagnitude`；`StepLock` 解鎖距離仍量 anchor、釋放從 `LockResolvedPosition` 開始；支撐選擇／接觸／骨盆未動——**無偏離規格**。EditMode **765／764／0 failed／1 skipped**（+4）；probe 629／629。

| | rule 關 r0 | 2 cm | 4 cm | rule 開 r0 | 2 cm | 4 cm |
|---|---|---|---|---|---|---|
| M1 Locked step flip | 15 | **35** | **38** | 2 | 1 | 0 |
| M5 鎖定中取樣跨階緣（heel＋toe） | 89 | **193** | **213** | 89 | 193 | 213 |
| M2 飽和帧比例 | — | 51% | 34% | — | 51% | 34% |
| M4 每段鎖定累積腳滑 p95 | 0 | **8 cm** | **12 cm** | 0 | 8 | 12 |
| M3 動畫 − 解出 XZ p95 | 9 cm | 7 | 5 | 9 | 7 | 5 |
| M6 riser 側向侵入（帧；p95／max mm） | 0 | 0 | 0 | 137（8／13） | **1111**（17／28） | **1360**（34／36） |
| M7 真實穿透 > 1 cm／垂直-only／懸空 | 0／0／643 | 0／0／**1423** | 0／0／**1450** | 5／0／643 | **703**／0／1423 | **763**／0／1450 |
| M8 最終站姿目標 Y 與 r0 相差 > 5 cm | — | **9 腳**（max 19 cm） | **11 腳** | — | 0 | 0 |
| M9 相位差／支撐改變／契約違反 | — | 0／0／0 | 0／0／0 | — | 0／0／0 | 0／0／0 |
| M13 B 類 | 192 | 192 | 192 | 192 | 192 | 192 |
| M12 動畫偏移：平移主導 | **97%**（旋轉殘差 p95 2 cm） | | | | | |

- 顯示旋轉：只有 `s1_Run_Up_11` 有 52 格差異（該情境衝出樓梯頂、IK 關閉，r0 與改動前的比對也只在此檔不一致 ⇒ 模擬非決定性，與 compliance 無關）。
- M11：Locked→Releasing XZ 跳變 max **0**（六組各 234 次，只比相鄰帧）。⚠️ 更正：Claude 審查時一度量到 15–16.5 cm，是比較了 probe 記錄中不相鄰的兩列（`s1_Walk_Down_04` 幀 131370 → 131378，移動中離停步點較遠時 probe 不記錄）造成的假象，不是真的跳變。
- 事前預測（§24.7）成立：compliance 在 Walk 上樓把腳尖推向 riser。

### 24.10 裁決（依 §24.8 事先寫定的規則）：**維持 XYZ hard lock；Y hard ＋ XZ soft 不採用**
1. **剩下的整階跳不是 hard lock 造成的**。鎖定中動畫腳相對 anchor 的偏移 97% 是**整體平移**（heel／ankle／toe 等量）＝停步 clip 的 root 滑行——那正是鎖腳要抵消的東西，不是「合理的收步／pivot」。放開它，取樣跨階緣次數 **×2.2–2.4**、Locked flip **15 → 35–38**。
2. **只是把一種瑕疵換成另外兩種**：腳在半徑內來回滑（每段鎖定累積 8–12 cm）；最終站姿有 9–11 隻腳整階不同。riser-intrusion 規則開著時翻轉「消失」，但代價是 riser 側向侵入 137 → 1111–1360 帧（p95 17／34 mm、max 28／36 mm）、> 1 cm 真實穿透 5 → 703–763 帧、懸空 ×2.2。
3. **2 cm／4 cm 都只是數值**：兩者都大半時間飽和（51%／34%），動畫要的偏移 p95 是 9 cm——沒有任何半徑是「真正需要的活動範圍」，因為需求本身是 root 滑行。
4. **沒有證據支持改成 heel／toe pivot anchor**：旋轉殘差 p95 只有 2 cm；A 類翻轉的觸發是 < 1 cm 的查詢漂移（§21.4），已由 §23 的 riser-intrusion 規則處理（剩 2 次皆為抬跟，屬第三類）。
5. 依 §24.8：停止，不在 compliance 上補 hysteresis／smoothing／特例。

### 24.11 ❓ 待使用者
1. 移除 compliance 程式（`LockedXZComplianceRadius`、`LockResolvedPosition`、`StepLock` 分支、4 條測試），或保留為 r＝0 的死開關？（建議移除——假設已被否決，留著只是一條不會再走的路徑。）
2. 人工 Play 看到的翻轉：X Bot 的 `LockedRiserIntrusionRule`（§23）**預設未開**。若 Play 時沒勾，看到的就是 §20 的 A 類——請勾選後再看一次；若仍可見，需要影片或停步位置來定位是哪一類。

## 25. 收尾：移除 XZ compliance、X Bot 開啟 riser-intrusion rule、最小驗收（2026-09-30）

### 25.1 使用者裁決
① 移除已否決的 XZ compliance（§24），研究結論留在文件。② 以 Editor API 確認 X Bot 的 `LockedRiserIntrusionRule`；若關閉就開啟，再做最小驗收。不再改 T3 行為；仍能看到的整階 flip 只記錄與分類，不加新規則。

### 25.2 移除（程式回到 §23 狀態）
`FootIKSettings.LockedXZComplianceRadius`、`FootContactState.LockResolvedPosition`、`ResolveHorizontal` 的半徑參數、`StepLock` 的 resolved 釋放分支、`FootIKController` 的接線、Codex 的 4 條測試（`FootContactSolverTests.cs` 整份還原為 HEAD）、probe 的 compliance 參數。
EditMode **761／760／0 failed／1 skipped**（＝ §23）；驗收 39 次停步與 §23 規則開啟組的目標高度**逐帧相同（39／39）**⇒ 移除後 runtime 與 §23 逐位元一致。

### 25.3 X Bot 設定（Editor API）
- 讀取：prefab `LockedRiserIntrusionRule = False`、場景實例未覆寫 ⇒ **§23 之後的人工 Play 都是在規則關閉下進行的**。
- 修改：`PrefabUtility.LoadPrefabContents` → `SerializedObject` → `SaveAsPrefabAsset`。檔案 diff **只有新增 7 行**：`LockedRiserIntrusionRule: 1`，其餘 6 行是從未序列化過的欄位寫出其程式預設值（`StandingUseSupportTransfer: 1`、`LockedSampleFrameFreeze: 0`、MinRise 0.08／MaxRise 0.3／SupportTolerance 0.03／EdgeTolerance 0.004）——有效行為只變這一個開關。Y Bot 未動（False）。
- 還原：同一欄位設回 False。⚠️ 資產改動只有 Unity 匯入成功，沒有測試保護（驗收 probe 讀的是這個值，見 25.4）。

### 25.4 最小驗收（`Probe_RiserRuleAcceptance`：不覆寫開關，讀 X Bot 載入值；39／39 皆為 True）
| 驗收 | 規則關（基準） | 規則開（X Bot） |
|---|---|---|
| proto Walk 上樓：Locked 腳尖整階跳 | 10 次（5 次停步） | **0** |
| s1 Run 下樓 #03 腳跟階角：整階跳 | 3 次 | **0**；側向插入 median 3／p95 9／max 13 mm（t＝38–47 暫態，idle 1–3 mm） |
| Run 下樓抬跟：釋放類跳變 | proto 4、s1 5 | **4、5（逐項相同，含數值）**——沒有新的 release artifact |
| B 類（落腳） | 68 | 68 |

### 25.5 規則開啟後仍存在的整階 flip（記錄＋分類，⛔ 不處理）
| 事件 | 位置 | 類別 | 說明 |
|---|---|---|---|
| **腳尖落腳 1 帧尖峰** | proto Walk 上樓停步 #00／01／04／05／06／10／11 右腳；#04 為 stop 後 t＝36（幀 1618）↑ +18.4 cm、t＝37（幀 1619，鎖定當帧）↓ −17.9 cm | **B 類（Unlocked 落腳）** | 最後一步落腳、β 0.87、尚未 Locked；腳尖取樣點埋在上一階內 9 cm ⇒ 當成垂直穿透整段抬起。與 A 類同一幾何（側碰 riser），但發生在規則管不到的 Unlocked 階段。**最可能是規則開啟後肉眼仍會看到的那一下。** |
| 抬跟延後 2 帧 | proto Run 下樓 #10 左腳 t＝24（+16.0 cm） | 第三類（抬跟／離地） | 使用者既定：人工 Play 仍可見才另開 release slice |
| 抬跟 | s1 Run 下樓 #07 左腳 t＝26（+13.8 cm） | 第三類 | 與規則關閉相同 |
| 釋放中的 4–13 cm 跳變 | 各情境（例：proto Walk 上樓 #00 右腳 t＝3 +12.8 cm） | release 類（既有） | 規則開關前後逐項相同，不是本輪產生 |

### 25.6 ❓ 待使用者
1. 人工 Play（規則已開）：看 proto Walk 上樓停步最後一步落腳時腳尖是否有一帧上跳（25.5 第一列）——若可見，它是 B 類，需要另開 slice（本輪依裁決不加規則）。
2. §21 的 `LockedSampleFrameFreeze`（已否決、預設關）是否同樣移除？

## 26. 定位：階梯邊緣停下時「全身連腿抖一下」（2026-09-30；⛔ 只量測，規則未改）

### 26.1 問題與判斷分支（使用者）
人工 Play：階梯邊緣停下時偶爾全身抖一下（上樓很少）。先判斷是不是 foot target 整階跳經由 IK／pelvis 傳上去（是 ⇒ 另開 B 類 landing slice）；foot target 穩定才查 pelvis／stand handoff。不碰 riser rule、SupportTransfer、ω、prediction。

### 26.2 方法
- 規則開啟後的 104 次停步（與 §23 規則開啟組逐位元相同）：找「身體高度＝ root Y ＋ 骨盆偏移」的二次差分尖峰，與「硬可達上限一帧內降 ≥ 5 cm ∧ 骨盆 3 帧內降 ≥ 5 cm」的事件。
- 可重現事件以 `Probe_BodyJerkEvent` 重錄（讀 X Bot 載入的規則值），加記**只寫不讀**的觀測欄位：`MovingPelvisState.DebugLeft/RightLimit`、`DebugLeft/RightHard`、`DebugLeft/RightHipNext`（＋既有速度、硬夾旗標、腿長）。
- 同一事件以 StopHold（SupportTransfer 關）重錄對照——只量測。

### 26.3 可重現事件：s1 Run 下樓 #05，**第一個抖動帧 ＝ 停步後 t＝58**
| t | 骨盆實際／目標 | 速度 | 硬夾 | 總上限／硬上限 | 左（尾腳）上限／硬 | 右（Primary）上限／硬 | 狀態 | 左腳 | 右腳 |
|---|---|---|---|---|---|---|---|---|---|
| 55 | −13.8／−4.7 | +0.29 | | −21.1／−0.2 | −21.1／∞ | −4.7／−0.2 | SupportTransfer | **U** β.94 tgt 0.710 | L rel tgt 0.893 |
| 57 | −12.9／−4.8 | +0.28 | | −21.3／−0.3 | −21.3／∞ | −4.8／−0.3 | SupportTransfer | **U** β.95 tgt 0.708 | L tgt 0.893 |
| **58** | **−18.4**／−22.9 | **−10.89** | ✅ | −22.9／**−18.4** | −22.9／**−18.4** | −4.9／−0.4 | None（落定） | **L** tgt 0.707（鎖在 0.611） | L tgt 0.893 |
| 59 | **−26.8**／−23.0 | −6.65 | | −23.0／−18.6 | −23.0／−18.6 | −5.0／−0.5 | None | L 0.706 | L 0.893 |
| 60 | **−35.0**（下限）／−23.1 | 0 | | −23.1／−18.7 | | | Stand | L 0.705 | L 0.893 |
| 64 | −34.0／−23.1 | +0.21 | | | | | Stand（ω6 爬回） | L 0.705 | L 0.893 |

- 兩腳 T3 目標、三點命中（左 0.611×3、右 0.804／0.804／0.611）、選定支撐、LockPosition／LockedSupportY 全程穩定（左腳鎖定當帧 LockedSupportY 0.611 ＝ 即時命中）。髖位（動畫）1.818–1.819 不變。
- ⇒ **foot target 沒有整階跳；抖動全部來自骨盆**：3 帧內 −12.9 → −35.0 cm（每帧被 `PelvisSmoothSpeed` 8.3 cm 夾住），比約束需要的 −23 cm 多掉 12 cm，之後以 ω6 用 > 0.5 s 爬回。

### 26.4 機制（兩段）
1. **上升**：SupportTransfer 以 Primary（右腳）站直；尾腳 Unlocked 依規則②不往下拉。Run 下樓不在預測批准範圍，而尾腳此時**沒有在下降**（β 0.93 懸停）⇒ 不是 Landing 角色 ⇒ LiveLanding cap 也沒有（cap ＝ ∞）。骨盆升到 −12.9 cm，而尾腳鎖定後需要 −23 cm。
2. **下衝**：尾腳鎖定當帧成為 Support，硬上限一帧內 −0.3 → −18.4 cm ⇒ 硬夾；夾住時速度 ＝「硬上限的變化率」（`MovingPelvisSolver.cs` 第 390 行），但這次上限不是在移動，而是**約束集合換了一隻腳** ⇒ −10.9 m/s ⇒ 衝過約束到下限 −35 cm。

### 26.5 普遍性與對照
- 104 次停步：**Run 下樓 2／26**（s1 #05、proto #09，兩者同型：SupportTransfer 中尾腳 U→L）；Run 上樓 1／26（proto #05，放開當帧的一般硬夾，−28.8 → −35.0，無過衝，不同機制）；**Walk 0／52**。
- StopHold 對照（同一事件）：s1 #05 骨盆在尾腳鎖定時早已蹲在 −35（沒有上升 ⇒ 沒有下衝，鎖定前後平順）；proto #09 全程單帧最大 **0.6 cm**（SupportTransfer 為 8.3 cm）。⇒ 這次抖動是 **SupportTransfer 交接**的產物，硬夾的速度承接把它放大成過衝。
- §25 的 B 類腳尖尖峰（proto Walk 上樓 #04 t＝36）：那一帧右腳 Unlocked 且沒有在下降 ⇒ 沒有任何約束；骨盆全程 −29.5 cm 不動 ⇒ **不會傳成全身抖**（只有那隻腳自己跳一帧）。

### 26.6 結論與下一步（❓ 待使用者；⛔ 未修）
依使用者的判斷分支：foot target 穩定 ⇒ 屬於 **pelvis／stand handoff**，不是 B 類 landing slice。候選槓桿（不在本輪實作）：
1. **硬夾的速度承接**：約束集合改變（新的 Support 出現）時不應把上限的階躍當成速度 ⇒ 消掉 12–17 cm 的過衝（留下「一次夾到 −18.4」的 5–7 cm 單帧）。
2. **非批准停步的尾腳未鎖定前上升**（§19.5 已列的 fallback 問題）：Run 下樓尾腳懸停、不是 Landing 角色時，SupportTransfer 照樣站直 ⇒ 鎖定時才發現要蹲回去。
兩者都動到 SupportTransfer／骨盆求解器，須使用者開 slice 裁決。

### 26.7 產出（throwaway）
`MovingPelvisState` 的 `DebugLeft/RightLimit`、`DebugLeft/RightHard`、`DebugLeft/RightHipNext`（只寫不讀）；probe `Probe_BodyJerkEvent`。

## 27. 修正：硬夾的「約束加入／換腳」不是上限速度（2026-10-01；pelvis／stand handoff slice 第 1 項）

### 27.1 使用者裁決
開 pelvis／stand handoff slice，**只修第 1 項**（§26.6-1）。「同一 active constraint 的 limit 移動」與「新 constraint 加入／owner 切換」必須分開：新約束首次生效時位置可以立即夾到合法邊界；⛔ 不得把 `(newLimit − oldLimit)/dt` 當速度；速度保留原連續值，或只取消會繼續往非法方向穿越邊界的分量；⛔ 不得注入向下速度。
不改 SupportTransfer 站直時機、StopHold、預測、ω、T3、riser rule。第 2 項（無預測時尾腳落地前不站直）暫不做——只有在過衝消除後剩下的合法下修仍肉眼突兀，才另開 policy slice。

### 27.2 契約（`MovingPelvisSolver` 硬可達）
- 每帧記下硬上限的 **owner**（`hard ＝ min(leftHard, rightHard)` 由哪隻腳決定；0 ＝ 無）。
- **同一約束**：上一帧也有有效硬上限，且 owner 相同 ⇒ 上限在移動 ⇒ 夾住時速度 ＝ `min(v, (clampedHard − previousHardLimit)/dt)`（既有行為，`HardClamp_FollowsTheConstraintRate_NotTheClampedDistance` 保護）。
- **約束加入／換腳**：其餘情況 ⇒ 位置照樣夾到 `max(hard, floor)`；速度 ＝ `min(v, 0)`——只取消向上（往非法方向）的分量，不承接階躍。`DebugHardActivation` 標記這一帧。
- 新增狀態 `PreviousHardOwner`；EditMode：`HardClamp_NewConstraintActivation_IsLegal_WithoutInjectedVelocity_OrOvershoot`（重現 s1 #05：啟用帧位置合法、速度 > −0.5 m/s、之後 90 帧最低不低於需求 0.5 cm 以上）、`HardClamp_OwnerSwitch_BetweenActiveConstraints_DoesNotInjectVelocity`。

### 27.3 驗收
| | 修正前 | 修正後 |
|---|---|---|
| s1 Run 下樓 #05（尾腳鎖定 t＝58） | −12.9 → −18.4（夾，v −10.89 m/s）→ −26.8 → **−35.0**，再 ω6 爬回 | −12.9 → **−18.4**（夾，v **−0.07**）→ −18.7 → −19.0 → … 收斂到需求 **−23.1**，無過衝 |
| proto Run 下樓 #09（t＝58） | −6.4 → −13.9 → −22.2 → −30.5 → **−35.0** | −6.4 → **−13.9**（夾，v **−0.16**）→ … 收斂到 **−18.5**，無過衝 |
| 啟用當帧單帧下修（＝剩下的必要 landing correction） | — | **5.5 cm／7.5 cm** |
| 啟用後最大速度 | 10.9／11.0 m/s | 0.22／0.23 m/s |

104 次停步（riser rule 開／關兩組，共 208）：
- 骨盆有改變的停步**只有這 2 個**（開關兩組相同）；其餘 102 個逐位元相同——**Walk 52 個全部不變**。
- 兩腳 T3 目標 0 差異（唯一例外 `s1_Run_Up_11`＝既知的 probe 非決定性情境，姿勢本身就不同）。
- 停步後骨盆觸及下限 −35 的次數：proto Run 下樓 2 → 1（#09 消失）；其他情境不變（那些是煞車時的強迫下降，與本修正無關）。
- EditMode **763／762／0 failed／1 skipped**（+2）。

### 27.4 範圍外、已觀察到（⛔ 未處理）
**同一約束快速移動**仍按上限變化率承接速度：Run 上樓剛放開的 1–3 帧，Support 腳的硬上限一帧移動 7–10 cm（s1 #07：−22.6 → −32.8，v −6.1 m/s；proto #05：−3.7 m/s），骨盆被帶到下限 −35。這是「同一 owner」路徑，修正前後逐位元相同；是否算抖動需人工 Play 判斷。

### 27.5 ⏳ 人工 Play
s1 樓梯與 proto 樓梯 Run 下樓、在階梯中段停下（尾腳最後一步落在下一階）：看「只剩 5～7 cm 的 landing correction」是否仍明顯。還原＝`MovingPelvisSolver` 硬夾段改回單一 follow 路徑（刪 `PreviousHardOwner`／`DebugHardActivation` 與 else 分支）。

## 28. 研究：Walk 下樓「前腳下去時很彎 → 接地延後 → 後腳更彎、骨盆更低」（2026-10-01；⛔ 只量測）

### 28.1 使用者觀察
Walk 下樓維持下蹲；前腳往下一階時不應該很彎——它讓前腳接地延後，後腳彎更多、骨盆更低。

### 28.2 方法
throwaway `StopLandingProbe.Probe_WalkDescentLeg`：兩道樓梯，平地走近 → 上樓 → 轉身 → Walk 連續下完整段（不停步）→ 平地。逐帧記 IK 後骨骼膝角（UpperLeg／LowerLeg／Foot）、動畫膝角（pose 髖 → pose 踝，以骨長餘弦定理）、腳底懸空（`−DebugContactDelta`）、骨盆、兩腳角色與上限。X Bot 現行設定（riser rule 開）。

### 28.3 結果
**落腳鎖定當帧**（中位數）：
| | 前腳膝角 IK／動畫 | 後腳膝角 IK／動畫 | 骨盆 | 前腳懸空 t−10／−5／−1 |
|---|---|---|---|---|
| 平地 | 158°／152° | 125°／121° | −3 cm | 2.5／1.1／0 cm |
| 下樓（proto） | 120°／149° | **72°**／123° | **−28** | **12.4／8.0／5.0** |
| 下樓（s1） | 147°／146° | **72°**／121° | **−31** | **12.6／10.1／7.0** |

**每個下樓步的擺盪段**（7 步）：前腳最小膝角 IK **46–70°**（動畫 103–108°）；前腳在鎖定前約 7 帧就已**完全伸直（≥ 170°），仍懸空 15–20 cm**；該步最深 Landing 需求 **−27～−38 cm**；擺盪期間平均骨盆 −25～−34 cm；後腳最小膝角 **50–73°**（動畫 104–138°）。

### 28.4 因果（與使用者假設對照）
一步裡有兩件不同的事：
1. **擺盪中段「前腳很彎」**：骨盆整段維持在 ~−30 cm，但擺盪腳的目標高度仍是「移動面（階緣包絡）＋ 動畫離地高度」，不隨身體下降 ⇒ 髖到腳的距離被壓短 ~30 cm ⇒ 膝蓋折到 46–70°（動畫只有 ~105°）。**這是骨盆低的結果，不是原因。**
2. **落地前「接地延後」**：前腳在落地前已經伸直，卻仍差 15–20 cm 才搆到下一階 ⇒ Landing 約束要求骨盆 −27～−38 cm ⇒ 骨盆下沉、後腳（還鎖在上一階、整個腳掌平貼）被迫彎到 50–73°。上升 ω 1.5 在兩步之間回不來 ⇒ 整段維持下蹲。
⇒ 使用者的「接地延後 → 後腳更彎、骨盆更低」成立；但延後的原因是**伸直的腿也搆不到**（平地步幅 ＋ 一個階高 > 腿長），不是前腳彎。

### 28.5 可能的槓桿（❓ 待使用者選；⛔ 未實作）
| 槓桿 | 層 | 預期 | 疑慮 |
|---|---|---|---|
| **A 前腳掌先著地**：下樓落地腳蹠屈（腳尖先下） | T3 落腳旋轉／接觸點 | 有效腿長 ＋ 腳尖偏移 × sin θ（ToeOffset 0.15 m、θ 25–30° ⇒ ~6–7.5 cm）⇒ 接地提早、Landing 需求變淺 ⇒ 骨盆與後腳彎曲一起減少 | 只補 15–20 cm 缺口的一部分；改的是 T3 的落腳姿勢（與 riser rule、鎖定點相關） |
| **B 擺盪腳提早往落地階下降** | T3 擺盪參考 | 直接消除擺盪中段 46–70° 的折膝（使用者看到的「很彎」）；腳較早到達踏面高度 | 不解決「伸直也搆不到」⇒ 骨盆深度大致不變；目標變低 ⇒ Landing 約束可能更早收緊 |
| **C 後腳抬跟**（下樓時支撐腳腳跟離地） | Contact／T3 | 後腳膝角改善（50–73° 是腳掌平貼的結果） | §9 已記錄的 Heel Lift slice；不改骨盆需求（綁定的是前腳） |
| D 樓梯專用步幅／動畫 | 動畫資源 | 根本解 | 磁碟上沒有樓梯移動 clip（memory：Kubold 目錄已窮舉） |

建議順序：**A（量化它能縮掉多少 Landing 需求）→ 視結果加 B**；C 是獨立的後腳外觀問題。先做 A 的離線估算再決定是否開 slice。

### 28.6 產出
`Probe_WalkDescentLeg`（throwaway）；資料在 session scratchpad（未入 repo）。

## 29. 方向重估：骨盆「不該太平穩」——文獻、業界與 Codex 討論（2026-10-01；⛔ 未實作）

### 29.1 使用者
§28 的思路影響所有樓梯情境（Walk／Run × 上／下）。本質上一隻腳在彎時另一隻在伸，骨盆位移本來就不該太平穩。查相關技術並與 Codex 討論。

### 29.2 文獻與業界
- **下樓生物力學**：台階 > 10 cm 時人偏好**前腳掌先著地**；腳跟先著地時前腳必須伸到更低才碰得到 ⇒ **支撐（後）腳膝屈與力矩更大**（Sci Rep 2020，PMC7435172）——正是 §28 量到的鏈條。身體由**後腳屈膝**降下、前腳往前下方伸（J Exp Biol 2008）。⇒ 下樓時後腳大幅屈膝是正確的人體機制，要消除的是持續的極端蹲姿，不是讓後腳伸直。
- **UE5 Foot Placement**（AnimationWarping）`PelvisSettings`：`heel_lift_ratio`（達成姿勢時先抬跟還是先降骨盆，1＝先抬跟）、`max_offset`、`linear_stiffness／damping`、`max_offset_horizontal`（超過先 roll 腳、再滑）。⇒ 業界把「抬跟 vs 降骨盆」做成明確的分配策略。
- **Ubisoft IK Rig**：骨盆高度跟著最低的腳（防劈腿）；樓梯以 IK 從基本走路姿態程序化產生，承認一般 foot IK 在樓梯只到「可接受」。
- 程序化 locomotion 常見做法是相位驅動的有界 bob（sin 波、振幅隨速度）——偏表現，不是生物力學。

### 29.3 Codex 意見（摘要；thread `01a0f7b3-…`）
1. **不衝突**：§7–§8 否決的是「支撐軌跡直接當骨盆目標、每步 20–25 cm」。中間解＝**Reach 約束是硬安全包絡；步態相位只在包絡內提供有界的每步回升預算**：`target ＝ min(phaseIntent, reachEnvelope)`，回升有幅度／速度／加速度上限，新約束永遠可往下覆寫。建議判準：Walk 實際骨盆每步峰谷 4–8 cm（>12 淘汰）、Run 6–10 cm（>16 淘汰）；前腳擺盪膝角 p10 ≥ 90° 且與動畫差 ≤ 15–20°；後腳 p10 ≥ 80°；前腳伸直時離地 ≤ 5 cm、距接觸 ≤ 2–3 帧；reach loss ≤ 1 cm。
2. **前腳掌著地與後腳抬跟都屬 T3 接觸層**，骨盆只消費 T3 公布的可達姿勢。直接旋轉 Locked 腳會和固定的 `LockPosition` 衝突（繞前掌轉時腳踝必須移動）⇒ 需要明確的 `ToeContact`／`RollingSupport` 子相位或 contact pivot；`LockedSupportY` 仍表示踏面高度；riser-intrusion 必須檢查整個 roll 掃掠體積。
3. **擺盪參考系該改，但不能只讓目標跟骨盆一起降**：拆成 `SwingPoseTarget`（外觀，早中段 hip-relative clearance）與 `LandingReachTarget`（算骨盆約束）；否則 Landing 會更早收緊、反而更蹲 ⇒ Landing 角色需要 landing-commit／預計接觸時間 gate，不能只靠「Unlocked ∧ 下降中」。
4. **瓶頸**：Walk 下樓＝平腳落地有效腿長不足；Walk 上樓＝後支撐腳全掌鎖定、缺 heel roll；Run 上樓＝既知 T3 後腳釋放過晚（上限單帧移動 7–10 cm，§27.4）；Run 下樓＝完整步樣本不足。
5. **風險**：用單一垂直骨盆自由度解 3D 不可達問題——toe-first 只補 6–7.5 cm，缺口 15–20 cm，剩下要靠步幅／水平落點／有限的水平骨盆位移；現有量測只看 `PelvisOffsetY`，**沒量最終的世界骨盆**（動畫本身已有 bob，可能重複加）；`min(left,right)` 的 soft limit owner 切換仍可能造成 jerk（§27 只處理了 hard limit）。

### 29.4 Claude 判斷
- 同意「硬包絡 ＋ 有界相位回升」作為骨盆的目標模型，取代「固定 ω_up 1.5 累積記憶」——它直接回應使用者的「不該太平穩」，又不回到 §7 的 20–25 cm。
- 同意分層：腳的姿勢（前掌著地、抬跟、擺盪外觀）是 T3 的事；骨盆只做可達性 ＋ 有界起伏。**順序上必須先 T3、後骨盆**，否則骨盆會一直在補 T3 的幾何缺口。
- Codex 指出的**量測缺口是對的**：§12–§28 的「骨盆」都是 `PelvisOffsetY`（修正量），不是世界骨盆；判準（峰谷 4–8 cm）必須量最終骨骼。
- ⚠️ `ToeContact`／`RollingSupport` 會改 T3 的 Locked 合約（LockPosition 不再等於腳踝）——這是跨子系統（T3 ↔ 骨盆 reach）的契約變更，依 CLAUDE.md 判準應先寫 **Trial ADR** 再實作。

### 29.5 建議的下一步（❓ 待使用者）
1. **先量、不改**：四情境（Walk／Run × 上／下）phase-binned 基準——**世界骨盆**（Hips 骨骼）每步峰谷與加速度、兩腳膝角分布（擺盪／支撐）、前腳伸直到接觸的帧數與懸空、reach loss；補完整的 Run 下樓樣本。用 29.3-1 的判準標出每個情境哪裡不合格。
2. 依量測結果決定第一個 T3 slice（預期是 Walk 下樓前掌著地；先寫 Trial ADR：接觸子相位與 LockPosition 語意）。
3. 骨盆的有界相位回升最後做。

## 30. 最終骨骼基準（四情境＋平地；2026-10-01；⛔ 只量測）

### 30.1 使用者要求
先量 **final bone**，不要再只量 `PelvisOffsetY`——否則「Walk 應該 4–8 cm」之類的比較都沒有意義。

### 30.2 量測方法與驗證（`StopLandingProbe.Probe_FinalBoneBaseline`，throwaway）
- **時間點**：記錄元件 `[DefaultExecutionOrder(32000)]`。`OnAnimatorIK` 讀 **FK**（本帧動畫，IK／骨盆偏移尚未套用）；`LateUpdate`（最後執行）讀**最終**骨骼＝畫面實際畫出的姿勢。舊 probe 在協程讀骨骼，拿到的是**上一帧**姿勢、與本帧 root 差一帧——已棄用。
- **驗證**：最終 Hips 相對模型的局部位移 −FK ＝ **上一帧** `PelvisOffsetY`（誤差 ±0.01 cm）；剩下的差異全部是 **root 在 `OnAnimatorIK` 之後才移動**（MotionDriver／Pipeline 在 LateUpdate 移動角色：樓梯上 Walk ±2.2、Run ±4.5 cm／帧，平地 ≈ 0），扣掉後殘差 ≤ 0.02 cm。⇒ 量測可信。
- **起伏**：每段（平地／上樓／下樓）以 Hips 高度對水平位置做線性去趨勢，再取每步（任一腳鎖定起點之間）峰谷。斜面上 root 的等速移動被去趨勢扣掉。
- **樣本**：模擬是決定性的（同路線重複三趟逐帧相同），改以起點錯開 0／0.2／0.4／0.6 m 取得不同步伐相位；兩道樓梯 × Walk／Run × 4 起點 ＝ 16 次。
- 「搆不到」＝套用中的腳目標（上一帧目標 ＋ root 本帧 IK 後的位移）超出腿長（骨長和）> 1 cm。⚠️ 腳目標是 Humanoid IK goal、與腳骨差 3–4 cm（§17），**只能相對平地基準比較**。

### 30.3 結果（膝角＝內角，180＝伸直；p10／p50）
| 步態 | 段 | 樓梯 | 步數 | 每步峰谷：最終 Hips／FK Hips／Offset（中位／p90 cm） | a95 最終／FK（m/s²） | 擺盪膝 IK／FK | 支撐膝 IK／FK | 鎖定前 3／1 帧懸空 | 搆不到 | 平均 Offset |
|---|---|---|---|---|---|---|---|---|---|---|
| Walk | 平地 | — | 17 | **6.5／7.5**；6.7／7.3；0.4／0.6 | 14.0／13.7 | 113／148；113／148 | 148／155；149／162 | 0／−0.2 | 0% | −3.1 |
| Walk | 上樓 | proto | 12 | **9.2／19.7**；6.3／6.6；5.4／15.0 | **27.4**／13.3 | **69**／86；113／149 | **77**／121；150／162 | 1.1／1.2 | 3.5% | **−29.2** |
| Walk | 上樓 | s1 | 5 | **14.7／19.3**；5.7／7.7；9.2／14.3 | **29.9**／13.7 | **65**／87；113／150 | **81**／141；155／162 | −2.1／−1.5 | 9.5% | **−30.2** |
| Walk | 下樓 | proto | 13 | 7.0／8.0；6.7／6.8；1.9／3.3 | 13.0／13.7 | **61**／96；113／150 | **81**／100；149／162 | **10.8／10.3** | 3.3% | **−27.6** |
| Walk | 下樓 | s1 | 7 | 6.7／7.1；6.5／7.9；2.2／2.4 | 20.2／19.4 | **54**／87；112／150 | **73**／90；148／162 | **9.6／9.2** | 4.8% | **−32.5** |
| Run | 平地 | — | 10 | **11.6／12.2**；11.7／12.5；0.2／0.8 | 40.7／36.0 | 70／128；70／130 | 119／131；123／136 | 1.2／−1.9 | 8.7% | −3.7 |
| Run | 上樓 | proto | 10 | 10.5／11.5；12.3／12.5；2.2／3.6 | **73.8**／32.8 | **69**／76；68／130 | **74**／98；122／132 | 1.9／−2.0 | **22.7%** | **−33.5** |
| Run | 上樓 | s1 | 6 | 10.8／11.8；12.4／12.8；2.0／4.9 | **65.2**／32.8 | **72**／86；68／130 | **72**／105；122／132 | 1.5／−7.4 | **28.0%** | **−33.8** |
| Run | 下樓 | proto | 10 | 10.1／14.3；10.1／13.9；4.5／4.9 | 38.2／37.4 | **45**／87；69／129 | **72**／88；122／136 | **14.4／10.2** | 1.0% | **−25.0** |
| Run | 下樓 | s1 | 7 | 10.7／11.3；9.9／10.7；4.3／4.7 | 40.0／39.2 | **47**／87；69／130 | **70**／85；122／132 | **16.1／7.8** | 2.0% | **−27.8** |

### 30.4 這組基準改變了什麼
1. **「骨盆太平穩」在世界座標下不成立**：動畫本身就帶起伏（平地 Walk 6.5、Run 11.6 cm／步），而且穿透骨盆偏移照樣出現在最終骨骼。Walk／Run 下樓與 Run 上樓的最終起伏都 ≈ 動畫本身。§29 Codex 的「Walk 4–8 cm」平地動畫就已滿足——**判準必須以 FK（動畫本身）為基準的相對值來定**，不是絕對值。
2. **Walk 上樓反而太顛**：最終 Hips 每步峰谷 p90 ≈ 20 cm（動畫 6–8），骨盆偏移本身每步擺 5–15 cm；a95 是動畫的 2 倍。
3. **Run 上樓有尖銳加速度**：起伏幅度正常，但 a95 65–74（動畫 33）⇒ 短時間大跳動（與 §27.4「同一 Support 腳硬上限一帧移動 7–10 cm」吻合）；搆不到比例是平地的 2.6–3.2 倍。
4. **四個樓梯情境的共同問題是「整體下蹲」，不是起伏**：平均骨盆偏移 −25～−34 cm（平地 −3）；支撐膝 p10 70–81°（動畫 122–155°）、擺盪膝 p10 45–72°（動畫 68–113°）。
5. **下樓（Walk 與 Run）都有接地延後**：鎖定前 3 帧腳底仍離踏面 10–16 cm（平地與上樓 ≈ 0–2 cm）。
6. 範圍外記錄：root 在 IK 之後才移動 ⇒ 樓梯上畫出來的腳相對 IK 目標每帧偏 Walk ±2.2、Run ±4.5 cm（上樓偏高、下樓偏低）。

### 30.5 修正後的判準建議（❓ 待使用者確認）
- 起伏：最終 Hips 每步峰谷 **≤ FK ＋ 3 cm**（Walk ≈ ≤ 10、Run ≈ ≤ 15），a95 **≤ FK × 1.5**。
- 下蹲：平均骨盆偏移、支撐膝 p10 與 FK 的差距（目前 −30 cm、差 ~45–75°）作為主指標。
- 接地：下樓鎖定前 3 帧懸空 ≤ 平地基準 ＋ 2 cm。
- 「伸直到落地 ≤ 2–3 帧」（§29 Codex）**不採用**：平地動畫本身就在接觸前約 25 帧伸直。

## 31. 追查：−25～−34 cm 的骨盆下壓是誰要求的（2026-10-01；⛔ 只量測＋離線重播）

### 31.1 使用者要求
先追查骨盆下壓是誰要求的，再決定要不要做 toe-first／heel-lift。

### 31.2 方法
`Probe_FinalBoneBaseline` 的記錄器加讀求解器狀態（Offset、目標、總上限、硬上限、回升模式、速度、兩腳角色與各自上限、外插髖位、腿長、腳在行進方向上的前後）。一致性：求解器 Offset 與 `PelvisOffsetY` 差 ≤ 0.03 cm。
- 「要求」＝本帧 `min(0, 目標)`；「記憶」＝要求 − 實際（實際比要求更低的部分＝回升慢留下的）。
- 綁定者＝兩腳上限較低者，分 前／後（行進方向）× Support（Locked）／Landing（下降中的 Unlocked）。
- 離線重播：用錄下的每帧兩腳上限照 `MovingPelvisSolver` 的移動中公式重播（臨界阻尼、下限 −35、硬夾含 §27 owner 規則）；不修改時逐帧誤差中位 ≤ 0.17 cm。腳踝抬高 Δ ⇒ 該腳上限放鬆 Δ（`LegBound` 對腳踝高度是 1:1）。最終 Hips 起伏以「實際最終 − 上一帧實際偏移 ＋ 上一帧重播偏移」重建。

### 31.3 誰要求（平均，cm）
| 情境 | 實際 | 此刻要求 | 記憶 | 要求 < −5 cm 時的綁定者（占全部帧） | 該綁定者需求 中位（p10） |
|---|---|---|---|---|---|
| Walk 上樓 | −29～−30 | −10～−11 | **19** | **後腳 Support** 28–33%、後腳 Landing 11–15% | −23～−28（−35～−42） |
| Walk 下樓 | −28～−33 | −13～−14 | **14–18** | **前腳 Landing** 45–52%、前腳 Support 10% | −19～−24（−29～−36） |
| Run 上樓 | −34（貼下限 −35） | −8～−9 | **25–26** | 前腳 Support 19–21%、**後腳 Support** 14–15% | 後腳 −35～−44（**−46～−55，超過下限**） |
| Run 下樓 | −25～−28 | −3～−4 | **22–23** | 前腳 Landing 14–18% | −11～−14 |
| 平地 Walk／Run | −3／−4 | −1 | 2–3 | 幾乎沒有（2–5%） | — |

⇒ **下蹲的一半以上不是「此刻有人要求」**：約束只在每步某一段要求很深，骨盆以 ω_up 1.5（時間常數 ~0.7 s，比一步還長）回升，下一個峰值前回不去，深度累積成「記憶」。

### 31.4 槓桿的離線估算（平均骨盆偏移 cm；最終 Hips 每步峰谷 中位／p90；a95）
| | Run 下樓 | Run 上樓 | Walk 下樓 | Walk 上樓 |
|---|---|---|---|---|
| 現況 | −26.3；10.4／14.4；40 | −33.5；10.0／11.9；74 | −30.0；6.9／8.0；18 | −29.7；13.4／19.5；29 |
| 後腳 heel-lift 腳踝 +5 | −26.0 | −33.0 | −29.9 | −28.9；13.2／18.6；26 |
| 後腳 heel-lift 腳踝 +8 | −25.7 | −32.7 | −29.9 | −28.9 |
| 前腳 toe-first 腳踝 +6 | −26.0 | −33.5 | **−26.9**；7.2／8.6；19 | −29.7 |
| 回升 ω 3 | −19.5；9.8／14.0；41 | −31.8；**a95 178** | −26.5；6.8／9.0；19 | −26.8；**16.8／24.8**；37 |
| 回升 ω 6 | **−13.2；10.4／13.8；44** | −28.5；**a95 201** | **−21.4；7.7／11.3；22** | −22.1；**25.5／29.3**；44 |
（動畫本身每步峰谷：Run 下樓 10.2、Run 上樓 12.5、Walk 下樓 6.6、Walk 上樓 6.3）

### 31.5 結論
1. **toe-first／heel-lift 都不是下蹲的主槓桿**：在現有求解器動態下最多改善 3 cm（只有 Walk 下樓的 toe-first 有感），因為深度主要是記憶，不是峰值。⇒ **暫不做**，除非先處理記憶。
2. **下樓（Walk／Run）**：下蹲主要是記憶 ⇒ 回升動態是主槓桿。Run 下樓 ω 6 可從 −26 降到 −13，最終起伏與加速度幾乎不變；Walk 下樓 ω 3 起伏不變（−30 → −27），ω 6 再深一點的改善（−21）代價是起伏 p90 8 → 11 cm。
3. **上樓（Walk／Run）**：需求本身每步週期性地很深，來自**後腳 Support**（後腳鎖在下一階、髖隨 root 沿斜面上升）。回升越快骨盆越貼著需求擺動 ⇒ Walk 上樓起伏 p90 20 → 29 cm、Run 上樓 a95 74 → 201。heel-lift ＋5 cm 不夠（需求中位 −23～−44）。Run 上樓的後腳需求超過骨盆下限。
4. ⚠️ 未定論：上樓後腳綁定時，動畫腳骨比 IK 後腳骨高 22–34 cm，但這個差距包含「root 沿斜面、腳實際在下一階」的落差；綁定時 β 0.87–0.92（T3 判斷仍是支撐期）⇒ **不能據此判定是 T3 釋放太晚**。要分清楚需量「動畫腳相對移動面的離地高度」。

### 31.6 建議（❓ 待使用者）
- **下樓**：開一個「回升動態」slice——依方向／情境分開回升速度（下樓與上樓不共用 ω_up 1.5），先以離線重播選值，判準用 §30.5（起伏 ≤ FK ＋ 3 cm、a95 ≤ FK × 1.5）。這是骨盆層的事，不動 T3。
- **上樓**：先做一輪量測，把後腳 Support 的深需求拆成「斜面 vs 踏面落差」與「動畫已抬腳但仍被鎖」兩部分，再決定是 T3 釋放時機、身體高度參考，還是接受。
- toe-first／heel-lift：等上面兩項做完、記憶消除後，再用同一套重播重新評估。

## 32. 上樓深約束的來源拆解（2026-10-01；⛔ 只量測，ω／toe／heel 不改）

### 32.1 使用者裁決
下樓可以進「回升動態」slice；上樓暫時不改 ω、不先改 toe／heel，先把每次深 constraint 的來源拆乾淨。

### 32.2 拆法（精確恆等式）
Locked 腳：`T3 目標高 ＝ 移動面 refY ＋ 動畫離地高 predH ＋ 接觸修正 lift`（實測誤差 0.000 cm）；`LegBound` 對腳踝高度 1:1（重算 vs 記錄最大差 0.019 cm）。因此
`需求 ＝ 動畫本意需求（腳在 refY＋predH 時）＋ lift`，`lift ≈ −(refY − 踏面) − (predH − 腳底高) ＋ 殘差`。
事件＝同一隻 Support 腳連續綁定且需求 < −5 cm，取最深帧。⚠️ 錯誤更正：初版分析用了「下一帧才套用的目標」（`TgtY`），與求解器用的本帧目標不同，造成最大 23.7 cm 的不一致；改用本帧目標後驗證通過。

### 32.3 結果（每事件中位，cm；括號＝占總需求比例）
| 情境 | 事件 | 需求 | 動畫本意 | 斜面 vs 踏面 | 動畫抬腳被按住 | 水平距離／腿長 |
|---|---|---|---|---|---|---|
| **Walk 上樓 後腳** | 32 | −36.5 | **−36.1（102%）** | +0.4（−2%） | −4.5（12%） | 0.51 |
| **Run 上樓 後腳** | 28 | −45.4 | **−45.6（102%）** | +2.3（−4%） | −5.3（9%） | 0.49 |
| Walk 下樓 前腳 | 22 | −20.4 | −11.7（69%） | −8.6（34%） | 0 | 0.16 |
| Walk 下樓 後腳 | 16 | −6.7 | ＋4.7 | −11.6 | 0 | 0.22 |
| Run 下樓 前腳 | 14 | −7.9 | ＋6.3 | −14.2 | 0 | 0.07 |

**「動畫本意」再拆**：扣掉「root 底下移動面 − 腳底下移動面」的高度差後（帧中位）：
| | 動畫本意 | root−腳 斜面高差 | 扣掉後 | 腳在 root 前方 |
|---|---|---|---|---|
| Walk 上樓 後腳 | −24.6 | 24.1 | **−1.3** | −0.30 m |
| Run 上樓 後腳 | −39.5 | 36.7 | **−4.3** | −0.35 m |
| Walk 平地 後腳（基準） | −11.7 | 10.2 | −1.6 | −0.29 m |
| Run 平地 後腳（基準） | −8.5 | 6.3 | −2.6 | −0.40 m |

### 32.4 結論
上樓的深約束 ≈ **90% 斜面幾何**：平地動畫在支撐後段把後腳放在 root 後方 0.30–0.35 m，樓梯上那個位置的地面比 root 底下低 24–37 cm（動畫以為在同一平面）⇒ 腿長不夠、骨盆被拉下。
- **不是** 踏面離散化（斜面 vs 踏面 ≈ 0）。
- **不太是** T3 釋放太晚（動畫已開始抬腳的部分 4.5–5.3 cm、≈ 10%）——§31.5-4 的疑點由此結案。
- heel-lift（腳踝 ＋5–8 cm）只能補其中一小塊；能動到這個量級的只有改變「後腳在支撐後段離 root 多遠」（步幅／斜面上的 stride warping、或在需求超過門檻時讓後腳提早離地）。這些都是 T3／步態層的決定，⛔ 本輪不做，待使用者開 slice。

## 33. 下樓回升動態 slice（2026-10-01；⏳ 待使用者裁決停步副作用；⛔ 未 commit）

### 33.1 使用者裁決
下樓可以進「回升動態」slice；上樓暫不改 ω、不先改 toe／heel（§32 只拆來源）。

### 33.2 實作
- `MovingPelvisTuning.DescendRiseOmega`／`FootIKSettings.MovingPelvisDescendRiseOmega`（**6**）：移動中（非停步語意）、`Step` 回升、root 垂直速度 < −`DescendingStopSpeed`（0.2 m/s）時，回升角頻率用它取代 `RiseOmega` 1.5。停步語意（StopHold／Stand／SupportTransfer）、TerrainExit、下降（DropOmega）都不受影響。`DebugDescendingRise` 標記。
- 選值：以最終骨骼重播（§31.2）掃 ω 1.5–8，判準 §30.5（最終起伏 中位與 p90 ≤ FK ＋ 3 cm、a95 ≤ FK × 1.5）：Walk 下樓 ω ≤ 6 通過、ω 8 不通過；Run 下樓到 ω 8 都通過 ⇒ 取 6（與 Stand／TerrainExit 相同）。
- 還原：`MovingPelvisDescendRiseOmega` 設成 1.5（＝ `MovingPelvisRiseOmega`）。X Bot 未序列化此欄位 ⇒ 使用程式預設 6。
- EditMode **766／765／0 failed／1 skipped**（+3：下樓用新 ω；平地／上樓與 ω 值無關（逐位元）；停步語意不受影響）。

### 33.3 移動中驗收（最終骨骼，16 次；前 → 後）
| | 平均偏移 | 最終起伏 中位／p90（FK） | a95 | 支撐膝 p10 | 擺盪膝 p10 | 落地前 3 帧懸空 |
|---|---|---|---|---|---|---|
| Walk 下樓 | −30.0 → **−21.9** | 6.9／8.0 → 7.6／10.3（6.7） | 15.8 → 21.6 | 76 → **89°** | 59 → 66° | 10.0 → 10.0 |
| Run 下樓 | −26.5 → **−14.8** | 10.5／14.2 → 10.4／13.1（10.1） | 39.6 → 43.2 | 70 → **92°** | 45 → 51° | 15.3 → 15.3 |
| 平地／上樓 | 逐帧相同（3,257 帧、0 差異） | | | | | |
與重播預測一致（Walk −22.2、Run −15.4）。落地前懸空不變（T3 問題，本 slice 不碰）。

### 33.4 ⚠️ 停步回歸（104 次 × riser rule 開，對照 §27 修正後）
- 上樓停步：逐帧相同（0／52）。T3 腳目標 0 差異。
- **下樓停步**：放開時骨盆更高（Run −24 → −9、Walk −29～−33 → −18～−24 cm），但煞車步的 Landing 需求仍要 −14～−32 cm（以前被移動中的「記憶」預先蹲好而看不出）⇒ 放開後要再降下去：
  | | 放開後 60 帧內總下降 中位（最大） | 單帧 ≥ 5 cm 的帧（全部 13 次停步合計） | 單帧最大 中位 |
  |---|---|---|---|
  | proto Run 下樓 | 1.0 → **15.1**（29.6） | 2 → **16** | 0.7 → **8.3**（速度上限，多在停步後 ~11 帧） |
  | s1 Run 下樓 | 5.4 → **18.6**（27.1） | 6 → **20** | 4.7 → 8.3 |
  | proto Walk 下樓 | 0.3 → 7.6（11.2） | 0 → 4 | 0.5 → 2.5 |
  | s1 Walk 下樓 | 0.4 → 6.7（10.1） | 0 → 3 | 0.6 → 0.9 |
  最低點與最終高度與之前幾乎相同。

### 33.5 ❓ 待使用者
1. 接受（Walk 下樓停步多一段 ~7 cm 的平緩下沉；Run 下樓停步多一段 15–19 cm、含一個 8 cm 的單帧）？
2. 只對 Walk 啟用（Run 下樓移動中的改善 −26 → −15 放棄）？
3. 保留，另開「下樓停步煞車」slice 處理放開後的下沉（停步語意／SupportTransfer 範圍）？

### 33.6 裁決（2026-10-02，使用者）
**保留** `MovingPelvisDescendRiseOmega` 6；下樓停步放開後的下沉**另開「下樓停步煞車」slice**（停步語意／SupportTransfer 範圍），交給下一個 session。
分析腳本已存進 repo：`docs/artifacts/pelvis-analysis/`（`baseline`＝最終骨骼基準、`attrib`＝下壓歸因、`replay`＝求解器離線重播、`decomp`＝深約束三段拆解、`stepflip_ab`＝停步 A/B；皆 `.py.txt`，路徑需改成新的 CSV 位置）。

## 34. 下樓停步煞車：放開後下沉的來源拆解（2026-10-02；⛔ 只量測，runtime 未改）

### 34.1 方法
- 下樓停步：2 道樓梯 × Walk／Run × 13 個起點，共 52 次，riser rule 開。現值（ω 6）用 §33.4 的 `Probe_StepFlip` 輸出；對照組用新的 throwaway `StopLandingProbe.Probe_DownStopBrakeControl`，同一批停步只把 `MovingPelvisDescendRiseOmega` 改回 1.5。對照組重現了 §33.4 的「改動前」數字（≥5 cm 的帧：proto Run 2、s1 Run 6）。
- 量的是 `PelvisOffsetY`（套用值）。這裡要回答的是求解器的時序，不是最終姿勢，所以用修正量即可（最終骨骼 ＝ FK ＋ 上一帧偏移，見 §30.2）。
- 離線重播 `docs/artifacts/pelvis-analysis/brake.py.txt`：用錄下的每帧事實重跑停步路徑，包含 StopHold／SupportTransfer／Stand、§27 owner 規則，以及 `PelvisSmoothSpeed` 每帧上限。與錄下的值相比，逐帧誤差中位 0.01 cm、p95 0.57 cm、最大 1.48 cm。

### 34.2 來源：放開後第一個 ≥ 5 cm 的單帧
| | 有事件的停步 | 是 §27 硬夾 activation 的 | 綁定腳 | 時點（放開後帧數） | 當時語意 |
|---|---|---|---|---|---|
| proto Run | 11／13 | **11／11** | 煞車步的腳，這一帧剛從 Unlocked 變 Locked | t＝12（9 次）、t＝28（2 次） | Primary 出現前（StopHold 路徑） |
| s1 Run | 11／13 | **11／11** | 同上 | t＝12（7 次）、t＝28（4 次） | 同上 |
| proto Walk | 4／13 | **4／4** | **尾腳**（Primary 之後才鎖定） | t＝17–33 | SupportTransfer |
| s1 Walk | 3／13 | **3／3** | 尾腳 | t＝14–25 | SupportTransfer |
| 對照 ω 1.5 | Run 7／26、Walk 0／26 | 全部 | 6 次＝煞車步的腳；1 次＝Primary（§27 的 proto #09） | t＝12（6）、t＝58（1） | — |

- **8.3 cm 是怎麼來的**：求解器在 activation 那一帧直接跳到硬上限，`FootIKController` 再用 `MoveTowards(…, PelvisSmoothSpeed 5 m/s)` 把套用值限制在每帧 8.3 cm 以內。⇒ 套用值會有 1–3 帧**高於硬上限**，也就是腿伸到 1.05 L 還碰不到踏面。這段 reach debt 最大 proto 16.8 cm、s1 22.8 cm（10／14 帧）；ω 1.5 時是 5.2／9.9 cm（1／8 帧）。
- 這不是新的機制。ω 1.5 時的少數事件也是同一型，只是 ω 6 讓幾乎每一次 Run 下樓停步都會遇到。

### 34.3 Run 的機制：需求在最後 2–3 帧才出現，時間窗只有約 10 帧
- 煞車步的腳在落地前約 4 帧以每帧約 12 cm 往下（T3 目標連續，沒有 pop）。它要落到比動畫腳低約 40 cm 的踏面（鎖定時 poseY − tgtY ≈ 44 cm）。鎖定時軟上限 −21～−46 cm，有 11／22 次低於下限 −35。
- 落地前的 Landing 約束幾乎沒有起到預告作用，原因有兩個：
  ①上限會放寬 κ·h（h 20 cm 時放寬 30 cm），到最後 2–3 帧才低於骨盆；
  ②Landing 角色**隔帧閃爍**。下降判定比的是 `SoleHeight`，而它是相對「腳正下方的地面」量的；腳跨過階緣，或那一帧地面射線沒打到（gY＝0）時，SoleHeight 會跳大，於是那一帧不算下降，角色就消失。
- 放開時的骨盆：ω 6 中位 proto −9、s1 −12 cm（ω 1.5 時是 −24／−27）。從放開到煞車步鎖定只有約 12 帧，而且 DropOmega 12 是從靜止起步 ⇒ 15–25 cm 的差距最後只能靠硬夾補上。
- ⇒ ω 1.5 時沒事，是因為移動中的「記憶」已經先蹲好了，**不是停步語意把這件事處理好了**。

### 34.4 Walk 的機制：規則②也把「正在下降落地的尾腳」排除了
- Primary 出現後進入 SupportTransfer，只有 Locked 腳能把骨盆往下拉（§19 規則②）。尾腳最後一步是 Landing（正在下降落地），它的上限 −29～−38 cm **提前 10 帧以上就已經算得出來**，但被排除在外。骨盆停在 −17 cm（ω 6 放開時本來就比較高），等尾腳鎖定才由硬夾一帧補 5–8 cm。
- `PredictedLanding` cap 只限制上升（規則④）。骨盆已經高於 cap 時，它不會起任何作用（例：cap −28～−43，骨盆 −17／−27）。

### 34.5 離線候選（重播，52 次下樓停步；n≥5 ＝ 單帧 ≥ 5 cm 的帧數合計；debt ＝ 套用值高於硬上限的帧數／最大值）
| 變體 | proto Run n≥5／單帧中位／debt | s1 Run | proto Walk n≥5／單帧最大 | s1 Walk |
|---|---|---|---|---|
| 現況（重播） | 17／8.3／10 帧 16.8 | 20／8.3／14 帧 22.8 | 4／8.3 | 3／6.1 |
| A 去閃爍（下降判定改看腳目標的世界高度；地面漏打時沿用上一帧） | 14／7.6／7 | 16／8.1／10 | 4／8.2 | 3／6.1 |
| B ＝ A ＋ Landing 投影到腳下地面（不放寬） | **7／4.0**／4 | 14／8.0／10 | 3／8.2 | 3／6.1 |
| B′ ＝ B 再加上 Landing 硬上限 | 34／8.3／24（更糟：太早、太深） | 33 | 23 | 12 |
| **C：SupportTransfer 中，正在下降落地的尾腳也能往下拉** | 15／8.3／10 | 19／8.3／14 | **0／2.5** | **0／4.4** |
| Oracle k＝8（完美預知鎖定帧的實際需求，提前 k 帧開始） | 5／4.6／1 | 10／8.1／9 | 6 | 4 |
| Oracle k＝12 | 2／1.6／1 | 7／4.8／9 | 5 | 2 |
| Oracle k＝20 | 2／1.6／1 | 6／2.4／9 | 1 | 2 |

- C 對上樓停步的影響：52 次中有 17 次改變，最大 0.9 cm。若要上樓逐位元不變，就要只在「下樓停步」時啟用（放開時 root 垂直速度 < −0.2 m/s，與 `StopUsesPrediction` 同一個鎖存）。
- Oracle 剩下的事件全部是同一型：放開後約 12 帧鎖定、需求觸及下限 −35，而骨盆從 −8～−13 起步。**就算完美預知，放開之後也沒有足夠的時間。**

### 34.6 結論
- **Walk**：來源是規則②連「正在下降落地的尾腳」也一起排除了。C（加上下樓停步閘）把 ≥5 cm 的帧從 7 降到 0、單帧最大 ≤ 4.4 cm，且沒有 reach debt。這等於改變 §19 規則②的解讀 ⇒ 需要裁決。
- **Run**：只改停步語意無法完整解決（見 oracle）。B 能讓 proto 改善一半，但 s1 幾乎沒變，而且會讓 Landing 在停步中變成第二種定義。要真正消除，骨盆必須在**放開之前**就比較低，而這屬於移動中 ω 的問題（§33 範圍，本 slice ⛔）；或者擴大停步預測（⛔ 範圍外，而且 oracle 上界顯示效果有限）。
- **獨立觀察**：每帧 8.3 cm 的上限本身就會造成 1–3 帧 reach debt（最多 17–23 cm）。要讓骨盆更平滑，代價就是腳碰不到踏面的帧數變多。

### 34.7 ❓ 待使用者
1. **Walk**：是否採用 C（只在下樓停步啟用；規則②改為「只排除 Releasing／上升中的擺盪，正在下降落地的尾腳照 Landing 約束」）？
2. **Run**：(a) 接受（Run 下樓停步保留 1–3 帧 8 cm 下沉，加上短暫搆不到）；(b) 採用 B（只在停步中；proto 改善一半、s1 有限）；(c) 重開 §33：Run 下樓移動中的 ω 調低（例如 3），用一部分記憶換停步——建議先做離線掃描再決定；(d) 擴大停步預測到 Run 下樓（範圍外，上界有限）。
3. Landing 角色閃爍（A）：移動中也存在（同一個 `FootLimit`）。單獨修效果很小，只在 B 裡才有意義——要不要另外登記？

## 35. 下樓停步：正在落地的尾腳也能往下拉（2026-10-02；Walk 採用、Run 暫不處理；⏳ 待人工 Play；⛔ 未 commit）

### 35.1 使用者裁決（§34.7）
- **Walk**：採用 C，**只在下樓停步啟用**。
- **Run**：⛔ 不採用 B（不在停步中再堆一套 Landing 定義）。先在遊戲鏡頭下看實際的 8 cm／1–3 帧有多明顯，再決定要不要重開 §33。
- **Landing 角色閃爍**：記為底層問題 WORKLOG **FU-14**，本 slice 不修。

### 35.2 實作（`MovingPelvisSolver`）
- 新鎖存 `StopDescending`：在放開那一帧決定，條件是 root 垂直速度 < −`DescendingStopSpeed`（0.2 m/s），**不分步態**。`StopUsesPrediction` 改為 `WalkGait ∧ StopDescending`，結果與原本相同。
- SupportTransfer 中，若 `DescendingTrailingLandingPulls ∧ StopDescending` 且尾腳是 **Landing**（Unlocked、有地面、正在下降），它的上限也併入 `down`（`DebugTrailingLandingPull` 標記）。Releasing／上升中的擺盪仍然不往下拉——**§19 規則②從「只有 Locked 腳能往下拉」收窄為「排除 Releasing／上升中的擺盪」，且只在下樓停步。**
- Run 下樓停步也會走到這條路徑（閘不分步態），但 Run 的事件都發生在 Primary 出現之前（§34.3），所以實際上幾乎不受影響。
- 開關 `FootIKSettings.StandingDescendingTrailingLandingPulls`（預設 true；X Bot 沒有序列化這個欄位 ⇒ 用程式預設）。**還原 ＝ 設成 false**，會逐位元回到 §34 的行為。
- EditMode **771／770／0 failed／1 skipped**（+5）：Walk／Run 下樓停步時，落地中的尾腳會往下拉；拉下 20 cm 時單帧 < 5 cm（DropOmega，不是夾下去）；平地／上樓停步不拉；Releasing 尾腳不拉；開關關閉時等於 §19 的行為。

### 35.3 runtime 驗收（`Probe_DescendingLandingPullAcceptance`，104 次停步，X Bot 載入的設定；對照 §33.4／§34 的錄製）
| | 骨盆有改變的停步 | T3 腳目標 | ≥5 cm 帧 | ≥3 cm 帧 | 單帧最大 | reach debt | 最終高度 |
|---|---|---|---|---|---|---|---|
| 上樓（4 組 × 13） | **0／52（逐位元相同）** | 0 差異 | 不變 | 不變 | 不變 | 不變 | 不變 |
| proto Walk 下樓 | 10／13 | 0 差異 | **4 → 0** | 6 → 0 | 8.2 → **2.5** | 0 → 0 | −12.0 → −12.0 |
| s1 Walk 下樓 | 9／13 | 0 差異 | **3 → 0** | 4 → 4 | 6.5 → **4.7** | 0 → 0 | −13.1 → −13.1 |
| proto Run 下樓 | 1／13（最大 2.3 cm） | 0 差異 | 16 → 16 | 21 → 20 | 8.3 → 8.3 | 10 → 10 | 不變 |
| s1 Run 下樓 | 2／13（最大 0.9 cm） | 0 差異 | 20 → 20 | 26 → 26 | 8.3 → 8.3 | 14 → 14 | 不變 |

- 與離線重播的預測一致（§34.5 的 C：proto 2.5、s1 4.4）。Walk 的總下沉中位從 7.9／6.1 變成 8.6／7.0 cm：深度一樣，只是更早開始、分散到更多帧。
- 先升後降（rise→drop）：改動前後大致相同（最大 10.6 → 11.0–11.3 cm，超過 1 cm 的停步數不變）。
- 平地停步：沒有在 runtime 量；放開時 root 垂直速度 ≈ 0，閘不會成立（EditMode 有測）。

### 35.4 ⏳ 人工 Play
1. **Walk 下樓停步**（兩道樓梯，在中段放開）：最後一步落地時，身體還會不會「頓一下」？骨盆應該在尾腳落地前就平順地降下去。
2. **Run 下樓停步**（本 slice 刻意沒有處理，用來決定要不要重開 §33）：放開後約 0.2 s（煞車步落地）時，身體一兩帧內下沉 8 cm 左右，同時腳短暫碰不到踏面（最多 17–23 cm）。在**遊戲鏡頭**下明不明顯？
3. 回歸：上樓停步、平地停步看起來與之前相同（上樓已逐帧驗證）。
