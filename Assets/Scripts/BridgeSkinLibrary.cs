using System;
using UnityEngine;

public enum BridgeSkinMotion { Reciprocating, CurvedReciprocating, RightAnchoredExtension }

/// <summary>
/// 各橋スキンの接続点と歩行面を、元画像の左下を原点とするピクセル座標で定義する。
/// </summary>
[Serializable]
public sealed class BridgeSkinDefinition
{
    public string id;
    public Sprite sprite;
    public bool enabled = true;
    [Min(0f)] public float weight = 1f;
    public BridgeSkinMotion motion;
    [Tooltip("Left/right support points in sprite-rect pixels, bottom-left origin. Not texture margins.")]
    public Vector2 leftFoot, rightFoot;
    [Tooltip("Left-to-right samples of the solid walking surface in the same pixel coordinates.")]
    public Vector2[] topSurface;
    [Tooltip("Moving Walkway only: fixed end-cap widths in source pixels. Middle stretches, caps retain their size.")]
    public Vector2 endCaps;

    public bool Validate(out string reason)
    {
        reason = null;
        if (sprite == null) reason = "Sprite is missing";
        else if (!Finite(weight) || weight <= 0f) reason = "Weight must be positive and finite";
        else if (!Finite(leftFoot.x) || !Finite(leftFoot.y) || !Finite(rightFoot.x) || !Finite(rightFoot.y)
            || rightFoot.x <= leftFoot.x || leftFoot.x < 0f || rightFoot.x > sprite.rect.width)
            reason = "Invalid support endpoints";
        else if (topSurface == null || topSurface.Length < 2) reason = "At least two top samples are required";
        else
        {
            for (int i = 0; i < topSurface.Length; i++)
                if (!Finite(topSurface[i].x) || !Finite(topSurface[i].y)
                    || (i > 0 && topSurface[i].x <= topSurface[i - 1].x)) reason = "Top samples must be finite and ordered";
            if (Mathf.Abs(topSurface[0].x-leftFoot.x) > 0.01f
                || Mathf.Abs(topSurface[topSurface.Length-1].x-rightFoot.x) > 0.01f) reason = "Top samples must end at the support endpoints";
            if (!Finite(endCaps.x) || !Finite(endCaps.y) || endCaps.x < 0 || endCaps.y < 0
                || endCaps.x+endCaps.y >= rightFoot.x-leftFoot.x) reason = "Invalid end caps";
        }
        return reason == null;
    }

    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

    public float PixelToSpan(float pixelX, float length, float maximumLength)
    {
        float pixels = rightFoot.x - leftFoot.x;
        float p = pixelX - leftFoot.x;
        if (motion != BridgeSkinMotion.RightAnchoredExtension) return p / pixels * length;
        float unit = maximumLength / pixels;
        float left = endCaps.x * unit, right = endCaps.y * unit;
        // 伸縮しても左右の装飾端は拡縮せず、中央部分だけを伸ばす。
        if (p <= endCaps.x) return p * unit;
        if (p >= pixels - endCaps.y) return length - (pixels - p) * unit;
        return left + (p - endCaps.x) / (pixels - endCaps.x - endCaps.y) * (length - left - right);
    }

    public float PixelHeight(Vector2 p, float length, float maximumLength)
    {
        float pixels = rightFoot.x-leftFoot.x;
        float baseline = Mathf.LerpUnclamped(leftFoot.y, rightFoot.y, (p.x-leftFoot.x)/pixels);
        return (p.y-baseline) / pixels * (motion == BridgeSkinMotion.RightAnchoredExtension ? maximumLength : length);
    }

    public float SpanToPixel(float distance,float length,float maximumLength)
    {
        float pixels=rightFoot.x-leftFoot.x;
        if(motion!=BridgeSkinMotion.RightAnchoredExtension)return leftFoot.x+distance/length*pixels;
        float unit=maximumLength/pixels,left=endCaps.x*unit,right=endCaps.y*unit;
        if(distance<=left)return leftFoot.x+distance/unit;
        if(distance>=length-right)return rightFoot.x-(length-distance)/unit;
        return leftFoot.x+endCaps.x+(distance-left)/(length-left-right)*(pixels-endCaps.x-endCaps.y);
    }
}

/// <summary>
/// 区間の準備時に橋スキンを抽選し、画像・形状・特殊動作の設定を渡す。
/// </summary>
[DisallowMultipleComponent]
public sealed class BridgeSkinLibrary : MonoBehaviour
{
    [SerializeField] private BridgeSkinDefinition[] skins = new BridgeSkinDefinition[0];
    [SerializeField] private Material skinMaterial;
    [SerializeField, Range(0.05f, 0.9f)] private float contractedLengthRatio = 0.35f;
    [SerializeField, Min(0.01f)] private float extensionSpeedMultiplier = 1f;
    [Header("Moving Walkway decoration only")]
    [Tooltip("Approximate world-X shift of the central belt artwork only. Fixed end caps, mesh, support surface and judgement stay in place. The shift is reduced when contracted to prevent texture folding.")]
    [SerializeField] private float movingWalkwayVisualOffsetX = -0.05f;
    [SerializeField] private bool drawGeometryGizmos;
    private readonly System.Random random = new System.Random(); // 傾斜の抽選とは独立した乱数を使う。
    private bool[] valid;
    private bool checkedSettings;
    public BridgeSkinDefinition Active { get; private set; }
    public Material SkinMaterial => skinMaterial;
    public float ContractedLengthRatio => Mathf.Clamp(contractedLengthRatio, 0.05f, 0.9f);
    public float ExtensionSpeedMultiplier => Mathf.Max(0.01f, extensionSpeedMultiplier);
    public float MovingWalkwayVisualOffsetX => float.IsNaN(movingWalkwayVisualOffsetX)
        || float.IsInfinity(movingWalkwayVisualOffsetX) ? 0f : movingWalkwayVisualOffsetX;
    public float MaximumLength { get; private set; }

    public void SelectForSection(float length)
    {
        MaximumLength = length;
        if(skinMaterial==null)
        {
            Active=null;
            if(!checkedSettings)Debug.LogWarning("[BridgeJust] Skin Material is missing. Using the original board and route.",this);
            checkedSettings=true;return;
        }
        if(skins==null) skins=new BridgeSkinDefinition[0];
        if (!checkedSettings)
        {
            valid = new bool[skins.Length];
            for (int i=0; i<skins.Length; i++)
            {
                var s=skins[i];
                if (s == null) { Debug.LogWarning($"[BridgeJust] Bridge skin {i}: missing definition; skipped.",this); continue; }
                if (!s.enabled) continue;
                valid[i] = s.Validate(out string reason);
                if (!valid[i]) Debug.LogWarning($"[BridgeJust] Bridge skin {i} ({s.id}): {reason}; skipped.",this);
            }
            checkedSettings=true;
        }
        double total=0;
        for(int i=0;i<skins.Length;i++) if(valid[i]) total+=skins[i].weight;
        Active=null;
        if(total<=0) { Debug.LogWarning("[BridgeJust] No usable bridge skins. Using the original board.",this); return; }
        double draw=random.NextDouble()*total;
        for(int i=0;i<skins.Length;i++) if(valid[i]) { draw-=skins[i].weight; if(draw<=0) { Active=skins[i]; break; } }
    }

    public void ResetForRun() { Active=null; MaximumLength=0; checkedSettings=false; }

    // 同じスキンを再抽選せず復元する。途中再開で次回以降の抽選順を進めないため。
    public void RestoreSection(BridgeSkinDefinition skin, float maximumLength)
    {
        Active = skin;
        MaximumLength = maximumLength;
    }
    private void OnValidate() { checkedSettings=false; }

    private void OnDrawGizmosSelected()
    {
        if (!drawGeometryGizmos || !Application.isPlaying || Active==null) return;
        var manager=FindFirstObjectByType<GameManager>();
        if(manager==null) return;
        var shape=manager.CurrentBridgeSurface;
        Gizmos.color=Color.cyan;
        Vector3 previous=shape.SkinPoint(Active.topSurface[0]);
        for(int i=1;i<Active.topSurface.Length;i++) {
            Vector3 point=shape.SkinPoint(Active.topSurface[i]); Gizmos.DrawLine(previous,point); previous=point;
        }
        Gizmos.color=Color.yellow;
        Gizmos.DrawSphere(shape.SkinPoint(Active.leftFoot),.025f);
        Gizmos.DrawSphere(shape.SkinPoint(Active.rightFoot),.025f);
    }
}

/// <summary>
/// 再利用する表示枠ごとに専用メッシュを持ち、橋スキンの画像を実際の形に合わせて描画する。
/// </summary>
public sealed class BridgeSkinVisual : IDisposable
{
    private const int Columns=128;
    private const int Nodes=Columns+4;
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly MeshRenderer renderer;
    private readonly Vector3[] vertices=new Vector3[Nodes*2];
    private readonly Vector2[] uv=new Vector2[Nodes*2];
    private readonly Color[] colors=new Color[Nodes*2];
    private readonly float[] columns=new float[Nodes];
    private BridgeSkinDefinition definition;
    private float lastLength=-1, lastMaximum=-1;
    private Vector3 lastAxis, lastLeftAxis;
    private float lastMin;
    private float lastVisualOffset;
    private Vector3 origin;
    private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();

    public BridgeSkinVisual(Transform parent)
    {
        root=new GameObject("Authored bridge skin"); root.transform.SetParent(parent,false);
        mesh=new Mesh { name="Bridge skin instance mesh" }; mesh.MarkDynamic();
        root.AddComponent<MeshFilter>().sharedMesh=mesh;
        renderer=root.AddComponent<MeshRenderer>();
        int[] triangles=new int[(Nodes-1)*6];
        for(int i=0;i<Nodes-1;i++) { int a=i*2,t=i*6; triangles[t]=a;triangles[t+1]=a+1;triangles[t+2]=a+2;triangles[t+3]=a+2;triangles[t+4]=a+1;triangles[t+5]=a+3; }
        mesh.vertices=vertices; mesh.triangles=triangles;
        root.SetActive(false);
    }
    public void Show(bool show) => root.SetActive(show);
    public bool Visible => root.activeSelf;
    public float TopY => renderer.bounds.max.y;
    public void Configure(BridgeBoardSurface shape, SpriteRenderer source, Material material)
    {
        var skin=shape.Skin;
        float length=shape.Max-shape.Min;
        float visualOffset=skin.id=="BridgeJust_Moving_Walkway"
            && skin.motion==BridgeSkinMotion.RightAnchoredExtension
            && source.TryGetComponent(out BridgeSkinLibrary library) ? library.MovingWalkwayVisualOffsetX : 0f;
        bool changed=definition!=skin || lastLength!=length || lastMaximum!=shape.SkinMaximumLength
            || lastMin!=shape.Min || lastAxis!=shape.Axis || lastLeftAxis!=shape.LeftAxis
            || lastVisualOffset!=visualOffset;
        origin=shape.Origin; root.transform.position=origin;
        if(changed)
        {
            Rect rect=skin.sprite.rect;
            for(int i=0;i<=Columns;i++)columns[i]=rect.width*i/Columns;
            columns[Columns+1]=skin.leftFoot.x+skin.endCaps.x;
            columns[Columns+2]=skin.rightFoot.x-skin.endCaps.y;
            columns[Columns+3]=Mathf.Clamp(skin.SpanToPixel(-shape.Min,length,shape.SkinMaximumLength),0,rect.width);
            Array.Sort(columns);
            for(int i=0;i<Nodes;i++)
            {
                float x=columns[i];
                for(int j=0;j<2;j++) {
                    int v=i*2+j; Vector2 pixel=new Vector2(x,rect.height*j);
                    vertices[v]=shape.SkinPoint(pixel)-origin;
                    // 位置補正は中央部分の画像の貼り付け位置だけに適用する。
                    // 形状自体を動かすと、右端の接続点やキャラクターの歩行面がずれてしまう。
                    float textureX=WalkwayArtworkX(x,skin,shape,length,visualOffset);
                    uv[v]=new Vector2((rect.x+textureX)/skin.sprite.texture.width,(rect.y+pixel.y)/skin.sprite.texture.height);
                    colors[v]=Color.white;
                }
            }
            mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.RecalculateBounds();
            definition=skin;lastLength=length;lastMaximum=shape.SkinMaximumLength;
            lastMin=shape.Min;lastAxis=shape.Axis;lastLeftAxis=shape.LeftAxis;
            lastVisualOffset=visualOffset;
        }
        root.layer=source.gameObject.layer;
        renderer.sharedMaterial=material;
        renderer.sortingLayerID=source.sortingLayerID;renderer.sortingOrder=source.sortingOrder;
        renderer.renderingLayerMask=source.renderingLayerMask;
        properties.SetTexture("_MainTex",skin.sprite.texture);
        properties.SetColor("_Color",Color.white); properties.SetColor("_RendererColor",Color.white);
        renderer.SetPropertyBlock(properties);
        root.SetActive(true);
    }
    private static float WalkwayArtworkX(float pixelX,BridgeSkinDefinition skin,BridgeBoardSurface shape,float length,float worldOffsetX)
    {
        if(worldOffsetX==0f)return pixelX;
        float from=skin.leftFoot.x+skin.endCaps.x,to=skin.rightFoot.x-skin.endCaps.y;
        if(pixelX<=from || pixelX>=to)return pixelX;
        float sourceWidth=to-from;
        float middleLength=length-(skin.endCaps.x+skin.endCaps.y)
            *shape.SkinMaximumLength/(skin.rightFoot.x-skin.leftFoot.x);
        if(middleLength<=0f || Mathf.Abs(shape.Axis.x)<.001f)return pixelX;
        // 画像を左に見せるには、参照する画像座標を右へずらす。
        // 補正を端に近づくほど弱め、最短時にも画像が逆転・折り返ししない範囲に制限する。
        float shift=Mathf.Clamp(-worldOffsetX/shape.Axis.x/middleLength*sourceWidth,-sourceWidth*.1f,sourceWidth*.1f);
        float t=(pixelX-from)/sourceWidth;
        float window=Mathf.Sin(Mathf.PI*t);window*=window;
        return pixelX+shift*window;
    }
    public void SetAlpha(float alpha)
    {
        properties.SetColor("_Color",new Color(1,1,1,alpha));
        properties.SetColor("_RendererColor",Color.white);
        renderer.SetPropertyBlock(properties);
    }
    public void Dispose() { UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(root); }
}
