using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Project.Presentation.Audio;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// M2 Audio 資產類的確定性 EditMode 單元測試。
    /// 驗證 docs/02-dev-spec.md §3.4.2 的契約：AudioLibrarySO 攤平查表
    /// （註冊可查得／未註冊回 null／重複後者覆蓋＋Editor 警告／冪等），
    /// AudioDefinitionSO 的 clip 池與音高範圍邊界。
    /// 私有序列化欄位以反射注入，等同 Inspector 的手動配置（比照 StateMachineTests.BuildConfig 慣例）。
    /// </summary>
    public class AudioSystemTests
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

        private AudioDefinitionSO CreateDefinition(AudioClip[] clips = null, Vector2? pitchRange = null)
        {
            var def = ScriptableObject.CreateInstance<AudioDefinitionSO>();
            _created.Add(def);
            if (clips != null) SetPrivateField(def, "clips", clips);
            if (pitchRange.HasValue) SetPrivateField(def, "pitchRange", pitchRange.Value);
            return def;
        }

        private AudioLibrarySO CreateLibrary(params AudioLibrarySO.AudioEntry[] entries)
        {
            var lib = ScriptableObject.CreateInstance<AudioLibrarySO>();
            _created.Add(lib);
            SetPrivateField(lib, "entries", entries);
            return lib;
        }

        private AudioClip CreateClip(string clipName)
        {
            var clip = AudioClip.Create(clipName, 44, 1, 44100, false);
            _created.Add(clip);
            return clip;
        }

        // === AudioLibrarySO：查表契約 ===

        [Test]
        public void Library_Get_AfterInitialize_ReturnsRegisteredDefinition()
        {
            var def = CreateDefinition();
            var lib = CreateLibrary(new AudioLibrarySO.AudioEntry { EventId = AudioEventId.Landing, Definition = def });

            lib.Initialize();

            Assert.AreSame(def, lib.Get(AudioEventId.Landing), "Initialize 後應查得註冊的 Definition");
        }

        [Test]
        public void Library_Get_WithoutInitialize_ReturnsNullSafely()
        {
            var lib = CreateLibrary();
            Assert.IsNull(lib.Get(AudioEventId.Landing), "未 Initialize 的查表應回 null（呼叫端靜默跳過），不得拋例外");
        }

        [Test]
        public void Library_Get_UnregisteredEvent_ReturnsNull()
        {
            var lib = CreateLibrary(); // 空 entries：事件尚未填表
            lib.Initialize();
            Assert.IsNull(lib.Get(AudioEventId.Landing), "未註冊事件應回 null＝呼叫端靜默跳過（允許逐步填表）");
        }

        [Test]
        public void Library_DuplicateEntries_LaterOverridesEarlier_AndWarnsInEditor()
        {
            var first = CreateDefinition();
            var second = CreateDefinition();
            var lib = CreateLibrary(
                new AudioLibrarySO.AudioEntry { EventId = AudioEventId.Landing, Definition = first },
                new AudioLibrarySO.AudioEntry { EventId = AudioEventId.Landing, Definition = second });

            // 重複警告是 §3.4.2 規格的一部分（「後者覆蓋前者並在 Editor 警告」）——本測試親手注入了
            // 重複條目，警告是被測契約的正確輸出而非誤鳴，故以宣告預期的方式雙向鎖定（未出現則失敗）。
            // 用鬆耦合關鍵詞（Regex）鎖定，訊息措辭調整不會誤傷本測試。
            LogAssert.Expect(LogType.Warning, new Regex("重複"));
            lib.Initialize();

            Assert.AreSame(second, lib.Get(AudioEventId.Landing), "同一事件重複填表時，後者必須覆蓋前者");
        }

        [Test]
        public void Library_Initialize_IsIdempotent()
        {
            var def = CreateDefinition();
            var lib = CreateLibrary(new AudioLibrarySO.AudioEntry { EventId = AudioEventId.Landing, Definition = def });

            lib.Initialize();
            lib.Initialize(); // 共享資產會被多個 Controller 各自 Initialize（§3.4.2），必須冪等

            Assert.AreSame(def, lib.Get(AudioEventId.Landing), "重複 Initialize 後查表結果必須不變");
        }

        // === AudioDefinitionSO：clip 池與音高邊界 ===

        [Test]
        public void Definition_GetRandomClip_EmptyPool_ReturnsNull()
        {
            var withNullArray = CreateDefinition(clips: null);
            var withEmptyArray = CreateDefinition(clips: new AudioClip[0]);

            Assert.IsNull(withNullArray.GetRandomClip(), "clip 池為 null 應回 null（呼叫端據此跳過播放）");
            Assert.IsNull(withEmptyArray.GetRandomClip(), "clip 池為空陣列應回 null（呼叫端據此跳過播放）");
        }

        [Test]
        public void Definition_GetRandomClip_SingleClip_ReturnsThatClip()
        {
            var clip = CreateClip("only");
            var def = CreateDefinition(clips: new[] { clip });

            Assert.AreSame(clip, def.GetRandomClip(), "單一 clip 池必回傳該 clip");
        }

        [Test]
        public void Definition_GetRandomPitch_StaysWithinRange()
        {
            var def = CreateDefinition(pitchRange: new Vector2(0.9f, 1.1f));

            for (int i = 0; i < 64; i++)
            {
                Assert.That(def.GetRandomPitch(), Is.InRange(0.9f, 1.1f), "音高必須落在 pitchRange 內");
            }
        }

        [Test]
        public void Definition_GetRandomPitch_DefaultRange_IsConstantOne()
        {
            var def = CreateDefinition(); // 預設 pitchRange = (1,1)

            for (int i = 0; i < 8; i++)
            {
                Assert.AreEqual(1f, def.GetRandomPitch(), "預設 (1,1) 範圍應恆為 1（不做音高變化）");
            }
        }
    }
}
