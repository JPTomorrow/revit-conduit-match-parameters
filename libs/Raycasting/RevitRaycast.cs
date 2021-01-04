using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using JPMorrow.Revit.Documents;
using JPMorrow.Tools.Diagnostics;

namespace JPMorrow.Revit.Tools
{
	public struct RRay
	{
		public RRayCollision[] collisions;
		public XYZ direction;
		public XYZ start_point;

		public bool GetFarthestCollision(out RRayCollision col)
		{
			if(!collisions.Any())
			{
				col = new RRayCollision();
				return false;
			}
			var max = collisions.Max(y => y.distance);
			col = collisions.Where(x => x.distance == max).First();
			return true;
		}

		public bool GetNearestCollision(out RRayCollision col)
		{
			if(!collisions.Any())
			{
				col = new RRayCollision();
				return false;
			}
			var min = collisions.Min(y => y.distance);
			col = collisions.Where(x => x.distance == min).First();
			return true;
		}
	}

	public struct RRayCollision
	{
		public XYZ point;
		public double distance;
		public ElementId other_id;
	}

	public static class RevitRaycast
	{
		/// <summary>
		/// Cast a ray in a direction and return all collisions
		/// </summary>
		public static RRay Cast(ModelInfo info, View3D view, List<BuiltInCategory> find_cats, XYZ origin_pt, XYZ ray_dir, double max_distance = -1, List<ElementId> ids_to_ignore = null)
		{
			RRay ray = new RRay
			{
				direction = ray_dir,
				start_point = origin_pt
			};

			bool prune_lengths = max_distance >= 0;

			ReferenceIntersector ref_intersect = new ReferenceIntersector(
				new ElementMulticategoryFilter(find_cats), FindReferenceTarget.Element, view)
			{
				FindReferencesInRevitLinks = true
			};

			List<ReferenceWithContext> rwcs = new List<ReferenceWithContext>();
			rwcs = ref_intersect.Find(ray.start_point, ray.direction).ToList();

			if(prune_lengths)
			{
				foreach(var rwc in rwcs.ToArray())
				{
					if(rwc.Proximity > max_distance)
						rwcs.Remove(rwc);
				}
			}

			List<RRayCollision> temp_collisions_storage = new List<RRayCollision>();
			if(ids_to_ignore == null)
				ids_to_ignore =  new List<ElementId>();

			foreach(var rwc in rwcs)
			{
				Reference r = rwc.GetReference();
				if(ids_to_ignore.Any(x => x.IntegerValue == r.ElementId.IntegerValue)) continue;

				Element collided_element = info.DOC.GetElement(r.ElementId);
				if(collided_element == null) continue;

				RRayCollision ray_collision = new RRayCollision();
				if(max_distance == -1)
				{
					ray_collision.distance = rwc.Proximity;
					ray_collision.other_id = collided_element.Id;
					ray_collision.point = r.GlobalPoint;
					temp_collisions_storage.Add(ray_collision);
				}
				else
				{
					if(rwc.Proximity <= max_distance)
					{
						ray_collision.distance = rwc.Proximity;
						ray_collision.other_id = collided_element.Id;
						ray_collision.point = r.GlobalPoint;
						temp_collisions_storage.Add(ray_collision);
					}
				}
			}

			ray.collisions = temp_collisions_storage.ToArray();
			return ray;
		}

        public static IEnumerable<ElementId> CastSphere(
            ModelInfo info, IEnumerable<XYZ> pts, double radius,
            BuiltInCategory bic = BuiltInCategory.INVALID, View view = null)
		{
            if(!pts.Any()) return new List<ElementId>();
            
            var solids = new Queue<Solid>();
            foreach(var pt in pts)
                solids.Enqueue(CreateSphereAt(pt, radius));

            var union_solid = solids.Dequeue();

            while(solids.Any()) {
                var other = solids.Dequeue();
                union_solid = BooleanOperationsUtils.ExecuteBooleanOperation(union_solid, other, BooleanOperationsType.Union);
            }

			ElementIntersectsSolidFilter intersectSphere = new ElementIntersectsSolidFilter(union_solid);
			FilteredElementCollector coll;
            coll = view != null ? new FilteredElementCollector(info.DOC, view.Id) : new FilteredElementCollector(info.DOC);

			var intersection = bic == BuiltInCategory.INVALID ?
                coll.WherePasses(intersectSphere).ToElementIds() :
                coll.WherePasses(intersectSphere).OfCategory(bic).ToElementIds();

			return intersection;
		}

		public static IEnumerable<ElementId> CastSphere(
            ModelInfo info, XYZ start_pt, double radius,
            BuiltInCategory bic = BuiltInCategory.INVALID, View view = null)
		{
			Solid sphere = CreateSphereAt(start_pt, radius);
			ElementIntersectsSolidFilter intersectSphere = new ElementIntersectsSolidFilter(sphere);
			FilteredElementCollector coll;
            coll = view != null ? new FilteredElementCollector(info.DOC, view.Id) : new FilteredElementCollector(info.DOC);

			var intersection = bic == BuiltInCategory.INVALID ?
                coll.WherePasses(intersectSphere).ToElementIds() :
                coll.WherePasses(intersectSphere).OfCategory(bic).ToElementIds();

			return intersection;
		}

        public static Solid CreateSphereAt(XYZ center, double radius) {
			Frame frame = new Frame( center,
			XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ );

			// Create a vertical half-circle loop;
			// this must be in the frame location.

			Arc arc = Arc.Create(
			center - radius * XYZ.BasisZ,
			center + radius * XYZ.BasisZ,
			center + radius * XYZ.BasisX );

			Line line = Line.CreateBound(
			arc.GetEndPoint(1),
			arc.GetEndPoint(0) );

			CurveLoop halfCircle = new CurveLoop();
			halfCircle.Append( arc );
			halfCircle.Append( line );

			List<CurveLoop> loops = new List<CurveLoop>( 1 );
			loops.Add( halfCircle );

			return GeometryCreationUtilities
			.CreateRevolvedGeometry(
				frame, loops, 0, 2 * Math.PI );
	    }
        
        public static IEnumerable<ElementId> CastBoundingBox(
            ModelInfo info, BoundingBoxXYZ bb, BuiltInCategory bic = BuiltInCategory.INVALID, View view = null) {

            Outline outline = new Outline(bb.Min, bb.Max);

            BoundingBoxIntersectsFilter filter = new BoundingBoxIntersectsFilter(outline);

            FilteredElementCollector coll;
            coll = view != null ? new FilteredElementCollector(info.DOC, view.Id) : new FilteredElementCollector(info.DOC);

            var intersection = bic == BuiltInCategory.INVALID ?
                coll.WherePasses(filter).ToElementIds() :
                coll.WherePasses(filter).OfCategory(bic).ToElementIds();
            
            return intersection;
        }

		/// <summary>
		/// Create model lines in a 3D view
		/// </summary>
		public static void DrawModelLines(ModelInfo info, ExternalEvent ev, InsertModelLine iml,  WorksetId workset_id, XYZ[] pts = null)
		{
			if(pts == null || !pts.Any())
				throw new Exception("No points were provided for drawing.");

			if(pts.Count() % 2 != 0)
				throw new Exception("Odd number of points feed to the display model lines.");

			iml.Info = info;
			iml.Line_Points = pts;
			iml.Workset_Id = workset_id;
			ev.Raise();
		}
	}

	public class InsertModelLine : IExternalEventHandler
	{
		public ModelInfo Info { get; set; }
		public XYZ[] Line_Points { get; set; }
		public WorksetId Workset_Id { get; set; }

		public void Execute(UIApplication app)
		{
			using (Transaction tx = new Transaction(Info.DOC, "placing model line"))
			{
				tx.Start();
				List<XYZ> pts_queue = new List<XYZ>(Line_Points);
				while(pts_queue.Count > 0)
				{
					XYZ[] current_pts = pts_queue.Take(2).ToArray();
					foreach(var pt in current_pts)
						pts_queue.Remove(pt);

					string line_str_style = "<Hidden>"; // system linestyle guaranteed to exist
					Create3DModelLine(current_pts[0], current_pts[1], line_str_style, Workset_Id);
				}
				tx.Commit();
			}
		}

		public SketchPlane NewSketchPlanePassLine(Line line)
		{
			XYZ p = line.GetEndPoint(0);
			XYZ q = line.GetEndPoint(1);
			XYZ norm = new XYZ(-10000, -10000, 0);
			Plane plane = Plane.CreateByThreePoints(p, q, norm);
			SketchPlane skPlane = SketchPlane.Create(Info.DOC, plane);
			return skPlane;
		}

		public void Create3DModelLine(XYZ p, XYZ q, string line_style, WorksetId id)
		{
			try
			{
				if (p.IsAlmostEqualTo(q))
				{
					debugger.show(err: "Expected two different points.");
					return;
				}
				Line line = Line.CreateBound(p, q);
				if (null == line)
				{
					debugger.show(err: "Geometry line creation failed.");
					return;
				}

				ModelCurve model_line_curve = null;
				model_line_curve = Info.DOC.Create.NewModelCurve(line, NewSketchPlanePassLine(line));

				Parameter workset_param = model_line_curve.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM );
				workset_param.Set(Workset_Id.IntegerValue);

				// set linestyle
				ICollection<ElementId> styles = model_line_curve.GetLineStyleIds();
				foreach(ElementId eid in styles)
				{
					Element e = Info.DOC.GetElement(eid);
					if (e.Name == line_style)
					{
						model_line_curve.LineStyle = e;
						break;
					}
				}
			}
			catch (Exception ex)
			{
				debugger.show(err:ex.ToString());
			}
		}

		public string GetName()
		{
			return "Insert Model Line";
		}
	}
}
