using UnityEngine;

namespace MapTool.Core
{
    /// <summary>Y축 기준 90도 단위 회전. 위에서 볼 때 시계 방향.</summary>
    public enum GridRotation
    {
        R0 = 0,
        R90 = 1,
        R180 = 2,
        R270 = 3,
    }

    public static class GridRotationExtensions
    {
        public static GridRotation Clockwise(this GridRotation rotation) => (GridRotation)(((int)rotation + 1) & 3);

        public static GridRotation CounterClockwise(this GridRotation rotation) => (GridRotation)(((int)rotation + 3) & 3);

        public static float ToDegrees(this GridRotation rotation) => ((int)rotation & 3) * 90f;

        /// <summary>회전을 적용한 점유 크기. 90도와 270도면 X와 Z가 바뀐다.</summary>
        public static Vector3Int RotateSize(this GridRotation rotation, Vector3Int size) =>
            ((int)rotation & 1) == 1 ? new Vector3Int(size.z, size.y, size.x) : size;
    }
}
