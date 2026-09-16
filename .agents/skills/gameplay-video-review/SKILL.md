---
name: gameplay-video-review
description: 看使用者錄的 Unity 遊玩影片（`C:/Users/USER/Videos/*.mp4`，OBS 螢幕錄影）。用 ffmpeg 抽格 ＋ Read 圖片，從影片裡實際觀察行為，而不是回「我看不了影片」。含 Game view 裁切座標、contact sheet 手法、debug 環配色對照表，以及五個踩過的坑。使用者貼 `.mp4`／說「看影片」／要你從錄影判斷某個行為時使用。
---

# 從錄影裡真的看到東西

**「我沒有影片工具」是錯的結論。** 沒有原生影片理解，但有 `ffmpeg` ＋ `Read` 圖片——
抽格 → 讀圖，就能把影片變成可觀察的證據。這條路已驗證可用（2026-09-15）。

## 0. 先決條件

`ffmpeg` 有裝但**不在 PATH**，每次都要先加：

```bash
export PATH="$PATH:/c/Users/USER/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.1-full_build/bin"
```

⚠️ 檔名有空格（`2026-09-15 19-32-20.mp4`），路徑一律加引號。
⚠️ **`ls | head -N` 會把新檔切掉**——檔案在不在一律用 `find ... -iname "*<日期>*"` 判斷，
不要因為 `ls` 沒看到就說「檔案不存在」。

先探規格，再決定抽格策略：

```bash
ffprobe -v error -show_entries format=duration -show_entries stream=width,height,r_frame_rate \
  -of default=noprint_wrappers=1 "C:/Users/USER/Videos/<檔名>.mp4"
```

典型：1280×720 / 30fps / 60–120s。

## 1. 🔴 最重要的一件事：這是 **Editor 螢幕錄影**，不是 Game 畫面錄影

整個 1280×720 裡，**Game view 只佔底部一小條**。不裁切就直接抽格＝看到的 95% 是
Hierarchy／Project 面板，遊戲內容小到判斷不了任何事。

**實測裁切框**：

```
crop=1056:584:112:86     # ✅ 修好 OBS ＋ Maximize On Play 之後（2026-09-15 20:24 起）——角色全身入鏡
crop=656:184:90:536      # ⛔ 舊的、被切掉的錄影（2026-09-15 19:32 之前）。膝蓋以下看不到，見 §7
```

⚠️ **這個框綁定使用者當下的面板佈局，換一次佈局就失效。**
每支新影片先抽一張全幅圖確認：

```bash
ffmpeg -y -v error -ss 60 -i "<影片>" -frames:v 1 "$OUT/full.png"   # 再 Read 它，量出 Game view 邊界
```

## 2. 兩段式：先 contact sheet 掃全片，再 burst 追細節

**每讀一張圖都要花 token**，所以先粗後細，不要一開始就 10fps。

### 第一段——全片概覽（找「什麼時候發生了什麼」）

```bash
OUT="<scratchpad>/vid"; mkdir -p "$OUT"
ffmpeg -y -v error -ss 17 -i "<影片>" \
 -vf "crop=656:184:90:536,fps=1/2,\
drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='%{eif\:(17+n*2)\:d}s':x=6:y=6:\
fontsize=22:fontcolor=yellow:box=1:boxcolor=black@0.75,\
tile=2x5:margin=2:padding=2" -an "$OUT/gv_%02d.png"
```

2 秒一格、2×5 拼成一張 → 95 秒的片只要 **4 張圖**讀完。

⚠️ **Windows 字型路徑在 ffmpeg filter 裡要跳脫成 `C\:/Windows/Fonts/arial.ttf`**
（冒號要跳脫、斜線用正斜線）。時間戳燒進畫面是必要的——沒有它就沒辦法在報告裡
指出「第 87 秒」，也沒辦法回頭 burst 同一個時刻。
`-ss` 放在 `-i` **前面**才是快速 seek。

### 第二段——鎖定時刻 burst

```bash
ffmpeg -y -v error -ss 86 -t 7 -i "<影片>" \
 -vf "crop=656:184:90:536,fps=1,drawtext=...,tile=1x7:margin=2:padding=2" -an "$OUT/x_%02d.png"
```

判斷**動畫節奏／腳頻／單帧事件**才提到 `fps=10`，並縮小 `scale` 讓 16 格塞進一張。

## 3. ⛔ 這種錄影答不出來的問題

**Game view 那一條的下緣被切掉，角色膝蓋以下看不到**（成因見 §7——**是 OBS 設定切的**）。
⇒ 腳步節奏、滑步、Foot IK 落點、落地姿勢 —— **這類影片一律判斷不了**，不要硬推。

要回答那類問題，請使用者**用 Maximize On Play 重錄**（見 §7）。
講清楚是錄影限制，不是「看起來沒問題」。

## 4. Debug 環配色對照（`MotionDriver.UpdateCapsuleDebugRing`，正本在程式）

Game view 的 Gizmos 開著時會看到五個同半徑的環，**這是 Dynamic Capsule V2 的五層管線**：

| 顏色 | 階段 |
|---|---|
| 灰 `(0.5,0.5,0.55)` | base center（未偏移基準） |
| **黃** | Desired |
| **橙** `(1,0.55,0.1)` | Capped |
| **青 cyan** | Smoothed |
| **洋紅 magenta** | Safe ＝ **實際寫入 controller.center** |

**五環重合 ＝ 姿勢站直、不需要偏移，那不是壞掉。**

其他常見 gizmo：`AIMovementSource` 在**敵人**身上畫 min（紅橙 `1,0.4,0.3`）／max（青）交戰距離環；
`AIInputSource` 畫 attackRange（想出手＝暖橙、不想＝暗灰）。

⚠️ **認不出來的東西先查身分，不要拿來推論。**
2026-09-15 畫面裡那個貫穿全片的**大紅圈**，對照完 `grep -rn "DrawWireDisc"` 仍未確定歸屬
（半徑比任何一個候選都大）。**當時的正確處置是標記為未識別，而不是猜一個解釋。**
同 [[unity-visual-artifact-triage]]。

## 5. 影片能證明什麼、不能證明什麼

✅ **能**：事件順序（誰先誰後）、狀態有沒有進／退、UI 數值變化（血條）、
投射物有沒有飛出去、屍體上還有沒有 debug 在畫、極近距離鏡頭穿模。

❌ **不能**：幀數、GC、實際數值、「有沒有 bug」的根因。
影片給的是**現象**；歸因一律回程式與測試。**看到現象 → 回 repo 找機制 → 才下結論。**

⚠️ 錄影窗口往往比你需要的短。2026-09-15 那支在敵人死後只剩 **4 秒**就結束，
不足以觀察 `disengageSeconds` 這種秒級的脫離計時。
**視窗不夠長就直說「觀察不到」，不要用 4 秒去否定一個 10 秒的計時器。**

## 6. 收尾

抽出來的 PNG 全部放 **scratchpad**，不進 repo（`AGENTS.md`：scratchpad 只放用完即丟的中間產物）。
真的要留的畫面（當成 bug 存證）才走 `docs/images/`，並在 `docs/00-map.md` 留指標。

需要讓使用者在手機上看到某一張，用 `SendUserFile`。

## 7. 🔴 錄影來源本身是壞的（2026-09-15 查證，**畫面有被切掉**）

**2026-09-15 那支影片的 Game view 之所以只有一條，不是 Unity 佈局，是 OBS 把畫面切掉了。**

使用者機器：**BenQ EX2710Q**（外接主螢幕，2560×1440 實體／Windows 邏輯 2048×1152 ＝125% 縮放）
＋ **AUO B173HAN04.9**（筆電內建 1920×1080，掛在 NVIDIA；BenQ 掛在 Intel）。

OBS 設定實測：

| 位置 | 值 |
|---|---|
| `basic.ini [Video]` | `BaseCX/CY = 2560×1440`、`OutputCX/CY = **1280×720**`、30fps |
| scene `monitor_capture` | `monitor_id` ＝ **BenQ**（2560×1440） |
| scene item transform | `pos = (0,0)`、**`scale = 1.3333`**、`bounds_type = 0`、`crop_* = 0` |

**算一下就知道**：來源 2560×1440 × 1.3333 ＝ **3413×1920**，畫布只有 2560×1440
⇒ **右邊 25%、下面 25% 直接超出畫布被切掉**。
（`1.3333 = 2560/1920` ⇒ 這個 scale 是「當初來源還是 1920×1080 時 Fit to screen 留下的陳舊值」。）

⚠️ **`cropdetect` 回 `crop=1280:720:0:0` 不代表沒被切**——內容是**溢出**不是**留黑邊**，
兩者 cropdetect 分辨不出來。**不要拿「滿版無黑邊」推論「畫面完整」，我犯過這個錯。**
可靠的判準是**找應該在畫面裡卻不見的東西**：那支片子裡 **Windows 工作列整條不見**
（`Screen.AllScreens` 顯示 DISPLAY2 的 `WorkingArea` 比 `Bounds` 矮 48px ⇒ 工作列存在），
那就是下緣被切的直接證據。

**修法（使用者自己點，一步）**：來源上右鍵 → 變換 → **符合螢幕大小（Ctrl+F）** 或 **重設變換（Ctrl+R）**。
順帶建議把 `OutputCX/CY` 從 1280×720 提到 1920×1080——目前是 2× 降採樣，判斷動畫品質很吃虧。

### ⇒ 但要看動畫品質，最省事的還是不要碰 OBS

**請使用者用 Unity 的 `Maximize On Play`**（Game view 分頁 Shift+Space）。
Game view 佔滿整個 Unity 視窗 ⇒ 角色全身入鏡、不需要裁切框、不受面板佈局漂移影響。

📌 **每支新影片開工前的必做檢查**：抽一張全幅圖 Read 它，確認
①Game view 邊界在哪 ②工作列／視窗邊框在不在（不在＝被切）。**兩秒的事，省掉整輪誤判。**
