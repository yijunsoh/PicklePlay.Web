using System.Collections.Generic;

namespace PicklePlay.Models
{
    public class MyGameViewModel
    {
        public List<Schedule> ActiveGames { get; set; }
        public List<Schedule> HistoryGames { get; set; }

        public MyGameViewModel()
        {
            ActiveGames = new List<Schedule>();
            HistoryGames = new List<Schedule>();
        }
    }
}
