using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    public enum IssueSeverity
    {
        /// <summary>동작은 하지만 의도와 다를 수 있다(프리팹 없음, 프로필 비어 있음).</summary>
        Warning = 0,
        /// <summary>데이터가 규칙을 어겼다(겹침, 범위 이탈, 정의 없음).</summary>
        Error = 1,
    }

    /// <summary>문제가 있는 대상의 종류. 목록 정렬 순서이기도 하다.</summary>
    public enum IssueTarget
    {
        Object = 0,
        Zone = 1,
        Scatter = 2,
        Spawn = 3,
    }

    /// <summary>검증 문제 종류. 코어 검사는 배치 결과 코드와 같은 문자열을 쓰고, 에디터 검사는 자기 코드를 더한다.</summary>
    public static class ValidationCodes
    {
        public const string Overlap = PlacementCodes.Overlap;
        public const string OutOfBounds = PlacementCodes.OutOfBounds;
        public const string UnknownDefinition = PlacementCodes.UnknownDefinition;
        public const string WrongKind = PlacementCodes.WrongKind;
        public const string InvalidScatter = PlacementCodes.InvalidScatter;
        public const string InvalidZone = PlacementCodes.InvalidZone;

        /// <summary>저장 데이터에 같은 ID가 두 번 이상 있다(에셋 직접 편집·병합 충돌).</summary>
        public const string DuplicateId = "duplicate-id";

        /// <summary>스폰 지점이 하나도 없다(경고).</summary>
        public const string NoSpawn = "no-spawn";

        /// <summary>스폰 지점이 칸 오브젝트가 있는 칸에 있다(경고). OtherId는 그 오브젝트.</summary>
        public const string SpawnBlocked = "spawn-blocked";
    }

    /// <summary>
    /// 검증 결과 한 줄. 대상은 Target 종류의 ID 목록이다(장식은 같은 원인·같은 정의를 한 줄로 묶어 여러 개).
    /// Area는 맵 원점 기준 로컬 좌표(미터)의 영역으로, 에디터가 씬뷰를 이 영역으로 옮길 때 쓴다.
    /// </summary>
    public readonly struct ValidationIssue
    {
        private readonly int[] targetIds;

        public ValidationIssue(IssueSeverity severity, string code, IssueTarget target, IReadOnlyCollection<int> targetIds,
            Bounds area, string message, int otherId = 0)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Target = target;
            this.targetIds = targetIds != null ? new List<int>(targetIds).ToArray() : Array.Empty<int>();
            Area = area;
            Message = message ?? string.Empty;
            OtherId = otherId;
        }

        public ValidationIssue(IssueSeverity severity, string code, IssueTarget target, int targetId,
            Bounds area, string message, int otherId = 0)
            : this(severity, code, target, new[] { targetId }, area, message, otherId) { }

        public IssueSeverity Severity { get; }
        public string Code { get; }
        public IssueTarget Target { get; }

        /// <summary>문제가 있는 대상들의 ID. 대부분 하나다.</summary>
        public IReadOnlyList<int> TargetIds => targetIds ?? Array.Empty<int>();

        /// <summary>첫 번째 대상 ID. 대상이 없으면 0.</summary>
        public int TargetId => TargetIds.Count > 0 ? TargetIds[0] : 0;

        /// <summary>겹침이면 상대 오브젝트 ID. 없으면 0.</summary>
        public int OtherId { get; }

        public Bounds Area { get; }
        public string Message { get; }

        public bool IsError => Severity == IssueSeverity.Error;

        public override string ToString() => $"[{Severity} {Code}] {Target} {string.Join(",", TargetIds)}: {Message}";
    }
}
