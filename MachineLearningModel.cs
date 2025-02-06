using Microsoft.ML;
using Microsoft.ML.Data;
using Spectre.Console;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Trader
{
	public static class MachineLearningModel
	{
		private static MLContext mlContext = new MLContext();
		private static ITransformer? model;

		public static void TrainModel(List<CryptoData> trainingData)
		{
			// Validate training data and log non-finite values
			foreach (var data in trainingData)
			{
				if (!IsFinite(data.Price) || !IsFinite(data.SMA) || !IsFinite(data.EMA) || !IsFinite(data.RSI) || !IsFinite(data.MACD) || !IsFinite(data.BollingerUpper) || !IsFinite(data.BollingerLower) || !IsFinite(data.ATR) || !IsFinite(data.Volatility))
				{
					Console.WriteLine($"Non-finite value found in training data: {data}");
					throw new InvalidOperationException("Training data contains non-finite values.");
				}
			}

			var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

			var pipeline = mlContext.Transforms.Concatenate("Features", nameof(CryptoData.Price), nameof(CryptoData.SMA), nameof(CryptoData.EMA), nameof(CryptoData.RSI), nameof(CryptoData.MACD), nameof(CryptoData.BollingerUpper), nameof(CryptoData.BollingerLower), nameof(CryptoData.ATR), nameof(CryptoData.Volatility))
				.Append(mlContext.Transforms.NormalizeMinMax("Features"))
				.Append(mlContext.Regression.Trainers.Sdca(labelColumnName: "Label"));

			AnsiConsole.WriteLine("Performing cross-validation in background...");
			Task.Run(() => CrossValidateModel(dataView, pipeline));

			AnsiConsole.WriteLine("Training started.");
			model = pipeline.Fit(dataView);
			AnsiConsole.WriteLine("Training completed.");
		}

		private static void CrossValidateModel(IDataView dataView, IEstimator<ITransformer> pipeline)
		{
			try
			{
				var cvResults = mlContext.Regression.CrossValidate(dataView, pipeline, numberOfFolds: 5);
				var avgR2 = cvResults.Average(r => r.Metrics.RSquared);
				var avgRMSE = cvResults.Average(r => r.Metrics.RootMeanSquaredError);

				// Output the results in a table
				var table = new Table();
				table.AddColumn("Fold");
				table.AddColumn("R^2");
				table.AddColumn("RMSE");

				for (int i = 0; i < cvResults.Count; i++)
				{
					table.AddRow(
						(i + 1).ToString(),
						cvResults[i].Metrics.RSquared.ToString("N4"),
						cvResults[i].Metrics.RootMeanSquaredError.ToString("N4")
					);
				}

				table.AddRow("Average", avgR2.ToString("N4"), avgRMSE.ToString("N4"));

				AnsiConsole.MarkupLine("[bold cyan]Cross-validation results:[/]");
				AnsiConsole.Write(table);
			}
			catch (Exception ex)
			{
				AnsiConsole.MarkupLine($"[bold red]Cross-validation failed: {ex.Message}[/]");
			}
		}

		public static void SaveModel(string modelPath)
		{
			if (model == null)
			{
				throw new InvalidOperationException("Model has not been trained.");
			}

			using (var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.Write))
			{
				mlContext.Model.Save(model, null, fileStream);
			}
		}

		public static void LoadModel(string modelPath)
		{
			using (var fileStream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				model = mlContext.Model.Load(fileStream, out var _);
			}
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

		public static float Predict(CryptoData input)
		{
			if (model == null)
			{
				throw new InvalidOperationException("Model has not been loaded.");
			}

			var predictionEngine = mlContext.Model.CreatePredictionEngine<CryptoData, CryptoPrediction>(model);
			var prediction = predictionEngine.Predict(input);
			return prediction.Score;
		}

		public class CryptoPrediction
		{
			[ColumnName("Score")]
			public float Score { get; set; }
		}
	}
}
