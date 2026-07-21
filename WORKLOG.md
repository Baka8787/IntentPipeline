# WORKLOG

> 唯一的進度管理文件。每完成一項立即更新。
> 歷史架構決策請看 `docs/changelog.md` 與 `docs/ADR/`；此檔只管「現在手上的工作」。

---

## 今日進度（2026-07-21）——Foot IK v1 收案輪（輪 1）✅

roadmap `docs/03-animation-roadmap.md` §1.4 收案清單執行完畢（詳 changelog v0.18.7）：

1. **程式碼**：`FootIKController.ResolveFoot` 旋轉公式還原基線「保留俯仰式」（`FromToRotation(worldUp, n) × poseRot`；A/B 軸對齊式歸檔）；`FootIKRig` 刪 `debugLogGoals` 臨時診斷段。
2. **文件同步**：changelog v0.18.7（樓梯 collider 根因／A/B 結論／設計哲學／v1 凍結宣告）；design-doc §4.6 補 Foot IK 設計哲學；dev-spec §3.5 補 v1 凍結狀態＋已知限制表 L1~L6、§3.5.3 首查項標否證、版本表補 v0.18.7（順修重複／錯置的 v0.18.3 列）。
3. **Foot IK v1 凍結**：架構健康、6 條已知限制（L1~L6）文件化於 dev-spec §3.5.2；品質升級改由 `docs/03` roadmap 承載。主線下一步＝**輪 2 Locomotion 資產升級**（＋Foot Phase 烘焙 stage＋B9）。

---

## 前次進度（2026-07-18，已收案 → changelog v0.17／v0.18）

三輪連發，詳見 changelog v0.17／v0.18：

1. **M2 Presentation Pipeline + Landing Audio ✅ 收案**（changelog v0.17）：修復前 session 幻覺殘局 → `JustLanded` 落地（YAGNI 閘門走完）＋`PresentationPipeline` 骨架（順序 6.5）＋Audio 三層（Event→Definition→Library）；Play 實測落地音正常。附 EditMode Warning 治理（RollState/JumpState 防線 `isPlaying` 語義精確化；測試契約輸出用 LogAssert.Expect＋鬆耦合 Regex）。
2. **M1 Locomotion ✅ 正式收案**（changelog v0.17 §5）：DoD 五項全過（0 error＋測試全綠／Play 實測／Profiler 0B／moveSpeedSource 接 Bake_Fast Run／Roll fade 資產真相驗證）。Locomotion 基線固定。
3. **M3 Foot IK 實作輪 ✅**（changelog v0.18）＋**M3.1 反饋迴路修正 ✅**（changelog v0.18.1）：實測腳踝抽搐 → Review 定位根因（Controller 採樣骨骼＝上一幀 IK 輸出，旋轉追逐＋權重鎖死雙迴路）→ 裁決雙管道修正——`FootIKController`（Root 決策，對 Animator 零依賴）⇄ 兩條單向管道（`FootIKTargetData` Controller 寫／`FootIKPoseData` Rig 寫）⇄ `FootIKRig`（Model，**Presentation Adapter**）。手填 footHeight 改讀 avatar `FeetBottomHeight`。抽搐複測通過、M3.5 基線已 push（2026-07-18）；**v1 已於 2026-07-21 凍結**（見頂部收案輪）。

---

## 待使用者作業

- **重編確認**：Unity 重編 0 error＋EditMode 測試 **42 條**全綠。收案輪程式改動＝旋轉公式一行還原＋刪 Editor-only 診斷段，不涉純函數／測試契約。
- **孤兒序列化值**（無害，Unity 靜默忽略）：`X Bot.prefab` 殘留 `debugLogGoals` 序列化值——同 v0.18.6 移除 `Enable*` flag 的既定情形，可在 Inspector 順手清、不清亦無影響。
- **資產側**（AI 不碰，SOP 由你在 Editor 執行）：牆壁 collider 過胖修正（身體碰不到牆）；CapsuleFitter Apply Prefab 確認；floor Scale Z 翻正（-25.153 → +25.153）若未做。**樓梯 collider 已修 ✅**。

---

## 工作清單

### Done（2026-07-18）
- [x] M2 全流程（黑板單幀事件 → 6.5 → Audio）＋收案；M1 DoD 收案；Warning 治理兩輪
- [x] M3 Foot IK：3 新檔＋Facade IK 通道＋`FootIKTests` 8 條＋Living Docs v0.18

### Doing
- [ ] **輪 2 資產盤點文件已產出 → `docs/04-locomotion-foundation.md`**（Kubold Movement Animset Pro：Catalog／Import Preset／Bake Strategy／Motion Feature Mapping／承載分析／工作拆分）。**等使用者核可 §8 五點方向後進 Phase A**（承載方式依實測定案，不預先拍板）。
- [ ] **鏡頭修復**：程式加了 Fail-Fast（target null 報錯）；**待使用者在場景**把角色 Root 拖入 Main Camera 的 `Third Person Camera.Target`，並把 `Mouse Sensitivity` 2→0.1。Cinemachine 為未來打磨選項（已裝 2.10.7），非本輪必要。

### Todo（輪 2，依 `docs/04` §7 拆分；核可後啟動）
- [ ] Phase A 地基＋loops：Import Preset（Idle→原地／Walk/Run/Sprint loop→位移）＋建 loops MotionBakeData 烘焙＋擴充 Mixer 至 Idle/Walk/Run/Sprint＋Facade 映射
- [ ] Phase B Foot Phase Curve 烘焙 stage（連續腳相，供選腳別）
- [ ] Phase C Starts/Stops/Turns 導入（烘焙曲線驅動＝Roll 先例）＋**承載方式實測定案**
- [ ] Phase D B9 參數平滑（資產定形後）

---

## Backlog / Future Work（超出目前範圍，不動手）

### Foot IK 品質路線圖 → 已凍結並移交 `docs/03-animation-roadmap.md`
- **v1 已凍結（收案輪，2026-07-21）**：架構健康＋6 條已知限制（L1~L6）文件化於 dev-spec §3.5.2；品質升級順序、技術分類、依賴關係全數移交 roadmap `docs/03`（輪 2 Locomotion 資產 → … → 輪 7 Foot IK v2 雙點採樣）。
- **~~首查項 GetIK* 值域~~ 已否證**：樓梯歪斜真凶＝斜坡 collider（環境資料錯誤，collider 修正後消失）；殘餘跨階腳掌穿模＝L1 單點採樣資訊量天花板，升級＝輪 7 Heel/Toe 雙點採樣。A/B 旋轉公式無感差、已回歸保留俯仰式（changelog v0.18.7）。
- ⚠️ 參考碼防搬運註記（仍有效，動 IK 前重讀）：其 raycast 從骨骼現值起打＝反饋污染（我們 M3.1 修掉的抽搐根因，快照 goal 起點勿退）；其 body 直接覆寫不適用（我們疊加式）；其漏設 RotationWeight 屬原 bug。骨盆模型重評（`bodyY − (minFootGoalY + legHeight)` 以腿長可達性直接建模）併輪 7 評估。

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

## 建議下一步 → 權威輪次順序見 `docs/03-animation-roadmap.md` §3

- **輪 2（＝既定 M4）購入 locomotion 資產**（Movement Animset Pro 級別）→ 左/右腳停步、pivot、方向性起步＋foot-phase 資料設計（烘焙管線加 analyzer 即可）；一併做 Foot Phase 烘焙 stage 與 B9 參數平滑（資產定形後）
- **輪 4 ArbiterPipeline**（順序 4.5 兌現；BlockIK/BlockAudio writer 到位、§7/§8.3 旗標粒度屆時有真實案例可答）→ Combat 前置
- **輪 6（＝既定 M5）Combat 初版（ARPG）**→ 產生 Hit/Death 等真正需要「封鎖」的狀態（前置：輪 4 Arbiter＋輪 5 Upper Body Layer）
- 表情模組：暫緩（X Bot 無臉部 rig）
