using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 맵툴 "여기서 플레이": 고른 스폰 ID를 SessionState에 남기고 플레이에 들어간다.
    /// PlayerSpawner가 Awake에서 그 값을 읽어 기본 스폰 대신 쓴다. 플레이가 끝나면 값을 지워 다음 일반 플레이는 기본 스폰에서 시작한다.
    /// 씬에 PlayerSpawner가 없으면 플레이어 후보에 붙여 줄지 묻는다.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromSpawn
    {
        static PlayFromSpawn()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) Clear();
            };
        }

        /// <summary>지금 플레이를 시작할 수 있는가(이미 플레이 중이거나 진입 중이면 아니다).</summary>
        public static bool CanPlay => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>대기 중인 스폰 ID. 없으면 0.</summary>
        public static int PendingSpawnId => SessionState.GetInt(PlayerSpawner.PlayFromSpawnKey, 0);

        public static void Clear() => SessionState.EraseInt(PlayerSpawner.PlayFromSpawnKey);

        /// <summary>스폰 spawnId에서 플레이를 시작한다. 준비가 안 됐거나 사용자가 취소하면 false.</summary>
        public static bool Play(int spawnId, string spawnLabel)
        {
            if (!CanPlay || spawnId == 0) return false;
            if (!EnsurePlayerSpawner(spawnLabel)) return false;

            SessionState.SetInt(PlayerSpawner.PlayFromSpawnKey, spawnId);
            EditorApplication.EnterPlaymode();
            return true;
        }

        /// <summary>
        /// 열린 씬에 켜진 PlayerSpawner가 있으면 true. 없으면 플레이어 후보에 붙일지 묻는다.
        /// 후보가 없으면 붙이는 방법을 알려 주고, 그래도 플레이할지 묻는다.
        /// </summary>
        private static bool EnsurePlayerSpawner(string spawnLabel)
        {
            if (Object.FindObjectsByType<PlayerSpawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length > 0) return true;

            const string title = "여기서 플레이";
            var candidate = FindPlayerCandidate();
            if (candidate != null)
            {
                int choice = EditorUtility.DisplayDialogComplex(title,
                    $"씬에 PlayerSpawner가 없어 플레이어를 {spawnLabel}(으)로 옮길 수 없습니다.\n\n" +
                    $"'{candidate.name}'에 PlayerSpawner를 붙이고 플레이할까요? (씬이 수정됩니다. 실행 취소 가능)",
                    "붙이고 플레이", "취소", "그냥 플레이");
                switch (choice)
                {
                    case 0:
                        Undo.AddComponent<PlayerSpawner>(candidate);
                        EditorGUIUtility.PingObject(candidate);
                        return true;
                    case 2:
                        return true;
                    default:
                        return false;
                }
            }

            return EditorUtility.DisplayDialog(title,
                $"씬에 PlayerSpawner가 없어 플레이어를 {spawnLabel}(으)로 옮길 수 없습니다.\n\n" +
                "플레이어 오브젝트를 고르고 Add Component → PlayerSpawner를 붙이면 됩니다.\n" +
                "(태그가 Player이거나 CharacterController가 하나뿐이면 자동으로 찾아 줍니다.)",
                "그냥 플레이", "취소");
        }

        /// <summary>
        /// PlayerSpawner를 붙일 플레이어 후보: "Player" 태그 오브젝트, 없으면 씬에 하나뿐인 CharacterController. 애매하면 null.
        /// </summary>
        internal static GameObject FindPlayerCandidate()
        {
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            if (tagged.Length == 1) return tagged[0];
            if (tagged.Length > 1) return null;

            var controllers = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => !EditorUtility.IsPersistent(c))
                .ToArray();
            return controllers.Length == 1 ? controllers[0].gameObject : null;
        }
    }
}
