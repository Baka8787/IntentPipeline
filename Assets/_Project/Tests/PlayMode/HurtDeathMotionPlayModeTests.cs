using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.StateMachine;
using Project.Core.Survivability;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// `StateType.Hurt` ／ `StateType.Death` 的**執行期位移**行為（`docs/26` §J）。
    ///
    /// <para><b>為什麼這幾條非 PlayMode 不可</b></para>
    /// 它們驗的是 <c>CharacterController.Move</c> 之後的**真實世界座標**——
    /// EditMode 沒有物理步進，`Move` 不會產生位移，也不會更新 <c>isGrounded</c>。
    /// 換句話說：**「死亡會不會滑行」這件事 EditMode 結構上驗不到。**
    ///
    /// ⚠️ 本檔**不**驗動畫好不好看、硬直手感——那需要人眼，見 `docs/26` §J.5。
    /// </summary>
    public class HurtDeathMotionPlayModeTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private GameObject BuildHost(out MotionDriver driver, out PlayerRuntimeData data)
        {
            var host = new GameObject("Hurt/Death Motion Host");
            _created.Add(host);
            host.transform.position = new Vector3(0f, 0.2f, 0f);
            CharacterController controller = host.AddComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            driver = host.AddComponent<MotionDriver>();
            data = new PlayerRuntimeData();
            return host;
        }

        /// <summary>
        /// 🔴 **死亡不得滑行。**
        ///
        /// 進 Death 的那一幀角色可能正在全速奔跑（<c>MoveSpeed=1</c>／<c>MoveDirection</c> 非零）。
        /// `DeathArbiterSource` 的 `BlockInput` 有**一幀延遲**，而且之後 `LocomotionModel` 的 B9
        /// 減速還要花時間把速度收到 0 ⇒ 若 `DeathState` 走 `ExecuteBaseMovement`，屍體會滑出一段距離。
        ///
        /// `DeathState` 改用 `ExecuteVerticalOnlyMovement` 之後，水平速度在**進入的那一幀**就消失。
        /// 本測試刻意**不歸零** `MoveSpeed`／`MoveDirection`，就是要證明這一點與輸入封鎖無關。
        /// </summary>
        [UnityTest]
        public IEnumerator PD1_DeathState_DoesNotSlideEvenAtFullSpeed()
        {
            GameObject host = BuildHost(out MotionDriver driver, out PlayerRuntimeData data);
            var state = new DeathState();
            yield return null;

            // 角色「正在全速前進」——這兩個欄位是 LocomotionModel 的輸出，死亡不會立刻清掉。
            data.MoveSpeed = 1f;
            data.MoveDirection = Vector3.forward;

            Vector3 before = host.transform.position;
            for (int i = 0; i < 10; i++)
            {
                state.OnUpdateMotion(driver, null, data);
                yield return null;
            }
            Vector3 after = host.transform.position;

            float horizontal = new Vector2(after.x - before.x, after.z - before.z).magnitude;
            Assert.Less(horizontal, 0.01f,
                $"死亡後水平滑行 {horizontal:F3} m —— DeathState 必須走 ExecuteVerticalOnlyMovement，" +
                "不得消費 MoveSpeed × MoveDirection（docs/26 §J）。");
        }

        /// <summary>重力仍要作用：屍體要落到地面，不是凍在半空。</summary>
        [UnityTest]
        public IEnumerator PD2_DeathState_StillFalls()
        {
            GameObject host = BuildHost(out MotionDriver driver, out PlayerRuntimeData data);
            host.transform.position = new Vector3(0f, 5f, 0f);
            var state = new DeathState();
            yield return null;

            float beforeY = host.transform.position.y;
            float earlyVelocity = 0f;
            for (int i = 0; i < 10; i++)
            {
                state.OnUpdateMotion(driver, null, data);
                // ⚠️ 取第 2 幀，不是第 1 幀：`SyncGroundedState` 刻意在**積分之前**發布
                //    `data.VerticalVelocity`（見 MotionDriver 該處註解——落地幀的 impact
                //    velocity 不能被貼地夾持銷毀）⇒ 黑板上的值恆落後一幀，第 1 幀必定是 0。
                if (i == 1) earlyVelocity = data.VerticalVelocity;
                yield return null;
            }

            // ⚠️ 判準刻意**與幀率無關**：Editor 的 PlayMode 幀長不固定，
            //    10 幀可能只有 0.04–0.15 s ⇒ 自由落體距離從 1 cm 到 11 cm 都合理。
            //    用「有沒有在掉」＋「重力有沒有在積分」表達，比寫死一個公尺數穩健。
            //
            // 🐛 2026-09-14：原本第二條斷言寫成 `VerticalVelocity < -0.5f`，那**正是**上面
            //    這段註解禁止的形狀——它其實是一個偽裝成速度的時間門檻（需要約 0.05 s 的
            //    累積）。Editor 跑得快時 10 幀只有 ~0.042 s ⇒ 實測 -0.41、穩定紅燈。
            //    改成**單調遞減**：既表達了「每幀都在積分」（只套一次的話後面不會更負），
            //    又完全不依賴幀長。
            Assert.Less(host.transform.position.y, beforeY,
                "死亡不等於凍結在半空——高度必須下降");
            Assert.Less(earlyVelocity, 0f,
                $"進入後立刻就要開始累積向下速度（實測 {earlyVelocity:F3}）");
            Assert.Less(data.VerticalVelocity, earlyVelocity,
                $"重力必須**每幀持續積分**，不是進入時套一次：" +
                $"第 2 幀 {earlyVelocity:F3} → 第 10 幀 {data.VerticalVelocity:F3}");
        }

        /// <summary>
        /// 🔴 **硬直期間不得繼續水平移動。**
        ///
        /// 若 `HurtState` 走 `ExecuteBaseMovement`，玩家就能一邊踉蹌一邊正常走位，硬直等於不存在；
        /// 若走 `ExecuteBakedCurveMovement`，`Bake_Fists_Hit_Right` 的**無號**速度會沿
        /// `transform.forward` 把角色往前推（實測峰值 2.27 m/s、總位移 0.84 m）——方向與量級都是錯的。
        /// </summary>
        [UnityTest]
        public IEnumerator PD3_HurtState_DoesNotTranslateHorizontally()
        {
            GameObject host = BuildHost(out MotionDriver driver, out PlayerRuntimeData data);
            var state = new HurtState();
            yield return null;

            data.MoveSpeed = 1f;
            data.MoveDirection = Vector3.forward;

            Vector3 before = host.transform.position;
            for (int i = 0; i < 10; i++)
            {
                state.OnUpdateMotion(driver, null, data);
                yield return null;
            }
            Vector3 after = host.transform.position;

            float horizontal = new Vector2(after.x - before.x, after.z - before.z).magnitude;
            Assert.Less(horizontal, 0.01f,
                $"硬直期間水平位移 {horizontal:F3} m —— 受擊第一版是**原地硬直**，" +
                "knockback 明確不在範圍內（docs/26 §J）。");
        }

        /// <summary>
        /// 傷害 → 發布 → FSM 接管，在**真實 Update 迴圈**裡跑一次。
        /// EditMode 的 `HD1` 驗的是同一件事，但那裡的 `PublishTo` 是手動呼叫的；
        /// 這裡走 `CharacterHealth` 自己的 pending flag 生命週期。
        /// </summary>
        [UnityTest]
        public IEnumerator PD4_DamageToHurtPublication_SurvivesARealFrame()
        {
            var host = new GameObject("Health Host");
            _created.Add(host);
            CharacterHealth health = host.AddComponent<CharacterHealth>();
            var data = new PlayerRuntimeData();
            yield return null;

            Assert.IsTrue(health.ApplyDamage(10f));
            health.PublishTo(data);
            Assert.IsTrue(data.Survivability.JustTookDamage, "發布當幀為真");
            Assert.IsFalse(data.Survivability.IsDead);

            yield return null;
            health.PublishTo(data);
            Assert.IsFalse(data.Survivability.JustTookDamage,
                "單幀事件：下一幀整區覆寫後必須自動回 false，不排隊、不補播");
        }
    }
}
