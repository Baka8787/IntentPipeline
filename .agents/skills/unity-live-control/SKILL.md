---
name: unity-live-control
description: 驅動使用者開著的那個 Unity Editor——跑測試（一律經 safe_unity_test.sh）、重編譯、讀寫資產、在真實場景上量測，全部不需要進 Play Mode。動到 Unity 資產、想跑測試、或要驗證「遊戲裡實際會發生什麼」時使用。內含五個踩過的坑與不進 Play 也能重建整條 gameplay 鏈路的手法。
---

# 驅動 live Unity Editor

**Editor 開著 ≠ 只能讀磁碟。** 本專案裝了 `com.unity.pipeline` ＋ `unity` CLI
（`~/AppData/Local/Unity/bin/unity`），可以直接對**執行中的 Editor** 下命令。

⚠️ **把驗證交回人工之前，先跑 `unity status`。** 2026-09-13 我因為 `unity test` 撞 project lock
就回報「測試需由使用者執行」——錯的是結論不是觀察。

## 基本入口

```bash
unity status                       # 連線中的 Editor：port / 專案 / 版本 / PID
unity list                         # 該 Editor 註冊的上百個命令
unity cmd <command> [--args]       # 在 live Editor 執行
unity job status <id> --json       # --detach 提交後取結果
```

常用：`recompile` / `recompile_status` / `console` / `test_status` / `eval_file`。（**測試不要直接 `run_tests`**——見下方「測試」：一律 `safe_unity_test.sh`）

## 測試（🔒 一律走安全入口 —— 2026-10-11 使用者裁決，正本 `docs/53` §17）

**EditMode／PlayMode 回歸與 Probe 測試，預設全部用 `safe_unity_test.sh`。** 直接 `unity cmd run_tests` 不是一般路徑。

```bash
E=docs/artifacts/editor-memory/safe_unity_test.sh
bash $E --mode PlayMode --check-only                       # 先確認全部安全檢查通過（不提交測試）
bash $E --mode EditMode                                    # EditMode 回歸
bash $E --mode PlayMode                                    # PlayMode 回歸
bash $E --mode PlayMode --filter <測試名稱片段>             # 單一類別／方法
bash $E --mode PlayMode --filter <Probe 方法> --include-explicit --label <名稱>   # [Explicit] Probe
```

| exit | 意義 | 你要做的事 |
|---|---|---|
| 0 | PASS／CHECK_OK | — |
| 1 | TESTS_FAILED（跑完、有失敗；名稱列在輸出與 `summary.txt`） | 照一般失敗處理，對照基線 |
| 3 | NO_TESTS_MATCHED（filter 沒選到；`[Explicit]` 需要 `--include-explicit`） | 檢查 filter |
| 5 | NOTHING_EXECUTED（選到的測試全部 skipped／ignored——什麼都沒驗證到） | 不要當成通過 |
| 2 | 參數／設定錯誤（沒有執行任何東西） | 修參數 |
| 4 | 結果缺失或格式錯誤 | **停**，回報 |
| 10–27 | SAFETY_STOP（原因在 `reason:`：Editor 無回應、編譯未乾淨（14）、場景未存、洪水、取樣器異常、逾時（18）、上一輪仍在跑（25）、recompile 逾時（26）、鎖被占用或殘留（27）…） | **停**，診斷並回報 |
| 130 | ABORTED（入口被中斷；迴圈與取樣器已停，不會再提交） | — |
| 30／31／32 | 取樣器啟動失敗／執行中告警／Editor 不是本專案或 CLI 無回應 | **停**，診斷並回報 |

每次執行的紀錄在 `Logs/safe-test/<UTC>-<label>.XXXX/`（`summary.txt`、`loop.txt`、`watch.csv`、run_tests／test_status JSON）。
⏱️ 從 Bash 工具跑 PlayMode 回歸（約 2–3 分鐘＋入口開銷）時用 `run_in_background`，或把 timeout 設 ≥ 600000 ms——工具逾時會中斷入口（安全：迴圈會被停掉，但這次 run 白跑）。

⛔ **入口失效時不得自行繞過防護直接 `unity cmd run_tests`。** 先診斷、回報；需要例外執行時交由使用者批准。
⛔ 也不要為了「讓入口通過」去存使用者的場景、關掉取樣器或調高門檻——停下來問。

**入口已經替你處理的坑**（仍要知道為什麼）：

1. ⛔ `unity test` 會另生 batchmode Editor、撞 project lock——入口只用 `unity cmd`。
2. PlayMode 必須 async（同步會被 domain reload 丟掉）——入口一律 async，EditMode 亦同。
3. ⛔ 編譯中排測試會鎖死主執行緒；改了 `.cs` 沒重編就跑會測到**舊組件**——入口實跑前先 `recompile` 並等到嚴格乾淨的 completed／up_to_date（且主執行緒回應），之後仍要求 `recompile_status` 乾淨**且**主執行緒回報 `compileFailed=False`、`isCompiling=False`。（`--check-only` 不觸發 recompile。）
4. ⛔ Play Mode 中、或場景 dirty（test runner 會跳強制存檔對話框、無限期卡住）時不排測試——入口在提交前擋下（`sceneDirty=True` 時請使用者存檔，⛔ 不要自己存）。
5. `test_status` 在 run 開始前回 `no_tests`、domain reload 期間可能暫時失敗——入口有界等待，且只接受「看過 running」或「狀態檔在提交後寫入」的 completed。

整套 EditMode 約 25 秒；PlayMode 約 2–2.5 分鐘。

## `eval_file` 的腳本形狀

傳進去的檔案是**方法體**，不是完整 C# 檔：

- ⛔ 不能寫 `using` 指示詞（會被當成 using-statement ⇒ `Identifier expected`）
- 用完整命名空間：`UnityEngine.Object.FindAnyObjectByType<T>()`
  （⚠️ `FindFirstObjectByType` 與帶 `FindObjectsSortMode` 的多載**已 deprecated，警告視為錯誤**）
- 最後 `return <string>`；輸出帶 `\r\n` 逸出，用 `sed 's/\\r\\n/\n/g'` 還原
- **用 Write 工具寫腳本檔，不要用 bash heredoc**——引號跳脫很容易炸
- 解析 JSON 用 PowerShell 的 `ConvertFrom-Json`（這台機器 bash 管線接 `python` 會回 exit 49）

## 不進 Play Mode 重建整條 gameplay 鏈路

這是本專案最有價值的驗證手法：**在真實場景幾何上跑真實元件**，但不進 Play。

```csharp
// 1. 臨時 rig：HideAndDontSave ⇒ 不會寫進使用者的場景
var go = new UnityEngine.GameObject("__probe_rig");
go.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
var cc = go.AddComponent<UnityEngine.CharacterController>();
// …從場景上的真實角色複製 center/height/radius/skinWidth…

// 2. 私有 serialized 設定用反射複製，別自己編數字
var f = typeof(TheComponent).GetField("settings",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
f.SetValue(myComponent, f.GetValue(sceneComponent));

// 3. EditMode 不會呼叫 Awake ⇒ sibling 快取是 null，手動補
typeof(TheComponent).GetMethod("Awake", NonPublic|Instance).Invoke(myComponent, null);

// 4. 這時就能在真實場景上呼叫它的公開 API（Physics 查詢在 EditMode 一樣有效）
```

動畫取樣另外在 `EditorSceneManager.NewPreviewScene()` 裡做，
**必須 `animator.applyRootMotion = true`**，否則 `SampleAnimation` 完全不移動 root。

## 使用者給的錄影 = 最高等級的證據，而且讀得到

**可以看影片。** 用 ffmpeg 抽影格，再用 Read 當圖片讀：

```bash
ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "video.mp4"
ffmpeg -v error -i "video.mp4" -vf "fps=3,scale=640:-1" frames/f%03d.jpg   # 先總覽
# 要讀畫面上的小字（debug 面板）就從原解析度裁切，不要用縮圖：
ffmpeg -v error -ss 4 -i "video.mp4" -vf "fps=2,crop=iw*0.25:ih*0.75:iw*0.75:ih*0.22" dbg/d%02d.png
```

⚠️ **這是離線量測取代不了的東西。** 2026-09-14 的教訓：連續三輪在「理想站位」做離線重建，
數字一路變好，使用者體感卻沒變、最後更差——因為**玩家實際的起攀距離是 0.17–0.23 m，
不是我一直在量的 0.462 m**。那個數字只有從影片裡的 debug 面板讀得到。

**收到錄影就先抽影格讀面板數值**，再決定要量什麼。

## ⚠️ 假結論防治（每一條都真的發生過）

- **放臨時 rig 前先射線找地面高度**，並確認用的是碰撞體的**近面**。
  我曾把 rig 放在 y=0（實際地面 0.31）又用了箱子遠面，得出「所有 1 m 障礙物都 CorridorBlocked」的假 bug。
- **`FindAnyObjectByType<T>()` 回傳的是任意一個，不是「那一個」。**
  場景有兩個角色時我拿到敵人當成玩家，整輪工作做在錯的 rig 上。
  要確認身分就看元件（`PlayerInputSource` vs `AIInputSource`）或 layer，或直接查測試裡的常數。
- **rig 會影響 humanoid 動畫的取樣結果。** 同一支 clip 在兩個 Avatar 上指尖差 3.7 cm。
  凡是「從動畫量幾何」的推導，都要指定**執行期實際使用的 rig**。
- **認不出來的視覺特徵先查身分，別拿來推論。** 洋紅＝材質遺失（見 memory `unity-visual-artifact-triage`）。

## 改資產

`AGENTS.md`「Unity Asset Authoring」：**可以改，但只能走 Editor API**，⛔ 不得手改
`.asset`／`.prefab`／`.unity`／`.meta` 的 YAML 文字。

```csharp
var so = new UnityEditor.SerializedObject(target);
so.FindProperty("fieldName").objectReferenceValue = value;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.EditorUtility.SetDirty(target);
UnityEditor.AssetDatabase.SaveAssetIfDirty(target);
```

prefab 用 `PrefabUtility.LoadPrefabContents` → 改 → `SaveAsPrefabAsset` → `UnloadPrefabContents`。

**改之前先把現值印出來留底**——`Assets/ScriptableObjects/Motion/Bake_*.asset` 等資產
**未進版控**，沒有 git 還原路徑。改完要能說出怎麼還原，並明講「這項只有 Unity 匯入成功，沒有測試保護」。
