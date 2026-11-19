using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>MapData를 담는 에셋. 맵 저장 단위다. 편집은 Map Tool 창에서만 한다.</summary>
    [CreateAssetMenu(menuName = "Map Tool/Map", fileName = "Map")]
    public sealed class MapAsset : ScriptableObject
    {
        [SerializeField] private MapData data = new(new Vector3Int(20, 4, 20), GridSpace.Unit);

        public MapData Data => data;

        internal void Initialize(Vector3Int size, GridSpace grid) => data = new MapData(size, grid);

        /// <summary>새 에셋을 JSON에서 만들 때만 쓴다. 열려 있는 맵의 내용은 ReplaceMapCommand로 바꿔야 씬·Undo와 맞는다.</summary>
        internal void SetData(MapData imported) => data = imported ?? throw new System.ArgumentNullException(nameof(imported));
    }

    /// <summary>
    /// 인스펙터에서 MapData를 직접 고치면 점유 맵·씬과 어긋나므로 요약만 보여 주고 창으로 보낸다.
    /// </summary>
    [CustomEditor(typeof(MapAsset))]
    internal sealed class MapAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var map = (MapAsset)target;
            var data = map.Data;
            EditorGUILayout.LabelField("크기 (X, 층, Z)", data.Size.ToString());
            EditorGUILayout.LabelField("셀 크기", data.Grid.CellSize.ToString());
            EditorGUILayout.LabelField("오브젝트 수", data.Count.ToString());
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("맵은 Map Tool 창에서 편집합니다. 데이터를 직접 고치면 씬과 어긋날 수 있습니다.", MessageType.Info);
            if (GUILayout.Button("Map Tool 창에서 열기")) MapToolWindow.Open(map);
        }
    }
}
