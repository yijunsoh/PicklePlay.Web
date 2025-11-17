namespace PicklePlay.Models
{
    public enum RankStatus
    {
        NR,        // Not Rated
        Provisional, // 0-9 matches
        Reliable     // 10+ matches with good diversity
    }

    public enum MatchFormat
    {
        Singles,
        Doubles
    }

    public enum GameOutcome
    {
        Win,
        Loss
    }
}