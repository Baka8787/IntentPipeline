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
    private readonly Dictionary<string, AnimancerState> _stateCache = new(); // IsPlaying / GetNormalizedTime 依據

    public override void Play(string stateKey) { /* TryGetTransition → animancer.Play(transition) → 快取 state */ }

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
6. `SetLayerWeight` 的 Lite 警報已移除（Pro 解除限制）；多層混合落地屬 F4（Upper Body Layer）。

#### Locomotion 1D Mixer 規格（F2，v0.16；門檻推導 v0.16.2）

`Locomotion.asset`（`TransitionAsset` 內含 `LinearMixerTransition`）：

| child | threshold | SynchronizeChildren | 說明 |
|---|---|---|---|
| Idle | 0 | ✗ | 非步態循環，不參與相位同步（避免拖慢步態群）；依 §0.4 Locomotion-原地 preset |
| Walking | **0.3** | ✓ | 依 §0.4 Locomotion-位移 preset；門檻 0.3 ≈ 1.677/5.66 由動畫數據推導（見下方資料流小節） |
| Fast Run | 1.0 | ✓ | 依 §0.4 Locomotion-位移 preset；門檻 1.0＝速度基準（最高速 clip） |

- **參數空間＝正規化輸入強度（0~1）**，即黑板 `MoveSpeed`（🆕 B9：經 `SmoothDamp` 平滑，讓鍵盤 0/1 輸入平順爬過各 tier）；資產內 `ParameterName` 綁 StringAsset `MoveSpeed`（與 `AnimationFacadeBase.ParamMoveSpeed` 常數一致）。各 child 門檻由動畫天生速度正規化推導（下方資料流小節），非憑感覺手填。徹底消滑步（中間值步速精確匹配）屬 M4（foot-phase）範疇。
- **腳步循環同步**：Animancer 原生 `SynchronizeChildren`（加權 NormalizedTime 對齊），Walk↔Run 混合區腳步不跳相。
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

- **代表速度（`AutoAverageSpeed` / `GetRepresentativeSpeed()`）**：`AutoAverageSpeed` 為 `SpeedCurve` 平均瞬時速度，烘焙時經 `MotionBakeData.ComputeAverageSpeed` 寫入（與執行期回退共用同一計算，杜絕兩處分歧）。`GetRepresentativeSpeed()` 欄位優先、為 0（舊資產未重烘焙）時即時回退算曲線平均——現有資產無需立即重烘焙即可被引用。loop locomotion（Walk／Run）為穩態，平均即代表速度。
- **MotionDriver 速度來源**：`MotionDriver` 新增 `[SerializeField] moveSpeedSource`（`MotionBakeData`，通常指最高速 clip Fast Run）＋ `overrideMoveSpeed`（bool）。`Awake` 時若有來源且未 override，以 `moveSpeedSource.GetRepresentativeSpeed()` 覆寫 `moveSpeed`（滿速＝動畫天生速度、根除滑步）。**唯一寫入時機在啟動**，之後 `moveSpeed` 就是一般序列化欄位，執行期熱路徑零新增成本。
- **Mixer 門檻推導**：`threshold_i = speed_i / speed_max`（各 child clip 代表速度 ÷ 最高速 child 代表速度）。當前值：Walk 0.3 ≈ `Bake_Walking`(1.677) / `Bake_Fast Run`(5.66)、Run 1.0、Idle 0。門檻語意＝「輸入強度到多少時，procedural 速度恰好等於該 clip 天生步速」，故在每個步態錨點上腳步視覺與位移速度同時對齊。（門檻寫入 `LinearMixerTransition` 資產由設計師依此公式手填；是否自動化見 changelog v0.16.2 裁決事項。）
- **不破壞 Data/Presentation 分離**：`MotionBakeData`、`MotionDriver` 同屬 `Presentation.Motion`；此資料流是 Presentation 層內部「烘焙資產 → 驅動器」的連接，不跨 Core／Presentation 邊界，黑板 schema 與依賴方向（Pipeline→Facade、State→Config→Bake）皆不變。
- **保留手動調整能力**：三處皆「Bake 提供預設值＋設計師可 override＋來源可追蹤」——`moveSpeedSource` 留空或勾 `overrideMoveSpeed` 即回手動值；Mixer 門檻可在公式建議外手動微調；比照 CapsuleFitter 的「工具給值、人可覆寫」慣例，但此處刻意不做原子綁定（速度是設計手感參數，非幾何約束，允許 gameplay 天生速度分離）。
