using System;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 맵에 놓인 오브젝트 하나. 값 타입이며 바꿀 때는 새 값으로 통째로 교체한다.
    /// Cell은 회전을 적용한 점유 영역의 최소 모서리 셀이다. 회전해도 Cell은 그대로이고 영역의 X/Z 크기만 바뀐다.
    /// InstanceId 0은 아직 맵에 들어가지 않은 후보를 뜻한다.
    /// </summary>
    [Serializable]
    public struct PlacedObject : IEquatable<PlacedObject>
    {
        [SerializeField] private int instanceId;
        [SerializeField] private string definitionId;
        [SerializeField] private Vector3Int cell;
        [SerializeField] private GridRotation rotation;

        public PlacedObject(int instanceId, string definitionId, Vector3Int cell, GridRotation rotation)
        {
            this.instanceId = instanceId;
            this.definitionId = definitionId;
            this.cell = cell;
            this.rotation = rotation;
        }

        public int InstanceId => instanceId;
        public string DefinitionId => definitionId;
        public Vector3Int Cell => cell;
        public GridRotation Rotation => rotation;

        public PlacedObject WithPose(Vector3Int newCell, GridRotation newRotation) =>
            new(instanceId, definitionId, newCell, newRotation);

        /// <summary>회전을 적용한 점유 영역.</summary>
        public BoundsInt GetBounds(ObjectDefinition definition) =>
            new(cell, rotation.RotateSize(definition.Size));

        public bool Equals(PlacedObject other) =>
            instanceId == other.instanceId && definitionId == other.definitionId &&
            cell == other.cell && rotation == other.rotation;

        public override bool Equals(object obj) => obj is PlacedObject other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(instanceId, definitionId, cell, rotation);

        public static bool operator ==(PlacedObject a, PlacedObject b) => a.Equals(b);
        public static bool operator !=(PlacedObject a, PlacedObject b) => !a.Equals(b);

        public override string ToString() => $"#{instanceId} {definitionId} {cell} {rotation}";
    }
}
