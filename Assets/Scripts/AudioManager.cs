using UnityEngine;

/// <summary>
/// 効果音とBGMを管理する。音声が未設定の場合は、その音を再生しない。
/// </summary>
[DisallowMultipleComponent]
public sealed class AudioManager : MonoBehaviour
{
    [SerializeField] private AudioSource soundEffectSource;
    [Tooltip("BGM専用のAudioSource。未設定なら同じGameObjectに自動で追加する（効果音用とは別）。")]
    [SerializeField] private AudioSource bgmSource;

    [Header("BGM")]
    [Tooltip("ゲーム中にループ再生するBGM。未設定ならBGMは鳴らない。")]
    [SerializeField] private AudioClip bgmClip;
    [SerializeField] private bool playBgmOnStart = true;

    [Header("効果音（未設定の音は鳴らない。代わりの音は使わない）")]
    [SerializeField] private AudioClip startRetryClip;
    [SerializeField] private AudioClip successClip;
    [SerializeField] private AudioClip perfectClip;
    [Tooltip("Perfectが1回目、2回目、3回目…と続いたときに順番に使う音。最後まで行ったら最初に戻る。空の所はPerfect Clipを使う。")]
    [SerializeField] private AudioClip[] perfectComboClips = new AudioClip[0];
    [SerializeField] private AudioClip failClip;

    [Header("音量（プレイ中も変更できる）")]
    [SerializeField, Range(0f, 1f)] private float soundEffectVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.4f;

    private bool ready;
    private bool bgmReady;
    private bool started;

    private void Awake()
    {
        SetUpBgmSource();

        if (soundEffectSource == null)
        {
            Debug.LogError("[BridgeJust] AudioManager: Assign a dedicated Sound Effect Source. "
                + "Gameplay can continue without sound effects.", this);
            return;
        }

        if (soundEffectSource == bgmSource)
        {
            Debug.LogError("[BridgeJust] AudioManager: Sound Effect Source and BGM Source "
                + "must be different AudioSources. Sound effects are disabled.", this);
            return;
        }

        soundEffectSource.Stop();
        soundEffectSource.playOnAwake = false;
        soundEffectSource.loop = false;
        soundEffectSource.spatialBlend = 0f;
        ready = true;
        ApplyEffectVolume();

        WarnIfMissing(startRetryClip, "Start Retry Clip (selected start/retry sound)");
        WarnIfMissing(successClip, "Success Clip");
        WarnIfMissing(perfectClip, "Perfect Clip: impactBell_heavy_003.ogg");
        WarnIfMissing(failClip, "Fail Clip: rollover6.ogg");
    }

    private void SetUpBgmSource()
    {
        if (bgmSource == null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
        }

        bgmSource.Stop();
        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
        bgmSource.spatialBlend = 0f;
        bgmReady = true;
        ApplyBgmVolume();

        WarnIfMissing(bgmClip, "BGM Clip");
    }

    private void Start()
    {
        started = true;
        if (playBgmOnStart)
        {
            PlayBgm();
        }
    }

    private void OnEnable()
    {
        // 一度無効になってから戻ったときにBGMを再開する。初回はStartで鳴らす。
        if (started && playBgmOnStart)
        {
            PlayBgm();
        }
    }

    /// <summary>設定済みのBGMをループ再生する。すでに再生中なら最初からやり直さない。</summary>
    public void PlayBgm()
    {
        if (!CanPlayBgm() || bgmClip == null)
        {
            return;
        }

        if (bgmSource.clip == bgmClip && bgmSource.isPlaying)
        {
            return;
        }

        bgmSource.clip = bgmClip;
        ApplyBgmVolume();
        bgmSource.Play();
    }

    public void StopBgm()
    {
        if (bgmReady && bgmSource != null)
        {
            bgmSource.Stop();
        }
    }

    public void PlayStartSound()
    {
        if (!CanPlay())
        {
            return;
        }

        // 失敗音の余韻を止めてから開始音を鳴らし、Retry時の重複再生を防ぐ。
        soundEffectSource.Stop();
        PlayEffect(startRetryClip);
    }

    public static int PerfectSequenceIndex(int consecutivePerfects, int clipCount)
        => clipCount > 0 ? (Mathf.Max(1, consecutivePerfects) - 1) % clipCount : -1;

    public AudioClip PerfectClipFor(int consecutivePerfects)
    {
        int index = PerfectSequenceIndex(consecutivePerfects,
            perfectComboClips == null ? 0 : perfectComboClips.Length);
        return index >= 0 && perfectComboClips[index] != null
            ? perfectComboClips[index] : perfectClip;
    }

    public void PlayResult(GameManager.StopResult result, int consecutivePerfects = 1)
    {
        if (!CanPlay())
        {
            return;
        }

        // Perfectでは専用音だけを鳴らし、通常成功音と重ねない。
        switch (result)
        {
            case GameManager.StopResult.Perfect:
                PlayEffect(PerfectClipFor(consecutivePerfects));
                break;
            case GameManager.StopResult.Success:
                PlayEffect(successClip);
                break;
            case GameManager.StopResult.Fail:
                PlayEffect(failClip);
                break;
        }
    }

    private void Update()
    {
        if (CanPlay())
        {
            ApplyEffectVolume();
        }

        if (CanPlayBgm())
        {
            ApplyBgmVolume();
        }
    }

    private void PlayEffect(AudioClip clip)
    {
        if (clip != null)
        {
            ApplyEffectVolume();
            soundEffectSource.PlayOneShot(clip);
        }
    }

    private void ApplyEffectVolume()
    {
        soundEffectSource.volume = SafeVolume(soundEffectVolume);
    }

    private void ApplyBgmVolume()
    {
        bgmSource.volume = SafeVolume(bgmVolume);
    }

    private static float SafeVolume(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
    }

    private bool CanPlay()
    {
        return ready && isActiveAndEnabled
            && soundEffectSource != null && soundEffectSource.isActiveAndEnabled;
    }

    private bool CanPlayBgm()
    {
        return bgmReady && isActiveAndEnabled && bgmSource != null && bgmSource.isActiveAndEnabled;
    }

    private void WarnIfMissing(AudioClip clip, string description)
    {
        if (clip == null)
        {
            Debug.LogWarning($"[BridgeJust] AudioManager: {description} is unassigned; "
                + "it will stay silent until assigned. No substitute is used.", this);
        }
    }

    private void OnDisable()
    {
        if (ready && soundEffectSource != null)
        {
            soundEffectSource.Stop();
        }

        StopBgm();
    }
}
