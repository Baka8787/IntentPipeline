---
name: unity-live-control
description: 驅動使用者開著的那個 Unity Editor——跑測試、重編譯、讀寫資產、在真實場景上量測，全部不需要進 Play Mode。動到 Unity 資產、想跑測試、或要驗證「遊戲裡實際會發生什麼」時使用。內含五個踩過的坑與不進 Play 也能重建整條 gameplay 鏈路的手法。
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

常用：`recompile` / `recompile_status` / `console` / `run_tests` / `test_status` / `eval_file`。

## 測試

```bash
unity cmd run_tests --mode EditMode --timeout 900 --detach   # 再 unity job status <id> --json
unity cmd run_tests --mode PlayMode --async_tests true       # 再輪詢 unity cmd test_status
```

**五個踩過的坑**

1. ⛔ **`unity test` 沒用** —— 它會另生一個 batchmode Editor，照樣撞 project lock
   （`專案已在執行中的編輯器（PID …）中開啟`）。一定要用 `unity cmd run_tests`。
2. ⛔ **PlayMode 一定要 `--async_tests true`**。同步跑會回 `Total: 0` ＋
   「entering play mode triggers a domain reload that drops the request」。
3. ⛔ **`recompile` 還沒 `completed` 就排測試 ⇒ 鎖死主執行緒**（`console`／`test_status`／
   `cancel_tests` 全部 timeout，實測卡 20 分鐘）。順序必須是
   `recompile` → 輪詢 `recompile_status` 到 `completed`／`up_to_date` → 才 `run_tests`。
4. ⛔ **使用者在 Play Mode 時也不要排 EditMode 測試**（同樣鎖死，踩過兩次）。
   排之前先用 `eval_file` 確認 `EditorApplication.isPlaying == false`。
5. `test_status` 在 async run 真正開始前會回 `no_tests`，**不要據此判定「跑完了」**。

照這個順序，整套 EditMode 約 25 秒。

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

`CLAUDE.md`「Unity Asset Authoring」：**可以改，但只能走 Editor API**，⛔ 不得手改
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
