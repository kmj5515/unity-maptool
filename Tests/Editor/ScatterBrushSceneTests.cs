using System.Collections.Generic;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Editor.Tests
{
    /// <summary>브러시 3단계: 획 찍기 간격, 단축키 단계, 씬 지면 샘플러, 드래그 한 번 = Undo 한 번.</summary>
    public sealed class ScatterBrushSceneTests
    {
        // 열린 씬의 다른 콜라이더와 겹치지 않게 멀리 둔다.
        private static readonly Vector3 Far = new(50000f, 0f, 50000f);

        private readonly List<GameObject> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created)
                if (go != null) Object.DestroyImmediate(go);
            created.Clear();
            Physics.SyncTransforms();
        }

        private GameObject Box(Vector3 position, Vector3 size, Transform parent = null, bool trigger = false)
        {
            var go = new GameObject("box");
            if (parent == null) created.Add(go);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = trigger;
            Physics.SyncTransforms();
            return go;
        }

        // ───────────────────────── 획 ─────────────────────────

        [Test]
        public void Stroke_StampsFirst_ThenEveryHalfRadius()
        {
            var stroke = new ScatterBrushStroke();
            stroke.Begin(false, 1);

            Assert.That(stroke.TryStamp(Vector3.zero, 4f), Is.True, "누른 순간 찍는다");
            Assert.That(stroke.TryStamp(new Vector3(1.9f, 0f, 0f), 4f), Is.False, "반경 × 0.5 = 2 미만");
            Assert.That(stroke.TryStamp(new Vector3(2f, 5f, 0f), 4f), Is.True, "높이는 무시하고 XZ로 잰다");
            Assert.That(stroke.TryStamp(new Vector3(2f, 0f, 1f), 4f), Is.False, "마지막 찍은 자리 기준");
        }

        [Test]
        public void Stroke_Inactive_NeverStamps_AndKeepsEraseMode()
        {
            var stroke = new ScatterBrushStroke();
            Assert.That(stroke.TryStamp(Vector3.zero, 1f), Is.False);

            stroke.Begin(true, 1);
            Assert.That(stroke.Erasing, Is.True);
            stroke.End();
            Assert.That(stroke.Active, Is.False);
            Assert.That(stroke.TryStamp(Vector3.zero, 1f), Is.False);
        }

        // ───────────────────────── 단축키 단계 ─────────────────────────

        [Test]
        public void StepRadius_TenPercent_Clamped()
        {
            Assert.That(ScatterBrushStroke.StepRadius(10f, 1), Is.EqualTo(11f).Within(1e-4f));
            Assert.That(ScatterBrushStroke.StepRadius(11f, -1), Is.EqualTo(10f).Within(1e-4f));
            Assert.That(ScatterBrushStroke.StepRadius(99f, 1), Is.EqualTo(ScatterBrushStroke.MaxRadius));
            Assert.That(ScatterBrushStroke.StepRadius(0.1f, -1), Is.EqualTo(ScatterBrushStroke.MinRadius));
        }

        [Test]
        public void StepDensity_LeavesZero_AndFallsToZero()
        {
            Assert.That(ScatterBrushStroke.StepDensity(0f, 1), Is.EqualTo(ScatterBrushStroke.DensityFloor));
            Assert.That(ScatterBrushStroke.StepDensity(1f, 1), Is.EqualTo(1.1f).Within(1e-4f));
            Assert.That(ScatterBrushStroke.StepDensity(ScatterBrushStroke.DensityFloor, -1), Is.EqualTo(0f));
            Assert.That(ScatterBrushStroke.StepDensity(49f, 1), Is.EqualTo(ScatterBrushStroke.MaxDensity));
        }

        // ───────────────────────── 지면 샘플러 ─────────────────────────

        [Test]
        public void Sampler_NoCollider_FallsBackToBasePlane()
        {
            var sampler = new SceneSurfaceSampler(Far.y + 2f, Far.y + 10f, null);

            Assert.That(sampler.Sample(new Vector2(Far.x, Far.z), out var point, out var normal), Is.True);
            Assert.That(point, Is.EqualTo(new Vector3(Far.x, Far.y + 2f, Far.z)));
            Assert.That(normal, Is.EqualTo(Vector3.up));

            var down = new Ray(new Vector3(Far.x, 50f, Far.z), Vector3.down);
            Assert.That(sampler.Pick(down, out point, out _), Is.True);
            Assert.That(point.y, Is.EqualTo(Far.y + 2f).Within(1e-3f));

            var sky = new Ray(new Vector3(Far.x, 50f, Far.z), Vector3.up);
            Assert.That(sampler.Pick(sky, out _, out _), Is.False, "평면도 못 맞히면 실패");
        }

        [Test]
        public void Sampler_HitsTopCollider_IgnoresTriggers()
        {
            Box(Far + new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 4f));                     // 윗면 y = 2
            Box(Far + new Vector3(0f, 5f, 0f), new Vector3(4f, 1f, 4f), trigger: true);       // 트리거는 무시

            var sampler = new SceneSurfaceSampler(Far.y, Far.y + 10f, null);
            Assert.That(sampler.Sample(new Vector2(Far.x, Far.z), out var point, out var normal), Is.True);
            Assert.That(point.y, Is.EqualTo(Far.y + 2f).Within(1e-3f));
            Assert.That(Vector3.Angle(normal, Vector3.up), Is.LessThan(0.1f));
        }

        [Test]
        public void Sampler_IgnoresCollidersUnderExclude()
        {
            var scatters = new GameObject(MapSceneView.ScattersChildName);
            created.Add(scatters);
            Box(Far + new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 4f));                     // 지면 윗면 y = 2
            Box(Far + new Vector3(0f, 3f, 0f), new Vector3(1f, 2f, 1f), scatters.transform); // 장식 인스턴스(윗면 y = 4)

            var sampler = new SceneSurfaceSampler(Far.y, Far.y + 10f, scatters.transform);
            sampler.Sample(new Vector2(Far.x, Far.z), out var point, out _);
            Assert.That(point.y, Is.EqualTo(Far.y + 2f).Within(1e-3f), "장식 위가 아니라 땅에 찍힌다");

            var withoutExclude = new SceneSurfaceSampler(Far.y, Far.y + 10f, null);
            withoutExclude.Sample(new Vector2(Far.x, Far.z), out point, out _);
            Assert.That(point.y, Is.EqualTo(Far.y + 4f).Within(1e-3f));
        }

        // ───────────────────────── 획 → 커맨드 ─────────────────────────

        [Test]
        public void Stroke_PaintThenErase_EachIsOneUndo()
        {
            var catalog = new ObjectCatalog(new[] { ObjectDefinition.Scatter("grass") });
            var map = new MapData(new Vector3Int(20, 2, 20), new GridSpace(Far, Vector3.one));
            var service = new PlacementService(map, catalog);
            var sampler = new SceneSurfaceSampler(Far.y, Far.y + 2f, null);
            var settings = new ScatterBrushSettings { Radius = 2f, Density = 2f, MinSpacing = 0.2f };
            var entries = new[] { new ScatterEntry("grass", 1f) };
            var stroke = new ScatterBrushStroke();

            // 뿌리기 획: 세 자리를 지나며 찍는다.
            service.BeginGroup();
            stroke.Begin(false, 7);
            foreach (float x in new[] { 5f, 6.5f, 8f })
            {
                var center = Far + new Vector3(x, 0f, 10f);
                if (!stroke.TryStamp(center, settings.Radius)) continue;
                var candidates = ScatterBrush.Generate(center, settings, entries, service, sampler.Sample, stroke.Random);
                if (candidates.Count > 0) service.Execute(new AddScattersCommand(candidates));
            }
            stroke.End();
            service.EndGroup();
            int painted = map.ScatterCount;
            Assert.That(painted, Is.GreaterThan(0));

            // 지우기 획
            service.BeginGroup();
            stroke.Begin(true, 7);
            var eraseCenter = Far + new Vector3(6.5f, 0f, 10f);
            Assert.That(stroke.TryStamp(eraseCenter, 10f), Is.True);
            var ids = ScatterBrush.CollectErase(eraseCenter, 10f, service);
            service.Execute(new RemoveScattersCommand(ids));
            stroke.End();
            service.EndGroup();
            Assert.That(map.ScatterCount, Is.EqualTo(0));

            Assert.That(service.Undo(), Is.True);
            Assert.That(map.ScatterCount, Is.EqualTo(painted), "지우기 획 전체가 한 번에 돌아온다");
            Assert.That(service.Undo(), Is.True);
            Assert.That(map.ScatterCount, Is.EqualTo(0), "뿌리기 획 전체가 한 번에 사라진다");
        }
    }
}
