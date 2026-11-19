using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 맵을 바꾸는 작업. PlacementService.Execute로 실행하고 성공하면 Undo 스택에 쌓인다.
    /// Do/Undo가 internal이라 커맨드 종류는 코어 안에서만 정의한다(맵 변경 경로를 한 곳으로 제한).
    /// 인스턴스 하나는 한 번만 Execute한다. 다시 실행은 Redo가 담당한다.
    /// </summary>
    public abstract class MapCommand
    {
        internal abstract PlacementResult Do(PlacementService service);
        internal abstract void Undo(PlacementService service);
    }

    public sealed class PlaceCommand : MapCommand
    {
        private readonly string definitionId;
        private readonly Vector3Int cell;
        private readonly GridRotation rotation;
        private PlacedObject placed;
        private bool hasId;

        public PlaceCommand(string definitionId, Vector3Int cell, GridRotation rotation = GridRotation.R0)
        {
            this.definitionId = definitionId;
            this.cell = cell;
            this.rotation = rotation;
        }

        /// <summary>실행에 성공한 뒤 맵에 들어간 오브젝트. Redo해도 같은 InstanceId를 쓴다.</summary>
        public PlacedObject Placed => placed;

        internal override PlacementResult Do(PlacementService service)
        {
            var candidate = hasId ? placed : new PlacedObject(0, definitionId, cell, rotation);
            var result = service.CanPlace(candidate);
            if (!result.IsValid) return result;

            if (!hasId)
            {
                placed = new PlacedObject(service.Map.AllocateId(), definitionId, cell, rotation);
                hasId = true;
            }
            service.AddObject(placed);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.RemoveObject(placed);
    }

    /// <summary>위치와 회전을 바꾼다. 제자리 회전도 이 커맨드로 한다.</summary>
    public sealed class MoveCommand : MapCommand
    {
        private readonly int instanceId;
        private readonly Vector3Int cell;
        private readonly GridRotation rotation;
        private PlacedObject before;
        private PlacedObject after;

        public MoveCommand(int instanceId, Vector3Int cell, GridRotation rotation)
        {
            this.instanceId = instanceId;
            this.cell = cell;
            this.rotation = rotation;
        }

        internal override PlacementResult Do(PlacementService service)
        {
            if (!service.Map.TryGet(instanceId, out var current))
                return PlacementResult.Fail(PlacementCodes.NotFound, $"오브젝트 #{instanceId}가 맵에 없습니다.");

            var moved = current.WithPose(cell, rotation);
            var result = service.CanPlace(moved);
            if (!result.IsValid) return result;

            before = current;
            after = moved;
            service.ReplaceObject(before, after);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.ReplaceObject(after, before);
    }

    public sealed class RemoveCommand : MapCommand
    {
        private readonly int instanceId;
        private PlacedObject removed;

        public RemoveCommand(int instanceId) => this.instanceId = instanceId;

        internal override PlacementResult Do(PlacementService service)
        {
            if (!service.Map.TryGet(instanceId, out removed))
                return PlacementResult.Fail(PlacementCodes.NotFound, $"오브젝트 #{instanceId}가 맵에 없습니다.");

            service.RemoveObject(removed);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.AddObject(removed);
    }
}
