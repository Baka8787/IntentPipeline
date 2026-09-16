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
        public void FirstFrameInRange_ProducesOneSlot1Request()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 1.5f), range: 2f);

            InputData data = default;
            source.FetchRawInputAtTimeForTests(ref data, 10f);

            Assert.IsTrue(data.Slot1ButtonDown, "第一次符合持續條件時應立即送一次 request pulse");
        }

        [Test]
        public void OutOfRange_ProducesNoAttackIntent()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 5f), range: 2f);

            InputData data = default;
            source.FetchRawInputAtTimeForTests(ref data, 10f);

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
            source.FetchRawInputAtTimeForTests(ref data, 10f);

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
            source.FetchRawInputAtTimeForTests(ref data, 10f);

            Assert.IsTrue(data.Slot1ButtonDown);
            Assert.IsFalse(data.Slot2ButtonDown);
            Assert.IsFalse(data.Slot3ButtonDown);
            Assert.IsFalse(data.JumpButtonDown);
            Assert.IsFalse(data.RollButtonDown);
            Assert.AreEqual(Vector2.zero, data.MoveInput, "移動輸入不歸攻擊來源管");
        }

        /// <summary>
        /// 持續 desire 不得重新退化成 level-trigger；同一個 retry window 內只有第一幀是 pulse。
        /// </summary>
        [Test]
        public void StayingInRange_DoesNotProduceSlot1EveryFrame()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 1f), range: 2f);

            InputData firstFrame = default;
            source.FetchRawInputAtTimeForTests(ref firstFrame, 10f);
            InputData secondFrame = default;
            source.FetchRawInputAtTimeForTests(ref secondFrame, 10.016f);
            InputData thirdFrame = default;
            source.FetchRawInputAtTimeForTests(ref thirdFrame, 10.032f);

            Assert.IsTrue(firstFrame.Slot1ButtonDown);
            Assert.IsFalse(secondFrame.Slot1ButtonDown);
            Assert.IsFalse(thirdFrame.Slot1ButtonDown);
            Assert.IsTrue(source.WantsToAttack(),
                "request pulse 已落下不代表 attack desire 消失；WantsToAttack 必須維持持續條件");
        }

        [Test]
        public void RetryIntervalNotReached_ProducesNoSecondRequest()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 1f), range: 2f);

            InputData first = default;
            source.FetchRawInputAtTimeForTests(ref first, 5f);
            InputData beforeRetry = default;
            source.FetchRawInputAtTimeForTests(ref beforeRetry, 5.499f);

            Assert.IsTrue(first.Slot1ButtonDown);
            Assert.IsFalse(beforeRetry.Slot1ButtonDown);
        }

        [Test]
        public void RetryIntervalReached_ProducesAnotherSingleFrameRequest()
        {
            AIInputSource source = CreateSource(targetPosition: new Vector3(0f, 0f, 1f), range: 2f);

            InputData first = default;
            source.FetchRawInputAtTimeForTests(ref first, 5f);
            InputData retry = default;
            source.FetchRawInputAtTimeForTests(ref retry, 5.5f);
            InputData followingFrame = default;
            source.FetchRawInputAtTimeForTests(ref followingFrame, 5.516f);

            Assert.IsTrue(retry.Slot1ButtonDown);
            Assert.IsFalse(followingFrame.Slot1ButtonDown,
                "retry 本身仍只能是一幀 pulse，下一幀必須回 false");
        }

        [Test]
        public void LeavingAttackRange_StopsRequests()
        {
            AIInputSource source = CreateSource(
                targetPosition: new Vector3(0f, 0f, 1f), range: 2f, out Transform targetTransform);

            InputData first = default;
            source.FetchRawInputAtTimeForTests(ref first, 5f);
            targetTransform.position = new Vector3(0f, 0f, 5f);
            InputData outsideAfterRetryWouldHaveElapsed = default;
            source.FetchRawInputAtTimeForTests(ref outsideAfterRetryWouldHaveElapsed, 6f);

            Assert.IsFalse(outsideAfterRetryWouldHaveElapsed.Slot1ButtonDown);
        }

        [Test]
        public void ReenteringAttackRange_RearmsImmediateRequest()
        {
            AIInputSource source = CreateSource(
                targetPosition: new Vector3(0f, 0f, 1f), range: 2f, out Transform targetTransform);

            InputData first = default;
            source.FetchRawInputAtTimeForTests(ref first, 5f);
            targetTransform.position = new Vector3(0f, 0f, 5f);
            InputData outside = default;
            source.FetchRawInputAtTimeForTests(ref outside, 5.1f);
            targetTransform.position = new Vector3(0f, 0f, 1f);
            InputData reentered = default;
            source.FetchRawInputAtTimeForTests(ref reentered, 5.2f);

            Assert.IsTrue(reentered.Slot1ButtonDown,
                "離開射程應重新武裝；再次進入不必等待舊 retry interval");
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
            return CreateSource(targetPosition, range, out _);
        }

        private AIInputSource CreateSource(Vector3 targetPosition, float range, out Transform targetTransform)
        {
            var gameObject = new GameObject("AIInputSource-Test");
            _created.Add(gameObject);

            var targetObject = new GameObject("AIInputSource-Target");
            _created.Add(targetObject);
            targetObject.transform.position = targetPosition;
            targetTransform = targetObject.transform;

            AIInputSource source = gameObject.AddComponent<AIInputSource>();
            source.ConfigureForTests(targetObject.transform, range);
            return source;
        }
    }
}
