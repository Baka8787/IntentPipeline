using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Core.Arbitration.Sources
{
    /// <summary>
    /// 🆕（ADR-009）第二顆 <see cref="IArbiterSource"/>：**死亡封鎖輸入**。
    ///
    /// <para><b>為什麼用既有的仲裁層，而不是讓輸入層自己查死亡</b></para>
    /// <c>ArbiterPipeline</c> 的檔頭本來就寫著「本類別不認識任何具體封鎖語意——
    /// 不知道有 UI 模式、不知道有游標、**不知道有死亡**」，並言明新增封鎖來源只要實作本介面、
    /// 掛上角色階層，管線與 Runner 零改動。這顆元件就是那句話的兌現。
    ///
    /// 更關鍵的是**輸入層查不到死亡**：`A27` 明文禁止 <c>AIInputSource</c> 出現
    /// <c>PlayerRuntimeData</c>（producer 不得回讀 gameplay state）。
    /// ⇒「死後不再產生 intent」**只能**走仲裁層，這不是選擇，是既有不變量推出來的結論。
    ///
    /// <para><b>⚠️ BlockInput 有一幀延遲，而且那不要緊</b></para>
    /// 仲裁在順序 4.5 寫入、順序 2 的閘門下一幀才看到（dev-spec §2.1 脆弱點，刻意不修）。
    /// 死亡的**真正**保證來自 <c>DeathState</c> 是最高優先權的吸收態：
    /// 順序 0.5 發布 <c>IsDead</c> → 順序 4 的 FSM 當幀就被 Death 接管，之後任何狀態都換不掉它。
    /// 本元件封鎖的是「意圖還在產生」造成的雜訊（例如死亡瞬間仍按著 W），不是行為正確性的最後防線。
    ///
    /// <para><b>只封鎖 <see cref="ArbiterData.BlockInput"/></b></para>
    /// 刻意**不**封鎖 IK／Audio／Expression：屍體仍在地面上，Foot IK 應該繼續貼地；
    /// 死亡音效與表情屬於表現層的事，沒有證據說它們該被關掉。第二個需求出現前不擴張。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeathArbiterSource : MonoBehaviour, IArbiterSource
    {
        /// <summary>【管線順序 4.5】死亡即請求封鎖輸入；其餘旗標留 <c>false</c> 交給別的來源。</summary>
        public ArbiterData Evaluate(PlayerRuntimeData data)
        {
            ArbiterData request = default;
            if (data != null) request.BlockInput = data.Survivability.IsDead;
            return request;
        }
    }
}
