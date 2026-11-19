using System.Collections.Generic;
using UnityEngine;

namespace MapTool
{
    /// <summary>
    /// 플레이를 시작하면 대상을 맵의 기본 스폰 지점으로 옮긴다. 플레이어 오브젝트에 붙인다.
    /// 카메라 등 다른 컴포넌트가 Awake에서 플레이어 위치를 읽기 전에 옮기도록 실행 순서를 앞당긴다.
    /// 스폰이 없으면 경고만 남기고 그 자리에서 시작한다.
    /// 에디터에서 맵툴 "여기서 플레이"로 시작하면 기본 대신 고른 스폰에서 시작한다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [Tooltip("옮길 대상. 비워 두면 이 오브젝트를 옮긴다.")]
        [SerializeField] private Transform target;

        public Transform Target => target != null ? target : transform;

        /// <summary>
        /// 맵툴 "여기서 플레이"가 고른 스폰 ID를 담는 SessionState 키(에디터 전용).
        /// 맵툴이 플레이 진입 직전에 쓰고 플레이가 끝나면 지운다. 값이 없으면 기본 스폰을 쓴다.
        /// </summary>
        public const string PlayFromSpawnKey = "MapTool.PlayFromSpawnId";

        private void Awake()
        {
            var spawn = PickStart();
            if (spawn == null)
            {
                Debug.LogWarning("[PlayerSpawner] 씬에 스폰 지점이 없어 제자리에서 시작합니다. 맵툴 '스폰' 모드에서 스폰을 놓으세요.", this);
                return;
            }
            MoveTo(Target, spawn.transform);
        }

        private SpawnPoint PickStart()
        {
            int requested = 0;
#if UNITY_EDITOR
            requested = UnityEditor.SessionState.GetInt(PlayFromSpawnKey, 0);
#endif
            var spawns = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var start = ChooseStart(spawns, requested, out bool requestMissing);
            if (requestMissing)
                Debug.LogWarning($"[PlayerSpawner] 맵툴에서 고른 스폰 #{requested}이(가) 씬에 없어 기본 스폰에서 시작합니다.", this);
            return start;
        }

        /// <summary>
        /// requestedId(0 = 지정 없음)가 있으면 그 스폰, 없거나 찾지 못하면 기본 스폰.
        /// requestMissing은 지정했는데 찾지 못했을 때 true.
        /// </summary>
        public static SpawnPoint ChooseStart(IReadOnlyCollection<SpawnPoint> spawns, int requestedId, out bool requestMissing)
        {
            requestMissing = false;
            if (requestedId != 0)
            {
                var chosen = SpawnPoint.PickById(spawns, requestedId);
                if (chosen != null) return chosen;
                requestMissing = true;
            }
            return SpawnPoint.PickDefault(spawns);
        }

        /// <summary>대상을 스폰 지점의 위치·방향으로 순간이동한다. CharacterController·Rigidbody가 있어도 안전하게 옮긴다.</summary>
        public static void MoveTo(Transform target, Transform spawn)
        {
            if (target == null || spawn == null) return;

            // 켜진 CharacterController는 Transform 직접 변경을 다음 Move에서 덮어쓸 수 있다.
            var controller = target.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;

            target.SetPositionAndRotation(spawn.position, spawn.rotation);
            if (target.TryGetComponent<Rigidbody>(out var body))
            {
                body.position = spawn.position;
                body.rotation = spawn.rotation;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            if (wasEnabled) controller.enabled = true;
        }
    }
}
