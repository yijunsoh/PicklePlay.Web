using PicklePlay.Models;
using System.Collections.Generic;

namespace PicklePlay.Models
{
    public class MyGameViewModel
    {
        public List<Schedule> ActiveGames { get; set; } = new List<Schedule>();
        public List<Schedule> HistoryGames { get; set; } = new List<Schedule>();
        
        // These are the properties for your new tabs
        public List<Schedule> HiddenGames { get; set; } = new List<Schedule>();
        public List<Schedule> BookmarkedGames { get; set; } = new List<Schedule>();
    }
}

