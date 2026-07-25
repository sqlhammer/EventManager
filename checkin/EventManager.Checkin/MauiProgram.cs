using EventManager.Checkin.Core;
using EventManager.Domain.Engines;
using EventManager.Sync;
using Microsoft.Extensions.Logging;

namespace EventManager.Checkin;

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

		// Composition root for the U6 app core. SpokeEventLog/CheckInService/WeighInService are built
		// after pairing (they need the assigned device id). On-device SQLite/SQLCipher + concrete
		// transport are host seams; InMemoryEventStore is the default.
		builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
		builder.Services.AddSingleton<IEventSerializer>(new JsonEventSerializer());
		builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(workerId: 3));
		builder.Services.AddSingleton<IWeighInPolicyEvaluator, WeighInPolicyEvaluator>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
