using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using BusTrackWeb.TokenProvider;
using Microsoft.AspNetCore.Authorization;
using BusTrackWeb.Models;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;

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
        public async Task<ActionResult> Confirm([FromQuery] long userId, [FromQuery] string code, [FromQuery] long exp)
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

                // Generate OAuth token
                var json = JsonConvert.SerializeObject(await OAuthTokenProvider.GenerateToken(u.email, u.hash.Split(':')[1], _options, true), _serializer);
                return new OkObjectResult(json);
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
                var url = Url.Action("ResetPassword", "Account", new { userId = user.id, code = code, exp = validTo }, protocol: HttpContext.Request.Scheme);
                user.resetPass = true; // Mark in the DB
                context.SaveChanges();

                // Send email
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress("BusTrack", "BusTrack@gmail.com"));
                msg.To.Add(new MailboxAddress(user.name, email));
                msg.Subject = "Reset BusTrack password";
                var html = new BodyBuilder();
                html.HtmlBody = $"You can reset your password throughout this link: <a href='{url}'>link</a><br/>If you didn't ask for a reset, ignore this message.";
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
        public ActionResult ResetPassword([FromQuery] long userId, [FromQuery] string code, [FromQuery] long exp, [FromForm] string password, [FromForm] string confPassword, [FromForm] string hash)
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
                if (!nCode.Equals(code) || !u.resetPass) return BadRequest("Code expired");

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

                    return Ok();
                }
                else // Access from reset link
                {
                    // Set view data
                    ViewData["userId"] = userId;
                    ViewData["code"] = code;
                    ViewData["exp"] = exp;
                    return View();
                }
            }
        }
        #endregion Anonymous access

        [HttpPost]
        [Authorize]
        public ActionResult ChangeName([FromForm] string newName, [FromForm] string sig, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sig, user, context)) return BadRequest("Bad signature"); // Check password signature before change anything
                user.name = newName;
                context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        [Authorize]
        public ActionResult ChangeEmail([FromForm] string email, [FromForm] string sig, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sig, user, context)) return BadRequest("Bad signature"); // Check password signature before change anything
                user.email = email;
                context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        [Authorize]
        public ActionResult ChangePassword([FromForm] string password, [FromForm] string sig, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sig, user, context)) return BadRequest("Bad signature"); // Check password signature before change anything
                string salt = user.hash.Split(':')[0];
                user.hash = new StringBuilder(salt).Append(':').Append(PerformHash(salt, password)).ToString(); // Save hash with this scheme-> salt:hash
                context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        [Authorize]
        public ActionResult Delete([FromForm] string sig, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sig, user, context)) return BadRequest("Bad signature"); // Check password signature before change anything
                context.Remove(user);
                context.SaveChanges();
            }
            return Ok();
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult> GetStatistics([FromForm] string sig, [FromForm] long id)
        {
            using (var context = new TFGContext())
            {
                User user = GetUser(id, context);
                if (user == null) return BadRequest("Non existent user");
                if (!CheckSignature(sig, user, context)) return BadRequest("Bad signature"); // Check password signature before doing anything
            }

            // Since entity framework DBcontext is not thread-safe, we have to create each one in the threads
            Task<int> totalTravelsTask = Task.Factory.StartNew(() =>
            {
                // Total travels
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    return user.Travels.Count;
                }
            });
            Task<double> travelsDayTask = Task.Factory.StartNew(() =>
            {
                // Travels by day
                return new Statistics().MapReduceTravelsByDay(id);
            });
            Task<long> mostUsedLineTask = Task.Factory.StartNew(() =>
            {
                // Most used line
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).ThenInclude(t => t.Line).Where(u => u.id == id).First();
                    var query = user.Travels.Where(t => t.Line != null).GroupBy(t => t.lineId).OrderByDescending(l => l.Count());
                    return query.Any() ? query.First().Key : 0L;
                }
            });
            Task<double> averageDurationTask = Task.Factory.StartNew(() =>
            {
                // Average travel duration
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    return user.Travels.Select(t => t.time).Average();
                }
            });
            Task<int> longestDurationTask = Task.Factory.StartNew(() =>
            {
                // Longest travel duration
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    return user.Travels.Select(t => t.time).Max();
                }
            });
            Task<double> pollutionBusTask = Task.Factory.StartNew(() =>
            {
                // Saved pollution with a normal bus
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    double sub = Statistics.POLLUTION_CAR - Statistics.POLLUTION_BUS;
                    return user.Travels.Where(t => t.distance != 0).Select(t => t.distance).Aggregate(0D, (a, b) => a + (b - sub));
                }
            });
            Task<double> pollutionEBusTask = Task.Factory.StartNew(() =>
            {
                // Saved pollution with an electrical bus
                using (var context = new TFGContext())
                {
                    User user = context.User.Include(u => u.Travels).Where(u => u.id == id).First();
                    double sub = Statistics.POLLUTION_CAR - Statistics.POLLUTION_BUS_E;
                    return user.Travels.Where(t => t.distance != 0).Select(t => t.distance).Aggregate(0D, (a, b) => a + (b - sub));
                }
            });

            // Wait for all tasks
            await Task.WhenAll(totalTravelsTask, travelsDayTask, mostUsedLineTask, averageDurationTask, longestDurationTask, pollutionBusTask, pollutionEBusTask);

            // Create JSON object
            var json = new
            {
                totalTravels = totalTravelsTask.Result,
                travelsByDay = travelsDayTask.Result,
                mostUsedLine = mostUsedLineTask.Result,
                averageDuration = averageDurationTask.Result,
                longestDuration = longestDurationTask.Result,
                pollutionBus = pollutionBusTask.Result,
                pollutionElectricBus = pollutionEBusTask
            };
            return new OkObjectResult(JsonConvert.SerializeObject(json, _serializer));
        }

        [HttpPost]
        [Authorize]
        public ActionResult Sync([FromForm] string sig, [FromForm] long id, [FromForm] string obj)
        {

            return Ok();
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

        #endregion Helpers Methods
    }
}
