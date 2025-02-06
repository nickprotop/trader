namespace trader.Models
{
	public class CryptoData
	{
		public float Price { get; set; }
		public float SMA { get; set; }
		public float EMA { get; set; }
		public float RSI { get; set; }
		public float MACD { get; set; }
		public float BollingerUpper { get; set; }
		public float BollingerLower { get; set; }
		public float ATR { get; set; }
		public float Volatility { get; set; }
		public float Label { get; set; } // The label is the target value (e.g., future price)

		public override string ToString()
		{
			return $"Price: {Price}, SMA: {SMA}, EMA: {EMA}, RSI: {RSI}, MACD: {MACD}, BollingerUpper: {BollingerUpper}, BollingerLower: {BollingerLower}, ATR: {ATR}, Volatility: {Volatility}, Label: {Label}";
		}
	}
}
