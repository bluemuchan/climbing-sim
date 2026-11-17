using UnityEngine;

/// <summary>
/// 计算左右手、左右脚、胸部和胯部六个点的平均位置。
/// 同时记录每个物理更新中重心向上移动的距离。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class BodyCenterTracker2D : MonoBehaviour
{
    [Header("四肢节点")]

    [SerializeField]
    private Transform leftHand;

    [SerializeField]
    private Transform rightHand;

    [SerializeField]
    private Transform leftFoot;

    [SerializeField]
    private Transform rightFoot;

    [Header("身体节点")]

    [SerializeField]
    private Transform chest;

    [SerializeField]
    private Transform hip;

    [Header("可视化，可不填写")]

    [Tooltip("用于在游戏中显示重心位置的圆形对象")]
    [SerializeField]
    private Transform centerMarker;

    [Header("运行时调试数据")]

    [SerializeField]
    private Vector2 currentCenter;

    [SerializeField]
    private Vector2 previousCenter;

    [SerializeField]
    private float upwardDistanceThisStep;

    private bool hasPreviousCenter;

    /// <summary>
    /// 当前六个节点的平均位置。
    /// 后面的体力系统会读取它。
    /// </summary>
    public Vector2 CurrentCenter
    {
        get { return currentCenter; }
    }

    /// <summary>
    /// 上一个物理更新中的重心位置。
    /// </summary>
    public Vector2 PreviousCenter
    {
        get { return previousCenter; }
    }

    /// <summary>
    /// 当前物理更新中重心向上移动的距离。
    /// 重心下降或水平移动时，这个值为0。
    /// </summary>
    public float UpwardDistanceThisStep
    {
        get { return upwardDistanceThisStep; }
    }

    /// <summary>
    /// 六个节点是否都已经正确设置。
    /// </summary>
    public bool IsConfigured
    {
        get
        {
            return leftHand != null
                && rightHand != null
                && leftFoot != null
                && rightFoot != null
                && chest != null
                && hip != null;
        }
    }

    private void OnEnable()
    {
        ResetTracking();
    }

    private void FixedUpdate()
    {
        if (!TryCalculateCenter(out Vector2 calculatedCenter))
        {
            upwardDistanceThisStep = 0f;
            return;
        }

        if (!hasPreviousCenter)
        {
            currentCenter = calculatedCenter;
            previousCenter = calculatedCenter;
            upwardDistanceThisStep = 0f;
            hasPreviousCenter = true;
        }
        else
        {
            previousCenter = currentCenter;
            currentCenter = calculatedCenter;

            float verticalChange =
                currentCenter.y - previousCenter.y;

            // 只记录向上的移动。
            // 向下移动或水平移动不会产生体力消耗数据。
            upwardDistanceThisStep =
                Mathf.Max(0f, verticalChange);
        }

        UpdateMarker();
    }

    /// <summary>
    /// 立即计算六个点的平均位置。
    /// </summary>
    public bool TryCalculateCenter(
    out Vector2 center
)
    {
        center = Vector2.zero;

        if (!IsConfigured)
        {
            return false;
        }

        const float limbWeight = 1f;
        const float chestWeight = 1.4f;
        const float hipWeight = 1.4f;

        float totalWeight =
            limbWeight * 4f
            + chestWeight
            + hipWeight;

        Vector2 weightedPosition =
            (Vector2)leftHand.position * limbWeight
            + (Vector2)rightHand.position * limbWeight
            + (Vector2)leftFoot.position * limbWeight
            + (Vector2)rightFoot.position * limbWeight
            + (Vector2)chest.position * chestWeight
            + (Vector2)hip.position * hipWeight;

        center =
            weightedPosition / totalWeight;

        return true;
    }

    /// <summary>
    /// 角色重生、传送或回到起点后调用，
    /// 防止位置突然变化被误认为重心大幅上升。
    /// </summary>
    public void ResetTracking()
    {
        upwardDistanceThisStep = 0f;
        hasPreviousCenter = false;

        if (TryCalculateCenter(out Vector2 calculatedCenter))
        {
            currentCenter = calculatedCenter;
            previousCenter = calculatedCenter;
            hasPreviousCenter = true;
            UpdateMarker();
        }
    }

    private void UpdateMarker()
    {
        if (centerMarker == null)
        {
            return;
        }

        centerMarker.position = new Vector3(
            currentCenter.x,
            currentCenter.y,
            centerMarker.position.z
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryCalculateCenter(out Vector2 calculatedCenter))
        {
            return;
        }

        // 即使没有创建可见 Marker，
        // 选中 PlayerSystems 时也能在 Scene 窗口看到重心。
        Gizmos.DrawWireSphere(calculatedCenter, 0.15f);
    }
}