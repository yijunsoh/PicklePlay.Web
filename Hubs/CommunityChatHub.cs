using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Helpers;
using System.Collections.Concurrent;

namespace PicklePlay.Hubs
{
    public class CommunityChatHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        
        // Track which users are in which community chat rooms
        private static readonly ConcurrentDictionary<string, HashSet<int>> _communityRooms = new();
        
        // Track user's connection ID to userId mapping
        private static readonly ConcurrentDictionary<string, int> _userConnections = new();

        public CommunityChatHub(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
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
            
            Console.WriteLine($"=== CommunityChatHub OnConnectedAsync ===");
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
            
            Console.WriteLine($"=== CommunityChatHub OnDisconnectedAsync ===");
            Console.WriteLine($"User ID: {userId}");
            Console.WriteLine($"Connection ID: {Context.ConnectionId}");

            if (_userConnections.TryRemove(Context.ConnectionId, out int removedUserId))
            {
                // Remove user from all community rooms
                foreach (var room in _communityRooms)
                {
                    if (room.Value.Remove(removedUserId))
                    {
                        Console.WriteLine($"User {removedUserId} removed from community {room.Key}");
                        
                        // Notify others in the room
                        await Clients.Group($"community_{room.Key}").SendAsync("UserLeftChat", new
                        {
                            userId = removedUserId,
                            timestamp = DateTime.UtcNow
                        });
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Join a community chat room
        public async Task JoinCommunityChat(int communityId)
        {
            var userId = GetCurrentUserId();

            Console.WriteLine($"=== JoinCommunityChat ===");
            Console.WriteLine($"User ID: {userId}");
            Console.WriteLine($"Community ID: {communityId}");

            if (userId == 0)
            {
                Console.WriteLine("ERROR: User not authenticated");
                await Clients.Caller.SendAsync("Error", "You must be logged in to join community chat");
                return;
            }

            try
            {
                // Verify user is a member of this community
                var membership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == communityId && 
                                              cm.UserId == userId && 
                                              cm.Status == "Active");

                if (membership == null)
                {
                    Console.WriteLine("ERROR: User is not a member of this community");
                    await Clients.Caller.SendAsync("Error", "You must be a member to join this community chat");
                    return;
                }

                // Add to SignalR group
                string groupName = $"community_{communityId}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                // Track user in community room
                if (!_communityRooms.ContainsKey(communityId.ToString()))
                {
                    _communityRooms[communityId.ToString()] = new HashSet<int>();
                }
                _communityRooms[communityId.ToString()].Add(userId);

                Console.WriteLine($"User {userId} joined community chat {communityId}");

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
                await Clients.Caller.SendAsync("JoinedCommunityChat", new
                {
                    communityId = communityId,
                    message = "Successfully joined community chat"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in JoinCommunityChat: {ex.Message}");
                await Clients.Caller.SendAsync("Error", "Failed to join community chat");
            }
        }

        // Leave a community chat room
        public async Task LeaveCommunityChat(int communityId)
        {
            var userId = GetCurrentUserId();

            if (userId == 0) return;

            try
            {
                string groupName = $"community_{communityId}";
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

                // Remove from tracking
                if (_communityRooms.TryGetValue(communityId.ToString(), out var users))
                {
                    users.Remove(userId);
                }

                Console.WriteLine($"User {userId} left community chat {communityId}");

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
                Console.WriteLine($"ERROR in LeaveCommunityChat: {ex.Message}");
            }
        }

        // Send message to community chat
        public async Task SendCommunityMessage(int communityId, string message)
        {
            var senderId = GetCurrentUserId();

            Console.WriteLine($"=== SendCommunityMessage ===");
            Console.WriteLine($"Sender ID: {senderId}");
            Console.WriteLine($"Community ID: {communityId}");
            Console.WriteLine($"Message: {message}");

            if (senderId == 0)
            {
                Console.WriteLine("ERROR: Sender not authenticated");
                await Clients.Caller.SendAsync("Error", "You must be logged in to send messages");
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine("ERROR: Empty message");
                await Clients.Caller.SendAsync("Error", "Message cannot be empty");
                return;
            }

            try
            {
                // Verify user is a member of this community
                var membership = await _context.CommunityMembers
                    .FirstOrDefaultAsync(cm => cm.CommunityId == communityId && 
                                              cm.UserId == senderId && 
                                              cm.Status == "Active");

                if (membership == null)
                {
                    Console.WriteLine("ERROR: User is not a member of this community");
                    await Clients.Caller.SendAsync("Error", "You must be a member to send messages");
                    return;
                }

                // Save message to database (we'll create this model next)
                var newMessage = new CommunityChatMessage
                {
                    CommunityId = communityId,
                    SenderId = senderId,
                    Content = message,
                    SentAt = DateTimeHelper.GetMalaysiaTime()
                };

                _context.CommunityChatMessages.Add(newMessage);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Message saved to database with ID: {newMessage.MessageId}");

                // Get sender info
                var sender = await _context.Users.FindAsync(senderId);

                // Prepare message data
                var messageData = new
                {
                    messageId = newMessage.MessageId,
                    communityId = communityId,
                    senderId = senderId,
                    senderName = sender?.Username ?? "Unknown",
                    senderProfilePicture = sender?.ProfilePicture,
                    content = message,
                    sentAt = newMessage.SentAt.ToString("o"),
                    isAdmin = membership.CommunityRole == "Admin",
                    isModerator = membership.CommunityRole == "Moderator"
                };

                // Send to all users in the community chat room
                string groupName = $"community_{communityId}";
                await Clients.Group(groupName).SendAsync("ReceiveCommunityMessage", messageData);

                Console.WriteLine($"Message broadcast to community {communityId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in SendCommunityMessage: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await Clients.Caller.SendAsync("Error", "Failed to send message. Please try again.");
            }
        }

        // Get online members count for a community
        public async Task GetOnlineMembers(int communityId)
        {
            var userId = GetCurrentUserId();

            if (userId == 0) return;

            try
            {
                var onlineCount = _communityRooms.TryGetValue(communityId.ToString(), out var users) 
                    ? users.Count 
                    : 0;

                await Clients.Caller.SendAsync("OnlineMembersCount", new
                {
                    communityId = communityId,
                    count = onlineCount
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in GetOnlineMembers: {ex.Message}");
            }
        }

        // Static method to check online members (used by controllers)
        public static int GetCommunityOnlineCount(int communityId)
        {
            return _communityRooms.TryGetValue(communityId.ToString(), out var users) 
                ? users.Count 
                : 0;
        }

        // Static method to get all online members
        public static List<int> GetCommunityOnlineMembers(int communityId)
        {
            return _communityRooms.TryGetValue(communityId.ToString(), out var users) 
                ? users.ToList() 
                : new List<int>();
        }
    }
}