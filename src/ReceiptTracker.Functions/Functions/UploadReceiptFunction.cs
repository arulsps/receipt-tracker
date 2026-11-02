using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ReceiptTracker.Functions.Models;
using ReceiptTracker.Functions.Services;

namespace ReceiptTracker.Functions.Functions;

public class UploadReceiptFunction
{
    private readonly IDocumentIntelligenceService _docAiService;
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<UploadReceiptFunction> _logger;

    public UploadReceiptFunction(
        IDocumentIntelligenceService docAiService,
        IDataverseService dataverseService,
        ILogger<UploadReceiptFunction> logger)
    {
        _docAiService = docAiService;
        _dataverseService = dataverseService;
        _logger = logger;
    }

    [Function("UploadReceipt")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "receipts/upload")] HttpRequestData req,
        FunctionContext executionContext)
    {
        _logger.LogInformation("Received receipt upload request.");

        try
        {
            using var reader = new StreamReader(req.Body);
            var requestBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Request body cannot be empty.");
            }

            var uploadRequest = JsonSerializer.Deserialize<ReceiptUploadRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (uploadRequest == null || string.IsNullOrWhiteSpace(uploadRequest.ImageBase64))
            {
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Invalid payload. 'imageBase64' field is required.");
            }

            byte[] imageBytes;
            try
            {
                // Handle potential data URL prefix: "data:image/jpeg;base64,..."
                var cleanBase64 = uploadRequest.ImageBase64;
                var commaIndex = cleanBase64.IndexOf(',');
                if (commaIndex >= 0)
                {
                    cleanBase64 = cleanBase64.Substring(commaIndex + 1);
                }

                imageBytes = Convert.FromBase64String(cleanBase64);
            }
            catch (FormatException)
            {
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Invalid base64 string provided.");
            }

            _logger.LogInformation("Receipt upload payload: ImageBytes length = {Length} bytes, FileName = {FileName}", imageBytes.Length, uploadRequest.FileName);

            if (imageBytes.Length < 100)
            {
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, $"Uploaded image is too small ({imageBytes.Length} bytes). Please verify the iOS shortcut image variable.");
            }

            using var imageStream = new MemoryStream(imageBytes);

            // 1. Analyze receipt via Azure AI Document Intelligence
            var parsed = await _docAiService.AnalyzeReceiptAsync(imageStream, executionContext.CancellationToken);

            // 2. Resolve/Create Store in Dataverse
            var storeId = await _dataverseService.EnsureStoreAsync(parsed.MerchantName, executionContext.CancellationToken);

            // 3. Create Receipt Header in Dataverse
            var receiptId = await _dataverseService.CreateReceiptHeaderAsync(
                storeId,
                parsed.TransactionDate,
                parsed.Subtotal,
                parsed.Tax,
                parsed.Total,
                executionContext.CancellationToken);

            // 4. Resolve Aliases and Create Line Items
            var itemSummaries = new List<ParsedReceiptItemSummary>();
            foreach (var item in parsed.Items)
            {
                var (matchedProductId, matchedProductName) = await _dataverseService.FindProductAliasAsync(
                    item.Description,
                    storeId,
                    executionContext.CancellationToken);

                await _dataverseService.CreateReceiptItemAsync(
                    receiptId,
                    matchedProductId,
                    item.Description,
                    item.Quantity,
                    item.UnitPrice,
                    item.TotalPrice,
                    executionContext.CancellationToken);

                itemSummaries.Add(new ParsedReceiptItemSummary
                {
                    RawName = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalPrice = item.TotalPrice,
                    MatchedProductId = matchedProductId,
                    MatchedProductName = matchedProductName
                });
            }

            var responseDto = new ReceiptUploadResponse
            {
                Success = true,
                ReceiptId = receiptId,
                StoreName = parsed.MerchantName,
                PurchaseDate = parsed.TransactionDate,
                TotalAmount = parsed.Total,
                ItemCount = itemSummaries.Count,
                Items = itemSummaries,
                DiagnosticInfo = parsed.DiagnosticInfo,
                Message = $"Processed receipt from {parsed.MerchantName} with {itemSummaries.Count} items totaling {(parsed.Total ?? 0):C2}."
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(responseDto, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing receipt upload: {Message}", ex.Message);
            return await CreateErrorResponseAsync(req, HttpStatusCode.InternalServerError, $"Error processing receipt: {ex.Message}");
        }
    }

    private static async Task<HttpResponseData> CreateErrorResponseAsync(HttpRequestData req, HttpStatusCode statusCode, string errorMessage)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var errorDto = new ReceiptUploadResponse
        {
            Success = false,
            Message = errorMessage
        };
        await response.WriteStringAsync(JsonSerializer.Serialize(errorDto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return response;
    }
}

