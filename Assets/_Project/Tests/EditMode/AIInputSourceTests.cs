using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 敵人攻擊決策的行為驗證。**架構位置**（攻擊 trigger 只能由 `IInputSource` 產生）
    /// 由 `ArchitectureRegressionTests.A27` 守，本檔只管「條件對不對」。
    /// </summary>
    public sealed class AIInputSourceTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        [Test]
        public void InRange_ProducesSlot1AttackIntent()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 1.5f), range: 2f);

            InputData data = default;
            source.FetchRawInput(ref data);

            Assert.IsTrue(data.Slot1ButtonDown, "目標在射程內就該想出手");
        }

        [Test]
        public void OutOfRange_ProducesNoAttackIntent()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 5f), range: 2f);

            InputData data = default;
            source.FetchRawInput(ref data);

            Assert.IsFalse(data.Slot1ButtonDown, "射程外不得出手");
        }

        /// <summary>沒接目標的敵人應該安靜，不是每幀嘗試攻擊空氣。</summary>
        [Test]
        public void NoTarget_ProducesNoAttackIntent()
        {
            var gameObject = new GameObject("AIInputSource-NoTarget");
            _created.Add(gameObject);
            AIInputSource source = gameObject.AddComponent<AIInputSource>();

            InputData data = default;
            source.FetchRawInput(ref data);

            Assert.IsFalse(data.Slot1ButtonDown);
        }

        /// <summary>
        /// 高低差不得阻斷攻擊。判準刻意與 `AIMovementSource.ResolveEngagementMovement`
        /// 一致（兩者都先把 y 歸零）——否則會出現「距離帶說站好了、攻擊說搆不到」的矛盾。
        /// </summary>
        [Test]
        public void VerticalSeparation_DoesNotBlockAttack()
        {
            Assert.IsTrue(
                AIInputSource.IsWithinAttackRange(Vector3.zero, new Vector3(0f, 8f, 1.5f), 2f),
                "只比水平距離；站在台階或斜坡上不該突然停手");

            Assert.IsFalse(
                AIInputSource.IsWithinAttackRange(Vector3.zero, new Vector3(0f, 0f, 2.5f), 2f),
                "水平距離超出射程仍應拒絕");
        }

        [Test]
        public void AttackRangeBoundary_IsInclusive()
        {
            Assert.IsTrue(AIInputSource.IsWithinAttackRange(Vector3.zero, new Vector3(0f, 0f, 2f), 2f),
                "剛好等於射程應算在內——邊界排除會讓敵人在距離帶邊緣抖動時偶爾漏拳");
        }

        /// <summary>
        /// 攻擊來源**只碰 Slot1**。移動輸入留給 `AIMovementSource`（順序 2.5 直接寫 `MovementIntent`），
        /// 兩者職責不得重疊——否則敵人的移動會有兩個來源。
        /// </summary>
        [Test]
        public void AttackSource_TouchesOnlySlot1()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 1f), range: 2f);

            InputData data = default;
            source.FetchRawInput(ref data);

            Assert.IsTrue(data.Slot1ButtonDown);
            Assert.IsFalse(data.Slot2ButtonDown);
            Assert.IsFalse(data.Slot3ButtonDown);
            Assert.IsFalse(data.JumpButtonDown);
            Assert.IsFalse(data.RollButtonDown);
            Assert.AreEqual(Vector2.zero, data.MoveInput, "移動輸入不歸攻擊來源管");
        }

        /// <summary>
        /// 🔴 **這條是刻意把「已知限制」釘住，不是在慶祝它。**
        ///
        /// `Slot1ButtonDown` 名義上是邊沿訊號（`WasPressedThisFrame` 不可能連續兩幀為真），
        /// 但 `AIInputSource` 目前是 **level-triggered**：只要在射程內就每幀為真。
        ///
        /// **原因是介面限制**：`IInputSource.FetchRawInput(ref InputData)` 拿不到黑板，
        /// 輸入源無從得知上一次按下有沒有被消化 ⇒ 想產生正確節奏的單幀脈衝，只剩
        /// 「AI 自己計時」（使用者已否決，那會讓能不能出手有第二個回答者）
        /// 或「回讀狀態機」（違反依賴方向）兩條路。
        ///
        /// **今天安全**：`EnemyPunchDefinition` 沒有 `Loop`／`WaitForTrigger`／`ChainSegments`，
        /// `ActionState` 兩條會讀 re-trigger 的路徑都走不到，節流全由 `Cooldown` 負責。
        ///
        /// **這條測試存在的意義**：哪天有人把它改成單幀脈衝，這裡會紅——
        /// 那時請先確認節奏來源是什麼，**不要在 AI 裡補計時器**。正解是替 `InputData`
        /// 補一組 `Slot1ButtonHeld`（比照既有的 `SprintButtonHeld` ／ `SprintButtonDown` 並存先例）。
        /// </summary>
        [Test]
        public void AttackIntent_IsLevelTriggered_KnownInterfaceLimit()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 1f), range: 2f);

            for (int frame = 0; frame < 3; frame++)
            {
                InputData data = default;
                source.FetchRawInput(ref data);
                Assert.IsTrue(data.Slot1ButtonDown,
                    $"第 {frame + 1} 幀：目前語意是「一直按著」，節流交給 ActionState 的 Cooldown");
            }
        }

        /// <summary>
        /// 攻擊來源與移動來源必須是**兩個不同的管線階段**。
        /// 同一個元件兼任兩者，就是把攻擊旗標寫回順序 2.5 的那個時序錯誤（A27 守的正是這個）。
        /// </summary>
        [Test]
        public void AttackSource_IsNotAMovementIntentSource()
        {
            AIInputSource source = CreateSource(targetPosition: Vector3.zero, range: 1f);

            Assert.IsInstanceOf<IInputSource>(source, "攻擊決策必須在順序 1 產生");
            Assert.IsNotInstanceOf<IMovementIntentSource>(source,
                "攻擊來源不得同時是 movement producer——順序 2.5 寫的旗標本幀已錯過 ProcessIntents");
        }

        private AIInputSource CreateSource(Vector3 targetPosition, float range)
        {
            var gameObject = new GameObject("AIInputSource-Test");
            _created.Add(gameObject);

            var targetObject = new GameObject("AIInputSource-Target");
            _created.Add(targetObject);
            targetObject.transform.position = targetPosition;

            AIInputSource source = gameObject.AddComponent<AIInputSource>();
            source.ConfigureForTests(targetObject.transform, range);
            return source;
        }
    }
}
