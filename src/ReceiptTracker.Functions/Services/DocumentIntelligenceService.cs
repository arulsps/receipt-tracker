using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ReceiptTracker.Functions.Models;

namespace ReceiptTracker.Functions.Services;

public class DocumentIntelligenceService : IDocumentIntelligenceService
{
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

            var endpoint = _configuration["DocumentIntelligence:Endpoint"]
                ?? _configuration["DocumentIntelligence__Endpoint"];
            var apiKey = _configuration["DocumentIntelligence:ApiKey"]
                ?? _configuration["DocumentIntelligence__ApiKey"];

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
        _logger.LogInformation("Sending receipt stream to Azure AI Document Intelligence (prebuilt-receipt)...");

        var client = GetClient();
        var binaryData = await BinaryData.FromStreamAsync(imageStream, cancellationToken);
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-receipt",
            binaryData,
            cancellationToken: cancellationToken);

        var result = operation.Value;
        var parsed = new ParsedReceipt();

        if (result.Documents == null || result.Documents.Count == 0)
        {
            _logger.LogWarning("No receipt documents recognized in the uploaded image.");
            return parsed;
        }

        var doc = result.Documents[0];
        var fields = doc.Fields;

        if (fields.TryGetValue("MerchantName", out var merchantField))
        {
            parsed.MerchantName = merchantField.ValueString ?? "Unknown Merchant";
        }

        if (fields.TryGetValue("TransactionDate", out var dateField) && dateField.ValueDate.HasValue)
        {
            parsed.TransactionDate = dateField.ValueDate.Value.DateTime;
        }

        if (fields.TryGetValue("Subtotal", out var subtotalField))
        {
            parsed.Subtotal = ExtractDecimal(subtotalField);
        }

        if (fields.TryGetValue("TotalTax", out var taxField))
        {
            parsed.Tax = ExtractDecimal(taxField);
        }

        if (fields.TryGetValue("Total", out var totalField))
        {
            parsed.Total = ExtractDecimal(totalField);
        }

        if (fields.TryGetValue("Items", out var itemsField) && itemsField.ValueList != null)
        {
            foreach (var itemField in itemsField.ValueList)
            {
                var itemDict = itemField.ValueDictionary;
                if (itemDict == null) continue;

                var item = new ParsedReceiptItem();

                if (itemDict.TryGetValue("Description", out var descField))
                {
                    item.Description = descField.ValueString ?? string.Empty;
                }

                if (itemDict.TryGetValue("Quantity", out var qtyField))
                {
                    item.Quantity = ExtractDecimal(qtyField) ?? 1.0m;
                }

                if (itemDict.TryGetValue("Price", out var unitPriceField))
                {
                    item.UnitPrice = ExtractDecimal(unitPriceField);
                }

                if (itemDict.TryGetValue("TotalPrice", out var totalPriceField))
                {
                    item.TotalPrice = ExtractDecimal(totalPriceField) ?? 0m;
                }
                else if (item.UnitPrice.HasValue)
                {
                    item.TotalPrice = item.UnitPrice.Value * item.Quantity;
                }

                if (!string.IsNullOrWhiteSpace(item.Description) || item.TotalPrice > 0)
                {
                    parsed.Items.Add(item);
                }
            }
        }

        _logger.LogInformation("Receipt parsed successfully. Merchant: {Merchant}, Date: {Date}, Total: {Total}, Items: {Count}",
            parsed.MerchantName, parsed.TransactionDate, parsed.Total, parsed.Items.Count);

        return parsed;
    }

    private static decimal? ExtractDecimal(DocumentField? field)
    {
        if (field == null) return null;
        if (field.ValueCurrency != null) return (decimal)field.ValueCurrency.Amount;
        if (field.ValueDouble.HasValue) return (decimal)field.ValueDouble.Value;
        if (field.ValueInt64.HasValue) return (decimal)field.ValueInt64.Value;
        return null;
    }
}
