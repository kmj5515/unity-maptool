using MapTool.Core;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 팔레트에 나오는 오브젝트 하나. 코어의 ObjectDefinition(ID, 점유 크기, 종류)에 프리팹과 카테고리를 더한 에디터용 에셋.
    /// 프리팹 피벗은 R0 기준 점유 영역의 바닥 중심에 둔다. 장식(Scatter)이면 피벗을 바닥 중심에 두고 점유 크기는 쓰지 않는다.
    /// </summary>
    [CreateAssetMenu(menuName = "Map Tool/Object Definition", fileName = "MapObject")]
    public sealed class MapObjectDefinition : ScriptableObject
    {
        [Tooltip("맵 데이터에 저장되는 ID. 비워 두면 에셋 이름을 쓴다. 배치한 뒤에 바꾸면 기존 배치가 끊긴다.")]
        [SerializeField] private string id;

        [Tooltip("칸 오브젝트 = 셀에 맞춰 놓고 칸을 차지(건물·울타리). 장식 = 브러시로 흩뿌리고 칸을 차지하지 않음(풀·꽃·돌)")]
        [SerializeField] private PlacementKind kind = PlacementKind.Grid;

        [Tooltip("회전하지 않았을 때 점유하는 셀 수 (X, 층, Z). 장식이면 쓰지 않는다")]
        [SerializeField] private Vector3Int size = Vector3Int.one;

        [SerializeField] private GameObject prefab;
        [SerializeField] private string category = "기타";

        public string Id => id;
        public PlacementKind Kind => kind;
        public bool IsScatter => kind == PlacementKind.Scatter;
        public Vector3Int Size => size;
        public GameObject Prefab => prefab;
        public string Category => string.IsNullOrEmpty(category) ? "기타" : category;

        /// <summary>
        /// 바닥 오브젝트로 보는 최대 높이(m). 이 높이 이하면 플레이어가 올라설 수 있어 스폰을 막지 않는다.
        /// CharacterController 기본 계단 높이(stepOffset)와 같다.
        /// </summary>
        public const float FloorMaxHeight = 0.3f;

        /// <summary>프리팹 메시 윗면이 <see cref="FloorMaxHeight"/> 이하면 바닥(길·바닥 타일)이다. 프리팹이나 메시가 없으면 아니다.</summary>
        public bool IsFloor => !IsScatter && PrefabMeasure.TryGetTopHeight(prefab, out float top) && top <= FloorMaxHeight;

        public ObjectDefinition ToCoreDefinition() =>
            IsScatter
                ? ObjectDefinition.Scatter(id)
                : new ObjectDefinition(id, new Vector3Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y), Mathf.Max(1, size.z)),
                    PlacementKind.Grid, IsFloor);

        internal void Configure(string newId, Vector3Int newSize, GameObject newPrefab, string newCategory,
            PlacementKind newKind = PlacementKind.Grid)
        {
            id = newId;
            size = newSize;
            prefab = newPrefab;
            category = newCategory;
            kind = newKind;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
            size = new Vector3Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y), Mathf.Max(1, size.z));
        }
    }
}
