using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 셀 → 그 셀을 점유한 오브젝트 ID. MapData에서 파생되는 색인이라 저장하지 않는다.
    /// 수정은 PlacementService만 한다.
    /// </summary>
    public sealed class OccupancyMap
    {
        private readonly Dictionary<Vector3Int, int> cells = new();

        public int OccupiedCellCount => cells.Count;

        public bool TryGetOccupant(Vector3Int cell, out int instanceId) => cells.TryGetValue(cell, out instanceId);

        public bool IsOccupied(Vector3Int cell) => cells.ContainsKey(cell);

        internal void Fill(BoundsInt bounds, int instanceId)
        {
            foreach (var cell in bounds.allPositionsWithin) cells[cell] = instanceId;
        }

        internal void Clear(BoundsInt bounds, int instanceId)
        {
            foreach (var cell in bounds.allPositionsWithin)
            {
                // 데이터가 이미 겹쳐 있던 경우 다른 오브젝트의 점유를 지우지 않는다.
                if (cells.TryGetValue(cell, out var occupant) && occupant == instanceId) cells.Remove(cell);
            }
        }

        internal void ClearAll() => cells.Clear();
    }
}
