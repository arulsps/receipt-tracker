# Dataverse Setup Guide: Receipt Tracker

This guide details the exact step-by-step procedure to set up the **Microsoft Dataverse** backend in your Power Apps Developer environment.

---

## Part 1: Create the Dataverse Solution

1. Sign in to the **Power Apps Maker Portal**: [https://make.powerapps.com](https://make.powerapps.com).
2. Ensure you have selected your **Developer Environment** in the environment switcher (top-right corner).
3. In the left navigation, click **Solutions** $\rightarrow$ **+ New solution**.
4. Fill in the solution details:
   - **Display name**: `Receipt Tracker`
   - **Name**: `ReceiptTracker`
   - **Publisher**: Select an existing publisher with prefix `cr` or create a new publisher:
     - *Publisher Display Name*: `Custom Receipts`
     - *Publisher Name*: `cr`
     - *Prefix*: `cr`
5. Click **Create**, then open the newly created **Receipt Tracker** solution.

---

## Part 2: Create the 6 Tables in Order

> [!IMPORTANT]
> Create tables in the exact order below so foreign-key lookup relationships can be established without circular dependency errors.

---

### Table 1: Store (`cr_Store`)
Stores the merchant/retailer details (e.g., Trader Joe's, Costco).

1. Click **+ New** $\rightarrow$ **Table** $\rightarrow$ **Table (advanced properties)**.
2. Enter:
   - **Display name**: `Store`
   - **Plural name**: `Stores`
   - **Schema name**: `cr_Store`
   - **Primary column display name**: `Store Name`
   - **Primary column schema name**: `cr_name`
3. Click **Save**.
4. Inside the table, add the following column:
   - **Display name**: `Default Category`
   - **Schema name**: `cr_defaultcategory`
   - **Data type**: `Single line of text`
   - **Requirement**: `Optional`
5. Click **Save Table**.

---

### Table 2: Category (`cr_Category`)
Stores product categories and habit replenishment cycles.

1. Click **+ New** $\rightarrow$ **Table** $\rightarrow$ **Table (advanced properties)**.
2. Enter:
   - **Display name**: `Category`
   - **Plural name**: `Categories`
   - **Schema name**: `cr_Category`
   - **Primary column display name**: `Category Name`
   - **Primary column schema name**: `cr_name`
3. Click **Save**.
4. Add the following custom columns:
   - **Column 1**:
     - **Display name**: `Is Essential`
     - **Schema name**: `cr_isessential`
     - **Data type**: `Choice` $\rightarrow$ `Yes/No` (Boolean)
     - **Default value**: `Yes`
   - **Column 2**:
     - **Display name**: `Target Frequency (Days)`
     - **Schema name**: `cr_targetfrequencydays`
     - **Data type**: `Whole Number`
     - **Format**: `None`
     - **Description**: Desired replenishment cycle (e.g., 14 for coffee, 21 for snacks).
5. Click **Save Table**.

---

### Table 3: Product (`cr_Product`)
Master de-duplicated catalog of items.

1. Click **+ New** $\rightarrow$ **Table** $\rightarrow$ **Table (advanced properties)**.
2. Enter:
   - **Display name**: `Product`
   - **Plural name**: `Products`
   - **Schema name**: `cr_Product`
   - **Primary column display name**: `Canonical Name`
   - **Primary column schema name**: `cr_canonicalname`
3. Click **Save**.
4. Add the following custom columns:
   - **Column 1 (Lookup to Category)**:
     - **Display name**: `Category`
     - **Schema name**: `cr_categoryid`
     - **Data type**: `Lookup` $\rightarrow$ Related table: `Category` (`cr_Category`)
   - **Column 2**:
     - **Display name**: `Unit of Measure`
     - **Schema name**: `cr_unitofmeasure`
     - **Data type**: `Single line of text` (or Choice: `Each`, `lb`, `Gallon`, `Pack`, `oz`)
   - **Column 3**:
     - **Display name**: `Is Recurring`
     - **Schema name**: `cr_isrecurring`
     - **Data type**: `Choice` $\rightarrow$ `Yes/No`
     - **Default value**: `Yes`
5. Click **Save Table**.

---

### Table 4: Product Alias (`cr_ProductAlias`)
Mapping cache that matches messy raw OCR strings to canonical products.

1. Click **+ New** $\rightarrow$ **Table** $\rightarrow$ **Table (advanced properties)**.
2. Enter:
   - **Display name**: `Product Alias`
   - **Plural name**: `Product Aliases`
   - **Schema name**: `cr_ProductAlias`
   - **Primary column display name**: `Raw OCR Name`
   - **Primary column schema name**: `cr_rawocrname`
3. Click **Save**.
4. Add the following custom columns:
   - **Column 1 (Lookup to Canonical Product)**:
     - **Display name**: `Canonical Product`
     - **Schema name**: `cr_productid`
     - **Data type**: `Lookup` $\rightarrow$ Related table: `Product` (`cr_Product`)
     - **Requirement**: `Required`
   - **Column 2 (Lookup to Store)**:
     - **Display name**: `Store`
     - **Schema name**: `cr_storeid`
     - **Data type**: `Lookup` $\rightarrow$ Related table: `Store` (`cr_Store`)
     - **Requirement**: `Optional`
5. Click **Save Table**.

---

### Table 5: Receipt (`cr_Receipt`)
Header record for each scanned transaction.

1. Click **+ New** $\rightarrow$ **Table** $\rightarrow$ **Table (advanced properties)**.
2. Enter:
   - **Display name**: `Receipt`
   - **Plural name**: `Receipts`
   - **Schema name**: `cr_Receipt`
   - **Primary column display name**: `Receipt Number`
   - **Primary column schema name**: `cr_receiptnumber`
     *(Can default to Autonumber or Text)*
3. Click **Save**.
4. Add the following custom columns:
   - **Column 1 (Lookup to Store)**:
     - **Display name**: `Store`
     - **Schema name**: `cr_storeid`
     - **Data type**: `Lookup` $\rightarrow$ Related table: `Store` (`cr_Store`)
   - **Column 2**:
     - **Display name**: `Purchase Date`
     - **Schema name**: `cr_purchasedate`
     - **Data type**: `Date and time` $\rightarrow$ Format: `Date only`
   - **Column 3**:
     - **Display name**: `Subtotal`
     - **Schema name**: `cr_subtotal`
     - **Data type**: `Currency`
   - **Column 4**:
     - **Display name**: `Tax`
     - **Schema name**: `cr_tax`
     - **Data type**: `Currency`
   - **Column 5**:
     - **Display name**: `Total Amount`
     - **Schema name**: `cr_totalamount`
     - **Data type**: `Currency`
     - **Requirement**: `Required`
   - **Column 6**:
     - **Display name**: `Status`
     - **Schema name**: `cr_status`
     - **Data type**: `Single line of text` (or Choice: `Pending Review`, `Confirmed`, `Archived`)
     - **Default value**: `Pending Review`
   - **Column 7 (Receipt Image)**:
     - **Display name**: `Receipt Image`
     - **Schema name**: `cr_receiptimage`
     - **Data type**: `File` $\rightarrow$ `Image`
5. Click **Save Table**.

---

### Table 6: Receipt Item (`cr_ReceiptItem`)
Line items parsed from the receipt.

1. Click **+ New** $\rightarrow$ **Table** $\rightarrow$ **Table (advanced properties)**.
2. Enter:
   - **Display name**: `Receipt Item`
   - **Plural name**: `Receipt Items`
   - **Schema name**: `cr_ReceiptItem`
   - **Primary column display name**: `Raw Item Name`
   - **Primary column schema name**: `cr_rawitemname`
3. Click **Save**.
4. Add the following custom columns:
   - **Column 1 (Lookup to Receipt - Parent)**:
     - **Display name**: `Receipt`
     - **Schema name**: `cr_receiptid`
     - **Data type**: `Lookup` $\rightarrow$ Related table: `Receipt` (`cr_Receipt`)
     - **Requirement**: `Required`
   - **Column 2 (Lookup to Product)**:
     - **Display name**: `Product`
     - **Schema name**: `cr_productid`
     - **Data type**: `Lookup` $\rightarrow$ Related table: `Product` (`cr_Product`)
     - **Requirement**: `Optional` (populates when alias matches or user assigns)
   - **Column 3**:
     - **Display name**: `Quantity`
     - **Schema name**: `cr_quantity`
     - **Data type**: `Decimal number` (Precision: `3 decimal places`)
     - **Default value**: `1.000`
   - **Column 4**:
     - **Display name**: `Unit Price`
     - **Schema name**: `cr_unitprice`
     - **Data type**: `Currency`
   - **Column 5**:
     - **Display name**: `Total Price`
     - **Schema name**: `cr_totalprice`
     - **Data type**: `Currency`
     - **Requirement**: `Required`
   - **Column 6**:
     - **Display name**: `Discount`
     - **Schema name**: `cr_discount`
     - **Data type**: `Currency`
5. **Configure Cascade Delete**:
   - Under `Receipt Item` $\rightarrow$ **Relationships**, find the N:1 relationship to `Receipt`.
   - Edit relationship $\rightarrow$ Expand **Advanced options**.
   - Set **Type of behavior**: `Parental` (deleting a receipt automatically deletes all its items).
6. Click **Save Table**.

---

## Part 3: Entra ID App Registration & Dataverse Application User

To allow the Azure Function to securely write to Dataverse without hardcoding your personal password:

### 3.1 Register Application in Microsoft Entra ID
1. Navigate to the **Azure Portal**: [https://portal.azure.com](https://portal.azure.com).
2. Go to **Microsoft Entra ID** $\rightarrow$ **App registrations** $\rightarrow$ **+ New registration**.
3. Enter:
   - **Name**: `ReceiptTracker-Backend`
   - **Supported account types**: `Accounts in this organizational directory only (Single tenant)`
   - Click **Register**.
4. On the **Overview** blade, note down:
   - **Application (client) ID**
   - **Directory (tenant) ID**
5. Go to **Certificates & secrets** $\rightarrow$ **+ New client secret**:
   - **Description**: `AzureFunctionKey`
   - **Expires**: `180 days` (or preferred duration)
   - Click **Add**.
   - **Copy the secret `Value` immediately** (it will be hidden after you leave the page).

---

### 3.2 Add Application User in Power Platform Admin Center
1. Go to the **Power Platform Admin Center**: [https://admin.powerplatform.microsoft.com](https://admin.powerplatform.microsoft.com).
2. Click **Environments** in the left sidebar $\rightarrow$ Select your **Developer Environment**.
3. In the environment details pane, click **Settings** (top bar).
4. Expand **Users + permissions** $\rightarrow$ Click **Application users**.
5. Click **+ New app user**.
6. Click **+ Add an app** $\rightarrow$ Select your registered app (`ReceiptTracker-Backend`) $\rightarrow$ Click **Add**.
7. Select your **Business unit** (default root business unit).
8. Under **Security roles**, click the pencil icon and assign:
   - **System Administrator** (standard for Developer/sandbox environments), OR
   - A custom security role with Create, Read, Write, and Append permissions on `cr_*` tables.
9. Click **Create**.

---

### 3.3 Find Your Dataverse Environment URL
1. In the Power Platform Admin Center, open your Developer Environment overview.
2. Copy the **Environment URL** (e.g., `https://orgXXXXXXXX.crm.dynamics.com`).

---

## Part 4: Configure `local.settings.json`

Open [`src/ReceiptTracker.Functions/local.settings.json`](file:///Users/arul/Documents/antigravity/Receipt%20Tracker/src/ReceiptTracker.Functions/local.settings.json) and enter your credentials:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "DocumentIntelligence:Endpoint": "https://<your-doc-ai-resource>.cognitiveservices.azure.com/",
    "DocumentIntelligence:ApiKey": "<your-doc-ai-key>",
    "Dataverse:EnvironmentUrl": "https://orgXXXXXXXX.crm.dynamics.com",
    "Dataverse:TenantId": "<your-tenant-id>",
    "Dataverse:ClientId": "<your-client-id>",
    "Dataverse:ClientSecret": "<your-client-secret-value>"
  }
}
```

Once saved, your Azure Function is fully configured to read and write directly to Dataverse!

