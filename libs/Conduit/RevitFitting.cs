using System.Runtime.Serialization;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using JPMorrow.Revit.Documents;
using JPMorrow.Tools.Diagnostics;
using System.IO;
using JPMorrow.Revit.Text;

namespace JPMorrow.Revit.Tools.ConduitFittings
{
	// Represents a single fitting in a conduit run
	readonly public struct Fitting
	{
		public double Angle { get; }
		public double Diameter { get; }
		public string Type { get; }

		public string GetAngleString(ModelInfo info) => UnitFormatUtils.Format(info.DOC.GetUnits(), UnitType.UT_Angle, Angle, true, false, CustomFormatValue.Angle);

		public string GetDiameterString(ModelInfo info)
		{
			var opts = CustomFormatValue.FeetAndInches;

			return UnitFormatUtils.Format(info.DOC.GetUnits(), UnitType.UT_Length, Diameter, true, false, opts);
		}

		public static bool IsAboveLaborDiameterLimit(Units units, Fitting fitting)
		{
			UnitFormatUtils.TryParse(units, UnitType.UT_Length, "1 1/2\"", out double elbow_prune_diameter);
			return fitting.Diameter > elbow_prune_diameter;
		}

		public static string[] FittingMaterialTypes { get; } = new string[5] { "EMT", "RMC", "RNC", "IMC", "PVC" };

		private static string SelectMaterialType(string material_str)
		{
			var type = FittingMaterialTypes.ToList().FirstOrDefault(x => material_str.Contains(x));
			if(type == null)
				return FittingMaterialTypes.First();
			else
				return type;
		}

		public bool IsValid { get => Angle >= 0 || Diameter >= 0; }

		public Fitting(double angle, double diameter, string type)
		{
			Angle = angle;
			Diameter = diameter;
			Type = SelectMaterialType(type);
		}

		public Fitting(ModelInfo info, string angle, string diameter, string type)
		{
			bool s = true;
			double cvt_ang = -1;
			s = UnitFormatUtils.TryParse(info.DOC.GetUnits(), UnitType.UT_Angle, angle, out cvt_ang);

			double cvt_dia = -1;
			s = UnitFormatUtils.TryParse(info.DOC.GetUnits(), UnitType.UT_Length, diameter, out cvt_dia);

			if(!s)
			{
				Angle = -1;
				Diameter = -1;
				Type = SelectMaterialType(type);
				throw new Exception("Invalid angle or diameter on fitting (E0000001)");
			}
			else
			{
				Angle = cvt_ang >= 0 ? cvt_ang : 0;
				Diameter = cvt_dia >= 0 ? cvt_dia : 0;
				Type = SelectMaterialType(type);
			}
		}

		public static IEnumerable<Fitting> FittingsFromConduitIds(
			ModelInfo info, IEnumerable<int> ids)
		{
			List<Fitting> ret_fits = new List<Fitting>();

			foreach(var c_id in ids)
			{
				var id = new ElementId(c_id);
				Element conduit = info.DOC.GetElement(id);

				if (conduit == null || conduit.Category == null ||
				!conduit.Category.Name.Equals("Conduit Fittings") ||
				conduit.LookupParameter("Angle") == null ||
				conduit.LookupParameter("Nominal Diameter") == null) continue;

				if (!(conduit is FamilyInstance inst)) continue;

				var c_name = inst.Symbol.Family.Name;
				var angle = conduit.LookupParameter("Angle").AsDouble();
				var diameter = conduit.LookupParameter("Nominal Diameter").AsDouble();

				Fitting entry = new Fitting(angle, diameter, c_name);
				ret_fits.Add(entry);
			}

			return ret_fits;
		}

		public static IEnumerable<FittingCount> GetFittingCounts(IEnumerable<Fitting> fittings)
		{
			List<FittingCount> ret_fc = new List<FittingCount>();

			foreach(var fitting in fittings)
			{
				var entry = new FittingCount(fitting, 1);


				var max_angle_tolerance = fitting.Angle + 1;
				var min_angle_tolerance = fitting.Angle - 1;

				if(ret_fc.Any(x => (x.Fitting.Angle >= min_angle_tolerance && x.Fitting.Angle <= max_angle_tolerance) &&
				x.Fitting.Diameter.Equals(fitting.Diameter) &&
				x.Fitting.Type.Equals(fitting.Type)))
				{
					var old_entry = ret_fc.Find(
						x => (x.Fitting.Angle >= min_angle_tolerance &&
                              x.Fitting.Angle <= max_angle_tolerance) &&
						x.Fitting.Diameter.Equals(fitting.Diameter) &&
						x.Fitting.Type.Equals(fitting.Type));

					ret_fc.Remove(old_entry);
					var new_cnt = old_entry.Count + 1;
					ret_fc.Add(new FittingCount(fitting, new_cnt));
				}
				else
				{
					ret_fc.Add(entry);
				}
			}

			return ret_fc;
		}

		public static IEnumerable<FittingCount> GetFittingCounts(ModelInfo info, IEnumerable<int> ids)
		{
			var fittings = FittingsFromConduitIds(info, ids);
			return GetFittingCounts(fittings);
		}
	}

	readonly public struct FittingCount
	{
		public readonly Fitting Fitting { get; }
		public readonly int Count { get; }

		public FittingCount(Fitting fitting, int cnt)
		{
			Fitting = fitting;
			Count = cnt;
		}
	}
}
