using System.Collections.Generic;
using UnityEngine;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 🆕（ADR-002）單一跳躍段。每段引用一份 <see cref="MotionBakeData"/>，
    /// 其 <c>AutoTakeoffDelay</c> / <c>AutoApexHeight</c> / <c>AutoCalculatedGravity</c>
    /// 即該段的「動畫資料」（唯一真相）。三個 Designer Tuning 倍率屬 <see cref="JumpStateParams"/>
    /// 全域層級，不放在每段。
    /// </summary>
    [System.Serializable]
    public struct JumpStage
    {
        [Tooltip("該段跳躍對應 clip 的烘焙資料，提供 AutoTakeoffDelay / AutoApexHeight / AutoCalculatedGravity")]
        public MotionBakeData Bake;
    }

    /// <summary>
    /// Jump 狀態的參數資產。
    /// 🆕（ADR-002）拔除硬編碼的 TakeoffDelay / ImpulseForce（物理量改由各段 <see cref="MotionBakeData"/>
    /// 提供、單一真相）；改承載「跳躍內容（Stages）」與「設計師手感倍率（Designer Tuning）」。
    /// 透過 <see cref="StateMachineConfigSO"/> 的 paramsMappings 綁定至 <see cref="StateType.Jump"/>，
    /// 由 <c>JumpState</c> 在 Initialize 時查表快取。
    /// </summary>
    /// <remarks>
    /// Movement Assistance（Coyote Time / Jump Buffer）與 Variable Jump（Min/Max Hold /
    /// Early Release Gravity Multiplier）的欄位與行為，依 ADR-002 §6-4 留待後續 ADR 定義其
    /// Owner/Writer/Reader 與輸入來源後再落地，此處刻意不先放無行為的死設定。
    /// </remarks>
    [CreateAssetMenu(fileName = "JumpStateParams", menuName = "Project/Core/StateParams/JumpStateParams")]
    public class JumpStateParams : StateParamsSO
    {
        [Header("Content — Multi Jump")]
        [Tooltip("有序段清單：第 0 段 = 地面跳，其後為空中段。可跳段數上限即本清單長度（不另設欄位）。")]
        public List<JumpStage> Stages = new List<JumpStage>();

        [Header("Designer Tuning（倍率，預設 1 = 完全依烘焙值）")]
        [Tooltip("apex 高度倍率，乘在各段 AutoApexHeight 上")]
        public float HeightMultiplier = 1f;

        [Tooltip("重力倍率，乘在各段 AutoCalculatedGravity 上")]
        public float GravityMultiplier = 1f;

        [Tooltip("起跳初速倍率，乘在逆推出的 v = √(2gh) 上")]
        public float LaunchVelocityMultiplier = 1f;
    }
}
