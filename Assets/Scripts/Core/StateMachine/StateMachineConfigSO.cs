using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Presentation.Motion;
using Project.Core.Actions;

namespace Project.Core.StateMachine
{
    [Serializable]
    public struct StateBakeMapping
    {
        public StateType State;
        [Tooltip("該狀態使用的烘焙運動資料，若該狀態不需要則留空")]
        public MotionBakeData BakeData;
    }

    [Serializable]
    public struct StateParamsMapping
    {
        public StateType State;
        [Tooltip("該狀態使用的參數資產（如 JumpStateParams），若該狀態不需要則留空")]
        public StateParamsSO Params;
    }

    [CreateAssetMenu(fileName = "StateMachineConfig", menuName = "Project/Core/StateMachineConfig")]
    public class StateMachineConfigSO : ScriptableObject
    {
        [SerializeField] private List<StateRule> rules = new List<StateRule>();

        [Header("動作烘焙資料配置")]
        [SerializeField] private List<StateBakeMapping> bakeMappings = new List<StateBakeMapping>();

        [Header("狀態專屬參數配置")]
        [SerializeField] private List<StateParamsMapping> paramsMappings = new List<StateParamsMapping>();

        // 🆕（ADR-005 D1）Action 身分索引。本欄位是 FU-2 的解——原本四張表全以 StateType 為鍵，
        // 因此一個角色只能有一份 ActionDefinitionSO。這裡新增一條**平行的、以 ActionSlot 為鍵**的索引，
        // 刻意**不動**既有四張表的形狀（那會波及 Jump/Roll 等與 Action 無關的狀態）。
        // 每份 Definition 自帶 Slot 欄位 ⇒ 這裡只需要一個扁平清單，不需要 mapping struct。
        [Header("Action 定義清單（ADR-005；每份自帶 Slot 身分）")]
        [SerializeField] private List<ActionDefinitionSO> actionDefinitions = new List<ActionDefinitionSO>();

        private readonly Dictionary<StateType, List<StateType>> _interruptMap = new();
        private readonly Dictionary<StateType, List<StateType>> _transitionMap = new();
        private readonly Dictionary<StateType, int> _priorityMap = new();
        private readonly Dictionary<StateType, MotionBakeData> _bakeMap = new();
        private readonly Dictionary<StateType, StateParamsSO> _paramsMap = new();
        private readonly Dictionary<ActionSlot, ActionDefinitionSO> _actionSlotMap = new();

        public void Initialize()
        {
            _interruptMap.Clear();
            _transitionMap.Clear();
            _priorityMap.Clear();
            _bakeMap.Clear();
            _paramsMap.Clear();
            _actionSlotMap.Clear();

            foreach (var rule in rules)
            {
                _interruptMap[rule.State] = rule.CanBeInterruptedBy ?? new List<StateType>();
                _transitionMap[rule.State] = rule.ValidTransitions ?? new List<StateType>();
                _priorityMap[rule.State] = rule.Priority;
            }

            foreach (var mapping in bakeMappings)
            {
                if (mapping.BakeData != null)
                {
                    _bakeMap[mapping.State] = mapping.BakeData;
                }
            }

            foreach (var mapping in paramsMappings)
            {
                if (mapping.Params != null)
                {
                    _paramsMap[mapping.State] = mapping.Params;
                }
            }

            BuildActionSlotMap();
        }

        /// <summary>
        /// 建立 ActionSlot → Definition 索引（ADR-005 D1）。
        ///
        /// **向後相容**：若 <c>actionDefinitions</c> 未填，退回讀取 <c>paramsMappings</c> 裡
        /// 綁在 <see cref="StateType.Action"/> 上的那一份（ADR-004 期間的 Throw／Damage 資產）。
        /// 這讓既有 prefab 不改一個欄位也能繼續跑——資產遷移是使用者側工作，不該被程式強制同步。
        /// </summary>
        private void BuildActionSlotMap()
        {
            for (int i = 0; i < actionDefinitions.Count; i++)
            {
                ActionDefinitionSO definition = actionDefinitions[i];
                if (definition == null || definition.Slot == ActionSlot.None) continue;

#if UNITY_EDITOR
                if (_actionSlotMap.ContainsKey(definition.Slot))
                {
                    Debug.LogError(
                        $"[StateMachineConfig] ActionSlot.{definition.Slot} 被多於一份 Definition 佔用" +
                        $"（後者：{definition.name}）。身分必須唯一，後者已被忽略。", this);
                    continue;
                }
#endif
                _actionSlotMap[definition.Slot] = definition;
            }

            if (_actionSlotMap.Count > 0) return;

            // 相容路徑：舊資產只在 paramsMappings 綁一份 Definition，以它自帶的 Slot 入索引。
            if (_paramsMap.TryGetValue(StateType.Action, out StateParamsSO legacy) &&
                legacy is ActionDefinitionSO legacyDefinition &&
                legacyDefinition.Slot != ActionSlot.None)
            {
                _actionSlotMap[legacyDefinition.Slot] = legacyDefinition;
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
        /// 泛型安全查表：依 StateType 取得綁定的狀態參數資產並轉型為 <typeparamref name="TParams"/>。
        /// 查無綁定或型別不符時回傳 null，呼叫端應自行 fallback 到程式碼內建預設值。
        /// 取代先前散落在 StateRule 內的 Jump 物理欄位與具體 float getter，統一由參數資產（SRP）承載。
        /// </summary>
        public TParams GetStateParams<TParams>(StateType state) where TParams : StateParamsSO
        {
            return _paramsMap.TryGetValue(state, out var stateParams) ? stateParams as TParams : null;
        }

        /// <summary>
        /// 🆕（ADR-005 D1）依 Action 身分取得 Definition。查無回傳 null，呼叫端據此拒絕進入。
        /// </summary>
        public ActionDefinitionSO GetActionDefinition(ActionSlot slot)
        {
            return _actionSlotMap.TryGetValue(slot, out var definition) ? definition : null;
        }

        /// <summary>本 Config 綁定了幾份 Action Definition。供組裝期驗證與測試使用。</summary>
        public int ActionDefinitionCount => _actionSlotMap.Count;
    }
}
