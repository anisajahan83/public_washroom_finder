using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Npgsql;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace SE1.Controllers
{
    public class OwnerController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public OwnerController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        
        private bool IsValidFullName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Regex.IsMatch(name, @"^[A-Za-z\s]{4,}$");
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

        public IActionResult Login()
        {
            
            if (!string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")) &&
                HttpContext.Session.GetString("UserRole") == "owner")
            {
                return RedirectToAction("Dashboard", "WashroomOwner");
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
                    ViewBag.Error = "Please enter both email and password.";
                    return View();
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    
                    string query = @"
                        SELECT user_id, full_name, email, password_hash, role, is_active 
                        FROM users 
                        WHERE (email = @email OR LOWER(full_name) = LOWER(@email) OR mobile = @email)
                        AND role = 'owner'";

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

                                    
                                    HttpContext.Session.Clear();
                                    HttpContext.Session.SetString("UserId", reader["user_id"].ToString());
                                    HttpContext.Session.SetString("UserName", reader["full_name"].ToString());
                                    HttpContext.Session.SetString("UserEmail", reader["email"].ToString());
                                    HttpContext.Session.SetString("UserRole", "owner");
                                    HttpContext.Session.SetString("IsAdmin", "false");

                                   
                                    UpdateLastLogin(Convert.ToInt32(reader["user_id"]));

                                   
                                    return RedirectToAction("Dashboard", "WashroomOwner");
                                }
                                else
                                {
                                    ViewBag.Error = "Invalid email or password.";
                                    return View();
                                }
                            }
                            else
                            {
                                ViewBag.Error = "No owner account found with this email. Please register first.";
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

        
        public IActionResult Register()
        {
            
            if (!string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")) &&
                HttpContext.Session.GetString("UserRole") == "owner")
            {
                return RedirectToAction("Dashboard", "WashroomOwner");
            }
            return View();
        }

       
        [HttpPost]
        public IActionResult Register(string fullName, string email, string mobile, string location,
                                       string password, string confirmPassword)
        {
            try
            {
                
                if (string.IsNullOrEmpty(fullName))
                {
                    ViewBag.Error = "Full name is required.";
                    return View();
                }
                if (fullName.Length < 4)
                {
                    ViewBag.Error = "Full name must be at least 4 characters long.";
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
                    ViewBag.Error = "Email must start with a small letter, no dot (.) allowed, and end with @gmail.com or @yahoo.com.";
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
                        VALUES (@fullName, @email, @mobile, @location, 'owner', @passwordHash, CURRENT_TIMESTAMP)
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
                            TempData["Success"] = "✅ Owner registration successful! Please login to continue.";
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
            catch (Exception) { }
        }
    }
}