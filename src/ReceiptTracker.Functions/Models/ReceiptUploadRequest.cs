namespace ReceiptTracker.Functions.Models;

public class ReceiptUploadRequest
{
    public string ImageBase64 { get; set; } = string.Empty;
    public string? FileName { get; set; } = "receipt.jpg";
    public string? Notes { get; set; }
}

public class ReceiptUploadResponse
{
    public bool Success { get; set; }
    public Guid? ReceiptId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public DateTime? PurchaseDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public List<ParsedReceiptItemSummary> Items { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public string? DiagnosticInfo { get; set; }
}

public class ParsedReceiptItemSummary
{
    public string RawName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public Guid? MatchedProductId { get; set; }
    public string? MatchedProductName { get; set; }
}

