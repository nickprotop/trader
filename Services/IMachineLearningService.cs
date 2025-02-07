using Microsoft.ML;
using System.Collections.Generic;
using trader.Models;

namespace trader.Services
{
	public interface IMachineLearningService
	{
		void TrainModel(List<CryptoData> trainingData);
		void SaveModel(string modelPath);
		void LoadModel(string modelPath);
		float Predict(CryptoData input);
		public void CrossValidateModel(IDataView dataView, IEstimator<ITransformer> pipeline);
	}
}
