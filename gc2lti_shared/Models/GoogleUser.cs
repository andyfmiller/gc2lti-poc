using System.ComponentModel.DataAnnotations;

namespace gc2lti_shared.Models
{
    public class GoogleUser
    {
        [Key]
        [MaxLength(22)]
        public string GoogleId { get; set; }
        [MaxLength(36)]
        public string UserId { get; set; }
    }
}
