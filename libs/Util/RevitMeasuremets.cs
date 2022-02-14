using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;
using JPMorrow.Revit.Text;

namespace JPMorrow.Revit.Measurements {
    public static class RMeasure {
#if REVIT2017 || REVIT2018 || REVIT2019 || REVIT2020 // DisplayUnitType Depreciated
        public static double LengthDbl(ModelInfo info, string cvt_str) {
            bool s = UnitFormatUtils.TryParse(info.DOC.GetUnits(), UnitType.UT_Length, cvt_str, out double val);
            return s ? val : -1;
        }

        public static double AngleDbl(ModelInfo info, string angle_str) {
            bool s = UnitFormatUtils.TryParse(info.DOC.GetUnits(), UnitType.UT_Angle, angle_str, out double val);
            return s ? val : -1;
        }

        public static string LengthFromDbl(ModelInfo info, double dbl) {
            return UnitFormatUtils.Format(info.DOC.GetUnits(), UnitType.UT_Length, dbl, true, false, CustomFormatValue.FeetAndInches);
        }

        public static string AngleFromDouble(ModelInfo info, double dbl) {
            return UnitFormatUtils.Format(info.DOC.GetUnits(), UnitType.UT_Angle, dbl, true, false, CustomFormatValue.Angle);
        }    
#else // ForgeTypeId updated
        public static double LengthDbl(ModelInfo info, string cvt_str) {
            bool s = UnitFormatUtils.TryParse(info.DOC.GetUnits(), UnitTypeId.FeetFractionalInches, cvt_str, out double val);
            return s ? val : -1;
        }
        
        public static double AngleDbl(ModelInfo info, string angle_str) {
            bool s = UnitFormatUtils.TryParse(info.DOC.GetUnits(), UnitTypeId.Degrees, angle_str, out double val);
            return s ? val : -1;
        }

        public static string LengthFromDbl(ModelInfo info, double dbl) {
            return UnitFormatUtils.Format(info.DOC.GetUnits(), SpecTypeId.Length, dbl, false, CustomFormatValue.FeetAndInches);
        }

        public static string AngleFromDouble(ModelInfo info, double dbl) {
            return UnitFormatUtils.Format(info.DOC.GetUnits(), SpecTypeId.Angle, dbl, false, CustomFormatValue.Angle);
        }
#endif
    }
}
