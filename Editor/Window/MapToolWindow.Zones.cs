using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 존 모드의 창 패널(목록·이름·프로필·우선순위·상자)과 씬뷰 면 핸들.
    /// 면 핸들은 드래그하는 동안 미리보기만 바꾸고, 놓을 때 ModifyZoneCommand 하나로 커밋해 Ctrl+Z 한 번에 되돌린다.
    /// </summary>
    public sealed partial class MapToolWindow
    {
        private static readonly int FaceHandleHash = "MapToolZoneFaceHandle".GetHashCode();
        private static readonly Color[] AxisColors =
        {
            new(0.95f, 0.35f, 0.3f, 1f), new(0.45f, 0.9f, 0.35f, 1f), new(0.35f, 0.6f, 1f, 1f),
        };
        private static readonly string[] AxisNames = { "X", "Y", "Z" };

        [SerializeField] private Vector2 zoneListScroll;

        private bool zoneResizing;
        private PlacedZone zoneResizeBefore;
        private PlacedZone zoneResizePending;
        private Vector3 zoneResizeStart;
        private int detailZoneId;

        // ───────────────────────── 창 패널 ─────────────────────────

        private void DrawZonePanel()
        {
            var zones = service.Map.Zones;
            EditorGUILayout.LabelField($"존 목록 ({zones.Count})", EditorStyles.boldLabel);
            if (zones.Count == 0)
            {
                EditorGUILayout.HelpBox("씬뷰에서 드래그하면 새 존이 생깁니다.", MessageType.Info);
                return;
            }

            zoneListScroll = EditorGUILayout.BeginScrollView(zoneListScroll, GUILayout.MaxHeight(180f));
            foreach (var zone in zones.OrderByDescending(z => z.Priority).ThenBy(z => z.Name))
                DrawZoneRow(zone);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            if (selectedZoneId != 0 && service.Map.TryGetZone(selectedZoneId, out var selected))
                DrawZoneDetail(selected);
            else
                EditorGUILayout.LabelField("드래그 = 새 존 · 클릭 = 선택 · 목록 더블클릭 = 씬뷰에서 보기", EditorStyles.miniLabel);
        }

        private void DrawZoneRow(PlacedZone zone)
        {
            bool isSelected = zone.Id == selectedZoneId;
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            if (isSelected) EditorGUI.DrawRect(rect, new Color(SelectColor.r, SelectColor.g, SelectColor.b, 0.25f));

            var swatch = new Rect(rect.x + 2f, rect.y + 4f, 8f, rect.height - 8f);
            EditorGUI.DrawRect(swatch, isSelected ? SelectColor : ZoneColor);

            var state = GetProfileState(zone.ProfileId, out string profileName);
            var labelRect = new Rect(rect.x + 14f, rect.y, rect.width - 14f, rect.height);
            string tooltip = state switch
            {
                ProfileState.Empty => "프로필이 지정되지 않아 소리가 나지 않습니다.",
                ProfileState.Missing => "프로필 에셋을 찾을 수 없습니다(지워졌거나 옮겨지지 않았는지 확인).",
                _ => profileName,
            };
            var content = new GUIContent(
                state == ProfileState.Ok ? $"{zone.Name}   {profileName} · p{zone.Priority}" : $"{zone.Name}   p{zone.Priority}",
                state == ProfileState.Ok ? null : EditorGUIUtility.IconContent("console.warnicon.sml").image,
                tooltip);
            GUI.Label(labelRect, content);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                selectedZoneId = zone.Id;
                if (e.clickCount == 2) FrameZone(zone);
                GUI.FocusControl(null);
                e.Use();
                Repaint();
                SceneView.RepaintAll();
            }
        }

        private void DrawZoneDetail(PlacedZone zone)
        {
            EditorGUILayout.LabelField($"선택한 존 #{zone.Id}", EditorStyles.boldLabel);

            // 선택이 바뀐 첫 그리기에서는 입력 필드 결과를 버린다.
            // 지연 필드는 포커스를 잃을 때 값을 돌려주는데, 그 순간 이미 다른 존이 선택돼 있으면 이전 존의 글자가 새 존에 들어간다.
            bool fresh = detailZoneId != zone.Id;
            if (fresh)
            {
                detailZoneId = zone.Id;
                EditorGUIUtility.editingTextField = false;
            }

            string newName = EditorGUILayout.DelayedTextField("이름", zone.Name);
            if (!fresh && newName != zone.Name && !string.IsNullOrWhiteSpace(newName))
                Execute(new ModifyZoneCommand(zone.WithName(newName.Trim())));

            DrawZoneProfileField(zone, fresh);

            int newPriority = EditorGUILayout.DelayedIntField(new GUIContent("우선순위", "겹친 곳에서는 높은 존이 이깁니다. 같으면 작은 존."), zone.Priority);
            if (!fresh && newPriority != zone.Priority)
                Execute(new ModifyZoneCommand(zone.WithPriority(newPriority)));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("상자", EditorStyles.miniBoldLabel);
            for (int i = 0; i < zone.Boxes.Count; i++)
            {
                var box = zone.Boxes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{i + 1}. 위치 {box.Min} · 크기 {box.Size.x}×{box.Size.y}×{box.Size.z}", EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(zone.Boxes.Count <= 1))
                    {
                        var tooltip = zone.Boxes.Count <= 1 ? "마지막 상자는 지울 수 없습니다. 존 삭제를 쓰세요." : "이 상자 삭제";
                        if (GUILayout.Button(new GUIContent("×", tooltip), EditorStyles.miniButton, GUILayout.Width(22f)))
                        {
                            int removeAt = i;
                            Execute(new ModifyZoneCommand(zone.WithBoxes(zone.Boxes.Where((_, index) => index != removeAt))));
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            EditorGUILayout.LabelField("Shift+드래그 = 상자 추가 · 면 핸들 드래그 = 크기 조절", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("씬뷰에서 보기")) FrameZone(zone);
                if (GUILayout.Button("존 삭제 (Del)")) DeleteAction();
            }
        }

        internal enum ProfileState { Empty, Missing, Ok }

        /// <summary>연결점이 없으면 ID를 그대로 이름으로 쓰고 Ok로 본다(확인할 방법이 없다).</summary>
        internal static ProfileState GetProfileState(string profileId, out string displayName)
        {
            displayName = profileId;
            if (string.IsNullOrEmpty(profileId)) return ProfileState.Empty;

            var bridge = ZoneBridge.Current;
            if (bridge == null) return ProfileState.Ok;
            foreach (var option in bridge.GetProfiles())
            {
                if (option.Id != profileId) continue;
                displayName = option.DisplayName;
                return ProfileState.Ok;
            }
            return ProfileState.Missing;
        }

        private void DrawZoneProfileField(PlacedZone zone, bool fresh)
        {
            var bridge = ZoneBridge.Current;
            if (bridge == null)
            {
                // 연결 어셈블리가 없을 때: ID를 직접 입력한다.
                string typed = EditorGUILayout.DelayedTextField(new GUIContent("프로필 ID"), zone.ProfileId);
                if (!fresh && typed != zone.ProfileId) Execute(new ModifyZoneCommand(zone.WithProfile(typed.Trim())));
                if (string.IsNullOrEmpty(zone.ProfileId))
                    EditorGUILayout.HelpBox("프로필이 없으면 이 존에서 소리가 바뀌지 않습니다.", MessageType.Warning);
                return;
            }

            var profiles = bridge.GetProfiles();
            var labels = new List<GUIContent> { new("(없음)") };
            int current = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                labels.Add(new GUIContent(profiles[i].DisplayName));
                if (profiles[i].Id == zone.ProfileId) current = i + 1;
            }
            bool missing = current == 0 && !string.IsNullOrEmpty(zone.ProfileId);
            if (missing)
            {
                labels.Add(new GUIContent("(찾을 수 없는 프로필)"));
                current = labels.Count - 1;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                int picked = EditorGUILayout.Popup(new GUIContent(bridge.ProfileLabel), current, labels.ToArray());
                if (picked != current)
                {
                    string id = picked == 0 ? string.Empty : picked <= profiles.Count ? profiles[picked - 1].Id : zone.ProfileId;
                    if (id != zone.ProfileId) Execute(new ModifyZoneCommand(zone.WithProfile(id)));
                }

                using (new EditorGUI.DisabledScope(current == 0 || missing))
                {
                    if (GUILayout.Button(new GUIContent("보기", "프로젝트 창에서 이 프로필 에셋을 가리킵니다."), EditorStyles.miniButton, GUILayout.Width(36f)))
                        bridge.PingProfile(zone.ProfileId);
                }
                if (GUILayout.Button(new GUIContent("새로…", "새 프로필 에셋을 만들어 이 존에 지정합니다."), EditorStyles.miniButton, GUILayout.Width(44f)))
                {
                    string created = bridge.CreateProfile(zone.Name);
                    if (!string.IsNullOrEmpty(created)) Execute(new ModifyZoneCommand(zone.WithProfile(created)));
                    GUIUtility.ExitGUI();
                }
            }

            if (missing)
                EditorGUILayout.HelpBox("지정된 프로필 에셋을 찾을 수 없습니다. 지워졌는지 확인하고 다시 고르세요.", MessageType.Error);
            else if (current == 0)
                EditorGUILayout.HelpBox(profiles.Count == 0
                    ? "프로젝트에 프로필이 없습니다. '새로…'로 만드세요."
                    : "프로필이 없으면 이 존에서 소리가 바뀌지 않습니다.", MessageType.Warning);
        }

        private void FrameZone(PlacedZone zone)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || zone.Boxes.Count == 0) return;

            var grid = service.Map.Grid;
            var bounds = zone.Bounds;
            var min = grid.CellToWorld(bounds.min);
            var max = grid.CellToWorld(bounds.max);
            sceneView.Frame(new Bounds((min + max) * 0.5f, max - min), false);
        }

        // ───────────────────────── 씬뷰 면 핸들 ─────────────────────────

        /// <summary>선택한 존의 모든 상자에 면마다 핸들을 그린다. 드래그·클릭 처리(HandleZone)보다 먼저 불러야 핸들이 이벤트를 먼저 받는다.</summary>
        private void HandleZoneFaces()
        {
            if (zoneDragging || selectedZoneId == 0 || !service.Map.TryGetZone(selectedZoneId, out var zone))
            {
                zoneResizing = false;
                return;
            }

            if (zoneResizing && zoneResizeBefore.Id != zone.Id) zoneResizing = false;
            var shown = zoneResizing ? zoneResizePending : zone;
            var grid = service.Map.Grid;

            for (int b = 0; b < shown.Boxes.Count; b++)
            {
                var bounds = shown.Boxes[b].Bounds;
                var min = grid.CellToWorld(bounds.min);
                var max = grid.CellToWorld(bounds.max);
                var center = (min + max) * 0.5f;

                for (int axis = 0; axis < 3; axis++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        bool maxSide = side == 1;
                        int id = GUIUtility.GetControlID(FaceHandleHash, FocusType.Passive);

                        var faceCenter = center;
                        faceCenter[axis] = maxSide ? max[axis] : min[axis];
                        var direction = Vector3.zero;
                        direction[axis] = maxSide ? 1f : -1f;
                        float size = HandleUtility.GetHandleSize(faceCenter) * 0.09f;

                        using (new Handles.DrawingScope(AxisColors[axis]))
                        {
                            EditorGUI.BeginChangeCheck();
                            var moved = Handles.Slider(id, faceCenter, direction, size, Handles.CubeHandleCap, 0f);
                            if (EditorGUI.EndChangeCheck())
                            {
                                if (!zoneResizing)
                                {
                                    zoneResizing = true;
                                    zoneResizeBefore = zone;
                                    zoneResizeStart = faceCenter;
                                }
                                float distance = Vector3.Dot(moved - zoneResizeStart, direction);
                                int cells = Mathf.RoundToInt(distance / grid.CellSize[axis]);
                                zoneResizePending = ResizedZone(zoneResizeBefore, b, axis, maxSide, cells);
                            }
                        }

                        if (zoneResizing && GUIUtility.hotControl == id && Event.current.type == EventType.Repaint)
                        {
                            var label = $"{AxisNames[axis]} {bounds.size[axis]}칸";
                            Handles.Label(faceCenter, label, EditorStyles.whiteBoldLabel);
                        }
                    }
                }
            }

            // 핸들을 놓으면 hotControl이 0으로 돌아온다. 그때 한 번만 커밋한다.
            if (zoneResizing && GUIUtility.hotControl == 0)
            {
                zoneResizing = false;
                if (zoneResizePending != zoneResizeBefore) Execute(new ModifyZoneCommand(zoneResizePending));
            }
        }

        private PlacedZone ResizedZone(PlacedZone zone, int boxIndex, int axis, bool maxSide, int cells)
        {
            var mapSize = service.Map.Size;
            return zone.WithBoxes(zone.Boxes.Select((box, i) =>
                i == boxIndex ? box.WithFaceMoved(axis, maxSide, cells, mapSize) : box));
        }

        /// <summary>씬뷰에 보여 줄 선택 존. 크기 조절 중이면 미리보기 값.</summary>
        private bool TryGetDisplayedSelectedZone(out PlacedZone zone)
        {
            if (!service.Map.TryGetZone(selectedZoneId, out zone)) return false;
            if (zoneResizing && zoneResizeBefore.Id == zone.Id) zone = zoneResizePending;
            return true;
        }
    }
}
