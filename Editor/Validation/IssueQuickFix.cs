using System;
using System.Collections.Generic;
using MapTool.Core;

namespace MapTool.Editor
{
    /// <summary>
    /// 검증 행 하나의 빠른 수정. 데이터를 바꾸는 수정은 코어 커맨드라서 Ctrl+Z로 되돌릴 수 있고,
    /// [정의 보기]처럼 데이터를 바꾸지 않는 수정은 PingDefinitionId만 채운다.
    /// </summary>
    internal sealed class IssueQuickFix
    {
        private IssueQuickFix(string label, string tooltip, MapCommand command, string pingDefinitionId)
        {
            Label = label;
            Tooltip = tooltip;
            Command = command;
            PingDefinitionId = pingDefinitionId;
        }

        public string Label { get; }
        public string Tooltip { get; }

        /// <summary>실행할 커맨드. null이면 데이터를 바꾸지 않는 수정.</summary>
        public MapCommand Command { get; }

        /// <summary>프로젝트 창에서 강조할 정의 ID. 커맨드 수정이면 null.</summary>
        public string PingDefinitionId { get; }

        private static IssueQuickFix Run(string label, string tooltip, MapCommand command) => new(label, tooltip, command, null);

        /// <summary>
        /// 문제에 맞는 수정을 만든다. 손으로 고쳐야 하는 문제(같은 ID, 프로필 비어 있음)나
        /// 대상이 이미 없으면 null.
        /// </summary>
        public static IssueQuickFix Create(ValidationIssue issue, MapData map)
        {
            if (map == null || issue.TargetIds.Count == 0) return null;
            if (issue.Code == ValidationCodes.DuplicateId) return null; // 어느 쪽을 남길지 사람이 정한다

            try
            {
                return issue.Target switch
                {
                    IssueTarget.Object => ForObject(issue, map),
                    IssueTarget.Scatter => ForScatter(issue, map),
                    IssueTarget.Zone => ForZone(issue, map),
                    _ => null,
                };
            }
            catch (InvalidOperationException)
            {
                // 다른 오브젝트·장식에 같은 ID가 있으면 맵 색인을 만들 수 없다. 같은 ID 문제를 먼저 고쳐야 한다.
                return null;
            }
        }

        private static IssueQuickFix ForObject(ValidationIssue issue, MapData map)
        {
            if (!map.TryGet(issue.TargetId, out var placed)) return null;

            switch (issue.Code)
            {
                case ValidationCodes.Overlap:
                    return Run("삭제", "나중에 놓인 이 오브젝트를 지웁니다. Ctrl+Z로 되돌릴 수 있습니다.", new RemoveCommand(placed.InstanceId));
                case ValidationCodes.OutOfBounds:
                case ValidationCodes.UnknownDefinition:
                case ValidationCodes.WrongKind:
                    return Run("삭제", "이 오브젝트를 지웁니다. Ctrl+Z로 되돌릴 수 있습니다.", new RemoveCommand(placed.InstanceId));
                case EditorValidationCodes.MissingPrefab:
                    return Ping(placed.DefinitionId);
                default:
                    return null;
            }
        }

        private static IssueQuickFix ForScatter(ValidationIssue issue, MapData map)
        {
            if (!map.TryGetScatter(issue.TargetId, out var first)) return null;

            switch (issue.Code)
            {
                case ValidationCodes.OutOfBounds:
                case ValidationCodes.UnknownDefinition:
                case ValidationCodes.WrongKind:
                case ValidationCodes.InvalidScatter:
                    int count = issue.TargetIds.Count;
                    return Run(count == 1 ? "삭제" : $"{count}개 삭제",
                        $"이 줄의 장식 {count}개를 한 번에 지웁니다. Ctrl+Z 한 번으로 모두 되돌립니다.",
                        new RemoveScattersCommand(issue.TargetIds));
                case EditorValidationCodes.MissingPrefab:
                    return Ping(first.DefinitionId);
                default:
                    return null;
            }
        }

        private static IssueQuickFix ForZone(ValidationIssue issue, MapData map)
        {
            if (!map.TryGetZone(issue.TargetId, out var zone)) return null;

            switch (issue.Code)
            {
                case ValidationCodes.InvalidZone:
                case ValidationCodes.OutOfBounds:
                    var kept = new List<ZoneBox>();
                    foreach (var box in zone.Boxes)
                    {
                        if (!box.IsEmpty && MapBoundsRule.IsInside(box.Bounds, map.Bounds)) kept.Add(box);
                    }
                    if (kept.Count == 0)
                        return Run("존 삭제", "쓸 수 있는 상자가 남지 않아 존을 지웁니다. Ctrl+Z로 되돌릴 수 있습니다.", new RemoveZoneCommand(zone.Id));
                    return Run("상자 정리",
                        $"크기가 0이거나 맵 밖에 있는 상자 {zone.Boxes.Count - kept.Count}개만 지우고 존은 남깁니다. Ctrl+Z로 되돌릴 수 있습니다.",
                        new ModifyZoneCommand(zone.WithBoxes(kept)));
                case EditorValidationCodes.ProfileMissing:
                    return Run("프로필 해제", "없는 프로필 연결을 비웁니다. 존 상세에서 다른 프로필을 고를 수 있습니다. Ctrl+Z로 되돌릴 수 있습니다.",
                        new ModifyZoneCommand(zone.WithProfile(string.Empty)));
                default:
                    return null; // 프로필 비어 있음: 행 클릭으로 존이 선택되니 거기서 고른다
            }
        }

        private static IssueQuickFix Ping(string definitionId) =>
            string.IsNullOrEmpty(definitionId)
                ? null
                : new IssueQuickFix("정의 보기", $"정의 '{definitionId}' 에셋을 프로젝트 창에서 보여 줍니다. 거기서 프리팹을 지정하세요.", null, definitionId);
    }
}
