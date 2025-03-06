using RestSharp;
using Microsoft.Extensions.Caching.Memory;
using BitcoinPriceService.Data;
using BitcoinPriceService.Models;

namespace BitcoinPriceService.Services;

public class BitcoinPriceAggregatorService
{
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BitcoinPriceAggregatorService> _logger;
    private static string BitstampUrl = "https://www.bitstamp.net/api/v2/ohlc/btcusd/?step=3600&limit=1", BitfinexUrl = "https://api.bitfinex.com/v2/candles/trade:1h:tBTCUSD/hist?limit=1";

    public BitcoinPriceAggregatorService(AppDbContext dbContext, IMemoryCache cache, ILogger<BitcoinPriceAggregatorService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<double?> GetAggregatedPriceAsync(DateTime timestamp)
    {
        string cacheKey = $"btc_price_{timestamp:yyyyMMddHH}";

        if (_cache.TryGetValue(cacheKey, out double cachedPrice))
        {
            _logger.LogInformation("Serving from cache.");
            return cachedPrice;
        }

        var dbPrice = _dbContext.BitcoinPrices
        .FirstOrDefault(p => p.Timestamp == timestamp.ToUniversalTime());

        if (dbPrice != null)
        {
            _cache.Set(cacheKey, dbPrice.AggregatedPrice, TimeSpan.FromHours(1));
            return dbPrice.AggregatedPrice;
        }

        double? price1 = await FetchPriceFromApi(BitstampUrl);
        double? price2 = await FetchPriceFromApi(BitfinexUrl);

        if (price1 == null || price2 == null) return null;

        double aggregatedPrice = (price1.Value + price2.Value) / 2;

        var newPrice = new BitcoinPrice
        {
            Timestamp = timestamp.ToUniversalTime(),  // Ensure UTC format
            AggregatedPrice = aggregatedPrice
        };
        _dbContext.BitcoinPrices.Add(newPrice);
        await _dbContext.SaveChangesAsync();

        _cache.Set(cacheKey, aggregatedPrice, TimeSpan.FromHours(1));
        return aggregatedPrice;
    }

    // Added 'virtual' so it can be mocked in tests
    public virtual async Task<double?> FetchPriceFromApi(string url)
    {
        try
        {
            var client = new RestClient();
            var request = new RestRequest(url, Method.Get);
            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                return null;

            var json = System.Text.Json.JsonDocument.Parse(response.Content);

            // Bitstamp JSON format requires accessing "data" -> "ohlc" array
            if (url.Contains("bitstamp"))
            {
                var ohlcArray = json.RootElement.GetProperty("data").GetProperty("ohlc");
                if (ohlcArray.GetArrayLength() > 0)
                {
                    // Convert string to double explicitly
                    string closePriceString = ohlcArray[0].GetProperty("close").GetString();
                    if (double.TryParse(closePriceString, out double closePrice))
                    {
                        return closePrice;
                    }
                    else
                    {
                        _logger.LogError("Failed to parse close price: {ClosePriceString}", closePriceString);
                        return null;
                    }
                }
            }

            // Bitfinex JSON format is an array at root level
            else if (url.Contains("bitfinex"))
            {
                var jsonArray = json.RootElement;
                if (jsonArray.GetArrayLength() > 0)
                {
                    return jsonArray[0][2].GetDouble(); // Close price is at index 2
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error fetching price from {Url}: {Message}", url, ex.Message);
            return null;
        }
    }
}
