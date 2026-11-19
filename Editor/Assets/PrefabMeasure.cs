using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 프리팹 에셋의 크기를 잰다. 씬에 꺼내지 않은 프리팹은 Renderer.bounds가 비어 있으므로
    /// 메시 자체의 bounds를 루트 기준 좌표로 옮겨 계산한다.
    /// </summary>
    public static class PrefabMeasure
    {
        /// <summary>루트 피벗(바닥 중심)에서 메시 윗면까지의 높이. 메시가 하나도 없으면 false.</summary>
        public static bool TryGetTopHeight(GameObject prefab, out float top)
        {
            top = float.NegativeInfinity;
            if (prefab == null) return false;

            var toRoot = prefab.transform.worldToLocalMatrix;
            bool found = false;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer switch
                {
                    SkinnedMeshRenderer skinned => skinned.sharedMesh,
                    MeshRenderer => renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null,
                    _ => null,
                };
                if (mesh == null) continue;

                var matrix = toRoot * renderer.transform.localToWorldMatrix;
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    top = Mathf.Max(top, matrix.MultiplyPoint3x4(corner).y);
                }
                found = true;
            }
            return found;
        }
    }
}
