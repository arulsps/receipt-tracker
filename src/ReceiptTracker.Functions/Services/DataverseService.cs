using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ReceiptTracker.Functions.Services;

public class DataverseService : IDataverseService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DataverseService> _logger;
    private readonly TokenCredential _credential;
    private readonly string _environmentUrl;
    private readonly string _scope;

    public DataverseService(HttpClient httpClient, IConfiguration configuration, ILogger<DataverseService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _environmentUrl = (configuration["Dataverse:EnvironmentUrl"] ?? string.Empty).TrimEnd('/');
        var tenantId = configuration["Dataverse:TenantId"];
        var clientId = configuration["Dataverse:ClientId"];
        var clientSecret = configuration["Dataverse:ClientSecret"];

        if (string.IsNullOrEmpty(_environmentUrl) || string.IsNullOrEmpty(tenantId) ||
            string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogWarning("Dataverse configuration is incomplete. Function will run in mock/dry-run mode for Dataverse calls.");
            _credential = new DefaultAzureCredential();
            _scope = "https://default.crm.dynamics.com/.default";
            return;
        }

        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _scope = $"{_environmentUrl}/.default";
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string relativeUri, CancellationToken ct)
    {
        var tokenContext = new TokenRequestContext(new[] { _scope });
        var token = await _credential.GetTokenAsync(tokenContext, ct);

        var requestUri = $"{_environmentUrl}/api/data/v9.2/{relativeUri.TrimStart('/')}";
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("Prefer", "return=representation");

        return request;
    }

    public async Task<Guid> EnsureStoreAsync(string storeName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_environmentUrl))
        {
            return Guid.NewGuid();
        }

        var escapedName = Uri.EscapeDataString(storeName.Replace("'", "''"));
        var queryUri = $"cr_store1s?$filter=cr_merchantname eq '{escapedName}'&$select=cr_store1id,cr_merchantname";

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, queryUri, cancellationToken);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonNode.Parse(content);
            var values = json?["value"]?.AsArray();

            if (values != null && values.Count > 0)
            {
                var existingId = values[0]?["cr_store1id"]?.GetValue<string>();
                if (Guid.TryParse(existingId, out var parsedGuid))
                {
                    _logger.LogInformation("Found existing store: {StoreName} (ID: {StoreId})", storeName, parsedGuid);
                    return parsedGuid;
                }
            }
        }

        _logger.LogInformation("Store '{StoreName}' not found. Creating new store in Dataverse...", storeName);
        var createPayload = new JsonObject
        {
            ["cr_merchantname"] = storeName
        };

        var postRequest = await CreateAuthorizedRequestAsync(HttpMethod.Post, "cr_store1s", cancellationToken);
        postRequest.Content = new StringContent(createPayload.ToJsonString(), Encoding.UTF8, "application/json");

        var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken);
        postResponse.EnsureSuccessStatusCode();

        var createdJson = JsonNode.Parse(await postResponse.Content.ReadAsStringAsync(cancellationToken));
        var createdId = createdJson?["cr_store1id"]?.GetValue<string>()
            ?? ExtractIdFromODataHeader(postResponse);

        return Guid.Parse(createdId);
    }

    public async Task<(Guid? ProductId, string? ProductName)> FindProductAliasAsync(string rawOcrName, Guid storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_environmentUrl))
        {
            return (null, null);
        }

        var escapedName = Uri.EscapeDataString(rawOcrName.Replace("'", "''"));
        var queryUri = $"cr_productaliases?$filter=cr_rawocrname eq '{escapedName}'&$select=cr_productaliasid,_cr_product_value";

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, queryUri, cancellationToken);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonNode.Parse(content);
            var values = json?["value"]?.AsArray();

            if (values != null && values.Count > 0)
            {
                var productIdStr = values[0]?["_cr_product_value"]?.GetValue<string>();
                if (Guid.TryParse(productIdStr, out var productId))
                {
                    _logger.LogInformation("Resolved OCR item '{RawOcr}' to canonical product '{ProductId}' via alias cache", rawOcrName, productId);
                    return (productId, null);
                }
            }
        }

        return (null, null);
    }

    public async Task<Guid> CreateReceiptHeaderAsync(
        Guid storeId,
        DateTime? purchaseDate,
        decimal? subtotal,
        decimal? tax,
        decimal? totalAmount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_environmentUrl))
        {
            return Guid.NewGuid();
        }

        var payload = new JsonObject
        {
            ["cr_receiptnumber"] = $"REC-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            ["cr_StoreID@odata.bind"] = $"/cr_store1s({storeId})"
        };

        // Note: Additional custom columns (cr_purchasedate, cr_totalamount, etc.) are optional and will be sent if present
        if (purchaseDate.HasValue)
        {
            payload["cr_purchasedate"] = purchaseDate.Value.ToString("yyyy-MM-dd");
        }
        if (subtotal.HasValue)
        {
            payload["cr_subtotal"] = subtotal.Value;
        }
        if (tax.HasValue)
        {
            payload["cr_tax"] = tax.Value;
        }
        if (totalAmount.HasValue)
        {
            payload["cr_totalamount"] = totalAmount.Value;
        }

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, "cr_receipts", cancellationToken);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        
        // If optional columns are not yet added to the Dataverse table, retry with base columns
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Receipt creation with full schema returned {StatusCode}: {Error}. Retrying with core fields...", response.StatusCode, errorBody);

            var fallbackPayload = new JsonObject
            {
                ["cr_receiptnumber"] = $"REC-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                ["cr_StoreID@odata.bind"] = $"/cr_store1s({storeId})"
            };

            var fallbackRequest = await CreateAuthorizedRequestAsync(HttpMethod.Post, "cr_receipts", cancellationToken);
            fallbackRequest.Content = new StringContent(fallbackPayload.ToJsonString(), Encoding.UTF8, "application/json");
            response = await _httpClient.SendAsync(fallbackRequest, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        var createdJson = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var createdId = createdJson?["cr_receiptid"]?.GetValue<string>()
            ?? ExtractIdFromODataHeader(response);

        _logger.LogInformation("Created Dataverse cr_Receipt: {ReceiptId}", createdId);
        return Guid.Parse(createdId);
    }

    public async Task<Guid> CreateReceiptItemAsync(
        Guid receiptId,
        Guid? productId,
        string rawItemName,
        decimal quantity,
        decimal? unitPrice,
        decimal totalPrice,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_environmentUrl))
        {
            return Guid.NewGuid();
        }

        var payload = new JsonObject
        {
            ["cr_receiptitemname"] = rawItemName,
            ["cr_Receipt@odata.bind"] = $"/cr_receipts({receiptId})"
        };

        if (productId.HasValue)
        {
            payload["cr_Product@odata.bind"] = $"/cr_products({productId.Value})";
        }

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, "cr_receiptitems", cancellationToken);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var createdJson = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var createdId = createdJson?["cr_receiptitemid"]?.GetValue<string>()
            ?? ExtractIdFromODataHeader(response);

        return Guid.Parse(createdId);
    }

    private static string ExtractIdFromODataHeader(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdUrl = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(entityIdUrl))
            {
                var startIndex = entityIdUrl.LastIndexOf('(');
                var endIndex = entityIdUrl.LastIndexOf(')');
                if (startIndex != -1 && endIndex > startIndex)
                {
                    return entityIdUrl.Substring(startIndex + 1, endIndex - startIndex - 1);
                }
            }
        }

        throw new InvalidOperationException("Unable to extract entity ID from Dataverse response.");
    }
}
