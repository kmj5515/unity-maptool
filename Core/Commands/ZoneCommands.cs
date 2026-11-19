namespace MapTool.Core
{
    /// <summary>존을 새로 만든다. 후보의 Id는 무시하고 맵이 새 Id를 준다. Redo해도 같은 Id를 쓴다.</summary>
    public sealed class AddZoneCommand : MapCommand
    {
        private readonly PlacedZone candidate;
        private PlacedZone added;
        private bool hasId;

        public AddZoneCommand(PlacedZone candidate) => this.candidate = candidate;

        /// <summary>실행에 성공한 뒤 맵에 들어간 존.</summary>
        public PlacedZone Added => added;

        internal override PlacementResult Do(PlacementService service)
        {
            var zone = hasId ? added : candidate;
            var result = service.CanPlaceZone(zone);
            if (!result.IsValid) return result;

            if (!hasId)
            {
                added = candidate.WithId(service.Map.AllocateZoneId());
                hasId = true;
            }
            service.AddZone(added);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.RemoveZone(added);
    }

    /// <summary>
    /// 존을 통째로 새 값으로 바꾼다(상자 편집, 프로필·우선순위·이름 변경).
    /// 새 값은 기존 존에 With*를 써서 만든다. Id가 같은 존을 찾아 교체한다.
    /// </summary>
    public sealed class ModifyZoneCommand : MapCommand
    {
        private readonly PlacedZone target;
        private PlacedZone before;

        public ModifyZoneCommand(PlacedZone after) => target = after;

        internal override PlacementResult Do(PlacementService service)
        {
            if (!service.Map.TryGetZone(target.Id, out var current))
                return PlacementResult.Fail(PlacementCodes.NotFound, $"존 #{target.Id}가 맵에 없습니다.");

            var result = service.CanPlaceZone(target);
            if (!result.IsValid) return result;

            before = current;
            service.ReplaceZone(before, target);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.ReplaceZone(target, before);
    }

    public sealed class RemoveZoneCommand : MapCommand
    {
        private readonly int zoneId;
        private PlacedZone removed;

        public RemoveZoneCommand(int zoneId) => this.zoneId = zoneId;

        internal override PlacementResult Do(PlacementService service)
        {
            if (!service.Map.TryGetZone(zoneId, out removed))
                return PlacementResult.Fail(PlacementCodes.NotFound, $"존 #{zoneId}가 맵에 없습니다.");

            service.RemoveZone(removed);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.AddZone(removed);
    }
}
