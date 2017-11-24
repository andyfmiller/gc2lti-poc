using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Auth.OAuth2.AspMvcCore;
using Google.Apis.Classroom.v1;
using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Classroom.v1.Data;
using Google.Apis.Services;
using LtiLibrary.NetCore.Common;
using LtiLibrary.NetCore.Lis.v1;
using LtiLibrary.NetCore.Lti.v1;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace gc2lti.Controllers
{
    [Route("[controller]")]
    public class Gc2LtiController : Controller
    {
        public IConfiguration Configuration { get; set; }

        public Gc2LtiController(IConfiguration config)
        {
            Configuration = config;
        }

        /// <summary>
        /// Converts a simple GET request from Google Classroom into an LTI request
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel an operation.</param>
        /// <param name="url">The LTI request will be POSTed to this URL.</param>
        /// <returns>
        /// Initially returns a <see cref="RedirectResult"/> to authorize Google API usage,
        /// then to POST the LTI request.
        /// </returns>
        [HttpGet("{nonce?}")]
        public async Task<ActionResult> Index(CancellationToken cancellationToken, string url)
        {
            if (!Request.IsHttps)
            {
                return BadRequest("SSL is required.");
            }
            if (IsRequestFromGoogleClassroom())
            {
                AlternateCourseIdForSession = GetAlternateCourseIdFromRequest();
            }
            else if (IsRequestFromWebPageThumbnail())
            {
                return View("Thumbnail");
            }

            var clientId = Configuration["Authentication:Google:ClientId"];
            var secret = Configuration["Authentication:Google:ClientSecret"];

            var result = await new AuthorizationCodeMvcApp(this, new AppFlowMetadata(clientId, secret))
                .AuthorizeAsync(cancellationToken)
                .ConfigureAwait(false);

            if (result.Credential != null)
            {
                // Check paramter
                if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    return BadRequest("Missing tool URL.");
                }

                // Start LTI request
                var ltiRequest =
                    new LtiRequest(LtiConstants.BasicLaunchLtiMessageType)
                    {
                        // Let the tool know this LTI request is coming from Google Classroom
                        ToolConsumerInfoProductFamilyCode = "google-classroom",

                        // Google Classroom always launches links in a new tab/window
                        LaunchPresentationDocumentTarget = DocumentTarget.window,

                        // The LTI request will be posted to the URL in the link
                        Url = uri
                    };

                try
                {
                    // Get information using Google Classroom API
                    using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                    {
                        HttpClientInitializer = result.Credential,
                        ApplicationName = "Google Classroom to LTI Service"
                    }))
                    {
                        await FillIntUserAndPersonInfo(cancellationToken, classroomService, ltiRequest);
                        await FileInResourceInfo(cancellationToken, classroomService, ltiRequest);
                        await FileInRoleInformation(cancellationToken, classroomService, ltiRequest);
                    }

                    // Get information using Google Directory API
                    using (var directoryService = new DirectoryService(new BaseClientService.Initializer
                    {
                        HttpClientInitializer = result.Credential,
                        ApplicationName = "Google Classroom to LTI Service"
                    }))
                    {
                        await FillInPersonSyncInfo(cancellationToken, directoryService, ltiRequest);
                    }
                }
                catch (Exception e)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, e);
                }

                // Based on the data collected from Google, look up the correct key and secret
                ltiRequest.ConsumerKey = "12345";
                var oauthSecret = "secret";

                // Sign the request
                ltiRequest.Signature = ltiRequest.SubstituteCustomVariablesAndGenerateSignature(oauthSecret);

                return View(ltiRequest);
            }
            return Redirect(result.RedirectUri);
        }

        #region Private Methods

        private static async Task FillInPersonSyncInfo(CancellationToken cancellationToken, DirectoryService directoryService,
            LtiRequest ltiRequest)
        {
            var getRequest = directoryService.Users.Get(ltiRequest.UserId);
            getRequest.ViewType = UsersResource.GetRequest.ViewTypeEnum.DomainPublic;
            try
            {
                var user = await getRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (user?.ExternalIds != null)
                {
                    foreach (var externalId in user.ExternalIds)
                    {
                        // This is where Google School Directory Sync stores the SIS ID
                        // https://support.google.com/a/answer/6027781?hl=en
                        if (externalId.Type.Equals("custom") && externalId.CustomType.Equals("person_id"))
                        {
                            ltiRequest.LisPersonSourcedId = externalId.Value;
                        }
                        // This is where Google Admin bulk user update stores the SIS ID
                        // https://support.google.com/a/answer/40057?hl=en
                        else if (externalId.Type.Equals("organization"))
                        {
                            ltiRequest.LisPersonSourcedId = externalId.Value;
                        }
                        // Capture the remaining externalIds (including school membership if
                        // using Google School Directory Sync) as custom parameters
                        else if (externalId.Type.Equals("custom"))
                        {
                            ltiRequest.AddCustomParameter(externalId.CustomType, externalId.Value);
                        }
                        else
                        {
                            ltiRequest.AddCustomParameter(externalId.Type, externalId.Value);
                        }
                    }
                }
            }
            catch (GoogleApiException ex) when (ex.Error.Code == 404)
            {
                // Swallow this "Not Found" error. This happens if the user
                // is not part of a G Suite.
            }
        }

        private static async Task FileInRoleInformation(CancellationToken cancellationToken, ClassroomService classroomService, 
            LtiRequest ltiRequest)
        {
            // Fill in the role information
            if (!string.IsNullOrEmpty(ltiRequest.ContextId) && !string.IsNullOrEmpty(ltiRequest.UserId))
            {
                try
                {
                    await classroomService.Courses.Teachers.Get(ltiRequest.ContextId, ltiRequest.UserId)
                        .ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    ltiRequest.SetRoles(new List<Role> { Role.Instructor });
                }
                catch (GoogleApiException ex) when (ex.Error.Code == 404)
                {
                    ltiRequest.SetRoles(new List<Role> { Role.Learner });
                }
            }
        }

        private async Task FileInResourceInfo(CancellationToken cancellationToken, ClassroomService classroomService, 
            LtiRequest ltiRequest)
        {
            // Fill in the resource (courseWork) information
            if (!string.IsNullOrEmpty(ltiRequest.ContextId))
            {
                var courseWorkRequest = classroomService.Courses.CourseWork.List(ltiRequest.ContextId);
                ListCourseWorkResponse courseWorkResponse = null;
                var thisPageUrl = Request.GetDisplayUrl();
                do
                {
                    if (courseWorkResponse != null)
                    {
                        courseWorkRequest.PageToken = courseWorkResponse.NextPageToken;
                    }

                    courseWorkResponse = await courseWorkRequest.ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var courseWorkItem = courseWorkResponse.CourseWork?.FirstOrDefault(w =>
                        w.Materials.FirstOrDefault(m => m.Link.Url.Equals(thisPageUrl)) !=
                        null);
                    if (courseWorkItem != null)
                    {
                        ltiRequest.ResourceLinkId = courseWorkItem.Id;
                        ltiRequest.ResourceLinkTitle = courseWorkItem.Title;
                        ltiRequest.ResourceLinkDescription = courseWorkItem.Description;
                    }
                } while (string.IsNullOrEmpty(ltiRequest.ResourceLinkId) &&
                         !string.IsNullOrEmpty(courseWorkResponse.NextPageToken));
            }
        }

        private async Task FillIntUserAndPersonInfo(CancellationToken cancellationToken, ClassroomService classroomService,
            LtiRequest ltiRequest)
        {
            // Fill in basic user and person information
            var profile = await classroomService.UserProfiles.Get("me").ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            ltiRequest.UserId = profile.Id;
            ltiRequest.LisPersonEmailPrimary = profile.EmailAddress;
            ltiRequest.LisPersonNameFamily = profile.Name.FamilyName;
            ltiRequest.LisPersonNameFull = profile.Name.FullName;
            ltiRequest.LisPersonNameGiven = profile.Name.GivenName;
            ltiRequest.UserImage = profile.PhotoUrl;

            // Fill in the context (course) information
            if (AlternateCourseIdForSession != null)
            {
                var coursesRequest = classroomService.Courses.List();
                coursesRequest.CourseStates = CoursesResource.ListRequest.CourseStatesEnum.ACTIVE;
                ListCoursesResponse coursesResponse = null;
                do
                {
                    if (coursesResponse != null)
                    {
                        coursesRequest.PageToken = coursesResponse.NextPageToken;
                    }

                    coursesResponse = await coursesRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    if (coursesResponse.Courses != null)
                    {
                        foreach (var course in coursesResponse.Courses)
                        {
                            if (Uri.TryCreate(course.AlternateLink, UriKind.Absolute,
                                out var alternateLink))
                            {
                                var alternateCourseIdFromList =
                                    alternateLink.Segments[alternateLink.Segments.Length - 1];
                                if (alternateCourseIdFromList.Equals(AlternateCourseIdForSession))
                                {
                                    ltiRequest.ContextId = course.Id;
                                    ltiRequest.ContextTitle = course.Name;
                                    ltiRequest.ContextType = ContextType.CourseSection;
                                    break;
                                }
                            }
                        }
                    }
                } while (string.IsNullOrEmpty(ltiRequest.ContextId) && !string.IsNullOrEmpty(coursesResponse.NextPageToken));
            }
        }

        private string AlternateCourseIdForSession
        {
            get { return TempData["AlternateCourseId"]?.ToString(); }
            set { TempData["AlternateCourseId"] = value; }
        }

        private string GetAlternateCourseIdFromRequest()
        {
            var referer = Request.Headers["Referer"];
            var refererUri = new Uri(referer, UriKind.Absolute);
            return refererUri.Segments[refererUri.Segments.Length - 1];
        }

        private bool IsRequestFromGoogleClassroom()
        {
            if (!Request.Headers.TryGetValue("Referer", out var referer))
            {
                return false;
            }
            if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                return false;
            }
            return refererUri.Host.Equals("classroom.google.com");
        }

        private bool IsRequestFromWebPageThumbnail()
        {
            if (!Request.Headers.TryGetValue("Referer", out var referer))
            {
                return false;
            }
            if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                return false;
            }
            return refererUri.Host.Equals("www.google.com")
                && refererUri.Segments.Length > 1
                && refererUri.Segments[1].Equals("webpagethumbnail?");
        }

        #endregion
    }
}