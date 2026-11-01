namespace ReceiptTracker.Functions.Models;

public class ParsedReceipt
{
    public string MerchantName { get; set; } = "Unknown Merchant";
    public DateTime? TransactionDate { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }
    public List<ParsedReceiptItem> Items { get; set; } = new();
}

public class ParsedReceiptItem
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1.0m;
    public decimal? UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public float? Confidence { get; set; }
}

