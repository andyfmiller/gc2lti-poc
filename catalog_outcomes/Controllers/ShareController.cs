using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using catalog_outcomes.Models;
using gc2lti_shared.Data;
using gc2lti_shared.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.AspMvcCore;
using Google.Apis.Classroom.v1;
using Google.Apis.Classroom.v1.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace catalog_outcomes.Controllers
{
    public class ShareController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly Gc2LtiDbContext _context;

        private string ClientId
        {
            get { return _configuration["Authentication:Google:ClientId"]; }
        }

        private string ClientSecret
        {
            get {  return _configuration["Authentication:Google:ClientSecret"]; }
        }

        public ShareController(IConfiguration config, Gc2LtiDbContext context)
        {
            _configuration = config;
            _context = context;
        }

        /// <summary>
        /// Collect the courseId
        /// </summary>
        public async Task<IActionResult> Index(CancellationToken cancellationToken, string url, string title, string description)
        {
            var result = await new AuthorizationCodeMvcApp(this, new AppFlowMetadata(ClientId, ClientSecret, _context))
                .AuthorizeAsync(cancellationToken)
                .ConfigureAwait(false);

            if (result.Credential == null)
            {
                return Redirect(result.RedirectUri);
            }

            try
            {
                // List the teacher's courses
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = result.Credential,
                    ApplicationName = "Google Classroom to LTI Service"
                }))
                {
                    // If the Google User was prompted to agree to the permissions, then
                    // the TokenResponse will include a RefreshToken which is suitable for
                    // offline use. This is the TokenResponse to use for sending grades back.
                    // Keep track of the UserId so we can look up the TokenResponse later.

                    if (result.Credential.Token.RefreshToken != null)
                    {
                        var userProfileRequest = classroomService.UserProfiles.Get("me");
                        var userProfile =
                            await userProfileRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                        var googleUser = await _context.GoogleUsers
                            .FindAsync(new object[] { userProfile.Id }, cancellationToken).ConfigureAwait(false);
                        if (googleUser == null)
                        {
                            googleUser = new GoogleUser
                            {
                                GoogleId = userProfile.Id,
                                UserId = result.Credential.UserId
                            };
                            await _context.AddAsync(googleUser, cancellationToken).ConfigureAwait(false);
                            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        }
                        else if (!googleUser.UserId.Equals(result.Credential.UserId, StringComparison.InvariantCultureIgnoreCase))
                        {
                            googleUser.UserId = result.Credential.UserId;
                            _context.Update(googleUser);
                            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }

                    var model = new ShareAssignModel
                    {
                        Url = url,
                        Title = title,
                        Description = description
                    };

                    var coursesRequest = classroomService.Courses.List();
                    coursesRequest.CourseStates = CoursesResource.ListRequest.CourseStatesEnum.ACTIVE;
                    ListCoursesResponse coursesResponse = null;
                    do
                    {
                        if (coursesResponse != null)
                        {
                            coursesRequest.PageToken = coursesResponse.NextPageToken;
                        }

                        coursesResponse =
                            await coursesRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                        if (coursesResponse.Courses != null)
                        {
                            foreach (var course in coursesResponse.Courses)
                            {
                                model.Courses.Add(course.Id, course.Name);
                            }
                        }
                    } while (!string.IsNullOrEmpty(coursesResponse.NextPageToken));

                    return View(model);
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// Confirm the title and instructions.
        /// </summary>
        public async Task<IActionResult> Confirm(CancellationToken cancellationToken, 
            string courseId, string url, string title, string description)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, _context);
            var token = await appFlow.Flow.LoadTokenAsync(appFlow.GetUserId(this), cancellationToken);
            var credential = new UserCredential(appFlow.Flow, appFlow.GetUserId(this), token);

            try
            {

                // Get the course name
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential, //result.Credential,
                    ApplicationName = "Google Classroom to LTI Service"
                }))
                {
                    var request = classroomService.Courses.Get(courseId);
                    var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                    var model = new ShareConfirmModel
                    {
                        CourseId = courseId,
                        CourseName = response.Name,
                        Description = description,
                        Url = url,
                        Title = title
                    };

                    return View(model);
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// Assign the item
        /// </summary>
        public async Task<IActionResult> Assign(CancellationToken cancellationToken, 
            string courseId, string url, string title, string description)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, _context);
            var token = await appFlow.Flow.LoadTokenAsync(appFlow.GetUserId(this), cancellationToken);
            var credential = new UserCredential(appFlow.Flow, appFlow.GetUserId(this), token);

            try
            {
                // Create the assignment
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Google Classroom to LTI Service"
                }))
                {
                    var nonce = CalculateNonce(8);
                    var linkUrl = new UriBuilder($"https://localhost:44319/gc2lti/{nonce}?url={url}&c={courseId}");
                    var courseWork = new CourseWork
                    {
                        Title = title,
                        Description = description,
                        MaxPoints = 100,
                        WorkType = "ASSIGNMENT",
                        Materials = new List<Material>()
                        {
                            new Material()
                            {
                                Link = new Link()
                                {
                                    Title = title,
                                    Url = linkUrl.Uri.AbsoluteUri
                                }
                            }
                        },
                        State = "PUBLISHED"
                    };

                    var createRequest = classroomService.Courses.CourseWork.Create(courseWork, courseId);
                    courseWork = await createRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                    return RedirectToAction("View", new { courseId, courseWorkId = courseWork.Id });
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        // Return a nonce to differentiate assignments with the same URL within a course
        private string CalculateNonce(int length)
        {
            const string allowableCharacters = "abcdefghijklmnopqrstuvwxyz0123456789";
            var bytes = new byte[length];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return new string(bytes.Select(x => allowableCharacters[x % allowableCharacters.Length]).ToArray());
        }

        public async Task<IActionResult> View(CancellationToken cancellationToken, string courseId, string courseWorkId)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, _context);
            var token = await appFlow.Flow.LoadTokenAsync(appFlow.GetUserId(this), cancellationToken);
            var credential = new UserCredential(appFlow.Flow, appFlow.GetUserId(this), token);

            try
            {
                // Get information using Google Classroom API
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Google Classroom to LTI Service"
                }))
                {
                    var request = classroomService.Courses.Get(courseId);
                    var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                    var model = new ShareViewModel
                    {
                        CourseId = courseId,
                        CourseWorkId = courseWorkId,
                        CourseAlternativeLink = response.AlternateLink
                    };

                    return View(model);
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }
    }
}