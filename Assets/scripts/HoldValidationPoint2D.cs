using UnityEngine;

/// <summary>
/// 可选组件。
///
/// 如果一个Hold需要指定Validator使用的代表抓取点，
/// 在Hold下面创建一个子物体并添加这个组件。
///
/// 如果不存在这个组件，Validator默认使用Collider.bounds.center。
///
/// 以后可以升级成多个Sampling Point。
/// </summary>
[DisallowMultipleComponent]
public class HoldValidationPoint2D : MonoBehaviour
{
}