# CharacterController 開發規格文件（API / 資料結構）

> **狀態**：草稿 v0.16.2
> **最後更新**：2026-07-17
> **用途**：實作時的對照表，採「介面先行，實作隨後」原則。

---

## 0. 命名與檔案結構規範

### 0.1 命名規範

* **介面**：`I` 前綴，例如 `IInputSource`
* **抽象基底類別**：`Base` 後綴，例如 `BaseState`
* **ScriptableObject 設定檔**：`SO` 後綴，例如 `StateMachineConfigSO`
* **私有欄位**：`_camelCase`
* **序列化私有欄位（`[SerializeField]`）**：**豁免底線規範，統一採用 `camelCase`（無底線）**。（2026-07-14 定調：全案既有序列化欄位自 v0.1 起一致如此；Unity 依欄位名稱序列化，事後改名會破壞 Inspector 既有綁定、被迫引入 `FormerlySerializedAs` 冗餘，故明文豁免而非遷移）
* **公開屬性 / 欄位**：`PascalCase`

### 0.2 資料夾結構

> ✅ **正式定調（2026-07-14）**：下方磁碟佈局為**最終正式結構**。本節最初（v0.5）規劃把所有專案內容收攏在 `Assets/_Project/` 底下，該規劃已**廢止**——為避免 GUID／`.meta` 搬移風險，不進行 Asset 遷移；`Assets/Scripts/`／`Assets/ScriptableObjects/` 直掛 `Assets/` 即為定案形，僅測試維持於 `Assets/_Project/Tests/`。（同步記載於 `CLAUDE.md` Project Structure 章節。）`Interrupt`／`Equipment`／`Pooling`／`Audio` 等目錄屬未來階段，建立時依本節骨架就地新增，不另起收攏層。

```
Assets/
  Scripts/
    Project.Runtime.asmdef   # Runtime 組件（引用 Animancer、InputSystem）
    Core/
      Blackboard/        # PlayerRuntimeData, IntentData, InputData (黑板資料層)
      Pipeline/          # IInputSource, PlayerInputSource, CharacterPipelineRunner (管線層)
      StateMachine/      # BaseState, FullBodyStateMachine, StateMachineConfigSO,
                         # StateRule, StateType, StateParamsSO, JumpStateParams
        States/          # IdleState, MoveState, JumpState, RollState
      Arbitration/       # ArbiterData (仲裁層；ArbiterPipeline 屬第四階段，尚未建立)
    Presentation/
      Animation/         # AnimationFacadeBase, AnimancerFacade
      Motion/            # MotionDriver, MotionBakeData, JumpLaunchData
      Camera/            # ThirdPersonCamera
    Editor/
      Project.Editor.asmdef  # Editor 組件（引用 Project.Runtime）
      Pipeline/          # CharacterPipelineRunnerEditor（Inspector 除錯擴充）
      Stages/            # MotionBakeEditor（烘焙工具）, MotionFeatureAnalysis（特徵分析階段）
      Tools/             # CharacterCapsuleFitter（膠囊一鍵匹配）, MotionClipImportSOP（匯入設定 SOP 套用）
  ScriptableObjects/
    Motion/              # Bake_*.asset（MotionBakeData 烘焙資產）
    StateMachine/        # PlayerStateMachineConfig.asset, JumpStateParams.asset
    Animation/           # Transition_*.asset（TransitionAsset：狀態鍵 → 過渡資產）、MoveSpeed.asset（StringAsset 參數名）
  Scenes/
  Prefabs/
  _Project/
    Tests/
      EditMode/          # Project.Tests.EditMode.asmdef, EditMode 單元測試
  Plugins/

```

---

### 0.3 GameObject 階層規範（v0.9 新增，v0.12 定調；詳見 docs/01-design-doc.md §2.6 與 `docs/ADR/001-root-model-hierarchy.md`）

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
2. `AnimancerFacade`（或任何 `AnimationFacadeBase` 子類）與 `AnimancerComponent` **同掛在 Root**：Facade 以 `GetComponent<AnimancerComponent>()`（同物件）取得動畫邏輯元件（禁止 `GetComponentInChildren`，會誤抓子物件）；`Animator` 位於 Model 子物件，以 `GetComponentInChildren<Animator>()` 排除 Root 自身後取得（**禁止 `transform.Find("Model")` 等名稱硬編碼**）。`AnimancerComponent.Animator` 序列化欄位須在 Prefab 指向 Model 的 `Animator`。
3. `Model` 底下的 `Animator.applyRootMotion` 必須為 `false`。除了 Inspector 手動關閉外，`AnimancerFacade.ValidateHierarchy()` 會在 `Awake()`（Runtime）與 `OnValidate()`（Editor）強制覆寫一次並記錄警告，避免未來換模型/美術誤勾選又忘記關閉。
4. `AnimancerFacade.ValidateHierarchy()` 另做 Fail-Fast 校驗（Runtime 拋例外／Editor 報錯）：Root 恰好 1 個 `AnimancerComponent` 且 0 個 `Animator`；Model 子物件恰好 1 個 `Animator`；`Animator` 須綁 Humanoid Avatar；`AnimancerComponent.Animator` 須指向該 Model `Animator`。詳見 ADR-001。
5. 任何遊戲邏輯（狀態機、Controller、Driver）一律只能持有 **Root** 的 `Transform` 引用；`ThirdPersonCamera.target` 等外部模組同理只能指向 Root。
6. **Capsule 對齊規範（v0.15 新增，v0.15.1 定稿）**：`CharacterController` 膠囊以 **Root 原點＝腳底** 為錨定——`center = (0, height/2 + skinWidth, 0)`。幾何依據：CharacterController（PhysX CCT）落地時膠囊表面與地面恆保持 `skinWidth`（contactOffset）的掃掠停距，若膠囊底直接對齊 Root 原點，角色會整體懸浮該距離（實測 skin 0/0.03/0.08 → 貼地/浮 3cm/浮 8cm，斜率 1）；上抬 `skinWidth` 後落地時 Root 原點（＝腳底）恰好貼地。**Model 子物件的 local transform 必須為 identity**，禁止用 Model 偏移遷就未校準的膠囊（v0.15 前 Prefab 曾以 `localPosition.y = -0.996` 補償預設膠囊，該做法廢止）。更換/新增 Humanoid 模型後，以 Editor 選單 `Tools/Project/角色 Capsule 自動對齊 (CapsuleFitter)` 一鍵匹配：Height＝rest pose 網格 bounds 高度、Radius＝基準 0.3 × `Animator.humanScale`（gameplay 統一半徑，不隨模型胖瘦跳動）、**SkinWidth＝radius×10%（v1.1 起由工具寫入，與 Center 原子綁定——禁止事後單獨手調 skinWidth，會造成 center 內嵌 skin 項與活欄位脫鉤、腳底偏移＝兩者差值）**、Model 歸零，含 Undo 與量測合理性警告。⚠️ 對場景實例執行後**務必 Apply 到 Prefab**（場景改動不會回寫 Prefab 依賴）。Editor-time 一次性執行，零 Runtime 成本；工具讀取 Model 屬離線配置（比照烘焙工具先例），不違反本節第 5 條的 Runtime 引用禁令。v2 待補（骨骼推估身高、髖寬下限、bounds 條件精化、stepOffset）見 `WORKLOG.md` Backlog。

---

### 0.4 Humanoid 動畫匯入規範（Root Transform 矩陣，v0.15.1 定調）

> 驗證歷程見 `docs/changelog.md` v0.15～v0.15.1。核心機制：本專案 `applyRootMotion` 恆為 false（ADR-001），**未 Bake Into Pose 的 root motion 成分會被引擎「抽出後丟棄」**——輕則水平滑步（hips 被錨定、雙腳反向滑動），重則垂直位移被抹平（跳躍前搖「雙腿向骨盆收攏」、翻滾「在髖部高度空中翻」）。原則：**執行期用不到的成分一律 Bake Into Pose；烘焙器要採樣的成分維持不 Bake。**

| Clip 類型 | 執行期驅動 | XZ Bake | Y Bake | Y Based Upon | Rot Bake | Loop Time | 現有 clip |
|---|---|---|---|---|---|---|---|
| **Locomotion-原地** | procedural（MotionDriver） | ✅ | ✅ | Original | ✅ | ✅ | Idle |
| **Locomotion-位移**（v0.16.1 新增） | procedural（MotionDriver）；烘焙採**速度真相** | **❌**（執行期抽出丟棄＝天然原地化） | ✅ | Original | ✅ | ✅ | Walking、Fast Run |
| **Jump 家族** | 物理 launch（ADR-002）；烘焙採 Y 特徵 | ✅ | ❌ | **Feet** | ✅ | ❌ | Jump |
| **烘焙曲線驅動** | SpeedCurve＋RotationCurve | ❌（採速度） | ✅ | Original | ❌（採 yaw） | ❌ | Stand To Roll |

（XZ／Rotation 的 Based Upon 一律 **Original**——所有 clip 共用 armature 原點參考系，杜絕切換瞬間的水平跳移。）
（**Locomotion-位移的 XZ ❌ 機制**：clip 保留真實 root motion，執行期 `applyRootMotion=false` 將其抽出後丟棄＝視覺原地播放；烘焙器（採樣時開 root motion）仍量得到天生步速，作為 `MotionDriver.moveSpeed` 校準與 Mixer 門檻換算的資料來源。若誤套全 Bake，位移被烤進姿勢：原地播放時角色在動畫內前衝、循環點瞬移回彈。⚠️ 原地型全 Bake 與位移型 Rot ✅ 屬文件目標值，v0.16.1 遷移後首次實測依「先驗證再定調」原則做最終確認。）

**規則**：
0. **AnimationClip 預設不可變（immutable by default），FBX 子 clip 為唯一預設真相來源（2026-07-17 定調，CLAUDE.md 同步收錄）**：所有引用端（`TransitionAsset`、`MotionBakeData.SourceClip`…）一律直接引用 FBX 內的子 clip；匯入設定變更經 SOP 工具落在 FBX 上、立即傳播到執行期，不存在快照過期問題。**禁止以 Ctrl+D 重萃取 `.anim` 作為一般流程**（歷史教訓：v0.15 preset 只落在 FBX、執行期 `.anim` 快照未同步而分岔，2026-07-17 盤點發現五支快照三支過期＋一次 GUID 更替斷引用）。一般調整（數值、Mixer、Transition、播放速度、`MotionDriver` 參數）一律在 Data／Presentation 層解決；僅當需要**修改動畫內容本身**（Animation Event、加曲線、改 keyframe、Import Setting 無法達成的特殊 Variant）才允許建立獨立 AnimationClip，且必須註明建立原因。
1. 新增動畫後，一律以 Project 視窗右鍵 `Project 動畫匯入 SOP` 套用對應 preset（工具經 `ModelImporter.defaultClipAnimations` 覆寫，take 名稱/影格範圍由 Unity 填入，`X Bot@動作名` 慣例使子 clip 自動獲得 @ 後的名稱；禁止手改 `.meta`）。該 clip 若有烘焙資產，套用後**必須重烘焙**。
2. **Jump 家族的 Y Based Upon＝Feet 是關鍵設定**：Y 的 Based Upon 是「全程連續追蹤」（非 XZ/Rotation 的僅起始對齊）——貼地段（前搖/收勢）root Y 持平、下沉保留在姿勢（執行期腳踩得住）；滯空段腳升高才進 root Y（烘焙器量測 `AutoApexHeight` 用、執行期丟棄改由 `ApplyJumpLaunch` 物理接管，無二重上升）。若誤用 Original，前搖下沉會被歸入 root motion Y 而被抹平。`AutoApexHeight` 語意＝**腳底淨空高度**，與 `g=8h/t²`／`v=√(2gh)` 自洽（ADR-002 §2.3）。
3. Mixamo 下載慣例：同一角色（X Bot）下載保 retarget 一致；第一支 with skin、其餘 without skin；FBX for Unity、30fps；**一律不勾 In Place（2026-07-17 反轉舊規）**——root motion 是速度／特徵的資料真相，In Place 會在源頭銷毀它（實證：In Place 版 Walking 烘出 0.1 m/s 雜訊，非 In Place 版量得 1.677 m/s），執行期原地化改由 Locomotion-位移 preset（XZ ❌＋`applyRootMotion=false` 抽出丟棄）達成；命名沿用 `X Bot@<動作名>`。

---



### 1.1 PlayerRuntimeData（全域黑板）

```csharp
public class PlayerRuntimeData
{
    // === 意圖區（每帧處理完即復位）===
    // 註：維持公開欄位而非 Property，避免 struct 值複製導致無法直接修改內部旗標
    public IntentData Intent;

    // === 仲裁區（由 ArbiterPipeline 每帧寫入，各表現層 Controller 唯讀）===
    // 註：同 Intent，維持公開欄位
    public ArbiterData Arbitration;

    // === 參數區（持續存在，每帧更新；實碼採自動屬性）===
    public float MoveSpeed { get; set; }
    public Vector2 MoveDirection { get; set; }
    public float UpperBodyWeight { get; set; }
    public Transform CameraTransform { get; set; }

    // ✅（v0.7 規劃、v0.8 實作完成，v0.11 定調公開欄位）由 MotionDriver.GetGravityThisFrame(data)
    // 每帧統一寫入，供狀態邏輯讀取地面接觸狀態，取代 JumpState 內部原本固定計時器模擬落地判定的做法
    public bool IsGrounded;

    // ⏸（v0.10 定案 → ADR-002 §6-1 延後，尚未實作）取代 MotionDriver 內部私有欄位 _verticalVelocity，
    // 讓非 Jump 狀態（未來的擊退、彈跳台、翻越）也能直接改垂直速度。
    // ADR-002 已定調：等出現「第二個垂直速度消費者」（wall-slide／擊飛／電梯）再落地，
    // 屆時重新界定 Owner/Writer/Readers；目前垂直速度仍封裝於 MotionDriver（選項 A，跳躍經
    // ApplyJumpLaunch 注入）。寫入權限規劃比照 CurrentWeapon 用 internal set。
    public float VerticalVelocity { get; internal set; }

    // ✅（v0.10 定案 → 2026-07-14 定調延後 → M2 落地）單幀邊沿旗標，由 MotionDriver.GetGravityThisFrame(data)
    // 比較本幀與上一幀 IsGrounded 的差異計算得出，僅在觸發那一幀為 true。
    // 供音效／鏡頭震動／落地特效等表現層 Controller 直接訂閱，不必自己追蹤上一幀的 IsGrounded。
    // 延後紀律兌現：第一個下游消費者（M2 AudioController 落地音）出現，欄位隨之落地（YAGNI 閘門通過）。
    // 生命週期：順序 6（MotionDriver 觸發）→ 6.5（PresentationPipeline 消費）→ 7（ResetTransientState 復位）。
    public bool JustLanded;
    public bool JustLeftGround;

    // 🆕（M2）統一復位所有單幀瞬態（意圖旗標 + 落地/離地邊沿），由 Runner 於管線順序 7 呼叫。
    // 復位屬生命週期管理（同 IntentData.Reset() 性質），不視為第二寫入者（觸發源仍唯一）。
    public void ResetTransientState();   // 實碼：Intent.Reset() + JustLanded/JustLeftGround = false

    // === 引用區 ===
    public ItemInstance CurrentWeapon { get; internal set; }
    public Transform AimTarget { get; set; }
}

```

#### 💡 黑板讀寫權限表

| 欄位 | 型別 | 誰寫入 | 誰讀取 | 備註 |
| --- | --- | --- | --- | --- |
| **Intent** | `IntentData` (struct) | InputPipeline | 狀態機 | 每帧結尾由 `ResetTransientState()` 統一復位（順序 7，🆕 M2 起與邊沿旗標一致生命週期） |
| **MoveSpeed** | `float` | Parameter Processor | AnimationFacade | 控制 Locomotion 1D Mixer 混合（✅ v0.16 兌現：`SyncAnimation` 每幀經 `SetFloat(ParamMoveSpeed)` 寫入動畫圖參數字典，由 Transition 資產內 `ParameterName` 綁定驅動，見 §3.2） |
| **CurrentWeapon** | `ItemInstance` | EquipmentDriver | 多處 | 唯讀引用，禁止外部修改內容 |
| **Arbitration** | `ArbiterData` (struct) | ArbiterPipeline | 各表現層 Controller | 每帧統一覆寫，Controller 只讀不寫 |
| **IsGrounded** | `bool` | MotionDriver（於 `GetGravityThisFrame(data)` 內部統一寫入，所有移動路徑最終都會呼叫此方法，來源 `CharacterController.isGrounded`） | 狀態機（如 `JumpState.IsLanded`） | ✅ v0.7 規劃、v0.8 實作完成；已取代 `JumpState` 內部原本的固定計時器落地判定 |
| **VerticalVelocity** | `float`（`internal set`） | MotionDriver、`Project.Core` 內的狀態類別 | 各表現層 Controller（唯讀） | ⏸ v0.10 定案 → **ADR-002 §6-1 延後**：等第二個垂直速度消費者（wall-slide／擊飛／電梯）再落地；目前垂直速度仍封裝於 `MotionDriver` |
| **JustLanded / JustLeftGround** | `bool` | MotionDriver（於 `GetGravityThisFrame(data)` 內比較前後兩幀 `IsGrounded`，**唯一觸發源**；順序 7 `ResetTransientState()` 的統一復位屬生命週期管理，不視為第二寫入者） | PresentationPipeline 驅動的表現層 Controller（✅ M2 首個消費者：`AudioController` 落地音；未來鏡頭震動／特效同窗口讀取） | ✅ v0.10 定案 → 2026-07-14 定調延後 → **M2 落地**（第一個下游消費者出現，YAGNI 閘門通過）；單幀生命週期：順序 6 生 → 6.5 消費 → 7 死 |

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
> * **後續動作**：用 Unity Profiler 量測升版前後的 GC Alloc 差異，截圖存入 `/docs/profiler/` 並更新 `docs/01-design-doc.md`。
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
| **5** | AnimationFacade 同步 | `RuntimeData` / 當前狀態 | 動畫播放指令＋參數同步 | 狀態變更時提交播放請求；每幀將 `MoveSpeed` 寫入動畫圖參數（v0.16，驅動 Locomotion Mixer 混合）。 |
| **6a** | MotionDriver 基礎運動 | `RuntimeData`（輸入方向/速度）＋單幀快取重力積分 | `CharacterController.Move` | **必須在 LateUpdate**，由當前狀態的 `OnUpdateMotion` 選擇移動路徑。v0.9 起全程式碼驅動，**不再讀取 `OnAnimatorMove` 根運動增量**（見 §3.2 風險註記）。 |
| **6b** | MotionDriver 烘焙曲線/補償 | `MotionBakeData`（＋補償目標點） | `CharacterController.Move` | **與 6a 同幀 LateUpdate 執行**。現行 Roll 走 `ExecuteBakedCurveMovement`（純曲線）；`ApplyBakedCompensation`（動態吸附）屬 Warping 階段，尚無呼叫端。 |
| **6.5** | PresentationPipeline Tick | `RuntimeData`（含單幀事件 `JustLanded` 等） | 各表現層 Controller 的表現輸出（M2：落地音；未來 IK／特效） | 🆕（M2）**LateUpdate，MotionDriver 之後**——單幀事件由順序 6 觸發、順序 7 復位，此處是唯一保證可讀到的時間窗。Runner 只呼叫 `PresentationPipeline.Tick`，不認識具體 Controller（見 §3.4）。 |
| **7** | ResetTransientState() | — | `RuntimeData.Intent` ＋ `JustLanded`／`JustLeftGround` 清空 | **LateUpdate 末尾**執行，確保所有讀取方已消耗。🆕（M2）由 `IntentData.Reset()` 擴充為統一復位所有單幀瞬態。 |

> ⚠️ **生命週期脆弱點警告**：
> 1. `ResetTransientState()`（原 `IntentData.Reset()`，M2 擴充）必須死守在管線最後（順序 7），若不小心提前，當幀意圖與單幀事件會在讀取方消費前被清空。
> 2. `ArbiterPipeline Tick`（順序 4.5）必須卡在狀態機**之後**、動畫表現層**之前**，確保動畫能讀到當幀最新的封鎖狀態。
> 3. `FullBodyStateMachine.Initialize()` 必須由 `CharacterPipelineRunner.Start()` 呼叫（**禁止放在 Awake**），確保黑板資料已完全初始化。
> 4. 🆕（M2）`PresentationPipeline.Tick`（順序 6.5）必須卡在 MotionDriver（順序 6，單幀事件觸發源）**之後**、統一復位（順序 7）**之前**——6 → 6.5 → 7 的相對順序是單幀事件「當幀生、當幀死」契約的物理基礎，勿調換。
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

    // 動畫鍵：預設以 enum 名稱對應 AnimancerFacade 的 TransitionMapping.StateKey，子類別可覆寫
    public virtual string AnimationKey => Type.ToString();

    public virtual void Initialize(StateMachineConfigSO config) => Config = config;
    
    public abstract bool CanEnter(PlayerRuntimeData data);
    public abstract void OnEnter(PlayerRuntimeData data);
    public abstract void OnTick(PlayerRuntimeData data, float deltaTime);
    public abstract void OnExit(PlayerRuntimeData data);

    // 【管線順序 6】由當前狀態決定本影格 LateUpdate 的物理位移結算路徑；
    // 預設走 MotionDriver.ExecuteBaseMovement(data)（純 Procedural），
    // Roll 覆寫為烘焙曲線驅動、Jump 覆寫為「前搖後注入 JumpLaunchData ＋ 常規結算」。
    public virtual void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
    {
        motionDriver.ExecuteBaseMovement(data);
    }

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
    // 動畫圖參數鍵：管線順序 5 每幀把黑板 MoveSpeed 送入動畫圖，
    // 訂閱者由 Transition 資產內的 ParameterName（StringAsset，名稱須一致）自行綁定
    public const string ParamMoveSpeed = "MoveSpeed";

    // v0.16（F1）：Play / PlayWithCallback 拔除 transitionDuration——
    // 過渡時長/速度/起始時間由 Transition 資產承載（單一真相），程式碼不再覆寫（2026-07-17 裁決 Q1）
    public abstract void Play(string stateKey);
    public abstract void PlayWithCallback(string stateKey, System.Action onComplete);
    public abstract void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f);
    public abstract void SetFloat(string key, float value);   // v0.16（F2）由空殼轉正：寫入動畫圖參數字典
    public abstract void SetBool(string key, bool value);
    public abstract bool IsPlaying(string stateKey);          // 語意：多鍵可映射同一資產（Idle/Move → Locomotion），結果一致
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

> ⚠️ **v0.10 更新**：實際程式碼在 v0.7/v0.8 曾把 `JumpImpulseForce`／`JumpTakeoffDelay` 直接加進 `StateRule`（上面這份介面草稿沒有反映這兩個欄位，兩者是分開演進的）。經盤點確認這違反單一職責原則，且會在多遊戲模式擴充時讓 `StateRule` 線性膨脹。**目標設計**改為下面的 `StateParamsSO` 方案，`StateRule` 維持只有拓撲欄位（`State`／`Priority`／`CanBeInterruptedBy`／`ValidTransitions`），不再新增任何狀態專屬的物理/表現參數。詳見 `docs/01-design-doc.md` §2.7、§5 Trade-off 表 v0.10 決策列。

#### StateParamsSO（狀態專屬參數資產，v0.10 新增設計，尚未實作）

> ⚠️ **v0.11／v0.13 更新**：本小節為 v0.10 設計草案，保留作歷史脈絡。`StateParamsSO` 機制已於 v0.11 實作；其中 `JumpStateParams` 的內容已由 **ADR-002** 重新定義（拔除下方範例的 `ImpulseForce`／`TakeoffDelay`，改為 `Stages` ＋ Designer Tuning 倍率）。**現行跳躍規格以下方「JumpStateParams／JumpLaunchData／MotionDriver 跳躍注入 API」小節與 `docs/ADR/002-data-driven-jump.md` 為準。**

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

#### JumpStateParams／JumpLaunchData／MotionDriver 跳躍注入 API（ADR-002 落地，取代上方 v0.10 草案）

> [歷史決策脈絡詳見 `docs/ADR/002-data-driven-jump.md`]

```csharp
// 單一跳躍段：每段引用一份 MotionBakeData（提供 AutoTakeoffDelay / AutoApexHeight / AutoCalculatedGravity）
[System.Serializable]
public struct JumpStage
{
    public MotionBakeData Bake;
}

// 跳躍參數資產：內容（Stages）＋ 設計師微調倍率（預設 1）。
// 不含 Coyote / Jump Buffer / Variable Jump（屬 ADR-002 §6 Deferred，未定案）。
[CreateAssetMenu(fileName = "JumpStateParams", menuName = "Project/Core/StateParams/JumpStateParams")]
public class JumpStateParams : StateParamsSO
{
    public List<JumpStage> Stages;          // 第 0 段 = 地面跳；可跳段數上限 = Stages.Count
    public float HeightMultiplier;          // 乘在 AutoApexHeight
    public float GravityMultiplier;         // 乘在 AutoCalculatedGravity
    public float LaunchVelocityMultiplier;  // 乘在逆推出的 v
}

// 跳躍發射資料契約（readonly struct，傳值零 GC）：由 JumpState 逆推、傳給 MotionDriver
public readonly struct JumpLaunchData
{
    public readonly float InitialVerticalVelocity; // 起跳初速（向上為正）
    public readonly float Gravity;                 // 該次採用的重力大小（正值）
    public JumpLaunchData(float initialVerticalVelocity, float gravity);
}
```

**MotionDriver 注入 API**：`ApplyJumpImpulse(float)` 已由 `public void ApplyJumpLaunch(in JumpLaunchData launch)` 取代。`MotionDriver` 新增 `_activeGravity`：注入時覆寫為該段重力、落地（`IsGrounded`）時於 `GetGravityThisFrame` 內部回復預設；`_verticalVelocity` / `_activeGravity` 的唯一寫入者仍為 `MotionDriver`。

**逆推時機**：`JumpState.Initialize()` 逐段以 `v = √(2gh)`（g = `AutoCalculatedGravity` × `GravityMultiplier`、h = `AutoApexHeight` × `HeightMultiplier`，再乘 `LaunchVelocityMultiplier`）預算並快取 `JumpLaunchData`；`OnUpdateMotion` 於當前段 `AutoTakeoffDelay` 過後點火注入。查無 `Stages` 或該段無可信烘焙資料時安全退化為程式碼內建預設值。

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

- **參數空間＝正規化輸入強度（0~1）**，即黑板 `MoveSpeed`；資產內 `ParameterName` 綁 StringAsset `MoveSpeed`（與 `AnimationFacadeBase.ParamMoveSpeed` 常數一致）。各 child 門檻由動畫天生速度正規化推導（下方資料流小節），非憑感覺手填。徹底消滑步（中間值步速精確匹配）屬 M4（foot-phase）範疇。
- **腳步循環同步**：Animancer 原生 `SynchronizeChildren`（加權 NormalizedTime 對齊），Walk↔Run 混合區腳步不跳相。
- **資料流（管線順序 5）**：`SyncAnimation()` 每幀 `SetFloat(ParamMoveSpeed, data.MoveSpeed)`——兌現 §1.1 權限表「MoveSpeed 的 Reader＝AnimationFacade」的既定設計。M1 裁決（Q2）不做平滑（Game Feel 留後續專門輪）。現況 Move 僅綁 WASD（`2DVector` composite 預設 `DigitalNormalized`，對角線模長＝1，經查證免 Clamp01，裁決 Q3），參數為 0/1 二值：混合區間需類比輸入（搖桿綁定）或 Editor 手動滑參數才踩得到。
- **FSM 拓撲零改動**：Idle/Move 狀態、StateType、Config 資產不動；「兩狀態共用一個表現資產」純由映射表達成（兩鍵指向同一資產）。

#### 動畫數據 → 配置資料流（v0.16.2）

**定位**：`MotionBakeData` 不是「人工查看後抄數字」的一次性分析工具，而是系統配置的可靠**資料真相來源**。`AnimationClip` 是表現資源（Presentation Resource），`MotionBakeData` 是該 clip 真實運動數據（位移、速度、重力、腳相）的權威來源。資料流：

```
AnimationClip（FBX 子 clip，表現資源）
  ↓ MotionBake / Feature Analysis（離線烘焙，§4.1／§4.3）
MotionBakeData（真實數據：SpeedCurve→AutoAverageSpeed、AutoApexHeight、AutoCalculatedGravity、EndPhase…）
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

#### MotionDriver（根運動與補償驅動）

> 🛑 **已知風險（2026-07-08 除錯發現）**：下方 `OnAnimatorMove` 路徑正確運作，同時依賴以下外部設定全部對齊，任一項偏離都會表現為「動畫原地不動」或「動作結束瞬移」：
> 1. `Animator`／`CharacterController`／`MotionDriver` 須在**同一個 GameObject**（`OnAnimatorMove` 不會跨物件傳遞）。
> 2. `Animator.applyRootMotion` 必須勾選。
> 3. `Animator.Animate Physics` 必須**不**勾選（勾選會讓回呼落在 FixedUpdate 節奏，與本類別以 `Time.deltaTime` 做每渲染幀積分的假設衝突）。
> 4. 動畫匯入設定的 `Root Transform Position (XZ) → Bake Into Pose` 必須**不**勾選（勾選會讓水平位移被烤進骨架姿勢，讀不到 `deltaPosition`）。
> 5. 任何繞過 `ExecuteBaseMovement` 的位移路徑（如 `ExecuteBakedCurveMovement`）都必須自行歸零 `_rootMotionDelta`，否則會累積殘留量並在切回 `ExecuteBaseMovement` 時一次性噴出。
>
> 中期正評估改為完全不依賴執行期 `OnAnimatorMove`、統一以「輸入速度 + 烘焙曲線速度」驅動的替代架構，降低上述耦合，尚未定案，見 `docs/01-design-doc.md` §5 Trade-off 表。
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

### 3.4 表現層管線與 Audio 子系統（🆕 M2）

> 定位：表現層的**統一驅動骨架**＋第一個具體 Controller（Audio）。Runner 只認 `PresentationPipeline`，
> 新增表現模組（IK／Facial／VFX）只需實作 `IPresentationController` 掛上角色階層，Runner 零改動。
> 架構歸類：非架構變更（依 CLAUDE.md Document Consolidation Policy 走 Living Doc，不開 ADR）；
> 設計理念與升級預留見 `docs/01-design-doc.md` §4.6。

#### 3.4.1 驅動骨架（`Assets/Scripts/Presentation/`）

```csharp
// 表現層 Controller 統一契約：讀黑板 → 驅動表現。實作者對 PlayerRuntimeData 只讀不寫。
public interface IPresentationController
{
    void Tick(PlayerRuntimeData data);
}

// 集中驅動：Runner Start() 以 GetComponentsInChildren<IPresentationController>() 一次性收集，
// LateUpdate 順序 6.5 統一 Tick。陣列建構時一次配置，執行期純 for 迴圈零 GC。
public class PresentationPipeline
{
    public PresentationPipeline(IPresentationController[] controllers);
    public void Tick(PlayerRuntimeData data);
}
```

**規則**：
* Controller **不得自帶 `Update`／`LateUpdate`**——時序由管線統一保證（順序 6.5），否則單幀事件的讀取窗口不可控。
* Controller 對黑板**只讀不寫**（含 `Arbitration` 旗標）。
* Controller 彼此**不得互相引用**；需要協調時走黑板（或未來的表現仲裁）。
* 本骨架只做「依序驅動」，不做輸出仲裁；多 Controller 競爭同一輸出時升級 ArbiterPipeline 式仲裁＋補 ADR。
* Controller 執行順序＝`GetComponentsInChildren` 的階層順序；Start 之後動態加掛的 Controller 不會被收集（現無此需求，出現時再議）。

#### 3.4.2 Audio 子系統（`Assets/Scripts/Presentation/Audio/`）

資料流：黑板單幀事件（`JustLanded`）→ `AudioController.Tick` → `AudioLibrarySO.Get(AudioEventId)` → `AudioDefinitionSO` → `AudioSource.PlayOneShot`

| 類別 | 型別 | 職責 | 關鍵 API／規則 |
| --- | --- | --- | --- |
| `AudioEventId` | enum | 事件語義鍵（Event → Definition 解耦）：玩法端只認「落地了」，播什麼由資產決定 | `Landing = 0`。**顯式數值＝查表索引，只增不改不重排**；腳步音等 Foot IK 週邊（腳相事件源）再擴充 |
| `AudioDefinitionSO` | ScriptableObject | 「**怎麼播**」：clip 池＋音量＋音高範圍（隨機微變化抗重複疲勞） | `GetRandomClip()`（池空回 null）／`GetRandomPitch()`／`Volume`；`CreateAssetMenu: Project/Audio/AudioDefinition` |
| `AudioLibrarySO` | ScriptableObject | 事件 → 定義的唯一查表窗口；Inspector 清單維護，執行期攤平 | `Initialize()`（由 Controller Awake 呼叫，攤平成 enum 值索引陣列：O(1)、零 boxing、冪等）／`Get(id)`（未註冊回 null＝靜默跳過，允許逐步填表）；`CreateAssetMenu: Project/Audio/AudioLibrary` |
| `AudioController` | MonoBehaviour, `IPresentationController` | 「**何時播**」：讀 `JustLanded`＋`BlockAudio`，查表播放 | 由管線順序 6.5 驅動，無自身 Update；`[RequireComponent(AudioSource)]`，掛角色 Root 階層（Runner `GetComponentsInChildren` 收得到即可）；缺引用 Awake 報錯（防禦線風格同 Runner／MotionDriver） |

* **M2 裁決：單一 `AudioSource`＋`PlayOneShot`**。已知侷限：pitch 是 Source 層屬性，連續觸發時後一發會改到仍在播的前一發；多音軌／Source 池屬 §5 Future Work，等第一個真實劣化案例再上。
* `BlockAudio` 讀取**契約先行**：writer（ArbiterPipeline，順序 4.5）到第四階段接入才存在，現值恆 `false`。
* Unity 接線（Phase 5 人工作業）：`AudioController`＋`AudioSource` 掛 Root；`library` 指向 `AudioLibrary` 資產；Library entries 填 `Landing → 落地 AudioDefinition`；Definition 填至少一個落地 `AudioClip`。

### 3.5 Foot IK 子系統（🆕 M3，`Assets/Scripts/Presentation/IK/`）

> 定位：第二個 `IPresentationController` 實例（Presentation Pipeline 骨架的首次回收驗證，Runner 零改動）。
> IK Solver 採 **Unity 內建 Humanoid IK**（M3 裁決 Q1：專案重點是 Character Architecture、不是 IK Solver）。
> 範圍＝腳部貼合＋骨盆補償（Q2，一體不拆）。
> 🔒 **v1 凍結（2026-07-21，收案輪；roadmap `docs/03` §1）**：架構層全部健康、剩餘問題全屬「已知限制」（§3.5.2 L1~L6，各有不改架構的升級路徑），品質升級改由整體 Animation Runtime Roadmap 承載、不再單點深挖。**設計哲學**（使用者裁決，全文 design-doc §4.6）：**Natural Pose > Terrain Adaptation > Perfect Foot Contact**——禁再加 Fade／Gate／權重修正修穿模，貼地改善走 Ground Sampling 升級（Heel/Toe 雙點、CapsuleCast）。**旋轉公式定形**：`FromToRotation(worldUp, hit.normal) × poseRot`（保留俯仰式，動畫腳踝俯仰原樣保留；A/B 軸對齊式「主動壓平」實測無感差已歸檔，changelog v0.18.7）。

#### 3.5.1 資料流（🆕 M3.1：雙管道、各自單寫單讀）

```
Blackboard（IsGrounded／BlockIK）
      │ 讀
      ▼                    Target 管道（Controller 唯一 Writer → Rig 唯一 Reader）
FootIKController ────寫──→ FootIKTargetData ────讀──→ FootIKRig【Presentation Adapter】
【Root，順序 6.5】                                        │ OnAnimatorIK()：
      ▲                                                  │  ①開頭 GetIKPosition/Rotation＋FeetBottomHeight
      │                    Pose 管道（Rig 唯一 Writer → Controller 唯一 Reader）
      └────讀── FootIKPoseData ←──寫─────────────────────┤（動畫原始 goal，IK 套用前＝無污染 pose）
                                                         │  ②套用 SetIK*／bodyPosition ← TargetData
```

* **為什麼是兩條管道（M3.1 教訓）**：Controller 需要「混合後動畫 pose」作 raycast 起點、旋轉基準與權重輸入，
  但骨骼 Transform 在 LateUpdate 時已被上一幀 IK 改寫——直接採樣骨骼＝把 IK 輸出當輸入，形成
  **旋轉追逐（腳踝抽搐）**與**權重鎖死（腳黏地）**兩條反饋迴路。動畫原始 pose 的唯一無污染來源是
  OnAnimatorIK 當下的 `GetIKPosition/GetIKRotation`，而該時間點只有 Rig 在場——故由 Rig 寫 Pose 快照。
  Target 與 Pose 屬不同資料流，**不混入同一結構**；兩條管道各自嚴守單一寫入者。
* **時序**：OnAnimatorIK（Animator 評估流程）早於 LateUpdate → Controller 讀到的是**本幀**新鮮快照；
  算出的目標下一幀套用（一幀延遲不變，見 §3.5.2）。`FootIKPoseData.IsWarm` 由 Rig 首寫時標記，
  Controller 據此不消費未初始化快照。

| 類別 | 位置 | 職責（允許） | 禁止 |
| --- | --- | --- | --- |
| `FootIKController` | Root | 讀黑板、讀 Pose 快照（`FootIKPoseData` 唯一 Reader）、雙腳 raycast（地面點＋法線）、權重計算（Q3 Pose Heuristic：動畫 goal 高度 `min~max` 線性帶）、權重／骨盆平滑（MoveTowards）、骨盆補償計算；`FootIKTargetData` 唯一 Writer | **對 Animator 零依賴**（M3.1：不持骨骼 Transform、不呼叫 `GetBoneTransform`）、呼叫任何 `SetIK*` |
| `FootIKTargetData` | 純 C# 資料 | Target 管道（Controller→Rig）：雙腳目標（位置／旋轉／雙權重）＋`PelvisOffsetY`。值語義（權重 0＝不生效），Reader 零布林判斷 | 不進 `PlayerRuntimeData`；不與 Pose 混入同一結構 |
| `FootIKPoseData` | 純 C# 資料 | Pose 管道（Rig→Controller）：動畫原始 IK goal（雙腳位置／旋轉，IK 套用前）＋Avatar 常數 `FeetBottomHeight`＋`IsWarm` 初始化標記 | 不存 Animator／Transform／MonoBehaviour／Physics 引用 |
| `FootIKRig` | Model（`[RequireComponent(Animator)]`） | **Presentation Adapter**（動畫系統邊界雙向轉接，各方向單寫單讀）：OnAnimatorIK **開頭** `GetIKPosition/GetIKRotation`＋`FeetBottomHeight` 寫入 Pose 快照（唯一 Writer）→ 接著原樣套用 TargetData（唯一 Reader）→ SetIK*／bodyPosition | Raycast、讀黑板、`IsGrounded`／狀態判斷、權重演算法、地面採樣、任何額外判斷 |

* **組裝**：`FootIKController.Awake` 建立兩份資料 → `rig.Bind(targetData, poseData)` 一次性注入；此後兩者僅透過兩條單向共享數據溝通，執行期**無任何方法呼叫／事件／回呼**（M3 裁決明禁 Event Bus／Message System／Callback）。Humanoid Avatar 有效性由 `AnimancerFacade.ValidateHierarchy` 既有 Fail-Fast 防線把關，IK 模組不重複驗。
* **IK pass 開啟**：`FootIKController.Start` 經 `AnimationFacadeBase.SetApplyAnimatorIK(0, true)`（🆕 基底 virtual no-op；`AnimancerFacade` 覆寫為 `Layers[i].ApplyAnimatorIK`）。放 Start＝確保 Facade 的 Awake 已完成，不賭同幀 Awake 順序。
* **Q4（Roll／Jump）**：不特判狀態——空中 `IsGrounded=false` 自然關閉；Roll 中腳部蜷起由 pose 權重自然降低。`BlockIK` 讀取契約先行（writer 到 ArbiterPipeline 接入才存在）。實測若 Roll 吸地明顯，回 Arbiter Pipeline 解決（Future Work）。
* **零 GC**：RuntimeData 一次配置；`Physics.Raycast`（單一命中 out 版）無堆配置；熱路徑無 `new`。

#### 3.5.2 已知限制（時序＋v1 凍結清單）

* Animator IK 與 Animancer（Playables）的時序：`OnAnimatorIK` 發生於 **Animator 評估流程**（PlayerLoop 中早於 LateUpdate）。
* Presentation Pipeline 更新於 **LateUpdate**（順序 6.5，MotionDriver 之後——Pose 快照為本幀新鮮值、膠囊位置為移動後值）。
* 因此 Controller 本幀計算的結果，**下一幀**的 IK pass 才會生效。
* ⚠️ 反饋禁令（M3.1 教訓）：Controller 的任何輸入**不得**來自骨骼 Transform 現值（那是上一幀 IK 的輸出）——pose 一律取自 `FootIKPoseData`（OnAnimatorIK 開頭的 `GetIK*`，IK 套用前）。違反即重現腳踝抽搐／腳黏地反饋迴路。
* 此一幀延遲屬 Unity Humanoid IK 的正常行為，非本專案缺陷。
* 權重平滑（`weightSmoothSpeed`）可降低視覺影響；站立／慢速移動下不可察覺。
* 腳部 IK 目標位置不做平滑（由 raycast 空間連續性保證）；台階邊緣的目標跳變由權重平滑吸收，若不足屬 tuning 範疇。

**v1 凍結已知限制（L1~L6，roadmap §1.3 快照落地本 Living Doc；可接受、升級路徑均不改架構）**：

| # | 症狀 | 根因 | 歸類 | 升級路徑（不改架構） |
| --- | --- | --- | --- | --- |
| L1 | 階梯上腳掌中段穿入上一階 | 單點採樣資訊量天花板：ray 只打腳踝下方，腳掌前段（~25cm）跨入上一階體積系統無從得知 | 已知限制 | Heel/Toe 雙點採樣（輪 7）：僅動 `SampleGround`／`ResolveFoot` 內部＋Settings，雙管道／Ownership 全不動 |
| L2 | toe-off 蹬地相腳尖少量穿模 | 動畫原生腳尖下壓 | 設計接受（哲學 P1 > P5） | 不修 |
| L3 | 左右腳高差 > `MaxPelvisOffset` 時低腳懸空 | 骨盆補償夾限的設計極限 | 已知限制 | 骨盆模型重評（腿長可達性直接建模，輪 7 選項） |
| L4 | IK 結果一幀延遲 | Humanoid IK 快照架構本質（本節上方時序段） | 已知限制 | 無需處理（60fps 不可察） |
| L5 | 腳貼近階梯立面時 ray 誤中上一階頂（「憑空踩半階」） | raycast origin 幾何（`RaycastUpOffset` 高於台階） | tuning 域 | 乾淨 collider 基線上調參（`RaycastUpOffset`／`RaycastDistance`），非程式碼問題 |
| L6 | A/B 旋轉公式（軸對齊 vs 保留俯仰）實測無感差 | 踩地相動畫俯仰本就小（平地夾角 ~2°） | 收案項（已結） | 依哲學回歸保留俯仰式，軸對齊式歸檔（changelog v0.18.7） |

> 灰色地帶（架構乾淨、觀感有天花板，記錄非缺陷）：IK 是純視覺層，`CharacterController` 膠囊高度不隨骨盆補償變動——階梯邊緣站姿的懸浮感上限由「膠囊幾何 × `MaxPelvisOffset`」共同決定，屬兩系統邊界已定義下的固有天花板。

#### 3.5.3 Future Work（M3 裁決明定不得提前實作，需要時一律 TODO）

**Foot IK 品質路線圖（M3.5 定調：單點＋權重補丁已到天花板，升級＝輸入資訊量）**：
* **~~首查項：GetIK* 值域~~ → 已否證（v0.18.7 收案）**：M3 交付時標註的唯一外部 API 風險——「`GetIKRotation/GetIKPosition` 在 Animancer（Playables）下值域是否正確」——經 2026-07-18 `debugLogGoals` 診斷數據排除（goal 位置與骨骼距離 ≈ 0.002m、旋轉健康、與上幀 Set 值非恆等）。**GetIK* 值域正常。** 長期被歸為此首查項的樓梯腳踝歪斜，真凶＝**樓梯 collider 整面是斜坡**（環境資料錯誤，非演算法／API 缺陷；collider 修正後消失，見 changelog v0.18.7）。殘餘的跨階腳掌穿模已改歸 §3.5.2 **L1**（單點採樣資訊量天花板，升級＝輪 7 Heel/Toe 雙點採樣）。
* **M4+ 品質升級**：Heel＋Toe 雙點採樣（邊緣高低面裁定＋腳掌 pitch 貼合）、CapsuleCast（體積採樣取代線採樣）、Foot Contact 狀態機（plant/lift 事件，兼 Footstep 音源）、Foot Phase Curve（烘焙腳相，等 Footstep／Audio 輪一併評估 Mixer 混合取值）。
* **實驗歸檔（M3.2~M3.5 結論，程式碼已移除、復刻看 changelog v0.18.2~v0.18.6）**：fade 族＝半 IK 常態化（棄）；Slope Gate＝邊緣震盪源且垂直 ray 打不中立面（棄）；濾波＝離散面選擇連續化（棄）；Reach Clamp＝方向正確（介入式、直接建模）但距離比模型在骨盆下沉情境誤傷，未來以膝蓋彎曲角度模型重評。
* 其他既有 Future Work：Footstep Event、Audio Integration、`BlockIK` Writer、Mini Arbiter、Animation Rigging Package、Two-Bone IK Solver、Motion Warping、IK Hint。

#### 3.5.4 極端案例收束與參數集中（🆕 M3.2）

> 目標＝作品集展示品質，非 AAA：只收束已實測驗證的極端案例（左右腳高差遠超步高時的腿部過度彎曲／不自然姿勢），不為未驗證問題加架構。全部參數集中於 `FootIKSettings`（Serializable，嵌入 Controller Inspector），杜絕 Magic Number 散落。

> 🆕 **M3.5 最終形（v0.18.6）——字面回歸 M3.1**：flag 版驗收未過（兩快篩證明殘餘的階梯腳踝歪斜與 M3.5 新增項無關、極可能 M3.1 即存在），依裁決**實驗機制連同 flag 全數移除**，程式碼回到 M3.1 演算法本體＋兩項保留（法線抬升＝M3.3 幾何正解、`FeetBottomHeight`）。版本語義：**M3.1＝Baseline、M3.2~M3.4＝Experimental（已移除，復刻看 changelog v0.18.2~v0.18.6）、M3.5＝Regression Recovery 最終形**。下表機制中僅 Pelvis Clamp 仍存在；其餘（Height Fade／雙腳高差 Fade／Reach Clamp／Slope Gate／Edge Filter ②③）為**歷史紀錄**。未來品質提升走輸入資訊量升級（Heel/Toe 雙點採樣、CapsuleCast、Foot Contact，見 §3.5.3 路線圖與 WORKLOG），不再往單點權重堆補丁；**遺留未解**：階梯腳踝歪斜（踏面中央亦現）——首查項＝`GetIKRotation/GetIKPosition` 在 Animancer（Playables）下的值域正確性（M3 交付時標註的唯一外部 API 風險）。

**最終權重公式**（v0.18.4）：`goalWeight = PoseHeuristic × DepthFade(僅向下) × FartherFootFade`，目標值變化一律經 `WeightSmoothSpeed` 的 MoveTowards 平滑（雙保險：fade 曲線平滑＋收斂平滑，保證不瞬切）。

| 機制 | 觸發 | 行為 | 參數 |
| --- | --- | --- | --- |
| **IK Height Fade**（必要，v0.18.4 P1：僅向下） | 單腳地面命中點**低於** Root 平面的深度（`max(0, rootY−groundY)`，僅向下計）進入 `IKFadeStart`~`IKFadeEnd` 帶 | SmoothStep 平滑遞減該腳權重至 0——向下探深不硬拉；**向上踩高階不受此限**（屈髖抬腿是 IK 最好的表現，保留；極限由 Reach Clamp＋遠腳 Fade 把守）。v0.18.2 原絕對值寫法把「搆不到」與「踩得上」混為一談、誤殺踩高階＝方向性錯誤 | `IKFadeStart` 0.35／`IKFadeEnd` 0.6 |
| **雙腳高差 Fade**（必要，v0.18.4 P3：改遠腳） | 左右腳地面高差 > `MaxFootHeightDifference`（超出「骨盆補償＋腿部伸展」合計能力的極端地形） | **距 Root 平面較遠**的腳平滑退出 IK（root 在高處＝放低腳（深不可及）、root 在低處＝放高腳（跨不上去），上下方向的極限處理一致——原「固定放低腳」在踩高階情境放錯腳），骨盆補償同步乘上同一 fade；過渡帶寬沿用 `FadeEnd−FadeStart`，不另設參數 | `MaxFootHeightDifference` 0.6（v0.18.3 由 0.45 上調：過低會誤殺 IK 本可貼好的大階梯——低腳被關→動畫姿勢直接穿模） |
| **Reach Clamp**（必要） | IK 目標與髖錨點距離 > 腿長 × `ReachRatio` | 目標沿原方向**夾回可達球面**（錨＝動畫髖位置＋當前骨盆偏移；腿長＝大腿＋小腿骨段和，由 Rig 量測入 Pose 快照）。只 clamp Target，不動 Animator／Solver | `ReachRatio` 0.98（v0.18.3 由 0.95 上調：站立／蹬伸時髖→踝 ≈ 腿長 96~99%，過緊會把正常踩地目標拉離地面、整體貼合劣化——本值只防數學性超伸） |
| **Slope Gate**（v0.18.3 新增） | raycast 命中面法線與垂直夾角 > `MaxGroundAngle` | 該命中視為**不可站表面**（樓梯立面／陡壁／邊緣的水平向法線）→ 無效化、交還動畫姿勢——腳不對齊不可站的面（樓梯邊緣腳背歪斜的根因）。語義對齊 `CharacterController.slopeLimit` | `MaxGroundAngle` 45° |
| **Pelvis Clamp**（建議） | 骨盆補償目標 | 夾在 `[-MaxPelvisOffset, 0]`＋`PelvisSmoothSpeed` 平滑——不可無限下降 | `MaxPelvisOffset` 0.35 |
| **Edge Filter ②法線低通**（v0.18.4） | 每幀命中法線 | `Slerp` 連續收斂至本幀法線——樓梯邊緣／碰撞體稜角的**單幀法線毛刺**（斜腳底板來源）被濾除，持續性坡面變化仍可跟上。以濾波取代「突變檢測＋分支」：無粘滯、無新分支。落空／禁用時向 up 回歸中性 | `NormalFilterSpeed` 12 |
| **Edge Filter ③目標修正量平滑**（v0.18.4） | 每幀 IK 目標 | 平滑「**相對動畫 goal 的偏移**」而非世界位置——邊緣 ray 在高低階之間跳動造成的目標瞬移被拉勻；因目標隨 pose 前移零滯後，奔跑中不產生腳部拖尾。落空時偏移歸零 | `TargetFilterSpeed` 3 |

> **Edge Filter 全貌**（M3 裁決新增＝單點採樣的最後穩定化，完成後才進 M4 雙點採樣）：①Slope Gate（v0.18.3，上表獨立行）＋②法線低通＋③目標修正量平滑。濾波跨幀態屬演算法內態（同 `MotionDriver._wasGrounded` 性質，非幀局部數據跨幀持有）。

* 目標抬升沿**地面法線**（v0.18.3）：`target = hit.point + hit.normal × FeetBottomHeight`——腳掌已對齊斜面，腳踝間隙同樣垂直於斜面；沿世界 up 抬會使前腳掌在斜坡上幾何性插入坡面（腳背穿模根因之一）。v0.18.4 起法線取濾波後值。
* 純函數：`ComputeHeightFade`／`ClampReach`（供 EditMode 測試，`FootIKTests` 42→49 條）。Edge Filter 的濾波屬跨幀狀態性行為（演算法內態，同 `MotionDriver._wasGrounded` 性質），以 Play 實測驗證。
* `FootIKPoseData` 擴充（M3.2）：髖位置 ×2＋腿長 ×2（Rig 於 OnAnimatorIK 開頭寫入——髖取 IK 套用前值無污染、骨段長度恆定，純賦值無判斷），Controller 維持對 Animator 零依賴。
* ⚠️ Inspector 遷移：既有序列化參數全數移入 `settings`（序列化路徑改變）——重編後需在 Inspector 重新確認（尤其 `GroundLayers`），預設值即上表。

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

### 4.3 特徵分析階段（`MotionFeatureAnalysis.cs`：跳躍物理特徵自動提取）

> 位置：Bake Pipeline 中 Root Motion 曲線提取「之後」、資產存檔「之前」（`MotionBakeEditor.SaveAsset` 內呼叫 `MotionFeatureAnalysisStage.Run`），完全不干涉既有曲線／旋轉收斂／腳相邏輯。
> 契約：`IMotionFeatureAnalyzer` 讀取 `MotionFeatureContext`（整段動畫的逐影格採樣緩衝）並將結果寫入 `MotionBakeData`；實作必須自帶安全退化，不可拋例外中斷管線。新增特徵（Stride Length、Trajectory…）＝實作介面並註冊到 `MotionFeatureAnalysisStage`，無需改動採樣迴圈或既有分析器。

#### 世界空間相對足跡演算法（World-Relative Footprint，v0.14 落地）

**基線（Rest Pose Baseline）**：烘焙器在實例化採樣替身後、第一次 `SampleAnimation` **之前**，快取雙腳骨骼的世界 Y（`MotionFeatureContext.LeftFootBaselineY` / `RightFootBaselineY`）。基線屬於 rig 本身（踝骨自然離地高度），與 clip 內容完全解耦，天然免疫「根節點蹲伏下沉被反相混疊成腳抬起」的舊誤判（v0.14 前的舊演算法以「腳踝相對根節點本地 Y > 絕對門檻」判離地，零點隨匯入設定漂移且被根節點自身位移污染，導致前搖誤判與滯空高估）。

**逐影格分類**：`腳騰空 ≡ 腳世界 Y > 自身基線 + FootLiftThreshold`（容忍度，Inspector 可調，預設 0.03m 以吸收蹬伸期踝骨抬升雜訊）；`騰空 ≡ 雙腳同時騰空`。

**Pass 1 — 事件偵測狀態機**：
1. 起跳候選：`前一幀觸地 → 本幀起連續 ≥2 幀騰空`（單幀騰空視為採樣雜訊）。
2. 持續騰空驗證：候選起的騰空段必須延續 `≥ MinAirTime`（0.1s），否則整段丟棄、續掃下一個候選——過濾跑步循環的雙腳騰空相與小碎跳。
3. 騰空段行進中，「單幀觸地、下一幀又騰空」視為擦地雜訊忽略；連續 ≥2 幀觸地（或片尾觸地幀）即為真實接觸，該接觸 run 的第一幀＝落地影格。
4. 第一段通過驗證的飛行即為本 clip 的跳躍（多段小跳只取第一弧）。

**Pass 2 — 精算閉環**：
1. 子影格線性插值：在跨越門檻線的相鄰兩採樣間解出精確交點——起跳取雙腳交點的 **max**（後離地的腳）、落地取 **min**（先觸地的腳）。
2. 最高點：只在 `[起跳, 落地]` 窗內掃描根節點世界 Y 最大值；基準為插值後起跳時刻的根節點高度（拋體弧線真正起點）。頂點本身不需插值（頂點速度趨近 0，30fps 量化誤差約 1.4mm）。
3. `AutoAirTime = 落地時刻 − 起跳時刻`；`AutoCalculatedGravity = 8·h / t_air²`（與執行期 `v = √(2gh)` 對稱自洽，見 ADR-002 §2.3）。

**安全退化矩陣**：

| 情境 | AutoTakeoffDelay | AutoApexHeight | AutoAirTime | AutoCalculatedGravity |
|---|---|---|---|---|
| 偵測不到起跳（Idle／Walk／Run／非跳躍） | 0 | 0 | 0 | 標準值 9.81 |
| 有起跳、無落地（jump-loop／跳上高台的不對稱拋物線） | 量測值 | 量測值（窗至片尾） | 0（明示未量測） | 標準值 9.81 |
| 落地找到但 t_air ≤ MinAirTime 或 h ≤ MinApexHeight（如 Bake Into Pose Y 採不到上升量） | 量測值 | 量測值 | 量測值 | 標準值 9.81 |
| 完整閉環 | 量測值 | 量測值 | 量測值 | 8h/t² |

**已知限制**：(1) 蹬伸期腳跟先離地會讓踝骨提早上升，深蹲推蹬類動畫的起跳時刻可能提早約 1 影格（容忍度 0.03m 已吸收大部分；未來可選配腳趾骨骼精化）。(2) 跳上高台類不對稱拋物線不符 `g = 8h/t²` 的對稱假設，一律誠實退化（廣義解 `g = 2(√h_up + √h_down)²/t²` 需另偵測落地面高度，暫不落地）。(3) 演算法全程為絕對值逐幀比較，無積分項、無累積誤差；量測精度受採樣率限制的部分已由子影格插值消除主要量化誤差。(4) 即使 `Root Transform Position (Y) → Bake Into Pose` 被誤勾，腳的世界 Y 仍隨姿勢升降，起跳／落地時刻照樣可量測，僅最高點（root 基準）退化、重力安全退回標準值。

#### 代表速度（AutoAverageSpeed，v0.16.2）——曲線聚合類特徵

特徵分兩類，資料依賴不同：**採樣提取類**（跳躍前搖／最高點／滯空）需原始逐影格採樣（foot/root 世界 Y），走 `JumpFeatureAnalyzer`（吃 `MotionFeatureContext.Samples`）；**曲線聚合類**（代表速度）是對「已提取的 `SpeedCurve`」再做的統計量，天然屬 `MotionBakeData` 自身，故由 `MotionBakeEditor.SaveAsset` 在寫入 `SpeedCurve` 後直接呼叫 `MotionBakeData.ComputeAverageSpeed` 填入 `AutoAverageSpeed`，不繞經 analyzer（analyzer 契約吃 `Samples`，而 `Samples` 不含水平位移／速度）。定義：`SpeedCurve` 關鍵影格值算術平均（等距採樣＝時間平均）。消費見 §3.2「動畫數據 → 配置資料流」。未來若新增同屬「曲線聚合類」的特徵（如 Stride Length 由位移曲線積分），沿用此處分工即可。

---

## 5. 待補充規格清單（Project Management）

### 第二階段進度（已完成）

* [x] `StateMachineConfigSO` 加入 `Priority` 欄位，`EvaluateInterrupts` 改為手動迴圈比大小，確保零 GC 確定性仲裁。
* [x] Animancer Lite v8 評估結論補入 `docs/01-design-doc.md` Trade-off 表。

### 第三階段（目前衝刺中）

* [x] **（v0.16.2 完成）** 動畫數據 → 配置資料流：`MotionBakeData` 新增 `AutoAverageSpeed`（代表速度，烘焙時寫入）＋ `GetRepresentativeSpeed()`（欄位優先、舊資產即時回退）；`MotionDriver` 新增 `moveSpeedSource`＋`overrideMoveSpeed`（Bake 提供滿速預設、可 override、來源可追蹤）；Mixer 門檻改由 `threshold=speed_i/speed_max` 推導。規格見 §3.2「動畫數據 → 配置資料流」，均在 `Presentation.Motion` 層內、不動 Data/Presentation 邊界。⚠️ 生效需在 Prefab 上把 `moveSpeedSource` 指向 `Bake_Fast Run`（不指則維持手填值，向後相容）。
* [x] `AnimancerFacade` 實作：建立 `stateKey` $\rightarrow$ `AnimationClip` 的映射配置資產機制。**✅ v0.16 落地並升級**：直接落在 `stateKey` → `TransitionAssetBase`（F1 Transition 資產機制，過渡參數由資產承載），見 §3.2；映射表本體維持序列化於 Facade 元件上——抽成 `AnimationSetSO` 屬 YAGNI 延後（等第二個角色／模式的動畫集共享需求，遷移路徑見 `docs/01-design-doc.md` §5 Trade-off 表 v0.16 列）。
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
* [ ] **（v0.9 提出，v0.10 已定案，⏸ ADR-002 §6-1 延後）** `VerticalVelocity` 從 `MotionDriver` 私有欄位移入 `PlayerRuntimeData` 黑板（`internal set`）。ADR-002 已定調實作時機：**等出現第二個垂直速度消費者**（wall-slide／擊飛／電梯）再做，屆時重新界定 Owner/Writer/Readers；在那之前垂直速度維持 `MotionDriver` 封裝（跳躍經 `ApplyJumpLaunch` 注入，選項 A）。介面設計見 §1.1。
* [x] **（v0.9 提出，v0.10 定案，2026-07-14 定調延後，✅ M2 落地）** 新增 `JustLanded`／`JustLeftGround` 單幀邊沿旗標供音效/鏡頭震動等表現層 Controller 訂閱。延後紀律兌現：第一個下游消費者（M2 `AudioController` 落地音）出現，欄位隨之落地——MotionDriver 唯一觸發源、順序 7 `ResetTransientState()` 統一復位，規格見 §1.1／§2.1／§3.4。
* [ ] **（新增，v0.9）** 角色 GameObject 階層遷移為 Root（Adapter）＋ Model 子物件兩層結構（詳見 §0.3、`docs/01-design-doc.md` §2.6）：既有場景/預製體需要一次性搬遷，並確認 `AnimancerFacade`、`ThirdPersonCamera` 等模組的 Inspector 引用在搬遷後仍正確指向 Root。
* [ ] **（新增，v0.9）** 在 `Model` 子物件的 `Animator` 上，除了 Inspector 手動關閉 `applyRootMotion` 外，於 `AnimancerFacade.Awake()`（或等效初始化流程）加一道程式碼防線，強制覆寫 `applyRootMotion = false`，避免未來換模型時又被誤勾選。
* [ ] **（新增，v0.10，最高優先）** `StateRule` 職責分離重構：新增抽象基底 `StateParamsSO` 與範例子類別 `JumpStateParams`（介面設計見 §3.2 新增小節），`StateMachineConfigSO` 補上 `paramsMappings` 與泛型查表方法 `GetStateParams<T>`；`JumpState.Initialize` 改為呼叫 `config.GetStateParams<JumpStateParams>(Type)`；完成後從 `StateRule` 移除 `JumpImpulseForce`／`JumpTakeoffDelay` 兩個欄位，`StateRule` 恢復只有純拓撲欄位。這是目前最高優先的重構項，因為後續任何新狀態（`SlideState`／`ClimbState`／`AimState`）的調參需求都應該走新機制，不該再繼續往 `StateRule` 加欄位。
* [x] **（v0.10 提出，2026-07-18 以執行期防線輕量解決，Editor 工具不建）** 評估 `StateParamsSO` 掛載型別驗證的 Editor 工具：`GetStateParams<T>` 靜默回 null 的防呆改由 `JumpState.Initialize` 的 Editor 警告承接（比照 RollState 斷鏈防線＋M2 Warning 治理 `Application.isPlaying` 條件）——「未綁定／引用失效／型別不符」三種情境進 Play 第一時間現形。專用存檔時驗證工具依「Editor Tool vs Documented Process」雙 Gate 評估**不建**（Gate A 弱：State 種類少、配置頻率低）；未來狀態種類明顯增多再重評。
* [ ] **（新增，v0.10）** `CharacterPipelineRunner.ProcessIntents` 已摸到 v0.1 決策訂下的「10-15 行重構訊號」門檻，且專案目標轉向支援多遊戲模式（不同模式的意圖處理邏輯會分岔），評估是否該啟動抽介面（`IIntentProcessor`／`IParameterProcessor`）。
* [x] **（新增，v0.14，當輪完成）** Jump Feature Analysis 演算法修復：起跳偵測改為「世界空間相對足跡」（Rest Pose 基線＋持續騰空驗證），根治「腳踝相對根節點＋絕對門檻」造成的前搖誤判；滯空時間由「`Duration − 起跳`」簡化估計改為「起跳 → 首次落地」雙 Pass 精確量測（含子影格線性插值與頂點窗格裁剪），根治逆推重力被系統性低估的發飄問題。規格見 §4.3；⚠️ 需手動重烘焙 `Bake_Jump.asset` 才生效。

### 後續第四、五階段

* [ ] 導入 `WarpedMotionExtractor` 核心腳本至 Editor 資料夾，建構離線生成管線。
* [ ] 在 `MotionDriver` 中擴充 `ExecuteWarpedMovement`，支援分段特徵點（Apex / End）時間與空間軸動態縮放校準。
* [ ] 以「翻越 1.5m 矮牆」與「精準側向閃避」作為第一個 Warping 功能測試 Demo。
* [ ] 仲裁器（Arbiter）多重封鎖同一旗標時的優先級合併規則。
* [ ] **（M2 新增）** Audio 多音軌／`AudioSource` 池：解除單一 Source 的 pitch 相互干擾與重疊播放限制（見 §3.4.2 已知侷限）；等第一個真實劣化案例出現再設計。
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
| 2026-07-08 | v0.9 | **補齊修訂紀錄**：新增 §0.3 GameObject 階層規範（Root Adapter + Model Child），呼應 `docs/01-design-doc.md` §2.6；§5 新增對應遷移任務；評估參考碼（BBBNexus）`VerticalVelocity`／`JustLanded`／`JustLeftGround` 兩項設計，當時暫緩（見 v0.10 更新為已定案） | Core Dev |
| 2026-07-08 | v0.10 | **StateRule 職責分離重構規劃**：新增 §3.2 `StateParamsSO` 抽象基底 + `JumpStateParams` 範例的完整介面設計，取代 v0.7/v0.8 把 `JumpImpulseForce`／`JumpTakeoffDelay` 直接塞進 `StateRule` 的做法；§1.1 黑板新增 `VerticalVelocity`／`JustLanded`／`JustLeftGround` 三個 v0.9 暫緩、v0.10 改為已定案的欄位設計；§5 待補清單新增 `StateRule` 重構任務（列為最高優先）與 `StateParamsSO` 型別驗證工具評估；確認 `JumpTakeoffDelay` 已透過手動調整實機驗證表現正常 | Core Dev |
| 2026-07-11 | v0.11 | **分支整併 + StateParamsSO 落地**：§3.2 規劃的 `StateParamsSO`／`JumpStateParams` 由「設計」轉為「已實作」（泛型 `GetStateParams<TParams>()` 取代過渡期 float-getter）；`StateRule` 移除 Jump 物理欄位、抽為獨立檔（純拓撲）；黑板 `IsGrounded` 採公開欄位；`JumpState`／`RollState` 加著地閘門；新增 asmdef（Project.Runtime／Editor／Tests.EditMode）與 `StateMachineTests` EditMode 測試 | Core Dev |
| 2026-07-13 | v0.14 | **Jump Feature Analysis 演算法修復（世界空間相對足跡）**：新增 §4.3 特徵分析階段規格（Rest Pose 基線、雙 Pass 事件偵測與精算閉環、安全退化矩陣、已知限制）；`MotionFeatureSample` 雙腳欄位語意改為世界 Y、`MotionFeatureContext` 新增雙腳基線；滯空時間「簡化估計」技術債清除（§5 對應項標記完成）。Runtime 零改動，僅 `MotionBakeData` 註解同步 | Core Dev |
| 2026-07-13 | v0.14.1 | **文件—程式碼一致性修正（純文件，零程式行為變更）**：§0.2 資料夾結構改為記載實際磁碟佈局並標註「原 `_Project/` 收攏規劃」為待決事項；§1.1 黑板 code block 對齊實碼簽名（參數區自動屬性、`CurrentWeapon` internal set），`VerticalVelocity` 補 **ADR-002 §6-1 延後**註記（讀寫表與 §5 待補清單同步）；§2.1 順序 6a 輸入欄修正過時的「Animancer 根運動增量」描述（v0.9 起全程式碼驅動）；§3.1 `BaseState` 補上實碼既有的 `AnimationKey` 與 `OnUpdateMotion`（管線順序 6 入口）；文件頭部版本狀態追平修訂紀錄 | Core Dev |
| 2026-07-14 | v0.14.2 | **決策收錄（純文件，零程式碼變更）**：①§0.2 資料夾結構由「待決」改為**正式定調現狀**（`Assets/Scripts/` 直掛為最終形，`_Project/` 收攏規劃廢止，CLAUDE.md 同步新增 Project Structure 章節）；②§0.1 新增**序列化私有欄位豁免條款**（`[SerializeField]` 統一 `camelCase`，CLAUDE.md 同步）；③`JustLanded`／`JustLeftGround` 定調**延後實作**（比照 `VerticalVelocity` 的 YAGNI 紀律，等第一個下游消費者；§1.1 code block／讀寫表／§5 待補清單同步標註） | Core Dev |
| 2026-07-14 | v0.15 | **兩支 Editor 工具＋Capsule 對齊規範（零 Runtime 變更）**：①§0.3 新增規則 6「Capsule 對齊規範」（Root 原點＝腳底＝膠囊底、Model 必為 identity、廢止 -0.996 偏移補償），配套 `CharacterCapsuleFitter` v1（Height=bounds／Radius=0.3×humanScale／Center=h/2／Model 歸零／Undo／合理性警告；骨骼推估等 v2 項留 Backlog）；②新增 `MotionClipImportSOP` 匯入設定套用工具（Locomotion／Jump／BakedCurve 三 preset，經 `defaultClipAnimations` 覆寫保引用不斷鏈），供 Jump 腳滑 Step 1 驗證使用——**Root Transform 匯入矩陣本身待實測通過後才寫入本文件定調**；③§0.2 目錄補 `Editor/Tools/` | Core Dev |
| 2026-07-14 | v0.15.1 | **Step 1 全數驗證通過，匯入矩陣正式定調＋CapsuleFitter v1.1**：①新增 **§0.4 Humanoid 動畫匯入規範**（Root Transform 矩陣＋Jump 家族 Y Based Upon=Feet 關鍵設定＋Mixamo 下載慣例），Step 1 三次迭代實測全過（跳躍前搖蹲下正常/腳底穩定、翻滾貼地、跑步無滑步、腳底貼地）後依「先驗證後定調」閘門入文；②§0.3 規則 6 定稿：`CapsuleFitter` v1.1 起 **skinWidth 由工具寫入（radius×10%）並與 Center 原子綁定**，根絕 center 內嵌 skin 項與活欄位脫鉤（v1 實測教訓：懸浮量＝兩者差值），補「場景實例執行後務必 Apply Prefab」警語（本輪實際根因）；③膠囊落地間隙定律 G=skinWidth 經使用者三點數據（0/0.03/0.08）實證，記入規則 6 | Core Dev |
| 2026-07-17 | v0.16 | **M1 Locomotion 落地（F1＋F2）**：①§3.1／§3.2 動畫門面升級 **Transition 資產機制**——`Play`／`PlayWithCallback` 拔除 `transitionDuration`（裁決 Q1：資產＝單一真相）、映射改 `TransitionMapping`（string → `TransitionAssetBase`）、Awake 建表＋預熱、`SetFloat`／`SetBool` 由空殼轉正為 Animancer v8 參數字典通道（Facade 不持有 Mixer 引用）；②新增「Locomotion 1D Mixer 規格」小節（Idle／Walking／Fast Run，threshold 0/0.5/1，Idle 不參與同步；參數空間＝0~1 輸入強度；裁決 Q2 不平滑；裁決 Q3 查證 Move 的 2DVector composite 預設 DigitalNormalized、對角線模長＝1，免 Clamp01）；③§0.2 補 `ScriptableObjects/Animation/`、§2.1 順序 5 補參數同步、§1.1 MoveSpeed Reader 兌現標註、§5 映射資產機制待辦勾銷。FSM 拓撲／State／MotionDriver／黑板 schema 零改動 | Core Dev |
| 2026-07-17 | v0.16.1 | **動畫資產治理定調：FBX 子 clip 直引（單一真相）**：①§0.4 新增規則 0——AnimationClip 預設不可變、FBX 子 clip 為唯一預設來源，Ctrl+D 重萃取廢止（僅內容修改可建獨立 clip 且須註明原因），CLAUDE.md 同步收錄；②矩陣 Locomotion 拆「原地／位移」兩列（位移型 XZ ❌＝執行期抽出原地化＋烘焙採速度真相）；③規則 3 反轉：Mixamo 一律**不勾 In Place**（In Place 版 Walking 0.1 m/s 雜訊 vs 非 In Place 1.677 m/s 實證）；④`MotionClipImportSOP` v2：Locomotion preset 拆為原地／位移兩個。遷移 SOP 與 `.anim` 盤點（五支全屬設定快照、零內容修改、全數退場）見 changelog v0.16.1 | Core Dev |
| 2026-07-17 | v0.16.2 | **動畫數據 → 配置資料流（MotionBakeData 定位升級）**：①§3.2 新增「動畫數據 → 配置資料流」小節（資料流圖＋代表速度／MotionDriver 速度來源／Mixer 門檻推導三條連接＋Data/Presentation 分離論證＋手動覆寫保留）；②§4.3 補「代表速度＝曲線聚合類特徵」與採樣提取類的分工；③Locomotion Mixer 門檻由手填改為 `speed_i/speed_max` 推導（Walk 0.5→0.3）；④§0.4／CLAUDE.md 新增「數據↔表現連動規則」四層 escalation（先 Data、再 Presentation、換 clip、最後才改 clip 內容）與「Clip＝表現資源、Bake Data＝數據真相」定位；⑤`RollState` 加烘焙資料斷鏈警告（設計問題提示，非限制）。程式碼：`MotionBakeData`（AutoAverageSpeed／GetRepresentativeSpeed／ComputeAverageSpeed）、`MotionBakeEditor`、`MotionDriver`、`RollState`；新增 `MotionBakeDataTests`（7 條）。全數 `Presentation.Motion` 層內，黑板 schema／依賴方向不變 | Core Dev |
| 2026-07-18 | v0.17 | **M2 Presentation Pipeline + Landing Audio**：①§1.1 `JustLanded`／`JustLeftGround` 由「定調延後」轉「✅ 落地」（第一個下游消費者出現，YAGNI 閘門通過），新增 `ResetTransientState()` 統一復位（意圖＋邊沿旗標一致生命週期）；②§2.1 順序表新增 **6.5 PresentationPipeline Tick**、順序 7 由 `IntentData.Reset()` 擴充為 `ResetTransientState()`，脆弱點警告補第 4 條（6 → 6.5 → 7 相對順序＝單幀事件契約的物理基礎）；③新增 §3.4 表現層管線與 Audio 子系統規格（`IPresentationController`／`PresentationPipeline`／`AudioEventId`／`AudioDefinitionSO`／`AudioLibrarySO`／`AudioController`）；④§5 邊沿旗標待辦勾銷、Future Work 補 Audio 多音軌／Source 池。程式碼新增 6 檔、修改 3 檔，依賴方向不變（Core 經介面驅動 Presentation）；（07-18 補）RollState 斷鏈警告補 `Application.isPlaying` 條件——EditMode 測試以最小拓撲 config 組裝屬合法輸入，防線語義精確化、Play 偵測力零損失；新增 M2 測試 12 條（`PresentationPipelineTests` 3＋`AudioSystemTests` 9，總數 22→34） | Core Dev |
| 2026-07-18 | v0.18 | **M3 Foot IK（Presentation Pipeline 第二個 Controller）**：新增 §3.5——`FootIKController`（Root，順序 6.5 決策端）→ `FootIKRuntimeData`（共享數據管道，單寫單讀）→ `FootIKRig`（Model，Thin Executor，`OnAnimatorIK` 套用）單向資料流；IK Solver＝Unity Humanoid IK（Q1）、腳部貼合＋骨盆補償一體（Q2）、權重＝Runtime Pose Heuristic（Q3，禁 Bake 擴充）、Roll/Jump 不特判（Q4，禁提前 Arbiter）；`AnimationFacadeBase` 新增 `SetApplyAnimatorIK` virtual（`AnimancerFacade` 覆寫）；已知時序限制（一幀延遲）入 §3.5.2；新增 `FootIKTests` 8 條（總數 34→42）；Runner／MotionDriver／黑板 schema 零改動 | Core Dev |
| 2026-07-18 | v0.18.1 | **M3.1 Foot IK 反饋迴路修正＋雙管道定調**：實測腳踝旋轉抽搐 → Review 定位根因＝Controller 採樣骨骼 Transform（上一幀 IK 輸出）形成旋轉追逐＋權重鎖死兩條反饋迴路。裁決落地：①`FootIKRuntimeData` 更名 **`FootIKTargetData`**、新增 **`FootIKPoseData`**（Rig 於 OnAnimatorIK 開頭寫入動畫原始 `GetIK*` goal＋`FeetBottomHeight`）——Target 與 Pose 兩條**各自單寫單讀**的獨立單向管道；②`FootIKRig` 重定位 **Presentation Adapter**（動畫系統邊界雙向轉接，仍零判斷零演算法）；③Controller **對 Animator 零依賴**（移除 `GetBoneTransform`／骨骼引用）；④手填 `footHeight` 欄位刪除，改用 avatar 內建 `left/rightFeetBottomHeight`（數據真相）；⑤§3.5.2 新增「反饋禁令」；ADR-001 §5 機械性補記（Presentation Adapter 兌現，非決策變更） | Core Dev |
| 2026-07-18 | v0.18.2 | **M3.2 Foot IK 極端案例收束**：新增 §3.5.4——IK Height Fade（單腳深度 SmoothStep 退出）＋雙腳高差 Fade（超過 `MaxFootHeightDifference` 低腳放棄、骨盆同退）＋Reach Clamp（目標夾回腿長×`ReachRatio` 可達球面，錨＝動畫髖＋骨盆偏移；只 clamp Target 不動 Solver）＋Pelvis Clamp（既有上限參數化）；全參數集中 `FootIKSettings`（Serializable）；`FootIKPoseData` 擴充髖位置＋腿長（Rig 量測寫入，Controller 維持對 Animator 零依賴）；純函數 `ComputeHeightFade`／`ClampReach`＋測試 42→49；§3.5.3 Future Work 補 IK Hint | Core Dev |
| 2026-07-18 | v0.18.3 | **M3.3 實測校正（使用者側）**：坡度閘門 `MaxGroundAngle`（Slope Gate，立面視為未命中）、目標沿地面法線抬升（腳背穿模修正）、`ReachRatio` 0.95→0.98 與 `MaxFootHeightDifference` 0.45→0.6 校正（0.95 拉離地面／0.45 誤殺大階梯＝v0.18.2 貼合劣化根因），詳 changelog v0.18.3 | Core Dev |
| 2026-07-18 | v0.18.4 | **M3.4 方向性修正＋Edge Filter 完成（裁決 P1/P3 核可、P2 暫緩）**：①P1——單腳 Height Fade 改**僅向下探深**（v0.18.2 絕對值誤殺踩高階、抬腿能力喪失＝方向性錯誤，抬腿表現回歸）；②P3——雙腳差 Fade 改套**距 Root 平面較遠腳**（上下極限處理一致）；③Edge Filter 補完——②法線低通（Slerp，稜角毛刺濾除）＋③目標修正量平滑（偏移空間，移動零拖尾），與 v0.18.3 的 ①Slope Gate 合為單點採樣最後穩定化；④Heel＋Toe 雙點採樣列 Future Work（P2 暫緩至 M4）；Settings 新增 `NormalFilterSpeed`／`TargetFilterSpeed`，純函數簽名未變、測試維持 49 條 | Core Dev |
| 2026-07-18 | v0.18.5 | **M3.5 Regression Recovery**：實測 regression → 分析定調（M3.1 二態權重系統被疊乘 fade 推進「半 IK」常態；Slope Gate 硬開關＝邊緣三路震盪源；法線低通把離散面選擇混成持續微斜）→ 預設行為**回退 M3.1 基線**＋保留法線抬升／`FeetBottomHeight`／**Reach Clamp 0.98 恆開**（對人體極限的直接建模：可達性＝腿長幾何，取代 fade 族的高差代理指標）；Height Fade／雙腳差 Fade／Slope Gate／Edge Filter ②③ 降為 Experimental A/B flag（`Enable*` 預設 false，不刪除）。版本語義：M3.1＝Baseline、M3.2~M3.4＝Experimental、M3.5＝Regression Recovery；未來品質路線＝Heel/Toe 雙點採樣／CapsuleCast／Foot Contact（M4+） | Core Dev |
| 2026-07-18 | v0.18.6 | **M3.5 最終形：字面回歸 M3.1**：flag 版驗收未過（兩快篩：ReachRatio→1.0 仍歪＋踏面中央亦歪 → 排除全部 M3.5 新增項）→ 依裁決實驗機制**連同 flag 全數移除**（Controller／Settings／PoseData／Rig 四檔回 M3.1 本體＋法線抬升＋`FeetBottomHeight`；Settings 精簡為 8 參數；測試 49→42）；§3.5.3 改寫為「Foot IK 品質路線圖」（首查項＝`GetIK*` 在 Playables 的值域、M4+ 雙點採樣／CapsuleCast／Foot Contact、實驗歸檔）；階梯腳踝歪斜列**遺留未解**（極可能 M3.1 即存在） | Core Dev |
| 2026-07-21 | v0.18.7 | **Foot IK v1 凍結（收案輪，對應 changelog v0.18.7／roadmap `docs/03`）**：§3.5 intro 補 v1 凍結宣告＋設計哲學（Natural Pose > Terrain Adaptation > Perfect Foot Contact）＋旋轉公式定形（保留俯仰式）；§3.5.2 補已知限制表 L1~L6（roadmap §1.3 落地 Living Doc）；§3.5.3 首查項（GetIK* 值域）標記**已否證**（樓梯歪斜真凶＝斜坡 collider，殘餘跨階穿模改歸 L1 單點採樣天花板）。程式碼：`ResolveFoot` 旋轉還原保留俯仰式、`FootIKRig` 刪 `debugLogGoals`；測試 42 條不變。（順修：移除本表重複／錯置的 v0.18.3 列，內容已存 changelog 與上方 v0.18.3 列） | Core Dev |