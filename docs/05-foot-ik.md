# Foot IK 子系統規格（Presentation Layer）

> **狀態**：v1 已凍結（2026-07-21）
> **定位**：`docs/02-dev-spec.md` 的**子系統分卷**（Dev Spec 實作 API 層）。原 dev-spec §3.5 全文於 2026-07-25 遷入本檔。
> **章節編號原樣保留**（`3.5.1`～`3.5.4`）——既有交叉引用（`dev-spec §3.5.2` 等）在本檔以同一編號可直接定位，遷檔零斷鏈。
> **上游**：架構理念見 `docs/01-design-doc.md` §4.6；品質升級路線見 `docs/03-animation-roadmap.md`；跨領域契約（黑板／管線順序／驅動介面）仍在 `docs/02-dev-spec.md` §1／§2／§3.1。

---

### 3.5 Foot IK 子系統（🆕 M3，`Assets/Scripts/Presentation/IK/`）

> 定位：第二個 `IPresentationController` 實例（Presentation Pipeline 骨架的首次回收驗證，Runner 零改動）。
> IK Solver 採 **Unity 內建 Humanoid IK**（M3 裁決 Q1：專案重點是 Character Architecture、不是 IK Solver）。
> 範圍＝腳部貼合＋骨盆補償（Q2，一體不拆）。
> 🔒 **v1 凍結（2026-07-21，收案輪；roadmap `docs/03` §1）**：架構層全部健康、剩餘問題全屬「已知限制」（§3.5.2 L1~L6，各有不改架構的升級路徑），品質升級改由整體 Animation Runtime Roadmap 承載、不再單點深挖。**設計哲學**（使用者裁決，全文 design-doc §4.6）：**Natural Pose > Terrain Adaptation > Perfect Foot Contact**——禁再加 Fade／Gate／權重修正修穿模，貼地改善走 Ground Sampling 升級（Heel/Toe 雙點、CapsuleCast）。**旋轉公式定形**：`FromToRotation(worldUp, hit.normal) × poseRot`（保留俯仰式，動畫腳踝俯仰原樣保留；A/B 軸對齊式「主動壓平」實測無感差已歸檔，changelog v0.18.7）。

#### 3.5.1 資料流（M3.1：雙管道；🔄 M3.x-A 修正擁有權）

> 🔄 **M3.x-A（2026-07-27）擁有權規則**：**管道的 lifetime owner ＝ 該管道的唯一 Writer。**
> * `FootIKTargetData`：`FootIKController` 寫 → **Controller 擁有**，注入給 Rig 讀。維持**單寫單讀**。
> * `FootIKPoseData`：`FootIKRig` 寫 → **Rig 擁有**（以欄位初始式持有），Controller 向它取引用。自此為**單寫多讀**。
>
> ⚠️ **owner 的語意限定**：`FootIKRig` owns the *lifetime* of the pose snapshot **because it is the sole writer** of that snapshot。這是**生命週期／權威的擁有權**，**不是業務擁有權**——Rig 不解讀這份資料、不判定 plant/lift、不認識任何消費端，兩個方向依然是純轉接、零決策分支。解讀屬各 Reader 自己的事。
>
> **為什麼要改**：先前 Controller 同時 `new` 出兩份資料，等於**擁有一份自己不寫的資料**。單讀時只是輕微味道；一出現第二個 Reader，新 Reader 就被迫向另一個 Controller 要引用——直接違反 `IPresentationController` 的「Controller 彼此不得互相引用」。改成「擁有權跟著寫入權走」後，新增 Reader 只需向 owner 取引用，**管道與既有 Reader 皆零改動**。
>
> **為什麼用欄位初始式而非 `Awake`**：欄位初始式在元件建構時執行、**早於所有 `Awake`**，所以「拿得到同一份有效實例」從時序紀律變成**結構保證**，讀取方不必關心元件間的 Awake 順序。由 `§7.1-A11` 與 `FootIKTests` 守住。

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
| `FootIKController` | Root | 讀黑板、讀 Pose 快照（🔄 M3.x-A：**Reader 之一**，不再是擁有者也不再是唯一 Reader）、雙腳 raycast（地面點＋法線）、權重計算（Q3 Pose Heuristic：動畫 goal 高度 `min~max` 線性帶）、權重／骨盆平滑（MoveTowards）、骨盆補償計算；`FootIKTargetData` 唯一 Writer | **對 Animator 零依賴**（M3.1：不持骨骼 Transform、不呼叫 `GetBoneTransform`）、呼叫任何 `SetIK*` |
| `FootIKTargetData` | 純 C# 資料 | Target 管道（Controller→Rig）：雙腳目標（位置／旋轉／雙權重）＋`PelvisOffsetY`。值語義（權重 0＝不生效），Reader 零布林判斷 | 不進 `PlayerRuntimeData`；不與 Pose 混入同一結構 |
| `FootIKPoseData` | 純 C# 資料 | Pose 管道（Rig→各 Reader，**單寫多讀**）：動畫原始 IK goal（雙腳位置／旋轉，IK 套用前）＋Avatar 常數 `FeetBottomHeight`＋`IsWarm` 初始化標記。🔄 lifetime owner ＝ 唯一 Writer `FootIKRig` | 不存 Animator／Transform／MonoBehaviour／Physics 引用；Reader 不得寫入 |
| `FootIKGroundData` 🆕 | 純 C# 資料 | **Ground 管道（Controller→各 Reader，單寫多讀，🆕 Stage 1）**：雙腳的 `HasGround`／`Point`／`Normal`／`GroundY`／`Collider`＋`IsWarm`。lifetime owner ＝ 唯一 Writer `FootIKController`（同一條「擁有權跟著寫入權走」規則） | ⚠️ 這是**物理觀測**不是 IK 決策——權重／目標／骨盆仍完全私有。讀取方只讀不寫、**不得跨帧快取 `Collider`**。現有 Reader ＝ `FootstepDetector`（正本 `docs/35` §4）|
| `FootIKRig` | Model（`[RequireComponent(Animator)]`） | **Presentation Adapter**（動畫系統邊界雙向轉接，各方向單寫單讀）：OnAnimatorIK **開頭** `GetIKPosition/GetIKRotation`＋`FeetBottomHeight` 寫入 Pose 快照（唯一 Writer）→ 接著原樣套用 TargetData（唯一 Reader）→ SetIK*／bodyPosition | Raycast、讀黑板、`IsGrounded`／狀態判斷、權重演算法、地面採樣、任何額外判斷 |

* **組裝**（🔄 M3.x-A）：`FootIKController.Awake` **只建立自己寫的 `FootIKTargetData`** → `rig.Bind(targetData)` 一次性注入 → 再由 `rig.PoseData` **取得** Pose 引用（Rig 以欄位初始式持有，故不需要等 Rig 的 `Awake`）。⚠️ 未來新增的 Pose Reader 一律**向 owner 取**（`FootIKRig.PoseData`），**不得**向 `FootIKController.PoseData` 要——後者構成 Controller 互相引用；此後兩者僅透過兩條單向共享數據溝通，執行期**無任何方法呼叫／事件／回呼**（M3 裁決明禁 Event Bus／Message System／Callback）。Humanoid Avatar 有效性由 `AnimancerFacade.ValidateHierarchy` 既有 Fail-Fast 防線把關，IK 模組不重複驗。
* **IK pass 開啟**：`FootIKController.Start` 經 `AnimationFacadeBase.SetApplyAnimatorIK(0, true)`（🆕 基底 virtual no-op；`AnimancerFacade` 覆寫為 `Layers[i].ApplyAnimatorIK`）。放 Start＝確保 Facade 的 Awake 已完成，不賭同幀 Awake 順序。
* **Q4（Roll／Jump）**：不特判狀態——空中 `IsGrounded=false` 自然關閉；Roll 中腳部蜷起由 pose 權重自然降低。`BlockIK` 讀取契約先行——🆕 **writer 已於輪 4 落地**（`ArbiterPipeline`，順序 4.5），但目前沒有任何 `IArbiterSource` 要求 `BlockIK`，旗標仍恆 `false`，`FootIKController` **零改動**（契約先行的代價在此兌現）。實測若 Roll 吸地明顯，回 Arbiter Pipeline 解決（Future Work）——屆時作法是新增一顆讀 FSM 狀態的來源，而非改本檔。
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

#### 3.5.4.1 🔴 斜坡骨盆震盪（2026-09-08，probe 實測確診並修正）

> **症狀**：角色**站在小斜坡上**時，在「骨盆高／膝蓋直」與「骨盆低／膝蓋彎」兩態間反覆跳動。
> **平地完全正常。**

**根因**——`SampleGround` 兩點採樣路徑的最後一行（已移除）：

```csharp
sample.GroundY = heelPenetration >= toePenetration ? heelHit.point.y : toeHit.point.y;
```

`lift = Max(0, Max(heelPen, toePen))` **對輸入連續**；
但 `GroundY` 是「取 **argmax 的另一個屬性**」——在 `heelPen == toePen` 的交叉點上，
回傳值從 `heelHit.point.y` **跳到** `toeHit.point.y`。斜坡上這兩點不等高 ⇒ **跳幅 ≈ heel-toe 跨距 × 坡度梯度**
＝ `(HeelOffset + ToeOffset) × 梯度` ＝ `0.25 × 梯度`。

📌 **`max()` 是連續的；「選出 argmax 之後改讀它的別的欄位」不是。** 這是本次最值得記住的一句。

**為什麼只有斜坡發作**：平地上 `heelHit.point.y == toeHit.point.y` ⇒ 三元在兩個**相等**的值之間選 ⇒ 跳幅 0 ⇒ 不可觀察。
讓兩者反覆交叉的是 idle 動畫的細微腳部位移 ＋ 角色沿坡面的緩慢滑動。

**實測佐證（throwaway probe，已刪除）**：五次取樣震盪幅度 `0.042`～`0.069` m。
反推梯度 `0.17`～`0.28` ⇒ **坡度 9.5°～15.5°**，與「小斜坡」吻合——**量化上對得起來，不是猜的**。
probe 同時證明 `IsGrounded`／`L.hit`／`R.hit`／`ikAllowed` **全程未翻動** ⇒ 排除了
「raycast 間歇落空」與「`CharacterController.isGrounded` 在斜坡上跳動」兩個更直覺的假說。

**修法**——`GroundY` 改由**抬升後的實際腳底平面**導出（＝ ankle-only 路徑本來就在用的同一條公式）：

```csharp
sample.GroundY = ComputeSoleHeight(TargetPosition, SoleNormal, footBottomHeight,
                                   worldHeel, heelHit.point, worldToe, toeHit.point, out float lift);
sample.TargetPosition.y += lift;
```

**為什麼這是對的，而不只是「比較平滑」**：骨盆補償要問的是
**「這隻腳最後被放在多低，骨盆得沉多少才搆得到」**——那是**解算後的腳底高度**，
不是「某一端底下的地面高度」。新值是 `TargetPosition`（連續）與 `lift`（連續）的函數 ⇒
**連續性由構造保證**，⛔ 不是靠加遲滯去掩蓋一個仍然存在的斷點
（與 `docs/15` §18.3 的 HeadLook clamp 是同一條紀律）。

- **平地逐字等價**：兩端無戳穿時 `lift = 0`，結果 ＝ `hitPoint.y` ＝ 舊值 ⇒ **不是行為改變**。
- **斜坡上骨盆會比舊版少沉一點**（沉到腳實際被放的位置，而不是最低端底下的地面）⇒ **這是 Play 要看的差異**。
- **不變量**：`FootIKTests.SoleHeight_SlopePenetrationCrossover_IsContinuous`
  以 12° 斜面掃過交叉點 40 步，斷言相鄰輸出差 ≤ 輸入步長（舊寫法會跳 ~5.3 cm ＝ 步長的 265 倍）；
  `SoleHeight_FlatGround_EqualsAnkleOnlyPlane` 守住平地等價。

**⚠️ 刻意沒修的相鄰風險（已登記，勿當成遺漏）**：`Tick` 裡
`pelvisTarget = (ikAllowed && left.HasHit && right.HasHit) ? ... : 0f` 同樣是「輸入不可用就硬切回 0」，
結構上屬同一類缺陷。但 **probe 證明這條路徑本次根本沒觸發** ⇒ 依專案紀律
（`CLAUDE.md`：沒有證據支撐的防呆一律擋下）**不加防呆**。它是潛在風險，不是已觀察到的缺陷。

#### 3.5.4.2 支撐加權骨盆 ＋ pose 同時刻的支撐量（2026-09-24，使用者裁決；正本推導見 `docs/44` §9.5–§9.6）

> **症狀**：樓梯移動面（`docs/44`）上下樓「跳動明顯」。量測：骨盆補償在 0 與 −0.35 m（上限）之間鋸齒，
> 髖骨垂直速度逐帧變化 p95 是平地的 10–20 倍；Run 上樓則被雙腳命中閘門鎖在 0。

**兩個改動：**

1. **支撐量改用 pose 同時刻的平面**：`FootIKPoseData.PosePlaneY`（Rig 在 OnAnimatorIK 與腳位一起寫入）。
   原本用 Tick 當下的 `transform.position.y`，但 root 在快照之後（LateUpdate）才移動——在移動面上 Run 每帧差 5–8 cm。
   同一個 stance 量同時是 IK 目標權重（③）與骨盆貢獻（②），兩者對「哪隻腳在支撐」不會各說各話。平地上逐位元不變。
2. **骨盆 ＝ 支撐加權的下沉需求**（`ComputeSupportedPelvisOffset`，取代 `ComputePelvisOffset`）：

   ```
   need_i = HasHit_i ? stance_i × Clamp(GroundY_i − rootY, −MaxPelvisOffset, 0) : 0
   target = ikAllowed ? min(need_L, need_R) : 0          （之後 MoveTowards，速率不變）
   ```

   - 擺盪腳（stance 0）的 ray 仍照常取得下一階供 IK 落點，但**不拉動骨盆**。
   - ⛔ 不是新的 HasGround 閘門：沒命中的腳只是「沒有需求」，不再讓整個骨盆歸零（舊規則讓 Run 上樓骨盆鎖在 0）。
   - `min` 換邊只有斜率折點；剩下的值跳變只有「落下中的腳換階」時的 stance × 級高，由既有 MoveTowards 限速。
   - 符合 Natural Pose > Terrain > Contact：沒有新增 Fade／Gate／權重補丁，不縮小活動空間。

**量測結果（`docs/44` §9.6）**：上限命中歸零；多數情境髖骨抖動下降（Walk 上樓 −35%、Run 下樓 −30～−43%）。
⚠️ **殘留**：髖骨在世界空間呈「階梯狀」——單支撐期間髖部維持在支撐踏面上方固定高度（root 持續下降、骨盆同步回升），
換腳時再以 −6～−8 m/s 下沉一級。這是「支撐腿長度不變」的幾何結果，不是訊號錯誤；Run 上樓因骨盆不再被鎖 0 也出現同樣的形狀。
要讓髖部沿斜面平滑移動，需要讓支撐腿在單支撐期間彎曲（骨盆不再只追「目前支撐踏面」）——這是新的骨盆模型，**待使用者裁決**。

#### 3.5.4.3 身體跟隨移動參考面的包絡（2026-09-24，Codex 兩輪審查；✅ 基底已實作、X Bot 啟用；❓ 支撐貢獻與下沉深度待使用者裁決）

**前提（已量測，`docs/44` §9.7.1）**：Walk 樓梯上 root 是直線、動畫髖部起伏與平地相同，階梯感 100% 來自 `PelvisOffsetY`。

**使用者要求**：身體高度跟隨一條**連續**的移動／參考面；踏面接觸靠支撐貢獻＋膝／腿的伸展；**不是**把現有的離散「較低踏面」目標抹平。

##### 為什麼「逐腳需求」的任何變體都不行（離線模擬，同一份探針資料，模擬器以現行公式重現實測）

髖骨逐帧速度變化 p95（m/s），平地 Walk ＝ 0.28：

| 公式 | Walk 上樓 | Walk 下樓 | Run 下樓 |
|---|---|---|---|
| A 現行（支撐 × 踏面需求） | 5.8–6.0 | 4.8–7.3 | 5.6–5.8 |
| B 支撐 × **移動面**需求 | 4.1–5.9 | 2.2–7.4 | 2.7–3.3 |
| C B ＋ 腿長可達性不足補償 | 4.7–5.9 | 4.4–7.1 | 5.1–5.7 |
| D 雙腳（不加權）× 移動面需求 | 6.4–9.9 | 2.8–7.5 | 2.4–3.5 |
| **F 身體跟隨移動面包絡（L＝0.45）** | **0.24** | **0.22–0.24** | **0.57** |
| F ＋ 支撐加權安全網（腳搆不到才補） | 0.45–0.83 | 0.66–1.13 | **5.3–5.6** |

原因：只要身體高度由「腳現在在哪」決定，需求就隨步伐週期擺盪——下坡那隻腳在前時要多沉「半步幅 × 坡度」，換腳時再收回；
Walk 的支撐腿在動畫裡幾乎打直（伸展餘裕中位數 1.5 cm，Run 8 cm），IK 也吸收不了。加權、限速、加可達性修正都只改變擺盪的形狀。
**要連續，需求就必須來自地形本身，而不是腳。**

##### 模型 F（🔄 修訂 1，依 Codex 第一輪審查）

Codex 第一輪（3 High）：①沿 facing 取樣 ⇒ 原地轉身／橫移（敵人邊橫移邊面向目標）會改變身體高度；
②「root 在不在代理面上」與「高差門檻」是硬閘門；③用碰撞矩陣壓掉踏面支撐，違反「踏面＝視覺接觸真相」。修訂：

```
代理面   ＝ MovementCollisionMask(角色) & ~GroundLayers（角色移動會碰、腳看不到；目前 ＝ CharacterMovementSurface）
root 支撐 ＝ root 正下方的移動世界命中；rootOnProxy ＝ 它是代理面；n ＝ 它的法線
取樣點    P_k ＝ root ＋ L·(cos 45°k, 0, sin 45°k)，k＝0..7（世界固定 8 方向，與 facing 無關）
h(P_k)   ＝ 命中代理面            ⇒ 命中高度
           否則若 rootOnProxy    ⇒ max(非代理命中高度, root 支撐平面在 P_k 的延伸高度)；沒命中 ⇒ 延伸高度
           否則                  ⇒ 不計入
Kref     ＝ min_k Clamp(h(P_k) − rootY, −MaxPelvisOffset, 0)
骨盆目標 ＝ !ikAllowed ? 0 : rootOnProxy ? Kref : min(Kref, §3.5.4.2 的逐腳支撐需求)
```

- **與朝向無關**：8 個世界固定方向在平面斜坡上給出 −L·tanθ·(0.92～1.00)，只和樓梯方位有關，轉身、橫移都不變。
- **沒有硬閘門**：懸崖（樓梯側邊、開放頂端）下方的地面被「root 支撐平面的延伸」取 max 蓋掉，不會把身體往下拉；
  樓梯底的地面和斜面起點同高，所以取樣點跨過底邊時數值連續。
  **Editor 掃描（兩道樓梯、中線與距側邊 5／15 cm 三條路徑、root 每 1 cm 一點、真實 raycast）**：
  修訂版最大跳變 **6–7 mm／cm**（進入樓梯時的斜率）；Codex 建議的「只計代理面」版在樓梯底跳 **0.18／0.22 m**
  （取樣點越過底邊時從「不計」變成 −L·tanθ）。唯一的跳變在從開放頂端走出去之後——那時已經離地、`ikAllowed` 為假。
- **一般地形**：root 不在代理面上時只有代理面命中計入 ⇒ 平地、斜坡、平台邊緣與現在逐位元相同；
  逐腳支撐需求（§3.5.4.2）只在這個模式下生效。在代理面上改由 Kref 負責——不再用碰撞矩陣判斷踏面。
  兩個模式的交界（樓梯頂／底）由樓梯契約 I6／I7 保證兩邊都 ≈ 0。
- **腳**：IK 目標仍是真實踏面（③ 不變）；比身體預期高的踏面由兩骨 IK 彎膝吸收，低的在包絡內搆得到。
- **L**：取樣半徑。X Bot 的推導：支撐腳離髖部最大水平距離 Walk 0.29 m／Run 0.23 m，再加上踏面可比移動面低一級（約半個踏面深度）⇒ 0.45。
  ⚠️ Codex：這是 X Bot 的量測，**不能直接套到 Enemy（Y Bot）**——做成每個角色的 `FootIKSettings` 欄位，Enemy 另行量測。
- **代價（玩家看得到）**：樓梯上身體固定下沉 ≈ 0.26 m（29.7°）～0.32 m（35.2°），接近 `MaxPelvisOffset` 0.35；支撐腿明顯彎膝。
  Codex 提醒這可能與 Natural Pose 優先衝突 ⇒ 需要人工看。
- 射線：每角色每帧 9 條（root ＋ 8），`Physics.Raycast` out 多載、遮罩快取，零配置。

##### ❓ 待使用者裁決：支撐貢獻是否進入身體高度

使用者的描述是「身體跟隨連續參考面，**支撐貢獻＋膝／腿伸展**負責踏面接觸」。修訂版把支撐貢獻只留在各腳的 IK 權重。
Codex：這個解讀在技術上一致，但**不是忠實的解讀**，需要使用者明確接受。Codex 提出的忠實版本是：

```
骨盆目標 ＝ Kref ＋ min_i( 支撐量_i × 真實腿長可達性不足_i )      （Kref 已經搆得到 ⇒ 支撐貢獻 0）
```

- 「真實可達性」需要 pre-IK 的髖關節位置與腿長。OnAnimatorIK 時骨骼 Transform 還是上一帧的（`FootIKPoseData` 的註解：直接取樣會形成反饋迴路），
  要從 `bodyPosition`／`bodyRotation` 加一個校準過的髖部偏移去估——M3.2 的 Reach Clamp 就是在這裡被移除的。
- 量測顯示在 L＝0.45 時這一項幾乎不會啟動（支撐腳搆不到 p95 ≤ 0.6 cm；只有 `Stairs (1)` Run 上樓 5.6 cm）。
- 若只用踏面高度近似（沒有腿長資訊），它會頻繁啟動並把階梯感帶回來（上表「F ＋ 支撐安全網」：Run 下樓 dv95 5.3–5.6）。

##### 驗收（提案）

- 自動：EditMode 純函數測試（Kref 的組合、延伸平面取 max、沒有命中＝不計入、模式交界）；
  PlayMode：Walk 樓梯髖骨 dv95 ≤ 平地 ×2、支撐腳浮起 p95 ≤ 1 cm、進出樓梯無跳變、**原地轉身與橫移時骨盆不變**、
  **Enemy（NavMesh、邊橫移邊面向目標）**、平地／一般地形逐位元相同。
- 人工：樓梯上的彎膝下沉幅度是否自然（L 可調）。

##### ✅ 實作與結果（2026-09-24）

**Codex 第二輪**：大致可行；2 項 Medium 已處理——
①root 支撐查詢的規格：起點 root 上方 `RaycastUpOffset`、往下到 root 下方 0.2 m、移動世界遮罩；沒命中 ⇒ 不在代理面上（刻意的語意邊界，寫進程式註解）。
②轉折處法線切換：Editor 以 **0.5 mm** 步距掃兩道樓梯底部轉折（中線與距側邊 15 cm），單步跳變 < 0.5 mm ⇒ 無實質跳變。
1 項 Low（8 方向的世界方位偏差：斜放的樓梯最多差約 8%／2.5 cm）⇒ 接受並記錄。Enemy 在量測前不啟用。

**程式**：`FootIKSettings.ReferenceSampleRadius`（0 ＝ 關閉，逐位元不變）；`FootIKController.ComputeReferenceOffset`（9 條 Raycast、遮罩在 Awake 快取、零配置），
純函數 `TryReferenceSampleHeight`／`ExtrapolateSupportPlaneY`／`CombinePelvisTarget`。X Bot ＝ 0.45（Editor API 寫入 prefab）；**Y Bot ＝ 0（未量測，維持 §3.5.4.2）**。

**測試**：EditMode 6 條（代理命中、一般地形不計入、懸崖被延伸平面蓋掉、樓梯底地面高於延伸平面、平面延伸與退化法線、模式組合）；
PlayMode `Player_BodyReference_IsIndependentOfFacing`（兩道樓梯，站在中段轉 0／45／90／180／270°，骨盆完全不變：−0.306／−0.350）。
全套：EditMode 665／664／1 既有略過；PlayMode 93／92（唯一失敗是既有的 C0）；樓梯 22／22。

**探針（Walk／Run × 兩道樓梯）**：

| | 骨盆殘差 前→後 | 髖骨 dv95 前→後（m/s） | 平地髖骨 dv95 |
|---|---|---|---|
| Walk 上樓 | 0.31–0.33 → **0.000** | 5.8–6.0 → **0.23** | 0.28 |
| Walk 下樓 | 0.31–0.34 → **0.000** | 4.8–7.3 → **0.22** | 0.28 |
| Run 上樓 | 0.29–0.39 → **0.000** | 5.5–7.4 → **0.57–0.58** | 1.16 |
| Run 下樓 | 0.21–0.22 → 0.02–0.06 | 5.1–5.2 → **0.56–0.58** | 1.16 |

支撐腳接觸：IK 權重 ≥ 0.9 的帧，腳底 − 踏面的分布與改前相同（主要是 `docs/44` §9.8 議題 3 的一帧 root 位移偏移），**沒有新的浮起**。

**⚠️ 玩家看得到的代價——需要人工判斷**：
- 骨盆在樓梯上中位數 **−0.335～−0.35 m**（碰到 `MaxPelvisOffset` 上限），比理論的 L·tanθ（0.26／0.32）更深。
  多出來的是 CharacterController 膠囊在斜面上讓 root 比移動面高出的距離（Walk 約 8–10 cm、Run 上樓 18–23 cm；膠囊底部球面接觸點不在 root 正下方）。
- 支撐腿髖→踝距離中位數從腿長的 **97% 降到 62–81%**（膝蓋明顯彎曲，p5 約 50–60%）。可能看起來像半蹲；`ReferenceSampleRadius` 是這個取捨的旋鈕（小 ⇒ 較直但腳較可能搆不到）。

#### 3.5.4.4 📝 研究：IK 貼地時機（2026-09-25，⛔ 只離線模擬，未實作；❓ 待使用者裁決）

> 🔎 外部架構校準（UE Foot Placement、AC Syndicate GDC 2016、Johansen 2009）：`docs/45`。結論：T 系列方向正確，但落腳端需改為「每步一個落點＋鎖腳」。
> 🧪 **T3 執行期原型已實作（`docs/45` §8）**：開關 `FootIKSettings.UseContactPrototype`（X Bot 開、Y Bot 關；關閉時逐位元不變），純函數在 `FootContactSolver.cs`。擺盪穿透 > 3 cm 27–48% → 1–5%，腳步對齊 Walk +8 → −1 帧；未解：停步外插、進鎖前跨階緣、腿過伸。⚠️ 未人工 Play 驗收。

**使用者要求**：優先處理 IK 貼地時機（腳步偵測器已在動畫最早的穩定落腳點，不再動）。
現行權重只看動畫腳高（`FootGroundedHeightMin/Max` 0.08／0.25 m），滑行中的低腳會被貼地。
提出並離線模擬一個**連續**的權重模型：高度貢獻 × 由既有動畫腳運動推導的落腳相位貢獻；不得是新的布林閘門或單一水平速度門檻。

**模擬器**：畫面腳位 ≈ lerp(FK 腳位, IK 目標, 權重)，FK ＝ 動畫腳位 ＋ 已套用骨盆；權重、目標、骨盆都取前一帧（它們在下一次動畫 pass 才生效）。
以實測權重重現畫面腳位：支撐期誤差中位數 1–3 mm（p95 1–2 cm），擺盪期較大（p95 約 10 cm）。以現行權重重現實測的觸地→擊發延遲（Walk 平地 +4／樓梯 +8 帧）。
**評估一律用真實踏面**（Editor raycast `StairTread`＋`Default`，在畫面腳位下方）——`FootIKGroundData.GroundY` 含腳跟／腳趾穿透抬升，
擺盪中前傾時可比真實地面高 10 cm，拿它當基準會系統性地錯（§9.9 的部分結論因此需要修正，見 `docs/44` §9.10）。

##### 模擬結果（兩道樓梯＋平地，Walk／Run，轉身、停步）

| 模型 | 樓梯擺盪腳穿透 p95（上／下） | 平地 Walk 滑行中被貼地 | 樓梯觸地→擊發（Walk 上／下） |
|---|---|---|---|
| M0 現行（權重＝高度） | 10–12／28–31 cm | 27.9 cm／步 | +8／+7 帧 |
| **M1 使用者要求：權重＝高度 × 相位**（相位＝1 − smoothstep(1, 4 m/s, 動畫腳水平速度)） | **16–36／29–31 cm（更糟）** | **2.3 cm／步** | +8／+8 帧 |
| T1 目標＝lerp(移動面, 踏面, β) ＋ 動畫離地高度；β＝高度 × 相位；權重＝1 | **0.3–1.9／0 cm** | 1.3 cm／步 | **+2／0 帧** |

- **M1 在平地達成了目標**（滑行中的低腳保留動畫姿勢，貼地距離降到 1/12），**但在樓梯上讓腳穿進台階更深**：
  身體被參考面包絡壓低約 0.35 m（§3.5.4.3），FK 腳位跟著下沉；權重一降，擺盪腳就沉進台階。
  **只調權重，在樓梯上無解**——權重高 ⇒ 滑行中貼地；權重低 ⇒ 穿進台階。
- **T1 改的是「腳相對於哪個面」**，不是權重：擺盪時腳維持動畫的離地高度、但相對**移動面**（樓梯上是高於所有踏面的斜面，所以不會穿透）；
  β 由高度 × 相位連續地把參考面從移動面換成真實踏面，腳在動畫落地時才貼上踏面。平地上移動面＝地面，目標＝動畫腳位本身（逐位元不變）。
  使用者要求的「高度貢獻 × 相位貢獻」保留，但用來決定**參考面**而不是 IK 權重。
- **T1 尚未乾淨**：目標每帧變化 p99 9–15 cm（`GroundY` 的抬升跳變 × β）；畫面腳的垂直加速度 p95 比現行好（樓梯 Walk 501 → 209、Run 335 → 176 m/s²），
  但 p99 仍和現行相當（636–838），遠高於純動畫（50–190）。下一步：β 的踏面端改用真實踏面（而非含抬升的 `GroundY`）、並檢查 β 的起始斜率。
- **轉身／停步**：M1 轉身時穿透更深（Walk p95 23 → 48 cm）；T1 轉身穿透 ≤ 2.6 cm、觸地→擊發 +6～+8 帧（現行 +8～+14）；停步三者都沒有穿透。
- **擺盪中途低點**（Stage 1 實測 WalkFwdLoop：0.044 m、4.81 m/s；解析計算，未另做合成重播）：水平速度 > 4 m/s ⇒ 相位項＝0 ⇒ M1／T1 都不會在中途低點貼地。

##### ⚠️ 觸地定義的限制

「畫面觸地」用「踝骨 − `FeetBottomHeight`」到真實地面 ≤ 1 cm。腳跟著地時腳掌上翹，這個估計會高估腳跟高度幾公分 ⇒ 畫面觸地被判得偏晚。
平地上現行 IK 的觸地→擊發，用真實地面量是 **−2 帧（聲音略早）**，用 `GroundY` 量是 +4 帧——兩者差在量測基準，不是行為。
要精確比較時序，需要腳跟／腳趾的真實位置（`FootIKController` 已經為 L1 雙點採樣算過，只是沒有輸出）。

#### 3.5.4.5 📝 研究：樓梯半蹲——包絡骨盆 vs 依可達性決定的骨盆（2026-09-25，⛔ 只離線模擬，未實作；❓ 待使用者裁決）

**起因**：T3（`docs/45` §8）開啟後人工 Play：跳動大致消失，但**樓梯上持續半蹲**。使用者裁決：這是骨盆／身體高度問題，不是 T3 腳部接觸問題。
候選方向（使用者提出）：保持動畫的名目身體高度，只做「支撐腿搆得到」所需的最小骨盆修正；可選：支撐腿過度壓縮時向上回復。

**模擬器**（`Temp/StairProbe/pelvis_sim.py`，用完即丟；資料 ＝ T3 開啟的 `StairFeelProbe`，兩道樓梯 × Walk／Run，上→轉身→下（中段停步））：
- T3 的 IK 目標與骨盆無關 ⇒ 所有模型面對同一組腳目標。髖關節 ＝ UpperLeg 骨骼，骨盆只垂直平移它。
- 在同一份資料上校準：腿長 L ＝ 0.889 m；Unity 腿 IK 在 |髖−目標| ≤ L 時精確到位，**Humanoid Leg Stretch 再延長 5%（0.9335 m）** 後腳才搆不到；
  用錄下的骨盆重播，髖—踝距離與骨骼一致（中位數誤差 0）。時序同執行期：第 N 帧算、第 N+1 帧套用，髖位一帧外插（同 T3）。
- 動畫本身的支撐腿伸展比（髖→pose goal ／ L）：平地與樓梯中位數都是 **0.975**（膝彎 Walk 約 25°、Run 約 42°）——這是「自然姿勢」的參照。

**模型**（每隻腳的支撐權重 ＝ 執行期既有的動畫腳高啟發式；骨盆夾在 [−0.35, 0]）：
- **包絡（現行）**：`ComputeReferenceOffset`，半徑 0.45。
- **NomCap（使用者的候選，字面版）**：目標 ＝ min(0, 硬性可達上限)；上限 ＝ min_i[w'_i · m_i]，m_i ＝ 讓第 i 隻腳剛好完全伸直的偏移，w'_i ＝ clamp01(1.25·w_i)。
- **J（Johansen 2009 §7 式）**：期望值 ＝ Σ w_i·d_i ／ max(Σw_i, 1)，d_i ＝ 讓第 i 隻腳保持**動畫伸展比**的偏移；再以同一個硬性上限封頂。
- 平滑：raw（不平滑）或臨界阻尼彈簧 ω＝6，外加接觸安全夾（不得比需求高出 1 cm，否則立即下降）。
- 水平距離已超過腿長的腳：延續到「垂直最近」的姿勢（連續；Codex 指出原本「直接 −0.35」會跳）。

##### 結果（樓梯上下 × 兩道樓梯，四段平均；bob ＝ 髖高度相對同段內 21 帧置中平均的偏差 p95）

| Walk | 骨盆中位數 | 支撐膝彎中位數 | 支撐腿伸展比 | 靠 Leg Stretch 的支撐帧 | 支撐腳搆不到 > 1 cm | 髖垂直加速度 p95 | bob p95 |
|---|---|---|---|---|---|---|---|
| 平地參照（包絡） | −2.5 cm | 25° | 0.98 | — | 0% | 17 m/s² | 2.9 cm |
| **包絡（現行）** | −34.2 cm | **85°** | 0.73 | 5% | 0% | **16** | **4.8 cm** |
| NomCap raw（字面候選） | −17.3 | **9°**（腿打直） | 1.00 | **23%** | 0% | 229 | 16.6 |
| J raw | −17.7 | **25°**（＝平地） | 0.98 | 16% | 0% | 224 | 15.9 |
| NomCap／J ＋彈簧 | −27.5 | 68–69° | 0.82–0.83 | 16–17% | 1% | 115–120 | 8.0–8.3 |
| 參考：2026-09-24 被否決的逐腳骨盆 | −16.4 | 25° | 0.98 | 21% | 4% | 335 | 14.3 |

| Run | 骨盆中位數 | 支撐膝彎 | 靠 Leg Stretch | 髖加速度 p95 | bob p95 |
|---|---|---|---|---|---|
| 平地參照 | −1.5 | 42° | — | 49 | 7.2 |
| **包絡（現行）** | −34.6 | **83°** | 4% | **35** | **7.1** |
| NomCap raw | −4.5 | **4°** | **45%** | 361 | 20.5 |
| J raw | −9.9 | 40° | 9% | 376 | 20.9 |
| J ＋彈簧 | −25.4 | 61° | 6% | 151 | 7.6 |
| 參考：被否決的逐腳骨盆 | −9.1 | 43° | 17% | 334 | 23.0 |

**結論**：
1. **包絡的深度 ≈ 每一步最壞時刻的可達性需求，一直維持著。** 需求本身在每一步內變化約 20 cm（J raw 的需求中位數 −7～−18 cm，p05 −27～−35 cm）：
   前腳踩上較低的踏面、或被鎖住的後腳落在髖部後方 0.24–0.31 m（垂直 1.09–1.16 m，而腿在這個水平距離只搆得到約 0.81 m）。半蹲不是包絡「沒理由地壓低」。
2. **使用者的候選照字面實作 ≈ 2026-09-24 已被否決的逐腳骨盆**（那個模型本來就是「腳下地面 vs root」的可達性需求）：
   每步骨盆起伏 16–20 cm、髖加速度是包絡的 7–13 倍；而且「名目高度」在樓梯上**不是**自然姿勢——支撐腿被拉直到 Leg Stretch（Walk 膝 9°、Run 4°，平地是 25°／42°）。
3. **向上回復在樓梯上沒有作用**：需求幾乎永遠是向下的。
4. J（保留動畫膝彎）在中位數上正好回到平地姿勢，但起伏與加速度同樣回來。
5. **任何「不犧牲接觸」的因果平滑**（彈簧＋1 cm 安全夾）只換回約 7 cm：Walk 膝 85° → 68°、Run 83° → 60°，
   代價是髖加速度 Walk 7 倍、Run 4–6 倍（bob 8 cm vs 包絡 5–7 cm）。放寬安全夾到 4 cm 能再高一點，但 8–17% 的支撐帧腳搆不到踏面——等於抵銷 T3 的接觸改善。
6. **root 離移動面的距離不改變絕對身體高度**（包絡與可達性模型都是絕對高度）；只在包絡被 −0.35 夾住時有影響（Run 上樓、`Stairs (1)` 86–92% 的帧）。
7. **要真的減少蹲，要從「需求」本身下手，不是骨盆**：
   - T3 釋放時機：被鎖住的後腳在髖部後方 0.55–0.7 m 才釋放（擺盪腳搆不到目標 11–75 cm，任何骨盆都救不了）；
   - 腳端可達性（腳跟先離地／腳尖先著地，對應 UE `heel_lift_ratio`）：粗估每公分腳端可達性換回約 0.7–0.9 cm 骨盆高度（模擬只提高了骨盆的假設腿長、沒有移動腳，屬上限估計）；
   - 適合樓梯的 locomotion clip：平地步幅落在樓梯上，每一步踩的深度都不同，是需求每步變化 20 cm 的根源。

**Codex 審查（骨盆公式）**：大方向成立（接觸安全的骨盆修正無法同時保留名目姿勢與包絡等級的平順）。指出三點並已修正後重跑：
①J 其實沒有排除「釋放中」的腳（在 10–19% 的樓梯帧由它決定上限）——改成明確的連續支撐語意；以 ReleaseBlend 淡出的版本不比較好，**不得用 `Phase != Releasing` 硬切**（會落入 snap-to-default）；
②J 不是字面的候選——另加 NomCap 並列，兩者是不同的姿勢政策，要由使用者選；③水平搆不到時「直接 −0.35」不連續——改為垂直最近姿勢。
執行期資料來源：髖位與腿長放進 `FootIKPoseData`（Rig 仍是唯一 Writer、只寫事實，不違反 A11），優先用 `bodyPosition ＋ bodyRotation × 由 avatar 量得的髖偏移`，除非探針證明 `OnAnimatorIK` 開頭的 UpperLeg 骨骼是本帧且未被污染。

> ✅ **裁決（2026-09-25）：(a)**——維持包絡骨盆，改從需求端下手。後續量測與 F1（凍結偏移的釋放）見 `docs/45` §9。

**❓ 待使用者裁決**（沒答案前不得改骨盆的執行期行為）：
- (a) 維持包絡，改從需求端下手（建議先做 T3 釋放時機，其次腳端可達性）；
- (b) 先接受折衷：J ＋ 彈簧（高約 7 cm、每步起伏約 8 cm、Walk 髖加速度約 7 倍），做成開關給人工 Play 判斷；
- (c) 字面候選（NomCap）——不建議：腿打直到 Leg Stretch，且起伏 ≈ 已否決的逐腳模型。

#### 3.5.4.6 停步站直：Early Recovery → Primary Support Rise → 尾腳前瞻上限 → Final Settle（2026-09-27 夜執行期原型；X Bot 開啟；⏳ 待人工 Play）

##### 現行合約（2026-09-27 夜，取代 support-leg＋quintic 與工作樹未記錄的 WT scalar-speed 實驗）

移動中的 Terrain Envelope E、T3 腳接觸／鎖腳語意、`FootstepDetector`、Issue 3、root／膠囊與 Y Bot 都不變；
全程不讀 FSM／animation／Locomotion Stop 狀態——只讀黑板移動意圖、root 運動學速度與 T3 自己的腳狀態。

| 段 | 現行定義 |
|---|---|
| reliable foot | T3 `Locked` ∧ β ≥ `LockBeta` ∧ pose 水平腳速 < `StandingReliableFootSpeedMax` 0.3 m/s ∧ live contact；無歷史／不連續帧不可靠 |
| **① EarlyRecovery**（`PosturePhase` 4） | 移動意圖 >0 → 0 的邊沿、且放開當下 root ≥ `StandingEarlyReleaseMinRootSpeed` 0.1 m/s ⇒ 進入（從 Moving 或仍在追隨包絡的 Return）。包絡路徑 `base` 原封不動繼續；抬升量 `a` 以 ω＝`StandingEarlyLiftOmega` 6 的臨界阻尼彈簧追 `w·max(0, U−base)`，`w＝clamp01(1−rootSpeed/rootSpeed@release)`；上限 `max(0, C−base)`，頂到時以 ≤`StandingEarlyLiftRetreatSpeed` 1 m/s 退讓，不瞬間下拉。移動意圖恢復 ⇒ Return（位置、速度承接） |
| primary edge | 沒有移動意圖、**新** reliable edge、root ≤ `StandingFinalSupportSpeedMax` 1.35 m/s ⇒ ②（從 Moving／EarlyRecovery／Return） |
| **② StopProgress**（Primary Support Rise） | 鎖存支撐腳 F、畫面上的支撐髖（pre-IK 髖＋當帧已套用 offset）、上下腿長；**承接速度** `L̇₀`＝畫面支撐髖相對 F 的徑向速度（帧 e−1→e）。支撐腿長 cubic Hermite 從 `(L₀, L̇₀)` 到 `(0.995·leg, 0)`，時長 `T＝min(D, 1.5·ΔL/L̇₀)`；D 依尾腳 T3 相位：`Unlocked∧β≥0.8∧下降` 0.18 s／`Locked∧live` 或 `Releasing∧β≥0.6∧下降` 0.20 s／其餘 0.40 s（`StandingRiseDuration*`）。逐帧以固定 F＋本帧（外插）primary 髖 XZ 反解骨盆，並 ≤ primary 全長可達。無單調保持、不從 0 速度重起 |
| **③ 尾腳前瞻可達上限** | 只在尾腳 T3 `Locked` 時：鎖定內第一個可靠帧鎖存它的目標（**每次鎖定只一次**，不逐帧更新、不讀即時射線），離開 `Locked` 即清除。`cap＝LegBound(尾髖, 鎖存點, 尾腿全長)`；單邊 C1 飽和 `SaturateBelow`：低於 `cap−m` 完全不動，以上指數逼近、永不超過；帶寬 `m＝min(StandingTrailingCapBand 3 cm, cap−骨盆)` 在第一次套用時鎖定 ⇒ 啟用帧輸出不變。上升途中上限消失 ⇒ 被壓住的差距以 ω＝`StandingTrailingCapReleaseOmega` 10 的彈簧從靜止收掉 |
| 擺盪尾腳 | `Releasing`／`Unlocked` ⇒ 不給任何預測上限（即時目標可在觸發後再跳一整階） |
| **④ Final Standing Settle** | 尾腳曾不可靠後的新 reliable edge ⇒ `TrailingSettled`；之後只做 `min(骨盆, C)`。伸展時間到且已落定 ⇒ `Upright`（既有：U 高於骨盆才以 ω6 小幅上調；≤ C） |
| Return | 移動意圖恢復、primary 失去可靠支撐、Upright 任一腳不可靠 ⇒ Return：清掉上限與抬升，位置與速度原樣承接，ω15 彈簧回 E。可達夾只管**高於包絡**的部分：`≤ max(C, E)`，頂到時以 ≤1 m/s 退讓（跑下樓時 C 會瞬間掉到包絡以下，照 C 夾曾單帧下拉 12 cm）。貼近 E 且近乎靜止才交回 Moving |
| invalidation | `dt≤0` 不推進；IK 失效、pose／腿長無效、瞬移、元件關閉或原型關閉一律 reset，回既有 E 路徑 |

**程式**：`StandingPostureSolver.cs`（`PostureTuning`／`PostureState`／`Step`）、`FootIKController.ResolveStandingPosture`（組 `PostureInputs`，含本帧未外插髖位）、`FootIKSettings`（新增 9 個欄位，C# 預設值；X Bot prefab 未改）。

##### 執行期驗證（2026-09-27 夜；`StairFeelProbe` 16 runs：兩道樓梯 × Walk／Run × early-cancel 0.30／0.45／0.60 s）

before＝工作樹 WT runtime（post-R8，同一 probe 序列）；after＝本節合約。畫面骨盆＝`hipsY`；視窗＝第一支撐 → 尾腳落定＋0.25 s。

| 停步 | 單帧最大（cm） | 峰值速度（m/s） | 峰值加速度（m/s²） | 落定修正（cm） | 落定時膝 primary／trailing | 尾腳 |
|---|---|---|---|---|---|---|
| proto_Walk 上 | 4.70 → **1.94** | 2.82 → 1.16 | 75 → 13 | −0.35 → −0.07 | 20°／23° | 擺盪 |
| proto_Walk 下 | 1.06 → 1.69 | 0.64 → 1.01 | 32 → 23 | −0.57 → **0.00** | 23°／25° | 已鎖（上限 17 帧） |
| s1_Walk 上 | 1.74 → 1.57 | 1.04 → 0.94 | 27 → 18 | −0.40 → **0.00** | 22°／25° | 已鎖（18 帧） |
| **s1_Walk 下（跨 18 cm 階）** | **13.60 → 2.11** | **8.16 → 1.27** | **526 → 39** | **−13.62 → +0.02** | 76°／21° | 已鎖（21 帧；啟用 Δv −0.11 m/s） |
| proto_Run 上 | 0.78 → 1.34 | 0.47 → 0.80 | 4 → 16 | +0.56 → −0.21 | **77°／78° → 16°／20°** | 擺盪 |
| proto_Run 下 | 2.26 → **0.84** | 1.36 → 0.50 | 43 → 13 | −0.53 → −0.41 | 19°／18° | 擺盪 |
| s1_Run 上 | 3.96 → **2.06** | 2.38 → 1.24 | 68 → 10 | −0.79 → −0.34 | 18°／18° | 擺盪 |
| s1_Run 下 | 1.24 → 1.23 | 0.74 → 0.74 | 33 → 19 | +0.35 → +0.35 | 22°／23° | 擺盪 |

- **沒有 13–17 cm 落定修正**：全部 ≤0.41 cm。尾腳落定時都搆得到（離全長 −1.1～−2.3 cm）。s1_Walk 下 primary 膝 76°＝跨階站姿本身（上階那隻腿必須彎），不是半蹲殘留。
- WT 在 proto_Run 上樓其實**沒有站起來**（落定時膝 77°、尾腳離全長 −20 cm，之後 0.5 s 才慢慢升）；新鏈 0.18 s 內站直。WT 的第一支撐帧速度跳動（proto_Walk 上 0.6→2.8 m/s）與末端硬停都消失。
- 尾腳仍在擺盪、因此**沒有預測上限**的：全部 Run 停步、proto_Walk 上／quick——鎖存只在落定帧出現，交給 ④，落定修正 −0.41～+0.35 cm。
- rise-then-drop：proto_Run 上樓 0→1——站直後 Upright 跟著 C 以 ≤0.3 cm／帧下修 2.6 cm（④ 的最小修正），不是尾腳 bob；其餘不增加。
- **取消**（24 early-cancel ＋ 4 quick-stop）：取消前已抬升 0.30 s ≤2.3 cm、0.45 s ≤7.7 cm、0.60 s ≤10.4 cm（Run 上樓），與離線預估一致；取消後 offset 單帧 ≤1.8 cm。
  quick-stop（第一支撐後 0.27 s 重按）因為這次**真的站直了**，Return 從直立降回跑步包絡：峰值 1.9 m/s（3.1 cm／帧），臨界阻尼、無跳動（WT 在同情境從未站起，所以沒有這段）。
- **初版執行期發現並修正**：WT 殘留的 Return 以 C 硬夾——跑下樓時 C 可瞬間低於包絡（−0.45 vs −0.31），early-cancel 下樓因此單帧下拉 12 cm。改為 `≤ max(C,E)`＋有限退讓後 0.82 cm／帧（與 before 相同）。
- **回歸**：T3 鎖腳相位全部帧一致；T3 目標與腳步事件只在 probe 瞬移回起點的那幾帧（`ecwarm`）不同，其餘 0 差異；root 差 ≤0.01 cm。
- **測試**：`StandingPostureSolverTests` 25 條（重寫）；EditMode 718／717／0 failed／1 skipped；完整 PlayMode 116／107／1 failed（既有 `C0_…IncidentalContact`）／8 skipped（probe 設計）。

##### 支撐腿 candidate 離線 gate（2026-09-27）

在改 runtime 前，以同一批 8 次 stop trace 重播固定 primary foot＋live primary hip 的幾何 candidate（
`Temp/StairProbe/support_leg_candidate_sim.py`）。另比較過「完全鎖死 entry hip」與「膝角插值」兩個版本：前者會因 root／動畫髖繼續移動而產生 reach error／錯誤末端膝角，
後者把峰值加速度推到 73.4 m/s²；兩者都淘汰。採用版本只鎖 entry 事實與 support foot，逐幀用 primary hip XZ 反解垂直高度。

- 相對舊 E→U，offset 單帧峰值依案例為 1.99–4.32 cm（舊 1.59–4.12）、速度 1.20–2.59 m/s、加速度 14.0–54.3 m/s²；**數值不保證全面變小**，這次目的不是偷偷調 duration／curve，而是把可見位移與單支撐腿伸展建立正確因果。
- 支撐膝在 primary→final 間連續由約 64–105° 伸到約 8–18°（離線），latched-primary reach error 0；尾腳最大 reach error 0.58 cm；預估 final join 最小修正 0–0.73 cm。
- 7／8 在 0.15–0.37 s 進入 target leg length ±0.5 cm；`proto_Walk/upstop` final 約 18°，未進嚴格 0.5 cm 門檻，但已接近 Idle 膝角，留給 Play 判斷，不用調 curve 掩蓋。

##### 執行期驗證（2026-09-27 support-leg candidate）

`StairFeelProbe.Probe_UpTurnDown` 保留原本兩道樓梯 × Walk／Run × T3／T3Posture 的 **8 個情境**；原本的上／下樓長停完整跑完後，
每個情境再回到樓梯中段做 quick-stop。T3Posture 的四個 restart 分配為 primary 後約 0.10／0.25／0.50 s；這樣不會讓新增案例改變原 8 個 baseline 的 gait phase／落腳幾何。

- 8／8 stair probe 通過；`StandingPostureSolverTests` **20／20**；完整 EditMode **709／708 passed／0 failed／1 skipped**。
- primary → final 仍是 11–25 帧。實際 pelvis world-Y 在 primary 後 **0.017–0.067 s** 已上升 >0.5 cm；即使 offset 暫被 monotonic hold，動畫髖＋固定支撐腿仍連續伸展，不會多等半蹲。
- latched support foot 的 XYZ drift **0**、primary switch 0；支撐膝由 64–106° 連續伸到 **1.6–19.5°**；latched-primary reach error ≤0.02 cm、尾腳 ≤0.32 cm。
- final plant 不跳 U：4／8 高度原樣保留，4／8 只做 two-foot C 所需的 **0.16／0.48／0.68／0.72 cm** 向下殘差修正。StopProgress 期間沒有重複 >1 cm rise/drop，可靠腳水平 slide 0。
- offset 峰值 **2.59 m/s／54.4 m/s²**；實際 pelvis world-Y 峰值 **2.65 m/s／52.6 m/s²**。這不是調參改善，Play 必須確認「膝伸直帶動身體」的因果是否已消除 teleport 感。
- quick restart 仍直接進既有 Return、位置不重設；純函數測試逐值守住承接速度。moving Terrain Envelope、T3、FootstepDetector、Issue 3、root／膠囊均未改。

本 slice **不調** 0.18／0.20／0.40、quintic、spring 或 clamp。Play 先只判斷幾何因果；若仍不對，回到支撐腿軌跡本身，不得退回 E→U 或「兩腳都落定後才開始升」。

##### 速度承接比較（2026-09-27 晚；⛔ 只量測，未改 runtime）

**問題**：使用者要求進入恢復時骨盆保留既有速度（C0／C1 連續、不 ease-out、不從零重新規劃），並以「維持約既有停步速度」為驗收條件。
改 runtime 前，在同一批 8 次正式停步上比較五個模型；一律量**畫面上的** primary 髖（帧 i+1 的 pre-IK UpperLeg ＋ 帧 i 的 output）。
OLD＝失敗的 E→U trace；SLQ＝上方 support-leg＋quintic（已記錄）；WT＝工作樹中**未記錄**的 scalar-speed MoveTowards 實驗（見 WORKLOG）；
C1＝固定支撐腳、腿長以 cubic Hermite 從「本帧顯示腿長／顯示的腿伸展速度」走到 0.995·leg、末速 0、無 monotonic hold；C2＝同 C1 但 quintic（起始加速度 0）。
重播腳本 `velocity_carry_sim.py` 為 throwaway（原 `Temp/StairProbe/*` 已被 Unity 重啟清空，trace 以 `StairFeelProbe` 重跑重建，entry 帧與舊 trace 逐帧一致）。

| 量 | 結果（8 次） |
|---|---|
| 進入時的骨盆速度 | 上樓 +0.07／+0.41／+0.86／+1.05 m/s；下樓 −0.25～−0.53 m/s（身體仍在落到支撐腿上）。**全部來自動畫髖**：進入前 envelope offset 速度 = 0 |
| 進入時的腿伸展速度 | −0.55～+0.45 m/s；需要的平均伸展速度（到尾腳落定）0.33–1.12 m/s ⇒ **只靠既有速度在任何一案都到不了站直** |
| OLD 進入首帧 Δv | ≈0（−0.04～+0.10 m/s）——失敗 trace **沒有**在進入時重設或減速；問題在其後加速到峰值 1.0–2.6 m/s、1.7–4.3 cm／帧 |
| SLQ | 首帧同 OLD，但下樓 4 案次帧被 monotonic hold 反轉 +0.31～+0.62 m/s；上樓 s1_Walk 先掉 0.41→0.30 再衝 2.7 m/s |
| WT（工作樹） | 進入單帧跳 **+0.43～+1.81 m/s**、下樓由降轉升；2／8 尾腳落定時仍差 11.2／6.2 cm（膝 59°／45°）、join 1.56 cm；末端硬停 ⇒ **淘汰** |
| C1 | 上樓進入 Δv ≤0.07 m/s；下樓 +0.19～+0.39（＝一帧的起始加速度，非重設）；峰值 0.92–2.12 m/s、1.54–3.53 cm／帧，8／8 都比 OLD 低 9–23%；支撐膝 63–106°→11–18°；join ≤0.35 cm |
| C2 | 進入 Δv ≤0.12 m/s（最連續）；但峰值 1.21–2.58 m/s、2.0–4.3 cm／帧，約同 OLD ⇒ C1 的峰值下降主要來自曲線形狀，不是速度承接本身 |

**真正的槓桿是截止時間預測**：3／8（兩道 Walk 停步與 proto_Walk 下樓）D bucket 取 0.20 s，但尾腳實際 0.383 s 才落定 ⇒ 0.2 s 就衝到站直再乾等。
以真實窗口當 T（oracle，runtime 拿不到）重跑 C1：這三案 1.54–3.53 → **0.98–1.89 cm／帧**、峰值 → 0.59–1.14 m/s。
另兩案（proto_Run 上樓、s1_Run 下樓）窗口確實只有 0.183 s：即使 oracle 仍需 2.7–2.9 cm／帧、1.6–1.7 m/s——這是「尾腳落定時已站直」這個要求下的平滑下限。

**結論**：「高速但連續」可以做到（C1／C2 皆無重設），但「維持約既有速度」這個驗收條件與資料衝突——恢復必須在 0.18–0.42 s 內**加速**到既有速度的數倍。
可見的逐帧大位移主要由 D 預測錯誤（過短）造成，不是速度重設。❓ 待使用者：①採用 C1（或 C2）取代 WT；②D 的預測規則（Locked＋live 尾腳目前給 0.20 s，實測仍會再走一步）；③0.183 s 窗口是否允許在尾腳落定後稍晚完成。

##### 停步前的動量來源：從放開按鍵往回追（2026-09-27 晚；⛔ 只量測）

**問題**：使用者觀察到骨盆在最後支撐之前就先變慢。從放開按鍵追到 Idle，把畫面骨盆 Y 拆成
`rootY`（膠囊沿樓梯）＋ `hipsLocalY − 已套用 offset`（動畫髖相對 root）＋ 已套用 IK offset，逐帧看速度。
資料：`StairFeelProbe` 8 次正式停步（raw trace 重建，見上節）；腳本 throwaway。

| | proto_Run 上 | proto_Run 下 | proto_Walk 上 | proto_Walk 下 | s1_Run 上 | s1_Run 下 | s1_Walk 上 | s1_Walk 下 |
|---|---|---|---|---|---|---|---|---|
| 停步前畫面垂直速度（m/s） | +2.69 | −2.19 | +1.37 | −1.32 | +2.96 | −2.15 | +1.63 | −1.63 |
| **root 停滯**（放開後 s；速度變化） | 0.033；+1.75→−0.90 | 0.033；−2.61→+1.12 | 0.317；+1.69→+0.16 | 0.200；−0.90→−0.11 | 0.033；+4.36→+0.54 | 0.033；−2.99→−0.26 | 0.233；+2.00→+0.01 | 0.050；−1.26→−0.05 |
| 第一支撐（s）／當時 root 剩停步前的 % | 0.800／9% | 0.550／30% | 0.567／56% | 0.750／20% | 0.550／25% | 0.800／13% | 0.783／20% | 0.600／21% |
| 放開→第一支撐：root／動畫髖／offset（cm） | +99.6／+0.8／+1.6 | −69.8／+8.2／−9.1 | +59.8／−3.0／0 | −62.5／+5.6／0 | +83.5／+3.1／0 | −128.8／+0.7／0 | +82.7／+4.4／0 | −53.1／+1.8／0 |
| 尾腳落定／FSM Idle（s） | 0.983／1.233 | 0.967／1.000 | 0.917／1.333 | 1.133／1.367 | 0.967／1.000 | 0.983／1.233 | 1.167／1.400 | 0.983／1.217 |

**第一個實質偏離帧＝root 停滯帧**：Run 放開後 2 帧；Walk 等 Pending Stop 到入場相位（0.05–0.32 s），停滯前畫面速度仍跟著停步前的步態。
停滯本身是**正好 2 帧 root 位移 0、第 3 帧約半速**，之後才是停步曲線的線性減速。全部 probe 的 36 個 locomotion stop 入口
（上／下樓、`settle`、`quickstop`；Walk／Run；T3 與 T3Posture）**無一例外**，與 Foot IK 無關。

五個假設逐一判定：

1. **動畫髖自己變慢**——部分成立、屬暫態：Run 停步 clip 在放開後約 0.17 s 有 −1.8～−2.0 m/s 的煞車下沉（proto／s1 Run 上樓畫面速度因此再掉到 0.2 m/s）；
   但放開→第一支撐的淨變化只有 −3～+8 cm，不是主因。Walk ≤0.6 m/s。
2. **包絡目標不再跟坡度**——不成立：6／8 的 E 整段釘在下限（−0.35／−0.335）不動；E 本來就沒有「跟坡度速度」這回事。
3. **offset 抵消動畫剩餘上升**——不成立：第一支撐前 offset 速度 6／8 為 0；proto_Run／s1_Run 下樓只有 ≤0.54 m/s 的同向（向下）變化。
4. **恢復太晚、繼承已慢的骨盆**——成立：第一支撐在放開後 0.55–0.80 s，root 只剩停步前的 9–56%；這段畫面垂直速度 ＝ 坡度（0.57–0.70，停步前後不變）× root 水平速度。
5. **停步轉場改了身體高度基準**——高度基準不成立（坡度比不變、E 不變、FSM 到尾腳落定後才進 Idle）；**但轉場本身造成 2 帧 root 停滯**——這是 Motion 層缺陷，見 `docs/07` §13.1 新增條目。

**結論**：骨盆動量確實在 StopProgress 之前就流失，來源依序是 ①Locomotion Stop 入場的 2 帧 root 停滯（可見頓挫，缺陷）②停步曲線的 root 減速 × 樓梯坡度（authored，正確）③Run 停步 clip 的煞車下沉（authored，暫態）。
Foot IK（包絡／offset）在第一支撐前完全沒有參與減速。要讓骨盆「穿過最後支撐步仍保有垂直運動」，
在現行架構下只能由 offset 在 root 減速期間（第一支撐之前）就開始補償——那是新的姿勢政策，❓ 待使用者決定是否研究。

> ✅ 2026-09-27 更新：①root 停滯（`docs/07` R8）已修並重量——36→0 個停滯入口，第一支撐前畫面骨盆不再有 0.03–0.32 s 的速度崩落；
> root 仍依停步曲線在第一支撐時只剩 9–56%。②下方「Early Stop Recovery」為使用者要求的後續量測。

##### Early Stop Recovery 離線量測（2026-09-27；⛔ 只量測，未改 runtime）

**使用者裁決的三段責任**：①Early Stop Recovery——確認是真的停步後，在 root 還在減速時就開始釋放深的移動包絡 offset；
②Primary Support Recovery——第一個可靠終端支撐腳建立後交給支撐腿伸展模型；③Final Standing Settle——尾腳落定只做剩餘的雙腳修正。
Motion 層的停步減速與姿勢恢復維持分開：①**只用 Foot IK 自己擁有的訊號**（黑板移動意圖＝0 的邊沿、root 運動學速度），不讀 Locomotion Stop 狀態。

**重播模型**（post-R8 trace；8 次正式停步＋8 次 quick-stop；三段共用同一個 ②C1 支撐腿模型與 ③最小 C 修正，只換 ① 的政策）：

| 項 | 定義 |
|---|---|
| 停步進度 | `w = clamp01(1 − rootH / rootH_release)`——運動學，Walk Pending 維持速度期間自然為 0 |
| 目標 | 抬升量 `a` 疊在**原封不動的**移動包絡路徑上：`a_target = w · max(0, U − base)`，U＝既有 upright bound（R／C） |
| 上限 | `a ≤ max(0, C − base)`（C＝既有支撐加權硬可達）；頂到時以 ≤1 m/s 退讓（soft），對照組硬夾（hard） |
| 動態 | 臨界阻尼 ω＝6 或 10；取消（意圖恢復）⇒ 既有 Return |

| 風險／效益 | B0（只在第一支撐才開始） | Early lift（soft ω6／ω10） |
|---|---|---|
| **腳搆不到**（Locked＋live 的腳） | — | **12／12 與 B0 相同**（抬升被 C 以下的餘裕封頂） |
| **重複起落** | — | 只有 Run 下樓：頂到快速下降的 C 時退讓 1.15–1.67 cm／帧，多 1 次 rise-then-drop（s1_Run 下樓；ω10 另含 s1_Run 上樓） |
| **取消的停步提早站起** | — | 放開後 0.2 s 內抬升 ≈0 cm；0.3 s ≤1.2 cm（ω10 s1_Run 上樓 4.3）；0.4 s ≤8.3 cm。第一支撐之後才取消的 quick-stop 兩者相同。⚠️ **0.3 s～第一支撐之間取消** probe 沒有覆蓋 |
| **交給第一支撐時的不連續** | Δv +0.05～+0.46 m/s | ω6 ≤0.18、ω10 ≤0.15 m/s |
| 第一支撐時已抬升 | 0 | Run 上樓 17–23 cm（proto）／6.7–8.8（s1）；s1_Walk 上樓 8.6–13.1；Run 下樓 3.5–6.8；Walk ≤5.3 |
| 支撐窗內峰值（proto_Run 上樓） | 2.35 m/s、3.91 cm／帧 | 0.82／0.48 m/s、1.36／0.80 cm／帧 |
| 支撐窗內峰值（s1_Walk 上樓） | 1.64 m/s、2.73 cm／帧 | 0.98／0.75 m/s、1.64／1.25 cm／帧 |
| 支撐窗內峰值（Run quick-stop） | 2.42／2.90 m/s | 0.87–0.52／1.40–1.03 m/s |

上樓的畫面骨盆從「先減速到 0.1 m/s、第一支撐後再衝到 2.35 m/s」變成減速期間保持 1.5–2 m/s、支撐後只剩 0.45–0.8 m/s 的小尾巴——正是使用者描述的「帶著既有垂直運動穿過最後支撐步」。
proto_Walk 上樓幾乎沒有效果（第一支撐時 root 還有 56%，w 小）。

**另一個獨立發現（與 Early lift 無關）**：R8 修正後 s1_Walk 下樓多走約 22 cm，最終站姿**跨在台階邊緣**（兩腳目標高差 18.3 cm，修前 0.3 cm）。
只看 primary 支撐腿的軌跡（C1／SLQ／工作樹 WT 都一樣）先把骨盆抬到 −0.07，尾腳落定時雙腳 C＝−0.24 ⇒ **單帧下修 13.6–17 cm**（實錄 WT runtime 亦同）。
「尾腳落定前不得重定義主軌跡、落定只做最小修正」在跨階站姿下不成立——②Primary Support Recovery 需要處理，❓ 待設計。

> ✅ 使用者裁決（2026-09-27）：R8 維持 `bb9b6f2`；烘焙器 (0,0) 不全域改；Early lift 暫用 soft ω6（不定案 tuning）；補「放開後 ~0.3 s～第一支撐之間取消」的 probe；
> **跨階站姿**：primary 支撐腿＝骨盆上升的唯一驅動者，尾腳＝**前瞻可達上限**（只封頂、不牽引）：`FinalCandidate = min(PrimarySupportDesired, TrailingFootReachCap)`。

##### 尾腳前瞻可達上限（Trailing reach cap）離線量測（2026-09-27；⛔ 只量測，未改 runtime）

**尾腳落點何時「夠可靠」——從既有 T3 狀態判定**（post-R8 trace，8 正式＋4 quick-stop 有 primary 的停步）：

| 類型 | 停步 | 第一支撐時尾腳的 T3 狀態 | 結論 |
|---|---|---|---|
| **L：尾腳已鎖** | proto_Walk 下、s1_Walk 上／下／quick | 全程 `Locked`；XZ 漂移 0；只是動畫腳滾動／抬起而暫時「不可靠」（hSpd ≤0.87、β ≥0.77） | **從第一支撐起可用**：T3 已承諾落點。以「本次鎖定中最後可靠的那一帧」鎖存目標，與最終落點差 0.8–1.9 cm |
| **S：尾腳擺盪** | 全部 Run、proto_Walk 上／quick | `Releasing`→`Unlocked`，到落定才 `Locked`（0.18–0.42 s） | 擺盪中的即時目標**不可用**：`β≥0.8∧下降` 在落定前 0.08–0.18 s 觸發、多半差 1.8–3.5 cm，但 proto_Walk quick **觸發後目標又跳一整階（18.9 cm）**——腳跨過階緣。⇒ 擺盪腳不給上限 |

`s1_Walk/stop` 的尾腳是 L 類：即時目標 Y 隨抬腳漂 +6 cm、且有一帧射線打到上一階 +17 cm——**正是不能讓即時射線牽引骨盆的原因**；鎖存值不受影響。

**規則（重播版本）**：
- 鎖存：尾腳 T3 `Locked` 且本帧可靠 ⇒ 若本次鎖定尚未鎖存，記下目標（**每次鎖定只鎖存一次**；runtime 對應 `FootLockState.LockedSupportY` 在鎖定內為常數）。離開 `Locked` ⇒ 清除。
- 上限：`cap = latch.y + sqrt(L_trail² − h²) − trailingHip.y`（L＝尾腿全長，h＝尾髖到落點水平距離）——顯示高度只隨 h 平滑變化，**不帶動畫髖 bob**。
- 套用：**單邊 C1 飽和**——低於 `cap − m` 原樣不動；以上 `cap − m·exp(−x/m)`，永不超過 cap。`m = min(3 cm, cap − 骨盆)` **在啟用當帧鎖定** ⇒ 啟用帧輸出不變。
- 持續到 Final Standing Settle 之後（落定帧不換鎖存值）；取消（Return／reset）即清除——cap 只會往下限，移除不會造成上跳。

| 驗收項 | `s1_Walk/stop`（跨 18 cm） | 其他 11 次 |
|---|---|---|
| 落定帧修正 | **−17.37 → +0.01 cm** | L 類 −0.53～−0.16 → −0.02～0.00 cm；S 類與無 cap **逐帧相同**（上限從未提前啟用） |
| 尾腳在落定時搆得到 | 搆得到（餘裕 0.03 cm；無 cap 時擺盪期間最多**搆不到 18 cm** 達 0.3 s） | 全部搆得到 |
| 啟用時不連續 | Δv −0.11 m/s | ≤0.20 m/s；最大 s1_Walk quick −0.53（仍在無 cap 支撐上升本身 ≤0.70 m/s 的逐帧範圍內） |
| 支撐上升連續 | 升到上限前與無 cap 相同；單帧最大 17.35 → 2.06 cm | 未改變 |
| 尾腳造成的逐步 bob | rise-then-drop 1 → 0 | L 類 1 → 0；S 類不變 |

被淘汰的版本（都量過）：hard `min`（啟用 Δv −1.14 m/s）；對稱 smooth-min（在帶內也會往下拉 ⇒ S 類落定帧多 −0.5 m/s）；落定帧才放開上限（上跳 +0.43～+0.88 cm）；每帧重新鎖存（鎖存值更新 ⇒ 落定帧上跳）。

**Early-cancel probe（新增）**：`StairFeelProbe` 在既有序列後瞬移回起點，上樓剛進樓梯即放開、下樓剛起步即放開，放開後 0.30／0.45／0.60 s 重按（參數 `earlyCancelDelay` 18／27／36 帧；18 的那次沿用既有檔名，所以既有 baseline 不變）。24 個取消窗口，重播 B0 vs Early lift ω6 soft＋trailing cap：

- 取消前已抬升：0.30 s ≤1.9 cm；0.45 s ≤7.0 cm（proto_Run 上樓）；0.60 s ≤10.4 cm（Run 上樓）。24 個中有 6 個在取消前已進入 primary。
- 重按後的下降速度與腳搆不到：**24／24 與 B0 相同**（3–7.6 cm／帧的大值是角色重新跑下樓的 root 運動，B0 同值）。
- 取消後頭幾帧 offset 上升（lift 的速度承接進 Return）≤0.85 cm；有 primary 的 6 個與 B0 差 ≤0.12 cm ⇒ **cap 釋放無跳動**。
- ❓ 0.45–0.60 s 取消的 Run 上樓在取消前已抬 7–10 cm：是否可接受是手感判斷，需要人工 Play。

##### 可見跳動診斷（2026-09-27；只加 trace，未改行為）

人工 Play 錄影確認站直段有肉眼可見的逐帧跳升，因此先凍結 duration／quintic／spring 與支撐政策，對上述 8 次正式樓梯停步逐帧記錄
`actual Hips Y`、`PelvisOffsetY`、StopProgress candidate、E／U／C、q、primary、支撐腳世界位置／髖踝距離／膝角、尾腳 T3 phase／live／reliable，
以及 phase edge、hard clamp、monotonic hold、spring、velocity reset。原始 trace 由 `StairFeelProbe` 輸出，濃縮／圖表由
`Temp/StairProbe/diagnose_stop_discontinuity.py` 產生。

**結論：主因在 StopProgress target trajectory，不是下游 solver 的額外不連續。**

- 8 次 primary→trailing plant 共找到 **67 個 ≥1 cm 的單帧骨盆位移**；每一個都與 candidate 的同帧位移完全相等（最大誤差 0），單帧峰值 **1.59–4.12 cm**。
  StopProgress 這段沒有跑 spring，而是直接以 `max(previous,candidate)` 寫輸出；因此連續的 quintic 在 60 Hz 取樣後仍可形成視覺上的大步幅。
- 這 67 帧中 hard-C clamp／spring／velocity reset／monotonic hold 都是 **0**；primary support 改選也是 **0**。所以不是「spring 算完又被 clamp 取代」、不是 phase transition 重設速度，也不是支撐腳切換。
- U／C 確實可因雙腳幾何變動在 StopProgress 中跳動（最壞 U 14.91 cm、C 6.27 cm），但 **trailing final join 當帧**的 U／C 變化僅 0–0.53 cm、輸出變化 ≤0.36 cm，並不是錄影中主要跳升。
  `s1_Run/upstop` 的 frame 8163→8164 另有明確單帧事件：U −0.1623→−0.0708（+9.15 cm）、C +2.01 cm；當時 primary R 仍 Locked＋reliable，
  trailing L 仍 Unlocked＋unreliable，但 L target Y 3.1427→3.2676（+12.49 cm），使 controlling preferred bound 從 trailing L 的 −0.1623 切到 primary R 的 −0.0709。
  這是 moving trailing target 經 live two-foot R 污染 U，不是 primary 幾何或 phase edge；舊 E→U candidate 因而同帧 +1.68 cm。新 support-leg candidate 完全不讀該 U，所以此問題已隔離但保留 trace 供後續獨立修正。
- primary 的 T3 target 水平位置全程固定（XZ drift 0）；Y 因既有接觸高度求解仍有 0.08–0.98 cm 漂移。支撐腿髖踝距離 0.54–0.75 m → 0.88–0.89 m、膝 64–106° → 0–16.2°，證實跳升發生在腿伸展過程，不是腳水平滑動。
- 時序是固定的一帧管線延遲：本帧 LateUpdate 的 solver output 在下一幀 OnAnimatorIK 套用，8 案對齊誤差 ≤0.005 cm（CSV 四／五位小數量化）；沒有一帧讀舊腳、下一帧突然改讀新腳的交錯。

此診斷直接導向上面的現行 support-leg extension：candidate 的幾何來源已更換；duration／curve／spring 未調。

##### 歷史基線（2026-09-25 Settling 原型，已被 StopProgress 取代）

**使用者要求**：移動中維持參考面包絡（不動）；**停下來站在樓梯上時**，身體往直立回復。⛔ 不是把 `PelvisOffsetY` lerp 回 0——
先算「目前支撐的兩隻腳都還搆得到」的最高骨盆，必要時才讓非必要的鎖腳重新落定。

**資料**：`StairFeelProbe` 加上樓梯中段 2.5 s 的停步（上樓、下樓各一次），兩道樓梯 × Walk／Run，T3 開啟；分析 `Temp/StairProbe/stop_sim.py`（用完即丟）。幾何校準同 §3.5.4.5。

##### 停步後的運動學（8 次停步都一樣）

- 放開按鍵後，locomotion 停步動畫還在走：root 速度 0.9–1.2 s 後才歸零；兩隻動畫腳 < 0.2 m/s、T3 兩腳都鎖定約在 1.0 s；FSM 變 Idle 在 1.0–1.4 s。
- 待機的兩隻腳是左右並排 ⇒ **8 次都踩在同一階**。
- 平地待機本身就是腿伸直（伸展比 1.004、膝約 17°）⇒ 「自然站姿」＝ 伸展比約 1.0；目標取 0.995 留一點餘裕。

##### 規則（Codex 審查後的版本）

所有訊號都是 Foot IK 自己的運動學或黑板資料，**不讀 FSM 狀態名稱**：root 水平速度（transform 位移／dt）、兩隻動畫腳的水平速度（pose 快照，T3 已在算）、
`ikAllowed`、T3 **本帧**的即時接觸、`MovementIntent.DesiredSpeedNormalized`（黑板，唯讀；Codex：Presentation 讀黑板不違反 LayerRules）。

| 符號 | 定義 |
|---|---|
| E | 既有參考面包絡目標（`ComputeReferenceOffset`，不動） |
| R | 偏好上限：兩隻腳都在伸展比 0.995 搆到**本帧** T3 目標的最高骨盆偏移（連續的不可行處理，同 §3.5.4.5） |
| C | 硬性上限：支撐加權（w′＝clamp01(1.25·支撐量)）、伸展比 1.0 的可達性需求——**唯一**允許立即下降的安全夾 |

| 狀態 | 動態 |
|---|---|
| **Moving**（預設） | 與今天完全相同：目標 E、MoveTowards 5 m/s |
| Moving → **Settling** | `ikAllowed` ∧ 移動意圖 ＝ 0 ∧ root < 0.05 m/s ∧ 兩隻動畫腳 < 0.2 m/s ∧ 兩腳都有即時接觸，**連續 6 帧** |
| **Settling／Upright** | 目標 T ＝ min(max(E, R), C)（**永遠不低於包絡** ⇒ 平地不變）；臨界阻尼彈簧 ω＝6；p ≤ C（夾住時向外的速度歸零）。\|p−T\| < 1 cm ⇒ 標記 Upright（動態相同） |
| → **Return** | 移動意圖 ≠ 0、root ≥ 0.05、任一動畫腳 ≥ 0.2、失去即時接觸 ⇒ 立即；彈簧 ω＝15 回 E，p ≤ C、p ≥ E |
| Return → Moving | \|p−E\| < 1 cm 且 \|v\| < 0.05 m/s ⇒ 交回 MoveTowards（今天的路徑） |
| 重設 | `!ikAllowed` ⇒ 既有行為（目標 0）；pose 未就緒 ⇒ 既有的提早 return；瞬移 ⇒ 歷史與彈簧重設；原型關閉 ⇒ 既有骨盆區塊逐位元不變 |

- 一個姿勢速度跨 Settling／Upright／Return 延續；進入 Settling 時以上一帧實際套用的位移作種子；`dt ≤ 0` 不推進計數器與彈簧。
- **骨盆必須在 T3 解完本帧兩腳之後才算**（Codex High）：今天骨盆在 ②、T3 在 ③'；原型開啟時要改成先解腳、再用「本帧的目標＋本帧即時接觸」算 R／C，
  讓同一帧寫出的腳目標與骨盆在下一帧一起套用（離線重播就是這樣配對的）。
- 鎖腳重新落定（v2，**預設關**）：只有 1／8 次停步需要（`Stairs (1)` Walk 下樓：一個鎖腳在休息腳位 9 cm 外、落在下一階；鎖住時上限 −22.1 cm、重新落定後 −3.6 cm）。
  若要做，必須走 T3 求解器既有的連續釋放／重鎖，不得直接改相位。

##### 離線重播結果（8 次樓梯停步；Return 用 ω＝15，含移動意圖閘門）

| 指標 | 結果 |
|---|---|
| 放開按鍵 → 站直 | **1.65–2.17 s**（偵測到靜止 0.95–1.30 s ＝ 停步動畫；上升 0.72–0.90 s） |
| 骨盆 | 包絡 −33.5／−35 cm → 站直 −5.8～−23 cm ⇒ **高 13–29 cm** |
| 站姿 | 膝 **16–19°**、伸展比 0.985–0.990（平地待機 17°、1.004）——包絡下是 63–96° |
| 支撐腳搆不到 | Settling／Upright 期間 **0**（表中 1.4–3.9 cm 的帧都在包絡自己的停步階段，進入 Settling 之前） |
| 腳滑動／浮空 | 0／0（v1 不動鎖腳；上限取兩腳的 min，且兩腳同一階） |
| 骨盆連續性 | 上升峰值 0.3–0.65 m/s；相對包絡的加速度 ≤ 154 m/s² |
| 回到移動 | 0.20–0.42 s 回到包絡；**下樓起步峰值 2.1–2.6 m/s**（第一步的目標在起步後 4–6 帧就落到下一階，必須降 20–23 cm）；上樓起步 0.8–1.4 m/s；起步時腳搆不到 0–3.9 cm（與今天相同） |
| 移動意圖閘門 | 只比運動學早 1 帧（按鍵後 +1 vs +2），下樓起步峰值 2.28／2.56 → 2.28／2.13 m/s |

對照：今天的包絡在樓梯入口／出口本來就以最高 5 m/s 移動；Codex 認為 ω＝15 的回復在 X Bot 試驗中站得住，但手感要人工看。
⛔ 不以「面向下樓」限制站直高度：本專案的面向 ≠ 移動方向（strafe／鎖定），後退或轉向時會預測錯。

##### Codex 審查（原型前必修，都已寫進上表）

1. **High**——T3 目標與即時接觸在今天的骨盆計算點還不存在 ⇒ 先解腳再算骨盆；不得把上一帧的 `PrevContactValid` 當成本帧接觸。
2. **High**——「T ＝ max(E, U)」與「p ≤ U」互相矛盾（平地 E＝−2.9、U＝−3.3 永遠到不了）⇒ 拆成偏好上限 R（0.995）與硬性安全夾 C（1.0）。
3. **Medium**——回到包絡是第四個階段（Return），重設行為要逐項定義；原型開啟時「Moving 與今天完全相同」只在 Return 以外成立，關閉時才是逐位元相同。
4. **Medium**——彈簧遞推、速度傳遞、夾住時的速度投影、`dt ≤ 0` 要明確定義。
5. 其他：髖位由 Rig 寫進 `FootIKPoseData`（事實；優先 `bodyPosition ＋ bodyRotation × 由 avatar 量得的髖偏移`，先用探針對照 UpperLeg 骨骼）；
   不需要 ADR（Presentation 內的局部政策，寫 Living Spec）。測試：E > U、C < E、進出連續、夾住後放開、失去 `ikAllowed`、瞬移、`dt＝0`、關閉時逐位元相同。

> ✅ 使用者核准（2026-09-25）：做 X Bot 限定、開關控制的執行期原型——見下方。

##### 🧪 執行期原型（2026-09-25；X Bot 開啟、Y Bot 關閉；⚠️ Codex 實作審查**尚未完成**（額度用盡），人工 Play 驗收未做）

**程式**：`StandingPostureSolver.cs`（新：`PosturePhase`／`PostureState`／`PostureInputs`、`LegBound`、`SpringStep`、`Step`）、
`FootIKController.ResolveStandingPosture`、`FootIKSettings`（`UseStandingPosturePrototype` 等，預設關）、`FootIKRig`／`FootIKPoseData`（髖位與腿長事實）。
開關關閉（或 T3 關閉）時骨盆照舊在 ② 之後以 MoveTowards 更新，逐位元不變。

- **髖位來源已實測驗證**：探針元件在 `OnAnimatorIK` 記錄候選值，與「帧末骨骼扣回 LateUpdate 的 root 位移與已套用骨盆」重建的本帧 pre-IK 髖位比對：
  **UpperLeg 骨骼在 `OnAnimatorIK` 開頭就是本帧 FK 姿勢（誤差 ≤ 0.01 cm）**；`bodyPosition ＋ 常數偏移` 反而差 1.8–2.8 cm（p50），最多 9 cm。
  ⇒ Rig 直接讀骨骼（仍只是事實，唯一 Writer 不變）。先前「transform 可能是上一帧」的顧慮在這個 rig 上不成立。
- 順序：原型開啟時骨盆改在 ③' T3 解完兩腳**之後**計算（④），`ResolveContactPosition` 回傳本帧的動畫腳速度與即時接觸。
- 偏好上限 R（0.995）與硬性上限 C（1.0、支撐加權）分開；目標 min(max(E, min(R,0)), C)；C 夾住時向上速度歸零；速度跨 Settling／Upright／Return 延續，
  進入 Settling 以上一帧位移為種子；Return 不夾 p ≥ E（E 是包絡原始目標，會跳）；`dt ≤ 0` 不推進；重設後第一帧量不到 root 速度 ⇒ 不算靜止（自審發現）。
- 靜止：`MovementIntent.DesiredSpeedNormalized ≤ 0` ∧ root < 0.05 m/s ∧ 兩隻動畫腳 < 0.2 m/s ∧ 兩腳即時接觸，連續 6 帧。不讀 FSM 狀態名稱；V1 不動鎖腳。

**執行期量測**（`StairFeelProbe`：兩道樓梯 × Walk／Run；上樓中段停 2.5 s → 往上重新起步；下樓中段停 2.5 s → 往上方點 8 帧原地轉向 → 再站 2 s → 往下重新起步；
對照 = 同情境只開 T3）：

| 指標 | 只開 T3（包絡） | T3 ＋ 站直原型 |
|---|---|---|
| 放開按鍵 → Upright | — | **1.65–2.17 s**（轉向後 1.33–1.43 s） |
| 站立骨盆 | −33.5／−35 cm | **−5.0～−22.2 cm** |
| 站立膝彎／伸展比 | 65–102°／0.68–0.86 | **22–24°／0.983–0.990**（轉向後一次兩腳不同階：79°） |
| 站立時腳搆不到 p95／滑動／浮空 p95 | 0／0／≤ 0.4 cm | **0／0／≤ 0.7 cm** |
| 回到包絡 | — | 0.28–0.52 s |
| 起步後 0.5 s 內骨盆下沉 | 0 | **13–29 cm**，峰值 0.76–2.92 m/s |
| 起步後 0.5 s 畫面髖部垂直加速度峰值 | 17–68 m/s² | 34–106 m/s²（proto Walk 往下起步：17 → **106**） |
| 起步時腳搆不到 | 1.4–11 cm | 相同（既有 T3，與姿勢無關） |

- **人工驗收的關鍵情境**：prototype 樓梯 Walk、下樓中段停步後往下起步——骨盆在 0.5 s 內降 24 cm、峰值 2.9 m/s。這就是「起步時突然蹲下」要看的地方。
  `Stairs (1)` Run 轉向後那一窗的 3.69 m/s 在只開 T3 時也一樣（Run 點一下會跑出去，包絡本身在跟）。
- 測試：`StandingPostureSolverTests` 13 個；PlayMode 新增 `Player_StandingPosture_RisesAboveEnvelope_WithFeetReachable`（兩道樓梯，三個朝向站定後骨盆高於包絡、腳搆得到）。
  既有的 `Player_BodyReference_IsIndependentOfFacing` 改為**明確只測包絡本身**（在測試內關閉站直原型）：站直時的骨盆刻意跟隨實際支撐、會隨朝向改變，那是新基線，由新測試驗證——
  不是測試豁免機制（CLAUDE.md「Architecture Invariants Track the Effective Baseline」）。
- 全套：EditMode 702／701／1 既有略過；PlayMode 99／98（唯一失敗是既有 C0）。
- 資產：`X Bot.prefab` `UseStandingPosturePrototype` ＝ 1（Editor API）。還原：取消這個勾。⚠️ 只有 Unity 匯入成功，沒有專門測試保護。
- **未完成**：Codex 實作審查（使用者要求在最終 Play 驗證之前；Codex 額度 2026-09-26 18:14 恢復）；人工 Play（下樓起步是否像突然蹲下）。


##### 📝 研究：停步語意——Settling（等全部靜止）vs StopRecovery（從最後的支撐腳開始）（2026-09-25，⛔ 只量測／離線模擬，未改執行期；`4c27527` 不動）

**使用者回饋**：現在的原型要等 root 與兩隻腳都完全停下才開始升骨盆；希望**停步動作進行中**身體就開始站起來——
最後的主要支撐腿開始伸直、抬高身體，同時後腳還在完成最後一步。問題是「狀態語意錯了」，不是六帧門檻太長（門檻不調）。

**資料**：站直原型開啟的 `StairFeelProbe`（`_t3p`，兩道樓梯 × Walk／Run，上樓／下樓中段停步）。腳目標、動畫、pre-IK 髖位都與骨盆無關 ⇒ 可離線重播。
分析：`Temp/StairProbe/stop_recovery.py`（用完即丟）。「可靠支撐」＝ T3 已鎖定這隻腳（β ≥ 0.95 且有即時接觸）且動畫腳 < 0.3 m/s。

**停步時間線**（秒，自放開按鍵起；8 次停步）

| 事件 | 時間 |
|---|---|
| 移動意圖歸零 | +0.017（一帧） |
| root 持續 < 0.5 m/s | 0.60–0.86 |
| **主要支撐腳最後一次落定**（之後不再離地） | **0.55–0.78**（此時 root 還在 0.41–1.24 m/s） |
| 後腳最後一次離地 → 最後落定 | 0.38–0.85 → **0.85–1.03** |
| root 停止 | 0.75–1.22 |
| **現行原型開始升骨盆**（Settling） | **0.98–1.33** |

⇒ 現行語意比「最後的支撐腳已經可用」晚 **0.35–0.7 s** 才開始。單腳支撐期間（主要腳落定 → 後腳落定）的站直上限 −21.9～+1.5 cm，兩腳上限 −22.2～−4.9 cm。

**「最後的」支撐腳能不能即時辨認**：停步動畫還會走 1–2 步。之後會再離地的支撐，落定時 root 速度 **≥ 1.37 m/s**（Walk）／≥ 1.40 m/s（Run）；
最後的主要支撐落定時 **0.41–1.24 m/s**。⇒ 以 root 速度門檻可以分開，但餘裕窄（prototype Walk 上樓 1.24 vs 其他情境 1.37）。

**離線模擬**（同一個模擬器，彈簧 ω 6、Return ω 15，都與原型相同；比較停步窗內）

| 版本 | 定義 |
|---|---|
| Settling（現行） | 移動意圖 0 ＋ root < 0.05 ＋ 兩腳 < 0.2 m/s，連續 6 帧；兩腳都約束 |
| SR0 | 移動意圖 0 ＋ 任一可靠支撐 ⇒ 開始；只有可靠支撐的腳約束站直高度，後腳落定才加入 |
| SR1 | SR0 ＋ 後腳依 β 漸進加入約束 |
| **SR1 ＋ root ≤ 1.0 m/s** | SR1 ＋ 進入時 root 速度門檻（辨認「最後的」支撐） |

| 指標（8 次停步） | Settling（現行） | SR0／SR1 | **SR1 ＋ root ≤ 1.0** |
|---|---|---|---|
| 放開按鍵 → 骨盆開始升 | 0.98–1.33 s | 0.03–0.67 s（4／8 次在 0.25 s 內就開始＝假起點） | **0.50–0.70 s** |
| 放開按鍵 → 站直 | 1.65–2.17 s | 0.93–1.63 s | **0.97–1.63 s** |
| 支撐膝彎 @0.75／1.0／1.25 s | 66–96°／62–94°／41–90° | 51–72°／4–58°／3–39° | **48–81°／14–58°／7–34°** |
| 支撐腳搆不到 | 0（s1 Run 上樓 5.8 cm，三者相同＝既有） | 同 | **同** |
| 後腳（擺盪）搆不到 | 0／3.0／20.2 cm（既有） | 同 | **同**（提早站起沒有讓後腿被拉直） |
| 骨盆速度／加速度（停步窗） | ≤ 0.65 m/s／≤ 31 m/s² | ≤ **2.22 m/s／133 m/s²**（s1 Walk 下樓） | **≤ 0.71 m/s／≤ 31 m/s²** |
| 第二隻腳加入時的跳動 | — | 最多 2.22 m/s | **≤ 0.65 m/s** |
| 上升中途又下沉（> 1 cm） | 0 | 0–5 次 | **0–1 次** |
| 滑動／浮空 | 0 | 0 | **0**（支撐腳搆得到 ⇒ T3 鎖定點不動） |

**結論**：
1. **語意確實是錯的。** 現行 Settling 等「root 停止且兩腳都靜止」，比最後的支撐腳可用晚 0.35–0.7 s；StopRecovery 從最後的支撐腳開始，
   早 0.35–0.7 s 開始升、早 0.5–0.9 s 站直，膝蓋在停步動作中逐漸伸直；沒有新增搆不到、滑動、浮空，平順度與 Settling 相當。
2. **但「任一可靠支撐」不夠**：放開按鍵時正在支撐的那隻腳往往還會再抬起（停步動畫再走 1–2 步）⇒ 假起點、上升後又下沉、s1 Walk 下樓 2.2 m/s 的跳動。
   需要一個「這是最後的支撐」的即時訊號；本資料中 root 速度 ≤ 1.0 m/s 可以分開（非最後的支撐 ≥ 1.37 m/s），但只有 8 次停步、且餘裕窄——這是新的參數，不是六帧門檻。
3. 後腳依 β 漸進加入（SR1）在這批資料沒有差別：既有的支撐加權硬性上限已經讓後腳漸進加入；加入時的跳動在有門檻時 ≤ 0.65 m/s。
4. **沒量到的**：停步後很快又按下（< 1 s）——StopRecovery 會先升一部分、Return 再降，Settling 則根本還沒升；需要探針加「短停步」情境。

**若採用，要改的是狀態機本身**（待使用者裁決後再改，Codex 審查的對象也會變）：
`Moving → StopRecovery（移動意圖 0 ∧ 至少一隻可靠支撐腳 ∧ root ≤ v_final；只有可靠支撐的腳約束偏好高度，硬性上限照舊支撐加權）
→ UprightStanding（兩腳都可靠支撐 ⇒ 兩腳約束）`；移動意圖 ≠ 0 或失去所有支撐 ⇒ 立即 Return（同現行）。


##### 📝 研究：樓梯停步對照平地停步的節奏（2026-09-25，⛔ 只量測／離線模擬，未改執行期）

**使用者簡化目標**：不需要精緻的樓梯站姿系統；在樓梯上停下，走向 Idle 的時機與視覺節奏要和**平地停步**（或下樓走到平地再停）差不多——
只要去掉多出來的「延遲的半蹲階段」。不追求最早恢復，除非資料證明必要，也不要求完美預測最後的支撐腳。

**基準**：探針的 `settle` 窗＝下樓走到平地 1.5 m 後停步（兩道樓梯 × Walk／Run，4 次）。平地上 `PelvisOffsetY` 幾乎不動，所以比的是**看得到的姿勢**：
髖部高度（腳下地面起算）與支撐膝彎。分析：`Temp/StairProbe/idle_rhythm.py`（用完即丟）。

**平地基準**：放開按鍵後髖部先隨停步動畫下沉一次，**0.20–0.28 s** 開始最後一次上升，**0.37–0.63 s** 接近待機（髖高差 ≤ 3 cm、膝 ≤ 31°），最終膝約 18–22°。
⚠️ 平地的「接近待機」發生在**最後兩步落地之前**（兩隻腳最後落定 0.55–0.63／0.83–0.98 s，root 停 0.73–1.22 s，與樓梯上的時間線相同）——
平地上每一步都踩在同一高度，所以停步動畫可以邊走邊站直；樓梯上最後幾步踩在不同的階，**不可能完全照抄**而不讓骨盆隨步起伏。

**比較**（8 次樓梯停步；「重複起伏」＝骨盆修正本身的下降 > 1 cm 次數；「看得到的轉折」＝世界座標髖高轉折 > 2 cm 次數，平地為 1–3）

| 規則 | 骨盆開始恢復 | 接近待機 | 支撐腳搆不到 | 骨盆速度／加速度峰值 | 重複起伏 | 看得到的轉折 |
|---|---|---|---|---|---|---|
| 平地基準 | 0.20–0.28 s | **0.37–0.63 s** | — | — | — | 1–3 |
| 只有包絡 | 永不 | 永不 | 0 | 0 | 0 | 0–2 |
| **現行 Settling**（root < 0.05 ∧ 兩腳 < 0.2，6 帧） | 1.00–1.33 s | 1.50–1.98 s | 0 | 0.65 m/s／8 | 0 | 0–3 |
| StopRecovery（可靠支撐 ＋ root ≤ 1.0） | 0.53–0.80 s | 0.92–1.45 s | 2.8 cm | 0.71／33 | 1 | 0–3 |
| 放開按鍵就開始（逐腳依支撐量） | 0.05–0.15 s | 0.65–1.42 s | 3.9 cm | **4.8／293** | **10** | 0–3 |
| 放開按鍵就開始，ω 10 | 0.03–0.12 s | 0.62–1.18 s | 3.9 cm | **4.8／259** | **13** | **1–7** |
| root 瞬時 ≤ 1.0 就開始（雙腳約束） | 0.05–0.35 s | 0.67–1.42 s | 3.9 cm | **4.4／274** | **10** | 0–5 |
| **雙腳約束、「靜止」改為 root ≤ 1.0 m/s 持續 6 帧** | **0.58–0.88 s** | **1.02–1.48 s** | **0** | **0.61／9** | **0** | **0–3** |
| 同上，root ≤ 0.5／0.3 | 0.73–1.00／0.80–1.08 s | 1.13–1.58／1.22–1.73 s | 0 | 0.68／8 | 0 | 0–3 |
| 同上 ≤ 1.0，彈簧 ω 9／12 | 0.55–0.80 s | 0.87–1.28／0.80–1.17 s | 0 | 0.99／17、1.39／25 | **1／2** | 0–3 |

平均髖高（m）與支撐膝（°）@ 0.25／0.5／0.75／1.0／1.5／2.0 s：平地最終 1.04 m、約 21°；
現行 Settling 0.80／0.79／0.85／0.88／0.99／1.05、76／84／83／79／47／25°；**建議規則 0.80／0.79／0.86／1.00／1.05／1.06、76／84／79／50／22／21°**。

**結論**：
1. **多出來的延遲半蹲來自「靜止」的定義**：現行規則要等 root 完全停（< 0.05 m/s）而且兩隻動畫腳都不動，停步動畫最後的腳步還在收的時候就一直蹲著。
2. **最簡單、合格的規則**：狀態機、雙腳約束、六帧門檻都不變，只把「靜止」改成「**沒有移動意圖 ∧ root < 1.0 m/s**（持續 6 帧）」，不再要求兩腳靜止。
   ⇒ 比現行早約 0.4–0.5 s 開始、早約 0.5 s 接近待機；支撐腳搆得到、沒有重複起伏、平順度與現行相同、看得到的髖部轉折與平地同級。
   不需要預測最後的支撐腳（StopRecovery 的「可靠支撐」條件反而多了 2.8 cm 搆不到與 1 次起伏）。
3. **放開按鍵就開始（或 root 瞬間變慢就開始）不行**：停步動畫還在走的那幾步，骨盆會照每一步踩的階起伏（10–13 次、峰值 4.4–4.8 m/s）＝ 已否決的逐腳骨盆。
4. **跟平地的剩餘差距（接近待機晚 0.4–0.85 s）在這批資料裡去不掉**：平地是邊走最後兩步邊站直；樓梯上那兩步踩在不同階，任何更早的規則都會帶回起伏或腳搆不到。
   彈簧加快（ω 9／12）只快 0.1–0.2 s，但會出現 1–2 次下沉 ⇒ 維持 ω 6。
5. 起步回到包絡（Return）不受影響（站立結束時的姿勢相同）。

**若採用**：只改 `FootIKController.ResolveStandingPosture` 的 `still` 條件（root 門檻 `StandingRootSpeedMax` 0.05 → 1.0，拿掉兩腳速度條件），其餘不動——
比 StopRecovery 小得多，Codex 審查的對象仍是 `4c27527` ＋ 這一處。

> ⚠️ **2026-09-26 使用者把目標收斂得更嚴格**：不是「早一點開始」即可，而是「第一支最終支撐腳穩定＝恢復開始；第二腳穩定＝恢復結束」。
> 因此上面的 root-only 結論與已套用、未 commit 的 `root < 1.0 × 6 帧` 只保留為比較基線，**不是建議的最終模型**；新結論如下。

##### 📝 研究：支撐腳 → 尾腳落定的 StopProgress 映射（2026-09-26，⛔ 只量測／離線重播，未改執行期）

**目標語意**：

`第一支最終支撐腳可靠落定 = upright recovery 0%`<br>
`尾腳完成最後回收並可靠落定 = upright recovery 100%`

不能再用「兩腳都停了才站起來」，也不能只把 root 速度門檻放寬或只加大彈簧。恢復期間要讓支撐膝與骨盆連續伸展，尾腳加入最終約束時不得跳。

**資料與重跑**：2026-09-26 重新啟動 Unity 6.5，跑既有 `StairFeelProbe.Probe_UpTurnDown`：兩道樓梯 × Walk／Run × T3／T3Posture，**8／8 通過**；
逐帧 CSV 在 `Temp/StairProbe/*_t3p.csv`（throwaway，不進版控）。固定 60 Hz；表中 frame 是 probe 已校正的 `Time.frameCount - 1`。

**量測定義（避免用結果反推訊號）**：

- `reliable foot` ＝ T3 `Locked` ∧ β ≥ 0.95 ∧ animation-pose 水平腳速 < 0.3 m/s ∧ 本帧 live contact。表中的「最終」只在離線標註時要求之後連續 ≥ 12 帧；執行期 proposal **不等待**這 12 帧。
- `primary plant` ＝兩腳各自最後一段可靠支撐的起點中，較早者；`final plant` ＝另一腳最後一段可靠支撐的起點。
- `trailing lift` ＝結束在 final plant 的最後一次 animation-pose 腳部回收：由最後局部最低點後上升 5 mm 的首帧標記；`apex` ＝其後最後一個 > 2 cm 局部峰值。這只用腳位運動學，不讀動畫狀態名稱。
- `upright bound U` ＝現有 `min(max(E, min(R, 0)), C)`；reach error ＝ `max(0, |hip + pelvis − T3 target| − 0.889 m)`。

**逐幀時間線（時間都相對按鍵釋放）**：

| 情境 | release frame | primary reliable plant | trailing lift | trailing apex | final reliable plant | P→F 間隔 |
|---|---:|---:|---:|---:|---:|---:|
| prototype Run 上 | 3039 | L 3087 / +0.800 s | R 3060 / +0.350 | 3074 / +0.583 | R 3098 / +0.983 | 11 f / 0.183 s |
| prototype Run 下 | 3232 | R 3265 / +0.550 s | L 3250 / +0.300 | 3269 / +0.617 | L 3290 / +0.967 | 25 f / 0.417 s |
| prototype Walk 上 | 1282 | L 1316 / +0.567 s | R 1310 / +0.467 | 1319 / +0.617 | R 1337 / +0.917 | 21 f / 0.350 s |
| prototype Walk 下 | 1532 | L 1577 / +0.750 s | R 1580 / +0.800 | 1589 / +0.950 | R 1600 / +1.133 | 23 f / 0.383 s |
| `Stairs (1)` Run 上 | 6601 | R 6634 / +0.550 s | L 6619 / +0.300 | 6638 / +0.617 | L 6659 / +0.967 | 25 f / 0.417 s |
| `Stairs (1)` Run 下 | 6789 | L 6837 / +0.800 s | R 6810 / +0.350 | 6824 / +0.583 | R 6848 / +0.983 | 11 f / 0.183 s |
| `Stairs (1)` Walk 上 | 4919 | L 4966 / +0.783 s | R 4969 / +0.833 | 4978 / +0.983 | R 4989 / +1.167 | 23 f / 0.383 s |
| `Stairs (1)` Walk 下 | 5137 | L 5173 / +0.600 s | R 5176 / +0.650 | 5185 / +0.800 | R 5196 / +0.983 | 23 f / 0.383 s |

**同一 P→F 區間的姿勢／可達性**（pelvis、E、U 是 root-relative offset；負值＝下沉）：

| 情境 | root 速度 P→F | 當前 pelvis / E @P | U @P→@F | 支撐膝 @P | 支撐腳 reach error @P |
|---|---:|---:|---:|---:|---:|
| prototype Run 上 | 0.438 → 0.264 m/s | −30.1 / −33.4 cm | −13.0 → −17.2 cm | 76.3° | 0 |
| prototype Run 下 | 1.158 → 0 | −35.0 / −35.0 cm | −12.9 → −4.7 cm | 81.3° | 0 |
| prototype Walk 上 | 1.290 → 0.354 | −33.5 / −33.5 cm | −33.5 → −16.5 cm | 105.1° | 0 |
| prototype Walk 下 | 0.468 → 0 | −30.5 / −33.5 cm | −12.2 → −11.1 cm | 78.3° | 0 |
| `Stairs (1)` Run 上 | 1.158 → 0 | −35.0 / −35.0 cm | −16.8 → −20.1 cm | 106.9° | 0 |
| `Stairs (1)` Run 下 | 0.444 → 0.264 | −32.3 / −35.0 cm | −14.9 → −10.6 cm | 74.8° | 0 |
| `Stairs (1)` Walk 上 | 0.462 → 0 | −30.5 / −35.0 cm | −7.7 → −10.3 cm | 91.1° | 0 |
| `Stairs (1)` Walk 下 | 0.468 → 0 | −33.5 / −35.0 cm | −23.5 → −21.8 cm | 60.3° | 0 |

8／8 的 root 在 P→F 淨減速；區間只有 **0.183–0.417 s**。這個下限很重要：最壞情境要在 11 帧內升約 24 cm，任何嚴格滿足「F 時已直立」的模型都不可能同時維持現行 0.6 m/s 左右的慢速上升。

**「哪一次 plant 才是 final primary」的因果可辨識性**：單看 `Locked`／β，執行期無法知道一隻剛落地的腳未來還會不會抬起；要立即開始就必須接受一個預測訊號、延遲確認、或動畫語意。
本批資料最簡單的預測器仍是 root 速度，但它只負責**分類 plant edge**、不負責驅動整段上升：

- final primary 的 root 速度＝**0.438–1.290 m/s**；
- 更早、之後還會再走一步的 reliable-plant edge＝**1.428–4.698 m/s**；
- `v_final = 1.35 m/s` 在 8 次內全分開，最小裕度只有 **0.138 m/s**。現行工作樹的 1.0 m/s 會錯過 1.158／1.290 的兩次真正 primary，故不符合「primary plant 當下開始」。

⇒ 1.35 只能是 X Bot prototype 的 classifier，不是架構常數；必須加不同 clip／速度／短停的 probe 才能決定是否可保留。完全拿掉 classifier 會重現 §「停步語意」的假起點。

**離線比較（同一 8 段 CSV）**：

| 模型 | 恢復開始 | final plant 時離 U | final 支撐膝 | P→F reach error max（支撐／尾腳） | pelvis v／a max | >1 cm 重複下降 | final join 單帧差 |
|---|---:|---:|---:|---:|---:|---:|---:|
| legacy Settling（root < 0.05 ＋兩腳 < 0.2，6 f，ω6） | +0.95–1.30 s | **12.6–28.8 cm** | 63.3–96.6° | 0／0.2 cm | 0.36 m/s／13.0 m/s² | 0 | ≤0.59 cm |
| 工作樹 root-gated early recovery（root < 1.0，6 f，ω6） | +0.52–0.77 s | **2.9–14.2 cm** | 33.0–68.6° | 0／0.2 cm | 0.61／9.5 | 0 | ≤0.89 cm |
| **StopProgress mapping（下述 proposal）** | **primary frame 本身**；可見 >5 mm 於 +0.033–0.083 s | **preferred U 內 0.5 cm** | **12.7–19.5°** | **0／0.2 cm** | **2.41／41.8** | **0** | **≤0.36 cm** |

逐情境在 final plant 的「仍低於 U」：legacy＝16.3／28.8／16.9／21.2／14.9／24.4／23.4／12.6 cm；root-gated＝5.8／14.2／11.0／4.7／4.1／13.1／4.5／2.9 cm；StopProgress＝0（單調保持可能比 preferred 高最多 0.5 cm，但仍低於 hard limit C）。

**建議的最小因果模型（尚未實作）**：

1. `Moving → StopRecovery`：沒有移動意圖，且**新**的 reliable-plant edge 通過 `v_final` classifier；這隻是 primary，另一隻是 trailing。這一帧 progress＝0，記住 foot identity、`startPelvis`／前一帧輸出與速度。不要等 6 帧，也不要等兩腳。
2. 用 primary 當下的 trailing T3 狀態只分兩個已量到的剩餘步長：
   - trailing `Unlocked` ∧ β ≥ 0.8 ∧ pose 垂直速度 < 0（已在最後下降）⇒ `D = 0.18 s`；
   - 其餘（仍 Locked／Releasing／上升）⇒ `D = 0.40 s`。
   兩群實測真值分別是 0.183 s（2 次）與 0.350–0.417 s（6 次）。
3. `q = smootherstep5(clamp01((now − primaryTime) / D))`，`smootherstep5(x)=6x⁵−15x⁴+10x³`。每帧仍用現有兩腳幾何算 `U(t)=min(max(E,min(R,0)),C)`；候選骨盆 `candidate = lerp(E,U,q)`。
4. StopRecovery 的輸出只上升：`pelvis = min(max(previousPelvis, candidate), C)`。也就是姿勢不因 preferred bound 的小幅回擺而反覆下降；**只有 hard reach limit C** 能立即壓低以保接觸。
5. trailing 出現 final reliable-plant edge ⇒ q＝1、進 Upright。離線中這個 latch 前已在 U 內 0.5 cm，加入第二腳的單帧差 ≤ 0.36 cm，不是「第二腳一到才開始站」。
6. 任一時刻移動意圖恢復或 primary 失效 ⇒ 進既有 Return，位置不重設、以最後兩帧輸出速度為 seed，ω15 回 E；`dt≤0`／teleport／IK 失效沿用現有 Reset。全程不讀 FSM／animation state 名稱。

這不是「提高 spring stiffness」：StopRecovery 本身改成明確的 0→1 姿勢映射；ω6 只保留給既有 Upright 微調（若仍需要），Return 仍是 ω15。

**符合／未證明的驗收項**：

- ✅ 第一支最終支撐腳事件當帧開始 progress；沒有額外等 root 完全靜止／兩腳靜止。
- ✅ 8／8 在 final plant 已到 preferred upright 內 0.5 cm，膝 12.7–19.5°；支撐 reach error 0，尾腳 0.2 cm 是三模型共同的原資料誤差。
- ✅ T3 腳目標與鎖點完全不改；離線無新增浮／滑的運動學條件，無 >1 cm 重複下降，final join ≤0.36 cm。
- ⚠️ 嚴格 endpoint 的代價是最短 11 帧情境 pelvis 峰值 **2.41 m/s／41.8 m/s²**。數學上連續但視覺是否像「太快站直」只能 Play 判斷；不能用離線數字宣稱 feel 通過。
- ⚠️ 現有長停後 Return 可原樣復用並攜帶速度；**<1 s 快速重新移動仍沒有資料**。實作前 probe 必須增加 primary 後 0.10／0.25／0.50 s restart，驗證沒有先升再砸回包絡。
- ⚠️ `v_final` 的 0.138 m/s 分離裕度很窄。至少要補平地、斜坡、不同起始速度／混合權重；若重疊，T3 contact 本身不提供足夠未來資訊，應升級為 motion/stop planner 公開的語意事件，而不是偷讀動畫 state 名稱。

**實作範圍（若核准）**：Presentation 內局部政策，不改黑板、不改 ownership、不需 ADR。改 `StandingPostureSolver`／`ResolveStandingPosture` 的狀態與 inputs，補純函數 EditMode 測試；
PlayMode probe 加 final-plant endpoint、單調性、join step、reach 與三種 quick-restart。工作樹已套用的 root-only 版本應被此模型**取代**，不是疊在它上面。

#### 3.5.5 Level 1 rigid sole approximation（🆕 2026-08-31，L1 落地；**視覺驗收暫時通過**）

> **狀態**：Level 1 完成，**不再繼續修**。剩餘誤差已降級為 `docs/03` §1.3-**L7**（設計接受）。
> 本節取代 §3.5.3 路線圖中「M4+ 品質升級」對 Heel/Toe 雙點採樣的預告——那一項**已實作**。

**約束模型（三層分工）**

| 層 | 決定什麼 | 由什麼決定 |
| --- | --- | --- |
| **旋轉** | 腳的姿態 | `R = FromToRotation(up, SoleNormal) · poseRotation`。左乘世界旋轉 ⇒ **動畫自身的 pitch 原樣保留**（toe-up／toe-down 都不被壓平），契合哲學「腳踝自由旋轉、不強制壓平」 |
| **水平位置** | 腳踝 XZ | **等式約束**：泰勒斯修正使腳踝維持在動畫 goal 的垂直線上。移植自 [ozz-animation `foot_ik`](https://guillaumeblanc.github.io/ozz-animation/samples/foot_ik/)，它明文點名舊寫法之誤：*"ankle position cannot be simply be offseted by foot offset"*。⚠️ **順帶修掉 M3.3 就存在的舊 bug**：`hit + n·fbh` 在 30° 坡會讓腳踝水平漂移 5cm 且坐得過低 |
| **垂直位置** | 腳踝 Y | **不等式約束（只抬不壓）**：由 `R` 與 `footBottomHeight` 算出 heel／toe **真實端點世界座標**，在該處打 ray，`lift = Max(0, Max(各端點穿透量))`。**argmax 的端點即接觸點**，其餘端點自動獲得淨空 |

**關鍵教訓（三次推翻的成因，寫下來避免重蹈）**

1. **雙點取較高者當「高度」是錯的**——連續坡面上必然選到上坡側，浮空 = 上坡側取樣距離 × tanθ（30° 約 8.7cm），且隨角色朝向擺動。**雙點只該回答「有沒有戳穿」，不該回答「地面在哪」。**
2. **由 heel/toe 高差合成 pitch 是錯的**——連續斜面上 pitch 早由命中法線提供（多餘），階梯上兩點高差根本不是坡度（錯誤）。**兩種地形沒有一種需要它。**
3. **residual 必須含腳的幾何**——先前版本以「地面 vs 假想平面」求穿透，算式裡沒有 `poseRotation`，於是任何動畫 pitch 對該約束都是**隱形**的。平地 ＋ toe-up φ 時腳跟穿地 `HeelOffset·sinφ − fbh·(1−cosφ)`（φ=20° 約 2.8cm），而 residual 恆回 0。**位置解與旋轉必須用同一個 `R`。**

**踝關節角度夾限（`MaxFootAlignAngle`，暫定 23°）**

腳底**不強制整面貼地**。超過夾限時腳保持較自然姿勢，由 residual 抬到上坡側接觸、下坡側浮空 ＝ 真人行為。
heel 淨空 = `span × (tanθ − tan c)`，`span = HeelOffset + ToeOffset`。
⚠️ **夾限的視覺效果依賴 `span`**——重量 `HeelOffset`／`ToeOffset` 後必須重新確認此值。
📌 屬哲學第 3 條明文允許的「Reach Clamp 類」，**不是**被禁的 Fade／Gate／降權重：權重系統零改動、夾限連續、無二態切換。
📌 與 production 引擎同構：UE 的 ground orientation 亦非 100% 貼地，且備有 heel adjustment／heel lift 設定 ⇒ **heel 浮起是可調特性，不是缺陷；0 clearance 不是唯一正解。**

**Level 1 已知限制（接受，不修）**

* 斜坡上少量 heel／sole 浮空，量級 ≈ `ΔToeOffset × sin(該腳 pitch)`；**前後腳不對稱**（後腳 pitch 大 ⇒ 誤差大）。近距離側視才可見，3–4m 鏡頭距離下不可見。
* **無 Medial／Lateral 取樣** ⇒ 側坡時腳底左右邊緣不受約束。
* 陡下坡整個踩地相維持腳跟接觸（真人會隨步態滾向前腳掌）——需 Foot Contact／Foot Phase 與**不對稱**踝角上限（蹠屈 40–50° 遠大於背屈 10–20°）才能表達。
* 階梯邊緣一端懸空、heel/toe 任一 ray 落空時退回 ankle-only（無殘差保護）。
* `GroundY`（骨盆輸入）在雙點路徑與退化路徑語意不同（前者＝接觸端點下方實際地面高度，後者＝腳踝推導接觸高度）。

**升級階梯（明確延後，作品集展示不需要）**

| Level | 內容 | 狀態 |
| --- | --- | --- |
| **Level 1** | Heel ＋ Toe 兩點剛體腳底近似；保留動畫 pitch；有限 slope alignment；residual 防穿；允許 heel clearance | ✅ **完成** |
| **Level 2** | 完整 foot footprint：Heel／Toe／**Medial／Lateral**（比照 UE 的 Foot/Floor Constraint 尺寸描述） | ⏸ 延後 |
| **Level 3** | **Ball／Toe bone secondary plant**——不再把整隻腳當單一剛體（比照 UE 的 `BallBone`） | ⏸ 延後 |

**PlayMode／Game View 探測可視化（✅ 2026-09-10）**

`FootIKController` 在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 內記錄每幀實際採樣結果，顯示
ankle／heel／toe rays、命中／落空、ground hit point、原始 `Normal`、夾限後 `SoleNormal`、
pre-IK goal 到最終 target 的修正、target sole pose／residual lift，以及 heel sample origin →
toe sample origin 的直接連線與 corrected foot-space `+Z` forward 箭頭。

* **Game View／PlayMode 主通道**：真正的 runtime `LineRenderer` 幾何。正常按 Play 即可直接看到，
  **不依賴 Game View 的 Gizmos 開關，也不需要選取角色**。
* **Scene View 詳查通道**：保留 `OnDrawGizmos`；它只負責 Editor 的近距離檢查，不是 Game View 前提。

顯示遵守「記錄，不重算」：兩條 presentation 只讀 `Tick` 內已完成的 Editor／Development-only private snapshot，
不重新發出 `Raycast`，也不把資料發布到黑板或新增 public API。A34 架構測試守住這條信任鏈。
`drawFootIKRuntimeLines` 與 `drawFootIKSceneGizmos` 分開控制，預設開啟。Release build 兩者皆不存在；
Development Build 可保留 runtime lines，不建立正式 HUD。

**Heel／Toe offset 使用的 basis（2026-09-10 明文化）**：`poseRotation` 來自
`Animator.GetIKRotation(AvatarIKGoal.LeftFoot/RightFoot)` 的 pre-IK goal rotation，**不是**直接讀
foot Transform／ankle bone Transform，也不是 character root rotation。Ankle ground query 命中後先算：

`sample.TargetRotation = FromToRotation(worldUp, SoleNormal) × poseRotation`

再以這個 corrected foot basis 建立 `localHeel = -Z × HeelOffset - Y × FootBottomHeight` 與
`localToe = +Z × ToeOffset - Y × FootBottomHeight`，轉到 world space。換言之，offset 的前後軸是
`sample.TargetRotation × Vector3.forward`；只有 sample origin 的向上偏移與 Raycast 方向使用 world
`+Y／-Y`。Debug 直接快取這次計算使用的 forward，不在 drawer 重新推導。

Heel／Toe 真正的 **BASE** 是 `sample.TargetPosition`（ankle query 命中後的 corrected ankle target），
不是原始 `Animator.GetIKPosition(Foot)`，也不是 foot bone Transform。它的 XZ 保留 Animator foot goal
的 XZ，但 Y 已由 ankle hit、`SoleNormal` 與 `FootBottomHeight` 修正。Scene View 以實心點和
`L/R BASE`、`L/R HEEL`、`L/R TOE` 標籤顯示三個 production 值；BASE→HEEL 用橘線、
BASE→TOE 用藍線、HEEL→TOE 用深灰線。既有無標籤 wire sphere 仍表示 hit／miss query end，
不要誤認為 origin。

**Follow-up（不現在實作）**

| # | 項目 | 說明 |
| --- | --- | --- |
| **FU-IK-1** | **`HeelOffset`／`ToeOffset` 幾何量測** | 現值（0.1／0.15）**不是正式量測值**。v4 後它們是**腳底幾何常數，不是手感旋鈕**——定義接觸候選集合，填錯即約束集合不完整，任何調參都救不了。⚠️ Heel 量到**腳跟後緣**、Toe 量到**蹠球**（不是腳尖尖端，該處上翹會導致 residual 過度抬升） |
| **FU-IK-2** | **Foot contact debug visualization** | ✅ **已完成（2026-09-10）**：Game View 用 runtime `LineRenderer`（不依賴 Gizmos），Scene View 用 `OnDrawGizmos`；顯示 heel／toe／ankle probes、hit／miss、raw／clamped normal、target sole pose、residual lift、heel→toe sample-origin span 與實際 corrected foot forward；無 Debug Framework |
| **FU-IK-3** | 有可視化後**重新量** `HeelOffset`／`ToeOffset` | 確認實際取樣點與鞋底幾何一致；量完後**只准再調一次** `MaxFootAlignAngle` |

**⚠️ 重開 Foot IK 的條件（任一成立才重開，否則不動）**

1. 平地仍**固定**穿模
2. 斜坡有**明顯**懸空
3. Walk／Run／Stop 有**明顯**跳動
4. heel／toe ray 顯示接觸點**明顯不對**（需先有 FU-IK-2）
5. 左右傾斜地形出現腳底**側邊明顯穿透**（且該地形確實出現在展示影片中）

> ⛔ **不得為了追求 0 浮空繼續提高 `MaxFootAlignAngle` 或擴大模型**——那條路的終點是整片腳底吸附在坡面上，直接違反鐵律 **Natural Pose > Terrain Adaptation > Perfect Foot Contact**。
