using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Npgsql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SE1.Controllers
{
    public class WashroomController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly string _imagePath;

        public WashroomController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _imagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "washrooms");
        }

        
        public IActionResult Register()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                TempData["Error"] = "Please login first to register a washroom.";
                return RedirectToAction("Login", "Home");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            return View();
        }

     
        [HttpPost]
        public async Task<IActionResult> Register(
            string name,
            string address,
            string openingHours,
            decimal usageCost,
            decimal hygieneScore,
            string securityStatus,
            string crowdLevel,
            bool bathAvailability,
            bool babyFacility,
            bool wheelchairAccess,
            bool handwash,
            bool handDryer,
            string description,
            string latitude,
            string longitude,
            IFormFile washroomImage)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(address) ||
                    string.IsNullOrEmpty(openingHours) || string.IsNullOrEmpty(securityStatus) ||
                    string.IsNullOrEmpty(crowdLevel))
                {
                    ViewBag.Error = "All required fields must be filled.";
                    return View();
                }

                if (washroomImage == null)
                {
                    ViewBag.Error = "Please upload an image of your washroom.";
                    return View();
                }

                string extension = Path.GetExtension(washroomImage.FileName).ToLower();
                if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
                {
                    ViewBag.Error = "Only JPG, JPEG, and PNG images are allowed.";
                    return View();
                }

                if (washroomImage.Length > 5 * 1024 * 1024)
                {
                    ViewBag.Error = "File size should be less than 5MB.";
                    return View();
                }

                int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");
                int washroomId = 0;

                
                decimal lat = 0, lng = 0;
                decimal.TryParse(latitude, out lat);
                decimal.TryParse(longitude, out lng);

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string insertQuery = @"
                        INSERT INTO washrooms (
                            name, address, opening_hours, usage_cost, hygiene_score,
                            security_status, crowd_level, bath_availability, 
                            baby_facility, wheelchair_access, handwash, hand_dryer,
                            latitude, longitude,
                            verified_status, description, created_by, created_at
                        ) VALUES (
                            @name, @address, @openingHours, @usageCost, @hygieneScore,
                            @securityStatus, @crowdLevel, @bathAvailability,
                            @babyFacility, @wheelchairAccess, @handwash, @handDryer,
                            @latitude, @longitude,
                            false, @description, @createdBy, CURRENT_TIMESTAMP
                        ) RETURNING washroom_id";

                    using (var cmd = new NpgsqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.Parameters.AddWithValue("@address", address);
                        cmd.Parameters.AddWithValue("@openingHours", openingHours);
                        cmd.Parameters.AddWithValue("@usageCost", usageCost);
                        cmd.Parameters.AddWithValue("@hygieneScore", hygieneScore);
                        cmd.Parameters.AddWithValue("@securityStatus", securityStatus);
                        cmd.Parameters.AddWithValue("@crowdLevel", crowdLevel);
                        cmd.Parameters.AddWithValue("@bathAvailability", bathAvailability);
                        cmd.Parameters.AddWithValue("@babyFacility", babyFacility);
                        cmd.Parameters.AddWithValue("@wheelchairAccess", wheelchairAccess);
                        cmd.Parameters.AddWithValue("@handwash", handwash);
                        cmd.Parameters.AddWithValue("@handDryer", handDryer);
                        cmd.Parameters.AddWithValue("@latitude", lat);
                        cmd.Parameters.AddWithValue("@longitude", lng);
                        cmd.Parameters.AddWithValue("@description", string.IsNullOrEmpty(description) ? "" : description);
                        cmd.Parameters.AddWithValue("@createdBy", userId);

                        var result = cmd.ExecuteScalar();
                        washroomId = result != null ? Convert.ToInt32(result) : 0;
                    }
                }

                if (washroomId > 0 && washroomImage != null)
                {
                    if (!Directory.Exists(_imagePath))
                    {
                        Directory.CreateDirectory(_imagePath);
                    }

                    string fileName = $"{washroomId}_{Guid.NewGuid()}{extension}";
                    string filePath = Path.Combine(_imagePath, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await washroomImage.CopyToAsync(stream);
                    }

                    using (var conn = new NpgsqlConnection(_connectionString))
                    {
                        conn.Open();

                        string imgQuery = @"
                            INSERT INTO washroom_images (washroom_id, image_url, created_at) 
                            VALUES (@washroomId, @imageUrl, CURRENT_TIMESTAMP)";

                        using (var cmd = new NpgsqlCommand(imgQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@washroomId", washroomId);
                            cmd.Parameters.AddWithValue("@imageUrl", "/images/washrooms/" + fileName);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                TempData["Success"] = "✅ Washroom registered successfully! It will be reviewed by admin.";
                return RedirectToAction("MyWashrooms");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "An error occurred: " + ex.Message;
                return View();
            }
        }

        
        public IActionResult MyWashrooms()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");
            var washrooms = new List<Washroom>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"
                    SELECT w.washroom_id, w.name, w.address, w.opening_hours, w.usage_cost, 
                           w.hygiene_score, w.security_status, w.crowd_level, 
                           w.bath_availability, w.baby_facility, w.wheelchair_access,
                           w.handwash, w.hand_dryer,
                           w.latitude, w.longitude,
                           w.verified_status, w.description, w.created_at,
                           (SELECT image_url FROM washroom_images WHERE washroom_id = w.washroom_id LIMIT 1) AS image_url
                    FROM washrooms w
                    WHERE w.created_by = @userId 
                    ORDER BY w.created_at DESC";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            washrooms.Add(new Washroom
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
                                Latitude = reader["latitude"] != DBNull.Value ? Convert.ToDecimal(reader["latitude"]) : 0,
                                Longitude = reader["longitude"] != DBNull.Value ? Convert.ToDecimal(reader["longitude"]) : 0,
                                VerifiedStatus = Convert.ToBoolean(reader["verified_status"]),
                                Description = reader["description"]?.ToString() ?? "",
                                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                                ImageUrl = reader["image_url"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }

            return View(washrooms);
        }

       
        [HttpPost]
        public IActionResult Delete(int id)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                return Json(new { success = false, message = "Please login first" });
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string checkQuery = "SELECT COUNT(*) FROM washrooms WHERE washroom_id = @id AND created_by = @userId";
                    using (var checkCmd = new NpgsqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@id", id);
                        checkCmd.Parameters.AddWithValue("@userId", userId);

                        long count = (long)checkCmd.ExecuteScalar();
                        if (count == 0)
                        {
                            return Json(new { success = false, message = "Washroom not found or unauthorized." });
                        }
                    }

                    string deleteQuery = "DELETE FROM washrooms WHERE washroom_id = @id AND created_by = @userId";
                    using (var cmd = new NpgsqlCommand(deleteQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@userId", userId);

                        cmd.ExecuteNonQuery();

                        return Json(new { success = true, message = "Washroom deleted successfully!" });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        
        public IActionResult Edit(int id)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                return RedirectToAction("Login", "Home");
            }

            int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");
            Washroom washroom = null;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                string query = @"
                    SELECT w.washroom_id, w.name, w.address, w.opening_hours, w.usage_cost,
                           w.hygiene_score, w.security_status, w.crowd_level,
                           w.bath_availability, w.baby_facility, w.wheelchair_access,
                           w.handwash, w.hand_dryer,
                           w.latitude, w.longitude,
                           w.verified_status, w.description,
                           (SELECT image_url FROM washroom_images WHERE washroom_id = w.washroom_id LIMIT 1) AS image_url
                    FROM washrooms w
                    WHERE w.washroom_id = @id AND w.created_by = @userId";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@userId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            washroom = new Washroom
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
                                Latitude = reader["latitude"] != DBNull.Value ? Convert.ToDecimal(reader["latitude"]) : 0,
                                Longitude = reader["longitude"] != DBNull.Value ? Convert.ToDecimal(reader["longitude"]) : 0,
                                VerifiedStatus = Convert.ToBoolean(reader["verified_status"]),
                                Description = reader["description"]?.ToString() ?? "",
                                ImageUrl = reader["image_url"]?.ToString() ?? ""
                            };
                        }
                    }
                }
            }

            if (washroom == null)
            {
                TempData["Error"] = "Washroom not found or unauthorized.";
                return RedirectToAction("MyWashrooms");
            }

            return View(washroom);
        }

        
        [HttpPost]
        public async Task<IActionResult> Edit(
            int id,
            string name,
            string address,
            string openingHours,
            decimal usageCost,
            decimal hygieneScore,
            string securityStatus,
            string crowdLevel,
            bool bathAvailability,
            bool babyFacility,
            bool wheelchairAccess,
            bool handwash,
            bool handDryer,
            string description,
            string latitude,
            string longitude,
            IFormFile washroomImage)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(address) ||
                    string.IsNullOrEmpty(openingHours) || string.IsNullOrEmpty(securityStatus) ||
                    string.IsNullOrEmpty(crowdLevel))
                {
                    ViewBag.Error = "All required fields must be filled.";
                    return View();
                }

                int userId = Convert.ToInt32(HttpContext.Session.GetString("UserId") ?? "0");

                
                decimal lat = 0, lng = 0;
                decimal.TryParse(latitude, out lat);
                decimal.TryParse(longitude, out lng);

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string checkQuery = "SELECT COUNT(*) FROM washrooms WHERE washroom_id = @id AND created_by = @userId";
                    using (var checkCmd = new NpgsqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@id", id);
                        checkCmd.Parameters.AddWithValue("@userId", userId);

                        long count = (long)checkCmd.ExecuteScalar();
                        if (count == 0)
                        {
                            ViewBag.Error = "Washroom not found or unauthorized.";
                            return View();
                        }
                    }

                    string updateQuery = @"
                        UPDATE washrooms SET
                            name = @name,
                            address = @address,
                            opening_hours = @openingHours,
                            usage_cost = @usageCost,
                            hygiene_score = @hygieneScore,
                            security_status = @securityStatus,
                            crowd_level = @crowdLevel,
                            bath_availability = @bathAvailability,
                            baby_facility = @babyFacility,
                            wheelchair_access = @wheelchairAccess,
                            handwash = @handwash,
                            hand_dryer = @handDryer,
                            latitude = @latitude,
                            longitude = @longitude,
                            description = @description,
                            updated_at = CURRENT_TIMESTAMP
                        WHERE washroom_id = @id AND created_by = @userId";

                    using (var cmd = new NpgsqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.Parameters.AddWithValue("@address", address);
                        cmd.Parameters.AddWithValue("@openingHours", openingHours);
                        cmd.Parameters.AddWithValue("@usageCost", usageCost);
                        cmd.Parameters.AddWithValue("@hygieneScore", hygieneScore);
                        cmd.Parameters.AddWithValue("@securityStatus", securityStatus);
                        cmd.Parameters.AddWithValue("@crowdLevel", crowdLevel);
                        cmd.Parameters.AddWithValue("@bathAvailability", bathAvailability);
                        cmd.Parameters.AddWithValue("@babyFacility", babyFacility);
                        cmd.Parameters.AddWithValue("@wheelchairAccess", wheelchairAccess);
                        cmd.Parameters.AddWithValue("@handwash", handwash);
                        cmd.Parameters.AddWithValue("@handDryer", handDryer);
                        cmd.Parameters.AddWithValue("@latitude", lat);
                        cmd.Parameters.AddWithValue("@longitude", lng);
                        cmd.Parameters.AddWithValue("@description", string.IsNullOrEmpty(description) ? "" : description);

                        cmd.ExecuteNonQuery();
                    }

                    
                    if (washroomImage != null && washroomImage.Length > 0)
                    {
                        string extension = Path.GetExtension(washroomImage.FileName).ToLower();
                        if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
                        {
                            ViewBag.Error = "Only JPG, JPEG, and PNG images are allowed.";
                            return View();
                        }

                        if (washroomImage.Length > 5 * 1024 * 1024)
                        {
                            ViewBag.Error = "File size should be less than 5MB.";
                            return View();
                        }

                        string getOldImageQuery = "SELECT image_url FROM washroom_images WHERE washroom_id = @id";
                        using (var cmd = new NpgsqlCommand(getOldImageQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            var oldImage = cmd.ExecuteScalar()?.ToString();
                            if (!string.IsNullOrEmpty(oldImage))
                            {
                                string oldFilePath = Path.Combine(_imagePath, Path.GetFileName(oldImage));
                                if (System.IO.File.Exists(oldFilePath))
                                {
                                    System.IO.File.Delete(oldFilePath);
                                }
                            }
                        }

                        string deleteImageQuery = "DELETE FROM washroom_images WHERE washroom_id = @id";
                        using (var cmd = new NpgsqlCommand(deleteImageQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.ExecuteNonQuery();
                        }

                        if (!Directory.Exists(_imagePath))
                        {
                            Directory.CreateDirectory(_imagePath);
                        }

                        string fileName = $"{id}_{Guid.NewGuid()}{extension}";
                        string filePath = Path.Combine(_imagePath, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await washroomImage.CopyToAsync(stream);
                        }

                        string imgQuery = @"
                            INSERT INTO washroom_images (washroom_id, image_url, created_at) 
                            VALUES (@washroomId, @imageUrl, CURRENT_TIMESTAMP)";

                        using (var cmd = new NpgsqlCommand(imgQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@washroomId", id);
                            cmd.Parameters.AddWithValue("@imageUrl", "/images/washrooms/" + fileName);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                TempData["Success"] = "✅ Washroom updated successfully!";
                return RedirectToAction("MyWashrooms");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "An error occurred: " + ex.Message;
                return View();
            }
        }

        
        public class Washroom
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
            public decimal Latitude { get; set; }     
            public decimal Longitude { get; set; }  
            public bool VerifiedStatus { get; set; }
            public string Description { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public string ImageUrl { get; set; } = string.Empty;
        }
    }
}