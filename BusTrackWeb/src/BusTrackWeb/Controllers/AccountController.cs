using System;
using System.Collections.Generic;
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
    }
}
