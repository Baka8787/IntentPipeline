# CharacterController 開發規格文件（API / 資料結構）

> **狀態**：草稿 v0.10
> **最後更新**：2026-07-08
> **用途**：實作時的對照表，採「介面先行，實作隨後」原則。

---

## 0. 命名與檔案結構規範

### 0.1 命名規範

* **介面**：`I` 前綴，例如 `IInputSource`
* **抽象基底類別**：`Base` 後綴，例如 `BaseState`
* **ScriptableObject 設定檔**：`SO` 後綴，例如 `StateMachineConfigSO`
* **私有欄位**：`_camelCase`
* **公開屬性 / 欄位**：`PascalCase`

### 0.2 資料夾結構

```
Assets/
  _Project/
    Scripts/
      Core/
        Blackboard/        # RuntimeData, IntentData (黑板資料層)
        Pipeline/          # InputPipeline, MainProcessorPipeline (管線層)
        StateMachine/      # BaseState, FullBodyStateMachine, StateMachineConfigSO
        Interrupt/         # InterruptProcessor, 攔截器
        Arbitration/       # ArbiterPipeline, IArbiter, ArbiterData (仲裁層)
      Presentation/
        Animation/         # AnimationFacadeBase 及其實作
        Motion/            # MotionDriver (根運動與物理結算)
        Audio/             # AudioDriver
      Equipment/
        Definitions/       # ItemDefinition, EquippableItemSO
        Runtime/           # ItemInstance, EquipmentDriver
      Pooling/
      Editor/              # 編輯器工具鏈
        Pipeline/          # AnimationBuildPipeline 核心框架、BuildProfile
        Stages/            # 可插拔管線節點 (Discovery, Validation, Extraction, PostProcess, AssetGen, Dependency, Report)
        Cache/             # BuildCache 雜湊與快取控制邏輯
        Window/            # AnimationBuildReport 可視化視窗
    Prefabs/
    ScriptableObjects/
      StateMachine/        # StateMachineConfigSO 配置資產
  Plugins/

```

---

### 0.3 GameObject 階層規範（v0.9 新增，詳見 01-design-doc.md §2.6）

角色物件一律拆成 **Root（Adapter）** 與 **Model（子物件）** 兩層，禁止把 `Animator` 或其他會產生根動作的元件跟 `CharacterController` 掛在同一顆物件上。

```
CharacterRoot                          <- 邏輯/物理權威層，外部一律引用這一層
  ├─ CharacterController               <- 物理碰撞體與世界座標的唯一權威
  ├─ CharacterPipelineRunner
  ├─ MotionDriver
  ├─ AnimancerFacade (: AnimationFacadeBase)
  ├─ AnimancerComponent
  ├─ PlayerInputSource (: IInputSource)
  └─ Model                             <- 美術/骨骼層，禁止被遊戲邏輯直接引用
       ├─ Animator                     <- Humanoid Avatar；applyRootMotion 必須為 false
       ├─ SkinnedMeshRenderer
       └─ <骨骼階層>                    <- 供未來手部 IK / 武器對位掛點使用
```

**規則**：
1. `CharacterController.center` 的高度基準以 Root 為準，Model 只做視覺呈現，不參與碰撞計算。
2. `AnimancerFacade`（或任何 `AnimationFacadeBase` 子類）掛在 Root，透過 `GetComponentInChildren<AnimancerComponent>()` 尋找 Model 底下的動畫元件——這與目前 `AnimancerFacade.cs` 既有寫法完全相容，不需要改介面。
3. `Model` 底下的 `Animator.applyRootMotion` 必須顯式設為 `false`。除了 Inspector 手動關閉外，建議在 `AnimancerFacade.Awake()` 或等效初始化流程中程式碼強制覆寫一次，避免未來換模型/美術誤勾選又忘記關閉。
4. 任何遊戲邏輯（狀態機、Controller、Driver）一律只能持有 **Root** 的 `Transform` 引用；`ThirdPersonCamera.target` 等外部模組同理只能指向 Root。

---



### 1.1 PlayerRuntimeData（全域黑板）

```csharp
public class PlayerRuntimeData
{
    // === 意圖區（每帧處理完即復位）===
    public IntentData Intent;

    // === 參數區（持續存在，每帧更新）===
    public float MoveSpeed;
    public Vector2 MoveDirection;
    public float UpperBodyWeight;
    public Transform CameraTransform;

    // ✅（v0.7 規劃、v0.8 實作完成）由 MotionDriver.GetGravityThisFrame(data) 每帧統一寫入，
    // 供狀態邏輯讀取地面接觸狀態，取代 JumpState 內部原本固定計時器模擬落地判定的做法
    public bool IsGrounded;

    // 🆕（v0.10 定案，尚未實作）取代 MotionDriver 內部私有欄位 _verticalVelocity，
    // 讓非 Jump 狀態（未來的擊退、彈跳台、翻越）也能直接改垂直速度，不必為每個情境各開一個 ApplyXxxImpulse 方法。
    // 寫入權限比照 CurrentWeapon 用 internal set 限制在 Project.Core 命名空間內（主要是 MotionDriver 與各狀態類別），
    // 一般表現層 Controller 仍只能讀。
    public float VerticalVelocity { get; internal set; }

    // 🆕（v0.10 定案，尚未實作）單幀邊沿旗標，由 MotionDriver.GetGravityThisFrame(data) 比較
    // 本幀與上一幀 IsGrounded 的差異計算得出，僅在觸發那一幀為 true，下一幀自動復位。
    // 供音效／鏡頭震動／落地特效等表現層 Controller 直接訂閱，不必自己追蹤上一幀的 IsGrounded。
    public bool JustLanded;
    public bool JustLeftGround;

    // === 仲裁區（由 ArbiterPipeline 每帧寫入，各表現層 Controller 唯讀）===
    // 註：維持公開欄位而非 Property，避免 struct 值複製導致無法直接修改內部旗標
    public ArbiterData Arbitration;

    // === 引用區 ===
    public ItemInstance CurrentWeapon;
    public Transform AimTarget;
}

```

#### 💡 黑板讀寫權限表

| 欄位 | 型別 | 誰寫入 | 誰讀取 | 備註 |
| --- | --- | --- | --- | --- |
| **Intent** | `IntentData` (struct) | InputPipeline | 狀態機 | 每帧結尾自動復位 |
| **MoveSpeed** | `float` | Parameter Processor | AnimationFacade | 控制 BlendTree 混合 |
| **CurrentWeapon** | `ItemInstance` | EquipmentDriver | 多處 | 唯讀引用，禁止外部修改內容 |
| **Arbitration** | `ArbiterData` (struct) | ArbiterPipeline | 各表現層 Controller | 每帧統一覆寫，Controller 只讀不寫 |
| **IsGrounded** | `bool` | MotionDriver（於 `GetGravityThisFrame(data)` 內部統一寫入，所有移動路徑最終都會呼叫此方法，來源 `CharacterController.isGrounded`） | 狀態機（如 `JumpState.IsLanded`） | ✅ v0.7 規劃、v0.8 實作完成；已取代 `JumpState` 內部原本的固定計時器落地判定 |
| **VerticalVelocity** | `float`（`internal set`） | MotionDriver、`Project.Core` 內的狀態類別 | 各表現層 Controller（唯讀） | 🆕 v0.10 定案，尚未實作；取代 `MotionDriver` 私有欄位 `_verticalVelocity` |
| **JustLanded / JustLeftGround** | `bool` | MotionDriver（於 `GetGravityThisFrame(data)` 內比較前後兩幀 `IsGrounded`） | 音效／鏡頭／特效等表現層 Controller | 🆕 v0.10 定案，尚未實作；單幀觸發，下一幀自動復位 |

> ⚠️ **`ref struct` 相容性警語**：`InputData` 已升版為 `ref struct`（見 1.3 節），**絕對不能**成為 `PlayerRuntimeData` 的欄位。黑板只能持有處理後轉換的 `IntentData` 或一般參數。違反此邊界將導致編譯直接失敗。

### 1.2 IntentData（意圖輕量結構）

```csharp
public struct IntentData
{
    public bool JumpRequested;
    public bool RollRequested;
    public bool FireRequested;
    // 結構體 + 整體覆寫 = 復位時零 GC Alloc
}

```

### 1.3 InputData（原始輸入，Stack-Only）

```csharp
public ref struct InputData
{
    public Vector2 MoveInput;
    public Vector2 LookInput;
    public bool JumpButtonDown;
    public bool RollButtonDown;
    public bool FireButtonDown;
}

```

> 🛑 **使用限制（執行盲區）**：
> * 只能存活在 Stack 上，**不能**被任何 `class` 持有為欄位。
> * 不能裝箱（Boxing）、不能用於 `async/await` 方法或 `yield return` 迭代器。
> * **後續動作**：用 Unity Profiler 量測升版前後的 GC Alloc 差異，截圖存入 `/docs/profiler/` 並更新 `01-design-doc.md`。
> 
> 

### 1.4 ArbiterData（功能仲裁旗標）

```csharp
public struct ArbiterData
{
    public bool BlockInput;       // 輸入封鎖：Intent Processor 停止寫入意圖
    public bool BlockIK;          // IK 封鎖：IK Controller 跳過本帧更新
    public bool BlockAudio;       // 音頻封鎖：Audio Controller 靜音
    public bool BlockExpression;  // 表情封鎖：表情 Controller 跳過更新
}

```

#### 💡 仲裁觸發情境表

| 旗標 | 誰寫入 | 誰讀取 | 典型觸發情境 |
| --- | --- | --- | --- |
| **BlockInput** | ArbiterPipeline | PipelineRunner | 死亡、被定身 CC、過場動畫 |
| **BlockIK** | ArbiterPipeline | IK Controller | 死亡、角色不可見、LOD 降級 |
| **BlockAudio** | ArbiterPipeline | Audio Controller | 死亡、劇烈爆炸靜音、LOD 降級 |
| **BlockExpression** | ArbiterPipeline | 表情 Controller | 死亡、頭部被全罩式頭盔遮擋 |

---

## 2. 核心管線與生命週期（Pipeline Layer）

### 2.1 Pipeline 處理順序規格表

| 順序 | 處理器 | 輸入 | 輸出 | 執行時機與關鍵備註 |
| --- | --- | --- | --- | --- |
| **1** | InputPipeline | 裝置原始輸入 | `InputData` | `ref struct` 採樣，隨後即銷毀。 |
| **2** | Intent Processor | `InputData` | `RuntimeData.Intent` | 若 `Arbitration.BlockInput == true` 則跳過。 |
| **3** | Parameter Processor | `RuntimeData` | `RuntimeData`（更新參數） | 計算當幀 MoveSpeed、移動方向等。 |
| **4** | 狀態機 Tick | `RuntimeData` | 狀態切換與邏輯驅動 | 讀取 Intent。讀完當幀即視為消耗完畢。 |
| **4.5** | ArbiterPipeline Tick | `RuntimeData` (含新狀態) | `RuntimeData.Arbitration` | **第四階段接入**，緊跟狀態機之後評估最新旗標。 |
| **5** | AnimationFacade 同步 | `RuntimeData` / 當前狀態 | 動畫播放指令 | 向 Animancer 提交當幀播放請求。 |
| **6a** | MotionDriver 基礎運動 | Animancer 根運動增量 | `CharacterController.Move` | **必須在 LateUpdate**，確保動畫已結算。 |
| **6b** | MotionDriver 烘焙補償 | `MotionBakeData` + 目標點 | 附加補償位移 | **與 6a 同幀 LateUpdate 執行**，進行動態吸附。 |
| **7** | IntentData.Reset() | — | `RuntimeData.Intent` 清空 | **LateUpdate 末尾**執行，確保所有讀取方已消耗。 |

> ⚠️ **生命週期脆弱點警告**：
> 1. `IntentData.Reset()` 必須死守在管線最後（順序 7），若不小心提前，當幀意圖會在狀態機讀取前被清空。
> 2. `ArbiterPipeline Tick`（順序 4.5）必須卡在狀態機**之後**、動畫表現層**之前**，確保動畫能讀到當幀最新的封鎖狀態。
> 3. `FullBodyStateMachine.Initialize()` 必須由 `CharacterPipelineRunner.Start()` 呼叫（**禁止放在 Awake**），確保黑板資料已完全初始化。
> 
> 

### 2.2 管線處理器重構介面（規劃中）

> 💡 **重構訊號**：當 `ProcessIntents` 或 `ProcessParameters` 內的 `if-else` 分支超過 10~15 行時，立即啟動此重構。

```csharp
public interface IIntentProcessor
{
    void Process(ref InputData input, ref IntentData intent); // 必須使用 ref 傳遞
}

public interface IParameterProcessor
{
    void Process(ref InputData input, PlayerRuntimeData data);
}

```

---

## 3. 狀態機與動畫表現系統（Presentation Layer）

### 3.1 驅動介面與核心類別

#### IInputSource（輸入源）

```csharp
public interface IInputSource
{
    void FetchRawInput(ref InputData data); // v0.3 升版：採 ref 寫入，杜絕 GC
}

```

#### BaseState（狀態基底）

```csharp
public abstract class BaseState
{
    public abstract StateType Type { get; }
    protected StateMachineConfigSO Config;

    public virtual void Initialize(StateMachineConfigSO config) => Config = config;
    
    public abstract bool CanEnter(PlayerRuntimeData data);
    public abstract void OnEnter(PlayerRuntimeData data);
    public abstract void OnTick(PlayerRuntimeData data, float deltaTime);
    public abstract void OnExit(PlayerRuntimeData data);

    // 預設由 SO 配置驅動打斷規則；子類別可 override 處理特殊情況（如無敵幀不可打斷）
    public virtual bool CanBeInterruptedBy(BaseState other)
    {
        return Config != null && Config.CheckCanInterrupt(this.Type, other.Type);
    }

    // 控制是否允許自然過渡。有鎖定期的狀態（Jump、Roll）應在動作結束前 override 為 false
    public virtual bool CanTransitionAway => true;
}

```

#### FullBodyStateMachine（狀態機主體）

```csharp
public class FullBodyStateMachine
{
    private readonly Dictionary<StateType, BaseState> _stateRegistry = new();
    private BaseState _currentState;
    private StateMachineConfigSO _config;

    public BaseState CurrentState => _currentState;

    public void Initialize(StateMachineConfigSO config, PlayerRuntimeData data) { ... }

    public void Tick(PlayerRuntimeData data, float deltaTime)
    {
        // 1. 執行當前狀態 OnTick
        // 2. EvaluateInterrupts (意圖打斷，由優先級排序評估)
        // 3. EvaluateTransitions (自然過渡，需 CanTransitionAway == true)
    }
}

```

#### AnimationFacadeBase（動畫門面抽象）

```csharp
public abstract class AnimationFacadeBase : MonoBehaviour
{
    public abstract void Play(string stateKey, float transitionDuration = 0.15f);
    public abstract void PlayWithCallback(string stateKey, System.Action onComplete, float transitionDuration = 0.1f);
    public abstract void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f);
    public abstract void SetFloat(string key, float value);
    public abstract void SetBool(string key, bool value);
    public abstract bool IsPlaying(string stateKey);
    public abstract float GetNormalizedTime();
}

```

---

### 3.2 狀態機與動畫具體實作（第三階段）

#### StateMachineConfigSO（設定檔資產）

```csharp
[Serializable]
public struct StateRule
{
    public StateType State;
    [Tooltip("哪些狀態可以主動打斷當前狀態（意圖觸發時檢查）")]
    public List<StateType> CanBeInterruptedBy;
    [Tooltip("當前狀態結束或無意圖時，允許自然過渡到的狀態優先級")]
    public List<StateType> ValidTransitions;
    public int Priority; // 用於 EvaluateInterrupts 當幀多意圖同時觸發時的排序
}

[CreateAssetMenu(fileName = "StateMachineConfig", menuName = "Project/Core/StateMachineConfig")]
public class StateMachineConfigSO : ScriptableObject
{
    [SerializeField] private List<StateRule> rules;
    private Dictionary<StateType, List<StateType>> _interruptMap;
    private Dictionary<StateType, List<StateType>> _transitionMap;

    public void Initialize() { /* List → Dictionary 建立 O(1) 執行期查表 */ }
    public bool CheckCanInterrupt(StateType current, StateType next) { ... }
    public IReadOnlyList<StateType> GetValidTransitions(StateType state) { ... }
}

```

> ⚠️ **v0.10 更新**：實際程式碼在 v0.7/v0.8 曾把 `JumpImpulseForce`／`JumpTakeoffDelay` 直接加進 `StateRule`（上面這份介面草稿沒有反映這兩個欄位，兩者是分開演進的）。經盤點確認這違反單一職責原則，且會在多遊戲模式擴充時讓 `StateRule` 線性膨脹。**目標設計**改為下面的 `StateParamsSO` 方案，`StateRule` 維持只有拓撲欄位（`State`／`Priority`／`CanBeInterruptedBy`／`ValidTransitions`），不再新增任何狀態專屬的物理/表現參數。詳見 `01-design-doc.md` §2.7、§5 Trade-off 表 v0.10 決策列。

#### StateParamsSO（狀態專屬參數資產，v0.10 新增設計，尚未實作）

**動機**：`StateRule` 的職責是 FSM 拓撲（誰能打斷誰、能過渡到誰、優先級），跟具體狀態要用什麼物理參數表現自己（Jump 的衝量、Roll 的翻滾距離、未來 Slide 的摩擦係數）是兩個完全不同的關注點。混在同一個結構體會導致：
1. Inspector 上不相關的狀態也會看到不屬於自己的欄位（例如設定 Idle 時也看得到 `Jump Impulse Force`）。
2. 每個 `StateRule` 元素都攜帶所有狀態的參數欄位，執行期有用不到的記憶體浪費。
3. 隨著 ARPG／射擊等模式陸續加入 SlideState、ClimbState、AimState，`StateRule` 會線性膨脹成沒人敢動的巨石結構——這正是專案要支援多遊戲模式的目標下，最需要提前避免的坑。

**介面設計**：

```csharp
/// <summary>
/// 狀態專屬參數資產的抽象基底。只是型別標記，不放任何共用欄位——
/// 共用欄位屬於 StateRule 的職責，不該在這裡重複。
/// </summary>
public abstract class StateParamsSO : ScriptableObject
{
}

// 範例：Jump 專屬參數，取代原本塞進 StateRule 的 JumpImpulseForce / JumpTakeoffDelay
[CreateAssetMenu(fileName = "JumpStateParams", menuName = "Project/Core/StateParams/Jump")]
public class JumpStateParams : StateParamsSO
{
    [Tooltip("起跳發射初速度 (m/s)。0 或未設定時，狀態端會 fallback 到程式碼內建預設值")]
    public float ImpulseForce;

    [Tooltip("衝量注入前的延遲秒數，用於等待動畫預備/蹲下姿勢播完。0 = 不延遲")]
    public float TakeoffDelay;
}

// 未來範例（尚未實作，僅供設計參考）：
// public class SlideStateParams : StateParamsSO { public float SlideDistance; public AnimationCurve FrictionCurve; }
// public class ClimbStateParams : StateParamsSO { public float ClimbSpeed; public float StaminaCostPerSecond; }
```

`StateMachineConfigSO` 擴充：

```csharp
[Serializable]
public struct StateParamsMapping
{
    public StateType State;
    [Tooltip("該狀態使用的專屬參數資產，型別需繼承 StateParamsSO；若該狀態不需要調參則留空")]
    public StateParamsSO Params;
}

// StateMachineConfigSO 內新增：
[SerializeField] private List<StateParamsMapping> paramsMappings = new();
private readonly Dictionary<StateType, StateParamsSO> _paramsMap = new();

// Initialize() 內新增對應的建表邏輯（比照 _bakeMap 的模式）

/// <summary>
/// 泛型查表，呼叫端指定期望型別；查無資料或型別不符時回傳 null，呼叫端自行 fallback。
/// </summary>
public TParams GetStateParams<TParams>(StateType state) where TParams : StateParamsSO
{
    return _paramsMap.TryGetValue(state, out var so) ? so as TParams : null;
}
```

呼叫端範例（`JumpState.Initialize`）：

```csharp
public override void Initialize(StateMachineConfigSO config)
{
    base.Initialize(config);

    var p = config?.GetStateParams<JumpStateParams>(Type);
    if (p != null)
    {
        if (p.ImpulseForce > 0f) _jumpImpulseForce = p.ImpulseForce;
        _takeoffDelay = Mathf.Max(0f, p.TakeoffDelay);
    }
}
```

**已知限制**：`GetStateParams<T>` 用 `as` 轉型，型別掛錯（例如 Jump 狀態誤掛了 `RollStateParams`）只會靜默回傳 `null`，不會報錯——目前先靠呼叫端的 fallback 預設值兜底，避免整個管線因為一個掛錯的資產而崩潰；長期可以考慮加一個 Editor 驗證工具，在 `StateMachineConfigSO` 存檔時檢查每筆 `StateParamsMapping` 掛的資產型別是否符合預期。

#### AnimancerFacade（Animancer Lite 封裝）

```csharp
public class AnimancerFacade : AnimationFacadeBase
{
    [SerializeField] private AnimancerComponent _animancer;
    // TODO: 建立 stateKey → AnimationClip 的映射資產機制

    public override void Play(string stateKey, float transitionDuration = 0.15f) { ... }

    public override void PlayWithCallback(string stateKey, System.Action onComplete, float transitionDuration = 0.1f)
    {
        // ⚠️ 注意：避免每次 new lambda 產生 GC Alloc，後續需接入物件池
    }

    public override void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f)
    {
        #if UNITY_EDITOR
        // Lite 版限制：Layer 1 以上在 Build 後無效。在此加入防禦 Log 避免靜默失效。
        if(layerIndex > 0) Debug.LogWarning("Animancer Lite 不支援 Runtime 多層混合！");
        #endif
    }
    // ... 其餘實作 ...
}

```

#### MotionDriver（根運動與補償驅動）

> 🛑 **已知風險（2026-07-08 除錯發現）**：下方 `OnAnimatorMove` 路徑正確運作，同時依賴以下外部設定全部對齊，任一項偏離都會表現為「動畫原地不動」或「動作結束瞬移」：
> 1. `Animator`／`CharacterController`／`MotionDriver` 須在**同一個 GameObject**（`OnAnimatorMove` 不會跨物件傳遞）。
> 2. `Animator.applyRootMotion` 必須勾選。
> 3. `Animator.Animate Physics` 必須**不**勾選（勾選會讓回呼落在 FixedUpdate 節奏，與本類別以 `Time.deltaTime` 做每渲染幀積分的假設衝突）。
> 4. 動畫匯入設定的 `Root Transform Position (XZ) → Bake Into Pose` 必須**不**勾選（勾選會讓水平位移被烤進骨架姿勢，讀不到 `deltaPosition`）。
> 5. 任何繞過 `ExecuteBaseMovement` 的位移路徑（如 `ExecuteBakedCurveMovement`）都必須自行歸零 `_rootMotionDelta`，否則會累積殘留量並在切回 `ExecuteBaseMovement` 時一次性噴出。
>
> 中期正評估改為完全不依賴執行期 `OnAnimatorMove`、統一以「輸入速度 + 烘焙曲線速度」驅動的替代架構，降低上述耦合，尚未定案，見 `01-design-doc.md` §5 Trade-off 表。
>
> ⚠️ **文件落後於實作提醒（v0.9 複查）**：下方 code block 是本節最初的規劃版偽代碼，實際上專案已經在 v0.5～v0.8 期間走完「完全不依賴 `OnAnimatorMove`」這條路線，目前 `MotionDriver.cs` 是純程式碼驅動（`ExecuteBaseMovement` / `ExecuteBakedCurveMovement` / `ApplyBakedCompensation` 都改吃 `PlayerRuntimeData data` 參數、`IsGrounded` 也統一在 `GetGravityThisFrame(data)` 內寫回黑板）。下方 code block 僅供理解「最初評估過的替代方案長相」，**不代表目前實作**，避免直接照抄。

```csharp
public class MotionDriver : MonoBehaviour
{
    [SerializeField] private AnimancerFacade _animancerFacade;
    [SerializeField] private CharacterController _characterController;
    private Vector3 _rootMotionDelta = Vector3.zero;

    private void OnAnimatorMove()
    {
        if (_animancerFacade != null) _rootMotionDelta += _animancerFacade.GetDeltaPosition();
    }

    public void ExecuteBaseMovement() // 【管線順序 6a】
    {
        if (_rootMotionDelta != Vector3.zero)
        {
            _characterController.Move(_rootMotionDelta);
            _rootMotionDelta = Vector3.zero;
        }
    }

    public void ExecuteBakedCurveMovement(MotionBakeData bakeData, float normalizedTime)
    {
        if (bakeData == null) return;
        float currentTime = normalizedTime * bakeData.Duration;
        float previousTime = Mathf.Max(0f, currentTime - Time.deltaTime);

        Vector3 moveDelta = transform.forward * bakeData.GetSpeedAt(currentTime) * Time.deltaTime;
        float deltaYaw = bakeData.GetRotationAt(currentTime) - bakeData.GetRotationAt(previousTime);
        
        transform.Rotate(Vector3.up, deltaYaw);
        _characterController.Move(moveDelta);
    }

    public void ApplyBakedCompensation(MotionBakeData bakeData, Vector3 actualTarget, float normalizedTime) // 【管線順序 6b】
    {
        if (bakeData == null) return;
        float currentTime = normalizedTime * bakeData.Duration;
        float remainingTime = bakeData.Duration - currentTime;

        if (remainingTime <= 0.001f) { ExecuteBakedCurveMovement(bakeData, normalizedTime); return; }

        // 1. 轉向補償 (Slerp Alignment)
        Vector3 toTarget = actualTarget - transform.position; toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime / remainingTime);
        }

        // 2. 位移扭曲補償 (Warping 計算法則)
        float curveSpeed = bakeData.GetSpeedAt(currentTime);
        Vector3 baseMoveDelta = transform.forward * curveSpeed * Time.deltaTime;

        Vector3 distanceToGo = actualTarget - transform.position; distanceToGo.y = 0f;
        Vector3 requiredVelocity = distanceToGo / remainingTime;
        Vector3 compensationVelocity = requiredVelocity - (transform.forward * curveSpeed);

        _characterController.Move(baseMoveDelta + (compensationVelocity * Time.deltaTime));
    }
}

```

---

### 3.3 狀態邏輯規則表（State Matrix）

| 狀態 | 所屬層 | CanTransitionAway 條件 | 可被誰意圖打斷 | 自然過渡目標（依優先級） | 核心邏輯備註 |
| --- | --- | --- | --- | --- | --- |
| **Idle** | FullBody | 永遠 `true` | Move, Jump, Roll | Move | 無意圖且速度時回歸。 |
| **Move** | FullBody | 永遠 `true` | Jump, Roll | Idle | MoveSpeed < 0.1f 時自動自然過渡回 Idle。 |
| **Jump** | FullBody | `IsLanded == true` | 空中狀態不可被打斷 | Idle, Move | 落地瞬間依黑板 MoveSpeed 決定自然過渡目標。 |
| **Roll** | FullBody | `IsRollFinished == true` | 翻滾中強制不可打斷 | Idle, Move | 動畫全程享有不可打斷的「無敵幀」語意。 |

---

## 4. 編輯器工具鏈：動畫烘焙與特徵提取（Editor Tooling）

> 🛠️ **本章節為離線工具組規格**：用於生成運行時 `MotionDriver` 所需的資料資產（`MotionBakeData` / `WarpedMotionData`）。
> 🛠️ **離線內容建置系統藍圖**：本工具鏈最終將演進為標準的 `Animation Build Pipeline`，包含：
> Source Discovery ──> Validation ──> Feature Extraction ──> Feature Post Process ──> Asset Generation ──> Dependency Update ──> Report
> 
> **現階段（第三階段）落地策略**：暫不寫通用框架類別，但在 `RootMotionExtractor.cs` 與 `WarpedMotionExtractor.cs` 內部，必須將程式碼嚴格依此順序切分為私有方法（Private Methods），確保未來能零代價重構拆分。

### 4.1 常規運動特徵提取（`RootMotionExtractor.cs`）

解決 Unity 導出四元數突變、動畫超限抖動、滑步判定問題。

1. **環境動態構造**：實例化臨時角色 Model，注入 Humanoid Avatar，設 `applyRootMotion = true`。掛載 `HideFlags.HideAndDontSave` 防止場景殘留殘渣。
2. **多階段特徵採樣與建置管線**：

* **【Extraction 階段】時間軸原始數據採樣**
    * **Pass 1 (原始幾何與骨骼採樣)**：呼叫 `AnimationClip.SampleAnimation` 逐影格步進，撈取最純粹的引擎底層軌跡。
        * *連續偏航角（Yaw）原始採樣*：利用 $\Delta R = R_{current} \cdot R_{last}^{-1}$ 提取旋轉增量，投影至 XZ 水平面（`forward.y = 0`），透過 `Vector3.SignedAngle` 換算並直接累加至原始數據陣列。
        * *骨骼特徵腳相採樣*：動態抓取 Humanoid 的 `LeftFoot` 與 `RightFoot` 骨骼，透過 `InverseTransformPoint` 將世界坐標轉回相對本地坐標，比對高度 $Y$ 值，低者判定為當前「落地腳（`FootPhase`）」。
    * **Pass 2 (原始速度採樣)**：依據目標 `PlaybackSpeed` 縮放時間軸，二次步進採樣計算 XZ 瞬時的原始物理速度。

* **【Post Process 階段】特徵數據二次加工與濾波**
    * *逆向容忍度掃描（數據裁剪）*：為防止動畫末尾的過沖抖動干擾打斷點，工具在此階段由數據尾端**逆向（由後往前）**掃描。當兩幀間角度差首度超越臨界閾值 `_rotationAngleToleranceDeg` 時，其下一幀（`i+1`）即標定為 `RotationFinishedTime`，用以裁剪無效的過沖抖動。
    * *速度與旋轉平滑濾波（數據優化）*：針對 Extraction 階段採樣出的原始速度與偏航角曲線，進行低通濾波（Low-pass Filter）或噪點過濾，從根本上杜絕因動畫超限抖動產生的滑步判定與視覺微抖動。

3. **反射硬碟落地**：利用 C# 反射遞迴遍歷 `PlayerSO` 等序列化入口，自動尋找匹配的資料欄位覆寫。調用 `EditorUtility.SetDirty` 與 `AssetDatabase.ForceReserializeAssets` 強制執行硬碟硬性序列化。

> ✅ **已解決（v0.7 文件盤點更新）**：v0.5.1 曾記錄 `MotionBakeEditor.cs` 對空 `GameObject`（無 `Animator`／`Avatar`）直接呼叫 `AnimationClip.SampleAnimation` 的技術債。經 2026-07-08 複查，目前程式碼**已依本節第 1 點實作**：`ExecuteBakePipeline()` 會 `Instantiate(characterPrefab)`、檢查 `animator.avatar != null && animator.avatar.isHuman`（不合法則中斷並跳錯誤對話框）、設定 `animator.applyRootMotion = true` 後才呼叫 `BakeCoreProcessor` 採樣。**先前「所有既有 MotionBakeData 資產視為不可信」的警語已不再適用於此問題**；若手上仍有 v0.5.1 之前烘焙出來的舊資產，建議重新烘焙一次以確保是用目前這版真人形 Avatar 流程產生的。
>
> 🛑 **殘留落差（尚未實作）**：目前的 `BakeCoreProcessor` 仍只做「單一迴圈同時採樣速度與偏航角」的簡化版本，尚未落實本節後段要求的：
> 1. Pass 1／Pass 2 分離（原始幾何骨骼採樣與速度採樣分兩階段）
> 2. 骨骼腳相採樣（`LeftFoot`／`RightFoot` 落地腳判定 `FootPhase`）
> 3. Post Process 階段的逆向容忍度裁剪（`RotationFinishedTime`）與低通濾波
>
> 這三項列入 §5 待補清單，屬於功能完整度落差，不影響目前已修復的「取樣來源正確性」問題。

### 4.2 高階動態扭曲特徵提取（`WarpedMotionExtractor.cs`）

針對 3D 空間立體位移（如翻牆、側滑）進行**物理模擬與特徵分段技術**。

1. **真實物理增量採樣（重大技術差異）**：
不使用強刷姿勢的 `SampleAnimation`（該方法不觸發 Root Motion 物理結算）。
* *作法*：構造 `AnimatorOverrideController` 將 Clip 掛載至運行狀態機，在迴圈中實時呼叫 `animator.Update(deltaTime)`，**強迫 Unity 引擎執行實質物理步進**。精確擷取 `animator.deltaPosition` 與 `animator.deltaRotation`。確保烘焙資料與運行期 `CharacterController` 的物理表現 $100\%$ 一致。


2. **三軸本地速度解耦 (3D Local Velocity)**：將擷取到的世界位移增量 `worldDelta` 透過 `transform.InverseTransformVector(worldDelta)` 逆矩陣轉換為當下影格的局部向量，除以 $\Delta t$ 換算為速度，拆分儲存為 $X$（左右）、$Y$（上下）、$Z$（前後）三條獨立的 `AnimationCurve`。
3. **自動化物理臨界點探測 (Automated Warp Points)**：實時記錄局部位移絕對軌跡陣列 `absolutePositions[]`。
* 若設定為 `Vault`（翻越）：自動遍歷尋找 `absolutePositions[i].y` 最大（最高點）的一幀，自動標記為 **"Apex"** 特徵點。
* 若設定為 `Dodge`（閃避）：自動遍歷尋找水平二維向量（$X, Z$）模長最大（移位最遠）的一幀，自動標記為 **"MaxDodge"** 特徵點。


4. **局部分段相對積分 (Segmented Relative Offsets)**：
在烘焙結尾執行相對化計算：

$$\text{BakedLocalOffset} = \text{CurrentAbsPos} - \text{LastAbsPos}$$



將特徵點坐標換算為「相對於上一個特徵點的相對位移」。將整個動作切成數個獨立的物理線段（例如：起跳 $\rightarrow$ 最高點 $\rightarrow$ 落地）。運行時 Motion Warping 系統便能獨立對齊、縮放其中某一段，而不會破壞其他分段的完美表現。

---

## 5. 待補充規格清單（Project Management）

### 第二階段進度（已完成）

* [x] `StateMachineConfigSO` 加入 `Priority` 欄位，`EvaluateInterrupts` 改為手動迴圈比大小，確保零 GC 確定性仲裁。
* [x] Animancer Lite v8 評估結論補入 `01-design-doc.md` Trade-off 表。

### 第三階段（目前衝刺中）

* [ ] `AnimancerFacade` 實作：建立 `stateKey` $\rightarrow$ `AnimationClip` 的映射配置資產機制。
* [ ] `PlayWithCallback` 的回調分配器（ObjectPool）實作，消除 Lambda 的 GC Alloc 隱患。
* [ ] `MotionDriver` 基礎版實作：驗證 LateUpdate 根運動物理同步。
* [ ] 動畫烘焙 Editor 工具實作（`RootMotionExtractor`）：以跳躍落地動畫進行首波驗證。
* [ ] `MotionDriver` 進階版：接入 `MotionBakeData`，驗證目標點補償誤差 $< 0.01\text{m}$。
* [ ] 上半身 Layer 實作（持槍/空手切換），確認 Lite 限制下的 Editor 表現行為。
* [x] **（v0.6，複查後確認已完成）** 依 4.1 節規格重寫 `MotionBakeEditor.cs`：改為實例化真實 Humanoid Prefab + 檢查 `avatar.isHuman` + `applyRootMotion = true` 後再採樣。**2026-07-08 複查確認此項已在程式碼中落地**，詳見上方 §4.1 已解決說明；仍缺 Pass 1/2 分離與腳相/濾波後處理，已拆成下方新項目追蹤。
* [x] **（新增，v0.6）** 補齊 `JumpState` 的垂直位移設計：明確定義起跳上升段是「純動畫根運動」還是「程式碼注入初速度（`ApplyJumpImpulse`）」，避免與 Roll 的水平曲線移動模式混用導致重力失效。**已實作**：`JumpState.OnUpdateMotion` 第一幀呼叫 `MotionDriver.ApplyJumpImpulse`，採用「程式碼注入初速度＋重力每幀積分」路線。
* [ ] **（新增，v0.6）** 評估是否將 `MotionDriver` 重構為不依賴執行期 `OnAnimatorMove` 的統一速度模型（輸入速度 + 烘焙曲線速度，單一 `CharacterController.Move()` 出口，重力每幀快取一次），降低目前對 Animator 設定/匯入設定/GameObject 階層的多重外部依賴。
* [ ] **（新增，v0.7，Code Review 發現）** `RootMotionExtractor` 補齊 Pass 1/Pass 2 分離、`LeftFoot`／`RightFoot` 骨骼腳相採樣（`FootPhase`）、Post Process 階段的逆向容忍度裁剪與低通濾波，目前 `MotionBakeEditor.cs` 只做到單一迴圈的速度＋偏航角採樣。
* [x] **（v0.7 提出，v0.7 當輪實作完成）** `PlayerRuntimeData` 補上 `IsGrounded` 欄位；`JumpState.IsLanded` 改讀黑板旗標，取代固定計時器判定。**v0.8 再優化**：同步時機從「`CharacterPipelineRunner.LateUpdate` 額外呼叫 `SyncGroundedState`」收斂進「`MotionDriver.GetGravityThisFrame(data)` 內部統一寫入」，見下方 v0.8 項目。
* [x] **（v0.7 提出，v0.7 當輪實作完成）** `JumpState.jumpImpulseForce` 改為從 `StateMachineConfigSO.GetJumpImpulseForce(Type)` 查表取得，不再依賴失效的 `[SerializeField]`。
* [x] **（v0.7 提出，v0.7 當輪實作完成）** `MotionDriver.Awake()` 補上 `characterController` 缺失時的 `Debug.LogError` 防呆。
* [x] **（v0.7 提出，v0.7 當輪實作完成）** `RollState.OnUpdateMotion` 加入 `animationFacade.IsPlaying(AnimationKey)` 檢查，失敗時退回 `ExecuteBaseMovement`。
* [x] **（v0.7 提出，v0.7 當輪實作完成）** `CharacterPipelineRunner.ProcessIntents` 的富文本 `Debug.Log` 包進 `#if UNITY_EDITOR`。
* [x] **（v0.7 提出，v0.7 當輪實作完成）** `AnimancerFacade.SetLayerWeight` 補上 `layerIndex < 0` 邊界檢查；`clipMappings` 補 `= new()` 保底。
* [x] **（新增，v0.8，實測發現）** Jump「先蹲下再往上」問題：新增可設定的 `StateRule.JumpTakeoffDelay`，`JumpState` 延遲期間維持一般貼地移動，時間到才呼叫 `ApplyJumpImpulse`，讓物理起飛時機與動畫預備蹲下姿勢的時間軸對齊。**已實作，並於 v0.10 經手動調整數值、實機測試確認表現正常**（延遲秒數需依實際動畫預備動作長度手動填入，非自動偵測）。
* [x] **（新增，v0.8）** `IsGrounded` 黑板同步收斂進 `MotionDriver.GetGravityThisFrame(data)` 內部，`ExecuteBakedCurveMovement`／`ApplyBakedCompensation` 簽名同步補上 `PlayerRuntimeData data` 參數，移除額外的 `SyncGroundedState` 呼叫點。**已實作**。
* [ ] **（v0.9 提出，v0.10 已定案，尚未實作）** `VerticalVelocity` 從 `MotionDriver` 私有欄位移入 `PlayerRuntimeData` 黑板（`internal set`），讓未來二段跳/擊退/彈跳台等需求可直接讀寫，不必為每個情境各開一個 `ApplyXxxImpulse` 方法。介面設計見 §1.1。
* [ ] **（v0.9 提出，v0.10 已定案，尚未實作）** 新增 `JustLanded`／`JustLeftGround` 單幀邊沿旗標供音效/鏡頭震動等表現層 Controller 訂閱。v0.9 當時因無下游消費者暫緩，v0.10 基於多遊戲模式落地音效/鏡頭震動是標配表現的判斷，改為提前採用。介面設計見 §1.1。
* [ ] **（新增，v0.9）** 角色 GameObject 階層遷移為 Root（Adapter）＋ Model 子物件兩層結構（詳見 §0.3、`01-design-doc.md` §2.6）：既有場景/預製體需要一次性搬遷，並確認 `AnimancerFacade`、`ThirdPersonCamera` 等模組的 Inspector 引用在搬遷後仍正確指向 Root。
* [ ] **（新增，v0.9）** 在 `Model` 子物件的 `Animator` 上，除了 Inspector 手動關閉 `applyRootMotion` 外，於 `AnimancerFacade.Awake()`（或等效初始化流程）加一道程式碼防線，強制覆寫 `applyRootMotion = false`，避免未來換模型時又被誤勾選。
* [ ] **（新增，v0.10，最高優先）** `StateRule` 職責分離重構：新增抽象基底 `StateParamsSO` 與範例子類別 `JumpStateParams`（介面設計見 §3.2 新增小節），`StateMachineConfigSO` 補上 `paramsMappings` 與泛型查表方法 `GetStateParams<T>`；`JumpState.Initialize` 改為呼叫 `config.GetStateParams<JumpStateParams>(Type)`；完成後從 `StateRule` 移除 `JumpImpulseForce`／`JumpTakeoffDelay` 兩個欄位，`StateRule` 恢復只有純拓撲欄位。這是目前最高優先的重構項，因為後續任何新狀態（`SlideState`／`ClimbState`／`AimState`）的調參需求都應該走新機制，不該再繼續往 `StateRule` 加欄位。
* [ ] **（新增，v0.10）** 評估是否需要 `StateParamsSO` 掛載型別驗證的 Editor 工具：在 `StateMachineConfigSO` 存檔時檢查每筆 `StateParamsMapping.Params` 的實際型別是否符合該 `StateType` 預期，避免 `GetStateParams<T>` 轉型失敗被靜默吞掉。
* [ ] **（新增，v0.10）** `CharacterPipelineRunner.ProcessIntents` 已摸到 v0.1 決策訂下的「10-15 行重構訊號」門檻，且專案目標轉向支援多遊戲模式（不同模式的意圖處理邏輯會分岔），評估是否該啟動抽介面（`IIntentProcessor`／`IParameterProcessor`）。

### 後續第四、五階段

* [ ] 導入 `WarpedMotionExtractor` 核心腳本至 Editor 資料夾，建構離線生成管線。
* [ ] 在 `MotionDriver` 中擴充 `ExecuteWarpedMovement`，支援分段特徵點（Apex / End）時間與空間軸動態縮放校準。
* [ ] 以「翻越 1.5m 矮牆」與「精準側向閃避」作為第一個 Warping 功能測試 Demo。
* [ ] 仲裁器（Arbiter）多重封鎖同一旗標時的優先級合併規則。
* [ ] Pipeline 處理器全面抽換為介面驅動（對齊 2.2 節架構草案）。
* [ ] 裝備系統 `ItemDefinition` 欄位規格定義。
* [ ] 補齊 InputData 升版為 ref struct 後的 Profiler 效能測試數據。
* [ ] 導入 `WarpedMotionExtractor` 核心腳本（採隱式分層寫法）。
* [x] (新增) 建立 `AnimationBuildPipeline` 通用框架，將舊有 Extractor 拆分為獨立的 `IBuildStage` 節點。
* [x] (新增) 實作 `BuildCache` 快取機制，透過 `LastWriteTime` 與 Hash 實現秒級的增量編譯（Incremental Build）。
* [x] (新增) 實作 `Animation Build Report` 編輯器視窗，展示烘焙成功率與零 GC 效能數據。

---

## 6. 修訂紀錄

| 日期 | 版本 | 變更內容 | 變更負責人 |
| --- | --- | --- | --- |
| 2026-06-28 | v0.1 | 初版結構建立（骨架定義） | Core Dev |
| 2026-06-29 | v0.2 | 補充 InputData ref struct 重構規劃與黑板相容性限制 | Core Dev |
| 2026-06-29 | v0.3 | InputData 正式升版為 ref struct；新增 Arbiter 仲裁結構與管線脆弱點警告 | Core Dev |
| 2026-07-03 | v0.4 | BaseState 對齊實作更新；加入 StateMachineConfigSO 與動態打斷規則表 | Core Dev |
| 2026-07-05 | v0.5 | **進入第三階段**；補齊 Animation 完整介面；定義常規與扭曲（Warped）雙提取器離線烘焙規格；重構編排全文件順序 | Architecture 組 |
| 2026-07-08 | v0.6 | 除錯過程中發現 `MotionBakeEditor.cs` 實作與 §4.1 規格脫鉤（空 GameObject 取樣、無 Humanoid Avatar），標記為技術債；§3.2 `MotionDriver` 範例補上 `OnAnimatorMove` 執行期外部依賴風險警語；§5 待補清單新增三項對應修復任務 | Core Dev |
| 2026-07-08 | v0.7 | **全面 Code Review 與文件同步**：複查確認 `MotionBakeEditor.cs` 的 Humanoid Avatar 採樣技術債（v0.6 記錄）**已在程式碼中修復**，更正 §4.1 說明並拆出殘留的 Pass 分離／腳相／濾波待辦；§1.1 `PlayerRuntimeData` 新增規劃中的 `IsGrounded` 欄位；§5 待補清單新增 7 項 Code Review 發現（Jump 落地判定資料流、`jumpImpulseForce` 序列化死碼、`MotionDriver` null 防禦、`RollState` 動畫播放防呆、熱路徑 log GC、`AnimancerFacade` 邊界檢查） | Core Dev |
| 2026-07-08 | v0.8 | **補齊修訂紀錄**：實作 Jump「先蹲下再往上」修正（`StateRule.JumpTakeoffDelay`）；`IsGrounded` 黑板同步收斂進 `MotionDriver.GetGravityThisFrame(data)` 內部統一寫入，取代額外呼叫 `SyncGroundedState`；§1.1／§5 同步更新 | Core Dev |
| 2026-07-08 | v0.9 | **補齊修訂紀錄**：新增 §0.3 GameObject 階層規範（Root Adapter + Model Child），呼應 `01-design-doc.md` §2.6；§5 新增對應遷移任務；評估參考碼（BBBNexus）`VerticalVelocity`／`JustLanded`／`JustLeftGround` 兩項設計，當時暫緩（見 v0.10 更新為已定案） | Core Dev |
| 2026-07-08 | v0.10 | **StateRule 職責分離重構規劃**：新增 §3.2 `StateParamsSO` 抽象基底 + `JumpStateParams` 範例的完整介面設計，取代 v0.7/v0.8 把 `JumpImpulseForce`／`JumpTakeoffDelay` 直接塞進 `StateRule` 的做法；§1.1 黑板新增 `VerticalVelocity`／`JustLanded`／`JustLeftGround` 三個 v0.9 暫緩、v0.10 改為已定案的欄位設計；§5 待補清單新增 `StateRule` 重構任務（列為最高優先）與 `StateParamsSO` 型別驗證工具評估；確認 `JumpTakeoffDelay` 已透過手動調整實機驗證表現正常 | Core Dev |