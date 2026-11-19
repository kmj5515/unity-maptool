using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 정의 인스펙터. 종류를 이름으로 보여 주고, 장식이면 쓰지 않는 점유 크기를 흐리게 한다.
    /// 칸 오브젝트면 자동 판정된 바닥 여부와 그 근거(프리팹 높이)를 보여 준다.
    /// </summary>
    [CustomEditor(typeof(MapObjectDefinition))]
    [CanEditMultipleObjects]
    internal sealed class MapObjectDefinitionEditor : UnityEditor.Editor
    {
        private static readonly GUIContent[] KindLabels =
        {
            new("칸 오브젝트", "셀에 맞춰 놓고 칸을 차지한다 (건물, 울타리, 상자)"),
            new("장식", "브러시로 흩뿌리고 칸을 차지하지 않는다 (풀, 꽃, 돌)"),
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("id"));

            var kind = serializedObject.FindProperty("kind");
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                EditorGUI.showMixedValue = kind.hasMultipleDifferentValues;
                int picked = EditorGUILayout.Popup(new GUIContent("종류", kind.tooltip), kind.enumValueIndex, KindLabels);
                EditorGUI.showMixedValue = false;
                if (scope.changed) kind.enumValueIndex = picked;
            }

            bool scatter = !kind.hasMultipleDifferentValues && kind.enumValueIndex == (int)PlacementKind.Scatter;
            using (new EditorGUI.DisabledScope(scatter))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("size"), new GUIContent(scatter ? "점유 크기 (장식은 안 씀)" : "점유 크기"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty("prefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("category"));

            serializedObject.ApplyModifiedProperties();

            if (!scatter && !serializedObject.isEditingMultipleObjects) DrawFloorInfo((MapObjectDefinition)target);
        }

        /// <summary>바닥 여부는 프리팹 높이로 자동 판정된다. 왜 스폰이 막히는지/안 막히는지 알 수 있게 근거를 보여 준다.</summary>
        private static void DrawFloorInfo(MapObjectDefinition definition)
        {
            string limit = $"{MapObjectDefinition.FloorMaxHeight:0.##}m";
            string text = !PrefabMeasure.TryGetTopHeight(definition.Prefab, out float top)
                ? "바닥 판정: 메시가 없어 바닥이 아닌 것으로 봅니다."
                : top <= MapObjectDefinition.FloorMaxHeight
                    ? $"바닥 오브젝트 (높이 {top:0.##}m ≤ {limit}) — 위에 스폰을 둬도 막힘 경고가 나지 않습니다."
                    : $"막는 오브젝트 (높이 {top:0.##}m > {limit}) — 이 칸에 스폰을 두면 '끼일 수 있음' 경고가 납니다.";
            EditorGUILayout.HelpBox(text, MessageType.None);
        }
    }
}
