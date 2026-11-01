# ReceiptTracker.Functions (.NET C# Azure Functions)

Isolated Worker Azure Functions microservice that ingests receipt images (via iOS Shortcuts or HTTP POST), analyzes line items using **Azure AI Document Intelligence (`prebuilt-receipt`)**, and persists headers and items to **Microsoft Dataverse**.

---

## 1. Project Structure

```
ReceiptTracker.sln
src/ReceiptTracker.Functions/
├── Functions/
│   └── UploadReceiptFunction.cs        # HTTP POST /api/receipts/upload
├── Services/
│   ├── IDocumentIntelligenceService.cs  # Azure AI Document Intelligence wrapper
│   ├── DocumentIntelligenceService.cs   # Calls 'prebuilt-receipt' model
│   ├── IDataverseService.cs             # Dataverse Web API interface
│   └── DataverseService.cs              # OData v9.2 client with Entra ID bearer auth
├── Models/
│   ├── ReceiptUploadRequest.cs          # Incoming base64 payload DTO
│   ├── ReceiptUploadResponse.cs         # iOS Shortcut response DTO
│   └── ParsedReceipt.cs                 # Normalized receipt and item models
├── Program.cs                           # Dependency injection & host configuration
├── host.json                            # Function host configuration
└── local.settings.json                  # Local secrets and configuration
```

---

## 2. Configuration Settings (`local.settings.json`)

Configure your Azure Cognitive Services and Microsoft Dataverse credentials:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "DocumentIntelligence:Endpoint": "https://<your-doc-intelligence-resource>.cognitiveservices.azure.com/",
    "DocumentIntelligence:ApiKey": "<your-azure-document-intelligence-api-key>",
    "Dataverse:EnvironmentUrl": "https://<your-org>.crm.dynamics.com",
    "Dataverse:TenantId": "<your-entra-id-tenant-id>",
    "Dataverse:ClientId": "<your-app-registration-client-id>",
    "Dataverse:ClientSecret": "<your-app-registration-client-secret>"
  }
}
```

---

## 3. Local Execution & Testing

### 3.1 Run Locally
```bash
cd "src/ReceiptTracker.Functions"
dotnet build
# With Azure Functions Core Tools installed:
func start
```

### 3.2 Test Upload with `curl`
Convert any receipt photo to base64 and test the endpoint:

```bash
# Generate base64 string
BASE64_IMAGE=$(base64 -i /path/to/test_receipt.jpg | tr -d '\n')

# Send POST request
curl -X POST http://localhost:7071/api/receipts/upload \
  -H "Content-Type: application/json" \
  -d "{\"imageBase64\": \"$BASE64_IMAGE\", \"fileName\": \"receipt.jpg\"}"
```

**Expected Response**:
```json
{
  "success": true,
  "receiptId": "b1b7470c-...",
  "storeName": "Trader Joe's",
  "purchaseDate": "2026-10-30T00:00:00",
  "totalAmount": 42.18,
  "itemCount": 5,
  "items": [
    {
      "rawName": "TJ OAT BRSTA 32OZ",
      "quantity": 2,
      "unitPrice": 3.99,
      "totalPrice": 7.98,
      "matchedProductId": "e4a21008-...",
      "matchedProductName": null
    }
  ],
  "message": "Processed receipt from Trader Joe's with 5 items totaling $42.18."
}
```

---

## 4. Deploying to Azure

### Using Azure CLI
```bash
# 1. Create Resource Group & Storage Account
az group create --name rg-receipt-tracker --location eastus
az storage account create --name streceipttracker --resource-group rg-receipt-tracker --sku Standard_LRS

# 2. Create Function App (Consumption Plan, .NET 7/8 isolated)
az functionapp create \
  --resource-group rg-receipt-tracker \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --functions-version 4 \
  --name func-receipt-tracker \
  --storage-account streceipttracker

# 3. Publish
dotnet publish -c Release
cd bin/Release/net7.0/publish
zip -r ../publish.zip .
az functionapp deployment source config-zip -g rg-receipt-tracker -n func-receipt-tracker --src ../publish.zip
```

