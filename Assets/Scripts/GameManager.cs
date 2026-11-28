using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// ゲーム全体の進行をまとめる中心クラス。移動や描画の実作業は各担当クラスへ依頼する。
/// 読む順序：Start → BeginRun → PrepareNextRound → Update → RequestStop → OnCrossingFinished。
/// 成功時は次の区間へ進み、失敗時はPlayFailureを経てRetryRunまたはRetryStageへ進む。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class GameManager : MonoBehaviour
{
    // 通常の流れは「移動 → 判定 → 横断 → スクロール → 次区間の準備」。
    // 失敗時は「判定 → 落下演出 → GAME OVER」。Moving以外では橋を止める入力を受け付けない。
    public enum GameState { Moving, Judging, Result, Crossing, Transition, GameOver, Scrolling, Failing }
    public enum StopResult { Perfect, Success, Fail }

    [Header("シーン内の参照")]
    [SerializeField] private BridgeMover bridgeMover;
    [SerializeField] private SpriteRenderer leftPlatform;
    [SerializeField] private SpriteRenderer rightPlatform;
    [Tooltip("空ならBridgeMoverと同じGameObjectのSpriteRendererを使う。")]
    [SerializeField] private SpriteRenderer bridgeRenderer;
    [SerializeField] private CharacterMover characterMover;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private AudioManager audioManager;
    [Tooltip("Main CameraのCameraFollow。空ならCamera.mainから探す。")]
    [SerializeField] private CameraFollow cameraFollow;
    [Tooltip("背景テーマの切り替え（任意）。シーンの読み込みやゲームの設定値は変えない。")]
    [SerializeField] private BridgeJustSceneManager bridgeJustSceneManager;
    [SerializeField] private CircleCompanionStack companionStack;
    [SerializeField] private FailSequenceController failSequence;

    [Header("判定（ワールド単位）")]
    [SerializeField, Min(0.0001f)] private float minimumOverlap = 0.20f;
    [SerializeField, Min(0f)] private float perfectHalfWidth = 0.15f;

    // 難易度はSCOREではなくSuccessCountで計算する。Perfectも成功1回として数える。
    [Header("難易度（スコアではなく成功回数で変化）")]
    [SerializeField, Min(0.01f)] private float initialBridgeLength = 2.50f;
    [SerializeField, Min(0f)] private float lengthDecreasePerSuccess = 0.01f;
    [SerializeField, Min(0.01f)] private float minimumBridgeLength = 2.34f;
    [SerializeField, Min(0.01f)] private float initialSpeed = 1.20f;
    [SerializeField, Min(0f)] private float speedIncreasePerSuccess = 0.035f;
    [SerializeField, Min(0.01f)] private float maximumSpeed = 1.80f;
    [SerializeField, Min(0f)] private float transitionSeconds = 0.15f;

    [Header("配置（シーンに置いた位置からの補正）")]
    [Tooltip("足場などを上下にずらす量（ワールドY）。プレイ開始時の基準位置に一度だけ加える。")]
    [SerializeField] private float gameplayVerticalOffset = 0.5f;
    [Tooltip("足場の上でのCircleの開始位置を、シーンの配置から前へずらす量。足場の端では半径の分だけ制限される。")]
    [SerializeField] private float circleStartOffsetX = 0.15f;

    [Header("入力")]
    [Tooltip("UIの上をタップしたときは橋を止めない。EventSystemとGraphicRaycasterが必要。")]
    [SerializeField] private bool blockInputOverUI = true;

    // 小数計算の丸め誤差だけを吸収する値。難易度を緩めるための余白ではない。
    private const float BoundaryTolerance = 0.00001f;
    private const float MinimumSuccessHalfWidth = 0.01f;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private bool initialized;
    // 加点待ちと停止入力の許可を別々に持ち、連打や到着通知の重複でも1回だけ処理する。
    private bool awardPending;
    private bool inputArmed;
    private int inputUnlockFrame;
    private SpriteRenderer originalLeft;
    private SpriteRenderer originalRight;
    private SpriteRenderer nextPlatform;
    private SpriteRenderer pooledPlatform;
    private Vector3 originalLeftPosition;
    private Vector3 originalRightPosition;
    private Quaternion originalLeftRotation;
    private Quaternion originalRightRotation;
    private float initialGap;
    private float cameraOffsetFromGap;
    private bool initialBridgeHidden;
    private readonly BridgeSlopeSchedule slope = new BridgeSlopeSchedule();
    private bool slopeWorldReady;
    private BridgeSkinLibrary bridgeSkins;
    private Coroutine failureRoutine;
    private float failureStarted;
    private bool failureSoundPlayed;
    private float characterClearance;
    private float initialCharacterAlong;
    private float runCharacterY;
    private Vector3 waitingCharacterBase;
    private StageCheckpoint checkpoint;

    private struct WorldPose
    {
        private Vector3 position, scale;
        private Quaternion rotation;

        public WorldPose(Transform target)
        {
            position = target.position;
            rotation = target.rotation;
            scale = target.localScale;
        }

        public void Restore(Transform target)
        {
            target.SetPositionAndRotation(position, rotation);
            target.localScale = scale;
        }
    }

    // 途中再開用の保存データ。区間を操作できる状態になったら上書きし、失敗前の状態を残す。
    private sealed class StageCheckpoint
    {
        public int score, successes, best, successCombo, perfectCombo, theme;
        public SpriteRenderer left, right, next;
        public WorldPose leftPose, rightPose, nextPose, circlePose, cameraPose;
        public Vector3 waitingBase;
        public BridgeMover.RoundState bridge;
        public CircleCompanionStack.RoundState child;
        public BridgeSkinDefinition skin;
        public float maximumLength;
    }

    public float CurrentSlopeAngle => slope.Angle;
    public long NextSlopeChangeStage => slope.NextChangeStage;
    private Vector3 SlopeAxis => BridgeSlopeGeometry.Axis(CurrentSlopeAngle);
    private Vector3 SlopeNormal => BridgeSlopeGeometry.Normal(CurrentSlopeAngle);

    // スクロール中だけ前の橋を見せる固定数の表示枠。区間ごとに生成し続けない。
    private readonly SpriteRenderer[] completedBridges = new SpriteRenderer[2];
    private BridgeBoardVisual activeBoardVisual;
    private readonly BridgeBoardVisual[] completedBoardVisuals = new BridgeBoardVisual[2];
    private const float RecycleMargin = 0.05f;
    private const float RebaseDistance = 256f;

    public GameState CurrentState { get; private set; } = GameState.Result;
    public StopResult? LastResult { get; private set; }
    public int Score { get; private set; }
    public int SuccessCount { get; private set; }
    public int BestScore { get; private set; }
    public int SuccessComboCount { get; private set; }
    public int PerfectComboCount { get; private set; }

    public BridgeBoardSurface CurrentBridgeSurface
        => new BridgeBoardSurface(leftPlatform, bridgeRenderer);

    public bool CanRetryStage
        => initialized && checkpoint != null && CurrentState == GameState.GameOver;

    // Awakeで橋の自動動作を止めておき、Startで参照の確認とゲーム開始を行う。
    private void Awake()
    {
        if (bridgeMover != null)
        {
            bridgeMover.HandleStopInput = false;
            bridgeMover.AutoStart = false;
        }
    }

    private void Start()
    {
        if (bridgeRenderer == null && bridgeMover != null)
        {
            bridgeRenderer = bridgeMover.GetComponent<SpriteRenderer>();
        }
        if (bridgeMover != null) bridgeSkins = bridgeMover.GetComponent<BridgeSkinLibrary>();
        if (failSequence == null) failSequence = GetComponent<FailSequenceController>();

        if (cameraFollow == null && Camera.main != null)
        {
            cameraFollow = Camera.main.GetComponent<CameraFollow>();
        }

        if (bridgeJustSceneManager == null)
        {
            bridgeJustSceneManager = GetComponent<BridgeJustSceneManager>();
        }

        if (!ValidateSetup())
        {
            return;
        }

        if (!EnsureBridgeDrawOrder()) return;

        if (!InitializeScrollWorld())
        {
            return;
        }

        if (audioManager == null)
        {
            audioManager = GetComponent<AudioManager>();
        }

        bridgeMover.HandleStopInput = false;
        characterMover.CrossingFinished += OnCrossingFinished;
        uiManager.RetryRequested += RetryRun;
        uiManager.RetryStageRequested += RetryStage;
        initialized = true;
        if (bridgeJustSceneManager != null)
            bridgeJustSceneManager.Initialize(this, cameraFollow,
                leftPlatform, rightPlatform, bridgeRenderer,
                characterMover.GetComponent<SpriteRenderer>());
        BeginRun();
    }

    private void Update()
    {
        if (CurrentState == GameState.Failing)
        {
            // 失敗演出が途中で止まった場合も、時間上限でGAME OVERへ進める。
            float timeout = failSequence != null ? failSequence.SafetyTimeout : 8f;
            if (Time.unscaledTime - failureStarted > timeout + 0.1f)
            {
                if (failureRoutine != null) StopCoroutine(failureRoutine);
                if (failSequence != null) failSequence.Cancel();
                Debug.LogWarning("[BridgeJust] Failure watchdog completed GAME OVER.", this);
                FinishFailure();
            }
            return;
        }
        if (CurrentState == GameState.Crossing)
        {
            if (characterMover == null || !characterMover.isActiveAndEnabled
                || !characterMover.IsCrossing)
            {
                ConfigurationError("Circle stopped or was disabled before it reported arrival.");
            }
            return;
        }

        if (CurrentState != GameState.Moving)
        {
            return;
        }

        // 停止入力はこのクラスだけで受け、橋側と二重に処理しない。
        if (bridgeMover != null)
        {
            bridgeMover.HandleStopInput = false;
        }

        // 新しい区間では、指やマウスを一度離してから停止入力を許可する。
        // Retryのクリックがそのまま新しい橋の停止にならないようにするため。
        if (!inputArmed)
        {
            if (Time.frameCount > inputUnlockFrame && !AnyPointerActive())
            {
                inputArmed = true;
            }
            return;
        }

        if (StopWasPressed())
        {
            RequestStop();
        }
    }

    /// <summary>
    /// 停止操作の入口。移動中かつ入力許可済みのときだけ、停止・判定・結果別の処理を1回行う。
    /// </summary>
    public void RequestStop()
    {
        if (!isActiveAndEnabled || CurrentState != GameState.Moving || !inputArmed)
        {
            return;
        }

        UpdateWaitingSupport();
        // 先に判定中へ切り替え、連打で同じ区間を再判定しないようにする。
        CurrentState = GameState.Judging;
        inputArmed = false;
        if (!ValidateSetup())
        {
            return;
        }

        if (!bridgeMover.IsMoving)
        {
            ConfigurationError("BridgeMover is already stopped. "
                + "Check its bounds/settings and avoid other stop-input handlers.");
            return;
        }

        bridgeMover.Freeze();
        // 判定用の座標は、原点が左足場の右上端、軸が今回の橋の傾斜方向。
        // 軸に投影した距離で比べるので、水平・上り・下りを同じ計算で扱える。
        Vector3 origin = BridgeSlopeGeometry.TopRight(leftPlatform);
        Vector3 axis = SlopeAxis;
        Vector2 bridge = BridgeSlopeGeometry.BottomInterval(bridgeRenderer, origin, axis);
        Vector2 left = BridgeSlopeGeometry.Interval(leftPlatform, origin, axis);
        Vector2 right = BridgeSlopeGeometry.Interval(rightPlatform, origin, axis);
        float ideal = (left.y + right.x) * 0.5f;
        float offset = (bridge.x + bridge.y) * 0.5f - ideal;
        float leftOverlap = BridgeSlopeGeometry.Overlap(bridge, left);
        float rightOverlap = BridgeSlopeGeometry.Overlap(bridge, right);

        // 左右どちらも最低重なり幅以上なら成功。
        // そのうえで理想の中心からのずれが小さければPerfect。
        bool connected = leftOverlap + BoundaryTolerance >= minimumOverlap
            && rightOverlap + BoundaryTolerance >= minimumOverlap;
        StopResult result = !connected ? StopResult.Fail
            : Mathf.Abs(offset) <= perfectHalfWidth + BoundaryTolerance
                ? StopResult.Perfect : StopResult.Success;

        LastResult = result;
        Debug.Log($"[BridgeJust] {result.ToString().ToUpperInvariant()} "
            + $"| stopX={bridgeMover.transform.position.x:F6}, slope={CurrentSlopeAngle:F0}, idealS={ideal:F6}, "
            + $"offset={offset:F6}, bridgeWidth={bridge.y - bridge.x:F6}, "
            + $"leftOverlap={leftOverlap:F6}, rightOverlap={rightOverlap:F6}, "
            + $"minimumOverlap={minimumOverlap:F6}", this);

        if (audioManager != null && result != StopResult.Fail)
        {
            // コンボの確定は到着後なので、ここでは「次に確定するPerfect連続数」で音を選ぶ。
            audioManager.PlayResult(result, PerfectComboCount + 1);
        }

        if (result == StopResult.Fail)
        {
            ResetComboCounts();
            awardPending = false;
            CurrentState = GameState.Failing;
            failureStarted = Time.unscaledTime;
            failureSoundPlayed = false;
            uiManager.HideCombo();
            uiManager.HidePerfect();
            UpdateBoardVisual(); // 失敗演出には、停止した瞬間の橋の形を渡す。
            failureRoutine = StartCoroutine(PlayFailure(
                leftOverlap + BoundaryTolerance >= minimumOverlap,
                rightOverlap + BoundaryTolerance >= minimumOverlap));
            return;
        }

        awardPending = true;
        CurrentState = GameState.Crossing;
        if (result == StopResult.Perfect
            && (PerfectComboCount == 0 || !uiManager.ComboFeedbackAvailable))
        {
            uiManager.ShowPerfect();
        }

        // 終点は次の足場の同じ相対位置。経路は実際の橋・曲面の上を通る。
        // 橋の厚みや曲面で高くなった分は、完成橋が消えた後に足場へ降ろす。
        var crossingSurface = new BridgeBoardSurface(leftPlatform, bridgeRenderer);
        float landingS = crossingSurface.Coordinates(CharacterStartOn(rightPlatform)).x;
        Vector3[] crossingPath = crossingSurface.PathToSurfacePosition(
            leftPlatform, rightPlatform,
            characterMover.transform.position, characterClearance, landingS);
        if (crossingPath != null && crossingPath.Length > 0 && companionStack != null
            && !companionStack.BeginFollowing(leftPlatform, rightPlatform, bridgeRenderer,
                crossingPath[crossingPath.Length - 1], crossingPath))
        {
            ConfigurationError("Circle Child needs a continuous supported path. "
                + "Check its radius and Follow Distance.");
            return;
        }
        if (!characterMover.BeginCross(crossingPath, SlopeAxis))
        {
            ConfigurationError("Circle could not cross the board. "
                + "Check the landing area, bridge thickness and Move Speed.");
        }
    }

    // 到着通知を受けて、ここで初めて加点と成功回数を確定する。
    // 通常は1点、Perfectは2点。続いてUI・背景を更新し、スクロールへ進む。
    private void OnCrossingFinished()
    {
        if (!isActiveAndEnabled || CurrentState != GameState.Crossing || !awardPending)
        {
            return;
        }

        // 先に加点の権利を使い切り、到着通知が重なっても二重加点しない。
        awardPending = false;
        Score += LastResult == StopResult.Perfect ? 2 : 1;
        SuccessCount += 1;
        BestScore = Mathf.Max(BestScore, Score);
        SuccessComboCount += 1;
        PerfectComboCount = LastResult == StopResult.Perfect ? PerfectComboCount + 1 : 0;
        CurrentState = GameState.Scrolling;
        uiManager.UpdateScore(Score, BestScore);
        uiManager.ShowCombo(SuccessComboCount, PerfectComboCount);
        Debug.Log($"[BridgeJust] Arrival | SCORE={Score}, successfulCrossings={SuccessCount}, "
            + $"successCombo={SuccessComboCount}, perfectCombo={PerfectComboCount}", this);
        if (bridgeJustSceneManager != null)
            bridgeJustSceneManager.BeginSectionTransition(SuccessCount);
        StartCoroutine(ScrollToNextSection());
    }

    // 最初からの再挑戦。GAME OVER中だけ受け付ける。
    public void RetryRun()
    {
        if (!isActiveAndEnabled || !initialized || CurrentState != GameState.GameOver)
        {
            return;
        }

        BeginRun();
    }

    // 失敗した区間から再挑戦する。落下やスクロールを止めてから、保存した状態を戻す。
    public void RetryStage()
    {
        if (!isActiveAndEnabled || !CanRetryStage) return;
        StageCheckpoint saved = checkpoint;
        // 復元より先に入力を閉じ、ボタンの連打で途中再開を重ねて実行しない。
        CurrentState = GameState.Transition;
        inputArmed = awardPending = false;
        StopAllCoroutines();
        failureRoutine = null;
        failureSoundPlayed = false;
        if (failSequence != null) failSequence.Cancel();
        cameraFollow.CancelScroll();
        characterMover.CancelCrossing();
        bridgeMover.Freeze();

        leftPlatform = saved.left;
        rightPlatform = saved.right;
        nextPlatform = saved.next;
        saved.leftPose.Restore(leftPlatform.transform);
        saved.rightPose.Restore(rightPlatform.transform);
        saved.nextPose.Restore(nextPlatform.transform);
        saved.circlePose.Restore(characterMover.transform);
        saved.cameraPose.Restore(cameraFollow.transform);
        waitingCharacterBase = saved.waitingBase;
        Score = saved.score;
        SuccessCount = saved.successes;
        BestScore = Mathf.Max(BestScore, saved.best);
        SuccessComboCount = saved.successCombo;
        PerfectComboCount = saved.perfectCombo;
        LastResult = null;
        // 傾斜の予定は失敗中に進まないので、そのまま使う。
        // 橋スキンも再抽選せず、失敗した区間と同じ条件へ戻す。
        if (bridgeSkins != null) bridgeSkins.RestoreSection(saved.skin, saved.maximumLength);
        bridgeMover.RestoreRoundState(saved.bridge);
        if (companionStack != null) companionStack.RestoreRoundState(saved.child);
        foreach (SpriteRenderer completed in completedBridges) completed.gameObject.SetActive(false);
        foreach (BridgeBoardVisual visual in completedBoardVisuals) visual?.Show(false);
        UpdateBoardVisual(); // 崩落で動いた表示用の橋の位置・回転・透明度も戻す。
        SetBoardVisible(!initialBridgeHidden);
        if (bridgeJustSceneManager != null) bridgeJustSceneManager.RestoreRoundTheme(saved.theme);
        uiManager.ShowPlaying(Score, BestScore);
        if (audioManager != null) audioManager.PlayStartSound();
        StartCoroutine(ResumeCheckpoint());
    }

    private IEnumerator ResumeCheckpoint()
    {
        yield return null; // ボタンを押したフレームをまたいでから、橋の停止入力を解禁する。
        if (CurrentState != GameState.Transition || !ValidateSetup()) yield break;
        inputUnlockFrame = Time.frameCount;
        CurrentState = GameState.Moving;
        bridgeMover.StartMoving();
        if (!bridgeMover.IsMoving) ConfigurationError("The saved bridge could not restart.");
    }

    // 停止入力や落下で状態が変わる前に、現在の区間の開始状態を記録する。
    private void CaptureStageCheckpoint()
    {
        checkpoint = new StageCheckpoint
        {
            score = Score,
            successes = SuccessCount,
            best = BestScore,
            successCombo = SuccessComboCount,
            perfectCombo = PerfectComboCount,
            theme = bridgeJustSceneManager != null ? bridgeJustSceneManager.CurrentThemeIndex : -1,
            left = leftPlatform,
            right = rightPlatform,
            next = nextPlatform,
            leftPose = new WorldPose(leftPlatform.transform),
            rightPose = new WorldPose(rightPlatform.transform),
            nextPose = new WorldPose(nextPlatform.transform),
            circlePose = new WorldPose(characterMover.transform),
            cameraPose = new WorldPose(cameraFollow.transform),
            waitingBase = waitingCharacterBase,
            bridge = bridgeMover.CaptureRoundState(),
            child = companionStack != null ? companionStack.CaptureRoundState() : default,
            skin = bridgeSkins != null ? bridgeSkins.Active : null,
            maximumLength = bridgeSkins != null ? bridgeSkins.MaximumLength : 0f
        };
    }

    // 落下と崩落はFailSequenceControllerに任せ、終わったらGAME OVERを表示する。
    private IEnumerator PlayFailure(bool leftConnected, bool rightConnected)
    {
        if (failSequence != null && failSequence.isActiveAndEnabled)
            yield return failSequence.Play(characterMover, companionStack,
                leftPlatform, rightPlatform, bridgeRenderer, activeBoardVisual,
                leftConnected, rightConnected,
                cameraFollow.GetComponent<Camera>(), PlayFailureSound);
        else
        {
            if (companionStack != null) companionStack.CancelWaiting();
            Debug.LogWarning("[BridgeJust] FailSequenceController is missing or disabled; "
                + "showing GAME OVER safely.", this);
        }
        FinishFailure();
    }

    private void PlayFailureSound()
    {
        if (failureSoundPlayed) return;
        failureSoundPlayed = true;
        if (audioManager != null) audioManager.PlayResult(StopResult.Fail);
    }

    private void FinishFailure()
    {
        if (CurrentState != GameState.Failing) return;
        PlayFailureSound();
        failureRoutine = null;
        CurrentState = GameState.GameOver;
        uiManager.ShowGameOver(Score, BestScore);
    }

    // 新しいプレイの初期化。数値・親子・足場・カメラを戻し、傾斜を抽選して最初の区間を準備する。
    private void BeginRun()
    {
        checkpoint = null;
        if (failureRoutine != null)
        {
            StopCoroutine(failureRoutine);
            failureRoutine = null;
        }
        if (failSequence != null) failSequence.Cancel();
        failureSoundPlayed = false;
        if (bridgeSkins != null) bridgeSkins.ResetForRun();
        if (companionStack != null) companionStack.ResetForRun();
        CurrentState = GameState.Transition;
        inputArmed = false;
        awardPending = false;
        Score = 0;
        SuccessCount = 0;
        ResetComboCounts();
        LastResult = null;
        bridgeMover.Freeze();
        ResetScrollWorld();
        bridgeMover.ResetBridge();
        SetBoardVisible(false);
        characterMover.ResetCharacter();
        slope.Reset();
        BridgeSlopeGeometry.RotateAroundTopCenter(leftPlatform, CurrentSlopeAngle);
        PlaceAfter(rightPlatform, leftPlatform);
        PlaceAfter(nextPlatform, rightPlatform);
        characterMover.PlaceAtStart(CharacterStartOn(leftPlatform));
        runCharacterY = characterMover.transform.position.y;
        cameraOffsetFromGap = cameraFollow.transform.position.x - CurrentGapCenter().x;
        slopeWorldReady = true;
        LogSlope();
        if (bridgeJustSceneManager != null) bridgeJustSceneManager.ResetForRun();
        uiManager.ShowPlaying(Score, BestScore);
        if (audioManager != null)
        {
            audioManager.PlayStartSound();
        }
        StartCoroutine(PrepareNextRound(0f));
    }

    private void ResetComboCounts()
    {
        SuccessComboCount = 0;
        PerfectComboCount = 0;
    }

    // 区間の準備。背景待ち → 難易度計算 → スキン抽選 → 配置 → 移動開始 → 状態の保存の順。
    private IEnumerator PrepareNextRound(float delay)
    {
        // 待ち時間が0でも次のフレームへ進め、Retry操作と新しい区間の入力を分ける。
        yield return null;
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        // 背景のフェードがスクロールより長い場合は、終わるまで入力を閉じておく。
        while (bridgeJustSceneManager != null && bridgeJustSceneManager.IsTransitioning)
            yield return null;

        if (CurrentState != GameState.Transition || !ValidateSetup())
        {
            yield break;
        }

        uiManager.HidePerfect();
        float gap = Vector3.Dot(BridgeSlopeGeometry.TopLeft(rightPlatform)
            - BridgeSlopeGeometry.TopRight(leftPlatform), SlopeAxis);
        // 橋を短くしすぎると成功できる位置がなくなるので、成功の余地が残る最短長を求める。
        float safeMinimum = gap + 2f * minimumOverlap + 2f * MinimumSuccessHalfWidth;
        float lengthFloor = Mathf.Max(minimumBridgeLength, safeMinimum);
        if (initialBridgeLength < lengthFloor - BoundaryTolerance)
        {
            ConfigurationError($"Initial Bridge Length must be at least {lengthFloor:F4} "
                + "for the current platforms and overlap settings.");
            yield break;
        }

        // 成功回数の分だけ短く・速くし、下限・上限で止める。Inspectorの値は書き換えない。
        float length = Mathf.Max(lengthFloor,
            initialBridgeLength - lengthDecreasePerSuccess * SuccessCount);
        float speed = Mathf.Min(maximumSpeed,
            initialSpeed + speedIncreasePerSuccess * SuccessCount);
        if (!bridgeMover.SetLengthAndSpeed(bridgeRenderer, length, speed))
        {
            ConfigurationError("Unable to apply the next bridge's length and speed.");
            yield break;
        }
        if (bridgeSkins != null) bridgeSkins.SelectForSection(length);

        if (!EnsureBridgeDrawOrder()
            || !bridgeMover.PrepareSlope(bridgeRenderer, CurrentGapCenter(), CurrentSlopeAngle))
        {
            ConfigurationError("The bridge center cannot start within the initial surface travel bounds.");
            yield break;
        }
        float width = length; // 伸縮橋は短い状態から始まるので、これは最大長として使う。
        waitingCharacterBase = characterMover.transform.position;
        Vector3 supportOrigin = BridgeSlopeGeometry.TopRight(leftPlatform);
        float waitingS = Vector3.Dot(waitingCharacterBase - supportOrigin, SlopeAxis);
        Vector2 boardSpan = BridgeSlopeGeometry.BottomInterval(bridgeRenderer, supportOrigin, SlopeAxis);
        float safeStart = Mathf.Max(0f, waitingS
            + BridgeSlopeGeometry.StepApproachDistance(
                BridgeSlopeGeometry.Thickness(bridgeRenderer), characterClearance)
            + 0.001f - boardSpan.x);
        if (!bridgeMover.IsExtending && !bridgeMover.PlaceAtTravel(safeStart))
        {
            ConfigurationError("Initial travel bounds leave no clear spawn position beside Circle.");
            yield break;
        }
        float minCenter = Mathf.Max(gap + minimumOverlap - width * 0.5f,
            gap * 0.5f + bridgeMover.MinTravel);
        float maxCenter = Mathf.Min(-minimumOverlap + width * 0.5f,
            gap * 0.5f + bridgeMover.MaxTravel);
        if (minCenter >= maxCenter - BoundaryTolerance
            || (!bridgeMover.IsExtending
                && Mathf.Abs(BridgeSlopeGeometry.Length(bridgeRenderer) - length) > 0.001f))
        {
            ConfigurationError("The surface length or travel bounds leave no usable successful position. "
                + "Review bridge length and initial limits.");
            yield break;
        }

        if (bridgeMover.IsExtending)
        {
            float socket = Vector3.Dot(bridgeMover.FixedRight - supportOrigin, SlopeAxis);
            float requiredLength = socket + minimumOverlap;
            float rightOverlap = socket - gap;
            if (rightOverlap + BoundaryTolerance < minimumOverlap
                || requiredLength > length + BoundaryTolerance
                || bridgeMover.ExtensionMinimum >= requiredLength - BoundaryTolerance)
            {
                ConfigurationError("Moving Walkway needs both failing short lengths and successful extended lengths. "
                    + "Check its contraction ratio and existing support geometry.");
                yield break;
            }
            Debug.Log($"[BridgeJust] Extension | socketS={socket:F4}, "
                + $"range=[{bridgeMover.ExtensionMinimum:F4},{length:F4}], "
                + $"success=[{requiredLength:F4},{length:F4}], "
                + $"perfect=[{Mathf.Max(requiredLength, length - 2f * perfectHalfWidth):F4},{length:F4}]", this);
        }

        LastResult = null;
        inputArmed = false;
        inputUnlockFrame = Time.frameCount;
        CurrentState = GameState.Moving;
        if (companionStack != null) companionStack.PrepareStage(SuccessCount + 1, rightPlatform);
        UpdateBoardVisual();
        SetBoardVisible(!initialBridgeHidden);
        bridgeMover.StartMoving();
        if (!bridgeMover.IsMoving)
        {
            ConfigurationError("Bridge could not start from its initial position. Check Min X / Max X.");
            yield break;
        }

        UpdateWaitingSupport();
        CaptureStageCheckpoint();

        Debug.Log($"[BridgeJust] Round | successfulCrossings={SuccessCount}, length={width:F4}, "
            + $"speed={speed:F4}, slope={CurrentSlopeAngle:F0}, "
            + $"skin={(bridgeSkins != null && bridgeSkins.Active != null ? bridgeSkins.Active.id : "Original")}", this);
        if (SuccessCount == 0)
        {
            LogGeometry();
            if (safeMinimum > minimumBridgeLength + BoundaryTolerance)
            {
                Debug.LogWarning($"[BridgeJust] Minimum bridge length raised to safe world width "
                    + $"{safeMinimum:F4} for this run.", this);
            }
        }
    }

    // シーンの配置を基準として記録し、3個目の足場と完成橋の表示枠を一度だけ作る。
    // 上下の補正はこの基準に一度だけ加えるので、区間進行やRetryのたびに積み上がらない。
    private bool InitializeScrollWorld()
    {
        originalLeft = leftPlatform;
        originalRight = rightPlatform;
        Vector3 verticalOffset = Vector3.up * gameplayVerticalOffset;
        originalLeftPosition = leftPlatform.transform.position + verticalOffset;
        originalRightPosition = rightPlatform.transform.position + verticalOffset;
        originalLeftRotation = leftPlatform.transform.rotation;
        originalRightRotation = rightPlatform.transform.rotation;
        initialGap = BridgeSlopeGeometry.TopLeft(rightPlatform).x
            - BridgeSlopeGeometry.TopRight(leftPlatform).x;
        Vector3 startOffset = characterMover.InitialPosition - BridgeSlopeGeometry.TopCenter(leftPlatform);
        initialCharacterAlong = Vector3.Dot(startOffset, leftPlatform.transform.right) + circleStartOffsetX;
        characterClearance = Vector3.Dot(startOffset, leftPlatform.transform.up);
        if (initialGap <= 0f || characterClearance <= 0f)
            return ConfigurationError("The initial scene needs a positive surface gap "
                + "and Circle above the left scaffold's top.");
        initialBridgeHidden = bridgeRenderer.forceRenderingOff;
        activeBoardVisual = new BridgeBoardVisual("Bridge surface (runtime)");

        // 元の足場は接続計算の基準として残し、表示用の浮遊島だけをコピーする。
        pooledPlatform = CreateVisualCopy(leftPlatform, "Scaffold (runtime pool)");
        Transform platformArt = leftPlatform.transform.Find("Floating Island Art");
        if (platformArt != null)
        {
            GameObject pooledArt = Instantiate(platformArt.gameObject, pooledPlatform.transform, false);
            pooledArt.name = platformArt.name;
        }
        nextPlatform = pooledPlatform;
        PlaceAfter(nextPlatform, rightPlatform);
        if (nextPlatform.bounds.min.x <= cameraFollow.ViewRightX + RecycleMargin)
        {
            return ConfigurationError("The third scaffold would appear during initial placement. "
                + "Use the existing portrait Game view; "
                + "this three-platform pool requires the next scaffold to start outside the right edge.");
        }
        for (int i = 0; i < completedBridges.Length; i++)
        {
            completedBridges[i] = CreateVisualCopy(bridgeRenderer,
                "Completed bridge (runtime pool " + i + ")");
            completedBridges[i].gameObject.SetActive(false);
            completedBoardVisuals[i] = new BridgeBoardVisual(
                "Completed surface (runtime pool " + i + ")");
        }
        return true;
    }

    // 記録した基準へ足場とカメラを戻し、使い回す足場と完成橋の表示を初期化する。
    private void ResetScrollWorld()
    {
        cameraFollow.ResetCamera();
        leftPlatform = originalLeft;
        rightPlatform = originalRight;
        originalLeft.transform.position = originalLeftPosition;
        originalRight.transform.position = originalRightPosition;
        originalLeft.transform.rotation = originalLeftRotation;
        originalRight.transform.rotation = originalRightRotation;
        nextPlatform = pooledPlatform;
        PlaceAfter(nextPlatform, rightPlatform);
        SetBoardVisible(false);
        foreach (BridgeBoardVisual visual in completedBoardVisuals) visual?.Show(false);
        foreach (SpriteRenderer completed in completedBridges)
        {
            if (completed != null) completed.gameObject.SetActive(false);
        }
    }

    // 子供の合流を待ち、前の橋をフェードしながらカメラを右へ動かす。
    // その後、古い右足場を新しい左足場にし、画面外の足場を先へ回して次の区間を準備する。
    private IEnumerator ScrollToNextSection()
    {
        if (CurrentState != GameState.Scrolling) yield break;
        if (companionStack != null)
            yield return companionStack.FinishArrival(SuccessCount,
                leftPlatform, rightPlatform, bridgeRenderer);
        if (CurrentState != GameState.Scrolling) yield break;
        SpriteRenderer trail = null;
        foreach (SpriteRenderer completed in completedBridges)
        {
            if (!completed.gameObject.activeSelf)
            {
                trail = completed;
                break;
            }
        }
        if (trail == null)
        {
            ConfigurationError("Completed bridge pool is full. "
                + "Check the camera portrait framing; visible bridges were preserved.");
            yield break;
        }
        CopyVisual(bridgeRenderer, trail);
        trail.gameObject.SetActive(true);
        trail.forceRenderingOff = true;
        int trailIndex = System.Array.IndexOf(completedBridges, trail);
        BridgeBoardVisual trailVisual = completedBoardVisuals[trailIndex];
        trailVisual.Configure(new BridgeBoardSurface(leftPlatform, bridgeRenderer), bridgeRenderer);
        trailVisual.Show(!initialBridgeHidden);
        SetBoardVisible(false);

        // 親が乗っている足場は角度を保ち、画面外の次の足場だけを新しい傾斜にする。
        if (nextPlatform.bounds.min.x <= cameraFollow.ViewRightX + RecycleMargin)
        {
            ConfigurationError("The future scaffold must be offscreen before changing its slope.");
            yield break;
        }
        if (slope.AdvanceTo((long)SuccessCount + 1L)) LogSlope();
        PlaceAfter(nextPlatform, rightPlatform);

        float nextCenter = (BridgeSlopeGeometry.TopRight(rightPlatform)
            + SlopeAxis * (initialGap * 0.5f)).x;
        float targetCameraX = nextCenter + cameraOffsetFromGap;
        if (targetCameraX <= cameraFollow.transform.position.x)
        {
            ConfigurationError("The next section must be to the right of the current camera.");
            yield break;
        }
        // 前の橋はスクロール後も画面の左端に残ることがあるので、スクロールに合わせてフェードする。
        float scrollStartX = cameraFollow.transform.position.x;
        float verticalShift = runCharacterY - CharacterStartOn(rightPlatform).y;
        float appliedVerticalShift = 0f;
        IEnumerator scroll = cameraFollow.ScrollToX(targetCameraX);
        while (scroll.MoveNext())
        {
            float progress = Mathf.Clamp01((cameraFollow.transform.position.x - scrollStartX)
                / (targetCameraX - scrollStartX));
            float wantedShift = verticalShift * progress;
            ShiftGameplayWorld(Vector3.up * (wantedShift - appliedVerticalShift));
            appliedVerticalShift = wantedShift;
            trailVisual.SetAlpha(1f - Mathf.SmoothStep(0f, 1f, progress));
            yield return scroll.Current;
        }
        if (CurrentState != GameState.Scrolling
            || !cameraFollow.ValidateSetup(out string cameraError)
            || cameraFollow.IsScrolling
            || Mathf.Abs(cameraFollow.transform.position.x - targetCameraX) > 0.001f)
        {
            ConfigurationError("Camera did not complete its scroll. "
                + "Keep CameraFollow enabled and do not use another camera controller.");
            yield break;
        }

        trail.gameObject.SetActive(false);
        trailVisual.Show(false);
        ShiftGameplayWorld(Vector3.up * (verticalShift - appliedVerticalShift));

        // 使い終わった足場が完全に画面外へ出てから、先の足場として置き直す。
        SpriteRenderer retired = leftPlatform;
        if (retired.bounds.max.x >= cameraFollow.ViewLeftX - RecycleMargin)
        {
            ConfigurationError("The previous left scaffold is still visible. "
                + "Pool recycling stopped to avoid a visible teleport. Check the portrait camera framing.");
            yield break;
        }
        Vector3 retiredPosition = retired.transform.position;
        Quaternion retiredRotation = retired.transform.rotation;
        PlaceAfter(retired, nextPlatform);
        if (retired.bounds.min.x <= cameraFollow.ViewRightX + RecycleMargin)
        {
            retired.transform.SetPositionAndRotation(retiredPosition, retiredRotation);
            ConfigurationError("The recycled scaffold would enter the visible area during placement. "
                + "Review portrait camera framing.");
            yield break;
        }
        leftPlatform = rightPlatform;
        rightPlatform = nextPlatform;
        nextPlatform = retired;
        RebaseWorldIfNeeded();
        CurrentState = GameState.Transition;
        yield return SettleCharacterOnPlatform(leftPlatform);
        if (companionStack != null) yield return companionStack.SettleOnPlatform(leftPlatform);
        yield return PrepareNextRound(transitionSeconds);
    }

    // 座標が大きくなりすぎると小数の精度が落ちるので、全体を同じ量だけ左へ戻す。
    private void RebaseWorldIfNeeded()
    {
        float delta = cameraFollow.InitialX - cameraFollow.transform.position.x;
        if (Mathf.Abs(delta) < RebaseDistance) return;
        ShiftGameplayWorld(Vector3.right * delta);
        cameraFollow.ShiftWorldX(delta);
    }

    // 足場・橋・親子・完成済みの橋を同じ量だけ動かし、接続がずれないようにする。
    private void ShiftGameplayWorld(Vector3 delta)
    {
        leftPlatform.transform.position += delta;
        rightPlatform.transform.position += delta;
        nextPlatform.transform.position += delta;
        foreach (SpriteRenderer completed in completedBridges)
            if (completed != null) completed.transform.position += delta;
        foreach (BridgeBoardVisual visual in completedBoardVisuals) visual?.Shift(delta);
        characterMover.ShiftWorld(delta);
        if (companionStack != null) companionStack.ShiftWorld(delta);
        bridgeMover.ShiftWorld(delta);
    }

    // 前の足場の右上端から、今回の傾斜方向へ一定の隙間を空けて次の足場を置く。
    private void PlaceAfter(SpriteRenderer item, SpriteRenderer previous)
    {
        BridgeSlopeGeometry.PlaceTopLeft(item, BridgeSlopeGeometry.TopRight(previous)
            + SlopeAxis * initialGap, CurrentSlopeAngle);
    }

    // 足場の上でのCircleの開始位置。端から半径分の余裕を取り、どの区間でも同じ相対位置にする。
    private Vector3 CharacterStartOn(SpriteRenderer platform)
    {
        float radius = FailSequenceController.BodyRadius(characterMover.GetComponent<SpriteRenderer>());
        float limit = Mathf.Max(0f, BridgeSlopeGeometry.Length(platform) * 0.5f - radius - 0.025f);
        float along = Mathf.Clamp(initialCharacterAlong, -limit, limit);
        Vector3 position = BridgeSlopeGeometry.TopCenter(platform)
            + platform.transform.right * along
            + platform.transform.up * characterClearance;
        position.z = characterMover.InitialPosition.z;
        return position;
    }

    // 横断で足場の上には着いているので、橋の厚みなどで高くなった分だけを足場へ降ろす。
    private IEnumerator SettleCharacterOnPlatform(SpriteRenderer platform)
    {
        Vector3 start = characterMover.transform.position;
        Vector3 target = CharacterStartOn(platform);
        float duration = Mathf.Max(0.15f, Vector3.Distance(start, target) / characterMover.MoveSpeed);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            characterMover.PlaceAtStart(Vector3.Lerp(start, target,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration))));
            yield return null;
        }
        characterMover.PlaceAtStart(target);
    }

    private Vector3 CurrentGapCenter() => (BridgeSlopeGeometry.TopRight(leftPlatform)
        + BridgeSlopeGeometry.TopLeft(rightPlatform)) * 0.5f;

    // 橋を足場より手前に描画する。
    private bool EnsureBridgeDrawOrder()
    {
        int leftLayer = SortingLayer.GetLayerValueFromID(leftPlatform.sortingLayerID);
        int rightLayer = SortingLayer.GetLayerValueFromID(rightPlatform.sortingLayerID);
        SpriteRenderer front = leftLayer >= rightLayer ? leftPlatform : rightPlatform;
        int order = front.sortingOrder;
        if (leftLayer == rightLayer)
            order = Mathf.Max(leftPlatform.sortingOrder, rightPlatform.sortingOrder);
        int bridgeLayer = SortingLayer.GetLayerValueFromID(bridgeRenderer.sortingLayerID);
        int platformLayer = Mathf.Max(leftLayer, rightLayer);
        if (bridgeLayer > platformLayer
            || (bridgeLayer == platformLayer && bridgeRenderer.sortingOrder > order))
            return true;
        if (order >= 32767) return ConfigurationError("No sorting order is available above the platforms.");
        bridgeRenderer.sortingLayerID = front.sortingLayerID;
        bridgeRenderer.sortingOrder = order + 1;
        return true;
    }

    // 移動後の位置に合わせて、親子の接地と表示用の橋の形を更新する。落下演出中は上書きしない。
    private void LateUpdate()
    {
        if (CurrentState == GameState.Moving) UpdateWaitingSupport();
        if (activeBoardVisual != null && activeBoardVisual.Visible
            && CurrentState != GameState.Failing && CurrentState != GameState.GameOver)
            UpdateBoardVisual();
    }

    private void SetBoardVisible(bool visible)
    {
        bridgeRenderer.forceRenderingOff = true; // 元の描画は隠し、移動・判定用の形として使う。
        activeBoardVisual?.Show(visible);
    }

    private void UpdateBoardVisual() => activeBoardVisual?.Configure(
        new BridgeBoardSurface(leftPlatform, bridgeRenderer), bridgeRenderer);

    private void UpdateWaitingSupport()
    {
        if (CurrentState != GameState.Moving || characterMover == null || characterMover.IsCrossing) return;
        Vector3 origin = BridgeSlopeGeometry.TopRight(leftPlatform);
        var surface = new BridgeBoardSurface(leftPlatform, bridgeRenderer);
        float baseHeight = Vector3.Dot(waitingCharacterBase - origin, SlopeNormal);
        float lift = surface.WaitingHeight(waitingCharacterBase, characterClearance) - baseHeight;
        characterMover.PlaceAtStart(waitingCharacterBase + SlopeNormal * lift);
        if (companionStack != null) companionStack.UpdateWaitingSupport(leftPlatform, bridgeRenderer);
    }

    private void LogSlope() => Debug.Log($"[BridgeJust] Slope | stage={(long)SuccessCount + 1L}, "
        + $"angle={CurrentSlopeAngle:F0}, interval={slope.Interval}, "
        + $"nextChangeStage={slope.NextChangeStage}", this);

    private static SpriteRenderer CreateVisualCopy(SpriteRenderer source, string name)
    {
        var copy = new GameObject(name).AddComponent<SpriteRenderer>();
        CopyVisual(source, copy);
        return copy;
    }

    private static void CopyVisual(SpriteRenderer source, SpriteRenderer copy)
    {
        copy.gameObject.layer = source.gameObject.layer;
        copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        copy.transform.localScale = source.transform.lossyScale;
        copy.sprite = source.sprite;
        copy.sharedMaterials = source.sharedMaterials;
        copy.color = source.color;
        copy.flipX = source.flipX;
        copy.flipY = source.flipY;
        copy.drawMode = source.drawMode;
        copy.size = source.size;
        copy.tileMode = source.tileMode;
        copy.adaptiveModeThreshold = source.adaptiveModeThreshold;
        copy.maskInteraction = source.maskInteraction;
        copy.sortingLayerID = source.sortingLayerID;
        copy.sortingOrder = source.sortingOrder;
        copy.renderingLayerMask = source.renderingLayerMask;
        copy.spriteSortPoint = source.spriteSortPoint;
        copy.enabled = source.enabled;
    }

    private static bool AnyPointerActive()
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.isPressed || touch.press.wasPressedThisFrame
                    || touch.press.wasReleasedThisFrame)
                {
                    return true;
                }
            }
        }

        Mouse mouse = Mouse.current;
        return mouse != null && (mouse.leftButton.isPressed || mouse.leftButton.wasPressedThisFrame
            || mouse.leftButton.wasReleasedThisFrame);
    }

    // タッチとマウスをまとめて扱う。タッチを優先し、同じ操作を二重に扱わない。
    private bool StopWasPressed()
    {
        Touchscreen touchscreen = Touchscreen.current;
        bool touchInProgress = false;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                touchInProgress |= touch.press.isPressed || touch.press.wasReleasedThisFrame
                    || touch.press.wasPressedThisFrame;
                if (touch.press.wasPressedThisFrame
                    && CanAcceptPointer(touch.position.ReadValue()))
                {
                    return true;
                }
            }
        }

        // タッチがUIに触れた場合、同時に発生するマウス入力でも橋を止めない。
        if (touchInProgress)
        {
            return false;
        }

        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame
            && CanAcceptPointer(mouse.position.ReadValue());
    }

    // Retryなどのボタンを押したときに、橋の停止として扱わない。
    private bool CanAcceptPointer(Vector2 position)
    {
        if (position.x < 0f || position.x >= Screen.width
            || position.y < 0f || position.y >= Screen.height)
        {
            return false;
        }

        EventSystem eventSystem = EventSystem.current;
        if (!blockInputOverUI || eventSystem == null)
        {
            return true;
        }

        var pointer = new PointerEventData(eventSystem) { position = position };
        eventSystem.RaycastAll(pointer, uiHits);
        foreach (RaycastResult hit in uiHits)
        {
            if (hit.module is GraphicRaycaster)
            {
                return false;
            }
        }

        return true;
    }

    // 参照・数値・橋と足場の位置関係を検査し、不正なら理由を出して安全に止める。
    private bool ValidateSetup()
    {
        if (!IsFinite(gameplayVerticalOffset) || !IsFinite(circleStartOffsetX))
            return ConfigurationError("Gameplay Vertical Offset and Circle Start Offset X must be finite.");
        if (bridgeMover == null || leftPlatform == null || rightPlatform == null
            || bridgeRenderer == null || characterMover == null || uiManager == null
            || cameraFollow == null)
        {
            return ConfigurationError("Assign Bridge Mover, Left/Right Platform SpriteRenderers, "
                + "Bridge Renderer, Character Mover (Circle), UI Manager, "
                + "and CameraFollow on Main Camera in the Inspector.");
        }

        if (!cameraFollow.ValidateSetup(out string cameraError))
            return ConfigurationError(cameraError);
        if (cameraFollow.TracksWorldObject(leftPlatform.transform)
            || cameraFollow.TracksWorldObject(rightPlatform.transform)
            || cameraFollow.TracksWorldObject(bridgeMover.transform)
            || cameraFollow.TracksWorldObject(characterMover.transform))
            return ConfigurationError("CameraFollow Screen Fixed World Objects must contain backgrounds only, "
                + "never scaffolds, Bridge or Circle.");

        if (!bridgeMover.isActiveAndEnabled)
        {
            return ConfigurationError("BridgeMover must be enabled on an active GameObject.");
        }

        if (leftPlatform == rightPlatform || bridgeRenderer == leftPlatform
            || bridgeRenderer == rightPlatform
            || bridgeRenderer.transform != bridgeMover.transform)
        {
            return ConfigurationError("Use three different SpriteRenderers. "
                + "Bridge Renderer must be on the BridgeMover object itself for width adjustment.");
        }

        if (!ValidRenderer(leftPlatform) || !ValidRenderer(rightPlatform)
            || !ValidRenderer(bridgeRenderer))
        {
            return ConfigurationError("All three SpriteRenderers need an assigned Sprite, "
                + "an active/enabled renderer, and finite positive world width.");
        }

        if (!IsFinite(minimumOverlap) || minimumOverlap <= BoundaryTolerance
            || !IsFinite(perfectHalfWidth) || perfectHalfWidth < 0f)
        {
            return ConfigurationError("Minimum Overlap must be greater than 0.00001; "
                + "Perfect Half Width must be non-negative. Both must be finite.");
        }

        Vector3 axis = slopeWorldReady ? SlopeAxis : Vector3.right;
        Vector3 origin = BridgeSlopeGeometry.TopRight(leftPlatform);
        Vector2 leftSurface = BridgeSlopeGeometry.Interval(leftPlatform, origin, axis);
        Vector2 rightSurface = BridgeSlopeGeometry.Interval(rightPlatform, origin, axis);
        if (leftSurface.y > rightSurface.x)
        {
            return ConfigurationError("Left/right platform references are reversed or their horizontal bounds overlap. "
                + "Check their Inspector assignments and placement.");
        }

        if (leftSurface.y - leftSurface.x + BoundaryTolerance < minimumOverlap
            || rightSurface.y - rightSurface.x + BoundaryTolerance < minimumOverlap)
        {
            return ConfigurationError("Minimum Overlap exceeds a platform's world width.");
        }

        float characterOnLeft = Vector3.Dot(
            characterMover.transform.position - BridgeSlopeGeometry.TopCenter(leftPlatform),
            leftPlatform.transform.right);
        if (!characterMover.isActiveAndEnabled || !characterMover.HasValidSpeed
            || characterMover.transform.IsChildOf(bridgeMover.transform)
            || Mathf.Abs(characterOnLeft) > BridgeSlopeGeometry.Length(leftPlatform) * 0.5f + BoundaryTolerance)
        {
            return ConfigurationError("Circle must have an enabled CharacterMover, positive Move Speed, "
                + "start over the left platform, and not be a child of Bridge.");
        }

        if ((CurrentState == GameState.Moving || CurrentState == GameState.Judging)
            && Vector3.Dot(bridgeMover.transform.right, axis) < 0.9999f)
        {
            return ConfigurationError("Bridge must be parallel to the current slope.");
        }
        if (slopeWorldReady && (CurrentState == GameState.Moving || CurrentState == GameState.Judging)
            && (Mathf.Abs(Vector3.Dot(
                    BridgeSlopeGeometry.BottomCenter(bridgeRenderer) - origin, SlopeNormal)) > 0.001f
                || Mathf.Abs(Vector3.Dot(BridgeSlopeGeometry.BottomCenter(bridgeRenderer)
                    - BridgeSlopeGeometry.TopLeft(rightPlatform), SlopeNormal)) > 0.001f
                || Vector3.Dot(rightPlatform.transform.right, axis) < 0.9999f))
            return ConfigurationError("Bridge underside must pass through the left connection corner "
                + "and rest on the right top surface.");

        if (!IsFinite(initialBridgeLength) || initialBridgeLength <= 0f
            || !IsFinite(minimumBridgeLength) || minimumBridgeLength <= 0f
            || minimumBridgeLength > initialBridgeLength
            || !IsFinite(lengthDecreasePerSuccess) || lengthDecreasePerSuccess < 0f
            || !IsFinite(initialSpeed) || initialSpeed <= 0f
            || !IsFinite(maximumSpeed) || maximumSpeed < initialSpeed
            || !IsFinite(speedIncreasePerSuccess) || speedIncreasePerSuccess < 0f
            || !IsFinite(transitionSeconds) || transitionSeconds < 0f)
        {
            return ConfigurationError("Difficulty values must be finite: "
                + "positive initial/minimum length and initial/maximum speed; "
                + "minimum length <= initial length; initial speed <= maximum speed; "
                + "non-negative increments and transition time.");
        }

        if (!uiManager.ValidateSetup(out string uiError))
        {
            return ConfigurationError(uiError);
        }

        EventSystem eventSystem = EventSystem.current;
        InputSystemUIInputModule inputModule = eventSystem != null
            ? eventSystem.GetComponent<InputSystemUIInputModule>() : null;
        StandaloneInputModule legacyModule = eventSystem != null
            ? eventSystem.GetComponent<StandaloneInputModule>() : null;
        if (inputModule == null || !inputModule.isActiveAndEnabled
            || (legacyModule != null && legacyModule.isActiveAndEnabled))
        {
            return ConfigurationError("Create an active EventSystem with InputSystemUIInputModule. "
                + "Disable/remove StandaloneInputModule. Keep the EventSystem outside Game Over Panel.");
        }

        return true;
    }

    // 設定不備のときの共通の停止処理。正常なゲームオーバーとは区別し、原因をConsoleへ出す。
    private bool ConfigurationError(string message)
    {
        CurrentState = GameState.Result;
        awardPending = false;
        inputArmed = false;
        StopAllCoroutines();
        failureRoutine = null;
        if (failSequence != null) failSequence.Cancel();
        if (cameraFollow != null) cameraFollow.CancelScroll();
        if (bridgeJustSceneManager != null) bridgeJustSceneManager.CancelTransition();
        if (characterMover != null)
        {
            characterMover.CancelCrossing();
        }
        if (bridgeMover != null)
        {
            bridgeMover.Freeze();
            // このクラスはBridgeMoverより先に初期化されるので、
            // 設定エラーの後に橋側のStartが自動移動を始めないよう止めておく。
            bridgeMover.enabled = false;
        }

        Debug.LogError($"[BridgeJust] Configuration error: {message}", this);
        enabled = false;
        return false;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        failureRoutine = null;
        if (failSequence != null) failSequence.Cancel();
        if (cameraFollow != null) cameraFollow.CancelScroll();
        if (bridgeJustSceneManager != null) bridgeJustSceneManager.CancelTransition();
        inputArmed = false;
        awardPending = false;
        if (bridgeMover != null)
        {
            bridgeMover.Freeze();
        }
        if (characterMover != null)
        {
            characterMover.CancelCrossing();
        }
    }

    private void OnDestroy()
    {
        activeBoardVisual?.Dispose();
        foreach (BridgeBoardVisual visual in completedBoardVisuals) visual?.Dispose();
        if (pooledPlatform != null) Destroy(pooledPlatform.gameObject);
        foreach (SpriteRenderer completed in completedBridges)
            if (completed != null) Destroy(completed.gameObject);
        if (initialized)
        {
            if (characterMover != null)
            {
                characterMover.CrossingFinished -= OnCrossingFinished;
            }
            if (uiManager != null)
            {
                uiManager.RetryRequested -= RetryRun;
                uiManager.RetryStageRequested -= RetryStage;
            }
        }
    }

    private void LogGeometry()
    {
        Vector3 origin = BridgeSlopeGeometry.TopRight(leftPlatform);
        Vector2 left = BridgeSlopeGeometry.Interval(leftPlatform, origin, SlopeAxis);
        Vector2 right = BridgeSlopeGeometry.Interval(rightPlatform, origin, SlopeAxis);
        // 伸縮橋は開始時にわざと短いので、判定に使える最大長を出す。
        float width = bridgeMover.IsExtending
            ? bridgeMover.ExtensionMaximum
            : BridgeSlopeGeometry.Length(bridgeRenderer);
        float ideal = (left.y + right.x) * 0.5f;
        float minCenter = right.x + minimumOverlap - width * 0.5f;
        float maxCenter = left.y - minimumOverlap + width * 0.5f;
        bool possible = minCenter < maxCenter;

        Debug.Log($"[BridgeJust] Geometry "
            + $"| slope={CurrentSlopeAngle:F0}, bridgeLength={width:F6}; "
            + $"leftS=[{left.x:F6}, {left.y:F6}]; "
            + $"rightS=[{right.x:F6}, {right.y:F6}]; "
            + $"idealS={ideal:F6}, successCenterS=[{minCenter:F6}, {maxCenter:F6}] "
            + $"(before intersection with BridgeMover movement bounds), possible={possible}", this);

        if (!possible)
        {
            Debug.LogWarning("[BridgeJust] Current widths and Minimum Overlap leave no successful bridge position. "
                + "Review the overlap setting; scene placement was preserved.", this);
        }
        else if (minCenter >= ideal - perfectHalfWidth - BoundaryTolerance
            && maxCenter <= ideal + perfectHalfWidth + BoundaryTolerance)
        {
            Debug.LogWarning("[BridgeJust] Every successful position is inside the Perfect range; "
                + "no ordinary SUCCESS range exists with these settings.", this);
        }
    }

    private static bool ValidRenderer(SpriteRenderer renderer)
    {
        Bounds bounds = renderer.bounds;
        return renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sprite != null
            && IsFinite(bounds.min.x) && IsFinite(bounds.max.x) && bounds.size.x > 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
