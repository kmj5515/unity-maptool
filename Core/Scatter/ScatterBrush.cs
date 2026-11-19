using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 브러시 설정. 에디터 창이 들고 있다가 찍을 때마다 넘긴다. 값 범위는 Sanitize로 맞춘다.
    /// 높이는 월드 Y(미터), 밀도는 1㎡당 개수다.
    /// </summary>
    [Serializable]
    public sealed class ScatterBrushSettings
    {
        [Tooltip("브러시 반지름 (m)")]
        public float Radius = 3f;
        [Tooltip("1㎡당 개수. 같은 자리를 계속 문질러도 이 밀도까지만 채운다")]
        public float Density = 0.5f;
        [Tooltip("장식끼리 최소 간격 (m). 0이면 간격 검사 없음")]
        public float MinSpacing = 0.6f;

        [Tooltip("Y축으로 아무 방향이나 돌린다")]
        public bool RandomYaw = true;
        public float ScaleMin = 0.8f;
        public float ScaleMax = 1.2f;
        [Tooltip("지면 기울기를 얼마나 따를지. 0=똑바로 서기, 1=지면에 딱 붙기")]
        [Range(0f, 1f)] public float AlignToNormal;

        [Tooltip("이보다 가파른 비탈에는 놓지 않는다 (도)")]
        [Range(0f, 90f)] public float MaxSlope = 40f;
        [Tooltip("켜면 아래 높이(월드 Y) 범위 안에만 놓는다")]
        public bool UseHeightFilter;
        public float MinHeight;
        public float MaxHeight = 10f;

        public ScatterBrushSettings Clone() => (ScatterBrushSettings)MemberwiseClone();

        /// <summary>잘못 들어간 값을 쓸 수 있는 범위로 맞춘다(최소>최대면 바꾼다).</summary>
        public void Sanitize()
        {
            Radius = Mathf.Clamp(Radius, 0.1f, 100f);
            Density = Mathf.Clamp(Density, 0f, 50f);
            MinSpacing = Mathf.Max(0f, MinSpacing);
            ScaleMin = Mathf.Max(0.01f, ScaleMin);
            ScaleMax = Mathf.Max(0.01f, ScaleMax);
            if (ScaleMin > ScaleMax) (ScaleMin, ScaleMax) = (ScaleMax, ScaleMin);
            AlignToNormal = Mathf.Clamp01(AlignToNormal);
            MaxSlope = Mathf.Clamp(MaxSlope, 0f, 90f);
            if (MinHeight > MaxHeight) (MinHeight, MaxHeight) = (MaxHeight, MinHeight);
        }
    }

    /// <summary>브러시에 담긴 장식 하나와 뽑힐 비율. 비율은 상대값(풀 7, 꽃 3 → 70%:30%).</summary>
    public readonly struct ScatterEntry
    {
        public ScatterEntry(string definitionId, float weight)
        {
            DefinitionId = definitionId;
            Weight = weight;
        }

        public string DefinitionId { get; }
        public float Weight { get; }
    }

    /// <summary>
    /// 월드 XZ 위치 아래의 지면을 찾는다. 에디터는 레이캐스트로, 테스트는 평면·경사 함수로 구현한다.
    /// 지면이 없으면 false.
    /// </summary>
    public delegate bool SurfaceSampler(Vector2 worldXZ, out Vector3 worldPoint, out Vector3 normal);

    /// <summary>
    /// 브러시 한 번 찍기의 계산. 맵은 바꾸지 않고 후보·지울 Id만 돌려준다.
    /// 실제 반영은 AddScattersCommand / RemoveScattersCommand로 하고, 드래그 한 번은 BeginGroup/EndGroup으로 묶는다.
    /// </summary>
    public static class ScatterBrush
    {
        /// <summary>목표 개수당 시도 횟수. 필터·간격에 막혀도 이만큼은 다른 자리를 찾아본다.</summary>
        private const int AttemptsPerItem = 6;
        private const int MaxAttempts = 2000;

        /// <summary>
        /// 원 안을 목표 밀도까지 채울 후보를 만든다. 이미 있는 장식 수를 빼고 모자란 만큼만 만든다.
        /// 지면이 없거나, 비탈·높이 필터에 걸리거나, 맵 밖이거나, 최소 간격 안에 다른 장식이 있으면 그 자리는 건너뛴다.
        /// </summary>
        public static List<PlacedScatter> Generate(
            Vector3 worldCenter,
            ScatterBrushSettings settings,
            IReadOnlyList<ScatterEntry> entries,
            PlacementService service,
            SurfaceSampler sampler,
            System.Random random)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            if (random == null) throw new ArgumentNullException(nameof(random));

            var result = new List<PlacedScatter>();
            float totalWeight = TotalWeight(entries, service);
            if (totalWeight <= 0f) return result;

            var s = settings.Clone();
            s.Sanitize();

            var grid = service.Map.Grid;
            var extent = Vector3.Scale(service.Map.Size, grid.CellSize);
            var localCenter = worldCenter - grid.Origin;

            int target = StochasticRound(s.Density * Mathf.PI * s.Radius * s.Radius, random);
            int need = target - CountInside(service, localCenter, s.Radius);
            if (need <= 0) return result;

            int attempts = Mathf.Min(need * AttemptsPerItem, MaxAttempts);
            for (int a = 0; a < attempts && result.Count < need; a++)
            {
                // 원 안에 고르게: 반지름은 sqrt로 뽑는다.
                double angle = random.NextDouble() * Math.PI * 2.0;
                float r = s.Radius * Mathf.Sqrt((float)random.NextDouble());
                var xz = new Vector2(worldCenter.x + r * (float)Math.Cos(angle), worldCenter.z + r * (float)Math.Sin(angle));

                if (!sampler(xz, out var point, out var normal)) continue;
                if (!PassesFilters(point, normal, s)) continue;

                var local = point - grid.Origin;
                if (local.x < 0f || local.z < 0f || local.x >= extent.x || local.z >= extent.z) continue;
                if (s.MinSpacing > 0f && (service.ScatterIndex.AnyWithin(local, s.MinSpacing) || TooClose(result, local, s.MinSpacing)))
                    continue;

                string definitionId = PickWeighted(entries, service, totalWeight, random);
                result.Add(new PlacedScatter(0, definitionId, local, MakeRotation(normal, s, random), MakeScale(s, random)));
            }
            return result;
        }

        /// <summary>
        /// 원 안(XZ)의 지울 장식 Id. onlyDefinitions가 null이면 종류를 가리지 않고, 있으면 그 종류만 지운다.
        /// 칸 오브젝트는 대상이 아니다.
        /// </summary>
        public static List<int> CollectErase(Vector3 worldCenter, float radius, PlacementService service, ICollection<string> onlyDefinitions = null)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            var found = new List<PlacedScatter>();
            service.ScatterIndex.Query(worldCenter - service.Map.Grid.Origin, radius, found);

            var ids = new List<int>(found.Count);
            foreach (var s in found)
            {
                if (onlyDefinitions == null || onlyDefinitions.Contains(s.DefinitionId)) ids.Add(s.Id);
            }
            return ids;
        }

        /// <summary>비탈·높이 필터를 통과하는지. 씬뷰 브러시 원 색을 정할 때도 쓴다.</summary>
        public static bool PassesFilters(Vector3 worldPoint, Vector3 normal, ScatterBrushSettings settings)
        {
            if (SlopeAngle(normal) > settings.MaxSlope + 0.01f) return false;
            if (settings.UseHeightFilter && (worldPoint.y < settings.MinHeight || worldPoint.y > settings.MaxHeight)) return false;
            return true;
        }

        /// <summary>지면 법선과 위쪽 사이 각도(도). 0이면 평지, 90이면 수직 벽.</summary>
        public static float SlopeAngle(Vector3 normal) =>
            normal.sqrMagnitude < 1e-8f ? 90f : Vector3.Angle(normal, Vector3.up);

        private static int CountInside(PlacementService service, Vector3 localCenter, float radius)
        {
            var found = new List<PlacedScatter>();
            service.ScatterIndex.Query(localCenter, radius, found);
            return found.Count;
        }

        private static bool TooClose(List<PlacedScatter> placed, Vector3 local, float spacing)
        {
            float s2 = spacing * spacing;
            foreach (var p in placed)
            {
                float dx = p.Position.x - local.x, dz = p.Position.z - local.z;
                if (dx * dx + dz * dz < s2) return true;
            }
            return false;
        }

        private static float TotalWeight(IReadOnlyList<ScatterEntry> entries, PlacementService service)
        {
            if (entries == null) return 0f;
            float total = 0f;
            foreach (var e in entries)
            {
                if (e.Weight > 0f && service.IsScatterDefinition(e.DefinitionId)) total += e.Weight;
            }
            return total;
        }

        private static string PickWeighted(IReadOnlyList<ScatterEntry> entries, PlacementService service, float totalWeight, System.Random random)
        {
            float roll = (float)random.NextDouble() * totalWeight;
            string last = null;
            foreach (var e in entries)
            {
                if (!(e.Weight > 0f) || !service.IsScatterDefinition(e.DefinitionId)) continue;
                last = e.DefinitionId;
                if (roll < e.Weight) return e.DefinitionId;
                roll -= e.Weight;
            }
            return last; // 부동소수 오차로 끝까지 온 경우
        }

        private static Quaternion MakeRotation(Vector3 normal, ScatterBrushSettings s, System.Random random)
        {
            float yaw = s.RandomYaw ? (float)(random.NextDouble() * 360.0) : 0f;
            var spin = Quaternion.AngleAxis(yaw, Vector3.up);
            if (s.AlignToNormal <= 0f || normal.sqrMagnitude < 1e-8f) return spin;
            var tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal.normalized), s.AlignToNormal);
            return tilt * spin;
        }

        private static float MakeScale(ScatterBrushSettings s, System.Random random) =>
            Mathf.Lerp(s.ScaleMin, s.ScaleMax, (float)random.NextDouble());

        // 0.4개 같은 기대값도 평균적으로 맞게: 소수 부분 확률로 하나 더.
        private static int StochasticRound(float value, System.Random random)
        {
            if (value <= 0f) return 0;
            int whole = Mathf.FloorToInt(value);
            return whole + (random.NextDouble() < value - whole ? 1 : 0);
        }
    }
}
