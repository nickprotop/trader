namespace Trader
{
	public static class TrailingStopLoss
	{
		public static void UpdateTrailingStopLoss(string coin, decimal currentPrice, decimal trailingPercentage)
		{
			decimal? stopLoss = Program.GetTrailingStopLoss(coin);
			if (!stopLoss.HasValue)
			{
				stopLoss = currentPrice * (1 - trailingPercentage);
				Program.SaveTrailingStopLoss(coin, stopLoss.Value);
			}
			else
			{
				decimal newStopLoss = currentPrice * (1 - trailingPercentage);
				if (newStopLoss > stopLoss.Value)
				{
					Program.SaveTrailingStopLoss(coin, newStopLoss);
				}
			}
		}

		public static decimal GetTrailingStopLoss(string coin)
		{
			return Program.GetTrailingStopLoss(coin) ?? decimal.MaxValue;
		}
	}
}