using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Npgsql;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace SE1.Controllers
{
    public class WashroomOwnerController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public WashroomOwnerController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        
        private bool IsOwnerLoggedIn()
        {
            return !string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")) &&
                   HttpContext.Session.GetString("UserRole") == "owner";
        }

        public IActionResult Dashboard()
        {
            if (!IsOwnerLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId"));
            var stats = new OwnerStats();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                
                string query1 = "SELECT COUNT(*) FROM washrooms WHERE created_by = @userId";
                using (var cmd = new NpgsqlCommand(query1, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    stats.TotalWashrooms = Convert.ToInt32(cmd.ExecuteScalar());
                }

                
                string query2 = "SELECT COUNT(*) FROM washrooms WHERE created_by = @userId AND verified_status = false";
                using (var cmd = new NpgsqlCommand(query2, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    stats.PendingWashrooms = Convert.ToInt32(cmd.ExecuteScalar());
                }

              
                string query3 = "SELECT COUNT(*) FROM washrooms WHERE created_by = @userId AND verified_status = true";
                using (var cmd = new NpgsqlCommand(query3, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    stats.VerifiedWashrooms = Convert.ToInt32(cmd.ExecuteScalar());
                }

                
                string query4 = @"
                    SELECT COUNT(*) FROM ratings r 
                    INNER JOIN washrooms w ON r.washroom_id = w.washroom_id 
                    WHERE w.created_by = @userId";
                using (var cmd = new NpgsqlCommand(query4, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    stats.TotalReviews = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            ViewBag.Stats = stats;
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.UserEmail = HttpContext.Session.GetString("UserEmail");
            ViewBag.UserId = userId;
            return View(stats);
        }

        
        public class OwnerStats
        {
            public int TotalWashrooms { get; set; }
            public int PendingWashrooms { get; set; }
            public int VerifiedWashrooms { get; set; }
            public int TotalReviews { get; set; }  
        }

       
        public IActionResult EditProfile()
        {
            if (!IsOwnerLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId"));
            var profile = new ProfileViewModel();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"SELECT user_id, full_name, email, mobile, location, latitude, longitude 
                                 FROM users WHERE user_id = @userId";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            profile.UserId = Convert.ToInt32(reader["user_id"]);
                            profile.FullName = reader["full_name"]?.ToString() ?? "";
                            profile.Email = reader["email"]?.ToString() ?? "";
                            profile.Mobile = reader["mobile"]?.ToString() ?? "";
                            profile.Location = reader["location"]?.ToString() ?? "";
                            profile.Latitude = reader["latitude"] != DBNull.Value ? Convert.ToDecimal(reader["latitude"]) : 0;
                            profile.Longitude = reader["longitude"] != DBNull.Value ? Convert.ToDecimal(reader["longitude"]) : 0;
                        }
                    }
                }
            }

            return View(profile);
        }

        [HttpPost]
        public IActionResult EditProfile(ProfileViewModel model)
        {
            if (!IsOwnerLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            try
            {
                
                if (string.IsNullOrEmpty(model.FullName))
                {
                    ViewBag.Error = "Full name is required.";
                    return View(model);
                }
                if (string.IsNullOrEmpty(model.Mobile))
                {
                    ViewBag.Error = "Mobile number is required.";
                    return View(model);
                }
                if (string.IsNullOrEmpty(model.Location))
                {
                    ViewBag.Error = "Location is required.";
                    return View(model);
                }

                int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId"));

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                        UPDATE users 
                        SET full_name = @fullName, 
                            mobile = @mobile, 
                            location = @location,
                            latitude = @latitude,
                            longitude = @longitude,
                            updated_at = CURRENT_TIMESTAMP
                        WHERE user_id = @userId";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@fullName", model.FullName);
                        cmd.Parameters.AddWithValue("@mobile", model.Mobile);
                        cmd.Parameters.AddWithValue("@location", model.Location);
                        cmd.Parameters.AddWithValue("@latitude", model.Latitude);
                        cmd.Parameters.AddWithValue("@longitude", model.Longitude);

                        cmd.ExecuteNonQuery();
                    }
                }

                HttpContext.Session.SetString("UserName", model.FullName);

                TempData["Success"] = "✅ Profile updated successfully!";
                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "An error occurred: " + ex.Message;
                return View(model);
            }
        }


        public class ProfileViewModel
        {
            public int UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Mobile { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public decimal Latitude { get; set; }      
            public decimal Longitude { get; set; }     
        }
    }
}