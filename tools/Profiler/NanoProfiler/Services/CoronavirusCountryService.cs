
using nanoFramework.Tools.NanoProfiler.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace nanoFramework.Tools.NanoProfiler.Services.API
{
    public class CoronavirusCountryService : ICoronavirusCountryService
    {
        public async Task<IEnumerable<CoronavirusCountry>> GetTopCasesAsync(int amountOfCountries)
        {
            using(HttpClient client = new HttpClient())
            {
                string requestUri = "https://corona.lmao.ninja/v3/covid-19/countries?sort=cases";

                HttpResponseMessage apiResponse = await client.GetAsync(requestUri);

                string jsonResponse = await apiResponse.Content.ReadAsStringAsync();

                List<CoronavirusCountry> apiCountries = JsonSerializer.Deserialize<List<CoronavirusCountry>>(jsonResponse, new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });

                return apiCountries.Take(amountOfCountries).Select(apiCountry => new CoronavirusCountry()
                {
                    Country = apiCountry.Country,
                    Cases = apiCountry.Cases

                });
            }
        }
    }
}
