# ADR-006：上半身動畫層（移動中出手）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🔵 **Proposed**（2026-09-06）——**尚未裁決，不得作為實作基線** |
| 日期 | 2026-09-06 |
| 提出者 | 自主批次（使用者休息中）。**需要使用者裁決才能翻 Trial** |
| 🔴 前置阻擋（2026-09-06 更新） | **ADR-007（Direction Authority）已於 2026-09-06 翻 `Trial` 並佔用 Trial 名額**（ADR-004 §0：同一時間只允許一個）⇒ **本 ADR 維持 `Proposed`，須等 ADR-007 `Accepted` 後才能翻 Trial**（使用者裁決）。順序理由：`docs/13` §7——上身層若先做，會與「橫移方向錯誤」混在一起無法歸因。<br>以下為 2026-09-06 之前的阻擋紀錄（`AnimationFacadeBase` 那條已隨 ADR-005 `Accepted` 解除）：**ADR-005 仍是 `Trial`。** `AnimationFacadeBase` 契約列在 `docs/11` §10.2 的 ⛔ 不得改動名單（Acceptance **G** 的前提）。且 ADR-004 §0 的「同一時間只允許一個 Trial」在 ADR-005 `Accepted` 前仍然生效 ⇒ **本 ADR 最快也只能等 ADR-005 結案後翻 Trial** |
| 關聯文件 | `docs/06-animation-presentation.md`（Facade 現況）、`docs/03-animation-roadmap.md`（輪 6 Combat 已預告上身層）、`docs/11-multi-action.md` §10.2 |

---

## 1. 問題

**移動中出手在機制上已經可以了，在畫面上不行。**

`ActionState.OnUpdateMotion` 沒有 Bake 時會走 `MotionDriver.ExecuteBaseMovement`，而
`LocomotionModel.Tick` 每幀無條件執行 ⇒ **施法途中按 WASD，角色真的會移動**。

但 `AnimancerFacade.Play(key)` 播的是**全身**動畫。所以畫面上是
「站定施法姿勢 ＋ 整個人平移」——讀起來是穿模滑行，不是「邊跑邊施法」。

📌 這不是 bug，是**缺一層**。ADR-004／005 兩輪都刻意把上身層排除在外
（`docs/08` §11.1、`docs/11` §1.3 都明列），現在它成為作品端第一個被看見的缺口。

### 1.1 一個已經存在的死欄位

`PlayerRuntimeData.UpperBodyWeight` **有寫入者、沒有任何消費者**——
`LocomotionModel` 每幀依速度寫 0 或 0.5，而全專案只有 Editor 除錯面板讀它。

它是上一輪為這件事預留的欄位。本 ADR 必須決定它的去留：**給它消費者，或刪掉它。**
留著一個沒人讀的黑板欄位，違反 dev-spec §1 的 ownership 紀律。

---

## 2. 候選方案

| 方案 | 做法 | 代價 |
|---|---|---|
| **A. 維持現狀** | 出手時全身動畫、角色照樣能移動 | 零成本，但滑行的觀感問題不會消失。**作品集影片裡看得出來** |
| **B. 出手時鎖住移動** | `ActionState` 期間把 movement intent 歸零 | 零新機制（`ArbiterData.BlockInput` 已有先例）。但**與「快速施展」的設計意圖矛盾**，且法術連段會變成三段定身 |
| **C. Animancer 分層 ＋ AvatarMask** | 下半身跑 locomotion、上半身播 Action | **本 ADR 主張的方向**。Animancer 原生支援 `Layers`；成本在 Facade 契約與 AvatarMask 資產 |
| **D. Additive 疊加** | 把出手動作做成 additive 疊在 locomotion 上 | 對「揮劍」這種大幅度動作品質不佳；且素材不是為 additive 製作的 |

⇒ **主張 C。** B 是可以先做的止血（成本近零），但它解的是不同的問題——
若使用者要的是「邊跑邊丟火球」，B 直接否定了需求。

---

## 3. Decision（草案，**待裁決**）

### D1 — 分層是 **Facade 的職責**，State 不得認識層

`ActionState` 繼續呼叫「播這個 key」，**不知道也不指定層**。
層與遮罩由 `AnimancerFacade` 依 authored 資料決定。

**理由**：CLAUDE.md 的依賴方向禁止 `Animation → StateMachine`，而讓 State 指定層等於
把 Presentation 的分層策略倒灌進 FSM。同一個 Action 在不同角色上可能分層不同
（玩家分層、敵人全身），那是**資產差異**，不該是狀態機的分支。

### D2 — 層與遮罩是 **authored data**，不是程式常數

`AnimancerFacade.TransitionMapping` 擴充為帶 `LayerIndex` ＋ `AvatarMask`（或層設定的引用）。
沒填 ⇒ 維持現有的全身行為。

**理由**：與 §1.1 的死欄位同源——「哪些動作分層」是設計決定，會隨素材與手感反覆改。
寫進程式就會變成一張 switch。**加一個分層動作＝加一列映射，零程式**，
這與 ADR-005 的「加一個 Action ＝零 runtime 程式」是同一條紀律。

---

## 4. 明確**不**由本 ADR 管的事

| 事項 | 去處 |
|---|---|
| 哪些 Action 要分層、遮罩切在哪根骨頭 | 資產／`docs/06` |
| 上半身層的權重曲線與淡入淡出時間 | `TransitionAsset`／`docs/06` |
| `UpperBodyWeight` 的**數值來源**（速度？狀態？固定值？） | Living Docs。⚠️ 但**它的去留**由本 ADR 決定（見 §5-C） |
| 敵人要不要分層 | 資產 |

---

## 5. Acceptance Criteria（`Trial → Accepted` 的條件，草案）

- [ ] **A. 移動中出手**：邊跑邊放火球，下半身是跑步、上半身是施法，**無滑行感**
- [ ] **B. 加一個分層動作 ＝ 零 runtime 程式**（一列映射填上層與遮罩即可）
- [ ] **C. `UpperBodyWeight` 有明確歸屬**：要嘛被本層消費，要嘛從黑板刪除。**不得繼續當死欄位**
- [ ] **D. 既有全身動作零回歸**：Melee／Roll／Jump／受擊仍是全身，且不需要改任何一列既有映射
- [ ] **E. EditMode ＋ PlayMode 全綠**，含 `AnimationFacadeBase` 契約相關的架構測試
- [ ] **F. 零 GC**：分層不得在每幀播放路徑上引入配置

**未通過** ⇒ 修 ADR ／ Living Spec → 再驗證，**不得補 workaround**。
**Revert 成本**：中。契約擴充是加法（沒填 ＝ 舊行為），但 Facade 內部的層管理需要一併回退。

---

## 6. 🔴 為什麼現在不能開工

1. **`AnimationFacadeBase` 契約在 `docs/11` §10.2 的 ⛔ 名單上。** 那份名單是 ADR-005
   Acceptance **G**（「Slow 跨系統傳播，五個下游檔案零修改」）的前提——現在動它，
   G 的證據鏈就斷了。
2. **ADR-004 §0：同一時間只允許一個 Trial。** ADR-005 尚未 `Accepted`。
3. ADR-005 的 **A／B／C／E 都還沒驗**（需要 Play ＋ Profiler，只有使用者做得到）。

⇒ **先關掉 ADR-005，再談本 ADR。** 這個順序不是流程潔癖：
現在動 Facade，之後若 ADR-005 的 Play 驗收出問題，將無法分辨是哪一邊造成的。

---

## 7. 修訂紀錄

| 日期 | 修改 | 原因 |
|---|---|---|
| 2026-09-06 | 建立草案（`Proposed`） | 使用者列出「施法＋移動」為想做的項目；調查後確認它需要動核心驅動介面 ⇒ 依 CLAUDE.md 判準③必須開 ADR，且必須等 ADR-005 結案 |
