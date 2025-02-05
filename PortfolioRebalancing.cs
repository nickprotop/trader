using Spectre.Console;

namespace Trader
{
    public static class PortfolioRebalancing
    {
        public static void RebalancePortfolio(Dictionary<string, decimal> targetAllocations, Dictionary<string, decimal> currentPrices)
        {
            decimal totalValue = RuntimeContext.balance + RuntimeContext.portfolio.Sum(p => p.Value * currentPrices[p.Key]);

            foreach (var target in targetAllocations)
            {
                string coin = target.Key;
                decimal targetValue = totalValue * target.Value;
                decimal currentValue = RuntimeContext.portfolio.ContainsKey(coin) ? RuntimeContext.portfolio[coin] * currentPrices[coin] : 0;

                if (currentValue < targetValue)
                {
                    decimal amountToBuy = targetValue - currentValue;
                    decimal quantityToBuy = amountToBuy / currentPrices[coin];
                    var buyResult = Program.tradeOperations.Buy(coin, quantityToBuy, currentPrices[coin]);
                    foreach (var result in buyResult)
                    {
                        AnsiConsole.MarkupLine(result);
                    }
                }
                else if (currentValue > targetValue)
                {
                    decimal amountToSell = currentValue - targetValue;
                    decimal quantityToSell = amountToSell / currentPrices[coin];
                    var sellResult = Program.tradeOperations.Sell(coin, currentPrices[coin]);
                    foreach (var result in sellResult)
                    {
                        AnsiConsole.MarkupLine(result);
                    }
                }
            }
        }
    }
}
