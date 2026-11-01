namespace ReceiptTracker.Functions.Services;

public interface IDataverseService
{
    Task<Guid> EnsureStoreAsync(string storeName, CancellationToken cancellationToken = default);
    Task<(Guid? ProductId, string? ProductName)> FindProductAliasAsync(string rawOcrName, Guid storeId, CancellationToken cancellationToken = default);
    Task<Guid> CreateReceiptHeaderAsync(
        Guid storeId,
        DateTime? purchaseDate,
        decimal? subtotal,
        decimal? tax,
        decimal? totalAmount,
        CancellationToken cancellationToken = default);
    Task<Guid> CreateReceiptItemAsync(
        Guid receiptId,
        Guid? productId,
        string rawItemName,
        decimal quantity,
        decimal? unitPrice,
        decimal totalPrice,
        CancellationToken cancellationToken = default);
}

