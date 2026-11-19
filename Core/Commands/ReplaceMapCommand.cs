using System;
using System.Linq;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>맵 내용 전체를 그 순간 그대로 담아 둔 값. 담긴 요소는 모두 값 타입이라 원본이 바뀌어도 그대로다.</summary>
    internal sealed class MapSnapshot
    {
        public Vector3Int Size { get; private set; }
        public GridSpace Grid { get; private set; }
        public PlacedObject[] Objects { get; private set; }
        public PlacedZone[] Zones { get; private set; }
        public PlacedScatter[] Scatters { get; private set; }
        public PlacedSpawn[] Spawns { get; private set; }
        public int DefaultSpawnId { get; private set; }

        public static MapSnapshot From(MapData map) => new()
        {
            Size = map.Size,
            Grid = map.Grid,
            Objects = map.Objects.ToArray(),
            Zones = map.Zones.ToArray(),
            Scatters = map.Scatters.ToArray(),
            Spawns = map.Spawns.ToArray(),
            DefaultSpawnId = map.DefaultSpawnId,
        };
    }

    /// <summary>
    /// 맵 내용 전체(크기·그리드 포함)를 다른 맵으로 바꾼다. JSON 덮어쓰기에 쓴다. Undo 한 번이면 이전 맵으로 돌아온다.
    /// 배치 규칙은 보지 않는다. 겹침·범위 밖·없는 정의도 그대로 들어가고 맵 검사가 알린다.
    /// </summary>
    public sealed class ReplaceMapCommand : MapCommand
    {
        private readonly MapSnapshot incoming;
        private MapSnapshot previous;

        /// <param name="source">넣을 맵. 지금 내용을 복사해 두므로 나중에 source를 바꿔도 이 커맨드는 그대로다.</param>
        public ReplaceMapCommand(MapData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            incoming = MapSnapshot.From(source);
        }

        internal override PlacementResult Do(PlacementService service)
        {
            previous = MapSnapshot.From(service.Map);
            service.ReplaceAll(incoming);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.ReplaceAll(previous);
    }
}
