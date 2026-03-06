using Game.Pathfind;
using Unity.Mathematics;

namespace RealisticPathFinding.Utils
{
    internal static class PathfindCostUtils
    {
        internal const float DefaultEpsilon = 1e-4f;

        internal static bool AlmostEqual(float a, float b, float epsilon = DefaultEpsilon)
        {
            return math.abs(a - b) < epsilon;
        }

        internal static bool AlmostEqual(PathfindCosts a, PathfindCosts b, float epsilon = DefaultEpsilon)
        {
            float4 d = math.abs(a.m_Value - b.m_Value);
            return d.x < epsilon && d.y < epsilon && d.z < epsilon && d.w < epsilon;
        }
    }
}
