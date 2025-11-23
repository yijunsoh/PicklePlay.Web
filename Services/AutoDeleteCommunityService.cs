using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PicklePlay.Data;
using PicklePlay.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace PicklePlay.Services
{
    public class AutoDeleteCommunityService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutoDeleteCommunityService> _logger;

        public AutoDeleteCommunityService(
            IServiceProvider serviceProvider,
            ILogger<AutoDeleteCommunityService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Auto Delete Community Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndDeleteInactiveCommunities();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Auto Delete Community Service");
                }

                // Check every 24 hours (daily)
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task CheckAndDeleteInactiveCommunities()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                var nowUtc = DateTime.UtcNow;
                var sixMonthsAgo = nowUtc.AddMonths(-6);

                _logger.LogInformation($"=== Auto Delete Community Check ===");
                _logger.LogInformation($"Current UTC Time: {nowUtc:yyyy-MM-dd HH:mm:ss}");
                _logger.LogInformation($"6 Months Cutoff: {sixMonthsAgo:yyyy-MM-dd HH:mm:ss}");

                // Find communities that are inactive for 6+ months
                var communitiesToDelete = await context.Communities
                    .Include(c => c.Memberships)
                    .Where(c => c.Status == "Active" && (
                        (c.LastActivityDate != null && c.LastActivityDate <= sixMonthsAgo) ||
                        (c.LastActivityDate == null && c.CreatedDate <= sixMonthsAgo)
                    ))
                    .ToListAsync();

                _logger.LogInformation($"Found {communitiesToDelete.Count} communities inactive for 6+ months");

                int deletedCount = 0;
                int errorCount = 0;

                foreach (var community in communitiesToDelete)
                {
                    try
                    {
                        await AutoDeleteCommunity(context, community);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error auto-deleting community {community.CommunityId}");
                        errorCount++;
                    }
                }

                _logger.LogInformation($"✅ Auto Delete Community Check Complete:");
                _logger.LogInformation($"   - Successfully deleted: {deletedCount} communities");
                _logger.LogInformation($"   - Errors: {errorCount} communities");
                _logger.LogInformation($"=== Auto Delete Community Check Complete ===\n");
            }
        }

        private async Task AutoDeleteCommunity(ApplicationDbContext context, Community community)
        {
            var nowUtc = DateTime.UtcNow;

            try
            {
                _logger.LogInformation($"Auto-deleting community {community.CommunityId} ({community.CommunityName})");

                // Store original name before modifying
                string originalName = community.CommunityName;
                
                // Generate unique suffix with timestamp and random characters
                string timestamp = nowUtc.ToString("yyyyMMddHHmmss");
                string randomSuffix = Guid.NewGuid().ToString("N")[..6]; // First 6 chars of GUID
                
                // Update community name with deletion marker (same format as admin)
                community.CommunityName = $"[{originalName}]_deleted_{timestamp}_{randomSuffix}";
                community.Status = "Deleted";
                community.DeletionReason = "Auto-deleted by system - inactive for 6+ months";
                community.DeletedByUserId = null; // System deletion, no user
                community.DeletionDate = nowUtc;
                community.IsSystemDeletion = true;

                // Update all community members to inactive
                foreach (var member in community.Memberships)
                {
                    member.Status = "Inactive";
                }

                await context.SaveChangesAsync();

                _logger.LogInformation($"✅ Successfully auto-deleted community {community.CommunityId}");
                _logger.LogInformation($"   - Original name: {originalName}");
                _logger.LogInformation($"   - New name: {community.CommunityName}");
                _logger.LogInformation($"   - Members updated: {community.Memberships.Count}");

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error auto-deleting community {community.CommunityId}");
                throw; // Re-throw to count as error
            }
        }
    }
}