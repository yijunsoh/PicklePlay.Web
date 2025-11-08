using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using PicklePlay.Models;
using PicklePlay.Models.ViewModels;
using System.Linq;
using System.Threading.Tasks;

namespace PicklePlay.ViewComponents
{
    public class EndorsementSectionViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public EndorsementSectionViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync(int userId)
        {
            var allReceived = await _context.Endorsements
                .Where(e => e.ReceiverUserId == userId)
                .ToListAsync();

            var allSkills = allReceived
                .Where(e => e.Skill != SkillEndorsement.None)
                .GroupBy(e => e.Skill)
                .Select(g => new EndorsementSummary { EndorsementName = g.Key.ToString(), Count = g.Count() })
                .OrderByDescending(s => s.Count)
                .ToList();

            var allPersonalities = allReceived
                .Where(e => e.Personality != PersonalityEndorsement.None)
                .GroupBy(e => e.Personality)
                .Select(g => new EndorsementSummary { EndorsementName = g.Key.ToString(), Count = g.Count() })
                .OrderByDescending(s => s.Count)
                .ToList();

            var vm = new ProfileEndorsementViewModel
            {
                AllSkills = allSkills,
                TopSkills = allSkills.Take(3).ToList(),
                AllPersonalities = allPersonalities,
                TopPersonalities = allPersonalities.Take(3).ToList()
            };

            return View("~/Views/Shared/_EndorsementSection.cshtml", vm);
        }
    }
}