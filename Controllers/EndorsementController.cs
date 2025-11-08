using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace PicklePlay.Controllers
{
    public class EndorsementController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public EndorsementController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private int? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        }

        // --- UPDATED [HttpGet] Give ACTION ---
        [HttpGet]
        public async Task<IActionResult> Give(int id)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return RedirectToAction("Login", "Auth");

            var schedule = await _context.Schedules.FindAsync(id);
            if (schedule == null) return NotFound();

            if (schedule.EndorsementStatus != EndorsementStatus.PendingEndorsement)
            {
                TempData["ErrorMessage"] = "Endorsements are not open for this game.";
                return RedirectToAction("MyGame", "MyGame");
            }
            
            // Get all confirmed players, EXCLUDING the current user
            var participants = await _context.ScheduleParticipants
                .Include(p => p.User)
                .Where(p => p.ScheduleId == id &&
                            p.Role == ParticipantRole.Player &&
                            p.Status == ParticipantStatus.Confirmed &&
                            p.UserId != currentUserId.Value)
                .OrderBy(p => p.User!.Username)
                .ToListAsync();

            // Get endorsements this user has *already given* in this game
            var existingEndorsements = await _context.Endorsements
                .Where(e => e.ScheduleId == id && e.GiverUserId == currentUserId.Value)
                .ToDictionaryAsync(e => e.ReceiverUserId, e => e); // Key = ReceiverUserId

            var vm = new GiveEndorsementViewModel
            {
                ScheduleId = id,
                ScheduleName = schedule.GameName ?? "Game"
            };

            // Map participants and their existing endorsements
            foreach (var p in participants)
            {
                var participantVM = new ParticipantEndorsement
                {
                    ReceiverUserId = p.UserId,
                    Username = p.User!.Username,
                    ProfilePicture = p.User.ProfilePicture
                };

                if (existingEndorsements.TryGetValue(p.UserId, out var existing))
                {
                    // An endorsement already exists for this person
                    participantVM.SelectedPersonality = existing.Personality;
                    participantVM.SelectedSkill = existing.Skill;
                    participantVM.HasExistingPersonality = existing.Personality != PersonalityEndorsement.None;
                    participantVM.HasExistingSkill = existing.Skill != SkillEndorsement.None;
                }
                
                vm.ParticipantsToEndorse.Add(participantVM);
            }

            return View("~/Views/Schedule/GiveEndorsement.cshtml", vm);
        }

        // --- UPDATED [HttpPost] Give ACTION (now an Upsert) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Give(GiveEndorsementViewModel vm)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Unauthorized();

            // Get all existing endorsements this user has given *for this game*
            var existingEndorsements = await _context.Endorsements
                .Where(e => e.ScheduleId == vm.ScheduleId && e.GiverUserId == currentUserId.Value)
                .ToDictionaryAsync(e => e.ReceiverUserId, e => e);

            foreach (var participant in vm.ParticipantsToEndorse)
            {
                if (existingEndorsements.TryGetValue(participant.ReceiverUserId, out var endorsementToUpdate))
                {
                    // --- UPDATE logic ---
                    // Only update if the user selected a new value (don't overwrite "Heart" with "None")
                    if (participant.SelectedPersonality != PersonalityEndorsement.None)
                    {
                        endorsementToUpdate.Personality = participant.SelectedPersonality;
                    }
                    if (participant.SelectedSkill != SkillEndorsement.None)
                    {
                        endorsementToUpdate.Skill = participant.SelectedSkill;
                    }
                    _context.Endorsements.Update(endorsementToUpdate);
                }
                else
                {
                    // --- INSERT logic ---
                    // Only create if a value was actually selected
                    if (participant.SelectedPersonality != PersonalityEndorsement.None || 
                        participant.SelectedSkill != SkillEndorsement.None)
                    {
                        _context.Endorsements.Add(new Endorsement
                        {
                            ScheduleId = vm.ScheduleId,
                            GiverUserId = currentUserId.Value,
                            ReceiverUserId = participant.ReceiverUserId,
                            Personality = participant.SelectedPersonality,
                            Skill = participant.SelectedSkill,
                            DateGiven = DateTime.Now
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Endorsements submitted successfully!";
            return RedirectToAction("MyGame", "MyGame");
        }

        // --- NEW: "See Endorsements" ACTION ---
        // --- UPDATED "See Endorsements" ACTION ---
        [HttpGet]
        public async Task<IActionResult> View(int id)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return RedirectToAction("Login", "Auth");

            var schedule = await _context.Schedules.FindAsync(id);
            if (schedule == null) return NotFound();

            // Get ALL endorsements for this game
            var allEndorsements = await _context.Endorsements
                .Include(e => e.ReceiverUser) // Need the receiver info
                .Include(e => e.GiverUser) // Need the giver info
                .Where(e => e.ScheduleId == id)
                .ToListAsync();

            // Get IDs of users *I* personally endorsed in this game
            var myEndorsedUserIds = allEndorsements
                .Where(e => e.GiverUserId == currentUserId.Value)
                .Select(e => e.ReceiverUserId)
                .Distinct()
                .ToList();

            // === 1. Process SKILL groups (for "Skill" tab) ===
            var skillGroups = allEndorsements
                .Where(e => e.Skill != SkillEndorsement.None)
                .GroupBy(e => e.Skill) // Group by "Dink", "Volley", etc.
                .Select(g => new EndorsementGroup
                {
                    EndorsementName = g.Key.ToString(),
                    Recipients = g
                        .GroupBy(e => e.ReceiverUser) // Group by *who* received it
                        .Select(userGroup => new EndorsementRecipient
                        {
                            UserId = userGroup.Key!.UserId,
                            Username = userGroup.Key.Username,
                            ProfilePicture = userGroup.Key.ProfilePicture,
                            Count = userGroup.Count(),
                            IsCurrentUser = userGroup.Key.UserId == currentUserId.Value,
                            // Check if *my* ID is in the list of people who gave this user this skill
                            IsEndorsedByMe = userGroup.Any(e => e.GiverUserId == currentUserId.Value)
                        })
                        .OrderByDescending(r => r.IsCurrentUser)
                        .ThenByDescending(r => r.Count)
                        .ToList()
                })
                .OrderBy(g => g.EndorsementName)
                .ToList();

            // === 2. Process PERSONALITY groups (for "Personality" tab) ===
            var personalityGroups = allEndorsements
                .Where(e => e.Personality != PersonalityEndorsement.None)
                .GroupBy(e => e.Personality)
                .Select(g => new EndorsementGroup
                {
                    EndorsementName = g.Key.ToString(),
                    Recipients = g
                        .GroupBy(e => e.ReceiverUser)
                        .Select(userGroup => new EndorsementRecipient
                        {
                            UserId = userGroup.Key!.UserId,
                            Username = userGroup.Key.Username,
                            ProfilePicture = userGroup.Key.ProfilePicture,
                            Count = userGroup.Count(),
                            IsCurrentUser = userGroup.Key.UserId == currentUserId.Value,
                            IsEndorsedByMe = userGroup.Any(e => e.GiverUserId == currentUserId.Value)
                        })
                        .OrderByDescending(r => r.IsCurrentUser)
                        .ThenByDescending(r => r.Count)
                        .ToList()
                })
                .OrderBy(g => g.EndorsementName)
                .ToList();

            // === 3. Process "My Awards" (for "My Awards" tab) ===
            var myAwards = allEndorsements
                .Where(e => e.ReceiverUserId == currentUserId.Value)
                .ToList();

            var mySkillAwards = myAwards
                .Where(e => e.Skill != SkillEndorsement.None)
                .GroupBy(e => e.Skill)
                .Select(g => new EndorsementSummary { EndorsementName = g.Key.ToString(), Count = g.Count() })
                .OrderByDescending(s => s.Count)
                .ToList();
                
            var myPersonalityAwards = myAwards
                .Where(e => e.Personality != PersonalityEndorsement.None)
                .GroupBy(e => e.Personality)
                .Select(g => new EndorsementSummary { EndorsementName = g.Key.ToString(), Count = g.Count() })
                .OrderByDescending(s => s.Count)
                .ToList();


            var vm = new ViewEndorsementViewModel
            {
                ScheduleId = id,
                ScheduleName = schedule.GameName ?? "Game",
                SkillGroups = skillGroups,
                PersonalityGroups = personalityGroups,
                MySkillAwards = mySkillAwards, // <-- ADDED
                MyPersonalityAwards = myPersonalityAwards // <-- ADDED
            };

            return View("~/Views/Schedule/SeeEndorsement.cshtml", vm);
        }
    }
}