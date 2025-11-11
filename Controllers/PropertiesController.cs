using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentRight.Data;
using RentRight.Models;
using Microsoft.AspNetCore.Identity;

namespace RentRight.Controllers
{
    [Authorize]
    public class PropertiesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public PropertiesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Properties
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            var properties = await _context.Properties.ToListAsync();
            return View(properties);
        }

        // GET: Properties/Details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {   
            if (id == null) return NotFound();

            var property = await _context.Properties
                .FirstOrDefaultAsync(m => m.Id == id);

            if (property == null) return NotFound();

            return View(property);
        }

        // GET: Properties/Create
        [Authorize(Roles = "Landlord,Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Properties/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Landlord,Admin")]
        public async Task<IActionResult> Create(Property property)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.GetUserAsync(User);
                property.LandlordId = user!.Id;

                _context.Add(property);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(property);
        }

        // GET: Properties/Edit/5
        [Authorize(Roles = "Landlord,Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var property = await _context.Properties.FindAsync(id);
            if (property == null) return NotFound();

            // security: only allow editing own properties if landlord
            var user = await _userManager.GetUserAsync(User);
            if (User.IsInRole("Landlord") && property.LandlordId != user!.Id)
                return Unauthorized();

            return View(property);
        }

        // POST: Properties/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Landlord,Admin")]
        public async Task<IActionResult> Edit(int id, Property property)
        {
            if (id != property.Id) return NotFound();

            // ensure we don’t lose LandlordId on update
            var existing = await _context.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (existing == null) return NotFound();
            property.LandlordId = existing.LandlordId;

            if (ModelState.IsValid)
            {
                _context.Update(property);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(property);
        }

        // GET: Properties/Delete/5
        [Authorize(Roles = "Landlord,Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var property = await _context.Properties.FirstOrDefaultAsync(p => p.Id == id);
            if (property == null) return NotFound();

            return View(property);
        }

        // POST: Properties/DeleteConfirmed/5
        [HttpPost, ActionName("DeleteConfirmed")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Landlord,Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var property = await _context.Properties.FindAsync(id);
            if (property != null)
            {
                _context.Properties.Remove(property);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
