using System.Runtime.Serialization;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JPMorrow.Tools.Diagnostics;
using JPMorrow.Revit.VoltageDrop;

namespace JPMorrow.Revit.Wires
{
    /// <summary>
    /// Class to represent voltage drop in revit
    /// </summary>
    public static class VDrop
	{
		private static Dictionary<double, int> CMillsToVDropIdx { get; } = new Dictionary<double, int>() {
			{ 0.0, 0 },

			{ 1620.0, 0 },
			{ 2580.0, 1 },
			{ 4110.0, 2 },
			{ 6530.0, 3 },
			{ 10380.0, 4 },
			{ 16510.0, 5 },
			{ 26240.0, 6 },
			{ 41740.0, 7 },
			{ 52620.0, 8 },
			{ 66360.0, 9 },
			{ 83690.0, 10 },
			{ 105600.0, 11 },
			{ 133100.0, 12 },
			{ 167800.0, 13 },
			{ 211600.0, 14 },
			{ 1000000000.0, 14 },
		};

		public static Dictionary<int, string> VDropIdxToWireSize { get; } = new Dictionary<int, string>() {
			{ 0, "#18" 	},
			{ 1, "#16" 	},
			{ 2, "#14" 	},
			{ 3, "#12" 	},
			{ 4, "#10" 	},
			{ 5, "#8" 	},
			{ 6, "#6" 	},
			{ 7, "#4" 	},
			{ 8, "#3" 	},
			{ 9, "#2" 	},
			{ 10, "#1" 	},
			{ 11, "#1/0" },
			{ 12, "#2/0" },
			{ 13, "#3/0" },
			{ 14, "#4/0" },
		};

		/// <summary>
		/// Effective voltage drop percentage
		/// allowed for a given panel voltage
		/// </summary>
		private static Dictionary<double, double> EDPermitted { get; } = new Dictionary<double, double>() {
			{ 120, 3.6 },
			{ 208, 6.24 },
			{ 277, 8.31 },
			{ 480, 14.4 },
		};

		/// <summary>
		/// mapping of wire sizes to resistances for deriving accurate copper k value
		/// </summary>
		private static Dictionary<string, double> CopperRValueTable = new Dictionary<string, double>()
		{
			{ "#18" , 7.77 },
			{ "#16" , 4.89 },
			{ "#14" , 3.07 },
			{ "#12" , 1.93 },
			{ "#10" , 1.21 },
			{ "#8" 	, 0.764 },
			{ "#6" 	, 0.491 },
			{ "#4" 	, 0.308 },
			{ "#3" 	, 0.245 },
			{ "#2" 	, 0.194 },
			{ "#1" 	, 0.154 },
			{ "#1/0", 0.122 },
			{ "#2/0", 0.0967 },
			{ "#3/0", 0.0766 },
			{ "#4/0", 0.0608 }
		};

		/// <summary>
		/// mapping of wire sizes to resistances for deriving accurate aluminium k value
		/// </summary>
		private static Dictionary<string, double> AluminiumRValueTable = new Dictionary<string, double>()
		{
			{ "#18" , 12.8 },
			{ "#16" , 8.05 },
			{ "#14" , 5.06 },
			{ "#12" , 3.18 },
			{ "#10" , 2.00 },
			{ "#8" 	, 1.26 },
			{ "#6" 	, 0.808 },
			{ "#4" 	, 0.508 },
			{ "#3" 	, 0.403 },
			{ "#2" 	, 0.319 },
			{ "#1" 	, 0.253 },
			{ "#1/0", 0.201 },
			{ "#2/0", 0.159 },
			{ "#3/0", 0.126 },
			{ "#4/0", 0.100 }
		};

		/// <summary>
		/// Calculate the voltage drop given some wire and
		/// panel information available on the panel schedule
		/// </summary>
		public static string DropVoltage(
			int phase, double distance, double volt_amps,
			double voltage, bool copper = true, string size_cutoff = "#12")
		{
			string wire_size = "#12";

			var inverse_wire_size_dict = new Dictionary<string, int>();
			foreach(var kvp in VDropIdxToWireSize)
				inverse_wire_size_dict.Add(kvp.Value, kvp.Key);

			// k value of copper and aluminum
			var copper_k = 12.9;
			var alum_k = 21.2;

			// get Effective Voltage Drop permitted
			bool s = EDPermitted.TryGetValue(voltage, out double ed_permitted);
			s = inverse_wire_size_dict.TryGetValue(size_cutoff, out int cutoff_idx);
			if(!s) return wire_size;

			// get amps
			double amps = volt_amps / voltage;

			// get area circular millimeters
			double cmils = (phase == 1 ? 2.0 : Math.Sqrt(3.0)) * (copper ? copper_k : alum_k) * distance * amps / ed_permitted;

			var cmils_idx = CMillsToVDropIdx.Keys.ToList().BinarySearch(cmils);

			if(cmils_idx < 0)
				cmils_idx = ~cmils_idx - 1;

			int cmils_ws_idx = CMillsToVDropIdx.Values.ToList()[cmils_idx];
			double k_factor_cmils = CMillsToVDropIdx.Keys.ToList()[cmils_idx];
			if(cmils_ws_idx < cutoff_idx) return wire_size;

			string final_ws = VDropIdxToWireSize[cmils_ws_idx];
			int ws_crawler = 1;
			double resistance = -1;
			double accurate_k = -1;

			var vd_percent = 100.0;

			while(vd_percent > 3.0)
			{
				// preform more accurate second pass using derived cmils
				resistance = copper ? CopperRValueTable[final_ws] : AluminiumRValueTable[final_ws];
				accurate_k = resistance * k_factor_cmils / 1000.0;

				var vd = (phase == 1 ? 2.0 : Math.Sqrt(3.0)) * accurate_k * distance * amps / cmils;
				vd_percent = 100.0 - (((voltage - vd) / voltage) * 100.0);

				if(vd_percent > 3.0)
				{
					if(!VDropIdxToWireSize.ContainsKey(cmils_ws_idx + ws_crawler))
					{
						debugger.show(err:"The voltage drop on the wires maxed out at " + String.Format("{0:0.00}", vd_percent) + "%, returning a " + VDropIdxToWireSize.Last().Value + " wire size.\n\n This wire size will still not satisfy the required voltage drop.\n\n This is usually an indication that the parameters you entered are misconfigured.\n\n Consider removing this wire and running it again with a different set of parameters, such as reducing the VA load.", header:"Voltage Drop Failure");
						break;
					}
					final_ws = VDropIdxToWireSize[cmils_ws_idx + ws_crawler];
					ws_crawler += 1;
				}
			}

			/* DEBUG DELETE
			string o = "phase: " + phase + " | " +
				"copper/aluminium: " + (copper ? "copper" : "aluminium") + " | " +
				"VA Load: " + volt_amps + " | " +
				"Panel Voltage: " + voltage + " | " +
				"Amps: " + amps + " | " +
				"Length: " + distance + " | " +
				"K Factor: " + accurate_k + " | " +
				"CMils: " + cmils + " | " +
				"Returned Wire Size: " + final_ws + "\n";

			File.AppendAllText("C:\\Users\\Jmorrow\\Desktop\\Test.txt", o);
			*/

			return final_ws;
		}
	}


	public struct WireColor
	{
		public static string[] All_Colors { get; } = new string[] {
			White, Gray, Green, Green_Yellow_Stripe,
			White_Black_Stripe, White_Blue_Stripe, White_Red_Stripe,
			Gray_Brown_Stripe, Gray_Orange_Stripe, Gray_Yellow_Stripe,
			Red, Blue, Black, Brown, Orange, Yellow
		};

		public static string[] Nuetral_Colors { get; } = new string[] {
			White, Gray, White_Red_Stripe, White_Black_Stripe, White_Blue_Stripe,
			Gray_Brown_Stripe, Gray_Orange_Stripe, Gray_Yellow_Stripe
		};

		public static string[] Ground_Colors { get; } = new string[] {
			Green, Green_Yellow_Stripe
		};

        public static string[] Voltage120208Colors { get; } = new string[] {
			White, White_Red_Stripe, White_Black_Stripe, White_Blue_Stripe,
            Red, Black, Blue
		};
        
        public static string[] Voltage277480Colors { get; } = new string[] {
			Gray, Gray_Brown_Stripe, Gray_Orange_Stripe, Gray_Yellow_Stripe,
            Brown, Orange, Yellow
		};

		// 120/208V & 277/480V Wire
		public static string White => "White";
		public static string Gray => "Gray";
		public static string Green => "Green";
		public static string Green_Yellow_Stripe => "Green w/ Yellow Stripe";
		public static string White_Red_Stripe => "White w/ Red Stripe";
		public static string White_Blue_Stripe => "White w/ Blue Stripe";
		public static string White_Black_Stripe => "White w/ Black Stripe";
		public static string Gray_Orange_Stripe => "Gray w/ Orange Stripe";
		public static string Gray_Yellow_Stripe => "Gray w/ Yellow Stripe";
		public static string Gray_Brown_Stripe => "Gray w/ Brown Stripe";
		public static string Red => "Red";
		public static string Blue => "Blue";
		public static string Black => "Black";
		public static string Brown => "Brown";
		public static string Orange => "Orange";
		public static string Yellow => "Yellow";

		// Telecom Wire
		public static Dictionary<string, string> TelecomWireNameToColor = new Dictionary<string, string>() {
			{ Wire.TelecomWireNames[0], "White w/ Orange Stripe" },
			{ Wire.TelecomWireNames[1], "Gray" },
			{ Wire.TelecomWireNames[2], "White" },
			{ Wire.TelecomWireNames[3], "White w/ Red Stripe" },
			{ Wire.TelecomWireNames[4], "White" },
			{ Wire.TelecomWireNames[5], "White" },
			{ Wire.TelecomWireNames[6], "White" },
			{ Wire.TelecomWireNames[7], "Black" },
			{ Wire.TelecomWireNames[8], "Black" },
			{ Wire.TelecomWireNames[9], "Blue" },
			{ Wire.TelecomWireNames[10], "Orange" },
		};


		public static string Get_277V(int c, bool is_nuetral, bool staggered, bool boy)
		{
			var c_num = PrimeCircuitNumber(c);

			if((c_num == 1) && boy) c_num = 3;
			if((c_num == 3) && boy) c_num = 1;

			if(staggered)
			{
				if(c_num == 1 || c_num == 2)
					return is_nuetral ? Gray_Brown_Stripe : Brown;
				else if (c_num == 3 || c_num == 4)
					return is_nuetral ? Gray_Orange_Stripe : Orange;
				else if(c_num == 5 || c_num == 6 || c_num == 0)
					return is_nuetral ? Gray_Yellow_Stripe : Yellow;
			}
			else
			{
				if(c_num == 1 || c_num == 4)
					return is_nuetral ? Gray_Brown_Stripe : Brown;
				else if (c_num == 2 || c_num == 5)
					return is_nuetral ? Gray_Orange_Stripe : Orange;
				else if(c_num == 3 || c_num == 6 || c_num == 0)
					return is_nuetral ? Gray_Yellow_Stripe : Yellow;
			}
			return Brown;
		}

		public static string Get_120V(int c, bool is_nuetral, bool staggered)
		{
			var c_num = PrimeCircuitNumber(c);

			if(staggered)
			{
				if(c_num == 1 || c_num == 2)
					return is_nuetral ? White_Black_Stripe : Black;
				else if (c_num == 3 || c_num == 4)
					return is_nuetral ? White_Red_Stripe : Red;
				else if( c_num == 5 || c_num == 6 || c_num == 0)
					return is_nuetral ? White_Blue_Stripe : Blue;
			}
			else
			{
				if(c_num == 1 || c_num == 4)
					return is_nuetral ? White_Black_Stripe : Black;
				else if (c_num == 2 || c_num == 5)
					return is_nuetral ? White_Red_Stripe : Red;
				else if(c_num == 3 || c_num == 6 || c_num == 0)
					return is_nuetral ? White_Blue_Stripe : Blue;
			}

			return Black;
		}

		public static string GetTelecomWireColor(string wire_name) {
			bool s = TelecomWireNameToColor.TryGetValue(wire_name, out string wire_color);
			if(!s)
				throw new Exception("A valid wire name was not provided: " + wire_name);
			return wire_color;
		}

		public static int PrimeCircuitNumber(int c)
		{
			int cNum = c > 6 ? c % 6 : c;
			return cNum;
		}
	}

	// Wire for use universal use with conduit
	[DataContract]
	public class Wire
	{
		[DataMember]
		public string CircuitNumber { get; private set; }
		[DataMember]
		public string Size { get; private set; }
		[DataMember]
		public string Color { get; private set; }
		[DataMember]
		public WireType WireType { get; private set; }

        public override string ToString() {
            return string.Format("{0} - {1} - {2}", CircuitNumber, Size, Color);
        }

        public bool GetPanelVoltage(out string panel_voltage) {
            
            panel_voltage = null;
            
            if(WireType == WireType.Branch) {
                if(WireColor.Voltage120208Colors.Any(x => x.Equals(Color))) {
                    panel_voltage = PanelVoltages[0];
                    return true;
                }
                else if(WireColor.Voltage277480Colors.Any(x => x.Equals(Color))){
                    panel_voltage = PanelVoltages[1];
                    return true;
                }
                
            }
            return false;
        }

		// Wire Size Master List
		public static string[] WireSizes { get; } = new string[28] {
			"2000MCM", "1750MCM", "1500MCM", "1250MCM", "1000MCM", "900MCM",
			"800MCM", "750MCM", "700MCM", "600MCM", "500MCM", "400MCM", "350MCM",
			"300MCM", "250MCM", "#4/0", "#3/0", "#2/0", "#1/0", "#1", "#2", "#3",
			"#4", "#6", "#8", "#10", "#12", "#14"
		};

        public static bool IsWireSizeGreaterThan(string source_wire_size, string compare_wire_size) {

            var source_idx = WireSizes.ToList().FindIndex(x => x.Equals(source_wire_size));
            var compare_idx = WireSizes.ToList().FindIndex(x => x.Equals(compare_wire_size));

            if(source_idx == -1 || compare_idx == -1) return false;
            if(compare_idx < source_idx) return true;
            return false;
        }

		public static string[] TelecomWireNames { get; } = new string[] {
			"18-2 Shielded Stranded Plenum",
			"18-3 Shielded Stranded Plenum",
			"18-4 Shielded Stranded Plenum",
			"18-2 Stranded",
			"18-2 Stranded Plenum Cable (Non-Shield)",
			"16-2 Stranded Plenum Cable (Non-Shield)",
			"16-3 Stranded Plenum Cable (Non-Shield)",
			"14AWG THHN/MTW",
			"12AWG THHN/MTW",
			"CAT6",
			"#22 AWG - 3 Conductors, Stranded, Shielded Flex Plenum Rated PVC",
		};

		public static string[] PanelVoltages { get; } = new string[] {
			"120/208V", "277/480V"
		};

		public static string[] GranularPanelVoltages { get; } = new string[] {
			"120", "208", "277", "480"
		};

		public static string[] WireMaterialTypes { get; } = new string[] {
			"Copper", "Aluminium"
		};

		public static Dictionary<int, string> BreakerSizeToWireSize { get; } = new Dictionary<int, string>() {
			{ 15, "#14"},
			{ 20, "#12"},
			{ 30, "#10"},
			{ 50, "#8"},
			{ 65, "#6"},
			{ 85, "#4"},
			{ 100, "#3"},
			{ 115, "#2"},
			{ 130, "#1"},
			{ 150, "#1/0"},
			{ 175, "#2/0"},
			{ 200, "#3/0"},
			{ 230, "#4/0"},
			{ 255, "250MCM"},
			{ 285, "300MCM"},
			{ 310, "350MCM"},
			{ 335, "400MCM"},
			{ 380, "500MCM"},
			{ 420, "600MCM"},
			{ 460, "700MCM"},
			{ 475, "750MCM"},
			{ 490, "800MCM"},
			{ 520, "900MCM"},
			{ 545, "1000MCM"},
			{ 590, "1250MCM"},
			{ 625, "1500MCM"},
			{ 650, "1750MCM"},
			{ 665, "2000MCM"},
		};

        public static Dictionary<string, int> WireSizeToBreakerSize { get; } = new Dictionary<string, int>() {
			{ "#14", 15 },
			{ "#12", 20 },
			{ "#10", 30 },
			{ "#8", 50 },
			{ "#6", 65 },
			{ "#4", 85 },
			{ "#3", 100 },
			{ "#2", 115 },
			{ "#1", 130 },
			{ "#1/0", 150 },
			{ "#2/0", 175 },
			{ "#3/0", 200 },
			{ "#4/0", 230 },
			{ "250MCM", 255 },
			{ "300MCM", 285 },
			{ "350MCM", 310 },
			{ "400MCM", 335 },
			{ "500MCM", 380 },
			{ "600MCM", 420 },
			{ "700MCM", 460 },
			{ "750MCM", 475 },
			{ "800MCM", 490 },
			{ "900MCM", 520 },
			{ "1000MCM", 545 },
			{ "1250MCM", 590 },
			{ "1500MCM", 625 },
			{ "1750MCM", 650 },
			{ "2000MCM", 665 },
		};

        public static Dictionary<int, string> BreakerSizeToProportionalGroundWireSize { get; } = new Dictionary<int, string>() {
		    { 15, "#14"},
			{ 20, "#12"},
			{ 60, "#10"},
			{ 100, "#8"},
			{ 200, "#6"},
			{ 300, "#4"},
			{ 400, "#3"},
			{ 500, "#2"},
			{ 600, "#1"},
			{ 800, "#1/0"},
			{ 1000, "#2/0"},
			{ 1200, "#3/0"},
			{ 1600, "#4/0"},
			{ 2000, "250MCM"},
			{ 2500, "350MCM"},
			{ 3000, "400MCM"},
			{ 4000, "500MCM"},
			{ 5000, "700MCM"},
			{ 6000, "800MCM"},
		};

		public static bool GetWireSizeFromBreakerSize(string breaker_size_in, out string breaker_size_out)
		{
			breaker_size_out = "#12";
			bool s = int.TryParse(breaker_size_in, out int bs);
			if(!s) return false;

			var idx = BreakerSizeToWireSize.Select(x => x.Key).ToList().BinarySearch(bs);
			if(idx < 0) idx = ~idx - 1;

			breaker_size_out = BreakerSizeToWireSize.Select(x => x.Value).ToList()[idx];
			return true;
		}

		// Constructor
		public Wire(
			string circuit_num, string size,
			string color, WireType type = WireType.None)
		{
			Size = size;
			Color = color;
			CircuitNumber = circuit_num;
			WireType = type;
		}
	}

	[DataContract]
	public enum WireType
	{
		None = 0,
		Branch = 1,
		Distribution = 2,
		Fire_Alarm = 3,
		P3 = 4
	}

	/// <summary>
	/// A manager for conduit run wire in Revit
	/// </summary>
	[DataContract]
	public class WireManager
	{
		[DataMember]
		private List<HashedWire> MasterWireDict { get; set; } = null;
		public bool HasWire() => MasterWireDict.Any();
		public void Clear() => MasterWireDict.Clear();

		public bool CheckConduitWire(int[] conduit_ids) => GetWires(conduit_ids) != null && GetWires(conduit_ids).Any();

		public WireManager(List<HashedWire> wire_dict)
		{
			if(MasterWireDict == null)
			 	MasterWireDict = new List<HashedWire>();
			MasterWireDict.AddRange(wire_dict);
		}

		public override string ToString()
		{
			return "Wiremanager";
		}

		public void AddWire(int[] run_ids, Wire wire)
		{
			var hash = CombineHashCodes(run_ids);
			MasterWireDict.Add(new HashedWire(hash, wire));
		}

		public void AddWires(int[] run_ids, IEnumerable<Wire> wires)
		{
			var hash = CombineHashCodes(run_ids);

			foreach(var wire in wires)
				MasterWireDict.Add(new HashedWire(hash, wire));
		}

		public void RemoveWire(int[] run_ids, Wire wire, out int removed_cnt)
		{
			removed_cnt = 0;
			var hash = CombineHashCodes(run_ids);
			var wire_to_rem = MasterWireDict.Where(
				x => x.Hash.Equals(hash) &&
				x.Wire.CircuitNumber.Equals(wire.CircuitNumber) &&
				x.Wire.WireType.Equals(wire.WireType) &&
				x.Wire.Color.Equals(wire.Color) &&
				x.Wire.Size.Equals(wire.Size));

			if(!wire_to_rem.Any()) return ;
			wire_to_rem.ToList().ForEach(x => MasterWireDict.Remove(x));
			removed_cnt = wire_to_rem.Count();
		}

		public void RemoveWires(int[] run_ids)
		{
			var hash = CombineHashCodes(run_ids);
			var wire_to_rem = MasterWireDict.Where(
				x => x.Hash.Equals(hash));

			if(!wire_to_rem.Any()) return;
			wire_to_rem.ToList().ForEach(x => MasterWireDict.Remove(x));
		}

		public IEnumerable<Wire> GetWires(int[] single_run_ids)
		{
			var hash = CombineHashCodes(single_run_ids);
			if(MasterWireDict == null || !MasterWireDict.Any()) return new List<Wire>();
			return MasterWireDict.Where(x => x.Hash.Equals(hash)).Select(x => x.Wire).ToList();
		}

		public void StoreBranchWire(
			IEnumerable<int> single_run_ids, string panel_voltage,
			IEnumerable<int> raw_circuits, WireCreationData w_data)
		{

			var c_ids = single_run_ids.ToList();
			var circuits = raw_circuits.ToList();

			bool already_grounded = HasGrounding(single_run_ids.ToArray());

			if(panel_voltage.Equals("120/208V"))
				circuits.ForEach(c => GetWireForBranchCircuit120V(c, single_run_ids.ToArray(), w_data));
			else
				circuits.ForEach(c => GetWireForBranchCircuit277V(c, single_run_ids.ToArray(), w_data));

			if(already_grounded) {
				return;
			}

			if(w_data.IsolatedGround)
			{
				string g_color = WireColor.Green_Yellow_Stripe;
				var wire =  new Wire("Common", w_data.Ground, g_color, WireType.Branch);
				AddWire(single_run_ids.ToArray(), wire);
			}
			else
			{
				string g_color = WireColor.Green;
				var wire = new Wire("Common", w_data.Ground, g_color, WireType.Branch);
				AddWire(single_run_ids.ToArray(), wire);
			}
		}

		public void StoreDistWire(IEnumerable<int> single_run_ids, Wire wire)
		{
			AddWire(single_run_ids.ToArray(), wire);
		}

		private void GetWireForBranchCircuit120V(
			int c, int[] ids, WireCreationData w_data)
		{
			string h_color = WireColor.Get_120V(c, false, w_data.StaggeredCircuits);
			var wire = new Wire(c.ToString(), w_data.Hot, h_color, WireType.Branch);
			AddWire(ids, wire);

			int nuetral_cnt = NuetralCount(ids);
			if(nuetral_cnt > 0) return;

			if(!w_data.PhaseNuetral)
			{
				var nuet_wire = new Wire("Common", w_data.Nuetral, WireColor.White, WireType.Branch);
				AddWire(ids, nuet_wire);
			}
			else
			{
				var color = WireColor.Get_120V(c, true, w_data.StaggeredCircuits);
				var nuet_wire = new Wire(c.ToString(), w_data.Nuetral, color, WireType.Branch);
				AddWire(ids, nuet_wire);
			}
		}

		private void GetWireForBranchCircuit277V(
			int c, int[] ids, WireCreationData w_data)
		{
			string h_color = WireColor.Get_277V(c, false, w_data.StaggeredCircuits, w_data.BOY);
			var wire = new Wire(c.ToString(), w_data.Hot, h_color, WireType.Branch);
			AddWire(ids, wire);

			int nuetral_cnt = NuetralCount(ids);
			if(nuetral_cnt > 0) return;

			if(!w_data.PhaseNuetral)
			{
				var nuet_wire = new Wire("Common", w_data.Nuetral, WireColor.Gray, WireType.Branch);
				AddWire(ids, nuet_wire);
			}
			else
			{
				var color = WireColor.Get_277V(c, true, w_data.StaggeredCircuits, w_data.BOY);
				var nuet_wire = new Wire(c.ToString(), w_data.Nuetral, color, WireType.Branch);
				AddWire(ids, nuet_wire);
			}
		}

		private bool HasGrounding(int[] ids)
		{
			return GetWires(ids).Where(wire => WireColor.Ground_Colors.Any(c => wire.Color.Equals(c))).Any();
		}

		private int NuetralCount(int[] ids)
		{
			return GetWires(ids).Where(wire => wire.Color.Equals("Gray") || wire.Color.Equals("White")).Count();
		}

		//flips all of the wire in the pipe to either staggered or sequential
		public static WireManager FlipStaggered(
			WireManager man, int[] conduit_ids,
			bool staggered, bool boy)
		{
			WireManager ret_man = man;

			var wires = man.GetWires(conduit_ids);
			foreach(Wire wire in wires)
			{
				Wire new_wire;
				var c_num = int.Parse(wire.CircuitNumber);

				// 120V HOTS
				if(	wire.Color == WireColor.Black ||
					wire.Color == WireColor.Blue ||
					wire.Color == WireColor.Red )
				{
					new_wire = new Wire(wire.CircuitNumber, wire.Size, WireColor.Get_120V(c_num, false, staggered), wire.WireType);
					ret_man.AddWire(conduit_ids, new_wire);
					continue;
				}

				// 277V HOTS
				if(	wire.Color == WireColor.Brown ||
					wire.Color == WireColor.Orange ||
					wire.Color == WireColor.Yellow )
				{
					new_wire = new Wire(wire.CircuitNumber, wire.Size, WireColor.Get_277V(c_num, false, staggered, boy), wire.WireType);
					ret_man.AddWire(conduit_ids, new_wire);
					continue;
				}

				// 120V NUETRALS
				if(	wire.Color == WireColor.White_Black_Stripe ||
					wire.Color == WireColor.White_Blue_Stripe ||
					wire.Color == WireColor.White_Red_Stripe )
				{
					new_wire = new Wire(wire.CircuitNumber, wire.Size, WireColor.Get_120V(c_num, true, staggered), wire.WireType);
					ret_man.AddWire(conduit_ids, new_wire);
					continue;
				}

				// 277V NUETRALS
				if(	wire.Color == WireColor.Gray_Brown_Stripe ||
					wire.Color == WireColor.Gray_Orange_Stripe ||
					wire.Color == WireColor.Yellow )
				{
					new_wire = new Wire(wire.CircuitNumber, wire.Size, WireColor.Get_277V(c_num, true, staggered, boy), wire.WireType);
					ret_man.AddWire(conduit_ids, new_wire);
					continue;
				}

				ret_man.AddWire(conduit_ids, wire);
			}

			return ret_man;
		}

		// make sure that a string to parameter from a conduit is parsed properly
		public static WireType ParseCircuitString(string to_parameter, out int[] out_circuits)
		{
			string to = to_parameter;
			List<int> ret_circuits = new List<int>();
			var type = WireType.Branch;

			out_circuits = ret_circuits.ToArray();

			Regex comma_filter = new Regex(@"(?<![-A-Za-z])\b(\d{1,2},?)+");
			Regex hyphen_filter = new Regex(@"(?<![-A-Za-z])\b(\d{1,2}-)\d{1,2}");
			bool comma_pass = comma_filter.Match(to).Success;
			bool hyphen_pass = hyphen_filter.Match(to).Success;

			if (comma_pass)
			{
				type = WireType.Branch;

				// split and filter out all special characters
				List<string> split_str = new List<string>();
				to.Split(' ').ToList().ForEach(x => {

					var comma_split = x.Split(',').ToList();
					comma_split.ForEach(y => {
						if(!y.Any(z => !char.IsLetterOrDigit(z)))
							split_str.Add(y);
					});
				});

				foreach (string mark in split_str)
				{
					// parse circuit mark as int
					int c = -1;
					bool s = int.TryParse(mark, out c);
					if(!s) continue;
					ret_circuits.Add(c);
				}
				out_circuits = ret_circuits.ToArray();
			}
			else if(hyphen_pass)
			{
				type = WireType.Branch;

				string[] split_str = to.Split('-');

				int c1 = -1;
				int c2 = -1;
				bool s = int.TryParse(split_str[0], out c1);
				s = int.TryParse(split_str[1], out c2);

				if(s)
				{
					if(c2 < c1)
					{
						var change = c1;
						c1 = c2;
						c2 = change;
					}
					for(var i = c1; i < c2; ++i)
						ret_circuits.Add(i);
				}

				out_circuits = ret_circuits.ToArray();
			}
			else
			{
				type = WireType.Distribution;
				out_circuits = null;
			}

			return type;
		}

		// hash function to handle wire dictionary swap
		private static int CombineHashCodes(params int[] hashCodes)
		{
			if (hashCodes == null)
			 	throw new ArgumentNullException("hashCodes");


			if (hashCodes.Length == 0)
				throw new IndexOutOfRangeException();

			if (hashCodes.Length == 1)
				return hashCodes[0];

			var result = hashCodes[0];

			for (var i = 1; i < hashCodes.Length; i++)
				result = CombineHashCodes(result, hashCodes[i]);

			return result;
		}

		private static int CombineHashCodes(int h1, int h2)
		{
			return (h1 << 5) + h1 ^ h2;
		}
	}

	public class WireCreationData
	{
		public string Hot { get; private set; }
		public string Nuetral { get; private set; }
		public string Ground { get; private set; }
		public bool StaggeredCircuits { get; private set; }
		public bool BOY { get; private set; }
		public bool PhaseNuetral { get; private set; }
		public bool IsolatedGround { get; private set; }

		public WireCreationData(
			string hot, string nuetral, string ground,
			bool stag, bool boy, bool phase_nuet, bool iso_grd)
		{
			Hot = hot;
			Nuetral = nuetral;
			Ground = ground;
			StaggeredCircuits = stag;
			BOY = boy;
			PhaseNuetral = phase_nuet;
			IsolatedGround = iso_grd;
		}
	}

	[DataContract]
	public class HashedWire
	{
		[DataMember]
		private readonly int h;
		public int Hash {get => h; }
		[DataMember]
		private readonly Wire w;
		public Wire Wire {get => w; }

		public HashedWire(int hash, Wire wire)
		{
			h = hash;
			w = wire;
		}
	}

	public class TotaledWire {
		public string Size { get; set; }
		public string Color { get; set; }
		public double Length { get; set; }

		public TotaledWire(string size, string color, double length) {
			Size = size;
			Color = color;
			Length = length;
		}
	}

	/// <summary>
	/// Container for combining wire into single entries for BOM output
	/// </summary>
	public class WireTotal
	{
		public List<TotaledWire> Wires { get; private set; }

		public bool IsEmpty { get => !Wires.Any(); }

		public WireTotal()
		{
			Wires = new List<TotaledWire>();
		}

		public void PushWire(int[] conduit_ids, double length, WireManager wm, bool telecom = false)
		{
			var wires = wm.GetWires(conduit_ids);

			if(telecom) {
				foreach(var w in wires)
				{
					if(!Wire.TelecomWireNames.Any(x => x.Equals(w.Size))) continue;

					int index = Wires
						.FindIndex(ind => ind.Size.Equals(w.Size) && ind.Color.Equals(w.Color));

					if (index > -1)
					{
						var existing_len = Wires[index].Length;
						var new_entry = new TotaledWire(w.Size, w.Color, existing_len + length);
						Wires[index] = new_entry;
					}
					else
						Wires.Add(new TotaledWire(w.Size, w.Color, length));
				}
			}
			else {
				foreach(var w in wires)
				{
                    var size = w.Size;
					if(!Wire.WireSizes.Any(x => x.Equals(size))) continue;

					int index = Wires
						.FindIndex(ind => ind.Size.Equals(size) && ind.Color.Equals(w.Color));

					if (index > -1)
					{
						var existing_len = Wires[index].Length;
						var new_entry = new TotaledWire(size, w.Color, existing_len + length);
						Wires[index] = new_entry;
					}
					else
						Wires.Add(new TotaledWire(size, w.Color, length));
				}
			}
		}
	}
}
