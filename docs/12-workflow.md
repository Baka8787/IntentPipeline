# 12 — 開發工作流與驗證階梯（Feature Slice ／ Verification Ladder）

> **這份文件解決的問題**：工作單位被切成 component 大小，導致
> 「implement 一小塊 → 停 → 使用者進 Unity 接線 → Play → 回來再 implement 下一小塊」的碎片化節奏。
> 每一次交回都要付一次 context switch 與一次 Editor 開機成本，而那個成本**不隨熟練度下降**。
>
> **裁決（2026-09-04，使用者明確要求）**：工作單位改為 **Feature Slice**，
> 整合點改為 **Feature completion → Integration checkpoint**，
> 取代舊的 **Component completion → Integration checkpoint**。
>
> **狀態**：🟢 生效中。本檔是 Living Doc（Dev Spec 層），與 `CLAUDE.md` ／ `AGENTS.md` 的
> 「Preferred Workflow」段互為正本與摘要——**兩份 MD 只放摘要與紅線，細節一律以本檔為準**。
>
> ⚠️ **本檔不放寬任何既有紅線**：Git lifecycle 仍全由使用者控制、AI 仍不得修改
> `.prefab`／`.asset`／`.meta`／場景／動畫資產、architecture-first 與 ownership 紀律不變。
> 改變的只有「AI 什麼時候應該停下來把工作交回人類」。

---

## 1. Feature Slice：工作單位的定義

**Feature Slice ＝ 一個玩家（或設計者）看得懂的完整行為**，不是一個 class、一個 MonoBehaviour、一個欄位。

| 這是一個 Feature Slice | 這不是 |
| --- | --- |
| 「近戰揮擊會打到敵人並讓敵人播受擊」 | 「新增 `MeleeHitboxSink`」 |
| 「暫停時角色不會被輸入影響，解除後行為正確」 | 「`GamePauseController` 實作 `IArbiterSource`」 |
| 「敵人會保持在近戰距離帶內」 | 「`AIMovementSource` 加三個距離欄位」 |

### 1.1 明確**不**構成停止條件的事

以下任一項發生時，**不得**因此中斷 Feature Slice 把工作交回使用者：

- 新增了一個 component ／ MonoBehaviour
- 新增了一個 `[SerializeField]` 欄位
- 這個東西**最終**會需要 prefab reference ／ Inspector 拖拉
- 這個東西**最終**需要 Play 才能看到效果
- 需要新的 ScriptableObject 資產才能真正跑起來

**理由**：這些都是「稍後會需要人工整合」的訊號，不是「現在無法繼續」的訊號。
只要仍有**不依賴 Unity Editor 人工操作**的工作可以合理繼續，就繼續做完。

### 1.2 什麼時候才真的該停

只有這三種：

1. **撞到 Integration Gate**（§3）——這個 Feature 能自動驗證的部分都做完了。
2. **需要架構裁決**——出現 `CLAUDE.md` ADR 判準①～④其中之一，或會改動 ownership／pipeline contract。
3. **需要 Integration Spike**（§5）——核心假設高度依賴 Unity 執行期行為，且延後驗證會造成大量返工。

---

## 2. 工作流（11 步）

```
1  Define Feature Slice          說清楚這一刀切的是哪個玩家可理解的行為
2  Identify Integration Risks    先想清楚哪些部分最終一定需要人工接線／Play
3  Read relevant docs/code       依 CLAUDE.md Context Discipline，locate first, read second
4  Resolve architecture Qs       只在真的需要時；不需要就不要開會
5  Implement Runtime + Tests     Runtime／Data ＋ 對應的 automated verification 一起寫
6  Iterate to closed loop        反覆到這個 feature 的核心行為可以被自動驗證
7  Reach Integration Gate        §3
8  Batch human Editor work       §4：一次列出整份 integration checklist
9  Play / Experience Validation   使用者一次驗收完整體驗
10 Fold back Living Docs         dev-spec／子系統分卷／WORKLOG 同步到實際狀態
11 Regression / Done             測試全綠、checklist 清空
```

**步驟 5–6 是這次改版的重點**：以前這裡是「implement 一塊 → 交回」，
現在是「implement ＋ 自動驗證 → 迭代到 closed loop → 才交回」。

---

## 3. Integration Boundary（整合邊界）

### 3.1 判準

在把工作交回使用者之前，先問：

> **「還有沒有任何不需要 Unity Editor 人工操作、就能推進這個 Feature 的工作？」**

有 ⇒ 繼續做。沒有 ⇒ 到達 Integration Gate。

### 3.2 允許用來提前驗證的手段

為了推進到 Integration Gate，**允許且鼓勵**使用：

- test fixture ／ fake ／ stub ／ 測試替身
- 在測試中以程式建構 `GameObject` ＋ `AddComponent`（**不是**修改 prefab）
- EditMode test
- PlayMode test
- `SerializedObject` ／ `AssetDatabase` **唯讀**檢查
- `InputTestFixture`（合成輸入）

**先驗證核心行為，再處理接線。** 接線是最後一哩，不是前置條件。

### 3.3 一個具體對照

❌ **舊節奏**
```
新增 MeleeHitboxSink → 「請接線測試」→ 新增距離帶 → 「請調欄位測試」→ 新增 sink 路由 → 「請 Play」
```

✅ **新節奏**
```
新增 MeleeHitboxSink ＋ 其 lifecycle 測試
  → 新增距離帶 ＋ 其 hysteresis 測試
  → 新增 sink 路由 ＋ per-slot 路由測試
  → 到達 Gate
  → 一次列出：要建的 collider、要填的 binding、要調的欄位、要跑的 Play 項
  → 使用者一次做完、一次驗收
```

---

## 4. Batch Integration（整合批次化）

到達 Integration Gate 時，**一次**產出一份 checklist，內容分三段：

| 段 | 內容 |
| --- | --- |
| **A. 資產／接線** | Prefab、Inspector 欄位、ScriptableObject、Scene 物件、Collider、Animation、VFX、HUD |
| **B. 自動驗證** | 要跑哪些 test assembly（EditMode／PlayMode），預期全綠 |
| **C. 人工驗收** | 只列真的需要人眼／人手的項目（§6），每項寫清楚「看什麼、什麼算過」 |

**紅線**：不得出現「新增 A → 叫使用者接 → 新增 B → 再叫使用者接」。
同一個 Feature 的人工整合工作，能合併就合併到同一個 checkpoint。

---

## 5. Integration Spike Exception（提前整合探針）

不要走到另一個極端——有些東西延後驗證的返工成本極高。

**允許提前做最小 Integration Spike 的條件**：核心假設高度依賴以下任一項，
且假設若錯誤會導致大量返工：

- animation timing ／ 動畫事件時序
- Unity lifecycle 的實際順序
- physics ／ collision 行為
- camera 行為
- 第三方套件（Animancer／Input System）的執行期行為
- 實際操作手感

**但必須明確寫出這個 spike 要回答的問題**，例如：
> 「Animancer 的 `Play` 在同一帧內連續呼叫兩次，第二次會不會吃掉第一次的 event？」

⛔ **不合格的 spike 理由**：
> 「新增了一個 component，所以請使用者接線測試。」

那不是 spike，那是把 Integration Gate 誤當成 stop condition。

---

## 6. Verification Ladder（驗證階梯）

**這是往後新增 Feature 時選擇驗證方法的基準。**
在把任何項目歸類為「人工」之前，先由上往下走一遍這張表。

| 層 | 驗證對象 | 手段 | 實作位置 |
| --- | --- | --- | --- |
| **L0** | 架構不變量（依賴方向、單一寫入者、命名空間邊界、禁止的型別） | 原始碼／asmdef 靜態掃描 | `EditMode/ArchitectureRegressionTests.cs` |
| **L1** | Prefab ／ Inspector 接線契約 | `AssetDatabase` ＋ `SerializedObject` **唯讀**檢查 | `EditMode/PrefabWiringTests.cs` |
| **L2** | 資料關係／校準（SO 之間的數值契約） | EditMode 讀取資產並斷言關係 | EditMode（視個案） |
| **L3** | 純邏輯行為（無 Unity 生命週期依賴） | EditMode 單元測試 ＋ fake／stub | EditMode 各子系統測試 |
| **L4** | 帧順序／輸入邊沿／執行期生命週期／暫停恢復 | PlayMode 測試（必要時 `InputTestFixture`） | `PlayMode/` |
| **L5** | 效能回歸**警報** | PlayMode ＋ `ProfilerRecorder` 之類的探針 | 見 §7 的保留條款 |
| **L6** | 體驗品質 | **人工 Play** | dev-spec §7.2 |

### 6.1 「AI 不能操作 Unity Editor」≠「只能人工測」

這是舊流程最大的邏輯跳躍。正確的推論是：

- AI 不能**接線** ⇒ 接線由使用者做。
- 但「接線對不對」**可以**由 L1 自動驗證。
- AI 不能**按 Play 用眼睛看** ⇒ 體驗品質由使用者驗。
- 但「一帧延遲成不成立」「暫停後 timeScale 有沒有還原」**可以**由 L4 自動驗證。

### 6.2 人工驗收應該保留給什麼

只有這些：

- animation feel（動畫手感、過渡自然度）
- camera feel（取景、跟隨、防穿牆的觀感）
- visual quality（IK 貼合、穿模、VFX）
- gameplay readability（看不看得懂發生了什麼）
- control feel（操作跟手程度）
- **需要正式 Player build ／ Profiler 才能成立的性能結論**
- 其他確實無法以 deterministic automated test 表達的體驗問題

### 6.3 降級規則

如果某項在階梯上**應該**可以自動化，但實際嘗試後發現：

- 需要依賴第三方 private／internal 序列化
- 在 Test Runner 下噪音過大、會偽陽性
- 自動化成本顯著高於它擋下的返工成本

⇒ **降級為人工，但必須寫下降級的理由**。
「還沒試」不是理由；「試過，原因是 X」才是。

---

## 7. 效能驗證的保留條款（重要）

**automated regression alarm ≠ 正式 zero-GC 結論。**

| | 用途 | 效力 |
| --- | --- | --- |
| **PlayMode ＋ ProfilerRecorder 探針** | 便宜的**回歸警報**——熱路徑突然開始配置時會叫 | ⚠️ **不足以宣告** zero-GC 達標 |
| **dev-spec §7.4 正式 SOP**（Development Build ＋ Profiler ＋ `PlayerLoop` 子樹） | 正式效能結論 | ✅ 唯一可用來宣告達標的依據 |

⛔ **不得**因為某次 PlayMode 測試量到 0 allocation，就宣稱正式 Player runtime 已完成 zero-GC 驗證。
Editor 下的數字含 Editor 開銷、且不等同 Player（§7.4.2 已列四項排除條件）。

📌 若探針在目前 Unity Test Runner 環境下噪音過大，**先不要納入 hard gate**——
噪音會訓練出「紅了就無視」的習慣，那比沒有警報更糟。

---

## 8. 與既有規則的關係

| 既有規則 | 本檔的影響 |
| --- | --- |
| `CLAUDE.md` **Stop After Edit** | **語意收斂**：它管的是 **Git 與編譯驗證的擁有權**，不是「每改一個檔就停」。詳見 `CLAUDE.md` 該段的修訂 |
| `CLAUDE.md` **Preferred Workflow** | 被本檔 §2 取代；兩份 MD 保留摘要 ＋ 指標 |
| Discuss-before-change | 維持 2026-08-29 的例外（trivial／local／test-only 不需要開會），並補上：**同一 Feature Slice 內的後續實作不需要重新開會** |
| Git Policy | **完全不變**。AI 仍不得執行任何 Git mutation（遠端容器純文件例外照舊） |
| 不碰 Unity 資產 | **完全不變**。測試只能**讀**資產，不得寫 |
| ADR 判準 | **完全不變**。本檔是流程文件，不是架構決策，因此不開 ADR |
| Trial／Spike code-first | 不變；本檔的 §5 與它相容（spike 用完即丟、不進 production path） |

---

## 9. 修訂紀錄

| 日期 | 內容 |
| --- | --- |
| 2026-09-04 | 建檔。Feature Slice 工作單位、Integration Boundary、Verification Ladder、效能保留條款。同步修訂 `CLAUDE.md`／`AGENTS.md` 的 Preferred Workflow 與 Stop After Edit 語意 |
