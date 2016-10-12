using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using BusTrack.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BusTrack
{
    [Activity(Label = "Estadísticas")]
    public class StatsActivity : Activity
    {
        private TableLayout table;
        private TableRow.LayoutParams paramL, paramR;
        private ProgressDialog dialog;
        private CancellationTokenSource cts;
        private TextView totalTravels, travelsDay, mostUsedLine, averageDuration, longestDuration,
             pollutionBus, pollutionEBus;

        protected async override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);

            SetContentView(Resource.Layout.Stats);

            MenuInitializer.InitMenu(this);

            FindViewById<Button>(Resource.Id.filterButton).Click += (o, e) =>
            {
                FragmentTransaction trans = FragmentManager.BeginTransaction();
                Fragment prev = FragmentManager.FindFragmentByTag("filterDiag");
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                new DatePickerFragment(async (f, t) =>
                {
                    cts = new CancellationTokenSource();
                    dialog.Show();

                    await Task.Run(async () =>
                    {
                        await RequestStats(f, t);
                    });

                    dialog.Dismiss();
                }).Show(trans, "filterDiag");
            };

            cts = new CancellationTokenSource();

            // Create a progress dialog meanwhile we retrieve user stats
            dialog = new ProgressDialog(this);
            dialog.Indeterminate = true;
            dialog.SetProgressStyle(ProgressDialogStyle.Spinner);
            dialog.SetMessage("Recogiendo estadísticas...");
            dialog.SetCancelable(true);
            dialog.CancelEvent += (o, e) =>
            {
                cts.Cancel();
                Toast.MakeText(this, "Cancelado", ToastLength.Long).Show();
            };
            dialog.Show();

            // Init UI
            table = FindViewById<TableLayout>(Resource.Id.tableLayout1);
            paramL = new TableRow.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent);
            paramR = new TableRow.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent);
            paramL.Weight = 1;
            paramR.Weight = 0;
            totalTravels = InitUI("Viajes totales");
            travelsDay = InitUI("Viajes por día");
            mostUsedLine = InitUI("Línea más usada");
            averageDuration = InitUI("Duración media de viaje");
            longestDuration = InitUI("Duración más larga de viaje");
            pollutionBus = InitUI("Contaminación ahorrada (Bus normal)"); // kg CO2/km
            pollutionEBus = InitUI("Contaminación ahorrada (Bus eléctrico)"); // kg CO2/km

            // Request stats
            await Task.Run(async () =>
            {
                await RequestStats();
            });

            dialog.Dismiss();
        }

        internal async Task RequestStats(DateTime? from = null, DateTime? to = null)
        {
            try
            {
                string resp = await AccountUtils.GetStatistics(this, cts.Token, from, to);
                if (resp.Length == 0) return;

                var json = JObject.Parse(resp);
                RunOnUiThread(() =>
                {
                    totalTravels.Text = json["totalTravels"].ToString();
                    travelsDay.Text = json["travelsByDay"].ToString();
                    mostUsedLine.Text = json["mostUsedLine"].ToObject<long>() != 0 ? "Línea " + json["mostUsedLine"].ToString() : "Ninguna";
                    averageDuration.Text = Math.Round(json["averageDuration"].ToObject<float>() / 60, 2)
                        .ToString() + " minutos";
                    longestDuration.Text = Math.Round(json["longestDuration"].ToObject<float>() / 60, 2)
                        .ToString() + " minutos";
                    pollutionBus.Text = json["pollutionBus"].ToString() + "kg CO2/km";
                    pollutionEBus.Text = json["pollutionElectricBus"].ToString() + "kg CO2/km";
                });
            }
            catch (Exception ex)
            {
                Log.Error(Utils.NAME_PREF, Java.Lang.Throwable.FromException(ex), "Failed to get stats!");
                RunOnUiThread(() => Toast.MakeText(this, Resource.String.SesExp, ToastLength.Long).Show());
                Finish();
                StartActivity(typeof(LoginActivity));
            }
        }

        /// <summary>
        /// Creates 2 text views and adds it to the TableLayout.
        /// </summary>
        /// <param name="text">The text to show on the left text view.</param>
        /// <returns>A tuple with both text views.</returns>
        private TextView InitUI(string text)
        {
            TextView right = new TextView(this), left = new TextView(this);
            left.Text = text;
            left.LayoutParameters = paramL;
            right.LayoutParameters = paramR;

            TableRow totalTravelsRow = new TableRow(this);
            totalTravelsRow.AddView(left);
            totalTravelsRow.AddView(right);
            table.AddView(totalTravelsRow);

            return right;
        }
    }

    internal class DatePickerFragment : DialogFragment, DatePicker.IOnDateChangedListener
    {
        private Action<DateTime, DateTime> callback;
        private DatePicker fromPicker, toPicker;
        private DateTime from = DateTime.Now, to = DateTime.Now;

        public DatePickerFragment(Action<DateTime, DateTime> action)
        {
            callback = action;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            base.OnCreateDialog(savedInstanceState);

            // Create dialog
            AlertDialog dialog;
            var builder = new AlertDialog.Builder(Activity);
            builder.SetView(Resource.Layout.DateFilter);
            builder.SetTitle("Filtrar por fecha");
            builder.SetPositiveButton("Aceptar", (EventHandler<DialogClickEventArgs>)null);
            builder.SetNegativeButton("Cancelar", (o, e) => Dismiss());

            dialog = builder.Create();
            dialog.Show();

            fromPicker = dialog.FindViewById<DatePicker>(Resource.Id.datePicker1);
            toPicker = dialog.FindViewById<DatePicker>(Resource.Id.datePicker2);

            fromPicker.Init(DateTime.Now.Year, DateTime.Now.Month - 1, DateTime.Now.Day, this);
            toPicker.Init(DateTime.Now.Year, DateTime.Now.Month - 1, DateTime.Now.Day, this);

            Button accept = dialog.GetButton((int)DialogButtonType.Positive);
            accept.Click += (o, e) =>
            {
                callback(from, to);
                dialog.Dismiss();
            };

            return dialog;
        }

        public void OnDateChanged(DatePicker view, int year, int monthOfYear, int dayOfMonth)
        {
            if (view.Equals(fromPicker)) from = new DateTime(year, monthOfYear + 1, dayOfMonth);
            else to = new DateTime(year, monthOfYear + 1, dayOfMonth);
        }
    }
}