using System.Collections.Generic;
using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 맵 검증: 툴바 배지 + 접히는 목록 + 씬뷰 표시.
    /// 맵을 열 때와 편집할 때마다 다시 검사한다(편집 직후가 아니라 다음에 그릴 때 — 한 프레임에 편집이 여러 번 몰려도 한 번만).
    /// 목록 행을 클릭하면 씬뷰가 그곳으로 가고 대상을 선택한다(오브젝트 → 선택 모드, 존 → 존 모드, 스폰 → 스폰 모드, 장식은 이동만).
    /// 씬뷰에는 오류를 항상 빨간 상자로, 경고는 목록을 펼쳤을 때만 노란 상자로 그린다.
    /// </summary>
    public sealed partial class MapToolWindow
    {
        private static readonly Color WarningColor = new(1f, 0.8f, 0.2f, 1f);
        private const float ValidationListHeight = 160f;

        [SerializeField] private bool validationExpanded;
        [SerializeField] private Vector2 validationScroll;

        private const float FixButtonWidth = 72f;

        private List<ValidationIssue> issues = new();
        private readonly List<IssueQuickFix> issueFixes = new(); // issues와 같은 순서. 수정이 없으면 null
        private bool validationDirty = true;

        private enum RowAction { None, Focus, Fix }
        private (IssueTarget target, string code, int id)? focusedIssue;
        private GUIStyle issueRowStyle;

        private int errorCount;
        private int warningCount;

        /// <summary>다음에 그릴 때 다시 검사한다.</summary>
        private void InvalidateValidation()
        {
            validationDirty = true;
        }

        private void EnsureValidated()
        {
            if (!validationDirty) return;
            validationDirty = false;

            issueFixes.Clear();
            if (service == null || catalog == null)
            {
                issues.Clear();
                errorCount = warningCount = 0;
                return;
            }

            issues = EditorMapValidator.Validate(service.Map, catalog);
            errorCount = warningCount = 0;
            foreach (var issue in issues)
            {
                if (issue.IsError) errorCount++;
                else warningCount++;
                issueFixes.Add(IssueQuickFix.Create(issue, service.Map));
            }

            // 고친 문제는 강조를 풀고, 남아 있으면 유지한다.
            if (focusedIssue.HasValue && !issues.Exists(i => Key(i) == focusedIssue.Value)) focusedIssue = null;
        }

        private static (IssueTarget, string, int) Key(ValidationIssue issue) => (issue.Target, issue.Code, issue.TargetId);

        // ───────────────────────── 창 ─────────────────────────

        /// <summary>툴바의 검증 버튼. 오류·경고 수를 배지로 보여 주고, 누르면 다시 검사하며 목록을 펼치거나 접는다.</summary>
        private void DrawValidationButton()
        {
            EnsureValidated();

            string text;
            Texture icon = null;
            if (errorCount > 0)
            {
                icon = EditorGUIUtility.IconContent("console.erroricon.sml").image;
                text = $"검증 {errorCount}" + (warningCount > 0 ? $" · 경고 {warningCount}" : "");
            }
            else if (warningCount > 0)
            {
                icon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
                text = $"검증 {warningCount}";
            }
            else
            {
                text = "검증 ✓";
            }
            var content = new GUIContent(text, icon,
                $"오류 {errorCount}개 · 경고 {warningCount}개. 누르면 다시 검사하고 목록을 펼치거나 접습니다.");

            bool expanded = GUILayout.Toggle(validationExpanded, content, EditorStyles.toolbarButton);
            if (expanded != validationExpanded)
            {
                validationExpanded = expanded;
                InvalidateValidation();
                SceneView.RepaintAll(); // 경고 상자는 펼쳤을 때만 그린다
            }
        }

        private void DrawValidationPanel()
        {
            if (!validationExpanded) return;
            EnsureValidated();

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("검증: 문제가 없습니다.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"검증 결과 — 오류 {errorCount}개 · 경고 {warningCount}개 (클릭 = 씬뷰에서 보기 · 줄 끝 버튼(삭제 등) = 빠른 수정, Ctrl+Z로 되돌림)", EditorStyles.miniBoldLabel);

            issueRowStyle ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
            float height = Mathf.Min(ValidationListHeight, issues.Count * (EditorGUIUtility.singleLineHeight + 4f) + 4f);
            validationScroll = EditorGUILayout.BeginScrollView(validationScroll, GUILayout.Height(height));
            for (int i = 0; i < issues.Count; i++)
            {
                var fix = i < issueFixes.Count ? issueFixes[i] : null;
                switch (DrawIssueRow(issues[i], fix))
                {
                    case RowAction.Focus:
                        FocusIssue(issues[i]);
                        GUIUtility.ExitGUI(); // 모드가 바뀌어 같은 이벤트에서 레이아웃이 달라지므로 여기서 끊는다
                        break;
                    case RowAction.Fix:
                        ApplyQuickFix(issues[i], fix);
                        GUIUtility.ExitGUI(); // 목록이 바뀌므로 여기서 끊는다
                        break;
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private RowAction DrawIssueRow(ValidationIssue issue, IssueQuickFix fix)
        {
            var icon = EditorGUIUtility.IconContent(issue.IsError ? "console.erroricon.sml" : "console.warnicon.sml").image;
            var content = new GUIContent(issue.Message, icon, issue.Message);
            float buttonSpace = fix != null ? FixButtonWidth + 4f : 0f;
            float width = Mathf.Max(100f, position.width - 40f);
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight + 2f, issueRowStyle.CalcHeight(content, width - buttonSpace));
            var rowRect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
            var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - buttonSpace, rowRect.height);

            bool focused = focusedIssue.HasValue && focusedIssue.Value == Key(issue);
            if (focused) EditorGUI.DrawRect(rowRect, new Color(SelectColor.r, SelectColor.g, SelectColor.b, 0.25f));
            else if (labelRect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.06f));

            // 버튼을 먼저 처리해야 버튼 클릭이 행 클릭으로 새지 않는다.
            if (fix != null)
            {
                var buttonRect = new Rect(rowRect.xMax - FixButtonWidth, rowRect.y + 1f, FixButtonWidth, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(buttonRect, new GUIContent(fix.Label, fix.Tooltip), EditorStyles.miniButton)) return RowAction.Fix;
            }

            GUI.Label(labelRect, content, issueRowStyle);
            EditorGUIUtility.AddCursorRect(labelRect, MouseCursor.Link);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && labelRect.Contains(e.mousePosition))
            {
                e.Use();
                return RowAction.Focus;
            }
            if (e.type == EventType.MouseMove) Repaint();
            return RowAction.None;
        }

        /// <summary>빠른 수정 실행. 커맨드는 Execute를 거쳐 Undo 기록·다시 검사가 된다.</summary>
        private void ApplyQuickFix(ValidationIssue issue, IssueQuickFix fix)
        {
            GUI.FocusControl(null);

            if (fix.Command == null)
            {
                if (catalog != null && catalog.TryGetAsset(fix.PingDefinitionId, out var asset))
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset; // 인스펙터에서 바로 프리팹을 지정할 수 있게
                }
                return;
            }

            if (!Execute(fix.Command).IsValid) return;

            // 지운 대상을 가리키던 선택을 푼다.
            if (fix.Command is RemoveCommand && SelectedId == issue.TargetId) SelectedId = 0;
            if (fix.Command is RemoveZoneCommand)
            {
                if (selectedZoneId == issue.TargetId) selectedZoneId = 0;
                if (detailZoneId == issue.TargetId) detailZoneId = 0;
            }

            status = $"빠른 수정: {fix.Label} (Ctrl+Z로 되돌림)";
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>씬뷰를 문제 영역으로 옮기고 대상을 선택한다.</summary>
        private void FocusIssue(ValidationIssue issue)
        {
            focusedIssue = Key(issue);
            FrameArea(issue.Area);

            // 같은 ID가 둘 이상이면 대상을 구별할 수 없으므로 이동만 한다.
            if (issue.Code != ValidationCodes.DuplicateId)
            {
                switch (issue.Target)
                {
                    case IssueTarget.Object when HasObject(issue.TargetId):
                        SwitchModeForIssue(Mode.Select);
                        SelectedId = issue.TargetId;
                        break;
                    case IssueTarget.Zone when service.Map.TryGetZone(issue.TargetId, out _):
                        SwitchModeForIssue(Mode.Zone);
                        selectedZoneId = issue.TargetId;
                        detailZoneId = 0;
                        break;
                    case IssueTarget.Spawn when issue.Code == ValidationCodes.NoSpawn:
                        SwitchModeForIssue(Mode.Spawn); // 바로 클릭해서 놓을 수 있게
                        break;
                    case IssueTarget.Spawn when service.Map.TryGetSpawn(issue.TargetId, out var spawn):
                        SwitchModeForIssue(Mode.Spawn);
                        selectedSpawnId = spawn.Id;
                        detailSpawnId = 0;
                        layer = Mathf.Clamp(spawn.Cell.y, 0, service.Map.Size.y - 1); // 바로 끌어 옮길 수 있게
                        break;
                }
            }

            GUI.FocusControl(null);
            Repaint();
            SceneView.RepaintAll();
        }

        // 다른 오브젝트에 같은 ID가 있으면 맵 색인이 예외를 던지므로 그때는 없는 것으로 본다.
        private bool HasObject(int instanceId)
        {
            try { return service.Map.TryGet(instanceId, out _); }
            catch (System.InvalidOperationException) { return false; }
        }

        private void SwitchModeForIssue(Mode target)
        {
            if (mode == target) return;
            EndBrushStroke();
            mode = target;
            dragging = false;
            zoneDragging = false;
            zoneResizing = false;
            spawnDragging = false;
            ghost.Hide();
        }

        private void FrameArea(Bounds localArea)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;

            // 한 칸짜리 문제도 주변이 보이게 최소 3칸 크기로 잡는다.
            var cell = service.Map.Grid.CellSize;
            var size = Vector3.Max(localArea.size, cell * 3f);
            sceneView.Frame(new Bounds(service.Map.Grid.Origin + localArea.center, size), false);
        }

        // ───────────────────────── 씬뷰 ─────────────────────────

        /// <summary>오류는 항상, 경고는 목록을 펼쳤을 때만 그린다. 클릭한 문제는 굵게 + 이름표.</summary>
        private void DrawValidationMarkers()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureValidated();
            if (issues.Count == 0) return;

            var origin = service.Map.Grid.Origin;
            foreach (var issue in issues)
            {
                bool focused = focusedIssue.HasValue && focusedIssue.Value == Key(issue);
                if (!issue.IsError && !validationExpanded && !focused) continue;

                var color = issue.IsError ? InvalidColor : WarningColor;
                var center = origin + issue.Area.center;
                var size = issue.Area.size;
                using (new Handles.DrawingScope(color))
                {
                    Handles.DrawWireCube(center, size);
                    if (focused) Handles.DrawWireCube(center, size * 1.02f + Vector3.one * 0.02f);
                }

                if (focused)
                {
                    var style = new GUIStyle(EditorStyles.whiteBoldLabel) { normal = { textColor = color } };
                    Handles.Label(center + Vector3.up * (size.y * 0.5f + 0.2f), issue.Message, style);
                }
            }
        }
    }
}
