
using System.Collections.Generic;
using JPMorrow.Revit.Documents;
using JPMorrow.Revit.Measurements;
using MoreLinq;
using System.Linq;
using System.Runtime.Serialization;

namespace JPMorrow.Revit.VoltageDrop {

    [DataContract]
    public class VoltageDropRule {

        private VoltageDropRule(
            double min_dist, double max_dist,
            string wire_size, string voltage) {

            MinDistance = min_dist;
            MaxDistance = max_dist;
            WireSize = wire_size;
            Voltage = voltage;
        }

        [DataMember]
        public double MinDistance { get; private set; }
        [DataMember]
        public double MaxDistance { get; private set; }
        [DataMember]
        public string WireSize { get; private set; }
        [DataMember]
        public string Voltage { get; private set; }

        public bool IsInRange(double d) => d > MinDistance && d < MaxDistance;
        
        // get a human readable text representation of a voltage drop rule
        public string ToString(ModelInfo info) {
            var mind = RMeasure.LengthFromDbl(info, MinDistance);
            var maxd = RMeasure.LengthFromDbl(info, MaxDistance);
            return string.Format("{0} < {1} < {2}", mind, WireSize, maxd);
        }

        // create a voltage drop rule given the user input
        public static VoltageDropRule DeclareRule(
            double min_distance, double max_distance,
            string wire_size, string voltage) {

            return new VoltageDropRule(min_distance, max_distance, wire_size, voltage);
        }

      
    }

    public static class VoltageDropEXT {

        public static IEnumerable<VoltageDropRule> OrderByPrecedence(this List<VoltageDropRule> source) {

            return source.OrderBy(x => x.MinDistance).ToList();
        }

        public static bool GetRuleByVoltageAndLength(
            this List<VoltageDropRule> source,
            string panel_voltage, double length,
            out VoltageDropRule base_rule) {

            var idx = source.FindIndex(x => x.Voltage.Equals(panel_voltage) && x.IsInRange(length));

            if(idx == -1) {
                base_rule = null;
                return false;
            }
            else {
                base_rule = source[idx];
                return true;
            }
        }
    }
}
