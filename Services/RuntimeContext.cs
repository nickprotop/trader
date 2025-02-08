namespace trader.Services
{
	public class RuntimeContext
	{
		public decimal Balance { get; set; } = 0;
		public Dictionary<string, List<decimal>> CachedPrices { get; set; } = new Dictionary<string, List<decimal>>();
		public Dictionary<string, decimal> Portfolio { get; set; } = new Dictionary<string, decimal>();
		public Dictionary<string, decimal> InitialInvestments { get; set; } = new Dictionary<string, decimal>();
		public Dictionary<string, decimal> TotalQuantityPerCoin { get; set; } = new Dictionary<string, decimal>();
		public Dictionary<string, decimal> TotalCostPerCoin { get; set; } = new Dictionary<string, decimal>();
		public Dictionary<string, decimal> CurrentPrices { get; set; } = new Dictionary<string, decimal>();
		public int CurrentPeriodIndex { get; set; } = 0;
	}
}
