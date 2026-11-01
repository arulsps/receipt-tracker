# Step-by-Step Guide: iOS Shortcut ("Scan Receipt")

This guide walks you through creating the iPhone **"Scan Receipt"** Shortcut. With this shortcut, you can snap a receipt from your **Action Button**, **Back-Tap**, **Lock Screen**, or **Home Screen Widget**, and automatically parse and record it in Dataverse.

---

## 1. Create the Shortcut on iPhone

1. Open the built-in **Shortcuts** app on your iPhone.
2. Tap the **+** (plus icon) in the top-right corner to create a new shortcut.
3. Tap the shortcut title at the top, select **Rename**, and name it: `Scan Receipt`.
4. (Optional) Tap **Choose Icon** and pick the receipt/document icon with your favorite color.

---

## 2. Add the Actions (In Exact Order)

### Action 1: Take Photo
1. Tap **Add Action**.
2. Search for **Take Photo**.
3. Select **Take Photo**.
4. Tap the chevron/disclosure arrow on the action to configure:
   - **Camera**: `Back`
   - **Quality**: `Normal` or `High`

---

### Action 2: Resize Image
1. Search for **Resize Image**.
2. Select **Resize Image** (it will automatically link to `Photo`).
3. Tap the width field and set **Width** to: `1920`.
   *(Height will calculate automatically to maintain the aspect ratio while keeping payload size small).*

---

### Action 3: Base64 Encode
1. Search for **Base64 Encode**.
2. Select **Base64 Encode** (it links to `Resized Image`).
3. Tap the options arrow:
   - **Line Breaks**: `None`

---

### Action 4: Get Contents of URL (HTTP POST)
1. Search for **Get Contents of URL**.
2. Select **Get Contents of URL**.
3. Enter your Azure Function URL:
   ```
   https://<your-function-app-name>.azurewebsites.net/api/receipts/upload?code=<your-function-key>
   ```
4. Tap the arrow to expand HTTP settings:
   - **Method**: Change from `GET` to `POST`.
   - **Headers**:
     - Tap **+ Add new header**.
     - Key: `Content-Type`
     - Text: `application/json`
   - **Request Body**: Select `JSON`.
     - Tap **+ Add new field** $\rightarrow$ choose **Text**:
       - Key: `imageBase64`
       - Value: Tap the value box, then select the variable **Base64 Encoded** from above.
     - Tap **+ Add new field** $\rightarrow$ choose **Text**:
       - Key: `fileName`
       - Value: `receipt.jpg`

---

### Action 5: Get Dictionary Value
1. Search for **Get Dictionary Value**.
2. Select **Get Dictionary Value**.
3. Set **Key** to: `message`.
4. Ensure **in** is set to: `Contents of URL`.

---

### Action 6: Show Notification
1. Search for **Show Notification**.
2. Select **Show Notification**.
3. Set the notification message:
   - Tap the text field and select the variable **Dictionary Value** (the message returned from the Azure Function).
   - Expand the action arrow:
     - **Title**: `Receipt Tracker`
     - **Play Sound**: `On`

---

### Action 7 (Optional): Open Power Apps
1. Search for **Open App**.
2. Select **Power Apps** so you can immediately review the parsed line items.

---

## 3. How to Trigger with 1 Tap on iPhone

* **iPhone 15 Pro / 16 / 16 Pro Action Button**:
  - Go to **Settings** $\rightarrow$ **Action Button** $\rightarrow$ swipe to **Shortcut** $\rightarrow$ choose **Scan Receipt**.
* **Back Tap (Any iPhone 8 or later)**:
  - Go to **Settings** $\rightarrow$ **Accessibility** $\rightarrow$ **Touch** $\rightarrow$ **Back Tap** $\rightarrow$ choose **Double Tap** $\rightarrow$ select **Scan Receipt**.
* **Lock Screen / Home Screen Widget**:
  - Add a **Shortcuts** widget directly to your Lock Screen or Home Screen.

