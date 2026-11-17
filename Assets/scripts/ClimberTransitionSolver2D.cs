using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 角色Transition几何求解器。
///
/// Hand Transition:
/// (H1, F1) -> (H2, F1)
///
/// Bridge状态：
/// H1 + H2 + F1 + FreeFoot
///
/// Foot Transition:
/// (H1, F1) -> (H1, F2)
///
/// Bridge状态：
/// H1 + FreeHand + F1 + F2
///
/// Solver可以选择保存所有合法Pose Sample，
/// 每个Pose包含Chest、Hip、自由肢体位置和估算COM。
/// </summary>
[DisallowMultipleComponent]
public class ClimberTransitionSolver2D : MonoBehaviour
{
    // =========================================================
    // Pose Sample
    // =========================================================

    [System.Serializable]
    public class PoseSample
    {
        public Vector2 chestPosition;

        public Vector2 hipPosition;

        /// <summary>
        /// Hand Transition = Free Foot
        /// Foot Transition = Free Hand
        /// </summary>
        public Vector2 freeLimbPosition;

        public Vector2 centerOfMass;

        /// <summary>
        /// 当前Pose最吃紧的支撑肢体，
        /// 使用Safe Reach的比例。
        /// </summary>
        public float reachUsage;
    }


    // =========================================================
    // Result
    // =========================================================

    [System.Serializable]
    public class TransitionResult
    {
        public bool valid;

        public float bestReachUsage;

        public Vector2 bestChestPosition;

        public Vector2 bestHipPosition;

        public int testedBodyPositions;

        /// <summary>
        /// 现在代表所有合法的
        /// Chest + Hip组合数量。
        /// </summary>
        public int validBridgePoseCount;
        // =========================================================
        // Pose Freedom
        // =========================================================

        /// <summary>
        /// 真正测试过的完整身体姿势数量。
        ///
        /// Hand Transition:
        /// 合法Chest + 每一个Torso Angle
        ///
        /// Foot Transition:
        /// 合法Hip + 每一个Torso Angle
        /// </summary>
        public int testedPoseConfigurationCount;


        /// <summary>
        /// 合法Bridge Pose占所有测试Pose的比例。
        /// </summary>
        public float poseFreedomRatio;


        // =========================================================
        // Reach Distribution
        // =========================================================

        public float averageReachUsage;

        public float medianReachUsage;

        public float reachUsageP75;

        public float reachUsageP90;
        /// <summary>
        /// 只有collectPoseSamples=true时
        /// 才会真正保存内容。
        /// </summary>
        public List<PoseSample> poseSamples =
            new List<PoseSample>();


        public static TransitionResult Invalid()
        {
            return new TransitionResult
            {
                valid = false,

                bestReachUsage = 0f,

                bestChestPosition = Vector2.zero,

                bestHipPosition = Vector2.zero,

                testedBodyPositions = 0,

                validBridgePoseCount = 0,

                testedPoseConfigurationCount = 0,

                poseFreedomRatio = 0f,

                averageReachUsage = 0f,

                medianReachUsage = 0f,

                reachUsageP75 = 0f,

                reachUsageP90 = 0f,

                poseSamples =
                    new List<PoseSample>()
            };
        }
    }


    // =========================================================
    // Reference
    // =========================================================

    [Header("角色数据")]

    [SerializeField]
    private ClimberReachProfile2D reachProfile;


    // =========================================================
    // Sampling
    // =========================================================

    [Header("搜索精度")]

    [Tooltip("Chest/Hip二维位置采样间隔")]
    [Min(0.01f)]
    [SerializeField]
    private float bodySampleSpacing = 0.05f;


    [Tooltip("Torso允许偏离垂直方向的最大角度")]
    [Range(0f, 90f)]
    [SerializeField]
    private float maxTorsoTiltDegrees = 60f;


    [Tooltip("Torso角度采样步长")]
    [Range(1f, 30f)]
    [SerializeField]
    private float torsoAngleStep = 5f;


    // =========================================================
    // COM
    // =========================================================

    [Header("COM 权重")]

    [Tooltip("Hand和Foot的权重")]
    [SerializeField]
    private float limbWeight = 1f;

    [Tooltip("Chest权重")]
    [SerializeField]
    private float chestWeight = 1.2f;

    [Tooltip("Hip权重")]
    [SerializeField]
    private float hipWeight = 1.2f;


    // =========================================================
    // HAND TRANSITION
    // =========================================================

    public TransitionResult SolveHandTransition(
        Vector2 supportHand,
        Vector2 supportFoot,
        Vector2 targetHand,
        bool collectPoseSamples = false
    )
    {
        TransitionResult result =
            TransitionResult.Invalid();


        if (!ReachProfileValid())
        {
            return result;
        }


        float armReach =
            reachProfile.SafeArmMaxReach;

        float legReach =
            reachProfile.SafeLegMaxReach;

        float torsoLength =
            reachProfile.TorsoLength;


        // =====================================================
        // Cheap Reject
        // =====================================================

        float handDistance =
            Vector2.Distance(
                supportHand,
                targetHand
            );


        if (handDistance >
            armReach * 2f)
        {
            return result;
        }


        // =====================================================
        // H1 / H2 Arm Circle重合区域的Bounding Box
        // =====================================================

        float minX =
            Mathf.Max(
                supportHand.x - armReach,
                targetHand.x - armReach
            );

        float maxX =
            Mathf.Min(
                supportHand.x + armReach,
                targetHand.x + armReach
            );

        float minY =
            Mathf.Max(
                supportHand.y - armReach,
                targetHand.y - armReach
            );

        float maxY =
            Mathf.Min(
                supportHand.y + armReach,
                targetHand.y + armReach
            );


        if (minX > maxX ||
            minY > maxY)
        {
            return result;
        }


        int xSteps =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (maxX - minX)
                    / bodySampleSpacing
                )
            );

        int ySteps =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (maxY - minY)
                    / bodySampleSpacing
                )
            );


        float bestScore =
            float.MaxValue;
        List<float> validReachUsages =
            new List<float>();

        // =====================================================
        // 枚举全部Chest
        // =====================================================

        for (int x = 0;
             x <= xSteps;
             x++)
        {
            float chestX =
                Mathf.Lerp(
                    minX,
                    maxX,
                    (float)x / xSteps
                );


            for (int y = 0;
                 y <= ySteps;
                 y++)
            {
                float chestY =
                    Mathf.Lerp(
                        minY,
                        maxY,
                        (float)y / ySteps
                    );


                Vector2 chest =
                    new Vector2(
                        chestX,
                        chestY
                    );


                result.testedBodyPositions++;


                // =================================================
                // Chest必须同时连接两只手
                // =================================================

                float supportArmDistance =
                    Vector2.Distance(
                        chest,
                        supportHand
                    );


                if (supportArmDistance >
                    armReach)
                {
                    continue;
                }


                float targetArmDistance =
                    Vector2.Distance(
                        chest,
                        targetHand
                    );


                if (targetArmDistance >
                    armReach)
                {
                    continue;
                }


                // =================================================
                // 以前：
                // 每个Chest只找一个最佳Hip。
                //
                // 现在：
                // 把所有合法Torso方向全部保留。
                // =================================================

                for (float angle =
                         -maxTorsoTiltDegrees;

                     angle <=
                         maxTorsoTiltDegrees + 0.001f;

                     angle +=
                         torsoAngleStep)
                {
                    result.testedPoseConfigurationCount++;
                    Vector2 torsoDirection =
                        RotateVector(
                            Vector2.down,
                            angle
                        );


                    Vector2 hip =
                        chest
                        + torsoDirection
                        * torsoLength;


                    // =============================================
                    // 支撑脚必须能够连接Hip
                    // =============================================

                    float supportLegDistance =
                        Vector2.Distance(
                            hip,
                            supportFoot
                        );


                    if (supportLegDistance >
                        legReach)
                    {
                        continue;
                    }


                    // =============================================
                    // 这是一个合法Bridge Pose
                    // =============================================

                    result.validBridgePoseCount++;


                    float supportArmUsage =
                        supportArmDistance
                        / armReach;

                    float targetArmUsage =
                        targetArmDistance
                        / armReach;

                    float supportLegUsage =
                        supportLegDistance
                        / legReach;


                    float poseScore =
                        Mathf.Max(
                            supportArmUsage,
                            targetArmUsage,
                            supportLegUsage
                        );
                    validReachUsages.Add(
                            poseScore
                        );


                    // =============================================
                    // 自由脚
                    //
                    // 不受玩家控制，
                    // 从Hip自然垂直向下。
                    // 使用真实Physical Leg Length。
                    // =============================================

                    Vector2 freeFoot =
                        hip
                        + Vector2.down
                        * reachProfile.PhysicalLegMaxReach;


                    // =============================================
                    // COM
                    // =============================================

                    Vector2 com =
                        CalculateCOM(
                            supportHand,
                            targetHand,
                            supportFoot,
                            freeFoot,
                            chest,
                            hip
                        );


                    // =============================================
                    // 保存Pose Sample
                    // =============================================

                    if (collectPoseSamples)
                    {
                        PoseSample sample =
                            new PoseSample();


                        sample.chestPosition =
                            chest;

                        sample.hipPosition =
                            hip;

                        sample.freeLimbPosition =
                            freeFoot;

                        sample.centerOfMass =
                            com;

                        sample.reachUsage =
                            poseScore;


                        result.poseSamples.Add(
                            sample
                        );
                    }


                    // =============================================
                    // 原来的Best Pose仍然保留
                    // =============================================

                    if (poseScore <
                        bestScore)
                    {
                        bestScore =
                            poseScore;

                        result.bestChestPosition =
                            chest;

                        result.bestHipPosition =
                            hip;
                    }
                }
            }
        }


        if (result.validBridgePoseCount > 0)
        {
            result.valid =
                true;

            result.bestReachUsage =
                bestScore;


            // =============================================
            // Pose Freedom
            // =============================================

            if (result.testedPoseConfigurationCount > 0)
            {
                result.poseFreedomRatio =
                    result.validBridgePoseCount
                    /
                    (float)result.testedPoseConfigurationCount;
            }


            // =============================================
            // Reach Distribution
            // =============================================

            CalculateReachStatistics(
                validReachUsages,
                result
            );
        }


        return result;
    }


    // =========================================================
    // FOOT TRANSITION
    // =========================================================

    public TransitionResult SolveFootTransition(
        Vector2 supportHand,
        Vector2 supportFoot,
        Vector2 targetFoot,
        bool collectPoseSamples = false
    )
    {
        TransitionResult result =
            TransitionResult.Invalid();


        if (!ReachProfileValid())
        {
            return result;
        }


        float armReach =
            reachProfile.SafeArmMaxReach;

        float legReach =
            reachProfile.SafeLegMaxReach;

        float torsoLength =
            reachProfile.TorsoLength;


        // =====================================================
        // Cheap Reject
        // =====================================================

        float footDistance =
            Vector2.Distance(
                supportFoot,
                targetFoot
            );


        if (footDistance >
            legReach * 2f)
        {
            return result;
        }


        // =====================================================
        // F1 / F2 Leg Circle重合区域Bounding Box
        // =====================================================

        float minX =
            Mathf.Max(
                supportFoot.x - legReach,
                targetFoot.x - legReach
            );

        float maxX =
            Mathf.Min(
                supportFoot.x + legReach,
                targetFoot.x + legReach
            );

        float minY =
            Mathf.Max(
                supportFoot.y - legReach,
                targetFoot.y - legReach
            );

        float maxY =
            Mathf.Min(
                supportFoot.y + legReach,
                targetFoot.y + legReach
            );


        if (minX > maxX ||
            minY > maxY)
        {
            return result;
        }


        int xSteps =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (maxX - minX)
                    / bodySampleSpacing
                )
            );

        int ySteps =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (maxY - minY)
                    / bodySampleSpacing
                )
            );


        float bestScore =
            float.MaxValue;
        List<float> validReachUsages =
            new List<float>();

        // =====================================================
        // 枚举全部Hip
        // =====================================================

        for (int x = 0;
             x <= xSteps;
             x++)
        {
            float hipX =
                Mathf.Lerp(
                    minX,
                    maxX,
                    (float)x / xSteps
                );


            for (int y = 0;
                 y <= ySteps;
                 y++)
            {
                float hipY =
                    Mathf.Lerp(
                        minY,
                        maxY,
                        (float)y / ySteps
                    );


                Vector2 hip =
                    new Vector2(
                        hipX,
                        hipY
                    );


                result.testedBodyPositions++;


                // =================================================
                // Hip必须同时连接F1和F2
                // =================================================

                float supportLegDistance =
                    Vector2.Distance(
                        hip,
                        supportFoot
                    );


                if (supportLegDistance >
                    legReach)
                {
                    continue;
                }


                float targetLegDistance =
                    Vector2.Distance(
                        hip,
                        targetFoot
                    );


                if (targetLegDistance >
                    legReach)
                {
                    continue;
                }


                // =================================================
                // 枚举所有合法Chest
                // =================================================

                for (float angle =
                         -maxTorsoTiltDegrees;

                     angle <=
                         maxTorsoTiltDegrees + 0.001f;

                     angle +=
                         torsoAngleStep)
                {
                    result.testedPoseConfigurationCount++;

                    Vector2 torsoDirection =
                        RotateVector(
                            Vector2.up,
                            angle
                        );


                    Vector2 chest =
                        hip
                        + torsoDirection
                        * torsoLength;


                    // =============================================
                    // 支撑手必须能连接Chest
                    // =============================================

                    float supportArmDistance =
                        Vector2.Distance(
                            chest,
                            supportHand
                        );


                    if (supportArmDistance >
                        armReach)
                    {
                        continue;
                    }


                    // =============================================
                    // 合法Bridge Pose
                    // =============================================

                    result.validBridgePoseCount++;


                    float supportLegUsage =
                        supportLegDistance
                        / legReach;

                    float targetLegUsage =
                        targetLegDistance
                        / legReach;

                    float supportArmUsage =
                        supportArmDistance
                        / armReach;


                    float poseScore =
                        Mathf.Max(
                            supportLegUsage,
                            targetLegUsage,
                            supportArmUsage
                        );
                    validReachUsages.Add(
                             poseScore
                        );

                    // =============================================
                    // 自由手
                    //
                    // 第一版假设自然垂直向下。
                    // 使用真实Physical Arm Length。
                    // =============================================

                    Vector2 freeHand =
                        chest
                        + Vector2.down
                        * reachProfile.PhysicalArmMaxReach;


                    // =============================================
                    // COM
                    // =============================================

                    Vector2 com =
                        CalculateCOM(
                            supportHand,
                            freeHand,
                            supportFoot,
                            targetFoot,
                            chest,
                            hip
                        );


                    // =============================================
                    // 保存Pose Sample
                    // =============================================

                    if (collectPoseSamples)
                    {
                        PoseSample sample =
                            new PoseSample();


                        sample.chestPosition =
                            chest;

                        sample.hipPosition =
                            hip;

                        sample.freeLimbPosition =
                            freeHand;

                        sample.centerOfMass =
                            com;

                        sample.reachUsage =
                            poseScore;


                        result.poseSamples.Add(
                            sample
                        );
                    }


                    // =============================================
                    // Best Pose
                    // =============================================

                    if (poseScore <
                        bestScore)
                    {
                        bestScore =
                            poseScore;

                        result.bestHipPosition =
                            hip;

                        result.bestChestPosition =
                            chest;
                    }
                }
            }
        }


        if (result.validBridgePoseCount > 0)
        {
            result.valid =
                true;

            result.bestReachUsage =
                bestScore;


            if (result.testedPoseConfigurationCount > 0)
            {
                result.poseFreedomRatio =
                    result.validBridgePoseCount
                    /
                    (float)result.testedPoseConfigurationCount;
            }


            CalculateReachStatistics(
                validReachUsages,
                result
            );
        }


        return result;
    }


    // =========================================================
    // COM
    // =========================================================

    private Vector2 CalculateCOM(
        Vector2 handA,
        Vector2 handB,
        Vector2 footA,
        Vector2 footB,
        Vector2 chest,
        Vector2 hip
    )
    {
        float totalWeight =
            limbWeight * 4f
            + chestWeight
            + hipWeight;


        Vector2 weightedPosition =
            handA * limbWeight
            +
            handB * limbWeight
            +
            footA * limbWeight
            +
            footB * limbWeight
            +
            chest * chestWeight
            +
            hip * hipWeight;


        return
            weightedPosition
            / totalWeight;
    }


    // =========================================================
    // Support State Cheap Filter
    // =========================================================

    public bool IsSupportStateGeometricallyPossible(
        Vector2 handPoint,
        Vector2 footPoint
    )
    {
        if (!ReachProfileValid())
        {
            return false;
        }


        float maximumSafeSpan =
            reachProfile.SafeArmMaxReach
            +
            reachProfile.TorsoLength
            +
            reachProfile.SafeLegMaxReach;


        return
            Vector2.Distance(
                handPoint,
                footPoint
            )
            <= maximumSafeSpan;
    }


    // =========================================================
    // Reach Statistics
    // =========================================================

    private void CalculateReachStatistics(
        List<float> values,
        TransitionResult result
    )
    {
        if (values == null ||
            values.Count == 0 ||
            result == null)
        {
            return;
        }


        // =============================================
        // Average
        // =============================================

        float sum =
            0f;


        for (int i = 0;
             i < values.Count;
             i++)
        {
            sum +=
                values[i];
        }


        result.averageReachUsage =
            sum / values.Count;


        // =============================================
        // Median / Percentiles
        // =============================================

        values.Sort();


        result.medianReachUsage =
            GetPercentile(
                values,
                0.50f
            );


        result.reachUsageP75 =
            GetPercentile(
                values,
                0.75f
            );


        result.reachUsageP90 =
            GetPercentile(
                values,
                0.90f
            );
    }

    private float GetPercentile(
    List<float> sortedValues,
    float percentile
)
    {
        if (sortedValues == null ||
            sortedValues.Count == 0)
        {
            return 0f;
        }


        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }


        percentile =
            Mathf.Clamp01(
                percentile
            );


        float position =
            (sortedValues.Count - 1)
            * percentile;


        int lowerIndex =
            Mathf.FloorToInt(
                position
            );

        int upperIndex =
            Mathf.CeilToInt(
                position
            );


        if (lowerIndex ==
            upperIndex)
        {
            return
                sortedValues[lowerIndex];
        }


        float t =
            position
            - lowerIndex;


        return Mathf.Lerp(
            sortedValues[lowerIndex],
            sortedValues[upperIndex],
            t
        );
    }

    // =========================================================
    // Helpers
    // =========================================================

    private bool ReachProfileValid()
    {
        if (reachProfile == null)
        {
            Debug.LogError(
                $"{name}: 没有指定ClimberReachProfile2D。"
            );

            return false;
        }


        if (reachProfile.SafeArmMaxReach <= 0f ||
            reachProfile.SafeLegMaxReach <= 0f ||
            reachProfile.PhysicalArmMaxReach <= 0f ||
            reachProfile.PhysicalLegMaxReach <= 0f ||
            reachProfile.TorsoLength <= 0f)
        {
            Debug.LogError(
                $"{name}: Reach Profile数据无效。" +
                $"请先Calculate Reach Profile。"
            );

            return false;
        }


        return true;
    }


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


    private void OnValidate()
    {
        bodySampleSpacing =
            Mathf.Max(
                0.01f,
                bodySampleSpacing
            );

        torsoAngleStep =
            Mathf.Clamp(
                torsoAngleStep,
                1f,
                30f
            );

        maxTorsoTiltDegrees =
            Mathf.Clamp(
                maxTorsoTiltDegrees,
                0f,
                90f
            );

        limbWeight =
            Mathf.Max(
                0.01f,
                limbWeight
            );

        chestWeight =
            Mathf.Max(
                0.01f,
                chestWeight
            );

        hipWeight =
            Mathf.Max(
                0.01f,
                hipWeight
            );
    }
}