# 16 — Code Review Protocol（正本）

> **狀態**：🟢 **生效中**（2026-09-08 使用者裁決）。
> **適用範圍**：本專案**所有** code review，含 AI 自審、交接前檢查、以及使用者明確要求的 review。
> ⚠️ **除非使用者明確更改，後續所有會話一律遵守本檔**，⛔ 不得自創替代標準。
> 觸發指標寫在 `CLAUDE.md`（每會話自動載入）；本檔是內容正本。

---

## 0. 前提

- Review **只讀不改**：先出報告，**不要順手改 code**，除非使用者另外指示
- **以本 repo 既有的架構文件、ADR、不變量、測試、依賴規則為唯一真相**
- ⛔ **不得為了批評而發明新的架構原則**。若某件事**看起來奇怪但已被 ADR 或 repo 規則明文允許**，**不列為 finding**

## 1. 審查順序（依序，不得跳過）

### ① Correctness
邏輯錯誤／生命週期與狀態錯誤／邊界情況／Unity API 誤用／資源與事件清理／
**在實質相關時**的配置與效能回歸

### ② Contract violations
違反的 ADR 或不變量／被繞過的既有契約／**重複的真相來源**／
**下游重新推導上游早已決定好的資訊**

### ③ Dependency direction
反向依賴／跨層依賴／隱藏依賴／**繞過既定管線的新旁路**

### ④ Decision ownership
逐一列出**這次 diff 引入的每一個 gameplay 或架構決策**，指出**誰擁有它**，
並標記**沒有明確擁有者**或**有多個互相competing 擁有者**的決策

### ⑤ Scope
區分「ticket 要求的改動」與「順手做的重構」；
標記**沒有當前需求就先建立的抽象或基礎設施**

### ⑥ Tests
既有測試**是否真的證明了被改動的行為**／哪些重要行為**目前只靠人工測試守著**／
⛔ **不得為了提高覆蓋率而建議測試**

## 2. 每條 finding 的必要欄位

| 欄位 | 說明 |
|---|---|
| **Severity** | `BLOCKER` ／ `HIGH` ／ `MEDIUM` ／ `LOW` |
| **File ＋ symbol／line** | 要能直接跳到 |
| **Evidence** | 實際的程式碼事實，不是感覺 |
| **Why it is a problem *in this repository*** | 要指名是哪一條 ADR／不變量／既有慣例被打到 |
| **Smallest reasonable fix** | 最小修法，不是最理想重構 |

⛔ **不報純風格偏好**，除非它造成具體的維護、正確性或架構問題。
⛔ **不誇獎實作**，⛔ **不摘要沒有變動的程式碼**。

## 3. 結尾必須有三段

- **Must fix before merge**
- **Worth fixing later**
- **No issue ／ intentional deviations verified against existing architecture**

**某一類沒有 finding 時，要明講「這類沒有發現」**，⛔ 不得為了湊數而發明。

## 4. 本專案（IntentPipeline）的加重項

以下四類**優先於**其他 finding：

1. **方向性資料流**違規（Input → Pipeline → RuntimeData → StateMachine → Animation → Motion）
2. **單一真相來源**破損
3. **決策擁有權**不明或分裂
4. **下游重新推導已經被上游 commit 的狀態**

📌 為什麼是這四項：本專案的架構價值幾乎全部集中在「誰決定什麼、決定幾次」。
ADR-003（intent 單一寫入者）、ADR-004 D2（release 時點單一權威）、
ADR-005 D1（單一 identity 鍵）、ADR-007 D1／D3／D4／D5（方向的擁有權）
**全部是同一條紀律的不同切面**——這四項失守，等於這些 ADR 同時失效。

## 5. 與既有機制的關係

- **不取代**架構回歸測試（`ArchitectureRegressionTests`）——那是機器守的**已知**不變量；
  本協定守的是**尚未被機器化**的判斷
- **不取代** `docs/12-workflow.md` 的 Verification Ladder——那回答「該自動測還是人工測」；
  本協定回答「這份改動有沒有問題」
- Review 發現**值得長期守住**的不變量時，依 CLAUDE.md「Test-as-Spec」原則
  **優先表達成測試**，而不是只寫在報告裡
