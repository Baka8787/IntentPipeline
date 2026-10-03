using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>目的地踏面的來源（docs/45 §11.2、§16）。</summary>
    public enum LandingSurfaceSource
    {
        None = 0,
        /// <summary>ADR-010 D5 next planned plant（停步 clip 的烘焙排程）。</summary>
        Prediction = 1,
        /// <summary>即時驗證：腳下三點（T3 同一條規則）在落腳前的最後幾帧。</summary>
        Live = 2,
    }

    /// <summary>可信度：最後 plant／即時驗證＝High；煞車／中途 plant＝Low（必須由即時驗證收斂，docs/45 §16.1）。</summary>
    public enum LandingSurfaceConfidence
    {
        None = 0,
        Low = 1,
        High = 2,
    }

    /// <summary>
    /// 🧪（docs/45 §17）一隻擺盪腳的目的地踏面。<see cref="Weight"/> ＝ 0 ⇒ T3 現行行為逐位元不變。
    /// </summary>
    public struct LandingSurfaceIntent
    {
        public bool Valid;
        /// <summary>已承諾的目的地踏面高度（世界 y）。</summary>
        public float SurfaceY;
        public LandingSurfaceConfidence Confidence;
        public LandingSurfaceSource Source;
        public int Revisions;
        /// <summary>擺盪目標朝目的地過渡的強度 ∈ [0,1]（已含可信度上限與即時矛盾時的退出）。</summary>
        public float Weight;
        /// <summary>過渡實際使用的踏面高度。改答案時**不追**：先把 <see cref="Weight"/> 退到 0，再換成新答案、重新進入（fade through zero）。</summary>
        public float BlendSurfaceY;
    }

    /// <summary>每隻腳**刻意跨帧保存**的 resolver 狀態（與 <see cref="FootContactState"/> 同類）。只存數值。</summary>
    public struct LandingSurfaceState
    {
        public LandingSurfaceIntent Intent;
        public bool HasPending;
        public float PendingY;
        public int PendingCount;
        /// <summary>上一帧的 next plant 世界 XZ——只用來判斷預測是否已經在世界中定住（不是答案的快取）。</summary>
        public bool HasPrevPlant;
        public Vector2 PrevPlantXZ;
        /// <summary>本次擺盪 β 曾低於 VerifyBeta（剛釋放時 β 仍高；驗證窗只在之後的上升穿越才開）。</summary>
        public bool SawLowBeta;
        /// <summary>本次擺盪已進入落腳前的即時驗證窗。鎖存到離開 Unlocked——β 在門檻附近來回時驗證不閃爍。</summary>
        public bool Verifying;
        /// <summary>即時三點連續與承諾不一致的帧數（達 ConfirmFrames 才算矛盾——單帧不觸發）。</summary>
        public int ContradictionCount;

        public void Reset() => this = default;
    }

    /// <summary>本帧的候選（全部是本帧事實；⛔ 不跨帧快取 ADR-010 快照——快照失效時這裡就是 false）。</summary>
    public struct LandingSurfaceInputs
    {
        public FootLockPhase Phase;
        public float Beta;
        /// <summary>ADR-010 next plant 就是這隻腳，且其落點下方有地面。</summary>
        public bool HasPlant;
        public float PlantSurfaceY;
        public bool PlantIsFinal;
        /// <summary>next plant 的世界 XZ。</summary>
        public Vector2 PlantXZ;
        /// <summary>T3 本帧在目標位置以三點最大穿透規則取到的踏面（擺盪中＝即時射線命中）。</summary>
        public bool HasLive;
        public float LiveSurfaceY;
    }

    public struct LandingSurfaceTuning
    {
        public float VerifyBeta;
        public int ConfirmFrames;
        public int ReviseFrames;
        public float SurfaceTolerance;
        public float LowConfidenceWeight;
        public float WeightRate;
        /// <summary>即時矛盾時過渡退出的速度（1/秒）；比 <see cref="WeightRate"/> 快——錯的目的地要在鎖定前退完。</summary>
        public float ContradictionRate;
        public float PlantStillDistance;
    }

    /// <summary>
    /// 🧪（docs/45 §17，T3 landing-surface slice Phase 2）destination landing surface 的純邏輯：
    /// 候選（已在世界中定住的 next plant；落腳前改以即時三點驗證）→ 承諾規則（§11.2：首次承諾連續 <c>ConfirmFrames</c> 帧一致、
    /// 改答案連續 <c>ReviseFrames</c> 帧一致，單帧不改答案）→ 過渡強度（可信度上限；即時矛盾／改答案時先退到 0）。
    /// 只在 Unlocked 運作；Locked／Releasing ⇒ 重設（鎖定由 T3 自己的支撐決定，authority 不變）。
    /// 零 GC、無 Unity 物理查詢——候選由呼叫端（FootIKController）本帧取得。
    /// </summary>
    public static class LandingSurfaceResolver
    {
        public static void Step(ref LandingSurfaceState state, in LandingSurfaceInputs input, float deltaTime, in LandingSurfaceTuning tuning)
        {
            if (input.Phase != FootLockPhase.Unlocked)
            {
                state.Reset();
                return;
            }

            ref LandingSurfaceIntent intent = ref state.Intent;

            // ⓪ 預測是否已在世界中定住：停步 pending／等待播放頭期間，落點以「目前 root ＋ clip 0 位移」發布（ADR-010 D1-R1），
            //    會跟著角色移動（Walk 實測每帧 3.8 cm）——那時它還不是一個落點。
            bool anchored = input.HasPlant && state.HasPrevPlant
                && (input.PlantXZ - state.PrevPlantXZ).sqrMagnitude <= tuning.PlantStillDistance * tuning.PlantStillDistance;
            state.HasPrevPlant = input.HasPlant;
            state.PrevPlantXZ = input.PlantXZ;

            // ① 本帧候選：定住的 next plant；落腳前（β 上升穿越 VerifyBeta 之後）改以即時三點**驗證**它。
            //    ⛔ 即時取樣只能驗證本帧存在的預測，不能自己產生意圖——否則一般行走每一步落腳前都會有 intent（no-intent 必須＝現行）。
            //    ⛔ 剛釋放時 β 仍高：不得因此開窗（否則整個擺盪都在追腳下的踏面）。
            if (input.Beta < tuning.VerifyBeta) state.SawLowBeta = true;
            else if (state.SawLowBeta) state.Verifying = true;
            bool verifying = anchored && input.HasLive && state.Verifying;
            float candidate = verifying ? input.LiveSurfaceY : input.PlantSurfaceY;
            LandingSurfaceSource source = verifying ? LandingSurfaceSource.Live : LandingSurfaceSource.Prediction;

            if (!anchored)
            {
                state.HasPending = false;
                state.PendingCount = 0;
                // 預測消失或尚未定住（停步中斷、next plant 換腳、pending 中）⇒ 意圖失效，過渡以 WeightRate 退出（不沿用舊答案）。
                intent.Valid = false;
            }
            else
            {
                // ② 承諾規則
                if (state.HasPending && Mathf.Abs(candidate - state.PendingY) < tuning.SurfaceTolerance)
                {
                    state.PendingCount++;
                }
                else
                {
                    state.HasPending = true;
                    state.PendingY = candidate;
                    state.PendingCount = 1;
                }

                bool differs = !intent.Valid || Mathf.Abs(state.PendingY - intent.SurfaceY) >= tuning.SurfaceTolerance;
                int need = intent.Valid ? tuning.ReviseFrames : tuning.ConfirmFrames;
                if (differs && state.PendingCount >= need)
                {
                    if (intent.Valid) intent.Revisions++;
                    intent.Valid = true;
                    intent.SurfaceY = state.PendingY;
                    if (intent.Weight <= 0f) intent.BlendSurfaceY = intent.SurfaceY; // 從 0 進入：直接用新答案
                }
                // 來源／可信度跟著「目前與承諾一致」的候選更新（同一階由即時驗證確認 ⇒ High）
                if (intent.Valid && Mathf.Abs(candidate - intent.SurfaceY) < tuning.SurfaceTolerance)
                {
                    intent.Source = source;
                    intent.Confidence = verifying || input.PlantIsFinal ? LandingSurfaceConfidence.High : LandingSurfaceConfidence.Low;
                }
            }

            // ③ 過渡強度：可信度上限；即時驗證與承諾矛盾、或答案已改但過渡仍用舊踏面 ⇒ 先退到 0（單帧變化受 WeightRate 限制）。
            float targetWeight = 0f;
            bool contradicted = false;
            if (intent.Valid)
            {
                state.ContradictionCount = verifying && Mathf.Abs(input.LiveSurfaceY - intent.SurfaceY) >= tuning.SurfaceTolerance
                    ? state.ContradictionCount + 1 : 0;
                contradicted = state.ContradictionCount >= tuning.ConfirmFrames;
                bool switching = intent.Weight > 0f && Mathf.Abs(intent.BlendSurfaceY - intent.SurfaceY) >= tuning.SurfaceTolerance;
                if (!contradicted && !switching)
                    targetWeight = intent.Confidence == LandingSurfaceConfidence.High ? 1f : Mathf.Clamp01(tuning.LowConfidenceWeight);
            }
            float rate = contradicted ? Mathf.Max(tuning.WeightRate, tuning.ContradictionRate) : tuning.WeightRate;
            intent.Weight = Mathf.MoveTowards(intent.Weight, targetWeight, rate * deltaTime);
            if (intent.Weight <= 0f)
            {
                if (intent.Valid)
                {
                    intent.BlendSurfaceY = intent.SurfaceY; // 退到 0 之後才換踏面 ⇒ 換的那一帧沒有任何效果
                }
                else
                {
                    int revisions = intent.Revisions;
                    intent = default;
                    intent.Revisions = revisions;
                }
            }
        }

        /// <summary>
        /// 擺盪參考的抬高（純函數）：接近落點時，參考面從移動斜坡升到目的地踏面，**保留動畫離地高度**。
        /// 只往上（目的地高於斜坡＝上樓）：下樓時腳還在上一階上方，物理上不可能低於它，參考維持斜坡。
        /// <paramref name="proximity"/> ∈ [0,1]：目標水平位置離預測落點的接近度（遠處不抬，避免擺盪初期整隻腳被拉高一兩階）。
        /// </summary>
        public static float ReferenceRaise(float rampY, float destinationY, float weight, float proximity)
            => Mathf.Clamp01(weight) * Mathf.Clamp01(proximity) * Mathf.Max(0f, destinationY - rampY);

        /// <summary>接近度 ＝ 1 − smoothstep(0, radius, 水平距離)；radius ≤ 0 ⇒ 1（不限距離）。</summary>
        public static float Proximity(float horizontalDistance, float radius)
        {
            if (radius <= 0f) return 1f;
            float t = Mathf.Clamp01(horizontalDistance / radius);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 擺盪目標的抬升（純函數）。<paramref name="liveLift"/> ＝ T3 現行 <see cref="FootContactSolver.ComputeMinimumLift"/>；
        /// 目的地抬升 ＝ β ×（目的地踏面 − 最低腳底端點）——β→1 時最低點正好落在目的地踏面上（與 T3「決定抬升的那一點貼地」同形）。
        /// 兩者依 <paramref name="weight"/> 混合後，仍以即時接觸為下限：**任何時候都不穿透實際地面**。
        /// <paramref name="weight"/> ＝ 0 ⇒ 回傳 <paramref name="liveLift"/>（呼叫端在 0 時不呼叫，保證逐位元不變）。
        /// </summary>
        public static float BlendLift(float liveLift, bool hasContact, float contactDelta, float destinationDelta, float beta, float weight)
        {
            float w = Mathf.Clamp01(weight);
            float blended = (1f - w) * liveLift + w * Mathf.Clamp01(beta) * destinationDelta;
            return hasContact ? Mathf.Max(blended, contactDelta) : blended;
        }
    }
}
