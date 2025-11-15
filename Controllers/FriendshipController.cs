using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using PicklePlay.Helpers;
using PicklePlay.Hubs;

namespace PicklePlay.Controllers
{
    public class FriendshipController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FriendshipController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ⬇️ CHANGED: Use Session instead of Claims
        private int GetCurrentUserId()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            return userId ?? 0;
        }

        // GET: Friend List
        [HttpGet]
        public async Task<IActionResult> GetFriendList()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
                return Unauthorized(new { success = false, message = "Please log in" });

            var friendships = await _context.Friendships
                .Include(f => f.UserOne)
                .Include(f => f.UserTwo)
                .Where(f => (f.UserOneId == currentUserId || f.UserTwoId == currentUserId) &&
                           f.Status == FriendshipStatus.Accepted)
                .ToListAsync();

            var friends = new List<FriendItem>();

            foreach (var friendship in friendships)
            {
                var friendUser = friendship.UserOneId == currentUserId 
                    ? friendship.UserTwo
                    : friendship.UserOne;

                if (friendUser == null) continue;

                var unreadCount = await _context.Messages
                    .CountAsync(m => m.SenderId == friendUser.UserId && 
                                    m.ReceiverId == currentUserId && 
                                    !m.IsRead);

                var lastMessage = await _context.Messages
                    .Where(m => (m.SenderId == currentUserId && m.ReceiverId == friendUser.UserId) ||
                               (m.SenderId == friendUser.UserId && m.ReceiverId == currentUserId))
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefaultAsync();

                friends.Add(new FriendItem
                {
                    UserId = friendUser.UserId,
                    Username = friendUser.Username ?? "Unknown",
                    ProfilePicture = friendUser.ProfilePicture,
                    IsOnline = ChatHub.IsUserOnline(friendUser.UserId),
                    LastSeen = friendUser.LastLogin,
                    UnreadMessageCount = unreadCount,
                    LastMessage = lastMessage?.Content,
                    LastMessageTime = lastMessage?.SentAt,
                    FriendshipId = friendship.FriendshipId
                });
            }

            friends = friends.OrderByDescending(f => f.LastMessageTime ?? DateTime.MinValue).ToList();

            var pendingRequests = await _context.Friendships
                .Include(f => f.UserOne)
                .Where(f => f.UserTwoId == currentUserId && f.Status == FriendshipStatus.Pending)
                .Select(f => new FriendRequestItem
                {
                    FriendshipId = f.FriendshipId,
                    UserId = f.UserOneId,
                    Username = f.UserOne!.Username ?? "Unknown",
                    ProfilePicture = f.UserOne.ProfilePicture,
                    RequestedAt = f.RequestDate,
                    IsIncoming = true
                })
                .ToListAsync();

            var sentRequests = await _context.Friendships
                .Include(f => f.UserTwo)
                .Where(f => f.UserOneId == currentUserId && f.Status == FriendshipStatus.Pending)
                .Select(f => new FriendRequestItem
                {
                    FriendshipId = f.FriendshipId,
                    UserId = f.UserTwoId,
                    Username = f.UserTwo!.Username ?? "Unknown",
                    ProfilePicture = f.UserTwo.ProfilePicture,
                    RequestedAt = f.RequestDate,
                    IsIncoming = false
                })
                .ToListAsync();

            var viewModel = new FriendListViewModel
            {
                Friends = friends,
                PendingRequests = pendingRequests,
                SentRequests = sentRequests
            };

            return Json(new { success = true, data = viewModel });
        }

        // POST: Send Friend Request
        [HttpPost]
        public async Task<IActionResult> SendFriendRequest([FromBody] int friendId)
        {
            var currentUserId = GetCurrentUserId();
            
            // ⬇️ ADDED: Debug logging
            Console.WriteLine($"=== SendFriendRequest Debug ===");
            Console.WriteLine($"Current User ID: {currentUserId}");
            Console.WriteLine($"Friend ID: {friendId}");
            
            if (currentUserId == 0)
            {
                Console.WriteLine("ERROR: User not logged in");
                return Unauthorized(new { success = false, message = "Please log in" });
            }

            if (currentUserId == friendId)
            {
                Console.WriteLine("ERROR: Cannot friend yourself");
                return BadRequest(new { success = false, message = "You cannot send a friend request to yourself" });
            }

            var existingFriendship = await _context.Friendships
                .FirstOrDefaultAsync(f =>
                    (f.UserOneId == currentUserId && f.UserTwoId == friendId) ||
                    (f.UserOneId == friendId && f.UserTwoId == currentUserId));

            if (existingFriendship != null)
            {
                Console.WriteLine($"ERROR: Friendship exists with status: {existingFriendship.Status}");
                
                if (existingFriendship.Status == FriendshipStatus.Accepted)
                    return BadRequest(new { success = false, message = "You are already friends" });
                
                if (existingFriendship.Status == FriendshipStatus.Pending)
                    return BadRequest(new { success = false, message = "Friend request already sent" });
            }

            var friendship = new Friendship
            {
                UserOneId = currentUserId,
                UserTwoId = friendId,
                Status = FriendshipStatus.Pending,
                RequestDate = DateTimeHelper.GetMalaysiaTime()
            };

            _context.Friendships.Add(friendship);

            var sender = await _context.Users.FindAsync(currentUserId);
            var notification = new Notification
            {
                UserId = friendId,
                Type = NotificationType.FriendRequest,
                Message = $"{sender?.Username ?? "Someone"} sent you a friend request",
                RelatedUserId = currentUserId,
                RelatedEntityId = friendship.FriendshipId,
                CreatedAt = DateTimeHelper.GetMalaysiaTime()
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            Console.WriteLine($"SUCCESS: Friend request created with ID: {friendship.FriendshipId}");

            return Json(new { success = true, message = "Friend request sent!" });
        }

        // POST: Accept Friend Request
        [HttpPost]
        public async Task<IActionResult> AcceptFriendRequest([FromBody] int friendshipId)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
                return Unauthorized(new { success = false, message = "Please log in" });

            var friendship = await _context.Friendships
                .Include(f => f.UserOne)
                .FirstOrDefaultAsync(f => f.FriendshipId == friendshipId && f.UserTwoId == currentUserId);

            if (friendship == null)
                return NotFound(new { success = false, message = "Friend request not found" });

            if (friendship.Status != FriendshipStatus.Pending)
                return BadRequest(new { success = false, message = "This request has already been processed" });

            friendship.Status = FriendshipStatus.Accepted;
            friendship.Status = FriendshipStatus.Accepted;
            friendship.AcceptedDate = DateTimeHelper.GetMalaysiaTime();
            var accepter = await _context.Users.FindAsync(currentUserId);
            var notification = new Notification
            {
                UserId = friendship.UserOneId,
                Type = NotificationType.FriendRequestAccepted,
                Message = $"{accepter?.Username ?? "Someone"} accepted your friend request",
                ActionUrl = "/CommunicationHub/Communication?tab=friends",
                RelatedUserId = currentUserId,
                CreatedAt = DateTimeHelper.GetMalaysiaTime()
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Friend request accepted!" });
        }

        // POST: Decline Friend Request
        [HttpPost]
        public async Task<IActionResult> DeclineFriendRequest([FromBody] int friendshipId)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
                return Unauthorized(new { success = false, message = "Please log in" });

            var friendship = await _context.Friendships
                .FirstOrDefaultAsync(f => f.FriendshipId == friendshipId && f.UserTwoId == currentUserId);

            if (friendship == null)
                return NotFound(new { success = false, message = "Friend request not found" });

            _context.Friendships.Remove(friendship);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Friend request declined" });
        }

        // POST: Remove Friend
        [HttpPost]
        public async Task<IActionResult> RemoveFriend([FromBody] int friendshipId)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
                return Unauthorized(new { success = false, message = "Please log in" });

            var friendship = await _context.Friendships
                .FirstOrDefaultAsync(f => f.FriendshipId == friendshipId &&
                    (f.UserOneId == currentUserId || f.UserTwoId == currentUserId));

            if (friendship == null)
                return NotFound(new { success = false, message = "Friendship not found" });

            _context.Friendships.Remove(friendship);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Friend removed" });
        }

        // GET: Chat Messages
        [HttpGet]
        public async Task<IActionResult> GetChatMessages(int friendId)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
                return Unauthorized(new { success = false, message = "Please log in" });

            var friendship = await _context.Friendships
                .FirstOrDefaultAsync(f =>
                    ((f.UserOneId == currentUserId && f.UserTwoId == friendId) ||
                     (f.UserOneId == friendId && f.UserTwoId == currentUserId)) &&
                    f.Status == FriendshipStatus.Accepted);

            if (friendship == null)
                return BadRequest(new { success = false, message = "You are not friends with this user" });

            var friend = await _context.Users.FindAsync(friendId);
            if (friend == null)
                return NotFound(new { success = false, message = "User not found" });

            var messages = await _context.Messages
                .Where(m => (m.SenderId == currentUserId && m.ReceiverId == friendId) ||
                           (m.SenderId == friendId && m.ReceiverId == currentUserId))
                .OrderBy(m => m.SentAt)
                .Select(m => new ChatMessageItem
                {
                    MessageId = m.MessageId,
                    SenderId = m.SenderId,
                    ReceiverId = m.ReceiverId,
                    Content = m.Content,
                    SentAt = m.SentAt,
                    IsRead = m.IsRead,
                    IsMine = m.SenderId == currentUserId
                })
                .ToListAsync();

            var unreadMessages = await _context.Messages
                .Where(m => m.SenderId == friendId && m.ReceiverId == currentUserId && !m.IsRead)
                .ToListAsync();

            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
                msg.ReadAt = DateTimeHelper.GetMalaysiaTime();
            }

            await _context.SaveChangesAsync();

            var viewModel = new ChatViewModel
            {
                FriendId = friend.UserId,
                FriendUsername = friend.Username ?? "Unknown",
                FriendProfilePicture = friend.ProfilePicture,
                IsOnline = ChatHub.IsUserOnline(friend.UserId),
                Messages = messages
            };

            return Json(new { success = true, data = viewModel });
        }

        // POST: Send Message
        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
                return Unauthorized(new { success = false, message = "Please log in" });

            var friendship = await _context.Friendships
                .FirstOrDefaultAsync(f =>
                    ((f.UserOneId == currentUserId && f.UserTwoId == request.ReceiverId) ||
                     (f.UserOneId == request.ReceiverId && f.UserTwoId == currentUserId)) &&
                    f.Status == FriendshipStatus.Accepted);

            if (friendship == null)
                return BadRequest(new { success = false, message = "You are not friends with this user" });

            var message = new Message
            {
                SenderId = currentUserId,
                ReceiverId = request.ReceiverId,
                Content = request.Content,
                SentAt = DateTimeHelper.GetMalaysiaTime()
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Message sent!", data = new { messageId = message.MessageId } });
        }

        [HttpGet]
        public IActionResult TestSignalR()
        {
            return Json(new { 
                success = true, 
                message = "SignalR hub is configured",
                hubUrl = "/chatHub"
            });
        }

        // ⬇️ ADD THIS: Main Communication Hub View
        [HttpGet]
        public async Task<IActionResult> Communication()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
            {
                TempData["ErrorMessage"] = "Please log in to access the Communication Hub.";
                return RedirectToAction("Login", "Account");
            }

            // Load pending team invitations
            var pendingTeamInvitations = await _context.TeamInvitations
                .Include(i => i.Team)
                .Include(i => i.Inviter)
                .Where(i => i.InviterUserId == currentUserId && i.Status == InvitationStatus.Pending)
                .ToListAsync();

            // Load pending friend requests
            var pendingFriendRequests = await _context.Friendships
                .Include(f => f.UserOne)
                .Where(f => f.UserTwoId == currentUserId && f.Status == FriendshipStatus.Pending)
                .ToListAsync();

            // Load pending community invitations
            var pendingCommunityInvitations = await _context.CommunityInvitations
                .Include(ci => ci.Community)
                .Include(ci => ci.Inviter)
                .Where(ci => ci.InviterUserId == currentUserId && ci.Status == InvitationStatus.Pending.ToString())
                .ToListAsync();

            // Load general notifications
            var generalNotifications = await _context.Notifications
                .Include(n => n.RelatedUser)
                .Where(n => n.UserId == currentUserId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt)
                .Take(20)
                .ToListAsync();

            var viewModel = new CommunicationHubViewModel
            {
                PendingTeamInvitations = pendingTeamInvitations,
                PendingFriendRequests = pendingFriendRequests,
                PendingCommunityInvitations = pendingCommunityInvitations,
                GeneralNotifications = generalNotifications
            };

            return View("~/Views/CommunicationHub/Communication.cshtml", viewModel);
        }

        // ⬇️ ADD THIS: Direct Chat Action
        [HttpGet]
        public IActionResult Chat(int friendId)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
            {
                return RedirectToAction("Login", "Account");
            }

            // Redirect to Communication page with query parameters
           return RedirectToAction("Communication", new { tab = "friends", chatWith = friendId });
        }
    }

    public class SendMessageRequest
    {
        public int ReceiverId { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}