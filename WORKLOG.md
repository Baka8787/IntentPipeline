# WORKLOG

> 唯一的進度管理文件。每完成一項立即更新。
> 歷史架構決策請看 `docs/changelog.md` 與 `docs/ADR/`；此檔只管「現在手上的工作」。

---

## 🔖 交辦（下一會話 Handoff）

> # 🟧 2026-10-07：A2／A1／ADR-011 AC2 三份裁決資料（`docs/45` §25–§27；⛔ runtime 未改）
>
> - **A2（§25）**：R1c 基線重量 P8b——錯階暫態 15→2（剩 2 在起跑段）、晚修正 max 5.3→2.0 cm、落地前加速度中位 +2.8；4 個跨階緣模糊鎖定 ⇒ ⏳ 使用者 Play（清單在 §25）。
> - **A1（§26）**：強制 RU 在樓梯上 re-step／換階 6–9→0–2、停步 +0.44 m、上樓首步單帧 +22–26 cm；LU 收腳步是刻意製作、平地無問題，但抬腳 8 cm＜級高 19.5 ⇒ **只在樓梯生效的選片 policy**，不是掩蓋資產缺陷。❓ 是否採用（需 Core 地形查詢，Living Spec）。
> - **ADR-011 AC2（§27）**：~12 cm 是動畫本身的起跑爬行（非 T3 補回）；⚠️ R1c 上線後畫面腳離地前滑動 4.5→11–13.5 cm ⇒ AC4（「拖步變好」）需在 R1c 下重新 Play。ADR-011 維持 Trial、AC2 不放寬。
> - 未處理（依裁決原樣保留）：工作樹 WIP（場景含 GroundLayers revert、材質、音量、ProBuilder、`docs/05`、`EditorSettings.asset`）。
>
> ---
>
> # ✅ 2026-10-06 determinism slice：失地確認 grace 改為累積時間（`docs/02` §825 行）
>
> - 問題：`JumpState.fallEntryGrace`（玩家 0.35／敵人 0.1）與 `MotionDriver.groundEdgeEventGrace`（0.1）以 `Time.time` 快照相減；1/60 步長下剛好落在 float32 邊界 ⇒ 依絕對時刻差一帧（樓梯探針 restart 段 `FallingLoop` 進入時刻在跑與跑之間翻轉）。
> - 修法：第一個失地 tick elapsed＝0、每 tick 累積 `Time.deltaTime`、`Time.frameCount` 守門（同帧多次 `CanEnter` 只累積一次）。門檻仍是時間。範圍只有這兩處（其他 `Time.time` 使用與 grounded／falling 無關）。
> - 測試：`GroundLossGraceDeterminismTests`（26；修前 8 紅＝start 2.0／1000.0 的 7≠6、22≠21）；`GroundEdgeEventTests`、`StateMachineConfigTests` 改新簽名；PlayMode `T9` 原本以 `WaitForSeconds` 不 tick 的牆鐘等待 ⇒ 改為逐帧 tick（語意差異：未被觀察的帧不計時；runtime 每帧 tick，無差異）。
> - 回歸：EditMode **846／845／0 failed／1 skipped**；PlayMode **96／95**（只有既有 `C0`）。樓梯 36 探針連跑兩次：**Foot IK／骨盆／支撐、locomotion、root、grounded 36／36 相同**；`FallingLoop` 一律在失地後 22 個記錄帧進入；剩 8 檔只有探針自己的 `cfSup`／`k25` 射線不同（未修，不升級）。
> - ✅ 使用者裁決（2026-10-07）：**暫不抽共用 timing primitive**——兩處各約 10 行、已有測試保護、Motion 不得依賴 StateMachine；出現第三個同類計時器或重整這一區時再評估（Core 小 struct）。
>
> ---
>
> # 🟧 下一會話開場：**R1c d0 = 3 cm 已裁決並在 X Bot 開啟（`docs/45` §24.7）；PlayMode 96／95（只有既有 C0）；EditMode 1 個失敗來自場景 GroundLayers override（非 R1c）**
>
> - 使用者 Play（2026-10-06）：3 cm 收腳直接完整、無明顯滑步、Walk RU 小步像正常短步、站立穩定；5 cm 仍見補回／較拖／偏碎步；下樓階緣「留在舊階高度」正常速度不明顯（已知限制）。
> - X Bot prefab：`LockedFollowExperiment` true、`LockedFollowDeadZone` 0.03（還原＝設 false）。
> - EditMode 820／818／1 failed：`StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable`——場景 X Bot 新增 `GroundLayers.m_Bits = 503` override（期望 −9），本會話開始後才出現、隨測試時的場景存檔落地；❓ 是否 revert 該 override。
> - PlayMode 全套回歸 **96／95／1 failed**（只有既有 `C0`）；Editor 重啟後執行。
> - 場景 X Bot `GroundLayers` override 已 revert（−9，只少 4 行；場景未 commit）⇒ EditMode **820／819／0 failed／1 skipped**。
> - 樓梯探針重現性：同設定連跑兩次 restart 段同樣分岔（stop 段 36／36 相同）⇒ nondeterminism；`FallingLoop` 進入差 1 帧，候選機制＝0.1 s grace 以 `Time.time` 絕對值相減、剛好 6 帧落在 float32 邊界（docs/45 §24.7）。❓ 是否另開修正（例如改成帧數或累積 dt）——本輪未動。
>
> ---
>
> # （前一段）**R1c runtime A/B 完成（`docs/45` §24）——使用者 Play 比較 Current／3 cm／5 cm 後裁決正式方向**
>
> **開場只需讀**：本段 ＋ `docs/45` §24.4–§24.6。
>
> - Runtime：`FootIKSettings.LockedFollowExperiment`（預設關 ⇒ 逐位元現行）＋ `LockedFollowDeadZone`；`FootContactSolver.ClampLockOffset`；Locked 仍 Locked、`LockPosition` 是原始錨點、釋放判準與垂直語意不動。X Bot **未開**。EditMode **820／819／0 failed／1 skipped**（+5）。PlayMode 回歸未跑（關閉路徑逐位元）。
> - 結果（72 次）：最後一步補回 Run LU re-step ×1.60 → ×1.20／×1.33、Walk ×1.26–1.37 → ×1.08–1.10／×1.12–1.16；hidden step 結束差距 9 → 3／5 cm、可見 57%／41%；lock／release 次數、鎖定時刻、最終踏面、骨盆 ΔY 三臂相同；loop 幾乎不變。代價：原本被鎖藏的爬行以動畫速度可見（每帧 ≤ 1.5 cm）。
> - 樓梯：①Walk RU 最後一步上樓 3 cm 單帧 ΔY 8.7 → 12.1（擺盪踏面切換提早 1 帧）②Walk RU 最後一步下樓 3／5 cm：鎖定中腳跟越過階緣、被鎖定支撐下限留在上一階高度 3–4 帧（依裁決未改語意）。
> - ⛔ 依裁決未做：選定 d0、開 X Bot、R2、P8b 合併、改 clip／playback、改 locked 垂直語意。
>
> ---
>
> # 🟧 下一會話開場：**T3 release-catch-up offline replay 完成（`docs/45` §23）——首選 R1c「鎖定點跟隨（死區 d0）」；待裁決是否進 runtime A/B**
>
> **開場只需讀**：本段 ＋ `docs/45` §23（表 23.2、23.4）。工具：`docs/artifacts/stop-snap/README.md` §5／§5.1。
>
> - Python 重播 T3 鎖定／釋放：**相位 99.953%、水平 p99 0.02 cm**（24 情境，平地＋樓梯、Walk＋Run）。
> - R1a（縮 UnlockRadius）／R1b（趨勢釋放）：重鎖循環、補回最差 ×4–10、停步可見滑動 13–19 cm ⇒ ❌。
> - **R1c 鎖定點跟隨**（gap 永遠 ≤ d0，超出部分以動畫速度可見）：停步最後一步補回 ×1.26 → **×1.08（d0 3 cm）／×1.12（5 cm）**；鎖定次數與時刻不變；loop 可見爬行 +0.4／+0.2 cm 中位；Walk RU hidden step 結束差距 9 → 3／5 cm。
> - R2 單獨不解決（Walk LU 最後一步 gap 不變、其他是不鎖 ⇒ 爬行全可見）；唯一好處是鎖定當下過衝 12.7 → 5.4 cm（A2 一側，維持獨立）。R3 只能當 safety（k1 拖曳 13 cm）。
> - ❓ 待使用者：①進 runtime A/B？（`FootIKSettings` 實驗開關、預設關；d0 0.03／0.05；量樓梯垂直——本輪未重播）②R1c 讓 `LockPosition` 會移動：消費者都在 FootIKController 內（Living Spec，不需 ADR），A/B 前要逐一檢查。
> - 「瞬變改善」＝ SwingFootLengthReference 的樓梯瞬拉改善（使用者 2026-10-04 確認）。一次性 pelvis-analysis 腳本 5 支已刪除（未追蹤檔）。
>
> ---
>
> # ~~🟧 收步「吸附感」診斷已完成（`docs/45` §22）~~ —— 已進 §23 replay（以下保留）
>
> # 🟧 （前一段）**收步「吸附感」診斷已完成（`docs/45` §22）——結論 C，但「快速拉過去」的時序是 T3 的鎖定／釋放；待使用者裁決下一個 slice**
>
> **開場只需讀**：本段 ＋ `docs/45` §22（尤其 §22.4 表、§22.7 建議）。工具與坑：`docs/artifacts/stop-snap/README.md` §4。⛔ 不需要重讀 §10–§21。
>
> ## 結論（2026-10-04，只量測、runtime 未改）
> - **動畫**：停步 clip 的收腳**不快**——最後一步／re-step 0.8–2.7 m/s，最後 100 ms 只剩 6–10 cm（loop 22–35 cm）；playback 與 loop 相同；runtime 收步＝clip 內容（V4 0.3–0.6 cm）。動畫提供的是「站著時的慢速爬行／起步」（< 1.4 m/s）。
> - **T3**：鎖定時機本身準（鎖時離停點 ≤ 2 cm）。問題是 β 只看速度 ⇒ 慢速移動的腳被鎖住，動畫在鎖下累積 9–11 cm，β < 0.7 時用 0.12 s 補回 ⇒ 最後一步畫面快 **×1.26–1.60**（loop 同機制只有 ×1.03–1.13）。Walk 逐帧：畫面 7 帧走 46 cm，動畫花 16 帧。
> - **Terrain**：Run LU re-step 的補回在平地／上／下樓相同（×1.6）；樓梯上同一段還要換 1–2 階（下樓每帧最高 8.6 cm 垂直）。
> - **Asset**：`RunFwdStop_LU` 最嚴重；Walk 最後一步次之；`RunFwdStop_RU` 幾乎乾淨；Walk RU 最後小步（15 cm）被鎖整段藏起來。
> - 次要：Run LU 煞車步一帧外插過衝 7–8 cm ＋回拉（P8b 開即消失；P8b 不影響釋放補回）。
>
> ## ❓ 待使用者
> 1. 是否開「T3 釋放補回」slice？建議**先離線重播** `StepLock` 三個候選（§22.7：補回速度限幅／鎖定改看 root 空間速度或位移停止／UnlockRadius 降到爬行量級），再談實作。⛔ 此輪沒有證據支持動 clip 或 playback。
> 2. Walk RU 最後小步被隱藏是否要處理（「該動沒動」，與吸附相反）。
> 3. handoff 原本的「瞬變改善指哪個開關」仍未確認（不影響本結論；本輪量的是 X Bot 現行設定，P8b 關）。
>
> ## 本輪 commit
> - `697a71b` docs(research)：補 commit `brake.py.txt`、`edges.csv.txt`（docs 已引用）。其餘 5 個未引用的 pelvis-analysis 腳本（attrib／baseline／decomp／replay／stepflip_ab）依賴已移除的 StopLandingProbe ⇒ 判為 throwaway，仍未追蹤，❓ 是否刪除。
> - 研究工具 commit（`PlantSettleProbe` 擴充＋`docs/artifacts/stop-snap/`）與文件 commit（§22、map、本段）見 `git log`。
>
> ---
>
> # ~~🟧 下一會話開場：收步時的「吸附感」——先驗證是否來自停步 clip 本身~~ —— ✅ 已由上方 §22 回答（以下保留原交辦）
>
> **開場只需讀**：本段 ＋ `docs/45` §19.2（LU 收腳時動畫腳在 T3 鎖下滑動）、§21（三個 track 的現況）、ADR-011。⛔ 不需要重讀 §10–§18。
>
> ## 使用者 Play 回報（2026-10-04，原話）
> - 「拖步有變好」⇒ B／ADR-011 的 AC4 **使用者確認改善**（仍是 Trial；AC2 未達 9–11 cm 的殘留未被要求處理）。
> - 「關於腳在收步時的變化，我認為可能跟動畫本身的 clip 有關，因為即使瞬變改善了但還是有吸附感」。
>   - 「瞬變改善」所指未確認（可能是 SwingFootLengthReference 的瞬拉、或 A2 開關的晚修正）——**開場先問清楚是哪一個開關／哪個情境**。
>
> ## 建議的第一步（只量測，不改 runtime）
> - 假設：「吸附感」＝ T3 把腳鎖在落地點，而停步 clip 裡的動畫腳**在鎖下仍在滑**——鎖越久、身體越往前，最後釋放時腳被拉走（§19.2：LU 左腳鎖住 18 帧期間動畫腳滑 12.7 cm；§21.1：RU 鎖住時動畫腳滑 p90 7.4–8.4 cm）。
> - 驗證 clip 本身：對 4 支 Stop clip（Run／Walk × LU／RU）用 Bake 資料或 preview scene 取樣，量每次 planted 期間腳在**世界**中的水平速度（root 位移＋腳在 root 空間的變化）——clip 自己就在滑 ⇒ 依 CLAUDE.md 動畫資產升級順序處理（資料／Presentation 參數 → 換 clip → 最後才改 clip 內容），⛔ 不先動 T3 鎖。
> - 對照：同一指標量 Run／Walk loop 的支撐期，區分「停步 clip 特有」與「所有 clip 都有」。
>
> ## 三個 track 的現況（不變，各自獨立）
> - **A1**（`94ba7ca`，只量測）：強制 RU ⇒ re-step 消失、停步 +0.41 m；地形感知選片待裁決。
> - **A2**（`c5ff446`，實驗開關 `PlantAwareExtrapolation` 預設關）：視覺 A/B 待使用者；§21.2 三個跨階緣情境待判讀。
> - **B**（`cc9b231`，ADR-011 🟡 Trial，X Bot 開）：使用者確認拖步改善；Accepted 前仍需決定 AC2（殘留 9–11 cm）是否放寬。
> - PlayMode 96／91：`C0` 既有＋`Climb2m` ×2、`CO5` 既有 flaky＋`Enemy_TraversesGeneratedStair(Stairs (1),Up)` 邊界抖動。
> - ⛔ 約定未變：不擴 LandingSurfaceResolver、不動 SwingFootLengthReference、不先動 release threshold。
>
> ## 工具
> - `PlantSettleProbe`（`[Explicit]`）：`Probe_RunStopSettleRestart`／`ForcedRU`／`PlantAwareExtrapAB`／`EntryPhaseFlatAB`／`EntryPhaseStairsAB`／`RestartPhase*`；輸出 `Temp/PlantSettle/`（Unity 重啟會清空）。分析 `docs/artifacts/plant-settle/`（`timeline.py.txt` 單腳逐帧最好用）。
> - Bake 匯出：`docs/artifacts/pelvis-analysis/export_bake_disp.eval.cs.txt`（複製成 `.cs` 再 `unity cmd eval_file`）。
> - ⚠️ 排 PlayMode 前單獨查 `isPlaying`（使用者可能正在 Play）；`recompile` 沒吃到新檔時用 `AssetDatabase.Refresh(ForceSynchronousImport)`＋`RequestScriptCompilation`。
>
> ---

> # 🟨 2026-10-04（四）— **A1 強制 RU 量測／A2 P8b runtime 開關／B ADR-011 起跑入場相位對齊（Trial，X Bot 開）——三個獨立 commit；⏳ 人工 Play**
>
> 📖 正本：`docs/45` §21、`docs/ADR/011-run-entry-phase-alignment.md`。EditMode **815／814／0 failed**。
>
> ---

> # 🟦 2026-10-04（三）— **A1／A2／B 三個獨立研究（只量測）：A1 決策在 LocomotionModel、避開 LU 代價已量；A2 P8b 消除停步錯階暫態；B 起始 phase 對齊左腳著地極值讓起跑貼地滑動降到 ≤ 8 cm**
>
> 📖 正本：`docs/45` §20。
>
> ---

> # 🟦 2026-10-04（二）— **Play 可見問題診斷（只量測）：收步後又動＝LU clip 第二次左腳落地（主要）＋煞車步一帧外插過衝；起跑拖步＝B3 crossfade 貼地滑動；runtime 未改**
>
> 📖 正本：`docs/45` §19。研究工具 commit `b7bb22c`（LandingSurfaceProbe＋§10–§18 腳本；不含 edges.csv 匯出結果與依賴已移除欄位的 StopLandingProbe 系腳本）。
>
> ---

> # 🟨 2026-10-04 — **停步最後一兩步仍瞬拉：成因拆解（A 腳尖／腳跟伸進 riser 為上樓主因）→ 擴展 riser 侵入規則 ❌ 否決並還原 → 擺盪腳長參考（X Bot 開）：Walk 上樓瞬拉 175 → 71、移動中 177 → 79；Run 下樓未解；⏳ 待人工 Play**
>
> 📖 正本：`docs/45` §18。EditMode **795／794／0 failed／1 skipped**（+5）；PlayMode **96／95／1 failed**（既有 `C0`；X Bot 已開）。
>
> - §17 的「落腳窗單帧」指標低估了問題（只看最後幾帧）；改用二階差分後，resolver 只少了約四分之一的停步瞬拉。
> - riser 侵入擴到擺盪：侵入一開始就 4–9 cm 深 ⇒ 腳鎖到錯的階、腳尖埋進 riser、骨盆跳 13.7 cm ⇒ 使用者裁決還原（程式、測試、設定已移除）。
> - 新：`FootIKSettings.SwingFootLengthReference`（擺盪參考＝三點下方移動面最高者）＋ `FootIKController.FootLengthReference`＋5 個測試；探針 A/B 驅動改為設定委派。
>
> ---

> # 🟨 2026-10-03（八）— **T3 landing-surface Phase 2：`LandingSurfaceResolver` 實作並接進 T3（X Bot 開）——上樓跨階跳動 Run 17 → 6、Walk 19 → 9 cm；下樓不變（幾何限制）；骨盆／tread flip 不變；no-intent 逐位元；⏳ 待人工 Play**
>
> 📖 正本：`docs/45` §17。EditMode **790／789／0 failed／1 skipped**（+22）；PlayMode **96／95／1 failed**（既有 `C0`；X Bot 已開 resolver）。
>
> - 新檔：`Presentation/IK/LandingSurfaceResolver.cs`、`Tests/EditMode/LandingSurfaceResolverTests.cs`、`docs/artifacts/pelvis-analysis/{outcome,verify_window}.py.txt`。改：`FootIKController`（T3 ⑤，只在 Unlocked）、`FootIKSettings`（`UseLandingSurfaceResolver` ＋ 9 個參數）、`LandingSurfaceProbe`（A/B 方法、intent／骨盆欄位）、X Bot prefab（Editor API；重存同時清掉 2 個已不存在的欄位、寫出 2 個等於程式預設的欄位——行為不變）。
> - ADR：只改 T3 內部擺盪語意 ⇒ Living Spec（`docs/45` §17），ADR-010 不動，沒有新黑板欄位。
> - A/B 抓到並修正四個缺陷（§17.2）：即時驗證自己產生 intent／目的地只在最後幾帧起作用／釋放瞬間就開驗證窗（Walk 下樓鎖定跳 44 cm）／錯的高可信度目的地退不完。
> - 高可信度路徑**不是**「幾乎不改答案」：即時驗證修正 §15 的語意案例（預測 XZ 準、踏面規則不同）——在 §16.1 裁決的前提下這是預期的。
>
> ---

> # 🟦 2026-10-03（七）— **Phase 1e：T3 支撐語意完全重現（247／247）；落地姿勢（Bake plant 時刻，甚至實際鎖定姿勢）都無法修正剩餘 6 次語意不一致——它們在 T3 規則下是位置敏感 ⇒ 不提 ADR-010 擴充；⛔ 只量測**
>
> 📖 正本：`docs/45` §15。in-engine 以反射呼叫 T3 的 `AccumulateContact`（probe side file `sem_*.csv`）；分析 `semantics.py.txt`／`semantics_b.py.txt`。離線重寫規則只有 204／247 一致，已作廢。
>
> - B0 現行 69% → B1（Bake plant 姿勢）71% → B2（實際鎖定姿勢）71%；semantic 6 次全部 0／6；逐帧正確率 83 → 77%。
> - 剩餘煞車步錯誤＝位置精度（Phase 1c 不修的 plant 時刻差 ~3 cm ＋ 1–2 cm）在跨階緣腳印上的結果；遲滯不處理。
> - ❓ 待使用者（§15.5）：①以目前能力進 Phase 2 ②重新考慮 Phase 1c 修正 2。
>
> ---

> # 🟦 2026-10-03（六）— **Phase 1d：ADR-010 D1-R1（next plant 自 variant committed 起發布）——煞車步首次可用 8 → 10 帧、首次承諾錯 6 → 2、早於 T3 跳動 1 → 26／35（lead 中位 +1 帧）；剩下錯一階約一半是落地規則與 T3 不一致，不是邊界位置；⛔ T3 未改**
>
> 📖 正本：`docs/45` §14、ADR-010 D1-R1。EditMode **768／767／0 failed／1 skipped**。`Active`（結束站姿）路徑不變，Run 最後落地結果不變。
>
> - Run 停步沒有 pending ⇒ 只提前等待播放頭的 2 帧；放開前沒有停步決定，這是上限。進入期發布的準度與播放中相當（81％／5.4 cm vs 83％／4.9 cm）。
> - 剩餘錯一階（11／35）：以腳踝計不集中在階緣（只有 3／11 在 5 cm 內）；以腳印計每一次都靠近階緣；在 T3 實際鎖定 XZ 上「平放三點最高」與 T3 支撐只有 29／35 一致 ⇒ 6 次是規則不一致。
> - ❓ 待使用者（§14.5）：①以目前能力進 Phase 2 ②先讓目的地落地規則與 T3 鎖定支撐規則一致（footprint 語意，非遲滯）。
>
> ---

> # 🟦 2026-10-03（五）— **Phase 1c：Run 煞車步 ~9 cm 偏差＝兩個時間定義；修正 1（姿勢比 root 早一次評估）已實作：偏差 9.2 → 4.4 cm、正確階 54 → 83%；修正 2（Bake plant 時刻 vs T3 鎖定）待裁決；⛔ T3 未改**
>
> 📖 正本：`docs/45` §13、ADR-010 D5（時間基準一行）。分析 `bias.py.txt`／`timebase.py.txt`。EditMode **768／767／0 failed／1 skipped**。
>
> - T3 比 Bake plant 早 5 帧鎖定；ΔXZ 不能由 Δt 內 root 前進解釋（root 25.6 cm）。root 對齊 `NormalizedTime`，畫面姿勢領先一帧（104／104）。
> - 修正 1：root 前進量算到「plant − 本帧實測 clip 前進量」（時間定義，非 magic offset）。Run 最後落地也變好（97%、0.6 cm）。
> - ❓ 待使用者（§13.5）：①修正 2（Bake plant 改用 T3 鎖定規則＋重烘 4 支資產）或接受約 4 cm 直接進 D1「提早發布」②結束站姿是否統一時間定義。
>
> ---

> # 🟦 2026-10-03（四）— **Phase 1b：ADR-010 D5 next-plant snapshot 接上（T3 未改）——最後落地成立（Walk 早 7–8、Run 早 9–14 帧，不需門檻）；Run 煞車步仍不成立（N 鎖定前 8 帧才出現＋~9 cm 系統偏差，0／35 早於跳動）**
>
> 📖 正本：`docs/45` §12、ADR-010 D5。EditMode **768／767／0 failed／1 skipped**。4 支 Stop 資產直接提供 plant 事件（沒有重新烘焙）。
>
> - 快照 +4 欄位：`HasNextPlant`／`NextPlantIsLeft`／`NextPlantAnkleInPublishRoot`（publish root 座標，含前進量）／`NextPlantIsFinal`（＝排程最後一次 planned plant，⛔ 不是 clip 結束或最後接地）。
> - 首次承諾錯：Run 最後落地 5%、Walk 最後落地 18%；改答案最晚在鎖定前 1 帧；晚期外插只會增加錯誤，不採用。Walk 中途落地沒有目的地資訊（輸出 no-intent）。
> - ❓ 待使用者（§12.5）：①先只對最後落地實作正式 resolver 並進 Phase 2 ②或先處理煞車步（提早發布＝修訂 D1 條件 2／校正 ~9 cm 偏差）③`RunFwdStop_RU` 同腳雙 plant 是否當偵測器瑕疵。
>
> ---

> # 🟦 2026-10-03（三）— **T3 landing-surface slice Phase 1：destination resolver 離線評估——最後落地用預測可提前 6–8（Walk）／17–21（Run）帧穩定、加即時驗證後鎖定時 0 錯；但執行期判斷不出「是不是最後落地」、Run 煞車步沒有可信來源 ⇒ Phase 2 前提未成立；⛔ T3 目標未改**
>
> 📖 正本：`docs/45` §11。`LandingSurfaceProbe` 加候選欄位重跑 208 次；離線策略重播 `docs/artifacts/pelvis-analysis/resolver.py.txt`。
>
> - 來源：P（停步預測，只描述各腳最後站姿；最後落地 XZ 誤差中位 1 cm、中途落地 0.5–0.8 m）、X（速度外插；下樓有系統偏差——到接觸時間用平地 pose 平面高度，約 15 帧前就外插到支撐腳那一階）、L（現況）。
> - 11% 錯一階：首次承諾降不了（預測水平誤差 p90 6–9 cm；跨階緣辨識不出來）；最後 3 帧即時驗證可讓鎖定時 0 錯，但這些仍在最後幾帧改答案。
> - Run 煞車步：只有 X，鎖定前中位 3 帧才穩定（跳動在約 7 帧前）⇒ 現有來源不行。上下樓可共用同一契約。
> - ❓ 待使用者（§11.8）：①以 resolver 為正式 consumer 開 ADR-010 獨立擴充（快照加「下一次 plant」）再重量 ②或先試 X 的世界高度修正 ③接受階緣站姿約 10% 晚改答案為固有限制。
>
> ---

> # 🟦 2026-10-03（二）— **Landing-surface continuity：擺盪腳的落腳面追著「腳目前所在階」走，跨到不同階時在鎖定前 1–4 帧才切（單帧 8–17 cm）；預測早 7–23 帧就指向最終階；⛔ 只量測、runtime 未改**
>
> 📖 正本：`docs/45` §10（獨立於 `docs/46` §35）。throwaway `LandingSurfaceProbe`（208 次停步：兩道樓梯 × Walk／Run × 上／下 × 斜向 0°／20° × 13）；分析 `docs/artifacts/pelvis-analysis/landing.py.txt`。
>
> - 機制：T3 擺盪目標＝移動斜坡（CharacterController 用，沿踏面前緣）＋ 動畫踝高 ＋ lift（∝ β，拉向腳跟／踝／尖**目前**所在踏面）⇒ 沒有「這一步落到哪」的資訊。跳動多在**腳踝點跨階緣**時（鎖定前 4–8 帧），之後 β 上升把它拉到位；ground miss 0 次。
> - 上下樓是同一機制的正負版本；斜向不是成因（0° 與 20° 幅度相同，只是較常落在不同階）。同一階也有 β 驅動的「斜坡 → 踏面」分量（Walk 上樓中位 10–12 cm）。
> - 與 FU-14 同家族（只看目前 XZ 的地面），不是同一缺陷：這裡是延遲，取樣本身穩定（A→B→A 1.7%）⇒ 不需要 footprint／遲滯。
> - 單獨「擺盪開始時 latch 預測落腳面」不夠：第一次可用時 11% 錯一整階（階緣站姿）、Walk 預測常在擺盪中段才出現、Run 煞車步沒有預測（而它們是最大的跳動 20–24 cm）。
> - ❓ 待使用者（§10.6）：開 T3 landing-surface slice？(a) 目的地過渡（修訂 §3 擺盪語意；預測＋即時驗證，無預測時外插）／(b) 先只把擺盪參考從移動斜坡改成踏面（§9.4 F1b 同一題）；Run 煞車步是否納入。
>
> ---

> # ✅ 2026-10-03 — **工作樹對齊 HEAD（`155b698`）後全套測試：EditMode 767／766／0 failed／1 skipped；PlayMode 96／95／1 failed（既有 `C0`）**
>
> - 依 runtime responsibility 切出的正式 commit：`33da8c4` riser rule → `1fc8017` 統一骨盆求解器 → `e5ff3b3` ADR-010（🟡 Trial）停步預測快照 → `6a4017a` X Bot 啟用 → `109488f` docs → `155b698` Walk Fix C。對應見 `docs/46` 檔頭。
> - 對齊：14 個 tracked 檔改回 HEAD 內容；`FootPlantScheduleBaker`、`StopLandingProbe`、`StairFeelProbe`（＋.meta）移出 `Assets/`。**原型完整備份在 `.prototype-backup-20261003/`**（`files/` 原路徑複本＋`tracked-vs-HEAD.patch`＋README；未追蹤）。Editor 確認：快照 6 欄位、無 plant baker、無 freeze 欄位。
> - EditMode 比原型少 4 個＝ADR-010 D3 排除的 3 個 Planned Stop 測試＋plant 偵測器測試。首次在 Unity 跑的 `A12`、`A5`、`Model_PublishesAndClearsNumericStopPrediction` 全過 ⇒ ADR-010 AC1 ✅。
> - PlayMode 唯一失敗 `C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact`：`30aa459` 引入、§19.2 起就已登記的既有失敗，與本系列無關。樓梯／停步相關 30 個全過（含 `Player_UnifiedMoving_*`／`Player_UnifiedStanding_*`）。
> - ⏳ 仍待：ADR-010 AC2／AC3 在正式版本重跑 probe（probe 已移到備份；它依賴的 THROWAWAY 觀測欄位不在正式程式中，要重跑需另寫正式版 probe 或暫時還原）；AC4 與 §35.4 人工 Play。
> - 工作樹仍未 commit：場景（`Stairs (1)`、`__StairMovementSurface`）、音量、材質、ProBuilder 設定、`docs/05` 樓梯 clip 盤點、`docs/artifacts/pelvis-analysis/`。
>
> ---

> # 🟨 2026-10-02（三）— **下樓停步：正在落地的尾腳也能往下拉（Walk 採用，只在下樓停步）：Walk ≥5 cm 帧 7 → 0、單帧最大 8.2／6.5 → 2.5／4.7 cm、上樓 52／52 逐位元不變；Run 刻意不處理，等 Play 判斷；⏳ 待人工 Play；⛔ 未 commit**
>
> 📖 正本：`docs/46` §35（裁決、實作、驗收、Play 清單）；§19 規則② 已加修訂註記。
>
> - `MovingPelvisSolver`：新鎖存 `StopDescending`（放開時 root 垂直速度 < −0.2，不分步態）；SupportTransfer 中，Landing 尾腳的上限併入下拉。開關 `FootIKSettings.StandingDescendingTrailingLandingPulls`（預設 true；**還原＝false**，逐位元回到之前）。
> - EditMode **771／770／0 failed／1 skipped**（+5）。runtime 104 次：T3 腳目標 0 差異；Run 下樓幾乎不變（16／20 個 ≥5 cm 帧保留——依裁決，先在遊戲鏡頭下判斷）。
> - Landing 角色閃爍 → **FU-14**（Follow-up 登記表，不排程）。
> - ⏳ **人工 Play**（§35.4）：①Walk 下樓中段停下，最後一步還會不會頓一下 ②**Run 下樓中段停下，煞車步落地時約 8 cm／1–3 帧的下沉在遊戲鏡頭下明不明顯** ⇒ 決定要不要重開 §33 ③上樓／平地停步回歸。
> - 新增 throwaway：`StopLandingProbe.Probe_DescendingLandingPullAcceptance`、`Probe_DownStopBrakeControl`。
>
> ---

> # 🟦 2026-10-02（二）— **下樓停步煞車：來源拆解完成——每一個 ≥5 cm 單帧都是「腳鎖定時的 §27 硬夾 activation」；Run＝煞車步、放開後時間窗不足（完美預知也只能部分解）；Walk＝規則②把正在下降落地的尾腳也排除了；⛔ 只量測、runtime 未改、未 commit**
>
> 📖 正本：`docs/46` §34。對照組 throwaway `Probe_DownStopBrakeControl`（ω 1.5，52 次，重現 §33.4 的「前」）；重播 `docs/artifacts/pelvis-analysis/brake.py.txt`（誤差中位 0.01、最大 1.5 cm）。
>
> - Run：煞車步的腳放開後約 12 帧鎖定，硬上限比骨盆低 15–25 cm（ω 1.5 時記憶已經先蹲好）。Landing 預告失效：κ·h 放寬，加上角色隔帧閃爍（SoleHeight 跨階緣／地面漏打）。8.3 cm＝`PelvisSmoothSpeed`；套用值落後硬上限 1–3 帧，reach debt 最大 17–23 cm。
> - Walk：SupportTransfer 只讓 Locked 腳往下拉；尾腳 Landing 的上限 −29～−38 提前 10 帧以上就知道，卻被忽略 ⇒ 鎖定時一帧補 5–8 cm。
> - 離線候選：**C（下降落地中的尾腳在 SupportTransfer 也能往下拉）⇒ Walk ≥5 cm 帧 7 → 0、單帧 ≤ 4.4、無 debt**；上樓停步改變 ≤ 0.9 cm（加上「下樓停步」閘即可為 0）。Run：B（去閃爍＋Landing 投影）proto 17 → 7、s1 20 → 14；Oracle（完美預知）k＝12 時 proto 2、s1 7——剩下的需求觸及 −35 且放開後時間不夠。
> - ❓ 待使用者（§34.7）：①Walk 採用 C？②Run：(a) 接受／(b) B／(c) 重開 §33 降低 Run 下樓移動中的 ω（建議先離線掃描）／(d) 擴大停步預測（範圍外）。③Landing 閃爍是否另外登記。
> - 本會話新增檔案：`StopLandingProbe` 的 `_descendRiseOmegaOverride`＋`Probe_DownStopBrakeControl`（throwaway）、`brake.py.txt`；`docs/46` §34、`docs/00-map` 指標。probe CSV 已複製到 session scratchpad（`Temp/StepFlip/*_w1.5.csv` 仍在，Unity 重啟會清空）。
>
> ---

> # 🟧 2026-10-02 — **下一會話開場：「下樓停步煞車」slice（使用者已裁決開）**
>
> **開場只需讀**：本段 ＋ `docs/46` §33（尤其 §33.4 的停步回歸數據與 §33.6 裁決）＋ §27（硬夾 owner 規則）＋ §19（SupportTransfer 語意）。量測方法與坑：memory `final-bone-measurement` ＋ `docs/46` §30.2。⛔ 不需要重讀 §20–§32 的研究過程。
>
> ## 問題（§33.4）
> `MovingPelvisDescendRiseOmega` 6（已保留）讓下樓**移動中**的下蹲變淺（Walk −30 → −22、Run −26 → −15 cm）。但下樓**停步**的煞車步仍要求 −14～−32 cm（Landing 約束）——以前被移動中的「記憶」預先蹲好，現在放開後才下沉：
> - Run 下樓停步：放開後 60 帧內下沉中位 15–19 cm（最大 30），單帧 8.3 cm（速度上限），多在停步後 ~11 帧；13 次停步合計 16–20 帧單帧 ≥ 5 cm。
> - Walk 下樓停步：下沉 ~7 cm（最大 11），較平緩。
> - 最低點與最終高度與改動前相同 ⇒ 問題是**時序與單帧速度**，不是深度。
>
> ## 範圍
> - ✅ 停步語意（StopHold／Stand／SupportTransfer）、放開後的下沉如何發生（誰綁定、是否硬夾 activation、`PelvisSmoothSpeed` 8.3 cm／帧上限）。
> - ⛔ 不動：上樓（ω／toe／heel 都不改，§32 只拆來源）、移動中的 ω、T3（riser rule、Locked 合約、落腳）、預測、Heel Lift、Planned Stop。
> - 建議開場第一步（只量測）：用 `Probe_StepFlip`（Down、rule 開）＋ `Probe_BodyJerkEvent` 的欄位，對每次下樓停步拆「放開後第一個 ≥ 5 cm 單帧」的來源（綁定腳／角色／是否 §27 activation／Primary 狀態），再提修法。對照組：`MovingPelvisDescendRiseOmega` 設 1.5 的同一事件。
>
> ## 目前程式狀態（全部⛔未 commit，工作樹是大量跨會話 WIP；commit 切法由使用者決定）
> | 項目 | 狀態 |
> |---|---|
> | §27 硬夾「約束加入／換腳」不承接速度 | 已實作＋測試；⏳ 人工 Play 未回報 |
> | §33 下樓回升 ω 6（`MovingPelvisDescendRiseOmega`） | 已實作＋3 測試；X Bot 用程式預設；還原＝設 1.5 |
> | §23 riser-intrusion rule | X Bot prefab **已開**（§25，Editor API；還原＝設 False） |
> | §21 Locked sample-frame freeze | 已否決、預設關，程式仍在——❓ 是否移除 |
> | §24 XZ compliance | 已移除 |
> | throwaway | `StopLandingProbe` 內多個 probe（StepFlip／StepFlipStopHold／Smoke／RiserRuleAcceptance／BodyJerkEvent／WalkDescentLeg／FinalBoneBaseline）；`FootContactState.Debug*`、`MovingPelvisState.Debug*`（只寫不讀） |
> | 測試基準 | EditMode **766／765／0 failed／1 skipped** |
>
> ## 已知、尚未處理（不在下一個 slice）
> - 上樓深約束 ≈ 90% 斜面幾何（後腳在支撐後段離 root 0.30–0.35 m）⇒ 需 T3／步態 slice（stride／提早離地）；toe-first／heel-lift 估算 ≤ 3 cm（§31–§32）。
> - §25 B 類：proto Walk 上樓最後落腳 1 帧腳尖 +18 cm 尖峰（Unlocked）。
> - §27.4：Run 上樓剛放開時同一 Support 腳硬上限一帧移動 7–10 cm（a95 是動畫 2 倍）。
> - §30.4-6：root 在 IK 之後才移動 ⇒ 樓梯上畫出來的腳相對 IK 目標每帧偏 Walk ±2.2、Run ±4.5 cm。
> - 下樓落地前 3 帧仍懸空 10–16 cm（T3 落腳；§28／§30）。
>
> ## 工具
> - 分析腳本：`docs/artifacts/pelvis-analysis/*.py.txt`（CSV 路徑要改）。probe 輸出在 `Temp/StepFlip/`——**Unity 重啟會清空，跑完立刻複製出來**。
> - PlayMode：`unity cmd run_tests --mode PlayMode --async_tests true --filter <name> --include_explicit true`；送出後確認 `isPlaying=True`（本會話遇過請求被丟掉、狀態檔是舊的）。
> - Codex MCP 會在 30 分鐘無回應時逾時，但背景仍會繼續跑完並寫檔。
>
> ---

> # 🟨 2026-10-01（六）— **上樓深約束拆解完成（≈90% 斜面幾何）；下樓回升 slice 已實作（ω 6）：移動中 Walk −30 → −22、Run −26 → −15，平地／上樓逐帧不變；⚠️ 下樓停步出現放開後下沉（Run 15–19 cm 含 8 cm 單帧）；⏳ 待裁決；⛔ 未 commit**
>
> 📖 正本：`docs/46` §32（上樓拆解）、§33（下樓 slice）。
>
> - §32：上樓後腳深需求 ≈ 「root 底下 vs 後腳底下的移動面高差」（24–37 cm）；扣掉後與平地相同。踏面離散化 ≈ 0、動畫已抬腳被按住 ≈ 10%。
> - §33：`MovingPelvisDescendRiseOmega` 6（還原＝1.5）。EditMode **766／765／0 failed／1 skipped**（+3）。
> - 停步回歸：上樓 0 差異；下樓停步煞車的 Landing 需求以前被「記憶」預先蹲好，現在放開後才下沉。
> - ❓ 待使用者：①接受 ②只對 Walk ③另開下樓停步煞車 slice。
>
> ---

> # 🟦 2026-10-01（五）— **骨盆下壓歸因：一半以上是回升慢累積的「記憶」，不是此刻的需求；toe-first／heel-lift 離線估算最多改善 3 cm ⇒ 暫不做；⛔ 只量測**
>
> 📖 正本：`docs/46` §31。最終骨骼記錄器加讀求解器狀態；離線重播（誤差中位 ≤ 0.17 cm）。
>
> - 實際 −25～−34 vs 此刻要求 −3～−14 ⇒ 記憶 14–26 cm。綁定者：Walk 上樓＝後腳 Support；Walk 下樓＝前腳 Landing；Run 上樓＝後腳 Support（需求超過 −35 下限）；Run 下樓＝前腳 Landing。
> - 重播：Run 下樓 ω 6 ⇒ −26 → −13、起伏不變；Walk 下樓 ω 3 ⇒ −27 起伏不變（ω 6 ⇒ −21、p90 +3 cm）；上樓加快回升 ⇒ Walk 起伏 p90 29 cm、Run a95 201（更糟）。
> - ⚠️ 上樓後腳深需求是否為 T3 釋放太晚：未定論（量到的差距含斜面 vs 踏面落差）。
> - ❓ 待使用者：①下樓開「回升動態」slice（依方向分開 ω）②上樓先拆解後腳需求。
>
> ---

> # 🟦 2026-10-01（四）— **最終骨骼基準完成：問題是「整體下蹲」與「Walk 上樓過顛」，不是「骨盆太平穩」；⛔ 只量測**
>
> 📖 正本：`docs/46` §30。throwaway `Probe_FinalBoneBaseline`（LateUpdate 最後執行讀最終骨骼、OnAnimatorIK 讀 FK；兩道樓梯 × Walk／Run × 4 起點）。
>
> - 量測驗證：最終 Hips ＝ FK ＋ 上一帧 PelvisOffsetY ＋ root 在 IK 之後的位移（MotionDriver 在 LateUpdate 移動角色），殘差 ≤ 0.02 cm。舊 probe 在協程讀骨骼＝上一帧姿勢，已棄用。
> - 動畫本身就有起伏（平地 Walk 6.5、Run 11.6 cm／步），下樓與 Run 上樓的最終起伏 ≈ 動畫；Walk 上樓 p90 ≈ 20 cm（過顛）；Run 上樓 a95 是動畫 2 倍。
> - 共同問題：平均骨盆偏移 −25～−34 cm、支撐膝 p10 70–81°（動畫 122–155°）；下樓鎖定前 3 帧仍懸空 10–16 cm。
> - ❓ 待使用者：確認修正後的判準（以 FK 為基準的相對值，§30.5）。
>
> ---

> # 🟦 2026-10-01（三）— **方向重估：骨盆改為「硬可達包絡＋有界相位回升」、腳的姿勢（前掌著地／抬跟／擺盪外觀）歸 T3；先量四情境世界骨盆基準；⛔ 未實作**
>
> 📖 正本：`docs/46` §29（文獻、UE5 Foot Placement `heel_lift_ratio`、Codex 討論 thread `01a0f7b3-…`）。
>
> - 文獻證實 §28 的鏈條：下樓腳跟先著地 ⇒ 前腳要伸更低 ⇒ 後腳屈膝更大；人在 >10 cm 台階偏好前腳掌先著地。
> - Codex：不回到 support-driven；建議判準 Walk 世界骨盆每步峰谷 4–8 cm、Run 6–10 cm；前掌著地／抬跟需要 T3 新的接觸子相位（會改 LockPosition 語意 ⇒ Trial ADR）。
> - ⚠️ 量測缺口：§12–§28 的骨盆都是 `PelvisOffsetY`，不是世界骨盆。
> - ❓ 待使用者：是否先做四情境 phase-binned 基準量測（不改程式）。
>
> ---

> # 🟦 2026-10-01（二）— **Walk 下樓前腳折膝研究：擺盪中段膝角 46–70°（動畫 ~105°）是骨盆低的結果；骨盆低的原因是前腳伸直後仍差 15–20 cm 搆不到下一階；⛔ 只量測、未改**
>
> 📖 正本：`docs/46` §28。throwaway `Probe_WalkDescentLeg`（兩道樓梯連續下樓＋平地基準）。
>
> - 鎖定當帧：後腳膝角 72°（動畫 ~122°，平地 125°）、骨盆 −28～−31 cm、前腳鎖定前 5 帧仍懸空 8–10 cm（平地 1 cm）。
> - 每步：前腳約鎖定前 7 帧已伸直（≥170°）仍懸空 15–20 cm ⇒ Landing 需求 −27～−38 cm ⇒ 骨盆下沉、後腳 50–73°。
> - ❓ 待使用者：槓桿 A 前腳掌先著地（預估補 6–7.5 cm）／B 擺盪腳提早下降（消除中段折膝）／C 後腳抬跟；建議先離線量化 A。
>
> ---

> # 🟨 2026-10-01 — **pelvis／stand handoff slice 第 1 項：硬夾區分「約束移動」與「約束加入／換腳」；s1 #05／proto #09 不再衝到 −35，只剩 5.5／7.5 cm 必要夾動；⏳ 待人工 Play；⛔ 未 commit**
>
> 📖 正本：`docs/46` §27。`MovingPelvisSolver` 硬夾：owner 相同才承接上限變化率；新約束／換腳 ⇒ 位置立即合法、速度 `min(v, 0)`。
>
> - 啟用後速度 10.9 → **0.07–0.16 m/s**，收斂到需求（−23.1／−18.5），無二次過衝。
> - 208 次停步只有這 2 個改變；Walk 52 個與其餘全部逐位元相同；T3 目標不變。EditMode **763／762／0 failed／1 skipped**（+2）。
> - 範圍外（記錄）：Run 上樓剛放開時同一 Support 腳的硬上限一帧移動 7–10 cm ⇒ 承接 3.7–6.1 m/s，骨盆到 −35（修正前後相同）。
> - ⏳ 人工 Play：兩道樓梯 Run 下樓中段停下，看剩下的 5～7 cm 是否仍明顯。第 2 項（無預測時尾腳落地前不站直）暫不做。
>
> ---

> # 🟦 2026-09-30（三）— **「全身抖一下」定位：foot target 穩定；是 SupportTransfer 尾腳鎖定交接＋硬夾速度承接過衝（骨盆 3 帧 −13 → −35 cm）；⛔ 只量測、規則未改、未 commit**
>
> 📖 正本：`docs/46` §26。可重現事件：s1 Run 下樓 #05，停步後 t＝58（另 proto Run 下樓 #09 同型；Run 下樓 2／26、Walk 0／52）。
>
> - 兩腳 T3 目標／命中／支撐／LockedSupportY／髖位全程穩定 ⇒ 不是 B 類 landing。
> - 機制：尾腳 Unlocked 懸停（非 Landing 角色、Run 下樓無預測 cap）⇒ SupportTransfer 站直到 −12.9；尾腳鎖定 ⇒ 硬上限一帧 −0.3 → −18.4 ⇒ 硬夾速度＝上限階躍（−10.9 m/s）⇒ 衝到下限 −35，再以 ω6 爬回。StopHold 對照：無此抖動（proto #09 單帧 0.6 cm）。
> - §25 B 類腳尖尖峰不傳到骨盆（該帧無約束，骨盆 −29.5 不動）。
> - ❓ 待使用者：開 pelvis／stand handoff slice？槓桿①硬夾速度承接不吃約束集合的階躍 ②非批准停步尾腳未鎖定前不上升（§19.5）。
>
> ---

> # 🟨 2026-09-30（二）— **XZ compliance 已移除；X Bot 的 `LockedRiserIntrusionRule` 已開（原本是關的）；最小驗收：Walk 上樓 Locked 腳尖跳 10 → 0、s1 階角 3 → 0、釋放類不變；殘餘＝Walk 上樓最後落腳 1 帧腳尖尖峰（B 類）；⏳ 待人工 Play；⛔ 未 commit**
>
> 📖 正本：`docs/46` §25。
>
> - 移除後 runtime 與 §23 逐位元一致（驗收 39／39 逐帧相同）；EditMode **761／760／0 failed／1 skipped**。
> - ⚠️ §23 之後的人工 Play 都是在規則**關閉**下（prefab False、場景未覆寫）。已以 Editor API 設為 True（prefab diff 只新增 7 行，有效變化只有這個開關）；還原＝設回 False。
> - 殘餘：proto Walk 上樓停步 7／13 次，右腳最後落腳前 1 帧（Unlocked）腳尖埋進上一階 9 cm ⇒ +18 cm 尖峰、下一帧鎖定回落——B 類，依裁決只記錄。抬跟 #10／#07 屬第三類（不變）。
> - ❓ 待使用者：人工 Play 是否仍看得到（重點看上面那一帧）；§21 freeze 開關是否也移除。
>
> ---

> # 🟦 2026-09-30 — **裁決：Locked foot 維持 XYZ hard lock；Y hard ＋ XZ soft（2／4 cm compliance）不採用；⛔ 未 commit**
>
> 📖 正本：`docs/46` §24（規格 §24.1–24.8 由 Claude 先寫；Codex 實作＋6 組 × 104 A/B；Claude 審查 §24.9–24.10）。
>
> - 實作無偏離規格；EditMode **765／764／0 failed／1 skipped**（+4）；probe 629／629。原始資料與 Codex 報告 `.codex-temp/xzc/`（REPORT.md 在 MCP 逾時後仍完成；與 Claude 審查一致，另更正 Claude 誤報的 15 cm 釋放跳變＝不相鄰列的假象）。
> - rule 關：Locked flip 15 → **35／38**、取樣跨階緣 89 → **193／213**、每段鎖定累積腳滑 p95 **8／12 cm**、最終站姿 9／11 腳整階不同。rule 開：翻轉 2 → 1／0，但 riser 側向侵入 137 → 1111／1360 帧、>1 cm 真實穿透 5 → 703／763。
> - 鎖定中動畫偏移 **97% 是整體平移**（root 滑行，不是 pivot）⇒ 沒有「真正需要的半徑」，也沒有改 pivot anchor 的證據。
> - ❓ 待使用者：①移除 compliance 程式（建議）或保留 r＝0 死開關 ②Play 時 X Bot 的 `LockedRiserIntrusionRule` 預設**未開**——請勾選後再看一次。
>
> ---

> # 🟨 2026-09-29（十）— **riser-intrusion 幾何條件 ε＝4 mm：A 類 15 → 2（剩下全是抬跟，本輪不處理）、階角站姿 0 翻、Releasing 62＝62（±9 cm pop 消失）、B 類不變、無垂直穿透；⏳ 待人工 Play；⛔ 開關預設關、未 commit**
>
> 📖 正本：`docs/46` §23。新增條件：T3 接觸修正前的端點高度 ≤ 上一階頂面 ＋ ε。
>
> - ε 由三群決定：真側碰 −10～−7.6 cm；腳跟階角 −2.35～+0.14 cm；抬跟 −0.77～+3.71 cm（除 #10 起始 2 帧外 min +0.68）⇒ ε＝4 mm。
> - 抬跟誤判：#07 0 帧、#10 2 帧（起始帧在頂面下 0.77 cm，確實是幾何側碰）；#10 從 t＝24 起與現行逐帧相同。
> - 側向 riser 侵入 median 3／p95 8／max 13 mm（s1 #03 腳跟，t＝38–47 暫態；idle 1–3 mm）。鎖定相位／時長／旋轉逐帧不變。EditMode **761／760／0 failed／1 skipped**；probe 213／213。
> - ⏳ 人工 Play：X Bot 的 `LockedRiserIntrusionRule` **未開**，驗收需勾選（清單見 §23.6）。
>
> ---

> # 🟦 2026-09-29（九）— **Locked riser-intrusion rule A/B：A 類 15 → 0、鎖定相位／旋轉不變、無新增垂直穿透、B 類 192 → 192；代價＝2 個離地抬跟 episode 的釋放 pop（~9 cm ×2）；⛔ 未改 T3 policy、未 commit**
>
> 📖 正本：`docs/46` §22。條件：Locked ∧ 支撐已選定 ∧ heel／toe ∧ 腳踝底仍在原支撐 ∧ 高出 8–30 cm ⇒ 該點用 `LockedSupportY`。開關 `LockedRiserIntrusionRule`（預設關）。
>
> - 側向 riser 侵入 median 3.0／p95 11／max 21 mm；9 個 episode（toe 5＝Walk 上樓，heel 4＝Run 下樓），ankle 全在原支撐。
> - 最大案例人工檢查（proto Run 下樓 #10）：β 0.93→0.74、外轉 20°、動畫腳跟已抬到上一階高度 ⇒ 是合理的抬跟＋pivot，不是側碰；釋放時規則失效 ⇒ +9.7／−8.8 cm（Releasing +4 的全部來源）。
> - 三種 episode：真側碰（埋入 7.6–10 cm）／階角站姿（+1.6–2.3 cm）／離地抬跟（在頂面之上）。「埋入深度 > 0」可排除第三類，但 s1 #03 有 2 帧 −0.1 cm 的垂直 knife-edge。
> - EditMode **761／760／0 failed／1 skipped**；probe 213／213。
> - ❓ 待使用者：①接受現版 ②加埋入深度條件再跑 A/B ③第三類（鎖定中懸空 β 下拉被上一階整段取消）另案？
>
> ---

> # 🟦 2026-09-29（八）— **Locked sample-frame freeze A/B：A 類翻轉 15 → 0，但擋到合理 pivot（腳尖查詢與畫面脫離 max 6.8 cm、新增 18 帧 ≤2.1 cm 穿透）⇒ 全 freeze 不合格；提出更窄的 riser-intrusion rule；⛔ 未改 T3 policy、未 commit**
>
> 📖 正本：`docs/46` §21。104 次停步 × freeze 關／開。
>
> - B：A 類 **0**（A 15）；LockPosition 不變；鎖定相位與顯示旋轉與 A **逐帧相同**（旋轉沒被 freeze、鎖定時長相同）。
> - 那 6 cm：停步進 idle 時動畫腳**繞腳跟附近外旋 20–24°、停在 ~15°**，整段在同一次 Locked 內；所有情境鎖定中旋轉 p95 14.6° ⇒ 是合理 pivot。全 freeze 讓腳尖查詢點整個 idle 偏離畫面腳尖 4–7 cm。
> - 更窄候選（未實作）：查詢照舊跟著畫面腳底；Locked 時某點即時地面比 `LockedSupportY` 高一整階以上 ⇒ 判成側向碰 riser、用鎖定支撐（補上既有 `max(即時, 鎖定)` 缺的往上方向）。估算只在 8 隻 A 類腳啟動、正常小抬升 0 帧被擋、側向插入 p95 11 mm／max 21 mm。
> - 原型開關 `LockedSampleFrameFreeze`（預設關）。EditMode **761／760／0 failed／1 skipped**。
> - ❓ 待使用者：①接受「全 freeze 不合格」②下一輪用同一套 A/B 驗證 riser-intrusion rule？
>
> ---

> # 🟦 2026-09-29（七）— **停穩過程腳目標整階翻轉：第一個變的是地面取樣（heel／toe 射線在階緣 mm 級翻轉）；Lock 未刷新；與 SupportTransfer 無關；⛔ 只量測、未修、未 commit**
>
> 📖 正本：`docs/46` §20。104 次停步（2 樓梯 × Walk／Run × 上／下 × 13）逐帧；同一事件 StopHold 對照 3 次。
>
> - 277／277 個整階事件：同一帧必有端點命中高度跳一整階 ⇒ T3 目標同帧跟著跳。Locked 期間 LockPosition／LockedSupportY 變化 **0 次**。Reliable 翻轉期間維持 true。ST vs StopHold：骨盆差 14 cm，腳目標**逐位元相同**。
> - **A 類（Locked，最終站姿）**：proto Walk 上樓 7／13、s1 Run 下樓 1／13；Walk 下樓 0／26。鎖定只固定腳踝 XZ，端點隨動畫腳旋轉漂 1–5 mm，最終站姿的腳尖／腳跟正好離階緣幾 mm ⇒ 往上翻（`max(即時, 鎖定支撐)` 只擋往下）⇒ 目標 +18–19 cm 維持 5–18 帧再掉回。
> - **B 類（Unlocked 落腳，β 0.3–0.9）**：端點越過階緣 ⇒ 目標跳 β×級高 4–13.5 cm，多為單向；Walk 下樓停步的翻轉全屬此類。
> - 測試：EditMode **761／760／0 failed／1 skipped**（不變）。throwaway：`Probe_StepFlip`／`Probe_StepFlipStopHold`、`FootContactState.Debug*`（決策不讀）。
> - ❓ 待使用者：①看到的是 A（站定後抬高一階又掉回）還是 B（落腳那一下往下跳）②A 類最小修法候選：鎖定期間三端點 XZ 用**鎖定當帧的腳朝向**取樣（先驗證大幅轉向時是否該改變取樣）。
>
> ---

> # 🟨 2026-09-29（六）— **SupportTransfer runtime 原型：Walk 下樓停步全部達標方向（無 rise→drop、無 snap、無 reach loss）；上樓停步出現 rise→drop 10 cm；⏳ 待人工 Play；⛔ 未 commit**
>
> 📖 正本：`docs/46` §19。使用者規則 ①–⑦（Primary 後立即站直；尾腳 swing 不往下拉；Walk 下樓用 corrected prediction 當 FutureReachCap；cap 只限最高；尾腳 Locked 後改 live Support；預測不可用時保守但不整段 StopHold；預測只批准 Walk 下樓）。
>
> - 程式：`MovingPelvisRecovery.SupportTransfer`／`MovingPelvisCapSource`；`MovingPelvisFoot.Reliable`；context 帶 gait、root 垂直速度、兩腳預測落點；`FootIKSettings.StandingUseSupportTransfer`（預設 true，false＝StopHold）。
> - **26 次 Walk 下樓**（StopHold → SupportTransfer，同一停步事件）：開始站 0.69 → **0.29 s**、尾腳落定時差 18.8 → **11.6 cm**、到最終 1.67 → 1.33 s；rise→drop 0、單帧 1.05 cm、reach loss 0；knife-edge 只是較晚站直。
> - 還差 11.6 cm 的原因（replica 重現 runtime）：**上升速率 ω6**（cap 定義與 primary 支撐腿都不是主因）。ω8／10／12 ⇒ 9.3／7.7／6.5 cm，單帧 1.3／1.6／1.9 cm。
> - **其他停步**：全部更早站；但 **Walk 上樓 rise→drop 10.1 cm、Run 上樓 4.3 cm**（尾腳鎖定時最終站姿要求更深——上樓沒有未來落點資訊，LiveLanding fallback 來得太晚）；平地／轉身 ≤ 2.1 cm。
> - 測試：EditMode **761／760／0 failed／1 skipped**（+7）；完整 PlayMode **174／165／1 failed（既有 C0）／8 skipped**。
> - ❓ 待使用者：①SupportTransfer ω（6 或 8–10）②上樓 fallback：擴批准到 Walk 上樓（需先驗證向上翻階）／非批准停步尾腳鎖定前不回升／接受。
>
> ---

> # 🟨 2026-09-29（五）— **落點預測參考點轉換（Foot 骨骼 → IK goal）：水平誤差 5.7 → 1.1 cm；翻階仍 3／26（實為 1 個「腳跟 2 mm 站在階緣」站姿）；⛔ 無消費者、未 commit**
>
> 📖 正本：`docs/46` §18。使用者裁決方向 2：bake 不動，prediction 端轉換；左右腳各一組 foot-local 偏移。
>
> - 程式：`FootIKSettings.StopLanding{Left,Right}GoalOffset`（預設 0；X Bot 以 Editor API 設為 §17 實測值）＋ `FootIKController.TryPredictStopLandingGoal`／純函數 `StopLandingGoalInRoot`。
> - ⚠️ 限制：烘焙沒有腳的旋轉 ⇒ 落地腳座標系以預測 root 朝向近似；bone→goal 偏移不是剛體常數（步態中 std 0.6–0.8 cm），取 clip 結束站姿值。
> - 驗收（同 26 次、轉換前後同一次執行）：前後 +3.9 → **+1.1** cm、左右 4.1 → **0.2** cm；正確階 23 → **23**／26。
> - 翻階定位（Editor 射線 1 mm 掃描）：實際腳跟在階緣前 **1.9 mm**、預測腳跟過階緣 9 mm ⇒ 剩下的翻階正是 ~1 cm 鎖定時機殘差，且真值只有 2 mm 餘裕。預測偏低 ⇒ 當 cap 是保守方向。
> - 測試：EditMode **754／753／0 failed／1 skipped**（+3）。
> - ❓ 待使用者：(a) 接受邊界翻階（保守）(b) 修 ~1 cm 鎖定時機殘差（只能縮小不能消除）(c) cap 在誤差帶跨階緣時取較低階。
>
> ---

> # 🟦 2026-09-29（四）— **+3.8 cm 落點偏差定位完成：烘焙記「腳骨」、T3 鎖「Humanoid IK goal」；⛔ 只量測**
>
> 📖 正本：`docs/46` §17。同一停步事件逐點比（root 空間）：烘焙腳踝 − FK 腳骨 ＝ **0.00**（烘焙正確）；FK 腳骨 − IK goal ＝ 前後 +2.3／+3.3、左右 +4.9／−3.9 cm（左右腳不同 ⇒ 解釋兩個偏差值）；IK goal − T3 鎖定點 ≈ 1 cm（鎖定後 goal 微動）。
> - ❓ 待使用者：修 bake（記 IK goal）或改 prediction reference（腳骨 → goal 的腳局部偏移）。
> - throwaway：`StopLandingProbe.Probe_FootPointChain`（未追蹤）。
>
> ---

> # 🟦 2026-09-29（三）— **停步落點預測驗證：root 幾乎完美，腳踝有 +3.8 cm 固定偏差 ⇒ 26 次 Walk 下樓停步 3 次翻階；⛔ runtime 未改**
>
> 📖 正本：`docs/46` §16。使用者：Planned Stop 只當 FutureLandingProvider（只回答尾腳落在哪），先驗證 Walk 下樓能不能公分級、不翻階。
>
> - 新 throwaway `StopLandingProbe`：13 個放開位置 × 2 樓梯 ＝ 26 次獨立停步（`StairFeelProbe` 是決定性的，8 次＝2 個樣本）。預測以反射呼叫既有 `TryPredictGroundedAnkle`，只記錄。
> - 23／26 正確階（垂直 ≤ 1.4 cm）；3／26 低一整階（−19 cm）。水平 5.7 cm＝前後 +3.9 ± 0.5（穩定）＋ 左右 ±4（依腳別）。
> - 拆解：預測 root +0.1 ± 0.1 cm（幾乎完美）；前後誤差全在 ankle-in-root（+3.4／+4.1–4.3，依停步 clip 變體）⇒ 烘焙點與 T3 鎖定點的**定義不一致**，不是預測雜訊。翻階全由此偏差造成。
> - 其他：26／26 最終兩腳同一階；預測在放開後 ~0.20 s 才有，約一半比 primary 晚。
> - ❓ 建議先查清定義差並修烘焙點後重跑 sweep（需要動 bake，本輪禁止）；替代：現況直接當下樓 cap（翻階偏保守）或轉向 foot-side。
>
> ---

> # 🟦 2026-09-29（二）— **SupportTransfer 離線驗證：Run 與 Walk 上行／轉身／平地成立；Walk 下行停步需要已知尾腳落點；⛔ runtime 未改**
>
> 📖 正本：`docs/46` §15。使用者：Moving 先不動，只處理停步後站直時機——前腳成為 Primary Support 就開始站直，尾腳 committed landing 只當 Future Reach Cap。
>
> - 方法：v2 trace（有 Reliable 旗標）重播 StopHold 與 SupportTransfer；StopHold replica 對 v3 runtime 中位誤差 0.03 cm。
> - **Run**：開始站直 1.01 → **0.36 s**、尾腳落定時 deficit 13.9 → **4.0 cm**、final stand 1.27 → 1.05 s，沒有新的下拉。
> - **Walk 上行、轉身、平地**：成立（0 次 > 3 cm）。**Walk 下行停步 6／6 失敗**：尾腳最後一步落到低一階，runtime 落地前 ~0.15 s 才知道 ⇒ 鎖定時硬可達單帧拉回最多 12 cm。已知落點（oracle）時 6／6 乾淨、站直 0.37 s 開始。
> - 事件分類注意：probe 的 `Time.frameCount` 跨 run 不能對齊；rise→drop 只計開始回升之後的下降（否則會把煞車強迫下降算進去）。
> - ❓ 待使用者：SupportTransfer 做 runtime？Walk 下行選 (a) 接受落腳下拉 (b) 下行時仍 hold（缺可靠訊號）(c) 只拿 Planned Stop 的落點預測當 cap（先驗證準度）。
>
> ---

> # 🟨 2026-09-29 — **v3：停步／站立交給統一求解器（StopHold → Stand）；Walk 停步下蹲消除、Run 剩強迫下降；⏳ 待人工 Play；⛔ 未 commit**
>
> 📖 正本：`docs/46` §14。使用者：v2「下到平地沒問題」，下一步處理停步下蹲。
>
> - 診斷（v2 逐帧）：舊停步鏈互相打架——`Upright` 硬夾 bug（單帧 19 cm 後帶負速度、無下限 ⇒ **−82 cm**）、`C`（權重×可達）在 −45↔−7 跳、StopProgress 只看 primary 腳、Return 目標是仍很深的 Moving 參考；**Planned Stop WIP 在 runtime 是活的**。
> - 做法：`UseUnifiedStandingPelvis`（X Bot 開）⇒ 舊鏈整條旁路（WIP 程式碼未動）；停步只改回升語意：放開後到落定前 `StopHold`（不回升），兩腳 Locked ＋ root < 0.05 m/s 後 `Stand`（ω6）。目標與約束與移動中相同。
> - 驗收（真正的停步，放開後 1.5 s，A／v2／v3）：**Walk** 先升後降 9.9／9.5／**1.3 cm**，> 5 cm 15／18／**0**／24；**Run** 12.6／20.2／**8.5**（最大 24.6／75.3／18.6），剩下的全是煞車步落到低階的「強迫下降」；搆不到帧 60／72／**18**。
> - ⚠️ 事件分類修正：probe 在樓梯頂「放 W 下一帧按 S」會有 1 帧意圖 0——不是停步，已排除。
> - 測試：EditMode **751／750／0 failed／1 skipped**（+6）；完整 PlayMode **120／111／1 failed（既有 C0）／8 skipped**（+1 新測試，舊鏈測試改為明確關掉統一站立）。
> - ❓ Run 強迫下降的三條路：①預知最後落腳（Planned Stop 的烘焙落點改當「未來 Landing 約束」）②Heel Lift／T3 落點 ③接受。
> - ⏳ 人工 Play：docs/46 §14.7。還原＝取消勾選 `UseUnifiedStandingPelvis`（回到 v2）。
>
> ---

> # 🟨 2026-09-28（深夜）— **Moving v2：回升拆成 StepRecovery／TerrainExitRecovery；樓梯→平地深蹲已消除；⏳ 待人工 Play；⛔ 未 commit（使用者指示保留為原型）**
>
> 📖 正本：`docs/46` §13（§12＝v1）。使用者裁決：不再調同一個 `ω_up`；Terrain Envelope 只當「周圍已不需要低高度」的提示，⛔ 不得回到目標或硬約束；Heel Lift 與 Stop／Standing 統一各自下一輪。
>
> - `MovingPelvisRecovery { None, Step, TerrainExit }`：步態內約束解除 ⇒ Step（ω 1.5）；地形提示高於骨盆 >5 cm（<2 cm 離開，滯後）⇒ TerrainExit（ω 6）。提示只選 ω，目標仍是 `min(0, 約束)`（EditMode 釘住）。
> - **樓梯→平地**（離開後 0.5 s）：Walk −24.0 → **−5.3 cm**（A −2.4）、1.5 s 內沒恢復 4 → 0；Run −3.6 → −1.0。
> - **樓梯內不變**：bob、搆不到逐項相同；唯一變差 Walk 下樓髖 a95 19.3 → 22.7（仍 < A 26.4）。
> - **沒有新 snap**：進入樓梯與 v1 逐帧相同；離開後停步的最大單帧 v2 ≤ v1。走出開放頂端的 8.3 cm／帧是既有 `ikAllowed=false → 0` 後備路徑（v1 同，A 4.3）——不屬於本輪。
> - 測試：EditMode **745／744／0 failed／1 skipped**（+6）；stair probe 16／16；完整 PlayMode **118／109／1 failed（既有 C0）／8 skipped**。
> - ⏳ 人工 Play：docs/46 §13.6。未解：停步後瞬間下蹲（Stop／Standing 統一）、Walk 上樓收益小（Heel Lift）、開放頂端離地後備。
> - Git：⛔ 依使用者指示不 commit（程式仍與暫停的 Planned Stop WIP 同檔）。
>
> ---

> # 🟨 2026-09-28（晚）— **統一 Moving 骨盆 runtime 原型 v1：已實作＋量測；⏳ 待人工 Play；❓ 2 項待裁決**
>
> 📖 正本：`docs/46` §12。使用者裁決：只替換 Moving、`R-noHL`、`ω_up 1.5`、Terrain Envelope 退為後備／診斷；stop／Planned Stop（WIP 暫停）／heel-lift／Run 上樓 T3 釋放都不碰。
>
> - 新 `MovingPelvisSolver.cs`（純函數）＋ `FootIKController.ResolveUnifiedMoving` ＋ 5 個設定欄位（`UseUnifiedMovingPelvis` 預設 false）；X Bot prefab 以 Editor API 開啟。
> - ⚠️ stop 規則一行未改，但 `StandingPostureSolver` 收到的 `Envelope` 輸入**改成 Moving 參考**（否則放開按鍵會往更深的包絡掉）。
> - 驗收（runtime，A→UM）：深度 Walk 上 −32.2→−29.5、下 −30.7→−29.9、Run 上 −31.8→−27.3、下 −14.8→−11.7；Walk 下 bob21 7.0→2.8、髖 a95 26→19；
>   Run 上搆不到 15.0%→13.1%；**Walk 上髖 a95 15.5→29.9（變顛）**；樓梯前平地不變（−2.4→−2.8）；**剛下完樓梯的平地 −2.5→−11.3（約 2 s 恢復暫態）**。
> - 根因：`ω_up` 同時是「每步 bob 預算」與「約束消失後的恢復速度」；包絡看得到前方樓梯結束，反應式求解器看不到。高度增益小是因為沒有抬跟（離線早已指出）。
> - 實作中修正：硬夾時速度跟隨上限的變化率（離線 R0 會把夾掉的距離當速度 ⇒ 衝過約束後慢慢爬回）。
> - 測試：EditMode **739／738／0 failed／1 skipped**（新增 15 條）；stair PlayMode **26／26**（新增 2、修改 1：`Player_BodyReference_IsIndependentOfFacing` 改為同時關掉統一路徑——它本來就只測包絡）。完整 PlayMode **118／109／1 failed（既有 `C0`）／8 skipped（probe 設計）**。
> - ❓ 待使用者：①是否加「Terrain E 當恢復提示」（E 高於骨盆 5 cm 以上時以 ω 6 回升；離線：下完樓梯平地 −11.3→−5.9、樓梯指標不變）；②或改 `ω_up 3`（−7.2，但 Walk 上更顛）。
> - ⏳ 人工 Play：docs/46 §12.6 六項。還原＝取消勾選 X Bot 的 `UseUnifiedMovingPelvis`。
> - Git：程式改動與工作樹中暫停的 Planned Stop WIP **在同兩個檔案裡**（`FootIKController.cs`、`FootIKSettings.cs`，prefab 也寫入了 WIP 欄位）⇒ 未 commit，待使用者決定怎麼切。
>
> ---

> # 🟦 2026-09-28 — **Pelvis／Body Height 重整 Phase 1：盤點＋離線設計完成；⛔ runtime 未改；❓ 5 項待裁決**
>
> 📖 正本：`docs/46-pelvis-body-height.md`；圖解 `docs/artifacts/pelvis-body-height.html`（Artifact：https://claude.ai/artifact/5RxLFi8dXcCEWYgq8GhU6U）。使用者指示：停止修補現有 pelvis 邏輯，先重做「樓梯移動中身體高度」這一層，stop 之後再接回。
>
> - 盤點：唯一寫入者不變，但內部有 **6 個相位、4 種「該多高」定義**；Moving（包絡）**完全沒有可達檢查**——現況 Walk 上樓 3.8%、Run 上樓 15% 的支撐帧搆不到。
> - 約束語意（量測）：上樓綁住高度的是**仍在動畫支撐期的後腳**，下樓是**即將落地的前腳**；Releasing 會要求 −30～−72 cm，屬暫態、不該拉骨盆。
> - 字面的 nominal-first（B）與 support-driven＋trailing cap（C）：上樓每步起伏 20–25 cm、加速度 7 倍 ⇒ 不可行。
> - 統一模型 R（I＝0；Support＋Landing 約束；後腳前掌支點；ω_down 12／ω_up 3；硬可達夾）：四個情境都比包絡高 6–8 cm（Run 下樓除外），Walk 上樓搆不到 → 0%、Run 上樓 15% → 3.8%；代價 Walk 上樓髖加速度 p95 15.5 → 23.4（ω_up 1.5 ⇒ 18.7）。
> - 剩下的 Run 上樓 3.8% 連 oracle 也解不了 ⇒ T3 樓梯釋放時機（需求端）。抬跟是 Contact 層行為，骨盆不能自己假設。
> - ⚠️ 工作樹有**未記錄的 Predictive Planned Stop WIP**（`PosturePhase.PlannedStop`、`LocomotionStopPrediction`、`.codex-temp/REPORT-planned-stop.md`）——本會話未碰；列為裁決項 5。
> - ❓ 待使用者：①統一模型作為 Moving 基線？②`ω_up` 1.5 或 3？③開 Contact 抬跟 slice？④開 Run 上樓 T3 釋放 slice？⑤Planned Stop WIP 的去留？
>
> ---

> # 🟨 2026-09-27（深夜二）— **停步站直四段鏈：執行期原型已實作並量測；⏳ 待人工 Play**
>
> 📖 正本：`docs/05` §3.5.4.6「現行合約」「執行期驗證」。取代工作樹未記錄的 WT 實驗（非疊加）。
>
> - ①EarlyRecovery（新 `PosturePhase` 4，ω6 soft）→ ②Primary Support Rise（Hermite，承接畫面支撐腿伸展速度）→ ③尾腳前瞻上限（T3 Locked 才有；每次鎖定鎖存一次；單邊飽和、啟用帧不變）→ ④Final Settle（只 `min(骨盆,C)`）。
> - s1_Walk 下（跨 18 cm）：落定修正 **−13.62 → +0.02 cm**、單帧 13.6 → 2.1 cm、峰值 8.2 → 1.3 m/s。8 正式停步落定修正 ≤0.41 cm；WT 在 proto_Run 上樓其實沒站起（膝 77°）→ 16°。
> - 擺盪尾腳（無預測上限）：全部 Run 停步、proto_Walk 上／quick。取消前抬升 ≤2.3／7.7／10.4 cm（0.30／0.45／0.60 s）。
> - 實作中抓到：WT 殘留的 Return 以 C 硬夾，跑下樓 C 低於包絡 ⇒ 單帧下拉 12 cm。改 `≤max(C,E)`＋1 m/s 有限退讓。
> - 測試：EditMode **718／717／0 failed／1 skipped**（`StandingPostureSolverTests` 重寫 25 條）；stair probe 16／16（8 by design skipped）；完整 PlayMode **116／107／1 failed（既有 C0）／8 skipped**。
> - ⚠️ `X Bot.prefab` 必須與程式一起 commit（`FormerlySerializedAs`：HEAD 的 0.05 會被讀成 FinalSupportSpeedMax）；內有 5 個舊 quintic 版孤兒欄位，Unity 會忽略、未重存 prefab 清除。
> - ⏳ 人工 Play：兩道樓梯 Walk／Run 上下停、放開後 0.3–0.6 s 重按、第一支撐後立即重按；重點看 s1_Walk 下樓跨階站姿、Run 取消前抬升 7–10 cm 是否可接受、quick-stop 重按從直立降回跑步。
>
> ---

> # 🟨 2026-09-27（深夜）— **跨階站姿：尾腳前瞻可達上限離線驗證通過；early-cancel probe 已補；⛔ 姿勢 runtime 仍未改**
>
> 📖 正本：`docs/05` §3.5.4.6「尾腳前瞻可達上限」。使用者裁決：R8＝`bb9b6f2` 定案；烘焙器 (0,0) 不改；Early lift 暫用 soft ω6；ω10／重烘焙凍結到幾何問題解決。
>
> - **落點可靠判定**：尾腳 T3 `Locked` ＋ 本次鎖定中「第一次可靠」時鎖存（每次鎖定一次）。擺盪腳不給上限——即時目標會在觸發後再跳一整階（proto_Walk quick 18.9 cm）。
> - `s1_Walk/stop`：落定修正 −17.37 → +0.01 cm；尾腳全程搆得到；啟用 Δv −0.11 m/s；單帧最大 17.35 → 2.06 cm；bob 1 → 0。擺盪類 8 次與無 cap 逐帧相同。
> - 套用＝單邊 C1 飽和（帶寬啟用時鎖定 ≤3 cm），持續到落定之後；淘汰 hard min／對稱 smooth-min／落定放開／每帧重鎖存（均量過）。
> - **Early-cancel probe**：`StairFeelProbe` 參數 `earlyCancelDelay` 18／27／36（既有 baseline 檔名不變）。24 窗口：取消前抬升 ≤1.9／7.0／10.4 cm（0.30／0.45／0.60 s），重按後下降與搆不到與 B0 相同，cap 釋放無跳動。
> - ❓ 下一步待使用者：是否把「Early lift ω6 soft ＋ C1 支撐腿（承接速度）＋ trailing cap」落成 runtime（取代工作樹未記錄的 WT）；runtime 會直接用 `LockedSupportY` 取代離線鎖存近似。
> - Git：本 slice 只有文件（docs/05、WORKLOG 與前面未 commit 的段落混在一起）＋未追蹤的 probe——未 commit，待使用者決定怎麼切。
>
> ---

> # 🟩 2026-09-27（夜）— **R8 已修（Stop 入場 root 停滯）；Early Stop Recovery 離線量測完成；⛔ 姿勢 runtime 未改**
>
> 📖 正本：`docs/07` §13.1 R8；`docs/05` §3.5.4.6「Early Stop Recovery 離線量測」。
>
> - **R8 根因（逐帧紀錄確認）**：①Play 當帧讀到播放頭 0；②新 state 第一次評估不推進時間；③烘焙器第 0 個 speed key 固定 (0,0) ⇒ 第 3 帧半速。
>   **修法**：`LocomotionStopRuntime.HoldsEntryMotion`（Pending ∨ 播放頭仍 0）期間沿用 Pending 語意；`ExecuteLocomotionStopMovement` 取速度樣本略過佔位 key。減速曲線逐字不變。
>   修後 31／31 入口無停滯。EditMode 710／709／0 failed／1 skipped；新 PlayMode 測試 1／1；stair probe 8／8。
>   完整 PlayMode 100／98：`C0_…IncidentalContact`（既有）＋ `Climb2m_RealPrefab_…`（1.8／2.05 時過時不過）。**A/B 已排除 R8**：暫時換回 pre-R8 檔案重跑，同樣失敗／翻轉（該測試全程按住 W、不觸發 Stop）；推定與工作樹其他未提交改動有關，未追。
> - ⚠️ 烘焙器 (0,0) 佔位 key 本身**未修**（全域：`AutoAverageSpeed` 會變）——❓ 待使用者。
> - **Early lift**（w＝root 減速進度、疊在包絡上、C 以下封頂、soft 退讓）：腳搆不到 12／12 不變；交接 Δv ≤0.18；上樓支撐窗峰值 2.35→0.48–0.82 m/s。
>   風險：Run 下樓退讓 ≤1.67 cm／帧 1 次；0.3 s～第一支撐之間取消的停步未覆蓋（需新 probe 情境）。
> - **新發現**：R8 後 s1_Walk 下樓最終站姿跨階（高差 18 cm）⇒ 只看 primary 的支撐腿軌跡在尾腳落定時單帧下修 13.6–17 cm（WT runtime 亦同）。Primary Support Recovery 需處理。
> - 🧹 throwaway：`StopEntryLogger` 已刪；`StairFeelProbe` 仍是未追蹤的探針。工作樹仍含未記錄的 WT 姿勢實驗（見下一則）。
>
> ---

> # 🟧 2026-09-27（晚）— **速度承接比較：進入時沒有速度重設；真正槓桿是 D 預測過短；⚠️ 工作樹有未記錄的 WT 實驗；⛔ 只量測**
>
> 📖 正本：`docs/05` §3.5.4.6「速度承接比較」。
>
> - ⚠️ **工作樹 ≠ 上一則 handoff**：`StandingPostureSolver.cs`／`FootIKSettings.cs`／`StandingPostureSolverTests.cs`（03:14–03:15）已改成 scalar-speed MoveTowards
>   （3D 髖速度量值當垂直速率、拿掉 D／quintic；Upright 需伸展完成）。**沒有文件、未 commit、非本會話所寫**。實測進入單帧跳 0.43–1.81 m/s、2／8 到不了站直 ⇒ 建議淘汰。
> - `Temp/` 在 19:30 被 Unity 重啟清空（原 trace 與 Codex 的 throwaway 腳本都沒了）；已用 `StairFeelProbe` 重跑（8／8）重建，entry 帧逐帧一致。
> - 失敗 trace 進入首帧 Δv≈0；可見跳動是其後加速到 1.0–2.6 m/s。進入速度 −0.53～+1.05 m/s，只靠它任何一案都到不了站直。
> - C1（support-leg 腿長 cubic Hermite，承接顯示的伸展速度，無 monotonic hold）：連續、峰值比 OLD 低 9–23%、join ≤0.35 cm。
>   3／8 Walk 案 D=0.20 但尾腳 0.383 s 才落定——若 T 對準真實窗口，逐帧位移減半（≤1.9 cm）。
> - ❓ 待使用者：①C1／C2 取代 WT？②D 預測規則；③0.183 s 窗口可否在尾腳落定後稍晚完成。Runtime 本會話未改。
> - **往回追（放開按鍵→Idle）**：骨盆動量在 StopProgress **之前**就流失。第一個偏離帧＝**Locomotion Stop 入場 2 帧 root 停滯**（位移正好 0、第 3 帧半速；36／36 stop 入口都有，與 Foot IK 無關）→ `docs/07` R8 🔴。
>   之後是停步曲線 root 減速 × 樓梯坡度（authored）＋ Run 停步 clip 約 0.17 s 的煞車下沉（暫態）。包絡 E／IK offset 在第一支撐前 6／8 完全不動 ⇒ 不是 Foot IK。
>   第一支撐時 root 只剩 9–56%。❓ 待使用者：先修 R8？是否研究「offset 在 root 減速期就開始補償」的新姿勢政策？
>
> ---

> # 🟨 2026-09-27 — **StopProgress 已改為 primary support-leg 幾何 candidate；⏳ 待人工 Play feel**
>
> 📖 正本：`docs/05` §3.5.4.6。離線：`Temp/StairProbe/support_leg_candidate_sim.py`；執行期比較：`diagnose_stop_discontinuity.py`／`compare_support_leg_runtime.py`。
>
> - primary reliable edge 現在鎖定：support foot 世界位置、entry hip、Upper／Lower leg length、起始髖踝長／膝角、0.995 idle 目標長／膝角。`FootIKPoseData` 新增上下腿分段長（Rig 仍唯一 Writer）。
> - StopProgress 不再 `lerp(E,U,q)`；既有 q 改為推進 `Lstart→Lidle`，每帧以固定 foot＋live primary hip XZ 反解 pelvis offset。moving trailing foot 的 target／U／C 不得重定義 candidate；進度期間只用 primary full-reach hard limit。
> - trailing final reliable edge 不強迫 q=1／U：高度有效就保留，無效只做 two-foot C 最小修正（實測 4／8 為 0；其餘 0.16／0.48／0.68／0.72 cm）。Return／速度承接、T3、moving E、Footstep、Issue 3、root／膠囊都沒改。
> - 離線先淘汰兩案：完全鎖 entry hip（root 繼續移動造成錯誤 reach／膝角）與 knee-angle interpolation（峰值加速度 73.4）。採用 linear leg-length extension；不調 duration／quintic／spring／clamp。
> - 最終 PlayMode trace：8／8 stair probe；latched support XYZ drift 0、primary switch 0、primary reach ≤0.02 cm、trailing ≤0.32 cm、水平 slide 0。actual pelvis world-Y 在 primary 後 0.017–0.067 s 上升 >0.5 cm；支撐膝 64–106°→1.6–19.5°；沒有重複 >1 cm rise/drop。
> - 峰值：offset 2.59 m/s／54.4 m/s²；actual pelvis world-Y 2.65 m/s／52.6 m/s²。這次是因果修正，不保證數值全面下降；人工 Play 要看「一支腳連續伸直帶動身體」是否取代 teleport 感。
> - `s1_Run/upstop` U 跳變已獨立定位：frame 8163→8164，trailing L 仍 Unlocked／unreliable，但 target Y +12.49 cm，使 U +9.15 cm；新 candidate 不讀 U，已隔離，未順手改 T3。
> - 測試：focused solver 20／20；完整 EditMode 709 total／708 passed／0 failed／1 skipped；stair PlayMode 8／8。Git mutation 依 repo policy 未執行。
>
> ---

> # 🟨 2026-09-27 — **StopProgress 可見跳動已定位：target trajectory；⛔ 尚未改支撐腿模型**
>
> 📖 正本：`docs/05` §3.5.4.6「可見跳動診斷」。重現：`Temp/StairProbe/diagnose_stop_discontinuity.py`；trace 欄位在 `StandingPostureSolver.PostureState`／`StairFeelProbe`。
>
> - 依錄影先凍結 quintic／duration／spring；只加 trace，沒有改 pelvis 行為。8 個既有 stair probe 重跑 **8／8 通過**。
> - 8 次正式 stop 共 67 個 ≥1 cm 位移帧；每帧 `PelvisOffsetY == StopProgress candidate`（誤差 0），峰值 1.59–4.12 cm／帧。這些帧 hard clamp、spring、velocity reset、monotonic hold、primary switch 全是 0。
> - trailing final join 的 U／C 變化 ≤0.53 cm、輸出 ≤0.36 cm；不是主要 snap。U／C 中段可跳（U 14.91 cm、C 6.27 cm），只有 `s1_Run/upstop` 的 U +9.15 cm 明確貢獻一個 +1.68 cm candidate step；其餘主因是短 D 內直接逐帧寫 candidate。
> - primary T3 target XZ drift 0，Y drift 0.08–0.98 cm；primary 全程不換腳。solver→下一帧 OnAnimatorIK 是固定一帧延遲，套用誤差 ≤0.005 cm，沒有 old/new foot data 交錯。
> - **裁決**：不先調曲線或彈簧。下一 slice 才實作使用者指定的 primary support-leg extension：尾腳移動時不得重定義主 pelvis trajectory；尾腳落定只做兩腳 reach validation＋最小殘差修正。
>
> ---

> # 🟨 2026-09-26 — **StopProgress 已實作並量測通過；⏳ 待人工 Play feel**
>
> 📖 正本：`docs/05` §3.5.4.6；摘要 `docs/44` §9.19。分析：`Temp/StairProbe/stop_progress_runtime.py`（throwaway）。
>
> - root-only Settling 已**移除／取代**：新 reliable T3 plant edge ＋ root≤1.35 classifier 開始；0.18／0.20 near-settle／0.40 s quintic 把 E→U；尾腳新 reliable edge latch 100%；只有 C 可向下修正。Return 保留位置與速度；不讀 animation／FSM state 名稱。
> - 保持不變：moving Terrain Envelope、T3 semantics、FootstepDetector、Issue 3、root／膠囊、Y Bot。X Bot 以 Editor API 寫入 StopProgress 參數。
> - 8 個原 stair probe baseline **8／8**；每案在原流程之後另做 quick restart（約 0.10／0.25／0.50 s），不污染原 gait phase。
> - 實測：primary 後 0.033–0.067 s 可見上升；final 時 U deficit 0、膝 0–16.2°；支撐 reach 0、尾腳 ≤0.20 cm、locked target slide 0、重複下降 0；baseline join ≤0.36 cm（全部額外事件 ≤0.38 cm）。
> - pelvis 峰值 2.47 m/s／41.8 m/s²；quick input edge 單帧位置差 ≤1.19 cm，速度不重設。`StandingPostureSolverTests` 16／16；站姿 PlayMode 2／2；完整 EditMode **705／704／0 failed／1 skipped**。
> - ⏳ 人工 Play：兩道樓梯 Walk／Run 上下長停與 quick stop，重點看最短 11f 的站直是否太急、restart Return 是否自然。若太急只調 duration／quintic，不退回等雙腳。
> - Git：repo policy 禁止 Codex 執行 commit；工作樹已是單一 coherent slice，交由使用者 commit。
>
> ---

> # 🟧 2026-09-25（續二十二）— **「靜止」改為 root < 1.0 m/s × 6 帧：已套用（未 commit）；⛔ 驗證被 Unity Editor 主執行緒卡住中斷**
>
> - 已改（工作樹，**未 commit**）：`FootIKController.ResolveStandingPosture` 的 `still` ＝ 移動意圖 0 ∧ root < `StandingRootSpeedMax` ∧ 兩腳即時接觸（既有安全條件）；
>   拿掉兩隻動畫腳速度條件與 `StandingFootSpeedMax`；`ResolveContactPosition` 不再輸出動畫腳速度（只剩 `liveContact`）。
>   `StandingRootSpeedMax` 預設 1.0（tooltip 標明是原型調參值）；`X Bot.prefab` 0.05 → 1（Editor API；舊欄位已移除）。Unity 編譯成功。
>   雙腳約束、6 帧、ω6、各階段都沒動。
> - ⛔ 之後 EditMode／PlayMode／探針**都沒跑成**：Editor（PID 46304）主執行緒從約 21:05 起不執行任何 pipeline 命令（含 `cancel_tests`），
>   視窗原本最小化；已還原並前置，仍卡住（與 unity-live-control skill 記錄的「匯入未完成就排測試」鎖死同型）。需要使用者處理 Editor（取消卡住的測試或重開）。
> - 待辦（依序）：①EditMode＋PlayMode 全套；②重跑 8 個樓梯停步探針（`_t3p`），與平地基準及 `_t3pv1`（舊 Settling）比對：
>   開始恢復／接近待機時間、重複起伏 0、支撐腳搆得到 0、起步不更差（`Temp/StairProbe/idle_rhythm.py`、`posture_check.py`）；③commit；
>   ④連同 `4c27527` 送 Codex（2026-09-26 18:14 後）。
> - 另記（既有、非本次）：`Logs/Editor.log` 有 199,266 次 `CharacterPipelineRunner.LateUpdate` 第 525 行 `_runtimeData` NullReference。
>
> ---

> # 🟨 2026-09-25（續二十一）— **樓梯停步對照平地節奏：只要改「靜止」定義（root < 1.0 m/s × 6 帧）；⛔ 只量測**
>
> 📖 正本：`docs/05` §3.5.4.6「停步節奏對照平地」；摘要 `docs/44` §9.18。分析 `Temp/StairProbe/idle_rhythm.py`（用完即丟）。
>
> - 使用者簡化目標：跟平地停步節奏差不多即可，只去掉延遲的半蹲；不追求最早、不要求預測最後支撐腳。
> - 平地基準：0.20–0.28 s 開始站直、0.37–0.63 s 接近待機——**在最後兩步落地之前**（平地每步同高）。
> - 最簡單合格規則：狀態機、雙腳約束、6 帧、ω6 都不變，「靜止」改成「移動意圖 0 ∧ root < 1.0 m/s」，不要求兩腳靜止
>   ⇒ 開始 0.58–0.88 s（現行 1.00–1.33）、接近待機 1.02–1.48 s（現行 1.50–1.98）；搆不到 0、重複起伏 0、平順度同現行。
> - 放開按鍵就開始／root 瞬間變慢就開始：照每步踩的階起伏 10–13 次、4.4–4.8 m/s（＝已否決的逐腳骨盆）。StopRecovery 的「可靠支撐」多 2.8 cm 搆不到、1 次起伏。
> - 剩下 0.4–0.85 s 的差距：樓梯最後兩步踩在不同階，本資料中去不掉；彈簧加快會出現下沉。
> - ❓ **待使用者**：是否採用這一處改動（`still` 條件），再連同 `4c27527` 一起送 Codex。執行期未改。
>
> ---

> # 🟨 2026-09-25（續二十）— **停步語意研究：Settling 等太久（晚 0.35–0.7 s）；StopRecovery 可行但需要「最後支撐」訊號；⛔ 只量測**
>
> 📖 正本：`docs/05` §3.5.4.6「停步語意」小節；摘要 `docs/44` §9.17。分析 `Temp/StairProbe/stop_recovery.py`（用完即丟）。`4c27527` 未動；Codex 審查仍待補（且對象可能改變）。
>
> - 時間線（放開按鍵起）：意圖 +0.017 s；主要支撐腳最後落定 0.55–0.78 s（root 仍 0.41–1.24 m/s）；後腳最後落定 0.85–1.03 s；root 停 0.75–1.22 s；**現行原型開始升 0.98–1.33 s**。
> - StopRecovery（只有可靠支撐的腳約束、後腳落定才加入）＋ root ≤ 1.0 m/s：**早 0.35–0.7 s 開始升、早 0.5–0.9 s 站直**，膝蓋在停步中漸進伸直；
>   無新增搆不到／滑動／浮空；平順度與 Settling 相當；第二隻腳加入 ≤ 0.65 m/s。
> - **沒有 root 門檻會假起點**（停步動畫還會走 1–2 步）：上升後又下沉、s1 Walk 下樓 2.2 m/s 跳動。最後支撐落定時 root ≤ 1.24 m/s，非最後的 ≥ 1.37 m/s——可分但餘裕窄。
> - 後腳依 β 漸進加入沒有差別（既有支撐加權硬性上限已經在做）。未量：停步後 < 1 s 又起步。
> - ❓ **待使用者**：是否把狀態機改成 StopRecovery（含 root 速度門檻這個新參數），再一起送 Codex；或先加「短停步」探針情境。
>
> ---

> # 🟧 2026-09-25（續十九）— **站直原型已實作（X Bot 開啟）；⚠️ Codex 實作審查未完成（額度用盡）、人工 Play 未做**
>
> 📖 正本：`docs/05` §3.5.4.6（「執行期原型」小節）；摘要 `docs/44` §9.16。
>
> - 程式：`StandingPostureSolver.cs`（新）、`FootIKController.ResolveStandingPosture`（T3 解完兩腳後才算骨盆）、`FootIKSettings`（`UseStandingPosturePrototype` 預設關）、
>   `FootIKRig`／`FootIKPoseData`（髖位＋腿長事實）。關閉時逐位元不變。
> - **髖位來源實測**：`OnAnimatorIK` 開頭讀 UpperLeg 骨骼 ＝ 本帧 pre-IK 髖位（≤ 0.01 cm）；bodyPosition＋常數偏移差 2–9 cm ⇒ Rig 直接讀骨骼。
> - 探針：放開按鍵 1.65–2.17 s 後站直，骨盆 −5～−22 cm（包絡 −33.5／−35）、膝 22–24°；站立時腳不滑、不浮、搆得到；回到包絡 0.28–0.52 s。
>   **起步後 0.5 s 骨盆降 13–29 cm，峰值 0.76–2.92 m/s**（proto Walk 往下起步最大，畫面髖部加速度 17 → 106 m/s²）。
> - 測試：EditMode 702／701／1 既有略過；PlayMode 99／98（既有 C0）。`Player_BodyReference_IsIndependentOfFacing` 改為明確只測包絡；新增站直 PlayMode 測試。
> - ⏳ **下一步**：①Codex 實作審查（2026-09-26 18:14 後；審查提示已備妥：規格 §3.5.4.6 ＋ 本次 diff）；②審查通過後最終探針量測；
>   ③**人工 Play**：兩道樓梯、Walk／Run、上下中段停 ≥ 2 s、往上／往下起步、站著原地轉向——**重點看「站直後往下起步」像不像突然蹲下**。
>   還原：取消 `X Bot.prefab` → FootIKController → `UseStandingPosturePrototype`。
>
> ---

> # 🟨 2026-09-25（續十八）— **停在樓梯上時站直：狀態提案＋離線重播＋Codex 審過；❓ 待核准原型，未改行為**
>
> 📖 正本：`docs/05` §3.5.4.6；摘要 `docs/44` §9.15。分析 `Temp/StairProbe/stop_sim.py`（用完即丟）。探針加了樓梯中段 2.5 s 停步（上、下各一次；未 commit）。
>
> - 狀態：Moving（包絡，不變）→ Settling（運動學＋移動意圖＝0，連續 6 帧）→ Upright → Return（ω15 回包絡）→ Moving。
>   目標 ＝ min(max(包絡, 偏好上限 0.995), 硬性上限 1.0)——永遠不低於包絡 ⇒ 平地不變。
> - 8 次樓梯停步：放開按鍵 1.65–2.17 s 後站直（停步動畫佔 0.95–1.3 s）；高 13–29 cm；膝 16–19°（平地待機 17°）；站立期間腳不滑、不浮、搆得到。
> - 回到移動 0.20–0.42 s；下樓起步第一步 4–6 帧就落到下一階 ⇒ 峰值 2.1–2.6 m/s 的下降（包絡本身在樓梯入口也到 5 m/s）。移動意圖只早 1 帧。
> - 鎖腳重新落定只在 1／8 次有用（+18.5 cm，但要滑 9 cm）⇒ v1 關閉。
> - Codex：方向成立；原型前必修——先解 T3 腳再算骨盆、拆偏好／硬性上限、明確的 Return 與重設、彈簧細節；髖位由 Rig 寫進 `FootIKPoseData`；不需要 ADR。
> - ❓ **待使用者**：是否做 X Bot 限定、開關控制的執行期原型。
>
> ---

> # 🟨 2026-09-25（續十七）— **降低可達性需求：釋放時機不是槓桿、腳端可達性只是上限；F1（凍結偏移的釋放）已實作**
>
> 📖 正本：`docs/45` §9；摘要 `docs/44` §9.14。分析腳本 `Temp/StairProbe/reach_req.py`（用完即丟）。
>
> - 裁決 (a)：骨盆維持包絡，不改。需求量測一律**不夾限**（0.35 會截掉最壞情況：`Stairs (1)` Run 上樓實際 −53 cm）。
> - **釋放時機**：所有釋放都由 β 觸發；動畫自己的後腳已在髖後 0.32–0.37 m。任何釋放規則、甚至拿掉鎖腳，需求 p05 只動 0–3.5 cm ⇒ 不是槓桿。
> - **腳端可達性**（腳跟先離地／腳尖先著地 ≤30°）：最壞需求淺 5–8 cm、Walk 中位數淺 5–7 cm，每步變化（16–38 cm）不變；屬上限估計，且包絡下幾乎不啟動 ⇒ 暫緩。
> - **F1 已實作**（T3 內部）：釋放目標 ＝ 當下動畫腳 ＋ 衰減的凍結偏移。落後動畫腳減半；Walk 釋放腿搆不到 24% → 10%；Run 不變（原因是擺盪參考 vs root 離面，F1b，Codex：暫不實作）。
>   代價：`Stairs (1)` Walk 擺盪穿透 3% → 5%、Run 水平加速度 +3～14%。
> - Codex：方向成立；要求改用不夾限數字（已改）；只做 F1，F1b／F2 暫緩。
> - 測試：EditMode 689／688／1 既有略過；PlayMode 97／96（既有 C0）。
> - ❓ **待使用者**：包絡骨盆下，需求端兩個槓桿都不會讓半蹲明顯變淺；要站直需「骨盆折衷 ＋ 需求降低」一起、或樓梯 clip（磁碟上沒有）。
>   人工 Play：看釋放時後腿是否不再被拉直（Walk 最明顯）。
>
> ---

> # 🟨 2026-09-25（續十六）— **樓梯半蹲＝骨盆問題：可達性骨盆離線模擬完成、Codex 審過；❓ 待裁決，未改行為**
>
> 📖 正本：`docs/05` §3.5.4.5；摘要 `docs/44` §9.13。模擬器 `Temp/StairProbe/pelvis_sim.py`（用完即丟，不進版控）。
>
> - 使用者 Play：T3 後跳動大致消失，但樓梯上持續半蹲。裁決：T3 維持現狀、不擴充落點鎖定；半蹲當骨盆問題處理。
> - **結論**：包絡的深度 ≈ 每一步最壞時刻的可達性需求（需求每步變化約 20 cm）。使用者的候選「名目高度＋最小修正」照字面做
>   ≈ 已否決的逐腳骨盆（每步起伏 16–20 cm），而且支撐腿被拉直到 Leg Stretch（膝 4–9°，平地 25°／42°）。向上回復在樓梯上無效。
>   接觸安全的平滑折衷（J＋彈簧）只換回約 7 cm（Walk 膝 85° → 68°），髖加速度約 7 倍。
> - **真正的槓桿在需求端**：T3 釋放時機（後腳鎖在髖後 0.55–0.7 m）、腳端可達性（腳跟先離地／腳尖先著地）、樓梯用 locomotion clip。
> - Codex：方向成立；指出「釋放中的腳沒被排除」「J ≠ 字面候選」「水平搆不到時跳到 −0.35」三點，已修正重跑，結論不變。
> - ❓ **待使用者**：(a) 維持包絡、先處理 T3 釋放時機（建議）；(b) J＋彈簧折衷做成開關給人工判斷；(c) 字面候選（不建議）。
> - 沒有改任何程式、資產或測試。T3 仍開啟。root／膠囊、FootstepDetector、議題 3、Y Bot 都沒動。
>
> ---

> # 🟩 2026-09-25（續十五）— **T3 腳部接觸原型：X Bot 已開啟，量測通過、Codex 認可方向；⏳ 待人工 Play**
>
> 📖 正本：`docs/45` §8；摘要 `docs/44` §9.12、`docs/05` §3.5.4.4。程式：`FootContactSolver.cs`（新）、`FootIKController.ResolveContactPosition`、`FootIKSettings`（T3 區塊）。
>
> - **做了什麼**：位置權重 1、目標＝擺盪相對移動面 ＋ 帶朝向的有號最小抬升 ＋ 水平鎖腳（含鎖定支撐）＋ 一帧外插；旋轉、骨盆、腳步偵測器、Y Bot 都沒動。關閉時逐位元不變。
> - **量測**（探針，兩道樓梯 × Walk／Run，含轉身與兩次停步；base 先擷取）：擺盪穿透 > 3 cm 27–48% → 1–5%（下樓 Walk 97% → 8–13%）；
>   垂直加速度 p99 四情境都降；腳步 擊發 − 畫面觸地 Walk +8～+12 → −1 帧、Run → 0～+2 帧，擊發次數不變。
> - **執行期才抓到的**：IK goal 與 FK 骨骼差 6 cm（T3 畫 goal 軌跡）；參考射線兩個 bug（目標跳 25–55 cm）；鎖定中換階（跳 18 cm）。都已修。
> - **Codex 三輪**：Play 前 2 Medium、Play 後 1 High ＋ 1 Medium，全修並補測試；**方向成立**，下一步是落點感知的鎖定。
> - **未解**：停步時水平抖動（外插過衝）、進鎖前跨階緣偶發一帧跳動、腿過伸帧增加。
> - 測試：EditMode 688／687／1 既有略過；PlayMode 97／96（既有 C0）。`CharacterPipelineRunner.cs` 的 `M` 只是時間戳，內容與 HEAD 相同（blob 一致）。
> - ⏳ **人工 Play 清單**（X Bot，兩道樓梯，Walk／Run 上下＋轉身＋停步）：①擺盪腳的姿態自然嗎（畫的是 IK goal 軌跡）；②踩住的腳黏得住嗎；
>   ③抬腳釋放自然嗎；④樓梯上有沒有偶發的一帧跳腳；⑤停步時腳有沒有橫向抖；⑥腿有沒有伸直過頭；⑦腳步聲對得上嗎。
>   A/B：取消 `X Bot.prefab` → FootIKController → `UseContactPrototype` 即回到原行為。
> - ❓ **待使用者**：人工 Play 後，是否把 T3 往前推（落點感知的鎖定／停步外插）。探針 `StairFeelProbe.cs` 仍未 commit（全部修完後刪）。
>
> ---

> # 🟨 2026-09-25（續十四）— **外部架構校準完成：T 系列方向成立，落腳端要改；Codex 同意做 T3 執行期原型（先修 2 High）**
>
> 📖 正本：`docs/45`（新）；摘要 `docs/44` §9.11。⛔ 只研究／離線模擬；沒改程式、資產或測試。commit `05d5921`、`482893c`。
>
> - **結論 2**（UE Foot Placement／AC Syndicate GDC 2016／Johansen 2009 ch.7／Clavet 2016）：擺盪看連續參考面、權重＝1、保留動畫離地高度——與外部一致。
>   **不符合的是落腳端**：外部是「每步一個落點＋鎖腳」，T1 是逐帧重取當下踏面。
> - 模擬：落腳高度要看腳掌範圍（支撐誤差 17–19 → 約 3 cm）；在 β 門檻閂住落點會鎖錯階；鎖腳只能在執行期驗證。
> - **Codex**：T3（腳掌支撐＋鎖腳狀態機）可做執行期原型、不需要 ADR。**原型前必做**：①帶朝向的逐點最小抬升（取代最高踏面）；
>   ②位置／旋轉權重分開；③鎖腳狀態機＋失效路徑；④探針改用 `GroundLayers`；⑤先量腳跟接觸時序基準。
> - ❓ **待使用者**：是否開始 T3 執行期原型（`docs/45` §7 的五項前置 ＋ 驗收指標）。骨盆深度、議題 3、Motion 層 root 離面距離仍獨立未動。
>
> ---

> # 🟥 2026-09-25（續十三）— **IK 貼地時機研究：只調權重在樓梯上無解；發現擺盪腳穿透退化**
>
> 📖 正本：`docs/05` §3.5.4.4、`docs/44` §9.10。⛔ 只離線模擬；沒改程式或資產。
>
> - 使用者要求的「權重＝高度 × 落腳相位」（M1）：平地滑行中貼地 27.9 → 2.3 cm／步，**但樓梯擺盪腳穿透更深**（上樓 p95 10 → 16–36 cm）。
> - 🔴 **退化**：參考面包絡骨盆讓樓梯下樓的擺盪腳插進台階（>3 cm 的帧 5–33% → 44–76%）。先前只驗了支撐腳。
> - 候選 T1：β（高度 × 相位）決定腳相對**移動面還是踏面**，權重＝1 ⇒ 穿透 ≈ 0、樓梯觸地→擊發 +2／0 帧；目標每帧跳變 p99 9–15 cm 待處理。
> - 量測修正：`GroundY` 含腳跟／腳趾抬升，不能當地面；平地 Walk 現行觸地→擊發實為 −2 帧（非 +4）。
> - Motion 層 root 離面距離另立 chip；議題 3 未動。
> - ❓ **待使用者**：接受改 IK **目標**語意（T 系列）而不只是權重？還是先回頭處理擺盪腳穿透（例如把參考面包絡暫時關閉）？
>
> ---

> # 🟨 2026-09-24（續十二）— **半徑掃描＋腳步時序細化：都只量測，結論指向上游**
>
> 📖 正本：`docs/44` §9.9。⛔ 沒改程式或資產；探針 `StairFeelProbe.cs`（未 commit）加了半徑參數與 root XZ。
>
> - 使用者裁決：身體只跟隨地形包絡，不加支撐貢獻。
> - **半徑**：不影響平順度（樓梯上 dv95 全部相同）；換到的直立度很有限——`Stairs (1)` 與 Run 上樓已碰 0.35 上限。
>   半蹲主因是 L·tanθ ＋ **root 離移動面 8–10 cm（Run 上樓 18–23 cm，膠囊在斜面上的幾何）**。建議維持 0.45。
> - **腳步時序**：扣掉議題 3 偏移後，所有情境都是聲音在後（Walk 平地 67／樓梯 133 ms；Run 33／83 ms）；
>   「提早 83 ms」是量測假象。偵測器已在動畫最早的穩定落腳點；剩下的差距來自 IK 在動畫腳還以 6 m/s 滑行時就把它貼地。
>   水平上限 3.0 只多 17 ms、犧牲安全邊際 ⇒ 不改。
> - ❓ 待使用者：是否處理 Foot IK 的貼地時機（腳步對齊的真正槓桿），或 root 離面距離（半蹲的真正槓桿）。
>
> ---

> # 🟨 2026-09-24（續十一）— **三議題分離：腳步聲已修、骨盆基底已修；❓ 3 項待裁決**
>
> 📖 正本：`docs/44` §9.8（總表）、`docs/35` §6（腳步聲）、`docs/05` §3.5.4.3（骨盆）。
>
> - **議題 2 腳步聲**（commit `b39cd7a`）：移動中改為「上膛後第一個符合資格的接觸帧」，幾乎不動時保留下軌（Codex High）。
>   Walk 平地 208–300 → 50 ms、上樓 150 → 83 ms。
> - **議題 1 骨盆**：身體跟隨 8 方向世界固定取樣的移動面包絡（Codex 兩輪；facing 取樣、硬閘門、碰撞矩陣壓踏面三個 High 都已換掉）。
>   Walk 樓梯髖骨 dv95 4.8–7.3 → **0.22–0.23**（平地 0.28）；轉身不變。X Bot 啟用，Y Bot 未量測維持舊規則。
> - **議題 3**（腳陷入／浮起一帧 root 位移）：獨立待辦，未動。
> - ❓ **待使用者**：①支撐貢獻是否進入身體高度（Codex 的「真實腿長可達性殘差」版本，需要 pre-IK 髖關節估計）；
>   ②樓梯上的下沉深度（骨盆碰到 0.35 上限、膝蓋明顯彎曲——需要看）；③`Stairs (1)` Walk 上樓腳步聲提早 83 ms 是否聽得出來。
> - EditMode 665／664／1 既有略過；PlayMode 93／92（既有 C0）。
>
> ---

> # 🟨 2026-09-24（續十）— **量測：Walk 階梯感＝純骨盆呈現；腳步延遲＝偵測時機（Walk 150–300 ms）**
>
> 📖 正本：`docs/44` §9.7。⛔ 只量測，沒改任何行為。
>
> - Walk 三訊號分離：root 在樓梯上是完美直線（殘差 0.000），動畫髖部起伏與平地相同（0.08），
>   髖骨的階梯感幾乎全是 `PelvisOffsetY`（殘差 0.31–0.34 m）⇒ **確認是呈現層**，新骨盆模型可以開始討論。
> - 腳步聲：畫面觸地 → 擊發，Walk 中位數 9 帧（150 ms），平地 12–18 帧（200–300 ms），下樓 2–4 帧；Run 0–3 帧。
>   音檔起音 ≈1 ms（場景全是 Stone），音訊輸出估 21–43 ms。主因：IK 在動畫腳低於 8 cm 時就把腳貼地（眼睛看到的觸地），
>   偵測器等動畫的腳完全放平停住才擊發（Walk 的放平段約 16 帧）。
> - 另外發現：樓梯上畫面的腳偏移一帧的 root 垂直位移（下樓陷 2–6 cm、上樓浮 1–6 cm）——IK 在 root 移動前解算。未修。
>
> ---

> # 🟨 2026-09-24（續九）— **腳步聲已修；骨盆部分改善，剩「髖部階梯狀下沉」待裁決**
>
> 📖 正本：`docs/44` §9.6；細節 `docs/05` §3.5.4.2（骨盆）、`docs/35` 狀態欄（腳步聲）。
>
> - 使用者裁決：骨盆＝支撐貢獻（非 HasGround 閘門）；腳步聲＝動畫節奏為權威、踏面只給材質。Codex 審兩輪（1 High ＋ 4 Medium 全採納）。
> - 共同的 High：兩個元件拿**移動後的 root** 減**移動前拍的腳位**（Run 斜面上每帧差 5–8 cm）⇒ 新增 `FootIKPoseData.PosePlaneY`。
> - 腳步聲：下樓擊發率回到平地水準（0.8–1.7 → 1.7–2.7／60 帧），轉身 0 → 2.0（3/4 情境）。
> - 骨盆：上限命中歸零、多數情境抖動下降 13–43%；但髖部在世界空間呈**階梯狀**（單支撐時停住、換腳時 3–4 帧內掉一級），
>   Run 上樓因為骨盆不再鎖 0 也出現。這是支撐腿長度不變的幾何結果 ⇒ 要平滑需要新骨盆模型（彎膝），**待裁決**。
> - EditMode 652／651／1 既有略過；PlayMode 91／90（唯一失敗是既有 C0）。探針 `StairFeelProbe.cs` 未 commit，留著比對。
>
> ---

> # 🟨 2026-09-24（續八）— **人工 Play：跳動與下樓腳步聲——已量測，待裁決方向**
>
> 📖 正本：`docs/44` §9.5。⛔ 只量測，沒改任何行為。
>
> - 使用者回報：「跳動明顯」「轉身下樓前兩步沒腳步聲」。
> - 探針 `StairFeelProbe.cs`（未 commit，用完即丟）：跳動在**骨盆補償**這一層（樓梯上在 0 與 −0.35 m 之間鋸齒，
>   髖骨速度變化 p95 是平地的 10–20 倍）；root 與動畫本身正常。腳步聲不只轉身，**整段下樓**大多被
>   `Descending`／`GroundDiscontinuity` 吃掉。
> - 共同根因：Foot IK 骨盆與 `FootstepDetector` 都假設「root 平面 ≈ 支撐面」，移動面刻意打破了它。
> - 停止條件：修法會改 Foot IK／腳步偵測政策 ⇒ 等使用者裁決。
>
> ---

> # 🟩 2026-09-24（續七）— **樓梯：Run Down 修好，PlayMode 契約 20/20；待人工手感驗收**
>
> 📖 正本：`docs/44` §9.4。
>
> **已完成**：
> - **Run Down 失地根因**：grounded 貼地力只有 2 m/s，Run 4.7 m/s 下 30–35° 需要 2.7–3.3 m/s ⇒ 每幀彈離斜面。
>   不是移動面的錯。
> - **修法**：啟用 `docs/42` 的 Query→Apply 貼地，但 **Query 只看 `CharacterMovementSurface`**（一般地形不變）。
>   `enableStepDownAssistPrototype` → `enableStepDownAssist`（`FormerlySerializedAs`），X Bot／Y Bot 開啟、
>   `stepDownQueryLayers`＝8；log 拆到 `logStepDownAssist`（預設關）；程式預設遮罩改 Nothing。
> - **停步路徑**（Codex round 3）：新增 `MotionDriver.ExecuteLocomotionStopMovement`，`LocomotionModel` 停步分支改用它；
>   Roll／Action 不變。gate ② 改為「本幀要求的水平速度 > 0.01 m/s」。
> - X Bot `enableStepUpDiagnosticsPrototype` 關回 0（量測結束）。
> - **PlayMode** `StairTraversalPlayModeTests`：20/20（兩道樓梯 × Player Walk／Run 上下、Run Down 中途停步、
>   跳躍、開放邊緣墜落、Enemy 上下、查詢語意）。Run Down 失地 119→0／105→0。
> - **全套**：EditMode 651／650 通過／1 既有略過；PlayMode 87／86 通過，唯一失敗是**既有的**
>   `C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact`（2026-09-23 `groundEdgeEventGrace` 造成）。
> - Codex round 2–4 見 `docs/44` §9.4。
>
> **仍未解（刻意不在本輪範圍）**：Run Up 根部離移動面 0.18–0.23 m／骨盆下潛；Locomotion Stop 進入時 2 幀水平停頓
> （平地同樣發生）；Walk 腳步聲同腳連擊；場景中另一道 ProBuilder `Stairs`（x≈7.9）未套用。
>
> **⚠️ 待使用者**：`SampleScene.unity` 仍未 commit（混有使用者自己的關卡編輯，見續六）；
> 貼地目前在 prefab 上**預設開啟**——若手感不對，關掉 `enableStepDownAssist` 即回到原行為（Run Down 失地會回來）。
>
> **下一步**：人工 Play 手感驗收（`docs/44` §9.3）。
>
> ---

> # 🟩 2026-09-24（續六）— **樓梯 authoring 契約：已套用、EditMode 全綠，Play 驗收進行中**
>
> 📖 正本：`docs/44`（新規格，非 ADR）。使用者授權自主推進，Codex 為第二審查者。
>
> **已完成**：
> - 工具：`Assets/Scripts/Editor/Tools/StairProfileAnalyzer.cs`（純函數）、`StairMovementSurfaceGenerator.cs`
>   （交易式產生／驗證／選單 `Tools/Project/Stairs/`）。
> - 資產：layer 3／8 改名 `CharacterMovementSurface`／`StairTread`；碰撞矩陣進 `DynamicsManager`；
>   5 個 prefab 的遮罩依 `docs/44` §5；刪除 `RampPrototypeLayerSetup`；`MotionDriver` 膠囊偏移夾持改用
>   碰撞矩陣推導的遮罩（Codex 抓到的遺漏）。
> - 場景：刪手擺 ramp；prototype 樓梯**原本懸空 0.302 m**，依量測下移貼地；兩道樓梯由同一工具產生移動面，
>   驗證器全過（殘差 1.3／1.4 mm、偏離 0）。
> - EditMode：649 / 648 通過 / 0 失敗 / 1 既有略過；契約測試 25 個全過。
> - Codex 審查：7 Medium 全修、1 Low 入文件（`docs/44` §9.2）。
>
> **⚠️ `SampleScene.unity` 未 commit**：檔案裡同時有本工作的移動面／樓梯下移，以及**使用者先前未 commit 的
> 關卡編輯**（`Stairs (1)`、`Cube (4)` 等都不在 HEAD）。Unity 場景無法安全拆分 ⇒ 由使用者決定怎麼 commit。
> 在那之前，乾淨 checkout 的場景契約測試會失敗（場景沒有移動面）。
>
> **下一步**：Play 驗收（`docs/44` §9.3）。
>
> ---

> # 🟤 2026-09-24（續五）— **Generalization：`Stairs (1)` 證明樓梯支援尚未完成**
>
> 📖 正本：`docs/43` §10。⛔ 研究＋提案，未實作、未改資產（全部是 live Editor 唯讀查詢）。
>
> 使用者糾正：換一道樓梯就變差 ⇒ 只證明了**單一道** prototype，不是樓梯支援完成。
>
> - **必要差異**：`Stairs (1)` 是 ProBuilder 場景物件（非 prefab、非等比縮放），只有一顆 Default
>   layer 的階梯 collider——**prototype 的任何一項改動都沒有**，所以它的表現就是修前基準
>   （Walk Down `isGrounded=false` 26–29% vs ramp 0%；骨盆頂到 0.35 上限）。幾何也不同：級高相同
>   0.194，但踏面 0.275 vs 0.343，坡度 35.2° vs 29.7°。
> - **prototype 本身不是規格**：ramp 手擺，比自己的 nosing line 陡 1.7°，與踏面高差 −0.375～+0.065 m；
>   照抄到 `Stairs (1)` 會錯 0.49 m。且 Run Down／Run Up 在 prototype 上也沒過。
> - 🔴 **稽核發現 prototype 造成的新問題**：把階梯移到 `TreadIKOnly` 後，相機（mask=1）、
>   瞄準（129）、traversal（19）、Ice 地面技能（51）都**看不見這道樓梯**；敵人 Y Bot 同時碰踏面與
>   ramp（prototype 對敵人無效），且敵人 Foot IK 會踩在看不見的 ramp 上。
> - **提案**：兩個語意 layer（`StairTread`＝視覺／接觸、`CharacterMovementSurface`＝移動），全域規則
>   G1–G3（碰撞矩陣進專案設定、遮罩規則、prefab 內 IK 遮罩）＋每道樓梯不變量 I1–I5；Editor 產生器
>   （通過 Gate A／B）＋EditMode 驗證測試。驗收改為「至少兩道幾何不同的樓梯通過 Play」。
> - **待裁決 D1–D4**（`docs/43` §10.8）：ramp 平面規則、layer 名稱與專案層改動、spec 還是 ADR、實作順序。
>
> ---

> # ⚫ 2026-09-24（續四）— **`CollisionFlags` REJECTED：與 `isGrounded` 逐位元相同**
>
> 📖 正本：`docs/43` §9.6–§9.7。只讀不改（僅保留先前加的量測 log）。
>
> 第三輪 1988 帧 `[StepUpDiag]`：`collisionBelow` 與 `postGrounded`（`characterController.isGrounded`）
> **1988/1988 完全一致**；先前看到的「分歧」是 log 的 `isGrounded` 欄位（`data.IsGrounded`）晚一帧
> 造成的假象（與前一帧 `collisionBelow` 1976/1976 一致）。⇒「`IsGrounded=false` 但
> `Below=true`」這個母體**結構上不存在**，不需要 ramp 資料就能否決。§9.3 的 25% 數字已標註作廢。
>
> **四個候選全部否決**（`StateType.Jump`／`JustLanded` 重組／`IsGroundedConfirmed`／
> `CollisionFlags`）。`MotionDriver` 沒有現成、零成本的「可走表面支撐」訊號；要有，就得新增一次
> 查詢，那是新的設計決定，需另開提案。
>
> **待清理（等使用者決定）**：`MotionDriver.ExecuteBaseMovement` 的 `CollisionFlags` 量測欄位
> 已無用途，可移除；X Bot 上 `enableFootstepConsoleTrace` 仍開著（場景未存檔）。
> ⚠️ 三輪 Play 測試都在 `Stairs (1)`（x≈−23～−17），不在 ramp prototype（x≈5–12, z≈9–12）。
>
> ---

> # ⚪ 2026-09-24（續三）— **`CollisionFlags` 量測第一輪：測錯地點，需補測**
>
> 📖 正本：`docs/43` §9（新增）。`MotionDriver.cs` 加了純量測（接住 `Move()` 原本丟棄的
> `CollisionFlags` 回傳值，寫進既有 `[StepUpDiag]` log 的 `collisionFlags`／`collisionBelow`
> 兩欄，零行為改變，0 error）。
>
> **🔴 這輪測試資料核對出地點不對**：用 `unity cmd eval_file` 查場景座標，比對角色軌跡
> （x≈−12~−22, z≈0.75~4.17），對上的是 **`Stairs (1)`**（layer=Default，**一般未改造的樓梯**），
> 不是 `docs/42`～`docs/43` 一直在驗證的 `Stairs_650_400_300_Prefab`／`RampProto`（x≈5~12,
> z≈9~12）。且 `[State]` log 只有 Idle↔Move 切換，**沒有任何 Jump/Falling 進場**——沒測到主動
> 跳躍或平台邊緣墜落；三段 Down pass 最高只到 2.6 m/s，**沒有 Run 速度資料**。
>
> **已量到但不是這輪要回答的問題**：平地 Sprint 對照組（167 帧）`isGrounded`／`collisionBelow`
> 100% 一致；未改造樓梯 Walk Down（3 段）裡，`isGrounded=False` 的帧只有約 25% 同時
> `collisionBelow=True`，且存在量級相近的反向不一致（`G=T,B=F`）——雙向都有噪音，不是單向更穩定。
> **這批數字保留在 §9.3 供參考，但不能拿來回答「CollisionFlags 能不能解決 Ramp Run Down 吃案」**，
> 地點與速度都對不上。
>
> **下一步**：`enableStepUpDiagnosticsPrototype` 與新欄位已就緒，不需要再改程式，只需要在正確
> 地點（`Stairs_650_400_300_RampProto`）重測 Ramp Walk Down／Ramp Run Down／主動跳躍／平台邊緣
> 墜落。
>
> ---

> # 🔵 2026-09-24（續二）— **`IsGroundedConfirmed` 提案 REJECTED：真實 Replay 驗證，淨效果是平手**
>
> 📖 正本：`docs/43` §8（新增）。⛔ 只讀不改，未新增任何欄位、未修改任何判定邏輯。
>
> 使用者裁決：不要信「原始閃爍收斂成更少次確認轉換」這句話本身，要求真的把 Run Down trace
> 帶著唯一假設性代換（`IsGrounded → IsGroundedConfirmed`）跑過 `FootstepDetector` 真實判定邏輯，
> 不是手推。用 `unity cmd eval_file` 反射呼叫 `MotionDriver.TrackGroundStateDivergence`／
> `ShouldConfirmGroundStateChange`（**正式程式碼本身，不是複刻**），逐帧餵入這次測試的真實
> `postGrounded` 序列（100 帧，frame 480–580，來源 `[StepUpDiag]` 逐帧無漏）。
>
> **結果：REJECTED**——確認轉換確實只發生 2 次（離地 f516、落地 f563，與 §7.2 手推方向一致），
> 但**總抑制帧數幾乎不變**（raw 39/100 帧 vs confirmed 40/100 帧，≈0.47s vs ≈0.48s）。轉換次數
> 變少 ≠ 抑制總量變少，只是**重新分布**：救回 5 帧（f509/511/513-515，分歧累積中但尚未確認離地）
> ／新增抑制 6 帧（f523/536/552-553/558-559），且**新增的 6 帧全部發生在 `raw grounded=True`
> 的瞬間**——角色物理上已經確認觸地，只是「確認」還沒追上，導致這個提案會在角色剛踩到東西的
> 那一刻硬生生吃掉那一步，尤其 f558/559 正好卡在下坡結束、動作恢復正常的**落地安定尾段**，
> 反而延後了正常腳步聲恢復的時機。**淨值是負的，不建議採用。**
>
> **第三個訊號盤點（使用者要求）**：檢查 `MotionDriver` 是否已有「可走表面支撐」語意。結論：
> **沒有，但找到一個零成本候選**——`ExecuteBaseMovement` 呼叫 `characterController.Move()`
> 時完全沒接收回傳的 `CollisionFlags`（Unity 每次 Move 免費算好，目前直接丟掉）。⚠️ **未驗證**
> 在這個場景下有沒有用，需要另一輪量測（加一條讀 `CollisionFlags` 的純觀測 log）才能判斷，
> 本節不提案新增欄位。`StepDownAssist` 的 `slopeOk` 查詢與 `FootIKGroundData.HasGround` 都不
> 符合「零額外查詢」或「角色整體支撐」的要求，已排除。
>
> ## 現況
> 下樓腳步聲吃案問題**目前沒有已驗證可用的修法**——三個候選（`StateType.Jump`／`JustLanded`
> 重組／`IsGroundedConfirmed`）都已否決，`CollisionFlags` 是唯一還沒驗證過的候選。回到研究階段，
> 不進實作。下一步（若要繼續）：先加 `CollisionFlags` 觀測 log、重跑 Run Down 測一輪，再談是否
> 提案。
>
> ---

> # 🟡 2026-09-24（續）— **下樓腳步聲吃案：訊號歸屬盤點 ＋ 最小提案（⛔ 只提案，未實作）**
>
> 📖 正本：`docs/43` §7（新增）。範圍明確限縮：**只處理 Run/Walk 下樓吃案**，Run 上樓的
> Pelvis／`FootPlantTracker` 內部守門（§5.2／§6.4）**不動、不在本節範圍**。
>
> **盤點結論**：黑板上與「是否空中」相關的訊號只有 `IsGrounded`（逐幀原始值，無去抖動）與
> `JustLanded`／`JustLeftGround`（有 `groundEdgeEventGrace` 0.1s 去抖動，但只是**邊沿事件**，
> 不是連續狀態）。`FullBodyStateMachine.CurrentState.Type`（`StateType.Jump`）**目前完全沒有
> 發布進黑板**，只有 Editor／Debug 面板讀得到。
>
> **兩個候選都比對過，不建議**：
> - `StateType.Jump`——① 架構上违反依賴方向且違反 `FootstepDetector.cs` 自己寫明的「不該知道
>   有哪些狀態存在」設計原則（當初為了修 Climb2m 假腳步聲刻意做的選擇）；② **用本輪實測資料
>   驗證過會更差**——`fallEntryGrace`（0.35s）的重置規則是「任何一幀 `isGrounded=true` 就整個
>   歸零」，比 `MotionDriver` 的確認邏輯更敏感，Run Down 那段實測資料裡 FSM **一次都沒有**判定
>   為 Falling。
> - `JustLanded`／`JustLeftGround` 本身——是邊沿不是狀態，撐不住逐幀的 Airborne 抑制閘門，
>   若在 `FootstepDetector` 自己拼湊區間狀態，等於重新發明 `MotionDriver` 已經算好的東西。
>
> **推薦方案**：`MotionDriver.SyncGroundedState` 內部早就有一個 private 的 `_wasGrounded`——
> 這正是「已確認接地狀態」的**連續值**，`JustLanded`／`JustLeftGround` 只是它的邊沿投影。
> 提案新增黑板欄位 `IsGroundedConfirmed`，唯一 Writer 仍是 `SyncGroundedState`（同一函式、同一組
> 既有變數，只多寫一行），`FootstepDetector` 的 Airborne 閘門改讀這個而非原始 `IsGrounded`。
> **用本輪 Run Down 實測序列逐幀重建驗證**：5 次原始閃爍會被收斂成 1 段乾淨的「真的離地」區間
> （約 0.53s，兩個翻轉點與實測到的 `ForcePlanted` 時間點吻合），且不引入任何新計時器——完全重用
> 已存在、已被信任的 `groundEdgeEventGrace` 邏輯。
>
> **ADR 判斷**：命中「黑板 schema 變更」觸發條件，但參考 `JustLanded`／`JustLeftGround` 當初
> 新增的先例（單純增補、不動 FSM 拓撲／管線順序／驅動介面），建議走 Living Spec 增補，不必開
> 新 ADR——**這是建議，不是裁決**。
>
> 6 組 regression case（平地 Sprint／Ramp Walk Down／Ramp Run Down／主動跳躍／平台邊緣墜落／
> 落地）已設計但未寫測試，見 `docs/43` §7.5。**⛔ 本節只到提案為止，未新增任何欄位、未改
> `SyncGroundedState`／`FootstepDetector`／任何判定邏輯**，下一步待使用者裁決是否採用。
>
> ---

> # 🟠 2026-09-24 — **`[FootstepTrace]` 事件級驗證：Run 下樓假說確認成立，Run／Walk 上樓另有獨立成因**
>
> 📖 正本：`docs/43` §6（新增）。⛔ 只讀不改，除了新增的觀測 log 本身，未動任何判定邏輯／
> grace／cooldown／IK／Pelvis／ramp。
>
> **追加的程式**：`FootstepDetector.cs` 新增獨立、預設關閉的 `enableFootstepConsoleTrace`
> （與既有 `recordDebugTrace` Inspector 面板 trace 完全分離），只在「有事件或抑制原因改變」時
> 輸出一行 `[FootstepTrace]` Console log（frame／foot／`FootstepKind`／`FootstepSuppressReason`／
> 原始 `IsGrounded`／`FootPlantTracker` 的 phase／armed／clearance／`ForcePlanted`／locomotion
> 速度／IK `HasGround`＋權重相關性欄位）。`unity cmd recompile` 驗證 0 error，已在 X Bot 上開啟
> （與 `enableStepUpDiagnosticsPrototype` 同時開、`enableStepDownAssistPrototype` 仍關閉）。
>
> ## 結果
>
> **平地對照組（250 帧 Sprint）**：`FootstepDetector` 完全健康——左右完美交替、間隔穩定
> 10–21 帧，零抑制。**確立偵測器本身沒有全域性缺陷，異常是樓梯／ramp 地形特有的。**
>
> **Run 下樓（58 帧，4.06 m/s）：假說 CONFIRMED，且比預期更嚴重**——`isGrounded` 5 次閃爍、
> 10 次 `Airborne` 抑制（雙腳×5），**整段 0 次 `Fired`**（不是間隔變長，是完全沒有任何一聲正常
> 腳步）。`ForcePlanted` 全程 0 次，排除「多餘 Landing 音效」的可能性——純粹是原始（未經
> `groundEdgeEventGrace` 過濾的）`IsGrounded` 閃爍直接餵給 `FootstepDetector` 的 Airborne 閘門，
> 導致大量吃案。
>
> **Run 上樓（2 段樣本）：獨立於 §5.2 已確認的 Pelvis/Foot IK 視覺缺陷**——不是同一個病灶。
> 擊發率過低（平地 13–21 帧一次 → 樓梯段拉長到 40–101 帧一次），成因是零星
> `GroundDiscontinuity`／`NeverArmed` 死路，外加一次 `ForcePlanted` 清空了上膛進度。
>
> 🔴 **超出原提問範圍的新發現**：**Walk 上下樓的「root 乾淨」不等於「腳步聲乾淨」**——即使
> `postGrounded` 全程 `True`（§4 已驗證），`FootPlantTracker` 仍反覆讓某一隻腳的步伐被
> discontinuity 吃掉，音效序列上出現同一隻腳連續擊發 2–4 次（f610/670/716/753 連續四次右腳等）。
> **左右交替在偵測器層的異常機制是「另一腳缺席」，不是「腳的身分算錯」**——沒有觀察到真正的
> 誤判，只有「該響的那次沒有響」。
>
> ## 下一步（等使用者裁決，本節未提出任何修法）
> Run 下樓（Airborne 整段吃案）與 Run／Walk 上樓（零星 discontinuity 死路）是**兩個獨立成因**，
> 不要假設同一個修法能一起解。`docs/43` §6.7 有完整對照表。
>
> ---

> # 🔴 2026-09-23（續五）— **Ramp 是否也解下樓？Walk 通過、Run 上／下樓各自曝出新問題**
>
> 📖 正本：`docs/43` §5（新增）。⛔ 只讀不改，`enableStepDownAssistPrototype` 全程保持關閉，
> Pelvis／Foot IK／Jump/Falling/StepDown 邏輯**逐字未動**。
>
> **任務**：既然 CharacterController 上下樓都只碰 ramp（§4 已驗證上樓），驗證 ramp 是否同時解決
> 下樓問題，決定 `docs/42` 的 Query→Apply StepDown Assist 是否還需要正式接線。
>
> ## 分岔裁決結果（Walk 速度，對應原始任務範圍）
>
> **通過**：Walk 下樓 4 次＋Walk 上樓 4 次（合計 1350 帧，兩次獨立 Play 測試皆同款結果）
> `postGrounded` **全程 0 次 False**，`stepOffsetDelta` 維持 0.005–0.08 平滑帶，全程**沒有任何
> 一次**觸發 FSM 的 Falling／Landing 狀態切換。**Walk 速度下 ramp geometry 已完整取代 StepDown
> Assist 的需求，`docs/42` 的 Query→Apply 機制對這道樓梯的 Walk 使用情境不需要正式接線。**
>
> ## 使用者人工複測追加：Sprint（Run）上／下樓，發現兩個 ramp 沒有解決的新問題
>
> 使用者體感明確裁決：「`IsGrounded` 穩定 ≠ 呈現問題已解」——Sprint 上樓視覺仍不自然、樓梯上
> 腳步聲節奏不規律。追加 Run 測試後，**證實體感是對的，且拆解出兩個獨立、不同機制的問題**：
>
> 1. **Run 上樓**：root／碰撞層乾淨（3 段合計 194 帧 `postGrounded` 0 次 False），但 Pelvis／
>    Foot IK 權重層被 Run 速度重新暴露——`pelvisY` 出現 ±0.25m 深下潛（是 Walk 時 §4.3 量到的
>    −0.054m 的 4.5 倍），單腳 IK 權重被夾在 0 或 1 長達 14–24 帧。**這正是 `docs/43` §3.3 當時
>    預告、但 Walk 速度沒有逼出來的「雙腳都命中才算，否則鎖零」資料模型缺陷**——不是新 bug，
>    是既有缺陷被 Run 步頻重新餵飽。修法方向仍是 §3.3 已指出的「有效支撐集合」重構，本輪只是
>    把預告變成已驗證的症狀。
> 2. **Run 下樓**：root 位置本身連續平滑（無彈道式不連續），但 `characterController.isGrounded`
>    這顆旗標本身在同一段裡有 69–73/83 帧回報 False（分散成多段 10–25 帧的閃爍，不是一次長墜
>    落）——與 Walk 下樓的乾淨結果形成鮮明對比。FSM 沒有被污染（閃爍都短於 `fallEntryGrace`
>    0.35s，Falling 動畫沒有誤觸發），但 `FootstepDetector` 直接讀**沒有 grace 過濾**的原始
>    `IsGrounded`，閃爍期間大量真實腳步被判定 `Airborne` 而靜音——**這是下樓腳步聲不規律最直接
>    的成因**，且是 ramp 沒有完全解決的部分（不是離散級高的彈道下墜，較像 Unity
>    `CharacterController.isGrounded` 在快速下坡本身就不穩定的已知侷限）。
>
> **排除的假說**：一度懷疑 Run 速度下 root 逐帧位移太大會頻繁觸發 `FootPlantTracker` 的
> `rootPlaneJumped`（0.06m）不連續守門，**用資料驗證後推翻**——194 帧裡只中了 1 帧，量級與 Walk
> 對照組相近，不是主要矛盾。
>
> **已知缺口（本輪刻意沒有補）**：完全沒有腳步聲事件本身的 Console 紀錄（`FootstepDetector`
> 的既有 debug 只有 Editor 面板，非 log）——上面兩點腳步聲成因是「讀地面／IK 訊號 + 讀程式碼」
> 推導的**假說**，不是直接量到的事件序列。下一步建議（未執行）：暫時給
> `FootstepDetector.Detect` 加一條純觀測 `Debug.Log`（欄位已經都在 `TraceEntry` 裡，換管道即
> 可），重跑一次 Run Up／Down 才能把假說換成確認。
>
> ## 下一步（等使用者裁決）
> 1. **下樓 StepDown Assist**：Walk 已證明不需要，Run 下樓的問題是 `IsGrounded` 旗標閃爍（不是
>    彈道掉落），`docs/42` 現有機制本來就不是為了修這個，**不建議直接拿 StepDown Assist 去補**，
>    需要另外評估。
> 2. **上樓 Pelvis 重構**：Run 已把 §3.3 的預告變成確認的症狀，若要正式修，方向已經寫在 §3.3。
> 3. **腳步聲診斷**：加 `FootstepDetector` 的觀測 `Debug.Log`、重跑 Run Up/Down，把假說變確認。
> 三者互相獨立，可任選其一或都先擱置。
>
> ---

> # 🟣 2026-09-23 — **本輪總結：樓梯 IK 抖動全鏈路（下樓已修＋上樓已驗證方向）**
>
> 📖 正本索引：`docs/31`（音效／FSM，已修）、`docs/42`（下樓 step-down assist，已收斂為提案）、
> `docs/43`（上樓抖動，已驗證方向）。以下每個細節都已個別記在對應文件與本檔下方的分段條目
> （時間序：由新到舊），這裡只給**下一個 session 需要的總覽**。
>
> ## 這輪的完整故事線
>
> 使用者測 Foot IK 時發現下樓梯高頻率觸發落地聲，一路追下去發現這其實是**同一個缺口的多層
> 下游症狀**：`MotionDriver.ExecuteBaseMovement` 沒有 UE 意義上的「Walking 模式」（主動貼地），
> 水平垂直永遠在同一次 `CharacterController.Move()` 彈道式積分——下樓每一階都真的自由落體，
> 上樓則是 `stepOffset` 內部逐幀量級不均的雜訊。兩個方向分開修：
>
> ### A. 下樓（`docs/31` ＋ `docs/42`，**已修並提交**）
> 1. **落地聲/JumpTakeoff 音效灌爆**——`MotionDriver.groundEdgeEventGrace`（事件層 grace）已修，
>    commit `d995a5a`。
> 2. **反覆進 Falling 動畫**——X Bot `JumpStateParams.fallEntryGrace` 由隱含 0.1s 調到 0.35s，
>    同一個 commit。
> 3. **Query→Apply 兩段式 step-down assist prototype**（`docs/42`，三輪迭代：自碰撞 bug → 水平
>    投影修正 → retry window 修正 Gate①單幀鎖死）——**已收斂**：樓梯掉出從「每階完整彈道落下
>    0.1–0.3s」壓到「轉角處 1–4 幀短暫重試」，使用者體感確認「有優化但還有問題，比上樓好」。
>    **已提煉成正式接線提案**（`docs/42` §8：R1–R10 已驗證機制規則、3 個待 tuning 參數、
>    EditMode/PlayMode regression test 清單）——**⛔ 提案階段，未接線，未裁決是否正式採用**。
>    `MotionDriver` 目前有 `enableStepDownAssistPrototype`（預設關閉的 prototype 開關，
>    現場 X Bot 上是 **false**）。
>
> ### B. 上樓（`docs/43`，**量測與方向驗證已完成，未轉正式架構**）
> 1. 假說量測：H3 成立——root 位置本身有 6.7× 幀間雜訊的連續修正（不是單一大跳變），Foot IK／
>    骨盆的雙腳同時命中門檻把這個雜訊放大成延伸鎖零再追趕的節奏；`IsGrounded` 全程不閃，
>    跟下樓是不同失效模式。
> 2. 與 Codex 討論修法方向：**優先建議「分層 ramp collider（移動用 ramp、Foot IK 查詢用原本
>    階梯幾何）＋ Pelvis 支撐語意重構」並行**，Presentation Reconciliation（視覺/物理 root
>    解耦）降為保險層、且若採用不能是裸 `SmoothDamp`（違反 `docs/29` §I 既有裁決）。
> 3. **單一樓梯 prototype 已驗證分層假說成立**：`Stairs_650_400_300_Prefab`（場景實例）
>    改到新 layer、新建 ramp collider 供 Player 碰撞、Foot IK 遮罩排除 ramp——`stepOffsetDelta`
>    雜訊從 6.7× 收斂到 2.4×、1963 幀 `IsGrounded` 全程未閃。**事先設的分岔條件（root 跳變消失
>    但 pelvis 仍週期性歸零才動 Pelvis）未觸發，Pelvis 邏輯這輪完全沒動。**
>
> ## 🔴 現在磁碟／場景的真實狀態（下一個 session 開場務必先讀這段）
>
> - **程式**：`MotionDriver.cs` 有多個 prototype 開關，**全部預設關閉、皆已 commit**
>   （`enableStepDownAssistPrototype`／`maxStepDownDistance`／`stepDownQueryLayers`／
>   `stepDownRetryWindow`／`enableStepUpDiagnosticsPrototype`）。`RampPrototypeLayerSetup.cs`
>   已 commit，但**只有在場景裡的 X Bot 掛了這個元件、且樓梯真的被搬到 TreadIKOnly layer 時
>   才有實際效果**。
> - **X Bot 目前的 Inspector 狀態**（未 commit，因為含場景與資產的即時調整）：
>   `enableStepUpDiagnosticsPrototype = true`、`enableStepDownAssistPrototype = false`、
>   `FootIKController.settings.GroundLayers` 已改成排除 `RampProto` 位元（`4294967287`，
>   不是預設的 Everything）、掛了 `RampPrototypeLayerSetup` 元件。
> - **場景（`SampleScene`）目前是 dirty、未存檔**：`Stairs_650_400_300_Prefab` 這個場景實例的
>   layer 已改成 `TreadIKOnly`；新增了 `Stairs_650_400_300_RampProto`（純碰撞體 ramp）。
> - **`ProjectSettings/TagManager.asset` 已新增兩個 layer**：index 3 = `RampProto`、
>   index 8 = `TreadIKOnly`（這個是專案級檔案，若之後 commit 程式，這個也該一起看要不要留）。
> - ⚠️ **上述場景/資產改動全部只影響這一道樓梯，且明確是 prototype**——下一個 session 如果
>   決定不採用，回復方式：不存檔 `SampleScene`（Unity 重開或 revert 即可丟棄樓梯 layer 變更與
>   ramp GameObject）、把 X Bot 的 `GroundLayers`／新開關改回預設、`TagManager.asset` 的兩個
>   layer 名稱可以留著不用（不影響任何東西）或清空。
>
> ## 下一步（三條路，互不衝突，可以任選）
> 1. **下樓**：依 `docs/42` §8 裁決是否正式接線（補 regression tests、移除除錯專用程式碼、
>    決定 tuning 參數最終值）。
> 2. **上樓**：若認可 ramp prototype 的結果，裁決要不要推廣到其餘 3 道樓梯、要不要存檔這次的
>    實驗場景；若要正式接線，比照 `docs/42` §8 同一套紀律（機制規則 vs tunable 參數 vs
>    regression tests）走一次。
> 3. **兩者都先擱置**，等使用者評估完手感／決定產品優先順序再回來。
>
> ---

> # 🟢 2026-09-23（一，續四）— **上樓抖動：與 Codex 討論修法方向，⛔ 未裁決**
>
> 📖 正本：`docs/43` §3。
>
> 使用者要求先跟 Codex 討論再回報。**建議方向：「分層 ramp collider」＋「Pelvis 支撐語意重構」
> 並行**：
> 1. CharacterController 只碰撞一個平滑 ramp（樓梯移動用）——從源頭消除 `stepOffset` 逐幀
>    雜訊，不需要角色一階一階解算。
> 2. 樓梯視覺階梯的碰撞體移到獨立 layer（Player 忽略、Foot IK `GroundLayers` 仍查得到）——
>    保留逐階精確度，腳還是會對齊到真正的踏面，不是隱形斜坡。
> 3. Pelvis 補償的真正斷點在呼叫端 `ikAllowed && left.HasHit && right.HasHit ? ... : 0f`，
>    混了「查到候選面」／「是不是支撐腳」／「套用多少 IK」三件事，需要重構成「有效支撐集合」
>    的資料模型（公式待下一輪設計）。
>
> Presentation Reconciliation（視覺／物理 root 解耦）**降為保險層**，只有樓梯無法／不便追加
> ramp collider 時才補；若採用**不能是裸 `SmoothDamp`**（那會違反 `docs/29` §I「不准 fade
> 掩蓋斷點」的既有裁決），要做成有界、可追溯的 correction channel（細節記在 `docs/43` §3.4）。
>
> **下一步待使用者裁決**：①是否核准分層 ramp 當樓梯新 authoring 慣例（需新增專用 layer、調
> Layer Collision Matrix、為 4 個樓梯物件各加 ramp collider，屬資產改動，走 Editor API，
> 仍需先問）；②Pelvis 支撐語意公式需要一輪設計；③若核准，先做一道樓梯 prototype 驗證。
>
> ---
>
> 📖 正本：`docs/43` §2。
>
> 撈 1979 幀 `[StepUpDiag]` log 分析，結論：
> 1. **上樓與下樓是不同失效模式**——純上樓連續片段（4 階）`IsGrounded` 全程 `True`，
>    不像下樓靠 grounded 閃爍驅動。
> 2. **H1 部分成立**：root 位置不是單一大跳變，是 `characterController.stepOffset` 內部
>    每幀量級不均的連續修正（同段 `stepOffsetDelta` 0.018–0.121m，約 6.7 倍幀間變化）。
> 3. **H2 也成立**：Foot IK 權重／骨盆補償在延伸窗口內釘死在極值（`pelvisY=0.00000` 出現
>    83／1979 次、`rightFootW=0.000` 連續 10+ 幀）——`ComputePelvisOffset` 要求雙腳同時命中，
>    快速爬階時擺盪腳量不到地面，補償整段鎖 0，等擺盪腳落地才一次追趕。
> 4. **結論：H3，但有明確上下游關係**——root 逐幀雜訊是上游訊號，Foot IK 權重計算是
>    root-relative，雜訊被下游放大，不是兩個獨立病灶。
>
> **尚未提出修法、尚未裁決**——下一輪才談方向（可能動 root 平滑、可能動 Foot IK 權重/
> 雙腳命中門檻、也可能兩者都要）。
>
> ---
>
> 📖 正本：`docs/42` §8（正式接線提案）、`docs/43`（上樓抖動研究輪，新檔）。
>
> 使用者裁決：Q8 不再追加壓低殘餘 1–4 幀掉出，把三輪 prototype 提煉成正式接線提案——
> `docs/42` §8.1 列出 **R1–R10 已驗證機制規則**（每條對應一次真實失敗量測，建議原樣鎖進
> 實作，例如 R6「Apply 前必須檢查本幀垂直分量已 ≤0」直接對應 Spike ③ 的起跳被吃掉 bug）、
> §8.2 列出 3 個**待 tuning 參數**（`maxStepDownDistance`／`stepDownQueryLayers`／
> `stepDownRetryWindow`，皆單一場次量出的實驗值）、§8.4 列出 EditMode／PlayMode regression
> test 清單（最重要：`Gate_RejectsWhenAscending` 與 `StepDownAssist_ActiveJumpLaunch_NotClobbered`，
> 保護 Spike ③ 的發現）。**本節只是提案，沒有動程式**，`enableStepDownAssistPrototype` 仍是
> 預設關閉的 prototype 開關。
>
> 同時另開 `docs/43` 研究上樓抖動——`MotionDriver` 新增純觀測開關
> `enableStepUpDiagnosticsPrototype`（與 step-down assist 完全獨立，不寫任何欄位、不影響
> `finalMovement`），每幀印 `stepOffsetDelta`（`actualDY − requestedDY`，量 `characterController.
> stepOffset` 內部頂了多少）＋ `pelvisY`／`leftFootW`／`rightFootW`。列了 H1（root 跳變）／
> H2（IK 補償跟不上）／H3（疊加）三個假說，**尚未量測、尚未提出修法**。
>
> `dotnet build` 0 錯誤。**下一步在使用者**：Play Mode 走一輪 Walk 上樓梯（建議關掉
> `enableStepDownAssistPrototype`，只開 `enableStepUpDiagnosticsPrototype` 避免 log 交錯），
> 撈 Console log 驗證 H1/H2/H3。
>
> ---

> # 🔵 2026-09-23（一，續）— **Near-Floor Following 研究輪**：Q8 三輪 Play 迭代，有意義改善、未完全通過
>
> 📖 正本：`docs/42-near-floor-following-step-down-study.md`。索引：`docs/00-map.md`。
>
> 使用者裁決：下樓梯 jitter 不要寫 Stairs 特例，參考 UE `FindFloor`／`MoveAlongFloor`／
> `FStepDownResult` 研究 `MotionDriver` 是否缺通用機制。**一句話結論**：`ExecuteBaseMovement`
> 永遠等同 UE 的 `PhysFalling`（水平垂直同一次 `Move()` 彈道式積分），沒有對應 `PhysWalking`
> （主動貼地）層——落地聲／Falling 閃爍（已修）與 Foot IK 骨盆抽動（未修）是同一缺口的三個症狀。
>
> ## Q1／Q2 使用者裁決
> `maxStepDownDistance` 放 `MotionDriver` Grounding 設定（不進黑板），prototype 先用
> 0.22–0.25m；assist 只在「上一幀已 grounded ＋ 本幀有效 horizontal locomotion」時生效，
> 靜止不介入（保護既有 edge-standing 裁決）。
>
> ## Blocking Question 的 Spike 結果（`unity cmd eval_file`，用完即刪）
> 🔴 **原方案（單次 Move 加無條件下壓分量）被推翻**：Spike 實測懸崖邊緣會被無條件全額
> 提交下壓距離（單幀瞬移 0.22–0.25m），**不是 probe**。且原本的「只看 grounded」gating
> 會把主動跳躍的正向速度直接覆寫成負的——起跳當幀完全跳不起來。
> ⇒ **改為 Query（`Physics.CapsuleCast`，非提交）→ Apply（找到才 Move，找不到完全不介入）
> 兩段式**，且 Apply 前須額外檢查本幀垂直分量已 ≤ 0（不只看 grounded）。
> 找不到面時的 fallback 與現行 `reboundForce` 貼地力逐位元一致，不改變懸崖邊緣現有行為。
> 方法論副產物：`CharacterController.Move()` 不認 `EditorSceneManager.NewPreviewScene()` 的隔離
> 物理世界，下次要獨立驗證得用 Play Mode 或真實場景幾何。
>
> ## Q3／Q7 讀碼查證結果（2026-09-23，續）
> **Q3（與 Dynamic Capsule Offset／Traversal Collision Profile／RootReachConstraint 交互）：
> 無交互，三者互斥。** `UpdateCapsuleOffset` 已對 Traversal Collision Profile 有 guard
> （`if (_traversalCollisionProfileActive) return;`），且已經是 `ExecuteBaseMovement` 第一行；
> Traversal 有自己獨立的執行路徑，不走 `ExecuteBaseMovement`；`RootReachConstraint` 根本不是
> `characterController.center` 的寫入者（是手部 IK 腕點夾持，跟膠囊無關）。**Query 步驟放在
> `UpdateCapsuleOffset(data)` 之後即可，不需要新協調機制。**
>
> **Q7（Query 遮罩該對齊哪份設定）：獨立新欄位，初值 `~0`。** 盤點後發現本專案**沒有專用
> Ground layer**（`TagManager.asset` 只有 Default/TransparentFX/Ignore Raycast/Water/UI/Player/Enemy），
> 且 Physics Layer Collision Matrix 全開（`DynamicsManager.asset` 全部是 `f`）——`characterController.Move()`
> 本身的真實碰撞範圍就是「全部 layer」，縮小 Query 遮罩只會讓 Query 跟 `Move()` 認知不一致。
> `~0` 是目前配置下**唯一與 `Move()` 行為一致**的選擇，且比照 `FootIKSettings.GroundLayers`／
> `TraversalProbeSettings.ObstacleMask` 各自獨立一份 `LayerMask` 的既有慣例，不跨模組共用。
>
> **Q3／Q7 已無阻斷，剩下的唯一技術性阻斷項是 Q8**（Query→Apply 對真實樓梯的貼齊平滑度——
> `CharacterController.Move()` 不認 preview scene 隔離物理，驗不到，需要 Play Mode 或真實場景）。
> Q4／Q5／Q6 都是可以留到 prototype 之後再談的次要問題，不擋進度。
>
> ## Q8：Play Mode Prototype 三輪迭代（`docs/42` §7，正本）
>
> `MotionDriver` 新增預設關閉、局部可撤銷的實驗開關（`enableStepDownAssistPrototype`），
> 用真實場景樓梯 + Console log（`unity cmd console --tail 3000` 撈、grep `[StepDownAssist]`、
> 統計連續失地時長分布）量測。三輪：
>
> 1. **`queryDist` 鎖死在 skin width**——Query 膠囊跟 `characterController` 同形狀同位置往下
>    cast，第一個命中的是**自己**。改用 `Physics.CapsuleCastNonAlloc` 過濾自身階層修復。
> 2. **修完自碰撞後樓梯每階仍週期性掉出 3–4 幀**——Query 從「移動前」位置直探，越過階緣時
>    正下方是立面（slope gate 擋）或完全落空。改成 Query 起點加上本幀水平位移（呼應 UE
>    `MoveAlongFloor` 先套水平再找地板）。**同時發現 Gate①（上一幀是否 grounded）單幀記憶
>    會永久鎖死**——一旦被某幀 slope gate 誤判擋下就再也不重試，量到比沒裝 assist 還糟的
>    42 幀掉出（0.7s）。
> 3. **Gate①改成短時間重試窗口**（`stepDownRetryWindow` 0.08s，🔬 實驗值，獨立欄位不共用
>    `fallEntryGrace`／`groundEdgeEventGrace`）。用新增的 `lostSupportAge` 欄位驗證重試期間
>    確實每幀重新查詢、不是假等待。三輪掉出時長分布：3–6 幀 → 混雜 8 次 11–42 幀長掉出 →
>    收斂 1–4 幀為主（唯一 62 幀離群值查證是真實主動跳躍，`preY=+5.51`，Gate③正確排除）。
>
> **Q8 結論**：有意義改善、尚未完全通過——樓梯掉出從「每階完整彈道落下 0.1–0.3s」收斂到
> 「轉角處 1–4 幀短暫重試（16–66ms）」，使用者體感：「有優化但還有問題，但情況比上樓好了」，
> 與數據方向一致。**上樓抖動是完全不同機制**（`stepOffset` 一幀內位置跳變，非重力/grounded
> 問題），獨立記錄，本輪刻意不修。
>
> **下一步待裁決**：要不要繼續壓縮殘餘的 1–4 幀重試，或先暫停轉去討論正式接線與 tuning；
> 上樓抖動需要另開研究。
>
> ---

> # 🟡 2026-09-23（一）— **下樓梯高頻率落地聲**：音效與 FSM 兩層都已修，⛔ 未經 Play 驗收
>
> 📖 根因與修法見 `docs/31` §C（stateDiagram）／§G／附錄 B（authored 值）／debug 表 #8，
> 與 `docs/02` §1.1 `JustLanded / JustLeftGround` 列。
>
> ## 使用者回報（分兩輪）
>
> 1. 測試 Foot IK 時發現下樓梯會高頻率觸發落地聲或腳步聲，疑似反覆進入掉落動畫。
> 2. Direction A 落地後回報：「還是會出現下落但沒聲音了，但是我需要的是正常下樓狀態」
>    ⇒ 音效已修好，但 FSM 仍會反覆進 Falling 動畫，需要進一步處理。
>
> ## 根因（兩層同源，同一套 grace 過濾模式分別補齊）
>
> 場景樓梯實測級高 0.195m，純重力自由落體到下一階 ≈0.199s——`characterController.isGrounded`
> 本來就會每一階閃爍一次原始訊號。
>
> - **音效層**：`data.JustLanded`／`JustLeftGround`（[MotionDriver.cs:2321](Assets/Scripts/Presentation/Motion/MotionDriver.cs:2321)
>   的 `SyncGroundedState`）原本是無去抖動的純邊沿偵測，直接驅動 `AudioController` 的
>   Landing／JumpTakeoff 音效，每階都各響一次。
> - **FSM 層**：`JumpState.fallEntryGrace` 雖然機制上就是為了過濾這類抖動而存在
>   （`docs/31` debug 表 #8 早就預告這個症狀），但 X Bot 的 `JumpStateParams.asset`
>   從未序列化這個欄位 ⇒ 吃 `DefaultFallEntryGrace = 0.1s`，而 Console 實測（2026-09-22
>   場次）樓梯 passive-fall 連續離地多數 0.02–0.21s、總離地（含 grace）最長 ≈0.306s——
>   0.1s 遠不夠，於是幾乎每階都真的判定為一次「非主動 Falling」。
>
> ## 已做
>
> **Direction A（音效層）**：新增 `MotionDriver.groundEdgeEventGrace`（預設 0.1s，獨立欄位，
> 不共用 `JumpStateParams`——Motion 層不反向依賴 StateMachine.States）。`data.IsGrounded`
> 完全不動（仍每幀原始值，`docs/38` §13 裁決不受影響）；只有 `JustLanded`／`JustLeftGround`
> 這兩個事件延遲到 grounded 狀態連續變化達 grace 才確認觸發。新增純函數
> `TrackGroundStateDivergence`／`ShouldConfirmGroundStateChange`（同 `JumpState.TrackUngroundedSince`
> 模式）＋ EditMode 測試 `Assets/_Project/Tests/EditMode/GroundEdgeEventTests.cs`（8 條）。
>
> **FSM 層（使用者確認需要後追加）**：透過 `unity cmd set_serialized_field`（Editor API，非手改
> YAML）把 X Bot `Assets/ScriptableObjects/StateMachine/JumpStateParams.asset` 的
> `fallEntryGrace` 由（隱含的）0.1s 顯式調到 **0.35s**——依 Console 實測最大值 0.306s 留安全
> margin，仍遠低於真實跳躍滯空 ≈0.67s（不影響主動跳躍資格判定，`fallEntryGrace` 本來就不放寬
> 起跳資格，只延遲非主動 Falling 的判定）。`AssetDatabase.SaveAssets()` 確認已落盤
> （`git diff` 可見新增一行 `fallEntryGrace: 0.35`）。
>
> `dotnet build`（Project.Runtime／Project.Tests.EditMode）與 Unity live Editor `recompile` 皆 0 錯誤。
> **EditMode 628／627 passed／0 failed／1 skipped**（含新增 8 條全過）。
>
> ## ⛔ 尚未做
>
> 1. 🔴 **Play 驗收（唯一剩下的缺口）**：`fallEntryGrace 0.35s` 這個數字是從**單次** Console log
>    樣本（2026-09-22 場次，32 筆進出 Falling 記錄）推導，未必涵蓋所有走位／速度組合
>    （例如用跑步下樓梯，理論上單階水平位移可能跨 2–3 階，滯空時間可能更長——見 `docs/31`
>    附錄 B 的 margin 說明）。**需要使用者重新下樓梯（含走與跑）確認手感**。
> 2. **已知取捨未驗證**：主動跳躍離地時 `JustLeftGround`（→ JumpTakeoff 音效）現在也會被
>    `groundEdgeEventGrace`（0.1s，音效層，未調整）延遲。理論上可接受（已知近似 + 實際跳躍
>    滯空遠長於 0.1s），但未經耳朵驗證。
> 3. **敵人／其他角色未動**：這次只調了 X Bot 的 `JumpStateParams.asset`；`EnemyJumpStateParams.asset`
>    的 `fallEntryGrace` 維持原值，若敵人也會走樓梯需要另外評估。
>
> ---

> # 🏁 2026-09-22（四）— **Climb2m 收尾**：動畫穿模判定為 Accepted Limitation，traversal 可以停了
>
> 📖 正本：`docs/40` **§17**（含裁決與重新評估的觸發條件）。
>
> ## 裁決（使用者明確裁決）
>
> > 目前不打算為 traversal 使用會刻意暴露這個角度的繞行／特寫鏡頭。以正常第三人稱 gameplay camera
> > 為主，所以**選 A，不做 post-release hand clamp**。之後真的要拍 close-up 再重新評估換 clip 或
> > 局部修正，**不要現在先加系統**。
>
> ⇒ **動畫在 mantle 段的手部穿模＝ Accepted Limitation。**
> 理由不是做不到，而是**在目前的鏡頭設計下它買不到任何看得見的東西**。
>
> **重新評估的觸發條件**（寫下來才不會變成口頭承諾）：①作品集加入 traversal 繞行／過場／特寫；
> ②改第一人稱或肩上近景；③換上 mantle 段停留更久的 clip。任一成立才回到 `docs/40` §17 的表。
>
> ## Climb2m 目前狀態
>
> | | |
> |---|---|
> | **Validated coverage** | candidate 高度 **1.65 – 2.05 m** |
> | 抓握窗內（我們負責的那段） | 手部殘差 ≤ 0.11 cm、arm ratio ≤ 0.946、**每根骨頭都在面外**（最貼 1 mm） |
> | root 軌跡 | 超出落腳面一律約 4 cm（＝站立 skin width），**不隨牆高變化** |
> | 腳步聲 | 離地期間零擊發；落地那一步是真的踩在平台上 |
> | 自動化 | EditMode 620（619 passed／0 failed／1 skipped）、PlayMode **67／67** |
>
> ## 保留的已知限制（四條，全部有量測依據）
>
> 1. **動畫 mantle 段穿模**（本次裁決）：放手後左手指節進入落腳面 4.6 → 10.2 cm，
>    **與牆高無關**（2.05 m 授權高度照樣穿）⇒ animation limitation，非 execution 缺陷。遊戲攝影機看不到。
> 2. **coverage 下緣是薄冰**：candidate 1.65 時 contact pre-IK 殘差 33.8 cm vs guard 0.35 ⇒ 餘裕 1.2 cm。
>    觸發的話 IK 整個掉到 0（斷裂，不是降級）。實測未觸發。⛔ 不以放寬 guard 換餘裕。
> 3. **進場距離敏感度未系統性量測**：2.05 m 在理想進場 0.462 m 時 arm ratio 0.942–0.949，逼近 budget 0.95；
>    走路進場 0.515 m 時只有 0.866。
> 4. **Foot IK 有與手部相同的一幀管線延遲**，本輪刻意未動。
>
> ## ⛔ 唯一剩下的缺口
>
> 🔴 **人工 Play 視覺驗收** —— ADR-008 **D10** 從 🟡 Trial 轉 Accepted 的唯一條件。
> 其餘自動化驗證全綠。**traversal 可以停在這裡，轉去做作品集展示。**
>
> ---

> # ✅ 2026-09-22（三）— **手指問題結案**：bone → goal 是固定基底轉換
>
> 📖 正本：`docs/40` **§15**（§14 是同一件事的前半場，保留作為「量錯 K」的紀錄）。
>
> 使用者裁決：先驗證 `K = inverse(B) × G` 是否為固定 basis conversion；若近似常數就**不要擴張方案**，
> 轉換放 `TraversalHandIKRig`，並先用 runtime probe 證明三個條件才正式接權重。**照做，全部成立。**
>
> ## K 近似常數
>
> 在 `OnAnimatorIK` 開頭、IK 尚未套用時**同時**取 B 與 G（§14 那次是在 `LateUpdate` 讀骨骼，
> 已被解算改寫，所以量到的是假的）。Climb2m contact window 內逐幀：
> **最大漂移 左 8.22°／右 7.87°**，而 K 本身量級 ≈98° ⇒ 近似常數成立。
> （先排除假象：`angle(goal, 我們設的值)` 是 45–70° 且持續變動 ⇒ `GetIKRotation` 確實來自動畫。）
>
> ## 做法
>
> 既然 B 與 G 每幀同源可得，**直接用本幀的 K**：`SetIKRotation(goal, committedBone × inverse(B)×G)`。
> 那 8° 漂移自動消掉，也不必烘 avatar 常數。轉換只放在 rig——Core 沒有 Animator，
> 也不該知道 avatar 慣例，它只用「骨骼朝向」這個與 rig 無關的語彙回答「手該朝哪裡」。
>
> ⚠️ pinned 朝向取**窗口中點 n=0.1296**，不是 contact 那一格：authored 的手在窗內自己會轉約 14°，
> 釘住起始格會讓 2.05 m 退化（手指從貼面變成懸在前緣外 2 cm）。這是取樣時點，不是新機制。
>
> ## 三個驗收條件（食指指節相對頂面，接觸瞬間）
>
> | 牆高 | 修前 | 修後 | |
> |---|---|---|---|
> | 2.05 | +0.002 m | **+0.007 m** | ✅ 未退化 |
> | 1.80 | −0.034 m | **+0.008 m** | ✅ 改善 |
> | 1.68 | **−0.083 m** | **+0.007 m** | ✅ 不再插進頂面 |
>
> 滿權重 post-IK 殘差 0.00／0.11／0.04／0.07 cm；reach ratio 與修前相同（2.05：0.942 vs 0.946）。
> 🎯 **三個高度的手指位置幾乎相同 ⇒ 高度相依性消失**，與 §13 同一個判準。
>
> EditMode 620（619 passed／0 failed／1 skipped）、PlayMode **67／67**。
>
> ## ⛔ 仍未做
>
> 1. 🔴 **人工 Play 視覺驗收**（ADR-008 D10 轉 Accepted 的唯一缺口）。
> 2. 進場距離敏感度仍未系統性量測（2.05 m 在理想進場時 reach ratio 0.942–0.949，逼近 budget 0.95）。
> 3. Foot IK 的一幀延遲。
> 4. Vault1m／Climb1m **沒有**烘焙手部朝向 ⇒ 仍走 ledge-frame fallback、`handIKRotationWeight` 為 0，
>    行為完全不變。要啟用需各自重烘。
>
> ---

> # 🟠 2026-09-22（二）— **手指插進頂面：根因確定，修法未落地**
>
> 📖 正本：`docs/40` **§14**（前半場；§15 才是結案）。
>
> 使用者回報：「五根指頭第一時間插進牆裡厚面直接浮空」。**這不是回歸，是一直存在**——
> 之前所有量測都只看腕點，看不到手指。
>
> | 牆高 | 腕點 | 食指相對頂面 | 手的朝向偏離 authored 抓握姿勢 |
> |---|---|---|---|
> | 2.05（authored） | −0.102 / −0.082 | **+0.002 m** | **11.0°** |
> | 1.68（下緣） | −0.102 / −0.082 | **−0.083 m** | **49.3°** |
>
> **腕點目標在每個高度完全相同 ⇒ 位置 solve 一直是對的，缺的是朝向。**
> `handIKRotationWeight` author 為 0 ⇒ 旋轉 IK 從未生效。兩骨 IK 為了搆到 32 cm 外的腕點
> 把前臂擺過去，手跟著轉到沒人決定過的角度 ⇒ 手指戳進頂面；升上來後轉回來 ⇒ 浮空。
> 這是 `gripEdgeInRoot` 那次修法的**旋轉版缺口**。
>
> ## 已做（留在 repo）
>
> `TraversalBakedAnchor` 新增 `left/rightHandRotationInRoot`；`TraversalContactDerivation` 一併採樣
> （與位置同一個 root yaw 框架）；已用 targeted `SerializedObject` 寫進 `Bake_Climb2m`
> （⚠️ **沒跑完整 re-bake**，`Apply()` 會重寫手動調過的欄位）。
> 舊資產反序列化為 `(0,0,0,0)`，以「模長≈1」判定未烘焙 ⇒ Vault1m／Climb1m 行為不變。
>
> ## ⛔ 為什麼沒啟用
>
> 把權重設 1 ＋ 用 `yaw × authoredRotation` 當目標，實測**更糟**：
> 三個高度的食指**全部**掉到頂面下方 **0.220 m**（連原本正確的 2.05 也壞）。
> 三個高度一模一樣 ⇒ 與牆高無關的固定錯誤 ⇒ **座標系錯了**。
>
> 根因：**烘焙的是 bone rotation，`Animator.SetIKRotation` 吃的是 goal rotation**，
> 兩者間有 avatar 決定的固定偏移（權重 0 的幀上量 `K = Inverse(bone) × GetIKRotation`，
> x 分量穩定在 ≈272°）。
>
> ⇒ **下一步：在 `TraversalHandIKRig` 做 bone→goal 空間轉換**（它同時看得到
> `GetIKRotation` 與骨骼，是唯一有資格做的地方），再把權重打開並重量手指。
> 在那之前權重維持 0、`TraversalPlanBuilder` **刻意不消費**新欄位——
> 半套接線比沒接線危險：把權重調上去就會重現上面那張表。
>
> ## 驗證
>
> EditMode 620（619 passed／0 failed／1 skipped）、PlayMode **67／67**，
> 手指數字已確認回到基準（2.05 食指 +0.002、1.68 −0.072）。
>
> ---

> # 🔴 2026-09-22 — **修掉前一輪造成的 root 過衝回歸**（使用者 Play 回報）
>
> 📖 正本：`docs/40` **§13**（§7 的結論已被它推翻）。
>
> ## 使用者回報
>
> 「原本能爬的最低高度爬不了了，且效果更差」＋兩張畫面（掛在邊緣姿勢扭曲／整個人對折趴在平台上）。
> **兩件事都是真的，而且都是前一輪造成的。**
>
> ## ① root 過衝（1.75 m 牆，ledge top 1.750）
>
> | n | 0.083 | 0.174 | 0.263 | **0.350** | 0.525 | 0.702 |
> |---|---|---|---|---|---|---|
> | rootY | 0.039 | 0.727 | 1.660 | **1.978** | 1.874 | 1.780 |
>
> **衝過落腳面 22.8 cm，再花約 1.3 s 沉回去。**
>
> **為什麼三個指標全綠**：往下沉是朝著平台，**沒有東西擋它** ⇒ `warpBlocked` ≈ 0；
> 手早就放開 ⇒ residual 乾淨；`rootDivergence` 量的是「root 與 plan 的距離」，而 plan 本身就要它衝上去。
> 📌 **「沒有指標變紅」≠「沒有問題」。**
>
> **根因**：Contact 的垂直修正有兩個成分——①**grip-driven**（手要搆到邊緣，抓握期間必須凍結）
> 與 ②**support-plane clamp**（接觸瞬間站在地上，不能把 root 解到地板下）。
> 前一輪讓**整份**凍結到 Transfer 之後，於是 ② 一路撐過邊緣才開始還，
> 而**動畫自己的垂直位移在 n≈0.35 就結束了** ⇒ 過衝。牆越矮 clamp 越大、越明顯
> （2.05 m 牆 clamp = 0，所以我在那裡完全量不到）。
>
> **修法**：`transferCorrection.y -= contactSupportPlaneClampDistance`。水平仍凍結。
> ⛔ 試過「垂直全部在 Transfer 前補完」——低牆好了，但 2.05 m 牆抓握期間多沉 6.1 cm，
> 滿權重手臂 ratio 0.866 → **0.976**，等於拿高牆的手換低牆的 root，否決。
>
> | 牆高 | root 超出落腳面 | 滿權重 ratio（contact／mid） | 滿權重手部殘差 |
> |---|---|---|---|
> | 2.05 | 4.3 cm | 0.720 ／ 0.946 | 0.00 ／ 0.09 cm |
> | 1.75 | 4.4 cm | 0.773 ／ 0.822 | 0.00 ／ 0.13 cm |
> | 1.68 | 3.8 cm | 0.832 ／ 0.802 | 0.00 ／ 0.05 cm |
>
> **超出量不再隨牆高變化**（一律 ~4 cm ＝ 站立 skin width）。異常不再隨參數縮放，才是修對的特徵。
>
> ## ② coverage 退回 1.65
>
> 前一輪我把 `coverageMinHeight` 1.65 → 1.72（理由：pre-IK 殘差距 guard 只剩 1 cm）。
> **使用者回報那個高度本來就在用**，且修好過衝後跑得乾淨 ⇒ **退回 1.65**。
>
> ⚠️ **保留的薄冰**：下緣（candidate 1.65）contact pre-IK 殘差 **33.8 cm** vs guard **0.35**，
> 餘裕 **1.2 cm**。觸發的話 IK 會整個掉到 0（斷裂，不是降級）。實測沒觸發。
> ⛔ 不要用放寬 guard 去換餘裕（`docs/39` F8）。正解是 `docs/39` Q6 改成 reach-ratio 計價。
>
> ## ③ 順帶修正的測試判準
>
> `TraversalClimbPlayModeTests` 原本斷言「Exit 之前不得有腳步聲」。**界線錯了。**
> 過衝修好後角色在 n≈0.51 就真的站上平台，右腳 n≈0.68 落地
> （`hasGround=True / phase=Planted / 水平速度 1.12 m/s`）＝**正確的腳步**，卻被 Exit(0.833) 判成缺陷。
> 改成「**不得在角色離地的那一幀出聲**」——缺陷的本體是「腳下沒有真地面卻出聲」，與時間無關。
>
> ## 驗證
>
> EditMode 620（619 passed／0 failed／1 skipped）、PlayMode **67／67**。
>
> ## ⛔ 仍未做
>
> 1. 🔴 **人工 Play 視覺驗收**（ADR-008 D10 轉 Accepted 的唯一缺口）。
> 2. **進場距離敏感度**：2.05 m 牆在理想進場 0.462 m 時滿權重 ratio **0.946**，逼近 budget 0.95；
>    走路進場 0.515 m 時只有 0.866。**這條敏感度沒有系統性量過。**
> 3. Foot IK 有相同的一幀延遲，本輪仍未動。
>
> ---

> # ✅ 2026-09-21（四）— **Climb2m 正式化完成**；`docs/39` 六個 exception 全數結案
>
> 📖 **正本在 `docs/40-climb2m-kinematic-budget.md`**（量測＋coverage 推導）與 `docs/30` §M（執行模型）。
> ADR-008 已加 **D10** 修訂紀錄（仍為 🟡 Trial——**只差人工 Play 的視覺驗收**）。
>
> ## 做了什麼
>
> | # | 改動 | 結果 |
> |---|---|---|
> | 1 | `Kind == Climb2m` 硬編 → authored `TraversalMotionBinding.executionModel`（`Absolute` / `Segmented`） | revert 路徑＝改一個資產欄位。Vault1m／Climb1m 維持 `Absolute`，行為未變 |
> | 2 | segment 數學改為 **committed plan ＋ 衰減的 carried error**（刪 `TryEvaluateBasis`） | `RootReachConstraint` 與 knot 內插原樣保留；compensation 從「牆高適應（公寸）」降為「執行誤差（公分）」 |
> | 3 | contact landmark 沿 **up** 夾在 entry 支撐面之上（`ClampOutsideWall` 的垂直同型，在 plan builder） | 低牆不再把膠囊解到地板下。夾持量以 `ContactSupportPlaneClampDistance` 可觀測 |
> | 4 | ⛔ **刪 Hand IK window override** | **這就是手臂 reach 破 1.0 的唯一根因**。ratio 1.055 → ≤ 0.87 |
> | 5 | 🆕 **手部 IK 一幀管線延遲補償** | 中點 post-IK 殘差 **6.77 cm → 0.60 cm**。這是「抓空氣」的真正來源 |
> | 6 | `ArmReachRatio` 改量**加權後的實際目標** | 舊式在 IK 已淡出時仍回報 1.18／1.23 的假破表 |
> | 7 | `coverageMinHeight` **1.65 → 1.72** | 在 1.65 時 contact 殘差距 IK guard 只剩 **1 cm**；1.72 有 **8 cm（23%）** |
> | 8 | 🆕 `TraversalClimbPlayModeTests`（真實 prefab ＋ `InputTestFixture` ＋ 真的按 W／Space） | 2.05／1.80 兩個高度，守 5 條不變量 |
>
> ## 驗證
>
> **EditMode 620（619 passed／0 failed／1 skipped）、PlayMode 67／67。**
> Play 實測（2.05／1.80／1.75／1.70／1.68 五個牆高）：IK 滿權重 arm ratio ≤ 0.87、手部殘差 ≤ 0.71 cm、
> warp blocked ≤ 0.36 cm、traversal 期間 root divergence ≤ 0.10 cm、Exit 前零腳步聲。
>
> ## 🟢 上一輪未結案的兩件，都結了
>
> - **② 1987 個 NRE** → **不是程式缺陷**。Play Mode 中的 domain reload 會把 `_runtimeData` 這類
>   非序列化欄位清成 null 且**不重跑 `Awake`**；已用 `EditorUtility.RequestScriptReload()` **主動重現**
>   （`[PROBE2] obj=X Bot runtimeData=NULL`，兩個角色逐幀）。`PROBE2` 已刪。
>   避免方式：Unity Preferences → **Script Changes While Playing → Recompile After Finished Playing**（使用者的偏好，我沒有替你改）。詳見 `docs/40` §12。
> - **③ 手沒固定住** → 由上表第 4、5 兩項結案。⚠️ 當時 WORKLOG 的推論（「IK window 與 anchor release 對齊」）
>   **方向對但理由錯**：不是 override 讓 root 自由移動，而是**動畫自己在 0.2407 之後就在推離邊緣**，
>   把 IK 釘到 transfer 等於跟動畫對抗。
>
> ## ⛔ 還沒做的（下一步）
>
> 1. 🔴 **人工 Play 視覺驗收**——手臂觀感、segment boundary 有無頓挫、contact 瞬間的手部修正是否刺眼（1.75 m 牆的 pre-IK 修正量是 27 cm）。**ADR-008 D10 要靠這一項才能從 Trial 轉 Accepted。**
> 2. entry distance 對 contact 殘差的敏感度**沒有量到**（量測腳本互搶角色，三次都失敗）——目前所有數字都在進場距離 ≈0.52 m 取得。
> 3. Foot IK **有相同的一幀延遲**，本輪刻意沒動。
>
> ---

> # ✅ 2026-09-21（三）— **Climb2m 假落地聲已修**；另有兩件未結案
>
> ## ① ✅ 假落地聲（已 commit）
>
> 使用者回報「Climb2m 爬上去一瞬間很多聲」。**實測後描述需要修正**：
>
> | 量測 | 結果 |
> |---|---|
> | `FootstepDetector` trace（ground truth） | 攀爬後腳步只擊發 **1 次**；跑向牆時 4 次（0.28 s 交替）＝正常跑步。**沒有腳步爆量** |
> | `JustLanded` 探針 | 一次攀爬出現 **2 次假落地**：`n=0.059`（正好抓握瞬間）、`n=0.535`（爬到一半） |
>
> 根因：爬牆時膠囊擦到牆面／邊緣讓 `isGrounded` 瞬間變 true，而
> `MotionDriver.cs` 的 `JustLanded` 是**無去抖動的純邊沿偵測** ⇒ 每擦一次就是一聲落地。
>
> 修法：`suppressGroundEdges = HasActiveTraversalPlan` ⇒ 只擋 `JustLanded` ／ `JustLeftGround`，
> **⛔ 不擋 `IsGrounded`**（那是 Foot IK／腳步偵測的閘門，也是 exit `reboundForce` 貼地刻意要達成的效果）。
> `_wasGrounded` 照常前進——「不回報」與「沒發生」是兩件事，與 `FootstepDetector` 的 `Airborne` 抑制同紀律。
>
> ⚠️ **未重現到「一瞬間很多聲」**：兩次假落地間隔 1.7 s。可能還有未量到的來源，**不敢說完全結案**。
>
> ## ② 🟡 787（後續累積到 1987）個 NullReferenceException —— **未解，探針已武裝**
>
> ```
> CharacterPipelineRunner.LateUpdate () at CharacterPipelineRunner.cs:525
>   → _runtimeData.ResetTransientState();
> ```
>
> **時間軸證明它與攀爬無關**：全部發生在 12:24:49–12:24:59（上一場 Play），
> 而使用者那次攀爬的 Play 在 12:25:06 才開始。我自己跑乾淨 Play：**0 次**。
>
> 🔴 **尚未解開的矛盾**：`_runtimeData` 在 `Awake` 無條件指派、無早退、無 Awake 例外；
> `ResetTransientState()` 與 `IntentData.Reset()` 都只碰值型別。兩邊照理都炸不了。
> 場景有 **2 個 `CharacterPipelineRunner`**（X Bot／Y Bot），另有一行
> `[TraversalState] StateMachineConfig 沒有綁定 TraversalStateParamsSO` 警告。
>
> ⚠️ **`CharacterPipelineRunner.cs` 目前帶著未 commit 的 `PROBE2`**（try/catch，會印出
> `obj=<GameObject 名稱>` 與真實內層堆疊）。**下次它再出現就會自己報上名來。**
>
> ## ③ 🔍 手沒固定住 —— 根因已定位，建議獨立一包
>
> **先確認度量同源**：baker 的 `armLength = Distance(upper,lower) + Distance(lower,hand)`
> 與 runtime instrumentation **完全相同** ⇒ 「1.055 vs budget 0.95」可比較，結論成立。
>
> | t | arm reach ratio | post-IK 殘差 |
> |---|---|---|
> | 0.065 Contact | 0.701 | 0.19 cm |
> | 0.169 midpoint | 0.811 | 2.27 cm |
> | 0.267 Transfer−1 | **1.044** | 2.71 cm |
> | 0.270 Transfer | **1.055** | 3.33 cm |
>
> 比值跨過 0.95 的時刻落在 0.169–0.267 之間，而
> `Bake_Climb2m.asset`：`anchorReleaseNormalizedTime = 0.2130`、`transferNormalizedTime = 0.2685`。
>
> **推論（強，未證明）**：reach constraint 的權威在 `anchorRelease 0.213` 交還（動畫自己放開的時刻），
> 而 **prototype exception #5 的 IK window override 把手一路釘到 Transfer 0.2685**。
> 那段差距裡 root 自由移動、IK 還在硬拉 ⇒ 手臂打直仍搆不到 ⇒ 殘差長到 3.33 cm。
>
> ⚠️ **真正的張力**：當初加 override 正是因為「IK 提前歸零 ⇒ late-contact drift」。
> 兩個方向都有代價，正解應是**讓 IK window 與 anchor release 對齊**（`docs/39` Q5「authored IK window」那一包）。
> ⛔ 本輪未動——它要同時處理 exception #5 與 reach budget，不該在雜項串裡順手改。

> # ✅ 2026-09-21（二）— **作品集面板改用 TextMeshPro**（使用者裁決）
>
> ## 為什麼最後還是導入 TMP
>
> 第一輪只調參數（`fontSize` 17→22、`dynamicPixelsPerUnit` 1→3）**不夠**。
> 對比不是問題（底 `α 0.72` 深色、字近白 `{0.92,0.95,1}`），問題純粹是 rasterize 銳利度：
> legacy `UI.Text` **先用 fontSize 烘成點陣、再乘 canvas scaleFactor 縮放** ⇒ 任何縮放都是重採樣。
> 實測 Game view 783×440 ⇒ `scaleFactor 0.4077` ⇒ 22 × 0.4077 ≈ **9 px**。
> TMP 是 **SDF**，在任何縮放下都銳利 —— 這是唯一的根治。
>
> ## 做了什麼
>
> | 步驟 | 內容 |
> |---|---|
> | 1 | `AssetDatabase.ImportPackage` 匯入 `TMP Essential Resources`（來自 `com.unity.ugui` 套件內），新增 `Assets/TextMesh Pro/`（Fonts／Resources／Shaders，含 `LiberationSans SDF`） |
> | 2 | `Project.Runtime.asmdef` ＋ `Project.Tests.EditMode.asmdef` 加 `Unity.TextMeshPro` 參考 |
> | 3 | `RuntimeDebugPanel` 四個欄位 `UnityEngine.UI.Text` → `TMPro.TextMeshProUGUI` |
> | 4 | **prefab asset 層**（`LoadPrefabContents`／`SaveAsPrefabAsset`，不是 instance override）換掉四個元件並重接引用；保留 text／color／fontSize 22，`alignment = Left`、`raycastTarget = false` |
> | 5 | `RuntimeDebugPanelTests` 改用 TMP 型別 |
> | 6 | `dynamicPixelsPerUnit` **3 → 1 還原** |
>
> ### 為什麼把 dynPPU 還原
>
> 它只影響**動態點陣字型**（legacy Text）。實測全場景 canvas 已**零個** legacy Text
> （`HealthBar: 0 / PlayerHud: 0`）⇒ 這個值現在完全失效。
> ⛔ 不留「理由已死的數字」——那正是本專案反覆踩到的那類東西。
>
> ## 🔒 順手補上的不變量（這是我自己開的門）
>
> `ArchitectureRegressionTests` 既有規則：**Core 與 Presentation 不得碰 `UnityEngine.UI`**，
> 理由是 asmdef 參考屬**組件層級**，一加就讓整個 Runtime 編得過 uGUI ⇒ 「加參考的同一刻就把門關上」。
>
> **加 `Unity.TextMeshPro` 等於把同一扇門又開一次** ⇒ 已把 `"TMPro"` 一併加入
> `LayerRules` 的 Presentation 與 Core 兩條 Forbidden 清單。
> HUD 屬 App 層；⛔ 不得讓表現層或 gameplay 層開始碰 TMP。
>
> ## 驗證與還原
>
> 驗證：四個欄位皆指向 `TextMeshProUGUI`／font `LiberationSans SDF`／size 22，
> 面板底下 legacy Text **0 個**，場景 instance 引用未斷。
> 還原：`git checkout -- "Assets/Prefabs/X Bot.prefab"` ＋ 刪除 `Assets/TextMesh Pro/` ＋ 回退 asmdef 與兩個 .cs。
>
> ⚠️ **尚未人工看過實際畫面** —— 字級 22 在 TMP 下的視覺大小與 legacy 不完全相同，可能還要微調。
>
> ---
>
> # ✅ 2026-09-21 — **Debug 一鍵開關（F6）＋ 作品集面板文字**
>
> ## F6 ＝ 全部可視化一鍵開／關
>
> ```
> `      工程面板開關（不變）
> F1     作品集面板 Basic/Off（不變）
> F2–F5  個別頻道（不變，仍需面板開著）
> F6     🆕 全部可視化一鍵開／關 —— 不需要開面板
> ```
>
> 放在 `_engineeringOpen` 閘門**之前**，與 backquote 同層級：backquote 開的是**面板**、
> F6 開的是**畫面上的可視化**，兩者互不依賴。
>
> ⭐ 語意刻意**不是**「記一個 master bool 再翻它」——那在使用者用 F2–F5 個別調過之後會與畫面脫節。
> 改成由**當下實際狀態**推導：**任一頻道開著 ⇒ 全部關；全部關著 ⇒ 全部開**。
> 不論先前怎麼調，按一下的結果都可預測。涵蓋 Capsule Offset／Foot IK／Traversal 線＋Hand Targets／Scene Gizmos。
> F6 本來就空著（2026-09-15 拔掉文字頻道後無擁有者），無衝突。
>
> ## 作品集面板文字：「小」與「糊」是兩個不同的原因
>
> 量測（Play 中讀實際值）：
>
> ```
> CanvasScaler ScaleWithScreenSize，參考 1920×1080
> 實際 Game view 783×440 ⇒ scaleFactor 0.4077
> Text: LegacyRuntime, fontSize 17   ⇒  17 × 0.4077 ≈ 6.9 px
> ```
>
> legacy `UI.Text` 是**先用 fontSize 17 烘成點陣、再縮到 0.41** ⇒ 降採樣 ⇒ 不是「銳利的小字」而是糊字。
>
> | 症狀 | 根因 | 修法 |
> |---|---|---|
> | **糊** | `dynamicPixelsPerUnit = 1` ⇒ 動態字型以 ~7 px rasterize | **1 → 3** |
> | **小** | fontSize 17 | **17 → 22**；`LayoutElement.preferredHeight` 22 → 28；面板高 132 → **148**（＝4×28 ＋ spacing 4×3 ＋ padding 24，算出來的不是猜的） |
>
> ⛔ **未導入 TextMeshPro**：Unity 6 雖隨 `com.unity.ugui` 附帶，但本專案零使用、asmdef 未引用、
> 也沒匯入 TMP Essentials 字型資產。為四行字導入整套 TMP 不划算。
> 🔓 日後 HUD 文字變多時才值得——SDF 在任何縮放下都銳利，那才是根治。
>
> ## ⚠️ 資產改動（無測試保護）
>
> `PortfolioDebugPanel` 屬於 **`X Bot.prefab`**，不是純場景物件 ⇒ 直接改會變成場景 instance override
> （prefab 仍是舊值，重新 instantiate 就打回原形）。
> 已用**逐屬性** `PrefabUtility.ApplyPropertyOverride` 推回 prefab（只推動過的 10 個屬性，
> ⛔ **沒有**用 `ApplyPrefabInstance` 把其他不相干的 override 一起掃進去）。prefab ＋ 場景已存檔。
>
> 還原：`git checkout -- "Assets/Prefabs/X Bot.prefab" Assets/Scenes/SampleScene.unity`
> ⚠️ 但這兩個檔**本來就有其他未 commit 的改動**，還原會一起丟掉。
>
> ## 📌 這個子系統第三次「可觀測性說了謊」
>
> ①`blocked displacement` 被 post-exit 貼地力污染（三個高度同一個數字）
> ②`arm ratio max` 在 contact window 外累加（回報 2.8，實際 1.07）
> ③`NoForwardHit` 把「掃描提前放棄」說成「前方沒東西」
>
> **三次都不是「沒有可觀測性」，而是「可觀測性說了謊」。**
> ⇒ 加 reject reason／量測欄位時要問的是「**這個值在所有情況下都是真的嗎**」，
> 不是「有沒有這個值」。

> # 🟠 2026-09-20（三）— **Animation Coverage 上下限 ＋ Climb1m 啟用**（程式已落地，待人工 Play）
>
> ## 做了什麼
>
> **Climb1m 不需要任何新執行路徑。** 它沒有 `Traversal` block ⇒ `TryBuild` piecewise 失敗
> ⇒ `TraversalState` 既有的 **EndpointFallback（V2）** 自動接手；Hand IK 因 `!IsPiecewise`
> 也自動關閉。轉場資產 `Animation/Climb1m.asset` 與 binding 早就接好了。⇒ **只缺覆蓋範圍與一個 bool。**
>
> | 改動 | 檔案 |
> |---|---|
> | `TraversalMotionBinding` 新增 `coverageMinHeight` ／ `coverageMaxHeight`（0 ＝ 不限制） | `TraversalStateParamsSO.cs` |
> | 新 reason `OutsideAnimationCoverage` ＋ 六參數 `Evaluate` 多載（舊四參數多載保留） | `TraversalSelectionPolicy.cs` |
> | 決策邊界取 binding 覆蓋值並傳入；覆蓋帶單向推給 Probe | `States/TraversalState.cs` |
> | Debug 環改畫**四個覆蓋界線**（藍＝Climb1m、橘＝Climb2m；小半徑＝下限、大半徑＝上限） | `TraversalProbe.cs` |
> | `S7` ／ `S8` 覆蓋閘門測試（含閉區間邊界） | `TraversalSelectionPolicyTests.cs` |
>
> ## 🔑 為什麼上下限放在 binding 而不是 `TraversalProbeSettings`
>
> 這是**這支 clip** 的性質，不是環境幾何的性質。Probe 回答「障礙物多高」；binding 回答
> 「我這支動畫撐得住多高」。放進 ProbeSettings 會讓 Core/Environment 開始擁有內容決策，撞 §F **I10**。
> Debug 環沿用 **I9 的 `SetDerivedRangeRequirements` 先例**：推四個 float 過去，Probe 不認識 binding。
> ⛔ 推過去的值**不參與判定**，只決定畫哪幾個環——畫面是決策的投影，不是第二份真相。
>
> 閘門位置在 `KindDisabled` **之後**、jump-vs-climb 仲裁**之前**：兩者同屬內容決策。
> 覆蓋範圍外 ⇒ 讓給普通 Jump ⇒ Jump 也搆不到就什麼都不發生。**那是已知限制，不是 bug，而且有 reason 可查。**
>
> ## 📐 已 author 的值（`TraversalStateParams.asset`）
>
> | | min | max | 依據 |
> |---|---|---|---|
> | **Climb1m** | **1.00** | 1.20 | 下限由 **apex** 決定（見下方修正）；上限 ⚠️ **推導起點，零 Play 驗證**（clip 升 1.0134 m） |
> | **Climb2m** | **1.65** | 2.05 | ✅ **實測推導**：pre-IK 殘差隨牆高 1:1，guard 0.35 m 的交叉點 ≈1.62 m，取 1.65 留餘裕 |
>
> `climb1mEnabled: false → true`。
>
> ## ⚠️ 誠實標記
>
> - **Climb1m 的 0.90／1.20 沒有任何 Play 證據**，只是「clip 升 1.01 m」的合理起點。第一次 Play 就可能要調。
> - 兩段覆蓋之間仍有 **1.20 – 1.65 m 的缺口**（先前是 0.80–1.62）。缺口沒有消失，只是**從「硬爬但難看」變成「明確拒絕且說得出理由」**。
> - 資產改動（覆蓋值 ＋ enable）**只有 Unity 匯入成功，沒有測試保護**。還原：`git checkout -- Assets/ScriptableObjects/StateMachine/TraversalStateParams.asset`。
>
> ## 🔧 修正：`climb1m` 下限 0.90 → **1.00**（「有時 jump 有時 climb」的根因）
>
> live 實測（`TraversalSelectionEvaluation`）：**`TheoreticalApex = 0.9535` ／ `SafeReach = 0.8035`**。
> policy 判 `height > SafeReach → climb`，所以 **0.8035–0.9535 是模糊帶**：
> 跳躍**物理上搆得到**，但 policy 判給 climb；climb 還要過 entry legality，過不了又退回 jump
> ⇒ **同一面牆、站位差一點就換行為**。原本設的 0.90 正好落在帶內。
>
> ⚠️ 先前把「下限比 jump 高度要高」理解成 `SafeReach`，**正確的基準是 apex**。
> 新的高度地圖（可一句話講完，適合作品集敘事）：
>
> ```
> ≤0.95 jump ／ 0.95–1.00 死區(4.6cm) ／ 1.00–1.20 Climb1m
> ／ 1.20–1.65 ⚠已知缺口 ／ 1.65–2.05 Climb2m ／ >2.05 無
> ```
>
> ## 🐛 簍空結構攀爬不了 —— **已定案並修復**（現場量測，非推測）
>
> ⚠️ 本檔稍早列的 A／B／C 三個假設（橫向缺口／縱向缺口終止掃描／corridor 擋路）**全部不成立**，已刪除。
>
> ### 現場量測（Play 中以相同參數重打射線）
>
> ```
> off  0.000 | hits=4   1.40:Cube (2)@0.34  1.50  1.60  1.70
> off ±0.175 | hits=4   同一個 collider、距離一致
> off ±0.350 | hits=4   同上
> ```
>
> 橫向 ±0.35 m 結果完全一致 ⇒ **不是橫向缺口**。
> `Cube (2)` ＝ bounds **y ∈ [1.425, 1.820]**、size 4.77 × **0.40** × 3.26 的**懸空厚板，底下全空**。
> 只有 1.40–1.70 四根命中，**0.30–1.30 全部落空**。
>
> ### 根因：`break` 同時表達了兩件不同的事
>
> `TraversalProbe.cs` 前掃迴圈：
>
> | 狀態 | 應該 | 舊版實際 |
> |---|---|---|
> | 打到過牆、現在沒打到 ⇒ **牆面到頂** | 記 `firstClearHeight` 後 `break` | `break` ✅ |
> | **還沒打到任何東西** ⇒ 障礙物還在上面 | 繼續往上掃 | **也 `break`** ❌ |
>
> 第一根射線在 `stepOffset = 0.30 m`，那裡是空的 ⇒ 直接 break ⇒ 迴圈**爬不到 1.40**
> ⇒ `NoForwardHit`、height 0.00。
>
> ⇒ 掃描把前提寫死成「障礙物是一面從地面長上來的實心牆」。
> **任何懸空板／桌面／欄杆／突出的簷都因此隱形**——比簍空結構廣得多。**這是 bug，不是限制。**
>
> ### 修法
>
> `if (!hasForwardHit) continue;` —— 一行。
> ✅ **對既有內容零行為差異**（實心牆第一根必命中，走原路徑）。
> ⚠️ **代價**：面向空曠處時由 1 根射線變成掃滿 ceiling（0.3→2.7，24 根）。命中後成本不變。
> 若日後量到這是熱點，正解是先用一次 BoxCast／CapsuleCast 涵蓋整個柱體當 early-out，**本輪刻意不做**
> （同時改兩件事會無法歸因）。
>
> ### 這塊板修完會變成什麼
>
> 板頂 1.820 − capsuleBottom 0.030 ＝ **height 1.79 m** ⇒ 落在 Climb2m band (1.25, 2.1] 且在
> coverage [1.65, 2.05] 內 ⇒ 走 Climb2m。**1.79 m 正好是先前 Play 驗過品質良好的高度**
> （pre-IK 18.12 cm、IK 權重 1.00）。
>
> ### 測試
>
> `TraversalProbePlayModeTests`：
> `FloatingSlabWithOpenSpaceBelow_IsStillDetected`（守不變量）＋
> `SolidWall_ScanBehaviourIsUnchangedByFloatingSlabFix`（守零回歸）。
>
> ---
>
> # ✅ 2026-09-20（二）— **Traversal Clip Coverage Spike：已結案，零 Play**
>
> ## 🔴 結論：Climb1m **不能**當低段 clip —— 它整段沒有手部接觸
>
> `TraversalContactDerivation.Measure(Bake_Climb1m, X Bot)` 回：
> **「測不到任何手部接觸（整段動畫沒有『先動再停』的手）」**，`IsValid = false`。
>
> **陽性對照**（同一次呼叫，證明偵測器是好的，不是門檻問題）：
>
> | Bake | valid | report |
> |---|---|---|
> | `Bake_Climb1m` | ❌ | 測不到任何手部接觸 |
> | `Bake_Climb2m` | ✅ | `BothHands  L=0.065-0.269  R=0.065-0.259  transfer=0.269  exit=0.833` |
> | `Bake_Vault1m` | ✅ | `LeftHand  L=0.207-0.253  transfer=0.253  exit=0.460` |
>
> ⇒ Climb1m **無法產生 contact plan**，不能參與 contact／Transfer／Hand-IK 任何一層。
> ⇒ **方案 (a)「clip coverage 分工」以現有資產不成立。** 答案是 **(b)**。
> ⇒ 順帶驗證：Climb2m 的 `contact 0.0648 / transfer 0.2685` **就是**量到的 plant 起訖，烘焙鏈路正確。
>
> ## 📐 Climb2m 的 validated coverage（由數據推導，非猜測）
>
> `pre-IK residual` 隨牆高以 **1:1** 下降（實測 1.79 m → 18.12 cm、1.58 m → 39.47 cm）。
> guard 門檻 0.35 m ⇒ 交叉點 `1.79 − (0.35 − 0.1812) = ` **≈ 1.62 m**。
>
> | 區間 | 狀態 |
> |---|---|
> | **1.8 – 2.05 m** | ✅ 已驗證良好（2.0 最佳，僅極小瑕疵） |
> | **1.62 – 1.8 m** | 🟡 可用但 IK 拉伸遞增，無跳動 |
> | **< 1.62 m** | ❌ guard 跳閘 ⇒ contact 當幀手不受控（「抓空氣」）再瞬移 |
>
> ## ⚠️ 因此暴露的真實缺口
>
> classifier band 是 **(1.25, 2.1]**，normal jump safeReach **0.8035 m**
> ⇒ **0.80 – 1.62 m 這一段沒有任何合適的動畫**（band 下緣 0.37 m 在 coverage 外）。
> ⛔ 本輪**未**改 band（使用者明確要求先不切）。
>
> ## ➡️ 待裁決（三選一，都不急）
>
> ① 接受並寫進作品集敘事（使用者原本的傾向）
> ② 找一支**有手部接觸**的 1–1.5 m 攀爬 clip（記憶指出 Kubold 素材庫可能有）
> ③ 讓 Climb1m 走**無手部接觸**的 V2 endpoint 路徑（不進 piecewise、不掛 Hand IK）——**新提案，未評估**
>
> ---
>
> # ⏹ 2026-09-20（二）— 原工作包（已被上方結果取代）
>
> **這是 spike，不是實作。成功條件不是「所有高度都能爬」**，而是：
> 支援範圍內穩定 ／ 超出範圍有明確可解釋的限制 ／ **不靠極端 IK 或 warp 把不適合的動畫硬塞進去**。
> 產出是一張 `Height | Climb1m quality | Climb2m quality | reason` 決策表，**不預先切 band**。
>
> ## 🔴 為什麼上一個提案（support-aware Y clamp）被撤回
>
> `Bake_Climb2m.asset:4980` → `leftGripEdgeInRoot.y = 1.9176`（primary hand）。
> ⇒ `desiredContactRoot.y ≈ ledgeY − 1.9176`。對照三次 Play 的 `dY applied`
> （2.02→+0.05、1.79→−0.19、1.58→−0.40）：**高度每降 0.21 m，dY 就降 0.21 m，斜率 1:1**。
> ⇒ **ledge < ~1.98 m 時 grip solve 一律落在地板以下**，而 Climb2m band 是 (1.25, 2.1]
> ⇒ `max(solvedY, floor)` 在 band 的 **86%** 飽和成 floor ＝ 現行 baked-delta policy 的結果
> ⇒ **39 cm 殘差不會有任何改善**。病根是 **pose 不匹配**（動畫 grip 在 root 上方 1.92 m），
> 不是 root 放錯位置，任何 root 平移都補不了。
>
> ## 📌 已查明的起點（Codex 不必重查）
>
> | 事實 | 出處 |
> |---|---|
> | `Bake_Climb1m.asset` **完全沒有 `Traversal:` 區塊**（頂層鍵只到 `BakedDuration`） | 該資產 |
> | Climb1m **binding 早已接好** —— bake 引用／warp 窗 0.8／recover 0.85／corrections 1.5・0.75 | `TraversalStateParams.asset:58-71` |
> | `climb1mEnabled: 0` | 同上 `:154` |
> | Climb1m 垂直位移 **1.0134 m**、`BakedDuration 2.1667`、`AutoApexHeight 1.172` | `docs/30` 附錄 B ＋ 資產 |
> | 烘焙入口 `TraversalContactDerivation.Apply(bake, rigPrefab, out report)` | `Assets/Scripts/Editor/Stages/` |
>
> ## 🎯 本輪的核心假說（要被證實或推翻）
>
> Climb1m 只升 1.01 m ⇒ grip edge 高度約 1.0–1.1 m ⇒ **適用帶約 0.95–1.15 m**；
> 在 1.3 m 以上它會往**反方向**壞（root 被抬離地面 ＝ 浮空），不是變矮。
> ⇒ **預測真正的 gap 是 ~1.15–1.9 m，兩支 clip 都不適合。**
> 若成立，那就是答案 —— 使用者已明確接受「幾何上可攀爬 ≠ 當前 clip 一定適用」作為正式限制。
>
> ## ⚠️ 我改了測試高度清單（並已說明理由）
>
> 原清單 1.3–2.0 **對 Climb1m 不公平** —— 它的甜蜜點很可能在 1.3 以下，只測 ≥1.3 會得到
> 「Climb1m 到處都爛」這個錯誤結論。**加測 1.0 / 1.15 / 1.2。**
> 並建議先跑 8 次夾出兩條邊界（Climb1m: 1.0・1.15・1.3・1.5；Climb2m: 1.5・1.7・1.9・2.0），
> 而不是 2 passes × 10 heights ＝ 20 次人工 Play。
>
> ## ⛔ 本輪不做
>
> contact-time scrubbing（方案 B，留作未來且**僅在仍有明顯 gap 時**才值得）／改 baker 本身／
> 永久改 selection band 或 classifier／調 residual guard／動 execution model／commit。
>
> ## 🔧 路由用的暫時資產改動（必須還原）
>
> pass 1 把 `climb1mMaxHeight` 設為 2.15 ＋ `climb1mEnabled: 1`（全部路由到 Climb1m）；
> pass 2 還原。**還原值：`climb1mMaxHeight 1.25` ／ `climb2mMaxHeight 2.1` ／ `climb1mEnabled 0`。**
> 障礙 depth 必須 > `vault1mMaxDepth 0.6`，否則低段會被 Vault1m 接走。
>
> ---
>
> # 🟠 2026-09-20 — **工作包：Climb2m segment-warp 驗證輪（A–D）**
>
> ## 📌 程式已落地，⏳ **卡在人工 Play**（2026-09-20 實測驗證）
>
> | 項 | 狀態 |
> |---|---|
> | **A** blocked 量測分離 | ✅ 已落地 —— `LastTraversalMaxWarpBlockedDisplacement` ／ `…MaxReboundBlockedDisplacement` |
> | **B** reach constraint 接回 | ✅ 已落地 —— 新增 `TraversalSegmentWarpPrototypeSegment.TryEvaluateBasis`，**兩個呼叫點都用它** |
> | **C** 刪 0.5 guard | ⚠️ **已刪，但 precondition 未驗** —— 見下方風險 |
> | **D** window override 保留 | ✅ 未動（`EvaluateClimb2mPrototypeWindow` 原樣） |
>
> **測試（本會話實跑，非 Codex 回報）**：EditMode **608 total／607 passed／0 failed／1 skipped**；
> PlayMode **62／62 passed**。`unity cmd recompile` 0 errors。
> ⚠️ 第一次跑出 607 是**舊組件**——`run_tests` 前必須先 `recompile` ＋ poll `recompile_status`。
>
> ## 🔴 唯一的風險：C 的 precondition 沒被驗證就執行了
>
> 工作包寫的是「**先用 0.35 重跑 2.0／1.8／1.6，全過才刪**」。Codex 的沙箱連不到 Editor，
> **沒跑那一步就直接刪了 override**。⇒ 目前工作樹處於 C 的**樂觀分支**。
> 🎯 好處是這變成一個乾淨的單變因實驗：**1.6 m 的 Play 就是判決**。
> 若 1.6 m 掉 IK（`docs/39` F8 預測過），revert ＝ 把 `0.5f` 的 `Mathf.Max` 與 root-error 閘門加回
> `TraversalHandIKController.cs`，一處。
>
> ## ⚠️ A 的已知近似（不是 bug，但要記著）
>
> **prototype path 的拆分是精確的**（rebound 是獨立 Move）；
> **production path 是投影推估**——rebound 併在同一個 `movementDelta` 裡，只能把 blocked 向量
> 投影到 rebound 方向再夾持。同一帧若 warp 本身也被向下擋住，那部分會被**誤記到 rebound**
> ⇒ production post-exit 幀的 warp blocked 會**低報**。本輪量測目標不在那裡，但別讓它變成下一個 F1。
>
> ---
>
> ## 原始工作包（發給 Codex 的內容）
>
> **正本 ＝ `docs/39-traversal-segment-warp-review.md`。** 本輪**不是**重構、**不是**正式升格，
> 目標是「**移除主要 hack 之後，prototype 的改善是否仍成立**」。
>
> ## 使用者人工觀察（本輪的真正驗收基準）
>
> | 高度 | 現況 |
> |---|---|
> | **2.0 m** | prototype 比原版多一點**很小**的瑕疵，不容易察覺 |
> | **1.6 m** | 比原版**好很多**，方向正確，但**仍看得到較大偏差** |
>
> ⇒ **本輪成功 ＝ 降低 1.6 m 的 visible drift ＋ 不明顯惡化 2.0 m。⛔ 不追求零誤差。**
>
> ## 只做四件事
>
> | | 內容 | 對應 finding |
> |---|---|---|
> | **A** | 分離 warp 與 rebound 的 blocked 記錄（`⛔ 不改 movement 行為`） | `docs/39` F1 |
> | **B** | 把 `RootReachConstraint` 接回 segment path（⛔ 不重新設計、不調 curve、不改 `maximumReachRatio`） | `docs/39` F2 |
> | **C** | B 完成後用**正式** guard `0.35` 重跑；過了就刪 `0.5` override，沒過**只記錄原始數據、不設計新門檻** | `docs/39` F8／Q6 |
> | **D** | **保留** IK window runtime override 不動（避免三個變因同時改） | `docs/39` Q5 留下一包 |
>
> ## ⛔ 本輪不做
>
> Y support clamp／`TransferRootTarget`／`executionModel` config／改 ADR 或 docs invariant／
> 動 Climb1m・Vault1m／重 author bake／調動畫／新增 coverage gate／**commit**。
>
> ## 重測矩陣（同場景同 rig：2.0／1.8／1.6）
>
> 每組七項：pre-IK hand residual、post-IK hand residual、max arm reach ratio、max root divergence、
> **true** warp blocked displacement、segment boundary velocity delta、completion。
> 取樣點：`Contact` ／ `Contact midpoint` ／ `Transfer − 1 frame` ／ `Transfer`。
>
> ⚠️ **真實 rig 的數字只有人工 Play 拿得到**——Codex 交付的是**可信的量測管道**與 Play checklist，
> ⛔ 不得自行編造矩陣數值。人工要看的四件事：2.0 m 是否更差／1.6 m 肩肘是否仍被拉長／
> Contact→Transfer 是否滑手／Transfer boundary 有無速度跳變。
>
> ## 回報七項
>
> ①改了哪些檔 ②reach constraint 如何接回 ③2.0／1.8／1.6 新舊對照 ④`0.5` 能否移除
> ⑤1.6 m 剩餘 visible error 主因 ⑥2.0 m 新瑕疵屬 compensation／IK／reach 哪一類
> ⑦下一包選 **Y support policy ／ authored IK window ／ TransferRootTarget** 之一
>

> # ✅ 2026-09-19 — **Portfolio Runtime Debug UI ＋ Traversal L/R Hand Target 可視化**
>
> - `RuntimeDebugPanel` 新增 **F1 = Basic / Off** 的 uGUI 作品集面板（右上，4 行）：
>   `State` 只讀 `CharacterPipelineRunner.CurrentState`；`Anim State` 只讀
>   `AnimancerFacade` 已成功接受的 key；`Clip` 只讀該 state／mixer 當下 dominant leaf clip；
>   `Gait` 只讀 `PlayerRuntimeData.Gait`。缺資料顯示 `—`，不由字串反推 gameplay。
> - 工程用 IMGUI 面板改為 **backquote (`)** 開關；F2–F5 不變。F4 現在同時開關
>   traversal probe 線與 hand targets。
> - `TraversalHandIKController` 新增 Game View runtime-visible 雙手可視化：
>   actual L/R hand bone → committed L/R target 線、target ring、rotation forward 短線，以及
>   `L Hand Target` / `R Hand Target` world-space label。只讀
>   `MotionDriver.ActiveTraversalPlan.ContactTargets`，不重查 Physics、不重算 placement、不改 IK/root/collider。
> - 現況確認：L/R target **本來就是獨立 committed data**；舊的中心球／共通點是
>   `candidate.TopPoint` shared ledge anchor，不是 Hand IK 唯一目標；保留作環境參考，
>   但球半徑已由 0.11 m 降為 0.04 m，低於具名 L/R target 的視覺優先級。
> - 驗證：Unity compile 0 errors；EditMode **606 passed / 0 failed / 1 existing skipped**；
>   PlayMode **61 / 61 passed**。Game View 實測 Idle =
>   `Idle / Idle / Idle / Run`，Walk 移動 = `Move / Move / WalkFwdLoop / Walk`；
>   Basic→Off 後 panel inactive，State 切換前後均為 Idle。
> - 尚待人眼 Play：Walk/Run Stop 短暫選片、Jump→Land 過場的文字可讀性，
>   以及 traversal 真正 Active 時 L/R marker 的視覺尺寸。這三項屬短暫時序／畫面品質，
>   不是資料流或接線未完成。
>

> # ✅ 2026-09-18 — **Subsystem Architecture 第二批完成**（Footstep＋Surface／Presentation Audio）
>
> 兩頁已寫入 `docs/artifacts/` 並發布；`architecture-tour` 路由表與 `docs/00-map.md` 已同步。
> commit `43c8cc5`（第一批 Codex 六輪複審修正）＋ `d0a7441`（第二批）。
> ⚠️ **本輪只動文件**——`Assets/Scripts` 改動 0、資產改動 0。
>
> ## 📌 過程中查到、與 `docs/` 不符的五件事（頁面內已逐條標記，**不是 bug**）
>
> | 發現 | 現況 | 影響 |
> |---|---|---|
> | **`docs/37` §12.1 的唯一 audio 待辦已經完成** | 13 顆 Combat 資產的 12 個 clip 參考**逐一對得上存在的 `.meta`**，沒有空槽也沒有死 GUID | ⇒ **不需要再跑 `WireClips.cs`**（見下方已劃掉的交辦） |
> | `FootstepKind.Shuffle` **已無任何 runtime producer** | `Advance` 只回 `Normal`／`None`；Bank 四個材質的 Shuffle 槽全空；`shuffleVolume` ＝ 0 | 保留 enum 是對的（查表以 `(int)` 為索引），但**它不是還在運作的功能** |
> | `AudioEventId` 0–3（Landing／Left／Right／Shuffle）**已無 runtime 呼叫端** | 腳步與落地全部改走 `FootstepAudioBankSO`；三顆 Library 資產也只登記 4／5／6 | 同上，化石層 |
> | `docs/37` §4.2「Combat 不依賴 Footstep 由 A4 白名單維持」 | **事實成立（逐檔驗過），但沒有測試守**——A4 的 `Presentation` 規則整層遞迴且放行 `Project.Presentation` 前綴 | 想要真的守住，得為 `Presentation/Actions` 另開一條 LayerRule |
> | `FootGroundContact.Collider` 的契約 vs `SurfaceResolver` | 契約寫「讀取方一律**只讀不存**，`Collider` 尤其不得留存」，而 `_cachedCollider` 跨幀持有它（僅用於 `ReferenceEquals`）；同段註解「本欄位目前沒有任何 runtime 讀取方」已過時 | 兩者只能有一個是對的，**目前沒有測試在守** |
>
> ⛔ 以上五條**都沒有動程式**。前三條是誠實記錄，後兩條若要收，是各自獨立的小工作包。
>
> ## ➡️ 優先事項不變
>
> 回到 gameplay ／ combat ／ animation ／ VFX。⛔ 不要因為這兩頁而重啟 audio（見下方結案交辦）。


> # ⏹ 2026-09-17（五）— **平台邊緣 grounding：`Deferred / Accepted Limitation`**（使用者明確裁決）
>
> ## ⛔ 下一個會話：**不要再碰這個問題**
>
> **正式行為已退回：`data.IsGrounded` ＝ `CharacterController.isGrounded`，沒有任何額外 support 規則。**
> 極端平台邊緣時**允許膠囊側面被平台角支撐、允許部分腳掌／腳尖懸空，不強迫掉落**。
> ⭐ 這是**已知且被接受的限制，不是待修的 bug**。
>
> | 已撤回的東西 | 狀態 |
> |---|---|
> | Unreal 式 smaller support radius 否決層 | ⛔ 程式已刪（`StandingSupportProbe.cs`／4 個 `[SerializeField]`／prefab 欄位） |
> | footprint／multi-point support | ⛔ 從未接線，也不會接 |
> | Temporary Collision Release | ⛔ 只做過 throwaway spike，已刪 |
> | outward auto-push／edge slip | ⛔ 只做過 throwaway spike，已刪 |
> | `StandingSupportTests`（PlayMode ×5） | ⛔ 已刪（測的是不存在的功能） |
>
> **runtime 與導入前的 `c28e1e5` 相比只差註解**（22 行純說明，零行為差異）。
> EditMode 596／595 passed、PlayMode **61／61**（66 → 61，正好是移除的 5 條）。
>
> ## 🔓 唯一的重開條件（正本 `docs/38` §13.3，四選一）
>
> ①改用自製／Kinematic Character Motor ②正式重寫 grounded／floor solver
> ③traversal／ledge gameplay 本身需要精確 support model ④實際展示證明此 overhang 明顯影響觀感或玩法
>
> ⛔ **否則不要再新增局部 workaround**——較小 support radius／footprint／暫時縮小碰撞體／
> outward push／只在某些狀態否決 grounded／為 Foot IK 開特例閘門，**這些全部量過了，全是淨負面或成本過高**。
>
> 📌 若真的要重開，**先讀 `docs/38` §12.9 的四條結論**，可省下整輪重新量測。
> 最重要的一條：**在 Unity `CharacterController` 上，任何只改「碰撞體形狀」的手段都無法乾淨地
> 製造「離開邊角」這個位移**——`Move()` 不沿邊角滑開，唯一會產生水平位移的機制是 depenetration。
>
> ## ➡️ 優先事項
>
> 回到 gameplay ／ combat ／ animation ／ VFX（與下方 audio 結案交辦一致）。


> # ⏹ 2026-09-17（四）— **Audio 結案並降優先級**（使用者明確裁決）
>
> ## ⛔ 下一個會話：**不要主動重啟 audio**
>
> | 項目 | 裁決 |
> |---|---|
> | 腳步／落地 | **維持現況**，不再微調 |
> | Melee Hit ／ Impact（存活的 10 顆） | ✅ **繼續使用** |
> | 法術音效 | **允許不完整**；日後有需要再補**少量** Cast／Impact |
> | Hurt ／ Death ／ Exertion 等人聲 | ⛔ **全部不做** |
> | 搜尋 voice pack | ⛔ **不需要** |
> | **DSP buffer A/B** | ⛔ **不做**（推翻先前排隊項；`docs/35`§6・`docs/36`§7・`docs/37`§9 已就地標註撤銷） |
> | 擴張 Audio Framework | ⛔ **不再擴張** |
>
> ## ~~✅ 唯一的 audio 待辦（一行，做完就結束）~~ → **已完成，2026-09-18 磁碟驗證**
>
> ~~`unity cmd eval_file --file Tools/audio-pipeline/WireClips.cs`~~
> ~~清掉 10 個 `AudioDefinition` 因素材移除而留下的 **missing clip slot**。~~
>
> 🔍 2026-09-18 逐檔掃過 `Assets/ScriptableObjects/Audio/Combat/*.asset`：
> 13 顆資產共 12 個 clip 參考（`Melee_Hit` 10、`Fireball_Release` 1、`Ice_Cast` 1），
> **每一個 GUID 都對得上存在的 `.meta`**，其餘 10 顆是乾淨的 `clips: []`，沒有空槽。
> ⇒ **這條待辦可以結案，不需要再跑。**
>
> ## ➡️ 優先事項回到 gameplay ／ combat ／ animation ／ VFX
>
> 正本 `docs/37` §12。若日後重啟素材，**不要照上次的順序跑**——
> 上次命中率 23%（43/56 白做），根因與正確順序記在 `docs/37` §11.3。


> # ⏸ 2026-09-17（三）— **戰鬥音效聽感驗收完成，素材工作暫停**
>
> ## 結果：56 顆採用 → **13 顆存活（23%）**
>
> | | |
> |---|---|
> | ✅ **Keep** | **Melee/Hit ×10**（拳擊肉體 ×4 ＋ 金屬刀刃命中 ×6）——使用者：「幾乎都接受」 |
> | 🟡 考慮中 | `FIREWhsh_Whoosh_Fire_Deep_Growl`、`Woosh_Sweep_Slide_Infographics`、`GLASMvmt_Whoosh_Glass_Crystal` |
> | ⛔ 寧願不要 | 其餘 **43 顆**，已移出 `Assets/`（原檔仍在 `_AudioResearch/Selected`／`Cut`） |
>
> ⇒ **除 Melee/Hit 外全部靜音。** 這是刻意的——使用者原則「不要為了『有聲音』硬塞」。
> 池空時 `GetNextClip()` 回 null、`AudioController` 靜默跳過，不報錯。
>
> ## ⭐ 陣亡的分類不是隨機的
>
> **人聲／生物聲 18 顆全滅**（Hurt 6／Death 2／Exertion 2／Jump 8）——你**事前就標成高風險**，判斷正確。
> designed 調性音 6 顆全滅。**冰材質實錄 8 顆也全滅**（真實冰裂聲讀不出「冰刺法術」的身份）。
> Melee/Swing 9 顆全滅（就是自帶 thud 的那顆母帶）。
>
> **存活的只有兩種性質：①錄下來的實體接觸 ②錄下來的空氣移動。**
> ⇒ Sonniss 給了 impact 層，而且**只**給了 impact 層——與你一開始的判斷逐字吻合。
>
> ## 🔴 這輪真正的教訓
>
> 我連續兩輪都是「下載 → 切段 → 匯入 → 請你驗收」，**43/56 白做，命中率 23%**。
> 瓶頸不是找不到素材，是**我無法判斷適不適合**。
> ⇒ 下次要把昂貴的一步倒過來：**先列候選 pack ＋ 官方 demo 連結，你聽 demo 挑完再跑管線。**
>
> ## ⏸ 決定
>
> - **素材工作暫停**（你的裁決）。人聲方向的問題亦裁決**不追**。
> - 程式面**完全沒動**：`Assets/Scripts` 改動 = 0，EditMode **592／591 passed** 不變。
>
> ## ⚠️ 唯一的待辦：Unity 重開後補跑一次接線
>
> ```bash
> unity cmd eval_file --file Tools/audio-pipeline/WireClips.cs
> ```
>
> 我移除那 43 顆時 Editor 已經關了 ⇒ 10 個 `AudioDefinition` 的 `clips` 陣列裡
> 還留著 missing 的槽位。**行為已經正確（靜音、不報錯）**，重跑只是把陣列收乾淨。
>
> ⛔ ~~DSP buffer A/B~~ —— **2026-09-17 已裁決不做**，見最上方的結案交辦。


> # 🎧 2026-09-17（二）— **驗收整備完成，等你聽**（系統零擴張）
>
> ⛔ **這一輪沒有動任何 runtime 程式**（`Assets/Scripts` 改動 = 0）、沒有新增 Audio Framework、
> 沒有新增事件類型。EditMode **592／591 passed／0 failed／1 skipped** 不變。
>
> ## ⚠️ 先說清楚：**Keep／Needs tuning／Replace／Needs extra layer 我不能填**
>
> 我聽不到聲音。你明講了「不要再以檔名或量測代替聽感判斷」——所以那張表是空的，等你。
> 我這輪只做兩件事：**①回答本來就是量測問題的 #5／#6；②把干擾聽感判斷的東西清掉。**
>
> ## 🐛 我上一輪做錯了一件事，已修（這件事會直接汙染你的驗收）
>
> `cut.mjs` 只做了 **peak 正規化**就收工，而 **peak ≠ 響度**。實測池內離散度：
>
> | 事件 | 修正前 | 修正後 |
> |---|---:|---:|
> | **Melee/Hit** | **15.0 dB** | **2.5 dB** |
> | Ice/Spawn | 17.0 dB | 8.7 dB ⚠️ |
> | Fireball/Impact | 12.0 dB | 4.6 dB |
> | Character/Hurt | 8.6 dB | 2.6 dB |
>
> **不修就驗收，「近戰有沒有重量」聽到的其實是「抽到 10 顆裡的哪一顆」。** 結論會是雜訊。
> （另外差點誤報三顆「空檔」——那是 integrated LUFS 的 400 ms gating 對 <0.4s clip 失效，
> **量測代理答非所問**，不是檔案壞掉。已改用與長度無關的指標。）
>
> ## ✅ #5 前置靜音：**沒有重演 footstep 的問題**
>
> 9 個事件 **0 ms**；最差是 `Woosh_Debris` 的 48 ms，而那顆本來就是緩起的 whoosh。
> 對比 `docs/34` 當時腳步是**每一顆**都帶 29.7 ms。
>
> ## ✅ #6 不會爆掉，但**會互相吃掉**——這就是 #1 的答案
>
> 最壞同時組合（冰刺打中）功率相加 **−9.7 dB**，離 0 dBFS 還有 10 dB ⇒ **不需要 limiter／ducking**。
>
> 🔴 **但 `Melee_Hit` 是全場最小聲（−22.6 dB），被同一瞬間的 `Enemy_Hurt`（−14.3 dB）壓低 8.3 dB。**
> 素材本身就低 10 dB（crest factor 極高 ＝ **有 transient、沒有 body**），`volume 0.95` 補不回來。
> ⇒ 「近戰沒有重量」有一個**與素材品質無關**的成因在這裡。
>
> ## ⚠️ 兩個我在選材時沒注意到的東西
>
> 1. **`Melee_Swing` 的母帶叫 `SWING IMPACTS … To Thud Impact` —— 它自己就含一次撞擊。**
>    而揮擊是「每次必響」⇒ **揮空也會有撞擊聲**；打中時是**兩次撞擊疊在 20 ms 內**。
>    備援有 `METLFric_SWING SCRAPE`（純刮擦無撞擊）可換。
> 2. `Woosh_Sweep_Slide_**Infographics**_Basic` 在 Fireball/Release 池裡——**來源本來就是 UI 素材**。
>    正對應你 #2 的疑慮，但它聽起來對不對仍然要你判斷。
>
> ## ⏱ 時序（近戰那條「流程」其實壓在同一個瞬間）
>
> | 動作 | Release | |
> |---|---:|---|
> | Melee | **0.24 s** | 命中窗**同幀**開，Hurt 慢一幀 ⇒ swing/hit/hurt = **0 / +20 ms / +36 ms** |
> | Fireball | **0.42 s** | 命中再 +0.2 s |
> | Ice | **0.48 s** | Release 與 Impact **同時** |
>
> ## 🎧 驗收工具（雙擊開啟）
>
> **`D:/Unity Project/_AudioResearch/Combat/audition.html`**
>
> - **A. 情境試聽** ×6：用 **Web Audio 以真實時間差與真實相對音量**疊播——**不是一次聽一顆 wav**。
>   已實測排程精確度：近戰情境 0 / 0.020 / 0.036 s，與設定完全一致。「🎲 換一組」可重抽。
> - **B. 逐池** ×12（56 選用 ＋ 31 備援）：每池一組四選一 ＋ 備註，
>   按「產生結論」輸出可直接貼回對話的 Markdown。**四個選項全部預設空白。**
>
> ## 📌 沒去找 magic pack —— 你的條件還沒成立
>
> 你說的是「**如果法術仍缺 designed sound**，再另外找」。那個「仍缺」要等你的聽感結論。
> 量測只說得出 `Infographics` 那顆**來源是 UI 素材**，說不出整個法術層聽起來夠不夠。
> ⇒ 等你回傳結論，若 Fireball／Ice 標成 Replace 或 Needs extra layer，我再去找。
>
> `Player_Death` 仍是 **1 顆**——已知缺口，照你說的先標記不急著補。


> # 🔊 2026-09-17 — **戰鬥音效**（程式／資產／**素材**／測試全部落地，⛔ **選材未經人耳驗證**）
>
> EditMode **592 total／591 passed／0 failed／1 skipped**（+10 條 `CA1`–`CA10`；skip 為既有 NavMesh 環境限制）。
> PlayMode **61／61 passed**。**正本 `docs/37-combat-audio.md`**。
>
> ## ⭐ 這一輪真正的發現
>
> **使用者問「哪些可以抽成真正共用的 audio 基礎元件」——答案是四項裡有三項根本不用抽。**
> `AudioDefinitionSO`（clip 池／shuffle bag／volume jitter／pitch range）、`AudioLibrarySO`、
> 3D playback 早就是通用的，而且早就住在 `Presentation/Audio` 而不是 Footstep 命名空間。
> 真正要動的只有一個名字：`FootstepAudioSourcePool` → **`AudioSourcePool`**
> （它原本就一行腳步知識都沒有，舊名字描述的是**第一個呼叫者**，不是職責）。
> **行為逐位元不變 ⇒ 零測試改動。** 上一輪把通用能力放在通用的位置，這一輪就只付了改名的成本。
>
> ⛔ **留在 Footstep domain**：`FootstepAudioBankSO`（key 是 Surface×Kind×Gait，對戰鬥全部無意義）、
> `FootstepDetector`／`SurfaceResolver`／`SurfaceMapSO`／`SurfaceId`／`FootstepKind`／`GaitTier`。
> **Combat 端沒有任何一行 `using` 指到 Footstep 型別。**
>
> ## 做了什麼
>
> | 項目 | 狀態 |
> |---|---|
> | 13 個事件全部接上**既有的時間權威**（不靠動畫時間猜） | ✅ |
> | `ActionAudioSink`（新，per-slot Cast／Release）；X Bot 掛三顆並綁 Slot 1／2／3 | ✅ |
> | 命中音：`ThrownProjectile`@`hit.point`／`MeleeHitboxSink`@被打者／`GroundEffectSink`@落點 | ✅ |
> | 黑板加 `SurvivabilityData.JustDied`（**事件**，與 `JustTookDamage` 逐字同構） | ✅ |
> | `AudioController` 成為角色音效輸出持有者（`PlayAt`）；Library 事件改走池 | ✅ |
> | Player／Enemy 各一份 `AudioLibrarySO`（同一個 enum，不同資產 ⇒ **不把陣營寫進詞彙**） | ✅ |
> | 13 個 `AudioDefinition` 資產建立並接線 | ✅（**clip 池全空**） |
>
> ## ⭐ 素材：已下載、已切、已選、已匯入（**56 個 clip**）
>
> **上一版我說「不建議下載 Sonniss」——那個結論被自己的量測推翻了。**
> 它建立在「346 檔 ÷ 121 library ⇒ 每個只有 2–4 顆，湊不出池」，**錯在把檔案數當成素材數**：
> `SWSH_SWING IMPACTS` 一個檔 **61.8 秒、內含 21 個獨立揮擊 take**；木質撞擊 16 個；四連拳 4 個。
> production library 的慣例就是一個檔放一整組 take。⚠️ **track list 只能說有什麼，不能說有多少。**
>
> | | |
> |---|---|
> | 分卷盤點 | 用 **HTTP Range 讀 zip central directory**，5 卷全部盤完，**沒有下載整包** |
> | ⭐ 重點修正 | **你指定的 part1 不是重點那卷**——part1 是 344 Audio 的 A→H（古董／理髮廳／聖誕節）。**part2 才是**（Kopeikin 冰與魔法、David Dumais 近戰、Epic Stock Media） |
> | 實際下載 | **256 MB**，不是 6.45 GB（Range 讓我們只抓需要的**檔案**，比「只抓需要的分卷」更細一級） |
> | 處理 | 63 母帶 → **87 個 clip**：切 take → **砍前置靜音** → 48k/mono/16-bit → peak −1 dBFS。⛔ 不用 compressor（會壓掉 transient） |
> | 前置靜音實測 | `ice, crack` **514 ms**、`magic, drone` **639 ms** ⇒ 不砍的話攻擊音會晚半秒 |
> | 選材 | **56 個**進 Unity（每事件 ≤10），31 個留 `Cut/` 當備援 |
> | ⛔ 授權 | wav **不進版控**——License v2.0 禁止「以音效的形式散布」，而 repo 是**公開**的。比照 `MovementAnimsetPro`。**重建程序在 `Tools/audio-pipeline/`（11 支腳本）** |
> | Boolean SFX | **沒下載，也不建議**：40 個 **128 kbps MP3**（MP3 天生帶前置靜音，正是我們在砍的東西）。且下載要走 itch 的 name-your-own-price 人工流程 ⇒ 依你指示沒有繞過 |
>
> ## 🔴 唯一真正沒被驗證的事：**選材對不對**
>
> **我沒有耳朵。** 選材依據只有：①客觀量測 ②檔名語意（UCS 前綴）③與專案已知事實的匹配
> （角色持劍 ⇒ 排除 16 個木棍撞擊）。**「符合 demo 的視覺與動畫重量」完全沒有被驗證過。**
>
> ⇒ 先開 **`D:/Unity Project/_AudioResearch/Combat/audition.html`**（瀏覽器，含 56 選用 ＋ 31 備援，
> 每個都有播放器與「在遊戲裡什麼時候會聽到」的情境說明）。
> 重點懷疑：①近戰夠不夠厚 ②法術會不會太 UI／太卡通 ③人聲會不會出戲（Anime Fight Voices 是**動漫風**）。
> 換素材：把 `Cut/` 的備援拖進 `Assets/Audio/Combat/...` → 跑 `WireClips.cs`。
>
> ⚠️ **最薄的幾項**：`Player_Death` 只有 **1** 顆；`Enemy_Hurt`／`Fireball_Cast`／`Ice_Cast` 各 2 顆。
> 法術與死亡正如你預期的「Sonniss 適合真實 weapon／body Foley，法術要另外找」——那是採購決定，留給你。
>
> ## Play 驗收（**clip 已填好，直接 Play 就有聲音**）
>
> 完整清單見 `docs/37` §8。最關鍵的三項：
> **①Melee 連段要響三聲**（只響一聲＝有人把 §5.1 的節流閘門加回去了）
> **②Fireball Impact 要出現在接觸點**，不是角色身上
> **③走下平台邊緣會不會也有起跳聲**——`JustLeftGround` 是「離地」不是「起跳」（已知近似）
>
> 📌 **DSP buffer A/B 依你的指示排在戰鬥音效之後**，本輪未動。

> # 🔊 2026-09-16 — **腳步聲 Stage 0 ＋ Stage 1**（已落地，⛔ 待 Play 驗收）
>
> EditMode **565 total／564 passed／0 failed／1 skipped**（skip 為既有 NavMesh 環境限制）。
> commit `5778ba8`。**正本 `docs/35-footstep-detector-stage1.md`**（研究輪 `docs/34` 已結案並標註推翻處）。
>
> ## ⭐ 這一輪真正的發現
>
> **舊偵測器不是「慢」，是在錯的相位擊發。** 重放真實動畫量到：`WalkFwdLoop` 的腳步
> **提前約 340 ms 擊發在擺盪中低點**，而真正的踩地因剩餘行程不足反而被吃掉。
> `docs/34` §4.2 把這項估成「落後 0–17 ms」——**該估計作廢**。
>
> 根因是**判準選錯**：用「腳的世界高度」分離「真正踩地」vs「擺盪中低點」只有 **1.27 倍**分離度；
> 改用**腳的世界水平速度**是 **4.4 倍**（踩住的腳在世界空間靜止）。**選對訊號比調對門檻重要。**
>
> ## 做了什麼
>
> | 項目 | 狀態 |
> |---|---|
> | WASD Stone × {Walk,Run,Drop,Shuffle} 24 顆匯入（mono/48k/PCM/preload） | ✅ |
> | 三事件不再共用同一顆 clip；shuffle bag ＋ 音量抖動 ＋ Landing 改用 Drop | ✅ |
> | AudioSource 3D（spatialBlend 1／min 2.0／max 40／doppler 0／spread 25°） | ✅ |
> | 偵測判準改為「離地高度 ＋ 水平速度」，分類 Normal／Shuffle | ✅ |
> | `FootIKGroundData`：複用 Foot IK 既有 raycast，**零額外查詢** | ✅ |
> | Editor 環形緩衝 trace 面板（`recordDebugTrace`，非每帧 log） | ✅ |
>
> ## 🟠 需要你決定的兩件事（Play 之後）
>
> 1. **gait 未分池**——Walk/Run/Sprint 目前共用 Walk 素材（Run 已匯入未接線）。屬 Stage 2，但這是
>    現在最明顯的品質缺口；若跑起來明顯不對，可考慮把它提前。
> 2. **`ProjectSettings/AudioManager.asset` 的 DSP buffer 1024 未動**——它現在是剩餘延遲中**最大的一項**
>    （21–43 ms），改 256 可省 16–32 ms，但是**全域設定**、以 CPU 換延遲 ⇒ 沒有你的裁決不動。
>
> ⚠️ **`docs/34` §8-Q1（管線①②對調）已作廢**：Stage 1 之後對調會讓偵測器讀到上一帧的地面高度，樓梯上淨虧。
>
> ## Play 驗收請聽
>
> Idle（應**完全無聲**）／Walk↔Run↔Stop 的收步有沒有碎步／上下樓梯節奏／左右腳是否有差異／
> 近距離音量是否足夠／Shuffle 是否太吵。勾 `FootstepDetector.recordDebugTrace` 可在 Inspector 看逐筆判定。


> # ✅ 2026-09-15（晚）— **敵人卡死 ＋ 死亡收束 ＋ 貼身鏡頭**（四項落地、一項退回裁決）
>
> EditMode **545 total／544 passed／0 failed／1 skipped**（+22 條；skip 是既有 NavMesh 環境限制）。
> PlayMode **56／56 passed**（`CO5` 先失敗 2 次、後連過 4 次 ⇒ **確認為 flaky**，見下方 🔴）。
>
> ## 做了什麼
>
> | 項目 | 狀態 | 正本 |
> |---|---|---|
> | **敵人只會迂迴時卡死** | ✅ 修好（側移諮詢 NavMesh ＋ 新增 `Recover` 模式 ＋ `Approach` 無路徑退化） | `docs/21` §1.2.1 |
> | **屍體仍是合法目標** | ✅ 修好，`docs/15` §16-7 **結案** | `docs/15` §4.2 |
> | **屍體吃掉火球** | ✅ 修好（`TryRequestHit` 對死者不消耗自己、不施 Slow） | 測試 `SlowEffectTests` |
> | **屍體仍用頭追著玩家轉** | ✅ 修好（`HeadLookController` 加 `IsDead` 抑制，比照 `CharacterFacingSource.DeathSuppressed`） | — |
> | **貼身鏡頭穿進角色** | ✅ 做了（`ShadowsOnly` 抑制 ＋ 遲滯）**數值未經人眼驗收** | `docs/09` §12 |
> | **亂序操作髒狀態** | ✅ 改成可重現的測試（用**出貨用** config 跑 400 帧亂序 → 鬆手 → 斷言收斂）**目前全綠** | `StateMachineChaosConvergenceTests` |
> | **投射物 tunneling** | ✅ 加了接線守門（每物理步位移 ≤ 半徑；現況 0.10 vs 0.15） | `PrefabWiringTests` |
> | **動畫腳頻過快** | ⛔ **未執行，退回裁決** | 見下方 🟠 |
>
> ## ✅ 腳頻（原 B1）— **A′ 已落地**，舊契約正式廢除
>
> **廢除**：「Combat 8-way 所有方向與 gait anchor 等速」。正本 `docs/21` §1.5.1、摘要 `docs/11` §13。
>
> 新規則（兩個環共用，**零手填**）：
> `threshold = dir × min(gaitAnchor, bake/max × maxPlaybackStretch)`，`playback = |threshold| / (bake/max)`。
> `maxPlaybackStretch` 是唯一設計參數（X Bot **1.35**／Y Bot 1 ＝不拉伸）。
>
> | 方向 | bake m/s | threshold | ％ of fwd | playback |
> |---|---:|---:|---:|---:|
> | Forward | 3.578 | 0.7500 | 100% | 1.313（**不變**） |
> | Forward Diagonal | 3.622 | 0.7500 | 100% | 1.297（**不變**） |
> | Right | 2.265 | 0.4884 | 65.1% | 1.350 |
> | Backward／Back Diagonal | 2.214 | 0.4773 | 63.6% | 1.350 |
> | Left | 2.165 | 0.4667 | 62.2% | 1.350 |
>
> **最高 playback 2.17× → 1.35×。** Walk 環不受影響（預算沒咬到）。
> ⛔ 使用者裁決：**不得**加最低方向速度比例或其他手感補償，先以純推導結果進 Play；太慢再另開 tuning。
>
> **資產**：新增 `XBotSpellDirectionalSpeedProfile.asset`、接上 X Bot、改寫 mixer idx 10/11/12/15/16。
> **程式**：`CombatDirectionalSpeedProfileSO` 加 `maxPlaybackStretch` ＋ 四個斜向欄位（四格全填才啟用八向插值；
> 留空沿用 cardinal L1 ⇒ **Y Bot 逐位元不變**）。
> **測試**：`SL5`／`L6` 已更新為新 baseline 並改名；`CombatDirectionalSpeedProfileTests` 新增八向與 stretch 測項。
>
> ⚠️ **已知並接受的副作用**：`ApplyCombatDirectionalSpeedLimit` 只看 `InCombat`、不看當前 mixer
> ⇒ **X Bot 在所有戰鬥中側移都會變慢**，不只施法時。`L6` 原本明文禁止此事，該禁令一併廢除。
>
> ## ✅ 火球／冰刺命中判定 — **已結案（2026-09-17，使用者 Play 確認修好）**
>
> > **⛔ 本項不再是 blocker。以下整節保留為診斷歷程的案例紀錄。⬇️**
> >
> > 三個獨立成因全數落地於 commit `7532772`，收尾於 `9386016`：
> > ①視覺 core 與 collider 分家 → VFX 生成時 unparent、回到原生模型，投射物以同速平行飛；
> > ②`Update` 位移 ＋ `OnTriggerEnter` → `FixedUpdate` ＋ `SphereCast` 掃掠**獨佔** hit authority；
> > ③瞄準點在段落邊界鎖死 → 承諾拆成 `_releaseContext`（facing，不變）／`_effectContext`（每帧刷新）。
> > 冰刺判定形狀同 commit 由 `OverlapSphere` 改為沿施法方向的 `OverlapCapsule`。
> >
> > **磁碟複查（2026-09-17，逐條對照程式碼而非憑 commit 訊息）**：
> > `ThrownProjectile.FixedUpdate` 已是掃掠獨佔、全檔無 `OnTriggerEnter`；
> > `ActionState.OnTick` 每帧呼叫 `RefreshEffectContext`；`GroundEffectSink` 已用 `OverlapCapsuleNonAlloc`。
> >
> > ⚠️ **下方「📌 仍未修的結構性弱點」那一段已作廢**——它說「位移在 `Update`、判定在物理步，兩者不同步」，
> > 而掃掠上線後兩者都在 `FixedUpdate`，該段描述的是修好之前的狀態。
> > 真正沒動的只有 `spawnPoint` 接在 `mixamorig:Spine2`，且已裁決那是**美術取向、不是缺陷**。
>
> ### （以下為當時的診斷過程）首要嫌疑：敵人的碰撞膠囊與網格錯位 0.18 m
>
> 回報：①火球**穿過敵人之後**才銷毀並受傷 ②冰刺視覺命中但實際不中。
>
> ### 已用 throwaway PlayMode 探針排除的（皆已刪除）
>
> | 量測 | 結果 |
> |---|---|
> | 真 `Projectile_Fireball` vs 合成 CC（r=0.32） | 第 16 物理步命中、當步扣血、下一步銷毀 ✅ |
> | 真 `Projectile_Fireball` vs **真 Y Bot prefab** | 命中於 `z=-0.459`，幾何預期 `-0.470` ⇒ **誤差 1.1 cm** ✅ |
>
> 同時排除：碰撞矩陣（全開）／layer 2 vs 7（未擋）／`m_AutoSyncTransforms = 0`／
> `CharacterController` 收不到 trigger／`ThrownProjectile` 的傷害路徑。
> **⚠️ 但探針裡 Y Bot 的腳本全被停用 ⇒ Capsule Offset 沒有作用。那正是沒重現的原因。**
>
> ### ✅ 已排除的三個假說（都量過，量級不夠）
>
> | 假說 | 實測 | 判定 |
> |---|---|---|
> | 敵人 capsule offset 位移 | 上限 0.18 m | ⛔ 影片過衝約 1 m，**差 5 倍** |
> | 投射物取樣太稀（tunneling） | `fixedDeltaTime = 0.02` ⇒ 每步 0.1 m；投射物直徑 0.3、敵人膠囊 0.64 | ⛔ 連續覆蓋，穿不過去 |
> | VFX／collider／root Transform 脫鉤 | collider vs root **0.000000 m**；VFX 根 **0.000 m** | ⛔ Transform 層完全一致 |
>
> 📌 **PhysX 同步延遲確實存在但有界**：`m_AutoSyncTransforms = 0` ⇒ `collider.bounds.center`
> 相對 Transform 呈**鋸齒狀 drift 0.015–0.120 m，每個物理步歸零**，`Physics.SyncTransforms()` 後為 0。
> 峰值 ≈ 一個物理步的位移。**解釋不了 1 m。**
> ⚠️ 探針 frame 0 曾量到 15.78 m——那是 `Instantiate` 後首次物理同步前，PhysX 還停在 prefab
> authored 座標（`m_LocalPosition.z: 7.32436`）的**量測假象**，不是執行期行為。
>
> 📌 `FireSparks` 的 `simulationSpace = World` ⇒ 已發射的火花**留在世界座標**拖在後面。
> 不是 bug，但它會系統性地讓**肉眼**把命中位置判偏（看到的尾巴不是火球本體）。
>
> ### 🔴 目前唯一在量級上對得上的根因：**瞄準點在 entry 就鎖死**
>
> （Codex 偵查 → 本會話逐條驗證）
>
> `ActionState.CaptureReleaseContext(data)` 在**進入 Action 的那一幀**執行
> （[ActionState.cs:178](Assets/Scripts/Core/StateMachine/States/ActionState.cs:178)，
> 註解「第 1 段邊界取得一次承諾」），之後 `_releaseContext` 是定值。
> Fireball 的 release 在 `FallbackDuration 1.2 × ReleaseNormalizedTime 0.35` = **0.42 秒後**。
>
> Y Bot 實際側移速度（**Codex 算 2.19 m/s 是錯的，漏算方向速度上限**）：
> `min(holdStrafeSpeedNormalized 0.35, Bake_StrafeLeftLoop 1.6443 / Bake_SprintFwdLoop 6.2614 = 0.2626)`
> × 6.2614 = **1.644 m/s**。
>
> | 階段 | 時間 | 相對鎖死座標的位移 |
> |---|---:|---:|
> | 抬手 | 0.42 s | 0.69 m |
> | 飛行（約 3 m ÷ 5 m/s） | 0.60 s | 0.99 m |
> | **合計** | | **≈ 1.7 m** |
>
> **敵人膠囊直徑 0.64 m ⇒ 側移中的敵人打不中是預期行為，不是偶發。**
> 這同時解釋了「看起來穿過身體」：過肩鏡頭下，飛向敵人一秒前位置的火球，投影就是擦過身體。
>
> ### 另外兩條獨立且已驗證的缺陷
>
> 1. **`spawnPoint` 接錯**：Slot 2 的 `FireballEmitter.spawnPoint` = `{fileID: 5221306491677144301}`
>    = **`mixamorig:Spine2`（胸椎）**，而 prefab 裡有命名正確、掛在右手下的 `ThrowSpawnPoint`
>    （local offset `(0, 0.006, 0.12)`）沒被使用。**火球目前從胸口長出來。** 無爭議，可直接修。
> 2. **冰刺視覺與判定形狀分離**：`Human_Spell_Ice` 六排 emitter 沿 +Z 在
>    0.58／1.15／1.87／2.66／**3.42**／**4.42** m，而判定球前緣 `castDistance 0.4 + effectRadius 3`
>    = **3.4 m** ⇒ 最後兩排超出 **0.42 m／1.42 m**（還沒算冰刺 mesh 的 0.79–0.82 尺寸）。
>    ⛔ 單純把 `effectRadius` 加大是**錯的**——會連背後與左右一起打到，違背「前方一列」的語意。
>
> ### ⚠️ 仍然成立的結構性弱點（今天不是主因，但該修）
>
> 位移在 `Update`（`Time.deltaTime`）、判定在物理步，**兩者不同步**。目前不穿透是**參數碰巧**
> （0.1 m 步長 vs 0.64 m 膠囊），不是結構保證；調高速度或掉幀就會放大。
> 📌 `PrefabWiringTests.ProjectileStepPerPhysicsTick_StaysWithinItsOwnRadius` 斷言的
> `speed × fixedDeltaTime ≤ radius` **是機率護欄不是保證**——它假設的取樣間距不是真實間距。
> 結構修法：`FixedUpdate` ＋ `SphereCast` 掃掠（會改變位移語意，需裁決）。
>
> ### ✅ 2026-09-16（晚）— **VFX 回到原生模型：生成時脫離投射物**（裁決 B，正本 `docs/11` §18）
>
> §16 把 core 的 `startSpeed` 歸零雖然對齊了，卻**把拖尾一起殺掉**（使用者回報）。
> 實測組成後發現兩個原因疊加：
> ①三個尾巴是掛在 core 粒子上的 `[Birth]` 子發射器，**空間展開來自 core 的本地位移**；
> ②它們用 `rateOverDistance`（6／1／2 每公尺）出粒，**出粒數綁在移動距離上**。
> ⇒ 這個 VFX 的前提是「**發射器不動、火球是會飛的粒子**」，與我們的「GameObject 在飛」互相衝突。
>
> **裁決 B**：`Initialize` 時把視覺 `SetParent(null, worldPositionStays: true)` 留在發射點；
> core 照原設定自走 **15 m/s**；投射物也以 **15 m/s** 平行飛。
> ✅ 視覺零妥協 ✅ 不變量**變強**（兩套獨立模擬靠同源同速同向對齊）
>
> ⚠️ **只改 startSpeed 會踩到的兩個隱藏地雷**（讀 duration／stopAction 才發現）：
>
> | 欄位 | 改為 | 不改會怎樣 |
> |---|---|---|
> | core `duration` | 5.0 → **0.1** | `max=1`＋`life=1.0` ⇒ 死了會補生 ⇒ detach 後**每秒從槍口再射一發** |
> | core `stopAction` | Destroy → **None** | 掛在 core 上 ⇒ core 結束時把**還活著的尾巴一起殺掉** |
>
> **命中收尾**：`Clear()` core（火球消失於接觸點）＋ `Stop(StopEmitting)` 全部；
> ⛔ 不整個 `Destroy`——那會把尾巴一刀切掉。
>
> **資產**：core `startSpeed` **15**／`duration` **0.1**／`stopAction` **None**；
> `FireballEmitter.projectileSpeed` 20 → **15**。第三方來源全程未動。
>
> 📌 **`projectileSpeed` 與 core 的 `startSpeed` 必須永遠一致**，改一邊要同步另一邊（`PV1` 守）。
>
> ⚠️ **需要人眼確認**：①火球會不會重複發射（`duration 0.1` 應已擋掉，屬推導未實測）
> ②命中時尾巴是自然淡出還是被切斷。
>
> ### ✅ 2026-09-16 — **火球視覺速度回復 ＋ 投射物改為連續掃掠**（正本 `docs/11` §17）
>
> §16 把 core 的 `startSpeed` 歸零後，core 與 collider 對齊了，但**視覺速度由 20 掉到 5 m/s**。
> 使用者裁決「視覺上還是原本的好」⇒ 把 collider 拉到特效原速。
>
> ```
> 原本  發射器 5 ＋ 粒子 15 = 20 m/s
> 現在  發射器 20 ＋ 粒子 0 = 20 m/s     ← core 仍釘在 collider 上
> ```
>
> ⚠️ **提速強制了掃掠**：20 m/s ⇒ 每步 0.40 m；正面命中窗 0.94 m（安全），
> **擦邊命中窗只剩 0.27 m ⇒ 必漏**。兩件事不能只做一半。
>
> | 項目 | 新契約 |
> |---|---|
> | 位移／判定 | `FixedUpdate` ＋ `Physics.SphereCastNonAlloc`，start → desiredEnd |
> | hit authority | **掃掠唯一。`OnTriggerEnter` 已整段移除** |
> | 合法命中 | ①非自己 ②非 owner／其子物件 ③**非屍體**（透明，同 tick 繼續往後找） |
> | LayerMask | `~0` ＋ `QueryTriggerInteraction.Collide`（維持現行廣義行為；⛔ 本輪不重構 layers） |
>
> **命中順序（不可調換）**：① 位移截斷到 `hit.distance` → ② impact 用 `hit.point` → ③ 才結算完成。
> ⛔ 不得先移到 `desiredEnd` 再回頭處理。
>
> **測試契約更替**：⛔ 移除 `ProjectileStepPerPhysicsTick_StaysWithinItsOwnRadius`
> （`speed × fixedDeltaTime ≤ radius` **不是保證**，只是機率護欄）；
> ✅ 新增 `ProjectileSweepPlayModeTests` `PS1`–`PS4`：
> **60 m/s（每步 1.2 m vs 膠囊 0.64 m）不 tunneling**／接觸點截斷／owner 排除／屍體透明。
>
> **資產**：`FireballEmitter.projectileSpeed` 5 → **20**；Throw 的 emitter **不動**（維持 5）。
>
> 📌 投射物 prefab 上的 kinematic `Rigidbody` 與 `isTrigger` 已非命中所需，**刻意保留不動**。
>
> ### 🔴 **真正的主因找到了：視覺 core 與 collider 分家**（2026-09-16，正本 `docs/11` §16）
>
> `Human_Spell_Fireball` 是為**靜止發射器**設計的 VFX——位移由**粒子自己**承擔
> （`main.startSpeed = 15`，Local）。而 `ThrownProjectile` **同時也在搬 GameObject**（5 m/s）。
>
> ```
> 視覺 core = 5 ＋ 15 = 20 m/s      判定 = 5 m/s      ⇒ 相對 10 m/s，差距線性拉開
> ```
>
> 實測：飛 0.22 s 差 **2.22 m**、0.67 s 差 **6.68 m**。**玩家瞄的是 core、遊戲判的是 collider。**
>
> **六項排查**（使用者指定）：① core 粒子 localZ **+2.22（AHEAD）** ② `startSpeed` **15.00** ← 唯一推力源
> ③ velocityOverLifetime False ④ inheritVelocity False ⑤ forceOverLifetime False
> ⑥ subEmitters **True × 4** —— 非推力源，但四個尾巴都是從 core 粒子生出來的
> ⇒ **釘回 core，整條視覺鏈一起回正**。
>
> **修法**：`Projectile_Fireball.prefab` 的 nested instance override `startSpeed 15 → 0`。
> ⛔ 第三方來源（`Assets/Kevin Iglesias/…`）**一行未動**——腳本先驗 `GetAssetPath` 屬於我們的 prefab
> 才寫入，事後 `git status --porcelain "Assets/Kevin Iglesias/"` 複查為空。
> **驗收：core↔collider 距離 2.22 m → 0.02 m。**
>
> **不變量**：**視覺 core ≈ collider**（使用者裁決，投射物唯一該守的視覺／判定不變量）。
> 由 `ProjectileVisualAlignmentPlayModeTests.PV1` 守，斷言的是**可觀察距離 ≤ 1 m**，
> ⛔ 不是 `startSpeed == 0`（那是實作細節，換素材或改用其他推進模組都會讓它失效卻仍破壞不變量）。
> core 身分用**結構判準**（帶 SubEmitter 的那個），不用名字也不用粒子大小。
> 📌 trail／sparks 不在約束內：`FireSparks` 維持 World，只要它們明確是尾巴。
>
> ⚠️ **Play 要確認的視覺副作用**：core 不再自走 ⇒ **拖尾會明顯變短**（相對速度 15 → 5）。
> 要加長就調 trail 的 lifetime／startSpeed，⛔ **不是把 core 速度加回去**。
> ⚠️ **未處理的殘差**：core billboard 半徑約 0.28 vs collider 0.15 ⇒ 視覺球大一圈（**尺寸差**非位置差）。
>
> 📌 **這條與下面的 Fix 1（瞄準點過期，1.7 m）獨立**，兩者都真實存在；
> 本條量級更大（10 m/s 持續拉開），是**主因**，Fix 1 降為次要。
>
> ### ✅ 兩條已修（使用者裁決「1 3 能改」）
>
> **Fix 1 — 承諾拆成 facing ／ effect 兩份**（正本 `docs/11` §14）
>
> | 欄位 | 何時取 | 用途 |
> |---|---|---|
> | `_releaseContext` | 段落邊界（原樣） | facing 承諾，**不變** |
> | `_effectContext` 🆕 | **每帧刷新、release 當下定案** | 投遞給 sinks |
>
> ⚠️ **這是刻意推翻一條被測試守著的契約**：`TD5`／`TD5B` 原文「release **不得**在 release 時重解」、
> 「facing 與世界效果必須是**同一份**承諾」。已依 CLAUDE.md 於同一工作包更新為新 baseline，
> 並在測試註解完整記錄推翻理由（0.42s 抬手 ＋ 0.6s 飛行 ＋ 1.644 m/s 側移 ⇒ 1.7m vs 0.64m 膠囊）。
> **代價**：身體朝向與彈道相差約 **13°**（3 m 距離）。⚠️ **Play 時若覺得那個落差怪，這個決定可以退回**。
> 新守門：`AC1`（effect 跟上 **且** facing 不動）／`AC2`（瞄準失效保留上次落點）。
>
> **Fix 3 — 冰刺判定形狀對齊視覺形狀**（正本 `docs/11` §15）
>
> `OverlapSphere`（身前 0.4 為心、半徑 3）→ `OverlapCapsule`（沿施法方向 0.58→4.42、橫向 0.7）。
> 數值來源是 VFX 自己的 emitter 佈局。X Bot 的 `effectRadius` 已由 3 改為 **0.7**（Editor API）。
> 新守門：`SlowEffectTests.IceHitVolume_*` 三條（涵蓋全列／跟著方向轉／退化不 NaN）。
> ⛔ 順帶被 `A29` 擋下一次：我原本在 sink 裡寫 `transform.root.forward` 當退化後備——
> 那是**讓 sink 自己發明方向權威**。已改為塌成零長度膠囊。
>
> ### ⛔ 第 2 項（`spawnPoint` 接在 `mixamorig:Spine2`）**沒有動**
>
> 使用者只授權 1 和 3。而且再評估：`ThrowSpawnPoint`（右手）是給 **Throw** 用的，
> **法術從軀幹放出來本來就合理** ⇒ Codex 把它列為「缺陷」可能過頭，那是美術取向選擇而非接線錯誤。
>
> ### ~~📌 仍未修的結構性弱點~~ ⛔ **已作廢（2026-09-17）——`7532772` 的掃掠就是這一段要的結構修法**
>
> 投射物位移在 `Update`、判定在物理步，**兩者不同步**（實測 drift 0.015–0.120 m 鋸齒）。
> 今天不是主因，但目前不穿透是**參數碰巧**不是結構保證。
> `PrefabWiringTests.ProjectileStepPerPhysicsTick_StaysWithinItsOwnRadius` 斷言的
> `speed × fixedDeltaTime ≤ radius` **是機率護欄不是保證**。
> 結構修法：`FixedUpdate` ＋ `SphereCast` 掃掠（會改變位移語意，需裁決）。
>
> ### 🔍 Play 時請確認這三件
>
> 1. **火球打得到側移中的敵人**；以及身體朝向與彈道那 13° 落差**看起來能不能接受**
> 2. **冰刺打得到最遠那兩排涵蓋的敵人**，且**背後的敵人不再被打到**
> 3. Debug gizmo：投射物的黃球（實際 collider）／紅球串（物理取樣）；
>    AoE 的膠囊**綠＝查到目標、紅＝0 個**，標籤直接寫出最近目標距離與判定結論
>
> 📌 ③「看不出緩速」你已澄清是**敵人本來就太慢**，不是緩速壞掉 ⇒ 不列為缺陷。
>
> ## ⛔ 還是你的：人類 Play 驗收
>
> 除了原本 Capsule V2 的那份清單（**平台邊緣那項你已裁決結案：radius 0.32 ＋ cap 0.18
> ⇒ 最壞懸空 0.50 m，可接受，cap 留 0.18**），本輪新增要看的：
>
> - **敵人不再卡死**：把敵人逼到牆縫／平台邊緣，看它會不會自己走回來。
>   選中敵人看 Scene view 的 `AI move:` 標籤——出現**洋紅 `Recover`** 就是它出過網又自己救回來。
> - **貼身鏡頭**：走到敵人身上，角色應該消失但**影子還在**；退開要平順恢復、**不得閃爍**。
>   太早／太晚消失就調 `hideTargetBelowDistance`（目前 0.95）。
> - **屍體**：打死敵人後，角色不得繼續朝屍體轉、不得用頭追屍體；火球要能**穿過屍體**打到後面的敵人。
>
> ---

> # ✅ 2026-09-15 — **Dynamic Capsule V2 已完整落地**（正本：`docs/27` §13；**白話教學：`docs/28`**）
>
> EditMode **523 total／522 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **56／56**；recompile 0 errors。資產已接、場景已存檔。
>
> ## 做了什麼
>
> | | |
> |---|---|
> | **TraversalProbe base-center bug** | ✅ 修好。`Awake` 快照 `AuthoredCenter/Radius/Height`，環境查詢全部改讀它。🔒 由 `TR1`（行為，掃 3 個 radius）＋`TR2`（欄位）守住 |
> | **Pose solver** | ✅ `CapsulePoseSolver`——純函式、零 GC（inline `Vec2x8`，不用 `new[]`）、確定性（Dykstra 24 次，`CP10` 拿 300 次比對釘住） |
> | **Profile 資產** | ✅ `CapsulePoseProfileSO` ＋ X Bot／Y Bot 各一份（**網格不同，數值差 42%，不可共用**） |
> | **Runtime** | ✅ 五層管線 Desired→Capped→Smoothed→Safe→center；`locomotionHasAuthority` 參數**消失** |
> | **Debug** | ✅ 五個圓環 ＋ 舊 MoveDirection 模型的紅虛線對照 |
> | **Tests** | ✅ `CP1–CP12`／`CD1–CD5`／`CO1–CO7`／`TR1`×3・`TR2` |
> | **資產** | ✅ 兩隻 prefab 接線完成；SampleScene 的 radius override 已清除**並存檔** |
>
> ## 效果（用 shipping code 跑真實動畫）
>
> | clip | 上半身超出（前） | （後） | 改善 |
> |---|---:|---:|---:|
> | **Run** | 0.204 | **0.024** | **88%** |
> | **Sprint** | 0.271 | **0.091** | **66%** |
> | RunStrafe45 | 0.209 | 0.030 | 85% |
> | **純側移** | 本來就沒穿模 | 解出 **0.000** | V1 會推 0.12 到**錯的一側** |
>
> 真實場景 Play（按住 W）：`desired=0.201 → capped=0.180`，`status=Corrected`，殘餘 0.0000。
>
> ## 🔴 下一個會話要知道的三件事
>
> 1. **Sprint 仍殘餘 9 cm，那不是 bug，也不要靠加大 `MaxOffset` 去追。**
>    根因是膠囊頂端剛好切在頭頂（`docs/28` §G）。要解得動 `height`／`center.y`
>    ——會連帶影響 step-over、天花板檢查與 traversal corridor ⇒ **獨立裁決，本輪明確沒做**。
> 2. **Y Bot 幾乎全程 `Infeasible`，那是訊號不是錯誤**：它的 Chest flesh radius 0.2902，
>    `allow` 只剩 0.0298，**胸膛本來就快把膠囊塞滿**。
>    ⛔ **不要為了讓它不 infeasible 去調小 flesh radius**——那是竄改量測值。要解得加大它的 radius。
>    （已實測不會抖：最壞 0.71 m/s，平滑時間常數 0.12 s。）
> 3. **⛔ 不得再用「核心質心是否在 radius 內」當判準**（使用者裁決）。
>    也不得用骨骼點代替網格——`docs/27` §11 就是這樣得出錯誤結論的。
>
> ## ⛔ 唯一還沒做的：人類 Play 驗收（看起來對不對）
>
> 自動化涵蓋了「數字對不對」，涵蓋不了「看起來對不對」。請實際 Play 看：
>
> - **Locomotion**：Idle／Walk／Run／Sprint／Strafe L,R／RunStrafe 45,135／Stop 有沒有抖或歪
> - **貼牆**：正面跑向牆、斜向跑向牆、沿牆 strafe、轉身貼牆、停止貼牆——頭還看不看得到穿模
> - **邊緣**：站到平台邊緣。偏移 0.18 會把**底部支撐點**一起前移 0.18，
>   可能出現「看起來踩空卻站著」或「還沒走到邊就掉下去」。**這一項是 cap 值能不能留在 0.18 的關鍵。**
> - **Gameplay**：Attack／Spell／Roll／Hurt／Death 期間碰撞行為合不合理（V2 這些狀態**不再停用**偏移）
> - **Traversal**：按著 W 靠牆能不能起攀（這是這輪修的 bug）；Climb／Vault 不得再出現 CorridorBlocked
>
> 📌 Debug：進 Play → 點 Game view → **`** 或 **F1** 開面板 → **F2** 切 Capsule Offset。
> 五個圈：灰＝基準／黃＝Desired／橙＝Capped／青＝Smoothed／洋紅＝實際寫入。
> **五圈重合 ＝ 姿勢站直不需要偏移，那不是壞掉。**


> # 🔴 2026-09-15 — **交辦：Capsule Offset ↔ Traversal 幾何衝突**（正本：`docs/27`）
>
> > ## 🔴 2026-09-15 更新：**下方的「問題陳述」歸因已被程式閱讀推翻，先讀 `docs/27` §10 第 0 條**
> >
> > 下方把失敗歸因於「站位退後 0.12 m 吃掉 entry 合法帶」，並引用「合法帶只剩 20 cm」。
> > 那個 20 cm 是 `docs/24` **§9.3 的 Vault1m 舊值**，`docs/24` §10 的工作包一已把它改成 **0.70 m 寬**；
> > Climb 路徑的實錄合法帶是 **[0.06, 0.81] m**（§19.2 面板 `legal 0.06–0.81`）。
> > ⇒ **0.34 在帶內，而且比 0.22 更接近理想的 0.462** ⇒ 距離判斷推不出失敗。
> >
> > 更可疑的是 `TraversalProbe` 沿 corridor 取樣時用的是**當下（已偏移）的 `characterController.center`**
> > （`TraversalProbe.cs:665`／`:265`／`:435`）⇒ 整條 corridor 檢查膠囊往前推 0.12 ⇒ `CorridorBlocked`，
> > 而那是 `TraversalEntryPolicy.Evaluate` 的**第一個** return，距離根本沒被評估。
> >
> > **⚠️ 這是程式閱讀，沒有 Play 證據。** 決定性證據很便宜：暫時把 `X Bot` 偏移開回來
> > ＋ 打開 `drawTraversalPanelText`（⚠️ `RuntimeDebugPanel.RescanScene` 每次掃描都會把它強制壓成 false ⇒ **必須先停用 X Bot 上的 `RuntimeDebugPanel` 元件**，這也正是現在看不到 reject reason 的原因），
> > 撞牆讀一次 **Entry reject reason** 即可分流。
> > ⛔ 在讀到那個 reason 之前，**不要**在 A／B／C 之間做選擇——三條路都建立在下方這個歸因上。
>
> > ## ✅ 2026-09-15 **再更新：已用 PlayMode 探針證實，歸因確定是錯的**（正本：`docs/27` §10 第 0 條）
> >
> > 探針 `Assets/_Project/Tests/PlayMode/CapsuleOffsetTraversalCorridorSpike.cs`（throwaway spike，只讀 `probe.Candidate`）
> > 掃 radius {0.12, 0.22, 0.32} × {基準／偏移開啟／**對照組：站位退後同樣 0.12 但 center 不偏移**}：
> >
> > | case | root→wall | 結果 |
> > |---|---:|---|
> > | baseline | 0.150 | `Climb2m` ✅ |
> > | **offset-on** | **0.270** | `None` ／ **CorridorBlocked** ／ `EntryToClearanceBlocked` ❌ |
> > | **control（同站位、不偏移 center）** | **0.270** | `Climb2m` ✅ |
> >
> > （0.22／0.32 半徑行為完全一致，連 `EntryCapsuleWallClearance` 都同為 0.0600。）
> >
> > 🔴 **同一個站位、相反的結果 ⇒ 站位不是原因，`characterController.center` 被偏移才是。**
> > reject 發生在 **Probe** 的 `IsCapsuleSegmentClear`（entry→clearance 段），
> > `TraversalEntryPolicy` 的距離判斷**根本沒被執行到**。
> > ⇒ **A／B／C 三條路都在解一個不存在的問題**；`MaxOffset` 不需要調。
> >
> > **修法方向**：讓 probe 讀 authored 基準 center。但 probe 在 `Core`、`MotionDriver._capsuleBaseCenter`
> > 在 `Presentation` ⇒ 不能直接問；候選 (a) probe 自己在 `Awake` 快照（多一個「基準」持有者）
> > (b) 走黑板欄位（單一真相，但要改 §1.1 權限表與 `WriterRules`）。
> > **這是 traversal 程式改動、traversal 仍 frozen ⇒ 等使用者裁決，本會話未動手。**
> >
> > 📌 本輪**未改任何 runtime 程式**（只新增一個測試檔＋改文件）。PlayMode **49/49**（48 ＋ 本探針）；recompile 0 errors。
>
> > ## ✅ 2026-09-15 **第三次更新：radius 已裁決為 0.32、已清理；`MaxOffset` 重新量測完畢**（正本：`docs/27` §10-0b ＋ **§11**）
> >
> > **① radius 裁決**：**0.32 ＝ authored baseline**（使用者）。SampleScene 那個未 commit 的
> > `0.12` 實例 override 已用 `PrefabUtility.RevertPropertyOverride` 清除，X Bot／Y Bot 現在都是 0.32。
> > `docs/27` 全篇 0.22 算術已改（§0／§1.5／§8.2／§9.5.1／§9.5.2／§10-2）。
> > ⚠️ **場景只改在 Unity 記憶體裡，尚未落盤 —— 需要使用者 Ctrl+S。**
> > 還原方式：Inspector 重新輸入 0.12。
> >
> > **② 重新量測**（`docs/27` §11；AnimationMode 121 幀 × 12 支 locomotion clip，preview scene，不碰使用者場景）。
> > 🔴 **踩到的坑**：humanoid clip 的 root motion **烘在 hips 裡**，不去趨勢會量到「Sprint 質心前移 4.16 m」。
> > 去趨勢要用**首尾質心差**，用 `clip.averageSpeed` 會殘留誤差。
> > 可信度：反推速度 1.64／3.58／6.26 對得上 1.62／3.50／6.10；Run／Sprint 質心 0.162／0.159
> > 與舊值 0.161／0.160 **差 1 mm ⇒ 重現前一輪量測**。
> >
> > | 相對 0.32 膠囊 | 結果 |
> > |---|---|
> > | **核心質心** | **從來沒有離開膠囊**。最糟 Sprint 0.212，離表面還有 **10.8 cm** |
> > | **頭** | 只有 Run（+0.020）／**Sprint（+0.085）**／RunStrafe45（+0.031／+0.002）超出 |
> > | **純側移** | `StrafeLeftLoop` 往左走、核心卻往**右**傾（cXavg +0.029）；`StrafeRightLoop` 也是反的 |
> >
> > **⇒ `MaxOffset = 0.12` 在 r = 0.32 下沒有任何量測支撐。**
> > 跟核心走 ⇒ 需要 **0**；改成追衝刺的頭 ⇒ 上界 **0.085**（且那是改變功能定義，撞上 §4.3「⛔ 不要用 max」）；
> > 而純側移時偏移會把膠囊推到**錯的一側**，誤差 ≈ 0.149 m，比它要修的 0.099 還大一個量級。
> > ⇒ **`X Bot` 目前關閉恰好是證據支持的狀態，本輪不改、也沒有重開。**
> >
> > **③ 下一步需要使用者裁決的三件事**（`docs/27` §11.4）：
> > (a) `Y Bot` 要不要跟著調（同 radius、同 MaxOffset，三條讀法一字不改適用）；
> > (b) 頭穿模三選一（加大 radius／加重 Head 權重／接受）——加大 radius 會再撞 traversal corridor；
> > (c) dynamic capsule 這個功能本身留不留。
> > 📌 **但 §10 第 0 條的 probe bug 無論如何都該修**，與 (c) 無關：那是 traversal 自己讀錯基準，
> > 任何第二個寫 `center` 的人都會再踩到。
>
> > ## 🔄 2026-09-15 **第四次更新：§11 的結論作廢；upper-body occupancy 研究完成**（正本：`docs/27` **§12**）
> >
> > 🔴 **使用者指出：以「核心質心是否在 radius 內」判定不需要 dynamic capsule 是錯的完成條件。**
> > Play 畫面明確看得到頭／上半身穿牆 ⇒ 需求客觀成立。`docs/27` §11 已標記作廢並記錄錯在哪。
> > ⛔ **後續不得再用核心質心當判準。**
> >
> > **錯在四件事**：①用平均回答外緣問題；②骨骼點 ≠ 網格（低估 3–10 倍）；
> > ③忽略膠囊在高處會變窄（頭高只有 0.306、顱頂只剩 0.067，不是 0.32）；④Play 證據優先於推導。
> >
> > **§12 改以蒙皮網格重測**（`BakeMesh` 28k 頂點，依 dominant bone 只留 Spine1/Spine2/Neck/Head，
> > ⛔ 排除手腳武器）：
> >
> > | | Spine1 | Spine2 | Neck | **Head** |
> > |---|---:|---:|---:|---:|
> > | RunFwd | 0.010 | 0.010 | 0.036 | **0.204** |
> > | **Sprint** | 0.029 | 0.042 | 0.092 | **0.271** |
> > | 純側移 | 0 | 0 | 0 | **0** |
> >
> > **求解法**（連續、非 argmax）：每個 landmark 配一個離線量的 flesh radius `r_i`
> > （Spine1 0.204／Spine2 0.187／Neck 0.095／Head 0.211），
> > 「球在膠囊內」⇔ `|q_i − c| ≤ ρ_i`，`ρ_i = sqrt((R−r_i)² − max(0, y_i−topC)²)`；
> > **`c*` ＝ 原點在 `∩D(q_i, ρ_i)` 的投影**。凸集投影對輸入連續 ⇒ 換 active constraint 不跳。
> > 實測 `dEst/frame ≤ 0.0124 m`（≈0.8 m/s），回頭驗真實網格**殘餘全為負**（完全覆蓋，餘裕 1.4–7.0 cm）。
> >
> > **需求量**：Walk 0／**Run 0.231**／**Sprint 0.296**／純側移 **0**／RunStrafe45 0.241（方向 Z:X = 2.7:1，**不是 45°**）。
> > ⇒ **現行模型兩端都錯且方向相反**：側移需要 0 卻推 0.12（還推向反側）；前跑需要 0.20–0.30 卻只給 0.12。
> >
> > **cap 取捨**（Sprint；膠囊整顆平移 ⇒ 底部支撐點同步前移）：
> > 0.12 → 殘餘 0.151／支撐前移 0.12；**0.18 → 殘餘 0.091**；0.30 → 殘餘 −0.023／支撐前移 0.296。
> > 📌 下半身外露**不是偏移造成的**（cap=0 就有 0.125），且 cap=0.06 反而讓它降到 0.076。
> >
> > **⇒ 建議 cap = 0.18**（拿掉 2/3 穿模，支撐前移還在一個腳掌量級），剩下的交給**膠囊高度／`center.y`**
> > 這個獨立槓桿——根因是膠囊頂端剛好切在頭頂。⛔ 那是另一個裁決，不在本輪。
> >
> > **⛔ 本輪只研究，未實作、未重開玩家 dynamic capsule、未改任何 runtime 程式。**
> > 需要裁決：(a) cap 取 0.12／0.18／0.30；(b) 要不要另開膠囊高度的題；
> > (c) `r_i` 寫死還是放 authored SO（目前 4 個數字量自 X Bot 網格，**換角色就不對**）。
> > 📌 實作前應先修 §10 第 0 條的 probe bug——偏移變大後它會壞得更明顯。
> >
> > 🟠 **順帶抓到一件要先確認的事**：`X Bot` 的膠囊半徑，prefab 是 **0.32**、
> > SampleScene 有一個**未 commit 的覆寫 0.12**、`docs/27` 全篇算術用的是 **0.22**，`Y Bot` 是 0.32。
> > `MaxOffset` 0.12 ＝ 整個半徑。**0.12 是刻意的還是誤觸？** 見 `docs/27` §10 第 0b 條。
>
> ## ✅ 測試狀態（2026-09-15 收工前已跑完）
>
> EditMode **519／518 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **48／48**；live Editor recompile 0 errors。
>
> ⚠️ **排測試前務必先確認 `scene.isDirty == false`。**
> dirty 時 EditMode test runner 會跳強制存檔對話框、**無限期卡死主執行緒**
> （本輪踩過一次，`cancel_tests` 也 timeout，只能重開 Editor）。
> dirty 就請使用者存檔，⛔ **不要自己存**——那是使用者的場景。
>
> ## 問題陳述
>
> **動態碰撞箱（capsule offset）與攀爬（traversal）在幾何上直接衝突，目前以「玩家關閉」暫時迴避。**
>
> | | 站位（root 到牆面） |
> |---|---|
> | 偏移關閉 | 膠囊前緣 ＝ `r` ＝ **0.22 m** |
> | 偏移開啟（滿值 0.12） | 膠囊前緣 ＝ **0.34 m** |
>
> ⇒ 角色停在**比原本遠 0.12 m** 的位置，而 `docs/24` §19 記載可執行起攀站位帶
> **只剩約 20 cm 寬** ⇒ **直接吃掉 60%**。按著 W ＝ 全速 ＝ 偏移滿值，正好是最糟情況。
> **Play 實測：按著 W 完全無法起攀。**
>
> 📌 **clamp 在這裡根本不會介入**：`CapsuleCast` 只有在 root 距牆 < `0.12 + 0.19 = 0.31 m`
> 時才撞得到牆，但角色在 0.34 m 就被 `CharacterController` 擋下了——永遠走不到 0.31。
> ⇒ **不是 clamp 沒做對，是「膠囊變大」與「起攀要夠近」本質衝突。**
>
> ## 目前狀態（不是待辦，是已生效的裁決）
>
> - `X Bot.capsuleOffset.Enabled = **false**`、`Y Bot = **true**`（敵人無 `TraversalProbe`，不受影響）
> - **Traversal 維持 frozen** ——⛔ 不為了 capsule offset 去改 `TraversalProbe` ／ entry policy
> - ⛔ **不刪除** solver／tests／gizmo。它們完好、有測試、可隨時重新啟用
> - `MaxOffset` 未動（0.12）
> - **dynamic capsule 本身沒有被否決**；暫停的只是 Player integration
>
> ## 🔴 下一個會話要小心的三個陷阱
>
> 1. **⛔ 不要以為換成 pose-driven 就解決了。** 實測跑步時核心質心前移 **0.161 m**，
>    比現在的 0.12 **更大** ⇒ 換模型只會讓衝突更嚴重。
>    **做之前必須先量「貼牆減速段」的質心軌跡**——角色撞牆前會減速，質心可能回收，
>    但那是假設，`docs/27` 沒有量過。
> 2. **⛔ 不要只改 `TraversalProbe` 去讀 base center。** 那只修一半：probe 讀對了，
>    但**物理站位仍然退後 0.12 m**，起攀照樣失敗。
> 3. **⛔ 不要把「頭穿模」當成同一個問題。** 實測衝刺時頭在核心質心前 **0.245 m**，
>    而膠囊半徑只有 0.22 ⇒ **即使把膠囊中心對齊質心，頭仍在外面 2.5 cm**。
>    那是獨立的取捨（加大半徑／加重 Head 權重／接受），而加大半徑**又會再吃掉起攀帶**。
>
> ## 三條可能的路（`docs/27` §9.5.3，都未裁決）
>
> | 路 | 內容 | 風險 |
> |---|---|---|
> | **A** | 等 traversal entry band 變寬到能吸收 0.12 m | 那是 traversal 自己的工作；`docs/24` §10–§11 已指出真正瓶頸是 `ForwardScanDistance 1.25 m` < 理想進場距離 1.402 m |
> | **B** | 為玩家單獨調低 `MaxOffset` 到 traversal 容忍得了的量 | 最省事，但要先量出「容忍得了的量」是多少；且偏移變小後效果可能看不出來 |
> | **C** | 改走 pose-driven 並確認貼牆時站位影響較小 | 見陷阱 1——**未經量測前不成立** |
>
> ## 先讀哪裡
>
> **`docs/27-pose-driven-capsule-study.md`** —— §1 是三個假設被實測推翻的完整數據、
> §9.5 是 clamp 的實作與這次衝突的機制、§10 是裁決紀錄與仍然開著的問題。
> ⛔ 在 §10 的三個問題有答案前，不要動手改 traversal。
>
> ## 順帶可用的工具（這幾輪剛做的）
>
> 進 Play → 點 Game view → **`** 或 **F1** 開 Debug Panel（F2 Capsule Offset／F3 Foot IK／
> F4 Traversal Runtime Lines／F5 Traversal Scene Gizmos；**預設全開**，面板是用來關掉的）。
> Traversal 的可視化現在有：掃描射線階梯、**以角色為中心的攀爬高度圓環**
> （黃＝Climb1m 上限、紅＝Climb2m 上限、灰＝ScanCeiling）、以及**線框球**標出
> 手抓點／牆面接觸點／該站的位置／人上去所需的空間。
> 📌 **`ForwardScanDistance` 現在畫得出來了**（腰高的琥珀色水平線）——
> `docs/24` §19 說它才是真正卡住玩家的東西，以前只存在於 Inspector 裡。

> # ✅ 2026-09-15 — **敵人血條／動態碰撞箱／Debug Panel**
>
> EditMode **519／518 passed／0 failed／1 skipped**；PlayMode **48／48**；recompile 0 errors。
> ✅ **人類 Play 驗收已通過**（2026-09-15 使用者）：敵人血條版面、debug 圖層皆無問題。
>
> 📌 收尾時三條測試紅燈，**三條都是治具的錯不是程式**，值得記下來：
> * `C11`：`CaptureCapsuleBaseCenter` 只擷取一次且在 `UpdateCapsuleOffset` 開頭，
>   測試卻**先把 center 弄髒才呼叫** ⇒ 擷取到的「authored 基準」是偏移後的位置。
> * `CO2`：用「跑 40 幀」＋絕對容差去驗 SmoothDamp 的漸近收斂 ⇒ **頻率相關**
>   （PD2 才剛因為同一類問題被修過）。改成依**時間**跑滿 5 個時間常數、比對峰值比例。
> * `CO3`：牆放在 `radius + 0.06`，但 clamp 用的是 `radius − skinWidth` 去掃
>   ⇒ 實際間隙 0.14 > 移動距離 0.12，**根本掃不到**。改用 `castRadius + 0.06`。
>
> ## ① 敵人頭上血條（`App/WorldSpaceHealthBar`）
>
> 可見性直接讀既有的 `CombatContext.InCombat` ⇒ **零新增狀態、零新增黑板欄位**。
> 敵人自己在順序 2.5 就在寫它，且已有黏性進出半徑（8m 進／12m 出）
> ⇒ **邊界不閃爍是免費附贈的**，不是另做的防抖。
>
> 🔴 **死亡也要隱藏，理由不明顯**：`AIMovementSource` **完全不認識死亡**
> （2026-09-14「屍體持續面向玩家」的同一個根因）⇒ 死掉的敵人**仍然是 `InCombat`**。
> 只看 `InCombat` 的話屍體頭上會掛著一條空血條。
> 📌 同一個教訓第二次出現：**「死了」不會自動傳播，每個讀者都要自己認識它。**
>
> 順帶抽出 `App/HealthBarFill`（第二個使用者到了）——抽它的理由不是省三行，
> 是那個**除零守衛**：`MaxHealth == 0` 除下去是 NaN，而 **Unity 不會為 NaN 的 `fillAmount` 報任何錯**。
>
> ## ② 動態碰撞箱：collision-aware clamp（見上方交辦）
>
> `SolveTargetOffset` → **`SolveDesiredOffset`**，`touchingWall` 參數移除 ⇒ 求解層完全不認識幾何體。
> 夾持改用 `Physics.CapsuleCast`，**cast 半徑刻意縮一個 `skinWidth`**
> （用原半徑掃，角色貼牆站定時起點就已重疊 ⇒ 回 `distance = 0` ⇒ 又變回舊行為）。
>
> ## ③ Runtime Debug Panel（`App/RuntimeDebugPanel`，掛在 X Bot 上）
>
> 🔴 **第一版用 `GUILayout.Toggle` 是錯的**：`CursorModeController` 把游標鎖住並隱藏
> ⇒ **勾選框根本點不到**。改成純鍵盤（` / F1 開關，F2–F5 切頻道）。
> 🔴 **第二個錯**：Capsule Offset 用 `Gizmos`／`Handles` 畫 ⇒ **Game 視窗看不到**。
> 改成 `LineRenderer`（與 Foot IK／Traversal 既有作法一致）。
>
> ⚠️ 過程中還抓到一個**只有 Development Player build 才會炸**的錯：插入位置把既有 Gizmos 程式碼
> 從 `#if UNITY_EDITOR` 併進了 `UNITY_EDITOR || DEVELOPMENT_BUILD`。
> **Editor recompile 與 `dotnet build` 都抓不到**（兩者都定義 `UNITY_EDITOR`）。已拆開。
>
> ## ④ Traversal 可視化：射線 ＋ 關鍵高度 ＋ 線框球
>
> 使用者要求「不需要文字，給我射線以及關鍵高度表示」。
> 第一版把高度畫成角色**側邊的短刻度** ⇒ 使用者回報「不知道那是什麼、為什麼在側邊」
> ⇒ 改成**以角色為中心的水平圓環**（套在身上才讀得出是高度門檻）。
> 偵測點改用**線框球**：手抓點（最大）／牆面接觸點／該站的位置／人上去所需的空間。
> ⛔ 全部取自 authored settings 與已算好的快照，**沒有新增任何 cast**（`docs/18` §1「記錄，不要重算」）。
>
> F6 文字頻道移除；`drawTraversalPanelText` 在面板掃描時被強制壓成 false。
> 所有 debug 預設開啟（**程式欄位初值與兩隻 prefab 的序列化值都要改**——
> 欄位初值不會覆蓋既有序列化值）。
>
> ## ⛔ 仍未做
>
> 1. ~~**人類 Play 驗收**：敵人血條位置／大小、debug 圖層讀不讀得懂~~
>    ⇒ ✅ **2026-09-15 使用者驗收通過，無問題**。本工作包的 ① ③ ④ 就此結案。
> 2. Ground Probe／Normal **刻意不做**，理由見 `docs/27` §9.7

> # 🎉 2026-09-15 — **第 3＋5 項完成：HUD（血條／冷卻）——六項全部結案**
>
> EditMode **509／508 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **45／45**；live Editor recompile 0 errors。
> **剩下的只有人眼**：版面位置、大小、顏色、看不看得懂。
>
> ## ⭐ 這一項真正的內容：`docs/17` §3.3 紅線 2 第一次被真實需求測試
>
> 血量在黑板上（ADR-009 D1 的共享真相）⇒ HUD 只是又一個讀者，沒問題。
> **但冷卻住在 `ActionState` 內部（ADR-004 D2），不在黑板上。**
> 最順手的做法就是加一個 `PlayerRuntimeData.Cooldowns[]` —— **那正是紅線 2 禁止的東西**。
>
> ✅ 實際採用：`CharacterPipelineRunner.GetActionCooldownNormalized(slot)`，
> 走紅線 2 自己點名的既有 pattern（`public InputDebugSnapshot InputDebug`）——
> Runner → FSM → `ActionState` 的**唯讀轉送**，不快取、不判斷、不記錄。
>
> 📌 **換算也留在擁有者那一側**：`CooldownVariance` 讓每次冷卻長度不同，
> HUD 若拿 authored 的 `Cooldown` 當分母，進度條會在**變異非零時**失準
> ——一種只在特定資產設定下才發作、看起來只是「進度條有點怪」的靜默錯誤。
> ⇒ `ActionState` 在 commit 當下記下**這一次實際用掉的長度**並回答 0–1（`T27` 釘住）。
>
> ## 🔴 順手關掉一扇剛被打開的門
>
> `Project.Runtime.asmdef` 為了 HUD 新增了 `UnityEngine.UI` 參考——那是**組件層級**的，
> 不處理的話整個 Runtime 從此都編得過 `Image`／`Canvas`。
> ⇒ 同一輪把 `UnityEngine.UI` 加進 `Core` 與 `Presentation` 的 `A4` Forbidden 清單。
> **加參考的同一刻就要關門**，否則畫面佈局的責任會慢慢滲進角色表現與 gameplay。
>
> ## HUD 為什麼住在 `App/` 而不是 `Presentation/`
>
> 不是品味，是**機器擋的**：`LayerRules` 禁 `Presentation` → `Project.Core.Pipeline`，
> 而 HUD 需要 Runner 才問得到冷卻。它也**不是** `IPresentationController`
> ——那支介面只收 `PlayerRuntimeData`，硬要塞就會逼出紅線 2 禁止的那個欄位。
> 📌 `docs/17` 紅線 3 早就預測了這件事，只是觸發它的不是 `StateType` 而是 `Pipeline`。
>
> ## v1 的兩個刻意省略
>
> * **無文字**：數字標籤每幀配置字串，是 HUD 最典型的 GC 來源，而本專案穩態 PlayerLoop 是 **0 B**
>   （changelog v0.24 實測）。v1 只有 `Image.fillAmount`（float 賦值，零配置）。
>   要加數字必須先準備無配置的 int→char 寫法。
> * **無圖示**（使用者裁決）：三格用色塊區分（素／橘／藍）。
>   📌 順帶查清楚：血條／冷卻圈**本來就不需要美術資源**，Unity 內建的 `UISprite`／`Knob` 就夠。
>   真正有美術需求的只有技能圖示，而它不在關鍵路徑上。
>
> ## 改了哪些資產（走 Editor API，可還原）
>
> `X Bot.prefab` 新增子物件 **`PlayerHud`**（Screen Space Overlay Canvas ＋ CanvasScaler 1920×1080；
> ⛔ 刻意不加 GraphicRaycaster —— HUD 不接受點擊，加了只會擋滑鼠射線）：
> 左下血條（`Filled/Horizontal`）＋ 下方置中三顆冷卻圓（`Filled/Radial360`，逆時針、從頂端開始）。
> 建構腳本是冪等的（先刪同名子物件再建），還原＝刪掉 `PlayerHud` 子物件。
>
> 📌 **HUD 掛在角色底下**，因此 `GetComponentInParent` 就解析得到 Runner ⇒ **零場景接線**，
> 也完全沒有動到 `SampleScene`。若日後要改成場景層級的 Canvas，`runner` 是序列化欄位，顯式指派即可，程式不必動。
>
> ## 新增測試（+9）
>
> `PlayerHudTests` 七條（含 🔴 **`MaxHealth == 0` 不得除以零** —— Unity 不會為 NaN 的 `fillAmount` 報任何錯，
> 畫面上只是「血條怪怪的」）／`ActionStateTests.T27`（冷卻進度的分母）／`PrefabWiringTests.W24`（HUD 接線四項）。
>
> ## ⛔ 仍未做
>
> 1. **人類 Play 驗收**（唯一剩下的事）：版面位置、大小、顏色、看不看得懂。
> 2. 無圖示、無數字、無敵人血條、無傷害數字、無 UI 動畫。
> 3. 冷卻圓的 `fillClockwise = false`（逆時針收攏）是猜的手感，看了不對就改那一個布林值。

> # ✅ 2026-09-14 — **第 6 項完成：重生**（正本：`docs/25` §8 ＋ **ADR-009 D3-R1**）
>
> EditMode **500／499 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **45／45**；live Editor recompile 0 errors。
> **剩下的只有人眼**：站得起來嗎、傳送位置對嗎、重生後還能正常打嗎。
>
> ## 🔴 這一項動到了一條已裁決的不變量，但**只動對應「死亡」的那一層**
>
> `DeathState` 的三層鎖裡**只有第 ② 層改了**：
>
> | 層 | 舊 | 新 |
> |---|---|---|
> | ① 進得去（`CanEnter` ＋ Priority） | `IsDead` | **一字未動** |
> | ② 出不來（自然過渡） | 恆 `false` | **`!IsDead`** |
> | ③ 出不來（被中斷 `CanBeInterruptedBy`） | 空 | **一字未動，仍然是空的** |
>
> **關鍵洞見**：原本的「出不來」其實一直是「**死著就出不來**」的簡寫——當時沒有任何東西
> 能讓 `IsDead` 變回 false，所以兩者等價。`Revive()` 存在之後兩者才分家。
> ⇒ 修訂後是 `CanEnter => IsDead` ／ `CanTransitionAway => !IsDead`，**進門與出門問同一個已 commit 的事實**。
> 跳躍／翻滾／出手／受擊／external request **仍然全都拉不出 Death**。
>
> **為什麼離開走「自然過渡」而不是「中斷」**：中斷的語意是「別的狀態比我更該發生、把我打斷」。
> 沒有任何狀態「打斷」死亡——是死亡這個前提本身消失了。所以第 ③ 層完全沒被碰過。
>
> ## authority ／ orchestration 分離（使用者裁決）
>
> | 角色 | 誰 | 不負責 |
> |---|---|---|
> | Survivability authority | `CharacterHealth.Revive()`（唯一能把 `IsDead` commit 回 false） | 重生點、傳送、FSM |
> | Position authority | `MotionDriver.Teleport()` | 何時搬、搬去哪 |
> | Orchestration | `RespawnController`（**自己零真相**） | 任何真相 |
>
> ⛔ 傳送**必須**經過 `MotionDriver`——它是 position 的單一寫入者（全專案沒有任何地方直接寫
> `transform.position`）。讓 controller 自己搬 Transform 會當場多出第二個寫入者。
>
> ## 🔴 為什麼重生鍵不能走 `InputData`
>
> `BlockInput` 的語意是「本幀管線看不到任何輸入」，Runner 在順序 2 直接把整份 `InputData`
> 歸零；而 `DeathArbiterSource` 正是在 `IsDead` 時抬起它 ⇒ **死著的時候收不到任何輸入，包括重生鍵。**
> ⇒ 比照 `GamePauseController`：自己持 `InputAction`、自己在 `Update` 讀。
> **重生與暫停同類——system-level 指令，不是角色的動作。**
>
> ## 順序：先傳送、再救活
>
> 反過來會留下**「活著、站在屍體原地、輸入已解封」**的一幀——足以讓玩家按出一個動作、
> 或讓敵人打中他，而他其實應該已經在重生點了。
>
> ## 🐛 首跑抓到一個真缺陷
>
> `MotionDriver.Teleport` 原本在 `characterController == null` 時 early-return
> ⇒ 退化配置會被**搬到新位置卻留著死前的垂直速度**，而清掉那個速度正是這個方法存在的理由之一。
> 已改為「只有停用／還原 CharacterController 那一步需要它，其餘清理無論如何都跑」，
> 由 `Teleport_WithoutCharacterController_StillResetsVerticalVelocity` 釘住。
>
> ## 改了哪些資產（走 Editor API，可還原）
>
> 1. **兩份 `StateMachineConfig`**：Death 的 `ValidTransitions` 由**空**改為 **[Idle, Move]**；
>    `CanBeInterruptedBy` 兩份都確認**仍為 0 筆**。⛔ 出口刻意不含 Action／Jump／Roll——
>    「重生瞬間就能出手」是沒人設計過的能力，而且看起來像 bug。
> 2. **`X Bot.prefab`**：根物件新增 `RespawnController`，`RespawnAction` 綁 **`<Keyboard>/r`**，
>    `health`／`motionDriver` 顯式指派，`respawnPoint` **留空**（＝退回場景開場姿態）。
>
> 還原＝清空那兩筆 `ValidTransitions`、刪掉那顆元件。
>
> ## 新增測試（+12）
>
> `HD7` 改寫為「**死著的時候**什麼都拉不出來」／`HD9`（復活同幀放行）／`HD10`（按著所有鍵復活仍先落 Idle）／
> `H11`–`H13`（Revive 三條，含「重生鍵不得變成活著時的免費補血鍵」）／`RespawnControllerTests` 六條／
> `W23`（重生接線三件事都不得缺）／`A38` 的 Death 出口斷言由「必須留空」改為「必須恰好是 Idle/Move」。
>
> ## ⛔ 仍未做
>
> 1. **人類 Play 驗收**（唯一剩下的事）。
> 2. **沒有 checkpoint 系統**：只有一個重生點，且目前用的是「場景開場姿態」的退路。
>    要顯式重生點就建一個空 GameObject 拖進 `respawnPoint` 欄位。
> 3. **沒有 encounter reset**：敵人不回血、不歸位。那是未來獨立的 **Restart Encounter** 功能。
> 4. **不清除暫時效果**：死前中的 Slow 會帶到重生後直到過期。已知邊界。
> 5. **沒有 `RespawnState`**：等到重生真的需要有持續時間的 phase（起身動畫、無敵窗）再考慮升格。
> 6. 六項中只剩 **3＋5（血條／冷卻 UI）**。

> # ✅ 2026-09-14 — **第 4 項完成：武器只在攻擊時顯現**（正本：`docs/11` 2026-09-14 段）
>
> EditMode **488／487 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **45／45**；live Editor recompile 0 errors。
>
> ## 🔴 本項的真正內容不是武器，是一條被誤當成設計的限制
>
> 掛點確實是既有的 `IActionLifecycleSink`（`Begin` 顯現／`Cleanup` 收起）——
> **但它當時是 per-slot 單顆的**，而 Slot1 已經被 `MeleeHitboxSink` 佔走
> （`ResolveActionLifecycleSinks` 明文把「同 slot 綁兩顆」當成 `LogError`，`W3` 還把它釘住了）。
>
> **使用者裁決：改成一個 slot 可以有多顆 sink。**
> 理由是 seam 的真實語意本來就是「這個 Action 的副作用」，而一個 Action 天生可以同時有
> 命中判定、武器顯隱、刀光、VFX、音效——它們共用同一組時點，卻沒有理由互相認識。
> ⛔ 明確否決 composite sink（把多接收者問題藏到另一層）與
> ⛔ 讓 `MeleeHitboxSink` 兼管 `WeaponSocket`（把 gameplay 與 presentation 綁死）。
>
> | | 內容 |
> |---|---|
> | **資料結構** | 稀疏陣列 → **jagged** `IActionLifecycleSink[][]`（外層＝slot） |
> | **順序** | ＝ Inspector binding 順序，且穩定（除錯時人會照著清單讀 log） |
> | **仍禁止** | 同一顆 sink 在同一個 slot 註冊兩次（症狀：命中判定跑兩次 ⇒ 傷害變兩倍） |
> | **合法** | 同一顆 sink 綁到**不同** slot ⇒ 比較的是「(slot, sink 實例)」組合 |
> | **相容** | 既有單顆 binding 與 legacy 單顆欄位一字不必改 |
> | **零 GC** | 全部配置在組裝期；派送是對具體陣列的索引迴圈 |
>
> **`IActionLifecycleSink` 介面本身一字未改**——變的只有「一個 slot 有幾個接收者」。
> 因此**不開 ADR**（`IActionLifecycleSink` 不在 CLAUDE.md 列舉的核心驅動介面內），走 Living Docs。
>
> ## 武器顯隱（v1 ＝ 瞬間顯隱，不是 dissolve）
>
> 新增 `Presentation/Equipment/WeaponVisibilitySink`：`Begin` 顯現／`Release` **刻意不動**
> （那是命中的瞬間，與武器該不該看得見無關）／`Cleanup` 收起。
> `Cleanup` 在自然播完與被中斷**兩條路徑**都會被呼叫 ⇒ 不會「被打斷後劍卡在手上」。
>
> 📌 **可見狀態存在 `WeaponSocket` 裡，不在 sink 裡**——因為 `Rebuild()` 會銷毀重建實例
> （`Awake`，以及在 Inspector 右鍵微調掛載位移時）。狀態若存在呼叫端，隱藏中的武器會在**調整位移的那一刻**
> 突然冒出來，而且只在調位移時發作。順帶也解掉了兩顆元件的 `Awake` 順序不可依賴的問題。
>
> ⚠️ **dissolve 未做**（使用者裁決 v1 只做「何時」）。要換成溶解只需替換
> `WeaponVisibilitySink.ApplyVisibility` 一個方法，時點與接線都不必動。
> Sword 是第三方資產（CLAUDE.md 禁改）⇒ 需要另建 dissolve 材質並在執行期換上，屬材質／VFX 工作。
>
> ## 改了哪些資產（走 Unity Editor API，可還原）
>
> `Assets/Prefabs/X Bot.prefab`：在 `X Bot` 根物件上**新增一顆 `WeaponVisibilitySink`**
> （`weaponSocket` 指向同物件的 `WeaponSocket`、`visibleWhenIdle = false`），
> 並在 Runner 的 `Action Sink Bindings` **新增一列 `Slot 1 → WeaponVisibilitySink`**。
> 最終清單：`[Slot1→MeleeHitboxSink] [Slot2→ThrowProjectileEmitter] [Slot3→GroundEffectSink] [Slot1→WeaponVisibilitySink]`。
> 還原＝刪掉那一列 binding 與那顆元件。
>
> ## 新增測試（+15）
>
> `ActionStateTests.T26`（多 sink 派送與順序）／`ActionSinkResolutionTests`（6 條：組裝期解析、
> 交錯 slot、重複拒絕、跨 slot 共用、無效列不位移、legacy 相容）／`WeaponSocketTests`（6 條顯隱行為，
> 含「`Rebuild` 不得靜默變回可見」）／`PrefabWiringTests.W22`（sink 必須解析得到掛點且必須被綁到 slot）。
>
> ## ⛔ 仍未做
>
> 1. **人類 Play 驗收**：劍出現／消失的時機看起來對嗎？揮劍時劍在手上嗎？放火球時劍應該不出現。
> 2. shader dissolve（見上）。
> 3. 六項中的 **3／5（UI）與 6（重生）未動**，定位資料仍在下方原始清單裡。

> # ✅ 2026-09-14 — **六項中的第 2、第 1 已完成**（戰鬥手感切片）
>
> EditMode **473／472 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **45／45**；live Editor recompile 0 errors。
> **剩下的只有人眼的部分** —— 敵人會不會真的打到人、屍體停住的朝向好不好看。
>
> ## 第 2 項：攻擊距離死區 —— 契約是**等號**，不是區間
>
> 🔴 **本輪最重要的發現：專案早就有一條反向的不變量，而它只守了一半。**
> `PrefabWiringTests.W10` 原本斷言 `attackRange ≤ maximumEngagementDistance`
> （2026-09-05 的「還在 Approach 就揮拳」）。加上本輪要的
> `attackRange ≥ maximumEngagementDistance`（否則有死區）之後，
> **兩個必要條件的交集只有相等** ⇒ W10 改為要求兩者是同一個數字。
> ⇒ 沒有新增 W21；**把新的一半折進既有的 W10**，一條契約只留一個地方。
>
> ⚠️ 相等仍有殘留重疊：最後 `distanceHysteresis` 那段（`max − deadZone` → `max`）
> 敵人是 Approach 且已在攻擊圈內，會邊走邊揮一小段。要消除就得再製造死區
> ⇒ **兩害相權取表現瑕疵，不取 gameplay 死局**（已寫進 `docs/21` §1.3）。
>
> **數值取自物理，不是手感猜測**：`MeleeHitbox` 是 root 空間 `(0,1,0.9)`、size `(0.9,1.2,1.2)`
> 的 box ⇒ 前緣 z = **1.5 m**；玩家 CharacterController 半徑 0.12 ⇒ 實際可及約 **1.62 m**。
> 故 Y Bot 兩個欄位同取 **1.6**（敵人停在 `1.6 − 0.15 = 1.45 m`，仍在觸及範圍內）。
>
> ## 第 1 項：屍體轉向 —— 死亡是 facing 的**否決層**
>
> 依使用者裁決採 (a)：`CharacterFacingSource.Tick` 讀 `Survivability.IsDead`，
> 為真時不論哪個優先序解出什麼都不送 request。不送 ⇒ MotionDriver 維持既有 rotation
> （Priority 4 既有語意）⇒ **沒有新增任何「凍結朝向」機制**。
>
> ⚠️ 記下這個教訓：**`DeathArbiterSource` 的 `BlockInput` 只擋得住「輸入產生的意圖」。**
> facing 的第二優先序讀 `CombatContext`，由 `AIMovementSource` 在順序 2.5 直接寫黑板，
> 不經過輸入 ⇒ 封鎖輸入對它完全無效。**日後任何「死後還在動」的症狀都要先問這一句。**
>
> ## 🔴 順帶修掉一個既有的假紅燈（不是本輪造成的）
>
> PlayMode `PD2_DeathState_StillFalls` 的第二條斷言寫成 `VerticalVelocity < -0.5f`，
> 那**正是它自己上面那段註解禁止的形狀**——一個偽裝成速度的時間門檻（需要約 0.05 s 累積）。
> Editor 跑得快時 10 幀只有 ~0.042 s ⇒ 實測 -0.41、**穩定紅燈**（連跑兩次都一樣，不是 flake）。
> 已改為**單調遞減**斷言（第 2 幀 → 第 10 幀必須更負），完全不依賴幀長。
> 📌 順帶釐清：`SyncGroundedState` 刻意在**積分之前**發布 `data.VerticalVelocity`
> ⇒ 黑板上的值恆落後一幀，**第 1 幀必定是 0**，取樣要從第 2 幀開始。
>
> ## 改了哪些資產（皆走 Unity Editor API，可還原）
>
> `Assets/Prefabs/Y Bot.prefab` 兩個序列化 float：
> `AIMovementSource.maximumEngagementDistance` **2 → 1.6**、
> `AIInputSource.attackRange` **1.8 → 1.6**。
> 還原＝把這兩個值改回去即可（無結構變更；`git diff` 該檔的其餘 193 行是**上一輪**
> 加 `MeleeHitbox` 的未 commit 變更，不是本輪產生的）。
>
> ## ⛔ 仍未做
>
> 1. **人類 Play 驗收**：敵人現在會不會真的打到人？停下來的距離手感如何？
>    屍體停住時的朝向自然嗎？（1.6 m 是從碰撞體幾何推出來的**起點**，不是定案手感值。）
> 2. 六項中的 **3／4／5／6 未動**，定位資料仍在下方原始清單裡。

> # 📋 2026-09-14 — **使用者 Play 後點名的六項**（下一輪處理；本輪只記錄與定位，未改任何東西）
>
> 依 Hurt／Death 垂直切片完成後的實際遊玩體驗提出。**排序是使用者給的原始順序，不是優先級。**
> 下面每一項都附本輪查到的定位，⛔ **但沒有一項做過完整診斷**——動手前仍要自己確認。
>
> ## ~~1. 敵人死亡後屍體會不斷面向玩家~~ ✅ 2026-09-14 已完成（採 (a)，見本段上方的完成紀錄）
>
> 🔍 **已定位，根因清楚**：`CharacterFacingSource`（順序 4.6）**完全不認識死亡**
> （`grep "IsDead\|Survivability\|Death"` 在該檔零命中），每幀照常送 facing request 給 `MotionDriver`。
> `DeathArbiterSource` 封鎖的是 **`BlockInput`**，不是 facing。
> 而 facing 的來源之一 `CombatContext` 由 `AIMovementSource`（順序 2.5）產生——**它也不認識死亡**。
>
> ⇒ 有兩個候選修法層，**要先裁決**：
> * (a) `CharacterFacingSource` 讀 `Survivability.IsDead` 後不送 request（最靠近症狀）
> * (b) 死亡時停止產生 `CombatContext`（更上游，但會連帶影響別的 consumer）
>
> ## ~~2. 敵人攻擊距離比迂迴距離短，玩家得自己往前靠~~ ✅ 2026-09-14 已完成（契約改為等號，見本段上方的完成紀錄）
>
> 🔍 **已定位，是數值帶重疊**（`Y Bot.prefab` 實測值）：
>
> | 欄位 | 值 |
> |---|---|
> | `AIMovementSource.minimumEngagementDistance` | 1.25 |
> | `AIMovementSource.maximumEngagementDistance` | **2.0** |
> | `AIInputSource.attackRange` | **1.8** |
>
> ⇒ 敵人在 **1.25–2.0 m** 這個帶內會判定「距離剛好」而 `Hold`／`Strafe` 不再前進，
> 但只有 **≤1.8 m** 才會送出攻擊 request ⇒ **1.8–2.0 m 之間是一個「站得住但打不到」的死區**。
> ⚠️ 不是只改一個數字就好：`distanceHysteresis = 0.15` 會讓實際停留點再浮動，
> 兩者的關係應該是**契約**（攻擊範圍必須涵蓋整個 hold 帶），值得用測試釘住而不是手調。
>
> ## ~~3. 沒有血條　／　5. 技能冷卻要 UI 顯示~~ ✅ 2026-09-15 已完成（uGUI；見本段上方的完成紀錄）
>
> 🔍 **這兩項是同一件事：專案目前完全沒有 UI 層。**
> `grep -rln "Canvas\|UnityEngine.UI\|UIDocument" Assets/Scripts/` 只命中 `GamePauseController`。
> ⚠️ **這是本清單裡唯一會長成新子系統的項目**，需要先決定 uGUI vs UI Toolkit、
> 以及「表現層怎麼讀角色資料」的邊界（`IPresentationController` 是讀黑板的既有 seam）。
>
> ✅ **好消息是資料端已經備好，不需要為 UI 新增黑板欄位**：
> * 血量：`data.Survivability.CurrentHealth` / `MaxHealth`（已在黑板）
> * 冷卻：`ActionState.GetCooldownRemaining(slot)` **已經是 public**，其註解原文就是
>   「供測試與**（未來）HUD** 讀取」
>
> ⛔ 依 `docs/17` 的 Observability 紅線：**不得為了顯示新增黑板欄位。**
>
> ## ~~4. 武器要攻擊時才顯現，其他狀態消融~~ ✅ 2026-09-14 顯隱已完成（**dissolve 未做**，見本段上方的完成紀錄）
>
> 🔍 `Presentation/Equipment/WeaponSocket`（`00-map` 標註「**只做視覺附著，不是裝備系統**」），
> 有 `weaponPrefab`／`socket`／`Rebuild()`。
> 顯隱時機的既有掛點是 **`IActionLifecycleSink`**（`Begin`／`Release`／`Cleanup`）——
> 近戰與投射物都走它，不必新建 seam。
> ⚠️ 「消融」若是指 shader dissolve，那是材質／VFX 工作，與顯隱時機是兩件事，建議分開評估。
>
> ## ~~6. 需要重生按鈕~~ ✅ 2026-09-14 已完成（走 ADR-009 **D3-R1 修訂**，不開新 ADR；見本段上方的完成紀錄）
>
> ⚠️ **這一項會動到已裁決的不變量，不是單純加功能。**
> `DeathState` 目前是**吸收態**，而且「出不去」是**刻意用三層鎖＋測試釘住的**
> （`CanTransitionAway=false` ＋ 空的 `CanBeInterruptedBy`／`ValidTransitions`；`HD7`／`A38` 守）。
> 重生 ＝ 明確地重新開放那道門 ⇒ 屬於**決策變更**，要先想清楚「誰有權把角色從 Death 拉出來」，
> 並同步更新 `HD7`／`A38`／dev-spec §3.3 的 Death 列，⛔ 不要靠繞過測試達成。
>
> ---
> ~~**建議順序**（僅供參考，使用者未指定）：2 → 1 是純數值／小改且直接改善戰鬥手感；~~
> ~~4 次之；6 需要一次裁決；3＋5 最大，建議獨立成一個 slice。~~
>
> ~~**2026-09-14 更新**：2 與 1 已完成。**剩餘順序建議：4 → 6 → 3＋5。**~~
> ~~4 走既有 `IActionLifecycleSink` 掛點，不需新 seam；6 需要一次裁決~~
> ~~（誰有權把角色拉出 Death）；3＋5 會長出新子系統，建議獨立成一個 slice。~~
>
> ~~**2026-09-14 再更新**：2、1、4 已完成（4 的 dissolve 除外）。~~
> ~~**剩餘：6（重生按鈕）與 3＋5（血條／冷卻 UI）。**~~
>
> ~~**2026-09-14 三更**：**2、1、4、6 皆已完成**（4 的 shader dissolve 除外）。~~
> ~~**六項中只剩 3＋5（血條／冷卻 UI）。**~~
>
> # 🎉 **2026-09-15 結案：六項全部完成。**
>
> 唯一未做的子項是 **4 的 shader dissolve**（使用者裁決 v1 只做「何時顯隱」）。
> **所有項目的共同剩餘工作＝人類 Play 驗收**，各段完成紀錄裡都列了各自要看什麼。
>
> 📌 **兩件仍然成立的順手事**（都不必新增任何 seam）：
> ① UI 上要加「重生」按鈕就直接綁 `RespawnController.Respawn()`（已是 public，就是為此開的）；
> ② 武器 dissolve 只需替換 `WeaponVisibilitySink.ApplyVisibility` 一個方法。
>
> 📌 **HUD 之後若要長大**（敵人血條、傷害數字、buff 列）先讀 `docs/17` §3.3 的三條紅線
> ——第 2 條與第 3 條在這一輪都被真實需求測試過，實證紀錄就寫在那裡。

> # ✅ 2026-09-14 — **Hurt ／ Death 垂直切片完成，遊戲內可用**（正本：`docs/26` §K）
>
> EditMode **471／470 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **45／45**；live Editor recompile 0 errors。
> **剩下的只有人眼的部分** —— 動畫好不好看、0.7 s 硬直手感、死亡姿勢。
>
> ## 用了哪些素材
>
> | 用途 | Clip | 長度 |
> |---|---|---|
> | Hurt | `Fists_Hit_Right`（`MovementAnimsetPro_Fighting.fbx`） | **0.700 s**（由 1.167 s trim） |
> | Death | **`Death_1`**（同一支 FBX） | 2.467 s |
>
> 專案內窮舉 325 支 clip 才找到 `Death_1`／`Death_2`（檔名完全看不出來——
> memory `kubold-fbx-name-does-not-predict-content` 再次應驗）。選 `Death_1`：較短、位移較小。
> Player／Enemy **共用同一支**，沒有建角色專屬 Death framework。
>
> ## 🔴 本輪最重要的發現：**Hurt 的 bake 不能拿來驅動 gameplay**
>
> `Fists_Hit_Right` 原生帶 **1.15 m 後退**，`Bake_Fists_Hit_Right` 也忠實烘了進去（峰值 **2.27 m/s**）。
> 但烘焙器用 `Vector3.Distance` 取速度 ⇒ **無號**，而 `ExecuteBakedCurveMovement` 沿 `transform.forward`
> 積分 ⇒ **一支「往後踉蹌」的動畫會把受擊者往前推進攻擊者**。方向錯、量級也錯。
>
> 修法（⛔ 沒有在 state 裡乘 offset）：匯入設定改 **Bake Into Pose**（視覺保留、膠囊不動）＋
> `HurtState`／`DeathState` 改走**既有的** `ExecuteVerticalOnlyMovement` ＋ bake **只剩提供時長**一個職責。
>
> ⚠️ 順帶釐清一個反直覺事實：**`lockRootPositionXZ` 不影響 `SampleAnimation` 讀到的 root 曲線**
> ⇒ 烘焙器仍會量到原始位移。「Bake Into Pose」修的是**視覺**，不是 bake 內容。已寫進 dev-spec §0.4 新增列。
>
> ## 🔴 順帶修掉的三個既有靜默缺陷
>
> 1. **玩家從來沒有過受擊反應** —— `DamageDefinition` 只掛在敵人 config，`X Bot` 的 37 筆 mapping 也沒有受擊動畫。
> 2. **敵人的拳頭從來不造成傷害** —— `Y Bot` 完全沒有 `MeleeHitboxSink`、binding 是空的、`EmitsRelease=false`。
> 3. **兩隻角色都沒掛 `CharacterHealth`／`DeathArbiterSource`**（上一輪列為人工項但未執行）。
>
> ⇒ 現在由 **`W18`**（每隻角色都要能受傷與死亡）、**`W19`**（Hurt／Death 動畫鍵解析得出來）、
> **`W20`**（Hurt 時長來自 bake，不是程式常數）釘住。
>
> ## 第一版數值（全部 authored）
>
> Player HP 100／Enemy HP 100／近戰 25（雙方）／Fireball 20／Slow 投射物 15／Ice AoE 15
> ⇒ **雙方都是 4 下致死**。
>
> ## ⚰️ `DamageDefinition.asset` 已刪除
>
> 刪前確認 `grep -rl <GUID>` 在 `Assets/` **零引用**。`A37` 已由「只允許一個已知孤兒」**收緊為零容忍**。
> `ActionSlot.Reaction = 100` enum 成員與數值**仍保留**（ADR-005：淘汰某一格要留著它的數值）。
>
> ## ⛔ 仍未做
>
> 1. **人類 Play 驗收**（`docs/26` §K.7）——這是唯一剩下的事。
> 2. **屍體 mesh 會離膠囊約 1 m**（`Death_1` 的姿勢位移）。膠囊不動（`PD1` 釘住），
>    但貼牆死亡時 mesh 有機會穿模。第一版接受。
> 3. **沒有屍體清理／respawn**：`DeathState` 是吸收態，死了就永久停在那裡。
> 4. `docs/15` §16-7（target validity 改讀 `IsDead`）依裁決**未做**，維持開啟。


> # ✅ 2026-09-14 — **Model B migration 完成：受擊不再是 Action**（正本：`docs/26` §J）
>
> EditMode **468／467 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **41／41**；live Editor recompile 0 errors。**⛔ 人類 Play 驗收尚未做。**
>
> ## 做了什麼
>
> `ActionSlot.Reaction` 退役 → 受擊成為 **`StateType.Hurt = 8`**，
> 由 **`SurvivabilityData.JustTookDamage`** 驅動（與 `DeathState ← IsDead` 完全對稱）。
> `ActionState` 裡**兩處「如果是 Reaction 就反過來」全部刪除**。
> `HurtState` 形狀照抄 `RollState`——共用底層 animation/motion 執行層，不共用 Action semantic。
>
> ## 🔴 盤點時發現的既有缺陷（不是本輪造成的，已順帶修掉）
>
> **玩家在此之前從來不會有受擊反應。** `DamageDefinition` 只掛在**敵人** config；
> `X Bot.prefab` 的 37 筆 transition mapping 裡也**沒有任何受擊動畫**。
> 而且不會有任何錯誤訊息——因為 `GetActionDefinition(Reaction)` 回 null 就靜默拒絕。
>
> ## 資產改動（全走 Editor API，⛔ 未手改 YAML；還原方式見 `docs/26` §J.2）
>
> 兩份 config 的 rules 全面重寫 ＋ `bakeMappings` 加 `Hurt → Bake_Fists_Hit_Right`；
> 敵人 config 移除 `DamageDefinition`；**`EnemyPunchDefinition.Interruptible: false → true`**；
> `Y Bot` 的 mapping `"Damage" → "Hurt"`；`X Bot` **新增** mapping `"Hurt" → Damage.asset`。
>
> ## ⛔ 三個仍開著的項目
>
> 1. **沒有 Death 動畫** —— `Assets/ScriptableObjects/Animation/` 裡沒有任何 Death transition。
>    `Play("Death")` 會 LogWarning 並 return，**角色仍然是死的**（authority 與 playback 已解耦），
>    只是維持上一個姿勢。**選哪一支 clip 是 authoring 決定，未代為挑選。**
> 2. **`DamageDefinition.asset` 仍在磁碟上**（Slot 仍是 100，但沒有任何 config 引用它，
>    內容已全數遷移）。刪除屬破壞性動作 ⇒ **等使用者裁決**；在那之前由 `A37` 指名釘住。
> 3. **`docs/15` §16-7（target validity 改讀 `IsDead`）刻意未做** ——
>    使用者明確要求不把 targeting scope 拉進本輪。前置條件已解除，等單獨排程。
>
> ## 新增的兩條架構不變量
>
> - **`A37`** —— runtime 不得引用退役的 `ActionSlot.Reaction`；Slot-100 資產只允許一個**指名的**孤兒；
>   **任何 config 都不得把它接回 `actionDefinitions`**（那才是會讓 Action 路徑復活的動作）。
> - **`A38`** —— 讀**出貨 config** 驗 permission 矩陣。
>   ⭐ 特別守住「**Roll／Traversal 擋 Hurt 必須由 `CanBeInterruptedBy` 表示，不得靠 priority 代替**」
>   （`HD8` 另從行為面驗：把 Hurt priority 拉到 999 仍然進不了 Roll）。


> # 🟠 2026-09-14 — **Hurt／Death ＋ 動態 Collider：程式全部落地，停在 Integration Gate**
>
> 正本：**`docs/25`**（§7＝實作狀態）＋ **`docs/ADR/009`（🟡 Trial）**。
> 三個 assembly `dotnet build` **0 errors**（Runtime／Editor／Tests.EditMode）。
>
> ## 🔴 兩件必須先知道的事
>
> 1. **EditMode 測試尚未跑過。** 本輪後段 live Editor 主執行緒無回應——
>    `recompile`／`recompile_status`／`console` 連續 timeout 超過 5 分鐘（`unity status` 仍顯示 ready，
>    那只代表 socket 在聽）。**開場第一件事：確認 Editor 可用後跑完整 EditMode。**
> 2. **沒有做任何資產改動 ⇒ Stage B 在遊戲裡完全不會發生。**
>    `DeathState` 沒有 config 規則就永遠進不去。這正是 `docs/24` §14.2 記過的
>    「有程式、有測試、但遊戲裡從來沒跑過」——**不要跳過下面的清單就去 Play。**
>
> ## 盤點的三個關鍵結論（決定了「要新增的東西比預期少很多」）
>
> 1. **受擊反應已經是一個 Action** —— `ActionSlot.Reaction = 100` ＋ `DamageDefinition.asset`。
>    ⇒ **沒有新增 Hurt state。**
> 2. **「這招不可被打斷」的 policy 欄位已經存在** —— `ActionPhaseEntry.Interruptible`。
>    ⇒ 未來 Super Armor **零新程式**，就是把那格設 false。
> 3. 🔴 **敵人攻擊打不斷的根因是 `EnemyPunchDefinition.Phases[0].Interruptible: 0`** ——
>    一個布林值，不是程式問題。
>
> ## 使用者裁決（`docs/25` §5）
>
> - **D1**：collider 跟隨**當下真正具有位移 authority 的 motion source**；v1 只做 locomotion
>   （世界 `MoveDirection` → Root local）。⛔ 不用 `transform.forward`；
>   ⛔ 實際位移只能當診斷／fallback（撞牆時趨近 0，會在最需要時失去方向）。
> - **D2**：**玩家攻擊可否被受擊打斷 —— 這輪不動**，玩家側既有值一個都沒碰。
> - **D3**：黑板 `Survivability` ＋ `CharacterHealth` 單一寫入者；最小欄位
>   `CurrentHealth`／`MaxHealth`／`IsDead`，⛔ 不放 hit direction／damage source／invulnerability。
>
> ## 🔧 Integration Gate —— 人工 Editor 清單（一次做完）
>
> ### A. 資產（走 Editor API 或 Inspector，⛔ 不得手改 YAML）
>
> | # | 資產 | 改什麼 | 為什麼 |
> |---|---|---|---|
> | A1 | `PlayerStateMachineConfig.asset` | 新增 `State: 7`（Death）規則：`Priority: 100`、`CanBeInterruptedBy` **留空**、`ValidTransitions` **留空** | 沒有這條，`DeathState` 永遠進不去 |
> | A2 | 同上 | 把 Death(7) **加進** Idle(1)／Move(2)／Jump(3)／Roll(4)／Action(5)／Traversal(6) 的 `CanBeInterruptedBy` | Jump／Roll／Traversal 目前是**空清單** ⇒ 死亡打不斷它們 |
> | A3 | `EnemyStateMachineConfig.asset` | 同 A1＋A2（該檔只有 State 1–5，沒有 Traversal） | 敵人也要會死 |
> | A4 | 🔴 `EnemyPunchDefinition.asset` | `Phases[0].Interruptible: 0 → **1**` | **這是「敵人攻擊被打斷」的全部**。還原＝設回 0 |
> | A5 | `X Bot.prefab` ／ `Y Bot.prefab` | 掛 **`CharacterHealth`**（Root，與 `ActionRequestTarget` 同物件）＋ **`DeathArbiterSource`**（Root）；設 `maxHealth` | 缺 `CharacterHealth` ⇒ 該角色不會受傷也不會死（`Survivability` 恆為 default，行為與導入前相同） |
> | A6 | 三個攻擊 sink 所在的 prefab | 確認新欄位 `damage`（預設 10）合理 | 數值在資產不在程式 |
> | A7 | Animancer | 需要 **`Death`** 這個 animation key 的 transition（`DeathState.AnimationKey` 預設 `"Death"`） | 沒有它 Death 進得去但沒有動畫。素材見 memory `kubold-action-catalog` |
>
> ### B. 測試（live Editor）
>
> `unity cmd recompile` → 輪詢 `recompile_status` 到 completed → `unity cmd run_tests --mode EditMode`。
> 預期新增 `SurvivabilityTests`（H1–H9）與 `CapsuleOffsetTests`（C1–C9）；
> `ActionStateTests.T14/T24`、`SlowEffectTests` 的 GroundEffect 那條已改讀新 seam。
>
> ### C. Play 驗收（ADR-009 §5 的 AC5／AC6）
>
> - 敵人攻擊中被打 ⇒ **攻擊中斷並播受擊**（不是把攻擊播完）。
> - 致死 ⇒ 倒地，之後**不再回到 locomotion 或 attack**。
>
> ### D. Collider（**預設停用**，要測才開）
>
> `MotionDriver` 的 `Capsule Offset → Enabled` 打勾。七項驗收情境在 `docs/25` §5 D1：
> forward／backward／左右 strafe／diagonal／**敵人持續面向玩家但側移繞圈**／停止回 neutral／
> **貼牆移動不得更容易卡住**。Gizmo 四色向量：藍＝facing、青＝MoveDirection、
> 黃＝target offset、洋紅＝current offset。
>
> ## ⚠️ 實作中發現、已處理的一個真實衝突
>
> `CharacterController.center` 現在有**兩個**執行期寫入者（traversal profile ＋ 動態偏移）。
> `BeginTraversalCollisionProfile` 若在偏移非零時擷取基準，整段 traversal 會建立在偏掉的基準上，
> 而 `RestoreTraversalCollisionProfile` 之後會把那個偏移「還原」成**永久值**。
> ⇒ 已在該方法開頭加 `ResetCapsuleOffsetImmediate()`。**這不是保險，是必要條件。**
>
> ## 另一個逼出來的設計決定
>
> 「死後不產生 intent」**不能**寫在輸入層：`A27` 明文禁止 `AIInputSource` 出現 `PlayerRuntimeData`。
> ⇒ 改用 `DeathArbiterSource`（`IArbiterSource` → `BlockInput`）。
> `ArbiterPipeline` 的檔頭本來就寫著「不知道有死亡……新增封鎖來源＝實作介面掛上階層，管線零改動」——
> **既有不變量把答案逼出來了，不是我選的。**

> # 🟡 2026-09-14 — **新工作包：Hurt／Death ＋ 動態 Collider**（正本：`docs/25`）；盤點完成，**待裁決 D1–D3**
>
> Traversal 已暫停（見下一段）。本工作包**尚未寫任何程式**。
>
> ## 盤點的三個關鍵結論（決定了「要新增的東西比預期少很多」）
>
> 1. **受擊反應已經是一個 Action** —— `ActionSlot.Reaction = 100` ＋ `DamageDefinition.asset`
>    （`AnimationKey: Damage`，`Interruptible: 1`），由 `MeleeHitboxSink`／`ThrownProjectile`／
>    `GroundEffectSink` 經 `ActionRequestTarget.RequestAction` 投遞。⇒ **不需要新的 Hurt state。**
> 2. **「這招不可被打斷」的 policy 欄位已經存在** —— `ActionPhaseEntry.Interruptible`（per-phase，authored）。
>    `CanBeInterruptedBy`（跨 StateType）與 `CanReenter`（Action→Action，含 Reaction）**都讀它**。
>    ⇒ 使用者問的「未來 Super Armor 要在哪定義」**已經有答案，零新程式**。
> 3. 🔴 **敵人攻擊打不斷的根因是一個既有資產欄位** —— `EnemyPunchDefinition.Phases[0].Interruptible: 0`
>    ⇒ `CanReenter` 第一關就 return false ⇒ Reaction 永遠重入不了。**改這個布林值即可。**
>
> ## 真正缺的東西（只有這些）
>
> `Health`／`Damage` 量值／`StateType.Death`（值 7）／`DeathState`／死亡的 **lethal 中斷授權**
>（`Interruptible: false` 的 Action 目前會連 Death 一起擋掉）。
> Collider 部分：`MotionDriver` **已有** runtime center-shift 通道（`TraversalCollisionProfile`，
> traversal 用、目前三個 binding 全關），本輪是替它接上第二個驅動來源。
>
> ## ⚠️ 實作前必須知道的風險
>
> `TraversalProbe` **逐幀讀** `CharacterController` 的 `center`／`radius`／`height`（14 處）
> ⇒ 動態 center 會直接改變起攀合法性與 `EntryCapsuleWallClearance`。
> **traversal committed 期間必須強制權重 0**，且不得與 traversal profile 同時寫 `center`。
> 其餘五條副作用（牆面無 sweep／斜坡失去接地／轉向掃弧／停止歸零／空中）見 `docs/25` §3.3。
>
> ## 下一步
>
> **`docs/25` §5 的 D1–D3 裁決** → Stage A（Survivability 骨架）→ Stage B（Death ＋ 中斷授權）
> → Stage C（Collider v1）。Stage D（per-animation capsule）與 traversal 共用同一個欄位，
> **建議在 traversal 解除暫停前不做**。

> # 🛑 2026-09-14 — **Traversal 暫停修改；已完成 Regression Diagnosis**（正本：`docs/24` §19）
>
> 使用者裁決：**先暫停繼續修改攀爬系統**，本輪只做診斷，**不新增 case／offset／fallback、不調參數、不重寫**。
> 診斷已完成且**未改任何 `.cs`／`.asset`／`.prefab`／`.unity`／`.meta`**，也沒跑 git。
>
> ## 開場先知道這四件事（其餘細節在 `docs/24` §19，不要重讀 §13–§18）
>
> 1. 🔴 **整個 traversal 子系統沒有任何 git 還原點。** 18 個 `.cs`、9 個測試、3 支 bake `.asset`、
>    `TraversalStateParams.asset`、`X Bot.prefab` 接線——全部 `??` untracked 或未 commit 的 `M`；
>    四個分支都不含 traversal 檔案，stash 空。**⇒「哪一次改壞的」沒有 commit 可以指，
>    也沒有東西可以 revert。** 這是「越改越差卻說不出是哪一次」的結構性原因。
>    **E0：請使用者先把目前整包（含 untracked）commit 起來，之後每個實驗才有 diff 可看。**
> 2. ⭐ **最大嫌疑不是 §15–§18，是更早的 piecewise。** piecewise 把全部 correction 綁在
>    `LeftContact` 那一格；Climb2m 的 contact 在**第 7 格（0.233 s）**，endpoint 窗則是 **1.62 s**
>    ⇒ **壓縮 7 倍**。實測站位 0.23 m ⇒ 0.232 m 要在 0.233 s 內吃掉 ＝ 峰值 **1.49 m/s**，
>    是該動畫自身速度（0.187 m/s）的 **8 倍**。Vault 的 contact 在第 18 格，所以沒事。
> 3. ⚠️ **影片上的 `R 19.0 cm` 推不出來。** 從 bake 的兩個 grip edge ＋ 場景箱子的實際四元數推導，
>    這段程式在水平邊緣上只能產出 **1.93 cm**（與 §16 記的 19.29 mm 完全吻合），而且**與站位無關**。
>    ⇒ 疑點在 **committed `TraversalLedgeFrame`（感測）**，不在 IK。
>    而 `TraversalDebugWindow` **剛好不顯示 ledge frame** —— 唯一能分辨的證據沒被印出來。
> 4. ⛔ **在 E1／E5 拿到結果之前，不要再動 IK／錨定／primary-vs-dual。** §15–§18 四輪都是在
>    沒有這兩項證據的情況下調錨定，結果是使用者說「比我提出反饋時還爛」。
>
> ## 下一輪只做這兩件事（都只需要一次 Play）
>
> - **E1**：`TraversalDebugWindow` 加一段**唯讀**顯示 committed ledge frame
>   （`EdgeOrigin／Tangent／TangentMin/Max／TopNormal／WallNormal`）。
> - **E5**：Play 時確認 `Hand IK:` 那行是不是 `Active`（§14.2 的歷史就是「有程式、有測試、遊戲裡沒跑過」）。
>
> 之後才輪到 A/B 對照：**E2** `Bake_Climb2m.Traversal.enabled = false`（一鍵關掉 piecewise＋HandIK＋
> Exit 凍結＋reboundForce 四層，回到 1.62 s 的 endpoint 窗）、**E3** `enableHandIK = false`、
> **E4** `entryPolicy.maximumCloseError` 0.4 → 0.10。全部可逆，還原方式在 §19.10。
>
> ## 順手查到、之前沒人記過的事實
>
> - `Bake_Climb2m.rootReachConstraint` 是 `enabled: 1` 但**三條曲線 109 格全為 0** ⇒ 這一層目前**完全沒作用**。
>   §18「RootReachConstraint 已壓住伸展」的前提**對現在的資產不成立**，別再沿用。
> - 三個 binding 的 `collisionProfile` **全部關閉、曲線皆空** ⇒ 「Capsule 穿模」目前**沒有任何機制在處理**。
> - `Bake_Climb1m` **整支沒有 `Traversal:` 區塊**（唯一沒被重推的 bake）。
>   目前 `climb1mEnabled: 0` 所以不影響玩，但打開就會靜默走 endpoint、沒有 Hand IK。
>
> ---
> <details><summary>以下為上一輪（退版回 §16）的交辦，保留備查</summary>

> # 🔴 2026-09-14 — **已退版回 §16**；Playtest 影片推翻了前兩輪的前提
>
> ## ⚠️ 開場先做兩件事
>
> 1. **補跑 EditMode 測試並確認全綠。** 上一輪退版後 PlayMode **41/41 通過**，
>    但 EditMode 在交接時仍在執行、**結果未確認**。（同一輪還踩了「EditMode 沒跑完就排 PlayMode／
>    排 eval_file」的坑，導致一個 eval timeout——見 `.claude/skills/unity-live-control`。）
> 2. **讀 `docs/24` §13–§18 之前先知道：§17／§18 已經被退掉**，那兩節描述的東西**不在程式裡**。
>
> ## 退了什麼、現在是什麼狀態
>
> 使用者原話：「**這修的比我提出前面那段反饋時還爛**」。已回退到他給排序回饋時的 §16 狀態：
>
> | 項目 | 現況 |
> |---|---|
> | `TraversalHandIKController` | 已移除 anchor blend／grip latch／surface tracking，**恢復固定腕點目標** |
> | `TraversalWarpPlan` | 已移除 `TraversalContactSurface` 與 `LeftWristToContact`／`RightWristToContact` |
> | `TraversalPlanBuilder` | 已移除對應接線 |
> | `Bake_Climb2m` IK 窗 | 已重烘回 **L `0.212963` / R `0.203704`** |
> | `Bake_Vault1m` | 未變動 |
> | 測試 | `B11`／`B15` 恢復原斷言；`SURF1`／`SURF2` 已移除 |
> | 編譯 | live Editor 0 errors |
>
> ⚠️ `TraversalContactDerivation` 的 release 改成顯式的 `KinematicRelease()`
>（各手自己的 `FixedGoalExtensionStart − fade`）。這是**為了重現 §16 的數值**而寫的，
> 行為與 §16 相同，但寫法與當時不同——重烘時要注意。
>
> ## 🔴 影片給出的兩個事實（前幾輪的量測完全沒涵蓋）
>
> 使用者提供 `C:\Users\USER\Videos\2026-09-14 06-50-22.mp4`。
> **可以用 ffmpeg 抽影格當圖片讀**（技法已寫進 `.claude/skills/unity-live-control`）。
>
> **① 實際起攀站位是 0.17–0.23 m，不是理想的 0.462 m。**
> 面板讀數：`2 Entry ACCEPT — AlreadyClose / wall 0.17 m (legal 0.06–0.81)`，另一次 `wall 0.23 m`；
> `4 Fit: approach animated 0.000 m → required −0.230 m`。
> ⇒ **前幾輪所有離線量測都做在 0.462 m 上，量的不是使用者在玩的情境。**
> 這是方法錯誤，不是參數沒調好。**以後任何 traversal 的驗收都要掃站位範圍，不能只量理想點。**
>
> **② 右手根本沒抓到邊緣。**
> `Execution: hand residual  L 0.0 cm   R 19.0 cm`，畫面上**左手扣在頂面、右手攤在立面上**。
> L=0.0 是因為 root 就是解來服務 primary（左手）的（§16），誤差整包丟給 secondary。
> `docs/24` §16 記的是「secondary 19.29 mm」——那是**理想站位**下的值；實際站位是 **19 cm**。
>
> ## 未完成的量測（下一步的關鍵）
>
> 腳本已存進 repo：**`docs/artifacts/residual_sweep.cs.txt`**（本輪 timeout，未取得結果）。
> 用法：複製到暫存 `.cs` 後 `unity cmd eval_file --file <path>`。
> 內容：**0.15 → 0.80 m 每 5 cm，記錄兩手各自的 contact residual 與 root correction**。
> 它要回答的問題：那 19 cm 是「這個站位特有」還是「§16 之後全距離都這樣」——
> **兩者要修的地方完全不同**：
> - 若全距離都大 ⇒ §16「root 只服務 primary、誤差全給 secondary」這個決定要重新評估。
> - 若只在近站位大 ⇒ 問題在 entry band 允許 0.06 m 起攀，而動畫要 0.462 m。
>
> ⛔ **在拿到這份資料之前不要再改 IK。** 前三輪都是在沒有實際站位資料的情況下調錨定方式，
> 結果是「數字一直變好、使用者體感沒變、最後變更差」。
>
> ## 使用者的品質排序（仍然有效）
>
> 1. **手掌固定到 ledge 的可信度** ← 最大破綻，即上面那兩點
> 2. 翻越結尾 → 站立收斂：已量，**root 本身平滑**；「被接管」感來自 **Foot IK 的離散啟用**（`docs/24` §17.5 的量測仍成立）
> 3. **攀爬起手／貼牆入口**：設計題，未動
> 4. Capsule 穿模：非優先
>
> ## 🔴 仍未驗證的前提
>
> **沒有人確認過執行期 `TraversalHandIKController.Status` 真的是 `Active`。**
> 影片面板顯示的 `hand residual` 來自 MotionDriver 的 commit-time 量測，**不是** IK 的執行狀態。
> 這個子系統的歷史正是「有程式、有測試、但遊戲裡從來沒跑過」（`docs/24` §14.2）。
> Play 時請確認，或在 `TraversalDebugWindow` 補一行顯示 controller 的 `Status`。

</details>

> # 🔴 2026-09-14 — **錨定的是接觸點，不是手腕**（正本 `docs/24` §18）；⚠️ Hand IK 是否真的在跑仍未驗證
>
> ## 為什麼前幾輪都沒解決
>
> 使用者在 §17 之後仍回報「**仍不像固定點**」。數字都對（錨定延長到骨盆越過平台、reach ≤0.957），
> 畫面還是不對 ⇒ **量錯了東西**。
>
> `AvatarIKGoal.LeftHand` 的 goal 是**手腕**，我們 commit 的也是固定的**手腕**點。
> 但抓握期間手掌會轉 **61.4°**，手腕到接觸點約 14.5 cm ⇒
> **手腕完美釘死時，指尖仍在邊緣上掃過 15.2 cm**——是 §14–§17 一路在修的漂移量的 2–7 倍。
> **前幾輪一直在把手腕釘得更準，但釘的本來就不是該固定的東西。**
>
> ## 修法
>
> 把「手腕→接觸點」偏移換算到**手骨座標系**（抓住時 latch 一次朝向），用當下手部旋轉還原：
> `wristTarget(n) = anchoredContact − handRotation(n) * offsetInHand`
> ⇒ 手掌翻轉時手腕讓位、接觸點留在原地。同時把退化起點從 reach 0.90 移到 **0.98**
>（`RootReachConstraint` 已壓住伸展，從 0.90 退化等於在撐起段砍掉一半權重）。
>
> | | 舊（釘手腕） | 新（釘接觸點） |
> |---|---|---|
> | reach 峰值 | 0.950 | **0.874** |
> | 接觸點最大偏移 | **15.2 cm** | **0（建構保證）** |
> | 抓握窗內錨定權重 | 受 reach 衰減 | **全程 1.0** |
>
> ⭐ 反直覺但合理：**釘接觸點比釘手腕更省手臂**。§15 為了不超伸而提前放手、並因此失去錨定，
> 其實是釘錯目標造成的假兩難。
>
> ## 🔴 最重要的未驗證前提
>
> **從來沒有人確認過執行期 `TraversalHandIKController.Status` 真的是 `Active`。**
> 所有量測都是離線重建 plan 的結果，而這個子系統的歷史正是
> 「有程式、有測試、但遊戲裡從來沒跑過」（`docs/24` §14.2 一次列出三個死路徑）。
>
> **Play 時第一件事：開 `Window ▸ Project ▸ Traversal Debug`，看 Hand IK 那一行是不是 `Active`。**
> 若是 `NotBound`／`AwaitingPose`／`RootMappingUnavailable`／`OutsideContactWindow`／`ResidualTooLarge`，
> 那才是真正的問題，§14–§18 的數字都還沒機會生效。
>
> ## 驗證
>
> **EditMode 447／446 passed／0 failed／1 existing skipped**、**PlayMode 41／41**。
> 本輪**未改任何資產**（只改執行期的目標解析）。⛔ 未執行 Git。
>
> ## 使用者排序中未動的項目
>
> 2. 翻越結尾收斂：已量，**root 本來就平滑**；「被接管」感來自 **Foot IK 的離散啟用**（§17.5）。下一輪修接手時機，不要動 warp。
> 3. **攀爬起手／貼牆入口**：設計題，需先定運動邏輯。
> 4. Capsule 穿模：非優先。

> # 🟠 2026-09-14 — 手掌錨定改成「面約束」（正本 `docs/24` §17），待 Play 驗收
>
> ## 使用者回饋的排序（本輪只做第 1 項）
>
> 1. **手掌固定到 ledge 的可信度** ← 本輪
> 2. 翻越結尾 → 站立的收斂：已量，**root 本來就平滑**，問題在別處（見下）
> 3. **攀爬起手／貼牆入口**：未動，是設計題
> 4. Capsule 穿模：非優先
>
> ## 做了什麼
>
> 量到的破綻：**IK 權重在 n=0.24 就歸零，那時 root 距平台頂面還有 0.485 m** ——
> 「手撐起身體」的後半段完全沒有錨定。放手的理由是放開後 reach ratio 會衝到 1.05+（手臂被拉直）。
>
> 語意修正：**contact 是面約束，不是點約束。** 真實 mantle 的手掌會沿頂面滾動，
> 釘死一個點必然超過手臂長度。改成依 reach ratio（0.90→1.00）在
> 「錨定固定點」與「貼在已驗證邊緣面上的動畫手腕」之間**連續混合**，
> 並新增 `TraversalContactSurface`（純資料）把「不得穿進頂面／不得滑出已驗證區間」變成執行期保證。
> 同時把 IK 窗還原成動畫量到的抓握結束（`Bake_Climb2m` L 0.2130→**0.2685**、R 0.2037→**0.2593**）
> —— **這一步才讓 §16 的 `RootReachConstraint` 真正開始工作**（手在 0.213 就放開時它無事可做）。
>
> | | 前 | 後 |
> |---|---|---|
> | 權重歸零時 root 距頂面 | −0.485 m | **−0.156 m** |
> | 歸零時骨盆 | 平台下方 0.270 m | **已越過平台 +0.002 m** |
> | 錨定期間最大 reach ratio | 1.048 | **0.957** |
>
> ## 第 2 項的結論：不要再動 warp
>
> 逐格一階差分：`drootY` 平滑收斂，**n≈0.81 時 rootDy 已經是 +0.000**，沒有 snap 也沒有突變點。
> ⇒ 「被接管」**不是 root warp 造成的**。最可能是 **Foot IK 的啟用是離散事件** ——
> `isGrounded` 要等 exit(0.8333) 之後的貼地力才變 true，Foot IK 才突然接手 clip 尾段那 1.1 cm 姿勢差。
> **下一輪修接手時機與斜坡，不要動 warp。**
>
> ## 已知殘留
>
> - 錨定退場時 `drootY` 在 n≈0.38 有約 **0.5 cm/step** 的小凸起（reach 約束淡出的導數不連續，**本輪新引入**）。
> - 第 3 項攀爬入口未動。
>
> ## 驗證
>
> **EditMode 447／446 passed／0 failed／1 existing skipped**、**PlayMode 41／41**。
> 新增 `SURF1`／`SURF2`；`B15` 改守「窗必須覆蓋整個抓握期」；
> `B11` 改守「抓握期間除了宣告過的 reach 約束外，不得有第二個東西推身體」。
> 資產：`Bake_Climb2m` 的 IK 窗（還原法見 `docs/24` §17.7）。⛔ 未執行 Git。
>
> ## Play 驗收
>
> 1. 撐起身體時**手看起來有沒有像固定點**（本輪的目標）。
> 2. 手肘有沒有被拉直／手臂扭曲（量測說 reach ≤ 0.957，應該沒有）。
> 3. 結尾收斂有沒有變差（本輪沒動那段）。

> # 🟠 2026-09-14 — Traversal 工作包二完成；待 Climb2m Play 品質驗收（正本 `docs/24` §16）
>
> ## A：先量後決定不再調窗
>
> 用 production `Bake_Climb2m` ＋ `X Bot` preview rig 逐格量測。以同法 goal-envelope proxy 比較，
> §15 的 kinematic release 已把左手最大 reach ratio **1.1019 → 0.8706**、reach>1 格數
> **3 → 0**；右手現為 **0.8849／0 格**。1.1019 不取代 §15 full-chain 的實測峰值 **1.086**。
> release 段仍有 L/R **10.2／3.7 mm 每格** goal 混合增量與 **63.9／69.5°/s** 方向角速度增量，
> 但 authored 動畫本身峰值就是 **403.6／436.0°/s**，且 envelope 已是 smoothstep。
> 判定剩餘量不是再挪窗能乾淨消除，依工作包指示直接處理 contact 結構；**Bake IK window 未再修改**。
>
> ## B：Primary root ＋ Secondary surface residual
>
> `TraversalPlanBuilder` 已移除雙手 absolute root 平均與 `SolveDual`：`BothHands` 固定 Left primary，
> root correction 只由 primary grip contact 決定；secondary 在 primary root 決定後查 Probe 已提交的
> `TraversalLedgeFrame`／safety interval，成功才發布 wrist target 給 Hand IK。窄 ledge 無 secondary
> surface 時保留 authored pose並停用該手 IK，不再壓縮雙手間距。
>
> Climb2m 理想進場同資料：舊平均 L/R residual **9.64／9.64 mm**；新解 primary L **0 mm**、
> secondary R **19.29 mm**（明確交給 IK）；contact→transfer correction 漂移仍 **0 mm**。
> `FingerContactClearance`／grip edge／wrist↔grip 仍只屬 animation/contact fitting，不是 Probe geometry；
> Plan／IK 沒有新增 Physics query。
>
> ## C：架構圖與驗證
>
> `docs/artifacts/architecture-tour.html` 已新增圖 19：
> `Probe → Selection → Animation Fitting → TraversalPlan → Piecewise Root Warp → Contact → Hand IK`，
> 並更新頁尾 Traversal Trial 基線。實際檔案沿用既有 inline SVG／notes 體例（原檔沒有 Mermaid runtime）。
>
> 新增 `HC6`、更新 `HC4／M6`，`A47` 禁止恢復 `SolveDual`。
> **focused 24／24；EditMode 445 total／444 passed／0 failed／1 existing skipped；PlayMode 41／41。**
> 原本唯一紅燈 M6 已關閉，沒有新紅燈。
>
> ## 資產／還原／Play
>
> 本工作包**沒有修改任何 `.asset／.prefab／.unity／.meta`**，因此 B/C 無資產還原步驟。
> `Bake_Climb2m` 仍是 L `[0.037037,0.064815,0.212963]`、R `[0.037037,0.064815,0.203704]`；
> 若要還原上一輪 release，必須走 Unity Editor API 設回 §15 記錄的舊窗，不可手改 YAML。
> ⚠️ 撐起自然度只有 Unity 匯入／離線數值量測，沒有自動化視覺測試保護，仍待人類 Play。

> # 🟠 2026-09-13 — Climb2m「手把身體撐起來」修正，待 Play 驗收（正本 `docs/24` §15）
>
> 使用者確認結尾浮空已消失，只剩撐起階段的手臂視覺問題。本輪用真實旋轉 2 m 箱、
> 近面射線、場景 `X Bot` probe settings 與 preview-scene `X Bot` rig 逐格量測。
>
> ## 根因（不是 root）
>
> - contact→transfer root correction 漂移 **0.000 cm**；root freeze 正常。
> - root 上升 **1.691 m** 時，動畫腕部本來會移動 L **11.52 cm**／R **11.03 cm**。
> - 舊 IK 卻把腕部固定到 plant end：n=0.2593 左臂 reach ratio **1.030**，
>   肘角從 authored 130° 被拉到 **180°**；n=0.2685 reach **1.086**。
>
> ## 最小修正
>
> `TraversalContactDerivation` 對雙手支撐另外量
> 「fixed contact wrist 開始連續要求比 authored wrist 更長肩—腕距離」，
> 讓既有 0.1 s fade 在該事件前歸零。Transfer marker／root mapping／runtime 架構不變；
> 單手 Vault、Climb1m、FBX、Animancer 都沒動。
>
> `Bake_Climb2m`（Editor API、X Bot）：
> L release **0.268519→0.212963**（0.240741 歸零）；
> R release **0.259259→0.203704**（0.231481 歸零）。
>
> ## 驗證／還原
>
> 新增 B15；**EditMode 444／442 passed／1 known M6 failed／1 skipped**；
> **PlayMode 41／41**。要還原舊行為，經 Unity Editor API 把兩窗設回
> L `[0.037037,0.064815,0.268519]`、R `[0.037037,0.064815,0.259259]`、rotation 0；
> 不可手改 YAML。B15 保護數值與幾何事件，但畫面自然度沒有自動化視覺測試，仍待 Play。
>
> ## Play 只看兩件
>
> 1. 接觸後把身體撐起來時，肩／肘是否不再被拉直或扭曲。
> 2. 已通過的結尾貼地是否仍維持（不得重新出現浮空／延後落地聲）。
>
> # 🟠 2026-09-13 — **Phase 3 Hand IK 接上了**（正本 `docs/24` §14），待 Play 驗收
>
> ## ⚠️ 先更正：玩家是 `X Bot`，不是 `Y Bot`
>
> 場景有兩個角色：**`X Bot` ＝ `PlayerInputSource` ＋ `TraversalProbe`，layer Player ⇒ 玩家**；
> `Y Bot` ＝ `AIInputSource`，layer Enemy。
> 我用 `FindAnyObjectByType<CharacterController>()` 拿到 `Y Bot` 就當成玩家、沒驗證，
> 把兩支 bake 重推到敵人 rig 上（已全部改回 `X Bot`）。
> ⛔ **`docs/24` §13.3 說「出貨資料用錯 rig」是錯的**——原本就是 `X Bot`，是我改壞又改回。
> `TraversalIntegrationWiringTests` 早就寫著 `PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab"`，
> **repo 裡已經有答案，我沒去看。**
>
> ## Hand IK 為什麼一直是死路徑（三個原因，全部已修）
>
> 1. **掛不上去**：兩個 MonoBehaviour 是 `FootIKController.cs`／`FootIKRig.cs` 的**次要類別**
>    ⇒ Unity 無法 AddComponent。已拆成 `TraversalHandIKController.cs`／`TraversalHandIKRig.cs`。
> 2. **bake 沒授權**：`handIKEnabled=0`、窗全 0 ⇒ 停在 `NotConfigured`。
>    已由 `TraversalContactDerivation` 自動寫入（窗＝測出來的 plant 換算，`rotationWeight=0` 只釘位置）。
> 3. **prefab 沒掛**：已掛上 `X Bot`（controller 在 root、rig 在 `Model`），`motionDriver` 已接。
>
> 另修掉一個設計缺陷：IK 淡出終點原本綁在 Exit marker，`Climb2m` 因此淡出長達 2 秒
> （手放開後還被黏著往上拖）。改成淡出與淡入等長。
>
> ## 實測（真實 2 m 箱、理想站位、玩家 rig）
>
> 抓握期間手自己會漂 **1.9 – 7.0 cm**（以前沒人管，手就鑽進牆裡）；
> IK 生效期間**最大殘差 0.166 m < 放棄門檻 0.35 m ⇒ IK 會接手**。
> 權重曲線與抓握窗吻合（0.042 淡入 → 0.083–0.250 滿權重 → 0.292 淡出）。
>
> ## 資產改動（都走 Editor API）
>
> | 資產 | 改了什麼 | 還原 |
> |---|---|---|
> | `Bake_Climb2m` / `Bake_Vault1m` | grip edge Y ＋ Hand IK 窗 | 重跑 `TraversalContactDerivation.Apply(bake, XBotPrefab, …)` |
> | `X Bot.prefab` | 新增 Hand IK controller ＋ rig | 移除這兩個元件 |
> | `SampleScene.unity` | `X Bot` 的 `WeaponSocket.weaponPrefab` ← null（拿掉擋視野的劍） | 把 `Sword` prefab 指回去 |
>
> ## 驗證
>
> **EditMode 443／441 passed／1 failed（`M6`，工作包二）／1 existing skipped**、**PlayMode 41／41**。
> 新增 `PlayerPrefab_TraversalHandIK_IsAttachedWiredAndAuthored`（守住「掛了、接了、bake 授權了」），
> `A49` 改讀新檔並釘住「必須是自己檔案的主類別」。
>
> ## 下一步：Play 驗收
>
> 爬 2 m 箱，看 ①抓握期間手還會不會鑽進牆 ②手臂會不會因為 IK 被拉扭 ③結尾那一下浮空還在不在。
>
> # 🟠 2026-09-13 — Playtest 點名的**兩個可見缺陷已修**，待 Play 驗收（正本 `docs/24` §13）
>
> ## 使用者的原話
>
> 「工作包一改了什麼**我沒體驗到**。我看不順眼的只有：**上去的瞬間手指插進牆面**、
> **動畫結束會有微小浮空**（有落地聲佐證）。」
> ⚠️ 記住這條回饋：**改動的價值由「看不看得到」決定，不是由「數字有沒有變好」決定。**
>
> ## ① 手指插進牆面 —— grip edge 的 Y 基準取錯
>
> 原本取「動畫自己的障礙物頂面」，但實測四根手指的**指尖**騎在那條平面上下各約 1 cm，
> 同時越過牆面 3–5 cm（扣過邊緣是對的）⇒ 指尖落在轉角實心裡。
> 改成由**真正壓在頂面上的四根指尖**取最低者，再讓出 `FingerContactClearance = 0.01 m`。
> 結果：四指從 −0.013…+0.009 變成 **+0.010…+0.032**（全部浮在頂面上）。回歸測試 `B14`。
>
> ## ② 結尾浮空 ＋ 落地聲 —— 同一個原因
>
> Exit 之後 plan 位置凍結 ⇒ `Move(Vector3.zero)` **不回報觸地** ⇒ `isGrounded` 全程 false
> ⇒ `FootIKController`（閘門就是 `IsGrounded`）**整段關閉**；
> 而 clip 最後一格不是站定姿勢（腳趾比 Idle 高 **1.2 cm**）⇒ 浮空；
> 狀態結束重力恢復才觸地 ⇒ `JustLanded` ⇒ 落地聲。
> 改成 Exit 之後沿用 locomotion 的 `reboundForce = −2` 貼地力，讓角色真的坐上去、
> grounded 由物理回答，Foot IK 在 traversal 內就接手。
>
> ## ⚠️ 過程中的事故（已修正，但要知道）
>
> 我先用 `Y Bot` 重烘 `Bake_Vault1m`，把接觸窗寫壞（0.207→0.356、reach −0.430＝手在身後）。查出兩件事：
>
> 1. **`X Bot` 與 `Y Bot` 的手不一樣**：同一格指尖差 **3.7 cm 高、3 cm 前**。
>    場景用的是 **`Y Bot`** ⇒ **推導必須用 `Y Bot`**，否則 grip edge 一開始就偏。
> 2. **接觸偵測對 rig 過度敏感**：原本只取「最長低速段」，低速 ≠ 接觸。
>    已加物理合理性過濾——**手在身體後方的低速段不可能是抓握**（`ConsiderPlantRun`）。
>    加上之後兩個 rig 對兩支 clip 結果完全一致。
>
> ⚠️ **`Bake_Climb2m`／`Bake_Vault1m` 已用 `Y Bot` 重新推導並寫入**（Editor API，未手改 YAML）。
> 這兩個 `.asset` **未進版控**（`??`）⇒ **沒有 git 還原路徑**，要還原只能重跑推導
> （現在是可重現的：`TraversalContactDerivation.Apply(bake, YBotPrefab, out report)`）。
>
> ## 下一步：Play 驗收（只看這兩件）
>
> 1. 爬上 2 m 箱子時，**抓握瞬間手指是不是貼在頂面上**而不是插進去。
> 2. 動畫結束時**還有沒有那一下浮空／落地聲**。
>    （落地聲可能改成在角色真的坐上平台的那一刻響——那是對的時機；若你覺得爬牆不該有落地聲，再說。）
>
> ## 驗證狀態
>
> live Editor recompile 0 errors；**EditMode 442／440 passed／1 failed／1 existing skipped**、
> **PlayMode 41／41**。唯一紅燈 `M6` 屬工作包二。⛔ 未執行 Git。
>
> # 🟢 2026-09-13 — Traversal **工作包一完成**（感測範圍推導 ＋ Animation Fitting v1），待 Play 驗收
>
> **正本：`docs/24` §10–§12（落地紀錄）、`docs/22` §19（摘要）。**
> 鏈路定案：`Probe → Selection → Animation Fitting → TraversalPlan → Root Warp → Contact → IK`
>
> ## 做了什麼（三項）
>
> 1. **感測範圍改由動畫需求推導**，不是換一個 magic number。
>    `TraversalStateParamsSO.GetRequiredSensingReach()` ＝
>    `max(啟用 kind 的推導進場距離 + MaximumFarError) / cos(MaximumFacingAngle)`；
>    `TraversalState` 推給 `TraversalProbe.SetDerivedRangeRequirements()`，Probe 取 `max(authored, derived)`。
>    **1.25 → 2.138 m。**
> 2. **落地預算與分類證據分離**：`Vault1mMaxDepth`(0.6) 只留給分類；
>    新增 `TraversalCandidate.LandingSurfaceDepth`，Probe 沿真正的落腳面續掃
>    （Climb 走平台頂面、Vault 走牆另一側地面）。
> 3. **Animation Fitting v1**（`Core/StateMachine/TraversalAnimationFit.cs`，純函式）：
>    `rate = 動畫助跑 / 需求助跑`，夾 [0.6, 1.6]，由 `AnimationFacadeBase.SetPlaybackSpeed`
>    （新增 virtual，預設 no-op）套用，`OnExit` 設回 1。
>    ＋ 修掉 `TryGetNaturalExitDepth` 量錯時間點的舊 bug（量在 clip 結尾 → 改量在 Exit marker）。
>
> ## 實測（離線重建，真實場景＋真實資產）
>
> | | 前 | 後 |
> |---|---|---|
> | Vault 可執行站位 | 1.10–1.25（**20 cm**） | **1.00–1.70 m** |
> | 理想站位・水平 exit correction | 0.765 m | **0.001 m** |
> | 理想站位・水平 contact correction | — | 0.015 m |
>
> ## ⚠️ 下一步：**Play 驗收**（唯一未完成項）
>
> 請在 1 m 薄牆前**退開約 1.2–1.5 m** 按 Jump（貼牆仍會 `TooCloseUnsafe`，那是預期的）。
> 看四件事：①能不能穩定觸發 ②助跑速度看起來自不自然（rate 在動）
> ③放手後有沒有還在往前滑 ④`Window ▸ Project ▸ Traversal Debug` 的 `4 Fit` 那一行有沒有數字。
>
> **已知會看到、但不屬本工作包的**：矮牆上 Vault 的**垂直** correction 固定 −0.33 m
> （`Vault1m` 是對 ~1.02 m 障礙物做的，那面牆實高 0.699 m）⇒ 需要 clip selection，已延後。
> 另外 exit 之後 root 會被凍結、run-out 在原地播完（既有行為，`docs/24` §12 列為待評估）。
>
> ## 驗證狀態
>
> live Editor recompile 0 errors；**EditMode 441／439 passed／1 failed／1 existing skipped**、
> **PlayMode 41／41**。唯一紅燈 `M6_NarrowLedge_ShrinksAndClampsHandSeparation`
> **屬工作包二**（使用者已指定在那裡連同 contact 語意一起修）。
> ⛔ 未執行 Git；未改任何 `.asset`／`.prefab`／`.unity`（新欄位靠 C# 欄位初始式生效，
> 實測 `animationFit enabled=True / [0.60, 1.60]`，不需要改資產）。
>
> ## 工作包二（使用者已定義，尚未開始）
>
> 重新評估合成 `gripEdge` 語意 ／ root constraint 改單一 primary contact（不再雙手平均）／
> secondary hand 做實際表面查詢與 residual ／ authored offset 明確歸屬 animation-contact fitting
> （不得混進 probe geometry）／ 同工作包修掉 `M6` ／ 完成後才重新評估 Phase 3 Hand IK。
> ⛔ 先不要用 IK 去修那 −0.16 m／+0.10 m 的手腕 offset——已驗證它與站位無關且接近動畫原始
> contact pose，屬 animation/contact definition 問題。
>
> # 🔵 2026-09-13 — Traversal：參考實作研究輪完成，**Phase 3 Hand IK 應延後**（正本 `docs/24`）
>
> ## 一句話
>
> **root mapping 已經是對的；擋住玩家的是 entry 合法窗與感測範圍互相矛盾。**
> 先讀 **`docs/24-traversal-reference-study.md`** §8（鏈路 KEEP／改語意／缺層）與 §9（實測證據）。
>
> ## 本輪確定的三件事（全部有實測，不是推論）
>
> 1. **測試有一條紅的**：`TraversalWarpPlanTests.M6_NarrowLedge_ShrinksAndClampsHandSeparation`
>    （EditMode 428／426 passed／**1 failed**／1 skipped）。§18.3 改成「每隻手用自己的橫向偏移」之後
>    窄 ledge 不再壓縮雙手間距 ⇒ 這條測試編碼的是已被取代的 baseline，**要在同一工作包內更新**。
> 2. **Climb2m 的 root mapping 正確**：離線重建真實 probe ＋ 真實 2 m 箱 ＋ 逐格取樣真實 clip，
>    站 0.15／0.30／0.46／0.60／0.80 m **五個站位**，接觸瞬間手腕相對真實邊緣都是
>    −0.162 m（下）／+0.09~0.10 m（外），與動畫自身關係（−0.163／+0.1025）差 ≤1 cm。
>    ⇒ 「手抓的位置不對」剩下的量級就是**這支動畫的抓握姿勢本身**。
> 3. ⛔ **真正的阻塞**：`ForwardScanDistance = 1.25 m`，但 Vault1m 由 bake 推導的理想進場距離是 **1.402 m**，
>    合法帶 [1.00, 1.75]。理想點在感測範圍外，**可執行站位只剩 1.10–1.30 m 約 20 cm 寬的縫**。
>    薄牆實測 0.20–0.97 m 全部 `TooCloseUnsafe`。加上 `climb1mEnabled=0` ⇒
>    **場景裡 1 m 級障礙物實際遊玩幾乎都沒反應**。這就是「改了跟沒給一樣」的原因。
>
> ## 研究結論：我們缺了一整層
>
> 五個參考實作（ALS／UE GASP／KINEMATION／Traverser／DPS）**全部**都有
> **Animation Fitting 層**（依量到的尺寸選 clip ／ 依高度推算 montage 起始時間 ／ 依長度縮放 play rate），
> 我們**一個都沒有**⇒ 所有尺寸落差只剩「加大位置 correction」一個出口。
> 這就是 `maxHorizontalCorrection` 1.5↔2.5 反覆、以及 Vault 0.666 m run-out 的來源。
>
> ## 下一步順序（`docs/24` §10，依「擋住玩家的程度」排，不是依技術趣味）
>
> 1. 修 `ForwardScanDistance` vs 推導進場距離的矛盾（止血）
> 2. **補 Animation Fitting**：先做 play rate 縮放（KINEMATION 式）
> 3. correction 內插參數：時間 → **該軸 root motion 進度**（`accum/total`），並移除「抓握期間凍結」特例
> 4. Contact target 降為**單主手 ＋ authored offset**（`MatchTarget` 天生單 target；DPS 雙手 braced hang 也只約束左手）
> 5. 修 `M6` 紅燈（與 4 同一個工作包）
> 6. **最後**才是 Phase 3 Hand IK ——在 1–4 完成前做它等於用 IK 去蓋前面每一層的誤差
>
> ## 工具筆記（本輪新增，值得沿用）
>
> 可以**完全不進 Play Mode** 重建整條 traversal 鏈路：以 `HideFlags.HideAndDontSave` 建臨時
> `CharacterController + TraversalProbe`（`settings` 用反射從場景 player 複製、手動 Invoke `Awake`），
> 在**真實場景**跑 `probe.Tick()`，再串 `TraversalEntryPolicy` / `TraversalPlanBuilder`；
> 動畫取樣在 `EditorSceneManager.NewPreviewScene()` 內（記得 `applyRootMotion = true`）。
> ⚠️ **放 rig 之前一定要先射線找地面高度、並確認用的是箱子的「近面」**——
> 本輪第一次掃描把 rig 放在 y=0／用了遠面，得到「所有 1 m 障礙物都 CorridorBlocked」的**假結論**。
> `unity cmd eval_file` 的腳本是**方法體**：不能寫 `using`，要用完整命名空間，最後 `return`。
>
> # 🟠 2026-09-13 — Traversal Phase 2：三個基準點 bug 已修，**測試尚未跑過**
>
> ## 開場先做兩件事
>
> 1. **確認 Editor 沒卡住**：`unity status`。上一輪在使用者 Play Mode 中排了 EditMode 測試，
>    主執行緒被鎖住。若 Pipeline 命令 timeout，請使用者停掉 Play／Test Runner。
> 2. **補跑測試**（這是唯一的未完成驗收）：
>    先確認 `EditorApplication.isPlaying == false`，再
>    `unity cmd run_tests --mode EditMode --timeout 900 --detach` →
>    `unity cmd run_tests --mode PlayMode --async_tests true`。
>    上一次全綠是 **EditMode 428／427 passed／0 failed／1 existing skipped、PlayMode 41／41**。
>    ⚠️ `B9`／`B10`／`B11` 的期望值可能需要隨 §18 的新數字更新。
>
> ## 工具（**先讀 memory `unity-live-editor-control`**）
>
> `unity` CLI 可以直接驅動使用者開著的 Editor：`unity cmd run_tests` 跑測試、
> `unity cmd eval_file` 讀寫資產與場景、`recompile`／`console`。
> ⛔ `unity test` 會另開 batchmode 撞 project lock；PlayMode 必須 `--async_tests true`；
> **recompile 未完成或使用者在 Play 時不要排測試**（已踩兩次）。
> 資產可改但**只能走 Editor API**（`CLAUDE.md`「Unity Asset Authoring」，2026-09-13 解禁）。
>
> ## 這輪做了什麼（正本：`docs/23` ＋ `docs/22` §13–§18）
>
> - **`docs/23`** ＝ 模型審查，推翻「V3 已完成」：三支正式 Bake 當時沒有 `Traversal` block，
>   piecewise／contact／IK 在出貨資料上從未執行過。
> - **§13 Phase 1**：移除 root 播放頭時間重映（實測去同步 Climb1m +0.82 m／Vault1m −1.02 m）；
>   垂直改加法；correction 窗由 bake 推導；selection／plan 的否決全部帶理由。
> - **§14–15 Phase 2**：`TraversalContactDerivation` 從動畫**自動測出**接觸標記
>   （手先動再停住＝接觸）；進場距離下放到每支動畫。
> - **§16**：Transfer knot 改為沿用接觸 correction ⇒ **抓握期間零額外位移**；
>   Debug 規則改為 **Scene View 只畫幾何、零文字**，數值在 `Window ▸ Project ▸ Traversal Debug`，
>   Game View 什麼都沒有，所有旗標預設關閉。
> - **§17**：對齊點由**手腕**改為**動畫自己的抓握邊緣**（四指扣頂面、拇指按牆面的姿勢會原樣落位）。
> - **§18（本輪最後、未測）**：修掉三個自己引入的 bug——進場距離與接觸解算基準不一致、
>   左右手對稱擺放、終點誤用探測常數。接觸修正 **0.303 → 0.025 m**、離開修正 **0.388 → 0.025 m**。
>
> ## 下一步：Play 驗收
>
> 請使用者站在**離牆約 0.46 m**（Climb2m 的理想距離）按 Jump。
> 預期：抓握瞬間不再有往後扯、放手後不再往前飄。
> 讀現場數據用 `unity cmd eval_file`（腳本樣板見 `docs/22` §18.1 的檢查項）：
> `probe.Candidate`／`EntryEvaluation`／`SelectionEvaluation`、
> `MotionDriver.LastCommittedTraversalPlan`／`LastCommittedTraversalDiagnostics`／
> `LastTraversalMaxBlockedDisplacement`（這三個**不會**在 traversal 結束時被清掉，就是為了這件事加的）。
>
> ## 已知未解
>
> - **Vault1m 的 run-out**：clip 總位移 3.259 m、自然落點在邊緣內側 1.857 m，遠超落腳面 0.6 m
>   ⇒ 離開修正仍有 0.666 m。要靠修剪播放區間或改 playback speed，**不要再加大 correction 預算**。
> - **Climb1m 已由使用者裁決停用**（`climb1mEnabled=0`）；其手部全程滑動，測不到接觸。
>   已接受的後果：1 m 厚箱子爬不上去（Jump apex 0.9535）。
> - **Hand IK 仍不存在**：`TraversalHandIKController`／`TraversalHandIKRig` 是次要 MonoBehaviour
>   類別（在 `FootIKController.cs`／`FootIKRig.cs` 內），Unity 無法掛載。要拆成同名檔案才能用。
>   殘差已降到可由 IK 收拾的量級，但**root 正確之前不要用 IK 去蓋**。
> - 使用者提過「先讓角色調好距離再上去」的 adjust step——尚未實作，待 Play 後再定。
>
> ## ⚠️ 這輪反覆犯的錯（請避免）
>
> 1. **修症狀的字面，不修症狀所屬的問題**。例：使用者說「Game 視窗一堆字」，
>    我只關了 Game 視窗的 TextMesh，結果把更多字堆進 Scene View。
> 2. **用沒認出來的視覺特徵下推論**。截圖裡的洋紅長條被我當成 debug 線追了兩輪，
>    實際上是角色手上的劍（材質 null ⇒ Unity 的材質遺失色）。見 memory `unity-visual-artifact-triage`。
> 3. **拿自己編的測試資料當證據**。要用 `unity cmd eval_file` 讀**使用者場景的真實幾何**。
>    一次假警報：候選的 `entryCapsuleWallClearance` 亂填 ⇒ 殘差從 1.7 cm 變成 19 cm。
> 4. **使用者要的是修好，不是選項**。真正需要裁決時才列選項，其餘自己判斷後直接做。

> # 🟠 2026-09-13 — Traversal 修復 **Phase 1／3 完成**（Runtime decidability ＋ Root/Pose 同步），待 Unity 跑測試
>
> **先讀 `docs/23-traversal-motion-mapping-review.md`**（模型審查正本）與 **`docs/22` §13**（Phase 1 落地）。
> ⚠️ 本輪推翻了先前「V3 已完成、只剩接線」的前提：三支正式 Bake **沒有 `Traversal` block**，
> 因此 piecewise／contact／Hand IK 在出貨資料上**從未被執行過**；Hand IK 的兩個元件是次要 MonoBehaviour
> 類別（`FootIKController.cs`／`FootIKRig.cs` 內），**Unity 無法掛載**。
>
> **Phase 1 已做**：①移除 endpoint 的 `trajectoryNormalized` 重映 ⇒ root 時間 ≡ 動畫時間
> （修正前實測去同步 Climb1m +0.82m／Climb2m +0.53m／Vault1m −1.02m，**且在 correction=0 時就發生**）；
> ②垂直改為與水平同型的加法 correction（移除乘法後段集中）；
> ③新增 `MotionBakeData.GetVerticalSettleNormalizedTime()`，未 author 時 correction 窗由 bake 推導
> （Vault 0.460／Climb1m 0.569／Climb2m 0.833），取代涵蓋整支 clip 的 0.8 預設；
> ④`TraversalSelectionPolicy.Evaluate` 回帶理由的結果 ⇒ **靜默否決結束**
> （實測門檻 `safeReach = 0.9535 − 0.15 = 0.8035 m`，≤0.80m 的 ledge 以前會無聲讓給普通 Jump）；
> ⑤`TryCreate`／`TryBuild` 新增帶 `TraversalWarpPlanRejection` 的多載，`TraversalPlanMode` 區分
> `Piecewise`／`EndpointFallback`／`Failed`；⑥Debug 面板去除全部 `Vector3` 全值，新增 entry L 形誤差拆解、
> 合法距離帶、facing 箭頭、當前格 original/warped 連線、`NO WARP PLAN` 標記。
>
> **驗證現況**：四個 assembly `dotnet build` **0 errors**；**測試已在使用者開啟中的 Editor 實跑**
> （`unity cmd run_tests`，見 memory `unity-live-editor-control`）：
> **EditMode 421 total／420 passed／0 failed／1 existing skipped**、**PlayMode 41／41 passed**。
> 新增的 B1–B7、S1–S5、WARP4B 全綠；更新過的 WARP4 與 PlayMode `V2_CommittedWarp…` 亦綠。
> ⛔ 註記：`unity test` 會另開 batchmode 而撞 project lock，要用 `unity cmd run_tests`；
> PlayMode 必須加 `--async_tests true`。
> 新增 `TraversalBakedAssetTests`（B1–B7，**直接載入三支正式 Bake**）與 `TraversalSelectionPolicyTests` S1–S5。
> 兩條既有斷言因為編碼了被移除的舊語義而在同一工作包內更新：`WARP4`（＋新增 `WARP4B`）與 PlayMode
> `V2_CommittedWarpEndsAtCandidateAndIgnoresLaterProbeTarget`（量測點 0.8 → 1.0）。
>
> **未做**：未執行 Git；未改 FBX／`.asset`／`.prefab`／`.meta`／`.unity`／`.inputactions`；
> 未調 entry 容差／capsule／collision profile／hand rotation weight（`docs/23` §J）。
>
> **Playtest 回饋後續（同日，見 `docs/22` §13.7）**：①Debug 文字全部移出 Game View ——
> 新增 `Window ▸ Project ▸ Traversal Debug` EditorWindow 放數值與決策鏈，空間關係留在 Scene View；
> 新欄位 `drawTraversalPanelText` 預設 false（prefab 未序列化 ⇒ 不用改 prefab）。
> ②修掉「終點固定高 5 cm」——`DestinationClearanceMargin` 是 query 抬升量卻被寫進 committed 終點，
> 現已分離（corridor 最後一段仍用抬升版，否則會被落腳面擋掉）。
> ③量測確認 **Climb1m 的手部高度誤差只有 +3 mm，問題在水平**（手落在邊緣內側 0.45–0.50 m），
> ⛔ 不得用調 root 高度掩蓋，正解是 Phase 2 contact knot。
> ④**使用者裁決：暫時停用 Climb1m** —— 新增 per-kind 內容開關（預設全開，Inspector 取消勾選即可），
> 停用回 `KindDisabled`。⚠️ 已接受的後果：Jump apex 0.9535 m ⇒ 1 m 厚箱子上不去。
> 驗證：**EditMode 424／423 passed／0 failed／1 skipped**、**PlayMode 41／41 passed**。
>
> **🟢 Phase 2 部分完成（同日，見 `docs/22` §14）**：
> ⚠️ **規則變更**：使用者裁決解除「AI 不碰資產」，改為**可改但只能走 Unity Editor API**
> （正本 `CLAUDE.md`「Unity Asset Authoring」；⛔ 仍不得手改 YAML）。
> ①新增 `TraversalContactDerivation`——從動畫**自動測出**接觸標記（手先動再停住 ＝ 接觸，
> 門檻是該手自身最大速度的 15%，相對量）。實測：**Vault1m 是單手翻越**（右手從不停住）、
> **Climb2m 雙手 n=0.065**；Climb1m 測不到（手全程滑動，已停用）。
> ②統一 bake／runtime 的旋轉空間（改用 yaw-only 逆旋轉，不再用 `InverseTransformPoint`）。
> ③把測出的 marker 寫進 Vault1m／Climb2m 的 bake ⇒ **`HasValidBakedData` 首次為 true**，
> piecewise 真的走得到：Climb2m 殘差 **1.7 cm**、Vault1m **0.0 cm**。
> ④同時修掉 Phase 1 的一個真 bug：`HasAuthoredWarpEnd` 分不出「作者填了 0.8」和「建構子預設 0.8」，
> 導致 bake 推導窗從未生效（改為顯式 `overrideWarpWindowEnd`，回歸測試 B8）。
> ⑤資產改動（`climb1mEnabled=0`、Vault `maxHorizontalCorrection` 1.5→2.5、兩支 bake 的 marker）
> 全部經 Editor API 寫入。
> 驗證：**EditMode 426／425 passed／0 failed／1 skipped**、**PlayMode 41／41**。
>
> **🟢 Phase 2（續）完成（`docs/22` §15）**：進場距離已下放到各支動畫
> （`MotionBakeData.TryGetDesiredEntryDistance()` ＝ 接觸時手的前伸距離 ＋ 該時刻已走的水平位移）。
> 推導值 **Climb2m 0.354 m／Vault1m 1.243 m**；`TraversalEntryPolicy.Evaluate` 新增
> `desiredWallDistanceOverride`（`NaN` 退回全域，合成 bake 的既有測試不受影響）。
> 效果：Vault 最大水平修正 **1.618 → 0.825 m**，因此 `maxHorizontalCorrection` 的 2.5 權宜放寬
> **已撤回為 1.5**。
> ⚠️ **玩法可見變更**：Vault 現在要求離牆 **0.84–1.59 m**，貼牆按 Jump 不再翻越
> （`Vault1m` 是跑動翻越、自帶 0.57 m 助跑；站定貼牆翻需要另一支動畫，素材庫沒有）。
> 被拒絕會退回普通 Jump（apex 0.9535），所以 **1 m 薄牆貼牆時過不去**——與 Climb1m 停用同類的內容缺口。
> **Play 時請退開約一個身位再按 Jump。**
> 驗證：**EditMode 427／426 passed／0 failed／1 skipped**、**PlayMode 41／41**。
>
> **🟢 Playtest 回饋修正（`docs/22` §16）**：
> ①**「手抓牢固之後身體還在位移」＝真正的動作缺陷**。Transfer knot 舊版指向另一個空間目標，
> 導致整段抓握期（Climb2m n=0.065→0.269）correction 一路內插；手的世界位置 ＝ clip 手位置 ＋ correction，
> 所以手被拖著在牆上滑。改為 **Transfer 沿用最後一次接觸的 correction ⇒ 抓握期間零額外位移**，
> 位移交還動畫自己。回歸測試 `B11`。
> ②**Debug 徹底改規則**：Scene View **只畫幾何、零文字**（唯一例外 `NO WARP PLAN` 一行）；
> 數值全在 Traversal Debug 視窗；Game View 什麼都沒有。**所有 traversal debug 旗標預設關閉**，
> prefab 序列化值一併設 0。順手修掉影片裡那條洋紅線（`DrawHandDebug` 的 `solved` 未綁定時是原點）。
> 驗證：**EditMode 428／427 passed／0 failed／1 skipped**、**PlayMode 41／41**。
>
> **🟢 對齊點修正（`docs/22` §17）**：使用者指出正確抓握姿勢是「四指按頂面、拇指按牆面」。
> 量測證實**動畫本來就做對了**——`Climb2m` 四指末端貼頂面、拇指低一截貼牆面。
> 錯的是我們把**手腕**釘在真實邊緣：手腕本該在頂面下方 **16.3 cm**、後方 **10.3 cm**，
> 硬釘上去＝把角色抬高 16 cm、往前推 10 cm ⇒ **高度不準＋身體穿進牆裡**。
> 改為對齊 **grip edge**（新增 `LeftGripEdgeInRoot`／`RightGripEdgeInRoot`，自動測出：
> 頂面高度＋手部最前端接觸點）。⚠️ 頂面高度**攀爬取結束高度、翻越取垂直峰值**——
> 用錯會讓 Vault 的 grip edge 翻負號。
> 結果：抓握點對邊緣誤差 **0.000 m**，手腕回到動畫原本的相對關係，root 往後退 10.3 cm 不再穿模，殘差 **0.0 cm**。
> 驗證：**EditMode 428／427 passed／0 failed／1 skipped**、**PlayMode 41／41**。
>
> **原 Phase 2 說明**：讓正式 Bake 真正能驅動 piecewise —— 擴充 `IMotionFeatureAnalyzer` 加 hand/contact
> feature、自動推導六個 knot、統一 bake-space 與 runtime-space 的旋轉（目前 bake 存完整 rotation、
> runtime 只按 yaw 還原）、並處理 `Bake_Vault1m` 的 3.26m 水平總位移（要分離 traversal 主體與助跑／跑出，
> 現在會以 `HorizontalCorrectionExceeded` 失敗，至少已可觀測）。

> # 🟢 2026-09-13 — Traversal V3 Correctness + Debug Pass 完成，停在 Editor Integration Gate
>
> **Playtest 四項答案／修正**：正式 Vault1m／Climb1m／Climb2m Bake 尚無 serialized Traversal block，故實際執行走
> endpoint fallback，hand target 沒進 root execution，vertical 也在單一尾段 window 修正；repo 原本完全沒有 Hand IK；貼牆由
> `TraversalEntryRejectReason.TooClose` 無條件拒絕。現已改為 V3 contact constraint 真正寫入 committed knots、Entry/Contact/
> Transfer/Exit 分段 Y mapping、Exit→Recovery destination hold，以及 asymmetric Entry distance band（safe `AlreadyClose` 接受、
> penetration 才 `TooCloseUnsafe`）。
>
> **Hand IK／Debug**：沿用 Presentation Pipeline 與 Animator IK seam 實作最小 Hand IK target/pose 雙管道；只讀 MotionDriver
> committed plan/time，不讀 Probe、不查 Physics、不改 root。每手 author start/full/release，Exit 前 weight 平滑回 0；invalid/
> unbound/unconfigured/disabled/outside-window/residual-too-large 均安全 weight=0 並顯示狀態。Entry、Contact、Vertical、IK 的
> runtime TextMesh＋Scene Gizmo 已能顯示 current/desired、真正 reason、animated/target/error、original/warped/platform Y、
> IK enabled/weight/target/animated/solved/residual。
>
> **目前驗證**：Unity compile 0 errors；Correctness＋Architecture focused EditMode 80／80；Traversal focused PlayMode 31／31；
> full EditMode 407 total／406 passed／0 failed／1 existing skipped；full PlayMode 41／41（Jump／Roll／Action／Locomotion regression）。
> 未修改 FBX／`.meta`／`.unity`／`.inputactions`／第三方動畫或任何 serialized asset value；未執行 Git。
>
> **唯一剩餘 Gate**：在三個 MotionBakeData author markers＋手骨＋IK timings 後重烘，使 Game debug 從 `Endpoint fallback`
> 變成 `Piecewise V3`；Player Root 掛 `TraversalHandIKController`、Animator 物件掛 `TraversalHandIKRig`，再 Play 驗正常距離、
> 合法貼牆、TooFar／TooCloseUnsafe／lateral／facing、三種 traversal、contact/vertical/IK 數值。本輪不要轉向 CollisionProfile。

> # 🟢 2026-09-13 — Traversal V3 runtime／tests／docs 完成，停在 Editor Integration Gate
>
> **修正的五個真實根因**：①移除 Probe／Classifier speed gate，grounded stationary 改用 committed facing；②以 pure
> `TraversalEntryPolicy` 分離 Candidate 與 Executable，太遠／太近／側偏／朝向超界 Reject；③ endpoint warp 升級為
> Entry／左右手 Contact／Transfer／Exit／Recovery 固定六 knot；④ standing capsule 與 animated body envelope 的差異正式
> 由 `TraversalCollisionProfile` 承載，不再誤寫成 capsule 大小單一原因；⑤ MotionBakeData 加 authored marker＋自動 hand-in-root sampling。
>
> **Environment／commit**：`TraversalLedgeFrame` 提供 edge origin、tangent、wall/top normal、合法 interval；Probe 仍是
> 唯一 query owner，並預算 Entry→Clearance→Transfer→Exit fixed-capsule corridor evidence。JumpPressed 只消費 snapshot。
> PlanBuilder commit 時計算 hand targets、root constraints、transfer clearance 與 piecewise corrections，執行期不回讀 Probe。
>
> **Collision／Recovery**：C0 fixed capsule baseline＋C1 centerOffset 已接；MotionDriver 是 center／height／radius 唯一 writer，
> 保存 requested/actual/blocked delta、CollisionFlags 與實際 capsule。Exit、early recovery、failure restore 冪等；C2 height 與
> C3 radius 為 disabled evidence-gated seam，未在無 Play 證據時啟用。Hand／Foot IK 保留為 root／collision 正確後的 quality pass。
>
> **自動驗證（Unity 6000.5.1f1）**：import／compile 0 errors；focused EditMode **92／92**、focused PlayMode
> **35／35**；full EditMode **394 total／393 passed／0 failed／1 existing skipped**；full PlayMode **41／41 passed**。
> 完整回歸涵蓋 Jump／Roll／Action／Locomotion；Architecture A39–A48 全綠。
>
> **硬邊界**：未修改 FBX／`.meta`／`.unity`／`.inputactions`／第三方動畫資產或既有 serialized asset values；未新增
> StateType／blackboard plan 欄位／第二 movement authority；未擴 Free Climbing、Ledge Hang、Moving Platform、Air Grab；未執行 Git。
>
> **下一步只剩一次 Editor Integration Gate**：三支 traversal 各自 author Entry／hand Contact／Transfer／Exit／Recovery、
> 選 hand bone 並重烘；Play 驗 stationary Jump、四種 Entry Reject 與 Vault1m／Climb1m／Climb2m。記錄 hand residual、
> chest／pelvis／limb penetration、requested-vs-actual delta、Exit error、Recovery latency，再以證據決定 C2／C3 或最小 Hand IK。

> # 🟢 2026-09-12 — Traversal V2 runtime／tests／docs 完成，停在 Editor Integration Gate
>
> **V2 的三項已驗證理由**（不是假說）：使用者實際 Play 發現① fixed baked trajectory 對環境高度／厚度／
> entry distance 敏感，起點／終點／接觸點 alignment error 明顯；②部分低平台可由 Normal Jump 自然登上，
> 卻被 Climb1m 搶走；③主要位移完成後仍等 animation 100%，visual tail 造成 recovery latency。
>
> **Selection**：新增純 static `TraversalSelectionPolicy`。Classifier／Probe 不變；Vault 永遠保留 contextual
> traversal，Climb1m／Climb2m 則從既有 ground Jump bake 的 launch velocity、gravity 與倍率推導 apex，扣
> `normalJumpReachSafetyMargin` 後決定 Normal Jump 或 Traversal。Policy 不查 Physics／Time、不寫黑板；
> `JumpState` 不依賴 `TraversalProbe`。
>
> **Committed Motion Warp**：三種 kind 仍共用既有 `TraversalState`。`OnEnter` 只以 committed candidate、
> entry pose、binding bake 建一次 `TraversalWarpPlan`；執行期不回讀 Probe。水平映射實際 destination，垂直保留
> `VerticalCurve` shape，yaw 對齊 traversal forward；correction 只在 per-binding warp window smoothstep 吸收。
> 每幀以 warped current-minus-previous delta 交給 `MotionDriver`／`CharacterController.Move`，沒有直接
> `transform.position` teleport。退化資料回退 V1 committed motion，拒絕 early recovery，不產 NaN／巨大 spike。
>
> **Recovery**：每格 binding 新增 `recoverNormalizedTime`，有效值不早於 warp end。有效 warp 完成且到 recovery
> 門檻後即可交還既有 `Jump → Move → Idle`，不等待動畫 1.0；`_canRecover` 與 `_isFinished` 分離，visual tail
> 由既有 animation transition/blend 接手。沒有新增 Vault／Climb／Warp／Recovery StateType 或黑板欄位。
>
> **自動驗證（Unity 6000.5.1f1）**：batch compile 0 errors；focused EditMode 53／53、focused PlayMode 22／22；
> full EditMode **379 total／378 passed／0 failed／1 existing ignored**；full PlayMode **36／36 passed**。
> 完整套件涵蓋既有 Jump／Roll／Action／Locomotion。新增 J1–J5、WARP1–WARP7、R1–R5 與 A45／A46。
>
> **硬邊界**：未重設 Probe／Classifier、未新增 physics query owner、未新增 StateType／blackboard traversal 欄位、
> 未修改 FBX／animation content／`.asset/.prefab/.unity/.inputactions`，未加入 IK，未執行 Git。
>
> **下一步只做一次人工 Integration Gate**：分別調 Vault1m／Climb1m／Climb2m 的 warpStart、warpEnd、
> recoverNormalizedTime；default tolerance 不足時才調水平／垂直 tolerance。Play 測低平台 Jump 與三種 traversal，
> 記錄 target 對齊誤差、腳／手接觸偏差、recovery latency、穿模／踏空。若 root／capsule 已正確而只剩末端接觸
> 偏差，下一輪才評估 Hand／Foot IK。

> # 🟢 2026-09-12 — Traversal V1 production wiring 完成，停在 Play Validation Gate
>
> **完成**：新增且只新增一個 `StateType.Traversal`／`TraversalState`；Vault1m、Climb1m、Climb2m
> 依 committed candidate 共用同一個 lifecycle。Candidate 在 `CanEnter` 鎖存，執行期不回讀 Probe、
> 不 query／reclassify；Traversal priority 高於 Jump，None 保留原 Jump fallback，`JumpState.cs` 未修改。
> 三種 traversal 一律走既有 `ExecuteCommittedCurveMovement`，completion 前不可 transition；完成後由
> `Jump → Move → Idle` authored transitions 決定 grounded ambient 或既有 airborne fall-entry。
>
> **資料／治理**：用最小 `TraversalStateParamsSO` 三格 binding（animation key＋MotionBakeData），未建立 library／
> dictionary framework。新增 `ADR-008` Trial，僅記 Integration topology；ADR-006／007 未因本輪被虛構成 Accepted。
> `PlayerRuntimeData` schema、`IMovementModel`、`BaseState` 與既有 ownership 不變。
>
> **Production asset wiring（使用者明確授權）**：`TraversalStateParams.asset` 已接 Vault1m／Climb1m／Climb2m；
> 三個 Animancer TransitionAsset 直接引用既有 FBX sub-clip；既有 Motion Bake pipeline 以 30 FPS 產出三份 bake，
> 垂直峰值約 1.02／1.18／2.00m。`PlayerStateMachineConfig` 的 Traversal priority=30、interrupt 空、transitions
> `Jump → Move → Idle`，Idle／Move 可進 Traversal；`X Bot.prefab` 已有三個對應 Animancer mapping。
>
> **工具／自動驗證（production 專案，Unity 6000.5.1f1）**：官方 Unity CLI **1.0.0-beta.9** 與專案
> `com.unity.pipeline` **0.7.0-exp.1** 已安裝。Runtime／Editor compile 0 errors；EditMode
> **363 total／362 passed／0 failed／1 existing ignored**（含新增 wiring tests 3／3）；PlayMode **26／26 passed**。
> 新增 T1–T9（另含 animation-start guard）與 A43／A44；既有 Jump／Roll／Action／Locomotion 全套無 regression。
> 既有 Probe PlayMode fixture 補 `Physics.SyncTransforms()`，消除 batchmode 建地面後立刻開跳的初始化時序不穩定；
> runtime 行為未因此變更。
>
> **硬邊界**：只在使用者後續明確授權後修改指定 `.asset`／`X Bot.prefab`；未改 `.unity/.inputactions`、FBX／
> animation content、Probe／Classifier，未做 warping／MatchTarget／IK／capsule resize，未執行 Git。
>
> **下一步只做人工 Play Validation**：驗 1m 薄牆、1m 厚平台、2m climb、None→Jump、vault 落差→Falling；
> 記錄起／終點誤差、穿模、踏空、手腳接觸偏差。
> 本輪不修 alignment；證據回來後才判斷小型 target correction 或 Motion Warping。

> # 🟢 2026-09-12 — Traversal V1 Environment Probe ＋垂直位移通道完成，停在 Integration Gate
>
> **工作包**：`docs/22-traversal-v1.md` §9 的 W1–W11 與 §9.3 測試已同批完成。
> `Core/Environment` 現有唯一 physics-query owner `TraversalProbe`、純函式 `TraversalClassifier`、
> 高度／depth 雙遲滯、完整 candidate／reject snapshot，以及只讀 snapshot 的 Game View runtime lines
> 與 Scene View Gizmo。Runner 只在順序 2.7 可選 Tick；candidate 不進黑板，尚不驅動任何 State／Motion。
>
> **垂直通道**：`MotionBakeData.VerticalCurve` 與 analyzer 已落地；
> `MotionDriver.ExecuteCommittedCurveMovement(...)` 以播放頭前後差值消費水平／垂直曲線，期間暫扣重力，
> 但仍經共用 `SyncGroundedState` 發布 `IsGrounded／VerticalVelocity／JustLanded／JustLeftGround`。
> 舊的 Jump／Roll／Action／Locomotion 呼叫端與路徑未改。
>
> **自動驗證**：Runtime build 0 errors（2 個既有 framework warning）；Editor build 0 errors
> （7 個既有 reference warning）；隔離副本的 EditMode 全套 **358 total／357 passed／0 failed／1 skipped**，
> PlayMode 全套 **16／16 passed**。隔離副本因 `com.kitwright.unity.mcp` 與 Unity 6000.5.1 API
> 不相容而只在副本移除該 package；原專案的 package／資產均未改。
>
> **硬邊界狀態**：未新增 StateType／黑板欄位，未改 `IMovementModel`／`BaseState`，未修改任何
> `.asset/.prefab/.unity/.inputactions` 或動畫美術資產，也未執行 Git 操作。原本已開啟的 Unity Editor
> 自動為新增 `.cs` 產生標準 companion `.meta`；Codex 未手動編輯任何 `.meta`。
>
> **接線已完成並靜態核對（2026-09-12）**：玩家 `X Bot.prefab` Root 有且只有一個 `TraversalProbe`，
> 與 `CharacterController`／`CharacterPipelineRunner` 同物件；`obstacleMask = 19`，涵蓋 SampleScene 的
> Default layer 並排除 Player／Enemy。兩個 debug 通道目前開啟；SampleScene 的 X Bot instance 正常繼承，
> 沒有 component removal／override；敵人 `Y Bot` 維持不掛 probe。
>
> **下一步只剩人工 Integration Gate**：烘三支 traversal `MotionBakeData`、依場景調 W4 tuning，
> 最後 Play 驗 debug 線與既有 gameplay 手感。⛔ 不要在此之前
> 擴到 Traversal State／FSM 接線；那是 §9.5 的下一個工作包。
>
> # 🟠 2026-09-12 — Traversal state reconciliation（純文件輪，無程式變更）
>
> **起因**：使用者指出「traversal 之前已做過實際驗證，只是忘記同步文件」，要求以實際狀態為基線重整文件。
>
> **掃描結果：在本 repo 內找不到任何 traversal 實作或驗證產物。** 九條可重跑的檢查與結果全部記在
> `docs/22` §0.0——**⛔ 下一個會話不要再重跑一次同樣的搜尋。** 摘要：所有分支的 commit grep 0 筆、
> stash 空、`Assets/Scripts` 與 `Assets/_Project` 的 traversal 符號只有 1 筆註解、`StateType` 仍是 6 個、
> 烘焙資產 0 筆、場景只有一般關卡幾何。⚠️ 證據限制：`.gitignore:100` 忽略整個 `MovementAnimsetPro/`，
> 第三方動畫包的匯入工作在 git 中不可見（但程式與 SO 都在版控內，故程式面結論可靠）。
> **若使用者能指出驗證所在（另一個專案／筆記／截圖），`docs/22` §0.0 與 §3 應立即重寫。**
>
> **真正找到的不同步（已修）**：`WORKLOG` 裡 combined vertical slice 的 Play 證據**從未回填 ADR 勾選框**，
> 導致 `docs/17` §7-F4 記成「七條驗收一條都沒打勾」，下一輪規劃因此低估進度、過度保守。已 fold back：
> **ADR-006 → 5/8 通過**（剩 E 回歸、H 零 GC）、**ADR-007 → 4/7 通過**（剩 A2、E、G，F 只驗 EditMode）。
> ⇒ **兩份 ADR 剩下的都不是程式，是「兩次 Play ＋ 跑一次 PlayMode ＋ 一次 Profiler」。**
> 做完即可翻 `Accepted`，`docs/18` §6-C 的 **X-3**（新增 `TraversalState`）隨之自動解除。
>
> **`docs/18` §6-C 文案已修**：舊標題「暫時不要做」被後續會話誤讀為永久禁令。改為五態狀態欄
> （🧊benchmark-freeze／⏸roadmap-defer／🔬spike-done／🚧in-implementation／⛔prohibited），
> 並明寫「⛔ 不得把 🧊 讀成 ⛔；判斷方式是解除條件成立了沒」。**未更動任何裁決內容。**
>
> **三個技術結論複查**：①⛔ **`Climb1m` 無素材是錯的，已由使用者指正並更正**——三支 clip 齊全，
> `Vault1m`／`Climb2m`／`Slide` 在 `SlideClimb.fbx`，**`Climb1m` 在 `RunStrafeUpdate.fbx`**（皆 Humanoid、loop 0）。
> 成因是搜尋只比對 FBX **檔名**。**教訓：Kubold 的檔名不預測內容，找 clip 必須窮舉所有 `.meta` 的 sub-clip**
> （`for f in $(find Assets -iname "*.fbx.meta"); do grep -H "^      name:" "$f"; done`），已寫進 `docs/04` §2 與 `docs/22` §0.0。
> **連帶反轉**：`Vault1m` 與 `Climb1m` 同為 1m ⇒ 高度分不開 ⇒ **obstacle depth 是 V1 必要維度，且必須在 P1 就進分類器**；
> ②垂直位移缺口**確認仍存在**，無任何 branch／commit 修過；
> ③`JumpState` 搶狀態**已對資產核實並修正結論**——中途搶奪可由「`CanBeInterruptedBy` 留空」排除
> （Jump／Roll 今天就是這樣），**真正的 must-fix 換成「退出幀是否 grounded」**，只能 Play 驗。
>
> ⚠️ 本輪**只動文件**（`docs/17`／`docs/18`／`docs/22`／`docs/00-map`／ADR-006／ADR-007／本檔），
> 未動任何 runtime code。不做任何 Git 操作。

> # 🟡 2026-09-11 — Spell 8-way＋soft target combined vertical slice 已實作，結構性 Play 通過，待腳步手感驗收
>
> 使用者批准 ADR-006／007 暫時同為 Trial 的一次性例外。X Bot 四個 Spell key 已 author 成 Layer 1
> `Human Body Upper Mask`，Layer 0 companion 指向 Spell 專用 `Locomotion_2D_Spell_WalkRun`；一般 Idle／Move
> 仍維持原 Layer 0／1D。`AnimancerFacade` 統一處理 overlay 淡入淡出與兩層預熱，ActionState 仍只送 key。
> 舊 `UpperBodyWeight` dead field 已移除。
>
> `PlayerCombatContextSource` 在同一 target producer 內發布無記憶 soft candidate：12m、水平半角 25°、
> camera alignment 優先、distance 次要。Fireball／Ice 顯式採 `CameraConeSoftTarget`；每個 Action／連段段落
> 開始只取一次，段內不追蹤、不 fallback，facing 與 projectile release 共用同一 commitment。全套 EditMode
> 330 passed／0 failed／1 skipped，Unity compile 0 errors。Live Play 已驗證：Spell 時 Layer 0 保持 8-way、
> Layer 1 播 Fireball／Ice 且使用 upper-body mask；Forward／Backward／Left／Right／Forward-right 能依
> `MoveX / MoveZ` 選到對應 child；Action 結束 overlay 歸零；無目標、前方、側方、背後、雙目標與段內
> target 消失情境皆符合 snapshot 規則，Console 無 gameplay error。仍待使用者以正常鏡頭／輸入驗收動畫
> blend、瞄準修正量與整體手感；無新 hard lock-on／方向來源／movement policy。
>
> 🆕 原 FullRing 把全套 Run directional clips 校正到 Sprint 6.2614 m/s，造成現行 Walk／Run 腳步快轉。
> 已改為 Walk 8-way（radius 0.35）＋Run 8-way（radius 0.75）雙環；各 child playback 由該 gait 的
> target world speed／各自 `MotionBakeData` 推導。補齊四支 Walk diagonal bake，兩支異常 import clip 已套回
> Locomotion-位移 SOP。Sprint gait、`Gait_ActionRPG` 與 MotionDriver 的 Sprint 最大速度來源皆未改；
> directional Sprint ring 刻意延後到 Sprint 操作真正啟用時。Live Play 接線抽查：Layer 0＝新 mixer（17 children），
> Walk Left／Backward 分別為 `1.33274×`，Run Left／Backward 分別為 `2.16935×`／`2.12143×`；
> Layer 1 Ice 同時保持播放，Console 無 error。待使用者確認腳步快轉觀感已改善。

> # 🟡 2026-09-11 — Enemy Punch locomotion 滑動已修正，待 Play 驗證
>
> 根因不是 root motion 或 Punch_Move，而是 `EnemyPunchDefinition.Start.Bake` 原本為 null：
> `ActionState.OnUpdateMotion` 因此走 `ExecuteBaseMovement`，在 Punch 播放期間繼續消費 AI 留在 blackboard 的
> `MoveDirection / MoveSpeed`。已用既有 `MotionBakeEditor.BatchBake`（X Bot humanoid sample rig、60 FPS）
> 對 `Enemy_Punch_R` transition 實際引用的 `Fists_Punch_R` FBX sub-clip 建立
> `Bake_Fists_Punch_R.asset`，並接到 `EnemyPunchDefinition.Start.Bake`。Bake 與 clip duration 均為
> 0.8000001 秒，`AutoAverageSpeed`、所有水平 speed samples、`TargetLocalDirection` 皆為 0；
> 因此 Action 期間走既有 baked-motion 分支，不再消費 AI locomotion 水平位移。Fireball／Ice 仍維持
> no-Bake → `ExecuteBaseMovement`，未新增 MovementLock／AttackMovementPolicy。新增
> `EnemyPunchMotionWiringTests` 鎖定接線、provenance、duration、零水平位移與兩個玩家法術 no-Bake；
> 相關 EditMode tests 51/51 passed，Unity compile 0 errors。
>
> 施法 8-way 後續已於本檔最上方的 combined vertical slice 落地；本段只保留 Enemy Punch 交辦歷史。

> # 🟡 2026-09-11 — Y Bot combat directional locomotion minimum set 已實作，待 Play 驗證
>
> Y Bot 的 `Move` 已改接專用 Cartesian 2D mixer：Idle＋`WalkFwdLoop`／`WalkBwdLoop`／
> `StrafeLeftLoop`／`StrafeRightLoop`，不含 diagonal、Run、Crouch，所有 child playback speed 都是 1。
> Back／Left／Right 已用既有 60 FPS 流程補 `MotionBakeData`；Y-only
> `CombatDirectionalSpeedProfileSO` 直接引用四方向 bake 與 MotionDriver 同一份最大速度 bake，
> `LocomotionModel` 只在該 actor `InCombat` 時以 committed facing local direction 套用 bake-derived speed cap。
> X Bot profile 維持 null、`Move` 仍指向原有 1D `Locomotion.asset`。Unity 編譯 0 errors；本輪指定
> EditMode tests 13/13 passed。注意：這組 walk cardinal bakes 都約 1.64435 m/s，並沒有素材內建的
> strafe/back 較慢差異；刻意不手填第二套倍率，待 Play 後再由使用者決定是否要換素材政策。

---

> # 🟡 2026-09-11 — Enemy AI v1 baseline 已收斂，待 Play 驗證
>
> 採 A 方案：既有 `AIMovementSource` 擁有 aggro 8/12 黏性進出與 tactical movement；
> `AIInputSource` 擁有 attack pulse／0.5s retry；`CharacterFacingSource` Priority 2 擁有敵人 persistent facing；
> `ActionState` 保持唯一執行權威。`EngagementMovement` 新增明確 `Strafe`，`Hold` 收斂為零移動 fallback，
> 不再用「Hold」名稱承載側移行為。新增 T35 證明 pulse 後下一幀 external Reaction 可被消費；
> W15 守同一 target、aggro／engagement／attack 三層距離關係。未來 Utility 僅記於 `docs/21`，未實作。

---

> # 🟡 2026-09-11 — Enemy attack request pulse 已實作，待 Play 驗證
>
> `AIInputSource.WantsToAttack` 保留持續條件；`Slot1ButtonDown` 改為首次入圈立即送、留圈每 0.5 秒重試、
> 離圈重新武裝的單幀 pulse。retry 只代表 producer 多久重送 request，不是 cooldown；AI 未讀 FSM／ActionState，
> `ActionState` 的 cooldown／variance／grounded／lifecycle 權威與既有 intent-vs-Reaction 仲裁皆未改。
> `AIInputSourceTests` 已覆蓋首次、非逐幀、interval 前後、離圈、重入；A27 另守 AI 不反向依賴狀態機，
> 並鎖住 `PlayerInputSource` 三個 slot 仍採 `WasPressedThisFrame()`。純 C# 驗證完成後停在 Integration Gate，
> 等使用者 Play 驗證 Enemy cadence 與 Fireball／Ice／Melee reaction starvation 是否解除。

---

> # 🟡 2026-09-10（最新裁決）— Observability 範圍收斂 ＋ Foot IK Game View probes 已實作（**待 Unity 驗證**）
>
> **本段取代下方較早的 O-1 四箭頭交辦。** world-space debug presentation 只保留兩類：
> ① Traversal／可跳上平台檢測；② Foot IK。判準是「玩家看角色行為時，無法知道系統實際採樣了
> 什麼世界幾何資料」。Facing／MoveDirection／Direction Authority 不再投入四箭頭、history 或 HUD；
> Direction Authority 的 Editor-only snapshot 與 A33 測試保留，日後 combat 8-way 有具體問題再重開。
>
> ## 已完成
>
> 1. `CharacterFacingSource`：移除四箭頭、deadzone、label；保留 `RecordFacingDebug` snapshot。
>    A33 改守「只記錄、不重算、無 world-space presentation、無跨檔讀取」。
> 2. `FootIKController`：在既有 query 點快取本幀真實 ankle／heel／toe probes、hit／miss、raw normal、
>    clamped `SoleNormal`、residual lift、pre-IK goal 與最終 target／sole pose。Game View 主通道改用
>    runtime `LineRenderer`，正常 Play 即可看且不依賴 `Gizmos`；Scene View 另保留 `OnDrawGizmos` 詳查。
>    🆕 同一 snapshot 另畫 heel origin → toe origin 的深灰連線，並以薄荷綠箭頭顯示 production offset
>    真正使用的 corrected foot-space `+Z`；不改解算、不改 offset、不新增 query。
>    🆕 Scene View 再以實心點標出 `L/R BASE`、`L/R HEEL`、`L/R TOE`，並畫 BASE→HEEL 橘線、
>    BASE→TOE 藍線。BASE 快取的是 `sample.TargetPosition`（corrected ankle target），不是原始
>    Animator IK goal／bone Transform；既有 wire sphere 是 query hit／miss end。
> 3. 新增 A34：Foot IK runtime renderer／Scene Gizmo 禁止重新呼叫 Physics／sampling API；
>    Game 主通道必須使用真正 renderer，禁止以 `Debug.DrawLine` 或 selected-only Gizmo 代替。
>    零新黑板欄位、零新 public API、零 Debug Framework；只存在於 Editor／Development debug。
> 4. 文件同步：`docs/05` §3.5.5、`docs/18` §1、`docs/17` 檔頭、`docs/02` §7.1。
>
> ## Traversal 邊界
>
> Production code 目前缺的是**角色前方 obstacle query 本身**，不只是缺 traversal candidate owner。
> 全域盤點到的 physics query 只有 Foot IK 向下射線、相機遮擋 SphereCast、鏡頭瞄準 Raycast、
> 地面特效的向下探地／附近碰撞，以及戰鬥目標 overlap；沒有任何查詢會產出角色前方的
> origin／direction／distance／hit point／hit normal。`CharacterController` 的 isGrounded／stepOffset／
> slopeLimit 也不暴露這些資料；`MotionDriver` 的 `CharacterController.Move` 是碰撞解算，且忽略
> `CollisionFlags`、沒有 `OnControllerColliderHit`／hit snapshot。因此本輪只固定未來顯示契約：前方 probe、obstacle hit、
> top／standable candidate、clearance、最終可跳判定。
> 不建立假 drawer、不為顯示發明 world data；等真正 query＋classification owner 落地時一起實作。
>
> ## 驗證與 Integration Gate
>
> - Runtime build：✅ 0 errors（2 個既有 NUnit target-framework warnings）。
> - EditMode CLI build：✅ 以 NUnit 相容目標 `TargetFrameworkVersion=v4.7.2` 建置為 0 errors；
>   預設 4.7.1 會觸發既有 NUnit 參考失配。Unity EditMode 實跑仍待驗。
> - Unity：跑 EditMode 全套，特別確認 A2／A33／A34 與既有 FootIKTests。
> - Game View **關閉 `Gizmos`**、不選角色，仍應看到雙腳 runtime probes：cyan ankle、orange heel、blue toe；
>   miss 紅色；黃色 raw normal、綠色 clamped sole normal；洋紅為 pre-IK goal → target／sole pose。
> - 深灰線必須直接連 heel/toe sample origins；薄荷綠箭頭由兩點中點指向 foot-space `+Z`／toe 方向。
>   由上方觀察其 XZ 投影是否與模型腳掌長軸一致；此項只驗 basis，不調 `HeelOffset`／`ToeOffset`。
> - 關閉個別 `drawFootIKRuntimeLines` 應只關 Game View runtime 線；切換 Game View `Gizmos` 不應影響它。
> - Scene View 的細節由 `drawFootIKSceneGizmos` 控制；它不是 Game View 可見性的前提。
> - 確認 Direction Authority 四箭頭／文字已不再出現。
>
> ⚠️ 不做任何 Git 操作；由使用者自行 commit。

---

> # ⛔ 2026-09-10（較早方案，已被上方最新裁決取代）— Benchmark fold-back ＋ O-1 四箭頭歷史紀錄
>
> ## 開場指令
>
> **直接做 Integration Gate 檢查清單，不要重新規劃。** 使用者已裁決的界線寫在下方，照做即可。
>
> ## A. 已完成（純文件 fold-back，無程式風險）
>
> | # | 內容 | 檔案 |
> |---|---|---|
> | 1 | `docs/18` §7 的 **S1–S7 全數套用**到 `docs/17`：**S2** 撤回「traversal 是 jump 特化」（推理錯誤）、**S4** 更正「第一份 world 資料」事實錯誤（`CombatContext.TargetPosition` 早是先例）、**S1** 時間軸全表重算（debug 預覽段 **28%**、traversal 全段 **37%**）、S3 腳步 IK「提前采样」列為未定、S5／S6 Ground Normal 由 🔴 校準為 🟡／🟢、S7 補 `stepOffset`／`slopeLimit` 真缺口 | `docs/17` |
> | 2 | `docs/17` §7-**F1 已解決**（211 筆未 commit 工作已由 `71f5a5c` 入版控），檔頭修訂提示同步 | `docs/17` |
> | 3 | **F7 fold back**：dev-spec §1.1 `MoveDirection` `Vector2` → **`Vector3`**（程式碼區塊 ＋ 權限表型別欄）。⇒ **ADR-007 翻 Accepted 的這項阻礙已排除** | `docs/02` |
> | 4 | **F8 fold back**：dev-spec §1.1 補 **`CombatContext`** 欄位與權限表列；§2.1 補**順序 2.6**（Combat Context Producer）與**順序 4.6**（CharacterFacingSource），兩列都寫明「為什麼只能卡在這個夾縫」 | `docs/02` |
> | 5 | 新增 **§2.9 Continuous／Committed／Suspended** 詞彙（定義 ＋ repo 案例對照 ＋ committed≠targeted 兩軸 ＋ Jump 逐軸混合）。⚠️ **只是分析模型，不是契約**：不改 `IMovementModel`／state contract、不進 dev-spec §1／§2／§3.1、**未開 ADR** | `docs/01` |
> | 6 | `docs/00-map.md` 指標同步（`docs/18` 狀態改為已套用；新增 §2.9 的最短路徑列） | `docs/00` |
>
> ## B. 已完成（程式）— Observability **O-1 Direction Authority 三箭頭**
>
> **宿主**：`Assets/Scripts/Core/Facing/CharacterFacingSource.cs`（全專案唯一同時看得到
> Action commitment／`MoveDirection`／`transform.forward` 的地方，ADR-007 D3）。
>
> - `Tick` 由「兩段 early-return」改為**單一出口**（`&&` 短路，**逐位元等價**），讓 debug 快照有唯一記錄點。
> - 新增 `#if UNITY_EDITOR` 區塊：`RecordFacingDebug`（**只賦值不判斷**，零 GC）＋ `OnDrawGizmos` 四支箭。
> - 四支箭**畫在不同高度**（forward 0.05／move out 0.25／intent 0.45／request 0.65）：
>   一致時疊成整齊垂直堆，任何分岔一眼看得到（同高度會 z-fight，看不出差 0.5° 還是完全一致）。
> - 顏色：白＝實際朝向｜綠＝model 輸出｜藍＝意圖｜**洋紅＝Action commitment 贏**｜**黃＝MoveDirection 贏**｜紅＝快照過期。
> - Action commitment 來源時另畫**死區扇形**（回答「死區是不是吞掉了本幀請求」）。
> - Label 印：outcome（`RequestSent`／`DeadzoneSuppressed`／`NoDirection`）、來源、死區角度、**∠(move out → forward)**。
> - `[SerializeField] drawDirectionAuthorityGizmos = true`（Editor-only）＝ per-character 總開關，**不需要接線**（新欄位取 C# 初始值）。
> - **Play 中從未被 Tick** ⇒ 紅字明講「是接線問題，不是 facing 邏輯」；Edit mode 不吵。
>
> **守住的界線**（使用者裁決）：`#if UNITY_EDITOR` 全包｜零新黑板欄位｜零新 public API｜
> 不重算 facing policy｜無 runtime HUD｜無新 Debug framework。
>
> **新增架構測試 `A33_FacingDebugSnapshot_RecordsWithoutRecomputing`**：
> ①四個 drawer 方法體內**禁止**出現 `TryResolveFacing`／`ShouldRequestFacing`／`IsWithinFacingDeadzone`／
> `TryGetActiveFacingCommitment`／`RequestFacing`（＝「記錄，不要重算」的機器化）；
> ②`_debug*` 欄位不得被任何其他 runtime 檔案讀取（單向紀律）。
>
> ## C. ⚠️ 驗證狀態（**誠實分界**）
>
> | 項目 | 狀態 |
> |---|---|
> | `dotnet build Project.Runtime.csproj` | ✅ **0 警告 0 錯誤** |
> | `dotnet build Project.Tests.EditMode.csproj` | ✅ **0 錯誤**（4 個 MSB3277 是既知的組件參考噪音） |
> | EditMode 測試實跑 | ❌ **未跑**——需要 Unity Test Runner（我跑不到） |
> | Play 驗證 | ❌ **未做** |
>
> ## D. 🔴 Integration Gate — 需要使用者在 Unity Editor 做的事
>
> 1. **開 Editor 讓它重新編譯 ＋ 生成 `.meta`**（本輪沒有新增 `.cs` 檔，只改既有檔案，風險低）。
> 2. **跑 EditMode 全套**，特別確認新增的 **A33** 與既有的 **A2／A31／A32／W11** 全綠。
> 3. **Play 驗收 O-1**（這就是 O-1 的第一個真實驗收案例）：
>    - Scene／Game view 打開 Gizmos。
>    - **玩家**：一般移動時四支箭應同向；**施法／攻擊瞬間**箭頭應轉為**洋紅**（Action commitment 贏），
>      且身體朝向被承諾方向拉住。
>    - **敵人（Y Bot）**：進入戰鬥後應**持續** target-facing——側移／後退時
>      **綠（move out）與白（forward）應該分岔**，這正是 8-way 要驗的東西。
>    - 若出現紅字「從未被 Tick」⇒ 該角色的 Runner 沒排到 `CharacterFacingSource`，是接線問題。
> 4. **Git commit（使用者執行）**——建議切點：`docs/17` `docs/18`（新檔）`docs/00` `docs/01` `docs/02`
>    ＋ `Core/Facing/CharacterFacingSource.cs` ＋ `ArchitectureRegressionTests.cs`。
>    ⚠️ `docs/artifacts/architecture-tour.html` 也在工作樹中被改動，但**不是本輪產出**，請自行判斷是否一起進。
>
> ## E. ➡️ 下一個實作區塊（使用者已裁決，**O-1 可用後直接開始，不要再擴張 observability**）
>
> **Combat / casting lower-body 8-way locomotion。**
> - 玩家與敵人**共用同一套** 8-way locomotion／`MoveX`／`MoveZ` 解算，**不做兩套 controller**。
> - 差異**只在 facing policy**：Player 只在需要的 combat／casting 情境取得 combat-facing；
>   Enemy 進入 combat 後**持續**維持 target-facing（側移／後退／迂迴／施法時皆然）。
> - 動畫方向解算鏈：**world movement → relative to current committed facing → MoveX／MoveZ**。
>   ⛔ **不得**直接拿 camera-relative direction 當動畫方向。
> - ⚠️ 若實作中發現 **ADR-007 尚未滿足的驗收條件會阻塞 8-way**，**回報具體 blocker，不要自行擴張 scope**。
>
> **暫緩**（使用者明確排序）：O-2 Foot IK 探測可視化、O-3 Stop trace。⛔ 不要同時展開。
>
> ## F. 🟡 本輪新發現的 finding（**只回報，未自行裁決**）
>
> **F10 — `docs/02-dev-spec.md` §7.1 自動項清單漏了 9 條已存在的架構測試**
> （`A12` `A14` `A15` `A27` `A28` `A29` `A30` `A31` `A32`；本輪已補登 `A33`）。
> ⚠️ 其中 **`A31`／`A32` 正是 ADR-007 D3 的兩條核心不變量**——機器守著，人讀的清單裡卻不存在。
> `CLAUDE.md` Context Discipline 把 §7.1 表當成「最便宜的正確架構摘要」，漏三分之一就不能再當摘要用。
> **不是本輪造成的**；逐條補寫要讀 9 支測試，屬獨立工作包 ⇒ **是否排程由使用者決定**。詳見 `docs/18` §8-F10。

---

> # ✅ 2026-09-10（同日後續）— 敵人失地：精簡 Jump 資產 ＋ Y Bot 映射已接線，W13 綠
>
> ## 實測回報
>
> Play 後兩行警告，來源是 **Y Bot（敵人）**，不是玩家：
> ```
> [AnimancerFacade] 狀態機請求播放 'FallingLoop'，但 Transition Mappings 查表失敗！
> [AnimancerFacade] 狀態機請求播放 'JumpIdleLandHard'，但 Transition Mappings 查表失敗！
> ```
> 表現正常、EditMode 全綠。
>
> ## 診斷（已核對 prefab／config YAML）
>
> | | |
> |---|---|
> | Y Bot 的 StateKey | `Idle` `Move` `Jump` `Roll` `Damage` `Enemy_Punch_R` `WalkStop_*` `RunStop_*` —— **Jump 家族零個** |
> | X Bot 的 StateKey | 含 `FallingLoop` 等 **18 個** Jump 家族鍵 |
> | Missing 欄位 | **沒有**（掃過 `Transition: {fileID: 0}`，零筆）——警告訊息那句提示這次不適用 |
> | 兩邊的 `JumpStateParams` | **同一份資產**（guid `a3c1461a…`）⇒ 敵人繼承玩家的完整 18 格變體表 |
>
> **因果**：`FullBodyStateMachine.Initialize` 對**每一隻**角色無條件註冊 `JumpState`；
> 在此之前敵人**永遠進不去**（AI 從不設 `JumpRequested`，`CanEnter` 恆 false）。
> 非主動失地入口給了它第一次進入機會 ⇒ 開始請求玩家專屬的鍵。
> **不是本輪的 bug，是本輪把既有接線缺口暴露出來。**
>
> `AnimancerFacade.TryGetTransition` 查不到就 warn ＋ 保留當前動畫、procedural 位移不受影響
> ⇒ 這就是「表現正常」的原因，是設計好的退化路徑。
>
> ## 📐 有用的推論：敵人真的摔了約 3.3 公尺
>
> `JumpIdleLandHard` 出現代表 `|v_y| ≥ 8 m/s`。**非主動失地不注入 launch ⇒ 用序列化的
> `gravity = -9.81`**（不是 bake 的 16.78，`_activeGravity` 只有 `ApplyJumpLaunch` 會覆寫）：
> ```
> h = 8² / (2 × 9.81) ≈ 3.26 m
> ```
> Y Bot 在場景的 `m_LocalPosition.y ≈ 1.4e-7`（不是出生點懸空），
> 且 log 裡**只有 hard land、沒有任何 normal land 鍵** ⇒ 每次落地都是重落地。
> **⇒ 敵人是真的從三公尺以上掉下來**（最可能是追著玩家走下同一個高台）。
> 反證了「不是 grounded 抖動誤判」——0.1 s grace 沒有誤觸發。
>
> ## 裁決（使用者 2026-09-10）
>
> **「既然敵人可能失地，就至少要有一套能表現 falling / landing 的資料。」**
> ⇒ 走**精簡自有表**路線，不是把玩家的 18 格複製過去。
>
> ⚠️ **修正我先前的說法**：我說過「缺 Walk／Run 會依 D3 退回 Idle」——**只在 `tier == None`
> 或沒有 foot phase 時成立**。敵人走路離地且 foot phase 有解時，`walk[]` 為空會讓
> `SelectByEntryPhase` 回 −1 ⇒ `default` ⇒ 保留現有鍵、`_landDuration = 0` ⇒ **立即退場**。
> 已核對 `LocomotionStopSelector.SelectByEntryPhase` 開頭就有 `variants == null || Length == 0 → -1`，
> **不會 NRE**。所以精簡表是安全的，只是「一般落地不播落地動畫、直接接回 locomotion」——
> 對敵人而言這個退化是可接受的，但要知道自己買到的是什麼。
>
> ## 🎮 Editor 接線 checklist（我不能做）
>
> **1. ✅ 已新建敵人專屬的 `JumpStateParams` 資產**：`EnemyJumpStateParams.asset`
>    - `animationVariants.falling` → `FallingLoop`
>    - `animationVariants.normalLand.idle` → `JumpIdleLand`
>    - `animationVariants.hardLand` → `JumpIdleLandHard`
>    - `start`／`normalLandToMove`／所有 `walk[]`／`run[]` **留空**
>    - `stages` **留空** —— 已核對：`jumpParams` 非 null 就不會有 `[JumpState] 查無 JumpStateParams` 警告，
>      `BuildStages` 會靜默合成一段 fallback，而非主動失地根本不注入，所以那段永遠用不到
>    - `hardLandingSpeed`／`landFallbackDuration`／`fallEntryGrace` 依敵人手感設定
>
> **2. ✅ `EnemyStateMachineConfig` 的 Jump `Params` 已改綁這份新資產**
>    （已核對磁碟：guid 由玩家的 `a3c1461a…` 改為 `0e316e35…`，兩隻角色自此不再共用同一份表）
>
> **3. ✅ Y Bot 的 `AnimancerFacade.transitionMappings` 已補 3 列**：
>    `FallingLoop`／`JumpIdleLand`／`JumpIdleLandHard`
>
> **4. ✅ 重跑 EditMode** —— `W13` 已轉綠。
>
> 🔑 **為什麼給敵人自己一份表，而不是共用玩家那份**：現在共用代表
> **玩家的跳躍調參會靜默決定敵人的落地分類**（`hardLandingSpeed` 同一個值），
> 而敵人永遠不跳、玩家那 18 格對它全是死重。這是單一真相來源的問題，不只是省接線。
>
> ## 🆕 W13：把這類問題壓回 EditMode
>
> `PrefabWiringTests` 已有 **W7** 守 *Action* 的動畫鍵，但**沒有 state 鍵的版本**——
> 所以這個缺口只能等 Play 才浮現。新增
> `W13_ReachableStateAnimationKeys_ResolveInTransitionMappings`：
> 對每隻角色檢查 ①`Idle`／`Move`／`Jump`／`Roll` 四個 `BaseState.AnimationKey`
> ②該角色 config 綁的 `JumpStateParams` 變體表裡**所有 authored 的格子**。
>
> 判準刻意是「authored 的格子」而不是「表裡全部 18 格」——`ResolveAnimationKey` 對無效變體
> 保留現有鍵、不會請求空鍵，所以**精簡表是合法配置，它要接的是自己填了幾格就接幾格**。
> W13 因此不預設任何一種選擇，A（共用完整表）與 B（精簡自有表）都能通過。
>
> ✅ **已結案**：接線完成、`W13` 轉綠、測試全綠、Play 無異常。
>
> 📌 W13 交付當下**刻意是紅的**——它就是上面那份 checklist 的機器版本。
> 這個做法有先例：`docs/14` 的 **W11** 同樣以「Y Bot 接線前是紅的」作為給使用者的接線提示。
> 交付紅燈只在「紅燈本身就是待辦清單」時成立，不是繞過 `CLAUDE.md`「交付驗收必須全綠」的通則。

> # ✅ 2026-09-10 — **Walk-off Falling ＋ VerticalVelocity 落地**（已驗收：測試全綠 ＋ Play 無異常）
>
> ## 這輪完成什麼
>
> **① 走出高台不再空中走路。** `JumpState.CanEnter` 現在有兩條入口：
>
> | 進入原因 | 起始 phase | launch velocity | 空中按跳 |
> |---|---|---|---|
> | **主動 Jump**（`JumpRequested && IsGrounded`） | `Start` | ✅ 照常注入 | ✅ 可推進 `Stages` |
> | **非主動失地**（連續離地 ≥ `FallEntryGrace`） | **`Falling`** | ⛔ **不注入** | ⛔ **拒絕**（守 ADR-002 無限空中跳禁令） |
>
> **零資產變更**：走的是 `EvaluateInterrupts`（每幀對所有狀態問 `CanEnter`），FSM config、
> transition 資產、`Falling` 動畫映射全部沿用既有接線。**未新增 StateType**，`A13'` 不受影響。
>
> **Fall-entry grace** 預設 **0.1 s**，落在 `JumpStateParams.fallEntryGrace`（`[SerializeField]`，
> 既有 `.asset` 缺 key 時由欄位初始化值安全退化，**不需要動資產**）。
> 以 `Time.time` **快照**實作而非累加 `deltaTime`——`CanEnter` 同一幀可能被
> `EvaluateInterrupts` 與 `EvaluateTransitions` 各問一次，快照天然冪等（`ActionState` 冷卻閘門同款）。
> ⚠️ **不是 Coyote Time**，本輪也沒有實作 Coyote Time。
>
> **② impact velocity 統一來源。** `PlayerRuntimeData.VerticalVelocity` 落地（ADR-002 §6-1 的
> 「第二個消費者」閘門由 walk-off falling 的落地分類達成）。`JumpState` 的 Normal／Hard 分類
> 改讀黑板，**不再用 `v₀ − g·elapsed` 公式**——公式假設「本次滯空有已知 launch」，非主動失地沒有，
> 沿用下去必然分裂成兩套公式。
>
> ## 🔑 發布點的關鍵細節（改動它就會壞）
>
> ```csharp
> data.JustLanded / JustLeftGround / IsGrounded   // 上一次 Move() 的結果
> data.VerticalVelocity = _verticalVelocity;      // ← 必須在這裡
> if (grounded && v < 0) v = reboundForce; ...    // 貼地夾持會銷毀 impact velocity
> ```
> 發布**必須**在 `reboundForce` 夾持之前，否則落地幀只會讀到 −2、所有落地都被分成 Normal。
> 這四行構成**同一瞬間的一致快照**：FSM 在 Update 讀到的 `IsGrounded` 與 `VerticalVelocity`
> 來自同一次 LateUpdate 寫入。⚠️ 代價是「只在**第一個 grounded 幀**正確」——
> 主動跳躍滯空遠超 `MinAirborneTimeBeforeLandingCheck`；非主動失地在 `OnEnter` 預先滿足該計時器。
>
> **唯一寫入者＝`MotionDriver`**，比 v0.10 草案（曾允許 `Project.Core` 狀態類別直寫）**更緊**。
> 狀態只讀不寫，注入仍走 `ApplyJumpLaunch`（ADR-002 選項 A 未破）。A5 已加對應 `WriterRule`。
>
> ## 為什麼沒開 ADR
>
> ADR-002 §6-1 **已經**決定了這件事，只是把時機延後並寫明「屆時重新界定 owner/writer/readers」。
> 這是**兌現既有決策**，走 `JustLanded`／`JustLeftGround` 同一條路徑（v0.10 定案 → 延後 →
> M2 消費者出現 → 寫進 Living Docs，當時也沒開 ADR）。**ADR-002 一字未動。**
>
> ## 自動驗證
>
> | | 結果 |
> |---|---|
> | `Project.Runtime.csproj` | **0 warnings / 0 errors** |
> | `Project.Tests.EditMode.csproj` | **0 errors**（4 個既有 `MSB3277` 組件版本 warning） |
> | EditMode ＋ PlayMode **執行** | ✅ **全綠**（使用者於 Unity 實跑） |
>
> 新增 EditMode regression（已跑，全綠）：
> `JumpState_FallEntryGrace_RequiresContinuousUngroundedTime`、
> `JumpState_PassiveFallEntry_StartsFallingAndDoesNotInjectLaunch`、
> `JumpState_ActiveEntry_StartsStartAndInjectsLaunchAsBefore`、
> `JumpState_LandingClassification_UsesBlackboardVelocityForEveryAirborneOrigin`（4 個 TestCase，兩種入口 × Normal／Hard）、
> `JumpState_PassiveFall_DoesNotGrantAirJump`，以及 A5 的 `VerticalVelocity` writer rule。
>
> ## ✅ 人工驗收：通過（使用者 2026-09-10 實跑）
>
> ⚠️ 使用者回報的是**整體無 bug、測試全綠**，不是逐項勾選。
> 以下 checklist 保留作為「這一輪交出去要驗什麼」的紀錄；
> 若日後出現 crossfade 或斜坡手感問題，**不應**把本節當成「已逐項驗過」的證據。
>
>
> 1. **跑 EditMode ＋ PlayMode 測試**（本輪只驗到編譯，測試尚未執行）。
> 2. **走出高台**（Walk／Run／左右腳 stride 中途離地）：是否在 ~0.1 s 內切到 `FallingLoop`？
>    有沒有明顯 pose snap／腿部抽回？第一版**刻意用一般 crossfade**，
>    不自然才依序考慮 ①transition duration ②`FallingLoop` 起播時間 ③最後才做專用短 Fall transition。
> 3. **斜坡／樓梯來回走**：不得出現 Move ↔ Falling 抽動。會抖就調高 `fallEntryGrace`（Inspector 可直接調）。
> 4. **空中按跳**：走出高台後按跳**必須沒有反應**。
> 5. **落地分類**：一般跳躍落地仍是 Normal；從高台走下去落到更低處應更容易觸發 Hard。
>    ⚠️ 換成實際速度後，離散積分會讓 |v_y| 比舊公式**略大**，Hard 可能比以前稍容易觸發——
>    **這是預期的**，屬 threshold tuning，本輪照裁決不調數值。
> 6. **回歸**：主動 Jump 的 Start／Falling／Land、Land2Move／Land2Run、LU／RU 左右腳選擇、
>    Hard Land recovery 鎖水平、NormalStop 被新意圖中斷——這些路徑本輪未改，但入口動過，值得回看一次。
>
> ## ⚠️ 本輪副作用（需要 Play 觀察，不是 bug）
>
> `CanEnter` 是 `EvaluateInterrupts` 每幀對**所有**狀態問的，所以現在**從 Roll 或 Action 中走出高台**
> 也會在 grace 後被 Jump 中斷進 Falling——是否發生完全由既有的 `CanBeInterruptedBy` 政策決定，
> 本輪**沒有**動那套政策。若實測覺得「翻滾出平台不該被打斷」，那是 interrupt 政策問題，不是本輪入口問題。
>
> ## 明確沒做（維持裁決）
>
> ⛔ Coyote Time　⛔ 新的 `Falling`／`Airborne` StateType　⛔ Hard Land threshold 調值
> ⛔ 專用 WalkOffLedge／RunOffLedge 動畫或 foot-phase 系統　⛔ 改 `ShouldEnterFalling`（主動跳躍的過頂判定維持公式）
> ⛔ 任何 `.asset`／`.prefab`／場景變更　⛔ 任何 Git 操作

> # ✅ 2026-09-09 — Jump Landing gameplay phase 修正：HardRecovery 鎖水平、NormalStop 可被新意圖中斷
>
> ## 根因與修正
>
> - 診斷與實測描述吻合：`JumpState` 落地後只累加 land timer；`CanTransitionAway` 一律等 duration，
>   所以 NormalStop 看不到落地後才出現的新 Move Intent；同時 `OnUpdateMotion` 始終走
>   `ExecuteBaseMovement`，Hard Land 仍會施加 Movement Output 的水平速度。
> - `JumpState` 新增**私有** `LandingPhase`：`None / NormalStop / NormalContinue / HardRecovery`。
>   沒有新增全域 state、interrupt 或 movement-lock framework。
> - `NormalStop` 若在 land 開始後收到有效 Move Intent，立即把 phase 釋放為 `None`；仍由 FSM
>   自然轉入 Move，再由既有 state-key routing 播動畫。`NormalContinue` 不接受這條提前釋放，
>   因此持續同一 Move Intent 不會在下一幀跳過 Land2Move／Land2Run。
> - `HardRecovery` 必須等 gameplay timer 結束；有意圖轉 Move，無意圖轉 Idle。duration 目前沿用
>   Bake／fallback 的 authored 秒數，但 owner 是 gameplay timer，不依賴 clip 播完 callback。
>   即使 Hard Land 視覺引用失效，也會用既有 `LandFallbackDuration`，不會靜默解除 recovery。
> - `MotionDriver.ExecuteVerticalOnlyMovement` 是窄用途執行出口：保留 facing、重力、grounded
>   同步與 `CharacterController.Move` collision，只不施加水平 Movement Output；沒有清空
>   `MoveDirection`，且 Transform writer 仍只有 MotionDriver。
>
> ## 自動驗證
>
> - Runtime／EditMode／PlayMode 三個 C# 專案：**0 compile errors**（只有既有 Unity 參考組件 warning）。
> - Unity EditMode：**283 total／282 passed／0 failed／1 skipped**（既有 NavMesh 環境限制）。
> - Unity PlayMode：**6 total／6 passed／0 failed**。
> - 新增六項 regression：`HardLand_HorizontalMotionIsSuppressedDuringRecovery`、
>   `HardLand_MoveIntentDoesNotExitBeforeRecoveryEnds`、
>   `HardLand_RecoveryEndsWithIntent_TransitionsToMove`、
>   `HardLand_RecoveryEndsWithoutIntent_TransitionsToIdle`、
>   `NormalLand_StartedWithoutIntent_NewIntentTransitionsToMoveImmediately`、
>   `NormalLandContinue_HeldIntent_DoesNotImmediatelySkipTransition`。
> - 既有 selector tests 已守住 Normal Land／Land2Move／Land2Run 與 LU／RU landing selection；
>   architecture regressions 守住 MotionDriver single Transform writer。airborne／非 Hard landing 的
>   `ExecuteBaseMovement` 分支未改，已做 targeted code-path 檢查；實際 control feel 仍列在人工驗收。
>
> ## 🎮 仍需人工 Play 驗收（只剩 feel／視覺銜接）
>
> 1. 高處重落地時持續推方向：recovery 期間不得水平滑動，結束後有意圖進 Move、無意圖進 Idle。
> 2. 無輸入開始 Normal Land，動畫剛開始後立刻推方向：應立即 blend 到 Move；第一版不加 minimum land time。
> 3. 落地當幀已有輸入的 Land2Move／Land2Run：持續按住時 transition clip 必須完整保有既定作用。
> 4. 回看空中控制、Normal Land、Land2Move／Land2Run 與 LU／RU 左右腳視覺選擇；若真的看到
>    1–2 frame flash，再以實測證據決定 minimum land time，本輪不預建。

> # ✅ 2026-09-09 — **MAP 跳躍全鏈打通**：物理來自 MAP、動畫已分段接線、EditMode 278 條全綠
>
> ## 這輪完成什麼
>
> ```
> Jump_place_ALL_short → Bake_Jump_place_ALL_short → JumpStateParams.Stages[0].Bake
>    （MAP 素材）           apex 0.9535 / g 16.78          （gameplay launch physics）
> ```
> **跳躍物理自此完全來自 MAP 自己的量測，Mixamo `Bake_Jump` 退場。**
>
> 🔑 **為什麼用 `_short` 而不是長版**（使用者裁決，實測支持）：兩者 apex **完全相同**（0.9535），
> 但 `AutoTakeoffDelay` 差 5.6 倍——長版 **0.4871s** vs `_short` **0.0873s**。
> 該欄位直接閘住 `ApplyJumpLaunch` ⇒ 用長版等於按下跳躍後乾等 0.49 秒才離地。
>
> ## 落地選擇規則（使用者 2026-09-09 裁決，已實作）
>
> ```
> 先判斷是不是重落地
> ├─ 是 → JumpIdleLandHard（單一，短路，不看移動意圖）
> └─ 否 → 再看移動意圖
>        ├─ 沒移動 → NormalLand（依 tier / LU-RU）
>        └─ 有移動 → NormalLandToMove（依 tier / LU-RU）
> ```
> - 重落地判準＝**落地瞬間垂直速度** `|v_y| > hardLandingSpeed`（預設 8 m/s），
>   由 state 自己的 launch 參數推導 ⇒ **零黑板變更**（ADR-002 §6-1 的 `VerticalVelocity` 仍未落地）。
>   ⛔ **刻意不用高度差**：落地點越低 elapsed 越長、`|v_y|` 自然越大，「摔得比跳得深」自動成立。
> - 門檻推導：正常跳躍落地 `√(2×16.78×0.9535) ≈ 5.66 m/s` ⇒ 門檻**必須高於它**；8 m/s ≈ 1.9m 落差。
> - `hardLand` 收斂成**單一** variant、`hardLandToMove` **移除**（重落地＝動量被吃掉，不存在「重落地且續走」）。
> - `didEnterFalling` 已移除（判準改速度後無讀者）。
>
> ## 接線實際落地（**先前一直沒跑，這是使用者看到「動作還是舊的」的原因**）
>
> | | 結果 |
> |---|---|
> | Transition 資產 | 17 個新建（＋舊 `Jump.asset`） |
> | Facade 映射 | 17 → **34 筆** |
> | 既有映射 | `Idle`／`Move`／`Roll`／`Jump`／`Throw_*`／`Spell_*`／收步 **全部完好** |
> | 變體表 | **17 格全滿** |
>
> 📌 **易混淆**：`Locomotion` 是 transition **資產名**，Facade 的 StateKey 是 **`Move`**
> （`BaseState.AnimationKey` 回傳 `Type.ToString()`）。查「Locomotion 映射還在嗎」會查到 0，那是正常的。
>
> ## 🔒 PHYSICS_GUARD 的期望值已更新（**不要刪掉這道守衛**）
>
> `WireJumpAnimationSet` 有一道守衛，斷言 `Stages[0].Bake` 是預期的 launch 來源。
> 它原本期望 Mixamo `Bake_Jump`（工具誕生時的前提是「接線只碰視覺」），**本輪 verify 時實際攔下了**
> 這個已被推翻的前提。期望值已改為 `LaunchSourceBakePath`，**形狀刻意不變**——
> 要守的性質是「接線工具不得偷改物理來源」。⛔ 換來源請走 `PointJumpPhysicsAtLaunchSource`。
>
> ## 測試
>
> **EditMode 278 條：277 passed／0 failed／1 skipped**（既有的 NavMesh 自我 `Assert.Ignore`）。
> 新測試全綠：`JumpAnimation_LandingSpeed_SelectsSameHardLandRegardlessOfMovement`／
> `_HardLandingSpeedBoundary_FlipsOnlyAboveThreshold`／`_CurrentLaunchData_NormalJumpDoesNotSelectHardLand`／
> `JumpLoop_NoLanding_ReportsTakeoffAndApexButHonestZeroAirTime`。既有 `A31`／`A32`／`W11`／`W12` 亦綠。
>
> ## 🎮 待 Play 驗收（重點在後兩項，是這輪新判準）
>
> 1. **idle 起跳** —— 前搖 0.087s，應明顯比先前俐落
> 2. **跑步起跳落地續跑** —— LU/RU 是否選對腳；`Land2Run` 僅 0.43s，應幾乎無停頓
> 3. 🆕 **空中急停** —— 跑步中起跳、**空中放開輸入** ⇒ 應播 `Land` 而非 `Land2Run`
> 4. 🆕 **重落地** —— 落差 > 1.9m 跳下 ⇒ 應播 `JumpIdleLandHard`，且**不論當下有無移動意圖**
>
> ## ⛔ 已知缺口（素材限制，非邏輯錯誤）
>
> `hardLand` 只有 `JumpIdleLandHard` 一支 ⇒ **跑步時重落地也會播站定的硬落地**。
>
> ## 🧹 待清理
>
> - `PointJumpPhysicsAtLaunchSource` 是一次性遷移入口，驗收後應刪除
> - Mixamo `Bake_Jump` 已成孤兒資產，檔案仍在，未刪
> - 備份：`_BACKUP_MAP_importsettings/`（含 `X Bot.prefab` 與 `JumpStateParams.asset` 的接線前快照）

> # 📐 2026-09-09 — MAP 跳躍：**量測來源與播放來源的配對定案（待使用者確認）**
>
> ## 結論一句話
>
> **配對不是 1:1。只有一支 clip 有資格當 launch data 來源（`Jump_place_ALL`）；播放則是五個家族。**
> ⇒ 需要的是「**一組物理 ＋ 五組視覺**」，不是「五組物理 ＋ 五組視覺」。
>
> ## 證據
>
> **① `Jump_place_ALL` 跨基線模式完全收斂**（B/C/D 三種演算法）：
> base 0.7650~0.7656（差 0.6mm）／**apex 0.9535（四位小數完全相同）**／air 0.7400~0.7457／g 13.72~13.93。
> ⇒ 它自己就能無歧義定義地面 ⇒ **有資格當單一真相來源**。
> 交叉驗證：`JumpIdleStart` 0.954／`Jump_place_ALL_short` 0.954／Mixamo 0.924 —— 三個獨立窗口一致。
>
> **② walk/run 的 `_ALL` 同樣不自足**（推翻我先前「`_ALL` 當量測來源」的推廣）：
> `Jump_run_lu_ALL` 的 apex 在 0.379~0.668、g 在 20~52 之間隨模式跑；
> `Jump_run_ru_ALL` 的 C 模式甚至測到完全不同的事件（apex 0）。
> **根因（使用者指出）**：run/walk 從跑步腳相蹬地起飛後**不再回到同一個地面** ⇒
> 換剪裁窗口變不出地面，**這是資訊缺失，不是偵測器不夠聰明**。
>
> **③ 但每家族各自的 launch data 是不需要的**：播放端是**條件驅動**（Kubold 模型，
> `m_HasExitTime: 0`）⇒ `*Start` 一定被落地事件切斷，**它的固有弧線本來就播不完** ⇒
> 「走路起跳弧線 0.57／跑步 0.67」這些數字沒有一個會被完整播出來。
>
> ## 提議的配對
>
> ```
> 量測（唯一 launch source）：Jump_place_ALL → JumpStateParams.Stages[0].Bake
> 播放（五家族視覺）        ：*Start / FallingLoop / *Land / *Land2Move → 變體表
> ```
>
> ⇒ **`JumpStateParams.Stages[]` 維持單段**，不需要依速度階層選 stage。
>
> ## 連帶：baseline detector 的取捨也定了
>
> 只需要量一支、且它跨模式收斂 ⇒ **不需要為 walk/run 繼續複雜化偵測器**（使用者已叫停 E）。
> 但 baker 仍必須修（現況 A 對**所有** clip 都給 0，含 Mixamo）。四模式 regression 結果：
>
> | | Mixamo | JumpIdleStart | RunFwdStop_LU | Roll | FootPhaseCurve |
> |---|---|---|---|---|---|
> | **B**（t=0 後） | ✅ | ✅ | ❌ 0 vs 磁碟 0.052 | ✅ | ✅ 不受影響 |
> | **D**（起飛前最低） | ✅ | ✅ | ✅ 0.0519 | ✅ 逐位吻合 | ✅ 不受影響 |
>
> ⇒ **建議採 D 當正式的 clip-local ground baseline 定義**（B 目前暫留在 `MotionBakeEditor`，未定案）。
> 📌 **`FootPhaseCurve` 完全不受基線取法影響**（A/B/C/D 的 key 數逐支相同）——這條風險已退場。
>
> ## ✅ 四件事已全部執行完畢（2026-09-09，使用者核可）
>
> | | 狀態 |
> |---|---|
> | ① baker baseline → **D** | ✅ `MotionBakeEditor.ResolveClipLocalGroundBaseline`。**定位已寫進 XML 註解**：這是通用的 feature-analysis baseline，**不是**為了讓所有 JumpStart 都能成功 Bake；沒有可觀測地面的 clip 退化為 0 是**資訊缺失的正確反映**，⛔ 不得為此再加規則。A/B/C 三個被否決的候選與各自失效證據一併留在註解裡 |
> | ② 烘 `Jump_place_ALL` | ✅ apex **0.95352**／takeoff **0.48706**／air **0.74001**／g **13.930**——與唯讀探針的 D 模式預測**逐位吻合** |
> | ③ `Stages[0].Bake` 改指 MAP | ✅ `Bake_Jump` → `Bake_Jump_place_ALL`（guid `13bd3553…`）。**跳躍物理自此完全來自 MAP 自己的量測** |
> | ④ 清 throwaway probe | ✅ `AnimationBatchOps` 1724 → 1326 行，殘留掃描 0 |
>
> **無波及**：時間戳確認只有跳躍家族被重烘（Mixamo `Bake_Jump` 22:23／`Bake_RunBwdLoop` 17:49／
> `Bake_Stand To Roll` 17:16 皆為舊值）。`JumpStateParams` 的 diff 只有 `Bake` 一個 guid 變更，
> 其餘是 Unity 補上 `animationVariants` schema 的空欄位（切片 A 加的，本來就會在下次存檔時出現）。
>
> ## 🎮 Play 要驗的具體預測（不是「感覺看看」）
>
> | | 舊（Mixamo） | 新（MAP） | 變化 |
> |---|---|---|---|
> | apex | 0.9244 | 0.9535 | **+3%（幾乎不變）** |
> | g | 17.712 | 13.930 | −21% |
> | 發射初速 `v=√(2gh)` | 5.72 m/s | 5.15 m/s | −10% |
> | **滯空時間** | 0.646 s | 0.740 s | **+15%** |
>
> ⇒ **跳同樣高，但明顯更「飄」**（滯空多 15%）。若 Play 感覺跳躍變慢變浮，那是**預期的正確結果**，
> 不是 bug；要調回原本手感就動 `JumpStateParams` 的三個 Multiplier，⛔ 不要回頭改 Bake。
>
> ## ⛔ 仍未做（刻意）
>
> - **Walk／Run 的 `*Start` 不追自己的 `AutoApexHeight`**（使用者裁決）：它們只提供左右腳視覺／
>   起跳姿態／gait 對應動畫。第一版 Idle／Walk／Run **共用同一組垂直 launch physics**。
> - 「跑步起跳是否該更低更遠」＝**下一層 gameplay design**，不是 Bake 工具該決定的。
> - `Bake_Jump`（Mixamo）成為孤兒資產，**檔案仍在**，未刪除。
> - `PointJumpPhysicsAtLaunchSource` 是一次性遷移入口，**驗收後應刪除**。

> # 🔴 2026-09-09 — **烘焙器 bug 確診**：跳躍物理特徵對**所有** clip 都量不出來
>
> ## 一句話
>
> `MotionBakeEditor.cs:450` 的 `rootTransform.SetPositionAndRotation(Vector3.zero, ...)`
> 讓**基線與採樣分屬兩個參考框** ⇒ 起跳偵測從第 0 幀就恆為「騰空」⇒ 找不到邊沿 ⇒ 全欄位退化為 0。
> ⚠️ **這不是 MAP 素材的問題——連 Mixamo 的 `X Bot@Jump` 也一樣量不出來。**
>
> ## 單變數對照實驗（唯讀探針，同一份採樣、只切換那一行）
>
> | | 基線 | `JumpIdleStart` 分析結果 |
> |---|---|---|
> | **不重設 root** | 0.7643 | takeoffDelay **0.0855**／apex **0.9538**／airTime **0.6854**／g 16.24 |
> | **重設 root**（＝烘焙器現況） | 0.0873 | **0／0／0**／g 9.81 |
>
> 四支 clip（含 Mixamo 對照）**行為完全一致**。
> 📌 兩個基線的差 `0.7643 − 0.0873 = 0.677` ≈ 這批 clip 的起始 root Y（0.68）——**算術對得上，機制確認**。
>
> ## 機制
>
> 基線在 root 歸零、**未套用動畫**時捕捉 ⇒ 得到 rig 真實踝高 0.0873。
> 但 `SampleAnimation` 之後會把 root 放到 **clip 自身的 root 高度**（≈0.68m，因為 Jump preset
> **刻意不把 Y 烘進姿勢**）⇒ 每一幀的腳高 ≈ 0.68 ＋ 踝高，遠超 `0.0873 + 0.03` 門檻。
>
> ⇒ 🔑 **world-space 基線法只在「Y 已烘進姿勢」的 clip 上成立**（locomotion 那批 Y ✅ 所以沒事）。
> **Jump 家族 preset 的 Y ❌ 正好使它失效**——preset 與偵測器互相矛盾，而這個矛盾一直沒被發現，
> 因為在此之前**沒有人重烘過任何跳躍 clip**（`Bake_Jump` 的好數值是更早期的產物）。
>
> ## ⚠️ 連帶影響
>
> `Bake_Jump.asset` 現存的值（0.736／0.924／0.646）**無法用目前的烘焙器重現**。
> ⛔ 任何人重烘它都會把跳躍物理清成 fallback 常數。**修好偵測器之前不要重烘 `Bake_Jump`。**
>
> ## ✅ 已確立（都靠量測，非推論）
>
> - **素材資料充足**：`JumpIdleStart` 有 0.0855s 前搖／0.95m 頂點／0.685s 滯空，`v = √(2gh)` 三輸入齊全
> - **`takeoff detector` 判準不粗**：`IsAirborne` 是雙腳 AND，單腳墊腳／抬跟不會誤判
> - **不需要裁 clip、不需要補資料模型、不需要留 Mixamo 當長期方案**
>
> ## ⛔ 我在這輪連續錯了三次，全是同一類錯誤（留作教訓）
>
> ①「`*Start` 第一幀已離地」→ 否證（`firstGroundedFrame = 0`）
> ②「50m 墜落是刻意設計、所以不落地量不到」→ 否證（它**有**落地，airTime 0.685）
> ③「資料在 `_ALL` 裡，`*Start` 不適合當量測來源」→ 否證（`*Start` 量得到且數字健康）
> **三次都是把工具行為誤讀成素材性質。** 使用者的提醒（「避免把工具缺陷誤認成素材限制」）擋下了
> 一個會讓資料模型繞著假限制設計的方向。

> # 🟡 2026-09-09 — **切片 A（跳躍視覺分段）程式完成**，待接線＋測試
>
> Codex 實作、Claude review。純 `.cs`，零資產、零 Git。`dotnet build` Runtime ＋ Tests.EditMode
> **Claude 獨立複驗 0 error**（不是只採信 Codex 自述）。
>
> ## 落地形狀
>
> - `JumpState` 新增內部相位 `Start / Falling / Land` ＋ 可變 `AnimationKey`（沿用順序 5 既有的「鍵變更即重播」）
> - **`Start → Falling` 用物理不用 clip 長度**：`v_y = v − g·(elapsed − takeoffDelay) <= 0` 且未落地
>   ⇒ ⛔ 沒有新增黑板欄位（垂直速度依 ADR-002 §6-1 仍封裝在 `MotionDriver`）
> - **Land 變體＝路徑決定**（Kubold 核心規則）：`Start→Land` 普通／`Start→Falling→Land` **Hard**
> - LU/RU **重用 `LocomotionStopSelector.SelectByEntryPhase`**，⛔ 沒有另寫一套選腳
> - 變體表是 authored data（`JumpStateParams` ＋ 既有 `LocomotionStopVariant`），⛔ 不是 Core 字串常數
> - 新介面 `IFootPhaseSource`（窄介面，`LocomotionModel` 實作）——**刻意不塞進 `IMovementModel`**：
>   未來的游泳／飛行 model 沒有腳相，塞進去會變 fat interface
>
> ## 🔴 我漏烘了 `FallingLoop`（Codex 抓到的）
>
> 前一輪那 16 支**不含 `FallingLoop`** ⇒ 沒有 `Bake_FallingLoop.asset` ⇒
> `LocomotionStopVariant.IsValid` 對 Falling 格恆為 false ⇒ **`Falling` 相位無法啟用
> ⇒ `LandHard` 那半條路是死的**。⚠️ **接線前必須補烘**（`AnimationBatchOps` 已有機制，加一個 clip 名即可）。
>
> ## ⚖️ Codex 的一處偏離（已審，接受）
>
> 我原本傾向本切片不動 `CanTransitionAway`。Codex 改了，理由成立：
> **FSM 在 `IsLanded` 為 true 的同一次 Tick 就離開 Jump ⇒ 順序 5 永遠看不到 Land 鍵。**
> 新式 `IsLanded && (!_hasLandVisual || _landElapsedTime >= _landDuration)` ——
> **未接線時 `_hasLandVisual == false` ⇒ 逐字等於舊行為**，退化閘門正確。
> 📌 語意區別已寫進註解：**進入** Land 相位由落地事件驅動；**相位長度**才讀 clip 長度。
>
> ## 🚧 待辦
>
> 1. **補烘 `FallingLoop`**（我來，需要 Unity 關閉）
> 2. **接線**（見下方清單）
> 3. **跑 EditMode**（⚠️ Unity 現在開著 ⇒ 我跑不了 batchmode，請用 Test Runner）
>
> # 📐 2026-09-09 — Kubold 對 MAP 跳躍的**原始用法**（從 `PlayerMaleController.controller` 實拆）
>
> 來源：`Assets/MovementAnimsetPro/Mecanim/PlayerMaleController.controller`（資產自帶，比任何文件都準）。
>
> ## 五條事實
>
> 1. **所有跳躍轉移都是 `m_HasExitTime: 0`** ⇒ **完全條件驅動，不看 clip 播完**。
>    ⇒ 「Start 要不要播到結束」這題**在 Kubold 的模型裡不存在**——它由旗標決定，clip 長度不參與。
> 2. 只用三個參數：**`IsJump`／`IsFalling`／`InputMagnitude`**。
> 3. **`JumpIdleStart` 的兩條出口**（都 `HasExitTime: 0`）：
>    - `IsJump == false` ＋ `InputMagnitude < 0.2` → **`JumpIdleLand`**（停）
>    - `IsJump == false` ＋ `InputMagnitude > 0.2` → **`JumpIdleLand2Walk`**（續走）
> 4. **`FallingLoop` 不是接在 Start 後面的直線**，而是「你在空中」的**匯流點**：
>    由 `IsFalling == true` 從 **5＋ 個來源**拉進去（controller 另有 49 條 AnyState 轉移）。
> 5. 🔑 **重落地的判準是「路徑」不是「量測」**：
>    - `Start → Land` ＝ 普通落地（`JumpIdleLand`）
>    - `Start → FallingLoop → Land` ＝ **`JumpIdleLandHard`**
>    ⇒ ⛔ **不需要量落下時間或高度**。`FallingTime` 參數雖然存在，但**這些轉移沒有用到它**。
>
> ## 對映：三個訊號我們**全部已經有**，不需要新黑板欄位
>
> | Kubold | 我們 |
> |---|---|
> | `IsJump` | `JumpState` 為 active（`_phase`／`IsLanded`） |
> | `IsFalling` | `!data.IsGrounded` |
> | `InputMagnitude`（門檻 0.2） | `MovementIntent.DesiredSpeedNormalized`（我們 FSM 既有門檻 0.1，同量級） |
>
> ## 🔑 這解掉了排序問題：視覺與物理可以拆成兩個獨立切片
>
> **動畫切換完全不需要 `AutoApexHeight`／`AutoAirTime`** ——它們只餵物理
> （`JumpState.BuildStages` 的 `v = √(2gh)`）。⇒
>
> - **切片 A（視覺分段）**：`JumpState.AnimationKey` 可變 ＋ 依上表三訊號選鍵。
>   **現在就能做，不依賴重烘、不依賴裁 clip。**
> - **切片 B（物理對齊）**：需要 `AutoTakeoffDelay`（何時 `ApplyJumpLaunch`）與
>   `AutoApexHeight`／`AutoCalculatedGravity`。**卡在分析器對 MAP clip 偵測不到起跳**（全 0）。
>
> ⚠️ **B 未解時 A 仍可上**：物理沿用既有 `Bake_Jump`（Mixamo）的參數，視覺換成 MAP 分段。
> 兩者不對齊的部分是**起跳瞬間的腳離地時機**，屬手感，不是壞掉。
>
> # 🟡 2026-09-08（夜）— MAP 跳躍：**SOP ＋ 烘焙已完成**，Start clip 影格範圍的疑慮**已排除**
> ### ⚠️ 更正：先前「`*Start` 沒修剪」的判斷**是錯的**——每支子 clip 都有自己的 `takeName`，
> ### 是各自獨立的 take，不是同一條時間軸的切片，`Jump_place_ALL` 的 0–68 不能拿來當基準比較。
>
> ## ✅ 已完成（Claude 以 batchmode 執行並逐項驗證）
>
> **新增兩個入口**：`MotionClipImportSOP.ApplyJumpTo`（per-clip Jump preset，與既有 Locomotion-位移
> 入口共用抽出的實作——**第二個使用者出現才抽**）＋ `AnimationBatchOps.ApplyJumpSopAndBake`。
>
> **16 支分段跳躍 clip 已套 Jump preset ＋ 烘焙**，產出 `Assets/ScriptableObjects/Motion/Bake_Jump*.asset`（**有進版控**）。
>
> ⛔ **刻意不含 `Jump_*_ALL`**：那是把「起跳→滯空→落地」烘成**固定長度**的整段 clip，
> 與 **ADR-002（Accepted）**「滯空時間由物理決定」直接衝突。分段版才對得上物理時點。
>
> ### 🔒 隔離驗證（這是本輪最重要的檢查）
>
> 跳躍住在**主檔** `MovementAnimsetPro.fbx`——那支同時裝著 `Throw_*`（ADR-004 已結案）、
> `RunFwdLoop`（locomotion 前進取樣點）、Crouch、ButtonPush。**整檔套 preset 會全部灌壞。**
>
> ✅ diff 新舊 `.meta`：**160 個變更行，每一行都落在我指名的那 16 支**，其他家族零波及。
> ✅ `JumpIdleStart` 實際值＝`loopBlendOrientation: 1`／`loopBlendPositionY: 0`／`loopBlendPositionXZ: 1`／
> `heightFromFeet: 1`／`loopTime: 0` ⇒ **與 dev-spec §0.4「Jump 家族」逐項相符**。
> ✅ `RunFwdLoop` 仍是 `Y=1／XZ=0／loopTime=1`（Locomotion-位移），**未被動**。
> 📌 備份：`D:/Unity Project/_BACKUP_MAP_importsettings/`（MAP 不在版控，git 救不回來）。
> 📌 ⚠️ Unity 的 YAML 欄位名是 **`loopBlend*`** 不是 API 的 `lockRoot*`——查證時別找錯欄位。
>
> ## 🔴 需要裁決：`*Start` clip 的影格範圍看起來沒修剪過
>
> | clip | firstFrame–lastFrame | BakedDuration |
> |---|---|---|
> | `JumpIdleStart` | **14–120** | **3.53 s** |
> | `JumpRunStart_LU` | **0–120** | **4.00 s** |
> | `JumpIdleLand` | 37–68 | 1.03 s |
> | `Jump_place_ALL`（未套用） | 0–68 | 2.27 s |
>
> **Land 類的範圍是收斂的（31 影格），Start 類卻一路到 120＝整個 take 的尾端**——
> 而 `Jump_place_ALL` 證明整段跳躍只到 frame 68。⇒ **`*Start` 的 `lastFrame` 極可能是未修剪的預設值。**
>
> **影響**：分段方案要用 `BakedDuration` 當起跳段的長度；3.5–4 秒顯然不是起跳時間 ⇒ **接線前必須先修剪**。
> ⛔ **我沒有自己改**：修剪影格範圍是 authoring（「起跳在第幾格結束」要看畫面），
> 而且 SOP 工具明文不碰影格範圍（由 Unity 填入）。**這是你在 Editor 看著決定的事。**
>
> ## ⏭️ 下一步（程式，不依賴上面那個裁決就能開始）
>
> `JumpState` 目前**沒有覆寫 `AnimationKey`** ⇒ 整個跳躍只播一個鍵。要做分段需要讓它可變——
> **機制早就存在**：管線順序 5 已支援「動畫鍵變更時重新提交 `Play()`」（ADR-004 落地，
> dev-spec §2.1 還註明「Idle／Move／Jump／Roll 的鍵為常數」）。
> 左右腳起跳同理：`LocomotionStopSelector` **已經在用 `FootPhaseCurve` 選 LU/RU 收步變體**，
> 起跳選 LU/RU 是同一個問題形狀 ⇒ 第二個使用者，合法一般化。
> ⇒ **三件事都不需要新 ADR**，走 Living Docs。

> # 🟡 2026-09-08（第二輪）— **S3b facing 收斂 ＋ HeadLook 回正** 程式已落地，**尚未驗證**
>
> **開場**：讀本段 ＋ `docs/16-review-protocol.md`（⚠️ 持續有效）。
> 規格正本：`docs/14` §4「S3b」／`docs/15` §18.3 的兩份修正紀錄。
>
> ## ✅ 殘留已清（2026-09-08）
>
> - **throwaway 接線方法已從 `AnimationBatchOps.cs` 整段刪除**（649 → 500 行）。
>   ⚠️ 該檔**仍有另一個既有 spike**（2026-09-07～08 的 7 支 strafe clip SOP），**那個不是本輪的，別誤刪**。
> - `IAimSource` 已進 `Project.Runtime.csproj`、`.meta` 已生成（Unity 重生過）。
> - 📌 曾出現 `dotnet build` 對 `Kybernetik.Animancer.csproj` 失敗（Burst 屬性找不到，5 個 error，
>   **我們的程式 0 error**）——那是 Unity 重生 csproj 時掉了 Burst 參考的側路問題，
>   Unity 自己的編譯器不受影響。開一次 Unity 即恢復。
>
> ## 🚫 Codex **不能**跑 Unity batchmode（本機實測，2026-09-08）
>
> Codex 的沙箱**連不到 Unity 的授權 IPC**（`Connection to channel LicenseClient-USER refused`），
> 且讀不到 `C:\Users\USER\AppData\...`。⚠️ **這不是可重試的錯誤，是結構限制**——
> 本機的 Unity Hub 與 Licensing Client 當時都正常執行中，是沙箱擋住的。
> ⇒ **凡是需要 Unity Editor／batchmode 的工作，不要交給 Codex**；它只能做純 `.cs`。
>
> ## 🔬 2026-09-08 Play 回報的三件事（已逐項查到根因）
>
> ### ① 敵人沒有受擊 —— **真 bug：距離幾何互斥，玩家根本打不到**
>
> 整條授權鏈**全部正確**：`MeleeSlash1Definition` 的 `EmitsRelease: 1`／`ReleaseNormalizedTime: 0.4`；
> `actionSinkBindings` Slot 1 → `MeleeHitboxSink`；hitbox 與 sink **同一顆 GameObject**、`m_IsTrigger: 1`、
> 初始 disabled；`DamageDefinition`（Slot 100 ＝ Reaction）**確實**在 `EnemyStateMachineConfig` 裡。
>
> **根因（使用者提出「為什麼之前可以」後才查對方向）**：
>
> | 量 | 值 |
> |---|---|
> | 敵人的交戰帶（`Y Bot`） | `minimumEngagementDistance: 1.25` ～ `maximumEngagementDistance: 2` |
> | 敵人的攻擊距離 | `attackRange: 1.8` |
> | **玩家 hitbox 的可及範圍** | BoxCollider `1.4 × 1.5 × 1.6`、center `(0,0,0)` ⇒ **水平僅 ±0.7 (x)／±0.8 (z)** |
>
> ⇒ **敵人最近只站到 1.25 m，玩家最遠只打得到 0.8 m。兩者永不重疊。**
> 敵人卻能從 1.8 m 打到玩家 ⇒ **單方面挨打**，與影片一致。
>
> **為什麼「之前可以」**：`AIMovementSource` 的 **engagement band（Hold／Approach／Retreat）＋ hold strafe
> 是新加的**（`git diff` 可見 `holdStrafeSpeedNormalized`／`ResolveHoldStrafeDirection`／band gizmo 全是 `+`）。
> 在那之前敵人是**一路貼上來**的，自然落在 0.8 m 的盒子裡。
> ⚠️ **`Y Bot` 的 collider 與 `MeleeHitboxSink` 的命中邏輯一行都沒改**
> （`MeleeHitboxSink` 只改了 `Release()` → `Release(in context)` 的簽章）——**不是它們壞掉，是距離設計走開了。**
>
> **⛔ 已被推翻的假說（留著避免重蹈）**：我第一版判斷是「`Y Bot` 只有 `CharacterController`、沒有 Rigidbody
> ⇒ `OnTriggerEnter` 不觸發」。**那個條件在「之前可以」的時候同樣成立** ⇒ 解釋不了時間差，**不是根因**。
> ⚠️ 但它**也還沒被證偽**——因為敵人從來沒進到盒子裡，trigger 事件根本沒機會被測到。
> ⇒ **等距離對上之後若仍打不到，才回頭驗這條**（`Y Bot` 加 `Rigidbody` ＋ `Is Kinematic`）。
>
> **要裁決的是設計，不是修 bug**：三個數字要對齊成一套（玩家可及／敵人交戰帶／敵人攻擊距離）。
> 📌 順帶暴露的不對稱：**敵人有明確的 `attackRange`，玩家沒有**——玩家的「攻擊距離」是
> BoxCollider 的尺寸隱含的，而且 center `(0,0,0)` 表示**背後也打得到**。
>
> ⚠️ **測試覆蓋缺口（誠實記錄）**：`CombatContextTests.CreateEnemy` 治具**額外加了 `SphereCollider`**，
> 比真實 prefab 寬鬆；而 `MeleeHitboxSink` 的測試是直接呼叫 `TryRequestHit`，**完全沒有經過
> `OnTriggerEnter`** ⇒ **目前沒有任何測試涵蓋真正的命中路徑**。A26 守的是「時機的擁有權」，不是「打得到」。
>
> ### ② 攻擊沒有軟鎖定 —— **預期行為，不是 bug**
>
> 兩份 Definition 的 YAML 裡**都沒有 `Targeting` 欄位** ⇒ 吃程式預設 `CameraForward`
> ⇒ 一律朝鏡頭、不吸敵。**這正是 `docs/11` §8.3 裁決要的。**
> **修法＝Inspector 一個欄位**：把 `MeleeSlash1Definition` 的 **Targeting** 改成 `CameraConeSoftTarget`。⛔ 不必改程式。
>
> ### ③ 武器破圖 —— **洋紅＝shader 在當前管線下不存在**
>
> 專案 `GraphicsSettings.m_CustomRenderPipeline` **有指定**（URP）；而 EEJANAI 那批材質
> （含 `swordmaterial.mat`）全部指向 shader guid `be891319084e9d147b09d89e80ce60e0`，
> 該 guid **在 `Assets/` 與 `Packages/` 都找不到** ⇒ 那是 **Built-in RP 的 shader** ⇒ URP 下渲染成洋紅。
> **修法**：Render Pipeline Converter，或直接把材質 shader 改成 `Universal Render Pipeline/Lit`。
>
> **位置沒調過**：`WeaponSocket` 的 `localPosition`／`localEulerAngles`／`localScale` 全是預設值。
> 這是**設計預期的手動步驟**——該欄位的 Header 寫著「進 Play 後選中生成出來的武器即可讀出對的數值」。
>
> ✅ 順帶：`weaponPrefab` 已改指 `Sword.prefab`（guid `06851bed…`）⇒ 先前的 `InvalidCastException` 應已消失 ⇒ **P5 應該會轉綠**。
>
> ## ✅ 測試已實跑（2026-09-08，Claude 以 batchmode 執行，非推測）
>
> | 套件 | 結果 |
> |---|---|
> | **EditMode** | **272 條：271 passed／0 failed／1 skipped** |
> | **PlayMode** | **5 條：4 passed／1 failed** ← 見下，**不是契約違規** |
>
> 本輪新增／修改的測試**全部 passed**：`W11`／`W12`／`A32`／`TC6C`／`TC6D`／`TC6E`／`TC7`／`TC7B`／
> `TC6A`／`TC6B`／`SoleHeight_SlopePenetrationCrossover_IsContinuous`／`SoleHeight_FlatGround_EqualsAnkleOnlyPlane`；
> 既有的 `A28`／`A31`／`T30`／`T31`／`TD5` 亦未被打壞。
>
> 唯一 skipped ＝ `AIMovementSource_WithoutCamera_ProducesNonZeroWorldDirection`，
> **既有的自我 `Assert.Ignore`**（EditMode 放不上臨時 NavMesh，架構面由 A30 守住），非本輪造成。
>
> ## 🔴 P5 紅了，但**不是 facing 違規**——它抓到了別的東西
>
> P5 自己的斷言**零違規**（XML 裡沒有任何「顆 CharacterFacingSource」訊息）⇒ **每隻角色都恰好一顆，契約成立**。
> 它是被**載入場景時拋出的既有例外**打紅的——Unity Test Framework 會把未預期的 log exception 視為失敗：
>
> ```
> InvalidCastException: Specified cast is not valid.
>   at UnityEngine.Object.Instantiate[T](T original, Transform parent)
>   at WeaponSocket.Rebuild()  WeaponSocket.cs:71
>   at WeaponSocket.Awake()    WeaponSocket.cs:44
> ```
>
> **根因（已查到具體資產）**：`X Bot.prefab` 的 `weaponPrefab` 欄位是
> ```
> weaponPrefab: {fileID: 100100000, guid: 8e3055a60484b194489bc7cded1491cc, type: 3}
> ```
> 那個 guid ＝ **`Sword.obj`（模型資產，ModelImporter）**，但 **`100100000` 是 prefab 主資產的 fileID 慣例**
> ——模型不用這個 ID（該 `.meta` 的 `internalIDToNameTable` 是空的）。
> ⇒ **引用是壞的**：prefab 式的 fileID 指向模型資產 ⇒ 執行期解析出來的東西不是 `GameObject`
> ⇒ `Instantiate<GameObject>` 當場 cast 失敗 ⇒ **劍從來沒有掛上去過**。
>
> **修法（Inspector 一次拖曳）**：把 `weaponPrefab` 改指
> `Assets/EEJANAI_Team/FreeSwordAnimations/Prefabs/Sword.prefab`
> （guid `06851bed1a7a2f941a32ff65c949c382`，**是真的 prefab**），而不是 `Sword.obj`。
>
> 📌 **這個例外在 Console 被容忍了兩天**（2026-09-07 的紀錄寫「維持獨立缺陷、不夾帶修復」）。
> **P5 上線第一次跑就把它從「可以忽略的紅字」變成「擋住 CI 的紅燈」** —— 這正是加這條測試的價值。
>
> ## ✅ 接線已全部完成（2026-09-08，逐項核對磁碟確認）
>
> | 元件 | X Bot.prefab | Y Bot.prefab | 場景 override |
> |---|---|---|---|
> | `CharacterFacingSource` | ✅ 1 | ✅ 1 | ✅ 0（無重複） |
> | `HeadLookController` | ✅ 1（`headBone` 已指定，含兩個新欄位） | – | ✅ 0 |
> | `PlayerCombatContextSource` | ✅ 1（`targetMask` = **Layer 7**／`m_Bits: 128`，其餘為程式預設） | – | ✅ 0 |
>
> ⚠️ **場景 override 全為 0 很重要**：若 prefab 有元件、場景實例又留著自己 added 的那顆，
> 執行期會變成兩顆，`GetComponent` 的回傳順序就成了權威選擇器。**`P5` 專門守這個。**
>
> 📌 **學到的事**：`CharacterFacingSource`／`HeadLookController`／`PlayerCombatContextSource`
> 這三顆 2026-09-06/07 新增的元件，當初**全部只加在場景實例、沒進 prefab**
> （舊元件如 `PlayerLocomotionPolicy`／`LocomotionModel`／`AimResolver` 都在 prefab 上）。
> ⇒ 把 `X Bot.prefab` 拖進新場景會得到一個殘廢的角色。**W11 第一次跑就抓到了這個漂移。**
>
> ## 這輪做了什麼（Codex 實作、Claude review，純 `.cs`，零資產、零 Git）
>
> | 工作包 | 內容 |
> |---|---|
> | **A：HeadLook 回正** | 回正改走 `lookDegreesPerSecond`（原本只有權重在淡出 ⇒ ≈560°/秒）；權重改成**角度歸零後才淡出**；新增 `releaseYawHysteresis = 10°` 消除邊界抖動 |
> | **B：S3b facing 收斂** | 刪掉 `MotionDriver` 的 `12f` fallback；**死區從執行者搬到決策者**並改成**只適用 Action commitment**；`ApplyFacingRequest` 只剩 slerp |
>
> 新不變量 **A32**（`ExecuteBaseMovement` 不得出現 `LookRotation`／`Slerp`／`transform.rotation`）
> ＋ **W11**（每個角色 Root 恰好一顆 `CharacterFacingSource`）。
> 新測試 `TC6A`／`TC6B`／`TC6E`；`TC6`／`T12` 因簽章變更一併改寫。
> `dotnet build` Runtime ＋ Tests.EditMode **皆 0 error（Claude 獨立複驗過，不是只採信 Codex 自述）**。
>
> ## ⚠️ Play 必須確認的行為變更
>
> **玩家的「移動朝向」原本有 8° 死區，現在沒有了** ⇒ 轉向變成連續。這是刻意的
> （死區的理由是「站定出手時腳掌釘地」，不適用於移動），但**手感會不一樣**。
> 要調就調 `aimFacingTurnSpeed`（X Bot 10／Y Bot 8），⛔ 不要把死區加回 `MotionDriver`。
>
> ## ✅ 「站小斜坡上一直抽搐」——**H3 確診並已修正**（2026-09-08）
>
> **結論**：`FootIKController.SampleGround` 用「誰穿得深就取誰的地面 Y」當 `GroundY`——
> 那在 `heelPen == toePen` 的交叉點上**不連續**，跳幅 ＝ heel-toe 跨距 × 坡度梯度。
> **`max()` 連續；「取 argmax 的另一個屬性」不連續。**
> 已改為由**抬升後的實際腳底平面**導出（＝ ankle-only 路徑本來就在用的公式）。
> **平地逐字等價**；斜坡上骨盆會比舊版少沉一點 ⇒ **這是 Play 要看的差異**。
>
> probe 實測 `0.042`～`0.069` m ⇒ 反推坡度 9.5°～15.5°，**與症狀量化吻合**；
> 同時證明 `IsGrounded`／`L.hit`／`R.hit` 全程未翻動 ⇒ H1／H2 排除。
> 新不變量 `FootIKTests.SoleHeight_SlopePenetrationCrossover_IsContinuous`（＋平地等價那條）。
> **probe 已整段刪除**（`CLAUDE.md` Spike/Probe Exception）。完整推導：`docs/05` §3.5.4.1。
>
> ⚠️ **刻意沒修**：`pelvisTarget` 的 `: 0f` 硬切屬同一類缺陷，但 probe 證明本次未觸發
> ⇒ 依專案紀律不加無證據的防呆。**已登記為潛在風險，不是遺漏。**
>
> <details><summary>（存查）確診前的三假說與 probe 設計</summary>
>
> **已排除頭部**：使用者提供的兩張截圖是**骨盆高度 ＋ 膝蓋彎曲**在兩態間反覆，
> 而 `HeadLookController` 只寫 `headBone.rotation` ⇒ 從構造上改不了骨盆 ⇒ **與 look-at 無關**。
>
> **定位**：`FootIKController.Tick` 這一行是硬切——
> ```csharp
> float pelvisTarget = (ikAllowed && left.HasHit && right.HasHit)
>     ? ComputePelvisOffset(...) : 0f;
> ```
> **任一輸入不可用 ⇒ `pelvisTarget` 硬切回 0。** 平地上 `ComputePelvisOffset` 本來就 ≈0 ⇒ 看不出來；
> 斜坡上它是明顯負值 ⇒ 一旦輸入間歇失效，骨盆就在「補償」與「不補償」之間彈跳 ＝ 那兩張圖。
>
> 📌 **這與這輪 HeadLook 的 clamp 是同一類缺陷**：「輸入不可用時 snap 回預設值」，而不是「連續地保持或衰減」。
>
> ### 三個候選觸發源（⛔ 尚未確診，**不得先修**）
>
> | | 假說 | 分辨方式 |
> |---|---|---|
> | **H1** | 某一腳 ankle raycast 間歇落空（`HasHit` false）——斜坡／邊緣幾何，`RaycastDistance 1.1`／`RaycastUpOffset 0.5` 在邊緣可能不夠 | probe 會看到 `L.hit`／`R.hit` 翻動 |
> | **H2** | `IsGrounded` 在斜坡上逐幀翻動（`CharacterController.isGrounded` 的經典行為；`reboundForce = -2` 貼地力）⇒ `ikAllowed` 翻動 | probe 會看到 `grounded` 翻動 |
> | **H3** | `GroundY` 自己在震盪（`ComputeAnkleTarget` 的泰勒斯修正在**斜面**上會放大 `rayStart` 的變化；平地因 `ibLength≈0` 早退，所以只在斜坡發作） | **所有 bool 都不會翻動** ⇒ 只有震盪偵測抓得到 |
>
> ### ✅ 探針已就位（throwaway，查清後**整段刪除**）
>
> `FootIKController` 已加 `#if UNITY_EDITOR` 的 `logSlopeDiagnostics`（預設 false，**新欄位 ⇒ 不必改資產**）。
>
> **使用者操作**：Play → 角色 Root 的 `FootIKController` Inspector 勾 **Log Slope Diagnostics**
> → 站上會抽搐的斜坡數秒 → 把 Console 所有 `[FootIK-Probe]` 行貼回來。
>
> 它只在**狀態邊沿**輸出（不刷屏），另有 30 幀環形窗在「所有 bool 全穩定」時仍偵測 `pelvisTarget` 震盪
> ——**那條就是專門用來抓 H3 的**，因為 H3 不會讓任何 bool 翻動。
>
> ⛔ **拿到資料前不要修**：三個假說的修法完全不同（H1 改採樣幾何／H2 改 grounded 判定或讓 IK 不看它／
> H3 改骨盆的連續性），猜著修會在這個 repo 留下沒有證據支撐的防呆。
>
> </details>
>
> 📌 **方法論存查**：這次「先寫探針取資料、再修」比直接猜快——三個假說裡最直覺的兩個
> （raycast 落空／`isGrounded` 跳動）**都是錯的**，真凶是唯一不會讓任何 bool 翻動的那一個。
> 若當時照直覺修，會加上兩層無效防呆並且**症狀照舊**。
>
> ## ⏭️ 下一個切片：**F6 —— Core 不得直接持有具體 `AimResolver`**
>
> 使用者已裁決：**排下一個獨立切片，⛔ 不與 S3b 混做**，屆時以**最小介面**收斂依賴。
> 現況：`ActionState`／`FullBodyStateMachine`／`CharacterPipelineRunner` 仍持有具體
> `Presentation.CameraControl.AimResolver`；`CharacterFacingSource` 亦持有具體 `MotionDriver`。
> `A4 LayerRules` 仍放行整個 `Project.Presentation`。背景見 ADR-007 §7 ／ `docs/09` §5.2。

> # ✅ 2026-09-08（晚）— 交辦 ① ② **程式已落地**，等使用者跑測試 ＋ Play
>
> **開場**：讀本段 ＋ `docs/16-review-protocol.md`（⚠️ 持續有效）。下方 2026-09-08（日間）那份
> 交接的 **①② 已經做完**，③④⑤ 原封不動仍然有效。
>
> ## 這輪做了什麼（純 `.cs`，零資產、零 Git）
>
> | # | 內容 | 檔案 |
> |---|---|---|
> | **①** | **Action Targeting Policy 落地**：`ActionTargetingPolicy` enum（`CameraForward` = 0 為預設）＋ `ActionDefinitionSO.Targeting` 欄位；`CaptureReleaseContext` 依 policy 分支；`TrySelectAimPoint` → **`TrySoftTarget`**（水平半角 30° 的角錐閘門） | `ActionDefinitionSO.cs`／`ActionState.cs` |
> | **②-F1** | **BLOCKER**：`TryComputeLookAngles` 超出 `maxYaw` 改**回傳 false**（不再 clamp）⇒ 權重淡回 0、頭回正，`SignedAngle` 的 ±180° wrap 翻轉**從構造上消失** | `HeadLookController.cs` |
> | **②-F2** | **HIGH**：新增 `lookDegreesPerSecond`（預設 360）＋ `Mathf.MoveTowards` ⇒ 角度本身有速率上限，不再瞬間指派 | 同上 |
> | **②-F9** | `CombatContextTests.TC6C` **改寫**：舊版斷言 clamp ＝把缺陷寫成正確行為。新版掃 360° 斷言連續性；另加 `TC6D`（pitch 仍 clamp）。`TC7` 一併改寫為角錐語意，新增 `TC7B` 釘住預設值 | `CombatContextTests.cs` |
>
> **文件 fold-back 已完成**：`docs/11` §8.3（落地紀錄 ＋ 開放問題的答案）／`docs/15` §18.3（修正紀錄）
> ＋ §19.7（「這次沒有改變的事」已失效）／ADR-007 §11（多一列落地紀錄，**D1–D5 一字未動**）。
>
> ## 🚧 Integration Gate —— 需要使用者做的事
>
> 1. **跑 EditMode 測試**。⚠️ 我跑不了：Unity Editor 開著 ⇒ batchmode 被 project lock 擋下。
>    我能驗到的只有 `dotnet build` **Runtime ＋ Tests.EditMode 皆 0 error**——**編譯 ≠ 測試**。
>    重點看：`TC6C`／`TC6D`／`TC7`／`TC7B`，以及**既有的** `ActionStateTests.T30/T31`、`CameraAimTests.T5/T6`。
> 2. **Play 驗收（②）**：貼著敵人繞圈跑到**正後方** ⇒ 頭應該**平順地放棄目標並回正**，⛔ 不得再出現單幀翻到另一邊。
> 3. **Play 驗收（①）**：普通攻擊／火球現在**一律朝鏡頭**（三份既有 Definition 都吃到新欄位的預設值）。
>    ⇒ 手感上想讓**哪一招**吸敵，在 Inspector 把該份 `ActionDefinition` 的 **Targeting** 改成
>    `CameraConeSoftTarget` 即可，**不需要改程式**。角錐要調寬窄改
>    `ActionState.SoftTargetConeHalfAngleDegrees`（⛔ 一顆全域常數，不下放 per-Action）。
> 4. **新欄位不需要重新接線**：`Targeting` 與 `lookDegreesPerSecond` 都是**新增**欄位 ⇒ 既有
>    `.asset`／場景實例吃程式預設值。⛔ 不必手動改任何資產。
>
> ## ⚠️ 三件必須誠實說的事
>
> - **①的行為預設值變了**：既有三份 Definition 從「全部吸敵」→「全部朝鏡頭」。這**正是裁決要的**，
>   但體感差異明顯，**不要誤判成回歸**。
> - **②-F2 的平滑手感只能人工驗**：節流發生在 `Tick`（需要 `Time.deltaTime` ＋ 真實 `Transform`），
>   EditMode 測不到。可自動測的是 F1 的結構性連續性——那也是 BLOCKER 的所在。
> - **③ 的 F4／F6 我沒有動**：兩者都需要架構裁決（見本段最後的「需要裁決」）＋ 敵人端接線，
>   ⛔ 不是「忘了做」。

> # 📦 2026-09-08 — 交接（依五類分開，**不要混談**）
> ### ⚠️ ① 與 ② 已於同日稍晚落地，見上方那段；③④⑤ 仍然有效。
>
> **開場**：讀本段 ＋ `docs/16-review-protocol.md`（review 規則，⚠️ 持續有效）。
> 其餘按需取用；⛔ 不要通讀 `docs/13`／`14`／`15`，它們很長。
>
> ## 🟢 目前基準狀態
>
> - `Locomotion.asset` ＝ **1D 三階**（已還原，正常）
> - 兩份**量測用** prototype 存在：`Locomotion_2D_Proto_RunRing`（前進 1.0×／側後 1.6×）與 `_FullRing`（1.75–2.89×）
>   ⇒ 使用者實測 **RunRing「除了停步外還 OK」** ⇒ **單環在數據與觀感上都成立**
> - 7 支 strafe clip：**已套 SOP ＋ 已烘焙**（batchmode），native speed 見 `docs/13` §10.7
> - facing：**已移除常駐目標朝向**（`docs/15` §19.7）⇒ 平常跟移動方向，只有出手期間跟承諾
>
> ---
>
> ## ① 已決定但未實作 —— **Action Targeting Policy**
>
> **裁決已完成**（`docs/11` §8.3，ADR-007 §11 有修訂紀錄），**程式一行未動**。
>
> | Policy | 語意 |
> |---|---|
> | `CameraForward`（**預設**） | 方向＝camera forward，**永不**自動修正 |
> | `CameraConeSoftTarget` | **僅當**鏡頭前方角錐內有合法敵人才修正 |
> | `SelfCentered` | 不需要 target／facing。⚠️ **只保留概念，不做內容** |
>
> 🔴 **現況與裁決不符**：`ActionState.CaptureReleaseContext` 無條件優先取 `CombatContext.TargetPosition`
> ⇒ **目前所有 Action 都等同 `CameraConeSoftTarget`**，「普通攻擊朝鏡頭」**還沒生效**。
>
> **落點**：`ActionDefinitionSO` 加 enum 欄位（預設 `CameraForward`）＋ `CaptureReleaseContext` 依 policy 分支。
> **開放問題**（實作期決定）：`CameraConeSoftTarget` 用**當下相機錐**還是 **Combat Context 的黏性目標**？
> ⚠️ 這同時決定 Combat Context 的黏性還有沒有存在必要。
>
> ---
>
> ## ② Confirmed bug —— **HeadLook 翻轉 ／ 未平滑**（使用者 Play 實證）
>
> **F1 BLOCKER** `HeadLookController.TryComputeLookAngles`：
> `Vector3.SignedAngle` 值域 `(-180,180]`，目標繞到**正後方**時 `+175° → -175°`，
> clamp 後成為 **`+70° → -70°` 的單幀翻轉**。⇒ 這就是「頭會一瞬間轉到另一邊」。
> **最小修法**：`|未 clamp 的 yaw| > maxYaw` 時**回傳 false**（視為沒有目標）⇒ 權重淡回 0、頭回正，
> **從構造上消除不連續**，而不是修補 wrap。
>
> **F2 HIGH** 同檔 `Tick`：`_yaw`／`_pitch` **直接指派、完全沒平滑**；`blendSpeed` 只管淡入淡出。
> ⇒ 任何目標變化都是瞬間套用。**最小修法**：`Mathf.MoveTowards`（度／秒），比照 `LocomotionSpeedSmoother`。
>
> **F9 MEDIUM** `CombatContextTests.TC6C` **把這個缺陷寫成了正確行為**（斷言 clamp 到 ±maxYaw）
> ⇒ 修 F1 時**必須一起改測試**，改成「掃過 180° 時每步 `|Δyaw|` 有上界」的連續性斷言。
>
> ---
>
> ## ③ 架構債（**已知、已記錄、不要當新發現**）
>
> **F4 HIGH — enemy facing authority 缺席，導致朝向決策重複**
> `MotionDriver.ExecuteBaseMovement` 仍有 `if (!hasFacingRequest) Slerp(..., 12f)` 這條
> 「面向移動方向」的決策，**與 `CharacterFacingSource` 優先序 ③ 是同一個決策**。
> 玩家端已是死路，**敵人端仍在跑**（`Y Bot` 沒有 `CharacterFacingSource`）。
> ⇒ 兩套實作、**不同調參**：`12f` 無死區 vs `aimFacingTurnSpeed 8–10` ＋ `aimFacingAngleDeadzone 8°–15°`
> ⇒ **玩家與敵人依不同且未宣告的規則轉身**，違反 ADR-007 **D3**。
> **處置**：給敵人 facing source（S3b），或把 fallback 併進 facing source 當最低優先序。
> ⚠️ **不要直接刪** —— 敵人還靠它。
>
> **F6 MEDIUM — Core → 具體 `AimResolver`**
> `ActionState`／`FullBodyStateMachine`／`CharacterPipelineRunner` 仍持有具體的
> `Presentation.CameraControl.AimResolver`；`A4 LayerRules` 仍放行整個 `Project.Presentation`。
> 已記錄於 ADR-007 §7（A4 收緊，原訂 S3）與 `docs/09` §5.2。**S3a 出貨時未做，債又老了一輪。**
>
> **其他**：F3（無目標時每幀 OverlapSphere）／F5（`ThirdPersonCamera` 的 `aimOffset`／`aimFieldOfView`
> 已成為打不到的死設定，但仍顯示你調過的數值）／F7（`AnimationBatchOps` spike 需 keep-or-delete 裁決）
>
> ---
>
> ## ④ 未裁決方向（⛔ **不得當成既定計畫引用**）
>
> | 方向 | 狀態 |
> |---|---|
> | **lock-on** | `docs/10` 有規格但未實作。facing 優先序 ② **已留位、無 producer**。⚠️ 若做，才需要處理「相機是否跟著目標」 |
> | **combat state（戰鬥狀態概念）** | 使用者提過「後面可能加入」。⚠️ **Combat Context ≠ combat state**：前者是「跟誰交戰」，後者是「是否進入戰鬥姿態」。⛔ 不得預設它等於常駐持劍或常駐 8-way |
> | **施法時融入 8 向** | **這是 8-way 真正的使用場景**（`docs/15` §19：Movement 與 Facing 只在出手期間分離）。但**尚未裁決怎麼切**——短暫進出要不要淡入淡出、切換條件怎麼表達 |
>
> 📌 `docs/13` §9.5.4 的「雙 mixer ＋ Run cap」**已降級為假說 H1**，⛔ 不是方案。
>
> ---
>
> ## ⑤ 後續內容工作 —— **Movement Animset Pro 跳躍**
>
> 素材已在專案裡（`docs/13` §2.2），**全部未接、未烘**：
>
> | 內容 | 素材 |
> |---|---|
> | 跳躍替換為 MAP | `Jump_place_ALL`／`Jump_walk_{ru,lu}_ALL`／`Jump_run_{ru,lu}_ALL` |
> | **左右腳起跳** | 上列的 `_ru`／`_lu` 後綴＝右腳／左腳起跳 ⇒ **需要腳相**（`FootPhaseCurve`）來選 |
> | **空中急停** | `JumpIdleLand`／`JumpIdleLandHard`／`JumpIdleLand2Walk` |
>
> ⚠️ **烘焙 preset 與 strafe 不同**：跳躍屬 **Jump 家族**（XZ ✅／**Y ❌**／Based Upon **Feet**／Rot ✅），
> 與 strafe 的「Locomotion-位移」**不一樣**（dev-spec §0.4 表）。⛔ **不要混在同一次選取套 preset。**
> 📌 ADR-002 已定調跳躍走**物理 launch**（`ApplyJumpLaunch`）＋烘焙採 `AutoApexHeight`；換素材要重新烘。
> 📌 左右腳起跳需要腳相資料 ⇒ 這是**目前唯一真的需要 `FootPhaseCurve` 的後續工作**。
>
> ---
>
> ## 🔧 batchmode 能力（本輪打通，後續可直接用）
>
> Unity 關閉後，AI 可跑：SOP 套用、批次烘焙、生成 mixer 資產、**EditMode 測試**。
> 入口在 `Assets/Scripts/Editor/Tools/AnimationBatchOps.cs`。
> ```
> "/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -nographics -quit \
>   -projectPath "D:/Unity Project/CharacterController" -executeMethod <靜態方法> -logFile <log>
> ```
> ⚠️ **Editor 開著會被 project lock 擋下**；⚠️ `Assets/MovementAnimsetPro/` **不在版控內**
> （`.gitignore:100`）⇒ 匯入設定改壞了 **git 救不回來**，只能重跑 SOP。

> # 🎯 2026-09-07 — 本輪任務定義：**8 向 strafe 資產驗證 ＋ 可丟棄 prototype**
>
> **一句話**：把 8 向 strafe 從「原始素材」升級成「locomotion library 成員」，
> 並用一個**隨時可切回**的 prototype 回答「這組資產能做到什麼」。
> ⛔ **本輪不做任何 gameplay 政策決策**（速度上限、雙 mixer、外插策略全部等實測）。
>
> ## ① 現況（程式已落地，使用者已實跑確認）
>
> | 切片 | 內容 | 狀態 |
> |---|---|---|
> | ADR-007 **S1** | 移動方向世界座標化；FU-6 結案 | ✅ 已驗 |
> | ADR-007 **S2** | `ActionReleaseContext`：facing 與 release 讀同一份承諾 | ✅ 程式完成 |
> | ADR-007 **S3a** | Combat Context ＋ 單一 facing source；右鍵 Aim 退場 | ✅ **使用者確認：靠近敵人自動面向** |
> | ADR-007 **S3c** | idle 轉身門檻（60°/10°）＋ `HeadLookController` | ✅ 程式完成，待 Play |
> | **S4a** | `MoveX`／`MoveZ` 參數發布 | ✅ 程式完成（無訂閱者 ⇒ 畫面無變化） |
> | 方向 dynamics | `directionTurnDegreesPerSecond = 720` | ✅ **使用者確認：敵人不抽搐** |
>
> 新不變量：**A28**（procedural 不讀 `transform.forward`）／**A29**（sink 不自解方向）／
> **A30**（只有玩家 producer 可讀 `CameraTransform`）／**A31**（`RequestFacing` 唯一送出者）
>
> ## ② 目前唯一壞掉的東西
>
> 🔴 **`Locomotion.asset` 仍是 2D（尚未還原）** ⇒ 1D 三階 mixer 不存在 ⇒
> 走路是 idle↔run 混合、全速跑步等於快轉、7 支 clip 匯入設定又是錯的 ⇒ **這就是「非常詭異」**。
>
> ```
> git checkout -- "Assets/ScriptableObjects/Animation/Locomotion.asset"
> ```
>
> **這是回到可玩基準的唯一動作**（Git 由使用者執行）。
>
> ## ③ 做完本輪之後應該有的體驗
>
> **A. 還原 1D 後立刻該有（基準線）**
> - 走／跑／衝刺三階正常、收步正常（回到 ADR-005 結案時的水準）
> - 戰鬥中**不按任何鍵**自動面向敵人；A／D 真的往世界左右位移
> - ⚠️ **但會播前進動畫＝滑步** —— **這是預期的中間狀態**，正是本輪要解的
> - idle 小幅度只轉頭；敵人移動不抽搐
>
> **B. prototype 開著時該有（本輪產出）**
> - 八個方向各自播對應 clip，**方向不顛倒**（左右不反、45 與 135 不互換）
> - 步頻一致（離線已證：8 支**全部 24 影格／0.7667 秒**）
> - 能用實測回答五件事：小輸入時步頻／run 附近自然度／sprint 滑步嚴重度／
>   各方向速度與姿勢一致性／**單一 ring 是否可接受**
>
> **C. 明確不會有（避免期待落差）**
> - ❌ 持劍、上半身分層（ADR-006 仍 `Proposed`，Trial 名額被 ADR-007 佔用）
> - ❌ 大角度轉身時腳跟著動（S3d，另需烘 3 支）
> - ❌ 八方向收步（stop 仍只有前向，見 ④）
> - ❌ 速度數值重調（`docs/13` §8：等形狀定案再統一調）
>
> ## ④ 🆕 本次核對新發現：**收步位移仍沿 `transform.forward`**
>
> `LocomotionModel.UpdateMotion` 在收步時走 `ExecuteBakedCurveMovement`，
> 而該路徑的水平速度是 `transform.forward * worldSpeed`（**ADR-007 D2 明文保留的例外**：
> 烘焙曲線表達 clip 自身座標系）。
>
> **S1 之前**移動方向恆等於朝向 ⇒ 一致；**S1＋戰鬥朝向之後**兩者可以不同 ⇒
> **面向敵人橫移時放開輸入，收步會朝「面向」而不是「移動方向」位移** ⇒ 看起來像往前一頓。
>
> 📌 **這不是回歸，是 S1 揭露的既有假設**（收步素材本來就只有前向：
> `RunFwdStop_LU/RU`／`WalkFwdStop_LU/RU`，`LocomotionModel` 也只有 walk／run 兩組 variant）。
> ⇒ **列入 Play 觀察項**，不在本輪修。
>
> ## ⑤ 需要先烘焙的資源清單
>
> ### 必烘：**7 支**（本輪前置）
>
> `RunBwdLoop`／`RunLtLoop`／`RunRtLoop`／`RunStrafeLeft45Loop`／`RunStrafeRight45Loop`／
> `RunStrafeLeft135Loop`／`RunStrafeRight135Loop`
>
> | 項目 | 內容 |
> |---|---|
> | **為什麼要烘** | 取 **native speed**（`SpeedCurve` → `GetRepresentativeSpeed()`）——這是回答「八支能否共用同一 ring」的**唯一必要資料** |
> | **哪些資料用不到** | `RotationCurve`／Jump 家族（`AutoApexHeight` 等）／`TargetLocalDirection`。`FootPhaseCurve` 之後做八方向收步才用得到 |
> | 🔴 **前置** | **必須先套匯入 SOP**（`docs/13` §9.4 缺陷 A）。dev-spec §0.4 規則 1 明文：套用後**必須重烘焙** ⇒ **先烘再 SOP 等於烘一組即將失效的數字** |
> | **preset** | **Locomotion-位移**（XZ ❌／Y ✅／Rot ✅／Based Upon 全 Original／Loop ✅） |
> | **選取方式** | Project 視窗選**那 7 支子 clip**。⛔ **不要選整個 FBX** —— `Throw_*` 七個條目住在同一支 FBX，ADR-004 已結案的東西會被灌壞 |
> | **烘焙成本** | **一次操作**：烘焙工具已支援批次（「加入 Project 選取的 Clip」→「批次烘焙 7 支」） |
>
> ### ⛔ 不要一起烘的
>
> | 素材 | 為什麼 |
> |---|---|
> | `StrafeRight45Loop`／`StrafeLeft135Loop` | **31 影格**，與那 8 支的 24 影格 cadence **不同** ⇒ 同 ring 會步頻打架。名字很像，**容易誤選** |
> | `Crouch_Walk*` 8 向 | 本輪不用 |
> | `TurnLt180`／`TurnRt90_Loop`／`TurnLt90_Loop`（S3d 用） | 🔴 **preset 不同**：轉身屬「**烘焙曲線驅動**」型（XZ ❌／Y ✅／**Rot ❌**，因為要採 yaw），與 strafe 的「Locomotion-位移」（Rot ✅）**相反**。<br>混在同一次選取會**誤套 preset** ⇒ **分兩次進 Editor，不要圖省事** |
>
> ### ✅ 已有、不需重烘
>
> `Bake_Idle`、`Bake_RunFwdLoop`（`RunFwdLoop` 的匯入設定**已符合 §0.4**，不必重套也不必重烘）
>
> ## ⑥ 本輪的執行順序
>
> 1. `git checkout` 還原 `Locomotion.asset` ⇒ **先回到可玩基準**
> 2. 對 7 支子 clip 套 **Locomotion-位移** preset ⇒ `LocomotionMixerWiringTests.L1` 轉綠
> 3. **批次烘焙**那 7 支
> 4. 跑 `L2_Report_StrideCalibrationTable` ⇒ 得到 native speed 與速度落差
> 5. 建 `Locomotion_2D_Prototype.asset`（**新資產**），`PlayerStateMachineConfig` 的 Locomotion 鍵暫時指過去
> 6. Play 走 `docs/13` §10.4 的六項觀察 ＋ 本檔 ④ 的收步觀察
> 7. **實測完**才回答 `docs/13` §10.5 的四個問題
>
> ⚠️ 第 5 步是**可逆的一個欄位**；⛔ **不要再就地改 `Locomotion.asset`**。


> # 🟢 2026-09-07 — S4a（8 向參數）＋ S3c（戰鬥待機只轉頭）程式已落地
>
> 規格：`docs/13` §9（8 向）／`docs/15` §18（待機朝向）。**兩者互相獨立、分開驗收。**
> `dotnet build` Runtime ＋ Tests.EditMode **0 error**；改動 6 個 `.cs`；零資產、零 Git。
> `RequestFacing` 呼叫者仍**恰好一個**（A31 綠）。
>
> ## ✅ 三件場景／資產接線完成（2026-09-07，Codex 操作 Unity Editor）
>
> **① 頭部 look-at**：`SampleScene` 的 X Bot Root 已掛 `HeadLookController`，`Head Bone` 已指向
> X Bot 骨架唯一的 **`mixamorig:Head`**；數值 `Max Yaw 70`／`Max Pitch 25`／`Blend Speed 8`。
>
> **② 8 向動畫**：`Locomotion.asset` 已從 `LinearMixerTransition` 換成
> **`MixerTransition2D`（Cartesian）**；新增 `MoveX.asset`／`MoveZ.asset` 並綁到兩軸。
> 取樣共 9 點（Idle 中心 ＋ 8 方向），座標依 `docs/13` §9.3；**速度階層未納入 2D**。
> 📌 資產實況修正：`RunStrafeUpdate.fbx` 實際只有後／左／右／四斜角 **7 支**；
> `RunFwdLoop` 只存在於 `MovementAnimsetPro.fbx`，因此前進點直接引用後者的 FBX sub-clip。
> 全部都是 FBX sub-clip 直引，沒有複製 `AnimationClip`。
>
> **③ 敵人放慢**：Y Bot 的 `AIMovementSource.desiredSpeedNormalized` 已由 **1 → 0.55**。
> 原本敵我都吃同一份 bake 速度 5.66 m/s，速度對稱使距離理論上拉不開，才讓 `leaveRadius 9`
> 幾乎無法達成；這不是 Combat Context 離場規則錯誤。
>
> **自動覆核**：序列化值與 9 個取樣點已由 Unity 回讀；編譯 0 error／0 warning；
> Play Mode 啟動未出現本批次相關錯誤。Console 仍有既有 `WeaponSocket.Rebuild()` 的
> `InvalidCastException`，堆疊不經本批次，維持獨立缺陷、不夾帶修復。
>
> ## ⛔ 還沒做：大角度轉身時「腳跟著動」（turn-in-place ＝ S3d）
>
> S3c 只做到「小角度不轉、只轉頭」。**大角度仍是 slerp ⇒ 仍會滑步**，只是次數大幅減少。
> 不滑步的正解是播原地轉身 clip **且 root 旋轉速率由 Bake 曲線驅動**（與 Roll 同一條路徑）。
> **前置是資產**：`TurnRt90_Loop`／`TurnLt90_Loop`／`TurnLt180` **都還沒烘**（只有 `Bake_TurnRt180`，引用次數 0）。
> ⇒ **使用者先用既有 Editor 烘焙工具把那三支烘出來**，S3d 才有東西可接。詳見 `docs/15` §18.4。

> # 🟡 2026-09-06 — **ADR-007（Direction Authority）＝ `Trial`，S1 → S2 實作中**
>
> **開場指令**：先讀 **`docs/ADR/007-direction-authority.md`**（決策，🟡 Trial）＋ **`docs/14-direction-authority.md`**（契約面／切片／測試／Codex 邊界）。
> 這兩份已經把該讀的都摘好了，**不需要**再通讀 `docs/09`／`docs/11`／`docs/13`。
>
> ## ✅ 使用者裁決（2026-09-06）
>
> | 項目 | 裁決 |
> |---|---|
> | **ADR-007** | `Proposed → **Trial**`（佔用 Trial 名額） |
> | **ADR-006**（上身層） | **維持 `Proposed`**，須等 007 `Accepted` 才能翻 Trial |
> | 實作順序 | **S1 → S2 連續執行**，中間不插入其他工作包（AoE 落點退化窗口，ADR-007 R3） |
> | 實作者 | **Codex** |
>
> ⚠️ **Trial ＝ 已裁決為實作基線，尚未由 slice 驗證。** 引用時必須註明狀態。
>
> ## ✅ S1＋S2 程式已落地（2026-09-06，Codex 實作 ／ 本會話覆核）
>
> | | 內容 | 狀態 |
> |---|---|---|
> | **S1** | 移動方向改世界座標：投影從 `MotionDriver` 搬到 `PlayerLocomotionPolicy`；`ExecuteBaseMovement` 用 `data.MoveDirection`（**不再用 `transform.forward`**）；`AIMovementSource` 刪掉相機投影與早退 ⇒ **FU-6 結案** | ✅ 程式完成 |
> | **S2** | 🆕 `ActionReleaseContext`（`readonly struct`）；`IActionLifecycleSink.Release(in ...)`；承諾改由 `ActionState` 在 **`OnEnter` ＋ 每個連段邊界**取得一次，**facing 與 release 讀同一份**；`AimResolver` 的 latch 狀態全刪、只剩無狀態查詢 ＋ 唯一 `RequestFacing` 送出點 | ✅ 程式完成 |
> | 新不變量 | **A28**（procedural 位移不得讀 `transform.forward`）／**A29**（sink 不得自行解算方向）／**A30**（只有玩家 producer 可引用 `CameraTransform`） | ✅ 已加入 |
>
> **覆核結果**：改動範圍＝**19 個 `.cs`**（runtime／editor／測試），**零資產、零 `.meta`、零 Git**。
> `dotnet build`：`Project.Runtime` **0 error**、`Project.Tests.EditMode` **0 error**。
> A5 `WriterRules` 零改動（沒有新增黑板欄位）。
>
> ⚠️ **`dotnet build` 0 error ≠ 測試通過。** Acceptance A–G **全部尚未打勾**。
>
> ## 🔧 2026-09-06 第一輪 Play 回饋已處理
>
> **回報：敵人移動會抽搐** ⇒ 這是 **R1 的另一面**（`docs/14` §4 已預先寫好處置條件）。
> 成因：S1 之前 `速度＝transform.forward` ＋ `Slerp(12·dt)`，**body slerp 實質上是 producer 方向的低通濾波器**；
> S1 拿掉別名也拿掉了濾波器 ⇒ producer 的方向不連續（Approach↔Hold 差 90°、側移翻向 180°）直接變成位移抖動。
> **已處置**：方向 dynamics 加回 model（`LocomotionSpeedSmoother` XZ 平面 `MoveTowardsAngle` 限速轉向；
> 唯一旋鈕 `LocomotionModel.directionTurnDegreesPerSecond` 預設 **720°/s**，嫌遲鈍就往上調）。⛔ 沒有退回讀 `transform.forward`（A28 仍綠）。
>
> 📌 **另登記一個獨立缺陷（刻意不同批修）**：`AIMovementSource` 的 `distanceHysteresis 0.15 m`
> 在 `moveSpeed 5.66 m/s` 下只有 **1.6 帧**就跑完 ⇒ 交戰模式可能每兩帧翻一次。
> 濾波器只能把它壓成小幅蛇行，**修不掉**。正解是遲滯改成時間／速度感知。詳見 `docs/14` §7-5。
> ⇒ **先看 720°/s 之後還剩多少抖動再決定要不要動它。**
>
> 📌 **開 Unity 前先知道這件事**：`Assets/Scripts/Core/Actions/ActionReleaseContext.cs` 是**新檔**，
> Unity 首次開啟會 import 並自動生成 `.meta`（AI 不建 `.meta`）。生成的 `.csproj` 也是那時才會納入該檔——
> 在那之前用 `dotnet build` 會噴 6 個假的 `CS0246`，**那不是程式問題**。
>
> 📌 `ThrowProjectileEmitter` 移除了 `[SerializeField] aimResolver` ⇒ prefab 裡會殘留一個孤兒序列化欄位，
> **Unity 下次存檔自己會丟掉，不用手動清**。
>
> ## 這份 ADR 在處理什麼
>
> 使用者裁決：**不要**只做 movement/facing 分離，也**不要**只為 A6 補「每段重新 latch」。
> 盤點後確認 `docs/13` §4.1／§4.2（strafe 不可能）與 §4.3（連段人與火球分家）**是同一個根因**：
> **移動方向／朝向／瞄準方向三個概念只有兩個載體，因此互相冒充。**
>
> 另有一項回溯發現：**`docs/09` §5.2 的 trip-wire ①（Core 讀 AimPoint）已於 2026-09-05 被安靜跨過**
> ——`ActionState`／`FullBodyStateMachine`／`CharacterPipelineRunner` 都直接持有具體的 `AimResolver`，
> 而 `LayerRules` 放行整個 `Project.Presentation` 所以機器沒擋。已記入 ADR-007 §1.1-E6 與 `docs/09` §5.2。
>
> ## 🔵 2026-09-06 第二輪回饋 ⇒ **需求變更：不要 Aim Mode，右鍵留給 Guard**
>
> 使用者實跑確認：**右鍵時始終面朝前方沒問題、敵人不抽搐** ⇒ S1／S2 ＋ R1 處置的物理面成立。
> 但**操作設計前提改了**：`<Mouse>/rightButton` **保留給 Block／Guard**，
> **不要任何獨立 Aim Mode**——朝向應由**戰鬥語境**自動取得，玩家只負責移動／攻擊／施法／格擋。
>
> ⇒ **診斷與設計提案：`docs/15-combat-context.md`（🔵 提案，待裁決）**。
> ADR-007 已依 Trial 規則修訂：**D1–D5 一字未改**；改的是 **D3 補充條款**（facing 來源必須是語境、不得是輸入模式）、
> **Acceptance A 改寫**、**S3 重新定義**為「Combat Context ＋ 單一 facing source」。
>
> 🔴 **連帶排程後果**：Acceptance A 原本靠右鍵就能驗，現在**必須等 S3** ⇒ **ADR-007 不可能在 S3 之前 `Accepted`**
> ⇒ ADR-006（上身層）的 Trial 名額也因此順延。
>
> ## ✅ 已裁決（2026-09-06）
>
> 1. **採納** `docs/15` 的 no-Aim-button combat-facing 模型與切片 **S3a**
> 2. **⛔ 不開 ADR-008** —— Combat Context 是 **ADR-007 D3 的實例化**（facing authority 的 target source），
>    不是另一套架構哲學。判準①的 ownership 寫進 **ADR-007 D3 補充條款**，`docs/15` 自此是 ADR-007 的 Living Spec
>
> ⇒ **S3a 契約面在 `docs/15` §13、測試在 §14、實作紀錄與 fold-back 在 §17。**
>
> ## ✅ S3a 程式已落地（2026-09-06，Codex ／ 本會話覆核）
>
> 新增 4 檔（`CombatContextData`／`PlayerCombatContextSource`／`CharacterFacingSource`／`CombatContextTests`）、
> 修改 9 檔；`dotnet build` Runtime ＋ Tests.EditMode **0 error**；**零資產、零 Git**。
> `RequestFacing` 全專案呼叫者**恰好一個**（A31 機器守住 ADR-007 D3）。右鍵 Aim 已完全退場。
>
> ### ✅ S3a 場景接線完成（2026-09-06，Codex 操作 Unity Editor）
>
> `Assets/Scenes/SampleScene.unity` 的 **X Bot 角色 Root** 已加掛
> `PlayerCombatContextSource` ＋ `CharacterFacingSource`（X Bot prefab 的 scene instance overrides），並填：
> `Enter Radius 6`／`Leave Radius 9`（**大於 Enter**）／`Disengage Seconds 5`／
> `Target Mask = Enemy`（mask **128**，取自 Y Bot `CharacterController` 的 layer）／`Selection Cone Angle 25`。
> ⛔ **不要**把舊的 `softTargetRadius 1.2` 填進 `Enter Radius`——那是 SphereCast 厚度，語意不同。
> 敵人側已核對：Y Bot 的 `ActionRequestTarget` **啟用中**，且啟用中的 `CharacterController`
> 位於 `Enemy` layer、包含在 `Target Mask` 內。場景已儲存；**沒有修改 prefab asset**。
> （玩家若也要靠「被打」進入戰鬥語境，X Bot 自己也需要 `ActionRequestTarget`。）
>
> ### 🟡 Play 時特別看這個（`docs/15` §17.2）
>
> 自由移動的轉身現在改吃 `MotionDriver` 的 **`aimFacingAngleDeadzone`（X Bot 現值 8°）＋ `aimFacingTurnSpeed`（10）**，
> 而不是原本的 12、無死區——因為 facing 收斂成單一 authority 後，移動轉向也走同一條路。
> **小幅轉向可能不轉或變鈍 ⇒ 把 `aimFacingAngleDeadzone` 往 0–2 調。** 這不是 bug，是收斂的必然後果。
>
> 🟡 **S3a 的一個已知取捨（`docs/15` §13.4）**：`ThirdPersonCamera` 目前用 `aimResolver.IsAiming`
> 切換過肩近景（`aimOffset`／`aimFieldOfView`，`docs/09` §6.2 是你實機調過的參數）。
> 右鍵退場後相機**沒有合法管道**讀 `InCombat`（它不是 `IPresentationController`、不持有黑板，
> 而 A4 禁止 Presentation 依賴 `Core.Combat`）⇒ **S3a 先把取景 blend 固定為探索取景**，
> 「戰鬥取景」登記為獨立議題（§16-1）：那同時是 feel 決策與架構決策，不該夾帶在 S3a 裡偷偷決定。
>
> ---
>
> ## ⏳ 等使用者做的事
>
> **S1＋S2 落地後的 Play 驗收**（`docs/14` §4／§6.3；EditMode／PlayMode 由機器守，不要重測）：
>
> 1. **strafe**：按住瞄準 ＋ 按 A ⇒ **朝目標、往世界左方位移**（不再朝目標走過去）
> 2. **FU-6 肉眼證據**：移動相機時**敵人的移動方向不再改變**
> 3. **A6**：Fireball 連段中敵人走動 ⇒ 每一段的**身體朝向與火球方向一致**
> 4. **觀察項（不是通過條件，看一眼回報就好）**：轉彎弧線是否變太直角（R1）／減速滑行不再隨相機彎（R2）／
>    橫移仍播前進動畫的滑步（**刻意的中間狀態**，由 `docs/13` §7-2 的 2D mixer 解決）
> 5. **零 GC 複驗**（Development Build ＋ Profiler，`docs/02` §7.4 SOP）——型別 `Vector2 → Vector3` 不該產生配置，但要看過
>
> ## 📌 Codex 的三條紅線（已寫進 `docs/14` §8）
>
> 1. **S1 與 S2 要連續做**：別名解除後 `GroundEffectSink` 的 `casterRoot.forward` 落點會短暫失準（ADR-007 R3）。
> 2. **不得新增第二個 `RequestFacing` 送出者**（D3）——撞到就停下來回報。
> 3. **不得新增黑板欄位**（那是 S3，要先改 ADR-007 §7 的處置欄）；不得碰 `.asset`／`.prefab`／`.meta`／場景／Git。
>
> ---

> # ✅ 2026-09-06 — **ADR-005 已結案（`Trial → Accepted`）**
>
> 使用者實跑回報：**A 功能上沒問題／B 沒問題／C 沒問題／E 沒問題**。
> 加上先前成立的 D／F／G ⇒ **A–G 七條全數通過**。
>
> **已同步更新的文件**：`docs/ADR/005`（狀態＋§4 打勾＋§5 修訂紀錄兩列）、
> `docs/11`（檔頭狀態＋**§10.2 ⛔ 名單標示解除**）、`docs/00-map.md`（三處）、`docs/13` §6。
>
> ## 🔓 這代表什麼
>
> `docs/11` §10.2 的 ⛔ 名單是 **Acceptance G 的觀察期前提**，不是永久禁令——
> G 要證明「Slow 跨系統傳播時五個下游檔案零修改」，觀察期間當然不能動被觀察的對象。
> **G 通過 ⇒ 觀察期結束 ⇒ 名單失效。**
>
> ⇒ `MotionDriver` 位移路徑／`AnimationFacadeBase` 契約／`LocomotionModel` **全部解鎖**，
> `docs/13` §7 的四步實作順序可以開工。
>
> ⚠️ **解鎖 ≠ 可以隨便改**：這些檔案仍受各自的 ADR 與不變量約束（ADR-003／ADR-001／A4／A20），
> 動之前照舊走 routing rule 判斷要不要開 ADR。
>
> ## 📌 結案時記下的一個表現層問題（**不阻擋結案**）
>
> A6（Fireball 連段）功能通過，但**敵人走動時會出現「人朝 A、火球飛 B」**：
>
> | 誰 | 何時決定方向 |
> |---|---|
> | 身體朝向 | `ActionState.OnEnter` **鎖一次** |
> | 發射方向 | `ThrowProjectileEmitter.Release()` **每段重算** |
>
> **兩邊各自都是對的**——朝向鎖一次是為了「揮擊途中甩相機不要跟著轉」，
> 發射方向重算是為了「火球要打得中」。**衝突只在連段這種跨越多次 `Release` 的 Action 上浮現。**
>
> 🔴 **不要只修連段**（例如「每段重新 latch」）：那只是讓兩個擁有者的時點碰巧一致，
> 下一個跨 Release 的機制出現時同樣的裂縫會再開一次。
> 根因與 `docs/13` §4.1／§4.2 是同一類——**朝向／移動方向／瞄準方向三者沒有共同的擁有者**。
> ⇒ 已登記 **`docs/13` §4.3**，屬結構性決策，依 CLAUDE.md 判準應開 ADR 一併處理。
>
> ## ▶️ 下一步（`docs/13` §7 的順序，未開工）
>
> 1. **movement／facing 分離** —— `MotionDriver` 在 `hasFacingRequest` 為真時，速度改用 `targetDirection`
>    而非 `transform.forward`。**不需要任何新動畫**，先讓「面向敵人往左走」在物理上成立
> 2. **2D locomotion mixer** —— 接上 `MovementAnimsetPro_RunStrafeUpdate` 裡**早就存在但引用次數 0** 的 8 向 strafe
> 3. **戰鬥狀態 ＋ 上身持劍層**（ADR-006，需先裁決）
> 4. **C2／C3 轉身**（`Bake_TurnRt180` 等已備）
>
> ⚠️ **速度數值等 1、2 做完再統一調**（理由見 `docs/13` §8：現在調等於用速度補償動畫與朝向的缺陷）。
> 唯一例外是相機——與 locomotion 正交，隨時可調。
>
> 🗡️ **獨立於上述順序、隨時可做**：修劍的材質（`docs/13` §1，真因是 `Sword.obj` 的材質重映指向不存在的 GUID）。
>
> ---
>
> <details>
> <summary>📋 已完成的驗收清單（2026-09-06，保留供追溯）</summary>
>
> ---
>
> ## ⓪ 先讀：哪些**已經**被自動化證明，不要重測
>
> | 條 | 已被機器守住的部分 | 仍需人工的部分 |
> |---|---|---|
> | **A** | **機制**：`T18`（`Assert.AreSame` 同一顆 `ActionState`）／`T19`（per-slot 冷卻不連坐）／`T20`（Action→Action 中斷）／`T21`（舊資產相容）<br>**接線**：`W4`（每個 slot 有 sink）／`W7`（動畫鍵解析得到）／`W9`（身分解析回自己） | **只剩「實際播出來」** |
> | **B** | **「零 runtime 程式」可由稽核證明**，見 ②。無需 Play | 選配 2 分鐘 live demo |
> | **C** | `T21` 鎖住舊資產的**解析**路徑；`W7` 鎖住動畫鍵接線 | **播放與位移**（測試碰不到） |
> | **E** | `A3` 只擋 `System.Linq` 這一類**靜態可見**的配置。§7.1-A3 已明文記載它抓不到裝箱類配置 | **全部**：Development Build ＋ Profiler |
>
> ---
>
> ## 🔴 排程約束：**A／B／C 與 E 不可能同一場跑完**
>
> `docs/02` §7.4.2 第 2 條明文：**量 GC 時不得在 Hierarchy 選取角色**——
> `CharacterPipelineRunnerEditor` 每幀重繪會配置字串（`ToString("F3")` 等）。
>
> 而 A／C 的觀察**恰恰需要**打開那個面板看 `Current State`。
> 加上 E 依 §7.4.3 必須是 **Development Build ＋ Player 連線**才算「達標」等級。
>
> ⇒ **最短是兩場，不是一場**：
> **第 1 場** Editor Play（A ＋ C ＋ 選配 B），Inspector 全程開著
> **第 2 場** Development Build（E），不開面板、不選角色
>
> 📌 **劍看不見不影響驗收**：A 驗的是「三個 Action 各自觸發」，看動畫本身即可判定。
> 材質問題（`docs/13` §1）與 ADR-005 無關，**不要為了它卡住驗收**。
>
> ---
>
> ## ① 第 1 場：Editor Play —— A ＋ C
>
> **場景**：`Assets/Scenes/SampleScene.unity`
> **準備**：Hierarchy 選中 **`X Bot`** → Inspector 找 `Character Pipeline Runner`
> → 展開「黑板數據流即時監視」。全程看兩個欄位：
> **`[Current State]`**（粗體大寫）與 **`Intent: Action Slot`**。
> **錄影**：整場開 OBS，一鏡到底。A 與 C 的證據都在同一支影片裡。
>
> ### C —— 既有 locomotion 無回歸（**先做 C**，理由見 §④）
>
> | 操作 | 預期 | Fail 的樣子 | Fail 先懷疑 |
> |---|---|---|---|
> | 站著不動 3 秒 | `Current State` ＝ **IDLE**，播 Idle 動畫 | 抖動／狀態在 IDLE↔MOVE 之間跳 | `LocomotionModel` 的 B9 平滑門檻 |
> | WASD 走一圈（含放開） | **MOVE**；放開後有收步動畫（`RunStop_*`／`WalkStop_*`）再回 IDLE | 直接瞬切 IDLE、或滑行 | `LocomotionStopSelector`（C1 既有功能） |
> | 空格跳，落地 | **JUMP** → 落地回 IDLE／MOVE | 卡在 JUMP、或落地穿地 | `JumpState`／`MotionDriver` 重力 |
> | 移動中翻滾 | **ROLL**，位移由烘焙曲線驅動 | 原地翻滾（無位移） | `Bake_Stand To Roll` 的 `BakedDuration` |
> | 各重複 **3 次** | 每次一致 | 只有某幾次壞 | 時序／狀態殘留 |
>
> ⚠️ **Throw 不在測試範圍**：`docs/11` §5.1 已把 Slot1 移交給 Melee，
> `ThrowDefinition` 不再被解析。**C 的原文「Throw 無回歸」應讀作「Throw 已依計畫退場」。**
>
> ### A —— 三個 Action 各自獨立觸發
>
> | # | 操作 | 預期 | Fail 的樣子 |
> |---|---|---|---|
> | A1 | 按 **滑鼠左鍵** | `Current State` → **ACTION**，播揮擊，人向前衝一小段（Bake 位移） | 沒進 ACTION ／ 播了但不位移 |
> | A2 | 按 **Q** | → ACTION，播左手指向施法，**胸口飛出火球** | 動畫有、火球沒有 |
> | A3 | 按 **E** | → ACTION，播單手上舉召喚，**身前約 0.4m 地面冒出冰刺** | 冰刺在別處／飄空中 |
> | A4 | **按 E，立刻按 Q** | **Q 要能出手** —— 這是 per-slot 冷卻獨立的關鍵證據 | Q 被吃掉 ⇒ 冷卻變全域 ⇒ **A 直接 Fail** |
> | A5 | 連按 E 三次 | 第 2、3 次**沒反應**（Ice 冷卻 1.5 秒） | 連發 ⇒ 冷卻沒生效 |
> | A6 | 按 Q，動作播到約 1/4 後再按 Q，再一次 | 接第 2 段（右手）→ 第 3 段（雙手），**三段各出一顆火球** | 只有第一顆 ⇒ 連段 Release 被吞 |
> | A7 | 冰刺打到敵人 | 敵人**明顯變慢**（速度剩 30%） | 沒變慢 |
> | | 每項重複 **2 次** | | |
>
> **A 的判準**：**A1／A2／A3 各自播出不同動畫且各自產生世界效果 ＋ A4 成立**。
> A6／A7 是加分項（連段與 Slow 屬本 ADR 之後加的功能），**不成立不阻擋 A**，但要記下來。
>
> **Fail 時先懷疑哪一層**：
> - 完全沒進 ACTION ⇒ 輸入層（看 `Intent: Action Slot` 有沒有跳到對應 slot）
> - 進了 ACTION 但沒動畫 ⇒ `transitionMappings` 的鍵（但 `W7` 已綠，機率低）
> - 有動畫沒世界效果 ⇒ sink 綁定（但 `W4` 已綠，機率低）
> - **以上三條測試都綠，所以最可能的失敗點是「資產數值」而不是「接線」**
>
> ---
>
> ## ② B —— 加下一個 Action ＝ 零 runtime 程式
>
> ### 主要證據：**稽核，不需要 Play**
>
> `EnemyPunchDefinition`（2026-09-05 加入）是本 ADR 之後新增的第 4 份 Definition，
> 加入時**只動了三樣**：一份 `.asset`、一列 `transitionMappings`、一列 `actionDefinitions`。
> `ActionState.cs`／`ActionDefinitionSO.cs`／`StateMachineConfigSO.cs` **當時一行未改**。
>
> > ⚠️ **誠實的但書**：同批確實新增了 `AIInputSource.cs`。但那是因為
> > **敵人原本連輸入來源都沒有**（`inputSourceComponent` 是 0），與「Action 系統需不需要改」正交——
> > 玩家加第 4 個 Action 不需要它。這一點在裁決時要自己判斷算不算數。
>
> ### 選配：2 分鐘 live demo（讓它看得見）
>
> 1. 複製 `MeleeSlash1Definition.asset`（Ctrl+D）→ 改名 `Demo_B_Definition`
> 2. 改它的 `AnimationKey` 為 **`Spell_Fireball_3`**（已在 `transitionMappings` 裡，不必新增映射）
> 3. `PlayerStateMachineConfig` 的 `actionDefinitions`：把 Melee 那筆**換成**這一筆
> 4. Play，按滑鼠左鍵 ⇒ **播的是雙手施法動畫**
> 5. **完全沒有重新編譯，沒有動任何 `.cs`**
> 6. 驗完把 config 換回來、刪掉 demo 資產
>
> ⚠️ **已知邊界（不是 Fail）**：玩家的第 4 個**新 slot** 需要 `ActionSlot` enum 加一員 ＝ 改程式。
> ADR-005 §3.2 已明文接受這個成本（「比照 `StateType` 先例，且該成本本來就該被看見」）。
> **B 驗的是「加 Definition」，不是「加 slot」。**
>
> ---
>
> ## ③ 第 2 場：Development Build —— E（零 GC）
>
> **完全照 `docs/02` §7.4 的既有 SOP，不要另創方法。** 摘要：
>
> | 步驟 | 內容 |
> |---|---|
> | 建置 | `File → Build Settings` ⇒ 勾 **Development Build** ＋ **Autoconnect Profiler** |
> | 連線 | Profiler 目標選單選 **機器名**（不是 `Play Mode`） |
> | 看哪裡 | **CPU Usage → 下方切 `Hierarchy` → `GC Alloc` 欄 → `PlayerLoop` 那一列** |
> | ⛔ 不要看 | CPU 圖表的 `GarbageCollector` 毫秒數（那是回收時間，不是配置量。**0ms 完全可能同時每幀都在配置**） |
> | 條件 | 穩態**直線走**、不跳／不滾／**不切狀態**、Deep Profile **關**、按 `Clear` |
> | 判定 | `PlayerLoop` 的 `GC Alloc` 連續數十幀 ＝ **0 B** |
>
> ### ⚠️ 這一輪必須額外量的東西（熱路徑有改動）
>
> ADR-005 §4-E 原文就標了「熱路徑有改動，**必須複驗**」。本輪之後又多了幾處，
> **請在穩態之外，額外量這三個情境**：
>
> | 情境 | 為什麼要量 |
> |---|---|
> | **連續施法 10 秒**（Q／E 交替） | `ActionState.OnTick`／`TryEmitRelease`／連段切段都在熱路徑 |
> | **敵人在旁邊繞圈 10 秒** | `AIMovementSource` 的 Hold 側移是本輪新增的每幀計算 |
> | **冰刺爆發 3 次** | `Physics.OverlapSphereNonAlloc` ＋ 探地 `RaycastNonAlloc` 用的是預配置緩衝，**要證明它真的沒配置** |
>
> ### 存證（§7.4.3 硬性要求）
>
> 截圖存 **`docs/images/profiler/`** 並**進版控**。
> 截圖必須自證是 Player 而非 Editor —— 把這三處一起框進去：
> ① 目標選單顯示**機器名** ② Hierarchy **沒有 `EditorLoop`** ③ `Deep Profile` 自動停用。
>
> 📌 **既有基準**（§7.4.4，2026-07-26）：穩態 `PlayerLoop` ＝ **0 B**；
> 狀態切換幀約 **2.6 KB**，已定位為 `Debug.Log` 的 `StackTraceUtility`，**僅存在於 Editor**、Release 由編譯器移除。
> ⇒ **Development Build 裡不該再看到那 2.6 KB**。若看到了，那是新的回歸。
>
> ---
>
> ## ④ 最短執行順序
>
> ```
> 第 1 場（Editor Play，Inspector 開著，全程錄影）
>   1. C —— Idle / Move / Jump / Roll 各 3 次      ← 先做
>   2. A —— A1→A2→A3→A4→A5→A6→A7 各 2 次
>   3. B（選配）—— 換 Definition，按左鍵，換回來
>
> 第 2 場（Development Build，不開面板、不選角色）
>   4. E —— 穩態 + 施法 10 秒 + 敵人繞圈 10 秒 + 冰刺 3 次
> ```
>
> **為什麼 C 排在 A 前面**：C 是**基準線**。若 locomotion 本身已經有問題，
> 之後 A 的每一個異常都會分不清是 Action 造成的還是本來就壞的。
> **先證明地板是平的，再在上面放東西。**
>
> **為什麼 B 排最後且是選配**：它的主要證據是稽核，Play demo 只是讓它看得見；
> 而且它需要改 config 再改回來，放在最後不會污染 A／C 的環境。
>
> ---
>
> ## ⑤ 跑完之後
>
> 把結果貼回來（影片 ＋ Profiler 截圖 ＋ 哪幾條 Fail），我再：
> - 依結果更新 ADR-005 §4 的勾與 §5 修訂紀錄
> - **通過** ⇒ `Trial → Accepted`，`docs/11` §10.2 的 ⛔ 名單自動解除
>   ⇒ `docs/13` §7 的四步實作順序可以開工
> - **不通過** ⇒ 依 §4 的「未通過」條款：先修 ADR ／ Living Spec，**不得補 workaround**
>
> ⚠️ **本輪我沒有改動 ADR-005 的狀態**，也沒有預先打勾——依使用者指示，跑完再裁決。
>
> </details>
>
> ---
>
> ### 🌀 2026-09-06 — 敵人 Hold battle-circle 側移（程式完成）
>
> - `AIMovementSource` 的 Hold 由零意圖改為水平 `toTarget` 的單位切線意圖；Approach／Retreat 原邏輯不變。
> - 新增 `holdStrafeSpeedNormalized = 0.35` 與 `strafeDirectionFlipInterval = 2.5`；方向符號為 producer
>   私有跨幀狀態，初始方向與每次間隔含隨機差異，仍只有 `ProduceIntent` 寫 `MovementIntent`。
> - Hold 基準速度經既有 `ResolveDesiredSpeedNormalized` 路徑套用 Slow；NavMeshAgent 仍只查路徑、不搬 Transform。
> - `SlowEffectTests` 新增 4 條純函數測試：垂直、反向、水平＋正規化、零向量安全。
> - `Project.Runtime`／`Project.Tests.EditMode` `dotnet build` 均 0 error。沙箱禁止讀 LocalAppData 的 SDK 探測，
>   故命令列額外指定已安裝的 Windows SDK path；Runtime 0 warning，EditMode 有 4 個既有 `MSB3277` 參考衝突警告。
> - 尚需 Unity Play 觀感驗收：側移方向翻轉是否自然，以及 NavMesh 邊界／障礙旁的切線是否需要日後加可行走性修正。
>   現有 `MotionDriver` 會朝移動方向旋轉，因此本輪是「面朝切線繞行」；持續面向玩家的真 strafe 需要日後獨立
>   combat-facing seam，不能從 movement producer 越層偷接。
>
> ---
>
> ### 🎯 2026-09-04（晚）— **下個 session：把 ADR-005 Trial 結掉**（原交辦紀錄）
>
> #### ⛳ 開場指令（使用者 2026-09-04 明確裁決，優先於一切預設行為）
>
> **直接從下方 ③ 的 Fireball／Ice Definition 接線開始。**
> ⛔ **不重新規劃**、⛔ **不先整理文件**、⛔ 不重讀 design-doc／dev-spec「熟悉一下」。
> 本段 ＋ `docs/11` §4 就是全部所需的 context。
>
> **一路推到「三技能可 Play 的切片」再集中驗收**——中途不要停下來要人工確認。
> 唯一的例外：**實作證明現有架構走不通**（＝ `docs/12-workflow.md` 的 stop condition：
> 架構假設被證偽／需要新 authority 或黑板 schema／規格互相衝突／無法安全繼續）。
>
> 📌 「新增一個 component、新增一個 `[SerializeField]`、最終會需要 prefab reference」
> **都不是停下來的理由**（`CLAUDE.md`「明確不構成 stop condition 的事」）。
>
> **一句話**：程式面已全部到位、Melee bake path 已 Play 確認走通；剩下的是**兩個法術的資產接線 ＋ 三輪驗收**，
> 全部不需要再寫 runtime 程式。
>
> ---
>
> #### ① 開工前必做：重跑 EditMode（上一輪剛修完，未確認）
>
> 2026-09-04 首次對「擴張後的測試集」實跑，出現三條紅：
> `T24_MeleeHitbox`／`ActiveSlow_...KeepsThirtyPercent`／`RepeatedSlow_RefreshesExpiry...`
>
> **成因（三條同一個）**：**EditMode 不在 Play mode ⇒ `AddComponent` 不呼叫 `Awake()`**
> ⇒ `AIMovementSource._effectState`、`MeleeHitboxSink.hitbox` 兩個 sibling 快取恆為 null
> ⇒ 倍率恆 1、命中窗開不了。**斷言沒錯，是 fixture 沒把環境建起來**（假性失敗：測到的是「沒有效果」那條路徑）。
>
> **已修**：抽出 `internal void ResolveEffectState()`（`AIMovementSource`）與 `internal void ResolveHitbox()`
> （`MeleeHitboxSink`），`Awake()` 照樣呼叫、**production 行為一字未改**；測試 fixture 顯式補上該步。
> 比照本專案既有慣例（`TemporaryGameplayEffectState.ApplySlowAt` 等 `internal` 顯式版本）。
> ⇒ **兩個 assembly 編譯 0 error，但尚未重跑測試。第一件事就是跑它。**
>
> ⚠️ **這個坑會再咬**：往後任何寫在 `Awake` 裡的快取，在 EditMode 測試都是 null。
> 值得寫進 `docs/12-workflow.md` 的 Verification Ladder（EditMode 那一階的已知限制）。
>
> ---
>
> #### ② 主線：ADR-005 §4 還缺 A／B／C／E
>
> | 條 | 缺什麼 | 需要 |
> |---|---|---|
> | **A** 兩份 Definition 獨立觸發 | Fireball／Ice 兩份 Definition ＋ sink 接線 | 資產 ＋ Play |
> | **B** 加下一個 Action ＝ 零 runtime 程式 | **真的加第 4 個 Action** 才算數 | 資產 |
> | **C** 既有無回歸 | Play | ⚠️ 見下方 Throw 註記 |
> | **D** EditMode 全綠 | 🔄 **勾已撤回**，需對當前測試集重跑（見①） | 跑測試 |
> | **E** 零 GC | Profiler（dev-spec §7.4 SOP） | Development Build |
>
> **D／F／G 之外全部未成立。** F（無第二個 gate 權威，A24 守）與 G（Slow 跨系統傳播，A25 守）已通過。
>
> 🔴 **C 的 Throw 子句需重述**：`actionDefinitions` 一旦非空，`paramsMappings` 的相容退路整條不走
> ⇒ **`ThrowDefinition` 已不再被解析**（Slot1 由 Melee 接手，`docs/11` §5.1 的既定移交）。
> Throw 因此**無法再被觸發、也就無法被回歸測試** ⇒ 驗收時 C 應改為
> 「Idle／Move／Jump／Roll 無回歸」，並註明 Throw 已依計畫退場。
>
> ---
>
> #### ③ 使用者側接線清單（一次做完）
>
> 1. ✅ **已建** `FireballDefinition.asset`：`Slot2`／`Spell_Fireball_1`（＋連段 2／3）／`FallbackDuration` 1.2／`EmitsRelease` @0.35／**冷卻 1.5**
> 2. ✅ **已建** `IceSpellDefinition.asset`：`Slot3`／`Spell_Ice`／`FallbackDuration` 1.366667／同上／**冷卻 4**
> 3. ✅ **已註冊**進 `PlayerStateMachineConfig.actionDefinitions`（三筆：Melee ＋ Fireball ＋ Ice）
>    ⚠️ **W4 現在會紅**，直到第 5 項把 Slot2／Slot3 的 sink 綁上——訊息會直接指出缺哪一格。
>    這是測試在做它該做的事，不是壞掉。
>    📌 詳細欄位表見 `docs/11` §4「已落地的 Definition 資產」。
>    🟡 `FallbackDuration` 由 frame 數 ÷ 30 推得，**fps 未經 Editor 確認**；前搖過長／動畫被切就調它。
> 4. ✅ **已建兩個 `ThrowProjectileEmitter` 實例**：`FireballEmitter`／`IceEmitter`，共用 `ThrowSpawnPoint` 與根上的 `AimResolver`，各自指向對應法術 prefab
> 5. ✅ Runner 的 `Action Sink Bindings` 已填滿三格：Slot1→`MeleeHitboxSink`／Slot2→Fireball emitter／Slot3→Ice emitter
>    🔴 該清單同樣是 **all-or-nothing**——非空即完全取代舊的單顆 sink 欄位
> 6. ✅ `AnimancerFacade.transitionMappings` 已補**四**列法術動畫鍵 → `Spell_Fireball_1/2/3`／`Spell_Ice`
>    （Melee 的 `Melee_Slash1` 已在第 2476 行，不用再加）
>    ✅ **四份 `TransitionAsset` 已建**（2026-09-04 使用者改指定素材，全改 `HumanF`）：
>    `Spell_Fireball_1/2/3.asset`（`Direct1H01_L` → `Direct1H01_R` → `Direct2H01`，連段三段）
>    ＋ `Spell_Ice.asset`（`Call1H01_L`，單手上舉召喚）。
>    全部 `animationType: 3`（Humanoid），與現用的 `slash1.fbx` 同類 ⇒ 可重定向到 X Bot。
>    ⚠️ **四列都要拖**：連段機制已落地（`docs/11` §4.3），`Spell_Fireball_2/3` 現在會被引用。
>    少拖任何一列 ⇒ 該段 `IsPlaying` 為 false、動畫不播（安靜失敗①）。
>
> ### 🔄 2026-09-05 使用者裁決：**Ice 改為地面 AoE，不再是投射物**
>
> 理由：「投射物 ＋ Slow 已經驗證過，再做一個飛行冰法只是重複同一條路徑，資訊增量太低。」
> ⇒ Ice 改用新的 **`GroundEffectSink`**（`docs/11` §4.4），用來證明**同一套 Action 架構可以接不同的 execution shape**。
>
> **程式已完成**（`Project.Runtime` ＋ `Project.Tests.EditMode` 皆 `dotnet build` 0 error）：
> - 新增 `Presentation/Actions/GroundEffectSink.cs`——第三個 `IActionLifecycleSink` 實作
> - `SlowMovementSpeedMultiplier` 由 `ThrownProjectile` private const 提升到 `TemporaryGameplayEffectState`（出現第二個投遞者）
> - `SlowEffectTests` +3 條；`ArchitectureRegressionTests.A21` 的 seam 清單補上 `GroundEffectSink.cs`
> - 🐞 同時修掉 `ThrowProjectileEmitter._releasedThisExecution` 吞掉連段第 2／3 段 Release 的 bug（`docs/11` §4.3）
>
> **⚠️ 使用者側待辦（Editor）**：
> 1. 讓 Unity 產生 `GroundEffectSink.cs.meta`
> 2. `X Bot` 上把 `IceEmitter` 的 `ThrowProjectileEmitter` 換成 `GroundEffectSink`，
>    `Effect Prefab` ← `Human_Spell_Ice`，`Aim Resolver` ← 根物件
> 3. Slot3 的 sink binding 重指到新元件（換元件後引用會變 None）
> 4. `Projectile_Icebolt.prefab` 本輪不再需要（先留著，別急著刪）
>
> ---
>
> ### 🥊 2026-09-05 — 敵人攻擊決策（`AIInputSource`）**程式完成，接線未做**
>
> **目標**：讓敵人的攻擊正式走 `IInputSource` → `ProcessIntents` → `ActionState`。
> 使用者明確限定本工作包**不碰** prefab YAML 與 `EnemyStateMachineConfig` migration。
>
> **已完成**（`Project.Runtime` ＋ `Project.Tests.EditMode` 皆 `dotnet build` 0 error）：
> - 新增 `Core/Pipeline/AIInputSource.cs`——射程內產生 `Slot1ButtonDown`，**不含任何冷卻計時器**
> - 新增 `AIInputSourceTests.cs`（7 條）
> - `ArchitectureRegressionTests` 新增 **A27**：攻擊 trigger 只能由 `IInputSource` 產生；
>   `AIMovementSource` 不得出現 `Slot*ButtonDown`；`AIInputSource` 不得出現計時／冷卻符號
> - 資產 `Enemy_Punch_R.asset` ／ `EnemyPunchDefinition.asset` 已由 Codex 完成並驗收
>
> **🔴 已知介面限制（不在本輪處理，但要記住）**：
> `IInputSource.FetchRawInput(ref InputData)` **拿不到黑板** ⇒ 輸入源無從得知上一次按下有沒有被消化。
> 因此 `AIInputSource` 目前是 **level-triggered**（射程內每幀為真），語意上等於「一直按著」，
> 而 `Slot1ButtonDown` 名義上是邊沿訊號。今天安全（`EnemyPunchDefinition` 沒有 `Loop`／`WaitForTrigger`／
> `ChainSegments`，兩條讀 re-trigger 的路徑都走不到）；**敵人一旦要連段或蓄力就會咬人**。
> 屆時正解是替 `InputData` 補 `Slot1ButtonHeld`（比照既有 `SprintButtonHeld`／`SprintButtonDown` 並存先例），
> **不是**在 AI 裡補計時器。`AIInputSourceTests.AttackIntent_IsLevelTriggered_KnownInterfaceLimit` 已釘住。
>
> ---
>
> ### 📌 已登記、**本輪刻意不做**的兩件事
>
> **① ~~`EnemyStateMachineConfig.actionDefinitions` migration~~ ✅ 2026-09-05 已完成**
> 見下方「Enemy Combat Vertical Slice」。Damage ＋ Punch 同一次搬進 `actionDefinitions`，
> 死掉的 `paramsMappings` Action 列已移除，並由新的 **W8** 守住。
>
> > ---
>
> ### ⚔️ 2026-09-05 — **Enemy Combat Vertical Slice**（接線完成，待 Play 驗收）
>
> **目標**：敵人從「會移動的假人」變成「接敵 → 站位 → 出拳 → 收招 → 再攻擊 → 被打出反應」。
>
> | 改動 | 內容 |
> |---|---|
> | `Y Bot.prefab` | 掛上 `AIInputSource`（root，`target` 與 `AIMovementSource` 指向同一個 X Bot Transform、`attackRange` 2）；`inputSourceComponent` 指向它；`transitionMappings` 補 `Enemy_Punch_R` |
> | `EnemyStateMachineConfig.asset` | 新增 `actionDefinitions`＝**DamageDefinition（Reaction）＋ EnemyPunchDefinition（Slot1）**；移除已失效的 `paramsMappings` State 5 |
> | `PlayerStateMachineConfig.asset` | 一併移除已失效的 `paramsMappings` State 5（指向早已退場的 `ThrowDefinition`）——見下方 scope 註記 |
> | `EnemyPunchDefinition.asset` | `EmitsRelease` 1 → **0** |
> | `AIInputSource.cs.meta` | 手寫（比照本專案 `.cs.meta` 的極簡格式：只有 `fileFormatVersion` ＋ `guid`） |
>
> **為什麼 `EmitsRelease` 改 0**：本輪敵人出拳是**純表演**——Y Bot 沒有 Slot1 的 sink，
> 而玩家身上也還沒有 `ActionRequestTarget`（`X Bot` 的該欄位是 0）⇒ 就算開了 Release 也沒有接收端，
> 只會讓 **W4** 變紅。敵人打傷玩家屬於「玩家受擊」那條線，不在本 slice。
>
> **新增測試**（`PrefabWiringTests`，皆為 generalized invariant、非寫死清單）：
> - **W7** 已註冊 Action 的 `AnimationKey`（**含 `ChainSegments`**）必須在該角色的 `transitionMappings` 解析得到
>   ← 抓 `docs/11` §4.1 的安靜失敗①，2026-09-04／09-05 實際踩過兩次
> - **W8** `actionDefinitions` 非空時，`paramsMappings` 不得再留 Action 綁定（遷移未做完的死接線）
> - **W9** 跑一次 `Initialize()`，斷言每份 Definition 都能以自己的 `Slot` 解析回自己（身分唯一性）
>
> ⚠️ **scope 註記**：W8 上線後照出**玩家 config 也有同一種死接線**（指向已退場的 `ThrowDefinition`）。
> 使用者本輪只交辦敵人，但留著會讓交付時測試是紅的 ⇒ 一併移除。
> 它本來就已被 `docs/11` §5.1 記為「不再被解析」，這次只是把資產補齊到與文件一致。
>
> **② `ThrowSpawnPoint` 改掛 Spine2 ＋ forward offset**
> 2026-09-05 已先用「Fireball emitter 的 `spawnPoint` 改指 `mixamorig:Spine2`」達成胸口發射（單一欄位改動）。
> 若要更乾淨的前方偏移，需把 `ThrowSpawnPoint` 重新掛到 Spine2 底下——
> **那是 parent／children 的階層變更，一律在 Unity Editor 內處理，不手改 prefab YAML。**
>
> ---
>
> ### 🩸 2026-09-06 — 受擊手感 ＋ Reaction 同 slot 重入 ＋ AI 決策可視化
>
> **① 受擊 4.1 秒 → 1.17 秒（換 clip，不改程式）**
> `DamageDefinition` 原本指向 `Idle_Hit_Strong_Left`（`BakedDuration` **4.1**，那是重擊踉蹌＋完整恢復），
> `FallbackDuration: 4.1` 只是照抄它。改用專案**已烘好**的 `Fists_Hit_Right`（1.1667）＋ `Bake_Fists_Hit_Right`。
> 📌 走的是 CLAUDE.md 動畫升級順序的**第 3 階（換 clip）**，沒有編輯或複製任何 AnimationClip。
>
> **② Reaction 同 slot 重入（使用者裁決採方案 b）**
> `ActionState.CanReenter` 新增 `AllowsSameSlotReentry(slot) => slot == ActionSlot.Reaction`，
> 並把 `DamageDefinition.Interruptible` 改為 1。
> 🔴 **普通 Action 的語意一字未改**——`T33`／`T33b` 就是為了守這件事而寫的：
> 若哪天有人把它放寬成「Interruptible 就好」，測試會紅並說明代價（連段第 2 段會被第 3 段的請求吃掉）。
> 理由與 §8.3 把 `Reaction` 排除在轉向規則外相同：**受擊不是出手**。
>
> **③ AI 決策層可視化**（`OnDrawGizmosSelected`，全段包在 `#if UNITY_EDITOR`）
> `AIMovementSource`：接戰帶內外圈、Approach/Hold/Retreat 顏色、到目標連線與水平距離。
> `AIInputSource`：攻擊圈（想出手＝暖色實心／不想＝暗灰）、attack desire、距離。
> 🎯 **目的是分流除錯**：距離與模式一致但角色仍亂走 ⇒ 問題不在 decision 層。
> ⚠️ 兩個元件各畫各的、**不互相引用**——`Core/Movement` 頂層的 LayerRule 白名單不含 `Core.Pipeline`。
>
> **④ Ice 落點再收斂**：`minCastDistance` 0.8／`maxCastRange` 2／`defaultCastDistance` 1.4。
> 📌 實際落點幾乎都頂到上限（瞄準射線通常打在遠處地面）⇒ **真正決定距離的是 `maxCastRange`**。
>
> ---
>
> ### 🤖 2026-09-06（夜）— 自主推進批次（使用者休息中，只回報不詢問）
>
> | # | 內容 | 風險 |
> |---|---|---|
> | 1 | **冷卻變異** `ActionDefinitionSO.CooldownVariance`；`EnemyPunchDefinition` 填 1.2 ⇒ 攻擊間隔 2–3.2 秒 | 低（新欄位預設 0 ＝ 舊行為） |
> | 2 | **W10**：`attackRange` 必須落在接戰帶 `[min, max]` 內 | 低（純測試） |
> | 3 | **鎖敵可視化**：`AimResolver` 畫瞄準線、soft-target 錐在該距離的實際半徑、已鎖定的出手朝向 | 低（Editor-only） |
> | 4 | **武器掛載** `Presentation/Equipment/WeaponSocket.cs` ＋ X Bot 掛上 `Sword.obj` | 中（動到兩個 prefab） |
>
> **① 為什麼冷卻變異放在資產而不是 AI**
> 業界通則是「近戰敵人平均 2–3 秒出手一次」，固定間隔會像節拍器。但 `AIInputSource` 明確不准自建計時節流
> （能不能出手的唯一回答者是 `ActionState`，ADR-004 D2）。做成 authored 欄位後，節奏變成**可調資料**——
> 玩家技能填 0 拿到可預測節奏、敵人填 > 0 拿到活的節奏，**兩者共用同一段程式**。
> 只加不減：實際冷卻永遠 ≥ authored `Cooldown`（`T34`／`T34b`／`T34c` 守）。
>
> **④ 🎉 專案裡本來就有一把完整的劍**
> `Assets/EEJANAI_Team/FreeSwordAnimations/Models/SwordSample/Sword.obj` ＋ PBR 貼圖 ＋ 材質。
> 先前回報「專案沒有武器模型」是**錯的**——我用 `find -iname "*sword*"` 搭配 fbx/obj 過濾時被 `-o` 的優先序坑了。
>
> **為什麼是執行期掛載而不是把劍拖進 prefab 階層**：
> 拖進階層 ⇒ 每個角色各自複製一份武器子樹，換武器要逐一改；
> 執行期掛載 ⇒ 武器是**一個資產引用**，換武器＝換一個欄位。
> 也順帶避開「改 prefab 父子結構」這個最高風險的 YAML 編輯類別。
>
> ⚠️ **Y Bot 的 prefab 只有 2 個 GameObject**（骨架來自模型實例）⇒ prefab 階段沒有手骨 Transform 可拖。
> 因此 `WeaponSocket` 支援**骨骼名稱查找**（`mixamorig:RightHand`），兩個角色共用同一套接線。
> 找不到骨骼時退回角色原點——**很醜但看得見**，比靜默消失便宜太多（`WeaponSocketTests` 守）。
>
> 📌 **Y Bot 的 `weaponPrefab` 刻意留空**：敵人打的是 `Fists_Punch_R`（空手拳），給它劍會與動作矛盾。
> 元件先掛著，等敵人有持劍動畫再填。
>
> **⚠️ 待 Play 調整**：劍的 `localPosition`／`localEulerAngles` 目前全 0，八成不對。
> 進 Play 後選中生成出來的劍、把對的數值抄回 prefab 即可；也可以在 Inspector 右鍵
> `Rebuild Attachment` 即時重掛，不必重進 Play。
>
> ### 🧱 2026-09-06（夜）— 兩個想做的功能撞到同一堵牆：**ADR-005 沒結案**
>
> 使用者列的「施法＋移動」與「大幅度轉身」，調查後**都不能現在做**，而且是同一個原因。
>
> | 功能 | 需要動 | 為什麼卡住 |
> |---|---|---|
> | **施法＋移動**（上半身層） | `AnimationFacadeBase` 契約 | 在 `docs/11` §10.2 的 ⛔ 名單上 |
> | **大幅度轉身**（C2／C3） | `LocomotionModel`／`LocomotionStopSelector` | 同上，且是 Acceptance **G** 的零修改名單 |
>
> 那份 ⛔ 名單是 ADR-005 Acceptance **G**（「Slow 跨系統傳播，五個下游檔案零修改」）的**前提**。
> 現在動它們，G 的證據鏈就斷了；而且之後 Play 出問題時將無法分辨是哪一邊造成的。
> 加上 ADR-004 §0 的「同一時間只允許一個 Trial」——**先關掉 ADR-005，這兩件事才排得進來。**
>
> **🎯 所以最高優先的其實是：把 ADR-005 的 A／B／C／E 驗完。** 四條都需要 Play ／ Profiler，只有使用者做得到。
>
> #### 📦 轉身：**設計與資產都已就緒，只差開工許可**
>
> - `Bake_TurnRt180`（`RotationFinishedTime` 1.5333）與 `Bake_RunFwdTurn180_R_LU`（0.7667）**已烘好**
> - 但兩者**目前被任何東西引用 0 次**——烘完就擱著
> - `docs/07` §852 已寫明：Turn／Pivot 走**同一條 Request→Selection→Motion 鏈**，
>   且「`p ≠ 1` 的 yaw 正確性已由本規格的多載預先解決」
> - ⇒ 不是設計缺口、不是素材缺口，**純粹是排序問題**
>
> #### 📄 新增 `docs/ADR/006-upper-body-layer.md`（🔵 **Proposed，待裁決**）
>
> 主張 Animancer 分層 ＋ AvatarMask，兩條決策草案：
> **D1 分層是 Facade 的職責，State 不得認識層**（否則 Presentation 的分層策略倒灌進 FSM）；
> **D2 層與遮罩是 authored data**（加一個分層動作＝加一列映射，零程式——與 ADR-005 同一條紀律）。
>
> 順帶記錄一個**已存在的死欄位**：`PlayerRuntimeData.UpperBodyWeight` 有寫入者（`LocomotionModel` 每幀寫）
> 但**全專案只有 Editor 除錯面板讀它**。ADR-006 的 Acceptance **C** 要求它「要嘛被消費、要嘛刪除」。
>
> #### ✅ prefab 完整性稽核（這一輪手改了不少 YAML）
>
> `X Bot` ／ `Y Bot` 皆通過：
> 每個 `- component: {fileID}` 都有對應的 `--- !u!114 &fileID` 區塊；
> 每個 `m_GameObject: {fileID}` 都指向存在的 GameObject。無懸空引用。
>
> ### 📋 已登記的優先級（2026-09-06 使用者裁決）
>
> | 順位 | 項目 | 理由 |
> |---|---|---|
> | **先** | **Input buffer（預輸入緩存）** | 直接改善操作容錯與連段手感；`ChainInputOpenNormalized` 只解了「太早按」的一半，「硬直中按」仍然丟失 |
> | **後** | **方向性受擊**（依命中角度選受擊動畫） | ⚠️ 需要先決定 **context → AnimationKey** 的架構形狀：目前一個 slot ＝ 一份 Definition、一個 Phase ＝ 一個 `AnimationKey`，依角度選要嘛在 Definition 內放多個候選、要嘛換別的形狀。**這是真正的架構岔路，不先談形狀不動手。** 四份 clip（`Fists_Hit_Left/Right`、`Idle_Hit_Strong_Left/Right`）已在專案內 |
>
> 參考來源：2026-09-06 使用者提供的第三方戰鬥系統影片（UE demo）。該影片同時展示了
> **攻擊令牌／站位槽的 gizmo 可視化**——與本輪 ③ 的動機一致，也再次支持
> 「Combat Director 等多隻敵人實際壞掉後再開 ADR」的既有判斷。
>
> ✅ **2026-09-05 磁碟接線完成**：`X Bot.prefab` 現為 3 顆發射器／3 筆 sink binding／17 筆 transition mapping；
> 新增內部 fileID 無重複，prefab 引用與欄位值已做靜態核對。solution 編譯完成（只有既存的 Animancer
> NUnit target-framework warning，無 error）。Unity Editor 當時已開啟，故 Test Runner／Play 驗收仍在現有 Editor 內集中執行。
>
> 🔧 **2026-09-05 實機回報後修正**：兩顆 emitter 的 `projectilePrefab` 欄位型別是 `ThrownProjectile`，
> 初次接線誤指到 prefab 根 `GameObject` 的 fileID，Unity 因型別不符將它視為未綁定。已改指向兩顆 prefab 內的
> `ThrownProjectile` component fileID（`2915347899319234830`）；GUID、其餘欄位與所有其他接線不變。
>
> 7. ✅ **連段已實作**（2026-09-04）：`ActionDefinitionSO` 加 `ChainSegments` ＋ `ChainInputOpenNormalized`；
>    `ActionState` 加排隊式切段。`Project.Runtime` 與 `Project.Tests.EditMode` 皆 `dotnet build` **0 error**。
>    新增 EditMode **T26–T29**（尚未在 Test Runner 實跑——見①）。
>    🔴 唯一改動的既有語意：`_releaseEmittedThisExecution` 改為切段時重置（每段各發一次 Release）。
>
> 📌 **法術素材**：`Kevin Iglesias/.../MagicAttacks/{Call,Directional,Omnidirectional}`，
> 每組都有 `Load`／`Cast`／合一三個檔。**用合一檔**——`docs/11` §4 已裁決不做 Load/Cast 分段
> （Throw 已展示過多 phase，且需求是「快速施展」）。
>
> ---
>
> #### ④ 明確延後、不進本輪
>
> | 項目 | 狀態 |
> |---|---|
> | **武器模型（劍）** | 🔴 沒有劍，近戰在**作品集影片**裡不成立（揮空手讀不出來）。但**不阻擋 ADR-005**——A／B／C／E 驗的是身分與冷卻，與手上有無道具無關。歸「作品集素材」線，與法術特效一起處理 |
> | **Melee motion mapping 展示** | 同上，需要劍才有意義。bake path 本身**已 Play 確認走通**（位移 ＋ 約 90° 轉向皆出現） |
> | 一維烘焙模型 → 三軸 root motion | dev-spec §7.3 已記完整調研與**兩個待驗前提**。⛔ 不在 Trial 期間動 |
> | Lock-on／auto-target／朝向規則 | `docs/10` 已改為延後；`docs/11` §8.3 記著規則但未實作 |
> | 冷卻 HUD | `docs/11` §6.2：曝光路徑是**黑板 schema 變更** ⇒ 要開 ADR，本輪不做 |
> | `AIMovementSource` 直讀 effect 元件 | dev-spec §7.3 的 architecture debt，Play 已證明功能成立 ⇒ 償還與否與功能脫鉤 |
>
> ---
>
> #### ⑤ ⚠️ 一大批未 commit（含治理文件）
>
> ```
>  M AGENTS.md / CLAUDE.md            ← 工作流改版
>  M ArchitectureRegressionTests.cs   ← +128 行
>  M AIMovementSource / MeleeHitboxSink / 三個測試檔  ← ①的修正
> ?? docs/12-workflow.md              ← 239 行，工作流正本
> ?? PrefabWiringTests.cs             ← 499 行，W0–W6 接線測試
> ?? Assets/_Project/Tests/PlayMode/  ← 全新 assembly，P1–P4
> ?? .codex/config.toml               ← 內容未審，決定是否進版控
> ```
> 四個 assembly（含 PlayMode）**皆已編譯 0 error**。使用者裁決：測試綠了再整理 commit。
> `.codex/config.toml` 需先看內容（可能含個人設定）再決定。
>
> ---
> ### 🧩 2026-09-04 — **開發工作流改版（Feature Slice）＋ 驗證階梯落地**；新增 L1 接線測試、A4 白名單、PlayMode 測試層
>
> **一句話**：工作單位從 component 改為 **Feature Slice**，並把「EditMode 做不到就歸類成人工」這個邏輯跳躍修掉——
> 新增 **Verification Ladder**（L0–L6）作為往後選擇驗證方法的基準，同時補上三項現在就值得自動化的測試。
> **本工作包是 workflow-doc ＋ test-only，未改動任何 runtime 架構、未動任何 `.prefab`／`.asset`／`.meta`／場景。**
>
> **① 文件變更**
> | 檔案 | 內容 |
> |---|---|
> | **`docs/12-workflow.md`**（新） | 正本。Feature Slice 定義、明確**不**構成 stop condition 的清單、Integration Boundary／Batch Integration、Integration Spike 例外、**Verification Ladder L0–L6**、效能驗證保留條款 |
> | `CLAUDE.md` ／ `AGENTS.md` | Preferred Workflow 改寫為 Feature Slice 版並**兩份逐字同步**；`Stop After Edit` 語意收斂為 **Verification Ownership** |
> | `docs/00-map.md` | 加入 `docs/12` 指標 |
> | `docs/02-dev-spec.md` | §0.2 加 `PlayMode/`；§7 加階梯對應；新增 **§7.1.1（W0–W6）**／**§7.1.2（P1–P4）**；A4 條目補白名單升級；M3／M8 標註已自動化的部分 |
>
> **🔴 已修正的 drift**：`AGENTS.md` 原本寫 "Mandatory before making **any** file changes"，
> 而 `CLAUDE.md` 早在 2026-08-29 就有 trivial／local／test-only 例外 ⇒ Codex 與 Claude 讀到兩套節奏。已同步。
>
> **② 新增／修改的測試（全部 `dotnet build` 0 error 實跑驗證）**
> | 測試 | 取代哪一類人工檢查 |
> |---|---|
> | **`PrefabWiringTests.cs`**（新，L1，W0–W6） | §7.2-**M3** 那一整面牆的接線指示中可機器判定的部分 |
> | **`ArchitectureRegressionTests.A4`**（升級） | 黑名單 → **黑名單＋白名單**。`AIMovementSource → Core.Effects` 這個**已證實的漏網案例**改以明文例外留存，不再是靜默通過 |
> | **`Assets/_Project/Tests/PlayMode/`**（新組件 ＋ P1–P4） | §7.2-**M8** 的 ①②⑦ |
>
> ⚠️ **A4 修法刻意是 generalized invariant，不是特判 class name**：升級的是機制（預設拒絕），
> `AIMovementSource` 只是被它照出來的其中一個點。
>
> ---
>
> ### 📋 使用者側整合 checklist（**一次做完，不需要分批**）
>
> **A. 讓 Unity 生成 `.meta`（必要前置）**
> - [ ] 開啟 Unity，讓它為以下新檔生成 `.meta`：
>       `Assets/_Project/Tests/EditMode/PrefabWiringTests.cs`
>       `Assets/_Project/Tests/PlayMode/`（資料夾）
>       `Assets/_Project/Tests/PlayMode/Project.Tests.PlayMode.asmdef`
>       `Assets/_Project/Tests/PlayMode/PauseLifecyclePlayModeTests.cs`
> - [ ] 確認 Test Runner 出現 **PlayMode** 分頁（＝新 asmdef 已被辨識）
>
> **B. 跑自動驗證（預期全綠）**
> - [ ] EditMode Run All —— 新增 7 條 W 測試應全過（W1–W6 對現況 prefab 應為綠）
> - [ ] PlayMode Run All —— P1–P4
> - [ ] ⚠️ 若 **W4** 變紅，那不是測試壞了，是**真的有 slot 沒接 sink**——訊息會直接指出是哪個 prefab／哪個 Action／該補哪一筆
>
> **C. 待裁決（我沒有動，因為超出 test-only scope）**
> - [ ] 🔴 **`GamePauseController` ／ `CursorModeController` ／ `UiModeArbiterSource` 目前掛在 `X Bot` 角色 Root 上**，
>       但 dev-spec §7.2-M3 與 design-doc §4.9 寫的是「在場景中另建一顆物件掛，**不要掛在角色 Root**」（全域狀態不屬於角色）。
>       **功能上兩種接法都能運作**（掛在階層內時 `GetComponentsInChildren<IArbiterSource>` 會自己找到，因此 `externalArbiterSources` 空著也對），
>       所以 W5 刻意**斷言結果而非位置**。**要嘛改資產、要嘛改文件——目前是文件與資產不一致。**
> - [ ] ⚠️ **`ThrowDefinition.asset` 沒有 `Slot` 欄位值**（序列化早於該欄位存在 ⇒ 實際為 `None(0)`）。
>       目前它**沒有**被註冊進任何 config，所以不會出事；但一旦加進 `actionDefinitions` 就會綁到 `None`。建議現在就在 Inspector 補上正確 slot。
> - [ ] ⚠️ **`EnemyStateMachineConfig` 的 `actionDefinitions` 是空的**，而 `DamageDefinition`（Slot 100 `Reaction`）已存在、Y Bot 也已掛 `ActionRequestTarget`。
>       ⇒ **近戰命中敵人時，敵人解析不到 Reaction definition**，受擊反應不會發生。這是既有交辦的接線缺口，不是新問題。
>
> **D. 人工驗收（真的只剩這些）**
> - [ ] 近戰命中手感、受擊反應的動畫觀感
> - [ ] 暫停／UI 模式的按鍵 interaction（Hold／Tap 分流）——需要真實輸入裝置，尚未自動化
> - [ ] 游標的實際套用與自癒（M9）——Editor 視窗焦點會影響 `Cursor`，**已明示降級**為人工
> - [ ] Profiler zero-GC（§7.4 正式 SOP）——**PlayMode 探針不能取代它**
>
> ---
>
> **③ 我判斷「現在不該自動化」的候選與理由**
> | 候選 | 決定 | 理由 |
> |---|---|---|
> | **Mixer／Gait 校準不變量**（原構想 C） | ❌ 不做 | `GaitProfileSO` 與 `MotionBakeData` **之間沒有任何序列化連結**——`intensity = speed_i / speed_max` 的公式需要知道「哪個 clip 是 walk／run／sprint 檔位」，而那個對應只存在於 Animancer Mixer 的 internal `_Thresholds`（B11 Gate B）與人的腦中。要做就得新增 SO 欄位＝改資產 schema，超出 test-only scope。⚠️ 另外 §7.2-M4 自己也寫了 intensity「允許依手感偏離」⇒ 它本來就不是硬不變量；真正硬的那一半（Mixer threshold）正好是碰不得的那一半 |
> | **Cursor 狀態的 PlayMode 斷言** | ❌ 不做 | Editor 視窗焦點會改動 `Cursor.lockState`（M9 已記錄），Test Runner 下噪音大。合併政策（`WantsFreeCursor`）本來就已由 EditMode 守住，PlayMode 只會加偽陽性 |
> | **`ProfilerRecorder` zero-GC hard gate** | ⏸ 暫緩 | 可做**回歸警報**，但 Editor 下的數字含 Editor 開銷、不等同 Player。在確認噪音水準之前不設為 hard gate——紅了就無視的閘門比沒有閘門更糟（`docs/12` §7 已寫成保留條款） |
> | **完整管線的 PlayMode 組裝測試**（按 Space 不進 JumpState 等） | ⏭ 下一個 slice | 需要 Runner ＋ Facade ＋ MotionDriver ＋ Config 全組裝；第一批先證明 PlayMode 這一層穩定可用，再往上疊。P3 已先鎖住它的必要條件 |
> | **Input System `InputTestFixture`** | ⏭ 下一個 slice | 本輪的四條測試都不需要它，故 `Project.Tests.PlayMode.asmdef` **刻意還沒有**引用 `Unity.InputSystem.TestFramework`（Input System 1.19.0 有支援，要用時補一行 reference 即可） |
>
> **④ 本工作包是否涉及 architecture contract change**：**否**。無新 ADR。
> 依 `CLAUDE.md` 的 routing rule，流程與驗證屬 Living Docs，不開 ADR。
>
> ### 🧩 2026-09-03 — Multi-Action 程式批次完成；三 assembly 已編譯，EditMode／Play 待集中驗收
>
> - `AIMovementSource` 新增可調近戰距離帶與 hysteresis：太近後退、帶內停住、太遠前進；仍只寫 `MovementIntent`。
> - 新增 `MeleeHitboxSink`：只由 Action lifecycle 的 `Release` 開窗、`Cleanup` 關窗；同一揮擊同一目標只送一次 `Reaction`，不以 VFX／particle collision 判定。
> - Quick／Ice 法術直接重用 `ThrowProjectileEmitter`／`ThrownProjectile`，不新增對稱複製類別；差異留在 prefab、速度與 `appliesSlow`。
> - 移除無 writer 的 `PlayerRuntimeData.AimTarget` 與 Editor 面板列；dev-spec schema 同步。
> - legacy 相容退路維持嚴格 slot：補 T22（Slot1 不得解析 Reaction）／T23（明設 Reaction 才可解析）；解析不到時新增 Editor-only、每 slot 一次警告。另補 T24（melee lifecycle／去重）與 A26（VFX 紅線）。
> - `Project.Runtime`／`Project.Editor`／`Project.Tests.EditMode` 均已由 `dotnet build` 編譯為 0 warning／0 error。⚠️ 不能在此環境跑 Unity Test Runner，故不得視為 EditMode 已通過。
> - 多 sink 缺口已結案：Runner 新增 `List<ActionSinkBinding>`，組裝期轉成 `ActionState.SlotCount` 大小的稀疏陣列；ActionState 依 `_activeSlot` 只通知該格 sink。`IActionLifecycleSink` 簽章不變。T25 鎖住 Slot2 不得誤觸 Slot1／3。
> - 相容規則為 all-or-nothing：新清單有任何一筆即完全忽略 legacy `actionReleaseSinkComponent`；清單全空才沿用單顆 sink。舊欄位名稱保留，既有 prefab 引用不會因欄位改名遺失。
> - 使用者側待辦：讓 Unity 產生 `MeleeHitboxSink.cs.meta`；建立 melee trigger Collider；Player Runner 的 Action Sink Bindings 一次填完整 Slot1=Melee、Slot2=Quick emitter、Slot3=Ice emitter，填完後可清空 legacy 單顆欄位；調整敵人的三個距離欄位；完成 EditMode、Play 與 Profiler 驗收。
>
> ### 🧩 2026-09-02（本機）— **合併完成 ＋ EditMode 全綠 ＋ Slow 切片 Play 通過**；ADR-005 §4 的 D／F／G 成立，A／B／C／E 待接線與 Profiler（最新，請先讀這段）
>
> **一句話**：遠端 ADR-005 分支與本機 WP1 已合併於 `integrate-adr005`，衝突依「保留雙方」處理完，
> **編譯與 EditMode 皆已實跑通過**——但 ADR-005 仍是 `Trial`，因為 A／B／C／E 全都需要資產接線、Play 或 Profiler。
>
> **① 分支拓撲（本輪由本機會話建立）**
> | 分支 | 內容 |
> |---|---|
> | `wp1-camera-aim` | `6ee3d45` — 本機所有未 commit 成果（AimResolver／ThirdPersonCamera／MotionDriver／A23／prefab／場景／ProjectSettings／Kevin Iglesias） |
> | `integrate-adr005` | 上者 ＋ `origin/claude/skill-system-showcase-6vqh6l`（ADR-005 多 Action）合併結果 |
> | `main` | 停在 `d5132e9`，未動 |
>
> **② 五項衝突的處置**
> | # | 處置 |
> |---|---|
> | **H1** `docs/11` 撞號 | 遠端的 `11-multi-action.md` 更名為 **`docs/11-multi-action.md`**；`00-map.md`、ADR-005、`docs/08` 的引用同步更新 |
> | **H2** B4 過時 | 遠端文件寫「`AimTarget` 是無 writer 的死欄位」已不成立（本機已有 `AimResolver`）。已依本機現況改寫 |
> | **H3** 程式衝突 | `BaseState.cs` 自動合併成功（`CanReenter` 與 `AnimationKey` 快取互不相干）。`ArchitectureRegressionTests.cs` 手動合併——**兩側各自新增的測項都保留** |
> | **H4** 文件衝突 | 加法為主，保留雙方 |
> | **H5** 本機未 commit | 已先 commit 到 `wp1-camera-aim`，未使用 stash |
>
> **③ 🔴 測項編號撞號（重要，會影響引用）**
> 兩側同輪都新增了「A23」：本機＝**AnimationKey 不得每帧配置**，遠端＝**Action 身分單一來源**。
> 本機那條已被 dev-spec §7.1 引用在先 ⇒ **遠端那條順延為 `A24_ActionIdentity_HasExactlyOneSourceOfTruth`**。
> `docs/02-dev-spec.md` §7.1 的表格與 `docs/11` 的引用皆已同步。**編號是引用鍵，不是排名。**
>
> **④ ✅ 編譯已通過（不是靜態閱讀——是真的跑編譯器）**
> 初版稽核只做了靜態閱讀，**漏掉一個真的編譯錯誤**：`StateMachineConfigSO` 缺 `using Project.Core.StateMachine.Actions;`
> ⇒ `CS0246: ActionDefinitionSO could not be found`。根因是 C# 的命名空間查找**只往父方向走、不搜尋子命名空間**，
> 而遠端分支只補了 `using Project.Core.Actions;`（`ActionSlot` 用的那個）。已修（`71c5de5`）。
>
> 📌 **方法升級（重要，往後都該這樣做）**：Unity 產生的 `.csproj` ＋ `dotnet build` 可以**不開 Editor 就編譯**：
> ```
> dotnet build Project.Runtime.csproj -p:ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true
> ```
> 該 flag 是繞過 `nunit.framework`（4.7.2）與 csproj（4.7.1）的參考解析失敗，**不是程式問題**；不加會噴一堆假的
> 「找不到 NUnit」。⚠️ `.csproj` 由 Unity 生成，新增檔案後要讓 Unity 重生一次才會納入編譯清單。
> **現況：`Project.Runtime`／`Project.Editor`／`Project.Tests.EditMode` 三個 assembly 皆 0 error。**
>
> **⑤ ✅ EditMode 全綠（使用者實跑，2026-09-02）**
> 過程抓到**三類問題**，全部已修：
> | # | 問題 | 性質 |
> |---|---|---|
> | 1 | `StateMachineConfigSO` 缺 `using Project.Core.StateMachine.Actions` ⇒ `CS0246` | 遠端分支從未編譯過。C# 命名空間查找**只往父方向走、不搜尋子命名空間** |
> | 2 | **`ActionState` 冷卻回歸**：`Complete()` 先 `ResetExecutionState()` 清掉 `_activeSlot`／`_definition`，`OnExit()` 才寫冷卻 ⇒ **自然播完的 Action 永遠不進冷卻，只有被中斷的才會** | 🔴 **真 bug**，由 T19 抓到。已抽出 `CommitCooldown()` 在兩個結束路徑各呼叫，靠 null 判定保證冪等 |
> | 3 | `ReleaseNormalizedTime_Zero`／T16／T17 直接呼叫 `OnEnter` 卻未表達 request | 測試對齊新 baseline（ADR-005 起 Definition 改為進入時依 slot 現查）。**斷言未放寬**，三條原始斷言一字未動 |
>
> **✅ A22 已轉綠**——它自 ADR-004 落地起一直是紅的（斷言 `IActionReleaseSink`，介面實名 `IActionLifecycleSink`）。
> ADR-004 §11 已補上結案列；ADR-005 §4 的 **D 與 F 已打勾**（F 由 A24 機器守衛）。
>
> **⑥ 命名統一（2026-09-02 完成，已驗證）**
> `ActionSlot` 改為「玩家觸發段依序編號（`Slot1/2/3`）＋ 保留段（`Reaction = 100`，非輸入驅動）」，
> **輸入層同步對齊**：`FireAction`／`SecondaryAction`／`TertiaryAction` → `Slot1Action`／`Slot2Action`／`Slot3Action`，
> `InputData` 的三顆 `*ButtonDown` 同理。原本同一排按鍵有**三種**命名法（語意名 ＋ 兩個拉丁序號），且都對不上 enum。
> ✅ **`[FormerlySerializedAs]` 遷移成功，左鍵／Q／E 三個 binding 全部保住，EditMode 全綠。**
> ⛔ 那三個 attribute 看起來像雜訊但**不得刪除**——欄位以名字序列化在 prefab，刪掉等於清空綁定。
>
> **⑦ ⛔ 仍未成立的事（不得視為完成）**
> - **EditMode 綠 ≠ 功能成立。** ADR-005 §4 的 **A／B／C／E 全部未打勾**：
>   A 缺資產接線 ＋ Play（程式面 T18–T21 已綠）／B 需真的加第三個 Action／C 需 Play 驗既有無回歸／E 需 Profiler。
> - Play 未驗；Profiler 零 GC **未量測**（熱路徑動過 `ProcessIntents`、`EvaluateInterrupts`，必須複驗）。
> - `ThirdPersonCamera.aimResolver` 在**場景實例**上仍是 `None`（WP1 的尾巴，見下面 2026-08-31 段⑤）。
>   跟 ADR-005 無關，但 Play 時會撞到。
>
> **⑧ ✅ Slow 最小切片已通過 Play（`8e7773a` 程式；`2f0a949` 接線）**
> **Acceptance G 成立**（使用者實測）：減速生效／到期自動恢復／重複命中不疊層，
> **且跨系統自動傳播**——敵人跑步動畫自己變走路、腳步聲自己變疏、停步選片自己降級，
> 五個下游檔案零修改（`A25` 守）。📌 **ADR-005 §4 原本只列到 F**，G 一直只存在於 `docs/11` §7.6 的引用中，
> 已於同輪補列進 ADR 並記錄通過。
> 🔧 順帶修掉一個**自合併起就存在的潛在回歸**：`DamageDefinition.asset` 沒有 `Slot` 欄位（比該欄位早存在）
> ⇒ 吃初始值 `Slot1`，但命中提交的是 `Reaction` ⇒ **敵人不會播 Damage 且不報錯**。已改為 `Reaction`，Play 確認。
> `Core/Effects/TemporaryGameplayEffectState`（單一 slot：tag／multiplier／expiresAt 分開存）
> ＋ `AIMovementSource` 結尾乘倍率 ＋ `ThrownProjectile` 投遞。語意見 `docs/11` §7.4（**「剩 30%」不是「降 30%」**）。
> ⛔ 未抽介面、未做 stacking／抗性／優先級／複合效果、未建 StatusEffect／GAS framework。**本輪也不得補做。**
> 🔧 本機稽核修正：原實作對**每顆**投射物都減速，但 `ThrownProjectile` 是三個技能共用元件 ⇒
> 改為 `[SerializeField] bool appliesSlow`（預設 false），只有 Ice Spell 的 prefab 勾。
>
> **⑨ 🟡 Architecture debt（使用者 2026-09-02 裁決：先跑 Play，之後再裁決要不要收斂）**
> `AIMovementSource` 目前**每幀直接讀效果元件**，與以下三處有張力：
> - dev-spec §2.5／line 448：producer **「不每幀回讀 gameplay state」**（ADR-003 D2 context-free）
> - dev-spec §7.3 既有張力列（2026-07-25 寫下）：「buff 是 gameplay state，producer 直接查詢＝context-free 破功…
>   可行方向是**『buff 寫黑板 status region，producer 讀資料』**」
> - `CLAUDE.md` 核心原則：「Gameplay reads data. Gameplay does not query other gameplay systems directly.」
>
> ⚠️ **A4 沒有擋下來**——它是 token 掃描，禁用清單裡沒有 `Project.Core.Effects`。**這是靜默通過，不是被批准。**
> 📌 **裁決**：先跑 Play 確認 Slow 的展示價值，**再**決定要不要收斂到 blackboard status region。
> **本輪不開新 ADR、不為了純度改 schema**（黑板 schema 變更是 CLAUDE.md 開 ADR 的判準①）。
> ⇒ **Play 通過後**才把這筆正式補進 dev-spec §7.3。本段是它在此之前的暫存處。
> revert 成本低——改動全是加法。
>
> **⑩ 下一步 —— 按「需要建多少資產」分層，不是按功能分**
>
> 📌 **先修正一個排序錯誤**：初版清單把三件成本天差地遠的事綁成一張表。
> 實際上**只有 Melee Slash 需要烘焙**，而 Slow **一個新資產都不用建**。
>
> **第 1 層 — Slow（零新資產、零烘焙、今天就能測）**
> 磁碟已備齊：`NavMesh-Navigation.asset`（已烘導航）、Y Bot 掛著 `AIMovementSource`＋`NavMeshAgent`＋
> `ActionRequestTarget`、`ThrownProjectile.prefab`、`ThrowDefinition.asset`，且 Throw 是 ADR-004 `Accepted` 已驗證可動。
> ⇒ **借用既有的 Throw 測 Slow**：
> 1. Y Bot 掛 `TemporaryGameplayEffectState` — 🔴 沒掛 ⇒ 倍率恆 1、Slow 全無效且**不報錯**
> 2. `ThrownProjectile.prefab` **暫時**勾 `Applies Slow` — ⚠️ Ice Spell prefab 做出來後要取消（§4：只有 Ice 減速）
> 3. Play：丟中 Y Bot → 速度**剩 30%**（不是降 30%）／連中兩次仍 0.3 不是 0.09／到期自動恢復
> 4. **Acceptance G**（本輪架構價值最高的一條）：敵人跑步動畫自己變走路、腳步聲自己變疏、停步選片自己換，
>    而 `LocomotionModel`／`LocomotionSpeedSmoother`／`LocomotionStopSelector`／`FootIKController`／`AudioController`
>    **五檔零修改**（`A25` 在守）。
>    📌 **這條鏈用的是敵人既有的 locomotion，與任何新動畫無關**——最便宜的一層剛好是價值最高的一層。
> 5. 通過後 ⇒ 裁決 ⑨ 的 debt（要不要收斂到 blackboard status region）
>
> **第 2 層 — 多 Action（要建 Definition，但不用烘焙）**
> `docs/11` §4 明列**兩個法術 `Bake` 留空**，用 `FallbackDuration` 即可跑通。
> 6. 兩份新 `ActionDefinitionSO`（Slot ＝ `Slot2`／`Slot3`），**冷卻設不同值**否則看不出 per-slot 獨立
> 7. **連同現有 `ThrowDefinition`（Slot ＝ `Slot1`）一起**填進 Config 的 `actionDefinitions`（**不是** `paramsMappings`）
>    🔴 `BuildActionSlotMap` 的退路是 **all-or-nothing** ⇒ 漏掉 Throw 它**安靜失效**（`docs/11` §10）
> 8. `AnimancerFacade` 的 Transition Mappings 補上新 `AnimationKey`（漏掉會噴紅字，不是靜默）
>    💡 想先驗機制的話，兩份 Definition 可暫時指向**既有** clip——per-slot 冷卻與互相打斷跟播什麼動畫無關
> 9. Play：獨立觸發／冷卻不連坐／互相打斷／既有 Idle・Move・Jump・Roll・Throw 無回歸 ⇒ 結掉 A／C
>
> **第 3 層 — Melee Slash motion mapping（⛔ 唯一需要烘焙的一項）**
> 10. FBX 匯入設定（`EEJANAI_Team/FreeSwordAnimations/FBX/slash1–9`）→ 烘 `MotionBakeData`
>     🔴 **兩道 gate 都安靜失敗**：`BakedDuration = 0` ⇒ `hasBake` false；Transition Mapping 沒接 ⇒ `IsPlaying` false。
>     兩者症狀都是「劍揮了人不動」，很容易誤判成烘焙壞掉。見 `docs/11` §4.1。
>     ⚠️ 重烘策略一直是「用到再烘」，目前只有 Roll 有 `BakedDuration`。
>
> **收尾**
> 11. EditMode 全套（新增 `SlowEffectTests` 6 條 ＋ `A25`）
> 12. Profiler 穩態 0 B/frame ⇒ 結掉 E
> 13. 全過之後 ADR-005 才可 `Trial → Accepted`
>
> ⚠️ 另有一筆與 ADR-005 無關但 Play 會撞到的：`ThirdPersonCamera.aimResolver` 在**場景實例**上仍是 `None`。
>
> ---

> ### 📍 2026-09-02 交接（歷史：合併前的本機交接）
>
> ### 🚚 2026-09-02 遠端 session 交接（歷史：遠端容器的交接，其結論已由上方合併段接手）
>
> **一句話**：遠端容器 session 產出 `ActionSlot` 多 Action 實作（分支 `claude/skill-system-showcase-6vqh6l`，5 個 commit），
> **但它一行都沒編譯過**，而且它是對著 `main@d5132e9` 的舊 clone 做的——**本機的 WP1 成果它完全不知道**。
>
> #### ① 環境事實（先理解這個，否則下面看不懂）
> - 該 session 跑在 **Claude Code on the web 的遠端容器**，容器內**沒有 Unity、沒有 C# 編譯器**。
> - 容器的 clone 停在 `main@d5132e9`，**看不到本機未 commit 的工作**。
> - ⇒ 該 session 的所有「程式分析」都可能對本機現況過時。**下方 ④ 列出已知過時的結論。**
>
> #### ② 分支上有什麼（`claude/skill-system-showcase-6vqh6l`，從 `main@d5132e9` 分出）
>
> | commit | 內容 |
> |---|---|
> | `95f5730` | ADR-005 草案 ＋ `docs/11-multi-action.md` |
> | `8dc8c87` | `CLAUDE.md` 新增 Remote Container Exception（純文件可由 AI commit） |
> | `b8c6da4` | **ADR-004 `Trial → Accepted`**（A–F 回填 ＋ §10.1 靜態稽核明細）；ADR-005 翻 `Trial` |
> | `6023e53` | **程式**：`ActionSlot` 身分、多 Definition、per-slot 冷卻、Action→Action 重入（15 個 `.cs`） |
> | `1c13753` | fold-back ＋ **撤回未經驗證的勾選** |
>
> #### ③ 驗證狀態（⚠️ 不要把任何一項當成已完成）
> - **ADR-004** ＝ `Accepted`。A／C／D／E 依使用者 Play 與實跑回報，B／F 為靜態稽核。
>   ⚠️ 但**同輪發現 A22 自 ADR-004 Trial 期起一直是紅的**（斷言 `IActionReleaseSink`，介面早已改名 `IActionLifecycleSink` 並擴為三方法）
>   ⇒ **ADR-004 的 D 當時建立在不成立的基礎上**。A22 斷言與 `docs/08` §2.7 已修，**但仍需重跑確認**。
> - **ADR-005** ＝ `Trial`。§4 **只有 F 打勾**（靜態稽核，不依賴編譯）；**A–E 全部待驗，且尚未編譯過**。
> - 📌 本輪出現**兩次「憑回報打勾」的失誤**（A22；以及一次跑在舊程式上的「EditMode 全綠」）。
>   **教訓已記入 `docs/11` §11.5：回填驗收前必須先確認「在哪個 checkout 上跑的」。**
>
> #### ④ 🔴 已知與本機現況衝突／過時的內容（**下一個會話必須處理**）
>
> | # | 問題 | 處置 |
> |---|---|---|
> | **H1** | **`docs/11` 撞號**：分支上是 `11-multi-action.md`，本機是 `09-camera-aim.md`（另有 `10-lock-on.md`） | 分支那份改名為 **`docs/11-multi-action.md`**，並更新 `00-map.md` 與 ADR-005 的交叉引用 |
> | **H2** | **`docs/11`(原09) §2.2 的 B4 已過時**：寫「`AimTarget` 是無 writer 的死欄位、沒有朝向／瞄準系統」 | 本機已有 `AimResolver.cs`。**依本機現況重寫 B4**，並確認 `AimTarget` 現在的 writer 是誰、要不要進 `WriterRules` |
> | **H3** | **程式衝突**：`BaseState.cs`（分支加 `CanReenter`）、`ArchitectureRegressionTests.cs`（分支修 A22 ＋ 加 A23） | 逐一手動合併。⛔ 不得為了解衝突刪掉任一方的不變量 |
> | **H4** | **文件衝突**：`WORKLOG.md`／`docs/00-map.md`／`docs/02-dev-spec.md`／`docs/ADR/004-action-in-fsm.md` 雙方都動過 | 多為加法，保留雙方內容 |
> | **H5** | 本機大量未 commit（prefab／場景／`ProjectSettings`／`TagManager`／Kevin Iglesias 動畫包／WP1 程式與文件） | **先 commit 到自己的分支再談合併**，不要 stash |
>
> #### ⑤ 建議順序
> 1. 本機工作先 commit（`git switch -c wp1-camera-aim && git add -A && git commit`）
> 2. **先單獨驗分支**：checkout → Unity 編譯 → EditMode（重點 **A22／A23／T18–T21**）
>    —— 目的是讓「編譯錯誤」與「合併衝突」不要混在一起 debug
> 3. 驗過再合併，處理 H1–H4
> 4. 合併後才做資產接線（Q／E ＋ 兩份 Definition 填 `actionDefinitions`）→ Play → Profiler 零 GC
>
> #### ⑥ 治理備註
> - 本輪程式 commit 走**一次性授權**。`CLAUDE.md` 的 Remote Container Exception **仍是純文件**，條文未改。
> - 在本機會話中，上述例外**完全不適用**——Git 全部由使用者執行。

> ### 📍 2026-09-02 補記 ②（歷史：ADR-005 第一輪落地）
>
> **同一角色現在能持有多份 `ActionDefinitionSO`，以 `ActionSlot` 為身分獨立觸發、獨立冷卻，共用同一顆 `ActionState`。B1／B2／B5（＝FU-2／FU-3／FU-1）一次解掉。**
> ⏳ **尚未編譯過**——本輪在遠端容器完成（容器內無 Unity），改動在分支 `claude/skill-system-showcase-6vqh6l`。
> ⚠️ 曾誤記為「EditMode 全綠」，但那次測試跑在**尚未拉取本分支**的本機專案上，驗的是舊程式，已撤回。
>
> **① 下一步（照順序）**
> 0. **先拉分支**：`git fetch origin && git checkout claude/skill-system-showcase-6vqh6l`（從 `main` 的 `d5132e9` 分出）→ Unity 重新編譯並為新檔 `Core/Actions/ActionSlot.cs` 生 `.meta` → **跑 EditMode，重點看 A22／A23／T18–T21**
> 1. **資產接線**：`.inputactions` 加 Q／E → 接 `PlayerInputSource.SecondaryAction`／`TertiaryAction`；兩份新 `ActionDefinitionSO`（`Slot` 設 `Secondary`／`Tertiary`）填進 `StateMachineConfig` 的 **`actionDefinitions`**（⚠️ 不是 `paramsMappings`）
> 2. **Play 驗證** ⇒ 回填 ADR-005 §4 的 **A／C**
> 3. **Profiler 零 GC** ⇒ 回填 **E**。⚠️ 熱路徑有改（`ProcessIntents`／`EvaluateInterrupts`），**必須複驗**
> 4. **B** 要等實際加第三個 Action 才算數（那就是 Melee）
>
> **② 實作推翻的假設（完整版在 `docs/11` §3.4）**
> - **`ActionSlot` 原本放錯層**：放在 `Core/StateMachine/Actions/` 會讓 Presentation 的 `ThrownProjectile` 踩到 `LayerRules` ⇒ 移到 **`Core/Actions/`**。**身分屬於跨層 seam 層，不屬於 FSM 層。**
> - **重入第一版有 priority 繞過**：就地 `TransitionTo` 會讓字典迭代順序決定結果、並繞過更高優先的狀態（Roll）⇒ 改為與其他候選走同一套 priority 比較。
> - **`OnEnter` 產生新耦合**（已接受）：Definition 不再於 `Initialize` 綁死 ⇒ `OnEnter` 必須重新解析 request。結構上成立，但這是 ADR-004 期沒有的。
>
> **③ 🐞 A22 自 ADR-004 Trial 期起一直是紅的**
> 它斷言 `IActionReleaseSink`，但介面早已改名 `IActionLifecycleSink` 並從 1 個方法擴為 3 個；斷言與 `docs/08` §2.7 都沒同步。已修。
> ⚠️ **這代表 ADR-004 §10 的 D 當時是在不成立的基礎上打勾的**（依據是口頭回報，非實跑）。
> **教訓：改名要一併 grep 測試與文件。** `docs` 的舊名不會自己壞給你看。
>
> **④ 治理**
> 本輪程式 commit 走**一次性授權**（`CLAUDE.md` 的 Remote Container Exception 仍是純文件，**條文未改**）。下次有 `.cs` 仍會停下來問。

> ### 📍 2026-09-02 補記 ①（P-0 結案）
>
> **ADR-004 `Trial → Accepted` 完成，ADR-005 `Proposed → Trial` 完成。下一個開工項目是 `docs/11` 的 P-A（identity 實作）。**
>
> - **A／C**（Play 跑通、既有四狀態無回歸）＝使用者 Play 驗收
> - **D／E**（EditMode 全綠、穩態 `0 B/frame`）＝使用者實跑
> - **B／F**（三權威一致、無第二套 authority）＝**靜態稽核**，明細寫在 **ADR-004 §10.1**
>
> **稽核順帶釐清兩件事，下一包會用到**：
> 1. **B 的正確讀法是「Action 子系統的動畫權威唯一」，不是專案全域只有一個播放點。** `LocomotionModel` 另有 `Play`／`PlayWithCallback`（Stop 選片），那是 **ADR-003 D4 授權**的 model 自驅動畫，早於 ADR-004 且正交。以後引用「動畫只由順序 5 播放」時要帶這個限定條件。
> 2. **兩處防禦性冗餘已登記**（ADR-004 §10.1）：release 雙重去重（`ActionState` ＋ emitter 各一）、`Cleanup()` 五路徑呼叫。**現在判定為冗餘而非 workaround**（時點權威仍單一、冪等、T15／T17 有覆蓋），但 **P-A 擴成多 Action 後要重新評估**——屆時它們會變成 per-action，冗餘可能滑向「兩個真相」。
>
> ⛔ **G4／G5／G7 仍未打勾**，它們不屬於 ADR-004 的停止線（G4＝Foot IK A/B 錄影、G7＝場景像關卡），與 P-A 無依賴關係。

> **一句話**：作品集方向重訂——**Throw 降級為 ADR-004 的驗收證據、不進影片**；主線改為 **Quick Spell ／ Ice Spell ／ Melee Slash 三技能 ＋ Slow effect**，並以 **ADR-005（Action Identity）** 承載。**唯一該立刻做的是 P-0：拿 Throw 現狀去過 ADR-004 Acceptance，零手感投入。**
>
> **① 本輪產出（純文件，未碰程式與資產）**
> - 🆕 `docs/ADR/005-multi-action-identity.md`（⚪ **Proposed**）——凍結 D1–D5 五條；**identity 表示法與容器形狀刻意不凍結**，候選比較在 §5、不凍結清單在 §8。
> - 🆕 `docs/11-multi-action.md`——ADR-005 的 Living Spec：現況盤點、三技能資產配置、Q／E 鍵位、冷卻 HUD、Slow、Targeting 降級版、測試計畫、檔案邊界。
> - `docs/00-map.md` 補上 `docs/08`／`docs/11` 指標（**`docs/08` 先前從未進地圖**）。
>
> **② ⚠️ 治理排序陷阱（最重要）**
> `CLAUDE.md`「同一時間只允許一個 Trial」＋ ADR-004 仍是 🟡 Trial ⇒ **ADR-005 現在不能是 Trial，只能是 `Proposed`**。
> 但 **ADR-004 §10 的 A–F 逐條檢查過，沒有任何一條牽涉手感**——它問的是「單一 authority 撐不撐得住多 phase 動作」。
> ⇒ **Throw 節奏慢、瞄準難用，對 Acceptance 完全無害。原封不動送驗收即可，這是解鎖新計畫的唯一合法路徑。**
>
> **③ 現在該做什麼**
> - ~~**P-0：ADR-004 Acceptance**~~（使用者側資產接線 ＋ Play ＋ §10 A–F 逐條回填）。停止線與清單見下方「🛑 當前輪次的停止 checkpoint」，**內容不變**。
> - P-0 結案後：ADR-005 翻牌 `Trial` → P-A（identity 實作）→ P-B／P-C／P-D 並行。順序表在 `docs/11` §11。
> - ⛔ **P-0 之前不得動任何 Action 程式**。
>
> **④ 2026-09-02 使用者裁決（已定案，不需再問）**
> - Throw：僅作 ADR-004 驗收證據，**不投手感、不進影片**，`docs/08` 一字不改。
> - `ActionSlot` 作為統一 identity 的**方向**採納；**具體容器／API 先不寫死**。
> - Slow 升為主要架構展示；**只有一個使用者，不建 StatusEffect framework**。
> - Camera／Aim **不砍**，降級為 supporting infrastructure——只做到三技能展示所需的 targeting／facing。
> - 技能鍵位：**Q ＝ Quick Spell、E ＝ Ice Spell**、滑鼠左鍵 ＝ Melee（P-0 結案前仍指向 Throw）。
> - 原 **WP1（鏡頭 ＋ Aim ＋ Throw 依 AimPoint）解散**——它整包的存在理由是救 Throw 手感，前提已消失。WP2／WP3 的內容併入 `docs/11` §11 的 P-A～P-F。
>
> **⑤ 本輪盤點出的關鍵事實（省下一次重讀）**
> - **B1**：一角色一份 Definition，根因在 `StateMachineConfigSO` 四張表**全以 `StateType` 為鍵**（不是 `ActionState` 偷懶）。
> - **B3**：**完全沒有 hit／damage／effect 系統**——`ThrownProjectile` 命中後唯一動作是 `target.RequestAction()`。Slow 是本輪唯一「真的新東西」。
> - **B4**：`PlayerRuntimeData.AimTarget` 是**死欄位**，全 repo 只有除錯面板讀它、**無任何 writer**。已登記為 FU-09-1，本輪必須處置（A5 破口）。
> - **可重用面比預期大**：`IActionLifecycleSink`（Begin／Release／Cleanup）撐得住法術發射、近戰 hitbox、既有投擲三種側效果；動畫映射是字串鍵查表，加動畫＝Inspector 加一列。**三技能的新程式只有兩個 sink 實作。**

> ### 📍 2026-08-31 交接（前一輪，Foot IK 相關仍有效）
> ### 🎬 2026-08-31（深夜）— **GC 回歸已修 ＋ WP1 程式已交付，全部待使用者驗證**（最新，請先讀這段）
>
> **一句話**：ADR-004 的 E（零 GC）根因已定位並修好、WP1 程式已由 Codex 交付，
> 但**兩者都還沒有在 Unity 裡跑過**——`.meta` 未生成、EditMode 未執行、Play 未驗。
>
> **① ADR-004 §10-E 的 GC 回歸（已修）**
> - **根因**：`BaseState.AnimationKey => Type.ToString()`。`Enum.ToString()` 每次呼叫都**裝箱＋配置字串**。
>   ADR-004（`c26c72d`）把順序 5 由「比較 `StateType`」改成「比較 `AnimationKey` 字串」以支援多 phase Action，
>   使該屬性由**每次轉場讀一次**變成**每帧讀一次** ⇒ 每角色 40 B，玩家＋敵人 = **80 B/frame**（Profiler 實測數字完全對得上：GC.Alloc Calls 4 ＝ 2 角色 × 2 次配置）。
> - **為什麼 v0.24 曾實測 0 B/frame**：當時 `AnimationKey` 只在轉場時被讀，配置量在穩態看不見。
> - **修法**：`BaseState` 快取一次（`Type` 對每個具體 state 是常數）。`ActionState` 早已 override 成自己的 phase 快取，不受影響。
> - **⚖️ 不影響 §10-F**：順序 5 輪詢動畫鍵是 D5 的設計要求，不是 workaround，也沒有第二套 authority。**設計不需退回。**
> - **新增架構不變量 A23**（`[Test]` **17 → 18**）：斷言連續兩次讀 `AnimationKey` 回傳**同一個 string 實例**。
>   刻意不掃 `.ToString()` 字面——那會過度擬合寫法；識別性直接描述要的性質。
>   📌 這是 **A3 能力邊界的第二個實例**（第一個是介面型 `foreach` 裝箱），已記入 dev-spec §7.1。
> - ⚠️ **E 的驗證狀態（據實記錄，不得寫成量測通過）**：使用者裁決**不再複測** ⇒ E ＝「已定位並修正 ＋ A23 自動守住」，
>   **不是**「重新量測通過」。dev-spec §7.4 的 Development Build 量測**尚未重跑**。已同步記入 ADR-004 §11。
>
> **② WP1 程式（Codex 已交付，`threadId 01a05468`）**
> - 新增 `Presentation/Camera/AimResolver.cs`、`_Project/Tests/EditMode/CameraAimTests.cs`（8 條測項 T-1～T-8）
> - 修改 `ThirdPersonCamera.cs`（移除 `LookAt` ＋ pivot 公轉 ＋ 取景 blend ＋ 防穿牆）、`ThrowProjectileEmitter.cs`（依 AimPoint 發射 ＋ 退化路徑）
> - **已獨立稽核通過**（不採信自述）：禁區零觸碰；`LookAt` 實呼叫已移除；`ThrownProjectile.cs` 未動；
>   無 LINQ；`transform.rotation` 單一寫入且碰撞只改 position（§7.3.2 第一條紅線成立）。
> - 🔴 **Codex 明確聲明未實跑測試**（它被禁止開 Unity）⇒ **「交回時全綠」尚未成立。**
>
> **③ 相機取景已定案（2026-08-31 實機調定，程式／prefab／規格三方同步）**
> `pivotHeight = 1.5`、`offset = (0.44, **−0.39**, −1.83)`、`aimOffset = (0.5, 0.42, −2)`、`mouseSensitivity = 0.1`（由舊值 2 修正）。
> 換算：**y 為負是刻意的** ⇒ 角色在畫面中心**上方 40%**（`atan(0.39/1.83)=12.0°`）、水平 13.5° 左（右肩過肩）、相機高 1.11 m、距離 1.83 m。
> 公式與失敗對照見 [`docs/09` §6.2](09-camera-aim.md)。
> 📌 過程中修掉的**規格錯誤（全部是我的，不是實作偏離）**：
> ①`offset` 語意在 D1 之後改變但沒說要遷移舊值（舊值 ⇒ 角色貼齊畫面下緣，看起來像相機壞掉）；
> ②**「側向 offset 會影響移動基底」的警告是錯的**——`rotation = Euler(pitch, yaw, 0)` 與 offset 無關，
> E3 只是**一次性的遷移論證**、不是持續約束 ⇒ §3-E1／E3、§4-D1、§9-T4、§10.1 T-3、§11.2、§12.1-R3 全部已改寫。
> 🔴 **教訓**：我連續三輪改程式卻漏改規格表格，全部由 **Codex 的停止條件擋下**（它三次拒絕開工並指出矛盾）。
> **改程式與改規格要在同一次動作內完成**，不要分兩趟。
>
> **④ Codex 複驗（thread `01a05e15`，2026-08-31）**
> C1–C10、防穿牆四紅線、AimPoint／soft target（確認選**角度最小**非最近）、Throw 三條退化路徑、
> T1–T6、§2.3.2 禁止項 —— **全部靜態符合**。唯一違反＝**測項 T-3 仍在測作廢判準**，已修；Runtime 三檔未動。
> 編譯面靜態檢查通過（`InternalsVisibleTo("Project.Tests.EditMode")` 已由我確認存在、asmdef 引用齊全）。
>
> **⑤ 接線現況（2026-08-31 由 prefab 實查，非截圖推測）**
> | 項目 | 狀態 |
> | --- | --- |
> | Layer 表：`6 = Player`(X Bot)／`7 = Enemy`(Y Bot) | — |
> | `obstructionMask` = Default | ✅ 排除 Player |
> | `aimRayMask` = Default + Enemy | ✅ **排除 Player**（最隱蔽的那個陷阱已避開） |
> | `softTargetMask` = Enemy | ✅ |
> | `AimResolver` 元件（掛在 X Bot）＋ `aimAction` = RMB | ✅ |
> | X Bot／Y Bot 皆在場景根層互為兄弟 | ✅ `transform.root` 不同 ⇒ 自我排除正確 |
> | 🔴 **`ThirdPersonCamera.aimResolver`** | ❌ **仍 None**。跨 prefab 引用 ⇒ **必須在場景實例上接**（同 `target` 既有的 override 作法），prefab 資產內拖不到 |
> | 🔴 **`ThrowProjectileEmitter.aimResolver`** | ❌ **仍 None**。與 `AimResolver` 同在 X Bot ⇒ **可直接在 Prefab 編輯模式拖** |
> | `AimResolver.sourceCamera` | ⚠️ None ⇒ 退回 `Camera.main`（有 MainCamera tag，會動），建議明確指定 |
>
> ⇒ **瞄準路徑目前完全沒啟動過**；`aimOffset` 從未被真正看過（§12.1-R3 的 24° 垂直擺動風險尚未驗證）。
>
> **⑥ 下一步（全部在使用者側，依序）**
> 1. **接上兩個 `aimResolver` 引用**（見⑤，兩者作法不同）。
> 2. 跑 EditMode：**應為 18 ＋ 8 = 26 條全綠**。任一條紅 ⇒ 回報，不要自行繞過。
> 3. 依 [`docs/09` §10.4](09-camera-aim.md) 跑**八步驗收操作鏈**。
> ⚠️ Codex 預測最可能先撞到：①兩個 `aimResolver` 未接完整；②探索／瞄準 `offset.y` 異號造成 24° 垂直擺動；
> ③ **P4**（D3 的強制觀察項，明顯違和即**直接升級 D3(c)**，已預先授權）。
>
> **① 已完成**
> - `docs/09-camera-aim.md`（WP1 規格，🟢 **已定案**）＋ `docs/00-map.md` 登記一行。
> - **確認不需要 ADR**：四條判準逐條 ❌（§12.3）。本包走 Living Doc 分卷，不開 ADR、不進 Trial。
> - **D1–D5 五個裁決點全部拍板**（使用者，見下表）。
> - **新增 §2.3「WP1 鏡頭完成線」**：C1–C10 的 DoD ＋ 使用者指定的 10 條明確不做 ＋ **驗收操作鏈**（§10.4）。
>
> **② 裁決結果（使用者 2026-08-31）**
> | # | 題目 | 裁決 |
> | --- | --- | --- |
> | **D1** | 相機旋轉單一權威 | ✅ 採納 (a)：移除 `LookAt` ＋ pivot 公轉 |
> | **D2** | AimPoint 住哪裡 | ✅ 採納 (a)：Presentation 私有，**不進黑板**，先不抽介面 |
> | **D3** 🔴 | 瞄準時角色轉不轉身 | ✅ 先採 (a)，**列為強制 Play 驗收項**；明顯違和**直接升級 (c)**（已預先授權，不必再問） |
> | **D4** | soft target 形狀 | ✅ 採納 (a) |
> | **D5** | Cinemachine | ✅ **WP1 不使用** |
>
> **③ scope 的兩處變動（重要，會與舊文字打架）**
> - 🔄 **相機防穿牆：Non-goal → In Scope**（`docs/09` S7／C9／§7.3）。本檔的 polish 桶已由 5 項改為 **4** 項。
>   範圍嚴格限縮：**只拉近、不轉向、不找路、不淡出**（四條紅線在 §7.3.2）。
> - 🆕 **Lock-on 切成後續獨立 Gameplay 工作包**（FU-13）——它同時動 Camera ＋ Facing ＋ Movement Basis ＋ Action Targeting 四個系統，
>   塞進 WP1 會讓本包的架構命題直接失效。
>
> **④ ⛔ scope 已鎖**：使用者明確指示**不得因「作品集品質」自行追加**。跑完 §10.4 的八步操作鏈即視為 WP1 Camera 部分完成。
>
> **⑤ 三個從程式挖出來、決定本包形狀的事實**
> - **FU-5 的修法可證明是行為中性的**：`offset.x == 0`，而 `MotionDriver` 與 `AIMovementSource` **兩個消費者都把 `camera.forward` 壓平**，壓平後 `LookAt` 版與 `Euler(pitch,yaw,0)` 版**水平分量完全相同**。相機 pitch 目前**零消費者**。（已寫成 EditMode 測項 T-3，讓這個前提會失敗）
> - 🆕 **FU-11**：相機是**第二個輸入權威**——`InputData.LookInput` 有採樣**無消費者**，相機直接讀 `Mouse.current.delta`，繞過 `BlockInput` 閘門。**本包不修**：`InputData` 是 `ref struct`、當帧銷毀，相機在 LateUpdate 拿不到 ⇒ 接回管線只能經黑板 ⇒ 撞停止條件①。
> - 🆕 **FU-12**：`PlayerRuntimeData.AimTarget` 是**死欄位**（無寫入者、無讀取者）。**刻意不使用、也不順手清理**——清理會動 schema，污染本包「零改動」的證明。
>
> **⑥ 停止線：已核對，仍未滿足 ⇒ 不派 Codex**
> 2026-08-31 實查：ADR-004 §10 的 **A–F 六項全部未勾**、**G1–G7 全部未勾**、ADR 狀態欄仍為 🟡 `Trial`。
> ⇒ WP1 停在「文件先行」這一步（規矩：**規格先落地並 commit，Codex 才開始寫程式**）。
> **下一個會話的判斷式**：先看 ADR-004 §10 是否已回填 ＋ G1／G2／G3／G6 是否打勾——
> **滿足才依既有流程派 Codex**（交付邊界見 [`docs/09` §11](09-camera-aim.md)）；**未滿足就不要越線**。
> ⚠️ 兩者仍**不互相 gate**：ADR-004 的 Play 驗收是使用者側工作，可與本規格的 review 平行進行。
>
> ---

> ### 📍 2026-08-31 交接（前一段，Foot IK 與 scope 收斂）
>
> **一句話**：Foot IK 軌 A 已結案；主線仍卡在 **ADR-004 的 Unity 資產側與 Play 驗收**（使用者側），程式面沒有待辦。
>
> **① 本輪完成了什麼**
> - **Foot IK L1 → Level 1 rigid sole approximation，視覺驗收暫時通過**（`MaxFootAlignAngle = 23°`）。經歷四版迭代（取較高者 → 泰勒斯＋殘差 → 踝角夾限 → 真實端點 residual），順帶修掉 M3.3 就存在的腳踝水平漂移舊 bug。**durable 紀錄在 `docs/05` §3.5.5**，`docs/03` §1.3 的 L1 已結案、殘餘誤差降級為 L7。
> - **Scope 收斂**：軌 A ＋ WP1–WP4 四個工作包、九段影片對照表、Codex 交辦邊界、git commit 切點 —— 全在下一節「Scope 收斂與後續四包」。
> - FU-1／FU-2／FU-3（Action→Action 中斷不可能／一角色一份 Definition／mailbox 無身分）已寫入 `docs/08` §11.1；ADR-004 §8 補 **L4**。
> - 🆕 **治理條文**：`CLAUDE.md`「Documents Live in the Repo, Not in a Session」——技術文件／架構圖／研究筆記／HTML artifact **一律先寫進 `docs/`**（HTML 類放 `docs/artifacts/`），repo 版為 source of truth，Artifact 只是發布副本，兩者必須同步；⛔ 不得只留在 scratchpad。使用者已授權此類 `docs/` 寫入。首個產物＝`docs/artifacts/foot-ik.html`（Foot IK 圖解導覽）。
>
> **② 現在該做什麼**
> - **不要動 Foot IK**。重開條件見 `docs/05` §3.5.5（五條，任一成立才重開）。繼續調之前**必須先做 FU-IK-2 可視化**。
> - **主線的停止線沒有變**：ADR-004 `Trial → Accepted`。等使用者完成資產接線與 Play 驗收，程式面無事可做。
> - ~~使用者說要開 **WP1（鏡頭 ＋ Aim ＋ Throw 依 AimPoint）** 時：先出 `docs/09-camera-aim.md` 規格，再派 Codex。~~
>   ✅ **已執行（2026-08-31 晚）**：規格已落地於 [`docs/09-camera-aim.md`](09-camera-aim.md)，現卡在五個裁決點——見本段最上方的 WP1 區塊。**WP1 不開 ADR** 已由 §12.3 逐條確認。
>
> **③ 環境與工具（本輪新增）**
> - **Codex 可直接呼叫**：`.mcp.json` 已設定（`cmd /c codex mcp-server`，`sandbox_mode=workspace-write`／`approval_policy=never`），工具為 `mcp__codex__codex` ／ `mcp__codex__codex-reply`。⚠️ **thread 會過期**（實測掉過一次），過期就開新 session 並補完整脈絡。
> - **Codex 沙箱與批准權在它那側**，MCP 層無法強制紀律 ⇒ 「不碰 git、不碰資產、檔案白名單」**必須每次寫進 prompt**，光靠 `AGENTS.md` 不夠。
> - 使用者**已授權**直接調用 Codex。
>
> **④ 工作樹狀態（2026-08-31 核對：`git status` 為 clean；git 全由使用者執行）**
> - ✅ **本輪全部產出已由使用者 commit**：Foot IK v4、ADR-004 Trial 的 P1／P2 程式與資產、Free Sword Animations 素材、本輪全部文件——工作樹已無未暫存／未追蹤變更。
> - **實際落點與原規劃不同**，落在兩筆：`c26c72d`（素材＋程式＋Unity 資產＋多數文件，合流成一筆）與 `d5132e9`（治理條文 ＋ `docs/artifacts/foot-ik.html`）。逐項對照見下方「🌱 Git commit 切點」A 段。
> - ⚠️ **commit 完成 ≠ 驗收完成**。ADR-004 仍是 `Trial`；停止線四項尚未全數成立（見上方「🛑 當前輪次的停止 checkpoint」）。**資產已進 repo 不等於接線正確**——第 1 項仍要由 Play 驗收判定。
>
> **⑤ 這輪學到的、值得下一個會話沿用的做法**
> - **先形式化約束模型再改程式**。Foot IK 繞了四版，前三版都是因為沒把「誰決定位置／誰決定旋轉／誰決定接觸」講清楚就動手。
> - **要求可否證的預測**：改動前先算出「若這個假設成立，你會看到 X cm 的偏差」，Play 才是測試而不是觀感投票。
> - **觀測條件 ≠ 交付條件**。Scene 視窗貼著腳看到的瑕疵，在 3–4m 鏡頭下不存在。判斷是否值得修時先問這個。

> ⬆️ **最新狀態（2026-08-29）＝優先順序已改變：「完善作品集」升為最高優先，高於 `docs/03-animation-roadmap.md` 的工程路線圖。**
> 進行中的工作＝下方「作品集最低限度衝刺」。Phase C1／C1.1（Walk＋Run Stop）已完成驗收並歸檔；`docs/08-skill-system.md` 設計稿已完成但**展示題材已改**（Punch → Throw，理由見下）。
> 📍 **會話開場請先讀 `docs/00-map.md`**（單頁索引：模組 → 檔案 → 治理章節），再讀本段。
> 📍 敘事與展示策略見 `LearningNotes/portfolio-framing.md`（不納入 `docs/00–NN` 工程編號）。
> 🆕 **2026-08-30 scope 收斂**：見下一節「Scope 收斂與後續四包」。**當前輪次的唯一任務是讓 ADR-004 拿到 `Accepted`**；
> 該節同時載明軌 A（可立即並行）、WP1–WP4、影片段落對照、Codex 交辦邊界與 git commit 切點。
> 原「作品集最低限度衝刺」的 P1／P2 仍是現行工作；**P3／P4／P5 已被拆進 WP2／WP3／WP4 與軌 A**（見該表下方註記）。

---

## 🧭 Scope 收斂與後續四包（2026-08-30 planning review）

> **為什麼有這一節**：實作過程持續暴露新問題，出現「發現一個問題就順手解下一個」的漂移。本節把問題分桶、
> 把後續切成**可獨立 qualification** 的工作包，並明確寫下**停止線**。
> **兩條評估軸並用**（缺一不可）：
> - **🏗 Architecture Qualification**：這包證明什麼技術能力？驗收＝測試／不變量／零 GC／「加第三個 X 是零程式」。
> - **🎬 Portfolio Qualification**：觀眾實際看到什麼？沒有它，影片缺哪一段？驗收＝錄影段落／外行能否複述。
>
> ⚠️ **分類規則**：一個項目只要在**任一條軸**上是必要的，就**不得**進 polish 桶。
> 只有兩條軸都只是加分的才是 polish（現存 **4** 項：震屏、hit stop、aim friction 微調、projectile 物件池）。
> 🔄 **2026-08-31 重新分類**：**相機碰撞避讓（防穿牆）已移出 polish 桶，進入 WP1 scope**（使用者裁決）。
> 理由正是本節的分類規則——它在 🎬 軸上是**必要**的：穿牆是外行一眼看得到的破綻，且它是「自由第三人稱視角基礎」與
> 「一顆會跟隨的攝影機」之間的分界線。原分類是誤判。範圍限縮為「只拉近、不轉向、不找路」，見 [`docs/09` §7.3](09-camera-aim.md)。

### 🎬 展示規格（取代舊的「15–20 秒 demo」）

- **主展示：約 1:45–2:00**，重點是**讓觀眾有時間理解「這套系統在實際遊戲中帶來什麼」**，不是塞更多功能。
- **Teaser：15–30 秒 ＝ 主展示的剪輯輸出**（段落 2／3／6／8 各取數秒），**不是額外工作包、不需要新內容**。

| # | 段落 | 秒數 | 交付者 | 沒有它，觀眾看不到 |
| --- | --- | --- | --- | --- |
| 1 | 探索移動（地形／tier／Stop 選片／腳步音／Foot IK／探索鏡頭） | 0:00–0:15 | **軌 A ＋ WP1** | 專案最厚的既有工作。這是唯一能把「隱形的正確」變可見的機會 |
| 2 | 鏡頭切換／瞄準 | 0:15–0:25 | **WP1** | 「這是個有戰鬥的遊戲」的第一個訊號 |
| 3 | 遠程 Throw ＋ soft target | 0:25–0:35 | **WP1** | 球會飛 ≠ 會瞄；缺這段投擲看起來像亂射 |
| 4 | 敵人接近 → Telegraph → 近戰出手 | 0:35–0:52 | **WP3** | 敵人是威脅而不是靶子 |
| 5 | 玩家閃避 | 0:52–0:58 | 既有 Roll ＋ WP3 | Roll 早就做完了，但沒有攻擊可閃時它只是翻滾動畫 |
| 6 | 玩家近戰揮劍 | 0:58–1:10 | **WP2** | **遠程／近戰對比**；缺它整個 demo 就是 projectile prototype |
| 7 | Action Mapping／不同角色動作 ＋ 改 SO 即改行為 | 1:10–1:30 | **WP2** | 架構價值唯一能被**看見**的一段（全片最重要的 20 秒） |
| 8 | 受擊與中斷（Throw 斷 Telegraph／揮劍被打斷） | 1:30–1:45 | **WP2 機制 ＋ WP3 Play** | 雙向互動成立 |
| 9 | 遭遇結束 | 1:45–2:00 | **WP3 ＋ WP4** | 有結局的遭遇 vs 沒剪完的錄影 |

### 🛑 ~~當前輪次的停止 checkpoint~~ —— ✅ **2026-09-02 已到線，結案**

> ✅ **ADR-004 已於 2026-09-02 改為 `Accepted`**（A–F 逐條回填見 ADR-004 §10，F 的靜態稽核明細見 §10.1）。
> **ADR-005 同日翻牌為 `Trial`，下一個開工項目是 `docs/11` 的 P-A。**

~~**停在 ADR-004 `Trial → Accepted` 的那一刻。** 全部成立才算到線，一件不多：~~（以下為歷史紀錄）

1. 資產與接線完成（Throw／Damage 的 transition mappings、Bake、兩份 Definition、Config 的 Action rules、`ThrownProjectile` trigger collider、敵人 prefab、NavMesh 烘焙）。
   - 📌 **2026-08-31 現況**：上述資產**檔案本身已進 repo**（`c26c72d`，含 `ThrowDefinition`／`DamageDefinition`／`EnemyStateMachineConfig`／`ThrownProjectile.prefab`／`Y Bot.prefab`／`NavMesh-Navigation.asset`／Throw 與 Damage 的 Bake 與動畫資產）。**但「檔案存在」不等於「接線正確」**——本項仍未打勾，由 Play 驗收判定。
2. ⚠️ **`playerCamera` 欄位或 MainCamera tag 必須確認存在**——`AIMovementSource` 在 `data.CameraTransform == null` 時**直接 return**，症狀是「敵人靜止不動」且**沒有任何錯誤訊息**。這條放進驗收清單，不要現場 debug。
3. G1／G2／G3／G6 打勾。
4. ADR-004 §10 的 **A–F 逐條回填**，特別是 **F（實作沒有逼出第二套 authority 或明顯 workaround）**。

**停止線之後、WP1 開工之前不做**：任何相機改動、任何 aim、任何第二份 Definition、任何敵人攻擊。
**新發現一律只登記不處理** → FU-1／FU-2／FU-3 已寫入 [`docs/08` §11.1](08-skill-system.md)；FU-4～FU-10 見下方登記表。

### 🅰️ 並行軌 A（舞台）——**現在就能開始**，不佔 sequential 順位

不 gate 也不被 gate，且大部分是使用者側資產工作，可與 ADR-004 收尾同時進行。
**做它的理由**：段落 1 的唯一來源；而且 P1／P2 的 Play 驗收在有斜坡／樓梯的場地上做，比在空地上做有意義得多。

- **🏗** 無（唯一程式項＝Foot IK L1 Heel/Toe 雙點採樣，**只准動 `SampleGround`／`ResolveFoot` 內部＋Settings**）。
- **🎬** G7 場景像關卡、G4 Foot IK 可 A/B。**沒有斜坡與樓梯，Foot IK 整塊工作在影片裡等於不存在。**
- **Scope**：斜坡／樓梯／障礙／一個明確目標點；Foot IK L5 調參；Foot IK L1 雙點採樣。
- **Non-goals**：關卡美術、光照打磨、導航以外的互動物件。
- 🛑 **停止條件**：Foot IK L1 需要動到 `SampleGround`／`ResolveFoot` 以外的任何東西 ⇒ 停並回報（`docs/05` §3.5.1／A11 邊界）。

#### 🎫 Ticket 軌A-IK-L1：Heel/Toe 雙點採樣 —— ✅ **結案（2026-08-31）**

> **結論：Level 1 rigid sole approximation，視覺驗收暫時通過。本輪 Foot IK 停在此狀態，不再繼續修。**
>
> - `MaxFootAlignAngle` **暫定 23°**（原 15°，調整後視覺明顯自然）
> - toe-up 姿態**有保留**
> - 嚴重穿模**已消失**
> - 剩餘 heel／sole 浮空**不明顯**（近距離側視才可見），**低於 must-fix 門檻**
> - Walk／Run 若無明顯跳動即視為 Level 1 驗收通過
> - ⛔ **不再為了追 0 浮空繼續提高 clamp 或擴大模型**
>
> 📄 **durable 紀錄已落到 [`docs/05` §3.5.5](05-foot-ik.md)**：約束模型三層分工、三次推翻的教訓、Level 1 已知限制、
> Level 1／2／3 升級階梯、FU-IK-1～3 follow-up、以及**重開 Foot IK 的五條條件**。
> `docs/03` §1.3 的 **L1 已結案**，殘餘誤差降級為新增的 **L7（設計接受）**。
>
> **後續待辦（follow-up，不現在實作）**：**FU-IK-1** 量準 `HeelOffset`／`ToeOffset`（現值非正式量測值；它們是**腳底幾何常數，不是手感旋鈕**）／
> **FU-IK-2** 最小 Foot contact 可視化（Gizmo ／ `Debug.DrawRay`，⛔ 不建 Debug Framework）／
> **FU-IK-3** 有可視化後重新量測並**只准再調一次** clamp。⚠️ **繼續調 Foot IK 之前必須先做 FU-IK-2，不要再靠 Scene 視窗肉眼猜。**
>
> ⏭️ **軌 A 的 Foot IK 部分到此結束；回主線 WP1（鏡頭 ＋ Aim ＋ Throw 依 AimPoint）。**

**問題**（`docs/03` §1.3-L1）：ray 只打腳踝下方、命中該踏面；腳掌前段（~25 cm）跨入上一階體積時系統無從得知 ⇒ 階梯上腳掌中段穿入上一階。**單點採樣的資訊量天花板**，不是參數沒調好。

**不可違反（設計哲學鐵律，優先於本 ticket 的一切目標）**
- **Natural Pose > Terrain Adaptation > Perfect Foot Contact。接受少量腳尖穿模，不接受為修穿模讓動作僵硬。**
- ⛔ **不得**用 Fade／Gate／降權重解決——貼地品質一律走 **Ground Sampling 升級**。
- 檢核問句必須答「否」：**這個機制會不會縮小角色原本的活動空間（抬腿／跨步／轉向）？**

**做法（建議；細節屬實作自由）**
1. `SampleGround` 由 1 條 ray 改為 **2 條**：heel＝`posePosition - footForward * HeelOffset`、toe＝`posePosition + footForward * ToeOffset`，`footForward` 取 `poseRotation * Vector3.forward` 水平化。
2. **`FootSample` 的對外形狀盡量不變**——`GroundY` 取**兩點中較高者**（防穿模）。如此 `ResolveFoot` 的目標式與 `ComputePelvisOffset` 的骨盆邏輯**零改動**。
   - 🔧 **2026-08-30 實作裁決（原文有歧義，已定案）**：`HitPoint` **只取較高命中的 Y 與法線，XZ 保留動畫 pose goal**。照搬較高命中的完整 XZ 會把腳踝水平拉向 heel 或 toe，**平地就會違反「逐字不變」**，也違反 Natural Pose 優先。
3. **旋轉：完全不動，逐字保留 M3.1 的 `FromToRotation(Vector3.up, sample.Normal) * poseRotation`。**
   - 🔴 **2026-08-30 Play 驗收後的設計更正（原文第 3 點「由兩點高度差求 pitch」已作廢）**：
     實測樓梯上整隻腳被扳斜近 40°。根因是 heel 與 toe 打在**兩個不連續的平面**（上階／下階踏面），
     程式把高差當成坡度：`span = 0.25m`、踢面 `0.2m` ⇒ `atan2(0.2, 0.25) ≈ 39°`。
     **兩個踏面各自都是平的、法線都是 up，卻被合成出一個不存在的斜面。**
   - 更根本的問題：**連續斜面上 pitch 早已由命中法線提供**（`FromToRotation(up, normal)` 就是在做這件事），
     heel/toe 高差只是把同一資訊算第二遍；而在階梯上它算的根本不是坡度。
     ⇒ **pitch 這個來源在兩種地形上，一種多餘、一種錯誤。**
   - ✅ **裁決（使用者，2026-08-30）：雙點採樣只決定「高度」，不決定「旋轉」。**
     `GroundY`／`HitPoint.Y`／`Normal` 取**較高命中**——**穿模由「把腳抬到較高的面」解決，不由旋轉解決**。
     這與既有骨盆規則同一哲學：**地面只能把腳往上頂，不能把腳往下拉**（`ComputePelvisOffset` 的「只下沉不上頂」）。
   - 📌 **代價（已接受）**：上樓梯時腳尖不會主動翹起去貼上一階；腳尖懸空時保持平貼較高踏面。
     依鐵律 **Natural Pose > Terrain > Contact**，這正是預期行為，不是缺陷。
   - 📌 **本輪 L1 沒有 EditMode 覆蓋**（取兩者較高＝`Mathf.Max`，不值得為了「有東西可測」而抽純函數）。
     驗證誠實地落在 Play。
4. **退化路徑**：任一 ray 落空 ⇒ **退回現行單點行為**。⛔ 不得因落空而關閉 IK 或降權重（那就是被禁的 Gate）。
5. `FootIKSettings` 新增 `HeelOffset`／`ToeOffset`（公尺）＋一顆 `UseTwoPointSampling` 布林。**該布林只用於 A/B 展示與退回基線，不得成為執行期的品質開關。**

> 🔄 **以上 1–5 為初版構想，已被實作推翻兩次。以下是現行設計（v3，🟡 待 Play 驗證）。**

#### ~~現行設計 v3（2026-08-30，三輪迭代後）~~ —— 已被 v4 取代，僅存演進紀錄

> 🔄 **v4（2026-08-31）取代 v3 的 residual**：v3 仍以「地面 vs 假想平面」求穿透，算式裡沒有腳的幾何，
> 因此任何動畫 pitch 對該約束都是**隱形**的（平地 ＋ toe-up 20° 時腳跟穿地 2.8cm 而 residual 恆回 0）。
> v4 改為由最終旋轉 `R` 算出 heel／toe **真實端點世界座標**、在該處打 ray、點對點比較。
> **完整的現行設計見 [`docs/05` §3.5.5](05-foot-ik.md)**；以下保留 v3 內容僅為演進脈絡。

**演進**：v1「雙點取較高者當高度」→ 斜坡浮空（誤差＝上坡側取樣距離×tanθ，30° 約 8.7cm，且隨朝向擺動）
→ v2「泰勒斯 ＋ 戳穿殘差」→ 幾何正確但**強制整面貼地**，不像真人
→ **v3「＋ 踝關節角度夾限」**。

| # | 機制 | 作用 |
| --- | --- | --- |
| **① 泰勒斯修正** | `ComputeAnkleTarget(rayStart, hitPoint, soleNormal, footBottomHeight)` | 腳踝抬升後仍落在**原本那條垂直 ray 上**，保住動畫 XZ。移植自 [ozz-animation `foot_ik`](https://guillaumeblanc.github.io/ozz-animation/samples/foot_ik/)，它明文點名舊寫法之誤：*"ankle position cannot be simply be offseted by foot offset"*。⚠️ **順帶修掉一個 M3.3 就存在的舊 bug**：`hit + n·fbh` 在 30° 坡會讓腳踝水平漂移 5cm 且坐得太低 |
| **② 戳穿殘差抬升** | `ComputePenetrationLift(...)` → `Max(0, 各取樣點戳穿量)` | 防穿模。**連續平面上恆等於 0**（腳底已與平面平行）⇒ 不需要「這是斜坡還是台階」的判別式——而那正是最容易寫成被禁 Gate 的地方。只抬不壓，同 `ComputePelvisOffset` 的「只下沉不上頂」哲學 |
| **③ 踝關節角度夾限** | `ClampGroundNormal(hitNormal, MaxFootAlignAngle)`，預設 **15°** | **腳底不強制整面貼地**（設計哲學明文）。超過夾限時腳保持較自然姿勢，②自動把腳抬到上坡側接觸、下坡側浮空＝真人行為。浮空高度 ≈ `span × tan(θ − 夾限)` |

**正確性關鍵**：夾限後的 `SoleNormal` 必須**一致地**用於①②③與 `GroundY` 四處——它們描述的是同一個腳底平面。只改旋轉不改殘差，抬升量就會對不上實際腳底。

**哲學檢核（三條全過）**：權重系統零改動，腳全程由 IK 接管；夾限是**連續**的，無二態切換、無震盪源；角度上限屬哲學第 3 條明文允許的「Reach Clamp 類」，**不是**被禁的 Fade／Gate／降權重。活動空間檢核答「否」——只限制地面對齊造成的踝角，不限制抬腿／跨步／轉向。

**`GroundY` 語意變更**：由「ray 原始命中高度」改為「最終腳踝目標對應的接觸高度」，使骨盆補償與殘差抬升後的實際落點一致。副作用：斜坡上骨盆下沉量略減（30° 約少 3cm）。⚠️ Play 時留意骨盆有沒有變得太挺。

**A/B 對照**：`UseTwoPointSampling = false` ⇒ 去掉②（①仍生效，幾何正確性不是可選項）；`MaxFootAlignAngle = 180` ⇒ 去掉③，回到 v2 的完全貼合。

**已知破綻（接受，不在本 ticket 解）**：陡下坡整個踩地相維持腳跟接觸，真人會隨步態滾向前腳掌——需要 Foot Contact／Foot Phase 與背屈／蹠屈不對稱角度才能表達；橫坡的腳掌左右邊緣未被採樣；heel/toe 任一 ray 落空時無殘差保護。**這些屬 `docs/03` 輪 7 品質輪，不是 L1 範圍。**

**驗收（DoD）**
- **EditMode**：pitch 計算抽成 `static` 純函數並附測試（比照既有 `ComputeFootWeight`／`ComputePelvisOffset` 先例）。
- **Play ①（回歸）**：**平地行為逐字不變**——雙點在平面上等高，必須退化為與現行完全一致。
- **Play ②（目標）**：樓梯上腳掌不再插入上一階；斜坡表現**不比現在差**。
- **Play ③（哲學）**：抬腿／跨步／轉向的活動範圍**無縮小**；未出現半 IK 常態化或抖動。
- **Play ④（觀察項，非驗收條件）**：兩點命中高度**非常接近**時，「取較高者」可能在幀間翻轉，帶動 `Normal` 與目標高度跳變（既有權重平滑只平滑權重，不平滑目標）。⚠️ **若真的看到跳動，不得用 gate／降權重處理**——那是被禁的路線；正解是往 Ground Sampling 再升級（遲滯取樣、SphereCast／CapsuleCast）。先觀察，不預先處理。
- **零 GC**：每腳 2 次 `Physics.Raycast`（每帧 2→4 條，非 alloc 多載），穩態 `0 B/frame`。
- **G4 可展示**：`UseTwoPointSampling` 開關能在樓梯上錄出 A/B 對照。

**檔案邊界**
- ✅ 只准動：`Presentation/IK/FootIKController.cs`（`SampleGround`／`ResolveFoot`／新純函數）、`Presentation/IK/FootIKSettings.cs`。
- ⛔ 不准動：`FootIKRig`／`FootIKPoseData`／`FootIKTargetData`／雙管道與 Ownership／`IPresentationController` 契約／`CharacterPipelineRunner`／權重系統。
- **Commit**：獨立一筆 `fix(ik): Foot IK L1 Heel/Toe 雙點採樣`，不與任何工作包混（由使用者執行）。

### 📦 四個 sequential 工作包（摘要；細節在各包開工時另立分卷）

| 包 | 🏗 Architecture Qualification | 🎬 Portfolio Qualification | 交付段落 | ADR 路由 |
| --- | --- | --- | --- | --- |
| **WP1** 鏡頭 ＋ Aim ＋ Throw 依 AimPoint | 「相機／瞄準是純 Presentation 關切」——交付時**黑板 schema 零改動、架構測試條數不變**。這是個**負面證明**：不是每個新功能都要動核心契約 | 探索鏡頭讓既有 locomotion 終於好看；瞄準可信；miss 能歸因於自己 | 1／2／3 | ✅ **確認不開 ADR**（四判準逐條 ❌，見 §12.3）→ 規格已落地 [`docs/09-camera-aim.md`](09-camera-aim.md)（🟡 待 §4 五個裁決點拍板） |
| **WP2** Multi-Action ＋ Action Mapping ＋ 玩家揮劍 | 一顆 `ActionState`／六員 `StateType`／七階管線不變的前提下跑多 action；**加第四個 action ＝ 一份資產 ＋ 一列映射，零程式**；裁決 FU-1 | **遠程／近戰對比**；架構價值唯一能「演」出來的一段（改 SO 即改行為） | 6／7（並讓 8 成為可能） | **ADR-005（Trial）**，前提：ADR-004 已 Accepted |
| **WP3** 敵人戰鬥遭遇 | **敵人攻擊不新增任何 runtime 程式**——Telegraph／Commit／Recovery 全由 `ActionPhase` 的逐 phase `Interruptible` ＋ `Cooldown` 表達 | 敵人是對手不是靶子；Roll 終於有存在理由；雙向互動 | 4／5／8／9 | 不開 ADR → Living Docs |
| **WP4** 主展示 ＋ Teaser | **無新增；本包不得產生任何程式或架構改動** | 整片；節奏與呼吸 | 全部 | 無 |

**WP2 補充（本包是重心，非「順便新增揮劍」）**：Action 身分化 → catalog／注入路徑（解 FU-2）→ 裁決 FU-1 → Action Mapping（`IntentData` 只有一顆 `FireRequested`，兩個動作必然要動它 ⇒ 這是走 ADR 的原因）→ 揮劍 ＋ `MeleeHitEmitter : IActionLifecycleSink`（Release 時一次 `OverlapSphereNonAlloc`，與 projectile 對稱）→ 玩家 `Damage` Definition（EditMode 證明 Action→Action 中斷）→ **命中回饋最小集**（材質閃白 ＋ 一顆命中音，走既有 `AudioController`／`AudioDefinitionSO`）→ **段落 7 的展示素材錄製**（原 G5／P4）。

**WP3 補充**：敵人決策元件**不寫 `MovementIntent`**，而是設定 `AIMovementSource` 的期望距離／速度 ⇒ **A5 白名單不變**。含 **Look At**（`docs/03` §2.3 的管道模式；Telegraph 可讀性有一半來自「它看著我」）與**遭遇結束的最小處理**（一個整數計數，**不是 HP 系統**）。

### 🧺 明確禁止現在擴張（不變，補一條界線）

傷害數值／HP／死亡系統；Effect／Buff／Status Framework（**Slow 因此不進任何一包**）；combo／輸入緩衝／通用 cancel window；Behavior Tree／Utility AI／GOAP／aggression token；通用 targeting service／全域註冊表／singleton；上身層／aim IK；新的管線階段；通用 camera state machine；**動 `LocomotionModel`**（`docs/07` §13.1-R4）。
🆕 **新界線**：命中回饋＝「一個材質參數 ＋ 一顆音效」。**一旦它開始需要註冊、查表或定義檔，就已經越線**——第二個使用者出現前不建 production abstraction。
🆕 **相機側界線（2026-08-31，使用者裁決）**：lock-on／target switching／strafing movement mode／shoulder swap／camera zone／
電影式自動構圖／多層 camera profile／複雜 obstruction avoidance／自動 reposition 與 corner solving／**為未來預抽的大型 Camera Framework**——
**一律不進 WP1**（清單全文在 [`docs/09` §2.3.2](09-camera-aim.md)）。防穿牆是**唯一**進 scope 的碰撞處理，且只准「拉近」。

### 📇 Follow-up 登記表（只登記，不處理）

| # | 發現 | 處理時機 |
| --- | --- | --- |
| **FU-1／FU-2／FU-3** | Action→Action 中斷不可能／一角色一份 Definition／mailbox 無身分 | **WP2**。全文已寫入 [`docs/08` §11.1](08-skill-system.md) |
| **FU-4** | Throw 沿角色 root forward 發射（`ThrowProjectileEmitter` 的 `Instantiate(..., transform.rotation)`） | **WP1 進行中** → 解法在 [`docs/09` §8.2](09-camera-aim.md)（含未接線時的退化路徑） |
| **FU-5** | 相機**旋轉雙權威**：`_yaw`／`_pitch` 只驅動位置軌道，最終 rotation 被 `LookAt` 整個覆寫 ⇒「滑但不好瞄」的根因是這個，不只是 damping 值 | **WP1 進行中** → 裁決點 [`docs/09` §4-D1](09-camera-aim.md)。🔍 **已證明修法對既有移動是行為中性的**（`offset.x == 0` ＋ 兩個消費者都壓平 `y`），論證見 §3-E3、測項 T-3 |
| **FU-6** | `AIMovementSource` 依賴 `data.CameraTransform`，把世界方向轉成相機空間只為了讓 `MotionDriver` 轉回世界；敵人的移動因此綁在玩家相機上 | **不排程**。正解是給 `MovementIntent` 座標基底語意或讓 producer 直接輸出世界方向（ADR-003 §9-L2 的延續），等第三個 producer 出現再談 |
| **FU-7** | Cinemachine 2.10.7 在 manifest 但專案零使用（只有 `Assets/StarterAssets` 引用）＝決策債 | ✅ **已裁決（2026-08-31，待使用者確認）：不導入**。四條理由見 [`docs/09` §4-D5](09-camera-aim.md)——主因正是預警過的 `CinemachineCore.GetInputAxis` 靜態全域 hook（停止條件②），且 CM 的價值面（blend 圖／優先級／群組取景）**一項都不在 WP1 scope 內**。manifest 清理屬使用者側、可選 |
| **FU-8** | `ThrownProjectile` 每次 `Instantiate`／`Destroy` | **不排程**。零 GC SOP 管的是穩態，投擲是事件型配置；Profiler 實測成為問題才做池 |
| **FU-9** | `ActionState.CanEnter` 對 `FireRequested` 與 external mailbox 同權，無仲裁 | WP2 順帶（多來源必然要定義誰贏） |
| **FU-10** | `LocomotionModel` 走向 God Class（`docs/07` §13.1-R4） | 不排程；**四個工作包都不得動它** |
| **FU-11** 🆕 | **相機是第二個輸入權威**：`InputData.LookInput` 有採樣但**零消費者**，`ThirdPersonCamera` 直接讀 `Mouse.current.delta`，繞過 `BlockInput` 閘門（以 `Cursor.lockState` 代理） | **不排程**（WP1 明確不修）。`InputData` 是 `ref struct`、當帧銷毀，LateUpdate 的相機拿不到 ⇒ 接回管線只能經黑板 ⇒ 撞停止條件①。觸發條件：①出現「游標自由但相機仍該轉」的模式（dev-spec §7.3 的失效條件）；或②**第三個** Presentation 元件想直接讀輸入裝置。詳見 [`docs/09` §5.3](09-camera-aim.md) |
| **FU-12** 🆕 | `PlayerRuntimeData.AimTarget` 是**死欄位**——有宣告、Editor 有顯示，**無寫入者也無讀取者** | **不排程**。⛔ **不得混進 WP1**：清理它會動黑板 schema ＋ dev-spec §1.1 ＋ Editor，會污染本包「schema 零改動」的負面證明。應獨立成一筆清理 commit |
| **FU-14** 🆕 | **Landing 角色隔帧閃爍**（`MovingPelvisSolver.FootLimit`）：「正在下降」用 `SoleHeight ≤ 上一帧` 判定，而 `SoleHeight` 是相對**腳正下方的地面**。腳跨過階緣、或該帧地面射線漏打（`HasHit` false／gY＝0）時，SoleHeight 會跳大 ⇒ 那一帧不算 Landing，約束就消失。移動中與停步都會發生（樓梯下樓 Run 最明顯，見 `docs/46` §34.3） | **不排程**（底層問題，使用者 2026-10-02 裁決另記）。單獨修的效果很小（§34.5 變體 A：Run ≥5 cm 帧 17→14／20→16），只有在 Landing 需要「提前預告」時才有意義。修法方向：用腳目標的世界高度判定下降，漏打時沿用上一帧地面——會改變移動中的骨盆，需要先跑移動中＋停步回歸 |
| **FU-13** ✅ | **Souls 式 lock-on** | ✅ **已開包（2026-08-31）** → 規格 [`docs/10-lock-on.md`](10-lock-on.md)（🟡 待 D1 裁決）。**拆兩階段**：Stage 1（不含 strafe）四條 ADR 判準全不成立 ⇒ **不開 ADR、走 Living Doc**；Stage 2（含 strafe）判準①③成立 ⇒ 必須開 ADR-005，且**不得在 ADR-004 仍為 Trial 時開始** |

---

## 🗡️ 素材登記：Free Sword Animations（2026-08-30 匯入，**WP2 才使用**）

**位置**：`Assets/EEJANAI_Team/FreeSwordAnimations/`（`FBX/` 12 個 ＋ `Animations/` 抽出的 `.anim` ＋ `Animations/Animator/` 的 `.controller` ＋ `Models/SwordSample/` ＋ `Prefabs/`）。

**現況更正**：**武器本體其實有**——`Models/SwordSample/Sword.obj` ＋ `swordmaterial.mat` ＋ 四張貼圖 ＋ `Prefabs/Sword.prefab`。
**真正缺的是掛點**：右手骨骼 socket ＋ 相對 transform ＋ 收放策略。⇒ 屬**使用者側 prefab 工作**，不是素材缺口。

**使用紀律（現在就定，避免 WP2 開工時漂移）**

1. ✅ **一律引用 `FBX/slash*.fbx` 的 sub-clip**。❌ **不要用 `Animations/*.anim`**——那是抽出的複本，違反 CLAUDE.md「Animation Assets: Immutable by Default」（FBX sub-clip 是唯一真相）。
2. ❌ **`Animations/Animator/*.controller` 一律不使用**。專案走 Animancer ＋ `AnimancerFacade.transitionMappings`，Mecanim controller 會是第二套播放權威。
3. ⚠️ **9 個 slash 只取 1**。取多個是 combo 的滑坡，而 combo 在禁止清單裡。WP2 需要的是「**第二個 action**」，不是「第二套攻擊系統」。
4. ⚠️ **掛點只做「永久掛在手上」**（最簡）。**不做 sheath／draw 拔劍收劍切換**——那是第二個 Action lifecycle 的偽裝，會把 WP2 的題目從「Action Mapping」偷換成「武器狀態管理」。
5. 🔍 **需先驗證**：這些 clip 是否為 Humanoid、能否 retarget 到 X Bot（不同 rig）。🛑 **若不能 retarget，停下來回報**——處置是換素材或換載體動作，**不是編輯 clip 內容**（CLAUDE.md 的四階升級順序：資料 → 表現層 → 換 clip → 才是改 clip 內容）。
6. `Scenes/Sample.unity`（素材附的展示場景）**不納入專案場景管理**，看完即可忽略。

---

## 🤝 Codex 交辦與交付邊界（2026-08-30 更新）

**檔案擁有權不變**（以擁有權切分，避免兩個 agent 改同一檔）：

| 角色 | 擁有 |
| --- | --- |
| **Claude** | `docs/**`、`LearningNotes/**`、`ArchitectureRegressionTests.cs`、`WORKLOG.md` |
| **Codex** | `Assets/Scripts/**`（新檔）、`Assets/_Project/Tests/EditMode/*Tests.cs`（**除** `ArchitectureRegressionTests.cs`） |
| **使用者** | 全部 `.prefab`／`.asset`／`.meta`／場景／Import 設定／NavMesh 烘焙／**全部 Git 操作**／全部 Play 驗收 |

**交付單元的規矩（適用每一包）**

1. **文件先行**：Claude 的規格／ADR 進 Trial **先落地並 commit**，Codex 才開始寫程式。這樣 review 時有對照基準，也符合 Trial-first 的「先修文件再驗證」。
2. **Codex 一次交付一個工作包的完整程式 ＋ 對應 EditMode 測試**，不做半包交付。允許交接過程短暫紅燈，**但交回給使用者驗收時必須全綠**。
3. **Codex 需要新的架構不變量時，提出需求由 Claude 落地**（`ArchitectureRegressionTests.cs` 是 Claude 獨佔，避免兩邊同時改同一張規則表）。
4. **Codex 不碰任何 Unity 資產、不碰 git。** 程式寫完即停，接線與驗收交還使用者。

**各包給 Codex 的紅線（撞到就停下來回報，不要自行擴 scope）**

| 包 | 🛑 停止條件 |
| --- | --- |
| **WP1** | ① 發現 aim 狀態**必須進黑板** ⇒ ADR 判準①，不得順手加欄位；② Cinemachine 需要靜態全域輸入 hook；③ soft target 開始需要目標列表／切換／跨系統查詢；④ 想改 `AIMovementSource` 或 `MovementIntent` 的座標基底（FU-6 是獨立議題） |
| **WP2** | ① FU-1 的候選解需要動 ADR-004 §3 的 D1–D7 任一條；② 出現「每個 Action 一個 `StateType`」的念頭（§5.2 已否決，A13′ 會紅）；③ **為 sword 建立 `ActionState` 子類別**（A19 會紅）；④ 命中判定開始需要起始幀／結束幀／多段／無敵幀；⑤ 命中回饋長出任何共用抽象；⑥ 同時出現兩個 Trial ADR |
| **WP3** | ① 敵人節奏**寫不進資產**、必須加程式分支 ⇒ 回頭修 WP2 規格，**不是在敵人身上補 `if`**；② 決策元件需要自己的狀態機；③ 決策元件想直接寫 `MovementIntent`（A5 會紅）；④「命中計數」開始長出血量／傷害值／死亡狀態／UI |
| **全部** | 任何包想動 `LocomotionModel`（`docs/07` §13.1-R4） |

---

## 🌱 Git commit 切點（**由使用者執行**；AI 一律不碰 git）

> 判準是「**能不能單獨 revert**」，**不是歷史好不好看**。為了漂亮的歷史去做 `git add -p` 拆同一個檔案，
> 代價高於收益——**檔案混在一起就合成一筆，並在 message 裡誠實說明**。

**A. ✅ 已完成（2026-08-31 核對）** —— 原標題：「現在（工作樹已累積 P1＋P2＋治理文件，尚未 commit）」

> 📌 **實際切法與下表規劃不同，原規劃全文保留作為歷史脈絡。** 使用者實際切成兩筆，
> C0-a／C0-b／Foot IK v4 與多數文件**合流進 `c26c72d`**，治理條文與 HTML artifact 落在 `d5132e9`。
> 這與本節「檔案混在一起就合成一筆」的判準不衝突，但**代價要記在帳上**：
> 第三方素材、ADR-004 Trial 程式、Foot IK 修正三者**已無法各自單獨 revert**——
> 日後若要移除 Free Sword 素材或退掉 Foot IK v4，會連帶動到 ADR-004 的程式與資產。
> ⚠️ 另注意 `c26c72d` 的 message 標題是 `fix(ik): …`，**未依 C0-b 規劃標注「Trial／待驗收」**
> ⇒ **ADR-004 的 Trial 狀態以本檔與 [`docs/ADR/004`](ADR/004-action-in-fsm.md) §0 為準，不以 commit message 為準。**

| 順序 | 原規劃切點 | 內容 | 為什麼單獨一筆 | 實際落點 |
| --- | --- | --- | --- | --- |
| **C0-a** | `chore(assets): 匯入 Free Sword Animations（EEJANAI_Team）` | 只有 `Assets/EEJANAI_Team/**` ＋ `.meta` | 第三方素材單獨一筆 ⇒ 日後換版或移除可乾淨 revert，不與自己的程式糾纏 | ✅ **`c26c72d`**（與 C0-b、Foot IK v4 合流，未單獨成筆） |
| **C0-b** | `feat: 敵人管線重用 ＋ Action in FSM（ADR-004 Trial，待 Play 驗收）` | `Assets/Scripts/**`、`ArchitectureRegressionTests.cs`、以及 P1／P2 的資產（Throw／Damage 動畫資產、Bake、`Actions/`、`EnemyStateMachineConfig`、`ThrownProjectile.prefab`、`Y Bot`、場景與 `X Bot.prefab` 改動） | ⚠️ **P1 與 P2 在 `CharacterPipelineRunner.cs` 內混在同一個檔**（D1 守衛拆解 ＋ Action 組裝），拆兩筆需要 hunk 級手術 ⇒ **合成一筆**。message 必須標 **Trial／待驗收**，不得寫成已完成 | ✅ **`c26c72d`**（含全部 P1／P2 Unity 資產；message 未標 Trial，見上方注意事項） |
| **C0-c** | `docs: Trial-first 治理 ＋ ADR-004 ＋ scope 收斂與後續四包` | `CLAUDE.md`、`WORKLOG.md`、`docs/**` | 文件與程式分開 ⇒ 程式若 revert，治理決策不會跟著消失 | ✅ **拆在兩筆**：`docs/**`／`WORKLOG.md`／`CLAUDE.md` 主體在 **`c26c72d`**（與程式同筆，未達成「文件與程式分開」）；「Documents Live in the Repo」條文 ＋ `docs/artifacts/foot-ik.html` 在 **`d5132e9`** |

**B. ⏳ 尚未開始 —— Play 驗收通過之後**

| 順序 | 建議切點 | 內容 |
| --- | --- | --- |
| **C1** | `docs: ADR-004 Trial → Accepted（A–F 回填）` | ADR-004 §10／§11、`docs/08` 狀態欄、`docs/changelog.md`、G1–G3／G6 打勾 |

⚠️ **`Trial → Accepted` 必須是獨立一筆，且晚於程式那一筆**——Accepted 是**驗收結果**，把它跟程式塞進同一個 commit 等於宣稱「寫完即通過」。

**B′. 🆕 現在工作樹上待 commit 的東西（2026-08-31 深夜）**

> ⚠️ **兩件事來源不同、revert 邊界不同，不要合成一筆。**

| 順序 | 建議切點 | 內容 |
| --- | --- | --- |
| **C0-d** | `fix(perf): AnimationKey 每帧配置（ADR-004 Trial 期回歸）＋ A23` | `Core/StateMachine/BaseState.cs`、`ArchitectureRegressionTests.cs`、`docs/02-dev-spec.md` §7.1、`docs/ADR/004` §11 |
| **C0-e** | `spec: WP1 相機／瞄準規格（docs/09）` | `docs/09-camera-aim.md`、`docs/00-map.md`、`WORKLOG.md` |
| **C0-f** | `feat(camera): WP1 相機單一權威 ＋ AimPoint ＋ 防穿牆（待 Play 驗收）` | `Presentation/Camera/**`、`Presentation/Actions/ThrowProjectileEmitter.cs`、`_Project/Tests/EditMode/CameraAimTests.cs` |

⚠️ **C0-d 必須早於 C0-f**：GC 修正是 ADR-004 的收尾，WP1 若要 revert 不該把它一起帶走。
⚠️ **C0-f 的 message 必須標「待 Play 驗收」**——測試尚未實跑，不得寫成已完成。

**C. ⏳ 尚未開始 —— 之後每個工作包的固定節奏（四筆，WP1 起適用）**

1. `spec:` / `docs:` — 規格與 ADR 進 Trial（**Codex 動程式之前**）
2. `feat:` — Codex 的程式 ＋ EditMode 測試（**全綠才交**）
3. `feat(assets):` / `chore(assets):` — 使用者側資產與接線（`.prefab`／`.asset`／`.meta`／場景）
4. `docs:` — Play 驗收後的 **fold-back**（Living Docs 對齊實況；如有 Trial 則轉 Accepted）

**為什麼程式與資產要分開**：Play 驗收失敗時可以單獨 revert 程式而**不丟掉資產工作**——`.meta` 的 GUID 重建代價遠高於重寫一次程式。

**軌 A 獨立 commit**：`feat(scene): 關卡地形（斜坡／樓梯／障礙）` 與 `fix(ik): Foot IK L1 Heel/Toe 雙點採樣` 各一筆，**不與任何工作包混**——它是並行軌，混進去會讓工作包的 revert 邊界失效。

> 📌 **實況（2026-08-31）**：Foot IK 那筆 ✅ 已完成於 **`c26c72d`**，但**沒有做到「不與任何工作包混」**——
> 它與 C0-a／C0-b 同筆，因此 Foot IK v4 現在無法脫離 ADR-004 的程式單獨 revert（代價已記在 A 段）。
> `feat(scene): 關卡地形` ⏳ **尚未開始**（軌 A 的場景部分，屬使用者側資產工作）。

---

## 🎯 作品集最低限度衝刺（2026-08-29 排定，**優先於一切工程路線圖**）

> **任務定位**：把專案從「一個人在空地上走路的 demo」推到「一個看起來像遊戲、且能證明架構價值的最小完成品」。
> **為什麼優先順序改變**：投遞卡在 HR／外行關卡，架構深度沒機會被技術面試官看到。根因診斷與敘事策略見
> `LearningNotes/portfolio-framing.md` §1–§2：**現有展示項的成功標誌都是「隱形」**（做對了外行只看到「角色正常走路」），
> 因此必須補上「外行看得出這很難做」的項目。
> **紀律不變**：本輪所有工作都必須是**既有路線圖的兌現**（ADR-003 §11 的 AI producer、dev-spec §1.4 的死亡來源、
> `docs/03` §1.3-L1 的「穿模觀感無法忍受時可提前」條款），**不得**為了作品集新造路線圖上沒有的系統。

### ✅ 完成定義（DoD）：作品集最低限度 Gate

- [x] **G1 敵人重用整條管線**：一隻敵人會朝玩家移動，且**完整重用** Walk/Run tier、Stop 腳相選片、Foot IK、腳步音；玩家與敵人跑**同一份** `CharacterPipelineRunner` 程式碼。
- [x] **G2 技能無可爭議**：玩家可發動 Throw（`Throw_Start`→`ThrowLoop`→`ThrowEnd*`）並丟出一顆會飛的投射物，命中敵人會有反應（播 `Damage`）。
- [x] **G3 中斷矩陣可展示**：技能可被移動／Jump／Roll 中斷，行為與 `docs/08` §8.3 的表格一致，且 EditMode 有對應測項。
- [ ] **G4 Foot IK 可 A/B**：斜坡／樓梯上開關 IK 的差異外行可見，且 L1（跨階腳掌穿模）已修。
- [ ] **G5 資料配置可展示**：改一份 ScriptableObject 的值 → Play 立刻看到行為改變，**至少三個可 demo 的參數**（速度 tier／跳躍高度／打斷規則）。
- [x] **G6 品質門檻**：EditMode 全綠（含本輪新增的架構不變量）；Development Build Profiler 穩態 `0 B/frame`（走 dev-spec §7.4 SOP）。
- [ ] **G7 場景像關卡**：demo 場景有斜坡／樓梯／障礙與明確目標，不是空地。

> 🔔 **交辦指令（不是提醒）**：**當最後一項打勾時，該會話必須主動告知使用者「已達作品集最低限度」**，
> 並附上七項的逐項驗收證據（哪個測試／哪張截圖／哪次 Play 驗收）。不得默默完成後繼續往下做。

---

### 🔴 兩顆必須先處理的架構地雷

**地雷 1：`CharacterPipelineRunner` 的入口守衛會讓敵人整條管線不跑**

```csharp
// CharacterPipelineRunner.Update() 第一行
if (_inputSource == null || _stateMachine == null) return;
```

敵人沒有 `IInputSource` ⇒ 順序 1～5 一行都不執行。這不是 bug，是**結構性假設**：Runner 目前假設「有輸入源」是管線運作的前提，
而 ADR-003 D2 的賣點正是「換掉 producer，Runner 零改動」。
⇒ **這是 ADR-003 §9-L2（「介面可能設計得不夠貼實需求，待第二個 model／producer 壓測」）的第一個實證。**
處理方式屬裁決點 **D1**（見下）。

**地雷 2：NavMesh 絕對不能擁有位移**

`NavMeshAgent` 預設自己搬 transform，違反「`CharacterController.Move` 是唯一位移出口」（`docs/07` §10.1）。
**正確接法**：`updatePosition = false` / `updateRotation = false`，只當**路徑查詢服務**用（取下一個 corner），
把方向交給 `AIMovementSource` 寫成 `MovementIntent`，位移仍全程走 `LocomotionModel → MotionDriver → CharacterController.Move`。
好處：Locomotion 平滑、tier、Stop 選片、Foot IK、腳步音**全部免費重用**。

> 順帶：敵人＝第二個 `PlayerRuntimeData` ＋第二個 Runner。design-doc §4.9 當初把 `Time.timeScale`／`Cursor` 判給應用層的理由
> 正是「第二隻角色進場立刻露餡」——**敵人是那個判斷的第一次真實驗收**，驗收結果請回填 design-doc §4.9。

---

### 工作包與順序

| 順位 | 工作包 | 內容 | 對應 Gate |
| --- | --- | --- | --- |
| **P1** | **敵人（適配控制器＋尋路）** 🟡 程式已落地、待 Unity 接線／Play 驗收 | ✅ Runner 無 input 仍跑完整 pipeline；✅ `AIMovementSource : IMovementIntentSource`；✅ NavMesh 關閉 Transform authority、只供 path direction。⏳ 使用者側 prefab／NavMesh 烘焙與 G1 Play 證據；受擊入口已落成 single-slot external Action request seam | G1 |
| **P1 平行** | **Foot IK L5 調參** | `RaycastUpOffset`／`RaycastDistance` 在乾淨 collider 基線上調參（`docs/03` §1.3-L5：**tuning 域，非程式問題**） | G4 |
| **P2** | **Throw → Projectile → Enemy Damage** 🟡 Runtime／EditMode 測項已落地，待 Unity 編譯與 Play 驗收 | ✅ 單一 `ActionState`；✅ Player Fire／Enemy external mailbox；✅ Player Throw／Enemy Damage 各一份 Definition；✅ phase-authored exactly-once release sink；✅ projectile 只提交 request；✅ A13′／A19–A22 與 T13–T15。⏳ 使用者建立 Transition／Bake／Definition／prefab 與 Config 規則後 Play 驗收；ADR-004 仍為 Trial | G2, G3 |
| **P3** | **Look At ＋ Foot IK L1** | ①Look At（`docs/03` §2.3：「複製 M3.1 Controller＋Rig 管道模式；零 Runner 改動」，有敵人後才有目標）②Foot IK L1 Heel/Toe 雙點採樣（**只動 `SampleGround`／`ResolveFoot` 內部＋Settings，雙管道／Ownership 全不動**） | G4 |
| **P4** | **資料配置展示（client 端）** | **先做零程式版**：錄影展示改 `GaitProfileSO`／`JumpStateParams`／`PlayerStateMachineConfig` → Play 立刻生效。只有證明「這樣還是看不懂」才做工具（裁決點 D3） | G5 |
| **P5** | **場景收尾** | 斜坡／樓梯／障礙＋一個明確目標，讓場景看起來像關卡 | G7 |

> **為什麼敵人排在技能之前**（與最初構想相反）：敵人是**舞台**——技能要打誰、Look At 要看誰、配置要配什麼，都等它。
> 而且兩顆地雷會影響後面所有設計，越早撞越好。

> 🔄 **2026-08-30 重排（見上方「Scope 收斂與後續四包」）**：
> **P1／P2 不變**，仍是現行工作，停止線＝ADR-004 `Accepted`。
> **P3 拆解**：Look At → **WP3**（Telegraph 可讀性的一半）；Foot IK L1 → **軌 A**。
> **P4（資料配置展示）→ WP2 段落 7**——它不是最後的加分項，是**架構價值唯一能「演」出來的一段**。
> **P5（場景收尾）→ 軌 A，現在就能開始**——它是影片段落 1 的唯一來源，且能讓 P1／P2 的 Play 驗收在像樣的場地上進行。
> ⚠️ **上一版把這三項當成 polish 是分類錯誤**：它們不 gate 任何技術 qualification，但**每一項都是觀眾必需品**。
> 判準已改為「**任一條軸必要即非 polish**」。

---

### 任務分配（Claude ／ Codex ／ 使用者）

> **分配原則：以檔案擁有權切分，避免兩個 agent 同時改同一個檔。** 跨界的檔案在下表明確標註協調方式。

| 角色 | 負責 | 檔案擁有權 |
| --- | --- | --- |
| **Claude** | 架構裁決與規格（D1／D2 兩案比較與建議）、新增架構不變量的定義、Foot IK L1 規格（守住「不動雙管道」邊界）、全部文件同步 | `docs/**`、`LearningNotes/**`、`ArchitectureRegressionTests.cs`、`WORKLOG.md` |
| **Codex** | 實作與功能測試：`AIMovementSource`、NavMesh 路徑查詢、投射物、`ActionState`＋Throw／Damage vertical slice、Look At 的 Controller／Rig、Foot IK 的 `SampleGround`／`ResolveFoot` 內部 | `Assets/Scripts/**`（新檔）、`Assets/_Project/Tests/EditMode/*Tests.cs` |
| **使用者** | 全部 Unity 資產側與驗收：`.prefab`／`.asset`／`.meta`／場景／Import 設定／`AnimancerFacade.transitionMappings`／NavMesh 烘焙；所有 Play 驗收；**全部 Git 操作** | 同左（**AI 一律不碰**） |

**需要協調的交界（先講好再動）**

| 檔案 | 誰動 | 條件 |
| --- | --- | --- |
| `Core/Pipeline/CharacterPipelineRunner.cs` | **Codex 實作** | **必須等 D1 裁決後**；同一次由 Claude 同步 dev-spec §2.1 與 §7.1 的對應條目 |
| `Core/Movement/Models/LocomotionModel.cs` | **本輪不動** | 若某工作包宣稱需要改它，先停下來提裁決——`docs/07` §13.1-R4 已預警它正走向 God Class |
| `Presentation/IK/**` | **Codex 實作**，Claude 出規格 | 只准動 `SampleGround`／`ResolveFoot` 內部與 Settings；**動到雙管道或 Ownership 即為越界**（`docs/05` §3.5.1／A11 守） |
| `ArchitectureRegressionTests.cs` | **Claude 獨佔** | Codex 若需要新不變量，提出需求由 Claude 落地，避免兩邊同時改同一張規則表 |

---

### ✅ 已裁決（2026-08-29，使用者拍板）

| # | 裁決 | 內容與後續 |
| --- | --- | --- |
| **D1** | ✅ **選 (a)：拆守衛，輸入源缺席時管線照跑**；🟡 程式已落地待 Play | 守衛只留 `_stateMachine != null`；取樣改 `_inputSource?.FetchRawInput(ref inputData)`，後續沿用輪 4 既有的「無輸入＝輸入歸零，管線照跑」語意。`MovementIntent` 的唯一寫入者語意是「每隻角色當下 active 的 `IMovementIntentSource`」；P1 合法實作現為 Player／AI 二選一，A5 白名單與 dev-spec 已同步。ADR-003 為 Accepted immutable log，壓測結果回填 Living Docs，不改寫該 ADR。 |
| **D2** | ✅ **選 (a)：敵人完整使用既有 `FullBodyStateMachine`** | **但 P1 只重用既有 Idle／Move，不為敵人新增任何 `StateType`**（A13 在 P1 範圍內零改動）。敵人與玩家跑同一份 FSM 程式與同一份 `StateMachineConfigSO` 拓撲 ⇒ 這是「一套 FSM 撐兩個角色」的第一次驗收 |
| **D3** | ✅ **先不做專屬工具** | 照 P4 的零程式版（錄影展示改既有 SO → Play 立刻生效）先驗證展示效果。**不足時才回頭談工具**，屆時須在文件寫明正當性來自作品集需求而非 Gate A／B |

| **D4** | ✅ **選 (a)：Action 併入 `FullBodyStateMachine`** | `StateType` 加**恰好一個**成員 `Action` ＋一顆資料驅動的 `ActionState`（動作＝`ActionDefinitionSO` 資產）。lifecycle／animation／interrupt 三者回歸 FSM 單一來源；順序 4.6 **不新增**。**Trial 期暫停 A13 → A13′（六員）＋ A19**。<br>📄 決策：[`docs/ADR/004-action-in-fsm.md`](ADR/004-action-in-fsm.md)（🟡 **Trial**）／實作規格：[`docs/08-skill-system.md`](08-skill-system.md)<br>🔧 **使用者修正兩點（已寫入 ADR）**：①`Priority` 是**競爭排序**不是打斷資格，G5 要展示 `CanBeInterruptedBy`／transition policy（ADR §3-D6、§5.4）；②A19 不是永久禁令，改為「禁止**為每個 Action** 建立獨立 subclass；差異優先資料化」，以 allowlist ＋書面理由實作（ADR §3-D3）。 |
| **D5** | ✅ **治理方式改為 Trial-first**（2026-08-29） | 見下方「🧪 治理原則」。ADR-004 是第一個適用者 |

---

## 🧪 治理原則：Trial-first（2026-08-29 起適用）

> **「Architecture decision 可以先成為 Trial implementation baseline；第一個真實 vertical slice 是架構驗證的一部分。
> 只有經實作與 Play／Test 驗證後才 `Accepted`。」**

```text
Design → Trial → Implement → Observe → Revise → Accept      ← 現行
Design → Freeze → Implement                                  ← 已棄用
```

**為什麼改**：原流程對單人開發產生壞誘因——ADR 一旦 `Accepted` 就凍結，實作撞到問題時**補 workaround 比修文件便宜**（修文件要開新 ADR）。結果是文件整潔、程式歪斜。

**規則**

1. **`Trial` 狀態的 ADR 是實作基線，但可被修改**，不必為每次修正開新 ADR；修改一律記入該 ADR 的「修訂紀錄」。
2. **實作暴露問題時：先修 Trial ADR ／ Living Spec → 再驗證。不得為了維護舊文字而補 workaround。**
3. ADR 只保留「**改錯會造成架構污染**」的決策；具體欄位、計時方式、冷卻細節等實作項一律下放 Living Spec，並在 ADR 內**明列哪些不凍結**（ADR-004 §9 為範本）。
4. **架構回歸測試驗證「目前有效的 baseline」**，該 baseline 可來自 Accepted ADR，**也可來自已正式進入 Trial 的 ADR**。因此 Trial 取代舊 invariant 時**不是「暫停」而是「取代」**，同一工作包內把測試換成新 baseline。⛔ **不建立 generic 的測試暫停／停用機制。** 允許 agent 交接過程短暫紅燈，**但交付使用者驗收時必須全綠**。
5. **Fold-back**：Trial／Spike 期間允許短暫 code-first，**但同一工作包結束、交付驗收之前，Living Docs／WORKLOG 必須 fold back 到實際程式狀態**；不得把未實證的內容寫成已完成事實。
6. **使用者已裁決 ≠ 工程上已驗證。** 引用 Trial 文件時必須註明其狀態。
7. `Accepted` 之後回到 Immutable Log 規則（要改決策就開新 ADR 取代）。

> 📌 **完整治理條文已提升至 `CLAUDE.md`**（「ADR Lifecycle」／「Code / Documentation Fold-back」／「Architecture Invariants Track the Effective Baseline」／「Spike / Probe Exception」四節）。本段只是本輪的操作摘要。

### ⏳ ADR-004 Acceptance Review（Throw vertical slice 完成後執行）

- [ ] **A** Throw 在 Unity Play 實際跑通（Start → Loop → End／Cancel 全程）
- [ ] **B** 三個權威與設計一致（動畫只由順序 5 播；打斷只由 FSM ＋資產決定；lifecycle 只有 `BaseState` 一套）
- [ ] **C** 既有 Idle／Move／Jump／Roll **無回歸**（動畫播放序列與位移路徑逐字不變）
- [ ] **D** EditMode 全綠，含 A13′／A19／A20 與行為等價回歸
- [ ] **E** 零 GC 通過（dev-spec §7.4 SOP，穩態 `0 B/frame`）
- [ ] **F** **實作沒有逼出第二套 authority 或明顯 workaround** ← 本 Trial 的真正目的

全通過 → 使用者確認 → ADR-004 `Trial → Accepted` ＋ 記入其 §11 ＋ 同步 changelog。
未通過 → 依 ADR-004 §10 的處置順序（先修文件再驗證，必要時轉 `Rejected` 並復原 A13）。
| **D1** | **Runner 入口守衛怎麼拆** | (a) 守衛改成「輸入源缺席時仍跑管線」——更誠實，兌現 ADR-003 D2 的宣稱，但動到跨領域契約（dev-spec §2.1）；(b) 給敵人掛一顆 null-object 輸入源——改動更小，但等於承認「Runner 需要一個假輸入源」，宣稱沒有真正被兌現。<br>**Claude 建議 (a)，且它其實比 (b) 更小**：輪 4 的 `BlockInput` 裁決（dev-spec §7.2-M5）已經確立「**沒有輸入＝輸入歸零，管線照跑**」這個語意，並實作為順序 2 閘門的 `inputData = default`。敵人是**同一個形狀**——守衛只需保留 `_stateMachine != null`，取樣改為 `_inputSource?.FetchRawInput(ref inputData)`，後面全部沿用既有歸零語意，`PlayerLocomotionPolicy` 依然是 `MovementIntent` 的唯一寫入者（A5 零改動）。⇒ 一行級改動，且**不是新機制，是既有裁決的第二個適用案例** |
| **D2** | **敵人要不要完整的 `FullBodyStateMachine`** | (a) 要（Idle/Move 重用，受擊另議）；(b) 不要，敵人只跑 locomotion ＋一個受擊播放。⚠️ 選 (a) 時注意 `StateType` 不得為了敵人擴張（A13） |
| **D3** | **資料配置要不要做專屬工具** | 先做零程式版（P4），只有證明不夠才做。⚠️ 若要做，**必須在文件裡誠實寫明：這顆工具的正當性來自作品集需求，不是 CLAUDE.md 的 Gate A／B** |

---

### 本輪明確不做

- ❌ **撿東西／拉拉桿**（`PickUp_*`／`PullLever_*`）——`_LH/_RH`＋`_90` 的 selection 復用**只有工程師看得到**，屬 L2 素材，延後。
- ❌ **傷害數值／血量系統**——受擊只播動畫。「受擊反應」與「傷害系統」是兩件事，後者無消費者（`docs/08` §3.3）。
- ❌ **法術／VFX**——法術＝punch ＋粒子，架構上完全相同；且 VFX 是真正的素材缺口，廉價特效會拉低觀感（`LearningNotes/portfolio-framing.md` §8）。
- ❌ **F4 Upper Body Layer**——本輪技能刻意選成不需要它的形狀。
- ❌ **新 ADR**——除非 D1 選了會改動跨領域契約的方案，屆時再議。
- ❌ **不得為了作品集新造路線圖上沒有的系統**（`LearningNotes/portfolio-framing.md` §5.3）。

---

## 🗂️ 已完成：Phase C Locomotion Transition Foundation（2026-08-20 重排，✅ 2026-08-21 全部驗收通過）

> **任務定位：先驗證資產與責任 seam，再實作。**禁止先寫 `LeftStopState` / `RightStopState`，也不得為了未來項目一次建出萬用 Animation Action framework。

### 開場必讀（僅這些）

1. `docs/00-map.md`
2. 本段
3. `docs/04-locomotion-foundation.md` §3／§4／§6／**§15**
4. `docs/ADR/003-movement-intent-layering.md` §3 D3／D4 與 §13.2
5. 需查權限時，只讀 `ArchitectureRegressionTests.cs` 的 `LayerRules` / `WriterRules`

### 本會話進度（2026-08-20）

- [x] `MotionBakeData.ComputeAverageSpeed` 只排除烘焙器產生的 `time=0/value=0` 第 0 帧哨兵；為空、單鍵與非哨兵零值補回歸測試。
- [x] 同步 `docs/02-dev-spec.md` 與 `docs/06-animation-presentation.md` 的代表速度定義；無黑板、ownership、FSM 或依賴方向變更，不開 ADR。
- [x] Kubold 來源 FBX 已回到 `Assets/MovementAnimsetPro/`；確認 Stop／Turn／Start 與 `RunFwdTurn180_*` 真實覆蓋，且沒有 90° Moving Pivot。代表 Pivot 改採 `RunFwdTurn180_R_LU`，缺口如實保留。
- [x] Phase C 4＋3 批次機械操作已完成；一次性具名選單隨即移除，不留下 Kubold／Phase C Editor menu 債。
- [x] 修正首次操作抓到的採樣前提錯誤：Animator 可位於 Root 子階層；單支與批次共用唯一 Humanoid Animator 解析器，Sample 對 Animator 所在 GameObject 執行。批次按鈕整合進既有 Motion Bake 視窗；新增 2 條解析回歸測試。
- [x] 將既有 Motion Bake 視窗改為通用批次烘焙：明確 Clip 清單／拖放／Project 選取加入／去重與空值驗證；共用採樣設定與既有 Bake 演算法，不修改 Import preset。
- [x] 使用者在 Unity Preview／Play 證實 `_LU/_RU`：`WalkFwdStop_LU` 左腳先停、`WalkFwdStop_RU` 右腳先停（First Stop／First Plant Foot）。
- [x] `Locomotion.asset` 直接配置手感門檻 `0 / 0.35 / 0.75 / 1`＋派生 PlaybackSpeed；不留下低頻一次性校正 UI。
- [x] Motion Bake 視窗補完整 ScrollView 與合理最小尺寸，批次清單在小視窗仍可操作。
- [x] 使用者 Play 驗收 Walk↔Run↔Sprint 基礎校正（主觀差異不大，但既有路徑可接受）。
- [x] Claude 規格完成；Codex 複核並否決 Presentation IK → Core 回流。
- [x] C1 程式垂直切片、功能測試與 A12～A15 架構守衛完成。
- [x] 使用者完成 Walk Transition／Mapping／Model 接線、修正 V1 `moveSpeedSource`，並通過 C1 檢查與 Play 驗收。
- [x] Run Stop LU／RU 套用曲線驅動 Import preset 並以 X Bot、60 FPS 烘焙；來源、Loop、速度與腳相 Gate 通過。
- [x] C1.1 Run 以「強度選集合 → 共用腳相選片」擴充既有 Stop runtime；無黑板／State／Facade／MotionDriver 契約變更。
- [x] Stop 速度接縫首輪修正：Walk／Run 下界收緊為 `0.35／0.75`；`RunStop_RU` playback `1.2588`，將 `t≈0.117 s` 峰值壓至 Run 錨點速度。相位連踩另案處理，不混入本次數值修正。
- [x] 修正下界收緊後 Run Stop 不觸發：tier 使用 SmoothDamp 前的 release-entry 快照；輸出仍用 Tick 後速度，消除 60／120 FPS 首幀衰減造成的 Gate 漏判。
- [x] 補正 SmoothDamp 漸近邊界：Band 比較沿用 `Epsilon=0.001`，實際穩態 `0.74999994` 可命中 Run；回歸測試改跑真實 smoother，不再手塞理想 `0.75`。
- [x] 建立 Run Transition、Facade mapping 與 Model refs，執行 Unity EditMode＋Run Stop Play 驗收；速度接縫與觸發邊界已確認完成。
- [x] Walk 連踩根因修正：不再以 Mixer root 加權時間查腳相；Facade 通用唯讀查主導 child clock，Locomotion 依已選 tier 的 loop Bake Data 選片。無 Mixer 同步、黑板、State 或 MotionDriver 變更。
- [x] 主導 child clock Play 複驗：不再以錯 gait 時鐘選片，但固定起點仍造成明顯全身姿勢跳動；確認 R2 的 ≈0.24 週期殘餘誤差已達必須處理的程度。
- [x] WalkStop LU／RU Fade `0.15 → 0.25 s` Play A/B：全身瞬間變動仍明顯，確認停止調 Fade，固定起點 pose mismatch 必須由相位等待處理。
- [x] Walk Pending Stop：以 Stop 起始 FootPhase 連續值比對 Walk loop 烘焙鍵，等待下一個最近 authored 入場點；等待期維持 release-entry 移動，0.5 s fail-safe。只套 Walk，Run 零改動。
- [x] Unity EditMode＋Play 驗收完成：任意 Walk 腳相放開會先自然走到匹配點再 Stop；無全身瞬跳、無先慢後衝；重新輸入／Jump／Roll 均可立即取消。
- [x] 完成學習復盤 `LearningNotes/phase-c-forward-stop.md`：彙整資料來源、Import／批次烘焙 SOP、Runtime 邏輯、最終數值、架構邊界與踩坑；並回填 `docs/07` 已解決的 V1 與被否決的 FootIK 回讀舊描述。學習筆記不納入 `docs/00–NN` 工程規格編號。

### 本輪固定順序

1. **Catalog**：盤點 Kubold Start／Stop／Moving Pivot／Turn in Place；記錄 clip、速度級、方向、角度、`_LU/_RU`、位移與旋轉。
2. **Representative Import + Bake**：只先選 Left Stop／Right Stop／90° Pivot／180° Turn 四支代表資產。同批修正 `ComputeAverageSpeed` 第 0 帧哨兵值偏差，再重烘受影響 locomotion clips，避免第二次全面重烘。
3. **Bake Gate**：檢查 `SpeedCurve`、`RotationCurve`、`RotationFinishedTime`、`EndPhase`、`TargetLocalDirection`、`FootPhaseCurve`、`BakedDuration`。大角度不正確就先修 Bake，不寫 Runtime 補丁。
4. **Semantic Gate**：在 Unity 播放確認 `_LU/_RU` 是抬腳、支撐腳、移動腳或最後落定腳；不准以檔名猜 mapping。
5. **Architecture Review**：以真實資產表回答 §15 G1–G4，再裁決 `LocomotionModel` 內部 phase／獨立 Presentation FSM／Gameplay State。
6. **Minimal seam**：只定義足以讓第一個 Stop 案例跑通的 Request → Selection → Motion Execution 邊界；`LocomotionTransition*` 仍是暫稱。
7. **Forward Stop vertical slice**：先 Walk Left／Right，驗證停止邊沿、腳相選片、Facade 播放、重新輸入／Jump／Roll 中斷與回 Idle；通過後才加 Run／Sprint。
8. **Fold back**：實測後才同步 design-doc、子系統 spec、State Matrix／架構測試與 changelog。若改 ownership／hierarchy／cross-cutting contract 才提新 ADR。

### 待驗證的責任邊界

```text
Gameplay Authority（允許什麼）
  → Locomotion Transition Selection（Start / Cycle / Stop / Pivot / Turn）
  → Motion Execution（Procedural / Baked / Distance-Matched / Warped）
  → Animation Post Process（Foot IK / Foot Lock / Pelvis / terrain adaptation）
```

- Ability／Gameplay FSM 管允許、封鎖與中斷；**不管左右腳選片**。
- Transition Selection 管選哪段資料；Motion Execution 管如何套用運動，不得綁死。
- Foot IK v1 是 ground adaptation，**不等於 Foot Lock**；IK 在選片與位移決策之後。
- Motion Warping 只在 Combat／Traversal 有真實 world target 時進場，不替代一般 Stop 的 Distance Matching。

### 本輪明確不做

- 不一次建完 Start／Stop／Pivot／Turn 全部 Runtime。
- 不預建 Motion Warping／Distance Matching／Foot Lock 完整 framework。
- 不新增 `LeftStopState`／`RightStopState`，不把 FootPhase 寫成 gameplay 黑板跨帧狀態。
- 不改 Accepted ADR-003；新結論若與它衝突，先停下評審。
- 不導入 Motion Matching；它仍是 v2.0 研究支線。

---

## 🟡 驗收結果：輪 4／4.1／4.2（2026-07-27，**剩一項**）

> 程式／測試／文件已全部寫完（changelog v0.25–v0.26）。
>
> **✅ 已通過**：EditMode **95 條全綠**／UI 模式（M7）／暫停與解除（M8 ①②）／Alt 不會誤觸暫停（③）／兩模式交錯時游標不被誤鎖（④）／游標自癒（M9 ⑤）／**§7.4 零 GC 複驗＝0 B**（已記入 §7.4.6）。
>
> **🛑 已知限制（非程式問題）**：`Alt`+`Esc` 是 **Windows 系統快捷鍵**，交錯按會被 OS 攔截並丟出遊戲視窗。M8 ④ 已改測等價的反向順序（先 Esc 後 Alt）。選 modifier 型持續按鍵前務必先查 OS 保留組合——詳見 dev-spec §1.4。
>
> **⬜ 唯一未結項 → M8 ⑤ 的後半**：暫停中按 **Space**，看 Inspector 監視器最上方的 **`[Current State]`** 是否由 `IDLE` 變 `JUMP`。
> * **移動不會排隊已確認且屬結構保證**（連續型意圖每帧覆寫 ＋ B9 吃 `deltaTime = 0` 推不動），這半已結案。
> * **trigger 意圖是另一回事**：`FullBodyStateMachine.Tick` 沒有 deltaTime 閘門，`JumpState.CanEnter` ＝ `JumpRequested && IsGrounded`，兩者皆與時間無關——**程式碼層面沒有任何東西阻止暫停中切進 JumpState**。
> * ⚠️ **看狀態欄，不要看畫面**：解除暫停後那一下跳躍可能不顯眼，肉眼會漏。
> * **若沒轉移，要查出是什麼擋住的**——依賴一個不知道為何存在的保護，比沒有保護更危險。
>
> 以下接線步驟保留備查（AI 不碰 `.prefab`／`.asset`／`.meta`／場景）。

### A. Inspector 綁定

**A-1　既有的 `UiModeArbiterSource`（語意已變：toggle → hold）**
* 在 `Ui Mode Action` 上**新增一個 `Hold` interaction**，`Duration` 設 **0.25**
* 綁定本身不用動（仍是 `<Keyboard>/leftAlt`）

**A-2　新增暫停器（⚠️ 不要掛在角色 Root）**
1. 場景中另建一顆空物件（例如 `SystemsRoot`），掛上 **`GamePauseController`**
2. 其 `Pause Toggle Action` 綁 **Esc**（`<Keyboard>/escape`）。🔄（輪 4.2）獨佔一顆鍵，**不需要 `Tap` interaction**

> 🔄 **暫停已改綁 Esc**，與 UI 模式的 Left Alt 不再共用，所以原本「Tap 門檻 ≤ Hold 門檻」的相依**已解除**。⚠️ 若日後改回共用一顆鍵，那條相依會回來（且是正確性條件，不是手感調味）。
> 📌 **為什麼暫停器不能掛角色 Root**：`Time.timeScale` 是全域狀態，角色黑板／仲裁是單一角色的。理由完整版見 design-doc §4.9。

**A-3　新增游標擁有者（🔴 缺這顆會很明顯地壞掉，見下方警告）**
1. 同一顆 `SystemsRoot` 再掛上 **`CursorModeController`**
2. 把 **`Ui Mode Source`** 拖入角色 Root 上的 `UiModeArbiterSource`
3. 把 **`Pause Controller`** 拖入同物件上的 `GamePauseController`

> 🔴 **這顆缺席時：開場游標不會被鎖住，而且相機完全不會轉。** 因為 `ThirdPersonCamera.Start` 原本那行初始鎖定**已被移除**——留著它就是第二個 Cursor 寫入者，「唯一擁有者」會淪為文件上的說法。這是**刻意讓它大聲壞掉**，症狀一眼可見，不是靜默漂移。
> 📌 **為什麼游標要搬到應用層**：暫停成為第二個滑鼠模式後，兩個各寫各的會產生可重現的碰撞（暫停中按住再放開 Alt，游標會被鎖回去）。詳見 changelog v0.26 §5。

### B. Play 模式行為驗收（＝dev-spec §7.2-**M8**，另 §7.2-M7 的觸發方式已改為「按住」）

| # | 驗收項 | 預期 |
| --- | --- | --- |
| 1 | 按 **Esc** | 世界凍結（角色與動畫全停） |
| 2 | ⚠️ **關鍵項**：再按 Esc | 必須能解除，且 `timeScale` 回到暫停前的值。這條驗證 `Update` 在 `timeScale == 0` 下照跑——若失敗，暫停將無法解除，回報我處理 |
| 3 | **按住** Alt 超過 0.25s | 進 UI 模式（游標出現、相機停轉、角色 B9 收步），**且不會順便暫停** |
| 4 | 放開 Alt | UI 模式全部復原 |
| 5 | 🆕 **先按 Esc 暫停 → 再按住 Alt → 放開 Alt** | 游標**必須仍然可見**。這是單一游標擁有者的實證（其一收手不得解除另一個的要求）。<br>⚠️ **順序不可顛倒**：`Alt`+`Esc` 是 Windows 系統快捷鍵（切換視窗），OS 層就攔截、Unity 收不到，實測會被丟出遊戲視窗——**鍵位與 OS 撞號，非程式問題**（2026-07-27 實測確認） |
| 6 | ✅ 暫停中的 trigger 意圖 | **已結案（v0.27）**：追查發現「暫停中按跳躍不會跳」靠的是另一個 bug 的副作用（見下方 v0.27 專段），已改由 `GamePauseController` 的 `BlockInput` 正式關閉 |

---

## 🔴 待使用者操作：v0.29（M3.x-B Footstep 落地）

> 程式／測試／文件已寫完。**我沒有跑過 Unity，也沒有實測任何場景**——下列全部待你驗。

### A. 接線

1. 角色 **Root** 掛上 **`FootstepDetector`**（它會自己 `GetComponentInChildren<FootIKRig>()` 取 pose 管道）
2. `AudioLibrarySO` 資產新增兩列：**`LeftFootstep`** 與 **`RightFootstep`** → 各自綁 `AudioDefinitionSO`
   * ⚠️ 未註冊不會報錯，只會靜默無聲（`AudioLibrarySO.Get()` 回 null）——所以「沒聲音」的第一個懷疑對象是這裡

### B. 驗收（都需要實際聽）

| 分類 | 項目 |
| --- | --- |
| 地形 | 平地／斜坡／階梯——腳步聲時機是否跟得上動畫落腳 |
| 速度 | Walk／Run／Sprint——**Sprint 的高步頻不得漏拍**（這是刻意不用時間閘的理由） |
| 靜止 | Idle 站著不得有腳步聲；**原地轉向應該有** |
| 跳躍 | 落地只聽到落地聲、**不得同時有腳步聲**；落地後走第一步聲音正常（抑制不得破壞跨帧狀態） |
| 翻滾 | ⚠️ **本輪未特別處理 Roll**——腳蜷起時高度劇烈變化，可能誤觸發。若實測明顯，回報我處理 |
| 空中 | ⚠️ 同上，**未加 `IsGrounded` 閘門**（未經裁決的東西我不自行加）。若空中有腳步聲，回報 |
| 左右 | 左右腳是否分別觸發（可先給兩個明顯不同的音效分辨） |
| 順序 | 在 Hierarchy 把 `FootstepDetector` 與 `AudioController` 上下對調 → **行為必須完全不變** |

### C. 需要調的參數（`FootstepDetector` 的 Inspector）

三個數字都是**我猜的初值**，必然要依實際動畫調：

* `ArmDescentSpeed = 0.35`（上膛：腳底下降速度門檻 m/s）
* `FireDescentSpeed = 0.05`（擊發：下降慢於此值即落腳）⚠️ **必須明顯小於上膛值**，否則 Schmitt trigger 失效
* `MinLiftExcursion = 0.03`（最小抬腳行程 m）

漏拍 → 調低 `ArmDescentSpeed`；多餘的聲音 → 調高 `ArmDescentSpeed` 或 `MinLiftExcursion`。

### D. 回歸

* **EditMode**：99 ＋ 21 → **120 條**
* **零 GC**：新增了順序 6.5 的第二段迴圈，建議依 §7.4 SOP 複驗（設計上已守：陣列 Start 收集、索引迴圈、struct 值複製、無 LINQ／無字串／無 new）

---

## 🔴 待使用者操作：v0.28（M3.x-A Pose 管道擁有權）

> 輪 3 Footstep 的前置。**調查結論：ADR-003 D4 完全未被觸及**（維持 Accepted、不修改、不新增 ADR）。真正要修的只有 `FootIKPoseData` 的擁有權。
> **接線：無。** 本輪不新增元件、不新增 Inspector 欄位。

### A. 驗收（Play 模式，重點是「行為必須零變化」）

| # | 驗收項 | 預期 |
| --- | --- | --- |
| 1 | 平地／斜坡走跑，觀察雙腳貼地 | 與變更前**無可感知差異**（本輪只搬擁有權，演算法一行未改） |
| 2 | 跳躍／翻滾中的腳部 | 同上，無抽搐、無黏地 |
| 3 | Console | 無 `FootIKController 找不到 FootIKRig` 之類的新錯誤 |

### B. 回歸

* **EditMode**：96 ＋ 3 → **99 條**（A11 ＋ `FootIKTests` 兩條擁有權測試）
* 既有 `FootIKTests` 8 條純函數測試**必須維持全綠**（演算法未動）

### C. 這輪唯一的行為差異（誠實記錄，目前不可觀察）

場上沒有 `FootIKController` 時，`FootIKRig` 現在**仍會寫入** Pose 快照（先前兩條管道共用一個 `return`，缺 Controller 時連 Pose 都不寫）。目前 Pose 的唯一讀取方正是 Controller 本身，所以**看不出差別**；這麼改是為了讓 M3.x-B 的偵測器不會因為「場上剛好沒有 IK Controller」就靜默收不到資料。

---

## 🔴 待使用者操作：v0.27（兩個互相抵銷的 bug）

> 這一輪是 M8 ⑤ 追查出來的。**根因一個、症狀兩個**：暫停時 `Move(finalMovement * 0)` ＝ `Move(Vector3.zero)`，而 Unity 的 `isGrounded` 由「上一次 Move 有沒有向下撞到東西」決定 ⇒ 零位移回報 false。於是①解除暫停時 `JustLanded` 假觸發（落地聲）②暫停中 `IsGrounded` 恆 false 讓 `JumpState.CanEnter` 失敗（那個「不知道為何存在的保護」）。**修掉①會讓②的保護消失**，故兩件同批修。完整推導見 changelog v0.27。

### A. 接線（🔴 缺這步跳躍缺口仍開著）

1. 角色 Root 的 `CharacterPipelineRunner`，新欄位 **`External Arbiter Sources`** 陣列 Size 設 **1**
2. 拖入場景中的 **`GamePauseController`**

> 沒拖的話：暫停中按 Space 會**真的**切進 `JumpState` 並卡住（`_airborneTimer` 在 `deltaTime = 0` 時不前進 ⇒ `IsLanded` 永遠 false ⇒ 退不出來），解除暫停後起跳。

### B. 驗收（＝dev-spec §7.2-M8 ⑥⑦⑧）

| # | 驗收項 | 預期 |
| --- | --- | --- |
| 1 | **站在地上**按 Esc 暫停 → 解除 | **不得聽到落地聲**（修復前必響） |
| 2 | 站在地上暫停 → 按 Space | Inspector 的 **`[Current State]` 必須維持 IDLE**；解除後也不得起跳。⚠️ 看狀態欄不要看畫面 |
| 3 | 跳到最高點暫停 → 解除 | 角色從原地續墜，落地聲在**看得見的下墜之後**才響（這是正確行為，不是 bug） |
| 4 | 一般移動、跳躍、翻滾 | 手感與 v0.26 完全一致（`IsTimeFrozen` 只在 `deltaTime <= 0` 生效，正常遊玩永不觸發） |

### C. 回歸

* **EditMode**：95 ＋ 1 → **96 條**
* ⚠️ `MotionDriver.IsTimeFrozen` **無法自動測**（需控制 `Time.deltaTime` 與真實 `CharacterController`），只能靠上表 1・3

**游標擁有權（＝dev-spec §7.2-M9，輪 4.2 新增）**

| # | 驗收項 | 預期 |
| --- | --- | --- |
| 7 | 開場 | 游標即被鎖住、相機正常轉動（＝`CursorModeController` 有接上，它接手了相機原本的初始鎖定） |
| 8 | 暫停期間 | 游標**常駐可見**（本輪需求） |
| 9 | 🎯 **關鍵回歸** | 暫停中按住 Alt 進 UI 模式 → 再放開 → **游標必須仍然可見**。舊架構在此會把游標鎖回去，正是本輪修的 bug |
| 10 | 兩個模式都退出後 | 游標回到鎖定 |
| 11 | 🆕 **外力自癒回歸** | Play 中讓 Game 視窗失焦再切回 → 游標必須在下一帧被拉回鎖定。**這是第一版 bug 的回歸測試**：初版快取「自己上次寫了什麼」，一旦 Unity 在背後解鎖（按 Esc、失焦都會）就永遠不再修正，游標永久可見。現版比對 `Cursor` 現值，故會自癒 |

> 📌 **副作用要知道**：因為現在每帧都會把游標拉回，Editor 內「按 Esc 逃出鎖定游標」的內建後門會被立刻收回。現行方案下不成問題（Esc 本來就是暫停鍵，暫停會正當地放開游標）。

### C. 回歸

* **EditMode 全綠**：83 條 ＋ `GamePauseControllerTests` 6 條 ＋ `CursorModeControllerTests` 6 條 → **95 條**
* ✅ **零 GC 複驗已完成**（2026-07-27）：本輪三處新增熱路徑（順序 4.5 `ArbiterPipeline.Tick`、`GamePauseController.Update`、`CursorModeController.Update`）實測維持 **0 B**，明細記入 **dev-spec §7.4.6**
* **Editor 錯誤複驗**：`CharacterPipelineRunnerEditor` 已改用 `RequiresConstantRepaint()`。確認 `GUIClips` 失衡與 `SerializedProperty has been Disposed` 兩條是否消失；**若仍出現**，把 `PlayerInputSource` 與 `UiModeArbiterSource` 兩顆元件在 Inspector 摺疊起來再測一次（可確認是否為 InputAction drawer），並考慮把 `UiModeArbiterSource` 移到角色的**子物件**（`GetComponentsInChildren` 照樣找得到）

---

## 🎯 下一會話：建議起手（2026-07-27 規劃）

> 📍 開場照舊：`docs/00-map.md` → 本段 → 只讀任務對應的 ADR／章節。

**先結掉上面 🟡 那段剩下的 M8 ⑤**（暫停中按 Space 看 `[Current State]`）；那一項會決定要不要順手補「暫停封鎖輸入」。

### 主推薦：輪 3 Footstep（**建議不照 roadmap 的 5 → 6 順序走**）

`docs/03-animation-roadmap.md` 排的是輪 5 Upper Body → 輪 6 Combat。**建議跳過輪 5，先做輪 3**，理由是這個專案自己的紀律：

* **輪 5 Upper Body Layer 現在沒有消費者。** 它的存在理由是「Combat 需要」，但 Combat 是輪 6。**先蓋基礎設施再等使用者，正是本專案一路刻意避開的事**——輪 4 的 `BlockInput` 讀取契約足足等了兩輪才等到真實 writer，等到時形狀是清楚的；Upper Body 沒有這個條件，現在做等於憑空決定「第二個 StateMachine vs Facade API」（roadmap §4-4 的未決點）。
* **輪 3 反而有一個已經在等的消費者**：`FootPhaseCurve` 在 v0.19 就烘進 4 支 loop（401·61·47·39 keys），**至今零消費者**。烘出來的資料沒人用，等於專案最核心的差異化敘事「自研烘焙管線 → 執行期表現」**還沒閉環過一次**。
* **附帶收益**：`AudioController` 的 Event → Definition → Library 三層目前只有落地音一個實例；Footstep 是第二個——就像輪 4 驗證了「讀取契約先行」，這會驗證三層查表是否真的可擴充。

**輪 3 要先裁決的點**（roadmap §4-2，不預答）：Animation Event 的承載選擇——`TransitionAsset` 序列化事件 vs 黑板單幀事件擴充，**哪類事件走哪條**。這條線畫錯會讓兩套機制長期混用。

### 順手可做，不需獨立輪次

**README 的 D 項 ＋ GIF。** 現在是寫「控制列表」最好的時機——控制方案這兩輪才真正補完（WASD／Ctrl／Shift／Space／Alt／Esc 六項齊了），在此之前寫都會過時。**GIF 仍是作品集首頁最大的單一缺口**，而素材已齊（0 GC 已驗、locomotion 手感已調、UI 模式與暫停可展示）。

### 明確不建議現在動

* **輪 5 Upper Body**：等 Combat 帶著真實需求進場（理由同上）
* **Phase C 停步分腿**：動畫品質收益最大，但會動 locomotion 核心手感，且要重烘——建議跟 🐛 `ComputeAverageSpeed` 0 值哨兵偏差（低估 1.6~2.6%）綁一起做，反正都要重烘一次

⛔ **明確沒做、也不要順手做**的（都是刻意延後，理由見 changelog v0.25 §6 與 v0.26 §7）：死亡 ArbiterSource、優先級／強制解封、鏡頭跳動抑制器、**Pause Menu／Canvas／EventSystem／UI navigation**、**暫停時封鎖角色輸入**、把 `CursorModeController` 的來源一般化成介面集合。

> ✅ **「Cursor service 抽象」已不在此列**——它於輪 4.2 落地（`App/CursorModeController`）。壓力在同一個工作階段就到了（「暫停時游標應常駐」），而且到來時形狀是清楚的。**這是一個「等真實壓力再抽象」奏效的正面案例**：若在輪 4.1 憑空抽，抽出來的很可能是埋著 LIFO 假設的「暫停自己存還原游標」版本（見 changelog v0.26 §5）。

---

## 🗂️ 已完成：輪 4 起手規劃（2026-07-26 規劃，✅ 已於 2026-07-27 執行完畢）

> 📍 開場照舊：`docs/00-map.md` → 本段 → 只讀任務對應的 ADR／章節。**不要**為了熟悉而整檔讀 dev-spec／design-doc。

### 主推薦：輪 4 ArbiterPipeline（順序 4.5）

**為什麼是它，而不是 Footstep／Phase C**：它是唯一一個**已經有具體需求在等、且卡著一個未決架構問題**的項目。

* **需求端已存在**：你的控制方案裡「**Alt ＝ 顯示滑鼠並停止移動**」還沒實作，而它正是 `BlockInput` 的第一個真實使用情境。
* **它會結掉 §7-M5 這個懸了兩輪的未決項**：「`BlockInput` 是否應同時凍結 `MovementIntent`？」現況是順序 2.5 刻意置於閘門外（維持 Migration 前行為），當時明寫「留待 ArbiterPipeline 真正有 writer 時一併裁決」。**現在有 writer 了，可以裁決了。**
* **黑板契約早就備好**：`ArbiterData{BlockInput, BlockIK, BlockAudio, BlockExpression}` 已在 §1.4，且 `A5` 的 WriterRules 目前把 `Arbitration` 標為「不得有任何執行期寫入者」——這一輪會是**第一次讓它合法擁有寫入者**，測試規則要同步更新（設計上刻意的摩擦）。
* 規模適中：一個 pipeline 階段 ＋ 一個裁決 ＋ 測試，不動 FSM 拓撲。

**開場要讀**：dev-spec §1.4（ArbiterData）、§2.1 順序 4.5、§7.2-M5、§7.3；design-doc §4.6（表現層管線既有骨架）。

**已知要一併裁決的三題**（別直接動手，先討論）：
1. `BlockInput` 該凍結哪些東西——trigger 意圖？`MovementIntent`？兩者語意不同（放開輸入 vs 凍結當下狀態），選錯會出現「封鎖瞬間角色定格」或「封鎖期間仍在滑行」。
2. 多來源封鎖的疊加（死亡／CC／過場同時要求封鎖）——現況是單一 bool，§2.4 舊規格提過優先級疊加，但那是 YAGNI 延後項，**先確認真的有第二個來源再做**。
3. 「顯示滑鼠」屬 Input 層還是 Arbiter 層？依 ADR-003 §13.3，游標狀態切換偏 Input／UI 職責，**不該讓 Arbiter 認識滑鼠**。

### 替代選項（若你想做動畫品質而非系統）

* **輪 3 Footstep**：FootPhaseCurve 在 v0.19 已烘進 4 支 loop，**至今沒有任何消費者**——這一輪會是它的第一個真實使用者，也會驗證「烘焙曲線 → 表現層事件」這條資料流。已有 `AudioController` 可擴充，規模小。
* **Phase C**：停步分腿姿勢（stop 動畫＋Foot Phase 選腳別）＋Starts/Stops/Turns。動畫品質收益最大，但也最大輪、且會動到 locomotion 的核心手感。

### 順手可做的小項（隨時，不需獨立輪次）

* README 稽核剩下的 D 項：**What works today**（跑起來會怎樣）、**控制列表**、**gait 數值來源鏈**、**GIF／截圖**。現在素材齊了（0 GC 已驗、locomotion 手感已調），**GIF 是作品集首頁最大的單一缺口**。
* GitHub Topics（目前空）、個人頁 Pin。
* 🐛 `ComputeAverageSpeed` 的 0 值哨兵偏差（低估 1.6~2.6%）——修正要全面重烘，**建議跟下次「反正要重烘」的輪次綁一起做**（例如 Phase C 導入新 clip 時）。

---

## 🏁 里程碑檢查點（2026-07-26，changelog v0.19 補記）

**v0.19 Foundation ＋ GaitProfile ＋ Run 預設型態 ＋ Animation-independent gameplay core ＋ Runtime baked data** 五項齊備，這條線第一次全程走通：

```
InputAction → InputData(ref struct) → PlayerLocomotionPolicy(+GaitProfileSO)
  → MovementIntent{強度[0-1], 方向, WalkModeActive}     ← 模型無關契約
  → LocomotionModel(B9 平滑 → Movement Output，自驅 SetFloat)
  → FSM(問 IsProducingMotion) → MotionDriver → CharacterController
```

**磁碟驗證的收案狀態（門檻於 2026-08-20 依手感再校正）**：`Locomotion.asset` 4-tier（`0/0.35/0.75/1`）／`moveSpeedSource`→`Bake_SprintFwdLoop`／4 支 loop 的 `FootPhaseCurve` 已補（401·61·47·39 keys）／`Gait_ActionRPG`（0.75／1.0／0.3651／toggle）／`Bake_Stand To Roll.BakedDuration` 2.3666668／**EditMode 76 綠（歷史實測值，本輪尚待重跑）**。

**仍未達成（勿當成已完成）**：`SourceClip` 欄位仍讓 clip 被打包載入（只是邏輯不讀）／0 GC 無 Profiler 存證／`MovementContext` 未實作／7 顆 Bake 的 `BakedDuration` 為 0（刻意延後）／`ComputeAverageSpeed` 低估 1.6~2.6%。

---

## Runtime → AnimationClip 依賴切斷（2026-07-26，changelog v0.23）——✅ 完成並驗證（76 綠）

起因：README 要宣稱「Kubold 只是 sample content」，先做了一次沿實際程式的 animation-independence 追蹤，抓到全專案唯一一條執行期 clip 耦合（`MotionBakeData.Duration => SourceClip.length`）。

### 已完成（修改 3 檔＋測試 3 條）
- `MotionBakeData`：新增序列化 `BakedDuration`；`Duration => BakedDuration`；`SourceClip` 註記為 Editor-side provenance。
- `MotionBakeEditor.SaveAsset`：烘焙時 `asset.BakedDuration = sourceClip.length;`
- `RollState`：退化條件改看**值**（`> 0`）而非引用；新增「資產未重烘」的 Editor 警告。
- 測試 73 → **76**（`Duration` 不依賴 clip／舊資產如實回 0／**Roll 無時長時不得秒退**）。

### ✅ 使用者側已完成
- **只重烘 `Bake_Stand To Roll`**（唯一有 `Duration` 消費者的資產）→ 翻滾恢復正常。
- **EditMode 76 條全綠。**

### 📌 刻意延後：其餘 7 顆 Bake 資產（決策，非遺漏）
其餘 `Bake_*.asset` 的 `BakedDuration` 目前為 **0**，**用到時再烘**（例：做狀態銜接而開始用 `Bake_Jump` 時，順手重烘該顆）。

依據：目前 `Duration` 的消費路徑**只有 Roll 一條**（`RollState.OnEnter` ＋ 它唯一呼叫的 `MotionDriver.ExecuteBakedCurveMovement`），其餘資產無人讀 `Duration`——`moveSpeedSource` 讀的是 `AutoAverageSpeed`、Jump 讀的是 `Auto*` 純量，兩者都已存在且正確。

> ⚠️ **這個延後帶著一個已知風險，別忘了**：日後若有**新的**消費者開始讀某顆未重烘資產的 `Duration`，它會拿到 0，而**目前只有 `RollState` 有「值 > 0」的退化閘門與 Editor 警告**，其他消費者沒有。
> 兩個處理選項（都不急，用到再說）：①新消費者上線時順手重烘該顆；②若這類消費者變多，就在 `MotionBakeData` 加一個 `#if UNITY_EDITOR` 的 `OnValidate` 警告，讓「未重烘」在資產層就現形，不必每個消費者各寫一次閘門。

---

## Repo 門面：README ＋ LICENSE（2026-07-25）——檔案已建，待你 commit

起因：repo 為 Public 且定位作品集，但 ①`LICENSE` 缺席＝保留所有權利，與「未來可抽取的開源套件」定位矛盾；②第三方資產已從歷史清除 → **fresh clone 無法編譯**，沒有 README 的訪客只會看到一個編不起來的專案。這是唯一一項「愈晚做代價愈高」的待辦。

### 已完成（AI 只建檔，git 由你執行）
- **`LICENSE`**：MIT，`Copyright (c) 2026 Baka8787`。**未修改 MIT 原文**（改授權條文是壞習慣）；第三方資產的排除說明放在 README 的 License 段。
- **`README.md`**：英文摘要 3 段（作品集門面）→ 專案定位 → 架構主張表 → **Mermaid 資料流圖**（GitHub 原生渲染）→ ADR 索引 → 專案結構 → 文件導覽 → **測試段（A1~A10 逐條說明「架構不變量是可執行的」）** → ⚠️ 第三方資產需求 → License。
- 順帶把 `docs/01`／`docs/02` 的文件標題從 `CharacterController` 改為 **`IntentPipeline`**（與 repo 名一致）。

### ⚠️ 待你確認／執行
1. **審 README 內容**：特別是「第三方資產需求」表（Animancer 走 `Packages/com.kybernetik.animancer/` 本機 UPM、Kubold 走 `Assets/MovementAnimsetPro/`）與英文摘要的措辭。
2. **commit ＋ push**（AI 不碰 git）。
3. 記憶清單剩餘兩項：**GitHub Topics**（目前空）、**個人頁 Pin**。

---

## Walk 型態 hold／toggle（2026-07-25，changelog v0.22）——✅ 測試已通過

落地第一套完整控制方案（參考終末地）：**WASD 預設 Run／Ctrl 切換 Walk 型態／Shift 閃避／Space 跳躍**，sprint 由 buff 驅動（未來）。**無架構變更**——沿用 ADR-003 D5 既有裁決，未開新 ADR。

### 已完成（修改 6 檔＋測試 4 條）
1. **`InputData.WalkButtonDown`**（邊沿）：與既有 `WalkButtonHeld` **並存**，raw input 層不預設控制方案。
2. **`MovementIntentData.WalkModeActive`**（mode state 進黑板，D5／§9-L5）：語意＝「型態開著沒有」，非「鍵按住沒有」。
3. **`GaitProfileSO.walkIsToggle`**：hold／toggle 成為**資產可配置項**——換玩法＝換資產的承諾對「操作語意」也成立。`ResolveIntensity` 第三參數改名 `walkHeld`→`walkActive`。
4. **`PlayerLocomotionPolicy`**：讀黑板 → 邊沿翻轉 → 寫回黑板，**零私有欄位**。
5. **Editor 監視器**：新增 `Walk Down（邊沿）` 與 `Walk Mode Active（型態）` 兩列，toggle 行為肉眼可驗。
6. **測試 69 → 73**（hold 鏡射／toggle 翻轉閂住／toggle 不看 Held／狀態不得殘留在 producer）。

### ✅ 使用者側已完成
- 建立 gait 資產、綁 `WalkAction` → Left Ctrl、勾 `walkIsToggle`；**EditMode 73 條全數通過**。
- **依手感調整數值**：`walkIntensity` 0.2651 → **0.3651**、`defaultIntensity` 0.574 → **0.75 以上**。
  - **這是安全的**：`threshold = speed_i/speed_max` 讓任意 intensity `p` 的混合動畫速度恆為 `p × speed_max`、與位移速度恆等 → **不會滑步**。偏離基準值只代表「刻意選了一個混合姿態」（walk≈走/跑之間、default≈跑/衝之間），不是校準錯誤。
  - 這條釐清已寫進 dev-spec §3.1（GaitProfileSO 紀律列）與 §7-M4——**公式綁的是 threshold，不是 intensity**，先前兩者被混在同一句話裡。

### 📌 若還想更快（第 3 階，需重烘）
把第三 tier 換成 Kubold 的 Fast Run clip（`Bake_Fast Run.asset` 已存在但缺 `AutoAverageSpeed`，須重烘），threshold 依公式重算。**禁止**調 `MotionDriver.moveSpeed` 或勾 `overrideMoveSpeed` → 那才會全域滑步（§9-L4）。

### 🐛 待修（低優先，需重烘全部資產）
1. `MotionBakeData.ComputeAverageSpeed` 把第 0 帧那支人造的 0 值算進算術平均 → 代表速度**低估 1.6%~2.6%**（Run 真值 3.578 記為 3.502）。修正＝跳過該支哨兵值，但會改變所有已烘值，需重烘一輪。
2. ~~🆕 **`MotionBakeData.Duration` 是全專案唯一一條「執行期邏輯讀 `AnimationClip`」的耦合**~~ → ✅ **已於 2026-07-26 以修法 A 解決（changelog v0.23）**：新增序列化 `BakedDuration`（烘焙期自 `clip.length` 快照）、`Duration` 改讀它、`SourceClip` 降為 Editor-side provenance、`RollState` 退化條件由「引用是否為 null」改為「**值是否 > 0**」。測試 73 → **76**。**⚠️ 需重烘 8 顆 Bake 資產，見下方清單。** 原始診斷保留於下：
   ```
   MotionBakeData.cs:88   public float Duration => SourceClip != null ? SourceClip.length : 0f;
   RollState.cs:58        _rollTimer = _rollBakeData != null ? _rollBakeData.Duration : FallbackDuration;
   ```
   fallback 檢查的是 **asset 為不為 null**，不是 **clip 為不為 null** → clip 遺失時 `_rollTimer = 0`、**Roll 第一帧就結束**，而 `FallbackDuration` 永遠用不到。**是「Roll 秒退」的同型變體**（上次根因在 asset 層＝bakeMappings 未綁，已修；這次在 clip 層，守不到）。
   - **目前不會觸發**：Roll 的 clip 是 Mixamo `X Bot@Stand To Roll.fbx`、在版控內、GUID 穩定 → 屬**潛伏缺陷**非現行 bug。
   - **修法 A（推薦）**：烘焙時把 `clip.length` 序列化成 `BakedDuration`，`Duration` 改讀它（與 `AutoAverageSpeed` 同 pattern）→ `MotionBakeData` 自此**完全不需執行期持有 clip 引用**，`SourceClip` 降為 Editor-only 溯源欄位。這也是「可抽成 Unity Plugin」需要的形狀。代價：重烘一輪。
   - **修法 B**：只在 `RollState` 補 `Duration > 0` 判斷。三字元修補，但耦合仍在，下一個消費者會再踩。
   - **待裁決，本輪未動手。**

---

## ADR-003 Migration Stage 2（2026-07-25，changelog v0.21）——✅ 程式完成、Unity 已驗證（EditMode 綠）

**完成判準已達成：Runner 不再認識任何 locomotion 概念**（並由新測試 A9 守住不回流）。ADR-003 §9-L1 結案；本輪**未改 ADR**（零 Blocking Issue）。

### 已完成
1. **新增 `Core/Movement/Models/`**：`IMovementModel`（通用抽象，兩個進入點）＋ `LocomotionModel`（MonoBehaviour，持有 `LocomotionSpeedSmoother`、寫 Movement Output、自驅 `SetFloat`）。
2. **遷移**：B9 平滑＋運動輸出導出＋動畫參數驅動全數離開 Runner（`DeriveMovementParameters` 刪除、`SyncAnimation` 只剩 `Play`、兩個平滑時間欄位移到 model）。
3. **注入鏈**：Runner 解析 `IMovementModel` → `FullBodyStateMachine.Initialize(config, data, model)` → `BaseState.Initialize(config, model)` 發給所有 state。**唯一實例＝結構保證**（本輪最大陷阱的解法）。
4. **FSM 門檻**：Idle／Move 的 `CanEnter` 改問 `IsProducingMotion`；`OnUpdateMotion` delegate 給 model（D3）。
5. **測試**：新增 **A9**（Runner 不得出現 locomotion token）／**A10**（平滑持有者唯一）；`StateMachineTests` 改用 `FakeMovementModel`。67 → **69** 條。
6. **文件**：dev-spec v0.21（§0.2／§1.1／§2.1 含新增脆弱點第 6 條／§3.1 新節／§7.1 A4・A5・A9・A10／§7.2 M3／**§7.3 結案兩列**）、design-doc v0.21（§4.8 改寫＋Trade-off 補列）、changelog v0.21（並依分卷規則把 v0.18.5／v0.18.6 移入歸檔卷）、`docs/00-map.md` 補 Models 列。

### ✅ 使用者側已完成（2026-07-25 當日回報＋磁碟核對）
1. **角色 Root 掛上 `LocomotionModel`**（與 `CharacterPipelineRunner` 同一顆 GameObject → Runner 欄位留空、`GetComponent` 補洞成立）；Accel 0.12／Decel 0.18 ＝原 Runner 值。
2. **EditMode 測試綠**（含新增的 A9／A10）。
3. **v0.19 Foundation 資產收齊**：`Locomotion.asset` 4-tier（`0 / 0.265 / 0.574 / 1`、4 clip）＋ `MotionDriver.moveSpeedSource` → `Bake_SprintFwdLoop`（`AutoAverageSpeed` 6.1008）。
   - 📌 prefab 內序列化的 `moveSpeed: 5.66` 是**舊值不必手改**——`MotionDriver` 啟動時以來源代表速度覆寫（唯一寫入時機在啟動，非熱路徑）。
   - 這也解除了先前預警的校準風險：mixer 頂 tier（Sprint）與位移滿速現已同源。

### ✅ 已回報通過
1. **§7-M1 行為等價**（含 Stage 2 的兩個迴歸點：跳躍落地不滑步、Idle↔Move 無速度跳變）。
2. **§7-M2 Profiler 0 GC** —— ✅ **自檢級達標**（2026-07-26，changelog v0.24）：量測過程中**抓到並修掉一個真的 bug**——`EvaluateTransitions` 對介面型 `IReadOnlyList<T>` 做 `foreach`，`List<T>` 的 struct enumerator 被裝箱，每帧 40 B。改索引迴圈後**穩態 `PlayerLoop` = 0 B**。
   - **量測程序已寫成 SOP → `docs/02-dev-spec.md` §7.4**（量哪裡／排除什麼／兩級判定／實測數據）。
   - 狀態切換幀約 2.6 KB，已拆解定位為 Editor-only 的 `Debug.Log`（其中 2.4 KB 是 Unity 的 `StackTraceUtility`，非我們的字串），Release 編譯移除。**不是回歸。**
   - ✅ **達標複驗完成**（同日）：Development Build 穩態 `PlayerLoop` = **0 B**；Player 側無 `EditorLoop`、CPU 7.19ms／記憶體 499.6 MB（Editor 為 31.65ms／3.38 GB）。**README 的零 GC 已升為「已驗證（Player 實測）」**——這是整份 README 唯一一條有量測數據撐著的宣稱。
   - ⚠️ **待你存檔**：把那張 Player Profiler 截圖存成 **`docs/images/profiler/gc-alloc-zero-player-walk.png`**。dev-spec §7.4.5 已用 `![]()` 內嵌、README 已連結它——**存檔前這兩處會是破圖**。
   - 🆕 `.gitignore` 新增 `/[Bb]uilds/`（原本的 `# Builds` 段只擋副檔名、擋不到資料夾，你的 `Builds/` 有 173 MB）。**證據進版控、產物不進。**
3. **changelog v0.19（Foundation 收案）** → ✅ **已於 2026-07-26 補寫**，並升格為里程碑檢查點（見下）。

---

## 文件結構優化（2026-07-25，changelog v0.20.1）——已完成，**無待辦**

起因：v0.20 完成後量測發現單一功能任務讀掉全專案 23%，讀取放大率 5×～40×。四項措施全數落地：

1. **changelog 分卷**：主檔只留最近 4 版（819 → 169 行），其餘進 `docs/changelog-archive.md`（一字未改）＋卷末版本索引表。**新增版本一律寫主檔頂端；主檔超過 4~5 版時把最舊的搬進歸檔卷。**
2. **新增 `docs/00-map.md`（45 行）**：模組 → 檔案 → 治理章節單頁索引＋「常見問題最短路徑」表。**維護規則：只記指標、不記細節。**
3. **dev-spec 分卷**（1,169 → 1,018 行）：§3.5 Foot IK → `docs/05-foot-ik.md`；§3.2 動畫呈現三小節 → `docs/06-animation-presentation.md`。**逐字搬移、章節編號原樣保留、原位留 stub → 既有引用零改寫**（全 docs 連結掃描 3/3 有效）。
4. **CLAUDE.md 新增 `Context Discipline` 章**：閱讀協定／Test-as-Spec 原則／Explore subagent 授權；並**明文推翻 2026-07-21「不回頭拆既有文件」規則**（附推翻依據與三條資格條件：已凍結、非跨領域契約、逐字搬移保編號留 stub）。

> ⚠️ **對你的唯一影響**：查 Foot IK 規格改看 `docs/05-foot-ik.md`（章節仍叫 3.5.x）；查 Animancer／Mixer 規格改看 `docs/06-animation-presentation.md`。dev-spec 原位置都有 stub 指路，不會找不到。

---

## 今日進度（2026-07-25）——ADR-003 Migration Stage 1（程式完成，待 Unity 驗證）

詳見 `docs/changelog.md` v0.20。**本輪未改 ADR-003（零 Blocking Issue）**；不新增 gameplay 功能、不提前實現 AI／Network／Vehicle。

### 已完成
1. **Stage 0 對照盤點（唯讀）**：ADR-003 D1~D5 全條款 ↔ 現有程式，三態標註（已存在相符／尚不存在／存在但形態不符）。結論：契約可完整映射，`Runner.ProcessParameters` 與 B9 的錯置屬 **ADR 自列的 §9-L1 Stage 2 遷移項**，非衝突。
2. **Stage 1 落地**（新增 5 檔／修改 5 檔）：`MovementIntentData` 黑板 region ＋ `IMovementIntentSource` ＋ `PlayerLocomotionPolicy` ＋ `GaitProfileSO` ＋ `LocomotionSpeedSmoother`（B9 抽成純運算 struct＝Stage 2 遷移單位）；管線新增**順序 2.5**；`InputData` 加中性 `SprintButtonHeld`／`WalkButtonHeld`。
3. **架構回歸檢核清單** → `docs/02-dev-spec.md` **§7**（A1~A8 自動／M1~M6 人工，各標實施方式），自動項實作為 **`ArchitectureRegressionTests`（A1~A5）** ＋ **`MovementIntentTests`（A6~A8）**，新增 **20 條**（5＋15）。
4. **文件同步**：dev-spec v0.20（§0.2／§1.1／§1.3／新增 §1.5／§2.1／§3.1／新增 §7）、design-doc v0.20（§4.1／§4.2／新增 §4.8／Trade-off 兩列）、changelog v0.20。

### ⚠️ 待使用者（Inspector／Play／Git——AI 不碰）
1. **【必做，否則角色不會動】在角色 Root（`X Bot` Prefab，掛 `CharacterPipelineRunner` 那顆）加上 `PlayerLocomotionPolicy` 元件。** Runner 的 `Movement Intent Source Component` 欄位可留空（Awake 會自動 `GetComponent` 補洞）；未掛則 Play 時 LogError 且 `MovementIntent` 恆 0。
2. **重編＋跑 EditMode 測試**：預期 0 error、**67 條全綠**。⚠️ 順帶更正文件漂移：先前紀錄的「42 條」已過時——磁碟實際 `[Test]` 為 **47** 條（無參數化測試），故本輪後為 47＋20＝**67**。**以 Test Runner 實跑數字為準**，若與 67 不符請回報。
3. **Play 行為等價驗收（§7-M1）**：**先不要建 `GaitProfileSO` 資產** ——留空時強度＝原始推桿量，手感應與本輪之前**完全一致**（加速平順、放開滑行收步、無滑步）。若有差異即為 regression，回報而非調參。
4. **（可選，行為等價驗收通過後再做）啟用 gait 方案「預設 Run／Shift=Sprint／Ctrl=Walk」**：
   - `PlayerInputSource` 新增的 `Sprint Action`／`Walk Action` 綁 Left Shift／Left Ctrl。
   - 建 `GaitProfile.asset`（`Assets/ScriptableObjects/Movement/`，選單 `Project/Core/Movement/GaitProfile`），拖進 `PlayerLocomotionPolicy`。
   - Gait intensity 是手感輸入，不再強制等於動畫天生速度比；目前採 default=0.75、sprint=1、walk=0.3651。Mixer Threshold 另採 0.35／0.75／1，並以派生 PlaybackSpeed 對齊實際速度。
5. **Profiler 0 GC 複驗（§7-M2）**：熱路徑新增的是值型別運算，預期 0 B，但仍請實測確認。

### 下一步（擇一）
- ~~**A. Stage 2（B9／MoveSpeed 歸位，收 §9-L1）**~~ → ✅ **已完成（2026-07-25，changelog v0.21）**，見本檔最上方專段。實作時發現 ADR 未預想的兩個時序陷阱（Jump 期間 dynamics 不可凍結／`SetFloat` 不可落 LateUpdate），故 model 採兩個進入點、順序 3 保留。
- **B. 先做 Foundation 收案（v0.19）／Phase C**：見下方前一輪交辦（⚠️ 收案狀態與磁碟不符，見最上方「待釐清」）。
- 📌 Stage 3（`MovementContext`、AI／Replay／Network producer、`CombatIntent`）**待真需求**，勿提前。

---

## 🔖 前一輪交辦（Foundation／Foot IK，仍有效）

> 本 session 量大（Foot IK 收案＋Locomotion Foundation＋B9＋Movement Policy ADR-003＋第三方屏蔽）。**先讀這段**；細節見 `docs/04-locomotion-foundation.md`、`docs/ADR/003-*`、下方各進度段。

### 已完成（本 session，程式全綠）
- **Foot IK v1 收案**（輪 1，changelog v0.18.7）
- **輪 2 Foundation 程式**：Foot Phase Curve stage（`MotionBakeData`+`MotionFeatureAnalysis`）／per-clip 版 `MotionClipImportSOP`／**B9 MoveSpeed 平滑**（`CharacterPipelineRunner`）＋5 新測試
- **Kubold 盤點**（docs/04）＋Import/Bake loops（速度真相 Walk 1.62／Run 3.50／Sprint 6.10 m/s）
- **Movement Policy 四輪對抗式評審**（docs/04 §11–14）→ **`docs/ADR/003-movement-intent-layering.md`**（Accepted＝契約定案、程式未實作；含 §13 四點責任邊界）
- **`.gitignore` 加第三方資產排除**（本段最後任務，SOP 見下）

### 待使用者（Inspector／Git／實測——AI 不碰）
1. **第三方資產屏蔽 SOP**（↓ 專段，你執行 git）
2. **Foundation 資產**：docs/04 §10 — `Locomotion.asset` 擴 4-tier（Idle 0／Walk 0.265／Run 0.574／Sprint 1.0；Sync 開 Walk/Run/Sprint）＋`MotionDriver.moveSpeedSource`→`Bake_SprintFwdLoop`；**重烘 4 支 loop** 補 FootPhaseCurve；Play 驗（按 W 平順加速無滑步）
3. **鏡頭**：角色 Root 拖入 Main Camera 的 `Third Person Camera.Target`、`Mouse Sensitivity` 2→0.1

### 待裁決／下一步（擇一起手）
- **A. Movement Intent Migration Stage 1（動程式）**：審 ADR-003 → 核可 → 落地最小 seam（`MovementIntent` region＋`IMovementIntentSource`＋`PlayerLocomotionPolicy`＋`GaitProfileSO`；行為等價＋順帶落地最初想要的「預設 Run／Shift=Sprint／Ctrl=Walk」）。**Stage 1 紀律：`MovementIntent` 唯一真相、`MoveSpeed` 過渡衍生值（ADR §13.4）**
- **B. Phase C**：停步分腿姿勢（stop 動畫＋Foot Phase 選腳別）＋Starts/Stops/Turns 導入（烘焙曲線驅動＝Roll 先例，per-clip 套 preset）＋承載定案
- 停步姿勢＝loop 無收步語意、非 Blocking，**建議歸 Phase C**（待確認）
- **changelog v0.19（Foundation 收案）** 待 Play 綠燈補

### 🚫 第三方資產屏蔽 SOP（你執行；AI 不碰 git）
現況：Animancer Pro／Kubold／StarterAssets **已被 git 追蹤**（~224MB），`.gitignore` 已加排除但**已追蹤檔需手動取消追蹤**才生效。
```bash
# 0) 最關鍵：確認 repo 為 PRIVATE（公開才觸發 EULA 二次散佈問題）
# 1) 取消追蹤（本機檔案保留、Unity 照常運作；只從 git index 移除）
git rm -r --cached "Packages/com.kybernetik.animancer"
git rm -r --cached "Assets/MovementAnimsetPro" "Assets/MovementAnimsetPro.meta"
git rm -r --cached "Assets/StarterAssets" "Assets/StarterAssets.meta"   # StarterAssets：確認專案不依賴再做
# 2) commit
git commit -m "Untrack third-party paid assets (Animancer Pro, Kubold); enforce via .gitignore"
```
- ⚠️ **歷史殘留**：上述只停「未來」追蹤；資產仍在**過去 commit 的歷史**裡。solo private repo → 保持 private 即足夠。**若曾公開／要公開** → 需 `git filter-repo` 重寫歷史清除（destructive，先備份）。
- ⚠️ **fresh clone 不可編譯**：Animancer＝執行期核心依賴、Kubold＝Bake 資產 GUID 引用來源。**建議 repo 加 `README` 註明必要資產與各自重匯入方式**（要我下輪寫可講）。
- X Bot／Mixamo（角色本體，免費但 Adobe 條款）：**不建議排除**（全場景依賴，破壞成本 > 低風險）；如在意另議。

---

## 今日進度（2026-07-21）——Foot IK v1 收案輪（輪 1）✅

roadmap `docs/03-animation-roadmap.md` §1.4 收案清單執行完畢（詳 changelog v0.18.7）：

1. **程式碼**：`FootIKController.ResolveFoot` 旋轉公式還原基線「保留俯仰式」（`FromToRotation(worldUp, n) × poseRot`；A/B 軸對齊式歸檔）；`FootIKRig` 刪 `debugLogGoals` 臨時診斷段。
2. **文件同步**：changelog v0.18.7（樓梯 collider 根因／A/B 結論／設計哲學／v1 凍結宣告）；design-doc §4.6 補 Foot IK 設計哲學；dev-spec §3.5 補 v1 凍結狀態＋已知限制表 L1~L6、§3.5.3 首查項標否證、版本表補 v0.18.7（順修重複／錯置的 v0.18.3 列）。
3. **Foot IK v1 凍結**：架構健康、6 條已知限制（L1~L6）文件化於 dev-spec §3.5.2；品質升級改由 `docs/03` roadmap 承載。主線下一步＝**輪 2 Locomotion 資產升級**（＋Foot Phase 烘焙 stage＋B9）。

---

## 前次進度（2026-07-18，已收案 → changelog v0.17／v0.18）

三輪連發，詳見 changelog v0.17／v0.18：

1. **M2 Presentation Pipeline + Landing Audio ✅ 收案**（changelog v0.17）：修復前 session 幻覺殘局 → `JustLanded` 落地（YAGNI 閘門走完）＋`PresentationPipeline` 骨架（順序 6.5）＋Audio 三層（Event→Definition→Library）；Play 實測落地音正常。附 EditMode Warning 治理（RollState/JumpState 防線 `isPlaying` 語義精確化；測試契約輸出用 LogAssert.Expect＋鬆耦合 Regex）。
2. **M1 Locomotion ✅ 正式收案**（changelog v0.17 §5）：DoD 五項全過（0 error＋測試全綠／Play 實測／Profiler 0B／moveSpeedSource 接 Bake_Fast Run／Roll fade 資產真相驗證）。Locomotion 基線固定。
3. **M3 Foot IK 實作輪 ✅**（changelog v0.18）＋**M3.1 反饋迴路修正 ✅**（changelog v0.18.1）：實測腳踝抽搐 → Review 定位根因（Controller 採樣骨骼＝上一幀 IK 輸出，旋轉追逐＋權重鎖死雙迴路）→ 裁決雙管道修正——`FootIKController`（Root 決策，對 Animator 零依賴）⇄ 兩條單向管道（`FootIKTargetData` Controller 寫／`FootIKPoseData` Rig 寫）⇄ `FootIKRig`（Model，**Presentation Adapter**）。手填 footHeight 改讀 avatar `FeetBottomHeight`。抽搐複測通過、M3.5 基線已 push（2026-07-18）；**v1 已於 2026-07-21 凍結**（見頂部收案輪）。

---

## 待使用者作業

- **重編確認**：Unity 重編 0 error＋EditMode 測試 **42 條**全綠。收案輪程式改動＝旋轉公式一行還原＋刪 Editor-only 診斷段，不涉純函數／測試契約。
- **孤兒序列化值**（無害，Unity 靜默忽略）：`X Bot.prefab` 殘留 `debugLogGoals` 序列化值——同 v0.18.6 移除 `Enable*` flag 的既定情形，可在 Inspector 順手清、不清亦無影響。
- **資產側**（AI 不碰，SOP 由你在 Editor 執行）：牆壁 collider 過胖修正（身體碰不到牆）；CapsuleFitter Apply Prefab 確認；floor Scale Z 翻正（-25.153 → +25.153）若未做。**樓梯 collider 已修 ✅**。

---

## 工作清單

### Done（2026-07-18）
- [x] M2 全流程（黑板單幀事件 → 6.5 → Audio）＋收案；M1 DoD 收案；Warning 治理兩輪
- [x] M3 Foot IK：3 新檔＋Facade IK 通道＋`FootIKTests` 8 條＋Living Docs v0.18

### Doing
- [ ] **🔬 Movement Policy 設計探索（`docs/04` §11 分析 ＋ §12 Architecture Review，純分析未改程式）**：發現目前**無速度模式選擇層**（`InputData` 無 modifier、`ProcessParameters` 寫死 `magnitude→MoveSpeed`）。§11 初提 MovementProfile＋Resolver；**§12 自我挑戰後部分推翻**——原案 overfit（1D-speed、擴不到 strafe/swim/vehicle）、seam 綁 input（netcode/AI 敵對）、DIP 弱。**修訂設計（§12.3）**：seam 上移黑板中性 **`MovementIntent`**＋介面化 **`IMovementIntentSource`**（player/AI/replay 可換）＋**model 走既有 `OnUpdateMotion` seam**＋gait profile 收窄＋mode/toggle state 進黑板。務實 staging：現在只放最小正確 seam，其餘加法。停步分腿姿勢＝loop 無收步 → Phase C（stop 動畫＋Foot Phase）。**§13 Architecture Validation（Runtime Data Flow Diagram）已完成**——畫圖時再修 3 點：R1 MovementIntent＝模型無關 intensity+dir（非 gait）、R2 B9 屬 Locomotion model（現況在 Runner＝待遷移殘餘耦合）、R3 producer context-free（無循環）。6 問驗證全過（ownership 單寫/lifetime snapshot-able/DIP 反轉/唯一無害 1-frame 回饋/seam 模型無關）。**§14 Design Review R2**：使用者再挑戰，抓出 3 條混淆軸線（皆成立）——①Movement Model（context 軸）≠ Gameplay State（action 軸），正交、需獨立 `MovementContext` resolver；②Blackboard 應 domain-partitioned intents（MovementIntent/CombatIntent/InteractionIntent）非單一 god-Intent；③MoveSpeed 屬 Locomotion model 內部、各 model 自驅動畫參數走通用 Facade（Facade 本身即抽象、**不需** IAnimationModel）。**§14.6/14.7 v3 圖（三軸分離）已重畫並複驗——無新裂縫、設計收斂**：Ownership/Lifetime/R-W/DIP/循環/耦合 六項全過；唯一殘餘＝B9 在 Runner（列 ADR known-migration）；補 nuance＝ambient state(Idle/Move) delegate model、intrinsic-motion state(Roll/Jump/Attack) 本就 override OnUpdateMotion（既有機制）。**✅ `docs/ADR/003-movement-intent-layering.md` 已撰寫**（Status/Context/Problem/Decision 5 契約/Diagram/Responsibility Matrix/Alternatives〔完整保留否決 BaseState-Shift／MovementModeResolver／Input-Modifier 三案理由〕/Trade-offs/Consequences/Known Limitations L1-L6/Migration Plan Stage 0-3+/Future Extension）。狀態＝Accepted（契約定案、程式尚未實作，比照 ADR-002）。**§13 補四點責任邊界**：①MovementIntent schema 僅適「方向性移動家族」非萬用（異質 model 開兄弟 schema）②MovementContext 描述性、不否決 State——Gameplay Authority 屬 Capability/Profile（how vs what's-allowed vs doing 三權分立）③Producer 不管 context-sensitive input，Input Routing 在上游(action map/Input Router)④Stage1 MovementIntent 唯一真相、MoveSpeed 僅過渡衍生值(禁繞過 intent 直寫)。**下一步待使用者核可 → Migration Stage 1（最小 seam：MovementIntent region＋IMovementIntentSource＋PlayerLocomotionPolicy＋GaitProfileSO，行為等價重構）**。實作時才更新 design-doc/dev-spec（ADR §10 文件責任）。停步歸 Phase C 待確認。
- [ ] **輪 2 Locomotion Foundation 進行中**（規劃 `docs/04`）。**已裁決**：四段 Idle/Walk/Run/Sprint（速度段數由資產決定，Jog 不硬補）、Humanoid retarget X Bot、承載延到 Foundation 驗證後。**已完成**：Import＋Bake（loop 速度真相有效——Walk 1.62／Run 3.50／Sprint 6.10 m/s；門檻 = speed/6.10）。**程式已落地**：Foot Phase Curve stage（`MotionBakeData`+`FootPhaseCurve`欄位/`GetFootPhaseAt`；`MotionFeatureAnalysis`+`FootPhaseCurveAnalyzer`+註冊）＋per-clip 版 `MotionClipImportSOP`（選子 clip 只套那幾支）。**SOP 誤用診斷**：主 FBX 全 clip 被灌 loopTime:1，但只波及未用到的非 loop clip（4 支 loop 完好）→ 不重下載，per-clip 工具已備供 Phase C。**待使用者**：重編 0 error → 重烘 4 支 loop 補 FootPhaseCurve → 我接 Mixer 擴充/Calibration。
- [ ] **鏡頭修復**：程式加了 Fail-Fast（target null 報錯）；**待使用者在場景**把角色 Root 拖入 Main Camera 的 `Third Person Camera.Target`，並把 `Mouse Sensitivity` 2→0.1。Cinemachine 為未來打磨選項（已裝 2.10.7），非本輪必要。

### Todo（輪 2，依 `docs/04` §7／§9 拆分）
- [x] Import Preset（loops）＋Bake（loops 速度真相）✅
- [x] Foot Phase Curve stage 程式（analyzer＋欄位）✅／per-clip SOP 工具 ✅
- [ ] 使用者重編 + 重烘 4 支 loop（補 FootPhaseCurve）
- [x] **Mixer 擴充 + Calibration SOP 已出（docs/04 §10）**——查證 `MoveSpeed=[0,1] × moveSpeed` 自洽，**零程式改動**（驗證資產決定規格原則）
- [ ] **使用者 Inspector 作業**：`Locomotion.asset` 擴 4 children（Idle 0 / Walk 0.265 / Run 0.574 / Sprint 1.0；Sync 開 Walk/Run/Sprint）＋`MotionDriver.moveSpeedSource` → `Bake_SprintFwdLoop`。Play：按 W 以 Sprint 6.10 前進無滑步（中間 tier 待 B9/analog）
- [x] **Phase D B9 參數平滑 ✅**（`CharacterPipelineRunner`：SmoothDamp 平滑 MoveSpeed＋減速保留方向；Runner-local、零 GC、FSM 零改動；手感 tunable moveSpeedAccel/DecelTime）→ **待 Play 實測調手感**
- [ ] Phase C Starts/Stops/Turns 導入（烘焙曲線驅動＝Roll 先例；per-clip 套 preset）＋**承載方式實測定案**

---

## Backlog / Future Work（超出目前範圍，不動手）

### Foot IK 品質路線圖 → 已凍結並移交 `docs/03-animation-roadmap.md`
- **v1 已凍結（收案輪，2026-07-21）**：架構健康＋6 條已知限制（L1~L6）文件化於 dev-spec §3.5.2；品質升級順序、技術分類、依賴關係全數移交 roadmap `docs/03`（輪 2 Locomotion 資產 → … → 輪 7 Foot IK v2 雙點採樣）。
- **~~首查項 GetIK* 值域~~ 已否證**：樓梯歪斜真凶＝斜坡 collider（環境資料錯誤，collider 修正後消失）；殘餘跨階腳掌穿模＝L1 單點採樣資訊量天花板，升級＝輪 7 Heel/Toe 雙點採樣。A/B 旋轉公式無感差、已回歸保留俯仰式（changelog v0.18.7）。
- ⚠️ 參考碼防搬運註記（仍有效，動 IK 前重讀）：其 raycast 從骨骼現值起打＝反饋污染（我們 M3.1 修掉的抽搐根因，快照 goal 起點勿退）；其 body 直接覆寫不適用（我們疊加式）；其漏設 RotationWeight 屬原 bug。骨盆模型重評（`bodyY − (minFootGoalY + legHeight)` 以腿長可達性直接建模）併輪 7 評估。

### 使用者明定 Future Work（M3 裁決重申：需要時一律 TODO，不得提前實作）
- **Foot Phase Curve**（烘焙腳相曲線；等 Footstep／Audio 輪一併評估 Mixer 混合取值）
- **Footstep Event ＋ Audio Integration**（腳步音；事件源設計與 Foot IK pose 採樣天然銜接）
- **BlockIK／BlockAudio Writer ＋ Mini Arbiter**（F6 ArbiterPipeline 範疇，順序 4.5 已預留）
- **Animation Rigging Package／Two-Bone IK Solver**（現用 Unity Humanoid IK，Q1 裁決）
- **Motion Warping**（`ApplyBakedCompensation` 已有雛形，無呼叫端）
- **F2 Strafe 2D Mixer**（等瞄準/鎖定移動需求）／**F3 Combat**／**F4 Upper Body Layer**

### 工具/演算法 Backlog（沿革見 changelog）
- **B1** `Mathf.DeltaAngle` 疑慮（≥360° 旋轉動畫進場時重評）
- **B2** 多段跳空中段前搖落地邊角（併 ADR-002 §6-4 後續）
- **B3** `PlayWithCallback` lambda 閉包 GC（仍無呼叫端）
- ~~**B4** Config bakeMappings 冗餘條目~~ ✅ 已收掉（使用者清理，現僅 Roll 一條）
- **B5** CapsuleFitter v2（骨骼推估）
- **B6** ValidateHierarchy 增補 Model identity 警告
- **B7** 前搖期間輸入未鎖手感（F6 範疇）
- **B8** Loop Pose 評估（走路循環有接縫時啟動）
- **B9** 動畫參數平滑（Game Feel 輪：SmoothDamp 落點裁決＋加減速曲線）
- **B10** Facade 映射鍵 Editor 驗證工具（低優先）
- **B12** Config 引用驗證（OnValidate 抓「條目存在但引用死」；JumpState/RollState 執行期防線已覆蓋主要風險）

---

## 建議下一步 → 權威輪次順序見 `docs/03-animation-roadmap.md` §3

- **輪 2（＝既定 M4）購入 locomotion 資產**（Movement Animset Pro 級別）→ 左/右腳停步、pivot、方向性起步＋foot-phase 資料設計（烘焙管線加 analyzer 即可）；一併做 Foot Phase 烘焙 stage 與 B9 參數平滑（資產定形後）
- **輪 4 ArbiterPipeline**（順序 4.5 兌現；BlockIK/BlockAudio writer 到位、§7/§8.3 旗標粒度屆時有真實案例可答）→ Combat 前置
- **輪 6（＝既定 M5）Combat 初版（ARPG）**→ 產生 Hit/Death 等真正需要「封鎖」的狀態（前置：輪 4 Arbiter＋輪 5 Upper Body Layer）
- 表情模組：暫緩（X Bot 無臉部 rig）
