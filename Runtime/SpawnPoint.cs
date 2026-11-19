using System.Collections.Generic;
using UnityEngine;

namespace MapTool
{
    /// <summary>
    /// 맵툴이 씬에 만들어 두는 플레이어 시작 지점. 위치·방향은 이 오브젝트의 Transform이다.
    /// 맵툴의 스폰 데이터에서 동기화되는 결과물이므로 직접 고치지 말고 맵툴 "스폰" 모드에서 고친다.
    /// 게임 코드는 FindDefault()로 시작 지점을 얻는다(PlayerSpawner가 그렇게 쓴다).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPoint : MonoBehaviour
    {
        private static readonly Color NormalColor = new(0.3f, 0.95f, 0.85f);
        private static readonly Color DefaultColor = new(1f, 0.45f, 0.9f);

        [SerializeField] private int id;
        [SerializeField] private string spawnName = string.Empty;
        [SerializeField] private bool isDefault;

        /// <summary>맵 데이터의 스폰 ID.</summary>
        public int Id => id;

        /// <summary>맵툴에서 붙인 이름.</summary>
        public string SpawnName => spawnName ?? string.Empty;

        /// <summary>맵의 기본 스폰인가.</summary>
        public bool IsDefault => isDefault;

        public void Configure(int spawnId, string displayName, bool asDefault)
        {
            id = spawnId;
            spawnName = displayName ?? string.Empty;
            isDefault = asDefault;
        }

        /// <summary>현재 열린 씬들에서 기본 스폰을 찾는다. 없으면 null.</summary>
        public static SpawnPoint FindDefault() =>
            PickDefault(FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

        /// <summary>현재 열린 씬들에서 ID로 찾는다. 없으면 null.</summary>
        public static SpawnPoint Find(int spawnId) =>
            PickById(FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None), spawnId);

        public static SpawnPoint PickById(IEnumerable<SpawnPoint> candidates, int spawnId)
        {
            if (candidates == null) return null;
            foreach (var spawn in candidates)
                if (spawn != null && spawn.id == spawnId) return spawn;
            return null;
        }

        /// <summary>기본으로 표시된 것, 없으면 ID가 가장 작은 것. 후보가 없으면 null.</summary>
        public static SpawnPoint PickDefault(IEnumerable<SpawnPoint> candidates)
        {
            SpawnPoint lowest = null;
            if (candidates == null) return null;
            foreach (var spawn in candidates)
            {
                if (spawn == null) continue;
                if (spawn.isDefault) return spawn;
                if (lowest == null || spawn.id < lowest.id) lowest = spawn;
            }
            return lowest;
        }

        // 맵툴 창이 닫혀 있어도 씬뷰에서 보이도록 간단한 표시를 남긴다.
        private void OnDrawGizmos()
        {
            var t = transform;
            Gizmos.color = isDefault ? DefaultColor : NormalColor;
            Gizmos.DrawWireSphere(t.position + Vector3.up * 0.05f, 0.35f);
            var tip = t.position + Vector3.up * 0.05f + t.forward * 0.6f;
            Gizmos.DrawLine(t.position + Vector3.up * 0.05f, tip);
            Gizmos.DrawLine(tip, tip - t.forward * 0.2f + t.right * 0.15f);
            Gizmos.DrawLine(tip, tip - t.forward * 0.2f - t.right * 0.15f);
        }
    }
}
