using System;
using System.Collections.Generic;
using MapTool.Core;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 브러시 프리셋 에셋 ("숲 바닥", "길가 꽃" 등). 브러시 설정과 섞을 장식·비율을 담는다.
    /// 장식은 ID가 아니라 정의 에셋 참조로 저장해서 정의 이름이나 ID가 바뀌어도 끊기지 않는다.
    /// 맵툴 창 브러시 패널에서 불러오기·덮어쓰기·새로 저장한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Map Tool/Scatter Brush Preset", fileName = "ScatterBrushPreset")]
    public sealed class ScatterBrushPreset : ScriptableObject
    {
        [SerializeField] private ScatterBrushSettings settings = new();
        [SerializeField] private List<ScatterBrushItem> items = new();

        public ScatterBrushSettings Settings => settings ??= new ScatterBrushSettings();
        public IReadOnlyList<ScatterBrushItem> Items => items ??= new List<ScatterBrushItem>();

        internal void Write(ScatterBrushSettings newSettings, IEnumerable<ScatterBrushItem> newItems)
        {
            settings = newSettings.Clone();
            items = new List<ScatterBrushItem>();
            foreach (var item in newItems) items.Add(item.Clone());
        }

        private void OnValidate() => settings?.Sanitize();
    }

    /// <summary>브러시에 담긴 장식 하나와 비율(상대값).</summary>
    [Serializable]
    public sealed class ScatterBrushItem
    {
        public const float MaxWeight = 10f;
        public const float DefaultWeight = 5f;

        [SerializeField] private MapObjectDefinition definition;
        [Range(0f, MaxWeight)]
        [SerializeField] private float weight = DefaultWeight;

        // 직렬화용
        private ScatterBrushItem() { }

        public ScatterBrushItem(MapObjectDefinition definition, float weight = DefaultWeight)
        {
            this.definition = definition;
            Weight = weight;
        }

        public MapObjectDefinition Definition => definition;

        public float Weight
        {
            get => weight;
            set => weight = Mathf.Clamp(value, 0f, MaxWeight);
        }

        /// <summary>정의가 남아 있고 장식 종류이며 비율이 0보다 커서 실제로 뽑힐 수 있는지.</summary>
        public bool IsUsable => definition != null && definition.IsScatter && !string.IsNullOrEmpty(definition.Id) && weight > 0f;

        public ScatterBrushItem Clone() => new(definition, weight);
    }
}
