using System;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 셀 좌표와 월드 좌표 변환. 셀 (0,0,0)의 최소 모서리가 Origin이다.
    /// Y는 층(높이 칸)이며 셀 높이는 CellSize.y로 따로 정할 수 있다.
    /// </summary>
    [Serializable]
    public struct GridSpace
    {
        /// <summary>경계에 딱 걸친 값이 부동소수 오차로 아래 칸에 떨어지지 않게 더하는 여유(셀 단위).</summary>
        private const float SnapEpsilon = 1e-4f;

        public Vector3 Origin;
        public Vector3 CellSize;

        public GridSpace(Vector3 origin, Vector3 cellSize)
        {
            if (cellSize.x <= 0f || cellSize.y <= 0f || cellSize.z <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "셀 크기는 모두 0보다 커야 합니다.");
            Origin = origin;
            CellSize = cellSize;
        }

        public static GridSpace Unit => new(Vector3.zero, Vector3.one);

        /// <summary>셀의 최소 모서리 월드 좌표.</summary>
        public Vector3 CellToWorld(Vector3Int cell) => Origin + Vector3.Scale(cell, CellSize);

        /// <summary>월드 좌표가 들어 있는 셀.</summary>
        public Vector3Int WorldToCell(Vector3 world)
        {
            var local = world - Origin;
            return new Vector3Int(
                Mathf.FloorToInt(local.x / CellSize.x + SnapEpsilon),
                Mathf.FloorToInt(local.y / CellSize.y + SnapEpsilon),
                Mathf.FloorToInt(local.z / CellSize.z + SnapEpsilon));
        }

        /// <summary>점유 영역 바닥면의 중심 월드 좌표. 프리팹 피벗을 이 위치에 두고 회전시킨다.</summary>
        public Vector3 FootprintBaseCenter(BoundsInt cells)
        {
            var center = cells.center;
            return Origin + Vector3.Scale(new Vector3(center.x, cells.yMin, center.z), CellSize);
        }
    }
}
