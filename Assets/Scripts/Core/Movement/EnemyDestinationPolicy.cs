using UnityEngine;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.2）行為／目的地選擇需要的**唯讀**導航查詢。
    /// 讓它們不直接碰 <c>UnityEngine.AI</c>——EditMode 沒有 NavMesh，決策邏輯仍要能完整測試
    /// （比照既有「呼叫端問 NavMesh、純函數做決定」的切法）。正式實作是 <see cref="EnemyNavigator"/>。
    /// </summary>
    internal interface IEnemyNavigationQuery
    {
        /// <summary>離 <paramref name="point"/> 最近、在 <paramref name="maxDistance"/> 內的可走點。</summary>
        bool TrySampleWalkablePoint(Vector3 point, float maxDistance, out Vector3 walkable);

        /// <summary>從 <paramref name="from"/> 到 <paramref name="to"/> 是否有**完整**路徑（不是部分路徑）。</summary>
        bool IsPathComplete(Vector3 from, Vector3 to);
    }

    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.4，P2）**遊走點挑選。** 在 Roam 半徑內隨機取點，
    /// 必須可取樣、仍在半徑內、離目前位置夠遠、而且路徑完整——「選得到的點一定走得到」。
    /// 亂數來源在建構時配置一次（零 GC）；種子可注入，測試可重現。
    /// </summary>
    internal sealed class RoamPointPicker
    {
        internal const float SampleRadius = 1.5f;

        private readonly System.Random _random;

        internal RoamPointPicker(int seed)
        {
            _random = new System.Random(seed);
        }

        internal float NextRange(float min, float max)
        {
            return min + (float)_random.NextDouble() * Mathf.Max(0f, max - min);
        }

        internal bool TryPick(
            Vector3 home,
            float radius,
            Vector3 self,
            float minimumDistance,
            int attempts,
            IEnemyNavigationQuery query,
            out Vector3 point)
        {
            point = default;
            if (query == null) return false;

            for (int i = 0; i < attempts; i++)
            {
                double angle = _random.NextDouble() * Mathf.PI * 2.0;
                float distance = radius * Mathf.Sqrt((float)_random.NextDouble());
                Vector3 candidate = home + new Vector3(
                    Mathf.Cos((float)angle) * distance, 0f, Mathf.Sin((float)angle) * distance);

                if (!query.TrySampleWalkablePoint(candidate, SampleRadius, out Vector3 walkable)) continue;
                if (HorizontalDistance(walkable, home) > radius) continue;
                if (HorizontalDistance(walkable, self) < minimumDistance) continue;
                if (!query.IsPathComplete(self, walkable)) continue;

                point = walkable;
                return true;
            }
            return false;
        }

        internal static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.magnitude;
        }
    }

    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.4，P3）**巡邏點游標**（純函數）。循環或往返；交戰回來從最近點接續。
    /// </summary>
    internal static class WaypointCursor
    {
        /// <summary>下一個索引。往返模式在兩端反向；只有一個點時停在原地。</summary>
        internal static int Advance(int index, int direction, int count, WaypointTraversal traversal, out int nextDirection)
        {
            nextDirection = direction >= 0 ? 1 : -1;
            if (count <= 1) return 0;

            if (traversal == WaypointTraversal.Loop)
            {
                nextDirection = 1;
                return (index + 1) % count;
            }

            int next = index + nextDirection;
            if (next >= count || next < 0)
            {
                nextDirection = -nextDirection;
                next = index + nextDirection;
            }
            return Mathf.Clamp(next, 0, count - 1);
        }

        /// <summary>離 <paramref name="self"/> 水平距離最近的有效 waypoint；沒有則回 -1。</summary>
        internal static int Nearest(EnemyTerritory territory, Vector3 self)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            int count = territory != null ? territory.WaypointCount : 0;
            for (int i = 0; i < count; i++)
            {
                if (!territory.TryGetWaypoint(i, out Vector3 waypoint)) continue;
                float distance = RoamPointPicker.HorizontalDistance(waypoint, self);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }
    }
}
