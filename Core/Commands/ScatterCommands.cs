using System.Collections.Generic;

namespace MapTool.Core
{
    /// <summary>
    /// 장식 여러 개를 한 번에 놓는다(브러시 한 번 찍기). 후보의 Id는 무시하고 맵이 새 Id를 준다.
    /// 놓을 수 없는 후보는 건너뛰고 나머지만 놓는다. 하나도 못 놓으면 실패(첫 실패 이유).
    /// Redo해도 같은 Id로 같은 장식이 다시 들어간다.
    /// </summary>
    public sealed class AddScattersCommand : MapCommand
    {
        private readonly List<PlacedScatter> candidates;
        private List<PlacedScatter> added;

        public AddScattersCommand(IEnumerable<PlacedScatter> candidates) => this.candidates = new List<PlacedScatter>(candidates);

        /// <summary>실행에 성공한 뒤 맵에 들어간 장식.</summary>
        public IReadOnlyList<PlacedScatter> Added => added ?? (IReadOnlyList<PlacedScatter>)System.Array.Empty<PlacedScatter>();

        /// <summary>첫 실행에서 건너뛴 후보 수.</summary>
        public int Skipped { get; private set; }

        internal override PlacementResult Do(PlacementService service)
        {
            if (added == null)
            {
                var accepted = new List<PlacedScatter>(candidates.Count);
                PlacementResult firstFailure = default;
                foreach (var candidate in candidates)
                {
                    var result = service.CanPlaceScatter(candidate);
                    if (result.IsValid)
                    {
                        accepted.Add(candidate);
                        continue;
                    }
                    if (Skipped == 0) firstFailure = result;
                    Skipped++;
                }

                if (accepted.Count == 0)
                {
                    return candidates.Count == 0
                        ? PlacementResult.Fail(PlacementCodes.NothingToDo, "놓을 장식이 없습니다.")
                        : firstFailure;
                }

                for (int i = 0; i < accepted.Count; i++) accepted[i] = accepted[i].WithId(service.Map.AllocateScatterId());
                added = accepted;
            }
            else
            {
                // Redo: Undo로 되돌린 상태라 정상이면 그대로 들어간다. 그 사이 정의가 사라졌을 때만 실패한다.
                foreach (var s in added)
                {
                    var result = service.CanPlaceScatter(s);
                    if (!result.IsValid) return result;
                }
            }

            service.AddScatters(added);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.RemoveScatters(added);
    }

    /// <summary>
    /// 장식 여러 개를 한 번에 지운다(지우개 한 번 찍기). 맵에 없는 Id는 건너뛴다. 하나도 없으면 실패.
    /// </summary>
    public sealed class RemoveScattersCommand : MapCommand
    {
        private readonly List<int> ids;
        private List<PlacedScatter> removed;

        public RemoveScattersCommand(IEnumerable<int> ids) => this.ids = new List<int>(ids);

        public IReadOnlyList<PlacedScatter> Removed => removed ?? (IReadOnlyList<PlacedScatter>)System.Array.Empty<PlacedScatter>();

        internal override PlacementResult Do(PlacementService service)
        {
            var found = new List<PlacedScatter>(ids.Count);
            var seen = new HashSet<int>();
            foreach (var id in ids)
            {
                if (seen.Add(id) && service.Map.TryGetScatter(id, out var s)) found.Add(s);
            }
            if (found.Count == 0)
                return PlacementResult.Fail(PlacementCodes.NotFound, "지울 장식이 맵에 없습니다.");

            removed = found;
            service.RemoveScatters(removed);
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service) => service.AddScatters(removed);
    }

    /// <summary>
    /// 여러 커맨드를 Undo 한 번으로 묶는다. PlacementService.BeginGroup/EndGroup이 만든다.
    /// 자식은 이미 실행된 상태로 들어오고, Undo는 역순, Redo는 순서대로 한다.
    /// </summary>
    internal sealed class CommandGroup : MapCommand
    {
        private readonly List<MapCommand> commands;

        public CommandGroup(List<MapCommand> commands) => this.commands = commands;

        public int Count => commands.Count;

        internal override PlacementResult Do(PlacementService service)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                var result = commands[i].Do(service);
                if (result.IsValid) continue;

                // 일부만 다시 된 상태로 두지 않는다.
                for (int j = i - 1; j >= 0; j--) commands[j].Undo(service);
                return result;
            }
            return PlacementResult.Ok;
        }

        internal override void Undo(PlacementService service)
        {
            for (int i = commands.Count - 1; i >= 0; i--) commands[i].Undo(service);
        }
    }
}
