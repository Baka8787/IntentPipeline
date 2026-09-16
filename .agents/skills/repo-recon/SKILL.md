---
name: repo-recon
description: 在這個 repo 裡用最低上下文找到答案——先查地圖與測試、再 grep、最後才讀檔；以及「先量再改」的證據紀律。開始任何調查、要找某個行為在哪裡實作、或準備下結論之前使用。
---

# 在這個 repo 裡查東西

專案 ~10k 行（docs 4k／code 6k）且註解密度高。**讀整檔是最大的上下文成本**——
實測一個 feature 大小的任務讀掉全專案 23%，放大倍率 5–40 倍。順序錯了就會付這個代價。

## 查找順序（照這個順序，不要跳）

1. **`WORKLOG.md` 最上方的「🔖 交辦」** —— 現在的狀態、上一輪做了什麼、已知未解。通常只需要這段。
2. **`docs/00-map.md`** —— 單頁索引：模組 → 檔案 → 治理章節。**它就是為了取代「全檔掃描」而存在的。**
3. **架構不變量看測試，不要看程式**：
   `Assets/_Project/Tests/EditMode/ArchitectureRegressionTests.cs`
   - 想知道「誰可以寫這個黑板欄位」⇒ 讀 `WriterRules`（~15 行），不要讀
     `CharacterPipelineRunner` ＋ `MotionDriver` ＋ `PlayerRuntimeData`（~600 行）
   - 想知道「哪一層不能依賴哪一層」⇒ 讀 `LayerRules`
   - **測試不會漂移，因為漂移會讓它變紅。** 它是全專案最便宜且最準的架構摘要。
4. **大文件先定位再讀**：`grep -n "^#"` 拿章節地圖，再 `Read` 帶 `offset`／`limit`。
   ⛔ 不要端到端讀 `docs/02-dev-spec.md`／`docs/01-design-doc.md`。
5. **程式優先 `Grep` 符號，不要 `Read` 含它的檔案**；真的要讀就讀那一段，不讀整檔。
6. ⛔ **已經在本次對話裡摘要過的檔案不要再讀一次。**

## repo 裡常常已經有答案

> 「`TraversalIntegrationWiringTests` 第 20 行早就寫著
> `PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab"`——repo 裡已經有答案，我沒去看。」

下結論之前先問：**這件事有沒有可能已經被某條測試、某個常數、或 `docs/00-map.md` 寫死了？**
測試裡的常數是事實來源，不是測試細節。

## 各文件的角色

| 檔 | 是什麼 | 什麼時候開 |
|---|---|---|
| `WORKLOG.md` 交辦段 | 現在手上的工作 | 每次開場 |
| `docs/00-map.md` | 單頁索引 | 第二個開 |
| `docs/ADR/` | **不可變**決策紀錄（`Accepted` 之後凍結） | 動到該決策範圍時 |
| `docs/01` / `docs/02` | Living：當前架構／跨領域契約 | 對照 API 與職責邊界 |
| `docs/NN-<子系統>.md` | 子系統分卷（Dev Spec 層） | 只在做那個子系統時 |
| `docs/12-workflow.md` | 工作流正本、**Verification Ladder** | 判斷「該自動測還是人工測」時 |
| `docs/16-review-protocol.md` | Code review 正本 | **任何 review 之前必讀** |
| `docs/changelog.md` | 歷史。**不是現況** | 查沿革／過去理由 |

**Explore subagent 已獲授權**用於大範圍扇出搜尋（「X 在哪」「哪些檔案碰到 Y」）——
它燒自己的上下文只回結論。⛔ 不要用它做已知位置的精準讀取（直接 `Read` 更便宜），
也不要用它做判斷（它只負責定位）。

## 證據紀律：先量再改

這個 repo 反覆出現同一類 bug：**兩處用了不同基準**
（進場距離 vs 接觸解算、authored 值 vs 建構子預設、量在 clip 結尾 vs 量在 marker）。
判準很簡單：**一個量要在哪個時刻被消費，就得在那個時刻量。**

因此：

- **不要憑推論改程式。** 先用 `unity-live-control` 的離線手法在**真實場景**上量出數字，
  把「修正前 / 修正後」並排寫出來。
- **拿自己編的測試資料當證據會出事。** 隨手填一個 `entryCapsuleWallClearance`
  曾讓量出來的殘差從 1.7 cm 變成 19 cm，差點誤報成程式 bug。
- **修症狀的字面，不等於修症狀所屬的問題。** 使用者說「Game 視窗一堆字」，
  只關掉 Game 視窗的 TextMesh ⇒ 字全部堆到 Scene View。
- **別人（含 subagent／Codex）的回報要驗。** 重跑一次測試、或讀一次資產實際值，比採信便宜得多。

## 改動的價值由「看不看得到」決定

> 使用者原話：「你改了什麼**我沒體驗到**。」

數字變好但玩家感受不到，就不算交付。動手前先問：**這個改動會讓畫面上哪一件事不一樣？**
答不出來就先去找那件會不一樣的事。

## 新增不變量時

**優先寫成測試，不要寫成散文。** 同一個成本換到「強制執行」＋「最便宜的摘要」兩件事。
`ArchitectureRegressionTests` 的 `A` 系列與 `TraversalBakedAssetTests` 的 `B` 系列都是範例：
後者直接載入**出貨資產**做斷言，專門防「測試綠但出貨資料完全走不到」。
