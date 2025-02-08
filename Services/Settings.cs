namespace trader.Services
{
	public class Settings
	{
		public int CustomIntervalSeconds { get; set; } = 30;
		public int CustomPeriods { get; set; } = 60;
		public string API_URL { get; set; } = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin,ethereum,solana,binancecoin,cardano&vs_currencies=usd";
		public string DbPath { get; set; } = "crypto_prices.db";
		public decimal StopLossThreshold { get; set; } = -0.05m;
		public decimal ProfitTakingThreshold { get; set; } = 0.05m;
		public decimal MaxInvestmentPerCoin { get; set; } = 3000m;
		public decimal StartingBalance { get; set; } = 10000m;
		public decimal TransactionFeeRate { get; set; } = 0.01m;
		public decimal TrailingStopLossPercentage { get; set; } = 0.05m;
		public decimal DollarCostAveragingAmount { get; set; } = 100m;
		public int DollarCostAveragingSecondsInterval { get; set; } = 60 * 60 * 3;
		public bool CheckForValidTimeIntervalToPerformAnalysis { get; set; } = true;
	}
}
