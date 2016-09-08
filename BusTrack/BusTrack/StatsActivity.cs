using Android.App;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using BusTrack.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace BusTrack
{
    [Activity(Label = "Estadísticas")]
    public class StatsActivity : Activity
    {
        private TableLayout table;
        private TableRow.LayoutParams paramL, paramR;

        protected async override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);

            SetContentView(Resource.Layout.Stats);

            MenuInitializer.InitMenu(this);

            // Create a progress dialog meanwhile we retrieve user stats
            ProgressDialog dialog = new ProgressDialog(this);
            dialog.Indeterminate = true;
            dialog.SetProgressStyle(ProgressDialogStyle.Spinner);
            dialog.SetMessage("Recogiendo estadísticas...");
            dialog.SetCancelable(false);
            dialog.Show();

            // Init UI
            table = FindViewById<TableLayout>(Resource.Id.tableLayout1);
            paramL = new TableRow.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent);
            paramR = new TableRow.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent);
            paramL.Weight = 1;
            paramR.Weight = 0;
            var totalTravels = InitUI("Viajes totales");
            var travelsDay = InitUI("Viajes por día");
            var mostUsedLine = InitUI("Línea más usada");
            var averageDuration = InitUI("Duración media");
            var longestDuration = InitUI("Duración más larga");
            var pollutionBus = InitUI("Contaminación ahorrada (Bus normal)"); // g CO2/km
            var pollutionEBus = InitUI("Contaminación ahorrada (Bus eléctrico)"); // g CO2/km

            // Request stats
            await Task.Run(async () =>
                {
                    try
                    {
                        string resp = await Utils.GetStatistics(this);
                        if (resp.Length == 0) return;

                        var json = JObject.Parse(resp);
                        RunOnUiThread(() =>
                        {
                            totalTravels.Item2.Text = json["totalTravels"].ToString();
                            travelsDay.Item2.Text = json["travelsByDay"].ToString();
                            mostUsedLine.Item2.Text = json["mostUsedLine"].ToString();
                            averageDuration.Item2.Text = json["averageDuration"].ToString();
                            longestDuration.Item2.Text = json["longestDuration"].ToString();
                            pollutionBus.Item2.Text = json["pollutionBus"].ToString() + "g CO2/km";
                            pollutionEBus.Item2.Text = json["pollutionElectricBus"].ToString() + "g CO2/km";
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Error(Utils.NAME_PREF, Java.Lang.Throwable.FromException(ex), "Failed to get stats!");
                        RunOnUiThread(() => Toast.MakeText(this, Resource.String.SesExp, ToastLength.Long).Show());
                        Finish();
                        StartActivity(typeof(LoginActivity));
                    }
                });

            dialog.Dismiss();
        }

        /// <summary>
        /// Creates 2 text views and adds it to the TableLayout.
        /// </summary>
        /// <param name="text">The text to show on the left text view.</param>
        /// <returns>A tuple with both text views.</returns>
        private Tuple<TextView, TextView> InitUI(string text)
        {
            TextView right = new TextView(this), left = new TextView(this);
            left.Text = text;
            left.LayoutParameters = paramL;
            right.LayoutParameters = paramR;

            TableRow totalTravelsRow = new TableRow(this);
            totalTravelsRow.AddView(left);
            totalTravelsRow.AddView(right);
            table.AddView(totalTravelsRow);

            return new Tuple<TextView, TextView>(left, right);
        }
    }
}