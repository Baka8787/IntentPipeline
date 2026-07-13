using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.StateMachine;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// <see cref="StateMachineConfigSO.GetStateParams{TParams}"/> 泛型安全查表的 EditMode 單元測試。
    /// 驗證 v0.11 落地的 StateParamsSO 機制三個契約：綁定正確型別回傳資產、
    /// 綁定錯誤型別靜默回傳 null（呼叫端 fallback 的前提）、未綁定狀態回傳 null。
    /// paramsMappings 為私有序列化欄位，比照 StateMachineTests 以反射注入，等同 Inspector 手動配置。
    /// </summary>
    public class StateMachineConfigTests
    {
        /// <summary>型別不符測試用的替身參數資產（模擬「Jump 狀態誤掛了別種 StateParamsSO」）。</summary>
        private sealed class WrongTypeParams : StateParamsSO { }

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private StateMachineConfigSO BuildConfig(params StateParamsMapping[] mappings)
        {
            var config = ScriptableObject.CreateInstance<StateMachineConfigSO>();
            _created.Add(config);

            FieldInfo mappingsField = typeof(StateMachineConfigSO)
                .GetField("paramsMappings", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mappingsField, "找不到 StateMachineConfigSO.paramsMappings 私有欄位（欄位名稱可能已變更）");
            mappingsField.SetValue(config, new List<StateParamsMapping>(mappings));

            config.Initialize();
            return config;
        }

        private TParams CreateParams<TParams>() where TParams : StateParamsSO
        {
            var so = ScriptableObject.CreateInstance<TParams>();
            _created.Add(so);
            return so;
        }

        [Test]
        public void GetStateParams_BoundWithCorrectType_ReturnsSameAsset()
        {
            var jumpParams = CreateParams<JumpStateParams>();
            var config = BuildConfig(new StateParamsMapping { State = StateType.Jump, Params = jumpParams });

            var result = config.GetStateParams<JumpStateParams>(StateType.Jump);

            Assert.AreSame(jumpParams, result, "綁定正確型別時應回傳同一份資產參考");
        }

        [Test]
        public void GetStateParams_BoundWithWrongType_ReturnsNullForCallerFallback()
        {
            var wrongParams = CreateParams<WrongTypeParams>();
            var config = BuildConfig(new StateParamsMapping { State = StateType.Jump, Params = wrongParams });

            var result = config.GetStateParams<JumpStateParams>(StateType.Jump);

            Assert.IsNull(result, "型別不符時應靜默回傳 null，讓呼叫端 fallback 到程式碼內建預設值（規格既定行為）");
        }

        [Test]
        public void GetStateParams_UnboundState_ReturnsNull()
        {
            var config = BuildConfig(); // 不綁任何參數資產

            Assert.IsNull(config.GetStateParams<JumpStateParams>(StateType.Jump),
                "未綁定的狀態應回傳 null");
        }
    }
}
