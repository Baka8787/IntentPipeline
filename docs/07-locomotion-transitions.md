# Locomotion Transitions — Phase C1：Forward Stop Vertical Slice（規格）

> **定位**：`docs/02-dev-spec.md` 的**子系統分卷**（Dev Spec 實作 API 層），治理 Locomotion 的**過渡段**
> （Start／Stop／Pivot／Turn）。目前已落 **C1 Walk Forward Stop**，並在驗收後擴充 **C1.1 Run Forward Stop**；其餘技術樹節點見 `docs/04-locomotion-foundation.md` §15.3。
> **上游**：`docs/ADR/003-movement-intent-layering.md`（D3／D4／§13.2／§13.4，**不修改**）、
> `docs/04-locomotion-foundation.md` §15（Phase C 執行規格與 G1–G4 Gate）、
> `docs/06-animation-presentation.md`（Facade／Mixer／Bake→配置資料流）。
> **本文件狀態**：C1／C1.1 已完成 Unity Play 驗收；Walk `0.25 s` Fade A/B 的殘餘姿勢跳動已由 Pending Stop 等待最近 authored 入場相位解決，速度連續與重新輸入／Jump／Roll 中斷皆通過。所有數字皆來自磁碟上的實際資產（見 §3），凡證據不足者一律列入 §11.3
> 「需要 Unity Play 驗證」，不以推測補洞。

---

## 0. 實作裁決修正（2026-08-20，優先於下文初稿）

初稿 §5.1／§7.2／§10.5／§13.4 提議讓 `LocomotionModel` 直接讀 `FootIKPoseData`。實作評審否決：
Models 對 Presentation 的既有放行，是讓 model 驅動通用 Animation／Motion seam；不代表 Core 可回讀 IK post-process。
A4 已新增 `Project.Presentation.IK` 精確禁令。

C1 改以一筆明確 `walkLoopBakeData` 查 `Bake_WalkFwdLoop.FootPhaseCurve`；這不是 Run／Sprint tier 表。
初版曾使用 `AnimationFacadeBase.GetNormalizedTime()` 的 Mixer root time，Play 驗收證實腳相交越附近偶爾連踩；
現行取樣由下方 §0.2 取代。資料缺失時有效變體仍確定性交替。

End Event 只設完成旗標，下一次 model Tick 先確認 ambient 位移權仍在才回 Locomotion；Jump／Roll 已接手時只讓
世代失效。Generation 在 Invalidate 後持續遞增，不以 `_stop = default` 清回零。下文涉及 FootIK 直讀與 callback
立即 Play 的初稿段落均由本節取代。

### 0.1 C1.1 Run Forward Stop 修正（2026-08-20）

C1 Play 驗收通過後，依 §13.2 的既定觸發條件加入 Run。`LocomotionStopSelector.SelectTier` 先以入場強度
選 Walk／Run 集合，再逐字共用既有 `SelectByEntryPhase`；`LocomotionStopRuntime` 只多記 active tier，仍只有
`LocomotionModel` 一個持有者，不進黑板、不加 State、不改 Facade／MotionDriver。Run 腳相改查
`Bake_RunFwdLoop.FootPhaseCurve`，而非誤用 Walk loop。

Run Stop Play 驗收發現 RU 在 `t≈0.117 s` 有 authored 加速峰值。LU 保持 playback `1.3124558`，峰值
`4.6083 m/s`；RU 改為 `1.2588`，將峰值壓到約 `4.696 m/s`，對齊 Run 錨點
`0.75 × 6.2614 = 4.696 m/s`。預設 Run 帶因此收緊為 `0.75–0.875`，上界仍為 Run `0.75`
與 Sprint `1.0` 的中點。Walk 帶同步收緊為 `0.35–0.50`；兩帶之間刻意留空，帶重疊視為含糊配置並退回 B9。
Sprint 沒有 Stop 資產，維持 B9。此加法不改 ownership／hierarchy／cross-cutting contract，因此不開 ADR。

### 0.2 Walk 連踩修正：主導 child clock（2026-08-21）

`Locomotion.asset` 關閉 `SynchronizeChildren`，且 Walk／Run／Sprint 的 authored 腳相原點不同；Mixer root 的
`NormalizedTime` 是子狀態時間的加權聚合，不能代表 Walk 或 Run 任一支 clip 的實際播放頭。Walk 穩態 `0.3651`
仍混入少量 Run，零交越附近因此可能把符號推到另一側，選錯 LU／RU 而出現同腳連踩。

`AnimationFacadeBase` 新增通用唯讀 `TryGetDominantChildNormalizedTime(stateKey, out time)`；Animancer 後端以
索引迴圈取最高權重直接 child，同權重固定取較前者，無有效 child 回 false。`LocomotionModel` 只把回傳時間交給
已選 tier 的 loop Bake Data；Core 不認識 Mixer、child index 或 AnimationClip。Walk Stop 帶 `0.35–0.50` 內 Walk
始終主導；Run 帶 `0.75–0.875` 內 Run 主導（上界同權重時取較前者 Run）。

否決直接開啟 `SynchronizeChildren`：它會改寫子 playable 速度，且三支 loop 的相位原點不一致，會擴大到已驗收的
門檻／PlaybackSpeed／滑步校正。本修正只更換選片時鐘，不改速度、資產、黑板、State 或 MotionDriver。符號選片
本身的 ≈0.24 週期理論誤差仍存在；若 Play 複驗仍有殘餘頓挫，下一案才評估相位起始對齊。

### 0.3 Walk 全身姿勢跳動：Fade A/B（2026-08-21）

§0.2 Play 複驗確認：選片時鐘修正後不再把 Mixer root 當 Walk 時間，但在任意時刻放開仍會看到全身姿勢於
Stop 入場時被拉動。這正是 §3.2／R2 已記錄的剩餘上限：兩支 Stop 只有 `0.0／0.5` 兩個固定起點，符號正確不等於
當下完整 pose 與起點相同。原 `FadeDuration = 0.15 s` 小於 Walk 最壞約 `0.24 s` 的相位差。

第一階段只把 `WalkStop_LU／RU` Fade 調為 `0.25 s`；Speed `1.3327742`、Start Time `0`、Bake Data、速度門檻與
Runtime 全部不動。Play 複驗仍有明顯瞬間全身變動，證實淡入不足以解決固定起點 pose mismatch；依既定 Gate
停止調 Fade，改由下方 §0.4 的 Pending Stop 處理。

### 0.4 Walk Pending Stop：等待最近 authored 入場相位（2026-08-21）

放開 Walk 時不立即播放。Selector 以每支 Stop 的 `FootPhaseCurve(0)` 連續值，在 Walk loop 的既有烘焙鍵中找最接近
的 authored 時刻，再選從目前 child clock 往前最早抵達的變體；不讀 LU／RU 檔名，也不手填 `0／0.5` 常數。

等待期間 `_smoother` 暫停推進，Movement Output 維持 release-entry 速度與方向，`UpdateMotion` 繼續走既有
`ExecuteBaseMovement`。因此角色自然走到匹配姿勢再進 Stop，不會先被 B9 減速、隨後又被 Stop 曲線加速。重新輸入、
離地或 Jump／Roll 接管沿用既有中斷；child clock 失效或等待超過 `0.5 s` 時立即播放既定變體，避免無限等待。

Pending 只套 Walk；已驗收的 Run 仍立即選片。它是 `LocomotionStopRuntime` 內的 model 私有階段，不是 Gameplay
State，不進黑板，不改 Facade／MotionDriver／Mixer。代價是放開至 Stop 開始多出最多約四分之一 Walk 週期的距離；
這是以停止反應距離交換 pose 連續性的明確手感裁決。

---

## 1. Problem

放開移動鍵時，角色目前只有一種收步方式：`LocomotionSpeedSmoother` 的 B9 `SmoothDamp` 減速
（`decelTime = 0.18 s`），期間持續播放 Locomotion 1D Mixer 並讓混合參數滑向 0。這條路徑有三個結構性缺陷：

1. **沒有停止姿勢**。角色以 Walk 姿勢逐漸縮小步幅到 0，不會「把腳收攏站定」——停在半跨步是常態。
   這一點在 `docs/04-locomotion-foundation.md` §11.2 就被記錄為已知問題並判定非 Blocking，至今未解。
2. **停止距離與動畫脫鉤**。B9 的停止距離＝`當前正規化速度 × moveSpeed × decelTime`（推導見 §8.4），
   與 Stop 動畫**授權的**停止距離（0.665 m／0.715 m，§3.1）相差 1.6×–2.8×。任何「播 Stop 動畫但保留 B9 位移」
   的做法都必然滑步。
3. **左右腳語意無處安放**。Kubold 提供 `WalkFwdStop_LU`／`_RU` 兩支，語意是 **First Stop／First Plant Foot**；
   選錯等於在支撐腳上再落一次同一隻腳，是最刺眼的一類動畫錯誤。而目前管線沒有任何一層有資格回答
   「現在該用哪隻腳停」——FSM 不該管（`docs/04` §15.2 邊界紀律 1），Presentation 不該有 gameplay 權威。

C1 要解的就是這三件事的**最小可驗證切片**：Walk Forward，兩個變體，一條 Request → Selection → Motion →
Presentation 的完整鏈路，並且鏈路上每一段都要能單獨被指認出問題（`docs/04` §15.8 Motion 驗收條）。

---

## 2. Scope／Non-goals

### 2.1 In Scope

| 項目 | 界定 |
| --- | --- |
| 動作範圍 | Walk／Run Forward Stop，各兩個 `_LU/_RU` 變體；Sprint 無資產，維持 B9 |
| Request | 放開移動意圖的**單帧邊沿**；`LocomotionModel` 私有，不進黑板 |
| Selection | 依**當下腳相**選變體；判準來自 `MotionBakeData.FootPhaseCurve`，**不讀檔名** |
| Motion Execution | `MotionDriver.ExecuteBakedCurveMovement`（既有 API ＋ 一個 playhead-delta 多載，§8.5） |
| Presentation | 經既有 `AnimationFacadeBase.PlayWithCallback`；Core 不認識 Animancer |
| 中斷 | 重新輸入／Jump／Roll／離地／暫停，皆有決定性結果（§9） |
| 驗收 | EditMode 自動測試 ＋ Play 人工項（§11） |

### 2.2 Non-goals（本輪明確不做）

- ❌ Sprint Stop、Start、Moving Pivot、Turn in Place（C2–C4，`docs/04` §15.3）。
- ❌ **不新增** `LeftStopState`／`RightStopState`，或任何新的 `StateType`。左右腳是 **data variant**。
- ❌ **不把 FootPhase 寫成 gameplay 黑板的跨帧狀態**（`docs/04` §15.2 邊界紀律 2）。
- ❌ **不新增 `PlayerRuntimeData` 欄位或寫入者**（A5 白名單零改動）。
- ❌ **不在 `AnimationFacadeBase` 加 `PlayStop` 之類的 Locomotion 專用 API**；Facade 維持通用 sink（ADR-003 D4）。
- ❌ 不建 Motion Matching、萬用 Animation Action framework、`LocomotionTransitionResolver` 泛用選片框架。
- ❌ 不建一次性 Editor 工具（CLAUDE.md「Editor Tool vs Documented Process」雙 Gate；本輪只有 2 個資產參數，Gate A 不成立）。
- ❌ Distance Matching、Foot Lock、Motion Warping、Stride／Pose Warping（§13.2 全數延後，附延後理由與觸發條件）。
- ❌ 不修改任何既有 Accepted ADR。

---

## 3. Evidence from Assets

> 以下全部是磁碟現況的量測值（`Assets/ScriptableObjects/Motion/*.asset` 的曲線關鍵影格、
> `MovementAnimsetPro.fbx.meta` 的匯入設定、`Locomotion.asset`、`Gait_ActionRPG.asset`、`X Bot.prefab`）。
> **這一節是後面所有裁決的唯一事實基礎**；重烘焙後必須重新量測。

### 3.1 兩支 Stop 資產

| 欄位 | `Bake_WalkFwdStop_LU` | `Bake_WalkFwdStop_RU` |
| --- | ---: | ---: |
| SourceClip（FBX 子 clip 直引） | `MovementAnimsetPro.fbx` #7400024 | 同 FBX #7400026 |
| `BakedDuration` | **1.3333334 s**（來源 40 frames @30 fps） | **1.5333334 s**（46 frames） |
| 烘焙取樣 | 60 Hz，81 keys | 60 Hz，93 keys |
| `SpeedCurve(0⁺)`（入場瞬時速度） | **1.4566 m/s** | **1.6158 m/s** |
| 速度收斂到 ≈0 的時刻 | ≈0.95 s（殘值 <0.02 至 ≈1.25 s） | ≈0.95 s（殘值 <0.02 至 ≈1.15 s） |
| **授權停止距離** `∫SpeedCurve dt` | **0.6654 m** | **0.7145 m** |
| 收斂後的殘餘位移 | <0.004 m（<0.6%） | <0.004 m |
| `RotationCurve` | **全程 0** | **全程 0** |
| `RotationFinishedTime` | 0 | 0 |
| `TargetLocalDirection` | (0, 0, 0) | (0, 0, 0) |
| `AutoAverageSpeed` | 0.49907 m/s | 0.46599 m/s |
| `EndPhase` | 1（RightFootDown） | 1（RightFootDown） |
| `FootPhaseCurve(0)` | **+0.1387**（右腳較低＝**右腳支撐**） | **−0.1448**（左腳較低＝**左腳支撐**） |
| 首次零交越 | **t=0.2339，R→L**（左腳落定） | **t=0.2296，L→R**（右腳落定） |
| `FootPhaseCurve(end)` | +0.0006 | +0.0006 |
| 匯入設定（`.fbx.meta`） | XZ Bake ❌／Y Bake ✅ Original／Rot Bake ❌／Loop ❌ | 同左 |

**四個直接可用的結論：**

1. **`_LU/_RU` 的機械意義已被曲線證實，與使用者 Preview 驗證一致。**
   LU 從**右腳支撐**入場、左腳在 t≈0.234 s 落定 ⇒ **左腳先停住**。
   RU 從**左腳支撐**入場、右腳在 t≈0.230 s 落定 ⇒ **右腳先停住**。
   兩支的首次落定時刻幾乎相同（0.23 s），是同一套 authoring。

2. **`EndPhase` 對 Stop 完全無效，這是可量化的。** 兩支的片尾曲線值都是 **+0.0006 m（0.6 mm）**——
   站定時雙腳等高，符號純粹是雜訊，而且兩支都被判成 `RightFootDown`。
   「以片尾 `EndPhase` 代替 `_LU/_RU`」在數值上就是不可能成立，不是風格偏好問題。

3. **停止距離是 authored constant，而且與播放速度無關**（推導見 §8.3）：LU 0.665 m、RU 0.715 m。
   兩者差 7.4%——**停止距離會因為當下踩哪隻腳而不同**，這是資產本身的性質，C1 不修正它。

4. **這兩支的匯入 preset 已經是 dev-spec §0.4 的「烘焙曲線驅動」那一列**（XZ ❌／Y ✅ Original／Rot ❌／Loop ❌，
   與 `Stand To Roll` 同類）。資產側**已經**把它們定位成「由 `SpeedCurve` 驅動位移」，不是 procedural loop。
   這是 §8 裁決的重要輸入：選 Distance Matching 反而要違逆已套用的匯入語意。

### 3.2 Walk Loop 對照（選片判準的另一半）

| 欄位 | `Bake_WalkFwdLoop` |
| --- | ---: |
| `BakedDuration` | 1.0 s（30 frames @30 fps），Loop ✅ |
| `AutoAverageSpeed` | 1.6443043 m/s（穩態，全程等速） |
| `FootPhaseCurve(0)` | −0.1447（左腳支撐） |
| 零交越 | t=0.2589（L→R）、t=0.7749（R→L） |
| `FootPhaseCurve(0.50)` | **+0.139** |

**關鍵對齊（本規格 §7 選片演算法的證據）：**

- `RU` 入場相位 −0.1448 ≈ Walk Loop 在 **t=0.00**（−0.1447）。
- `LU` 入場相位 +0.1387 ≈ Walk Loop 在 **t=0.50**（+0.139）。

也就是說 **兩支 Stop 的入場姿勢剛好把步態週期切成兩半（0.0 與 0.5）**。
Loop 的支撐腳切換點在 0.2589／0.7749，與兩個理想入場點的中點（0.25／0.75）只差 0.009／0.025 週期。
**因此「以腳相符號選片」在數值上幾乎等同「選入場相位最接近的變體」**——用符號（穩健，不受腿長／地形影響）
不會比用數值（精確但受振幅影響）差。這是選符號而非選數值的實證依據，不是偏好。

**同時要誠實記錄它的上限**：符號選片保證「支撐腳正確」，**不保證相位對齊**。
最壞情況發生在剛跨過零交越時（例如 loop t=0.26 就放開），此時入場相位誤差可達 **≈0.24 週期**
（walk 約 0.24 s）。這個誤差只能靠 Transition 的 fade 吸收；要真正消除必須做相位對齊起始時間或
Distance Matching（§13.2 延後項）。**用兩支資產 ＋ 符號選片，這是理論上限，不是實作缺陷。**

### 3.3 現行配置（Locomotion 側）

| 來源 | 值 |
| --- | --- |
| `Locomotion.asset` `_Thresholds` | `0 / 0.35 / 0.75 / 1`（Idle／Walk／Run／Sprint） |
| `Locomotion.asset` `_Speeds` | `1 / 1.3327742 / 1.3124558 / 1` |
| `_SynchronizeChildren` | 全關（速度精確模式，見 `docs/06`） |
| `Gait_ActionRPG.asset` | `default 0.75`／`sprint 1`／`walk 0.3651`／`walkIsToggle 1` |
| `LocomotionModel`（prefab） | `moveSpeedAccelTime 0.12`／`moveSpeedDecelTime 0.18` |
| `MotionDriver`（prefab） | `moveSpeedSource → Bake_SprintFwdLoop`（6.2613893 m/s）／`overrideMoveSpeed false` |
| `AnimancerFacade.transitionMappings` | `Idle`→`Locomotion.asset`、`Move`→**同一份** `Locomotion.asset`、`Jump`、`Roll` |

### 3.4 ✅ V1 已解決：`moveSpeedSource` 與 Mixer 共用 Sprint 基準

`Locomotion.asset` 的 `_Speeds` 以 `speed_max = 6.2613893`（Sprint 代表速度）推導：
`0.35 × 6.2614 / 1.6443 = 1.3328`、`0.75 × 6.2614 / 3.5781 = 1.3125`、`1 × 6.2614 / 6.2614 = 1`。
Prefab 的 `MotionDriver.moveSpeedSource` 已由錯誤的 `Bake_RunFwdLoop` 改接 `Bake_SprintFwdLoop`，
`overrideMoveSpeed` 維持 false；Mixer 門檻、child playback 與世界速度現在使用同一基準。

修正前 Walk 世界速度只有 `0.3651 × 3.5781 = 1.306 m/s`，Stop 入場為 LU `1.941`／RU `2.154 m/s`，
放開會前衝 `+49%/+65%`。修正後 Walk 為 `2.286 m/s`，入場落差收斂為 `−15.1%/−5.8%`，已完成 Play 驗收。

---

## 4. Responsibility Boundary

### 4.1 對應 `docs/04` §15.2 的四層

| 層 | C1 由誰承載 | 負責 | **不**負責 |
| --- | --- | --- | --- |
| Gameplay Authority | `FullBodyStateMachine`（**零改動**） | 允許／封鎖／中斷／優先級 | 不知道有 Stop、不選左右腳、不新增 `StateType` |
| Transition Selection | `LocomotionStopSelector`（純函式）＋ model 上序列化的變體集合 | 「用哪一份 `MotionBakeData` ＋ 哪一個動畫鍵」 | 不呼叫 `CharacterController`、不寫 IK pose |
| Motion Execution | `MotionDriver.ExecuteBakedCurveMovement` | 如何把選中的曲線變成位移 | 不決定選哪支、不決定 gameplay 是否允許 |
| Animation Post Process | Foot IK v1（**零改動**） | 地形貼合 | **不是**選片權威；仍在選片與位移決策之後 |

### 4.2 G1–G4 裁決（`docs/04` §15.6）

| Gate | 裁決 | 依據 |
| --- | --- | --- |
| **G1 承載** | **Movement Model 內部 phase**（`LocomotionModel`），既非 Presentation FSM 亦非 Gameplay State | Stop **隨時可取消**（重新輸入／Jump／Roll 皆即時中斷）、**無 gameplay 禁止**、**無不可取消窗**、**無 net snapshot 需求** ⇒ 依 G1 準則不是 Gameplay State。但它**擁有 authoritative motion**（曲線驅動位移）⇒ 也不是純 Presentation。而「怎麼移動」本來就是 Movement Model 的職責（ADR-003 D3／D4），且 §15.6-G4 已明文把「locomotion-local dynamics」歸給 active model。**收步就是 locomotion 把自己從移動帶回靜止的 dynamics**，歸屬因此唯一 |
| **G2 Selection seam** | 只收**一個維度：腳相**；變體集合是一組**明確列舉**的 `(MotionBakeData, animationKey)` | Catalog 只證明了 Walk Fwd 的兩支變體。不預建 speed tier／direction／angle 欄位（§15.6-G2「只收 Catalog 證明的維度」） |
| **G3 Motion seam** | Selection 回答「哪份資料」，Execution 由 model 在 `ExecuteBaseMovement`／`ExecuteBakedCurveMovement` 之間選 | **不建 `StopMover` 類別**，不把 Stop 封死在單一執行法；換執行策略＝改 model 裡的一個分支，Selection 零改 |
| **G4 ownership** | authored facts → `MotionBakeData`；停止邊沿／當下腳相／播放進度 → **active model 私有**；FSM 不為選片擴寫黑板 | 與 §15.6-G4 逐字一致 |

### 4.3 為什麼「不是 Gameplay State」在程式上也成立

`LocomotionModel` 已經是 `MoveSpeed`／`MoveDirection`／`UpperBodyWeight` 的唯一寫入者，
已經在順序 3 驅動自己的動畫參數（D4），也已經在順序 6 透過 `UpdateMotion` 決定位移路徑（D3）。
Stop 用到的三件事——**讀 intent 的邊沿**、**選一份 Bake 資料**、**改走另一條 MotionDriver 方法**——
沒有一件超出這顆 model 既有的權責。因此：

- **黑板 schema 零改動**、**A5 白名單零改動**、**FSM 拓撲零改動**、**依賴方向零改動**。
- 依 CLAUDE.md 路由規則，這是「非架構性的子系統加法」⇒ **寫 Living Doc，不開 ADR**（唯一需留意的邊界見 §13.4）。

---

## 5. Data Contracts

### 5.1 既有資料（只讀，零改動）

| 資料 | 用途 | 存取 |
| --- | --- | --- |
| `MotionBakeData.SpeedCurve` | Stop 的位移真相 | `GetSpeedAt(t)` |
| `MotionBakeData.FootPhaseCurve` | 變體的**入場腳相**（authored fact） | `GetFootPhaseAt(0f)` |
| `MotionBakeData.BakedDuration` | 播放長度／逾時保護 | `Duration` |
| `MotionBakeData.RotationCurve` | C1 全零，仍照常交給 Execution（為 C2/C3 保留路徑） | `GetRotationAt(t)` |
| `PlayerRuntimeData.MovementIntent` | Request 邊沿的唯一輸入 | 只讀 |
| `PlayerRuntimeData.IsGrounded` | Request 閘門與離地中斷 | 只讀 |
| `AnimationFacadeBase.TryGetDominantChildNormalizedTime` | 實際主導 locomotion child 的播放頭（見 §7.2） | 依 `stateKey` 唯讀查詢；Core 不認識動畫圖型別 |

### 5.2 新增資料（全部是 model 私有，**不進黑板**）

```csharp
// Assets/Scripts/Core/Movement/Models/LocomotionStopRuntime.cs
// 定位比照 LocomotionSpeedSmoother：純值型別、無 MonoBehaviour 相依、可 EditMode 決定性驗證、
// 由單一擁有者（active model）內嵌，換擁有者只是換一個欄位。
public struct LocomotionStopRuntime
{
    public bool  IsActive;                 // 本次 Stop 是否進行中
    public bool  IsPending;                // Walk 正等待最近 authored 入場相位
    public bool  IsPlaying;                // 已開始播放並套用 Stop 曲線
    public int   VariantIndex;             // 選中的變體索引（-1 = 無）
    public int   Generation;               // 播放世代；每次 Start/Abort 遞增，用來否決過期回調
    public float TargetNormalizedTime;     // Pending 的 unwrapped Walk child 目標播放頭
    public float NormalizedTime;           // 本帧播放進度（來自 Facade，唯一真相）
    public float PreviousNormalizedTime;   // 上一帧進度（供 playhead-delta 位移積分，§8.5）
    public float ElapsedRealTime;          // 真實經過秒數，僅供逾時保護（§9.3）
}
```

```csharp
// LocomotionModel 上新增的序列化配置（[SerializeField] 依 §0.1 豁免採 camelCase）
[System.Serializable]
public struct LocomotionStopVariant
{
    public MotionBakeData BakeData;    // 入場腳相與位移曲線的唯一來源
    public string AnimationKey;        // AnimancerFacade.transitionMappings 的 StateKey
}
// [SerializeField] private LocomotionStopVariant[] walkStopVariants;  // 明確列舉，C1 = 2 筆
// [SerializeField] private string locomotionAnimationKey = "Idle";    // 回到 Locomotion 資產的鍵
// [SerializeField] private float walkStopMinIntensity = 0.35f;        // 入場強度下界（§7.4）
// [SerializeField] private float walkStopMaxIntensity = 0.50f;        // 入場強度上界（§7.4）
```

### 5.3 Ownership／Lifetime（回答 Q2）

| 項目 | 擁有者 | 可寫者 | 保存多久 |
| --- | --- | --- | --- |
| **Stop Request**（邊沿本身） | active `IMovementModel` | 同左 | **單帧**——在產生它的那次 `Tick` 內就被消費，從不跨帧存在，也不是欄位 |
| `LocomotionStopRuntime` | active `IMovementModel`（值型別內嵌） | 同左 | 一次 Pending＋Stop 的存續期（等待上限 0.5 s＋一支 clip 長度）；完成／中斷即清空，**不汙染下一次播放** |
| `walkStopVariants` 配置 | 資產／Inspector | 設計師 | 永久（配置期建表，執行期只讀） |

**為什麼不進黑板（四條，任何一條單獨就足夠）：**

1. **沒有黑板外的消費者。** Selection、Execution、Presentation 三段全都在 model 內完成。
2. `docs/04` §15.2 邊界紀律 2 明文：`FootPhaseCurve` 是 authored data，**不改成 gameplay 黑板跨帧狀態**。
3. 進黑板必然新增 `PlayerRuntimeData` 寫入者，違反 §15.8 Ownership 驗收條，且要動 A5 白名單。
4. **snapshot-able 沒有被犧牲**（ADR-003 §9-L5）：跨帧狀態全部顯式在一顆值型別 struct 上、無隱藏靜態態，
   與 `LocomotionSpeedSmoother` 同一個 pattern。

---

## 6. Request → Selection → Motion → Presentation 流程

### 6.1 每帧鏈路（掛在既有管線順序上，**不新增管線階段**）

```
順序 2.5  PlayerLocomotionPolicy      → 寫 MovementIntent（唯一寫入者，零改動）
順序 3    LocomotionModel.Tick        → ① B9 平滑照常推進（不得凍結，§10.3）
   （Update）                            ② Stop Request 邊沿判定（§6.2）
                                        ③ 命中 → Selection（§7）→ Facade.PlayWithCallback
                                        ④ Stop 進行中 → 取 Facade.GetNormalizedTime() 更新 playhead
                                        ⑤ 中斷判定（§9）
順序 4    FullBodyStateMachine.Tick   → IsProducingMotion 含 Stop ⇒ 停留在 Move（零改動）
順序 5    Runner.SyncAnimation        → 狀態未變 ⇒ 不 Play（零改動）
順序 6    Move/IdleState.OnUpdateMotion → LocomotionModel.UpdateMotion
   （LateUpdate）                       Stop 進行中 → ExecuteBakedCurveMovement（曲線驅動）
                                        否則         → ExecuteBaseMovement（原行為，逐字不變）
順序 6.5  PresentationPipeline        → Foot IK／Footstep 照常（零改動）
順序 7    ResetTransientState         → 零改動（Stop 狀態不在黑板，不需要例外）
```

### 6.2 Request 的邊沿定義（回答 Q1）

**裁決：`raw intent 歸零` 的上升沿，並以「model 當下確實握有位移權」為閘門。**

```
StopRequested(本帧) ⟺
      _wasIntending                                   // 上一帧 intent ≥ Epsilon（model 私有 bool）
   && intent.DesiredSpeedNormalized < Epsilon         // 本帧歸零（Epsilon = 0.001f，沿用既有常數）
   && !_stop.IsActive                                 // 沒有進行中的 Stop
   && data.IsGrounded                                 // 在地面
   && _lastMotionFrame == Time.frameCount - 1         // 上一帧確實有 ambient 狀態把位移 delegate 給我
   && deltaTime > 0f                                  // 沒有時間流逝就沒有邊沿（同 MotionDriver.IsTimeFrozen）
   && walkStopMinIntensity <= releaseEntryIntensity <= walkStopMaxIntensity // Walk 帶（§7.4）
```

`releaseEntryIntensity` 必須在本幀呼叫 `LocomotionSpeedSmoother.Tick` **之前**快照。若用 Tick 後的值，
穩定 Run `0.75` 在放開首幀會因 `decelTime=0.18` 降為約 `0.7388`（60 FPS）／`0.7471`（120 FPS），
兩者都錯過 Run 下界 `0.75`，而且是否命中會受幀率影響。輸出 `MoveSpeed` 仍採 Tick 後值；只有單幀
Stop tier request 使用放開前快照，因此沒有第二個速度真相，也不改 B9 dynamics。

Band 邊界比較沿用 `LocomotionSpeedSmoother.Epsilon = 0.001`：SmoothDamp 臨界阻尼只會漸近目標，
60 FPS 收斂 120 幀後的 Run 實值為 `0.74999994`，若用嚴格 `>= 0.75` 仍會永久漏判。
容忍範圍只到 `0.749`，對 `6.2614 m/s` 的速度差約 `0.0063 m/s`（0.13%），不會重新放行
原本 `0.70` 的暴衝區；上下界共用同一 tolerance，重疊時仍由 `SelectTier` fail closed。

**為什麼是 raw intent 歸零，而不是另外兩個候選：**

| 候選 | 否決理由 |
| --- | --- |
| **平滑速度下降** | 不是邊沿而是狀態，且 B9 減速**正是本規格要取代的東西**——用它當觸發是循環定義。另外它無法區分「放開」與「從 Sprint 降到 Walk」 |
| **`IsProducingMotion` 即將退出** | **太晚**。門檻是 `speed ≥ 0.1`，以 `decelTime = 0.18` 計，從走路強度衰減到 0.1 約需 0.35–0.5 s，此時角色已幾乎停住、可用的停止距離只剩幾公分。Stop 動畫需要在**全速**時開始才有 0.665 m 可用 |
| **raw intent 歸零** ✅ | 這是「玩家放手」的**第一帧**，B9 尚未吃掉任何速度，完整停止距離都還在。而且它是 producer 已經算好的值，model 只讀不寫，不破壞任何 ownership |

**三個必要閘門的理由：**

- `_lastMotionFrame == frameCount - 1`：`UpdateMotion` 只由 ambient 狀態（Idle／Move）呼叫。
  這一條同時擋掉「在 Jump／Roll 途中放開移動鍵」誤觸發 Stop，**且不需要 model 認識 `StateType`**（A4 禁令保持綠燈）。
- `deltaTime > 0f`：與 `MotionDriver.IsTimeFrozen` 同一條哲學——「沒有時間就沒有東西要積分、沒有邊沿可偵測」。
  沒有這條，暫停的第一帧（`BlockInput` 把輸入歸零）會在世界凍結時開一次 Stop。
- Walk 帶上下界：見 §7.4；**下界比上界更重要**（缺下界會前衝，不是只是不好看）。

### 6.3 一次完整 Stop 的時序（LU，`p = 1.3327742`，Sprint 基準）

| 時間 | 事件 |
| --- | --- |
| t=0（順序 3） | 邊沿命中 → 量測腳相（右腳支撐）→ 選 `LU` → `PlayWithCallback("WalkStop_LU", cb)`；`Generation++` |
| t=0（順序 6） | `ExecuteBakedCurveMovement`，playhead 0 → Δ；世界速度 ≈1.94 m/s |
| t≈0.18 s | 左腳落定（曲線 t≈0.234 s／p）；Foot IK 與 Footstep 照常反應，**不需要任何新事件** |
| t≈0.71 s | 曲線速度收斂到 ≈0，位移實質結束（>99% 距離已走完） |
| t≈1.00 s | clip 結束 → Animancer End Event → 回調（世代相符）→ `Play("Idle")`、`_stop` 清空 |
| t≈1.00 s（順序 4） | `IsProducingMotion` 回到 `_smoother.Speed ≥ 0.1`＝false ⇒ FSM Move → Idle |

> ⚠️ **已知手感缺口**：LU 的最後 ≈0.33 s、RU 的最後 ≈0.58 s 是**速度為零的收勢段**。
> 這段期間 FSM 仍停在 `Move`（因為 `IsProducingMotion` 被 Stop 撐住），Inspector 的 `[Current State]` 會顯示
> `MOVE` 但角色站著不動。這是刻意的**單一完成權威**取捨（§9.3），列為 §11.3-V7 待實測。

---

## 7. LU／RU Selection Algorithm

### 7.1 判準（回答 Q3）

**規則：選「入場腳相與當下腳相同號」的變體。**
等價說法：**First Stop Foot ＝ 當下正在擺動的那隻腳**（＝當下支撐腳的另一隻）。

| 當下支撐腳 | 擺動腳（＝先落定） | 應選變體 | 該變體 `FootPhaseCurve(0)` | 符號 |
| --- | --- | --- | ---: | --- |
| 左腳（curve < 0） | 右腳 | **RU** | −0.1448 | 同號 ✓ |
| 右腳（curve > 0） | 左腳 | **LU** | +0.1387 | 同號 ✓ |

**這條規則的三個性質，全部有 §3 的證據支撐：**

1. **不讀檔名。** 判準完全來自 `variant.BakeData.GetFootPhaseAt(0f)` 的符號。
   `_LU/_RU` 只是給人看的資產名；程式碼裡沒有任何字串比對（§11.1-T7 以測試守住）。
2. **不需要新的腳相定義。** 執行期量測與烘焙用的是**同一個量**：`左腳世界 Y − 右腳世界 Y`
   （烘焙側見 `MotionFeatureAnalysis.FootPhaseCurveAnalyzer`；執行期側見 §7.2）。單位、符號、共模消除全部相同。
3. **兩支變體剛好把週期切成 0.0／0.5**（§3.2），所以符號選片 ≈ 最近入場相位選片。

### 7.2 執行期腳相怎麼來（含退化策略）

| 順位 | 來源 | 說明 | 退化條件 |
| --- | --- | --- | --- |
| **① 首選** | `Facade.TryGetDominantChildNormalizedTime(locomotionKey, out t)` → 當前 tier 的 `loopBakeData.GetFootPhaseAt(Repeat(t,1) × Duration)` | 讀實際主導 pose 的 child clock，再以 authored Bake Data 解讀腳相；Facade 介面通用、唯讀，不洩漏 Animancer | state 未播放、不是 parent、所有 child 權重為 0、時間非有限值或 Bake Data 無效 → 退 ② |
| **② 退化** | **交替**（model 私有索引，每次 Stop 前進） | 無可靠相位時的決定性行為，避免永遠同一隻腳先停 | 同時 `#if UNITY_EDITOR` 每 tier 警告一次 |

**為什麼不用 Mixer root time**：它是多支 child time 的加權聚合；在未同步且各 loop 相位原點不同時沒有一支對應的
Bake Data。**為什麼不用 `EndPhase`**：§3.1 結論 2——兩支片尾值都是 +0.0006 m，資訊量為零。

取樣仍發生在順序 3（Update），與動畫參數驅動同一階段；不挪到 LateUpdate，避免 `Play` 比參數晚一帧。

### 7.3 演算法（零配置、零 LINQ、索引迴圈）

```
SelectVariant(variants, runtimePhaseValue):
    if variants == null || variants.Length == 0: return -1
    bool runtimeLeftDown = runtimePhaseValue < 0f
    for i in 0 .. variants.Length-1:                    // 索引迴圈；具體陣列，不對介面 foreach
        if variants[i].BakeData == null: continue
        bool entryLeftDown = variants[i].BakeData.GetFootPhaseAt(0f) == FootPhase.LeftFootDown
        if entryLeftDown == runtimeLeftDown: return i   // 同號即命中
    return firstValidIndex(variants)                    // 沒有同號者（資產只有一支／都同號）→ 取第一個有效者
```

- 純函式、無狀態、無配置 ⇒ 可在 EditMode 以合成 `MotionBakeData` 完整覆蓋（§11.1-T1）。
- 回傳索引而非資產，讓呼叫端同時取得 `AnimationKey`，**Selection 不碰 Facade、不碰 MotionDriver**。
- **未來擴充**（C2/C3 變體數 > 2 時）：把「同號即命中」換成「入場相位數值最接近」即可，介面不變。
  C1 刻意不先做：兩支反相位資產下兩者等價（§3.2），先做等於無證據地引入振幅敏感度。

### 7.4 如何只選 Walk Stop，而不提前建 Run／Sprint 泛用框架（回答 Q4）

**三道各自獨立的約束，全部是「不作為」而非「新框架」：**

1. **變體集合是明確列舉的陣列**，由 Inspector 指定兩支 Walk 資產。
   沒有 tier enum、沒有 direction enum、沒有 angle 欄位、沒有 `Dictionary<SpeedTier, ...>`。
   加 Run Stop ＝ 未來在 §7.3 之前**多一層 tier 選集合**，那是 C2+ 在有 Catalog 證據時才做的加法。

2. **入場強度上下界閘門**（§6.2 最後一條）。上界擋掉 Run／Sprint，下界擋掉「幾乎停住才放開」：

   | 界線 | 預設 | 推導 | 越界的後果 |
   | --- | ---: | --- | --- |
   | `walkStopMaxIntensity` | 0.50 | Walk 錨點 0.35 與 Run 錨點 0.75 的中點 | 用 Walk Stop 收 Run 速度：0.665 m 停止距離不夠，會急煞 |
   | `walkStopMinIntensity` | 0.35 | 取兩變體較高峰值：RU `2.1535 / 6.2614 = 0.344`，向上收斂至 Walk 錨點 0.35 | **低於此值時至少一支 Stop 曲線會比角色當前速度快 ⇒ 放開反而前衝**。下界必須看全部變體峰值，不只看 LU 首帧 |

   > 兩個預設值都以 **`moveSpeed = 6.2614`** 推導；§3.4 的 V1 已修正，Prefab 現在以 `Bake_SprintFwdLoop` 提供此基準。

3. **越界＝走既有路徑，不是走「另一種 Stop」**。閘門不命中時完全不產生 request，
   位移與動畫逐字回到今天的 B9 行為 ⇒ **Run／Sprint／慢速釋放的既有手感是結構性不變的**（§11.2 回歸項因此可證）。

---

## 8. Motion Execution 比較與最終裁決

### 8.1 候選

- **A：直接播放 Bake `SpeedCurve`**（`ExecuteBakedCurveMovement`，Roll 先例）。
- **B：Distance Matching**——保留 B9 位移，改用「剩餘停止距離」驅動動畫 playhead（Epic 的作法）。
- **C：維持 B9 位移，Stop clip 純表現**（不改位移權威）。
- **D：A ＋ 入場速度校正**（第一段以 warp 把曲線速度拉到入場速度）。

### 8.2 五軸比較（數字全部來自 §3）

| 軸 | A 曲線驅動 | B Distance Matching | C 純表現 | D A＋入場校正 |
| --- | --- | --- | --- | --- |
| **速度連續性** | 入場有一次落差：−15.1%（LU）／−5.8%（RU）（Sprint 基準）。之後連續 | **最佳**：位移完全不變，天然連續 | **完美**（位移沒動） | 好（第一段被拉平），但引入一個看不見的 warp 係數 |
| **停止距離** | **固定 0.665 m／0.715 m**，authored，無法調（§8.3） | **可控**：以 B9 預測距離為準，動畫去配合 | 0.235 m／0.411 m（Sprint 世界速度基準），**與動畫脫鉤 1.6×–2.8×** | 固定，同 A |
| **滑步** | **結構上為零**：世界速度恆等於曲線的 root 速度（§8.4） | 低，但取決於距離曲線取樣密度與剩餘距離預測品質 | **嚴重**：距離差 1.6×–2.8×，全程滑 | 校正段內必然滑（那正是校正的定義） |
| **複雜度** | **最低**：既有 API ＋ 一個 playhead 多載；無新烘焙特徵 | 高：需累積距離曲線（新烘焙特徵）＋距離→時間反查＋Facade 需要「設定播放時間」的新 API＋B9 剩餘距離預測 | 最低（等於不做） | 中：多一組 warp 參數與它的失效條件 |
| **未來擴充成本** | 低：Pivot／Turn 同族（`RotationCurve` 已在，`ExecuteBakedCurveMovement` 已支援 yaw） | **最終目的地**，但 `docs/04` §15.3／§15.9 明文排在 **C5**、且前提是「基本 Stop／Pivot 選片、曲線與中斷穩定」 | 無（它不是一個可擴充的答案） | 低但會留下一個沒人敢動的魔術係數 |

### 8.3 一個決定性的量化事實：停止距離對播放速度免疫

以 `p` 為播放倍率、`v_world(t) = SpeedCurve(t_clip) × p`、`t_clip = p × t_real`：

```
∫ v_world dt_real = ∫ SpeedCurve(t_clip) × p × (dt_clip / p) = ∫ SpeedCurve dt_clip = 常數
```

⇒ **停止距離永遠是 0.665 m（LU）／0.715 m（RU），調 `p` 只改變停多久、不改變停多遠。**
這句話同時是 A 的最大限制，也是「要讓停止距離可控就只能走 B」的證明——**這正是 Distance Matching 存在的理由**，
也解釋了為什麼它不能被 A 的參數調整取代。

### 8.4 對照組：B9 的停止距離（回答 Q6 的前半）

`Mathf.SmoothDamp` 是**臨界阻尼二階響應，不是等加速度**。目標為 0、時間常數 `T = decelTime` 時，
連續極限解為 `s(t) = (s₀ + (ṡ₀ + ω s₀)t)·e^(−ωt)`，`ω = 2/T`，積分得：

```
剩餘正規化距離 = 2·s₀/ω + ṡ₀/ω² = s₀·T + ṡ₀·T²/4          （乘上 moveSpeed 得公尺）
穩態（ṡ₀ = 0）：距離 = s₀ × T × moveSpeed
```

代入：`0.3651 × 0.18 × 3.5781 = 0.235 m`（目前接線）／`0.3651 × 0.18 × 6.2614 = 0.411 m`（Sprint 基準）。
對照動畫授權的 0.665 m ⇒ **1.6×–2.8× 落差**。這就是 §8.2「C 全程滑步」那一格的數字來源。

**若日後採 B，取得停止距離的正確方法是「前向模擬」而不是套上式：**

```
LocomotionSpeedSmoother probe = _smoother;        // 值型別複製 ⇒ 零 GC
float distance = 0f;  int guard = 0;
while (probe.Speed >= Epsilon && guard++ < MaxProbeSteps)   // MaxProbeSteps ≈ 64（≈1.07 s @60Hz）
{
    probe.Tick(in zeroIntent, accelTime, decelTime, step);
    distance += probe.Speed * moveSpeed * step;
}
```

**為什麼前向模擬優於閉式解**：它用的是**將來真的會產生位移的那份程式碼**，所以不可能與實作分岔——
有人改了平滑律（換曲線、加 maxSpeed、改 ε snap），預測自動跟著改。閉式解只是直覺與交叉檢查用。
另需注意三點誤差來源：Unity `SmoothDamp` 的離散化與連續解有 O((ω·dt)²) 差異、`Epsilon = 0.001` 的 snap 會截掉尾巴、
以及**斜坡／碰撞／重新輸入會改變實際速度，所以 B 必須每帧重算剩餘距離**（Epic 的作法），不能在 request 當下算一次就用到底。

### 8.5 裁決：**A**（曲線驅動），並補一個 playhead-delta 多載

**選 A 的五個理由（按權重）：**

1. **`docs/04` §15.3／§15.9 已經把 Distance Matching 排在 C5**，前提是「基本 Stop 選片、曲線與中斷穩定」。
   C1 先做 B 等於跳過自己設的制動點。
2. **資產側已經表態**：兩支 Stop 的匯入 preset 就是「烘焙曲線驅動」那一列（§3.1 結論 4）。選 B 反而要違逆它。
3. **滑步在 A 之下是結構性為零**：clip 的 root XZ 被 `applyRootMotion=false` 抽出丟棄（原地播放），
   而我們用**完全相同的曲線值**推動角色 ⇒ 支撐腳的世界速度 ≈ 0。這正是 Locomotion loop 在各錨點成立的同一個機制。
4. **零新增 Facade API、零新增烘焙特徵、零新增黑板欄位**。B 至少要三樣新東西（累積距離曲線、距離→時間反查、
   Facade 的「設定播放進度」API）。
5. **通往 C2／C3 的路是同一條**：Turn／Pivot 也是 `SpeedCurve + RotationCurve` 驅動，`ExecuteBakedCurveMovement`
   本來就支援 yaw。

**必要的最小 API 補強（generic，非 Locomotion 專用）：**

`ExecuteBakedCurveMovement(bakeData, normalizedTime, data)` 目前內部以 `previousTime = currentTime − Time.deltaTime`
推算上一帧，這**內建了「播放倍率＝1」的假設**：

- 位移面：`p ≠ 1` 時世界速度應為 `SpeedCurve × p`，否則角色只走 `0.665/p` 公尺 ⇒ 滑步。
- 旋轉面：`deltaYaw` 用的時間窗會錯（C1 的 `RotationCurve` 全零所以看不出來，**C2/C3 一定會踩到**）。

因此新增多載，把「上一帧的 playhead」改為由呼叫端傳入：

```csharp
// MotionDriver（Presentation.Motion，位移出口不變、擁有者不變）
public void ExecuteBakedCurveMovement(
    MotionBakeData bakeData, float normalizedTime, float previousNormalizedTime, PlayerRuntimeData data)
// 速度以梯形積分求本帧平均，再換算成世界速度：
//   Δt_clip = (normalizedTime − previousNormalizedTime) × Duration
//   v_world = 0.5f * (GetSpeedAt(t_prev) + GetSpeedAt(t_now)) * (Δt_clip / Time.deltaTime)
// 既有 3 參數多載改為以 previousNormalizedTime = (t_now − Time.deltaTime)/Duration 委派，RollState 行為逐字不變。
```

**這個形狀的三個好處：** ①**不需要任何地方知道 `p`**——playhead 差本身就帶著倍率，資產的 `_Speed` 因此是唯一真相，
不會出現「model 上一份、資產上一份」的第二真相（`docs/06` 的 Threshold/PlaybackSpeed 紀律同源）；
②梯形積分讓近線性的減速段幾乎無誤差（矩形積分在整段會累積約 1.6 cm ≈ 2.4%）；③順手修掉既有的 yaw 假設。

**`p` 本身怎麼定（資產側，走 SOP 不建工具）：**

```
p_stop = v_entry / SpeedCurve(0⁺)              // 讓銜接速度連續的理論值
C1 採用：p_stop = 1.3327742（＝ Locomotion.asset 的 Walk 子動作倍率）
```

理由：Walk loop 本身就是以 1.3328 倍播放（門檻 0.35 是手感值、非天生比例），
**跟著它走可保持步頻連續**，且銜接落差只剩 −15.1%（LU）／−5.8%（RU）。
若改用理論值（1.569／1.415）銜接速度全連續，但步頻會比 loop 快 18%。
**兩者的取捨屬手感，列 §11.3-V2 實測後由使用者定案**；程式碼兩種都不用改（`p` 只住在資產裡）。

### 8.6 被否決者的保留理由

- **B**：不是不對，是**順序不對**——它需要穩定的選片與中斷做地基（`docs/04` §15.9 明文）。C5 見。
- **C**：唯一完全無風險的選項，但它不解 §1 的任何一條問題（距離差 1.6×–2.8× ⇒ 全程滑步）。
- **D**：warp 係數是一個看不見的第二真相，且它想解的「入場落差」在 §3.4 的速度基準修正後只有 −5.8%–−15%。
  用一個永久機制換 6%–15%，不划算；真要解就直接走 B。

---

## 9. Completion／Interruption Matrix

### 9.1 誰擁有哪個「完成」

刻意分成**兩個職責不同的完成**，並明訂衝突時誰贏：

| 完成 | 權威 | 觸發 | 效果 |
| --- | --- | --- | --- |
| **Presentation 完成** | `AnimationFacadeBase.PlayWithCallback` 的回調（Animancer End Event） | clip 播完 | 回放 Locomotion 資產、清空 `_stop` |
| **Motion 完成** | Bake 曲線本身 | `SpeedCurve` 收斂到 0（LU t≈0.95 s、RU t≈0.95 s，clip 時間） | 位移自然歸零，**不需要任何判斷**——曲線值就是 0 |
| **逾時保護** | model 的 `ElapsedRealTime` | `> BakedDuration / p_min + margin`（建議 margin 0.25 s，`p_min` 取 1） | 強制完成。**回調永遠不來時角色不得卡住**（映射漏設、資產無效時 `TryGetTransition` 只 LogWarning 就 return） |

> **衝突規則**：三者都只做「清空 `_stop` ＋ 回放 Locomotion」這一件事，且都經由同一個
> `CompleteStop(reason)` 入口並比對世代 ⇒ **先到者生效，後到者被世代檢查否決**，不存在競態。

### 9.2 回調不會誤完成別次播放（回答 Q8）

`AnimancerFacade.PlayWithCallback` 的既有實作把 `OnEnd` 設在 **`AnimancerState`** 上，
並在首次觸發時把自己設回 null。這帶來三個必須被規格處理的性質：

| Animancer 性質 | 對本規格的後果 | 處置 |
| --- | --- | --- |
| End Event 超過結束時間後可能**每帧**觸發 | 必須在回調內**立即**切走 | 回調第一件事就是 `Play(locomotionAnimationKey)`；且 lambda 已自我 null 化 |
| **淡出中的 state 仍在推進時間**，因此中斷後舊 state 仍可能抵達自己的結束點並觸發 | **過期回調是真實可能，不是理論風險** | **世代檢查（必要，非裝飾）**：`Generation` 在每次 Start／Abort 都遞增；回調攜帶當時的世代，不符即 `return` |
| 多個 StateKey 映射同一份 Transition ⇒ **共用同一個 `AnimancerState`，也共用 `OnEnd`** | 兩個變體若共用資產，End Event 會互相蓋掉 | **LU／RU 必須是兩份獨立的 TransitionAsset**，且不得與 `Idle`／`Move`／`Jump`／`Roll` 共用（§12.3 資產約束） |

```csharp
// 概念形；世代由閉包攜帶，因為 Action 簽名不帶參數且不得為此改動通用 Facade。
int generation = ++_stop.Generation;
_facade.PlayWithCallback(variant.AnimationKey, () => CompleteStop(generation, StopEndReason.Callback));

private void CompleteStop(int generation, StopEndReason reason)
{
    if (!_stop.IsActive || generation != _stop.Generation) return;   // 過期回調 ⇒ 靜默丟棄
    _stop = default;                                                 // 不留任何私有殘留
    _facade.Play(locomotionAnimationKey);
}
```

**回調的 GC 誠實揭露**：`PlayWithCallback` 內部本來就每次配置一個閉包（`docs/06` 明文、dev-spec §5 既有待辦）；
本規格再加一個攜帶世代的閉包 ⇒ **每次 Stop 開始約 2 次配置（數十位元組），穩態每帧仍為 0 B**。
不為了省這一次配置而放棄世代精確性；正解是償還 §5 的回調 ObjectPool 待辦（§13.2）。

### 9.3 中斷矩陣（回答 Q9）

| 事件 | 偵測點與方式 | 位移（順序 6） | 表現 | `_stop` |
| --- | --- | --- | --- | --- |
| **重新輸入**（`DesiredSpeedNormalized ≥ Epsilon`） | 順序 3，**同帧**（intent 於 2.5 寫入） | 立刻回 `ExecuteBaseMovement` | `Play(locomotionAnimationKey)` | `Generation++`、清空 |
| **Jump** | FSM 於順序 4 中斷 Move→Jump；model 於**下一帧** `Tick` 發現 `_lastMotionFrame` 斷鏈 | `JumpState` 自帶位移（既有 override） | 順序 5 播 `Jump`（狀態變更） | `Generation++`、清空 |
| **Roll** | 同上 | `RollState` 曲線位移 | 順序 5 播 `Roll` | `Generation++`、清空 |
| **離地**（非 Jump，例如走下懸崖） | 順序 3 讀 `data.IsGrounded == false` | 回 `ExecuteBaseMovement`（重力路徑） | `Play(locomotionAnimationKey)` | `Generation++`、清空 |
| **暫停／`deltaTime <= 0`** | 順序 3 | `MotionDriver.IsTimeFrozen` 整段跳過（既有） | Animancer 隨 `timeScale` 凍結，playhead 不前進 | **保留**（不中斷、不完成） |
| **`BlockInput`（UI 模式）** | 上游把 `InputData` 歸零 ⇒ 看起來與「放開」完全相同 | 正常開一次 Stop | 正常 | 正常 |
| **Stop 自然完成** | 回調／逾時 | `ExecuteBaseMovement`（此時速度已 ≈0） | `Play(locomotionAnimationKey)`；下一帧 FSM → Idle | 清空 |
| **Stop 進行中再次放開** | 不可能——intent 已是 0，產生不出上升沿 | — | — | — |

**Jump／Roll 中斷為什麼不需要新介面：**
`UpdateMotion` 只由 ambient 狀態呼叫。FSM 一旦切到 Jump／Roll，delegate 就停止 ⇒
model 在 `Tick` 比對 `_lastMotionFrame != Time.frameCount - 1` 即可判定「我的位移權已被收回」。
這**不是**取巧，而是「model 只在 ambient 狀態把位移 delegate 給它時才擁有位移權」這條既有規則的直接讀出。
代價是**中斷判定晚一帧**（16 ms），但那一帧的位移本來就已經由 Jump／Roll 的 override 接手，**不會有雙重位移**。

> ⚠️ **`BlockInput` 會啟動 Stop 是行為變更**：dev-spec §7.2-M7 ③ 目前驗收的是「滑行收步」。
> 改成播 Stop 動畫仍滿足「不是瞬間定格」，而且更好看，但**這是被人工驗收過的項目**，列 §11.3-V6 請使用者確認。

---

## 10. Zero-GC 與 Ownership

### 10.1 CharacterController 的唯一位移出口（回答 Q10）

```
Move/IdleState.OnUpdateMotion  →  LocomotionModel.UpdateMotion  →  MotionDriver.Execute*  →  CharacterController.Move
```

- Stop 期間**唯一**改變的是最後一段選哪個 `Execute*`。**沒有任何新的 `CharacterController` 引用被建立**。
- `GetGravityThisFrame` 仍在 `ExecuteBakedCurveMovement` 內被呼叫 ⇒ `IsGrounded`／`JustLanded`／`JustLeftGround`
  在 Stop 期間照常更新，**落地音、Footstep、Foot IK 的輸入鏈完全不變**。
- `MotionDriver` 仍是這三個旗標與 `Move()` 的唯一擁有者；A5 白名單零改動。

### 10.2 為什麼 Stop 期間**不得**把曲線速度寫進 `MoveSpeed`

ADR-003 §13.4 明文：`MoveSpeed` 必須恆可由 `MovementIntent`（＋model dynamics）重新導出，
**禁止任何路徑繞過 intent 直寫**。曲線速度來自資產、不來自 intent ⇒ 寫進去就是製造第二真相，
而且會讓 A7（同一 intent 序列 → 相同輸出）當場變紅。

**已知語意缺口（誠實記錄）**：Stop 期間黑板 `MoveSpeed` 是 B9 的衰減值，**不等於**角色真實速度。
現有消費者都不受影響——`ExecuteBaseMovement`（Stop 期間不走）、`JumpState` 空中控制（與 Stop 互斥）、
Mixer 參數（本來就該衰減到 Idle）、Editor 監視器（顯示用）。
若未來出現需要「真實速度」的消費者，那是**新需求**，屆時再裁決要不要開一個獨立的 Movement Output 欄位。

### 10.3 B9 在 Stop 期間必須繼續推進

`_smoother.Tick` 不得凍結，理由兩條：①§10.2 的單一真相紀律；②中斷回移動時要能立刻續跑。

**副作用（列入實測）**：Stop 進行到一半被重新輸入中斷時，B9 已衰減到接近 0
（t=0.2 s 時約 0.46 m/s、t=0.5 s 時 ≈0.1 m/s），而曲線當下的世界速度還有 1.37 m/s ⇒ **會有一次明顯的頓挫**。
C1 接受並記錄（§11.3-V5、§13.2 附已預先分析的解法），**不在本輪為此動 §13.4 的不變量**。

### 10.4 `IsProducingMotion` 的擴義

```csharp
public bool IsProducingMotion => _smoother.Speed >= MoveThreshold || _stop.IsActive;
```

**必要性**：不加這一項，Stop 播到約 0.4 s 時 `_smoother.Speed` 已跌破 0.1 ⇒ FSM 轉 Idle ⇒
順序 5 播 `Idle` ⇒ **Stop 動畫被自己的收步切斷**。
**語意正確性**：介面文件寫的是「本模型此刻是否正在產生運動」——Stop 期間 model 確實在產生運動（來自曲線），
所以這是把既有語意講完整，不是為了繞過 FSM。

### 10.5 零 GC 檢核（回答 Q11）

| 熱路徑動作 | 配置 | 依據 |
| --- | --- | --- |
| 邊沿判定、Walk 帶閘門 | 0 | 純數值比較 |
| 主導 child clock 查詢 | 0 | `_stateCache` 查表＋`ParentState` 索引迴圈；無陣列、LINQ 或 boxing |
| `SelectVariant` | 0 | 具體陣列 ＋ 索引迴圈（**不對介面型別 `foreach`**，避免 A3 抓不到的 struct enumerator 裝箱，dev-spec §7.1-A3） |
| `GetFootPhaseAt` / `GetSpeedAt` | 0 | `AnimationCurve.Evaluate` |
| `ExecuteBakedCurveMovement` | 0 | 既有路徑 |
| 動畫鍵 | 0 | 序列化字串欄位，**無插值、無串接**；Facade 內 `string→StringReference` 走 intern 快取 |
| **`PlayWithCallback`（每次 Stop 開始）** | **≈2 次閉包**（既有 1 ＋ 世代 1） | §9.2 已揭露；穩態每帧仍 0 B |
| 前向模擬 probe（若日後採 B） | 0 | 值型別複製 |
| Editor 警告字串 | 0（Release） | 一律包 `#if UNITY_EDITOR` |

**§7.4 SOP 補充**：量測時把「Stop 起始帧」與「穩態帧」分開判讀。穩態必須維持 0 B（§7.4.5 的既有結論不得退化），
Stop 起始帧允許出現上表那一筆已知配置，且必須能對上數量級。

---

## 11. Tests／Play Acceptance

### 11.1 EditMode 自動測試（新增 `Assets/_Project/Tests/EditMode/LocomotionStopTests.cs`）

| ID | 測項 | 斷言 |
| --- | --- | --- |
| **T1** | 選片：runtime 相位為負（左腳支撐） | 選中入場相位為負的變體（＝RU 語意） |
| **T2** | 選片：runtime 相位為正 | 選中入場相位為正的變體（＝LU 語意） |
| **T3** | 選片退化 | 空集合→−1；只有一支→回那一支；`BakeData == null` 的項目被跳過；全部同號→回第一個有效者 |
| **T4** | **真實資產語意鎖定**（`AssetDatabase` 載入兩支 Bake） | `LU.GetFootPhaseAt(0) == RightFootDown` 且 `RU.GetFootPhaseAt(0) == LeftFootDown`；兩者相反。**重烘焙若翻轉符號，這條立刻紅** |
| **T5** | 真實資產距離守門 | 兩支的 `∫SpeedCurve dt` 落在 [0.5, 0.9] m；`RotationCurve` 全零 |
| **T6** | Request 邊沿 | 「移動→歸零」序列恰好產生 1 次 request；持續歸零不再產生；`deltaTime = 0` 不產生；`IsGrounded = false` 不產生；未 delegate（`_lastMotionFrame` 斷鏈）不產生 |
| **T7** | Walk 帶閘門 | 強度 0.75／0.20 皆不產生 request；0.3651 產生 |
| **T8** | **完成只算一次** | 同一世代回調呼叫兩次 → 只完成一次；**中斷後**舊世代回調到達 → 被丟棄且不影響新 Stop |
| **T9** | 中斷矩陣 | 對每個 §9.3 事件斷言：位移路徑列舉（`Procedural`／`BakedCurve`）正確、`_stop.IsActive == false`、無殘留旗標 |
| **T10** | 逾時保護 | 回調永不到達時，超過 `Duration + margin` 必定完成 |
| **T11** | `IsProducingMotion` | Stop 進行中恆 true（即使 `_smoother.Speed == 0`）；完成後隨 smoother |
| **T12** | 行為等價回歸 | 閘門不命中時，`UpdateMotion` 的呼叫序列與加入 Stop 前**逐字相同** |
| **T13** | 主導 child clock | Mixer root 與主導 child 落在相反腳相時，必須依 child 選片；防止回退成加權聚合時間 |
| **T14** | 最近 authored 入場相位 | 合成 loop 的兩個 entry target 位於 0／0.5；目前時間 0.2 選 0.5，0.6 選下一圈 1.0 |
| **T15** | Pending runtime | Pending 不接受 callback、不推進 Stop playhead；升級 Playing 後世代不變且才開始推進 |
| **T16** | Pending model 整合 | 放開首帧維持 Locomotion 與 release-entry 速度；child clock 抵達 target 後才播放指定 Stop |

### 11.2 架構回歸（加進 `ArchitectureRegressionTests.cs`，`docs/02-dev-spec.md` §7.1 同步）

| ID | 不變量 | 判定方式 |
| --- | --- | --- |
| **A5／A4／A3／A9／A10** | **白名單與禁令一律零改動** | 既有測試。新增寫入者／新增 `StateType` 依賴／LINQ 都會直接變紅 |
| **A12（新）** | **腳相與 Stop 不得進黑板** | 反射：`PlayerRuntimeData` 的公開成員名不得含 `Foot`／`Stop`／`Phase` |
| **A13（新）** | **不得新增 Stop 狀態** | 反射：`StateType` 的成員恆為 `{None, Idle, Move, Jump, Roll}`（新增需同步改測試＋State Matrix，讓遺漏現形） |
| **A14（新）** | **Facade 維持通用** | 反射：`AnimationFacadeBase` **本型別宣告**的公開成員名不得含 `Stop`／`Locomotion`／`Walk`（排除 `MonoBehaviour.StopCoroutine` 等繼承 API） |
| **A15（新）** | **`LocomotionStopRuntime` 全域唯一持有者** | 宣告形 regex 掃描 Runtime，恰好 1 個（比照 A10 的理由：>1 份跨帧狀態＝中斷語意會分岔） |

> 依 CLAUDE.md「Test-as-Spec」：A12／A13／A14 是把使用者這輪定下的三條紅線**機器化**——
> 它們比散文更便宜，而且不會漂移。

### 11.3 Play 驗收與待驗清單

**A. 功能驗收（Play）**

| # | 驗收項 | 預期 |
| --- | --- | --- |
| P1 | **LU／RU 選片** | 左腳支撐時放開 → **右腳先落定**；右腳支撐時放開 → **左腳先落定**。除錯顯示需同時列出「量測相位值、選中索引、資產名」，**不靠肉眼猜** |
| P2 | **完成回 Idle** | `[Current State]` 由 MOVE → IDLE；無殘留、下一次起步正常 |
| P3 | **中途重新輸入** | 立刻恢復移動；無排隊、無二次播放、無定格 |
| P4 | **Jump／Roll 中斷** | 立刻切換；Stop 不再影響位移；落地後行為正常 |
| P5 | **不重複 callback** | 反覆「放開→立刻按住」20 次，除錯計數器的完成次數 == 實際完整播完的次數 |
| P6 | **最終停止距離** | 自放開帧起量測 ≈ **0.665 m（LU）／0.715 m（RU）**（±10%）。與改動前的 B9 距離（§8.4）明顯不同屬預期 |
| P7 | **無明顯 foot slide** | Stop 全程支撐腳不滑；斜坡上由 Foot IK 貼合，時機不變 |
| P8 | **不破壞現有 locomotion** | Idle／Walk／Run／Sprint／Jump／Roll／Footstep／Landing／Pause 全部不退化；**Run／Sprint 放開的手感與今天逐字相同**（閘門不命中 ⇒ 走原路徑） |
| P9 | **腳步音不重複發報** | Stop 期間的落腳由既有 `FootstepDetector`（幾何偵測）產生；**不得**新增任何 clip 事件式腳步 |
| P10 | **零 GC** | 穩態 0 B；Stop 起始帧只出現 §10.5 揭露的那一筆 |
| P11 | **Pending Walk** | 任意腳相放開後最多多走約四分之一週期，到匹配姿勢才開始 Stop；無全身瞬跳、無先慢後衝；重新輸入立即取消 |

**B. 🔴 需要 Unity Play 驗證（證據不足，不得猜測）**

| # | 待驗項 | 為什麼現在無法定案 | 阻塞性 |
| --- | --- | --- | --- |
| **V1 ✅** | `MotionDriver.moveSpeedSource` 應指向哪一支 | 已改接 `Bake_SprintFwdLoop`（6.2614），與 Mixer 推導基準一致（§3.4） | **已解決並通過 Play** |
| **V2** | `p_stop` 取 1.3327742（步頻連續）或理論值 1.569／1.415（速度連續） | 純手感，資產側一行設定即可切換 | 不阻塞（先用 1.3327742） |
| **V3** | `Facade.GetNormalizedTime()` 在「Mixer → Clip」交叉淡入期間是否可靠回報 Stop clip 的進度 | `RollState` 已依賴同一假設（Clip → Clip），但**Mixer → Clip 尚未驗證** | 不阻塞（逾時保護兜底），但 P6 距離不準時第一個要查這裡 |
| **V4 ✅** | 腳相來源是否需要回讀 `FootIKPoseData` | 實作評審否決 Presentation IK → Core；現行以主導 child clock＋對應 loop Bake Data 取 authored phase | **已由 A4 禁令與 EditMode／Play 驗收關閉** |
| **V5** | 淡出中的 Animancer state 是否真的會觸發 End Event（＝世代檢查是否真的會被用到） | Animancer 內部行為，不讀它的私有實作（CLAUDE.md Gate B） | 不阻塞（世代檢查無論如何都正確） |
| **V6** | `BlockInput`（UI 模式／暫停）時啟動 Stop 是否可接受 | 這會改變 dev-spec §7.2-M7 ③ 已驗收過的行為 | 不阻塞，但需使用者點頭 |
| **V7** | 收勢段（LU ≈0.33 s／RU ≈0.58 s 速度為 0 但仍在 MOVE）手感是否可接受 | 純感知 | 不阻塞；若不行，備案是「Motion 完成即結束 Stop、讓片尾由 fade 吸收」 |
| **V8** | 中途中斷的頓挫（§10.3）是否明顯 | 純感知 | 不阻塞；解法已預先分析（§13.2） |

---

## 12. Planned File Changes

> **本文件只做規劃。本次工作階段除新增本檔外不修改任何程式、資產、Prefab、場景或既有 ADR。**

### 12.1 新增（Runtime）

| 檔案 | 內容 |
| --- | --- |
| `Assets/Scripts/Core/Movement/Models/LocomotionStopRuntime.cs` | §5.2 的值型別跨帧狀態（比照 `LocomotionSpeedSmoother` 的定位與註解密度） |
| `Assets/Scripts/Core/Movement/Models/LocomotionStopSelector.cs` | §7.3 的純函式選片 ＋ `LocomotionStopVariant` |

### 12.2 修改（Runtime）

| 檔案 | 變更 | 風險 |
| --- | --- | --- |
| `Core/Movement/Models/LocomotionModel.cs` | 邊沿判定、Walk 帶閘門、腳相量測、選片、`PlayWithCallback`、playhead 追蹤、中斷、`IsProducingMotion` 擴義 | 中：本檔是 Stop 的唯一承載者，需留意不要長成 God Class（選片與跨帧狀態已外移成兩個可測單元） |
| `Presentation/Motion/MotionDriver.cs` | 新增 `ExecuteBakedCurveMovement(..., previousNormalizedTime, ...)` 多載；既有 3 參數版委派過去 | 低：`RollState` 行為逐字不變（既有測試守）；順帶修掉 `p ≠ 1` 的 yaw 假設 |

### 12.3 使用者側資產（**AI 不碰 `.prefab`／`.asset`／`.meta`／場景**）

1. 建兩份 **獨立** `TransitionAsset`（`WalkStop_LU`／`WalkStop_RU`），各自引用 FBX 子 clip
   `WalkFwdStop_LU`／`WalkFwdStop_RU`（**直引，不得 Ctrl+D 複製 clip**）。
2. 兩份資產設定：`Speed = 1.3327742`（§8.5，待 V2）、**`Start Time` 明確設為 0**、`Fade Duration` 建議 0.15
   （與 `Locomotion.asset` 同值）。
   > ⚠️ **`Start Time` 必須明確設 0**：Animancer 對「仍在淡出中的同一份資產」重新 `Play` 時可能**接續**而非重播。
   > 中斷後立刻再次觸發同一變體時，這會讓 Stop 從中段開始。
3. `AnimancerFacade.transitionMappings` 加兩列：`WalkStop_LU`／`WalkStop_RU` → 對應資產。
   **不得**與 `Idle`／`Move`／`Jump`／`Roll` 共用資產（§9.2 共用 `AnimancerState` ⇒ End Event 互蓋）。
4. `LocomotionModel`（角色 Root）填 `walkStopVariants` 兩筆（Bake 資產 ＋ 動畫鍵）、`locomotionAnimationKey = "Idle"`。
5. **V1 已完成**：`MotionDriver.moveSpeedSource` 已接 `Bake_SprintFwdLoop`，與 Mixer 推導基準一致（§3.4）。

### 12.4 文件同步（實測通過後才寫，遵循 §15.8 Fold back）

| 檔案 | 變更 |
| --- | --- |
| `docs/02-dev-spec.md` | §2.1 順序 3／6 備註（Stop 走哪條路徑）；§3.3 State Matrix 的 Move 列加註「Stop 期間 `IsProducingMotion` 由 model 撐住」；§3.2 `MotionDriver` 新多載；§7.1 加 A12–A15 |
| `docs/01-design-doc.md` | §4.8 Movement Model 職責補上「locomotion 過渡段（收步）」 |
| `docs/04-locomotion-foundation.md` | §15.7／§15.10 標記 C1 已規格化並指向本檔 |
| `docs/00-map.md` | 模組表加一列：Locomotion 過渡 → `docs/07-locomotion-transitions.md` |
| `WORKLOG.md` | 交辦段換成 C1 實作與 §11.3 待驗清單 |
| `docs/changelog.md` | 版本條目 |

---

## 13. Risks／Deferred Work

### 13.1 已知風險（帶處置）

| # | 風險 | 影響 | 處置 |
| --- | --- | --- | --- |
| R1 | **已關閉：V1 接線不一致**（§3.4） | 修正前 Walk Stop 前衝 | Prefab 已接 `Bake_SprintFwdLoop`，並由 Play 驗收 |
| R2 | Walk 已等待最近 authored 入場相位；Bake 的單一腳相值仍不是完整骨架 pose 距離 | 理論相位誤差收斂至烘焙取樣／每幀跨越量，但仍需 Play 證實全身連續 | 驗收 §0.4／P11；若仍跳動，下一步是離線完整 pose 特徵，不再調 Fade 或改速度 |
| R3 | 停止距離是 authored constant，且 LU／RU 差 7.4% | 停止距離不可調、且因腳而異 | §8.3 已證明這是 A 方案的本質限制；可控距離＝走 C5 |
| R4 | `LocomotionModel` 職責變重 | 走向 God Class | 選片（純函式）與跨帧狀態（struct）已外移；若 C2/C3 再加兩種過渡，屆時把「過渡段」整組抽成 model 的協作者並回到 G1 重新裁決 |
| R5 | Stop 起始帧的閉包配置 | 破壞「每帧 0 B」的直覺讀數 | §10.5 已量化並寫進 §7.4 判讀規則；根治＝償還 dev-spec §5 的回調 ObjectPool 待辦 |
| R6 | 中斷判定晚一帧（Jump／Roll） | 16 ms | 該帧位移已由 override 接手，無雙重位移；不為此新增介面方法 |
| R7 | 中斷回移動的頓挫（§10.3） | 手感 | §11.3-V8 實測；解法已預先分析（§13.2） |

### 13.2 延後項（附觸發條件與已預先分析的解法）

| 項目 | 觸發條件 | 已知解法方向 |
| --- | --- | --- |
| **Distance Matching（C5）** | C1 選片／曲線／中斷穩定後，且出現「停止距離必須可控」的真需求（對位、掩體、互動） | 以 §8.4 的前向模擬每帧預測剩餘距離；需要新烘焙特徵（累積距離曲線）＋距離→時間反查＋Facade 的通用「設定播放進度」API。**本規格的 playhead-delta 多載已是它的第一塊地基** |
| **Run Stop（C1.1）** | ✅ C1 全部驗收通過，已實作 | 在 §7.3 之前多一層「以入場強度選變體集合」；Selection 演算法零改動 |
| **Sprint Stop** | 有可用的 Sprint `_LU/_RU` Stop 資產 | 目前 Catalog 無資產，維持 B9，不以 Run Stop 代替 |
| **Turn／Pivot（C2／C3）** | Catalog 已備（`Bake_TurnRt180`：`RotationFinishedTime` 1.5333；`Bake_RunFwdTurn180_R_LU`：0.7667、`TargetLocalDirection` (0,0,−1)） | 同一條 Request→Selection→Motion 鏈；**`p ≠ 1` 的 yaw 正確性已由本規格的多載預先解決** |
| **中斷頓挫修正** | §11.3-V8 判定明顯 | 中斷時以曲線當下速度回填 smoother。⚠️ 這會觸及 ADR-003 §13.4「MoveSpeed 恆可由 intent 導出」，**必須先確認是否仍可由 intent 序列重現**（可以，因為整段 Stop 本身就是 intent 觸發的）——屬需要獨立裁決的事，不順手做 |
| **Foot Lock（C6）** | planted-foot 資料與滑腳可量測後 | 不吞進 Foot IK v1（`docs/04` §15.2 邊界紀律 3） |
| **Motion Warping** | Combat／Traversal 出現真實 world target | 一般 Stop 不用它（無 world target），這條界線不變 |
| **Stride／Pose Warping** | 出現「同一支 Stop 要涵蓋連續速度範圍」的需求 | 姿勢後處理，**不得**取代 Request／Selection／Motion Execution 的任何一段 |
| **Motion Matching** | v2.0 研究支線 | 採納會取代現行 locomotion 選片，**必須開新 ADR** |

### 13.3 從技術案例吸收了什麼、明確**沒有**照搬什麼

| 來源 | 吸收 | 沒有照搬 |
| --- | --- | --- |
| Epic Distance Matching | 「停止距離才是 Stop 的主變數」這個觀點；§8.3／§8.4 的距離對照都由此而來 | 不引入 UE 的 Distance Matching 節點與 pose 選取管線；C1 不做距離驅動（C5 才做） |
| TLOU2 Motion Matching | **先分類動作語意（Stopping／Foot Plant），再用腳相／速度選 variant**——本規格的 Selection 就是這個原則的最小版 | 不引入 Motion Matching；不以軌跡相似度猜選片 |
| Animancer End Events | End Event 可能重複觸發、必須立即切走、共享 state 涉及 callback ownership ⇒ §9.2 的世代檢查與「兩份獨立資產」約束 | 不改 `AnimationFacadeBase` 的通用簽名；不依賴 Animancer 私有結構 |
| Animancer Mixer Synchronization | 「同步是改寫子 Playable 速度」這件事解釋了為何 loop 腳相可被解讀，也解釋了為何 §7.2-② 的 Mixer `NormalizedTime` 不可直接當相位 | **不讓 Core 讀 Animancer Mixer**；不把同步當成 Stop 選片依據 |
| Epic Stride／Pose Warping | 姿勢後處理可降滑步的認識 | 明確列為延後；不得取代 Request／Selection／Motion Execution |

### 13.4 是否需要新 ADR？——**結論：不需要，且不新增 Presentation IK → Core 資料邊**

**不需要的依據（逐條對照 CLAUDE.md 路由規則）：**

- 黑板 schema：零改動；`PlayerRuntimeData` 不新增欄位、不新增寫入者（A5 白名單零改動）。
- FSM hierarchy：零改動；不新增 `StateType`、不新增 State 類別。
- 依賴方向：零改動；Core 仍不認識 Animancer（A4），model 仍不認識 FSM／Pipeline。
- ADR-003：**沒有任何一條被推翻**。D3（ambient 狀態 delegate 位移給 active model）、D4（model 自驅動畫參數、
  Facade 維持通用）、§13.4（單一真相）在本規格中都是**被引用來做裁決的依據**，不是被繞過的對象。

⇒ 屬「非架構性的子系統加法」，依規則寫 Living Docs（§12.4），**不開新 ADR、不修改既有 ADR**。

初稿提出的 `Core/Movement/Models → Project.Presentation.IK.FootIKPoseData` 回讀已被實作評審否決。
既有放行是讓 model **驅動**通用 Animation／Motion seam，不代表 Core 可回讀 IK post-process；
`ArchitectureRegressionTests.LayerRules` 已加入 `Project.Presentation.IK` 精確禁令。

現行只使用兩項既有資料：Facade 的通用唯讀主導 child clock，以及已選 tier 的 loop／Stop `MotionBakeData`。
因此黑板、FSM、ownership、hierarchy 與跨層契約皆未變，維持「非架構性的子系統加法」結論，不開新 ADR。
