# Animation Runtime Roadmap（技術樹與開發順序）

> **定位**：規劃文件（Planning Doc）。本檔只承載「評估、分類、排序」——不承載架構決策。
> 任何項目實際落地前，仍走既定路由（`CLAUDE.md`）：非架構變更 → 直接寫 Living Docs（`01-design-doc.md`／`02-dev-spec.md`）；系統性破壞式變更 → 開新 ADR。
> **緣起**（2026-07-20）：Foot IK 多輪實測後，使用者裁決停止單點細節修補、確立設計哲學（Natural Pose > Terrain Adaptation > Perfect Foot Contact），並要求以整體 Animation Runtime 演進取代單一 IK 深挖。本檔為該裁決的產物。
> **編號註記**：本檔以「輪次」排序，避免與 WORKLOG 的 M4（購入資產）／dev-spec §3.5.3 的「M4+ 品質升級」兩處編號語義衝突。

---

## 1. Foot IK v1 凍結評估

### 1.1 判定：**可凍結（v1 = M3.5 基線＋收案輪小修）**

理由：架構層全部健康、剩餘問題全數屬「已知限制」而非缺陷，且每條限制都有不需改架構的升級路徑。繼續投入的邊際收益（腳掌貼合精度）遠低於同等時間投入其他子系統的收益——這正是 M3.2~M3.4 實驗輪用實證換來的結論（單點＋權重補丁已到天花板）。

### 1.2 架構健康檢查（結論：無架構缺陷）

| 檢查項 | 狀態 | 依據 |
| --- | --- | --- |
| 模組邊界 | ✅ | Controller 對 Animator 零依賴；Rig＝Presentation Adapter 零判斷（dev-spec §3.5.1） |
| 資料流 | ✅ | 雙管道各自單寫單讀；反饋禁令成立（腳踝抽搐根因已根治，EditMode 測試 42 條） |
| 管線契約 | ✅ | 順序 6.5 時序、單幀事件窗口、`IsWarm` 防線全數遵守 |
| 零 GC | ✅ | 一次性配置＋stack 採樣結構，熱路徑無 `new` |
| 擴充預留 | ✅ | `BlockIK` 讀取契約先行（等 Arbiter）；參數集中 `FootIKSettings` |
| 外部 API 風險 | ✅ 已否證 | M3 交付時標註的「`GetIK*` 在 Playables 下值域」疑慮，經 2026-07-18 診斷數據排除（goal 位置≈骨骼 0.002m、旋轉健康）——dev-spec §3.5.3「首查項」待同步更新 |

灰色地帶（架構乾淨、觀感有天花板，記錄非缺陷）：IK 是純視覺層，`CharacterController` 膠囊高度不隨骨盆補償變動——階梯邊緣站姿的懸浮感上限由「膠囊幾何 × `MaxPelvisOffset`」共同決定，屬兩系統邊界已定義下的固有天花板。

### 1.3 已知限制（可接受，文件化後凍結）

| # | 症狀 | 根因 | 歸類 | 升級路徑（不改架構） |
| --- | --- | --- | --- | --- |
| L1 | 階梯上腳掌中段穿入上一階（2026-07-20 實測，collider 修正後仍存） | **單點採樣資訊量天花板**：ray 只打腳踝下方、命中所在踏面；腳掌前段（~25cm）跨入上一階體積，系統無從得知 | 已知限制 | Heel/Toe 雙點採樣（輪 7）：僅動 `SampleGround`／`ResolveFoot` 內部＋Settings，雙管道／Ownership 全不動 |
| L2 | toe-off 蹬地相腳尖少量穿模 | 動畫原生腳尖下壓 | **設計接受**（哲學 P1 > P5） | 不修 |
| L3 | 左右腳高差 > `MaxPelvisOffset` 時低腳懸空 | 骨盆補償夾限的設計極限 | 已知限制 | 骨盆模型重評（腿長可達性直接建模，輪 7 選項） |
| L4 | IK 結果一幀延遲 | Humanoid IK 快照架構本質（dev-spec §3.5.2 已文件化） | 已知限制 | 無需處理（60fps 不可察） |
| L5 | 腳貼近階梯立面時 ray 誤中上一階頂（「憑空踩半階」） | raycast origin 幾何（`RaycastUpOffset` 高於台階） | tuning 域 | 乾淨 collider 基線上調參（`RaycastUpOffset`／`RaycastDistance`），非程式碼問題 |
| L6 | A/B 旋轉公式（軸對齊 vs 保留俯仰）實測無感差 | 踩地相動畫俯仰本來就小（平地夾角 ~2°） | 收案項 | 依哲學（腳踝自由旋轉、不強制壓平）回歸 git 基線「保留俯仰式」，軸對齊式歸檔 |

### 1.4 凍結執行清單（✅ 程式＋文件側已於 2026-07-21 執行，見 changelog v0.18.7）

1. ✅ 程式碼：還原 `ResolveFoot` 旋轉公式為 git 基線（保留俯仰式）；刪除 `FootIKRig.debugLogGoals` 臨時診斷段。
2. ✅ `changelog.md` v0.18.7：樓梯 collider 根因（**教訓：IK 疑難先驗 collision 幾何**——28° 歪斜實為斜坡 collider 坡角）、A/B 結論、設計哲學轉向、v1 凍結宣告。
3. ✅ `01-design-doc.md`：Foot IK 段（§4.6）補「設計哲學」（五優先級＋活動空間檢核問句）。
4. ✅ `02-dev-spec.md` §3.5：補 v1 凍結狀態（intro）＋已知限制表 L1~L6（§3.5.2）；§3.5.3「首查項（GetIK* 值域）」標記已否證。
5. ✅ `WORKLOG.md`：Foot IK 品質路線圖段改為指向本檔。
6. ⏳（使用者，資產側）牆壁 collider 過胖修正；樓梯 collider 已於 2026-07-20 修正 ✅。

---

## 2. 技術分類

> 每項五欄：解決的問題／與現架構相容性／是否需改架構／作品集亮點／建議導入時機。
> **既有地基**（後續全部技術的承載面，多數技術因此是「加法」而非「改法」）：黑板＋單幀事件（M2）、PresentationPipeline 介面收集（新 Controller 零 Runner 改動）、TransitionAsset／Mixer（v0.16）、烘焙管線 `AnimationBuildPipeline`＋`BuildCache`（IBuildStage 可插拔）、雙管道 IK 模式（M3.1，可複製）、Adapter Root/Model（ADR-001）、`StateParamsSO`（v0.10）、FBX 直引治理（v0.16.1）。

### 2.1 核心架構（Architecture Foundation）——「沒有它，後面的功能會腐蝕架構」

| 技術 | 解決的問題 | 相容性 | 需改架構？ | 作品集亮點 | 導入時機 |
| --- | --- | --- | --- | --- | --- |
| **F6 ArbiterPipeline**（順序 4.5） | 狀態 → 表現層封鎖的單一決策點（Hit/Death 時 BlockIK/BlockInput），根除「Controller 各自讀狀態」的耦合 | ★★★ 架構本來就為它留洞：順序 4.5 預留、`ArbiterData` 規格已寫（dev-spec §1.4）、`BlockIK` 讀端已接 | 否——是把既定架構**補完** | 中高（旗標解耦 vs 狀態耦合的架構敘事，design-doc §2.5 已有完整論述） | 輪 5 前夕（第一批真實 Block* 用例＝Combat 出現時，符合 YAGNI 紀律） |
| **F4 Upper Body Layer**（Animancer Layers＋Avatar Mask） | 上身動作（持武器／攻擊／換彈）與下身 locomotion 並行 | ★★☆ Animancer Pro 已解鎖 Layers；Facade 已有 `SetLayerWeight` 雛形；design-doc §2.3 本就規劃三層 | **部分**——上身狀態的驅動方式（第二狀態機 vs Facade 直驅）是 design-doc §7 開放問題，需裁決（可能開 ADR） | 高（分層混合＋打斷優先級是 3C 標配能力） | 輪 6（Combat 需要上身攻擊時） |
| **Animation Event 管道標準化**（TransitionAsset 序列化事件） | 動畫時間軸事件（腳步時點）→ 玩法／音效的標準通道，不違反「clip 不可變」治理 | ★★★ v0.16.1 決策已預留此通道；Audio 三層（M2）是現成消費端 | 否（Presentation 層內） | 中（治理一致性的展示） | 輪 3（Footstep 輪） |
| **Foot Phase Curve 烘焙**（`FootPhase` 採樣 stage） | 腳相資料（哪腳著地／相位）供 Footstep／Foot Contact／未來同步使用 | ★★★ 烘焙管線 IBuildStage 可插拔；dev-spec §5 既定未完成項（v0.7 Code Review 發現） | 否——烘焙管線延伸 | 高（離線特徵提取＝本專案差異化敘事的延續） | 輪 2（新資產進場需重烘焙，一次做掉） |

### 2.2 Gameplay 必要功能——「把『能動』變『像遊戲』」

| 技術 | 解決的問題 | 相容性 | 需改架構？ | 作品集亮點 | 導入時機 |
| --- | --- | --- | --- | --- | --- |
| **Locomotion 資產升級**（起步／停步／pivot／急停，Movement Animset Pro 級） | 現行 Idle↔Move 直接混合的「滑行感」；左右腳感知的方向性起停 | ★★★ Mixer／TransitionAsset／烘焙管線全就緒；新狀態走 ADR-002 既定模式（Living Doc 路由） | 否 | 中（觀感提升大、技術敘事一般——但它是後面 Distance Matching 的前提） | **輪 2（下一個大輪，＝WORKLOG 既定 M4）** |
| **F2 Strafe 2D Mixer** | 鎖定／瞄準模式的八向橫移 | ★★★ Animancer 2D Mixer 就緒；threshold 公式 B11 屆時依雙 Gate 重評自動化 | 否 | 中 | Combat 鎖定需求出現時（輪 6 內或後） |
| **Combat 初版**（攻擊 combo／受擊／死亡，＝WORKLOG 既定 M5） | 真正的封鎖用例（Block*）、「一狀態 → 多鍵」映射的兌現、上身層需求來源 | ★★★ 狀態機 Priority／打斷規則就緒；`StateParamsSO` 承載攻擊參數 | 否（前置：Arbiter＋UpperBody） | 高（完整 3C＋Combat 的閉環敘事） | 輪 6 |
| **Motion Warping**（`ApplyBakedCompensation` 兌現） | 攻擊貼靶、翻越 1.5m 矮牆、精準閃避的位移對齊 | ★★★ **規劃最完整的未來項**：`WarpedMotionExtractor` 規格已寫（dev-spec §4.2）、MotionDriver 雛形已在、第四五階段清單既定 Demo | 否（MotionDriver 擴充） | **極高**（AAA 關鍵字 × 自研烘焙管線閉環——「離線特徵 → 執行期扭曲」全鏈路自建） | 輪 6 後段～輪 7（攻擊對齊需求出現時） |

### 2.3 品質提升（Quality Improvement）

| 技術 | 解決的問題 | 相容性 | 需改架構？ | 作品集亮點 | 導入時機 |
| --- | --- | --- | --- | --- | --- |
| **Foot IK v2：Heel/Toe 雙點採樣** | 凍結限制 L1（跨階腳掌穿模）——腳尖處地形資訊納入 pitch | ★★★ 演算法內部擴充（§1.3 L1 欄） | 否 | 中（常見技術，展示完成度） | 輪 7（品質輪；穿模觀感無法忍受時可提前） |
| **Foot Contact 狀態機**（plant/lift 事件） | 腳步事件源（footstep 音）＋plant 期鎖定（消滑步） | ★★★ 吃 Foot Phase 烘焙資料；事件走黑板單幀事件範本（`JustLanded`） | 否 | 中高 | 輪 3 初版（音源）＋輪 7 完整版（plant 鎖定） |
| **B9 動畫參數平滑**（MoveSpeed SmoothDamp） | 鍵盤 0/1 輸入的 Mixer 跳變 | ★★★ 既定 backlog | 否 | 低 | 輪 2 一併（起停步資產會改變此處需求形態，先資產後平滑） |
| **Distance Matching**（停步／落地距離匹配）〔本輪新提案，採納待裁決〕 | 停步滑行、落地穿插——依「剩餘距離」對齊動畫播放時間 | ★★★ 烘焙管線天作之合：SpeedCurve 積分即 distance 曲線，或烘焙直接輸出 | 否（State／MotionDriver 內） | **高**（AAA 關鍵字 × 管線閉環，成本遠低於 Motion Matching） | 輪 7（停步資產進場後才有意義） |
| **Look At（頭部視線 IK）** | 角色「活著」的觀感（看目標／鏡頭方向） | ★★★ Humanoid `SetLookAt*` 便宜；複製 M3.1 Controller＋Rig 管道模式；零 Runner 改動 | 否 | 單項低、整體觀感貢獻高 | 任意輪穿插（低成本高觀感） |

### 2.4 AAA Polish（進階選修）

| 技術 | 解決的問題 | 相容性 | 需改架構？ | 作品集亮點 | 導入時機 |
| --- | --- | --- | --- | --- | --- |
| **Inertialization（慣性化過渡）**〔新提案〕 | cross-fade 的「混合糊感」——轉場以速度銜接取代姿勢插值（TLOU2／Gears 級） | ★☆☆ **需先調研** Animancer v8 支援度；若無內建＝侵入動畫求值層的自研（骨骼後處理），須架構評估 | 視調研結果（可能要 ADR） | **極高**（若自研成功＝深度技術敘事） | 輪 8+，核心玩法完備後專門輪；調研先行 |
| **Stride Warping**（步幅縮放） | 速度連續變化下步幅／步頻的自然適應 | ★★☆ 需 IK 層＋根速度協同 | 否（IK 子系統延伸） | 高 | 輪 8+ |
| **Ragdoll Blending / Hit Reaction** | 受擊物理混合、死亡過渡 | ★★☆ Model 層擴充（ADR-001 預留掛點）；需 physics rig＋blend 機制 | 否（Model 層內） | 中高 | Combat 成熟後（輪 8+） |
| **全身地形適應**（骨盆側傾／脊柱） | 斜坡上半身觀感 | ★★★ IK 子系統延伸 | 否 | 中 | 輪 8+ |
| **Motion Matching** | 狀態機選片的根本限制（資料驅動選片） | ★☆☆ **與現行 StateMachine＋Mixer 是替代關係**：locomotion 表現層整個換掉；需動捕級資料庫 | **是——真正的架構變更**，採納必開新 ADR（supersede v0.16 Locomotion 決策） | 極高，但成本爆炸 | **研究型支線，不進主線**；v2.0 願景層再議 |

---

## 3. 技術樹（依賴關係）

```
既有地基（黑板／管線／烘焙／Facade／雙管道 IK 模式）
│
├─ 輪 1【收案】Foot IK v1 凍結（§1.4 清單）
│
├─ 輪 2【資產】Locomotion 升級（起停步/pivot）＝既定 M4
│    ├─ Foot Phase Curve 烘焙 stage（順路一次做）
│    └─ B9 參數平滑（資產定形後）
│         │
│         ├─ 輪 3【事件】Footstep 輪
│         │    ├─ Animation Event 管道標準化（TransitionAsset 事件）
│         │    ├─ Foot Contact 初版（plant/lift 事件源）
│         │    └─ Footstep Audio（複用 M2 三層）
│         │
│         └─ 輪 7 Distance Matching（吃停步資產＋烘焙 distance 曲線）
│
├─ 輪 4【架構】F6 ArbiterPipeline（小輪）──┐
├─ 輪 5【架構】F4 Upper Body Layer ────────┤（Combat 前置）
│                                          ▼
├─ 輪 6【玩法】Combat 初版＝既定 M5（Block* 真實用例）
│    ├─ F2 Strafe 2D（鎖定移動需求出現時）
│    └─ Motion Warping（貼靶／翻越＝既定第四五階段 Demo）
│
├─ 輪 7【品質】Foot IK v2（Heel/Toe 雙點＋Contact 完整版＋骨盆模型重評）
│    ／Distance Matching／Look At（可穿插）
│
└─ 輪 8+【AAA 選修】Inertialization（調研先行）／Stride Warping
     ／Ragdoll Blending／全身地形適應
     ⚠ 研究支線（不進主線）：Motion Matching（採納＝新 ADR）
```

### 建議開發順序（主線一句話版）

| 輪 | 內容 | 性質 |
| --- | --- | --- |
| 1 | Foot IK v1 收案（§1.4） | 小輪，止血收口 |
| 2 | Locomotion 資產升級＋Foot Phase 烘焙＋B9 | 大輪＝既定 M4；資料驅動管線吃真實資產集的壓力測試 |
| 3 | Footstep 輪（Event 管道＋Contact 初版＋Audio） | 中輪；三個子系統天然同捆 |
| 4 | F6 Arbiter | 小輪；架構補完 |
| 5 | F4 Upper Body Layer | 中輪；含驅動方式裁決 |
| 6 | Combat 初版（＋Strafe 2D／Motion Warping 隨需求） | 大輪＝既定 M5 |
| 7 | 品質輪（Foot IK v2／Distance Matching／Look At 擇項） | 中輪；回頭收割 |
| 8+ | AAA 選修（Inertialization 調研先行） | 專門輪 |

**作品集亮點投報率排序**（本專案差異化敘事＝「自研烘焙管線＋資料驅動配置」，吃烘焙資料的執行期技術 payoff 最高）：
1. **Motion Warping**（管線閉環的直接兌現，規劃已最完整）
2. **Distance Matching**（同款敘事、成本更低）
3. **Inertialization**（若調研可行——深度最高）
4. **Upper Body Layer＋Arbiter**（架構敘事：設計文件與 ADR 本身就是展品）
5. Foot IK v2／Foot Contact（完成度展示）

---

## 4. 待裁決點（各輪落地前需討論，本檔不預答）

1. **輪 2**：起步／停步／pivot 的狀態承載——新增 StateType vs Mixer 內部處理（影響 FSM 拓撲與映射表）。
2. **輪 3**：Animation Event 的承載選擇——TransitionAsset 序列化事件 vs 黑板事件擴充的分工線（哪類事件走哪條）。
3. **輪 4**：仲裁旗標粒度與多來源合併規則（design-doc §7 既有開放問題）。
4. **輪 5**：上身層驅動方式——第二個 StateMachine vs Facade 層 API；上身／全身打斷優先級（design-doc §7 第一條）。
5. **輪 7**：Distance Matching 採納與否（本檔新提案）；Foot IK v2 的骨盆模型是否一併重評。
6. **輪 8+**：Inertialization 調研結論的架構評估；Motion Matching 是否升格為 v2.0 願景（若是＝新 ADR）。

## 5. 遺留小項（資產側，使用者自理）

- 牆壁 collider 過胖（2026-07-20 發現：身體碰不到牆，視覺與碰撞邊界差距過大）。
- CapsuleFitter Apply Prefab 確認；floor Scale Z 翻正（-25.153 → +25.153）若未做。
