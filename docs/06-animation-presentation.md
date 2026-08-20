# 動畫呈現子系統規格（Animation Presentation）

> **定位**：`docs/02-dev-spec.md` 的**子系統分卷**（Dev Spec 實作 API 層）。原 dev-spec §3.2 中的**動畫呈現三小節**於 2026-07-25 遷入本檔：`AnimancerFacade`（Transition 資產機制）、`Locomotion 1D Mixer 規格`、`動畫數據 → 配置資料流`。
> **小節標題原樣保留**——既有交叉引用（`dev-spec §3.2「動畫數據 → 配置資料流」` 等）在本檔以同一標題可直接定位，遷檔零斷鏈。
> **未遷入的部分（刻意留在 dev-spec §3.2）**：`StateMachineConfigSO`／`StateParamsSO`（FSM 配置契約，與 §3.3 State Matrix 同族）、`JumpStateParams／JumpLaunchData`、`MotionDriver`（管線順序 6 的驅動契約，§2.1 直接引用）——這三者是**跨領域契約**，依 CLAUDE.md「dev-spec 只留跨領域契約」的分工留在主檔。
> **上游**：動畫門面**抽象**（`AnimationFacadeBase`）仍在 `docs/02-dev-spec.md` §3.1；資產治理規範（clip 不可變、匯入矩陣）在 §0.4。

---

#### AnimancerFacade（Animancer v8 Pro 封裝，v0.16 Transition 資產機制）

```csharp
public class AnimancerFacade : AnimationFacadeBase
{
    [System.Serializable]
    public struct TransitionMapping
    {
        public string StateKey;                // 慣例＝StateType.ToString()；BaseState.AnimationKey 可覆寫
        public TransitionAssetBase Transition; // 抽象基底：ClipTransition / LinearMixerTransition… 皆可承載
    }

    [SerializeField] private AnimancerComponent animancer; // 序列化欄位依 §0.1 豁免條款採 camelCase
    [SerializeField] private List<TransitionMapping> transitionMappings = new();

    private readonly Dictionary<string, TransitionAssetBase> _transitionMap = new();
    private readonly Dictionary<string, AnimancerState> _stateCache = new(); // IsPlaying / child time 查詢依據

    public override void Play(string stateKey) { /* TryGetTransition → animancer.Play(transition) → 快取 state */ }

    public override bool TryGetDominantChildNormalizedTime(string stateKey, out float normalizedTime)
    { /* ParentState 直接子狀態中取最高 Weight；同權重固定取前者；無有效 child 時 false */ }

    public override void PlayWithCallback(string stateKey, System.Action onComplete)
    {
        // ⚠️ 注意：結束回調 lambda 每次呼叫產生一次閉包 GC Alloc，§5 既有待辦（回調 ObjectPool）維持追蹤
    }

    public override void SetFloat(string key, float value)
    {
        // 寫入 Animancer v8 Parameters（ParameterDictionary）；訂閱者由資產內 ParameterName 綁定
    }
}

```

**運作規則（v0.16 定調）**：
1. **資產＝單一真相**：過渡時長（FadeDuration）、播放速度、起始時間、循環、事件全部由 `TransitionAsset` 承載；`Play(string stateKey)` 不提供 duration（2026-07-17 裁決 Q1），杜絕程式碼靜默覆寫資產。未來若出現「執行期動態 fade」需求（受擊打斷等），屆時另開專用重載，不回頭加預設參數。
2. **Awake 建表＋預熱**：一次性建 `_transitionMap`，並以 `animancer.States.GetOrCreate(transition)` 預建全部 AnimancerState——首播的一次性堆配置移到初始化，Play/SetFloat 熱路徑零 GC。
3. **冪等播放**：Animancer 依 `transition.Key` 對應 state，對「已在播放中」的同一資產重複 `Play` 不會重頭播放——Idle/Move 兩鍵映射同一份 Locomotion 資產時，狀態切換動畫層無縫。
4. **查表防線**：映射缺失或資產無效（內部 transition／clip 未指定）時警告並安全返回（不拋例外），與 v0.15 前的 clip 查表防線行為一致；`RollState` 的 `IsPlaying` 防呆鏈不受影響。
5. **SetFloat／SetBool＝通用參數通道**：寫入 Animancer v8 `Parameters`（`ParameterDictionary`，型別化容器無裝箱；string→StringReference 隱轉走 intern 快取，穩態零 GC）。**Facade 不持有任何 Mixer 引用**——「哪個 Mixer 訂閱哪個參數」由 Transition 資產內序列化的 `ParameterName`（StringAsset）決定，資料流：黑板 → 參數字典 → 資產綁定。
6. **子狀態時間＝通用唯讀查詢**：`TryGetDominantChildNormalizedTime` 只依 `stateKey` 查其最高權重直接子狀態；索引迴圈、零配置、同權重固定取前者。Facade 不暴露 Mixer／Clip／child index，沒有動畫圖子狀態概念的後端安全回 false。Mixer root 的 `NormalizedTime` 是子狀態時間的加權聚合，不得拿來代替某支 Bake Data 的實際播放頭。
7. `SetLayerWeight` 的 Lite 警報已移除（Pro 解除限制）；多層混合落地屬 F4（Upper Body Layer）。

#### Locomotion 1D Mixer 規格（F2，v0.16；門檻推導 v0.16.2）

`Locomotion.asset`（`TransitionAsset` 內含 `LinearMixerTransition`）：

| child | threshold（目前手感值） | 原生代表速度 | 依門檻推導的 PlaybackSpeed | SynchronizeChildren（目前） |
|---|---:|---:|---:|---:|
| Idle | 0 | 0 | 1 | ✗ |
| Walk | 0.35 | 1.6443043 m/s | 1.3327742 | ✗ |
| Run | 0.75 | 3.5780573 m/s | 1.3124558 | ✗ |
| Sprint | 1 | 6.2613893 m/s | 1 | ✗ |

- **參數空間＝正規化輸入強度（0~1）**，即 `LocomotionModel` 內部平滑後的 `MoveSpeed`；資產內 `ParameterName` 綁 StringAsset `MoveSpeed`（與 `AnimationFacadeBase.ParamMoveSpeed` 常數一致）。Threshold 是可依手感調整的 Presentation 錨點；一旦偏離天生速度比，子動作 PlaybackSpeed 必須依下方公式同步派生，不能讓兩個值各自手填。
- **腳步循環同步有明確取捨**：Animancer 原生 `SynchronizeChildren` 會以加權 NormalizedTime 對齊腳相，但也會逐幀改寫子 Playable 的實際速度。因此「腳相優先」模式只保證各 Threshold 錨點速度一致；要求 Linear Mixer 整個混合區間的循環平均速度數學一致時，必須使用「速度精確」模式關閉持續同步。逐幀腳掌零滑動仍需 Distance Matching／距離取樣，不得把平均速度校正誤稱為逐幀精確。
- **資料流（管線順序 5）**：`SyncAnimation()` 每幀 `SetFloat(ParamMoveSpeed, data.MoveSpeed)`——兌現 §1.1 權限表「MoveSpeed 的 Reader＝AnimationFacade」的既定設計。M1 裁決（Q2）不做平滑（Game Feel 留後續專門輪）。現況 Move 僅綁 WASD（`2DVector` composite 預設 `DigitalNormalized`，對角線模長＝1，經查證免 Clamp01，裁決 Q3），參數為 0/1 二值：混合區間需類比輸入（搖桿綁定）或 Editor 手動滑參數才踩得到。
- **FSM 拓撲零改動**：Idle/Move 狀態、StateType、Config 資產不動；「兩狀態共用一個表現資產」純由映射表達成（兩鍵指向同一資產）。

#### 動畫數據 → 配置資料流（v0.16.2）

**定位**：`MotionBakeData` 不是「人工查看後抄數字」的一次性分析工具，而是系統配置的可靠**資料真相來源**。`AnimationClip` 是表現資源（Presentation Resource），`MotionBakeData` 是該 clip 真實運動數據（位移、速度、重力、腳相）的權威來源。資料流：

```
AnimationClip（FBX 子 clip，表現資源）
  ↓ MotionBake / Feature Analysis（離線烘焙，§4.1／§4.3）
MotionBakeData（真實數據：SpeedCurve→AutoAverageSpeed、AutoApexHeight、AutoCalculatedGravity、EndPhase、FootPhaseCurve…）
  ↓ GetRepresentativeSpeed() / Auto* 欄位（單一存取契約）
Runtime / Config Data（MotionDriver.moveSpeed、Mixer threshold、JumpStateParams 逆推…）
  ↓
MotionDriver ＋ Locomotion Mixer ＋ Presentation
```

- **代表速度（`AutoAverageSpeed` / `GetRepresentativeSpeed()`）**：`AutoAverageSpeed` 為 `SpeedCurve` 平均瞬時速度；計算時排除烘焙器建立曲線起點所產生的第 0 帧 `time=0/value=0` 哨兵（曲線必須還有後續實際樣本）。烘焙時經 `MotionBakeData.ComputeAverageSpeed` 寫入，與執行期回退共用同一計算，杜絕兩處分歧。`GetRepresentativeSpeed()` 欄位優先、為 0（舊資產未重烘焙）時即時回退算曲線平均——現有資產無需立即重烘焙即可被引用。loop locomotion（Walk／Run）為穩態，平均即代表速度。
- **MotionDriver 速度來源**：`MotionDriver` 新增 `[SerializeField] moveSpeedSource`（`MotionBakeData`，通常指最高速 clip Fast Run）＋ `overrideMoveSpeed`（bool）。`Awake` 時若有來源且未 override，以 `moveSpeedSource.GetRepresentativeSpeed()` 覆寫 `moveSpeed`（滿速＝動畫天生速度、根除滑步）。**唯一寫入時機在啟動**，之後 `moveSpeed` 就是一般序列化欄位，執行期熱路徑零新增成本。
- **Mixer 校正**：不調手感時的自然門檻仍是 `threshold_i = speed_i / speed_max`，此時各子動作 PlaybackSpeed＝1。若設計師把門檻調成 `t_i`，則播放倍率成為派生值：`playback_i = t_i × speed_max / speed_i`。如此每個錨點皆滿足「動畫循環平均速度＝t_i × gameplay 滿速」；關閉 `SynchronizeChildren` 時，Linear Mixer 的線性插值亦使兩錨點間維持同一等式。Idle（t=0）固定倍率 1，不做除零。
- **配置 SOP（不建一次性工具）**：目前只有固定四個 tier，Threshold 與 PlaybackSpeed 直接在 `Locomotion.asset` 設定。重烘造成代表速度改變時，依公式重算三個移動 child 的 PlaybackSpeed；這是低頻、少量的資產配置，不為它增加 Editor 選單、視窗區塊或永久維護面。
- **不破壞 Data/Presentation 分離**：`MotionBakeData`、`MotionDriver` 同屬 `Presentation.Motion`；此資料流是 Presentation 層內部「烘焙資產 → 驅動器」的連接，不跨 Core／Presentation 邊界，黑板 schema 與依賴方向（Pipeline→Facade、State→Config→Bake）皆不變。
- **保留手動調整能力但消除雙重手填**：`moveSpeedSource` 留空或勾 `overrideMoveSpeed` 仍可回到手動 gameplay 滿速；Mixer Threshold 可依手感微調，但 PlaybackSpeed 必須由工具依 Bake Data 派生。Threshold 是設計輸入、PlaybackSpeed 是結果，不形成第二份可獨立漂移的真相。
