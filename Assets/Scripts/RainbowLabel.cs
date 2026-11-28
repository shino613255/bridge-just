using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 文字画像と数字画像を、縦横比を保ちながら表示領域に収まるように並べる。
/// </summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class RainbowLabel : MaskableGraphic
{
    [Serializable]
    public struct Glyph
    {
        public string token;
        public Sprite sprite;
    }

    [SerializeField] private Glyph[] glyphs = new Glyph[0];
    [SerializeField] private string caption = "SCORE 0";
    [SerializeField, Min(1)] private float glyphHeight = 50f;
    [SerializeField, Min(0)] private float spacing = 5f;
    private readonly List<Image> cells = new List<Image>();
    private string drawn;
    private Vector2 previousSize;
    public string Caption => caption;

    public void Configure(Glyph[] assets, string value, float height)
    {
        glyphs = assets;
        glyphHeight = height;
        SetText(value);
    }

    public void SetText(string value)
    {
        caption = value ?? "";
        drawn = null;
        RebuildRow();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
        drawn = null;
    }

    protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

    private void LateUpdate()
    {
        if (drawn != caption || previousSize != rectTransform.rect.size) RebuildRow();
        // 画像の色はそのままにし、UIManagerのアニメーションからは透明度だけを受け取る。
        foreach (Image cell in cells)
            if (cell != null) cell.color = new Color(1f, 1f, 1f, color.a);
    }

    private void RebuildRow()
    {
        if (glyphs == null || !isActiveAndEnabled) return;
        if (cells.Count == 0)
            foreach (Transform child in transform)
                if (child.name.StartsWith("Rainbow glyph ") && child.TryGetComponent(out Image image))
                    cells.Add(image);

        // 文字列の先頭から、一致する一番長いトークンの画像を順に選ぶ。
        var sprites = new List<Sprite>();
        string text = caption.ToUpperInvariant();
        for (int at = 0; at < text.Length;)
        {
            Glyph? match = null;
            foreach (Glyph glyph in glyphs)
                if (!string.IsNullOrEmpty(glyph.token) && glyph.sprite != null
                    && at + glyph.token.Length <= text.Length
                    && string.Compare(text, at, glyph.token, 0, glyph.token.Length,
                        StringComparison.Ordinal) == 0
                    && (!match.HasValue || glyph.token.Length > match.Value.token.Length))
                    match = glyph;
            if (match.HasValue)
            {
                sprites.Add(match.Value.sprite);
                at += match.Value.token.Length;
            }
            else at++;
        }

        float height = Mathf.Min(glyphHeight, Mathf.Max(1f, rectTransform.rect.height));
        float total = Mathf.Max(0, sprites.Count - 1) * spacing;
        foreach (Sprite sprite in sprites) total += height * sprite.rect.width / sprite.rect.height;
        float fit = total > 0f ? Mathf.Min(1f, Mathf.Max(1f, rectTransform.rect.width) / total) : 1f;
        float x = -total * fit * 0.5f;
        for (int i = 0; i < sprites.Count; i++)
        {
            if (i == cells.Count)
            {
                var item = new GameObject("Rainbow glyph " + i,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                item.layer = gameObject.layer;
                item.transform.SetParent(transform, false);
                cells.Add(item.GetComponent<Image>());
            }
            Image cell = cells[i];
            if (cell.GetComponent<Shadow>() == null)
            {
                Shadow shadow = cell.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0.02f, 0.04f, 0.12f, 0.8f);
                shadow.effectDistance = new Vector2(2f, -2f);
            }
            cell.gameObject.SetActive(true);
            cell.raycastTarget = false;
            cell.sprite = sprites[i];
            cell.preserveAspect = true;
            cell.type = Image.Type.Simple;
            float width = height * sprites[i].rect.width / sprites[i].rect.height * fit;
            RectTransform rt = cell.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(width, height * fit);
            rt.anchoredPosition = new Vector2(x + width * 0.5f, 0f);
            x += width + spacing * fit;
        }
        for (int i = sprites.Count; i < cells.Count; i++) cells[i].gameObject.SetActive(false);
        previousSize = rectTransform.rect.size;
        drawn = caption;
    }
}
