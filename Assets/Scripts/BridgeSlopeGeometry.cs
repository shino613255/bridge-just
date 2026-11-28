using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 橋と足場の接続・重なり・歩行経路に共通する座標計算。
/// 傾いたSpriteをワールドX方向の外接矩形で測ると幅が変わるため、傾斜に沿った距離で扱う。
/// このファイルには計算関数、歩行面データ、橋の表示、傾斜の抽選の4つのまとまりがある。
/// </summary>
public static class BridgeSlopeGeometry
{
    // Axisは足場に沿う向き、Normalはそこから上へ直角に伸びる向き。どちらも長さ1。
    public const float AngleDegrees = 15f;
    public static Vector3 Axis(float angle) => Quaternion.Euler(0f, 0f, angle) * Vector3.right;
    public static Vector3 Normal(float angle) => Quaternion.Euler(0f, 0f, angle) * Vector3.up;

    // Spriteの角をワールド座標で返す。回転・Scale・Pivotの違いも反映される。
    public static Vector3 TopLeft(SpriteRenderer r) => r.transform.TransformPoint(
        new Vector3(r.localBounds.min.x, r.localBounds.max.y, r.localBounds.center.z));

    public static Vector3 TopRight(SpriteRenderer r) => r.transform.TransformPoint(
        new Vector3(r.localBounds.max.x, r.localBounds.max.y, r.localBounds.center.z));

    public static Vector3 TopCenter(SpriteRenderer r) => (TopLeft(r) + TopRight(r)) * 0.5f;

    public static Vector3 BottomLeft(SpriteRenderer r) => r.transform.TransformPoint(
        new Vector3(r.localBounds.min.x, r.localBounds.min.y, r.localBounds.center.z));

    public static Vector3 BottomRight(SpriteRenderer r) => r.transform.TransformPoint(
        new Vector3(r.localBounds.max.x, r.localBounds.min.y, r.localBounds.center.z));

    public static Vector3 BottomCenter(SpriteRenderer r) => (BottomLeft(r) + BottomRight(r)) * 0.5f;
    public static float Thickness(SpriteRenderer r) => Vector3.Distance(TopCenter(r), BottomCenter(r));
    public static float Length(SpriteRenderer r) => Vector3.Distance(TopLeft(r), TopRight(r));

    // 上面の両端を軸へ投影し、原点からの距離の範囲（小さい方, 大きい方）にする。
    public static Vector2 Interval(SpriteRenderer r, Vector3 origin, Vector3 axis)
    {
        float a = Vector3.Dot(TopLeft(r) - origin, axis);
        float b = Vector3.Dot(TopRight(r) - origin, axis);
        return new Vector2(Mathf.Min(a, b), Mathf.Max(a, b));
    }

    // 橋は足場の上に載せるので、判定に使う橋の端は下面側から取る。
    public static Vector2 BottomInterval(SpriteRenderer r, Vector3 origin, Vector3 axis)
    {
        float a = Vector3.Dot(BottomLeft(r) - origin, axis);
        float b = Vector3.Dot(BottomRight(r) - origin, axis);
        return new Vector2(Mathf.Min(a, b), Mathf.Max(a, b));
    }

    // 2つの範囲が重なっている長さ。離れていれば0。
    public static float Overlap(Vector2 a, Vector2 b)
        => Mathf.Max(0f, Mathf.Min(a.y, b.y) - Mathf.Max(a.x, b.x));

    // 回転したあと、左上端が目標点にぴったり重なるように動かす。
    public static void PlaceTopLeft(SpriteRenderer r, Vector3 point, float angle)
    {
        r.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        r.transform.position += point - TopLeft(r);
    }

    // 上面の中心を動かさずに傾ける。
    public static void RotateAroundTopCenter(SpriteRenderer r, float angle)
    {
        Vector3 center = TopCenter(r);
        r.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        r.transform.position += center - TopCenter(r);
    }

    // 傾斜が変わる角で、キャラクターの中心が急に飛ばずに角を回れる位置を求める。
    public static Vector3 CornerCenter(Vector3 corner, Vector3 oldNormal, Vector3 newNormal, float clearance)
    {
        return corner + (oldNormal + newNormal)
            * (clearance / (1f + Vector3.Dot(oldNormal, newNormal)));
    }

    public static Vector3 PointAfterTurn(SpriteRenderer platform, Vector3 point, Vector3 pivot, float angle)
    {
        Quaternion turn = Quaternion.Euler(0f, 0f, angle)
            * Quaternion.Inverse(platform.transform.rotation);
        return pivot + turn * (point - pivot);
    }

    public static void TurnAround(SpriteRenderer platform, Vector3 pivot, float angle)
    {
        Vector3 position = PointAfterTurn(platform, platform.transform.position, pivot, angle);
        platform.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
    }

    /// <summary>
    /// 橋の端での乗り上がり・降りを含め、実際の表面を通る連続した経路を求める。
    /// </summary>
    public static Vector3[] BoardCrossingPath(SpriteRenderer left, SpriteRenderer right,
        SpriteRenderer board, Vector3 start, float clearance)
    {
        return new BridgeBoardSurface(left, board).CrossingPath(left, right, start, clearance);
    }

    public static float BoardSupportLift(float horizontalDistance, float thickness, float clearance)
    {
        if (horizontalDistance >= clearance || clearance <= 0f) return 0f;
        return Mathf.Max(0f, thickness + Mathf.Sqrt(Mathf.Max(0f,
            clearance * clearance - horizontalDistance * horizontalDistance)) - clearance);
    }

    // 円が板の角に触れ始める水平距離。半径と板の厚みから三平方の定理で求める。
    public static float StepApproachDistance(float thickness, float clearance)
    {
        return thickness <= clearance
            ? Mathf.Sqrt(Mathf.Max(0f,
                clearance * clearance - (clearance - thickness) * (clearance - thickness)))
            : clearance;
    }
}

/// <summary>
/// 1区間の橋を、接続点からの距離と高さで表す。
/// 判定・描画・キャラクターの接地計算で、このデータを共有する。
/// </summary>
public struct BridgeBoardSurface
{
    public Vector3 Origin, Axis, Normal, LeftAxis, LeftNormal;
    public float Min, Max, Height;
    public BridgeSkinDefinition Skin;
    public float SkinMaximumLength;
    public Material SkinMaterial;
    public float Cosine => Vector3.Dot(LeftAxis, Axis);
    public bool Bent => Cosine < 0.9999f;

    // 左足場の右上端を原点にし、橋側と足場側それぞれの向きを保存する。
    // 傾斜が異なる接続部も、1つの座標系で処理できる。
    public BridgeBoardSurface(SpriteRenderer left, SpriteRenderer board)
    {
        Origin = BridgeSlopeGeometry.TopRight(left);
        Axis = board.transform.right;
        Normal = board.transform.up;
        LeftAxis = left.transform.right;
        LeftNormal = left.transform.up;
        Vector2 span = BridgeSlopeGeometry.BottomInterval(board, Origin, Axis);
        Min = span.x;
        Max = span.y;
        Height = BridgeSlopeGeometry.Thickness(board);
        var library = board.GetComponent<BridgeSkinLibrary>();
        Skin = library != null ? library.Active : null;
        SkinMaximumLength = library != null ? library.MaximumLength : 0f;
        SkinMaterial = library != null ? library.SkinMaterial : null;
    }

    // ワールド座標を「橋に沿う距離, 橋からの高さ」へ変換する。Worldはその逆。
    public Vector2 Coordinates(Vector3 world) => new Vector2(
        Vector3.Dot(world - Origin, Axis),
        Vector3.Dot(world - Origin, Normal));

    public Vector3 World(Vector2 p) => Origin + Axis * p.x + Normal * p.y;

    // 古い足場に乗る部分だけを折り曲げる。
    // 傾斜方向への投影距離は変えないので、見た目をつないでも成功判定の長さは変わらない。
    public Vector3 SkinPoint(Vector2 pixel)
    {
        float s = Min + Skin.PixelToSpan(pixel.x, Max - Min, SkinMaximumLength);
        float n = Skin.PixelHeight(pixel, Max - Min, SkinMaximumLength);
        if (s < 0f) n += s * Vector3.Dot(LeftAxis, Normal) / Cosine;
        return World(new Vector2(s, n));
    }

    private Vector2 SkinCoordinates(Vector2 pixel) => Coordinates(SkinPoint(pixel));

    // 足場の折れ目と伸縮橋の飾りの端で区切る。
    // 境界をまたいで補間すると、角や固定端の位置が丸められてしまうため。
    private float NextSkinKnot(float from, float to)
    {
        float next = to;
        void Test(float x) { if (x > from + 0.00001f && x < next) next = x; }
        Test(Skin.SpanToPixel(-Min, Max - Min, SkinMaximumLength));
        if (Skin.motion == BridgeSkinMotion.RightAnchoredExtension)
        {
            Test(Skin.leftFoot.x + Skin.endCaps.x);
            Test(Skin.rightFoot.x - Skin.endCaps.y);
        }
        return next;
    }

    // キャラクターを支える橋の表面を、区間座標の図形として作る。
    public Vector2[][] Polygons()
    {
        if (Skin != null)
        {
            var edges = new List<Vector2[]>(Skin.topSurface.Length + 5);
            Vector2 a = Skin.leftFoot;
            for (int i = 0; i <= Skin.topSurface.Length; i++)
            {
                Vector2 b = i < Skin.topSurface.Length ? Skin.topSurface[i] : Skin.rightFoot;
                float x = a.x;
                do
                {
                    float next = NextSkinKnot(x, b.x);
                    Vector2 p = Vector2.Lerp(a, b, Mathf.InverseLerp(a.x, b.x, x));
                    Vector2 q = Vector2.Lerp(a, b, Mathf.InverseLerp(a.x, b.x, next));
                    if (a.x == b.x)
                    {
                        p = a;
                        q = b;
                    }
                    edges.Add(new[] { SkinCoordinates(p), SkinCoordinates(q) });
                    x = next;
                } while (x < b.x - 0.00001f);
                a = b;
            }
            return edges.ToArray();
        }
        BridgeBoardSurface shape = this;
        Vector2[] Rectangle(float from, float to, Vector3 axis, Vector3 normal)
        {
            Vector3 a = shape.Origin + axis * from, b = shape.Origin + axis * to;
            return new[]
            {
                shape.Coordinates(a),
                shape.Coordinates(b),
                shape.Coordinates(b + normal * shape.Height),
                shape.Coordinates(a + normal * shape.Height)
            };
        }
        if (!Bent) return new[] { Rectangle(Min, Max, Axis, Normal) };
        var polygons = new List<Vector2[]>(3);
        if (Min < 0f)
            polygons.Add(Rectangle(Min / Cosine, Mathf.Min(Max, 0f) / Cosine, LeftAxis, LeftNormal));
        if (Max > 0f) polygons.Add(Rectangle(Mathf.Max(Min, 0f), Max, Axis, Normal));
        // 傾斜が変わる所を三角形で埋め、接続部に尖った突起ができないようにする。
        if (Min < 0f && Max > 0f)
            polygons.Add(new[] { Vector2.zero, Coordinates(Origin + LeftNormal * Height), new Vector2(0f, Height) });
        return polygons.ToArray();
    }

    // 半径radiusの円が表面に乗るときの、中心の高さを求める。
    // 各辺と各頂点が要求する高さの最大値を取るので、山・谷・板の端にもめり込まない。
    public static float SupportHeight(Vector2[][] polygons, float s, float radius)
    {
        float height = float.NegativeInfinity;
        foreach (Vector2[] polygon in polygons)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                // 頂点に触れるときの中心の高さ = 頂点の高さ + √(半径² − 横の距離²)
                float dx = s - a.x;
                if (Mathf.Abs(dx) <= radius)
                    height = Mathf.Max(height, a.y + Mathf.Sqrt(Mathf.Max(0f, radius * radius - dx * dx)));
                if (b.x < a.x) { Vector2 swap = a; a = b; b = swap; }
                float length = Vector2.Distance(a, b);
                if (length < 0.000001f || b.x - a.x < 0.000001f) continue;
                // 辺の上を転がるときの中心は、辺を半径分だけ外側へずらした線の上にある。
                Vector2 tangent = (b - a) / length;
                Vector2 offset = new Vector2(-tangent.y, tangent.x) * radius;
                float along = (s - a.x - offset.x) / tangent.x;
                if (along >= 0f && along <= length)
                    height = Mathf.Max(height, a.y + offset.y + tangent.y * along);
            }
        }
        return height;
    }

    // 待機中のキャラの下に橋が来たとき、体を持ち上げる高さ。元の高さより下へは押し込まない。
    public float WaitingHeight(Vector3 position, float radius)
    {
        Vector2 p = Coordinates(position);
        if (Skin != null)
        {
            float h = float.NegativeInfinity;
            Vector2 a = SkinCoordinates(Skin.leftFoot);
            for (int i = 0; i <= Skin.topSurface.Length; i++)
            {
                Vector2 start = i == 0 ? Skin.leftFoot : Skin.topSurface[i - 1];
                Vector2 end = i < Skin.topSurface.Length ? Skin.topSurface[i] : Skin.rightFoot;
                float x = start.x;
                do
                {
                    float next = NextSkinKnot(x, end.x);
                    Vector2 pixel = Vector2.Lerp(start, end, Mathf.InverseLerp(start.x, end.x, next));
                    if (start.x == end.x) pixel = end;
                    Vector2 b = SkinCoordinates(pixel);
                    h = Mathf.Max(h, SegmentSupportHeight(a, b, p.x, radius));
                    a = b;
                    x = next;
                } while (x < end.x - 0.00001f);
            }
            return Mathf.Max(p.y, h);
        }
        return Mathf.Max(p.y, SupportHeight(Polygons(), p.x, radius));
    }

    private static float SegmentSupportHeight(Vector2 a, Vector2 b, float s, float radius)
    {
        float h = float.NegativeInfinity;
        foreachEnd(a);
        foreachEnd(b);
        void foreachEnd(Vector2 p)
        {
            float dx = s - p.x;
            if (Mathf.Abs(dx) <= radius)
                h = Mathf.Max(h, p.y + Mathf.Sqrt(Mathf.Max(0, radius * radius - dx * dx)));
        }
        if (b.x < a.x)
        {
            var swap = a;
            a = b;
            b = swap;
        }
        float length = Vector2.Distance(a, b);
        if (length > 0.000001f && b.x - a.x > 0.000001f)
        {
            Vector2 t = (b - a) / length, offset = new Vector2(-t.y, t.x) * radius;
            float along = (s - a.x - offset.x) / t.x;
            if (along >= 0 && along <= length) h = Mathf.Max(h, a.y + offset.y + t.y * along);
        }
        return h;
    }

    // 右足場の中央か、橋の端を越えた位置を着地点にして経路を作る。
    public Vector3[] CrossingPath(SpriteRenderer left, SpriteRenderer right, Vector3 start, float radius)
    {
        if (radius <= 0f || Cosine < 0.5f) return null;
        Vector2 landing = BridgeSlopeGeometry.Interval(right, Origin, Axis);
        float end = Mathf.Max((landing.x + landing.y) * 0.5f,
            Max + (Skin != null
                ? radius + 0.005f
                : BridgeSlopeGeometry.StepApproachDistance(Height, radius)));
        if (end <= Coordinates(start).x || end > landing.y - 0.001f) return null;
        return PathToSurfacePosition(left, right, start, radius, end);
    }

    /// <summary>
    /// 指定した終点まで、キャラクターの半径に合う表面経路を作る。子供が合流する経路にも使う。
    /// </summary>
    public Vector3[] PathToSurfacePosition(SpriteRenderer left, SpriteRenderer right,
        Vector3 start, float radius, float end)
    {
        if (radius <= 0f || Cosine < 0.5f) return null;
        var polygons = new List<Vector2[]>(Polygons())
        {
            new[]
            {
                Coordinates(BridgeSlopeGeometry.TopLeft(left)),
                Coordinates(BridgeSlopeGeometry.TopRight(left))
            },
            new[]
            {
                Coordinates(BridgeSlopeGeometry.TopLeft(right)),
                Coordinates(BridgeSlopeGeometry.TopRight(right))
            }
        };
        Vector2[][] solids = polygons.ToArray();
        Vector2 first = Coordinates(start);
        if (end < BridgeSlopeGeometry.Interval(left, Origin, Axis).x
            || end > BridgeSlopeGeometry.Interval(right, Origin, Axis).y) return null;
        var points = new List<Vector3>();
        Vector3 origin = Origin, axis = Axis, normal = Normal;
        bool detailedSkin = Skin != null;

        // 距離sの位置で、表面に乗るキャラの中心をワールド座標で返す。
        Vector3 At(float s)
        {
            float height = SupportHeight(solids, s, radius);
            Vector3 p = origin + axis * s + normal * height;
            p.z = start.z;
            return p;
        }

        // 表面の曲がりが大きい所だけ細かく分割し、直線でつないだ経路と表面のずれを抑える。
        void Segment(float a, Vector3 pa, float b, Vector3 pb, int depth)
        {
            float middle = (a + b) * 0.5f;
            Vector3 pm = At(middle);
            // スキン付きの橋は短い凹凸を中点だけでは見落とすので、1/4と3/4の点でも調べる。
            bool skinError = detailedSkin && (Mathf.Abs(b - a) > 0.01f
                || Vector3.Distance(At(Mathf.Lerp(a, b, .25f)), Vector3.Lerp(pa, pb, .25f)) > 0.00005f
                || Vector3.Distance(At(Mathf.Lerp(a, b, .75f)), Vector3.Lerp(pa, pb, .75f)) > 0.00005f);
            if (depth < (detailedSkin ? 14 : 12)
                && (skinError || Mathf.Abs(b - a) > 0.04f
                    || Vector3.Distance(pm, (pa + pb) * 0.5f) > 0.0002f))
            {
                Segment(a, pa, middle, pm, depth + 1);
                Segment(middle, pm, b, pb, depth + 1);
            }
            else points.Add(pb);
        }

        // 今の体が表面から大きく離れていたら、急に表面へ飛ばさず経路作成を失敗にする。
        Vector3 supportedStart = At(first.x);
        if (Vector3.Distance(start, supportedStart) > 0.001f) return null;
        Segment(first.x, start, end, At(end), 0);
        return points.ToArray();
    }
}

/// <summary>
/// 橋を描画するための使い回せる表示部品。
/// 元のSpriteRendererは移動・判定の基準として残し、表示はここで描き分ける。
/// </summary>
public sealed class BridgeBoardVisual : IDisposable
{
    private readonly GameObject root;
    private readonly SpriteRenderer[] parts = new SpriteRenderer[3];
    private Sprite jointSprite, sourceSprite;
    private Vector3 jointLeftNormal, jointNormal;
    private float jointHeight;
    private bool jointReady;
    private BridgeBoardSurface surface;
    private Color color;
    private readonly BridgeSkinVisual skinVisual;
    public bool Visible => root.activeSelf;
    public BridgeBoardSurface Surface => surface;

    public float TopY
    {
        get
        {
            if (skinVisual.Visible) return skinVisual.TopY;
            float top = float.NegativeInfinity;
            foreach (var part in parts)
                if (part.enabled) top = Mathf.Max(top, part.bounds.max.y);
            return top;
        }
    }

    // 表示用の部品は一度だけ作って使い回し、成功のたびにGameObjectが増えないようにする。
    public BridgeBoardVisual(string name)
    {
        root = new GameObject(name);
        skinVisual = new BridgeSkinVisual(root.transform);
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = new GameObject("Board part " + i).AddComponent<SpriteRenderer>();
            parts[i].transform.SetParent(root.transform, false);
        }
        root.SetActive(false);
    }

    public void Show(bool visible) => root.SetActive(visible);

    public void Shift(Vector3 delta)
    {
        surface.Origin += delta;
        root.transform.position += delta;
    }

    // 親を動かして橋全体を一体で落とす。曲線・接続部・伸縮の状態はそのまま保つ。
    public void ApplyCollapse(Vector3 pivot, Vector3 displacement, float degrees)
    {
        Quaternion turn = Quaternion.Euler(0, 0, degrees);
        root.transform.SetPositionAndRotation(pivot + displacement - turn * pivot, turn);
    }

    public void SetAlpha(float alpha)
    {
        Color faded = color;
        faded.a *= alpha;
        foreach (SpriteRenderer part in parts) part.color = faded;
        skinVisual.SetAlpha(alpha);
    }

    // 表面データから表示を作り直す。スキンがあれば画像メッシュ、なければ板と接続部を描く。
    public void Configure(BridgeBoardSurface shape, SpriteRenderer source)
    {
        surface = shape;
        color = source.color;
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (SpriteRenderer part in parts)
        {
            part.enabled = false;
            part.gameObject.layer = source.gameObject.layer;
            part.sharedMaterials = source.sharedMaterials;
            part.color = color;
            part.sortingLayerID = source.sortingLayerID;
            part.sortingOrder = source.sortingOrder;
            part.renderingLayerMask = source.renderingLayerMask;
            part.maskInteraction = source.maskInteraction;
            part.spriteSortPoint = source.spriteSortPoint;
        }
        if (shape.Skin != null && shape.SkinMaterial != null)
        {
            skinVisual.Configure(shape, source, shape.SkinMaterial);
            return;
        }
        skinVisual.Show(false);

        // 指定した範囲を1枚の板として表示する。下面を基準に合わせ、厚みは上向きに付ける。
        void Strip(int index, float from, float to, Vector3 axis, Vector3 normal)
        {
            if (to - from < 0.000001f) return;
            SpriteRenderer part = parts[index];
            part.sprite = source.sprite;
            part.flipX = source.flipX;
            part.flipY = source.flipY;
            part.drawMode = SpriteDrawMode.Simple;
            Bounds bounds = source.localBounds;
            part.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg);
            part.transform.localScale = new Vector3(
                (to - from) / bounds.size.x, shape.Height / bounds.size.y, 1f);
            part.transform.position = shape.Origin + axis * ((from + to) * 0.5f)
                - (part.transform.TransformPoint(
                    new Vector3(bounds.center.x, bounds.min.y, bounds.center.z))
                    - part.transform.position);
            part.enabled = true;
        }

        if (!shape.Bent)
        {
            Strip(0, shape.Min, shape.Max, shape.Axis, shape.Normal);
            return;
        }
        if (shape.Min < 0f)
            Strip(0, shape.Min / shape.Cosine, Mathf.Min(shape.Max, 0f) / shape.Cosine,
                shape.LeftAxis, shape.LeftNormal);
        if (shape.Max > 0f) Strip(1, Mathf.Max(shape.Min, 0f), shape.Max, shape.Axis, shape.Normal);
        if (shape.Min >= 0f || shape.Max <= 0f) return;
        if (sourceSprite != source.sprite)
        {
            if (jointSprite != null) UnityEngine.Object.Destroy(jointSprite);
            sourceSprite = source.sprite;
            jointReady = false;
            // 元の画像は加工せず、実行時に作ったコピーだけを接続部の形に変える。
            Rect rect = sourceSprite.rect;
            jointSprite = Sprite.Create(sourceSprite.texture, rect,
                new Vector2(sourceSprite.pivot.x / rect.width, sourceSprite.pivot.y / rect.height),
                sourceSprite.pixelsPerUnit, 0, SpriteMeshType.Tight);
            jointSprite.name = sourceSprite.name + " (runtime bevel)";
        }
        Vector2 center = sourceSprite.bounds.center;
        Vector2 a = (Vector2)(shape.LeftNormal * shape.Height), b = (Vector2)(shape.Normal * shape.Height);
        Vector2 pixelCenter = sourceSprite.pivot + center * sourceSprite.pixelsPerUnit;
        if (!jointReady
            || Vector3.Distance(jointLeftNormal, shape.LeftNormal) > 0.00001f
            || Vector3.Distance(jointNormal, shape.Normal) > 0.00001f
            || Mathf.Abs(jointHeight - shape.Height) > 0.00001f)
        {
            jointSprite.OverrideGeometry(
                new[]
                {
                    pixelCenter,
                    pixelCenter + a * sourceSprite.pixelsPerUnit,
                    pixelCenter + b * sourceSprite.pixelsPerUnit
                },
                a.x * b.y - a.y * b.x > 0f ? new ushort[] { 0, 2, 1 } : new ushort[] { 0, 1, 2 });
            jointReady = true;
            jointLeftNormal = shape.LeftNormal;
            jointNormal = shape.Normal;
            jointHeight = shape.Height;
        }
        parts[2].sprite = jointSprite;
        parts[2].transform.SetPositionAndRotation(shape.Origin - (Vector3)center, Quaternion.identity);
        parts[2].transform.localScale = Vector3.one;
        parts[2].enabled = true;
    }

    public void Dispose()
    {
        skinVisual.Dispose();
        if (jointSprite != null) UnityEngine.Object.Destroy(jointSprite);
        UnityEngine.Object.Destroy(root);
    }
}

/// <summary>
/// 傾斜を変えるタイミングと向きの抽選を管理する。ステージ番号はGameManagerから受け取る。
/// </summary>
public sealed class BridgeSlopeSchedule
{
    private readonly System.Random random;
    public int Direction { get; private set; }
    public int Interval { get; private set; }
    public long NextChangeStage { get; private set; }
    public float Angle => Direction * BridgeSlopeGeometry.AngleDegrees;

    public BridgeSlopeSchedule(System.Random random = null)
    {
        this.random = random ?? new System.Random();
    }

    // 新しいプレイの最初の傾斜と、次に変わるまでの区間数（3〜6）を抽選する。
    public void Reset()
    {
        Direction = random.Next(0, 3) - 1;
        Interval = random.Next(3, 7);
        NextChangeStage = 1L + Interval;
    }

    // ステージが予定に達したら抽選する。番号がまとめて進んでも追いつけるようwhileにしている。
    public bool AdvanceTo(long stage)
    {
        bool changed = false;
        while (stage >= NextChangeStage && NextChangeStage > 0)
        {
            // 下り・水平・上りをそれぞれ1/3で抽選する。同じ傾斜が続くこともある。
            Direction = random.Next(0, 3) - 1;
            Interval = random.Next(3, 7);
            NextChangeStage += Interval;
            changed = true;
        }
        return changed;
    }
}
