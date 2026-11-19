using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>JSON을 맵으로 읽지 못했을 때. 메시지는 사용자에게 그대로 보여 줄 수 있는 문장이다.</summary>
    public sealed class MapJsonException : Exception
    {
        public MapJsonException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// 맵을 사람이 읽을 수 있는 JSON으로 내보내고 다시 읽는다.
    /// 내부 저장 구조(MapData 필드)와 분리된 고정 형식이라 코드 구조가 바뀌어도 파일은 그대로 읽힌다.
    /// 형식이 바뀌면 Version을 올리고 FromJson에서 옛 버전을 변환한다.
    ///
    /// 읽을 때 형식이 깨진 파일(문법 오류, 다른 종류의 JSON, 중복 Id, 잘못된 크기·회전)은 거부한다.
    /// 반면 맵 내용의 문제(없는 정의, 범위 밖·겹침)는 그대로 읽고 맵 검사(MapValidator)가 알려 주게 둔다.
    /// </summary>
    public static class MapJson
    {
        public const string FormatName = "MapTool.Map";
        public const int Version = 1;

        public static string ToJson(MapData map, bool pretty = true)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var file = new MapFile
            {
                format = FormatName,
                version = Version,
                size = Ints(map.Size),
                origin = Floats(map.Grid.Origin),
                cellSize = Floats(map.Grid.CellSize),
                defaultSpawn = map.DefaultSpawnId,
            };

            foreach (var o in map.Objects)
                file.objects.Add(new ObjectEntry { id = o.InstanceId, definition = o.DefinitionId, cell = Ints(o.Cell), rotation = Degrees(o.Rotation) });

            foreach (var z in map.Zones)
            {
                var entry = new ZoneEntry { id = z.Id, name = z.Name, profile = z.ProfileId, priority = z.Priority };
                foreach (var box in z.Boxes)
                    entry.boxes.Add(new BoxEntry { min = Ints(box.Min), size = Ints(box.Size) });
                file.zones.Add(entry);
            }

            foreach (var s in map.Scatters)
            {
                var q = s.Rotation;
                file.scatters.Add(new ScatterEntry
                {
                    id = s.Id, definition = s.DefinitionId, position = Floats(s.Position),
                    rotation = new[] { q.x, q.y, q.z, q.w }, scale = s.Scale,
                });
            }

            foreach (var s in map.Spawns)
                file.spawns.Add(new SpawnEntry { id = s.Id, name = s.Name, cell = Ints(s.Cell), rotation = Degrees(s.Rotation) });

            var json = JsonUtility.ToJson(file, pretty);
            return pretty ? CollapseNumberArrays(json) : json;
        }

        // JsonUtility는 배열 원소마다 줄을 바꾼다. [3, 0, 4]처럼 숫자 배열은 한 줄로 모아 읽기 쉽고 diff도 짧게 한다.
        private static readonly Regex NumberArray = new(@"\[\s*(-?[\d.eE+-]+(?:\s*,\s*-?[\d.eE+-]+)*)\s*\]", RegexOptions.Compiled);
        private static readonly Regex Spaces = new(@"\s*,\s*", RegexOptions.Compiled);

        private static string CollapseNumberArrays(string json) =>
            NumberArray.Replace(json, m => "[" + Spaces.Replace(m.Groups[1].Value, ", ") + "]");

        /// <summary>새 MapData를 만든다. 읽을 수 없는 파일이면 MapJsonException.</summary>
        public static MapData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapJsonException("파일이 비어 있습니다.");

            MapFile file;
            try
            {
                file = JsonUtility.FromJson<MapFile>(json);
            }
            catch (Exception e)
            {
                throw new MapJsonException("JSON 문법이 올바르지 않습니다.", e);
            }

            if (file == null || file.format != FormatName)
                throw new MapJsonException($"맵툴 맵 JSON이 아닙니다(\"format\": \"{FormatName}\"이 없습니다).");
            if (file.version < 1)
                throw new MapJsonException($"버전 {file.version}은(는) 올바른 버전이 아닙니다.");
            if (file.version > Version)
                throw new MapJsonException($"더 새로운 맵툴에서 만든 파일입니다(버전 {file.version}, 이 맵툴은 {Version}까지 읽습니다).");

            var size = Int3(file.size, "size");
            if (size.x < 1 || size.y < 1 || size.z < 1)
                throw new MapJsonException($"맵 크기 {size}가 잘못되었습니다. 모두 1 이상이어야 합니다.");
            var cellSize = Float3(file.cellSize, "cellSize");
            if (cellSize.x <= 0f || cellSize.y <= 0f || cellSize.z <= 0f)
                throw new MapJsonException($"칸 크기 {cellSize}가 잘못되었습니다. 모두 0보다 커야 합니다.");

            var map = new MapData(size, new GridSpace(Float3(file.origin, "origin"), cellSize));

            var objectIds = new HashSet<int>();
            foreach (var o in file.objects ?? new List<ObjectEntry>())
            {
                CheckId(o.id, objectIds, "오브젝트");
                map.Add(new PlacedObject(o.id, o.definition ?? string.Empty, Int3(o.cell, $"오브젝트 #{o.id}의 cell"), Rotation(o.rotation, $"오브젝트 #{o.id}")));
            }

            var zoneIds = new HashSet<int>();
            foreach (var z in file.zones ?? new List<ZoneEntry>())
            {
                CheckId(z.id, zoneIds, "구역");
                var boxes = new List<ZoneBox>();
                foreach (var b in z.boxes ?? new List<BoxEntry>())
                    boxes.Add(new ZoneBox(Int3(b.min, $"구역 #{z.id}의 min"), Int3(b.size, $"구역 #{z.id}의 size")));
                map.AddZone(new PlacedZone(z.id, z.name, z.profile, z.priority, boxes));
            }

            var scatterIds = new HashSet<int>();
            foreach (var s in file.scatters ?? new List<ScatterEntry>())
            {
                CheckId(s.id, scatterIds, "장식");
                var r = s.rotation;
                if (r == null || r.Length != 4)
                    throw new MapJsonException($"장식 ~{s.id}의 rotation은 [x, y, z, w] 네 숫자여야 합니다.");
                var rotation = new Quaternion(r[0], r[1], r[2], r[3]);
                if (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f) rotation = Quaternion.identity;
                map.AddScatter(new PlacedScatter(s.id, s.definition ?? string.Empty, Float3(s.position, $"장식 ~{s.id}의 position"), rotation, s.scale));
            }

            var spawnIds = new HashSet<int>();
            foreach (var s in file.spawns ?? new List<SpawnEntry>())
            {
                CheckId(s.id, spawnIds, "스폰");
                map.AddSpawn(new PlacedSpawn(s.id, s.name, Int3(s.cell, $"스폰 @{s.id}의 cell"), Rotation(s.rotation, $"스폰 @{s.id}")));
            }

            // 지워진 스폰을 가리키는 기본 지정은 버린다(맵은 첫 스폰을 기본으로 쓴다).
            map.SetDefaultSpawnId(spawnIds.Contains(file.defaultSpawn) ? file.defaultSpawn : 0);
            return map;
        }

        private static void CheckId(int id, HashSet<int> seen, string kind)
        {
            if (id <= 0) throw new MapJsonException($"{kind} Id {id}가 잘못되었습니다. 1 이상이어야 합니다.");
            if (!seen.Add(id)) throw new MapJsonException($"{kind} Id {id}가 두 번 나옵니다.");
        }

        private static int Degrees(GridRotation rotation) => ((int)rotation & 3) * 90;

        private static GridRotation Rotation(int degrees, string what)
        {
            int normalized = ((degrees % 360) + 360) % 360;
            if (normalized % 90 != 0)
                throw new MapJsonException($"{what}의 회전 {degrees}도는 90도 단위가 아닙니다.");
            return (GridRotation)(normalized / 90);
        }

        private static int[] Ints(Vector3Int v) => new[] { v.x, v.y, v.z };
        private static float[] Floats(Vector3 v) => new[] { v.x, v.y, v.z };

        private static Vector3Int Int3(int[] a, string what)
        {
            if (a == null || a.Length != 3) throw new MapJsonException($"{what}은(는) 숫자 세 개 [x, y, z]여야 합니다.");
            return new Vector3Int(a[0], a[1], a[2]);
        }

        private static Vector3 Float3(float[] a, string what)
        {
            if (a == null || a.Length != 3) throw new MapJsonException($"{what}은(는) 숫자 세 개 [x, y, z]여야 합니다.");
            return new Vector3(a[0], a[1], a[2]);
        }

        // ── 파일 형식(필드 이름이 곧 JSON 키다. 바꾸면 Version을 올릴 것) ──

#pragma warning disable 0649 // JsonUtility가 채운다
        [Serializable]
        private sealed class MapFile
        {
            public string format;
            public int version;
            public int[] size;
            public float[] origin;
            public float[] cellSize;
            public List<ObjectEntry> objects = new();
            public List<ZoneEntry> zones = new();
            public List<ScatterEntry> scatters = new();
            public List<SpawnEntry> spawns = new();
            public int defaultSpawn;
        }

        [Serializable]
        private sealed class ObjectEntry
        {
            public int id;
            public string definition;
            public int[] cell;
            public int rotation;
        }

        [Serializable]
        private sealed class ZoneEntry
        {
            public int id;
            public string name;
            public string profile;
            public int priority;
            public List<BoxEntry> boxes = new();
        }

        [Serializable]
        private sealed class BoxEntry
        {
            public int[] min;
            public int[] size;
        }

        [Serializable]
        private sealed class ScatterEntry
        {
            public int id;
            public string definition;
            public float[] position;
            public float[] rotation;
            public float scale = 1f;
        }

        [Serializable]
        private sealed class SpawnEntry
        {
            public int id;
            public string name;
            public int[] cell;
            public int rotation;
        }
#pragma warning restore 0649
    }
}
