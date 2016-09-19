using BusTrackWeb.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BusTrackWeb
{
    /// <summary>
    /// Class in charge of all statistics functionality.
    /// </summary>
    internal class Statistics
    {
        public static readonly float POLLUTION_CAR = 119F, POLLUTION_BUS = 104F, POLLUTION_BUS_E = 18.6F;

        #region mapReduceTravelsDayWeek

        private ConcurrentBag<DayOfWeek> travelWBag = null;
        private BlockingCollection<DayOfWeek> travelWChunks = null;
        private ConcurrentDictionary<DayOfWeek, int> travelWStore = null;

        /// <summary>
        /// MapReduce method for get the most popular day of the week.
        /// </summary>
        /// <returns>The most popular day of the wek.</returns>
        internal int MapReduceTravelsDayWeek()
        {
            if (travelWChunks == null || travelWChunks.IsAddingCompleted)
            {
                travelWBag = new ConcurrentBag<DayOfWeek>();
                travelWChunks = new BlockingCollection<DayOfWeek>(travelWBag);
                travelWStore = new ConcurrentDictionary<DayOfWeek, int>();
            }

            ThreadPool.QueueUserWorkItem((o) =>
            {
                MapWTravels();
            });

            ReduceWTravels();

            return travelWStore.Count > 0 ? (int) travelWStore.Aggregate((a, b) => a.Value >= b.Value ? a : b).Key : -1;
        }

        /// <summary>
        /// Mapping function. Fills the blocking collection with days of the week.
        /// </summary>
        private void MapWTravels()
        {
            Parallel.ForEach(ProduceTravelsIDs(-1), id =>
            {
                using (var context = new TFGContext())
                {
                    var query = context.Travel.Where(t => t.id == id);
                    if (!query.Any()) return;

                    Travel travel = query.First();
                    travelWChunks.Add(travel.date.DayOfWeek);
                }
            });

            travelWChunks.CompleteAdding();
        }

        /// <summary>
        /// Reducing function. Fills the dictionary with dayOfWeek-numTimes pairs.
        /// </summary>
        private void ReduceWTravels()
        {
            Parallel.ForEach(travelWChunks.GetConsumingEnumerable(), day =>
            {
                travelWStore.AddOrUpdate(day, 1, (key, value) => Interlocked.Increment(ref value));
            });
        }

        #endregion

        #region mapReduceTravelsByDay

        private ConcurrentBag<DateTimeOffset> travelBag = null;
        private BlockingCollection<DateTimeOffset> travelChunks = null;
        private ConcurrentDictionary<DateTimeOffset, int> travelStore = null;

        /// <summary>
        /// MapReduce method for get travels/day more precise.
        /// </summary>
        /// <param name="id">The user ID or optional to indicate we want all travels.</param>
        /// <returns>The user/general travels by day.</returns>
        internal double MapReduceTravelsByDay(long id = -1)
        {
            if (travelChunks == null || travelChunks.IsAddingCompleted)
            {
                travelBag = new ConcurrentBag<DateTimeOffset>();
                travelChunks = new BlockingCollection<DateTimeOffset>(travelBag);
                travelStore = new ConcurrentDictionary<DateTimeOffset, int>();
            }

            ThreadPool.QueueUserWorkItem((o) =>
            {
                MapTravels(id);
            });

            ReduceTravels();

            // Sum all values
            double value = travelStore.Values.AsParallel().Sum();

            // Divide it with total count
            return Math.Round(value / (travelStore.Count == 0 ? 1 : travelStore.Count), 2);
        }

        /// <summary>
        /// Mapping function. Fills the blocking collection with dates.
        /// </summary>
        /// <param name="userId">The user ID or -1 to indicate all travels.</param>
        private void MapTravels(long userId)
        {
            Parallel.ForEach(ProduceTravelsIDs(userId), id =>
            {
                using (var context = new TFGContext())
                {
                    var query = context.Travel.Where(t => t.id == id);
                    if (!query.Any()) return;

                    Travel travel = query.First();
                    travelChunks.Add(travel.date.Date);
                }
            });

            travelChunks.CompleteAdding();
        }

        /// <summary>
        /// Reducing function. Fills the dictionary with date-numTimes pairs.
        /// </summary>
        private void ReduceTravels()
        {
            Parallel.ForEach(travelChunks.GetConsumingEnumerable(), day =>
            {
                travelStore.AddOrUpdate(day, 1, (key, value) => Interlocked.Increment(ref value));
            });
        }

        /// <summary>
        /// Source of mapping function.
        /// </summary>
        /// <param name="id">The user ID or -1 to indicate all travels.</param>
        /// <returns>Gets all travels IDs associated with the user.</returns>
        private IEnumerable<long> ProduceTravelsIDs(long id)
        {
            using (var context = new TFGContext())
            {
                List<int> res = new List<int>();
                var all = id != -1 ? context.Travel.Where(t => t.userId == id) : context.Travel;
                foreach (Travel t in all)
                {
                    yield return t.id;
                }
            }
        }

        #endregion mapReduceTravelsByDay
    }
}