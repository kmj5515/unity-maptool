using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 장식을 XZ 평면 버킷으로 나눠 원 범위 검색을 빠르게 한다(브러시 최소 간격, 지우개).
    /// 높이(Y)는 보지 않는다. 같은 XZ에 겹쳐 쌓인 층이 있어도 위에서 내려다본 원 안이면 찾는다.
    /// 좌표는 맵 로컬(미터). 저장하지 않고 PlacementService가 맵에서 다시 만든다.
    /// </summary>
    public sealed class ScatterSpatialIndex
    {
        private readonly float bucketSize;
        private readonly Dictionary<long, List<PlacedScatter>> buckets = new();

        public ScatterSpatialIndex(float bucketSize)
        {
            if (!(bucketSize > 0f)) throw new ArgumentOutOfRangeException(nameof(bucketSize), bucketSize, "버킷 크기는 0보다 커야 합니다.");
            this.bucketSize = bucketSize;
        }

        public int Count { get; private set; }

        public void Clear()
        {
            buckets.Clear();
            Count = 0;
        }

        public void Add(PlacedScatter scatter)
        {
            long key = KeyOf(scatter.Position.x, scatter.Position.z);
            if (!buckets.TryGetValue(key, out var list)) buckets.Add(key, list = new List<PlacedScatter>());
            list.Add(scatter);
            Count++;
        }

        public bool Remove(PlacedScatter scatter)
        {
            long key = KeyOf(scatter.Position.x, scatter.Position.z);
            if (!buckets.TryGetValue(key, out var list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id != scatter.Id) continue;
                list.RemoveAt(i);
                if (list.Count == 0) buckets.Remove(key);
                Count--;
                return true;
            }
            return false;
        }

        /// <summary>XZ 거리 radius 이내(경계 포함)의 장식을 results에 더한다. results는 비우지 않는다.</summary>
        public void Query(Vector3 localCenter, float radius, List<PlacedScatter> results)
        {
            if (radius < 0f) return;
            float r2 = radius * radius;
            int x0 = Bucket(localCenter.x - radius), x1 = Bucket(localCenter.x + radius);
            int z0 = Bucket(localCenter.z - radius), z1 = Bucket(localCenter.z + radius);
            for (int bx = x0; bx <= x1; bx++)
            {
                for (int bz = z0; bz <= z1; bz++)
                {
                    if (!buckets.TryGetValue(Pack(bx, bz), out var list)) continue;
                    foreach (var s in list)
                    {
                        float dx = s.Position.x - localCenter.x, dz = s.Position.z - localCenter.z;
                        if (dx * dx + dz * dz <= r2) results.Add(s);
                    }
                }
            }
        }

        /// <summary>XZ 거리 radius 미만에 하나라도 있는지. 최소 간격 검사용이라 경계는 포함하지 않는다.</summary>
        public bool AnyWithin(Vector3 localCenter, float radius)
        {
            if (radius <= 0f) return false;
            float r2 = radius * radius;
            int x0 = Bucket(localCenter.x - radius), x1 = Bucket(localCenter.x + radius);
            int z0 = Bucket(localCenter.z - radius), z1 = Bucket(localCenter.z + radius);
            for (int bx = x0; bx <= x1; bx++)
            {
                for (int bz = z0; bz <= z1; bz++)
                {
                    if (!buckets.TryGetValue(Pack(bx, bz), out var list)) continue;
                    foreach (var s in list)
                    {
                        float dx = s.Position.x - localCenter.x, dz = s.Position.z - localCenter.z;
                        if (dx * dx + dz * dz < r2) return true;
                    }
                }
            }
            return false;
        }

        private int Bucket(float v) => Mathf.FloorToInt(v / bucketSize);
        private long KeyOf(float x, float z) => Pack(Bucket(x), Bucket(z));
        private static long Pack(int x, int z) => ((long)x << 32) | (uint)z;
    }
}
