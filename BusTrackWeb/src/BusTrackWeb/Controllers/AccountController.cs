using BusTrackWeb.Models;
using BusTrackWeb.TokenProvider;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BusTrackWeb.Controllers
{
    [Route("account/[action]")]
    public class AccountController : Controller
    {
        private readonly OAuthOptions _options;
        private readonly ILogger _logger;
        private readonly JsonSerializerSettings _serializer;

        public AccountController(IOptions<OAuthOptions> opts, ILoggerFactory logger)
        {
            _options = opts.Value;
            OAuthTokenProvider.ThrowIfInvalidOptions(_options);

            _logger = logger.CreateLogger<OAuthTokenProvider>();

            _serializer = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
        }

        #region Anonymous access

        [HttpGet]
        [AllowAnonymous]
        public ActionResult Confirm([FromQuery] long userId, [FromQuery] string code, [FromQuery] long exp)
        {
            if (!ModelState.IsValid) return BadRequest("Validation error");
            DateTime validTo = OAuthTokenProvider.FromUnixEpochDate(exp);
            if (validTo - DateTime.UtcNow <= TimeSpan.Zero) return BadRequest("Code expired");

            using (var context = new TFGContext())
            {
                // Check if user exists
                var query = from us in context.User where userId == us.id select us;
                if (!query.Any()) return BadRequest("Code expired");

                // Check if code is correct
                User u = query.First();
                string nCode;
                using (var hmac = new HMACSHA256(Convert.FromBase64String(u.hash.Split(':')[0])))
                {
                    nCode = Base64UrlEncoder.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Concat(u.id, u.email, u.name, exp))));
                }
                if (!nCode.Equals(code) || u.confirmed) return BadRequest("Code expired");

                // Confirm user
                u.confirmed = true;
                context.SaveChanges();

                ViewData["title"] = "Confirmación correcta";
                ViewData["msg"] = "Tu cuenta ha sido confirmada. Ya puedes utilizar la aplicación móvil.";
                return View("Confirm");
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult ForgotPassword([FromForm] string email)
        {
            if (!ModelState.IsValid) return BadRequest("Validation error");
            if (email == null || !email.Contains("@")) return BadRequest("Email is not valid");

            using (var context = new TFGContext())
            {
                // Check if email exists
                var query = from us in context.User where email == us.email select us;
                if (!query.Any()) return Ok(); // Don't let anyone knows if an email exists in the DB or not!

                User user = query.First();
                byte[] salt = Convert.FromBase64String(user.hash.Split(':')[0]);

                // Generate email token
                string code;
                long validTo = OAuthTokenProvider.ToUnixEpochDate(DateTime.UtcNow.AddDays(1));
                using (var hmac = new HMACSHA256(salt))
                {
                    code = Base64UrlEncoder.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Concat(user.id, user.email, user.name, validTo))));
                }
                string signature;
                using (var sha = SHA512.Create())
                {
                    signature = Base64UrlEncoder.Encode(sha.ComputeHash(Encoding.UTF8.GetBytes(user.email)));
                }
                var url = Url.Action("ResetPassword", "Account", new { userId = user.id, code = code, exp = validTo, sign = signature }, protocol: HttpContext.Request.Scheme);
                user.resetPass = true; // Mark in the DB
                context.SaveChanges();

                // Send email
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress("BusTrack", "BusTrack@gmail.com"));
                msg.To.Add(new MailboxAddress(user.name, email));
                msg.Subject = "Reset de contraseña BusTrack solicitada";
                var html = new BodyBuilder();
                html.HtmlBody = $"Puedes resetear tu contraseña a través de este link:<br/><a href='{url}'>{url}</a><br/>Si no has pedido el reseteo de tu contraseña, simplemente ignora este mensaje.";
                msg.Body = html.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    client.Connect("smtp.gmail.com", 587);

                    // Disable OAuth2 for this client
                    client.AuthenticationMechanisms.Remove("XOAUTH2");
                    // TODO: Input password or use an alternative mail server
                    /*client.Authenticate("BusTrack", "");
                    client.Send(msg);*/
                    client.Disconnect(true);
                }
            }

            return Ok();
        }

        [HttpGet]
        [AllowAnonymous]
        public ActionResult ResetPassword([FromQuery] long userId, [FromQuery] string code, [FromQuery] long exp,
            [FromQuery] string sign)
        {
            if (!ModelState.IsValid) return GenValidation();
            DateTime validTo = OAuthTokenProvider.FromUnixEpochDate(exp);
            if (validTo - DateTime.UtcNow <= TimeSpan.Zero) return GenExpired();

            using (var context = new TFGContext())
            {
                // Check if user exists
                var query = from us in context.User where userId == us.id select us;
                if (!query.Any()) return GenExpired();

                // Check if code is correct
                User u = query.First();
                string nCode;
                using (var hmac = new HMACSHA256(Convert.FromBase64String(u.hash.Split(':')[0])))
                {
                    nCode = Base64UrlEncoder.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Concat(u.id, u.email, u.name, exp))));
                }
                if (!nCode.Equals(code) || !u.resetPass) return GenExpired();

                // Set view data
                ViewData["userId"] = userId;
                ViewData["code"] = code;
                ViewData["exp"] = exp;
                ViewData["sign"] = sign;
                return View();
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult ResetPassword([FromForm] long userId, [FromForm] string code, [FromForm] long exp, [FromForm] string password, [FromForm] string confPassword,
            [FromForm] string hash, [FromForm] string sign)
        {
            if (!ModelState.IsValid) return GenValidation();
            DateTime validTo = OAuthTokenProvider.FromUnixEpochDate(exp);
            if (validTo - DateTime.UtcNow <= TimeSpan.Zero) return GenExpired();

            using (var context = new TFGContext())
            {
                // Check if user exists
                var query = from us in context.User where userId == us.id select us;
                if (!query.Any()) return GenExpired();

                // Check if code is correct
                User u = query.First();
                string nCode;
                using (var hmac = new HMACSHA256(Convert.FromBase64String(u.hash.Split(':')[0])))
                {
                    nCode = Base64UrlEncoder.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Concat(u.id, u.email, u.name, exp))));
                }
                if (!nCode.Equals(code) || !u.resetPass) return GenExpired();

                // Access from view
                if (password != null && confPassword != null && password.Equals(confPassword))
                {
                    // Hash password hashed with a random salt
                    byte[] salt = new byte[512 / 8];
                    using (var rng = RandomNumberGenerator.Create())
                    {
                        rng.GetBytes(salt);
                    }
                    string nhash = Convert.ToBase64String(KeyDerivation.Pbkdf2(hash, salt, KeyDerivationPrf.HMACSHA512, 10000, 512 / 8));
                    u.hash = new StringBuilder(Convert.ToBase64String(salt)).Append(":").Append(nhash).ToString();
                    u.resetPass = false;
                    context.SaveChanges();

                    ViewData.Clear();
                    ViewData["title"] = "Contraseña reseteada";
                    ViewData["msg"] = "Tu contraseña ha sido reseteada correctamente.";
                    return View("Confirm");
                }
                return GenValidation();
            }
        }

        private ActionResult GenExpired()
        {
            ViewData["type"] = "error";
            ViewData["title"] = "Código expirado";
            ViewData["msg"] = "El link que intentas acceder ha caducado.";
            return View("Confirm");
        }

        private ActionResult GenValidation()
        {
            ViewData["type"] = "error";
            ViewData["title"] = "Error de validación";
            return View("Confirm");
        }

        #endregion Anonymous access

        [HttpPost]
        [Authorize]
        public ActionResult ChangeName([FromForm] string name, [FromForm] string sign, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sign, user, context)) return BadRequest("Bad signature"); // Check password signature before change anything
                user.name = name;
                context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        [Authorize]
        public ActionResult ChangeEmail([FromForm] string email, [FromForm] string sign, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sign, user, context)) return BadRequest("Bad signature"); // Check password signature before change anything
                user.email = email;
                context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        [Authorize]
        public ActionResult ChangePassword([FromForm] string password, [FromForm] string sign, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sign, user, context)) return BadRequest("Bad signature"); // Check password signature before change anything
                string salt = user.hash.Split(':')[0];
                user.hash = new StringBuilder(salt).Append(':').Append(PerformHash(salt, password)).ToString(); // Save hash with this scheme-> salt:hash
                context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        [Authorize]
        public ActionResult Delete([FromForm] string sign, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sign, user, context)) return BadRequest("Bad signature"); // Check password signature before change anything
                context.Remove(user);
                context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult> GetStatistics([FromForm] long id, [FromForm] DateTime? from = null, [FromForm] DateTime? to = null)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
            }

            // Since entity framework DBcontext is not thread-safe, we have to create each one in the threads
            Task<int> totalTravelsTask = Task.Factory.StartNew(() =>
            {
                // Total travels
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    return user.Travels.Where(t => Statistics.IsInRange(t.date, from, to)).Count();
                }
            });
            Task<double> travelsDayTask = Task.Factory.StartNew(() =>
            {
                // Travels by day
                return new Statistics().MapReduceTravelsByDay(from, to, id);
            });
            Task<long> mostUsedLineTask = Task.Factory.StartNew(() =>
            {
                // Most used line
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).ThenInclude(t => t.Line).Where(u => u.id == id).First();
                    var query = user.Travels.Where(t => t.Line != null && Statistics.IsInRange(t.date, from, to)).GroupBy(t => t.lineId).OrderByDescending(l => l.Count());
                    return query.Any() ? query.First().Key : 0L;
                }
            });
            Task<double> averageDurationTask = Task.Factory.StartNew(() =>
            {
                // Average travel duration
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    var query = user.Travels.Where(t => Statistics.IsInRange(t.date, from, to)).Select(t => t.time);
                    return query.Any() ? query.Average() : 0;
                }
            });
            Task<int> longestDurationTask = Task.Factory.StartNew(() =>
            {
                // Longest travel duration
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    var query = user.Travels.Where(t => Statistics.IsInRange(t.date, from, to)).Select(t => t.time);
                    return query.Any() ? query.Max() : 0;
                }
            });
            Task<double> pollutionBusTask = Task.Factory.StartNew(() =>
            {
                // Saved pollution with a normal bus
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    double sub = Statistics.POLLUTION_CAR - Statistics.POLLUTION_BUS;
                    var query = user.Travels.Where(t => t.distance != 0 && Statistics.IsInRange(t.date, from, to));
                    return query.Any() ? (query.Select(t => t.distance).ToList().Sum() * sub) / 1000000F : 0D;
                }
            });
            Task<double> pollutionEBusTask = Task.Factory.StartNew(() =>
            {
                // Saved pollution with an electrical bus
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    double sub = Statistics.POLLUTION_CAR - Statistics.POLLUTION_BUS_E;
                    var query = user.Travels.Where(t => t.distance != 0 && Statistics.IsInRange(t.date, from, to));
                    return query.Any() ? (query.Select(t => t.distance).ToList().Sum() * sub) / 1000000F : 0D;
                }
            });

            // Wait for all tasks
            await Task.WhenAll(totalTravelsTask, travelsDayTask, mostUsedLineTask, averageDurationTask, longestDurationTask, pollutionBusTask, pollutionEBusTask);

            // Create JSON object
            var json = new
            {
                totalTravels = totalTravelsTask.Result,
                travelsByDay = Math.Round(travelsDayTask.Result, 2, MidpointRounding.AwayFromZero),
                mostUsedLine = mostUsedLineTask.Result,
                averageDuration = Math.Round(averageDurationTask.Result, 2, MidpointRounding.AwayFromZero),
                longestDuration = longestDurationTask.Result,
                pollutionBus = Math.Round(pollutionBusTask.Result, 2, MidpointRounding.AwayFromZero),
                pollutionElectricBus = Math.Round(pollutionEBusTask.Result, 2, MidpointRounding.AwayFromZero)
            };
            return new OkObjectResult(JsonConvert.SerializeObject(json, _serializer));
        }

        [HttpPost]
        [Authorize]
        public ActionResult AddTravel([FromBody] Travel item)
        {
            using (var context = new TFGContext())
            {
                if (item == null || item.initId == 0 || item.time == 0 || item.busId.Length == 0 ||
                    item.lineId == 0) return BadRequest();

                var ql = context.Line.Where(l => l.id == item.lineId);
                var qb = context.Bus.Where(b => b.mac.Equals(item.busId));
                var qi = context.Stop.Where(s => s.id == item.initId);
                var qe = context.Stop.Where(s => s.id == item.endId);
                var qu = context.User.Where(u => u.id == item.userId);

                if (ql.Any() && qb.Any() && qi.Any())
                {
                    Line line = ql.First();
                    Bus bus = qb.First();
                    Stop init = qi.First();
                    context.Travel.Add(item);

                    item.Line = line;
                    line.Travels.Add(item);
                    item.Bus = bus;
                    bus.Travels.Add(item);
                    item.Init = init;
                    init.InitialTravels.Add(item);

                    if (qe.Any())
                    {
                        Stop end = qe.First();
                        item.End = end;
                        end.EndingTravels.Add(item);
                    }

                    if (qu.Any())
                    {
                        User user = qu.First();
                        item.User = user;
                        user.Travels.Add(item);
                    }

                    context.SaveChanges();
                }
                else return BadRequest("Inexistent IDs found");

                return Ok();
            }
        }

        #region Helpers methods

        /// <summary>
        /// Checks if the given password signature is correct or not.
        /// </summary>
        /// <param name="signature">The password signature.</param>
        /// <param name="user">The User object in the DB.</param>
        /// <param name="context">The DB context.</param>
        /// <returns>True if the signature is valid or false in otherwise.</returns>
        private bool CheckSignature(string signature, User user, TFGContext context)
        {
            string hash = PerformHash(user.hash.Split(':')[0], signature);
            string sHash = user.hash.Split(':')[1];
            return hash.Equals(sHash);
        }

        /// <summary>
        /// Performs a hash with a key derivation HMAC SHA512.
        /// </summary>
        /// <param name="salt">The salt to apply.</param>
        /// <param name="password">The password to hash.</param>
        /// <returns>The hashed password.</returns>
        private string PerformHash(string salt, string password)
        {
            byte[] bSalt = Convert.FromBase64String(salt);
            return Convert.ToBase64String(KeyDerivation.Pbkdf2(password, bSalt, KeyDerivationPrf.HMACSHA512, 10000, 512 / 8));
        }

        /// <summary>
        /// Gets a User object through its ID.
        /// </summary>
        /// <param name="id">The user ID.</param>
        /// <param name="context">The DB context.</param>
        /// <returns>The User object or null if the ID doesn't exist.</returns>
        private User GetUser(long id, TFGContext context)
        {
            var query = from u in context.User where u.id == id select u;
            return query.Any() ? query.First() : null;
        }

        #endregion Helpers methods
    }
}