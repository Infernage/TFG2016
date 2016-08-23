using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using BusTrackWeb.TokenProvider;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Authorization;
using BusTrackWeb.Models;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using MailKit.Net.Smtp;

namespace BusTrackWeb.Controllers
{
    [Route("oauth/[action]")]
    public class OAuthController : Controller
    {
        private readonly OAuthOptions _options;
        private readonly ILogger _logger;
        private readonly JsonSerializerSettings _serializer;

        public OAuthController(IOptions<OAuthOptions> opts, ILoggerFactory logger)
        {
            _options = opts.Value;
            OAuthTokenProvider.ThrowIfInvalidOptions(_options);

            _logger = logger.CreateLogger<OAuthTokenProvider>();

            _serializer = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> Generate([FromForm] string username, [FromForm] string password)
        {
            if (!ModelState.IsValid) return BadRequest("Validation error");

            var response = await OAuthTokenProvider.GenerateToken(username, password, _options);
            if (response == null)
            {
                _logger.LogInformation($"Invalid username ({username}) or password");
                return BadRequest("Invalid credentials");
            }

            var json = JsonConvert.SerializeObject(response, _serializer);
            return new OkObjectResult(json);
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult Register([FromForm] string name, [FromForm] string email, [FromForm] string password)
        {
            if (!ModelState.IsValid) return BadRequest("Validation error");
            if (name == null || email == null || password == null) return BadRequest("All credentials must be filled");

            using (var context = new TFGContext())
            {
                // Check if email is unique
                var query = from us in context.User where email == us.email select us;
                if (query.Any()) return BadRequest("Email already in use");

                // Hash password hashed with a random salt
                byte[] salt = new byte[512 / 8];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }
                string hash = Convert.ToBase64String(KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA512, 10000, 512 / 8));

                // Create a new user into DB
                User user = new User
                {
                    email = email,
                    hash = new StringBuilder(Convert.ToBase64String(salt)).Append(":").Append(hash).ToString(),
                    name = name,
                    confirmed = false
                };
                context.User.Add(user);
                context.SaveChanges();

                // Generate email token
                string code;
                long validTo = OAuthTokenProvider.ToUnixEpochDate(DateTime.UtcNow.AddDays(1));
                using (var hmac = new HMACSHA256(salt))
                {
                    code = Base64UrlEncoder.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Concat(user.id, user.email, user.name, validTo))));
                }
                var url = Url.Action("Confirm", "Account", new { userId = user.id, code = code, exp = validTo }, protocol: HttpContext.Request.Scheme);

                // Send email
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress("BusTrack", "BusTrack@gmail.com"));
                msg.To.Add(new MailboxAddress(name, email));
                msg.Subject = "Confirm BusTrack account";
                var html = new BodyBuilder();
                html.HtmlBody = $"Please, confirm your account by clicking this link: <a href='{url}'>link</a>";
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

        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> Refresh([FromForm] string token)
        {
            if (!ModelState.IsValid) return BadRequest("Validation error");
            // Refresh token is always 32 byte length!
            if (token.Length != 32) return BadRequest("Invalid refresh token");

            var response = await OAuthTokenProvider.Refresh(token, _options);
            if (response == null)
            {
                return BadRequest("Invalid refresh token");
            }

            var json = JsonConvert.SerializeObject(response, _serializer);
            return new OkObjectResult(json);
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult> Revoke([FromForm] string token)
        {
            await OAuthTokenProvider.Revoke(token);
            return Ok(); // Always success even with an invalid token
        }
    }
}
