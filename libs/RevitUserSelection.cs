using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JPMorrow.Tools.Revit.MEP.Selection
{
	/// <summary>
	/// Revit User Selection of conduit.
	/// </summary>
	public class RSelect
	{

		/// <summary>List of user selected elements</summary>
		private List<Element> _pickedElements = new List<Element>();

		/// <summary>is the user selected list of elements empty</summary>
		public bool IsEmpty { get { return _pickedElements.Count == 0 ? true : false; } }

		/// <summary>
		/// Promts the user to select 
		/// elements from the active document
		/// </summary>
		/// <param name="doc">Revit Document</param>
		/// <param name="uidoc">Revit UIDocument</param>
		/// <param name="type">type of elements to select</param>
		public RSelect(Document doc, UIDocument uidoc, SelectionType type, SelectionMode mode = SelectionMode.Active, bool multiple = true)
		{
			switch(type)
			{
				case SelectionType.Conduit:
					List<Element> els = new List<Element>();
					if(mode == SelectionMode.Active)
					{
						if (!multiple)
						{
							ElementId id = uidoc.Selection.PickObject(ObjectType.Element, new ConduitSelectionFilter(doc)).ElementId;
							els.Add(doc.GetElement(id));
						}
						else
						{
							els = ProcReferences(doc, uidoc.Selection.PickObjects(ObjectType.Element, new ConduitSelectionFilter(doc)).ToList());
						}
					}
					_pickedElements = els.Any() ? els : ProcSelectedIds(doc, uidoc, type);
					break;
			}
		}
		
		/// <summary>
		/// Proccess the references handed back by the user selection 
		/// </summary>
		/// <param name="doc">Revit Document</param>
		/// <param name="refs">references to proccess</param>
		/// <returns></returns>
		private List<Element> ProcReferences(Document doc, List<Reference> refs)
		{
			List<ElementId> ids = refs.Select(x => x.ElementId).ToList();
			List<Element> retList = new List<Element>();
			foreach(ElementId id in ids)
			{
				retList.Add(doc.GetElement(id));
			}
			return retList;
		}

		private List<Element> ProcSelectedIds (Document doc, UIDocument uidoc, SelectionType type)
		{
			List<ElementId> ids = uidoc.Selection.GetElementIds().ToList();
			List<Element> retList = new List<Element>();
			foreach (ElementId id in ids)
			{
				Element el = doc.GetElement(id);
				if (el.Category.Name == null) continue;
				if(el.Category.Name == "Conduits")
					retList.Add(el);
			}
			return retList;
		}

		/// <summary>
		/// Get the Elements that the user selected
		/// </summary>
		/// <returns></returns>
		public List<Element> GetUserPickedElements()
		{
			return _pickedElements;
		}

		public List<ElementId> GetUserPickedIds()
		{
			return _pickedElements.Select(x => x.Id).ToList();
		}
	}

	public enum SelectionType
	{
		Conduit = 0,
	}

	public enum SelectionMode
	{
		Passive = 0,
		Active = 1
	}

	public class ConduitSelectionFilter : ISelectionFilter
	{
		Document Doc { get; set; } = null;

		public bool AllowElement(Element elem)
		{
			if (Doc == null) return false;

			if (elem.Category.Name == "Conduits")
				return true;
			else
				return false;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			if (Doc == null) return false;
			Element elem = Doc.GetElement(reference.ElementId);
			if (elem.Category.Name == "Conduits")
				return true;
			else
				return false;
		}

		public ConduitSelectionFilter(Document doc)
		{
			Doc = doc;
		}
	}
}
