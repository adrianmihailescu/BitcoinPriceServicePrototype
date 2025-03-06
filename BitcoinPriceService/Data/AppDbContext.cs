using Microsoft.EntityFrameworkCore;
using BitcoinPriceService.Models;

namespace BitcoinPriceService.Data;

public class AppDbContext : DbContext
{
    public DbSet<BitcoinPrice> BitcoinPrices { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
