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

            // Run immediately on startup
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
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task CheckAndEndSchedules()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var nowUtc = DateTime.UtcNow;
                var nowMalaysia = DateTimeHelper.GetMalaysiaTime();
                
                _logger.LogInformation($"=== Schedule Auto-End Check ===");
                _logger.LogInformation($"Current UTC Time: {nowUtc:yyyy-MM-dd HH:mm:ss}");
                _logger.LogInformation($"Current Malaysia Time: {nowMalaysia:yyyy-MM-dd HH:mm:ss}");

                // ===== COMPETITIONS: InProgress (7) → Completed (8) =====
                var competitions = await context.Schedules
                    .Where(s => s.ScheduleType == ScheduleType.Competition &&
                               s.Status == ScheduleStatus.InProgress && // Status = 7
                               s.EndTime.HasValue)
                    .ToListAsync();

                _logger.LogInformation($"Checking {competitions.Count} competitions with InProgress (7) status");

                int competitionsChanged = 0;
                foreach (var comp in competitions)
                {
                    var endTime = DateTime.SpecifyKind(comp.EndTime!.Value, DateTimeKind.Utc);
                    var timeSinceEnd = nowUtc - endTime;
                    
                    if (timeSinceEnd.TotalHours >= 24)
                    {
                        _logger.LogInformation($"  ✓ Competition {comp.ScheduleId} ({comp.GameName}):");
                        _logger.LogInformation($"    EndTime: {endTime:yyyy-MM-dd HH:mm:ss} UTC");
                        _logger.LogInformation($"    Hours since end: {timeSinceEnd.TotalHours:F2}");
                        _logger.LogInformation($"    Status: InProgress (7) → Completed (8)");
                        
                        comp.Status = ScheduleStatus.Completed; // Status = 8
                        competitionsChanged++;
                    }
                }

                if (competitionsChanged > 0)
                {
                    _logger.LogInformation($"✓ Found {competitionsChanged} competitions to auto-end");
                }
                else
                {
                    _logger.LogInformation("✓ No competitions need to be auto-ended");
                }

                // ===== GAME SCHEDULES: Active (1) → Past (2) =====
                var oneOffGames = await context.Schedules
                    .Where(s => s.ScheduleType == ScheduleType.OneOff &&
                               s.Status == ScheduleStatus.Active && // Status = 1
                               s.StartTime.HasValue &&
                               s.Duration.HasValue)
                    .ToListAsync();

                _logger.LogInformation($"Checking {oneOffGames.Count} game schedules with Active (1) status");

                int gamesChanged = 0;
                foreach (var game in oneOffGames)
                {
                    var startTime = DateTime.SpecifyKind(game.StartTime!.Value, DateTimeKind.Utc);
                    var durationTimeSpan = ScheduleHelper.GetTimeSpan(game.Duration!.Value);
                    var calculatedEndTime = startTime.Add(durationTimeSpan);
                    var timeSinceEnd = nowUtc - calculatedEndTime;
                    
                    if (timeSinceEnd.TotalHours >= 24)
                    {
                        _logger.LogInformation($"  ✓ Game {game.ScheduleId} ({game.GameName}):");
                        _logger.LogInformation($"    StartTime: {startTime:yyyy-MM-dd HH:mm:ss} UTC");
                        _logger.LogInformation($"    Duration: {game.Duration}");
                        _logger.LogInformation($"    Calculated EndTime: {calculatedEndTime:yyyy-MM-dd HH:mm:ss} UTC");
                        _logger.LogInformation($"    Hours since end: {timeSinceEnd.TotalHours:F2}");
                        _logger.LogInformation($"    Status: Active (1) → Past (2)");
                        
                        game.Status = ScheduleStatus.Past; // Status = 2
                        gamesChanged++;
                    }
                }

                if (gamesChanged > 0)
                {
                    _logger.LogInformation($"✓ Found {gamesChanged} games to auto-end");
                }
                else
                {
                    _logger.LogInformation("✓ No games need to be auto-ended");
                }

                // Save all changes
                var totalChanges = competitionsChanged + gamesChanged;
                if (totalChanges > 0)
                {
                    var savedCount = await context.SaveChangesAsync();
                    _logger.LogInformation($"✅ Successfully saved {savedCount} changes to database");
                    _logger.LogInformation($"   - {competitionsChanged} competitions: InProgress (7) → Completed (8)");
                    _logger.LogInformation($"   - {gamesChanged} games: Active (1) → Past (2)");
                }
                else
                {
                    _logger.LogInformation("✓ No schedules need to be auto-ended at this time");
                }

                _logger.LogInformation($"=== Auto-End Check Complete ===\n");
            }
        }
    }
}