using UnityEngine;

namespace Project.Presentation.Motion
{
    /// <summary>
    /// 🆕（2026-09-17，正本 `docs/38`）**「這個位置還算不算可站」的純函式判定。**
    ///
    /// <para><b>問題：Collision support ≠ Valid standing support</b></para>
    /// Unity 的 <c>CharacterController</c> 會讓**膠囊側面**掛在平台角上
    /// ⇒ 實測 root 可超出邊緣 0.23–0.32 m（量級 ≈ radius 0.32）而仍然 <c>isGrounded</c>，
    /// 腳尖（距 root 0.114 m）因此懸空達 **0.434 m**，視覺上明顯不對。
    ///
    /// <para><b>作法：照抄 Unreal 的 perch 形狀，不動碰撞膠囊</b></para>
    /// <c>UCharacterMovementComponent::ComputePerchResult(GetValidPerchRadius(), …)</c> 的核心是：
    /// 碰撞 shape 維持原尺寸，但**用一個較小的半徑重跑一次 floor query**，
    /// 失敗就把地板判為不可站（UE 原碼：<c>OutFloorResult.bWalkableFloor = false</c>）。
    /// 問的是 **「如果我的腳只有這麼寬，還踩得到東西嗎？」**
    ///
    /// ⇒ radius 0.32 完整保留（它還負責牆面／肩膀／手臂的水平保護、traversal corridor、
    /// enemy spacing、Capsule Pose profile 的基準），本類別只影響 <c>IsGrounded</c>。
    ///
    /// <para><b>⚠️ 搜尋深度必須 ≥ step height，不能只用貼地容差</b></para>
    /// 實測踩過：深度取 0.05 時**斜坡 11/11 全部誤判**——膠囊停在 30° 斜面上時，
    /// 底部離「正下方的斜面」比離接觸點遠 <c>r(1/cos30 − 1) ≈ 0.05 m</c>，剛好搆不到。
    /// **「探不到地面」≠「沒有地面」。** UE 用
    /// <c>FloorSweepTraceDist = max(MAX_FLOOR_DIST, MaxStepHeight + adjust)</c> 正是為此。
    ///
    /// <para><b>⛔ 本類別不是 grounding authority</b></para>
    /// 它只回答一個布林。誰能寫 <c>IsGrounded</c> 完全沒有改變——
    /// 仍然只有 <see cref="MotionDriver"/>（由 `§7-A5` WriterRules 守住）。
    /// 形狀與 UE 一致：perch 是 <c>FindFloor</c> 的內部細節，下游只讀最終結果。
    /// </summary>
    public static class StandingSupportProbe
    {
        /// <summary>掃到的候選上限。腳下同時有這麼多層 collider 已屬異常，超過就取前 N 個。</summary>
        public const int BufferSize = 8;

        /// <summary>
        /// 腳下是否存在**有效支撐**。
        /// </summary>
        /// <param name="footWorldPosition">膠囊底部的世界座標（不是 root，也不是膠囊中心）。</param>
        /// <param name="up">角色的向上軸。</param>
        /// <param name="supportRadius">
        /// 有效支撐半徑。**必須小於碰撞半徑**才有意義（等於碰撞半徑 ⇒ 與 <c>isGrounded</c> 同義）。
        /// 對應 UE 的 <c>GetValidPerchRadius()</c>。
        /// </param>
        /// <param name="floorSearchDepth">
        /// 由腳底往下搜尋多深仍算「腳下有地」。⚠️ 見類別說明：**必須涵蓋 step height**，
        /// 否則斜坡與樓梯會大量誤判。
        /// </param>
        /// <param name="probeLift">起點抬高多少，避免 cast 起始就與地面重疊。</param>
        /// <param name="maxSlopeDegrees">超過此坡度的表面不算可站（對齊 <c>CharacterController.slopeLimit</c>）。</param>
        /// <param name="mask">查詢層遮罩。</param>
        /// <param name="selfRoot">角色自己的 root；其下的 collider 一律忽略。</param>
        /// <param name="buffer">呼叫端持有的固定容量緩衝（零配置）。</param>
        /// <param name="supportHit">命中的支撐面（僅在回傳 true 時有意義）。</param>
        public static bool HasValidStandingSupport(
            Vector3 footWorldPosition,
            Vector3 up,
            float supportRadius,
            float floorSearchDepth,
            float probeLift,
            float maxSlopeDegrees,
            LayerMask mask,
            Transform selfRoot,
            RaycastHit[] buffer,
            out RaycastHit supportHit)
        {
            supportHit = default;

            // ⭐ **無法查詢時一律不否決**（回傳 true ＝ 維持原本的 isGrounded）。
            //    這一層只有「否決」的權力，沒有「授予」的權力；設定缺失或參數退化時，
            //    正確的失敗方向是**什麼都不做**，而不是讓角色憑空掉下去。
            if (buffer == null || buffer.Length == 0) return true;
            if (supportRadius <= 0f || floorSearchDepth <= 0f) return true;

            // ⭐ 起點抬高 (supportRadius + probeLift)：球心在該高度時，球的**底部**正好在
            //    腳底上方 probeLift 處，於是向下走 probeLift 就接觸腳底平面。
            Vector3 origin = footWorldPosition + up * (supportRadius + probeLift);
            float distance = probeLift + floorSearchDepth;

            int count = Physics.SphereCastNonAlloc(
                origin, supportRadius, -up, buffer, distance, mask, QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Collider collider = buffer[i].collider;
                if (collider == null) continue;
                // 忽略自己。⚠️ 角色的 CharacterController 就在探測起點旁邊，不濾掉會自問自答。
                if (selfRoot != null && collider.transform.IsChildOf(selfRoot)) continue;

                // ⚠️ 起始即重疊時 Unity 會回報 normal 為零、distance 為 0；那不是有效的地面觀測，跳過。
                if (buffer[i].distance <= 0f && buffer[i].normal == Vector3.zero) continue;

                if (Vector3.Angle(buffer[i].normal, up) > maxSlopeDegrees) continue;

                if (buffer[i].distance >= nearest) continue;
                nearest = buffer[i].distance;
                supportHit = buffer[i];
                found = true;
            }

            return found;
        }
    }
}
