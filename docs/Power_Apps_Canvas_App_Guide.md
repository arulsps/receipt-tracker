# Step-by-Step Guide: Power Apps Mobile Review App ("Receipt Tracker")

This guide walks you through creating the **Power Apps Mobile Review Screen** (Phone Layout) on [make.powerapps.com](https://make.powerapps.com). 

With this app, whenever you snap a receipt on your iPhone:
1. You see the receipt appear in your mobile queue.
2. You can inspect all parsed line items (e.g., `BEETROOT`, `CURRY LEAVES`, `PONNI RAW RICE`).
3. You can link any raw item to a canonical product (or create a new product on the fly).
4. Tapping **"Approve & Learn Aliases"** saves the mapping into Dataverse, so **all future receipts from this store will match automatically with zero clicks**.

---

## 1. Create the Canvas App (Phone Layout)

1. Open **[make.powerapps.com](https://make.powerapps.com)**.
2. In the top-right header, ensure your developer environment is selected (matches URL `org5dd5bd11`).
3. On the left navigation, click **Create** $\rightarrow$ select **Blank app**.
4. Under **Blank canvas app**, click **Create**:
   - **App name**: `Receipt Tracker`
   - **Format**: **Phone**
   - Click **Create**.

---

## 2. Connect Your Dataverse Tables

1. In the left toolbar of Power Apps Studio, click the **Data** icon (cylinder icon).
2. Click **+ Add data** and search for each of your Dataverse tables, clicking to add them:
   - **`Receipts`** (`cr_receipts`)
   - **`Receipt Items`** (`cr_receiptitems`)
   - **`Stores`** (`cr_store1s`)
   - **`Products`** (`cr_products`)
   - **`Product Aliases`** (`cr_productaliases`)

---

## 3. Screen 1: Receipts List (`scr_ReceiptsList`)

1. In the Tree view, rename `Screen1` to **`scr_ReceiptsList`**.
2. **Add a Header**:
   - Insert a **Rectangle**: Width = `Parent.Width`, Height = `65`, Fill = `RGBA(24, 76, 120, 1)`.
   - Insert a **Label**: Text = `"Receipt Tracker"`, Font size = `20`, Font weight = `Bold`, Color = `White`, X = `20`, Y = `15`.
   - Insert a **Reload/Refresh Icon**: X = `Parent.Width - 50`, Y = `18`, Color = `White`.
     - `OnSelect` property:
       ```powerfx
       Refresh(Receipts); Refresh('Receipt Items'); Refresh(Stores);
       ```
3. **Add the Receipts Gallery**:
   - Insert a **Blank vertical gallery** (`gal_Receipts`).
   - Position: X = `10`, Y = `75`, Width = `Parent.Width - 20`, Height = `Parent.Height - 85`.
   - Set `Items` property:
     ```powerfx
     Sort(Receipts, createdon, SortOrder.Descending)
     ```
4. **Design the Gallery Template Card**:
   - Set `TemplateSize`: `110`.
   - Inside the gallery template, insert:
     - **Card Background** (Rectangle): Fill = `RGBA(245, 247, 250, 1)`, BorderColor = `RGBA(220, 224, 230, 1)`, BorderThickness = `1`, Radius = `8`.
     - **Store Name Label**:
       - `Text` =
         ```powerfx
         Coalesce(LookUp(Stores, cr_store1id = ThisItem.cr_StoreID.cr_store1id, cr_merchantname), "Store Purchase")
         ```
       - Font size = `16`, Weight = `Bold`, Color = `RGBA(30, 41, 59, 1)`.
     - **Receipt Number & Date Label**:
       - `Text` =
         ```powerfx
         ThisItem.cr_receiptnumber & " • " & Text(ThisItem.createdon, "dd mmm yyyy, hh:mm")
         ```
       - Font size = `12`, Color = `RGBA(100, 116, 139, 1)`.
     - **Chevron / Right Arrow Icon**:
       - X = `Parent.TemplateWidth - 40`, Y = `40`.
       - `OnSelect` property:
         ```powerfx
         Set(gblSelectedReceipt, ThisItem);
         Navigate(scr_ReceiptDetail, ScreenTransition.Cover);
         ```

---

## 4. Screen 2: Line Items Review & Approval (`scr_ReceiptDetail`)

1. Click **+ New screen** $\rightarrow$ select **Blank screen**. Rename it to **`scr_ReceiptDetail`**.
2. **Add Header with Back Button**:
   - Insert a **Rectangle**: Width = `Parent.Width`, Height = `65`, Fill = `RGBA(24, 76, 120, 1)`.
   - Insert a **Back Arrow Icon**: X = `15`, Y = `18`, Color = `White`.
     - `OnSelect` property:
       ```powerfx
       Back(ScreenTransition.Uncover)
       ```
   - Insert a **Label**: Text = `"Review Receipt"`, Font size = `20`, Font weight = `Bold`, Color = `White`, X = `55`, Y = `15`.
3. **Add Summary Header Card**:
   - Insert a container or rectangle at Y = `75`, Height = `80`:
     - **Store Name Label**:
       ```powerfx
       Coalesce(LookUp(Stores, cr_store1id = gblSelectedReceipt.cr_StoreID.cr_store1id, cr_merchantname), "Store")
       ```
       Font size = `18`, Weight = `Bold`.
     - **Receipt Info Label**:
       ```powerfx
       gblSelectedReceipt.cr_receiptnumber & " (" & CountRows(Filter('Receipt Items', cr_Receipt.cr_receiptid = gblSelectedReceipt.cr_receiptid)) & " items)"
       ```
4. **Add the Line Items Gallery (`gal_Items`)**:
   - Insert a **Blank vertical gallery** (`gal_Items`):
     - Position: X = `10`, Y = `165`, Width = `Parent.Width - 20`, Height = `Parent.Height - 275`.
     - `Items` property:
       ```powerfx
       Filter('Receipt Items', cr_Receipt.cr_receiptid = gblSelectedReceipt.cr_receiptid)
       ```
     - Set `TemplateSize`: `95`.
5. **Inside Each Item Row**:
   - **Raw Item Name Label**:
     - `Text` = `ThisItem.cr_receiptitemname`
     - Font size = `14`, Weight = `SemiBold`.
   - **Canonical Product Combo Box (`cmb_Product`)**:
     - Insert a **Combo box** under the label.
     - `Items` = `Products`
     - `IsSearchable` = `true`
     - In the Properties pane on the right:
       - **Primary text**: `cr_productname`
       - **Search field**: `cr_productname`
     - `DefaultSelectedItems` =
       ```powerfx
       Filter(Products, cr_productid = ThisItem.cr_Product.cr_productid)
       ```
6. **Add "Approve & Learn Aliases" Button**:
   - Insert a **Button** at the bottom:
     - Position: X = `20`, Y = `Parent.Height - 85`, Width = `Parent.Width - 40`, Height = `50`.
     - `Text` = `"Approve & Learn Aliases"`
     - `Fill` = `RGBA(16, 185, 129, 1)` (Emerald Green).
     - `OnSelect` property:
       ```powerfx
       // 1. Loop through all items that have a product selected
       ForAll(
           Filter(gal_Items.AllItems, !IsBlank(cmb_Product.Selected)),
           // A. Save/learn new alias
           Patch(
               'Product Aliases',
               Defaults('Product Aliases'),
               {
                   cr_productaliasname: ThisRecord.cr_receiptitemname & " -> " & cmb_Product.Selected.cr_productname,
                   cr_rawocrname: ThisRecord.cr_receiptitemname,
                   cr_Product: LookUp(Products, cr_productid = cmb_Product.Selected.cr_productid),
                   cr_Store1: LookUp(Stores, cr_store1id = gblSelectedReceipt.cr_StoreID.cr_store1id)
               }
           );
           // B. Link product directly to receipt item
           Patch(
               'Receipt Items',
               ThisRecord,
               {
                   cr_Product: LookUp(Products, cr_productid = cmb_Product.Selected.cr_productid)
               }
           )
       );

       // 2. Notify and return
       Notify("Receipt approved and aliases learned for future scans!", NotificationType.Success);
       Navigate(scr_ReceiptsList, ScreenTransition.Uncover);
       ```

---

## 5. Screen 3: Habit Frequency & Cadence Watchlist (`scr_Habits`)

1. Click **+ New screen** $\rightarrow$ select **Blank screen**. Rename to **`scr_Habits`**.
2. **Add Header**:
   - Rectangle header: Title = `"Purchase Cadence Watchlist"`.
3. **Add Gallery of Tracked Products (`gal_Habits`)**:
   - Insert a gallery with `Items`:
     ```powerfx
     Filter(Products, cr_isrecurring = true)
     ```
   - Inside the template:
     - **Product Name**: `ThisItem.cr_productname`
     - **Purchase Count**:
       ```powerfx
       "Purchased " & CountRows(Filter('Receipt Items', cr_Product.cr_productid = ThisItem.cr_productid)) & " times"
       ```
     - **Last Bought**:
       ```powerfx
       "Last: " & Text(Max(Filter('Receipt Items', cr_Product.cr_productid = ThisItem.cr_productid), createdon), "dd mmm yyyy")
       ```
     - **Days Since Last Purchase**:
       ```powerfx
       DateDiff(Max(Filter('Receipt Items', cr_Product.cr_productid = ThisItem.cr_productid), createdon), Today(), TimeUnit.Days) & " days ago"
       ```

---

## 6. Publish and Install on iPhone

1. In Power Apps Studio, click the **Save** icon (top right), then click **Publish**.
2. Click **Publish this version**.
3. On your **iPhone**:
   - Open the **Power Apps** mobile app (download from App Store if not already installed).
   - Sign in with your developer account.
   - Open **Receipt Tracker**.
   - (Optional) In your **Scan Receipt** iOS Shortcut, add Action 7: **Open App** $\rightarrow$ select **Power Apps** so taking a photo immediately jumps into this review screen!

