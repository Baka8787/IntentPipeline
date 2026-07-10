using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine
{
    [Serializable]
    public struct StateRule
    {
        public StateType State;
        [Tooltip("數值越高，同帧複數意圖觸發時越優先執行")]
        public int Priority;
        [Tooltip("哪些狀態可以主動打斷當前狀態（意圖觸發時檢查）")]
        public List<StateType> CanBeInterruptedBy;
        [Tooltip("當前狀態結束或無意圖時，允許自然過渡到的狀態優先級")]
        public List<StateType> ValidTransitions;

        // 🆕（v0.7 Code Review）取代 JumpState 身上失效的 [SerializeField]。
        // BaseState 子類別是純 C# 類別、非 MonoBehaviour/ScriptableObject，欄位無法被 Unity 序列化，
        // 掛 [SerializeField] 在那邊只是死碼、Inspector 永遠看不到也調不到。
        // 依規格書慣例（比照 RollState 從 Config SO 查表拿 MotionBakeData 的做法），
        // 需要 Inspector 可調數值的狀態一律走這裡，而不是狀態類別本身。
        [Tooltip("僅供需要垂直衝量的狀態使用（如 Jump）。0 表示未在此設定，執行期會 fallback 到程式碼內建預設值")]
        public float JumpImpulseForce;

        // 🆕（v0.8，除錯「先蹲下再往上」發現）Jump 動畫若有預備蹲下姿勢（Anticipation），
        // 該姿勢是骨骼 Pose、跟根位移無關，若物理衝量在進入狀態當幀就注入，
        // 會跟動畫預備動作的時間軸對不上，看起來像「已經在往上飛，但身體還在蹲」。
        // 設定此值 = 預備動作的秒數，JumpState 會在這段時間內維持一般貼地移動，等時間到才真正注入衝量。
        [Tooltip("跳躍衝量注入前的延遲秒數，用於等待動畫預備/蹲下姿勢播完再真正離地。0 = 不延遲，沿用原本行為")]
        public float JumpTakeoffDelay;

        // 🆕（v0.8 除錯發現）起跳「前搖／預備動作」與物理離地時機脫鉤：
        // 目前一進入 Jump 狀態就立刻注入垂直衝量，但 Jump 動畫 clip 開頭若有下蹲蓄力的前搖幀，
        // 骨架還在播蹲下姿勢時，CharacterController 已經瞬間往上飄，造成「先蹲下再往上、數據不符」。
        // 參考 BBBNexus 的 MotionType.Mixed + RotationFinishedTime 概念：
        // 用一個時間閾值，讓前搖播完才切換到「真正離地」的行為。
        [Tooltip("僅供 Jump 使用：延遲幾秒才真正注入起跳衝量，等待動畫前搖（下蹲蓄力）播放完畢。0 表示維持原本『進入狀態立即注入』的行為")]
        public float JumpLaunchDelay;
    }

    [Serializable]
    public struct StateBakeMapping
    {
        public StateType State;
        [Tooltip("該狀態使用的烘焙運動資料，若該狀態不需要則留空")]
        public MotionBakeData BakeData;
    }

    [CreateAssetMenu(fileName = "StateMachineConfig", menuName = "Project/Core/StateMachineConfig")]
    public class StateMachineConfigSO : ScriptableObject
    {
        [SerializeField] private List<StateRule> rules = new List<StateRule>();

        [Header("動作烘焙資料配置")]
        [SerializeField] private List<StateBakeMapping> bakeMappings = new List<StateBakeMapping>();

        private readonly Dictionary<StateType, List<StateType>> _interruptMap = new();
        private readonly Dictionary<StateType, List<StateType>> _transitionMap = new();
        private readonly Dictionary<StateType, int> _priorityMap = new();
        private readonly Dictionary<StateType, MotionBakeData> _bakeMap = new();
        private readonly Dictionary<StateType, float> _jumpImpulseMap = new(); // 🆕
        private readonly Dictionary<StateType, float> _jumpTakeoffDelayMap = new(); // 🆕 v0.8

        public void Initialize()
        {
            _interruptMap.Clear();
            _transitionMap.Clear();
            _priorityMap.Clear();
            _bakeMap.Clear();
            _jumpImpulseMap.Clear(); // 🆕
            _jumpTakeoffDelayMap.Clear(); // 🆕 v0.8

            foreach (var rule in rules)
            {
                _interruptMap[rule.State] = rule.CanBeInterruptedBy ?? new List<StateType>();
                _transitionMap[rule.State] = rule.ValidTransitions ?? new List<StateType>();
                _priorityMap[rule.State] = rule.Priority;
                _jumpImpulseMap[rule.State] = rule.JumpImpulseForce; // 🆕
                _jumpTakeoffDelayMap[rule.State] = rule.JumpTakeoffDelay; // 🆕 v0.8
            }

            foreach (var mapping in bakeMappings)
            {
                if (mapping.BakeData != null)
                {
                    _bakeMap[mapping.State] = mapping.BakeData;
                }
            }
        }

        public bool CheckCanInterrupt(StateType currentState, StateType nextState)
        {
            return _interruptMap.TryGetValue(currentState, out var list) && list.Contains(nextState);
        }

        public IReadOnlyList<StateType> GetValidTransitions(StateType state)
        {
            return _transitionMap.TryGetValue(state, out var list) ? list : Array.Empty<StateType>();
        }

        public int GetPriority(StateType state)
        {
            return _priorityMap.TryGetValue(state, out var priority) ? priority : 0;
        }

        public MotionBakeData GetBakeData(StateType state)
        {
            return _bakeMap.TryGetValue(state, out var data) ? data : null;
        }

        /// <summary>
        /// 取代 JumpState 身上失效的 [SerializeField] jumpImpulseForce。
        /// 回傳 0 代表 Config 內未設定該狀態的衝量值，呼叫端應自行 fallback 到內建預設值。
        /// </summary>
        public float GetJumpImpulseForce(StateType state)
        {
            return _jumpImpulseMap.TryGetValue(state, out var force) ? force : 0f;
        }

        /// <summary>
        /// 🆕（v0.8）取得跳躍衝量注入前的延遲秒數，用於等待預備動畫播完。回傳 0 代表未設定。
        /// </summary>
        public float GetJumpTakeoffDelay(StateType state)
        {
            return _jumpTakeoffDelayMap.TryGetValue(state, out var delay) ? delay : 0f;
        }
    }
}