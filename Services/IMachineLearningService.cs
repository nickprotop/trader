using System.Collections.Generic;
using Trader;

namespace trader.Services
{
	public interface IMachineLearningService
	{
		void TrainModel(List<CryptoData> trainingData);
		void SaveModel(string modelPath);
		void LoadModel(string modelPath);
		float Predict(CryptoData input);
	}
}
