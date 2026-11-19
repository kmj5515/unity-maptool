using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>존을 이루는 그리드 정렬 상자 하나. 셀 [Min, Min + Size) 범위다.</summary>
    [Serializable]
    public struct ZoneBox : IEquatable<ZoneBox>
    {
        [SerializeField] private Vector3Int min;
        [SerializeField] private Vector3Int size;

        public ZoneBox(Vector3Int min, Vector3Int size)
        {
            this.min = min;
            this.size = size;
        }

        public ZoneBox(BoundsInt bounds) : this(bounds.position, bounds.size) { }

        public Vector3Int Min => min;
        public Vector3Int Size => size;
        public BoundsInt Bounds => new(min, size);
        public bool IsEmpty => size.x < 1 || size.y < 1 || size.z < 1;
        public int CellCount => IsEmpty ? 0 : size.x * size.y * size.z;

        public bool Contains(Vector3Int cell)
        {
            var max = min + size;
            return cell.x >= min.x && cell.x < max.x &&
                   cell.y >= min.y && cell.y < max.y &&
                   cell.z >= min.z && cell.z < max.z;
        }

        /// <summary>
        /// 한 면을 바깥쪽으로 cells칸 민 상자(음수면 안쪽). axis는 0=X, 1=Y, 2=Z, maxSide면 +쪽 면.
        /// 크기는 최소 1칸을 지키고, 면은 맵 범위 [0, mapSize)를 넘지 않는다.
        /// </summary>
        public ZoneBox WithFaceMoved(int axis, bool maxSide, int cells, Vector3Int mapSize)
        {
            int lo = min[axis];
            int hi = min[axis] + size[axis];
            if (maxSide) hi = Mathf.Clamp(hi + cells, lo + 1, Mathf.Max(lo + 1, mapSize[axis]));
            else lo = Mathf.Clamp(lo - cells, Mathf.Min(0, hi - 1), hi - 1);

            var newMin = min;
            var newSize = size;
            newMin[axis] = lo;
            newSize[axis] = hi - lo;
            return new ZoneBox(newMin, newSize);
        }

        public bool Equals(ZoneBox other) => min == other.min && size == other.size;
        public override bool Equals(object obj) => obj is ZoneBox other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(min, size);
        public override string ToString() => $"{min}+{size}";
    }

    /// <summary>
    /// 맵에 놓인 존 하나(앰비언스·리버브 등). 코어는 존이 무슨 소리인지 모르고 ProfileId 문자열만 갖는다.
    /// 오브젝트와 달리 점유 규칙과 무관해서 오브젝트·다른 존과 겹칠 수 있다.
    /// PlacedObject처럼 값 타입이고 바꿀 때는 With* 로 새 값을 만들어 ModifyZoneCommand로 교체한다.
    /// Boxes 배열은 생성할 때 복사하므로 넘긴 배열을 나중에 바꿔도 존은 그대로다.
    /// Id 0은 아직 맵에 들어가지 않은 후보를 뜻한다.
    /// </summary>
    [Serializable]
    public struct PlacedZone : IEquatable<PlacedZone>
    {
        [SerializeField] private int id;
        [SerializeField] private string name;
        [SerializeField] private string profileId;
        [SerializeField] private int priority;
        [SerializeField] private ZoneBox[] boxes;

        public PlacedZone(int id, string name, string profileId, int priority, IEnumerable<ZoneBox> boxes)
        {
            this.id = id;
            this.name = name ?? string.Empty;
            this.profileId = profileId ?? string.Empty;
            this.priority = priority;
            this.boxes = boxes != null ? new List<ZoneBox>(boxes).ToArray() : Array.Empty<ZoneBox>();
        }

        public int Id => id;

        /// <summary>아티스트가 목록에서 알아보기 위한 이름. 동작에는 영향 없다.</summary>
        public string Name => name ?? string.Empty;

        /// <summary>오디오 쪽 프로필(AudioZoneProfile)을 찾는 키. 비어 있으면 아직 지정하지 않은 존이다.</summary>
        public string ProfileId => profileId ?? string.Empty;

        /// <summary>같은 종류의 존이 겹치면 높은 쪽이 이긴다.</summary>
        public int Priority => priority;

        public IReadOnlyList<ZoneBox> Boxes => boxes ?? Array.Empty<ZoneBox>();

        /// <summary>상자들의 셀 수 합. 상자끼리 겹친 셀은 중복해서 센다(우선순위 동률 판정용 근사치).</summary>
        public int CellCount
        {
            get
            {
                int total = 0;
                foreach (var box in Boxes) total += box.CellCount;
                return total;
            }
        }

        /// <summary>모든 상자를 감싸는 영역. 상자가 없으면 크기 0.</summary>
        public BoundsInt Bounds
        {
            get
            {
                var list = Boxes;
                if (list.Count == 0) return default;
                var lo = list[0].Min;
                var hi = list[0].Min + list[0].Size;
                for (int i = 1; i < list.Count; i++)
                {
                    lo = Vector3Int.Min(lo, list[i].Min);
                    hi = Vector3Int.Max(hi, list[i].Min + list[i].Size);
                }
                return new BoundsInt(lo, hi - lo);
            }
        }

        public bool Contains(Vector3Int cell)
        {
            foreach (var box in Boxes)
            {
                if (box.Contains(cell)) return true;
            }
            return false;
        }

        public PlacedZone WithName(string newName) => new(id, newName, profileId, priority, Boxes);
        public PlacedZone WithProfile(string newProfileId) => new(id, name, newProfileId, priority, Boxes);
        public PlacedZone WithPriority(int newPriority) => new(id, name, profileId, newPriority, Boxes);
        public PlacedZone WithBoxes(IEnumerable<ZoneBox> newBoxes) => new(id, name, profileId, priority, newBoxes);
        internal PlacedZone WithId(int newId) => new(newId, name, profileId, priority, Boxes);

        public bool Equals(PlacedZone other)
        {
            if (id != other.id || Name != other.Name || ProfileId != other.ProfileId || priority != other.priority)
                return false;

            var a = Boxes;
            var b = other.Boxes;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i])) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is PlacedZone other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(id, Name, ProfileId, priority, Boxes.Count);

        public static bool operator ==(PlacedZone a, PlacedZone b) => a.Equals(b);
        public static bool operator !=(PlacedZone a, PlacedZone b) => !a.Equals(b);

        public override string ToString() => $"zone #{id} '{Name}' {ProfileId} p{priority} boxes={Boxes.Count}";
    }
}
