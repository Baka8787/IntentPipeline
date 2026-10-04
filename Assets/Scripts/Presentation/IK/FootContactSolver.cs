using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>T3 鎖腳狀態（docs/45 §7 ②）：未鎖 → 鎖定 → 釋放中 → 未鎖。</summary>
    public enum FootLockPhase
    {
        Unlocked = 0,
        Locked = 1,
        Releasing = 2,
    }

    /// <summary>
    /// 🧪（T3 原型，docs/45 §6–§7）每隻腳**刻意跨帧保存**的演算法狀態——與骨盆平滑、`FootPlantTracker` 同類，
    /// 不是帧內資料。只存數值：⛔ 不存 <c>RaycastHit</c>／<c>Collider</c>（幾何每帧重新查詢）。
    /// 任何失效條件（IK 不允許、pose 無效、元件停用、瞬移）一律 <see cref="Reset"/>，不做部分復原。
    /// </summary>
    public struct FootContactState
    {
        public FootLockPhase Phase;

        /// <summary>鎖定當帧的原始腳踝水平錨點（世界 XZ；R1c 不寫它；y 不使用——高度每帧由接觸解重算）。</summary>
        public Vector3 LockPosition;

        /// <summary>釋放進度：1 ＝ 仍在鎖定點，0 ＝ 已回到動畫腳位。只在 <see cref="FootLockPhase.Releasing"/> 有意義。</summary>
        public float ReleaseBlend;

        /// <summary>
        /// 釋放開始那一帧的「effective Locked 位置 − 動畫腳位」（水平；現行即鎖定點，R1c 最長 d0）。
        /// 釋放期間目標 ＝ 當下動畫腳位 ＋ smoothstep(ReleaseBlend) × 這個偏移：
        /// 腳立刻跟上動畫的移動，只有「開始時的差距」逐漸消失——不再朝一個留在原地的鎖定點插值
        /// （舊寫法下動畫腳一離開，目標就越拖越遠：Run 釋放中落後髖部 p90 0.62 m，docs/45 §9）。
        /// </summary>
        public Vector3 ReleaseOffset;

        /// <summary>上一帧的 pose 腳踝（世界）與其相對 pose 平面的高度——一帧外插與腳速的唯一來源。</summary>
        public bool HasHistory;
        public Vector3 PrevPosePosition;
        public float PrevPoseHeight;

        /// <summary>上一帧在目標位置是否取得接觸（鎖定／維持鎖定的前提）。</summary>
        public bool PrevContactValid;

        /// <summary>鎖定期間最後一帧的目標高度。釋放時高度從它平滑過渡到即時解（Codex T3 審查 Medium 2）。</summary>
        public float LockedHeight;

        /// <summary>
        /// 鎖定當帧選定的支撐高度（決定最小抬升的那一點下方的地面）。鎖定期間它是腳底的地面下限：
        /// 腳在鎖定中滾動（腳跟抬起、朝向改變）時，端點可能跨過階緣——若每帧重新選支撐，抬升會跳一個級高（實測 18 cm）。
        /// 支撐和水平位置一起在鎖定時決定，釋放時才重新交給即時查詢（docs/45 §7 Medium ①）。
        /// </summary>
        public float LockedSupportY;

        /// <summary>
        /// 選定支撐的是哪一個端點（0 腳跟／1 腳踝底／2 腳尖）。鎖定支撐**只套用在這一點**：
        /// 套到所有端點會讓鎖定後一帧的約束集合改變（例如較低的腳跟也被墊到上一階）而再跳一次（Codex High）。
        /// </summary>
        public int LockedSupportPoint;

        // 觀測用（探針讀取；每帧覆寫，任何決策都不讀）。
        public float DebugBeta;
        public float DebugLift;

        public void Reset() => this = default;
    }

    /// <summary>
    /// 🧪（T3 原型）腳部接觸的純函數：一帧外插、落腳相位 β、帶朝向的最小抬升、鎖腳狀態轉移。
    /// 不發物理查詢、不讀 Unity 時間——由 <c>FootIKController</c> 查好幾何後呼叫（EditMode 可直接測）。
    ///
    /// 為什麼需要外插：IK 目標在 LateUpdate 由第 N 帧 pose 算出、第 N+1 帧才套用。
    /// 舊模型擺盪腳權重為 0 所以看不出來；T3 擺盪時位置權重為 1，不外插會讓腳落後動畫一帧
    /// （實測擺盪腳每帧移動 Walk 7.5–10 cm、Run 11–16 cm；一帧外插後殘差中位數約 1 cm）。
    /// </summary>
    public static class FootContactSolver
    {
        /// <summary>
        /// 由 pose 歷史外插下一帧（目標實際套用那一帧）的腳踝水平位置與動畫離地高度，並回傳動畫腳的水平速度。
        /// 沒有歷史或不連續（一帧位移超過 <paramref name="maxStep"/>，例如瞬移）⇒ 回傳 false、不外插、速度 0，
        /// 呼叫端應視為重新開始（狀態重設）。無論結果如何都會把本帧寫進歷史。
        /// </summary>
        public static bool PredictPose(ref FootContactState state, Vector3 posePosition, float poseHeight,
            float deltaTime, float maxStep, out Vector3 predictedPosition, out float predictedHeight, out float horizontalSpeed)
        {
            bool continuous = state.HasHistory && deltaTime > 0f
                && (posePosition - state.PrevPosePosition).sqrMagnitude <= maxStep * maxStep;

            if (continuous)
            {
                Vector3 step = posePosition - state.PrevPosePosition;
                predictedPosition = new Vector3(posePosition.x + step.x, posePosition.y, posePosition.z + step.z);
                predictedHeight = poseHeight + (poseHeight - state.PrevPoseHeight);
                horizontalSpeed = new Vector2(step.x, step.z).magnitude / deltaTime;
            }
            else
            {
                predictedPosition = posePosition;
                predictedHeight = poseHeight;
                horizontalSpeed = 0f;
            }

            state.HasHistory = true;
            state.PrevPosePosition = posePosition;
            state.PrevPoseHeight = poseHeight;
            return continuous;
        }

        /// <summary>
        /// 落腳相位 β ∈ [0,1] ＝ 高度貢獻（既有支撐量）× 速度貢獻（1 − smoothstep(speedMin, speedMax, 腳水平速度)）。
        /// 連續；決定「腳相對移動面還是接觸面」，**不是** IK 權重（docs/05 §3.5.4.4 T1）。
        /// </summary>
        public static float ComputeContactBeta(float stance, float horizontalSpeed, float speedMin, float speedMax)
        {
            float t = speedMax > speedMin
                ? Mathf.Clamp01((horizontalSpeed - speedMin) / (speedMax - speedMin))
                : (horizontalSpeed <= speedMin ? 0f : 1f);
            float phase = 1f - t * t * (3f - 2f * t);
            return Mathf.Clamp01(stance) * phase;
        }

        /// <summary>
        /// 一個腳底端點相對它下方地面的有號穿透量（正 ＝ 在地面下）。
        /// 端點 ＝ 腳踝 ＋ 旋轉 × (0, −腳底高, 前後偏移)——**保留腳的朝向**，不把腳壓平（Codex High 1）。
        /// </summary>
        public static Vector3 SolePoint(Vector3 ankle, Quaternion rotation, float footBottomHeight, float forwardOffset)
            => ankle + rotation * new Vector3(0f, -footBottomHeight, forwardOffset);

        /// <summary>
        /// 最小抬升（Codex High 1 的有號版本）。<paramref name="contactDelta"/> ＝ max_i(地面_i − 端點_i.y)：
        /// 正 ＝ 目前朝向下至少一點穿透，負 ＝ 整隻腳懸空這麼高。
        /// - 穿透（≥0）⇒ 一律完整抬升：任何相位都不允許腳底穿進地面。
        /// - 懸空（&lt;0）⇒ 只下拉 β 比例：擺盪（β≈0）保留動畫離地高度，落腳（β→1）才貼上接觸面。
        /// 兩支在 0 處相接 ⇒ 對 <paramref name="contactDelta"/> 連續。
        /// </summary>
        public static float ComputeMinimumLift(float contactDelta, float beta)
            => contactDelta >= 0f ? contactDelta : Mathf.Clamp01(beta) * contactDelta;

        /// <summary>
        /// 目前狀態下的目標水平位置：未鎖 ⇒ 動畫（外插後）；鎖定 ⇒ 鎖定點，或在其周圍有限度跟隨動畫；
        /// 釋放中 ⇒ 動畫 ＋ smoothstep(ReleaseBlend) × 釋放開始時凍結的偏移（見 <see cref="FootContactState.ReleaseOffset"/>）。
        /// </summary>
        public static Vector3 ResolveHorizontal(in FootContactState state, Vector3 animationPosition, float followRadius = 0f)
        {
            switch (state.Phase)
            {
                case FootLockPhase.Locked:
                    // 關閉時直接走原本的建構路徑；不可用 anim + (lock - anim) 假裝等價，否則會多一次浮點運算。
                    if (followRadius <= 0f)
                        return new Vector3(state.LockPosition.x, animationPosition.y, state.LockPosition.z);
                    Vector2 offset = ClampLockOffset(
                        new Vector2(state.LockPosition.x - animationPosition.x, state.LockPosition.z - animationPosition.z), followRadius);
                    return new Vector3(animationPosition.x + offset.x, animationPosition.y, animationPosition.z + offset.y);
                case FootLockPhase.Releasing:
                    float s = ReleaseWeight(in state);
                    return new Vector3(animationPosition.x + s * state.ReleaseOffset.x, animationPosition.y,
                        animationPosition.z + s * state.ReleaseOffset.z);
                default:
                    return animationPosition;
            }
        }

        /// <summary>
        /// R1c：把「錨點 − 動畫腳」限制在 <paramref name="followRadius"/> 內。
        /// 半徑 ≤ 0 直接回傳原值，供關閉路徑保留現行數值語意。
        /// </summary>
        public static Vector2 ClampLockOffset(Vector2 offset, float followRadius)
        {
            if (followRadius <= 0f) return offset;
            float sqrMagnitude = offset.sqrMagnitude;
            float radiusSquared = followRadius * followRadius;
            return sqrMagnitude > radiusSquared
                ? offset * (followRadius / Mathf.Sqrt(sqrMagnitude))
                : offset;
        }

        /// <summary>
        /// 一個腳底端點的有效地面高度。鎖定中（且支撐已選定）的**選定端點**：max(即時地面, 鎖定支撐)，即時沒命中時就用鎖定支撐——
        /// 它滑出原本那一階不會讓腳掉下去；滑進更高的面仍會抬起（不得穿透）。其他端點與其他狀態：即時地面。
        /// 回傳 false ＝ 這個端點沒有地面可用。⚠️ 回傳 true 不代表「本帧真的有接觸」——接觸是否有效只能看即時命中（Codex Medium）。
        /// </summary>
        public static bool ContactGround(in FootContactState state, bool supportSelected, int point, bool hasHit, float hitY, out float groundY)
        {
            if (state.Phase == FootLockPhase.Locked && supportSelected && point == state.LockedSupportPoint)
            {
                groundY = hasHit ? Mathf.Max(hitY, state.LockedSupportY) : state.LockedSupportY;
                return true;
            }
            groundY = hitY;
            return hasHit;
        }

        /// <summary>釋放中「仍留在鎖定端」的比例 ＝ smoothstep(ReleaseBlend)；非釋放狀態回 0。</summary>
        public static float ReleaseWeight(in FootContactState state)
        {
            if (state.Phase != FootLockPhase.Releasing) return 0f;
            float r = Mathf.Clamp01(state.ReleaseBlend);
            return r * r * (3f - 2f * r);
        }

        /// <summary>
        /// 目標高度。釋放中：從鎖定期最後的高度以與水平相同的 smoothstep 過渡到即時解——釋放點的查詢可能跨過階緣，
        /// 即時解會跳一個級高（Codex Medium 2）；但仍以接觸底線 <paramref name="contactFloorY"/>（腳底剛好不穿透的高度）
        /// 為下限：過渡可以晚一點放下腳，不能讓腳穿進地面。其他狀態：即時解。
        /// </summary>
        public static float ResolveHeight(in FootContactState state, float liveY, bool hasContact, float contactFloorY)
        {
            if (state.Phase != FootLockPhase.Releasing) return liveY;
            float y = Mathf.Lerp(liveY, state.LockedHeight, ReleaseWeight(in state));
            return hasContact ? Mathf.Max(contactFloorY, y) : y;
        }

        /// <summary>
        /// 鎖腳狀態轉移（每帧一次，在決定本帧目標之前）。滯後：鎖定需要 β ≥ lockBeta；放開在 β &lt; unlockBeta、
        /// 動畫腳離鎖定點超過 unlockRadius（水平）、或上一帧失去接觸時。釋放以 releaseTime 秒平滑回到動畫腳位，
        /// 完成後才可能再次鎖定（避免在門檻附近來回抖動）。
        /// </summary>
        public static void StepLock(ref FootContactState state, float beta, Vector3 animationPosition, float deltaTime,
            float lockBeta, float unlockBeta, float unlockRadius, float releaseTime, float followRadius = 0f)
        {
            switch (state.Phase)
            {
                case FootLockPhase.Unlocked:
                    if (beta >= lockBeta && state.PrevContactValid)
                    {
                        state.Phase = FootLockPhase.Locked;
                        state.LockPosition = animationPosition;
                    }
                    break;

                case FootLockPhase.Locked:
                    float dx = animationPosition.x - state.LockPosition.x;
                    float dz = animationPosition.z - state.LockPosition.z;
                    if (beta < unlockBeta || dx * dx + dz * dz > unlockRadius * unlockRadius || !state.PrevContactValid)
                    {
                        state.Phase = FootLockPhase.Releasing;
                        state.ReleaseBlend = 1f;
                        // 與傳進來的動畫腳位同一個參數凍結 ⇒ 轉移當帧目標與 Locked 的 effective 位置相同（不跳）。
                        // 關閉時直接保留原路徑，避免 clamp 引入多餘浮點運算。
                        if (followRadius <= 0f)
                        {
                            state.ReleaseOffset = new Vector3(-dx, 0f, -dz);
                        }
                        else
                        {
                            Vector2 offset = ClampLockOffset(new Vector2(-dx, -dz), followRadius);
                            state.ReleaseOffset = new Vector3(offset.x, 0f, offset.y);
                        }
                    }
                    break;

                case FootLockPhase.Releasing:
                    state.ReleaseBlend = releaseTime > 0f ? state.ReleaseBlend - deltaTime / releaseTime : 0f;
                    if (state.ReleaseBlend <= 0f)
                    {
                        state.ReleaseBlend = 0f;
                        state.Phase = FootLockPhase.Unlocked;
                    }
                    break;
            }
        }
    }
}
