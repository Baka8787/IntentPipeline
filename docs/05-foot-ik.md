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

