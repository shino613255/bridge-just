using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 既存のCircle Child 1人の待機・獲得・経路追従・回転を管理する。シーン参照を保つため以前のクラス名を使用。
/// </summary>
[DisallowMultipleComponent]
public sealed class CircleCompanionStack : MonoBehaviour
{
    [SerializeField] private CharacterMover parentCircle;
    [SerializeField] private GameObject childObject;
    [Tooltip("Center-to-center distance along the current surface. Clamped only for body clearance and the landing platform.")]
    [SerializeField, Min(0.1f)] private float followDistance = 0.70f;
    [SerializeField, Min(0.01f)] private float settleSeconds = 0.15f;

    private Transform originalParent;
    private Vector3 originalPosition, originalScale, waitingBase;
    private Quaternion originalRotation;
    private CharacterMover childMover;
    private SpriteRenderer body;
    private float radius, parentRadius, rollingMultiplier;
    private bool captured, waiting, following;
    private Vector3[] path;
    private float[] distances;
    private float progress;
    private Vector3 pathOrigin, pathAxis;
    private Vector3[] leadPath;
    private float[] leadDistances;
    public int AcquiredCount { get; private set; }
    public bool IsJoining { get; private set; }
    public int WaitingStage => waiting ? 11 : 0;
    public bool IsFollowingPath => following;

    public struct RoundState
    {
        internal int acquired;
        internal bool waiting, active;
        internal Vector3 position, scale, waitingBase;
        internal Quaternion rotation;
        internal Transform parent;
    }

    public RoundState CaptureRoundState()
    {
        if (!Capture()) return default;
        Transform child = childObject.transform;
        return new RoundState { acquired = AcquiredCount, waiting = waiting, active = childObject.activeSelf,
            position = child.position, rotation = child.rotation, scale = child.localScale,
            parent = child.parent, waitingBase = waitingBase };
    }

    public void RestoreRoundState(RoundState state)
    {
        if (!Capture()) return;
        StopAllCoroutines();
        following = IsJoining = false;
        path = leadPath = null; distances = leadDistances = null; progress = 0f;
        AcquiredCount = state.acquired; waiting = state.waiting; waitingBase = state.waitingBase;
        Transform child = childObject.transform;
        child.SetParent(state.parent, false);
        child.localScale = state.scale;
        child.SetPositionAndRotation(state.position, state.rotation);
        childObject.SetActive(state.active);
        if (childMover != null) { childMover.CancelCrossing(); childMover.enabled = false; }
    }

    private bool Capture()
    {
        if (captured) return true;
        if (parentCircle == null || childObject == null || childObject == parentCircle.gameObject
            || !childObject.TryGetComponent(out body) || body.sprite == null)
        {
            Debug.LogError("[BridgeJust] Assign the existing Circle Child and parent Circle to the single companion component.", this);
            return false;
        }
        originalParent = childObject.transform.parent;
        originalPosition = childObject.transform.position;
        originalRotation = childObject.transform.rotation;
        originalScale = childObject.transform.localScale;
        childMover = childObject.GetComponent<CharacterMover>();
        radius = body.localBounds.extents.x * Mathf.Abs(childObject.transform.lossyScale.x);
        SpriteRenderer parentBody = parentCircle.GetComponent<SpriteRenderer>();
        parentRadius = parentBody.localBounds.extents.x * Mathf.Abs(parentCircle.transform.lossyScale.x);
        rollingMultiplier = childMover != null ? childMover.RollingMultiplier : parentCircle.RollingMultiplier;
        if (childMover != null) childMover.enabled = false; // 子供の移動はこのクラスにまとめるため、子供側の自動移動は止める。
        if (radius <= 0f || parentRadius <= 0f) return false;
        captured = true;
        return true;
    }

    public void ResetForRun()
    {
        following = waiting = IsJoining = false;
        AcquiredCount = 0; path = null; leadPath = null; leadDistances = null;
        if (!Capture()) return;
        Transform child = childObject.transform;
        child.SetParent(originalParent, false);
        child.localScale = originalScale;
        child.SetPositionAndRotation(originalPosition, originalRotation);
        childObject.SetActive(false);
    }

    public void PrepareStage(int stage, SpriteRenderer rightPlatform)
    {
        if (!Capture()) return;
        if (AcquiredCount == 1)
        {
            waitingBase = childObject.transform.position;
            return;
        }
        // 成功回数10回でステージ11を準備する。Perfectの加点数とは別に数える。
        if (stage != 11 || waiting || rightPlatform == null) return;
        float along = Mathf.Max(0f, BridgeSlopeGeometry.Length(rightPlatform) * 0.5f - radius - 0.06f);
        childObject.transform.rotation = originalRotation;
        childObject.transform.position = BridgeSlopeGeometry.TopCenter(rightPlatform)
            + rightPlatform.transform.right * along + rightPlatform.transform.up * radius;
        waitingBase = childObject.transform.position;
        waiting = true;
        childObject.SetActive(true);
    }

    public void UpdateWaitingSupport(SpriteRenderer left, SpriteRenderer board)
    {
        if ((!waiting && AcquiredCount == 0) || following || IsJoining || childObject == null) return;
        var surface = new BridgeBoardSurface(left, board);
        Vector2 p = surface.Coordinates(waitingBase);
        float height = surface.WaitingHeight(waitingBase, radius);
        Vector3 next = waitingBase + surface.Normal * (height - p.y);
        childObject.transform.position = next;
    }

    private float LandingS(BridgeBoardSurface surface, SpriteRenderer right)
    {
        Vector2 landing = BridgeSlopeGeometry.Interval(right, surface.Origin, surface.Axis);
        float parentS = Vector3.Dot(parentCircle.transform.position - surface.Origin, surface.Axis);
        float gap = Mathf.Max(followDistance, parentRadius + radius + 0.025f);
        // 子供の到着位置は、再利用される古い足場ではなく、親がいる新しい足場の範囲内に収める。
        return Mathf.Clamp(parentS - gap, landing.x + radius + 0.025f, landing.y - radius - 0.025f);
    }

    public bool BeginFollowing(SpriteRenderer left, SpriteRenderer right, SpriteRenderer board, Vector3 parentDestination, Vector3[] parentPath = null)
    {
        if (AcquiredCount == 0) return true;
        var surface = new BridgeBoardSurface(left, board);
        Vector2 landing = BridgeSlopeGeometry.Interval(right, surface.Origin, surface.Axis);
        float gap = Mathf.Max(followDistance, parentRadius + radius + 0.025f);
        float end = Mathf.Clamp(Vector3.Dot(parentDestination - surface.Origin, surface.Axis) - gap,
            landing.x + radius + 0.025f, landing.y - radius - 0.025f);
        Vector3[] points = surface.PathToSurfacePosition(left, right, childObject.transform.position, radius, end);
        if (!SetPath(points, surface.Origin, surface.Axis)) return false;
        leadPath=null; leadDistances=null;
        if(parentPath!=null && parentPath.Length>0)
        {
            leadPath=new Vector3[parentPath.Length+1];leadPath[0]=parentCircle.transform.position;
            Array.Copy(parentPath,0,leadPath,1,parentPath.Length);
            leadDistances=new float[leadPath.Length];
            for(int i=1;i<leadPath.Length;i++)leadDistances[i]=leadDistances[i-1]+Vector3.Distance(leadPath[i-1],leadPath[i]);
        }
        following = true;
        return true;
    }

    private bool SetPath(Vector3[] points, Vector3 origin, Vector3 axis)
    {
        if (points == null || points.Length == 0) return false;
        path = new Vector3[points.Length + 1];
        path[0] = childObject.transform.position;
        Array.Copy(points, 0, path, 1, points.Length);
        distances = new float[path.Length];
        for (int i = 1; i < path.Length; i++) distances[i] = distances[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        progress = 0f; pathOrigin = origin; pathAxis = axis;
        return true;
    }

    private float DistanceAtS(float s)
    {
        for (int i = 1; i < path.Length; i++)
        {
            float a = Vector3.Dot(path[i - 1] - pathOrigin, pathAxis);
            float b = Vector3.Dot(path[i] - pathOrigin, pathAxis);
            if (s <= b) return Mathf.Lerp(distances[i - 1], distances[i], Mathf.InverseLerp(a, b, s));
        }
        return distances[distances.Length - 1];
    }

    private void LateUpdate()
    {
        if (!following || !parentCircle.IsCrossing || path == null) return;
        float gap = Mathf.Max(followDistance, parentRadius + radius + 0.025f);
        float parentS = Vector3.Dot(parentCircle.transform.position - pathOrigin, pathAxis);
        float s = parentS-gap;
        if(leadPath!=null)
        {
            // 親が通った経路に沿って追従距離を測り、子供自身の半径に合う経路上の位置へ移動する。
            // 曲線を直線で横切ってめり込まないようにする。
            int i=1;
            while(i<leadPath.Length-1 && Vector3.Dot(leadPath[i]-pathOrigin,pathAxis)<parentS)i++;
            float a=Vector3.Dot(leadPath[i-1]-pathOrigin,pathAxis), b=Vector3.Dot(leadPath[i]-pathOrigin,pathAxis);
            float target=Mathf.Max(0f,Mathf.Lerp(leadDistances[i-1],leadDistances[i],Mathf.InverseLerp(a,b,parentS))-gap);
            i=1;while(i<leadPath.Length-1 && leadDistances[i]<target)i++;
            s=Vector3.Dot(Vector3.Lerp(leadPath[i-1],leadPath[i],Mathf.InverseLerp(leadDistances[i-1],leadDistances[i],target))-pathOrigin,pathAxis);
        }
        MoveAlongPath(DistanceAtS(s), parentCircle.MoveSpeed * Time.deltaTime);
    }

    private void MoveAlongPath(float target, float budget)
    {
        float next = Mathf.MoveTowards(progress, Mathf.Max(progress, target), Mathf.Max(0f, budget));
        int i = 1;
        while (i < distances.Length - 1 && distances[i] < next) i++;
        childObject.transform.position = Vector3.Lerp(path[i - 1], path[i],
            Mathf.InverseLerp(distances[i - 1], distances[i], next));
        float travelled = next - progress;
        if (travelled > 0f)
            childObject.transform.Rotate(0f, 0f, -travelled / radius * Mathf.Rad2Deg * rollingMultiplier, Space.World);
        progress = next;
    }

    public IEnumerator FinishArrival(int successes, SpriteRenderer left, SpriteRenderer right, SpriteRenderer board)
    {
        if (IsJoining) yield break;
        bool recruit = waiting && successes == 11 && AcquiredCount == 0;
        if (recruit)
        {
            waiting = false; IsJoining = true; // 待ち時間に入る前に獲得処理を開始済みにし、二重に仲間へ加わるのを防ぐ。
            var surface = new BridgeBoardSurface(left, board);
            Vector3[] arrival = surface.PathToSurfacePosition(left, right, childObject.transform.position, radius,
                LandingS(surface, right));
            // 仲間になるときは停止中の親の上を跳び越え、親の体を通り抜けないようにする。
            // 出発点と着地点はどちらも子供の半径に合った支持面上に置く。
            if (arrival != null)
            {
                Vector2 parent = surface.Coordinates(parentCircle.transform.position);
                float clearance = parentRadius + radius + 0.025f;
                for (int i = 0; i < arrival.Length; i++)
                {
                    Vector2 point = surface.Coordinates(arrival[i]);
                    float dx = point.x - parent.x;
                    if (Mathf.Abs(dx) < clearance)
                    {
                        float top = parent.y + Mathf.Sqrt(clearance * clearance - dx * dx);
                        arrival[i] += surface.Normal * Mathf.Max(0f, top - point.y);
                    }
                }
            }
            if (!SetPath(arrival, surface.Origin, surface.Axis))
            {
                IsJoining = false;
                Debug.LogError("[BridgeJust] Circle Child could not reach a supported landing position.", this);
                yield break;
            }
        }
        if (following || recruit)
        {
            while (progress < distances[distances.Length - 1] - 0.00001f)
            {
                MoveAlongPath(distances[distances.Length - 1], parentCircle.MoveSpeed * Time.deltaTime);
                yield return null;
            }
            MoveAlongPath(distances[distances.Length - 1], float.MaxValue);
            if (recruit) AcquiredCount = 1;
            following = IsJoining = false;
            path = null; leadPath=null;leadDistances=null; waitingBase = childObject.transform.position;
        }
    }

    // 完成した橋が消えてから、同じ足場の上面へ滑らかに降ろす。
    // 子供が橋を渡っている間は、使い終わった足場を移動しない。
    public IEnumerator SettleOnPlatform(SpriteRenderer platform)
    {
        if (AcquiredCount == 0) yield break;
        Vector3 start = childObject.transform.position;
        float along = Vector3.Dot(start - BridgeSlopeGeometry.TopCenter(platform), platform.transform.right);
        Vector3 target = BridgeSlopeGeometry.TopCenter(platform) + platform.transform.right * along + platform.transform.up * radius;
        target.z = start.z;
        float elapsed = 0f;
        while (elapsed < settleSeconds)
        {
            elapsed += Time.deltaTime;
            childObject.transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / Mathf.Max(0.01f, settleSeconds))));
            yield return null;
        }
        childObject.transform.position = target;
        waitingBase = target;
    }

    public void ShiftWorld(Vector3 delta)
    {
        if (!captured || (!waiting && AcquiredCount == 0)) return;
        childObject.transform.position += delta;
        waitingBase += delta; pathOrigin += delta;
        if (path != null) for (int i = 0; i < path.Length; i++) path[i] += delta;
        if (leadPath != null) for (int i=0;i<leadPath.Length;i++) leadPath[i]+=delta;
    }

    public void CancelWaiting()
    {
        waiting = following = IsJoining = false; path = null; leadPath=null;leadDistances=null;
        if (AcquiredCount == 0 && childObject != null) childObject.SetActive(false);
    }

    /// <summary>
    /// 獲得済みの子供だけ、失敗演出へ移動の制御を渡す。
    /// </summary>
    public bool TakeFailureControl(out Transform child,out float bodyRadius,out float rolling,out float spacing)
    {
        child=null;bodyRadius=0f;rolling=0f;spacing=followDistance;
        bool acquired=AcquiredCount==1 && Capture() && childObject.activeInHierarchy;
        CancelWaiting(); // 通常の追従更新を解除する。獲得済みの子供は落下を見せるため表示を保つ。
        if(!acquired)return false;
        child=childObject.transform;bodyRadius=radius;rolling=rollingMultiplier;
        return true;
    }
}
