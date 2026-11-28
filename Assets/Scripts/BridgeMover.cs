using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 橋の移動・停止・長さ変更を担当する。
/// 読む順序：PrepareSlopeで移動軸を準備し、StartMovingからUpdateで動かし、Freezeで止める。
/// 成功判定と入力を許可するタイミングはGameManagerが担当する。
/// </summary>
[DisallowMultipleComponent]
public sealed class BridgeMover : MonoBehaviour
{
    [Header("最初の区間での移動範囲")]
    [Tooltip("最初の区間でのワールドXの範囲。以降の区間では、傾斜に沿った移動距離として使う。")]
    [SerializeField] private float minX = -1.2f;
    [SerializeField] private float maxX = 1.2f;
    [SerializeField, Min(0f)] private float speed = 1.2f;

    [Header("直接入力")]
    [Tooltip("GameManagerが入力を受け付けてFreeze()を呼ぶ場合はオフにする。")]
    [SerializeField] private bool handleStopInput = true;

    public bool IsMoving { get; private set; }

    public bool HandleStopInput
    {
        get => handleStopInput;
        set => handleStopInput = value;
    }

    private int direction = 1;
    private Vector3 initialPosition;
    private Vector3 initialScale;
    private float initialWorldWidth;
    // 位置は「原点 + 移動軸 × 移動距離」で表す。原点をずらせば、スクロール後も同じ往復処理を使える。
    private Vector3 trackOrigin;
    private Vector3 trackAxis = Vector3.right;
    private Quaternion initialRotation;
    private BridgeSkinLibrary skinLibrary;
    private SpriteRenderer ownRenderer;
    private float extensionMaximum, extensionMinimum;
    private Vector3 fixedRight;

    public bool IsExtending => skinLibrary != null && skinLibrary.Active != null
        && skinLibrary.Active.motion == BridgeSkinMotion.RightAnchoredExtension;

    public float ExtensionMinimum => extensionMinimum;
    public float ExtensionMaximum => extensionMaximum;
    public Vector3 FixedRight => fixedRight;

    // 区間開始時の位置・移動方向・伸縮状態。途中再開で同じ条件に戻すために使う。
    public struct RoundState
    {
        internal Vector3 position, scale, origin, axis, socket;
        internal Quaternion rotation;
        internal float speed, minimum, maximum;
        internal int direction;
    }

    public RoundState CaptureRoundState() => new RoundState
    {
        position = transform.position,
        rotation = transform.rotation,
        scale = transform.localScale,
        origin = trackOrigin,
        axis = trackAxis,
        socket = fixedRight,
        speed = speed,
        minimum = extensionMinimum,
        maximum = extensionMaximum,
        direction = direction
    };

    // 止めてから保存値を戻す。動き出すタイミングはGameManagerに任せる。
    public void RestoreRoundState(RoundState state)
    {
        Freeze();
        transform.SetPositionAndRotation(state.position, state.rotation);
        transform.localScale = state.scale;
        trackOrigin = state.origin;
        trackAxis = state.axis;
        fixedRight = state.socket;
        speed = state.speed;
        direction = state.direction;
        extensionMinimum = state.minimum;
        extensionMaximum = state.maximum;
    }

    public bool AutoStart { get; set; } = true;

    // InspectorのminX/maxXは最初の区間のワールドX。これを移動軸上の距離に直して使う。
    public float MinTravel => minX - initialPosition.x;
    public float MaxTravel => maxX - initialPosition.x;
    public float MinX => trackOrigin.x + trackAxis.x * MinTravel;
    public float MaxX => trackOrigin.x + trackAxis.x * MaxTravel;
    public float Travel => Vector3.Dot(transform.position - trackOrigin, trackAxis);

    // 起動時の位置・Scale・回転を基準として保存する。Retryや長さ変更はこの基準を使う。
    private void Awake()
    {
        initialPosition = transform.position;
        initialScale = transform.localScale;
        initialRotation = transform.rotation;
        trackOrigin = initialPosition;
        trackAxis = transform.right;
        skinLibrary = GetComponent<BridgeSkinLibrary>();
        ownRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        if (AutoStart)
        {
            StartMoving();
        }
    }

    // 通常の橋は一定速度で往復し、伸縮橋は専用の処理で伸び縮みする。
    private void Update()
    {
        if (!IsMoving)
        {
            return;
        }

        // 移動より先に入力を見て、停止を押したフレームに余分に動かないようにする。
        if (handleStopInput && StopWasPressed())
        {
            Freeze();
            return;
        }

        if (IsExtending) { AdvanceExtension(); return; }

        float travel = Travel;
        float lower = MinTravel;
        float upper = MaxTravel;
        double width = (double)upper - lower;
        if (!SettingsAreValid() || !IsFinite(travel)
            || travel < lower - 0.0001f || travel > upper + 0.0001f)
        {
            Freeze();
            Debug.LogWarning("BridgeMover: Invalid movement settings or travel outside the surface bounds. "
                + "Movement stopped without repositioning.", this);
            return;
        }

        // 往復の位置を「行きと帰りを合わせた距離」に直してから進める。
        // 範囲の2倍で余りを取って後半を折り返すので、長いフレームでも端で正しく跳ね返る。
        double offset = (double)Mathf.Clamp(travel, lower, upper) - lower;
        double phase = direction > 0 ? offset : 2.0 * width - offset;
        phase = (phase + (double)speed * Time.deltaTime) % (2.0 * width);
        float nextTravel = (float)(lower + (phase <= width ? phase : 2.0 * width - phase));
        direction = phase < width ? 1 : -1;
        transform.position = trackOrigin + trackAxis * nextTravel;
    }

    /// <summary>
    /// 今の位置から移動を始める。移動範囲や速度が不正なら始めない。
    /// </summary>
    public void StartMoving()
    {
        if (IsExtending) { IsMoving = extensionMaximum > extensionMinimum && speed >= 0; return; }
        float travel = Travel;
        if (!SettingsAreValid() || !IsFinite(travel)
            || travel < MinTravel - 0.0001f || travel > MaxTravel + 0.0001f)
        {
            Freeze();
            Debug.LogWarning("BridgeMover: Use finite initial Min X < Max X, non-negative Speed, "
                + "and travel limits containing the current surface coordinate. Position was preserved.", this);
            return;
        }

        IsMoving = true;
    }

    /// <summary>
    /// 移動を止める。位置・回転・長さは変えない。
    /// </summary>
    public void Freeze()
    {
        if (!IsMoving)
        {
            return;
        }

        IsMoving = false;
    }

    /// <summary>
    /// 最初からのRetry用に、位置・回転・移動方向を戻す。長さと速度はGameManagerが設定する。
    /// </summary>
    public void ResetBridge()
    {
        Freeze();
        transform.position = initialPosition;
        transform.rotation = initialRotation;
        trackOrigin = initialPosition;
        trackAxis = transform.right;
        direction = 1;
    }

    /// <summary>
    /// 止まっている橋を別の区間に置く。Inspectorの移動範囲は書き換えない。
    /// </summary>
    public bool PrepareSegment(float offsetFromFirstGap, float spawnX)
    {
        if (IsMoving || !IsFinite(offsetFromFirstGap) || !IsFinite(spawnX)
            || spawnX < minX + offsetFromFirstGap || spawnX > maxX + offsetFromFirstGap)
        {
            return false;
        }

        Vector3 position = initialPosition;
        position.x = spawnX;
        transform.position = position;
        transform.rotation = initialRotation;
        trackOrigin = position;
        trackAxis = transform.right;
        direction = 1;
        return true;
    }

    public void ShiftWorldX(float delta)
    {
        ShiftWorld(Vector3.right * delta);
    }

    // 止まっている橋を、移動の基準点や固定した右端も含めて平行移動する。
    public void ShiftWorld(Vector3 delta)
    {
        if (IsMoving || !IsFinite(delta.x) || !IsFinite(delta.y) || !IsFinite(delta.z)) return;
        trackOrigin += delta;
        transform.position += delta;
        fixedRight += delta;
    }

    // 新しい区間の傾斜を設定し、橋の下面を足場の上に載せる。長さを決めてから呼ぶ。
    public bool PrepareSlope(SpriteRenderer renderer, Vector3 surfaceCenter, float angle)
    {
        if (IsMoving || renderer == null || renderer.transform != transform || !IsFinite(angle)
            || !IsFinite(surfaceCenter.x) || !IsFinite(surfaceCenter.y) || !IsFinite(surfaceCenter.z)
            || MinTravel > 0f || MaxTravel < 0f) return false;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        // 橋の下面の中心を、左右の足場の上端の中間点に合わせる。
        transform.position += surfaceCenter - BridgeSlopeGeometry.BottomCenter(renderer);
        trackOrigin = transform.position;
        trackAxis = BridgeSlopeGeometry.Axis(angle);
        direction = 1;
        if (IsExtending)
        {
            // 最大長のときの右端を保存し、以後は右端を固定したまま左へ伸び縮みする。
            fixedRight = BridgeSlopeGeometry.BottomRight(renderer);
            extensionMinimum = extensionMaximum * skinLibrary.ContractedLengthRatio;
            var s = skinLibrary.Active;
            float caps = (s.endCaps.x + s.endCaps.y) / (s.rightFoot.x - s.leftFoot.x) * extensionMaximum;
            extensionMinimum = Mathf.Max(extensionMinimum, caps + extensionMaximum * 0.02f);
            ApplyExtensionLength(extensionMinimum);
        }
        return true;
    }

    // 通常の橋の開始位置を、移動軸上の距離で指定する。伸縮橋には使わない。
    public bool PlaceAtTravel(float travel)
    {
        if (IsExtending) return false;
        if (IsMoving || !IsFinite(travel) || travel < MinTravel || travel > MaxTravel) return false;
        transform.position = trackOrigin + trackAxis * travel;
        return true;
    }

    /// <summary>
    /// 橋の長さと速度を設定する。X方向のScaleだけを変え、厚みは保つ。
    /// </summary>
    public bool SetLengthAndSpeed(SpriteRenderer renderer, float worldLength, float moveSpeed)
    {
        if (IsMoving || renderer == null || renderer.transform != transform
            || !IsFinite(worldLength) || worldLength <= 0f
            || !IsFinite(moveSpeed) || moveSpeed < 0f || !SettingsAreValid())
        {
            Debug.LogError("BridgeMover: Stop the bridge, assign its own SpriteRenderer, "
                + "and use valid length, speed and movement bounds.", this);
            return false;
        }

        if (initialWorldWidth == 0f)
        {
            initialWorldWidth = BridgeSlopeGeometry.Length(renderer);
        }

        if (!IsFinite(initialWorldWidth) || initialWorldWidth <= 0f)
        {
            Debug.LogError("BridgeMover: The initial world width must be positive and finite.", this);
            return false;
        }

        Vector3 scale = initialScale;
        scale.x = initialScale.x * worldLength / initialWorldWidth;
        transform.localScale = scale;
        speed = moveSpeed;
        extensionMaximum = worldLength;
        return true;
    }

    // 右端を固定したまま、指定の長さになるようScaleと位置を変える。
    private void ApplyExtensionLength(float length)
    {
        Vector3 scale = initialScale;
        scale.x = initialScale.x * length / initialWorldWidth;
        transform.localScale = scale;
        transform.position += fixedRight - BridgeSlopeGeometry.BottomRight(ownRenderer);
    }

    // 伸縮橋は、位置ではなく長さを最短と最大の間で往復させる。
    private void AdvanceExtension()
    {
        double width = extensionMaximum - extensionMinimum;
        if (width <= 0 || !IsFinite(speed)) { Freeze(); return; }
        double offset = Mathf.Clamp(BridgeSlopeGeometry.Length(ownRenderer),
            extensionMinimum, extensionMaximum) - extensionMinimum;
        double phase = direction > 0 ? offset : 2 * width - offset;
        phase = (phase + speed * skinLibrary.ExtensionSpeedMultiplier * Time.deltaTime) % (2 * width);
        ApplyExtensionLength((float)(extensionMinimum + (phase <= width ? phase : 2 * width - phase)));
        direction = phase < width ? 1 : -1;
    }

    private bool SettingsAreValid()
    {
        return IsFinite(minX) && IsFinite(maxX) && minX < maxX
            && IsFinite(MinX) && IsFinite(MaxX) && MinX < MaxX
            && IsFinite(speed) && speed >= 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool StopWasPressed()
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.wasPressedThisFrame)
                {
                    return true;
                }
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return false;
        }

        Vector2 pointer = mouse.position.ReadValue();
        return pointer.x >= 0f && pointer.x < Screen.width
            && pointer.y >= 0f && pointer.y < Screen.height;
    }
}
