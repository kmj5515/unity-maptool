using System;
using System.Collections.Generic;
using System.IO;
using Audio;
using MapTool.Core;
using MapTool.Editor;
using UnityEditor;
using UnityEngine;

namespace MapTool.Audio.Editor
{
    /// <summary>
    /// 맵툴 존 ↔ 오디오 존 연결. 존의 ProfileId에는 AudioZoneProfile 에셋의 GUID를 저장한다.
    /// GUID라서 에셋 이름을 바꾸거나 폴더를 옮겨도 연결이 끊기지 않는다. 창에는 에셋 이름으로 보인다.
    /// </summary>
    public sealed class AudioZoneBridge : ZoneBridge
    {
        private static List<ZoneProfileOption> cache;

        public AudioZoneBridge()
        {
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        public override string ProfileLabel => "오디오 프로필";

        public override IReadOnlyList<ZoneProfileOption> GetProfiles()
        {
            if (cache != null) return cache;

            cache = new List<ZoneProfileOption>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(AudioZoneProfile)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                cache.Add(new ZoneProfileOption(guid, Path.GetFileNameWithoutExtension(path)));
            }
            cache.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            return cache;
        }

        public override string CreateProfile(string suggestedName)
        {
            string folder = "Assets";
            var profiles = GetProfiles();
            if (profiles.Count > 0)
                folder = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(profiles[0].Id))?.Replace('\\', '/') ?? folder;

            string name = string.IsNullOrWhiteSpace(suggestedName) ? "AudioZoneProfile" : suggestedName.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');

            string path = EditorUtility.SaveFilePanelInProject(
                "새 오디오 존 프로필", name, "asset", "프로필 에셋을 저장할 위치를 고르세요.", folder);
            if (string.IsNullOrEmpty(path)) return null;

            var profile = ScriptableObject.CreateInstance<AudioZoneProfile>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            Invalidate();
            EditorGUIUtility.PingObject(profile);
            return AssetDatabase.AssetPathToGUID(path);
        }

        public override void PingProfile(string profileId)
        {
            var profile = LoadProfile(profileId);
            if (profile != null) EditorGUIUtility.PingObject(profile);
        }

        public override IZoneSceneSync CreateSceneSync(PlacementService service, Transform mapRoot) =>
            new AudioZoneSceneSync(service, mapRoot, LoadProfile);

        /// <summary>GUID로 프로필 에셋을 읽는다. 비었거나 없으면 null.</summary>
        public static AudioZoneProfile LoadProfile(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AudioZoneProfile>(path);
        }

        private static void Invalidate() => cache = null;
    }
}
