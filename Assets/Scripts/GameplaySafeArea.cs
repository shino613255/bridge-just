using UnityEngine;

/// <summary>
/// 端末のSafe Areaと余白に合わせて、ゲーム中のUIを画面内に配置する。
/// </summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class GameplaySafeArea : MonoBehaviour
{
    [SerializeField] private Vector2 padding = new Vector2(32f, 28f);
    private Rect lastSafe;
    private Vector2 lastScreen;
    private void OnEnable() => Apply();
    private void OnValidate() => Apply();
    private void Update()
    {
        if (lastSafe != Screen.safeArea || lastScreen != new Vector2(Screen.width, Screen.height)) Apply();
    }
    public static Rect Normalized(Rect safe, Vector2 screen)
    {
        if (screen.x <= 0 || screen.y <= 0) return new Rect(0, 0, 1, 1);
        return Rect.MinMaxRect(Mathf.Clamp01(safe.xMin / screen.x), Mathf.Clamp01(safe.yMin / screen.y),
            Mathf.Clamp01(safe.xMax / screen.x), Mathf.Clamp01(safe.yMax / screen.y));
    }
    private void Apply()
    {
        lastSafe = Screen.safeArea; lastScreen = new Vector2(Screen.width, Screen.height);
        Rect area = Normalized(lastSafe, lastScreen);
        var rt = (RectTransform)transform;
        rt.anchorMin = area.min; rt.anchorMax = area.max;
        rt.offsetMin = padding; rt.offsetMax = -padding;
        rt.localScale = Vector3.one;
    }
}
