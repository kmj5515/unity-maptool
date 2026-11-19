using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 씬뷰 드래그 한 번(획). 누른 순간 한 번 찍고, 이후에는 브러시가 반경 × StampSpacing만큼 움직일 때마다 찍는다.
    /// 획마다 새 랜덤을 쓴다. 지우기인지는 누른 순간의 Shift로 정해 획 도중 바뀌지 않는다.
    /// </summary>
    internal sealed class ScatterBrushStroke
    {
        /// <summary>반경 대비 찍기 간격. 0.5면 원이 반씩 겹쳐 빨리 그어도 빈틈이 없다.</summary>
        public const float StampSpacing = 0.5f;

        private Vector3 lastStamp;
        private bool hasStamp;

        public bool Active { get; private set; }
        public bool Erasing { get; private set; }
        public System.Random Random { get; private set; }

        public void Begin(bool erasing, int? seed = null)
        {
            Active = true;
            Erasing = erasing;
            hasStamp = false;
            Random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public void End()
        {
            Active = false;
            hasStamp = false;
        }

        /// <summary>여기서 찍어야 하면 true를 돌려주고 마지막 찍은 자리로 기억한다. XZ 거리로 잰다.</summary>
        public bool TryStamp(Vector3 worldPoint, float radius)
        {
            if (!Active) return false;
            if (hasStamp)
            {
                float spacing = Mathf.Max(0.01f, radius * StampSpacing);
                float dx = worldPoint.x - lastStamp.x, dz = worldPoint.z - lastStamp.z;
                if (dx * dx + dz * dz < spacing * spacing) return false;
            }
            lastStamp = worldPoint;
            hasStamp = true;
            return true;
        }

        // ───────────────────────── 단축키 단계 ─────────────────────────

        /// <summary>한 번 누를 때 반경·밀도를 바꾸는 비율(±10%).</summary>
        public const float StepFactor = 1.1f;
        public const float MinRadius = 0.1f;
        public const float MaxRadius = 100f;
        public const float MaxDensity = 50f;
        /// <summary>밀도 0에서 올릴 때 시작값. 비율로만 바꾸면 0에서 못 벗어난다.</summary>
        public const float DensityFloor = 0.05f;

        /// <summary>direction &gt; 0이면 키우고 &lt; 0이면 줄인다.</summary>
        public static float StepRadius(float radius, int direction)
        {
            if (direction == 0) return radius;
            float next = direction > 0 ? radius * StepFactor : radius / StepFactor;
            return Mathf.Clamp(next, MinRadius, MaxRadius);
        }

        /// <summary>direction &gt; 0이면 키우고 &lt; 0이면 줄인다. 아주 작아지면 0으로 떨어진다.</summary>
        public static float StepDensity(float density, int direction)
        {
            if (direction == 0) return density;
            if (direction > 0) return Mathf.Min(MaxDensity, density < DensityFloor ? DensityFloor : density * StepFactor);
            float next = density / StepFactor;
            return next < DensityFloor ? 0f : next;
        }
    }
}
