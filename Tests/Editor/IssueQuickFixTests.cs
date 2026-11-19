using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Editor.Tests
{
    public sealed class IssueQuickFixTests
    {
        private readonly List<Object> created = new();
        private EditorObjectCatalog catalog;
        private MapData map;
        private PlacementService service;

        private MapObjectDefinition Definition(string id, PlacementKind kind, GameObject prefab)
        {
            var definition = ScriptableObject.CreateInstance<MapObjectDefinition>();
            created.Add(definition);
            definition.name = id;
            definition.Configure(id, Vector3Int.one, prefab, "Test", kind);
            return definition;
        }

        [SetUp]
        public void SetUp()
        {
            var prefab = new GameObject("prefab");
            created.Add(prefab);
            catalog = new EditorObjectCatalog(new[]
            {
                Definition("crate", PlacementKind.Grid, prefab),
                Definition("bare", PlacementKind.Grid, null),
                Definition("grass", PlacementKind.Scatter, prefab),
            });
            map = new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit);
            // 스폰이 없으면 경고가 나오므로 비어 있는 위층 구석에 하나 둔다.
            map.AddSpawn(new PlacedSpawn(1, "시작", new Vector3Int(9, 1, 9), GridRotation.R0));
            service = new PlacementService(map, catalog);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
                if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private static MapToolWindow.ProfileState Profiles(string id) =>
            string.IsNullOrEmpty(id) ? MapToolWindow.ProfileState.Empty
            : id == "gone" ? MapToolWindow.ProfileState.Missing
            : MapToolWindow.ProfileState.Ok;

        private List<ValidationIssue> Validate(MapData target = null) => EditorMapValidator.Validate(target ?? map, catalog, Profiles);

        private ValidationIssue Single(string code)
        {
            var issues = Validate();
            Assert.AreEqual(1, issues.Count(i => i.Code == code), string.Join("\n", issues));
            return issues.First(i => i.Code == code);
        }

        private static ZoneBox Box(int x, int z, int w, int d) => new(new Vector3Int(x, 0, z), new Vector3Int(w, 1, d));

        /// <summary>수정을 실행하면 문제가 사라지고, Undo 한 번이면 문제가 돌아온다.</summary>
        private void AssertFixAndUndo(ValidationIssue issue, string expectedLabel)
        {
            var fix = IssueQuickFix.Create(issue, map);
            Assert.IsNotNull(fix, issue.ToString());
            Assert.AreEqual(expectedLabel, fix.Label);
            Assert.IsNotNull(fix.Command);

            Assert.IsTrue(service.Execute(fix.Command).IsValid);
            CollectionAssert.IsEmpty(Validate(), "수정 뒤에도 문제가 남음");

            Assert.IsTrue(service.Undo());
            Assert.IsTrue(Validate().Any(i => i.Code == issue.Code && i.TargetId == issue.TargetId), "Undo로 되돌아오지 않음");
        }

        [Test]
        public void 겹침_나중오브젝트_삭제()
        {
            map.Add(new PlacedObject(1, "crate", Vector3Int.zero, GridRotation.R0));
            map.Add(new PlacedObject(2, "crate", Vector3Int.zero, GridRotation.R0));

            var issue = Single(ValidationCodes.Overlap);
            Assert.AreEqual(2, issue.TargetId);
            AssertFixAndUndo(issue, "삭제");
            Assert.IsTrue(map.TryGet(1, out _) && map.TryGet(2, out _));
        }

        [Test]
        public void 정의없는오브젝트_삭제()
        {
            map.Add(new PlacedObject(1, "ghost", Vector3Int.zero, GridRotation.R0));
            AssertFixAndUndo(Single(ValidationCodes.UnknownDefinition), "삭제");
        }

        [Test]
        public void 장식묶음_한번에삭제_Undo한번()
        {
            map.AddScatter(new PlacedScatter(1, "ghost", new Vector3(1f, 0f, 1f), Quaternion.identity, 1f));
            map.AddScatter(new PlacedScatter(2, "ghost", new Vector3(2f, 0f, 2f), Quaternion.identity, 1f));
            map.AddScatter(new PlacedScatter(3, "grass", new Vector3(3f, 0f, 3f), Quaternion.identity, 1f));

            AssertFixAndUndo(Single(ValidationCodes.UnknownDefinition), "2개 삭제");
            Assert.AreEqual(3, map.Scatters.Count);
        }

        [Test]
        public void 존_나쁜상자만정리()
        {
            map.AddZone(new PlacedZone(1, "숲", "forest", 0, new[] { Box(0, 0, 2, 2), Box(3, 3, 0, 2), Box(8, 8, 5, 5) }));

            var issues = Validate();
            Assert.AreEqual(2, issues.Count, string.Join("\n", issues));
            foreach (var issue in issues)
                Assert.AreEqual("상자 정리", IssueQuickFix.Create(issue, map)?.Label, issue.ToString());

            AssertFixAndUndo(issues[0], "상자 정리");
            Assert.IsTrue(map.TryGetZone(1, out var zone));
            Assert.AreEqual(3, zone.Boxes.Count);

            Assert.IsTrue(service.Execute(IssueQuickFix.Create(issues[0], map).Command).IsValid);
            Assert.IsTrue(map.TryGetZone(1, out zone));
            Assert.AreEqual(1, zone.Boxes.Count);
            Assert.AreEqual("forest", zone.ProfileId);
        }

        [Test]
        public void 존_남는상자없으면_존삭제()
        {
            map.AddZone(new PlacedZone(1, "밖", "forest", 0, new[] { Box(8, 8, 5, 5) }));
            AssertFixAndUndo(Single(ValidationCodes.OutOfBounds), "존 삭제");
        }

        [Test]
        public void 상자없는존_존삭제()
        {
            map.AddZone(new PlacedZone(1, "빈", "forest", 0, new ZoneBox[0]));
            AssertFixAndUndo(Single(ValidationCodes.InvalidZone), "존 삭제");
        }

        [Test]
        public void 없는프로필_해제()
        {
            map.AddZone(new PlacedZone(1, "숲", "gone", 0, new[] { Box(0, 0, 2, 2) }));

            var fix = IssueQuickFix.Create(Single(EditorValidationCodes.ProfileMissing), map);
            Assert.AreEqual("프로필 해제", fix.Label);
            Assert.IsTrue(service.Execute(fix.Command).IsValid);
            Assert.IsTrue(map.TryGetZone(1, out var zone));
            Assert.AreEqual("", zone.ProfileId);
            // 해제 뒤에는 '비어 있음' 경고로 바뀐다(수정 버튼 없음, 클릭해서 고른다).
            Assert.IsNull(IssueQuickFix.Create(Single(EditorValidationCodes.ProfileEmpty), map));

            Assert.IsTrue(service.Undo());
            Assert.IsTrue(map.TryGetZone(1, out zone));
            Assert.AreEqual("gone", zone.ProfileId);
        }

        [Test]
        public void 프리팹없음_정의보기_데이터변경없음()
        {
            map.Add(new PlacedObject(1, "bare", Vector3Int.zero, GridRotation.R0));

            var fix = IssueQuickFix.Create(Single(EditorValidationCodes.MissingPrefab), map);
            Assert.AreEqual("정의 보기", fix.Label);
            Assert.IsNull(fix.Command);
            Assert.AreEqual("bare", fix.PingDefinitionId);
        }

        [Test]
        public void 같은ID_수정없음_다른오브젝트수정도없음()
        {
            map.Add(new PlacedObject(1, "crate", Vector3Int.zero, GridRotation.R0));
            map.Add(new PlacedObject(2, "crate", new Vector3Int(2, 0, 2), GridRotation.R0));
            map.Add(new PlacedObject(3, "ghost", new Vector3Int(5, 0, 5), GridRotation.R0));
            string json = JsonUtility.ToJson(map);
            var broken = new MapData(Vector3Int.one, GridSpace.Unit);
            JsonUtility.FromJsonOverwrite(json.Replace("\"instanceId\":2", "\"instanceId\":1"), broken);

            var issues = Validate(broken);
            Assert.IsTrue(issues.Any(i => i.Code == ValidationCodes.DuplicateId), string.Join("\n", issues));
            // 색인을 만들 수 없는 상태라 아무 오브젝트 수정도 내지 않는다(예외 없이).
            foreach (var issue in issues)
                Assert.IsNull(IssueQuickFix.Create(issue, broken), issue.ToString());
        }
    }
}
