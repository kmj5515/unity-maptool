using System;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 브러시로 흩뿌린 장식 하나. 칸을 차지하지 않으며 자유 위치·회전·균일 스케일을 갖는다.
    /// Position은 맵 원점(GridSpace.Origin) 기준 로컬 좌표(미터)라서 원점을 옮기면 함께 따라간다.
    /// Id 0은 아직 맵에 들어가지 않은 후보다. Id는 칸 오브젝트와 따로 센다.
    /// </summary>
    [Serializable]
    public struct PlacedScatter : IEquatable<PlacedScatter>
    {
        [SerializeField] private int id;
        [SerializeField] private string definitionId;
        [SerializeField] private Vector3 position;
        [SerializeField] private Quaternion rotation;
        [SerializeField] private float scale;

        public PlacedScatter(int id, string definitionId, Vector3 position, Quaternion rotation, float scale)
        {
            this.id = id;
            this.definitionId = definitionId;
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        public int Id => id;
        public string DefinitionId => definitionId;
        /// <summary>맵 원점 기준 로컬 좌표(미터).</summary>
        public Vector3 Position => position;
        public Quaternion Rotation => rotation;
        public float Scale => scale;

        public PlacedScatter WithId(int newId) => new(newId, definitionId, position, rotation, scale);

        public Vector3 WorldPosition(GridSpace grid) => grid.Origin + position;

        public bool Equals(PlacedScatter other) =>
            id == other.id && definitionId == other.definitionId &&
            position == other.position && rotation == other.rotation && scale.Equals(other.scale);

        public override bool Equals(object obj) => obj is PlacedScatter other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(id, definitionId, position, rotation, scale);
        public static bool operator ==(PlacedScatter a, PlacedScatter b) => a.Equals(b);
        public static bool operator !=(PlacedScatter a, PlacedScatter b) => !a.Equals(b);
        public override string ToString() => $"~{id} {definitionId} {position} x{scale:0.##}";
    }
}
