using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 배치할 수 있는 오브젝트 종류. 코어는 ID와 점유 크기만 안다.
    /// 프리팹, 썸네일, 카테고리는 에디터 쪽 에셋이 갖고 이 정의로 변환해서 넘긴다.
    /// </summary>
    public sealed class ObjectDefinition
    {
        public ObjectDefinition(string id, Vector3Int size, PlacementKind kind = PlacementKind.Grid, bool isFloor = false)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("정의 ID가 비어 있습니다.", nameof(id));
            if (size.x < 1 || size.y < 1 || size.z < 1)
                throw new ArgumentOutOfRangeException(nameof(size), size, "점유 크기는 모두 1 이상이어야 합니다.");
            Id = id;
            Size = size;
            Kind = kind;
            IsFloor = isFloor;
        }

        /// <summary>칸을 차지하지 않는 장식 정의. 점유 크기는 쓰지 않으므로 1로 둔다.</summary>
        public static ObjectDefinition Scatter(string id) => new(id, Vector3Int.one, PlacementKind.Scatter);

        public string Id { get; }

        /// <summary>회전하지 않았을 때(R0)의 점유 크기(셀 수). 장식이면 쓰지 않는다.</summary>
        public Vector3Int Size { get; }

        public PlacementKind Kind { get; }

        /// <summary>
        /// 위를 걸어 다닐 수 있는 바닥 높이 오브젝트(길, 바닥 타일). 칸은 차지하지만 스폰을 막지 않는다.
        /// 에디터는 프리팹 높이를 재서 자동으로 정한다.
        /// </summary>
        public bool IsFloor { get; }

        public override string ToString() => $"{Id} {Size} {Kind}{(IsFloor ? " Floor" : "")}";
    }

    /// <summary>오브젝트를 맵에 놓는 방식.</summary>
    public enum PlacementKind
    {
        /// <summary>셀에 맞춰 놓고 칸을 차지한다(건물, 울타리, 상자). 겹침 검사 대상.</summary>
        Grid = 0,
        /// <summary>자유 위치·회전·스케일로 흩뿌리는 장식(풀, 꽃, 돌). 칸을 차지하지 않고 겹침 검사도 없다.</summary>
        Scatter = 1,
    }

    public interface IObjectCatalog
    {
        bool TryGet(string definitionId, out ObjectDefinition definition);
    }

    /// <summary>정의 ID로 찾는 단순 카탈로그.</summary>
    public sealed class ObjectCatalog : IObjectCatalog
    {
        private readonly Dictionary<string, ObjectDefinition> definitions = new(StringComparer.Ordinal);

        public ObjectCatalog(IEnumerable<ObjectDefinition> items = null)
        {
            if (items == null) return;
            foreach (var item in items) Add(item);
        }

        public int Count => definitions.Count;

        public void Add(ObjectDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!definitions.TryAdd(definition.Id, definition))
                throw new ArgumentException($"정의 ID '{definition.Id}'가 이미 있습니다.", nameof(definition));
        }

        public bool TryGet(string definitionId, out ObjectDefinition definition)
        {
            definition = null;
            return definitionId != null && definitions.TryGetValue(definitionId, out definition);
        }
    }
}
