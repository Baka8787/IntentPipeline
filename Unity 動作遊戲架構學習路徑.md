# Unity 動作遊戲架構學習路徑

## 學習目標

本學習計畫的目標並非快速完成一款遊戲，而是建立一套具備可維護性、可擴充性且適合展示於作品集的 Unity 動作遊戲架構。

重點包含：

* 建立清楚的資料流（Data Flow）
* 實作分層狀態機（Hierarchical State Machine）
* 邏輯與動畫解耦（Logic / Presentation Separation）
* 建立可擴充的角色控制架構
* 完成一個可展示的 Demo 專案

---

# 學習階段規劃

## 第一階段：地基建設 ✅

### 學習目標

建立完整資料流，先不要接動畫系統。

### 實作內容

* 建立專案架構
* 安裝 Unity New Input System
* 建立 `PlayerRuntimeData`（黑板）
* 建立 `InputData`（v0.3 已升版為 `ref struct`）
* 建立 `InputPipeline`
  * 採樣輸入
  * 將玩家意圖寫入黑板
* 建立 `ArbiterData`（仲裁旗標，預留第四階段接入）
* 使用 `Debug.Log` 或 `OnGUI` 顯示黑板內容
* 驗證資料流是否正確

### 本階段重點

理解：

> **玩家意圖（Intent）** 與 **角色狀態（State）** 必須分離。

不要急著製作動畫，先確認資料流設計沒有問題。

---

## 第二階段：分層狀態機（核心）✅

### 學習目標

完成 FullBody State Layer，並同步評估 Animancer Lite v8 的可行性。

### 實作內容

#### 狀態機本體
* 設計 `BaseState` 抽象類別
* 建立 `FullBodyStateMachine`（狀態機主體，負責切換邏輯）
* 建立 State Registry（狀態註冊表）
* 完成以下 FullBody 層狀態：
  * `IdleState`
  * `MoveState`
  * `JumpState`
  * `RollState`
* 接入 `CharacterPipelineRunner` 順序 4 預留位置

#### ScriptableObject 狀態規則配置
* 建立 `StateRuleSO`（進入條件、打斷規則配置）
* 建立狀態規則配置資產（每個狀態各一份）
* 完成狀態進入 / 打斷規則表（見 `02-dev-spec.md` 第 4 節）

#### Animancer Lite v8 評估（只讀，不實作）
* 閱讀 Animancer Lite 文件，確認以下項目：
  * `AnimancerComponent` 基本 API（`Play()`、Layer 管理、transition 回調）
  * Lite 版的功能限制（層數上限、商業授權限制等）
  * 確認 `AnimationFacadeBase` 的抽象介面能否直接包住 Animancer Lite
* 將評估結論記入 `01-design-doc.md` Trade-off 表

### 本階段亮點

ScriptableObject 配置 State Rule 是整套架構最值得展示的部分，也是作品集的重要特色。

> **注意**：本階段暫不接動畫，狀態切換只靠 `Debug.Log` 或 BlackboardDebugViewer 驗證正確性即可。

---

## 第三階段：表現層解耦 + Animancer Lite 接入

### 學習目標

讓遊戲邏輯完全不直接操作動畫，並接入 Animancer Lite 作為底層動畫系統。

### 實作內容

#### Animation Facade
* 建立 `AnimationFacadeBase`（抽象介面層）
* 實作 `AnimancerFacade`（以 Animancer Lite 作為底層，取代原規劃的 Unity Animator 版本）

#### MotionDriver 基礎版本
* 建立 `MotionDriver`
  * LateUpdate 同步動畫 Root Motion
  * 解決滑步問題

#### 動畫資料烘焙工具（Editor 端）
* 建立烘焙工具（Editor Script），將動畫 clip 的根運動逐幀積分，匯出為輕量資料檔
* 資料格式：逐幀累計位移陣列（`float[]` 或自訂 ScriptableObject）
* 先以跳躍落地動畫作為第一個烘焙目標進行驗證
> ⚠️ **架構師提示（嚴防過度工程）**：
> 在寫第一版 `RootMotionExtractor` 時，**絕對不要**花時間去寫 Pipeline 框架類別、Validator 介面或 Cache 系統。
> 請在腳本內直接用四個私有方法（Validate/Extract/PostProcess/Write）硬編碼跑通流程。
> 優先讓 Runtime 的 `MotionDriver` 拿到資料並完成吸附（Warping）除錯。此時的目標是快速通關，而非完美的工具鏈。

#### MotionDriver 接入烘焙資料
* 讀取烘焙資料，在 Runtime 計算「理論位移」與「實際目標位移」的差值
* 將補償速度傳入 MotionDriver 進行修正
* 驗證目標：跳躍落地時角色落點與目標點誤差在可接受範圍內

#### 上半身 Layer
* 空手
* 持槍

### 建議實作順序

1. `AnimationFacadeBase` 抽象介面（先定介面再寫實作）
2. `AnimancerFacade` 基礎版（只做 `Play()`，能讓 Idle/Move/Jump/Roll 各自播對應動畫）
3. `MotionDriver` 基礎版（LateUpdate 純根運動同步，先驗證不滑步）
4. 動畫烘焙 Editor 工具（先以跳躍落地動畫驗證）
5. `MotionDriver` 接入烘焙補償
6. 上半身 Layer（`SetLayerWeight`）

### 本階段重點

完成：

> **Gameplay Logic → AnimationFacadeBase → AnimancerFacade → Animancer Lite**

動畫烘焙資料的職責邊界：

> **Editor 工具提取資料 → MotionBakeData.asset → MotionDriver.SampleAt() → 補償位移**

### Animancer Lite 限制提醒

- Runtime Build 僅支援 Layer 0，上半身 Layer 在 Build 版本無效
- `AnimancerFacade.SetLayerWeight(index > 0)` 加入 `#if UNITY_EDITOR` 防禦
- 本階段以 Editor 驗證為主，Build 測試前須先決定是否升級 Pro
- 詳見 `02-dev-spec.md` 2.8 節升級路徑說明

---

## 第四階段：仲裁器與打斷系統

### 學習目標

加入較進階的架構設計。

### 實作內容

* 實作 `ArbiterPipeline`
  * 接入 `CharacterPipelineRunner` 順序 4.5 預留位置
  * 依狀態機目前狀態統一寫入 `RuntimeData.Arbitration` 仲裁旗標
* 建立具體仲裁器實作（實作 `IArbiter`）
  * 死亡仲裁器（封鎖輸入、IK、音頻）
  * LOD 仲裁器（視距離降頻）
* 建立 `InterruptProcessor`
  * 全域打斷
  * 上半身打斷
* 建立簡易 Action Arbiter
  * 管理請求優先權
  * 解決技能衝突

### 本階段亮點

這部分能充分展現：

* 系統設計能力
* 架構思維
* 可擴充性

也是整個專案最有特色的地方之一。

---

## 第五階段：裝備系統、物件池與 MotionDriver 進階

### 學習目標

補足專案完整度，並實作需要狀態機配合的進階位移修正。

### 實作內容

#### 裝備系統
* `ItemDefinition`
* `EquipmentDriver`
* 至少兩種武器，展示不同武器邏輯

#### 物件池
* `SimpleObjectPoolSystem`
* 應用於：投射物、特效、可重複生成物件

#### MotionDriver 進階：分段積分位移修正
* 實作攀爬等需要分段位移補償的動作
* 利用第三階段烘焙好的分段位移資料，與 Runtime 實際目標位置比較
* 積分補償速度，實現局部目標修正（例如攀爬分為「起跳抓邊」與「拉身上去」兩個積分區間）
* 前提：攀爬狀態需在第二階段狀態機中先行建立

#### Animation Build Pipeline 工業級重構（專案壓軸大招）
* 將第三、五階段累積的烘焙腳本徹底解耦，重構為可插拔的 `Animation Build Pipeline`
* 實作 **Source Discovery**：支援選取物右鍵烘焙（Selection）與 ScriptableObject 配置掃描
* 實作 **Validation**：前置檢查 Avatar Missing 與 Humanoid 設定，防止無效採樣
* 實作 **Build Cache**：利用 GUID 與檔案雜湊（Hash）建立快取系統，實現**增量編譯**，將重烘焙時間從數十秒壓低至 1 秒內
* 實作 **Build Report Window**：製作專屬編輯器視窗，優雅展示編譯成果與時間耗時，極大化提升作品集賣相

---

## 第六階段：作品集打磨

### 學習目標

將專案整理成一份完整的作品集。

### 實作內容

#### README

撰寫：

* 專案介紹
* 系統架構
* 流程圖（Mermaid 或其他流程圖）
* 各模組設計理念

#### Demo 影片

錄製約 3–5 分鐘影片，介紹：

* 專案架構
* 核心設計理念
* 系統運作流程
* 解決了哪些問題
* 為什麼採用目前的架構

#### GitHub 維護

保持良好的 Commit 歷史：

* 每完成一個功能就 Commit
* Commit 訊息具描述性
* 避免一次提交大量內容

---

# 新手實作建議

## 1. 先求完成，再求漂亮

第一版可以接受程式碼不夠優雅。

重點是：

* 功能先完成
* 資料流跑通
* 再逐步重構

---

## 2. 直接使用 Animancer Lite，不繞路 Unity Animator

第二階段完成 Animancer Lite 評估後，第三階段直接以 `AnimancerFacade` 包裝 Animancer Lite。

未來若升級至 Animancer Pro，只需修改 Facade 內部即可，切換成本極低。

---

## 3. 不要急著加入額外插件

例如：

* Final IK
* Cinemachine

這些屬於加分功能，建議核心架構完成後再視需求加入。

---

## 4. 建立小型測試場景

每完成一個子系統，就建立一個獨立測試場景，例如：

* Input Test
* State Machine Test
* Animation Test（含 Animancer Lite 驗證）
* Motion Bake Test（烘焙資料驗證）
* Equipment Test
* Object Pool Test

讓每個系統都能獨立驗證，降低整合時的除錯成本。

---

# 最終成果

完成本學習路徑後，應具備以下成果：

* Input Pipeline（ref struct 零 GC 設計）
* Runtime Blackboard（意圖 / 參數 / 仲裁三區分離）
* 分層狀態機（Hierarchical State Machine）
* ScriptableObject 規則配置
* Logic / Animation 解耦
* AnimationFacade（Animancer Lite 接入）
* MotionDriver（基礎根運動同步 + 烘焙資料補償）
* 動畫資料烘焙工具（Editor Script）
* Interrupt System
* ArbiterPipeline（仲裁管線）
* Action Arbiter
* Equipment System
* Object Pool
* 完整 README
* Demo 展示影片
* 清晰的 GitHub Commit 歷史

最終完成的不只是角色控制器，而是一套具備良好擴充性與維護性的 Unity 動作遊戲架構，可作為求職作品集的重要展示專案。