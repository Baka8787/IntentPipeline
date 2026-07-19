# ADR-001：GameObject 階層 Root/Model 分離規範

| 欄位 | 內容 |
|---|---|
| 狀態 | **Accepted** |
| 日期 | 2026-07-12 |
| 關聯文件 | `docs/01-design-doc.md` §2.6 / §4.4 / §4.7、`docs/02-dev-spec.md` §0.3 |
| 影響模組 | `AnimancerFacade`、角色 Prefab（`Assets/Prefabs/X Bot.prefab`）、設計文件 |
| 釐清事項 | 定調 `AnimancerComponent` 的掛載位置，消除 §2.6 與 §0.3 的既有矛盾（見「決策」） |

---

## 1. 背景 (Context)

除錯 v0.8「Jump 先蹲下再往上」問題時，根因追到 `Animator.applyRootMotion`：只要它沒關乾淨，Unity 會自動把動畫的根動作（Root Motion）套用到掛著 `Animator` 的那顆 `Transform`。若這顆 `Transform` 與 `CharacterController` 是**同一顆物件**，Unity 自動套用的位移就會跟我們手動呼叫的 `CharacterController.Move()` 互搶同一份世界座標，表現為抖動、瞬移或「動畫原地不動」。

這是「表現層解耦」的精神在**物件階層**這個維度上還沒落實——邏輯上做了 `AnimationFacade` 隔離，但實體階層上「邏輯根」與「美術根」還疊在同一顆物件。

### 既有文件矛盾

本 ADR 定調前，兩份文件對 `AnimancerComponent` 的掛載位置**互相矛盾**：

- `docs/01-design-doc.md` §2.6 的階層圖把 `AnimancerComponent` 放在 **Model 子物件**，並以此為由主張 `AnimancerFacade` 繼續用 `GetComponentInChildren<AnimancerComponent>()`。
- `docs/02-dev-spec.md` §0.3 的階層圖把 `AnimancerComponent` 放在 **Root**。

程式碼（`AnimancerFacade.Awake()`）當時沿用 `GetComponentInChildren`，等於預設「元件可能在子物件」，兩種擺法都能跑，矛盾因此長期潛伏。本 ADR 一次定調。

---

## 2. 決策 (Decision)

角色物件一律拆成 **Root（Adapter，邏輯／物理權威層）** 與 **Model（子物件，美術／骨骼層）** 兩層。

```
CharacterRoot                          ← 邏輯/物理權威層，外部一律只引用這一層
 ├─ CharacterController                 ← 物理碰撞體與世界座標的唯一權威
 ├─ CharacterPipelineRunner
 ├─ MotionDriver
 ├─ AnimancerFacade (: AnimationFacadeBase)
 ├─ AnimancerComponent                  ← 動畫「邏輯」元件，定調在 Root
 ├─ PlayerInputSource (: IInputSource)
 └─ Model                               ← 美術/骨骼層，禁止被遊戲邏輯直接引用
      ├─ Animator                       ← Humanoid Avatar；applyRootMotion 必須為 false
      ├─ SkinnedMeshRenderer
      └─ <骨骼階層>                      ← 供未來 FaceRig / WeaponRig / IKRig 掛點使用
```

### 2.1 釐清矛盾：`AnimancerComponent` 定調在 Root

以 `docs/02-dev-spec.md` §0.3 為準，`AnimancerComponent` 掛在 **Root**，`Animator` 掛在 **Model**。理由：

1. **職責歸屬**：`AnimancerComponent` 是「動畫邏輯」元件（播放狀態、管理 Graph），`AnimancerFacade` 直接依賴它；兩者同在 Root，`Facade` 可用 `GetComponent<>()`（同物件）取得，比 `GetComponentInChildren<>()`（跨層搜尋）更嚴格、意圖更清楚。真正屬於「美術」的只有 `Animator`＋網格＋骨骼，這些才下放到 Model。
2. **Animancer 原生支援跨物件**：`AnimancerComponent` 內部以序列化欄位 `_Animator` 引用 `Animator`（見 `AnimancerComponent.cs` 的 `Animator` 屬性與 `Reset()` 的 `GetComponentInParentOrChildren`）。官方文件亦說明 `AnimancerComponent` 可位於 `Animator` 的**父物件或子物件**，因此「`AnimancerComponent` 在 Root、`Animator` 在 Model 子物件」是原生支援且受官方推薦的配置。
3. **物理隔離的根本保證仍在 Animator 這一層**：把 `Animator` 放 Model，即使 `applyRootMotion` 哪天被誤勾，Unity 自動套用的根動作也只會改動 **Model 的 local transform**，不會波及 Root 的世界座標，兩者物理上不可能打架。這一點與 `AnimancerComponent` 放哪無關，故移動 `AnimancerComponent` 到 Root 不影響此保證。

### 2.2 元件獲取規範（禁止硬編碼名稱）

| 元件 | 獲取方式 | 禁止 |
|---|---|---|
| `AnimancerComponent` | `GetComponent<AnimancerComponent>()`（Root／自身） | `GetComponentInChildren`（會誤抓子物件上的元件，違反 Root 職責） |
| `Animator` | `GetComponentInChildren<Animator>(true)` 後排除 Root 自身 | `transform.Find("Model")` 等**任何依賴名稱字串**的硬編碼查找 |

`AnimancerComponent` 的 `Animator` 序列化欄位必須在 Prefab 中明確指向 **Model 子物件的 `Animator`**（Animancer 的 `TryGetComponent` 自動補值只會找同物件，跨物件必須手動指定）。

### 2.3 結構校驗與 Fail-Fast 防線

`AnimancerFacade` 引入獨立的 `ValidateHierarchy()`，於 `Awake()`（Runtime）與 `OnValidate()`（Editor 非執行狀態）皆會執行，規則如下：

1. **Root 職責**：Root 必須恰好 **1 個** `AnimancerComponent`；Root **不得**掛任何 `Animator` → 違反則報錯。
2. **Model 職責**：子物件（不含 Root 自身）中的 `Animator` 數量必須**恰好 1 個** → 違反則報錯。
3. **Humanoid 綁定**：`Animator` 存在但未指定 Avatar，或 Avatar 非 Humanoid → 報錯。
4. **連線正確性**：`AnimancerComponent.Animator` 欄位必須指向該 Model `Animator` → 否則報錯（避免 Facade 對著沒接好的 Graph 空播）。
5. **強制關閉 Root Motion**：若找到 Model `Animator` 且 `applyRootMotion == true`，程式碼**強制覆寫為 false** 並記錄警告，徹底隔離物理與美術動畫位移，防止未來換模型時美術誤勾選。

行為差異：
- **Runtime（`Awake`）**：違規直接 `throw`（Fail-Fast），讓錯誤在最早期就爆出來，而不是留到第一次 `Play()` 才 NRE。
- **Editor（`OnValidate`）**：只 `Debug.LogError` 給出清楚訊息，不中斷編輯流程；強制關閉 Root Motion 的動作延後到 `OnValidate` 之外執行，避免 Unity「不可於 OnValidate 期間修改其他物件」警告。

---

## 3. 後果 (Consequences)

### 正面
- **物理權威單一化**：世界座標只由 `CharacterController`（Root）與 `MotionDriver` 的程式路徑改動，Root Motion 與物理位移不可能互搶。
- **美術資產可替換**：換裝、換模型只需替換 Model 子物件，Root 上的邏輯元件、Collider 尺寸、Inspector 綁定完全不受影響。
- **Fail-Fast**：階層或綁定錯了，開場即報錯，不再是難以定位的位移抖動。
- **擴充自然**：`FaceRig` / `WeaponRig` / `IKRig` / `Animator Override` / `LOD Animator` / `Multiple Models` 天然掛在 Model 子樹，不與 Root 邏輯層混雜；校驗以「子物件恰好 1 個 Animator」為界，未來要支援多 Model 時只需放寬此條規則，不必動 Facade 介面。

### 代價
- 現有單層 Prefab 需要**一次性遷移**（見第 4 節）。
- `AnimancerFacade` 由 `GetComponentInChildren` 改為 `GetComponent`（Root），舊有「把 `AnimancerComponent` 放子物件」的擺法會被校驗擋下——這是刻意的收斂。
- Editor 校驗會在 `applyRootMotion` 被誤勾時主動關閉並將 `Animator` 標記為 dirty，會產生一筆 Prefab／場景差異（僅在真的被誤勾時觸發）。

---

## 4. 遷移步驟 (Migration) — `Assets/Prefabs/X Bot.prefab`

目前 `X Bot.prefab` 為 Mixamo FBX 的 Prefab Variant，`Animator` 由基底 FBX 繼承（單層結構）。需在 Unity Editor 手動遷移（**不建議手改 YAML**）：

1. 開啟 Prefab，於根物件下**新增一個空子物件**，命名為 `Model`（名稱僅供人類辨識，程式碼不依賴它）。
2. 把美術／骨骼相關節點（`Animator` 所在的模型子樹、`SkinnedMeshRenderer`、骨架）搬進 `Model`。
3. 確保 Root 上保留：`CharacterController`、`CharacterPipelineRunner`、`MotionDriver`、`AnimancerFacade`、`AnimancerComponent`、`PlayerInputSource`。Root 上**不得**殘留 `Animator`。
4. 在 Root 的 `AnimancerComponent` 上，把 `Animator` 欄位指向 `Model` 子物件的 `Animator`。
5. 確認 `Model` 的 `Animator` 綁定 Humanoid `Avatar`，`applyRootMotion` 取消勾選（即使忘記，Runtime 也會被強制關閉）。
6. 檢查所有 Inspector 引用（`ThirdPersonCamera.target` 等外部模組）一律指向 **Root**，不是 Model。
7. 進入 Play；若階層有誤，`AnimancerFacade.ValidateHierarchy()` 會直接拋出清楚的錯誤訊息。

---

## 5. 擴充彈性 (Future Extensibility)

本規範刻意不把邏輯綁死在「唯一一顆固定的 Model 子物件」：

- **FaceRig / WeaponRig / IKRig**：作為 Model 子樹下的節點或獨立 Rig 元件，引用 Model 底下的骨骼 `Transform`；與只讀黑板仲裁旗標的表現層 Controller（design-doc §4.6）分開放。
  （📌 2026-07-18 機械性補記，非決策變更：此預留已由 M3 Foot IK 兌現——`FootIKRig` 掛 Model、定位為 **Presentation Adapter**（動畫系統邊界的雙向轉接：讀 Target 套 IK＋寫 Pose 快照，各自單寫單讀），與 Root 端決策 Controller 以兩條單向資料管道橋接，規格見 `docs/02-dev-spec.md` §3.5。）
- **Animator Override / LOD Animator**：仍是 Model 這一層的 `Animator` 家族，維持「子物件恰好 1 個 Animator」不變。
- **Multiple Models**（換裝／可插拔外觀）：未來若需同時掛多個 Model，將第 2.3 節規則 2 由「恰好 1 個」放寬為「至少 1 個並指定主 Animator」即可，`AnimancerFacade` 對外介面不變。
