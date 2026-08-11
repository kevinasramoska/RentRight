using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace RentRight.Models
{
    public class ApplicationUser : IdentityUser
    {
        [StringLength(200)]
        public string FullName { get; set; } = string.Empty;

        public ICollection<Property> PropertiesOwned { get; set; } = [];
        public ICollection<Tenancy> Tenancies { get; set; } = [];
    }
}
