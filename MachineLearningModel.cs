using Microsoft.ML;
using Microsoft.ML.Data;
using System.Collections.Generic;
using System.IO;

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
				if (!IsFinite(data.Price) || !IsFinite(data.SMA) || !IsFinite(data.EMA) || !IsFinite(data.RSI) || !IsFinite(data.MACD))
				{
					Console.WriteLine($"Non-finite value found in training data: {data}");
					throw new InvalidOperationException("Training data contains non-finite values.");
				}
			}

			var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

			var pipeline = mlContext.Transforms.Concatenate("Features", nameof(CryptoData.Price), nameof(CryptoData.SMA), nameof(CryptoData.EMA), nameof(CryptoData.RSI), nameof(CryptoData.MACD))
				.Append(mlContext.Transforms.NormalizeMinMax("Features"))
				.Append(mlContext.Regression.Trainers.Sdca(labelColumnName: "Label", maximumNumberOfIterations: 100));

			model = pipeline.Fit(dataView);
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
