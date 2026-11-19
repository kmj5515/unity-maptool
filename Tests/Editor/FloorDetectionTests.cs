using System.Collections.Generic;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Editor.Tests
{
    public sealed class FloorDetectionTests
    {
        private readonly List<Object> created = new();

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        /// <summary>피벗이 바닥에 오도록 큐브를 자식으로 넣은 프리팹 대용 오브젝트. 높이 height(m).</summary>
        private GameObject Block(float height)
        {
            var root = Track(new GameObject($"Block {height}"));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = new Vector3(5f, height, 5f);
            cube.transform.localPosition = new Vector3(0f, height / 2f, 0f);
            return root;
        }

        private MapObjectDefinition Definition(GameObject prefab, PlacementKind kind = PlacementKind.Grid)
        {
            var definition = Track(ScriptableObject.CreateInstance<MapObjectDefinition>());
            definition.Configure("test", new Vector3Int(5, 1, 5), prefab, "테스트", kind);
            return definition;
        }

        [Test]
        public void TopHeight_IsMeasuredFromRootPivot_IncludingChildTransforms()
        {
            var block = Block(1.2f);
            block.transform.position = new Vector3(50f, 10f, 0f); // 루트 위치는 결과에 영향이 없어야 한다

            Assert.IsTrue(PrefabMeasure.TryGetTopHeight(block, out float top));
            Assert.That(top, Is.EqualTo(1.2f).Within(1e-4f));

            Assert.IsFalse(PrefabMeasure.TryGetTopHeight(Track(new GameObject("Empty")), out _));
            Assert.IsFalse(PrefabMeasure.TryGetTopHeight(null, out _));
        }

        [Test]
        public void FlatPrefab_IsFloor_TallOrMissingIsNot()
        {
            Assert.IsTrue(Definition(Block(0.05f)).ToCoreDefinition().IsFloor, "얇은 길은 바닥");
            Assert.IsTrue(Definition(Block(MapObjectDefinition.FloorMaxHeight - 0.01f)).ToCoreDefinition().IsFloor, "기준 바로 아래는 바닥");
            Assert.IsFalse(Definition(Block(MapObjectDefinition.FloorMaxHeight + 0.01f)).ToCoreDefinition().IsFloor, "기준 바로 위는 막음");
            Assert.IsFalse(Definition(Block(1f)).ToCoreDefinition().IsFloor, "상자 높이는 막음");
            Assert.IsFalse(Definition(null).ToCoreDefinition().IsFloor, "프리팹이 없으면 막음으로 본다");
            Assert.IsFalse(Definition(Block(0.05f), PlacementKind.Scatter).ToCoreDefinition().IsFloor, "장식은 해당 없음");
        }
    }
}
