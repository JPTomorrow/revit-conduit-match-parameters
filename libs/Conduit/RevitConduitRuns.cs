using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;
using JPMorrow.Revit.Tools;
using JPMorrow.Tools.Diagnostics;
using JPMorrow.Revit.RvtMiscUtil;
using System.IO;
using JPMorrow.Revit.Wires;
using JPMorrow.Revit.Tools.ConduitFittings;
using JPMorrow.Revit.Text;

namespace JPMorrow.Revit.ConduitRuns
{
	/// <summary>
	/// Represents a conduit run in Revit
	/// </summary>
	[DataContract]
	public class ConduitRunInfo
	{
		[DataMember]
		public List<int> ConduitIds { get; private set; }
		[DataMember]
		public List<int> FittingIds { get; private set; }
		[DataMember]
		public RunEndpointInfo[] RunEndpointInfo { get; private set; }
		[DataMember]
		public string From { get; private set; }
		[DataMember]
		public string To { get; private set; }
		[DataMember]
		public double Diameter { get; private set; }
		[DataMember]
		public double Length { get; private set; }
		[DataMember]
		public string ConduitMaterialType { get; private set; }
		[DataMember]
		public double FittingBends { get; private set; }
		[DataMember]
		public int GeneratingConduitId { get; private set; }

		public int[] WireIds { get => ConduitIds.Concat(FittingIds).ToArray(); }

		// string conversions
		public string DiameterStr(ModelInfo info) => UnitFormatUtils.Format(info.DOC.GetUnits(), UnitType.UT_Length, Diameter, true, false, CustomFormatValue.FeetAndInches);
		public string LengthStr(ModelInfo info) => UnitFormatUtils.Format(info.DOC.GetUnits(), UnitType.UT_Length, Length, true, false, CustomFormatValue.FeetAndInches);
		public string FittingBendsStr(ModelInfo info) => UnitFormatUtils.Format(info.DOC.GetUnits(), UnitType.UT_Angle, FittingBends, true, false);

		public void OverrideToStr(string val) => To = val;

		// Different types of possible conduit material
		public static string[] ConduitMaterialTypes { get; } = new string[]{
			"EMT", "PVC", "RNC", "FLEX", "IMC", "RMC"
		};

		private ConduitRunInfo(
			ModelInfo info, RunNetwork rn, int[] jbox_ids)
		{
			GeneratingConduitId = rn.StartId;
			ConduitIds = new List<int>(rn.RunIds);
			FittingIds = new List<int>(rn.FittingIds);
			RunEndpointInfo = new RunEndpointInfo[2];

			// get endpoint info
			for(var i = 0; i < 2; i++)
			{
				if(jbox_ids.Count() < i + 1)
					RunEndpointInfo[0] = new RunEndpointInfo(-1, EndpointConnectionType.unconnected);
				else
					RunEndpointInfo[1] = new RunEndpointInfo(jbox_ids[i], EndpointConnectionType.jbox);
			}

			// Fill out conduit properties
			From = GetFrom(info);
			To = GetTo(info);
			Diameter = GetDiameter(info);
			Length = GetTotalLength(info);
			FittingBends = GetTotalBends(info);
			ConduitMaterialType = GetConduitMaterialType(info);

		}

		private void AssimilateCRI(ModelInfo info, ConduitRunInfo cri)
		{
			ConduitIds = ConduitIds.Union(cri.ConduitIds).ToList();
			FittingIds = FittingIds.Union(cri.FittingIds).ToList();
			RunEndpointInfo = cri.RunEndpointInfo;
			Length = GetTotalLength(info);
			Diameter = cri.Diameter;
			From = cri.From;
			To = cri.To;
			FittingBends = cri.FittingBends;
			ConduitMaterialType = cri.ConduitMaterialType;
		}

		public static void ProcessCRIFromConduitId(
			ModelInfo info, IEnumerable<ElementId> ids,
			List<ConduitRunInfo> add_cri_list)
		{
			List<RunNetwork> nets = new List<RunNetwork>();
			foreach(var id in ids)
			{
				var rn = new RunNetwork(info.DOC.GetElement(id));
				if(!nets.Any(net => net.RunIds.Concat(net.FittingIds).Contains(rn.StartId)))
					nets.Add(rn);
			}

			Stack<RunNetwork> fitting_nets = new Stack<RunNetwork>(nets);
			nets.Clear();
			while(fitting_nets.Any())
			{
				var rn = fitting_nets.Pop();
				var f = rn.FittingIds;
				f.ForEach(id => {
					if(fitting_nets.Any(net => net.FittingIds.Any(rnid => rnid == id)))
						rn.FittingIds.Remove(id);
				});
				nets.Add(rn);
			}

			var cris = new List<ConduitRunInfo>();
			foreach(var rn in nets)
			{
				cris.Add(new ConduitRunInfo(info, rn, rn.ConnectedJboxIds.ToArray()));
			}

			add_cri_list.AddRange(cris);
		}

		/// <summary>
		/// get the From parameter for this conduit run
		/// </summary>
		private string GetFrom(ModelInfo info)
		{
			string ret_from = "";
			foreach (var id in ConduitIds)
			{
				Element conduit = info.DOC.GetElement(new ElementId(id));
				string from = conduit.LookupParameter("From").AsString();

				// prime the value
				if(id == ConduitIds.First())
					ret_from = from;

				if(String.IsNullOrWhiteSpace(from) || ret_from != from)
				{
					ret_from = "UNSET";
					break;
				}
				else
					ret_from = from;
			}
			return ret_from;
		}

		/// <summary>
		/// get the To parameter for this conduit run
		/// </summary>
		private string GetTo(ModelInfo info)
		{
			string ret_to = "UNSET";
			foreach (var id in ConduitIds)
			{
				Element conduit = info.DOC.GetElement(new ElementId(id));
				string to = conduit.LookupParameter("To").AsString();

				// prime the value
				if(id == ConduitIds.First())
					ret_to = to;

				if(String.IsNullOrWhiteSpace(to) || ret_to != to)
				{
					ret_to = "UNSET";
					break;
				}
				else
					ret_to = to;
			}
			return ret_to;
		}

		private string GetConduitMaterialType(ModelInfo info)
		{
			var id = ConduitIds.First();
			var el = info.DOC.GetElement(new ElementId(id));
			return el.Name;
		}

		/// <summary>
		/// get the Diameter parameter for a conduit stick
		/// </summary>
		private double GetDiameter(ModelInfo info)
		{
			Element conduit = info.DOC.GetElement(new ElementId(ConduitIds.First()));
			var p = conduit.LookupParameter("Diameter(Trade Size)");
			if(p == null || !p.HasValue) p = conduit.LookupParameter("Diameter");
			return p.AsDouble();
		}

		/// <summary>
		/// get the total length for this conduit run
		/// </summary>
		private double GetTotalLength(ModelInfo info)
		{
			double ret_dbl = 0;
			foreach (var id in ConduitIds)
			{
				Element conduit = info.DOC.GetElement(new ElementId(id));

				//get total length of run
				if (conduit.LookupParameter("Length") != null &&
					conduit.LookupParameter("Length").HasValue)
				{
					ret_dbl += conduit.LookupParameter("Length").AsDouble();
				}
				else if (conduit.LookupParameter("Conduit Length") != null)
				{
					string potential_angle = Regex.Match(conduit.LookupParameter("Angle").AsValueString(), @"\d\d").Value;
					if(String.IsNullOrWhiteSpace(potential_angle))
						potential_angle = Regex.Match(conduit.LookupParameter("Angle").AsValueString(), @"\d").Value;

					double centralAngle = double.Parse(potential_angle);
					double bendRad = conduit.LookupParameter("Bend Radius").AsDouble();
					double fittingLength = ((2 * Math.PI * bendRad) * (centralAngle / 360)) + (conduit.LookupParameter("Conduit Length").AsDouble() * 2);
					ret_dbl += fittingLength;
				}
			}
			return ret_dbl;
		}

		/// <summary>
		/// get the total degree bends of all the conduit fittings in this run
		/// </summary>
		private double GetTotalBends(ModelInfo info)
		{
			double ret_dbl = 0;
			foreach (var id in ConduitIds)
			{
				Element conduit = info.DOC.GetElement(new ElementId(id));
				if (conduit.LookupParameter("Angle") != null)
				{
					ret_dbl += conduit.LookupParameter("Angle").AsDouble();
				}
			}
			return ret_dbl;
		}

		/// <summary>
		/// Get the wire size parameter in revit for the conduit run
		/// </summary>
		public string GetRevitWireSizeString(ModelInfo info)
		{
			var ids = ConduitIds;
			List<string> param_outputs = new List<string>();
			foreach(var id in ids)
			{
				var el = info.DOC.GetElement(new ElementId(id));
				if(el == null || el.LookupParameter("Wire Size") == null || String.IsNullOrWhiteSpace(el.LookupParameter("Wire Size").AsString())) continue;

				param_outputs.Add(el.LookupParameter("Wire Size").AsString());
			}

			return param_outputs.Any() ? param_outputs.First() : "";
		}

		/// <summary>
		/// ToString override
		/// </summary>
		public string ToString(ModelInfo info)
		{
			return string.Format(
				"Type: {0}\nFrom: {1}\nTo: {2}\nDiameter: {3}\n Length: {4}\nSegments: {5}",
				ConduitMaterialType, From, To, DiameterStr(info),
				LengthStr(info), ConduitIds.Count().ToString());
		}
	}

	/// <summary>
	/// Expresses Info about the object on the end of a conduit run
	/// </summary>
	[DataContract]
	public class RunEndpointInfo
	{
		[DataMember]
		public int ElementId { get; private set; }
		[DataMember]
		public EndpointConnectionType ConnectionType { get; private set; }

		public RunEndpointInfo(
			int end_element, EndpointConnectionType type_of_connection)
		{
			ElementId = end_element;
			ConnectionType = type_of_connection;
		}
	}

	/// <summary>
	/// Types of posible connections on the end of a conduit run
	/// </summary>
	public enum EndpointConnectionType
	{
		jbox = 1,
		unconnected = 0,
		other = -1
	}

	/////////////////////
	/// Run Network /////
	/////////////////////

	public class RunNetwork
	{
		public List<int> RunIds {  get; private set; }
		public List<int> FittingIds {  get; private set; }
		public List<int> ConnectedJboxIds {  get; private set; }
		public int StartId {  get; private set; }

		public RunNetwork(Element first_conduit)
		{
			RunIds = new List<int>();
			FittingIds = new List<int>();
			ConnectedJboxIds = new List<int>();

			RunIds.Add(first_conduit.Id.IntegerValue);
			StartId = first_conduit.Id.IntegerValue;

			Connector[] end_connectors = RvtUtil.GetNonSetConnectors(first_conduit, RvtUtil.GetConnectors);
			Connector next_connector = end_connectors[0];
			string prev_type = first_conduit.Name;

			void setPrevType(Element el)
			{
				if(el.Category.Name.Equals("Conduits"))
					prev_type = el.Name;
			}

			bool continue_processing = true;
			bool flopped_sides = false;

			while(continue_processing)
			{
				ConduitConnectorPack ccp = new ConduitConnectorPack(next_connector, prev_type);

				void pushCat(Element el)
				{
					if(el.Category.Name.Equals("Conduits"))
					{
						RunIds.Add(el.Id.IntegerValue);
					}
					else if(el.Category.Name.Equals("Conduit Fittings"))
					{
						FittingIds.Add(el.Id.IntegerValue);
					}
				}

				string log_str = "";
				switch(ccp.Result)
				{
					case NetworkResult.proceed:
						log_str = "proceed";
						pushCat(ccp.Conduit);
						setPrevType(ccp.Conduit);
						next_connector = ccp.NextConnector;
						break;
					case NetworkResult.stop_end_of_conduit:
						log_str = "stop! end of conduit";
						if(!flopped_sides)
						{
							flopped_sides = true;
							next_connector = end_connectors[1];
						}
						else
							continue_processing = false;
						break;
					case NetworkResult.stop_jbox:
						log_str = "stop! junction box";
						ConnectedJboxIds.Add(ccp.Conduit.Id.IntegerValue);
						if(!flopped_sides)
						{
							flopped_sides = true;
							next_connector = end_connectors[1];
						}
						else
							continue_processing = false;
						break;
					case NetworkResult.error:
						log_str = "error";
						continue_processing = false;
						break;
					default:
						throw new Exception("run network hit default value.");
				}

				//DEBUG
				bool debug = false; if(debug) debugger.show(err:log_str);
			}
		}

		private enum NetworkResult
		{
			proceed = 2,
			stop_end_of_conduit = 1,
			stop_jbox = 0,
			error = -1
		}

		private class ConduitConnectorPack
		{
			public Element Conduit { get; private set; }
			public Connector NextConnector { get; private set; }
			public NetworkResult Result { get; private set; }

			private static double ConnectorTolerance = .00001;

			public ConduitConnectorPack(Connector c, string prev_type)
			{
				static bool chk_category(string x) => x != "Conduits" && x != "Conduit Fittings";

				NextConnector = null;

				if(!c.IsConnected)
				{
					Result = NetworkResult.stop_end_of_conduit;
				}
				else
				{
					Connector first_jump = null;
					Connector second_jump = null;

					foreach(Connector c2 in c.AllRefs)
					{
						if(!c2.Origin.IsAlmostEqualTo(c.Origin, ConnectorTolerance)) continue;
						first_jump = c2;
					}

					if(first_jump == null)
					{
						Result = NetworkResult.stop_end_of_conduit;
						return;
					}

					Conduit = first_jump.Owner;
					if(chk_category(Conduit.Category.Name))
					{
						Result = NetworkResult.stop_jbox;
						return;
					}

					if(Conduit.Category.Name.Equals("Conduits"))
					{
						if(!Conduit.Name.Equals(prev_type))
						{
							Result = NetworkResult.stop_end_of_conduit;
							NextConnector = first_jump;
							return;
						}
					}


					foreach(Connector c3 in RvtUtil.GetConnectors(first_jump.Owner))
					{
						if(c3.Origin.IsAlmostEqualTo(first_jump.Origin, ConnectorTolerance)) continue;
						second_jump = c3;
					}

					if(second_jump == null)
					{
						Result = NetworkResult.error;
						return;
					}

					NextConnector = second_jump;
					Result = NetworkResult.proceed;
					return;
				}
			}
		}
	}
}