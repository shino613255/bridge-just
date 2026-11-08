using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// SCORE・BEST・成功演出・GAME OVERとRetryボタンを表示する。
/// 数値や結果はGameManagerから受け取り、ここでは得点や判定を計算しない。
/// 文字の実装は通常Text・TextMeshPro・RainbowLabelの違いをSetLabelで吸収する。
/// </summary>
[DisallowMultipleComponent]
public sealed class UIManager : MonoBehaviour
{
    [Tooltip("Drag the Text or TextMeshPro child, not its Canvas.")]
    [SerializeField] private Graphic scoreText;
    [SerializeField] private Graphic bestText;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Graphic gameOverScoreText;
    [SerializeField] private Button retryButton;
    [Tooltip("Retry the current round. Keep this inside the same Game Over Panel as Retry Button.")]
    [SerializeField] private Button retryStageButton;
    [SerializeField] private Graphic perfectText;
    [SerializeField, Min(0f)] private float perfectDisplaySeconds = 0.6f;

    [Header("Combo feedback (optional separate Text or TMP label)")]
    [Tooltip("Use a separate Japanese-capable label outside Game Over Panel. Font size is set on that label.")]
    [SerializeField] private Graphic comboText;
    [Tooltip("Optional shared parent for the separate PERFECT and COMBO rows. Animates both together.")]
    [SerializeField] private RectTransform feedbackRoot;
    [SerializeField, Range(0.8f, 1f)] private float comboDisplaySeconds = 0.9f;
    [SerializeField, Min(0.01f)] private float comboPopSeconds = 0.28f;
    [SerializeField, Min(1f)] private float successPopScale = 1.12f;
    [SerializeField, Min(1f)] private float perfectPopScale = 1.25f;
    [SerializeField] private Color successComboColor = new Color(1f, 0.78f, 0.12f, 1f);
    [SerializeField] private Color perfectComboColor = new Color(1f, 0.52f, 0.05f, 1f);

    // ボタンの通知先をここで固定せず、イベントを受け取ったGameManagerが再挑戦の処理を行う。
    public event Action RetryRequested;
    public event Action RetryStageRequested;
    public bool ComboFeedbackAvailable => comboText != null;
    private float perfectTimeRemaining;
    private float comboElapsed;
    private bool comboVisible;
    private bool showingPerfectCombo;
    private bool comboScaleCaptured;
    private Vector3 comboBaseScale;
    private Color comboDisplayColor;

    // ボタンに通知用の処理を登録し、コンボ演出で戻る基準Scaleを保存する。
    private void Awake()
    {
        if (comboText != null)
        {
            comboBaseScale = AnimationTarget.localScale;
            comboScaleCaptured = true;
            comboText.raycastTarget = false;
        }

        if (retryButton != null)
        {
            retryButton.onClick.AddListener(OnRetryClicked);
        }
        if (retryStageButton != null) retryStageButton.onClick.AddListener(OnRetryStageClicked);
    }

    // 必要な表示先・ボタン・Canvasの入力設定を検査する。パネルを隠してUIManager自体が止まらない構成も確認。
    public bool ValidateSetup(out string error)
    {
        error = null;
        if (!isActiveAndEnabled || !IsLabel(scoreText) || gameOverPanel == null || retryButton == null)
        {
            error = "UIManager needs an active component, Score Text, Game Over Panel and Retry Button. Labels must be Legacy Text or TMP_Text.";
        }
        else if ((bestText != null && !IsLabel(bestText))
            || (gameOverScoreText != null && !IsLabel(gameOverScoreText))
            || (perfectText != null && !IsLabel(perfectText)))
        {
            error = "UIManager optional labels must also be Legacy Text or TMP_Text.";
        }
        else if (transform.IsChildOf(gameOverPanel.transform))
        {
            error = "Attach UIManager outside Game Over Panel so hiding the panel does not disable UIManager.";
        }
        else if (!retryButton.transform.IsChildOf(gameOverPanel.transform))
        {
            error = "Place Retry Button inside Game Over Panel.";
        }
        else if (!retryButton.enabled || !retryButton.interactable
            || retryButton.GetComponentInParent<GraphicRaycaster>(true) == null)
        {
            error = "Retry Button must be enabled, interactable, and inside a Canvas with GraphicRaycaster.";
        }
        else if (EventSystem.current != null
            && EventSystem.current.transform.IsChildOf(gameOverPanel.transform))
        {
            error = "Keep EventSystem outside Game Over Panel so it stays active during play.";
        }
        else if (retryStageButton != null && (retryStageButton == retryButton
            || !retryStageButton.transform.IsChildOf(gameOverPanel.transform)
            || !retryStageButton.enabled || !retryStageButton.interactable
            || retryStageButton.GetComponentInParent<GraphicRaycaster>(true) == null))
        {
            error = "Retry Stage Button must be a separate, enabled button inside Game Over Panel's raycast Canvas.";
        }
        else if (perfectText != null && (perfectText == scoreText || perfectText == bestText
            || perfectText == gameOverScoreText || transform.IsChildOf(perfectText.transform)))
        {
            error = "Perfect Text must be a separate label outside the UIManager object's ancestors.";
        }
        else if (float.IsNaN(perfectDisplaySeconds) || float.IsInfinity(perfectDisplaySeconds)
            || perfectDisplaySeconds < 0f)
        {
            error = "Perfect Display Seconds must be finite and non-negative.";
        }
        else if (comboText != null && (!IsLabel(comboText) || comboText == scoreText
            || comboText == bestText || comboText == perfectText || comboText == gameOverScoreText
            || comboText.transform.IsChildOf(gameOverPanel.transform)
            || transform.IsChildOf(comboText.transform)
            || retryButton.transform.IsChildOf(comboText.transform)))
        {
            error = "Combo Text must be a separate Text/TMP label outside Game Over Panel and must not contain UIManager or Retry Button.";
        }
        else if (!Finite(comboDisplaySeconds) || comboDisplaySeconds < 0.8f || comboDisplaySeconds > 1f
            || !Finite(comboPopSeconds) || comboPopSeconds <= 0f
            || !Finite(successPopScale) || successPopScale < 1f
            || !Finite(perfectPopScale) || perfectPopScale < 1f)
        {
            error = "Combo duration must be 0.8-1.0 seconds, pop duration positive, and pop scales at least 1. Use finite values.";
        }
        else if (feedbackRoot != null && (perfectText == null || comboText == null
            || !perfectText.transform.IsChildOf(feedbackRoot) || !comboText.transform.IsChildOf(feedbackRoot)
            || transform.IsChildOf(feedbackRoot) || feedbackRoot.IsChildOf(gameOverPanel.transform)))
        {
            error = "Feedback Root must contain the separate Perfect and Combo labels outside Game Over Panel and UIManager.";
        }

        return error == null;
    }

    // プレイ中の表示へ切り替える。結果パネルと一時演出を消し、渡されたスコアを反映する。
    public void ShowPlaying(int score, int best)
    {
        gameOverPanel.SetActive(false);
        HidePerfect();
        HideCombo();
        UpdateScore(score, best);
    }

    // スコア値の計算はせず、GameManagerから受け取った現在値をラベルへ渡す。
    public void UpdateScore(int score, int best)
    {
        SetLabel(scoreText, $"SCORE {score}");
        SetLabel(bestText, $"BEST {best}");
    }

    // 一時演出を消し、結果スコアを入れてから2種類のRetryボタンを含むパネルを表示する。
    public void ShowGameOver(int score, int best)
    {
        HidePerfect();
        HideCombo();
        UpdateScore(score, best);
        SetLabel(gameOverScoreText, $"SCORE {score}");
        gameOverPanel.SetActive(true);
    }

    // 判定時の単独Perfect表示を開始する。表示時間をカウントダウンするための残り秒数を設定する。
    public void ShowPerfect()
    {
        HideCombo();
        if (perfectText == null)
        {
            return;
        }

        SetLabel(perfectText, "PERFECT");
        perfectText.color = Color.white;
        perfectTimeRemaining = perfectDisplaySeconds;
        perfectText.gameObject.SetActive(perfectDisplaySeconds > 0f);
    }

    public void HidePerfect()
    {
        perfectTimeRemaining = 0f;
        // 次区間の準備で消すのは単独のPerfect表示だけ。
        // Perfectとコンボの2段表示は、コンボのアニメーションが終わるまで両方を保つ。
        if (showingPerfectCombo && comboVisible) return;
        if (perfectText != null)
        {
            perfectText.gameObject.SetActive(false);
        }
    }

    // 到着後に確定した連続数を表示する。連続Perfectが2回以上なら、通常コンボより優先して2段表示にする。
    public void ShowCombo(int successCount, int perfectCount)
    {
        // 最初のPerfectは判定時に表示済みなので、到着時に演出をやり直さない。
        // この場面では累計の成功コンボへ表示を置き換えることも避ける。
        if (perfectCount == 1) return;
        HideCombo();
        if (comboText == null || (successCount < 2 && perfectCount < 2))
        {
            return;
        }

        if (!comboScaleCaptured)
        {
            comboBaseScale = AnimationTarget.localScale;
            comboScaleCaptured = true;
        }

        HidePerfect();
        showingPerfectCombo = perfectCount >= 2;
        SetLabel(comboText, $"COMBO {(showingPerfectCombo ? perfectCount : successCount)}");
        if (showingPerfectCombo && perfectText != null)
        {
            SetLabel(perfectText, "PERFECT");
            perfectText.color = Color.white;
            perfectTimeRemaining = 0f;
            perfectText.gameObject.SetActive(true);
        }
        comboDisplayColor = showingPerfectCombo ? perfectComboColor : successComboColor;
        comboText.color = comboDisplayColor;
        comboText.raycastTarget = false;
        AnimationTarget.localScale = comboBaseScale;
        comboElapsed = 0f;
        comboVisible = true;
        comboText.gameObject.SetActive(true);
    }

    // アニメーションで変えたScaleを基準へ戻してから非表示にし、次の表示で拡大が積み重ならないようにする。
    public void HideCombo()
    {
        comboVisible = false;
        comboElapsed = 0f;
        if (comboText != null)
        {
            if (comboScaleCaptured)
            {
                AnimationTarget.localScale = comboBaseScale;
            }
            comboText.gameObject.SetActive(false);
        }
        if (showingPerfectCombo) HidePerfect();
        showingPerfectCombo = false;
    }

    // 表示時間を進め、拡大・縮小と最後のフェードを更新する。時間が終わった演出は非表示にする。
    private void Update()
    {
        if (perfectTimeRemaining > 0f)
        {
            perfectTimeRemaining -= Time.deltaTime;
            if (perfectTimeRemaining <= 0f)
            {
                HidePerfect();
            }
        }

        if (comboVisible && comboText != null)
        {
            comboElapsed += Time.deltaTime;
            if (comboElapsed >= comboDisplaySeconds)
            {
                HideCombo();
                return;
            }

            // 経過秒数を0〜1へ変換する。これを補間に使うと、演出時間を調整しても動きの形を保てる。
            float t = Mathf.Clamp01(comboElapsed / Mathf.Min(comboPopSeconds, comboDisplaySeconds));
            float scale;
            if (showingPerfectCombo)
            {
                // 大きくする → 少し縮める → 元の大きさへ戻す、の3段階で弾む動きを作る。
                scale = t < 0.4f ? Mathf.Lerp(1f, perfectPopScale, Mathf.SmoothStep(0f, 1f, t / 0.4f))
                    : t < 0.75f ? Mathf.Lerp(perfectPopScale, 0.96f, Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.35f))
                    : Mathf.Lerp(0.96f, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.75f) / 0.25f));
            }
            else
            {
                scale = 1f + (successPopScale - 1f) * Mathf.Sin(Mathf.PI * t);
            }

            // 元のScaleに演出用の倍率を掛ける。直前のScaleへ掛け続けると、表示するたびに大きくなってしまう。
            AnimationTarget.localScale = comboBaseScale * scale;
            Color color = comboDisplayColor;
            color.a *= Mathf.Clamp01((comboDisplaySeconds - comboElapsed) / 0.15f);
            comboText.color = color;
            if (showingPerfectCombo && perfectText != null)
                perfectText.color = new Color(1f, 1f, 1f, color.a);
        }
    }

    private void OnDisable()
    {
        HideCombo();
        HidePerfect();
    }

    private Transform AnimationTarget => feedbackRoot != null ? feedbackRoot : comboText.rectTransform;

    private static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void OnRetryClicked()
    {
        if (isActiveAndEnabled)
        {
            RetryRequested?.Invoke();
        }
    }

    // Awakeで登録したボタンの処理を解除し、同じ通知が重複登録されるのを防ぐ。
    private void OnDestroy()
    {
        if (retryButton != null)
        {
            retryButton.onClick.RemoveListener(OnRetryClicked);
        }
        if (retryStageButton != null) retryStageButton.onClick.RemoveListener(OnRetryStageClicked);
    }

    private void OnRetryStageClicked()
    {
        if (isActiveAndEnabled) RetryStageRequested?.Invoke();
    }

    private static bool IsLabel(Graphic label)
    {
        return label is Text || label is TMP_Text || label is RainbowLabel;
    }

    // 表示先の型ごとに文字の渡し方を切り替える。呼び出し側は同じメソッドで各ラベルを更新できる。
    private static void SetLabel(Graphic label, string value)
    {
        if (label is RainbowLabel rainbow)
        {
            rainbow.SetText(value);
        }
        else if (label is Text legacy)
        {
            legacy.text = value;
        }
        else if (label is TMP_Text tmp)
        {
            tmp.text = value;
        }
    }
}
