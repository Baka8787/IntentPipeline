# WORKLOG

> 唯一的進度管理文件。每完成一項立即更新。
> 歷史架構決策請看 `docs/changelog.md` 與 `docs/ADR/`；此檔只管「現在手上的工作」。

---

## 今日目標（2026-07-14）

**第二輪（分析輪，進行中）**：Jump 前搖腳滑根因分析、CharacterController 自動對齊方案評估、Animancer Pro 動畫下載規劃。純分析不改程式，方案待使用者確認後才實作。

**證據（已查實）**：
- `X Bot@Jump.fbx.meta`／`X Bot@Idle.fbx.meta`：`clipAnimations: []` → 四支動畫全用 Unity 預設匯入（XZ Based Upon=Center of Mass、Bake Into Pose 全關、Loop Time 全關）
- `X Bot.prefab`：CharacterController 全預設（Height=2, Radius=0.5, Center=(0,0,0)）；Model 子物件 `localPosition.y = -0.996` 手動補償 capsule 幾何 → Root 原點懸在角色胸口高度
- 場景無 capsule override
- 新資訊：專案已升級 **Animancer Pro**（文件 Trade-off 表仍記載 Lite 策略，待同步）

**使用者回饋（2026-07-14）**：分析方向認同，堅持「先驗證、再修改」。
- **C1 拆兩步**：Step 1＝改 Import Settings 並實測腳滑是否消除；Step 2＝若殘留，啟動 MotionDriver／JumpState／Input 共因分析（決策樹已預備）。SOP 在驗證通過後才寫入 dev-spec 定調。
- **C2 定向核可**：Editor Tool（非 Runtime）確定；v1 範圍限定 Height／Center／Radius(humanScale)／Model 歸零／Undo／必要警告，骨骼推估等留 v2。

**第三輪（實作輪，2026-07-14）✅ 已完成（詳見 changelog v0.15）**：
- [x] 新增 `Editor/Tools/MotionClipImportSOP.cs`：三 preset 右鍵套用匯入矩陣（經 defaultClipAnimations，保引用不斷鏈）——Step 1 執行器
- [x] 新增 `Editor/Tools/CharacterCapsuleFitter.cs`：CapsuleFitter v1（依核可範圍，排除項留 TODO 註記）
- [x] dev-spec §0.3 新增規則 6「Capsule 對齊規範」、§0.2 補 Tools 目錄、版本升 v0.15；changelog v0.15 條目
- [x] **紀律遵循**：Import 矩陣未寫入 dev-spec（等 Step 1 實測通過）；零 Runtime 程式碼變更；未動 .asset／.meta

**Step 1 第一次實測（2026-07-14）**：使用者已套 preset＋重烘焙（meta 證實 XZ/Rot Bake 生效、特徵值 0.736s/0.527m/10.09 自洽）。結果：水平滑移解決，**殘留垂直症狀**——「蹲下階段身體往腰/肚子方向靠攏」（腳向骨盆收攏、蹲不下去）。
**根因精修（可自證）**：Y 的 Based Upon＝Original 時，整段垂直運動（含蹲下下沉）被歸入 root motion Y——證據即烘焙器能從 root 世界 Y 量到 apex；執行期 applyRootMotion=false 丟棄整條 → 下沉被抹平。**修正：Jump preset 的 Y Based Upon 改為 Feet**（Y 的 Based Upon 是全程連續追蹤：貼地段 root Y 持平、下沉留在姿勢；滯空段腳升高才進 root Y，烘焙可量、執行期由物理接管）。工具已更新。

**Step 1 第二次實測（2026-07-14）**：✅ **蹲下正常、腳底穩定**（Y=Feet 修正驗證通過）。
**新現象 → 迭代 2**：所有狀態均勻懸浮（~8cm），但 Model 底與膠囊底彼此貼合（使用者空中放下驗證）。根因＝CharacterController 落地時膠囊面與地面恆保持 `skinWidth`（0.08）緩衝間隙（引擎防穿插設計），「膠囊底＝腳底」對齊法必然懸浮該距離——**CapsuleFitter v1 Center 公式缺陷，已修**：`center = (0, height/2 + skinWidth, 0)`；dev-spec §0.3 規則 6 與 changelog v0.15 §4 補記同步。

**⏳ 等待使用者（迭代 2 驗證）**：
1. （建議）選 Root 把 CharacterController 的 Skin Width 手動改為 0.03（radius 0.3 的 10%；工具依約不動此欄位）
2. 重新執行 CapsuleFitter（Center 現依 skinWidth 補償）→ Apply Prefab
3. 空中放下重測：腳底應貼地（誤差 < 1cm）；順帶走一下斜坡/台階確認 stepOffset 無異常
4. 全數通過後回報 → 我把 Import 矩陣（含 Y=Feet）正式寫入 dev-spec 定調＋指引 Fast Run/Stand To Roll 套用與重烘焙 Roll，任務一、二結案

---

## 前一輪目標（2026-07-14 決策收錄輪）

收錄使用者對 Q1~Q4 的裁決至 CLAUDE.md 與 Living Docs（純文件輪，零程式碼變更）。 ✅ 全數完成

---

## 工作清單

### Done（2026-07-14 決策收錄輪）
- [x] **Q1 收錄**：資料夾結構正式定調現狀 — `CLAUDE.md` 新增「Project Structure (Canonical)」章節；dev-spec §0.2 由「⚠️ 現況說明／待決」改為「✅ 正式定調」，`_Project/` 收攏規劃標記廢止
- [x] **Q2 收錄**：`JustLanded`／`JustLeftGround` 延後實作（等第一個下游消費者）— dev-spec §1.1 code block／讀寫權限表／§5 待補清單、design-doc §4.2／§5 Trade-off 表／§8.2 全數同步標註
- [x] **Q3 收錄**：序列化欄位命名豁免 — `CLAUDE.md` AI Coding Rules 與 dev-spec §0.1 加入「`[SerializeField]` 私有欄位統一 `camelCase`（無底線）」明文條款
- [x] **Q4 收錄**：Backlog B1 標記「已裁決：暫不修正」（見下方 Backlog）
- [x] 版本狀態：design-doc／dev-spec 頭部升 v0.14.2（2026-07-14）並各補修訂行；changelog 頂部新增 v0.14.2 決策收錄條目

### Done（2026-07-13 維護收尾輪，摘要）
- [x] 全檔盤點程式碼↔文件一致性；確認 `Bake_Anim_Jump.asset` 已依 v0.14 重烘焙且接線正確
- [x] 文件同步：`VerticalVelocity` ADR-002 §6-1 延後註記、dev-spec §0.2/§1.1/§2.1/§3.1 對齊實碼、版本狀態修復
- [x] 測試：新增 `MotionFeatureAnalysisTests`（9 條）＋ `StateMachineConfigTests`（3 條）；tests asmdef 補 `Project.Editor` 引用
- [x] 清理：Idle/Move/Roll log 包 `#if UNITY_EDITOR`；`PlayWithCallback` 失實註解修正
- [x] changelog v0.14.1 條目；建立本 WORKLOG

### Doing
（無）

### Todo
（無 — 待使用者本地跑完測試後排定下一輪）

---

## 已完成內容摘要（2026-07-14 輪）

**本輪性質：純文件（決策收錄），零程式碼變更、未動任何 `.asset`／場景、無 Git 操作。**

| 檔案 | 內容 |
|---|---|
| `CLAUDE.md` | 新增 Project Structure (Canonical) 章節（Q1）；AI Coding Rules 命名規範加序列化欄位豁免（Q3） |
| `docs/02-dev-spec.md` | §0.2 定調（Q1）、§0.1 豁免條款（Q3）、§1.1×2＋§5 JustLanded 延後標註（Q2）、頭部 v0.14.2＋修訂行 |
| `docs/01-design-doc.md` | §4.2／§5 Trade-off 表／§8.2 JustLanded 延後標註（Q2）、頭部 v0.14.2＋修訂行 |
| `docs/changelog.md` | 頂部新增 v0.14.2 決策收錄條目（Q1~Q4 裁決與反思） |
| `WORKLOG.md` | 本檔更新 |

---

## 決策紀錄（2026-07-14 裁決，詳見 changelog v0.14.2）

- **Q1** 資料夾結構：**定調現狀**（`Assets/Scripts/` 直掛），`_Project/` 收攏規劃廢止，不做 Asset 遷移。
- **Q2** `JustLanded`／`JustLeftGround`：**延後實作**（YAGNI），等第一個下游消費者出現。
- **Q3** 命名規範：**明文豁免** `[SerializeField]` 欄位採 `camelCase`，不做 `FormerlySerializedAs` 遷移。
- **Q4** Backlog B1：**暫不修正**，維持紀錄。

---

## Backlog / Future Work（超出目前範圍，不動手）

### 使用者明定 Future Work（2026-07-14 指示，禁止提前實作）
- **F1 ITransition / TransitionAsset 重構**（Facade 升級，Mixer 前置；即原 C3）
- **F2 Animancer Mixer 導入**（Locomotion 1D／Strafe 2D）
- **F3 Combat 動畫架構**（ARPG Combo／FPS）
- **F4 Upper Body Layer**（Avatar Mask 上半身）
- **F5 Foot IK**（原 C4；Step 1/2 排除殘差後再評估）
- **F6 Input Arbiter Pipeline**（第四階段）
- **F7 Animancer Pro 文件同步**（原 C5：design-doc Trade-off 表仍記 Lite 策略，待使用者確認升級時間點）

### 工具/演算法 Backlog
- **B1 `CalculateRotationFinishedTime` 的 `Mathf.DeltaAngle` 疑慮**（`MotionBakeEditor.cs:249`）：累計偏航角接近 360° 整數倍的動畫會被誤判收斂。**已裁決（2026-07-14）：暫不修正**——僅影響 Roll 類短旋轉動畫、實害機率低、修正需重烘焙成本過高。若未來引入大角度旋轉動畫（原地轉身 ≥360°）再重啟評估。
- **B2 多段跳「空中段前搖期間落地」邊角**（`JumpState.OnTick`）：現況空中段 clip 前搖必為 0，無實害；屬 Coyote/Buffer 手感議題，建議併入 ADR-002 §6-4 後續 ADR。
- **B3 `PlayWithCallback` lambda 閉包 GC**：dev-spec §5 既有待辦（回調 ObjectPool），無呼叫端，維持追蹤。
- **B4 Config `bakeMappings` 中 Jump(State 3) 條目已無消費者**：無害冗餘，下次於 Unity Editor 動 Config 資產時順手移除（AI 不動 `.asset`）。
- **B5 CapsuleFitter v2**（2026-07-14 新增，使用者核可的混合方案完整版）：Head/Neck 骨骼推估身高為權威、bounds 降級為條件精化（≤骨骼估計×1.10 才採用）、髖寬（LeftUpperLeg↔RightUpperLeg）/2 半徑下限、skinWidth/stepOffset 納入自動匹配。
- **B6 ValidateHierarchy 增補「Model local transform 非 identity」警告**（2026-07-14 新增）：Capsule 對齊規範（dev-spec §0.3 規則 6）落地後的 Runtime 側防線，屬 `AnimancerFacade.cs`（Runtime 檔）修改，本輪 scope 外。
- **B7 前搖期間輸入未鎖的手感設計**（2026-07-14 新增，Step 2 嫌疑之一）：`JumpState` 前搖 0.72s 走 `ExecuteBaseMovement` 會隨輸入滑行/轉身。若 Step 1 後判定需要鎖定，候選解依架構契合度：仲裁層 BlockInput（F6 範疇）＞ MotionDriver 受約束移動出口（Public API 變更需核可）；禁止 JumpState 改寫 MoveDirection（單一寫入者）。
- **B8 Locomotion 的 Loop Pose 評估**（2026-07-14 新增）：匯入 SOP 工具刻意不動 loopPose；若 Mixamo walk/run 循環有接縫再評估開啟。

---

## 待使用者確認事項

（無 — Q1~Q4 已全數裁決並收錄）

---

## 建議下一步

1. **Step 1 驗證（使用者，Unity Editor）**：套用 Idle/Jump 匯入 preset → 重烘焙 Jump → 四項驗證（流程見 changelog v0.15 §3 與本檔「⏳ 等待使用者」）。
2. **CapsuleFitter 套用（使用者）**：場景 Root 執行工具 → Apply Prefab → 角色下移貼地。
3. **回報結果後（AI）**：通過 → Import 矩陣寫入 dev-spec 定調＋指引 Run/Roll 套用與重烘焙；未過 → 啟動 Step 2 共因分析（決策樹已備）。
4. （前輪遺留）Test Runner 跑 EditMode 測試（16 條）；Unity 會為新檔生成 .meta。
5. 下一輪範圍候選（均屬 Future Work，等使用者指派）：F1~F7 見 Backlog。
