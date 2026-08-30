# WORKLOG

> 唯一的進度管理文件。每完成一項立即更新。
> 歷史架構決策請看 `docs/changelog.md` 與 `docs/ADR/`；此檔只管「現在手上的工作」。

---

## 🔖 交辦（下一會話 Handoff）

> ### 📍 2026-08-31 交接（最新，請先讀這段）
>
> **一句話**：Foot IK 軌 A 已結案；主線仍卡在 **ADR-004 的 Unity 資產側與 Play 驗收**（使用者側），程式面沒有待辦。
>
> **① 本輪完成了什麼**
> - **Foot IK L1 → Level 1 rigid sole approximation，視覺驗收暫時通過**（`MaxFootAlignAngle = 23°`）。經歷四版迭代（取較高者 → 泰勒斯＋殘差 → 踝角夾限 → 真實端點 residual），順帶修掉 M3.3 就存在的腳踝水平漂移舊 bug。**durable 紀錄在 `docs/05` §3.5.5**，`docs/03` §1.3 的 L1 已結案、殘餘誤差降級為 L7。
> - **Scope 收斂**：軌 A ＋ WP1–WP4 四個工作包、九段影片對照表、Codex 交辦邊界、git commit 切點 —— 全在下一節「Scope 收斂與後續四包」。
> - FU-1／FU-2／FU-3（Action→Action 中斷不可能／一角色一份 Definition／mailbox 無身分）已寫入 `docs/08` §11.1；ADR-004 §8 補 **L4**。
>
> **② 現在該做什麼**
> - **不要動 Foot IK**。重開條件見 `docs/05` §3.5.5（五條，任一成立才重開）。繼續調之前**必須先做 FU-IK-2 可視化**。
> - **主線的停止線沒有變**：ADR-004 `Trial → Accepted`。等使用者完成資產接線與 Play 驗收，程式面無事可做。
> - 使用者說要開 **WP1（鏡頭 ＋ Aim ＋ Throw 依 AimPoint）** 時：先出 `docs/09-camera-aim.md` 規格，再派 Codex。**WP1 不開 ADR**（純 Presentation、黑板 schema 零改動）。
>
> **③ 環境與工具（本輪新增）**
> - **Codex 可直接呼叫**：`.mcp.json` 已設定（`cmd /c codex mcp-server`，`sandbox_mode=workspace-write`／`approval_policy=never`），工具為 `mcp__codex__codex` ／ `mcp__codex__codex-reply`。⚠️ **thread 會過期**（實測掉過一次），過期就開新 session 並補完整脈絡。
> - **Codex 沙箱與批准權在它那側**，MCP 層無法強制紀律 ⇒ 「不碰 git、不碰資產、檔案白名單」**必須每次寫進 prompt**，光靠 `AGENTS.md` 不夠。
> - 使用者**已授權**直接調用 Codex。
>
> **④ 工作樹狀態（未 commit，git 全由使用者執行）**
> - Foot IK v4（3 檔）、ADR-004 Trial 的 P1／P2 程式與資產、Free Sword Animations 素材、本輪全部文件。
> - commit 切點建議見下方「🌱 Git commit 切點」一節。
>
> **⑤ 這輪學到的、值得下一個會話沿用的做法**
> - **先形式化約束模型再改程式**。Foot IK 繞了四版，前三版都是因為沒把「誰決定位置／誰決定旋轉／誰決定接觸」講清楚就動手。
> - **要求可否證的預測**：改動前先算出「若這個假設成立，你會看到 X cm 的偏差」，Play 才是測試而不是觀感投票。
> - **觀測條件 ≠ 交付條件**。Scene 視窗貼著腳看到的瑕疵，在 3–4m 鏡頭下不存在。判斷是否值得修時先問這個。

> ⬆️ **最新狀態（2026-08-29）＝優先順序已改變：「完善作品集」升為最高優先，高於 `docs/03-animation-roadmap.md` 的工程路線圖。**
> 進行中的工作＝下方「作品集最低限度衝刺」。Phase C1／C1.1（Walk＋Run Stop）已完成驗收並歸檔；`docs/08-skill-system.md` 設計稿已完成但**展示題材已改**（Punch → Throw，理由見下）。
> 📍 **會話開場請先讀 `docs/00-map.md`**（單頁索引：模組 → 檔案 → 治理章節），再讀本段。
> 📍 敘事與展示策略見 `LearningNotes/portfolio-framing.md`（不納入 `docs/00–NN` 工程編號）。
> 🆕 **2026-08-30 scope 收斂**：見下一節「Scope 收斂與後續四包」。**當前輪次的唯一任務是讓 ADR-004 拿到 `Accepted`**；
> 該節同時載明軌 A（可立即並行）、WP1–WP4、影片段落對照、Codex 交辦邊界與 git commit 切點。
> 原「作品集最低限度衝刺」的 P1／P2 仍是現行工作；**P3／P4／P5 已被拆進 WP2／WP3／WP4 與軌 A**（見該表下方註記）。

---

## 🧭 Scope 收斂與後續四包（2026-08-30 planning review）

> **為什麼有這一節**：實作過程持續暴露新問題，出現「發現一個問題就順手解下一個」的漂移。本節把問題分桶、
> 把後續切成**可獨立 qualification** 的工作包，並明確寫下**停止線**。
> **兩條評估軸並用**（缺一不可）：
> - **🏗 Architecture Qualification**：這包證明什麼技術能力？驗收＝測試／不變量／零 GC／「加第三個 X 是零程式」。
> - **🎬 Portfolio Qualification**：觀眾實際看到什麼？沒有它，影片缺哪一段？驗收＝錄影段落／外行能否複述。
>
> ⚠️ **分類規則**：一個項目只要在**任一條軸**上是必要的，就**不得**進 polish 桶。
> 只有兩條軸都只是加分的才是 polish（現存 5 項：震屏、hit stop、aim friction 微調、相機碰撞避讓、projectile 物件池）。

### 🎬 展示規格（取代舊的「15–20 秒 demo」）

- **主展示：約 1:45–2:00**，重點是**讓觀眾有時間理解「這套系統在實際遊戲中帶來什麼」**，不是塞更多功能。
- **Teaser：15–30 秒 ＝ 主展示的剪輯輸出**（段落 2／3／6／8 各取數秒），**不是額外工作包、不需要新內容**。

| # | 段落 | 秒數 | 交付者 | 沒有它，觀眾看不到 |
| --- | --- | --- | --- | --- |
| 1 | 探索移動（地形／tier／Stop 選片／腳步音／Foot IK／探索鏡頭） | 0:00–0:15 | **軌 A ＋ WP1** | 專案最厚的既有工作。這是唯一能把「隱形的正確」變可見的機會 |
| 2 | 鏡頭切換／瞄準 | 0:15–0:25 | **WP1** | 「這是個有戰鬥的遊戲」的第一個訊號 |
| 3 | 遠程 Throw ＋ soft target | 0:25–0:35 | **WP1** | 球會飛 ≠ 會瞄；缺這段投擲看起來像亂射 |
| 4 | 敵人接近 → Telegraph → 近戰出手 | 0:35–0:52 | **WP3** | 敵人是威脅而不是靶子 |
| 5 | 玩家閃避 | 0:52–0:58 | 既有 Roll ＋ WP3 | Roll 早就做完了，但沒有攻擊可閃時它只是翻滾動畫 |
| 6 | 玩家近戰揮劍 | 0:58–1:10 | **WP2** | **遠程／近戰對比**；缺它整個 demo 就是 projectile prototype |
| 7 | Action Mapping／不同角色動作 ＋ 改 SO 即改行為 | 1:10–1:30 | **WP2** | 架構價值唯一能被**看見**的一段（全片最重要的 20 秒） |
| 8 | 受擊與中斷（Throw 斷 Telegraph／揮劍被打斷） | 1:30–1:45 | **WP2 機制 ＋ WP3 Play** | 雙向互動成立 |
| 9 | 遭遇結束 | 1:45–2:00 | **WP3 ＋ WP4** | 有結局的遭遇 vs 沒剪完的錄影 |

### 🛑 當前輪次的停止 checkpoint（**這是硬線**）

**停在 ADR-004 `Trial → Accepted` 的那一刻。** 全部成立才算到線，一件不多：

1. 資產與接線完成（Throw／Damage 的 transition mappings、Bake、兩份 Definition、Config 的 Action rules、`ThrownProjectile` trigger collider、敵人 prefab、NavMesh 烘焙）。
2. ⚠️ **`playerCamera` 欄位或 MainCamera tag 必須確認存在**——`AIMovementSource` 在 `data.CameraTransform == null` 時**直接 return**，症狀是「敵人靜止不動」且**沒有任何錯誤訊息**。這條放進驗收清單，不要現場 debug。
3. G1／G2／G3／G6 打勾。
4. ADR-004 §10 的 **A–F 逐條回填**，特別是 **F（實作沒有逼出第二套 authority 或明顯 workaround）**。

**停止線之後、WP1 開工之前不做**：任何相機改動、任何 aim、任何第二份 Definition、任何敵人攻擊。
**新發現一律只登記不處理** → FU-1／FU-2／FU-3 已寫入 [`docs/08` §11.1](08-skill-system.md)；FU-4～FU-10 見下方登記表。

### 🅰️ 並行軌 A（舞台）——**現在就能開始**，不佔 sequential 順位

不 gate 也不被 gate，且大部分是使用者側資產工作，可與 ADR-004 收尾同時進行。
**做它的理由**：段落 1 的唯一來源；而且 P1／P2 的 Play 驗收在有斜坡／樓梯的場地上做，比在空地上做有意義得多。

- **🏗** 無（唯一程式項＝Foot IK L1 Heel/Toe 雙點採樣，**只准動 `SampleGround`／`ResolveFoot` 內部＋Settings**）。
- **🎬** G7 場景像關卡、G4 Foot IK 可 A/B。**沒有斜坡與樓梯，Foot IK 整塊工作在影片裡等於不存在。**
- **Scope**：斜坡／樓梯／障礙／一個明確目標點；Foot IK L5 調參；Foot IK L1 雙點採樣。
- **Non-goals**：關卡美術、光照打磨、導航以外的互動物件。
- 🛑 **停止條件**：Foot IK L1 需要動到 `SampleGround`／`ResolveFoot` 以外的任何東西 ⇒ 停並回報（`docs/05` §3.5.1／A11 邊界）。

#### 🎫 Ticket 軌A-IK-L1：Heel/Toe 雙點採樣 —— ✅ **結案（2026-08-31）**

> **結論：Level 1 rigid sole approximation，視覺驗收暫時通過。本輪 Foot IK 停在此狀態，不再繼續修。**
>
> - `MaxFootAlignAngle` **暫定 23°**（原 15°，調整後視覺明顯自然）
> - toe-up 姿態**有保留**
> - 嚴重穿模**已消失**
> - 剩餘 heel／sole 浮空**不明顯**（近距離側視才可見），**低於 must-fix 門檻**
> - Walk／Run 若無明顯跳動即視為 Level 1 驗收通過
> - ⛔ **不再為了追 0 浮空繼續提高 clamp 或擴大模型**
>
> 📄 **durable 紀錄已落到 [`docs/05` §3.5.5](05-foot-ik.md)**：約束模型三層分工、三次推翻的教訓、Level 1 已知限制、
> Level 1／2／3 升級階梯、FU-IK-1～3 follow-up、以及**重開 Foot IK 的五條條件**。
> `docs/03` §1.3 的 **L1 已結案**，殘餘誤差降級為新增的 **L7（設計接受）**。
>
> **後續待辦（follow-up，不現在實作）**：**FU-IK-1** 量準 `HeelOffset`／`ToeOffset`（現值非正式量測值；它們是**腳底幾何常數，不是手感旋鈕**）／
> **FU-IK-2** 最小 Foot contact 可視化（Gizmo ／ `Debug.DrawRay`，⛔ 不建 Debug Framework）／
> **FU-IK-3** 有可視化後重新量測並**只准再調一次** clamp。⚠️ **繼續調 Foot IK 之前必須先做 FU-IK-2，不要再靠 Scene 視窗肉眼猜。**
>
> ⏭️ **軌 A 的 Foot IK 部分到此結束；回主線 WP1（鏡頭 ＋ Aim ＋ Throw 依 AimPoint）。**

**問題**（`docs/03` §1.3-L1）：ray 只打腳踝下方、命中該踏面；腳掌前段（~25 cm）跨入上一階體積時系統無從得知 ⇒ 階梯上腳掌中段穿入上一階。**單點採樣的資訊量天花板**，不是參數沒調好。

**不可違反（設計哲學鐵律，優先於本 ticket 的一切目標）**
- **Natural Pose > Terrain Adaptation > Perfect Foot Contact。接受少量腳尖穿模，不接受為修穿模讓動作僵硬。**
- ⛔ **不得**用 Fade／Gate／降權重解決——貼地品質一律走 **Ground Sampling 升級**。
- 檢核問句必須答「否」：**這個機制會不會縮小角色原本的活動空間（抬腿／跨步／轉向）？**

**做法（建議；細節屬實作自由）**
1. `SampleGround` 由 1 條 ray 改為 **2 條**：heel＝`posePosition - footForward * HeelOffset`、toe＝`posePosition + footForward * ToeOffset`，`footForward` 取 `poseRotation * Vector3.forward` 水平化。
2. **`FootSample` 的對外形狀盡量不變**——`GroundY` 取**兩點中較高者**（防穿模）。如此 `ResolveFoot` 的目標式與 `ComputePelvisOffset` 的骨盆邏輯**零改動**。
   - 🔧 **2026-08-30 實作裁決（原文有歧義，已定案）**：`HitPoint` **只取較高命中的 Y 與法線，XZ 保留動畫 pose goal**。照搬較高命中的完整 XZ 會把腳踝水平拉向 heel 或 toe，**平地就會違反「逐字不變」**，也違反 Natural Pose 優先。
3. **旋轉：完全不動，逐字保留 M3.1 的 `FromToRotation(Vector3.up, sample.Normal) * poseRotation`。**
   - 🔴 **2026-08-30 Play 驗收後的設計更正（原文第 3 點「由兩點高度差求 pitch」已作廢）**：
     實測樓梯上整隻腳被扳斜近 40°。根因是 heel 與 toe 打在**兩個不連續的平面**（上階／下階踏面），
     程式把高差當成坡度：`span = 0.25m`、踢面 `0.2m` ⇒ `atan2(0.2, 0.25) ≈ 39°`。
     **兩個踏面各自都是平的、法線都是 up，卻被合成出一個不存在的斜面。**
   - 更根本的問題：**連續斜面上 pitch 早已由命中法線提供**（`FromToRotation(up, normal)` 就是在做這件事），
     heel/toe 高差只是把同一資訊算第二遍；而在階梯上它算的根本不是坡度。
     ⇒ **pitch 這個來源在兩種地形上，一種多餘、一種錯誤。**
   - ✅ **裁決（使用者，2026-08-30）：雙點採樣只決定「高度」，不決定「旋轉」。**
     `GroundY`／`HitPoint.Y`／`Normal` 取**較高命中**——**穿模由「把腳抬到較高的面」解決，不由旋轉解決**。
     這與既有骨盆規則同一哲學：**地面只能把腳往上頂，不能把腳往下拉**（`ComputePelvisOffset` 的「只下沉不上頂」）。
   - 📌 **代價（已接受）**：上樓梯時腳尖不會主動翹起去貼上一階；腳尖懸空時保持平貼較高踏面。
     依鐵律 **Natural Pose > Terrain > Contact**，這正是預期行為，不是缺陷。
   - 📌 **本輪 L1 沒有 EditMode 覆蓋**（取兩者較高＝`Mathf.Max`，不值得為了「有東西可測」而抽純函數）。
     驗證誠實地落在 Play。
4. **退化路徑**：任一 ray 落空 ⇒ **退回現行單點行為**。⛔ 不得因落空而關閉 IK 或降權重（那就是被禁的 Gate）。
5. `FootIKSettings` 新增 `HeelOffset`／`ToeOffset`（公尺）＋一顆 `UseTwoPointSampling` 布林。**該布林只用於 A/B 展示與退回基線，不得成為執行期的品質開關。**

> 🔄 **以上 1–5 為初版構想，已被實作推翻兩次。以下是現行設計（v3，🟡 待 Play 驗證）。**

#### ~~現行設計 v3（2026-08-30，三輪迭代後）~~ —— 已被 v4 取代，僅存演進紀錄

> 🔄 **v4（2026-08-31）取代 v3 的 residual**：v3 仍以「地面 vs 假想平面」求穿透，算式裡沒有腳的幾何，
> 因此任何動畫 pitch 對該約束都是**隱形**的（平地 ＋ toe-up 20° 時腳跟穿地 2.8cm 而 residual 恆回 0）。
> v4 改為由最終旋轉 `R` 算出 heel／toe **真實端點世界座標**、在該處打 ray、點對點比較。
> **完整的現行設計見 [`docs/05` §3.5.5](05-foot-ik.md)**；以下保留 v3 內容僅為演進脈絡。

**演進**：v1「雙點取較高者當高度」→ 斜坡浮空（誤差＝上坡側取樣距離×tanθ，30° 約 8.7cm，且隨朝向擺動）
→ v2「泰勒斯 ＋ 戳穿殘差」→ 幾何正確但**強制整面貼地**，不像真人
→ **v3「＋ 踝關節角度夾限」**。

| # | 機制 | 作用 |
| --- | --- | --- |
| **① 泰勒斯修正** | `ComputeAnkleTarget(rayStart, hitPoint, soleNormal, footBottomHeight)` | 腳踝抬升後仍落在**原本那條垂直 ray 上**，保住動畫 XZ。移植自 [ozz-animation `foot_ik`](https://guillaumeblanc.github.io/ozz-animation/samples/foot_ik/)，它明文點名舊寫法之誤：*"ankle position cannot be simply be offseted by foot offset"*。⚠️ **順帶修掉一個 M3.3 就存在的舊 bug**：`hit + n·fbh` 在 30° 坡會讓腳踝水平漂移 5cm 且坐得太低 |
| **② 戳穿殘差抬升** | `ComputePenetrationLift(...)` → `Max(0, 各取樣點戳穿量)` | 防穿模。**連續平面上恆等於 0**（腳底已與平面平行）⇒ 不需要「這是斜坡還是台階」的判別式——而那正是最容易寫成被禁 Gate 的地方。只抬不壓，同 `ComputePelvisOffset` 的「只下沉不上頂」哲學 |
| **③ 踝關節角度夾限** | `ClampGroundNormal(hitNormal, MaxFootAlignAngle)`，預設 **15°** | **腳底不強制整面貼地**（設計哲學明文）。超過夾限時腳保持較自然姿勢，②自動把腳抬到上坡側接觸、下坡側浮空＝真人行為。浮空高度 ≈ `span × tan(θ − 夾限)` |

**正確性關鍵**：夾限後的 `SoleNormal` 必須**一致地**用於①②③與 `GroundY` 四處——它們描述的是同一個腳底平面。只改旋轉不改殘差，抬升量就會對不上實際腳底。

**哲學檢核（三條全過）**：權重系統零改動，腳全程由 IK 接管；夾限是**連續**的，無二態切換、無震盪源；角度上限屬哲學第 3 條明文允許的「Reach Clamp 類」，**不是**被禁的 Fade／Gate／降權重。活動空間檢核答「否」——只限制地面對齊造成的踝角，不限制抬腿／跨步／轉向。

**`GroundY` 語意變更**：由「ray 原始命中高度」改為「最終腳踝目標對應的接觸高度」，使骨盆補償與殘差抬升後的實際落點一致。副作用：斜坡上骨盆下沉量略減（30° 約少 3cm）。⚠️ Play 時留意骨盆有沒有變得太挺。

**A/B 對照**：`UseTwoPointSampling = false` ⇒ 去掉②（①仍生效，幾何正確性不是可選項）；`MaxFootAlignAngle = 180` ⇒ 去掉③，回到 v2 的完全貼合。

**已知破綻（接受，不在本 ticket 解）**：陡下坡整個踩地相維持腳跟接觸，真人會隨步態滾向前腳掌——需要 Foot Contact／Foot Phase 與背屈／蹠屈不對稱角度才能表達；橫坡的腳掌左右邊緣未被採樣；heel/toe 任一 ray 落空時無殘差保護。**這些屬 `docs/03` 輪 7 品質輪，不是 L1 範圍。**

**驗收（DoD）**
- **EditMode**：pitch 計算抽成 `static` 純函數並附測試（比照既有 `ComputeFootWeight`／`ComputePelvisOffset` 先例）。
- **Play ①（回歸）**：**平地行為逐字不變**——雙點在平面上等高，必須退化為與現行完全一致。
- **Play ②（目標）**：樓梯上腳掌不再插入上一階；斜坡表現**不比現在差**。
- **Play ③（哲學）**：抬腿／跨步／轉向的活動範圍**無縮小**；未出現半 IK 常態化或抖動。
- **Play ④（觀察項，非驗收條件）**：兩點命中高度**非常接近**時，「取較高者」可能在幀間翻轉，帶動 `Normal` 與目標高度跳變（既有權重平滑只平滑權重，不平滑目標）。⚠️ **若真的看到跳動，不得用 gate／降權重處理**——那是被禁的路線；正解是往 Ground Sampling 再升級（遲滯取樣、SphereCast／CapsuleCast）。先觀察，不預先處理。
- **零 GC**：每腳 2 次 `Physics.Raycast`（每帧 2→4 條，非 alloc 多載），穩態 `0 B/frame`。
- **G4 可展示**：`UseTwoPointSampling` 開關能在樓梯上錄出 A/B 對照。

**檔案邊界**
- ✅ 只准動：`Presentation/IK/FootIKController.cs`（`SampleGround`／`ResolveFoot`／新純函數）、`Presentation/IK/FootIKSettings.cs`。
- ⛔ 不准動：`FootIKRig`／`FootIKPoseData`／`FootIKTargetData`／雙管道與 Ownership／`IPresentationController` 契約／`CharacterPipelineRunner`／權重系統。
- **Commit**：獨立一筆 `fix(ik): Foot IK L1 Heel/Toe 雙點採樣`，不與任何工作包混（由使用者執行）。

### 📦 四個 sequential 工作包（摘要；細節在各包開工時另立分卷）

| 包 | 🏗 Architecture Qualification | 🎬 Portfolio Qualification | 交付段落 | ADR 路由 |
| --- | --- | --- | --- | --- |
| **WP1** 鏡頭 ＋ Aim ＋ Throw 依 AimPoint | 「相機／瞄準是純 Presentation 關切」——交付時**黑板 schema 零改動、架構測試條數不變**。這是個**負面證明**：不是每個新功能都要動核心契約 | 探索鏡頭讓既有 locomotion 終於好看；瞄準可信；miss 能歸因於自己 | 1／2／3 | **不開 ADR** → `docs/09-camera-aim.md` |
| **WP2** Multi-Action ＋ Action Mapping ＋ 玩家揮劍 | 一顆 `ActionState`／六員 `StateType`／七階管線不變的前提下跑多 action；**加第四個 action ＝ 一份資產 ＋ 一列映射，零程式**；裁決 FU-1 | **遠程／近戰對比**；架構價值唯一能「演」出來的一段（改 SO 即改行為） | 6／7（並讓 8 成為可能） | **ADR-005（Trial）**，前提：ADR-004 已 Accepted |
| **WP3** 敵人戰鬥遭遇 | **敵人攻擊不新增任何 runtime 程式**——Telegraph／Commit／Recovery 全由 `ActionPhase` 的逐 phase `Interruptible` ＋ `Cooldown` 表達 | 敵人是對手不是靶子；Roll 終於有存在理由；雙向互動 | 4／5／8／9 | 不開 ADR → Living Docs |
| **WP4** 主展示 ＋ Teaser | **無新增；本包不得產生任何程式或架構改動** | 整片；節奏與呼吸 | 全部 | 無 |

**WP2 補充（本包是重心，非「順便新增揮劍」）**：Action 身分化 → catalog／注入路徑（解 FU-2）→ 裁決 FU-1 → Action Mapping（`IntentData` 只有一顆 `FireRequested`，兩個動作必然要動它 ⇒ 這是走 ADR 的原因）→ 揮劍 ＋ `MeleeHitEmitter : IActionLifecycleSink`（Release 時一次 `OverlapSphereNonAlloc`，與 projectile 對稱）→ 玩家 `Damage` Definition（EditMode 證明 Action→Action 中斷）→ **命中回饋最小集**（材質閃白 ＋ 一顆命中音，走既有 `AudioController`／`AudioDefinitionSO`）→ **段落 7 的展示素材錄製**（原 G5／P4）。

**WP3 補充**：敵人決策元件**不寫 `MovementIntent`**，而是設定 `AIMovementSource` 的期望距離／速度 ⇒ **A5 白名單不變**。含 **Look At**（`docs/03` §2.3 的管道模式；Telegraph 可讀性有一半來自「它看著我」）與**遭遇結束的最小處理**（一個整數計數，**不是 HP 系統**）。

### 🧺 明確禁止現在擴張（不變，補一條界線）

傷害數值／HP／死亡系統；Effect／Buff／Status Framework（**Slow 因此不進任何一包**）；combo／輸入緩衝／通用 cancel window；Behavior Tree／Utility AI／GOAP／aggression token；通用 targeting service／全域註冊表／singleton；上身層／aim IK；新的管線階段；通用 camera state machine；**動 `LocomotionModel`**（`docs/07` §13.1-R4）。
🆕 **新界線**：命中回饋＝「一個材質參數 ＋ 一顆音效」。**一旦它開始需要註冊、查表或定義檔，就已經越線**——第二個使用者出現前不建 production abstraction。

### 📇 Follow-up 登記表（只登記，不處理）

| # | 發現 | 處理時機 |
| --- | --- | --- |
| **FU-1／FU-2／FU-3** | Action→Action 中斷不可能／一角色一份 Definition／mailbox 無身分 | **WP2**。全文已寫入 [`docs/08` §11.1](08-skill-system.md) |
| **FU-4** | Throw 沿角色 root forward 發射（`ThrowProjectileEmitter` 的 `Instantiate(..., transform.rotation)`） | WP1 |
| **FU-5** | 相機**旋轉雙權威**：`_yaw`／`_pitch` 只驅動位置軌道，最終 rotation 被 `LookAt` 整個覆寫 ⇒「滑但不好瞄」的根因是這個，不只是 damping 值 | WP1 |
| **FU-6** | `AIMovementSource` 依賴 `data.CameraTransform`，把世界方向轉成相機空間只為了讓 `MotionDriver` 轉回世界；敵人的移動因此綁在玩家相機上 | **不排程**。正解是給 `MovementIntent` 座標基底語意或讓 producer 直接輸出世界方向（ADR-003 §9-L2 的延續），等第三個 producer 出現再談 |
| **FU-7** | Cinemachine 2.10.7 在 manifest 但專案零使用（只有 `Assets/StarterAssets` 引用）＝決策債 | WP1 開包時一次裁決「用或不用」。⚠️ 若導入需掛 `CinemachineCore.GetInputAxis` 這類**靜態全域輸入 hook**，等於引入第二個輸入權威 ⇒ 停並回報 |
| **FU-8** | `ThrownProjectile` 每次 `Instantiate`／`Destroy` | **不排程**。零 GC SOP 管的是穩態，投擲是事件型配置；Profiler 實測成為問題才做池 |
| **FU-9** | `ActionState.CanEnter` 對 `FireRequested` 與 external mailbox 同權，無仲裁 | WP2 順帶（多來源必然要定義誰贏） |
| **FU-10** | `LocomotionModel` 走向 God Class（`docs/07` §13.1-R4） | 不排程；**四個工作包都不得動它** |

---

## 🗡️ 素材登記：Free Sword Animations（2026-08-30 匯入，**WP2 才使用**）

**位置**：`Assets/EEJANAI_Team/FreeSwordAnimations/`（`FBX/` 12 個 ＋ `Animations/` 抽出的 `.anim` ＋ `Animations/Animator/` 的 `.controller` ＋ `Models/SwordSample/` ＋ `Prefabs/`）。

**現況更正**：**武器本體其實有**——`Models/SwordSample/Sword.obj` ＋ `swordmaterial.mat` ＋ 四張貼圖 ＋ `Prefabs/Sword.prefab`。
**真正缺的是掛點**：右手骨骼 socket ＋ 相對 transform ＋ 收放策略。⇒ 屬**使用者側 prefab 工作**，不是素材缺口。

**使用紀律（現在就定，避免 WP2 開工時漂移）**

1. ✅ **一律引用 `FBX/slash*.fbx` 的 sub-clip**。❌ **不要用 `Animations/*.anim`**——那是抽出的複本，違反 CLAUDE.md「Animation Assets: Immutable by Default」（FBX sub-clip 是唯一真相）。
2. ❌ **`Animations/Animator/*.controller` 一律不使用**。專案走 Animancer ＋ `AnimancerFacade.transitionMappings`，Mecanim controller 會是第二套播放權威。
3. ⚠️ **9 個 slash 只取 1**。取多個是 combo 的滑坡，而 combo 在禁止清單裡。WP2 需要的是「**第二個 action**」，不是「第二套攻擊系統」。
4. ⚠️ **掛點只做「永久掛在手上」**（最簡）。**不做 sheath／draw 拔劍收劍切換**——那是第二個 Action lifecycle 的偽裝，會把 WP2 的題目從「Action Mapping」偷換成「武器狀態管理」。
5. 🔍 **需先驗證**：這些 clip 是否為 Humanoid、能否 retarget 到 X Bot（不同 rig）。🛑 **若不能 retarget，停下來回報**——處置是換素材或換載體動作，**不是編輯 clip 內容**（CLAUDE.md 的四階升級順序：資料 → 表現層 → 換 clip → 才是改 clip 內容）。
6. `Scenes/Sample.unity`（素材附的展示場景）**不納入專案場景管理**，看完即可忽略。

---

## 🤝 Codex 交辦與交付邊界（2026-08-30 更新）

**檔案擁有權不變**（以擁有權切分，避免兩個 agent 改同一檔）：

| 角色 | 擁有 |
| --- | --- |
| **Claude** | `docs/**`、`LearningNotes/**`、`ArchitectureRegressionTests.cs`、`WORKLOG.md` |
| **Codex** | `Assets/Scripts/**`（新檔）、`Assets/_Project/Tests/EditMode/*Tests.cs`（**除** `ArchitectureRegressionTests.cs`） |
| **使用者** | 全部 `.prefab`／`.asset`／`.meta`／場景／Import 設定／NavMesh 烘焙／**全部 Git 操作**／全部 Play 驗收 |

**交付單元的規矩（適用每一包）**

1. **文件先行**：Claude 的規格／ADR 進 Trial **先落地並 commit**，Codex 才開始寫程式。這樣 review 時有對照基準，也符合 Trial-first 的「先修文件再驗證」。
2. **Codex 一次交付一個工作包的完整程式 ＋ 對應 EditMode 測試**，不做半包交付。允許交接過程短暫紅燈，**但交回給使用者驗收時必須全綠**。
3. **Codex 需要新的架構不變量時，提出需求由 Claude 落地**（`ArchitectureRegressionTests.cs` 是 Claude 獨佔，避免兩邊同時改同一張規則表）。
4. **Codex 不碰任何 Unity 資產、不碰 git。** 程式寫完即停，接線與驗收交還使用者。

**各包給 Codex 的紅線（撞到就停下來回報，不要自行擴 scope）**

| 包 | 🛑 停止條件 |
| --- | --- |
| **WP1** | ① 發現 aim 狀態**必須進黑板** ⇒ ADR 判準①，不得順手加欄位；② Cinemachine 需要靜態全域輸入 hook；③ soft target 開始需要目標列表／切換／跨系統查詢；④ 想改 `AIMovementSource` 或 `MovementIntent` 的座標基底（FU-6 是獨立議題） |
| **WP2** | ① FU-1 的候選解需要動 ADR-004 §3 的 D1–D7 任一條；② 出現「每個 Action 一個 `StateType`」的念頭（§5.2 已否決，A13′ 會紅）；③ **為 sword 建立 `ActionState` 子類別**（A19 會紅）；④ 命中判定開始需要起始幀／結束幀／多段／無敵幀；⑤ 命中回饋長出任何共用抽象；⑥ 同時出現兩個 Trial ADR |
| **WP3** | ① 敵人節奏**寫不進資產**、必須加程式分支 ⇒ 回頭修 WP2 規格，**不是在敵人身上補 `if`**；② 決策元件需要自己的狀態機；③ 決策元件想直接寫 `MovementIntent`（A5 會紅）；④「命中計數」開始長出血量／傷害值／死亡狀態／UI |
| **全部** | 任何包想動 `LocomotionModel`（`docs/07` §13.1-R4） |

---

## 🌱 Git commit 切點（**由使用者執行**；AI 一律不碰 git）

> 判準是「**能不能單獨 revert**」，**不是歷史好不好看**。為了漂亮的歷史去做 `git add -p` 拆同一個檔案，
> 代價高於收益——**檔案混在一起就合成一筆，並在 message 裡誠實說明**。

**A. 現在（工作樹已累積 P1＋P2＋治理文件，尚未 commit）**

| 順序 | 建議切點 | 內容 | 為什麼單獨一筆 |
| --- | --- | --- | --- |
| **C0-a** | `chore(assets): 匯入 Free Sword Animations（EEJANAI_Team）` | 只有 `Assets/EEJANAI_Team/**` ＋ `.meta` | 第三方素材單獨一筆 ⇒ 日後換版或移除可乾淨 revert，不與自己的程式糾纏 |
| **C0-b** | `feat: 敵人管線重用 ＋ Action in FSM（ADR-004 Trial，待 Play 驗收）` | `Assets/Scripts/**`、`ArchitectureRegressionTests.cs`、以及 P1／P2 的資產（Throw／Damage 動畫資產、Bake、`Actions/`、`EnemyStateMachineConfig`、`ThrownProjectile.prefab`、`Y Bot`、場景與 `X Bot.prefab` 改動） | ⚠️ **P1 與 P2 在 `CharacterPipelineRunner.cs` 內混在同一個檔**（D1 守衛拆解 ＋ Action 組裝），拆兩筆需要 hunk 級手術 ⇒ **合成一筆**。message 必須標 **Trial／待驗收**，不得寫成已完成 |
| **C0-c** | `docs: Trial-first 治理 ＋ ADR-004 ＋ scope 收斂與後續四包` | `CLAUDE.md`、`WORKLOG.md`、`docs/**` | 文件與程式分開 ⇒ 程式若 revert，治理決策不會跟著消失 |

**B. Play 驗收通過之後**

| 順序 | 建議切點 | 內容 |
| --- | --- | --- |
| **C1** | `docs: ADR-004 Trial → Accepted（A–F 回填）` | ADR-004 §10／§11、`docs/08` 狀態欄、`docs/changelog.md`、G1–G3／G6 打勾 |

⚠️ **`Trial → Accepted` 必須是獨立一筆，且晚於程式那一筆**——Accepted 是**驗收結果**，把它跟程式塞進同一個 commit 等於宣稱「寫完即通過」。

**C. 之後每個工作包的固定節奏（四筆）**

1. `spec:` / `docs:` — 規格與 ADR 進 Trial（**Codex 動程式之前**）
2. `feat:` — Codex 的程式 ＋ EditMode 測試（**全綠才交**）
3. `feat(assets):` / `chore(assets):` — 使用者側資產與接線（`.prefab`／`.asset`／`.meta`／場景）
4. `docs:` — Play 驗收後的 **fold-back**（Living Docs 對齊實況；如有 Trial 則轉 Accepted）

**為什麼程式與資產要分開**：Play 驗收失敗時可以單獨 revert 程式而**不丟掉資產工作**——`.meta` 的 GUID 重建代價遠高於重寫一次程式。

**軌 A 獨立 commit**：`feat(scene): 關卡地形（斜坡／樓梯／障礙）` 與 `fix(ik): Foot IK L1 Heel/Toe 雙點採樣` 各一筆，**不與任何工作包混**——它是並行軌，混進去會讓工作包的 revert 邊界失效。

---

## 🎯 作品集最低限度衝刺（2026-08-29 排定，**優先於一切工程路線圖**）

> **任務定位**：把專案從「一個人在空地上走路的 demo」推到「一個看起來像遊戲、且能證明架構價值的最小完成品」。
> **為什麼優先順序改變**：投遞卡在 HR／外行關卡，架構深度沒機會被技術面試官看到。根因診斷與敘事策略見
> `LearningNotes/portfolio-framing.md` §1–§2：**現有展示項的成功標誌都是「隱形」**（做對了外行只看到「角色正常走路」），
> 因此必須補上「外行看得出這很難做」的項目。
> **紀律不變**：本輪所有工作都必須是**既有路線圖的兌現**（ADR-003 §11 的 AI producer、dev-spec §1.4 的死亡來源、
> `docs/03` §1.3-L1 的「穿模觀感無法忍受時可提前」條款），**不得**為了作品集新造路線圖上沒有的系統。

### ✅ 完成定義（DoD）：作品集最低限度 Gate

- [ ] **G1 敵人重用整條管線**：一隻敵人會朝玩家移動，且**完整重用** Walk/Run tier、Stop 腳相選片、Foot IK、腳步音；玩家與敵人跑**同一份** `CharacterPipelineRunner` 程式碼。
- [ ] **G2 技能無可爭議**：玩家可發動 Throw（`Throw_Start`→`ThrowLoop`→`ThrowEnd*`）並丟出一顆會飛的投射物，命中敵人會有反應（播 `Damage`）。
- [ ] **G3 中斷矩陣可展示**：技能可被移動／Jump／Roll 中斷，行為與 `docs/08` §8.3 的表格一致，且 EditMode 有對應測項。
- [ ] **G4 Foot IK 可 A/B**：斜坡／樓梯上開關 IK 的差異外行可見，且 L1（跨階腳掌穿模）已修。
- [ ] **G5 資料配置可展示**：改一份 ScriptableObject 的值 → Play 立刻看到行為改變，**至少三個可 demo 的參數**（速度 tier／跳躍高度／打斷規則）。
- [ ] **G6 品質門檻**：EditMode 全綠（含本輪新增的架構不變量）；Development Build Profiler 穩態 `0 B/frame`（走 dev-spec §7.4 SOP）。
- [ ] **G7 場景像關卡**：demo 場景有斜坡／樓梯／障礙與明確目標，不是空地。

> 🔔 **交辦指令（不是提醒）**：**當最後一項打勾時，該會話必須主動告知使用者「已達作品集最低限度」**，
> 並附上七項的逐項驗收證據（哪個測試／哪張截圖／哪次 Play 驗收）。不得默默完成後繼續往下做。

---

### 🔴 兩顆必須先處理的架構地雷

**地雷 1：`CharacterPipelineRunner` 的入口守衛會讓敵人整條管線不跑**

```csharp
// CharacterPipelineRunner.Update() 第一行
if (_inputSource == null || _stateMachine == null) return;
```

敵人沒有 `IInputSource` ⇒ 順序 1～5 一行都不執行。這不是 bug，是**結構性假設**：Runner 目前假設「有輸入源」是管線運作的前提，
而 ADR-003 D2 的賣點正是「換掉 producer，Runner 零改動」。
⇒ **這是 ADR-003 §9-L2（「介面可能設計得不夠貼實需求，待第二個 model／producer 壓測」）的第一個實證。**
處理方式屬裁決點 **D1**（見下）。

**地雷 2：NavMesh 絕對不能擁有位移**

`NavMeshAgent` 預設自己搬 transform，違反「`CharacterController.Move` 是唯一位移出口」（`docs/07` §10.1）。
**正確接法**：`updatePosition = false` / `updateRotation = false`，只當**路徑查詢服務**用（取下一個 corner），
把方向交給 `AIMovementSource` 寫成 `MovementIntent`，位移仍全程走 `LocomotionModel → MotionDriver → CharacterController.Move`。
好處：Locomotion 平滑、tier、Stop 選片、Foot IK、腳步音**全部免費重用**。

> 順帶：敵人＝第二個 `PlayerRuntimeData` ＋第二個 Runner。design-doc §4.9 當初把 `Time.timeScale`／`Cursor` 判給應用層的理由
> 正是「第二隻角色進場立刻露餡」——**敵人是那個判斷的第一次真實驗收**，驗收結果請回填 design-doc §4.9。

---

### 工作包與順序

| 順位 | 工作包 | 內容 | 對應 Gate |
| --- | --- | --- | --- |
| **P1** | **敵人（適配控制器＋尋路）** 🟡 程式已落地、待 Unity 接線／Play 驗收 | ✅ Runner 無 input 仍跑完整 pipeline；✅ `AIMovementSource : IMovementIntentSource`；✅ NavMesh 關閉 Transform authority、只供 path direction。⏳ 使用者側 prefab／NavMesh 烘焙與 G1 Play 證據；受擊入口已落成 single-slot external Action request seam | G1 |
| **P1 平行** | **Foot IK L5 調參** | `RaycastUpOffset`／`RaycastDistance` 在乾淨 collider 基線上調參（`docs/03` §1.3-L5：**tuning 域，非程式問題**） | G4 |
| **P2** | **Throw → Projectile → Enemy Damage** 🟡 Runtime／EditMode 測項已落地，待 Unity 編譯與 Play 驗收 | ✅ 單一 `ActionState`；✅ Player Fire／Enemy external mailbox；✅ Player Throw／Enemy Damage 各一份 Definition；✅ phase-authored exactly-once release sink；✅ projectile 只提交 request；✅ A13′／A19–A22 與 T13–T15。⏳ 使用者建立 Transition／Bake／Definition／prefab 與 Config 規則後 Play 驗收；ADR-004 仍為 Trial | G2, G3 |
| **P3** | **Look At ＋ Foot IK L1** | ①Look At（`docs/03` §2.3：「複製 M3.1 Controller＋Rig 管道模式；零 Runner 改動」，有敵人後才有目標）②Foot IK L1 Heel/Toe 雙點採樣（**只動 `SampleGround`／`ResolveFoot` 內部＋Settings，雙管道／Ownership 全不動**） | G4 |
| **P4** | **資料配置展示（client 端）** | **先做零程式版**：錄影展示改 `GaitProfileSO`／`JumpStateParams`／`PlayerStateMachineConfig` → Play 立刻生效。只有證明「這樣還是看不懂」才做工具（裁決點 D3） | G5 |
| **P5** | **場景收尾** | 斜坡／樓梯／障礙＋一個明確目標，讓場景看起來像關卡 | G7 |

> **為什麼敵人排在技能之前**（與最初構想相反）：敵人是**舞台**——技能要打誰、Look At 要看誰、配置要配什麼，都等它。
> 而且兩顆地雷會影響後面所有設計，越早撞越好。

> 🔄 **2026-08-30 重排（見上方「Scope 收斂與後續四包」）**：
> **P1／P2 不變**，仍是現行工作，停止線＝ADR-004 `Accepted`。
> **P3 拆解**：Look At → **WP3**（Telegraph 可讀性的一半）；Foot IK L1 → **軌 A**。
> **P4（資料配置展示）→ WP2 段落 7**——它不是最後的加分項，是**架構價值唯一能「演」出來的一段**。
> **P5（場景收尾）→ 軌 A，現在就能開始**——它是影片段落 1 的唯一來源，且能讓 P1／P2 的 Play 驗收在像樣的場地上進行。
> ⚠️ **上一版把這三項當成 polish 是分類錯誤**：它們不 gate 任何技術 qualification，但**每一項都是觀眾必需品**。
> 判準已改為「**任一條軸必要即非 polish**」。

---

### 任務分配（Claude ／ Codex ／ 使用者）

> **分配原則：以檔案擁有權切分，避免兩個 agent 同時改同一個檔。** 跨界的檔案在下表明確標註協調方式。

| 角色 | 負責 | 檔案擁有權 |
| --- | --- | --- |
| **Claude** | 架構裁決與規格（D1／D2 兩案比較與建議）、新增架構不變量的定義、Foot IK L1 規格（守住「不動雙管道」邊界）、全部文件同步 | `docs/**`、`LearningNotes/**`、`ArchitectureRegressionTests.cs`、`WORKLOG.md` |
| **Codex** | 實作與功能測試：`AIMovementSource`、NavMesh 路徑查詢、投射物、`ActionState`＋Throw／Damage vertical slice、Look At 的 Controller／Rig、Foot IK 的 `SampleGround`／`ResolveFoot` 內部 | `Assets/Scripts/**`（新檔）、`Assets/_Project/Tests/EditMode/*Tests.cs` |
| **使用者** | 全部 Unity 資產側與驗收：`.prefab`／`.asset`／`.meta`／場景／Import 設定／`AnimancerFacade.transitionMappings`／NavMesh 烘焙；所有 Play 驗收；**全部 Git 操作** | 同左（**AI 一律不碰**） |

**需要協調的交界（先講好再動）**

| 檔案 | 誰動 | 條件 |
| --- | --- | --- |
| `Core/Pipeline/CharacterPipelineRunner.cs` | **Codex 實作** | **必須等 D1 裁決後**；同一次由 Claude 同步 dev-spec §2.1 與 §7.1 的對應條目 |
| `Core/Movement/Models/LocomotionModel.cs` | **本輪不動** | 若某工作包宣稱需要改它，先停下來提裁決——`docs/07` §13.1-R4 已預警它正走向 God Class |
| `Presentation/IK/**` | **Codex 實作**，Claude 出規格 | 只准動 `SampleGround`／`ResolveFoot` 內部與 Settings；**動到雙管道或 Ownership 即為越界**（`docs/05` §3.5.1／A11 守） |
| `ArchitectureRegressionTests.cs` | **Claude 獨佔** | Codex 若需要新不變量，提出需求由 Claude 落地，避免兩邊同時改同一張規則表 |

---

### ✅ 已裁決（2026-08-29，使用者拍板）

| # | 裁決 | 內容與後續 |
| --- | --- | --- |
| **D1** | ✅ **選 (a)：拆守衛，輸入源缺席時管線照跑**；🟡 程式已落地待 Play | 守衛只留 `_stateMachine != null`；取樣改 `_inputSource?.FetchRawInput(ref inputData)`，後續沿用輪 4 既有的「無輸入＝輸入歸零，管線照跑」語意。`MovementIntent` 的唯一寫入者語意是「每隻角色當下 active 的 `IMovementIntentSource`」；P1 合法實作現為 Player／AI 二選一，A5 白名單與 dev-spec 已同步。ADR-003 為 Accepted immutable log，壓測結果回填 Living Docs，不改寫該 ADR。 |
| **D2** | ✅ **選 (a)：敵人完整使用既有 `FullBodyStateMachine`** | **但 P1 只重用既有 Idle／Move，不為敵人新增任何 `StateType`**（A13 在 P1 範圍內零改動）。敵人與玩家跑同一份 FSM 程式與同一份 `StateMachineConfigSO` 拓撲 ⇒ 這是「一套 FSM 撐兩個角色」的第一次驗收 |
| **D3** | ✅ **先不做專屬工具** | 照 P4 的零程式版（錄影展示改既有 SO → Play 立刻生效）先驗證展示效果。**不足時才回頭談工具**，屆時須在文件寫明正當性來自作品集需求而非 Gate A／B |

| **D4** | ✅ **選 (a)：Action 併入 `FullBodyStateMachine`** | `StateType` 加**恰好一個**成員 `Action` ＋一顆資料驅動的 `ActionState`（動作＝`ActionDefinitionSO` 資產）。lifecycle／animation／interrupt 三者回歸 FSM 單一來源；順序 4.6 **不新增**。**Trial 期暫停 A13 → A13′（六員）＋ A19**。<br>📄 決策：[`docs/ADR/004-action-in-fsm.md`](ADR/004-action-in-fsm.md)（🟡 **Trial**）／實作規格：[`docs/08-skill-system.md`](08-skill-system.md)<br>🔧 **使用者修正兩點（已寫入 ADR）**：①`Priority` 是**競爭排序**不是打斷資格，G5 要展示 `CanBeInterruptedBy`／transition policy（ADR §3-D6、§5.4）；②A19 不是永久禁令，改為「禁止**為每個 Action** 建立獨立 subclass；差異優先資料化」，以 allowlist ＋書面理由實作（ADR §3-D3）。 |
| **D5** | ✅ **治理方式改為 Trial-first**（2026-08-29） | 見下方「🧪 治理原則」。ADR-004 是第一個適用者 |

---

## 🧪 治理原則：Trial-first（2026-08-29 起適用）

> **「Architecture decision 可以先成為 Trial implementation baseline；第一個真實 vertical slice 是架構驗證的一部分。
> 只有經實作與 Play／Test 驗證後才 `Accepted`。」**

```text
Design → Trial → Implement → Observe → Revise → Accept      ← 現行
Design → Freeze → Implement                                  ← 已棄用
```

**為什麼改**：原流程對單人開發產生壞誘因——ADR 一旦 `Accepted` 就凍結，實作撞到問題時**補 workaround 比修文件便宜**（修文件要開新 ADR）。結果是文件整潔、程式歪斜。

**規則**

1. **`Trial` 狀態的 ADR 是實作基線，但可被修改**，不必為每次修正開新 ADR；修改一律記入該 ADR 的「修訂紀錄」。
2. **實作暴露問題時：先修 Trial ADR ／ Living Spec → 再驗證。不得為了維護舊文字而補 workaround。**
3. ADR 只保留「**改錯會造成架構污染**」的決策；具體欄位、計時方式、冷卻細節等實作項一律下放 Living Spec，並在 ADR 內**明列哪些不凍結**（ADR-004 §9 為範本）。
4. **架構回歸測試驗證「目前有效的 baseline」**，該 baseline 可來自 Accepted ADR，**也可來自已正式進入 Trial 的 ADR**。因此 Trial 取代舊 invariant 時**不是「暫停」而是「取代」**，同一工作包內把測試換成新 baseline。⛔ **不建立 generic 的測試暫停／停用機制。** 允許 agent 交接過程短暫紅燈，**但交付使用者驗收時必須全綠**。
5. **Fold-back**：Trial／Spike 期間允許短暫 code-first，**但同一工作包結束、交付驗收之前，Living Docs／WORKLOG 必須 fold back 到實際程式狀態**；不得把未實證的內容寫成已完成事實。
6. **使用者已裁決 ≠ 工程上已驗證。** 引用 Trial 文件時必須註明其狀態。
7. `Accepted` 之後回到 Immutable Log 規則（要改決策就開新 ADR 取代）。

> 📌 **完整治理條文已提升至 `CLAUDE.md`**（「ADR Lifecycle」／「Code / Documentation Fold-back」／「Architecture Invariants Track the Effective Baseline」／「Spike / Probe Exception」四節）。本段只是本輪的操作摘要。

### ⏳ ADR-004 Acceptance Review（Throw vertical slice 完成後執行）

- [ ] **A** Throw 在 Unity Play 實際跑通（Start → Loop → End／Cancel 全程）
- [ ] **B** 三個權威與設計一致（動畫只由順序 5 播；打斷只由 FSM ＋資產決定；lifecycle 只有 `BaseState` 一套）
- [ ] **C** 既有 Idle／Move／Jump／Roll **無回歸**（動畫播放序列與位移路徑逐字不變）
- [ ] **D** EditMode 全綠，含 A13′／A19／A20 與行為等價回歸
- [ ] **E** 零 GC 通過（dev-spec §7.4 SOP，穩態 `0 B/frame`）
- [ ] **F** **實作沒有逼出第二套 authority 或明顯 workaround** ← 本 Trial 的真正目的

全通過 → 使用者確認 → ADR-004 `Trial → Accepted` ＋ 記入其 §11 ＋ 同步 changelog。
未通過 → 依 ADR-004 §10 的處置順序（先修文件再驗證，必要時轉 `Rejected` 並復原 A13）。
| **D1** | **Runner 入口守衛怎麼拆** | (a) 守衛改成「輸入源缺席時仍跑管線」——更誠實，兌現 ADR-003 D2 的宣稱，但動到跨領域契約（dev-spec §2.1）；(b) 給敵人掛一顆 null-object 輸入源——改動更小，但等於承認「Runner 需要一個假輸入源」，宣稱沒有真正被兌現。<br>**Claude 建議 (a)，且它其實比 (b) 更小**：輪 4 的 `BlockInput` 裁決（dev-spec §7.2-M5）已經確立「**沒有輸入＝輸入歸零，管線照跑**」這個語意，並實作為順序 2 閘門的 `inputData = default`。敵人是**同一個形狀**——守衛只需保留 `_stateMachine != null`，取樣改為 `_inputSource?.FetchRawInput(ref inputData)`，後面全部沿用既有歸零語意，`PlayerLocomotionPolicy` 依然是 `MovementIntent` 的唯一寫入者（A5 零改動）。⇒ 一行級改動，且**不是新機制，是既有裁決的第二個適用案例** |
| **D2** | **敵人要不要完整的 `FullBodyStateMachine`** | (a) 要（Idle/Move 重用，受擊另議）；(b) 不要，敵人只跑 locomotion ＋一個受擊播放。⚠️ 選 (a) 時注意 `StateType` 不得為了敵人擴張（A13） |
| **D3** | **資料配置要不要做專屬工具** | 先做零程式版（P4），只有證明不夠才做。⚠️ 若要做，**必須在文件裡誠實寫明：這顆工具的正當性來自作品集需求，不是 CLAUDE.md 的 Gate A／B** |

---

### 本輪明確不做

- ❌ **撿東西／拉拉桿**（`PickUp_*`／`PullLever_*`）——`_LH/_RH`＋`_90` 的 selection 復用**只有工程師看得到**，屬 L2 素材，延後。
- ❌ **傷害數值／血量系統**——受擊只播動畫。「受擊反應」與「傷害系統」是兩件事，後者無消費者（`docs/08` §3.3）。
- ❌ **法術／VFX**——法術＝punch ＋粒子，架構上完全相同；且 VFX 是真正的素材缺口，廉價特效會拉低觀感（`LearningNotes/portfolio-framing.md` §8）。
- ❌ **F4 Upper Body Layer**——本輪技能刻意選成不需要它的形狀。
- ❌ **新 ADR**——除非 D1 選了會改動跨領域契約的方案，屆時再議。
- ❌ **不得為了作品集新造路線圖上沒有的系統**（`LearningNotes/portfolio-framing.md` §5.3）。

---

## 🗂️ 已完成：Phase C Locomotion Transition Foundation（2026-08-20 重排，✅ 2026-08-21 全部驗收通過）

> **任務定位：先驗證資產與責任 seam，再實作。**禁止先寫 `LeftStopState` / `RightStopState`，也不得為了未來項目一次建出萬用 Animation Action framework。

### 開場必讀（僅這些）

1. `docs/00-map.md`
2. 本段
3. `docs/04-locomotion-foundation.md` §3／§4／§6／**§15**
4. `docs/ADR/003-movement-intent-layering.md` §3 D3／D4 與 §13.2
5. 需查權限時，只讀 `ArchitectureRegressionTests.cs` 的 `LayerRules` / `WriterRules`

### 本會話進度（2026-08-20）

- [x] `MotionBakeData.ComputeAverageSpeed` 只排除烘焙器產生的 `time=0/value=0` 第 0 帧哨兵；為空、單鍵與非哨兵零值補回歸測試。
- [x] 同步 `docs/02-dev-spec.md` 與 `docs/06-animation-presentation.md` 的代表速度定義；無黑板、ownership、FSM 或依賴方向變更，不開 ADR。
- [x] Kubold 來源 FBX 已回到 `Assets/MovementAnimsetPro/`；確認 Stop／Turn／Start 與 `RunFwdTurn180_*` 真實覆蓋，且沒有 90° Moving Pivot。代表 Pivot 改採 `RunFwdTurn180_R_LU`，缺口如實保留。
- [x] Phase C 4＋3 批次機械操作已完成；一次性具名選單隨即移除，不留下 Kubold／Phase C Editor menu 債。
- [x] 修正首次操作抓到的採樣前提錯誤：Animator 可位於 Root 子階層；單支與批次共用唯一 Humanoid Animator 解析器，Sample 對 Animator 所在 GameObject 執行。批次按鈕整合進既有 Motion Bake 視窗；新增 2 條解析回歸測試。
- [x] 將既有 Motion Bake 視窗改為通用批次烘焙：明確 Clip 清單／拖放／Project 選取加入／去重與空值驗證；共用採樣設定與既有 Bake 演算法，不修改 Import preset。
- [x] 使用者在 Unity Preview／Play 證實 `_LU/_RU`：`WalkFwdStop_LU` 左腳先停、`WalkFwdStop_RU` 右腳先停（First Stop／First Plant Foot）。
- [x] `Locomotion.asset` 直接配置手感門檻 `0 / 0.35 / 0.75 / 1`＋派生 PlaybackSpeed；不留下低頻一次性校正 UI。
- [x] Motion Bake 視窗補完整 ScrollView 與合理最小尺寸，批次清單在小視窗仍可操作。
- [x] 使用者 Play 驗收 Walk↔Run↔Sprint 基礎校正（主觀差異不大，但既有路徑可接受）。
- [x] Claude 規格完成；Codex 複核並否決 Presentation IK → Core 回流。
- [x] C1 程式垂直切片、功能測試與 A12～A15 架構守衛完成。
- [x] 使用者完成 Walk Transition／Mapping／Model 接線、修正 V1 `moveSpeedSource`，並通過 C1 檢查與 Play 驗收。
- [x] Run Stop LU／RU 套用曲線驅動 Import preset 並以 X Bot、60 FPS 烘焙；來源、Loop、速度與腳相 Gate 通過。
- [x] C1.1 Run 以「強度選集合 → 共用腳相選片」擴充既有 Stop runtime；無黑板／State／Facade／MotionDriver 契約變更。
- [x] Stop 速度接縫首輪修正：Walk／Run 下界收緊為 `0.35／0.75`；`RunStop_RU` playback `1.2588`，將 `t≈0.117 s` 峰值壓至 Run 錨點速度。相位連踩另案處理，不混入本次數值修正。
- [x] 修正下界收緊後 Run Stop 不觸發：tier 使用 SmoothDamp 前的 release-entry 快照；輸出仍用 Tick 後速度，消除 60／120 FPS 首幀衰減造成的 Gate 漏判。
- [x] 補正 SmoothDamp 漸近邊界：Band 比較沿用 `Epsilon=0.001`，實際穩態 `0.74999994` 可命中 Run；回歸測試改跑真實 smoother，不再手塞理想 `0.75`。
- [x] 建立 Run Transition、Facade mapping 與 Model refs，執行 Unity EditMode＋Run Stop Play 驗收；速度接縫與觸發邊界已確認完成。
- [x] Walk 連踩根因修正：不再以 Mixer root 加權時間查腳相；Facade 通用唯讀查主導 child clock，Locomotion 依已選 tier 的 loop Bake Data 選片。無 Mixer 同步、黑板、State 或 MotionDriver 變更。
- [x] 主導 child clock Play 複驗：不再以錯 gait 時鐘選片，但固定起點仍造成明顯全身姿勢跳動；確認 R2 的 ≈0.24 週期殘餘誤差已達必須處理的程度。
- [x] WalkStop LU／RU Fade `0.15 → 0.25 s` Play A/B：全身瞬間變動仍明顯，確認停止調 Fade，固定起點 pose mismatch 必須由相位等待處理。
- [x] Walk Pending Stop：以 Stop 起始 FootPhase 連續值比對 Walk loop 烘焙鍵，等待下一個最近 authored 入場點；等待期維持 release-entry 移動，0.5 s fail-safe。只套 Walk，Run 零改動。
- [x] Unity EditMode＋Play 驗收完成：任意 Walk 腳相放開會先自然走到匹配點再 Stop；無全身瞬跳、無先慢後衝；重新輸入／Jump／Roll 均可立即取消。
- [x] 完成學習復盤 `LearningNotes/phase-c-forward-stop.md`：彙整資料來源、Import／批次烘焙 SOP、Runtime 邏輯、最終數值、架構邊界與踩坑；並回填 `docs/07` 已解決的 V1 與被否決的 FootIK 回讀舊描述。學習筆記不納入 `docs/00–NN` 工程規格編號。

### 本輪固定順序

1. **Catalog**：盤點 Kubold Start／Stop／Moving Pivot／Turn in Place；記錄 clip、速度級、方向、角度、`_LU/_RU`、位移與旋轉。
2. **Representative Import + Bake**：只先選 Left Stop／Right Stop／90° Pivot／180° Turn 四支代表資產。同批修正 `ComputeAverageSpeed` 第 0 帧哨兵值偏差，再重烘受影響 locomotion clips，避免第二次全面重烘。
3. **Bake Gate**：檢查 `SpeedCurve`、`RotationCurve`、`RotationFinishedTime`、`EndPhase`、`TargetLocalDirection`、`FootPhaseCurve`、`BakedDuration`。大角度不正確就先修 Bake，不寫 Runtime 補丁。
4. **Semantic Gate**：在 Unity 播放確認 `_LU/_RU` 是抬腳、支撐腳、移動腳或最後落定腳；不准以檔名猜 mapping。
5. **Architecture Review**：以真實資產表回答 §15 G1–G4，再裁決 `LocomotionModel` 內部 phase／獨立 Presentation FSM／Gameplay State。
6. **Minimal seam**：只定義足以讓第一個 Stop 案例跑通的 Request → Selection → Motion Execution 邊界；`LocomotionTransition*` 仍是暫稱。
7. **Forward Stop vertical slice**：先 Walk Left／Right，驗證停止邊沿、腳相選片、Facade 播放、重新輸入／Jump／Roll 中斷與回 Idle；通過後才加 Run／Sprint。
8. **Fold back**：實測後才同步 design-doc、子系統 spec、State Matrix／架構測試與 changelog。若改 ownership／hierarchy／cross-cutting contract 才提新 ADR。

### 待驗證的責任邊界

```text
Gameplay Authority（允許什麼）
  → Locomotion Transition Selection（Start / Cycle / Stop / Pivot / Turn）
  → Motion Execution（Procedural / Baked / Distance-Matched / Warped）
  → Animation Post Process（Foot IK / Foot Lock / Pelvis / terrain adaptation）
```

- Ability／Gameplay FSM 管允許、封鎖與中斷；**不管左右腳選片**。
- Transition Selection 管選哪段資料；Motion Execution 管如何套用運動，不得綁死。
- Foot IK v1 是 ground adaptation，**不等於 Foot Lock**；IK 在選片與位移決策之後。
- Motion Warping 只在 Combat／Traversal 有真實 world target 時進場，不替代一般 Stop 的 Distance Matching。

### 本輪明確不做

- 不一次建完 Start／Stop／Pivot／Turn 全部 Runtime。
- 不預建 Motion Warping／Distance Matching／Foot Lock 完整 framework。
- 不新增 `LeftStopState`／`RightStopState`，不把 FootPhase 寫成 gameplay 黑板跨帧狀態。
- 不改 Accepted ADR-003；新結論若與它衝突，先停下評審。
- 不導入 Motion Matching；它仍是 v2.0 研究支線。

---

## 🟡 驗收結果：輪 4／4.1／4.2（2026-07-27，**剩一項**）

> 程式／測試／文件已全部寫完（changelog v0.25–v0.26）。
>
> **✅ 已通過**：EditMode **95 條全綠**／UI 模式（M7）／暫停與解除（M8 ①②）／Alt 不會誤觸暫停（③）／兩模式交錯時游標不被誤鎖（④）／游標自癒（M9 ⑤）／**§7.4 零 GC 複驗＝0 B**（已記入 §7.4.6）。
>
> **🛑 已知限制（非程式問題）**：`Alt`+`Esc` 是 **Windows 系統快捷鍵**，交錯按會被 OS 攔截並丟出遊戲視窗。M8 ④ 已改測等價的反向順序（先 Esc 後 Alt）。選 modifier 型持續按鍵前務必先查 OS 保留組合——詳見 dev-spec §1.4。
>
> **⬜ 唯一未結項 → M8 ⑤ 的後半**：暫停中按 **Space**，看 Inspector 監視器最上方的 **`[Current State]`** 是否由 `IDLE` 變 `JUMP`。
> * **移動不會排隊已確認且屬結構保證**（連續型意圖每帧覆寫 ＋ B9 吃 `deltaTime = 0` 推不動），這半已結案。
> * **trigger 意圖是另一回事**：`FullBodyStateMachine.Tick` 沒有 deltaTime 閘門，`JumpState.CanEnter` ＝ `JumpRequested && IsGrounded`，兩者皆與時間無關——**程式碼層面沒有任何東西阻止暫停中切進 JumpState**。
> * ⚠️ **看狀態欄，不要看畫面**：解除暫停後那一下跳躍可能不顯眼，肉眼會漏。
> * **若沒轉移，要查出是什麼擋住的**——依賴一個不知道為何存在的保護，比沒有保護更危險。
>
> 以下接線步驟保留備查（AI 不碰 `.prefab`／`.asset`／`.meta`／場景）。

### A. Inspector 綁定

**A-1　既有的 `UiModeArbiterSource`（語意已變：toggle → hold）**
* 在 `Ui Mode Action` 上**新增一個 `Hold` interaction**，`Duration` 設 **0.25**
* 綁定本身不用動（仍是 `<Keyboard>/leftAlt`）

**A-2　新增暫停器（⚠️ 不要掛在角色 Root）**
1. 場景中另建一顆空物件（例如 `SystemsRoot`），掛上 **`GamePauseController`**
2. 其 `Pause Toggle Action` 綁 **Esc**（`<Keyboard>/escape`）。🔄（輪 4.2）獨佔一顆鍵，**不需要 `Tap` interaction**

> 🔄 **暫停已改綁 Esc**，與 UI 模式的 Left Alt 不再共用，所以原本「Tap 門檻 ≤ Hold 門檻」的相依**已解除**。⚠️ 若日後改回共用一顆鍵，那條相依會回來（且是正確性條件，不是手感調味）。
> 📌 **為什麼暫停器不能掛角色 Root**：`Time.timeScale` 是全域狀態，角色黑板／仲裁是單一角色的。理由完整版見 design-doc §4.9。

**A-3　新增游標擁有者（🔴 缺這顆會很明顯地壞掉，見下方警告）**
1. 同一顆 `SystemsRoot` 再掛上 **`CursorModeController`**
2. 把 **`Ui Mode Source`** 拖入角色 Root 上的 `UiModeArbiterSource`
3. 把 **`Pause Controller`** 拖入同物件上的 `GamePauseController`

> 🔴 **這顆缺席時：開場游標不會被鎖住，而且相機完全不會轉。** 因為 `ThirdPersonCamera.Start` 原本那行初始鎖定**已被移除**——留著它就是第二個 Cursor 寫入者，「唯一擁有者」會淪為文件上的說法。這是**刻意讓它大聲壞掉**，症狀一眼可見，不是靜默漂移。
> 📌 **為什麼游標要搬到應用層**：暫停成為第二個滑鼠模式後，兩個各寫各的會產生可重現的碰撞（暫停中按住再放開 Alt，游標會被鎖回去）。詳見 changelog v0.26 §5。

### B. Play 模式行為驗收（＝dev-spec §7.2-**M8**，另 §7.2-M7 的觸發方式已改為「按住」）

| # | 驗收項 | 預期 |
| --- | --- | --- |
| 1 | 按 **Esc** | 世界凍結（角色與動畫全停） |
| 2 | ⚠️ **關鍵項**：再按 Esc | 必須能解除，且 `timeScale` 回到暫停前的值。這條驗證 `Update` 在 `timeScale == 0` 下照跑——若失敗，暫停將無法解除，回報我處理 |
| 3 | **按住** Alt 超過 0.25s | 進 UI 模式（游標出現、相機停轉、角色 B9 收步），**且不會順便暫停** |
| 4 | 放開 Alt | UI 模式全部復原 |
| 5 | 🆕 **先按 Esc 暫停 → 再按住 Alt → 放開 Alt** | 游標**必須仍然可見**。這是單一游標擁有者的實證（其一收手不得解除另一個的要求）。<br>⚠️ **順序不可顛倒**：`Alt`+`Esc` 是 Windows 系統快捷鍵（切換視窗），OS 層就攔截、Unity 收不到，實測會被丟出遊戲視窗——**鍵位與 OS 撞號，非程式問題**（2026-07-27 實測確認） |
| 6 | ✅ 暫停中的 trigger 意圖 | **已結案（v0.27）**：追查發現「暫停中按跳躍不會跳」靠的是另一個 bug 的副作用（見下方 v0.27 專段），已改由 `GamePauseController` 的 `BlockInput` 正式關閉 |

---

## 🔴 待使用者操作：v0.29（M3.x-B Footstep 落地）

> 程式／測試／文件已寫完。**我沒有跑過 Unity，也沒有實測任何場景**——下列全部待你驗。

### A. 接線

1. 角色 **Root** 掛上 **`FootstepDetector`**（它會自己 `GetComponentInChildren<FootIKRig>()` 取 pose 管道）
2. `AudioLibrarySO` 資產新增兩列：**`LeftFootstep`** 與 **`RightFootstep`** → 各自綁 `AudioDefinitionSO`
   * ⚠️ 未註冊不會報錯，只會靜默無聲（`AudioLibrarySO.Get()` 回 null）——所以「沒聲音」的第一個懷疑對象是這裡

### B. 驗收（都需要實際聽）

| 分類 | 項目 |
| --- | --- |
| 地形 | 平地／斜坡／階梯——腳步聲時機是否跟得上動畫落腳 |
| 速度 | Walk／Run／Sprint——**Sprint 的高步頻不得漏拍**（這是刻意不用時間閘的理由） |
| 靜止 | Idle 站著不得有腳步聲；**原地轉向應該有** |
| 跳躍 | 落地只聽到落地聲、**不得同時有腳步聲**；落地後走第一步聲音正常（抑制不得破壞跨帧狀態） |
| 翻滾 | ⚠️ **本輪未特別處理 Roll**——腳蜷起時高度劇烈變化，可能誤觸發。若實測明顯，回報我處理 |
| 空中 | ⚠️ 同上，**未加 `IsGrounded` 閘門**（未經裁決的東西我不自行加）。若空中有腳步聲，回報 |
| 左右 | 左右腳是否分別觸發（可先給兩個明顯不同的音效分辨） |
| 順序 | 在 Hierarchy 把 `FootstepDetector` 與 `AudioController` 上下對調 → **行為必須完全不變** |

### C. 需要調的參數（`FootstepDetector` 的 Inspector）

三個數字都是**我猜的初值**，必然要依實際動畫調：

* `ArmDescentSpeed = 0.35`（上膛：腳底下降速度門檻 m/s）
* `FireDescentSpeed = 0.05`（擊發：下降慢於此值即落腳）⚠️ **必須明顯小於上膛值**，否則 Schmitt trigger 失效
* `MinLiftExcursion = 0.03`（最小抬腳行程 m）

漏拍 → 調低 `ArmDescentSpeed`；多餘的聲音 → 調高 `ArmDescentSpeed` 或 `MinLiftExcursion`。

### D. 回歸

* **EditMode**：99 ＋ 21 → **120 條**
* **零 GC**：新增了順序 6.5 的第二段迴圈，建議依 §7.4 SOP 複驗（設計上已守：陣列 Start 收集、索引迴圈、struct 值複製、無 LINQ／無字串／無 new）

---

## 🔴 待使用者操作：v0.28（M3.x-A Pose 管道擁有權）

> 輪 3 Footstep 的前置。**調查結論：ADR-003 D4 完全未被觸及**（維持 Accepted、不修改、不新增 ADR）。真正要修的只有 `FootIKPoseData` 的擁有權。
> **接線：無。** 本輪不新增元件、不新增 Inspector 欄位。

### A. 驗收（Play 模式，重點是「行為必須零變化」）

| # | 驗收項 | 預期 |
| --- | --- | --- |
| 1 | 平地／斜坡走跑，觀察雙腳貼地 | 與變更前**無可感知差異**（本輪只搬擁有權，演算法一行未改） |
| 2 | 跳躍／翻滾中的腳部 | 同上，無抽搐、無黏地 |
| 3 | Console | 無 `FootIKController 找不到 FootIKRig` 之類的新錯誤 |

### B. 回歸

* **EditMode**：96 ＋ 3 → **99 條**（A11 ＋ `FootIKTests` 兩條擁有權測試）
* 既有 `FootIKTests` 8 條純函數測試**必須維持全綠**（演算法未動）

### C. 這輪唯一的行為差異（誠實記錄，目前不可觀察）

場上沒有 `FootIKController` 時，`FootIKRig` 現在**仍會寫入** Pose 快照（先前兩條管道共用一個 `return`，缺 Controller 時連 Pose 都不寫）。目前 Pose 的唯一讀取方正是 Controller 本身，所以**看不出差別**；這麼改是為了讓 M3.x-B 的偵測器不會因為「場上剛好沒有 IK Controller」就靜默收不到資料。

---

## 🔴 待使用者操作：v0.27（兩個互相抵銷的 bug）

> 這一輪是 M8 ⑤ 追查出來的。**根因一個、症狀兩個**：暫停時 `Move(finalMovement * 0)` ＝ `Move(Vector3.zero)`，而 Unity 的 `isGrounded` 由「上一次 Move 有沒有向下撞到東西」決定 ⇒ 零位移回報 false。於是①解除暫停時 `JustLanded` 假觸發（落地聲）②暫停中 `IsGrounded` 恆 false 讓 `JumpState.CanEnter` 失敗（那個「不知道為何存在的保護」）。**修掉①會讓②的保護消失**，故兩件同批修。完整推導見 changelog v0.27。

### A. 接線（🔴 缺這步跳躍缺口仍開著）

1. 角色 Root 的 `CharacterPipelineRunner`，新欄位 **`External Arbiter Sources`** 陣列 Size 設 **1**
2. 拖入場景中的 **`GamePauseController`**

> 沒拖的話：暫停中按 Space 會**真的**切進 `JumpState` 並卡住（`_airborneTimer` 在 `deltaTime = 0` 時不前進 ⇒ `IsLanded` 永遠 false ⇒ 退不出來），解除暫停後起跳。

### B. 驗收（＝dev-spec §7.2-M8 ⑥⑦⑧）

| # | 驗收項 | 預期 |
| --- | --- | --- |
| 1 | **站在地上**按 Esc 暫停 → 解除 | **不得聽到落地聲**（修復前必響） |
| 2 | 站在地上暫停 → 按 Space | Inspector 的 **`[Current State]` 必須維持 IDLE**；解除後也不得起跳。⚠️ 看狀態欄不要看畫面 |
| 3 | 跳到最高點暫停 → 解除 | 角色從原地續墜，落地聲在**看得見的下墜之後**才響（這是正確行為，不是 bug） |
| 4 | 一般移動、跳躍、翻滾 | 手感與 v0.26 完全一致（`IsTimeFrozen` 只在 `deltaTime <= 0` 生效，正常遊玩永不觸發） |

### C. 回歸

* **EditMode**：95 ＋ 1 → **96 條**
* ⚠️ `MotionDriver.IsTimeFrozen` **無法自動測**（需控制 `Time.deltaTime` 與真實 `CharacterController`），只能靠上表 1・3

**游標擁有權（＝dev-spec §7.2-M9，輪 4.2 新增）**

| # | 驗收項 | 預期 |
| --- | --- | --- |
| 7 | 開場 | 游標即被鎖住、相機正常轉動（＝`CursorModeController` 有接上，它接手了相機原本的初始鎖定） |
| 8 | 暫停期間 | 游標**常駐可見**（本輪需求） |
| 9 | 🎯 **關鍵回歸** | 暫停中按住 Alt 進 UI 模式 → 再放開 → **游標必須仍然可見**。舊架構在此會把游標鎖回去，正是本輪修的 bug |
| 10 | 兩個模式都退出後 | 游標回到鎖定 |
| 11 | 🆕 **外力自癒回歸** | Play 中讓 Game 視窗失焦再切回 → 游標必須在下一帧被拉回鎖定。**這是第一版 bug 的回歸測試**：初版快取「自己上次寫了什麼」，一旦 Unity 在背後解鎖（按 Esc、失焦都會）就永遠不再修正，游標永久可見。現版比對 `Cursor` 現值，故會自癒 |

> 📌 **副作用要知道**：因為現在每帧都會把游標拉回，Editor 內「按 Esc 逃出鎖定游標」的內建後門會被立刻收回。現行方案下不成問題（Esc 本來就是暫停鍵，暫停會正當地放開游標）。

### C. 回歸

* **EditMode 全綠**：83 條 ＋ `GamePauseControllerTests` 6 條 ＋ `CursorModeControllerTests` 6 條 → **95 條**
* ✅ **零 GC 複驗已完成**（2026-07-27）：本輪三處新增熱路徑（順序 4.5 `ArbiterPipeline.Tick`、`GamePauseController.Update`、`CursorModeController.Update`）實測維持 **0 B**，明細記入 **dev-spec §7.4.6**
* **Editor 錯誤複驗**：`CharacterPipelineRunnerEditor` 已改用 `RequiresConstantRepaint()`。確認 `GUIClips` 失衡與 `SerializedProperty has been Disposed` 兩條是否消失；**若仍出現**，把 `PlayerInputSource` 與 `UiModeArbiterSource` 兩顆元件在 Inspector 摺疊起來再測一次（可確認是否為 InputAction drawer），並考慮把 `UiModeArbiterSource` 移到角色的**子物件**（`GetComponentsInChildren` 照樣找得到）

---

## 🎯 下一會話：建議起手（2026-07-27 規劃）

> 📍 開場照舊：`docs/00-map.md` → 本段 → 只讀任務對應的 ADR／章節。

**先結掉上面 🟡 那段剩下的 M8 ⑤**（暫停中按 Space 看 `[Current State]`）；那一項會決定要不要順手補「暫停封鎖輸入」。

### 主推薦：輪 3 Footstep（**建議不照 roadmap 的 5 → 6 順序走**）

`docs/03-animation-roadmap.md` 排的是輪 5 Upper Body → 輪 6 Combat。**建議跳過輪 5，先做輪 3**，理由是這個專案自己的紀律：

* **輪 5 Upper Body Layer 現在沒有消費者。** 它的存在理由是「Combat 需要」，但 Combat 是輪 6。**先蓋基礎設施再等使用者，正是本專案一路刻意避開的事**——輪 4 的 `BlockInput` 讀取契約足足等了兩輪才等到真實 writer，等到時形狀是清楚的；Upper Body 沒有這個條件，現在做等於憑空決定「第二個 StateMachine vs Facade API」（roadmap §4-4 的未決點）。
* **輪 3 反而有一個已經在等的消費者**：`FootPhaseCurve` 在 v0.19 就烘進 4 支 loop（401·61·47·39 keys），**至今零消費者**。烘出來的資料沒人用，等於專案最核心的差異化敘事「自研烘焙管線 → 執行期表現」**還沒閉環過一次**。
* **附帶收益**：`AudioController` 的 Event → Definition → Library 三層目前只有落地音一個實例；Footstep 是第二個——就像輪 4 驗證了「讀取契約先行」，這會驗證三層查表是否真的可擴充。

**輪 3 要先裁決的點**（roadmap §4-2，不預答）：Animation Event 的承載選擇——`TransitionAsset` 序列化事件 vs 黑板單幀事件擴充，**哪類事件走哪條**。這條線畫錯會讓兩套機制長期混用。

### 順手可做，不需獨立輪次

**README 的 D 項 ＋ GIF。** 現在是寫「控制列表」最好的時機——控制方案這兩輪才真正補完（WASD／Ctrl／Shift／Space／Alt／Esc 六項齊了），在此之前寫都會過時。**GIF 仍是作品集首頁最大的單一缺口**，而素材已齊（0 GC 已驗、locomotion 手感已調、UI 模式與暫停可展示）。

### 明確不建議現在動

* **輪 5 Upper Body**：等 Combat 帶著真實需求進場（理由同上）
* **Phase C 停步分腿**：動畫品質收益最大，但會動 locomotion 核心手感，且要重烘——建議跟 🐛 `ComputeAverageSpeed` 0 值哨兵偏差（低估 1.6~2.6%）綁一起做，反正都要重烘一次

⛔ **明確沒做、也不要順手做**的（都是刻意延後，理由見 changelog v0.25 §6 與 v0.26 §7）：死亡 ArbiterSource、優先級／強制解封、鏡頭跳動抑制器、**Pause Menu／Canvas／EventSystem／UI navigation**、**暫停時封鎖角色輸入**、把 `CursorModeController` 的來源一般化成介面集合。

> ✅ **「Cursor service 抽象」已不在此列**——它於輪 4.2 落地（`App/CursorModeController`）。壓力在同一個工作階段就到了（「暫停時游標應常駐」），而且到來時形狀是清楚的。**這是一個「等真實壓力再抽象」奏效的正面案例**：若在輪 4.1 憑空抽，抽出來的很可能是埋著 LIFO 假設的「暫停自己存還原游標」版本（見 changelog v0.26 §5）。

---

## 🗂️ 已完成：輪 4 起手規劃（2026-07-26 規劃，✅ 已於 2026-07-27 執行完畢）

> 📍 開場照舊：`docs/00-map.md` → 本段 → 只讀任務對應的 ADR／章節。**不要**為了熟悉而整檔讀 dev-spec／design-doc。

### 主推薦：輪 4 ArbiterPipeline（順序 4.5）

**為什麼是它，而不是 Footstep／Phase C**：它是唯一一個**已經有具體需求在等、且卡著一個未決架構問題**的項目。

* **需求端已存在**：你的控制方案裡「**Alt ＝ 顯示滑鼠並停止移動**」還沒實作，而它正是 `BlockInput` 的第一個真實使用情境。
* **它會結掉 §7-M5 這個懸了兩輪的未決項**：「`BlockInput` 是否應同時凍結 `MovementIntent`？」現況是順序 2.5 刻意置於閘門外（維持 Migration 前行為），當時明寫「留待 ArbiterPipeline 真正有 writer 時一併裁決」。**現在有 writer 了，可以裁決了。**
* **黑板契約早就備好**：`ArbiterData{BlockInput, BlockIK, BlockAudio, BlockExpression}` 已在 §1.4，且 `A5` 的 WriterRules 目前把 `Arbitration` 標為「不得有任何執行期寫入者」——這一輪會是**第一次讓它合法擁有寫入者**，測試規則要同步更新（設計上刻意的摩擦）。
* 規模適中：一個 pipeline 階段 ＋ 一個裁決 ＋ 測試，不動 FSM 拓撲。

**開場要讀**：dev-spec §1.4（ArbiterData）、§2.1 順序 4.5、§7.2-M5、§7.3；design-doc §4.6（表現層管線既有骨架）。

**已知要一併裁決的三題**（別直接動手，先討論）：
1. `BlockInput` 該凍結哪些東西——trigger 意圖？`MovementIntent`？兩者語意不同（放開輸入 vs 凍結當下狀態），選錯會出現「封鎖瞬間角色定格」或「封鎖期間仍在滑行」。
2. 多來源封鎖的疊加（死亡／CC／過場同時要求封鎖）——現況是單一 bool，§2.4 舊規格提過優先級疊加，但那是 YAGNI 延後項，**先確認真的有第二個來源再做**。
3. 「顯示滑鼠」屬 Input 層還是 Arbiter 層？依 ADR-003 §13.3，游標狀態切換偏 Input／UI 職責，**不該讓 Arbiter 認識滑鼠**。

### 替代選項（若你想做動畫品質而非系統）

* **輪 3 Footstep**：FootPhaseCurve 在 v0.19 已烘進 4 支 loop，**至今沒有任何消費者**——這一輪會是它的第一個真實使用者，也會驗證「烘焙曲線 → 表現層事件」這條資料流。已有 `AudioController` 可擴充，規模小。
* **Phase C**：停步分腿姿勢（stop 動畫＋Foot Phase 選腳別）＋Starts/Stops/Turns。動畫品質收益最大，但也最大輪、且會動到 locomotion 的核心手感。

### 順手可做的小項（隨時，不需獨立輪次）

* README 稽核剩下的 D 項：**What works today**（跑起來會怎樣）、**控制列表**、**gait 數值來源鏈**、**GIF／截圖**。現在素材齊了（0 GC 已驗、locomotion 手感已調），**GIF 是作品集首頁最大的單一缺口**。
* GitHub Topics（目前空）、個人頁 Pin。
* 🐛 `ComputeAverageSpeed` 的 0 值哨兵偏差（低估 1.6~2.6%）——修正要全面重烘，**建議跟下次「反正要重烘」的輪次綁一起做**（例如 Phase C 導入新 clip 時）。

---

## 🏁 里程碑檢查點（2026-07-26，changelog v0.19 補記）

**v0.19 Foundation ＋ GaitProfile ＋ Run 預設型態 ＋ Animation-independent gameplay core ＋ Runtime baked data** 五項齊備，這條線第一次全程走通：

```
InputAction → InputData(ref struct) → PlayerLocomotionPolicy(+GaitProfileSO)
  → MovementIntent{強度[0-1], 方向, WalkModeActive}     ← 模型無關契約
  → LocomotionModel(B9 平滑 → Movement Output，自驅 SetFloat)
  → FSM(問 IsProducingMotion) → MotionDriver → CharacterController
```

**磁碟驗證的收案狀態（門檻於 2026-08-20 依手感再校正）**：`Locomotion.asset` 4-tier（`0/0.35/0.75/1`）／`moveSpeedSource`→`Bake_SprintFwdLoop`／4 支 loop 的 `FootPhaseCurve` 已補（401·61·47·39 keys）／`Gait_ActionRPG`（0.75／1.0／0.3651／toggle）／`Bake_Stand To Roll.BakedDuration` 2.3666668／**EditMode 76 綠（歷史實測值，本輪尚待重跑）**。

**仍未達成（勿當成已完成）**：`SourceClip` 欄位仍讓 clip 被打包載入（只是邏輯不讀）／0 GC 無 Profiler 存證／`MovementContext` 未實作／7 顆 Bake 的 `BakedDuration` 為 0（刻意延後）／`ComputeAverageSpeed` 低估 1.6~2.6%。

---

## Runtime → AnimationClip 依賴切斷（2026-07-26，changelog v0.23）——✅ 完成並驗證（76 綠）

起因：README 要宣稱「Kubold 只是 sample content」，先做了一次沿實際程式的 animation-independence 追蹤，抓到全專案唯一一條執行期 clip 耦合（`MotionBakeData.Duration => SourceClip.length`）。

### 已完成（修改 3 檔＋測試 3 條）
- `MotionBakeData`：新增序列化 `BakedDuration`；`Duration => BakedDuration`；`SourceClip` 註記為 Editor-side provenance。
- `MotionBakeEditor.SaveAsset`：烘焙時 `asset.BakedDuration = sourceClip.length;`
- `RollState`：退化條件改看**值**（`> 0`）而非引用；新增「資產未重烘」的 Editor 警告。
- 測試 73 → **76**（`Duration` 不依賴 clip／舊資產如實回 0／**Roll 無時長時不得秒退**）。

### ✅ 使用者側已完成
- **只重烘 `Bake_Stand To Roll`**（唯一有 `Duration` 消費者的資產）→ 翻滾恢復正常。
- **EditMode 76 條全綠。**

### 📌 刻意延後：其餘 7 顆 Bake 資產（決策，非遺漏）
其餘 `Bake_*.asset` 的 `BakedDuration` 目前為 **0**，**用到時再烘**（例：做狀態銜接而開始用 `Bake_Jump` 時，順手重烘該顆）。

依據：目前 `Duration` 的消費路徑**只有 Roll 一條**（`RollState.OnEnter` ＋ 它唯一呼叫的 `MotionDriver.ExecuteBakedCurveMovement`），其餘資產無人讀 `Duration`——`moveSpeedSource` 讀的是 `AutoAverageSpeed`、Jump 讀的是 `Auto*` 純量，兩者都已存在且正確。

> ⚠️ **這個延後帶著一個已知風險，別忘了**：日後若有**新的**消費者開始讀某顆未重烘資產的 `Duration`，它會拿到 0，而**目前只有 `RollState` 有「值 > 0」的退化閘門與 Editor 警告**，其他消費者沒有。
> 兩個處理選項（都不急，用到再說）：①新消費者上線時順手重烘該顆；②若這類消費者變多，就在 `MotionBakeData` 加一個 `#if UNITY_EDITOR` 的 `OnValidate` 警告，讓「未重烘」在資產層就現形，不必每個消費者各寫一次閘門。

---

## Repo 門面：README ＋ LICENSE（2026-07-25）——檔案已建，待你 commit

起因：repo 為 Public 且定位作品集，但 ①`LICENSE` 缺席＝保留所有權利，與「未來可抽取的開源套件」定位矛盾；②第三方資產已從歷史清除 → **fresh clone 無法編譯**，沒有 README 的訪客只會看到一個編不起來的專案。這是唯一一項「愈晚做代價愈高」的待辦。

### 已完成（AI 只建檔，git 由你執行）
- **`LICENSE`**：MIT，`Copyright (c) 2026 Baka8787`。**未修改 MIT 原文**（改授權條文是壞習慣）；第三方資產的排除說明放在 README 的 License 段。
- **`README.md`**：英文摘要 3 段（作品集門面）→ 專案定位 → 架構主張表 → **Mermaid 資料流圖**（GitHub 原生渲染）→ ADR 索引 → 專案結構 → 文件導覽 → **測試段（A1~A10 逐條說明「架構不變量是可執行的」）** → ⚠️ 第三方資產需求 → License。
- 順帶把 `docs/01`／`docs/02` 的文件標題從 `CharacterController` 改為 **`IntentPipeline`**（與 repo 名一致）。

### ⚠️ 待你確認／執行
1. **審 README 內容**：特別是「第三方資產需求」表（Animancer 走 `Packages/com.kybernetik.animancer/` 本機 UPM、Kubold 走 `Assets/MovementAnimsetPro/`）與英文摘要的措辭。
2. **commit ＋ push**（AI 不碰 git）。
3. 記憶清單剩餘兩項：**GitHub Topics**（目前空）、**個人頁 Pin**。

---

## Walk 型態 hold／toggle（2026-07-25，changelog v0.22）——✅ 測試已通過

落地第一套完整控制方案（參考終末地）：**WASD 預設 Run／Ctrl 切換 Walk 型態／Shift 閃避／Space 跳躍**，sprint 由 buff 驅動（未來）。**無架構變更**——沿用 ADR-003 D5 既有裁決，未開新 ADR。

### 已完成（修改 6 檔＋測試 4 條）
1. **`InputData.WalkButtonDown`**（邊沿）：與既有 `WalkButtonHeld` **並存**，raw input 層不預設控制方案。
2. **`MovementIntentData.WalkModeActive`**（mode state 進黑板，D5／§9-L5）：語意＝「型態開著沒有」，非「鍵按住沒有」。
3. **`GaitProfileSO.walkIsToggle`**：hold／toggle 成為**資產可配置項**——換玩法＝換資產的承諾對「操作語意」也成立。`ResolveIntensity` 第三參數改名 `walkHeld`→`walkActive`。
4. **`PlayerLocomotionPolicy`**：讀黑板 → 邊沿翻轉 → 寫回黑板，**零私有欄位**。
5. **Editor 監視器**：新增 `Walk Down（邊沿）` 與 `Walk Mode Active（型態）` 兩列，toggle 行為肉眼可驗。
6. **測試 69 → 73**（hold 鏡射／toggle 翻轉閂住／toggle 不看 Held／狀態不得殘留在 producer）。

### ✅ 使用者側已完成
- 建立 gait 資產、綁 `WalkAction` → Left Ctrl、勾 `walkIsToggle`；**EditMode 73 條全數通過**。
- **依手感調整數值**：`walkIntensity` 0.2651 → **0.3651**、`defaultIntensity` 0.574 → **0.75 以上**。
  - **這是安全的**：`threshold = speed_i/speed_max` 讓任意 intensity `p` 的混合動畫速度恆為 `p × speed_max`、與位移速度恆等 → **不會滑步**。偏離基準值只代表「刻意選了一個混合姿態」（walk≈走/跑之間、default≈跑/衝之間），不是校準錯誤。
  - 這條釐清已寫進 dev-spec §3.1（GaitProfileSO 紀律列）與 §7-M4——**公式綁的是 threshold，不是 intensity**，先前兩者被混在同一句話裡。

### 📌 若還想更快（第 3 階，需重烘）
把第三 tier 換成 Kubold 的 Fast Run clip（`Bake_Fast Run.asset` 已存在但缺 `AutoAverageSpeed`，須重烘），threshold 依公式重算。**禁止**調 `MotionDriver.moveSpeed` 或勾 `overrideMoveSpeed` → 那才會全域滑步（§9-L4）。

### 🐛 待修（低優先，需重烘全部資產）
1. `MotionBakeData.ComputeAverageSpeed` 把第 0 帧那支人造的 0 值算進算術平均 → 代表速度**低估 1.6%~2.6%**（Run 真值 3.578 記為 3.502）。修正＝跳過該支哨兵值，但會改變所有已烘值，需重烘一輪。
2. ~~🆕 **`MotionBakeData.Duration` 是全專案唯一一條「執行期邏輯讀 `AnimationClip`」的耦合**~~ → ✅ **已於 2026-07-26 以修法 A 解決（changelog v0.23）**：新增序列化 `BakedDuration`（烘焙期自 `clip.length` 快照）、`Duration` 改讀它、`SourceClip` 降為 Editor-side provenance、`RollState` 退化條件由「引用是否為 null」改為「**值是否 > 0**」。測試 73 → **76**。**⚠️ 需重烘 8 顆 Bake 資產，見下方清單。** 原始診斷保留於下：
   ```
   MotionBakeData.cs:88   public float Duration => SourceClip != null ? SourceClip.length : 0f;
   RollState.cs:58        _rollTimer = _rollBakeData != null ? _rollBakeData.Duration : FallbackDuration;
   ```
   fallback 檢查的是 **asset 為不為 null**，不是 **clip 為不為 null** → clip 遺失時 `_rollTimer = 0`、**Roll 第一帧就結束**，而 `FallbackDuration` 永遠用不到。**是「Roll 秒退」的同型變體**（上次根因在 asset 層＝bakeMappings 未綁，已修；這次在 clip 層，守不到）。
   - **目前不會觸發**：Roll 的 clip 是 Mixamo `X Bot@Stand To Roll.fbx`、在版控內、GUID 穩定 → 屬**潛伏缺陷**非現行 bug。
   - **修法 A（推薦）**：烘焙時把 `clip.length` 序列化成 `BakedDuration`，`Duration` 改讀它（與 `AutoAverageSpeed` 同 pattern）→ `MotionBakeData` 自此**完全不需執行期持有 clip 引用**，`SourceClip` 降為 Editor-only 溯源欄位。這也是「可抽成 Unity Plugin」需要的形狀。代價：重烘一輪。
   - **修法 B**：只在 `RollState` 補 `Duration > 0` 判斷。三字元修補，但耦合仍在，下一個消費者會再踩。
   - **待裁決，本輪未動手。**

---

## ADR-003 Migration Stage 2（2026-07-25，changelog v0.21）——✅ 程式完成、Unity 已驗證（EditMode 綠）

**完成判準已達成：Runner 不再認識任何 locomotion 概念**（並由新測試 A9 守住不回流）。ADR-003 §9-L1 結案；本輪**未改 ADR**（零 Blocking Issue）。

### 已完成
1. **新增 `Core/Movement/Models/`**：`IMovementModel`（通用抽象，兩個進入點）＋ `LocomotionModel`（MonoBehaviour，持有 `LocomotionSpeedSmoother`、寫 Movement Output、自驅 `SetFloat`）。
2. **遷移**：B9 平滑＋運動輸出導出＋動畫參數驅動全數離開 Runner（`DeriveMovementParameters` 刪除、`SyncAnimation` 只剩 `Play`、兩個平滑時間欄位移到 model）。
3. **注入鏈**：Runner 解析 `IMovementModel` → `FullBodyStateMachine.Initialize(config, data, model)` → `BaseState.Initialize(config, model)` 發給所有 state。**唯一實例＝結構保證**（本輪最大陷阱的解法）。
4. **FSM 門檻**：Idle／Move 的 `CanEnter` 改問 `IsProducingMotion`；`OnUpdateMotion` delegate 給 model（D3）。
5. **測試**：新增 **A9**（Runner 不得出現 locomotion token）／**A10**（平滑持有者唯一）；`StateMachineTests` 改用 `FakeMovementModel`。67 → **69** 條。
6. **文件**：dev-spec v0.21（§0.2／§1.1／§2.1 含新增脆弱點第 6 條／§3.1 新節／§7.1 A4・A5・A9・A10／§7.2 M3／**§7.3 結案兩列**）、design-doc v0.21（§4.8 改寫＋Trade-off 補列）、changelog v0.21（並依分卷規則把 v0.18.5／v0.18.6 移入歸檔卷）、`docs/00-map.md` 補 Models 列。

### ✅ 使用者側已完成（2026-07-25 當日回報＋磁碟核對）
1. **角色 Root 掛上 `LocomotionModel`**（與 `CharacterPipelineRunner` 同一顆 GameObject → Runner 欄位留空、`GetComponent` 補洞成立）；Accel 0.12／Decel 0.18 ＝原 Runner 值。
2. **EditMode 測試綠**（含新增的 A9／A10）。
3. **v0.19 Foundation 資產收齊**：`Locomotion.asset` 4-tier（`0 / 0.265 / 0.574 / 1`、4 clip）＋ `MotionDriver.moveSpeedSource` → `Bake_SprintFwdLoop`（`AutoAverageSpeed` 6.1008）。
   - 📌 prefab 內序列化的 `moveSpeed: 5.66` 是**舊值不必手改**——`MotionDriver` 啟動時以來源代表速度覆寫（唯一寫入時機在啟動，非熱路徑）。
   - 這也解除了先前預警的校準風險：mixer 頂 tier（Sprint）與位移滿速現已同源。

### ✅ 已回報通過
1. **§7-M1 行為等價**（含 Stage 2 的兩個迴歸點：跳躍落地不滑步、Idle↔Move 無速度跳變）。
2. **§7-M2 Profiler 0 GC** —— ✅ **自檢級達標**（2026-07-26，changelog v0.24）：量測過程中**抓到並修掉一個真的 bug**——`EvaluateTransitions` 對介面型 `IReadOnlyList<T>` 做 `foreach`，`List<T>` 的 struct enumerator 被裝箱，每帧 40 B。改索引迴圈後**穩態 `PlayerLoop` = 0 B**。
   - **量測程序已寫成 SOP → `docs/02-dev-spec.md` §7.4**（量哪裡／排除什麼／兩級判定／實測數據）。
   - 狀態切換幀約 2.6 KB，已拆解定位為 Editor-only 的 `Debug.Log`（其中 2.4 KB 是 Unity 的 `StackTraceUtility`，非我們的字串），Release 編譯移除。**不是回歸。**
   - ✅ **達標複驗完成**（同日）：Development Build 穩態 `PlayerLoop` = **0 B**；Player 側無 `EditorLoop`、CPU 7.19ms／記憶體 499.6 MB（Editor 為 31.65ms／3.38 GB）。**README 的零 GC 已升為「已驗證（Player 實測）」**——這是整份 README 唯一一條有量測數據撐著的宣稱。
   - ⚠️ **待你存檔**：把那張 Player Profiler 截圖存成 **`docs/images/profiler/gc-alloc-zero-player-walk.png`**。dev-spec §7.4.5 已用 `![]()` 內嵌、README 已連結它——**存檔前這兩處會是破圖**。
   - 🆕 `.gitignore` 新增 `/[Bb]uilds/`（原本的 `# Builds` 段只擋副檔名、擋不到資料夾，你的 `Builds/` 有 173 MB）。**證據進版控、產物不進。**
3. **changelog v0.19（Foundation 收案）** → ✅ **已於 2026-07-26 補寫**，並升格為里程碑檢查點（見下）。

---

## 文件結構優化（2026-07-25，changelog v0.20.1）——已完成，**無待辦**

起因：v0.20 完成後量測發現單一功能任務讀掉全專案 23%，讀取放大率 5×～40×。四項措施全數落地：

1. **changelog 分卷**：主檔只留最近 4 版（819 → 169 行），其餘進 `docs/changelog-archive.md`（一字未改）＋卷末版本索引表。**新增版本一律寫主檔頂端；主檔超過 4~5 版時把最舊的搬進歸檔卷。**
2. **新增 `docs/00-map.md`（45 行）**：模組 → 檔案 → 治理章節單頁索引＋「常見問題最短路徑」表。**維護規則：只記指標、不記細節。**
3. **dev-spec 分卷**（1,169 → 1,018 行）：§3.5 Foot IK → `docs/05-foot-ik.md`；§3.2 動畫呈現三小節 → `docs/06-animation-presentation.md`。**逐字搬移、章節編號原樣保留、原位留 stub → 既有引用零改寫**（全 docs 連結掃描 3/3 有效）。
4. **CLAUDE.md 新增 `Context Discipline` 章**：閱讀協定／Test-as-Spec 原則／Explore subagent 授權；並**明文推翻 2026-07-21「不回頭拆既有文件」規則**（附推翻依據與三條資格條件：已凍結、非跨領域契約、逐字搬移保編號留 stub）。

> ⚠️ **對你的唯一影響**：查 Foot IK 規格改看 `docs/05-foot-ik.md`（章節仍叫 3.5.x）；查 Animancer／Mixer 規格改看 `docs/06-animation-presentation.md`。dev-spec 原位置都有 stub 指路，不會找不到。

---

## 今日進度（2026-07-25）——ADR-003 Migration Stage 1（程式完成，待 Unity 驗證）

詳見 `docs/changelog.md` v0.20。**本輪未改 ADR-003（零 Blocking Issue）**；不新增 gameplay 功能、不提前實現 AI／Network／Vehicle。

### 已完成
1. **Stage 0 對照盤點（唯讀）**：ADR-003 D1~D5 全條款 ↔ 現有程式，三態標註（已存在相符／尚不存在／存在但形態不符）。結論：契約可完整映射，`Runner.ProcessParameters` 與 B9 的錯置屬 **ADR 自列的 §9-L1 Stage 2 遷移項**，非衝突。
2. **Stage 1 落地**（新增 5 檔／修改 5 檔）：`MovementIntentData` 黑板 region ＋ `IMovementIntentSource` ＋ `PlayerLocomotionPolicy` ＋ `GaitProfileSO` ＋ `LocomotionSpeedSmoother`（B9 抽成純運算 struct＝Stage 2 遷移單位）；管線新增**順序 2.5**；`InputData` 加中性 `SprintButtonHeld`／`WalkButtonHeld`。
3. **架構回歸檢核清單** → `docs/02-dev-spec.md` **§7**（A1~A8 自動／M1~M6 人工，各標實施方式），自動項實作為 **`ArchitectureRegressionTests`（A1~A5）** ＋ **`MovementIntentTests`（A6~A8）**，新增 **20 條**（5＋15）。
4. **文件同步**：dev-spec v0.20（§0.2／§1.1／§1.3／新增 §1.5／§2.1／§3.1／新增 §7）、design-doc v0.20（§4.1／§4.2／新增 §4.8／Trade-off 兩列）、changelog v0.20。

### ⚠️ 待使用者（Inspector／Play／Git——AI 不碰）
1. **【必做，否則角色不會動】在角色 Root（`X Bot` Prefab，掛 `CharacterPipelineRunner` 那顆）加上 `PlayerLocomotionPolicy` 元件。** Runner 的 `Movement Intent Source Component` 欄位可留空（Awake 會自動 `GetComponent` 補洞）；未掛則 Play 時 LogError 且 `MovementIntent` 恆 0。
2. **重編＋跑 EditMode 測試**：預期 0 error、**67 條全綠**。⚠️ 順帶更正文件漂移：先前紀錄的「42 條」已過時——磁碟實際 `[Test]` 為 **47** 條（無參數化測試），故本輪後為 47＋20＝**67**。**以 Test Runner 實跑數字為準**，若與 67 不符請回報。
3. **Play 行為等價驗收（§7-M1）**：**先不要建 `GaitProfileSO` 資產** ——留空時強度＝原始推桿量，手感應與本輪之前**完全一致**（加速平順、放開滑行收步、無滑步）。若有差異即為 regression，回報而非調參。
4. **（可選，行為等價驗收通過後再做）啟用 gait 方案「預設 Run／Shift=Sprint／Ctrl=Walk」**：
   - `PlayerInputSource` 新增的 `Sprint Action`／`Walk Action` 綁 Left Shift／Left Ctrl。
   - 建 `GaitProfile.asset`（`Assets/ScriptableObjects/Movement/`，選單 `Project/Core/Movement/GaitProfile`），拖進 `PlayerLocomotionPolicy`。
   - Gait intensity 是手感輸入，不再強制等於動畫天生速度比；目前採 default=0.75、sprint=1、walk=0.3651。Mixer Threshold 另採 0.35／0.75／1，並以派生 PlaybackSpeed 對齊實際速度。
5. **Profiler 0 GC 複驗（§7-M2）**：熱路徑新增的是值型別運算，預期 0 B，但仍請實測確認。

### 下一步（擇一）
- ~~**A. Stage 2（B9／MoveSpeed 歸位，收 §9-L1）**~~ → ✅ **已完成（2026-07-25，changelog v0.21）**，見本檔最上方專段。實作時發現 ADR 未預想的兩個時序陷阱（Jump 期間 dynamics 不可凍結／`SetFloat` 不可落 LateUpdate），故 model 採兩個進入點、順序 3 保留。
- **B. 先做 Foundation 收案（v0.19）／Phase C**：見下方前一輪交辦（⚠️ 收案狀態與磁碟不符，見最上方「待釐清」）。
- 📌 Stage 3（`MovementContext`、AI／Replay／Network producer、`CombatIntent`）**待真需求**，勿提前。

---

## 🔖 前一輪交辦（Foundation／Foot IK，仍有效）

> 本 session 量大（Foot IK 收案＋Locomotion Foundation＋B9＋Movement Policy ADR-003＋第三方屏蔽）。**先讀這段**；細節見 `docs/04-locomotion-foundation.md`、`docs/ADR/003-*`、下方各進度段。

### 已完成（本 session，程式全綠）
- **Foot IK v1 收案**（輪 1，changelog v0.18.7）
- **輪 2 Foundation 程式**：Foot Phase Curve stage（`MotionBakeData`+`MotionFeatureAnalysis`）／per-clip 版 `MotionClipImportSOP`／**B9 MoveSpeed 平滑**（`CharacterPipelineRunner`）＋5 新測試
- **Kubold 盤點**（docs/04）＋Import/Bake loops（速度真相 Walk 1.62／Run 3.50／Sprint 6.10 m/s）
- **Movement Policy 四輪對抗式評審**（docs/04 §11–14）→ **`docs/ADR/003-movement-intent-layering.md`**（Accepted＝契約定案、程式未實作；含 §13 四點責任邊界）
- **`.gitignore` 加第三方資產排除**（本段最後任務，SOP 見下）

### 待使用者（Inspector／Git／實測——AI 不碰）
1. **第三方資產屏蔽 SOP**（↓ 專段，你執行 git）
2. **Foundation 資產**：docs/04 §10 — `Locomotion.asset` 擴 4-tier（Idle 0／Walk 0.265／Run 0.574／Sprint 1.0；Sync 開 Walk/Run/Sprint）＋`MotionDriver.moveSpeedSource`→`Bake_SprintFwdLoop`；**重烘 4 支 loop** 補 FootPhaseCurve；Play 驗（按 W 平順加速無滑步）
3. **鏡頭**：角色 Root 拖入 Main Camera 的 `Third Person Camera.Target`、`Mouse Sensitivity` 2→0.1

### 待裁決／下一步（擇一起手）
- **A. Movement Intent Migration Stage 1（動程式）**：審 ADR-003 → 核可 → 落地最小 seam（`MovementIntent` region＋`IMovementIntentSource`＋`PlayerLocomotionPolicy`＋`GaitProfileSO`；行為等價＋順帶落地最初想要的「預設 Run／Shift=Sprint／Ctrl=Walk」）。**Stage 1 紀律：`MovementIntent` 唯一真相、`MoveSpeed` 過渡衍生值（ADR §13.4）**
- **B. Phase C**：停步分腿姿勢（stop 動畫＋Foot Phase 選腳別）＋Starts/Stops/Turns 導入（烘焙曲線驅動＝Roll 先例，per-clip 套 preset）＋承載定案
- 停步姿勢＝loop 無收步語意、非 Blocking，**建議歸 Phase C**（待確認）
- **changelog v0.19（Foundation 收案）** 待 Play 綠燈補

### 🚫 第三方資產屏蔽 SOP（你執行；AI 不碰 git）
現況：Animancer Pro／Kubold／StarterAssets **已被 git 追蹤**（~224MB），`.gitignore` 已加排除但**已追蹤檔需手動取消追蹤**才生效。
```bash
# 0) 最關鍵：確認 repo 為 PRIVATE（公開才觸發 EULA 二次散佈問題）
# 1) 取消追蹤（本機檔案保留、Unity 照常運作；只從 git index 移除）
git rm -r --cached "Packages/com.kybernetik.animancer"
git rm -r --cached "Assets/MovementAnimsetPro" "Assets/MovementAnimsetPro.meta"
git rm -r --cached "Assets/StarterAssets" "Assets/StarterAssets.meta"   # StarterAssets：確認專案不依賴再做
# 2) commit
git commit -m "Untrack third-party paid assets (Animancer Pro, Kubold); enforce via .gitignore"
```
- ⚠️ **歷史殘留**：上述只停「未來」追蹤；資產仍在**過去 commit 的歷史**裡。solo private repo → 保持 private 即足夠。**若曾公開／要公開** → 需 `git filter-repo` 重寫歷史清除（destructive，先備份）。
- ⚠️ **fresh clone 不可編譯**：Animancer＝執行期核心依賴、Kubold＝Bake 資產 GUID 引用來源。**建議 repo 加 `README` 註明必要資產與各自重匯入方式**（要我下輪寫可講）。
- X Bot／Mixamo（角色本體，免費但 Adobe 條款）：**不建議排除**（全場景依賴，破壞成本 > 低風險）；如在意另議。

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
- [ ] **🔬 Movement Policy 設計探索（`docs/04` §11 分析 ＋ §12 Architecture Review，純分析未改程式）**：發現目前**無速度模式選擇層**（`InputData` 無 modifier、`ProcessParameters` 寫死 `magnitude→MoveSpeed`）。§11 初提 MovementProfile＋Resolver；**§12 自我挑戰後部分推翻**——原案 overfit（1D-speed、擴不到 strafe/swim/vehicle）、seam 綁 input（netcode/AI 敵對）、DIP 弱。**修訂設計（§12.3）**：seam 上移黑板中性 **`MovementIntent`**＋介面化 **`IMovementIntentSource`**（player/AI/replay 可換）＋**model 走既有 `OnUpdateMotion` seam**＋gait profile 收窄＋mode/toggle state 進黑板。務實 staging：現在只放最小正確 seam，其餘加法。停步分腿姿勢＝loop 無收步 → Phase C（stop 動畫＋Foot Phase）。**§13 Architecture Validation（Runtime Data Flow Diagram）已完成**——畫圖時再修 3 點：R1 MovementIntent＝模型無關 intensity+dir（非 gait）、R2 B9 屬 Locomotion model（現況在 Runner＝待遷移殘餘耦合）、R3 producer context-free（無循環）。6 問驗證全過（ownership 單寫/lifetime snapshot-able/DIP 反轉/唯一無害 1-frame 回饋/seam 模型無關）。**§14 Design Review R2**：使用者再挑戰，抓出 3 條混淆軸線（皆成立）——①Movement Model（context 軸）≠ Gameplay State（action 軸），正交、需獨立 `MovementContext` resolver；②Blackboard 應 domain-partitioned intents（MovementIntent/CombatIntent/InteractionIntent）非單一 god-Intent；③MoveSpeed 屬 Locomotion model 內部、各 model 自驅動畫參數走通用 Facade（Facade 本身即抽象、**不需** IAnimationModel）。**§14.6/14.7 v3 圖（三軸分離）已重畫並複驗——無新裂縫、設計收斂**：Ownership/Lifetime/R-W/DIP/循環/耦合 六項全過；唯一殘餘＝B9 在 Runner（列 ADR known-migration）；補 nuance＝ambient state(Idle/Move) delegate model、intrinsic-motion state(Roll/Jump/Attack) 本就 override OnUpdateMotion（既有機制）。**✅ `docs/ADR/003-movement-intent-layering.md` 已撰寫**（Status/Context/Problem/Decision 5 契約/Diagram/Responsibility Matrix/Alternatives〔完整保留否決 BaseState-Shift／MovementModeResolver／Input-Modifier 三案理由〕/Trade-offs/Consequences/Known Limitations L1-L6/Migration Plan Stage 0-3+/Future Extension）。狀態＝Accepted（契約定案、程式尚未實作，比照 ADR-002）。**§13 補四點責任邊界**：①MovementIntent schema 僅適「方向性移動家族」非萬用（異質 model 開兄弟 schema）②MovementContext 描述性、不否決 State——Gameplay Authority 屬 Capability/Profile（how vs what's-allowed vs doing 三權分立）③Producer 不管 context-sensitive input，Input Routing 在上游(action map/Input Router)④Stage1 MovementIntent 唯一真相、MoveSpeed 僅過渡衍生值(禁繞過 intent 直寫)。**下一步待使用者核可 → Migration Stage 1（最小 seam：MovementIntent region＋IMovementIntentSource＋PlayerLocomotionPolicy＋GaitProfileSO，行為等價重構）**。實作時才更新 design-doc/dev-spec（ADR §10 文件責任）。停步歸 Phase C 待確認。
- [ ] **輪 2 Locomotion Foundation 進行中**（規劃 `docs/04`）。**已裁決**：四段 Idle/Walk/Run/Sprint（速度段數由資產決定，Jog 不硬補）、Humanoid retarget X Bot、承載延到 Foundation 驗證後。**已完成**：Import＋Bake（loop 速度真相有效——Walk 1.62／Run 3.50／Sprint 6.10 m/s；門檻 = speed/6.10）。**程式已落地**：Foot Phase Curve stage（`MotionBakeData`+`FootPhaseCurve`欄位/`GetFootPhaseAt`；`MotionFeatureAnalysis`+`FootPhaseCurveAnalyzer`+註冊）＋per-clip 版 `MotionClipImportSOP`（選子 clip 只套那幾支）。**SOP 誤用診斷**：主 FBX 全 clip 被灌 loopTime:1，但只波及未用到的非 loop clip（4 支 loop 完好）→ 不重下載，per-clip 工具已備供 Phase C。**待使用者**：重編 0 error → 重烘 4 支 loop 補 FootPhaseCurve → 我接 Mixer 擴充/Calibration。
- [ ] **鏡頭修復**：程式加了 Fail-Fast（target null 報錯）；**待使用者在場景**把角色 Root 拖入 Main Camera 的 `Third Person Camera.Target`，並把 `Mouse Sensitivity` 2→0.1。Cinemachine 為未來打磨選項（已裝 2.10.7），非本輪必要。

### Todo（輪 2，依 `docs/04` §7／§9 拆分）
- [x] Import Preset（loops）＋Bake（loops 速度真相）✅
- [x] Foot Phase Curve stage 程式（analyzer＋欄位）✅／per-clip SOP 工具 ✅
- [ ] 使用者重編 + 重烘 4 支 loop（補 FootPhaseCurve）
- [x] **Mixer 擴充 + Calibration SOP 已出（docs/04 §10）**——查證 `MoveSpeed=[0,1] × moveSpeed` 自洽，**零程式改動**（驗證資產決定規格原則）
- [ ] **使用者 Inspector 作業**：`Locomotion.asset` 擴 4 children（Idle 0 / Walk 0.265 / Run 0.574 / Sprint 1.0；Sync 開 Walk/Run/Sprint）＋`MotionDriver.moveSpeedSource` → `Bake_SprintFwdLoop`。Play：按 W 以 Sprint 6.10 前進無滑步（中間 tier 待 B9/analog）
- [x] **Phase D B9 參數平滑 ✅**（`CharacterPipelineRunner`：SmoothDamp 平滑 MoveSpeed＋減速保留方向；Runner-local、零 GC、FSM 零改動；手感 tunable moveSpeedAccel/DecelTime）→ **待 Play 實測調手感**
- [ ] Phase C Starts/Stops/Turns 導入（烘焙曲線驅動＝Roll 先例；per-clip 套 preset）＋**承載方式實測定案**

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
