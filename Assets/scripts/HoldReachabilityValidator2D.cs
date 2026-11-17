using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
/// <summary>
/// Automatically scans all holds in the level,
/// and builds the first-stage candidate Support States.
///
/// A Support State is defined as:
///
///     one hand gripping a hold
///     +
///     one foot supporting on a hold
///
/// This stage does not build the Transition Graph yet.
/// </summary>
[DisallowMultipleComponent]
public class HoldReachabilityValidator2D : MonoBehaviour
{
    // =========================================================
    // Hold Information
    // =========================================================

    [System.Serializable]
    public class ScannedHoldInfo
    {
        public string displayName;

        public HoldGripCapacity2D hold;

        public Collider2D collider;

        public Vector2 validationPoint;

        public bool usesCustomValidationPoint;

        public bool isSidepull;

        public SidepullHold2D sidepull;

        public HoldLevelPoint2D.PointRole role;

        public int maxGripCount;

        public bool isStartHandPoint;

        public bool isStartFootPoint;

        public bool isFinishPoint;

        // =====================================================
        // Reachability / Route Analysis
        // =====================================================

        [Header("Reachability")]

        public bool reachableFromStart;

        public bool reachableAsHand;

        public bool reachableAsFoot;


        [Header("Route Membership")]

        public bool onShortestRoute;

        public bool onEasiestRoute;
    }
    // =========================================================
    // Difficulty Settings
    // =========================================================

    [Header("Difficulty - Movement")]

    [Tooltip(
        "When Reach P75 is below this value, " +
        "reach contributes almost no additional difficulty."
    )]
    [Range(0f, 1f)]
    [SerializeField]
    private float easyReachUsage =
        0.5f;


    [Tooltip(
        "When Reach P75 reaches this value, " +
        "Reach Difficulty is treated as 1."
    )]
    [Range(0f, 1f)]
    [SerializeField]
    private float hardReachUsage =
        1.0f;


    [Tooltip("Weight of Reach in the base movement difficulty.")]
    [Min(0f)]
    [SerializeField]
    private float reachDifficultyWeight =
        0.55f;


    [Tooltip("Weight of Pose Freedom in the base movement difficulty.")]
    [Min(0f)]
    [SerializeField]
    private float poseDifficultyWeight =
        0.45f;
    [Header("Difficulty - Sidepull")]

    [Tooltip("Difficulty assigned to a Weak COM sample.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float weakDifficulty =
    0.35f;


    [Tooltip("Difficulty assigned to a Strain COM sample.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float strainDifficulty =
        0.75f;


    [Tooltip(
        "Additional penalty when the COM crosses the Fall Boundary. " +
        "Because Fall is independent from Stable/Weak/Strain, " +
        "this penalty is added separately."
    )]
    [Range(0f, 1f)]
    [SerializeField]
    private float fallDifficultyPenalty =
        0.6f;


    [Tooltip(
        "Strength of the Sidepull contribution to final movement difficulty. " +
        "A value of 1 applies the full effect."
    )]
    [Range(0f, 1f)]
    [SerializeField]
    private float sidepullInfluence =
        1f;

    public IReadOnlyList<TransitionEdgeInfo> ValidTransitions
    {
        get { return validTransitions; }
    }

    // =========================================================
    // Whether this transition requires COM Sidepull analysis
    // =========================================================

    private bool TransitionHasRelevantSidepull(
        SupportStateInfo fromState,
        SupportStateInfo toState,
        TransitionType type
    )
    {
        if (fromState == null ||
            toState == null)
        {
            return false;
        }


        // =====================================================
        // Hand Move
        //
        // H1 + F1 -> H2 + F1
        //
        // Bridge phase:
        // Both H1 and H2 are in use.
        // =====================================================

        if (type == TransitionType.HandMove)
        {
            ScannedHoldInfo supportHandInfo =
                FindScannedHoldInfo(
                    fromState.handHold
                );

            ScannedHoldInfo targetHandInfo =
                FindScannedHoldInfo(
                    toState.handHold
                );


            bool supportIsSidepull =
                supportHandInfo != null
                &&
                supportHandInfo.sidepull != null;


            bool targetIsSidepull =
                targetHandInfo != null
                &&
                targetHandInfo.sidepull != null;


            return
                supportIsSidepull
                ||
                targetIsSidepull;
        }


        // =====================================================
        // Foot Move
        //
        // H1 + F1 -> H1 + F2
        //
        // Only H1 participates in Sidepull evaluation.
        // =====================================================

        ScannedHoldInfo handInfo =
            FindScannedHoldInfo(
                fromState.handHold
            );


        return
            handInfo != null
            &&
            handInfo.sidepull != null;
    }

    // =========================================================
    // Automatically analyze the Sidepull COM distribution for a transition
    // =========================================================

    private void AnalyzeSidepullForTransition(
        TransitionEdgeInfo edge,
        SupportStateInfo fromState,
        SupportStateInfo toState,
        ClimberTransitionSolver2D.TransitionResult result
    )
    {
        // =====================================================
        // Default: no Sidepull analysis
        // =====================================================

        edge.hasSidepullAnalysis =
            false;

        edge.totalComSamples =
            0;

        edge.stableSampleCount =
            0;

        edge.weakSampleCount =
            0;

        edge.strainSampleCount =
            0;

        edge.beyondFallSampleCount =
            0;

        edge.stableRatio =
            0f;

        edge.weakRatio =
            0f;

        edge.strainRatio =
            0f;

        edge.beyondFallRatio =
            0f;


        if (result == null ||
            result.poseSamples == null ||
            result.poseSamples.Count == 0)
        {
            return;
        }


        // =====================================================
        // Check whether this edge actually involves a Sidepull
        // =====================================================

        if (!TransitionHasRelevantSidepull(
                fromState,
                toState,
                edge.type))
        {
            return;
        }


        edge.hasSidepullAnalysis =
            true;

        edge.totalComSamples =
            result.poseSamples.Count;


        // =====================================================
        // Iterate through all valid COM samples
        // =====================================================

        foreach (
            ClimberTransitionSolver2D.PoseSample sample
            in result.poseSamples
        )
        {
            SidepullHold2D.SidepullState state;

            bool beyondFall;


            EvaluateTransitionComSample(
                fromState,
                toState,
                edge.type,
                sample.centerOfMass,
                out state,
                out beyondFall
            );


            // =================================================
            // Stable / Weak / Strain
            // =================================================

            switch (state)
            {
                case SidepullHold2D.SidepullState.Weak:

                    edge.weakSampleCount++;

                    break;


                case SidepullHold2D.SidepullState.Strain:

                    edge.strainSampleCount++;

                    break;


                default:

                    edge.stableSampleCount++;

                    break;
            }


            // =================================================
            // Fall is counted independently
            // =================================================

            if (beyondFall)
            {
                edge.beyondFallSampleCount++;
            }
        }


        // =====================================================
        // Ratio
        // =====================================================

        if (edge.totalComSamples > 0)
        {
            float total =
                edge.totalComSamples;


            edge.stableRatio =
                edge.stableSampleCount
                / total;

            edge.weakRatio =
                edge.weakSampleCount
                / total;

            edge.strainRatio =
                edge.strainSampleCount
                / total;

            edge.beyondFallRatio =
                edge.beyondFallSampleCount
                / total;
        }
    }



    // =========================================================
    // Evaluate a single COM sample
    // =========================================================

    private void EvaluateTransitionComSample(
        SupportStateInfo fromState,
        SupportStateInfo toState,
        TransitionType type,
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
        // HAND MOVE
        //
        // H1 + F1 -> H2 + F1
        //
        // Bridge:
        // H1 + H2 + F1
        // =====================================================

        if (type == TransitionType.HandMove)
        {
            ScannedHoldInfo supportInfo =
                FindScannedHoldInfo(
                    fromState.handHold
                );

            ScannedHoldInfo targetInfo =
                FindScannedHoldInfo(
                    toState.handHold
                );


            SidepullHold2D supportSidepull =
                supportInfo != null
                ?
                supportInfo.sidepull
                :
                null;


            SidepullHold2D targetSidepull =
                targetInfo != null
                ?
                targetInfo.sidepull
                :
                null;


            // =================================================
            // Both hand points belong to the same Sidepull
            //
            // The current model uses the midpoint of the two points as the Grip Point.
            // =================================================

            if (supportSidepull != null &&
                targetSidepull != null &&
                supportSidepull == targetSidepull)
            {
                Vector2 midpoint =
                    (
                        fromState.handPoint
                        +
                        toState.handPoint
                    )
                    * 0.5f;


                SidepullHold2D.SidepullEvaluation evaluation =
                    supportSidepull.EvaluateAt(
                        midpoint,
                        centerOfMass
                    );


                worstState =
                    evaluation.state;

                beyondFall =
                    evaluation.beyondFallBoundary;


                return;
            }


            // =================================================
            // H1 Sidepull
            // =================================================

            if (supportSidepull != null)
            {
                SidepullHold2D.SidepullEvaluation evaluation =
                    supportSidepull.EvaluateAt(
                        fromState.handPoint,
                        centerOfMass
                    );


                worstState =
                    GetWorseSidepullState(
                        worstState,
                        evaluation.state
                    );


                if (evaluation.beyondFallBoundary)
                {
                    beyondFall =
                        true;
                }
            }


            // =================================================
            // H2 Sidepull
            // =================================================

            if (targetSidepull != null)
            {
                SidepullHold2D.SidepullEvaluation evaluation =
                    targetSidepull.EvaluateAt(
                        toState.handPoint,
                        centerOfMass
                    );


                worstState =
                    GetWorseSidepullState(
                        worstState,
                        evaluation.state
                    );


                if (evaluation.beyondFallBoundary)
                {
                    beyondFall =
                        true;
                }
            }


            return;
        }


        // =====================================================
        // FOOT MOVE
        //
        // H1 + F1 -> H1 + F2
        //
        // The hand does not change, so only H1 is evaluated.
        // =====================================================

        ScannedHoldInfo handInfo =
            FindScannedHoldInfo(
                fromState.handHold
            );


        if (handInfo == null ||
            handInfo.sidepull == null)
        {
            return;
        }


        SidepullHold2D.SidepullEvaluation footMoveEvaluation =
            handInfo.sidepull.EvaluateAt(
                fromState.handPoint,
                centerOfMass
            );


        worstState =
            footMoveEvaluation.state;

        beyondFall =
            footMoveEvaluation.beyondFallBoundary;
    }

    // =========================================================
    // Compare Sidepull states
    // =========================================================

    private SidepullHold2D.SidepullState GetWorseSidepullState(
        SidepullHold2D.SidepullState a,
        SidepullHold2D.SidepullState b
    )
    {
        if (GetSidepullSeverity(b) >
            GetSidepullSeverity(a))
        {
            return b;
        }


        return a;
    }


    private int GetSidepullSeverity(
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
    // Support State
    // =========================================================


    [System.Serializable]
    public class SupportStateInfo
    {
        public string displayName;

        public HoldGripCapacity2D handHold;
        public HoldGripCapacity2D footHold;

        public Vector2 handPoint;
        public Vector2 footPoint;

        public bool sameHold;

        public float handFootDistance;


        // =====================================================
        // BFS / Reachability
        // =====================================================

        [Header("Reachability")]

        public bool reachableFromStart;

        /// <summary>
        /// Minimum number of transitions required to reach this state from Start.
        /// The Start State itself = 0.
        /// Unreachable = -1.
        /// </summary>
        public int distanceFromStart = -1;

        /// <summary>
        /// The previous state before reaching this state in BFS.
        /// Used to reconstruct the final route.
        /// </summary>
        public int previousStateIndex = -1;

        /// <summary>
        /// The edge in validTransitions used to reach this state.
        /// </summary>
        public int incomingTransitionIndex = -1;

        /// <summary>
        /// Whether the selected guaranteed solution route passes through this state.
        /// </summary>
        public bool onSolutionPath;

        // =====================================================
        // Easiest Route Search
        // =====================================================

        public bool easiestRouteReached;

        public bool easiestRouteSettled;

        public float easiestPeakDifficulty;

        public float easiestTotalDifficulty;

        public int easiestMoveCount = -1;

        public int easiestPreviousStateIndex = -1;

        public int easiestIncomingTransitionIndex = -1;

        public bool onEasiestSolutionPath;
    }

    // =========================================================
    // Transition
    // =========================================================

    public enum TransitionType
    {
        HandMove,
        FootMove
    }


    [System.Serializable]
    public class TransitionEdgeInfo
    {
        public string displayName;

        public int fromStateIndex;
        public int toStateIndex;

        public TransitionType type;

        public float bestReachUsage;

        [Header("Pose Freedom")]

        public int testedPoseConfigurationCount;

        [Range(0f, 1f)]
        public float poseFreedomRatio;


        [Header("Reach Distribution")]

        public float averageReachUsage;

        public float medianReachUsage;

        public float reachUsageP75;

        public float reachUsageP90;

        public Vector2 bestChestPosition;
        public Vector2 bestHipPosition;

        public int validBridgePoseCount;


        // =====================================================
        // Sidepull COM Analysis
        // =====================================================

        [Header("Sidepull COM Analysis")]

        public bool hasSidepullAnalysis;

        public int totalComSamples;

        public int stableSampleCount;

        public int weakSampleCount;

        public int strainSampleCount;

        public int beyondFallSampleCount;


        [Range(0f, 1f)]
        public float stableRatio;

        [Range(0f, 1f)]
        public float weakRatio;

        [Range(0f, 1f)]
        public float strainRatio;

        [Range(0f, 1f)]
        public float beyondFallRatio;

        // =====================================================
        // Difficulty Model
        // =====================================================

        [Header("Difficulty")]

        [Range(0f, 1f)]
        public float poseDifficulty;

        [Range(0f, 1f)]
        public float reachDifficulty;

        [Range(0f, 1f)]
        public float sidepullDifficulty;

        [Range(0f, 1f)]
        public float baseMovementDifficulty;

        [Range(0f, 1f)]
        public float overallDifficulty;

    }


    // =========================================================
    // References
    // =========================================================

    [Header("Solver")]

    [SerializeField]
    private ClimberTransitionSolver2D transitionSolver;


    // =========================================================
    // Scan Results
    // =========================================================

    [Header("Scan Results")]

    [SerializeField]
    private List<ScannedHoldInfo> scannedHolds =
        new List<ScannedHoldInfo>();


    [Header("Candidate Support States")]

    [SerializeField]
    private List<SupportStateInfo> candidateSupportStates =
        new List<SupportStateInfo>();

    [Header("Valid Transitions")]

    [SerializeField]
    private List<TransitionEdgeInfo> validTransitions =
    new List<TransitionEdgeInfo>();

    [Header("Start -> Finish Search Results")]

    [SerializeField]
    private bool finishReachable;

    [SerializeField]
    private int reachableStateCount;

    [SerializeField]
    private int unreachableStateCount;

    [SerializeField]
    private int solutionFinishStateIndex = -1;

    [SerializeField]
    private int solutionStepCount = -1;


    [Header("Guaranteed Solution Route")]

    [SerializeField]
    private List<int> solutionStateIndices =
        new List<int>();

    [SerializeField]
    private List<int> solutionTransitionIndices =
        new List<int>();

    [Header("Easiest Guaranteed Route")]

    [SerializeField]
    private bool easiestFinishReachable;

    [SerializeField]
    private int easiestFinishStateIndex =
        -1;

    [SerializeField]
    private float easiestRoutePeakDifficulty;

    [SerializeField]
    private float easiestRouteTotalDifficulty;

    [SerializeField]
    private int easiestRouteMoveCount =
        -1;


    [SerializeField]
    private List<int> easiestSolutionStateIndices =
        new List<int>();

    [SerializeField]
    private List<int> easiestSolutionTransitionIndices =
        new List<int>();

    // =========================================================
    // Statistics
    // =========================================================

    [Header("Statistics")]

    [SerializeField]
    private int scannedHoldCount;

    [SerializeField]
    private int startHandHoldCount;

    [SerializeField]
    private int startFootHoldCount;

    [SerializeField]
    private int finishHoldCount;

    [SerializeField]
    private int rawSupportPairCount;

    [SerializeField]
    private int rejectedByCapacity;

    [SerializeField]
    private int rejectedByGeometry;

    [SerializeField]
    private int candidateSupportStateCount;

    [SerializeField]
    private int testedHandTransitionCount;

    [SerializeField]
    private int validHandTransitionCount;

    [SerializeField]
    private int testedFootTransitionCount;

    [SerializeField]
    private int validFootTransitionCount;

    [SerializeField]
    private int totalValidTransitionCount;

    [SerializeField]
    private int reachableHoldCount;

    [SerializeField]
    private int unreachableHoldCount;

    [SerializeField]
    private int shortestRouteHoldCount;

    [SerializeField]
    private int easiestRouteHoldCount;


    // =========================================================
    // Debug
    // =========================================================

    [Header("Gizmo")]

    [SerializeField]
    private bool drawHoldPoints = true;

    [SerializeField]
    private bool drawSelectedSupportState = true;

    [Tooltip(
    "Display hold reachability and route classification for the entire level."
)]
    [SerializeField]
    private bool drawHoldRouteAnalysis = true;


    [Min(0.01f)]
    [SerializeField]
    private float holdRouteAnalysisRadius =
        0.13f;

    [Min(0)]
    [SerializeField]
    private int debugSupportStateIndex = 0;

    [Min(0.01f)]
    [SerializeField]
    private float holdPointGizmoRadius = 0.08f;


    // =========================================================
    // Public
    // =========================================================

    public IReadOnlyList<ScannedHoldInfo> ScannedHolds
    {
        get { return scannedHolds; }
    }

    public IReadOnlyList<SupportStateInfo> CandidateSupportStates
    {
        get { return candidateSupportStates; }
    }


    // =========================================================
    // MAIN
    // =========================================================

    [ContextMenu("Scan Holds And Build Support States")]
    public void ScanHoldsAndBuildSupportStates()
    {
        ClearResults();


        if (transitionSolver == null)
        {
            Debug.LogError(
                $"{name}: ClimberTransitionSolver2D is not assigned."
            );

            return;
        }


        ScanAllHolds();

        BuildCandidateSupportStates();

        BuildTransitionGraph();


        // Shortest Route / Reachability
        FindReachableStatesFromStart();


        // Easiest Route
        FindEasiestRoute();


        // Hold-level analysis
        AnalyzeHoldReachability();


        // Step Debugger
        RefreshEasiestStepDebug();


        UpdateStatistics();


        Debug.Log(
                $"========== REACHABILITY SCAN COMPLETE ==========\n" +

                $"Holds = {scannedHoldCount}\n" +

                $"Start Hand Holds = {startHandHoldCount}\n" +
                $"Start Foot Holds = {startFootHoldCount}\n" +
                $"Finish Holds = {finishHoldCount}\n\n" +

                $"Raw Support Pairs = {rawSupportPairCount}\n" +
                $"Rejected By Capacity = {rejectedByCapacity}\n" +
                $"Rejected By Geometry = {rejectedByGeometry}\n" +
                $"Candidate Support States = {candidateSupportStateCount}\n\n" +

                $"Tested Hand Transitions = {testedHandTransitionCount}\n" +
                $"Valid Hand Transitions = {validHandTransitionCount}\n\n" +

                $"Tested Foot Transitions = {testedFootTransitionCount}\n" +
                $"Valid Foot Transitions = {validFootTransitionCount}\n\n" +

                $"TOTAL VALID TRANSITIONS = {totalValidTransitionCount}"
            );
        Debug.Log(
            $"========== LEVEL VALIDATION ==========\n" +

            $"Reachable States = {reachableStateCount}\n" +
            $"Unreachable States = {unreachableStateCount}\n" +

            $"Finish Reachable = {finishReachable}\n" +

            (
                finishReachable
                ?
                $"Guaranteed Solution Steps = {solutionStepCount}"
                :
                "NO GUARANTEED PATH TO FINISH"
            )


            );
        Debug.Log(
             $"========== EASIEST ROUTE ==========\n" +

             $"Finish Reachable = " +
             $"{easiestFinishReachable}\n" +

             (
                 easiestFinishReachable
                 ?
                 $"Moves = {easiestRouteMoveCount}\n" +
                 $"Peak Difficulty = " +
                 $"{easiestRoutePeakDifficulty:F3}\n" +
                $"Total Difficulty = " +
                $"{easiestRouteTotalDifficulty:F3}"
        :
                "NO EASIEST ROUTE FOUND"
                )
                );
    }


    // =========================================================
    // Scan Holds
    // =========================================================

    private void ScanAllHolds()
    {
        HoldGripCapacity2D[] holds =
        FindObjectsByType<HoldGripCapacity2D>();



        foreach (HoldGripCapacity2D hold in holds)
        {
            if (hold == null)
            {
                continue;
            }


            Collider2D collider =
                FindHoldCollider(hold);


            if (collider == null)
            {
                Debug.LogWarning(
                    $"{hold.name}: " +
                    $"HoldGripCapacity2D was found, " +
                    $"but no Trigger Collider2D was found."
                );

                continue;
            }


            ScannedHoldInfo info =
                CreateHoldInfo(
                    hold,
                    collider
                );


            scannedHolds.Add(info);
        }


        /*
         * Sort by name.
         *
         * This keeps the Inspector order as consistent as possible
         * after each rescan, which makes debugging easier.
         */
        scannedHolds.Sort(
            (a, b) =>
                string.Compare(
                    a.displayName,
                    b.displayName,
                    System.StringComparison.Ordinal
                )
        );
    }


    // =========================================================
    // Find the hold collider
    // =========================================================

    private Collider2D FindHoldCollider(
        HoldGripCapacity2D hold
    )
    {
        Collider2D[] colliders =
            hold.GetComponentsInChildren<Collider2D>(
                true
            );


        // Prefer a trigger collider
        foreach (Collider2D collider in colliders)
        {
            if (collider != null &&
                collider.isTrigger)
            {
                return collider;
            }
        }


        // Fall back to the first collider if no trigger exists
        if (colliders.Length > 0)
        {
            return colliders[0];
        }


        return null;
    }


    // =========================================================
    // Build Hold Info
    // =========================================================

    private ScannedHoldInfo CreateHoldInfo(
        HoldGripCapacity2D hold,
        Collider2D collider
    )
    {
        ScannedHoldInfo info =
            new ScannedHoldInfo();


        info.displayName =
            hold.name;

        info.hold =
            hold;

        info.collider =
            collider;

        info.maxGripCount =
            hold.MaxGripCount;


        // =====================================================
        // Validator Point
        // =====================================================

        HoldValidationPoint2D customPoint =
            hold.GetComponentInChildren<
                HoldValidationPoint2D
            >(true);


        if (customPoint != null)
        {
            info.validationPoint =
                customPoint.transform.position;

            info.usesCustomValidationPoint =
                true;
        }
        else
        {
            info.validationPoint =
                collider.bounds.center;

            info.usesCustomValidationPoint =
                false;
        }


        // =====================================================
        // Sidepull
        // =====================================================

        SidepullHold2D sidepull =
     hold.GetComponentInChildren<SidepullHold2D>(true);

        if (sidepull == null)
        {
            sidepull =
                hold.GetComponentInParent<SidepullHold2D>();
        }

        info.sidepull =
            sidepull;

        info.isSidepull =
            sidepull != null;


        // =====================================================
        // Start / Finish
        // =====================================================

        HoldLevelPoint2D levelPoint =
            hold.GetComponentInChildren<
                HoldLevelPoint2D
            >(true);


        if (levelPoint == null)
        {
            levelPoint =
                hold.GetComponentInParent<
                    HoldLevelPoint2D
                >();
        }


        if (levelPoint != null)
        {
            info.role =
                levelPoint.Role;


            if (levelPoint.Role ==
                HoldLevelPoint2D.PointRole.Start)
            {
                HoldLevelPoint2D.StartLimb limbs =
                    levelPoint.StartLimbs;


                bool leftHand =
                    (limbs &
                     HoldLevelPoint2D.StartLimb.LeftHand)
                    != 0;

                bool rightHand =
                    (limbs &
                     HoldLevelPoint2D.StartLimb.RightHand)
                    != 0;


                bool leftFoot =
                    (limbs &
                     HoldLevelPoint2D.StartLimb.LeftFoot)
                    != 0;

                bool rightFoot =
                    (limbs &
                     HoldLevelPoint2D.StartLimb.RightFoot)
                    != 0;


                info.isStartHandPoint =
                    leftHand
                    || rightHand;

                info.isStartFootPoint =
                    leftFoot
                    || rightFoot;
            }


            info.isFinishPoint =
                levelPoint.Role ==
                HoldLevelPoint2D.PointRole.Finish;
        }
        else
        {
            info.role =
                HoldLevelPoint2D.PointRole.None;
        }


        return info;
    }


    // =========================================================
    // Build Candidate Support States
    // =========================================================

    private void BuildCandidateSupportStates()
    {
        /*
         * First try every hold combination:
         *
         * Hand = Hold A
         * Foot = Hold B
         *
         * Therefore the theoretical number of combinations is approximately:
         *
         * HoldCount²
         */


        for (int handIndex = 0;
             handIndex < scannedHolds.Count;
             handIndex++)
        {
            ScannedHoldInfo handHold =
                scannedHolds[handIndex];


            for (int footIndex = 0;
                 footIndex < scannedHolds.Count;
                 footIndex++)
            {
                ScannedHoldInfo footHold =
                    scannedHolds[footIndex];


                rawSupportPairCount++;


                // =============================================
                // The same hold is used by both a hand and a foot
                // =============================================

                bool sameHold =
                    handHold.hold ==
                    footHold.hold;


                if (sameHold)
                {
                    /*
                     * One hand + one foot
                     * use the same hold at the same time,
                     *
                     * so the hold requires a capacity of at least 2.
                     */

                    if (handHold.maxGripCount < 2)
                    {
                        rejectedByCapacity++;

                        continue;
                    }
                }


                // =============================================
                // Cheap Geometric Filter
                // =============================================

                bool geometricallyPossible =
                    transitionSolver
                        .IsSupportStateGeometricallyPossible(
                            handHold.validationPoint,
                            footHold.validationPoint
                        );


                if (!geometricallyPossible)
                {
                    rejectedByGeometry++;

                    continue;
                }


                // =============================================
                // Build candidate state
                // =============================================

                SupportStateInfo state =
                    new SupportStateInfo();


                state.handHold =
                    handHold.hold;

                state.footHold =
                    footHold.hold;

                state.handPoint =
                    handHold.validationPoint;

                state.footPoint =
                    footHold.validationPoint;

                state.sameHold =
                    sameHold;

                state.handFootDistance =
                    Vector2.Distance(
                        state.handPoint,
                        state.footPoint
                    );


                state.displayName =
                    $"H:{handHold.displayName} | " +
                    $"F:{footHold.displayName}";


                candidateSupportStates.Add(
                    state
                );
            }
        }
    }

    // =========================================================
    // Add Valid Transition
    // =========================================================

    private void AddTransition(
        int fromIndex,
        int toIndex,
        TransitionType type,
        ClimberTransitionSolver2D.TransitionResult result
    )
    {
        TransitionEdgeInfo edge =
            new TransitionEdgeInfo();


        edge.fromStateIndex =
            fromIndex;

        edge.toStateIndex =
            toIndex;

        edge.type =
            type;


        edge.bestReachUsage =
            result.bestReachUsage;

        edge.bestChestPosition =
            result.bestChestPosition;

        edge.bestHipPosition =
            result.bestHipPosition;

        edge.validBridgePoseCount =
            result.validBridgePoseCount;

        edge.testedPoseConfigurationCount =
            result.testedPoseConfigurationCount;

        edge.poseFreedomRatio =
            result.poseFreedomRatio;


        edge.averageReachUsage =
            result.averageReachUsage;

        edge.medianReachUsage =
            result.medianReachUsage;

        edge.reachUsageP75 =
            result.reachUsageP75;

        edge.reachUsageP90 =
            result.reachUsageP90;


        SupportStateInfo fromState =
            candidateSupportStates[fromIndex];

        SupportStateInfo toState =
            candidateSupportStates[toIndex];


        edge.displayName =
            $"S{fromIndex} → S{toIndex} " +
            $"({type}) | " +
            $"{fromState.displayName} → " +
            $"{toState.displayName}";

        AnalyzeSidepullForTransition(
            edge,
            fromState,
            toState,
            result
            );

        CalculateTransitionDifficulty(
            edge
            );

        validTransitions.Add(
            edge
        );
    }

    // =========================================================
    // Build Transition Graph
    // =========================================================

    private void BuildTransitionGraph()
    {
        validTransitions.Clear();


        int stateCount =
            candidateSupportStates.Count;


        // =====================================================
        // Use each state as a starting state
        // =====================================================

        for (int fromIndex = 0;
             fromIndex < stateCount;
             fromIndex++)
        {
            SupportStateInfo fromState =
                candidateSupportStates[fromIndex];


            // =================================================
            // Try to reach every other state
            // =================================================

            for (int toIndex = 0;
                 toIndex < stateCount;
                 toIndex++)
            {
                if (fromIndex == toIndex)
                {
                    continue;
                }


                SupportStateInfo toState =
                    candidateSupportStates[toIndex];


                // =================================================
                // HAND TRANSITION
                //
                // Same foot
                // Different hand
                //
                // (H1,F1) -> (H2,F1)
                // =================================================

                bool sameFoot =
                    fromState.footHold ==
                    toState.footHold;

                bool differentHand =
                    fromState.handHold !=
                    toState.handHold;


                if (sameFoot &&
                    differentHand)
                {
                    testedHandTransitionCount++;


                    bool needsComSamples =
                        TransitionHasRelevantSidepull(
                          fromState,
                            toState,
                             TransitionType.HandMove
                         );


                    ClimberTransitionSolver2D.TransitionResult result =
                        transitionSolver.SolveHandTransition(
                            fromState.handPoint,
                            fromState.footPoint,
                            toState.handPoint,
                            needsComSamples
                        );


                    if (result.valid)
                    {
                        AddTransition(
                            fromIndex,
                            toIndex,
                            TransitionType.HandMove,
                            result
                        );


                        validHandTransitionCount++;
                    }


                    /*
                     * This state pair is already identified as a Hand Move,
                     * so there is no need to check it as a Foot Move.
                     */
                    continue;
                }


                // =================================================
                // FOOT TRANSITION
                //
                // Same hand
                // Different foot
                //
                // (H1,F1) -> (H1,F2)
                // =================================================

                bool sameHand =
                    fromState.handHold ==
                    toState.handHold;

                bool differentFoot =
                    fromState.footHold !=
                    toState.footHold;


                if (sameHand &&
                    differentFoot)
                {
                    testedFootTransitionCount++;


                    bool needsComSamples =
                     TransitionHasRelevantSidepull(
                        fromState,
                        toState,
                        TransitionType.FootMove
                        );


                    ClimberTransitionSolver2D.TransitionResult result =
                        transitionSolver.SolveFootTransition(
                            fromState.handPoint,
                            fromState.footPoint,
                            toState.footPoint,
                            needsComSamples
                        );


                    if (result.valid)
                    {
                        AddTransition(
                            fromIndex,
                            toIndex,
                            TransitionType.FootMove,
                            result
                        );


                        validFootTransitionCount++;
                    }
                }


                /*
                 * If:
                 *
                 * the hand is different
                 * and the foot is also different
                 *
                 * do nothing.
                 *
                 * A single transition is not allowed to move both a hand and a foot.
                 */
            }
        }


        totalValidTransitionCount =
            validTransitions.Count;
    }

    // =========================================================
    // BFS: search the entire Transition Graph from Start
    // =========================================================

    private void FindReachableStatesFromStart()
    {
        // =====================================================
        // Clear results from the previous search
        // =====================================================

        finishReachable =
            false;

        solutionFinishStateIndex =
            -1;

        solutionStepCount =
            -1;

        solutionStateIndices.Clear();

        solutionTransitionIndices.Clear();


        // =====================================================
        // Reset all states
        // =====================================================

        for (int i = 0;
             i < candidateSupportStates.Count;
             i++)
        {
            SupportStateInfo state =
                candidateSupportStates[i];

            state.reachableFromStart =
                false;

            state.distanceFromStart =
                -1;

            state.previousStateIndex =
                -1;

            state.incomingTransitionIndex =
                -1;

            state.onSolutionPath =
                false;
        }


        // =====================================================
        // BFS Queue
        // =====================================================

        Queue<int> queue =
            new Queue<int>();


        // =====================================================
        // Find all valid Start Support States
        //
        // Requirements:
        // Hand Hold = a Start Hand Hold
        // Foot Hold = a Start Foot Hold
        // =====================================================

        int startStateCount =
            0;


        for (int i = 0;
             i < candidateSupportStates.Count;
             i++)
        {
            SupportStateInfo state =
                candidateSupportStates[i];


            ScannedHoldInfo handInfo =
                FindScannedHoldInfo(
                    state.handHold
                );

            ScannedHoldInfo footInfo =
                FindScannedHoldInfo(
                    state.footHold
                );


            if (handInfo == null ||
                footInfo == null)
            {
                continue;
            }


            bool validStartState =
                handInfo.isStartHandPoint
                &&
                footInfo.isStartFootPoint;


            if (!validStartState)
            {
                continue;
            }


            state.reachableFromStart =
                true;

            state.distanceFromStart =
                0;

            state.previousStateIndex =
                -1;

            state.incomingTransitionIndex =
                -1;


            queue.Enqueue(i);

            startStateCount++;
        }


        // =====================================================
        // No Start State found
        // =====================================================

        if (startStateCount == 0)
        {
            Debug.LogError(
                "Reachability Validator: " +
                "No valid Start Support State was found.\n" +
                "At least one Start Hand Hold and one Start Foot Hold are required."
            );

            UpdateReachableStateStatistics();

            return;
        }


        // =====================================================
        // BFS
        // =====================================================

        while (queue.Count > 0)
        {
            int currentStateIndex =
                queue.Dequeue();


            SupportStateInfo currentState =
                candidateSupportStates[
                    currentStateIndex
                ];


            // =================================================
            // Iterate through all edges leaving the current state
            // =================================================

            for (int edgeIndex = 0;
                 edgeIndex < validTransitions.Count;
                 edgeIndex++)
            {
                TransitionEdgeInfo edge =
                    validTransitions[edgeIndex];


                if (edge.fromStateIndex !=
                    currentStateIndex)
                {
                    continue;
                }


                int nextStateIndex =
                    edge.toStateIndex;


                if (nextStateIndex < 0 ||
                    nextStateIndex >=
                    candidateSupportStates.Count)
                {
                    continue;
                }


                SupportStateInfo nextState =
                    candidateSupportStates[
                        nextStateIndex
                    ];


                // Already visited by BFS
                if (nextState.reachableFromStart)
                {
                    continue;
                }


                // =============================================
                // First time reaching this state
                // =============================================

                nextState.reachableFromStart =
                    true;

                nextState.distanceFromStart =
                    currentState.distanceFromStart
                    + 1;

                nextState.previousStateIndex =
                    currentStateIndex;

                nextState.incomingTransitionIndex =
                    edgeIndex;


                queue.Enqueue(
                    nextStateIndex
                );
            }
        }


        // =====================================================
        // Count reachable states
        // =====================================================

        UpdateReachableStateStatistics();


        // =====================================================
        // Find the nearest Finish among all reachable states
        // =====================================================

        FindNearestReachableFinishState();


        // =====================================================
        // Rebuild the route if Finish is reachable
        // =====================================================

        if (finishReachable)
        {
            RebuildSolutionPath();
        }
    }

    // =========================================================
    // Find Easiest Route
    // =========================================================

    private void FindEasiestRoute()
    {
        // =====================================================
        // Reset
        // =====================================================

        easiestFinishReachable =
            false;

        easiestFinishStateIndex =
            -1;

        easiestRoutePeakDifficulty =
            0f;

        easiestRouteTotalDifficulty =
            0f;

        easiestRouteMoveCount =
            -1;


        easiestSolutionStateIndices.Clear();

        easiestSolutionTransitionIndices.Clear();


        // =====================================================
        // Reset all states
        // =====================================================

        for (int i = 0;
             i < candidateSupportStates.Count;
             i++)
        {
            SupportStateInfo state =
                candidateSupportStates[i];


            state.easiestRouteReached =
                false;

            state.easiestRouteSettled =
                false;

            state.easiestPeakDifficulty =
                0f;

            state.easiestTotalDifficulty =
                0f;

            state.easiestMoveCount =
                -1;

            state.easiestPreviousStateIndex =
                -1;

            state.easiestIncomingTransitionIndex =
                -1;

            state.onEasiestSolutionPath =
                false;
        }


        // =====================================================
        // Initialize all Start States
        // =====================================================

        int startStateCount =
            0;


        for (int i = 0;
             i < candidateSupportStates.Count;
             i++)
        {
            SupportStateInfo state =
                candidateSupportStates[i];


            if (!IsValidStartState(state))
            {
                continue;
            }


            state.easiestRouteReached =
                true;

            state.easiestPeakDifficulty =
                0f;

            state.easiestTotalDifficulty =
                0f;

            state.easiestMoveCount =
                0;

            state.easiestPreviousStateIndex =
                -1;

            state.easiestIncomingTransitionIndex =
                -1;


            startStateCount++;
        }


        if (startStateCount == 0)
        {
            Debug.LogError(
                "Easiest Route Search: " +
                "No valid Start State was found."
            );

            return;
        }


        // =====================================================
        // Dijkstra-like Search
        //
        // A PriorityQueue is not used here yet.
        // Each iteration scans all states to find the best unsettled state.
        //
        // This is sufficient for the current scale of tens or hundreds of states,
        // and keeps the code easier to inspect.
        // =====================================================

        while (true)
        {
            int currentIndex =
                FindBestUnsettledState();


            // No searchable state remains
            if (currentIndex < 0)
            {
                break;
            }


            SupportStateInfo currentState =
                candidateSupportStates[
                    currentIndex
                ];


            currentState.easiestRouteSettled =
                true;


            // =================================================
            // Iterate through all outgoing edges of the current state
            // =================================================

            for (int edgeIndex = 0;
                 edgeIndex < validTransitions.Count;
                 edgeIndex++)
            {
                TransitionEdgeInfo edge =
                    validTransitions[edgeIndex];


                if (edge.fromStateIndex !=
                    currentIndex)
                {
                    continue;
                }


                int nextIndex =
                    edge.toStateIndex;


                if (nextIndex < 0 ||
                    nextIndex >=
                    candidateSupportStates.Count)
                {
                    continue;
                }


                SupportStateInfo nextState =
                    candidateSupportStates[
                        nextIndex
                    ];


                // A settled state does not need to be updated again
                if (nextState.easiestRouteSettled)
                {
                    continue;
                }


                // =============================================
                // Candidate Route Cost
                // =============================================

                float candidatePeak =
                    Mathf.Max(
                        currentState.easiestPeakDifficulty,
                        edge.overallDifficulty
                    );


                float candidateTotal =
                    currentState.easiestTotalDifficulty
                    +
                    edge.overallDifficulty;


                int candidateMoves =
                    currentState.easiestMoveCount
                    + 1;


                // =============================================
                // First time reaching this state
                // =============================================

                bool shouldUpdate =
                    !nextState.easiestRouteReached;


                // =============================================
                // Or a better route was found
                // =============================================

                if (!shouldUpdate)
                {
                    shouldUpdate =
                        IsRouteBetter(
                            candidatePeak,
                            candidateTotal,
                            candidateMoves,

                            nextState.easiestPeakDifficulty,
                            nextState.easiestTotalDifficulty,
                            nextState.easiestMoveCount
                        );
                }


                if (!shouldUpdate)
                {
                    continue;
                }


                // =============================================
                // Update the state
                // =============================================

                nextState.easiestRouteReached =
                    true;

                nextState.easiestPeakDifficulty =
                    candidatePeak;

                nextState.easiestTotalDifficulty =
                    candidateTotal;

                nextState.easiestMoveCount =
                    candidateMoves;

                nextState.easiestPreviousStateIndex =
                    currentIndex;

                nextState.easiestIncomingTransitionIndex =
                    edgeIndex;
            }
        }


        // =====================================================
        // Select the easiest Finish among all reachable Finish states
        // =====================================================

        FindBestEasiestFinish();


        // =====================================================
        // Rebuild the route
        // =====================================================

        if (easiestFinishReachable)
        {
            RebuildEasiestSolutionPath();
        }
    }

    // =========================================================
    // Find the best currently unsettled state
    // =========================================================

    private int FindBestUnsettledState()
    {
        int bestIndex =
            -1;


        for (int i = 0;
             i < candidateSupportStates.Count;
             i++)
        {
            SupportStateInfo state =
                candidateSupportStates[i];


            if (!state.easiestRouteReached)
            {
                continue;
            }


            if (state.easiestRouteSettled)
            {
                continue;
            }


            if (bestIndex < 0)
            {
                bestIndex =
                    i;

                continue;
            }


            SupportStateInfo bestState =
                candidateSupportStates[
                    bestIndex
                ];


            bool better =
                IsRouteBetter(
                    state.easiestPeakDifficulty,
                    state.easiestTotalDifficulty,
                    state.easiestMoveCount,

                    bestState.easiestPeakDifficulty,
                    bestState.easiestTotalDifficulty,
                    bestState.easiestMoveCount
                );


            if (better)
            {
                bestIndex =
                    i;
            }
        }


        return bestIndex;
    }

    // =========================================================
    // Find Best Finish
    // =========================================================

    private void FindBestEasiestFinish()
    {
        easiestFinishReachable =
            false;

        easiestFinishStateIndex =
            -1;


        for (int i = 0;
             i < candidateSupportStates.Count;
             i++)
        {
            SupportStateInfo state =
                candidateSupportStates[i];


            if (!state.easiestRouteReached)
            {
                continue;
            }


            if (!IsFinishState(state))
            {
                continue;
            }


            // First Finish
            if (easiestFinishStateIndex < 0)
            {
                easiestFinishStateIndex =
                    i;

                continue;
            }


            SupportStateInfo currentBest =
                candidateSupportStates[
                    easiestFinishStateIndex
                ];


            if (
                IsRouteBetter(
                    state.easiestPeakDifficulty,
                    state.easiestTotalDifficulty,
                    state.easiestMoveCount,

                    currentBest.easiestPeakDifficulty,
                    currentBest.easiestTotalDifficulty,
                    currentBest.easiestMoveCount
                )
            )
            {
                easiestFinishStateIndex =
                    i;
            }
        }


        // =====================================================
        // No Finish found
        // =====================================================

        if (easiestFinishStateIndex < 0)
        {
            return;
        }


        easiestFinishReachable =
            true;


        SupportStateInfo finishState =
            candidateSupportStates[
                easiestFinishStateIndex
            ];


        easiestRoutePeakDifficulty =
            finishState.easiestPeakDifficulty;

        easiestRouteTotalDifficulty =
            finishState.easiestTotalDifficulty;

        easiestRouteMoveCount =
            finishState.easiestMoveCount;
    }

    // =========================================================
    // Rebuild Easiest Route
    // =========================================================

    private void RebuildEasiestSolutionPath()
    {
        easiestSolutionStateIndices.Clear();

        easiestSolutionTransitionIndices.Clear();


        if (!easiestFinishReachable ||
            easiestFinishStateIndex < 0)
        {
            return;
        }


        int currentIndex =
            easiestFinishStateIndex;


        while (currentIndex >= 0)
        {
            SupportStateInfo state =
                candidateSupportStates[
                    currentIndex
                ];


            state.onEasiestSolutionPath =
                true;


            easiestSolutionStateIndices.Add(
                currentIndex
            );


            if (
                state.easiestIncomingTransitionIndex
                >= 0
            )
            {
                easiestSolutionTransitionIndices.Add(
                    state.easiestIncomingTransitionIndex
                );
            }


            currentIndex =
                state.easiestPreviousStateIndex;
        }


        // Finish → Start
        // Convert to Start -> Finish

        easiestSolutionStateIndices.Reverse();

        easiestSolutionTransitionIndices.Reverse();

        FindHardestStepOnEasiestRoute();
    }

    // =========================================================
    // Find Hardest Step On Easiest Route
    // =========================================================

    private void FindHardestStepOnEasiestRoute()
    {
        hardestEasiestRouteStepIndex =
            -1;


        float highestDifficulty =
            -1f;


        for (int i = 0;
             i < easiestSolutionTransitionIndices.Count;
             i++)
        {
            int edgeIndex =
                easiestSolutionTransitionIndices[i];


            if (edgeIndex < 0 ||
                edgeIndex >= validTransitions.Count)
            {
                continue;
            }


            TransitionEdgeInfo edge =
                validTransitions[edgeIndex];


            if (edge.overallDifficulty >
                highestDifficulty)
            {
                highestDifficulty =
                    edge.overallDifficulty;

                hardestEasiestRouteStepIndex =
                    i;
            }
        }
    }

    // =========================================================
    // Refresh Easiest Route Step Debugger
    // =========================================================

    [ContextMenu("Refresh Easiest Step Debug")]
    private void RefreshEasiestStepDebug()
    {
        ResetEasiestStepDebug();


        if (!easiestFinishReachable)
        {
            return;
        }


        if (easiestSolutionTransitionIndices == null ||
            easiestSolutionTransitionIndices.Count == 0)
        {
            return;
        }


        // =====================================================
        // Clamp Step Index
        // =====================================================

        debugEasiestStepIndex =
            Mathf.Clamp(
                debugEasiestStepIndex,
                0,
                easiestSolutionTransitionIndices.Count - 1
            );


        // =====================================================
        // Find the edge corresponding to this step
        // =====================================================

        int edgeIndex =
            easiestSolutionTransitionIndices[
                debugEasiestStepIndex
            ];


        if (edgeIndex < 0 ||
            edgeIndex >= validTransitions.Count)
        {
            return;
        }


        TransitionEdgeInfo edge =
            validTransitions[edgeIndex];


        // =====================================================
        // Basic
        // =====================================================

        debugStepName =
            $"Step {debugEasiestStepIndex + 1} / " +
            $"{easiestSolutionTransitionIndices.Count} | " +
            $"S{edge.fromStateIndex} -> S{edge.toStateIndex}";


        debugStepType =
            edge.type;

        debugFromStateIndex =
            edge.fromStateIndex;

        debugToStateIndex =
            edge.toStateIndex;


        // =====================================================
        // Difficulty
        // =====================================================

        debugOverallDifficulty =
            edge.overallDifficulty;

        debugPoseDifficulty =
            edge.poseDifficulty;

        debugReachDifficulty =
            edge.reachDifficulty;

        debugSidepullDifficulty =
            edge.sidepullDifficulty;


        // =====================================================
        // Raw Data
        // =====================================================

        debugPoseFreedom =
            edge.poseFreedomRatio;

        debugReachP75 =
            edge.reachUsageP75;

        debugReachP90 =
            edge.reachUsageP90;


        // =====================================================
        // Sidepull
        // =====================================================

        debugHasSidepull =
            edge.hasSidepullAnalysis;

        debugStableRatio =
            edge.stableRatio;

        debugWeakRatio =
            edge.weakRatio;

        debugStrainRatio =
            edge.strainRatio;

        debugFallRatio =
            edge.beyondFallRatio;
    }

    // =========================================================
    // Reset Easiest Step Debug
    // =========================================================

    private void ResetEasiestStepDebug()
    {
        debugStepName =
            "";

        debugFromStateIndex =
            -1;

        debugToStateIndex =
            -1;


        debugOverallDifficulty =
            0f;

        debugPoseDifficulty =
            0f;

        debugReachDifficulty =
            0f;

        debugSidepullDifficulty =
            0f;


        debugPoseFreedom =
            0f;

        debugReachP75 =
            0f;

        debugReachP90 =
            0f;


        debugHasSidepull =
            false;

        debugStableRatio =
            0f;

        debugWeakRatio =
            0f;

        debugStrainRatio =
            0f;

        debugFallRatio =
            0f;
    }

    // =========================================================
    // Find scanned information from a Hold component
    // =========================================================

    private ScannedHoldInfo FindScannedHoldInfo(
        HoldGripCapacity2D hold
    )
    {
        if (hold == null)
        {
            return null;
        }


        for (int i = 0;
             i < scannedHolds.Count;
             i++)
        {
            if (scannedHolds[i].hold ==
                hold)
            {
                return scannedHolds[i];
            }
        }


        return null;
    }
    // =========================================================
    // Start State
    // =========================================================


    private bool IsValidStartState(
        SupportStateInfo state
    )
    {
        if (state == null)
        {
            return false;
        }


        ScannedHoldInfo handInfo =
            FindScannedHoldInfo(
                state.handHold
            );

        ScannedHoldInfo footInfo =
            FindScannedHoldInfo(
                state.footHold
            );


        if (handInfo == null ||
            footInfo == null)
        {
            return false;
        }


        return
            handInfo.isStartHandPoint
            &&
            footInfo.isStartFootPoint;
    }

    // =========================================================
    // Finish State
    // =========================================================

    private bool IsFinishState(
        SupportStateInfo state
    )
    {
        if (state == null)
        {
            return false;
        }


        ScannedHoldInfo handInfo =
            FindScannedHoldInfo(
                state.handHold
            );

        ScannedHoldInfo footInfo =
            FindScannedHoldInfo(
                state.footHold
            );


        bool handOnFinish =
            handInfo != null
            &&
            handInfo.isFinishPoint;


        bool footOnFinish =
            footInfo != null
            &&
            footInfo.isFinishPoint;


        return
            handOnFinish
            ||
            footOnFinish;
    }

    // =========================================================
    // Lexicographic Route Comparison
    //
    // First priority: Peak Difficulty
    // Second priority: Total Difficulty
    // Third priority: Move Count
    // =========================================================

    private bool IsRouteBetter(
        float candidatePeak,
        float candidateTotal,
        int candidateMoves,

        float currentPeak,
        float currentTotal,
        int currentMoves
    )
    {
        const float epsilon =
            0.00001f;


        // =====================================================
        // 1. Peak
        // =====================================================

        if (candidatePeak <
            currentPeak - epsilon)
        {
            return true;
        }


        if (candidatePeak >
            currentPeak + epsilon)
        {
            return false;
        }


        // =====================================================
        // 2. Total
        // =====================================================

        if (candidateTotal <
            currentTotal - epsilon)
        {
            return true;
        }


        if (candidateTotal >
            currentTotal + epsilon)
        {
            return false;
        }


        // =====================================================
        // 3. Move Count
        // =====================================================

        return
            candidateMoves <
            currentMoves;
    }

    // =========================================================
    // Reachable Statistics
    // =========================================================

    private void UpdateReachableStateStatistics()
    {
        reachableStateCount =
            0;

        unreachableStateCount =
            0;


        foreach (
            SupportStateInfo state
            in candidateSupportStates
        )
        {
            if (state.reachableFromStart)
            {
                reachableStateCount++;
            }
            else
            {
                unreachableStateCount++;
            }
        }
    }

    // =========================================================
    // Analyze Hold Reachability And Route Membership
    // =========================================================

    private void AnalyzeHoldReachability()
    {
        // =====================================================
        // Reset
        // =====================================================

        foreach (
            ScannedHoldInfo hold
            in scannedHolds
        )
        {
            if (hold == null)
            {
                continue;
            }


            hold.reachableFromStart =
                false;

            hold.reachableAsHand =
                false;

            hold.reachableAsFoot =
                false;

            hold.onShortestRoute =
                false;

            hold.onEasiestRoute =
                false;
        }


        // =====================================================
        // 1. Reachability
        //
        // If a hold appears in any
        // reachable Support State,
        // it is considered reachable from Start.
        // =====================================================

        for (int stateIndex = 0;
             stateIndex < candidateSupportStates.Count;
             stateIndex++)
        {
            SupportStateInfo state =
                candidateSupportStates[stateIndex];


            if (state == null ||
                !state.reachableFromStart)
            {
                continue;
            }


            ScannedHoldInfo handInfo =
                FindScannedHoldInfo(
                    state.handHold
                );


            if (handInfo != null)
            {
                handInfo.reachableFromStart =
                    true;

                handInfo.reachableAsHand =
                    true;
            }


            ScannedHoldInfo footInfo =
                FindScannedHoldInfo(
                    state.footHold
                );


            if (footInfo != null)
            {
                footInfo.reachableFromStart =
                    true;

                footInfo.reachableAsFoot =
                    true;
            }
        }


        // =====================================================
        // 2. Shortest Route
        // =====================================================

        if (solutionStateIndices != null)
        {
            foreach (
                int stateIndex
                in solutionStateIndices
            )
            {
                if (stateIndex < 0 ||
                    stateIndex >=
                    candidateSupportStates.Count)
                {
                    continue;
                }


                SupportStateInfo state =
                    candidateSupportStates[
                        stateIndex
                    ];


                MarkStateHoldsAsShortestRoute(
                    state
                );
            }
        }


        // =====================================================
        // 3. Easiest Route
        // =====================================================

        if (easiestSolutionStateIndices != null)
        {
            foreach (
                int stateIndex
                in easiestSolutionStateIndices
            )
            {
                if (stateIndex < 0 ||
                    stateIndex >=
                    candidateSupportStates.Count)
                {
                    continue;
                }


                SupportStateInfo state =
                    candidateSupportStates[
                        stateIndex
                    ];


                MarkStateHoldsAsEasiestRoute(
                    state
                );
            }
        }


        // =====================================================
        // 4. Statistics
        // =====================================================

        reachableHoldCount =
            0;

        unreachableHoldCount =
            0;

        shortestRouteHoldCount =
            0;

        easiestRouteHoldCount =
            0;


        foreach (
            ScannedHoldInfo hold
            in scannedHolds
        )
        {
            if (hold == null)
            {
                continue;
            }


            if (hold.reachableFromStart)
            {
                reachableHoldCount++;
            }
            else
            {
                unreachableHoldCount++;
            }


            if (hold.onShortestRoute)
            {
                shortestRouteHoldCount++;
            }


            if (hold.onEasiestRoute)
            {
                easiestRouteHoldCount++;
            }
        }
    }

    // =========================================================
    // Mark Shortest Route Holds
    // =========================================================

    private void MarkStateHoldsAsShortestRoute(
        SupportStateInfo state
    )
    {
        if (state == null)
        {
            return;
        }


        ScannedHoldInfo handInfo =
            FindScannedHoldInfo(
                state.handHold
            );


        if (handInfo != null)
        {
            handInfo.onShortestRoute =
                true;
        }


        ScannedHoldInfo footInfo =
            FindScannedHoldInfo(
                state.footHold
            );


        if (footInfo != null)
        {
            footInfo.onShortestRoute =
                true;
        }
    }

    // =========================================================
    // Mark Easiest Route Holds
    // =========================================================

    private void MarkStateHoldsAsEasiestRoute(
        SupportStateInfo state
    )
    {
        if (state == null)
        {
            return;
        }


        ScannedHoldInfo handInfo =
            FindScannedHoldInfo(
                state.handHold
            );


        if (handInfo != null)
        {
            handInfo.onEasiestRoute =
                true;
        }


        ScannedHoldInfo footInfo =
            FindScannedHoldInfo(
                state.footHold
            );


        if (footInfo != null)
        {
            footInfo.onEasiestRoute =
                true;
        }
    }

    // =========================================================
    // Find the nearest reachable Finish State
    // =========================================================

    private void FindNearestReachableFinishState()
    {
        finishReachable =
            false;

        solutionFinishStateIndex =
            -1;


        int bestDistance =
            int.MaxValue;


        for (int i = 0;
             i < candidateSupportStates.Count;
             i++)
        {
            SupportStateInfo state =
                candidateSupportStates[i];


            if (!state.reachableFromStart)
            {
                continue;
            }


            ScannedHoldInfo handInfo =
                FindScannedHoldInfo(
                    state.handHold
                );

            ScannedHoldInfo footInfo =
                FindScannedHoldInfo(
                    state.footHold
                );


            bool handOnFinish =
                handInfo != null
                &&
                handInfo.isFinishPoint;


            bool footOnFinish =
                footInfo != null
                &&
                footInfo.isFinishPoint;


            if (!handOnFinish &&
                !footOnFinish)
            {
                continue;
            }


            if (state.distanceFromStart <
                bestDistance)
            {
                bestDistance =
                    state.distanceFromStart;

                solutionFinishStateIndex =
                    i;
            }
        }


        if (solutionFinishStateIndex >= 0)
        {
            finishReachable =
                true;

            solutionStepCount =
                bestDistance;
        }
    }

    // =========================================================
    // Rebuild the Start -> Finish route
    // =========================================================

    private void RebuildSolutionPath()
    {
        solutionStateIndices.Clear();

        solutionTransitionIndices.Clear();


        if (!finishReachable ||
            solutionFinishStateIndex < 0)
        {
            return;
        }


        int currentIndex =
            solutionFinishStateIndex;


        // =====================================================
        // Trace backward from Finish to Start
        // =====================================================

        while (currentIndex >= 0)
        {
            SupportStateInfo state =
                candidateSupportStates[
                    currentIndex
                ];


            state.onSolutionPath =
                true;


            solutionStateIndices.Add(
                currentIndex
            );


            if (state.incomingTransitionIndex >= 0)
            {
                solutionTransitionIndices.Add(
                    state.incomingTransitionIndex
                );
            }


            currentIndex =
                state.previousStateIndex;
        }


        // =====================================================
        // The route is currently Finish -> Start
        // Reverse it to Start -> Finish
        // =====================================================

        solutionStateIndices.Reverse();

        solutionTransitionIndices.Reverse();
    }

    // =========================================================
    // Calculate Transition Difficulty
    // =========================================================

    private void CalculateTransitionDifficulty(
        TransitionEdgeInfo edge
    )
    {
        if (edge == null)
        {
            return;
        }


        // =====================================================
        // 1. Pose Difficulty
        // =====================================================

        edge.poseDifficulty =
            Mathf.Clamp01(
                1f
                - edge.poseFreedomRatio
            );


        // =====================================================
        // 2. Reach Difficulty
        //
        // Use P75 rather than Best Reach.
        // =====================================================

        float reachRange =
            hardReachUsage
            - easyReachUsage;


        if (reachRange <= 0.0001f)
        {
            edge.reachDifficulty =
                edge.reachUsageP75 >=
                hardReachUsage
                ?
                1f
                :
                0f;
        }
        else
        {
            edge.reachDifficulty =
                Mathf.Clamp01(
                    (
                        edge.reachUsageP75
                        - easyReachUsage
                    )
                    /
                    reachRange
                );
        }


        // =====================================================
        // 3. Base Movement Difficulty
        //
        // Only consider:
        // Reach + Pose Freedom
        // =====================================================

        float movementWeightTotal =
            reachDifficultyWeight
            +
            poseDifficultyWeight;


        if (movementWeightTotal >
            0.0001f)
        {
            edge.baseMovementDifficulty =
                (
                    edge.reachDifficulty
                    * reachDifficultyWeight
                    +
                    edge.poseDifficulty
                    * poseDifficultyWeight
                )
                /
                movementWeightTotal;
        }
        else
        {
            edge.baseMovementDifficulty =
                0f;
        }


        // =====================================================
        // 4. Sidepull Difficulty
        // =====================================================

        if (edge.hasSidepullAnalysis)
        {
            /*
             * Stable Cost = 0
             *
             * Therefore there is no need to include:
             * stableRatio * 0
             */

            float stateDifficulty =
                edge.weakRatio
                * weakDifficulty
                +
                edge.strainRatio
                * strainDifficulty;


            /*
             * Fall Boundary is not a fourth state,
             * so it is added as an extra risk penalty.
             */

            float fallPenalty =
                edge.beyondFallRatio
                * fallDifficultyPenalty;


            edge.sidepullDifficulty =
                Mathf.Clamp01(
                    stateDifficulty
                    +
                    fallPenalty
                );
        }
        else
        {
            edge.sidepullDifficulty =
                0f;
        }


        // =====================================================
        // 5. Overall Difficulty
        //
        // Do not use a simple average directly.
        //
        // Stable Sidepull = no change to the original movement difficulty.
        // As Sidepull difficulty increases, push the overall Difficulty toward 1.
        // =====================================================

        float sidepullEffect =
            edge.sidepullDifficulty
            * sidepullInfluence;


        edge.overallDifficulty =
            edge.baseMovementDifficulty
            +
            sidepullEffect
            *
            (
                1f
                - edge.baseMovementDifficulty
            );


        edge.overallDifficulty =
            Mathf.Clamp01(
                edge.overallDifficulty
            );
    }


    // =========================================================
    // Statistics
    // =========================================================

    private void UpdateStatistics()
    {
        scannedHoldCount =
            scannedHolds.Count;


        startHandHoldCount =
            0;

        startFootHoldCount =
            0;

        finishHoldCount =
            0;


        foreach (
            ScannedHoldInfo hold
            in scannedHolds
        )
        {
            if (hold.isStartHandPoint)
            {
                startHandHoldCount++;
            }

            if (hold.isStartFootPoint)
            {
                startFootHoldCount++;
            }

            if (hold.isFinishPoint)
            {
                finishHoldCount++;
            }
        }


        candidateSupportStateCount =
            candidateSupportStates.Count;
    }


    // =========================================================
    // Clear
    // =========================================================

    [ContextMenu("Clear Scan Results")]
    public void ClearResults()
    {
        scannedHolds.Clear();

        candidateSupportStates.Clear();

        validTransitions.Clear();


        scannedHoldCount =
            0;

        startHandHoldCount =
            0;

        startFootHoldCount =
            0;

        finishHoldCount =
            0;

        rawSupportPairCount =
            0;

        rejectedByCapacity =
            0;

        rejectedByGeometry =
            0;

        candidateSupportStateCount =
            0;

        testedHandTransitionCount =
            0;

        validHandTransitionCount =
            0;

        testedFootTransitionCount =
            0;

        validFootTransitionCount =
            0;

        totalValidTransitionCount =
            0;

        solutionStateIndices.Clear();

        solutionTransitionIndices.Clear();

        finishReachable =
            false;

        reachableStateCount =
            0;

        unreachableStateCount =
            0;

        solutionFinishStateIndex =
            -1;

        solutionStepCount =
            -1;

        easiestFinishReachable =
            false;

        easiestFinishStateIndex =
            -1;

        easiestRoutePeakDifficulty =
            0f;

        easiestRouteTotalDifficulty =
            0f;

        easiestRouteMoveCount =
            -1;


        easiestSolutionStateIndices.Clear();

        easiestSolutionTransitionIndices.Clear();
    }


    // =========================================================
    // Gizmos
    // =========================================================

    private void OnDrawGizmos()
    {
        if (drawHoldPoints)
        {
            DrawHoldGizmos();
        }

        if (drawHoldRouteAnalysis)
        {
            DrawHoldRouteAnalysis();
        }

        if (drawSelectedSupportState)
        {
            DrawSelectedSupportState();
        }

        if (drawOutgoingTransitions)
        {
            DrawOutgoingTransitions();
        }

        if (drawSolutionPath)
        {
            DrawSolutionPath();
        }

        if (drawEasiestSolutionPath)
        {
            DrawEasiestSolutionPath();
        }

        if (drawEasiestStepDebug)
        {
            DrawEasiestStepDebug();
        }


    }

    [SerializeField]
    private bool drawOutgoingTransitions = true;

    [SerializeField]
    private bool drawSolutionPath = true;

    [Tooltip(
    "Display the easiest guaranteed route. Magenta lines indicate the Easiest Route."
)]
    [SerializeField]
    private bool drawEasiestSolutionPath = true;


    [Min(0.01f)]
    [SerializeField]
    private float easiestRoutePointRadius = 0.07f;

    [Min(0.01f)]
    [SerializeField]
    private float easiestRouteArrowHeadLength = 0.12f;

    [Range(10f, 80f)]
    [SerializeField]
    private float easiestRouteArrowHeadAngle = 28f;

    [SerializeField]
    private Vector2 easiestRouteLabelOffset =
        new Vector2(0.05f, 0.05f);

    // =========================================================
    // Easiest Route Step Debugger
    // =========================================================

    [Header("Easiest Route Step Debugger")]

    [SerializeField]
    private bool drawEasiestStepDebug = true;

    [Min(0)]
    [SerializeField]
    private int debugEasiestStepIndex = 0;

    [SerializeField]
    private int hardestEasiestRouteStepIndex =
    -1;

    [Header("Selected Step")]

    [SerializeField]
    private string debugStepName;

    [SerializeField]
    private TransitionType debugStepType;

    [SerializeField]
    private int debugFromStateIndex = -1;

    [SerializeField]
    private int debugToStateIndex = -1;


    [Header("Selected Step - Difficulty")]

    [Range(0f, 1f)]
    [SerializeField]
    private float debugOverallDifficulty;

    [Range(0f, 1f)]
    [SerializeField]
    private float debugPoseDifficulty;

    [Range(0f, 1f)]
    [SerializeField]
    private float debugReachDifficulty;

    [Range(0f, 1f)]
    [SerializeField]
    private float debugSidepullDifficulty;


    [Header("Selected Step - Raw Data")]

    [Range(0f, 1f)]
    [SerializeField]
    private float debugPoseFreedom;

    [SerializeField]
    private float debugReachP75;

    [SerializeField]
    private float debugReachP90;


    [Header("Selected Step - Sidepull")]

    [SerializeField]
    private bool debugHasSidepull;

    [Range(0f, 1f)]
    [SerializeField]
    private float debugStableRatio;

    [Range(0f, 1f)]
    [SerializeField]
    private float debugWeakRatio;

    [Range(0f, 1f)]
    [SerializeField]
    private float debugStrainRatio;

    [Range(0f, 1f)]
    [SerializeField]
    private float debugFallRatio;

    private void DrawHoldGizmos()
    {
        foreach (
            ScannedHoldInfo hold
            in scannedHolds
        )
        {
            if (hold == null ||
                hold.hold == null)
            {
                continue;
            }


            // Start
            if (hold.isStartHandPoint ||
                hold.isStartFootPoint)
            {
                Gizmos.color =
                    Color.green;
            }

            // Finish
            else if (hold.isFinishPoint)
            {
                Gizmos.color =
                    Color.magenta;
            }

            // Sidepull
            else if (hold.isSidepull)
            {
                Gizmos.color =
                    Color.yellow;
            }

            // Normal
            else
            {
                Gizmos.color =
                    Color.cyan;
            }


            Gizmos.DrawWireSphere(
                hold.validationPoint,
                holdPointGizmoRadius
            );
        }
    }

    // =========================================================
    // Draw Hold Route Analysis
    // =========================================================

    private void DrawHoldRouteAnalysis()
    {
        if (scannedHolds == null ||
            scannedHolds.Count == 0)
        {
            return;
        }


        foreach (
            ScannedHoldInfo hold
            in scannedHolds
        )
        {
            if (hold == null ||
                hold.hold == null)
            {
                continue;
            }


            // =================================================
            // Priority 1:
            // Used by both the Shortest and Easiest routes
            // =================================================

            if (hold.onShortestRoute &&
                hold.onEasiestRoute)
            {
                Gizmos.color =
                    Color.white;
            }


            // =================================================
            // Priority 2:
            // Easiest Route
            // =================================================

            else if (hold.onEasiestRoute)
            {
                Gizmos.color =
                    Color.magenta;
            }


            // =================================================
            // Priority 3:
            // Shortest Route
            // =================================================

            else if (hold.onShortestRoute)
            {
                Gizmos.color =
                    Color.yellow;
            }


            // =================================================
            // Reachable hold not used by either primary route
            // =================================================

            else if (hold.reachableFromStart)
            {
                Gizmos.color =
                    Color.cyan;
            }


            // =================================================
            // Completely unreachable
            // =================================================

            else
            {
                Gizmos.color =
                    Color.red;
            }


            Gizmos.DrawWireSphere(
                hold.validationPoint,
                holdRouteAnalysisRadius
            );
        }
    }


    private void DrawSelectedSupportState()
    {
        if (candidateSupportStates == null ||
            candidateSupportStates.Count == 0)
        {
            return;
        }


        int index =
            Mathf.Clamp(
                debugSupportStateIndex,
                0,
                candidateSupportStates.Count - 1
            );


        SupportStateInfo state =
            candidateSupportStates[index];


        if (state == null)
        {
            return;
        }


        // Hand
        Gizmos.color =
            Color.cyan;

        Gizmos.DrawWireSphere(
            state.handPoint,
            holdPointGizmoRadius * 1.5f
        );


        // Foot
        Gizmos.color =
            Color.yellow;

        Gizmos.DrawWireSphere(
            state.footPoint,
            holdPointGizmoRadius * 1.5f
        );


        // Support-pair connection
        Gizmos.color =
            Color.white;

        Gizmos.DrawLine(
            state.handPoint,
            state.footPoint
        );
    }

    // =========================================================
    // Draw Outgoing Transition Graph
    // =========================================================

    private void DrawOutgoingTransitions()
    {
        if (candidateSupportStates == null ||
            candidateSupportStates.Count == 0)
        {
            return;
        }


        int selectedIndex =
            Mathf.Clamp(
                debugSupportStateIndex,
                0,
                candidateSupportStates.Count - 1
            );


        SupportStateInfo selectedState =
            candidateSupportStates[selectedIndex];


        foreach (
            TransitionEdgeInfo edge
            in validTransitions
        )
        {
            if (edge.fromStateIndex !=
                selectedIndex)
            {
                continue;
            }


            if (edge.toStateIndex < 0 ||
                edge.toStateIndex >=
                candidateSupportStates.Count)
            {
                continue;
            }


            SupportStateInfo targetState =
                candidateSupportStates[
                    edge.toStateIndex
                ];


            // =============================================
            // Hand Move
            // =============================================

            if (edge.type ==
                TransitionType.HandMove)
            {
                Gizmos.color =
                    Color.green;


                Gizmos.DrawLine(
                    selectedState.handPoint,
                    targetState.handPoint
                );
            }


            // =============================================
            // Foot Move
            // =============================================

            else
            {
                Gizmos.color =
                    Color.yellow;


                Gizmos.DrawLine(
                    selectedState.footPoint,
                    targetState.footPoint
                );
            }
        }
    }

    // =========================================================
    // Draw Guaranteed Solution
    // =========================================================

    private void DrawSolutionPath()
    {
        if (!finishReachable ||
            solutionTransitionIndices == null ||
            solutionTransitionIndices.Count == 0)
        {
            return;
        }


        for (int i = 0;
             i < solutionTransitionIndices.Count;
             i++)
        {
            int edgeIndex =
                solutionTransitionIndices[i];


            if (edgeIndex < 0 ||
                edgeIndex >=
                validTransitions.Count)
            {
                continue;
            }


            TransitionEdgeInfo edge =
                validTransitions[edgeIndex];


            SupportStateInfo fromState =
                candidateSupportStates[
                    edge.fromStateIndex
                ];

            SupportStateInfo toState =
                candidateSupportStates[
                    edge.toStateIndex
                ];


            // =============================================
            // Hand Move
            // =============================================

            if (edge.type ==
                TransitionType.HandMove)
            {
                Gizmos.color =
                    Color.green;


                Gizmos.DrawLine(
                    fromState.handPoint,
                    toState.handPoint
                );
            }


            // =============================================
            // Foot Move
            // =============================================

            else
            {
                Gizmos.color =
                    Color.yellow;


                Gizmos.DrawLine(
                    fromState.footPoint,
                    toState.footPoint
                );
            }


            // =============================================
            // Also draw the Chest/Hip of the Bridge Pose
            // =============================================

            Gizmos.color =
                Color.white;


            Gizmos.DrawWireSphere(
                edge.bestChestPosition,
                0.05f
            );


            Gizmos.DrawWireSphere(
                edge.bestHipPosition,
                0.05f
            );
        }
    }

    // =========================================================
    // Draw Easiest Route As Ordered Actions
    // =========================================================

    private void DrawEasiestSolutionPath()
    {
        if (!easiestFinishReachable)
        {
            return;
        }


        if (easiestSolutionTransitionIndices == null ||
            easiestSolutionTransitionIndices.Count == 0)
        {
            return;
        }


        for (int i = 0;
             i < easiestSolutionTransitionIndices.Count;
             i++)
        {
            int edgeIndex =
                easiestSolutionTransitionIndices[i];


            if (edgeIndex < 0 ||
                edgeIndex >= validTransitions.Count)
            {
                continue;
            }


            TransitionEdgeInfo edge =
                validTransitions[edgeIndex];


            if (edge.fromStateIndex < 0 ||
                edge.fromStateIndex >=
                candidateSupportStates.Count)
            {
                continue;
            }


            if (edge.toStateIndex < 0 ||
                edge.toStateIndex >=
                candidateSupportStates.Count)
            {
                continue;
            }


            SupportStateInfo fromState =
                candidateSupportStates[
                    edge.fromStateIndex
                ];

            SupportStateInfo toState =
                candidateSupportStates[
                    edge.toStateIndex
                ];


            Vector2 fromPoint;

            Vector2 toPoint;

            Color moveColor;

            string moveLabel;


            // =====================================================
            // Hand Move
            // BLUE
            // =====================================================

            if (edge.type ==
                TransitionType.HandMove)
            {
                fromPoint =
                    fromState.handPoint;

                toPoint =
                    toState.handPoint;

                moveColor =
                    Color.blue;

                moveLabel =
                    "H";
            }


            // =====================================================
            // Foot Move
            // ORANGE
            // =====================================================

            else
            {
                fromPoint =
                    fromState.footPoint;

                toPoint =
                    toState.footPoint;

                moveColor =
                    new Color(
                        1f,
                        0.5f,
                        0f
                    );

                moveLabel =
                    "F";
            }


            // =====================================================
            // Directional Arrow
            // =====================================================

            DrawEasiestRouteArrow(
                fromPoint,
                toPoint,
                moveColor
            );


            // =====================================================
            // Target Point
            // =====================================================

            Gizmos.color =
                moveColor;

            Gizmos.DrawWireSphere(
                toPoint,
                easiestRoutePointRadius
            );


            // =====================================================
            // Step Number
            //
            // 1 H
            // 2 F
            // 3 H
            // ...
            // =====================================================

#if UNITY_EDITOR

            Vector3 labelPosition =
                (
                    (Vector3)fromPoint
                    +
                    (Vector3)toPoint
                )
                * 0.5f
                +
                (Vector3)easiestRouteLabelOffset;


            GUIStyle style =
                new GUIStyle(
                    EditorStyles.boldLabel
                );

            style.normal.textColor =
                moveColor;

            style.fontSize =
                14;


            Handles.Label(
                labelPosition,
                $"{i + 1} {moveLabel}",
                style
            );

#endif
        }
    }

    // =========================================================
    // Draw Easiest Route Arrow
    // =========================================================

    private void DrawEasiestRouteArrow(
        Vector2 from,
        Vector2 to,
        Color color
    )
    {
        Vector2 delta =
            to - from;


        if (delta.sqrMagnitude <
            0.000001f)
        {
            return;
        }


        Vector2 direction =
            delta.normalized;


        // Main line
        Gizmos.color =
            color;

        Gizmos.DrawLine(
            from,
            to
        );


        // =====================================================
        // Arrow head
        // =====================================================

        Vector3 backward =
            -(Vector3)direction;


        Vector3 leftDirection =
            Quaternion.Euler(
                0f,
                0f,
                easiestRouteArrowHeadAngle
            )
            * backward;


        Vector3 rightDirection =
            Quaternion.Euler(
                0f,
                0f,
                -easiestRouteArrowHeadAngle
            )
            * backward;


        Gizmos.DrawLine(
            to,
            (Vector3)to
            +
            leftDirection
            * easiestRouteArrowHeadLength
        );


        Gizmos.DrawLine(
            to,
            (Vector3)to
            +
            rightDirection
            * easiestRouteArrowHeadLength
        );
    }

    // =========================================================
    // Draw Selected Easiest Route Step
    // =========================================================

    private void DrawEasiestStepDebug()
    {
        if (!easiestFinishReachable)
        {
            return;
        }


        if (easiestSolutionTransitionIndices == null ||
            easiestSolutionTransitionIndices.Count == 0)
        {
            return;
        }


        int stepIndex =
            Mathf.Clamp(
                debugEasiestStepIndex,
                0,
                easiestSolutionTransitionIndices.Count - 1
            );


        int edgeIndex =
            easiestSolutionTransitionIndices[
                stepIndex
            ];


        if (edgeIndex < 0 ||
            edgeIndex >= validTransitions.Count)
        {
            return;
        }


        TransitionEdgeInfo edge =
            validTransitions[edgeIndex];


        if (edge.fromStateIndex < 0 ||
            edge.fromStateIndex >=
            candidateSupportStates.Count)
        {
            return;
        }


        if (edge.toStateIndex < 0 ||
            edge.toStateIndex >=
            candidateSupportStates.Count)
        {
            return;
        }


        SupportStateInfo fromState =
            candidateSupportStates[
                edge.fromStateIndex
            ];

        SupportStateInfo toState =
            candidateSupportStates[
                edge.toStateIndex
            ];


        // =====================================================
        // Current movement
        // =====================================================

        Gizmos.color =
            Color.white;


        if (edge.type ==
            TransitionType.HandMove)
        {
            Gizmos.DrawLine(
                fromState.handPoint,
                toState.handPoint
            );


            Gizmos.DrawWireSphere(
                fromState.handPoint,
                0.12f
            );

            Gizmos.DrawWireSphere(
                toState.handPoint,
                0.12f
            );
        }
        else
        {
            Gizmos.DrawLine(
                fromState.footPoint,
                toState.footPoint
            );


            Gizmos.DrawWireSphere(
                fromState.footPoint,
                0.12f
            );

            Gizmos.DrawWireSphere(
                toState.footPoint,
                0.12f
            );
        }


        // =====================================================
        // Best Bridge Pose
        // =====================================================

        Gizmos.color =
            Color.cyan;


        Gizmos.DrawWireSphere(
            edge.bestChestPosition,
            0.09f
        );


        Gizmos.DrawWireSphere(
            edge.bestHipPosition,
            0.09f
        );


        Gizmos.DrawLine(
            edge.bestChestPosition,
            edge.bestHipPosition
        );


        // =====================================================
        // Hand Transition Bridge
        // =====================================================

        if (edge.type ==
            TransitionType.HandMove)
        {
            Gizmos.DrawLine(
                edge.bestChestPosition,
                fromState.handPoint
            );

            Gizmos.DrawLine(
                edge.bestChestPosition,
                toState.handPoint
            );

            Gizmos.DrawLine(
                edge.bestHipPosition,
                fromState.footPoint
            );
        }


        // =====================================================
        // Foot Transition Bridge
        // =====================================================

        else
        {
            Gizmos.DrawLine(
                edge.bestChestPosition,
                fromState.handPoint
            );

            Gizmos.DrawLine(
                edge.bestHipPosition,
                fromState.footPoint
            );

            Gizmos.DrawLine(
                edge.bestHipPosition,
                toState.footPoint
            );
        }
    }
}