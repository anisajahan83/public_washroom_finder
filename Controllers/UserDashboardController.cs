using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace SE1.Controllers
{
    public class UserDashboardController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public UserDashboardController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        private bool IsUserLoggedIn()
        {
            return !string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail"));
        }

       
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        
        private string ExtractCity(string location)
        {
            if (string.IsNullOrEmpty(location)) return "";

            
            string[] cities = {
                "Chittagong", "Chattogram", "Dhaka", "Khulna", "Rajshahi",
                "Barisal", "Sylhet", "Rangpur", "Mymensingh", "Cox's Bazar",
                "Comilla", "Narayanganj", "Gazipur"
            };

            string locationLower = location.ToLower();

            foreach (var city in cities)
            {
                if (locationLower.Contains(city.ToLower()))
                {
                    return city;
                }
            }

            
            var parts = location.Split(',');
            return parts[0].Trim();
        }

        public IActionResult Dashboard()
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = 0;
            int.TryParse(HttpContext.Session.GetString("UserId"), out userId);

            ViewBag.UserName = HttpContext.Session.GetString("UserName") ?? "User";
            ViewBag.UserEmail = HttpContext.Session.GetString("UserEmail") ?? "";
            ViewBag.UserId = userId;

            var stats = new DashboardStats();
            string userCity = "";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

               
                string locQuery = "SELECT location FROM users WHERE user_id = @userId";
                using (var cmd = new NpgsqlCommand(locQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    var result = cmd.ExecuteScalar();
                    userCity = ExtractCity(result?.ToString() ?? "");
                }

                
                string query1 = @"SELECT COUNT(*) FROM washrooms 
                                  WHERE verified_status = true 
                                  AND (@userCity = '' OR LOWER(address) LIKE '%' || LOWER(@userCity) || '%')";
                using (var cmd = new NpgsqlCommand(query1, conn))
                {
                    cmd.Parameters.AddWithValue("@userCity", userCity);
                    stats.NearbyCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                string query2 = "SELECT COUNT(*) FROM favorites WHERE user_id = @userId";
                using (var cmd = new NpgsqlCommand(query2, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    stats.FavoriteCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                string query3 = "SELECT COUNT(*) FROM ratings WHERE user_id = @userId";
                using (var cmd = new NpgsqlCommand(query3, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    stats.RatingCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                string query4 = "SELECT COUNT(*) FROM reports WHERE user_id = @userId";
                using (var cmd = new NpgsqlCommand(query4, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    stats.ReportCount = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            ViewBag.UserCity = userCity;
            ViewBag.Stats = stats;
            return View("Dashboard", stats);
        }

       
        public IActionResult EditProfile()
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");
            var user = new UserProfileViewModel();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"
                    SELECT user_id, full_name, email, mobile, location, profile_picture
                    FROM users 
                    WHERE user_id = @userId";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            user.UserId = Convert.ToInt32(reader["user_id"]);
                            user.FullName = reader["full_name"]?.ToString() ?? "";
                            user.Email = reader["email"]?.ToString() ?? "";
                            user.Mobile = reader["mobile"]?.ToString() ?? "";
                            user.Location = reader["location"]?.ToString() ?? "";
                            user.ProfilePicture = reader["profile_picture"]?.ToString() ?? "";
                        }
                    }
                }
            }

            if (user.UserId == 0)
            {
                return RedirectToAction("Dashboard");
            }

            return View(user);
        }

      
        [HttpPost]
        public IActionResult EditProfile(string fullName, string email, string mobile, string location,
                                          string latitude, string longitude)
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");

            try
            {
                
                if (string.IsNullOrEmpty(fullName))
                {
                    ViewBag.Error = "Full name is required.";
                    return View();
                }
                if (string.IsNullOrEmpty(email))
                {
                    ViewBag.Error = "Email address is required.";
                    return View();
                }
                if (string.IsNullOrEmpty(mobile))
                {
                    ViewBag.Error = "Mobile number is required.";
                    return View();
                }
                if (string.IsNullOrEmpty(location))
                {
                    ViewBag.Error = "Location is required.";
                    return View();
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                  
                    string checkQuery = "SELECT COUNT(*) FROM users WHERE email = @email AND user_id != @userId";
                    using (var checkCmd = new NpgsqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@email", email);
                        checkCmd.Parameters.AddWithValue("@userId", userId);

                        long count = (long)checkCmd.ExecuteScalar();
                        if (count > 0)
                        {
                            ViewBag.Error = "Email already registered by another user.";
                            return View();
                        }
                    }

                    
                    string updateQuery = @"
                UPDATE users SET 
                    full_name = @fullName,
                    email = @email,
                    mobile = @mobile,
                    location = @location,
                    latitude = @latitude,
                    longitude = @longitude,
                    updated_at = CURRENT_TIMESTAMP
                WHERE user_id = @userId";

                    using (var cmd = new NpgsqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@fullName", fullName);
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@mobile", mobile);
                        cmd.Parameters.AddWithValue("@location", location);

                        decimal lat = 0, lng = 0;
                        decimal.TryParse(latitude, out lat);
                        decimal.TryParse(longitude, out lng);
                        cmd.Parameters.AddWithValue("@latitude", lat);
                        cmd.Parameters.AddWithValue("@longitude", lng);
                        cmd.Parameters.AddWithValue("@userId", userId);

                        int rows = cmd.ExecuteNonQuery();

                        if (rows > 0)
                        {
                            HttpContext.Session.SetString("UserName", fullName);
                            HttpContext.Session.SetString("UserEmail", email);

                            TempData["Success"] = "✅ Profile updated successfully!";
                            return RedirectToAction("Dashboard");
                        }
                        else
                        {
                            ViewBag.Error = "Failed to update profile. Please try again.";
                            return View();
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

        public IActionResult ChangePassword()
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            return View();
        }


        [HttpPost]
        public IActionResult ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");

            try
            {
                if (string.IsNullOrEmpty(currentPassword))
                {
                    ViewBag.Error = "Current password is required.";
                    return View();
                }

                if (string.IsNullOrEmpty(newPassword))
                {
                    ViewBag.Error = "New password is required.";
                    return View();
                }

                if (newPassword.Length < 6)
                {
                    ViewBag.Error = "New password must be at least 6 characters long.";
                    return View();
                }

                if (string.IsNullOrEmpty(confirmPassword))
                {
                    ViewBag.Error = "Please confirm your new password.";
                    return View();
                }

                if (newPassword != confirmPassword)
                {
                    ViewBag.Error = "New passwords do not match.";
                    return View();
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string getQuery = "SELECT password_hash FROM users WHERE user_id = @userId";
                    using (var getCmd = new NpgsqlCommand(getQuery, conn))
                    {
                        getCmd.Parameters.AddWithValue("@userId", userId);
                        string storedHash = getCmd.ExecuteScalar()?.ToString() ?? "";

                        string hashedCurrent = HashPassword(currentPassword);
                        if (storedHash != hashedCurrent)
                        {
                            ViewBag.Error = "Current password is incorrect.";
                            return View();
                        }
                    }

                    string updateQuery = "UPDATE users SET password_hash = @newHash, updated_at = CURRENT_TIMESTAMP WHERE user_id = @userId";
                    using (var cmd = new NpgsqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@newHash", HashPassword(newPassword));
                        cmd.Parameters.AddWithValue("@userId", userId);

                        int rows = cmd.ExecuteNonQuery();

                        if (rows > 0)
                        {
                            TempData["Success"] = "✅ Password changed successfully!";
                            return RedirectToAction("Dashboard");
                        }
                        else
                        {
                            ViewBag.Error = "Failed to change password. Please try again.";
                            return View();
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

        
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public IActionResult ForgotPassword(string email, string newPassword, string confirmPassword)
        {
            try
            {
                if (string.IsNullOrEmpty(email))
                {
                    ViewBag.Error = "Email address is required.";
                    return View();
                }

                if (string.IsNullOrEmpty(newPassword))
                {
                    ViewBag.Error = "New password is required.";
                    return View();
                }

                if (newPassword.Length < 6)
                {
                    ViewBag.Error = "Password must be at least 6 characters long.";
                    return View();
                }

                if (string.IsNullOrEmpty(confirmPassword))
                {
                    ViewBag.Error = "Please confirm your password.";
                    return View();
                }

                if (newPassword != confirmPassword)
                {
                    ViewBag.Error = "Passwords do not match.";
                    return View();
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string checkQuery = "SELECT user_id FROM users WHERE email = @email";
                    using (var checkCmd = new NpgsqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@email", email);
                        var result = checkCmd.ExecuteScalar();

                        if (result == null)
                        {
                            ViewBag.Error = "No account found with this email address.";
                            return View();
                        }

                        int userId = Convert.ToInt32(result);

                        string updateQuery = "UPDATE users SET password_hash = @newHash, updated_at = CURRENT_TIMESTAMP WHERE user_id = @userId";
                        using (var cmd = new NpgsqlCommand(updateQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@newHash", HashPassword(newPassword));
                            cmd.Parameters.AddWithValue("@userId", userId);

                            int rows = cmd.ExecuteNonQuery();

                            if (rows > 0)
                            {
                                TempData["Success"] = "✅ Password reset successfully! Please login with your new password.";
                                return RedirectToAction("Login", "Home");
                            }
                            else
                            {
                                ViewBag.Error = "Failed to reset password. Please try again.";
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

        
        public IActionResult MyReports()
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");
            var reports = new List<MyReportViewModel>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"
                    SELECT 
                        r.report_id, r.issue_type, r.description, r.photo_url,
                        r.status, r.created_at, r.resolved_at, r.admin_comment,
                        w.washroom_id, w.name AS washroom_name, w.address AS washroom_address,
                        (SELECT image_url FROM washroom_images WHERE washroom_id = w.washroom_id LIMIT 1) AS washroom_image
                    FROM reports r
                    JOIN washrooms w ON r.washroom_id = w.washroom_id
                    WHERE r.user_id = @userId
                    ORDER BY r.created_at DESC";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            reports.Add(new MyReportViewModel
                            {
                                ReportId = Convert.ToInt32(reader["report_id"]),
                                WashroomId = Convert.ToInt32(reader["washroom_id"]),
                                WashroomName = reader["washroom_name"]?.ToString() ?? "Unknown",
                                WashroomAddress = reader["washroom_address"]?.ToString() ?? "",
                                WashroomImage = reader["washroom_image"]?.ToString() ?? "",
                                IssueType = reader["issue_type"]?.ToString() ?? "",
                                Description = reader["description"]?.ToString() ?? "",
                                PhotoUrl = reader["photo_url"]?.ToString() ?? "",
                                Status = reader["status"]?.ToString() ?? "Pending",
                                AdminComment = reader["admin_comment"]?.ToString() ?? "",
                                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                                ResolvedAt = reader["resolved_at"] != DBNull.Value ?
                                              Convert.ToDateTime(reader["resolved_at"]) : (DateTime?)null
                            });
                        }
                    }
                }
            }

            return View(reports);
        }

        public IActionResult ReportWashroom(int? washroomId)
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            var model = new ReportWashroomViewModel();
            var washrooms = new List<WashroomSelectViewModel>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = "SELECT washroom_id, name, address FROM washrooms WHERE verified_status = true ORDER BY name";
                using (var cmd = new NpgsqlCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        washrooms.Add(new WashroomSelectViewModel
                        {
                            WashroomId = Convert.ToInt32(reader["washroom_id"]),
                            Name = reader["name"]?.ToString() ?? "",
                            Address = reader["address"]?.ToString() ?? ""
                        });
                    }
                }
            }

            ViewBag.Washrooms = washrooms;

            if (washroomId.HasValue && washroomId.Value > 0)
            {
                model.WashroomId = washroomId.Value;
                var selected = washrooms.FirstOrDefault(w => w.WashroomId == washroomId.Value);
                if (selected != null)
                {
                    model.WashroomName = selected.Name;
                    model.WashroomAddress = selected.Address;
                }
            }

            return View(model);
        }

        
        [HttpPost]
        public IActionResult ReportWashroom(int washroomId, string issueType, string description, IFormFile? photo)
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");

            try
            {
                if (washroomId <= 0)
                {
                    ViewBag.Error = "Please select a valid washroom.";
                    return View();
                }

                if (string.IsNullOrEmpty(issueType))
                {
                    ViewBag.Error = "Please select an issue type.";
                    return View();
                }

                if (string.IsNullOrEmpty(description))
                {
                    ViewBag.Error = "Please provide a description of the issue.";
                    return View();
                }

                string photoUrl = "";

                if (photo != null && photo.Length > 0)
                {
                    string extension = Path.GetExtension(photo.FileName).ToLower();
                    if (extension == ".jpg" || extension == ".jpeg" || extension == ".png")
                    {
                        if (photo.Length <= 5 * 1024 * 1024)
                        {
                            string uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "reports");
                            if (!Directory.Exists(uploadPath))
                            {
                                Directory.CreateDirectory(uploadPath);
                            }

                            string fileName = $"{userId}_{DateTime.Now.Ticks}{extension}";
                            string filePath = Path.Combine(uploadPath, fileName);

                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                photo.CopyTo(stream);
                            }

                            photoUrl = "/images/reports/" + fileName;
                        }
                    }
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string insertQuery = @"
                        INSERT INTO reports (user_id, washroom_id, issue_type, description, photo_url, status, created_at)
                        VALUES (@userId, @washroomId, @issueType, @description, @photoUrl, 'Pending', CURRENT_TIMESTAMP)
                        RETURNING report_id";

                    using (var cmd = new NpgsqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@washroomId", washroomId);
                        cmd.Parameters.AddWithValue("@issueType", issueType);
                        cmd.Parameters.AddWithValue("@description", description);
                        cmd.Parameters.AddWithValue("@photoUrl", photoUrl);

                        int reportId = (int)cmd.ExecuteScalar();

                        if (reportId > 0)
                        {
                            TempData["Success"] = "✅ Report submitted successfully! Admin will review it.";
                            return RedirectToAction("MyReports");
                        }
                        else
                        {
                            ViewBag.Error = "Failed to submit report. Please try again.";
                            return View();
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

        public IActionResult Nearby(double? lat, double? lng, double? radius, string search, string costType, bool? bathOnly, bool? openOnly, bool? wheelchairOnly, bool? babyOnly)
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");

            
            string userLocation = "";
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                string locQuery = "SELECT location FROM users WHERE user_id = @userId";
                using (var cmd = new NpgsqlCommand(locQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    var result = cmd.ExecuteScalar();
                    userLocation = result?.ToString() ?? "";
                }
            }

            string userCity = ExtractCity(userLocation);

            double userLat = lat ?? 22.3384;
            double userLng = lng ?? 91.8321;
            double userRadius = radius ?? 500.0; 

            var washrooms = new List<WashroomViewModel>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"
                    SELECT 
                        w.washroom_id, w.name, w.address, w.latitude, w.longitude, 
                        w.opening_hours, w.usage_cost, w.hygiene_score,
                        w.security_status, w.crowd_level, 
                        w.bath_availability, w.baby_facility, w.wheelchair_access,
                        w.handwash, w.hand_dryer, w.verified_status, w.description,
                        w.contact_number,
                        (SELECT image_url FROM washroom_images WHERE washroom_id = w.washroom_id LIMIT 1) AS image_url,
                        COALESCE(AVG(r.rating), 0) AS avg_rating,
                        COUNT(r.rating) AS total_ratings,
                        w.hygiene_score AS cleanliness_rating
                    FROM washrooms w
                    LEFT JOIN ratings r ON w.washroom_id = r.washroom_id
                    WHERE w.verified_status = true
                      AND (@userCity = '' OR LOWER(w.address) LIKE '%' || LOWER(@userCity) || '%')";

                if (!string.IsNullOrEmpty(search))
                {
                    query += " AND (w.name ILIKE @search OR w.address ILIKE @search)";
                }

                if (!string.IsNullOrEmpty(costType) && costType != "all")
                {
                    if (costType == "free")
                        query += " AND w.usage_cost = 0";
                    else if (costType == "paid")
                        query += " AND w.usage_cost > 0";
                }

                if (bathOnly == true)
                {
                    query += " AND w.bath_availability = true";
                }

                if (wheelchairOnly == true)
                {
                    query += " AND w.wheelchair_access = true";
                }

                if (babyOnly == true)
                {
                    query += " AND w.baby_facility = true";
                }

                query += @" GROUP BY w.washroom_id 
                           ORDER BY w.hygiene_score DESC";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userCity", userCity);

                    if (!string.IsNullOrEmpty(search))
                        cmd.Parameters.AddWithValue("@search", "%" + search + "%");

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var openingHours = reader["opening_hours"]?.ToString() ?? "";
                            int washroomId = reader["washroom_id"] != DBNull.Value ? Convert.ToInt32(reader["washroom_id"]) : 0;

                            double washroomLat = reader["latitude"] != DBNull.Value ? Convert.ToDouble(reader["latitude"]) : 0;
                            double washroomLng = reader["longitude"] != DBNull.Value ? Convert.ToDouble(reader["longitude"]) : 0;

                            
                            double distance = 0;
                            if (washroomLat != 0 && washroomLng != 0)
                            {
                                var R = 6371; 
                                var dLat = ToRadians(washroomLat - userLat);
                                var dLon = ToRadians(washroomLng - userLng);
                                var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                                        Math.Cos(ToRadians(userLat)) * Math.Cos(ToRadians(washroomLat)) *
                                        Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                                var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
                                distance = R * c;
                            }

                            washrooms.Add(new WashroomViewModel
                            {
                                WashroomId = washroomId,
                                Name = reader["name"]?.ToString() ?? "",
                                Address = reader["address"]?.ToString() ?? "",
                                Latitude = washroomLat,
                                Longitude = washroomLng,
                                City = userCity,
                                OpeningHours = openingHours,
                                UsageCost = Convert.ToDecimal(reader["usage_cost"]),
                                HygieneScore = Convert.ToDecimal(reader["hygiene_score"]),
                                CleanlinessRating = Convert.ToDecimal(reader["cleanliness_rating"]),
                                SecurityStatus = reader["security_status"]?.ToString() ?? "",
                                CrowdLevel = reader["crowd_level"]?.ToString() ?? "",
                                BathAvailability = Convert.ToBoolean(reader["bath_availability"]),
                                BabyFacility = Convert.ToBoolean(reader["baby_facility"]),
                                WheelchairAccess = Convert.ToBoolean(reader["wheelchair_access"]),
                                Handwash = Convert.ToBoolean(reader["handwash"]),
                                HandDryer = Convert.ToBoolean(reader["hand_dryer"]),
                                VerifiedStatus = Convert.ToBoolean(reader["verified_status"]),
                                Description = reader["description"]?.ToString() ?? "",
                                ContactNumber = reader["contact_number"]?.ToString() ?? "",
                                ImageUrl = reader["image_url"]?.ToString() ?? "",
                                AvgRating = Convert.ToDecimal(reader["avg_rating"]),
                                TotalRatings = Convert.ToInt32(reader["total_ratings"]),
                                DistanceKm = distance,
                                IsOpen = IsWashroomOpen(openingHours),
                                IsFavorite = IsFavorite(userId, washroomId)
                            });
                        }
                    }
                }
            }

            ViewBag.UserLat = userLat;
            ViewBag.UserLng = userLng;
            ViewBag.Radius = userRadius;
            ViewBag.SearchTerm = search;
            ViewBag.CostType = costType;
            ViewBag.BathOnly = bathOnly;
            ViewBag.OpenOnly = openOnly;
            ViewBag.WheelchairOnly = wheelchairOnly;
            ViewBag.BabyOnly = babyOnly;
            ViewBag.UserCity = userCity;

            return View(washrooms);
        }

        private double ToRadians(double angle)
        {
            return Math.PI * angle / 180.0;
        }

        
        public IActionResult Details(int id)
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");
            WashroomDetailViewModel washroom = null;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"
                    SELECT 
                        w.*,
                        (SELECT image_url FROM washroom_images WHERE washroom_id = w.washroom_id LIMIT 1) AS image_url,
                        (SELECT array_agg(image_url) FROM washroom_images WHERE washroom_id = w.washroom_id) AS all_images,
                        COALESCE(AVG(r.rating), 0) AS avg_rating,
                        COUNT(r.rating) AS total_ratings,
                        u.full_name AS owner_name
                    FROM washrooms w
                    LEFT JOIN ratings r ON w.washroom_id = r.washroom_id
                    LEFT JOIN users u ON w.created_by = u.user_id
                    WHERE w.washroom_id = @id
                    GROUP BY w.washroom_id, u.full_name";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var allImages = reader["all_images"] != DBNull.Value ?
                                            ((object[])reader["all_images"]).Select(x => x?.ToString() ?? "").ToList() :
                                            new List<string>();

                            int washroomId = reader["washroom_id"] != DBNull.Value ? Convert.ToInt32(reader["washroom_id"]) : 0;

                            washroom = new WashroomDetailViewModel
                            {
                                WashroomId = washroomId,
                                Name = reader["name"]?.ToString() ?? "",
                                Address = reader["address"]?.ToString() ?? "",
                                Latitude = reader["latitude"] != DBNull.Value ? Convert.ToDouble(reader["latitude"]) : 0,
                                Longitude = reader["longitude"] != DBNull.Value ? Convert.ToDouble(reader["longitude"]) : 0,
                                City = reader["city"]?.ToString() ?? "N/A",
                                OpeningHours = reader["opening_hours"]?.ToString() ?? "",
                                UsageCost = Convert.ToDecimal(reader["usage_cost"]),
                                HygieneScore = Convert.ToDecimal(reader["hygiene_score"]),
                                SecurityStatus = reader["security_status"]?.ToString() ?? "",
                                CrowdLevel = reader["crowd_level"]?.ToString() ?? "",
                                BathAvailability = Convert.ToBoolean(reader["bath_availability"]),
                                BabyFacility = Convert.ToBoolean(reader["baby_facility"]),
                                WheelchairAccess = Convert.ToBoolean(reader["wheelchair_access"]),
                                Handwash = Convert.ToBoolean(reader["handwash"]),
                                HandDryer = Convert.ToBoolean(reader["hand_dryer"]),
                                VerifiedStatus = Convert.ToBoolean(reader["verified_status"]),
                                Description = reader["description"]?.ToString() ?? "",
                                ContactNumber = reader["contact_number"]?.ToString() ?? "",
                                ImageUrl = reader["image_url"]?.ToString() ?? "",
                                AllImages = allImages,
                                AvgRating = Convert.ToDecimal(reader["avg_rating"]),
                                TotalRatings = Convert.ToInt32(reader["total_ratings"]),
                                OwnerName = reader["owner_name"]?.ToString() ?? "Unknown",
                                IsOpen = IsWashroomOpen(reader["opening_hours"]?.ToString() ?? ""),
                                IsFavorite = IsFavorite(userId, washroomId)
                            };
                        }
                    }
                }
            }

            if (washroom == null)
            {
                return RedirectToAction("Nearby");
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT rating, comment FROM ratings WHERE user_id = @userId AND washroom_id = @washroomId";
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@washroomId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            washroom.UserRating = Convert.ToInt32(reader["rating"]);
                            washroom.UserComment = reader["comment"]?.ToString() ?? "";
                        }
                    }
                }
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                string query = @"
                    SELECT r.rating, r.comment, r.created_at, u.full_name
                    FROM ratings r
                    JOIN users u ON r.user_id = u.user_id
                    WHERE r.washroom_id = @washroomId AND r.user_id != @userId
                    ORDER BY r.created_at DESC
                    LIMIT 10";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@washroomId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        washroom.Reviews = new List<ReviewViewModel>();
                        while (reader.Read())
                        {
                            washroom.Reviews.Add(new ReviewViewModel
                            {
                                UserName = reader["full_name"]?.ToString() ?? "",
                                Rating = Convert.ToInt32(reader["rating"]),
                                Comment = reader["comment"]?.ToString() ?? "",
                                CreatedAt = Convert.ToDateTime(reader["created_at"])
                            });
                        }
                    }
                }
            }

            return View(washroom);
        }

        [HttpPost]
        public IActionResult ToggleFavorite(int washroomId)
        {
            if (!IsUserLoggedIn())
            {
                return Json(new { success = false, message = "Please login first." });
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string checkQuery = "SELECT COUNT(*) FROM favorites WHERE user_id = @userId AND washroom_id = @washroomId";
                    using (var cmd = new NpgsqlCommand(checkQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@washroomId", washroomId);

                        long count = (long)cmd.ExecuteScalar();

                        if (count > 0)
                        {
                            string deleteQuery = "DELETE FROM favorites WHERE user_id = @userId AND washroom_id = @washroomId";
                            using (var delCmd = new NpgsqlCommand(deleteQuery, conn))
                            {
                                delCmd.Parameters.AddWithValue("@userId", userId);
                                delCmd.Parameters.AddWithValue("@washroomId", washroomId);
                                delCmd.ExecuteNonQuery();
                            }
                            return Json(new { success = true, action = "removed", message = "Removed from favorites." });
                        }
                        else
                        {
                            string insertQuery = "INSERT INTO favorites (user_id, washroom_id) VALUES (@userId, @washroomId)";
                            using (var insCmd = new NpgsqlCommand(insertQuery, conn))
                            {
                                insCmd.Parameters.AddWithValue("@userId", userId);
                                insCmd.Parameters.AddWithValue("@washroomId", washroomId);
                                insCmd.ExecuteNonQuery();
                            }
                            return Json(new { success = true, action = "added", message = "Added to favorites!" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

      
        [HttpPost]
        public IActionResult RateWashroom(int washroomId, int rating, string comment)
        {
            if (!IsUserLoggedIn())
            {
                return Json(new { success = false, message = "Please login first." });
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string checkQuery = "SELECT COUNT(*) FROM ratings WHERE user_id = @userId AND washroom_id = @washroomId";
                    using (var cmd = new NpgsqlCommand(checkQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@washroomId", washroomId);

                        long count = (long)cmd.ExecuteScalar();

                        if (count > 0)
                        {
                            string updateQuery = "UPDATE ratings SET rating = @rating, comment = @comment, updated_at = CURRENT_TIMESTAMP WHERE user_id = @userId AND washroom_id = @washroomId";
                            using (var updCmd = new NpgsqlCommand(updateQuery, conn))
                            {
                                updCmd.Parameters.AddWithValue("@rating", rating);
                                updCmd.Parameters.AddWithValue("@comment", string.IsNullOrEmpty(comment) ? "" : comment);
                                updCmd.Parameters.AddWithValue("@userId", userId);
                                updCmd.Parameters.AddWithValue("@washroomId", washroomId);
                                updCmd.ExecuteNonQuery();
                            }
                            return Json(new { success = true, message = "Rating updated successfully!" });
                        }
                        else
                        {
                            string insertQuery = "INSERT INTO ratings (user_id, washroom_id, rating, comment) VALUES (@userId, @washroomId, @rating, @comment)";
                            using (var insCmd = new NpgsqlCommand(insertQuery, conn))
                            {
                                insCmd.Parameters.AddWithValue("@userId", userId);
                                insCmd.Parameters.AddWithValue("@washroomId", washroomId);
                                insCmd.Parameters.AddWithValue("@rating", rating);
                                insCmd.Parameters.AddWithValue("@comment", string.IsNullOrEmpty(comment) ? "" : comment);
                                insCmd.ExecuteNonQuery();
                            }
                            return Json(new { success = true, message = "Rating submitted successfully!" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        
        public IActionResult Favorites()
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");
            var washrooms = new List<WashroomViewModel>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"
                    SELECT 
                        w.washroom_id, w.name, w.address, w.latitude, w.longitude, 
                        w.city, w.opening_hours, w.usage_cost, w.hygiene_score,
                        w.security_status, w.crowd_level, 
                        w.bath_availability, w.baby_facility, w.wheelchair_access,
                        w.handwash, w.hand_dryer, w.verified_status, w.description,
                        (SELECT image_url FROM washroom_images WHERE washroom_id = w.washroom_id LIMIT 1) AS image_url,
                        COALESCE(AVG(r.rating), 0) AS avg_rating,
                        COUNT(r.rating) AS total_ratings,
                        f.created_at AS favorite_date
                    FROM favorites f
                    JOIN washrooms w ON f.washroom_id = w.washroom_id
                    LEFT JOIN ratings r ON w.washroom_id = r.washroom_id
                    WHERE f.user_id = @userId
                    GROUP BY w.washroom_id, f.created_at
                    ORDER BY f.created_at DESC";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int washroomId = reader["washroom_id"] != DBNull.Value ? Convert.ToInt32(reader["washroom_id"]) : 0;

                            washrooms.Add(new WashroomViewModel
                            {
                                WashroomId = washroomId,
                                Name = reader["name"]?.ToString() ?? "",
                                Address = reader["address"]?.ToString() ?? "",
                                Latitude = reader["latitude"] != DBNull.Value ? Convert.ToDouble(reader["latitude"]) : 0,
                                Longitude = reader["longitude"] != DBNull.Value ? Convert.ToDouble(reader["longitude"]) : 0,
                                City = reader["city"]?.ToString() ?? "N/A",
                                OpeningHours = reader["opening_hours"]?.ToString() ?? "",
                                UsageCost = Convert.ToDecimal(reader["usage_cost"]),
                                HygieneScore = Convert.ToDecimal(reader["hygiene_score"]),
                                SecurityStatus = reader["security_status"]?.ToString() ?? "",
                                CrowdLevel = reader["crowd_level"]?.ToString() ?? "",
                                BathAvailability = Convert.ToBoolean(reader["bath_availability"]),
                                BabyFacility = Convert.ToBoolean(reader["baby_facility"]),
                                WheelchairAccess = Convert.ToBoolean(reader["wheelchair_access"]),
                                Handwash = Convert.ToBoolean(reader["handwash"]),
                                HandDryer = Convert.ToBoolean(reader["hand_dryer"]),
                                VerifiedStatus = Convert.ToBoolean(reader["verified_status"]),
                                Description = reader["description"]?.ToString() ?? "",
                                ImageUrl = reader["image_url"]?.ToString() ?? "",
                                AvgRating = Convert.ToDecimal(reader["avg_rating"]),
                                TotalRatings = Convert.ToInt32(reader["total_ratings"]),
                                IsFavorite = true
                            });
                        }
                    }
                }
            }

            return View(washrooms);
        }

        
        public IActionResult MyRatings()
        {
            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");
            var ratings = new List<UserRatingViewModel>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"
                    SELECT 
                        r.rating_id, r.rating, r.comment, r.created_at, r.updated_at,
                        w.washroom_id, w.name, w.address,
                        (SELECT image_url FROM washroom_images WHERE washroom_id = w.washroom_id LIMIT 1) AS image_url
                    FROM ratings r
                    JOIN washrooms w ON r.washroom_id = w.washroom_id
                    WHERE r.user_id = @userId
                    ORDER BY r.created_at DESC";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int ratingId = reader["rating_id"] != DBNull.Value ? Convert.ToInt32(reader["rating_id"]) : 0;

                            ratings.Add(new UserRatingViewModel
                            {
                                RatingId = ratingId,
                                WashroomId = Convert.ToInt32(reader["washroom_id"]),
                                WashroomName = reader["name"]?.ToString() ?? "",
                                WashroomAddress = reader["address"]?.ToString() ?? "",
                                Rating = Convert.ToInt32(reader["rating"]),
                                Comment = reader["comment"]?.ToString() ?? "",
                                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                                UpdatedAt = reader["updated_at"] != DBNull.Value ?
                                            Convert.ToDateTime(reader["updated_at"]) : (DateTime?)null,
                                ImageUrl = reader["image_url"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }

            return View(ratings);
        }

        

        private bool IsWashroomOpen(string openingHours)
        {
            if (string.IsNullOrEmpty(openingHours))
                return false;

            if (openingHours.Contains("24/7") || openingHours.Contains("24 hours"))
                return true;

            return true;
        }

        private bool IsFavorite(int userId, int washroomId)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                string query = "SELECT COUNT(*) FROM favorites WHERE user_id = @userId AND washroom_id = @washroomId";
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@washroomId", washroomId);
                    return (long)cmd.ExecuteScalar() > 0;
                }
            }
        }


        public class DashboardStats
        {
            public int NearbyCount { get; set; }
            public int FavoriteCount { get; set; }
            public int RatingCount { get; set; }
            public int ReportCount { get; set; }
        }

        public class UserProfileViewModel
        {
            public int UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Mobile { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string ProfilePicture { get; set; } = string.Empty;
        }

        public class MyReportViewModel
        {
            public int ReportId { get; set; }
            public int WashroomId { get; set; }
            public string WashroomName { get; set; } = string.Empty;
            public string WashroomAddress { get; set; } = string.Empty;
            public string WashroomImage { get; set; } = string.Empty;
            public string IssueType { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string PhotoUrl { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string AdminComment { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime? ResolvedAt { get; set; }
        }

        public class ReportWashroomViewModel
        {
            public int WashroomId { get; set; }
            public string WashroomName { get; set; } = string.Empty;
            public string WashroomAddress { get; set; } = string.Empty;
            public string IssueType { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        public class WashroomSelectViewModel
        {
            public int WashroomId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
        }

        public class WashroomViewModel
        {
            public int WashroomId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public string City { get; set; } = string.Empty;
            public string OpeningHours { get; set; } = string.Empty;
            public decimal UsageCost { get; set; }
            public decimal HygieneScore { get; set; }
            public decimal CleanlinessRating { get; set; }
            public string SecurityStatus { get; set; } = string.Empty;
            public string CrowdLevel { get; set; } = string.Empty;
            public bool BathAvailability { get; set; }
            public bool BabyFacility { get; set; }
            public bool WheelchairAccess { get; set; }
            public bool Handwash { get; set; }
            public bool HandDryer { get; set; }
            public bool VerifiedStatus { get; set; }
            public string Description { get; set; } = string.Empty;
            public string ContactNumber { get; set; } = string.Empty;
            public string ImageUrl { get; set; } = string.Empty;
            public decimal AvgRating { get; set; }
            public int TotalRatings { get; set; }
            public double DistanceKm { get; set; }
            public bool IsOpen { get; set; }
            public bool IsFavorite { get; set; }
            public DateTime? FavoriteDate { get; set; }
        }

        public class WashroomDetailViewModel : WashroomViewModel
        {
            public List<string> AllImages { get; set; } = new List<string>();
            public string OwnerName { get; set; } = string.Empty;
            public int UserRating { get; set; }
            public string UserComment { get; set; } = string.Empty;
            public List<ReviewViewModel> Reviews { get; set; } = new List<ReviewViewModel>();
        }

        public class ReviewViewModel
        {
            public string UserName { get; set; } = string.Empty;
            public int Rating { get; set; }
            public string Comment { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }

        public class UserRatingViewModel
        {
            public int RatingId { get; set; }
            public int WashroomId { get; set; }
            public string WashroomName { get; set; } = string.Empty;
            public string WashroomAddress { get; set; } = string.Empty;
            public int Rating { get; set; }
            public string Comment { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public string ImageUrl { get; set; } = string.Empty;
        }
    }
}