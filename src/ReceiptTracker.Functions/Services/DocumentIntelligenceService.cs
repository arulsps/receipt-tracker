using System.Globalization;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ReceiptTracker.Functions.Models;

namespace ReceiptTracker.Functions.Services;

public class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private static readonly Regex PricePattern = new(@"([0-9]{1,4}(?:,[0-9]{3})*\.[0-9]{2}|[0-9]{1,4},[0-9]{2}|[0-9]+)", RegexOptions.Compiled);
    private static readonly Regex PriceOnlyRegex = new(@"^[\$£€¥]?\s*([0-9]{1,4}(?:,[0-9]{3})*\.[0-9]{2}|[0-9]{1,4},[0-9]{2})(?:\s*[\*A-Za-z0-9%]{1,3})?$", RegexOptions.Compiled);
    private static readonly Regex DatePattern = new(@"\b(\d{1,4}[-/\.]\d{1,2}[-/\.]\d{1,4}|\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{2,4})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TotalKeywordRegex = new(@"(?i)\b(total|amount due|balance due|total to pay|grand total|balance|amount paid)\b", RegexOptions.Compiled);
    private static readonly Regex SummaryLineRegex = new(@"(?i)\b(subtotal|sub-total|total|tax|vat|balance due|amount due|cash|change|visa|mastercard|amex|debit|credit|card|thank you|welcome|customer copy|tip|gratuity|payment)\b", RegexOptions.Compiled);
    private static readonly Regex HeaderLineRegex = new(@"(?i)^(tel|phone|register|cashier|order #|receipt #|invoice #|date|time|vat reg|table|server)\b", RegexOptions.Compiled);
    private static readonly Regex ItemLineRegex = new(@"^(?:(\d+)\s*[xX*]?\s+)?(.+?)\s+[\$£€¥]?\s*([0-9]{1,4}(?:,[0-9]{3})*\.[0-9]{2}|[0-9]{1,4},[0-9]{2})(?:\s*[\*A-Za-z0-9%]{1,3})?$", RegexOptions.Compiled);
    private static readonly Regex TimePattern = new(@"^\d{1,2}:\d{2}(?::\d{2})?(?:\s*(?:AM|PM|am|pm))?$", RegexOptions.Compiled);
    private static readonly Regex PhoneUiPattern = new(@"(?i)^(wifi|5g|4g|3g|lte|search|cancel|done|settings|back|new shortcut|<|>|\.\.\.)$", RegexOptions.Compiled);

    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentIntelligenceService> _logger;
    private DocumentIntelligenceClient? _client;
    private readonly object _lock = new();

    public DocumentIntelligenceService(IConfiguration configuration, ILogger<DocumentIntelligenceService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private DocumentIntelligenceClient GetClient()
    {
        if (_client != null)
        {
            return _client;
        }

        lock (_lock)
        {
            if (_client != null) return _client;

            var endpoint = (_configuration["DocumentIntelligence:Endpoint"]
                ?? _configuration["DocumentIntelligence__Endpoint"]
                ?? _configuration["DocumentIntelligenceEndpoint"])?.Trim();

            var apiKey = (_configuration["DocumentIntelligence:ApiKey"]
                ?? _configuration["DocumentIntelligence__ApiKey"]
                ?? _configuration["DocumentIntelligenceApiKey"])?.Trim();

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("DocumentIntelligence endpoint or API key is not configured in environment variables.");
            }

            _client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            return _client;
        }
    }

    public async Task<ParsedReceipt> AnalyzeReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending receipt stream (length: {Length} bytes) to Azure AI Document Intelligence (prebuilt-receipt)...", imageStream.Length);

        var client = GetClient();
        if (imageStream.CanSeek)
        {
            imageStream.Position = 0;
        }

        var binaryData = BinaryData.FromStream(imageStream);
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-receipt",
            binaryData,
            cancellationToken: cancellationToken);

        var result = operation.Value;
        var parsed = new ParsedReceipt();

        _logger.LogInformation("Document Intelligence analysis completed. Content length: {ContentLen}, Documents: {DocCount}, Pages: {PageCount}, Tables: {TableCount}",
            result.Content?.Length ?? 0,
            result.Documents?.Count ?? 0,
            result.Pages?.Count ?? 0,
            result.Tables?.Count ?? 0);

        if (!string.IsNullOrEmpty(result.Content))
        {
            parsed.RawTextSnippet = result.Content.Length > 300 ? result.Content.Substring(0, 300) : result.Content;
        }

        // STEP 1: Attempt structured extraction from AnalyzedDocuments
        bool extractedFromDocument = false;
        if (result.Documents != null && result.Documents.Count > 0)
        {
            var doc = result.Documents[0];
            var fields = doc.Fields;
            _logger.LogInformation("Found document with type: {DocType}, confidence: {Confidence}. Fields present: {FieldKeys}",
                doc.DocumentType, doc.Confidence, string.Join(", ", fields.Keys));

            // Merchant Name
            var merchantField = FindField(fields, "MerchantName", "VendorName", "Merchant", "StoreName", "Vendor", "SellerName", "Name");
            var extractedMerchant = ExtractString(merchantField);
            if (!string.IsNullOrWhiteSpace(extractedMerchant))
            {
                parsed.MerchantName = CleanMerchantName(extractedMerchant);
            }

            // Transaction Date
            var dateField = FindField(fields, "TransactionDate", "Date", "InvoiceDate", "PurchaseDate");
            if (dateField != null)
            {
                if (dateField.ValueDate.HasValue)
                {
                    parsed.TransactionDate = dateField.ValueDate.Value.DateTime;
                }
                else
                {
                    parsed.TransactionDate = ParseDateFromText(dateField.Content);
                }
            }

            // Subtotal
            var subtotalField = FindField(fields, "Subtotal", "NetTotal", "SubTotal", "TotalBeforeTax");
            parsed.Subtotal = ExtractDecimal(subtotalField);

            // Tax
            var taxField = FindField(fields, "TotalTax", "Tax", "VAT", "VatAmount", "TaxAmount");
            parsed.Tax = ExtractDecimal(taxField);

            // Total
            var totalField = FindField(fields, "Total", "InvoiceTotal", "GrandTotal", "TotalAmount", "AmountDue", "BalanceDue", "TotalToPay");
            parsed.Total = ExtractDecimal(totalField);

            // Items
            var itemsField = FindField(fields, "Items", "PurchasedItems", "LineItems");
            if (itemsField != null && itemsField.ValueList != null)
            {
                foreach (var itemField in itemsField.ValueList)
                {
                    var itemDict = itemField.ValueDictionary;
                    var item = new ParsedReceiptItem();

                    if (itemDict != null)
                    {
                        var descField = FindField(itemDict, "Description", "Name", "Item", "Product", "Title", "Text");
                        item.Description = ExtractString(descField) ?? string.Empty;

                        var qtyField = FindField(itemDict, "Quantity", "Qty", "Count");
                        item.Quantity = ExtractDecimal(qtyField) ?? 1.0m;

                        var unitPriceField = FindField(itemDict, "Price", "UnitPrice", "Rate");
                        item.UnitPrice = ExtractDecimal(unitPriceField);

                        var totalPriceField = FindField(itemDict, "TotalPrice", "Price", "Amount", "Total", "LineTotal");
                        item.TotalPrice = ExtractDecimal(totalPriceField) ?? 0m;

                        if (item.TotalPrice == 0m && item.UnitPrice.HasValue)
                        {
                            item.TotalPrice = item.UnitPrice.Value * item.Quantity;
                        }
                    }

                    // Fallback to itemField.Content if subfields didn't yield description/price
                    if (string.IsNullOrWhiteSpace(item.Description) && !string.IsNullOrWhiteSpace(itemField.Content))
                    {
                        var parsedLineItem = ParseLineItemText(itemField.Content);
                        if (parsedLineItem != null)
                        {
                            item.Description = parsedLineItem.Description;
                            item.Quantity = parsedLineItem.Quantity;
                            item.TotalPrice = parsedLineItem.TotalPrice;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(item.Description) || item.TotalPrice > 0)
                    {
                        parsed.Items.Add(item);
                    }
                }
            }

            if (parsed.MerchantName != "Unknown Merchant" || parsed.Total.HasValue || parsed.Items.Count > 0)
            {
                extractedFromDocument = true;
            }
        }
        else
        {
            _logger.LogWarning("No structured AnalyzedDocuments recognized by prebuilt-receipt model. Falling back to OCR line parsing...");
        }

        // STEP 2: Table Extraction Fallback for Items (if items still empty)
        if (parsed.Items.Count == 0 && result.Tables != null && result.Tables.Count > 0)
        {
            ExtractItemsFromTables(result.Tables, parsed);
        }

        // STEP 3: Fallback from OCR Lines and Raw Content (if Merchant, Date, Total, or Items are missing)
        var allLines = CollectAllLines(result);
        ExtractFromOcrFallback(allLines, parsed);

        var docKeys = (result.Documents != null && result.Documents.Count > 0)
            ? string.Join(",", result.Documents[0].Fields.Keys)
            : "None";

        var preview = allLines.Count > 0
            ? string.Join(" ; ", allLines.Take(10))
            : (!string.IsNullOrWhiteSpace(result.Content)
                ? (result.Content.Length > 150 ? result.Content.Substring(0, 150) : result.Content)
                : "No text");

        parsed.DiagnosticInfo = $"Mode: {(extractedFromDocument ? "Prebuilt" : "OCR")} | DocType: {(result.Documents?.Count > 0 ? result.Documents[0].DocumentType : "None")} | Fields: [{docKeys}] | Lines: {allLines.Count} | Sample: [{preview.Replace("\r", " ").Replace("\n", " ")}]";

        _logger.LogInformation("Receipt parse complete. Merchant: '{Merchant}', Date: {Date}, Total: {Total}, Items: {Count}. Diagnostics: {Diag}",
            parsed.MerchantName, parsed.TransactionDate, parsed.Total, parsed.Items.Count, parsed.DiagnosticInfo);

        return parsed;
    }

    private static List<string> CollectAllLines(AnalyzeResult result)
    {
        var lines = new List<string>();
        if (result.Pages != null)
        {
            foreach (var page in result.Pages)
            {
                if (page.Lines != null)
                {
                    foreach (var line in page.Lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line.Content))
                        {
                            lines.Add(line.Content.Trim());
                        }
                    }
                }
            }
        }

        if (lines.Count == 0 && !string.IsNullOrWhiteSpace(result.Content))
        {
            lines = result.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(l => l.Trim())
                                  .Where(l => !string.IsNullOrWhiteSpace(l))
                                  .ToList();
        }

        return lines;
    }

    private static void ExtractFromOcrFallback(List<string> lines, ParsedReceipt parsed)
    {
        // Merchant Name Fallback
        if (parsed.MerchantName == "Unknown Merchant" && lines.Count > 0)
        {
            foreach (var line in lines.Take(8))
            {
                var cleaned = CleanMerchantName(line);
                if (IsLikelyMerchantName(cleaned))
                {
                    parsed.MerchantName = cleaned;
                    break;
                }
            }
        }

        // Transaction Date Fallback
        if (!parsed.TransactionDate.HasValue)
        {
            foreach (var line in lines)
            {
                var parsedDate = ParseDateFromText(line);
                if (parsedDate.HasValue)
                {
                    parsed.TransactionDate = parsedDate.Value;
                    break;
                }
            }
        }

        // Total Fallback
        if (!parsed.Total.HasValue)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (TotalKeywordRegex.IsMatch(line))
                {
                    var price = ParseDecimalFromText(line);
                    if (!price.HasValue && i + 1 < lines.Count)
                    {
                        price = ParseDecimalFromText(lines[i + 1]);
                    }
                    if (price.HasValue && price.Value > 0)
                    {
                        parsed.Total = price.Value;
                    }
                }
            }
        }

        // Items Fallback (if still 0 items)
        if (parsed.Items.Count == 0 && lines.Count > 0)
        {
            int i = 0;
            while (i < lines.Count)
            {
                var line = lines[i];
                if (SummaryLineRegex.IsMatch(line) || HeaderLineRegex.IsMatch(line))
                {
                    i++;
                    continue;
                }

                // Try single-line item: [Quantity] [Description] [Price] [Optional VAT code]
                var item = ParseLineItemText(line);
                if (item != null && item.TotalPrice > 0 && !string.IsNullOrWhiteSpace(item.Description))
                {
                    if (parsed.Total.HasValue && item.TotalPrice == parsed.Total.Value && item.Description.Length < 3)
                    {
                        i++;
                        continue;
                    }

                    parsed.Items.Add(item);
                    i++;
                    continue;
                }

                // Try two-line item: lines[i] is Description, lines[i+1] is Price
                if (i + 1 < lines.Count && line.Length >= 2 && !PriceOnlyRegex.IsMatch(line) && !SummaryLineRegex.IsMatch(line))
                {
                    var nextLine = lines[i + 1];
                    if (PriceOnlyRegex.IsMatch(nextLine) && !SummaryLineRegex.IsMatch(nextLine))
                    {
                        var twoLinePrice = ParseDecimalFromText(nextLine);
                        if (twoLinePrice.HasValue && twoLinePrice.Value > 0)
                        {
                            parsed.Items.Add(new ParsedReceiptItem
                            {
                                Description = line,
                                Quantity = 1.0m,
                                TotalPrice = twoLinePrice.Value,
                                UnitPrice = twoLinePrice.Value
                            });
                            i += 2;
                            continue;
                        }
                    }
                }

                i++;
            }
        }
    }

    private static void ExtractItemsFromTables(IReadOnlyList<DocumentTable> tables, ParsedReceipt parsed)
    {
        foreach (var table in tables)
        {
            var rows = table.Cells.GroupBy(c => c.RowIndex).OrderBy(g => g.Key).ToList();
            if (rows.Count <= 1) continue;

            foreach (var rowGroup in rows.Skip(1))
            {
                var rowCells = rowGroup.OrderBy(c => c.ColumnIndex).ToList();
                string description = string.Empty;
                decimal? price = null;
                decimal qty = 1.0m;

                foreach (var cell in rowCells)
                {
                    var text = cell.Content?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var cellPrice = ParseDecimalFromText(text);
                    if (cellPrice.HasValue && cellPrice.Value > 0 && text.Contains('.'))
                    {
                        price = cellPrice.Value;
                    }
                    else if (int.TryParse(text, out var intQty) && intQty > 0 && intQty < 100)
                    {
                        qty = intQty;
                    }
                    else if (!SummaryLineRegex.IsMatch(text) && text.Length > 2)
                    {
                        description = text;
                    }
                }

                if (!string.IsNullOrWhiteSpace(description) && price.HasValue)
                {
                    parsed.Items.Add(new ParsedReceiptItem
                    {
                        Description = description,
                        Quantity = qty,
                        TotalPrice = price.Value,
                        UnitPrice = qty > 1 ? Math.Round(price.Value / qty, 2) : price.Value
                    });
                }
            }
        }
    }

    private static DocumentField? FindField(IReadOnlyDictionary<string, DocumentField> fields, params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            if (fields.TryGetValue(name, out var field))
            {
                return field;
            }

            var match = fields.FirstOrDefault(kvp => string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase));
            if (match.Value != null)
            {
                return match.Value;
            }
        }
        return null;
    }

    private static string? ExtractString(DocumentField? field)
    {
        if (field == null) return null;
        if (!string.IsNullOrWhiteSpace(field.ValueString)) return field.ValueString.Trim();
        if (!string.IsNullOrWhiteSpace(field.Content)) return field.Content.Trim();
        return null;
    }

    private static decimal? ExtractDecimal(DocumentField? field)
    {
        if (field == null) return null;
        if (field.ValueCurrency != null) return (decimal)field.ValueCurrency.Amount;
        if (field.ValueDouble.HasValue) return (decimal)field.ValueDouble.Value;
        if (field.ValueInt64.HasValue) return (decimal)field.ValueInt64.Value;
        if (!string.IsNullOrWhiteSpace(field.Content))
        {
            return ParseDecimalFromText(field.Content);
        }
        return null;
    }

    public static decimal? ParseDecimalFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = PricePattern.Match(text);
        if (!match.Success) return null;

        var raw = match.Value;
        if (raw.Contains(',') && raw.Contains('.'))
        {
            raw = raw.Replace(",", "");
        }
        else if (raw.Contains(',') && raw.Length - raw.LastIndexOf(',') - 1 == 2)
        {
            raw = raw.Replace(',', '.');
        }

        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    public static DateTime? ParseDateFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var directDate))
        {
            return directDate;
        }

        var match = DatePattern.Match(text);
        if (match.Success && DateTime.TryParse(match.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return parsedDate;
        }

        return null;
    }

    private static bool IsLikelyMerchantName(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 60) return false;
        if (TimePattern.IsMatch(trimmed)) return false;
        if (PhoneUiPattern.IsMatch(trimmed)) return false;
        if (HeaderLineRegex.IsMatch(trimmed)) return false;
        if (DatePattern.IsMatch(trimmed)) return false;
        if (Regex.IsMatch(trimmed, @"^[$£€¥]?\s*[0-9.,\s]+$")) return false;
        return true;
    }

    private static string CleanMerchantName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown Merchant";
        var cleaned = Regex.Replace(name.Trim(), @"(?i)^(welcome to\s+|thank you for visiting\s+|thank you for shopping at\s+|customer receipt\s*[-:]?\s*|receipt\s*[-:]?\s*)", "");
        cleaned = cleaned.Trim().Trim(',', '.', ':', ';', '-', '_', '#');
        return cleaned.Length >= 2 ? cleaned : "Unknown Merchant";
    }

    private static ParsedReceiptItem? ParseLineItemText(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var match = ItemLineRegex.Match(line.Trim());
        if (!match.Success) return null;

        var qtyStr = match.Groups[1].Value;
        var descStr = match.Groups[2].Value.Trim();
        var priceStr = match.Groups[3].Value;

        if (string.IsNullOrWhiteSpace(descStr) || descStr.Length < 2) return null;

        decimal qty = 1.0m;
        if (!string.IsNullOrWhiteSpace(qtyStr) && decimal.TryParse(qtyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedQty))
        {
            qty = parsedQty;
        }

        var totalPrice = ParseDecimalFromText(priceStr) ?? 0m;
        if (totalPrice <= 0m) return null;

        return new ParsedReceiptItem
        {
            Description = descStr,
            Quantity = qty,
            TotalPrice = totalPrice,
            UnitPrice = qty > 1 ? Math.Round(totalPrice / qty, 2) : totalPrice
        };
    }
}
