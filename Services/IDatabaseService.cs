using trader.Models;

namespace trader.Services
{
	public interface IDatabaseService
	{
		public void RecordTransaction(string type, string coinName, decimal quantity, decimal price, decimal fee, decimal? gainLoss);
		public List<HistoricalData> LoadHistoricalData();
		public void InitializeRuntimeContext();
		public decimal CalculateTotalFees();
		public List<(decimal Price, DateTime Timestamp)> GetRecentPrices(string coin, int? rowCount);
		public DateTime? GetLastPurchaseTime(string coin);
		public void SaveDCAConfig(string coin, DateTime lastPurchaseTime);
		public void SaveTrailingStopLoss(string coin, decimal stopLoss);
		public decimal? GetTrailingStopLoss(string coin);
		public void StoreIndicatorsInDatabase(Dictionary<string, decimal> prices);
	}
}
