using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;

namespace RentRight.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;

        // Inject ApplicationDbContext so we can access the database
        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        // Homepage - show latest properties
        public async Task<IActionResult> Index()
        {
            var latestProperties = await _context.Properties
                .Include(p => p.Landlord)
                .OrderByDescending(p => p.Id)
                .Take(6)
                .ToListAsync();

            return View(latestProperties);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}