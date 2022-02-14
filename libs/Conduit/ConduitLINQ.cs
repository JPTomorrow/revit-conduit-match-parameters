using Autodesk.Revit.DB;
using System;
using JPMorrow.Revit.Documents;
using JPMorrow.Revit.Tools;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB.Electrical;
using JPMorrow.Revit.Measurements;

public static class ConduitLINQ
{
	private static Exception NaConduit(Element conduit) => new Exception("This is not a piece of conduit: (ElementID) " + conduit.Id.ToString());

	public static double ConduitLength(this Element conduit)
	{
		if(!conduit.Category.Name.Equals("Conduits"))
			throw NaConduit(conduit);

		Curve conduit_curve = (conduit.Location as LocationCurve).Curve;
		return conduit_curve.Length;
	}

	public static Line GetConduitLine(this Conduit conduit, bool reversed = false)
	{
		Curve conduit_curve = (conduit.Location as LocationCurve).Curve;
		var endpoint1 = conduit_curve.GetEndPoint(reversed ? 1 : 0);
		var endpoint2 = conduit_curve.GetEndPoint(reversed ? 0 : 1);
		return Line.CreateBound(endpoint1, endpoint2);
	}

	public static bool GetNextRackedConduits(
		this Element conduit, ModelInfo info,
		View3D view,  IEnumerable<BuiltInCategory> clash_categories, IEnumerable<BuiltInCategory> conduit_categories, out List<Element> next_racked_conduits, double chk_dist = -1)
	{
		double parse_len(string len_str) => RMeasure.LengthDbl(info, len_str);

		if(chk_dist == -1) chk_dist = parse_len("3\"");

		if(!conduit.Category.Name.Equals("Conduits"))
			throw NaConduit(conduit);

		// derive point along start conduit to start all checks
		Curve start_curve = (conduit.Location as LocationCurve).Curve;
		XYZ conduit_dir = Line.CreateBound(start_curve.GetEndPoint(0), start_curve.GetEndPoint(1)).Direction;
		XYZ chk_pt = RGeo.DerivePointBetween(start_curve.GetEndPoint(0), start_curve.GetEndPoint(1), chk_dist);

		// CHECK RIGHT VECOR, FOLLOWED BY LEFT, FOLLOWED BY DOWN, FOLLOWED BY UP
		XYZ foward = conduit_dir;
		XYZ down = RGeo.PrimitiveDirection.Down;
		XYZ up = RGeo.PrimitiveDirection.Up;
		XYZ right = foward.Normalize().CrossProduct(up.Normalize());
		XYZ left = -right;

		var between_pipe_distance = parse_len("8\"");

		// cast rays
		RRay right_ray = RevitRaycast.Cast(info, view, conduit_categories.ToList(), chk_pt, right, max_distance:between_pipe_distance);
		RRay left_ray = RevitRaycast.Cast(info, view, conduit_categories.ToList(), chk_pt, left, max_distance:between_pipe_distance);
		RRay down_ray = RevitRaycast.Cast(info, view, conduit_categories.ToList(), chk_pt, down, max_distance:between_pipe_distance);
		RRay up_ray = RevitRaycast.Cast(info, view, conduit_categories.ToList(), chk_pt, up, max_distance:between_pipe_distance);

		RRayCollision first_collision;
		first_collision.other_id = new ElementId(-1);
		next_racked_conduits = new List<Element>();
		bool s = false;

		if(right_ray.collisions.Any(x => conduit.Id.IntegerValue != x.other_id.IntegerValue))
		{
			first_collision = right_ray.collisions.First(x => conduit.Id.IntegerValue != x.other_id.IntegerValue);
			next_racked_conduits.Add(info.DOC.GetElement(first_collision.other_id));
			s = !s;
		}

		if(left_ray.collisions.Any(x => conduit.Id.IntegerValue != x.other_id.IntegerValue))
		{
			first_collision = left_ray.collisions.First(x => conduit.Id.IntegerValue != x.other_id.IntegerValue);
			next_racked_conduits.Add(info.DOC.GetElement(first_collision.other_id));
			s = !s;
		}

		if(down_ray.collisions.Any(x => conduit.Id.IntegerValue != x.other_id.IntegerValue))
		{
			first_collision = down_ray.collisions.First(x => conduit.Id.IntegerValue != x.other_id.IntegerValue);
			next_racked_conduits.Add(info.DOC.GetElement(first_collision.other_id));
			s = !s;
		}

		if(up_ray.collisions.Any(x => conduit.Id.IntegerValue != x.other_id.IntegerValue))
		{
			first_collision = up_ray.collisions.First(x => conduit.Id.IntegerValue != x.other_id.IntegerValue);
			next_racked_conduits.Add(info.DOC.GetElement(first_collision.other_id));
			s = !s;
		}

		return s;
	}
}