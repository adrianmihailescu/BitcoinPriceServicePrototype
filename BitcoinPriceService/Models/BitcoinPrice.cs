namespace BitcoinPriceService.Models;

public class BitcoinPrice
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } // Stored in UTC
    public double AggregatedPrice { get; set; }
}
