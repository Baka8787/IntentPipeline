using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    public class RollState : BaseState
    {
        public override StateType Type => StateType.Roll;

        // 查無烘焙資料時的安全退化時長（秒）。與下方警告訊息共用同一常數，避免行為與提示不一致。
        private const float FallbackDuration = 0.5f;

        private float _rollTimer;
        public bool IsRollFinished { get; private set; }
        public override bool CanTransitionAway => IsRollFinished;

        private MotionBakeData _rollBakeData;

        // 🆕 移除建構子，改在 Initialize 裡查表
        public override void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            base.Initialize(config, movementModel);
            _rollBakeData = config.GetBakeData(Type);

#if UNITY_EDITOR
            // 🆕（v0.16.2）設計問題提示（非限制）：翻滾的時長與曲線位移皆由 MotionBakeData 驅動，
            // 查無資料時只能退化為固定計時 + 一般移動（原地翻滾感、且會提早結束）。此警告讓「資產斷鏈」
            // 在進 Play 的第一時間現形，而非靠肉眼發現「翻滾怪怪的」——2026-07-17 遷移 FBX 子 clip 直引後，
            // Config.bakeMappings 指向已刪舊 Bake 造成 Roll 秒退，正是此類無聲斷鏈的實例。
            // 🆕（M2 Warning 治理）補上 Application.isPlaying 條件，把防線語義寫精確：它守護的是
            // 「進 Play 後 Roll 會退化」，而產品組裝只發生在 Play 中（Runner.Start → 狀態機 Initialize）；
            // EditMode 測試以最小拓撲 config（無 bakeMappings）組裝狀態機是有意的合法輸入，不是斷鏈。
            // Player build 整段已被 UNITY_EDITOR 排除，因此本條件唯一排除的就是 EditMode 測試環境——
            // 偵測力零損失，測試側也不必以 LogAssert 耦合警告文字。
            if (_rollBakeData == null && Application.isPlaying)
            {
                Debug.LogWarning(
                    $"[RollState] 查無 {Type} 的 MotionBakeData（StateMachineConfig 的 bakeMappings 未綁定或引用已失效）。" +
                    $"翻滾將退化為固定 {FallbackDuration}s 計時、且不套用烘焙曲線位移（原地翻滾感、提早結束）。" +
                    "請檢查 Config.bakeMappings 是否指向有效的 Bake 資產（例如動畫改走 FBX 子 clip 直引後，Bake 的 SourceClip 或 Config 映射是否已同步更新）。");
            }

            // 🆕（2026-07-26）第二種斷鏈：資產**存在但沒有可用時長**——`BakedDuration` 於 2026-07-26 導入，
            // 在該日之前烘焙的舊資產此欄位為 0。舊寫法只擋 `_rollBakeData == null`，擋不到這種「半斷鏈」，
            // 於是 `_rollTimer` 直接為 0、翻滾第一帧就結束，而 FallbackDuration 永遠用不到。
            // 現在行為上已由 OnEnter 的 `> 0` 閘門接住，這裡負責讓「該重烘了」這件事在 Play 的第一時間現形。
            if (_rollBakeData != null && _rollBakeData.Duration <= 0f && Application.isPlaying)
            {
                Debug.LogWarning(
                    $"[RollState] {Type} 的 MotionBakeData（{_rollBakeData.name}）BakedDuration 為 0——" +
                    "此資產是在 BakedDuration 欄位導入前烘焙的（或來源 clip 當時不可用）。" +
                    $"翻滾已安全退化為固定 {FallbackDuration}s 計時，但曲線位移仍會以 t=0 取樣（原地翻滾感）。" +
                    "請以 Motion Bake 工具**重跑一次烘焙**寫入正確時長。");
            }
#endif
        }

        // 🆕 翻滾同樣需著地才能發動，確保無法在空中翻滾
        public override bool CanEnter(PlayerRuntimeData data) => data.Intent.RollRequested && data.IsGrounded;

        public override void OnEnter(PlayerRuntimeData data)
        {
            // 富文本字串會產生 GC Alloc，比照 JumpState / CharacterPipelineRunner 慣例（ADR-002 §3）
            // 包進 UNITY_EDITOR，Release 建置由編譯器直接移除。
#if UNITY_EDITOR
            Debug.Log("<color=cyan>[State] 進入 ROLL 翻滾（無敵幀開始）</color>");
#endif
            // 🆕（2026-07-26）退化條件從「資產是否存在」擴為「資產是否帶得出可用時長」。
            // 舊寫法 `_rollBakeData != null ? Duration : FallbackDuration` 有個守不到的洞：
            // 資產存在、但 Duration 為 0（未重烘／來源 clip 缺席）時 `_rollTimer` 會是 0，
            // 翻滾第一帧即結束，且 FallbackDuration 永遠用不到。改以「值本身」而非「引用」判定。
            float bakedDuration = _rollBakeData != null ? _rollBakeData.Duration : 0f;
            _rollTimer = bakedDuration > 0f ? bakedDuration : FallbackDuration;
            IsRollFinished = false;
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (IsRollFinished) return;

            _rollTimer -= deltaTime;
            if (_rollTimer <= 0)
            {
                IsRollFinished = true;
#if UNITY_EDITOR
                Debug.Log("<color=blue>[State] ROLL 翻滾結束</color>");
#endif
            }
        }

        public override void OnExit(PlayerRuntimeData data) => IsRollFinished = false;

        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            // 在信任 GetNormalizedTime() 之前，先確認動畫實際上真的在播 Roll clip。
            // 若 AnimancerFacade.Play() 因為 clip mapping 查表失敗而提前 return（只會 LogWarning，不拋例外），
            // GetNormalizedTime() 仍會回傳「目前 Layer 0 正在播放的舊動畫」進度，
            // 若不做這層檢查，位移會被一個跟 Roll 完全不相干的進度值驅動，只會在畫面上看到角色亂飄，
            // 主控台卻只有一句容易被忽略的 warning。
            bool hasValidBakeData = _rollBakeData != null;
            bool isActuallyPlayingRoll = animationFacade != null && animationFacade.IsPlaying(AnimationKey);

            if (!hasValidBakeData || !isActuallyPlayingRoll)
            {
                motionDriver.ExecuteBaseMovement(data); // 防呆：退回一般 Procedural 結算
                return;
            }

            float normalizedTime = animationFacade.GetNormalizedTime();
            motionDriver.ExecuteBakedCurveMovement(_rollBakeData, normalizedTime, data);
        }
    }
}