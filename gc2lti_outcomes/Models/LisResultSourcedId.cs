using Newtonsoft.Json;

namespace gc2lti_outcomes.Models
{
    public class LisResultSourcedId
    {
        [JsonProperty("courseId")]
        public string CourseId { get; set; }
        [JsonProperty("courseWorkId")]
        public string CourseWorkId { get; set; }
        [JsonProperty("userId")]
        public string UserId { get; set; }
    }
}
