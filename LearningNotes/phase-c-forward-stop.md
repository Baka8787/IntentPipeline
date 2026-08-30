# Phase C Forward Stop — 學習復盤

> **狀態**：2026-08-21，C1 Walk Forward Stop／C1.1 Run Forward Stop 已完成 EditMode 與 Unity Play 驗收。  
> **定位**：本檔記錄「怎麼走到最後答案」，供下一個 Start／Pivot／Turn 任務避坑；它不是新的規格真相。  
> **正式契約**：以 [`07-locomotion-transitions.md`](../docs/07-locomotion-transitions.md)、
> [`06-animation-presentation.md`](../docs/06-animation-presentation.md) 與
> [`ADR-003`](../docs/ADR/003-movement-intent-layering.md) 為準。

---

## 1. 結案成果

| 範圍 | 最終行為 |
| --- | --- |
| Walk Stop | `0.35–0.50` 入場強度內，等待下一個最接近 Stop authored 起點的 Walk 相位，再播 LU／RU |
| Run Stop | `0.75–0.875` 入場強度內，依當下 Run 腳相立即選 LU／RU；不套 Walk Pending |
| Sprint Stop | 沒有對應資產，不拿 Run Stop 冒充；維持既有 B9 `SmoothDamp` |
| Motion | Stop 播放期間由 `MotionBakeData.SpeedCurve` 經 `MotionDriver` 驅動 |
| 中斷 | 重新輸入、離地、Jump、Roll 都會讓 Stop 失效，不蓋掉新的 gameplay authority |
| 架構 | 不新增 State、不進黑板、不新增 Stop 專用 Facade API；`LocomotionModel` 是唯一 runtime holder |
| 工具 | 保留通用批次烘焙器；移除 Phase C／Kubold 專用的一次性選單與面板 |

```text
raw intent 放開邊沿
  → release-entry 強度選 Walk／Run 集合
  → Bake Data 選變體／入場時刻
  → AnimationFacade 播放表現
  → MotionDriver 套用曲線位移
  → model 統一仲裁完成、逾時與中斷
```

---

## 2. 資料來源

### 2.1 專案內的事實來源

| 資料 | 用途 | 注意事項 |
| --- | --- | --- |
| [`MovementAnimsetPro.fbx`](../Assets/MovementAnimsetPro/Animations/MovementAnimsetPro.fbx) 與 [`.meta`](../Assets/MovementAnimsetPro/Animations/MovementAnimsetPro.fbx.meta) | Walk／Run loop、Stop LU／RU 子 clip 與 Import 設定 | FBX 子 clip 是動畫內容唯一真相；不 Ctrl+D 複製 `.anim` |
| [`MAP_Unity_v1.5_animlist.pdf`](../Assets/MovementAnimsetPro/MAP_Unity_v1.5_animlist.pdf) | 原廠 Catalog 與命名參考 | 命名只能當索引，語意仍須 Preview＋Bake 驗證 |
| [`X Bot.prefab`](../Assets/Prefabs/X%20Bot.prefab) | Humanoid 採樣角色與最終接線 | Animator 位於子階層，不要求 Gameplay Root 自己掛 Animator |
| [`Assets/ScriptableObjects/Motion/`](../Assets/ScriptableObjects/Motion/) | 速度、位移、旋轉、腳相與 duration | Import 改動後必須重烘 |
| [`Locomotion.asset`](../Assets/ScriptableObjects/Animation/Locomotion.asset) | Mixer 門檻、child speed、同步開關 | 門檻是手感資料，不是 Bake Data 唯一決定的常數 |
| [`WalkStop_LU.asset`](../Assets/ScriptableObjects/Animation/WalkStop_LU.asset) 等 Transition | Fade、speed、start time | 表現調整住在 Transition，不修改 FBX |
| [`LocomotionModel.cs`](../Assets/Scripts/Core/Movement/Models/LocomotionModel.cs) | Request、tier、Pending、播放與中斷仲裁 | Stop runtime 唯一持有者 |
| [`LocomotionStopSelector.cs`](../Assets/Scripts/Core/Movement/Models/LocomotionStopSelector.cs) | 純函式 tier／變體／未來相位選擇 | 不讀檔名、不碰 Facade／MotionDriver |
| [`LocomotionStopRuntime.cs`](../Assets/Scripts/Core/Movement/Models/LocomotionStopRuntime.cs) | 值型別跨幀狀態與 generation | 不進黑板 |
| [`LocomotionStopTests.cs`](../Assets/_Project/Tests/EditMode/LocomotionStopTests.cs) | 行為與真實資產語意守衛 | 最便宜的行為規格入口 |
| [`ArchitectureRegressionTests.cs`](../Assets/_Project/Tests/EditMode/ArchitectureRegressionTests.cs) | A12–A15 架構守衛 | 鎖定黑板、State、Facade 與單一 holder |

可信度順序：FBX／`.meta` → `MotionBakeData` → Transition／Mixer／Prefab → Unity Preview／Play。
檔名與外部案例只能協助理解，不能凌駕本機資產證據。

### 2.2 外部原始案例

| 來源 | 吸收 | 沒有照搬 |
| --- | --- | --- |
| [Animancer Mixer Synchronization](https://kybernetik.com.au/animancer/docs/manual/blending/mixers/synchronization/) | 同步會調整子 Playable 速度，會干擾本專案 exact playback calibration | 沒有為腳相開同步，也沒讓 Core 讀 Mixer 私有結構 |
| [Animancer Events](https://kybernetik.com.au/animancer/docs/manual/events/animancer/)／[Sequence API](https://kybernetik.com.au/animancer/api/Animancer/Sequence/) | End callback 只當完成請求；用 generation 管 callback ownership | 沒改 Facade 成 Stop 專用 API |
| [Epic Distance Matching](https://dev.epicgames.com/documentation/en-us/unreal-engine/distance-matching-in-unreal-engine) | 若需指定距離停住，主變數應是剩餘距離而非線性時間 | 本輪接受 authored distance，不提前建 C5 |
| [Epic Motion Matching](https://dev.epicgames.com/documentation/en-us/unreal-engine/motion-matching-in-unreal-engine) | 先分類 Stopping／Foot Plant，再談 variant selection | 沒導入 pose database 或 trajectory query |
| [Naughty Dog GDC：Motion Matching in The Last of Us Part II](https://gdcvault.com/play/1027118/Motion-Matching-in-The-Last) | Stop 與 Foot Plant 不能和一般方向改變混成一題 | 只採分類思路，現行仍是明確列舉的兩變體 |

---

## 3. Import 與批次烘焙

### 3.1 本輪真正依賴的資料組

曾經操作的「4＋3」只是當次批次清單，**不是工具內建規則**。Forward Stop 結案依賴：

- 4 支 Stop：`WalkFwdStop_LU/RU`、`RunFwdStop_LU/RU`。
- 3 個速度錨點：`WalkFwdLoop`、`RunFwdLoop`、`SprintFwdLoop`。
- Walk／Run loop 的 `FootPhaseCurve` 同時是 Stop 選片的 authored 相位來源。

日後換動畫包時應重做 Catalog，不把上述名稱寫死進 Editor 工具。

### 3.2 SOP

1. 在 Project 視窗選**具名 AnimationClip 子資產**，避免同 FBX 其他 Idle／Turn／Start 被一起改動。
2. Loop 套 `Locomotion-位移` preset：XZ 不 Bake、Y Bake、Rotation Bake、Loop 開。
3. Stop 套 `烘焙曲線驅動` preset：XZ 不 Bake、Y Bake、Rotation 不 Bake、Loop 關。
4. 開啟 `Tools → Project → 動畫根運動物理烘焙工具 v4.0`。
5. 採樣角色指定 `Assets/Prefabs/X Bot.prefab`，取樣率 `60 FPS`。
6. 把明確 Clip 拖入或由 Project 選取加入通用批次清單；清單會去重、驗空且可捲動。
7. 批次執行；所有 Clip 共用單支模式的採樣設定與 Bake 演算法。
8. 檢查 `SourceClip`、`BakedDuration`、`SpeedCurve`、`FootPhaseCurve`、Loop、旋轉與方向欄位。

採樣器契約是：**Root 或其子階層恰好有一個綁定有效 Humanoid Avatar 的 Animator**。
`X Bot.prefab` 的 Animator 在 Model 子物件；早期的「Animator 必須掛 Root」是假前提。

### 3.3 為何移除 Phase C 專用入口

- 一次性資產名單會過期，也把第三方包名稱變成永久維護面。
- Import preset 與 Bake 是兩種責任，不因一次連續操作就必須硬綁。
- 真正值得留下的是通用、重複且容易漏步驟的能力：多 Clip 清單、共用設定、去重、驗證與捲動。

---

## 4. 結案資料快照

### 4.1 Mixer 與世界速度

| 項目 | 結案值 |
| --- | ---: |
| Mixer thresholds | `0 / 0.35 / 0.75 / 1` |
| Child playback speeds | `1 / 1.3327742 / 1.3124558 / 1` |
| `SynchronizeChildren` | 全關 |
| `MotionDriver.moveSpeedSource` | `Bake_SprintFwdLoop`，`6.2613893 m/s` |
| Walk 穩態強度 | `0.3651` |
| Walk／Run Stop bands | `0.35–0.50`／`0.75–0.875` |
| Band epsilon | `0.001` |

門檻保留使用者指定的手感值；播放倍率由資產代表速度派生：

```text
playback_i = threshold_i × speed_max / naturalSpeed_i
Walk = 0.35 × 6.2613893 / 1.6443043 = 1.3327742
Run  = 0.75 × 6.2613893 / 3.5780573 = 1.3124558
```

### 4.2 Bake 與 Transition

| 資產 | Duration | `AutoAverageSpeed` | Runtime 設定／用途 |
| --- | ---: | ---: | --- |
| Walk loop | `1.0 s` | `1.6443043` | Walk 錨點＋Pending phase |
| Run loop | `0.7666667 s` | `3.5780573` | Run 錨點＋立即選片 phase |
| Sprint loop | `0.6333334 s` | `6.2613893` | Mixer／MotionDriver `speed_max` |
| Walk Stop LU | `1.3333334 s` | `0.49906573` | entry `+0.1387`；Fade `.25`；Speed `1.3327742` |
| Walk Stop RU | `1.5333334 s` | `0.46599144` | entry `-0.1448`；Fade `.25`；Speed `1.3327742` |
| Run Stop LU | `1.2666668 s` | `1.1914282` | Fade `.15`；Speed `1.3124558` |
| Run Stop RU | `1.5000001 s` | `1.300034` | Fade `.15`；Speed `1.2588` |

Walk loop：`phase(0)=-0.1447`、零交越約 `0.2589/0.7749`、`phase(0.5)=+0.139`。
Walk LU／RU 停止距離為 `0.6654/0.7145 m`，首次落定約 `0.2339/0.2296 s`。

Run RU 沒沿用 `1.3124558`，因為原片在 `t≈0.117 s` 有 authored speed peak；改成 `1.2588` 後峰值約 `4.696 m/s`，不再小暴衝。

---

## 5. Runtime 實作邏輯

### 5.1 Request 與 tier

Stop request 是 raw intent 歸零的單幀上升沿，且必須 grounded、Stop 未 active、上一幀 ambient locomotion 仍持有位移權、`deltaTime > 0`。

入場強度在 `_smoother.Tick` **之前**快照，避免放開第一幀的衰減把 Run `0.75` 降到 band 外。Tier 再以 `Epsilon=0.001` 判斷；真實 `SmoothDamp` 穩態可能是 `0.74999994`，不能假設精確 `0.75`。兩帶若重疊代表配置含糊，安全退回 B9。

### 5.2 LU／RU 與相位來源

- LU：從右腳支撐入場，左腳先落定。
- RU：從左腳支撐入場，右腳先落定。

Runtime 讀 `variant.BakeData.GetFootPhaseAt(0)`，不讀名字。`EndPhase` 無法選片：兩支 Walk Stop 結尾都約 `+0.0006` 且被判 `RightFootDown`，站定後雙腳等高，片尾符號只是雜訊。

相位時鐘取 `AnimationFacadeBase.TryGetDominantChildNormalizedTime`。未同步 Mixer 的 root time 是多 child 加權值，不等於 Walk／Run 任一實際播放頭；Walk `0.3651` 混入的少量 Run 在零交越附近足以選錯腳。

Facade seam 保持通用唯讀：Animancer 端用無配置索引迴圈找最高權重直接 child；Core 只拿時間查已選 tier 的 loop Bake Data，不認識 Mixer、child index 或 AnimationClip。

### 5.3 Walk Pending Stop

兩支固定起點約在 Walk cycle `0.0/0.5`。只選對腳仍可能與當下完整 pose 差約 `0.24 cycle`，所以 Fade `.15 → .25` 仍會全身瞬跳。

```text
for 每支有效 Stop variant
    entryValue = Stop.FootPhaseCurve(0)
    在 loop 烘焙 keys 找 value 最接近 entryValue 的 authored key
    換算成目前或下一 cycle 的未來 normalized time
選擇從目前 child clock 往前最早抵達的 variant／target

target 在 0.02 tolerance 內 → 立即播
否則 → Pending，走到 target 再播
clock 無效或等待 > 0.5 s → 播放已選 variant，避免卡住
```

Pending 期間暫停 `_smoother.Tick`，保留 release-entry 速度／方向並走 `ExecuteBaseMovement`。若先讓 B9 減速，Stop 曲線開始時就會變成「先慢後衝」。重新輸入、離地、Jump、Roll 都立即取消。Run 已驗收，維持立即依腳相選片。

### 5.4 曲線位移與生命週期

Stop 播放後，`MotionDriver.ExecuteBakedCurveMovement` 用前後 playhead 與梯形平均速度積分。播放倍率已包含在 playhead delta；位移仍只經 `CharacterController.Move`，Stop 曲線不回寫 `MoveSpeed`。

播放倍率改變瞬時速度與完成時間，但不改 authored 停止距離：

```text
v_world = SpeedCurve(t_clip) × playback
dt_real = dt_clip / playback
∫ v_world dt_real = ∫ SpeedCurve(t_clip) dt_clip
```

End callback 只設 `CompletionRequested`，下一次 model Tick 仲裁。另有 `BakeDuration + 0.25 s` timeout；重新輸入、離地、Jump／Roll 接手也走同一失效入口。每次播放有單調 generation，舊 callback 不能完成新 Stop；`Invalidate` 不可用 `this = default` 把 generation 歸零。

---

## 6. 架構邊界

| 邊界 | 裁決 |
| --- | --- |
| State／Blackboard | Stop 是 model 私有 dynamics；不新增左右 State，也不保存 request／phase |
| Selection | 純資料運算；左右腳是 data variant |
| Animation | 經通用 Facade；Core 不直接碰 Animancer／Animator |
| IK | 不回讀 `FootIKPoseData`；IK 是選片與位移之後的地形 post-process |
| Motion | `MotionDriver` 是唯一位移出口，不直接寫 Transform |
| 資產 | FBX 子 clip 直引；調整放 Import、Bake、Mixer、Transition、Prefab |
| GC | runtime state 是 struct、熱路徑不用 LINQ；已知 callback 閉包是 Stop 起始事件配置，不是每幀配置 |

Claude 初稿曾提議 `Core/Movement/Models → Presentation.IK.FootIKPoseData` 回讀。評審否決：允許 model 認識 Presentation 是為了**驅動**通用 Animation／Motion seam，不代表可讀回 IK post-process。現行只讀 authored Bake Data 與 Facade 通用時鐘，A4 也有精確禁令。

---

## 7. 踩坑紀錄

| 症狀 | 無效判斷／作法 | 根因 | 最終修正與教訓 |
| --- | --- | --- | --- |
| X Bot 顯示 Root 沒 Animator | 強迫 Animator 掛 Gameplay Root | Animator 合法位於 Model 子階層 | 解析全子階層唯一有效 Humanoid Animator，Sample 對其 GameObject |
| 選單越做越亂 | 為一次操作增加 Phase C 入口 | 一次性工作被誤當永久 workflow | 移除具名入口，只留通用批次能力 |
| 小視窗不能操作 | 清單無完整 ScrollView | 一般工具可用性缺陷 | 整窗捲動＋合理最小尺寸，保持通用 |
| LU／RU 解讀反覆 | 只看字尾猜抬腳／支撐腳 | 名稱描述 First Stop／Plant Foot | Preview＋entry curve＋首次零交越三方驗證 |
| 兩支 `EndPhase` 相同 | 想看片尾選 LU／RU | 站定後雙腳等高 | 只看 authored entry，不看片尾 |
| 手感門檻被改回自然比例 | 把 Bake 速度當 threshold 唯一答案 | threshold 是手感，速度一致是 playback 的責任 | 保留 `0/.35/.75/1`，公式派生 child speed |
| Walk 放開前衝 `+49%/+65%` | 先調 Fade／Stop 曲線 | `moveSpeedSource` 錯接 Run `3.5781`，Mixer 卻以 Sprint `6.2614` 推導 | 改接 Sprint；先驗整條世界速度鏈 |
| Run Stop 偶爾不出現 | 放開後才讀 smoother | 第一幀衰減先把 `.75` 降到 `.739/.747` | 使用 pre-decay snapshot |
| Run 穩態仍漏 Gate | 測試手塞理想 `.75` | 漸近值是 `.74999994` | 共用 `Epsilon=.001`，測試跑真實 smoother |
| Run RU 小暴衝 | 全 Run Stop 都套 `1.3124558` | `t≈.117` 有局部 authored peak | RU 單獨 `1.2588`；代表平均速度不能取代看曲線 |
| Walk 同腳連踩 | 用 Mixer root time | root 是 child 加權時間 | 改讀主導 child clock |
| 想開 Mixer Sync | 以為同步只對齊時間 | 同步會調 child speed，且 gait 相位原點不同 | 保持全關，速度與選片分離 |
| 選對腳仍全身瞬跳 | Fade `.15 → .25` | 支撐腳正確不等於完整 pose 對齊 | Walk 等最近 authored entry；Fade 不能製造 pose match |
| Pending 先慢再衝 | 等待時仍讓 B9 減速 | Stop 開始後曲線重新給較高速度 | Pending 冻結 smoother，保留入場速度／方向 |
| Pending 可能卡住 | 只等理想相位 | clock／Bake／mapping 可能失效 | `.5 s` fail-safe＋中斷取消 |
| 舊 End callback 干擾新 Stop | callback 直接清 runtime／播 Idle | callback ownership 跨世代 | callback 只提 request，generation 擋舊回調 |
| 想讀 Foot IK 選片 | 畫面結果看似最準 | 形成 Presentation IK → Core 反向邊，地形也會污染 authored phase | Bake Data＋通用 clock；IK 保持後處理 |
| 想靠 playback 改停止距離 | 直覺認為播慢會走短 | 曲線速度與時間倍率抵消 | playback 只校速度／節奏；可控距離等 C5 |

---

## 8. 否決／延後方案

| 方案 | 狀態 | 理由／觸發條件 |
| --- | --- | --- |
| Fade-only | 否決 | `.25 s` A/B 仍跳；已由 Pending 解決 |
| Mixer Synchronization | 否決 | 改 child speed 並破壞校正 |
| Core 回讀 Foot IK | 否決 | 新增反向跨層資料邊 |
| 左／右 Gameplay State、phase 進黑板 | 否決 | data variant 不等於 gameplay authority |
| 複製 FBX clip 成 `.anim` | 否決 | 產生快照漂移 |
| Phase C 具名永久工具 | 已移除 | 一次性清單不值得維護 |
| Distance Matching | 延後 C5 | 有「必須在指定距離停住」的真需求才投入距離曲線與反查 API |
| Motion Matching | v2.0 研究 | 會取代 selection 架構，需新 ADR |
| Foot Lock／Pose Warping | 延後 | 量測證明 planted-foot 滑移仍明顯才做，不能替代選片與位移 |

---

## 9. 驗證與下輪清單

`LocomotionStopTests` 已覆蓋真實 LU／RU 語意、fallback、最近未來 phase、band／epsilon、單幀 request、generation／timeout、Pending promotion、pre-decay tier、dominant child clock 與 active motion。A12–A15 鎖定：不擴黑板、不加 State、Facade 保持通用、runtime 只有一個 holder。

Unity Play 已驗收：Walk 任意腳相放開可自然走到匹配點再 Stop；無全身瞬跳、無先慢後衝；Run RU 無小暴衝；重新輸入、Jump、Roll 都可立即接管。

下一個 Transition 任務依序檢查：

1. 先 Catalog，確認資產存在並用 Preview 驗證語意。
2. 套正確 Import preset，以同一 Humanoid／sample rate 烘焙。
3. 驗整條速度鏈：natural speed → threshold → playback → `moveSpeedSource` → 局部 peak。
4. 分開 Request、Selection、Motion、Completion／Interruption。
5. 姿勢問題先區分速度、腳相、完整 pose mismatch，不能共用一個旋鈕。
6. 所有等待都要有 timeout、資料 fallback 與 gameplay interruption。
7. 架構邊界寫成測試；一次性操作寫進 SOP／復盤，不寫成永久工具。
