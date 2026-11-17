using UnityEngine;

/// <summary>
/// Hand Transition 的可视化测试工具。
///
/// 真正的计算全部交给
/// ClimberTransitionSolver2D。
///
/// 测试：
/// (H1, F1) -> (H2, F1)
/// </summary>
[DisallowMultipleComponent]
public class HandTransitionTester2D : MonoBehaviour
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

    [Header("测试的 Hand Transition")]

    [Tooltip("当前保持抓住的手点 H1")]
    [SerializeField]
    private Transform supportHandPoint;

    [Tooltip("当前保持踩住的脚点 F1")]
    [SerializeField]
    private Transform supportFootPoint;

    [Tooltip("另一只手准备抓的新点 H2")]
    [SerializeField]
    private Transform targetHandPoint;


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
    private int testedChestPositions;

    [SerializeField]
    private int validBridgePoseCount;

    private ClimberTransitionSolver2D.TransitionResult lastResult;

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
    // Sidepull COM Analysis
    // =========================================================

    [Header("Sidepull COM Analysis")]

    [SerializeField]
    private bool hasSidepullInTransition;

    [SerializeField]
    private SidepullHold2D supportHandSidepull;

    [SerializeField]
    private SidepullHold2D targetHandSidepull;


    [SerializeField]
    private int totalComSamples;

    [SerializeField]
    private int stableSampleCount;

    [SerializeField]
    private int weakSampleCount;

    [SerializeField]
    private int strainSampleCount;

    [SerializeField]
    private int beyondFallSampleCount;


    [Range(0f, 1f)]
    [SerializeField]
    private float stableRatio;

    [Range(0f, 1f)]
    [SerializeField]
    private float weakRatio;

    [Range(0f, 1f)]
    [SerializeField]
    private float strainRatio;

    [Range(0f, 1f)]
    [SerializeField]
    private float beyondFallRatio;

    // =========================================================
    // Find Sidepull
    // =========================================================

    private SidepullHold2D FindSidepullForPoint(
        Transform point
    )
    {
        if (point == null)
        {
            return null;
        }


        SidepullHold2D sidepull =
            point.GetComponentInParent<
                SidepullHold2D
            >();


        if (sidepull != null)
        {
            return sidepull;
        }


        sidepull =
            point.GetComponentInChildren<
                SidepullHold2D
            >(true);


        return sidepull;
    }

    // =========================================================
    // Evaluate One COM Sample
    // =========================================================

    private void EvaluateComSample(
        Vector2 centerOfMass,
        out SidepullHold2D.SidepullState worstState,
        out bool beyondFall
    )
    {
        worstState =
            SidepullHold2D.SidepullState.Stable;

        beyondFall =
            false;


        // =====================================================
        // H1和H2属于同一个Sidepull
        //
        // 游戏实际规则：
        // 双手同抓一个Sidepull → Grip Point取中点
        // =====================================================

        if (supportHandSidepull != null &&
            targetHandSidepull != null &&
            supportHandSidepull ==
            targetHandSidepull)
        {
            Vector2 midpoint =
                (
                    (Vector2)supportHandPoint.position
                    +
                    (Vector2)targetHandPoint.position
                )
                * 0.5f;


            SidepullHold2D.SidepullEvaluation evaluation =
                supportHandSidepull.EvaluateAt(
                    midpoint,
                    centerOfMass
                );


            worstState =
                evaluation.state;

            beyondFall =
                evaluation.beyondFallBoundary;


            return;
        }


        // =====================================================
        // Support Hand Sidepull
        // =====================================================

        if (supportHandSidepull != null)
        {
            SidepullHold2D.SidepullEvaluation evaluation =
                supportHandSidepull.EvaluateAt(
                    supportHandPoint.position,
                    centerOfMass
                );


            worstState =
                GetWorseState(
                    worstState,
                    evaluation.state
                );


            if (evaluation.beyondFallBoundary)
            {
                beyondFall =
                    true;
            }
        }


        // =====================================================
        // Target Hand Sidepull
        // =====================================================

        if (targetHandSidepull != null)
        {
            SidepullHold2D.SidepullEvaluation evaluation =
                targetHandSidepull.EvaluateAt(
                    targetHandPoint.position,
                    centerOfMass
                );


            worstState =
                GetWorseState(
                    worstState,
                    evaluation.state
                );


            if (evaluation.beyondFallBoundary)
            {
                beyondFall =
                    true;
            }
        }
    }

    // =========================================================
    // Analyze COM Samples Against Sidepull
    // =========================================================

    private void AnalyzeSidepullComSamples()
    {
        ResetSidepullAnalysis();


        if (lastResult == null ||
            lastResult.poseSamples == null ||
            lastResult.poseSamples.Count == 0)
        {
            return;
        }


        // =====================================================
        // 自动判断H1 / H2是不是Sidepull
        // =====================================================

        supportHandSidepull =
            FindSidepullForPoint(
                supportHandPoint
            );

        targetHandSidepull =
            FindSidepullForPoint(
                targetHandPoint
            );


        hasSidepullInTransition =
            supportHandSidepull != null
            ||
            targetHandSidepull != null;


        if (!hasSidepullInTransition)
        {
            return;
        }


        totalComSamples =
            lastResult.poseSamples.Count;


        // =====================================================
        // 遍历全部COM
        // =====================================================

        foreach (
            ClimberTransitionSolver2D.PoseSample sample
            in lastResult.poseSamples
        )
        {
            SidepullHold2D.SidepullState state;

            bool beyondFall;


            EvaluateComSample(
                sample.centerOfMass,
                out state,
                out beyondFall
            );


            // =============================================
            // Stable / Weak / Strain
            // =============================================

            switch (state)
            {
                case SidepullHold2D.SidepullState.Weak:

                    weakSampleCount++;

                    break;


                case SidepullHold2D.SidepullState.Strain:

                    strainSampleCount++;

                    break;


                default:

                    stableSampleCount++;

                    break;
            }


            // =============================================
            // Fall是独立统计
            // =============================================

            if (beyondFall)
            {
                beyondFallSampleCount++;
            }
        }


        // =====================================================
        // Ratio
        // =====================================================

        if (totalComSamples > 0)
        {
            stableRatio =
                stableSampleCount
                / (float)totalComSamples;

            weakRatio =
                weakSampleCount
                / (float)totalComSamples;

            strainRatio =
                strainSampleCount
                / (float)totalComSamples;

            beyondFallRatio =
                beyondFallSampleCount
                / (float)totalComSamples;
        }


        Debug.Log(
            $"========== SIDEPULL COM ANALYSIS ==========\n" +
            $"Total COM Samples = {totalComSamples}\n" +
            $"Stable = {stableSampleCount} " +
            $"({stableRatio * 100f:F1}%)\n" +
            $"Weak = {weakSampleCount} " +
            $"({weakRatio * 100f:F1}%)\n" +
            $"Strain = {strainSampleCount} " +
            $"({strainRatio * 100f:F1}%)\n" +
            $"Beyond Fall Boundary = " +
            $"{beyondFallSampleCount} " +
            $"({beyondFallRatio * 100f:F1}%)"
        );
    }

    // =========================================================
    // Worst Sidepull State
    // =========================================================

    private SidepullHold2D.SidepullState GetWorseState(
        SidepullHold2D.SidepullState a,
        SidepullHold2D.SidepullState b
    )
    {
        if (GetStateSeverity(b) >
            GetStateSeverity(a))
        {
            return b;
        }


        return a;
    }


    private int GetStateSeverity(
        SidepullHold2D.SidepullState state
    )
    {
        switch (state)
        {
            case SidepullHold2D.SidepullState.Strain:
                return 2;

            case SidepullHold2D.SidepullState.Weak:
                return 1;

            default:
                return 0;
        }
    }

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

    [ContextMenu("Test Hand Transition")]
    public void TestHandTransition()
    {
        ResetResult();


        if (!ReferencesComplete())
        {
            Debug.LogError(
                $"{name}: Hand Transition Tester引用没有填写完整。"
            );

            return;
        }


        // =============================================
        // 所有计算交给Solver
        // =============================================

        ClimberTransitionSolver2D.TransitionResult result =
             transitionSolver.SolveHandTransition(
             supportHandPoint.position,
             supportFootPoint.position,
             targetHandPoint.position,
        true
    );

        lastResult = result;


        // =============================================
        // 把Solver结果复制到Inspector
        // =============================================

        transitionValid =
            result.valid;

        bestReachUsage =
            result.bestReachUsage;

        bestChestPosition =
            result.bestChestPosition;

        bestHipPosition =
            result.bestHipPosition;

        testedChestPositions =
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
        // 新增
        AnalyzeSidepullComSamples();

        // =============================================
        // Console
        // =============================================

        if (transitionValid)
        {
            Debug.Log(
                $"HAND TRANSITION VALID\n" +
                $"Best Reach Usage = " +
                $"{bestReachUsage * 100f:F1}%\n" +
                $"Valid Bridge Poses = " +
                $"{validBridgePoseCount}\n" +
                $"Tested Chest Positions = " +
                $"{testedChestPositions}"
            );
        }
        else
        {
            Debug.Log(
                $"HAND TRANSITION INVALID\n" +
                $"Tested Chest Positions = " +
                $"{testedChestPositions}"
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

        testedChestPositions =
            0;

        validBridgePoseCount =
            0;
        ResetSidepullAnalysis();
    }
    private void ResetSidepullAnalysis()
    {
        hasSidepullInTransition =
            false;

        supportHandSidepull =
            null;

        targetHandSidepull =
            null;


        totalComSamples =
            0;

        stableSampleCount =
            0;

        weakSampleCount =
            0;

        strainSampleCount =
            0;

        beyondFallSampleCount =
            0;


        stableRatio =
            0f;

        weakRatio =
            0f;

        strainRatio =
            0f;

        beyondFallRatio =
            0f;
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
            targetHandPoint != null;
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


            // Target Hand
            if (targetHandPoint != null)
            {
                Gizmos.color =
                    Color.green;

                Gizmos.DrawWireSphere(
                    targetHandPoint.position,
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
        }


        // =============================================
        // 没有找到Bridge Pose
        // =============================================

        if (!transitionValid)
        {
            return;
        }


        if (supportHandPoint == null ||
            supportFootPoint == null ||
            targetHandPoint == null)
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


        // Chest -> Support Hand
        Gizmos.DrawLine(
            bestChestPosition,
            supportHandPoint.position
        );


        // Chest -> Target Hand
        Gizmos.DrawLine(
            bestChestPosition,
            targetHandPoint.position
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

        // =========================================================
        // COM Feasible Region
        // =========================================================

        if (drawComSamples &&
            lastResult != null &&
            lastResult.poseSamples != null)
        {
            foreach (
                ClimberTransitionSolver2D.PoseSample sample
                in lastResult.poseSamples
            )
            {
                // =============================================
                // 没有Sidepull
                // 保持紫色
                // =============================================

                if (!hasSidepullInTransition)
                {
                    Gizmos.color =
                        Color.magenta;
                }

                // =============================================
                // 有Sidepull
                // 根据COM状态分类颜色
                // =============================================

                else
                {
                    SidepullHold2D.SidepullState state;

                    bool beyondFall;


                    EvaluateComSample(
                        sample.centerOfMass,
                        out state,
                        out beyondFall
                    );


                    // =========================================
                    // Fall Boundary优先显示黑色
                    // =========================================

                    if (beyondFall)
                    {
                        Gizmos.color =
                            Color.black;
                    }
                    else
                    {
                        switch (state)
                        {
                            case SidepullHold2D.SidepullState.Weak:

                                Gizmos.color =
                                    Color.yellow;

                                break;


                            case SidepullHold2D.SidepullState.Strain:

                                Gizmos.color =
                                    Color.red;

                                break;


                            default:

                                Gizmos.color =
                                    Color.green;

                                break;
                        }
                    }
                }


                Gizmos.DrawSphere(
                    sample.centerOfMass,
                    comSampleRadius
                );
            }
        }
    }
}