using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BT.Forecaster {
	public class ModelInput {
		public DateTime Month { get; set; }

		public float Year { get; set; }

		public float TotalInvoiceAmt { get; set; }
	}
}
