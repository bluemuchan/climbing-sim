using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class PhysicsDraggable2D : MonoBehaviour
{
    [Header("运行时IK")]

    [Tooltip("这只手/脚对应的RuntimeLimbIK2D")]
    [SerializeField]
    private RuntimeLimbIK2D runtimeIK;

    [Header("岩点抓取")]

    [Tooltip("选择Hold Layer")]
    [SerializeField]
    private LayerMask holdLayer;

    private Camera mainCamera;

    private Rigidbody2D objectRigidbody;

    private Collider2D objectCollider;

    /*
     * 只表示连接岩点的临时Joint。
     * 不代表连接骨骼的永久HingeJoint。
     */
    private HingeJoint2D holdJoint;

    private Collider2D currentHoldCollider;

    private HoldGripCapacity2D currentHoldCapacity;

    // 抓住Hold那一刻记录下来的抓取点。
    // 使用Hold Rigidbody的局部坐标保存，
    // 不再实时读取Hand的位置。
    private Vector2 currentHoldGripLocalPoint;

    private bool hasRecordedGripPoint;

    private bool isDragging;

    private bool interactionAllowed = true;

    private bool holdAttachmentAllowed = true;

    public bool IsDragging
    {
        get { return isDragging; }
    }

    public bool IsAttachedToHold
    {
        get { return holdJoint != null; }
    }

    public Collider2D CurrentHoldCollider
    {
        get { return currentHoldCollider; }
    }

    private void Awake()
    {
        mainCamera =
            Camera.main;

        objectRigidbody =
            GetComponent<Rigidbody2D>();

        objectCollider =
            GetComponent<Collider2D>();

        if (mainCamera == null)
        {
            Debug.LogError(
                "场景中没有Main Camera。"
            );
        }

        if (objectCollider == null)
        {
            Debug.LogError(
                $"{name} 没有Collider2D。"
            );
        }

        if (runtimeIK == null)
        {
            Debug.LogError(
                $"{name} 没有指定Runtime Limb IK。"
            );
        }
    }

    private void Start()
    {
        TryAttachToHold();
    }

    private void Update()
    {
        if (!interactionAllowed)
        {
            return;
        }

        if (Mouse.current == null
            || mainCamera == null
            || objectCollider == null
            || runtimeIK == null)
        {
            return;
        }

        Vector2 mouseWorldPosition =
            GetMouseWorldPosition();

        if (
            Mouse.current
                .leftButton
                .wasPressedThisFrame
        )
        {
            if (
                objectCollider.OverlapPoint(
                    mouseWorldPosition
                )
            )
            {
                StartDragging(
                    mouseWorldPosition
                );
            }
        }

        if (
            isDragging
            && Mouse.current
                .leftButton
                .isPressed
        )
        {
            runtimeIK.SetTarget(
                mouseWorldPosition
            );
        }

        if (
            isDragging
            && Mouse.current
                .leftButton
                .wasReleasedThisFrame
        )
        {
            StopDragging();

            TryAttachToHold();
        }
    }

    private void StartDragging(
        Vector2 mouseWorldPosition
    )
    {
        /*
         * 如果原本抓着岩点，
         * 先真正松手。
         */
        DetachFromHold();

        isDragging = true;

        runtimeIK.BeginDrag(
            mouseWorldPosition
        );
    }

    private void StopDragging()
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;

        runtimeIK.EndDrag();
    }

    private void TryAttachToHold()
    {
        if (!holdAttachmentAllowed ||
            holdJoint != null)
        {
            return;
        }


        Collider2D[] holdColliders =
            Physics2D.OverlapPointAll(
                objectRigidbody.position,
                holdLayer
            );


        foreach (
            Collider2D holdCollider
            in holdColliders
        )
        {
            if (TryAttachToCollider(
                holdCollider
            ))
            {
                return;
            }
        }
    }

    private bool TryAttachToCollider(
    Collider2D holdCollider
)
    {
        if (holdCollider == null)
        {
            return false;
        }

        if (holdJoint != null)
        {
            return false;
        }


        // =====================================================
        // Hold Rigidbody
        // =====================================================

        Rigidbody2D holdRigidbody =
            holdCollider.attachedRigidbody;


        if (holdRigidbody == null)
        {
            Debug.LogWarning(
                $"{holdCollider.name} 没有 Rigidbody2D。"
            );

            return false;
        }


        // =====================================================
        // 容量
        // =====================================================

        HoldGripCapacity2D capacity =
            holdCollider
                .GetComponentInParent<
                    HoldGripCapacity2D
                >();


        if (capacity != null)
        {
            if (!capacity.TryOccupy(this))
            {
                return false;
            }
        }


        // =====================================================
        // 创建抓取 Joint
        // =====================================================

        holdJoint =
            gameObject
                .AddComponent<HingeJoint2D>();


        holdJoint.autoConfigureConnectedAnchor =
            false;

        holdJoint.connectedBody =
            holdRigidbody;

        holdJoint.anchor =
            Vector2.zero;


        // =====================================================
        // 记录真正的抓取位置
        // =====================================================

        Vector3 localPoint =
            holdRigidbody
                .transform
                .InverseTransformPoint(
                    objectRigidbody.position
                );


        currentHoldGripLocalPoint =
            new Vector2(
                localPoint.x,
                localPoint.y
            );

        hasRecordedGripPoint =
            true;


        holdJoint.connectedAnchor =
            currentHoldGripLocalPoint;


        holdJoint.enableCollision =
            false;

        holdJoint.useMotor =
            false;

        holdJoint.useLimits =
            false;


        // =====================================================
        // 当前Hold记录
        // =====================================================

        currentHoldCollider =
            holdCollider;

        currentHoldCapacity =
            capacity;


        Debug.Log(
            $"{name} 抓住了 {holdCollider.name}"
        );

        HoldLevelPoint2D levelPoint =
    holdCollider
        .GetComponentInParent<
            HoldLevelPoint2D
        >();

        if (levelPoint != null)
        {
            levelPoint.OnLimbAttached(this);
        }


        return true;
    }
    public bool TryAttachToSpecificHold(
    Collider2D holdCollider
)
    {
        if (!holdAttachmentAllowed)
        {
            return false;
        }

        if (holdJoint != null)
        {
            return false;
        }

        if (holdCollider == null)
        {
            return false;
        }


        /*
         * Restart后要求肢体真的位于
         * Start Hold内部。
         *
         * 避免由于Level配置错误，
         * 手隔着很远突然连接一个Joint。
         */
        if (!holdCollider.OverlapPoint(
            objectRigidbody.position
        ))
        {
            Debug.LogWarning(
                $"{name} 不在 Start Hold " +
                $"{holdCollider.name} 内部，" +
                $"无法重新抓取。"
            );

            return false;
        }


        return TryAttachToCollider(
            holdCollider
        );
    }

    private void DetachFromHold()
    {
        // =============================================
        // 删除物理抓取Joint
        // =============================================

        if (holdJoint != null)
        {
            holdJoint.enabled =
                false;

            Destroy(holdJoint);

            holdJoint =
                null;
        }


        // =============================================
        // 释放Hold容量
        // =============================================

        if (currentHoldCapacity != null)
        {
            currentHoldCapacity
                .Release(this);

            currentHoldCapacity =
                null;
        }

        // 清除抓取坐标
        hasRecordedGripPoint =
            false;

        currentHoldGripLocalPoint =
            Vector2.zero;

        // =============================================
        // 清除当前Hold记录
        // =============================================

        currentHoldCollider =
            null;


    }

    /// <summary>
    /// 保留这个方法名，
    /// 这样现有Stamina脚本不用修改。
    ///
    /// 现在体力不再控制TargetJoint力，
    /// 而是控制Runtime IK的跟随速度。
    /// </summary>
    public void SetDragStrengthFromStamina(
        float normalizedStamina
    )
    {
        if (runtimeIK == null)
        {
            return;
        }

        runtimeIK.SetControlStrength(
            normalizedStamina
        );
    }

    public void ForceDetachFromHold()
    {
        DetachFromHold();
    }

    public void ForceReleaseAndCancelDrag()
    {
        if (isDragging)
        {
            StopDragging();
        }

        DetachFromHold();
    }

    public void SetInteractionAllowed(
        bool allowed
    )
    {
        interactionAllowed =
            allowed;

        holdAttachmentAllowed =
            allowed;

        if (!allowed)
        {
            ForceReleaseAndCancelDrag();
        }
    }

    public void ResetForNewAttempt()
    {
        interactionAllowed =
            true;

        holdAttachmentAllowed =
            true;

        if (isDragging)
        {
            StopDragging();
        }

        DetachFromHold();

        SetDragStrengthFromStamina(
            1f
        );
    }

    private Vector2 GetMouseWorldPosition()
    {
        Vector2 mouseScreenPosition =
            Mouse.current
                .position
                .ReadValue();

        Vector3 screenPosition =
            new Vector3(
                mouseScreenPosition.x,
                mouseScreenPosition.y,
                -mainCamera
                    .transform
                    .position
                    .z
            );

        Vector3 worldPosition =
            mainCamera
                .ScreenToWorldPoint(
                    screenPosition
                );

        return new Vector2(
            worldPosition.x,
            worldPosition.y
        );
    }

    /// <summary>
    /// 当前真正的抓取坐标。
    ///
    /// 这个位置记录于成功抓住Hold的瞬间，
    /// 不会因为Hand后来被IK或碰撞挤动而改变。
    ///
    /// 如果Hold本身移动或旋转，
    /// 抓取点会跟随Hold一起移动。
    /// </summary>
    public Vector2 CurrentHoldGripWorldPoint
    {
        get
        {
            if (!hasRecordedGripPoint ||
                currentHoldCollider == null)
            {
                return objectRigidbody != null
                    ? objectRigidbody.position
                    : (Vector2)transform.position;
            }

            Rigidbody2D holdRigidbody =
                currentHoldCollider.attachedRigidbody;

            if (holdRigidbody == null)
            {
                return objectRigidbody != null
                    ? objectRigidbody.position
                    : (Vector2)transform.position;
            }

            Vector3 worldPoint =
                holdRigidbody.transform.TransformPoint(
                    currentHoldGripLocalPoint
                );

            return new Vector2(
                worldPoint.x,
                worldPoint.y
            );
        }
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (isDragging
            && runtimeIK != null)
        {
            StopDragging();
        }

        /*
         * 如果角色被Disable时仍抓着Hold，
         * 一定释放抓取容量。
         */
        DetachFromHold();
    }

    /// <summary>
    /// 禁止/恢复玩家操作，但不解除当前已经建立的Hold抓取。
    ///
    /// 主要用于关卡完成。
    /// </summary>
    public void SetInputAllowedWithoutDetaching(
        bool allowed
    )
    {
        interactionAllowed = allowed;

        /*
         * 如果完成关卡时刚好正在拖动一个肢体，
         * 结束这个拖动。
         *
         * 已经抓住Hold的其他肢体不会受到影响。
         */
        if (!allowed && isDragging)
        {
            StopDragging();
        }
    }
}