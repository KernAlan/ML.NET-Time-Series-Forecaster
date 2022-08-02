using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;
using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace BT.Forecaster {
	public class RevenueForecaster {
		/// <summary>
		/// How many "periods" you want to predict for. If you're intaking days and want to output for a year, horizon would be 365
		/// </summary>
		public int TimeHorizonDesired { get; set; }
		/// <summary>
		/// The size of each window iteration you want to train on. To capture more variance, increase the window. Note that there is a minimum ratio of data to number of windows. Note: This is the most important number to accuracy
		/// </summary>
		public int WindowSize { get; set; }
		/// <summary>
		/// How many periods to leave in buffer. Usually it makes sense to mkae this WindowSize +1.
		/// </summary>
		public int SeriesLength { get; set; }
		/// <summary>
		/// Always make train size the size of your data set if possible. There is no penalty for making this a very large number--it just sets the maximum training set number.
		/// </summary>
		public int TrainSize { get; set; }
		/// <summary>
		/// Float between 0 and 1. This sets the distance between lower and upper bounds. The lower your confidence number, the tighter the upper and lower ranges will be.
		/// </summary>
		public float ConfidenceLevel { get; set; }

		public RevenueForecaster(long tenantId, int timeHorizonDesired, int windowSize, int seriesLength, int trainSize, float confidenceLevel) {
			TenantId = tenantId;
			TimeHorizonDesired = timeHorizonDesired;
			WindowSize = windowSize;
			SeriesLength = seriesLength;
			TrainSize = trainSize;
			ConfidenceLevel = confidenceLevel;
		}

		public List<long[]> Run() {
			string rootDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../"));
			string modelPath = Path.Combine(rootDir, "MLModel.zip"); // This is where the model is saved
			var connectionString = "";

			MLContext mlContext = new MLContext();

			DatabaseLoader loader = mlContext.Data.CreateDatabaseLoader<ModelInput>();

			string query = "";

			DatabaseSource dbSource = new DatabaseSource(SqlClientFactory.Instance,
											connectionString,
											query);

			IDataView dataView = loader.Load(dbSource);

			IDataView firstYearData = mlContext.Data.FilterRowsByColumn(dataView, "Year", upperBound: 1);
			IDataView secondYearData = mlContext.Data.FilterRowsByColumn(dataView, "Year", lowerBound: 1);

			// Here we set the parameters for how we will forecast by SSA

			var forecastingPipeline = mlContext.Forecasting.ForecastBySsa(
				outputColumnName: "ForecastedRevenue",
				inputColumnName: "TotalInvoiceAmt",
				windowSize: WindowSize,
				seriesLength: SeriesLength,
				trainSize: TrainSize,
				horizon: TimeHorizonDesired,
				confidenceLevel: ConfidenceLevel,
				confidenceLowerBoundColumn: "LowerBoundRevenue",
				confidenceUpperBoundColumn: "UpperBoundRevenue");

			SsaForecastingTransformer forecaster = forecastingPipeline.Fit(firstYearData);

			Evaluate(secondYearData, forecaster, mlContext);

			var forecastEngine = forecaster.CreateTimeSeriesEngine<ModelInput, ModelOutput>(mlContext);

			// CheckPoint saves the model
			forecastEngine.CheckPoint(mlContext, modelPath);

			// This first forecast is to evaluate how well our model performs via console write
			Forecast(secondYearData, TimeHorizonDesired, forecastEngine, mlContext);

			// Now use the entire dataset
			var entireForecast = forecastingPipeline.Fit(dataView);

			// This is the actual prediction using the time horizon defined. Unlike forecast, this will not output metrics since it's extending our model
			ModelOutput forecast = forecastEngine.Predict();
			Console.WriteLine(forecast);

			// Now put the forecast into a list we can access easily

			var list = new List<long[]>();

			for (var i = 0; i < forecast.ForecastedRevenue.Length; i++) {
				list.Add(new long[] { Math.Max(0, (long)forecast.LowerBoundRevenue[i]), Math.Max(0, (long)forecast.ForecastedRevenue[i]), Math.Max(0, (long)forecast.UpperBoundRevenue[i]) });
			}

			return list;
		}

		public void Evaluate(IDataView testData, ITransformer model, MLContext mlContext) {
			// Make predictions
			IDataView predictions = model.Transform(testData);

			// Actual values
			IEnumerable<float> actual =
				mlContext.Data.CreateEnumerable<ModelInput>(testData, true)
					.Select(observed => observed.TotalInvoiceAmt);

			// Predicted values
			IEnumerable<float> forecast =
				mlContext.Data.CreateEnumerable<ModelOutput>(predictions, true)
					.Select(prediction => prediction.ForecastedRevenue[0]);

			// Calculate error (actual - forecast)
			var metrics = actual.Zip(forecast, (actualValue, forecastValue) => actualValue - forecastValue);

			// Get metric averages
			var MAE = metrics.Average(error => Math.Abs(error)); // Mean Absolute Error
			var RMSE = Math.Sqrt(metrics.Average(error => Math.Pow(error, 2))); // Root Mean Squared Error

			// Output metrics
			Console.WriteLine("Evaluation Metrics");
			Console.WriteLine("---------------------");
			Console.WriteLine($"Mean Absolute Error: {MAE:F3}");
			Console.WriteLine($"Root Mean Squared Error: {RMSE:F3}\n");
		}

		public void Forecast(IDataView testData, int horizon, TimeSeriesPredictionEngine<ModelInput, ModelOutput> forecaster, MLContext mlContext) {

			ModelOutput forecast = forecaster.Predict();

			IEnumerable<string> forecastOutput =
				mlContext.Data.CreateEnumerable<ModelInput>(testData, reuseRowObject: false)
					.Take(horizon)
					.Select((ModelInput invoice, int index) => {
						string month = invoice.Month.ToShortDateString();
						float actualInvoiceAmt = invoice.TotalInvoiceAmt;
						float lowerEstimate = Math.Max(0, forecast.LowerBoundRevenue[index]);
						float estimate = forecast.ForecastedRevenue[index];
						float upperEstimate = forecast.UpperBoundRevenue[index];
						return $"Date: {month}\n" +
						$"Actual Invoiced Amt: {actualInvoiceAmt}\n" +
						$"Lower Estimate: {lowerEstimate}\n" +
						$"Forecast: {estimate}\n" +
						$"Upper Estimate: {upperEstimate}\n";
					});

			// Output predictions
			Console.WriteLine("Revenue Forecast");
			Console.WriteLine("---------------------");
			foreach (var prediction in forecastOutput) {
				Console.WriteLine(prediction);
			}
		}

		public List<long> Actuals() {
			var list = new List<long>();
			var connectionString = "";

			DataTable dt = new DataTable();

			using (SqlConnection connection = new SqlConnection(connectionString)) {
				using (SqlCommand cmd = new SqlCommand(query, connection)) {
					connection.Open();
					using (SqlDataReader reader = cmd.ExecuteReader()) {
						dt.Load(reader);
					}
				}
			}

			foreach (DataRow cRow in dt.Rows) {
				var amt = cRow.GetOrDefault<int>("TotalInvoiceAmt", 0);
				list.Add(amt);
			}

			return list;
		}
	}
}