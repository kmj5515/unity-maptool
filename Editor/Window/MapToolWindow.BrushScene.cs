using System.Collections.Generic;
using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 브러시 모드의 씬뷰 조작.
    ///   그리기 모드: 드래그 = 뿌리기, Shift+드래그 = 지우기
    ///   지우개 모드: 드래그 = 지우기, Shift+드래그 = 뿌리기
    ///   E = 그리기/지우개 전환, [ ] = 반경, Ctrl+휠 = 밀도 (드래그 한 번 = Undo 한 번)
    /// 브러시 원은 지면 법선을 따라 기울고, 중심이 필터를 통과하면 초록·아니면 빨강·구성이 비었으면 회색, 지우개는 주황 점선.
    /// 장식은 ScatterSceneView가 씬 오브젝트로 보여 주고, 지우는 중이면 지워질 장식이 빨간 점으로 강조된다.
    /// 지우개는 장식만 지운다. 칸 오브젝트는 건드리지 않는다.
    /// </summary>
    public sealed partial class MapToolWindow
    {
        private const int BrushCircleSegments = 64;

        private static readonly Color BrushPassColor = new(0.3f, 1f, 0.45f, 1f);
        private static readonly Color BrushFailColor = new(1f, 0.3f, 0.3f, 1f);
        private static readonly Color BrushEmptyColor = new(0.65f, 0.65f, 0.65f, 1f);
        private static readonly Color BrushEraseColor = new(1f, 0.6f, 0.15f, 1f);
        private static readonly Color ScatterEraseDotColor = new(1f, 0.25f, 0.2f, 1f);

        private readonly ScatterBrushStroke brushStroke = new();
        private readonly Vector3[] brushCircleBuffer = new Vector3[BrushCircleSegments * 2];
        private GUIStyle brushLabelStyle;

        private void HandleBrush(SceneView sceneView, Event e, int controlId)
        {
            // 핫 컨트롤을 다른 데서 가져갔으면(창 밖에서 마우스를 뗌 등) 획을 닫는다.
            if (brushStroke.Active && GUIUtility.hotControl != controlId && e.type != EventType.Layout) EndBrushStroke();

            var sampler = SceneSurfaceSampler.ForMap(service, view.Root);
            bool hasPoint = sampler.Pick(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var center, out var normal);
            if (e.type == EventType.MouseLeaveWindow) hasPoint = false;

            bool erasing = brushStroke.Active ? brushStroke.Erasing : brush.WantsErase(e.shift);
            if (hasPoint && e.type == EventType.Repaint) DrawBrushGizmo(center, normal, erasing);

            if (e.type == EventType.ScrollWheel && (e.control || e.command) && !e.alt)
            {
                int direction = e.delta.y < 0f ? 1 : e.delta.y > 0f ? -1 : 0;
                brush.Settings.Density = ScatterBrushStroke.StepDensity(brush.Settings.Density, direction);
                e.Use();
                Repaint();
                sceneView.Repaint();
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown when IsPrimary(e) && hasPoint:
                    if (!erasing && !brush.HasUsableEntries)
                    {
                        sceneView.ShowNotification(new GUIContent("브러시 구성이 비었습니다. 팔레트에서 장식을 담으세요."), 1.5f);
                        e.Use();
                        break;
                    }
                    GUIUtility.hotControl = controlId;
                    service.BeginGroup();
                    brushStroke.Begin(erasing);
                    if (brushStroke.TryStamp(center, brush.Settings.Radius)) BrushStamp(center, sampler);
                    e.Use();
                    break;

                case EventType.MouseDrag when brushStroke.Active && GUIUtility.hotControl == controlId:
                    if (hasPoint && brushStroke.TryStamp(center, brush.Settings.Radius)) BrushStamp(center, sampler);
                    e.Use();
                    break;

                case EventType.MouseUp when brushStroke.Active && GUIUtility.hotControl == controlId:
                    EndBrushStroke();
                    e.Use();
                    break;
            }
        }

        private void BrushStamp(Vector3 center, SceneSurfaceSampler sampler)
        {
            if (brushStroke.Erasing)
            {
                var ids = ScatterBrush.CollectErase(center, brush.Settings.Radius, service, brush.EraseFilter());
                if (ids.Count > 0) Execute(new RemoveScattersCommand(ids));
                return;
            }

            var candidates = ScatterBrush.Generate(center, brush.Settings, brush.ToEntries(), service, sampler.Sample, brushStroke.Random);
            if (candidates.Count > 0) Execute(new AddScattersCommand(candidates));
        }

        /// <summary>획을 닫아 지금까지 찍은 것을 Undo 한 칸으로 올린다. 획이 없으면 아무것도 안 한다.</summary>
        private void EndBrushStroke()
        {
            if (!brushStroke.Active) return;
            brushStroke.End();
            service?.EndGroup();
            if (GUIUtility.hotControl != 0) GUIUtility.hotControl = 0;
        }

        /// <summary>[ ] 단축키. 브러시 모드에서만 반경을 ±10% 바꾼다.</summary>
        internal void BrushRadiusAction(int direction)
        {
            if (!IsEditing || mode != Mode.Brush) return;
            brush.Settings.Radius = ScatterBrushStroke.StepRadius(brush.Settings.Radius, direction);
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>E 단축키. 브러시 모드에서 그리기/지우개를 바꾼다. 드래그 중에는 무시한다(획은 시작한 모드로 끝난다).</summary>
        internal void BrushEraseToggleAction()
        {
            if (!IsEditing || mode != Mode.Brush || brushStroke.Active) return;
            SetBrushEraseMode(!brush.EraseMode);
        }

        private void SetBrushEraseMode(bool erase)
        {
            if (brush.EraseMode == erase) return;
            brush.EraseMode = erase;
            Repaint();
            SceneView.RepaintAll();
        }

        // ───────────────────────── 그리기 ─────────────────────────

        private void DrawBrushGizmo(Vector3 center, Vector3 normal, bool erasing)
        {
            var settings = brush.Settings;
            float radius = settings.Radius;
            bool empty = !erasing && !brush.HasUsableEntries;
            bool passes = ScatterBrush.PassesFilters(center, normal, settings);
            var color = erasing ? BrushEraseColor : empty ? BrushEmptyColor : passes ? BrushPassColor : BrushFailColor;

            // 지면에 살짝 띄워 깜빡임(z-fighting)을 막는다.
            var up = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            var lifted = center + up * 0.03f;

            if (erasing) DrawEraseTargets(center, radius);

            using (new Handles.DrawingScope(color))
            {
                if (erasing) DrawDottedCircle(lifted, up, radius);
                else Handles.DrawWireDisc(lifted, up, radius, 2f);
                Handles.DrawLine(lifted, lifted + up * Mathf.Min(radius * 0.3f, 1f));
            }
            using (new Handles.DrawingScope(new Color(color.r, color.g, color.b, 0.08f)))
                Handles.DrawSolidDisc(lifted, up, radius);

            brushLabelStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel) { richText = false };
            brushLabelStyle.normal.textColor = color;
            // 끝에 Shift가 무엇을 하는지 적는다(지금 모드의 반대). 드래그 중에는 뺀다.
            string shiftHint = brushStroke.Active ? "" : erasing ? " · Shift=그리기" : " · Shift=지우개";
            bool nothingToErase = erasing && brush.EraseFilter() is { Count: 0 };
            string text = erasing ? $"지우개 · 반경 {radius:0.0}m{(nothingToErase ? " · 지울 종류 없음(구성이 비었음)" : "")}"
                : empty ? "구성 비어 있음"
                : $"반경 {radius:0.0}m · 밀도 {settings.Density:0.##}/㎡{(passes ? "" : " · 필터 밖")}";
            text += shiftHint;
            Handles.Label(lifted + Vector3.Cross(up, Vector3.forward).normalized * -radius + up * 0.2f, text, brushLabelStyle);
        }

        private void DrawDottedCircle(Vector3 center, Vector3 normal, float radius)
        {
            var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.99f ? Vector3.forward : Vector3.up).normalized;
            var bitangent = Vector3.Cross(normal, tangent);
            for (int i = 0; i < BrushCircleSegments; i++)
            {
                float a0 = i * Mathf.PI * 2f / BrushCircleSegments;
                float a1 = (i + 1) * Mathf.PI * 2f / BrushCircleSegments;
                brushCircleBuffer[i * 2] = center + (tangent * Mathf.Cos(a0) + bitangent * Mathf.Sin(a0)) * radius;
                brushCircleBuffer[i * 2 + 1] = center + (tangent * Mathf.Cos(a1) + bitangent * Mathf.Sin(a1)) * radius;
            }
            Handles.DrawDottedLines(brushCircleBuffer, 4f);
        }

        /// <summary>
        /// 지우개일 때 지워질 장식을 빨간 점으로 그린다. 장식 자체는 씬 오브젝트(ScatterSceneView)로 보인다.
        /// </summary>
        private void DrawEraseTargets(Vector3 center, float radius)
        {
            var ids = ScatterBrush.CollectErase(center, radius, service, brush.EraseFilter());
            if (ids.Count == 0) return;

            var grid = service.Map.Grid;
            Handles.color = ScatterEraseDotColor;
            foreach (int id in ids)
            {
                if (!service.Map.TryGetScatter(id, out var scatter)) continue;
                var position = scatter.WorldPosition(grid);
                Handles.DotHandleCap(0, position, Quaternion.identity, HandleUtility.GetHandleSize(position) * 0.045f, EventType.Repaint);
            }
            Handles.color = Color.white;
        }
    }
}
