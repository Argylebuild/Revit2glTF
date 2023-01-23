using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GLTFRevitExport.Extensions {
    static class NumberExtensions {
        /// <summary>
        /// Consider a Revit length zero 
        /// if is smaller than this.
        /// </summary>
        const float _eps = 1.0e-9f;

        /// <summary>
        /// Conversion factor from feet to meter.
        /// </summary>
        const float _feet_to_m = 0.3048f;

        // 1/10 of a mm
        const short _resolution = 4;

        public static float ToSingle(this double d)
            => Convert.ToSingle(d);

        /// <summary>
        /// Convert double length value from feet to meter
        /// </summary>
        public static float ToGLTFLength(this double d) {
            var f = d.ToSingle();
            if (Math.Abs(f) <= _eps)
                return 0f;
            return _feet_to_m * f;
        }

        public static float Round(this float number) {
            return Math.Round(Convert.ToDouble(number), _resolution).ToSingle();
        }

        // https://stackoverflow.com/a/3875619/2350244
        public static bool AlmostEquals(this double a, double b, double epsilon = _eps) {
            const double min = 2.2250738585072014E-308d;
            double absA = Math.Abs(a);
            double absB = Math.Abs(b);
            double diff = Math.Abs(a - b);

            if (a.Equals(b)) { // shortcut, handles infinities
                return true;
            }
            else if (a == 0 || b == 0 || absA + absB < min) {
                // a or b is zero or both are extremely close to it
                // relative error is less meaningful here
                return diff < (epsilon * min);
            }
            else { // use relative error
                return diff / (absA + absB) < epsilon;
            }
        }
    }
}
