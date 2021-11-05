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
using JPMorrow.Revit.Measurements;

namespace JPMorrow.Revit.Tools.ConduitFittings
{
	// Represents a single fitting in a conduit run
	public class Fitting
	{
		public double Angle { get; }
		public double Diameter { get; }
		public string Type { get; }

		public string GetAngleString(ModelInfo info) => RMeasure.AngleFromDouble(info.DOC, Angle);
		public string GetDiameterString(ModelInfo info) => RMeasure.LengthFromDbl(info.DOC, Diameter);

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
			double cvt_ang = RMeasure.AngleDbl(info.DOC, angle);
			double cvt_dia = RMeasure.LengthDbl(info.DOC, diameter);

			if(cvt_ang == -1 || cvt_dia == -1)
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
	}
}
