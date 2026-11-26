using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MapTool.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 프로젝트의 모든 맵을 검증 탭과 같은 규칙(EditorMapValidator)으로 검사한다.
    /// 맵은 에디터에서 씬에 구워지므로 씬만 보고는 어느 맵이 빌드에 들어가는지 알 수 없다. 그래서 전부 검사하고,
    /// 연습·작업 중인 맵은 에셋 라벨 SkipLabel을 붙여 뺀다.
    /// </summary>
    public static class MapBuildCheck
    {
        public const string SkipLabel = "MapTool-SkipBuildCheck";

        public readonly struct MapResult
        {
            public MapResult(string path, List<ValidationIssue> issues)
            {
                Path = path;
                Issues = issues;
            }

            public string Path { get; }
            public List<ValidationIssue> Issues { get; }
            public int ErrorCount => Issues.Count(i => i.IsError);
        }

        internal static List<MapResult> Check(IEnumerable<(string path, MapData data)> maps, EditorObjectCatalog catalog,
            Func<string, MapToolWindow.ProfileState> profileState = null)
        {
            var results = new List<MapResult>();
            foreach (var (path, data) in maps)
            {
                if (data == null) continue;
                results.Add(new MapResult(path, EditorMapValidator.Validate(data, catalog, profileState)));
            }
            return results;
        }

        /// <summary>SkipLabel이 없는 MapAsset 전부. skipped에는 라벨로 뺀 맵 경로.</summary>
        internal static List<(string path, MapData data)> FindMaps(out List<string> skipped)
        {
            var maps = new List<(string, MapData)>();
            skipped = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(MapAsset)}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<MapAsset>(path);
                if (asset == null) continue;
                if (AssetDatabase.GetLabels(asset).Contains(SkipLabel)) skipped.Add(path);
                else maps.Add((path, asset.Data));
            }
            return maps;
        }

        internal static string Format(IReadOnlyList<MapResult> results, IReadOnlyCollection<string> skipped)
        {
            int errors = results.Sum(r => r.ErrorCount);
            int warnings = results.Sum(r => r.Issues.Count) - errors;
            var sb = new StringBuilder($"[MapTool] 맵 {results.Count}개 검사: 오류 {errors}, 경고 {warnings}");
            if (skipped.Count > 0) sb.Append($" (라벨 '{SkipLabel}'로 제외 {skipped.Count}개)");
            foreach (var r in results)
            {
                if (r.Issues.Count == 0) continue;
                sb.Append($"\n  {r.Path}");
                foreach (var issue in r.Issues) sb.Append("\n    ").Append(issue);
            }
            return sb.ToString();
        }

        /// <summary>검사하고 콘솔에 남긴다. 오류 수를 돌려준다.</summary>
        internal static int RunAndLog(out string message)
        {
            var results = Check(FindMaps(out var skipped), EditorObjectCatalog.LoadAll());
            message = Format(results, skipped);
            int errors = results.Sum(r => r.ErrorCount);
            if (errors > 0) Debug.LogError(message);
            else if (results.Any(r => r.Issues.Count > 0)) Debug.LogWarning(message);
            else Debug.Log(message);
            return errors;
        }

        [MenuItem("Tools/Map Tool/모든 맵 검사", priority = 21)]
        private static void RunFromMenu() => RunAndLog(out _);
    }

    /// <summary>빌드 전에 모든 맵을 검사해 오류(겹침·범위 이탈·정의 없음·중복 ID 등)가 있으면 빌드를 멈춘다. 경고는 통과.</summary>
    internal sealed class MapBuildGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            int errors = MapBuildCheck.RunAndLog(out _);
            if (errors > 0)
                throw new BuildFailedException(
                    $"맵 검증 오류 {errors}건. Map Tool 검증 탭에서 고치거나, 빌드에 안 쓰는 맵이면 라벨 '{MapBuildCheck.SkipLabel}'을 붙이세요.");
        }
    }
}
