using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;

public class ShareAssignModel
{
    public IDictionary<string, string> Courses { get; }
    public string Url { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }

    public SelectList CoursesList
    {
        get
        {
            return new SelectList(
                Courses.Select(c => new { Value = c.Key, Text = c.Value }),
                "Value",
                "Text"
            );
        }
    }

    public ShareAssignModel()
    {
        Courses = new Dictionary<string, string>();
    }
}