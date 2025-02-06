using Spectre.Console;

namespace trader.Services
{
	public interface ITradeOperations
	{
		string[] Buy(string coinName, decimal? quantity, decimal price);
		string[] Sell(string coinName, decimal price);
	}

	public class SimulationTradeOperations : ITradeOperations
	{
		private readonly IDatabaseService _databaseService;
		private readonly ISettingsService _settingsService;
		private readonly RuntimeContext _runtimeContext;

		public SimulationTradeOperations(IDatabaseService databaseService, ISettingsService settingsService, RuntimeContext runtimeContext)
		{
			_databaseService = databaseService;
			_settingsService = settingsService;
			_runtimeContext = runtimeContext;
		}

		public string[] Buy(string coinName, decimal? quantity, decimal price)
		{
			decimal amountToBuy = quantity ?? _runtimeContext.Balance / 10 / price; // Buy with 10% of the balance
			decimal cost = amountToBuy * price;
			decimal fee = cost * _settingsService.Settings.TransactionFeeRate;
			decimal totalCost = cost + fee;

			// Calculate the current investment in the coin
			decimal currentInvestment = _runtimeContext.InitialInvestments.ContainsKey(coinName) ? _runtimeContext.InitialInvestments[coinName] : 0;

			// Check if the new investment exceeds the maximum limit
			if (currentInvestment + totalCost > _settingsService.Settings.MaxInvestmentPerCoin)
			{
				return new string[] { $"[bold red]Cannot buy {amountToBuy} of {coinName} at {price} each. Maximum investment limit of {_settingsService.Settings.MaxInvestmentPerCoin:C} exceeded.[/]" };
			}

			if (_runtimeContext.Balance >= totalCost)
			{
				_runtimeContext.Balance -= totalCost;

				// Update portfolio
				if (_runtimeContext.Portfolio.ContainsKey(coinName))
					_runtimeContext.Portfolio[coinName] += amountToBuy;
				else
					_runtimeContext.Portfolio[coinName] = amountToBuy;

				// Update initial investments
				if (_runtimeContext.InitialInvestments.ContainsKey(coinName))
					_runtimeContext.InitialInvestments[coinName] += cost;
				else
					_runtimeContext.InitialInvestments[coinName] = cost;

				// Update total quantity and cost for cost basis calculation
				if (_runtimeContext.TotalQuantityPerCoin.ContainsKey(coinName))
				{
					_runtimeContext.TotalQuantityPerCoin[coinName] += amountToBuy;
					_runtimeContext.TotalCostPerCoin[coinName] += cost;
				}
				else
				{
					_runtimeContext.TotalQuantityPerCoin[coinName] = amountToBuy;
					_runtimeContext.TotalCostPerCoin[coinName] = cost;
				}

				// Record the transaction in the database
				_databaseService.RecordTransaction("BUY", coinName, amountToBuy, price, fee, null);

				return new string[] { $"[bold green]Bought {amountToBuy} of {coinName} at {price} each. Fee: {fee:C}. New balance: {_runtimeContext.Balance:C}[/]" };
			}
			else
			{
				return new string[] { $"[bold red]Insufficient balance to buy {amountToBuy} of {coinName} at {price} each.[/]" };
			}
		}

		public string[] Sell(string coinName, decimal price)
		{
			if (_runtimeContext.Portfolio.ContainsKey(coinName) && _runtimeContext.Portfolio[coinName] > 0)
			{
				decimal quantityToSell = _runtimeContext.Portfolio[coinName];
				decimal amountToSell = quantityToSell * price;
				decimal fee = amountToSell * _settingsService.Settings.TransactionFeeRate;
				decimal netAmountToSell = amountToSell - fee;

				// Calculate average cost basis
				decimal averageCostBasis = _runtimeContext.TotalCostPerCoin[coinName] / _runtimeContext.TotalQuantityPerCoin[coinName];
				decimal costOfSoldCoins = averageCostBasis * quantityToSell;

				// Calculate gain or loss
				decimal gainOrLoss = netAmountToSell - costOfSoldCoins;

				// Update balance and portfolio
				_runtimeContext.Balance += netAmountToSell;
				_runtimeContext.Portfolio[coinName] = 0;

				// Update total quantity and cost
				_runtimeContext.TotalQuantityPerCoin[coinName] -= quantityToSell;
				_runtimeContext.TotalCostPerCoin[coinName] -= costOfSoldCoins;

				// Adjust initial investments
				if (_runtimeContext.InitialInvestments.ContainsKey(coinName))
				{
					_runtimeContext.InitialInvestments[coinName] -= costOfSoldCoins;
					if (_runtimeContext.InitialInvestments[coinName] <= 0)
					{
						_runtimeContext.InitialInvestments.Remove(coinName);
					}
				}

				// Record the transaction in the database with gain or loss
				_databaseService.RecordTransaction("SELL", coinName, quantityToSell, price, fee, gainOrLoss);

				return new string[] {
					$"[bold cyan]Sold {quantityToSell} of {coinName} at {price} each. Fee: {fee:C}. New balance: {_runtimeContext.Balance:C}[/]",
					$"Transaction Gain/Loss: {(gainOrLoss > 0 ? "[bold green]" : "[bold red]")}{gainOrLoss:C}[/]"
				};
			}
			else
			{
				return new string[] { $"[bold red]No holdings to sell for {coinName}.[/]" };
			}
		}
	}
}
