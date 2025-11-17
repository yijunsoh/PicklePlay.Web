using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Helpers;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Services
{
    public class ScheduleAutoEndService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ScheduleAutoEndService> _logger;

        public ScheduleAutoEndService(
            IServiceProvider serviceProvider,
            ILogger<ScheduleAutoEndService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Schedule Auto-End Service started");

            // ⬇️ Run immediately on startup
            try
            {
                _logger.LogInformation("Running initial check for schedules to auto-end...");
                await CheckAndEndSchedules();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in initial auto-end check");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndEndSchedules();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Schedule Auto-End Service");
                }

                // Check every 30 minutes
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        private async Task CheckAndEndSchedules()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Get current Malaysia time
                var nowMalaysia = DateTimeHelper.GetMalaysiaTime();
                var nowUtc = DateTime.UtcNow;
                var autoEndThreshold = nowUtc.AddHours(-24); // 24 hours ago in UTC

                _logger.LogInformation($"=== Schedule Auto-End Check ===");
                _logger.LogInformation($"Current Malaysia Time: {nowMalaysia:yyyy-MM-dd HH:mm:ss}");
                _logger.LogInformation($"Current UTC Time: {nowUtc:yyyy-MM-dd HH:mm:ss}");
                _logger.LogInformation($"Auto-End Threshold (UTC): {autoEndThreshold:yyyy-MM-dd HH:mm:ss}");

                // ===== COMPETITIONS =====
                var competitionsToEnd = await context.Schedules
                    .Where(s => s.ScheduleType == ScheduleType.Competition &&
                               s.Status == ScheduleStatus.InProgress &&
                               s.EndTime.HasValue &&
                               s.EndTime.Value <= autoEndThreshold)
                    .ToListAsync();

                if (competitionsToEnd.Any())
                {
                    _logger.LogInformation($"Found {competitionsToEnd.Count} competitions to auto-end (change to Completed)");

                    foreach (var competition in competitionsToEnd)
                    {
                        competition.Status = ScheduleStatus.Completed;
                        _logger.LogInformation($"  - Competition {competition.ScheduleId}: {competition.GameName} → Completed");
                        _logger.LogInformation($"    EndTime (UTC): {competition.EndTime:yyyy-MM-dd HH:mm:ss}");
                    }
                }
                else
                {
                    _logger.LogInformation("No competitions need to be auto-ended");
                }

                // ===== ONE-OFF SCHEDULES =====
                var oneOffToEnd = await context.Schedules
                    .Where(s => s.ScheduleType == ScheduleType.OneOff &&
                               s.Status == ScheduleStatus.Active &&
                               s.EndTime.HasValue &&
                               s.EndTime.Value <= autoEndThreshold)
                    .ToListAsync();

                if (oneOffToEnd.Any())
                {
                    _logger.LogInformation($"Found {oneOffToEnd.Count} one-off schedules to auto-end (change to Past)");

                    foreach (var schedule in oneOffToEnd)
                    {
                        schedule.Status = ScheduleStatus.Past;
                        _logger.LogInformation($"  - OneOff {schedule.ScheduleId}: {schedule.GameName} → Past");
                        _logger.LogInformation($"    EndTime (UTC): {schedule.EndTime:yyyy-MM-dd HH:mm:ss}");
                    }
                }
                else
                {
                    _logger.LogInformation("No one-off schedules need to be auto-ended");
                }

                // ===== RECURRING SCHEDULES =====
                var recurringToEnd = await context.Schedules
                    .Where(s => s.ScheduleType == ScheduleType.Recurring &&
                               s.Status == ScheduleStatus.Active &&
                               s.EndTime.HasValue &&
                               s.EndTime.Value <= autoEndThreshold)
                    .ToListAsync();

                if (recurringToEnd.Any())
                {
                    _logger.LogInformation($"Found {recurringToEnd.Count} recurring schedules to auto-end (change to Past)");

                    foreach (var schedule in recurringToEnd)
                    {
                        schedule.Status = ScheduleStatus.Past;
                        _logger.LogInformation($"  - Recurring {schedule.ScheduleId}: {schedule.GameName} → Past");
                        _logger.LogInformation($"    EndTime (UTC): {schedule.EndTime:yyyy-MM-dd HH:mm:ss}");
                    }
                }
                else
                {
                    _logger.LogInformation("No recurring schedules need to be auto-ended");
                }

                // Save all changes
                var totalChanges = competitionsToEnd.Count + oneOffToEnd.Count + recurringToEnd.Count;
                if (totalChanges > 0)
                {
                    await context.SaveChangesAsync();
                    _logger.LogInformation($"✓ Successfully auto-ended {totalChanges} schedules");
                }
                else
                {
                    _logger.LogInformation("✓ No schedules need to be auto-ended at this time");
                }
            }
        }
    }
}