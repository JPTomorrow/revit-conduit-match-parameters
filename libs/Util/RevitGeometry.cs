using System;
using System.Collections.Generic;
using System.Linq;
using MoreLinq;
using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;
using JPMorrow.Tools.Diagnostics;
using System.Runtime.Serialization;

namespace JPMorrow.Revit.Tools
{

    public static class RGeo
	{
		internal static class PrimitiveDirection
		{
			public static XYZ Up { get; } = new XYZ(0, 0, 1);
			public static XYZ Down { get; } = new XYZ(0, 0, -1);
			public static XYZ XLeft { get; } = new XYZ(-1, 0, 0);
			public static XYZ XRight { get; } = new XYZ(1, 0, 0);
			public static XYZ YLeft { get; } = new XYZ(0, -1, 0);
			public static XYZ YRight { get; } = new XYZ(0, 1, 0);
		}

        // returns a solid representing
        // the crop box of a floor plan view
        // even if it is irregular
        public static Solid CorrectIrregularCropBox(ModelInfo info, View view, double scale) {
            
            var range = (view as ViewPlan).GetViewRange();
            var crop_shape = view.GetCropRegionShapeManager().GetCropShape();
            
            var top_lvl = info.DOC.GetElement(range.GetLevelId(PlanViewPlane.TopClipPlane)) as Level;
            var bottom_lvl = info.DOC.GetElement(range.GetLevelId(PlanViewPlane.BottomClipPlane)) as Level;
            var top_z = range.GetOffset(PlanViewPlane.TopClipPlane) + top_lvl.Elevation;
            var bottom_z = range.GetOffset(PlanViewPlane.BottomClipPlane) + bottom_lvl.Elevation;

            var corrected_loops = new List<CurveLoop>();
            foreach(var l in crop_shape) {
                var loop = l.ToList();
                var edges = new List<Curve>();
                
                foreach(var c in loop) {
                    var e1 = c.GetEndPoint(0);
                    var e2 = c.GetEndPoint(1);
                    edges.Add(Line.CreateBound(new XYZ(e1.X, e1.Y, top_z), new XYZ(e2.X, e2.Y, top_z)));
                }

                var new_loop = CurveLoop.Create(edges);
                new_loop = new_loop.ScaleLoop(scale);

                debugger.debug_show(err:l.PrintGeoGebraFormattedPoints() + "\n" + new_loop.PrintGeoGebraFormattedPoints());
                corrected_loops.Add(new_loop);
            }
            
            var solid = GeometryCreationUtilities
                .CreateExtrusionGeometry(corrected_loops, RGeo.PrimitiveDirection.Down, top_z - bottom_z );
            
            return solid;
        }

        public static IEnumerable<XYZ> DerivePointsOnLine(
            Line line, double distance = 1) {
            
            var d = distance;
            var dd = distance;

            var ret_pts = new List<XYZ>();
            while(dd <= line.Length) {
                ret_pts.Add(DerivePointBetween(line, dd));
                dd += d;
            }
            
            return ret_pts;
        }

		public static XYZ DerivePointBetween(Line line, double distance = 1)
		{
			return DerivePointBetween(line.GetEndPoint(0), line.GetEndPoint(1), distance);
		}

		/// <summary>
		/// Get a point between two points that is X distance away
		/// </summary>
		/// <param name="start">starting XYZ coordinate</param>
		/// <param name="end">ending XYZ coordinate</param>
		/// <param name="distance">distance of next point</param>
		/// <returns>a point derived in between</returns>
		public static XYZ DerivePointBetween(XYZ start, XYZ end, double distance = 1)
		{
			double fi = Math.Atan2(end.Y - start.Y, end.X - start.X);
			// Your final point
			XYZ xyz = new XYZ(
                start.X + distance * Math.Cos(fi),
				start.Y + distance * Math.Sin(fi), end.Z);
            
			return xyz;
		}

		public static Line ExtendLineEndPoint(Line line, double length_to_extend)
		{
			var orig_length = line.Length;
			var ep1 = line.GetEndPoint(0);
			var ep2 = line.GetEndPoint(1);


			var x = ep2.X + (ep2.X - ep1.X) / orig_length * length_to_extend;
			var y = ep2.Y + (ep2.Y - ep1.Y) / orig_length * length_to_extend;

			XYZ new_ep = new XYZ(x, y, ep2.Z);
			return Line.CreateBound(ep1, new_ep);
		}

		public static bool IsLeft(Line line, XYZ chk_pt)
		{
			var a = line.GetEndPoint(0);
			var b = line.GetEndPoint(1);
			return Math.Sign((b.X - a.X) * (chk_pt.Y - a.Y) - (b.Y - a.Y) * (chk_pt.X - a.X)) < 0;
		}

		public static bool IsRight(Line line, XYZ chk_pt)
		{
			var a = line.GetEndPoint(0);
			var b = line.GetEndPoint(1);
			return Math.Sign((b.X - a.X) * (chk_pt.Y - a.Y) - (b.Y - a.Y) * (chk_pt.X - a.X)) > 0;
		}

		public static bool IsPtOnLine(Line line, XYZ chk_pt)
		{
			var a = line.GetEndPoint(0);
			var b = line.GetEndPoint(1);
			return Math.Sign((b.X - a.X) * (chk_pt.Y - a.Y) - (b.Y - a.Y) * (chk_pt.X - a.X)) == 0;
		}

		public static double AngleBetweenLines(Line line1, Line line2)
		{
			var d1 = line1.Direction;
			var d2 = line2.Direction;
			return Math.Acos(d1.Normalize().DotProduct(d2.Normalize()));
		}

		public static bool IsRotationClockwise(XYZ a, XYZ b, XYZ c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 0;

		public static IEnumerable<XYZ> GetBoundingBoxLowerPts(BoundingBoxXYZ bb) {
		    return new List<XYZ>() {
				new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
				new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
				new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
				new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
			};
		}
	}

	/// <summary>
	/// Geometry based extension methods
	/// </summary>
	public static class RGeo_Ext
	{
		/// <summary>
		/// Get a printable string of an XYZ structure
		/// </summary>
		public static string PrintPt(this XYZ pt)
		{
			return String.Format(
				"({0}, {1}, {2})",
				pt.X, pt.Y, pt.Z
			);
		}

        public static string PrintPts(this Solid solid) {
            string o = "";

            foreach(var e in solid.Edges) {
                
                var e1 = (e as Edge).AsCurve().GetEndPoint(0);
                var e2 = (e as Edge).AsCurve().GetEndPoint(1);
                o +=
                    e1.ToString() + "\n" +
                    e2.ToString() + "\n" +
                    "------------------------------\n";
            }
            
            return o;
        }

        public static bool BoundingBoxContains(this BoundingBoxXYZ bb, XYZ p) {
            
            if( bb.Min.X <= p.X && bb.Max.X >= p.X &&
                bb.Min.Y <= p.Y && bb.Max.Y >= p.Y &&
                bb.Min.Z <= p.Z && bb.Max.Z >= p.Z)
            {
                return true;
            }
                
            return false;
        }

        public static string PrintPtsInsideBox(this Solid solid, BoundingBoxXYZ bb) {
            
            string o = "";

            o += "Bounding Box Coordinates" + "\n";
            o += bb.Min.ToString() + " " + bb.BoundingBoxContains(bb.Min).ToString() + "\n";
            o += bb.Max.ToString() + " " + bb.BoundingBoxContains(bb.Max).ToString() + "\n";
            o += "------------------------------\n\n";
            o += "Points For Each Edge Of Solid" + "\n";

            foreach(var e in solid.Edges) {
                
                var e1 = (e as Edge).AsCurve().GetEndPoint(0);
                var e2 = (e as Edge).AsCurve().GetEndPoint(1);
                o +=
                    e1.ToString() + " " + bb.BoundingBoxContains(e1).ToString() + "\n" +
                    e2.ToString() + " " + bb.BoundingBoxContains(e2).ToString() + "\n" +
                    "------------------------------\n";
            }
            
            return o;
        }

        public static bool IsPtInsideSolid(this Solid solid, XYZ pt) {

            var ep1s = solid.Edges.Cast<Edge>().Select(x => x.AsCurve().GetEndPoint(0));
            var ep2s = solid.Edges.Cast<Edge>().Select(x => x.AsCurve().GetEndPoint(1));
            var eps = ep1s.Concat(ep2s);

            var min_x = eps.MinBy(x => x.X).First().X;
            var min_y = eps.MinBy(x => x.Y).First().Y;
            var min_z = eps.MinBy(x => x.Z).First().Z;
            
            var max_x = eps.MaxBy(x => x.X).First().X;
            var max_y = eps.MaxBy(x => x.Y).First().Y;
            var max_z = eps.MaxBy(x => x.Z).First().Z;

            var min_pt = new XYZ(min_x, min_y, min_z);
            var max_pt = new XYZ(max_x, max_y, max_z);

            BoundingBoxXYZ bb = new BoundingBoxXYZ();
            bb.Min = min_pt;
            bb.Max = max_pt;

            return bb.BoundingBoxContains(pt);
        }

        public static UV ComputeCurveLoopCentroid(this CurveLoop loop) {

            var pts = new List<UV>();
            
            foreach(var c in loop) {
                var eps = new[] { c.GetEndPoint(0), c.GetEndPoint(1)};
                pts.AddRange(eps.Select(x => new UV(x.X, x.Y)));
            }
            
            pts = pts.DistinctBy(x => new { x.U, x.V }).ToList();
            int vertex_count = pts.Count();
            
            double[] centroid = new[] { 0.0, 0.0 };
            double signedArea = 0.0;
            double x0 = 0.0; // Current vertex X
            double y0 = 0.0; // Current vertex Y
            double x1 = 0.0; // Next vertex X
            double y1 = 0.0; // Next vertex Y
            double a = 0.0;  // Partial signed area

            // For all vertices except last
            int i = 0;
            for (i = 0; i < vertex_count - 1; ++i)
            {
                x0 = pts[i].U;
                y0 = pts[i].V;
                x1 = pts[i+1].U;
                y1 = pts[i+1].V;
                a = x0*y1 - x1*y0;
                signedArea += a;
                centroid[0] += (x0 + x1)*a;
                centroid[1] += (y0 + y1)*a;
            }
            
            // Do last vertex separately to avoid performing an expensive
            // modulus operation in each iteration.
            x0 = pts[i].U;
            y0 = pts[i].V;
            x1 = pts[0].U;
            y1 = pts[0].V;
            a = x0*y1 - x1*y0;
            signedArea += a;
            centroid[0] += (x0 + x1)*a;
            centroid[1] += (y0 + y1)*a;

            signedArea *= 0.5;
            centroid[0] /= (6.0*signedArea);
            centroid[1] /= (6.0*signedArea);

            return new UV(centroid[0], centroid[1]);
        }

        public static CurveLoop ScaleLoop(this CurveLoop loop, double scale = 1.0) {

            var old_centroid = loop.ComputeCurveLoopCentroid();

            List<Curve> new_curves = new List<Curve>();
            foreach(var c in loop) {
                var eps = new[] { c.GetEndPoint(0), c.GetEndPoint(1) };
                // @CONTINUE
            }
            
            Transform scale_trans = Transform.Identity;
            scale_trans = scale_trans.ScaleBasisAndOrigin(scale);
            var new_loop = CurveLoop.CreateViaTransform(loop, scale_trans);

            
            
            
            var new_centroid = new_loop.ComputeCurveLoopCentroid();
            var trans = Transform.CreateTranslation(new XYZ(old_centroid.U, old_centroid.V, 0) - new XYZ(new_centroid.U, new_centroid.V, 0));
            new_loop.Transform(trans);

            return new_loop;
        }

        // get a string formated to be used with the web tool GeoGebra TM
        public static string PrintGeoGebraFormattedPoints(this CurveLoop loop) {

            string o = "";
            
            foreach(var c in loop) {
                var eps = new[] { c.GetEndPoint(0), c.GetEndPoint(1)};
                o += string.Format(
                    "Segment(({0},{1}),({2},{3}))",
                    eps[0].X, eps[0].Y, eps[1].X, eps[1].Y) + "\n";
            }

            return o;
        }
	}

    // XYZ point that can be saved to a file unlike revit.
    [DataContract]
    public class XYZSerializable {
        
        [DataMember]
        public double X { get; private set; }
        [DataMember]
        public double Y { get; private set; }
        [DataMember]
        public double Z { get; private set; }

        public XYZSerializable(XYZ pt) {
            X = pt.X;
            Y = pt.Y;
            Z = pt.Z;
        }

        public XYZSerializable(double x, double y, double z) {
            X = x;
            Y = y;
            Z = z;
        }

        public XYZ RevitPoint() {
            return new XYZ(X, Y, Z);
        }
    }
}
