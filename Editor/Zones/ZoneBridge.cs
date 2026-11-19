using System;
using System.Collections.Generic;
using MapTool.Core;
using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>존 프로필 드롭다운에 보일 항목 하나. Id는 PlacedZone.ProfileId에 그대로 저장된다.</summary>
    public readonly struct ZoneProfileOption
    {
        public ZoneProfileOption(string id, string displayName)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
    }

    /// <summary>존 데이터를 씬에 반영하는 쪽. MapSceneView와 같은 수명(세션)을 갖는다.</summary>
    public interface IZoneSceneSync : IDisposable
    {
        /// <summary>씬을 데이터와 맞춘다. 무엇이든 바뀌었으면 true.</summary>
        bool Sync();
    }

    /// <summary>
    /// 맵툴 존 편집과 실제 존 시스템(오디오 등) 사이의 연결점.
    /// MapTool.Editor는 오디오를 모르므로 다른 어셈블리가 이 클래스를 상속해 프로필 목록과 씬 반영을 제공한다.
    /// 구현은 TypeCache로 찾고 매개변수 없는 생성자로 만든다. 없으면 창은 프로필 ID를 직접 입력받고 씬 반영을 하지 않는다.
    /// </summary>
    public abstract class ZoneBridge
    {
        /// <summary>창에 보일 이름. 예: "오디오 존 프로필"</summary>
        public virtual string ProfileLabel => "프로필";

        /// <summary>고를 수 있는 모든 프로필. 매 그리기마다 불리므로 구현 쪽에서 캐시한다.</summary>
        public abstract IReadOnlyList<ZoneProfileOption> GetProfiles();

        /// <summary>새 프로필 에셋을 만들고 그 Id를 돌려준다. 취소하면 null.</summary>
        public virtual string CreateProfile(string suggestedName) => null;

        /// <summary>프로젝트 창에서 프로필 에셋을 가리킨다.</summary>
        public virtual void PingProfile(string profileId) { }

        /// <summary>씬 반영을 만든다. mapRoot는 "[MapTool] 맵이름" 오브젝트. 반영하지 않으면 null.</summary>
        public virtual IZoneSceneSync CreateSceneSync(PlacementService service, Transform mapRoot) => null;

        private static bool searched;
        private static ZoneBridge current;

        /// <summary>프로젝트에 있는 구현(여럿이면 이름순 첫 번째). 없으면 null.</summary>
        public static ZoneBridge Current
        {
            get
            {
                if (searched) return current;
                searched = true;

                Type chosen = null;
                foreach (var type in TypeCache.GetTypesDerivedFrom<ZoneBridge>())
                {
                    if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) continue;
                    if (chosen == null || string.CompareOrdinal(type.FullName, chosen.FullName) < 0) chosen = type;
                }
                current = chosen != null ? (ZoneBridge)Activator.CreateInstance(chosen) : null;
                return current;
            }
        }
    }
}
