using EventManager.Judge.Core;
using EventManager.Sync;
using Microsoft.Extensions.Logging;

namespace EventManager.Judge;

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

		// Composition root for the U5 app core. The on-device SQLite/SQLCipher IEventStore and the
		// concrete SignalR/WSS ISyncTransport are host seams; InMemoryEventStore is the default.
		// SpokeEventLog + ScoreCaptureService are built after pairing (they need the assigned device id).
		builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
		builder.Services.AddSingleton<IEventSerializer>(new JsonEventSerializer());
		builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(workerId: 2));
		builder.Services.AddSingleton<MatQueueViewModel>();
		builder.Services.AddSingleton<CrossMatViewModel>();
		builder.Services.AddSingleton<FocusModeState>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
