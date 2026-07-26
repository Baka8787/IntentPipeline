using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

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
        private static string StripComments(string source)
        {
            string withoutBlock = Regex.Replace(source, @"/\*.*?\*/",
                m => Regex.Replace(m.Value, @"[^\r\n]", string.Empty), RegexOptions.Singleline);
            return Regex.Replace(withoutBlock, @"//[^\r\n]*", string.Empty);
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
            public string[] Forbidden;   // 該層原始碼中不得出現的 token
            public string Reason;
            public bool TopLevelOnly;    // true = 不遞迴子資料夾（子資料夾另有更精確的規則）
        }

        private static readonly LayerRule[] LayerRules =
        {
            new LayerRule
            {
                Folder = "Presentation",
                Forbidden = new[] { "Project.Core.StateMachine", "StateType", "Project.Core.Pipeline", "IInputSource", "InputData" },
                Reason = "表現層不得反向依賴狀態機或輸入層（禁止 Animation→StateMachine、Motion→Input）；表現層只讀黑板"
            },
            new LayerRule
            {
                Folder = "Core/StateMachine",
                Forbidden = new[] { "Project.Core.Pipeline", "CharacterPipelineRunner" },
                Reason = "State 不得認識 Controller（禁止 State→Controller）"
            },
            new LayerRule
            {
                Folder = "Core",
                Forbidden = new[] { "Animancer", "Animator" },
                Reason = "Core 不得直接碰 Animation API，一律經 AnimationFacadeBase 抽象（禁止 Controller→Animation API）"
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
                Reason = "ADR-003 D2：producer 必須 context-free——不得回讀 gameplay state，否則 producer→state 同幀回圈重現"
            },
            new LayerRule
            {
                Folder = "Core/Movement/Models",
                Forbidden = new[] { "Project.Core.StateMachine", "StateType", "Project.Core.Pipeline", "CharacterPipelineRunner" },
                Reason = "ADR-003 D3：model 由 state delegate 呼叫、不得反向認識狀態機或管線（model 正交於 gameplay FSM）"
            },
            // 🆕（輪 4）仲裁層：design-doc §4.5「不該直接呼叫任何表現層 Controller 的方法
            //    （只能透過寫黑板旗標溝通）」的機器化。刻意**不**禁 StateMachine——
            //    §2.5 的資料流本就是「Arbiter 讀 state → 轉譯成旗標」，未來的 Death source 需要它。
            new LayerRule
            {
                Folder = "Core/Arbitration",
                Forbidden = new[] { "Project.Presentation", "IPresentationController" },
                Reason = "design-doc §4.5：仲裁層只能透過黑板旗標與表現層溝通，不得直接呼叫表現層 Controller"
            },
            new LayerRule
            {
                Folder = "Core/Blackboard",
                Forbidden = new[] { "Project.Core.Pipeline", "Project.Core.StateMachine", "Project.Presentation" },
                Reason = "黑板是純資料層，不得認識任何消費者（否則單向資料流退化成雙向耦合）"
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
                    foreach (string token in rule.Forbidden)
                    {
                        if (!code.Contains(token)) continue;
                        violations.Add($"{RelativePath(path)} 出現 '{token}' → {rule.Reason}");
                    }
                }
            }

            CollectionAssert.IsEmpty(violations,
                "偵測到反向／跨層依賴：\n" + string.Join("\n", violations));
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
            new WriterRule { Member = "MovementIntent", AllowedFiles = new[] { "PlayerLocomotionPolicy.cs" },
                             Owner = "當下 active 的 IMovementIntentSource（ADR-003 D2 single-writer）" },
            new WriterRule { Member = "Intent", AllowedFiles = new[] { "CharacterPipelineRunner.cs" },
                             Owner = "Intent Processor（管線順序 2）" },
            // 🆕（ADR-003 Stage 2）以下三欄自此為「active Movement Model 發布的 **Movement Output**」，
            //    不再是 Runner 維護的 locomotion state。換 model ＝ 換這裡的檔名（唯一寫入者恆為一個 model）。
            new WriterRule { Member = "MoveSpeed", AllowedFiles = new[] { "LocomotionModel.cs" },
                             Owner = "active IMovementModel（順序 3 Tick；ADR-003 D4）" },
            new WriterRule { Member = "MoveDirection", AllowedFiles = new[] { "LocomotionModel.cs" },
                             Owner = "active IMovementModel（順序 3 Tick；同上）" },
            new WriterRule { Member = "UpperBodyWeight", AllowedFiles = new[] { "LocomotionModel.cs" },
                             Owner = "active IMovementModel（順序 3 Tick；同上）" },
            new WriterRule { Member = "IsGrounded", AllowedFiles = new[] { "MotionDriver.cs" },
                             Owner = "MotionDriver.GetGravityThisFrame" },
            new WriterRule { Member = "JustLanded", AllowedFiles = new[] { "MotionDriver.cs" },
                             Owner = "MotionDriver.GetGravityThisFrame（唯一觸發源）" },
            new WriterRule { Member = "JustLeftGround", AllowedFiles = new[] { "MotionDriver.cs" },
                             Owner = "MotionDriver.GetGravityThisFrame（唯一觸發源）" },
            // 🆕（輪 4）Arbitration 第一次擁有合法的執行期寫入者。
            // ⚠️ 唯一寫入者是**管線**而非任何 IArbiterSource：來源只回傳自己的請求（值複製），
            //    合併與寫黑板由 ArbiterPipeline 獨佔——多來源進場時本白名單**不會**跟著變長。
            new WriterRule { Member = "Arbitration", AllowedFiles = new[] { "ArbiterPipeline.cs" },
                             Owner = "ArbiterPipeline（順序 4.5；OR 合併所有 IArbiterSource 後整體覆寫）" },
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
                "MoveSpeed", "MoveDirection", "UpperBodyWeight",   // model 的運動輸出
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
    }
}
