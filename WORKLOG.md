# WORKLOG

> 唯一的進度管理文件。每完成一項立即更新。
> 歷史架構決策請看 `docs/changelog.md` 與 `docs/ADR/`；此檔只管「現在手上的工作」。

---

## 今日目標（2026-07-17）

**M1 Locomotion 實作輪（F1＋F2）**：✅ 程式碼＋文件完成；⏳ 等待使用者 Unity Editor 資產作業與實測驗收（見下方「待使用者確認事項」）。

**流程**：設計輪（F1/F2 提案＋Animancer v8 API 逐條查證）→ 使用者裁決 → 實作。裁決紀錄：
- **Q1**：`Play`／`PlayWithCallback` 拔除 `transitionDuration`，Transition 資產＝單一真相（Single Source of Truth）。
- **Q2**：M1 不加 SmoothDamp，原值直送；Game Feel（平滑／加減速）留後續專門輪（→ Backlog B9）。
- **Q3**：先查證再決定、不預先加防呆——實查 `X Bot.prefab`：Move 僅 WASD 綁定、`2DVector` composite 無 mode 參數＝Unity 預設 `DigitalNormalized`（對角線模長恆為 1）→ **免 Clamp01、未加任何程式碼**。複查觸發條件：新增搖桿綁定或 composite 改 Analog 模式。

**完成項**：
- [x] **F1**：`AnimationFacadeBase` 簽名更新（Play/PlayWithCallback 拔 duration、新增 `ParamMoveSpeed` 常數、IsPlaying 多鍵語意註記）；`AnimancerFacade` 升級 `TransitionMapping`（string→`TransitionAssetBase`）＋ Awake 建表預熱（首播堆配置移到初始化）＋ `TryGetTransition` 雙防線 ＋ 移除 Lite 警報
- [x] **F2**：`SetFloat`／`SetBool` 由空殼轉正（寫 Animancer v8 `Parameters` 參數字典；「哪個 Mixer 聽哪個參數」由資產內 `ParameterName` 綁定，Facade 不持有 Mixer 引用）；`CharacterPipelineRunner.SyncAnimation()` 每幀送 `MoveSpeed`
- [x] **文件**：dev-spec v0.16（§3.1/§3.2 契約與 Mixer 規格）／design-doc v0.16（Trade-off 新增四列：過渡資料載體、Locomotion 表現載體、映射鍵 string vs StateType 複核、AnimationSetSO YAGNI 延後＋平滑遷移路徑）／changelog v0.16
- [x] **範圍紀律**：FSM 拓撲、全部 State、MotionDriver、黑板 schema 零改動；16 條 EditMode 測試零觸及；不開 ADR（Living Docs 路由）

**追加輪（同日，動畫資產治理 v0.16.1）**：使用者裁決資產策略改為 **FBX 子 clip 直引**（AnimationClip 預設不可變、Ctrl+D 重萃取廢止、Mixamo 一律不勾 In Place）。
- [x] `.anim` 盤點：五支全為純設定快照（`m_Events` 全空、零內容修改）→ **全數退場，無一符合保留條件**
- [x] `MotionClipImportSOP` v2：Locomotion preset 拆「原地／位移」兩個（位移型＝XZ 不 Bake 供採速度＋Loop）
- [x] 文件：CLAUDE.md 新增 Animation Assets 規範章；dev-spec v0.16.1（§0.4 規則 0＋矩陣拆列＋In Place 反轉）；design-doc v0.16.1（Trade-off 新列）；changelog v0.16.1（遷移 SOP＋引用風險盤點）
- 🔴 風險盤點重點：Prefab 映射現為 `"Idle/Move"` **合併鍵**（查表比對完整字串，兩狀態動畫都播不出來）、Locomotion 的 Walking child 為 Missing（GUID 更替）、Jump/Roll 映射未接、Fast Run FBX 從未套過 preset——修正全數併入遷移 SOP

---

## 前一輪摘要（2026-07-14，詳見 changelog v0.14.2～v0.15.1）

Jump 腳滑／懸浮收案：匯入矩陣定調（dev-spec §0.4，Jump 家族 Y Based Upon=Feet 關鍵解）＋ CapsuleFitter v1.1（skinWidth 與 Center 原子綁定、G=skinWidth 定律實證）＋ 決策收錄輪（Q1~Q4）。全數於匯入層＋膠囊幾何層根治，Runtime 零修改結案。

---

## 工作清單

### Done（2026-07-17 M1 實作輪）
- [x] F1 Transition 資產機制（三檔：`AnimationFacadeBase`／`AnimancerFacade`／`CharacterPipelineRunner`）
- [x] F2 Locomotion 參數資料流（黑板 → 參數字典 → 資產綁定）
- [x] Living Docs v0.16 同步（dev-spec／design-doc／changelog）

### Done（2026-07-14，摘要）
- [x] `MotionClipImportSOP`＋`CharacterCapsuleFitter` v1.1；dev-spec §0.4 匯入矩陣定調；Q1~Q4 決策收錄（詳 changelog）

### Doing
（無）

### Todo
（無 — 等待使用者完成 M1 Editor 作業與實測後排定下一輪；建議順序見文末）

---

## Backlog / Future Work（超出目前範圍，不動手）

### 使用者明定 Future Work
- ~~**F1 ITransition / TransitionAsset 重構**（Facade 升級，Mixer 前置；即原 C3）~~ ✅ 已完成（2026-07-17，M1；changelog v0.16）
- **F2 Animancer Mixer 導入**：~~Locomotion 1D~~ ✅ 已完成（2026-07-17，M1）；**Strafe 2D 尚未動工**（等瞄準／鎖定移動需求，屬射擊/ARPG 模式範疇；Facade 與參數通道已就緒，屆時純資產＋一個新參數鍵）
- **F3 Combat 動畫架構**（ARPG Combo／FPS）
- **F4 Upper Body Layer**（Avatar Mask 上半身）
- **F5 Foot IK**（原 C4；Step 1/2 排除殘差後再評估）
- **F6 Input Arbiter Pipeline**（第四階段）
- ~~**F7 Animancer Pro 文件同步**~~ ✅ 已完成（2026-07-14，design-doc v0.14.3）

### 工具/演算法 Backlog
- **B1 `CalculateRotationFinishedTime` 的 `Mathf.DeltaAngle` 疑慮**（`MotionBakeEditor.cs:249`）：已裁決（2026-07-14）暫不修正——僅影響 Roll 類短旋轉動畫、實害機率低、修正需重烘焙。引入大角度旋轉動畫（原地轉身 ≥360°）時重啟評估。
- **B2 多段跳「空中段前搖期間落地」邊角**（`JumpState.OnTick`）：現況空中段 clip 前搖必為 0，無實害；屬 Coyote/Buffer 手感議題，建議併入 ADR-002 §6-4 後續 ADR。
- **B3 `PlayWithCallback` lambda 閉包 GC**：dev-spec §5 既有待辦（回調 ObjectPool），仍無呼叫端，維持追蹤。（v0.16 簽名已拔 duration，閉包問題本身不變。）
- **B4 Config `bakeMappings` 中 Jump(State 3) 條目已無消費者**：無害冗餘，下次於 Unity Editor 動 Config 資產時順手移除（AI 不動 `.asset`）。
- **B5 CapsuleFitter v2**：Head/Neck 骨骼推估身高為權威、bounds 降級為條件精化（≤骨骼估計×1.10 才採用）、髖寬/2 半徑下限、skinWidth/stepOffset 納入自動匹配。
- **B6 ValidateHierarchy 增補「Model local transform 非 identity」警告**：Capsule 對齊規範的 Runtime 側防線，屬 `AnimancerFacade.cs` 修改，另輪處理。
- **B7 前搖期間輸入未鎖的手感設計**：候選解依架構契合度：仲裁層 BlockInput（F6 範疇）＞ MotionDriver 受約束移動出口；禁止 JumpState 改寫 MoveDirection（單一寫入者）。
- **B8 Locomotion 的 Loop Pose 評估**：**Walking clip 本輪進場（M1）——匯入後若走路循環有接縫，即為啟動時機**；匯入 SOP 工具維持不動 loopPose。
- **B9 動畫參數平滑（Game Feel 輪；2026-07-17 新增，Q2 裁決遺留項）**：鍵盤 0/1 二值輸入下 Idle↔Run 混合為瞬時跳變（Mixer 權重是參數的純函數）。候選落點：`SyncAnimation` 表現端 SmoothDamp（不動黑板與物理手感）vs Parameter Processor 平滑黑板值（連帶加減速手感一起改）。建議與加減速曲線、（可能的）搖桿綁定一起做專門輪。
- **B10 Facade 映射鍵 Editor 驗證工具（2026-07-17 新增，低優先）**：比對 `transitionMappings` 的 StateKey 與 `StateType` 名稱／`AnimationKey` 覆寫，於接線期抓 typo——design-doc §5 v0.16「映射鍵維持 string」列的既知代價補償措施。

---

## 待使用者確認事項

**M1 收尾＝v0.16.1 遷移（2026-07-17 晚間盤點：使用者已完成絕大部分 ✅）**：
- [x] `.anim` 五支全刪；五份 Bake 重烘並直指 FBX 子 clip（`Bake_Stand To Roll` 曲線與舊值逐位一致，一致性驗證通過）
- [x] Transition 全部直指 FBX 子 clip（Locomotion 三 child／Jump／新建 Roll）；Prefab 四條映射接對（合併鍵已拆）
- [x] 調參全到位：Walk threshold **0.3**、Fade **0.15**、`MotionDriver.moveSpeed` **5.66**
- [ ] 🔴 **唯一殘留（Roll 秒退根因）**：`PlayerStateMachineConfig` 的 `bakeMappings` 四條全指向已刪除的舊 `Bake_Anim_*`（全專案僅剩的死引用）→ `GetBakeData(Roll)` 得 null → `RollState` 走 0.5s fallback 即離開。**修法**：State 4（Roll）改指 `Bake_Stand To Roll.asset`；其餘三條（Idle／Move／Jump）**整條刪除**——無 runtime 消費者，Jump 的真正路徑（`JumpStateParams` → `Bake_Jump`）已接對，順手收掉 B4。
- [ ] 驗收：v0.16 §4 六項＋Roll 時長恢復 ≈2.38s＋EditMode 測試 16 條。

**前輪遺留**：
- CapsuleFitter v1.1 收案最後一步：選場景角色 Root → `Tools/Project/角色 Capsule 自動對齊 (CapsuleFitter)` → **Apply 到 Prefab** → 空中放下確認貼地不變。此後 skinWidth 請勿單獨手調，一律重跑工具。
- EditMode 測試（16 條）結果尚未回報（2026-07-13 起遺留，可與本輪驗收一起跑）。

---

## 建議下一步（M1 後里程碑順序，2026-07-14 規劃審視輪提案）

- **M1 Locomotion 升級** ✅ 程式碼側完成（2026-07-17）——收尾＝上方使用者作業＋實測
- **M2 Audio 模組**（第一個表現層 Controller）＋依約落地 `JustLanded`／`JustLeftGround`（其第一個消費者出現）
- **M3 Foot IK 模組**（不平地面腳部貼合；掛 Model 子樹，ADR-001 §5 既定掛點）
- **M4 購入 locomotion 資產**（Movement Animset Pro 級別）→ 左/右腳停步、90/180 pivot、方向性起步＋執行期 foot-phase 資料設計（烘焙管線加 analyzer 即可，零改採樣迴圈）
- **M5 Combat 初版（ARPG）**→ 產生 Hit/Death 等真正需要「封鎖」的狀態
- **M6 ArbiterPipeline**（§7/§8.3 的旗標粒度開放問題屆時有真實案例可答）
- 表情模組：暫緩——X Bot 無臉部 rig/BlendShapes，等有臉部資產的角色進場再排
