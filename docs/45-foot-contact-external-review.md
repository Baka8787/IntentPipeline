# 45 — 腳部接觸的外部架構校準：移動面 ≠ 接觸面時的擺盪／落腳（2026-09-25，研究，⛔ 未實作）

> **定位**：研究筆記（外部架構校準）。**本地量測是主要證據**（`docs/05` §3.5.4.4、`docs/44` §9.5–§9.10）；
> 外部來源只用來校準架構方向，不覆蓋實測。只引用官方文件／原始碼 API、論文、GDC 簡報。
> **問題**：root／膠囊沿連續斜面（樓梯移動面）移動，畫面上的腳必須踩在離散踏面上時，
> 擺盪軌跡、擺盪→落腳過渡、目標構建 vs 權重混合、骨盆、鎖腳、樓梯分別怎麼處理。T 系列暫停實作，等本研究結論。

## 1. 來源

| 代號 | 來源 | 性質 |
|---|---|---|
| **UE-FP** | Unreal Engine `AnimNode_FootPlacement` 與其設定結構（Plant／Pelvis／Trace／Interpolation Settings）Python API 文件 | 官方 API 文件（引擎內建的腳部放置節點） |
| **AC** | Clifford Roche、Carlos Torres-Cros，*Fitting the World: A Biomechanical Approach to Foot IK*，GDC 2016（Ubisoft，Assassin's Creed Syndicate） | 量產技術簡報 |
| **RJ** | Rune Skovbo Johansen，*Automated Semi-Procedural Animation for Character Locomotion*，碩士論文，Aarhus University，2009（Unity Locomotion System 的基礎）第 7 章 | 論文＋公開實作 |
| **MM** | Simon Clavet，*Motion Matching and The Road to Next-Gen Animation*，GDC 2016（Ubisoft） | 量產技術簡報 |
| **U-IK** | Unity Scripting API `Animator.SetIKPositionWeight` | 官方文件 |

## 2. 逐題回答

### A. 成熟系統只改 IK 權重，還是構建依相位變化的目標軌跡／參考空間？

**構建目標，不是只調權重。** 三個系統都在「目標」這一側下功夫：
- **RJ** 擺盪軌跡是逐帧**構建**的：在上一個落腳點與預測的下一個落腳點之間插值，依地形切線加一段抬升弧避開台階，
  再抬到支撐腳所在的地面高度，最後把動畫原本的抬腳與側移**疊加**上去（§7.3.2–§7.3.7）。
- **AC** 每隻腳獨立預測落點（位置＋混合時間／比例），並把動畫的腳高重新解讀為「相對腳路徑」的高度（投影片〈Predictive Foot Motion〉）。
- **UE-FP** 以 plant／unplant 狀態、地面 trace、彈簧插值決定每隻腳的目標，節點的 alpha 只是整體強度。
- **U-IK**：Unity 的權重語意就是「0＝動畫原始位置，1＝目標」——只在 FK 與單一目標之間線性混合。
  ⇒ 在 Unity Humanoid IK 裡要表達「軌跡構建」，就是**逐帧構建目標、權重維持高值**；這正是 T1「權重＝1、目標由參考面構建」的形式。

**本地證據一致**：只調權重（M0／M1）在樓梯上兩難——權重高就滑行中貼地，權重低就穿進台階（`docs/05` §3.5.4.4）。

### B. 「擺盪時保留相對移動／參考面的軌跡，落腳時混向真實接觸面」是已知、站得住的模式嗎？

**是，而且幾乎是逐字對應。**
- **UE-FP** `simple_collision_influence`：擺盪中對齊簡化碰撞還是複雜碰撞的比例；說明直接寫到樓梯常用簡化的斜坡碰撞，
  追蹤簡化幾何可讓擺盪中的腳軌跡更平滑。——這就是「擺盪看移動面、落腳看真實面」。
- **AC**「動畫中的高度＝腳路徑上方的高度」「絕不讓腳低於腳路徑」；樓梯與峰頂用 **Ground Envelope**（沿腳路徑的地面點取**凸包**，得到連續路徑，「只給腳用」）。
  我們由產生器沿**踏面前緣連線**生成的移動面（`docs/44` §3），幾何上就是樓梯剖面的凸包。
- **RJ** 軌跡以前後落腳點之間的連續線為基準，再加動畫原本的抬腳（§7.3.7）；腳在落點附近才被落點約束完全接管（§7.2.2）。

### C. 過渡通常怎麼控制？

**組合，而且是連續的**：
- **UE-FP**：腳速來自動畫的速度曲線（`speed_curve_name`）；低於 `speed_threshold` 視為踩住，
  `speed_threshold` 到 `unalignment_speed_threshold` 之間是連續過渡，文件說這段應大致對應腳掌滾動期；另加 `distance_to_ground`（離落腳平面多近才完全對齊）。
- **AC**：接觸由**資料**定義（動畫標記的 Left／Right Contact 區段，自動產生）；產生依據是腳尖速度＋腳尖高度，再加「IK 姿勢過濾」
  （假設地面、解一次腳、檢查位置與旋轉誤差）得到信心值。預測另外提供混合時間／比例。
- **RJ**：由動作分析得到的關鍵時間點（抬腳、離地、觸地、踩實）與相位（支撐／擺盪／腳掌滾動），腳掌滾動期做插值。
- **MM**：動畫中腳尖幾乎不動時鎖住腳尖；接觸事件在動作資料上標記。

**與 T1 的對照**：T1 的 β＝高度 × 水平速度相位，用的是同一類訊號（高度＋速度）、也是連續範圍，方向正確；
差別在外部系統把速度／接觸放在**動畫資料**（烘焙曲線或標記），不是每帧有限差分。

### D. 接觸面本身是階梯狀時，怎麼避免目標跳變？

- **擺盪段**：不對離散面取樣——**AC** 用凸包 Ground Envelope（連續路徑），**UE-FP** 用簡化碰撞，**RJ** 用落腳點之間的平滑地面高度軌跡（S 形曲線）。
- **落腳段**：目標是**這一步的落點**，不是「腳現在正下方」：**AC**／**RJ** 都預測每一步的落點；**RJ** 指出預測會一直更新到觸地，
  且越接近落點、平滑越強（§7.2.2）。**UE-FP** 用彈簧插值（`floor_linear_stiffness` 等）追地面。
- **踩住之後**：鎖住（見 E／F），不再重新取樣。
- **對照 T1 的量測**：T1 目標每帧跳變 p99 9–15 cm，來源正是「落腳端取的是腳**當下**正下方的踏面（`GroundY`，還含抬升）」——
  β 上升期間腳還在跨階緣，踏面每帧換階。**這是外部系統明確避開的做法。**

### E. 責任劃分

| 責任 | 外部做法 | 我們 |
|---|---|---|
| 移動／碰撞面 | 膠囊走簡化碰撞；UE-FP 把它當成擺盪參考之一 | ✅ 產生式移動面（`CharacterMovementSurface`），只給移動與身體參考 |
| 擺盪軌跡 | 在連續參考（腳路徑／包絡／簡化碰撞）之上疊動畫抬腳；絕不低於參考 | ⏳ T1（移動面＋動畫離地高度） |
| 落腳／接觸 IK | 每一步一個落點；觸地附近才完全接管；接觸後鎖住，偏離過大才放開 | ❌ 目前是「當下正下方」逐帧取樣、沒有鎖腳 |
| 骨盆／身體 | **UE-FP**：可選「所有腿／只看踩住的腳／上坡看前腳、下坡偏低腳但避免過度壓縮」，可設定先抬腳跟或先降髖（`heel_lift_ratio`），上限 `max_offset`；**AC**：沿「上一落點→預測落點」坡度、以支撐腿決定（上下坡不同）、臨界阻尼彈簧去彈跳；**RJ**：每條腿算「保持原彎曲度」與「完全伸直」兩個高度，平滑合成（刻意避免 min 造成二階不連續），寧可多彎不可過伸 | ◐ 參考面包絡（常數下沉）——與 AC「沿坡度」同向，但深度不看落腳可達性、會碰上限 |

### F. 與我們觀察到的失敗模式對應

| 我們觀察到的 | 外部對應 |
|---|---|
| **IK 提早貼地**（動畫腳還以 6 m/s 滑行就被貼地） | AC：反應式 IK 被形容為遲鈍、看起來很程序化，改成預測式；UE-FP：用速度區間定義腳掌滾動期，而不是高度門檻 |
| **擺盪腳穿進台階** | AC 明列「小障礙、樓梯與峰頂」的腳穿透，解法是 Virtual Ground／Ground Envelope（凸包）；UE-FP：擺盪用簡化碰撞；RJ：依地形切線抬升 |
| **目標在踏面間跳變** | 外部都用「每步一個落點＋接觸後鎖住」，擺盪用連續路徑，不在離散面上逐帧取樣 |
| **骨盆過度補償**（碰 0.35 上限、半蹲） | UE-FP `max_offset`（超過就搆不到）、`max_offset_horizontal`（防止腳鎖住時髖降太低）、`heel_lift_ratio`；RJ：過伸會僵、取 min 會二階不連續 |
| **鎖到錯的階** | UE-FP `unplant_radius`／`unplant_angle` 與 `replant_*_ratio`（偏離過大就放開、滯後重鎖）；AC「不要完全鎖死」（鎖定／允許微滑／解鎖三態） |

## 3. 對照表

| 外部模式 | 我們目前的 T1 | 相符／不符 | 需要的改動 |
|---|---|---|---|
| 擺盪用連續參考（UE 簡化碰撞；AC 凸包包絡；RJ 落點間地面軌跡） | 擺盪相對移動面（＝踏面前緣連線的凸包） | ✅ 相符 | 無；保持「只給腳用」語意 |
| 動畫腳高＝參考之上的高度，絕不低於參考（AC；RJ 疊加原始抬腳） | 目標＝參考 ＋ max(0, 動畫離地高度) | ✅ 相符 | 無 |
| 以**目標構建**表達軌跡，權重只是強度（UE-FP、RJ；Unity 權重語意） | 權重＝1，目標由參考面構建 | ✅ 相符 | 無 |
| 過渡用連續的速度＋接近度（UE 速度曲線區間＋`distance_to_ground`；AC 腳尖速度／高度＋姿勢信心） | β＝高度 × 執行期水平速度相位 | ◐ 訊號與形式相符；來源不同 | 建議：速度／接觸改由**烘焙資料**提供（專案已有 `MotionBakeData`），執行期有限差分只作退路 |
| **每一步一個落點**，越接近觸地越穩（AC、RJ） | 落腳端取腳**當下**正下方的踏面，逐帧重取 | ❌ **不符**（實測：目標跳變 p99 9–15 cm） | **必要**：落腳端改為這一步的落點（預測或在 β 開始上升時決定），用真實踏面高度（不是含抬升的 `GroundY`），越接近觸地越穩 |
| 接觸後**鎖腳**，偏離過大才放開、滯後重鎖（UE lock／unplant／replant；AC 三態；MM 腳尖鎖定） | 沒有鎖腳；踩住後仍跟著動畫腳位與當下踏面 | ❌ **不符** | **必要**：鎖腳狀態＋滯後（先做位置鎖、旋轉自由，對應 AC「最常用」的情境）；它同時消除支撐期的踏面重取 |
| 骨盆由踩住的腳／支撐腿與可達性決定、可先抬腳跟再降髖、彈簧平滑（UE、AC、RJ） | 參考面包絡常數下沉，不看可達性，碰 0.35 上限 | ◐ 方向與 AC「沿坡度」一致；深度控制不符 | **不擋 T 系列**。T 系列讓擺盪腳不再依賴 FK 高度後，重新評估骨盆（可達性／抬腳跟優先）；root 離面距離另案（Motion 層） |
| 移動／碰撞面與接觸面分離（UE 簡化 vs 複雜碰撞） | 產生式移動面 vs 踏面 | ✅ 相符 | 無 |
| IK 每帧從原始姿勢重解、不依賴上一帧結果（RJ §7.4.4） | 從 pre-IK pose 快照解 | ✅ 相符 | 無 |

## 4. 結論

**結論 2：T 系列方向正確，但實作前需要特定修改。**

外部三個獨立來源（UE 引擎節點、Assassin's Creed 量產系統、Unity Locomotion System 論文）都採用同一個架構：
擺盪軌跡建在連續的參考上（簡化碰撞／凸包包絡），動畫腳高是參考之上的高度，權重只是強度，落腳時才交給真實接觸面。
T1 的核心（參考面切換、權重＝1、保留動畫離地高度）與此一致，而且我們的移動面在幾何上就是 AC 所說的凸包包絡。

但 T1 在**落腳端**與外部做法不同，而那正是實測剩下的問題（目標跳變 p99 9–15 cm）：

1. **落腳目標要是「這一步的落點」**，不是腳當下正下方逐帧重取的踏面；並改用真實踏面高度（`GroundY` 含腳跟／腳趾抬升，擺盪中可高 10 cm）。
2. **加上鎖腳**：踩住後鎖位置、旋轉自由；偏離過大才放開、滯後重鎖。它同時讓支撐期不再重取踏面。
3. **（建議，非阻擋）** β 的速度／接觸訊號改由烘焙資料提供，對齊 UE 的速度曲線與 AC 的接觸標記；執行期有限差分作退路。
4. **骨盆不在 T 系列範圍內**，但外部都由踩住的腳與可達性決定骨盆；T 系列落地後再重評（root 離面距離仍是 Motion 層獨立議題）。

下一步（待使用者裁決）：依上述 1、2 修改 T 系列，先離線模擬「每步落點＋鎖腳」對目標連續性、穿透、觸地→擊發的影響，再送 Codex 審查。

## 5. 參考連結

- UE-FP：[AnimNode_FootPlacement](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/AnimNode_FootPlacement?application_version=5.4)、
  [FootPlacementPlantSettings](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/FootPlacementPlantSettings?application_version=5.3)、
  [FootPlacementTraceSettings](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/FootPlacementTraceSettings?application_version=5.4)、
  [FootPlacementPelvisSettings](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/FootPlacementPelvisSettings?application_version=5.4)、
  [PelvisHeightMode](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/PelvisHeightMode?application_version=5.4)、
  [FootPlacementLockType](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/FootPlacementLockType?application_version=5.4)、
  [FootPlacemenLegDefinition](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/FootPlacemenLegDefinition?application_version=5.4)、
  [FootPlacementInterpolationSettings](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/FootPlacementInterpolationSettings?application_version=5.4)
- AC：[GDC Vault 摘要](https://www.gdcvault.com/play/1023316/Fitting-the-World-A-Biomechanical)、[投影片 PDF](https://media.gdcvault.com/gdc2016/Presentations/Roche_Clifford_Fitting%20the%20World.pdf)
- RJ：[論文頁](https://runevision.com/thesis/)、[論文 PDF](https://runevision.com/thesis/rune_skovbo_johansen_thesis.pdf)（第 7 章，p.66–90）、[Locomotion System 原始碼](https://github.com/runevision/LocomotionSystem)
- MM：[GDC Vault](https://gdcvault.com/play/1023280/Motion-Matching-and-The-Road)、[文字稿（Internet Archive）](https://archive.org/stream/GDC2016Clavet/GDC2016-Clavet_djvu.txt)
- U-IK：[Animator.SetIKPositionWeight](https://docs.unity3d.com/ScriptReference/Animator.SetIKPositionWeight.html)
