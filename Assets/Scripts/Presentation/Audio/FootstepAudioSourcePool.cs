using UnityEngine;

namespace Project.Presentation.Audio
{
    /// <summary>
    /// 🆕（Stage 2）極小的 <c>AudioSource</c> 池——解 M2 起就記錄在案的兩個侷限。
    ///
    /// ## 它解掉的是什麼
    /// <c>pitch</c> 與**位置**都是 `AudioSource` 的**狀態**，不是 `PlayOneShot` 的參數。
    /// 因此單一 Source 會有兩個問題：
    /// 1. 連續腳步時，後一發設定的 pitch 會**改到仍在播的前一發**（跑步步距 ~0.3 s、clip ~0.4 s ⇒ 必然重疊）；
    /// 2. 所有聲音只能從角色中心播，左右腳無法各自定位。
    ///
    /// 每次播放借一個**獨立的 Source**，兩個問題同時消失：各自的 pitch 互不干擾，
    /// 位置也可以分別設成左腳／右腳／角色中心。
    ///
    /// ## ⚠️ 這不是通用 Audio Framework
    /// 沒有優先權、沒有 virtualization、沒有 category routing、沒有 ducking。
    /// 它只做一件事：**給我一個現在沒在用的 AudioSource**。
    /// 池子滿了就搶用最早借出的那一個（腳步是高頻短音，搶掉最舊的一發是正確取捨——
    /// 讓新的一步沒有聲音比讓舊的一步播完更糟）。
    /// </summary>
    public sealed class FootstepAudioSourcePool
    {
        private readonly AudioSource[] _sources;
        private readonly float[] _busyUntil;
        private int _cursor;

        /// <summary>
        /// 依樣板建立池。樣板的 3D 設定（spatialBlend／rolloff／min-max distance／mixer group）會逐一複製，
        /// 因此**只要調樣板那一顆，整池跟著變**——不需要在 Inspector 維護 N 顆一樣的 Source。
        /// </summary>
        public FootstepAudioSourcePool(AudioSource template, Transform parent, int size)
        {
            size = Mathf.Max(1, size);
            _sources = new AudioSource[size];
            _busyUntil = new float[size];

            for (int i = 0; i < size; i++)
            {
                var go = new GameObject($"FootstepAudioSource_{i}");
                go.transform.SetParent(parent, false);
                var source = go.AddComponent<AudioSource>();
                CopySettings(template, source);
                source.playOnAwake = false;
                _sources[i] = source;
            }
        }

        /// <summary>
        /// 在指定世界位置以指定音高播一發。
        /// </summary>
        /// <param name="clip">要播的 clip。</param>
        /// <param name="worldPosition">播放位置（左腳／右腳／角色中心）。</param>
        /// <param name="volume">最終音量。</param>
        /// <param name="pitch">本發音高。⚠️ 因為每發用獨立 Source，這裡設的 pitch **不會**影響其他仍在播的聲音。</param>
        public void Play(AudioClip clip, Vector3 worldPosition, float volume, float pitch)
        {
            if (clip == null) return;

            int index = Rent(clip.length, pitch);
            AudioSource source = _sources[index];

            source.transform.position = worldPosition;
            source.pitch = pitch;
            source.clip = clip;
            source.volume = volume;
            source.Play();
        }

        /// <summary>找一個閒置的 Source；全忙就搶用最早借出的那一個。</summary>
        private int Rent(float clipLength, float pitch)
        {
            float now = Time.unscaledTime;

            // 先找已經播完的
            for (int i = 0; i < _sources.Length; i++)
            {
                int index = (_cursor + i) % _sources.Length;
                if (_busyUntil[index] > now) continue;
                _cursor = (index + 1) % _sources.Length;
                _busyUntil[index] = now + EstimateDuration(clipLength, pitch);
                return index;
            }

            // 全忙：搶最早會結束的那一個（＝最早借出的）
            int oldest = 0;
            for (int i = 1; i < _sources.Length; i++)
                if (_busyUntil[i] < _busyUntil[oldest]) oldest = i;

            _busyUntil[oldest] = now + EstimateDuration(clipLength, pitch);
            _cursor = (oldest + 1) % _sources.Length;
            return oldest;
        }

        // 以 pitch 反推實際播放時間（pitch 2.0 ⇒ 播一半時間）。只是「什麼時候可以再借」的估計，
        // 不需要精確——估短了頂多提早搶用，估長了頂多多借一顆。
        private static float EstimateDuration(float clipLength, float pitch)
        {
            float speed = Mathf.Max(0.01f, Mathf.Abs(pitch));
            return clipLength / speed;
        }

        private static void CopySettings(AudioSource from, AudioSource to)
        {
            if (from == null) return;
            to.outputAudioMixerGroup = from.outputAudioMixerGroup;
            to.spatialBlend = from.spatialBlend;
            to.rolloffMode = from.rolloffMode;
            to.minDistance = from.minDistance;
            to.maxDistance = from.maxDistance;
            to.dopplerLevel = from.dopplerLevel;
            to.spread = from.spread;
            to.priority = from.priority;
            to.bypassEffects = from.bypassEffects;
            to.bypassListenerEffects = from.bypassListenerEffects;
            to.bypassReverbZones = from.bypassReverbZones;
            to.reverbZoneMix = from.reverbZoneMix;
            if (from.rolloffMode == AudioRolloffMode.Custom)
                to.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                    from.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        }
    }
}
