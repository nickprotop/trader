using Trader;

namespace trader.Services
{
	public interface IDatabaseService
	{
		public void RecordTransaction(string type, string coinName, decimal quantity, decimal price, decimal fee, decimal? gainLoss);
		public List<HistoricalData> LoadHistoricalData();
		public void InitializeRuntimeContext();

	}
}
