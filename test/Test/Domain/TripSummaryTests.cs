using AwtrixSharpWeb.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test.Domain
{
    internal class TripSummaryTests
    {
        public static TripSummary Create(DateTimeOffset depart, DateTimeOffset arrive)
        {
            return new TripSummary
            {
                Origin = TimePlace.Factory(depart),
                Destination = TimePlace.Factory(arrive)
            };
        }

        public static TripSummary Create(DateTimeOffset depart)
        {
            return new TripSummary
            {
                Origin = TimePlace.Factory(depart),
                Destination = TimePlace.Factory(depart.AddMinutes(30))
            };
        }
    }
}
