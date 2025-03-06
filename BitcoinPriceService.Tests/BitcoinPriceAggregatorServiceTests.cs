namespace BitcoinPriceService.Tests;

using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using BitcoinPriceService.Data;
using BitcoinPriceService.Models;
using BitcoinPriceService.Services;
using Microsoft.EntityFrameworkCore;

public class BitcoinPriceAggregatorServiceTests
{
    private BitcoinPriceAggregatorService GetService(AppDbContext dbContext, IMemoryCache cache, ILogger<BitcoinPriceAggregatorService> logger)
    {
        return new BitcoinPriceAggregatorService(dbContext, cache, logger);
    }

    [Fact]
    public async Task GetAggregatedPriceAsync_ReturnsCachedPrice_IfAvailable()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDB1")
            .Options;
        var dbContext = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<BitcoinPriceAggregatorService>>().Object;
        var service = GetService(dbContext, cache, logger);

        DateTime testTime = DateTime.UtcNow;
        double cachedPrice = 45000.5;
        cache.Set($"btc_price_{testTime:yyyyMMddHH}", cachedPrice, TimeSpan.FromHours(1));

        // Act
        var result = await service.GetAggregatedPriceAsync(testTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(cachedPrice, result);
    }

    [Fact]
    public async Task GetAggregatedPriceAsync_ReturnsPriceFromDB_IfNotCached()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDB2")
            .Options;
        var dbContext = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<BitcoinPriceAggregatorService>>().Object;
        var service = GetService(dbContext, cache, logger);

        DateTime testTime = DateTime.UtcNow;
        double dbPrice = 46000.8;

        dbContext.BitcoinPrices.Add(new BitcoinPrice
        {
            Timestamp = testTime,
            AggregatedPrice = dbPrice
        });
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.GetAggregatedPriceAsync(testTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(dbPrice, result);
    }

    [Fact]
    public async Task GetAggregatedPriceAsync_FetchesAndStoresPrice_IfNotCachedOrInDB()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDB3")
            .Options;
        var dbContext = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<BitcoinPriceAggregatorService>>().Object;
        var service = GetService(dbContext, cache, logger);

        DateTime testTime = DateTime.UtcNow;

        // Mock the API calls
        var mockService = new Mock<BitcoinPriceAggregatorService>(dbContext, cache, logger);
        mockService.CallBase = true; // Allow actual methods except those overridden
        mockService.Setup(s => s.FetchPriceFromApi(It.IsAny<string>()))
                .ReturnsAsync(45000.0); // Mock API price

        // Act
        var result = await mockService.Object.GetAggregatedPriceAsync(testTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(45000.0, result); // Mocked API price
    }
}

