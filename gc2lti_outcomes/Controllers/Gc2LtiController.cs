using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using gc2lti_outcomes.Data;
using gc2lti_outcomes.Models;
using Google;
using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Auth.OAuth2.AspMvcCore;
using Google.Apis.Classroom.v1;
using Google.Apis.Classroom.v1.Data;
using Google.Apis.Services;
using LtiLibrary.NetCore.Common;
using LtiLibrary.NetCore.Lis.v1;
using LtiLibrary.NetCore.Lti.v1;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace gc2lti_outcomes.Controllers
{
    [Route("[controller]")]
    public class Gc2LtiController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly Gc2LtiDbContext _context;

        public Gc2LtiController(IConfiguration config, Gc2LtiDbContext context)
        {
            _configuration = config;
            _context = context;
        }

        /// <summary>
        /// Converts a simple GET request from Google Classroom into an LTI request
        /// </summary>
        [HttpGet("{nonce?}")]
        public async Task<ActionResult> Index(CancellationToken cancellationToken, string url, string c)
        {
            if (IsRequestFromWebPageThumbnail())
            {
                return View("Thumbnail");
            }

            var clientId = _configuration["Authentication:Google:ClientId"];
            var secret = _configuration["Authentication:Google:ClientSecret"];

            var result = await new AuthorizationCodeMvcApp(this, new AppFlowMetadata(clientId, secret, _context))
                .AuthorizeAsync(cancellationToken)
                .ConfigureAwait(false);

            if (result.Credential == null)
            {
                return Redirect(result.RedirectUri);
            }

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
                    Url = uri,

                    // The ContextId is in the request
                    ContextId = c
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
                    await FillInUserAndPersonInfo(cancellationToken, classroomService, ltiRequest);
                    await FillInContextInfo(cancellationToken, classroomService, ltiRequest);
                    await FillInContextRoleInfo(cancellationToken, classroomService, ltiRequest);
                    await FillInResourceAndOutcomesInfo(cancellationToken, classroomService, ltiRequest);
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
            const string oauthSecret = "secret";

            // Sign the request
            ltiRequest.Signature = ltiRequest.SubstituteCustomVariablesAndGenerateSignature(oauthSecret);

            return View(ltiRequest);
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

        private static async Task FillInContextRoleInfo(CancellationToken cancellationToken, ClassroomService classroomService, 
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

        private static async Task FillInContextInfo(CancellationToken cancellationToken, ClassroomService classroomService, 
            LtiRequest ltiRequest)
        {
            // Fill in the context (course) information
            if (!string.IsNullOrEmpty(ltiRequest.ContextId))
            {
                var courseRequest = classroomService.Courses.Get(ltiRequest.ContextId);
                var course = await courseRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (course != null)
                { 
                    ltiRequest.ContextTitle = course.Name;
                    ltiRequest.ContextType = ContextType.CourseSection;
                }
            }
        }

        private async Task FillInResourceAndOutcomesInfo(CancellationToken cancellationToken, ClassroomService classroomService,
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
                    var courseWork = courseWorkResponse.CourseWork?.FirstOrDefault(w =>
                        w.Materials.FirstOrDefault(m => m.Link.Url.Equals(thisPageUrl)) !=
                        null);
                    if (courseWork != null)
                    {
                        ltiRequest.ResourceLinkId = courseWork.Id;
                        ltiRequest.ResourceLinkTitle = courseWork.Title;
                        ltiRequest.ResourceLinkDescription = courseWork.Description;

                        // If we have the AuthToken and RefreshToken for the user that created the 
                        // CourseWork, then accept outcomes
                        if (courseWork.AssociatedWithDeveloper.HasValue && courseWork.AssociatedWithDeveloper.Value)
                        {
                            var googleUser = await _context.GoogleUsers
                                .FindAsync(new object[] {courseWork.CreatorUserId}, cancellationToken)
                                .ConfigureAwait(false);
                            if (googleUser != null)
                            {
                                ltiRequest.LisOutcomeServiceUrl =
                                    new Uri($"{Request.Scheme}://{Request.Host}/outcomes").ToString();

                                var lisResultSourcedId = new LisResultSourcedId
                                {
                                    CourseId = ltiRequest.ContextId,
                                    CourseWorkId = ltiRequest.ResourceLinkId,
                                    StudentId = ltiRequest.UserId,
                                    TeacherId = courseWork.CreatorUserId
                                };
                                ltiRequest.LisResultSourcedId =
                                    JsonConvert.SerializeObject(lisResultSourcedId, Formatting.None);
                            }
                        }
                    }
                } while (string.IsNullOrEmpty(ltiRequest.ResourceLinkId) &&
                         !string.IsNullOrEmpty(courseWorkResponse.NextPageToken));
            }
        }

        private static async Task FillInUserAndPersonInfo(CancellationToken cancellationToken, ClassroomService classroomService,
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