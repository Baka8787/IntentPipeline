# Camera ＋ Aim ＋ Throw-by-AimPoint（WP1 實作規格）

> **狀態**：🟢 **裁決完成（2026-08-31），規格已定案；等 ADR-004 硬停止線滿足後才派 Codex。**
> §4 的 D1–D5 已由使用者逐條拍板（D1 採納／D2 採納／D3 先 (a) 但列為**強制** Play 驗收項／D4 採納／D5 不使用 Cinemachine）。
> **同一次裁決另新增 scope boundary 與完成線，見 §2.3**——其中**基本 camera collision 由 Non-goal 改為 In Scope**（覆寫本檔初稿與 WORKLOG polish 桶的分類）。
> **ADR 路由**：**不開 ADR**。四條判準（黑板 schema／ownership 變更；FSM 拓撲或 hierarchy 變更；管線順序或核心驅動介面變更；推翻既有架構不變量）**一條都不成立**——本包全部落在 Presentation 層。
> 依 CLAUDE.md routing rule，直接寫 Living Doc 分卷（本檔），不進 ADR、不進 Trial。
> **前置**：ADR-004 硬停止線。**2026-08-31 使用者回報：A–D／F 已通過 Play 驗收，僅 E（零 GC）未過**；
> 該次 GC 已定位並修正（`BaseState.AnimationKey` 的 `Enum.ToString()` 每帧配置，見 ADR-004 §11），
> 使用者裁決**不再複測**、**直接派 Codex 開工 WP1**。
> ⚠️ **據實記錄**：ADR-004 的狀態欄仍為 🟡 `Trial`，**E 的驗證方式是「修正 ＋ A23 自動測試」而非重新量測**；
> `Trial → Accepted` 的狀態翻轉與 G1–G7 打勾**仍屬使用者側**（獨立 commit C1）。
>
> **🏗 Architecture Qualification（本包要證明什麼）**：**「相機／瞄準是純 Presentation 關切」**——
> 交付時 **黑板 schema 零改動、管線階段零新增、`ArchitectureRegressionTests` 條數不變**。
> 這是一個**負面證明**：不是每個新功能都要動核心契約。**因此「有沒有加欄位」本身就是驗收項**，不是實作細節。
>
> **🎬 Portfolio Qualification**：影片段落 1（探索鏡頭讓既有 locomotion 終於好看）／2（鏡頭切換與瞄準）／3（遠程 Throw ＋ soft target）。

---

## 1. Problem

現行相機是專案裡**唯一沒有被架構紀律整理過的模組**。它在 M1 之前就存在，之後每一輪都只是被動打補丁（游標閘門、移除第二個 `Cursor` 寫入者），從未被當成一個有契約的模組看待。三個病灶：

### 1.1 FU-5：相機旋轉是雙權威（**「滑但不好瞄」的根因**）

[`ThirdPersonCamera.LateUpdate`](../Assets/Scripts/Presentation/Camera/ThirdPersonCamera.cs) 現行結尾四行：

```csharp
Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0);
Vector3 targetPosition = target.position + (rotation * offset);
transform.position = Vector3.Lerp(transform.position, targetPosition, followSpeed * Time.deltaTime);
transform.LookAt(target.position + Vector3.up * 1.5f);   // ← 這一行把上面算的旋轉整個丟掉
```

`_yaw`／`_pitch` **只決定位置軌道**；最終 `transform.rotation` 由 `LookAt` 完全覆寫。後果：

- **玩家輸入的 pitch 不是相機的 pitch**。實際 pitch 由「軌道位置 → 胸口」的幾何反推而來，且被 `followSpeed` 的位置 Lerp 二次污染——**旋轉延遲繼承了位置延遲**。這就是「轉起來很滑、但要瞄準時對不準」的來源，**不是 damping 值調得不對**。
- 沒有任何「相機正前方」可以拿來當瞄準射線。要做 Aim，這一條必須先修。

### 1.2 FU-4：Throw 沿角色 root forward 發射，與相機無關

[`ThrowProjectileEmitter.Release`](../Assets/Scripts/Presentation/Actions/ThrowProjectileEmitter.cs)：

```csharp
ThrownProjectile projectile = Instantiate(projectilePrefab, origin.position, transform.rotation);
```

`transform.rotation` ＝ 角色 root 朝向。而角色 root 的朝向由 [`MotionDriver.ExecuteBaseMovement`](../Assets/Scripts/Presentation/Motion/MotionDriver.cs) 在**有移動輸入時**才 Slerp 轉向。因此**站著不動時，相機可以繞角色轉 180°，投擲方向完全不變**。
影片段落 3 的判準是「球會飛 ≠ 會瞄」——現況正好落在「會飛不會瞄」那一側。

### 1.3 瞄準目前沒有任何載體（且黑板裡有一個死欄位）

- `PlayerRuntimeData.AimTarget`（`Transform`）**宣告了但沒有寫入者也沒有讀取者**，唯一觸及它的是 `CharacterPipelineRunnerEditor` 的除錯顯示。它是早期預留的殘留，**不是本包的既有基礎設施**。
- ⚠️ **本包明確不使用它**，理由見 §5.2。它應被視為**應清理的死欄位**（登記為 FU-12），不是「反正已經在 schema 裡了，用它就等於零改動」的漏洞。

---

## 2. Scope／Non-goals

### 2.1 In Scope

| # | 項目 | 對應段落／FU |
| --- | --- | --- |
| S1 | 相機旋轉單一權威：移除 `LookAt`，`_yaw`／`_pitch` 同時決定位置與旋轉 | FU-5、段落 1 |
| S2 | 探索／瞄準兩種取景（offset ＋ FOV 的連續 blend），**不是狀態機** | 段落 1、2 |
| S3 | `AimResolver`：由相機射線解出**世界 AimPoint**，Presentation 私有、不進黑板 | 段落 2、3 |
| S4 | **最小 soft target**：單次非配置 cast ＋ 角度錐判定，命中則把 AimPoint 吸附到目標中心 | 段落 3 |
| S5 | Throw 依 AimPoint 發射（取代 root forward），**含退化路徑** | FU-4、段落 3 |
| S6 | FU-7 Cinemachine 決策債一次裁決 | — |
| S7 🆕 | **基本 camera collision／防穿牆**：沿 pivot → 相機的射線做一次 cast，被擋就**沿同一條線拉近**。**只改距離，不改旋轉** | §7.3；使用者裁決 2026-08-31 |
| S8 🆕 | **滑鼠靈敏度可配置** | 既有 `mouseSensitivity` `[SerializeField]` 已滿足；本包只需**確認不被 D1 的改寫弄丟**（回歸項，非新功能） |

### 2.2 Non-goals（本輪明確不做）

- ❌ **任何黑板欄位的新增／修改／刪除**（含清理 `AimTarget` 死欄位——那是獨立的清理工作，混進來會污染「schema 零改動」的證明）。
- ❌ **任何管線階段新增**（不新增順序 x.x；`AimResolver` **不是** `IPresentationController`，理由見 §6.3）。
- ❌ **上身層／Aim IK／角色上半身跟著瞄準轉**。那是動畫層的第二套權威，屬 `docs/03` 品質輪。
- ❌ **通用 targeting service／目標列表／目標切換／全域註冊表**。soft target 是「一次 cast 的當幀結果」，不保存、不排序、不記憶。
- ❌ **通用 camera state machine**。兩種取景用一顆 `float` blend，不用狀態機。
- ❌ **把相機輸入接回輸入管線**（FU-11）。理由見 §5.3——它會**逼出黑板欄位**，正好撞上本包的停止條件①。
- ❌ **動 `LocomotionModel`**（`docs/07` §13.1-R4，全工作包通用禁令）。

---

### 2.3 🎯 WP1 鏡頭完成線（**scope boundary，使用者裁決 2026-08-31**）

> **目標定位**：不是做完整 Camera Framework，而是做到**「成熟、可展示的第三人稱動作遊戲基礎鏡頭」**——
> 方向接近《刺客教條》／魂類共用的**自由第三人稱視角基礎**。
> ⚠️ **這一節是 scope 的上界也是下界**：清單達成即視為 WP1 Camera 部分完成，
> **不得因為「作品集品質」自行追加**（使用者明確指示）。

#### 2.3.1 至少完成（DoD）

| # | 項目 | 由何處交付 | 驗收 |
| --- | --- | --- | --- |
| C1 | 自由第三人稱 yaw／pitch | 既有 ＋ D1 | P2 |
| C2 | pitch clamp | 既有 `minPitch`／`maxPitch`，**D1 改寫後必須保留** | T-4、P2 |
| C3 | **單一 camera rotation authority**：移除 `_yaw`／`_pitch` 與 `LookAt` 的雙權威 | §4-D1(a) | P2、T-3 |
| C4 | **camera-relative movement 不退化** | §3-E3 的行為中性論證 | **P1（最重要的回歸項）** |
| C5 | AimPoint 可由 camera／screen center 穩定解析 | §8.1 | P6 |
| C6 | Throw 依 AimPoint 而非 `root.forward` | §8.2 | P4、P6 |
| C7 | 滑鼠靈敏度可配置 | 既有 `mouseSensitivity`（S8） | P2 |
| C8 | **停止輸入後不得明顯抖動、漂移或自行搶方向** | D1(a) 旋轉不 Lerp ⇒ 無殘留插值；**本包不引入任何 auto-recenter** | **P10** |
| C9 | **基本 camera collision／防穿牆** | §7.3 | **P11** |
| C10 ✅ | ~~若 P4 出現明顯視覺違和 ⇒ 升級 (c)~~ **已於 2026-08-31 觸發並啟用 (c)** | §4-D3(c)（形狀已定形） | P4 |

> ⚠️ **C10 的邊界**：升級 (c) 時只做**最小** facing request，**不得**順勢擴張成完整 facing system
> （不做 strafe facing、不做 idle 時的 look-at、不做 facing 優先級表）。寫入者仍只有 `MotionDriver`。
> 📌 **觸發條件已定為 `AimResolver.IsAiming`**（非「Action 期間」）——理由見 §4-D3(c) 第 6 點：
> 這是唯一每帧可用、且不需要為 sink 新增 `Update` 的訊號。實務上投擲本來就在瞄準中進行。
> ⚠️ **代價（接受）**：不按瞄準鍵直接投擲時，身體不會轉 ⇒ 仍會有前丟動畫與彈道不一致。若這成為問題再談。

#### 2.3.2 WP1 明確不做（使用者裁決，補充 §2.2）

- ❌ 完整 Souls **lock-on**
- ❌ **target switching**
- ❌ **strafing movement mode**
- ❌ **shoulder swap**
- ❌ 複雜 **camera zone**
- ❌ **電影式自動構圖**
- ❌ **多層 camera profile system**
- ❌ **複雜 obstruction avoidance**（C9 只做「拉近」，見 §7.3 的四條紅線）
- ❌ **自動 reposition／corner solving**
- ❌ 為未來需求**預先抽象大型 Camera Framework**

> 📌 **Lock-on 為什麼被切成獨立的後續 Gameplay 工作包**（使用者裁決）：
> 它會**同時**影響 Camera、Character Facing、Movement Basis（strafe 座標基底）與 Action Targeting **四個系統**。
> 那是一個跨系統的 gameplay 決策，**不是相機功能**——塞進 WP1 會讓本包的架構命題（「相機是純 Presentation 關切」）直接失效。
> 登記為 **FU-13**（§12.2）。

---

## 3. Evidence from Code（本規格的事實基礎）

| # | 事實 | 出處 | 對設計的影響 |
| --- | --- | --- | --- |
| E1 | **改動前**的 `offset = (0, 2, -3.5)`，**x 分量為 0** | `ThirdPersonCamera`（WP1 之前） | 使 §4-D1 的行為中性論證成立（見 E3）。⚠️ **這是改動前的歷史值**，現行值見 §6.2「已定案的取景參數」 |
| E2 | `camera.forward`／`right` 的**兩個消費者都壓平** `y = 0`：`MotionDriver.ExecuteBaseMovement`、`AIMovementSource.ProduceIntent` | 兩檔皆 `camForward.y = 0f` | 相機 pitch **目前零消費者** |
| E3 | 由 E1＋E2：`LookAt` 版與 `Euler(pitch,yaw,0)` 版**壓平後的水平方向完全相同**（`LookAt` 的水平分量＝ `-(rot*offset)_xz`，因**當時** `offset.x == 0` 故恆在 yaw 軸上） | 幾何 | **移除 `LookAt` 對現有移動是可證明的行為中性**；唯一改變的垂直分量沒有消費者。<br>🔴 **E3 是一次性的「遷移論證」，不是持續約束**：它只回答「這次改動會不會動到移動」。改動落地後 `rotation` 只由 yaw／pitch 決定、**與 offset 無關** ⇒ 之後 `offset.x` 要設多少都不影響移動基底（見 §6.2 勘誤） |
| E4 | 角色朝向的**唯一寫入者**是 `MotionDriver.ExecuteBaseMovement`，且**只在 `MoveDirection.sqrMagnitude > 0.001f` 時**寫 | `MotionDriver` | 站立瞄準時身體不會轉 → §4-D3 的裁決點 |
| E5 | `PlayerRuntimeData.AimTarget` 無寫入者、無讀取者 | 全域 grep | 不得當成現成基礎設施使用（§5.2） |
| E6 | `InputData.LookInput` 有採樣（`PlayerInputSource`）但**無消費者**；相機直接讀 `Mouse.current.delta` | `PlayerInputSource` / `ThirdPersonCamera` | FU-11；本包不修（§5.3） |
| E7 | 相機以 `Cursor.lockState == Locked` 當滑鼠閘門，非讀黑板 `Arbitration` | `ThirdPersonCamera` | dev-spec §7.3 已載明其成立前提與失效條件；本包**不觸發**失效條件（§5.3） |
| E8 | `ThrownProjectile` 沿 `transform.forward` 直線飛行，命中 `ActionRequestTarget` 提交 request | `ThrownProjectile` | 只要生成時的 rotation 對，彈道就對——**不需要動 projectile 本身** |
| E9 | 架構回歸測試現有 `[Test]` 共 **18** 條（A1–A5、A9–A16、A19–A23） | `ArchitectureRegressionTests.cs` | 「條數不變」的基準值。🔄 **2026-08-31 由 17 更新為 18**：ADR-004 Trial 期修 GC 回歸時新增 **A23**（`AnimationKey` 不得每帧配置）。**與 WP1 無關，WP1 仍須維持 18 不變** |

---

## 4. ✅ 裁決點（**已於 2026-08-31 由使用者逐條拍板**）

| # | 題目 | 裁決 |
| --- | --- | --- |
| **D1** | 相機旋轉單一權威的修法 | ✅ **採納 (a)** |
| **D2** | AimPoint 住在哪裡 | ✅ **採納 (a)**（Presentation 私有，不進黑板；**先不抽介面**） |
| **D3** | 瞄準時角色要不要轉向 | ✅ **先採 (a)**，但**列為 WP1 強制 Play 驗收項**（P4）；**若明顯違和直接升級既有 (c)**，不再另行討論 |
| **D4** | soft target 的形狀 | ✅ **採納 (a)** |
| **D5** | Cinemachine 用或不用 | ✅ **WP1 不使用** |

> 以下保留各裁決點的完整選項與理由，作為決策脈絡（不刪除，便於日後回溯「否決了什麼」）。

### D1 — 相機旋轉單一權威的修法

| 選項 | 內容 | 評估 |
| --- | --- | --- |
| **(a) ✅ 建議** | 移除 `LookAt`；`transform.rotation = Quaternion.Euler(_pitch, _yaw, 0)`（**不 Lerp 旋轉**，玩家輸入即所得）。位置改為繞**胸口 pivot** 公轉：`pivot = target.position + Vector3.up * pivotHeight`，`position = Lerp(position, pivot + rotation * offset, followSpeed * dt)` | 旋轉零延遲、位置仍有跟隨阻尼＝「身體會晃、準心不晃」。由 E3，對既有移動**可證明中性** |
| (b) | 保留 `LookAt`，另開一個「瞄準用的虛擬 forward」 | 兩個 forward 是雙權威的**加倍**，不是修復。否決 |
| (c) | 旋轉也 Lerp（保留現有「滑」的手感） | 手感＝準心跟不上滑鼠。這正是 FU-5 的症狀本身 |

> ⚠️ **(a) 的已知代價（必須進 Play 驗收）**：現行 `LookAt` 會讓角色恆在畫面中心；改為 pivot 公轉後，角色在畫面中的位置由 `offset` 與 `pivotHeight` 決定。
> **驗收方式是「取景是否可接受」，不是「是否與舊版相同」**——舊版的取景是 `LookAt` 的副產物，不是設計。
> 🔴 **本段原有的警告已作廢（2026-08-31 勘誤，詳見 §6.2）**：原文寫「E3 的成立前提是 `offset.x == 0`，
> 日後採用側向 offset 會使移動方向改變」。**那是錯的。**
> D1(a) 落地後 `rotation = Quaternion.Euler(_pitch, _yaw, 0)`，**與 `offset` 完全無關**
> ⇒ camera forward 恆等於 yaw 方向，`offset.x` 設多少都**不影響移動基底**。
> E3 的 `offset.x == 0` 只是「拿新舊兩版比對」時的前提，**不是落地後的持續約束**。

### D2 — AimPoint 住在哪裡

| 選項 | 內容 | 評估 |
| --- | --- | --- |
| **(a) ✅ 建議** | **Presentation 私有**：新增 `AimResolver` MonoBehaviour，以 `public bool TryGetAimPoint(out Vector3 point)` 對外；消費者以 `[SerializeField]` **具體型別**直接持有 | 黑板零改動＝本包的架構命題本身。零新增管線階段 |
| (b) | 進黑板（新增 `Vector3 AimPoint`） | **撞停止條件①**。需要 ADR，且會讓本包的負面證明失敗 |
| (c) | 沿用既有死欄位 `AimTarget` | 形式上「零改動」但實質是漏洞：`Transform` 表達不了自由瞄準的空點，且會讓一個該刪的欄位獲得續命理由。否決 |

> 📌 **為什麼不先抽 `IAimSource` 介面**：本輪只有**一個**消費者（`ThrowProjectileEmitter`）。
> CLAUDE.md「第二個使用者出現前不得建立 production abstraction」直接適用。
> **WP2 的近戰是可預期的第二個消費者**——屆時再抽介面，那時候介面的形狀會由兩個真實需求決定，而不是由想像決定。
> 這與 `IInputSource`／`IMovementIntentSource` 的歷史一致：它們都是在第二個實作出現時才長出來的。

### D3 — 瞄準時角色身體要不要轉向（**最可能在 Play 翻盤的一項**）

| 選項 | 內容 | 評估 |
| --- | --- | --- |
| **(a) ✅ 建議先做** | **不轉**。投擲方向取自 AimPoint，身體朝向維持既有規則（`MotionDriver` 只在移動時轉） | 零核心檔改動。⚠️ **已知破綻**：站立不動時相機可繞 180°，會出現「面朝北、球往南飛」 |
| (c) | 擴充**既有唯一寫入者** `MotionDriver`：瞄準期間目標朝向改為壓平的 aim 方向，且不移動時也套用 | 單一寫入者不破，但要動熱路徑核心檔。**形狀先寫死在此**（見下），避免 Play 現場發明 |
| (b) | 新增一個元件寫 `transform.rotation` | ⛔ **第二個旋轉寫入者，違反 Respect Ownership。永久否決。** |

**✅ 裁決流程（使用者 2026-08-31）**：先做 (a)，並把「站立瞄準時身體朝向」列為 **WP1 強制 Play 驗收項**（§10.3-P4）。
**判準寫死為「角色朝向與 Throw 方向完全相反而形成明顯視覺違和」**——成立即**直接升級 (c)**，不需要再開一輪討論。
這遵循 Foot IK 那一輪學到的「**先觀察，不預先處理**」（WORKLOG 交辦 ⑤），但**預先授權了處置**，避免驗收卡在等裁決。
⚠️ 升級 (c) 時只補**最小** action-facing，見 §2.3.1-C10 的邊界。

> ### ✅ **(c) 已於 2026-08-31 啟用**（P4 實測觸發，使用者預先授權）
>
> **觸發理由（使用者回報）**：Throw 動畫是**往前丟**。身體不轉時，動畫說「往前」而球飛向別處 ⇒ 明顯違和。
> 📌 **同時解掉「看起來像自動追蹤」**：追蹤感的來源正是「球離開的方向與身體朝向不一致」。
> 身體面向 AimPoint 後，前丟動畫是對的，球也離開得自然。**兩個症狀同一根因。**
>
> **實作定形（不得偏離）**
> 1. `MotionDriver` 新增 `public void RequestFacing(Vector3 worldDirection)`：存下**壓平並正規化**的方向 ＋ 幀戳記，只在當帧有效。
> 2. `MotionDriver` 以**一個私有方法**套用該請求，並在 **`ExecuteBaseMovement` 與 `ExecuteBakedCurveMovement` 兩者的開頭**各呼叫一次，套用後清除。
>    ⚠️ **兩條路徑都要**——Throw 的四個 phase **全部帶 Bake**（實查 `ThrowDefinition`），走的是 baked 路徑；
>    只改 `ExecuteBaseMovement` 會完全沒有效果。
> 3. `ExecuteBaseMovement` 內：**有 facing request ⇒ 它取代**由 `MoveDirection` 導出的朝向目標，
>    且**不受 `MoveDirection.sqrMagnitude > 0.001f` 門檻限制**（站著不動也要轉）；沒有請求則走既有規則。
> 4. 🔄 **轉向需要死區 ＋ 可調速率**（2026-08-31 Play 回饋改寫；原文為「沿用既有 Slerp 係數、不新增旋鈕」）。
>    **為什麼改**：Throw 的 loop 姿勢是**半蹲**，腳掌釘在地上。**連續的微小旋轉**會讓腳看起來一直被扭，
>    這比「不轉」更難看。標準解法是**小偏差不轉、超過門檻才一次平順轉過去**——
>    腳於是讀起來像「站定」，而不是「被拖著走」。
>    - `MotionDriver` 新增**兩個** `[SerializeField]`：`aimFacingAngleDeadzone`（度，建議起手 **15**）
>      與 `aimFacingTurnSpeed`（Slerp 係數，建議起手 **8**，低於既有移動轉向的 12）。
>    - 偏差 ≤ 死區 ⇒ **完全不轉**（不是轉很慢，是不轉）。超過 ⇒ 以 `aimFacingTurnSpeed` 平滑轉向目標。
>    - ⚠️ 死區**不得**做成進出兩個不同門檻的遲滯（那是第二套狀態）；單一門檻即可，抖動由 Slerp 吸收。
>    - ⛔ 這兩個是**構圖／手感參數**，不是架構旋鈕。除此之外不得再加第三個。
>
>    📌 **若死區＋降速仍不可接受**，正解**不是**程序化踏步：那需要 foot locking ＋ 觸發式踏步，
>    會動到**已凍結的 Foot IK**（`docs/05` §3.5.5 的五條重開條件），且屬新系統、不在 CLAUDE.md 的
>    動畫四階升級階梯上。屆時應**另立工作包**並重新評估素材（turn-in-place clip ＋ 下半身分層），
>    ⛔ **不得**在 WP1 內擴張。
> 5. `IsTimeFrozen` 時整段跳過（同既有守衛）。
> 6. **請求來源＝`AimResolver`**：`IsAiming` 為真的每一帧呼叫一次 `RequestFacing(壓平的 aim 方向)`。
>
> **⛔ 紅線**
> - `transform.rotation`（角色）的寫入者**仍然只有 `MotionDriver`**。不得新增第二個寫入者。
> - ⛔ 不得為此新增黑板欄位——request 是**同層元件間的直接呼叫**。
> - ⛔ 不得長成 facing system：**不做** strafe facing、**不做** idle 時的 look-at、**不做** facing 優先級表。
>
> **📌 已知邊界（登記，不在本次處理）**：`ExecuteBakedCurveMovement` 也被 Roll 使用，而 Roll 的曲線自帶旋轉。
> 若玩家「按住瞄準的同時翻滾」，facing request 會與曲線旋轉疊加。
> 預期影響輕微（facing 先套用、曲線 delta 疊加其上），**先觀察不預先處理**；
> 若 Play 看到打架，正解是把請求閘門綁到 FSM 狀態，**不是**在 `MotionDriver` 裡加優先級表。

### D4 — soft target 的形狀

| 選項 | 內容 | 評估 |
| --- | --- | --- |
| **(a) ✅ 建議** | 沿瞄準射線一次 `Physics.SphereCastNonAlloc`，取**角度偏差最小**且持有 `ActionRequestTarget` 的一個；命中則 AimPoint 吸附到其 collider bounds 中心 | 無列表保存、無切換、無註冊表。**當幀算、當幀用、不記憶** |
| (b) | 目標列表 ＋ 最近目標快取 ＋ 切換鍵 | ⛔ **撞停止條件③**（「soft target 開始需要目標列表／切換／跨系統查詢」）。不做 |

> **判定用 `ActionRequestTarget`**（`Core/Actions/`）而不是新的 `ITargetable`：它已經是「可以承受一次 Action request」的既有標記，
> 語意恰好等於「值得被瞄準」。**新增第二個標記型別＝第二套目標定義**，沒有換到任何東西。

### D5 — FU-7：Cinemachine 用或不用

**✅ 建議：不導入，並在本檔記錄理由，讓這筆決策債結案。**

| 理由 | 說明 |
| --- | --- |
| **撞停止條件②的風險高** | manifest 內是 **2.10.7（CM2）**。CM2 的預設輸入路徑是 `CinemachineCore.GetInputAxis` 這類**靜態全域 hook**——等於在 `PlayerInputSource` 之外引入第二個輸入權威，正是 WORKLOG 為 WP1 寫下的停止條件② |
| **本包的架構命題會被稀釋** | 要證明的是「相機是純 Presentation 關切、不需要動核心契約」。用一顆第三方黑盒相機，這個命題就沒有被證明，只是被繞過 |
| **需求規模不匹配** | 本包全部所需＝一個 pivot 公轉 ＋ 兩組取景 blend ＋ 一條射線。CM 的價值在 blend 圖、優先級、Impulse、群組取景——**一項都不在 scope 內**，且多數在明確禁止清單裡 |
| **既有先例一致** | 這與 B11（Mixer threshold 不建工具）同一判準：**投資與問題規模不匹配時，選文件化／自寫最小解** |
| **可逆** | 若 WP4 錄影階段真的需要 CM 的 Impulse 做震屏（polish 桶項目），屆時**單獨**評估，不影響本包 |

**後續**：`Packages/manifest.json` 的清理**屬使用者側**（AI 不動專案設定檔）。不清理也無妨——未使用的套件不影響架構論證，只是 dead weight。

---

## 5. Responsibility Boundary

### 5.1 三個角色，各自只擁有一件事

| 元件 | 擁有 | **不**擁有 |
| --- | --- | --- |
| `ThirdPersonCamera` | 相機的**位置與旋轉**（取景） | 瞄準語意、目標判定、投擲方向 |
| `AimResolver` | **AimPoint 的解算**（射線 ＋ soft target ＋ 瞄準意圖旗標） | 相機怎麼擺、投射物怎麼生成、角色朝向 |
| `ThrowProjectileEmitter` | projectile 的**生成與初始化**（既有職責，不變） | AimPoint 怎麼算、有沒有 soft target |

### 5.2 為什麼 Aim 不進黑板（本包的核心論證）

黑板的存在理由是**跨層溝通**：Input → Pipeline → StateMachine → Motion 之間需要一個共同的資料窗口。
而 AimPoint 的**生產者與消費者都在 Presentation 層**（相機 → 投擲發射器），**沒有任何一條跨層邊界要跨**。
把它放進黑板不會讓任何模組解耦，只會：

- 讓 `PlayerRuntimeData` 多一個**只有玩家角色有意義**的欄位（敵人的 `PlayerRuntimeData` 會有一個恆為 default 的 AimPoint）；
- 讓 A5 的 `WriterRules` 多一列，把一個純表現層的關切寫進**跨領域契約**；
- 使「加功能就要加欄位」成為專案的預設反射——那正是本包要用負面證明打掉的東西。

> 🚩 **反向 trip-wire（撞到就停下來提 ADR，不得自行加欄位）**：
> 若出現以下任一項，代表 aim **真的**跨層了，屆時走 ADR 判準①：
> 1. **Core 層**（`StateMachine`／`Movement`／`Pipeline`）需要讀 AimPoint（例如某個 State 的進入條件取決於有無目標）；
> 2. **敵人**也需要 aim（＝它不再是玩家專屬的表現層關切）；
> 3. aim 需要**跨幀保存**並被順序 7 的復位語意管理。

> ### 🔴 2026-09-06 — trip-wire ① 確認**已被跨過**（回溯認列）
>
> `docs/11` §8.3 的 action-time facing 於 **2026-09-05** 落地時，`ActionState`／`FullBodyStateMachine`／
> `CharacterPipelineRunner` 三個 **Core** 檔案開始 `using Project.Presentation.CameraControl` 並持有
> **具體類別** `AimResolver`——這正是 trip-wire ①，而且是最重的形式（依賴具體類別而非抽象）。
> 當時**沒有停下來開 ADR**（`Core/StateMachine` 的 `LayerRules` 放行整個 `Project.Presentation`，機器也沒擋）。
> ⇒ 處置已補上：**`docs/ADR/007-direction-authority.md`**（🟡 **Trial**，2026-09-06 裁決）＋ `docs/14-direction-authority.md`。
> ⚠️ 本節下方 §4-D3(c) 的兩條紅線（「不得新增黑板欄位」「不得長成 facing system」）
> **由 ADR-007 §7 重新處置**：前者保留推翻權（僅限 S3），後者語意收窄為「禁止多送出者，允許單一 authority 內部有優先序」。

### 5.3 為什麼**不**把相機輸入接回輸入管線（FU-11，登記不處理）

現況：`InputData.LookInput` 有採樣、無消費者；相機直接讀 `Mouse.current.delta`（E6）。看似「順手接一下」，實際上做不到：

- `InputData` 是 **`ref struct`，只活在 stack 且當帧銷毀**（dev-spec §1.3 的刻意設計），相機在 **LateUpdate** 執行，**拿不到**它。
- 要讓相機讀到，唯一路徑是把 look 值**存進黑板** ⇒ **新增欄位** ⇒ 撞停止條件① ⇒ 需要 ADR。
- 而 `Cursor.lockState` 閘門的成立前提（dev-spec §7.3）**本包不觸發失效條件**：瞄準取景切換**不改變游標狀態**，也不引入「游標自由但相機仍該轉」的模式。

⇒ **本包維持現狀，並把它登記為 FU-11**，附明確的觸發條件（見 §12.2）。這是誠實的邊界，不是遺漏。

---

## 6. Data Contracts

### 6.1 既有資料（**只讀，零改動**）

| 資料 | 用途 | 改動 |
| --- | --- | --- |
| `PlayerRuntimeData.CameraTransform` | 既有：`MotionDriver`／`AIMovementSource` 的相機空間基底 | **無**（本包不寫、不改語意） |
| `PlayerRuntimeData.Intent.FireRequested` | 既有：Throw 的觸發 | **無** |
| `ActionRequestTarget` | 既有：可承受 Action request 的標記 | **無**（soft target 借用它做判定，不修改它） |

### 6.2 新增資料（**全部是 Presentation 私有欄位，不進黑板**）

```csharp
// Presentation/Camera/AimResolver.cs（新檔）
[SerializeField] private Camera sourceCamera;              // 射線來源；未指派則 Camera.main
[SerializeField] private InputAction aimAction;            // 取景切換意圖（可不綁＝永遠探索取景）
[SerializeField] private float maxAimDistance = 60f;
[SerializeField] private LayerMask aimRayMask = ~0;        // 幾何命中
[SerializeField] private LayerMask softTargetMask = ~0;    // 目標搜尋
[SerializeField] private float softTargetRadius = 1.2f;    // cast 半徑
[SerializeField] private float softTargetMaxAngle = 8f;    // 角度錐（度）
private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];   // 建構期一次配置，零 GC

// Presentation/Camera/ThirdPersonCamera.cs（修改）
// ⚠️ 下列預設值為**現行實況**（2026-08-31 使用者實機調定），與本節下方「已定案的取景參數」表一致。
[SerializeField] private Vector3 offset = new Vector3(0.44f, -0.39f, -1.83f);
[SerializeField] private float pivotHeight = 1.5f;         // 原本寫死在 LookAt 裡的 magic number
[SerializeField] private Vector3 aimOffset = new Vector3(0.5f, 0.42f, -2f);
[SerializeField] private float aimFieldOfView = 45f;
[SerializeField] private float framingBlendSpeed = 8f;
[SerializeField] private AimResolver aimResolver;          // 只讀 IsAiming
private float _framingBlend;                               // 0 = 探索, 1 = 瞄準（連續，非狀態機）
```

> 🔴 **勘誤（2026-08-31，實作後修正）**：本節原本警告「over-shoulder 的 `aimOffset.x` 會讓 E3 中性論證失效、移動方向偏移」。
> **那是錯的。** D1(a) 落地後 `rotation = Quaternion.Euler(_pitch, _yaw, 0)`，**與 `offset` 完全無關**——
> camera forward（及其壓平值）恆等於 yaw 方向，不論 offset 的 x／y 是多少。
> ⇒ **側向 offset 不影響移動基底**；P5 的原判準（「是否斜走」）前提不成立，已改寫為構圖檢查。
> 原警告是拿舊 `LookAt` 實作的直覺套到新實作上，屬推理錯誤，不是實作偏離規格。

⚠️ **`offset`／`aimOffset` 的語意在 D1 之後改變了，舊值不可沿用**（2026-08-31 Play 首次驗收即撞到）：

| | 舊（LookAt） | 現版（pivot 公轉） |
| --- | --- | --- |
| 意思 | 相對**腳底**的位置，之後 `LookAt` 把鏡頭**轉回去對準胸口** | 相對 pivot 的**相機座標系**偏移，**無任何回正** |
| `y` 的效果 | 只是把相機抬高，LookAt 會補償 | **把角色推到光軸下方 `atan(y / |z|)`** |

**實例（首次 Play 的故障）**：沿用舊值 `offset = (0, 2, -3.5)`、垂直 FOV 60（半視角 30°）
⇒ `atan(2/3.5) = 29.7°` ⇒ **29.7/30 = 99%，角色貼齊畫面下緣**，且相機高度 `1.5 + 2 = 3.5 m` 造成俯視。
**看起來像相機壞掉，實際上是資料未遷移。**

**換算公式**（FOV 60 垂直 ⇒ 半視角 30°；16:9 ⇒ 水平半視角 45.7°）：

```
垂直偏離中心 = atan(offset.y / |offset.z|)      → 佔畫面比例 = 該角度 / 30°
水平偏離中心 = atan(offset.x / |offset.z|)      → 佔畫面比例 = 該角度 / 45.7°
相機高度(pitch 0) = pivotHeight + offset.y
```

### ✅ 已定案的取景參數（使用者 2026-08-31 實機調定並確認）

| 欄位 | 值 | 換算結果 |
| --- | --- | --- |
| `pivotHeight` | **1.5** | 胸口高度。⚠️ 同時是防穿牆 SphereCast 的**起點** |
| `offset` | **(0.44, −0.39, −1.83)** | 🔴 **y 為負是刻意的** ⇒ 局部 pivot 落在光軸**上方** `atan(0.39/1.83) = 12.0°` ⇒ 角色在畫面中心**上方 40%**，下方留給前方地面與空間。<br>水平 `atan(0.44/1.83) = 13.5°` ⇒ 中心左方 30%（右肩過肩）。相機高度 `1.5 − 0.39 = 1.11 m`、距離 **1.83 m** |
| `aimOffset` | **(0.5, 0.42, −2)** | 瞄準 FOV 45（半視角 22.5°）⇒ 垂直 `atan(0.42/2) = 11.9°` ⇒ 中心**下方 53%**；水平 `atan(0.5/2) = 14°` |
| `mouseSensitivity` | **0.1** | 由舊值 2 修正（舊值約為建議上限的 13 倍） |
| `minPitch` / `maxPitch` | −20 / 60 | 使用者實測可接受 |

**⛔ 不要在沒有實機比對的情況下改這些值。** 程式預設已同步為上表。

> ⚠️ **`offset.y` 與 `aimOffset.y` 的正負號相反是目前的實況，且尚未實機驗證**——
> 探索時角色在中心**上方**、瞄準時在**下方**，切換瞬間會有約 24° 的垂直擺動。
> 在 `aimResolver` 接線之前，瞄準路徑根本不會啟動，所以 `aimOffset` **從未被真正看過**。見 §12.1-R3。

**失敗對照（保留以認得症狀）**：舊值 `offset = (0, 2, -3.5)` ⇒ `atan(2/3.5) = 29.7°`，而半視角只有 30° ⇒ **99%，角色貼齊下緣**；
`aimOffset = (0.6, 1.6, -2)` ⇒ `atan(1.6/2) = 38.7°` > 22.5° ⇒ **角色整個離開畫面**。
兩者都不是程式壞掉，是**構圖參數超出視錐**。

📌 **這是純構圖參數，調整不影響任何架構性質**（不影響移動基底、不影響 AimPoint、不影響防穿牆）。

### 6.3 為什麼 `AimResolver` **不是** `IPresentationController`

| 理由 | 說明 |
| --- | --- |
| **契約不符** | `IPresentationController.Tick(PlayerRuntimeData)` 的語意是「讀黑板 → 驅動表現」。`AimResolver` **不讀黑板也不寫黑板**，它讀相機與物理世界。硬塞進去要接受一個用不到的參數 |
| **時間窗不需要** | 順序 6.5 存在的理由是「單幀事件唯一保證讀得到的時間窗」。AimPoint 不是單幀事件 |
| **會製造順序依賴** | 它必須在相機移動**之後**才有效。掛進 `PresentationPipeline` 反而讓正確性依賴 `GetComponentsInChildren` 的回傳順序——正是 `PresentationPipeline` 註解裡明文要避免的病 |

**改用 lazy per-frame 快取**解決順序問題，見 §7.2。

---

## 7. 每帧鏈路（**掛在既有順序上，不新增管線階段**）

### 7.1 鏈路

```text
Update
  順序 1–5   ── 完全不變（Runner 不認識相機，也不會開始認識）
LateUpdate
  順序 6     ── MotionDriver.Move（角色位置定案）
  [Unity]    ── ThirdPersonCamera.LateUpdate
                  ① 讀 Mouse delta（閘門：Cursor.lockState，不變）→ _yaw / _pitch
                  ② _framingBlend → Lerp(exploreOffset, aimOffset) / Lerp(baseFov, aimFov)
                  ③ rotation = Euler(_pitch, _yaw, 0)          ← 單一權威（D1）
                  ④ position = Lerp(pos, pivot + rotation * offset, followSpeed * dt)
                  ⑤ 防穿牆：沿 pivot→position 一次 cast，被擋就拉近（§7.3）
                       ⚠️ 只改「距離」，rotation 一個字都不碰 —— 否則 ③ 的單一權威當場失效
  順序 6.5   ── PresentationPipeline.Tick（不變，AimResolver 不在其中）
  順序 7     ── ResetTransientState（不變）

事件驅動（非每帧）
  ActionState phase → Release → ThrowProjectileEmitter.Release()
                                  └─ aimResolver.TryGetAimPoint(out point)   ← 此刻才解算（§7.2）
```

**Runner 零改動。管線順序表零改動。dev-spec §2.1 不需要新增任何一列。**

### 7.2 順序無關性：lazy per-frame 解算

`AimResolver` **不在 `LateUpdate` 主動計算**，而是在被詢問時計算並以 `Time.frameCount` 快取：

```csharp
public bool TryGetAimPoint(out Vector3 point)
{
    if (_cachedFrame != Time.frameCount) { Resolve(); _cachedFrame = Time.frameCount; }
    point = _cachedPoint;
    return _hasAim;
}
```

**為什麼**：`Release()` 由 `ActionState` 在**順序 5**（Update）觸發，而相機在 **LateUpdate** 才移動——
若 `AimResolver` 自己在 LateUpdate 算，`Release()` 讀到的會是**上一帧**的相機姿態。
Lazy 解算保證「誰先問誰觸發」，**同一帧內恆使用當下最新的相機 transform**，且不論兩者的 Unity 執行順序如何。
同一帧多次詢問只算一次（每帧至多 2 次 cast）。

> ⚠️ **代價（誠實記錄）**：AimPoint 在 Update 詢問時反映的是**上一帧**相機姿態（相機尚未移動），
> 在 LateUpdate 詢問時反映當帧。以現行唯一消費者（`Release()`，Update）而言＝**一帧延遲，約 16ms**。
> 這與順序 6.5 的事件發布採同一取捨（見 `PresentationPipeline` 註解）。若準心與實際彈道出現可見偏差，
> 正解是**把準心也畫在同一個 AimPoint 上**（兩者同源就不會分岔），不是把解算搬進 LateUpdate。

### 7.3 相機防穿牆（camera collision，🆕 S7／C9）

> **這一項原本是 Non-goal（polish 桶），2026-08-31 由使用者裁決移入 scope。**
> 定位是**「基礎鏡頭的必要條件」**而非打磨——牆會穿幫是外行一眼看得到的破綻，
> 而它恰好也是「自由第三人稱視角基礎」與「一顆會跟隨的攝影機」之間的分界線。

#### 7.3.1 演算法（在 §7.1 的 ④ 之後執行）

```text
dir  = normalize(position - pivot)
dist = |position - pivot|                      ← 已含 followSpeed 阻尼的結果
hit  = Physics.SphereCast(pivot, probeRadius, dir, out h, dist + skin, obstructionMask,
                          QueryTriggerInteraction.Ignore)

targetDist = hit ? max(minDistance, h.distance - skin) : dist

// ⚠️ 非對稱：拉近瞬間、推遠漸進
_currentDist = (targetDist < _currentDist)
             ? targetDist                                              ← 立即（防穿幫）
             : MoveTowards(_currentDist, targetDist, returnSpeed * dt) ← 漸進（防彈出）

position = pivot + dir * _currentDist
```

**為什麼非對稱**：拉近若也走插值，轉身貼牆的那一兩帧會**先穿進牆裡**再慢慢退出來——穿幫就發生在那幾帧。
推遠若不插值，離開遮蔽物的瞬間相機會**啪一下彈開**。
📌 **這與專案既有的兩處哲學同構**：`ComputePelvisOffset` 的「只下沉不上頂」、Foot IK 殘差的「只抬不壓」——
**單向立即、反向漸進**是本專案處理「幾何硬約束 vs 視覺連續性」的既定手法，不是這裡臨時發明的。

#### 7.3.2 四條紅線（撞到就停下來回報，**不得自行擴張**）

| ⛔ | 為什麼 |
| --- | --- |
| **不得改 rotation** | 一旦碰撞會轉相機，D1 建立的單一旋轉權威**當場失效**，且玩家會感覺鏡頭被搶走 |
| **不得改 yaw／pitch** | 同上，且會污染 AimPoint（瞄準方向被牆決定＝不可接受） |
| **不做 corner solving／自動 reposition** | §2.3.2 明確禁止。被卡住時**寧可拉到 `minDistance` 也不自行找路** |
| **不做多段 cast／遮蔽物淡出／透明化** | 那是 obstruction avoidance，屬另一個量級的問題。本包只有「拉近」一種手段 |

#### 7.3.3 新增欄位（Presentation 私有，仍不進黑板）

```csharp
[SerializeField] private LayerMask obstructionMask;        // ⚠️ 必須排除角色自身的 layer
[SerializeField, Min(0f)] private float probeRadius = 0.25f;
[SerializeField, Min(0f)] private float collisionSkin = 0.1f;
[SerializeField, Min(0f)] private float minDistance = 0.6f;
[SerializeField, Min(0f)] private float returnSpeed = 6f;
private float _currentDist;
```

⚠️ **`obstructionMask` 未設定（含角色自身 layer）＝相機恆被自己的 collider 擋住、貼死在 `minDistance`。**
這是**最可能的接線失誤**，已列為 Play 驗收 P11 的第一項與 §11.5 的接線備註。

---

## 8. Aim 解算與 Throw 方向

### 8.1 AimPoint 解算（`AimResolver.Resolve`）

```text
① ray = sourceCamera.ViewportPointToRay(0.5, 0.5)          ← 螢幕中心，與準心同源
② Physics.Raycast(ray, out hit, maxAimDistance, aimRayMask, QueryTriggerInteraction.Ignore)
      命中 → basePoint = hit.point
      落空 → basePoint = ray.origin + ray.direction * maxAimDistance   ← 天空也有 AimPoint，永遠可瞄
③ soft target：SphereCastNonAlloc(ray, softTargetRadius, _hitBuffer, maxAimDistance, softTargetMask)
      對每個命中取 GetComponentInParent<ActionRequestTarget>()
      過濾：非自己、在角度錐 softTargetMaxAngle 內
      取角度偏差最小者 → point = collider.bounds.center
④ 無 soft target → point = basePoint
```

**退化路徑（比照 Foot IK 的紀律：落空就退回，⛔ 不關閉功能）**：
`sourceCamera == null` ⇒ `TryGetAimPoint` 回 `false`，消費者退回既有行為（§8.2）。**不丟例外、不停用投擲。**

### 8.2 Throw 依 AimPoint（`ThrowProjectileEmitter.Release` 的最小改動）

```csharp
Transform origin = spawnPoint != null ? spawnPoint : transform;
Quaternion rotation = ResolveThrowRotation(origin.position);   // 內部處理退化
ThrownProjectile projectile = Instantiate(projectilePrefab, origin.position, rotation);
projectile.Initialize(projectileSpeed, projectileLifetime, transform.root);
```

```csharp
// static 純函數，供 EditMode 直接測（比照 ComputeFootWeight／ComputePelvisOffset 先例）
internal static Quaternion ComputeThrowRotation(Vector3 spawn, Vector3 aimPoint, Quaternion fallback)
{
    Vector3 d = aimPoint - spawn;
    return d.sqrMagnitude > MinAimSqrDistance ? Quaternion.LookRotation(d.normalized) : fallback;
}
```

- `aimResolver == null` 或 `TryGetAimPoint` 回 `false` ⇒ **逐字退回現行 `transform.rotation`**（＝未接線時行為與現在完全相同，不會靜默壞掉）。
- 退化門檻 `MinAimSqrDistance` 防「AimPoint 落在生成點上」導致 `LookRotation(zero)` 的警告與未定義朝向。
- ⚠️ **`ThrownProjectile` 一行都不改**（E8）：它沿自己的 `transform.forward` 飛，方向正確性完全由生成時的 rotation 決定。

---

## 9. Zero-GC 與 Ownership

| # | 檢核 | 做法 |
| --- | --- | --- |
| T1 | **零 GC** | 每帧至多 **3** 次 cast——相機防穿牆 1（§7.3）＋ aim 射線 1 ＋ soft target 1（§8.1，且僅在被詢問的帧才算）。皆用**非配置多載**（`Physics.Raycast(out hit, …)`／`SphereCast(out hit, …)`／`SphereCastNonAlloc` ＋ 建構期配置的 `_hitBuffer`）。無 LINQ（A3 守）、無字串插值、無 `new` |
| T2 | **旋轉單一寫入者** | `transform.rotation`（相機）＝ `ThirdPersonCamera` 獨佔，**且 §7.3 的防穿牆只准改距離**；`transform.rotation`（角色）＝ `MotionDriver` 獨佔且**本包不動**（除非 D3 升級 (c)，屆時仍是 `MotionDriver` 獨佔） |
| T3 | **黑板零寫入** | `AimResolver` 與 `ThirdPersonCamera` 皆**不持有** `PlayerRuntimeData`。A5 `WriterRules` 零改動 |
| T4 🔄 | **旋轉權威入碼** | 🔴 **本條已改寫（2026-08-31 勘誤）**。原文要求把「`offset.x == 0` 才行為中性」寫進註解——那個說法**已作廢**（§6.2）。<br>**現行要求**：`ThirdPersonCamera` 的註解必須寫明「**相機 rotation 只由 `_pitch`／`_yaw` 決定、與 `offset` 無關**，因此側向／過肩 offset 不影響 camera-relative 移動基底」，並註明**防穿牆只准改位置、不得改 rotation**。<br>⛔ **不得**再把已作廢的 `offset.x == 0` 前提寫進程式。 |
| T5 | **層級依賴** | 全部新增／修改都在 `Project.Presentation`，只單向引用 `Project.Core.Actions`（既有方向）。A4 `LayerRules` 零改動 |
| T6 | **架構測試條數** | 維持 **18**（E9）。⛔ 本包**不新增架構不變量**——若實作過程認為需要，**停下來提給 Claude**（該檔 Claude 獨佔） |

---

## 10. Tests／Play 驗收

### 10.1 EditMode（新增 `Assets/_Project/Tests/EditMode/CameraAimTests.cs`）

不碰 Physics（EditMode 無場景），只測抽出的 `static` 純函數：

| # | 測項 | 斷言 |
| --- | --- | --- |
| T-1 | `ComputeThrowRotation` 正常 | AimPoint 在右前方 ⇒ 回傳 rotation 的 forward 指向該點（容差 1e-4） |
| T-2 | `ComputeThrowRotation` 退化 | AimPoint ≈ 生成點 ⇒ **逐字回傳 fallback**，且不觸發 `LookRotation` 警告 |
| T-3 🔄 | `ComputeOrbitPosition(pivot, yaw, pitch, offset)` | yaw=0 ⇒ 相機在 pivot 正後方；yaw=90 ⇒ 在右側。<br>🔴 **原第三個斷言「`offset.x == 0` 時壓平 forward 恆等於 yaw 方向」已作廢**——它把 E3 的遷移前提誤當成持續不變量（§6.2 勘誤）。<br>**改為斷言**：`ComputeOrbitPosition` 的**回傳位置**隨 offset 改變，但相機朝向由 `Quaternion.Euler(pitch, yaw, 0)` 單獨決定 ⇒ **給定相同 yaw／pitch、不同 offset.x，壓平後的 forward 完全相同**。這才是現行要守的性質 |
| T-4 | `ClampPitch` | 超出 `[minPitch, maxPitch]` 被夾住，邊界值不震盪 |
| T-5 | `IsWithinSoftTargetCone(origin, dir, candidate, maxAngle, maxDistance)` | 錐內回 true、錐外回 false；**距離為 0 的退化輸入回 false 而非 NaN** |
| T-6 | soft target 選擇 | 兩個候選都在錐內 ⇒ 取**角度偏差最小**者（非最近者）——把選擇判準釘死 |
| T-7 🆕 | `ResolveCameraDistance(desiredDist, hasHit, hitDistance, skin, minDistance)` | 無命中 ⇒ 逐字回 `desiredDist`；命中 ⇒ `hitDistance - skin`；**命中距離小於 `minDistance` ⇒ 夾在 `minDistance`**（不得回負值或 0） |
| T-8 🆕 | `AdvanceOccludedDistance(current, target, returnSpeed, dt)` | **拉近立即**（`target < current` ⇒ 逐字回 `target`）；**推遠漸進**（`target > current` ⇒ 回介於兩者之間且單調逼近）。這條把 §7.3.1 的非對稱釘死 |

> **T-3 的意義**：它把「**相機朝向與 offset 解耦**」釘成會失敗的測試。日後若有人讓 offset 回頭影響 rotation（例如為了防穿牆而改朝向），這條會紅。
> **T-8 的意義**：非對稱很容易在後續重構時被「順手改成兩邊都插值」而悄悄消失，症狀是偶發穿幫——測試讓它不能悄悄消失。

### 10.2 架構回歸

**零新增**（T6）。既有 18 條必須全綠——特別是 **A3（零 LINQ）**、**A4（層級）**、**A5（黑板單一寫入者，本包應完全不觸及）**。

### 10.3 Play 驗收（使用者側）

| # | 項目 | 判準 |
| --- | --- | --- |
| **P1（回歸）** | 探索取景下的移動**行為不變** | 按 W 前進方向與改動前一致（E3 的實測驗證）。⚠️ 這是 D1 最重要的回歸項 |
| **P2** | 相機手感 | 滑鼠停 ⇒ 準心立刻停（旋轉零延遲）；角色移動 ⇒ 位置仍有阻尼。**「身體會晃、準心不晃」** |
| **P3** | 取景切換 | 按住 aim 鍵 ⇒ 平滑過到 over-shoulder ＋ 窄 FOV；放開平滑復原。**無跳變、無二態閃爍** |
| **P4** 🔴 | **D3 的關鍵觀察項** | 站立不動、相機繞到角色側後方 → 投擲。**球是否飛向準心？看起來是否荒謬（面朝北、球往南）？** 這一項的結果決定 D3 要不要升級到 (c) |
| **P5** 🔄 | **取景構圖**（原判準已作廢，見 §6.2 勘誤） | ①探索取景：**角色在畫面下半部、相機約肩膀高度、看得到前方空間**；②瞄準取景：角色**仍在畫面內**且偏向一側。<br>⚠️ **判準是構圖，不是「和舊版一樣」**。若構圖不對，**先檢查 `offset.y`／`aimOffset.y`**（§6.2 校準表）——這是純資料，不是程式問題 |
| **P6** | soft target | ⚠️ **先確認 `aimRayMask` 已排除角色自身 layer**（§11.5）——否則 AimPoint 會落在自己背上，投擲往腳邊飛。<br>然後：準心稍微偏離敵人時仍命中；準心明顯偏離時**不**命中（吸附不能強到看起來像自動瞄準） |
| **P7（退化）** | 未接線行為 | `AimResolver` 欄位留空 ⇒ 投擲**完全退回現行 root forward 行為**，無錯誤、無例外 |
| **P8（零 GC）** | Development Build Profiler | 穩態 `0 B/frame`（走 dev-spec §7.4 SOP） |
| **P9（仲裁回歸）** | 既有 UI 模式／暫停 | 按住 Alt／Esc 期間相機仍停轉（`Cursor.lockState` 閘門未被本包破壞） |
| **P10** 🆕（C8） | **停手後的靜止品質** | 放開滑鼠與 WASD 後：**無抖動、無漂移、不自行搶方向**。特別檢查角色停步的收步過程中鏡頭不被帶著晃 |
| **P11** 🆕（C9） | **防穿牆** | ①**先確認 `obstructionMask` 已排除角色自身 layer**（否則相機貼死在 `minDistance`，症狀像壞掉）；②角色背靠牆／柱子繞鏡頭 ⇒ 相機拉近而**不穿牆、不看到牆內**；③離開遮蔽 ⇒ 平順推遠、**不啪一下彈開**；④**鏡頭方向與準心不因碰撞而改變**（§7.3.2 第一條紅線的實測） |

### 10.4 🎬 WP1 Camera 驗收操作鏈（**使用者指定；跑完即視為 Camera 部分完成**）

> 一次連續操作走完，中途不重進 Play。**這條鏈是完成線的實測形式**，不是額外的展示要求。

```text
① 跑動
② 繞角色旋轉鏡頭                    → C1／C3（自由 yaw，旋轉單一權威）
③ 改變 camera-relative 移動方向      → C4（P1：移動基底不退化）
④ 停下                              → C8（P10：不抖動／不漂移／不搶方向）
⑤ 上下調整視角                      → C2（pitch clamp 生效且不卡頓）
⑥ 瞄準敵人                          → C5（AimPoint 穩定）＋ soft target（P6）
⑦ Throw 朝 AimPoint 正確命中         → C6（P4／P6）
⑧ 靠牆測 camera 不穿牆               → C9（P11）
```

⚠️ **⑦ 若出現「角色朝向與 Throw 方向完全相反」的明顯視覺違和** ⇒ 依 §4-D3(c) 補**最小** action-facing（已預先授權，見 §2.3.1-C10），補完後**重跑整條鏈**（(c) 會動 `MotionDriver`，P1／P8 必須重驗）。

---

## 11. Planned File Changes（**Codex 邊界**）

### 11.1 新增（Runtime）

| 檔案 | 內容 |
| --- | --- |
| `Assets/Scripts/Presentation/Camera/AimResolver.cs` | AimPoint 解算 ＋ soft target ＋ `IsAiming`；lazy per-frame 快取；純函數對外 `internal static` 供測試 |

### 11.2 修改（Runtime）

| 檔案 | 改動範圍 |
| --- | --- |
| `Assets/Scripts/Presentation/Camera/ThirdPersonCamera.cs` | 移除 `LookAt`；pivot 公轉；兩組取景 blend；**防穿牆（§7.3）**；**旋轉權威寫進註解（依 §9-T4 的現行條文）**。**游標閘門邏輯與 `mouseSensitivity`／`minPitch`／`maxPitch` 逐字保留**（C2／C7 是回歸項，不是新功能）<br>🔴 原文此處寫「E3 前提寫進註解」，該要求**已隨 §6.2 勘誤作廢**，改以 §9-T4 為準 |
| `Assets/Scripts/Presentation/Actions/ThrowProjectileEmitter.cs` | `Release()` 的 rotation 來源 ＋ `ComputeThrowRotation` 純函數 ＋ 退化路徑。**其餘（held visual／exactly-once／Cleanup）逐字不動** |

### 11.3 新增（測試）

| 檔案 | 內容 |
| --- | --- |
| `Assets/_Project/Tests/EditMode/CameraAimTests.cs` | §10.1 的 T-1～T-8（含防穿牆的兩條純函數） |

### 11.4 ⛔ 不准動（撞到就停下來回報）

- `Core/Blackboard/**`（**任何欄位增刪改**）、`Core/Pipeline/CharacterPipelineRunner.cs`、`Core/Movement/**`、`Core/StateMachine/**`
- `Presentation/Motion/MotionDriver.cs`（**除非 D3 明確裁決為 (c)**）
- `Presentation/Animation/**`、`Presentation/IK/**`
- `Assets/_Project/Tests/EditMode/ArchitectureRegressionTests.cs`（**Claude 獨佔**）
- 任何 `.prefab`／`.asset`／`.meta`／場景／`Packages/manifest.json`（**使用者側**）
- **全部 Git 操作**

### 11.5 使用者側（AI 不碰）

| 項目 | 說明 |
| --- | --- |
| 準心 UI | 螢幕中心一個 UI `Image`。**零程式**——只要與 `ViewportPointToRay(0.5, 0.5)` 同源即可 |
| 🔴 **`AimResolver` 必須掛在「玩家階層內」，不可掛在相機上** | soft target 的自我排除寫成 `candidate.transform.root == transform.root`，**依據的是 `AimResolver` 自己的 root**。掛在 Main Camera（場景根物件）上時，`transform.root` ＝ 相機 ⇒ **排除失效**。<br>⚠️ **今天掛錯不會壞，WP2 才會壞**：目前 `X Bot`（玩家）**沒有** `ActionRequestTarget`（只有 `Y Bot` 有），所以玩家不是 soft target 候選、掛哪裡都看不出差別。但 WP2 規劃要給玩家一份 `Damage` Definition ⇒ 玩家會取得 `ActionRequestTarget` ⇒ **屆時準心會吸附到玩家自己身上**，而且症狀出現的時間點與掛載位置相隔數週，極難歸因。**現在就掛對。** |
| `AimResolver` 欄位 | `sourceCamera`（指 Main Camera）、`aimAction`（建議 RMB 按住）、`maxAimDistance` 60、`aimRayMask`／`softTargetMask`（見下一列）、`softTargetRadius` 1.2、`softTargetMaxAngle` 8 |
| ✅ **取景參數（已定案）** | `pivotHeight = 1.5`、`offset = (0.44, **−0.39**, −1.83)`、`aimOffset = (0.5, 0.42, −2)`、`aimFieldOfView = 45`、`mouseSensitivity = 0.1`。<br>**已於 2026-08-31 實機調定並同步為程式預設**（換算見 §6.2）。<br>⚠️ prefab 有自己序列化的值，程式預設**不會**回頭覆蓋既有 prefab |
| **`obstructionMask`（防穿牆）** 🆕 | ⚠️ **最可能的接線失誤**：必須**排除角色自身的 layer**，否則相機恆被自己的 collider 擋住、貼死在 `minDistance`——症狀是「鏡頭黏在角色臉上」，看起來像程式壞掉。列為 P11 第一項 |
| 🔴 **`aimRayMask`（瞄準射線）** 🆕 | ⚠️ **同一級的接線陷阱，且更隱蔽**：必須**排除角色自身的 layer**。<br>`AimResolver.Resolve` 的 **soft target 迴圈有排除自己**（`candidate.transform.root == transform.root`），但**幾何射線那條沒有**——它完全依賴本 mask。而預設值是 `~0`（全部）。<br>相機在角色正後方時，`ViewportPointToRay(0.5, 0.5)` **會直接穿過角色身體** ⇒ AimPoint 落在**自己的背上** ⇒ 投擲往腳邊飛。<br>📌 **裁決（2026-08-31）：以 layer mask 解，不在程式加自我排除**——mask 是 Unity 解這件事的正統做法，執行期零成本；加 `RaycastNonAlloc` ＋ root 比對只是為同一件事多一層迴圈。<br>⚠️ **不要靠過肩偏移繞過它**：側向 offset 會讓射線偏離身體，但那是**構圖碰巧掩蓋缺陷**；相機被牆推到 `minDistance` 時射線又會穿回身體。 |
| 測試用牆／柱 | 驗收操作鏈⑧需要一面能靠的牆。可與軌 A 的場景工作合併 |
| `ThrowProjectileEmitter.aimResolver` | 拖入。⚠️ **不拖＝靜默退回舊行為**（P7），這是刻意的退化而非壞掉 |
| 敵人的 `ActionRequestTarget` ＋ collider layer | soft target 的搜尋依據 |

---

## 12. Risks／Deferred Work

### 12.1 已知風險（帶處置）

| # | 風險 | 處置 |
| --- | --- | --- |
| R1 | **D1 改變取景構圖**，主觀上「看起來不一樣」被誤判為壞掉 | 驗收判準寫死為「取景是否可接受」而非「是否與舊版相同」（§4-D1）。P1 只回歸**移動方向** |
| R2 | **D3 (a) 在 P4 被否決**，需升級 (c) 動 `MotionDriver` | 形狀已預先定形（§4-D3），不在 Play 現場發明。⚠️ 升級後 P1／P8 必須重跑 |
| ~~R3~~ 🔴 | ~~over-shoulder 的 `aimOffset.x != 0` 讓移動方向偏移~~ | **已作廢（2026-08-31 勘誤，§6.2）**：這個風險**不存在**——rotation 與 offset 無關，側向 offset 不影響移動基底。<br>🆕 **取代它的真實風險**：探索 `offset.y` 為負（角色在中心**上方**）、瞄準 `aimOffset.y` 為正（**下方**）⇒ 按下瞄準時角色有約 **24° 的垂直擺動**。**尚未實機驗證**（aimResolver 未接線前瞄準路徑不會啟動）。擺動過大就調 `aimOffset.y`，純構圖資料 |
| R4 | soft target 吸附過強 ⇒ 看起來像自動瞄準，反而**削弱**「會瞄」的說服力 | P6 是雙向判準（該中要中、**該失手要失手**）。`softTargetMaxAngle` 預設保守（8°） |
| R5 | 一帧延遲（§7.2）造成準心與彈道分岔 | 準心與 AimPoint 同源即不分岔。⛔ 不得為此把解算搬進 LateUpdate |
| R6 🆕 | **防穿牆把相機拉得太近**，貼進角色模型內部或看到模型背面 | `minDistance` 是唯一旋鈕（**純資料**）。⛔ 不得為此加「角色淡出／近距離切換模型層」——那是 §2.3.2 禁止的 obstruction avoidance。真的太近就接受並調 `minDistance` |
| R7 🆕 | **`obstructionMask` 誤設**（未排除角色自身 layer）⇒ 相機恆貼死在 `minDistance`，看起來像程式壞掉 | 已列為 P11 第一項與 §11.5 接線備註。⚠️ **這不是程式 bug，是接線失誤**——Codex 側無法防守，只能靠驗收清單擋 |
| R8 🆕 | 防穿牆的 cast 讓每帧 cast 數由 2 → **3** | 已計入 §9-T1 的預算並全走非配置多載。P8 的 `0 B/frame` 是實測判準，不靠估計 |

### 12.2 登記表（**只登記，不處理**；併入 WORKLOG 的 FU 表）

| # | 發現 | 處理時機 |
| --- | --- | --- |
| **FU-11** 🆕 | **相機是第二個輸入權威**：`InputData.LookInput` 有採樣無消費者，相機直接讀 `Mouse.current.delta`，繞過 `BlockInput` 閘門（改以 `Cursor.lockState` 代理）。**接回管線會逼出黑板欄位**（§5.3） | **不排程**。觸發條件：①出現「游標自由但相機仍該轉」的模式（dev-spec §7.3 已載明的失效條件）；或②**第三個** Presentation 元件想直接讀輸入裝置。屆時做 camera-input contract |
| **FU-12** 🆕 | `PlayerRuntimeData.AimTarget` 是**死欄位**（無寫入者、無讀取者，僅 Editor 顯示） | **不排程**。清理它會動黑板 schema ＋ dev-spec §1.1 ＋ Editor，應獨立成一筆，**不得混進本包**（會污染「schema 零改動」的證明） |
| **FU-4** | ✅ **本包解決** | — |
| **FU-5** | ✅ **本包解決** | — |
| **FU-7** | ✅ **本包裁決：不導入 Cinemachine**（§4-D5） | manifest 清理屬使用者側，可選 |
| **FU-6** | `AIMovementSource` 依賴 `CameraTransform` 把世界方向轉成相機空間 | **不排程**（WORKLOG 既有結論）。⚠️ 本包**加深**了這條張力（相機改動會影響敵人移動），但不改變其結構。正解仍是等第三個 producer 出現 |
| **FU-13** 🆕 | **Souls 式 lock-on**（含 target switching／strafing movement mode） | **後續獨立 Gameplay 工作包**（使用者裁決 2026-08-31），**不進 WP1**。理由：它同時改動 **Camera ＋ Character Facing ＋ Movement Basis ＋ Action Targeting** 四個系統——那是跨系統 gameplay 決策，不是相機功能。⚠️ 開包時**極可能需要 ADR**（strafe 會逼出 `MovementIntent` 的座標基底語意＝FU-6 的正解，且 lock-on target 很可能真的需要跨層 ⇒ 觸發 §5.2 的 trip-wire ②） |

### 12.3 是否需要新 ADR？——**結論：不需要**

| ADR 判準 | 本包 | 說明 |
| --- | --- | --- |
| ① 黑板 schema／ownership 變更 | ❌ | §5.2 論證 ＋ §9-T3 檢核 ＋ A5 零改動 |
| ② FSM 拓撲或 GameObject hierarchy 變更 | ❌ | 不新增 `StateType`、不改 Root／Model 兩層結構 |
| ③ 管線順序或核心驅動介面變更 | ❌ | §7.1；Runner 零改動、dev-spec §2.1 零改動 |
| ④ 推翻既有架構不變量 | ❌ | §9-T6；18 條全綠且條數不變 |

⇒ 依 CLAUDE.md routing rule，**直接寫本 Living Doc 分卷，不開 ADR、不進 Trial**。
**這個「不需要」本身就是本包的交付物之一**——若交付時發現任一格變成 ✅，代表架構命題失敗，**停下來提 ADR，不得偷偷加欄位**。

---

## 12. 貼身時的角色抑制（2026-09-15，缺陷修正）

**症狀**：2026-09-15 錄影 63s／71s／90s——與敵人貼身纏鬥時，畫面被角色背部整片填滿。

**這不是防穿牆失效。** `ResolveCollisionPosition` 正確地把相機拉近了；
問題出在拉近到 `minDistance = 0.6` 之後——角色膠囊半徑 0.32、網格的肩與臂更外凸
⇒ **相機落在角色網格內部**。沒有任何一層負責「拉到極限之後讓角色讓開」。

**修正**：`ThirdPersonCamera` 新增貼身抑制。`_currentDist` 低於 `hideTargetBelowDistance`
時把 target 底下的 `SkinnedMeshRenderer` 切成 `ShadowCastingMode.ShadowsOnly`。

| 欄位 | 預設 | 意義 |
|---|---|---|
| `hideTargetBelowDistance` | 0.95 | 低於此距離隱藏角色本體。**0 ⇒ 停用** |
| `hideDistanceHysteresis` | 0.15 | 解除隱藏所需的額外距離 |
| `suppressedRenderers` | 空 | 留空 ⇒ 從 `target` 底下解析（比照本專案「欄位留空 ⇒ 補洞」慣例） |

⚠️ **為什麼是 ShadowsOnly 而不是淡出**：淡出需要一份透明材質變體（資產＋shader 工作）；
`shadowCastingMode` 是零配置、可逆、不動任何資產的最小手段，**影子仍在**
⇒「角色還在那裡」的空間資訊不會丟失。若日後要做 dither fade，`SetSuppressedRenderersHidden` 就是替換點。

⚠️ **遲滯是必要的，不是優化**：門檻是一個距離，而相機距離每帧都在阻尼變化
⇒ 沒有遲滯就會在門檻附近逐帧顯示／隱藏，**角色閃爍比穿模更糟**。
純函數 `ResolveTargetHidden` 由 `CameraAimTests.T13`／`T14` 守住
（含「門檻 0 必須能退出已隱藏狀態」——否則關掉功能後角色會永遠消失）。

📌 **數值未經人眼驗收**：0.95／0.15 是依 `minDistance = 0.6` ＋ 膠囊半徑 0.32 推得的起點，
不是調過手感的值。若貼身時角色消失得太早或太晚，**調這兩個欄位即可，屬純呈現資料**。
