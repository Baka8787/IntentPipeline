using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// 停步站直階段（docs/05 §3.5.4.6）：Moving → EarlyRecovery → StopProgress → Upright → Return → Moving。
    /// 數值刻意固定（探針／CSV 讀整數）：新增的 EarlyRecovery 排在最後。
    /// </summary>
    public enum PosturePhase
    {
        Moving = 0,
        StopProgress = 1,
        Upright = 2,
        Return = 3,
        EarlyRecovery = 4,
    }

    public enum PostureFoot
    {
        None = 0,
        Left = 1,
        Right = 2,
    }

    /// <summary>T3 解完後提供給姿勢狀態機的單腳觀測；不讀動畫 state 名稱。</summary>
    public struct PostureFootInputs
    {
        public FootLockPhase Phase;
        public float Beta;
        public float HorizontalSpeed;
        public float VerticalSpeed;
        public bool LiveContact;
        public bool Reliable;
    }

    /// <summary>站直原型的可調值（FootIKSettings 轉交；純資料，EditMode 測試可直接建構）。</summary>
    public struct PostureTuning
    {
        public float MovingSpeed;
        public float FinalSupportSpeedMax;
        public float UprightOmega;
        public float ReturnOmega;
        public float HandbackTolerance;
        public float HandbackSpeed;
        public float EarlyLiftOmega;
        public float EarlyLiftRetreatSpeed;
        public float EarlyReleaseMinRootSpeed;
        public float TrailingCapBand;
        public float TrailingCapReleaseOmega;
        public float RiseDurationNearSettle;
        public float RiseDurationSettling;
        public float RiseDurationDefault;

        public static PostureTuning Default => new PostureTuning
        {
            MovingSpeed = 5f,
            FinalSupportSpeedMax = 1.35f,
            UprightOmega = 6f,
            ReturnOmega = 15f,
            HandbackTolerance = 0.01f,
            HandbackSpeed = 0.05f,
            EarlyLiftOmega = 6f,
            EarlyLiftRetreatSpeed = 1f,
            EarlyReleaseMinRootSpeed = 0.1f,
            TrailingCapBand = 0.03f,
            TrailingCapReleaseOmega = 10f,
            RiseDurationNearSettle = 0.18f,
            RiseDurationSettling = 0.20f,
            RiseDurationDefault = 0.40f,
        };
    }

    /// <summary>刻意跨帧保存的姿勢狀態（只存數值）。失效時一律 <see cref="Reset"/>。</summary>
    public struct PostureState
    {
        public PosturePhase Phase;
        public PostureFoot PrimaryFoot;
        /// <summary>骨盆 offset 的速度（m/s）；跨 EarlyRecovery／StopProgress／Upright／Return 承接。</summary>
        public float Velocity;
        public bool TrailingWasUnreliable;
        public bool TrailingSettled;
        public bool PreviousLeftReliable;
        public bool PreviousRightReliable;
        public bool PreviousMovementRequested;
        public bool HasPreviousMovement;
        public float PreviousOutput;
        public bool HasPreviousOutput;
        /// <summary>上一帧畫面上的髖（pre-IK 髖 ＋ 當帧已套用的 offset）；用來量第一支撐時的承接速度。</summary>
        public Vector3 PreviousDisplayedLeftHip;
        public Vector3 PreviousDisplayedRightHip;
        public bool HasPreviousDisplayedHips;

        // ① Early Stop Recovery
        public float EarlyBase;
        public float EarlyLift;
        public float EarlyLiftVelocity;
        public float ReleaseRootSpeed;

        // ② Primary Support Rise
        public Vector3 PrimarySupportPosition;
        public Vector3 PrimaryHipPosition;
        public float PrimaryUpperLegLength;
        public float PrimaryLowerLegLength;
        public float PrimaryStartLegLength;
        public float PrimaryTargetLegLength;
        /// <summary>第一支撐當帧畫面上的支撐腿伸展速度（m/s）＝ 承接的初速。</summary>
        public float PrimaryExtensionSpeed;
        public float PrimaryRiseDuration;
        public float PrimaryElapsed;
        public float PrimaryDesiredLegLength;
        public float PrimaryStartKneeAngle;
        public float PrimaryTargetKneeAngle;

        // ③ Trailing prospective reach cap
        public bool TrailingLatched;
        public Vector3 TrailingLatchPosition;
        public float TrailingCapBand;
        public float TrailingCapGap;
        public float TrailingCapGapVelocity;

        // 觀測用（探針讀取；每帧覆寫，決策只讀上面的狀態）。
        public float DebugEnvelope;
        public float DebugPreferred;
        public float DebugHardLimit;
        public float DebugProgress;
        public float DebugRootSpeed;
        public bool DebugLeftReliable;
        public bool DebugRightReliable;
        public bool DebugLeftLive;
        public bool DebugRightLive;
        public float DebugLeftHorizontalSpeed;
        public float DebugRightHorizontalSpeed;
        public float DebugLeftVerticalSpeed;
        public float DebugRightVerticalSpeed;
        public PosturePhase DebugPhaseBefore;
        public PostureFoot DebugPrimaryBefore;
        public float DebugStart;
        public float DebugUprightBound;
        public float DebugCandidateTarget;
        public float DebugPreHardOutput;
        public float DebugOutput;
        public float DebugVelocityBefore;
        public float DebugEarlyLift;
        public float DebugTrailingCap;
        public bool DebugEnteredEarlyRecovery;
        public bool DebugEnteredStopProgress;
        public bool DebugLatchedUpright;
        public bool DebugEnteredReturn;
        public bool DebugSpringApplied;
        public bool DebugTrailingCapApplied;
        public bool DebugHardClampApplied;
        public bool DebugVelocityReset;

        public void Reset() => this = default;
    }

    /// <summary>本帧給姿勢狀態機的輸入（FootIKController 在 T3 解完兩腳後準備）。</summary>
    public struct PostureInputs
    {
        public float Envelope;
        public float Preferred;
        public float HardLimit;
        public float RootSpeed;
        public bool MovementRequested;
        public float PreferredExtension;
        /// <summary>下一帧（本帧輸出套用時）的 pre-IK 髖位：一帧外插，與 T3 腳目標同一套用帧。</summary>
        public Vector3 LeftHip;
        public Vector3 RightHip;
        /// <summary>本帧的 pre-IK 髖位（未外插）；加上本帧已套用的 offset ＝ 畫面上的髖。</summary>
        public Vector3 LeftHipCurrent;
        public Vector3 RightHipCurrent;
        public Vector3 LeftTarget;
        public Vector3 RightTarget;
        public float LeftUpperLegLength;
        public float LeftLowerLegLength;
        public float RightUpperLegLength;
        public float RightLowerLegLength;
        public PostureFootInputs Left;
        public PostureFootInputs Right;
    }

    /// <summary>
    /// 停步站直的純函數：①放開後依 root 減速進度逐步釋放包絡深度；②第一個可靠終端支撐腳建立後，由固定支撐腿的
    /// 伸展驅動骨盆（承接進入時的速度）；③已 T3 鎖定的尾腳只當前瞻可達上限；④雙腳落定只做最小修正。
    /// 不發物理查詢、不讀 Unity 時間、不讀 Locomotion／FSM 狀態。
    /// </summary>
    public static class StandingPostureSolver
    {
        public static float LegBound(Vector3 hip, Vector3 ankleTarget, float reach)
        {
            float dx = hip.x - ankleTarget.x, dz = hip.z - ankleTarget.z;
            float h2 = dx * dx + dz * dz;
            float r2 = reach * reach;
            float vertical = h2 >= r2 ? 0f : Mathf.Sqrt(r2 - h2);
            return vertical - (hip.y - ankleTarget.y);
        }

        /// <summary>Fixed support foot + desired hip-to-ankle length → vertical pelvis offset for the live primary hip.</summary>
        public static float SupportLegPelvisOffset(Vector3 hip, Vector3 supportPosition, float desiredLength)
        {
            float dx = hip.x - supportPosition.x, dz = hip.z - supportPosition.z;
            float horizontal2 = dx * dx + dz * dz;
            float length2 = desiredLength * desiredLength;
            float vertical = horizontal2 >= length2 ? 0f : Mathf.Sqrt(length2 - horizontal2);
            return supportPosition.y + vertical - hip.y;
        }

        /// <summary>Knee flexion in degrees: 0 is straight, larger values are more bent.</summary>
        public static float KneeAngle(float hipToAnkleLength, float upperLegLength, float lowerLegLength)
        {
            if (upperLegLength <= 0f || lowerLegLength <= 0f) return 0f;
            float distance = Mathf.Clamp(hipToAnkleLength,
                Mathf.Abs(upperLegLength - lowerLegLength), upperLegLength + lowerLegLength);
            float cosine = Mathf.Clamp((upperLegLength * upperLegLength + lowerLegLength * lowerLegLength
                                         - distance * distance) / (2f * upperLegLength * lowerLegLength), -1f, 1f);
            return 180f - Mathf.Acos(cosine) * Mathf.Rad2Deg;
        }

        public static void SpringStep(ref float position, ref float velocity, float target, float omega, float dt)
        {
            float acceleration = omega * omega * (target - position) - 2f * omega * velocity;
            velocity += acceleration * dt;
            position += velocity * dt;
        }

        /// <summary>Cubic Hermite：起點位置／速度（<paramref name="startTangent"/> 已乘上時長）、終點位置、終點速度 0。</summary>
        public static float HermiteToRest(float start, float startTangent, float end, float s)
        {
            float s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * start + (s3 - 2f * s2 + s) * startTangent + (-2f * s3 + 3f * s2) * end;
        }

        /// <summary>
        /// 單邊 C1 飽和：<c>value ≤ cap − band</c> 原樣返回；以上以指數逼近 cap，永遠不超過 cap。
        /// band ≤ 0 ⇒ 退化為 <c>min(value, cap)</c>。
        /// </summary>
        public static float SaturateBelow(float value, float cap, float band)
        {
            if (band <= 1e-6f) return Mathf.Min(value, cap);
            float x = value - (cap - band);
            return x <= 0f ? value : cap - band * Mathf.Exp(-x / band);
        }

        public static float UprightBound(in PostureInputs input)
            => Mathf.Min(Mathf.Max(input.Envelope, Mathf.Min(input.Preferred, 0f)), input.HardLimit);

        /// <summary>第一支撐的支撐腿伸展時長（docs/05 §3.5.4.6 duration 表：依尾腳的 T3 相位決定是否已接近落定）。</summary>
        public static float RiseDuration(in PostureFootInputs trailing, in PostureTuning tuning)
        {
            if (trailing.Phase == FootLockPhase.Unlocked && trailing.Beta >= 0.8f && trailing.VerticalSpeed < 0f)
                return tuning.RiseDurationNearSettle;
            if ((trailing.LiveContact && trailing.Phase == FootLockPhase.Locked)
                || (trailing.Phase == FootLockPhase.Releasing && trailing.Beta >= 0.6f && trailing.VerticalSpeed < 0f))
                return tuning.RiseDurationSettling;
            return tuning.RiseDurationDefault;
        }

        public static void Step(ref PostureState state, in PostureInputs input, float dt, ref float pelvis,
            in PostureTuning tuning)
        {
            if (dt <= 0f) return;

            float start = pelvis;
            state.DebugPhaseBefore = state.Phase;
            state.DebugPrimaryBefore = state.PrimaryFoot;
            state.DebugStart = start;
            state.DebugUprightBound = UprightBound(in input);
            state.DebugCandidateTarget = start;
            state.DebugPreHardOutput = start;
            state.DebugOutput = start;
            state.DebugVelocityBefore = state.Velocity;
            state.DebugEarlyLift = 0f;
            state.DebugTrailingCap = float.NaN;
            state.DebugEnteredEarlyRecovery = false;
            state.DebugEnteredStopProgress = false;
            state.DebugLatchedUpright = false;
            state.DebugEnteredReturn = false;
            state.DebugSpringApplied = false;
            state.DebugTrailingCapApplied = false;
            state.DebugHardClampApplied = false;
            state.DebugVelocityReset = false;

            Vector3 displayedLeftHip = input.LeftHipCurrent + Vector3.up * start;
            Vector3 displayedRightHip = input.RightHipCurrent + Vector3.up * start;
            bool leftEdge = input.Left.Reliable && !state.PreviousLeftReliable;
            bool rightEdge = input.Right.Reliable && !state.PreviousRightReliable;
            bool canStart = !input.MovementRequested && input.RootSpeed <= tuning.FinalSupportSpeedMax;
            bool releaseEdge = state.HasPreviousMovement && state.PreviousMovementRequested && !input.MovementRequested;

            // ① 放開移動意圖 ⇒ Early Stop Recovery（從 Moving，或仍在追隨包絡的 Return；root 幾乎不動時不是 locomotion 停步）。
            if ((state.Phase == PosturePhase.Moving || state.Phase == PosturePhase.Return) && releaseEdge
                && !float.IsInfinity(input.RootSpeed) && input.RootSpeed >= tuning.EarlyReleaseMinRootSpeed)
            {
                state.Phase = PosturePhase.EarlyRecovery;
                state.EarlyBase = pelvis;
                state.EarlyLift = 0f;
                state.EarlyLiftVelocity = 0f;
                state.ReleaseRootSpeed = input.RootSpeed;
                state.DebugEnteredEarlyRecovery = true;
            }

            // ② 新可靠落腳邊沿（沒有移動意圖、root 已慢到可能是最後支撐）⇒ Primary Support Rise。
            if ((state.Phase == PosturePhase.Moving || state.Phase == PosturePhase.EarlyRecovery
                 || state.Phase == PosturePhase.Return)
                && canStart && (leftEdge || rightEdge))
            {
                BeginPrimaryRise(ref state, in input, dt, displayedLeftHip, displayedRightHip, leftEdge, rightEdge, in tuning);
            }

            bool enteredReturn = false;
            if (state.Phase == PosturePhase.EarlyRecovery && input.MovementRequested)
            {
                state.Phase = PosturePhase.Return;
                enteredReturn = true;
            }
            else if (state.Phase == PosturePhase.StopProgress)
            {
                bool primaryReliable = state.PrimaryFoot == PostureFoot.Left ? input.Left.Reliable : input.Right.Reliable;
                bool trailingReliable = state.PrimaryFoot == PostureFoot.Left ? input.Right.Reliable : input.Left.Reliable;
                bool trailingEdge = state.PrimaryFoot == PostureFoot.Left ? rightEdge : leftEdge;
                state.TrailingWasUnreliable |= !trailingReliable;
                if (input.MovementRequested || !primaryReliable)
                {
                    state.Phase = PosturePhase.Return;
                    enteredReturn = true;
                }
                else if (state.TrailingWasUnreliable && trailingEdge)
                {
                    state.TrailingSettled = true;
                }
            }
            else if (state.Phase == PosturePhase.Upright
                     && (input.MovementRequested || !input.Left.Reliable || !input.Right.Reliable))
            {
                state.Phase = PosturePhase.Return;
                enteredReturn = true;
            }
            if (enteredReturn)
            {
                // 取消時放掉前瞻上限與 early lift：位置與速度原樣交給 Return（只會往下限的上限，移除不會上跳）。
                state.TrailingLatched = false;
                state.TrailingCapBand = -1f;
                state.TrailingCapGap = 0f;
                state.TrailingCapGapVelocity = 0f;
                state.EarlyLift = 0f;
                state.EarlyLiftVelocity = 0f;
            }
            state.DebugEnteredReturn = enteredReturn;

            switch (state.Phase)
            {
                case PosturePhase.Moving:
                    pelvis = Mathf.MoveTowards(pelvis, input.Envelope, tuning.MovingSpeed * dt);
                    state.DebugCandidateTarget = input.Envelope;
                    state.DebugPreHardOutput = pelvis;
                    state.Velocity = 0f;
                    state.DebugVelocityReset = state.DebugVelocityBefore != 0f;
                    state.DebugProgress = 0f;
                    break;

                case PosturePhase.EarlyRecovery:
                    StepEarlyRecovery(ref state, in input, dt, ref pelvis, in tuning);
                    state.Velocity = (pelvis - start) / dt;
                    break;

                case PosturePhase.StopProgress:
                    StepPrimaryRise(ref state, in input, dt, ref pelvis, in tuning);
                    state.Velocity = (pelvis - start) / dt;
                    if (state.PrimaryElapsed >= state.PrimaryRiseDuration && state.TrailingSettled)
                    {
                        state.Phase = PosturePhase.Upright;
                        state.DebugLatchedUpright = true;
                    }
                    break;

                case PosturePhase.Upright:
                {
                    // ④ Final Standing Settle：只做兩腳的最小修正，不引入新的大目標。
                    float target = state.DebugUprightBound;
                    state.DebugCandidateTarget = target;
                    if (target > pelvis)
                    {
                        SpringStep(ref pelvis, ref state.Velocity, target, tuning.UprightOmega, dt);
                        state.DebugSpringApplied = true;
                    }
                    else if (state.Velocity > 0f)
                    {
                        state.Velocity = 0f;
                        state.DebugVelocityReset = true;
                    }

                    state.DebugPreHardOutput = pelvis;
                    if (pelvis > input.HardLimit)
                    {
                        state.DebugHardClampApplied = true;
                        pelvis = input.HardLimit;
                        if (state.Velocity > 0f)
                        {
                            state.Velocity = 0f;
                            state.DebugVelocityReset = true;
                        }
                    }
                    state.DebugProgress = 1f;
                    break;
                }

                case PosturePhase.Return:
                {
                    state.DebugCandidateTarget = input.Envelope;
                    SpringStep(ref pelvis, ref state.Velocity, input.Envelope, tuning.ReturnOmega, dt);
                    state.DebugSpringApplied = true;
                    state.DebugPreHardOutput = pelvis;
                    // 可達上限只管「高於包絡」的部分：Moving 本來就坐在包絡上、不看 C。
                    // 跑下樓時支撐加權的 C 會瞬間掉到包絡以下；若照 C 夾，Return 會比 Moving 還低、單帧下拉十幾公分。
                    // 頂到上限時與 ① 相同：以有限速度退讓（彈簧本身下降更快就照彈簧），不瞬間下拉。
                    float returnLimit = Mathf.Max(input.HardLimit, input.Envelope);
                    if (pelvis > returnLimit)
                    {
                        state.DebugHardClampApplied = true;
                        pelvis = Mathf.Max(returnLimit, Mathf.Min(pelvis, start - tuning.EarlyLiftRetreatSpeed * dt));
                        state.Velocity = Mathf.Min(state.Velocity, (pelvis - start) / dt);
                        state.DebugVelocityReset = true;
                    }
                    // 交回 Moving 仍要求貼近且幾乎靜止：E 在動時 Return 的彈簧本身就是比 MoveTowards 更平順的追隨，
                    // 提早交回會把彈簧的追隨落差（2v/ω）變成 MoveTowards 的單帧跳動。
                    if (!enteredReturn
                        && Mathf.Abs(pelvis - input.Envelope) < tuning.HandbackTolerance
                        && Mathf.Abs(state.Velocity) < tuning.HandbackSpeed)
                    {
                        state.Phase = PosturePhase.Moving;
                        state.PrimaryFoot = PostureFoot.None;
                        state.Velocity = 0f;
                        state.DebugVelocityReset = true;
                        state.DebugProgress = 0f;
                    }
                    break;
                }
            }

            state.DebugOutput = pelvis;

            state.PreviousLeftReliable = input.Left.Reliable;
            state.PreviousRightReliable = input.Right.Reliable;
            state.PreviousMovementRequested = input.MovementRequested;
            state.HasPreviousMovement = true;
            state.PreviousOutput = start;
            state.HasPreviousOutput = true;
            state.PreviousDisplayedLeftHip = displayedLeftHip;
            state.PreviousDisplayedRightHip = displayedRightHip;
            state.HasPreviousDisplayedHips = true;
            state.DebugRootSpeed = input.RootSpeed;
            state.DebugLeftReliable = input.Left.Reliable;
            state.DebugRightReliable = input.Right.Reliable;
            state.DebugLeftLive = input.Left.LiveContact;
            state.DebugRightLive = input.Right.LiveContact;
            state.DebugLeftHorizontalSpeed = input.Left.HorizontalSpeed;
            state.DebugRightHorizontalSpeed = input.Right.HorizontalSpeed;
            state.DebugLeftVerticalSpeed = input.Left.VerticalSpeed;
            state.DebugRightVerticalSpeed = input.Right.VerticalSpeed;
        }

        /// <summary>
        /// ① 抬升量疊在原封不動的移動包絡路徑上：<c>w＝1−rootSpeed/releaseRootSpeed</c>（運動學停步進度），
        /// 目標 <c>w·max(0, U−base)</c>，上限 <c>max(0, C−base)</c>；頂到上限時以有限速度退讓，不瞬間下拉。
        /// </summary>
        private static void StepEarlyRecovery(ref PostureState state, in PostureInputs input, float dt, ref float pelvis,
            in PostureTuning tuning)
        {
            state.EarlyBase = Mathf.MoveTowards(state.EarlyBase, input.Envelope, tuning.MovingSpeed * dt);
            float progress = float.IsInfinity(input.RootSpeed) || state.ReleaseRootSpeed <= 0f
                ? 0f
                : Mathf.Clamp01(1f - input.RootSpeed / state.ReleaseRootSpeed);
            float target = progress * Mathf.Max(0f, state.DebugUprightBound - state.EarlyBase);
            float headroom = Mathf.Max(0f, input.HardLimit - state.EarlyBase);
            float previousLift = state.EarlyLift;
            SpringStep(ref state.EarlyLift, ref state.EarlyLiftVelocity, Mathf.Min(target, headroom),
                tuning.EarlyLiftOmega, dt);
            if (state.EarlyLift > headroom)
            {
                state.EarlyLift = Mathf.Max(headroom, previousLift - tuning.EarlyLiftRetreatSpeed * dt);
                state.EarlyLiftVelocity = Mathf.Min(state.EarlyLiftVelocity, 0f);
                state.DebugHardClampApplied = true;
            }

            pelvis = state.EarlyBase + state.EarlyLift;
            state.DebugCandidateTarget = state.EarlyBase + Mathf.Min(target, headroom);
            state.DebugPreHardOutput = pelvis;
            state.DebugEarlyLift = state.EarlyLift;
            state.DebugProgress = progress;
        }

        private static void BeginPrimaryRise(ref PostureState state, in PostureInputs input, float dt,
            Vector3 displayedLeftHip, Vector3 displayedRightHip, bool leftEdge, bool rightEdge, in PostureTuning tuning)
        {
            state.PrimaryFoot = leftEdge && rightEdge
                ? (input.Left.HorizontalSpeed <= input.Right.HorizontalSpeed ? PostureFoot.Left : PostureFoot.Right)
                : (leftEdge ? PostureFoot.Left : PostureFoot.Right);
            bool left = state.PrimaryFoot == PostureFoot.Left;

            Vector3 foot = left ? input.LeftTarget : input.RightTarget;
            float upper = left ? input.LeftUpperLegLength : input.RightUpperLegLength;
            float lower = left ? input.LeftLowerLegLength : input.RightLowerLegLength;
            Vector3 hip = left ? displayedLeftHip : displayedRightHip;
            Vector3 previousHip = state.HasPreviousDisplayedHips
                ? (left ? state.PreviousDisplayedLeftHip : state.PreviousDisplayedRightHip)
                : hip;

            float startLength = Vector3.Distance(hip, foot);
            // 承接：畫面上的支撐腿伸展速度（hip 相對固定腳的徑向速度）。Hermite 從它起步 ⇒ 畫面骨盆 C1 連續。
            float extensionSpeed = startLength > 1e-4f
                ? Vector3.Dot(hip - foot, (hip - previousHip) / dt) / startLength
                : 0f;
            float targetLength = Mathf.Max(0f, input.PreferredExtension) * (upper + lower);

            PostureFootInputs trailing = left ? input.Right : input.Left;
            float duration = RiseDuration(in trailing, in tuning);
            float remaining = targetLength - startLength;
            if (extensionSpeed > 1e-4f && remaining > 0f)
                duration = Mathf.Min(duration, 1.5f * remaining / extensionSpeed);

            state.PrimarySupportPosition = foot;
            state.PrimaryHipPosition = hip;
            state.PrimaryUpperLegLength = upper;
            state.PrimaryLowerLegLength = lower;
            state.PrimaryStartLegLength = startLength;
            state.PrimaryTargetLegLength = targetLength;
            state.PrimaryExtensionSpeed = extensionSpeed;
            state.PrimaryRiseDuration = Mathf.Max(duration, 2f * dt);
            state.PrimaryElapsed = 0f;
            state.PrimaryDesiredLegLength = startLength;
            state.PrimaryStartKneeAngle = KneeAngle(startLength, upper, lower);
            state.PrimaryTargetKneeAngle = KneeAngle(targetLength, upper, lower);
            state.TrailingWasUnreliable = !trailing.Reliable;
            state.TrailingSettled = false;
            state.TrailingLatched = false;
            state.TrailingCapBand = -1f;
            state.TrailingCapGap = 0f;
            state.TrailingCapGapVelocity = 0f;
            state.EarlyLift = 0f;
            state.EarlyLiftVelocity = 0f;
            state.DebugProgress = 0f;
            state.Phase = PosturePhase.StopProgress;
            state.DebugEnteredStopProgress = true;
        }

        /// <summary>
        /// ② 支撐腿長 L(t)：Hermite 從 (L₀, L̇₀) 到 (L_idle, 0)，逐帧以固定支撐腳與本帧 primary 髖 XZ 反解骨盆。
        /// ③ 尾腳若已 T3 Locked，鎖定內第一個可靠帧鎖存它的目標（每次鎖定只鎖存一次），以單邊飽和封頂。
        /// ④ 尾腳落定後只再套兩腳硬可達 C 的最小修正。
        /// </summary>
        private static void StepPrimaryRise(ref PostureState state, in PostureInputs input, float dt, ref float pelvis,
            in PostureTuning tuning)
        {
            bool left = state.PrimaryFoot == PostureFoot.Left;
            Vector3 primaryHip = left ? input.LeftHip : input.RightHip;
            float primaryLength = state.PrimaryUpperLegLength + state.PrimaryLowerLegLength;

            // 本帧輸出在下一帧顯示 ⇒ 取 t＝elapsed＋dt 的值（進入當帧就已往前一步，不會多停一帧）。
            state.PrimaryElapsed += dt;
            float s = Mathf.Clamp01(state.PrimaryElapsed / state.PrimaryRiseDuration);
            float length = HermiteToRest(state.PrimaryStartLegLength,
                state.PrimaryExtensionSpeed * state.PrimaryRiseDuration, state.PrimaryTargetLegLength, s);
            state.PrimaryDesiredLegLength = length;
            float candidate = Mathf.Min(
                SupportLegPelvisOffset(primaryHip, state.PrimarySupportPosition, length),
                LegBound(primaryHip, state.PrimarySupportPosition, primaryLength));
            state.DebugCandidateTarget = candidate;
            float output = candidate;

            // ③ 前瞻可達上限：只看 T3 Locked 的尾腳；擺盪中（Releasing／Unlocked）不預測。
            PostureFootInputs trailing = left ? input.Right : input.Left;
            if (trailing.Phase != FootLockPhase.Locked)
            {
                state.TrailingLatched = false;
                state.TrailingCapBand = -1f;
            }
            else if (!state.TrailingLatched && trailing.Reliable)
            {
                state.TrailingLatched = true;
                state.TrailingLatchPosition = left ? input.RightTarget : input.LeftTarget;
            }

            if (state.TrailingLatched)
            {
                Vector3 trailingHip = left ? input.RightHip : input.LeftHip;
                float trailingLength = left
                    ? input.RightUpperLegLength + input.RightLowerLegLength
                    : input.LeftUpperLegLength + input.LeftLowerLegLength;
                float cap = LegBound(trailingHip, state.TrailingLatchPosition, trailingLength);
                if (state.TrailingCapBand < 0f)
                    state.TrailingCapBand = Mathf.Clamp(cap - output, 0f, tuning.TrailingCapBand);
                float capped = SaturateBelow(output, cap, state.TrailingCapBand);
                state.DebugTrailingCap = cap;
                state.DebugTrailingCapApplied = capped < output - 1e-6f;
                state.TrailingCapGap = output - capped;
                state.TrailingCapGapVelocity = 0f;
                output = capped;
            }
            else if (state.TrailingCapGap > 0f)
            {
                // 上限在上升途中消失（尾腳離開 Locked）：已被壓住的差距以臨界阻尼彈簧從靜止收掉——
                // 速度從 0 起步（C1），不瞬間往上跳。
                SpringStep(ref state.TrailingCapGap, ref state.TrailingCapGapVelocity, 0f,
                    tuning.TrailingCapReleaseOmega, dt);
                if (state.TrailingCapGap < 1e-5f)
                {
                    state.TrailingCapGap = 0f;
                    state.TrailingCapGapVelocity = 0f;
                }
                output -= state.TrailingCapGap;
            }

            state.DebugPreHardOutput = output;
            if (state.TrailingSettled && output > input.HardLimit)
            {
                // ④ 雙腳都落定：只做剩餘的兩腳最小修正。
                output = input.HardLimit;
                state.DebugHardClampApplied = true;
            }

            pelvis = output;
            state.DebugProgress = s;
        }
    }
}
