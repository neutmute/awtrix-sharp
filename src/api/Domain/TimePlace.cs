namespace AwtrixSharpWeb.Domain
{
    public class TimePlace
    {
        public DateTimeOffset Time { get; set; }

        public string Place { get; set; }

        public TimePlace()
        {
            Time = DateTimeOffset.Parse("2000-01-01");
            Place = string.Empty;
        }

        public TimePlace AsRounded()
        {
            DateTimeOffset RoundToNearestMinute(DateTimeOffset d) => d.AddSeconds(-d.Second);
            return new TimePlace
            {
                Time = RoundToNearestMinute(Time),
                Place = Place
            };
        }

        public override string ToString() => $"{Time:HH:mm} {Place}";

        public static TimePlace Factory(DateTimeOffset time, string place = "")
        {
            return new TimePlace
            {
                Time = time,
                Place = place
            };  
        }
    }
}
