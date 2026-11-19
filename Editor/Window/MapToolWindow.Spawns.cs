using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 스폰 모드: 창의 스폰 목록(이름·기본 지정·여기서 플레이·삭제)과 씬뷰 배치·선택·이동.
    /// 빈 칸 클릭 = 그 자리에 바로 배치(미리보기 화살표가 커서를 따라다니고 R로 방향을 바꾼다),
    /// 스폰 클릭 = 선택, 드래그 = 이동(놓을 때 커맨드 하나로 커밋).
    /// 스폰 화살표는 모든 모드에서 보이고, 기본 스폰은 다른 색 + ★로 그린다.
    /// </summary>
    public sealed partial class MapToolWindow
    {
        private static readonly Color SpawnColor = new(0.3f, 0.95f, 0.85f, 1f);
        private static readonly Color DefaultSpawnColor = new(1f, 0.45f, 0.9f, 1f);
        private const string DefaultMark = "★";

        [SerializeField] private GridRotation spawnRotation;
        [SerializeField] private Vector2 spawnListScroll;

        private int selectedSpawnId;
        private bool spawnDragging;
        private Vector3Int spawnDragTarget;
        private int detailSpawnId;

        private int DefaultSpawnIdOrZero => service.Map.TryGetDefaultSpawn(out var spawn) ? spawn.Id : 0;

        // ───────────────────────── 창 패널 ─────────────────────────

        private void DrawSpawnPanel()
        {
            var spawns = service.Map.Spawns;
            EditorGUILayout.LabelField($"스폰 지점 ({spawns.Count})", EditorStyles.boldLabel);
            if (spawns.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"씬뷰에서 칸을 클릭하면 그 자리에 스폰 지점이 생깁니다. R = 방향 돌리기.\n첫 지점이 플레이 시작 지점({DefaultMark} 기본)이 됩니다.",
                    MessageType.Info);
                return;
            }

            int defaultId = DefaultSpawnIdOrZero;
            spawnListScroll = EditorGUILayout.BeginScrollView(spawnListScroll, GUILayout.MaxHeight(180f));
            foreach (var spawn in spawns) DrawSpawnRow(spawn, spawn.Id == defaultId);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            if (selectedSpawnId != 0 && service.Map.TryGetSpawn(selectedSpawnId, out var selected))
                DrawSpawnDetail(selected, selected.Id == defaultId);
            else
                EditorGUILayout.LabelField($"빈 칸 클릭 = 새 스폰 · 스폰 클릭 = 선택 · 드래그 = 이동 · R = 방향 · {DefaultMark} = 기본 지정 · ▶ = 그 지점에서 플레이 · 목록 더블클릭 = 씬뷰에서 보기",
                    EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawSpawnRow(PlacedSpawn spawn, bool isDefault)
        {
            bool isSelected = spawn.Id == selectedSpawnId;
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            if (isSelected) EditorGUI.DrawRect(rect, new Color(SelectColor.r, SelectColor.g, SelectColor.b, 0.25f));

            // 버튼을 먼저 처리해야 버튼 클릭이 행 클릭으로 새지 않는다.
            var starRect = new Rect(rect.x, rect.y, 22f, rect.height);
            var deleteRect = new Rect(rect.xMax - 22f, rect.y, 22f, rect.height);
            var playRect = new Rect(deleteRect.x - 24f, rect.y, 22f, rect.height);
            var previous = GUI.contentColor;
            if (isDefault) GUI.contentColor = DefaultSpawnColor;
            bool makeDefault = GUI.Button(starRect,
                new GUIContent(isDefault ? DefaultMark : "☆", isDefault ? "플레이를 시작할 기본 스폰입니다." : "이 지점을 기본 스폰으로 지정"),
                EditorStyles.miniButton);
            GUI.contentColor = previous;
            if (makeDefault && !isDefault)
            {
                Execute(new SetDefaultSpawnCommand(spawn.Id));
                GUIUtility.ExitGUI();
            }
            using (new EditorGUI.DisabledScope(!PlayFromSpawn.CanPlay))
            {
                if (GUI.Button(playRect, new GUIContent("▶", "이 지점에서 플레이 (기본 스폰은 그대로)"), EditorStyles.miniButton))
                    PlaySpawn(spawn);
            }
            if (GUI.Button(deleteRect, new GUIContent("×", "이 스폰 지점 삭제"), EditorStyles.miniButton))
            {
                if (Execute(new RemoveSpawnCommand(spawn.Id)).IsValid && selectedSpawnId == spawn.Id) selectedSpawnId = 0;
                GUIUtility.ExitGUI();
            }

            var labelRect = new Rect(starRect.xMax + 4f, rect.y, playRect.x - starRect.xMax - 8f, rect.height);
            var c = spawn.Cell;
            GUI.Label(labelRect, new GUIContent($"{spawn.Name}   ({c.x}, {c.y}, {c.z}) · {FacingLabel(spawn.Rotation)}", "클릭 = 선택 · 더블클릭 = 씬뷰에서 보기"));

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && labelRect.Contains(e.mousePosition))
            {
                selectedSpawnId = spawn.Id;
                if (e.clickCount == 2) FrameSpawn(spawn);
                GUI.FocusControl(null);
                e.Use();
                Repaint();
                SceneView.RepaintAll();
            }
        }

        private void DrawSpawnDetail(PlacedSpawn spawn, bool isDefault)
        {
            EditorGUILayout.LabelField($"선택한 스폰 @{spawn.Id}{(isDefault ? $"  {DefaultMark} 기본" : "")}", EditorStyles.boldLabel);

            // 선택이 바뀐 첫 그리기에서는 입력 필드 결과를 버린다(존 패널과 같은 이유: 이전 스폰의 이름이 새 스폰에 들어가는 것 방지).
            bool fresh = detailSpawnId != spawn.Id;
            if (fresh)
            {
                detailSpawnId = spawn.Id;
                EditorGUIUtility.editingTextField = false;
            }

            string newName = EditorGUILayout.DelayedTextField("이름", spawn.Name);
            if (!fresh && newName != spawn.Name && !string.IsNullOrWhiteSpace(newName))
                Execute(new ModifySpawnCommand(spawn.WithName(newName.Trim())));

            EditorGUILayout.LabelField("위치", $"셀 {spawn.Cell}");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("방향", FacingLabel(spawn.Rotation));
                if (GUILayout.Button("회전 (R)", GUILayout.Width(70f))) RotateAction();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(isDefault))
                {
                    if (GUILayout.Button(new GUIContent($"{DefaultMark} 기본으로 지정", "플레이를 시작하면 플레이어가 이 지점에 섭니다.")))
                    {
                        Execute(new SetDefaultSpawnCommand(spawn.Id));
                        GUIUtility.ExitGUI();
                    }
                }
                if (GUILayout.Button("씬뷰에서 보기")) FrameSpawn(spawn);
                if (GUILayout.Button("삭제 (Del)"))
                {
                    DeleteAction();
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUI.DisabledScope(!PlayFromSpawn.CanPlay))
            {
                if (GUILayout.Button(new GUIContent("▶ 여기서 플레이", "플레이어를 이 지점에 세우고 플레이합니다. 기본 스폰은 바뀌지 않습니다."),
                        GUILayout.Height(24f)))
                    PlaySpawn(spawn);
            }
        }

        private void PlaySpawn(PlacedSpawn spawn)
        {
            // 플레이 진입은 도메인 리로드를 부르므로 이번 GUI 그리기를 여기서 끝낸다.
            if (PlayFromSpawn.Play(spawn.Id, $"'{spawn.Name}'")) GUIUtility.ExitGUI();
        }

        /// <summary>"90° (+X)"처럼 각도와 바라보는 축을 함께 보여 준다.</summary>
        private static string FacingLabel(GridRotation rotation) => rotation switch
        {
            GridRotation.R90 => "90° (+X)",
            GridRotation.R180 => "180° (−Z)",
            GridRotation.R270 => "270° (−X)",
            _ => "0° (+Z)",
        };

        /// <summary>씬뷰를 스폰 지점으로 옮기고 층을 그 지점에 맞춘다(바로 드래그할 수 있게).</summary>
        private void FrameSpawn(PlacedSpawn spawn)
        {
            layer = Mathf.Clamp(spawn.Cell.y, 0, service.Map.Size.y - 1);
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;

            var grid = service.Map.Grid;
            var center = spawn.WorldPosition(grid) + Vector3.up * (grid.CellSize.y * 0.5f);
            sceneView.Frame(new Bounds(center, grid.CellSize * 3f), false);
            SceneView.RepaintAll();
        }

        private string NextSpawnName()
        {
            var names = new HashSet<string>(service.Map.Spawns.Select(s => s.Name));
            int n = service.Map.Spawns.Count + 1;
            while (names.Contains($"스폰 {n}")) n++;
            return $"스폰 {n}";
        }

        // ───────────────────────── 씬뷰 ─────────────────────────

        /// <summary>현재 층의 그 칸에 있는 스폰. 없으면 0.</summary>
        private int SpawnAt(Vector3Int cell)
        {
            foreach (var spawn in service.Map.Spawns)
            {
                if (spawn.Cell == cell) return spawn.Id;
            }
            return 0;
        }

        private void HandleSpawn(Event e, int controlId, bool hasCell, Vector3Int cell)
        {
            int hovered = hasCell ? SpawnAt(cell) : 0;

            switch (e.type)
            {
                case EventType.MouseDown when IsPrimary(e) && hasCell:
                    GUIUtility.hotControl = controlId;
                    if (hovered != 0 && service.Map.TryGetSpawn(hovered, out var grabbed))
                    {
                        selectedSpawnId = hovered;
                        spawnDragging = true;
                        spawnDragTarget = grabbed.Cell;
                    }
                    else
                    {
                        var command = new AddSpawnCommand(new PlacedSpawn(0, NextSpawnName(), cell, spawnRotation));
                        if (Execute(command).IsValid) selectedSpawnId = command.Added.Id;
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when spawnDragging && GUIUtility.hotControl == controlId:
                    if (hasCell)
                    {
                        // 맵 밖으로 끌어도 실패하지 않게 가장자리에 붙인다.
                        var size = service.Map.Size;
                        spawnDragTarget = new Vector3Int(Mathf.Clamp(cell.x, 0, size.x - 1), cell.y, Mathf.Clamp(cell.z, 0, size.z - 1));
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    if (spawnDragging && service.Map.TryGetSpawn(selectedSpawnId, out var moving) && spawnDragTarget != moving.Cell)
                        Execute(new ModifySpawnCommand(moving.WithCell(spawnDragTarget)));
                    spawnDragging = false;
                    e.Use();
                    Repaint();
                    break;
            }

            if (e.type != EventType.Repaint) return;

            if (selectedSpawnId != 0 && service.Map.TryGetSpawn(selectedSpawnId, out var selected))
            {
                var shownCell = spawnDragging ? spawnDragTarget : selected.Cell;
                DrawFootprint(new BoundsInt(shownCell, Vector3Int.one), spawnDragging ? ValidColor : SelectColor);
            }

            if (spawnDragging || !hasCell) return;
            if (hovered != 0)
            {
                if (hovered != selectedSpawnId) DrawFootprint(new BoundsInt(cell, Vector3Int.one), HoverColor);
                return;
            }

            // 빈 칸: 놓일 화살표 미리보기
            var preview = new PlacedSpawn(0, "", cell, spawnRotation);
            var check = service.CanPlaceSpawn(preview);
            bool valid = check.IsValid;
            var color = valid ? ValidColor : InvalidColor;
            DrawFootprint(new BoundsInt(cell, Vector3Int.one), color);
            // 놓을 수 없으면 클릭 전에 이유를 보여 준다.
            DrawSpawnMarker(preview.WorldPosition(service.Map.Grid), preview.WorldRotation, color, valid ? $"새 스폰 · {FacingLabel(spawnRotation)} (R)" : check.Message);
        }

        /// <summary>모든 스폰을 화살표로 그린다. 스폰 모드에서 끌고 있는 지점은 끌리는 자리에 그린다.</summary>
        private void DrawSpawns()
        {
            if (Event.current.type != EventType.Repaint) return;

            var grid = service.Map.Grid;
            int defaultId = DefaultSpawnIdOrZero;
            bool spawnMode = mode == Mode.Spawn;
            foreach (var spawn in service.Map.Spawns)
            {
                bool isDefault = spawn.Id == defaultId;
                bool isSelected = spawnMode && spawn.Id == selectedSpawnId;
                var shown = isSelected && spawnDragging ? spawn.WithCell(spawnDragTarget) : spawn;

                var color = isSelected ? SelectColor : isDefault ? DefaultSpawnColor : SpawnColor;
                string label = isDefault ? $"{DefaultMark} {spawn.Name}" : spawn.Name;
                DrawSpawnMarker(shown.WorldPosition(grid), shown.WorldRotation, color, label);
            }
        }

        /// <summary>바닥 원 + 바라보는 쪽 화살표 + 기둥 + 이름표.</summary>
        private void DrawSpawnMarker(Vector3 position, Quaternion rotation, Color color, string label)
        {
            var cellSize = service.Map.Grid.CellSize;
            float radius = Mathf.Min(cellSize.x, cellSize.z) * 0.4f;
            float poleHeight = cellSize.y * 0.6f;
            var ground = position + Vector3.up * 0.02f;
            var forward = rotation * Vector3.forward;
            var right = rotation * Vector3.right;

            using (new Handles.DrawingScope(new Color(color.r, color.g, color.b, 0.18f)))
                Handles.DrawSolidDisc(ground, Vector3.up, radius);

            using (new Handles.DrawingScope(color))
            {
                Handles.DrawWireDisc(ground, Vector3.up, radius, 2f);
                Handles.DrawAAConvexPolygon(
                    ground + forward * radius * 1.15f,
                    ground - forward * radius * 0.35f + right * radius * 0.55f,
                    ground - forward * radius * 0.35f - right * radius * 0.55f);
                Handles.DrawLine(ground, ground + Vector3.up * poleHeight, 2f);
            }

            if (string.IsNullOrEmpty(label)) return;
            var style = new GUIStyle(EditorStyles.whiteBoldLabel) { normal = { textColor = color } };
            Handles.Label(ground + Vector3.up * (poleHeight + 0.15f), label, style);
        }
    }
}
