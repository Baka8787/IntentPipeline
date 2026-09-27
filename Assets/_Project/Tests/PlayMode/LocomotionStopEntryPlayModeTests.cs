using System.Collections;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// docs/07 R8：Stop 第一段播放頭區間不得積到烘焙器的 SpeedCurve(0)=0 佔位 key（會變半速），
    /// 且其後的減速曲線必須逐字不變。
    /// </summary>
    public class LocomotionStopEntryPlayModeTests
    {
        private GameObject _go;
        private MotionBakeData _bake;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            if (_bake != null) Object.Destroy(_bake);
        }

        [UnityTest]
        public IEnumerator StopFirstInterval_UsesFirstMeasuredSpeed_AndLaterCurveIsUnchanged()
        {
            _go = new GameObject("StopEntryDriver");
            _go.transform.position = new Vector3(0f, 50f, 0f); // 遠離場景幾何：只量水平位移
            _go.AddComponent<CharacterController>();
            MotionDriver driver = _go.AddComponent<MotionDriver>();

            _bake = ScriptableObject.CreateInstance<MotionBakeData>();
            _bake.BakedDuration = 1f;
            _bake.SampleRate = 60f;
            // 與真實 Stop bake 同形：第 0 key 是邊界佔位 (0,0)，之後才是量到的速度。
            _bake.SpeedCurve = Linear(new Vector2(0f, 0f), new Vector2(1f / 60f, 3.5f), new Vector2(0.5f, 2f), new Vector2(1f, 0f));
            _bake.RotationCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f));
            var data = new PlayerRuntimeData();

            yield return null;
            Vector3 before = _go.transform.position;
            driver.ExecuteLocomotionStopMovement(_bake, 0.02f, 0f, data);
            Vector3 d = _go.transform.position - before;
            float firstInterval = new Vector2(d.x, d.z).magnitude;
            float expected = 0.5f * (_bake.GetSpeedAt(1f / 60f) + _bake.GetSpeedAt(0.02f)) * 0.02f;
            Assert.AreEqual(expected, firstInterval, 0.0005f,
                "第一段必須以第一個量到的速度起算；舊行為會是一半（佔位 key 0 拉低梯形平均）。");
            Assert.Greater(firstInterval, 0.06f);

            yield return null;
            before = _go.transform.position;
            driver.ExecuteLocomotionStopMovement(_bake, 0.52f, 0.5f, data);
            d = _go.transform.position - before;
            float midInterval = new Vector2(d.x, d.z).magnitude;
            float midExpected = 0.5f * (_bake.GetSpeedAt(0.5f) + _bake.GetSpeedAt(0.52f)) * 0.02f;
            Assert.AreEqual(midExpected, midInterval, 0.0005f, "佔位 key 之後的減速曲線必須逐字不變。");
        }

        private static AnimationCurve Linear(params Vector2[] points)
        {
            var keys = new Keyframe[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float inSlope = i > 0 ? (points[i].y - points[i - 1].y) / (points[i].x - points[i - 1].x) : 0f;
                float outSlope = i < points.Length - 1 ? (points[i + 1].y - points[i].y) / (points[i + 1].x - points[i].x) : 0f;
                keys[i] = new Keyframe(points[i].x, points[i].y, inSlope, outSlope);
            }
            return new AnimationCurve(keys);
        }
    }
}
