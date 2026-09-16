using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Project.App;
using Project.Core.Survivability;
using Project.Presentation.Motion;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 重生協調（2026-09-14 使用者裁決）。
    ///
    /// <para><b>本檔驗的是「順序」與「邊界」，不是「重生好不好玩」</b></para>
    /// <c>RespawnController</c> 自己**沒有任何真相**——血量在 <c>CharacterHealth</c>、
    /// 位置在 <c>MotionDriver</c>。它唯一的內容就是**先整理世界、再宣告活著**。
    /// 所以這裡能驗、也只該驗：它有沒有照那個順序做、以及它在不該動作時有沒有安靜。
    ///
    /// <para><b>FSM 那一半由誰驗</b></para>
    /// 「<c>IsDead</c> 變回 false 之後 Death 會放行」是 `HurtAndDeathStateTests.HD9`／`HD10`；
    /// 「出貨 config 的 Death 出口恰好是 Idle／Move」是 `ArchitectureRegressionTests.A38`。
    /// 本檔**不重複**那兩件事。
    /// </summary>
    public sealed class RespawnControllerTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [Test]
        public void Respawn_WhileAlive_DoesNothing()
        {
            RespawnController controller = CreateRig(out CharacterHealth health, out GameObject character, out _);
            Vector3 positionBefore = character.transform.position;

            Assert.IsFalse(controller.Respawn(), "角色還活著 ⇒ 不做任何事，回 false");
            Assert.AreEqual(positionBefore, character.transform.position,
                "⛔ 活著時按重生鍵不得把人傳走——那是一個沒人設計過的瞬移能力");
            Assert.IsFalse(health.IsDead);
        }

        /// <summary>
        /// 死掉之後重生：**人回到重生點，而且活了**。
        /// 這是整個功能的最小可觀測結果。
        /// </summary>
        [Test]
        public void Respawn_WhileDead_TeleportsToPointAndRevives()
        {
            RespawnController controller = CreateRig(
                out CharacterHealth health, out GameObject character, out Transform point);

            character.transform.position = new Vector3(50f, 0f, -20f);
            health.ApplyDamage(999f);
            Assert.IsTrue(health.IsDead, "測試前提：先真的死掉");

            Assert.IsTrue(controller.Respawn());

            Assert.AreEqual(point.position, character.transform.position,
                "必須回到重生點");
            Assert.IsFalse(health.IsDead, "必須真的活過來");
            Assert.AreEqual(health.MaxHealth, health.CurrentHealth, 0.0001f, "回滿血");
        }

        /// <summary>
        /// 傳送必須把**垂直速度與重力狀態**一併清乾淨。
        ///
        /// 墜落致死時 <c>MotionDriver</c> 的垂直速度是一個很大的負值；不清掉的話，
        /// 角色會在重生點的第一幀就以墜落速度往下衝——看起來像「重生點在地板下面」。
        /// ⚠️ 這條驗的是 <c>MotionDriver.Teleport</c> 的清理責任，
        /// 而 <c>Teleport</c> 之所以住在 MotionDriver，是因為它是 position 的**單一寫入者**。
        /// </summary>
        [Test]
        public void Teleport_ResetsVerticalVelocity()
        {
            RespawnController controller = CreateRig(
                out CharacterHealth health, out GameObject character, out Transform point);
            MotionDriver driver = character.GetComponent<MotionDriver>();

            SetPrivateField(driver, "_verticalVelocity", -42f);
            health.ApplyDamage(999f);

            Assert.IsTrue(controller.Respawn());

            Assert.AreEqual(0f, GetPrivateField<float>(driver, "_verticalVelocity"), 0.0001f,
                "傳送後垂直速度必須歸零——否則重生第一幀就以墜落速度往下衝");
            Assert.AreEqual(point.position, character.transform.position);
        }

        /// <summary>
        /// 沒有指派重生點時，退回**場景開始時**的位置——不是「當下位置」。
        /// 若退路取的是當下位置，重生點會跟著角色漂移，等於沒有重生點：
        /// 死在哪就在哪站起來，而畫面上看起來只是「重生好像沒作用」。
        /// </summary>
        [Test]
        public void Respawn_WithoutPoint_FallsBackToStartingPose()
        {
            var character = new GameObject("Respawn-Fallback");
            _created.Add(character);
            character.transform.position = new Vector3(3f, 0f, 4f);
            CharacterController capsule = character.AddComponent<CharacterController>();
            MotionDriver driver = character.AddComponent<MotionDriver>();
            SetPrivateField(driver, "characterController", capsule);
            CharacterHealth health = character.AddComponent<CharacterHealth>();
            health.InitializeForTest(100f);

            RespawnController controller = character.AddComponent<RespawnController>();
            controller.ConfigureForTests(health, driver, null);
            // EditMode 不呼叫 Awake ⇒ 手動重現它「記下開場姿態」的那一步。
            InvokeAwake(controller);

            character.transform.position = new Vector3(99f, 0f, 99f);
            health.ApplyDamage(999f);

            Assert.IsTrue(controller.Respawn());
            Assert.AreEqual(new Vector3(3f, 0f, 4f), character.transform.position,
                "沒有重生點時退回開場位置，⛔ 不得退回「當下位置」（那等於沒有重生點）");
        }

        /// <summary>
        /// 🐛 **退化配置（沒有 <c>CharacterController</c>）也必須清掉垂直速度。**
        ///
        /// 這條是 2026-09-14 首跑抓到的真缺陷：`Teleport` 原本在
        /// <c>characterController == null</c> 時 early-return，於是角色**被搬到新位置、
        /// 卻留著死前的垂直速度**——而清掉那個速度正是這個方法存在的理由之一。
        /// 只有「停用／還原 CharacterController」那一步需要它存在，其餘清理無論如何都要跑。
        /// </summary>
        [Test]
        public void Teleport_WithoutCharacterController_StillResetsVerticalVelocity()
        {
            var character = new GameObject("Teleport-No-Controller");
            _created.Add(character);
            MotionDriver driver = character.AddComponent<MotionDriver>();
            SetPrivateField(driver, "_verticalVelocity", -42f);

            driver.Teleport(new Vector3(1f, 2f, 3f), Quaternion.identity);

            Assert.AreEqual(new Vector3(1f, 2f, 3f), character.transform.position, "仍要搬得動");
            Assert.AreEqual(0f, GetPrivateField<float>(driver, "_verticalVelocity"), 0.0001f,
                "沒有 CharacterController 也必須清掉垂直速度——⛔ 不得因為退化配置就跳過清理");
        }

        /// <summary>接線缺失時必須安靜地什麼都不做，不得丟例外。</summary>
        [Test]
        public void Respawn_WithoutHealth_DoesNothing()
        {
            var host = new GameObject("Respawn-No-Health");
            _created.Add(host);
            RespawnController controller = host.AddComponent<RespawnController>();
            controller.ResolveDependencies();

            Assert.IsNull(controller.ResolvedHealth, "測試前提：身上沒有 CharacterHealth");
            Assert.DoesNotThrow(() => controller.Respawn());
        }

        // =====================================================================
        // 治具
        // =====================================================================

        private RespawnController CreateRig(
            out CharacterHealth health, out GameObject character, out Transform respawnPoint)
        {
            character = new GameObject("Respawn-Character");
            _created.Add(character);
            character.transform.position = Vector3.zero;
            CharacterController capsule = character.AddComponent<CharacterController>();
            MotionDriver driver = character.AddComponent<MotionDriver>();
            // EditMode 不呼叫 Awake ⇒ 手動補上 production 在 Awake 做的 GetComponent 補洞，
            // 否則測試會走「沒有 CharacterController」的退化路徑，驗不到真正的傳送流程。
            SetPrivateField(driver, "characterController", capsule);

            health = character.AddComponent<CharacterHealth>();
            health.InitializeForTest(100f);

            var point = new GameObject("Respawn-Point");
            _created.Add(point);
            point.transform.SetPositionAndRotation(
                new Vector3(-7f, 0f, 11f), Quaternion.Euler(0f, 90f, 0f));
            respawnPoint = point.transform;

            RespawnController controller = character.AddComponent<RespawnController>();
            controller.ConfigureForTests(health, driver, respawnPoint);
            return controller;
        }

        private static void InvokeAwake(MonoBehaviour behaviour)
        {
            System.Reflection.MethodInfo awake = behaviour.GetType().GetMethod(
                "Awake",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(awake, $"找不到 {behaviour.GetType().Name}.Awake");
            awake.Invoke(behaviour, System.Array.Empty<object>());
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName}（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName}（欄位名稱可能已變更）");
            return (T)field.GetValue(target);
        }
    }
}
