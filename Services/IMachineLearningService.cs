using Microsoft.ML;
using System.Collections.Generic;
using trader.Models;

namespace trader.Services
{
	public interface IMachineLearningService
	{
		public void TrainModel();
		void SaveModel(string modelPath);
		void LoadModel(string modelPath);
		float Predict(CryptoData input);
		public void CrossValidateModel(IDataView dataView, IEstimator<ITransformer> pipeline);
	}
}
