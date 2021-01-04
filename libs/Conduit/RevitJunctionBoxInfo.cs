using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using JPMorrow.Tools.Diagnostics;
using JPMorrow.Revit.Documents;

namespace JPMorrow.Revit.Tools
{
	public struct JBoxInfo
	{
		public int jbox;
		public int connections;
		public int[] connected_conduit_ids;
	}

	public static class RJBI
	{
		public static JBoxInfo ParseJunctionBox(ElementId jbox_id, ModelInfo info)
		{
			JBoxInfo jbi = new JBoxInfo();
			jbi.connections = 0;
			jbi.jbox = jbox_id.IntegerValue;

			Element jbox = info.DOC.GetElement(jbox_id);

			if(jbox.Category.Name != "Electrical Fixtures")
				throw new Exception("The Element is not a junction box.");

			List<int> temp_ids = new List<int>();
			foreach (Connector c in GetConnectors(jbox))
			{
				if(c.ConnectorType != ConnectorType.End) continue;
				if(c.IsConnected)
				{
					jbi.connections++;
					Element el_to_add = GetConnectedConduit(c);
					if(el_to_add != null)
						temp_ids.Add(el_to_add.Id.IntegerValue);
				}
			}

			//fill connected conduit array
			jbi.connected_conduit_ids = new int[temp_ids.Count()];
			temp_ids.CopyTo(jbi.connected_conduit_ids, 0);

			return jbi;
		}

		private static Element GetConnectedConduit(Connector c)
		{
			if(c.Owner.Category.Name != "Electrical Fixtures")
				throw new Exception("GetConnectedConduit(): A non-electrical fixture element was fed.");

			Connector ret_c = null;
			foreach(Connector c2 in c.AllRefs)
			{
				if(!c2.Origin.IsAlmostEqualTo(c.Origin)) continue;
				ret_c = c2;
			}

			if(ret_c == null) return null;

			return ret_c.Owner;
		}

		/// <summary>
		/// Return the given element's connector set.
		/// </summary>
		private static ConnectorSet GetConnectors(Element e)
		{
			if (e is MEPCurve)
				return ((MEPCurve)e)?
				.ConnectorManager?
					.Connectors;

			if (e is FamilyInstance)
				return ((FamilyInstance)e)?
				.MEPModel?
					.ConnectorManager?
					.Connectors;

			return null;
		}
	}
}