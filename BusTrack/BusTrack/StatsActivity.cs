using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Realms;
using BusTrack.Utilities;
using BusTrack.Data;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace BusTrack
{
    [Activity(Label = "Estadísticas")]
    public class StatsActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);
            //ActionBar.SetBackgroundDrawable(new ColorDrawable(Color.LightBlue));

            SetContentView(Resource.Layout.Stats);

            MenuInitializer.InitMenu(this);

            using (Realm realm = Realm.GetInstance(Utils.NAME_PREF))
            {
                var travels = realm.All<Travel>(); // TODO: Use with user ID

                TableLayout table = FindViewById<TableLayout>(Resource.Id.tableLayout1);
                TableRow.LayoutParams paramL = new TableRow.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent),
                    paramR = new TableRow.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent);
                paramL.Weight = 1;
                paramR.Weight = 0;

                // Total travels
                TextView totalTravels = new TextView(this), totalTravelsL = new TextView(this);
                totalTravelsL.Text = "Viajes totales";
                totalTravels.Text = travels.Count().ToString();
                totalTravelsL.LayoutParameters = paramL;
                totalTravels.LayoutParameters = paramR;
                TableRow totalTravelsRow = new TableRow(this);
                totalTravelsRow.AddView(totalTravelsL);
                totalTravelsRow.AddView(totalTravels);
                table.AddView(totalTravelsRow);

                // Travels by day
                TextView travelsDay = new TextView(this), travelsDayL = new TextView(this);
                travelsDayL.Text = "Viajes por día";
                travelsDay.Text = mapReduce().ToString();
                travelsDayL.LayoutParameters = paramL;
                travelsDay.LayoutParameters = paramR;
                TableRow totalTravelsDayRow = new TableRow(this);
                totalTravelsDayRow.AddView(travelsDayL);
                totalTravelsDayRow.AddView(travelsDay);
                table.AddView(totalTravelsDayRow);

                // Most used line
                TextView mostUsedLine = new TextView(this), mostUsedLineL = new TextView(this);
                mostUsedLineL.Text = "Línea más usada";
                {
                    string mostUsedL;
                    var mostUsedLines = travels.ToList().Where(t => t.line != null).GroupBy(t => t.line).OrderByDescending(l => l.Count());//.Take(1).Select(l => l.Key);
                    if (mostUsedLines.Count() == 0) mostUsedL = "N/A";
                    else mostUsedL = "Línea " + mostUsedLines.First().Key.id;
                }
                mostUsedLineL.LayoutParameters = paramL;
                mostUsedLine.LayoutParameters = paramR;
                TableRow mostUsedLineRow = new TableRow(this);
                mostUsedLineRow.AddView(mostUsedLineL);
                mostUsedLineRow.AddView(mostUsedLine);
                table.AddView(mostUsedLineRow);

                // Average travel duration
                TextView averageDuration = new TextView(this), averageDurationL = new TextView(this);
                averageDurationL.Text = "Duración media";
                {
                    float value = 0;
                    int n = 0;
                    foreach (Travel t in travels)
                    {
                        if (t.time != 0)
                        {
                            value += t.time;
                            n++;
                        }
                    }

                    if (value != 0) averageDuration.Text = Math.Round(value / 60, 2).ToString() + " minutos";
                    else averageDuration.Text = "N/A";
                }
                averageDurationL.LayoutParameters = paramL;
                averageDuration.LayoutParameters = paramR;
                TableRow averageDurationRow = new TableRow(this);
                averageDurationRow.AddView(averageDurationL);
                averageDurationRow.AddView(averageDuration);
                table.AddView(averageDurationRow);

                // Longest travel duration
                TextView longestDuration = new TextView(this), longestDurationL = new TextView(this);
                longestDurationL.Text = "Duración más larga";
                {
                    var orderer = travels.OrderByDescending(t => t.time).ToList();
                    if (orderer.Count > 0) longestDuration.Text = Math.Round(((float)orderer.First().time) / 60, 2).ToString() + " minutos";
                    else longestDuration.Text = "N/A";
                }
                longestDurationL.LayoutParameters = paramL;
                longestDuration.LayoutParameters = paramR;
                TableRow longestDurationRow = new TableRow(this);
                longestDurationRow.AddView(longestDurationL);
                longestDurationRow.AddView(longestDuration);
                table.AddView(longestDurationRow);

                // Saved pollution with a normal bus
                TextView pollutionBus = new TextView(this), pollutionBusL = new TextView(this);
                pollutionBusL.Text = "Contaminación ahorrada (Bus normal)";
                {
                    float substract = Utils.POLLUTION_CAR - Utils.POLLUTION_BUS;
                    float value = 0;
                    foreach (Travel t in travels)
                    {
                        if (t.distance != 0) value += t.distance * substract;
                    }

                    pollutionBus.Text = value.ToString() + "g CO2/km";
                }
                pollutionBusL.LayoutParameters = paramL;
                pollutionBus.LayoutParameters = paramR;
                TableRow pollutionBusRow = new TableRow(this);
                pollutionBusRow.AddView(pollutionBusL);
                pollutionBusRow.AddView(pollutionBus);
                table.AddView(pollutionBusRow);

                // Saved pollution with an electrical bus
                TextView pollutionBusElectric = new TextView(this), pollutionBusElectricL = new TextView(this);
                pollutionBusElectricL.Text = "Contaminación ahorrada (Bus eléctrico)";
                {
                    float substract = Utils.POLLUTION_CAR - Utils.POLLUTION_BUS_E;
                    float value = 0;
                    foreach (Travel t in travels)
                    {
                        if (t.distance != 0) value += t.distance * substract;
                    }

                    pollutionBusElectric.Text = value.ToString() + "g CO2/km";
                }
                pollutionBusElectricL.LayoutParameters = paramL;
                pollutionBusElectric.LayoutParameters = paramR;
                TableRow pollutionBusElectricRow = new TableRow(this);
                pollutionBusElectricRow.AddView(pollutionBusElectricL);
                pollutionBusElectricRow.AddView(pollutionBusElectric);
                table.AddView(pollutionBusElectricRow);
            }
        }

        #region mapReduce

        private ConcurrentBag<DateTimeOffset> travelBag = null;
        private BlockingCollection<DateTimeOffset> travelChunks = null;
        private ConcurrentDictionary<DateTimeOffset, int> travelStore = null;

        private double mapReduce()
        {
            if (travelChunks == null || travelChunks.IsAddingCompleted)
            {
                travelBag = new ConcurrentBag<DateTimeOffset>();
                travelChunks = new BlockingCollection<DateTimeOffset>(travelBag);
                travelStore = new ConcurrentDictionary<DateTimeOffset, int>();
            }

            ThreadPool.QueueUserWorkItem((o) =>
            {
                mapDays();
            });

            reduceDays();

            // Sum all values
            double value = 0;
            foreach (int n in travelStore.Values)
            {
                value += n;
            }

            // Divide it with total count
            return Math.Round(value / (travelStore.Count == 0 ? 1 : travelStore.Count), 2);
        }

        private void mapDays()
        {
            Parallel.ForEach(produceTravelIds(), id =>
            {
                using (Realm realm = Realm.GetInstance(Utils.NAME_PREF))
                {
                    var res = realm.All<Travel>().Where(t => t.id == id);
                    if (res.Count() == 0) return;

                    Travel travel = res.First();
                    travelChunks.Add(travel.date.Date);
                }
            });

            travelChunks.CompleteAdding();
        }

        private void reduceDays()
        {
            Parallel.ForEach(travelChunks.GetConsumingEnumerable(), day =>
            {
                travelStore.AddOrUpdate(day, 1, (key, value) => Interlocked.Increment(ref value));
            });
        }

        private IEnumerable<int> produceTravelIds()
        {
            using (Realm realm = Realm.GetInstance(Utils.NAME_PREF))
            {
                List<int> res = new List<int>();
                var all = realm.All<Travel>();
                foreach (Travel t in all)
                {
                    //yield return t.id; -> Doesn't work in xamarin?
                    res.Add(t.id);
                }
                return res;
            }
        }

        #endregion mapReduce
    }
}