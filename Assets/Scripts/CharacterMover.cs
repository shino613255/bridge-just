using System;
using UnityEngine;

/// <summary>
/// 親Circleを指定された経路に沿って移動させ、移動距離に応じて転がし、到着を1回通知する。
/// GameManagerが経路と開始タイミングを決め、このクラスは実際の移動を担当する。
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterMover : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float moveSpeed = 3f;

    [Header("Rolling")]
    [Tooltip("1 = natural rolling from distance/radius; 0 = no rotation. Does not affect movement speed.")]
    [SerializeField, Min(0f)] private float rotationMultiplier = 1f;

    // 到着時の通知。GameManagerはこの通知を受けて加点するため、移動中にはスコアが増えない。
    public event Action CrossingFinished;
    public bool IsCrossing { get; private set; }
    public float InitialX => initialPosition.x;
    public Vector3 InitialPosition => initialPosition;
    public bool HasValidSpeed => IsFinite(moveSpeed) && moveSpeed > 0f;
    public float MoveSpeed => moveSpeed;
    public float RollingMultiplier => rotationMultiplier;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 destination;
    private float rollingRadius;
    private Vector3[] path;
    private int waypoint;

    private void Awake()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;

        // 顔を含めず、丸本体のローカルBoundsとScaleから半径を求める。
        // 回転すると変わるワールドの外接矩形を使わず、プレイ中は一定の半径を保つ。
        SpriteRenderer body = GetComponent<SpriteRenderer>();
        if (body != null && body.sprite != null)
        {
            rollingRadius = body.localBounds.extents.x * Mathf.Abs(transform.lossyScale.x);
        }

        if (!IsFinite(rollingRadius) || rollingRadius <= 0f)
        {
            Debug.LogWarning("[BridgeJust] CharacterMover: Circle needs its own SpriteRenderer with positive width for rolling. Movement will continue without rotation.", this);
        }
    }

    public bool BeginCross(float destinationX)
    {
        if (!IsFinite(destinationX) || destinationX <= transform.position.x) return false;
        Vector3 target = transform.position;
        target.x = destinationX;
        return BeginCross(new[] { target });
    }

    public bool BeginCross(Vector3[] points)
    {
        return BeginCross(points, Vector3.right);
    }

    // 経路が正しいか確認してからコピーする。移動中に呼び出し元が配列を書き換えても経路が変わらない。
    // forwardAxisへの内積で「前へ進んでいるか」を調べるので、斜めの経路にも対応できる。
    public bool BeginCross(Vector3[] points, Vector3 forwardAxis)
    {
        if (!isActiveAndEnabled || IsCrossing || !HasValidSpeed || points == null || points.Length == 0
            || !IsFinite(forwardAxis.x) || !IsFinite(forwardAxis.y) || !IsFinite(forwardAxis.z)
            || forwardAxis.sqrMagnitude < 0.5f)
        {
            Debug.LogError("[BridgeJust] CharacterMover: Enable Circle, set a positive Move Speed, and use a destination to its right.", this);
            return false;
        }

        Vector3 previous = transform.position;
        forwardAxis = forwardAxis.normalized;
        foreach (Vector3 point in points)
        {
            if (!IsFinite(point.x) || !IsFinite(point.y) || !IsFinite(point.z)
                || Vector3.Dot(point - previous, forwardAxis) < -0.001f)
                return false;
            previous = point;
        }
        path = (Vector3[])points.Clone();
        waypoint = 0;
        destination = path[0];
        IsCrossing = true;
        return true;
    }

    // 毎フレーム、今回使える移動距離だけ経路を進み、進んだ実距離を回転へ反映する。
    private void Update()
    {
        if (!IsCrossing)
        {
            return;
        }

        if (!HasValidSpeed)
        {
            IsCrossing = false;
            Debug.LogError("[BridgeJust] CharacterMover: Move Speed became invalid during crossing.", this);
            return;
        }

        // 1フレームで進める距離を経路上の複数の点に分配する。
        // 点に着くたびに余りを次の点へ使うため、細かい曲線でも点ごとに停止しない。
        float remaining = moveSpeed * Time.deltaTime;
        while (IsCrossing)
        {
            Vector3 previous = transform.position;
            Vector3 next = Vector3.MoveTowards(previous, destination, remaining);
            // Vector3の等値比較には微小な許容差があるため、到着とみなしたら終点の座標を正確に代入する。
            transform.position = next == destination ? destination : next;
            float travelled = Vector3.Distance(previous, transform.position);
            ApplyRolling(travelled);
            remaining = Mathf.Max(0f, remaining - travelled);
            if (transform.position != destination) break;
            if (++waypoint < path.Length)
            {
                destination = path[waypoint];
                if (remaining <= 0f) break;
            }
            else
            {
                IsCrossing = false;
                path = null;
                // 先に移動中フラグと経路を解除し、最後に到着を通知する。これにより通知は横断1回につき1回になる。
                CrossingFinished?.Invoke();
            }
        }
    }

    // 円が転がる距離は半径×角度なので、その逆算で回転量を求める。倍率は見た目の回転だけに作用する。
    private void ApplyRolling(float distance)
    {
        if (!IsFinite(rollingRadius) || rollingRadius <= 0f
            || !IsFinite(rotationMultiplier) || rotationMultiplier < 0f || distance == 0f)
        {
            return;
        }

        // 回転角は「移動距離÷半径」で求める。ラジアンを度に直し、右へ転がる時計回りを負の角度で表す。
        float degrees = -(distance / rollingRadius) * Mathf.Rad2Deg * rotationMultiplier;
        if (IsFinite(degrees))
        {
            transform.Rotate(0f, 0f, degrees, Space.World);
        }
    }

    // 最初からのRetry用に、経路を捨てて位置と回転を起動時の姿勢へ戻す。
    public void ResetCharacter()
    {
        IsCrossing = false;
        path = null;
        transform.position = initialPosition;
        transform.rotation = initialRotation;
    }

    // 落下演出や途中再開へ制御を渡すため、位置と回転を保ったまま横断だけを中止する。
    public void CancelCrossing()
    {
        IsCrossing = false;
        path = null;
    }

    /// <summary>
    /// 区間移行中にワールド全体の位置をずらす。転がった角度は初期化しない。
    /// </summary>
    public void ShiftWorldX(float delta)
    {
        ShiftWorld(Vector3.right * delta);
    }

    public void ShiftWorld(Vector3 delta)
    {
        if (IsCrossing || !IsFinite(delta.x) || !IsFinite(delta.y) || !IsFinite(delta.z)) return;
        transform.position += delta;
        destination += delta;
    }

    // 横断中ではないときだけ外部から位置を調整する。回転角は保つため、区間移行で回転が急に戻らない。
    public void PlaceAtStart(Vector3 position)
    {
        if (!IsCrossing) transform.position = position;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
