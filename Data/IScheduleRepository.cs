using PicklePlay.Models;

namespace PicklePlay.Data
{
    public interface IScheduleRepository
    {
        IEnumerable<Schedule> All();
        Schedule? GetById(int id);
        void Add(Schedule schedule);
        // --- ADD THESE ---
        void Update(Schedule schedule);
        void Delete(int id);
        // --- END ADD ---
    }
}