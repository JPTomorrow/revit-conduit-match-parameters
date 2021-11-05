using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;
using JPMorrow.Tools.Diagnostics;
using JPMorrow.Revit.RvtMiscUtil;
using JPMorrow.Revit.Text;
using JPMorrow.Revit.Measurements;

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
        public string ReportedWireSizes { get; private set; }
        [DataMember]

        public int GeneratingConduitId { get; private set; }
        public int[] WireIds { get => ConduitIds.ToArray(); }
        public static int ElecRoomTakeoffLength { get => 10; }

        public void OverrideToStr(string val) => To = val;
        public void OverrideMaterialType(string type) => ConduitMaterialType = type;

        // string conversions
        public string DiameterStr(Document doc) => RMeasure.LengthFromDbl(doc, Diameter);
        public string LengthStr(Document doc) => RMeasure.LengthFromDbl(doc, Length);

        public string GetSets(ModelInfo info)
        {

            Element el(int x) => info.DOC.GetElement(new ElementId(x));
            Parameter p(int x) => el(x).LookupParameter("Set(s)");
            bool is_empty(int x) => string.IsNullOrWhiteSpace(p(x).AsString());
            bool is_null(int x) => p(x) == null;

            foreach (var id in ConduitIds)
            {
                if (!is_null(id) && !is_empty(id))
                {
                    return p(id).AsString();
                }
            }

            return "N/A";
        }

        // Different types of possible conduit material
        public static string[] ConduitMaterialTypes { get; } = new string[]{
            "EMT", "PVC", "RNC", "FLEX", "IMC", "RMC"
        };

        public static string[] ConduitMaterialTypeFullNames { get; } = new string[] {
            "Rigid Nonmetallic Conduit (RNC Sch 40)",
            "Electrical Metallic Tubing (EMT)",
        };

        public override string ToString()
        {
            return string.Format(
                "[ From: {0}, To: {1} Reported Wire Size: {2} Material: {3} ]",
                From, To, ReportedWireSizes, ConduitMaterialType);
        }

        public string ToString(ModelInfo info)
        {
            return string.Format(
                "Type: {0}\nFrom: {1}\nTo: {2}\nDiameter: {3}\n Length: {4}\nSegments: {5}",
                ConduitMaterialType, From, To, DiameterStr(info.DOC),
                LengthStr(info.DOC), ConduitIds.Count().ToString());
        }

        public bool Equals(ConduitRunInfo other)
        {
            return From.Equals(other.From) && To.Equals(other.To);
        }

        public bool DeepEquals(ConduitRunInfo other)
        {
            return (From.Equals(other.From)) &&
                    (To.Equals(other.To)) &&
                    (ReportedWireSizes.Equals(other.ReportedWireSizes)) &&
                    (ConduitMaterialType.Equals(other.ConduitMaterialType)) &&
                    (Diameter == other.Diameter);
        }

        /// <summary>
        /// combine all provided like runs into one entry
        /// </summary>
        public static ConduitRunInfo CombineRuns(
            ModelInfo info, ConduitRunInfo cri,
            IEnumerable<ConduitRunInfo> others,
            out List<ConduitRunInfo> old_cris)
        {
            var o = others.ToList();
            o.Remove(cri);
            var matching_cris = o.Where(x => x.DeepEquals(cri)).ToList();

            if (!matching_cris.Any())
            {
                old_cris = null;
                return null;
            }

            old_cris = matching_cris;
            return new ConduitRunInfo(info, cri, matching_cris);
        }

        // copy constructor for combining runs
        private ConduitRunInfo(ModelInfo info, ConduitRunInfo cri, IEnumerable<ConduitRunInfo> others)
        {
            GeneratingConduitId = cri.GeneratingConduitId;
            ConduitIds = cri.ConduitIds;
            FittingIds = cri.FittingIds.Concat(others.ToList().SelectMany(x => x.FittingIds)).ToList();
            RunEndpointInfo = cri.RunEndpointInfo;
            From = cri.From;
            To = cri.To;
            Diameter = cri.Diameter;

            double len = cri.Length;
            foreach (var o in others)
                len += o.Length;

            Length = len;
            FittingBends = GetTotalBends(info);
            ConduitMaterialType = cri.ConduitMaterialType;
            ReportedWireSizes = cri.ReportedWireSizes;
        }

        // normal constructor
        private ConduitRunInfo(ModelInfo info, RunNetwork rn, int[] jbox_ids)
        {
            GeneratingConduitId = rn.StartId;
            ConduitIds = new List<int>(rn.RunIds.OrderBy(x => x).ToList());
            FittingIds = new List<int>(rn.FittingIds.OrderBy(x => x).ToList());
            RunEndpointInfo = new RunEndpointInfo[2];

            // get endpoint info
            for (var i = 0; i < 2; i++)
            {
                if (jbox_ids.Count() < i + 1)
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
            ReportedWireSizes = GetReportedWireSizes(info);

        }

        public static void ProcessCRIFromConduitId(
            ModelInfo info, IEnumerable<ElementId> conduit_ids, List<ConduitRunInfo> add_cri_list)
        {

            var ids = new List<ElementId>(conduit_ids);
            ids = ids.OrderBy(x => x.IntegerValue).ToList();

            List<RunNetwork> nets = new List<RunNetwork>();

            while (ids.Any())
            {
                var id = ids.First();
                var rn = new RunNetwork(info, info.DOC.GetElement(id));

                // remove ids from list of possible ids
                var rem_ids = ids.Where(x => rn.AllIds.Any(y => y == x.IntegerValue));
                rem_ids.ToList().ForEach(x => ids.Remove(x));
                nets.Add(rn);
            }

            Stack<RunNetwork> fitting_nets = new Stack<RunNetwork>(nets);
            nets.Clear();

            while (fitting_nets.Any())
            {
                var rn = fitting_nets.Pop();
                var f = rn.FittingIds;

                f.ForEach(id =>
                {
                    if (fitting_nets.Any(net => net.FittingIds.Any(rnid => rnid == id)))
                        rn.FittingIds.Remove(id);
                });
                nets.Add(rn);
            }

            var cris = new List<ConduitRunInfo>();
            foreach (var rn in nets)
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
            if (!ConduitIds.Any()) return "UNSET";

            Element el(int id) => info.DOC.GetElement(new ElementId(id));
            bool has_from(int id) => el(id) != null && el(id).LookupParameter("From") != null;
            string from(int id) => el(id).LookupParameter("From").AsString();
            bool has_val(int id) => !string.IsNullOrWhiteSpace(from(id));

            if (!ConduitIds.Any(x => has_from(x))) return "NO FROM PARAMETER LOADED";

            var id = ConduitIds.FirstOrDefault(x => has_from(x) && has_val(x));
            if (id <= 0) return "UNSET";
            return !ConduitIds.All(x => has_from(x) && has_val(x) && from(x).Equals(from(id))) ? "UNSET" : from(id);
        }

        /// <summary>
        /// get the To parameter for this conduit run
        /// </summary>
        private string GetTo(ModelInfo info)
        {
            if (!ConduitIds.Any()) return "UNSET";

            Element el(int id) => info.DOC.GetElement(new ElementId(id));
            bool has_to(int id) => el(id) != null && el(id).LookupParameter("To") != null;
            string to(int id) => el(id).LookupParameter("To").AsString();
            bool has_val(int id) => !string.IsNullOrWhiteSpace(to(id));

            if (!ConduitIds.Any(x => has_to(x))) return "NO TO PARAMETER LOADED";

            var id = ConduitIds.FirstOrDefault(x => has_to(x) && has_val(x));
            if (id <= 0) return "UNSET";
            return !ConduitIds.All(x => has_to(x) && has_val(x) && to(x).Equals(to(id))) ? "UNSET" : to(id);
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
            if (p == null || !p.HasValue) p = conduit.LookupParameter("Diameter");
            return p.AsDouble();
        }

        private string GetReportedWireSizes(ModelInfo info)
        {
            List<string> wire_sizes = new List<string>();

            foreach (var id in ConduitIds)
            {
                var c = info.DOC.GetElement(new ElementId(id));
                var p = c.LookupParameter("Wire Size");
                if (p == null || !p.HasValue) continue;
                wire_sizes.Add(p.AsString());
            }

            if (wire_sizes.Any() &&
                wire_sizes.All(x => x.Equals(wire_sizes.First())) &&
                !wire_sizes.All(x => x.Equals("")))
                return wire_sizes.First();

            return "---";
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
                    if (String.IsNullOrWhiteSpace(potential_angle))
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
            foreach (var id in ids)
            {
                var el = info.DOC.GetElement(new ElementId(id));
                if (el == null || el.LookupParameter("Wire Size") == null || String.IsNullOrWhiteSpace(el.LookupParameter("Wire Size").AsString())) continue;

                param_outputs.Add(el.LookupParameter("Wire Size").AsString());
            }

            return param_outputs.Any() ? param_outputs.First() : "";
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
        public List<int> RunIds { get; private set; }
        public List<int> FittingIds { get; private set; }
        public List<int> ConnectedJboxIds { get; private set; }
        public int StartId { get; private set; }

        public List<int> AllIds { get => RunIds.Concat(FittingIds).ToList(); }

        public override string ToString()
        {
            return string.Join("\n", RunIds.Concat(FittingIds).Select(x => x.ToString()));
        }

        public RunNetwork(ModelInfo info, Element first_conduit)
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
                if (el.Category.Name.Equals("Conduits"))
                    prev_type = el.Name;
            }

            bool continue_processing = true;
            bool flopped_sides = false;

            string log_str = "";
            void add_to_log(string txt) => log_str += txt + "\n";

            while (continue_processing)
            {

                ConduitConnectorPack ccp = new ConduitConnectorPack(info, next_connector, prev_type);

                void pushCat(Element el)
                {
                    if (el.Category.Name.Equals("Conduits"))
                    {
                        RunIds.Add(el.Id.IntegerValue);
                    }
                    else if (el.Category.Name.Equals("Conduit Fittings"))
                    {
                        FittingIds.Add(el.Id.IntegerValue);
                    }
                }

                switch (ccp.Result)
                {

                    case NetworkResult.proceed:
                        add_to_log("proceed");
                        pushCat(ccp.Conduit);
                        setPrevType(ccp.Conduit);
                        next_connector = ccp.NextConnector;
                        break;

                    case NetworkResult.stop_end_of_conduit:
                        add_to_log("stop! end of conduit");

                        if (!flopped_sides && end_connectors[1] != null)
                        {
                            add_to_log("FLIPPING SIDES!!!!!!!!");
                            flopped_sides = true;
                            next_connector = end_connectors[1];
                        }
                        else
                            continue_processing = false;
                        break;

                    case NetworkResult.stop_jbox:
                        add_to_log("stop! junction box");
                        ConnectedJboxIds.Add(ccp.Conduit.Id.IntegerValue);

                        if (!flopped_sides)
                        {
                            flopped_sides = true;
                            next_connector = end_connectors[1];
                        }
                        else
                            continue_processing = false;
                        break;

                    case NetworkResult.error:
                        add_to_log("error");
                        continue_processing = false;
                        break;

                    default:
                        add_to_log("run network hit default value.");
                        continue_processing = false;
                        break;
                }
            }

            bool debug = false; if (debug) debugger.debug_show(err: log_str);
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

            public ConduitConnectorPack(ModelInfo info, Connector c, string prev_type)
            {
                static bool not_conduit_or_fitting(string x) => x != "Conduits" && x != "Conduit Fittings";

                NextConnector = null;

                if (!c.IsConnected)
                { // conduit not connected to anything
                    Result = NetworkResult.stop_end_of_conduit;
                }
                else
                {
                    var all_refs = GetConnectorListFromSet(c.AllRefs).ToList();
                    all_refs.Remove(c);

                    // stop if no connector matches
                    if (!all_refs.Any(x => IsConnectedTo(info, c, x)))
                    {
                        Result = NetworkResult.stop_end_of_conduit;
                        return;
                    }

                    Connector first_jump = null;

                    first_jump = all_refs.Where(x => IsConnectedTo(info, c, x)).First();

                    Conduit = first_jump.Owner;
                    if (not_conduit_or_fitting(Conduit.Category.Name))
                    {
                        Result = NetworkResult.stop_jbox;
                        return;
                    }

                    // stop if next conduit is of a different material type
                    if (Conduit.Category.Name.Equals("Conduits") && !Conduit.Name.Equals(prev_type))
                    {
                        Result = NetworkResult.stop_end_of_conduit;
                        NextConnector = first_jump;
                        return;
                    }

                    // start of second jump
                    Connector second_jump = null;

                    all_refs = GetConnectorListFromSet(RvtUtil.GetConnectors(first_jump.Owner)).ToList();
                    all_refs.Remove(first_jump);

                    // stop if no connector matches
                    if (!all_refs.Any(x => !IsConnectedTo(info, c, x)))
                    {
                        Result = NetworkResult.error;
                        return;
                    }


                    second_jump = all_refs.Where(x => !IsConnectedTo(info, c, x)).First();

                    NextConnector = second_jump;
                    Result = NetworkResult.proceed;
                }
            }

            private IEnumerable<Connector> GetConnectorListFromSet(ConnectorSet c)
            {

                List<Connector> cc = new List<Connector>();
                var it = c.ForwardIterator();

                while (it.MoveNext())
                {
                    cc.Add(it.Current as Connector);
                }

                return cc;
            }

            private bool IsConnectedTo(ModelInfo info, Connector c, Connector c2)
            {

                if (c.ConnectorType == ConnectorType.Logical || c2.ConnectorType == ConnectorType.Logical) return false;
                bool s = false;

                try
                {
                    s = c.Origin.IsAlmostEqualTo(c2.Origin, ConnectorTolerance);
                }
                catch
                {
                    info.SEL.SetElementIds(new[] { c.Owner.Id, c2.Owner.Id });

                    var c1type = Enum.GetName(typeof(ConnectorType), c.ConnectorType);

                    var c2type = Enum.GetName(typeof(ConnectorType), c2.ConnectorType);
                    throw new Exception("Connector 1 Type: " + c1type + " | Connector 2 Type" + c2type);
                }
                return s;
            }
        }
    }
}