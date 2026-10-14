# ai-no-camera — 交戰中 AI 方向不依賴相機的「暫時破壞」驗證

對象：`Assets/_Project/Tests/PlayMode/AIMovementNoCameraPlayModeTests.cs`（`docs/57` §14.2）。

`mutation_check.py` 暫時把 `AIMovementSource.cs` 改成三種依賴相機的錯誤版本，每一種都必須讓新測試**以預期的斷言**失敗，還原後必須再通過。三個突變都不含 `CameraTransform` 這個字，所以靜態的 A30 一個都抓不到——這正是新測試要補的缺口。

| 突變 | 錯誤行為 | 必須命中的斷言片段 |
|---|---|---|
| M1 | 沒有 `Camera.main` ⇒ 交戰時完全不產生意圖 | `沒有任何移動意圖` |
| M2 | 沒有相機 ⇒ 產生固定的錯誤方向（`Vector3.right`，非零、單位、水平，但沒朝向目標） | `沒有朝向目標` |
| M3 | 只有 Strafe 分支（原 EditMode 測試的 1.5 m 情境）在沒有相機時停步 | `Strafe）沒有移動意圖` |

```bash
python -I docs/artifacts/ai-no-camera/mutation_check.py --compile-only   # 不需 Unity：每個突變都要能編譯
python -I docs/artifacts/ai-no-camera/mutation_check.py                  # 需要健康的 Editor；經 safe_unity_test.sh
```

安全：`AIMovementSource.cs` 與 HEAD 不同就拒絕執行；原始位元組在 `finally` 還原並以 SHA-256 驗證；Unity 只經 `safe_unity_test.sh`（先 recompile、dirty scene／Editor 忙碌就停）；入口不是 PASS／TESTS_FAILED ⇒ 結論為 INCOMPLETE，永遠不算通過。
Exit：0 全部命中且還原後通過 · 1 有突變存活或還原後失敗 · 3 環境阻擋 · 2 前置條件／還原失敗。
