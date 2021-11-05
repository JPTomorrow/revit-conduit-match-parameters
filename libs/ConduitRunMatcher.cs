using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using JPMorrow.Tools.Revit.MEP.Selection;
using JPMorrow.Revit.ConduitRuns;
using JPMorrow.Tools.Diagnostics;
using JPMorrow.Revit.Documents;

namespace JPMorrow.Revit.Conduit
{
    public class ConduitPropogationPair
    {
        public Element MatchConduit { get; set; }
        public RunNetwork Network { get; set; }
        public ConduitPropogationPair(Element conduit, RunNetwork network)
        {
            MatchConduit = conduit;
            Network = network;
        }
    }

    public static class ConduitRunMatcher
    {
        public static Result MatchRuns(ModelInfo info)
        {
            // get selected conduit
            var conduit_selection = info.SEL.GetElementIds().Select(x => info.DOC.GetElement(x)).ToList();
            conduit_selection.RemoveAll(x => !x.Category.Name.Equals("Conduits"));

            if (conduit_selection.Count == 0)
                return Result.Failed;

            // get run networks
            var networks = conduit_selection.Select(x => new ConduitPropogationPair(x, new RunNetwork(info, x))).ToList();

            // prune networks where the match conduit is contained in two separate networks
            foreach (var n in networks.ToArray())
            {
                var from = n.MatchConduit.LookupParameter("From");
                var to = n.MatchConduit.LookupParameter("To");
                bool is_valid(Parameter p) => p != null && !string.IsNullOrWhiteSpace(p.AsString());

                if (!is_valid(from) || !is_valid(to))
                {
                    networks.Remove(n);
                    continue;
                }

                var mid = n.MatchConduit.Id;
                if (networks.Any(x =>
                    x.MatchConduit.Id.IntegerValue != mid.IntegerValue &&
                    x.Network.AllIds.Contains(mid.IntegerValue)))
                {
                    networks.Remove(n);
                }
            }

            // process the matches
            ProcessMatchTransaction(info, networks);
            return Result.Succeeded;
        }

        private static void ProcessMatchTransaction(
            ModelInfo revit_info, IEnumerable<ConduitPropogationPair> networks)
        {
            //local functions
            Parameter p(Element x, string str) => x.LookupParameter(str);

            using (TransactionGroup tgx = new TransactionGroup(revit_info.DOC, "Propogating parameters"))
            {
                tgx.Start();

                using (Transaction tx = new Transaction(revit_info.DOC, "clear run id"))
                {
                    tx.Start();
                    foreach (var n in networks)
                    {
                        foreach (var id in n.Network.RunIds.Concat(n.Network.FittingIds))
                        {
                            Element el = revit_info.DOC.GetElement(new ElementId(id));
                            p(el, "From").Set(p(n.MatchConduit, "From").AsString());
                            p(el, "To").Set(p(n.MatchConduit, "To").AsString());
                            p(el, "Wire Size").Set(p(n.MatchConduit, "Wire Size").AsString());
                            p(el, "Comments").Set(p(n.MatchConduit, "Comments").AsString());
                            p(el, "Set(s)").Set(p(n.MatchConduit, "Set(s)").AsString());
                        }
                    }
                    tx.Commit();
                }
                tgx.Assimilate();
            }
        }
    }
}