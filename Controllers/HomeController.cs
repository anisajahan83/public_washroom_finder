using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;
using System;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace SE1.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(_connectionString))
            {
                throw new Exception("Connection string 'DefaultConnection' not found in appsettings.json");
            }
        }

        
        private bool IsValidFullName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Regex.IsMatch(name, @"^[A-Za-z\s]{3,}$");
        }

        
        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return false;
            return Regex.IsMatch(email, @"^[a-z][a-zA-Z0-9]*(@gmail\.com|@yahoo\.com)$");
        }

        
        private bool IsValidMobile(string mobile)
        {
            if (string.IsNullOrEmpty(mobile)) return false;
            return Regex.IsMatch(mobile, @"^01[0-9]{9}$");
        }

       
        private bool IsValidLocation(string location)
        {
            if (string.IsNullOrEmpty(location)) return false;
            return !string.IsNullOrWhiteSpace(location);
        }

       
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        
        private void UpdateLastLogin(int userId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE user_id = @userId";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception)
            {
               
            }
        }

        public IActionResult Index()
        {
            return View();
        }

        
        public IActionResult AboutContact()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult UserDashboard()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                return RedirectToAction("Login");
            }
            
            string role = HttpContext.Session.GetString("UserRole") ?? "user";
            if (role == "owner")
            {
                return RedirectToAction("Dashboard", "WashroomOwner");
            }
            return RedirectToAction("Dashboard", "UserDashboard");
        }

        
        public IActionResult GoogleLogin(string returnUrl = null)
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = Url.Action("GoogleLoginCallback", new { returnUrl = returnUrl })
            };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        
        public async Task<IActionResult> GoogleLoginCallback(string returnUrl = null)
        {
            try
            {
                var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                if (!authenticateResult.Succeeded)
                {
                    TempData["Error"] = "Google login failed. Please try again.";
                    return RedirectToAction("Login");
                }

                
                var email = authenticateResult.Principal?.FindFirst(ClaimTypes.Email)?.Value;
                var name = authenticateResult.Principal?.FindFirst(ClaimTypes.Name)?.Value;
                var picture = authenticateResult.Principal?.FindFirst("picture")?.Value;

                if (string.IsNullOrEmpty(email))
                {
                    TempData["Error"] = "Could not retrieve email from Google. Please try again.";
                    return RedirectToAction("Login");
                }

                
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = "SELECT user_id, full_name, email, role, is_active FROM users WHERE email = @email";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@email", email);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                               
                                bool isActive = Convert.ToBoolean(reader["is_active"]);
                                if (!isActive)
                                {
                                    TempData["Error"] = "Your account has been deactivated. Please contact support.";
                                    return RedirectToAction("Login");
                                }

                                string userRole = reader["role"].ToString();

                                
                                HttpContext.Session.Clear();
                                HttpContext.Session.SetString("UserId", reader["user_id"].ToString());
                                HttpContext.Session.SetString("UserName", reader["full_name"].ToString());
                                HttpContext.Session.SetString("UserEmail", reader["email"].ToString());
                                HttpContext.Session.SetString("UserRole", userRole);
                                HttpContext.Session.SetString("IsAdmin", "false");
                                HttpContext.Session.SetString("IsGoogleLogin", "true");

                                
                                UpdateLastLogin(Convert.ToInt32(reader["user_id"]));

                                TempData["Success"] = "✅ Login successful with Google!";

                               
                                if (userRole == "owner")
                                {
                                    return RedirectToAction("Dashboard", "WashroomOwner");
                                }
                                else
                                {
                                    return RedirectToAction("Dashboard", "UserDashboard");
                                }
                            }
                            else
                            {
                               
                                reader.Close();

                                
                                string randomPassword = Guid.NewGuid().ToString().Substring(0, 8);
                                string hashedPassword = HashPassword(randomPassword);

                                string insertQuery = @"
                            INSERT INTO users (full_name, email, mobile, location, password_hash, role, is_active, created_at) 
                            VALUES (@fullName, @email, @mobile, @location, @passwordHash, 'user', true, CURRENT_TIMESTAMP)
                            RETURNING user_id";

                                using (var insertCmd = new NpgsqlCommand(insertQuery, conn))
                                {
                                    string userName = name ?? email.Split('@')[0];
                                    insertCmd.Parameters.AddWithValue("@fullName", userName);
                                    insertCmd.Parameters.AddWithValue("@email", email);
                                    insertCmd.Parameters.AddWithValue("@mobile", "N/A");
                                    insertCmd.Parameters.AddWithValue("@location", "Google User");
                                    insertCmd.Parameters.AddWithValue("@passwordHash", hashedPassword);

                                    int newUserId = (int)insertCmd.ExecuteScalar();

                                    if (newUserId > 0)
                                    {
                                       
                                        HttpContext.Session.Clear();
                                        HttpContext.Session.SetString("UserId", newUserId.ToString());
                                        HttpContext.Session.SetString("UserName", userName);
                                        HttpContext.Session.SetString("UserEmail", email);
                                        HttpContext.Session.SetString("UserRole", "user");
                                        HttpContext.Session.SetString("IsAdmin", "false");
                                        HttpContext.Session.SetString("IsGoogleLogin", "true");

                                        TempData["Success"] = "✅ Account created with Google! Welcome " + userName + "!";
                                        return RedirectToAction("Dashboard", "UserDashboard");
                                    }
                                }
                            }
                        }
                    }
                }

                TempData["Error"] = "An error occurred during Google login.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred: " + ex.Message;
                return RedirectToAction("Login");
            }
        }

        
        public IActionResult Login()
        {
            if (!string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                return RedirectToAction("UserDashboard");
            }
            return View();
        }

        
        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    ViewBag.Error = "Please enter both email/username and password.";
                    return View();
                }

               
                if (email == "NAMHS" && password == "SHMAN12345")
                {
                    HttpContext.Session.Clear();
                    HttpContext.Session.SetString("IsAdmin", "true");
                    HttpContext.Session.SetString("UserName", "Admin");
                    HttpContext.Session.SetString("UserEmail", "NAMHS");
                    HttpContext.Session.SetString("UserId", "0");
                    HttpContext.Session.SetString("UserRole", "admin");
                    HttpContext.Session.SetString("IsGoogleLogin", "false");
                    return RedirectToAction("Dashboard", "Admin");
                }

                
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT user_id, full_name, email, role, password_hash, is_active 
                        FROM users 
                        WHERE email = @email OR LOWER(full_name) = LOWER(@email) OR mobile = @email";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@email", email);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string storedHash = reader["password_hash"].ToString();
                                string hashedPassword = HashPassword(password);

                                if (storedHash == hashedPassword)
                                {
                                    bool isActive = Convert.ToBoolean(reader["is_active"]);
                                    if (!isActive)
                                    {
                                        ViewBag.Error = "Your account has been deactivated. Please contact support.";
                                        return View();
                                    }

                                    string userRole = reader["role"].ToString();

                                    
                                    HttpContext.Session.Clear();
                                    HttpContext.Session.SetString("UserId", reader["user_id"].ToString());
                                    HttpContext.Session.SetString("UserName", reader["full_name"].ToString());
                                    HttpContext.Session.SetString("UserEmail", reader["email"].ToString());
                                    HttpContext.Session.SetString("UserRole", userRole);
                                    HttpContext.Session.SetString("IsAdmin", "false");
                                    HttpContext.Session.SetString("IsGoogleLogin", "false");

                                    UpdateLastLogin(Convert.ToInt32(reader["user_id"]));

                                    ViewBag.Success = "Login successful!";

                                    
                                    if (userRole == "owner")
                                    {
                                        return RedirectToAction("Dashboard", "WashroomOwner");
                                    }
                                    else
                                    {
                                        return RedirectToAction("Dashboard", "UserDashboard");
                                    }
                                }
                                else
                                {
                                    ViewBag.Error = "Invalid email/username or password.";
                                    return View();
                                }
                            }
                            else
                            {
                                ViewBag.Error = "User not found. Please check your email/username.";
                                return View();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "An error occurred: " + ex.Message;
                return View();
            }
        }

        
        public IActionResult Signup()
        {
            return View();
        }

        
        [HttpPost]
        public IActionResult Signup(string fullName, string email, string mobile, string location, string password, string confirmPassword)
        {
            try
            {
               
                if (string.IsNullOrEmpty(fullName))
                {
                    ViewBag.Error = "Full name is required.";
                    return View();
                }
                if (fullName.Length < 3)
                {
                    ViewBag.Error = "Full name must be at least 3 characters long.";
                    return View();
                }
                if (!IsValidFullName(fullName))
                {
                    ViewBag.Error = "Full name can only contain letters and spaces.";
                    return View();
                }

                
                if (string.IsNullOrEmpty(email))
                {
                    ViewBag.Error = "Email address is required.";
                    return View();
                }
                if (!IsValidEmail(email))
                {
                    ViewBag.Error = "Email must start with a small letter, no dot (.) allowed, and end with @gmail.com or @yahoo.com (e.g., john123@gmail.com).";
                    return View();
                }

                if (string.IsNullOrEmpty(mobile))
                {
                    ViewBag.Error = "Mobile number is required.";
                    return View();
                }
                if (!IsValidMobile(mobile))
                {
                    ViewBag.Error = "Mobile must start with 01 and be exactly 11 digits (e.g., 01712345678).";
                    return View();
                }

              
                if (string.IsNullOrEmpty(location))
                {
                    ViewBag.Error = "Please detect or enter your location.";
                    return View();
                }

                if (string.IsNullOrEmpty(password))
                {
                    ViewBag.Error = "Password is required.";
                    return View();
                }
                if (password.Length < 6)
                {
                    ViewBag.Error = "Password must be at least 6 characters long.";
                    return View();
                }

                if (string.IsNullOrEmpty(confirmPassword))
                {
                    ViewBag.Error = "Please confirm your password.";
                    return View();
                }
                if (password != confirmPassword)
                {
                    ViewBag.Error = "Passwords do not match.";
                    return View();
                }

              
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string checkQuery = "SELECT COUNT(*) FROM users WHERE email = @email OR mobile = @mobile";
                    using (var checkCmd = new NpgsqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@email", email);
                        checkCmd.Parameters.AddWithValue("@mobile", mobile);

                        long count = (long)checkCmd.ExecuteScalar();
                        if (count > 0)
                        {
                            ViewBag.Error = "Email or Mobile number already registered. Please login.";
                            return View();
                        }
                    }

                    string insertQuery = @"
                        INSERT INTO users (full_name, email, mobile, location, role, password_hash, created_at) 
                        VALUES (@fullName, @email, @mobile, @location, 'user', @passwordHash, CURRENT_TIMESTAMP)
                        RETURNING user_id";

                    string hashedPassword = HashPassword(password);

                    using (var cmd = new NpgsqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@fullName", fullName);
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@mobile", mobile);
                        cmd.Parameters.AddWithValue("@location", location);
                        cmd.Parameters.AddWithValue("@passwordHash", hashedPassword);

                        int newUserId = (int)cmd.ExecuteScalar();

                        if (newUserId > 0)
                        {
                            HttpContext.Session.Clear();
                            TempData["Success"] = "✅ Registration successful! Please login to continue.";
                            return RedirectToAction("Login");
                        }
                        else
                        {
                            ViewBag.Error = "Registration failed. Please try again.";
                            return View();
                        }
                    }
                }
            }
            catch (NpgsqlException ex)
            {
                ViewBag.Error = "Database error: " + ex.Message;
                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Error = "An error occurred: " + ex.Message;
                return View();
            }
        }

       
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }
    }
}