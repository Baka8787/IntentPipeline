using Project.Core.Blackboard;

namespace Project.Core.Arbitration
{
    /// <summary>
    /// 🆕（輪 4）仲裁管線：管線順序 4.5 的執行者，也是 <c>PlayerRuntimeData.Arbitration</c> 的
    /// **全專案唯一執行期寫入者**（dev-spec §1.1 權限表／§7-A5）。
    ///
    /// 職責邊界（design-doc §4.5）：
    /// 「詢問所有 <see cref="IArbiterSource"/> → OR 合併 → 寫黑板」，如此而已。
    /// 本類別**不認識任何具體封鎖語意**——不知道有 UI 模式、不知道有游標、不知道有死亡。
    /// 新增封鎖來源＝實作 <see cref="IArbiterSource"/> 掛上角色階層，本檔與 Runner 皆零改動
    /// （比照 <c>PresentationPipeline</c> 的既有骨架先例）。
    ///
    /// 零 GC：來源陣列於建構時一次配置（來源為 Runner Start 的 <c>GetComponentsInChildren</c>），
    /// 執行期 <see cref="Tick"/> 為純索引 for 迴圈。
    /// ⚠️ **禁用介面型 <c>foreach</c>**——實測 <c>IReadOnlyList&lt;T&gt;</c> 的 foreach 會裝箱 struct
    /// enumerator（每帧 40 B，只有 Profiler 抓得到，A3 靜態掃描無能為力；見 dev-spec §7.1-A3）。
    /// </summary>
    public class ArbiterPipeline
    {
        private readonly IArbiterSource[] _sources;

        public ArbiterPipeline(IArbiterSource[] sources)
        {
            _sources = sources ?? System.Array.Empty<IArbiterSource>();
        }

        /// <summary>
        /// 【管線順序 4.5】重算本幀所有仲裁旗標並整體覆寫黑板仲裁區。
        /// </summary>
        public void Tick(PlayerRuntimeData data)
        {
            // ⚠️ 每帧從 default（全 false）重算，**不**以黑板現值為起點——
            //    否則旗標只會愈疊愈多、永遠關不掉（封鎖是「本幀有沒有人在要求」，不是累積狀態）。
            ArbiterData flags = default;

            for (int i = 0; i < _sources.Length; i++)
            {
                ArbiterData request = _sources[i].Evaluate(data);

                // 合併政策（輪 4 裁決）：**純 OR，任一來源要求即封鎖**。
                // 刻意**不做**優先級／強制解封（某來源可否決他人的封鎖）——那需要真實的競爭情境
                // （死亡 vs 過場誰贏？）才能裁決語意，現在決定等於在沒有壓力測試下把介面定死。
                // 屆時只改本迴圈，所有 IArbiterSource 實作零改動。見 dev-spec §7.3。
                flags.BlockInput      |= request.BlockInput;
                flags.BlockIK         |= request.BlockIK;
                flags.BlockAudio      |= request.BlockAudio;
                flags.BlockExpression |= request.BlockExpression;
            }

            data.Arbitration = flags;
        }
    }
}
