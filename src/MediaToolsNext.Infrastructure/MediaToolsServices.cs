using MediaToolsNext.Core;
using Microsoft.Extensions.DependencyInjection;

namespace MediaToolsNext.Infrastructure;

public static class MediaToolsServices
{
    public static IServiceCollection AddMediaToolsNext(this IServiceCollection services, Func<IServiceProvider, string> databasePath)
    {
        services.AddSingleton<IExternalToolProbe, ExternalToolProbe>();
        services.AddSingleton<IFileDiscoverer, FileDiscoverer>();
        services.AddSingleton<IFileActionService, FileActionService>();
        services.AddSingleton<IMediaValidator, ImageValidator>();
        services.AddSingleton<IMediaValidator>(sp => new MediaStreamValidator(MediaCategory.Video, sp.GetRequiredService<IExternalToolProbe>()));
        services.AddSingleton<IMediaValidator>(sp => new MediaStreamValidator(MediaCategory.Audio, sp.GetRequiredService<IExternalToolProbe>()));
        services.AddSingleton<IMediaValidator, DocumentValidator>();
        services.AddSingleton<IScanStore>(sp => new SqliteScanStore(databasePath(sp)));
        services.AddSingleton<IScannerPipeline, ScannerPipeline>();
        return services;
    }
}

