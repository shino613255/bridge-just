using UnityEngine;

/// <summary>
/// 効果音を管理する。音声が未設定の場合は、その音を再生しない。
/// </summary>
[DisallowMultipleComponent]
public sealed class AudioManager : MonoBehaviour
{
    [SerializeField] private AudioSource soundEffectSource;

    [Header("Clips - assign explicitly; no automatic substitutions")]
    [SerializeField] private AudioClip startRetryClip;
    [SerializeField] private AudioClip successClip;
    [SerializeField] private AudioClip perfectClip;
    [Tooltip("Continuous Perfect 1, 2, 3, 4... use these clips cyclically. Missing entries fall back to the retained Perfect Clip.")]
    [SerializeField] private AudioClip[] perfectComboClips = new AudioClip[0];
    [SerializeField] private AudioClip failClip;

    [Header("Volume (adjust here, including during Play)")]
    [SerializeField, Range(0f, 1f)] private float soundEffectVolume = 0.7f;

    private bool ready;

    private void Awake()
    {
        if (soundEffectSource == null)
        {
            Debug.LogError("[BridgeJust] AudioManager: Assign a dedicated Sound Effect Source. Gameplay can continue without audio.", this);
            enabled = false;
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
        int index = PerfectSequenceIndex(consecutivePerfects, perfectComboClips == null ? 0 : perfectComboClips.Length);
        return index >= 0 && perfectComboClips[index] != null ? perfectComboClips[index] : perfectClip;
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

    private static float SafeVolume(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
    }

    private bool CanPlay()
    {
        return ready && isActiveAndEnabled && soundEffectSource != null && soundEffectSource.isActiveAndEnabled;
    }

    private void WarnIfMissing(AudioClip clip, string description)
    {
        if (clip == null)
        {
            Debug.LogWarning($"[BridgeJust] AudioManager: {description} is unassigned; it will stay silent until assigned. No substitute is used.", this);
        }
    }

    private void OnDisable()
    {
        if (ready && soundEffectSource != null)
        {
            soundEffectSource.Stop();
        }
    }
}
