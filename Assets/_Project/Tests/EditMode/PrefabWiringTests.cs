using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Arbitration;
// 註：IMovementModel 雖然放在 Core/Movement/Models/ 資料夾，命名空間仍是 Project.Core.Movement
//     （本專案的資料夾與命名空間刻意不一一對應）。
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using Project.App;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// **Verification Ladder L1**（`docs/12-workflow.md` §6）：Prefab ／ Inspector 接線契約的自動驗證。
    ///
    /// <para><b>這個檔案取代了什麼</b></para>
    /// dev-spec §7.2-**M3** 是一整面牆的人工接線指示（「Runner 的 X 欄位拖入 Y」「沒拖＝暫停中按跳躍會卡住」…），
    /// 每次動到角色組裝就要人工重走一遍。**M3 的理由寫的是「資產／Prefab 配置屬使用者側（AI 不碰 .prefab）」——
    /// 那是「AI 不能<b>接線</b>」的理由，不是「測試不能<b>驗證</b>接線」的理由。**
    /// 本檔把可機器判定的那一半接了過來：接線<b>由使用者做</b>，接線<b>對不對由測試講</b>。
    ///
    /// <para><b>只讀，絕不寫</b></para>
    /// 全程 <see cref="AssetDatabase"/> ＋ <see cref="SerializedObject"/> 唯讀存取，
    /// 不呼叫 <c>ApplyModifiedProperties</c>、不 <c>SetDirty</c>、不 <c>SaveAssets</c>。
    /// ⛔ 本檔**不得**新增任何會修改 `.prefab`／`.asset`／`.meta` 的程式碼（CLAUDE.md 紅線）。
    ///
    /// <para><b>設計原則：斷言「解析得出來」，不是斷言「欄位有填」</b></para>
    /// <see cref="CharacterPipelineRunner.Awake"/> 對多數 seam 都有「欄位留空 ⇒ 同物件 <c>GetComponent</c> 補洞」
    /// 的防禦線。因此正確的契約是**最終解析得到**，不是**Inspector 欄位非空**——
    /// 後者會把合法的留空配置誤判成違規（X Bot 的 Movement Intent Source／Model 正是刻意留空）。
    /// 解析規則必須與 <c>Awake</c> **逐字對齊**：那裡用的是 <c>GetComponent</c>（同一個 GameObject），
    /// 不是 <c>GetComponentInChildren</c>。
    ///
    /// <para><b>為什麼用「關聯式」而不是「寫死清單」</b></para>
    /// 例如 sink 覆蓋率：不寫死「Slot1／2／3 都要有 sink」（那是在斷言一個**還沒發生**的未來），
    /// 而是斷言「**該角色的 config 註冊了哪些 slot，就要有對應的 sink**」。
    /// 這條今天是綠的，且在使用者把 Quick／Ice 的 Definition 加進 config 卻忘了填 sink 的那一刻自動變紅。
    /// </summary>
    public class PrefabWiringTests
    {
        /// <summary>角色 prefab 的搜尋根目錄。第三方素材夾刻意不掃。</summary>
        private const string PrefabSearchRoot = "Assets/Prefabs";

        // =====================================================================
        // 探索：任何帶 CharacterPipelineRunner 的 prefab 自動納入覆蓋範圍
        // =====================================================================

        private struct CharacterPrefab
        {
            public string Path;
            public GameObject Root;
            public CharacterPipelineRunner Runner;
            public SerializedObject RunnerSerialized;

            /// <summary>Runner 所在的那顆 GameObject——同物件補洞的解析基準。</summary>
            public GameObject Host => Runner.gameObject;

            public override string ToString() => Path;
        }

        private static List<CharacterPrefab> LoadCharacterPrefabs()
        {
            var result = new List<CharacterPrefab>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabSearchRoot });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                var runner = root.GetComponentInChildren<CharacterPipelineRunner>(true);
                if (runner == null) continue;

                result.Add(new CharacterPrefab
                {
                    Path = path,
                    Root = root,
                    Runner = runner,
                    RunnerSerialized = new SerializedObject(runner)
                });
            }

            return result;
        }

        private static SerializedProperty Field(CharacterPrefab prefab, string name)
        {
            SerializedProperty property = prefab.RunnerSerialized.FindProperty(name);
            Assert.IsNotNull(property,
                $"[{prefab.Path}] CharacterPipelineRunner 找不到序列化欄位 '{name}'。\n" +
                "欄位可能已改名——本測試與 Runner 的欄位名稱綁定，改名時要一起更新（這是刻意的：改名會清空既有 prefab 接線）。");
            return property;
        }

        /// <summary>
        /// 依 <c>Awake</c> 的規則解析一個 seam：明確指派優先，其次同物件 <c>GetComponent</c>。
        /// 回傳 null ＝ 執行期會拿到 null（Runner 會 <c>LogError</c>，但那要等到 Play）。
        /// </summary>
        private static T ResolveSeam<T>(CharacterPrefab prefab, string fieldName) where T : class
        {
            var assigned = Field(prefab, fieldName).objectReferenceValue as Component;
            if (assigned is T typed) return typed;

            return prefab.Host.GetComponent<T>();
        }

        // =====================================================================
        // W0 — 探索本身要有守衛
        // =====================================================================

        /// <summary>
        /// 若 prefab 被搬走／改名，其他測試會因為「掃不到任何角色」而**全部靜默通過**。
        /// 這條是防止整個檔案退化成空轉的守衛。
        /// </summary>
        [Test]
        public void W0_CharacterPrefabs_AreDiscoverable()
        {
            List<CharacterPrefab> prefabs = LoadCharacterPrefabs();

            Assert.IsNotEmpty(prefabs,
                $"在 {PrefabSearchRoot} 底下找不到任何帶 CharacterPipelineRunner 的 prefab。\n" +
                "若角色 prefab 已搬移，請更新 PrefabSearchRoot——否則本檔所有接線檢查都會靜默通過。");
        }

        // =====================================================================
        // W1 — 核心驅動 seam 必須解析得出來
        // =====================================================================

        /// <summary>
        /// 取代 M3 的「角色 Root 掛 PlayerLocomotionPolicy ＋ LocomotionModel」那一段人工檢查。
        /// ⚠️ <c>IInputSource</c> 刻意**不在**必要清單內——dev-spec §2.1 順序 1 明定它是
        /// 可選角色能力（AI 角色沒有輸入源，由順序 2.5 的 producer 直接產生 domain intent）。
        /// </summary>
        [Test]
        public void W1_CoreDrivingSeams_Resolve()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                if (ResolveSeam<IMovementIntentSource>(prefab, "movementIntentSourceComponent") == null)
                {
                    violations.Add(
                        $"{prefab.Path} → CharacterPipelineRunner：解析不到 IMovementIntentSource\n" +
                        $"    contract : 管線順序 2.5 的唯一 MovementIntent 寫入者（ADR-003 D2）\n" +
                        $"    expected : 'movementIntentSourceComponent' 指派一個實作，或在 '{prefab.Host.name}' 上掛 PlayerLocomotionPolicy／AIMovementSource\n" +
                        $"    actual   : 欄位為 None 且同物件上沒有任何實作\n" +
                        $"    症狀     : MovementIntent 恆為 0，角色完全不會移動");
                }

                if (ResolveSeam<IMovementModel>(prefab, "movementModelComponent") == null)
                {
                    violations.Add(
                        $"{prefab.Path} → CharacterPipelineRunner：解析不到 IMovementModel\n" +
                        $"    contract : 管線順序 3 的 Movement Output 唯一發布者（ADR-003 D3／D4）\n" +
                        $"    expected : 'movementModelComponent' 指派一個實作，或在 '{prefab.Host.name}' 上掛 LocomotionModel\n" +
                        $"    actual   : 欄位為 None 且同物件上沒有任何實作\n" +
                        $"    症狀     : 無運動輸出，角色不會移動也不會進 Move 狀態");
                }

                if (ResolveSeam<AnimationFacadeBase>(prefab, "animationFacade") == null)
                {
                    violations.Add(
                        $"{prefab.Path} → CharacterPipelineRunner：解析不到 AnimationFacadeBase\n" +
                        $"    contract : 管線順序 5 的動畫播放唯一出口（dev-spec §3.1）\n" +
                        $"    expected : 'animationFacade' 指派，或在 '{prefab.Host.name}' 上掛 AnimancerFacade\n" +
                        $"    actual   : 兩者皆無");
                }

                if (ResolveSeam<MotionDriver>(prefab, "motionDriver") == null)
                {
                    violations.Add(
                        $"{prefab.Path} → CharacterPipelineRunner：解析不到 MotionDriver\n" +
                        $"    contract : 管線順序 6 的位移唯一出口（dev-spec §2.1）\n" +
                        $"    expected : 'motionDriver' 指派，或在 '{prefab.Host.name}' 上掛 MotionDriver\n" +
                        $"    actual   : 兩者皆無");
                }

                if (Field(prefab, "stateMachineConfig").objectReferenceValue == null)
                {
                    violations.Add(
                        $"{prefab.Path} → CharacterPipelineRunner：'stateMachineConfig' 未指派\n" +
                        $"    contract : FSM 的轉移／優先權／Action 註冊表來源（dev-spec §3.2）\n" +
                        $"    expected : 一份 StateMachineConfigSO\n" +
                        $"    actual   : None\n" +
                        $"    症狀     : Runner 的 Start 會直接放棄組裝，整條角色管線停擺");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "角色 prefab 的核心驅動 seam 解析失敗（Verification Ladder L1）：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W2 — 每個角色恰好一顆 active producer（dev-spec §7.1-A5 的 prefab 那一半）
        // =====================================================================

        /// <summary>
        /// A5 的條文寫著「Prefab 必須只配置一顆 active producer」，但 A5 的實作只掃原始碼，
        /// **從來沒有真的檢查過 prefab**。這條補上那一半。
        /// 兩顆 producer 同時存在時，<c>GetComponent</c> 的回傳順序決定誰勝出＝單一寫入者靠運氣成立。
        /// </summary>
        [Test]
        public void W2_ExactlyOneActiveMovementIntentSource_PerCharacter()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var sources = new List<IMovementIntentSource>();
                prefab.Host.GetComponents(sources);

                if (sources.Count > 1)
                {
                    var names = new List<string>();
                    for (int i = 0; i < sources.Count; i++) names.Add(sources[i].GetType().Name);

                    violations.Add(
                        $"{prefab.Path} → '{prefab.Host.name}' 上有 {sources.Count} 顆 IMovementIntentSource\n" +
                        $"    contract : MovementIntent 單一寫入者（dev-spec §7.1-A5）\n" +
                        $"    expected : 恰好 1 顆 active producer\n" +
                        $"    actual   : {string.Join("、", names)}\n" +
                        $"    症狀     : 誰勝出取決於 GetComponent 的回傳順序＝單一寫入者靠運氣成立");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "偵測到多重 Movement 意圖 producer：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W3 — Action Sink Bindings 的結構完整性
        // =====================================================================

        /// <summary>
        /// 取代 WORKLOG 交辦裡那三行人工檢查（「Slot 別填錯、別重複、別拖到沒實作介面的元件」）。
        /// ⚠️ enum 一律讀 <c>intValue</c> 而非 <c>enumValueIndex</c>——
        /// <see cref="ActionSlot"/> 的數值刻意不連續（1／2／3／100），
        /// <c>enumValueIndex</c> 取的是「第幾個成員」，用它會把 Reaction(100) 讀成 4。
        /// </summary>
        [Test]
        public void W3_ActionSinkBindings_AreWellFormed()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                SerializedProperty bindings = Field(prefab, "actionSinkBindings");
                var seen = new HashSet<int>();

                for (int i = 0; i < bindings.arraySize; i++)
                {
                    SerializedProperty element = bindings.GetArrayElementAtIndex(i);
                    int slotValue = element.FindPropertyRelative("Slot").intValue;
                    Object sink = element.FindPropertyRelative("Sink").objectReferenceValue;

                    if (slotValue == (int)ActionSlot.None)
                    {
                        violations.Add(
                            $"{prefab.Path} → Action Sink Bindings #{i}：Slot 是 None\n" +
                            $"    contract : None ＝『這一幀沒有人要出手』，不是一個可綁定的身分（ActionSlot 註解）\n" +
                            $"    expected : Slot1／Slot2／Slot3／Reaction\n" +
                            $"    actual   : None(0)");
                    }

                    if (!seen.Add(slotValue))
                    {
                        violations.Add(
                            $"{prefab.Path} → Action Sink Bindings #{i}：Slot {(ActionSlot)slotValue} 重複綁定\n" +
                            $"    contract : 每個 slot 至多一顆 sink（ResolveActionLifecycleSinks 會忽略後者並 LogError）\n" +
                            $"    expected : 每個 slot 出現一次\n" +
                            $"    actual   : 出現第二次");
                    }

                    if (sink == null)
                    {
                        violations.Add(
                            $"{prefab.Path} → Action Sink Bindings #{i}（Slot {(ActionSlot)slotValue}）：Sink 為 None\n" +
                            $"    contract : 每一筆 binding 都要指向一顆 IActionLifecycleSink\n" +
                            $"    expected : 一個實作 IActionLifecycleSink 的元件\n" +
                            $"    actual   : 空欄位");
                    }
                    else if (sink is not IActionLifecycleSink)
                    {
                        violations.Add(
                            $"{prefab.Path} → Action Sink Bindings #{i}（Slot {(ActionSlot)slotValue}）：" +
                            $"'{sink.GetType().Name}' 沒有實作 IActionLifecycleSink\n" +
                            $"    contract : Action 生命週期 seam（ADR-004 D2）\n" +
                            $"    expected : MeleeHitboxSink／ThrowProjectileEmitter 之類的實作\n" +
                            $"    actual   : {sink.GetType().Name}\n" +
                            $"    症狀     : Runner 在 Awake LogError，該 slot 出手時不會有任何世界效果");
                    }
                }
            }

            CollectionAssert.IsEmpty(violations,
                "Action Sink Bindings 結構違規：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W4 — 已註冊的 Action slot 必須有 sink（關聯式契約）
        // =====================================================================

        /// <summary>
        /// **本檔最有價值的一條。** 它把「config 註冊了什麼」與「Runner 綁了什麼」對起來，
        /// 因此不需要寫死任何 slot 清單，也不會斷言一個還沒發生的未來。
        ///
        /// ⚠️ 只檢查**需要世界效果**的 slot：一個 Action 可以只播動畫（例如受擊 Reaction），
        /// 那種 definition 沒有 sink 是合法的。判準取自 definition 自己的
        /// <c>EmitsRelease</c>——會發 Release 就代表它預期有人接。
        ///
        /// ⚠️ sink 解析必須複製 <c>ResolveActionLifecycleSinks</c> 的 all-or-nothing 語意：
        /// 新清單只要有任何一筆，legacy 單顆欄位就**完全**被忽略。
        /// 這條語意正是最容易在 Inspector 上踩到的陷阱——填了 Slot1 之後，
        /// 原本靠 legacy 欄位運作的其他 slot 會**靜默失效**。
        /// </summary>
        [Test]
        public void W4_RegisteredActionSlots_HaveResolvableSink()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var config = Field(prefab, "stateMachineConfig").objectReferenceValue as StateMachineConfigSO;
                if (config == null) continue; // W1 已經報過

                var configSerialized = new SerializedObject(config);
                SerializedProperty definitions = configSerialized.FindProperty("actionDefinitions");
                if (definitions == null || definitions.arraySize == 0) continue;

                // Runner 這一側：複製 ResolveActionLifecycleSinks 的 all-or-nothing 規則
                SerializedProperty bindings = Field(prefab, "actionSinkBindings");
                bool usesBindingList = bindings.arraySize > 0;

                var boundSlots = new HashSet<int>();
                for (int i = 0; i < bindings.arraySize; i++)
                {
                    SerializedProperty element = bindings.GetArrayElementAtIndex(i);
                    if (element.FindPropertyRelative("Sink").objectReferenceValue is IActionLifecycleSink)
                    {
                        boundSlots.Add(element.FindPropertyRelative("Slot").intValue);
                    }
                }

                bool hasLegacySink =
                    Field(prefab, "actionReleaseSinkComponent").objectReferenceValue is IActionLifecycleSink
                    || prefab.Host.GetComponent<IActionLifecycleSink>() != null;

                for (int i = 0; i < definitions.arraySize; i++)
                {
                    var definition = definitions.GetArrayElementAtIndex(i).objectReferenceValue as ActionDefinitionSO;
                    if (definition == null) continue;

                    if (!DefinitionExpectsSink(definition)) continue;

                    bool resolved = usesBindingList
                        ? boundSlots.Contains((int)definition.Slot)
                        : hasLegacySink;

                    if (resolved) continue;

                    violations.Add(
                        $"{prefab.Path} → Action '{definition.name}'（Slot {definition.Slot}）解析不到 sink\n" +
                        $"    contract : 有 EmitsRelease 的 phase ⇒ 該 slot 必須有 IActionLifecycleSink（ADR-004 D2）\n" +
                        $"    expected : {(usesBindingList ? $"Action Sink Bindings 補一筆 Slot={definition.Slot}" : "指派 legacy sink 欄位，或在角色上掛一顆 IActionLifecycleSink")}\n" +
                        $"    actual   : {(usesBindingList ? $"清單有 {bindings.arraySize} 筆但不含 {definition.Slot}（⚠️ 清單非空 ⇒ legacy 欄位被完全忽略）" : "legacy 欄位為空且同物件上沒有實作")}\n" +
                        $"    症狀     : 該技能會正常播動畫並走完 phase，但**不會產生任何世界效果**（不生投射物／不開命中窗）");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "已註冊的 Action slot 缺少 sink（Verification Ladder L1）：\n\n" + string.Join("\n\n", violations));
        }

        /// <summary>只播動畫的 Action（例如受擊 Reaction）不需要 sink；會發 Release 的才需要。</summary>
        private static bool DefinitionExpectsSink(ActionDefinitionSO definition)
        {
            ActionPhaseEntry[] phases = definition.Phases;
            if (phases == null) return false;

            for (int i = 0; i < phases.Length; i++)
            {
                if (phases[i].EmitsRelease) return true;
            }
            return false;
        }

        // =====================================================================
        // W5 — 玩家角色的輸入必須是可封鎖的
        // =====================================================================

        /// <summary>
        /// 取代 M3 的「沒拖 External Arbiter Sources ＝ 暫停中按跳躍會卡在 JumpState」那一條。
        ///
        /// ⚠️ **刻意斷言「結果」而不是「位置」**：dev-spec §7.2-M3 寫的是
        /// 「在場景中另建一顆物件掛 GamePauseController，拖進 Runner 的 External Arbiter Sources」，
        /// 但目前 X Bot 是把 GamePauseController 直接掛在角色 Root 上 —— 那樣
        /// <c>Start</c> 的 <c>GetComponentsInChildren&lt;IArbiterSource&gt;</c> 會自己找到它，
        /// 功能同樣成立。兩種接法都能通過本測試。
        /// 📌 「該掛在哪」是 design-doc §4.9 的架構問題（全域狀態不屬於角色），
        /// **不在本測試的裁決範圍**——已列入 WORKLOG 待使用者裁決。
        ///
        /// ⚠️ 只對**有輸入源**的角色生效：AI 角色沒有 IInputSource，本來就沒有輸入要封鎖。
        /// </summary>
        [Test]
        public void W5_PlayerCharacters_HaveReachableArbiterSource()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                bool isPlayerControlled = ResolveSeam<IInputSource>(prefab, "inputSourceComponent") != null;
                if (!isPlayerControlled) continue;

                var inHierarchy = new List<IArbiterSource>();
                prefab.Root.GetComponentsInChildren(true, inHierarchy);

                int externalCount = 0;
                SerializedProperty external = Field(prefab, "externalArbiterSources");
                for (int i = 0; i < external.arraySize; i++)
                {
                    if (external.GetArrayElementAtIndex(i).objectReferenceValue != null) externalCount++;
                }

                if (inHierarchy.Count == 0 && externalCount == 0)
                {
                    violations.Add(
                        $"{prefab.Path} → 玩家角色沒有任何可達的 IArbiterSource\n" +
                        $"    contract : 管線順序 4.5 的 Arbitration 必須有來源，否則 BlockInput 恆 false（dev-spec §2.1）\n" +
                        $"    expected : 角色階層內掛一顆 IArbiterSource，或 'externalArbiterSources' 拖入場景中的來源\n" +
                        $"    actual   : 階層內 0 顆、外部引用 0 筆\n" +
                        $"    症狀     : 暫停中按跳躍會真的切進 JumpState 並卡住，解除暫停後才起跳（dev-spec §7.3 已記錄的回歸）");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "玩家角色缺少仲裁來源：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W6 — CursorModeController 的必要引用
        // =====================================================================

        /// <summary>
        /// 取代 M3 的「這顆缺席時開場游標不會被鎖住、連帶相機不會轉」那一條。
        /// 只在該元件存在時檢查——它不是每個 prefab 都該有。
        /// </summary>
        [Test]
        public void W6_CursorModeController_HasRequiredReferences()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                foreach (CursorModeController controller in prefab.Root.GetComponentsInChildren<CursorModeController>(true))
                {
                    var serialized = new SerializedObject(controller);

                    AssertReference(violations, prefab.Path, controller, serialized, "uiModeSource",
                        "UI 模式的自由游標請求來源");
                    AssertReference(violations, prefab.Path, controller, serialized, "pauseController",
                        "暫停期間的自由游標請求來源");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "CursorModeController 引用未接（Verification Ladder L1）：\n\n" + string.Join("\n\n", violations));
        }

        private static void AssertReference(
            List<string> violations, string prefabPath, Object owner,
            SerializedObject serialized, string fieldName, string role)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                violations.Add(
                    $"{prefabPath} → {owner.GetType().Name}：找不到序列化欄位 '{fieldName}'（欄位可能已改名）");
                return;
            }

            if (property.objectReferenceValue != null) return;

            violations.Add(
                $"{prefabPath} → {owner.GetType().Name}.'{fieldName}' 未指派\n" +
                $"    contract : {role}（design-doc §4.9：Cursor API 的唯一擁有者需要 OR 合併所有來源）\n" +
                $"    expected : 一個非空引用\n" +
                $"    actual   : None\n" +
                $"    症狀     : 該來源的自由游標請求永遠不會被看見（例如暫停時游標不會出現）");
        }
    }
}
