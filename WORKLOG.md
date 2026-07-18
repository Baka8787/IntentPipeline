# WORKLOG

> 唯一的進度管理文件。每完成一項立即更新。
> 歷史架構決策請看 `docs/changelog.md` 與 `docs/ADR/`；此檔只管「現在手上的工作」。

---

## 今日進度（2026-07-18）

三輪連發，詳見 changelog v0.17／v0.18：

1. **M2 Presentation Pipeline + Landing Audio ✅ 收案**（changelog v0.17）：修復前 session 幻覺殘局 → `JustLanded` 落地（YAGNI 閘門走完）＋`PresentationPipeline` 骨架（順序 6.5）＋Audio 三層（Event→Definition→Library）；Play 實測落地音正常。附 EditMode Warning 治理（RollState/JumpState 防線 `isPlaying` 語義精確化；測試契約輸出用 LogAssert.Expect＋鬆耦合 Regex）。
2. **M1 Locomotion ✅ 正式收案**（changelog v0.17 §5）：DoD 五項全過（0 error＋測試全綠／Play 實測／Profiler 0B／moveSpeedSource 接 Bake_Fast Run／Roll fade 資產真相驗證）。Locomotion 基線固定。
3. **M3 Foot IK 實作輪 ✅ 程式碼＋文件完成**（changelog v0.18）：`FootIKController`（Root 決策）→`FootIKRuntimeData`（單寫單讀管道）→`FootIKRig`（Model Thin Executor）；Unity Humanoid IK＋Pelvis Compensation；Runner 零改動（骨架首次回收驗證）。**⏳ 等待使用者 Unity 接線＋斜坡/台階實測**（見下方）。

---

## 待使用者作業（M3 收尾）

- [ ] 重編確認 0 error＋EditMode 測試 **42 條**全綠（34＋FootIKTests 8）
- [ ] X Bot Prefab：**Model 子物件**（有 Animator 那顆）掛 `FootIKRig`；**Root** 掛 `FootIKController`（Inspector 確認 `groundLayers` 含地形 Layer）
- [ ] 場景搭斜坡（~10°/20°）＋台階（~0.1/0.2m）測試區（平地看不出 IK 效果）
- [ ] Play 驗收：平地不變形／斜坡雙腳貼合＋骨盆下沉／台階邊緣腳不懸空；Roll、Jump 目視無吸地異常（Q4 驗證點）；Profiler 玩法路徑 GC 維持 0B
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
