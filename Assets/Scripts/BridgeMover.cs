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
    [Header("Initial movement range (surface units)")]
    [Tooltip("Initial limits become travel distances along the active slope. Existing values are preserved.")]
    [SerializeField] private float minX = -1.2f;
    [SerializeField] private float maxX = 1.2f;
    [SerializeField, Min(0f)] private float speed = 1.2f;

    [Header("Temporary Phase 2 input")]
    [Tooltip("Disable when GameManager takes ownership of input and calls Freeze().")]
    [SerializeField] private bool handleStopInput = true;

    public bool IsMoving { get; private set; }

    // GameManagerが入力をまとめて受け付けるときは、この直接入力を無効にする。
    public bool HandleStopInput
    {
        get => handleStopInput;
        set => handleStopInput = value;
    }

    private int direction = 1;
    private Vector3 initialPosition;
    private Vector3 initialScale;
    private float initialWorldWidth;
    // 位置は「原点＋移動軸×移動距離」で表す。原点をずらせば、横スクロール後も同じ往復処理を使える。
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

    // 区間開始時の位置・移動方向・伸縮状態を保存する。途中再開で同じ条件を復元するためのデータ。
    public struct RoundState
    {
        internal Vector3 position, scale, origin, axis, socket;
        internal Quaternion rotation;
        internal float speed, minimum, maximum;
        internal int direction;
    }

    // 途中再開用に、停止される前の区間開始姿勢と往復・伸縮の状態を取り出す。
    public RoundState CaptureRoundState() => new RoundState
    {
        position = transform.position, rotation = transform.rotation, scale = transform.localScale,
        origin = trackOrigin, axis = trackAxis, socket = fixedRight, speed = speed,
        minimum = extensionMinimum, maximum = extensionMaximum, direction = direction
    };

    // 一度停止してから保存値を戻す。移動の再開タイミングはGameManagerに任せる。
    public void RestoreRoundState(RoundState state)
    {
        Freeze();
        transform.SetPositionAndRotation(state.position, state.rotation);
        transform.localScale = state.scale;
        trackOrigin = state.origin; trackAxis = state.axis; fixedRight = state.socket;
        speed = state.speed; direction = state.direction;
        extensionMinimum = state.minimum; extensionMaximum = state.maximum;
    }

    public bool AutoStart { get; set; } = true;
    // Inspectorの範囲は最初の区間を基準にした値。以降は移動軸と原点をずらして再利用する。
    // 初期のワールドX座標の設定を、橋が移動する軸上の距離へ変換する。
    public float MinTravel => minX - initialPosition.x;
    public float MaxTravel => maxX - initialPosition.x;
    public float MinX => trackOrigin.x + trackAxis.x * MinTravel;
    public float MaxX => trackOrigin.x + trackAxis.x * MaxTravel;
    public float Travel => Vector3.Dot(transform.position - trackOrigin, trackAxis);

    // 作成時の位置・Scale・回転を保存する。以後のRetryや長さ変更はこの基準を使う。
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

    // 通常の橋は一定速度で往復し、伸縮橋は専用処理へ分岐する。停止中は何もしない。
    private void Update()
    {
        if (!IsMoving)
        {
            return;
        }

        // 移動より先に入力を確認し、停止を押したフレームに余分な移動が入るのを防ぐ。
        if (handleStopInput && StopWasPressed())
        {
            Freeze();
            return;
        }

        if(IsExtending) { AdvanceExtension(); return; }

        float travel = Travel;
        float lower = MinTravel;
        float upper = MaxTravel;
        double width = (double)upper - lower;
        if (!SettingsAreValid() || !IsFinite(travel) || travel < lower - 0.0001f || travel > upper + 0.0001f)
        {
            Freeze();
            Debug.LogWarning("BridgeMover: Invalid movement settings or travel outside the surface bounds. Movement stopped without repositioning.", this);
            return;
        }

        // 往復の位置を「行きと帰りを合わせた距離」に変換してから進める。
        // 範囲の2倍で余りを取り、後半を折り返すと、長いフレームでも境界を越えた分を正しく戻せる。
        double offset = (double)Mathf.Clamp(travel, lower, upper) - lower;
        double phase = direction > 0 ? offset : 2.0 * width - offset;
        // 速度×Time.deltaTimeが今回進む距離。実行フレーム数によらず、実時間あたりの速さを保つ。
        phase = (phase + (double)speed * Time.deltaTime) % (2.0 * width);
        float nextTravel = (float)(lower + (phase <= width ? phase : 2.0 * width - phase));
        direction = phase < width ? 1 : -1;
        transform.position = trackOrigin + trackAxis * nextTravel;
    }

    /// <summary>
    /// 現在の位置から移動を開始・再開する。移動範囲や速度が不正なら開始しない。
    /// </summary>
    public void StartMoving()
    {
        if(IsExtending) { IsMoving=extensionMaximum>extensionMinimum && speed>=0; return; }
        float travel = Travel;
        if (!SettingsAreValid() || !IsFinite(travel) || travel < MinTravel - 0.0001f || travel > MaxTravel + 0.0001f)
        {
            Freeze();
            Debug.LogWarning("BridgeMover: Use finite initial Min X < Max X, non-negative Speed, and travel limits containing the current surface coordinate. Position was preserved.", this);
            return;
        }

        IsMoving = true;
    }

    /// <summary>
    /// 移動フラグだけを解除する。何度呼んでも位置・回転・長さは変わらない。
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
    /// 最初からのRetryで位置・回転・移動方向を戻す。長さと速度はGameManagerが別途設定する。
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
    /// 停止中の橋を別の区間に配置する補助処理。Inspectorの移動範囲は書き換えない。
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

    // 停止中の橋だけを、移動軌道と固定右端も含めて平行移動する。
    public void ShiftWorld(Vector3 delta)
    {
        if (IsMoving || !IsFinite(delta.x) || !IsFinite(delta.y) || !IsFinite(delta.z)) return;
        trackOrigin += delta;
        transform.position += delta;
        fixedRight += delta;
    }

    // 新しい区間の傾斜と支持面を設定する。長さを先に決めてから呼び、橋の下面を足場へ載せる。
    public bool PrepareSlope(SpriteRenderer renderer, Vector3 surfaceCenter, float angle)
    {
        if (IsMoving || renderer == null || renderer.transform != transform || !IsFinite(angle)
            || !IsFinite(surfaceCenter.x) || !IsFinite(surfaceCenter.y) || !IsFinite(surfaceCenter.z)
            || MinTravel > 0f || MaxTravel < 0f) return false;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        // 橋の下面の中心を、足場の支持面の中心に一致させる。
        // 下面を実際のSpriteから求めるので、橋の厚み・拡縮・Pivotの違いも反映される。
        transform.position += surfaceCenter - BridgeSlopeGeometry.BottomCenter(renderer);
        trackOrigin = transform.position;
        trackAxis = BridgeSlopeGeometry.Axis(angle);
        direction = 1;
        if(IsExtending)
        {
            // 最大まで伸びた橋を基準に右端の接続位置を保存し、以後は右端を固定して左へ伸縮する。
            fixedRight=BridgeSlopeGeometry.BottomRight(renderer);
            extensionMinimum=extensionMaximum*skinLibrary.ContractedLengthRatio;
            var s=skinLibrary.Active;
            float caps=(s.endCaps.x+s.endCaps.y)/(s.rightFoot.x-s.leftFoot.x)*extensionMaximum;
            extensionMinimum=Mathf.Max(extensionMinimum,caps+extensionMaximum*0.02f);
            ApplyExtensionLength(extensionMinimum);
        }
        return true;
    }

    // 通常橋の開始位置を移動軸上の距離で指定する。範囲外や伸縮橋には適用しない。
    public bool PlaceAtTravel(float travel)
    {
        if(IsExtending) return false;
        if (IsMoving || !IsFinite(travel) || travel < MinTravel || travel > MaxTravel) return false;
        transform.position = trackOrigin + trackAxis * travel;
        return true;
    }

    /// <summary>
    /// 停止中に実際の橋長と速度を設定する。初期Scaleとの比率でX方向だけを拡縮し、厚みは保つ。
    /// </summary>
    public bool SetLengthAndSpeed(SpriteRenderer renderer, float worldLength, float moveSpeed)
    {
        if (IsMoving || renderer == null || renderer.transform != transform
            || !IsFinite(worldLength) || worldLength <= 0f
            || !IsFinite(moveSpeed) || moveSpeed < 0f || !SettingsAreValid())
        {
            Debug.LogError("BridgeMover: Stop the bridge, assign its own SpriteRenderer, and use valid length, speed and movement bounds.", this);
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

    // 右端を固定したまま橋長に合わせてX方向のScaleと位置を更新する。
    private void ApplyExtensionLength(float length)
    {
        Vector3 scale=initialScale;
        scale.x=initialScale.x*length/initialWorldWidth;
        transform.localScale=scale;
        transform.position+=fixedRight-BridgeSlopeGeometry.BottomRight(ownRenderer);
    }

    // 長さを往復する量として扱い、短い状態と最大長の間を伸縮する。左右への往復とは別の動き。
    private void AdvanceExtension()
    {
        double width=extensionMaximum-extensionMinimum;
        if(width<=0 || !IsFinite(speed)) { Freeze(); return; }
        double offset=Mathf.Clamp(BridgeSlopeGeometry.Length(ownRenderer),extensionMinimum,extensionMaximum)-extensionMinimum;
        double phase=direction>0?offset:2*width-offset;
        phase=(phase+speed*skinLibrary.ExtensionSpeedMultiplier*Time.deltaTime)%(2*width);
        ApplyExtensionLength((float)(extensionMinimum+(phase<=width?phase:2*width-phase)));
        direction=phase<width?1:-1;
    }

    // NaNや無限大、逆転した移動範囲などを拒否して、計算で不正な位置が生まれるのを防ぐ。
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
