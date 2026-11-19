using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 맵의 유일한 진실. 씬 오브젝트는 이 데이터로부터 만들어지는 결과물이다.
    /// 읽기는 누구나 하지만 변경은 PlacementService(커맨드)를 거쳐야 하므로 수정 메서드는 internal이다.
    /// 맵 범위는 셀 [0, Size)이다.
    /// </summary>
    [Serializable]
    public sealed class MapData : ISerializationCallbackReceiver
    {
        [SerializeField] private Vector3Int size;
        [SerializeField] private GridSpace grid;
        [SerializeField] private List<PlacedObject> objects = new();
        [SerializeField] private int nextInstanceId = 1;

        // 존은 점유 규칙과 무관한 별도 목록. 개수가 적어 색인 없이 선형 탐색한다. ID는 오브젝트와 따로 센다.
        [SerializeField] private List<PlacedZone> zones = new();
        [SerializeField] private int nextZoneId = 1;

        // 브러시로 뿌린 장식. 칸 점유와 무관하며 수천 개가 될 수 있어 색인을 둔다. ID는 따로 센다.
        [SerializeField] private List<PlacedScatter> scatters = new();
        [SerializeField] private int nextScatterId = 1;

        // 플레이어 스폰 지점. 몇 개뿐이라 선형 탐색한다. ID는 따로 센다.
        // 기본 스폰은 Id 하나로만 저장해 "기본이 둘" 같은 상태가 생기지 않게 한다. 0이면 지정 안 됨.
        [SerializeField] private List<PlacedSpawn> spawns = new();
        [SerializeField] private int nextSpawnId = 1;
        [SerializeField] private int defaultSpawnId;

        // InstanceId → objects 인덱스. 저장하지 않고 필요할 때 다시 만든다.
        [NonSerialized] private Dictionary<int, int> index;

        // 장식 Id → scatters 인덱스. 같은 방식으로 필요할 때 다시 만든다.
        [NonSerialized] private Dictionary<int, int> scatterIndex;

        public MapData(Vector3Int size, GridSpace grid)
        {
            if (size.x < 1 || size.y < 1 || size.z < 1)
                throw new ArgumentOutOfRangeException(nameof(size), size, "맵 크기는 모두 1 이상이어야 합니다.");
            this.size = size;
            this.grid = grid;
        }

        public Vector3Int Size => size;
        public GridSpace Grid => grid;
        public BoundsInt Bounds => new(Vector3Int.zero, size);
        public IReadOnlyList<PlacedObject> Objects => objects;
        public int Count => objects.Count;

        public bool TryGet(int instanceId, out PlacedObject placed)
        {
            EnsureIndex();
            if (index.TryGetValue(instanceId, out var i))
            {
                placed = objects[i];
                return true;
            }
            placed = default;
            return false;
        }

        internal int AllocateId()
        {
            EnsureIndex(); // 불러온 데이터의 최대 ID보다 크게 맞춘다
            return nextInstanceId++;
        }

        internal void Add(PlacedObject placed)
        {
            EnsureIndex();
            if (placed.InstanceId <= 0) throw new ArgumentException("InstanceId가 할당되지 않았습니다.", nameof(placed));
            if (index.ContainsKey(placed.InstanceId))
                throw new InvalidOperationException($"InstanceId {placed.InstanceId}가 이미 맵에 있습니다.");

            objects.Add(placed);
            index.Add(placed.InstanceId, objects.Count - 1);
            // 정해진 Id로 넣는 경우(불러오기·Undo)에도 다음 발급 Id가 겹치지 않게 한다.
            nextInstanceId = Math.Max(nextInstanceId, placed.InstanceId + 1);
        }

        internal void Replace(PlacedObject placed)
        {
            EnsureIndex();
            if (!index.TryGetValue(placed.InstanceId, out var i))
                throw new InvalidOperationException($"InstanceId {placed.InstanceId}가 맵에 없습니다.");
            objects[i] = placed;
        }

        internal void Remove(int instanceId)
        {
            EnsureIndex();
            if (!index.TryGetValue(instanceId, out var i))
                throw new InvalidOperationException($"InstanceId {instanceId}가 맵에 없습니다.");

            // 저장 파일의 순서가 흔들리지 않게 순서를 유지한 채 지운다.
            objects.RemoveAt(i);
            index = null;
        }

        public IReadOnlyList<PlacedZone> Zones => zones ??= new List<PlacedZone>();

        public bool TryGetZone(int zoneId, out PlacedZone zone)
        {
            int i = IndexOfZone(zoneId);
            zone = i >= 0 ? zones[i] : default;
            return i >= 0;
        }

        internal int AllocateZoneId()
        {
            // 불러온 데이터(또는 존이 없던 예전 데이터)의 최대 ID보다 크게 맞춘다.
            int maxId = 0;
            foreach (var zone in Zones) maxId = Math.Max(maxId, zone.Id);
            nextZoneId = Math.Max(Math.Max(nextZoneId, 1), maxId + 1);
            return nextZoneId++;
        }

        internal void AddZone(PlacedZone zone)
        {
            if (zone.Id <= 0) throw new ArgumentException("존 Id가 할당되지 않았습니다.", nameof(zone));
            if (IndexOfZone(zone.Id) >= 0) throw new InvalidOperationException($"존 #{zone.Id}가 이미 맵에 있습니다.");
            zones.Add(zone);
        }

        internal void ReplaceZone(PlacedZone zone)
        {
            int i = IndexOfZone(zone.Id);
            if (i < 0) throw new InvalidOperationException($"존 #{zone.Id}가 맵에 없습니다.");
            zones[i] = zone;
        }

        internal void RemoveZone(int zoneId)
        {
            int i = IndexOfZone(zoneId);
            if (i < 0) throw new InvalidOperationException($"존 #{zoneId}가 맵에 없습니다.");
            zones.RemoveAt(i);
        }

        private int IndexOfZone(int zoneId)
        {
            var list = Zones;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == zoneId) return i;
            }
            return -1;
        }

        public IReadOnlyList<PlacedSpawn> Spawns => spawns ??= new List<PlacedSpawn>();

        /// <summary>저장된 기본 스폰 Id. 0이면 지정 안 됨. 실제로 쓸 지점은 TryGetDefaultSpawn으로 얻는다.</summary>
        public int DefaultSpawnId => defaultSpawnId;

        public bool TryGetSpawn(int spawnId, out PlacedSpawn spawn)
        {
            int i = IndexOfSpawn(spawnId);
            spawn = i >= 0 ? spawns[i] : default;
            return i >= 0;
        }

        /// <summary>
        /// 플레이를 시작할 지점. 기본으로 지정한 지점이 있으면 그것, 지정이 없거나 그 지점이 없으면 첫 지점.
        /// 스폰 지점이 하나도 없으면 false.
        /// </summary>
        public bool TryGetDefaultSpawn(out PlacedSpawn spawn)
        {
            if (TryGetSpawn(defaultSpawnId, out spawn)) return true;
            var list = Spawns;
            spawn = list.Count > 0 ? list[0] : default;
            return list.Count > 0;
        }

        internal int AllocateSpawnId()
        {
            int maxId = 0;
            foreach (var spawn in Spawns) maxId = Math.Max(maxId, spawn.Id);
            nextSpawnId = Math.Max(Math.Max(nextSpawnId, 1), maxId + 1);
            return nextSpawnId++;
        }

        /// <summary>index가 음수면 끝에 넣는다(Undo는 원래 자리에 넣어 목록 순서를 지킨다).</summary>
        internal void AddSpawn(PlacedSpawn spawn, int index = -1)
        {
            if (spawn.Id <= 0) throw new ArgumentException("스폰 지점 Id가 할당되지 않았습니다.", nameof(spawn));
            if (IndexOfSpawn(spawn.Id) >= 0) throw new InvalidOperationException($"스폰 지점 @{spawn.Id}가 이미 맵에 있습니다.");
            var list = spawns ??= new List<PlacedSpawn>();
            if (index < 0 || index > list.Count) list.Add(spawn);
            else list.Insert(index, spawn);
        }

        internal void ReplaceSpawn(PlacedSpawn spawn)
        {
            int i = IndexOfSpawn(spawn.Id);
            if (i < 0) throw new InvalidOperationException($"스폰 지점 @{spawn.Id}가 맵에 없습니다.");
            spawns[i] = spawn;
        }

        internal void RemoveSpawn(int spawnId)
        {
            int i = IndexOfSpawn(spawnId);
            if (i < 0) throw new InvalidOperationException($"스폰 지점 @{spawnId}가 맵에 없습니다.");
            spawns.RemoveAt(i);
        }

        internal void SetDefaultSpawnId(int spawnId) => defaultSpawnId = spawnId;

        internal int IndexOfSpawn(int spawnId)
        {
            var list = Spawns;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == spawnId) return i;
            }
            return -1;
        }

        public IReadOnlyList<PlacedScatter> Scatters => scatters ??= new List<PlacedScatter>();
        public int ScatterCount => Scatters.Count;

        public bool TryGetScatter(int id, out PlacedScatter scatter)
        {
            EnsureScatterIndex();
            if (scatterIndex.TryGetValue(id, out var i))
            {
                scatter = scatters[i];
                return true;
            }
            scatter = default;
            return false;
        }

        internal int AllocateScatterId()
        {
            EnsureScatterIndex(); // 불러온 데이터의 최대 ID보다 크게 맞춘다
            return nextScatterId++;
        }

        internal void AddScatter(PlacedScatter scatter)
        {
            EnsureScatterIndex();
            if (scatter.Id <= 0) throw new ArgumentException("장식 Id가 할당되지 않았습니다.", nameof(scatter));
            if (scatterIndex.ContainsKey(scatter.Id))
                throw new InvalidOperationException($"장식 ~{scatter.Id}가 이미 맵에 있습니다.");

            scatters.Add(scatter);
            scatterIndex.Add(scatter.Id, scatters.Count - 1);
            nextScatterId = Math.Max(nextScatterId, scatter.Id + 1);
        }

        /// <summary>여러 개를 한 번에 지운다. 순서는 유지하고 목록은 한 번만 훑는다. 없는 Id가 있으면 예외.</summary>
        internal void RemoveScatters(ICollection<int> ids)
        {
            if (ids.Count == 0) return;
            EnsureScatterIndex();
            foreach (var id in ids)
            {
                if (!scatterIndex.ContainsKey(id))
                    throw new InvalidOperationException($"장식 ~{id}가 맵에 없습니다.");
            }

            var set = ids as HashSet<int> ?? new HashSet<int>(ids);
            scatters.RemoveAll(s => set.Contains(s.Id));
            scatterIndex = null;
        }

        /// <summary>크기·그리드를 바꾼다. 내용은 건드리지 않는다(JSON 덮어쓰기가 내용을 비운 뒤에 부른다).</summary>
        internal void Reshape(Vector3Int newSize, GridSpace newGrid)
        {
            if (newSize.x < 1 || newSize.y < 1 || newSize.z < 1)
                throw new ArgumentOutOfRangeException(nameof(newSize), newSize, "맵 크기는 모두 1 이상이어야 합니다.");
            size = newSize;
            grid = newGrid;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        // 에셋 재임포트 등으로 같은 객체에 다시 역직렬화되면 색인을 버리고 다시 만든다.
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            index = null;
            scatterIndex = null;
        }

        private void EnsureScatterIndex()
        {
            if (scatterIndex != null) return;

            scatters ??= new List<PlacedScatter>();
            scatterIndex = new Dictionary<int, int>(scatters.Count);
            int maxId = 0;
            for (int i = 0; i < scatters.Count; i++)
            {
                int id = scatters[i].Id;
                if (!scatterIndex.TryAdd(id, i))
                    throw new InvalidOperationException($"맵 데이터에 장식 Id {id}가 중복되어 있습니다.");
                maxId = Math.Max(maxId, id);
            }
            nextScatterId = Math.Max(Math.Max(nextScatterId, 1), maxId + 1);
        }

        private void EnsureIndex()
        {
            if (index != null) return;

            objects ??= new List<PlacedObject>();
            index = new Dictionary<int, int>(objects.Count);
            int maxId = 0;
            for (int i = 0; i < objects.Count; i++)
            {
                int id = objects[i].InstanceId;
                if (!index.TryAdd(id, i))
                    throw new InvalidOperationException($"맵 데이터에 InstanceId {id}가 중복되어 있습니다.");
                maxId = Math.Max(maxId, id);
            }
            nextInstanceId = Math.Max(nextInstanceId, maxId + 1);
        }
    }
}
