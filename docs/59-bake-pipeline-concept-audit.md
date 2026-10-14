# 59 — Bake Pipeline Concept Fragmentation / Semantic Duplication Audit（2026-10-14）

> **性質**：研究報告，**只讀不改**。runtime、Editor 腳本、測試、Unity 資產皆未動（`git status` 對 `Assets/ScriptableObjects/Motion` 為空，已確認）。
> **範圍**：Editor 端 `MotionBakeEditor`、`MotionFeatureAnalysis`、`FootPlantScheduleBaker`、`TraversalContactDerivation`、`AnimationBatchOps`；資料 `MotionBakeData`；runtime consumer `LocomotionModel`、`LocomotionStopSelector`、`JumpState`／`JumpStateParams`、`MotionDriver`（SpeedCurve 消費）、`FootIKController`（next plant）、`TraversalStateParamsSO`／`TraversalWarpPlan`。
> **方法**：每個 bake 欄位追 producer（哪個 stage 寫）→ consumer（實際 call site）；數值一律取**出貨資產**。
> **證據等級**：🟢 引擎內唯讀量測（2026-10-14，`unity cmd eval_file`，preview scene／`AssetDatabase` 讀取，未存任何資產）；🟡 唯讀解析 `.asset` YAML；🟠 靜態推導或離線算術。腳本留在 session scratchpad（用完即丟），可依 §J 重建。
> **輸出對象**：`docs/58` §4（WP-I1 SpeedCurve 語意）的 producer 側回填見 **§K**。

---

## 0. 本審查遵守的既有裁決（不重議）

| 來源 | 內容 | 對本審查的限制 |
|---|---|---|
| ADR-010 §9（2026-10-03）＋ `docs/45` §16.1 | **Bake plant 不依賴 T3 鎖定門檻**；landing-pose contract、遲滯（T3 側）不採 | C-A（Animation Plant）與 C-B（IK Lock）分離**不是** finding |
| `docs/48` §3.2 K1–K6（正本 `docs/45` §28） | C-A ⇏ C-B；`plant` 只指 C-A；Run／Walk 停步預測可信度由 resolver 即時驗證收斂 | 本審查只看 **C-A 內部**（兩個 bake 欄位之間）的關係 |
| `docs/48` §0.2 D2-g | F2-J（Jump clock-family 錯配）＝ Known low-impact edge case／Deferred | 不重報 Jump 用哪支 loop 的時鐘 |
| `docs/07` §0.2 | Stop 速度帶落在該 loop 主導區間 | Stop intensity 帶是 policy，不是 bake 數值的手抄 |
| CLAUDE.md B11／Gate B | 不寫 Animancer `_Thresholds`；mixer／Stop playback 人工依公式推導 | 人工推導的 0.3651／0.75、1.3328 等不列為 finding |
| CLAUDE.md「Animation Assets: Immutable by Default」 | FBX sub-clip 唯一真相；MotionBakeData 是動畫真實數值來源 | 修法只能改 baker／bake 欄位，不能改 clip |
| `docs/50` L.0／M-03、`docs/58` §4 | 預測 vs 執行 ≤ 1 cm；⛔ 不為數值收斂而重構 | §C P-02 只**補一個參考點**（producer 真值），不推翻該容差 |
| `docs/52` J4（使用者裁決） | coverage floor 1.00 依 Jump apex **人工設定**，維持 | 不重報為「手抄 bake 值」 |

---

## A. Executive Summary

**整體 fragmentation：Low–Moderate。** 烘焙管線本身是本 repo 最乾淨的一段：**每個 bake 欄位都只有一個寫入點**（唯一例外是 `Traversal` block，見 P-01），`AutoAverageSpeed` 的「烘焙值＋runtime 回退共用同一個函式」是值得複製的 pattern，`FootPhaseCurve` 與 `FootPlantSchedule` 是**刻意分工**而非同一事實推導兩次。

碎裂集中在兩類，而且都不是「兩套規則」，而是「**producer 的輸出格式沒有被寫下、也沒有被測試守住**」：

| # | 概念 | 一句話 |
|---|---|---|
| 1 | **Traversal block 有兩個寫入者** | `MotionBakeEditor` 重烘會用**另一個取樣器**覆寫 `TraversalContactDerivation` 寫的 anchors；🟢 實測 `Bake_Climb2m` 重烘後**手部朝向消失**（`HasHandRotations` True→False），沒有測試守 |
| 2 | **SpeedCurve 的 key 語意** | 每個 key ＝「上一個樣本到這個樣本的水平弦長 ÷ 標稱間隔」（後向差分）。key 0 (0,0) 佔位：🟢 **60／60 出貨 bake 成立**；另有**第二種佔位**——7／60 支在結尾多一個 Δt ≈ 1e-7 s、值 ≈ 0 的重複 key，沒有任何 consumer 規則處理 |
| 3 | **積分 vs producer 真值** | 以 producer 自己的 root 弦長和為真值（🟢 引擎內重算一致），「略過佔位」的停步積分（預測 d／執行 b）**整段多走 +12～+29 mm**（執行端依 `docs/50` M-03 為 +14～+35 mm）；最粗糙的「瞬時速度 × dt」(a) 反而最準（≤ 0.6 mm） |
| 4 | **Plant 偵測沒有遲滯** | `Bake_RunFwdStop_RU` 同一次左腳支撐被切成兩個事件（0.567、0.767 s）——🟢 腳速在 0.06–0.24 m/s 間抖動跨過 0.20 門檻；於是 0.567–0.767 s 發布的 next plant 指向**正被鎖住的那隻腳** |
| 5 | **Bake 世代混雜** | 出貨資產由 ≥ 5 個 producer 世代寫成（`VerticalCurve` 只 4／60 有、`FootPlantSchedule` 只 4 支 Stop 有、4 支無引用舊資產 `Duration = 0`、`Stand To Roll` 的 `AutoAverageSpeed` 用舊定義算）——**任一支重烘都會一次改動好幾個欄位**，而只有部分被測試涵蓋 |

**使用者指定的三個問題的結論**

1. **`FootPhaseCurve` vs `FootPlantSchedule`**：同一個取樣串（同一趟 `SampleAnimation`、同一根 `HumanBodyBones.*Foot`），回答**不同問題**——前者是「哪隻腳比較低」的連續有號量（無門檻、每個時刻都有答案，用於 clip 之間對相位），後者是「腳何時低且靜止」的離散事件（絕對門檻，用於預測落點位置）。⇒ **① Healthy specialization**。出貨資料上兩者一致（Walk Stop 入場的相位符號＝第 0 個 plant 的腳；Stop 入場值對應 loop 的 key 誤差 ≤ 0.15 mm）。真正的問題在 **plant 偵測器內部**（P-03 無遲滯）與 **`EndPhase` 這第三個欄位**（P-05：與 `FootPhaseCurve` 終點符號 56／56 相同，卻被拿來當**任意時刻**的 fallback）。
2. **`SpeedCurve`**：key 0 由 `MotionBakeEditor.cs:592` **唯一**寫入（`BatchBake`／`AnimationBatchOps` 走同一路徑，沒有其他 stage 改寫 SpeedCurve）；**沒有任何測試**守 producer 格式或出貨資產（現有測試全是合成曲線）。見 §K 的完整回填清單。
3. **Ownership**：除 `Traversal` 外每欄位唯一 producer；**沒有發現新的「手抄 bake 值進 config」**——已知的幾處（B11、`docs/52` T-03／J4、`docs/47` F-10）皆已登記或已裁決。另有 2 個**零 runtime consumer** 的 bake 欄位（`TargetLocalDirection`、`RotationFinishedTime`）。

**現在需要處理嗎？** P-01 建議**下次碰 traversal bake 之前**先補守門測試（一行斷言就能防住一次靜默回歸）；P-04（格式守門）併入 `docs/58` WP-I2 ③；P-02／P-03 需要一次 Play／probe 判斷是否可見，再裁決（§❓）。

---

## B. Concept Inventory

### B.1 欄位 → producer → consumer（ownership 表）

| Bake 欄位 | 唯一 producer（stage） | 判準／算法 | Runtime consumer | 出貨覆蓋（60 支） | 風險 |
|---|---|---|---|---|---|
| `SourceClip`／`SampleRate`／`BakedDuration` | `MotionBakeEditor.SaveAsset` | clip 長度快照 | 全部（`Duration`） | `SampleRate` 56×60 fps、4×30 fps（Climb1m／Climb2m／Vault1m／Fists_Hit_Right）；4 支缺 `BakedDuration`（皆無引用） | Low |
| **`SpeedCurve`** | `MotionBakeEditor`（`:592` key 0、`:598-599` 其餘） | 水平弦長 ÷ **標稱** interval，記在樣本終點時刻（後向差分） | MotionDriver（a／b）、`GetHorizontalDisplacementAt`（c）、`GetLocomotionStopHorizontalDisplacementAt`（d）、`ComputeAverageSpeed`（e） | 60／60 key0＝(0,0)；7／60 尾端重複 key | **Medium**（知識分散，P-02／P-04） |
| `RotationCurve` | `MotionBakeEditor`（`:593`、`:607`） | 累計 yaw；key 0 (0,0) 是**真值**（累計量起點） | MotionDriver、TraversalWarpPlan | 60／60 | Low |
| `AutoAverageSpeed` | `SaveAsset:819` → `MotionBakeData.ComputeAverageSpeed` | 算術平均，key0==(0,0) 才略過 | `GetRepresentativeSpeed`（MotionDriver 滿速、Directional speed profile 分母） | `Stand To Roll` 存值用舊定義（P-06） | Low |
| `RotationFinishedTime` | `MotionBakeEditor.CalculateRotationFinishedTime` | 末段 ±5° 收斂 | **無**（只有 `IsRotationFinished`，零呼叫） | — | Low（死欄位） |
| `EndPhase` | `MotionBakeEditor:622-623`（另起一次 `SampleClipPose`，root-local Y） | 終點「左踝較低」 | **只有** `GetFootPhaseAt` 的 fallback | 56／56 與 `sign(FootPhaseCurve(end))` 相同 | Low（P-05） |
| `TargetLocalDirection` | `MotionBakeEditor.CalculateTargetLocalDirection` | 起訖位移投影 | **無**（只有 `EnemyPunchMotionWiringTests` 斷言原地） | — | Low（死欄位） |
| `FootPlantSchedule` | `FootPlantScheduleBaker.Build`（`SaveAsset:825`） | 踝高 ≤ **整段最低** + 2.5 cm ∧ 水平速度（中央差分）≤ 0.20 m/s，連續 ≥ 2 樣本，同側間隔 ≥ 0.12 s | `LocomotionModel.PublishStopPrediction` → `LocomotionStopPrediction` → `FootIKController.TryPredictNextPlantGoal` → LandingSurfaceResolver | 只有 4 支 Stop（其他資產在 2026-10-03 後未重烘） | **Medium**（P-03） |
| `AutoTakeoffDelay`／`AutoApexHeight`／`AutoAirTime`／`AutoCalculatedGravity` | `JumpFeatureAnalyzer`（MotionFeatureAnalysisStage） | clip-local ground baseline（**滾動最低＋凍結**）＋ 0.03 m | `JumpStateParams.TryGetLaunch`、`JumpState:150`、`TraversalSelectionPolicy`（copy，`docs/47` F-05） | — | 已登記（F-05／T-03） |
| `VerticalCurve` | `VerticalCurveAnalyzer` | root Y − 第一帧 | TraversalWarpPlan、Committed、`GetVerticalSettleNormalizedTime` | **4／60**（只 Climb1m／Climb2m／Vault1m／Fists_Hit_Right 重烘過） | Low（缺時退 0，consumer 只在 traversal） |
| **`FootPhaseCurve`** | `FootPhaseCurveAnalyzer` | 左踝世界 Y − 右踝世界 Y，**無門檻** | `LocomotionStopSelector.SelectByEntryPhase`（**符號**）、`TrySelectNearestFuturePhaseMatch`（**值匹配**）、`LocomotionModel.TryGetRunEntryPhase`（**極值**，ADR-011）、`JumpState`（符號） | 56／60（缺的 4 支皆無引用） | Low |
| **`Traversal`**（block） | ⚠️ **兩個**：`TraversalContactDerivation.Apply`（`:379`，整個 block）與 `MotionBakeEditor.BakeTraversalAnchors`（`:835`→`:886`，只 anchors） | 兩個不同的 anchor 取樣器 | `TraversalStateParamsSO`、`TraversalWarpPlan`、Hand IK | Climb2m／Vault1m 有；Climb1m 無 block | **Medium**（P-01） |

### B.2 主要概念的 Producer → Transform → Consumer

```text
同一趟 SampleAnimation（MotionBakeEditor.BakeClipMotion，X Bot，applyRootMotion）
  每帧讀：root 位置／旋轉、LeftFoot／RightFoot 骨骼（＝踝）世界位置
  ├─ root XZ 弦長 ÷ 標稱 interval ───────────────→ SpeedCurve（key0 佔位 (0,0)）
  │                                                  ├─ ComputeAverageSpeed → AutoAverageSpeed → 滿速分母
  │                                                  └─ 5 種積分（docs/58 §4.3）
  ├─ root yaw 累計 ─────────────────────────────→ RotationCurve（key0 真值 0）
  ├─ 踝 Y、root Y → MotionFeatureContext
  │     ├─ JumpFeatureAnalyzer（滾動最低基線 + 0.03）→ Auto* 跳躍特徵
  │     ├─ VerticalCurveAnalyzer → VerticalCurve
  │     └─ FootPhaseCurveAnalyzer（L.y − R.y，無門檻）→ FootPhaseCurve
  │           ├─ 符號：Stop Run／fallback 選片、Jump 起跳腳
  │           ├─ 值匹配：Walk Stop 等相位（loop key ↔ Stop 入場值）
  │           └─ 極值：ADR-011 Run 起跑相位
  └─ 踝世界／root 空間 → FootPlantScheduleBaker（整段最低 + 2.5 cm ∧ ≤ 0.20 m/s）
        → FootPlantSchedule（≤ 4 事件 ＋ EndPose）→ LocomotionModel → next plant／結束站姿
另起一次取樣（SampleClipPose @ duration）→ EndPhase（只當 fallback）
另一個 Editor 入口（TraversalContactDerivation.Apply，preview scene）
  手骨速度（自身最大值 × 0.15）→ marker 時間 → anchors（含手部朝向）／grip edge／Hand IK 窗／reach → Traversal
  ⚠️ 之後若再跑 MotionBakeEditor：BakeTraversalAnchors 用「不帶朝向」的取樣器覆寫 anchors
```

---

## C. Fragmentation Findings

### P-01 `Traversal` block 有兩個寫入者，重烘會靜默丟掉手部朝向 — **Medium**

**File＋symbol**：`MotionBakeEditor.SaveAsset:835` → `BakeTraversalAnchors:860-886` → `SampleTraversalAnchor:906-938`（7 參數建構子，朝向寫 `default`）；`TraversalContactDerivation.Apply:379` ← `TrySampleAnchors:585`（9 參數建構子，含 `inverseRootYaw * hand.rotation`）。

**Evidence**
- 🟢 引擎內反射呼叫 `BakeTraversalAnchors`（回傳 struct、**不存檔**），輸入出貨 `Bake_Climb2m.Traversal`：`shippedRot=True → rebakedRot=False`；grip edge、Hand IK 窗、reach constraint **原樣保留**；四個 anchor 的 root／手位置差 **0.0000**（這兩支 clip 的 root 只有 yaw，兩種座標框剛好重合）。`Bake_Vault1m` 出貨本來就沒有朝向（兩邊皆 False）。
- `TraversalStateParamsSO.cs:589-592`：`HasHandRotations` 為 false 時退回舊路徑——那正是 `027f0ce`（2026-09-22「手指不再插進頂面」）修掉的行為。
- 測試：`grep HasHandRotations|HandRotation Assets/_Project/Tests` ＝ **0 筆**。`TraversalBakedAssetTests` B7／B14 只斷言 `HasValidBakedData`／`HasGripEdges`，兩者重烘後仍為 true ⇒ **不會變紅**。
- 兩個取樣器另有座標框差異：`MotionBakeEditor` 用完整 root 旋轉的 `InverseTransformPoint`，`Derivation` 用 yaw-only——目前出貨 clip 數值相同，pitch／roll 不為 0 的 root 才會分岔（🟠）。

**為什麼在本 repo 是問題**：這是本專案最在意的 **single source of truth／決策擁有權**——同一欄位兩個 producer、**最後執行者贏**，且輸出不同。重烘 traversal bake 是有理由發生的日常操作（任何 SpeedCurve／VerticalCurve 修正、`docs/24` §14.7 的還原 SOP 也只寫了「重跑 Derivation」，沒寫順序）。`MotionBakeEditor:829-834` 的註解還寫「marker 是 authored」，但 `docs/23` Phase 2 起 marker 已是 Derivation 測出的。
**分類**：② Duplicate fact derivation（同一 anchor、兩個取樣器）＋ ownership 不清。
**最小修法**（未實作）：①先補一條資產測試：`Bake_Climb2m` 的所有 contact anchor `HasHandRotations == true`；②`BakeTraversalAnchors` 改呼叫 Derivation 的同一個取樣函式（或在 block 已有朝向時不覆寫 anchors），使 anchors 只有一個定義。

---

### P-02 SpeedCurve 的 key 語意沒被寫下：佔位跳過讓停步整段比動畫 root 多走 1–3 cm — **Low（數值）／Medium（知識分散）**

延伸 `docs/50` M-03 與 `docs/58` §4（該處的「預測 vs 執行 ≤ 1 cm」仍成立；本條補的是**第三個參考點：producer 真值**）。

**Producer 的實際語意**（`MotionBakeEditor.cs:596-599`）：key *i*（*i* ≥ 1）＝ root 在 (t<sub>i−1</sub>, t<sub>i</sub>] 的**水平弦長 ÷ 標稱 interval**，記在 t<sub>i</sub>。⇒ 精確的累計位移是**右端點階梯和** Σ v<sub>i</sub>·Δt，也就是 root 弦長和。

**Evidence**：🟢 引擎內在 preview scene 重做取樣，root 弦長和＝ WalkStop_LU 0.6654、_RU 0.7145、RunStop_LU 1.5091、_RU 1.9501 m，與 🟡 YAML 上 Σ v<sub>i</sub>Δt **到 0.1 mm 相同**。各積分對這個真值（🟠 對序列化 key 的精確算術；b 取 `docs/50` M-03 的離線 60 fps 值）：

| Bake | v₁ | 真值 Σ v·Δt | c（含佔位梯形） | d（略過佔位，**停步預測**） | b（**停步執行**，M-03） | a（瞬時 × dt，M-03） |
|---|---|---|---|---|---|---|
| WalkFwdStop_LU | 1.457 | 0.6654 | −0.0 mm | **+12.1 mm** | +14.5 mm | — |
| WalkFwdStop_RU | 1.616 | 0.7145 | −0.0 | **+13.5** | +16.1 | — |
| RunFwdStop_LU | 3.511 | 1.5091 | −0.0 | **+29.3** | +34.9 | — |
| RunFwdStop_RU | 3.483 | 1.9501 | −0.1 | **+28.9** | +34.0 | — |
| Stand To Roll | 0.630 | 4.5968 | −0.3 | +5.0 | — | +0.6 |
| slash1_Baked | 0.342 | 0.8020 | −3.2 | −0.4 | — | +0.0 |
| Climb1m（30 fps） | 0.346 | 1.3678 | −0.1 | +5.6 | +0.6 | — |

- 解析式（🟠，與上表吻合）：c(t<sub>k</sub>) ＝ 真值 − v<sub>k</sub>·Δt／2（全程落後半個樣本，**終點在速度歸零時恰好正確**）；d(t<sub>k</sub>) ＝ 真值 ＋ (v₁ − v<sub>k</sub>)·Δt／2（第一段精確，之後持續超前，停步終點超前 v₁·Δt／2）。
- `docs/07` R8（2026-09-27）把佔位排除是為了修**入場第 3 帧半速頓挫**——那一帧的修法是對的；但它只把第一段換成精確值，其後的梯形仍帶半樣本偏移，於是**總量**變成超前。預測（d）與執行（b）同向超前，所以兩者一致（M-03 的結論不受影響）。

**為什麼在本 repo 是問題**：CLAUDE.md 說 MotionBakeData 是「動畫真實位移」的唯一來源；目前四種積分對「真實位移」給四種答案，而**唯一和 producer 語意精確對齊的是最粗糙的 a**——這個事實不在任何文件裡。停步期間雙腳由 T3 鎖在世界座標，root 多走 1.2–3.5 cm 會由 IK 吸收（腿被拉長一點）或顯示為腳的微小前滑，量級與 R1c 的 d0 = 3 cm 同級（🟠 是否可見未量）。
**分類**：② Duplicate fact derivation（同一條曲線、同一個「區間位移」問題、對 producer 語意的不同假設）。根因同 `docs/47` F-09：輸出格式由 consumer 各自記住。
**最小修法**（未實作）：①**文件**：把「key = 後向差分、真值 = 右端點階梯和」寫進 `docs/58` §4（§K）；②**是否修數值**交裁決（§❓ D59-1）。若修，canonical 形狀是 G-2（bake 擁有累計位移曲線），不是再加一種積分。

---

### P-03 Plant 偵測沒有遲滯：一次支撐被切成兩個事件 — **Low–Medium**

**File＋symbol**：`FootPlantScheduleBaker.IsCandidate`（固定 0.20 m/s，無遲滯）、`AppendSide:96-105`（`minimumSeparation` 0.12 s 只比對「上一個已發布事件」）。

**Evidence**（🟢 引擎內，以 committed `FootPlantScheduleBaker.Build` 對出貨 clip 重建）：
- `Bake_RunFwdStop_RU` 事件 `Left@0.567、Left@0.767、Right@1.017`。0.533–0.783 s 左踝高度 ≤ 1.3 cm（遠在 2.5 cm 帶內），水平速度在 **0.06–0.24 m/s 之間抖動**：0.650 s（0.219）斷窗 → 0.667 s 新窗離上一事件 0.10 s < 0.12 s 被丟 → 0.733–0.750 s（0.202、0.242）再斷 → 0.767 s 新窗距 0.567 s 已 0.20 s ⇒ 發布第二個左腳事件。
- 兩事件 AnkleInRoot.z 差 −0.188 m，root 同期前進 +0.195 m（🟡 YAML 原型鍵 `RootForwardDisplacementFromClipStart`）⇒ 左踝世界位置只移 ≈ 0.7 cm：**同一次支撐**。`FootPhaseCurve` 在 0.483–1.233 s 全程左腳較低、沒有零交越，佐證同一結論。
- Consumer 後果（🟠 推導）：`PublishStopPrediction` 取「clip 時間晚於目前的第一個事件」——0.567–0.767 s 之間發布的 next plant ＝ `Left`（正被 T3 鎖住的腳），`NextPlantIsFinal = false`；真正在擺盪的右腳（約 0.5→1.017 s）在這 0.2 s（≈ 40% 擺盪期）拿不到預測，`docs/48` K2 讓 resolver 對左腳那份重設，右腳退回即時驗證。`FootIKController:294` 對 Run 停步也消費 next plant（規則⑦只限 pelvis 的 Walk）。

**為什麼在本 repo 是問題**：C-A 的契約是「**一次接觸一個事件**」（`FootPlantScheduleBaker` 摘要：「每個連續區只發布一次」），但「連續區」被速度雜訊定義。這是 `snap-to-default` 缺陷類型的 bake 版：連續量（腳速）餵給單一硬門檻，結果是離散事件數量不穩。⚠️ 這**不是** T3 的遲滯問題（10-03 裁決不採的是 T3／landing-pose 的遲滯），而是 C-A 自己的偵測器。
**附帶觀察**（🟠）：Run 停步的**第一次觸地**不在排程內（`RunFwdStop_LU` 相位在 0.133 s 換腳、第一個 plant 在 0.300 s；`_RU` 0.133 s 換腳後到 0.567 s 才有 plant）——基準是「整段最低」＝結束站姿高度，跑步支撐的踝較高。也就是說 schedule 實際語意是「**接近最終站姿高度的靜止支撐**」，不是「每次觸地」。這與 ADR-010 D5 用途相容，但沒有寫下。
**分類**：③ Local rule drift（偵測器的穩定性）；不是兩套定義。
**最小修法**（未實作）：同側相鄰事件若「中間無對側事件 ∧ 踝世界位移 < 容差」則合併（或速度門檻加遲滯）；需重烘 4 支 Stop、屬 Living Spec（ADR-010 是 Trial）。交裁決 D59-3。

---

### P-04 SpeedCurve 有第二種佔位（尾端重複 key），而格式完全沒有測試守 — **Low（數值）／Medium（守門缺口）**

**File＋symbol**：`MotionBakeEditor.cs:502`（`totalFrames = CeilToInt(duration × rate) + 1`）、`:556`（`time > duration` 夾回）、`:598`（`dist / interval` 用**標稱**間隔）。

**Evidence**（🟢 引擎內普查 60 支 `MotionBakeData`）：
- key 0 ＝ (0,0)：**60／60**。
- 尾端重複 key（相鄰 Δt < 1 ms）：**7／60**——FallingLoop（2.4e-7 s）、Jump_place_ALL_short、JumpIdleLand、JumpRun_LU_Land、JumpWalk_LU_Land（1.2e-7）、**JumpWalk_LU_Land2Walk（6.0e-8，前一 key 1.7299 → 0）**、**Vault1m（2.4e-7，0.0483 → 0）**；`GetSpeedAt(Duration)` 全部回 0。成因：`duration × rate` 的浮點積略大於整數 ⇒ 多一個樣本、時間被夾回 `duration`、弦長 ≈ 0 卻除以完整 interval。
- 影響（🟠）：積分可忽略（Vault1m 終點 Δ ≈ 1e-8 m）；`ComputeAverageSpeed` 多算一個 0（Land2Walk 低估 ≈ 1.8%，無 consumer）；Jump 不消費落地 clip 的 SpeedCurve（`JumpState` 只讀 `Duration`）。**目前無可見後果**。
- 測試盤點：`MotionBakeDataTests:56-70,178-188`、`LocomotionStopEntryPlayModeTests:37-58`、`TraversalWarpPlanTests` 全是**合成曲線**；`MotionBakeEditorTests` 只測 `FootPlantScheduleBaker` 與 Animator 解析；`TraversalBakedAssetTests`／`TraversalIntegrationWiringTests`／`EnemyPunchMotionWiringTests` 讀出貨資產但只斷言非空／有運動／原地。⇒ **沒有任何測試**守「producer 寫 (0,0)」或「出貨 bake 的 key 0 是 (0,0)」。
**為什麼在本 repo 是問題**：`docs/58` §4.1 的兩個「無條件略過」依賴一個**只有 producer 原始碼知道**的格式；尾端這種佔位連 consumer 規則都沒有。這是 Test-as-Spec 原則（CLAUDE.md）的缺口：格式是架構事實，卻只存在於一行 `AddKey(0f, 0f)`。
**分類**：③（格式知識分散）＋ 守門缺口。
**最小修法**（未實作）：併入 `docs/58` WP-I2 ③，並把範圍擴成「**所有被引用的出貨 bake**：key0 == (0,0)、無 Δt < 1e-3 的相鄰 key、`AutoAverageSpeed == ComputeAverageSpeed(SpeedCurve)`、`Duration > 0`」；producer 端把樣本數改成以「最後一個不超過 duration 的整數帧」為界（只在重烘時生效）。

---

### P-05 `EndPhase` 是 `FootPhaseCurve` 終點的第二次推導，卻被當成任意時刻的 fallback — **Low**

**File＋symbol**：`MotionBakeEditor:622-623`（另起 `SampleClipPose`、root-local Y）；`MotionBakeData.GetFootPhaseAt:1044`（曲線缺 ⇒ 對**任何 time** 回 `EndPhase`）。
**Evidence**：🟡 56 支有曲線的出貨 bake，`EndPhase` 與 `sign(FootPhaseCurve(end))` **56／56 相同**——同一事實推導兩次。Consumer 政策不一：`JumpState.SelectVariant:562-566` 明文**拒絕**這個 fallback（缺曲線即整格失效）；`LocomotionStopSelector.SelectByEntryPhase:63` 以 `GetFootPhaseAt(0f)` 問**入場**相位，曲線缺時會靜默拿到**結束**相位（`LocomotionStopVariant.IsValid` 不要求曲線）。出貨 4 支 Stop 都有曲線 ⇒ 目前不可達。
**附帶**：雙腳著地時符號由 rig 決定——22 支 bake 終點、11 支起點 |L−R| < 2 mm（多為 0.0006 m ＝ X Bot 左右踝靜止高度差）⇒ 一律判 `RightFootDown`。目前沒有 consumer 在雙支撐時刻查符號（Stop／Jump 入場都在單支撐，|值| ≥ 0.13），但沒有寫成契約。
**文件漂移**：`docs/04` §89–91 仍寫「Stops 用 `EndPhase` 對齊停步後 Idle 的腳相」——現行程式沒有這個 consumer。
**分類**：② Duplicate fact derivation（低風險）＋ fallback 語意錯位。
**最小修法**（未實作）：`GetFootPhaseAt` 的 fallback 只在 `time ≥ Duration` 時回 `EndPhase`（或乾脆移除，比照 Jump 讓缺曲線＝失效）；`docs/04` fold back。

---

### P-06 Bake 世代混雜：重烘一支資產會一次改動多個欄位 — **Low–Medium（累積）**

**Evidence**（🟢 普查＋🟡 YAML）：

| 世代標記 | 出貨現況 | 重烘後會變成 |
|---|---|---|
| 無 `BakedDuration`／`AutoAverageSpeed`（2026-07-26 前） | `Fast Run`、`Idle`、`Jump`、`Walking`：`Duration = 0`，**皆無任何 prefab／SO 引用** | — （建議刪，D59-4） |
| `AutoAverageSpeed` 用舊定義（含佔位） | `Stand To Roll`：存 1.92874，現行定義 1.94232（+0.7%）；無 consumer 讀它 | 值改變 |
| 無 `VerticalCurve` | 56／60 | 出現 VerticalCurve（Committed 路徑開始有垂直位移——目前只有 traversal 走 Committed） |
| 無 `FootPlantSchedule` | 56／60（含所有 loop、Jump） | **每支**都會出現排程與 EndPose（baker 對所有 clip 執行）；`MotionBakeData.cs:688` tooltip「非 Stop clip 維持 default」只是**因為還沒重烘**才成立 |
| Stop 排程含原型鍵 | 4 支 Stop 的 `FootPlantSchedule` 帶 `DetectionHorizontalSpeedMax`、`LeftHipInRoot`、`RootForwardDisplacementFromClipStart` 等程式已不讀的鍵（`e5ff3b3`：「已烘焙的 4 支 Stop 資產直接提供事件，未重新烘焙」） | 原型鍵消失；🟢 **事件與結束站姿逐位元不變**（committed baker 重建 4／4 完全相同） |
| Traversal anchors 帶朝向 | Climb2m | **朝向消失**（P-01） |

另：`FootPlantScheduleBaker` 的基準是「整段最低」，正是 `MotionBakeEditor:310-313` 文件為 Jump 基線**否決**的候選 C（Jump*Start 尾段自由落體到 −52～−78 m）——對 Stop clip 正確，但 baker 不限 clip 家族，Jump 一旦重烘就會帶一份無意義的排程（無 consumer 讀）。
**為什麼在本 repo 是問題**：「Bake Data 是真相」的前提是**同一個 producer 版本產生**；目前只要有人為了一個欄位重烘，其他欄位就連帶改動，而其中只有 traversal 與 Stop 有部分資產測試。
**分類**：⑤ Data plumbing（provenance）。
**最小修法**（未實作）：P-04 的資產測試＋P-01 的朝向斷言即可把「重烘後會變」的項目變成會紅的測試；`FootPlantSchedule` 是否限 Stop clip 寫入交 D59-5。

---

### P-07 兩個零 consumer 的 bake 欄位 — **Low**

`TargetLocalDirection`（附 Inspector 開關與兩個預設常數）與 `RotationFinishedTime`／`IsRotationFinished`：`grep` runtime 零呼叫。前者唯一讀者是 `EnemyPunchMotionWiringTests:55-56`（用來斷言「原地」）。屬 v0.7 參考演算法移植時的預留欄位。⇒ ④ Special-case 殘留；去留見 D59-4（不急，零成本存在）。

---

### 未成立的懷疑（已查證）
- **「有兩個 stage 寫 SpeedCurve」**：否。`SpeedCurve` 只在 `SaveAsset:813` 指派；`AnimationBatchOps` 透過 `MotionBakeEditor.BatchBake`，自身不寫任何 bake 欄位（`grep` 指派式＝0）。
- **「runtime 有寫死 1/60」**：否。兩個停步佔位略過都用 `1 / bake.SampleRate`，對 30 fps 的 Climb1m 也正確。
- **「Stop 入場相位與 loop 對不上」**：否。🟡 Stop 入場值在 loop key 中的最小誤差：WalkStop_LU 0.08 mm（loop n=0.5）、_RU 0.02 mm（n=0）、RunStop_LU 0.15 mm（n=0.522）、_RU 0（n=0）——Stop clip 是 loop 的 authored 延續。
- **「出貨 Stop 排程是原型產物、與 committed baker 不同」**：否（🟢 4／4 逐位元相同）。剩下的只是「沒有測試釘住」，併入 P-04／P-06。

---

## D. Special-case Object Inventory

| 物件 | 位置 | 狀態 | 建議 |
|---|---|---|---|
| SpeedCurve 頭端佔位 (0,0) | `MotionBakeEditor:592` | 4 處 consumer 知識（`docs/58` §4.1） | 寫成格式契約＋資產測試（P-04） |
| SpeedCurve 尾端重複 key | `MotionBakeEditor:502,556,598` | 7／60，無 consumer 規則 | 修樣本數（重烘時生效）＋資產測試 |
| `EndPhase` fallback | `MotionBakeData:1044` | Jump 拒絕、Stop 接受 | P-05 |
| 原型鍵（Stop 排程） | 4 支 Stop `.asset` | 下次重序列化自動消失 | 無需動作 |
| 舊世代無引用 bake ×4 | `Bake_Fast Run/Idle/Jump/Walking` | `Duration = 0` | D59-4 |
| `TargetLocalDirection`／`RotationFinishedTime` | `MotionBakeData` | 零 consumer | D59-4 |
| `MotionBakeEditor` 註解「Traversal marker 是 authored」 | `:829-834`、`MotionBakeData:668` header | 已被 Derivation 取代 | 改註解（隨 P-01） |
| `FootPlantSchedule` tooltip「非 Stop 維持 default」 | `MotionBakeData:688` | 只因未重烘成立 | 隨 D59-5 改 |

## E. Repeated Decision Rules

| 規則 | 版本 | 判定 |
|---|---|---|
| 「腳在地上」 | Jump 基線（滾動最低＋凍結、+0.03 m）／plant（整段最低、+2.5 cm、≤ 0.20 m/s）／phase（L−R 符號、無門檻）／Traversal 手 plant（自身最大速度 × 0.15） | 各自服務不同 clip 家族／問題——**§H**；唯一缺陷是 plant 無遲滯（P-03） |
| 「SpeedCurve key 0 是佔位」 | 停步執行（時間）、停步預測（時間）、平均（值）、其餘不處理 | P-02／P-04；`docs/58` §4.1 |
| 「這段走多少」 | a／b／c／d／e | P-02（補 producer 真值）；收斂見 `docs/50` G-4／本檔 G-2 |
| 「終點哪隻腳在下」 | `EndPhase`（root-local，另取樣）／`FootPhaseCurve(end)`（world） | P-05 |
| 「anchor 在 root 空間的位置」 | `SampleTraversalAnchor`（全旋轉、無朝向）／`TrySampleAnchors`（yaw-only、含朝向） | P-01 |

## F. Data Plumbing

### F-P1：SpeedCurve 格式（1 producer → 4 consumer 規則 → 0 測試）
`MotionBakeEditor:592` → `MotionBakeData.SpeedCurve` → {MotionDriver Stop `minSpeedSampleTime`、`GetLocomotionStopHorizontalDisplacementAt`、`ComputeAverageSpeed`、其餘不處理}。格式知識單向流到 consumer，沒有回到 producer 端的測試。⇒ P-04。

### F-P2：Loop bake 參照（`docs/47` F-10，仍開）
Bake 側補一個事實：`AnimationBatchOps.WireJumpAnimationSet` 以寫死路徑（`:36-37` `Bake_WalkFwdLoop`／`Bake_RunFwdLoop`）維護 `JumpStateParams` 那一份，prefab 上 `LocomotionModel.walk/runLoopBakeData` 那一份是人工接線——**兩份之間沒有相等測試**（`grep` tests＝0）。不重報為新 finding。

### F-P3：Traversal block（兩個入口 → 一個欄位）
`TraversalContactDerivation.Apply`（只能經 `eval_file`／SOP 呼叫，無選單）與 `MotionBakeEditor`（選單＋`BatchBake`）都寫 `Traversal`；執行順序只存在於 `docs/24` §14.7 的還原表。⇒ P-01。

### F-P4：Bake → 預測 → IK（next plant，7 跳；`docs/47` F-P1 已列）
本檔只補資料品質：輸入端的事件數受 P-03 影響。

---

## G. Top Refactor Candidates（最多 5 個，皆未實作）

### G-1 Traversal anchors 單一寫入者
```text
Current          兩個 anchor 取樣器；MotionBakeEditor 重烘覆寫 Derivation 的 anchors、丟朝向（P-01）
→ Canonical      「Traversal block 由 TraversalContactDerivation 擁有」
→ Ownership      Derivation 唯一寫 anchors；MotionBakeEditor 只在 block 未由 Derivation 產生時才取樣，或直接呼叫同一取樣函式
→ Disappear      SampleTraversalAnchor 的無朝向版本、「marker 是 authored」的註解
→ Remain         MotionBakeEditor 的其餘欄位；Derivation 的 Measure／Apply 形狀
```
先做：一條資產測試（Climb2m contact anchors `HasHandRotations`）。行為差異：無（對出貨資產）。

### G-2 Bake 擁有「累計水平位移」曲線（與 `docs/50` G-4、`docs/07` §13.2 Distance Matching 同一件事）
```text
Current          SpeedCurve（後向差分＋頭尾佔位）＋ 5 種積分（P-02／P-04）
→ Canonical      DisplacementCurve D(t)：root 水平弦長累計，key 0 ＝ 0 是真值（不是佔位）
→ Ownership      MotionBakeEditor 寫；consumer 一律 D(t1) − D(t0)（TraversalWarpPlan 已是此形）
→ Disappear      兩個無條件略過、GetLocomotionStop…／GetHorizontal… 分家、佔位知識
→ Remain         SpeedCurve 作為顯示／平均速度來源（AutoAverageSpeed 語意不變）
```
⚠️ 行為變更：停步總位移 −12～−35 mm（回到動畫值）、Roll／Action 逐帧分佈改變 ⇒ 需 PlayMode 前後對照＋重烘全部引用中的 bake。觸發條件：D59-1 判定可見，或 Distance Matching 需求出現。

### G-3 Bake 格式守門（WP-I2 ③ 擴充）
資產測試一次釘住：key0 == (0,0)、無尾端重複 key、`AutoAverageSpeed` 與現行定義一致、被引用 bake `Duration > 0`、Climb2m 朝向、（Editor-only）4 支 Stop 以 committed baker 重建排程逐位元相同。無行為變更；把 P-04／P-06 的「重烘會變」轉成紅燈。

### G-4 Plant 事件合併規則
同側相鄰事件、中間無對側事件、踝世界位移 < 容差 ⇒ 合併（或速度門檻遲滯）。行為變更：`RunFwdStop_RU` 3 → 2 事件，0.567–0.767 s 的 next plant 改指向右腳。需重烘 4 支 Stop＋ `LandingSurfaceProbe` 前後對照。路由：`docs/45`／ADR-010（Trial）Living Spec。

### G-5 Bake 世代收尾
刪 4 支無引用舊 bake；`EndPhase` fallback 限縮；`TargetLocalDirection`／`RotationFinishedTime` 去留；`FootPlantSchedule` 是否限 Stop clip 寫入。全是小項，可在下一次需要重烘時順手做。

---

## H. Things That Look Fragmented But Should NOT Be Merged

| 看起來重複 | 為什麼保持分離 |
|---|---|
| **`FootPhaseCurve` vs `FootPlantSchedule`** | 同一取樣串、不同問題：前者是**相對**量（哪隻腳較低，每刻有答案、無門檻），用於 clip 之間對相位；後者是**絕對**事件（腳何時低且靜止，在 root 空間何處），用於預測落點。合併會讓相位匹配吃到門檻雜訊、或讓落點預測失去位置資訊。出貨資料兩者一致（§C 末「未成立的懷疑」） |
| `FootPhaseCurve` 的三種讀法（符號／值匹配／極值） | 都是對 bake 擁有的同一條曲線的**查詢**，不是三份推導；`TrySelectNearestFuturePhaseMatch` 用值匹配正是為了避開符號的粗糙 |
| Jump 基線（滾動最低＋凍結）vs plant 基線（整段最低） | 服務不同 clip 家族：Jump*Start 尾段自由落體會讓「整段最低」失效（`MotionBakeEditor:310-313`），Stop clip 以結束站姿為最低正是 plant 要的語意。要做的是**限定適用範圍**（D59-5），不是共用 |
| Traversal 手 plant（自身最大速度 × 0.15）vs 腳 plant（0.20 m/s） | 不同身體部位、不同問題（抓握窗 vs 落腳點）；相對門檻是 Derivation 的刻意設計（`TraversalContactDerivation` 摘要） |
| C-A（bake plant）vs C-B（T3 lock） | ADR-010 §9／`docs/48` K1 已裁決分離 |
| `AutoAverageSpeed` 烘焙值 vs `GetRepresentativeSpeed` 回退計算 | 共用同一個 `ComputeAverageSpeed`——這是本 repo **正確**的「單一定義、兩個時間點」範例；`Stand To Roll` 的過期值是世代問題（P-06），不是定義分岔 |
| `RotationCurve` key 0 (0,0) vs `SpeedCurve` key 0 (0,0) | 同形不同義：累計量的起點是真值，差分量的起點是佔位。**不要**把「略過 key 0」推廣到 RotationCurve／VerticalCurve |
| 瞬時 × dt（a）看似最粗糙 | 對後向差分 key 它恰好是右端點階梯和——最貼近 producer 語意。收斂時不要把它「升級」成梯形 |
| 人工推導的 mixer `_Speeds`、Stop playback、Gait 強度、Stop intensity 帶 | B11／Gate B 與 `docs/07` §0.2／§3.4 的刻意設計 |
| `docs/52` T-03 的 coverage floor 1.00 | J4 使用者裁決維持人工 |

---

## I. Architecture Health Verdict

> **烘焙管線的「事實」是單一來源的；碎裂的是「事實的格式」——producer 的輸出約定（後向差分、頭尾佔位、哪個 stage 擁有 Traversal block）只存在於 producer 原始碼與 consumer 的註解裡，沒有被寫成契約、也沒有被出貨資產測試守住。**

**支持「健康」**：除 `Traversal` 外每欄位唯一 producer（§B.1）；`FootPhaseCurve`／`FootPlantSchedule` 是刻意分工；`ComputeAverageSpeed` 單一定義；committed plant baker 對出貨資料逐位元可重現；runtime 沒有寫死取樣率；沒有新的手抄 bake 值。
**支持「格式碎裂」**：P-01（兩個 anchor 取樣器、最後執行者贏、已實測會丟資料）；P-02／P-04（佔位知識分散、第二種佔位無人處理、0 個格式測試）；P-06（≥ 5 個 producer 世代並存）。
**趨勢**：與 `docs/47` §I 同型——每次新增 bake 欄位都附了測試，但測的是 **consumer 對合成資料的行為**，不是 **producer 對出貨資料的輸出**。建議把「新增／改動 bake 欄位 ⇒ 補一條出貨資產斷言」列入 slice 收尾條件。

---

## J. 量測紀錄與待量測

**本次已做（🟢，全部唯讀、未存檔，結束後 `git status Assets/ScriptableObjects/Motion` 為空）**

| 腳本（session scratchpad，用完即丟） | 做什麼 | 結果 |
|---|---|---|
| `plant_probe.cs` | preview scene 實例化 X Bot，依 `MotionBakeEditor` 取樣形狀重取 4 支 Stop，呼叫 committed `FootPlantScheduleBaker.Build`，比對出貨排程；列出 RunStop_RU 左踝 0.53–0.80 s 高度／速度；累加 root 弦長 | 4／4 逐位元相同；弦長＝0.6654／0.7145／1.5091／1.9501；P-03 的速度抖動 |
| `anchor_probe.cs` | 反射呼叫 private `BakeTraversalAnchors`（回傳 struct）於出貨 Climb2m／Vault1m | P-01：Climb2m 朝向 True→False，位置差 0 |
| `speed_probe.cs` | 普查全部 `MotionBakeData` 的 SpeedCurve 頭尾、平均速度、Duration | 60／60 頭端 (0,0)；7 尾端重複；1 平均過期；4 Duration = 0 |

**待量測（⛔ 本次未做）**
1. **P-02 是否可見**：PlayMode 在平地執行 4 支 Stop 到結束，量「root 位移 − 動畫 root 弦長」與停步期間鎖定腳的 IK 伸展量／腳世界漂移（對照 R1c d0 = 3 cm）。即 `docs/50` WP-I2 ④ 再加一欄「對 producer 真值」。
2. **P-03 的 consumer 後果**：`LandingSurfaceProbe` 在樓梯上跑 RunFwdStop_RU，記錄 0.567–0.767 s 右腳是否失去 swing destination、是否有可見差異。

---

## K. 回填 `docs/58` §4（WP-I1 SpeedCurve 語意）的事實清單

> 給協調 session：以下每條都可直接引用；證據等級已標。

1. **Producer 唯一**：`SpeedCurve` 只由 `MotionBakeEditor.BakeClipMotion` 產生——key 0 在 `:592` `speedCurve.AddKey(0f, 0f)`（`i == 0` 分支），其餘 key 在 `:598-599`；`SaveAsset:813` 是唯一指派點。`BatchBake`、`AnimationBatchOps.ApplyJumpSopAndBake／ApplyStrafeSopAndBake` 都走同一路徑；`MotionFeatureAnalysisStage`、`TraversalContactDerivation` **不碰** SpeedCurve。🟢（原始碼＋`grep` 指派式）
2. **Key 的精確語意**：key *i*（*i* ≥ 1）＝ root 在 (t<sub>i−1</sub>, t<sub>i</sub>] 的**水平弦長 ÷ 標稱 interval（1／SampleRate）**，記在 t<sub>i</sub>（後向差分）。⇒ producer 真值的累計位移 ＝ Σ v<sub>i</sub>·Δt（右端點階梯和）＝ root 弦長和。🟢 引擎內重算與 YAML 一致到 0.1 mm。
3. **Key 0 為什麼是 (0,0)**：t = 0 沒有前一個樣本可差分，寫入佔位以建立曲線起點。⚠️ 只有 SpeedCurve 是佔位；`RotationCurve`／`VerticalCurve` 的 key 0 = 0 是**真值**（累計量起點），⛔ 不得把略過規則推廣過去。
4. **出貨現況**：**60／60** 支 `MotionBakeData` 的 `SpeedCurve[0] == (0,0)`。🟢 引擎內普查（2026-10-14）。
5. **第二種佔位（建議寫進 §4.1）**：7／60 支在結尾多一個 Δt = 6e-8～2.4e-7 s、值 ≈ 0 的重複 key（FallingLoop、Jump_place_ALL_short、JumpIdleLand、JumpRun_LU_Land、JumpWalk_LU_Land、JumpWalk_LU_Land2Walk、Vault1m），`GetSpeedAt(Duration) = 0`。成因 `:502` 樣本數 ＋ `:556` 時間夾回 ＋ `:598` 標稱間隔。積分影響可忽略；`ComputeAverageSpeed` 會多算一個 0。🟢
6. **SampleRate 不全是 60**：Climb1m／Climb2m／Vault1m／Fists_Hit_Right 是 30 fps；兩個停步略過都用 `1 / bake.SampleRate`，沒有寫死 1/60。🟡＋🟢
7. **測試現況**：**沒有任何測試**守 producer 格式或出貨資產的 key 0——現有相關測試（`MotionBakeDataTests:56-70,178-188`、`LocomotionStopEntryPlayModeTests:37-58`、`TraversalWarpPlanTests`）全用合成曲線；讀出貨資產的測試只斷言非空／有運動。⇒ WP-I2 ③ **仍需要**，建議擴成 G-3 的清單（頭端 (0,0)＋無尾端重複＋`AutoAverageSpeed` 一致＋`Duration > 0`）。
8. **§4.3 建議加一欄「對 producer 真值」**（不改「預測 vs 執行 ≤ 1 cm」契約）：a ≈ 真值（Roll +0.6 mm、slash1 +0.0 mm）；c ＝ 真值 − v<sub>end</sub>·Δt／2（≤ 3.2 mm）；d／Stop-b ＝ 真值 ＋ (v₁ − v<sub>k</sub>)·Δt／2 ⇒ 停步整段 **+12.1／+13.5／+29.3／+28.9 mm（d）**、**+14.5～+34.9 mm（b，M-03 值）**。R8 的略過修好了入場第 3 帧半速，但總量因此超前半個首樣本。是否處理見 D59-1。🟢（真值）＋🟠（積分算術）
9. **`AutoAverageSpeed` 世代**：`Stand To Roll` 存值 1.92874 是含佔位的舊定義（現行 1.94232）；無 consumer 讀它。🟢

---

## ❓ 需要使用者裁決的點

| # | 問題 | 選項 | 建議 |
|---|---|---|---|
| **D59-1** | 停步整段比動畫 root 多走 1.2–3.5 cm（P-02）要不要處理？ | (a) 只寫進 `docs/58` 當已知語意；(b) 先做 §J-1 量測再決定；(c) 排 G-2 | **(b)**：量到腳漂移／IK 伸展可見才排 G-2；不可見就停在 (a) |
| **D59-2** | P-01 的修法範圍 | (a) 只補資產測試；(b) 測試＋ `BakeTraversalAnchors` 改用同一取樣器 | **(b)**，但 (a) 可以先單獨落地（一行斷言、零行為變更） |
| **D59-3** | Plant 偵測加合併／遲滯（P-03／G-4）？ | (a) 做（RunStop_RU 3→2 事件，重烘 4 支 Stop）；(b) 先量 §J-2；(c) 記為已知 | **(b)** |
| **D59-4** | 刪 4 支無引用舊 bake、`TargetLocalDirection`／`RotationFinishedTime` 去留、`EndPhase` fallback 限縮 | 各自 Yes／No | 舊 bake：Yes（無引用、`Duration = 0`）；兩個死欄位：不急；fallback 限縮：Yes（Jump 已這樣做） |
| **D59-5** | `FootPlantSchedule` 是否只對 Stop clip 寫入（避免重烘後每支 loop／Jump 都帶一份無意義排程） | (a) 限定（以 bake 旗標或 clip 家族）；(b) 維持全寫、改 tooltip | (b) 成本最低；(a) 只在有人誤用時再做 |

---

## 附錄：本次讀過的關鍵 call site

`MotionBakeEditor.cs:300-360,500-710,736-760,780-938` ｜ `MotionFeatureAnalysis.cs:1-110,255-359` ｜ `FootPlantScheduleBaker.cs`（全） ｜ `TraversalContactDerivation.cs:1-60,280-390,525-545,585-640` ｜ `AnimationBatchOps.cs:21-60,436-460,940-975,1380-1400` ｜ `MotionBakeData.cs:8-30,184-240,651-1106` ｜ `LocomotionModel.cs:75-100,150-180,230-310,370-395,468-505,555-580` ｜ `LocomotionStopSelector.cs:30-200` ｜ `JumpState.cs:355-380,540-570` ｜ `MotionDriver.cs:868-890,2620-2665` ｜ `FootIKController.cs:294,910-913` ｜ 測試：`MotionBakeDataTests`、`MotionBakeEditorTests`、`MotionFeatureAnalysisTests`、`LocomotionStopTests`、`LocomotionStopEntryPlayModeTests`、`TraversalBakedAssetTests`（`grep`） ｜ 資產：`Assets/ScriptableObjects/Motion/Bake_*.asset` 60 支（YAML 唯讀＋引擎內唯讀）、GUID 反查引用（prefab／SO） ｜ git：`e5ff3b3`、`5d61273`
