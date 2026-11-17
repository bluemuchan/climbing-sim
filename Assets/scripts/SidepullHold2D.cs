using UnityEngine;

/// <summary>
/// Sidepull 岩点。
///
/// 一只手抓住时：
///     以该手的位置作为所有区域计算原点。
///
/// 两只手同时抓住同一个 Sidepull 时：
///     以两只手的中点作为所有区域计算原点。
///
/// 根据 COM 判断：
///     Stable
///     Weak
///     Strain
///
/// 同时自动计算 Fall Boundary。
/// COM 连续越过 Fall Boundary 达到指定时间后，
/// 当前抓住该 Sidepull 的所有手都会脱落。
/// </summary>
[DefaultExecutionOrder(0)]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class SidepullHold2D : MonoBehaviour
{
    public enum SidepullState
    {
        Stable,
        Weak,
        Strain
    }

    [System.Serializable]
    public struct SidepullEvaluation
    {
        public SidepullState state;

        public bool beyondFallBoundary;

        public SidepullEvaluation(
            SidepullState state,
            bool beyondFallBoundary
        )
        {
            this.state = state;
            this.beyondFallBoundary =
                beyondFallBoundary;
        }
    }

    // =========================================================
    // 玩家引用
    // =========================================================

    [Header("玩家系统")]

    [SerializeField]
    private BodyCenterTracker2D centerTracker;

    [SerializeField]
    private PhysicsDraggable2D leftHand;

    [SerializeField]
    private PhysicsDraggable2D rightHand;


    // =========================================================
    // 垂直 Sidepull
    // =========================================================

    [Header("垂直 Sidepull 特殊规则")]

    [Tooltip(
        "开口接近垂直向上时，" +
        "整个区域视为 Stable，并禁用 Fall。"
    )]
    [Range(0f, 10f)]
    [SerializeField]
    private float verticalStableTolerance = 2f;


    // =========================================================
    // 体力倍率
    // =========================================================

    [Header("体力恢复倍率")]

    [SerializeField]
    private float stableRecoveryMultiplier = 1f;

    [SerializeField]
    private float weakRecoveryMultiplier = 0.25f;

    [SerializeField]
    private float strainRecoveryMultiplier = -0.5f;


    // =========================================================
    // Slip
    // =========================================================

    [Header("Fall / Slip")]

    [Tooltip("连续越过 Fall Boundary 后多少秒脱手")]
    [Min(0.1f)]
    [SerializeField]
    private float slipTime = 3f;

    [Tooltip(
        "Fall Boundary 的额外安全距离。" +
        "目前建议保持 0。"
    )]
    [Min(0f)]
    [SerializeField]
    private float slipBoundaryMargin = 0f;


    // =========================================================
    // Gizmo
    // =========================================================

    [Header("Gizmo 调试")]

    [SerializeField]
    private bool drawDebugLines = true;

    [Tooltip("Play 模式下是否继续显示 Gizmo")]
    [SerializeField]
    private bool drawDebugLinesWhilePlaying = true;

    [Min(0.1f)]
    [SerializeField]
    private float debugLineLength = 2f;


    // =========================================================
    // 运行时状态
    // =========================================================

    [Header("运行时调试")]

    [SerializeField]
    private SidepullState currentState =
        SidepullState.Stable;

    [SerializeField]
    private bool leftHandAttached;

    [SerializeField]
    private bool rightHandAttached;

    [SerializeField]
    private bool bothHandsAttached;

    [Tooltip(
        "当前所有Sidepull规则实际使用的坐标原点"
    )]
    [SerializeField]
    private Vector2 activeGripPoint;

    [SerializeField]
    private bool beyondFallBoundary;

    [SerializeField]
    private float slipTimer;


    private Collider2D sidepullCollider;


    // =========================================================
    // 公共读取
    // =========================================================

    public SidepullState CurrentState
    {
        get { return currentState; }
    }

    public Vector2 ActiveGripPoint
    {
        get { return activeGripPoint; }
    }

    public bool BothHandsAttached
    {
        get { return bothHandsAttached; }
    }

    public bool BeyondFallBoundary
    {
        get { return beyondFallBoundary; }
    }

    public float SlipProgress
    {
        get
        {
            if (slipTime <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                slipTimer / slipTime
            );
        }
    }

    // =========================================================
    // Validator / AI 纯几何查询
    // =========================================================

    /// <summary>
    /// 根据指定Grip Point和COM位置，
    /// 查询这个Sidepull的几何状态。
    ///
    /// 不读取实际玩家当前是否抓住该Hold，
    /// 不修改currentState，
    /// 不修改slipTimer，
    /// 不会导致玩家脱手。
    ///
    /// 因此可以安全地给Validator / AI调用。
    /// </summary>
    public SidepullEvaluation EvaluateAt(
        Vector2 gripPoint,
        Vector2 centerPosition
    )
    {
        // =====================================================
        // 垂直向上的Sidepull
        //
        // 按当前游戏规则：
        // 永远Stable，同时禁用Fall。
        // =====================================================

        if (IsVerticalUp())
        {
            return new SidepullEvaluation(
                SidepullState.Stable,
                false
            );
        }


        // =====================================================
        // Stable / Weak / Strain
        // =====================================================

        SidepullState state =
            CalculateState(
                gripPoint,
                centerPosition
            );


        // =====================================================
        // Fall Boundary
        // =====================================================

        bool beyondBoundary =
            IsBeyondFallBoundary(
                gripPoint,
                centerPosition
            );


        return new SidepullEvaluation(
            state,
            beyondBoundary
        );
    }


    // =========================================================
    // Unity
    // =========================================================

    private void Awake()
    {
        sidepullCollider =
            GetComponent<Collider2D>();

        if (centerTracker == null)
        {
            Debug.LogError(
                $"{name}: 没有指定 BodyCenterTracker2D。"
            );
        }

        if (leftHand == null)
        {
            Debug.LogError(
                $"{name}: 没有指定 Left Hand。"
            );
        }

        if (rightHand == null)
        {
            Debug.LogError(
                $"{name}: 没有指定 Right Hand。"
            );
        }
    }

    private void FixedUpdate()
    {
        if (centerTracker == null)
        {
            return;
        }

        UpdateAttachedHands();

        // 没有任何手抓住这个Sidepull
        if (!leftHandAttached &&
            !rightHandAttached)
        {
            ResetRuntimeState();
            return;
        }

        // =============================================
        // 计算本帧真正使用的 Grip Point
        // =============================================

        activeGripPoint =
            CalculateActiveGripPoint();

        Vector2 centerPosition =
            centerTracker.CurrentCenter;

        // =============================================
        // Stable / Weak / Strain
        // =============================================

        currentState =
            CalculateState(
                activeGripPoint,
                centerPosition
            );

        // =============================================
        // 0°向上Sidepull
        // =============================================

        if (IsVerticalUp())
        {
            currentState =
                SidepullState.Stable;

            beyondFallBoundary =
                false;

            slipTimer =
                0f;

            return;
        }

        // =============================================
        // Fall Boundary
        // =============================================

        beyondFallBoundary =
            IsBeyondFallBoundary(
                activeGripPoint,
                centerPosition
            );

        // =============================================
        // Slip Timer
        // =============================================

        if (beyondFallBoundary)
        {
            slipTimer +=
                Time.fixedDeltaTime;

            if (slipTimer >= slipTime)
            {
                ForceReleaseAttachedHands();
            }
        }
        else
        {
            // 必须连续越界。
            // 一旦回到安全侧立即清零。
            slipTimer = 0f;
        }
    }


    // =========================================================
    // 判断哪只手抓着
    // =========================================================

    private void UpdateAttachedHands()
    {
        leftHandAttached =
            IsHandAttachedToThisSidepull(
                leftHand
            );

        rightHandAttached =
            IsHandAttachedToThisSidepull(
                rightHand
            );

        bothHandsAttached =
            leftHandAttached
            && rightHandAttached;
    }


    // =========================================================
    // 计算实际 Grip Point
    // =========================================================

    private Vector2 CalculateActiveGripPoint()
    {
        // =========================================
        // 两只手同时抓住同一个Sidepull
        // =========================================

        if (bothHandsAttached)
        {
            Vector2 leftGripPoint =
                leftHand.CurrentHoldGripWorldPoint;

            Vector2 rightGripPoint =
                rightHand.CurrentHoldGripWorldPoint;

            return
                (leftGripPoint + rightGripPoint)
                * 0.5f;
        }


        // =========================================
        // 只有左手
        // =========================================

        if (leftHandAttached)
        {
            return
                leftHand.CurrentHoldGripWorldPoint;
        }


        // =========================================
        // 只有右手
        // =========================================

        if (rightHandAttached)
        {
            return
                rightHand.CurrentHoldGripWorldPoint;
        }


        return transform.position;
    }


    // =========================================================
    // Stable / Weak / Strain
    // =========================================================

    private SidepullState CalculateState(
        Vector2 gripPoint,
        Vector2 centerPosition
    )
    {
        if (IsVerticalUp())
        {
            return SidepullState.Stable;
        }

        Vector2 handToCenter =
            centerPosition - gripPoint;

        if (handToCenter.sqrMagnitude <
            0.000001f)
        {
            return SidepullState.Weak;
        }

        // Sidepull真正的视觉开口方向
        Vector2 openingDirection =
            GetOpeningDirection();

        // 绿色线：
        // 与开口方向相反
        Vector2 pullDirection =
            -openingDirection;

        // 黄色线：
        // 世界空间正下方
        Vector2 yellowDirection =
            Vector2.down;

        // 黄色 → 绿色的有符号角度
        float greenAngle =
            Vector2.SignedAngle(
                yellowDirection,
                pullDirection
            );

        // 红线：
        // 黄色和绿色之间的一半
        float redAngle =
            greenAngle * 0.5f;

        // 黄色 → COM方向
        float centerAngle =
            Vector2.SignedAngle(
                yellowDirection,
                handToCenter.normalized
            );

        // =============================================
        // 开口朝右
        // =============================================

        if (greenAngle < 0f)
        {
            // 黄色线右边全部 Strain
            if (centerAngle > 0f)
            {
                return SidepullState.Strain;
            }

            // 黄线与红线之间 Weak
            if (centerAngle > redAngle)
            {
                return SidepullState.Weak;
            }

            // 红线左边全部 Stable
            return SidepullState.Stable;
        }

        // =============================================
        // 开口朝左
        // =============================================

        else
        {
            // 黄色线左边全部 Strain
            if (centerAngle < 0f)
            {
                return SidepullState.Strain;
            }

            // 黄线与红线之间 Weak
            if (centerAngle < redAngle)
            {
                return SidepullState.Weak;
            }

            // 红线右边全部 Stable
            return SidepullState.Stable;
        }
    }


    // =========================================================
    // 黑色 Fall Boundary
    // =========================================================

    private bool IsBeyondFallBoundary(
        Vector2 gripPoint,
        Vector2 centerPosition
    )
    {
        Vector2 openingDirection =
            GetOpeningDirection();

        Vector2 gripToCenter =
            centerPosition - gripPoint;

        /*
         * 黑线与 Opening Direction 垂直。
         *
         * Dot > 0：
         * COM位于Sidepull开口方向的一侧。
         */
        float openingSideAmount =
            Vector2.Dot(
                gripToCenter,
                openingDirection
            );

        return openingSideAmount >
            slipBoundaryMargin;
    }


    // =========================================================
    // 强制脱手
    // =========================================================

    private void ForceReleaseAttachedHands()
    {
        /*
         * 这里特别重要：
         *
         * 如果两只手都抓住，
         * 我们先记录它们，
         * 再一起释放。
         *
         * 避免释放第一只手之后，
         * 第二只手的状态突然切换成
         * “单手模式”。
         */

        bool releaseLeft =
            leftHandAttached;

        bool releaseRight =
            rightHandAttached;

        Debug.Log(
            $"{name}: COM越过Fall Boundary " +
            $"{slipTime:F1}秒，Sidepull脱手。"
        );

        if (releaseLeft &&
            leftHand != null)
        {
            leftHand.ForceDetachFromHold();
        }

        if (releaseRight &&
            rightHand != null)
        {
            rightHand.ForceDetachFromHold();
        }

        slipTimer =
            0f;

        beyondFallBoundary =
            false;

        leftHandAttached =
            false;

        rightHandAttached =
            false;

        bothHandsAttached =
            false;
    }


    // =========================================================
    // 当前手是不是抓这个Sidepull
    // =========================================================

    public bool IsHandAttachedToThisSidepull(
        PhysicsDraggable2D hand
    )
    {
        if (hand == null)
        {
            return false;
        }

        if (!hand.IsAttachedToHold)
        {
            return false;
        }

        if (hand.CurrentHoldCollider == null)
        {
            return false;
        }

        SidepullHold2D attachedSidepull =
            hand.CurrentHoldCollider
                .GetComponentInParent<SidepullHold2D>();

        return attachedSidepull == this;
    }


    // =========================================================
    // 给体力系统读取
    // =========================================================

    public SidepullState GetStateForHand(
        PhysicsDraggable2D hand
    )
    {
        if (!IsHandAttachedToThisSidepull(hand))
        {
            return SidepullState.Stable;
        }

        /*
         * 因为 currentState 已经使用：
         *
         * 单手位置
         * 或
         * 双手中点
         *
         * 计算完成，
         * 所以两只手抓住时读取到完全相同的状态。
         */

        return currentState;
    }

    public float GetRecoveryMultiplierForHand(
        PhysicsDraggable2D hand
    )
    {
        if (!IsHandAttachedToThisSidepull(hand))
        {
            return 1f;
        }

        switch (currentState)
        {
            case SidepullState.Weak:

                return
                    weakRecoveryMultiplier;

            case SidepullState.Strain:

                return
                    strainRecoveryMultiplier;

            default:

                return
                    stableRecoveryMultiplier;
        }
    }


    // =========================================================
    // Sidepull方向
    // =========================================================

    private Vector2 GetOpeningDirection()
    {
        /*
         * Rotation Z = 0：
         *
         * transform.up = 世界正上方
         *
         * 所以完全符合：
         *
         * Sidepull朝上 = 0°
         */

        return
            ((Vector2)transform.up)
            .normalized;
    }

    private bool IsVerticalUp()
    {
        float angle =
            Vector2.Angle(
                GetOpeningDirection(),
                Vector2.up
            );

        return angle <=
            verticalStableTolerance;
    }


    // =========================================================
    // 重置
    // =========================================================

    private void ResetRuntimeState()
    {
        currentState =
            SidepullState.Stable;

        bothHandsAttached =
            false;

        beyondFallBoundary =
            false;

        slipTimer =
            0f;

        activeGripPoint =
            transform.position;
    }


    // =========================================================
    // Gizmo
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawDebugLines)
        {
            return;
        }

        if (Application.isPlaying &&
            !drawDebugLinesWhilePlaying)
        {
            return;
        }

        Vector2 gizmoGripPoint =
            transform.position;

        /*
         * Play模式：
         * 如果真的有手抓住，
         * 使用真正的运行时 Grip Point。
         */
        if (Application.isPlaying)
        {
            bool leftAttached =
                IsHandAttachedToThisSidepull(
                    leftHand
                );

            bool rightAttached =
                IsHandAttachedToThisSidepull(
                    rightHand
                );

            if (leftAttached &&
    rightAttached)
            {
                gizmoGripPoint =
                    (
                        leftHand.CurrentHoldGripWorldPoint
                        +
                        rightHand.CurrentHoldGripWorldPoint
                    )
                    * 0.5f;
            }
            else if (leftAttached)
            {
                gizmoGripPoint =
                    leftHand.CurrentHoldGripWorldPoint;
            }
            else if (rightAttached)
            {
                gizmoGripPoint =
                    rightHand.CurrentHoldGripWorldPoint;
            }
        }

        DrawDebugAtPoint(
            gizmoGripPoint
        );
    }


    // =========================================================
    // Gizmo具体绘制
    // =========================================================

    private void DrawDebugAtPoint(
        Vector2 gripPoint
    )
    {
        Vector2 openingDirection =
            GetOpeningDirection();

        Vector2 pullDirection =
            -openingDirection;

        Vector2 yellowDirection =
            Vector2.down;

        float greenAngle =
            Vector2.SignedAngle(
                yellowDirection,
                pullDirection
            );

        float redAngle =
            greenAngle * 0.5f;

        Vector2 redDirection =
            RotateVector(
                yellowDirection,
                redAngle
            );


        // =============================================
        // 绿色：
        // Sidepull Pull Direction
        // =============================================

        Gizmos.color =
            Color.green;

        Gizmos.DrawLine(
            gripPoint,
            gripPoint
                + pullDirection
                * debugLineLength
        );


        // =============================================
        // 黄色：
        // Weak / Strain
        // =============================================

        Gizmos.color =
            Color.yellow;

        Gizmos.DrawLine(
            gripPoint,
            gripPoint
                + yellowDirection
                * debugLineLength
        );


        // =============================================
        // 红色：
        // Stable / Weak
        // =============================================

        Gizmos.color =
            Color.red;

        Gizmos.DrawLine(
            gripPoint,
            gripPoint
                + redDirection
                * debugLineLength
        );


        // =============================================
        // 黑色：
        // Fall Boundary
        // =============================================

        if (!IsVerticalUp())
        {
            Vector2 blackDirection =
                new Vector2(
                    -openingDirection.y,
                    openingDirection.x
                );

            Gizmos.color =
                Color.black;

            Gizmos.DrawLine(
                gripPoint
                    - blackDirection
                    * debugLineLength,

                gripPoint
                    + blackDirection
                    * debugLineLength
            );
        }


        // =============================================
        // COM连接线
        // =============================================

        if (Application.isPlaying &&
            centerTracker != null)
        {
            Gizmos.color =
                Color.white;

            Gizmos.DrawLine(
                gripPoint,
                centerTracker.CurrentCenter
            );


            // COM位置本身也画一个小球
            Gizmos.DrawWireSphere(
                centerTracker.CurrentCenter,
                0.08f
            );
        }


        // =============================================
        // 双手中点显示
        // =============================================

        if (Application.isPlaying &&
            bothHandsAttached)
        {
            Gizmos.color =
                Color.cyan;

            Gizmos.DrawWireSphere(
                gripPoint,
                0.12f
            );
        }
    }


    // =========================================================
    // 向量旋转
    // =========================================================

    private Vector2 RotateVector(
        Vector2 vector,
        float degrees
    )
    {
        float radians =
            degrees
            * Mathf.Deg2Rad;

        float cos =
            Mathf.Cos(radians);

        float sin =
            Mathf.Sin(radians);

        return new Vector2(
            vector.x * cos
                - vector.y * sin,

            vector.x * sin
                + vector.y * cos
        );
    }

}