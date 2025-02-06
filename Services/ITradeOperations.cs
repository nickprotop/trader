using Spectre.Console;

namespace trader.Services
{
	public interface ITradeOperations
	{
		string[] Buy(string coinName, decimal? quantity, decimal price);
		string[] Sell(string coinName, decimal price);
	}
}
