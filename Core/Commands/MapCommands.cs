using System.Collections.Generic;
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

    /// <summary>
    /// 여러 오브젝트를 같은 거리만큼 함께 옮긴다. 회전과 서로의 간격은 그대로다.
    /// 묶음끼리는 서로의 현재 자리를 빈 칸으로 보므로 A가 B의 옛 자리로 들어가도 된다.
    /// 하나라도 못 놓으면 아무것도 옮기지 않는다.
    /// </summary>
    public sealed class MoveManyCommand : MapCommand
    {
        private readonly List<int> instanceIds;
        private readonly Vector3Int offset;
        private PlacedObject[] before;
        private PlacedObject[] after;

        public MoveManyCommand(IEnumerable<int> instanceIds, Vector3Int offset)
        {
            this.instanceIds = Distinct(instanceIds);
            this.offset = offset;
        }

        internal override PlacementResult Do(PlacementService service)
        {
            if (instanceIds.Count == 0 || offset == Vector3Int.zero)
                return PlacementResult.Fail(PlacementCodes.NothingToDo, "옮길 내용이 없습니다.");

            var current = new PlacedObject[instanceIds.Count];
            var moved = new PlacedObject[instanceIds.Count];
            for (int i = 0; i < instanceIds.Count; i++)
            {
                if (!service.Map.TryGet(instanceIds[i], out current[i]))
                    return PlacementResult.Fail(PlacementCodes.NotFound, $"오브젝트 #{instanceIds[i]}가 맵에 없습니다.");
                moved[i] = current[i].WithPose(current[i].Cell + offset, current[i].Rotation);
            }

            var result = service.CanPlaceGroup(moved);
            if (!result.IsValid) return result;

            before = current;
            after = moved;
            // ReplaceObject는 자기 소유 셀만 비우므로 묶음 안에서 자리를 주고받아도 순서와 상관없이 점유가 맞는다.
            for (int i = 0; i < after.Length; i++) service.ReplaceObject(before[i], after[i]);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service)
        {
            for (int i = after.Length - 1; i >= 0; i--) service.ReplaceObject(after[i], before[i]);
        }

        internal static List<int> Distinct(IEnumerable<int> ids)
        {
            var list = new List<int>();
            var seen = new HashSet<int>();
            if (ids != null)
                foreach (var id in ids)
                    if (seen.Add(id)) list.Add(id);
            return list;
        }
    }

    /// <summary>
    /// 여러 오브젝트를 한 번에 놓는다(복제 붙여넣기). 후보의 InstanceId는 무시하고 새로 받는다.
    /// 후보끼리 겹치거나 하나라도 못 놓으면 아무것도 놓지 않는다.
    /// </summary>
    public sealed class PlaceManyCommand : MapCommand
    {
        private readonly PlacedObject[] candidates;
        private PlacedObject[] placed;

        public PlaceManyCommand(IEnumerable<PlacedObject> candidates)
        {
            var list = new List<PlacedObject>();
            if (candidates != null)
                foreach (var c in candidates)
                    list.Add(new PlacedObject(0, c.DefinitionId, c.Cell, c.Rotation));
            this.candidates = list.ToArray();
        }

        /// <summary>실행에 성공한 뒤 맵에 들어간 오브젝트들. 후보 순서와 같고 Redo해도 같은 InstanceId를 쓴다.</summary>
        public IReadOnlyList<PlacedObject> Placed => placed ?? System.Array.Empty<PlacedObject>();

        internal override PlacementResult Do(PlacementService service)
        {
            var result = service.CanPlaceGroup(placed ?? candidates);
            if (!result.IsValid) return result;

            if (placed == null)
            {
                placed = new PlacedObject[candidates.Length];
                for (int i = 0; i < candidates.Length; i++)
                {
                    var c = candidates[i];
                    placed[i] = new PlacedObject(service.Map.AllocateId(), c.DefinitionId, c.Cell, c.Rotation);
                }
            }
            foreach (var p in placed) service.AddObject(p);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service)
        {
            for (int i = placed.Length - 1; i >= 0; i--) service.RemoveObject(placed[i]);
        }
    }

    /// <summary>여러 오브젝트를 한 번에 지운다. 하나라도 맵에 없으면 아무것도 지우지 않는다.</summary>
    public sealed class RemoveManyCommand : MapCommand
    {
        private readonly List<int> instanceIds;
        private PlacedObject[] removed;

        public RemoveManyCommand(IEnumerable<int> instanceIds) => this.instanceIds = MoveManyCommand.Distinct(instanceIds);

        internal override PlacementResult Do(PlacementService service)
        {
            if (instanceIds.Count == 0)
                return PlacementResult.Fail(PlacementCodes.NothingToDo, "지울 오브젝트가 없습니다.");

            var found = new PlacedObject[instanceIds.Count];
            for (int i = 0; i < instanceIds.Count; i++)
            {
                if (!service.Map.TryGet(instanceIds[i], out found[i]))
                    return PlacementResult.Fail(PlacementCodes.NotFound, $"오브젝트 #{instanceIds[i]}가 맵에 없습니다.");
            }

            removed = found;
            foreach (var r in removed) service.RemoveObject(r);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service)
        {
            for (int i = removed.Length - 1; i >= 0; i--) service.AddObject(removed[i]);
        }
    }
}
