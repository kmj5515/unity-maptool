using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MapTool.Editor
{
    /// <summary>
    /// 배치 전에 커서 위치에 보여 주는 반투명 프리팹. 놓을 수 있으면 초록, 없으면 빨강.
    /// 씬에 저장되지 않고 계층 창에도 보이지 않는다(HideAndDontSave).
    /// </summary>
    internal sealed class GhostPreview : IDisposable
    {
        private static readonly Color ValidColor = new(0.3f, 1f, 0.45f, 0.35f);
        private static readonly Color InvalidColor = new(1f, 0.3f, 0.3f, 0.35f);

        private GameObject instance;
        private MapObjectDefinition source;
        private Material material;

        public void Show(MapObjectDefinition definition, Pose pose, bool valid)
        {
            if (definition == null || definition.Prefab == null)
            {
                Hide();
                return;
            }

            if (instance == null || source != definition) Rebuild(definition);
            instance.transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (!instance.activeSelf) instance.SetActive(true);
            material.color = valid ? ValidColor : InvalidColor;
        }

        public void Hide()
        {
            if (instance != null && instance.activeSelf) instance.SetActive(false);
        }

        public void Dispose()
        {
            DestroyInstance();
            if (material != null) Object.DestroyImmediate(material);
            material = null;
        }

        private void Rebuild(MapObjectDefinition definition)
        {
            DestroyInstance();
            EnsureMaterial();

            instance = Object.Instantiate(definition.Prefab);
            instance.name = "MapTool Ghost";
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            source = definition;
        }

        private void EnsureMaterial()
        {
            if (material != null) return;

            // 렌더 파이프라인과 관계없이 그려지는 단색 셰이더(Handles가 쓰는 것). 알파 블렌딩으로 반투명하게 쓴다.
            material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Back);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private void DestroyInstance()
        {
            if (instance != null) Object.DestroyImmediate(instance);
            instance = null;
            source = null;
        }
    }
}
