using UnityEngine;

namespace Project.Core.Actions
{
    /// <summary>
    /// Action 段落邊界取得的世界方向承諾。純資料快照，不查詢相機、物理或任何 Unity 物件。
    /// </summary>
    public readonly struct ActionReleaseContext
    {
        private const float DirectionSqrEpsilon = 0.000001f;

        /// <summary>段落邊界取得的世界瞄準點，保留高度。</summary>
        public Vector3 AimPoint { get; }

        /// <summary>由角色朝向基準指向 <see cref="AimPoint"/> 的正規化世界方向，保留高度。</summary>
        public Vector3 Direction { get; }

        /// <summary>是否成功取得方向承諾；false 時其他欄位皆為預設值。</summary>
        public bool HasAim { get; }

        public ActionReleaseContext(Vector3 aimPoint, Vector3 direction)
        {
            AimPoint = aimPoint;
            Direction = direction.sqrMagnitude > DirectionSqrEpsilon
                ? direction.normalized
                : Vector3.zero;
            HasAim = true;
        }
    }
}
