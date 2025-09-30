using System;
using System.Linq;
using System.Reflection;
using Game.Simulation; // for TimeSystem.kTicksPerDay
using Unity.Mathematics;

static class Time2WorkInterop
{
    // cached across calls; call InvalidateCache() if you want to re-read later
    static bool _checked;
    static float _factor = 1f;

    public static float GetFactor()
    {
        if (_checked) return _factor;
        _factor = 1f;
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => {
                    try { return a.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
                })
                .FirstOrDefault(t => t.FullName == "Time2Work.Time2WorkTimeSystem");

            if (type != null)
            {
                // Preferred: read public static timeReductionFactor
                var f = type.GetField("timeReductionFactor", BindingFlags.Public | BindingFlags.Static);
                if (f != null && f.FieldType == typeof(float))
                {
                    var val = (float)f.GetValue(null);
                    if (val > 0f) _factor = val;
                }
                else
                {
                    // Fallback: derive from kTicksPerDay vs vanilla
                    var k = type.GetField("kTicksPerDay", BindingFlags.Public | BindingFlags.Static);
                    if (k != null)
                    {
                        int t2wTicks = (int)k.GetValue(null);
                        _factor = math.max(0.0001f, (float)t2wTicks / TimeSystem.kTicksPerDay);
                    }
                }
            }
        }
        catch { /* ignore */ }
        _checked = true;
        return _factor;
    }

    public static void InvalidateCache() => _checked = false;
}
