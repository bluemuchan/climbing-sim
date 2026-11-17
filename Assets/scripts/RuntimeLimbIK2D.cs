using UnityEngine;

/// <summary>
/// 运行时两段式IK。
///
/// 拖动时：
/// 1. 暂时断开这一条肢体的物理Joint
/// 2. 将肢体改成Kinematic
/// 3. 使用两段式IK根据鼠标位置摆姿势
///
/// 松开时：
/// 1. 恢复Dynamic
/// 2. 恢复原有HingeJoint2D
///
/// Root（Chest/Hip）永远保持Dynamic，
/// 因此拖动手脚不会给身体中心施加拉力。
/// </summary>
[DisallowMultipleComponent]
public class RuntimeLimbIK2D : MonoBehaviour
{
    [Header("三个节点")]

    [Tooltip("肩部或胯部")]
    [SerializeField]
    private Transform rootPoint;

    [Tooltip("肘部或膝盖")]
    [SerializeField]
    private Transform jointPoint;

    [Tooltip("手部或脚部")]
    [SerializeField]
    private Transform endPoint;

    [Header("两根骨骼")]

    [Tooltip("肩→肘，或胯→膝")]
    [SerializeField]
    private Transform firstBone;

    [Tooltip("肘→手，或膝→脚")]
    [SerializeField]
    private Transform secondBone;

    [Header("拖动速度")]

    [Tooltip("满体力时，手脚每秒最多移动多少世界单位")]
    [Min(0.1f)]
    [SerializeField]
    private float fullStaminaMoveSpeed = 6f;

    [Tooltip("0体力附近保留的移动速度比例")]
    [Range(0f, 1f)]
    [SerializeField]
    private float minimumMoveSpeedMultiplier = 0.15f;

    [Header("弯曲")]

    [Tooltip("如果手肘或膝盖向错误方向弯曲，可以切换")]
    [SerializeField]
    private bool flipBendDirection;

    [Header("运行时调试")]

    [SerializeField]
    private bool isDragging;

    [SerializeField]
    private float firstLength;

    [SerializeField]
    private float secondLength;

    [SerializeField]
    private Vector2 desiredTarget;

    [SerializeField]
    private Vector2 controlledTarget;

    private Rigidbody2D jointBody;
    private Rigidbody2D endBody;

    private Rigidbody2D firstBoneBody;
    private Rigidbody2D secondBoneBody;

    /*
     * 这一条肢体自己的四个物理连接。
     *
     * Root -> FirstBone
     * Joint -> FirstBone
     * Joint -> SecondBone
     * End -> SecondBone
     */
    private HingeJoint2D rootToFirstJoint;
    private HingeJoint2D jointToFirstJoint;
    private HingeJoint2D jointToSecondJoint;
    private HingeJoint2D endToSecondJoint;

    private float staminaControl = 1f;

    private Vector2 lastJointPosition;
    private Vector2 lastEndPosition;

    private Vector2 lastFirstBonePosition;
    private Vector2 lastSecondBonePosition;

    private float lastFirstBoneRotation;
    private float lastSecondBoneRotation;

    public bool IsDragging
    {
        get { return isDragging; }
    }

    private void Awake()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        jointBody =
            jointPoint.GetComponent<Rigidbody2D>();

        endBody =
            endPoint.GetComponent<Rigidbody2D>();

        firstBoneBody =
            firstBone.GetComponent<Rigidbody2D>();

        secondBoneBody =
            secondBone.GetComponent<Rigidbody2D>();

        if (jointBody == null
            || endBody == null
            || firstBoneBody == null
            || secondBoneBody == null)
        {
            Debug.LogError(
                $"{name}: Runtime IK需要Joint、End和两根Bone都有Rigidbody2D。"
            );

            enabled = false;
            return;
        }

        // 游戏开始时直接根据当前姿势记录真实骨骼长度。
        firstLength =
            Vector2.Distance(
                rootPoint.position,
                jointPoint.position
            );

        secondLength =
            Vector2.Distance(
                jointPoint.position,
                endPoint.position
            );

        FindPhysicalJoints();

        controlledTarget =
            endBody.position;

        desiredTarget =
            controlledTarget;
    }

    private bool ValidateReferences()
    {
        if (rootPoint == null
            || jointPoint == null
            || endPoint == null
            || firstBone == null
            || secondBone == null)
        {
            Debug.LogError(
                $"{name}: Runtime Limb IK引用没有填写完整。"
            );

            return false;
        }

        return true;
    }

    /// <summary>
    /// 自动找到这条肢体原有的HingeJoint2D。
    /// 前提是你的Joint都放在圆形节点上，
    /// 并通过Connected Body连接对应Bone。
    /// </summary>
    private void FindPhysicalJoints()
    {
        rootToFirstJoint =
            FindJointConnectingTo(
                rootPoint,
                firstBoneBody
            );

        jointToFirstJoint =
            FindJointConnectingTo(
                jointPoint,
                firstBoneBody
            );

        jointToSecondJoint =
            FindJointConnectingTo(
                jointPoint,
                secondBoneBody
            );

        endToSecondJoint =
            FindJointConnectingTo(
                endPoint,
                secondBoneBody
            );

        if (rootToFirstJoint == null)
        {
            Debug.LogError(
                $"{name}: 没找到 Root → First Bone 的 HingeJoint2D。"
            );
        }

        if (jointToFirstJoint == null)
        {
            Debug.LogError(
                $"{name}: 没找到 Joint → First Bone 的 HingeJoint2D。"
            );
        }

        if (jointToSecondJoint == null)
        {
            Debug.LogError(
                $"{name}: 没找到 Joint → Second Bone 的 HingeJoint2D。"
            );
        }

        if (endToSecondJoint == null)
        {
            Debug.LogError(
                $"{name}: 没找到 End → Second Bone 的 HingeJoint2D。"
            );
        }
    }

    private HingeJoint2D FindJointConnectingTo(
        Transform owner,
        Rigidbody2D connectedBody
    )
    {
        HingeJoint2D[] joints =
            owner.GetComponents<HingeJoint2D>();

        foreach (HingeJoint2D joint in joints)
        {
            if (joint.connectedBody == connectedBody)
            {
                return joint;
            }
        }

        return null;
    }

    public void BeginDrag(Vector2 mousePosition)
    {
        if (isDragging)
        {
            return;
        }

        isDragging = true;

        controlledTarget =
            endBody.position;

        desiredTarget =
            mousePosition;

        SetPhysicalConnectionsEnabled(false);

        /*
         * 只有这一条肢体变成Kinematic。
         *
         * Chest/Hip绝对不改，
         * 所以身体继续自由运动。
         */
        SetLimbBodyType(
            RigidbodyType2D.Kinematic
        );

        // 清掉旧速度
        ClearLimbVelocity();

        SolvePose(controlledTarget);
        ApplyPoseImmediately();
    }

    public void SetTarget(Vector2 mousePosition)
    {
        desiredTarget =
            mousePosition;
    }

    private void FixedUpdate()
    {
        if (!isDragging)
        {
            return;
        }

        float currentMoveSpeed =
            Mathf.Lerp(
                fullStaminaMoveSpeed
                    * minimumMoveSpeedMultiplier,
                fullStaminaMoveSpeed,
                staminaControl
            );

        /*
         * 手不会瞬移到鼠标。
         * stamina越低，跟随速度越慢。
         */
        controlledTarget =
            Vector2.MoveTowards(
                controlledTarget,
                desiredTarget,
                currentMoveSpeed
                    * Time.fixedDeltaTime
            );

        SolvePose(controlledTarget);
        ApplyPoseKinematically();
    }

    public void EndDrag()
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;

        /*
         * 先确保最后一次IK姿势准确落到Rigidbody上。
         */
        SolvePose(controlledTarget);
        ApplyPoseImmediately();

        SetLimbBodyType(
            RigidbodyType2D.Dynamic
        );

        ClearLimbVelocity();

        /*
         * 最后恢复物理连接。
         */
        SetPhysicalConnectionsEnabled(true);
    }

    public void SetControlStrength(
        float normalizedStamina
    )
    {
        staminaControl =
            Mathf.Clamp01(
                normalizedStamina
            );
    }

    private void SolvePose(
        Vector2 requestedEndPosition
    )
    {
        Vector2 root =
            rootPoint.position;

        Vector2 rootToTarget =
            requestedEndPosition - root;

        float requestedDistance =
            rootToTarget.magnitude;

        Vector2 direction;

        if (requestedDistance < 0.0001f)
        {
            direction =
                Vector2.right;
        }
        else
        {
            direction =
                rootToTarget
                / requestedDistance;
        }

        float maximumReach =
            firstLength
            + secondLength;

        float minimumReach =
            Mathf.Abs(
                firstLength
                - secondLength
            );

        float safeMinimumReach =
            Mathf.Max(
                minimumReach,
                0.0001f
            );

        float distance =
            Mathf.Clamp(
                requestedDistance,
                safeMinimumReach,
                maximumReach
            );

        Vector2 solvedEnd =
            root
            + direction * distance;

        float distanceAlongDirection =
            (
                firstLength * firstLength
                - secondLength * secondLength
                + distance * distance
            )
            / (2f * distance);

        float heightSquared =
            firstLength * firstLength
            - distanceAlongDirection
            * distanceAlongDirection;

        float perpendicularHeight =
            Mathf.Sqrt(
                Mathf.Max(
                    0f,
                    heightSquared
                )
            );

        Vector2 basePoint =
            root
            + direction
            * distanceAlongDirection;

        Vector2 perpendicular =
            new Vector2(
                -direction.y,
                direction.x
            );

        float bendSign =
            flipBendDirection
            ? -1f
            : 1f;

        Vector2 solvedJoint =
            basePoint
            + perpendicular
            * perpendicularHeight
            * bendSign;

        lastJointPosition =
            solvedJoint;

        lastEndPosition =
            solvedEnd;

        CalculateBonePose(
            root,
            solvedJoint,
            out lastFirstBonePosition,
            out lastFirstBoneRotation
        );

        CalculateBonePose(
            solvedJoint,
            solvedEnd,
            out lastSecondBonePosition,
            out lastSecondBoneRotation
        );
    }

    private void CalculateBonePose(
        Vector2 start,
        Vector2 end,
        out Vector2 position,
        out float rotation
    )
    {
        Vector2 direction =
            end - start;

        position =
            (start + end) * 0.5f;

        rotation =
            Mathf.Atan2(
                direction.y,
                direction.x
            )
            * Mathf.Rad2Deg;
    }

    /// <summary>
    /// 拖动期间使用。
    /// Kinematic Rigidbody使用MovePosition/MoveRotation。
    /// </summary>
    private void ApplyPoseKinematically()
    {
        jointBody.MovePosition(
            lastJointPosition
        );

        endBody.MovePosition(
            lastEndPosition
        );

        firstBoneBody.MovePosition(
            lastFirstBonePosition
        );

        firstBoneBody.MoveRotation(
            lastFirstBoneRotation
        );

        secondBoneBody.MovePosition(
            lastSecondBonePosition
        );

        secondBoneBody.MoveRotation(
            lastSecondBoneRotation
        );
    }

    /// <summary>
    /// 状态切换时立即同步姿势。
    /// </summary>
    private void ApplyPoseImmediately()
    {
        jointBody.position =
            lastJointPosition;

        endBody.position =
            lastEndPosition;

        firstBoneBody.position =
            lastFirstBonePosition;

        firstBoneBody.rotation =
            lastFirstBoneRotation;

        secondBoneBody.position =
            lastSecondBonePosition;

        secondBoneBody.rotation =
            lastSecondBoneRotation;
    }

    private void SetLimbBodyType(
        RigidbodyType2D bodyType
    )
    {
        jointBody.bodyType =
            bodyType;

        endBody.bodyType =
            bodyType;

        firstBoneBody.bodyType =
            bodyType;

        secondBoneBody.bodyType =
            bodyType;
    }

    private void SetPhysicalConnectionsEnabled(
        bool value
    )
    {
        if (rootToFirstJoint != null)
        {
            rootToFirstJoint.enabled =
                value;
        }

        if (jointToFirstJoint != null)
        {
            jointToFirstJoint.enabled =
                value;
        }

        if (jointToSecondJoint != null)
        {
            jointToSecondJoint.enabled =
                value;
        }

        if (endToSecondJoint != null)
        {
            endToSecondJoint.enabled =
                value;
        }
    }

    private void ClearLimbVelocity()
    {
        jointBody.linearVelocity =
            Vector2.zero;

        jointBody.angularVelocity =
            0f;

        endBody.linearVelocity =
            Vector2.zero;

        endBody.angularVelocity =
            0f;

        firstBoneBody.linearVelocity =
            Vector2.zero;

        firstBoneBody.angularVelocity =
            0f;

        secondBoneBody.linearVelocity =
            Vector2.zero;

        secondBoneBody.angularVelocity =
            0f;
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (isDragging)
        {
            EndDrag();
        }
    }
}