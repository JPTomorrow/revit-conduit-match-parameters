using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using JPMorrow.Tools.Revit.MEP.Selection;
using JPMorrow.Revit.ConduitRuns;
using JPMorrow.Revit.Documents;
using JPMorrow.Tools.Diagnostics;

namespace MainApp
{
	public struct IndexedSelection
	{
		public int idx;
		public Reference pick_reference;
	}

	[Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.DB.Macros.AddInId("58F7B2B7-BF6D-4B39-BBF8-13F7D9AAE97E")]
	public partial class ThisApplication : IExternalCommand
	{
		public Result Execute(ExternalCommandData cData, ref string message, ElementSet elements)
        {
			string[] dataDirectories = new string[0];
			bool debugApp = false;

			//set revit documents
			ModelInfo revit_info = ModelInfo.StoreDocuments(cData, dataDirectories, debugApp);

			//spool up search system
			List<Reference> highlighted_elements = new List<Reference>();

			bool running = true;
			while(running)
			{
				//get user to select conduit
				List<Reference> selected_conduit = new List<Reference>();

				//try catch to tell if cancelled
				try
				{
					while(selected_conduit.Count < 2)
						selected_conduit.Add(revit_info.UIDOC.Selection.PickObject(ObjectType.Element, new ConduitSelectionFilter(revit_info.DOC),  "Select a Conduit"));
				}
				catch
				{
					running = false;
					continue;
				}

				if(!selected_conduit.Any() || selected_conduit.Count < 2) return Result.Succeeded;
				highlighted_elements.AddRange(selected_conduit);

				//separate first conduit from the rest
				Element conToProp = revit_info.DOC.GetElement(selected_conduit.First());
				selected_conduit.Remove(selected_conduit.First());
				List<Element> conduits_to_propogate = new List<Element>();
				selected_conduit.Select(x => x.ElementId).ToList().ForEach(y => conduits_to_propogate.Add(revit_info
				.DOC.GetElement(y)));

				//local functions
				Parameter p(Element x, string str) => x.LookupParameter(str);
				bool p_null(Element x, string str) => p(x, str) == null;

				//check for parameters and quit if null
				if (p_null(conToProp, "From") || p_null(conToProp, "To") ||
					p_null(conToProp, "Wire Size") || p_null(conToProp, "Comments"))
				{
					debugger.show(
						header: "Conduit Match Params", sub: "Parameters",
						err: "You do not have the 'To', 'From', or 'Wire Size' parameters loaded for conduits.");
					return Result.Succeeded;
				}

				using (TransactionGroup tgx = new TransactionGroup(revit_info.DOC, "Propogating parameters"))
				{
					tgx.Start();

					using (Transaction tx = new Transaction(revit_info.DOC, "clear run id"))
					{
						tx.Start();
						foreach(var conduit in conduits_to_propogate)
						{
							RunNetwork rn = new RunNetwork(conduit);
							foreach(var id in rn.RunIds.Concat(rn.FittingIds))
							{
								Element el = revit_info.DOC.GetElement(new ElementId(id));
								p(el, "From").Set(		p(conToProp, "From").AsString());
								p(el, "To").Set(		p(conToProp, "To").AsString());
								p(el, "Wire Size").Set(	p(conToProp, "Wire Size").AsString());
								p(el, "Comments").Set(	p(conToProp, "Comments").AsString());
							}
						}
						tx.Commit();
					}
					tgx.Assimilate();
				}
			}
			revit_info.UIDOC.Selection.SetElementIds(highlighted_elements.Select(x => x.ElementId).ToList());
			return Result.Succeeded;
        }

		#region startup
		private void Module_Startup(object sender, EventArgs e)
		{

		}

		private void Module_Shutdown(object sender, EventArgs e)
		{

		}
		#endregion

		#region Revit Macros generated code
		private void InternalStartup()
		{
			this.Startup += new System.EventHandler(Module_Startup);
			this.Shutdown += new System.EventHandler(Module_Shutdown);
		}
		#endregion
	}
}