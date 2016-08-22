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
        public async Task<ActionResult> Confirm([FromQuery] long userId, [FromQuery] string code)
        {
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
                    nCode = Base64UrlEncoder.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Concat(u.id, u.email, u.name))));
                }
                if (!nCode.Equals(code)) return BadRequest("Code expired");

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
            // TODO: Forgot password
            return Ok();
        }

        [HttpGet]
        [AllowAnonymous]
        public ActionResult ResetPassword([FromQuery] long userId, [FromQuery] string code)
        {
            // TODO: Reset password
            return Ok();
        }
    }
}
