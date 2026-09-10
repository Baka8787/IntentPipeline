#if UNITY_EDITOR
using Animancer;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Project.Core.Combat;
using Project.Core.Facing;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    // 一次性 Animation batchmode 作業集中處：只承載已明確指定的資產遷移／量測入口，
    // 不把低頻接線工作擴張成常設 Window、Wizard 或 runtime abstraction。每個入口都必須先完成
    // 全量唯讀前檢查，再開始任何寫入；這是避免 batchmode 中途才發現缺件、留下半套資產的原因。
    public static class AnimationBatchOps
    {
        private const string SourceFbxPath =
            "Assets/MovementAnimsetPro/Animations/MovementAnimsetPro_RunStrafeUpdate.fbx";
        private const string BaseMovementFbxPath =
            "Assets/MovementAnimsetPro/Animations/MovementAnimsetPro.fbx";
        private const string CharacterPrefabPath = "Assets/Prefabs/X Bot.prefab";
        private const string MotionAssetFolder = "Assets/ScriptableObjects/Motion";
        private const string AnimationAssetFolder = "Assets/ScriptableObjects/Animation";
        private const string LocomotionAssetPath = AnimationAssetFolder + "/Locomotion.asset";
        private const string JumpTransitionTemplatePath = AnimationAssetFolder + "/Jump.asset";
        private const string JumpStateParamsPath =
            "Assets/ScriptableObjects/StateMachine/JumpStateParams.asset";
        private const string LegacyJumpBakePath = MotionAssetFolder + "/Bake_Jump.asset";
        private const string LegacyJumpBakeGuid = "de633036526a931498f985475fbebdd8";
        private const string WalkLoopBakePath = MotionAssetFolder + "/Bake_WalkFwdLoop.asset";
        private const string RunLoopBakePath = MotionAssetFolder + "/Bake_RunFwdLoop.asset";
        private const string MoveXAssetPath = AnimationAssetFolder + "/MoveX.asset";
        private const string MoveZAssetPath = AnimationAssetFolder + "/MoveZ.asset";
        private const string FullRingAssetPath =
            AnimationAssetFolder + "/Locomotion_2D_Proto_FullRing.asset";
        private const string RunRingAssetPath =
            AnimationAssetFolder + "/Locomotion_2D_Proto_RunRing.asset";
        private const float PrototypeFadeDuration = 0.15f;
        private const float SampleRate = 60f;

        private static readonly string[] _requiredClipNames =
        {
            "RunBwdLoop",
            "RunLtLoop",
            "RunRtLoop",
            "RunStrafeLeft45Loop",
            "RunStrafeRight45Loop",
            "RunStrafeLeft135Loop",
            "RunStrafeRight135Loop"
        };

        private static readonly string[] _prototypeDirectionalClipNames =
        {
            "RunFwdLoop",
            "RunBwdLoop",
            "RunLtLoop",
            "RunRtLoop",
            "RunStrafeLeft45Loop",
            "RunStrafeRight45Loop",
            "RunStrafeLeft135Loop",
            "RunStrafeRight135Loop"
        };

        private static readonly Vector2[] _prototypeUnitThresholds =
        {
            new(0f, 1f),
            new(0f, -1f),
            new(-1f, 0f),
            new(1f, 0f),
            new(-0.7071f, 0.7071f),
            new(0.7071f, 0.7071f),
            new(-0.7071f, -0.7071f),
            new(0.7071f, -0.7071f)
        };

        /// <summary>
        /// Movement Animset Pro 跳躍子 clip 的**分段**集合（Start／Land／Land2*）。
        ///
        /// ⛔ **播放端刻意不含 `Jump_*_ALL`**：那是把「起跳→滯空→落地」烘成一支**固定長度**的整段 clip，
        /// 與 **ADR-002（Accepted）**「跳躍走物理 launch、滯空時間由物理決定」直接衝突——
        /// clip 播完人還在空中、或人落地了 clip 還沒完。分段版本才能讓起跳／落地各自對齊真正的物理時點。
        ///
        /// <para><b>✅ 但 `Jump_place_ALL` 仍在本清單裡——它的角色是「量測」不是「播放」（2026-09-09 裁決）</b></para>
        /// 「固定長度」只在**播放**時是缺點；**量測不播放**，完整弧線反而是必要條件。
        /// 實測它是**唯一**跨基線演算法收斂的跳躍 clip（B/C/D 三種取法：base 差 0.6mm、
        /// apex 四位小數完全相同 0.9535、air 差 5.7ms、g 差 1.5%）⇒ 有資格當單一 launch data 來源。
        ///
        /// ⚠️ **Walk／Run 的 `*Start` 不追自己的 `AutoApexHeight`**（使用者 2026-09-09 明確裁決）：
        /// 它們提供的是**左右腳視覺／起跳姿態／gait 對應動畫**，不是 gameplay jump height。
        /// 第一版 Idle／Walk／Run **共用同一組垂直 launch physics**，動畫依當下 gait／foot phase 換。
        /// 📌 實測佐證：walk/run 的 `_ALL` 也不自足（`Jump_run_lu_ALL` 的 apex 隨演算法在 0.379~0.668 之間跑）
        /// ——因為它們從跑步腳相蹬地起飛後**不再接地**，那是資訊缺失，換剪裁窗口變不出地面。
        /// </summary>
        private static readonly string[] _jumpClipNames =
        {
            // 🎯 量測來源（唯一）：gameplay 的垂直 launch data 由 `_short` 反推（v = √(2gh)）。
            // ⚠️ **用 `_short` 不用長版**（使用者 2026-09-09 裁決，實測支持）：兩者 apex 完全相同
            // （皆 0.9535），但 **`AutoTakeoffDelay` 差了 5.6 倍**——長版 0.4871s vs `_short` 0.0873s。
            // 那個欄位直接閘住 `ApplyJumpLaunch`（`JumpState.OnUpdateMotion` 等它才發射）
            // ⇒ 用長版等於按下跳躍後**乾等 0.49 秒**才離地，手感直接毀掉。
            // 📌 `_short` 的 g 也較高（16.78 vs 13.93）＝更俐落，與「idle2jump2idle 用 short 比較好」一致。
            // 📌 長版仍烘（下一行），保留作為對照與 A/B 的依據，但**不是** Stages[0].Bake 的來源。
            "Jump_place_ALL_short",
            "Jump_place_ALL",
            // 起跳（原地／走／跑）。LU／RU ＝ 左腳／右腳起跳，由 loop 的 FootPhaseCurve 選。
            "JumpIdleStart",
            "JumpWalkStart_LU", "JumpWalkStart_RU",
            "JumpRunStart_LU",  "JumpRunStart_RU",
            // 落地收住（急停）
            "JumpIdleLand", "JumpIdleLandHard",
            "JumpWalk_LU_Land", "JumpWalk_RU_Land",
            "JumpRun_LU_Land",  "JumpRun_RU_Land",
            // 落地接續移動
            "JumpIdleLand2Walk",
            "JumpWalk_LU_Land2Walk", "JumpWalk_RU_Land2Walk",
            "JumpRun_LU_Land2Run",   "JumpRun_RU_Land2Run",
        };

        /// <summary>
        /// 跳躍 clip 住在 MAP 的**主 FBX**，與 strafe 用的 <c>SourceFbxPath</c>
        /// （<c>MovementAnimsetPro_RunStrafeUpdate.fbx</c>）**不是同一支**。
        ///
        /// ⚠️ 這支主 FBX 同時裝著 Crouch／ButtonPush／**Throw_\***／**RunFwdLoop** 等多個已結案的家族
        /// （`Throw_*` 屬 ADR-004、`RunFwdLoop` 是 locomotion 前進點）——
        /// ⛔ **整檔套 preset 會把它們全部灌壞**，所以只能走 per-clip 的明確清單。
        /// </summary>
        private const string JumpSourceFbxPath =
            "Assets/MovementAnimsetPro/Animations/MovementAnimsetPro.fbx";

        private static AnimationClip[] ResolveJumpClips()
        {
            var clips = new AnimationClip[_jumpClipNames.Length];
            for (int i = 0; i < _jumpClipNames.Length; i++)
            {
                clips[i] = ResolveExactClip(JumpSourceFbxPath, _jumpClipNames[i]);
            }
            return clips;
        }

        /// <summary>
        /// 空中循環：Kubold 的 `FallingLoop` 是「你在空中」的匯流狀態
        /// （`PlayerMaleController` 由 `IsFalling` 從 5＋ 個來源拉進去），
        /// 本專案用它當 `JumpState` 的 Falling 相位視覺。
        ///
        /// ⚠️ **刻意不含 `FallingLoop_RootMotion`**：那支會萃取位移，與
        /// 「垂直運動由物理擁有」（ADR-002）直接衝突。
        /// </summary>
        private static readonly string[] _fallingClipNames = { "FallingLoop" };

        /// <summary>
        /// 第一階段只接五個唯一鍵。MAP 沒有 Hard→Move 專用 clip，因此「下降後停住」用
        /// <c>JumpIdleLandHard</c>，「下降後續走」仍用 <c>JumpIdleLand2Walk</c>；
        /// 這讓 Hard 路徑與續走分流各自有可觀察落點，又不虛構第六支素材。
        /// </summary>
        private static readonly string[] _idleJumpAnimationKeys =
        {
            "JumpIdleStart",
            "FallingLoop",
            "JumpIdleLand",
            "JumpIdleLandHard",
            "JumpIdleLand2Walk"
        };

        /// <summary>
        /// 第二階段才加入的十二個 Walk／Run × LU／RU 鍵。陣列順序固定為 LU、RU，
        /// 因為 <see cref="LocomotionStopSelector"/> 讀的是 Bake Data 腳相，不讀檔名字尾；
        /// 固定順序只讓輸出與資產 diff 可預測，不把 LU/RU 規則藏進接線工具。
        /// </summary>
        private static readonly string[] _locomotionJumpAnimationKeys =
        {
            "JumpWalkStart_LU", "JumpWalkStart_RU",
            "JumpRunStart_LU", "JumpRunStart_RU",
            "JumpWalk_LU_Land", "JumpWalk_RU_Land",
            "JumpRun_LU_Land", "JumpRun_RU_Land",
            "JumpWalk_LU_Land2Walk", "JumpWalk_RU_Land2Walk",
            "JumpRun_LU_Land2Run", "JumpRun_RU_Land2Run"
        };

        private sealed class JumpWiringContext
        {
            public readonly Dictionary<string, AnimationClip> Clips = new();
            public readonly Dictionary<string, MotionBakeData> Bakes = new();
            public ClipTransition TransitionTemplate;
            public JumpStateParams JumpParams;
            public MotionBakeData[] StageBakeSnapshot;
            public MotionBakeData WalkLoopBake;
            public MotionBakeData RunLoopBake;
            public TransitionAssetBase LegacyJumpTransition;
        }

        private static AnimationClip[] ResolveFallingClips()
        {
            var clips = new AnimationClip[_fallingClipNames.Length];
            for (int i = 0; i < _fallingClipNames.Length; i++)
            {
                clips[i] = ResolveExactClip(JumpSourceFbxPath, _fallingClipNames[i]);
            }
            return clips;
        }

        // ================================================================
        // 🔬 一次性資產遷移（2026-09-09）：跳躍物理來源 Mixamo → MAP。
        //     完成並驗收後整段刪除（CLAUDE.md Spike / Probe Exception）。
        // ================================================================

        // JumpStateParamsPath 已定義於本類別上方（接線入口共用），此處不重複宣告。
        // ⚠️ `_short`：apex 與長版相同（0.9535），但 takeoffDelay 0.0873 vs 0.4871
        // ⇒ 長版會讓角色按下跳躍後乾等 0.49 秒才離地。詳見 _jumpClipNames 的說明。
        private const string LaunchSourceBakePath =
            MotionAssetFolder + "/Bake_Jump_place_ALL_short.asset";

        /// <summary>
        /// 把 <c>JumpStateParams.Stages[0].Bake</c> 從 Mixamo 的 <c>Bake_Jump</c> 改指 MAP 的
        /// <c>Bake_Jump_place_ALL</c>，讓 gameplay 的垂直 launch physics 自此吃 MAP 自己的量測。
        ///
        /// 🔴 **寫入前強制驗證量測有效**：`JumpState.BuildStages` 的閘門是
        /// <c>AutoApexHeight &gt; 0 &amp;&amp; AutoCalculatedGravity &gt; 0</c>——不滿足就會靜默退化成
        /// fallback 常數。⇒ ⛔ **絕不把物理指向一個量測失敗的資產**，寧可中止並回報。
        ///
        /// ⚠️ 只動 <c>Stages[0].Bake</c> 一個欄位；三個 Multiplier 與其餘欄位一律不碰。
        /// 帶 <c>-verifyOnly</c> 時完全不寫入，只回讀現況。
        /// </summary>
        public static void PointJumpPhysicsAtLaunchSource()
        {
            try
            {
                bool verifyOnly = HasCommandLineFlag("-verifyOnly");
                Debug.Log($"[JumpPhysics] BEGIN|mode={(verifyOnly ? "verify-only" : "write")}");

                var bake = AssetDatabase.LoadAssetAtPath<MotionBakeData>(LaunchSourceBakePath);
                if (bake == null)
                    throw new System.InvalidOperationException($"找不到量測來源：{LaunchSourceBakePath}");

                Debug.Log(
                    $"[JumpPhysics] SOURCE|{LaunchSourceBakePath}|apex={bake.AutoApexHeight:F4}" +
                    $"|takeoffDelay={bake.AutoTakeoffDelay:F4}|airTime={bake.AutoAirTime:F4}" +
                    $"|g={bake.AutoCalculatedGravity:F4}");

                if (bake.AutoApexHeight <= 0f || bake.AutoCalculatedGravity <= 0f)
                {
                    throw new System.InvalidOperationException(
                        $"量測無效（apex={bake.AutoApexHeight}, g={bake.AutoCalculatedGravity}）——" +
                        "JumpState 會靜默退化成 fallback 常數。已中止，未修改任何資產。");
                }

                var jumpParams = AssetDatabase.LoadAssetAtPath<JumpStateParams>(JumpStateParamsPath);
                if (jumpParams == null)
                    throw new System.InvalidOperationException($"找不到：{JumpStateParamsPath}");

                var so = new SerializedObject(jumpParams);
                SerializedProperty stages = so.FindProperty("Stages");
                if (stages == null || !stages.isArray || stages.arraySize < 1)
                    throw new System.InvalidOperationException("JumpStateParams.Stages 不是陣列或為空。");

                SerializedProperty bakeProp = stages.GetArrayElementAtIndex(0).FindPropertyRelative("Bake");
                if (bakeProp == null)
                    throw new System.InvalidOperationException("Stages[0] 找不到 Bake 欄位。");

                Object before = bakeProp.objectReferenceValue;
                Debug.Log($"[JumpPhysics] BEFORE|stages={stages.arraySize}|bake={(before != null ? before.name : "<null>")}");

                if (!verifyOnly && before != bake)
                {
                    bakeProp.objectReferenceValue = bake;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(jumpParams);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                // 回讀驗證：重新載入資產而非沿用記憶體物件。
                var reloaded = AssetDatabase.LoadAssetAtPath<JumpStateParams>(JumpStateParamsPath);
                var verify = new SerializedObject(reloaded);
                Object after = verify.FindProperty("Stages").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("Bake").objectReferenceValue;
                Debug.Log($"[JumpPhysics] AFTER|bake={(after != null ? after.name : "<null>")}");

                Debug.Log($"[JumpPhysics] RESULT|SUCCESS|mode={(verifyOnly ? "verify-only" : "write")}");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[JumpPhysics] RESULT|FAILURE|message={exception.Message}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Unity batchmode 接線入口。預設只處理可覆蓋 Start／Falling／Normal Land／Hard Land／
        /// Land2Move 五條程式路徑的 Idle 最小集合；命令列帶 <c>-includeLocomotionTiers</c>
        /// 才擴到 Walk／Run × LU／RU。帶 <c>-verifyOnly</c> 時整條路徑只讀，不呼叫任何寫入 API。
        /// </summary>
        public static void WireJumpAnimationSet()
        {
            bool includeLocomotionTiers = HasCommandLineFlag("-includeLocomotionTiers");
            bool verifyOnly = HasCommandLineFlag("-verifyOnly");
            string[] targetKeys = GetJumpWiringKeys(includeLocomotionTiers);

            try
            {
                Debug.Log(
                    "[AnimationBatchOps] BEGIN|operation=WireJumpAnimationSet|" +
                    $"mode={(verifyOnly ? "verify" : "write")}|" +
                    $"tier={(includeLocomotionTiers ? "idle+locomotion" : "idle")}|keyCount={targetKeys.Length}");

                // 先把 17 支 clip／bake、Prefab 組件數、序列化欄位形狀、舊 Jump 物理與映射
                // 一次檢完。這個順序很重要：任何缺件都必須發生在 CreateAsset／SetDirty 之前。
                JumpWiringContext context = PreflightJumpWiring(targetKeys);

                if (!verifyOnly)
                {
                    EnsureJumpTransitionAssets(context, targetKeys);
                    WireJumpStateParams(context, includeLocomotionTiers);
                    WireJumpFacadeMappings(context, targetKeys);
                    AssetDatabase.SaveAssets();
                }

                VerifyJumpWiring(context, targetKeys, includeLocomotionTiers);
                Debug.Log(
                    "[AnimationBatchOps] RESULT|SUCCESS|operation=WireJumpAnimationSet|" +
                    $"mode={(verifyOnly ? "verify" : "write")}|" +
                    $"tier={(includeLocomotionTiers ? "idle+locomotion" : "idle")}|keyCount={targetKeys.Length}");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError(
                    "[AnimationBatchOps] RESULT|FAILURE|operation=WireJumpAnimationSet|" +
                    $"mode={(verifyOnly ? "verify" : "write")}|" +
                    $"message={SanitizeReportValue(exception.Message)}");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static JumpWiringContext PreflightJumpWiring(IReadOnlyList<string> targetKeys)
        {
            var context = new JumpWiringContext();

            if (!AssetDatabase.IsValidFolder(AnimationAssetFolder))
                throw new System.InvalidOperationException($"動畫資產資料夾不存在：{AnimationAssetFolder}");

            // 前置條件明定為完整 17 支，而不是只查本次五支：Idle 驗證成功後會直接用同一入口擴階，
            // 現在就抓出缺 bake 才不會把錯誤延後到第二階段、誤以為第一階段已證明素材完整。
            for (int i = 0; i < _jumpClipNames.Length; i++)
                ResolveAndValidateJumpBake(context, _jumpClipNames[i]);
            ResolveAndValidateJumpBake(context, _fallingClipNames[0]);

            TransitionAsset templateAsset = LoadRequiredAsset<TransitionAsset>(
                JumpTransitionTemplatePath, "既有 Jump TransitionAsset 範本");
            if (!templateAsset.HasTransition || templateAsset.Transition is not ClipTransition template ||
                template.Clip == null)
            {
                throw new System.InvalidOperationException(
                    $"'{JumpTransitionTemplatePath}' 必須維持為有效 ClipTransition；不猜測或手造 SerializeReference 型別。");
            }
            context.TransitionTemplate = template;
            Debug.Log(
                "[AnimationBatchOps] PRECHECK|SUCCESS|kind=transitionTemplate|" +
                $"type={template.GetType().FullName}|path={JumpTransitionTemplatePath}");

            for (int i = 0; i < targetKeys.Count; i++)
                ValidateExistingTransitionAsset(context, targetKeys[i]);

            context.JumpParams = LoadRequiredAsset<JumpStateParams>(JumpStateParamsPath, "JumpStateParams");
            context.StageBakeSnapshot = ValidateJumpParamsSchemaAndSnapshot(context.JumpParams);

            // 🔄 2026-09-09：守衛的**期望值**更新，形狀不變。
            // 它原本斷言「Stages[0].Bake 必須是 Mixamo 的 Bake_Jump」——那是本工具誕生時的前提
            // （當時的裁決是「接線只碰視覺，物理一行不動」）。使用者後來裁決**物理改吃 MAP 自己的量測**，
            // 由 `PointJumpPhysicsAtLaunchSource` 遷移到 `Bake_Jump_place_ALL_short`。
            // ⇒ 守衛要守的性質沒變（**接線工具不得偷改物理來源**），只是它該比對的目標換了人。
            // ⛔ 不要把守衛刪掉——它在這一輪就實際攔下了一次過時前提，正是它存在的理由。
            MotionBakeData launchSourceBake =
                LoadRequiredAsset<MotionBakeData>(LaunchSourceBakePath, "跳躍 launch data 來源 Bake");
            if (context.StageBakeSnapshot.Length == 0 || context.StageBakeSnapshot[0] != launchSourceBake)
            {
                string actual = context.StageBakeSnapshot.Length > 0 && context.StageBakeSnapshot[0] != null
                    ? context.StageBakeSnapshot[0].name
                    : "<null>";
                throw new System.InvalidOperationException(
                    "PHYSICS_GUARD 失敗：JumpStateParams.Stages[0].Bake 必須指向 " +
                    $"'{LaunchSourceBakePath}'，實際為 '{actual}'。" +
                    "若確實要換 launch 來源，請走 PointJumpPhysicsAtLaunchSource，不要由接線流程順手改。");
            }
            Debug.Log(
                "[AnimationBatchOps] PHYSICS_GUARD|SUCCESS|stage=0|" +
                $"bake={launchSourceBake.name}|apex={launchSourceBake.AutoApexHeight:F4}" +
                $"|takeoffDelay={launchSourceBake.AutoTakeoffDelay:F4}|stageCount={context.StageBakeSnapshot.Length}");

            // Loop bake 不是 17 支跳躍素材的一員；若它真的缺席，依需求保留空值並明確回報，
            // 不拿近似名稱或手填腳相頂替。現有專案的正確資產是 WalkFwdLoop／RunFwdLoop。
            context.WalkLoopBake = TryResolveLoopBake(WalkLoopBakePath, "Walk");
            context.RunLoopBake = TryResolveLoopBake(RunLoopBakePath, "Run");

            GameObject prefab = LoadRequiredAsset<GameObject>(CharacterPrefabPath, "X Bot prefab");
            AnimancerFacade[] facades = prefab.GetComponentsInChildren<AnimancerFacade>(true);
            if (facades.Length != 1)
            {
                throw new System.InvalidOperationException(
                    $"'{CharacterPrefabPath}' 必須恰好有 1 顆 AnimancerFacade，實際為 {facades.Length}。");
            }

            var serializedFacade = new SerializedObject(facades[0]);
            SerializedProperty mappings = RequireProperty(serializedFacade, "transitionMappings", "AnimancerFacade");
            context.LegacyJumpTransition = FindUniqueMappingTransition(mappings, "Jump", out int jumpCount);
            if (jumpCount != 1 || context.LegacyJumpTransition == null)
            {
                throw new System.InvalidOperationException(
                    $"'{CharacterPrefabPath}' 的既有 Jump 映射必須恰好一筆且 Transition 不為空；實際筆數={jumpCount}。");
            }
            Debug.Log(
                "[AnimationBatchOps] PRECHECK|SUCCESS|kind=facade|" +
                $"count=1|legacyJump=preserved|path={CharacterPrefabPath}");

            return context;
        }

        private static void ResolveAndValidateJumpBake(JumpWiringContext context, string clipName)
        {
            AnimationClip clip = ResolveExactClip(JumpSourceFbxPath, clipName);
            string bakePath = GetJumpBakePath(clipName);
            MotionBakeData bake = LoadRequiredAsset<MotionBakeData>(bakePath, $"{clipName} MotionBakeData");
            if (bake.Duration <= 0f || float.IsNaN(bake.Duration) || float.IsInfinity(bake.Duration))
                throw new System.InvalidOperationException($"'{bakePath}' 的 Duration 必須大於 0，實際為 {FormatFloat(bake.Duration)}。");
            if (bake.SourceClip != clip)
            {
                throw new System.InvalidOperationException(
                    $"'{bakePath}' 的 SourceClip 不是 FBX 內精確命名的 '{clipName}'；拒絕把錯 bake 接進變體表。");
            }

            context.Clips.Add(clipName, clip);
            context.Bakes.Add(clipName, bake);
            Debug.Log(
                "[AnimationBatchOps] PRECHECK|SUCCESS|kind=jumpBake|" +
                $"name={clipName}|duration={FormatFloat(bake.Duration)}|path={bakePath}");
        }

        private static MotionBakeData TryResolveLoopBake(string assetPath, string tier)
        {
            MotionBakeData bake = AssetDatabase.LoadAssetAtPath<MotionBakeData>(assetPath);
            bool valid = bake != null && bake.Duration > 0f &&
                         bake.FootPhaseCurve != null && bake.FootPhaseCurve.length > 0;
            if (!valid)
            {
                Debug.LogWarning(
                    "[AnimationBatchOps] LOOP_BAKE|MISSING|" +
                    $"tier={tier}|action=leaveEmpty|path={assetPath}");
                return null;
            }

            Debug.Log(
                "[AnimationBatchOps] LOOP_BAKE|SUCCESS|" +
                $"tier={tier}|duration={FormatFloat(bake.Duration)}|path={assetPath}");
            return bake;
        }

        private static MotionBakeData[] ValidateJumpParamsSchemaAndSnapshot(JumpStateParams jumpParams)
        {
            var serializedParams = new SerializedObject(jumpParams);
            SerializedProperty stages = RequireProperty(serializedParams, "Stages", "JumpStateParams");
            if (!stages.isArray || stages.arraySize <= 0)
                throw new System.InvalidOperationException("JumpStateParams.Stages 必須至少有第 0 段；接線工具不建立或改寫物理段。");

            var snapshot = new MotionBakeData[stages.arraySize];
            for (int i = 0; i < stages.arraySize; i++)
            {
                SerializedProperty bake = RequireRelativeProperty(
                    stages.GetArrayElementAtIndex(i), "Bake", $"JumpStateParams.Stages[{i}]");
                snapshot[i] = bake.objectReferenceValue as MotionBakeData;
            }

            SerializedProperty table = RequireProperty(serializedParams, "animationVariants", "JumpStateParams");
            ValidateVariantSetSchema(RequireRelativeProperty(table, "start", "animationVariants.start"), "start");
            ValidateVariantSchema(RequireRelativeProperty(table, "falling", "animationVariants.falling"), "falling");
            ValidateVariantSetSchema(RequireRelativeProperty(table, "normalLand", "animationVariants.normalLand"), "normalLand");
            ValidateVariantSetSchema(RequireRelativeProperty(table, "normalLandToMove", "animationVariants.normalLandToMove"), "normalLandToMove");
            ValidateVariantSchema(RequireRelativeProperty(table, "hardLand", "animationVariants.hardLand"), "hardLand");
            RequireProperty(serializedParams, "walkLoopBakeData", "JumpStateParams");
            RequireProperty(serializedParams, "runLoopBakeData", "JumpStateParams");
            RequireProperty(serializedParams, "hardLandingSpeed", "JumpStateParams");
            return snapshot;
        }

        private static void ValidateVariantSetSchema(SerializedProperty set, string label)
        {
            ValidateVariantSchema(RequireRelativeProperty(set, "idle", label), label + ".idle");
            SerializedProperty walk = RequireRelativeProperty(set, "walk", label);
            SerializedProperty run = RequireRelativeProperty(set, "run", label);
            if (!walk.isArray || !run.isArray)
                throw new System.InvalidOperationException($"Jump 動畫變體 '{label}' 的 walk/run 必須維持為陣列。");
        }

        private static void ValidateVariantSchema(SerializedProperty variant, string label)
        {
            RequireRelativeProperty(variant, "bakeData", label);
            RequireRelativeProperty(variant, "animationKey", label);
        }

        private static void ValidateExistingTransitionAsset(JumpWiringContext context, string key)
        {
            string assetPath = GetJumpTransitionPath(key);
            UnityEngine.Object existingMainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existingMainAsset == null)
            {
                Debug.Log($"[AnimationBatchOps] PRECHECK|SUCCESS|kind=transitionPath|key={key}|state=missing|path={assetPath}");
                return;
            }

            if (existingMainAsset is not TransitionAsset asset || !asset.HasTransition ||
                asset.Transition is not ClipTransition transition || transition.Clip != context.Clips[key])
            {
                throw new System.InvalidOperationException(
                    $"'{assetPath}' 已存在但不是引用 '{key}' FBX 子 clip 的 ClipTransition；依規格不得覆寫既有資產。");
            }

            Debug.Log($"[AnimationBatchOps] PRECHECK|SUCCESS|kind=transitionPath|key={key}|state=reusable|path={assetPath}");
        }

        private static void EnsureJumpTransitionAssets(JumpWiringContext context, IReadOnlyList<string> targetKeys)
        {
            for (int i = 0; i < targetKeys.Count; i++)
            {
                string key = targetKeys[i];
                string assetPath = GetJumpTransitionPath(key);
                TransitionAsset asset = AssetDatabase.LoadAssetAtPath<TransitionAsset>(assetPath);
                if (asset != null)
                {
                    Debug.Log($"[AnimationBatchOps] TRANSITION|SUCCESS|key={key}|action=reused|path={assetPath}");
                    continue;
                }

                // Jump.asset 與 RunStop_LU.asset 的實際 YAML 都是 ClipTransition。這裡走 Animancer
                // 公開 Clone API 複製 Jump 範本（含 fade/speed/events/start time），再只換 Clip；
                // 由 Unity 自己序列化 [SerializeReference] 型別中繼資料，絕不手寫 rid/type YAML。
                var cloneContext = new CloneContext();
                var transition = (ClipTransition)context.TransitionTemplate.Clone(cloneContext);
                transition.Clip = context.Clips[key];

                asset = ScriptableObject.CreateInstance<TransitionAsset>();
                asset.name = key;
                asset.Transition = transition;
                AssetDatabase.CreateAsset(asset, assetPath);
                Debug.Log($"[AnimationBatchOps] TRANSITION|SUCCESS|key={key}|action=created|path={assetPath}");
            }

            // Prefab 的 object reference 必須指向已正式落盤的資產；先 SaveAssets 可避免把尚無
            // persistent path 的暫存 ScriptableObject 寫進 Prefab。
            AssetDatabase.SaveAssets();
        }

        private static void WireJumpStateParams(JumpWiringContext context, bool includeLocomotionTiers)
        {
            var serializedParams = new SerializedObject(context.JumpParams);
            serializedParams.Update();
            SerializedProperty table = RequireProperty(serializedParams, "animationVariants", "JumpStateParams");

            SerializedProperty start = RequireRelativeProperty(table, "start", "animationVariants.start");
            SerializedProperty normalLand = RequireRelativeProperty(table, "normalLand", "animationVariants.normalLand");
            SerializedProperty normalLandToMove = RequireRelativeProperty(
                table, "normalLandToMove", "animationVariants.normalLandToMove");
            SerializedProperty hardLand = RequireRelativeProperty(table, "hardLand", "animationVariants.hardLand");

            SetVariant(RequireRelativeProperty(start, "idle", "start.idle"), context, "JumpIdleStart");
            SetVariant(RequireRelativeProperty(table, "falling", "animationVariants.falling"), context, "FallingLoop");
            SetVariant(RequireRelativeProperty(normalLand, "idle", "normalLand.idle"), context, "JumpIdleLand");
            SetVariant(
                RequireRelativeProperty(normalLandToMove, "idle", "normalLandToMove.idle"),
                context,
                "JumpIdleLand2Walk");
            // MAP 只有 JumpIdleLandHard：資料形狀已收斂成單一 variant，不能再藉由 tier／腳相
            // 複製 alias，否則工具會重新製造 Runtime 已移除的「Hard 且續走」假維度。
            SetVariant(hardLand, context, "JumpIdleLandHard");

            if (includeLocomotionTiers)
            {
                SetVariantArray(RequireRelativeProperty(start, "walk", "start.walk"), context,
                    "JumpWalkStart_LU", "JumpWalkStart_RU");
                SetVariantArray(RequireRelativeProperty(start, "run", "start.run"), context,
                    "JumpRunStart_LU", "JumpRunStart_RU");
                SetVariantArray(RequireRelativeProperty(normalLand, "walk", "normalLand.walk"), context,
                    "JumpWalk_LU_Land", "JumpWalk_RU_Land");
                SetVariantArray(RequireRelativeProperty(normalLand, "run", "normalLand.run"), context,
                    "JumpRun_LU_Land", "JumpRun_RU_Land");
                SetVariantArray(RequireRelativeProperty(normalLandToMove, "walk", "normalLandToMove.walk"), context,
                    "JumpWalk_LU_Land2Walk", "JumpWalk_RU_Land2Walk");
                SetVariantArray(RequireRelativeProperty(normalLandToMove, "run", "normalLandToMove.run"), context,
                    "JumpRun_LU_Land2Run", "JumpRun_RU_Land2Run");

                RequireProperty(serializedParams, "walkLoopBakeData", "JumpStateParams").objectReferenceValue =
                    context.WalkLoopBake;
                RequireProperty(serializedParams, "runLoopBakeData", "JumpStateParams").objectReferenceValue =
                    context.RunLoopBake;
            }

            serializedParams.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(context.JumpParams);
            AssertStageBakesUnchanged(context);
            Debug.Log(
                "[AnimationBatchOps] PARAMS|SUCCESS|action=updated|" +
                $"tier={(includeLocomotionTiers ? "idle+locomotion" : "idle")}|path={JumpStateParamsPath}");
        }

        private static void SetVariant(SerializedProperty variant, JumpWiringContext context, string key)
        {
            RequireRelativeProperty(variant, "bakeData", key).objectReferenceValue = context.Bakes[key];
            RequireRelativeProperty(variant, "animationKey", key).stringValue = key;
        }

        private static void SetVariantArray(
            SerializedProperty variants,
            JumpWiringContext context,
            string firstKey,
            string secondKey)
        {
            variants.arraySize = 2;
            SetVariant(variants.GetArrayElementAtIndex(0), context, firstKey);
            SetVariant(variants.GetArrayElementAtIndex(1), context, secondKey);
        }

        private static void WireJumpFacadeMappings(JumpWiringContext context, IReadOnlyList<string> targetKeys)
        {
            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(CharacterPrefabPath);
                AnimancerFacade[] facades = prefabRoot.GetComponentsInChildren<AnimancerFacade>(true);
                if (facades.Length != 1)
                    throw new System.InvalidOperationException($"Prefab Contents 內 AnimancerFacade 數量改變：{facades.Length}。");

                var serializedFacade = new SerializedObject(facades[0]);
                serializedFacade.Update();
                SerializedProperty mappings = RequireProperty(serializedFacade, "transitionMappings", "AnimancerFacade");

                for (int i = 0; i < targetKeys.Count; i++)
                {
                    string key = targetKeys[i];
                    TransitionAsset transition = LoadRequiredAsset<TransitionAsset>(
                        GetJumpTransitionPath(key), key + " TransitionAsset");
                    string action = UpsertFacadeMapping(mappings, key, transition);
                    Debug.Log($"[AnimationBatchOps] FACADE|SUCCESS|key={key}|action={action}|path={CharacterPrefabPath}");
                }

                serializedFacade.ApplyModifiedPropertiesWithoutUndo();
                TransitionAssetBase legacyJump = FindUniqueMappingTransition(mappings, "Jump", out int jumpCount);
                if (jumpCount != 1 || legacyJump != context.LegacyJumpTransition)
                    throw new System.InvalidOperationException("LEGACY_GUARD 失敗：接線過程改動了既有 Jump 映射。");

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, CharacterPrefabPath);
            }
            finally
            {
                if (prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static string UpsertFacadeMapping(
            SerializedProperty mappings,
            string stateKey,
            TransitionAssetBase transition)
        {
            int firstIndex = -1;
            int duplicateCount = 0;
            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty element = mappings.GetArrayElementAtIndex(i);
                string existingKey = RequireRelativeProperty(element, "StateKey", "TransitionMapping").stringValue;
                if (!string.Equals(existingKey, stateKey, System.StringComparison.Ordinal)) continue;
                if (firstIndex < 0) firstIndex = i;
                else duplicateCount++;
            }

            // 冪等不只是不再 append；若舊批次或人工曾留下同名重複項，也收斂為第一筆。
            // 反向刪除可避免前方 index 位移，且只碰本次 target key，其他既有映射原封不動。
            for (int i = mappings.arraySize - 1; i > firstIndex && duplicateCount > 0; i--)
            {
                SerializedProperty element = mappings.GetArrayElementAtIndex(i);
                string existingKey = RequireRelativeProperty(element, "StateKey", "TransitionMapping").stringValue;
                if (!string.Equals(existingKey, stateKey, System.StringComparison.Ordinal)) continue;
                mappings.DeleteArrayElementAtIndex(i);
                duplicateCount--;
            }

            if (firstIndex < 0)
            {
                firstIndex = mappings.arraySize;
                mappings.InsertArrayElementAtIndex(firstIndex);
            }

            SerializedProperty mapping = mappings.GetArrayElementAtIndex(firstIndex);
            RequireRelativeProperty(mapping, "StateKey", stateKey).stringValue = stateKey;
            RequireRelativeProperty(mapping, "Transition", stateKey).objectReferenceValue = transition;
            return firstIndex == mappings.arraySize - 1 ? "added" : "updated";
        }

        private static void VerifyJumpWiring(
            JumpWiringContext context,
            IReadOnlyList<string> targetKeys,
            bool includeLocomotionTiers)
        {
            for (int i = 0; i < targetKeys.Count; i++)
            {
                string key = targetKeys[i];
                string assetPath = GetJumpTransitionPath(key);
                TransitionAsset asset = AssetDatabase.LoadAssetAtPath<TransitionAsset>(assetPath);
                if (asset == null || !asset.HasTransition || asset.Transition is not ClipTransition transition ||
                    transition.Clip != context.Clips[key])
                {
                    throw new System.InvalidOperationException(
                        $"VERIFY transition 失敗：'{assetPath}' 不存在或未引用 '{key}' FBX 子 clip。");
                }
                Debug.Log($"[AnimationBatchOps] VERIFY_TRANSITION|SUCCESS|key={key}|path={assetPath}");
            }

            VerifyJumpParams(context, includeLocomotionTiers);
            VerifyFacadeMappings(context, targetKeys);
        }

        private static void VerifyJumpParams(JumpWiringContext context, bool includeLocomotionTiers)
        {
            JumpStateParams jumpParams = LoadRequiredAsset<JumpStateParams>(JumpStateParamsPath, "JumpStateParams");
            var serializedParams = new SerializedObject(jumpParams);
            SerializedProperty table = RequireProperty(serializedParams, "animationVariants", "JumpStateParams");
            SerializedProperty start = RequireRelativeProperty(table, "start", "animationVariants.start");
            SerializedProperty normalLand = RequireRelativeProperty(table, "normalLand", "animationVariants.normalLand");
            SerializedProperty normalLandToMove = RequireRelativeProperty(
                table, "normalLandToMove", "animationVariants.normalLandToMove");
            SerializedProperty hardLand = RequireRelativeProperty(table, "hardLand", "animationVariants.hardLand");

            VerifyVariant(RequireRelativeProperty(start, "idle", "start.idle"), context, "JumpIdleStart", "start.idle");
            VerifyVariant(RequireRelativeProperty(table, "falling", "animationVariants.falling"), context, "FallingLoop", "falling");
            VerifyVariant(RequireRelativeProperty(normalLand, "idle", "normalLand.idle"), context, "JumpIdleLand", "normalLand.idle");
            VerifyVariant(RequireRelativeProperty(normalLandToMove, "idle", "normalLandToMove.idle"), context,
                "JumpIdleLand2Walk", "normalLandToMove.idle");
            VerifyVariant(hardLand, context, "JumpIdleLandHard", "hardLand");

            if (includeLocomotionTiers)
            {
                VerifyVariantArray(RequireRelativeProperty(start, "walk", "start.walk"), context, "start.walk",
                    "JumpWalkStart_LU", "JumpWalkStart_RU");
                VerifyVariantArray(RequireRelativeProperty(start, "run", "start.run"), context, "start.run",
                    "JumpRunStart_LU", "JumpRunStart_RU");
                VerifyVariantArray(RequireRelativeProperty(normalLand, "walk", "normalLand.walk"), context, "normalLand.walk",
                    "JumpWalk_LU_Land", "JumpWalk_RU_Land");
                VerifyVariantArray(RequireRelativeProperty(normalLand, "run", "normalLand.run"), context, "normalLand.run",
                    "JumpRun_LU_Land", "JumpRun_RU_Land");
                VerifyVariantArray(RequireRelativeProperty(normalLandToMove, "walk", "normalLandToMove.walk"), context,
                    "normalLandToMove.walk", "JumpWalk_LU_Land2Walk", "JumpWalk_RU_Land2Walk");
                VerifyVariantArray(RequireRelativeProperty(normalLandToMove, "run", "normalLandToMove.run"), context,
                    "normalLandToMove.run", "JumpRun_LU_Land2Run", "JumpRun_RU_Land2Run");
                MotionBakeData actualWalk = RequireProperty(
                    serializedParams, "walkLoopBakeData", "JumpStateParams").objectReferenceValue as MotionBakeData;
                MotionBakeData actualRun = RequireProperty(
                    serializedParams, "runLoopBakeData", "JumpStateParams").objectReferenceValue as MotionBakeData;
                if (actualWalk != context.WalkLoopBake || actualRun != context.RunLoopBake)
                    throw new System.InvalidOperationException("VERIFY JumpStateParams loop bake 引用不符。");
                Debug.Log(
                    "[AnimationBatchOps] VERIFY_LOOP_BAKE|SUCCESS|" +
                    $"walk={(actualWalk != null ? WalkLoopBakePath : "empty")}|" +
                    $"run={(actualRun != null ? RunLoopBakePath : "empty")}");
            }

            AssertStageBakesUnchanged(context);
            Debug.Log($"[AnimationBatchOps] VERIFY_PARAMS|SUCCESS|path={JumpStateParamsPath}");
        }

        private static void VerifyVariant(
            SerializedProperty variant,
            JumpWiringContext context,
            string expectedKey,
            string slot)
        {
            MotionBakeData actualBake = RequireRelativeProperty(variant, "bakeData", slot).objectReferenceValue as MotionBakeData;
            string actualKey = RequireRelativeProperty(variant, "animationKey", slot).stringValue;
            if (actualBake != context.Bakes[expectedKey] ||
                !string.Equals(actualKey, expectedKey, System.StringComparison.Ordinal))
            {
                throw new System.InvalidOperationException(
                    $"VERIFY 變體 '{slot}' 不符：expectedKey={expectedKey}。");
            }
            Debug.Log($"[AnimationBatchOps] VERIFY_VARIANT|SUCCESS|slot={slot}|key={expectedKey}");
        }

        private static void VerifyVariantArray(
            SerializedProperty variants,
            JumpWiringContext context,
            string slot,
            string firstKey,
            string secondKey)
        {
            if (variants.arraySize != 2)
                throw new System.InvalidOperationException($"VERIFY 變體 '{slot}' 必須恰好兩格，實際為 {variants.arraySize}。");
            VerifyVariant(variants.GetArrayElementAtIndex(0), context, firstKey, slot + "[0]");
            VerifyVariant(variants.GetArrayElementAtIndex(1), context, secondKey, slot + "[1]");
        }

        private static void AssertStageBakesUnchanged(JumpWiringContext context)
        {
            var serializedParams = new SerializedObject(context.JumpParams);
            SerializedProperty stages = RequireProperty(serializedParams, "Stages", "JumpStateParams");
            if (stages.arraySize != context.StageBakeSnapshot.Length)
                throw new System.InvalidOperationException("PHYSICS_GUARD 失敗：接線過程改變了 JumpStateParams.Stages 長度。");

            for (int i = 0; i < stages.arraySize; i++)
            {
                MotionBakeData actual = RequireRelativeProperty(
                    stages.GetArrayElementAtIndex(i), "Bake", $"Stages[{i}]").objectReferenceValue as MotionBakeData;
                if (actual != context.StageBakeSnapshot[i])
                    throw new System.InvalidOperationException($"PHYSICS_GUARD 失敗：接線過程改變了 Stages[{i}].Bake。");
            }
            Debug.Log(
                "[AnimationBatchOps] PHYSICS_GUARD|SUCCESS|action=verifiedUnchanged|" +
                $"stageCount={stages.arraySize}|stage0={LegacyJumpBakePath}");
        }

        private static void VerifyFacadeMappings(JumpWiringContext context, IReadOnlyList<string> targetKeys)
        {
            GameObject prefab = LoadRequiredAsset<GameObject>(CharacterPrefabPath, "X Bot prefab");
            AnimancerFacade[] facades = prefab.GetComponentsInChildren<AnimancerFacade>(true);
            if (facades.Length != 1)
                throw new System.InvalidOperationException($"VERIFY AnimancerFacade 數量不等於 1：{facades.Length}。");

            var serializedFacade = new SerializedObject(facades[0]);
            SerializedProperty mappings = RequireProperty(serializedFacade, "transitionMappings", "AnimancerFacade");
            for (int i = 0; i < targetKeys.Count; i++)
            {
                string key = targetKeys[i];
                TransitionAssetBase actual = FindUniqueMappingTransition(mappings, key, out int count);
                TransitionAsset expected = AssetDatabase.LoadAssetAtPath<TransitionAsset>(GetJumpTransitionPath(key));
                if (count != 1 || actual != expected)
                    throw new System.InvalidOperationException($"VERIFY Facade key '{key}' 必須恰好一筆且指向對應 TransitionAsset。");
                Debug.Log($"[AnimationBatchOps] VERIFY_FACADE|SUCCESS|key={key}|count=1|path={CharacterPrefabPath}");
            }

            TransitionAssetBase legacyJump = FindUniqueMappingTransition(mappings, "Jump", out int jumpCount);
            if (jumpCount != 1 || legacyJump != context.LegacyJumpTransition)
                throw new System.InvalidOperationException("LEGACY_GUARD 失敗：既有 Jump 映射未被完整保留。");
            Debug.Log($"[AnimationBatchOps] LEGACY_GUARD|SUCCESS|key=Jump|count=1|path={CharacterPrefabPath}");
        }

        private static TransitionAssetBase FindUniqueMappingTransition(
            SerializedProperty mappings,
            string stateKey,
            out int count)
        {
            count = 0;
            TransitionAssetBase result = null;
            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty mapping = mappings.GetArrayElementAtIndex(i);
                string key = RequireRelativeProperty(mapping, "StateKey", "TransitionMapping").stringValue;
                if (!string.Equals(key, stateKey, System.StringComparison.Ordinal)) continue;
                count++;
                if (count == 1)
                {
                    result = RequireRelativeProperty(
                        mapping, "Transition", "TransitionMapping").objectReferenceValue as TransitionAssetBase;
                }
            }
            return result;
        }

        private static SerializedProperty RequireProperty(SerializedObject serializedObject, string name, string owner)
        {
            SerializedProperty property = serializedObject.FindProperty(name);
            return property != null
                ? property
                : throw new System.InvalidOperationException($"{owner} 找不到必要序列化欄位 '{name}'。");
        }

        private static SerializedProperty RequireRelativeProperty(SerializedProperty parent, string name, string owner)
        {
            SerializedProperty property = parent?.FindPropertyRelative(name);
            return property != null
                ? property
                : throw new System.InvalidOperationException($"{owner} 找不到必要序列化欄位 '{name}'。");
        }

        private static string[] GetJumpWiringKeys(bool includeLocomotionTiers)
        {
            if (!includeLocomotionTiers)
                return (string[])_idleJumpAnimationKeys.Clone();

            var keys = new string[_idleJumpAnimationKeys.Length + _locomotionJumpAnimationKeys.Length];
            System.Array.Copy(_idleJumpAnimationKeys, keys, _idleJumpAnimationKeys.Length);
            System.Array.Copy(
                _locomotionJumpAnimationKeys,
                0,
                keys,
                _idleJumpAnimationKeys.Length,
                _locomotionJumpAnimationKeys.Length);
            return keys;
        }

        private static bool HasCommandLineFlag(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string GetJumpBakePath(string clipName)
            => MotionAssetFolder + "/Bake_" + clipName + ".asset";

        private static string GetJumpTransitionPath(string stateKey)
            => AnimationAssetFolder + "/" + stateKey + ".asset";

        /// <summary>
        /// Unity batchmode entry point：對跳躍分段子 clip 套 **Jump 家族** preset 後批次烘焙。
        ///
        /// ⚠️ **與 <see cref="ApplyStrafeSopAndBake"/> 共用同一支 FBX，但 preset 幾乎每項都相反**：
        /// Jump 家族＝XZ ✅／Y ❌／Based Upon **Feet**／Rot ✅／Loop ❌；
        /// Locomotion-位移＝XZ ❌／Y ✅／Original／Rot ✅／Loop ✅。
        /// ⇒ 只能走 per-clip 的明確清單入口（<c>MotionClipImportSOP.ApplyJumpTo</c>），
        /// ⛔ **絕不可整檔套用**，否則會把 strafe 那批剛套好的設定灌掉（dev-spec §0.4 規則 1）。
        /// </summary>
        public static void ApplyJumpSopAndBake()
        {
            try
            {
                Debug.Log($"[AnimationBatchOps] BEGIN|operation=ApplyJumpSopAndBake|clipCount={_jumpClipNames.Length}");

                AnimationClip[] clips = ResolveJumpClips();

                if (!MotionClipImportSOP.ApplyJumpTo(clips, out string sopReport))
                    throw new System.InvalidOperationException($"Jump SOP 失敗：{sopReport}");

                Debug.Log($"[AnimationBatchOps] SOP|SUCCESS\n{sopReport}");

                // 🔴 FallingLoop **不屬於** Jump 家族，必須分開套（2026-09-09）：
                // Jump preset 是 loopTime:false，套在空中循環上會讓它播完就停。
                // 它在本專案只當純視覺（垂直運動歸物理，ADR-002）⇒ 走 Locomotion-原地：
                // XZ／Y／Rot 全 Bake Into Pose、不萃取 root motion、可循環。
                AnimationClip[] fallingClips = ResolveFallingClips();
                if (!MotionClipImportSOP.ApplyLocomotionInPlaceTo(fallingClips, out string fallingSopReport))
                    throw new System.InvalidOperationException($"FallingLoop SOP 失敗：{fallingSopReport}");

                Debug.Log($"[AnimationBatchOps] SOP_FALLING|SUCCESS\n{fallingSopReport}");

                AssetDatabase.Refresh();

                // SaveAndReimport 會重建 FBX 子資產；Refresh 後重新解析，避免沿用失效引用。
                clips = ResolveJumpClips();
                fallingClips = ResolveFallingClips();

                // 兩組 preset 不同但烘焙路徑相同，合併成一次批次。
                var allClips = new AnimationClip[clips.Length + fallingClips.Length];
                System.Array.Copy(clips, allClips, clips.Length);
                System.Array.Copy(fallingClips, 0, allClips, clips.Length, fallingClips.Length);
                clips = allClips;

                GameObject characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
                if (characterPrefab == null)
                    throw new System.InvalidOperationException($"找不到採樣角色 prefab：{CharacterPrefabPath}");

                if (!MotionBakeEditor.BatchBake(characterPrefab, SampleRate, clips, out string bakeReport))
                    throw new System.InvalidOperationException($"批次烘焙失敗：{bakeReport}");

                Debug.Log($"[AnimationBatchOps] BAKE|SUCCESS\n{bakeReport}");
                LogMachineReadableBakeReport(clips);
                Debug.Log($"[AnimationBatchOps] RESULT|SUCCESS|clipCount={clips.Length}|sampleRate={SampleRate:F0}");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[AnimationBatchOps] RESULT|FAILURE|message={SanitizeReportValue(exception.Message)}");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Unity batchmode entry point. Apply the Locomotion-位移 import preset, rebake, report, then exit.
        /// </summary>
        public static void ApplyStrafeSopAndBake()
        {
            try
            {
                Debug.Log($"[AnimationBatchOps] BEGIN|operation=ApplyStrafeSopAndBake|clipCount={_requiredClipNames.Length}");

                if (!TryResolveRequiredClips(out AnimationClip[] clips, out string resolveError))
                    throw new System.InvalidOperationException(resolveError);

                if (!MotionClipImportSOP.ApplyLocomotionTravelTo(clips, out string sopReport))
                    throw new System.InvalidOperationException($"Locomotion-位移 SOP 失敗：{sopReport}");

                Debug.Log($"[AnimationBatchOps] SOP|SUCCESS\n{sopReport}");

                AssetDatabase.Refresh();

                // SaveAndReimport 會重建 FBX 子資產；Refresh 後重新依白名單解析，避免沿用失效引用。
                if (!TryResolveRequiredClips(out clips, out resolveError))
                    throw new System.InvalidOperationException($"SOP 重匯入後重新解析失敗：{resolveError}");

                GameObject characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
                if (characterPrefab == null)
                    throw new System.InvalidOperationException($"找不到採樣角色 prefab：{CharacterPrefabPath}");

                if (!MotionBakeEditor.BatchBake(characterPrefab, SampleRate, clips, out string bakeReport))
                    throw new System.InvalidOperationException($"批次烘焙失敗：{bakeReport}");

                Debug.Log($"[AnimationBatchOps] BAKE|SUCCESS\n{bakeReport}");
                LogMachineReadableBakeReport(clips);
                Debug.Log($"[AnimationBatchOps] RESULT|SUCCESS|clipCount={clips.Length}|sampleRate={SampleRate:F0}");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[AnimationBatchOps] RESULT|FAILURE|message={SanitizeReportValue(exception.Message)}");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Unity batchmode entry point. Generate both throwaway 2D locomotion measurement prototypes, report, then exit.
        /// Uses only Animancer's public construction API（ref-return properties included）and never writes its private
        /// serialized fields via reflection. This is a CLAUDE.md Spike / Probe Exception, not a durable B11 automation.
        /// </summary>
        public static void GenerateLocomotion2DPrototypes()
        {
            try
            {
                Debug.Log("[AnimationBatchOps] BEGIN|operation=GenerateLocomotion2DPrototypes|assetCount=2");

                AnimationClip[] clips = ResolvePrototypeClips();
                MotionBakeData[] bakeData = ResolveBakeData(clips);
                float[] nativeSpeeds = ResolveNativeSpeeds(clips, bakeData);
                float moveSpeed = ResolveRuntimeMoveSpeed();
                float runRingRadius = nativeSpeeds[1] / moveSpeed;
                EnsurePositiveFinite(runRingRadius, "RunRing 半徑");

                StringAsset moveX = LoadRequiredAsset<StringAsset>(MoveXAssetPath, "MoveX StringAsset");
                StringAsset moveZ = LoadRequiredAsset<StringAsset>(MoveZAssetPath, "MoveZ StringAsset");

                Vector2[] fullRingThresholds = CreateThresholds(1f);
                float[] fullRingSpeeds = CreatePlaybackSpeeds(moveSpeed, nativeSpeeds);
                Vector2[] runRingThresholds = CreateThresholds(runRingRadius);
                // 代數上等於 runRingRadius * moveSpeed；直接使用前進 native speed 可避免
                // float 的除後再乘誤差，確保 RunFwdLoop 的 prototype playback speed 精確為 1。
                float[] runRingSpeeds = CreatePlaybackSpeeds(nativeSpeeds[1], nativeSpeeds);

                WritePrototype(FullRingAssetPath, clips, fullRingThresholds, fullRingSpeeds, moveX, moveZ);
                WritePrototype(RunRingAssetPath, clips, runRingThresholds, runRingSpeeds, moveX, moveZ);
                AssetDatabase.SaveAssets();

                LogPrototypeReport(
                    "FullRing", FullRingAssetPath, clips, fullRingThresholds, nativeSpeeds, fullRingSpeeds);
                LogPrototypeReport(
                    "RunRing", RunRingAssetPath, clips, runRingThresholds, nativeSpeeds, runRingSpeeds);

                Debug.Log(
                    "[AnimationBatchOps] RESULT|SUCCESS|assetCount=2|" +
                    $"moveSpeed={FormatFloat(moveSpeed)}|runRingRadius={FormatFloat(runRingRadius)}");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[AnimationBatchOps] RESULT|FAILURE|message={SanitizeReportValue(exception.Message)}");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static AnimationClip[] ResolvePrototypeClips()
        {
            var clips = new AnimationClip[1 + _prototypeDirectionalClipNames.Length];
            clips[0] = ResolveIdleClipFromBaseline();
            clips[1] = ResolveExactClip(BaseMovementFbxPath, _prototypeDirectionalClipNames[0]);

            for (int i = 1; i < _prototypeDirectionalClipNames.Length; i++)
                clips[i + 1] = ResolveExactClip(SourceFbxPath, _prototypeDirectionalClipNames[i]);

            return clips;
        }

        private static AnimationClip ResolveIdleClipFromBaseline()
        {
            TransitionAsset asset = LoadRequiredAsset<TransitionAsset>(LocomotionAssetPath, "1D Locomotion 基準");
            if (!asset.HasTransition || asset.Transition is not LinearMixerTransition mixer)
            {
                throw new System.InvalidOperationException(
                    $"'{LocomotionAssetPath}' 必須維持為含 LinearMixerTransition 的 1D 基準；未寫入該資產。");
            }

            UnityEngine.Object[] animations = mixer.Animations;
            float[] thresholds = mixer.Thresholds;
            int count = animations?.Length ?? 0;
            AnimationClip idle = null;

            for (int i = 0; i < count; i++)
            {
                if (thresholds == null || i >= thresholds.Length || !Mathf.Approximately(thresholds[i], 0f))
                    continue;
                if (animations[i] is not AnimationClip candidate)
                    throw new System.InvalidOperationException("Locomotion 1D 基準的 (0) child 不是 AnimationClip。");
                if (idle != null)
                    throw new System.InvalidOperationException("Locomotion 1D 基準有多支 threshold=0 的 AnimationClip。");

                idle = candidate;
            }

            return idle != null
                ? idle
                : throw new System.InvalidOperationException("Locomotion 1D 基準找不到 threshold=0 的 Idle clip。");
        }

        private static AnimationClip ResolveExactClip(string assetPath, string clipName)
        {
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
            AnimationClip result = null;

            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is not AnimationClip clip ||
                    !string.Equals(clip.name, clipName, System.StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new System.InvalidOperationException($"'{assetPath}' 內有重複的精確 clip 名稱：{clipName}");

                result = clip;
            }

            return result != null
                ? result
                : throw new System.InvalidOperationException($"'{assetPath}' 找不到精確命名 clip：{clipName}");
        }

        private static MotionBakeData[] ResolveBakeData(IReadOnlyList<AnimationClip> clips)
        {
            var required = new HashSet<AnimationClip>(clips);
            var bakeByClip = new Dictionary<AnimationClip, MotionBakeData>();
            string[] guids = AssetDatabase.FindAssets("t:MotionBakeData", new[] { MotionAssetFolder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MotionBakeData bake = AssetDatabase.LoadAssetAtPath<MotionBakeData>(path);
                if (bake == null || bake.SourceClip == null || !required.Contains(bake.SourceClip))
                    continue;
                if (bakeByClip.ContainsKey(bake.SourceClip))
                {
                    throw new System.InvalidOperationException(
                        $"clip '{bake.SourceClip.name}' 對應到多份 MotionBakeData；無法唯一決定 native speed。");
                }

                bakeByClip.Add(bake.SourceClip, bake);
            }

            var resolved = new MotionBakeData[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                if (!bakeByClip.TryGetValue(clips[i], out resolved[i]))
                {
                    throw new System.InvalidOperationException(
                        $"在 '{MotionAssetFolder}' 找不到 SourceClip='{clips[i].name}' 的 MotionBakeData；中止生成。");
                }
            }

            return resolved;
        }

        private static float[] ResolveNativeSpeeds(
            IReadOnlyList<AnimationClip> clips, IReadOnlyList<MotionBakeData> bakeData)
        {
            var nativeSpeeds = new float[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                nativeSpeeds[i] = bakeData[i].GetRepresentativeSpeed();
                if (i > 0)
                    EnsurePositiveFinite(nativeSpeeds[i], $"{clips[i].name} native speed");
            }

            return nativeSpeeds;
        }

        private static float ResolveRuntimeMoveSpeed()
        {
            GameObject prefab = LoadRequiredAsset<GameObject>(CharacterPrefabPath, "X Bot prefab");
            MotionDriver driver = prefab.GetComponentInChildren<MotionDriver>(true);
            if (driver == null)
                throw new System.InvalidOperationException($"'{CharacterPrefabPath}' 找不到 MotionDriver。");

            var serializedDriver = new SerializedObject(driver);
            SerializedProperty manualProperty = serializedDriver.FindProperty("moveSpeed");
            SerializedProperty sourceProperty = serializedDriver.FindProperty("moveSpeedSource");
            SerializedProperty overrideProperty = serializedDriver.FindProperty("overrideMoveSpeed");
            if (manualProperty == null || sourceProperty == null || overrideProperty == null)
                throw new System.InvalidOperationException("MotionDriver 的 moveSpeed 序列化欄位不完整。");

            float moveSpeed;
            if (overrideProperty.boolValue)
            {
                moveSpeed = manualProperty.floatValue;
            }
            else
            {
                var source = sourceProperty.objectReferenceValue as MotionBakeData;
                if (source == null)
                    throw new System.InvalidOperationException("MotionDriver 未 override，但 moveSpeedSource 為空。");

                moveSpeed = source.GetRepresentativeSpeed();
            }

            EnsurePositiveFinite(moveSpeed, "MotionDriver 執行期 moveSpeed");
            return moveSpeed;
        }

        private static Vector2[] CreateThresholds(float ringRadius)
        {
            var thresholds = new Vector2[1 + _prototypeUnitThresholds.Length];
            thresholds[0] = Vector2.zero;
            for (int i = 0; i < _prototypeUnitThresholds.Length; i++)
                thresholds[i + 1] = _prototypeUnitThresholds[i] * ringRadius;
            return thresholds;
        }

        private static float[] CreatePlaybackSpeeds(
            float targetWorldSpeed, IReadOnlyList<float> nativeSpeeds)
        {
            var speeds = new float[nativeSpeeds.Count];
            speeds[0] = 1f;

            for (int i = 1; i < speeds.Length; i++)
            {
                speeds[i] = targetWorldSpeed / nativeSpeeds[i];
                EnsurePositiveFinite(speeds[i], $"child index {i} playback speed");
            }

            return speeds;
        }

        private static void WritePrototype(
            string assetPath,
            AnimationClip[] clips,
            Vector2[] thresholds,
            float[] speeds,
            StringAsset moveX,
            StringAsset moveZ)
        {
            var mixer = new MixerTransition2D
            {
                FadeDuration = PrototypeFadeDuration,
                Speed = 1f
            };

            var animations = new UnityEngine.Object[clips.Length];
            for (int i = 0; i < clips.Length; i++)
                animations[i] = clips[i];

            mixer.Type = MixerTransition2D.MixerType.Cartesian;
            mixer.Animations = animations;
            mixer.Thresholds = thresholds;
            mixer.Speeds = speeds;
            mixer.SynchronizeChildren = new bool[clips.Length];
            mixer.DefaultParameter = Vector2.zero;
            mixer.ParameterNameX = moveX;
            mixer.ParameterNameY = moveZ;

            TransitionAsset asset = AssetDatabase.LoadAssetAtPath<TransitionAsset>(assetPath);
            if (asset == null)
            {
                UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (existing != null)
                {
                    throw new System.InvalidOperationException(
                        $"輸出路徑已有非 TransitionAsset 資產：{assetPath} ({existing.GetType().Name})");
                }

                asset = ScriptableObject.CreateInstance<TransitionAsset>();
                asset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                asset.Transition = mixer;
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                asset.Transition = mixer;
                EditorUtility.SetDirty(asset);
            }
        }

        private static void LogPrototypeReport(
            string prototype,
            string assetPath,
            IReadOnlyList<AnimationClip> clips,
            IReadOnlyList<Vector2> thresholds,
            IReadOnlyList<float> nativeSpeeds,
            IReadOnlyList<float> speeds)
        {
            Debug.Log($"[AnimationBatchOps] PROTOTYPE|name={prototype}|assetPath={assetPath}|childCount={clips.Count}");
            for (int i = 0; i < clips.Count; i++)
            {
                Vector2 threshold = thresholds[i];
                Debug.Log(
                    $"[AnimationBatchOps] CHILD|prototype={prototype}|name={clips[i].name}|" +
                    $"threshold=({FormatFloat(threshold.x)},{FormatFloat(threshold.y)})|" +
                    $"radius={FormatFloat(threshold.magnitude)}|native={FormatFloat(nativeSpeeds[i])}|" +
                    $"speed={FormatFloat(speeds[i])}");
            }
        }

        private static T LoadRequiredAsset<T>(string assetPath, string label) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            return asset != null
                ? asset
                : throw new System.InvalidOperationException($"找不到 {label}：{assetPath}");
        }

        private static void EnsurePositiveFinite(float value, string label)
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new System.InvalidOperationException($"{label} 必須是有效正數，實際為 {FormatFloat(value)}。");
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool TryResolveRequiredClips(out AnimationClip[] clips, out string error)
        {
            clips = new AnimationClip[_requiredClipNames.Length];
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(SourceFbxPath);
            if (subAssets == null || subAssets.Length == 0)
            {
                clips = null;
                error = $"FBX 不存在或沒有可解析的子資產：{SourceFbxPath}";
                return false;
            }

            // 不把 FBX 內所有 AnimationClip 收進批次清單；只把名稱與 _requiredClipNames 完全相等的
            // 七支放入固定索引，Throw/Crouch/近似名稱一律忽略。
            for (int assetIndex = 0; assetIndex < subAssets.Length; assetIndex++)
            {
                if (subAssets[assetIndex] is not AnimationClip clip) continue;

                for (int requiredIndex = 0; requiredIndex < _requiredClipNames.Length; requiredIndex++)
                {
                    if (!string.Equals(clip.name, _requiredClipNames[requiredIndex], System.StringComparison.Ordinal))
                        continue;

                    if (clips[requiredIndex] != null)
                    {
                        clips = null;
                        error = $"FBX 內出現重複的精確 clip 名稱：{_requiredClipNames[requiredIndex]}";
                        return false;
                    }

                    clips[requiredIndex] = clip;
                    break;
                }
            }

            var missing = new StringBuilder();
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null) continue;
                if (missing.Length > 0) missing.Append(", ");
                missing.Append(_requiredClipNames[i]);
            }

            if (missing.Length > 0)
            {
                clips = null;
                error = $"FBX 缺少必要的精確命名子 clip：{missing}";
                return false;
            }

            error = null;
            return true;
        }

        private static void LogMachineReadableBakeReport(IReadOnlyList<AnimationClip> clips)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip clip = clips[i];
                string assetPath = MotionBakeEditor.GetOutputAssetPath(clip);
                MotionBakeData bakeData = AssetDatabase.LoadAssetAtPath<MotionBakeData>(assetPath);
                if (bakeData == null)
                    throw new System.InvalidOperationException($"烘焙完成後找不到輸出資產：{assetPath}");

                string nativeSpeed = bakeData.GetRepresentativeSpeed().ToString("R", CultureInfo.InvariantCulture);
                string bakedDuration = bakeData.BakedDuration.ToString("R", CultureInfo.InvariantCulture);
                Debug.Log(
                    $"[AnimationBatchOps] CLIP|name={clip.name}|nativeSpeed={nativeSpeed}|" +
                    $"bakedDuration={bakedDuration}|assetPath={assetPath}");
            }
        }

        private static string SanitizeReportValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            return value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
        }
    }
}
#endif
