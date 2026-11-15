using Microsoft.Extensions.Options;
using Modulix.Api.Observability;
using Modulix.Infrastructure.Email;

namespace Modulix.Api.Email;

/// <summary>
/// Kuyruktaki e-postaları düzenli aralıklarla gönderir (<see cref="EmailDispatcher"/>). Ayarlar eksikse göndermez;
/// e-postalar kuyrukta bekler ve ayarlar tamamlanıp uygulama yeniden başlatılınca gönderilir. Kuyruk (bekleyen sayısı,
/// en eski bekleyenin yaşı) her durumda dakikada bir ölçülür (<see cref="JobMetrics"/>); birikme tam da ayar eksikken olur.
/// </summary>
public sealed class EmailDispatchWorker(
    IServiceScopeFactory scopes,
    IOptions<EmailOptions> options,
    JobMetrics metrics,
    TimeProvider time,
    ILogger<EmailDispatchWorker> logger) : BackgroundService
{
    public const string JobName = "email.dispatch";

    private static readonly TimeSpan MeasureInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.WorkerEnabled)
        {
            return;
        }

        if (!settings.IsConfigured)
        {
            logger.LogWarning("E-posta ayarları eksik (Email:FromAddress, Email:Smtp:Host); e-postalar gönderilmeden kuyrukta bekleyecek.");
        }

        var interval = settings.IsConfigured ? TimeSpan.FromSeconds(Math.Max(1, settings.PollIntervalSeconds)) : MeasureInterval;
        DateTimeOffset? lastMeasured = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var attempted = 0;
            try
            {
                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<EmailDispatcher>();
                if (settings.IsConfigured)
                {
                    attempted = await dispatcher.DispatchPendingAsync(stoppingToken);
                }

                if (lastMeasured is null || time.GetUtcNow() - lastMeasured >= MeasureInterval)
                {
                    metrics.ObserveEmailQueue(await dispatcher.MeasureAsync(stoppingToken));
                    lastMeasured = time.GetUtcNow();
                }

                metrics.TurnCompleted(JobName);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Ör. veritabanına ulaşılamadı; bir sonraki turda tekrar denenir.
                metrics.TurnFailed(JobName);
                logger.LogError(exception, "E-posta kuyruğu işlenemedi.");
            }

            // Tam dolu bir tur daha fazla e-posta olduğunu gösterir; beklemeden devam edilir.
            if (attempted < settings.BatchSize)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
