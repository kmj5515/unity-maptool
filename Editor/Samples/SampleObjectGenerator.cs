using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapTool.Editor
{
    /// <summary>
    /// 테스트용 임시 오브젝트(기본 도형 조합 프리팹 + 머티리얼 + 정의 에셋)를 만든다.
    /// 셀 크기 1m 기준이며 프리팹 피벗은 점유 영역 바닥 중심, 앞면은 -Z(등받이·머리판이 +Z)다.
    /// 이미 있는 에셋은 건너뛰므로 여러 번 실행해도 된다.
    /// </summary>
    internal static class SampleObjectGenerator
    {
        // 패키지 폴더는 읽기 전용일 수 있으므로 프로젝트의 Assets 아래에 만든다.
        public const string DefaultRoot = "Assets/MapToolSamples";

        private const string Furniture = "가구";
        private const string Decor = "소품";
        private const string Structure = "구조물";

        private static readonly Color Wood = new(0.55f, 0.38f, 0.24f);
        private static readonly Color Fabric = new(0.36f, 0.52f, 0.72f);
        private static readonly Color Linen = new(0.92f, 0.9f, 0.86f);
        private static readonly Color Leaf = new(0.35f, 0.6f, 0.3f);
        private static readonly Color Clay = new(0.75f, 0.42f, 0.3f);
        private static readonly Color Stone = new(0.72f, 0.71f, 0.68f);
        private static readonly Color Metal = new(0.3f, 0.3f, 0.32f);
        private static readonly Color Light = new(1f, 0.86f, 0.6f);

        private sealed class Spec
        {
            public string Id;
            public string Category;
            public Vector3Int Size;
            public Action<Builder> Build;
        }

        private static readonly Spec[] Specs =
        {
            new() { Id = "crate", Category = Decor, Size = new(1, 1, 1), Build = b => b.Box(Wood, 0, 0.4f, 0, 0.8f, 0.8f, 0.8f) },
            new()
            {
                Id = "table", Category = Furniture, Size = new(2, 1, 1), Build = b =>
                {
                    b.Box(Wood, 0, 0.72f, 0, 1.9f, 0.08f, 0.9f);
                    foreach (float x in new[] { -0.85f, 0.85f })
                    foreach (float z in new[] { -0.35f, 0.35f })
                        b.Box(Wood, x, 0.34f, z, 0.08f, 0.68f, 0.08f);
                },
            },
            new()
            {
                Id = "bed", Category = Furniture, Size = new(2, 1, 3), Build = b =>
                {
                    b.Box(Wood, 0, 0.2f, 0, 1.9f, 0.4f, 2.9f);
                    b.Box(Linen, 0, 0.47f, -0.1f, 1.8f, 0.15f, 2.6f);
                    b.Box(Linen, 0, 0.6f, 1.05f, 1.2f, 0.12f, 0.4f);
                    b.Box(Wood, 0, 0.6f, 1.41f, 1.9f, 1.2f, 0.08f);
                },
            },
            new()
            {
                Id = "sofa", Category = Furniture, Size = new(3, 1, 1), Build = b =>
                {
                    b.Box(Fabric, 0, 0.22f, 0, 2.9f, 0.44f, 0.9f);
                    b.Box(Fabric, 0, 0.62f, 0.37f, 2.9f, 0.6f, 0.16f);
                    b.Box(Fabric, -1.37f, 0.42f, 0, 0.16f, 0.36f, 0.9f);
                    b.Box(Fabric, 1.37f, 0.42f, 0, 0.16f, 0.36f, 0.9f);
                },
            },
            new()
            {
                Id = "shelf", Category = Furniture, Size = new(2, 2, 1), Build = b =>
                {
                    b.Box(Wood, 0, 1f, 0.42f, 1.9f, 2f, 0.06f);
                    b.Box(Wood, -0.92f, 1f, 0.1f, 0.06f, 2f, 0.7f);
                    b.Box(Wood, 0.92f, 1f, 0.1f, 0.06f, 2f, 0.7f);
                    foreach (float y in new[] { 0.05f, 0.65f, 1.3f, 1.95f })
                        b.Box(Wood, 0, y, 0.1f, 1.84f, 0.05f, 0.7f);
                },
            },
            new()
            {
                Id = "plant", Category = Decor, Size = new(1, 1, 1), Build = b =>
                {
                    b.Cylinder(Clay, 0, 0.2f, 0, 0.5f, 0.4f);
                    b.Sphere(Leaf, 0, 0.7f, 0, 0.6f);
                },
            },
            new()
            {
                Id = "lamp", Category = Decor, Size = new(1, 2, 1), Build = b =>
                {
                    b.Cylinder(Metal, 0, 0.03f, 0, 0.4f, 0.06f);
                    b.Cylinder(Metal, 0, 0.8f, 0, 0.05f, 1.5f);
                    b.Cylinder(Light, 0, 1.6f, 0, 0.5f, 0.35f);
                },
            },
            new() { Id = "wall", Category = Structure, Size = new(1, 2, 1), Build = b => b.Box(Stone, 0, 1f, 0, 1f, 2f, 0.15f) },
            new() { Id = "pillar", Category = Structure, Size = new(1, 3, 1), Build = b => b.Cylinder(Stone, 0, 1.5f, 0, 0.5f, 3f) },
            new()
            {
                Id = "fence", Category = Structure, Size = new(2, 1, 1), Build = b =>
                {
                    foreach (float x in new[] { -0.9f, 0f, 0.9f })
                        b.Box(Wood, x, 0.45f, 0, 0.1f, 0.9f, 0.1f);
                    b.Box(Wood, 0, 0.35f, 0, 2f, 0.06f, 0.06f);
                    b.Box(Wood, 0, 0.7f, 0, 2f, 0.06f, 0.06f);
                },
            },
        };

        [MenuItem("Tools/Map Tool/샘플 오브젝트 생성", priority = 20)]
        private static void GenerateMenu()
        {
            int created = Generate(DefaultRoot);
            Debug.Log($"[MapTool] 샘플 오브젝트 {created}개를 만들었습니다 ({DefaultRoot}).");
        }

        /// <returns>새로 만든 정의 수.</returns>
        internal static int Generate(string root)
        {
            EnsureFolder($"{root}/Materials");
            EnsureFolder($"{root}/Prefabs");
            EnsureFolder($"{root}/Definitions");

            int created = 0;
            var materials = new Dictionary<Color, Material>();
            foreach (var spec in Specs)
            {
                string definitionPath = $"{root}/Definitions/{spec.Id}.asset";
                if (AssetDatabase.LoadAssetAtPath<MapObjectDefinition>(definitionPath) != null) continue;

                var prefab = BuildPrefab(spec, root, materials);
                var definition = ScriptableObject.CreateInstance<MapObjectDefinition>();
                definition.Configure(spec.Id, spec.Size, prefab, spec.Category);
                AssetDatabase.CreateAsset(definition, definitionPath);
                created++;
            }

            AssetDatabase.SaveAssets();
            return created;
        }

        private static GameObject BuildPrefab(Spec spec, string root, Dictionary<Color, Material> materials)
        {
            string path = $"{root}/Prefabs/{spec.Id}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(spec.Id);
            try
            {
                spec.Build(new Builder(go.transform, color => GetMaterial(color, root, materials)));
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static Material GetMaterial(Color color, string root, Dictionary<Color, Material> cache)
        {
            if (cache.TryGetValue(color, out var cached)) return cached;

            string path = $"{root}/Materials/Sample_{ColorUtility.ToHtmlStringRGB(color)}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(material, path);
            }
            cache[color] = material;
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private sealed class Builder
        {
            private readonly Transform parent;
            private readonly Func<Color, Material> material;
            private int count;

            public Builder(Transform parent, Func<Color, Material> material)
            {
                this.parent = parent;
                this.material = material;
            }

            public void Box(Color color, float x, float y, float z, float sx, float sy, float sz) =>
                Add(PrimitiveType.Cube, color, new Vector3(x, y, z), new Vector3(sx, sy, sz));

            /// <summary>diameter와 height는 미터. 유니티 실린더는 기본 높이가 2라 Y 스케일을 절반으로 준다.</summary>
            public void Cylinder(Color color, float x, float y, float z, float diameter, float height) =>
                Add(PrimitiveType.Cylinder, color, new Vector3(x, y, z), new Vector3(diameter, height * 0.5f, diameter));

            public void Sphere(Color color, float x, float y, float z, float diameter) =>
                Add(PrimitiveType.Sphere, color, new Vector3(x, y, z), Vector3.one * diameter);

            private void Add(PrimitiveType type, Color color, Vector3 position, Vector3 scale)
            {
                var part = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(part.GetComponent<Collider>());
                part.name = $"{type}_{count++}";
                part.transform.SetParent(parent, false);
                part.transform.localPosition = position;
                part.transform.localScale = scale;
                part.GetComponent<MeshRenderer>().sharedMaterial = material(color);
            }
        }
    }
}
