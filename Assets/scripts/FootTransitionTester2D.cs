using UnityEngine;

/// <summary>
/// Foot Transition 的可视化测试工具。
///
/// 真正的计算全部交给
/// ClimberTransitionSolver2D。
///
/// 测试：
/// (H1, F1) -> (H1, F2)
/// </summary>
[DisallowMultipleComponent]
public class FootTransitionTester2D : MonoBehaviour
{
    // =========================================================
    // Solver
    // =========================================================

    [Header("Solver")]

    [SerializeField]
    private ClimberTransitionSolver2D transitionSolver;

    [Tooltip("只用于画Safe Reach圆，不参与Transition计算")]
    [SerializeField]
    private ClimberReachProfile2D reachProfile;


    // =========================================================
    // 测试点
    // =========================================================

    [Header("测试的 Foot Transition")]

    [Tooltip("当前保持抓住的手点 H1")]
    [SerializeField]
    private Transform supportHandPoint;

    [Tooltip("当前保持踩住的脚点 F1")]
    [SerializeField]
    private Transform supportFootPoint;

    [Tooltip("另一只脚准备踩的新点 F2")]
    [SerializeField]
    private Transform targetFootPoint;


    // =========================================================
    // Result
    // =========================================================

    [Header("测试结果")]

    [SerializeField]
    private bool transitionValid;

    [SerializeField]
    private float bestReachUsage;

    [SerializeField]
    private Vector2 bestChestPosition;

    [SerializeField]
    private Vector2 bestHipPosition;

    [SerializeField]
    private int testedHipPositions;

    [SerializeField]
    private int validBridgePoseCount;

    private ClimberTransitionSolver2D.TransitionResult
    lastResult;

    [SerializeField]
    private int testedPoseConfigurationCount;

    [SerializeField]
    private float poseFreedomRatio;

    [SerializeField]
    private float averageReachUsage;

    [SerializeField]
    private float medianReachUsage;

    [SerializeField]
    private float reachUsageP75;

    [SerializeField]
    private float reachUsageP90;

    // =========================================================
    // Gizmo
    // =========================================================

    [Header("Gizmo")]

    [SerializeField]
    private bool drawDebug = true;

    [SerializeField]
    private bool drawSafeReachCircles = true;

    [SerializeField]
    private bool drawComSamples = true;

    [Min(0.005f)]
    [SerializeField]
    private float comSampleRadius = 0.015f;


    // =========================================================
    // Public
    // =========================================================

    public bool TransitionValid
    {
        get { return transitionValid; }
    }

    public float BestReachUsage
    {
        get { return bestReachUsage; }
    }


    // =========================================================
    // TEST
    // =========================================================

    [ContextMenu("Test Foot Transition")]
    public void TestFootTransition()
    {
        ResetResult();


        if (!ReferencesComplete())
        {
            Debug.LogError(
                $"{name}: Foot Transition Tester引用没有填写完整。"
            );

            return;
        }


        // =============================================
        // 所有计算交给Solver
        // =============================================

        ClimberTransitionSolver2D.TransitionResult result =
            transitionSolver.SolveFootTransition(
                supportHandPoint.position,
                supportFootPoint.position,
                targetFootPoint.position,
                true
);
        lastResult = result;

        // =============================================
        // Solver结果复制到Inspector
        // =============================================

        transitionValid =
            result.valid;

        bestReachUsage =
            result.bestReachUsage;

        bestChestPosition =
            result.bestChestPosition;

        bestHipPosition =
            result.bestHipPosition;

        testedHipPositions =
            result.testedBodyPositions;

        validBridgePoseCount =
            result.validBridgePoseCount;

        testedPoseConfigurationCount =
            result.testedPoseConfigurationCount;

        poseFreedomRatio =
            result.poseFreedomRatio;

        averageReachUsage =
            result.averageReachUsage;

        medianReachUsage =
            result.medianReachUsage;

        reachUsageP75 =
            result.reachUsageP75;

        reachUsageP90 =
            result.reachUsageP90;

        // =============================================
        // Console
        // =============================================

        if (transitionValid)
        {
            Debug.Log(
                $"FOOT TRANSITION VALID\n" +
                $"Best Reach Usage = " +
                $"{bestReachUsage * 100f:F1}%\n" +
                $"Valid Bridge Poses = " +
                $"{validBridgePoseCount}\n" +
                $"Tested Hip Positions = " +
                $"{testedHipPositions}"
            );
        }
        else
        {
            Debug.Log(
                $"FOOT TRANSITION INVALID\n" +
                $"Tested Hip Positions = " +
                $"{testedHipPositions}"
            );
        }
    }


    // =========================================================
    // Reset
    // =========================================================

    private void ResetResult()
    {
        transitionValid =
            false;

        bestReachUsage =
            0f;

        bestChestPosition =
            Vector2.zero;

        bestHipPosition =
            Vector2.zero;

        testedHipPositions =
            0;

        validBridgePoseCount =
            0;
    }


    // =========================================================
    // Reference Check
    // =========================================================

    private bool ReferencesComplete()
    {
        return
            transitionSolver != null
            &&
            supportHandPoint != null
            &&
            supportFootPoint != null
            &&
            targetFootPoint != null;
    }


    // =========================================================
    // Gizmo
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawDebug)
        {
            return;
        }


        // =============================================
        // Safe Reach圆
        // =============================================

        if (drawSafeReachCircles &&
            reachProfile != null)
        {
            float armReach =
                reachProfile.SafeArmMaxReach;

            float legReach =
                reachProfile.SafeLegMaxReach;


            // Support Hand
            if (supportHandPoint != null)
            {
                Gizmos.color =
                    Color.cyan;

                Gizmos.DrawWireSphere(
                    supportHandPoint.position,
                    armReach
                );
            }


            // Support Foot
            if (supportFootPoint != null)
            {
                Gizmos.color =
                    Color.yellow;

                Gizmos.DrawWireSphere(
                    supportFootPoint.position,
                    legReach
                );
            }


            // Target Foot
            if (targetFootPoint != null)
            {
                Gizmos.color =
                    Color.green;

                Gizmos.DrawWireSphere(
                    targetFootPoint.position,
                    legReach
                );
            }
        }


        // =============================================
        // 无合法Bridge Pose
        // =============================================

        if (!transitionValid)
        {
            return;
        }


        if (supportHandPoint == null ||
            supportFootPoint == null ||
            targetFootPoint == null)
        {
            return;
        }


        // =============================================
        // 最佳Bridge Pose
        // =============================================

        Gizmos.color =
            Color.white;


        // Chest
        Gizmos.DrawWireSphere(
            bestChestPosition,
            0.08f
        );


        // Hip
        Gizmos.DrawWireSphere(
            bestHipPosition,
            0.08f
        );


        // Support Hand -> Chest
        Gizmos.DrawLine(
            supportHandPoint.position,
            bestChestPosition
        );


        // Chest -> Hip
        Gizmos.DrawLine(
            bestChestPosition,
            bestHipPosition
        );


        // Hip -> Support Foot
        Gizmos.DrawLine(
            bestHipPosition,
            supportFootPoint.position
        );


        // Hip -> Target Foot
        Gizmos.DrawLine(
            bestHipPosition,
            targetFootPoint.position
        );

        // =========================================================
        // COM Feasible Region
        // =========================================================

        
    }
}