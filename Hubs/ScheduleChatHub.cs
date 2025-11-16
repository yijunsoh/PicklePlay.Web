using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Helpers;
using System.Collections.Concurrent;

namespace PicklePlay.Hubs
{
    public class ScheduleChatHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        
        // Track which users are in which schedule chat rooms
        private static readonly ConcurrentDictionary<string, HashSet<int>> _scheduleRooms = new();
        
        // Track user's connection ID to userId mapping
        private static readonly ConcurrentDictionary<string, int> _userConnections = new();

        public ScheduleChatHub(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private int GetCurrentUserId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
                return 0;

            var userId = httpContext.Session.GetInt32("UserId");
            return userId ?? 0;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            
            Console.WriteLine($"=== ScheduleChatHub OnConnectedAsync ===");
            Console.WriteLine($"User ID: {userId}");
            Console.WriteLine($"Connection ID: {Context.ConnectionId}");

            if (userId > 0)
            {
                _userConnections[Context.ConnectionId] = userId;
                Console.WriteLine($"User {userId} connected with connection {Context.ConnectionId}");
            }
            else
            {
                Console.WriteLine("WARNING: User not authenticated");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            
            Console.WriteLine($"=== ScheduleChatHub OnDisconnectedAsync ===");
            Console.WriteLine($"User ID: {userId}");
            Console.WriteLine($"Connection ID: {Context.ConnectionId}");

            if (_userConnections.TryRemove(Context.ConnectionId, out int removedUserId))
            {
                // Remove user from all schedule rooms
                foreach (var room in _scheduleRooms)
                {
                    if (room.Value.Remove(removedUserId))
                    {
                        Console.WriteLine($"User {removedUserId} removed from schedule {room.Key}");
                        
                        // Notify others in the room
                        await Clients.Group($"schedule_{room.Key}").SendAsync("UserLeftChat", new
                        {
                            userId = removedUserId,
                            timestamp = DateTime.UtcNow
                        });
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Join a schedule chat room
        public async Task JoinScheduleChat(int scheduleId)
        {
            var userId = GetCurrentUserId();

            Console.WriteLine($"=== JoinScheduleChat ===");
            Console.WriteLine($"User ID: {userId}");
            Console.WriteLine($"Schedule ID: {scheduleId}");

            if (userId == 0)
            {
                Console.WriteLine("ERROR: User not authenticated");
                await Clients.Caller.SendAsync("Error", "You must be logged in to join schedule chat");
                return;
            }

            try
            {
                // ⬇️ FIXED: Check if user is SCHEDULE ORGANIZER (from ScheduleParticipants)
                var isScheduleOrganizer = await _context.ScheduleParticipants
                    .AnyAsync(sp => sp.ScheduleId == scheduleId && 
                                   sp.UserId == userId && 
                                   sp.Role == ParticipantRole.Organizer);

                // ⬇️ Check if user is in a confirmed team
                var isInConfirmedTeam = await _context.Teams
                    .Where(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed)
                    .AnyAsync(team => team.TeamMembers.Any(tm => 
                        tm.UserId == userId && 
                        tm.Status == TeamMemberStatus.Joined
                    ));

                // User must be organizer OR in confirmed team
                if (!isScheduleOrganizer && !isInConfirmedTeam)
                {
                    Console.WriteLine("ERROR: User is not organizer and not in a confirmed team");
                    await Clients.Caller.SendAsync("Error", "Only organizers and confirmed team members can join this chat");
                    return;
                }

                Console.WriteLine($"User {userId} access granted - IsOrganizer: {isScheduleOrganizer}, IsInTeam: {isInConfirmedTeam}");

                // Add to SignalR group
                string groupName = $"schedule_{scheduleId}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                // Track user in schedule room
                if (!_scheduleRooms.ContainsKey(scheduleId.ToString()))
                {
                    _scheduleRooms[scheduleId.ToString()] = new HashSet<int>();
                }
                _scheduleRooms[scheduleId.ToString()].Add(userId);

                Console.WriteLine($"User {userId} joined schedule chat {scheduleId}");

                // Get user info
                var user = await _context.Users.FindAsync(userId);

                // Notify others in the room
                await Clients.OthersInGroup(groupName).SendAsync("UserJoinedChat", new
                {
                    userId = userId,
                    username = user?.Username ?? "Unknown",
                    profilePicture = user?.ProfilePicture,
                    timestamp = DateTime.UtcNow
                });

                // Send confirmation to caller
                await Clients.Caller.SendAsync("JoinedScheduleChat", new
                {
                    scheduleId = scheduleId,
                    message = "Successfully joined schedule chat"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in JoinScheduleChat: {ex.Message}");
                await Clients.Caller.SendAsync("Error", "Failed to join schedule chat");
            }
        }

        // Leave a schedule chat room
        public async Task LeaveScheduleChat(int scheduleId)
        {
            var userId = GetCurrentUserId();

            if (userId == 0) return;

            try
            {
                string groupName = $"schedule_{scheduleId}";
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

                // Remove from tracking
                if (_scheduleRooms.TryGetValue(scheduleId.ToString(), out var users))
                {
                    users.Remove(userId);
                }

                Console.WriteLine($"User {userId} left schedule chat {scheduleId}");

                // Get user info
                var user = await _context.Users.FindAsync(userId);

                // Notify others
                await Clients.OthersInGroup(groupName).SendAsync("UserLeftChat", new
                {
                    userId = userId,
                    username = user?.Username ?? "Unknown",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in LeaveScheduleChat: {ex.Message}");
            }
        }

        // Send message to schedule chat
        public async Task SendScheduleMessage(int scheduleId, string message)
        {
            var senderId = GetCurrentUserId();

            Console.WriteLine($"=== SendScheduleMessage ===");
            Console.WriteLine($"Sender ID: {senderId}");
            Console.WriteLine($"Schedule ID: {scheduleId}");

            if (senderId == 0)
            {
                await Clients.Caller.SendAsync("Error", "You must be logged in to send messages");
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                await Clients.Caller.SendAsync("Error", "Message cannot be empty");
                return;
            }

            try
            {
                // ⬇️ FIXED: Check if user is SCHEDULE ORGANIZER (from ScheduleParticipants)
                var isScheduleOrganizer = await _context.ScheduleParticipants
                    .AnyAsync(sp => sp.ScheduleId == scheduleId && 
                                   sp.UserId == senderId && 
                                   sp.Role == ParticipantRole.Organizer);

                // Check if user is in a confirmed team
                var isInConfirmedTeam = await _context.Teams
                    .Where(t => t.ScheduleId == scheduleId && t.Status == TeamStatus.Confirmed)
                    .AnyAsync(team => team.TeamMembers.Any(tm => 
                        tm.UserId == senderId && 
                        tm.Status == TeamMemberStatus.Joined
                    ));

                // User must be organizer OR in confirmed team
                if (!isScheduleOrganizer && !isInConfirmedTeam)
                {
                    Console.WriteLine("ERROR: User is not organizer and not in a confirmed team");
                    await Clients.Caller.SendAsync("Error", "Only organizers and confirmed team members can send messages");
                    return;
                }

                Console.WriteLine($"User {senderId} - IsOrganizer: {isScheduleOrganizer}");

                // Save message to database
                var newMessage = new ScheduleChatMessage
                {
                    ScheduleId = scheduleId,
                    SenderId = senderId,
                    Content = message,
                    SentAt = DateTimeHelper.GetMalaysiaTime()
                };

                _context.ScheduleChatMessages.Add(newMessage);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Message saved with ID: {newMessage.MessageId}");

                // Get sender info
                var sender = await _context.Users.FindAsync(senderId);

                // ⬇️ FIXED: isOrganizer should be based on ScheduleParticipants, NOT team captain
                var messageData = new
                {
                    messageId = newMessage.MessageId,
                    scheduleId = scheduleId,
                    senderId = senderId,
                    senderName = sender?.Username ?? "Unknown",
                    senderProfilePicture = sender?.ProfilePicture,
                    content = message,
                    sentAt = newMessage.SentAt.ToString("o"),
                    isOrganizer = isScheduleOrganizer // ⬅️ FIXED: Use ScheduleParticipants check
                };

                string groupName = $"schedule_{scheduleId}";
                await Clients.Group(groupName).SendAsync("ReceiveScheduleMessage", messageData);

                Console.WriteLine($"Message broadcast to schedule {scheduleId} - Organizer badge: {isScheduleOrganizer}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in SendScheduleMessage: {ex.Message}");
                await Clients.Caller.SendAsync("Error", "Failed to send message. Please try again.");
            }
        }

        // Get online participants count for a schedule
        public async Task GetOnlineParticipants(int scheduleId)
        {
            var userId = GetCurrentUserId();

            if (userId == 0) return;

            try
            {
                var onlineCount = _scheduleRooms.TryGetValue(scheduleId.ToString(), out var users) 
                    ? users.Count 
                    : 0;

                await Clients.Caller.SendAsync("OnlineParticipantsCount", new
                {
                    scheduleId = scheduleId,
                    count = onlineCount
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in GetOnlineParticipants: {ex.Message}");
            }
        }

        // Static method to check online participants (used by controllers)
        public static int GetScheduleOnlineCount(int scheduleId)
        {
            return _scheduleRooms.TryGetValue(scheduleId.ToString(), out var users) 
                ? users.Count 
                : 0;
        }

        // Static method to get all online participants
        public static List<int> GetScheduleOnlineParticipants(int scheduleId)
        {
            return _scheduleRooms.TryGetValue(scheduleId.ToString(), out var users) 
                ? users.ToList() 
                : new List<int>();
        }
    }
}