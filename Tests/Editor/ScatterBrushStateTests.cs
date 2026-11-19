using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Editor.Tests
{
    public sealed class ScatterBrushStateTests
    {
        private readonly List<Object> created = new();

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        private MapObjectDefinition Definition(string id, PlacementKind kind = PlacementKind.Scatter)
        {
            var definition = Track(ScriptableObject.CreateInstance<MapObjectDefinition>());
            definition.name = id;
            definition.Configure(id, Vector3Int.one, null, "Test", kind);
            return definition;
        }

        private ScatterBrushPreset Preset() => Track(ScriptableObject.CreateInstance<ScatterBrushPreset>());

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
                if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
        }

        // ───────────────────────── 정의 종류 ─────────────────────────

        [Test]
        public void Definition_ScatterKind_BecomesCoreScatter()
        {
            Assert.That(Definition("grass").ToCoreDefinition().Kind, Is.EqualTo(PlacementKind.Scatter));
            Assert.That(Definition("crate", PlacementKind.Grid).ToCoreDefinition().Kind, Is.EqualTo(PlacementKind.Grid));
        }

        [Test]
        public void Catalog_Signature_ChangesWithKind()
        {
            var definition = Definition("rock", PlacementKind.Grid);
            string before = new EditorObjectCatalog(new[] { definition }).Signature;
            definition.Configure("rock", Vector3Int.one, null, "Test", PlacementKind.Scatter);
            Assert.That(new EditorObjectCatalog(new[] { definition }).Signature, Is.Not.EqualTo(before));
        }

        // ───────────────────────── 구성 ─────────────────────────

        [Test]
        public void Toggle_AddsThenRemoves_WithDefaultWeight()
        {
            var state = new ScatterBrushState();
            var grass = Definition("grass");

            Assert.That(state.Toggle(grass), Is.True);
            Assert.That(state.Contains(grass), Is.True);
            Assert.That(state.Items[0].Weight, Is.EqualTo(ScatterBrushItem.DefaultWeight));

            Assert.That(state.Toggle(grass), Is.False);
            Assert.That(state.Contains(grass), Is.False);
        }

        [Test]
        public void Add_DoesNotDuplicateOrRemove()
        {
            var state = new ScatterBrushState();
            var grass = Definition("grass");

            Assert.That(state.Add(grass), Is.True);
            Assert.That(state.Add(grass), Is.False);
            Assert.That(state.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void Weight_IsClamped()
        {
            var item = new ScatterBrushItem(Definition("grass"), 99f);
            Assert.That(item.Weight, Is.EqualTo(ScatterBrushItem.MaxWeight));
            item.Weight = -1f;
            Assert.That(item.Weight, Is.EqualTo(0f));
        }

        [Test]
        public void Share_IsAmongUsableItems()
        {
            var state = new ScatterBrushState();
            state.Add(Definition("grass"));
            state.Add(Definition("flower"));
            state.Add(Definition("crate", PlacementKind.Grid)); // 장식이 아니라 몫 계산에서 빠진다
            state.Items[0].Weight = 3f;
            state.Items[1].Weight = 1f;

            Assert.That(state.Share(0), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(state.Share(1), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(state.Share(2), Is.EqualTo(0f));
        }

        [Test]
        public void ToEntries_SkipsDeletedGridAndZeroWeight()
        {
            var state = new ScatterBrushState();
            var deleted = Definition("deleted");
            state.Add(Definition("grass"));
            state.Add(Definition("crate", PlacementKind.Grid));
            state.Add(Definition("zero"));
            state.Add(deleted);
            state.Items[2].Weight = 0f;
            Object.DestroyImmediate(deleted);

            var entries = state.ToEntries();
            Assert.That(entries.Select(e => e.DefinitionId), Is.EqualTo(new[] { "grass" }));
            Assert.That(state.HasUsableEntries, Is.True);
        }

        [Test]
        public void EraseFilter_NullWhenAllKinds_ElseCompositionIds()
        {
            var state = new ScatterBrushState();
            state.Add(Definition("grass"));
            state.Add(Definition("flower"));

            Assert.That(state.EraseFilter(), Is.EquivalentTo(new[] { "grass", "flower" }));
            state.EraseAllKinds = true;
            Assert.That(state.EraseFilter(), Is.Null);
        }

        [Test]
        public void WantsErase_DrawMode_ShiftErases()
        {
            var state = new ScatterBrushState();
            Assert.That(state.EraseMode, Is.False, "기본은 그리기 모드");
            Assert.That(state.WantsErase(shift: false), Is.False);
            Assert.That(state.WantsErase(shift: true), Is.True);
        }

        [Test]
        public void WantsErase_EraseMode_ShiftDraws()
        {
            var state = new ScatterBrushState { EraseMode = true };
            Assert.That(state.WantsErase(shift: false), Is.True);
            Assert.That(state.WantsErase(shift: true), Is.False);
        }

        [Test]
        public void EraseMode_SurvivesSerialization_AndIgnoredByPresetMatch()
        {
            var state = new ScatterBrushState();
            state.Add(Definition("grass"));
            var preset = Preset();
            state.SaveTo(preset);

            state.EraseMode = true;
            Assert.That(state.Matches(preset), Is.True, "모드는 프리셋 비교에서 뺀다");

            // 창 직렬화(도메인 리로드)와 같은 경로
            var copy = JsonUtility.FromJson<ScatterBrushState>(JsonUtility.ToJson(state));
            Assert.That(copy.EraseMode, Is.True);

            state.Load(preset);
            Assert.That(state.EraseMode, Is.True, "프리셋을 불러와도 모드는 그대로");
        }

        // ───────────────────────── 프리셋 ─────────────────────────

        [Test]
        public void Preset_RoundTrip_CopiesValues()
        {
            var grass = Definition("grass");
            var source = new ScatterBrushState();
            source.Add(grass);
            source.Items[0].Weight = 7f;
            source.Settings.Radius = 4.5f;
            source.Settings.Density = 2f;

            var preset = Preset();
            source.SaveTo(preset);

            var loaded = new ScatterBrushState();
            loaded.Load(preset);
            Assert.That(loaded.Settings.Radius, Is.EqualTo(4.5f));
            Assert.That(loaded.Settings.Density, Is.EqualTo(2f));
            Assert.That(loaded.Items.Count, Is.EqualTo(1));
            Assert.That(loaded.Items[0].Definition, Is.SameAs(grass));
            Assert.That(loaded.Items[0].Weight, Is.EqualTo(7f));
        }

        [Test]
        public void Preset_DoesNotShareReferencesWithState()
        {
            var state = new ScatterBrushState();
            state.Add(Definition("grass"));
            var preset = Preset();
            state.SaveTo(preset);

            // 저장 뒤 상태를 고쳐도 프리셋은 그대로
            state.Settings.Radius = 9f;
            state.Items[0].Weight = 1f;
            Assert.That(preset.Settings.Radius, Is.Not.EqualTo(9f));
            Assert.That(preset.Items[0].Weight, Is.EqualTo(ScatterBrushItem.DefaultWeight));

            // 불러온 뒤 상태를 고쳐도 프리셋은 그대로
            state.Load(preset);
            state.Settings.Density = 8f;
            state.Items[0].Weight = 2f;
            Assert.That(preset.Settings.Density, Is.Not.EqualTo(8f));
            Assert.That(preset.Items[0].Weight, Is.EqualTo(ScatterBrushItem.DefaultWeight));
        }

        [Test]
        public void Matches_UntilSettingsOrCompositionChange()
        {
            var state = new ScatterBrushState();
            state.Add(Definition("grass"));
            var preset = Preset();
            state.SaveTo(preset);
            Assert.That(state.Matches(preset), Is.True);

            state.Settings.Radius += 1f;
            Assert.That(state.Matches(preset), Is.False);

            state.Load(preset);
            Assert.That(state.Matches(preset), Is.True);
            state.Items[0].Weight = 1f;
            Assert.That(state.Matches(preset), Is.False);

            state.Load(preset);
            state.Add(Definition("flower"));
            Assert.That(state.Matches(preset), Is.False);

            state.Load(preset);
            state.EraseAllKinds = true; // 지우개 대상은 프리셋 비교에서 뺀다
            Assert.That(state.Matches(preset), Is.True);
        }
    }
}
