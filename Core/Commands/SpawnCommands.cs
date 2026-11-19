namespace MapTool.Core
{
    /// <summary>
    /// 스폰 지점을 새로 만든다. 후보의 Id는 무시하고 맵이 새 Id를 준다. Redo해도 같은 Id를 쓴다.
    /// 맵에 기본 스폰이 정해져 있지 않으면 새 지점이 기본이 된다(Undo하면 되돌린다).
    /// </summary>
    public sealed class AddSpawnCommand : MapCommand
    {
        private readonly PlacedSpawn candidate;
        private PlacedSpawn added;
        private bool hasId;
        private int previousDefault;

        public AddSpawnCommand(PlacedSpawn candidate) => this.candidate = candidate;

        /// <summary>실행에 성공한 뒤 맵에 들어간 스폰 지점.</summary>
        public PlacedSpawn Added => added;

        internal override PlacementResult Do(PlacementService service)
        {
            var spawn = hasId ? added : candidate;
            var result = service.CanPlaceSpawn(spawn);
            if (!result.IsValid) return result;

            if (!hasId)
            {
                added = candidate.WithId(service.Map.AllocateSpawnId());
                hasId = true;
            }
            previousDefault = service.Map.DefaultSpawnId;
            service.AddSpawn(added);
            if (!service.Map.TryGetSpawn(previousDefault, out _)) service.SetDefaultSpawn(added.Id);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service)
        {
            if (service.Map.DefaultSpawnId != previousDefault) service.SetDefaultSpawn(previousDefault);
            service.RemoveSpawn(added);
        }
    }

    /// <summary>스폰 지점을 새 값으로 바꾼다(이동, 회전, 이름). 새 값은 기존 지점에 With*를 써서 만든다.</summary>
    public sealed class ModifySpawnCommand : MapCommand
    {
        private readonly PlacedSpawn target;
        private PlacedSpawn before;

        public ModifySpawnCommand(PlacedSpawn after) => target = after;

        internal override PlacementResult Do(PlacementService service)
        {
            if (!service.Map.TryGetSpawn(target.Id, out var current))
                return PlacementResult.Fail(PlacementCodes.NotFound, $"스폰 지점 @{target.Id}가 맵에 없습니다.");

            var result = service.CanPlaceSpawn(target);
            if (!result.IsValid) return result;

            before = current;
            service.ReplaceSpawn(before, target);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.ReplaceSpawn(target, before);
    }

    /// <summary>
    /// 스폰 지점을 지운다. 지운 것이 기본이면 남은 첫 지점이 기본이 된다(없으면 기본 없음).
    /// Undo하면 같은 자리(목록 순서)로 돌아오고 기본 지정도 되돌린다.
    /// </summary>
    public sealed class RemoveSpawnCommand : MapCommand
    {
        private readonly int spawnId;
        private PlacedSpawn removed;
        private int index;
        private int previousDefault;

        public RemoveSpawnCommand(int spawnId) => this.spawnId = spawnId;

        internal override PlacementResult Do(PlacementService service)
        {
            if (!service.Map.TryGetSpawn(spawnId, out removed))
                return PlacementResult.Fail(PlacementCodes.NotFound, $"스폰 지점 @{spawnId}가 맵에 없습니다.");

            index = service.Map.IndexOfSpawn(spawnId);
            previousDefault = service.Map.DefaultSpawnId;
            service.RemoveSpawn(removed);
            if (previousDefault == spawnId)
            {
                var spawns = service.Map.Spawns;
                service.SetDefaultSpawn(spawns.Count > 0 ? spawns[0].Id : 0);
            }
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service)
        {
            service.AddSpawn(removed, index);
            if (service.Map.DefaultSpawnId != previousDefault) service.SetDefaultSpawn(previousDefault);
        }
    }

    /// <summary>플레이를 시작할 때 쓸 기본 스폰 지점을 바꾼다.</summary>
    public sealed class SetDefaultSpawnCommand : MapCommand
    {
        private readonly int spawnId;
        private int previous;

        public SetDefaultSpawnCommand(int spawnId) => this.spawnId = spawnId;

        internal override PlacementResult Do(PlacementService service)
        {
            if (!service.Map.TryGetSpawn(spawnId, out _))
                return PlacementResult.Fail(PlacementCodes.NotFound, $"스폰 지점 @{spawnId}가 맵에 없습니다.");
            if (service.Map.DefaultSpawnId == spawnId)
                return PlacementResult.Fail(PlacementCodes.NothingToDo, "이미 기본 스폰 지점입니다.");

            previous = service.Map.DefaultSpawnId;
            service.SetDefaultSpawn(spawnId);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.SetDefaultSpawn(previous);
    }
}
