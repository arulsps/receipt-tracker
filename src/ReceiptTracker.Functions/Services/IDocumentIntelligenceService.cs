using ReceiptTracker.Functions.Models;

namespace ReceiptTracker.Functions.Services;

public interface IDocumentIntelligenceService
{
    Task<ParsedReceipt> AnalyzeReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default);
}

