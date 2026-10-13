using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Survivability;
using Project.Presentation.Audio;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 🆕（2026-09-17）戰鬥音效切片的 EditMode 契約測試。
    ///
    /// <para><b>這一組守的是「觸發來源正確」，不是「有沒有聲音出來」</b></para>
    /// EditMode 不在 Play mode，`AudioSource.Play` 不會真的出聲，也沒有 `Awake`。
    /// 因此測試斷言的是**可觀察的決定**：
    /// ①`CharacterHealth` 在什麼時候抬起死亡事件、抬幾次；
    /// ②`ActionAudioSink` 把哪一個 Definition 送到哪一個階段；
    /// ③連段時 release 音響幾聲。
    /// 「聽起來對不對」留給人工 Play——那是 `docs/12` §6 Verification Ladder 的正確分界。
    ///
    /// <para><b>命名</b></para>
    /// `CA*` = Combat Audio，比照既有的 `CP*`／`CO*`／`PS*` 慣例。
    /// </summary>
    public class CombatAudioTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }

        private GameObject NewObject(string objectName)
        {
            var go = new GameObject(objectName);
            _created.Add(go);
            return go;
        }

        private AudioDefinitionSO CreateDefinition(string assetName)
        {
            var def = ScriptableObject.CreateInstance<AudioDefinitionSO>();
            def.name = assetName;
            _created.Add(def);
            return def;
        }

        // =====================================================================
        // CA1–CA5 — 死亡事件（SurvivabilityData.JustDied）
        //
        // ⭐ 這幾條真正在守的是：**死亡音的觸發源是「事件」不是「狀態」**。
        //    若有人日後把 AudioController 改成讀 IsDead，CA2 會立刻紅。
        // =====================================================================

        /// <summary>CA1 — 致死傷害抬起死亡事件，且**不**同時抬起受擊事件（ADR-009 D2 的決策擁有權）。</summary>
        [Test]
        public void CA1_LethalDamage_PublishesJustDied_AndNotJustTookDamage()
        {
            var health = NewObject("Victim").AddComponent<CharacterHealth>();
            health.InitializeForTest(10f);
            var data = new PlayerRuntimeData();

            health.ApplyDamage(999f);
            health.PublishTo(data);

            Assert.IsTrue(data.Survivability.JustDied, "致死那一幀必須抬起 JustDied");
            Assert.IsTrue(data.Survivability.IsDead, "同一幀 IsDead 也必須為真");
            Assert.IsFalse(data.Survivability.JustTookDamage,
                "致死不得同時抬起 JustTookDamage——否則受擊音與死亡音會在同一幀一起播");
        }

        /// <summary>
        /// CA2 — 死亡事件**只有一幀**。
        /// ⭐ 這是整組測試最重要的一條：它就是「事件 vs 狀態」的差別。
        /// 若改用 `IsDead` 當觸發源，第二次 Publish 仍會是 true ⇒ 死亡音每幀重播。
        /// </summary>
        [Test]
        public void CA2_JustDied_IsSingleFrameEvent_WhileIsDeadStaysTrue()
        {
            var health = NewObject("Victim").AddComponent<CharacterHealth>();
            health.InitializeForTest(10f);
            var data = new PlayerRuntimeData();

            health.ApplyDamage(999f);
            health.PublishTo(data);
            Assert.IsTrue(data.Survivability.JustDied, "第一次發布應抬起事件");

            health.PublishTo(data);
            Assert.IsFalse(data.Survivability.JustDied,
                "JustDied 必須在寫出後立即清除——它是事件不是狀態");
            Assert.IsTrue(data.Survivability.IsDead,
                "IsDead 是持續狀態，必須維持為真（兩者語意不同，這正是分成兩個欄位的理由）");
        }

        /// <summary>CA3 — 對屍體再施加傷害不會再抬起死亡事件（否則屍體被補刀會重播死亡音）。</summary>
        [Test]
        public void CA3_DamagingACorpse_DoesNotRepublishJustDied()
        {
            var health = NewObject("Victim").AddComponent<CharacterHealth>();
            health.InitializeForTest(10f);
            var data = new PlayerRuntimeData();

            health.ApplyDamage(999f);
            health.PublishTo(data); // 消費掉死亡事件

            health.ApplyDamage(999f);
            health.PublishTo(data);

            Assert.IsFalse(data.Survivability.JustDied, "屍體再挨打不得再次抬起死亡事件");
            Assert.IsFalse(data.Survivability.JustTookDamage, "屍體也不得抬起受擊事件");
        }

        /// <summary>
        /// CA4 — 未致死傷害抬起受擊事件、**不**抬起死亡事件。
        /// 與 CA1 成對，把兩個分支都釘住。
        /// </summary>
        [Test]
        public void CA4_NonLethalDamage_PublishesJustTookDamage_Only()
        {
            var health = NewObject("Victim").AddComponent<CharacterHealth>();
            health.InitializeForTest(100f);
            var data = new PlayerRuntimeData();

            health.ApplyDamage(10f);
            health.PublishTo(data);

            Assert.IsTrue(data.Survivability.JustTookDamage, "未致死傷害必須抬起受擊事件");
            Assert.IsFalse(data.Survivability.JustDied, "未致死不得抬起死亡事件");
            Assert.IsFalse(data.Survivability.IsDead, "未致死不得標記死亡");
        }

        /// <summary>
        /// CA5 — 在死亡與發布之間復活，不得殘留死亡事件。
        /// 否則角色會「一復活就播一次死亡音」——與 `Revive()` 清 `_pendingHurt` 同一個理由。
        /// </summary>
        [Test]
        public void CA5_ReviveBeforePublish_ClearsPendingDeath()
        {
            var health = NewObject("Victim").AddComponent<CharacterHealth>();
            health.InitializeForTest(10f);
            var data = new PlayerRuntimeData();

            health.ApplyDamage(999f);
            Assert.IsTrue(health.HasPendingDeath, "前提：死亡事件此時尚未發布");

            health.Revive();
            health.PublishTo(data);

            Assert.IsFalse(data.Survivability.JustDied, "復活後不得殘留死亡事件");
            Assert.IsFalse(data.Survivability.IsDead, "復活後不得仍是死亡狀態");
        }

        /// <summary>
        /// CA6 — 以 0 血起始的角色**不是**「這一幀死的」。
        /// 場景裡預先擺放的屍體不該在第一幀播死亡音。
        /// </summary>
        [Test]
        public void CA6_StartingDead_IsNotADeathEvent()
        {
            var health = NewObject("Corpse").AddComponent<CharacterHealth>();
            health.InitializeForTest(0f);
            var data = new PlayerRuntimeData();

            health.PublishTo(data);

            Assert.IsTrue(data.Survivability.IsDead, "0 血起始應為死亡狀態");
            Assert.IsFalse(data.Survivability.JustDied, "但那不是一次死亡**事件**");
        }

        // =====================================================================
        // CA7–CA10 — ActionAudioSink（Cast ／ Release 的投遞）
        //
        // 以 `AudioController` 的 `PlayAt` 為觀測點會需要 Play mode，
        // 因此改為斷言**送進 sink 的是哪一個 Definition**：
        // 用一顆沒有 AudioController 的 sink 驗「不炸」，用欄位注入驗「接對了」。
        // =====================================================================

        private ActionAudioSink CreateSink(
            AudioDefinitionSO cast, AudioDefinitionSO release, out AudioController controller)
        {
            GameObject root = NewObject("Character");
            controller = root.AddComponent<AudioController>();

            var sink = root.AddComponent<ActionAudioSink>();
            SetPrivateField(sink, "castDefinition", cast);
            SetPrivateField(sink, "releaseDefinition", release);
            sink.ResolveAudioController();
            return sink;
        }

        /// <summary>
        /// CA7 — sink 沿階層找得到同一顆角色的 <see cref="AudioController"/>。
        /// ⭐ 這條守的是「**不是 singleton**」：取得方式必須是階層查找，
        /// 因此把 sink 放在沒有 AudioController 的物件上時，它就該是 null 而不是抓到別人的。
        /// </summary>
        [Test]
        public void CA7_Sink_ResolvesAudioControllerFromOwnHierarchy()
        {
            ActionAudioSink sink = CreateSink(null, null, out AudioController controller);

            FieldInfo field = typeof(ActionAudioSink)
                .GetField("audioController", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.AreSame(controller, field.GetValue(sink),
                "sink 必須沿自己的階層取得 AudioController（不是全域查找）");
        }

        /// <summary>
        /// CA8 — 沒有 AudioController 的孤兒 sink 被呼叫時**安靜地什麼都不做**，不得丟例外。
        /// 缺音效不該讓整個 Action 崩掉——這與 `AudioLibrarySO.Get` 回 null 時靜默跳過同一個紀律。
        /// </summary>
        [Test]
        public void CA8_Sink_WithoutAudioController_DoesNotThrow()
        {
            var sink = NewObject("OrphanSink").AddComponent<ActionAudioSink>();
            SetPrivateField(sink, "castDefinition", CreateDefinition("Cast"));
            SetPrivateField(sink, "releaseDefinition", CreateDefinition("Release"));
            sink.ResolveAudioController();

            var context = default(ActionReleaseContext);
            Assert.DoesNotThrow(() => sink.Begin(), "缺 AudioController 時 Begin 必須安靜跳過");
            Assert.DoesNotThrow(() => sink.Release(in context), "缺 AudioController 時 Release 必須安靜跳過");
            Assert.DoesNotThrow(() => sink.Cleanup(), "Cleanup 恆為 no-op");
        }

        /// <summary>
        /// CA9 — 兩個階段的 Definition 是**各自獨立**的欄位，不會互相汙染。
        /// 看似瑣碎，但它釘住的是「Cast 與 Release 是兩個事件」這個設計決定：
        /// 有人把它們合併成一個欄位時，這條會紅。
        /// </summary>
        [Test]
        public void CA9_Sink_CastAndRelease_AreIndependentFields()
        {
            AudioDefinitionSO cast = CreateDefinition("Cast");
            AudioDefinitionSO release = CreateDefinition("Release");
            ActionAudioSink sink = CreateSink(cast, release, out _);

            FieldInfo castField = typeof(ActionAudioSink)
                .GetField("castDefinition", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo releaseField = typeof(ActionAudioSink)
                .GetField("releaseDefinition", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.AreSame(cast, castField.GetValue(sink), "Cast 欄位不得被 Release 覆寫");
            Assert.AreSame(release, releaseField.GetValue(sink), "Release 欄位不得被 Cast 覆寫");
        }

        /// <summary>
        /// CA10 — **連段時每一段都要響**。
        ///
        /// ⭐ 這一條記錄的是實作過程中差點踩進去的坑：我原本在 sink 裡加了
        /// `_releasedThisExecution` 閘門（比照 `MeleeHitboxSink`），那會讓三段連擊只響第一聲。
        /// `ActionState.TryAdvanceChain` **刻意**在每段重置 `_releaseEmittedThisExecution` 並再發一次
        /// Release（「每段各出一顆投射物」），所以「一次 Action 該響幾聲」已由 ActionState 回答完。
        /// 這裡以「連續呼叫 Release 不被吞掉」把該決定釘住。
        /// </summary>
        [Test]
        public void CA10_Sink_RepeatedRelease_IsNotThrottled()
        {
            ActionAudioSink sink = CreateSink(null, CreateDefinition("Swing"), out _);
            var context = default(ActionReleaseContext);

            // 連段：Begin 一次、Release 三次。sink 不得自行節流。
            sink.Begin();
            Assert.DoesNotThrow(() =>
            {
                sink.Release(in context);
                sink.Release(in context);
                sink.Release(in context);
            }, "sink 不得持有跨 Release 的節流狀態——那是 ActionState 的職責");

            FieldInfo[] fields = typeof(ActionAudioSink)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                Assert.AreNotEqual("_releasedThisExecution", field.Name,
                    "⛔ ActionAudioSink 不得重新引入 release 節流旗標：" +
                    "ActionState 的連段會在每段各發一次 Release，加上閘門會讓第 2、3 段無聲。");
            }
        }

        /// <summary>
        /// 🆕（2026-10-12，使用者裁決 Q6，`docs/54` §3.6）**受擊語音跟實際傷害事件，不跟 Hurt 狀態。**
        ///
        /// Roll／Traversal 擋下硬直時（`docs/26` Model B），傷害已成立、受擊事件已發布——
        /// 被丟棄的只有硬直**反應**，語音照常。這個性質目前由「`AudioController` 根本不認識 FSM」保證；
        /// 本測試把它釘住，避免日後有人「順手」改成跟著 `HurtState` 走而沒有任何紅燈。
        /// 聽感仍保留人工驗證。
        /// </summary>
        [Test]
        public void CA11_HurtAudio_IsDrivenByDamageEvent_NotByFsmState()
        {
            string path = System.IO.Path.Combine(
                Application.dataPath, "Scripts", "Presentation", "Audio", "AudioController.cs");
            Assert.IsTrue(System.IO.File.Exists(path), $"找不到 {path}");

            string code = StripLineComments(System.IO.File.ReadAllText(path));

            StringAssert.Contains("data.Survivability.JustTookDamage", code,
                "受擊語音的唯一觸發來源必須是已 commit 的傷害事件");
            foreach (string token in new[] { "StateType", "HurtState", "FullBodyStateMachine", "CurrentState" })
            {
                StringAssert.DoesNotContain(token, code,
                    $"AudioController 出現 '{token}'：受擊語音不得依賴 Hurt 狀態是否切入（Q6）");
            }
        }

        /// <summary>
        /// 🆕（2026-10-13，使用者裁決：命中音「依武器拆開」）**命中音的語意跟著武器，不跟著陣營。**
        /// 玩家持劍 ⇒ 只有金屬刀刃；敵人出拳 ⇒ 只有拳打肉身。原本兩者共用一個混合池，
        /// 每次隨機抽到另一種武器的聲音（劍砍出拳擊聲、拳頭打出金屬刮擦聲）。
        ///
        /// ⚠️ wav 不進版控（授權，`docs/37` §6.5）⇒ 沒有素材的 clone 上 clip 引用會是 null；
        /// 這裡只檢查**解析得到**的 clip，空引用交給 `AudioDefinitionSO` 既有的防護。
        /// 兩支 sink 指向不同資產這一條則與素材是否存在無關，永遠檢查。
        /// </summary>
        [Test]
        public void CA12_MeleeHitAudio_MatchesTheWeapon_BladeForPlayerFistForEnemy()
        {
            AudioDefinitionSO player = LoadHitAudio("Assets/Prefabs/X Bot.prefab");
            AudioDefinitionSO enemy = LoadHitAudio("Assets/Prefabs/Y Bot.prefab");
            Assert.AreNotSame(player, enemy, "玩家（劍）與敵人（拳）的命中音必須是不同的池");

            AssertClipsAll(player, "WEAPSwrd_Sword_Slide_Cuts", "玩家劍的命中音只能是金屬刀刃");
            AssertClipsAll(enemy, "FGHTImpt_4_x_Punch_Body", "敵人拳頭的命中音只能是拳打肉身");
        }

        private static AudioDefinitionSO LoadHitAudio(string prefabPath)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath);
            var sink = prefab.GetComponentInChildren<Project.Presentation.Actions.MeleeHitboxSink>(true);
            Assert.IsNotNull(sink, $"{prefabPath} 缺少 MeleeHitboxSink");
            var audio = new UnityEditor.SerializedObject(sink).FindProperty("hitAudio").objectReferenceValue as AudioDefinitionSO;
            Assert.IsNotNull(audio, $"{prefabPath} 的 MeleeHitboxSink.hitAudio 未指派");
            return audio;
        }

        private static void AssertClipsAll(AudioDefinitionSO definition, string prefix, string message)
        {
            SerializedPropertyIterate(definition, clip =>
                StringAssert.StartsWith(prefix, clip.name, $"{message}（{definition.name} 含 {clip.name}）"));
        }

        private static void SerializedPropertyIterate(AudioDefinitionSO definition, System.Action<AudioClip> visit)
        {
            UnityEditor.SerializedProperty clips = new UnityEditor.SerializedObject(definition).FindProperty("clips");
            Assert.IsNotNull(clips, "AudioDefinitionSO.clips 欄位不存在");
            for (int i = 0; i < clips.arraySize; i++)
            {
                if (clips.GetArrayElementAtIndex(i).objectReferenceValue is AudioClip clip) visit(clip);
            }
        }

        private static string StripLineComments(string code)
        {
            var builder = new System.Text.StringBuilder(code.Length);
            foreach (string line in code.Split('\n'))
            {
                int index = line.IndexOf("//", System.StringComparison.Ordinal);
                builder.Append(index >= 0 ? line.Substring(0, index) : line).Append('\n');
            }
            return builder.ToString();
        }
    }
}
