using Project.Core.Blackboard;
using UnityEngine;

namespace Project.Presentation.Look
{
    /// <summary>
    /// 戰鬥語境的頭部附加旋轉。由 PresentationPipeline 在順序 6.5 驅動，
    /// 只讀 CombatContext，並在動畫已評估的骨骼姿勢上疊加世界空間 yaw／pitch。
    ///
    /// **連續性由三件事共同保證**（2026-09-08，皆為使用者 Play 實證的缺陷修正）：
    /// ① 超出 <c>maxYaw</c> 一律**視為沒有目標**而非 clamp——clamp 會在目標繞到正後方時
    ///    把 <c>SignedAngle</c> 的 ±180° wrap 放大成 ±maxYaw 的單幀翻轉；
    /// ② 取得／放棄目標使用不同 yaw 門檻，避免目標在邊界附近令權重逐幀翻動；
    /// ③ 追隨與回正都由 <c>lookDegreesPerSecond</c> 限速，權重只在角度歸零後淡出，
    ///    不再以短促的權重淡出冒充回正。
    /// ⛔ 想把 ① 改回 clamp 之前，先讀 `CombatContextTests.TC6C`——它守的正是這條。
    /// </summary>
    public sealed class HeadLookController : MonoBehaviour, IPresentationController
    {
        private const float DirectionSqrEpsilon = 0.000001f;
        private const float SettledAngleEpsilon = 0.01f;

        [SerializeField] private Transform headBone;
        [SerializeField] private float maxYaw = 70f;
        [SerializeField] private float maxPitch = 25f;
        [SerializeField] private float blendSpeed = 8f;
        [SerializeField] private float releaseYawHysteresis = 10f;

        [Tooltip("頭部角度本身的追隨速率（度／秒）。blendSpeed 只管淡入淡出的權重，\n" +
                 "不管角度變化的速率——沒有這一項時，任何目標變化都是瞬間套用。\n" +
                 "360 ＝ 掃完整個 ±70° 可視角約 0.2 秒；0 ＝ 角度凍結（不建議）。")]
        [SerializeField] private float lookDegreesPerSecond = 360f;

        private float _weight;
        private float _yaw;
        private float _pitch;
        private bool _hasActiveLook;

        public void Tick(PlayerRuntimeData data)
        {
            if (headBone == null) return;

            float targetYaw = 0f;
            float targetPitch = 0f;
            float effectiveMaxYaw = Mathf.Abs(maxYaw) +
                                    (_hasActiveLook ? Mathf.Max(0f, releaseYawHysteresis) : 0f);
            bool hasTarget = data != null &&
                             data.CombatContext.InCombat &&
                             data.CombatContext.HasTarget &&
                             TryComputeLookAngles(
                                 headBone.forward,
                                 data.CombatContext.TargetPosition - headBone.position,
                                 Vector3.up,
                                 effectiveMaxYaw,
                                 maxPitch,
                                 out targetYaw,
                                 out targetPitch);

            // 遲滯只存在於呼叫端的「目前是否已取得目標」狀態；純函數仍只回答指定上限內能否解角。
            // 一旦超過 release 門檻，本幀立刻結算為未取得，下一次必須回到 acquire 門檻內才能重取。
            _hasActiveLook = hasTarget;

            // 沒有目標時 targetYaw／targetPitch 保持 0，與追隨共用同一條角速度上限。
            // 回正因此是可見角度的連續變化，不會再由 0.125 秒的權重淡出把 70° 瞬間乘回 0。
            float maxStep = Mathf.Max(0f, lookDegreesPerSecond) * Time.deltaTime;
            _yaw = Mathf.MoveTowards(_yaw, targetYaw, maxStep);
            _pitch = Mathf.MoveTowards(_pitch, targetPitch, maxStep);

            bool anglesSettled = Mathf.Abs(_yaw) <= SettledAngleEpsilon &&
                                 Mathf.Abs(_pitch) <= SettledAngleEpsilon;
            float targetWeight = hasTarget || !anglesSettled ? 1f : 0f;
            _weight = Mathf.MoveTowards(
                _weight, targetWeight, Mathf.Max(0f, blendSpeed) * Time.deltaTime);
            if (_weight <= 0f) return;

            Vector3 currentHorizontal = Vector3.ProjectOnPlane(headBone.forward, Vector3.up);
            if (currentHorizontal.sqrMagnitude <= DirectionSqrEpsilon) return;
            currentHorizontal.Normalize();

            Quaternion yawOffset = Quaternion.AngleAxis(_yaw * _weight, Vector3.up);
            Vector3 yawedHorizontal = yawOffset * currentHorizontal;
            Vector3 pitchAxis = Vector3.Cross(Vector3.up, yawedHorizontal);
            if (pitchAxis.sqrMagnitude <= DirectionSqrEpsilon) return;
            pitchAxis.Normalize();

            Quaternion pitchOffset = Quaternion.AngleAxis(_pitch * _weight, pitchAxis);
            headBone.rotation = pitchOffset * yawOffset * headBone.rotation;
        }

        /// <summary>
        /// 計算「目前頭部朝向 → 目標」的世界空間附加角度。pitch 的符號可直接交給
        /// 以角色右軸為旋轉軸的 AngleAxis；只依賴值型別輸入，執行期零配置。
        /// </summary>
        internal static bool TryComputeLookAngles(
            Vector3 currentForward,
            Vector3 targetDirection,
            Vector3 worldUp,
            float maxYaw,
            float maxPitch,
            out float yaw,
            out float pitch)
        {
            yaw = 0f;
            pitch = 0f;

            if (currentForward.sqrMagnitude <= DirectionSqrEpsilon ||
                targetDirection.sqrMagnitude <= DirectionSqrEpsilon ||
                worldUp.sqrMagnitude <= DirectionSqrEpsilon)
            {
                return false;
            }

            currentForward.Normalize();
            targetDirection.Normalize();
            worldUp.Normalize();

            Vector3 currentHorizontal = Vector3.ProjectOnPlane(currentForward, worldUp);
            Vector3 targetHorizontal = Vector3.ProjectOnPlane(targetDirection, worldUp);
            if (currentHorizontal.sqrMagnitude <= DirectionSqrEpsilon ||
                targetHorizontal.sqrMagnitude <= DirectionSqrEpsilon)
            {
                return false;
            }
            currentHorizontal.Normalize();
            targetHorizontal.Normalize();

            // 🔴 刻意**不 clamp**：超出可視角就當作「沒有目標」。
            // Vector3.SignedAngle 的值域是 (-180, 180]——目標繞到正後方時 +175° 會在下一幀變成 -175°，
            // clamp 之後就成了 +maxYaw → -maxYaw 的**單幀翻轉**（使用者 Play 實證：頭會一瞬間轉到另一邊）。
            // 回傳 false ⇒ 上層以角速度限制回正，角度歸零後才淡出權重 ⇒ **不連續從構造上不存在**，
            // 而不是靠偵測 wrap 或快速淡出權重去掩蓋一個仍然存在的斷點。
            yaw = Vector3.SignedAngle(currentHorizontal, targetHorizontal, worldUp);
            if (Mathf.Abs(yaw) > Mathf.Abs(maxYaw))
            {
                yaw = 0f;
                return false;
            }

            float currentElevation = Mathf.Asin(
                Mathf.Clamp(Vector3.Dot(currentForward, worldUp), -1f, 1f)) * Mathf.Rad2Deg;
            float targetElevation = Mathf.Asin(
                Mathf.Clamp(Vector3.Dot(targetDirection, worldUp), -1f, 1f)) * Mathf.Rad2Deg;

            // 角色右軸的正向 AngleAxis 會把 forward 壓向下方，因此 elevation 差需反號。
            pitch = Mathf.Clamp(
                currentElevation - targetElevation,
                -Mathf.Abs(maxPitch),
                Mathf.Abs(maxPitch));
            return true;
        }
    }
}
