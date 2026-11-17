using UnityEngine;

/// <summary>
/// 编辑模式下的两段式 IK。
/// 保持两根骨骼长度固定，根据末端手脚的位置自动计算肘或膝。
/// Play 模式中停止工作，让 Rigidbody2D 和 HingeJoint2D 接管。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class EditModeLimbIK2D : MonoBehaviour
{
    [Header("三个身体节点")]

    [Tooltip("肩膀或胯部节点")]
    [SerializeField] private Transform rootPoint;

    [Tooltip("肘部或膝盖节点")]
    [SerializeField] private Transform jointPoint;

    [Tooltip("手部或脚部节点")]
    [SerializeField] private Transform endPoint;

    [Header("两根物理骨骼")]

    [Tooltip("肩到肘，或胯到膝")]
    [SerializeField] private Transform firstBone;

    [Tooltip("肘到手，或膝到脚")]
    [SerializeField] private Transform secondBone;

    [Header("固定长度")]

    [Tooltip("第一根骨骼长度")]
    [Min(0.01f)]
    [SerializeField] private float firstLength = 2f;

    [Tooltip("第二根骨骼长度")]
    [Min(0.01f)]
    [SerializeField] private float secondLength = 2f;

    [Header("弯曲方向")]

    [Tooltip("切换肘或膝向另一侧弯曲")]
    [SerializeField] private bool flipBendDirection;

    

    [Header("编辑辅助")]

    [Tooltip("关闭后脚本暂时不调整这个肢体")]
    [SerializeField] private bool enableEditorIK = true;

    private void Update()
    {
        // 运行游戏时完全交给物理系统
        if (Application.isPlaying || !enableEditorIK)
        {
            return;
        }

        SolveIK();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying && enableEditorIK)
        {
            SolveIK();
        }
    }

    /// <summary>
    /// 用当前节点间距记录骨骼长度。
    /// 初次设置时从组件菜单调用一次。
    /// </summary>
    [ContextMenu("Capture Current Lengths")]
    private void CaptureCurrentLengths()
    {
        if (rootPoint == null ||
            jointPoint == null ||
            endPoint == null)
        {
            Debug.LogWarning(
                $"{name}: 请先指定 Root、Joint 和 End。"
            );

            return;
        }

        firstLength = Vector2.Distance(
            rootPoint.position,
            jointPoint.position
        );

        secondLength = Vector2.Distance(
            jointPoint.position,
            endPoint.position
        );

        Debug.Log(
            $"{name}: 已记录长度 " +
            $"L1 = {firstLength:F3}, " +
            $"L2 = {secondLength:F3}"
        );

        SolveIK();
    }

    private void SolveIK()
    {
        if (rootPoint == null ||
            jointPoint == null ||
            endPoint == null ||
            firstBone == null ||
            secondBone == null)
        {
            return;
        }

        Vector2 rootPosition = rootPoint.position;
        Vector2 desiredEndPosition = endPoint.position;

        Vector2 rootToEnd =
            desiredEndPosition - rootPosition;

        float requestedDistance =
            rootToEnd.magnitude;

        // 两根骨骼允许的最大与最小距离
        float maximumReach =
            firstLength + secondLength;

        float minimumReach =
             Mathf.Abs(firstLength - secondLength);

        float safeMinimumReach =
             Mathf.Max(minimumReach, 0.0001f);


        Vector2 direction;

        if (requestedDistance < 0.0001f)
        {
            // 手脚和根节点完全重合时给一个默认方向
            direction = Vector2.right;
        }
        else
        {
            direction = rootToEnd / requestedDistance;
        }

        float clampedDistance = Mathf.Clamp(
            requestedDistance,
            safeMinimumReach,
            maximumReach
        );

        // 如果手脚被拖到了不可到达的位置，
        // 把它限制回固定臂长/腿长允许的范围。
        Vector2 reachableEndPosition =
            rootPosition + direction * clampedDistance;

        endPoint.position = new Vector3(
            reachableEndPosition.x,
            reachableEndPosition.y,
            endPoint.position.z
        );

        /*
         * 使用两个圆相交的方法计算肘/膝：
         *
         * 圆1：以 Root 为中心，半径 firstLength
         * 圆2：以 End 为中心，半径 secondLength
         *
         * 两圆交点就是关节可能的位置。
         */

        float distanceAlongDirection =
            (
                firstLength * firstLength
                - secondLength * secondLength
                + clampedDistance * clampedDistance
            )
            / (2f * clampedDistance);

        float heightSquared =
            firstLength * firstLength
            - distanceAlongDirection
            * distanceAlongDirection;

        float perpendicularHeight =
            Mathf.Sqrt(Mathf.Max(0f, heightSquared));

        Vector2 basePoint =
            rootPosition
            + direction * distanceAlongDirection;

        Vector2 perpendicular =
            new Vector2(-direction.y, direction.x);

        float bendSign =
            flipBendDirection ? -1f : 1f;

        Vector2 calculatedJointPosition =
            basePoint
            + perpendicular
            * perpendicularHeight
            * bendSign;

        jointPoint.position = new Vector3(
            calculatedJointPosition.x,
            calculatedJointPosition.y,
            jointPoint.position.z
        );

        // 只移动和旋转骨骼，不修改 Scale
        PositionBoneBetween(
            firstBone,
            rootPosition,
            calculatedJointPosition
        );

        PositionBoneBetween(
            secondBone,
            calculatedJointPosition,
            reachableEndPosition
        );
    }

    private static void PositionBoneBetween(
        Transform bone,
        Vector2 startPosition,
        Vector2 endPosition
    )
    {
        Vector2 direction =
            endPosition - startPosition;

        if (direction.sqrMagnitude < 0.000001f)
        {
            return;
        }

        Vector2 middlePosition =
            (startPosition + endPosition) * 0.5f;

        bone.position = new Vector3(
            middlePosition.x,
            middlePosition.y,
            bone.position.z
        );

        float angle =
            Mathf.Atan2(direction.y, direction.x)
            * Mathf.Rad2Deg;

        bone.rotation =
            Quaternion.Euler(0f, 0f, angle);

        // 故意不修改 bone.localScale
    }
}