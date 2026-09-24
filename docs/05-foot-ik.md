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
