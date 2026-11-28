using System;
using UnityEngine;

/// <summary>
/// 親Circleを経路に沿って動かし、進んだ距離に応じて転がし、到着を1回だけ知らせる。
/// 経路と開始のタイミングはGameManagerが決める。
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterMover : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float moveSpeed = 3f;

    [Header("転がり")]
    [Tooltip("1で進んだ距離どおりに自然に転がる。0で回転しない。移動の速さには影響しない。")]
    [SerializeField, Min(0f)] private float rotationMultiplier = 1f;

    // 到着の通知。GameManagerはこれを受けて加点するので、移動中はスコアが増えない。
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

        // 半径は顔を含めず、丸い本体の大きさから求める。
        // 回転で変わるワールドの外接矩形は使わず、プレイ中は同じ半径を使う。
        SpriteRenderer body = GetComponent<SpriteRenderer>();
        if (body != null && body.sprite != null)
        {
            rollingRadius = body.localBounds.extents.x * Mathf.Abs(transform.lossyScale.x);
        }

        if (!IsFinite(rollingRadius) || rollingRadius <= 0f)
        {
            Debug.LogWarning("[BridgeJust] CharacterMover: Circle needs its own SpriteRenderer "
                + "with positive width for rolling. Movement will continue without rotation.", this);
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

    // 経路を確認してからコピーする。呼び出し元があとで配列を書き換えても経路は変わらない。
    // forwardAxis方向に後戻りしていないかを見るので、斜めの経路にも使える。
    public bool BeginCross(Vector3[] points, Vector3 forwardAxis)
    {
        if (!isActiveAndEnabled || IsCrossing || !HasValidSpeed || points == null || points.Length == 0
            || !IsFinite(forwardAxis.x) || !IsFinite(forwardAxis.y) || !IsFinite(forwardAxis.z)
            || forwardAxis.sqrMagnitude < 0.5f)
        {
            Debug.LogError("[BridgeJust] CharacterMover: Enable Circle, set a positive Move Speed, "
                + "and use a destination to its right.", this);
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

        // 1フレームで進める距離を、経路の点から点へ順に使っていく。
        // 点に着いたら余りを次の点へ回すので、細かい曲線でも点ごとに止まらない。
        float remaining = moveSpeed * Time.deltaTime;
        while (IsCrossing)
        {
            Vector3 previous = transform.position;
            Vector3 next = Vector3.MoveTowards(previous, destination, remaining);
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
                // 移動中の状態を解除してから通知するので、通知は1回の横断につき1回だけ。
                CrossingFinished?.Invoke();
            }
        }
    }

    // 回転角 = 移動距離 ÷ 半径。右へ転がるときは時計回り（マイナスの角度）。
    private void ApplyRolling(float distance)
    {
        if (!IsFinite(rollingRadius) || rollingRadius <= 0f
            || !IsFinite(rotationMultiplier) || rotationMultiplier < 0f || distance == 0f)
        {
            return;
        }

        float degrees = -(distance / rollingRadius) * Mathf.Rad2Deg * rotationMultiplier;
        if (IsFinite(degrees))
        {
            transform.Rotate(0f, 0f, degrees, Space.World);
        }
    }

    // 最初からのRetry用に、起動時の位置と回転へ戻す。
    public void ResetCharacter()
    {
        IsCrossing = false;
        path = null;
        transform.position = initialPosition;
        transform.rotation = initialRotation;
    }

    // 位置と回転はそのままで、横断だけを止める。
    public void CancelCrossing()
    {
        IsCrossing = false;
        path = null;
    }

    /// <summary>
    /// ワールド全体を動かすときに使う。転がった角度は戻さない。
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

    // 横断中でなければ位置を直接置く。回転は変えないので、区間の切り替えで向きが急に戻らない。
    public void PlaceAtStart(Vector3 position)
    {
        if (!IsCrossing) transform.position = position;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
