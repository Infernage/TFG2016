using BusTrackWeb.Models;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace BusTrackWeb.TokenProvider
{
    internal class OAuthTokenProvider
    {
        internal static long ToUnixEpochDate(DateTime date) => (long)Math.Round((date.ToUniversalTime() - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds);

        internal static DateTime FromUnixEpochDate(long date) => new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(date);

        internal static ConcurrentStack<string> BlackList = new ConcurrentStack<string>();

        /// <summary>
        /// Revokes an OAuth token (JWT or refresh token).
        /// </summary>
        /// <param name="token">The token to be revoked.</param>
        internal async static Task Revoke(string token)
        {
            if (token == null) return;
            // GUID length is always 32!
            if (token.Length == 32)
            {
                // Refresh token
                using (var context = new TFGContext())
                {
                    // Check if token exists in DB
                    var query = context.User.Include(u => u.Token).Where(u => u.Token.id == token);
                    if (!query.Any()) return;

                    // Remove token from DB and from association with User
                    User user = query.First();
                    context.Remove(user.Token);
                    user.Token.User = null;
                    user.Token = null;
                    await context.SaveChangesAsync();
                }
            }
            else
            {
                // Access token
                try
                {
                    // Read token and check if has expired
                    var sToken = new JwtSecurityTokenHandler().ReadToken(token);
                    if ((sToken.ValidTo - DateTime.UtcNow) > TimeSpan.Zero)
                    {
                        // If not, add to the blacklist
                        BlackList.Push(token);
                    }
                }
                catch
                {
                    // Ignore exception. Only thrown when access token is invalid
                }
            }
        }

        /// <summary>
        /// Generates an OAuth token from a refresh token.
        /// </summary>
        /// <param name="refresh_token">The refresh token.</param>
        /// <param name="options">The current OAuth options.</param>
        /// <returns>An OAuth token or null if the refresh token is invalid or expired.</returns>
        internal async static Task<object> Refresh(string refresh_token, OAuthOptions options)
        {
            using (var context = new TFGContext())
            {
                // Search if token exists and has an user associated with it
                var query = context.User.Include(u => u.Token).Where(u => u.Token.id == refresh_token);
                if (!query.Any()) return Task.FromResult<object>(null);

                User user = query.First();
                if (user.Token.exp - DateTime.UtcNow <= TimeSpan.Zero) return Task.FromResult<object>(null);
                string encoded = await GenerateJWT(user.email, options);
                return new
                {
                    scope = "useraccount",
                    token_type = "Bearer",
                    access_token = encoded,
                    expires_in = (int)options.ValidFor.TotalSeconds,
                    refresh_token = refresh_token,
                    id = user.id,
                    email = user.email,
                    name = user.name
                };
            }
        }

        /// <summary>
        /// Generates an OAuth token.
        /// </summary>
        /// <param name="email">The user email.</param>
        /// <param name="password">The password hashed from the android app.</param>
        /// <param name="options">The current OAuth options.</param>
        /// <param name="passHashed">Tells to the method if the password is already server-hashed.</param>
        /// <returns>An OAuth token or null if the identity couldn't be retrieved.</returns>
        internal async static Task<object> GenerateToken(string email, string password, OAuthOptions options, bool passHashed = false)
        {
            // Retrieve identity from DB
            var identity = await GetIdentity(email, password, passHashed);
            if (identity == null)
            {
                return null;
            }

            // Generate JWT
            string encoded = await GenerateJWT(email, options);

            // Create OAuth token
            var response = new
            {
                scope = "useraccount",
                token_type = "Bearer",
                access_token = encoded,
                expires_in = (int)options.ValidFor.TotalSeconds,
                refresh_token = identity.FindFirst("refreshToken").Value,
                id = identity.FindFirst("userId").Value,
                email = email,
                name = identity.FindFirst("userName").Value
            };
            return response;
        }

        /// <summary>
        /// Generates a new JWT.
        /// </summary>
        /// <param name="username">The user email.</param>
        /// <param name="options">The current OAuth options.</param>
        /// <returns>A JSON Web Token.</returns>
        private async static Task<string> GenerateJWT(string username, OAuthOptions options)
        {
            // Specifically add the jti (random nonce), iat (issued timestamp), and sub (subject/user) claims.
            // You can add other claims here, if you want:
            var claims = new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(JwtRegisteredClaimNames.Jti, await options.JtiGenerator()),
                new Claim(JwtRegisteredClaimNames.Iat, ToUnixEpochDate(options.IssuedAt).ToString(), ClaimValueTypes.Integer64)
            };

            // Create the JWT and write it to a string
            var jwt = new JwtSecurityToken(
                issuer: options.Issuer,
                audience: options.Audience,
                claims: claims,
                notBefore: options.NotBefore,
                expires: options.Expiration,
                signingCredentials: options.SigningCredentials);

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        /// <summary>
        /// Gets an identity from the database, checking if user exists and password is correct.
        /// </summary>
        /// <param name="user">The user email.</param>
        /// <param name="password">The password hashed from the android app.</param>
        /// <param name="passHashed">Tells to the method, if the password is already server-hashed.</param>
        /// <returns>A ClaimsIdentity if everything went right, or null in otherwise.</returns>
        private static Task<ClaimsIdentity> GetIdentity(string user, string password, bool passHashed)
        {
            using (var context = new TFGContext())
            {
                // Check if user exists
                var query = context.User.Include(us => us.Token).Where(us => user == us.email);
                if (!query.Any()) goto invalid;

                // Get user and compare stored hash with created one
                User u = query.First();
                byte[] salt = Convert.FromBase64String(u.hash.Split(':')[0]);
                string sHash = u.hash.Split(':')[1];
                string hash = passHashed ? password : Convert.ToBase64String(KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA512, 10000, 512 / 8));
                if (!hash.Equals(sHash)) goto invalid;
                if (u.resetPass) u.resetPass = false; // User logged in, no need to reset pass

                // We are authenticating with user/password, generate new UserToken!
                UserToken token = new UserToken
                {
                    id = Guid.NewGuid().ToString("n"),
                    sub = u.email,
                    User = u
                };
                if (u.Token != null)
                {
                    UserToken tk = u.Token;
                    context.Remove(tk);
                }
                u.Token = token;
                context.SaveChanges();

                return Task.FromResult(new ClaimsIdentity(new GenericIdentity(user, "Token"), new Claim[]
                {
                    new Claim("refreshToken", token.id),
                    new Claim("userId", u.id.ToString()),
                    new Claim("userName", u.name)
                }));
            }

        invalid:
            // Invalid credentials or inexistent account
            return Task.FromResult<ClaimsIdentity>(null);
        }

        internal static void ThrowIfInvalidOptions(OAuthOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (options.ValidFor <= TimeSpan.Zero)
            {
                throw new ArgumentException("Must be a non-zero TimeSpan.", nameof(OAuthOptions.ValidFor));
            }

            if (options.SigningCredentials == null)
            {
                throw new ArgumentNullException(nameof(OAuthOptions.SigningCredentials));
            }

            if (options.JtiGenerator == null)
            {
                throw new ArgumentNullException(nameof(OAuthOptions.JtiGenerator));
            }
        }
    }
}