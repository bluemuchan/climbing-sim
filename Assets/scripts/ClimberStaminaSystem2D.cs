using TMPro;
using UnityEngine;

/// <summary>
/// 玩家统一体力系统。
///
/// 体力变化由三个彼此独立的来源组成：
///
/// 1. 抓点状态：
///    Normal Hold / Sidepull
///    可以产生正恢复，也可以产生负恢复。
///
/// 2. COM向上移动：
///    产生额外体力消耗。
///
/// 3. WASD身体施力：
///    持续产生体力消耗。
///
/// 三项最终共同作用于同一个体力条。
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class ClimberStaminaSystem2D : MonoBehaviour
{
    // =========================================================
    // 系统引用
    // =========================================================

    [Header("重心系统")]

    [SerializeField]
    private BodyCenterTracker2D centerTracker;


    [Header("身体控制")]

    [SerializeField]
    private BodyForceController2D bodyForceController;


    // =========================================================
    // 四肢
    // =========================================================

    [Header("双手")]

    [SerializeField]
    private PhysicsDraggable2D leftHand;

    [SerializeField]
    private PhysicsDraggable2D rightHand;


    [Header("双脚")]

    [SerializeField]
    private PhysicsDraggable2D leftFoot;

    [SerializeField]
    private PhysicsDraggable2D rightFoot;


    // =========================================================
    // 体力参数
    // =========================================================

    [Header("体力参数")]

    [Min(1f)]
    [SerializeField]
    private float maximumStamina = 100f;


    [Tooltip("COM每向上移动1个世界单位消耗多少体力")]
    [Min(0f)]
    [SerializeField]
    private float staminaCostPerUnitRise = 30f;


    [Tooltip("持续使用有效WASD输入时，每秒消耗多少体力")]
    [Min(0f)]
    [SerializeField]
    private float wasdStaminaCostPerSecond = 8f;


    [Tooltip(
        "Normal Hold或Stable Sidepull状态下的基础每秒恢复量"
    )]
    [Min(0f)]
    [SerializeField]
    private float recoveryPerSecond = 15f;


    [Tooltip(
        "忽略非常微小的COM向上物理抖动"
    )]
    [Min(0f)]
    [SerializeField]
    private float minimumRiseToDrain = 0.001f;


    // =========================================================
    // UI
    // =========================================================

    [Header("UI")]

    [SerializeField]
    private TMP_Text staminaText;


    // =========================================================
    // 运行时状态
    // =========================================================

    [Header("运行时体力状态")]

    [SerializeField]
    private float currentStamina;

    [SerializeField]
    private bool isExhausted;

    [SerializeField]
    private bool canCurrentlyRecover;


    [Header("运行时调试 - 体力变化")]

    [Tooltip(
        "当前抓点组合最终得到的恢复倍率"
    )]
    [SerializeField]
    private float currentRecoveryMultiplier;


    [Tooltip(
        "当前每秒实际恢复/负恢复速度"
    )]
    [SerializeField]
    private float currentRecoveryRatePerSecond;


    [Tooltip(
        "当前WASD每秒消耗"
    )]
    [SerializeField]
    private float currentWasdCostPerSecond;


    [Tooltip(
        "当前物理帧由COM上升产生的体力消耗"
    )]
    [SerializeField]
    private float currentComCostThisStep;


    [Header("运行时调试 - 抓点类型")]

    [SerializeField]
    private bool leftHandOnSidepull;

    [SerializeField]
    private bool rightHandOnSidepull;

    [SerializeField]
    private bool hasNormalHandSupport;


    // =========================================================
    // 公共读取
    // =========================================================

    public float CurrentStamina
    {
        get { return currentStamina; }
    }

    public float MaximumStamina
    {
        get { return maximumStamina; }
    }

    public float NormalizedStamina
    {
        get
        {
            if (maximumStamina <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                currentStamina / maximumStamina
            );
        }
    }

    public bool IsExhausted
    {
        get { return isExhausted; }
    }

    public float CurrentRecoveryMultiplier
    {
        get { return currentRecoveryMultiplier; }
    }

    public float CurrentRecoveryRatePerSecond
    {
        get { return currentRecoveryRatePerSecond; }
    }


    // =========================================================
    // 初始化
    // =========================================================

    private void Awake()
    {
        currentStamina =
            maximumStamina;

        isExhausted =
            false;
    }

    private void Start()
    {
        ApplyStaminaToLimbs();
        UpdateStaminaUI();
    }


    // =========================================================
    // 主体力循环
    // =========================================================

    private void FixedUpdate()
    {
        if (!ReferencesAreConfigured())
        {
            return;
        }

        if (isExhausted)
        {
            return;
        }

        // =====================================================
        // 0. 双手全部脱离岩点
        // =====================================================

        bool anyHandAttached =
            leftHand.IsAttachedToHold
            || rightHand.IsAttachedToHold;

        if (!anyHandAttached)
        {
            currentStamina = 0f;

            ExhaustClimber();

            return;
        }

        float staminaChange =
            0f;

        float fixedDeltaTime =
            Time.fixedDeltaTime;


        // =====================================================
        // 1. 抓点状态产生的恢复 / 负恢复
        // =====================================================

        canCurrentlyRecover =
            CanRecoverStamina();

        currentRecoveryMultiplier =
            CalculateRecoveryMultiplier();

        currentRecoveryRatePerSecond =
            0f;

        if (canCurrentlyRecover)
        {
            /*
             * 这里的倍率可以为负数。
             *
             * Normal / Stable：
             * +15 × 1
             *
             * Weak：
             * +15 × 0.25
             *
             * Strain：
             * +15 × -0.5
             */
            currentRecoveryRatePerSecond =
                recoveryPerSecond
                * currentRecoveryMultiplier;

            staminaChange +=
                currentRecoveryRatePerSecond
                * fixedDeltaTime;
        }


        // =====================================================
        // 2. COM向上移动产生的独立消耗
        // =====================================================

        currentComCostThisStep =
            0f;

        float upwardDistance =
            centerTracker.UpwardDistanceThisStep;

        if (upwardDistance >
            minimumRiseToDrain)
        {
            currentComCostThisStep =
                upwardDistance
                * staminaCostPerUnitRise;

            staminaChange -=
                currentComCostThisStep;
        }


        // =====================================================
        // 3. WASD持续使用产生的独立消耗
        // =====================================================

        currentWasdCostPerSecond =
            0f;

        if (bodyForceController.IsApplyingForce)
        {
            currentWasdCostPerSecond =
                wasdStaminaCostPerSecond;

            staminaChange -=
                currentWasdCostPerSecond
                * fixedDeltaTime;
        }


        // =====================================================
        // 4. 所有来源共同作用于体力
        // =====================================================

        currentStamina +=
            staminaChange;

        currentStamina =
            Mathf.Clamp(
                currentStamina,
                0f,
                maximumStamina
            );


        // =====================================================
        // 5. 体力影响手脚控制能力
        // =====================================================

        ApplyStaminaToLimbs();


        // =====================================================
        // 6. UI
        // =====================================================

        UpdateStaminaUI();


        // =====================================================
        // 7. 耗尽
        // =====================================================

        if (currentStamina <= 0f)
        {
            ExhaustClimber();
        }
    }


    // =========================================================
    // 是否满足基本“休息姿势”
    // =========================================================

    private bool CanRecoverStamina()
    {
        bool atLeastOneHandAttached =
            leftHand.IsAttachedToHold
            || rightHand.IsAttachedToHold;

        bool atLeastOneFootAttached =
            leftFoot.IsAttachedToHold
            || rightFoot.IsAttachedToHold;

        bool anyLimbBeingDragged =
            leftHand.IsDragging
            || rightHand.IsDragging
            || leftFoot.IsDragging
            || rightFoot.IsDragging;

        return
            atLeastOneHandAttached
            &&
            atLeastOneFootAttached
            &&
            !anyLimbBeingDragged;
    }


    // =========================================================
    // 计算抓点组合的恢复倍率
    // =========================================================

    private float CalculateRecoveryMultiplier()
    {
        bool leftAttached =
            leftHand.IsAttachedToHold;

        bool rightAttached =
            rightHand.IsAttachedToHold;


        // 没有任何手抓住
        if (!leftAttached &&
            !rightAttached)
        {
            leftHandOnSidepull =
                false;

            rightHandOnSidepull =
                false;

            hasNormalHandSupport =
                false;

            return 0f;
        }


        // =============================================
        // 找出左右手当前抓住的Sidepull
        // =============================================

        SidepullHold2D leftSidepull =
            GetSidepullForHand(
                leftHand
            );

        SidepullHold2D rightSidepull =
            GetSidepullForHand(
                rightHand
            );


        leftHandOnSidepull =
            leftAttached
            && leftSidepull != null;

        rightHandOnSidepull =
            rightAttached
            && rightSidepull != null;


        // =============================================
        // 判断有没有普通Hold提供稳定支撑
        // =============================================

        bool leftHandOnNormal =
            leftAttached
            && leftSidepull == null;

        bool rightHandOnNormal =
            rightAttached
            && rightSidepull == null;


        hasNormalHandSupport =
            leftHandOnNormal
            || rightHandOnNormal;


        // =============================================
        // 没有任何Sidepull
        // → 全部都是普通Hold
        // =============================================

        if (!leftHandOnSidepull &&
            !rightHandOnSidepull)
        {
            return 1f;
        }


        // =============================================
        // 找当前所有Sidepull中最差的倍率
        // =============================================

        float worstSidepullMultiplier =
            1f;


        if (leftHandOnSidepull)
        {
            float leftMultiplier =
                leftSidepull
                    .GetRecoveryMultiplierForHand(
                        leftHand
                    );

            worstSidepullMultiplier =
                Mathf.Min(
                    worstSidepullMultiplier,
                    leftMultiplier
                );
        }


        if (rightHandOnSidepull)
        {
            float rightMultiplier =
                rightSidepull
                    .GetRecoveryMultiplierForHand(
                        rightHand
                    );

            worstSidepullMultiplier =
                Mathf.Min(
                    worstSidepullMultiplier,
                    rightMultiplier
                );
        }


       


        // =============================================
        // 只有Sidepull
        // =============================================

        /*
         * 一只Sidepull：
         * 直接使用其状态。
         *
         * 两只Sidepull：
         * Mathf.Min已经自动选择了更差的那个。
         *
         * 如果双手抓同一个Sidepull：
         * SidepullHold2D已经用双手中点算出共同状态，
         * 因此左右手读取到相同Multiplier。
         */

        return
            worstSidepullMultiplier;
    }


    // =========================================================
    // 当前手抓的是不是Sidepull
    // =========================================================

    private SidepullHold2D GetSidepullForHand(
        PhysicsDraggable2D hand
    )
    {
        if (hand == null)
        {
            return null;
        }

        if (!hand.IsAttachedToHold)
        {
            return null;
        }

        if (hand.CurrentHoldCollider == null)
        {
            return null;
        }

        return
            hand.CurrentHoldCollider
                .GetComponentInParent<
                    SidepullHold2D
                >();
    }


    // =========================================================
    // 将体力同步给手脚控制系统
    // =========================================================

    private void ApplyStaminaToLimbs()
    {
        float normalized =
            NormalizedStamina;

        if (leftHand != null)
        {
            leftHand
                .SetDragStrengthFromStamina(
                    normalized
                );
        }

        if (rightHand != null)
        {
            rightHand
                .SetDragStrengthFromStamina(
                    normalized
                );
        }

        if (leftFoot != null)
        {
            leftFoot
                .SetDragStrengthFromStamina(
                    normalized
                );
        }

        if (rightFoot != null)
        {
            rightFoot
                .SetDragStrengthFromStamina(
                    normalized
                );
        }
    }


    // =========================================================
    // 体力耗尽
    // =========================================================

    private void ExhaustClimber()
    {
        if (isExhausted)
        {
            return;
        }

        currentStamina =
            0f;

        isExhausted =
            true;

        canCurrentlyRecover =
            false;

        currentRecoveryRatePerSecond =
            0f;

        currentWasdCostPerSecond =
            0f;

        currentComCostThisStep =
            0f;


        /*
         * 四肢全部：
         *
         * 取消拖动
         * +
         * 解除Hold
         * +
         * 禁止重新抓取
         */

        leftHand
            .SetInteractionAllowed(false);

        rightHand
            .SetInteractionAllowed(false);

        leftFoot
            .SetInteractionAllowed(false);

        rightFoot
            .SetInteractionAllowed(false);


        ApplyStaminaToLimbs();
        UpdateStaminaUI();


        Debug.Log(
            "体力耗尽：所有手脚已脱离岩点。"
        );
    }


    // =========================================================
    // 重置
    // =========================================================

    [ContextMenu("Reset Stamina")]
    public void ResetStamina()
    {
        currentStamina =
            maximumStamina;

        isExhausted =
            false;

        canCurrentlyRecover =
            false;

        currentRecoveryMultiplier =
            0f;

        currentRecoveryRatePerSecond =
            0f;

        currentWasdCostPerSecond =
            0f;

        currentComCostThisStep =
            0f;


        if (leftHand != null)
        {
            leftHand.ResetForNewAttempt();
        }

        if (rightHand != null)
        {
            rightHand.ResetForNewAttempt();
        }

        if (leftFoot != null)
        {
            leftFoot.ResetForNewAttempt();
        }

        if (rightFoot != null)
        {
            rightFoot.ResetForNewAttempt();
        }


        if (centerTracker != null)
        {
            centerTracker.ResetTracking();
        }


        ApplyStaminaToLimbs();
        UpdateStaminaUI();


        Debug.Log(
            "体力系统已重置。"
        );
    }


    // =========================================================
    // UI
    // =========================================================

    private void UpdateStaminaUI()
    {
        if (staminaText == null)
        {
            return;
        }

        int displayedPercentage =
            Mathf.CeilToInt(
                NormalizedStamina
                * 100f
            );

        staminaText.text =
            $"{displayedPercentage}%";
    }


    // =========================================================
    // 引用检查
    // =========================================================

    private bool ReferencesAreConfigured()
    {
        return
            centerTracker != null
            &&
            bodyForceController != null
            &&
            leftHand != null
            &&
            rightHand != null
            &&
            leftFoot != null
            &&
            rightFoot != null;
    }


    // =========================================================
    // Inspector校验
    // =========================================================

    private void OnValidate()
    {
        maximumStamina =
            Mathf.Max(
                1f,
                maximumStamina
            );

        currentStamina =
            Mathf.Clamp(
                currentStamina,
                0f,
                maximumStamina
            );
    }
}