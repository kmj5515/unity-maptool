using System;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 맵에 놓인 스폰 지점 하나(플레이어가 나타나는 칸과 바라보는 방향).
    /// 오브젝트·존처럼 값 타입이고 바꿀 때는 With* 로 새 값을 만들어 ModifySpawnCommand로 교체한다.
    /// 칸을 점유하지 않는다. 오브젝트가 있는 칸에 놓이면 검사에서 경고한다.
    /// Id 0은 아직 맵에 들어가지 않은 후보를 뜻한다.
    /// </summary>
    [Serializable]
    public struct PlacedSpawn : IEquatable<PlacedSpawn>
    {
        [SerializeField] private int id;
        [SerializeField] private string name;
        [SerializeField] private Vector3Int cell;
        [SerializeField] private GridRotation rotation;

        public PlacedSpawn(int id, string name, Vector3Int cell, GridRotation rotation)
        {
            this.id = id;
            this.name = name ?? string.Empty;
            this.cell = cell;
            this.rotation = rotation;
        }

        public int Id => id;

        /// <summary>아티스트가 목록에서 알아보기 위한 이름(예: "마을 입구"). 동작에는 영향 없다.</summary>
        public string Name => name ?? string.Empty;

        public Vector3Int Cell => cell;

        /// <summary>플레이어가 바라볼 방향. R0이면 +Z(앞).</summary>
        public GridRotation Rotation => rotation;

        /// <summary>플레이어 발이 놓일 월드 좌표(칸 바닥면 중심).</summary>
        public Vector3 WorldPosition(GridSpace grid) => grid.FootprintBaseCenter(new BoundsInt(cell, Vector3Int.one));

        public Quaternion WorldRotation => Quaternion.Euler(0f, rotation.ToDegrees(), 0f);

        public PlacedSpawn WithName(string newName) => new(id, newName, cell, rotation);
        public PlacedSpawn WithCell(Vector3Int newCell) => new(id, name, newCell, rotation);
        public PlacedSpawn WithRotation(GridRotation newRotation) => new(id, name, cell, newRotation);
        internal PlacedSpawn WithId(int newId) => new(newId, name, cell, rotation);

        public bool Equals(PlacedSpawn other) =>
            id == other.id && Name == other.Name && cell == other.cell && rotation == other.rotation;

        public override bool Equals(object obj) => obj is PlacedSpawn other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(id, Name, cell, rotation);

        public static bool operator ==(PlacedSpawn a, PlacedSpawn b) => a.Equals(b);
        public static bool operator !=(PlacedSpawn a, PlacedSpawn b) => !a.Equals(b);

        public override string ToString() => $"spawn @{id} '{Name}' {cell} {rotation}";
    }
}
