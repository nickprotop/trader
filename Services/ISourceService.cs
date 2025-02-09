using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trader;

namespace trader.Services
{
	public enum PriceSource
	{
		coingecko = 1,
		coinbase = 2
	}

	public interface ISourceService
	{
		public Task<Dictionary<string, decimal>> GetCryptoPrices(PriceSource priceSource);
	}
}
