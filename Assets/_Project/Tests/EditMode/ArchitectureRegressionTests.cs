using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;                 // A37／A38：讀**出貨資產**，防「測試綠但出貨資料走不到」
using Project.Core.StateMachine;   // A23：直接構造四個 ambient／intrinsic state 檢查 AnimationKey 的識別性

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（ADR-003 落地）**架構回歸測試**：把 docs/02-dev-spec.md §7「架構回歸檢核清單」中
    /// 標註為「自動」的條目落實為可執行斷言（A1～A5）。與一般功能測試不同——
    /// 本檔驗證的是**架構不變量**（Ownership／DIP／Single Writer／依賴方向），
    /// 失敗代表某次修改破壞了架構契約，而不是某個功能算錯。
    ///
    /// 手法：靜態原始碼掃描（`Assets/Scripts/Core`、`Assets/Scripts/Presentation`）＋ asmdef 宣告解析。
    /// 選擇原始碼掃描而非反射，是因為多數不變量（誰寫入黑板、哪層 import 了哪層）在編譯後的
    /// 型別資訊裡已被抹平，只有在原始碼層面才驗得到。
    /// </summary>
    /// <remarks>
    /// ⚠️ 掃描範圍與已知精度：
    /// 1. **只掃 Runtime 程式**（`Core`／`Presentation`）。`Editor/` 的除錯工具（如 Inspector 監視器可手動
    ///    改寫黑板意圖）刻意排除——單一寫入者是**執行期**契約，Editor-only 除錯不受此限。
    /// 2. 掃描前會移除註解，避免文件性文字（例如註解裡提到 `CharacterPipelineRunner`）造成假陽性。
    ///    字串常值內若含 `//` 會被一併截斷——此偏差只會讓檢查**變寬鬆**（漏報），不會造成假陽性。
    /// 3. 條列式 token 比對採單純子字串比對，故意保守：寧可誤報後由人確認，也不放過反向依賴。
    /// </remarks>
    public class ArchitectureRegressionTests
    {
        private static readonly string ScriptsRoot = Path.Combine(Application.dataPath, "Scripts");

        /// <summary>Runtime 程式的掃描範圍（相對於 Assets/Scripts）。</summary>
        private static readonly string[] RuntimeFolders = { "Core", "Presentation" };

        // =====================================================================
        // 共用工具
        // =====================================================================

        private static IEnumerable<string> RuntimeScriptPaths()
        {
            foreach (string folder in RuntimeFolders)
            {
                string root = Path.Combine(ScriptsRoot, folder);
                Assert.IsTrue(Directory.Exists(root), $"找不到 Runtime 程式目錄：{root}（dev-spec §0.2 的資料夾結構可能已變更）");

                foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    yield return path;
                }
            }
        }

        /// <summary>移除區塊／行註解（保留換行以維持行號），讓掃描只看真正的程式碼。</summary>
        /// <summary>
        /// 以檔名在 `Assets/Scripts` 下唯一定位一支腳本。
        /// 檔案搬家時測試仍然有效；真的重複命名時**明講是重複**，而不是報一個誤導的「找不到」。
        /// </summary>
        private static string FindSingleScript(string fileName)
        {
            string[] matches = Directory.GetFiles(ScriptsRoot, fileName, SearchOption.AllDirectories);

            Assert.AreEqual(1, matches.Length,
                $"在 Assets/Scripts 下找到 {matches.Length} 個 {fileName}（期望剛好 1 個）：\n" +
                string.Join("\n", matches));

            return matches[0];
        }

        private static string StripComments(string source)
        {
            string withoutBlock = Regex.Replace(source, @"/\*.*?\*/",
                m => Regex.Replace(m.Value, @"[^\r\n]", string.Empty), RegexOptions.Singleline);
            return Regex.Replace(withoutBlock, @"//[^\r\n]*", string.Empty);
        }

        /// <summary>擷取指定方法的大括號內容，供只針對單一路徑的架構文字檢查使用。</summary>
        private static string ExtractMethodBody(string source, string methodName)
        {
            string code = StripStringLiterals(StripComments(source));
            Match signature = Regex.Match(
                code,
                @"\b" + Regex.Escape(methodName) + @"\s*\([^;{}]*\)\s*\{");
            Assert.IsTrue(signature.Success, $"找不到方法 {methodName} 的實作");

            int openBrace = code.IndexOf('{', signature.Index);
            int depth = 0;
            for (int i = openBrace; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0)
                {
                    return code.Substring(openBrace + 1, i - openBrace - 1);
                }
            }

            Assert.Fail($"方法 {methodName} 的大括號不成對");
            return string.Empty;
        }

        /// <summary>
        /// 移除一般字串常值（含內插字串的外殼），供「型別依賴」類掃描使用——
        /// 面向使用者的說明文字（Tooltip／LogError）指名具體元件是設定指引，不算依賴。
        /// </summary>
        private static string StripStringLiterals(string source)
        {
            return Regex.Replace(source, "\"(?:\\\\.|[^\"\\\\\\r\\n])*\"", "\"\"");
        }

        private static string RelativePath(string absolutePath)
        {
            return absolutePath.Substring(Application.dataPath.Length - "Assets".Length).Replace('\\', '/');
        }

        [Serializable]
        private class AsmdefManifest
        {
#pragma warning disable CS0649 // 由 JsonUtility 反序列化填入
            public string name;
            public string[] references;
            public string[] includePlatforms;
#pragma warning restore CS0649
        }

        private static AsmdefManifest LoadAsmdef(string fileName)
        {
            string[] hits = Directory.GetFiles(Application.dataPath, fileName, SearchOption.AllDirectories);
            Assert.AreEqual(1, hits.Length, $"預期專案內恰好一份 {fileName}，實際找到 {hits.Length} 份");
            return JsonUtility.FromJson<AsmdefManifest>(File.ReadAllText(hits[0]));
        }

        private static bool Contains(string[] values, string target)
        {
            if (values == null) return false;
            foreach (string value in values)
            {
                if (string.Equals(value, target, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // =====================================================================
        // A1 — asmdef 依賴方向必須單向（Runtime ← Editor ← Tests）
        // =====================================================================

        [Test]
        public void A1_AssemblyDependencyDirection_IsOneWay()
        {
            AsmdefManifest runtime = LoadAsmdef("Project.Runtime.asmdef");
            AsmdefManifest editor = LoadAsmdef("Project.Editor.asmdef");
            AsmdefManifest tests = LoadAsmdef("Project.Tests.EditMode.asmdef");

            Assert.IsFalse(Contains(runtime.references, "Project.Editor"),
                "Project.Runtime 不得引用 Project.Editor——依賴方向必須是 Editor → Runtime 單向，否則建置期會斷");
            Assert.IsFalse(Contains(runtime.references, "Project.Tests.EditMode"),
                "Project.Runtime 不得引用測試組件");
            // 🆕（2026-09-04）PlayMode 測試組件加入後，同一條依賴方向對它一樣成立。
            // 刻意只斷言「Runtime 不引用它」而不載入該 asmdef——測試層的組態屬測試層自己的事。
            Assert.IsFalse(Contains(runtime.references, "Project.Tests.PlayMode"),
                "Project.Runtime 不得引用 PlayMode 測試組件");
            Assert.IsTrue(runtime.includePlatforms == null || runtime.includePlatforms.Length == 0,
                "Project.Runtime 必須對所有平台開放（includePlatforms 為空）——這是它不可能相依 Editor-only 程式的結構性保證");

            Assert.IsTrue(Contains(editor.references, "Project.Runtime"),
                "Project.Editor 應引用 Project.Runtime（工具讀取執行期型別屬合法方向）");
            Assert.IsTrue(Contains(editor.includePlatforms, "Editor"),
                "Project.Editor 必須限定 Editor 平台，避免工具碼進入建置");

            Assert.IsTrue(Contains(tests.includePlatforms, "Editor"),
                "Project.Tests.EditMode 必須限定 Editor 平台");
        }

        // =====================================================================
        // A2 — Runtime 程式不得在 UNITY_EDITOR 保護之外碰 UnityEditor API
        // =====================================================================

        [Test]
        public void A2_RuntimeScripts_TouchUnityEditor_OnlyInsideEditorGuards()
        {
            var violations = new List<string>();

            foreach (string path in RuntimeScriptPaths())
            {
                string[] lines = StripComments(File.ReadAllText(path)).Split('\n');
                var guardStack = new Stack<bool>();

                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].Trim();

                    if (trimmed.StartsWith("#if", StringComparison.Ordinal))
                    {
                        bool inherited = guardStack.Count > 0 && guardStack.Peek();
                        guardStack.Push(inherited || trimmed.Contains("UNITY_EDITOR"));
                        continue;
                    }
                    if (trimmed.StartsWith("#else", StringComparison.Ordinal) ||
                        trimmed.StartsWith("#elif", StringComparison.Ordinal))
                    {
                        if (guardStack.Count > 0) guardStack.Pop();
                        guardStack.Push(false); // #else 分支不受 UNITY_EDITOR 保護
                        continue;
                    }
                    if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                    {
                        if (guardStack.Count > 0) guardStack.Pop();
                        continue;
                    }

                    if (!lines[i].Contains("UnityEditor")) continue;
                    if (guardStack.Count > 0 && guardStack.Peek()) continue;

                    violations.Add($"{RelativePath(path)}:{i + 1}");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "Runtime 程式碼觸及 UnityEditor 時必須包在 #if UNITY_EDITOR 內，否則 Player 建置會編譯失敗：\n" +
                string.Join("\n", violations));
        }

        // =====================================================================
        // A3 — Runtime 熱路徑零 LINQ（零 GC 紀律的可自動化切片）
        // =====================================================================

        [Test]
        public void A3_RuntimeScripts_DoNotUseLinq()
        {
            var violations = new List<string>();

            foreach (string path in RuntimeScriptPaths())
            {
                if (StripComments(File.ReadAllText(path)).Contains("System.Linq"))
                {
                    violations.Add(RelativePath(path));
                }
            }

            CollectionAssert.IsEmpty(violations,
                "Runtime 程式不得引用 System.Linq（迭代器與委派會在熱路徑產生 GC Alloc，違反零 GC 目標）：\n" +
                string.Join("\n", violations));
        }

        // =====================================================================
        // A4 — 層級依賴禁令（CLAUDE.md Dependency Direction ＋ ADR-003 producer context-free）
        // =====================================================================

        private struct LayerRule
        {
            public string Folder;        // 相對 Assets/Scripts 的資料夾
            public string[] Forbidden;   // 該層原始碼中不得出現的 token（黑名單）
            public string Reason;
            public bool TopLevelOnly;    // true = 不遞迴子資料夾（子資料夾另有更精確的規則）

            /// <summary>
            /// 🆕（2026-09-04）該層**允許**參照的 <c>Project.*</c> 命名空間（白名單，前綴比對）。
            /// null ＝ 本規則只做黑名單（例如 `Core` 那條擋的是第三方 API token，不是命名空間）。
            ///
            /// <para><b>為什麼要有白名單——黑名單漏過一次的實證</b></para>
            /// A4 原本只有黑名單，於是它**只擋想得到的東西，想不到的預設放行**。
            /// `AIMovementSource` 每帧直接讀 `Project.Core.Effects` 的 gameplay state
            /// （與 ADR-003 D2「producer 必須 context-free」直接衝突）就是這樣**靜默通過**的——
            /// dev-spec §7.3 原話：「A4 沒有擋下來⋯⋯這是靜默通過，不是被批准」。
            ///
            /// 白名單把預設值反過來：**沒列出的命名空間一律不通過**。
            /// 新增一個合法依賴的成本是「在這裡補一行」，而那一行正是應該有人停下來想一秒的地方。
            ///
            /// <para><b>已知精度邊界（誠實記錄）</b></para>
            /// 比對的是原始碼中出現的 `Project.X.Y` 文字，因此涵蓋 `using` 與完全限定名。
            /// 同一 assembly 內以 rootNamespace 省略前綴的相對參照（例如 `Models.LocomotionModel`）
            /// **掃不到**——本專案現況全部使用明確 `using Project.X;`，故實務上覆蓋完整。
            /// 這個邊界與既有 token 掃描同級：保守、會漏報、不會假陽性。
            /// </summary>
            public string[] AllowedNamespaces;
        }

        private static readonly LayerRule[] LayerRules =
        {
            new LayerRule
            {
                Folder = "Presentation",
                // 🆕（2026-09-15，HUD）`UnityEngine.UI` 一併列入禁令。
                // `Project.Runtime.asmdef` 為了 `App/PlayerHud` 新增了 UnityEngine.UI 參考，
                // 那條參考是**組件層級**的 ⇒ 整個 Runtime 組件從此都「編得過」uGUI。
                // ⛔ HUD 是 App 層的東西；讓表現層（甚至 Core）開始碰 Image／Canvas，
                //    等於把畫面佈局的責任滲進角色表現與 gameplay。加參考的同一刻就把門關上。
                Forbidden = new[]
                {
                    "Project.Core.StateMachine", "StateType", "Project.Core.Pipeline",
                    "IInputSource", "InputData", "UnityEngine.UI",
                },
                AllowedNamespaces = new[]
                {
                    "Project.Presentation",       // 自己
                    "Project.Core.Blackboard",    // 只讀黑板——表現層的唯一合法輸入
                    "Project.Core.Actions",       // ActionSlot／IActionLifecycleSink：Action seam 的身分與回呼
                    "Project.Core.Effects",       // ThrownProjectile 施加 Slow（docs/11 §7.5，刻意的設計）
                    // 🆕（ADR-009 D2）三個命中 sink 改為只送傷害：`CharacterHealth.ApplyDamage`。
                    //    與上一列 Core.Effects 同形狀——sink 是 gameplay event 的**投遞端**，
                    //    它必須認得收件者。⚠️ 允許的是「送傷害」，**不是**讓表現層讀血量做決定：
                    //    「未致死播受擊／致死進 Death」全部在 CharacterHealth 這一側（ADR-009 D2）。
                    "Project.Core.Survivability",
                },
                Reason = "表現層不得反向依賴狀態機或輸入層（禁止 Animation→StateMachine、Motion→Input）；表現層只讀黑板"
            },
            new LayerRule
            {
                Folder = "Core/StateMachine",
                Forbidden = new[] { "Project.Core.Pipeline", "CharacterPipelineRunner" },
                AllowedNamespaces = new[]
                {
                    "Project.Core.StateMachine",  // 自己（含 .States／.Actions）
                    "Project.Core.Blackboard",
                    "Project.Core.Actions",       // ActionSlot：Action 身分的單一來源（ADR-005 D1）
                    "Project.Core.Environment",   // TraversalProbe/Candidate：TraversalState 的唯讀准入 seam（ADR-008）
                    "Project.Core.Movement",      // IMovementModel：BaseState.Initialize 的 ambient delegate（ADR-003 D3）
                    // 🔒 ADR-007 §7-E6 的實證：原本放行整個 Project.Presentation 前綴，讓
                    //    CameraControl.AimResolver 的具體依賴靜默通過。只保留 State 真正需要的兩類 seam。
                    "Project.Presentation.Motion",      // MotionDriver／MotionBakeData：狀態驅動位移的合法 seam
                    "Project.Presentation.Animation",   // AnimationFacadeBase：狀態驅動動畫的合法 seam
                },
                Reason = "State 不得認識 Controller（禁止 State→Controller）"
            },
            new LayerRule
            {
                Folder = "Core",
                // 🆕（2026-09-15）`UnityEngine.UI` 同理——見 Presentation 那一條的說明。
                // gameplay 層碰 UI 是「把顯示需求倒灌進規則」的第一步，而 `docs/17` §3.3
                // 紅線 2 正是在防這件事（禁止為了顯示而汙染 gameplay 的資料與依賴）。
                Forbidden = new[] { "Animancer", "Animator", "UnityEngine.UI" },
                Reason = "Core 不得直接碰 Animation API，一律經 AnimationFacadeBase 抽象（禁止 Controller→Animation API）；也不得碰 UI"
            },
            // ⚠️ 本層刻意**不遞迴**：`Core/Movement` 根目錄放的是 intent producer（context-free、
            //    連 Presentation 都不得認識）；`Core/Movement/Models` 放的是 Movement Model
            //    （依 ADR-003 D4 必須驅動 Facade／MotionDriver，故允許 Presentation）。
            //    兩者紀律不同，若共用一條規則會逼出「model 不能自驅動畫參數」的錯誤結論。
            new LayerRule
            {
                Folder = "Core/Movement",
                TopLevelOnly = true,
                Forbidden = new[] { "Project.Core.StateMachine", "StateType", "Project.Presentation" },
                AllowedNamespaces = new[]
                {
                    "Project.Core.Movement",      // 自己
                    "Project.Core.Blackboard",    // 寫 MovementIntent＝producer 的唯一輸出

                    // ⚠️ **已知架構張力，不是被批准的模式**（dev-spec §7.3 紅字條目）。
                    //    AIMovementSource 每帧回讀 TemporaryGameplayEffectState 的倍率，
                    //    與本規則的 Reason（producer context-free）直接衝突，也與 CLAUDE.md
                    //    「Gameplay reads data. Gameplay does not query other gameplay systems directly.」衝突。
                    //    使用者 2026-09-02 裁決：先跑 Play 確認 Slow 的展示價值，再決定是否收斂到
                    //    黑板的 status region。Play 已通過（Acceptance G），**這筆 debt 尚未償還**。
                    //    📌 列在這裡的意義：把「靜默通過」變成「明文記載的例外」。
                    //       償還時刪掉這一行即可，A4 會立刻指出剩下的違規點。
                    "Project.Core.Effects",
                },
                Reason = "ADR-003 D2：producer 必須 context-free——不得回讀 gameplay state，否則 producer→state 同幀回圈重現"
            },
            new LayerRule
            {
                Folder = "Core/Movement/Models",
                Forbidden = new[] { "Project.Core.StateMachine", "StateType", "Project.Core.Pipeline", "CharacterPipelineRunner", "Project.Presentation.IK" },
                AllowedNamespaces = new[]
                {
                    "Project.Core.Movement",      // 自己（含 .Models）
                    "Project.Core.Blackboard",
                    "Project.Presentation",       // ADR-003 D4：model 必須自驅 Facade／MotionDriver
                },
                Reason = "model 可驅動通用 Animation/Motion seam，但不得回讀 IK post-process 輸出"
            },
            // 🆕（輪 4）仲裁層：design-doc §4.5「不該直接呼叫任何表現層 Controller 的方法
            //    （只能透過寫黑板旗標溝通）」的機器化。刻意**不**禁 StateMachine——
            //    §2.5 的資料流本就是「Arbiter 讀 state → 轉譯成旗標」，未來的 Death source 需要它。
            new LayerRule
            {
                Folder = "Core/Arbitration",
                Forbidden = new[] { "Project.Presentation", "IPresentationController" },
                AllowedNamespaces = new[]
                {
                    "Project.Core.Arbitration",   // 自己（含 .Sources）
                    "Project.Core.Blackboard",    // 寫 Arbitration 區＝仲裁的唯一輸出
                },
                Reason = "design-doc §4.5：仲裁層只能透過黑板旗標與表現層溝通，不得直接呼叫表現層 Controller"
            },
            new LayerRule
            {
                Folder = "Core/Blackboard",
                Forbidden = new[] { "Project.Core.Pipeline", "Project.Core.StateMachine", "Project.Presentation" },
                AllowedNamespaces = new[]
                {
                    "Project.Core.Blackboard",    // 自己
                    "Project.Core.Actions",       // IntentData 的 RequestedActionSlot（ADR-005 D1）
                    "Project.Core.Arbitration",   // PlayerRuntimeData 內嵌 ArbiterData（值型別欄位）
                },
                Reason = "黑板是純資料層，不得認識任何消費者（否則單向資料流退化成雙向耦合）"
            },
            // 🆕（2026-09-04）組裝根（composition root）。這一層本來就會看見很多東西——
            //    白名單在此不是為了限制廣度，而是為了讓**新出現的**依賴必須被人明確承認。
            //    刻意**不**列 `Project.Core.Effects`：Runner 若開始認識 gameplay effect，
            //    那是管線重新沾上玩法概念（A9 守 locomotion，這裡守其餘）。
            new LayerRule
            {
                Folder = "Core/Pipeline",
                Forbidden = new string[0],
                AllowedNamespaces = new[]
                {
                    "Project.Core.Pipeline",      // 自己
                    "Project.Core.Blackboard",
                    "Project.Core.Actions",
                    "Project.Core.Arbitration",
                    "Project.Core.Movement",      // 只透過 IMovementIntentSource／IMovementModel 介面（A9 另行守住具體型別）
                    "Project.Core.StateMachine",
                    "Project.Presentation",       // 組裝 MotionDriver／Facade／PresentationPipeline
                    // 🆕（ADR-007 S3a，2026-09-08 明確登記）順序 2.6 的 PlayerCombatContextSource
                    // 與順序 4.6 的 CharacterFacingSource。⚠️ 這裡登記的是**具體型別**而非介面，
                    // 屬刻意：AI 版 facing source 是 S3b 才出現，CLAUDE.md「第二個使用者出現前
                    // 不得建立 production abstraction」⇒ 現在抽 IFacingSource 是提前抽象。
                    // 📌 S3b 落地時應改為介面並回頭把這兩列收斂（docs/15 §13.1）。
                    "Project.Core.Combat",
                    "Project.Core.Facing",
                    "Project.Core.Environment", // TraversalProbe：順序 2.7 的可選具體 producer（docs/22 §9.2 W8）
                    // 🆕（ADR-009 D1）順序 0.5 的 CharacterHealth.PublishTo。同 Probe：Runner 只負責**排程**，
                    //    不認識傷害、受擊或死亡動畫。與上方刻意不列 Core.Effects 的理由不衝突——
                    //    那條擋的是「Runner 開始做 gameplay 決策」，這裡 Runner 只呼叫一個發布方法。
                    "Project.Core.Survivability",
                },
                Reason = "組裝根只認識介面與既有層；新增的依賴必須明確登記，不得靜默長出來"
            },
        };

        [Test]
        public void A4_LayerBoundaries_HaveNoForbiddenDependencies()
        {
            var violations = new List<string>();

            foreach (LayerRule rule in LayerRules)
            {
                string root = Path.Combine(ScriptsRoot, rule.Folder.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(Directory.Exists(root), $"找不到 {rule.Folder}（dev-spec §0.2 的資料夾結構可能已變更）");

                SearchOption depth = rule.TopLevelOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                foreach (string path in Directory.GetFiles(root, "*.cs", depth))
                {
                    string code = StripComments(File.ReadAllText(path));

                    // ① 黑名單：擋「這一層絕對不能碰的具體東西」（含第三方 API token）
                    foreach (string token in rule.Forbidden)
                    {
                        if (!code.Contains(token)) continue;
                        violations.Add($"{RelativePath(path)} 出現 '{token}' → {rule.Reason}");
                    }

                    // ② 白名單：擋「沒有人想到要禁、因此預設放行」的新命名空間
                    if (rule.AllowedNamespaces == null) continue;

                    // 額外剝除字串常值：assembly 屬性（InternalsVisibleTo("Project.Tests.EditMode")）
                    // 與 LogError 文字裡的命名空間是**文字**，不是型別依賴。
                    string typeCode = StripStringLiterals(code);
                    var reported = new HashSet<string>();

                    foreach (Match match in Regex.Matches(typeCode, @"Project(?:\.[A-Za-z_][A-Za-z0-9_]*)+"))
                    {
                        if (IsNamespaceAllowed(rule.AllowedNamespaces, match.Value)) continue;
                        if (!reported.Add(match.Value)) continue; // 同一個參照在同一檔只報一次

                        violations.Add(
                            $"{RelativePath(path)} 參照 '{match.Value}'，不在 {rule.Folder} 的白名單內 → {rule.Reason}\n" +
                            $"        白名單：{string.Join("、", rule.AllowedNamespaces)}\n" +
                            "        若這個依賴刻意且合法，請把它明確加進該層的 AllowedNamespaces。\n" +
                            "        ⚠️ 預設不通過正是本檢查的重點：黑名單只擋想得到的，白名單擋所有沒想到的\n" +
                            "           （dev-spec §7.3 的 AIMovementSource→Core.Effects 就是黑名單漏過的實例）。");
                    }
                }
            }

            CollectionAssert.IsEmpty(violations,
                "偵測到反向／跨層依賴：\n" + string.Join("\n", violations));
        }

        /// <summary>前綴比對：允許 <c>Project.Presentation</c> 即一併允許 <c>Project.Presentation.Motion</c>。</summary>
        private static bool IsNamespaceAllowed(string[] allowed, string reference)
        {
            for (int i = 0; i < allowed.Length; i++)
            {
                if (reference == allowed[i]) return true;
                if (reference.StartsWith(allowed[i] + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // =====================================================================
        // A5 — 黑板單一寫入者（Ownership／Single Writer）
        // =====================================================================

        private struct WriterRule
        {
            public string Member;         // PlayerRuntimeData 的可寫成員
            public string[] AllowedFiles; // 允許寫入的檔名（Runtime）
            public string Owner;          // 文件上的擁有者說明
        }

        private static readonly WriterRule[] WriterRules =
        {
            new WriterRule { Member = "MovementIntent", AllowedFiles = new[] { "PlayerLocomotionPolicy.cs", "AIMovementSource.cs" },
                             Owner = "每隻角色當下唯一 active 的 IMovementIntentSource（ADR-003 D2 single-writer）" },
            new WriterRule { Member = "CombatContext", AllowedFiles = new[] { "PlayerCombatContextSource.cs", "AIMovementSource.cs" },
                             Owner = "每隻角色當下唯一 active 的 combat context producer（ADR-007 D3）" },
            new WriterRule { Member = "Intent", AllowedFiles = new[] { "CharacterPipelineRunner.cs" },
                             Owner = "Intent Processor（管線順序 2）" },
            // 🆕（ADR-003 Stage 2）以下兩欄自此為「active Movement Model 發布的 **Movement Output**」，
            //    不再是 Runner 維護的 locomotion state。換 model ＝ 換這裡的檔名（唯一寫入者恆為一個 model）。
            new WriterRule { Member = "MoveSpeed", AllowedFiles = new[] { "LocomotionModel.cs" },
                             Owner = "active IMovementModel（順序 3 Tick；ADR-003 D4）" },
            new WriterRule { Member = "MoveDirection", AllowedFiles = new[] { "LocomotionModel.cs" },
                             Owner = "active IMovementModel（順序 3 Tick；同上）" },
            new WriterRule { Member = "IsGrounded", AllowedFiles = new[] { "MotionDriver.cs" },
                             Owner = "MotionDriver.GetGravityThisFrame" },
            // v0.10 草案曾允許「MotionDriver、Project.Core 內的狀態類別」；此處刻意收窄為
            // MotionDriver 唯一寫入，狀態只經 ApplyJumpLaunch 注入，永不直接寫黑板欄位。
            new WriterRule { Member = "VerticalVelocity", AllowedFiles = new[] { "MotionDriver.cs" },
                             Owner = "MotionDriver.GetGravityThisFrame（貼地夾持前發布；唯一寫入者）" },
            new WriterRule { Member = "JustLanded", AllowedFiles = new[] { "MotionDriver.cs" },
                             Owner = "MotionDriver.GetGravityThisFrame（唯一觸發源）" },
            new WriterRule { Member = "JustLeftGround", AllowedFiles = new[] { "MotionDriver.cs" },
                             Owner = "MotionDriver.GetGravityThisFrame（唯一觸發源）" },
            // 🆕（輪 4）Arbitration 第一次擁有合法的執行期寫入者。
            // ⚠️ 唯一寫入者是**管線**而非任何 IArbiterSource：來源只回傳自己的請求（值複製），
            //    合併與寫黑板由 ArbiterPipeline 獨佔——多來源進場時本白名單**不會**跟著變長。
            new WriterRule { Member = "Arbitration", AllowedFiles = new[] { "ArbiterPipeline.cs" },
                             Owner = "ArbiterPipeline（順序 4.5；OR 合併所有 IArbiterSource 後整體覆寫）" },
            // 🆕（M3.x-B）表現層事件廣播快照。唯一寫入者是**管線**而非任何 IPresentationEventSource：
            //    來源只回傳自己的 value struct，合併與寫黑板由 PresentationPipeline 獨佔。
            // ⚠️ 這條同時是 IPresentationController「對黑板只讀不寫」契約的機器化守衛：
            //    任何 Controller 想寫這一區都會在此變紅，契約不需要靠人記得。
            new WriterRule { Member = "PresentationEvents", AllowedFiles = new[] { "PresentationPipeline.cs" },
                             Owner = "PresentationPipeline（順序 6.5 末尾；OR 合併所有 IPresentationEventSource 後整體覆寫）" },
            // 🆕（ADR-009 D1）生存區。⚠️ 唯一寫入者是持有生命值的**元件本身**，不是管線——
            //    與 Arbitration／PresentationEvents 相反：那兩區是「多來源合併」所以由管線獨佔寫入，
            //    這一區只有一個真相持有者，沒有東西要合併。Runner 只在順序 0.5 呼叫 PublishTo。
            //    ⛔ 攻擊方 sink 不得寫這一區——它們只送傷害（D2 的決策擁有權）。
            new WriterRule { Member = "Survivability", AllowedFiles = new[] { "CharacterHealth.cs" },
                             Owner = "CharacterHealth（順序 0.5 PublishTo；ADR-009 D1 single-writer）" },
        };

        [Test]
        public void A5_BlackboardMembers_HaveSingleDocumentedWriter()
        {
            var violations = new List<string>();

            foreach (WriterRule rule in WriterRules)
            {
                // 比對「.Member（.子成員）* = / += / -= ...」形式的寫入，排除 == != >= <=。
                var assignment = new Regex(@"\." + Regex.Escape(rule.Member) + @"\b(?:\.\w+)*\s*(?:[-+*/]\s*)?=(?!=)");

                foreach (string path in RuntimeScriptPaths())
                {
                    string fileName = Path.GetFileName(path);
                    string code = StripComments(File.ReadAllText(path));
                    if (!assignment.IsMatch(code)) continue;

                    bool allowed = false;
                    foreach (string candidate in rule.AllowedFiles)
                    {
                        if (string.Equals(candidate, fileName, StringComparison.Ordinal)) { allowed = true; break; }
                    }

                    if (!allowed)
                    {
                        violations.Add($"{RelativePath(path)} 寫入 PlayerRuntimeData.{rule.Member}，" +
                                       $"但該欄位的唯一寫入者應為：{rule.Owner}");
                    }
                }
            }

            CollectionAssert.IsEmpty(violations,
                "偵測到新的黑板寫入者（違反 Ownership／Single Writer）。若這是刻意的所有權變更，" +
                "請先更新 dev-spec §1.1 權限表與本檔的 WriterRules，並在文件說明理由：\n" +
                string.Join("\n", violations));
        }

        // =====================================================================
        // A9 — 通用管線不得認識 locomotion 概念（🆕 ADR-003 Stage 2 完成判準）
        // =====================================================================

        /// <summary>
        /// Stage 2 的驗收條件本身：<c>CharacterPipelineRunner</c> 是**通用**管線驅動者，
        /// 只認識 <c>IMovementIntentSource</c>／<c>IMovementModel</c> 兩支介面。
        /// 一旦有人為了方便又把速度／平滑／gait 塞回 Runner，此測試立刻變紅——
        /// 這正是 §9-L1 當年得以悄悄存在的漏洞。
        /// </summary>
        [Test]
        public void A9_PipelineRunner_KnowsNoLocomotionConcept()
        {
            string[] forbidden =
            {
                "MoveSpeed", "MoveDirection",                       // model 的運動輸出
                "LocomotionSpeedSmoother", "SmoothDamp",           // model 的內部 dynamics
                "GaitProfile", "LocomotionModel",                  // 具體 policy／具體 model（DIP：只准依賴介面）
            };

            string path = Path.Combine(ScriptsRoot, "Core", "Pipeline", "CharacterPipelineRunner.cs");
            Assert.IsTrue(File.Exists(path), $"找不到 {path}");

            // 額外剝除字串常值：Tooltip／LogError 會（合法地）指名預設元件叫 LocomotionModel，
            // 那是給人看的設定指引，不是型別依賴。只剝這一項測試，避免放寬其他掃描。
            string code = StripStringLiterals(StripComments(File.ReadAllText(path)));
            var violations = new List<string>();
            foreach (string token in forbidden)
            {
                if (code.Contains(token)) violations.Add(token);
            }

            CollectionAssert.IsEmpty(violations,
                "CharacterPipelineRunner 重新沾上 locomotion 概念（ADR-003 D4／§9-L1 已於 Stage 2 結案）。" +
                "這些量屬 active Movement Model 的內部 dynamics，應留在 model 內：\n" +
                string.Join("\n", violations));
        }

        // =====================================================================
        // A10 — 跨幀平滑狀態全域唯一（🆕 ADR-003 Stage 2）
        // =====================================================================

        /// <summary>
        /// <c>LocomotionSpeedSmoother</c> 是值型別，**每個持有者都會有自己一份平滑狀態**。
        /// 若 Idle／Move 各持一份，狀態切換時平滑值會被重置，放開輸入的收步就會斷掉。
        /// 因此執行期只准存在一個持有者（＝ active model 本身）。
        /// </summary>
        [Test]
        public void A10_LocomotionSmoother_HasExactlyOneRuntimeHolder()
        {
            // 「宣告為欄位／區域變數」的形式：型別名後面接識別字（排除 `new`、型別自身的定義檔）。
            var declaration = new Regex(@"\bLocomotionSpeedSmoother\s+_?\w");

            var holders = new List<string>();
            foreach (string path in RuntimeScriptPaths())
            {
                if (string.Equals(Path.GetFileName(path), "LocomotionSpeedSmoother.cs", StringComparison.Ordinal)) continue;
                if (declaration.IsMatch(StripComments(File.ReadAllText(path)))) holders.Add(RelativePath(path));
            }

            Assert.AreEqual(1, holders.Count,
                "LocomotionSpeedSmoother 的執行期持有者必須恰好一個（active Movement Model）。" +
                "多於一個＝平滑狀態被切分，Idle↔Move 切換會重置收步；" +
                $"零個＝B9 平滑遺失。實際找到：{(holders.Count == 0 ? "（無）" : string.Join("、", holders))}");
        }

        // =====================================================================
        // A11 — Pose 管道的 lifetime owner ＝ 它的唯一 Writer（🆕 M3.x-A）
        // =====================================================================

        /// <summary>
        /// <c>FootIKPoseData</c> 是**單寫多讀**管道：唯一 Writer 是 <c>FootIKRig</c>，
        /// 讀取方（<c>FootIKController</c>、未來的 Foot Contact 偵測器）向它取得同一份引用。
        ///
        /// 本測試守的是**擁有權**而非寫入權：建構點只准有一個，且必須在唯一 Writer 檔案內。
        /// 多於一個＝有人自己 new 了一份，讀到的將是永遠不會被寫入的空快照
        /// （症狀是「IK 沒反應」或「偵測器收不到腳步」，而且完全不會報錯）；
        /// 零個＝管道消失。
        ///
        /// ⚠️ **刻意不採 A5 那種「以成員名做賦值型 regex」的寫法**：
        /// <c>FootIKTargetData</c> 與 <c>FootIKPoseData</c> **成員同名**（皆有 <c>LeftFootPosition</c> 等），
        /// 成員名掃描會把 Controller 對 Target 的合法寫入誤判為違規。建構點掃描不受同名干擾。
        /// </summary>
        [Test]
        public void A11_FootIKPoseData_HasExactlyOneConstructionSite_InItsSoleWriter()
        {
            // 兩種建構寫法都要抓：`new FootIKPoseData(` 與欄位初始式的 target-typed `= new(`。
            var construction = new Regex(@"new\s+FootIKPoseData\s*\(|FootIKPoseData\s+_?\w+\s*=\s*new\s*\(");

            var sites = new List<string>();
            foreach (string path in RuntimeScriptPaths())
            {
                if (construction.IsMatch(StripComments(File.ReadAllText(path)))) sites.Add(RelativePath(path));
            }

            Assert.AreEqual(1, sites.Count,
                "FootIKPoseData 的建構點必須恰好一個（它的唯一 Writer FootIKRig）。" +
                "多於一個＝有 Reader 自己 new 了一份，會靜默讀到永不更新的空快照；" +
                $"零個＝管道消失。實際找到：{(sites.Count == 0 ? "（無）" : string.Join("、", sites))}");

            StringAssert.EndsWith("FootIKRig.cs", sites[0],
                "FootIKPoseData 的 lifetime owner 必須是它的唯一 Writer（FootIKRig）。" +
                "擁有權跟著寫入權走——擁有者不是 Writer 時，新增第二個 Reader 就會被迫向其他 Controller 要引用，" +
                "違反 IPresentationController 的『Controller 彼此不得互相引用』契約。");
        }

        [Test]
        public void A12_StopAndFootPhase_DoNotExpandGameplayBlackboard()
        {
            string[] forbidden = { "Foot", "Stop", "Phase" };
            var violations = new List<string>();
            var members = typeof(Project.Core.Blackboard.PlayerRuntimeData).GetMembers(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var member in members)
            {
                foreach (string token in forbidden)
                    if (member.Name.Contains(token)) violations.Add(member.Name);
            }
            CollectionAssert.IsEmpty(violations);
        }

        [Test]
        public void A13Prime_StateTopologyMatchesAcceptedAndTrialADRs()
        {
            // 🆕（ADR-009 D3 Trial）Death、🆕（docs/26 Model B）Hurt 都加在**最後**：
            //    enum 的 int 值是 config 資產的序列化身分，插在中間會讓既有的
            //    CanBeInterruptedBy／ValidTransitions 默默指向別的狀態。
            CollectionAssert.AreEqual(
                new[] { "None", "Idle", "Move", "Jump", "Roll", "Action", "Traversal", "Death", "Hurt" },
                Enum.GetNames(typeof(Project.Core.StateMachine.StateType)));
        }

        [Test]
        public void A14_AnimationFacade_RemainsLocomotionAgnostic()
        {
            string[] forbidden = { "Stop", "Locomotion", "Walk" };
            var violations = new List<string>();
            var members = typeof(Project.Presentation.Animation.AnimationFacadeBase).GetMembers(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.DeclaredOnly);
            foreach (var member in members)
            {
                foreach (string token in forbidden)
                    if (member.Name.Contains(token)) violations.Add(member.Name);
            }
            CollectionAssert.IsEmpty(violations);
        }

        [Test]
        public void A15_LocomotionStopRuntime_HasExactlyOneRuntimeHolder()
        {
            var declaration = new Regex(@"\bLocomotionStopRuntime\s+_?\w");
            var holders = new List<string>();
            foreach (string path in RuntimeScriptPaths())
            {
                if (string.Equals(Path.GetFileName(path), "LocomotionStopRuntime.cs", StringComparison.Ordinal)) continue;
                if (declaration.IsMatch(StripComments(File.ReadAllText(path)))) holders.Add(RelativePath(path));
            }
            Assert.AreEqual(1, holders.Count);
        }

        [Test]
        public void A16_AIMovementSource_UsesNavMeshForQueriesOnly()
        {
            string path = Path.Combine(ScriptsRoot, "Core", "Movement", "AIMovementSource.cs");
            Assert.IsTrue(File.Exists(path), $"找不到 {path}");

            string code = StripComments(File.ReadAllText(path));
            StringAssert.Contains("updatePosition = false", code,
                "AI 的 NavMeshAgent 不得取得 Transform 位移 authority");
            StringAssert.Contains("updateRotation = false", code,
                "AI 的 NavMeshAgent 不得取得 Transform 旋轉 authority");
            StringAssert.DoesNotContain("CharacterController", code,
                "AI producer 只能寫 MovementIntent，不得繞過 MotionDriver 直接位移");
        }

        [Test]
        public void A19_ActionState_HasNoPerActionSubclasses()
        {
            var declaration = new Regex(@"\bclass\s+(\w+)\s*:\s*ActionState\b");
            var subclasses = new List<string>();
            foreach (string path in RuntimeScriptPaths())
            {
                MatchCollection matches = declaration.Matches(StripComments(File.ReadAllText(path)));
                foreach (Match match in matches) subclasses.Add($"{match.Groups[1].Value} ({RelativePath(path)})");
            }

            CollectionAssert.IsEmpty(subclasses,
                "禁止一個 Action 一個 State subclass；若 lifecycle 本質不同，須先在 A19 allowlist 留書面理由：\n" +
                string.Join("\n", subclasses));
        }

        [Test]
        public void A20_ActionLayer_DoesNotBypassMotionDriver()
        {
            string actionsRoot = Path.Combine(ScriptsRoot, "Core", "StateMachine", "Actions");
            var paths = new List<string>(Directory.GetFiles(actionsRoot, "*.cs", SearchOption.AllDirectories))
            {
                Path.Combine(ScriptsRoot, "Core", "StateMachine", "States", "ActionState.cs")
            };
            var violations = new List<string>();
            foreach (string path in paths)
            {
                if (StripComments(File.ReadAllText(path)).Contains("CharacterController"))
                    violations.Add(RelativePath(path));
            }
            CollectionAssert.IsEmpty(violations, "Action 位移只能經 MotionDriver：\n" + string.Join("\n", violations));
        }

        [Test]
        public void A21_ExternalActionSeams_HaveNoAnimationOrTransitionAuthority()
        {
            string[] paths =
            {
                Path.Combine(ScriptsRoot, "Core", "Actions", "ActionRequestTarget.cs"),
                Path.Combine(ScriptsRoot, "Presentation", "Actions", "ThrowProjectileEmitter.cs"),
                Path.Combine(ScriptsRoot, "Presentation", "Actions", "MeleeHitboxSink.cs"),
                Path.Combine(ScriptsRoot, "Presentation", "Actions", "ThrownProjectile.cs"),
                // 🆕 2026-09-05：Ice 的地面 AoE seam。新增 sink 就把它掛進這條不變量，
                // 否則「外部 seam 不得持有動畫／轉移權威」會隨著實作變多而逐漸失去覆蓋。
                Path.Combine(ScriptsRoot, "Presentation", "Actions", "GroundEffectSink.cs")
            };
            string[] forbidden = { "AnimationFacadeBase", "TransitionTo", "IntentData", ".Intent", ".Play(" };
            var violations = new List<string>();
            foreach (string path in paths)
            {
                string code = StripComments(File.ReadAllText(path));
                foreach (string token in forbidden)
                    if (code.Contains(token)) violations.Add($"{RelativePath(path)} 出現 {token}");
            }
            CollectionAssert.IsEmpty(violations,
                "External request／projectile 只能提交 request 或執行 side effect，不得取得 FSM／動畫 authority：\n" +
                string.Join("\n", violations));
        }

        [Test]
        public void A22_ActionState_DoesNotInstantiateUnityObjects()
        {
            string path = Path.Combine(ScriptsRoot, "Core", "StateMachine", "States", "ActionState.cs");
            string code = StripComments(File.ReadAllText(path));
            StringAssert.DoesNotContain("Instantiate", code);
            StringAssert.DoesNotContain("Destroy", code);
            // ⚠️ 介面實名為 IActionLifecycleSink（`Core/Actions/IActionLifecycleSink.cs`）。
            //    原斷言寫成 `IActionReleaseSink`——那個名字在專案裡從不存在，因此本測項自 ADR-004 落地起
            //    就是紅的，只是**當時沒有人真的跑過測試**（2026-08-31 首次執行才暴露）。
            //    這條的意圖不變：ActionState 只能把 Unity side effect 委派給 sink，不得自己生滅物件。
            StringAssert.Contains("IActionLifecycleSink", code);
        }

        // ------------------------------------------------------------------
        // A23 — AnimationKey 不得每帧配置（零 GC 紀律的可自動化切片，比照 A3）
        //
        // 背景：ADR-004 把管線順序 5 由「比較 StateType」改為「比較 AnimationKey 字串」，
        //       使同一個 ActionState 能切換多 phase。代價是本屬性從「每次轉場讀一次」
        //       變成「**每帧**讀一次」。而 `Enum.ToString()` 每次呼叫都裝箱 ＋ 配置字串，
        //       於是熱路徑每角色每帧漏 40 B（玩家＋敵人 = 80 B/frame，Profiler 實測）。
        //
        // 為什麼用「同一個實例」而不是掃字面 `.ToString()`：
        //   掃字面會過度擬合寫法（換成 nameof／字典／插值就漏掉），而**識別性**直接描述我們要的性質——
        //   「重複讀取不得產生新物件」。任何仍會配置的實作都會讓這條紅。
        // ------------------------------------------------------------------
        [Test]
        public void A23_StateAnimationKey_DoesNotAllocatePerAccess()
        {
            var states = new List<BaseState>
            {
                new IdleState(), new MoveState(), new JumpState(), new RollState(), new TraversalState()
            };

            var violations = new List<string>();
            foreach (BaseState state in states)
            {
                string first = state.AnimationKey;
                string second = state.AnimationKey;

                if (!ReferenceEquals(first, second))
                {
                    violations.Add($"{state.GetType().Name}.AnimationKey 每次讀取都回傳新實例（值＝\"{first}\"）");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "AnimationKey 由管線順序 5 每帧讀取，必須快取；`=> Type.ToString()` 會裝箱並配置字串。\n" +
                "修法：在 BaseState 快取一次（Type 對每個具體 state 是常數）。\n" +
                string.Join("\n", violations));
        }

        // =====================================================================
        // A24 — Action identity 單一來源（🆕 ADR-005 D1，Trial）
        //
        // ⚠️ 合併註記（2026-09-02）：本測項在遠端分支上原編號為 A23，與本機同輪新增的
        //    「AnimationKey 不得每帧配置」撞號。本機那條已被 dev-spec §7.1 引用在先，
        //    故本測項順延為 A24。編號是引用鍵，不是排名。
        // =====================================================================

        [Test]
        public void A24_ActionIdentity_HasExactlyOneSourceOfTruth()
        {
            const string canonical = "ActionSlot.cs";
            var violations = new List<string>();

            // ① 身分只准宣告一次。任何第二個「Action 身分」enum 都是 D1 禁止的第二把鍵。
            var declarations = new List<string>();
            foreach (string path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string code = StripComments(File.ReadAllText(path));
                if (Regex.IsMatch(code, @"enum\s+\w*(ActionSlot|ActionId|SkillId|AbilityId)\w*\b"))
                {
                    declarations.Add(RelativePath(path));
                }
            }

            if (declarations.Count != 1 || !declarations[0].EndsWith(canonical))
            {
                violations.Add(
                    $"Action 身分必須恰好宣告一次於 {canonical}，實際：[{string.Join(", ", declarations)}]");
            }

            // ② 冷卻仍是 ActionState 獨佔（ADR-004 D2 延續）——不得外流到 Runner／Config／Presentation。
            foreach (string path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path) == "ActionState.cs") continue;
                if (Path.GetFileName(path) == "ActionDefinitionSO.cs") continue; // authored 欄位，非執行期狀態
                string code = StripComments(File.ReadAllText(path));
                if (code.Contains("_cooldownEndTime"))
                {
                    violations.Add($"{RelativePath(path)} 持有冷卻執行期狀態；能不能出手只有 ActionState 能回答");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "ADR-005 D1／ADR-004 D2 違規：\n" + string.Join("\n", violations));
        }

        // =====================================================================
        // A25 — Slow 不得擴散到 MovementIntent 下游（ADR-005 Acceptance G）
        // =====================================================================

        [Test]
        public void A25_Slow_DoesNotLeakIntoMovementIntentConsumers()
        {
            string[] paths =
            {
                Path.Combine(ScriptsRoot, "Core", "Movement", "Models", "LocomotionModel.cs"),
                Path.Combine(ScriptsRoot, "Core", "Movement", "LocomotionSpeedSmoother.cs"),
                Path.Combine(ScriptsRoot, "Core", "Movement", "Models", "LocomotionStopSelector.cs"),
                Path.Combine(ScriptsRoot, "Presentation", "IK", "FootIKController.cs"),
                Path.Combine(ScriptsRoot, "Presentation", "Audio", "AudioController.cs")
            };
            string[] forbidden = { "Effect.Slow", "TemporaryGameplayEffectState", "MovementSpeedMultiplier" };
            var violations = new List<string>();

            foreach (string path in paths)
            {
                string code = StripComments(File.ReadAllText(path));
                foreach (string token in forbidden)
                {
                    if (code.Contains(token)) violations.Add($"{RelativePath(path)} 出現 Slow 符號 {token}");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "Slow 必須只在 MovementIntent producer 上縮放；速度階層、停步、Foot IK 與音效應自動沿用同一份意圖：\n" +
                string.Join("\n", violations));
        }

        // =====================================================================
        // A26 — 近戰命中時機只能來自 Action lifecycle（docs/11 §4 紅線）
        // =====================================================================

        [Test]
        public void A26_MeleeHitboxTiming_IsOwnedByActionLifecycle()
        {
            string path = Path.Combine(ScriptsRoot, "Presentation", "Actions", "MeleeHitboxSink.cs");
            Assert.IsTrue(File.Exists(path), $"找不到 {path}");

            string code = StripComments(File.ReadAllText(path));
            StringAssert.Contains("IActionLifecycleSink", code,
                "近戰命中窗必須由 ActionState 的 lifecycle seam 驅動");
            // 🔄（ADR-009 D2）原本釘的是 `RequestAction(ActionSlot.Reaction)`。
            //    受擊鏈路沒有消失，只是**投遞端換了**：sink 只送傷害，
            //    「未致死播受擊／致死進 Death」由 CharacterHealth 決定。
            //    這一條的用意不變：近戰不得自己發明命中處置，必須沿用與 projectile 相同的那一條。
            StringAssert.Contains("ApplyDamage", code,
                "近戰命中必須沿用 projectile／地面 AoE 的同一條傷害 seam（CharacterHealth.ApplyDamage）");
            StringAssert.DoesNotContain("RequestAction(ActionSlot.Reaction)", code,
                "攻擊方不得自己決定要播受擊——它不知道這一下會不會致死（ADR-009 D2）");
            StringAssert.DoesNotContain("ParticleSystem", code,
                "VFX／particle collision 不得成為命中來源或決定命中時機");
            StringAssert.DoesNotContain("OnParticleCollision", code,
                "VFX／particle collision 不得成為命中來源或決定命中時機");
        }

        /// <summary>
        /// **A27 — 攻擊 trigger 只能由 `IInputSource`（順序 1）產生。**
        ///
        /// 這條守的是一個**時序**事實，不是風格偏好：`CharacterPipelineRunner` 的順序是
        /// **2 `ProcessIntents` → 2.5 `ProduceIntent`**，且每幀開頭 `InputData inputData = default`。
        /// ⇒ 在 movement source 的 `ProduceIntent` 裡寫 `Slot*ButtonDown`，本幀已錯過 `ProcessIntents`、
        /// 下一幀又被歸零，**那個旗標永遠不會被任何人讀到**。
        ///
        /// 症狀會是「敵人就是不出手，但沒有任何錯誤訊息」——這種靜默失敗值得用測試釘死，
        /// 因為它看起來就像「AI 邏輯寫錯了」，很容易往錯的方向查。
        /// </summary>
        [Test]
        public void A27_AttackTrigger_IsProducedByInputSourceOnly()
        {
            // ⚠️ 刻意用搜尋而不是寫死資料夾：本專案的資料夾與命名空間不一一對應
            //    （`IMovementModel` 在 Models/ 但命名空間是 Project.Core.Movement），
            //    寫死路徑會在檔案搬家時變成「找不到檔案」的假性失敗——那正是 2026-09-05 首跑踩到的。
            string movementSourcePath = FindSingleScript("AIMovementSource.cs");

            string movementCode = StripComments(File.ReadAllText(movementSourcePath));
            foreach (string slotFlag in new[] { "Slot1ButtonDown", "Slot2ButtonDown", "Slot3ButtonDown" })
            {
                StringAssert.DoesNotContain(slotFlag, movementCode,
                    $"{RelativePath(movementSourcePath)} 不得寫入 {slotFlag}：\n" +
                    "    管線順序是 2 ProcessIntents → 2.5 ProduceIntent ⇒ 在此寫入的攻擊旗標永遠讀不到。\n" +
                    "    攻擊決策屬於 IInputSource（順序 1），見 AIInputSource。");
            }

            string inputSourcePath = FindSingleScript("AIInputSource.cs");

            string inputCode = StripComments(File.ReadAllText(inputSourcePath));
            StringAssert.Contains("IInputSource", inputCode,
                "AIInputSource 必須是 IInputSource（順序 1），否則它產生的攻擊旗標同樣會被錯過");
            StringAssert.DoesNotContain("IMovementIntentSource", inputCode,
                "攻擊來源不得同時扮演 movement producer——兩者刻意分屬不同管線階段");

            // AI 可持有 request retry cadence，但不得回讀 FSM／ActionState 或複製真正的執行資格。
            foreach (string forbidden in new[]
                     {
                         "using Project.Core.StateMachine", "Project.Core.StateMachine.",
                         "GetComponent<ActionState>", "TryGetComponent<ActionState>",
                         "GetComponent<FullBodyStateMachine>", "TryGetComponent<FullBodyStateMachine>",
                         "PlayerRuntimeData", "StateType"
                     })
            {
                StringAssert.DoesNotContain(forbidden, inputCode,
                    $"AIInputSource 出現 {forbidden}：producer 只能決定何時重送 request，" +
                    "不得知道或複製 ActionState 的執行資格。");
            }

            string playerInputSourcePath = FindSingleScript("PlayerInputSource.cs");
            string playerInputCode = StripComments(File.ReadAllText(playerInputSourcePath));
            foreach (string slot in new[] { "Slot1", "Slot2", "Slot3" })
            {
                StringAssert.Contains(
                    $"data.{slot}ButtonDown = {slot}Action != null && {slot}Action.WasPressedThisFrame();",
                    playerInputCode,
                    $"PlayerInputSource 的 {slot} 必須維持 WasPressedThisFrame edge semantic；" +
                    "AI request cadence 不得改變玩家輸入行為。");
            }
        }

        // =====================================================================
        // A28～A32 — ADR-007：方向權威
        // =====================================================================

        [Test]
        public void A28_ProceduralMovement_DoesNotUseTransformForward()
        {
            string path = Path.Combine(ScriptsRoot, "Presentation", "Motion", "MotionDriver.cs");
            Assert.IsTrue(File.Exists(path), $"找不到 {path}");

            string methodBody = ExtractMethodBody(File.ReadAllText(path), "ExecuteBaseMovement");
            StringAssert.DoesNotContain("transform.forward", methodBody,
                "ExecuteBaseMovement 的 procedural 位移不得以 facing（transform.forward）冒充世界移動方向；" +
                "烘焙曲線路徑的同名參照不在本方法體掃描範圍內。");
        }

        [Test]
        public void A29_ActionLifecycleSinks_DoNotResolveDirectionAuthority()
        {
            string[] sinkFiles =
            {
                "ThrowProjectileEmitter.cs",
                "GroundEffectSink.cs",
                "MeleeHitboxSink.cs"
            };
            string[] forbidden = { "AimResolver", "TryGetAimPoint", "transform.root.forward" };
            var violations = new List<string>();

            foreach (string fileName in sinkFiles)
            {
                string path = FindSingleScript(fileName);
                string code = StripStringLiterals(StripComments(File.ReadAllText(path)));
                foreach (string token in forbidden)
                {
                    if (code.Contains(token)) violations.Add($"{RelativePath(path)} 出現 {token}");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "IActionLifecycleSink 實作只能消費 ActionState 交付的方向承諾，不得自行解算方向：\n" +
                string.Join("\n", violations));
        }

        [Test]
        public void A30_OnlyPlayerMovementProducer_MayReferenceCameraTransform()
        {
            string movementRoot = Path.Combine(ScriptsRoot, "Core", "Movement");
            Assert.IsTrue(Directory.Exists(movementRoot), $"找不到 {movementRoot}");

            var violations = new List<string>();
            foreach (string path in Directory.GetFiles(movementRoot, "*.cs", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(path), "PlayerLocomotionPolicy.cs", StringComparison.Ordinal)) continue;

                string code = StripStringLiterals(StripComments(File.ReadAllText(path)));
                if (code.Contains("CameraTransform")) violations.Add(RelativePath(path));
            }

            CollectionAssert.IsEmpty(violations,
                "相機基底只能是玩家 producer 的輸入投影工具；Core/Movement 頂層其他檔案不得引用 CameraTransform：\n" +
                string.Join("\n", violations));
        }

        [Test]
        public void A31_CharacterFacingSource_IsOnlyFacingRequestSender()
        {
            var senders = new List<string>();
            var invocation = new Regex(@"\bRequestFacing\s*\(");

            foreach (string path in RuntimeScriptPaths())
            {
                if (string.Equals(Path.GetFileName(path), "MotionDriver.cs", StringComparison.Ordinal)) continue;

                string code = StripStringLiterals(StripComments(File.ReadAllText(path)));
                if (invocation.IsMatch(code)) senders.Add(RelativePath(path));
            }

            Assert.AreEqual(1, senders.Count,
                "MotionDriver.RequestFacing 必須在全 Runtime 恰好只有一個送出檔案：\n" +
                string.Join("\n", senders));
            Assert.AreEqual("Assets/Scripts/Core/Facing/CharacterFacingSource.cs", senders[0],
                "ADR-007 D3 的唯一 facing authority 必須是 CharacterFacingSource。");
        }

        [Test]
        public void A32_MotionDriver_DoesNotDecideFacing()
        {
            string path = Path.Combine(ScriptsRoot, "Presentation", "Motion", "MotionDriver.cs");
            Assert.IsTrue(File.Exists(path), $"找不到 {path}");

            string methodBody = ExtractMethodBody(File.ReadAllText(path), "ExecuteBaseMovement");
            string[] forbidden = { "LookRotation", "Slerp", "transform.rotation", "transform.Rotate" };
            foreach (string token in forbidden)
            {
                StringAssert.DoesNotContain(token, methodBody,
                    $"ExecuteBaseMovement 出現 '{token}'：ADR-007 D2／D3 規定 MotionDriver 是 rotation 執行者，" +
                    "不是『往哪面向／本幀該不該轉』的決策者；請把 facing 政策收斂回 CharacterFacingSource。");
            }
        }

        // =====================================================================
        // A33 — Direction Authority 快照不得成為第二套決策系統
        //       （🆕 2026-09-10，Observability 範圍收斂後保留的被動診斷）
        //
        // ⭐ 這條守的不是風格，是**信任鏈**：
        //    一個會自己重算 facing 優先序的 gizmo ＝ 第二套 facing 決策實作。
        //    兩邊算得不一樣時，它會讓你相信錯的那一邊——**那比沒有 debug 更糟**。
        //    docs/16-review-protocol.md 已把「下游重新推導已 commit 的狀態」列為本專案加重審查項；
        //    本測試是那條紀律在 observability 上的機器化切片，讓「debug 只記錄不重算」不再只是一句承諾。
        // =====================================================================

        [Test]
        public void A33_FacingDebugSnapshot_RemainsPassiveWithoutPresentation()
        {
            string source = File.ReadAllText(FindSingleScript("CharacterFacingSource.cs"));

            // ① 快照只能記錄決策當下的既有回傳值，不得重跑優先序或死區判定。
            string recorderBody = ExtractMethodBody(source, "RecordFacingDebug");
            string[] decisionApis =
            {
                "TryResolveFacing", "ShouldRequestFacing", "IsWithinFacingDeadzone",
                "TryGetActiveFacingCommitment", "RequestFacing",
            };

            foreach (string api in decisionApis)
            {
                StringAssert.DoesNotContain(api, recorderBody,
                    $"RecordFacingDebug 出現 '{api}'：Direction Authority 快照只能記錄" +
                    "Tick 當下已算完的值，不得成為第二套 facing 決策。");
            }

            // ② 本輪明確收回 Direction Authority 的 world-space presentation。
            StringAssert.DoesNotContain("OnDrawGizmos", source,
                "Direction Authority 目前只保留快照，不再畫四箭頭／deadzone／label。");
            StringAssert.DoesNotContain("UnityEditor.Handles", source,
                "Direction Authority 目前不應有 world-space presentation。");

            // ③ 單向紀律：擁有者寫，任何其他 runtime 路徑不得讀取 facing 快照欄位。
            string[] facingDebugFields =
            {
                "_debugDesiredDirection", "_debugMoveDirection", "_debugForwardAtDecision",
                "_debugResolvedDirection", "_debugFromActionCommitment", "_debugOutcome",
            };
            var leaks = new List<string>();
            foreach (string path in RuntimeScriptPaths())
            {
                if (string.Equals(Path.GetFileName(path), "CharacterFacingSource.cs", StringComparison.Ordinal)) continue;

                string code = StripStringLiterals(StripComments(File.ReadAllText(path)));
                foreach (string field in facingDebugFields)
                {
                    if (!code.Contains(field)) continue;
                    leaks.Add($"{RelativePath(path)}: {field}");
                }
            }

            CollectionAssert.IsEmpty(leaks,
                "debug 快照欄位是單向的（擁有者寫、Editor 診斷保留），⛔ 不得被任何 runtime 路徑讀取——\n" +
                "一旦有 gameplay 讀它，debug 顯示就從『觀察』變成『輸入』，" +
                "而它的更新時機（順序 4.6 之後、可能過期一幀）從來沒有被設計成契約：\n" +
                string.Join("\n", leaks));
        }

        [Test]
        public void A34_FootIKDebug_UsesRuntimeRenderingWithoutRepeatingPhysicsQueries()
        {
            string source = File.ReadAllText(FindSingleScript("FootIKController.cs"));
            string[] presentationMethods =
            {
                "UpdateRuntimeDebugLines", "AppendFootRuntimeLines", "AppendRuntimeProbe",
                "AppendRuntimeVector", "AppendRuntimeArrow", "AppendRuntimeLine", "GetOrCreateRuntimeLine",
                "OnDrawGizmos", "DrawFootGizmos", "DrawHeelToeOriginsGizmo",
                "DrawProbeGizmo", "DrawVectorGizmo", "DrawArrowGizmo",
            };
            string[] forbidden = { "Physics.", "RaycastGround", "SampleGround", "SampleSingleGround" };

            foreach (string method in presentationMethods)
            {
                string body = ExtractMethodBody(source, method);
                foreach (string token in forbidden)
                {
                    StringAssert.DoesNotContain(token, body,
                        $"{method} 出現 '{token}'：Foot IK presentation 只能畫 Tick 當下記錄的快照，" +
                        "不得重發 physics query，否則顯示值可能與實際 IK 決策不同。");
                }
            }

            string tickBody = ExtractMethodBody(source, "Tick");
            StringAssert.Contains("UpdateRuntimeDebugLines", tickBody,
                "Game View 主通道必須由 production Tick 已記錄的快照更新，而不是依賴 Gizmos callback。");
            StringAssert.Contains("LineRenderer", source,
                "Foot IK Game View debug 必須使用真正的 runtime renderer，正常 Play 不得依賴 Gizmos 開關。");
            StringAssert.Contains("#if UNITY_EDITOR || DEVELOPMENT_BUILD", source,
                "runtime debug renderer 只允許存在於 Editor／Development debug build。");
            StringAssert.DoesNotContain("Debug.DrawLine", source,
                "Debug.DrawLine 在 Game View 仍受 Gizmos 開關控制，不能充當正常 Play 的主通道。");

            StringAssert.Contains("private void OnDrawGizmos()", source,
                "Scene View 詳查通道可以保留非 selected-only Gizmos。");
            StringAssert.DoesNotContain("OnDrawGizmosSelected", source,
                "Scene View 詳查也不能依賴選取角色。");

            StringAssert.Contains(
                "sample.DebugFootForward = sample.TargetRotation * Vector3.forward", source,
                "heel/toe forward debug 必須記錄 production offset 實際使用的 corrected foot basis，" +
                "不得在 presentation 改用 character root 或 world axis。");
            StringAssert.Contains(
                "AppendRuntimeLine(sample.DebugHeelOrigin, sample.DebugToeOrigin", source,
                "Game View 必須直接連接實際 heel/toe sample origins，才能檢查腳掌長軸。");
            StringAssert.Contains("sample.DebugTwoPointBase = sample.TargetPosition", source,
                "BASE 標記必須快取 Heel/Toe offset 真正展開的 corrected ankle target，" +
                "不得誤標成 Animator goal 或 bone Transform。");
            StringAssert.Contains("\"L BASE\", \"L HEEL\", \"L TOE\"", source,
                "Scene View 必須明確標示左腳 BASE／HEEL／TOE。");
            StringAssert.Contains("\"R BASE\", \"R HEEL\", \"R TOE\"", source,
                "Scene View 必須明確標示右腳 BASE／HEEL／TOE。");
        }

        [Test]
        public void A39_TraversalProbe_IsEnvironmentLayersOnlyPhysicsQueryOwner()
        {
            string environmentRoot = Path.Combine(ScriptsRoot, "Core", "Environment");
            string[] paths = Directory.GetFiles(environmentRoot, "*.cs", SearchOption.AllDirectories);
            var violations = new List<string>();

            foreach (string path in paths)
            {
                string code = StripStringLiterals(StripComments(File.ReadAllText(path)));
                if (!code.Contains("Physics.")) continue;
                if (!string.Equals(Path.GetFileName(path), "TraversalProbe.cs", StringComparison.Ordinal))
                    violations.Add(RelativePath(path));
            }

            CollectionAssert.IsEmpty(violations,
                "Core/Environment 只有 TraversalProbe.cs 可以發 physics query：\n" +
                string.Join("\n", violations));

            string classifier = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalClassifier.cs"))));
            StringAssert.DoesNotContain("Time.", classifier,
                "TraversalClassifier 必須是與 frame clock 無關的純函式");
            StringAssert.DoesNotContain("PlayerRuntimeData", classifier,
                "TraversalClassifier 不得讀黑板；輸入只能是 measurement/settings/previousKind");
        }

        [Test]
        public void A40_TraversalDebug_UsesSnapshotWithoutRepeatingPhysicsQueries()
        {
            string source = File.ReadAllText(FindSingleScript("TraversalProbe.cs"));
            string[] presentationMethods =
            {
                "UpdateTraversalRuntimeDebugLines", "AppendTraversalRuntimeProbe",
                "AppendTraversalRuntimeVector", "AppendTraversalRuntimeCapsule",
                "AppendTraversalRuntimeLine", "GetOrCreateTraversalRuntimeLine",
                "EnsureTraversalRuntimeDebugRoot", "UpdateTraversalRuntimeLabel",
                "GetTraversalDebugLabel", "OnDrawGizmos", "DrawTraversalProbeGizmo",
                "DrawTraversalVectorGizmo", "DrawTraversalCapsuleGizmo",
            };
            string[] forbidden = { "Physics.", "Raycast", "CheckCapsule" };

            foreach (string method in presentationMethods)
            {
                string body = ExtractMethodBody(source, method);
                foreach (string token in forbidden)
                {
                    StringAssert.DoesNotContain(token, body,
                        $"{method} 出現 '{token}'：Traversal debug 只能畫 Tick 已記錄的 snapshot");
                }
            }

            string completeTickBody = ExtractMethodBody(source, "CompleteTick");
            StringAssert.Contains("UpdateTraversalRuntimeDebugLines", completeTickBody,
                "Game View 主通道必須在 production Tick 完成快照後更新");
            StringAssert.Contains("LineRenderer", source,
                "Traversal Game View debug 必須使用真正的 runtime renderer");
            StringAssert.Contains("#if UNITY_EDITOR || DEVELOPMENT_BUILD", source);
            StringAssert.Contains("private void OnDrawGizmos()", source);
            StringAssert.DoesNotContain("OnDrawGizmosSelected", source);
        }

        [Test]
        public void A41_TraversalCandidate_RemainsOutsidePlayerRuntimeData()
        {
            string source = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("PlayerRuntimeData.cs"))));
            StringAssert.DoesNotContain("TraversalCandidate", source);
            StringAssert.DoesNotContain("TraversalKind", source);
        }

        [Test]
        public void A42_JumpState_HasNoTraversalEnvironmentDependency()
        {
            string source = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("JumpState.cs"))));
            StringAssert.DoesNotContain("Project.Core.Environment", source);
            StringAssert.DoesNotContain("TraversalCandidate", source);
            StringAssert.DoesNotContain("TraversalKind", source);
        }

        [Test]
        public void A43_TraversalExecution_UsesCommittedMotionWithoutQueriesOrGravityFallback()
        {
            string traversal = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalState.cs"))));

            string[] queryTokens = { "Physics.", "Raycast", "CheckCapsule", "TraversalClassifier" };
            foreach (string token in queryTokens)
                StringAssert.DoesNotContain(token, traversal,
                    $"TraversalState 出現 '{token}'：執行期只能消費 CanEnter 已提交的 Probe candidate");

            StringAssert.Contains("ExecuteCommittedCurveMovement", traversal,
                "TraversalState 的三種 kind 必須共用 MotionDriver committed 3D motion 路徑");
            StringAssert.DoesNotContain("ExecuteBaseMovement", traversal,
                "Traversal committed 期間不得退回普通 gravity 路徑");
            StringAssert.DoesNotContain("ExecuteVerticalOnlyMovement", traversal,
                "Traversal committed 期間不得改走普通 gravity 路徑");

            string[] blackboardAssignments =
            {
                ".IsGrounded =", ".VerticalVelocity =", ".JustLanded =", ".JustLeftGround ="
            };
            foreach (string assignment in blackboardAssignments)
                StringAssert.DoesNotContain(assignment, traversal,
                    "TraversalState 不得成為第二個 grounded／vertical writer；唯一寫入者仍是 MotionDriver");

            string facade = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("AnimancerFacade.cs"))));
            string[] facadeForbidden =
            {
                "TraversalProbe", "TraversalCandidate", "TraversalClassifier", "Physics."
            };
            foreach (string token in facadeForbidden)
                StringAssert.DoesNotContain(token, facade,
                    $"AnimancerFacade 出現 '{token}'：Presentation 不得 query 或重新分類 traversal");
        }

        [Test]
        public void A44_StateMachineEnvironmentDependency_UsesOnlyTraversalIntegrationSeam()
        {
            string stateMachineRoot = Path.Combine(ScriptsRoot, "Core", "StateMachine");
            string[] paths = Directory.GetFiles(stateMachineRoot, "*.cs", SearchOption.AllDirectories);
            var violations = new List<string>();

            foreach (string path in paths)
            {
                string source = StripStringLiterals(StripComments(File.ReadAllText(path)));
                bool usesEnvironment = source.Contains("Project.Core.Environment") ||
                                       source.Contains("TraversalCandidate") ||
                                       source.Contains("TraversalKind") ||
                                       source.Contains("TraversalProbe");
                if (!usesEnvironment) continue;

                string fileName = Path.GetFileName(path);
                if (fileName != "TraversalState.cs" &&
                    fileName != "TraversalStateParamsSO.cs" &&
                    fileName != "TraversalSelectionPolicy.cs" &&
                    // 🆕（docs/24 §8）Animation Fitting 是 traversal seam 的一層，與 selection policy 同級：
                    //    純函式、只讀 candidate／entry／bake，不查 Physics、不寫黑板、不決定狀態轉移。
                    //    它必須認得 TraversalCandidate（現場尺寸）才能回答「動畫要怎麼播才合身」。
                    fileName != "TraversalAnimationFit.cs" &&
                    fileName != "FullBodyStateMachine.cs")
                {
                    violations.Add(RelativePath(path));
                }
            }

            CollectionAssert.IsEmpty(violations,
                "Core/StateMachine → Core/Environment 只允許 TraversalState、其 selection policy、" +
                "Animation Fitting、三格 authored params，" +
                "以及 FullBodyStateMachine 的組裝注入 seam：\n" + string.Join("\n", violations));
        }

        /// <summary>
        /// 🆕（docs/24 §8）**Animation Fitting 必須是純函式層。**
        ///
        /// 它坐在 `Selection` 與 `TraversalPlan` 之間，回答「這支動畫要怎麼播才貼近現場尺寸」。
        /// 一旦它開始自己查 Physics 或讀 Transform，就變成第二個環境查詢擁有者——
        /// 那正是 ADR-008 把所有 query 收斂到 Probe 想避免的事。
        /// 播放速率的**套用**屬於 TraversalState（經 Facade），不屬於這一層。
        /// </summary>
        [Test]
        public void A50_TraversalAnimationFitting_IsPureAndOwnsNoQueries()
        {
            string source = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalAnimationFit.cs"))));
            string[] forbidden =
            {
                "Physics.", "Time.", "transform.", "PlayerRuntimeData",
                "TraversalProbe", "AnimationFacadeBase", "MonoBehaviour",
            };
            foreach (string token in forbidden)
                StringAssert.DoesNotContain(token, source,
                    $"TraversalAnimationFit 出現 '{token}'：Fitting 必須是可測的純函式，" +
                    "不得自己查環境、不得驅動表現層");
        }

        [Test]
        public void A45_TraversalSelectionPolicy_IsPureAndDoesNotMoveOwnership()
        {
            string policy = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalSelectionPolicy.cs"))));
            string[] forbidden =
            {
                "Physics.", "Time.", "PlayerRuntimeData", "TraversalProbe", "transform."
            };
            foreach (string token in forbidden)
                StringAssert.DoesNotContain(token, policy,
                    $"TraversalSelectionPolicy 出現 '{token}'：Jump-vs-Climb 必須是 deterministic 純 gameplay policy");

            string jump = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("JumpState.cs"))));
            StringAssert.DoesNotContain("TraversalSelectionPolicy", jump,
                "JumpState 不得反向認識 traversal selection；policy 只在 Traversal 准入側消費 Jump authority");
        }

        [Test]
        public void A46_TraversalWarp_HasNoQueriesOrDirectPositionWriter()
        {
            string plan = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalWarpPlan.cs"))));
            string traversal = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalState.cs"))));
            string motion = File.ReadAllText(FindSingleScript("MotionDriver.cs"));
            string capture = ExtractMethodBody(motion, "TryCreateTraversalWarpPlan");
            string execute = ExtractMethodBody(motion, "ExecuteTraversalWarpedMovement");
            string move = ExtractMethodBody(motion, "MoveTraversalAndRecord");
            string combined = plan + "\n" + traversal + "\n" + capture + "\n" + execute + "\n" + move;

            StringAssert.DoesNotContain("Physics.", combined,
                "Warp plan／State／Motion execution 不得重發 traversal physics query");
            StringAssert.DoesNotContain("transform.position =", combined,
                "Traversal warp 不得直接 teleport Transform position");
            StringAssert.Contains("MoveTraversalAndRecord", execute,
                "warped execution 必須委派給 MotionDriver 的 traversal movement evidence seam");
            StringAssert.Contains("characterController.Move", move,
                "warped delta 必須仍由 MotionDriver 的 CharacterController.Move 權威執行");
            StringAssert.Contains("currentPosition - previousPosition", execute,
                "warp execution 必須輸出 warped current - warped previous displacement delta");

            string runtimeData = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("PlayerRuntimeData.cs"))));
            StringAssert.DoesNotContain("Warp", runtimeData,
                "Traversal warp plan 是 state-private committed data，不得新增黑板欄位");
        }

        [Test]
        public void A47_TraversalV3PoliciesAndSolvers_ArePureCommittedDataBuilders()
        {
            string entryAndSelection = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalSelectionPolicy.cs"))));
            string planBuilder = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalStateParamsSO.cs"))));
            string warpPlan = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalWarpPlan.cs"))));
            string combined = entryAndSelection + "\n" + planBuilder + "\n" + warpPlan;

            string[] forbidden =
            {
                "Physics.", "Time.", "PlayerRuntimeData", "TraversalProbe", "transform."
            };
            foreach (string token in forbidden)
                StringAssert.DoesNotContain(token, combined,
                    $"Traversal V3 entry／plan／constraint solve 出現 '{token}'：commit builders 必須是 pure data transform");

            StringAssert.DoesNotContain("SolveDual", planBuilder,
                "Traversal root constraint 只能由單一 primary contact 決定；不得恢復雙手硬約束／平均 API");
        }

        [Test]
        public void A48_MotionDriver_IsOnlyRuntimeCharacterControllerShapeWriter()
        {
            string scriptsRoot = Path.Combine(ScriptsRoot);
            string[] paths = Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories);
            var violations = new List<string>();
            string[] shapeAssignments =
            {
                "characterController.center =", "characterController.height =", "characterController.radius =",
                "_characterController.center =", "_characterController.height =", "_characterController.radius ="
            };

            foreach (string path in paths)
            {
                if (string.Equals(Path.GetFileName(path), "MotionDriver.cs", StringComparison.Ordinal)) continue;
                string source = StripStringLiterals(StripComments(File.ReadAllText(path)));
                foreach (string assignment in shapeAssignments)
                {
                    if (!source.Contains(assignment)) continue;
                    violations.Add(RelativePath(path) + " -> " + assignment);
                }
            }

            CollectionAssert.IsEmpty(violations,
                "CharacterController runtime shape property writer 只能是 MotionDriver：\n" +
                string.Join("\n", violations));

            string motion = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("MotionDriver.cs"))));
            StringAssert.Contains("RestoreTraversalCollisionProfile", motion);
            StringAssert.Contains("characterController.center = _traversalOriginalCenter", motion);
            StringAssert.Contains("characterController.height = _traversalOriginalHeight", motion);
            StringAssert.Contains("characterController.radius = _traversalOriginalRadius", motion);
        }

        [Test]
        public void A49_TraversalHandIK_ConsumesCommittedPresentationDataOnly()
        {
            // 🔄 2026-09-13：兩個類別原本是 `FootIKController.cs`／`FootIKRig.cs` 裡的**次要類別**，
            //    Unity 因此**完全無法把它們掛到 GameObject 上**（MonoBehaviour 的類名必須等於檔名），
            //    整個 Hand IK 是死路徑。已各自拆成同名檔案；本測試改讀新檔，
            //    順便把「必須存在於自己的檔案」這件事一起守住——掛不上去的元件等於不存在。
            string controllerFile = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalHandIKController.cs"))));
            int handControllerStart = controllerFile.IndexOf(
                "public sealed class TraversalHandIKController", StringComparison.Ordinal);
            Assert.GreaterOrEqual(handControllerStart, 0,
                "TraversalHandIKController 必須是 TraversalHandIKController.cs 的主類別，" +
                "否則 Unity 掛不上去（次要 MonoBehaviour 類別無法 AddComponent）");
            string handController = controllerFile.Substring(handControllerStart);
            string rigFile = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("TraversalHandIKRig.cs"))));
            int handRigStart = rigFile.IndexOf(
                "public sealed class TraversalHandIKRig", StringComparison.Ordinal);
            Assert.GreaterOrEqual(handRigStart, 0,
                "TraversalHandIKRig 必須是 TraversalHandIKRig.cs 的主類別");
            string handRig = rigFile.Substring(handRigStart);
            string combined = handController + "\n" + handRig;

            string[] forbidden =
            {
                "TraversalProbe", "Physics.", "Raycast", "CheckCapsule",
                "\n            transform.position ="
            };
            foreach (string token in forbidden)
                StringAssert.DoesNotContain(token, combined,
                    $"Traversal Hand IK 出現 '{token}'：IK 只能消費 committed target，不能修 root 或重查環境");

            StringAssert.Contains("HasActiveTraversalPlan", handController);
            StringAssert.Contains("SetIKPositionWeight", handRig);
            StringAssert.Contains("AvatarIKGoal.LeftHand", handRig);
            StringAssert.Contains("AvatarIKGoal.RightHand", handRig);

            string runtimeData = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("PlayerRuntimeData.cs"))));
            StringAssert.DoesNotContain("TraversalHand", runtimeData,
                "Traversal Hand IK presentation data 不得進 PlayerRuntimeData");
        }

        [Test]
        public void A35_CombatDirectionalLocomotion_DoesNotUseCameraOrRuntimeMixerSwitching()
        {
            string model = StripStringLiterals(StripComments(File.ReadAllText(FindSingleScript("LocomotionModel.cs"))));
            string profile = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("CombatDirectionalSpeedProfileSO.cs"))));
            string combined = model + "\n" + profile;

            string[] forbidden =
            {
                "CameraTransform", "Camera.main", "Vector3.Angle", "Vector3.SignedAngle",
                "MixerTransition2D", "Locomotion_2D"
            };
            foreach (string token in forbidden)
            {
                StringAssert.DoesNotContain(token, combined,
                    $"combat directional locomotion runtime 出現 '{token}'：方向解算只能用 committed actor facing，" +
                    "1D／2D 選擇必須是 actor asset policy，不得用 camera 或角度門檻逐幀切換。");
            }
        }

        [Test]
        public void A36_ActionState_DoesNotOwnAnimationLayering()
        {
            string source = StripStringLiterals(StripComments(
                File.ReadAllText(FindSingleScript("ActionState.cs"))));
            string[] forbidden =
            {
                "AnimancerLayer", "AvatarMask", "BaseLayerTransition", "LayerIndex"
            };

            foreach (string token in forbidden)
            {
                StringAssert.DoesNotContain(token, source,
                    $"ActionState 出現 '{token}'：ADR-006 規定 State 只送 animation key；" +
                    "layer、mask 與 Layer 0 companion 必須留在 authored TransitionMapping／Facade。");
            }
        }

        // =====================================================================
        // A37 / A38 — Model B migration（docs/26）：受擊不再是 Action
        // =====================================================================

        /// <summary>
        /// **A37 — `ActionSlot.Reaction` 已退役，runtime 與正式資產都不得再使用它。**
        ///
        /// <para>這條守的是一個容易靜默復活的東西。</para>
        /// enum 成員與數值 100 **刻意保留**（ADR-005：「要淘汰某一格請留著它的數值」——
        /// 移除或回收會讓殘存的舊資產默默指向別的 slot，而不是變成無效值）。
        /// 正因為它還在，任何人都可以「順手」再用它一次，而且編譯得過。
        /// ⇒ 用測試把「保留身分」與「不得使用」分開釘住。
        ///
        /// 資產面同樣要守：一份 `Slot: 100` 的 `ActionDefinitionSO` 會讓受擊同時
        /// 走 Action 與 Hurt 兩條路，而畫面上只看得出「有時候怪怪的」。
        /// </summary>
        [Test]
        public void A37_RetiredReactionSlot_HasNoRuntimeCallerOrAsset()
        {
            var violations = new List<string>();

            foreach (string path in RuntimeScriptPaths())
            {
                // 註解與字串裡提到它是**允許的**（退役說明、遷移紀錄都需要指名它）。
                string code = StripStringLiterals(StripComments(File.ReadAllText(path)));
                if (code.Contains("ActionSlot.Reaction"))
                    violations.Add($"{RelativePath(path)} 仍引用 ActionSlot.Reaction");
            }

            // ── 資產面 ────────────────────────────────────────────────────────
            // 🔒 **2026-09-14 遷移完成**：`DamageDefinition.asset`（最後一份 Slot-100 資產）已刪除，
            //    內容全數遷移（bake → bakeMappings、動畫鍵 → prefab mapping、時長 → bake.Duration）。
            //    本條因此從「只允許一個已知孤兒」收緊為**零容忍**。
            //    ⚠️ enum 成員與數值 100 **仍然保留**（ADR-005：淘汰某一格要留著它的數值，
            //       否則殘存資產會默默指向別的 slot）——保留身分、禁止使用，是兩件事。
            foreach (string guid in AssetDatabase.FindAssets("t:ActionDefinitionSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<
                    Project.Core.StateMachine.Actions.ActionDefinitionSO>(path);
                if (definition == null) continue;
#pragma warning disable CS0618 // 本測試的職責就是偵測退役成員，必須指名它
                if (definition.Slot == Project.Core.Actions.ActionSlot.Reaction)
#pragma warning restore CS0618
                    violations.Add($"{path} 的 Slot 是已退役的 Reaction(100)");
            }

            // ⭐ 第二道：就算有人建了一份 Slot-100 資產，真正讓 Action 路徑復活的是**把它接進 config**。
            foreach (string guid in AssetDatabase.FindAssets("t:StateMachineConfigSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<
                    Project.Core.StateMachine.StateMachineConfigSO>(path);
                if (config == null) continue;
                config.Initialize();
#pragma warning disable CS0618
                if (config.GetActionDefinition(Project.Core.Actions.ActionSlot.Reaction) != null)
#pragma warning restore CS0618
                    violations.Add($"{path} 仍把一份 Slot-100 Definition 接在 actionDefinitions 上");
            }

            CollectionAssert.IsEmpty(violations,
                "ActionSlot.Reaction 已於 docs/26（Model B）退役——受擊改為 StateType.Hurt，" +
                "由 SurvivabilityData.JustTookDamage 驅動。\n" +
                "成員與數值 100 僅為 serialization 相容而保留（ADR-005：淘汰某一格要留著它的數值，" +
                "否則殘存資產會默默指向別的 slot）。\n" +
                string.Join("\n", violations));
        }

        /// <summary>
        /// **A38 — 出貨 config 的 transition permission 必須與已裁決的政策一致。**
        ///
        /// <para><b>⭐ 為什麼這條非要讀正式資產不可</b></para>
        /// 使用者 2026-09-14 明確要求：**「Roll／Traversal 阻擋 Hurt 應由 config 明確表示，
        /// 不要依靠 `Hurt priority &lt; Roll priority` 來代替」**——
        /// permission（`CanBeInterruptedBy`）與 candidate priority 是兩個概念，
        /// 後者只在**多個合法 transition 並存時**決定誰贏。
        ///
        /// 若只用程式測試建一份 config 來驗，驗到的是測試自己編的資料；
        /// 真正決定遊戲行為的是這兩份 `.asset`。這與 `TraversalBakedAssetTests` 的 B 系列同一種理由：
        /// **防「測試綠但出貨資料完全走不到」**。
        /// </summary>
        [Test]
        public void A38_ShippingStateConfigs_MatchDecidedInterruptionPolicy()
        {
            const string PlayerConfigPath =
                "Assets/ScriptableObjects/StateMachine/PlayerStateMachineConfig.asset";
            const string EnemyConfigPath =
                "Assets/ScriptableObjects/StateMachine/EnemyStateMachineConfig.asset";

            AssertConfigPolicy(PlayerConfigPath, hasTraversal: true);
            AssertConfigPolicy(EnemyConfigPath, hasTraversal: false);
        }

        private static void AssertConfigPolicy(string path, bool hasTraversal)
        {
            var config = AssetDatabase.LoadAssetAtPath<
                Project.Core.StateMachine.StateMachineConfigSO>(path);
            Assert.IsNotNull(config, $"找不到 {path}");
            config.Initialize();

            var T = typeof(Project.Core.StateMachine.StateType);
            var Idle = Project.Core.StateMachine.StateType.Idle;
            var Move = Project.Core.StateMachine.StateType.Move;
            var Jump = Project.Core.StateMachine.StateType.Jump;
            var Roll = Project.Core.StateMachine.StateType.Roll;
            var Action = Project.Core.StateMachine.StateType.Action;
            var Traversal = Project.Core.StateMachine.StateType.Traversal;
            var Hurt = Project.Core.StateMachine.StateType.Hurt;
            var Death = Project.Core.StateMachine.StateType.Death;
            _ = T;

            var violations = new List<string>();

            void Require(Project.Core.StateMachine.StateType from,
                         Project.Core.StateMachine.StateType to, bool expected, string why)
            {
                bool actual = config.CheckCanInterrupt(from, to);
                if (actual != expected)
                {
                    violations.Add(
                        $"{from} 可否被 {to} 打斷：預期 {expected}，實際 {actual} —— {why}");
                }
            }

            // ── Hurt 的 transition permission（使用者 2026-09-14 裁決）──────────────
            Require(Idle, Hurt, true, "Locomotion → Hurt：允許");
            Require(Move, Hurt, true, "Locomotion → Hurt：允許");
            Require(Jump, Hurt, true, "Jump → Hurt：允許（⚠️ 舊資產的 Jump 清單是空的）");
            Require(Action, Hurt, true, "Action → Hurt：允許（是否真的進去仍受 phase 的 Interruptible 夾制）");
            Require(Hurt, Hurt, true, "Hurt → Hurt：允許（硬直中再次受擊要再踉蹌一次）");
            Require(Roll, Hurt, false,
                "Roll → Hurt：**不允許**。⛔ 這一條必須由 config 明確表示，" +
                "不得用 Hurt priority < Roll priority 代替——permission 與 priority 是兩個概念。");
            if (hasTraversal)
            {
                Require(Traversal, Hurt, false,
                    "Traversal → Hurt：**不允許**。同上，必須由 config 明確表示。");
            }

            // ── Death 對所有 living state 都必須可達 ────────────────────────────────
            foreach (var living in new[] { Idle, Move, Jump, Roll, Action, Hurt })
                Require(living, Death, true, $"{living} → Death：所有 living state 都必須可死");
            if (hasTraversal) Require(Traversal, Death, true, "Traversal → Death：lethal 必須能進");

            // ── Death 是吸收態：沒有任何狀態「打斷」得了它 ─────────────────────────
            // 🔄 2026-09-14（respawn，ADR-009 D3 修訂）：這一整段**一字未改**。
            //    重生不是「被打斷」——是死亡這個前提本身消失了，所以走的是自然過渡。
            //    ⇒ 三層鎖裡的這一層仍然完全封死。
            foreach (var any in new[] { Idle, Move, Jump, Roll, Action, Traversal, Hurt, Death })
                Require(Death, any, false, "Death → 任何狀態：不得被任何狀態中斷");

            // 🔄 2026-09-14（respawn）：由「必須留空」改為「必須恰好是 Idle／Move」。
            //    出口存在，但**最小**：⛔ 不得出現 Action／Jump／Roll／Traversal。
            //    直接開那些邊會讓「重生瞬間就能出手」變成沒人設計過的能力，而且它看起來像 bug
            //    ——按著攻擊鍵等重生，角色一活過來就揮一拳。
            //    真正把角色救出來的權威仍然只有 `CharacterHealth.Revive()`：
            //    `DeathState.CanTransitionAway => !IsDead`，這裡的清單只決定「出去之後落在哪」。
            var deathTransitions = new List<Project.Core.StateMachine.StateType>(
                config.GetValidTransitions(Death));
            CollectionAssert.AreEquivalent(
                new[] { Idle, Move }, deathTransitions,
                $"{path}：Death 的 ValidTransitions 必須恰好是 Idle／Move——" +
                "吸收態的鎖有三層（Priority／CanTransitionAway／CanBeInterruptedBy／ValidTransitions），" +
                "其中一半住在資產裡，所以要用測試而不是紀律守住。");

            // ── priority：只在多個 transition 都合法時才決定誰贏 ──────────────────
            int hurtPriority = config.GetPriority(Hurt);
            int actionPriority = config.GetPriority(Action);
            int deathPriority = config.GetPriority(Death);
            Assert.Greater(hurtPriority, actionPriority,
                $"{path}：Hurt priority 必須 > Action —— EvaluateInterrupts 用 strict '>' 比較，" +
                "同分時由 Dictionary 註冊順序決定勝負（Action 先註冊），受擊會被靜默吃掉。");
            Assert.Greater(deathPriority, hurtPriority,
                $"{path}：Death priority 必須高於 Hurt。");

            CollectionAssert.IsEmpty(violations,
                $"{path} 的 transition permission 與已裁決政策不符（docs/26 §I.6）：\n" +
                string.Join("\n", violations));
        }
    }
}
