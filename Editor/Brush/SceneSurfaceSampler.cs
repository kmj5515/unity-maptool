using MapTool.Core;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 브러시 지면 찾기. 씬 콜라이더에 레이캐스트하고(트리거 무시), 맞는 게 없으면 맵 바닥 평면(원점 Y)을 지면으로 쓴다.
    /// exclude 아래 콜라이더(맵이 만든 장식 인스턴스)는 무시해서 풀 위에 풀이 쌓이지 않게 한다.
    /// </summary>
    internal sealed class SceneSurfaceSampler
    {
        /// <summary>맵 꼭대기보다 이만큼 위에서 아래로 쏜다.</summary>
        private const float TopPadding = 1f;
        /// <summary>맵 바닥보다 이만큼 아래까지 찾는다(바닥보다 낮은 지형).</summary>
        private const float BottomDepth = 100f;
        private const float PickDistance = 10000f;

        private readonly float baseY;
        private readonly float topY;
        private readonly Transform exclude;
        private readonly RaycastHit[] hits = new RaycastHit[64];

        public SceneSurfaceSampler(float baseY, float topY, Transform exclude)
        {
            this.baseY = baseY;
            this.topY = Mathf.Max(baseY, topY);
            this.exclude = exclude;
        }

        /// <summary>맵 높이 범위와 장식 컨테이너로 만든다. 콜라이더 위치를 최신으로 맞춘 뒤 쓴다.</summary>
        public static SceneSurfaceSampler ForMap(PlacementService service, Transform root)
        {
            Physics.SyncTransforms();
            var grid = service.Map.Grid;
            float top = grid.Origin.y + service.Map.Size.y * grid.CellSize.y;
            var scatters = root != null ? root.Find(MapSceneView.ScattersChildName) : null;
            return new SceneSurfaceSampler(grid.Origin.y, top, scatters);
        }

        /// <summary>코어 Generate용 대리자 모양. 위에서 아래로 쏴서 지면을 찾는다. 바닥 평면이 있어 항상 true.</summary>
        public bool Sample(Vector2 worldXZ, out Vector3 worldPoint, out Vector3 normal)
        {
            var origin = new Vector3(worldXZ.x, topY + TopPadding, worldXZ.y);
            float distance = topY - baseY + TopPadding + BottomDepth;
            if (TryNearest(new Ray(origin, Vector3.down), distance, out var hit))
            {
                worldPoint = hit.point;
                normal = hit.normal;
                return true;
            }

            worldPoint = new Vector3(worldXZ.x, baseY, worldXZ.y);
            normal = Vector3.up;
            return true;
        }

        /// <summary>마우스 광선이 닿는 지면. 콜라이더가 없으면 바닥 평면, 평면도 못 맞히면(하늘을 봄) false.</summary>
        public bool Pick(Ray ray, out Vector3 worldPoint, out Vector3 normal)
        {
            if (TryNearest(ray, PickDistance, out var hit))
            {
                worldPoint = hit.point;
                normal = hit.normal;
                return true;
            }

            var plane = new Plane(Vector3.up, new Vector3(0f, baseY, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                worldPoint = ray.GetPoint(enter);
                normal = Vector3.up;
                return true;
            }

            worldPoint = default;
            normal = Vector3.up;
            return false;
        }

        private bool TryNearest(Ray ray, float maxDistance, out RaycastHit nearest)
        {
            int count = Physics.RaycastNonAlloc(ray, hits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            nearest = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (exclude != null && hit.transform.IsChildOf(exclude)) continue;
                if (found && hit.distance >= nearest.distance) continue;
                nearest = hit;
                found = true;
            }
            return found;
        }
    }
}
