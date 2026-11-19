using UnityEngine;

namespace MapTool.Core
{
    /// <summary>배치 검사 결과. 실패하면 Code(기계용)와 Message(사람용)를 담는다.</summary>
    public readonly struct PlacementResult
    {
        public static readonly PlacementResult Ok = new(true, null, null);

        private PlacementResult(bool isValid, string code, string message)
        {
            IsValid = isValid;
            Code = code;
            Message = message;
        }

        public bool IsValid { get; }
        public string Code { get; }
        public string Message { get; }

        public static PlacementResult Fail(string code, string message) => new(false, code, message);

        public override string ToString() => IsValid ? "Ok" : $"{Code}: {Message}";
    }

    /// <summary>기본 실패 코드. 규칙을 추가하면 자기 코드를 정의해서 쓴다.</summary>
    public static class PlacementCodes
    {
        public const string UnknownDefinition = "unknown-definition";
        public const string OutOfBounds = "out-of-bounds";
        public const string Overlap = "overlap";
        public const string NotFound = "not-found";

        /// <summary>존에 상자가 없거나 크기가 0인 상자가 있다.</summary>
        public const string InvalidZone = "invalid-zone";

        /// <summary>칸 배치에 장식 정의를 쓰거나, 장식 배치에 칸 정의를 썼다.</summary>
        public const string WrongKind = "wrong-kind";

        /// <summary>장식의 스케일이 0 이하이거나 위치·회전 값이 숫자가 아니다.</summary>
        public const string InvalidScatter = "invalid-scatter";

        /// <summary>실행할 내용이 비어 있다(후보 0개).</summary>
        public const string NothingToDo = "nothing-to-do";
    }

    /// <summary>규칙이 검사에 쓰는 정보. Bounds는 후보의 회전을 적용한 점유 영역이다.</summary>
    public readonly struct PlacementContext
    {
        public PlacementContext(MapData map, OccupancyMap occupancy, PlacedObject candidate, ObjectDefinition definition)
        {
            Map = map;
            Occupancy = occupancy;
            Candidate = candidate;
            Definition = definition;
            Bounds = candidate.GetBounds(definition);
        }

        public MapData Map { get; }
        public OccupancyMap Occupancy { get; }
        public PlacedObject Candidate { get; }
        public ObjectDefinition Definition { get; }
        public BoundsInt Bounds { get; }
    }

    /// <summary>배치 규칙 하나. PlacementService가 등록된 순서대로 검사하고 처음 실패한 결과를 돌려준다.</summary>
    public interface IPlacementRule
    {
        PlacementResult Check(in PlacementContext context);
    }

    /// <summary>점유 영역 전체가 맵 범위 안에 있어야 한다.</summary>
    public sealed class MapBoundsRule : IPlacementRule
    {
        public PlacementResult Check(in PlacementContext context)
        {
            var b = context.Bounds;
            var map = context.Map.Bounds;
            return IsInside(b, map)
                ? PlacementResult.Ok
                : PlacementResult.Fail(PlacementCodes.OutOfBounds, $"'{context.Definition.Id}'이(가) 맵 밖으로 나갑니다.");
        }

        /// <summary>b가 map 안에 완전히 들어가는지. 배치·존·검증이 같이 쓴다.</summary>
        public static bool IsInside(BoundsInt b, BoundsInt map)
        {
            return b.xMin >= map.xMin && b.yMin >= map.yMin && b.zMin >= map.zMin &&
                   b.xMax <= map.xMax && b.yMax <= map.yMax && b.zMax <= map.zMax;
        }
    }

    /// <summary>다른 오브젝트가 점유한 셀과 겹치면 안 된다. 이동 중인 자기 자신은 무시한다.</summary>
    public sealed class OverlapRule : IPlacementRule
    {
        public PlacementResult Check(in PlacementContext context)
        {
            int self = context.Candidate.InstanceId;
            foreach (var cell in context.Bounds.allPositionsWithin)
            {
                if (context.Occupancy.TryGetOccupant(cell, out var occupant) && occupant != self)
                {
                    // 사람이 읽는 메시지라 내부 번호 대신 정의 ID(기본값 = 팔레트에 보이는 에셋 이름)를 쓴다.
                    string what = context.Map.TryGet(occupant, out var other) ? $"'{other.DefinitionId}'" : $"오브젝트 #{occupant}";
                    return PlacementResult.Fail(PlacementCodes.Overlap, $"셀 {cell}에 이미 {what}이(가) 있습니다.");
                }
            }
            return PlacementResult.Ok;
        }
    }
}
