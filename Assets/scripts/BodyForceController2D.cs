using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class BodyForceController2D : MonoBehaviour
{
    [Header("身体推力")]
    [Min(0f)]
    [SerializeField]
    private float movementForce = 20f;


    [Header("脚部支撑")]

    [SerializeField]
    private PhysicsDraggable2D leftFoot;

    [SerializeField]
    private PhysicsDraggable2D rightFoot;

    [Tooltip(
        "两只脚都没有踩在岩点上时，" +
        "WASD推力乘以这个倍率。\n" +
        "0.5 = 推力减半"
    )]
    [Range(0f, 1f)]
    [SerializeField]
    private float noFootSupportForceMultiplier = 0.5f;


    [Header("运行时调试")]

    [SerializeField]
    private Vector2 currentInputDirection;

    [SerializeField]
    private bool hasFootSupport;

    [SerializeField]
    private float currentForceMultiplier = 1f;

    [SerializeField]
    private float currentActualForce;


    // =========================================================
    // 公共读取
    // =========================================================

    public Vector2 CurrentInputDirection
    {
        get { return currentInputDirection; }
    }

    /// <summary>
    /// 当前是否存在有效WASD施力输入。
    ///
    /// 仍然保持原来的逻辑：
    /// 即使没有脚、推力被削弱，
    /// 只要WASD有效，仍然算正在施力。
    ///
    /// 因此现有体力系统仍然会正常消耗WASD体力。
    /// </summary>
    public bool IsApplyingForce
    {
        get
        {
            return currentInputDirection.sqrMagnitude
                > 0.001f;
        }
    }

    public bool HasFootSupport
    {
        get { return hasFootSupport; }
    }

    public float CurrentForceMultiplier
    {
        get { return currentForceMultiplier; }
    }


    private Rigidbody2D body;


    // =========================================================
    // 初始化
    // =========================================================

    private void Awake()
    {
        body =
            GetComponent<Rigidbody2D>();

        if (leftFoot == null)
        {
            Debug.LogWarning(
                $"{name}: BodyForceController没有指定Left Foot。"
            );
        }

        if (rightFoot == null)
        {
            Debug.LogWarning(
                $"{name}: BodyForceController没有指定Right Foot。"
            );
        }
    }


    // =========================================================
    // 输入
    // =========================================================

    private void Update()
    {
        ReadMovementInput();
    }


    private void ReadMovementInput()
    {
        currentInputDirection =
            Vector2.zero;

        if (Keyboard.current == null)
        {
            return;
        }


        bool up =
            Keyboard.current.wKey.isPressed;

        bool down =
            Keyboard.current.sKey.isPressed;

        bool left =
            Keyboard.current.aKey.isPressed;

        bool right =
            Keyboard.current.dKey.isPressed;


        // =====================================================
        // 保留之前的冲突规则
        //
        // W + S
        // 或
        // A + D
        //
        // 只要有一组冲突，本帧完全不施力。
        // =====================================================

        if ((up && down) ||
            (left && right))
        {
            currentInputDirection =
                Vector2.zero;

            return;
        }


        if (up)
        {
            currentInputDirection +=
                Vector2.up;
        }

        if (down)
        {
            currentInputDirection +=
                Vector2.down;
        }

        if (left)
        {
            currentInputDirection +=
                Vector2.left;
        }

        if (right)
        {
            currentInputDirection +=
                Vector2.right;
        }


        // =====================================================
        // 对角方向归一化
        //
        // W
        // 和
        // W + A
        //
        // 总推力大小相同。
        // =====================================================

        if (currentInputDirection.sqrMagnitude >
            0.001f)
        {
            currentInputDirection =
                currentInputDirection.normalized;
        }
    }


    // =========================================================
    // 物理推力
    // =========================================================

    private void FixedUpdate()
    {
        UpdateFootSupport();


        if (currentInputDirection.sqrMagnitude <=
            0.001f)
        {
            currentActualForce =
                0f;

            return;
        }


        // =====================================================
        // 根据有没有脚点决定推力倍率
        // =====================================================

        if (hasFootSupport)
        {
            currentForceMultiplier =
                1f;
        }
        else
        {
            currentForceMultiplier =
                noFootSupportForceMultiplier;
        }


        float actualForce =
            movementForce
            * currentForceMultiplier;


        currentActualForce =
            actualForce;


        Vector2 force =
            currentInputDirection
            * actualForce;


        body.AddForce(
            force,
            ForceMode2D.Force
        );
    }


    // =========================================================
    // 检查脚部支撑
    // =========================================================

    private void UpdateFootSupport()
    {
        bool leftFootAttached =
            leftFoot != null
            &&
            leftFoot.IsAttachedToHold;

        bool rightFootAttached =
            rightFoot != null
            &&
            rightFoot.IsAttachedToHold;


        // 只需要至少一只脚有支撑
        hasFootSupport =
            leftFootAttached
            || rightFootAttached;


        currentForceMultiplier =
            hasFootSupport
            ? 1f
            : noFootSupportForceMultiplier;
    }
}