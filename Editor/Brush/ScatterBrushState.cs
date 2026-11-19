using System;
using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 맵툴 창이 지금 쓰는 브러시 상태(설정 + 섞을 장식 구성 + 그리기/지우개 모드 + 지우개 대상).
    /// 창의 직렬화 필드라 도메인 리로드·창 다시 열기 뒤에도 남는다. 프리셋과는 별개이고 불러오기·저장할 때만 복사한다.
    /// </summary>
    [Serializable]
    internal sealed class ScatterBrushState
    {
        [SerializeField] private ScatterBrushSettings settings = new();
        [SerializeField] private List<ScatterBrushItem> items = new();
        [SerializeField] private bool eraseAllKinds;
        [SerializeField] private bool eraseMode;

        public ScatterBrushSettings Settings => settings ??= new ScatterBrushSettings();
        public IReadOnlyList<ScatterBrushItem> Items => ItemList;
        private List<ScatterBrushItem> ItemList => items ??= new List<ScatterBrushItem>();

        /// <summary>끄면(기본) 지우개는 구성에 체크한 종류만 지운다. 켜면 장식은 종류를 가리지 않고 지운다.</summary>
        public bool EraseAllKinds
        {
            get => eraseAllKinds;
            set => eraseAllKinds = value;
        }

        /// <summary>켜면 지우개 모드: 그냥 드래그가 지우기, Shift+드래그가 그리기. 끄면(기본) 반대.</summary>
        public bool EraseMode
        {
            get => eraseMode;
            set => eraseMode = value;
        }

        /// <summary>지금 드래그하면 지우는지. Shift는 현재 모드를 잠깐 뒤집는다.</summary>
        public bool WantsErase(bool shift) => eraseMode != shift;

        public bool Contains(MapObjectDefinition definition) => IndexOf(definition) >= 0;

        /// <summary>구성에 없으면 기본 비율로 넣고 true. 이미 있으면 그대로 두고 false.</summary>
        public bool Add(MapObjectDefinition definition)
        {
            if (definition == null || Contains(definition)) return false;
            ItemList.Add(new ScatterBrushItem(definition));
            return true;
        }

        /// <summary>팔레트 타일 클릭: 구성에 없으면 넣고, 있으면 뺀다. 넣었으면 true.</summary>
        public bool Toggle(MapObjectDefinition definition)
        {
            int index = IndexOf(definition);
            if (index < 0) return Add(definition);
            RemoveAt(index);
            return false;
        }

        public void RemoveAt(int index) => ItemList.RemoveAt(index);

        public void Clear() => ItemList.Clear();

        /// <summary>뽑힐 수 있는 항목 중 이 항목이 차지하는 몫(0~1). 뽑힐 수 없으면 0.</summary>
        public float Share(int index)
        {
            var item = Items[index];
            if (!item.IsUsable) return 0f;
            float total = Items.Where(i => i.IsUsable).Sum(i => i.Weight);
            return total > 0f ? item.Weight / total : 0f;
        }

        /// <summary>코어 브러시에 넘길 목록. 정의가 지워졌거나 장식이 아니거나 비율 0인 항목은 뺀다.</summary>
        public List<ScatterEntry> ToEntries() =>
            Items.Where(i => i.IsUsable).Select(i => new ScatterEntry(i.Definition.Id, i.Weight)).ToList();

        public bool HasUsableEntries => Items.Any(i => i.IsUsable);

        /// <summary>지우개 대상 정의 ID. 모든 종류를 지우면 null(ScatterBrush.CollectErase 규약).</summary>
        public HashSet<string> EraseFilter() =>
            EraseAllKinds
                ? null
                : new HashSet<string>(Items.Where(i => i.Definition != null).Select(i => i.Definition.Id), StringComparer.Ordinal);

        // ───────────────────────── 프리셋 ─────────────────────────

        /// <summary>프리셋 값을 복사해 온다. 이후 상태를 바꿔도 프리셋은 그대로다.</summary>
        public void Load(ScatterBrushPreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            settings = preset.Settings.Clone();
            settings.Sanitize();
            items = preset.Items.Where(i => i != null).Select(i => i.Clone()).ToList();
        }

        public void SaveTo(ScatterBrushPreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            preset.Write(Settings, Items);
        }

        /// <summary>프리셋과 설정·구성이 같은지. 다르면 창에 "수정됨"을 표시한다. 그리기/지우개 모드와 지우개 대상은 비교하지 않는다.</summary>
        public bool Matches(ScatterBrushPreset preset)
        {
            if (preset == null) return false;
            if (JsonUtility.ToJson(Settings) != JsonUtility.ToJson(preset.Settings)) return false;

            var theirs = preset.Items.Where(i => i != null).ToList();
            if (theirs.Count != Items.Count) return false;
            for (int i = 0; i < theirs.Count; i++)
            {
                if (theirs[i].Definition != Items[i].Definition) return false;
                if (!Mathf.Approximately(theirs[i].Weight, Items[i].Weight)) return false;
            }
            return true;
        }

        private int IndexOf(MapObjectDefinition definition)
        {
            if (definition == null) return -1;
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Definition == definition) return i;
            return -1;
        }
    }
}
