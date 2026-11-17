using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 控制一个Hold最多可以同时被多少个手/脚抓住。
///
/// Max Grip Count = 1
/// → 同时只能有一个肢体抓住。
///
/// Max Grip Count = 2
/// → 可以同时有两个肢体抓住。
/// </summary>
[DisallowMultipleComponent]
public class HoldGripCapacity2D : MonoBehaviour
{
    [Header("抓取容量")]

    [Tooltip("这个岩点最多允许多少个手/脚同时抓住")]
    [Min(1)]
    [SerializeField]
    private int maxGripCount = 1;


    [Header("运行时调试")]

    [SerializeField]
    private int currentGripCount;


    private readonly HashSet<PhysicsDraggable2D>
        occupants =
            new HashSet<PhysicsDraggable2D>();


    public int MaxGripCount
    {
        get { return maxGripCount; }
    }

    public int CurrentGripCount
    {
        get
        {
            CleanupInvalidOccupants();

            return occupants.Count;
        }
    }

    public bool HasFreeSlot
    {
        get
        {
            CleanupInvalidOccupants();

            return occupants.Count
                < maxGripCount;
        }
    }


    /// <summary>
    /// 尝试给某只手/脚占用一个抓取位置。
    ///
    /// true  = 成功
    /// false = 岩点已经满了
    /// </summary>
    public bool TryOccupy(
        PhysicsDraggable2D limb
    )
    {
        if (limb == null)
        {
            return false;
        }

        CleanupInvalidOccupants();

        // 已经登记过，不重复占位置。
        if (occupants.Contains(limb))
        {
            return true;
        }

        // 已达到容量。
        if (occupants.Count >= maxGripCount)
        {
            UpdateDebugCount();

            return false;
        }

        occupants.Add(limb);

        UpdateDebugCount();

        return true;
    }


    /// <summary>
    /// 手脚松开这个Hold时释放容量。
    /// </summary>
    public void Release(
        PhysicsDraggable2D limb
    )
    {
        if (limb == null)
        {
            return;
        }

        occupants.Remove(limb);

        CleanupInvalidOccupants();

        UpdateDebugCount();
    }


    public bool Contains(
        PhysicsDraggable2D limb
    )
    {
        CleanupInvalidOccupants();

        return occupants.Contains(limb);
    }


    private void CleanupInvalidOccupants()
    {
        occupants.RemoveWhere(
            limb => limb == null
        );

        UpdateDebugCount();
    }


    private void UpdateDebugCount()
    {
        currentGripCount =
            occupants.Count;
    }


    private void OnDisable()
    {
        occupants.Clear();

        currentGripCount = 0;
    }


    private void OnValidate()
    {
        maxGripCount =
            Mathf.Max(
                1,
                maxGripCount
            );
    }
}