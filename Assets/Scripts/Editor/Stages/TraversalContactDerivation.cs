#if UNITY_EDITOR
using Project.Presentation.Motion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project.Editor.Stages
{
    /// <summary>
    /// 🆕（docs/23 Phase 2）從動畫**自動測出** traversal 的接觸標記，取代人工填 6 個 normalized time。
    ///
    /// 依據 Horizon Zero Dawn（GDC 2017）的分工：**動畫決定 WHEN、程式決定 WHERE。**
    /// 「WHEN」不該是人憑感覺填的數字——它是動畫裡客觀存在的事件，可以量出來：
    /// **手先動、然後停住不動的那一刻就是接觸。**
    ///
    /// 偵測方式（全部是相對量，沒有場景相關的魔術數字）：
    /// <list type="number">
    /// <item>逐格採樣手骨世界座標，算出速度。</item>
    /// <item>門檻 ＝ 該手在整段動畫中的**最大速度** × <see cref="PlantSpeedFraction"/>。
    ///   用自己的最大速度當基準，因此不受動畫快慢或角色尺寸影響。</item>
    /// <item>必須**先超過門檻**（手真的動起來）**再掉到門檻以下**，才算接觸——
    ///   否則動畫開頭「手還沒抬起來」的靜止會被誤判成接觸。</item>
    /// <item>低速必須連續維持 <see cref="MinimumPlantFrames"/> 格以上，排除揮動中的瞬間轉向。</item>
    /// </list>
    ///
    /// 實測（2026-09-13）：`Vault1m` 正確測出**只有左手**接觸（右手全程揮動，從不停住）；
    /// `Climb2m` 正確測出**雙手**在 n≈0.07 抓住並維持到 n≈0.26。
    /// </summary>
    public static class TraversalContactDerivation
    {
        /// <summary>接觸速度門檻佔該手自身最大速度的比例。相對量，與動畫快慢無關。</summary>
        private const float PlantSpeedFraction = 0.15f;

        /// <summary>低速必須連續維持的格數；低於此值視為揮動途中的瞬間減速，不是接觸。</summary>
        private const int MinimumPlantFrames = 3;

        /// <summary>只在動畫前段找接觸——後段是站定／跑出，那裡的低速是走路不是抓握。</summary>
        private const float SearchWindowNormalizedEnd = 0.5f;

        /// <summary>
        /// 指尖與頂面之間要保留的淨空（m）。**手指有厚度**——骨骼點剛好貼面等於網格埋進去一半。
        /// 這是 animation／contact fitting 的 authored 量（`docs/24` §13），不是 probe geometry；
        /// 它只在 bake 期進入 grip edge，執行期不會有第二個地方再加減一次。
        /// </summary>
        private const float FingerContactClearance = 0.01f;

        /// <summary>
        /// Hand IK 權重的淡入／淡出時間（秒）。這是**混合時長**，與 TransitionAsset 的 fade 同類，
        /// 因此用秒而不是 normalized time——不同長度的 clip 才會有一致的手感。
        /// 淡出與淡入等長（見 <c>TraversalHandIKWindow.Evaluate</c>）。
        /// </summary>
        private const float HandIKFadeSeconds = 0.1f;

        /// <summary>
        /// Fixed-wrist IK must retain a small elbow reserve. One means a mathematically straight arm;
        /// 0.95 keeps a bounded five-percent reach reserve without changing contact placement.
        /// </summary>
        private const float MaximumAnchoredReachRatio = 0.95f;

        /// <summary>
        /// 由測出來的 plant 換算 IK 權重窗：**手落下之前就開始淡入**（落下那一格已經是滿權重，
        /// 才不會在接觸瞬間彈一下）。雙手支撐時，若固定腕點開始比動畫腕點更拉長肩—腕鏈，
        /// 雙手支撐由共同的 Transfer 開始淡出，使兩手在 pelvis 已越過平台支撐面後一起交棒。
        /// 沒有測到 plant 的手回傳無效窗，呼叫端會據此不啟用 IK。
        /// </summary>
        /// <summary>
        /// 雙手支撐的 release：固定腕點開始比 authored 腕點要求更長的肩—腕距離之前，IK 必須已淡出。
        /// 沒有測到該事件、或單手動作時回 <c>NaN</c>，呼叫端改用 plant 結束點。
        /// </summary>
        private static float KinematicRelease(in HandPlant plant, float fade, bool bothHands)
        {
            if (!bothHands || !plant.HasFixedGoalExtensionStart) return float.NaN;
            return plant.FixedGoalExtensionStartNormalizedTime - fade;
        }

        private static TraversalHandIKWindow BuildHandIKWindow(
            in HandPlant plant, float fade, float releaseOverride)
        {
            if (!plant.HasPlant) return default;
            float full = Mathf.Clamp01(plant.StartNormalizedTime);
            float start = Mathf.Clamp01(full - fade);
            bool hasOverride = !float.IsNaN(releaseOverride) &&
                               !float.IsInfinity(releaseOverride);
            float release = Mathf.Clamp01(Mathf.Max(
                hasOverride ? releaseOverride : plant.EndNormalizedTime,
                full));
            return new TraversalHandIKWindow(true, start, full, release);
        }

        /// <summary>採樣結果：一隻手的接觸窗。</summary>
        public readonly struct HandPlant
        {
            public readonly bool HasPlant;
            public readonly float StartNormalizedTime;
            public readonly float EndNormalizedTime;
            /// <summary>接觸瞬間，手相對 root 的前方距離（公尺）。決定這支動畫需要多遠的進場距離。</summary>
            public readonly float ForwardReachAtPlant;
            /// <summary>接觸瞬間，手相對 root 的高度（公尺）。</summary>
            public readonly float HeightAtPlant;
            /// <summary>
            /// 固定接觸腕點第一次連續要求比 authored 腕點更長肩—腕距離的時刻。
            /// NaN 代表 plant 窗內沒有發生；只用來限制雙手支撐的 IK，不改 contact／transfer marker。
            /// </summary>
            public readonly float FixedGoalExtensionStartNormalizedTime;
            public bool HasFixedGoalExtensionStart =>
                !float.IsNaN(FixedGoalExtensionStartNormalizedTime) &&
                !float.IsInfinity(FixedGoalExtensionStartNormalizedTime) &&
                FixedGoalExtensionStartNormalizedTime >= StartNormalizedTime &&
                FixedGoalExtensionStartNormalizedTime <= EndNormalizedTime;

            public HandPlant(
                float startNormalizedTime,
                float endNormalizedTime,
                float forwardReachAtPlant,
                float heightAtPlant,
                float fixedGoalExtensionStartNormalizedTime = float.NaN)
            {
                HasPlant = true;
                StartNormalizedTime = startNormalizedTime;
                EndNormalizedTime = endNormalizedTime;
                ForwardReachAtPlant = forwardReachAtPlant;
                HeightAtPlant = heightAtPlant;
                FixedGoalExtensionStartNormalizedTime = fixedGoalExtensionStartNormalizedTime;
            }
        }

        /// <summary>一支動畫的完整測量結果。</summary>
        public readonly struct Measurement
        {
            public readonly bool IsValid;
            public readonly string Report;
            public readonly HandPlant Left;
            public readonly HandPlant Right;
            public readonly TraversalHandContactMode ContactMode;
            public readonly float TransferNormalizedTime;
            public readonly float ExitNormalizedTime;
            public readonly float RecoveryNormalizedTime;
            public readonly float PelvisVerticalVelocityAtExit;
            public readonly float PelvisVerticalVelocityAtRecovery;

            public Measurement(string report) : this()
            {
                IsValid = false;
                Report = report;
            }

            public Measurement(
                in HandPlant left,
                in HandPlant right,
                TraversalHandContactMode contactMode,
                float transferNormalizedTime,
                float exitNormalizedTime,
                float recoveryNormalizedTime,
                float pelvisVerticalVelocityAtExit,
                float pelvisVerticalVelocityAtRecovery,
                string report)
            {
                IsValid = true;
                Report = report;
                Left = left;
                Right = right;
                ContactMode = contactMode;
                TransferNormalizedTime = transferNormalizedTime;
                ExitNormalizedTime = exitNormalizedTime;
                RecoveryNormalizedTime = recoveryNormalizedTime;
                PelvisVerticalVelocityAtExit = pelvisVerticalVelocityAtExit;
                PelvisVerticalVelocityAtRecovery = pelvisVerticalVelocityAtRecovery;
            }
        }

        /// <summary>
        /// 在隔離的 preview scene 裡採樣並測量。**不動使用者開啟的場景。**
        /// </summary>
        public static Measurement Measure(MotionBakeData bake, GameObject rigPrefab)
        {
            if (bake == null || bake.SourceClip == null) return new Measurement("bake 或 SourceClip 缺失");
            if (rigPrefab == null) return new Measurement("缺少採樣用的 rig prefab");
            float duration = bake.Duration;
            if (duration <= 0f) return new Measurement("BakedDuration 無效");

            AnimationClip clip = bake.SourceClip;
            float sampleRate = bake.SampleRate > 0f ? bake.SampleRate : 30f;
            int frames = Mathf.Max(MinimumPlantFrames * 2, Mathf.RoundToInt(duration * sampleRate));

            var rootLocal = new Vector3[frames + 1];
            var leftLocal = new Vector3[frames + 1];
            var rightLocal = new Vector3[frames + 1];
            var leftUpperArmLocal = new Vector3[frames + 1];
            var rightUpperArmLocal = new Vector3[frames + 1];
            var hipsInOrigin = new Vector3[frames + 1];

            UnityEngine.SceneManagement.Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, preview);
                Animator animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman)
                    return new Measurement("rig prefab 上找不到 humanoid Animator");

                // ⚠️ 沒有這一行 root 完全不動（實測 2026-09-13）：SampleAnimation 不會套用 root motion。
                animator.applyRootMotion = true;
                Transform root = animator.transform;
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Transform rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (left == null || right == null || leftUpperArm == null ||
                    rightUpperArm == null || hips == null)
                    return new Measurement("Avatar 缺少手臂骨");

                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clip.SampleAnimation(animator.gameObject, 0f);
                Vector3 origin = root.position;
                Quaternion originRotation = root.rotation;
                Quaternion inverseOrigin = Quaternion.Inverse(originRotation);

                for (int i = 0; i <= frames; i++)
                {
                    float normalized = i / (float)frames;
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    clip.SampleAnimation(animator.gameObject, normalized * clip.length);
                    rootLocal[i] = inverseOrigin * (root.position - origin);
                    leftLocal[i] = inverseOrigin * (left.position - origin);
                    rightLocal[i] = inverseOrigin * (right.position - origin);
                    leftUpperArmLocal[i] = inverseOrigin * (leftUpperArm.position - origin);
                    rightUpperArmLocal[i] = inverseOrigin * (rightUpperArm.position - origin);
                    hipsInOrigin[i] = inverseOrigin * (hips.position - origin);
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }

            float deltaTime = duration / frames;
            HandPlant leftPlant =
                DetectPlant(leftLocal, rootLocal, leftUpperArmLocal, deltaTime, frames);
            HandPlant rightPlant =
                DetectPlant(rightLocal, rootLocal, rightUpperArmLocal, deltaTime, frames);

            TraversalHandContactMode mode;
            if (leftPlant.HasPlant && rightPlant.HasPlant) mode = TraversalHandContactMode.BothHands;
            else if (leftPlant.HasPlant) mode = TraversalHandContactMode.LeftHand;
            else if (rightPlant.HasPlant) mode = TraversalHandContactMode.RightHand;
            else return new Measurement("測不到任何手部接觸（整段動畫沒有『先動再停』的手）");

            float latestContactStart = Mathf.Max(
                leftPlant.HasPlant ? leftPlant.StartNormalizedTime : 0f,
                rightPlant.HasPlant ? rightPlant.StartNormalizedTime : 0f);
            // Transfer ＝ 最後一隻手放開的時刻：身體從「靠手支撐」轉為「靠腳支撐」。
            float release = Mathf.Max(
                leftPlant.HasPlant ? leftPlant.EndNormalizedTime : 0f,
                rightPlant.HasPlant ? rightPlant.EndNormalizedTime : 0f);
            float transfer = Mathf.Clamp01(Mathf.Max(release, latestContactStart + 1f / frames));

            // Exit ＝ 垂直動作結束（動畫自己「爬完了」的時刻，見 MotionBakeData）。
            float exit = Mathf.Clamp01(Mathf.Max(transfer, bake.GetVerticalSettleNormalizedTime()));
            int exitFrame = Mathf.Clamp(Mathf.RoundToInt(exit * frames), 1, frames);
            int recoveryFrame = FindFirstPelvisSettleFrame(
                hipsInOrigin, rootLocal, exitFrame, frames);
            float recovery = Mathf.Clamp01(Mathf.Max(exit, recoveryFrame / (float)frames));
            float exitPelvisVelocity = PelvisVerticalVelocityAt(
                hipsInOrigin, rootLocal, exitFrame, deltaTime);
            float recoveryPelvisVelocity = PelvisVerticalVelocityAt(
                hipsInOrigin, rootLocal, recoveryFrame, deltaTime);

            string report =
                $"mode={mode} " +
                $"L={(leftPlant.HasPlant ? $"{leftPlant.StartNormalizedTime:F3}-{leftPlant.EndNormalizedTime:F3} reach={leftPlant.ForwardReachAtPlant:F3} h={leftPlant.HeightAtPlant:F3}" : "none")} " +
                $"R={(rightPlant.HasPlant ? $"{rightPlant.StartNormalizedTime:F3}-{rightPlant.EndNormalizedTime:F3} reach={rightPlant.ForwardReachAtPlant:F3} h={rightPlant.HeightAtPlant:F3}" : "none")} " +
                $"transfer={transfer:F3} exit={exit:F3} recovery={recovery:F3} " +
                $"pelvisV={exitPelvisVelocity:F3}->{recoveryPelvisVelocity:F3}m/s";

            return new Measurement(
                in leftPlant, in rightPlant, mode, transfer, exit, recovery,
                exitPelvisVelocity, recoveryPelvisVelocity, report);
        }

        /// <summary>
        /// 測量 ＋ 採樣 anchor ＋ 寫回 <see cref="MotionBakeData"/> 資產。
        /// 走 Unity 自己的 API（欄位寫入 ＋ <c>SaveAssetIfDirty</c>），不碰 YAML 文字。
        /// </summary>
        public static bool Apply(MotionBakeData bake, GameObject rigPrefab, out string report)
        {
            Measurement measurement = Measure(bake, rigPrefab);
            report = measurement.Report;
            if (!measurement.IsValid) return false;

            float leftTime = measurement.Left.HasPlant
                ? measurement.Left.StartNormalizedTime
                : measurement.Right.StartNormalizedTime;
            float rightTime = measurement.Right.HasPlant
                ? measurement.Right.StartNormalizedTime
                : leftTime;
            // BothHands 契約要求 right >= left；兩手同時抓住時測出來會相等，這裡只做保序。
            rightTime = Mathf.Max(leftTime, rightTime);

            var block = new TraversalMotionBakeBlock(
                true,
                measurement.ContactMode,
                HumanBodyBones.LeftHand,
                HumanBodyBones.RightHand,
                leftTime,
                rightTime,
                measurement.TransferNormalizedTime,
                measurement.ExitNormalizedTime,
                measurement.RecoveryNormalizedTime,
                0f);

            if (!block.HasValidAuthoredMarkers)
            {
                report += " | 測出的時間不符合 marker 契約（transfer 必須嚴格大於 latest contact 且小於 exit）";
                return false;
            }

            if (!TrySampleAnchors(bake, rigPrefab, in block, out TraversalBakedAnchor[] anchors))
            {
                report += " | anchor 採樣失敗";
                return false;
            }

            block = block.WithBakedAnchors(
                anchors[0], anchors[1], anchors[2], anchors[3], anchors[4], anchors[5]);
            if (!TrySampleGripEdges(
                    bake, rigPrefab, in block,
                    out Vector3 leftGripEdge, out Vector3 rightGripEdge, out string gripReport))
            {
                report += $" | grip edge 採樣失敗（{gripReport}）";
                return false;
            }
            report += $" | gripEdge L={leftGripEdge:F3} R={rightGripEdge:F3}";
            block = block.WithHandGripEdges(leftGripEdge, rightGripEdge);

            // ⭐ 2026-09-13（docs/24 §14）：**Root warp 只在接觸那一格把身體對齊；
            //    其餘每一格的手要靠 IK 釘住。** 沒有這個窗，手就只是跟著動畫跑，
            //    在真實牆面上必然穿進穿出（Playtest 截圖裡手掌壓進箱子側面就是這個）。
            //    窗完全由測出來的 plant 換算：**動畫說手黏著的期間，就是 IK 生效的期間。**
            float duration = bake.Duration;
            float fade = duration > 0f ? Mathf.Clamp01(HandIKFadeSeconds / duration) : 0f;
            // 雙手支撐共用 Transfer 作為「開始交棒」時刻。舊 kinematic release 以提前放手
            // 避開超伸；現在由 bake-derived root reach constraint 保留手肘餘量，因此不再犧牲錨定。
            // 雙手支撐時，release 取「固定腕點開始拉長肩—腕鏈」的時刻再往前讓出一個淡出長度
            //（§15 的 kinematic release）。單手動作沿用 plant 結束點。
            bool bothHands = measurement.ContactMode == TraversalHandContactMode.BothHands;
            TraversalHandIKWindow leftWindow = BuildHandIKWindow(
                measurement.Left, fade, KinematicRelease(measurement.Left, fade, bothHands));
            TraversalHandIKWindow rightWindow = BuildHandIKWindow(
                measurement.Right, fade, KinematicRelease(measurement.Right, fade, bothHands));
            // 單手動作時，未使用的那隻手沿用主手的窗，避免 HasValidHandIK 因為空窗而否決。
            if (!block.UsesLeftHand) leftWindow = rightWindow;
            if (!block.UsesRightHand) rightWindow = leftWindow;

            // rotationWeight = 0：**只釘位置，不轉手腕。**
            // 參考實作一致（Dynamic Parkour System 的 MatchTargetWeightMask 旋轉權重就是 0）——
            // 把手腕硬轉到 ledge frame 會扭曲前臂，而抓握的朝向動畫本來就做對了。
            block = block.WithHandIK(leftWindow, rightWindow, 0f);
            report += $" | handIK L=[{leftWindow.StartNormalizedTime:F3},{leftWindow.FullNormalizedTime:F3}," +
                      $"{leftWindow.ReleaseNormalizedTime:F3}] R=[{rightWindow.StartNormalizedTime:F3}," +
                      $"{rightWindow.FullNormalizedTime:F3},{rightWindow.ReleaseNormalizedTime:F3}] " +
                      $"enabled={block.HandIKEnabled}";

            if (measurement.ContactMode == TraversalHandContactMode.BothHands)
            {
                if (!TryBuildRootReachConstraint(
                        bake, rigPrefab, in block, in leftWindow, in rightWindow,
                        out TraversalRootReachConstraint reachConstraint,
                        out string reachReport))
                {
                    report += $" | root reach constraint 失敗（{reachReport}）";
                    return false;
                }
                block = block.WithRootReachConstraint(in reachConstraint);
                report += $" | {reachReport}";
            }

            bake.Traversal = block;
            if (!bake.Traversal.HasValidBakedData)
            {
                report += " | 寫入後 HasValidBakedData 仍為 false";
                return false;
            }

            EditorUtility.SetDirty(bake);
            AssetDatabase.SaveAssetIfDirty(bake);
            report += " | 已寫入資產";
            return true;
        }

        /// <summary>
        /// 量出**動畫自己的那個「邊緣」**在 root 空間的位置（接觸瞬間，yaw-only）。
        ///
        /// 定義（兩者都由資料決定，沒有手填常數）：
        /// <list type="bullet">
        /// <item><b>頂面高度</b> ＝ clip 結束時的 root 高度（腳最後站在平台上）。</item>
        /// <item><b>牆面位置</b> ＝ 接觸瞬間**手部最前端的接觸點**（四指與拇指的 distal 取最大前伸）。
        ///   手不可能在牆裡面，所以最前端就是牆面。</item>
        /// </list>
        /// 橫向取手腕自身的座標，使左右手分佈仍由 ledge interval 決定。
        ///
        /// 對齊這個點而不是手腕，整隻手的 authored 姿勢（四指扣頂面、拇指按牆面）才會原樣落位。
        /// </summary>
        private static bool TrySampleGripEdges(
            MotionBakeData bake,
            GameObject rigPrefab,
            in TraversalMotionBakeBlock block,
            out Vector3 leftGripEdge,
            out Vector3 rightGripEdge,
            out string report)
        {
            leftGripEdge = Vector3.zero;
            rightGripEdge = Vector3.zero;
            report = string.Empty;
            AnimationClip clip = bake.SourceClip;
            if (clip == null) { report = "no clip"; return false; }

            UnityEngine.SceneManagement.Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, preview);
                Animator animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman) { report = "no humanoid"; return false; }
                animator.applyRootMotion = true;
                Transform root = animator.transform;

                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clip.SampleAnimation(animator.gameObject, 0f);
                Vector3 origin = root.position;
                float originYaw = root.rotation.eulerAngles.y;
                Quaternion inverseOriginYaw = Quaternion.Inverse(Quaternion.Euler(0f, originYaw, 0f));

                float topSurfaceY = ResolveObstacleTopHeight(bake);

                leftGripEdge = SampleGripEdge(
                    animator, clip, block.LeftHandContactNormalizedTime,
                    topSurfaceY, origin, inverseOriginYaw, true);
                rightGripEdge = SampleGripEdge(
                    animator, clip, block.RightHandContactNormalizedTime,
                    topSurfaceY, origin, inverseOriginYaw, false);
                report = $"topSurfaceY={topSurfaceY:F3}";
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            return true;
        }

        /// <summary>
        /// 障礙物頂面在動畫自身座標中的高度。**兩種動作要分開處理，而且可以由資料本身分辨：**
        /// <list type="bullet">
        /// <item><b>攀爬</b>（結束時人在上面）：頂面 ＝ 動畫結束高度。`Climb2m` ⇒ 1.978。</item>
        /// <item><b>翻越</b>（翻過去落在對面地面，結束高度回到起點）：結束高度是 0，
        ///   頂面要取**垂直曲線的峰值**——身體越過障礙時的高度。`Vault1m` ⇒ 1.020。</item>
        /// </list>
        /// 判準是相對的：結束位移不到峰值的一半 ⇒ 視為「翻過去」。
        /// 用錯會讓 grip edge 的高度整個翻負號（Vault 實測會得到 −1.020）。
        /// </summary>
        private static float ResolveObstacleTopHeight(MotionBakeData bake)
        {
            float duration = bake.Duration;
            float endHeight = bake.GetVerticalAt(duration);
            float startHeight = bake.GetVerticalAt(0f);
            float peak = startHeight;
            const int Samples = 120;
            for (int i = 0; i <= Samples; i++)
            {
                float value = bake.GetVerticalAt(i / (float)Samples * duration);
                if (value > peak) peak = value;
            }

            float netRise = Mathf.Abs(endHeight - startHeight);
            float peakRise = Mathf.Abs(peak - startHeight);
            if (peakRise <= 0.0001f) return endHeight;
            return netRise < peakRise * 0.5f ? peak : endHeight;
        }

        private static Vector3 SampleGripEdge(
            Animator animator,
            AnimationClip clip,
            float normalizedTime,
            float topSurfaceY,
            Vector3 origin,
            Quaternion inverseOriginYaw,
            bool left)
        {
            Transform root = animator.transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SampleAnimation(animator.gameObject, Mathf.Clamp01(normalizedTime) * clip.length);

            float rootYaw = root.rotation.eulerAngles.y;
            Quaternion inverseRootYaw = Quaternion.Inverse(Quaternion.Euler(0f, rootYaw, 0f));
            Vector3 rootPosition = root.position;

            Transform wrist = animator.GetBoneTransform(
                left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (wrist == null) return Vector3.zero;
            Vector3 wristInRoot = inverseRootYaw * (wrist.position - rootPosition);

            HumanBodyBones[] contacts = left
                ? new[]
                {
                    HumanBodyBones.LeftThumbDistal, HumanBodyBones.LeftIndexDistal,
                    HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftRingDistal,
                    HumanBodyBones.LeftLittleDistal,
                }
                : new[]
                {
                    HumanBodyBones.RightThumbDistal, HumanBodyBones.RightIndexDistal,
                    HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightRingDistal,
                    HumanBodyBones.RightLittleDistal,
                };

            float wallZ = wristInRoot.z;
            for (int i = 0; i < contacts.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(contacts[i]);
                if (bone == null) continue;
                float z = (inverseRootYaw * (bone.position - rootPosition)).z;
                if (z > wallZ) wallZ = z;
            }

            // topSurfaceY 是相對**動畫起點**量的；grip edge 要相對**接觸當下的 root**，
            // 所以扣掉 root 到此刻已經上升的高度。
            float rootRiseAtContact = (inverseOriginYaw * (rootPosition - origin)).y;
            float topInRoot = topSurfaceY - rootRiseAtContact;

            // ⭐ 2026-09-13 Playtest：「上去的瞬間手指插進牆面」。
            //
            // grip edge 的 Y 原本取**動畫自己的障礙物頂面**。但那條平面是從 VerticalCurve 推出來的
            // 統計量，而 Mixamo 這支 clip 的指尖其實**騎在它上下各約 1 cm**——實測四根手指的
            // 指尖（distal 的葉節點）相對頂面是 −0.013 … +0.009 m。
            // 同時指尖比 distal 關節再往前 2.3–5.4 cm（那是正確的：手指本來就要扣過邊緣），
            // 於是「指尖在頂面下方 ＋ 已經越過牆面」＝**指尖落在頂面轉角的實心裡**。
            //
            // 修法：Y 改由**實際會壓在頂面上的四根手指指尖**決定（拇指排除——它按的是牆面，
            // 實測低 12.6 cm），取最低的那一根，再往下讓出 FingerContactClearance。
            // 對齊之後最低的指尖就會**浮在真實頂面上方** clearance，而不是切進去。
            //
            // ⚠️ FingerContactClearance 是 **animation／contact fitting 的 authored 量**
            //（手指有厚度，骨骼點貼面等於網格埋一半），**不是 probe geometry**。
            //  它只在 bake 期進入 grip edge，執行期不會再有第二個地方加減它。
            float gripY = topInRoot;
            float lowestTipY = float.PositiveInfinity;
            HumanBodyBones[] topFaceFingers = left
                ? new[]
                {
                    HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleDistal,
                    HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftLittleDistal,
                }
                : new[]
                {
                    HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal,
                    HumanBodyBones.RightRingDistal, HumanBodyBones.RightLittleDistal,
                };
            for (int i = 0; i < topFaceFingers.Length; i++)
            {
                Transform distal = animator.GetBoneTransform(topFaceFingers[i]);
                if (distal == null) continue;
                for (int c = 0; c < distal.childCount; c++)
                {
                    float tipY = (inverseRootYaw * (distal.GetChild(c).position - rootPosition)).y;
                    if (tipY < lowestTipY) lowestTipY = tipY;
                }
            }

            if (!float.IsInfinity(lowestTipY))
                gripY = lowestTipY - FingerContactClearance;

            return new Vector3(wristInRoot.x, gripY, wallZ);
        }

        /// <summary>
        /// 六個 marker 時刻的 root pose ＋ hand-in-root 採樣。
        ///
        /// ⚠️（docs/23 §D）**旋轉空間必須與執行期一致。** 執行期是以
        /// <c>Quaternion.Euler(0, yaw, 0) * handInRoot</c> 還原手的位置，因此這裡也只能用
        /// **yaw-only** 的逆旋轉，不能用 <c>Transform.InverseTransformPoint</c>
        /// （那會帶進 root 的 pitch／roll 與 lossyScale，clip 的 root 一旦有傾角，手就會偏）。
        /// </summary>
        private static bool TrySampleAnchors(
            MotionBakeData bake,
            GameObject rigPrefab,
            in TraversalMotionBakeBlock block,
            out TraversalBakedAnchor[] anchors)
        {
            anchors = null;
            AnimationClip clip = bake.SourceClip;
            if (clip == null) return false;

            float[] times =
            {
                0f,
                block.LeftHandContactNormalizedTime,
                block.RightHandContactNormalizedTime,
                block.TransferNormalizedTime,
                block.ExitNormalizedTime,
                block.RecoveryNormalizedTime,
            };
            var result = new TraversalBakedAnchor[times.Length];

            UnityEngine.SceneManagement.Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, preview);
                Animator animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman) return false;
                animator.applyRootMotion = true;
                Transform root = animator.transform;
                Transform left = animator.GetBoneTransform(block.LeftHandBone);
                Transform right = animator.GetBoneTransform(block.RightHandBone);
                if (left == null || right == null) return false;

                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clip.SampleAnimation(animator.gameObject, 0f);
                Vector3 origin = root.position;
                float originYaw = root.rotation.eulerAngles.y;
                Quaternion inverseOriginYaw = Quaternion.Inverse(Quaternion.Euler(0f, originYaw, 0f));

                for (int i = 0; i < times.Length; i++)
                {
                    float normalized = Mathf.Clamp01(times[i]);
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    clip.SampleAnimation(animator.gameObject, normalized * clip.length);

                    Vector3 rootLocalPosition = inverseOriginYaw * (root.position - origin);
                    float rootLocalYaw = Mathf.DeltaAngle(originYaw, root.rotation.eulerAngles.y);
                    Quaternion inverseRootYaw =
                        Quaternion.Inverse(Quaternion.Euler(0f, root.rotation.eulerAngles.y, 0f));
                    Vector3 leftInRoot = inverseRootYaw * (left.position - root.position);
                    Vector3 rightInRoot = inverseRootYaw * (right.position - root.position);

                    result[i] = new TraversalBakedAnchor(
                        normalized, rootLocalPosition, rootLocalYaw,
                        leftInRoot, rightInRoot, true, true);
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }

            anchors = result;
            return true;
        }

        /// <summary>
        /// 「先動起來、再停住」＝ 接觸。回傳該手在搜尋窗內**最長**的一段低速區間。
        /// </summary>
        /// <summary>
        /// 收下一段低速區間當作候選接觸，取最長的那一段。
        ///
        /// ⭐ 2026-09-13：**低速不等於接觸。** 只用「最長低速段」選窗，對 rig 極度敏感——
        /// 同一支 `Vault1m`，`X Bot` 測到 0.207–0.253（手在 root 前方 0.725 m，正確），
        /// 換成 `Y Bot`（實際遊玩用的 rig，手骨略大）就跳到 0.356–0.414，
        /// 而那一段的手在 **root 後方 0.430 m**——那是翻過去之後手往後擺的減速，不是抓握。
        ///
        /// 加一條物理合理性過濾：**扶在前方障礙物上的手不可能在身體後面**。
        /// 這條與 rig 無關，也不需要任何門檻調參，且不會影響本來就正確的窗
        /// （`Climb2m` +0.359／`Vault1m`@X Bot +0.725 都通過）。
        /// </summary>
        private static void ConsiderPlantRun(
            Vector3[] hand, Vector3[] root, int start, int length,
            ref int bestStart, ref int bestLength)
        {
            if (start < 0 || length <= 0) return;
            if (hand[start].z - root[start].z <= 0f) return;
            if (length <= bestLength) return;
            bestStart = start;
            bestLength = length;
        }

        /// <summary>
        /// Exit fixes the gameplay root on the platform; Recovery waits for the first pelvis
        /// vertical turning point after it. At that point the source pose has effectively zero
        /// vertical velocity, so the stand blend does not have to reverse a moving pelvis.
        /// </summary>
        private static int FindFirstPelvisSettleFrame(
            Vector3[] hipsInOrigin, Vector3[] rootInOrigin, int exitFrame, int frames)
        {
            if (hipsInOrigin == null || rootInOrigin == null || frames < 1)
                return Mathf.Max(0, exitFrame);

            int start = Mathf.Clamp(exitFrame, 1, frames);
            float previousDelta =
                (hipsInOrigin[start] - rootInOrigin[start]).y -
                (hipsInOrigin[start - 1] - rootInOrigin[start - 1]).y;
            if (previousDelta >= 0f) return start;

            for (int i = start + 1; i <= frames; i++)
            {
                float currentDelta =
                    (hipsInOrigin[i] - rootInOrigin[i]).y -
                    (hipsInOrigin[i - 1] - rootInOrigin[i - 1]).y;
                if (currentDelta >= 0f)
                {
                    return Mathf.Abs(previousDelta) <= Mathf.Abs(currentDelta) ? i - 1 : i;
                }
                previousDelta = currentDelta;
            }

            return start;
        }

        private static float PelvisVerticalVelocityAt(
            Vector3[] hipsInOrigin, Vector3[] rootInOrigin, int frame, float deltaTime)
        {
            if (hipsInOrigin == null || rootInOrigin == null || frame <= 0 ||
                frame >= hipsInOrigin.Length || deltaTime <= 0f)
                return 0f;

            float current = (hipsInOrigin[frame] - rootInOrigin[frame]).y;
            float previous = (hipsInOrigin[frame - 1] - rootInOrigin[frame - 1]).y;
            return (current - previous) / deltaTime;
        }

        /// <summary>
        /// Converts the X Bot's primary-arm reach envelope into a root-local curve. The curve is
        /// a pure bake output: runtime evaluation needs no Animator bone reads and no Physics query.
        /// </summary>
        private static bool TryBuildRootReachConstraint(
            MotionBakeData bake,
            GameObject rigPrefab,
            in TraversalMotionBakeBlock block,
            in TraversalHandIKWindow leftWindow,
            in TraversalHandIKWindow rightWindow,
            out TraversalRootReachConstraint constraint,
            out string report)
        {
            constraint = default;
            report = string.Empty;
            if (bake == null || bake.SourceClip == null || rigPrefab == null ||
                block.ContactMode != TraversalHandContactMode.BothHands || !block.UsesLeftHand)
            {
                report = "只支援有 X Bot rig 的雙手 primary-left 動作";
                return false;
            }

            float duration = bake.Duration;
            float sampleRate = bake.SampleRate > 0f ? bake.SampleRate : 30f;
            int frames = Mathf.Max(MinimumPlantFrames * 2, Mathf.RoundToInt(duration * sampleRate));
            if (duration <= 0f || frames < 2)
            {
                report = "duration/sample count 無效";
                return false;
            }

            var rootInOrigin = new Vector3[frames + 1];
            var rootYawInOrigin = new Quaternion[frames + 1];
            var leftHandInOrigin = new Vector3[frames + 1];
            var rightHandInOrigin = new Vector3[frames + 1];
            var leftUpperInOrigin = new Vector3[frames + 1];
            var rightUpperInOrigin = new Vector3[frames + 1];
            var leftArmLength = new float[frames + 1];
            var rightArmLength = new float[frames + 1];

            UnityEngine.SceneManagement.Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, preview);
                Animator animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    report = "rig prefab 上找不到 humanoid Animator";
                    return false;
                }

                animator.applyRootMotion = true;
                Transform root = animator.transform;
                Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Transform rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Transform leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                Transform rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                if (leftHand == null || rightHand == null || leftUpper == null || rightUpper == null ||
                    leftLower == null || rightLower == null)
                {
                    report = "Avatar 缺少手臂骨";
                    return false;
                }

                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                bake.SourceClip.SampleAnimation(animator.gameObject, 0f);
                Vector3 origin = root.position;
                Quaternion inverseOrigin = Quaternion.Inverse(root.rotation);

                for (int i = 0; i <= frames; i++)
                {
                    float normalized = i / (float)frames;
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    bake.SourceClip.SampleAnimation(animator.gameObject, normalized * bake.SourceClip.length);
                    rootInOrigin[i] = inverseOrigin * (root.position - origin);
                    Quaternion rootRotation = inverseOrigin * root.rotation;
                    rootYawInOrigin[i] = Quaternion.Euler(0f, rootRotation.eulerAngles.y, 0f);
                    leftHandInOrigin[i] = inverseOrigin * (leftHand.position - origin);
                    rightHandInOrigin[i] = inverseOrigin * (rightHand.position - origin);
                    leftUpperInOrigin[i] = inverseOrigin * (leftUpper.position - origin);
                    rightUpperInOrigin[i] = inverseOrigin * (rightUpper.position - origin);
                    Vector3 leftLowerInOrigin = inverseOrigin * (leftLower.position - origin);
                    Vector3 rightLowerInOrigin = inverseOrigin * (rightLower.position - origin);
                    leftArmLength[i] = Vector3.Distance(leftUpperInOrigin[i], leftLowerInOrigin) +
                                       Vector3.Distance(leftLowerInOrigin, leftHandInOrigin[i]);
                    rightArmLength[i] = Vector3.Distance(rightUpperInOrigin[i], rightLowerInOrigin) +
                                        Vector3.Distance(rightLowerInOrigin, rightHandInOrigin[i]);
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }

            int leftContactFrame = Mathf.Clamp(
                Mathf.RoundToInt(block.LeftHandContactNormalizedTime * frames), 0, frames);
            int rightContactFrame = Mathf.Clamp(
                Mathf.RoundToInt(block.RightHandContactNormalizedTime * frames), 0, frames);
            Vector3 fixedLeftWrist = leftHandInOrigin[leftContactFrame];
            Vector3 fixedRightWrist = rightHandInOrigin[rightContactFrame];
            float fadeSpan = Mathf.Max(0f,
                leftWindow.FullNormalizedTime - leftWindow.StartNormalizedTime);
            int fadeEndFrame = Mathf.Clamp(
                Mathf.RoundToInt((leftWindow.ReleaseNormalizedTime + fadeSpan) * frames),
                leftContactFrame, frames);
            int correctionEndFrame = FindFirstRootApexFrame(rootInOrigin, fadeEndFrame, frames);
            if (correctionEndFrame <= fadeEndFrame)
            {
                report = "Hand IK 歸零後找不到可平滑交還 authored root 的 apex";
                return false;
            }

            var corrections = new Vector3[frames + 1];
            for (int i = leftContactFrame; i <= fadeEndFrame; i++)
            {
                Vector3 toGoal = fixedLeftWrist - leftUpperInOrigin[i];
                float distance = toGoal.magnitude;
                float maximumDistance = MaximumAnchoredReachRatio * leftArmLength[i];
                if (distance <= maximumDistance || distance <= 0.000001f) continue;
                Vector3 correctionInOrigin = toGoal * ((distance - maximumDistance) / distance);
                corrections[i] = Quaternion.Inverse(rootYawInOrigin[i]) * correctionInOrigin;
            }

            Vector3 releaseCorrection = corrections[fadeEndFrame];
            for (int i = fadeEndFrame + 1; i < correctionEndFrame; i++)
            {
                float t = (i - fadeEndFrame) / (float)(correctionEndFrame - fadeEndFrame);
                float smooth = t * t * (3f - 2f * t);
                corrections[i] = Vector3.Lerp(releaseCorrection, Vector3.zero, smooth);
            }

            float maximumLeftRatio = 0f;
            float maximumRightRatio = 0f;
            float maximumCorrection = 0f;
            for (int i = 0; i <= frames; i++)
            {
                float normalized = i / (float)frames;
                Vector3 correctionInOrigin = rootYawInOrigin[i] * corrections[i];
                maximumCorrection = Mathf.Max(maximumCorrection, correctionInOrigin.magnitude);
                if (leftWindow.Evaluate(normalized, block.ExitNormalizedTime) > 0.0001f)
                {
                    float ratio = Vector3.Distance(
                        leftUpperInOrigin[i] + correctionInOrigin, fixedLeftWrist) /
                        leftArmLength[i];
                    maximumLeftRatio = Mathf.Max(maximumLeftRatio, ratio);
                }
                if (rightWindow.Evaluate(normalized, block.ExitNormalizedTime) > 0.0001f)
                {
                    float ratio = Vector3.Distance(
                        rightUpperInOrigin[i] + correctionInOrigin, fixedRightWrist) /
                        rightArmLength[i];
                    maximumRightRatio = Mathf.Max(maximumRightRatio, ratio);
                }
            }

            const float RatioTolerance = 0.001f;
            if (maximumLeftRatio > MaximumAnchoredReachRatio + RatioTolerance ||
                maximumRightRatio > MaximumAnchoredReachRatio + RatioTolerance)
            {
                report = $"primary-only correction 無法同時守住雙手（L={maximumLeftRatio:F3}, " +
                         $"R={maximumRightRatio:F3}）";
                return false;
            }

            AnimationCurve x = CreateNormalizedCurve(corrections, 0);
            AnimationCurve y = CreateNormalizedCurve(corrections, 1);
            AnimationCurve z = CreateNormalizedCurve(corrections, 2);
            constraint = new TraversalRootReachConstraint(
                MaximumAnchoredReachRatio,
                leftWindow.ReleaseNormalizedTime,
                correctionEndFrame / (float)frames,
                x, y, z);
            report = $"rootReach max={MaximumAnchoredReachRatio:F2} L={maximumLeftRatio:F3} " +
                     $"R={maximumRightRatio:F3} correction={maximumCorrection * 100f:F1}cm " +
                     $"release={leftWindow.ReleaseNormalizedTime:F3} end={correctionEndFrame / (float)frames:F3}";
            return constraint.IsValid;
        }

        private static int FindFirstRootApexFrame(Vector3[] rootInOrigin, int startFrame, int frames)
        {
            int start = Mathf.Clamp(startFrame + 1, 1, frames - 1);
            for (int i = start; i < frames; i++)
            {
                if (rootInOrigin[i + 1].y <= rootInOrigin[i].y)
                    return i;
            }
            return frames;
        }

        private static AnimationCurve CreateNormalizedCurve(Vector3[] values, int component)
        {
            int frames = values.Length - 1;
            var keys = new Keyframe[values.Length];
            for (int i = 0; i <= frames; i++)
            {
                float value = component == 0 ? values[i].x : component == 1 ? values[i].y : values[i].z;
                float previous = i > 0
                    ? (component == 0 ? values[i - 1].x : component == 1 ? values[i - 1].y : values[i - 1].z)
                    : value;
                float next = i < frames
                    ? (component == 0 ? values[i + 1].x : component == 1 ? values[i + 1].y : values[i + 1].z)
                    : value;
                float tangent = i == 0
                    ? (next - value) * frames
                    : i == frames
                        ? (value - previous) * frames
                        : (next - previous) * frames * 0.5f;
                keys[i] = new Keyframe(i / (float)frames, value, tangent, tangent);
            }
            return new AnimationCurve(keys);
        }

        private static HandPlant DetectPlant(
            Vector3[] hand, Vector3[] root, Vector3[] upperArm, float deltaTime, int frames)
        {
            if (deltaTime <= 0f) return default;

            float maximumSpeed = 0f;
            var speed = new float[frames + 1];
            for (int i = 1; i <= frames; i++)
            {
                speed[i] = (hand[i] - hand[i - 1]).magnitude / deltaTime;
                if (speed[i] > maximumSpeed) maximumSpeed = speed[i];
            }
            if (maximumSpeed <= 0f) return default;

            float threshold = maximumSpeed * PlantSpeedFraction;
            int searchEnd = Mathf.Min(frames, Mathf.CeilToInt(frames * SearchWindowNormalizedEnd));

            bool hasMoved = false;
            int bestStart = -1, bestLength = 0;
            int runStart = -1, runLength = 0;
            for (int i = 1; i <= searchEnd; i++)
            {
                if (speed[i] > threshold)
                {
                    // 手正在動：先記錄「已經動過」，並結束目前的低速段。
                    hasMoved = true;
                    ConsiderPlantRun(hand, root, runStart, runLength, ref bestStart, ref bestLength);
                    runStart = -1; runLength = 0;
                    continue;
                }

                if (!hasMoved) continue; // 動畫開頭手還沒抬起來的靜止，不是接觸。
                if (runStart < 0) runStart = i;
                runLength++;
            }
            ConsiderPlantRun(hand, root, runStart, runLength, ref bestStart, ref bestLength);
            if (bestStart < 0 || bestLength < MinimumPlantFrames) return default;

            int endIndex = Mathf.Min(frames, bestStart + bestLength - 1);
            Vector3 handAtPlant = hand[bestStart];
            Vector3 rootAtPlant = root[bestStart];
            Vector3 offset = handAtPlant - rootAtPlant;
            float extensionStart = FindFixedGoalExtensionStart(
                hand, upperArm, bestStart, endIndex, frames);
            return new HandPlant(
                bestStart / (float)frames,
                endIndex / (float)frames,
                offset.z,
                offset.y,
                extensionStart);
        }

        /// <summary>
        /// 找出固定接觸腕點開始「拉長」手臂的幾何事件。比較同一格 shoulder 到
        /// fixed-contact wrist 與 authored wrist 的距離，不引入公尺或角度門檻；
        /// 連續 <see cref="MinimumPlantFrames"/> 格成立才採用，排除單格取樣雜訊。
        /// </summary>
        private static float FindFixedGoalExtensionStart(
            Vector3[] hand, Vector3[] upperArm, int plantStart, int plantEnd, int frames)
        {
            if (hand == null || upperArm == null || plantStart < 0 || plantEnd <= plantStart)
                return float.NaN;

            Vector3 fixedGoal = hand[plantStart];
            int runStart = -1;
            int runLength = 0;
            for (int i = plantStart + 1; i <= plantEnd; i++)
            {
                float fixedDistanceSqr = (fixedGoal - upperArm[i]).sqrMagnitude;
                float authoredDistanceSqr = (hand[i] - upperArm[i]).sqrMagnitude;
                if (fixedDistanceSqr > authoredDistanceSqr)
                {
                    if (runStart < 0) runStart = i;
                    runLength++;
                    if (runLength >= MinimumPlantFrames)
                        return runStart / (float)frames;
                }
                else
                {
                    runStart = -1;
                    runLength = 0;
                }
            }

            return float.NaN;
        }
    }
}
#endif
