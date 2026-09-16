using UnityEngine;
using Project.Core.Effects;
using Project.Core.Survivability;

namespace Project.Presentation.Actions
{
    /// <summary>
    /// 最小 Throw projectile：直線飛行、命中提交 external Action request、逾時銷毀。
    /// 不認識 FSM、ActionDefinition 或 AnimationFacade。
    /// </summary>
    public sealed class ThrownProjectile : MonoBehaviour
    {
        // Slow 倍率的單一真相已移到 TemporaryGameplayEffectState（2026-09-05，見該處說明）——
        // Ice 改走地面 AoE 後多了第二個投遞者，同一個常數不再各留一份。
        private const float SlowMovementSpeedMultiplier =
            TemporaryGameplayEffectState.SlowMovementSpeedMultiplier;

        // ⚠️ `ThrownProjectile` 是**共用**的投射物元件——Throw、Quick Spell、Ice Spell 走同一支程式、
        //    只換 prefab。因此 Slow **必須由資產決定，不能寫死在程式裡**：
        //    `docs/11` §4 明列只有 Ice Spell 投遞 Slow，Throw 與 Quick Spell 命中不減速。
        //    預設 false ⇒ 既有 Throw prefab 不改一個欄位，行為與加 Slow 之前完全相同。
        [SerializeField] private bool appliesSlow;
        [SerializeField, Min(0.01f)] private float slowDuration = 3f;

        // 🆕（ADR-009 D2）命中傷害。與 slowDuration 同一種配置慣例：**數值在資產上，不在程式裡**。
        // 攻擊方只說「打多少」；要播受擊還是要死，由持有生命值的一方（CharacterHealth）決定。
        [SerializeField, Min(0f)] private float damage = 10f;

        // 🆕（2026-09-16）**掃掠的層遮罩。第一版刻意維持現行的廣義行為（`~0`）。**
        // 舊實作靠 trigger ＋ physics matrix 隱含決定打誰；改成 SphereCast 之後必須顯式給遮罩。
        // ⛔ 本輪**不**順手重構 physics layers（使用者明確裁決）——那是獨立的一件事。
        [SerializeField] private LayerMask sweepMask = ~0;

        // 🆕（2026-09-16，使用者裁決 B）**飛行視覺在生成時脫離投射物。**
        //
        // 🐞 為什麼非這樣不可：`Human_Spell_Fireball` 的整個設計前提是
        //    **「發射器不動、火球是一顆會飛的粒子」**——core 粒子以 startSpeed 15 自走，
        //    三個尾巴是掛在它身上的 [Birth] 子發射器、而且用 `rateOverDistance` 依**移動距離**出粒。
        //    我們的投射物卻是「GameObject 在飛」。兩種模型互相衝突：
        //    把 core 釘回發射器（§16）雖然讓視覺與 collider 對齊，卻同時抽掉了尾巴的空間展開
        //    **與**出粒依據 ⇒ 拖尾幾乎消失（使用者 2026-09-16 回報）。
        //
        // ⇒ 讓 VFX 回到它原本的模型：**生成時 unparent，留在發射點不動**，
        //    core 粒子照原設定自走；gameplay 投射物以**相同速度**平行飛。
        //    兩者同源、同速、同向 ⇒ 「視覺 core ≈ collider」的不變量仍然成立，
        //    而視覺**零妥協**（不必猜任何 lifetime 換算值）。
        [Tooltip("飛行視覺的根。留空 ⇒ 自動解析為第一個帶 ParticleSystem 的子物件。")]
        [SerializeField] private Transform visualRoot;

        [Tooltip("脫離後的視覺存活多久才回收。需涵蓋 core 壽命 ＋ 最長的尾巴壽命。")]
        [SerializeField, Min(0.1f)] private float detachedVisualLifetime = 4f;

        [Tooltip("命中時在 hit.point 生成的一次性效果。留空 ⇒ 不生成（現況）。")]
        [SerializeField] private GameObject impactEffectPrefab;
        [SerializeField, Min(0.01f)] private float impactEffectLifetime = 2f;

        /// <summary>固定容量，執行期不配置；掃掠一次最多檢視這麼多命中。</summary>
        private const int SweepBufferSize = 16;
        private readonly RaycastHit[] _sweepBuffer = new RaycastHit[SweepBufferSize];
        private SphereCollider _sphere;
        private GameObject _detachedVisual;

        private float _speed;
        private float _remainingLifetime;
        private Transform _ownerRoot;
        private bool _completed;

        public void Initialize(float speed, float lifetime, Transform ownerRoot)
        {
            _speed = Mathf.Max(0f, speed);
            _remainingLifetime = Mathf.Max(0.01f, lifetime);
            _ownerRoot = ownerRoot;
            _completed = false;
            if (_sphere == null) _sphere = GetComponentInChildren<SphereCollider>();
            DetachVisual();
        }

        /// <summary>
        /// 把飛行視覺從投射物上卸下，留在**發射當下的世界位姿**。
        /// ⚠️ 必須在位姿設定**之後**呼叫——`ThrowProjectileEmitter` 先 `Instantiate(prefab, position, rotation)`
        /// 再 `Initialize`，所以這裡讀到的已是正確的發射位姿。
        /// </summary>
        private void DetachVisual()
        {
            if (_detachedVisual != null) return;

            Transform visual = visualRoot != null ? visualRoot : ResolveVisualChild();
            if (visual == null || visual == transform) return;

            visual.SetParent(null, worldPositionStays: true);
            _detachedVisual = visual.gameObject;

            // 視覺自己播完就回收；投射物命中與否都不影響它把尾巴播完。
            if (Application.isPlaying)
                Destroy(_detachedVisual, Mathf.Max(0.1f, detachedVisualLifetime));
        }

        /// <summary>欄位留空時的補洞：第一個帶 ParticleSystem 的子物件。</summary>
        private Transform ResolveVisualChild()
        {
            var system = GetComponentInChildren<ParticleSystem>(true);
            if (system == null) return null;

            Transform candidate = system.transform;
            while (candidate.parent != null && candidate.parent != transform)
                candidate = candidate.parent;
            return candidate.parent == transform ? candidate : null;
        }

        /// <summary>
        /// 命中時熄掉視覺：**清掉 core 粒子**（火球在接觸點消失），但**讓已經生出來的尾巴自然淡出**。
        /// ⛔ 不整個 Destroy——那會把尾巴一刀切掉，比穿模還醜。
        /// core 的身分用結構判準（帶 SubEmitter 的那一個），與 `ProjectileVisualAlignmentPlayModeTests` 一致。
        /// </summary>
        private void ExtinguishVisual()
        {
            if (_detachedVisual == null) return;

            foreach (ParticleSystem system in _detachedVisual.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.subEmitters.subEmittersCount > 0) system.Clear(false);
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// 🔄 **2026-09-16：位移與命中判定改由 `FixedUpdate` 的 SphereCast 掃掠獨佔**（使用者裁決）。
        ///
        /// <para><b>舊實作為什麼不安全</b></para>
        /// 舊版在 `Update` 直接搬 `transform`、命中靠之後的 `OnTriggerEnter`。
        /// 那**不是連續碰撞**：PhysX 只在物理步同步一次姿態，因此它真正「看過」的只有一串離散取樣點。
        /// 位移用 `Time.deltaTime`（可變、逐帧）、取樣用物理步，**兩者不同步**
        /// ⇒ 「單幀距離小於半徑」只能**降低**略過的機率，不是保證。
        /// 火球視覺速度回到 20 m/s 之後每步位移 0.40 m，擦邊命中窗可小到 0.27 m ⇒ **必然開始漏**。
        ///
        /// <para><b>新契約</b></para>
        /// 本 tick 由 `start` 掃到 `desiredEnd`；**找到第一個合法命中就把本 tick 的位移截斷到
        /// `hit.distance`**，root 移到 `start + dir * hit.distance`，impact 用 `hit.point`，
        /// **之後**才結算並完成。
        /// ⛔ **不得先移到 desiredEnd 再回頭處理命中**——那會讓視覺與 impact 位置穿到目標裡面。
        /// </summary>
        private void FixedUpdate()
        {
            if (_completed) return;

            float deltaTime = Time.fixedDeltaTime;
            if (deltaTime <= 0f) return;

#if UNITY_EDITOR
            RecordDebugSample();
#endif
            Vector3 start = transform.position;
            Vector3 direction = transform.forward;
            float desiredDistance = _speed * deltaTime;

            if (desiredDistance > 0f &&
                TryFindFirstValidHit(start, direction, desiredDistance, out RaycastHit hit))
            {
                // ① 先截斷位移 —— root 停在接觸點，不越過目標。
                transform.position = start + direction * hit.distance;
                // ② impact 用真正的接觸點（不是 root，也不是 desiredEnd）。
                SpawnImpactEffect(hit.point);
                // ③ 最後才結算。
                ResolveImpact(hit.collider);
                return;
            }

            transform.position = start + direction * desiredDistance;
            _remainingLifetime -= deltaTime;
            if (_remainingLifetime <= 0f) Complete();
        }

        /// <summary>
        /// 掃掠並取**最近的合法命中**。合法性三條：
        /// ①不是自己 ②不是 owner 或 owner 的子物件 ③不是屍體（屍體不擋子彈，見 <see cref="TryRequestHit"/>）。
        /// </summary>
        private bool TryFindFirstValidHit(
            Vector3 start, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;
            float radius = ResolveSweepRadius();
            if (radius <= 0f) return false;

            int count = Physics.SphereCastNonAlloc(
                start, radius, direction, _sweepBuffer, distance, sweepMask,
                QueryTriggerInteraction.Collide);

            bool found = false;
            float nearest = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider collider = _sweepBuffer[i].collider;
                if (collider == null) continue;
                if (collider.transform.IsChildOf(transform)) continue;
                if (_ownerRoot != null && collider.transform.IsChildOf(_ownerRoot)) continue;

                // 屍體是透明的：讓掃掠在同一個 tick 內繼續找後面的活目標。
                CharacterHealth health = collider.GetComponentInParent<CharacterHealth>();
                if (health != null && health.IsDead) continue;

                if (_sweepBuffer[i].distance >= nearest) continue;
                nearest = _sweepBuffer[i].distance;
                hit = _sweepBuffer[i];
                found = true;
            }

            return found;
        }

        private float ResolveSweepRadius()
        {
            if (_sphere == null) _sphere = GetComponentInChildren<SphereCollider>();
            if (_sphere == null) return 0f;
            Vector3 scale = _sphere.transform.lossyScale;
            return _sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        }

        private void SpawnImpactEffect(Vector3 point)
        {
            if (impactEffectPrefab == null) return;
            GameObject instance = Instantiate(impactEffectPrefab, point, transform.rotation);
            if (Application.isPlaying) Destroy(instance, Mathf.Max(0.01f, impactEffectLifetime));
        }

        /// <summary>命中已由掃掠裁決；這裡只負責把效果投遞出去並收尾。</summary>
        private void ResolveImpact(Collider collider)
        {
            CharacterHealth health = collider.GetComponentInParent<CharacterHealth>();
            var effectState = collider.GetComponentInParent<TemporaryGameplayEffectState>();

            if ((health != null || effectState != null) && TryRequestHit(health, effectState)) return;

            // 打到世界（牆、地面）或目標不接受結算 ⇒ 仍然在接觸點結束。
            Complete();
        }

        /// <summary>供確定性測試與掃掠共用；成功後同一 projectile 不會再次提交。</summary>
        public bool TryRequestHit(CharacterHealth health, TemporaryGameplayEffectState effectState = null)
        {
            if (_completed || (health == null && effectState == null)) return false;

            // 🆕（2026-09-15）**屍體不擋子彈。**
            // 舊版對已死目標照樣 `_completed = true` ＋ `Destroy`，即使 `ApplyDamage` 回 false
            // ⇒ 投射物被屍體吃掉，站在屍體後面的活敵人打不到。
            // 掃掠端會把屍體視為**透明**（同一個 tick 內繼續往後找活目標）；
            // 這裡再守一次，確保任何呼叫路徑都不會消耗掉自己。
            // ⚠️ 連帶不施加 Slow——減速一具屍體沒有意義，而且那會讓「命中」語意分岔成兩半。
            if (health != null && health.IsDead) return false;

            _completed = true;
            // 🆕（ADR-009 D2）投射物只送傷害。**不再自己呼叫 RequestAction(Reaction)**——
            // 它不知道這一下會不會致死，讓它決定後果會在致死幀產生「受擊先播、死亡再蓋」的競爭。
            // 未致死時的 Reaction 由 CharacterHealth 沿用同一條既有鏈路發出。
            if (health != null) health.ApplyDamage(damage);

            // Projectile 只投遞「Effect.Slow／0.3 倍／有限時長」，完全不認識目標的移動系統。
            // `appliesSlow` 由 prefab 決定 ⇒ 只有 Ice Spell 的那顆會減速（docs/11 §4）。
            if (appliesSlow && effectState != null)
                effectState.ApplySlow(SlowMovementSpeedMultiplier, slowDuration);
            ExtinguishVisual();
            if (Application.isPlaying) Destroy(gameObject);
            return true;
        }

        private void Complete()
        {
            if (_completed) return;
            _completed = true;
            ExtinguishVisual();
            Destroy(gameObject);
        }

#if UNITY_EDITOR
        // ⚠️ **全段 Editor-only 診斷，不參與任何 gameplay 決策。**
        //
        // 它回答的問題（2026-09-15 使用者裁決的根因）：
        // 本元件在 **Update** 直接搬 `transform`，命中卻只靠之後的 `OnTriggerEnter`。
        // 那**不是連續碰撞**——PhysX 只在物理步同步一次 transform，因此它實際「看過」的
        // 只有一串**離散取樣點**。取樣點之間的空隙有多大、目標落在哪兩點之間，
        // 在畫面上完全看不出來——**這組 gizmo 就是把那個空隙畫出來**。
        //
        // 📌 `speed × fixedDeltaTime ≤ radius` 只是**降低**略過的機率，不是保證：
        //    位移用的是 `Time.deltaTime`（可變、逐帧），取樣用的是物理步，兩者不同步。

        private const int DebugSampleCapacity = 96;

        [Header("Debug（Editor-only）")]
        [Tooltip("畫出實際 collider 位置，以及物理步真正取樣到的離散位置。")]
        [SerializeField] private bool drawHitDebug = true;

        // 預先配置，執行期不再配置（即使是 Editor-only 也不破壞零 GC 慣例）。
        private readonly Vector3[] _physicsSamples = new Vector3[DebugSampleCapacity];
        private int _physicsSampleCount;

        /// <summary>
        /// 記錄每個物理步的掃掠起點。改成掃掠之後這串點**不再是「唯一被測過的位置」**——
        /// 相鄰兩點之間現在由 SphereCast 連續覆蓋。保留它是為了**看得到彈道與截斷點**。
        /// </summary>
        private void RecordDebugSample()
        {
            if (!drawHitDebug || _completed) return;
            if (_physicsSampleCount >= DebugSampleCapacity) return;
            _physicsSamples[_physicsSampleCount++] = transform.position;
        }

        private void OnDrawGizmos()
        {
            if (!drawHitDebug) return;

            var sphere = GetComponentInChildren<SphereCollider>();
            float radius = 0f;
            if (sphere != null)
            {
                Vector3 scale = sphere.transform.lossyScale;
                radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));

                // 🟡 目前這一帧的**實際 collider**（不是網格、不是 VFX）。
                Gizmos.color = new Color(1f, 0.92f, 0.2f, 0.9f);
                Gizmos.DrawWireSphere(sphere.transform.TransformPoint(sphere.center), radius);
            }

            // 🔴 物理**真正看過**的離散位置。相鄰兩球之間是完全沒有被測試過的空隙——
            //    目標若整個落在空隙裡，就會發生「視覺上穿過去了卻沒有命中」。
            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.8f);
            for (int i = 0; i < _physicsSampleCount; i++)
            {
                if (radius > 0f) Gizmos.DrawWireSphere(_physicsSamples[i], radius);
                if (i > 0) Gizmos.DrawLine(_physicsSamples[i - 1], _physicsSamples[i]);
            }

            // 📊 **把「看起來很快」換成數字。**
            // `measuredStep` 是實測的相鄰取樣距離，不是 `speed × fixedDeltaTime` 算出來的理論值——
            // 這兩者會不一致，正是因為位移在 Update（`Time.deltaTime`）、取樣在物理步。
            float measuredStep = _physicsSampleCount >= 2
                ? Vector3.Distance(
                    _physicsSamples[_physicsSampleCount - 1], _physicsSamples[_physicsSampleCount - 2])
                : 0f;
            // 🔄 掃掠上線後，step 大小**不再是安全判準**——連續覆蓋由 SphereCast 保證。
            //    這裡改成純資訊：step 仍是有用的彈道尺度參考。
            bool canTunnel = false;

            UnityEditor.Handles.color = canTunnel
                ? new Color(1f, 0.25f, 0.2f, 1f)
                : new Color(0.2f, 1f, 0.4f, 1f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.45f,
                $"speed {_speed:0.##} m/s\n"
                + $"measured step {measuredStep:0.###} m  (r={radius:0.###}, d={radius * 2f:0.###})\n"
                + $"samples {_physicsSampleCount}\n"
                + "sweep: SphereCast 連續覆蓋（step 僅供參考，不再是安全判準）");
        }
#endif
    }
}
