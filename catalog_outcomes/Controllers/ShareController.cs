using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using catalog_outcomes.Models;
using gc2lti_shared.Data;
using gc2lti_shared.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.AspMvcCore;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2.Web;
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
        private readonly Gc2LtiDbContext _db;

        private string ClientId
        {
            get { return _configuration["Authentication:Google:ClientId"]; }
        }

        private string ClientSecret
        {
            get {  return _configuration["Authentication:Google:ClientSecret"]; }
        }

        public ShareController(IConfiguration config, Gc2LtiDbContext db)
        {
            _configuration = config;
            _db = db;
        }

        /// <summary>
        /// 1. Collect the courseId
        /// </summary>
        public async Task<IActionResult> Index(CancellationToken cancellationToken, string url, string title, string description)
        {
            var result = await new AuthorizationCodeMvcApp(this, new AppFlowMetadata(ClientId, ClientSecret, _db))
                .AuthorizeAsync(cancellationToken)
                .ConfigureAwait(false);

            if (result.Credential == null)
            {
                return Redirect(result.RedirectUri);
            }

            var model = new ShareAssignModel
            {
                Url = url,
                Title = title,
                Description = description
            };

            try
            {
                // List the teacher's courses
                using (var classroomService = new ClassroomService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = result.Credential,
                    ApplicationName = "Google Classroom to LTI Service"
                }))
                {
                    if (!await SaveOfflineToken(cancellationToken, classroomService, result))
                    {
                        return RedirectToAction("Index", model);
                    }

                    // Fill in the view model with a list of my courses
                    var coursesRequest = classroomService.Courses.List();
                    coursesRequest.CourseStates = CoursesResource.ListRequest.CourseStatesEnum.ACTIVE;
                    coursesRequest.TeacherId = "me";
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
            catch (GoogleApiException e) when (e.Message.Contains("invalid authentication credentials"))
            {
                // Force a new UserId
                TempData.Remove("user");
                return RedirectToAction("Index", model);
            }
            catch (TokenResponseException e) when (e.Message.Contains("invalid_grant"))
            {
                // Force a new UserId
                TempData.Remove("user");
                return RedirectToAction("Index", model);
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// If the Google User was prompted to agree to the permissions, then
        /// the TokenResponse will include a RefreshToken which is suitable for
        /// offline use. This is the TokenResponse to use for sending grades back.
        /// Keep track of the UserId so we can look up the TokenResponse later.
        /// </summary>
        private async Task<bool> SaveOfflineToken(CancellationToken cancellationToken, ClassroomService classroomService, AuthorizationCodeWebApp.AuthResult result)
        {
            var userProfileRequest = classroomService.UserProfiles.Get("me");
            var userProfile =
                await userProfileRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var googleUser = await _db.GoogleUsers
                .FindAsync(new object[] {userProfile.Id}, cancellationToken).ConfigureAwait(false);

            // If there is no matching GoogleUser, then we need to make one
            if (googleUser == null)
            {
                // If we don't have an offline Token, force a login and acceptance
                if (string.IsNullOrEmpty(result.Credential.Token.RefreshToken))
                {
                    await result.Credential.RevokeTokenAsync(cancellationToken).ConfigureAwait(false);
                    return false;
                }

                // Otherwise record a reference to the token
                googleUser = new GoogleUser
                {
                    GoogleId = userProfile.Id,
                    UserId = result.Credential.UserId
                };
                await _db.AddAsync(googleUser, cancellationToken).ConfigureAwait(false);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            // If there is a matching GoogleUser with a new Token, record it
            else if (!googleUser.UserId.Equals(result.Credential.UserId,
                StringComparison.InvariantCultureIgnoreCase))
            {
                googleUser.UserId = result.Credential.UserId;
                _db.Update(googleUser);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        /// <summary>
        /// 2. Confirm the title, description, tool url, and max points
        /// </summary>
        public async Task<IActionResult> Confirm(CancellationToken cancellationToken, 
            string courseId, string url, string title, string description)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, _db);
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
                        MaxPoints = 100,
                        Title = title,
                        Url = url,
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
        /// 3. Create the assignment
        /// </summary>
        public async Task<IActionResult> Assign(CancellationToken cancellationToken, 
            string courseId, string url, string title, string description, int maxPoints)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, _db);
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
                    var linkUrl = IsRequestLocal()
                            ? $"{_configuration["Localhost"]}/gc2lti/{nonce}?url={url}&c={courseId}"
                            : $"{_configuration["Remotehost"]}/gc2lti/{nonce}?url={url}&c={courseId}";
                    var courseWork = new CourseWork
                    {
                        Title = title,
                        Description = description,
                        MaxPoints = maxPoints,
                        WorkType = "ASSIGNMENT",
                        Materials = new List<Material>()
                        {
                            new Material()
                            {
                                Link = new Link()
                                {
                                    Title = title,
                                    Url = linkUrl
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

        /// <summary>
        /// 4. Display results with link to view the course
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="courseId"></param>
        /// <param name="courseWorkId"></param>
        /// <returns></returns>
        public async Task<IActionResult> View(CancellationToken cancellationToken, string courseId, string courseWorkId)
        {
            var appFlow = new AppFlowMetadata(ClientId, ClientSecret, _db);
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

        private bool IsRequestLocal()
        {
            return Request.HttpContext.Connection.RemoteIpAddress == null
                   || Request.HttpContext.Connection.LocalIpAddress == null
                   || Request.HttpContext.Connection.RemoteIpAddress.Equals(Request.HttpContext.Connection.LocalIpAddress)
                   || IPAddress.IsLoopback(Request.HttpContext.Connection.RemoteIpAddress);
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
    }
}