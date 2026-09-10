using Project.Core.Blackboard;
using Project.Core.StateMachine;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.Facing
{
    /// <summary>
    /// 每個角色唯一的 facing request 送出者。優先序與「本幀是否需要轉」都在此一次解完，
    /// MotionDriver 只消費方向並維持 rotation 單一寫入者。
    /// explicit target 的第二順位刻意留白，待 lock-on 切片加入來源後再實作。
    /// </summary>
    public sealed class CharacterFacingSource : MonoBehaviour
    {
        private const float DirectionSqrEpsilon = 0.000001f;

        [SerializeField] private float facingAngleDeadzone = 8f;

        private MotionDriver _motionDriver;
        private FullBodyStateMachine _stateMachine;

        public void Initialize(MotionDriver motionDriver, FullBodyStateMachine stateMachine)
        {
            _motionDriver = motionDriver;
            _stateMachine = stateMachine;
        }

        /// <summary>管線順序 4.6：FSM 已確定本幀 Action 承諾，LateUpdate 尚未消費 request。</summary>
        public void Tick(PlayerRuntimeData data)
        {
            if (_motionDriver == null || data == null) return;

            Vector3 commitment = default;
            bool hasCommitment = _stateMachine != null &&
                                 _stateMachine.TryGetActiveFacingCommitment(out commitment);
            if (!TryResolveFacing(
                    hasCommitment,
                    commitment,
                    data.MoveDirection,
                    out Vector3 direction,
                    out bool fromActionCommitment))
            {
                return;
            }

            // 死區只保護站定出手的 Action 姿勢，避免半蹲腳掌被連續微轉拖著扭。
            // MovementDirection 是 continuous dynamics：即使角差很小也必須逐幀送出，否則 NavMesh
            // 的小幅航向修正會累積成「停住 → 飄出死區 → 猛轉」的極限環。
            if (!ShouldRequestFacing(
                    transform.forward,
                    direction,
                    fromActionCommitment,
                    facingAngleDeadzone))
            {
                return;
            }

            _motionDriver.RequestFacing(direction);
        }

        internal static bool TryResolveFacing(
            bool hasActionCommitment,
            Vector3 actionCommitment,
            Vector3 moveDirection,
            out Vector3 worldDirection,
            out bool fromActionCommitment)
        {
            if (hasActionCommitment && TryNormalizeHorizontal(actionCommitment, out worldDirection))
            {
                fromActionCommitment = true;
                return true;
            }

            // Priority 2（explicit target／lock-on）預留；S3a 沒有 producer，不建立假資料。
            if (TryNormalizeHorizontal(moveDirection, out worldDirection))
            {
                fromActionCommitment = false;
                return true;
            }

            // Priority 4：維持當前朝向＝不送 request，交給 MotionDriver 保持既有 rotation。
            worldDirection = default;
            fromActionCommitment = false;
            return false;
        }

        /// <summary>
        /// 只回答兩個水平世界方向是否落在同一個死區內，不決定哪一種 facing 來源該使用死區。
        /// 來源政策留在 <see cref="Tick"/>，避免 movement 與站定 Action 再次共享一套錯誤規則。
        /// </summary>
        internal static bool IsWithinFacingDeadzone(
            Vector3 currentForward,
            Vector3 desiredDirection,
            float deadzoneDegrees)
        {
            if (!TryNormalizeHorizontal(currentForward, out Vector3 current) ||
                !TryNormalizeHorizontal(desiredDirection, out Vector3 desired))
            {
                return false;
            }

            return Vector3.Angle(current, desired) <= Mathf.Max(0f, deadzoneDegrees);
        }

        /// <summary>
        /// 將「死區只屬於 Action commitment」表達成可測的來源政策。
        /// movement 即使落在同一角度範圍內也永遠回傳 true，避免小幅航向修正被週期性吞掉。
        /// </summary>
        internal static bool ShouldRequestFacing(
            Vector3 currentForward,
            Vector3 desiredDirection,
            bool fromActionCommitment,
            float deadzoneDegrees)
        {
            return !fromActionCommitment ||
                   !IsWithinFacingDeadzone(currentForward, desiredDirection, deadzoneDegrees);
        }

        private static bool TryNormalizeHorizontal(Vector3 direction, out Vector3 normalized)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= DirectionSqrEpsilon)
            {
                normalized = default;
                return false;
            }

            normalized = direction.normalized;
            return true;
        }
    }
}
