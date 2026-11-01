using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReceiptTracker.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient<IDataverseService, DataverseService>();
        services.AddSingleton<IDocumentIntelligenceService, DocumentIntelligenceService>();
    })
    .Build();

host.Run();
