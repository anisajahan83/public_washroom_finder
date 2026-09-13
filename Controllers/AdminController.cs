using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Npgsql;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace SE1.Controllers
{
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public AdminController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        private bool IsAdminLoggedIn()
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            var isAdmin = HttpContext.Session.GetString("IsAdmin");
            return !string.IsNullOrEmpty(userEmail) && isAdmin == "true";
        }

        public IActionResult Dashboard()
        {
            if (!IsAdminLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            var stats = new AdminStats();

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query1 = "SELECT COUNT(*) FROM washrooms";
                    using (var cmd = new NpgsqlCommand(query1, conn))
                    {
                        stats.TotalWashrooms = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    string query2 = "SELECT COUNT(*) FROM washrooms WHERE verified_status = false";
                    using (var cmd = new NpgsqlCommand(query2, conn))
                    {
                        stats.PendingWashrooms = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    string query3 = "SELECT COUNT(*) FROM washrooms WHERE verified_status = true";
                    using (var cmd = new NpgsqlCommand(query3, conn))
                    {
                        stats.VerifiedWashrooms = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    string query4 = "SELECT COUNT(*) FROM users";
                    using (var cmd = new NpgsqlCommand(query4, conn))
                    {
                        stats.TotalUsers = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    string query5 = "SELECT COUNT(*) FROM reports";
                    using (var cmd = new NpgsqlCommand(query5, conn))
                    {
                        stats.TotalReports = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    string query6 = "SELECT COUNT(*) FROM reports WHERE status = 'Pending'";
                    using (var cmd = new NpgsqlCommand(query6, conn))
                    {
                        stats.PendingReports = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Database error: " + ex.Message;
                return View(new AdminStats());
            }

            ViewBag.Stats = stats;
            ViewBag.UserName = HttpContext.Session.GetString("UserName") ?? "Admin";
            return View(stats);
        }

        public IActionResult Washrooms()
        {
            if (!IsAdminLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            var washrooms = new List<WashroomViewModel>();

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT w.washroom_id, w.name, w.address, w.opening_hours, w.usage_cost,
                               w.hygiene_score, w.security_status, w.crowd_level,
                               w.bath_availability, w.baby_facility, w.wheelchair_access,
                               w.handwash, w.hand_dryer,
                               w.verified_status, w.description, w.created_at,
                               u.full_name AS owner_name, u.email AS owner_email,
                               (SELECT image_url FROM washroom_images WHERE washroom_id = w.washroom_id LIMIT 1) AS image_url
                        FROM washrooms w
                        LEFT JOIN users u ON w.created_by = u.user_id
                        ORDER BY w.created_at DESC";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            washrooms.Add(new WashroomViewModel
                            {
                                WashroomId = Convert.ToInt32(reader["washroom_id"]),
                                Name = reader["name"]?.ToString() ?? "",
                                Address = reader["address"]?.ToString() ?? "",
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
                                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                                OwnerName = reader["owner_name"]?.ToString() ?? "Unknown",
                                OwnerEmail = reader["owner_email"]?.ToString() ?? "Unknown",
                                ImageUrl = reader["image_url"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Database error: " + ex.Message;
            }

            return View(washrooms);
        }

        [HttpPost]
        public IActionResult VerifyWashroom(int id)
        {
            if (!IsAdminLoggedIn())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"UPDATE washrooms SET verified_status = true, updated_at = CURRENT_TIMESTAMP 
                                    WHERE washroom_id = @id";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int rows = cmd.ExecuteNonQuery();

                        if (rows > 0)
                        {
                            return Json(new { success = true, message = "Washroom verified successfully!" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Washroom not found." });
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
        public IActionResult DeleteWashroom(int id)
        {
            if (!IsAdminLoggedIn())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = "DELETE FROM washrooms WHERE washroom_id = @id";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int rows = cmd.ExecuteNonQuery();

                        if (rows > 0)
                        {
                            return Json(new { success = true, message = "Washroom deleted successfully!" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Washroom not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        public IActionResult Users()
        {
            if (!IsAdminLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            var users = new List<UserViewModel>();

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT user_id, full_name, email, mobile, location, role, 
                               is_active, created_at, last_login
                        FROM users 
                        ORDER BY created_at DESC";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            users.Add(new UserViewModel
                            {
                                UserId = Convert.ToInt32(reader["user_id"]),
                                FullName = reader["full_name"]?.ToString() ?? "",
                                Email = reader["email"]?.ToString() ?? "",
                                Mobile = reader["mobile"]?.ToString() ?? "",
                                Location = reader["location"]?.ToString() ?? "Not set",
                                Role = reader["role"]?.ToString() ?? "",
                                IsActive = Convert.ToBoolean(reader["is_active"]),
                                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                                LastLogin = reader["last_login"] != DBNull.Value ?
                                            Convert.ToDateTime(reader["last_login"]) : (DateTime?)null
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Database error: " + ex.Message;
            }

            return View(users);
        }

        public IActionResult Reports()
        {
            if (!IsAdminLoggedIn())
            {
                return RedirectToAction("Login", "Home");
            }

            var reports = new List<ReportViewModel>();

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT r.report_id, r.issue_type, r.description, r.status, 
                               r.created_at, r.resolved_at,
                               u.full_name AS user_name, u.email AS user_email,
                               w.name AS washroom_name
                        FROM reports r
                        LEFT JOIN users u ON r.user_id = u.user_id
                        LEFT JOIN washrooms w ON r.washroom_id = w.washroom_id
                        ORDER BY r.created_at DESC";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            reports.Add(new ReportViewModel
                            {
                                ReportId = Convert.ToInt32(reader["report_id"]),
                                IssueType = reader["issue_type"]?.ToString() ?? "",
                                Description = reader["description"]?.ToString() ?? "",
                                Status = reader["status"]?.ToString() ?? "",
                                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                                ResolvedAt = reader["resolved_at"] != DBNull.Value ?
                                             Convert.ToDateTime(reader["resolved_at"]) : (DateTime?)null,
                                UserName = reader["user_name"]?.ToString() ?? "Unknown",
                                UserEmail = reader["user_email"]?.ToString() ?? "Unknown",
                                WashroomName = reader["washroom_name"]?.ToString() ?? "Unknown"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Database error: " + ex.Message;
            }

            return View(reports);
        }

        [HttpPost]
        public IActionResult ResolveReport(int id)
        {
            if (!IsAdminLoggedIn())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"UPDATE reports SET status = 'Resolved', resolved_at = CURRENT_TIMESTAMP 
                                    WHERE report_id = @id";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int rows = cmd.ExecuteNonQuery();

                        if (rows > 0)
                        {
                            return Json(new { success = true, message = "Report resolved successfully!" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Report not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        public class AdminStats
        {
            public int TotalWashrooms { get; set; }
            public int PendingWashrooms { get; set; }
            public int VerifiedWashrooms { get; set; }
            public int TotalUsers { get; set; }
            public int TotalReports { get; set; }
            public int PendingReports { get; set; }
        }

        public class WashroomViewModel
        {
            public int WashroomId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string OpeningHours { get; set; } = string.Empty;
            public decimal UsageCost { get; set; }
            public decimal HygieneScore { get; set; }
            public string SecurityStatus { get; set; } = string.Empty;
            public string CrowdLevel { get; set; } = string.Empty;
            public bool BathAvailability { get; set; }
            public bool BabyFacility { get; set; }
            public bool WheelchairAccess { get; set; }
            public bool Handwash { get; set; }
            public bool HandDryer { get; set; }
            public bool VerifiedStatus { get; set; }
            public string Description { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public string OwnerName { get; set; } = string.Empty;
            public string OwnerEmail { get; set; } = string.Empty;
            public string ImageUrl { get; set; } = string.Empty;
        }

        public class UserViewModel
        {
            public int UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Mobile { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public bool IsActive { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? LastLogin { get; set; }
        }

        public class ReportViewModel
        {
            public int ReportId { get; set; }
            public string IssueType { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime? ResolvedAt { get; set; }
            public string UserName { get; set; } = string.Empty;
            public string UserEmail { get; set; } = string.Empty;
            public string WashroomName { get; set; } = string.Empty;
        }
    }
}