using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BT.Forecaster {
	public class ModelOutput {
		public float[] ForecastedRevenue { get; set; }

		public float[] LowerBoundRevenue { get; set; }

		public float[] UpperBoundRevenue { get; set; }
	}
}
