using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 成功回数に応じて背景を切り替え、画面全体に縦横比を保って表示する。
/// </summary>
[DisallowMultipleComponent]
public sealed class BridgeJustSceneManager : MonoBehaviour
{
    [Tooltip("Dedicated background SpriteRenderer directly under Main Camera.")]
    [SerializeField] private SpriteRenderer backgroundRenderer;
    // 旧シーンの参照との互換性を保つため、以前の表示範囲の項目だけ残している。
    [SerializeField, HideInInspector] private SpriteRenderer screenArea;
    [SerializeField] private Sprite[] backgroundThemes = new Sprite[0];
    [SerializeField, Min(1)] private int stagesPerBackground = 5;
    [SerializeField, Min(0f)] private float fadeSeconds = 0.5f;
    [SerializeField] private bool loopBackgrounds = true;

    private GameManager owner;
    private CameraFollow cameraFollow;
    private SpriteRenderer fadeRenderer;
    private Camera view;
    private int backgroundLayer, backgroundOrder;
    private float depth;
    private bool prepared;
    private int pendingThemeIndex = -1;
    private Coroutine fadeRoutine;
    private Sprite originalSprite;
    private Color originalColor;
    private bool originalEnabled;
    private bool originalForceRenderingOff;
    private SpriteDrawMode originalDrawMode;
    private SpriteMaskInteraction originalMaskInteraction;
    private int originalSortingLayer;
    private int originalSortingOrder;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;

    // ステージ番号はGameManagerの成功回数から求め、ここでは別のカウンターを増やさない。
    public long CurrentStage => owner != null ? (long)Math.Max(0, owner.SuccessCount) + 1L : 1L;
    public int CurrentThemeIndex { get; private set; } = -1;
    public bool IsTransitioning { get; private set; }
    public int StagesPerBackground => stagesPerBackground;

    /// <summary>
    /// 背景切り替えの進捗と次の背景を取得する。
    /// フェード中は切り替え先をプレビューし、その区切りの進捗を満タンとして返す。
    /// </summary>
    public bool TryGetWorldProgress(out int completed, out int total, out Sprite next)
    {
        completed = 0; total = stagesPerBackground; next = null;
        if (!prepared || owner == null || total < 1 || CurrentThemeIndex < 0
            || backgroundThemes == null || backgroundThemes.Length < 2) return false;
        long successes = Math.Max(0, owner.SuccessCount);
        int desired = CalculateThemeIndex(successes + 1L, total, backgroundThemes.Length, loopBackgrounds);
        bool completing = desired != CurrentThemeIndex;
        int previewIndex = completing ? desired : CalculateThemeIndex((successes / total + 1L) * total + 1L,
            total, backgroundThemes.Length, loopBackgrounds);
        if (previewIndex == CurrentThemeIndex || !TryGetTheme(previewIndex, out next)) return false;
        completed = completing ? total : (int)(successes % total);
        return true;
    }

    /// <summary>
    /// ステージ番号から背景配列の添字を求める。設定が不正な場合は-1を返す。
    /// </summary>
    public static int CalculateThemeIndex(long stageNumber, int stagesPerTheme, int themeCount, bool loop)
    {
        if (stagesPerTheme <= 0 || themeCount <= 0) return -1;
        long group = (Math.Max(1L, stageNumber) - 1L) / stagesPerTheme;
        return loop ? (int)(group % themeCount) : (int)Math.Min(group, themeCount - 1L);
    }

    /// <summary>
    /// 背景の参照と描画設定を準備する。設定が不足している場合は背景切り替えを無効にする。
    /// </summary>
    public void Initialize(GameManager game, CameraFollow follow, params SpriteRenderer[] foreground)
    {
        owner = game;
        cameraFollow = follow;
        if (prepared) return;
        if (!isActiveAndEnabled || backgroundThemes == null || backgroundThemes.Length == 0)
        {
            LogBackgroundState("initialization skipped: disabled or no themes");
            return;
        }
        if (!ValidateConfiguration(foreground, out string error))
        {
            Debug.LogWarning("[BridgeJust] Background themes disabled: " + error, this);
            LogBackgroundState("initialization failed");
            return;
        }

        originalSprite = backgroundRenderer.sprite;
        originalColor = backgroundRenderer.color;
        originalEnabled = backgroundRenderer.enabled;
        originalForceRenderingOff = backgroundRenderer.forceRenderingOff;
        originalDrawMode = backgroundRenderer.drawMode;
        originalMaskInteraction = backgroundRenderer.maskInteraction;
        originalSortingLayer = backgroundRenderer.sortingLayerID;
        originalSortingOrder = backgroundRenderer.sortingOrder;
        originalLocalPosition = backgroundRenderer.transform.localPosition;
        originalLocalRotation = backgroundRenderer.transform.localRotation;
        originalLocalScale = backgroundRenderer.transform.localScale;

        fadeRenderer = new GameObject("Background fade (runtime)").AddComponent<SpriteRenderer>();
        fadeRenderer.transform.SetParent(cameraFollow.transform, false);
        fadeRenderer.gameObject.layer = backgroundRenderer.gameObject.layer;
        fadeRenderer.sharedMaterials = backgroundRenderer.sharedMaterials;
        view = cameraFollow.GetComponent<Camera>();
        depth = Mathf.Clamp(Vector3.Dot(backgroundRenderer.transform.position - view.transform.position, view.transform.forward),
            view.nearClipPlane + 0.05f, view.farClipPlane - 0.05f);
        backgroundRenderer.drawMode = SpriteDrawMode.Simple;
        backgroundRenderer.maskInteraction = SpriteMaskInteraction.None;
        backgroundRenderer.sortingLayerID = backgroundLayer;
        backgroundRenderer.sortingOrder = backgroundOrder;
        backgroundRenderer.forceRenderingOff = false;
        fadeRenderer.maskInteraction = SpriteMaskInteraction.None;
        fadeRenderer.sortingLayerID = backgroundLayer;
        fadeRenderer.sortingOrder = backgroundOrder + 1;
        fadeRenderer.enabled = false;
        prepared = true;
        UpdateLayout();
    }

    public void ResetForRun()
    {
        CancelTransition();
        CurrentThemeIndex = -1;
        if (!prepared) return;
        if (TryGetTheme(0, out Sprite first))
        {
            backgroundRenderer.sprite = first;
            backgroundRenderer.color = Color.white;
            backgroundRenderer.enabled = true;
            CurrentThemeIndex = 0;
            UpdateLayout();
        }
        else
        {
            // 背景Aが未設定でも、前のプレイの背景を残したままにしない。
            backgroundRenderer.sprite = originalSprite;
            backgroundRenderer.color = originalColor;
            backgroundRenderer.enabled = originalEnabled;
            UpdateLayout();
        }
        LogBackgroundState("run start / retry");
    }

    public void BeginSectionTransition(int successfulCrossings)
    {
        if (!prepared || !isActiveAndEnabled || owner == null
            || (owner.CurrentState != GameManager.GameState.Scrolling && owner.CurrentState != GameManager.GameState.Transition)
            || successfulCrossings != owner.SuccessCount) return;

        int index = CalculateThemeIndex((long)Math.Max(0, successfulCrossings) + 1L,
            stagesPerBackground, backgroundThemes == null ? 0 : backgroundThemes.Length, loopBackgrounds);
        if (index == CurrentThemeIndex || index == pendingThemeIndex || IsTransitioning
            || !TryGetTheme(index, out Sprite next)) return;
        if (backgroundRenderer.sprite == next)
        {
            CurrentThemeIndex = index;
            return;
        }
        if (!Finite(fadeSeconds) || fadeSeconds < 0f) return;
        if (fadeSeconds == 0f)
        {
            ApplyTheme(index, next);
            return;
        }

        pendingThemeIndex = index;
        IsTransitioning = true;
        fadeRoutine = StartCoroutine(FadeToTheme(index, next));
    }

    /// <summary>
    /// 区間開始時の背景へ戻す。ステージ番号と進捗はGameManagerの成功回数に従う。
    /// </summary>
    public void RestoreRoundTheme(int index)
    {
        CancelTransition();
        if (prepared && TryGetTheme(index, out Sprite theme)) ApplyTheme(index, theme);
    }

    private IEnumerator FadeToTheme(int index, Sprite next)
    {
        fadeRenderer.sprite = next;
        fadeRenderer.color = new Color(1f, 1f, 1f, 0f);
        fadeRenderer.enabled = true;
        UpdateLayout();
        float elapsed = 0f;
        float duration = fadeSeconds;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            fadeRenderer.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }
        ApplyTheme(index, next);
        fadeRenderer.enabled = false;
        fadeRenderer.sprite = null;
        IsTransitioning = false;
        pendingThemeIndex = -1;
        fadeRoutine = null;
    }

    private void ApplyTheme(int index, Sprite sprite)
    {
        backgroundRenderer.sprite = sprite;
        backgroundRenderer.color = Color.white;
        backgroundRenderer.enabled = true;
        CurrentThemeIndex = index;
        UpdateLayout();
        LogBackgroundState("theme changed");
    }

    private void LogBackgroundState(string reason)
    {
        string prefix = $"[BridgeJust] Background | {reason}, stage={CurrentStage}, index={CurrentThemeIndex}";
        if (backgroundRenderer == null)
        {
            Debug.LogWarning(prefix + ", Background Renderer is unassigned.", this);
            return;
        }

        Camera camera = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : null;
        bool cameraIncludesLayer = camera != null
            && (camera.cullingMask & (1 << backgroundRenderer.gameObject.layer)) != 0;
        Bounds bounds = backgroundRenderer.bounds;
        Debug.Log(prefix
            + $", sprite={backgroundRenderer.sprite?.name ?? "None"}"
            + $", rendererEnabled={backgroundRenderer.enabled}, objectActive={backgroundRenderer.gameObject.activeInHierarchy}"
            + $", alpha={backgroundRenderer.color.a:F3}, forceRenderingOff={backgroundRenderer.forceRenderingOff}"
            + $", sortingLayer={backgroundRenderer.sortingLayerName}({backgroundRenderer.sortingLayerID}), order={backgroundRenderer.sortingOrder}"
            + $", maskInteraction={backgroundRenderer.maskInteraction}, fullViewport=True"
            + $", cameraIncludesLayer={cameraIncludesLayer}, worldCenter={bounds.center}, worldSize={bounds.size}", this);
    }

    public void CancelTransition()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = null;
        IsTransitioning = false;
        pendingThemeIndex = -1;
        if (fadeRenderer != null)
        {
            fadeRenderer.enabled = false;
            fadeRenderer.sprite = null;
        }
    }

    private bool TryGetTheme(int index, out Sprite sprite)
    {
        sprite = backgroundThemes != null && index >= 0 && index < backgroundThemes.Length ? backgroundThemes[index] : null;
        return sprite != null && sprite.bounds.size.x > 0f && sprite.bounds.size.y > 0f;
    }

    private bool ValidateConfiguration(SpriteRenderer[] foreground, out string error)
    {
        error = null;
        if (owner == null || cameraFollow == null || backgroundRenderer == null
            || backgroundRenderer.transform.parent != cameraFollow.transform
            || backgroundRenderer.transform == owner.transform)
            error = "Assign a dedicated background SpriteRenderer directly under Main Camera.";
        else if (stagesPerBackground < 1 || !Finite(fadeSeconds) || fadeSeconds < 0f
            || !cameraFollow.GetComponent<Camera>().orthographic)
            error = "Use an orthographic camera, positive Stages Per Background and non-negative Fade Seconds.";
        else
        {
            SpriteRenderer back = null;
            foreach (SpriteRenderer item in foreground)
            {
                if (item == null) continue;
                if (item == backgroundRenderer) { error = "Do not assign a gameplay renderer as background."; return false; }
                if (back == null || SortingLayer.GetLayerValueFromID(item.sortingLayerID) < SortingLayer.GetLayerValueFromID(back.sortingLayerID)
                    || (item.sortingLayerID == back.sortingLayerID && item.sortingOrder < back.sortingOrder)) back = item;
            }
            if (back == null || back.sortingOrder < -32766) error = "No valid sorting order is available behind gameplay.";
            else { backgroundLayer = back.sortingLayerID; backgroundOrder = back.sortingOrder - 2; }
        }
        return error == null;
    }

    private void LateUpdate()
    {
        if (prepared) UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (!prepared || view == null) return;
        FitToViewport(backgroundRenderer);
        FitToViewport(fadeRenderer);
    }

    private void FitToViewport(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null) return;
        Vector3 imageSize = renderer.sprite.bounds.size;
        if (imageSize.x <= 0f || imageSize.y <= 0f) return;
        float height = view.orthographicSize * 2f;
        float scale = Mathf.Max(height * view.aspect / imageSize.x, height / imageSize.y);
        renderer.transform.rotation = view.transform.rotation;
        SetWorldScale(renderer.transform, new Vector3(scale, scale, 1f));
        Vector3 center = view.transform.position + view.transform.forward * depth;
        // 画像の中心を表示領域に合わせる。Pivotが中央以外でも画像のBoundsを基準にする。
        renderer.transform.position += center - renderer.bounds.center;
    }

    private static void SetWorldScale(Transform item, Vector3 worldScale)
    {
        Vector3 parent = item.parent.lossyScale;
        if (parent.x == 0f || parent.y == 0f || parent.z == 0f) return;
        item.localScale = new Vector3(worldScale.x / parent.x, worldScale.y / parent.y, worldScale.z / parent.z);
    }

    private void OnDisable() => CancelTransition();

    private void OnDestroy()
    {
        CancelTransition();
        if (prepared && backgroundRenderer != null)
        {
            backgroundRenderer.sprite = originalSprite;
            backgroundRenderer.color = originalColor;
            backgroundRenderer.enabled = originalEnabled;
            backgroundRenderer.forceRenderingOff = originalForceRenderingOff;
            backgroundRenderer.drawMode = originalDrawMode;
            backgroundRenderer.maskInteraction = originalMaskInteraction;
            backgroundRenderer.sortingLayerID = originalSortingLayer;
            backgroundRenderer.sortingOrder = originalSortingOrder;
            backgroundRenderer.transform.localPosition = originalLocalPosition;
            backgroundRenderer.transform.localRotation = originalLocalRotation;
            backgroundRenderer.transform.localScale = originalLocalScale;
        }
        if (fadeRenderer != null) Destroy(fadeRenderer.gameObject);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
