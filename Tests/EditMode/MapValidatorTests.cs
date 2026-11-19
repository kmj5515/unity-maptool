using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class MapValidatorTests
    {
        private ObjectCatalog catalog;
        private MapData map;

        [SetUp]
        public void SetUp()
        {
            catalog = Catalog(new Vector3Int(1, 1, 1));
            map = new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit);
            // 스폰이 없으면 경고가 나오므로 비어 있는 위층 구석에 하나 둔다.
            map.AddSpawn(new PlacedSpawn(1, "시작", new Vector3Int(9, 1, 9), GridRotation.R0));
        }

        // 배치한 뒤 정의 크기가 바뀐 상황을 같은 ID의 다른 카탈로그로 흉내 낸다.
        private static ObjectCatalog Catalog(Vector3Int crateSize, PlacementKind crateKind = PlacementKind.Grid,
            PlacementKind grassKind = PlacementKind.Scatter) =>
            new(new[]
            {
                new ObjectDefinition("crate", crateSize, crateKind),
                new ObjectDefinition("grass", Vector3Int.one, grassKind),
            });

        private PlacedObject AddObject(int id, int x, int z, string def = "crate")
        {
            var placed = new PlacedObject(id, def, new Vector3Int(x, 0, z), GridRotation.R0);
            map.Add(placed);
            return placed;
        }

        private void AddScatter(int id, Vector3 position, float scale = 1f, string def = "grass") =>
            map.AddScatter(new PlacedScatter(id, def, position, Quaternion.identity, scale));

        private static ZoneBox Box(int x, int z, int sx, int sz) => new(new Vector3Int(x, 0, z), new Vector3Int(sx, 1, sz));

        private void AddZone(int id, params ZoneBox[] boxes) => map.AddZone(new PlacedZone(id, "숲", "forest", 0, boxes));

        private static MapData RoundTrip(MapData source, string from, string to)
        {
            string json = JsonUtility.ToJson(source);
            Assert.IsTrue(json.Contains(from), json);
            var copy = new MapData(Vector3Int.one, GridSpace.Unit);
            JsonUtility.FromJsonOverwrite(json.Replace(from, to), copy);
            return copy;
        }

        [Test]
        public void Validate_정상맵_문제없음()
        {
            AddObject(1, 0, 0);
            AddObject(2, 1, 0);
            AddZone(1, Box(0, 0, 4, 4));
            AddScatter(1, new Vector3(5.5f, 0f, 5.5f));

            CollectionAssert.IsEmpty(MapValidator.Validate(map, catalog));
        }

        [Test]
        public void Validate_정의크기가커져겹침_한쌍을한줄로_나중것을대상으로()
        {
            AddObject(1, 0, 0);
            AddObject(2, 1, 0);
            var grown = Catalog(new Vector3Int(2, 1, 2)); // (0,0)~(2,2)와 (1,0)~(3,2): 2칸 겹침

            var issues = MapValidator.Validate(map, grown);

            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            var issue = issues[0];
            Assert.AreEqual(ValidationCodes.Overlap, issue.Code);
            Assert.AreEqual(IssueSeverity.Error, issue.Severity);
            Assert.AreEqual(IssueTarget.Object, issue.Target);
            Assert.AreEqual(2, issue.TargetId);
            Assert.AreEqual(1, issue.OtherId);
            Assert.AreEqual(new Bounds(new Vector3(1.5f, 0.5f, 1f), new Vector3(1f, 1f, 2f)), issue.Area);
            StringAssert.Contains("2칸", issue.Message);
        }

        [Test]
        public void Validate_세개가한셀에겹침_쌍마다한줄()
        {
            AddObject(1, 0, 0);
            AddObject(2, 1, 0);
            AddObject(3, 1, 1);
            var grown = Catalog(new Vector3Int(2, 1, 2));

            var pairs = MapValidator.Validate(map, grown)
                .Where(i => i.Code == ValidationCodes.Overlap)
                .Select(i => (i.TargetId, i.OtherId))
                .ToList();

            // 셀 (1,0,1)은 1·2·3이 모두 차지하지만 첫 주인(1)과의 쌍만 센다. 2·3은 (2,0,1)에서 만난다.
            CollectionAssert.AreEquivalent(new[] { (2, 1), (3, 1), (3, 2) }, pairs);
        }

        [Test]
        public void Validate_정의크기가커져맵밖_범위이탈()
        {
            AddObject(1, 9, 9);

            var issues = MapValidator.Validate(map, Catalog(new Vector3Int(2, 1, 1)));

            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            Assert.AreEqual(ValidationCodes.OutOfBounds, issues[0].Code);
            Assert.AreEqual(1, issues[0].TargetId);
        }

        [Test]
        public void Validate_정의없음과종류바뀜_오브젝트()
        {
            AddObject(1, 0, 0, "deleted");
            AddObject(2, 2, 0);

            var issues = MapValidator.Validate(map, Catalog(Vector3Int.one, crateKind: PlacementKind.Scatter));

            Assert.AreEqual(2, issues.Count, string.Join("\n", issues));
            Assert.AreEqual((ValidationCodes.UnknownDefinition, 1), (issues[0].Code, issues[0].TargetId));
            Assert.AreEqual((ValidationCodes.WrongKind, 2), (issues[1].Code, issues[1].TargetId));
        }

        [Test]
        public void Validate_장식문제는원인과정의별로묶음()
        {
            AddScatter(1, new Vector3(1f, 0f, 1f), def: "gone");
            AddScatter(2, new Vector3(3f, 0f, 3f), def: "gone");
            AddScatter(3, new Vector3(5f, 0f, 5f), scale: 0f);
            AddScatter(4, new Vector3(-1f, 0f, 5f));
            AddScatter(5, new Vector3(5f, 0f, 5f)); // 정상

            var issues = MapValidator.Validate(map, catalog);

            Assert.AreEqual(3, issues.Count, string.Join("\n", issues));
            Assert.IsTrue(issues.All(i => i.Target == IssueTarget.Scatter));

            var gone = issues.Single(i => i.Code == ValidationCodes.UnknownDefinition);
            CollectionAssert.AreEqual(new[] { 1, 2 }, gone.TargetIds);
            StringAssert.Contains("2개", gone.Message);
            Assert.IsTrue(gone.Area.Contains(new Vector3(1f, 0f, 1f)) && gone.Area.Contains(new Vector3(3f, 0f, 3f)));

            CollectionAssert.AreEqual(new[] { 3 }, issues.Single(i => i.Code == ValidationCodes.InvalidScatter).TargetIds);
            CollectionAssert.AreEqual(new[] { 4 }, issues.Single(i => i.Code == ValidationCodes.OutOfBounds).TargetIds);
        }

        [Test]
        public void Validate_장식정의가칸으로바뀜_종류오류()
        {
            AddScatter(1, new Vector3(1f, 0f, 1f));

            var issues = MapValidator.Validate(map, Catalog(Vector3Int.one, grassKind: PlacementKind.Grid));

            Assert.AreEqual(ValidationCodes.WrongKind, issues.Single().Code);
        }

        [Test]
        public void Validate_존상자없음_빈상자_범위밖()
        {
            AddZone(1);
            AddZone(2, Box(0, 0, 2, 2), Box(3, 3, 0, 2));
            AddZone(3, Box(0, 0, 2, 2), Box(8, 8, 4, 1));

            var issues = MapValidator.Validate(map, catalog);

            Assert.AreEqual(3, issues.Count, string.Join("\n", issues));
            Assert.IsTrue(issues.All(i => i.Target == IssueTarget.Zone));
            Assert.AreEqual((ValidationCodes.InvalidZone, 1), (issues[0].Code, issues[0].TargetId));
            Assert.AreEqual((ValidationCodes.InvalidZone, 2), (issues[1].Code, issues[1].TargetId));
            StringAssert.Contains("상자 2번", issues[1].Message);
            Assert.AreEqual((ValidationCodes.OutOfBounds, 3), (issues[2].Code, issues[2].TargetId));
            StringAssert.Contains("상자 2번", issues[2].Message);
        }

        [Test]
        public void Validate_중복ID_오브젝트_예외없이두번째를보고()
        {
            AddObject(1, 0, 0);
            AddObject(2, 5, 5);
            var broken = RoundTrip(map, "\"instanceId\":2", "\"instanceId\":1");

            var issues = MapValidator.Validate(broken, catalog);

            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            Assert.AreEqual(ValidationCodes.DuplicateId, issues[0].Code);
            Assert.AreEqual(1, issues[0].TargetId);
            Assert.AreEqual(new Vector3(5.5f, 0.5f, 5.5f), issues[0].Area.center); // 두 번째 것의 위치
        }

        [Test]
        public void Validate_중복ID_존과장식()
        {
            AddZone(1, Box(0, 0, 2, 2));
            AddZone(2, Box(4, 4, 2, 2));
            AddScatter(1, new Vector3(1f, 0f, 1f));
            AddScatter(2, new Vector3(2f, 0f, 2f));
            string json = JsonUtility.ToJson(map);
            // 존과 장식 모두 필드 이름이 "id"라 한 번에 바꾼다.
            var broken = new MapData(Vector3Int.one, GridSpace.Unit);
            JsonUtility.FromJsonOverwrite(json.Replace("\"id\":2", "\"id\":1"), broken);

            var issues = MapValidator.Validate(broken, catalog);

            Assert.AreEqual(2, issues.Count, string.Join("\n", issues));
            Assert.IsTrue(issues.All(i => i.Code == ValidationCodes.DuplicateId && i.TargetId == 1));
            Assert.AreEqual(IssueTarget.Zone, issues[0].Target);
            Assert.AreEqual(IssueTarget.Scatter, issues[1].Target);
        }

        [Test]
        public void Sort_오류먼저_대상종류_ID순()
        {
            var area = new Bounds();
            var list = new System.Collections.Generic.List<ValidationIssue>
            {
                new(IssueSeverity.Warning, "w", IssueTarget.Object, 1, area, ""),
                new(IssueSeverity.Error, "e", IssueTarget.Scatter, 1, area, ""),
                new(IssueSeverity.Error, "e", IssueTarget.Object, 5, area, ""),
                new(IssueSeverity.Error, "e", IssueTarget.Object, 2, area, ""),
            };

            MapValidator.Sort(list);

            CollectionAssert.AreEqual(
                new[] { (IssueTarget.Object, 2), (IssueTarget.Object, 5), (IssueTarget.Scatter, 1), (IssueTarget.Object, 1) },
                list.Select(i => (i.Target, i.TargetId)));
            Assert.AreEqual(IssueSeverity.Warning, list[3].Severity);
        }

        [Test]
        public void Validate_데이터를바꾸지않음()
        {
            AddObject(1, 0, 0);
            AddObject(2, 1, 0);
            string before = JsonUtility.ToJson(map);

            MapValidator.Validate(map, Catalog(new Vector3Int(2, 1, 2)));

            Assert.AreEqual(before, JsonUtility.ToJson(map));
        }
    }
}
