/*
	This file handles cable tray from a revit model and
	packages it to be used by other systems, namely UI, in the program.

	Author: Justin Morrow
	Date Created: 9/23/2020
*/


using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;

namespace JPMorrow.Revit.CableTray
{
	[DataContract]
	public class CableTrayInfo {
		[DataMember]
		public string From { get; private set; }
		[DataMember]
		public string To { get; private set; }
		[DataMember]
		public int[] TrayIds { get; private set; }
		[DataMember]
		public int[] FittingIds { get; private set; }


		private CableTrayInfo(
			string from, string to,
			IEnumerable<int> tray_ids,
			IEnumerable<int> fitting_ids) {

				From = from;
				To = to;
				TrayIds = tray_ids.ToArray();
				FittingIds = fitting_ids.ToArray();
		}

		public static IEnumerable<CableTrayInfo> MakeCableTrays(ModelInfo info, TrayNetwork network) {
			return null;
		}


	}

	public class TrayNetwork {

		public int[] TrayIds { get; private set; }
		public int[] FittingIds { get; private set; }

		private TrayNetwork() {

		}

		public static TrayNetwork ParseTrayNetworkFromElements(ModelInfo info, IEnumerable<ElementId> ids) {

			List<ElementId> exclude_list = new List<ElementId>();
			bool excluded(ElementId id) => exclude_list.Any(x => x.IntegerValue == id.IntegerValue);

			foreach(var id in ids) {
				var el = info.DOC.GetElement(id);
				if(excluded(id)) continue;


			}

			return null;
		}


	}
}