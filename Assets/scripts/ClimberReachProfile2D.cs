using UnityEngine;

/// <summary>
/// 保存并计算角色的基本身体尺寸。
///
/// 后续Reachability Validator统一从这里读取：
///
/// 手臂长度
/// 腿长度
/// 躯干长度
/// Safe Reach
///
/// 不直接负责判断关卡是否可达。
/// </summary>
[DisallowMultipleComponent]
public class ClimberReachProfile2D : MonoBehaviour
{
    // =========================================================
    // 手臂节点
    // =========================================================

    [Header("左手臂")]

    [SerializeField]
    private Transform chest;

    [SerializeField]
    private Transform leftElbow;

    [SerializeField]
    private Transform leftHand;


    [Header("右手臂")]

    [SerializeField]
    private Transform rightElbow;

    [SerializeField]
    private Transform rightHand;


    // =========================================================
    // 腿部节点
    // =========================================================

    [Header("身体 / 腿")]

    [SerializeField]
    private Transform hip;


    [Header("左腿")]

    [SerializeField]
    private Transform leftKnee;

    [SerializeField]
    private Transform leftFoot;


    [Header("右腿")]

    [SerializeField]
    private Transform rightKnee;

    [SerializeField]
    private Transform rightFoot;


    // =========================================================
    // Validator安全倍率
    // =========================================================

    [Header("AI安全可达范围")]

    [Tooltip(
        "AI保证路线允许使用的最大肢体伸展比例。\n" +
        "0.9 = 最多使用物理最大长度的90%。"
    )]
    [Range(0.5f, 1f)]
    [SerializeField]
    private float safeReachMultiplier = 0.9f;


    // =========================================================
    // 计算结果
    // =========================================================

    [Header("计算结果 - 手臂")]

    [SerializeField]
    private float leftUpperArmLength;

    [SerializeField]
    private float leftLowerArmLength;

    [SerializeField]
    private float rightUpperArmLength;

    [SerializeField]
    private float rightLowerArmLength;

    [SerializeField]
    private float physicalArmMaxReach;

    [SerializeField]
    private float safeArmMaxReach;


    [Header("计算结果 - 腿")]

    [SerializeField]
    private float leftUpperLegLength;

    [SerializeField]
    private float leftLowerLegLength;

    [SerializeField]
    private float rightUpperLegLength;

    [SerializeField]
    private float rightLowerLegLength;

    [SerializeField]
    private float physicalLegMaxReach;

    [SerializeField]
    private float safeLegMaxReach;


    [Header("计算结果 - 身体")]

    [SerializeField]
    private float torsoLength;


    // =========================================================
    // Public
    // =========================================================

    public float PhysicalArmMaxReach
    {
        get { return physicalArmMaxReach; }
    }

    public float SafeArmMaxReach
    {
        get { return safeArmMaxReach; }
    }

    public float PhysicalLegMaxReach
    {
        get { return physicalLegMaxReach; }
    }

    public float SafeLegMaxReach
    {
        get { return safeLegMaxReach; }
    }

    public float TorsoLength
    {
        get { return torsoLength; }
    }

    public float SafeReachMultiplier
    {
        get { return safeReachMultiplier; }
    }


    // =========================================================
    // 自动计算
    // =========================================================

    [ContextMenu("Calculate Reach Profile")]
    public void CalculateReachProfile()
    {
        if (!ReferencesComplete())
        {
            Debug.LogError(
                $"{name}: Reach Profile引用没有填写完整。"
            );

            return;
        }


        // =====================================================
        // 左手臂
        // =====================================================

        leftUpperArmLength =
            Vector2.Distance(
                chest.position,
                leftElbow.position
            );

        leftLowerArmLength =
            Vector2.Distance(
                leftElbow.position,
                leftHand.position
            );


        // =====================================================
        // 右手臂
        // =====================================================

        rightUpperArmLength =
            Vector2.Distance(
                chest.position,
                rightElbow.position
            );

        rightLowerArmLength =
            Vector2.Distance(
                rightElbow.position,
                rightHand.position
            );


        float leftArmMax =
            leftUpperArmLength
            + leftLowerArmLength;

        float rightArmMax =
            rightUpperArmLength
            + rightLowerArmLength;


        /*
         * 使用左右手中较短的那一边。
         *
         * Validator目标是“保证可以完成”，
         * 所以这里采取保守值。
         */
        physicalArmMaxReach =
            Mathf.Min(
                leftArmMax,
                rightArmMax
            );


        safeArmMaxReach =
            physicalArmMaxReach
            * safeReachMultiplier;


        // =====================================================
        // 左腿
        // =====================================================

        leftUpperLegLength =
            Vector2.Distance(
                hip.position,
                leftKnee.position
            );

        leftLowerLegLength =
            Vector2.Distance(
                leftKnee.position,
                leftFoot.position
            );


        // =====================================================
        // 右腿
        // =====================================================

        rightUpperLegLength =
            Vector2.Distance(
                hip.position,
                rightKnee.position
            );

        rightLowerLegLength =
            Vector2.Distance(
                rightKnee.position,
                rightFoot.position
            );


        float leftLegMax =
            leftUpperLegLength
            + leftLowerLegLength;

        float rightLegMax =
            rightUpperLegLength
            + rightLowerLegLength;


        physicalLegMaxReach =
            Mathf.Min(
                leftLegMax,
                rightLegMax
            );


        safeLegMaxReach =
            physicalLegMaxReach
            * safeReachMultiplier;


        // =====================================================
        // Torso
        // =====================================================

        torsoLength =
            Vector2.Distance(
                chest.position,
                hip.position
            );


        Debug.Log(
            $"Reach Profile计算完成\n" +
            $"Arm Physical Max = {physicalArmMaxReach:F3}\n" +
            $"Arm Safe Max = {safeArmMaxReach:F3}\n" +
            $"Leg Physical Max = {physicalLegMaxReach:F3}\n" +
            $"Leg Safe Max = {safeLegMaxReach:F3}\n" +
            $"Torso = {torsoLength:F3}"
        );
    }


    private bool ReferencesComplete()
    {
        return
            chest != null
            &&
            hip != null
            &&
            leftElbow != null
            &&
            leftHand != null
            &&
            rightElbow != null
            &&
            rightHand != null
            &&
            leftKnee != null
            &&
            leftFoot != null
            &&
            rightKnee != null
            &&
            rightFoot != null;
    }


    private void OnValidate()
    {
        safeReachMultiplier =
            Mathf.Clamp(
                safeReachMultiplier,
                0.5f,
                1f
            );
    }
}