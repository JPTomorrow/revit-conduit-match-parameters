using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;
using JPMorrow.Revit.Text;

namespace JPMorrow.Revit.Measurements {
    public static class RMeasure {
        public static double LengthDbl(ModelInfo info, string cvt_str) {
            bool s = UnitFormatUtils.TryParse(info.DOC.GetUnits(), UnitType.UT_Length, cvt_str, out double val);

            if(s)
                return val;
            else
                return -1;
        }

        public static string LengthFromDbl(ModelInfo info, double dbl) {
            return UnitFormatUtils.Format(info.DOC.GetUnits(), UnitType.UT_Length, dbl, true, false, CustomFormatValue.FeetAndInches);
        }
    }
}
