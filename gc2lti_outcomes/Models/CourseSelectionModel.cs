using Microsoft.AspNetCore.Mvc.Rendering;

namespace gc2lti_outcomes.Models
{
    public class CourseSelectionModel : ResourceModel
    {
        public SelectList Courses { get; set; }
        public string PersonName { get; set; }
        public string UserId { get; set; }
    }
}