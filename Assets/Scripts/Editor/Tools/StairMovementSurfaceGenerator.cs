using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Editor
{
    public enum StairTopLanding
    {
        /// <summary>樓梯頂端外有一塊同高的可走平台。</summary>
        Continuous,

        /// <summary>樓梯頂端外沒有平台（或落差大於一級）——走出去就是墜落，與視覺一致。</summary>
        OpenDrop,
    }

    /// <summary>一次產生或驗證的完整量測結果。所有數字都在世界座標。</summary>
    public sealed class StairSurfaceReport
    {
        public GameObject Stair;
        public readonly List<string> Failures = new List<string>();
        public bool IsValid => Failures.Count == 0;

        public StairProfile Profile;
        public Vector3 Origin;
        public Vector3 Ascent;
        public Vector3 Lateral;
        public float LateralMin;
        public float LateralMax;
        public float BottomLandingY;
        public float EntryRiser;
        public StairTopLanding TopLanding;
        public float TopLandingY = float.NaN;
        public float RampStartS;
        public Vector3[] WorldVertices;

        public float MinGap = float.NaN;
        public float MaxGap = float.NaN;
        public float MaxVertexDrift = float.NaN;

        public float Width => LateralMax - LateralMin;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Stair != null ? Stair.name : "<null>").Append(IsValid ? " ✅" : " ❌");
            if (Profile != null && Profile.RiserS != null)
            {
                sb.Append($" risers={Profile.RiserCount} riser={Profile.RiserHeight:F3} tread={Profile.TreadDepth:F3}")
                  .Append($" slope={Profile.SlopeDegrees:F2}° residual={Profile.NosingResidual:F4}")
                  .Append($" width={Width:F3} entryRiser={EntryRiser:F3} bottomY={BottomLandingY:F3}")
                  .Append($" top={TopLanding}{(float.IsNaN(TopLandingY) ? "" : $"@{TopLandingY:F3}")}");
            }
            if (!float.IsNaN(MinGap)) sb.Append($" gap=[{MinGap:F3},{MaxGap:F3}]");
            if (!float.IsNaN(MaxVertexDrift)) sb.Append($" drift={MaxVertexDrift:F4}");
            foreach (string f in Failures) sb.Append("\n   - ").Append(f);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 樓梯移動面的產生器與驗證器（正本規格：`docs/44`）。
    ///
    /// 從樓梯自己的碰撞體量出級高、踏面與上升方向，沿「踏面前緣連線」產生一塊只給
    /// CharacterController 碰撞的實心斜坡（`CharacterMovementSurface`），並把原本的階梯碰撞體
    /// 移到 `StairTread`。**沒有任何逐物件參數**——兩道幾何不同的樓梯走同一條規則。
    ///
    /// Editor Tool 兩道閘門：
    /// * Gate A：手擺 ramp 已實證會出錯（prototype 比自己的前緣連線陡 1.7°、最低處在踏面下 0.375 m；
    ///   照抄到另一道樓梯會錯 0.49 m），且每改一次關卡就要重做。
    /// * Gate B：只用公開的 `Collider.Raycast`／`Physics.RaycastAll`，不讀 FBX 或 ProBuilder 內部資料。
    /// </summary>
    public static class StairMovementSurfaceGenerator
    {
        public const string TreadLayerName = "StairTread";
        public const string MovementLayerName = "CharacterMovementSurface";
        public const string SurfaceObjectName = "__StairMovementSurface";

        public const float SampleStep = 0.005f;

        /// <summary>接縫與高度差的容許值，等於 CharacterController 的 skinWidth（0.03 m）——CC 感覺不到的落差。</summary>
        public const float LandingTolerance = 0.03f;

        /// <summary>實心斜坡的底面沉入地面的深度，避免與地面共面。</summary>
        public const float FloorSink = 0.1f;

        public const float LateralConsistencyTolerance = 0.01f;
        public const float DriftTolerance = 0.01f;
        public const float SlopeMarginDegrees = 5f;

        private const string MenuRoot = "Tools/Project/Stairs/";

        [MenuItem(MenuRoot + "Generate Movement Surface (Selected Stairs)")]
        private static void GenerateForSelection()
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                StairSurfaceReport report = Generate(go, apply: true);
                if (report.IsValid) Debug.Log("[StairSurface] " + report, go);
                else Debug.LogError("[StairSurface] 未產生——\n" + report, go);
            }
        }

        [MenuItem(MenuRoot + "Generate Movement Surface (Selected Stairs)", true)]
        private static bool GenerateForSelectionValidate() => Selection.gameObjects.Length > 0;

        [MenuItem(MenuRoot + "Validate Stairs In Open Scenes")]
        private static void ValidateOpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                float slopeLimit = MinCharacterSlopeLimit(scene);
                foreach (GameObject stair in FindAuthoredStairs(scene))
                {
                    StairSurfaceReport report = Validate(stair, slopeLimit);
                    if (report.IsValid) Debug.Log("[StairSurface] " + report, stair);
                    else Debug.LogError("[StairSurface] " + report, stair);
                }
                foreach (string name in FindUnauthoredStairShapes(scene))
                    Debug.LogWarning($"[StairSurface] {scene.name}/{name} 是階梯剖面但沒有移動面");
            }
        }

        /// <summary>
        /// 量測並（<paramref name="apply"/> 為真時）寫入移動面。**寫入是交易式的**：先過所有量測檢查才寫，
        /// 寫完再跑一次完整驗證（I1–I8），任何一條不過就整組 Undo 還原——不會留下不合契約的 ramp。
        /// </summary>
        /// <param name="characterSlopeLimit">NaN ⇒ 取樓梯所在場景中所有 CharacterController 的最小 slopeLimit。</param>
        public static StairSurfaceReport Generate(GameObject stair, bool apply, float characterSlopeLimit = float.NaN)
        {
            var report = new StairSurfaceReport { Stair = stair };
            if (stair == null) { report.Failures.Add("沒有樓梯物件"); return report; }

            Collider collider = stair.GetComponent<Collider>();
            if (collider == null) { report.Failures.Add("樓梯根物件上沒有 Collider"); return report; }
            if (collider is MeshCollider meshCollider && meshCollider.convex)
            { report.Failures.Add("樓梯碰撞體是 convex——凸包不可能表示階梯"); return report; }

            Physics.SyncTransforms();

            if (!TryFindProfile(collider, report)) return report;
            MeasureLateralExtent(collider, report);
            if (!report.IsValid) return report;
            CheckLateralConsistency(collider, report);
            MeasureLandings(stair, report);
            if (!report.IsValid) return report;

            if (float.IsNaN(characterSlopeLimit)) characterSlopeLimit = MinCharacterSlopeLimit(stair.scene);
            float maxSlope = characterSlopeLimit - SlopeMarginDegrees;
            if (report.Profile.SlopeDegrees > maxSlope)
                report.Failures.Add($"坡度 {report.Profile.SlopeDegrees:F2}° 超過角色 slopeLimit {characterSlopeLimit:F1}° − 餘裕 {SlopeMarginDegrees}°");
            if (!report.IsValid) return report;

            BuildVertices(report);
            if (!apply) return report;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Stair Movement Surface");

            Apply(stair, report);
            if (report.IsValid)
            {
                Physics.SyncTransforms();
                CheckAppliedSurface(stair, report);
            }

            if (report.IsValid)
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
            else
            {
                Undo.RevertAllDownToGroup(undoGroup);
                report.Failures.Insert(0, "寫入後驗證未通過，已整組還原");
            }
            return report;
        }

        /// <summary>
        /// 驗證一道已套用的樓梯：重新量測（不寫入）→ 比對現有移動面是否與重新產生的結果一致 →
        /// 逐點檢查移動面與可見表面的高度差 → 檢查坡度。
        /// </summary>
        public static StairSurfaceReport Validate(GameObject stair, float characterSlopeLimit)
        {
            StairSurfaceReport report = Generate(stair, apply: false, characterSlopeLimit);
            if (!report.IsValid) return report;
            CheckAppliedSurface(stair, report);
            return report;
        }

        /// <summary>I1–I3、I5：場景中已存在的移動面是否就是現在重新產生出來的那一塊，且高差在範圍內。</summary>
        private static void CheckAppliedSurface(GameObject stair, StairSurfaceReport report)
        {
            int treadLayer = LayerMask.NameToLayer(TreadLayerName);
            int movementLayer = LayerMask.NameToLayer(MovementLayerName);
            if (treadLayer < 0 || movementLayer < 0)
            {
                report.Failures.Add($"缺少 layer：{TreadLayerName}／{MovementLayerName}");
                return;
            }

            if (stair.layer != treadLayer)
                report.Failures.Add($"樓梯碰撞體 layer 是 {LayerMask.LayerToName(stair.layer)}，應為 {TreadLayerName}");

            MeshCollider surface = FindSurfaceCollider(stair, report, movementLayer);
            if (surface == null) return;

            float drift = 0f;
            Vector3[] local = surface.sharedMesh.vertices;
            if (local.Length != report.WorldVertices.Length)
            {
                report.Failures.Add($"移動面頂點數 {local.Length}，重新產生應為 {report.WorldVertices.Length}——請重新產生");
            }
            else
            {
                for (int i = 0; i < local.Length; i++)
                    drift = Mathf.Max(drift, Vector3.Distance(surface.transform.TransformPoint(local[i]), report.WorldVertices[i]));
                report.MaxVertexDrift = drift;
                if (drift > DriftTolerance)
                    report.Failures.Add($"移動面與樓梯目前的幾何偏離 {drift:F3} m——樓梯被改過但移動面沒有重新產生");
            }

            MeasureGaps(surface, report, movementLayer);
            if (report.MinGap < -LandingTolerance)
                report.Failures.Add($"移動面低於可見表面 {-report.MinGap:F3} m（容許 {LandingTolerance}）");
            float maxAllowedGap = report.Profile.RiserHeight + LandingTolerance;
            if (report.MaxGap > maxAllowedGap)
                report.Failures.Add($"移動面高出可見表面 {report.MaxGap:F3} m，超過一級 {report.Profile.RiserHeight:F3} ＋ {LandingTolerance}");
        }

        public static List<GameObject> FindAuthoredStairs(Scene scene)
        {
            var result = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != SurfaceObjectName || t.parent == null) continue;
                    if (!result.Contains(t.parent.gameObject)) result.Add(t.parent.gameObject);
                }
            }
            return result;
        }

        /// <summary>場景中「剖面是樓梯、但沒有移動面」的碰撞體（只看根物件上的非 convex MeshCollider）。</summary>
        public static List<string> FindUnauthoredStairShapes(Scene scene)
        {
            var result = new List<string>();
            int movementLayer = LayerMask.NameToLayer(MovementLayerName);
            Physics.SyncTransforms();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (collider.convex || collider.isTrigger || !collider.enabled) continue;
                    if (collider.gameObject.layer == movementLayer) continue;
                    if (collider.GetComponentInParent<CharacterController>() != null) continue;
                    if (collider.transform.Find(SurfaceObjectName) != null) continue;
                    var probe = new StairSurfaceReport { Stair = collider.gameObject };
                    if (TryFindProfile(collider, probe)) result.Add(collider.gameObject.name);
                }
            }
            return result;
        }

        public static float MinCharacterSlopeLimit(Scene scene)
        {
            float limit = float.MaxValue;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (CharacterController cc in root.GetComponentsInChildren<CharacterController>(true))
                    limit = Mathf.Min(limit, cc.slopeLimit);
            return limit == float.MaxValue ? 45f : limit;
        }

        // ------------------------------------------------------------------ 量測

        private static bool TryFindProfile(Collider collider, StairSurfaceReport report)
        {
            Bounds bounds = collider.bounds;
            Vector3 origin = new Vector3(bounds.center.x, 0f, bounds.center.z);
            Transform t = collider.transform;
            Vector3[] axes = { Horizontal(t.right), Horizontal(t.forward) };

            StairProfile best = null;
            Vector3 bestAxis = Vector3.zero;
            string lastFailure = "找不到上升軸";
            foreach (Vector3 axis in axes)
            {
                if (axis == Vector3.zero) continue;
                foreach (float sign in new[] { 1f, -1f })
                {
                    Vector3 ascent = axis * sign;
                    StairProfile profile = StairProfileAnalyzer.Analyze(ScanLine(collider, origin, ascent, 0f));
                    if (!profile.IsValid) { lastFailure = profile.Failure; continue; }
                    if (best == null || profile.RiserCount > best.RiserCount) { best = profile; bestAxis = ascent; }
                }
            }

            if (best == null)
            {
                report.Failures.Add("不是可辨識的樓梯剖面：" + lastFailure);
                return false;
            }

            report.Profile = best;
            report.Origin = origin;
            report.Ascent = bestAxis;
            report.Lateral = Vector3.Cross(Vector3.up, bestAxis).normalized;
            return true;
        }

        private static void MeasureLateralExtent(Collider collider, StairSurfaceReport report)
        {
            StairProfile p = report.Profile;
            int mid = p.TreadHeights.Length / 2;
            float sMid = (p.RiserS[mid - 1] + p.RiserS[mid]) * 0.5f;
            float treadY = p.TreadHeights[mid];

            SpanOf(collider.bounds, report.Origin, report.Lateral, out float lMin, out float lMax);
            float first = float.NaN, last = float.NaN;
            float previous = float.NaN;
            for (float l = lMin + SampleStep * 0.5f; l < lMax; l += SampleStep)
            {
                Vector3 xz = report.Origin + report.Ascent * sMid + report.Lateral * l;
                if (!RaycastCollider(collider, xz, out float y) || Mathf.Abs(y - treadY) > StairProfileAnalyzer.TreadFlatnessTolerance)
                    continue;
                if (!float.IsNaN(previous) && l - previous > SampleStep * 2.5f)
                {
                    report.Failures.Add($"踏面 #{mid} 在橫向 {previous:F3}～{l:F3} 之間有缺口——踏面不是連續的一整片");
                    return;
                }
                if (float.IsNaN(first)) first = l;
                last = l;
                previous = l;
            }

            if (float.IsNaN(first)) { report.Failures.Add("量不到踏面寬度"); return; }
            report.LateralMin = first - SampleStep * 0.5f;
            report.LateralMax = last + SampleStep * 0.5f;
        }

        private static void CheckLateralConsistency(Collider collider, StairSurfaceReport report)
        {
            StairProfile center = report.Profile;
            foreach (float fraction in new[] { 0.1f, 0.9f })
            {
                float lateral = Mathf.Lerp(report.LateralMin, report.LateralMax, fraction);
                StairProfile side = StairProfileAnalyzer.Analyze(ScanLine(collider, report.Origin, report.Ascent, lateral));
                if (!side.IsValid) { report.Failures.Add($"橫向 {fraction:P0} 處剖面無效：{side.Failure}"); continue; }
                if (side.RiserCount != center.RiserCount)
                { report.Failures.Add($"橫向 {fraction:P0} 處有 {side.RiserCount} 級，中線有 {center.RiserCount} 級"); continue; }
                for (int k = 0; k < center.RiserCount; k++)
                {
                    if (Mathf.Abs(side.RiserS[k] - center.RiserS[k]) > LateralConsistencyTolerance ||
                        Mathf.Abs(side.TreadHeights[k + 1] - center.TreadHeights[k + 1]) > LateralConsistencyTolerance)
                    {
                        report.Failures.Add($"橫向 {fraction:P0} 處第 {k} 級與中線不一致——樓梯不是直的等寬樓梯");
                        break;
                    }
                }
            }
        }

        private static void MeasureLandings(GameObject stair, StairSurfaceReport report)
        {
            StairProfile p = report.Profile;
            int movementLayer = LayerMask.NameToLayer(MovementLayerName);
            float fromY = stair.GetComponent<Collider>().bounds.max.y + 2f;
            Scene scene = stair.scene;

            // 移動面橫跨整個寬度 ⇒ 頂底平台必須在整個寬度上都成立，不能只看中線。
            float[] fractions = { 0.5f, 0.1f, 0.9f };
            Vector3 PointAt(float s, float fraction) =>
                report.Origin + report.Ascent * s + report.Lateral * Mathf.Lerp(report.LateralMin, report.LateralMax, fraction);

            // I6 底端：以中線定義入口高度，兩側必須與它一致。
            float? bottom = VisibleSurfaceY(PointAt(p.FirstEdgeS - 0.05f, 0.5f), fromY, stair, movementLayer, scene);
            if (bottom == null) { report.Failures.Add("樓梯底端外找不到可走地面"); return; }
            report.BottomLandingY = bottom.Value;
            report.EntryRiser = p.BottomTreadHeight - bottom.Value;
            if (Mathf.Abs(report.EntryRiser - p.RiserHeight) > LandingTolerance)
                report.Failures.Add($"入口第一級（地面→最低踏面）高 {report.EntryRiser:F3} m，與樓梯級高 {p.RiserHeight:F3} 不一致（容許 {LandingTolerance}）——" +
                                    "樓梯沒有貼在底端地面上，移動面會在入口懸空超過一級");

            report.RampStartS = (report.BottomLandingY - p.NosingIntercept) / p.NosingSlope;
            if (report.RampStartS > p.FirstEdgeS + LandingTolerance)
                report.Failures.Add("踏面前緣連線在樓梯內部才落到地面——入口高度異常");

            foreach (float fraction in fractions)
            {
                for (int i = 0; i <= 2; i++)
                {
                    float s = Mathf.Lerp(report.RampStartS, p.FirstEdgeS - 0.05f, i / 2f);
                    float? y = VisibleSurfaceY(PointAt(s, fraction), fromY, stair, movementLayer, scene);
                    if (y == null || Mathf.Abs(y.Value - report.BottomLandingY) > LandingTolerance)
                    {
                        report.Failures.Add($"入口平台在 s={s:F3}、橫向 {fraction:P0} 不平（{(y == null ? "無地面" : y.Value.ToString("F3"))} vs {report.BottomLandingY:F3}）");
                        return;
                    }
                }
            }

            // I7 頂端：每個橫向位置各自分類，三處必須是同一種且（若連續）同高。
            bool first = true;
            foreach (float fraction in fractions)
            {
                float? top = VisibleSurfaceY(PointAt(p.LastEdgeS + 0.05f, fraction), fromY, stair, movementLayer, scene);
                float drop = top == null ? float.PositiveInfinity : p.TopTreadHeight - top.Value;
                StairTopLanding kind;
                if (Mathf.Abs(drop) <= LandingTolerance) kind = StairTopLanding.Continuous;
                else if (drop > p.RiserHeight + LandingTolerance) kind = StairTopLanding.OpenDrop;
                else
                {
                    report.Failures.Add($"頂端（橫向 {fraction:P0}）平台與最高踏面高差 {drop:F3} m——既不是同高平台，也不是明確的邊緣落差");
                    return;
                }

                if (first)
                {
                    report.TopLanding = kind;
                    report.TopLandingY = top ?? float.NaN;
                    first = false;
                }
                else if (kind != report.TopLanding ||
                         (kind == StairTopLanding.Continuous && Mathf.Abs(top.Value - report.TopLandingY) > LandingTolerance))
                {
                    report.Failures.Add($"頂端平台在橫向 {fraction:P0} 是 {kind}，中線是 {report.TopLanding}——寬度方向不一致");
                    return;
                }
            }
        }

        private static void MeasureGaps(MeshCollider surface, StairSurfaceReport report, int movementLayer)
        {
            float minGap = float.PositiveInfinity, maxGap = float.NegativeInfinity;
            float fromY = surface.bounds.max.y + 2f;
            foreach (float fraction in new[] { 0.1f, 0.5f, 0.9f })
            {
                float lateral = Mathf.Lerp(report.LateralMin, report.LateralMax, fraction);
                for (float s = report.RampStartS + 0.01f; s < report.Profile.LastEdgeS - 0.01f; s += 0.01f)
                {
                    Vector3 xz = report.Origin + report.Ascent * s + report.Lateral * lateral;
                    if (!RaycastCollider(surface, xz, out float movementY)) continue;
                    float? visibleY = VisibleSurfaceY(xz, fromY, null, movementLayer, surface.gameObject.scene);
                    if (visibleY == null) continue;
                    float gap = movementY - visibleY.Value;
                    minGap = Mathf.Min(minGap, gap);
                    maxGap = Mathf.Max(maxGap, gap);
                }
            }
            report.MinGap = minGap;
            report.MaxGap = maxGap;
        }

        private static MeshCollider FindSurfaceCollider(GameObject stair, StairSurfaceReport report, int movementLayer)
        {
            var surfaces = new List<Transform>();
            foreach (Transform child in stair.transform)
                if (child.name == SurfaceObjectName) surfaces.Add(child);
            if (surfaces.Count != 1)
            {
                report.Failures.Add($"應恰好有一個 {SurfaceObjectName} 子物件，實際 {surfaces.Count} 個");
                return null;
            }

            GameObject go = surfaces[0].gameObject;
            if (go.layer != movementLayer)
                report.Failures.Add($"移動面 layer 是 {LayerMask.LayerToName(go.layer)}，應為 {MovementLayerName}");
            Collider[] colliders = go.GetComponents<Collider>();
            if (colliders.Length != 1 || !(colliders[0] is MeshCollider mc) || !mc.convex || mc.sharedMesh == null)
            {
                report.Failures.Add("移動面必須恰好是一顆有 mesh 的 convex MeshCollider");
                return null;
            }
            return mc;
        }

        // ------------------------------------------------------------------ 產生

        private static void BuildVertices(StairSurfaceReport report)
        {
            StairProfile p = report.Profile;
            float yFloor = report.BottomLandingY;
            float yBase = yFloor - FloorSink;
            // 剖面（s, y）：入口落在地面 → 最後一道踢面頂端 → 頂端踏面 → 垂直下到底 → 回入口下方。
            var profile = new[]
            {
                new Vector2(report.RampStartS, yFloor),
                new Vector2(p.LastNosingS, p.TopTreadHeight),
                new Vector2(p.LastEdgeS, p.TopTreadHeight),
                new Vector2(p.LastEdgeS, yBase),
                new Vector2(report.RampStartS, yBase),
            };

            var vertices = new Vector3[profile.Length * 2];
            float[] laterals = { report.LateralMin, report.LateralMax };
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < profile.Length; i++)
                {
                    Vector3 xz = report.Origin + report.Ascent * profile[i].x + report.Lateral * laterals[side];
                    vertices[side * profile.Length + i] = new Vector3(xz.x, profile[i].y, xz.z);
                }
            }
            report.WorldVertices = vertices;
        }

        private static void Apply(GameObject stair, StairSurfaceReport report)
        {
            int treadLayer = LayerMask.NameToLayer(TreadLayerName);
            int movementLayer = LayerMask.NameToLayer(MovementLayerName);
            if (treadLayer < 0 || movementLayer < 0)
            {
                report.Failures.Add($"缺少 layer：{TreadLayerName}／{MovementLayerName}——先設定專案 layer（docs/44 §4）");
                return;
            }

            Transform existing = stair.transform.Find(SurfaceObjectName);
            GameObject surface;
            if (existing == null)
            {
                surface = new GameObject(SurfaceObjectName);
                Undo.RegisterCreatedObjectUndo(surface, "Generate Stair Movement Surface");
                Undo.SetTransformParent(surface.transform, stair.transform, "Generate Stair Movement Surface");
            }
            else
            {
                surface = existing.gameObject;
                Undo.RecordObject(surface, "Generate Stair Movement Surface");
                Undo.RecordObject(surface.transform, "Generate Stair Movement Surface");
            }

            surface.transform.localPosition = Vector3.zero;
            surface.transform.localRotation = Quaternion.identity;
            surface.transform.localScale = Vector3.one;
            surface.layer = movementLayer;
            GameObjectUtility.SetStaticEditorFlags(surface, GameObjectUtility.GetStaticEditorFlags(stair));

            var local = new Vector3[report.WorldVertices.Length];
            for (int i = 0; i < local.Length; i++) local[i] = surface.transform.InverseTransformPoint(report.WorldVertices[i]);

            var mesh = new Mesh { name = "StairMovementSurface (" + stair.name + ")" };
            mesh.vertices = local;
            mesh.triangles = PrismTriangles(local.Length / 2);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            MeshCollider collider = surface.GetComponent<MeshCollider>();
            if (collider == null) collider = Undo.AddComponent<MeshCollider>(surface);
            Undo.RecordObject(collider, "Generate Stair Movement Surface");
            collider.sharedMesh = null;
            collider.convex = true;
            collider.isTrigger = false;
            collider.sharedMesh = mesh;

            Undo.RecordObject(stair, "Generate Stair Movement Surface");
            stair.layer = treadLayer;

            EditorSceneManager.MarkSceneDirty(stair.scene);
        }

        private static int[] PrismTriangles(int n)
        {
            var tris = new List<int>();
            for (int i = 1; i < n - 1; i++)
            {
                tris.Add(0); tris.Add(i + 1); tris.Add(i);
                tris.Add(n); tris.Add(n + i); tris.Add(n + i + 1);
            }
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                tris.Add(i); tris.Add(next); tris.Add(n + next);
                tris.Add(i); tris.Add(n + next); tris.Add(n + i);
            }
            return tris.ToArray();
        }

        // ------------------------------------------------------------------ 查詢工具

        private static List<Vector2> ScanLine(Collider collider, Vector3 origin, Vector3 ascent, float lateral)
        {
            Vector3 lateralAxis = Vector3.Cross(Vector3.up, ascent).normalized;
            SpanOf(collider.bounds, origin, ascent, out float sMin, out float sMax);
            var samples = new List<Vector2>();
            for (float s = sMin + SampleStep * 0.5f; s < sMax; s += SampleStep)
            {
                Vector3 xz = origin + ascent * s + lateralAxis * lateral;
                if (RaycastCollider(collider, xz, out float y)) samples.Add(new Vector2(s, y));
            }
            return samples;
        }

        private static bool RaycastCollider(Collider collider, Vector3 xz, out float y)
        {
            Bounds b = collider.bounds;
            var ray = new Ray(new Vector3(xz.x, b.max.y + 1f, xz.z), Vector3.down);
            if (collider.Raycast(ray, out RaycastHit hit, b.size.y + 2f))
            {
                y = hit.point.y;
                return true;
            }
            y = 0f;
            return false;
        }

        /// <summary>
        /// 某點正下方「看得到的」最高表面：只看樓梯所在場景；排除移動面 layer 與角色；
        /// 可選擇排除樓梯本身（量平台時）。
        /// </summary>
        private static float? VisibleSurfaceY(Vector3 xz, float fromY, GameObject excludeRoot, int movementLayer, Scene scene)
        {
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(xz.x, fromY, xz.z), Vector3.down,
                fromY + 100f, ~0, QueryTriggerInteraction.Ignore);
            float? best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.gameObject.scene != scene) continue;
                if (hit.collider.gameObject.layer == movementLayer) continue;
                if (excludeRoot != null && hit.collider.transform.IsChildOf(excludeRoot.transform)) continue;
                if (hit.collider.GetComponentInParent<CharacterController>() != null) continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = hit.point.y;
                }
            }
            return best;
        }

        private static void SpanOf(Bounds bounds, Vector3 origin, Vector3 axis, out float min, out float max)
        {
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;
            Vector3 c = bounds.center, e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, 0f, (i & 2) == 0 ? -e.z : e.z);
                float s = Vector3.Dot(new Vector3(corner.x, 0f, corner.z) - origin, axis);
                min = Mathf.Min(min, s);
                max = Mathf.Max(max, s);
            }
        }

        private static Vector3 Horizontal(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero;
        }
    }
}
