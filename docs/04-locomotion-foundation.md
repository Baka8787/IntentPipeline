# Locomotion Foundation — 輪 2 資產盤點與導入規劃（Kubold Movement Animset Pro）

> **定位**：規劃／分析文件（Planning Doc），對應 roadmap `docs/03-animation-roadmap.md` 輪 2。
> 本檔承載「資產盤點、Import Preset、Bake Strategy、Motion Feature Mapping、承載分析、工作拆分」——**不承載架構決策**。
> **流程紀律（使用者裁決 2026-07-21）**：先建立 Locomotion Foundation（把 Kubold 完整導入現有 Animation Runtime）→ 依資產**實際**結構再決定 Start／Stop／Pivot 承載方式（FSM State vs Presentation），**不預先拍板**。
> **真相流向**：本檔（planning）→ 實作後把 durable 結果（最終 preset、Bake 配置、Mixer/Transition 結構、若有承載決策）fold 進 Living Docs（`01-design-doc.md`／`02-dev-spec.md`），必要時開 ADR。
> **來源**：clip 清單由 7 支 FBX 的 `.meta`（`clipAnimations` takeName）完整抽出；官方對照 `MAP_Unity_v1.5_animlist.pdf`。

---

## 1. Animation Catalog（完整盤點）

- **來源真相**：FBX 子 clip 直引（§0.4 規則 0，AnimationClip 預設不可變）。7 支動畫 FBX + `Models/Dummy.fbx`（Kubold 骨架，非執行期資產）。
- **Rig**：主 FBX `animationType: 3`（Humanoid）、`avatarSetup: 1`（Create From This Model）。Humanoid clip 以肌肉空間儲存，**自動 retarget 到 X Bot 的 Humanoid avatar**——這是能用在現有角色上的關鍵前提，已滿足。
- **更新檔優先**：`_SprintFixed`／`_RunStrafeUpdate` 是 Kubold 的修正／更新包，與主 FBX 有同名 clip（如 `SprintFwdLoop`）。**同名時採更新版**（`_SprintFixed` 的 Sprint、`_RunStrafeUpdate` 的 `*_new` Crouch）。
- **命名規律**：`{Gait}{Dir}{Kind}{Angle}_{Foot}`。Gait＝Walk/Run/Sprint；Dir＝Fwd/Bwd/StrafeLeft/StrafeRight；Kind＝Loop/Start/Stop/Turn/Arch/Lean；Angle＝90/135/180；Foot＝`_L/_R`（起步 lead 腳）或 `_LU/_RU`（Left/Right foot **Up**＝停步/轉身的落定腳相）。

### 1.1 分類總表（全 7 FBX，標本輪範圍）

| 類別 | 代表 clip | 數量 | 循環 | 位移/旋轉 | 本輪 (輪 2) |
| --- | --- | --- | --- | --- | --- |
| **Idle（基準）** | `Idle` | 1 | ✓ | 原地 | ✅ 核心 |
| Idle 變體 | `Idle2`~`Idle6`（_Idles FBX） | 5 | ✓ | 原地 | ⏸ 延後（觀感選配） |
| **原地轉身** | `TurnRt90_Loop`/`TurnLt90_Loop`/`TurnRt180`/`TurnLt180` | 4 | 部分 | 純旋轉 | ✅ 核心 |
| **Walk 前向 loop** | `WalkFwdLoop` | 1 | ✓ | 位移 | ✅ 核心 |
| **Walk 前向 start** | `WalkFwdStart`(+`90_L/R`,`135_L/R`,`180_L/R`) | 7 | ✗ | 位移+旋轉 | ✅ Phase C |
| **Walk 前向 stop** | `WalkFwdStop_LU`/`WalkFwdStop_RU` | 2 | ✗ | 位移(減速) | ✅ Phase C |
| **Run 前向 loop** | `RunFwdLoop` | 1 | ✓ | 位移 | ✅ 核心 |
| **Run 前向 start** | `RunFwdStart`(+`90_L/R`,`135_L/R`,`180_L/R`) | 7 | ✗ | 位移+旋轉 | ✅ Phase C |
| **Run 前向 stop** | `RunFwdStop_RU`/`RunFwdStop_LU` | 2 | ✗ | 位移(減速) | ✅ Phase C |
| **Run 180 轉身** | `RunFwdTurn180_{L/R}_{LU/RU}` | 4 | ✗ | 位移+旋轉 | ✅ Phase C |
| **Sprint loop** | `SprintFwdLoop`（採 `_SprintFixed`） | 1 | ✓ | 位移 | ✅ 核心 |
| 曲線 locomotion | `Walk/RunArchLoop_{L/R}`, `*Loop_Lean{L/R}` | 8 | ✓ | 位移(弧線) | ⏸ 延後（轉向打磨） |
| Strafe / 後退 | `StrafeLeft/Right*`, `WalkBwd*`, `RunLt/Rt/Bwd*`, `RunStrafe*` | ~25 | 混 | 側向/後向 | ⏸ **延後＝需 2D Mixer（roadmap F2）** |
| Crouch（含 `_new`） | `Crouch_*`, `Crouch_Walk*_new` | ~30 | 混 | 位移 | ⏸ 延後（蹲伏系統） |
| Jump / Fall | `Jump_*`, `JumpWalk/Run*`, `FallingLoop*` | ~25 | 混 | Y 特徵 | ⏸ 延後（專案已有自研 Jump，見下方 §2 註） |
| Fighting | `Fists_*`, `*Punch/Kick/Hit/Knockdown`, `Death_*` | ~19 | 混 | — | ⏸ roadmap 輪 6 Combat |
| 互動/翻越/其他 | `ButtonPush/PickUp/PullLever/Throw*`, `Vault1m/Climb2m/Slide`, `SitChair*`, `Patrol*` | ~35 | 混 | 混 | ⏸ 各自後續輪 |

> 完整 clip 逐條清單見各 FBX `.meta` 的 `clipAnimations`（本表已窮舉分類，未省略類別）。

---

## 2. 輪 2 範圍界定（Locomotion Foundation）

**納入（前向 locomotion 核心）**：
- **Phase A 地基＋loops**：`Idle`、`WalkFwdLoop`、`RunFwdLoop`、`SprintFwdLoop`（Mixer 核心，直接替換現行 Idle/Move 1D Mixer，並以真實 Kubold 資產壓測導入/烘焙管線）。
- **Phase C 一次性動作**：Walk/Run 的 Start（含 90/135/180）、Stop（`_LU/_RU`）、原地轉身（Turn90/180）、Run 180 轉身。**承載方式在此階段依實測定案**。

**延後（記於 Backlog，本輪不動）**：
- **Strafe／後退**（~25）：需 2D 方向 Mixer＝roadmap **F2 Strafe 2D**，且連動 B11 門檻公式；前向地基穩固後再開。
- **Crouch／Jump／Fighting／互動／翻越**：各屬後續輪；其中 **Jump**——專案已有自研資料驅動跳躍（ADR-002：物理 launch + Bake Y 特徵），Kubold Jump 家族**暫不導入**，未來若要換素材再對照。
- **Idle 變體、Arch/Lean 曲線 locomotion**：觀感選配，主線穩定後再評估。

**理由**：roadmap 輪 2 主目標＝消除滑行感 + 起停/pivot；前向 locomotion 已涵蓋此目標的完整鏈路（loop→start→stop→turn）。側向/後退屬「移動維度擴充」（2D），與地基正交，先做會把 1D→2D 的 Mixer 決策提前綁死。

---

## 3. Import Preset（依 §0.4 Root Transform 矩陣分類）

**映射到現有 4 原型**（§0.4）——本輪**零新增 preset 類型**，全部複用既有先例：

| Kubold 類別 | §0.4 原型 | XZ Bake | Y Bake | Rot Bake | Loop Time | 執行期驅動 | 先例 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `Idle` | **Locomotion-原地** | ✅ | ✅ | ✅ | ✓ | procedural | Idle |
| `WalkFwdLoop`/`RunFwdLoop`/`SprintFwdLoop` | **Locomotion-位移** | ❌（抽出丟棄=原地化） | ✅ | ✅ | ✓ | procedural；烘焙採速度真相 | Walking、Fast Run |
| Start / Stop / Turn（一次性、位移+旋轉） | **烘焙曲線驅動** | ❌（採速度） | ✅ | ❌（採 yaw） | ✗ | SpeedCurve + RotationCurve | **Stand To Roll** |

- **Rig/Retarget**：維持 Humanoid；retarget 走 Humanoid→Humanoid 自動肌肉映射（Kubold 自帶 avatar 即可，無需 Copy X Bot avatar；若實測有比例滑步再議 Copy From Other Avatar）。
- **套用方式**：一律用 `MotionClipImportSOP` 工具套 preset（§0.4 規則 1，`ModelImporter.defaultClipAnimations` 覆寫，**禁手改 `.meta`**）。套用後有烘焙資產者**必須重烘焙**。
- **待確認**：`烘焙曲線驅動` preset 目前先例 Roll 是否已涵蓋「Rot Bake ❌ 採 yaw」對**大角度轉身**（Turn180、Start180）的正確性——Roll 轉幅小，Kubold Turn180 是半圈，需 Phase C 首個 turn clip 烘焙後實測確認 RotationCurve 合理（此為 §0.4 「先驗證再定調」的落點）。

---

## 4. Bake Strategy

**原則**：loop 取「代表速度」穩態值；一次性動作取「全曲線」。全部 clip 建 `MotionBakeData` 資產、直引 FBX 子 clip。

| Clip 群 | 主要烘焙輸出 | 用途 |
| --- | --- | --- |
| Loops（Walk/Run/Sprint） | `AutoAverageSpeed`（+ `SpeedCurve` 供穩定度檢視） | `MotionDriver.moveSpeed` 來源（最高速 clip）＋ Mixer 門檻推導（`threshold = speed_i / speed_max`，§3.2 資料流） |
| Starts | `SpeedCurve`（加速段）、`RotationCurve`+`RotationFinishedTime`（90/135/180 起步的轉向）、`EndPhase` | 執行期依曲線驅動起步位移/轉向；`EndPhase` 決定銜接哪隻腳進 loop |
| Stops | `SpeedCurve`（減速段）、`EndPhase`（`_LU/_RU`＝落定腳相） | 依曲線驅動減速；`EndPhase` 對齊停步後 Idle 的腳相 |
| Turns（原地/移動） | `RotationCurve`、`RotationFinishedTime` | 執行期套 deltaYaw 驅動轉身；`RotationFinishedTime` 防收尾抖動 |

**烘焙速度預期序**（實際值待烘焙量測，此處僅相對關係）：`Idle(0) < WalkFwdLoop < RunFwdLoop < SprintFwdLoop`。門檻由各 loop 的 `AutoAverageSpeed / SprintSpeed` 推導（設計師依公式手填，B11 決策：門檻是可調表現參數，不自動寫 Animancer `_Thresholds`）。

**新增烘焙 Stage（本輪捆綁，roadmap §2.1 / dev-spec §5 既定未完成項）**：
- **Foot Phase Curve**（`IBuildStage` 可插拔擴充）：現有 `MotionBakeData.EndPhase` 僅「動畫結束瞬間」單值；Kubold 的 Start/Stop 是**腳別特化**（`_LU/_RU`），要在 loop 進行中「當下哪隻腳著地」才能選對停步/起步變體 → 需**連續腳相曲線**（時間 → 哪腳觸地）。此 stage 亦為未來 Footstep（roadmap 輪 3）與 SynchronizeChildren 相位對齊的共用資料源。
  - 產出候選：`AnimationCurve FootPhaseCurve`（0=左觸地、1=右觸地，或雙腳高度差符號），供執行期查詢當前相位。
  - 偵測法：沿用 §4.3 「世界空間相對足跡」機制（雙腳世界 Y vs Rest Pose 基線），取較低腳為觸地腳。

---

## 5. Motion Feature Mapping（clip → 需要的特徵；缺口分析）

| 特徵（`MotionBakeData` 欄位） | 現況 | 本輪需求 | 缺口？ |
| --- | --- | --- | --- |
| `AutoAverageSpeed` / `GetRepresentativeSpeed()` | ✅ 已有（Walking/FastRun 在用） | Loops 速度真相 → moveSpeed + 門檻 | 無 |
| `SpeedCurve` | ✅ 已有（Roll 在用） | Starts 加速 / Stops 減速 | 無 |
| `RotationCurve` + `RotationFinishedTime` | ✅ 已有（Roll 在用） | Turns / 帶角度 Starts 的轉向 | 無 |
| `EndPhase`（`FootPhase` L/R） | ✅ 已有（enum 定義齊全） | Stop 落定腳相對齊 | 無（但需 Foot Phase Curve 供「進行中」查詢） |
| `TargetLocalDirection` | ✅ 已有 | （側向/後退才用）本輪前向不需 | 無（延後 F2 才用） |
| **Foot Phase Curve（連續腳相）** | ❌ **缺**（僅單點 EndPhase） | Start/Stop 腳別選擇 + 未來 Footstep/同步 | **是＝本輪唯一新增特徵** |
| Start/Stop 行進距離 | ❌ 無 | （Distance Matching 才用） | 延後 roadmap 輪 7，不在本輪 |

**結論**：本輪特徵層**幾乎全部複用**現有 `MotionBakeData`；唯一新增＝**Foot Phase Curve 烘焙 stage**（已在 roadmap 捆綁範圍內）。無需改 `MotionBakeData` 以外的資料契約。

---

## 6. 承載分析（Mixer / Transition / 新 State）——輸入資料，**不預先定案**

> 依使用者裁決：承載方式由 Phase A/C 導入後的**實測行為**定案，本節只把候選與判準列清楚。

| Clip 群 | 天然承載傾向 | 理由 | 是否需新 FSM State |
| --- | --- | --- | --- |
| Idle / Walk / Run / Sprint loop | **Mixer**（1D 速度） | 連續速度混合，直接擴充現行 Idle/Move `LinearMixer`（多鍵→一資產先例 v0.16） | 否（拓撲零改動） |
| Start / Stop / Turn | **Transition（Presentation）候選** | 一次性、事件觸發（開始/停止移動、急轉）、腳別特化——形態同 Roll（Facade 播一次性 Transition） | **待定**（見下方判準） |

**新 FSM State 的判準（Phase C 實測回答，不提前）**：
- 若 Start/Stop 只需「Facade 播一次性 Transition → 混回 loop Mixer」、**無狀態級鎖定/完成判定** → **Presentation 承載，FSM 拓撲零改動**（延續 Idle/Move 共用 Mixer 精神、design-doc §2.7「拓撲與模式無關」）。
- 若需要「停步期間鎖輸入直到腳踩定」「起步完成前不可打斷」等**狀態級語意** → 才評估新 State；但**「封鎖輸入」語意本屬 Arbiter（roadmap 輪 4）範疇**，不應提前塞進 locomotion 狀態（YAGNI + 依賴方向）。
- **Sprint 承載**待定：第 4 個 Mixer 節點（1D 續擴）vs 獨立段——依 Sprint 是否需獨立打斷規則決定。

**上一輪的傾向（僅記錄，非結論）**：B 為主（Presentation）、A 為輔（少量 State）。本輪以資產實測驗證或推翻。

---

## 7. 本輪工作拆分（重新規劃）

> 每階段結束為一個可 Play 驗收點；Phase A 綠燈才進 B/C。程式改動走既定路由（非架構→Living Docs；承載若升為架構決策→ADR）。
> **使用者裁決細化（2026-07-21）**：管線嚴格照序推進 **Catalog → Import → Bake → Motion Features → Locomotion Mixer → Parameter Calibration**（＝下表 A+B 展開）；Start/Stop/Pivot 承載（原 C3）**明確延到整個 Foundation 驗證完成後**才決定（見 §8.1 第 4 點）。本輪主目標＝完整 Foundation，不急著加動畫功能。

| Phase | 工作 | 產出 | 負責 |
| --- | --- | --- | --- |
| **A. 地基＋loops** | A1 Import Preset 套用（Idle→原地；Walk/Run/Sprint loop→位移）｜A2 建 loops `MotionBakeData`+烘焙（取速度真相）｜A3 擴充 Mixer 至 Idle/Walk/Run/Sprint、門檻由速度推導｜A4 Facade 映射 + `moveSpeed` source 接最高速 clip｜A5 Play：換速無滑步、相位同步 | Kubold loop locomotion 上線，替換現行 Idle/Move | A1/A2 資產側=使用者(SOP)；A3/A4 程式=我；A5 使用者實測 |
| **B. Foot Phase 烘焙** | B1 設計 `FootPhaseCurve` `IBuildStage`（§4.3 足跡機制）｜B2 烘焙 in-scope clips 腳相 | 連續腳相資料，供 Phase C 選腳別 | 我（stage 程式）+ 使用者（烘焙執行） |
| **C. 一次性動作＋承載定案** | C1 烘焙 Starts/Stops/Turns（SpeedCurve+RotationCurve+EndPhase）｜C2 以 Transition（Facade）實作起停/轉身，實測行為｜C3 **據實測定案承載方式**（→ 補 dev-spec/design-doc，必要時 ADR） | 起步/停步/pivot 上線 + 承載決策落地 | 我（程式）+ 使用者（烘焙/實測/裁決） |
| **D. 參數平滑（B9）** | 資產定形後：MoveSpeed SmoothDamp + 加減速曲線 | Game Feel 收尾 | 我 + 使用者調參 |

**延後（Backlog，本輪不動）**：Strafe/後退 2D Mixer（F2）、Crouch、Kubold Jump 對照、Fighting（輪 6）、互動/翻越、Idle 變體、Arch/Lean 曲線 locomotion、Distance Matching（輪 7）。

---

## 8. 已裁決與 Import 執行規劃（2026-07-21）

### 8.1 已裁決（使用者確認）
1. **範圍**：Foundation 全做（Catalog→Import→Bake→Motion Features→Mixer→Parameter Calibration 完整管線），前向 locomotion。Strafe/後退/Crouch/Jump/Fighting/互動 全數延後。
2. **速度分段**：**Idle / Walk / Run / Sprint 四段**（不硬補不存在的 Jog）。**原則：速度段數由動畫資產決定**——Foundation 忠實反映現有素材，不為預設規格硬湊。未來導入含 Jog 的資產時，透過 **Catalog → Bake → Mixer 擴充**即可（新增一個 loop child + 一個門檻），**不動 FSM 與整體架構**（Mixer 門檻是資料驅動的表現參數，非拓撲）。
3. **Retarget**：全部 Humanoid、retarget 到 **X Bot**（維持現有角色，不換成 Kubold Dummy）。
4. **承載紀律**：Start/Stop/Pivot 承載（FSM State vs Presentation）**延到整個 Foundation 驗證完成後**，依實際資產結構與動畫效果再定。本輪目標＝完整 Locomotion Foundation，不急著加動畫功能。

### 8.2 盤點發現（需在對應步驟處理）
- **F1 已裁決（2026-07-21）→ 四段**：Kubold 前向移動 loop 僅 3 支（`WalkFwdLoop`/`RunFwdLoop`/`SprintFwdLoop`），**無獨立 Jog**。使用者裁決**不硬補**——Foundation＝Idle/Walk/Run/Sprint 四段，忠實反映資產。落實 §8.1 第 2 點「速度段數由資產決定」原則；未來含 Jog 的資產走 Catalog→Bake→Mixer 擴充，不改架構。
- **F2（Import 步驟）SOP 工具粒度**：`MotionClipImportSOP` 目前「整檔套用」——但 Kubold 一支 FBX 內含多類型 clip（`MovementAnimsetPro.fbx` ~85 支混 Idle/Walk/Start/Turn/Jump/Crouch）。整檔套用會誤套全部。**Phase A 的 3 支 loop 先在 FBX Inspector 逐 clip 手設**（量少可行）；Phase C（25+ 一次性動作）時再評估把工具增強為「僅套用選取的子 clip」（Gate A 量大/易錯 ✓、Gate B 用 Unity 公開 API ✓，屆時提案）。

### 8.3 Import SOP — Phase A loops（Foundation 第一步，你在 Editor 執行）
在對應 FBX 的 Inspector → Animation 分頁 → Clips 清單，對下列 clip **逐支**設 Root Transform（依 §3 preset）：

| Clip | 來源 FBX | Preset | Root Transform 設定 |
| --- | --- | --- | --- |
| `Idle` | `MovementAnimsetPro.fbx` | Locomotion-原地 | Bake XZ ✅ / Y ✅ / Rot ✅；Loop Time ✅；Based Upon 全 Original |
| `WalkFwdLoop` | `MovementAnimsetPro.fbx` | Locomotion-位移 | Bake XZ **❌** / Y ✅ / Rot ✅；Loop Time ✅；Based Upon 全 Original |
| `RunFwdLoop` | `MovementAnimsetPro.fbx` | Locomotion-位移 | 同上 |
| `SprintFwdLoop` | `MovementAnimsetPro_SprintFixed.fbx` | Locomotion-位移 | 同上；此 FBX 僅 1 clip，可直接用現有 SOP 工具「Locomotion-位移」選單整檔套用 |

> 主 FBX 的 Idle/Walk/Run Loop 三支需逐 clip 手設（避免誤套其餘 ~82 支）。Retarget：三支 FBX 的 Rig → Animation Type = Humanoid、Avatar Definition 維持 Create From This Model（Humanoid 自動 retarget 到 X Bot）。

### 8.4 下一步
Import（8.3）為你在 Editor 的即時可執行步驟。你套用後，我接著出 **Bake 步驟**規格（loops 建 `MotionBakeData`＋烘焙取速度真相）與 **Foot Phase Curve stage**（Motion Features）的程式設計，並實作 **Mixer 擴充/Facade 映射/Calibration**。

（本檔為分析/規劃，除本檔外未改任何程式或資產。）
