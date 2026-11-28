using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// SCORE・BEST・成功演出・GAME OVERとRetryボタンを表示する。
/// 数値や結果はGameManagerから受け取り、ここでは得点や判定を計算しない。
/// 通常のText・TextMeshPro・RainbowLabelのどれでも、SetLabelで同じように文字を設定できる。
/// </summary>
[DisallowMultipleComponent]
public sealed class UIManager : MonoBehaviour
{
    [Tooltip("TextかTextMeshProのオブジェクトを入れる（Canvasではない）。")]
    [SerializeField] private Graphic scoreText;
    [SerializeField] private Graphic bestText;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Graphic gameOverScoreText;
    [SerializeField] private Button retryButton;
    [Tooltip("今のステージからやり直すボタン。Retry Buttonと同じGame Over Panelの中に置く。")]
    [SerializeField] private Button retryStageButton;
    [SerializeField] private Graphic perfectText;
    [SerializeField, Min(0f)] private float perfectDisplaySeconds = 0.6f;

    [Header("コンボ表示（任意）")]
    [Tooltip("日本語を表示できる別のラベル。Game Over Panelの外に置く。文字の大きさはそのラベル側で設定する。")]
    [SerializeField] private Graphic comboText;
    [Tooltip("PERFECTとCOMBOの行をまとめる親（任意）。設定すると2行を一緒に動かす。")]
    [SerializeField] private RectTransform feedbackRoot;
    [SerializeField, Range(0.8f, 1f)] private float comboDisplaySeconds = 0.9f;
    [SerializeField, Min(0.01f)] private float comboPopSeconds = 0.28f;
    [SerializeField, Min(1f)] private float successPopScale = 1.12f;
    [SerializeField, Min(1f)] private float perfectPopScale = 1.25f;
    [SerializeField] private Color successComboColor = new Color(1f, 0.78f, 0.12f, 1f);
    [SerializeField] private Color perfectComboColor = new Color(1f, 0.52f, 0.05f, 1f);

    // ボタンが押されたことだけを知らせ、再挑戦の処理はGameManagerが行う。
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

    // ラベル・ボタン・Canvasの設定を確認する。パネルを隠したときにUIManagerまで止まらないかも見る。
    public bool ValidateSetup(out string error)
    {
        error = null;
        if (!isActiveAndEnabled || !IsLabel(scoreText) || gameOverPanel == null || retryButton == null)
        {
            error = "UIManager needs an active component, Score Text, Game Over Panel and Retry Button. "
                + "Labels must be Legacy Text or TMP_Text.";
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
            error = "Retry Stage Button must be a separate, enabled button "
                + "inside Game Over Panel's raycast Canvas.";
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
            error = "Combo Text must be a separate Text/TMP label outside Game Over Panel "
                + "and must not contain UIManager or Retry Button.";
        }
        else if (!Finite(comboDisplaySeconds) || comboDisplaySeconds < 0.8f || comboDisplaySeconds > 1f
            || !Finite(comboPopSeconds) || comboPopSeconds <= 0f
            || !Finite(successPopScale) || successPopScale < 1f
            || !Finite(perfectPopScale) || perfectPopScale < 1f)
        {
            error = "Combo duration must be 0.8-1.0 seconds, pop duration positive, "
                + "and pop scales at least 1. Use finite values.";
        }
        else if (feedbackRoot != null && (perfectText == null || comboText == null
            || !perfectText.transform.IsChildOf(feedbackRoot) || !comboText.transform.IsChildOf(feedbackRoot)
            || transform.IsChildOf(feedbackRoot) || feedbackRoot.IsChildOf(gameOverPanel.transform)))
        {
            error = "Feedback Root must contain the separate Perfect and Combo labels "
                + "outside Game Over Panel and UIManager.";
        }

        return error == null;
    }

    public void ShowPlaying(int score, int best)
    {
        gameOverPanel.SetActive(false);
        HidePerfect();
        HideCombo();
        UpdateScore(score, best);
    }

    public void UpdateScore(int score, int best)
    {
        SetLabel(scoreText, $"SCORE {score}");
        SetLabel(bestText, $"BEST {best}");
    }

    public void ShowGameOver(int score, int best)
    {
        HidePerfect();
        HideCombo();
        UpdateScore(score, best);
        SetLabel(gameOverScoreText, $"SCORE {score}");
        gameOverPanel.SetActive(true);
    }

    // 判定した瞬間に出す、単独のPERFECT表示。
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
        // PERFECTとCOMBOの2段表示中は、コンボのアニメーションが終わるまでPERFECTも残す。
        if (showingPerfectCombo && comboVisible) return;
        if (perfectText != null)
        {
            perfectText.gameObject.SetActive(false);
        }
    }

    // 到着後に確定した連続数を表示する。Perfectが2回以上続いたら、通常コンボより優先して2段表示にする。
    public void ShowCombo(int successCount, int perfectCount)
    {
        // 1回目のPerfectは判定時に表示済みなので、ここでは何もしない。
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

    // Scaleを元に戻してから隠す。戻さないと、次に表示したとき拡大が積み重なる。
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

            float t = Mathf.Clamp01(comboElapsed / Mathf.Min(comboPopSeconds, comboDisplaySeconds));
            float scale;
            if (showingPerfectCombo)
            {
                // 大きくする → 少し縮める → 元の大きさへ戻す、の3段階で弾ませる。
                scale = t < 0.4f
                    ? Mathf.Lerp(1f, perfectPopScale, Mathf.SmoothStep(0f, 1f, t / 0.4f))
                    : t < 0.75f
                        ? Mathf.Lerp(perfectPopScale, 0.96f, Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.35f))
                        : Mathf.Lerp(0.96f, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.75f) / 0.25f));
            }
            else
            {
                scale = 1f + (successPopScale - 1f) * Mathf.Sin(Mathf.PI * t);
            }

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
