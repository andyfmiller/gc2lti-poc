using System;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Catalog.Pages
{
    public class ResourceModel : PageModel
    {
        public string ShareUrl { get; set; }
        public void OnGet()
        {
            
            ShareUrl =
                // There are 3 parts to the URL that will be shared to classroom:
                //
                // 1) Service that converts a Google Classroom request into an LTI request

                "https://localhost:44319/gc2lti/"

                // 2) Add a nonce to the URL so that tools can be assigned more than once
                //    and gc2lti can trace back to the appropriate CourseWork. Note that this
                //    nonce is added before the Share to Classroom button is rendered and
                //    I could not find a way to change it each time the button is clicked.
                //    That means if this URL is shared more than once to the same course without refreshing
                //    the page, there will be two CourseWork items with identical URLs, and
                //    gc2lti will not be able to distinguish them when the URL is clicked. 
                //    In that case the most recent CourseWork item with the matching URL will be used.

                + Guid.NewGuid().ToString("N")

                // 3) The URL of the LTI tool. This is where the LTI request will be posted.

                + "?url=http://lti.tools/test/tp.php";
        }
    }
}