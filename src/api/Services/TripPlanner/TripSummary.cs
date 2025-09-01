namespace AwtrixSharpWeb.Services.TripPlanner
{
    public class TripSummary
    {
        public TimePlace Origin { get; set; }

        public TimePlace Destination { get; set; }

        public TimeSpan TravelTime => Destination.Time - Origin.Time;

        public TripSummary AsRounded()
        {
            return new TripSummary
            {
                Origin = Origin.AsRounded(),
                Destination = Destination.AsRounded()
            };
        }

        public override string ToString()
        {
            return $"{Origin} -> {Destination} ({TravelTime:mm} mins)";
        }

        public static TripSummary Factory(DateTimeOffset time, string place = "")
        {
            return new TripSummary
            {
                Origin = TimePlace.Factory(time),
                Destination = new TimePlace()
            };
        }

    }
}
