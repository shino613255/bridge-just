using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背景の切り替えまでの進み具合を読み取り、NEXT WORLDのプレビュー・星・数字を表示する。
/// 成功回数は自分では数えない。
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldProgressUI : MonoBehaviour
{
    [SerializeField] private GameManager game;
    [SerializeField] private BridgeJustSceneManager worlds;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Image preview;
    [SerializeField] private Text progressText;
    [Tooltip("0/5〜5/5を画像で表示するとき用（任意）。5段階以外のときは数字のTextを使う。")]
    [SerializeField] private Image progressImage;
    [SerializeField] private Sprite[] fiveStepProgressSprites = new Sprite[0];
    [SerializeField] private Image[] stars = new Image[0];
    [SerializeField] private Sprite filledStar;
    [SerializeField] private Sprite emptyStar;
    private int lastCompleted = -1, lastTotal = -1;
    private Sprite lastPreview;
    public int DisplayedProgress => lastCompleted;
    public int DisplayedTotal => lastTotal;
    public Sprite PreviewSprite => lastPreview;

    private void LateUpdate()
    {
        if (panelRoot == null) return;
        bool visible = game != null && game.isActiveAndEnabled && worlds != null
            && game.CurrentState != GameManager.GameState.GameOver
            && preview != null && progressText != null && filledStar != null && emptyStar != null;
        int completed = 0, total = 0;
        Sprite next = null;
        visible = visible && worlds.TryGetWorldProgress(out completed, out total, out next)
            && stars != null && stars.Length >= total;
        if (visible)
            for (int i = 0; i < total; i++) if (stars[i] == null) visible = false;
        panelRoot.SetActive(visible);
        if (!visible)
        {
            lastCompleted = lastTotal = -1;
            lastPreview = null;
            return;
        }
        bool imageCount = progressImage != null && total == 5 && fiveStepProgressSprites != null
            && completed >= 0 && completed < fiveStepProgressSprites.Length
            && fiveStepProgressSprites[completed] != null;
        progressText.gameObject.SetActive(!imageCount);
        if (progressImage != null)
        {
            progressImage.gameObject.SetActive(imageCount);
            if (imageCount) progressImage.sprite = fiveStepProgressSprites[completed];
        }
        if (lastCompleted != completed || lastTotal != total || lastPreview != next)
        {
            progressText.text = $"{completed}/{total}";
            preview.sprite = next;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null) continue;
                stars[i].gameObject.SetActive(i < total);
                stars[i].sprite = i < completed ? filledStar : emptyStar;
            }
            lastCompleted = completed;
            lastTotal = total;
            lastPreview = next;
        }
        // 縦横比を保ったまま、丸いマスク全体を覆う大きさにする。
        Rect area = ((RectTransform)preview.transform.parent).rect;
        Vector2 image = next.rect.size;
        float scale = Mathf.Max(area.width / image.x, area.height / image.y);
        preview.rectTransform.sizeDelta = image * scale;
    }
}
