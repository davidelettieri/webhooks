using System.Text;
using Webhooks.Publishers;

namespace WorkerService.Publisher;

public class Worker(
    ILogger<Worker> logger,
    IWebhookPublisher webhookPublisher) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private readonly IWebhookPublisher _webhookPublisher = webhookPublisher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Thread.Sleep(5000); // Wait for the receiver to start.
        while (true)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }

                await _webhookPublisher.PublishAsync(new Uri("http://localhost:5033/webhooks/receive"),
                    Guid.NewGuid().ToString(), Encoding.UTF8.GetBytes($"{{\"id\":\"{Guid.NewGuid()}\"}}"),
                    cancellationToken: stoppingToken);

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}