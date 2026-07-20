# WORKLOG

> 唯一的進度管理文件。每完成一項立即更新。
> 歷史架構決策請看 `docs/changelog.md` 與 `docs/ADR/`；此檔只管「現在手上的工作」。

---

## 今日進度（2026-07-18）

三輪連發，詳見 changelog v0.17／v0.18：

1. **M2 Presentation Pipeline + Landing Audio ✅ 收案**（changelog v0.17）：修復前 session 幻覺殘局 → `JustLanded` 落地（YAGNI 閘門走完）＋`PresentationPipeline` 骨架（順序 6.5）＋Audio 三層（Event→Definition→Library）；Play 實測落地音正常。附 EditMode Warning 治理（RollState/JumpState 防線 `isPlaying` 語義精確化；測試契約輸出用 LogAssert.Expect＋鬆耦合 Regex）。
2. **M1 Locomotion ✅ 正式收案**（changelog v0.17 §5）：DoD 五項全過（0 error＋測試全綠／Play 實測／Profiler 0B／moveSpeedSource 接 Bake_Fast Run／Roll fade 資產真相驗證）。Locomotion 基線固定。
3. **M3 Foot IK 實作輪 ✅**（changelog v0.18）＋**M3.1 反饋迴路修正 ✅**（changelog v0.18.1）：實測腳踝抽搐 → Review 定位根因（Controller 採樣骨骼＝上一幀 IK 輸出，旋轉追逐＋權重鎖死雙迴路）→ 裁決雙管道修正——`FootIKController`（Root 決策，對 Animator 零依賴）⇄ 兩條單向管道（`FootIKTargetData` Controller 寫／`FootIKPoseData` Rig 寫）⇄ `FootIKRig`（Model，**Presentation Adapter**）。手填 footHeight 改讀 avatar `FeetBottomHeight`。**⏳ 等待使用者重編＋抽搐複測**（見下方）。

---

## 待使用者作業（M3 收尾）

**🔬 A/B 進行中（v0.18.7 候選）：旋轉公式「軸對齊壓平」 vs 基線「保留俯仰」**
- 改動：`ResolveFoot` 一行——`FromToRotation(poseUp, n) × poseRot`（腳底主動壓平）取代 `FromToRotation(worldUp, n) × poseRot`（保留動畫俯仰）。對照組＝git 基線（M3.5）。
- [ ] Play 對照重點：**階梯踏面上腳底板應水平**（遺留歪斜的直接驗證點）；平地站立／走動腳部自然（壓平只在權重 1 時生效、踩地相腳本來近平，預期損失極小）；斜坡貼合不退步；抬放腳過渡無異常
- [ ] 裁決：採用 → 收案入 changelog v0.18.7；不採 → `git checkout` 回基線並記錄結論
- （已 push ✅ 2026-07-18：M3.5 基線入版控——Foot IK 第一個乾淨版控錨點）

**M3.5 最終形（v0.18.6，字面回歸 M3.1）——push 前 checklist**：
- [ ] 重編 0 error＋EditMode 測試 **42 條**全綠（實驗純函數 7 條已隨機制移除）
- [ ] Inspector：Settings 剩 8 個參數（實驗參數與 Enable* flag 已刪，Unity 忽略孤兒序列化值）；確認 `GroundLayers` 含地形 Layer
- [ ] Play smoke test：平地／斜坡／樓梯行為＝M3.1（含大階梯抬腿）；**階梯腳踝歪斜為已知遺留**（極可能 M3.1 即存在，非 regression，不阻擋 push——首查項見下方路線圖）
- [ ] 上述綠燈後 **push to GitHub**（Git 由你操作）：這是 Foot IK 的第一個乾淨版控基線，未來所有對比以它為錨
- [ ] （遺留）CapsuleFitter Apply Prefab 確認；floor Scale Z 若尚未翻正順手改（-25.153 → +25.153）

---

## 工作清單

### Done（2026-07-18）
- [x] M2 全流程（黑板單幀事件 → 6.5 → Audio）＋收案；M1 DoD 收案；Warning 治理兩輪
- [x] M3 Foot IK：3 新檔＋Facade IK 通道＋`FootIKTests` 8 條＋Living Docs v0.18

### Doing
（無——等 M3 Unity 驗收）

### Todo
（驗收後排定下一輪；建議順序見文末）

---

## Backlog / Future Work（超出目前範圍，不動手）

### Foot IK 品質路線圖（M3.5 定調：單點＋權重補丁已到天花板，升級＝輸入資訊量）
- **首查項（下一輪 IK 起點，2026-07-18 參考碼對照後更新）**：階梯腳踝歪斜（踏面中央亦現、M3.1 即存在）→ 最可能根因＝**旋轉公式語義**：現行 `FromToRotation(worldUp, normal) × poseRot` **保留動畫腳踝俯仰**（locomotion 混合姿勢腳踝幾乎恆帶微俯仰，階梯上與水平踏沿對比即「腳底板斜」；踏面中央 normal=up 時=動畫原樣，歪照舊）；候選修正＝**軸對齊式**（`AngleAxis(Angle(poseUp→normal), Cross(poseUp, normal)) × poseRot`＝把腳底主動壓平貼地，參考碼驗證形態）。一個公式替換、零架構變更，A/B 後裁決。~~原假說 GetIK* 值域~~（參考碼佐證用法正常，降級）
- **M4+**：Heel＋Toe 雙點採樣（邊緣高低面裁定＋腳掌 pitch）、CapsuleCast（體積採樣；參考形態＝沿腳踝 −localUp 方向 CapsuleCast 檢測近距離接觸當權重）、Foot Contact 狀態機（plant/lift 事件，兼 Footstep 音源）、骨盆模型重評（參考形態＝`bodyY − (minFootGoalY + legHeight)` 以腿長可達性直接建模，取代地面差代理）
- **實驗歸檔**（程式碼已清除，復刻看 changelog v0.18.2~v0.18.6）：fade 族＝半 IK 常態化（棄）；Slope Gate＝邊緣震盪源（棄）；濾波＝離散選擇連續化（棄）；Reach Clamp＝方向正確但距離比模型在骨盆下沉時誤傷（未來以膝角度模型重評）
- ⚠️ 參考碼防搬運註記：其 raycast 從骨骼現值起打＝反饋污染（我們 M3.1 修掉的抽搐根因，快照 goal 起點勿退）；其 body 直接覆寫不適用（我們疊加式）；其漏設 RotationWeight 屬原 bug

### 使用者明定 Future Work（M3 裁決重申：需要時一律 TODO，不得提前實作）
- **Foot Phase Curve**（烘焙腳相曲線；等 Footstep／Audio 輪一併評估 Mixer 混合取值）
- **Footstep Event ＋ Audio Integration**（腳步音；事件源設計與 Foot IK pose 採樣天然銜接）
- **BlockIK／BlockAudio Writer ＋ Mini Arbiter**（F6 ArbiterPipeline 範疇，順序 4.5 已預留）
- **Animation Rigging Package／Two-Bone IK Solver**（現用 Unity Humanoid IK，Q1 裁決）
- **Motion Warping**（`ApplyBakedCompensation` 已有雛形，無呼叫端）
- **F2 Strafe 2D Mixer**（等瞄準/鎖定移動需求）／**F3 Combat**／**F4 Upper Body Layer**

### 工具/演算法 Backlog（沿革見 changelog）
- **B1** `Mathf.DeltaAngle` 疑慮（≥360° 旋轉動畫進場時重評）
- **B2** 多段跳空中段前搖落地邊角（併 ADR-002 §6-4 後續）
- **B3** `PlayWithCallback` lambda 閉包 GC（仍無呼叫端）
- ~~**B4** Config bakeMappings 冗餘條目~~ ✅ 已收掉（使用者清理，現僅 Roll 一條）
- **B5** CapsuleFitter v2（骨骼推估）
- **B6** ValidateHierarchy 增補 Model identity 警告
- **B7** 前搖期間輸入未鎖手感（F6 範疇）
- **B8** Loop Pose 評估（走路循環有接縫時啟動）
- **B9** 動畫參數平滑（Game Feel 輪：SmoothDamp 落點裁決＋加減速曲線）
- **B10** Facade 映射鍵 Editor 驗證工具（低優先）
- **B12** Config 引用驗證（OnValidate 抓「條目存在但引用死」；JumpState/RollState 執行期防線已覆蓋主要風險）

---

## 建議下一步（M3 驗收後）

- **M4 購入 locomotion 資產**（Movement Animset Pro 級別）→ 左/右腳停步、pivot、方向性起步＋foot-phase 資料設計（烘焙管線加 analyzer 即可）
- **M5 Combat 初版（ARPG）**→ 產生 Hit/Death 等真正需要「封鎖」的狀態
- **M6 ArbiterPipeline**（順序 4.5 兌現；BlockIK/BlockAudio writer 到位、§7/§8.3 旗標粒度屆時有真實案例可答）
- 表情模組：暫緩（X Bot 無臉部 rig）
