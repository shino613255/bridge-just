using System.Collections;
using UnityEngine;

/// <summary>
/// キャラクター到着後の横スクロールと、画面内に固定する背景オブジェクトの位置を管理する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraFollow : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float scrollSeconds = 0.6f;
    [Tooltip("画面に固定しておく飾り（任意）。カメラの子の背景やUIは入れなくてよい。")]
    [SerializeField] private Transform[] screenFixedWorldObjects = new Transform[0];
    [Tooltip("足場・橋・キャラを画面上でどれだけ上に表示するか（ワールド単位）。背景と固定した飾りは動かない。")]
    [SerializeField] private float stageScreenRaise = 0.5f;

    private Camera view;
    private Vector3 initialPosition;
    private Vector3[] backgroundPositions;
    private bool captured;
    public bool IsScrolling { get; private set; }
    public float InitialX => initialPosition.x;
    public float ViewLeftX
        => view.ViewportToWorldPoint(new Vector3(0f, 0.5f, view.nearClipPlane)).x;
    public float ViewRightX
        => view.ViewportToWorldPoint(new Vector3(1f, 0.5f, view.nearClipPlane)).x;

    private void Awake()
    {
        view = GetComponent<Camera>();
        // カメラを下げると、足場・橋・キャラは画面上で上へ移る。
        // 固定した飾りは同じだけ下げ、画面上の位置を変えない。
        Vector3 lower = Vector3.down * (Finite(stageScreenRaise) ? stageScreenRaise : 0f);
        initialPosition = transform.position + lower;
        backgroundPositions = new Vector3[screenFixedWorldObjects.Length];
        for (int i = 0; i < screenFixedWorldObjects.Length; i++)
        {
            if (screenFixedWorldObjects[i] != null)
                backgroundPositions[i] = screenFixedWorldObjects[i].position + lower;
        }
        transform.position = initialPosition;
        captured = true;
        ApplyBackgroundPositions();
    }

    public bool ValidateSetup(out string error)
    {
        error = null;
        if (!captured || !isActiveAndEnabled || view == null || !view.enabled || !view.orthographic
            || Vector3.Dot(transform.right, Vector3.right) < 0.9999f)
            error = "CameraFollow needs the active, unrotated orthographic Main Camera.";
        else if (!Finite(scrollSeconds) || scrollSeconds <= 0f)
            error = "CameraFollow Scroll Seconds must be positive and finite.";
        else
        {
            for (int i = 0; i < screenFixedWorldObjects.Length; i++)
            {
                Transform item = screenFixedWorldObjects[i];
                if (item == null) continue;
                if (item == transform || transform.IsChildOf(item))
                {
                    error = "CameraFollow background entries must be assigned "
                        + "and must not contain the camera.";
                    break;
                }
                for (int j = 0; j < i; j++)
                {
                    if (screenFixedWorldObjects[j] != null
                        && (item == screenFixedWorldObjects[j]
                            || item.IsChildOf(screenFixedWorldObjects[j])
                            || screenFixedWorldObjects[j].IsChildOf(item)))
                    {
                        error = "Assign each background root once; do not include its children separately.";
                        break;
                    }
                }
                if (error != null) break;
            }
        }
        return error == null;
    }

    public bool TracksWorldObject(Transform item)
    {
        foreach (Transform root in screenFixedWorldObjects)
            if (root != null && (item.IsChildOf(root) || root.IsChildOf(item))) return true;
        return false;
    }

    // GameManagerが動かすので、区間の切り替えを止めればスクロールも止まる。
    public IEnumerator ScrollToX(float targetX)
    {
        if (IsScrolling || !Finite(targetX) || !ValidateSetup(out string error))
        {
            Debug.LogError("[BridgeJust] Camera scroll could not start. "
                + "Check CameraFollow setup and state.", this);
            yield break;
        }
        IsScrolling = true;
        Vector3 start = transform.position;
        float elapsed = 0f;
        while (elapsed < scrollSeconds)
        {
            if (!isActiveAndEnabled || !view.enabled)
            {
                IsScrolling = false;
                yield break;
            }
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / scrollSeconds));
            Vector3 position = start;
            position.x = Mathf.Lerp(start.x, targetX, t);
            transform.position = position;
            ApplyBackgroundPositions();
            yield return null;
        }
        Vector3 finish = start;
        finish.x = targetX;
        transform.position = finish;
        ApplyBackgroundPositions();
        IsScrolling = false;
    }

    private void LateUpdate() => ApplyBackgroundPositions();

    private void ApplyBackgroundPositions()
    {
        if (!captured) return;
        Vector3 offset = transform.position - initialPosition;
        for (int i = 0; i < screenFixedWorldObjects.Length; i++)
        {
            Transform item = screenFixedWorldObjects[i];
            if (item != null && !item.IsChildOf(transform))
                item.position = backgroundPositions[i] + offset;
        }
    }

    public void CancelScroll() => IsScrolling = false;

    public void ResetCamera()
    {
        IsScrolling = false;
        transform.position = initialPosition;
        ApplyBackgroundPositions();
    }

    public void ShiftWorldX(float delta)
    {
        if (IsScrolling || !Finite(delta)) return;
        transform.position += Vector3.right * delta;
        ApplyBackgroundPositions();
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
