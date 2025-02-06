namespace trader.Models
{
	public class HistoricalData
	{
		public string Name { get; set; } = "###";
		public decimal Price { get; set; }
		public decimal SMA { get; set; }
		public decimal EMA { get; set; }
		public decimal RSI { get; set; }
		public decimal MACD { get; set; }
		public DateTime Timestamp { get; set; }
		public decimal BollingerUpper { get; set; }
		public decimal BollingerLower { get; set; }
		public decimal ATR { get; set; }
		public decimal Volatility { get; set; }
	}
}
