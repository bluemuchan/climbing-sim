using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;


[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public class ClimbingLevelController2D
    : MonoBehaviour
{
    // =========================================================
    // 玩家
    // =========================================================

    [Header("玩家肢体")]

    [SerializeField]
    private PhysicsDraggable2D leftHand;

    [SerializeField]
    private PhysicsDraggable2D rightHand;

    [SerializeField]
    private PhysicsDraggable2D leftFoot;

    [SerializeField]
    private PhysicsDraggable2D rightFoot;


    // =========================================================
    // 系统
    // =========================================================

    [Header("玩家系统")]

    [SerializeField]
    private ClimberStaminaSystem2D staminaSystem;

    [SerializeField]
    private BodyForceController2D bodyForceController;


    // =========================================================
    // Reset
    // =========================================================

    [Header("Restart需要恢复的对象")]

    [Tooltip(
        "把 Bones 和 Balls 根对象拖进来。" +
        "系统会自动保存它们下面所有Rigidbody2D的初始位置。"
    )]
    [SerializeField]
    private Transform[] resetRoots;


    [Header("重开设置")]

    [SerializeField]
    private bool allowRKeyRestart = true;


    // =========================================================
    // Finish
    // =========================================================

    [Header("Finish")]

    [Tooltip("完成关卡后冻结角色")]
    [SerializeField]
    private bool freezePlayerOnFinish = true;

    [SerializeField]
    private UnityEvent onLevelCompleted;


    // =========================================================
    // Runtime
    // =========================================================

    [Header("运行时")]

    [SerializeField]
    private bool levelCompleted;


    public bool LevelCompleted
    {
        get { return levelCompleted; }
    }


    // =========================================================
    // 初始物理状态
    // =========================================================

    private class BodyStartState
    {
        public Rigidbody2D body;

        public Vector2 position;

        public float rotation;

        public RigidbodyType2D bodyType;

        public RigidbodyConstraints2D constraints;

        public bool simulated;
    }


    private readonly List<BodyStartState>
        initialBodyStates =
            new List<BodyStartState>();


    private HoldLevelPoint2D[]
        levelPoints;


    // =========================================================
    // Awake
    // =========================================================

    private void Awake()
    {
        CaptureInitialPlayerPose();

        levelPoints =
            FindObjectsByType<HoldLevelPoint2D>();
    }


    // =========================================================
    // R Restart
    // =========================================================

    private void Update()
    {
        if (!allowRKeyRestart)
        {
            return;
        }

        if (Keyboard.current == null)
        {
            return;
        }


        if (Keyboard.current.rKey
            .wasPressedThisFrame)
        {
            RestartLevel();
        }
    }


    // =========================================================
    // 保存初始姿势
    // =========================================================

    private void CaptureInitialPlayerPose()
    {
        initialBodyStates.Clear();


        HashSet<Rigidbody2D> foundBodies =
            new HashSet<Rigidbody2D>();


        foreach (
            Transform root
            in resetRoots
        )
        {
            if (root == null)
            {
                continue;
            }


            Rigidbody2D[] bodies =
                root.GetComponentsInChildren<
                    Rigidbody2D
                >(true);


            foreach (
                Rigidbody2D body
                in bodies
            )
            {
                if (body == null ||
                    foundBodies.Contains(body))
                {
                    continue;
                }


                foundBodies.Add(body);


                BodyStartState state =
                    new BodyStartState();


                state.body =
                    body;

                state.position =
                    body.position;

                state.rotation =
                    body.rotation;

                state.bodyType =
                    body.bodyType;

                state.constraints =
                    body.constraints;

                state.simulated =
                    body.simulated;


                initialBodyStates.Add(
                    state
                );
            }
        }


        Debug.Log(
            $"记录了 {initialBodyStates.Count} " +
            $"个 Rigidbody2D 的初始状态。"
        );
    }


    // =========================================================
    // Restart
    // =========================================================

    [ContextMenu("Restart Level")]
    public void RestartLevel()
    {
        levelCompleted =
            false;


        // -----------------------------------------------------
        // 暂时关闭WASD
        // -----------------------------------------------------

        if (bodyForceController != null)
        {
            bodyForceController.enabled =
                false;
        }


        // -----------------------------------------------------
        // 先结束所有拖动 / 抓取
        // -----------------------------------------------------

        PrepareLimbForRestart(
            leftHand
        );

        PrepareLimbForRestart(
            rightHand
        );

        PrepareLimbForRestart(
            leftFoot
        );

        PrepareLimbForRestart(
            rightFoot
        );


        // -----------------------------------------------------
        // 恢复整个角色初始物理姿势
        // -----------------------------------------------------

        foreach (
            BodyStartState state
            in initialBodyStates
        )
        {
            if (state.body == null)
            {
                continue;
            }


            state.body.simulated =
                state.simulated;

            state.body.bodyType =
                state.bodyType;

            state.body.constraints =
                state.constraints;


            state.body.position =
                state.position;

            state.body.rotation =
                state.rotation;


            state.body.linearVelocity =
                Vector2.zero;

            state.body.angularVelocity =
                0f;
        }


        Physics2D.SyncTransforms();


        // -----------------------------------------------------
        // 重置体力
        // -----------------------------------------------------

        if (staminaSystem != null)
        {
            /*
             * 这里会：
             *
             * 恢复100%体力
             * 恢复四肢Interaction
             * Reset COM tracking
             */
            staminaSystem.ResetStamina();
        }


        Physics2D.SyncTransforms();


        // -----------------------------------------------------
        // 根据Start Hold重新抓取
        // -----------------------------------------------------

        AttachStartPoints();


        // -----------------------------------------------------
        // 恢复WASD
        // -----------------------------------------------------

        if (bodyForceController != null)
        {
            bodyForceController.enabled =
                true;
        }


        Debug.Log(
            "关卡已重新开始。"
        );
    }


    private void PrepareLimbForRestart(
        PhysicsDraggable2D limb
    )
    {
        if (limb == null)
        {
            return;
        }


        limb.ResetForNewAttempt();
    }


    // =========================================================
    // Start Point
    // =========================================================

    private void AttachStartPoints()
    {
        /*
         * 允许运行时增加/删除关卡点，
         * 所以Restart时重新搜索一次。
         */

        levelPoints =
           FindObjectsByType<HoldLevelPoint2D>();


        foreach (
            HoldLevelPoint2D point
            in levelPoints
        )
        {
            if (point == null)
            {
                continue;
            }


            if (point.Role !=
                HoldLevelPoint2D
                    .PointRole
                    .Start)
            {
                continue;
            }


            Collider2D holdCollider =
                point.PointCollider;


            if (holdCollider == null)
            {
                continue;
            }


            HoldLevelPoint2D.StartLimb limbs =
                point.StartLimbs;


            // LEFT HAND
            if ((limbs &
                 HoldLevelPoint2D
                    .StartLimb
                    .LeftHand) != 0)
            {
                AttachStartLimb(
                    leftHand,
                    holdCollider,
                    "Left Hand"
                );
            }


            // RIGHT HAND
            if ((limbs &
                 HoldLevelPoint2D
                    .StartLimb
                    .RightHand) != 0)
            {
                AttachStartLimb(
                    rightHand,
                    holdCollider,
                    "Right Hand"
                );
            }


            // LEFT FOOT
            if ((limbs &
                 HoldLevelPoint2D
                    .StartLimb
                    .LeftFoot) != 0)
            {
                AttachStartLimb(
                    leftFoot,
                    holdCollider,
                    "Left Foot"
                );
            }


            // RIGHT FOOT
            if ((limbs &
                 HoldLevelPoint2D
                    .StartLimb
                    .RightFoot) != 0)
            {
                AttachStartLimb(
                    rightFoot,
                    holdCollider,
                    "Right Foot"
                );
            }
        }
    }


    private void AttachStartLimb(
        PhysicsDraggable2D limb,
        Collider2D holdCollider,
        string displayName
    )
    {
        if (limb == null)
        {
            return;
        }


        /*
         * 如果一个肢体被错误地配置到了
         * 两个Start Hold，
         * 第一个成功后跳过第二个。
         */
        if (limb.IsAttachedToHold)
        {
            Debug.LogWarning(
                $"{displayName} 被配置到了多个Start Hold。"
            );

            return;
        }


        bool success =
            limb.TryAttachToSpecificHold(
                holdCollider
            );


        if (!success)
        {
            Debug.LogWarning(
                $"{displayName} 无法抓住Start Hold：" +
                $"{holdCollider.name}"
            );
        }
    }


    // =========================================================
    // Finish
    // =========================================================

    public void CompleteLevel(
        HoldLevelPoint2D finishPoint,
        PhysicsDraggable2D touchingLimb
    )
    {
        if (levelCompleted)
        {
            return;
        }


        levelCompleted =
            true;


        Debug.Log(
            $"关卡完成！" +
            $"{touchingLimb.name} " +
            $"碰到了 Finish：{finishPoint.name}"
        );


        // -----------------------------------------------------
        // 停止控制
        // -----------------------------------------------------

        if (bodyForceController != null)
        {
            bodyForceController.enabled =
                false;
        }


        LockLimbInput(
     leftHand
 );

        LockLimbInput(
            rightHand
        );

        LockLimbInput(
            leftFoot
        );

        LockLimbInput(
            rightFoot
        );



        // -----------------------------------------------------
        // 冻结角色
        // -----------------------------------------------------

        if (freezePlayerOnFinish)
        {
            FreezePlayer();
        }


        // -----------------------------------------------------
        // 给以后UI / 下一关使用
        // -----------------------------------------------------

        onLevelCompleted?.Invoke();
    }


    private void SetLimbInteraction(
        PhysicsDraggable2D limb,
        bool value
    )
    {
        if (limb != null)
        {
            limb.SetInteractionAllowed(
                value
            );
        }
    }


    private void FreezePlayer()
    {
        foreach (
            BodyStartState state
            in initialBodyStates
        )
        {
            if (state.body == null)
            {
                continue;
            }


            state.body.linearVelocity =
                Vector2.zero;

            state.body.angularVelocity =
                0f;

            state.body.constraints =
                RigidbodyConstraints2D
                    .FreezeAll;
        }
    }

    private void LockLimbInput(
    PhysicsDraggable2D limb
)
    {
        if (limb == null)
        {
            return;
        }

        limb.SetInputAllowedWithoutDetaching(
            false
        );
    }
}