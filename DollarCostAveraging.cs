using Spectre.Console;

namespace Trader
{
	public static class DollarCostAveraging
	{
		public static string[] ExecuteDCA(string coin, decimal amount, decimal currentPrice, TimeSpan interval)
		{
			DateTime? lastPurchaseTime = Program.GetLastPurchaseTime(coin);
			if (!lastPurchaseTime.HasValue || (DateTime.UtcNow - lastPurchaseTime.Value) >= interval)
			{
				decimal quantity = amount / currentPrice;
				var buyResult = Program.tradeOperations.Buy(coin, quantity, currentPrice);
				Program.SaveDCAConfig(coin, DateTime.UtcNow);

				return buyResult;
			}

			return new string[] { };
		}
	}
}