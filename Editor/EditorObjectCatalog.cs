using System;
using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 프로젝트의 MapObjectDefinition 에셋을 모은 카탈로그. 코어에는 IObjectCatalog로 넘기고 팔레트는 에셋 목록을 쓴다.
    /// ID가 비었거나 중복된 정의는 경고를 남기고 제외한다.
    /// </summary>
    public sealed class EditorObjectCatalog : IObjectCatalog
    {
        private readonly Dictionary<string, MapObjectDefinition> assets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ObjectDefinition> definitions = new(StringComparer.Ordinal);
        private readonly List<MapObjectDefinition> ordered = new();

        public EditorObjectCatalog(IEnumerable<MapObjectDefinition> items)
        {
            foreach (var item in items)
            {
                if (item == null) continue;
                if (string.IsNullOrEmpty(item.Id))
                {
                    Debug.LogWarning($"[MapTool] '{item.name}'의 ID가 비어 있어 제외합니다.", item);
                    continue;
                }
                if (assets.ContainsKey(item.Id))
                {
                    Debug.LogWarning($"[MapTool] 정의 ID '{item.Id}'가 중복됩니다. '{item.name}'은 제외합니다.", item);
                    continue;
                }

                assets.Add(item.Id, item);
                definitions.Add(item.Id, item.ToCoreDefinition());
                ordered.Add(item);
            }

            ordered.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Category, b.Category);
                return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
            });
            Categories = ordered.Select(d => d.Category).Distinct().ToList();
            Signature = string.Join("|", ordered.Select(d =>
                $"{d.Id}:{d.Kind}:{d.Size}:{definitions[d.Id].IsFloor}:{AssetDatabase.GetAssetPath(d.Prefab)}"));
        }

        /// <summary>카테고리, 이름 순으로 정렬된 정의 목록.</summary>
        public IReadOnlyList<MapObjectDefinition> Definitions => ordered;

        public IReadOnlyList<string> Categories { get; }

        /// <summary>ID·종류·크기·바닥 여부·프리팹이 바뀌었는지 비교하는 용도.</summary>
        public string Signature { get; }

        public static EditorObjectCatalog LoadAll() =>
            new(AssetDatabase.FindAssets($"t:{nameof(MapObjectDefinition)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<MapObjectDefinition>(AssetDatabase.GUIDToAssetPath(guid))));

        public bool TryGet(string definitionId, out ObjectDefinition definition)
        {
            definition = null;
            return definitionId != null && definitions.TryGetValue(definitionId, out definition);
        }

        public bool TryGetAsset(string definitionId, out MapObjectDefinition asset)
        {
            asset = null;
            return definitionId != null && assets.TryGetValue(definitionId, out asset);
        }
    }
}
