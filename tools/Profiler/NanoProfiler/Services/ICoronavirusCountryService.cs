
using nanoFramework.Tools.NanoProfiler.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.NanoProfiler.Services
{
    public interface ICoronavirusCountryService
    {
        Task<IEnumerable<CoronavirusCountry>> GetTopCases(int amountOfCountries);
    }
}
