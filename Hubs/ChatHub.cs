using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Helpers;
using System.Collections.Concurrent;

namespace PicklePlay.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private static readonly ConcurrentDictionary<int, string> _onlineUsers = new();

        public ChatHub(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        // ⬇️ CHANGED: Use Session instead of Claims
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
            
            Console.WriteLine($"=== ChatHub OnConnectedAsync ===");
            Console.WriteLine($"User ID: {userId}");
            Console.WriteLine($"Connection ID: {Context.ConnectionId}");

            if (userId > 0)
            {
                _onlineUsers[userId] = Context.ConnectionId;
                Console.WriteLine($"User {userId} added to online users");

                // Notify friends that this user is online
                await NotifyFriendsOnlineStatus(userId, true);
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
            
            Console.WriteLine($"=== ChatHub OnDisconnectedAsync ===");
            Console.WriteLine($"User ID: {userId}");
            Console.WriteLine($"Connection ID: {Context.ConnectionId}");

            if (userId > 0)
            {
                _onlineUsers.TryRemove(userId, out _);
                Console.WriteLine($"User {userId} removed from online users");

                // Notify friends that this user is offline
                await NotifyFriendsOnlineStatus(userId, false);
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendPrivateMessage(int receiverId, string message)
        {
            var senderId = GetCurrentUserId();

            Console.WriteLine($"=== SendPrivateMessage ===");
            Console.WriteLine($"Sender ID: {senderId}");
            Console.WriteLine($"Receiver ID: {receiverId}");
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
                // Check if users are friends
                var friendship = await _context.Friendships
                    .FirstOrDefaultAsync(f =>
                        ((f.UserOneId == senderId && f.UserTwoId == receiverId) ||
                         (f.UserOneId == receiverId && f.UserTwoId == senderId)) &&
                        f.Status == FriendshipStatus.Accepted);

                if (friendship == null)
                {
                    Console.WriteLine("ERROR: Users are not friends");
                    await Clients.Caller.SendAsync("Error", "You can only send messages to friends");
                    return;
                }

                // Save message to database
                var newMessage = new Message
                {
                    SenderId = senderId,
                    ReceiverId = receiverId,
                    Content = message,
                    SentAt = DateTimeHelper.GetMalaysiaTime(),
                    IsRead = false
                };

                _context.Messages.Add(newMessage);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Message saved to database with ID: {newMessage.MessageId}");

                // Get sender info
                var sender = await _context.Users.FindAsync(senderId);
                var senderName = sender?.Username ?? "Unknown";

                // Prepare message data
                var messageData = new
                {
                    messageId = newMessage.MessageId,
                    senderId = senderId,
                    senderName = senderName,
                    receiverId = receiverId,
                    content = message,
                    sentAt = newMessage.SentAt.ToString("o"),
                    isRead = false
                };

                // Send to receiver if online
                if (_onlineUsers.TryGetValue(receiverId, out string? receiverConnectionId))
                {
                    Console.WriteLine($"Sending message to receiver connection: {receiverConnectionId}");
                    await Clients.Client(receiverConnectionId).SendAsync("ReceivePrivateMessage", messageData);
                }
                else
                {
                    Console.WriteLine($"Receiver {receiverId} is offline");
                }

                // Send confirmation to sender
                Console.WriteLine($"Sending confirmation to sender");
                await Clients.Caller.SendAsync("MessageSent", messageData);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in SendPrivateMessage: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await Clients.Caller.SendAsync("Error", "Failed to send message. Please try again.");
            }
        }

        public async Task GetOnlineFriends()
        {
            var userId = GetCurrentUserId();

            Console.WriteLine($"=== GetOnlineFriends ===");
            Console.WriteLine($"User ID: {userId}");

            if (userId == 0)
            {
                Console.WriteLine("ERROR: User not authenticated");
                return;
            }

            try
            {
                // Get user's friends
                var friendIds = await _context.Friendships
                    .Where(f => (f.UserOneId == userId || f.UserTwoId == userId) &&
                               f.Status == FriendshipStatus.Accepted)
                    .Select(f => f.UserOneId == userId ? f.UserTwoId : f.UserOneId)
                    .ToListAsync();

                Console.WriteLine($"Found {friendIds.Count} friends");

                // Check which friends are online
                var onlineFriends = friendIds
                    .Where(id => _onlineUsers.ContainsKey(id))
                    .Select(id => new { userId = id, isOnline = true })
                    .ToList();

                Console.WriteLine($"{onlineFriends.Count} friends are online");

                // Send online status for each friend
                foreach (var friend in onlineFriends)
                {
                    await Clients.Caller.SendAsync("FriendOnlineStatusChanged", friend);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in GetOnlineFriends: {ex.Message}");
            }
        }

        public async Task MarkMessagesAsRead(int senderId)
        {
            var receiverId = GetCurrentUserId();

            Console.WriteLine($"=== MarkMessagesAsRead ===");
            Console.WriteLine($"Receiver ID: {receiverId}");
            Console.WriteLine($"Sender ID: {senderId}");

            if (receiverId == 0)
            {
                Console.WriteLine("ERROR: User not authenticated");
                return;
            }

            try
            {
                var unreadMessages = await _context.Messages
                    .Where(m => m.SenderId == senderId && 
                               m.ReceiverId == receiverId && 
                               !m.IsRead)
                    .ToListAsync();

                Console.WriteLine($"Found {unreadMessages.Count} unread messages");

                foreach (var message in unreadMessages)
                {
                    message.IsRead = true;
                    message.ReadAt = DateTimeHelper.GetMalaysiaTime();
                }

                await _context.SaveChangesAsync();
                Console.WriteLine("Messages marked as read");

                // Notify sender that messages were read
                if (_onlineUsers.TryGetValue(senderId, out string? senderConnectionId))
                {
                    await Clients.Client(senderConnectionId).SendAsync("MessagesRead", new
                    {
                        readBy = receiverId,
                        messageIds = unreadMessages.Select(m => m.MessageId).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in MarkMessagesAsRead: {ex.Message}");
            }
        }

        public async Task UserTyping(int receiverId, bool isTyping)
        {
            var senderId = GetCurrentUserId();

            if (senderId == 0) return;

            if (_onlineUsers.TryGetValue(receiverId, out string? receiverConnectionId))
            {
                await Clients.Client(receiverConnectionId).SendAsync("UserTyping", new
                {
                    userId = senderId,
                    isTyping = isTyping
                });
            }
        }

        private async Task NotifyFriendsOnlineStatus(int userId, bool isOnline)
        {
            try
            {
                // Get user's friends
                var friendIds = await _context.Friendships
                    .Where(f => (f.UserOneId == userId || f.UserTwoId == userId) &&
                               f.Status == FriendshipStatus.Accepted)
                    .Select(f => f.UserOneId == userId ? f.UserTwoId : f.UserOneId)
                    .ToListAsync();

                Console.WriteLine($"Notifying {friendIds.Count} friends about user {userId} status: {(isOnline ? "online" : "offline")}");

                // Notify each online friend
                foreach (var friendId in friendIds)
                {
                    if (_onlineUsers.TryGetValue(friendId, out string? friendConnectionId))
                    {
                        await Clients.Client(friendConnectionId).SendAsync("FriendOnlineStatusChanged", new
                        {
                            userId = userId,
                            isOnline = isOnline
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in NotifyFriendsOnlineStatus: {ex.Message}");
            }
        }

        // Static method to check if user is online (used by FriendshipController)
        public static bool IsUserOnline(int userId)
        {
            return _onlineUsers.ContainsKey(userId);
        }

        // Get online user count
        public static int GetOnlineUserCount()
        {
            return _onlineUsers.Count;
        }

        // Get all online user IDs
        public static List<int> GetOnlineUserIds()
        {
            return _onlineUsers.Keys.ToList();
        }
    }
}