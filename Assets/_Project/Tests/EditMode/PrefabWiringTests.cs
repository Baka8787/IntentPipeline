using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Arbitration;
using Project.Core.Combat;
using Project.Core.Facing;
// 註：IMovementModel 雖然放在 Core/Movement/Models/ 資料夾，命名空間仍是 Project.Core.Movement
//     （本專案的資料夾與命名空間刻意不一一對應）。
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Actions;
using Project.Presentation.Animation;
using Project.Core.Survivability;
using Project.Presentation.Equipment;
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

        /// <summary>
        /// ADR-007 D3 的「每個角色恰好一個 facing authority」同時適用玩家與 AI。
        /// Runner 只會從自己的 Host 以 <c>GetComponent</c> 解析，因此掛在子物件也不算完成接線。
        /// </summary>
        [Test]
        public void W11_AllCharacterRoots_HaveExactlyOneCharacterFacingSource()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                CharacterFacingSource[] sources = prefab.Host.GetComponents<CharacterFacingSource>();
                if (sources.Length == 1) continue;

                violations.Add(
                    $"{prefab.Path} → '{prefab.Host.name}' 上有 {sources.Length} 顆 CharacterFacingSource\n" +
                    "    contract : ADR-007 D3：每個角色恰好一個 facing authority\n" +
                    "    expected : 角色 Root 上恰好 1 顆 CharacterFacingSource\n" +
                    $"    actual   : {sources.Length} 顆");
            }

            CollectionAssert.IsEmpty(violations,
                "角色 facing authority 接線不完整：\n" +
                "請在 Y Bot 的角色 Root 加掛 CharacterFacingSource（ADR-007 D3：每個角色恰好一個 facing authority）。\n\n" +
                string.Join("\n\n", violations));
        }

        /// <summary>
        /// 持續 combat-facing 是 actor policy：玩家維持預設 false，敵人才顯式啟用。
        /// 玩家判別沿用本檔既有慣例——看 Runner Host 上是否有
        /// <see cref="PlayerLocomotionPolicy"/>，不把 prefab 名稱變成 gameplay 身分。
        ///
        /// ⚠️ **編號註記（2026-09-11）**：本測項在 `docs/20` §4-B2 原被指名為 W12，
        /// 但 W12 已被 <see cref="W12_PlayerCharacterRoots_HaveExactlyOnePlayerCombatContextSource"/> 佔用，
        /// 故順延為 **W14**（W13 亦已佔用）。比照 `ArchitectureRegressionTests` 的 A24 撞號處置：
        /// **編號是引用鍵，不是排名**——新來者順延，不動既有測項的編號。
        ///
        /// 📌 本測項在 Y Bot prefab 勾選 `usePersistentCombatFacing` **之前是紅的**，那是刻意的：
        /// 它是 `docs/20` §3-B1 那條人工項的機器形式，紅燈就是那張 checklist 還沒做完。
        /// </summary>
        [Test]
        public void W14_PersistentCombatFacing_MatchesActorPolicy()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                bool isPlayer = prefab.Host.GetComponent<PlayerLocomotionPolicy>() != null;
                bool expected = !isPlayer;
                CharacterFacingSource source = prefab.Host.GetComponent<CharacterFacingSource>();

                if (source == null)
                {
                    violations.Add(
                        $"{prefab.Path} → Runner Host '{prefab.Host.name}' 找不到 CharacterFacingSource\n" +
                        "    contract : usePersistentCombatFacing 是每角色的 facing actor policy；玩家 false、敵人 true\n" +
                        $"    expected : {(expected ? "true（敵人）" : "false（玩家）")}\n" +
                        "    actual   : CharacterFacingSource 缺席，無法讀取");
                    continue;
                }

                var serialized = new SerializedObject(source);
                SerializedProperty property = serialized.FindProperty("usePersistentCombatFacing");
                if (property == null)
                {
                    violations.Add(
                        $"{prefab.Path} → Runner Host '{prefab.Host.name}' 的 CharacterFacingSource 找不到序列化欄位\n" +
                        "    contract : usePersistentCombatFacing 是每角色的 facing actor policy；玩家 false、敵人 true\n" +
                        $"    expected : {(expected ? "true（敵人）" : "false（玩家）")}\n" +
                        "    actual   : SerializedObject.FindProperty 回傳 null");
                    continue;
                }

                if (property.boolValue == expected) continue;

                violations.Add(
                    $"{prefab.Path} → Runner Host '{prefab.Host.name}' 的 CharacterFacingSource\n" +
                    "    contract : usePersistentCombatFacing 是每角色的 facing actor policy；玩家 false、敵人 true\n" +
                    $"    expected : {(expected ? "true（敵人）" : "false（玩家）")}\n" +
                    $"    actual   : {(property.boolValue ? "true" : "false")}");
            }

            CollectionAssert.IsEmpty(violations,
                "角色 persistent combat-facing actor policy 接線錯誤：\n\n" +
                string.Join("\n\n", violations));
        }

        /// <summary>
        /// 玩家判別使用 Runner Host 上的 <see cref="PlayerLocomotionPolicy"/>：它是玩家 movement producer，
        /// 敵人沒有，因此不會把 AI 錯當成需要玩家 combat context 的角色。
        ///
        /// 這條刻意檢查「恰好一顆」而非至少一顆。CombatContext 是 facing 與 Action soft-target
        /// 共用的單一真相；重複 producer 與完全缺席都會讓結果失去唯一權威。
        /// </summary>
        [Test]
        public void W12_PlayerCharacterRoots_HaveExactlyOnePlayerCombatContextSource()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                if (prefab.Host.GetComponent<PlayerLocomotionPolicy>() == null) continue;

                PlayerCombatContextSource[] sources =
                    prefab.Host.GetComponents<PlayerCombatContextSource>();
                if (sources.Length == 1) continue;

                violations.Add(
                    $"{prefab.Path} → Runner Host '{prefab.Host.name}' 上有 {sources.Length} 顆 PlayerCombatContextSource\n" +
                    "    contract : 玩家 CombatContext 必須有恰好一個 producer，否則 InCombat／Target 沒有單一真相\n" +
                    "    expected : 玩家角色 Root 上恰好 1 顆 PlayerCombatContextSource\n" +
                    $"    actual   : {sources.Length} 顆\n" +
                    "    症狀     : 缺席時 CombatContext.InCombat 恆 false，HeadLook 與 Action soft-target 都不會啟動");
            }

            CollectionAssert.IsEmpty(violations,
                "玩家 CombatContext producer 接線不完整：\n" +
                "請在 `X Bot.prefab` 的 Root 加掛 `PlayerCombatContextSource`，並把 `targetMask` 設為 Layer 7 " +
                "（m_Bits: 128）；其餘欄位保留程式預設值。\n\n" +
                string.Join("\n\n", violations));
        }

        // =====================================================================
        // W3 — Action Sink Bindings 的結構完整性
        // =====================================================================

        /// <summary>
        /// 取代 WORKLOG 交辦裡那三行人工檢查（「Slot 別填錯、別重複、別拖到沒實作介面的元件」）。
        /// ⚠️ enum 一律讀 <c>intValue</c> 而非 <c>enumValueIndex</c>——
        /// <see cref="ActionSlot"/> 的數值刻意不連續（1／2／3／100），
        /// <c>enumValueIndex</c> 取的是「第幾個成員」，用它會把 Reaction(100) 讀成 4。
        ///
        /// <para><b>🆕 2026-09-14：重複的定義變了（使用者裁決）</b></para>
        /// 舊規則是「**同一個 slot 只能出現一次**」。現在一個 slot 可以有多顆 sink——
        /// 一個 Action 本來就可能同時有命中判定、武器顯隱、刀光、VFX、音效等彼此獨立的副作用。
        /// ⇒ 本測項改為只禁止「**同一顆 sink 在同一個 slot 註冊兩次**」。
        ///
        /// 為什麼那一條仍要禁：它必定是接線手滑（重複的列不會帶來任何新效果），
        /// 而症狀是**該 sink 的效果在一次 Action 裡發生兩遍**——命中判定跑兩次、傷害變兩倍。
        /// 沒有人會把「這隻敵人好像特別痛」聯想到 Inspector 上多出來的一列。
        ///
        /// 📌 同一顆 sink 綁到**不同** slot 是合法的（例如共用的音效 sink），因此比較的是
        /// 「(slot, sink 實例)」這個組合，不是 slot、也不是 sink。
        /// 執行期的對應守衛由 <c>ActionSinkResolutionTests</c> 守。
        /// </summary>
        [Test]
        public void W3_ActionSinkBindings_AreWellFormed()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                SerializedProperty bindings = Field(prefab, "actionSinkBindings");
                // 比較的是「(slot, sink 實例)」這個組合——不是 slot（一個 slot 可以有多顆），
                // 也不是 sink（同一顆綁到不同 slot 是合法的）。
                var seen = new HashSet<(int Slot, Object Sink)>();

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

                    if (sink != null && !seen.Add((slotValue, sink)))
                    {
                        violations.Add(
                            $"{prefab.Path} → Action Sink Bindings #{i}：" +
                            $"Slot {(ActionSlot)slotValue} 重複註冊了同一顆 sink '{sink.name}' ({sink.GetType().Name})\n" +
                            $"    contract : 一個 slot 可以有多顆**不同的** sink（命中判定／武器顯隱／刀光…），\n" +
                            $"               但同一顆不得註冊兩次（ResolveActionLifecycleSinks 會忽略後者並 LogError）\n" +
                            $"    expected : 移除重複的那一列，或改綁另一顆 sink\n" +
                            $"    症狀     : 該 sink 的效果在一次 Action 裡發生兩遍——命中判定跑兩次、傷害變兩倍");
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
        // W7 — 已註冊 Action 的動畫鍵必須在該角色的 transitionMappings 解析得到
        // =====================================================================

        /// <summary>
        /// 這條抓的是 `docs/11` §4.1 記載的**安靜失敗①**：動畫鍵沒接 ⇒ `IsPlaying` 為 false
        /// ⇒ 動畫不播、位移退回 base movement，但**流程照跑、Console 不報錯**。
        /// 症狀看起來像「烘焙壞掉」或「AI 邏輯錯了」，很容易往錯的方向查。
        ///
        /// 2026-09-04／09-05 這個坑實際踩過兩次（法術四列漏拖、敵人出拳沒接鍵），
        /// 兩次都是人眼比對字串才發現。字串比對正是機器該做的事。
        ///
        /// ⚠️ **連段段落也要檢查**：`ChainSegments` 的鍵不在 `Phases` 裡，
        /// 只掃 `Phases` 會漏掉第 2、3 段——那正是「動畫只播第一段」的成因。
        /// </summary>
        [Test]
        public void W7_RegisteredActionAnimationKeys_ResolveInTransitionMappings()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var config = Field(prefab, "stateMachineConfig").objectReferenceValue as StateMachineConfigSO;
                if (config == null) continue; // W1 已經報過

                var facade = ResolveSeam<AnimancerFacade>(prefab, "animationFacade");
                if (facade == null) continue; // W1 已經報過

                HashSet<string> mappedKeys = CollectMappedAnimationKeys(facade);

                var configSerialized = new SerializedObject(config);
                SerializedProperty definitions = configSerialized.FindProperty("actionDefinitions");
                if (definitions == null) continue;

                for (int i = 0; i < definitions.arraySize; i++)
                {
                    var definition = definitions.GetArrayElementAtIndex(i).objectReferenceValue as ActionDefinitionSO;
                    if (definition == null) continue;

                    CollectUnmappedKeys(prefab, definition, definition.Phases, "Phases", mappedKeys, violations);
                    CollectUnmappedKeys(
                        prefab, definition, definition.ChainSegments, "ChainSegments", mappedKeys, violations);
                }
            }

            CollectionAssert.IsEmpty(violations,
                "已註冊 Action 的動畫鍵在 transitionMappings 找不到（Verification Ladder L1）：\n\n" +
                string.Join("\n\n", violations));
        }

        private static HashSet<string> CollectMappedAnimationKeys(AnimancerFacade facade)
        {
            var keys = new HashSet<string>();
            var serialized = new SerializedObject(facade);
            SerializedProperty mappings = serialized.FindProperty("transitionMappings");
            if (mappings == null) return keys;

            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty element = mappings.GetArrayElementAtIndex(i);

                // 只有 Transition 也指派了才算「接上」——鍵填了但資產留空，症狀與沒填一模一樣。
                if (element.FindPropertyRelative("Transition").objectReferenceValue == null) continue;

                string key = element.FindPropertyRelative("StateKey").stringValue;
                if (!string.IsNullOrEmpty(key)) keys.Add(key);
            }

            return keys;
        }

        private static void CollectUnmappedKeys(
            CharacterPrefab prefab,
            ActionDefinitionSO definition,
            ActionPhaseEntry[] entries,
            string sourceLabel,
            HashSet<string> mappedKeys,
            List<string> violations)
        {
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                string key = entries[i].AnimationKey;
                if (string.IsNullOrEmpty(key) || mappedKeys.Contains(key)) continue;

                violations.Add(
                    $"{prefab.Path} → '{definition.name}' 的 {sourceLabel}[{i}] 動畫鍵 '{key}' 沒有對應的 transition mapping\n" +
                    $"    contract : 每個 authored AnimationKey 都要在該角色的 AnimancerFacade.transitionMappings 有一列\n" +
                    $"    expected : 補一列 StateKey='{key}' 並指派 TransitionAsset\n" +
                    $"    症狀     : 該段**動畫不會播、但 Action 流程照跑完**，且 Console 不會有任何錯誤");
            }
        }

        // =====================================================================
        // W8 — 遷移到 actionDefinitions 之後，不得留著失效的 Action paramsMapping
        // =====================================================================

        /// <summary>
        /// `StateMachineConfigSO.BuildActionSlotMap` 的相容退路是 **all-or-nothing**：
        /// `actionDefinitions` 一旦非空，`paramsMappings` 裡綁在 `StateType.Action` 上的那一份
        /// **完全不再被解析**。
        ///
        /// 留著它不會報錯，但它會變成一份**看起來還在生效、其實是死的**接線——
        /// 下一個人（或三個月後的自己）看到 Inspector 有那一列，會合理推論 Damage 還走那條路。
        /// Throw 退場（`docs/11` §5.1）與敵人 Damage 遷移（2026-09-05）都是被這條語意咬過的。
        ///
        /// ⇒ 遷移必須**做完**：搬進 `actionDefinitions`，並把死掉的那一列移除。
        /// </summary>
        [Test]
        public void W8_MigratedConfigs_DoNotKeepDeadActionParamsMapping()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var config = Field(prefab, "stateMachineConfig").objectReferenceValue as StateMachineConfigSO;
                if (config == null) continue;

                var configSerialized = new SerializedObject(config);
                SerializedProperty definitions = configSerialized.FindProperty("actionDefinitions");
                if (definitions == null || definitions.arraySize == 0) continue; // 還沒遷移，相容路徑仍合法

                SerializedProperty paramsMappings = configSerialized.FindProperty("paramsMappings");
                if (paramsMappings == null) continue;

                for (int i = 0; i < paramsMappings.arraySize; i++)
                {
                    SerializedProperty element = paramsMappings.GetArrayElementAtIndex(i);
                    if (element.FindPropertyRelative("State").intValue != (int)StateType.Action) continue;

                    violations.Add(
                        $"{prefab.Path} → {config.name} 同時有 actionDefinitions（{definitions.arraySize} 筆）" +
                        $"與 paramsMappings[{i}] 的 Action 綁定\n" +
                        $"    contract : actionDefinitions 非空 ⇒ paramsMappings 的 Action 退路完全不被解析\n" +
                        $"    expected : 該 Definition 搬進 actionDefinitions 後，移除這一列\n" +
                        $"    症狀     : 它看起來還在生效、其實是死接線；讀 Inspector 的人會被誤導");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "遷移未做完（Verification Ladder L1）：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W9 — 真實 config 資產必須解析得出它自己註冊的每一個身分
        // =====================================================================

        /// <summary>
        /// 比 W7／W8 高一階：不只看欄位填了什麼，而是**跑一次 `Initialize()`**，
        /// 斷言 `GetActionDefinition(slot)` 真的拿得回同一份資產。
        ///
        /// 這條直接守住 2026-09-05 敵人遷移的三個要求：
        /// Punch 進得了 `Slot1`、Damage 仍解析得到 `Reaction`、而且**兩者都不再經過 `paramsMappings` 退路**
        /// （退路在 `actionDefinitions` 非空時本來就不會被走，因此這裡拿得到 ＝ 新路徑成立）。
        ///
        /// 也順帶守住身分唯一性：兩份 Definition 搶同一個 `Slot` 時，
        /// `BuildActionSlotMap` 只會保留先到的那份並在 Editor 記一筆 LogError——
        /// 那種「有註冊卻拿不回來」正是這條會抓到的形狀。
        /// </summary>
        [Test]
        public void W9_ActionDefinitions_ResolveByTheirOwnSlot()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var config = Field(prefab, "stateMachineConfig").objectReferenceValue as StateMachineConfigSO;
                if (config == null) continue;

                var configSerialized = new SerializedObject(config);
                SerializedProperty definitions = configSerialized.FindProperty("actionDefinitions");
                if (definitions == null || definitions.arraySize == 0) continue;

                config.Initialize();

                for (int i = 0; i < definitions.arraySize; i++)
                {
                    var definition = definitions.GetArrayElementAtIndex(i).objectReferenceValue as ActionDefinitionSO;
                    if (definition == null)
                    {
                        violations.Add($"{prefab.Path} → {config.name}.actionDefinitions[{i}] 是空引用");
                        continue;
                    }

                    if (definition.Slot == ActionSlot.None)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{definition.name}' 的 Slot 是 None ⇒ 永遠不會被索引，等同沒註冊");
                        continue;
                    }

                    ActionDefinitionSO resolved = config.GetActionDefinition(definition.Slot);
                    if (resolved == definition) continue;

                    violations.Add(
                        $"{prefab.Path} → '{definition.name}'（Slot {definition.Slot}）註冊了卻解析不回自己\n" +
                        $"    actual   : {(resolved == null ? "null" : resolved.name)}\n" +
                        $"    最可能的原因 : 同一個 Slot 被兩份 Definition 佔用（身分必須唯一，ADR-005 D1）");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "config 註冊的身分解析不回來（Verification Ladder L1）：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W10 — 攻擊圈與接戰帶必須是同一個距離
        // =====================================================================

        /// <summary>
        /// 敵人的「想出手距離」與「想站在哪」是**兩個元件各自的欄位**，
        /// 沒有任何程式強制它們一致——而它們不一致時的症狀非常難猜：
        ///
        /// <list type="bullet">
        /// <item><c>attackRange &gt; maximumEngagementDistance</c> ⇒ 還在 Approach 就開始出拳，
        /// 一邊滑步一邊揮拳、動畫在 Move／Punch 之間反覆跳。**2026-09-05 的「敵人亂動」就是這個。**</item>
        /// <item><c>attackRange &lt; maximumEngagementDistance</c> ⇒ 兩者之間是一段
        /// **「站得住但打不到」的死區**：敵人判定「距離剛好」而停止前進並側移，
        /// 但這個距離送不出攻擊 request ⇒ 它在原地繞圈，永遠不出手。
        /// **2026-09-14 使用者 Play 回報的「敵人攻擊距離比迂迴距離短，玩家得自己往前靠」就是這個**
        /// （當時 `attackRange 1.8 &lt; maximumEngagementDistance 2.0`）。</item>
        /// <item><c>attackRange &lt; minimumEngagementDistance</c> ⇒ 進到能打的距離前就先 Retreat，
        /// **敵人永遠不會出手**（上一條的極端版本）。</item>
        /// </list>
        ///
        /// <para><b>⇒ 唯一同時避開前兩條的解是相等</b></para>
        /// 這不是把標準訂得過嚴，而是兩個必要條件交集後的唯一解：
        /// `maximumEngagementDistance` 是敵人**會停下來不再前進的最遠距離**
        /// （由 <c>CombatContextTests.TC11</c> 以純函數證明它是緊上界），
        /// 而 `attackRange` 是**願意出手的最遠距離**。「會停下來的地方就要願意出手」
        /// 與「願意出手的地方都已經停下來」同時成立 ⇒ 兩者必須是同一個數字。
        /// 第三條（≥ minimum）因此自動成立，仍保留為獨立訊息，因為它的症狀不同。
        ///
        /// <para><b>殘留重疊是 hysteresis 帶來的，無法消除</b></para>
        /// 即使相等，敵人在最後 <c>distanceHysteresis</c> 那一段（<c>maximum − deadZone</c> 到
        /// <c>maximum</c>）仍是 Approach 且已在攻擊圈內 ⇒ 會「邊走邊揮」一小段。
        /// 要完全消除就得讓 `attackRange ≤ maximum − deadZone`，但那**又會製造死區**。
        /// ⚠️ 兩害相權：死區是 gameplay 死局（敵人不打人），邊走邊揮只是表現瑕疵
        /// ⇒ 本專案選擇容忍後者。
        ///
        /// <para><b>這條不管拳頭打不打得到</b></para>
        /// 真正的物理觸及範圍在 <c>MeleeHitboxSink</c> 的 collider 幾何上，是另一個層次的問題。
        /// 本條只保證**決策層不自相矛盾**。
        ///
        /// ⚠️ 這條是**關聯式**的：不寫死任何數值，只斷言兩個元件的數值互相自洽。
        /// </summary>
        [Test]
        public void W10_AttackRange_MatchesTheHoldBandUpperBound()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var attackSource = prefab.Host.GetComponent<AIInputSource>();
                var movementSource = prefab.Host.GetComponent<AIMovementSource>();
                if (attackSource == null || movementSource == null) continue; // 玩家沒有這兩顆，跳過

                float attackRange = SerializedFloat(attackSource, "attackRange");
                float minimum = SerializedFloat(movementSource, "minimumEngagementDistance");
                float maximum = SerializedFloat(movementSource, "maximumEngagementDistance");

                if (attackRange > maximum)
                {
                    violations.Add(
                        $"{prefab.Path} → attackRange {attackRange:0.##} > maximumEngagementDistance {maximum:0.##}\n" +
                        $"    症狀 : 還在 Approach 就出拳 ⇒ 一邊滑步一邊揮拳，動畫在 Move／Punch 之間反覆跳");
                }

                if (attackRange < maximum)
                {
                    violations.Add(
                        $"{prefab.Path} → attackRange {attackRange:0.##} < maximumEngagementDistance {maximum:0.##}\n" +
                        $"    死區 : {attackRange:0.##}–{maximum:0.##} m 之間站得住但打不到\n" +
                        $"    症狀 : 敵人靠近後就在原地繞圈不出手，**沒有任何錯誤訊息**\n" +
                        $"    修法 : 兩個欄位改成同一個數字（以實際 MeleeHitbox 的觸及範圍為準）");
                }

                if (attackRange < minimum)
                {
                    violations.Add(
                        $"{prefab.Path} → attackRange {attackRange:0.##} < minimumEngagementDistance {minimum:0.##}\n" +
                        $"    症狀 : 進到能打的距離前就先 Retreat ⇒ **敵人永遠不會出手**，且不會報錯");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "AI 的攻擊圈與接戰帶不自洽（Verification Ladder L1）：\n\n" + string.Join("\n\n", violations));
        }

        /// <summary>
        /// Enemy baseline 的三組距離各有單一語意：attack range 決定「想不想嘗試出手」、
        /// engagement band 決定戰鬥中怎麼移動、aggro enter/leave 決定是否交戰。
        /// 本測試只守關係，不把 8/12 first-pass tuning 鎖成正式平衡值。
        /// </summary>
        [Test]
        public void W15_EnemyDecisionTargetsAndDistanceLayers_AreCoherent()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var attackSource = prefab.Host.GetComponent<AIInputSource>();
                var movementSource = prefab.Host.GetComponent<AIMovementSource>();
                if (attackSource == null || movementSource == null) continue;

                var attackSerialized = new SerializedObject(attackSource);
                var movementSerialized = new SerializedObject(movementSource);

                Object attackTarget = attackSerialized.FindProperty("target").objectReferenceValue;
                Object movementTarget = movementSerialized.FindProperty("target").objectReferenceValue;
                float attackRange = attackSerialized.FindProperty("attackRange").floatValue;
                float engagementMaximum =
                    movementSerialized.FindProperty("maximumEngagementDistance").floatValue;
                float aggroEnter = movementSerialized.FindProperty("aggroEnterRadius").floatValue;
                float aggroLeave = movementSerialized.FindProperty("aggroLeaveRadius").floatValue;

                if (attackTarget != movementTarget)
                {
                    violations.Add(
                        $"{prefab.Path} → AIInputSource 與 AIMovementSource 指向不同 target\n" +
                        "    contract : awareness、movement、action decision 必須使用同一個已知目標\n" +
                        $"    attack  : {(attackTarget != null ? attackTarget.name : "<none>")}\n" +
                        $"    movement: {(movementTarget != null ? movementTarget.name : "<none>")}");
                }

                if (aggroEnter <= engagementMaximum)
                {
                    violations.Add(
                        $"{prefab.Path} → aggroEnterRadius {aggroEnter:0.##} <= " +
                        $"maximumEngagementDistance {engagementMaximum:0.##}\n" +
                        "    contract : aggro range 決定是否交戰；engagement distance 只決定戰鬥內站位，兩者不得共用同一門檻");
                }

                if (attackRange > aggroEnter)
                {
                    violations.Add(
                        $"{prefab.Path} → attackRange {attackRange:0.##} > aggroEnterRadius {aggroEnter:0.##}\n" +
                        "    contract : Enemy 不得在尚未能進入 Combat 的距離送 Attack request");
                }

                if (aggroLeave < aggroEnter)
                {
                    violations.Add(
                        $"{prefab.Path} → aggroLeaveRadius {aggroLeave:0.##} < aggroEnterRadius {aggroEnter:0.##}\n" +
                        "    contract : leave 必須大於等於 enter，才能形成 engagement hysteresis");
                }
            }

            CollectionAssert.IsEmpty(violations,
                "Enemy Decision 的 target 或距離分層不自洽：\n\n" + string.Join("\n\n", violations));
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
                // 🔴 **2026-09-05 修正判準**：原本寫的是「解析得到 `IInputSource` ⇒ 是玩家角色」。
                //    那個 proxy 只在「玩家是唯一有輸入來源的角色」時碰巧成立——`AIInputSource`
                //    上線的那一刻它就錯了（敵人被誤判成玩家，要求它掛 IArbiterSource）。
                //    **「有輸入來源」不等於「由玩家操作」**；真正的判準是輸入來自輸入裝置，
                //    也就是 PlayerInputSource。這條契約要保護的「暫停中按跳躍會卡住」本來就是玩家的問題。
                bool isPlayerControlled = ResolveSeam<PlayerInputSource>(prefab, "inputSourceComponent") != null;
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

        // =====================================================================
        // W13 — FSM 可請求的每一個 state 動畫鍵，都必須在該角色的 transitionMappings 解析得到
        // =====================================================================

        /// <summary>
        /// W7 守的是 **Action** 的動畫鍵；本條守的是 **state 自己的**動畫鍵。兩者是同一類安靜失敗
        /// （鍵沒接 ⇒ 動畫不播、流程照跑），但來源不同，所以 W7 對 state 鍵的覆蓋率是零。
        ///
        /// 2026-09-10 實際踩到：walk-off falling 讓敵人**第一次**有辦法進入 <c>JumpState</c>
        /// （在此之前 AI 從不設 <c>JumpRequested</c>，<c>CanEnter</c> 永遠 false），於是 Y Bot 開始請求
        /// <c>FallingLoop</c>／<c>JumpIdleLandHard</c>——而它的 facade 一個 Jump 家族鍵都沒有。
        /// 症狀只有兩行 Console 警告 ＋ 動畫維持原樣（<c>AnimancerFacade</c> 查表失敗的退化路徑），
        /// 位移與 FSM 完全正常 ⇒ **EditMode 全綠、Play 也「看起來正常」**。這正是本條要消滅的盲區。
        ///
        /// 📌 <c>FullBodyStateMachine.Initialize</c> 對**每一隻**角色無條件註冊
        /// Idle／Move／Jump／Roll／Action ⇒「這隻角色用不到那個狀態」不是可以不接線的理由。
        /// 只要它 config 綁的 <c>JumpStateParams</c> authored 了某一格，它就可能請求那一格。
        ///
        /// 檢查範圍刻意是「**authored 的格子**」而不是「表裡所有格子」：
        /// <c>JumpState.ResolveAnimationKey</c> 在變體無效時保留現有鍵、不會請求空鍵，
        /// 沒填的格子本來就不會被請求。因此「敵人只給一套精簡的 falling／landing」是合法配置——
        /// 它要接的不是全部 18 格，而是**它自己那張表填了幾格就接幾格**。
        ///
        /// ⚠️ 已知的保守偏差（刻意）：Walk／Run 陣列裡 authored 但缺 <c>FootPhaseCurve</c> 的格子，
        /// 執行期 <c>SelectVariant</c> 其實選不到，本條仍要求接線。寧可多接一列，
        /// 也不要讓「補上曲線之後才發現鍵沒接」變成下一次的 Play-only 發現。
        /// 這裡刻意**不複製** <c>SelectVariant</c> 的可達性判斷——測試重述執行期邏輯只會製造第二份真相。
        /// </summary>
        [Test]
        public void W13_ReachableStateAnimationKeys_ResolveInTransitionMappings()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var config = Field(prefab, "stateMachineConfig").objectReferenceValue as StateMachineConfigSO;
                if (config == null) continue; // W1 已經報過

                var facade = ResolveSeam<AnimancerFacade>(prefab, "animationFacade");
                if (facade == null) continue; // W1 已經報過

                HashSet<string> mappedKeys = CollectMappedAnimationKeys(facade);

                // ① BaseState.AnimationKey ＝ Type.ToString()。這四個狀態一律被註冊、一律可能被請求。
                //    Action 的鍵來自 definition，已由 W7 覆蓋，不在此重複。
                RequireMappedStateKey(prefab, nameof(StateType.Idle), "BaseState.AnimationKey", mappedKeys, violations);
                RequireMappedStateKey(prefab, nameof(StateType.Move), "BaseState.AnimationKey", mappedKeys, violations);
                RequireMappedStateKey(prefab, nameof(StateType.Jump), "BaseState.AnimationKey（相位解析失敗時的保留鍵）", mappedKeys, violations);
                RequireMappedStateKey(prefab, nameof(StateType.Roll), "BaseState.AnimationKey", mappedKeys, violations);

                // ② JumpState 的相位鍵全部來自 config 綁定的 JumpStateParams 變體表。
                var jumpParams = config.GetStateParams<JumpStateParams>(StateType.Jump);
                if (jumpParams == null) continue; // 未綁定 ⇒ JumpState 走硬編碼退化，不會請求變體鍵

                JumpAnimationVariantTable table = jumpParams.AnimationVariants;
                RequireMappedVariant(prefab, jumpParams, table.Falling, "Falling", mappedKeys, violations);
                RequireMappedVariant(prefab, jumpParams, table.HardLand, "HardLand", mappedKeys, violations);
                RequireMappedVariantSet(prefab, jumpParams, table.Start, "Start", mappedKeys, violations);
                RequireMappedVariantSet(prefab, jumpParams, table.NormalLand, "NormalLand", mappedKeys, violations);
                RequireMappedVariantSet(prefab, jumpParams, table.NormalLandToMove, "NormalLandToMove", mappedKeys, violations);
            }

            CollectionAssert.IsEmpty(violations,
                "FSM 可請求的 state 動畫鍵在 transitionMappings 找不到（Verification Ladder L1）：\n\n" +
                string.Join("\n\n", violations));
        }

        private static void RequireMappedStateKey(
            CharacterPrefab prefab,
            string key,
            string sourceLabel,
            HashSet<string> mappedKeys,
            List<string> violations)
        {
            if (mappedKeys.Contains(key)) return;

            violations.Add(
                $"{prefab.Path} → state 動畫鍵 '{key}'（{sourceLabel}）沒有對應的 transition mapping\n" +
                $"    contract : FullBodyStateMachine 對每隻角色都註冊 Idle／Move／Jump／Roll，四個鍵一律要接\n" +
                $"    expected : 補一列 StateKey='{key}' 並指派 TransitionAsset\n" +
                $"    症狀     : 進入該狀態時動畫不換、Console 只有一行警告，FSM 與位移看起來完全正常");
        }

        private static void RequireMappedVariantSet(
            CharacterPrefab prefab,
            JumpStateParams jumpParams,
            JumpAnimationVariantSet set,
            string label,
            HashSet<string> mappedKeys,
            List<string> violations)
        {
            RequireMappedVariant(prefab, jumpParams, set.Idle, label + ".Idle", mappedKeys, violations);
            RequireMappedVariantArray(prefab, jumpParams, set.Walk, label + ".Walk", mappedKeys, violations);
            RequireMappedVariantArray(prefab, jumpParams, set.Run, label + ".Run", mappedKeys, violations);
        }

        private static void RequireMappedVariantArray(
            CharacterPrefab prefab,
            JumpStateParams jumpParams,
            LocomotionStopVariant[] variants,
            string label,
            HashSet<string> mappedKeys,
            List<string> violations)
        {
            if (variants == null) return;

            for (int i = 0; i < variants.Length; i++)
            {
                RequireMappedVariant(prefab, jumpParams, variants[i], $"{label}[{i}]", mappedKeys, violations);
            }
        }

        private static void RequireMappedVariant(
            CharacterPrefab prefab,
            JumpStateParams jumpParams,
            LocomotionStopVariant variant,
            string label,
            HashSet<string> mappedKeys,
            List<string> violations)
        {
            // 沒 authored 的格子執行期不會被請求（ResolveAnimationKey 保留現有鍵），因此不要求接線。
            if (!variant.IsValid) return;

            string key = variant.AnimationKey;
            if (string.IsNullOrEmpty(key) || mappedKeys.Contains(key)) return;

            violations.Add(
                $"{prefab.Path} → '{jumpParams.name}' 的 {label} 動畫鍵 '{key}' 沒有對應的 transition mapping\n" +
                $"    contract : config 綁的 JumpStateParams 只要 authored 了這一格，這隻角色就可能請求它\n" +
                $"    expected : 補一列 StateKey='{key}' 並指派 TransitionAsset，\n" +
                $"               或改綁一份**這隻角色自己的** JumpStateParams（只 author 它真的需要的格子）\n" +
                $"    症狀     : 空中／落地時動畫不換，Console 只有一行 AnimancerFacade 查表失敗警告");
        }

        // =====================================================================
        // W18 / W19 / W20 — Hurt ／ Death 垂直切片的接線（docs/26 §K）
        // =====================================================================

        /// <summary>
        /// **W18 — 每隻角色都要能受傷與死亡。**
        ///
        /// <para>這條守的是一個曾經真實存在、而且完全靜默的缺陷：</para>
        /// 2026-09-14 盤點時發現 **玩家從來沒有過受擊反應**——`DamageDefinition` 只掛在敵人 config，
        /// `X Bot` 的 37 筆 transition mapping 裡也沒有任何受擊動畫。
        /// 而失敗模式是「什麼都沒發生」：沒有例外、沒有紅字，只有「被打好像沒反應」。
        ///
        /// 缺 <c>CharacterHealth</c> 的角色 <c>Survivability</c> 恆為 <c>default</c>
        /// ⇒ 永遠不會進 Hurt／Death，而且同樣安靜。
        /// </summary>
        [Test]
        public void W18_CharacterPrefabs_CanTakeDamageAndDie()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                if (prefab.Host.GetComponent<Project.Core.Survivability.CharacterHealth>() == null)
                {
                    violations.Add(
                        $"{prefab.Path} 缺 CharacterHealth\n" +
                        "    contract : 它是 Survivability 的唯一寫入者（ADR-009 D1）；缺席 ⇒ 該角色永遠不會受傷或死亡\n" +
                        "    症狀     : 打得到、扣不了血，且**沒有任何錯誤訊息**");
                }

                if (prefab.Host.GetComponent<Project.Core.Arbitration.Sources.DeathArbiterSource>() == null)
                {
                    violations.Add(
                        $"{prefab.Path} 缺 DeathArbiterSource\n" +
                        "    contract : 死後停止產生 intent 只能走仲裁層——A27 禁止輸入層回讀 gameplay state\n" +
                        "    症狀     : 屍體仍在產生移動／攻擊意圖");
                }
            }

            CollectionAssert.IsEmpty(violations, string.Join("\n", violations));
        }

        /// <summary>
        /// **W19 — Hurt ／ Death 的動畫鍵必須解析得出來。**
        ///
        /// 兩個鍵都是 <c>BaseState.AnimationKey</c> 的預設（<c>Type.ToString()</c>），
        /// 不經任何 authored Definition ⇒ **沒有第二個地方會抱怨接線漏掉**。
        /// <c>AnimancerFacade.Play</c> 查表失敗只 LogWarning 後 return：
        /// 角色照樣進入 Hurt／Death（狀態權威與動畫播放是解耦的），但**維持上一個姿勢**。
        /// 那正是「看起來像沒壞」的失敗模式。
        /// </summary>
        [Test]
        public void W19_HurtAndDeathAnimationKeys_ResolveInTransitionMappings()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                var facade = prefab.Host.GetComponentInChildren<AnimancerFacade>(true);
                if (facade == null) continue;

                HashSet<string> mappedKeys = MappedStateKeys(facade);
                foreach (string key in new[]
                         {
                             StateType.Hurt.ToString(),
                             StateType.Death.ToString(),
                         })
                {
                    if (mappedKeys.Contains(key)) continue;
                    violations.Add(
                        $"{prefab.Path} 沒有 StateKey='{key}' 的 transition mapping\n" +
                        "    contract : Hurt／Death 的動畫鍵是 StateType 名稱，不經 Definition 資產\n" +
                        "    症狀     : 角色確實進入該狀態（扣血、被鎖住），但維持上一個姿勢");
                }
            }

            CollectionAssert.IsEmpty(violations, string.Join("\n", violations));
        }

        /// <summary>
        /// **W20 — Hurt 的時長必須來自 bake，不是程式常數。**
        ///
        /// <c>HurtState</c> 的 <c>FallbackDuration</c> 只是斷鏈時的安全退化。
        /// 正式資產若沒綁 Hurt 的 bakeMapping，硬直長度就悄悄變成程式裡的數字——
        /// 使用者 2026-09-14 明確要求「不要在 runtime state 裡 hardcode 秒數」。
        ///
        /// ⚠️ **Death 刻意不要求 bake**：它是吸收態、沒有計時器，也不消費 baked 位移
        /// （走 <c>ExecuteVerticalOnlyMovement</c>）⇒ 為它建一份 bake 只會多一個沒人讀的資產。
        /// </summary>
        [Test]
        public void W20_HurtDuration_ComesFromBakeMapping_AndDeathNeedsNone()
        {
            var violations = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:StateMachineConfigSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<StateMachineConfigSO>(path);
                if (config == null) continue;
                config.Initialize();

                MotionBakeData hurtBake = config.GetBakeData(StateType.Hurt);
                if (hurtBake == null)
                {
                    violations.Add($"{path} 的 bakeMappings 沒有 Hurt ⇒ 硬直長度會退化成 HurtState 的程式常數");
                }
                else if (!(hurtBake.Duration > 0f))
                {
                    violations.Add($"{path} 的 Hurt bake（{hurtBake.name}）Duration = {hurtBake.Duration}；請重跑烘焙");
                }
            }

            CollectionAssert.IsEmpty(violations, string.Join("\n", violations));
        }

        // =====================================================================
        // W25 — 世界空間血條接線必須完整（2026-09-15）
        // =====================================================================

        /// <summary>
        /// **W25 — <c>WorldSpaceHealthBar</c> 必須解析得到 Runner、有血條、且 Canvas 是 World Space。**
        ///
        /// <para>三種失敗模式，其中兩種**方向相反地難猜**：</para>
        /// <list type="bullet">
        /// <item><b>解析不到 Runner</b> ⇒ `LateUpdate` 第一行就 return ⇒ 血條**維持 prefab 上的預設**
        /// （編輯時是顯示的、滿血的）⇒ 畫面上是「每隻敵人頭上都掛著一條永遠滿的血條」。
        /// 看起來像「可見性政策沒做」，而不是「沒接到角色」。</item>
        /// <item><b>`healthFill` 沒指派</b> ⇒ 顯隱會動、長度不會動 ⇒ 看起來像血量沒在扣。</item>
        /// <item><b>Canvas 不是 World Space</b>（例如誤設成 Overlay）⇒ 血條會**貼在螢幕上**而不是
        /// 跟著敵人 ⇒ 多隻敵人時全部疊在同一個位置。</item>
        /// </list>
        ///
        /// ⚠️ <c>visualRoot</c> 留空**不是**違規：那是刻意的退路（用本物件自己）。
        /// 沒掛這顆元件的角色也不是違規——玩家的血條在螢幕 HUD 上。
        /// </summary>
        [Test]
        public void W25_WorldSpaceHealthBars_AreFullyWired()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                WorldSpaceHealthBar[] bars = prefab.Root.GetComponentsInChildren<WorldSpaceHealthBar>(true);

                foreach (WorldSpaceHealthBar bar in bars)
                {
                    var serialized = new SerializedObject(bar);

                    bool resolvesRunner = serialized.FindProperty("runner").objectReferenceValue != null
                        || bar.GetComponentInParent<CharacterPipelineRunner>(true) != null;
                    if (!resolvesRunner)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{bar.gameObject.name}' 的 WorldSpaceHealthBar 解析不到 Runner\n" +
                            $"    contract : 血量與 CombatContext 都從 Runner 的黑板讀\n" +
                            $"    症狀     : 血條維持 prefab 預設（顯示、滿血）⇒ 看起來像可見性政策沒做");
                    }

                    if (serialized.FindProperty("healthFill").objectReferenceValue == null)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{bar.gameObject.name}' 的 healthFill 未指派\n" +
                            $"    症狀     : 顯隱會動、長度不會動 ⇒ 看起來像血量沒在扣");
                    }

                    var canvas = bar.GetComponent<Canvas>();
                    if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{bar.gameObject.name}' 的 Canvas 不是 World Space" +
                            $"（{(canvas == null ? "沒有 Canvas" : canvas.renderMode.ToString())}）\n" +
                            $"    contract : 頭上血條必須跟著角色在世界裡，billboard 才有意義\n" +
                            $"    症狀     : 血條貼在螢幕上 ⇒ 多隻敵人時全部疊在同一個位置");
                    }
                }
            }

            CollectionAssert.IsEmpty(violations,
                "世界空間血條接線不完整：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W24 — HUD 接線必須完整（2026-09-15）
        // =====================================================================

        /// <summary>
        /// **W24 — <c>PlayerHud</c> 必須解析得到 Runner、有血條、且每一列冷卻 binding 都是完整的。**
        ///
        /// <para>失敗模式全部靜默，而且**方向相反的兩種都很難猜**：</para>
        /// <list type="bullet">
        /// <item><b>解析不到 Runner</b> ⇒ `LateUpdate` 第一行就 return。HUD **畫得出來但永遠不更新**
        /// ——血條停在編輯時的 `fillAmount`（通常是滿的）。看起來像「角色沒受傷」，
        /// 而不是「HUD 沒接上」。</item>
        /// <item><b>`healthFill` 沒指派</b> ⇒ 冷卻會動、血條不會動。最容易被當成血量系統壞了。</item>
        /// <item><b>binding 的 Slot 是 <c>None</c></b> ⇒ 查詢恆回 0 ⇒ 那一格**永遠亮著**，
        /// 看起來像「這個技能沒有冷卻」。</item>
        /// <item><b>binding 的 Overlay 是空的</b> ⇒ 那一格永遠不畫冷卻，症狀同上。</item>
        /// </list>
        ///
        /// ⚠️ 沒掛 <c>PlayerHud</c> 的角色**不是**違規——敵人沒有 HUD。本條只檢查已經掛上的那些。
        /// ⚠️ 本條**不檢查** <c>Image.type</c> 是否為 <c>Filled</c>：那是視覺設定，
        /// 錯了在 Play 的第一秒就看得出來（整塊蓋住不會動），不屬於「靜默」那一類。
        /// </summary>
        [Test]
        public void W24_PlayerHuds_AreFullyWired()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                PlayerHud[] huds = prefab.Root.GetComponentsInChildren<PlayerHud>(true);

                foreach (PlayerHud hud in huds)
                {
                    var serialized = new SerializedObject(hud);

                    // 解析規則與 runtime 的 ResolveRunner 逐字對齊：欄位優先，留空則往父物件找。
                    bool resolvesRunner = serialized.FindProperty("runner").objectReferenceValue != null
                        || hud.GetComponentInParent<CharacterPipelineRunner>(true) != null;
                    if (!resolvesRunner)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{hud.gameObject.name}' 的 PlayerHud 解析不到 CharacterPipelineRunner\n" +
                            $"    contract : 血量讀黑板、冷卻讀 Runner 的唯讀查詢，兩條路都要先有 Runner\n" +
                            $"    症狀     : HUD 畫得出來但**永遠不更新**，血條停在編輯時的值（通常是滿的）");
                    }

                    if (serialized.FindProperty("healthFill").objectReferenceValue == null)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{hud.gameObject.name}' 的 healthFill 未指派\n" +
                            $"    症狀     : 冷卻會動、血條不會動 ⇒ 最容易被當成血量系統壞了");
                    }

                    SerializedProperty bindings = serialized.FindProperty("cooldownBindings");
                    for (int i = 0; i < bindings.arraySize; i++)
                    {
                        SerializedProperty element = bindings.GetArrayElementAtIndex(i);
                        int slotValue = element.FindPropertyRelative("Slot").intValue;
                        Object overlay = element.FindPropertyRelative("CooldownOverlay").objectReferenceValue;

                        if (slotValue == (int)ActionSlot.None)
                        {
                            violations.Add(
                                $"{prefab.Path} → '{hud.gameObject.name}' 的 cooldownBindings #{i}：Slot 是 None\n" +
                                $"    症狀     : 查詢恆回 0 ⇒ 那一格**永遠亮著**，看起來像這個技能沒有冷卻");
                        }

                        if (overlay == null)
                        {
                            violations.Add(
                                $"{prefab.Path} → '{hud.gameObject.name}' 的 cooldownBindings #{i}" +
                                $"（Slot {(ActionSlot)slotValue}）：CooldownOverlay 是空的\n" +
                                $"    expected : 指派一張 Image（Type 設 Filled）\n" +
                                $"    症狀     : 那一格永遠不畫冷卻，同樣看起來像沒有冷卻");
                        }
                    }
                }
            }

            CollectionAssert.IsEmpty(violations,
                "HUD 接線不完整：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W23 — 重生接線必須完整（2026-09-14）
        // =====================================================================

        /// <summary>
        /// **W23 — <c>RespawnController</c> 必須解析得到 health／motionDriver，且 <c>RespawnAction</c> 要有綁定。**
        ///
        /// <para>三種失敗模式**全部靜默**，而且各自指向錯的懷疑對象：</para>
        /// <list type="bullet">
        /// <item><b>沒綁按鍵</b> ⇒ 按了沒反應。看起來像「重生功能沒做」或「鍵位不對」，
        /// 但真正的原因是 <c>InputAction</c> 上一個 binding 都沒有。</item>
        /// <item><b>解析不到 <c>CharacterHealth</c></b> ⇒ <c>Respawn()</c> 第一行就 return false。
        /// 同樣是「按了沒反應」。</item>
        /// <item><b>解析不到 <c>MotionDriver</c></b> ⇒ **人活了但沒被傳送**——在屍體原地站起來。
        /// 這個最糟：功能看起來「有在動」，只是位置不對，最容易被當成重生點設錯。</item>
        /// </list>
        ///
        /// ⚠️ <c>respawnPoint</c> 留空**不是**違規：那是刻意的退路（退回場景開場姿態，
        /// 見 <c>docs/25</c> §8.6）。沒掛 <c>RespawnController</c> 的角色也不是違規——
        /// 敵人本來就不重生。本條只檢查**已經掛上**的那些。
        /// </summary>
        [Test]
        public void W23_RespawnControllers_AreFullyWired()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                RespawnController[] controllers =
                    prefab.Root.GetComponentsInChildren<RespawnController>(true);

                foreach (RespawnController controller in controllers)
                {
                    // 解析規則與 runtime 的 ResolveDependencies 逐字對齊：欄位優先，留空則往下找。
                    var serialized = new SerializedObject(controller);
                    bool hasHealth = serialized.FindProperty("health").objectReferenceValue != null
                        || controller.GetComponentInChildren<CharacterHealth>(true) != null;
                    bool hasDriver = serialized.FindProperty("motionDriver").objectReferenceValue != null
                        || controller.GetComponentInChildren<MotionDriver>(true) != null;

                    if (!hasHealth)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{controller.gameObject.name}' 的 RespawnController 解析不到 CharacterHealth\n" +
                            $"    contract : Revive() 是 IsDead 的唯一寫入路徑，缺它就沒有任何東西能把角色救回來\n" +
                            $"    症狀     : 按重生鍵完全沒反應，且**沒有任何錯誤訊息**");
                    }

                    if (!hasDriver)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{controller.gameObject.name}' 的 RespawnController 解析不到 MotionDriver\n" +
                            $"    contract : 它是 position 的單一寫入者，傳送只能經過它\n" +
                            $"    症狀     : **人活了但沒被傳送**——在屍體原地站起來，最容易被誤判成重生點設錯");
                    }

                    // ⚠️ 刻意用 SerializedObject 讀 binding 數，**不碰 `InputAction` 型別**——
                    //    那會讓測試組件需要新增一條 Unity.InputSystem 的 asmdef 參考。
                    //    為了數一個陣列長度而擴大測試組件的依賴面並不划算，且本檔本來就是
                    //    「全程 SerializedObject 唯讀存取」的形狀。
                    SerializedProperty bindings =
                        serialized.FindProperty("RespawnAction.m_SingletonActionBindings");
                    if (bindings == null || bindings.arraySize == 0)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{controller.gameObject.name}' 的 RespawnAction 沒有任何 binding\n" +
                            $"    contract : 重生是 system-level 指令，自己持 InputAction（⛔ 不走 InputData，" +
                            $"死亡時 BlockInput 會把整份輸入歸零）\n" +
                            $"    expected : 例如 <Keyboard>/r\n" +
                            $"    症狀     : 按了沒反應，看起來像功能沒做或鍵位不對");
                    }
                }
            }

            CollectionAssert.IsEmpty(violations,
                "重生接線不完整：\n\n" + string.Join("\n\n", violations));
        }

        // =====================================================================
        // W22 — 武器顯隱的 sink 必須真的接上（2026-09-14）
        // =====================================================================

        /// <summary>
        /// **W22 — <c>WeaponVisibilitySink</c> 必須解析得到掛點，而且必須被綁到至少一個 slot。**
        ///
        /// <para>守的是這顆元件**兩種都完全靜默**的失敗模式</para>
        /// <list type="bullet">
        /// <item><b>沒綁 slot</b> ⇒ `Begin`／`Cleanup` 永遠不會被呼叫。但它的 `Awake` 仍會把武器藏起來
        /// ⇒ 畫面上是「**武器從頭到尾都不見**」，看起來像模型或掛點壞了，
        /// 而真正的原因在 Runner 的清單上少一列。</item>
        /// <item><b>解析不到 <c>WeaponSocket</c></b> ⇒ 每個回呼都安靜 return
        /// ⇒ 畫面上是「武器一直都在」，看起來像**功能沒做**。</item>
        /// </list>
        /// 兩個方向都不會有任何錯誤訊息，而且指向錯誤的懷疑對象——正是該寫成測試的形狀。
        ///
        /// <para>⚠️ 沒掛這顆元件**不是**違規</para>
        /// 「武器一直握著」是合法的角色設定（Y Bot 根本沒有武器）。本條只檢查**已經掛上**的那些。
        /// 行為本身由 <c>WeaponSocketTests</c> 驗；本條只驗接線。
        /// </summary>
        [Test]
        public void W22_WeaponVisibilitySinks_AreResolvedAndBoundToASlot()
        {
            var violations = new List<string>();

            foreach (CharacterPrefab prefab in LoadCharacterPrefabs())
            {
                WeaponVisibilitySink[] sinks = prefab.Root.GetComponentsInChildren<WeaponVisibilitySink>(true);
                if (sinks.Length == 0) continue;

                SerializedProperty bindings = Field(prefab, "actionSinkBindings");

                foreach (WeaponVisibilitySink sink in sinks)
                {
                    // 解析規則與 runtime 的 Awake 逐字對齊：欄位優先，留空則同物件與子物件查找。
                    var serialized = new SerializedObject(sink);
                    Object authoredSocket = serialized.FindProperty("weaponSocket").objectReferenceValue;
                    bool resolvesSocket = authoredSocket != null
                        || sink.GetComponentInChildren<WeaponSocket>(true) != null;

                    if (!resolvesSocket)
                    {
                        violations.Add(
                            $"{prefab.Path} → '{sink.gameObject.name}' 上的 WeaponVisibilitySink 解析不到 WeaponSocket\n" +
                            $"    contract : 它只負責「何時顯隱」，實際切換由 WeaponSocket 執行\n" +
                            $"    expected : 指派 Weapon Socket 欄位，或把它掛在有 WeaponSocket 的物件上\n" +
                            $"    症狀     : 每個回呼都安靜 return ⇒ 武器一直都在，看起來像功能沒做");
                    }

                    if (!IsBoundToAnySlot(bindings, sink))
                    {
                        violations.Add(
                            $"{prefab.Path} → '{sink.gameObject.name}' 上的 WeaponVisibilitySink 沒有被綁到任何 slot\n" +
                            $"    contract : 它是 IActionLifecycleSink，時點只能由 Action Sink Bindings 送達\n" +
                            $"    expected : Action Sink Bindings 補一列指向這顆元件（近戰通常是 Slot1）\n" +
                            $"    症狀     : Begin／Cleanup 永遠不會被呼叫，但 Awake 已經把武器藏起來\n" +
                            $"               ⇒ **武器從頭到尾都不見**，看起來像模型或掛點壞了");
                    }
                }
            }

            CollectionAssert.IsEmpty(violations,
                "武器顯隱接線不完整：\n\n" + string.Join("\n\n", violations));
        }

        private static bool IsBoundToAnySlot(SerializedProperty bindings, Object sink)
        {
            for (int i = 0; i < bindings.arraySize; i++)
            {
                SerializedProperty element = bindings.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("Sink").objectReferenceValue == sink) return true;
            }

            return false;
        }

        // 🔄 **2026-09-16：`ProjectileStepPerPhysicsTick_StaysWithinItsOwnRadius` 已移除。**
        //
        // 它斷言 `speed × fixedDeltaTime ≤ radius`，用意是擋 tunneling。但那**不是保證**：
        // 位移發生在 `Update`（`Time.deltaTime`、可變）、取樣發生在物理步，兩者不同步
        // ⇒ 真實取樣間距不等於那個算式，它只能**降低**略過的機率。
        //
        // `ThrownProjectile` 已於 2026-09-16 改為 `FixedUpdate` ＋ `SphereCast` 連續掃掠
        // （使用者裁決，`docs/11` §17）⇒ 安全性改由**掃掠本身**保證，速度不再受這條啟發式限制
        // （火球因此得以回到 20 m/s 的視覺速度）。
        // ⇒ 新契約由 `ProjectileSweepPlayModeTests` 以**行為**驗證：高速不 tunneling、
        //    命中在接觸點截斷、owner 排除、屍體透明。

        /// <summary>唯讀取出序列化的 float；欄位改名時要大聲失敗，不要悄悄回傳 0。</summary>
        private static float SerializedFloat(Component component, string propertyName)
        {
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName);
            Assert.IsNotNull(property,
                $"{component.GetType().Name} 找不到序列化欄位 '{propertyName}'（欄位可能已改名）");
            return property.floatValue;
        }

        private static HashSet<string> MappedStateKeys(AnimancerFacade facade)
        {
            var keys = new HashSet<string>();
            var so = new SerializedObject(facade);
            SerializedProperty maps = so.FindProperty("transitionMappings");
            for (int i = 0; i < maps.arraySize; i++)
            {
                SerializedProperty element = maps.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("Transition").objectReferenceValue == null) continue;
                keys.Add(element.FindPropertyRelative("StateKey").stringValue);
            }
            return keys;
        }
    }
}
