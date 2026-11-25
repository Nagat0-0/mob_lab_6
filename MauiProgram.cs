using lab_6.Pages;
using lab_6.Services;
using Microsoft.Extensions.Logging;

namespace lab_6;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<ArtApiService>();
        builder.Services.AddSingleton<GalleryDatabase>();
        builder.Services.AddSingleton<PlayPage>();
        builder.Services.AddSingleton<GalleryPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}