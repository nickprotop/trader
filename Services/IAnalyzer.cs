using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace trader.Services
{
	public interface IAnalyzer
	{
		public List<string> AnalyzeIndicators(int customPeriods, int analysisWindowSeconds, bool analysisOnly);

	}
}
