using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class HoldLevelPoint2D : MonoBehaviour
{
    public enum PointRole
    {
        None,
        Start,
        Finish
    }

    [System.Flags]
    public enum StartLimb
    {
        None = 0,

        LeftHand = 1 << 0,
        RightHand = 1 << 1,

        LeftFoot = 1 << 2,
        RightFoot = 1 << 3
    }


    [Header("关卡属性")]

    [SerializeField]
    private PointRole role =
        PointRole.None;


    [Header("Start 设置")]

    [Tooltip(
        "只有 Role = Start 时使用。" +
        "可以同时选择多个肢体。"
    )]
    [SerializeField]
    private StartLimb startLimbs =
        StartLimb.None;


    [Header("运行时")]

    [SerializeField]
    private ClimbingLevelController2D levelController;


    private Collider2D pointCollider;


    public PointRole Role
    {
        get { return role; }
    }

    public StartLimb StartLimbs
    {
        get { return startLimbs; }
    }

    public Collider2D PointCollider
    {
        get { return pointCollider; }
    }


    private void Awake()
    {
        pointCollider =
            GetComponent<Collider2D>();

        if (levelController == null)
        {
            levelController =
               FindAnyObjectByType<ClimbingLevelController2D
                >();
        }
    }


    // =========================================================
    // Finish
    // =========================================================

    // =========================================================
    // 有肢体真正抓住这个点
    // =========================================================

    public void OnLimbAttached(
        PhysicsDraggable2D limb
    )
    {
        if (role != PointRole.Finish)
        {
            return;
        }

        if (limb == null)
        {
            return;
        }

        if (levelController == null)
        {
            levelController =
                FindAnyObjectByType<ClimbingLevelController2D
                >();
        }

        if (levelController == null)
        {
            Debug.LogWarning(
                $"{name}: 找不到 ClimbingLevelController2D。"
            );

            return;
        }


        Debug.Log(
            $"{limb.name} 成功抓住 Finish：{name}"
        );


        levelController.CompleteLevel(
            this,
            limb
        );
    }


    private void OnValidate()
    {
        if (role != PointRole.Start)
        {
            startLimbs =
                StartLimb.None;
        }
    }
}