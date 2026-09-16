using UnityEngine;

namespace Project.Presentation.Motion
{
    /// <summary>
    /// Dynamic capsule offset 的 **per-instance 開關**（V2，2026-09-15）。
    ///
    /// <para><b>🔄 V2 把數值搬走了</b></para>
    /// V1 時這個 struct 裝著 5 個手感數字。V2 的參數是**從角色網格量出來的物理事實**
    /// （flesh radius）＋ 由它推導的上限與平滑，全部搬進 <see cref="CapsulePoseProfileSO"/>
    /// ——因為 X Bot 與 Y Bot 的網格不同（實測 Chest flesh radius 0.2038 vs 0.2902），
    /// 那是**角色資料**，不是每個 prefab 各自亂填的旋鈕。
    ///
    /// 這裡只留下「這一隻角色現在要不要開」——那是場景佈署決定，刻意**不**放進共用資產，
    /// 否則關掉玩家會連帶關掉所有人。
    /// </summary>
    [System.Serializable]
    public struct CapsuleOffsetSettings
    {
        [Tooltip("總開關（per-instance）。關閉時 center 永遠等於 authored 基準值。")]
        public bool Enabled;

        [Tooltip("CapsulePoseProfile（角色膠囊姿勢配置）。\n" +
                 "⚠️ 必填才會作用——沒有 profile 就沒有 flesh radius，沒有 flesh radius 就無從求解。\n" +
                 "⛔ 不同角色若網格不同，**不得共用同一份**（X Bot=Beta 網格、Y Bot=Alpha 網格，實測數值差 42%）。")]
        public CapsulePoseProfileSO Profile;

        /// <summary>行為與導入本機制之前完全相同的中性設定。</summary>
        public static CapsuleOffsetSettings Disabled => new CapsuleOffsetSettings
        {
            Enabled = false,
            Profile = null,
        };
    }

    /// <summary>
    /// 幾何夾持（collision-aware clamp）的**算術部分**。
    ///
    /// <para><b>🔄 V2（2026-09-15）：`SolveDesiredOffset` 已移除</b></para>
    /// 舊的 <c>MoveDirection × MaxOffset × speed</c> 模型被實測推翻，不再是正確方案：
    /// <list type="bullet">
    /// <item><b>方向錯</b>：純側移時身體實際往**反方向**傾（`docs/27` §11.3 ③）
    /// ⇒ 模型把膠囊推到錯的一側，製造出比它要修的問題**大一個量級**的誤差。</item>
    /// <item><b>量級錯</b>：前向跑步實際需要 0.20–0.30，舊模型最多給 0.12（`docs/27` §12.5）。</item>
    /// <item><b>前提錯</b>：移動方向不是姿勢的函數。姿勢是動畫自己的力學。</item>
    /// </list>
    /// 取而代之的是 <see cref="CapsulePoseSolver"/>——直接讀當幀姿勢。
    /// <c>MoveDirection</c> 只保留為 debug 對照（`MotionDriver` 的 gizmo 會把舊模型的目標畫成虛線圈）。
    ///
    /// 本類別因此只剩下夾持算術：**純函式、無 Unity 物件依賴** ⇒ EditMode 可完整驗證。
    /// </summary>
    public static class CapsuleOffsetSolver
    {
        /// <summary>
        /// 🆕（2026-09-15）把期望偏移**沿同一方向**縮到幾何體允許的距離。
        ///
        /// <para><b>為什麼是純函式，而 cast 留在呼叫端</b></para>
        /// `Physics.CapsuleCast` 需要場景與 Unity 物件；但「拿到 allowed 之後該怎麼縮」是**算術**。
        /// 把算術切出來 ⇒ 邊界條件（allowed ≥ requested／allowed = 0／退化輸入）
        /// 全部可以在 EditMode 確定性驗證，不需要蓋一面測試用的牆。
        ///
        /// <para><b>⚠️ 只縮長度，不轉方向</b></para>
        /// 沿牆滑動時把偏移「轉向」到切線方向是另一個設計（也更容易在轉角處抖動）；
        /// 本函式只回答「能走多遠」，方向仍然完全由 <see cref="CapsulePoseSolver"/> 決定
        /// ⇒ **幾何體不會變成偏移方向的第二個決定者。**
        /// </summary>
        /// <param name="desiredOffset">Root local 的期望偏移。</param>
        /// <param name="allowedDistance">幾何體允許的世界距離（cast 結果，≥ 0）。</param>
        /// <param name="requestedDistance">期望偏移對應的世界距離（&gt; 0）。</param>
        public static Vector3 ClampToAllowedDistance(
            Vector3 desiredOffset, float allowedDistance, float requestedDistance)
        {
            if (!IsFinite(desiredOffset) || !IsFinite(allowedDistance) || !IsFinite(requestedDistance))
                return Vector3.zero;

            // 沒有要移動就沒有東西要夾。
            if (requestedDistance <= 0f) return Vector3.zero;

            // 一點都不能動（cast 起點就已重疊）⇒ 維持在基準位置。
            if (allowedDistance <= 0f) return Vector3.zero;

            // 沒被擋住 ⇒ 原樣放行。⚠️ 不得在此「順便」放大。
            if (allowedDistance >= requestedDistance) return desiredOffset;

            return desiredOffset * (allowedDistance / requestedDistance);
        }

        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
