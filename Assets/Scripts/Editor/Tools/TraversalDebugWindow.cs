#if UNITY_EDITOR
using Project.Core.Environment;
using Project.Presentation.Motion;
using UnityEditor;
using UnityEngine;

namespace Project.Editor.Tools
{
    /// <summary>
    /// Traversal 的**數值面板**（Window ▸ Project ▸ Traversal Debug）。
    ///
    /// 為什麼在這裡而不是 Game View：traversal 的診斷資訊量天生就大（三層決策 ＋ plan ＋ 執行），
    /// 全部塞進 Game View 的 <c>TextMesh</c> 會擋住正在觀察的畫面本身——而畫面才是要看的東西。
    /// 分工固定為三層：
    /// <list type="bullet">
    /// <item><b>Scene View gizmo／runtime line</b>：空間關係（探線、ledge、corridor、entry band、
    ///   original vs warped 軌跡、knot、手部誤差向量）。**能畫的就畫，不寫成字。**</item>
    /// <item><b>本視窗</b>：純量與決策鏈——為什麼進／沒進、走哪條 plan、差多少。</item>
    /// <item><b>Game View</b>：預設**什麼都不顯示**。真的需要時才開 MotionDriver 上的
    ///   <c>drawTraversalMotionDebug</c>／Probe 上的 <c>drawTraversalRuntimeLines</c>。</item>
    /// </list>
    ///
    /// 只讀既有 public 屬性，不查 Physics、不改任何狀態。
    /// </summary>
    public sealed class TraversalDebugWindow : EditorWindow
    {
        private const float RepaintIntervalSeconds = 0.05f;

        private TraversalProbe _probe;
        private MotionDriver _driver;
        private double _nextRepaint;
        private Vector2 _scroll;

        [MenuItem("Window/Project/Traversal Debug")]
        private static void Open()
        {
            TraversalDebugWindow window = GetWindow<TraversalDebugWindow>("Traversal");
            window.minSize = new Vector2(320f, 240f);
        }

        private void OnEnable() => EditorApplication.update += Poll;
        private void OnDisable() => EditorApplication.update -= Poll;

        private void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + RepaintIntervalSeconds;
            Repaint();
        }

        private void OnGUI()
        {
            ResolveTargets();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_probe == null)
            {
                EditorGUILayout.HelpBox(
                    "場上找不到 TraversalProbe。進 Play Mode，或選取帶有 TraversalProbe 的角色。",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawDecisionChain();
            DrawPlan();
            DrawExecution();

            EditorGUILayout.EndScrollView();
        }

        private void ResolveTargets()
        {
            if (_probe != null && _driver != null) return;
            if (Selection.activeGameObject != null)
            {
                _probe = Selection.activeGameObject.GetComponentInParent<TraversalProbe>();
                if (_probe != null) _driver = _probe.GetComponent<MotionDriver>();
            }
            if (_probe != null) return;
            _probe = FindFirstObjectByType<TraversalProbe>();
            if (_probe != null) _driver = _probe.GetComponent<MotionDriver>();
        }

        // =====================================================================
        // ① 決策鏈：Classify → Entry → Select。回答「為什麼發生／沒發生」。
        // =====================================================================
        private void DrawDecisionChain()
        {
            TraversalCandidate candidate = _probe.Candidate;
            TraversalEntryEvaluation entry = _probe.EntryEvaluation;
            TraversalSelectionEvaluation selection = _probe.SelectionEvaluation;

            Section("Decision chain");

            Row("1 Classify",
                candidate.IsValid ? candidate.Kind.ToString() : $"None — {candidate.RejectReason}",
                candidate.IsValid);
            Indent($"direction source = {candidate.DirectionSource}" +
                   $"   height = {candidate.Height:F2} m   depth = {candidate.Depth:F2} m");

            bool entryEvaluated = entry.DistanceBand != TraversalEntryDistanceBand.Invalid ||
                                  entry.RejectReason != TraversalEntryRejectReason.None;
            if (!entryEvaluated)
            {
                Row("2 Entry", "not evaluated (press Jump)", true);
            }
            else
            {
                Row("2 Entry",
                    entry.Executable ? $"ACCEPT — {entry.DistanceBand}" : $"REJECT — {entry.RejectReason}",
                    entry.Executable);
                Indent($"wall {entry.LongitudinalDistance:F2} m " +
                       $"(legal {entry.MinimumLongitudinalDistance:F2}–" +
                       $"{entry.MaximumLongitudinalDistance:F2})");
                Indent($"lateral {entry.LateralError * 100f:F1} cm   " +
                       $"facing {entry.FacingAngleError:F1}°   " +
                       $"capsule clearance {entry.CapsuleWallClearance * 100f:F1} cm");
            }

            if (!selection.HasResult)
            {
                Row("3 Select", "not evaluated (press Jump)", true);
            }
            else
            {
                Row("3 Select",
                    selection.PreferTraversal ? "TRAVERSAL" : "NORMAL JUMP",
                    selection.PreferTraversal);
                Indent($"reason = {selection.Reason}");
                if (selection.HasReachEvidence)
                {
                    Indent($"ledge {selection.CandidateHeight:F2} m vs " +
                           $"jump reach {selection.SafeReach:F2} m " +
                           $"(apex {selection.TheoreticalApex:F2} m)");
                }
            }

            // ④ Animation Fitting（docs/24 §8）——「動畫用什麼速率播、落點取哪一個」必須看得到，
            //    否則它會變成第二個「推導值從來沒生效」的靜默層（docs/22 §13.8 的教訓）。
            TraversalAnimationFit fit = _probe.AnimationFit;
            Row("4 Fit",
                fit.IsFitted
                    ? $"rate {fit.PlaybackRate:F3}"
                    : $"rate 1.000 — {fit.Reason}",
                fit.IsFitted);
            if (fit.Reason != TraversalFitReason.MissingBakeData)
            {
                Indent($"approach animated {Metres(fit.ApproachAnimatedDistance)} → " +
                       $"required {Metres(fit.ApproachRequiredDistance)}" +
                       (fit.IsFitted && !Mathf.Approximately(fit.RawPlaybackRate, fit.PlaybackRate)
                           ? $"   (raw {fit.RawPlaybackRate:F3}, CLAMPED)"
                           : string.Empty));
                Indent($"landing animated {Metres(fit.LandingAnimatedDepth)}   " +
                       $"probe verified {Metres(fit.LandingAvailableDepth)}   " +
                       $"committed {Metres(fit.LandingCommittedDepth)}");
            }

            Indent($"sensing reach {_probe.EffectiveForwardScanDistance:F2} m   " +
                   $"landing scan {_probe.EffectiveLandingScanDepth:F2} m   (derived from bakes)");
        }

        private static string Metres(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? "—" : $"{value:F3} m";

        // =====================================================================
        // ② Plan：走哪條、為什麼不是另一條、correction 預算夠不夠。
        // =====================================================================
        private void DrawPlan()
        {
            Section("Plan");
            if (_driver == null)
            {
                Indent("找不到同物件上的 MotionDriver。");
                return;
            }

            TraversalWarpPlanDiagnostics diagnostics = _driver.ActiveTraversalPlanDiagnostics;
            if (!diagnostics.HasResult)
            {
                Indent("尚未 commit（不在 traversal 中）。");
                return;
            }

            Row("mode", diagnostics.Mode.ToString(),
                diagnostics.Mode == TraversalPlanMode.Piecewise ||
                diagnostics.Mode == TraversalPlanMode.EndpointFallback);

            if (diagnostics.PiecewiseRejection != TraversalWarpPlanRejection.None)
                Indent($"piecewise unavailable: {diagnostics.PiecewiseRejection}");
            if (diagnostics.EndpointRejection != TraversalWarpPlanRejection.None)
                Indent($"endpoint failed: {diagnostics.EndpointRejection}");

            Indent($"correction window {diagnostics.WarpStartNormalizedTime:F3}–" +
                   $"{diagnostics.WarpEndNormalizedTime:F3}" +
                   (diagnostics.WarpEndIsDataDerived ? "  (derived from bake)" : "  (authored)"));

            if (!float.IsNaN(diagnostics.RequiredHorizontalCorrection))
            {
                bool ok = diagnostics.RequiredHorizontalCorrection <=
                          diagnostics.MaxHorizontalCorrection;
                Row("H correction",
                    $"{diagnostics.RequiredHorizontalCorrection:F2} / " +
                    $"{diagnostics.MaxHorizontalCorrection:F2} m", ok);
            }
            if (!float.IsNaN(diagnostics.RequiredVerticalCorrection))
            {
                bool ok = Mathf.Abs(diagnostics.RequiredVerticalCorrection) <=
                          diagnostics.MaxVerticalCorrection;
                Row("V correction",
                    $"{diagnostics.RequiredVerticalCorrection:+0.00;-0.00; 0.00} / " +
                    $"{diagnostics.MaxVerticalCorrection:F2} m", ok);
            }
        }

        // =====================================================================
        // ③ 執行：時間基準、已套用的修正、被擋掉的位移。
        // =====================================================================
        private void DrawExecution()
        {
            Section("Execution");
            if (_driver == null || !_driver.HasActiveTraversalPlan)
            {
                Indent("沒有進行中的 traversal。");
                return;
            }

            TraversalWarpPlan plan = _driver.ActiveTraversalPlan;
            float n = _driver.ActiveTraversalNormalizedTime;

            Indent($"t = {n:F3}");

            float rootTime = plan.TrajectoryNormalizedAt(n);
            float desync = rootTime - Mathf.Clamp01(n);
            Row("root/pose dt", $"{desync:+0.0000;-0.0000; 0.0000}", Mathf.Abs(desync) < 1e-4f);

            if (TraversalWarpPlan.TryEvaluateOriginal(
                    plan.Bake, plan.StartPosition, plan.StartForward, n,
                    out Vector3 original, out _) &&
                plan.TryEvaluate(n, out Vector3 warped, out _))
            {
                Indent($"dY applied {warped.y - original.y:+0.00;-0.00; 0.00} m" +
                       $"   to destination {plan.TargetPosition.y - warped.y:+0.00;-0.00; 0.00} m");
                if (!plan.IsPiecewise)
                    Indent($"correction blend {plan.CorrectionWeightAt(n) * 100f:F0} %");
            }

            if (plan.IsPiecewise)
            {
                Indent($"hand residual  L {plan.ContactTargets.LeftHandResidualError * 100f:F1} cm" +
                       $"   R {plan.ContactTargets.RightHandResidualError * 100f:F1} cm");
            }

            TraversalExecutionResult result = _driver.LastTraversalExecutionResult;
            if (!result.HasResult) return;
            float blocked = result.BlockedDisplacement.magnitude;
            Row("blocked",
                $"{blocked * 100f:F1} cm of {result.RequestedDisplacement.magnitude * 100f:F1} cm" +
                $"   flags {result.CollisionFlags}",
                blocked < 0.002f);
        }

        // =====================================================================
        private static void Section(string title)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void Row(string label, string value, bool ok)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = ok
                ? previous
                : (EditorGUIUtility.isProSkin ? new Color(1f, 0.55f, 0.4f) : new Color(0.7f, 0.1f, 0f));
            EditorGUILayout.LabelField(label, value);
            GUI.contentColor = previous;
        }

        private static void Indent(string text)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(text);
            EditorGUI.indentLevel--;
        }
    }
}
#endif
