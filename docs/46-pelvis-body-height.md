# 46 — Pelvis / Body Height 子系統重整（Phase 1：盤點＋離線設計）

> **狀態**：📝 研究／提案（2026-09-28）。⛔ **runtime 未改**、⛔ 未碰 stop 規則、⛔ 未碰工作樹中的 Predictive Planned Stop（WIP）。
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

## 9. ❓ 待使用者裁決
1. **是否採用 §6 統一模型作為 Moving 的實作基線**（先只換 Moving，stop 暫維持現狀）？
2. **bob 預算**：`ω_up` 取 1.5（較深、較平順）或 3（較高、髖加速度較大）？——只有 Play 能判斷。
3. **抬跟**：是否開 Contact 層 slice（T3 後腳前掌支點）？不做則骨盆用 `R-noHL`（Walk 上樓仍有 3% 搆不到）。
4. **Run 上樓 T3 釋放時機**：是否另開 slice？
5. **工作樹的 Predictive Planned Stop（WIP）**：暫停、保留或撤回？它是本檔要統一掉的第 7 個高度規則。

## 10. 限制
- 離線；未在 Unity 中驗證。抬跟假設 Contact 會真的做到。
- Run 下樓 steady 窗口裡沒有完整的一步（只能用 bob21）⇒ 低信心。
- 顯示髖加速度含動畫本身的上下運動（平地參照：Walk 10、Run 31–39 m/s²）。
- `κ`、`φmax`、前掌距離未調整；支撐約束用全腿長（1.0·L）⇒ 綁住時該腿打直（後腳膝 0°）。runtime 可能改用 0.98–0.995·L 保留一點膝彎（約多降 1–2 cm）。
- 平地也會變（統一模型沒有平地特例）：需回歸確認。

## 11. 重現
`Temp/StairProbe/*_r45_t3*.csv`（Unity 重啟會清空 `Temp/`，已知）＋ `docs/artifacts/pelvis_model_sim.py.txt`（改名 `.py`；需 numpy：`C:\Users\USER\anaconda3\python`）：
`python pelvis_model_sim.py main`（主表）／`sweep`（§7.3）。
