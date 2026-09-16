using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Audio;
using Project.Presentation.Footstep;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// Stage 2：Surface 解析 ＋ (Surface, Kind, Gait) → clip pool 的確定性測試。
    ///
    /// ⭐ 本輪最重要的驗收標準是**一致性**：使用者回報的首要問題是
    /// 「Walk 播 Stone，收步卻冒出草地感」。因此這裡最關鍵的幾條測的不是「有沒有聲音」，
    /// 而是「**同一次接觸的三種事件會不會用到不同材質**」。
    /// </summary>
    public class FootstepSurfaceTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        // =====================================================================
        // 建構輔助
        // =====================================================================

        private Material NewMaterial(string name)
        {
            var m = Track(new Material(Shader.Find("Unlit/Color")));
            m.name = name;
            return m;
        }

        private SurfaceMapSO NewMap(SurfaceId defaultSurface, params (Material mat, SurfaceId surface)[] entries)
        {
            var map = Track(ScriptableObject.CreateInstance<SurfaceMapSO>());
            var so = new UnityEditor.SerializedObject(map);
            so.FindProperty("defaultSurface").enumValueIndex = (int)defaultSurface;
            var arr = so.FindProperty("materials");
            arr.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Material").objectReferenceValue = entries[i].mat;
                e.FindPropertyRelative("Surface").enumValueIndex = (int)entries[i].surface;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            map.Initialize();
            return map;
        }

        /// <summary>建立一個「有 Renderer、有 Collider」的地板物件。</summary>
        private Collider NewFloor(Material material)
        {
            var go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.GetComponent<Collider>();
        }

        // =====================================================================
        // Surface 解析優先級
        // =====================================================================

        [Test]
        public void Material_ResolvesThroughMap()
        {
            Material wood = NewMaterial("SomeWoodLookingThing");
            Collider floor = NewFloor(wood);
            SurfaceMapSO map = NewMap(SurfaceId.Stone, (wood, SurfaceId.Wood));

            var resolver = new SurfaceResolver();
            Assert.AreEqual(SurfaceId.Wood, resolver.Resolve(floor, Vector3.zero, map));
        }

        [Test]
        public void UnmappedMaterial_FallsBackToDefaultSurface()
        {
            Material unknown = NewMaterial("Floor_Mat_03");
            Collider floor = NewFloor(unknown);
            SurfaceMapSO map = NewMap(SurfaceId.Stone); // 完全沒設定

            var resolver = new SurfaceResolver();
            Assert.AreEqual(SurfaceId.Stone, resolver.Resolve(floor, Vector3.zero, map),
                "沒有配置的地板也必須有聲音——落到 Default Surface，不得回 Unknown");
        }

        [Test]
        public void SurfaceOverride_BeatsMaterialMapping()
        {
            Material stoneLooking = NewMaterial("Concrete_A");
            Collider floor = NewFloor(stoneLooking);
            floor.gameObject.AddComponent<SurfaceOverride>();

            var so = new UnityEditor.SerializedObject(floor.GetComponent<SurfaceOverride>());
            so.FindProperty("surface").enumValueIndex = (int)SurfaceId.Wood;
            so.ApplyModifiedPropertiesWithoutUndo();

            SurfaceMapSO map = NewMap(SurfaceId.Stone, (stoneLooking, SurfaceId.Stone));

            var resolver = new SurfaceResolver();
            Assert.AreEqual(SurfaceId.Wood, resolver.Resolve(floor, Vector3.zero, map),
                "Override 是最高優先級——美術 Material 與聲學材質可以不同（貼石紋的木橋）");
        }

        [Test]
        public void SurfaceOverride_OnParent_CoversChildColliders()
        {
            Material mat = NewMaterial("Bridge_Mat");
            var parent = Track(new GameObject("BridgeRoot"));
            parent.AddComponent<SurfaceOverride>();
            var so = new UnityEditor.SerializedObject(parent.GetComponent<SurfaceOverride>());
            so.FindProperty("surface").enumValueIndex = (int)SurfaceId.Wood;
            so.ApplyModifiedPropertiesWithoutUndo();

            Collider child = NewFloor(mat);
            child.transform.SetParent(parent.transform);

            SurfaceMapSO map = NewMap(SurfaceId.Stone);
            var resolver = new SurfaceResolver();

            Assert.AreEqual(SurfaceId.Wood, resolver.Resolve(child, Vector3.zero, map),
                "掛在父層即可涵蓋其下所有子 collider——這是「不必逐個掛」的關鍵");
        }

        [Test]
        public void NoCollider_ResolvesToUnknown_AndDoesNotReuseStaleResult()
        {
            Material wood = NewMaterial("Wood");
            Collider floor = NewFloor(wood);
            SurfaceMapSO map = NewMap(SurfaceId.Stone, (wood, SurfaceId.Wood));

            var resolver = new SurfaceResolver();
            Assert.AreEqual(SurfaceId.Wood, resolver.Resolve(floor, Vector3.zero, map));

            Assert.AreEqual(SurfaceId.Unknown, resolver.Resolve(null, Vector3.zero, map),
                "在空中時不得沿用「上一次踩在木橋上」的結果——那不代表等一下會落在木橋上");
        }

        [Test]
        public void Memoization_ReturnsSameResultForSameCollider()
        {
            Material wood = NewMaterial("Wood");
            Collider floor = NewFloor(wood);
            SurfaceMapSO map = NewMap(SurfaceId.Stone, (wood, SurfaceId.Wood));

            var resolver = new SurfaceResolver();
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(SurfaceId.Wood, resolver.Resolve(floor, Vector3.zero, map));
        }

        [Test]
        public void SwitchingCollider_ReResolves()
        {
            Material wood = NewMaterial("Wood");
            Material grass = NewMaterial("Grass");
            Collider woodFloor = NewFloor(wood);
            Collider grassFloor = NewFloor(grass);
            SurfaceMapSO map = NewMap(SurfaceId.Stone, (wood, SurfaceId.Wood), (grass, SurfaceId.Grass));

            var resolver = new SurfaceResolver();
            Assert.AreEqual(SurfaceId.Wood, resolver.Resolve(woodFloor, Vector3.zero, map));
            Assert.AreEqual(SurfaceId.Grass, resolver.Resolve(grassFloor, Vector3.zero, map),
                "換了 collider 必須重新解析——記憶化不得吃掉材質變化");
            Assert.AreEqual(SurfaceId.Wood, resolver.Resolve(woodFloor, Vector3.zero, map));
        }

        [Test]
        public void NullMap_ResolvesToUnknown()
        {
            Collider floor = NewFloor(NewMaterial("X"));
            var resolver = new SurfaceResolver();
            Assert.AreEqual(SurfaceId.Unknown, resolver.Resolve(floor, Vector3.zero, null),
                "沒有對應表時安全靜默，不得丟例外");
        }

        // =====================================================================
        // Bank：(Surface, Kind, Gait) → pool
        // =====================================================================

        private AudioDefinitionSO NewDefinition(string name)
        {
            var def = Track(ScriptableObject.CreateInstance<AudioDefinitionSO>());
            def.name = name;
            return def;
        }

        /// <summary>建立一個 Bank；每個 set 傳 (surface, walk, run, shuffle, landing)。</summary>
        private FootstepAudioBankSO NewBank(SurfaceId defaultSurface,
            params (SurfaceId s, AudioDefinitionSO walk, AudioDefinitionSO run,
                    AudioDefinitionSO shuffle, AudioDefinitionSO landing)[] sets)
        {
            var bank = Track(ScriptableObject.CreateInstance<FootstepAudioBankSO>());
            var so = new UnityEditor.SerializedObject(bank);
            so.FindProperty("defaultSurface").enumValueIndex = (int)defaultSurface;
            var arr = so.FindProperty("surfaces");
            arr.arraySize = sets.Length;
            for (int i = 0; i < sets.Length; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Surface").enumValueIndex = (int)sets[i].s;
                e.FindPropertyRelative("Walk").objectReferenceValue = sets[i].walk;
                e.FindPropertyRelative("Run").objectReferenceValue = sets[i].run;
                e.FindPropertyRelative("Shuffle").objectReferenceValue = sets[i].shuffle;
                e.FindPropertyRelative("Landing").objectReferenceValue = sets[i].landing;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            bank.Initialize();
            return bank;
        }

        [Test]
        public void Gait_SelectsWalkOrRunPool()
        {
            AudioDefinitionSO walk = NewDefinition("StoneWalk");
            AudioDefinitionSO run = NewDefinition("StoneRun");
            FootstepAudioBankSO bank = NewBank(SurfaceId.Stone,
                (SurfaceId.Stone, walk, run, null, null));

            Assert.AreSame(walk, bank.ResolveDefinition(SurfaceId.Stone, FootstepKind.Normal, false, GaitTier.Walk, out _));
            Assert.AreSame(run, bank.ResolveDefinition(SurfaceId.Stone, FootstepKind.Normal, false, GaitTier.Run, out _));
        }

        /// <summary>WASD Free Bundle 沒有 Sprint 素材（實測：只有 Drop/Jump/Run/Shuffle/Sneak/Walk）。</summary>
        [Test]
        public void Sprint_UsesRunPool()
        {
            AudioDefinitionSO walk = NewDefinition("StoneWalk");
            AudioDefinitionSO run = NewDefinition("StoneRun");
            FootstepAudioBankSO bank = NewBank(SurfaceId.Stone,
                (SurfaceId.Stone, walk, run, null, null));

            Assert.AreSame(run,
                bank.ResolveDefinition(SurfaceId.Stone, FootstepKind.Normal, false, GaitTier.Sprint, out _),
                "沒有 Sprint 素材時用 Run 池——⛔ 不得為此造一套 pitch 合成的假 Sprint");
        }

        /// <summary>⭐ 本輪最重要的一條：缺素材時**寧可換 kind 也不換 surface**。</summary>
        [Test]
        public void MissingShuffle_FallsBackWithinSameSurface_NotToAnotherSurface()
        {
            AudioDefinitionSO stoneWalk = NewDefinition("StoneWalk");
            AudioDefinitionSO stoneShuffle = NewDefinition("StoneShuffle");
            AudioDefinitionSO woodWalk = NewDefinition("WoodWalk");

            FootstepAudioBankSO bank = NewBank(SurfaceId.Stone,
                (SurfaceId.Stone, stoneWalk, null, stoneShuffle, null),
                (SurfaceId.Wood, woodWalk, null, null, null)); // Wood 沒有 Shuffle

            AudioDefinitionSO resolved =
                bank.ResolveDefinition(SurfaceId.Wood, FootstepKind.Shuffle, false, GaitTier.Run, out float scale);

            Assert.AreSame(woodWalk, resolved,
                "Wood 缺 Shuffle 時要用 **Wood** Walk，不得跳去用 Stone Shuffle——" +
                "材質跑掉比音色類別不精準嚴重得多，那正是使用者回報的出戲來源");
            Assert.Less(scale, 0.45f, "用較響的 Walk 當替身時必須再壓低音量");
        }

        [Test]
        public void UnknownSurface_UsesDefaultSurfaceSet()
        {
            AudioDefinitionSO stoneWalk = NewDefinition("StoneWalk");
            FootstepAudioBankSO bank = NewBank(SurfaceId.Stone,
                (SurfaceId.Stone, stoneWalk, null, null, null));

            Assert.AreSame(stoneWalk,
                bank.ResolveDefinition(SurfaceId.Unknown, FootstepKind.Normal, false, GaitTier.Walk, out _),
                "角色在空中／完全沒解析到材質時，仍然要有聲音");
        }

        [Test]
        public void Landing_UsesLandingPoolOfTheSameSurface()
        {
            AudioDefinitionSO woodWalk = NewDefinition("WoodWalk");
            AudioDefinitionSO woodLanding = NewDefinition("WoodLanding");
            AudioDefinitionSO stoneLanding = NewDefinition("StoneLanding");

            FootstepAudioBankSO bank = NewBank(SurfaceId.Stone,
                (SurfaceId.Stone, null, null, null, stoneLanding),
                (SurfaceId.Wood, woodWalk, null, null, woodLanding));

            Assert.AreSame(woodLanding,
                bank.ResolveDefinition(SurfaceId.Wood, FootstepKind.None, true, GaitTier.Run, out _),
                "在木頭上落地要用木頭的落地音");
        }

        [Test]
        public void MasterVolume_ScalesEverything()
        {
            FootstepAudioBankSO bank = NewBank(SurfaceId.Stone, (SurfaceId.Stone, null, null, null, null));
            var so = new UnityEditor.SerializedObject(bank);
            so.FindProperty("masterVolume").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            bank.Initialize();

            Assert.AreEqual(0.25f, bank.ApplyMasterVolume(1f, 0.5f), 1e-4f,
                "最終音量＝clip 池音量 × master × 類別／gait 係數");
        }

        // =====================================================================
        // ⭐ 一致性：三種事件共用同一次 Surface 決策
        // =====================================================================

        [Test]
        public void AllThreeEventKinds_ShareOneSurfaceDecision()
        {
            // 這條測的是**結構**：三種事件讀的是 PresentationEventData 上同一個欄位，
            // 因此它們在型別層就不可能不一致。若哪天有人把 Surface 拆成左右兩份，這條會紅。
            var events = new PresentationEventData
            {
                LeftFootstep = FootstepKind.Normal,
                RightFootstep = FootstepKind.Shuffle,
                Landed = true,
                Surface = SurfaceId.Wood,
            };

            AudioDefinitionSO woodWalk = NewDefinition("WoodWalk");
            AudioDefinitionSO woodShuffle = NewDefinition("WoodShuffle");
            AudioDefinitionSO woodLanding = NewDefinition("WoodLanding");
            AudioDefinitionSO stoneWalk = NewDefinition("StoneWalk");

            FootstepAudioBankSO bank = NewBank(SurfaceId.Stone,
                (SurfaceId.Stone, stoneWalk, null, null, null),
                (SurfaceId.Wood, woodWalk, null, woodShuffle, woodLanding));

            AudioDefinitionSO normal =
                bank.ResolveDefinition(events.Surface, events.LeftFootstep, false, GaitTier.Walk, out _);
            AudioDefinitionSO shuffle =
                bank.ResolveDefinition(events.Surface, events.RightFootstep, false, GaitTier.Walk, out _);
            AudioDefinitionSO landing =
                bank.ResolveDefinition(events.Surface, FootstepKind.None, true, GaitTier.Walk, out _);

            Assert.AreSame(woodWalk, normal);
            Assert.AreSame(woodShuffle, shuffle);
            Assert.AreSame(woodLanding, landing);
            Assert.AreNotSame(stoneWalk, normal,
                "⭐ 同一帧的 Normal／Shuffle／Landing 不得有任何一個掉回別的材質");
        }
    }
}
