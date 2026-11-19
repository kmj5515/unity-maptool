using System;
using System.IO;
using System.Text;
using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// JSON 내보내기·가져오기.
    ///   툴바 "JSON ▾" → 내보내기 / 덮어쓰기(확인 후, Ctrl+Z 한 번으로 되돌림)
    ///   맵이 없을 때 → "JSON에서 새 맵 만들기"
    /// 형식이 깨진 파일은 이유를 알려 주고 거부한다. 없는 오브젝트 정의 등 내용 문제는 그대로 가져오고 맵 검사가 알린다.
    /// </summary>
    public sealed partial class MapToolWindow
    {
        private const string JsonFolderPref = "MapTool.JsonFolder";

        private static string JsonFolder
        {
            get => EditorPrefs.GetString(JsonFolderPref, "");
            set => EditorPrefs.SetString(JsonFolderPref, value ?? "");
        }

        private void DrawJsonMenu()
        {
            if (!EditorGUILayout.DropdownButton(new GUIContent("JSON"), FocusType.Passive, EditorStyles.toolbarDropDown)) return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("JSON으로 내보내기..."), false, ExportJson);
            menu.AddItem(new GUIContent("JSON으로 덮어쓰기..."), false, OverwriteFromJson);
            menu.ShowAsContext();
        }

        private void ExportJson()
        {
            if (map == null || service == null) return;
            string path = EditorUtility.SaveFilePanel("맵을 JSON으로 내보내기", JsonFolder, map.name + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                File.WriteAllText(path, MapJson.ToJson(service.Map), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("내보내기 실패", $"파일을 쓰지 못했습니다.\n{path}\n\n{e.Message}", "확인");
                return;
            }

            JsonFolder = Path.GetDirectoryName(path);
            if (IsInsideProject(path)) AssetDatabase.Refresh();
            status = $"JSON으로 내보냈습니다: {path}";
            Repaint();
        }

        private void OverwriteFromJson()
        {
            if (map == null || service == null) return;
            var imported = PickJson("JSON으로 덮어쓰기", out string path);
            if (imported == null) return;

            var current = service.Map;
            string message =
                $"'{map.name}'의 내용을 모두 이 파일로 바꿉니다.\n{Path.GetFileName(path)}\n\n" +
                $"지금:   {MapJsonSummary.Counts(current)}\n" +
                $"바뀐 뒤: {MapJsonSummary.Counts(imported)}\n" +
                (current.Size != imported.Size || !SameGrid(current.Grid, imported.Grid) ? "\n맵 크기 또는 칸 크기도 바뀝니다.\n" : "") +
                "\nCtrl+Z(되돌리기) 한 번으로 이전 맵으로 돌아올 수 있습니다.";
            if (!EditorUtility.DisplayDialog("JSON으로 덮어쓰기", message, "덮어쓰기", "취소")) return;

            EndBrushStroke();
            dragging = zoneDragging = zoneResizing = spawnDragging = false;
            if (!Execute(new ReplaceMapCommand(imported)).IsValid) return;
            layer = Mathf.Clamp(layer, 0, service.Map.Size.y - 1);
            ShowImportResult(service.Map, "덮어썼습니다");
        }

        private void DrawNewMapFromJsonButton()
        {
            if (!GUILayout.Button("JSON에서 새 맵 만들기...")) return;

            var imported = PickJson("JSON에서 새 맵 만들기", out string jsonPath);
            if (imported == null) return;

            string path = EditorUtility.SaveFilePanelInProject("새 맵", Path.GetFileNameWithoutExtension(jsonPath), "asset", "맵 에셋을 저장할 위치를 고르세요.");
            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateInstance<MapAsset>();
            asset.SetData(imported);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            map = asset;
            LoadSession();
            ShowImportResult(asset.Data, "새 맵을 만들었습니다");
        }

        /// <summary>파일을 골라 읽는다. 취소하거나 읽지 못하면(이유를 알린 뒤) null.</summary>
        private static MapData PickJson(string title, out string path)
        {
            path = EditorUtility.OpenFilePanel(title, JsonFolder, "json");
            if (string.IsNullOrEmpty(path)) return null;
            JsonFolder = Path.GetDirectoryName(path);

            try
            {
                return MapJson.FromJson(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (MapJsonException e)
            {
                EditorUtility.DisplayDialog("가져오기 실패", $"{Path.GetFileName(path)}을(를) 맵으로 읽을 수 없습니다.\n\n{e.Message}", "확인");
            }
            catch (IOException e)
            {
                EditorUtility.DisplayDialog("가져오기 실패", $"파일을 읽지 못했습니다.\n{path}\n\n{e.Message}", "확인");
            }
            return null;
        }

        private void ShowImportResult(MapData data, string done)
        {
            int unknown = MapJsonSummary.CountUnknownDefinitions(data, catalog);
            status = $"{done}. {MapJsonSummary.Counts(data)}";
            if (unknown > 0)
            {
                status += $"\n알 수 없는 오브젝트 {unknown}개 — 이 프로젝트에 없는 정의입니다. 맵 검사 목록에서 확인하세요.";
                validationExpanded = true;
            }
            InvalidateValidation();
            Repaint();
        }

        private static bool SameGrid(GridSpace a, GridSpace b) => a.Origin == b.Origin && a.CellSize == b.CellSize;

        private static bool IsInsideProject(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/') + "/";
            return full.StartsWith(assets, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>가져오기 확인·결과 문구에 쓰는 개수 세기.</summary>
    internal static class MapJsonSummary
    {
        public static string Counts(MapData map) =>
            $"크기 {map.Size.x}×{map.Size.y}×{map.Size.z}, 오브젝트 {map.Count}, 존 {map.Zones.Count}, 장식 {map.ScatterCount}, 스폰 {map.Spawns.Count}";

        /// <summary>카탈로그에 없는 정의를 쓰는 오브젝트·장식 수.</summary>
        public static int CountUnknownDefinitions(MapData map, IObjectCatalog catalog)
        {
            if (catalog == null) return 0;
            int count = 0;
            foreach (var o in map.Objects)
                if (!catalog.TryGet(o.DefinitionId, out _)) count++;
            foreach (var s in map.Scatters)
                if (!catalog.TryGet(s.DefinitionId, out _)) count++;
            return count;
        }
    }
}
