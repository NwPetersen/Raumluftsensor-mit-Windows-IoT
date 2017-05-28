using System;

namespace IoT_Gassensoren
{
    class DataPoint
    {
        public DateTimeOffset TimeOfEvent { get; set; }
        public double Value { get; set; }

    }
}
