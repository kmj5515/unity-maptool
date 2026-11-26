using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// Map Tool이 편집 중이고 씬뷰나 Map Tool 창에 포커스가 있을 때만 켜지는 단축키.
    /// 컨텍스트 단축키라 같은 키의 전역 단축키(Ctrl+Z = 유니티 Undo, R = 스케일 도구)보다 먼저 처리된다.
    /// Ctrl+Z가 유니티 Undo가 아니라 코어 커맨드 스택으로 가게 하는 연결점이다.
    /// </summary>
    internal static class MapToolShortcuts
    {
        internal sealed class Context : IShortcutContext
        {
            public bool active
            {
                get
                {
                    var window = MapToolWindow.Active;
                    if (window == null || !window.IsCapturingInput) return false;
                    var focused = EditorWindow.focusedWindow;
                    return focused is SceneView || focused == window;
                }
            }
        }

        [Shortcut("Map Tool/Undo", typeof(Context), KeyCode.Z, ShortcutModifiers.Action)]
        private static void Undo() => MapToolWindow.Active?.UndoAction();

        [Shortcut("Map Tool/Redo", typeof(Context), KeyCode.Y, ShortcutModifiers.Action)]
        private static void Redo() => MapToolWindow.Active?.RedoAction();

        [Shortcut("Map Tool/Redo (Shift+Z)", typeof(Context), KeyCode.Z, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        private static void RedoShiftZ() => MapToolWindow.Active?.RedoAction();

        [Shortcut("Map Tool/Rotate", typeof(Context), KeyCode.R)]
        private static void Rotate() => MapToolWindow.Active?.RotateAction();

        [Shortcut("Map Tool/Brush Radius Down", typeof(Context), KeyCode.LeftBracket)]
        private static void BrushRadiusDown() => MapToolWindow.Active?.BrushRadiusAction(-1);

        [Shortcut("Map Tool/Brush Radius Up", typeof(Context), KeyCode.RightBracket)]
        private static void BrushRadiusUp() => MapToolWindow.Active?.BrushRadiusAction(1);

        [Shortcut("Map Tool/Brush Erase Toggle", typeof(Context), KeyCode.E)]
        private static void BrushEraseToggle() => MapToolWindow.Active?.BrushEraseToggleAction();

        [Shortcut("Map Tool/Delete", typeof(Context), KeyCode.Delete)]
        private static void Delete() => MapToolWindow.Active?.DeleteAction();

        [Shortcut("Map Tool/Duplicate", typeof(Context), KeyCode.D, ShortcutModifiers.Action)]
        private static void Duplicate() => MapToolWindow.Active?.DuplicateAction();
    }
}
